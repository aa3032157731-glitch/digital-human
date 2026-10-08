#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using UnityEngine;

// Explicit command-line smoke mode only. Normal launches never create this object.
public static class LipSyncPlayerSmoke
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static async void Run()
    {
        if (!Environment.GetCommandLineArgs().Contains("--mouth-smoke-test")) return;
        try
        {
            var config = SpeechConfig.FromSubscription(new string('0', 32), "eastasia");
            using (var synthesizer = new SpeechSynthesizer(config, null)) { }
            var go = new GameObject("Mouth SDK smoke", typeof(AudioSource), typeof(StreamingPcmAudioPlayer));
            UnityEngine.Object.DontDestroyOnLoad(go); go.GetComponent<AudioSource>().volume = 0;
            var player = go.GetComponent<StreamingPcmAudioPlayer>();
            long id = player.BeginStream(24000); player.EnqueuePcm(id, new byte[12000]); player.EndStream(id);
            double until = Time.realtimeSinceStartupAsDouble + 10;
            while (player.State != PcmPlaybackState.Completed)
            {
                if (Time.realtimeSinceStartupAsDouble > until) throw new Exception("Player audio clock timeout");
                await Task.Delay(20);
            }
            if (player.PlayedSamples != 6000) throw new Exception("Player sample accounting failed");
            Debug.Log("LIPSYNC_WINDOWS_SMOKE_PASS sdk=1.51.2 pcmSamples=6000"); Application.Quit(0);
        }
        catch (Exception e) { Debug.LogError("LIPSYNC_WINDOWS_SMOKE_FAIL " + e.GetType().Name + ": " + e.Message); Application.Quit(1); }
    }
}
#endif
