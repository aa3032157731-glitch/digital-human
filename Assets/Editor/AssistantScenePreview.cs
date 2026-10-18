using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Renders the real main scene in play mode with the redesigned UI. Uses an isolated sample settings file
// (no credentials, no persistent chat history), so the user's configuration and conversations are untouched.
// Usage: -executeMethod AssistantScenePreview.Run ; output in .work/ui-preview/scene-*.png
[InitializeOnLoad]
public static class AssistantScenePreview
{
    private const string Flag = "AssistantScenePreview";
    private const string SettingsFile = "Temp/assistant-scene-preview.json";
    private const string Output = ".work/ui-preview";

    static AssistantScenePreview()
    {
        // The settings override is static and is lost on the domain reload that precedes play mode.
        if (SessionState.GetBool(Flag, false)) UseIsolatedSettings();
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Flag, false))
            { SessionState.SetBool(Flag, false); Capture(); }
        };
    }

    private static void UseIsolatedSettings() =>
        typeof(VoiceChatRuntimeConfigStore).GetField("EditorSettingsPathOverride", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, Path.GetFullPath(SettingsFile));

    public static void Run()
    {
        try
        {
            var settings = new RuntimeVoiceChatSettings(); settings.EnsureDefaults();
            settings.memory.enablePersistentConversation = false; settings.memory.persistentDataFolder = "AssistantUiPreview";
            settings.knowledge.enableKnowledgeBase = false; settings.features.listenOnStart = false;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(SettingsFile)));
            UseIsolatedSettings();
            if (!VoiceChatRuntimeConfigStore.SaveSettings(settings)) throw new Exception("could not write preview settings");
            EditorSceneManager.OpenScene("Assets/Scenes/main.unity");
            SessionState.SetBool(Flag, true); EditorApplication.EnterPlaymode();
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    private static async void Capture()
    {
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            await Frames(30);
            var controller = UnityEngine.Object.FindObjectOfType<VoiceChatController>();
            var history = (List<ChatCompletionService.ChatMessage>)typeof(VoiceChatController).GetField("history", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller);
            history.Add(new ChatCompletionService.ChatMessage("user", "你好，你是谁？"));
            history.Add(new ChatCompletionService.ChatMessage("assistant", "你好！我是小北，大学咨询大厅的数字人助手。入学、选课、校园生活的问题都可以问我。"));
            history.Add(new ChatCompletionService.ChatMessage("user", "图书馆几点开门？"));
            history.Add(new ChatCompletionService.ChatMessage("assistant", "图书馆周一至周五 8:00–22:00 开放，周末 9:00–21:00；考试周会延长到 23:00。"));
            var dashboard = UnityEngine.Object.FindObjectOfType<AssistantDashboard>();
            await Frames(10); dashboard.Refresh(); await Frames(10);
            Render("scene-main");
            var switcher = UnityEngine.Object.FindObjectOfType<CharacterModelSwitcher>();
            for (int i = 0; i < switcher.modelRoots.Length; i++)
            {
                if (switcher.modelRoots[i].GetComponent<VrmMouthDriver>() == null) continue;
                switcher.SetModel(i); await Frames(30); Render("scene-" + switcher.GetCurrentModelName()); break;
            }
            UnityEngine.Object.FindObjectOfType<AdvancedSettingsPanel>().OpenPanel();
            await Frames(10); Render("scene-settings");
            Debug.Log("SCENE_PREVIEW_PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    private static async Task Frames(int count)
    {
        for (int start = Time.frameCount; Time.frameCount < start + count;) await Task.Yield();
    }

    // Batch mode has no game window: draw the main camera and all overlay canvases into a render texture.
    private static void Render(string name)
    {
        var camera = Camera.main;
        var target = new RenderTexture(1920, 1080, 24); target.Create();
        var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
        var modes = new Dictionary<Canvas, RenderMode>();
        foreach (var canvas in canvases)
        {
            if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            modes[canvas] = canvas.renderMode; canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
            canvas.planeDistance = camera.nearClipPlane + .05f + canvas.sortingOrder * .0001f;
        }
        camera.targetTexture = target;
        Canvas.ForceUpdateCanvases();
        foreach (var scaler in UnityEngine.Object.FindObjectsOfType<CanvasScaler>()) typeof(CanvasScaler).GetMethod("Handle", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(scaler, null);
        Canvas.ForceUpdateCanvases();
        // Re-run the panels' own sizing for the capture resolution (they normally do this every frame).
        foreach (var dashboard in UnityEngine.Object.FindObjectsOfType<AssistantDashboard>())
        { typeof(AssistantDashboard).GetMethod("Resize", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(dashboard, null); dashboard.Refresh(); }
        foreach (var hub in UnityEngine.Object.FindObjectsOfType<AssistantSettingsHub>()) typeof(AssistantSettingsHub).GetMethod("Resize", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hub, null);
        Canvas.ForceUpdateCanvases();
        foreach (var layout in UnityEngine.Object.FindObjectsOfType<LayoutGroup>()) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)layout.transform);
        foreach (var shadow in UnityEngine.Object.FindObjectsOfType<UiShadowFollow>()) shadow.Sync();
        Canvas.ForceUpdateCanvases();
        camera.Render();
        var prior = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false); image.ReadPixels(new UnityEngine.Rect(0, 0, 1920, 1080), 0, 0); image.Apply();
        Directory.CreateDirectory(Output); File.WriteAllBytes(Path.Combine(Output, name + ".png"), image.EncodeToPNG());
        RenderTexture.active = prior; camera.targetTexture = null;
        foreach (var pair in modes) pair.Key.renderMode = pair.Value;
        UnityEngine.Object.Destroy(image); UnityEngine.Object.Destroy(target);
    }
}
