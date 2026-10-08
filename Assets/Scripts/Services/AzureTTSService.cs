using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Newtonsoft.Json.Linq;

public sealed class AzureTTSService : ITTSService
{
    public event Action<byte[]> AudioChunkReceived;
    public event Action<string> Error;
    public event Action Completed;
    public int OutputSampleRate => 24000;
    public string Diagnostics { get; private set; }
    private static readonly HttpClient Client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly object CacheGate = new object();
    private static readonly Dictionary<string, Tuple<DateTime, JArray>> VoiceCache = new Dictionary<string, Tuple<DateTime, JArray>>();
    private readonly object gate = new object();
    private readonly Queue<string> textQueue = new Queue<string>();
    private readonly AzureTTSSettings config;
    private readonly HttpClient client;
    private readonly Uri testEndpoint;
    private CancellationTokenSource cancellation;
    private Task worker;
    private bool inputCompleted, disposed;
    private int pendingCharacters;

    public AzureTTSService(AzureTTSConfig asset) : this(asset != null ? asset.settings : null) { }
    // Injectable transport permits offline validation without touching a subscription.
    public AzureTTSService(AzureTTSSettings settings, HttpClient transport = null, Uri endpoint = null)
    {
        config = settings?.Copy(); client = transport ?? Client; testEndpoint = endpoint;
    }
    public Task StartAsync(CancellationToken token)
    {
        lock (gate)
        {
            if (disposed || worker != null) throw new InvalidOperationException("Azure TTS 会话不可重复启动。");
            if (config == null) throw new InvalidOperationException("未配置 Azure TTS。");
            config.Validate();
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            worker = Task.Run(() => RunAsync(cancellation.Token));
            return Task.CompletedTask;
        }
    }
    public void EnqueueText(string text)
    {
        lock (gate)
        {
            if (disposed || inputCompleted || string.IsNullOrEmpty(text)) return;
            if (pendingCharacters + text.Length > 65536) throw new InvalidOperationException("待合成文本超过上限。");
            textQueue.Enqueue(text); pendingCharacters += text.Length;
        }
    }
    public void CompleteInput() { lock (gate) inputCompleted = true; }
    public Task WaitForCompletionAsync() { lock (gate) return worker ?? Task.CompletedTask; }
    public Task CancelAsync()
    {
        lock (gate) { if (!disposed) cancellation?.Cancel(); return worker == null ? Task.CompletedTask : Observe(worker); }
    }
    private static async Task Observe(Task task) { try { await task.ConfigureAwait(false); } catch { } }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true; cancellation?.Cancel(); textQueue.Clear(); pendingCharacters = 0;
            var source = cancellation;
            if (worker == null) source?.Dispose();
            else _ = worker.ContinueWith(t => { var observed = t.Exception; source?.Dispose(); }, TaskScheduler.Default);
        }
    }
    private async Task RunAsync(CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        var chunker = new TtsTextChunker(config);
        try
        {
            await ValidateVoiceAsync(token).ConfigureAwait(false);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                bool done;
                lock (gate)
                {
                    while (textQueue.Count > 0) { string text = textQueue.Dequeue(); pendingCharacters -= text.Length; chunker.Append(text, clock.ElapsedMilliseconds); }
                    done = inputCompleted;
                }
                string chunk = chunker.Take(clock.ElapsedMilliseconds, done);
                if (chunk != null)
                {
                    if (!string.IsNullOrWhiteSpace(chunk)) await SynthesizeAsync(chunk, token).ConfigureAwait(false);
                    continue;
                }
                if (done && chunker.IsEmpty) break;
                await Task.Delay(10, token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            Completed?.Invoke(); // Success only, strictly after the last PCM callback.
        }
        catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        catch (Exception ex)
        {
            string message = ex is InvalidOperationException ? ex.Message : "Azure 朗读连接失败或超时，请检查区域、网络和资源权限。";
            Error?.Invoke(message);
            throw new InvalidOperationException(message);
        }
    }
    private Uri Endpoint(string path) => testEndpoint != null ? new Uri(testEndpoint, path) : new Uri("https://" + config.region + ".tts.speech.microsoft.com/" + path);
    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, Endpoint(path));
        request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", config.apiKey.Trim());
        request.Headers.TryAddWithoutValidation("User-Agent", "CamelDigitalman");
        return request;
    }
    private async Task ValidateVoiceAsync(CancellationToken token)
    {
        JArray voices = null;
        if (testEndpoint == null)
            lock (CacheGate)
                if (VoiceCache.TryGetValue(config.region, out var hit) && DateTime.UtcNow - hit.Item1 < TimeSpan.FromMinutes(10)) voices = hit.Item2;
        if (voices == null)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var request = Request(HttpMethod.Get, "cognitiveservices/voices/list"))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(config.idleTimeoutSeconds));
                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false))
                {
                    CheckResponse(response);
                    voices = JArray.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
            }
            if (testEndpoint == null) lock (CacheGate) VoiceCache[config.region] = Tuple.Create(DateTime.UtcNow, voices);
        }
        JObject selected = null;
        foreach (var v in voices) if (string.Equals(v["ShortName"]?.ToString(), config.voiceName, StringComparison.Ordinal)) { selected = (JObject)v; break; }
        if (selected == null) throw new InvalidOperationException("所选 Azure 音色不在该区域的可用列表中，请检查音色名称及区域。");
        if (!string.IsNullOrEmpty(config.style) && !string.Equals(config.style, "neutral", StringComparison.OrdinalIgnoreCase))
        {
            bool supported = false;
            foreach (var style in selected["StyleList"] as JArray ?? new JArray()) if (style.ToString() == config.style) supported = true;
            if (!supported) throw new InvalidOperationException("当前 Azure 音色不支持所选语气，请修改风格或留空使用自然语气。");
        }
    }
    private async Task SynthesizeAsync(string text, CancellationToken token)
    {
        using (var total = CancellationTokenSource.CreateLinkedTokenSource(token))
        using (var idle = CancellationTokenSource.CreateLinkedTokenSource(total.Token))
        using (var request = Request(HttpMethod.Post, "cognitiveservices/v1"))
        {
            total.CancelAfter(TimeSpan.FromSeconds(config.requestTimeoutSeconds));
            idle.CancelAfter(TimeSpan.FromSeconds(config.idleTimeoutSeconds));
            request.Headers.TryAddWithoutValidation("X-Microsoft-OutputFormat", "raw-24khz-16bit-mono-pcm");
            request.Content = new StringContent(BuildSsml(text, config), Encoding.UTF8, "application/ssml+xml");
            var clock = Stopwatch.StartNew();
            using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idle.Token).ConfigureAwait(false))
            {
                CheckResponse(response);
                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (idle.Token.Register(() => stream.Dispose()))
                {
                    var buffer = new byte[4096];
                    int received = 0; long first = -1;
                    while (true)
                    {
                        idle.CancelAfter(TimeSpan.FromSeconds(config.idleTimeoutSeconds));
                        int count = await stream.ReadAsync(buffer, 0, buffer.Length, idle.Token).ConfigureAwait(false);
                        idle.Token.ThrowIfCancellationRequested();
                        if (count == 0) break;
                        if (first < 0) first = clock.ElapsedMilliseconds;
                        received += count;
                        var packet = new byte[count]; Buffer.BlockCopy(buffer, 0, packet, 0, count);
                        token.ThrowIfCancellationRequested();
                        AudioChunkReceived?.Invoke(packet);
                    }
                    if (received == 0 || (received & 1) != 0) throw new InvalidOperationException("Azure 返回了空音频或不完整的 PCM 数据。");
                    Diagnostics = $"Azure firstByte={first}ms synthesis={clock.ElapsedMilliseconds}ms bytes={received}";
                }
            }
        }
    }
    private static void CheckResponse(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        int code = (int)response.StatusCode;
        string reason = code == 401 || code == 403 ? "请检查 Key、区域和资源权限" : code == 429 ? "请求受限，请稍后重试或检查配额" : code == 400 ? "请检查音色和语音参数" : "请检查区域和网络，稍后重试";
        throw new InvalidOperationException($"Azure Speech HTTP {code}：{reason}。");
    }
    public static string BuildSsml(string text, AzureTTSSettings settings)
    {
        const string ns = "http://www.w3.org/2001/10/synthesis";
        var result = new StringBuilder();
        using (var xml = XmlWriter.Create(result, new XmlWriterSettings { OmitXmlDeclaration = true }))
        {
            xml.WriteStartElement("speak", ns); xml.WriteAttributeString("version", "1.0");
            xml.WriteAttributeString("xml", "lang", null, settings.language);
            xml.WriteStartElement("voice", ns); xml.WriteAttributeString("name", settings.voiceName);
            bool style = !string.IsNullOrWhiteSpace(settings.style) && !string.Equals(settings.style, "neutral", StringComparison.OrdinalIgnoreCase);
            if (style)
            {
                xml.WriteStartElement("mstts", "express-as", "https://www.w3.org/2001/mstts");
                xml.WriteAttributeString("style", settings.style);
                xml.WriteAttributeString("styledegree", settings.styleDegree.ToString(CultureInfo.InvariantCulture));
            }
            xml.WriteStartElement("prosody", ns);
            xml.WriteAttributeString("rate", settings.ratePercent.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture) + "%");
            xml.WriteAttributeString("pitch", settings.pitchPercent.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture) + "%");
            xml.WriteAttributeString("volume", settings.volumePercent.ToString(CultureInfo.InvariantCulture));
            xml.WriteString(text); xml.WriteEndElement();
            if (style) xml.WriteEndElement();
            xml.WriteEndElement(); xml.WriteEndElement();
        }
        return result.ToString();
    }
}
