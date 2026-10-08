using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class AzureTtsValidation
{
    private static int checks;
    private static bool finalRun;
    public static void RunFinal() { finalRun = true; Run(); }
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool pass, string message) { if (!pass) throw new Exception(message); ++checks; }
    private static AzureTTSSettings Settings() => new AzureTTSSettings { apiKey = "offline-test-secret", region = "eastasia" };
    public static async void Run()
    {
        try
        {
            TestPackets(); TestChunker(); TestPlayer(); TestSettings();
            await TestService(); TestLegacyTerminal(); TestScene();
            Debug.Log("AZURE_TTS_VALIDATION_PASS checks=" + checks);
            if (finalRun) { AzureTtsBuildValidation.Preview(); AzureTtsBuildValidation.Build(); }
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    private static byte[] Pcm(long start, int samples)
    {
        var data = new byte[samples * 2];
        for (int i = 0; i < samples; i++) { short value = (short)((start + i) % 2000 - 1000); data[i * 2] = (byte)value; data[i * 2 + 1] = (byte)(value >> 8); }
        return data;
    }
    private static void TestPackets()
    {
        var buffer = new PcmPacketBuffer();
        var input = Pcm(0, 150000); var rng = new System.Random(7);
        long id = buffer.Begin(input.Length); int offset = 0;
        while (offset < input.Length)
        {
            int count = Math.Min(rng.Next(1, 4000), input.Length - offset);
            var packet = new byte[count]; Buffer.BlockCopy(input, offset, packet, 0, count);
            Check(buffer.Append(id, packet), "Accept random odd/even packet"); offset += count;
        }
        buffer.End(id);
        int decoded = 0; var floats = new float[789];
        while (buffer.PendingBytes > 0)
        {
            int count = buffer.Read(floats, floats.Length);
            for (int i = 0; i < count; i++) Check(floats[i] == (short)((decoded + i) % 2000 - 1000) / 32768f, "PCM exact sample ordering");
            decoded += count;
        }
        Check(buffer.IsDrained && decoded == 150000, "All samples drained");
        long old = id; id = buffer.Begin(8);
        Check(!buffer.Append(old, new byte[2]), "Reject stale packet"); buffer.End(old);
        Check(buffer.Append(id, new byte[8]) && !buffer.Append(id, new byte[1]) && buffer.Error != null, "Bounded overflow is explicit");
        id = buffer.Begin(8); buffer.Append(id, new byte[1]); buffer.End(id);
        Check(buffer.Error != null, "Incomplete tail rejected");
        for (int i = 0; i < 50; i++) { old = id; buffer.Clear(); id = buffer.Begin(8); Check(!buffer.Append(old, new byte[2]), "Rapid cancellation isolation"); }
    }
    private static void TestChunker()
    {
        var settings = Settings();
        var chunker = new TtsTextChunker(settings);
        chunker.Append("你好", 0); Check(chunker.Take(179, false) == null, "First timer waits");
        Check(chunker.Take(180, false) == "你好", "First timer flushes short text");
        chunker.Append("短句。后面还有一些文字", 200);
        Check(chunker.Take(200, false) == "短句。", "Scan internal punctuation");
        Check(chunker.Take(210, true) == "后面还有一些文字", "Flush trailing text");
        string text = string.Concat(Enumerable.Repeat("中文 😀 hello world & <tag> 123.45。", 20));
        chunker = new TtsTextChunker(settings); var output = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            chunker.Append(text.Substring(i, 1), i * 20);
            var part = chunker.Take(i * 20, false); if (part != null) { Check(!char.IsHighSurrogate(part[part.Length - 1]), "No split surrogate"); output.Append(part); }
        }
        while (!chunker.IsEmpty) output.Append(chunker.Take(100000, true));
        Check(output.ToString() == text, "No lost or repeated text");
        var xml = new XmlDocument(); settings.style = "cheerful";
        xml.LoadXml(AzureTTSService.BuildSsml("<&你好>\"", settings));
        Check(xml.DocumentElement.InnerText == "<&你好>\"", "SSML XML escaping");
        Check(xml.OuterXml.Contains("express-as") && xml.OuterXml.Contains("styledegree"), "SSML style controls");
    }
    private static long Counter(StreamingPcmAudioPlayer player, string name) => (long)typeof(StreamingPcmAudioPlayer).GetField(name, Private).GetValue(player);
    private static void Tick(StreamingPcmAudioPlayer player, double now) => typeof(StreamingPcmAudioPlayer).GetMethod("Tick", Private).Invoke(player, new object[] { now });
    private static void TestPlayer()
    {
        var go = new GameObject("PCM Validation", typeof(AudioSource), typeof(StreamingPcmAudioPlayer));
        try
        {
            var player = go.GetComponent<StreamingPcmAudioPlayer>(); player.audioSource = go.GetComponent<AudioSource>(); player.clipSeconds = 5;
            long id = player.BeginStream(8000); double now = AudioSettings.dspTime;
            player.EnqueuePcm(id, Pcm(0, 400)); player.EndStream(id); Tick(player, now);
            Check(player.State == PcmPlaybackState.Draining, "Short tail starts below threshold");
            Tick(player, now + 0.1); Check(player.State == PcmPlaybackState.Completed && player.audioSource.clip == null, "Tail completes and releases clip");
            id = player.BeginStream(8000); player.EnqueuePcm(id, Pcm(0, 1600)); Tick(player, now);
            Tick(player, now + 1.0); Check(player.State == PcmPlaybackState.Rebuffering && Counter(player, "played") == 1600, "DSP frontier protects 1s stall");
            player.EnqueuePcm(id, Pcm(1600, 1600)); Tick(player, now + 1.01);
            Check(player.IsResume && Counter(player, "played") == 1600, "Resume does not reset cursor");
            player.EndStream(id); Tick(player, now + 2); Check(player.State == PcmPlaybackState.Completed, "Resumed stream drains");
            id = player.BeginStream(8000); long produced = 0; int activeClips = Resources.FindObjectsOfTypeAll<AudioClip>().Length;
            // Accelerated 10 minutes: every newly written sample is checked over >100 wraps.
            for (int frame = 0; frame < 30000; frame++)
            {
                player.EnqueuePcm(id, Pcm(produced, 160)); produced += 160;
                long before = Counter(player, "written"); Tick(player, now + frame * 0.02);
                long after = Counter(player, "written");
                if (after > before)
                {
                    var actual = new float[after - before]; player.audioSource.clip.GetData(actual, (int)(before % player.audioSource.clip.samples));
                    for (int i = 0; i < actual.Length; i++) if (actual[i] != (short)((before + i) % 2000 - 1000) / 32768f) throw new Exception("Ring write corrupt at " + (before + i));
                }
                Check(after - Counter(player, "played") < 40000 && player.QueuedSeconds < 1, "Bounded ring during ten-minute stream");
            }
            Check(Resources.FindObjectsOfTypeAll<AudioClip>().Length == activeClips, "No clip accumulation");
            player.EndStream(id); Tick(player, now + 605);
            Check(player.State == PcmPlaybackState.Completed && Counter(player, "played") == produced, "Ten-minute sample count exact");
            id = player.BeginStream(8000); player.EnqueuePcm(id, Pcm(0, 100000));
            for (int i = 0; i < 6; i++) Tick(player, now);
            Check(Counter(player, "written") <= 40000 - 1024 && player.QueuedSeconds > 0, "Fast producer cannot overwrite unread audio");
            player.StopAndClear();
            Check(!player.EnqueuePcm(id, new byte[4]), "Player rejects cancelled stream");
            for (int i = 0; i < 50; i++) { id = player.BeginStream(16000); player.StopAndClear(); }
            Check(player.audioSource.clip == null, "Repeated begin/stop releases clip");
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
    private static void TestSettings()
    {
        var legacy = JsonUtility.FromJson<RuntimeVoiceChatSettings>("{\"schemaVersion\":4,\"features\":{\"ttsProvider\":\"Doubao\"},\"azureTts\":null,\"playback\":null}");
        legacy.EnsureDefaults(); Check(legacy.schemaVersion == 6 && legacy.azureTts != null && legacy.playback.startBufferSeconds == 0.12f && legacy.features.ttsProvider == "Doubao", "v4 migration preserves provider");
        legacy.azureTts = Settings(); legacy.azureTts.style = "cheerful";
        var copy = JsonUtility.FromJson<RuntimeVoiceChatSettings>(JsonUtility.ToJson(legacy)); copy.EnsureDefaults();
        Check(copy.azureTts.style == "cheerful" && copy.azureTts.apiKey == "offline-test-secret", "Azure config roundtrip");
        Check((int)TTSProvider.XunfeiSuperSmart == 0 && (int)TTSProvider.XunfeiOnline == 1 && (int)TTSProvider.Doubao == 2 && (int)TTSProvider.AzureSpeech == 3, "Serialized enum compatibility");
    }
    private static async Task TestService()
    {
        foreach (string mode in new[] { "ok", "401", "429", "badvoice", "badstyle", "odd", "empty", "cancel", "timeout" })
        {
            var settings = Settings(); settings.idleTimeoutSeconds = 1; settings.requestTimeoutSeconds = 3;
            if (mode == "badvoice") settings.voiceName = "unknown";
            if (mode == "badstyle") settings.style = "unsupported";
            using (var client = new HttpClient(new FakeHandler(mode)))
            using (var service = new AzureTTSService(settings, client, new Uri("https://offline.invalid/")))
            using (var cts = new CancellationTokenSource())
            {
                var data = new List<byte>(); bool completed = false; string error = null;
                service.AudioChunkReceived += bytes => data.AddRange(bytes); service.Completed += () => completed = true; service.Error += message => error = message;
                await service.StartAsync(cts.Token); service.EnqueueText("你好。"); service.CompleteInput();
                if (mode == "cancel") cts.CancelAfter(100);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                bool failed = false;
                try { await service.WaitForCompletionAsync(); }
                catch (Exception) { failed = true; if (mode == "ok") throw; }
                Check(failed == (mode != "ok"), "Expected service outcome: " + mode);
                if (mode == "ok") Check(completed && data.SequenceEqual(Pcm(0, 5000)) && error == null, "Streaming PCM bytes and success ordering");
                else Check(!completed, "Failure/cancel never emits success: " + mode);
                if (mode == "cancel") Check(error == null && watch.ElapsedMilliseconds < 1500, "Cancellation is prompt and silent");
                else if (mode != "ok") Check(error != null && !error.Contains(settings.apiKey), "Useful error without key: " + mode);
            }
        }
    }
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly string mode;
        public FakeHandler(string mode) { this.mode = mode; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Get) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[{\"ShortName\":\"zh-CN-XiaoxiaoNeural\",\"StyleList\":[\"cheerful\"]}]") });
            if (mode == "401" || mode == "429") return Task.FromResult(new HttpResponseMessage((HttpStatusCode)int.Parse(mode)));
            Check(request.Headers.GetValues("X-Microsoft-OutputFormat").Single() == "raw-24khz-16bit-mono-pcm", "Raw PCM request format");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ChunkStream(mode)) });
        }
    }
    private sealed class ChunkStream : MemoryStream
    {
        private readonly string mode;
        public ChunkStream(string mode) : base(mode == "empty" ? new byte[0] : mode == "odd" ? new byte[3] : Pcm(0, 5000)) { this.mode = mode; }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            await Task.Delay(mode == "cancel" || mode == "timeout" ? 5000 : 1, token);
            return await base.ReadAsync(buffer, offset, Math.Min(count, 317), token);
        }
    }
    private static void TestLegacyTerminal()
    {
        foreach (ITTSService service in new ITTSService[] { new XFTTSService(null), new XFOnlineTTSService(null), new DoubaoTTSService(null, null, null) })
        {
            int completed = 0, errors = 0;
            service.Completed += () => ++completed; service.Error += _ => ++errors;
            service.CancelAsync().GetAwaiter().GetResult();
            service.GetType().GetMethod("CompleteSuccessfully", Private).Invoke(service, null);
            service.GetType().GetMethod("CompleteWithError", Private).Invoke(service, new object[] { "late error", new Exception("late") });
            Check(completed == 0 && errors == 0 && service.WaitForCompletionAsync().IsCanceled, "Cancellation rejects late terminal callbacks");
            service.Dispose(); service.Dispose();
        }
    }
    private static void TestScene()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/main.unity");
        var controller = UnityEngine.Object.FindObjectOfType<VoiceChatController>(true);
        var panel = UnityEngine.Object.FindObjectOfType<AdvancedSettingsPanel>(true);
        Check(controller.azureTtsConfig != null && controller.streamingPlayer != null, "Scene config and player wired");
        var transforms = panel.GetComponentsInChildren<Transform>(true);
        foreach (var name in new[] { "Tab_TtsAzure", "Page_TtsAzure", "Button_AzurePreview", "Button_AzureStop", "Button_TTSProvider_AzureSpeech", "Input_AzureTTS_Region", "Page_AudioBuffer" })
            Check(transforms.Any(t => t.name == name), "Scene control " + name);
        Check(transforms.First(t => t.name == "Input_AzureTTS_ApiKey").GetComponent<InputField>().contentType == InputField.ContentType.Password, "Azure secret masked");
        Check(string.IsNullOrEmpty(controller.azureTtsConfig.settings.apiKey), "No key in config asset");
    }
}
