using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.CognitiveServices.Speech;

public sealed class AzureFacialTTSService : IAlignedTTSService
{
    public event Action<byte[]> AudioChunkReceived;
    public event Action<string> Error;
    public event Action Completed;
    public event Action<AlignedSpeechSegment> SegmentReady;
    public int OutputSampleRate => 24000;
    private readonly AzureTTSSettings settings;
    private readonly object gate = new object();
    private readonly Queue<string> pending = new Queue<string>();
    private readonly HashSet<int> outstanding = new HashSet<int>();
    private readonly SemaphoreSlim slots = new SemaphoreSlim(2, 2);
    private CancellationTokenSource cancellation;
    private Task worker;
    private bool done, disposed;
    private int pendingChars;
    private readonly Func<AzureTTSSettings, string, int, CancellationToken, Task<AlignedSpeechSegment>> synthesize;

    public AzureFacialTTSService(AzureTTSSettings settings,
        Func<AzureTTSSettings, string, int, CancellationToken, Task<AlignedSpeechSegment>> synthesize = null)
    {
        this.settings = settings?.Copy();
        this.synthesize = synthesize ?? ((config, text, sequence, token) => SynthesizeSentenceAsync(config, text, sequence, token));
    }
    public Task StartAsync(CancellationToken token)
    {
        lock (gate)
        {
            if (disposed || worker != null) throw new InvalidOperationException("语音会话已启动或已释放。");
            if (settings == null) throw new InvalidOperationException("未配置 Azure。");
            settings.Validate();
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            worker = Task.Run(() => RunAsync(cancellation.Token));
            return Task.CompletedTask;
        }
    }
    public void EnqueueText(string text)
    {
        lock (gate)
        {
            if (done || disposed || string.IsNullOrEmpty(text)) return;
            if (pendingChars + text.Length > 65536) throw new InvalidOperationException("待合成文字过长。");
            pending.Enqueue(text); pendingChars += text.Length;
        }
    }
    public void CompleteInput() { lock (gate) done = true; }
    public Task WaitForCompletionAsync() { lock (gate) return worker ?? Task.CompletedTask; }
    public void AcknowledgeSegment(int sequence)
    {
        lock (gate) if (!disposed && outstanding.Remove(sequence)) slots.Release();
    }
    public async Task CancelAsync()
    {
        Task task;
        lock (gate) { if (!disposed) cancellation?.Cancel(); task = worker; }
        if (task != null) try { await task.ConfigureAwait(false); } catch { }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true; cancellation?.Cancel(); pending.Clear();
            var source = cancellation;
            if (worker == null) { source?.Dispose(); slots.Dispose(); }
            else _ = worker.ContinueWith(t => { var ignored = t.Exception; source?.Dispose(); slots.Dispose(); }, TaskScheduler.Default);
        }
    }
    private async Task RunAsync(CancellationToken token)
    {
        var clock = Stopwatch.StartNew(); var chunker = new SentenceSpeechChunker(); int sequence = 0;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested(); bool complete;
                lock (gate)
                {
                    while (pending.Count > 0) { string text = pending.Dequeue(); pendingChars -= text.Length; chunker.Append(text, clock.ElapsedMilliseconds); }
                    complete = done;
                }
                string sentence = chunker.Take(clock.ElapsedMilliseconds, complete);
                if (sentence != null)
                {
                    if (string.IsNullOrWhiteSpace(sentence)) continue;
                    await slots.WaitAsync(token).ConfigureAwait(false);
                    var segment = await synthesize(settings, sentence, sequence++, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    lock (gate) outstanding.Add(segment.sequence);
                    var receiver = SegmentReady;
                    receiver?.Invoke(segment);
                    AudioChunkReceived?.Invoke(segment.pcm);
                    if (receiver == null) AcknowledgeSegment(segment.sequence);
                    continue;
                }
                if (complete && chunker.IsEmpty) break;
                await Task.Delay(10, token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested(); Completed?.Invoke();
        }
        catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        catch (Exception ex)
        {
            string message = ex is InvalidOperationException ? ex.Message : "Azure 连续口型连接失败，请检查资源、网络和 Speech SDK。";
            Error?.Invoke(message); throw new InvalidOperationException(message);
        }
    }
    public static string BuildSsml(string text, AzureTTSSettings settings)
    {
        var xml = new XmlDocument(); xml.LoadXml(AzureTTSService.BuildSsml(text, settings));
        var voice = xml.DocumentElement.FirstChild;
        var viseme = xml.CreateElement("mstts", "viseme", "https://www.w3.org/2001/mstts");
        viseme.SetAttribute("type", "FacialExpression"); voice.PrependChild(viseme);
        return xml.OuterXml;
    }
    public static async Task<AlignedSpeechSegment> SynthesizeSentenceAsync(AzureTTSSettings settings, string text, int sequence, CancellationToken token,
        Action<ulong, uint, string> inspectEvent = null)
    {
        settings.Validate();
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            var config = SpeechConfig.FromSubscription(settings.apiKey.Trim(), settings.region);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.requestTimeoutSeconds));
            config.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw24Khz16BitMonoPcm);
            config.SpeechSynthesisVoiceName = settings.voiceName;
            using (var synth = new SpeechSynthesizer(config, null))
            {
                var collector = new SpeechAnimationCollector(); var collectorGate = new object();
                synth.VisemeReceived += (s, e) => { lock (collectorGate) { collector.Add(e.AudioOffset, e.VisemeId, e.Animation); inspectEvent?.Invoke(e.AudioOffset, e.VisemeId, e.Animation); } };
                var synthesis = synth.SpeakSsmlAsync(BuildSsml(text, settings));
                var stopSignal = Task.Delay(Timeout.Infinite, timeout.Token);
                if (await Task.WhenAny(synthesis, stopSignal).ConfigureAwait(false) != synthesis)
                {
                    // Observe late native completion without retaining a live response or exposing SDK error details.
                    _ = synth.StopSpeakingAsync();
                    _ = synthesis.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion) t.Result.Dispose(); else { var ignored = t.Exception; } }, TaskScheduler.Default);
                    token.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("Azure 小句合成超时。");
                }
                using (var result = await synthesis.ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();
                    if (result.Reason == ResultReason.SynthesizingAudioCompleted)
                        lock (collectorGate) return collector.Complete(result.AudioData, sequence);
                    var details = SpeechSynthesisCancellationDetails.FromResult(result);
                    // Retry only an explicit rejection of the animation feature, before publishing any PCM.
                    if (details.ErrorCode == CancellationErrorCode.BadRequest &&
                        ((details.ErrorDetails ?? "").IndexOf("viseme", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         (details.ErrorDetails ?? "").IndexOf("FacialExpression", StringComparison.OrdinalIgnoreCase) >= 0))
                        return await AudioOnlyAsync(settings, text, sequence, timeout.Token).ConfigureAwait(false);
                    throw new InvalidOperationException("Azure 合成失败（" + details.ErrorCode + "），请检查区域、音色和权限。");
                }
            }
        }
    }
    private static async Task<AlignedSpeechSegment> AudioOnlyAsync(AzureTTSSettings settings, string text, int sequence, CancellationToken token)
    {
        using (var service = new AzureTTSService(settings))
        using (var pcm = new MemoryStream())
        {
            service.AudioChunkReceived += bytes => pcm.Write(bytes, 0, bytes.Length);
            await service.StartAsync(token).ConfigureAwait(false); service.EnqueueText(text); service.CompleteInput();
            await service.WaitForCompletionAsync().ConfigureAwait(false);
            var result = new SpeechAnimationCollector().Complete(pcm.ToArray(), sequence);
            result.fallbackReason = "当前音色拒绝连续动画，已保留原音色朗读"; return result;
        }
    }
}
