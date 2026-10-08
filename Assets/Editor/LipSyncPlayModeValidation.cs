using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class LipSyncPlayModeValidation
{
    static LipSyncPlayModeValidation()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("MouthValidation", false))
            { SessionState.SetBool("MouthValidation", false); RunChecks(); }
        };
    }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/main.unity");
        var roots = UnityEngine.Object.FindObjectOfType<CharacterModelSwitcher>(true).modelRoots;
        foreach (var root in roots) root.transform.SetParent(null);
        var kept = new System.Collections.Generic.HashSet<GameObject>(roots);
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (!kept.Contains(root)) UnityEngine.Object.DestroyImmediate(root);
        var host = new GameObject("Isolated Model Switcher");
        var switcher = host.AddComponent<CharacterModelSwitcher>(); switcher.modelRoots = roots; switcher.applyOnStart = false;
        new GameObject("Isolated Audio Listener", typeof(AudioListener));
        // Prevent microphone, camera, LLM and saved-account writes during isolated runtime tests.
        foreach (var b in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true))
            if (!(b is AlignedMouthDriver) && b.GetType().Namespace != "CrazyMinnow.SALSA") b.enabled = false;
        SessionState.SetBool("MouthValidation", true); EditorApplication.EnterPlaymode();
    }
    private sealed class Feed : IAlignedTTSService
    {
        public event Action<byte[]> AudioChunkReceived { add { } remove { } }
        public event Action<string> Error { add { } remove { } }
        public event Action Completed;
        public event Action<AlignedSpeechSegment> SegmentReady;
        public int OutputSampleRate => 24000;
        public readonly List<int> acknowledged = new List<int>();
        public void Send(AlignedSpeechSegment s) => SegmentReady?.Invoke(s);
        public Action<AlignedSpeechSegment> Snapshot() => SegmentReady;
        public void AcknowledgeSegment(int n) { acknowledged.Add(n); }
        public Task StartAsync(CancellationToken token) => Task.CompletedTask;
        public void EnqueueText(string text) { }
        public void CompleteInput() => Completed?.Invoke();
        public Task WaitForCompletionAsync() => Task.CompletedTask;
        public Task CancelAsync() => Task.CompletedTask;
        public void Dispose() { }
    }
    private static AlignedSpeechSegment Sample(int sequence, float seconds)
    {
        var pcm = new byte[(int)(seconds * 24000) * 2];
        for (int i = 0; i < pcm.Length / 2; ++i) { short v = (short)(Math.Sin(i * Math.PI * 440 / 24000) * 4000); pcm[i * 2] = (byte)v; pcm[i * 2 + 1] = (byte)(v >> 8); }
        var frames = new float[(int)Math.Ceiling(seconds * 60)][];
        for (int i = 0; i < frames.Length; ++i) { frames[i] = new float[55]; frames[i][17] = .3f + .2f * (float)Math.Sin(i * .3); frames[i][20] = .2f; }
        return new AlignedSpeechSegment { sequence = sequence, pcm = pcm, frames = frames, mode = MouthAnimationMode.BlendShapes };
    }
    private static async Task Until(Func<bool> predicate, double seconds)
    {
        double until = Time.realtimeSinceStartupAsDouble + seconds;
        while (!predicate()) { if (Time.realtimeSinceStartupAsDouble > until) throw new Exception("Mouth runtime timeout"); await Task.Delay(10); }
    }
    private static async void RunChecks()
    {
        try
        {
            var switcher = UnityEngine.Object.FindObjectOfType<CharacterModelSwitcher>(true);
            // Avoid controller operations on a deliberately disabled scene controller.
            switcher.voiceChatController = null;
            var go = new GameObject("Isolated Mouth Playback", typeof(AudioSource), typeof(StreamingPcmAudioPlayer), typeof(AlignedMouthPlayback));
            var player = go.GetComponent<StreamingPcmAudioPlayer>(); player.clipSeconds = 5;
            go.GetComponent<AudioSource>().volume = 0; // Long synthetic stress test stays silent.
            foreach (var salsa in UnityEngine.Object.FindObjectsOfType<CrazyMinnow.SALSA.Salsa>(true)) salsa.audioSrc = go.GetComponent<AudioSource>();
            var mouth = go.GetComponent<AlignedMouthPlayback>(); mouth.player = player; mouth.modelSwitcher = switcher;
            var feed = new Feed(); long id = player.BeginStream(24000); mouth.Begin(feed, id);
            feed.Send(Sample(0, .5f)); await Until(() => player.IsAudible, 3);
            await Until(() => player.State == PcmPlaybackState.Rebuffering, 3);
            long stoppedAt = player.PlayedSamples; await Task.Delay(150);
            if (player.PlayedSamples != stoppedAt) throw new Exception("Mouth clock advanced in rebuffer");
            feed.Send(Sample(1, .5f)); feed.CompleteInput();
            await Until(() => player.State == PcmPlaybackState.Completed, 4);
            if (player.PlayedSamples != 24000 || feed.acknowledged.Count != 2) throw new Exception("Sentence sample accounting failed");
            feed = new Feed(); id = player.BeginStream(24000); mouth.Begin(feed, id);
            feed.Send(Sample(0, 32)); feed.CompleteInput();
            await Until(() => player.IsAudible, 3);
            for (int i = 0; i < switcher.modelRoots.Length; ++i)
            {
                switcher.currentModelIndex = i;
                for (int j = 0; j < switcher.modelRoots.Length; ++j) switcher.modelRoots[j].SetActive(j == i);
                await Task.Delay(100);
                if (mouth.Status != "连续口型") throw new Exception("Model switch lost timeline");
            }
            await Until(() => player.State == PcmPlaybackState.Completed, 35);
            if (player.PlayedSamples != 768000 || player.UnderrunCount != 0) throw new Exception("Long circular playback drift");
            for (int i = 0; i < 20; ++i)
            {
                feed = new Feed(); id = player.BeginStream(24000); mouth.Begin(feed, id); var late = feed.Snapshot();
                mouth.Clear(); player.StopAndClear();
                var next = new Feed(); id = player.BeginStream(24000); mouth.Begin(next, id);
                late(Sample(0, .1f)); await Task.Delay(20);
                if (player.QueuedSeconds != 0) throw new Exception("Old generation was accepted");
                mouth.Clear(); player.StopAndClear();
            }
            await Task.Delay(150);
            int realReplays = 0;
            string samplePath = LipSyncProjectTools.Output + "/live-sample.json";
            if (System.IO.File.Exists(samplePath))
            {
                var sample = Newtonsoft.Json.JsonConvert.DeserializeObject<AlignedSpeechSegment>(System.IO.File.ReadAllText(samplePath));
                for (int model = 0; model < switcher.modelRoots.Length; ++model)
                {
                    switcher.currentModelIndex = model;
                    for (int j = 0; j < switcher.modelRoots.Length; ++j) switcher.modelRoots[j].SetActive(j == model);
                    using (var recorded = new RecordedSpeechService(sample, false))
                    {
                        id = player.BeginStream(24000); mouth.Begin(recorded, id);
                        await recorded.StartAsync(CancellationToken.None); recorded.CompleteInput();
                        await Until(() => player.IsAudible, 3); await Task.Delay(200);
                        if (mouth.Status != "连续口型") throw new Exception("Real sample did not use continuous animation");
                        await Until(() => player.State == PcmPlaybackState.Completed, sample.Duration + 3);
                        if (player.PlayedSamples != sample.SampleCount) throw new Exception("Real sample audio duration mismatch");
                        ++realReplays;
                    }
                }
            }
            Debug.Log("LIPSYNC_PLAYMODE_PASS sentences=2 longPlayback=32s modelSwitches=2 cancellations=20 realReplays=" + realReplays);
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
