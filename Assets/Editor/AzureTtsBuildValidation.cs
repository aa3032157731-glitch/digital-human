using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class AzureTtsBuildValidation
{
    public static void Preview()
    {
        // Use isolated settings; never render or change the user's saved credentials.
        var file = Path.GetFullPath("../garbage/azure-preview-settings.json");
        typeof(VoiceChatRuntimeConfigStore).GetField("EditorSettingsPathOverride", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, file);
        var settings = new RuntimeVoiceChatSettings(); settings.EnsureDefaults();
        settings.azureTts.region = "eastasia"; settings.azureTts.style = "cheerful";
        File.WriteAllText(file, JsonUtility.ToJson(settings));
        EditorSceneManager.OpenScene("Assets/Scenes/main.unity");
        var panel = UnityEngine.Object.FindObjectOfType<AdvancedSettingsPanel>(true);
        panel.OpenPanel(); panel.ShowPageByName("Page_TtsAzure");
        SavePreview("../garbage/azure-settings-preview.png");
        panel.ShowPageByName("Page_AudioBuffer"); SavePreview("../garbage/audio-buffer-preview.png");
        Debug.Log("AZURE_UI_PREVIEW_PASS");
    }
    private static void SavePreview(string path)
    {
        var camera = Camera.main ?? UnityEngine.Object.FindObjectOfType<Camera>();
        if (camera == null) throw new Exception("No scene camera");
        var target = new RenderTexture(1920, 1080, 24); camera.targetTexture = target;
        foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
        {
            if (!canvas.isRootCanvas) continue;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = camera.nearClipPlane + 0.1f;
        }
        Canvas.ForceUpdateCanvases(); camera.Render();
        var prior = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply(); File.WriteAllBytes(Path.GetFullPath(path), image.EncodeToPNG());
        RenderTexture.active = prior; camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target);
    }
    public static void Build()
    {
        // Discard any unsaved preview/test scene changes before building saved scenes.
        EditorSceneManager.OpenScene("Assets/Scenes/Begin.unity");
        var path = Path.GetFullPath("../garbage/AzureWindows/CamelDigitalman.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = path, target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
        });
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("Windows build failed: " + report.summary.result);
        Debug.Log("AZURE_WINDOWS_BUILD_PASS bytes=" + report.summary.totalSize);
    }
}
