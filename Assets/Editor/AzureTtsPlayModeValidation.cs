using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class AzureTtsPlayModeValidation
{
    static AzureTtsPlayModeValidation()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("AzureAudioValidation", false))
            { SessionState.SetBool("AzureAudioValidation", false); RunChecks(); }
        };
    }
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool("AzureAudioValidation", true);
        EditorApplication.EnterPlaymode();
    }
    private static byte[] Tone(int samples)
    {
        var data = new byte[samples * 2];
        for (int i = 0; i < samples; i++) { short value = (short)(Math.Sin(i * 2 * Math.PI * 220 / 8000) * 8000); data[2 * i] = (byte)value; data[2 * i + 1] = (byte)(value >> 8); }
        return data;
    }
    private static async Task Until(Func<bool> predicate, double seconds)
    {
        double end = Time.realtimeSinceStartupAsDouble + seconds;
        while (!predicate()) { if (Time.realtimeSinceStartupAsDouble > end) throw new Exception("Audio state timeout"); await Task.Delay(10); }
    }
    private static async void RunChecks()
    {
        try
        {
            new GameObject("Audio Listener", typeof(AudioListener));
            var go = new GameObject("Real Audio Test", typeof(AudioSource), typeof(StreamingPcmAudioPlayer));
            var player = go.GetComponent<StreamingPcmAudioPlayer>(); player.clipSeconds = 5;
            var source = go.GetComponent<AudioSource>(); source.spatialBlend = 0;
            long id = player.BeginStream(8000);
            player.EnqueuePcm(id, Tone(2400));
            await Until(() => source.isPlaying, 2);
            float peak = 0; var output = new float[256];
            for (int i = 0; i < 12; i++)
            {
                source.GetOutputData(output, 0); foreach (float value in output) peak = Mathf.Max(peak, Mathf.Abs(value));
                await Task.Delay(10);
            }
            await Until(() => player.State == PcmPlaybackState.Rebuffering, 2);
            if (peak < 0.01f) throw new Exception("No actual AudioSource output detected; peak=" + peak);
            foreach (int gap in new[] { 100, 300, 1000 })
            {
                await Task.Delay(gap);
                player.EnqueuePcm(id, Tone(2400));
                await Until(() => player.IsPlaying, 2);
                if (!player.IsResume) throw new Exception("Resume flag missing");
                await Until(() => player.State == PcmPlaybackState.Rebuffering, 2);
            }
            player.EndStream(id); await Until(() => player.State == PcmPlaybackState.Completed, 2);
            id = player.BeginStream(8000); player.EnqueuePcm(id, Tone(72000)); player.EndStream(id);
            await Until(() => player.State == PcmPlaybackState.Completed, 12);
            if (player.UnderrunCount != 0) throw new Exception("Unexpected underrun with buffered audio");
            for (int i = 0; i < 50; i++) { long old = id; id = player.BeginStream(8000); if (player.EnqueuePcm(old, Tone(10))) throw new Exception("Stale audio accepted"); player.StopAndClear(); }
            await Task.Delay(50);
            if (source.clip != null || source.isPlaying) throw new Exception("Audio survived cancellation");
            await AssistantPreviewValidation.Run(player);
            Debug.Log("AZURE_PLAYMODE_AUDIO_PASS peak=" + peak + " gaps=100,300,1000ms wraps=1 cancellations=50");
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
}
