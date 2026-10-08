using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public sealed class XFOnlineTTSService : ITTSService
{
    public event Action<byte[]> AudioChunkReceived;
    public event Action<string> Error;
    public event Action Completed;

    public int OutputSampleRate => config != null ? config.sampleRate : 16000;

    private readonly XFOnlineTTSConfig config;
    private readonly ConcurrentQueue<string> textQueue = new ConcurrentQueue<string>();
    private readonly TaskCompletionSource<bool> completionSource = new TaskCompletionSource<bool>();
    private CancellationTokenSource internalCts;
    private ClientWebSocket activeWebSocket;
    private volatile bool inputCompleted;
    private int terminalState;

    public XFOnlineTTSService(XFOnlineTTSConfig config)
    {
        this.config = config;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ValidateConfig();

        if (!string.Equals(config.audioEncoding, "raw", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This Unity streaming player expects Xunfei online TTS audioEncoding=raw PCM.");
        }

        internalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = internalCts.Token;
        _ = Task.Run(() => WorkerLoopAsync(token));
        return Task.CompletedTask;
    }

    public void EnqueueText(string text)
    {
        if (string.IsNullOrEmpty(text) || inputCompleted)
        {
            return;
        }

        textQueue.Enqueue(text);
    }

    public void CompleteInput()
    {
        inputCompleted = true;
    }

    public Task WaitForCompletionAsync()
    {
        return completionSource.Task;
    }

    public Task CancelAsync()
    {
        CompleteCancelled();
        try { internalCts?.Cancel(); activeWebSocket?.Abort(); } catch (ObjectDisposedException) { }
        CompleteCancelled();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        CompleteCancelled();
        try { internalCts?.Cancel(); } catch (ObjectDisposedException) { }
        activeWebSocket?.Dispose();
        internalCts?.Dispose();
    }

    private async Task WorkerLoopAsync(CancellationToken cancellationToken)
    {
        var pending = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                while (textQueue.TryDequeue(out var delta))
                {
                    pending.Append(delta);
                }

                var shouldFlush = pending.Length >= config.minChunkChars ||
                                  EndsWithSentencePunctuation(pending) ||
                                  (inputCompleted && pending.Length > 0);

                if (shouldFlush)
                {
                    var text = TakeChunk(pending, config.maxChunkChars);
                    await SynthesizeOnceAsync(text, cancellationToken);
                    continue;
                }

                if (inputCompleted && pending.Length == 0 && textQueue.IsEmpty)
                {
                    break;
                }

                await Task.Delay(Math.Max(5, config.textPollIntervalMs), cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            CompleteSuccessfully();
        }
        catch (OperationCanceledException)
        {
            CompleteCancelled();
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            CompleteCancelled();
        }
        catch (Exception ex)
        {
            CompleteWithError($"XF online TTS failed: {ex.Message}", ex);
        }

    }

    private async Task SynthesizeOnceAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        using (var webSocket = new ClientWebSocket())
        {
            activeWebSocket = webSocket;
            var uri = XFWebSocketAuth.BuildAuthenticatedUri(config.websocketUrl, config.apiKey, config.apiSecret);

            await webSocket.ConnectAsync(uri, cancellationToken);
            await SendTextAsync(webSocket, BuildRequest(text), cancellationToken);

            while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
            {
                var message = await ReceiveTextAsync(webSocket, cancellationToken);
                if (string.IsNullOrEmpty(message))
                {
                    throw new IOException("TTS connection closed before the final audio frame.");
                }

                if (HandleResponse(message))
                {
                    break;
                }
            }
        }

        activeWebSocket = null;
    }

    private string BuildRequest(string text)
    {
        var business = new JObject
        {
            ["aue"] = config.audioEncoding,
            ["auf"] = config.audioFormat,
            ["vcn"] = config.voiceName,
            ["speed"] = config.speed,
            ["volume"] = config.volume,
            ["pitch"] = config.pitch,
            ["bgs"] = config.backgroundSound,
            ["tte"] = string.IsNullOrWhiteSpace(config.textEncoding) ? "UTF8" : config.textEncoding
        };

        if (!string.IsNullOrWhiteSpace(config.englishPronunciation))
        {
            business["reg"] = config.englishPronunciation;
        }

        if (!string.IsNullOrWhiteSpace(config.numberReadMode))
        {
            business["rdn"] = config.numberReadMode;
        }

        var root = new JObject
        {
            ["common"] = new JObject
            {
                ["app_id"] = config.appId
            },
            ["business"] = business,
            ["data"] = new JObject
            {
                ["status"] = 2,
                ["text"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
            }
        };

        return root.ToString(Newtonsoft.Json.Formatting.None);
    }

    private bool HandleResponse(string json)
    {
        var root = JObject.Parse(json);
        var code = root["code"]?.Value<int>() ?? 0;

        if (code != 0)
        {
            var message = root["message"]?.ToString() ?? "unknown error";
            throw new InvalidOperationException($"XF online TTS error {code}: {message}");
        }

        var audioBase64 = root["data"]?["audio"]?.ToString();
        if (!string.IsNullOrEmpty(audioBase64))
        {
            AudioChunkReceived?.Invoke(Convert.FromBase64String(audioBase64));
        }

        var status = root["data"]?["status"]?.Value<int>() ?? -1;
        return status == 2;
    }

    private static async Task SendTextAsync(ClientWebSocket webSocket, string text, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<string> ReceiveTextAsync(ClientWebSocket webSocket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];

        using (var stream = new MemoryStream())
        {
            while (true)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                stream.Write(buffer, 0, result.Count);

                if (result.EndOfMessage)
                {
                    break;
                }
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    private static bool EndsWithSentencePunctuation(StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            return false;
        }

        var c = builder[builder.Length - 1];
        return c == '。' || c == '！' || c == '？' || c == '；' ||
               c == '.' || c == '!' || c == '?' || c == ';' || c == '\n';
    }

    private static string TakeChunk(StringBuilder builder, int maxChars)
    {
        var count = Math.Min(Math.Max(1, maxChars), builder.Length);
        var text = builder.ToString(0, count);
        builder.Remove(0, count);
        return text;
    }

    private void CompleteSuccessfully()
    {
        if (Interlocked.CompareExchange(ref terminalState, 1, 0) != 0) return;
        try { Completed?.Invoke(); } finally { completionSource.TrySetResult(true); }
    }

    private void CompleteWithError(string message, Exception exception)
    {
        if (Interlocked.CompareExchange(ref terminalState, 2, 0) != 0) return;
        try { Error?.Invoke(message); } finally { completionSource.TrySetException(exception); }
    }

    private void CompleteCancelled()
    {
        if (Interlocked.CompareExchange(ref terminalState, 3, 0) == 0) completionSource.TrySetCanceled();
    }

    private void ValidateConfig()
    {
        if (config == null)
        {
            throw new InvalidOperationException("XFOnlineTTSConfig is not assigned.");
        }

        if (string.IsNullOrWhiteSpace(config.appId) ||
            string.IsNullOrWhiteSpace(config.apiKey) ||
            string.IsNullOrWhiteSpace(config.apiSecret))
        {
            throw new InvalidOperationException("XFOnlineTTSConfig credentials are incomplete.");
        }
    }
}
