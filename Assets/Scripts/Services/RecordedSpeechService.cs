using System;
using System.Threading;
using System.Threading.Tasks;

// Local A/B replay always uses identical PCM; no additional cloud request.
public sealed class RecordedSpeechService : IAlignedTTSService
{
    public event Action<byte[]> AudioChunkReceived;
    public event Action<string> Error { add { } remove { } }
    public event Action Completed;
    public event Action<AlignedSpeechSegment> SegmentReady;
    public int OutputSampleRate => segment.sampleRate;
    private readonly AlignedSpeechSegment segment;
    private CancellationToken token;
    private bool finished;
    public RecordedSpeechService(AlignedSpeechSegment sample, bool salsa)
    {
        segment = new AlignedSpeechSegment { sequence = 0, sampleRate = sample.sampleRate, pcm = sample.pcm,
            frames = sample.frames, visemes = sample.visemes, mode = salsa ? MouthAnimationMode.Salsa : sample.mode,
            fallbackReason = salsa ? "同音频 A/B 对比" : sample.fallbackReason };
    }
    public Task StartAsync(CancellationToken cancellationToken) { token = cancellationToken; return Task.CompletedTask; }
    public void EnqueueText(string text) { }
    public void CompleteInput()
    {
        if (finished) return; token.ThrowIfCancellationRequested(); finished = true;
        SegmentReady?.Invoke(segment); AudioChunkReceived?.Invoke(segment.pcm); Completed?.Invoke();
    }
    public Task WaitForCompletionAsync() => Task.CompletedTask;
    public void AcknowledgeSegment(int sequence) { }
    public Task CancelAsync() { finished = true; return Task.CompletedTask; }
    public void Dispose() { finished = true; }
}
