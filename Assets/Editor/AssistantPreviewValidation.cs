using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public static class AssistantPreviewValidation
{
    private sealed class Speech : ITTSService
    {
        public event Action<byte[]> AudioChunkReceived;
        public event Action Completed;
        public event Action<string> Error { add { } remove { } }
        public int OutputSampleRate => 8000;
        public bool hold, disposed;
        public string text;
        private TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
        private CancellationTokenRegistration registration;
        public Task StartAsync(CancellationToken token)
        { registration = token.Register(() => completion.TrySetCanceled()); return Task.CompletedTask; }
        public void EnqueueText(string value) { text = value; AudioChunkReceived?.Invoke(new byte[4800]); }
        public void CompleteInput() { if (!hold) { Completed?.Invoke(); completion.TrySetResult(true); } }
        public Task WaitForCompletionAsync() => completion.Task;
        public Task CancelAsync() { completion.TrySetCanceled(); return Task.CompletedTask; }
        public void Dispose() { disposed = true; registration.Dispose(); }
    }
    public static async Task Run(StreamingPcmAudioPlayer player)
    {
        var host = new GameObject("Isolated preview controller"); host.SetActive(false);
        var controller = host.AddComponent<VoiceChatController>(); controller.streamingPlayer = player;
        controller.ttsProvider = TTSProvider.Doubao; controller.activeModelProfileId = "unchanged";
        var method = typeof(VoiceChatController).GetMethod("PreviewSpeechServiceAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        var success = new Speech();
        await (Task)method.Invoke(controller, new object[] { success, "合成测试", CancellationToken.None });
        if (!success.disposed || success.text != "合成测试" || player.State != PcmPlaybackState.Completed) throw new Exception("Preview completion/disposal failed");
        var waiting = new Speech { hold = true };
        using (var cancel = new CancellationTokenSource())
        {
            var task = (Task)method.Invoke(controller, new object[] { waiting, "取消测试", cancel.Token });
            await Task.Delay(50); cancel.Cancel(); bool stopped = false;
            try { await task; } catch (OperationCanceledException) { stopped = true; }
            if (!stopped || !waiting.disposed || player.audioSource.clip != null) throw new Exception("Preview cancellation left audio alive");
        }
        var old = new Speech { hold = true };
        var oldTask = (Task)method.Invoke(controller, new object[] { old, "旧试听", CancellationToken.None });
        var current = new Speech();
        var newTask = (Task)method.Invoke(controller, new object[] { current, "新试听", CancellationToken.None });
        try { await oldTask; } catch (OperationCanceledException) { }
        await newTask;
        if (!old.disposed || !current.disposed || controller.ttsProvider != TTSProvider.Doubao || controller.activeModelProfileId != "unchanged") throw new Exception("Preview changed selected provider/model");
        UnityEngine.Object.Destroy(host);
        Debug.Log("ASSISTANT_PREVIEW_PASS completion cancellation replacement selection-preserved");
    }
}
