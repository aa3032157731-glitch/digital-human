using System;
using System.Threading;
using System.Threading.Tasks;

public interface ITTSService : IDisposable
{
    event Action<byte[]> AudioChunkReceived;
    event Action<string> Error;
    event Action Completed;

    int OutputSampleRate { get; }

    Task StartAsync(CancellationToken cancellationToken);
    void EnqueueText(string text);
    void CompleteInput();
    Task WaitForCompletionAsync();
    Task CancelAsync();
}
