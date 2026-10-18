using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Offline checks use synthetic credentials, a loopback fake server and an isolated settings file.
// No real subscription is contacted. Page previews are written to .work/ui-preview.
public static class AssistantUiValidation
{
    private const string PreviewFolder = ".work/ui-preview";
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static int checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name); checks++; Debug.Log("ASSISTANT_CHECK " + name);
    }
    public static async void Run()
    {
        try
        {
            TestMigration(); await TestCatalog(); await TestConnectionTester();
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

    // Minimal loopback HTTP server: answers every request with the configured status and body.
    private sealed class FakeServer : IDisposable
    {
        private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        public int status = 200;
        public string body = "{}";
        public string lastRequest = "";
        public string Url => "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
        public FakeServer() { listener.Start(); _ = Loop(); }
        private async Task Loop()
        {
            while (true)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(); } catch (Exception) { return; }
                using (client)
                {
                    var stream = client.GetStream(); var buffer = new byte[16384];
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                    lastRequest = Encoding.UTF8.GetString(buffer, 0, read);
                    var payload = Encoding.UTF8.GetBytes(body);
                    var head = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " Test\r\nContent-Type: application/json\r\nContent-Length: " + payload.Length + "\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(head, 0, head.Length); await stream.WriteAsync(payload, 0, payload.Length);
                }
            }
        }
        public void Dispose() => listener.Stop();
    }
    private static async Task TestConnectionTester()
    {
        var settings = new RuntimeVoiceChatSettings(); settings.EnsureDefaults();
        var result = await ServiceConnectionTester.TestAccountAsync(ServiceAccounts.New("AzureSpeech"), settings, CancellationToken.None);
        Check(!result.ok && result.message.Contains("请先填写") && result.message.Contains("API 密钥"), "missing credentials reported without a request");
        Check(ServiceConnectionTester.Describe(new InvalidOperationException("Azure Speech HTTP 401：请检查 Key、区域和资源权限。")).Contains("鉴权失败"), "HTTP 401 explained");
        Check(ServiceConnectionTester.Describe(new InvalidOperationException("XF TTS error 11200: licc failed")).Contains("11200"), "Xunfei error code explained");
        Check(ServiceConnectionTester.Describe(new HttpRequestException("No such host is known")).Contains("无法连接"), "network failure explained");
        Check(ServiceConnectionTester.Describe(new System.Net.WebSockets.WebSocketException("The server returned status code '401' when status code '101' was expected.")).Contains("鉴权失败"), "WebSocket handshake 401 explained");
        using (var server = new FakeServer())
        {
            var account = ServiceAccounts.New("Compatible"); account.Set("uri", server.Url + "/v1"); account.Set("apiKey", "synthetic-good");
            server.body = "{\"data\":[{\"id\":\"model-a\"},{\"id\":\"model-b\"}]}";
            result = await ServiceConnectionTester.TestAccountAsync(account, settings, CancellationToken.None);
            Check(result.ok && result.message.Contains("2"), "valid key accepted through the model list");
            Check(server.lastRequest.StartsWith("GET /v1/models") && server.lastRequest.Contains("Bearer synthetic-good"), "test request carries the drafted key");
            server.status = 401; server.body = "{\"error\":{\"message\":\"invalid api key\"}}";
            result = await ServiceConnectionTester.TestAccountAsync(account, settings, CancellationToken.None);
            Check(!result.ok && result.message.Contains("鉴权失败"), "rejected key reported");
            server.status = 404; server.body = "{}";
            settings.serviceAccounts.Add(account); settings.modelProfiles[0].serviceAccountId = account.id; settings.modelProfiles[0].model = "model-a";
            result = await ServiceConnectionTester.TestAccountAsync(account, settings, CancellationToken.None);
            Check(!result.ok && result.message.Contains("404") && server.lastRequest.StartsWith("POST /v1/chat/completions"), "no model list falls back to a chat request");
            var cancelled = new CancellationTokenSource(); cancelled.Cancel(); bool threw = false;
            try { await ServiceConnectionTester.TestAccountAsync(account, settings, cancelled.Token); } catch (OperationCanceledException) { threw = true; }
            Check(threw, "user cancellation is not reported as a failure");
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
        typeof(AssistantSettingsHub).GetMethod("SetStatus", Private).Invoke(hub, new object[] { "修改后点击“保存并应用”生效", UiTone.Neutral });
        string baseline = JsonConvert.SerializeObject(hub.Draft);
        var camera = new GameObject("PreviewCamera").AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(196, 205, 200, 255);
        SavePreview(camera, "providers", 1920, 1080);
        typeof(AssistantSettingsHub).GetField("category", Private).SetValue(hub, "语音合成");
        typeof(AssistantSettingsHub).GetMethod("ShowProviders", Private).Invoke(hub, null);
        SavePreview(camera, "providers-speech", 1920, 1080);
        typeof(AssistantSettingsHub).GetMethod("ShowModules", Private).Invoke(hub, null);
        SavePreview(camera, "modules", 1920, 1080);
        Check(JsonConvert.SerializeObject(hub.Draft) == baseline, "navigation does not mutate draft");
        typeof(AssistantSettingsHub).GetMethod("OpenModule", Private).Invoke(hub, new object[] { "听觉" });
        SavePreview(camera, "module-asr", 1920, 1080);
        owner.ShowPageByName("Page_TtsAzure"); SavePreview(camera, "speech", 1920, 1080);
        SavePreview(camera, "speech-small", 1280, 720);
        owner.ShowPageByName("Page_DeepSeek"); SavePreview(camera, "chat-model", 1920, 1080);
        owner.ShowPageByName("Page_Features"); SavePreview(camera, "general", 1920, 1080);
        owner.ShowPageByName("Page_ApiKeys"); SavePreview(camera, "providers-large", 2560, 1440);

        var azure = hub.Draft.serviceAccounts.First(a => a.kind == "AzureSpeech");
        azure.Set("apiKey", "synthetic0000000000000000000d0bd2"); azure.Set("region", "japaneast");
        string fingerprint = (string)typeof(AssistantSettingsHub).GetMethod("Fingerprint", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { azure });
        var recordType = typeof(AssistantSettingsHub).GetNestedType("TestRecord", BindingFlags.NonPublic);
        var record = Activator.CreateInstance(recordType, true);
        recordType.GetField("fingerprint").SetValue(record, fingerprint);
        recordType.GetField("result").SetValue(record, new ServiceTestResult { ok = false, message = "鉴权失败：密钥无效，或与区域 / App ID 不匹配。", detail = "Azure Speech HTTP 401：请检查 Key、区域和资源权限。", milliseconds = 412 });
        ((System.Collections.IDictionary)typeof(AssistantSettingsHub).GetField("tests", Private).GetValue(hub))[azure.id] = record;
        typeof(AssistantSettingsHub).GetMethod("EditAccount", Private).Invoke(hub, new object[] { azure });
        Check(owner.interfaceRoot.GetComponentsInChildren<InputField>(true).Any(i => i.contentType == InputField.ContentType.Password), "service secret masked");
        Check(owner.interfaceRoot.GetComponentsInChildren<Text>(true).Any(t => t.text == "测试连接"), "account page offers a connection test");
        Check(owner.interfaceRoot.GetComponentsInChildren<Text>(true).Any(t => t.text.Contains("鉴权失败")), "test result shown on the account page");
        SavePreview(camera, "account", 1920, 1080);
        typeof(AssistantSettingsHub).GetMethod("ShowAccounts", Private).Invoke(hub, new object[] { "AzureSpeech" });
        Check(owner.interfaceRoot.GetComponentsInChildren<Text>(true).Any(t => t.text == "测试未通过"), "account list shows the test status");
        SavePreview(camera, "accounts", 1920, 1080);
        Check(owner.interfaceRoot.GetComponentsInChildren<Canvas>(true).Length == 1, "one interface canvas");
        hub.RequestClose();
        Check(owner.interfaceRoot.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Confirmation"), "unsaved edits prompt");
        SavePreview(camera, "confirm", 1920, 1080);
        typeof(AssistantSettingsHub).GetMethod("CloseNow", Private).Invoke(hub, null);
        Check(!hub.IsOpen && !AssistantSettingsHub.ModalOpen, "close clears modal input block");

        var fakeHost = new GameObject("SyntheticController"); fakeHost.SetActive(false);
        var controller = fakeHost.AddComponent<VoiceChatController>();
        controller.enablePersistentConversation = false; controller.enableKnowledgeBase = false; controller.persistentDataFolder = "AssistantUiPreview";
        var history = (List<ChatCompletionService.ChatMessage>)typeof(VoiceChatController).GetField("history", Private).GetValue(controller);
        history.Add(new ChatCompletionService.ChatMessage("user", "你好，能介绍一下你自己吗？"));
        history.Add(new ChatCompletionService.ChatMessage("assistant", "你好！我是数字人助手小北。你可以问我入学、选课、校园生活等问题，我会结合知识库给你解答。"));
        history.Add(new ChatCompletionService.ChatMessage("user", "图书馆几点开门？"));
        history.Add(new ChatCompletionService.ChatMessage("assistant", "图书馆周一至周五 8:00–22:00 开放，周末 9:00–21:00；考试周会延长到 23:00。"));
        history.Add(new ChatCompletionService.ChatMessage("user", "好的，谢谢"));
        var canvas = owner.interfaceRoot.GetComponentsInChildren<Canvas>(true).Single();
        var dashboard = owner.gameObject.AddComponent<AssistantDashboard>(); dashboard.Build(canvas.transform, controller, hub.Open);
        SavePreview(camera, "dashboard", 1920, 1080, dashboard);
        Check(owner.interfaceRoot.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Bubble") == 5, "conversation rendered as bubbles");
        SavePreview(camera, "dashboard-small", 1280, 720, dashboard);
        SavePreview(camera, "dashboard-large", 2560, 1440, dashboard);
        history.Clear(); dashboard.Refresh();
        SavePreview(camera, "dashboard-empty", 1920, 1080, dashboard);
        Check(owner.interfaceRoot.GetComponentsInChildren<ChatTextInputField>(true).Length == 1, "dashboard has one composer");
        Check(owner.interfaceRoot.GetComponentsInChildren<Transform>(true).Any(t => t.name == "InputLevel"), "dashboard contains microphone feedback");
    }
    private static void SavePreview(Camera camera, string name, int width, int height, AssistantDashboard dashboard = null)
    {
        string directory = Path.GetFullPath(PreviewFolder); Directory.CreateDirectory(directory);
        var target = new RenderTexture(width, height, 24); target.Create(); camera.targetTexture = target;
        foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
        { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
        for (int pass = 0; pass < 2; pass++)
        {
            Canvas.ForceUpdateCanvases();
            foreach (var scaler in UnityEngine.Object.FindObjectsOfType<CanvasScaler>()) typeof(CanvasScaler).GetMethod("Handle", Private).Invoke(scaler, null);
            foreach (var hub in UnityEngine.Object.FindObjectsOfType<AssistantSettingsHub>())
            {
                typeof(AssistantSettingsHub).GetMethod("Resize", Private).Invoke(hub, null);
                // Pages lay out grids for the width at build time; rebuild once the preview size is applied.
                if (pass == 0 && hub.IsOpen) (typeof(AssistantSettingsHub).GetField("reopen", Private).GetValue(hub) as Action)?.Invoke();
            }
            if (dashboard != null) { typeof(AssistantDashboard).GetMethod("Resize", Private).Invoke(dashboard, null); dashboard.Refresh(); }
            Canvas.ForceUpdateCanvases();
            foreach (var layout in UnityEngine.Object.FindObjectsOfType<LayoutGroup>()) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)layout.transform);
            foreach (var shadow in UnityEngine.Object.FindObjectsOfType<UiShadowFollow>()) shadow.Sync();
        }
        Canvas.ForceUpdateCanvases(); camera.Render();
        var prior = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false); image.ReadPixels(new UnityEngine.Rect(0, 0, width, height), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG()); RenderTexture.active = prior; camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target);
    }
}
