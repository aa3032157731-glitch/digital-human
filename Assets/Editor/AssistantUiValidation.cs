using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Offline checks use synthetic credentials and an isolated file. No subscription is contacted.
public static class AssistantUiValidation
{
    private static int checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name); checks++; Debug.Log("ASSISTANT_CHECK " + name);
    }
    public static async void Run()
    {
        try
        {
            TestMigration(); await TestCatalog();
            Render(); Debug.Log("ASSISTANT_UI_PASS checks=" + checks); EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    private static void TestMigration()
    {
        var settings = new RuntimeVoiceChatSettings { schemaVersion = 5 };
        var p = ChatModelProfile.Create("DeepSeek"); p.apiKey = "synthetic-only"; p.model = "model-a";
        var p2 = p.Copy(); p2.id = Guid.NewGuid().ToString("N"); p2.model = "model-b"; p2.temperature = .2f;
        var p3 = p.Copy(); p3.id = Guid.NewGuid().ToString("N"); p3.apiKey = "separate-synthetic-key";
        settings.modelProfiles.AddRange(new[] { p, p2, p3 }); settings.activeModelProfileId = p2.id;
        settings.azureTts.apiKey = "synthetic-azure"; settings.azureTts.region = "eastasia";
        settings.EnsureDefaults();
        Check(settings.schemaVersion == 6, "schema v6");
        Check(p.serviceAccountId == p2.serviceAccountId && p3.serviceAccountId != p.serviceAccountId, "merge exact matching credentials only");
        Check(settings.activeModelProfileId == p2.id && p2.temperature == .2f, "keep selection and model parameters");
        string json = JsonConvert.SerializeObject(settings); settings.EnsureDefaults();
        Check(JsonConvert.SerializeObject(settings) == json, "migration idempotent");
        var account = settings.serviceAccounts.Find(a => a.id == p.serviceAccountId); account.Set("apiKey", "updated-synthetic");
        ServiceAccounts.Resolve(settings);
        Check(p.apiKey == "updated-synthetic" && p2.apiKey == "updated-synthetic" && p3.apiKey != p.apiKey, "shared credentials resolve independently");
        var azure = settings.serviceAccounts.Find(a => a.kind == "AzureSpeech");
        Check(azure.Get("apiKey") == "synthetic-azure" && azure.Get("region") == "eastasia", "speech credentials migrate");
        var roundtrip = JsonConvert.DeserializeObject<RuntimeVoiceChatSettings>(JsonConvert.SerializeObject(settings)); roundtrip.EnsureDefaults();
        Check(roundtrip.serviceAccounts.Count == settings.serviceAccounts.Count && roundtrip.modelProfiles[1].model == "model-b", "roundtrip accounts and models");
        string file = Path.GetFullPath("Temp/assistant-ui-settings.json"); Directory.CreateDirectory(Path.GetDirectoryName(file));
        typeof(VoiceChatRuntimeConfigStore).GetField("EditorSettingsPathOverride", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, file);
        File.WriteAllText(file, "{\"schemaVersion\":5,\"deepSeek\":{\"apiKey\":\"old-synthetic\"}}");
        Check(VoiceChatRuntimeConfigStore.SaveSettings(settings), "atomic save");
        Check(File.Exists(file + ".v5.bak") && File.ReadAllText(file + ".v5.bak").Contains("old-synthetic"), "original migration backup");
        Check(VoiceChatRuntimeConfigStore.LoadSettings().activeModelProfileId == p2.id, "saved active model restored");
        var deleted = roundtrip.serviceAccounts.Find(a => a.kind == "Doubao"); roundtrip.serviceAccounts.Remove(deleted); roundtrip.EnsureDefaults();
        Check(!roundtrip.serviceAccounts.Exists(a => a.id == deleted.id), "deleted unused account stays deleted");
        p.serviceAccountId = "missing"; bool rejected = false;
        try { ServiceAccounts.Resolve(settings); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "missing reference rejected");
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int requests;
        public HttpStatusCode code = HttpStatusCode.OK;
        public string body = "{\"data\":[{\"id\":\"alpha\"},{\"id\":\"alpha\"},{\"id\":\"beta\"}]}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { requests++; token.ThrowIfCancellationRequested(); return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) }); }
    }
    private static async Task TestCatalog()
    {
        var a = ServiceAccounts.New("Compatible"); a.Set("uri", "https://example.invalid/v1/chat/completions"); a.Set("apiKey", "synthetic");
        Check(ServiceCatalog.Endpoint(a).AbsoluteUri == "https://example.invalid/v1/models", "model list URL normalization");
        var handler = new Handler();
        using (var catalog = new ServiceCatalog(handler))
        {
            var list = await catalog.FetchAsync(a, false, CancellationToken.None);
            Check(list.Count == 2, "catalog duplicate IDs removed");
            await catalog.FetchAsync(a, false, CancellationToken.None); Check(handler.requests == 1, "catalog cache");
            a.Set("apiKey", "other-synthetic"); await catalog.FetchAsync(a, false, CancellationToken.None);
            Check(handler.requests == 2, "credential change invalidates cache");
            handler.code = HttpStatusCode.Unauthorized; bool rejected = false;
            try { await catalog.FetchAsync(a, true, CancellationToken.None); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && catalog.Cached(a).Count == 2, "failed refresh retains cache");
            var azure = ServiceAccounts.New("AzureSpeech"); azure.Set("region", "eastasia"); azure.Set("apiKey", "synthetic");
            handler.code = HttpStatusCode.OK; handler.body = "[{\"ShortName\":\"zh-CN-Test\",\"LocalName\":\"测试音色\",\"Locale\":\"zh-CN\",\"StyleList\":[\"cheerful\"]}]";
            var voices = await catalog.FetchAsync(azure, true, CancellationToken.None);
            Check(voices[0].language == "zh-CN" && voices[0].styles[0] == "cheerful", "Azure voice metadata");
            var cancel = new CancellationTokenSource(); cancel.Cancel(); bool cancelled = false;
            try { await catalog.FetchAsync(azure, true, cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "catalog cancellation");
        }
    }
    private static void Render()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var samples = new RuntimeVoiceChatSettings(); samples.EnsureDefaults();
        VoiceChatRuntimeConfigStore.SaveSettings(samples);
        var owner = new GameObject("SettingsHost").AddComponent<AdvancedSettingsPanel>();
        owner.interfaceRoot = new GameObject("AssistantInterface", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<RectTransform>();
        owner.OpenPanel(); var hub = owner.GetComponent<AssistantSettingsHub>();
        Check(hub.IsOpen && hub.Draft != null, "settings opens with isolated draft");
        var initialProvider = hub.Draft.features.ttsProvider;
        Check(hub.Save(), "empty first-run chat does not block saving speech settings");
        Check(hub.Draft.features.ttsProvider == initialProvider, "saving preserves provider selection");
        string overrideFile = (string)typeof(VoiceChatRuntimeConfigStore).GetField("EditorSettingsPathOverride", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        string blockedPath = Path.GetFullPath("Temp/assistant-write-failure"); Directory.CreateDirectory(blockedPath);
        typeof(VoiceChatRuntimeConfigStore).GetField("EditorSettingsPathOverride", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, blockedPath);
        string originalDraft = JsonConvert.SerializeObject(hub.Draft);
        Check(!hub.Save() && JsonConvert.SerializeObject(hub.Draft) == originalDraft, "write failure preserves draft");
        typeof(VoiceChatRuntimeConfigStore).GetField("EditorSettingsPathOverride", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, overrideFile);
        owner.ShowPageByName("Page_ApiKeys");
        typeof(AssistantSettingsHub).GetMethod("SetStatus", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hub, new object[] { "修改后点击保存并应用" });
        string baseline = JsonConvert.SerializeObject(hub.Draft);
        var camera = new GameObject("PreviewCamera").AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(208, 221, 227, 255);
        SavePreview(camera, "providers", 1920, 1080);
        typeof(AssistantSettingsHub).GetMethod("ShowModules", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hub, null);
        SavePreview(camera, "modules", 1920, 1080);
        Check(JsonConvert.SerializeObject(hub.Draft) == baseline, "navigation does not mutate draft");
        owner.ShowPageByName("Page_TtsAzure"); SavePreview(camera, "speech", 1920, 1080);
        SavePreview(camera, "speech-small", 1280, 720);
        owner.ShowPageByName("Page_DeepSeek"); SavePreview(camera, "chat-model", 1920, 1080);
        owner.ShowPageByName("Page_ApiKeys"); SavePreview(camera, "providers-large", 2560, 1440);
        var account = hub.Draft.serviceAccounts.First(a => a.kind == "DeepSeek");
        typeof(AssistantSettingsHub).GetMethod("EditAccount", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hub, new object[] { account });
        Check(owner.interfaceRoot.GetComponentsInChildren<InputField>(true).Any(i => i.contentType == InputField.ContentType.Password), "service secret masked");
        SavePreview(camera, "account", 1920, 1080);
        Check(owner.interfaceRoot.GetComponentsInChildren<Canvas>(true).Length == 1, "one interface canvas");
        hub.RequestClose();
        Check(owner.interfaceRoot.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Confirmation"), "unsaved edits prompt");
        typeof(AssistantSettingsHub).GetMethod("CloseNow", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hub, null);
        Check(!hub.IsOpen && !AssistantSettingsHub.ModalOpen, "close clears modal input block");
        var fakeHost = new GameObject("SyntheticController"); fakeHost.SetActive(false);
        var controller = fakeHost.AddComponent<VoiceChatController>();
        controller.enablePersistentConversation = false; controller.enableKnowledgeBase = false;
        var canvas = owner.interfaceRoot.GetComponentsInChildren<Canvas>(true).Single();
        var dashboard = owner.gameObject.AddComponent<AssistantDashboard>(); dashboard.Build(canvas.transform, controller, hub.Open);
        var chat = (Text)typeof(AssistantDashboard).GetField("conversation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dashboard);
        chat.text = "用户：你好，能介绍一下你吗？\n\n助手：你好！我是你的数字人助手，可以与你交流，也可以结合知识库回答问题。";
        SavePreview(camera, "dashboard", 1920, 1080);
        SavePreview(camera, "dashboard-small", 1280, 720);
        Check(owner.interfaceRoot.GetComponentsInChildren<ChatTextInputField>(true).Length == 1, "dashboard has one composer");
        Check(owner.interfaceRoot.GetComponentsInChildren<Text>(true).Any(t => t.text.Contains("麦克风")), "dashboard contains microphone feedback");
    }
    private static void SavePreview(Camera camera, string name, int width, int height)
    {
        string directory = Path.GetFullPath("ValidationArtifacts"); Directory.CreateDirectory(directory);
        var target = new RenderTexture(width, height, 24); target.Create(); camera.targetTexture = target;
        foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
        { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
        Canvas.ForceUpdateCanvases();
        foreach (var hub in UnityEngine.Object.FindObjectsOfType<AssistantSettingsHub>()) typeof(AssistantSettingsHub).GetMethod("Resize", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hub, null);
        Canvas.ForceUpdateCanvases();
        foreach (var layout in UnityEngine.Object.FindObjectsOfType<VerticalLayoutGroup>()) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)layout.transform);
        Canvas.ForceUpdateCanvases(); camera.Render();
        var prior = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG()); RenderTexture.active = prior; camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target);
    }
}

