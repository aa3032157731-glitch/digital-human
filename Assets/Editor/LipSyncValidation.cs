using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Microsoft.CognitiveServices.Speech;

public static class LipSyncValidation
{
    private static int checks;
    private static void Check(bool result, string message) { ++checks; if (!result) throw new Exception(message); }
    public static async void Run()
    {
        try
        {
            LipSyncProjectTools.Install();
            TestData(); TestChunker(); TestModels(); TestNative(); await TestServiceQueue();
            foreach (string name in new[] { "TestPackets", "TestChunker", "TestPlayer", "TestSettings", "TestLegacyTerminal", "TestScene" })
                typeof(AzureTtsValidation).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            await (Task)typeof(AzureTtsValidation).GetMethod("TestService", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            File.WriteAllText(LipSyncProjectTools.Output + "/validation.txt", "PASS checks=" + checks + "; existing Azure/PCM offline suite passed\n");
            Debug.Log("LIPSYNC_OFFLINE_PASS checks=" + checks); EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    private static string Frames(int start, params float[] jaw)
    {
        var rows = new JArray();
        foreach (float v in jaw) { var row = new float[55]; row[17] = v; rows.Add(new JArray(row)); }
        return new JObject { ["FrameIndex"] = start, ["BlendShapes"] = rows }.ToString();
    }
    private static void TestData()
    {
        var c = new SpeechAnimationCollector();
        c.Add(0, 0, Frames(1, 1)); c.Add(0, 0, Frames(0, 0)); c.Add(0, 0, Frames(1, 1));
        var sample = c.Complete(new byte[1600], 0); var values = new float[55]; sample.Sample(1 / 120.0, values);
        Check(sample.mode == MouthAnimationMode.BlendShapes && Math.Abs(values[17] - .5f) < .0001, "Indexed frames interpolate; duplicate and out-of-order events handled");
        var overshoot = new float[55]; overshoot[23] = -.046f; overshoot[53] = -.006f;
        c = new SpeechAnimationCollector(); c.Add(0, 0, new JObject { ["FrameIndex"] = 0, ["BlendShapes"] = new JArray { new JArray(overshoot) } }.ToString());
        Check(c.Complete(new byte[800], 0).mode == MouthAnimationMode.BlendShapes, "Observed Azure signed coefficients do not discard valid animation");
        c = new SpeechAnimationCollector(); c.Add(0, 21, Frames(0, .1f)); c.Add(0, 21, Frames(2, .2f));
        Check(c.Complete(new byte[24000], 0).mode == MouthAnimationMode.Visemes, "Missing frame falls back to viseme");
        c = new SpeechAnimationCollector(); c.Add(0, 2, "{bad json}");
        Check(c.Complete(new byte[24000], 0).mode == MouthAnimationMode.Visemes, "Bad JSON fallback");
        c = new SpeechAnimationCollector(); c.Add(0, 2, Frames(0, 1.5f));
        Check(c.Complete(new byte[24000], 0).mode == MouthAnimationMode.Visemes, "Out of range weights fallback");
        c = new SpeechAnimationCollector(); c.Add(0, 0, "");
        Check(c.Complete(new byte[24000], 0).mode == MouthAnimationMode.Salsa, "No meaningful animation fallback");
        c = new SpeechAnimationCollector(); c.Add(0, 21, ""); sample = c.Complete(new byte[24000], 0); sample.Sample(.1, values);
        Check(values[17] == 0 && values[18] == 1, "p/b/m closes lips");
        for (int id = 0; id < 22; ++id)
        { Array.Clear(values, 0, 55); MouthChannels.AddViseme(id, 1, values); Check(values.Where((v, i) => !MouthChannels.IsMouth(i)).All(v => v == 0), "Viseme leaves upper face alone"); }
        bool badPcm = false; try { c.Complete(new byte[3], 0); } catch (InvalidOperationException) { badPcm = true; }
        Check(badPcm, "Reject odd PCM");
        var xml = new System.Xml.XmlDocument(); xml.LoadXml(AzureFacialTTSService.BuildSsml("<&测试>", new AzureTTSSettings()));
        Check(xml.OuterXml.Contains("FacialExpression") && xml.DocumentElement.InnerText == "<&测试>", "SSML request and escaping");
        var old = JsonConvert.DeserializeObject<RuntimeVoiceChatSettings>("{\"schemaVersion\":6,\"azureTts\":{\"voiceName\":\"zh-CN-XiaoxiaoNeural\"}}"); old.EnsureDefaults();
        Check(old.azureTts.continuousLipSync && old.testMouth.mouthIntensity == .85f, "Old JSON gets lip-sync defaults");
        var json = JsonUtility.FromJson<AzureTTSSettings>("{\"voiceName\":\"zh-CN-XiaoxiaoNeural\"}");
        Check(json.continuousLipSync, "Unity JSON preserves enabled default");
    }
    private static void TestChunker()
    {
        var c = new SentenceSpeechChunker(); c.Append("爸爸妈妈", 0);
        Check(c.Take(1199, false) == null, "Wait for natural sentence");
        Check(c.Take(1200, false) == "爸爸妈妈", "Bounded first sentence wait");
        c = new SentenceSpeechChunker(); c.Append("你好。后续", 0);
        Check(c.Take(1, false) == "你好。" && c.Take(2, true) == "后续", "Sentence punctuation and tail");
        string text = string.Concat(Enumerable.Repeat("你好，2026.09.15 and internationalization 😀 test。", 10));
        c = new SentenceSpeechChunker(); var output = new System.Text.StringBuilder();
        for (int i = 0; i < text.Length; ++i) { c.Append(text.Substring(i, 1), i * 10); var part = c.Take(i * 10, false); if (part != null) { Check(!char.IsHighSurrogate(part[part.Length - 1]), "Do not split surrogate"); output.Append(part); } }
        while (!c.IsEmpty) output.Append(c.Take(10000, true));
        Check(output.ToString() == text, "No lost/repeated text");
    }
    private static void TestModels()
    {
        var drivers = UnityEngine.Object.FindObjectsOfType<AlignedMouthDriver>(true);
        Check(drivers.Length == 2, "Two saved model drivers");
        foreach (var driver in drivers)
        {
            Check(driver.HasMapping, "Mapped model " + driver.name);
            foreach (int i in Enumerable.Range(14, 27).Concat(new[] { 51 }))
                Check(driver.profile.bindings.Any(b => b.channel == i), "Required channel " + i + " for " + driver.name);
            var weights = new float[55]; weights[17] = .5f; weights[20] = .8f; weights[0] = 1;
            var salsa = driver.GetComponent<CrazyMinnow.SALSA.Salsa>(); bool prior = salsa.enabled;
            driver.Apply(weights); driver.Render(0);
            Check(!salsa.enabled, "Exclusive mouth ownership");
            var jawMap = driver.profile.boneBindings.First(b => b.channel == 17 && b.bonePath.EndsWith("CC_Base_JawRoot"));
            Check(Quaternion.Angle(driver.transform.Find(jawMap.bonePath).localRotation, jawMap.neutralRotation) > 1, "Authored jaw bone correction opens mouth");
            foreach (var b in driver.profile.bindings)
            {
                var t = driver.transform.Find(b.rendererPath); var smr = t.GetComponent<SkinnedMeshRenderer>();
                Check(smr.sharedMesh.GetBlendShapeIndex(b.shape) >= 0, "Resolvable named binding");
                float value = smr.GetBlendShapeWeight(smr.sharedMesh.GetBlendShapeIndex(b.shape));
                Check(value >= 0 && value <= 100, "Bounded shape");
            }
            driver.Release(); driver.Render(.1f);
            Check(salsa.enabled == prior, "Restore SALSA within 100ms");
        }
    }
    private static void TestNative()
    {
        var config = SpeechConfig.FromSubscription(new string('0', 32), "eastasia");
        using (var synth = new SpeechSynthesizer(config, null)) Check(synth != null, "SDK managed/native loading (no network)");
    }
    private static async Task TestServiceQueue()
    {
        var settings = new AzureTTSSettings { apiKey = "offline-synthetic", region = "eastasia" };
        Func<AzureTTSSettings, string, int, CancellationToken, Task<AlignedSpeechSegment>> fake = (s, text, i, token) =>
            Task.FromResult(new SpeechAnimationCollector().Complete(new byte[2400], i));
        using (var service = new AzureFacialTTSService(settings, fake))
        {
            int received = 0, completed = 0; var order = new System.Collections.Concurrent.ConcurrentQueue<int>();
            service.SegmentReady += s => { order.Enqueue(s.sequence); Interlocked.Increment(ref received); };
            service.Completed += () => ++completed;
            await service.StartAsync(CancellationToken.None); service.EnqueueText("第一句。第二句。第三句。"); service.CompleteInput();
            for (int i = 0; i < 100 && received < 2; ++i) await Task.Delay(10);
            await Task.Delay(50); Check(received == 2 && completed == 0, "Two-segment synthesis backpressure");
            service.AcknowledgeSegment(0); service.AcknowledgeSegment(0);
            var completedTask = service.WaitForCompletionAsync();
            Check(await Task.WhenAny(completedTask, Task.Delay(3000)) == completedTask, "Acknowledgement unblocks synthesis");
            await completedTask;
            Check(order.ToArray().SequenceEqual(new[] { 0, 1, 2 }) && completed == 1, "Exactly once ordered publication");
        }
        using (var service = new AzureFacialTTSService(settings, fake))
        {
            int received = 0; service.SegmentReady += s => ++received;
            await service.StartAsync(CancellationToken.None); service.EnqueueText("一。二。三。"); service.CompleteInput();
            for (int i = 0; i < 100 && received < 2; ++i) await Task.Delay(10);
            await service.CancelAsync(); Check(received == 2 && service.WaitForCompletionAsync().IsCompleted, "Cancellation releases blocked producer");
        }
    }
    public static async void CaptureLive()
    {
        try
        {
            var path = File.Exists(LipSyncProjectTools.Output + "/live-settings.json") ? LipSyncProjectTools.Output + "/live-settings.json" : VoiceChatRuntimeConfigStore.SettingsFilePath;
            if (!File.Exists(path)) throw new InvalidOperationException("本机配置不存在。");
            var settings = JsonConvert.DeserializeObject<RuntimeVoiceChatSettings>(File.ReadAllText(path)); settings.EnsureDefaults(); ServiceAccounts.Resolve(settings);
            settings.azureTts.Validate();
            var events = new JArray();
            var sample = await AzureFacialTTSService.SynthesizeSentenceAsync(settings.azureTts, "爸爸妈妈，乌云与圆月。Hello, nice to meet you.", 0, CancellationToken.None,
                (ticks, id, animation) => events.Add(new JObject { ["ticks"] = ticks, ["id"] = id, ["animation"] = animation }));
            Directory.CreateDirectory(LipSyncProjectTools.Output);
            File.WriteAllText(LipSyncProjectTools.Output + "/live-events.json", events.ToString());
            File.WriteAllText(LipSyncProjectTools.Output + "/live-sample.json", JsonConvert.SerializeObject(sample));
            File.WriteAllText(LipSyncProjectTools.Output + "/live-result.txt", "mode=" + sample.mode + " frames=" + (sample.frames?.Length ?? 0) + " duration=" + sample.Duration + " fallback=" + sample.fallbackReason);
            Debug.Log("LIPSYNC_LIVE_RESULT " + sample.mode);
            EditorApplication.Exit(sample.mode == MouthAnimationMode.BlendShapes ? 0 : 2);
        }
        catch (Exception e) { Debug.LogError(e is InvalidOperationException ? e.Message : "连续口型验证失败。"); EditorApplication.Exit(1); }
    }
    public static void ReprocessCapture()
    {
        try
        {
            var collector = new SpeechAnimationCollector();
            foreach (var e in JArray.Parse(File.ReadAllText(LipSyncProjectTools.Output + "/live-events.json")))
                collector.Add(e.Value<ulong>("ticks"), e.Value<uint>("id"), e.Value<string>("animation"));
            var prior = JsonConvert.DeserializeObject<AlignedSpeechSegment>(File.ReadAllText(LipSyncProjectTools.Output + "/live-sample.json"));
            var sample = collector.Complete(prior.pcm, 0);
            File.WriteAllText(LipSyncProjectTools.Output + "/live-sample.json", JsonConvert.SerializeObject(sample));
            File.WriteAllText(LipSyncProjectTools.Output + "/live-result.txt", "mode=" + sample.mode + " frames=" + (sample.frames?.Length ?? 0) + " duration=" + sample.Duration + " fallback=" + sample.fallbackReason);
            if (sample.mode != MouthAnimationMode.BlendShapes) throw new Exception(sample.fallbackReason);
            Debug.Log("LIPSYNC_REAL_CAPTURE_PASS frames=" + sample.frames.Length);
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
