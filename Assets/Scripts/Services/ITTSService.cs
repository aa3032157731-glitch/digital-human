using System;
using System.Threading;
using System.Threading.Tasks;

public interface ITTSService : IDisposable
{
    // Immutable, ordered mono PCM16 little-endian packets. Events may run on a worker thread.
    event Action<byte[]> AudioChunkReceived;
    event Action<string> Error;
    // Successful input/ synthesis completion only, after all audio packets (not playback completion).
    event Action Completed;

    int OutputSampleRate { get; }

    Task StartAsync(CancellationToken cancellationToken);
    void EnqueueText(string text);
    void CompleteInput();
    Task WaitForCompletionAsync();
    Task CancelAsync();
}
