using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Run uses an isolated store and local mock. RunLiveConfigured sends a separate short connection test.
public static class ChatFeatureValidation
{
    public static async void RunLiveConfigured()
    {
        try
        {
            var settings = VoiceChatRuntimeConfigStore.LoadSettings();
            if (settings == null) throw new InvalidOperationException("没有本机配置。");
            var profile = settings.modelProfiles.Find(p => p.id == settings.activeModelProfileId).Copy();
            profile.Validate();
            profile.enableTools = false;
            using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                var service = new ChatCompletionService(profile, "这是连接测试，请仅回复 OK。");
                var reply = await service.CompleteChatAsync(new List<ChatCompletionService.ChatMessage>
                    { new ChatCompletionService.ChatMessage("user", "请回复 OK。") }, cancellation.Token);
                if (string.IsNullOrWhiteSpace(reply.content)) throw new InvalidOperationException("接口未返回正文。");
            }
            Debug.Log("LIVE_CONFIGURED_ENDPOINT_PASS host=" + new Uri(profile.Endpoint).Host);
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            Debug.LogError("LIVE_CONFIGURED_ENDPOINT_FAILED " + (ex is InvalidOperationException ? ex.Message : "连接失败或超时"));
            EditorApplication.Exit(1);
        }
    }

    private static int checks;
    private static VoiceChatController controller;
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }

    private static ChatModelProfile Mock(string model = "stream") => new ChatModelProfile
        { uri = "http://127.0.0.1:18769", apiKey = "mock-only", model = model };

    public static async void Run()
    {
        try
        {
            typeof(VoiceChatRuntimeConfigStore).GetField("EditorSettingsPathOverride", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, Path.GetFullPath("../garbage/test-settings-" + Guid.NewGuid().ToString("N") + ".json"));
            var legacy = JsonUtility.FromJson<RuntimeVoiceChatSettings>("{\"schemaVersion\":3,\"deepSeek\":{\"apiKey\":\"mock-only\",\"model\":\"legacy\",\"chatCompletionsUrl\":\"http://localhost:18769\"}}");
            legacy.EnsureDefaults();
            Check(legacy.schemaVersion == 4 && legacy.modelProfiles.Count == 1 && legacy.modelProfiles[0].model == "legacy", "Legacy migration");
            Check(legacy.readRepliesAloud, "Legacy default read-aloud");
            var id = legacy.activeModelProfileId;
            legacy.EnsureDefaults();
            Check(legacy.modelProfiles.Count == 1 && legacy.activeModelProfileId == id, "Idempotent migration");
            legacy.modelProfiles.Add(Mock());
            legacy.activeModelProfileId = legacy.modelProfiles[1].id;
            var roundtrip = JsonUtility.FromJson<RuntimeVoiceChatSettings>(JsonUtility.ToJson(legacy));
            roundtrip.EnsureDefaults();
            Check(roundtrip.modelProfiles.Count == 2 && roundtrip.activeModelProfileId == legacy.activeModelProfileId, "Profile persistence");
            Check(Mock().Endpoint == "http://127.0.0.1:18769/chat/completions", "Base URI normalization");
            Check(new ChatModelProfile { uri = "https://example.com/api/v3/chat/completions/" }.Endpoint == "https://example.com/api/v3/chat/completions", "Full URI normalization");
            Check(new ChatModelProfile { uri = "https://example.com/v1" }.Endpoint == "https://example.com/v1/chat/completions", "Versioned URI normalization");
            var profile = Mock();
            var service = new ChatCompletionService(profile, "test");
            profile.uri = "https://invalid.example";
            var messages = new List<ChatCompletionService.ChatMessage> { new ChatCompletionService.ChatMessage("user", "test") };
            var build = typeof(ChatCompletionService).GetMethod("BuildRequestBody", Private);
            var body = (JObject)build.Invoke(service, new object[] { messages, null, true, null, null });
            Check(body["thinking"] == null && body["temperature"] == null && body["max_tokens"] == null && body["tools"] == null, "Minimal compatible payload");
            var advanced = Mock();
            advanced.preset = "DeepSeek"; advanced.disableThinking = true;
            advanced.sendSampling = true; advanced.sendTokenLimit = true; advanced.useMaxCompletionTokens = true;
            var advancedBody = (JObject)build.Invoke(new ChatCompletionService(advanced, ""), new object[] { messages, null, false, null, null });
            Check(advancedBody["thinking"] != null && advancedBody["temperature"] != null && advancedBody["max_completion_tokens"] != null, "Advanced options opt in");
            advanced.preset = "Compatible";
            var genericBody = (JObject)build.Invoke(new ChatCompletionService(advanced, ""), new object[] { messages, null, false, null, null });
            Check(genericBody["thinking"] == null, "Provider options isolated");
            var deltaParser = typeof(ChatCompletionService).GetMethod("ParseDelta", BindingFlags.NonPublic | BindingFlags.Static);
            Check(deltaParser.Invoke(null, new object[] { "{\"choices\":[{\"delta\":{\"reasoning_content\":\"private reasoning\"}}]}" }) == null, "Reasoning excluded from visible reply");
            var output = new StringBuilder();
            await service.StreamChatAsync(messages, s => output.Append(s), CancellationToken.None);
            Check(output.ToString() == "hello world", "SSE parsing and immutable profile snapshot");
            output.Clear();
            await new ChatCompletionService(Mock("json"), "").StreamChatAsync(messages, s => output.Append(s), CancellationToken.None);
            Check(output.ToString() == "hello world", "JSON response fallback");
            var complete = await service.CompleteChatAsync(messages, CancellationToken.None);
            Check(complete.content == "hello world", "Nonstream completion");
            try
            {
                await new ChatCompletionService(Mock("unauthorized"), "").CompleteChatAsync(messages, CancellationToken.None);
                throw new Exception("Expected authentication failure");
            }
            catch (InvalidOperationException ex) { Check(ex.Message.Contains("401") && !ex.Message.Contains("mock-only"), "Safe auth error"); }
            using (var cancel = new CancellationTokenSource(200))
            {
                var start = DateTime.UtcNow;
                try { await new ChatCompletionService(Mock("slow"), "").StreamChatAsync(messages, _ => { }, cancel.Token); }
                catch (Exception) when (cancel.IsCancellationRequested) { }
                Check((DateTime.UtcNow - start).TotalSeconds < 2, "Streaming cancellation unblocks read");
            }

            EditorSceneManager.OpenScene("Assets/Scenes/main.unity");
            controller = UnityEngine.Object.FindObjectOfType<VoiceChatController>(true);
            Check(controller != null, "Scene controller");
            controller.persistentDataFolder = Path.GetFullPath("../garbage/chat-validation-memory-" + Guid.NewGuid().ToString("N"));
            controller.enablePersistentConversation = true;
            controller.loadLatestConversationOnStart = false;
            controller.enableKnowledgeBase = false;
            controller.refreshKnowledgeBaseOnStart = false;
            controller.enableAgentTools = false;
            controller.readRepliesAloud = false;
            controller.modelProfiles = new List<ChatModelProfile> { Mock("slow"), Mock() };
            controller.activeModelProfileId = controller.modelProfiles[0].id;
            typeof(VoiceChatController).GetMethod("InitializeMemoryStores", Private).Invoke(controller, null);
            EditorApplication.update += Pump;
            Check(!controller.SubmitTextMessage("  "), "Blank submission ignored");
            Check(controller.SubmitTextMessage("first test message"), "Text accepted");
            await Task.Delay(450);
            controller.activeModelProfileId = controller.modelProfiles[1].id;
            Check(controller.SubmitTextMessage("second test message"), "Interrupt and replace");
            await WaitForAnswer();
            Check(controller.currentAssistantText == "hello world", "No stale deltas after interruption");
            var history = controller.FormatConversationText(80);
            Check(history.Contains("first test message") && history.Contains("second test message"), "History preserved across model switch");
            Check(history.Contains("interrupted"), "Partial response retained as interrupted");
            Check(history.Split(new[] { "second test message" }, StringSplitOptions.None).Length == 2, "User message not duplicated");
            controller.readRepliesAloud = true;
            controller.ttsProvider = TTSProvider.Doubao;
            controller.doubaoTtsConfig = null;
            Check(controller.SubmitTextMessage("audio failure test"), "Text with unavailable TTS accepted");
            await WaitForAnswer();
            Check(controller.currentAssistantText == "hello world" && controller.ChatStatus.Contains("朗读不可用"), "TTS failure preserves text");
            var memory = UnityEngine.Object.FindObjectOfType<VoiceChatMemoryPanel>(true);
            typeof(VoiceChatMemoryPanel).GetMethod("InitializeUi", Private).Invoke(memory, null);
            Check(memory.GetComponent<TextChatComposer>() != null, "Composer attached");
            Check(memory.expandedHeight >= 782, "Composer has reserved layout height");
            var settings = UnityEngine.Object.FindObjectOfType<AdvancedSettingsPanel>(true);
            typeof(AdvancedSettingsPanel).GetMethod("Initialize", Private).Invoke(settings, null);
            settings.OpenPanel();
            settings.ShowPageByName("Page_DeepSeek");
            Check(UnityEngine.Object.FindObjectOfType<ModelProfilesPanel>(true) != null, "Model editor attached");
            Canvas.ForceUpdateCanvases();
            var inputs = UnityEngine.Object.FindObjectsOfType<InputField>(true);
            Check(Array.Exists(inputs, input => input is ChatTextInputField), "Multiline input exists");
            Check(Array.Exists(inputs, input => input.name == "API 密钥" && input.contentType == InputField.ContentType.Password), "Secret field masked");
            var editor = UnityEngine.Object.FindObjectOfType<ModelProfilesPanel>(true);
            var editedSettings = (RuntimeVoiceChatSettings)typeof(ModelProfilesPanel).GetField("settings", Private).GetValue(editor);
            int originalCount = editedSettings.modelProfiles.Count;
            typeof(ModelProfilesPanel).GetMethod("Add", Private).Invoke(editor, new object[] { "Ark" });
            Check(editedSettings.modelProfiles.Count == originalCount + 1 && editedSettings.modelProfiles[originalCount].preset == "Ark", "Add Ark preset");
            typeof(ModelProfilesPanel).GetMethod("Delete", Private).Invoke(editor, null);
            Check(editedSettings.modelProfiles.Count == originalCount, "Delete draft profile");
            memory.RefreshAll();
            SaveUiPreview("../garbage/model-profiles-preview.png");
            settings.ClosePanel();
            SaveUiPreview("../garbage/text-chat-preview.png");
            Debug.Log("CHAT_FEATURE_VALIDATION_PASS checks=" + checks);
            EditorApplication.update -= Pump;
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorApplication.update -= Pump;
            EditorApplication.Exit(1);
        }
    }

    private static void Pump()
    {
        if (controller != null) typeof(VoiceChatController).GetMethod("Update", Private).Invoke(controller, null);
    }

    private static async Task WaitForAnswer()
    {
        var until = DateTime.UtcNow.AddSeconds(12);
        while (controller.IsGenerating && DateTime.UtcNow < until) await Task.Delay(50);
        Pump();
        Check(!controller.IsGenerating, "Turn completes");
    }

    private static void SaveUiPreview(string path)
    {
        var camera = Camera.main ?? UnityEngine.Object.FindObjectOfType<Camera>();
        if (camera == null) return;
        var target = new RenderTexture(1920, 1080, 24);
        camera.targetTexture = target;
        foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
        {
            if (!canvas.isRootCanvas) continue;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = camera.nearClipPlane + 0.1f;
        }
        Canvas.ForceUpdateCanvases();
        camera.Render();
        var prior = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.GetFullPath(path), image.EncodeToPNG());
        RenderTexture.active = prior;
        camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(target);
    }
}
