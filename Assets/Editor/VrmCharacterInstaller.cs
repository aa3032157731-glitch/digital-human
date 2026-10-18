using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UniVRM10;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Adds every .vrm in Assets/MyCharacter/VRM to the main scene's CharacterModelSwitcher with vowel lip sync.
// Re-running updates the existing VRM roots instead of duplicating them.
public static class VrmCharacterInstaller
{
    public const string VrmFolder = "Assets/MyCharacter/VRM";
    private const string ScenePath = "Assets/Scenes/main.unity";
    // UniGLTF.ImporterRenderPipelineTypes.UniversalRenderPipeline; the importer type lives in a non-auto-referenced editor assembly.
    private const int UniversalRenderPipeline = 2;

    [MenuItem("Tools/Voice Chat/Install VRM Characters")]
    public static void InstallMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        try { EditorUtility.DisplayDialog("VRM 角色", Install(VrmFolder, true), "确定"); }
        catch (Exception e) { Debug.LogException(e); EditorUtility.DisplayDialog("VRM 角色", e.Message, "确定"); }
    }

    public static void InstallBatch()
    {
        try { Debug.Log(Install(VrmFolder, true)); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    public static string Install(string folder, bool saveScene)
    {
        var paths = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.vrm").Select(p => p.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal).ToList()
            : new List<string>();
        if (paths.Count == 0) throw new InvalidOperationException("没有找到 VRM 文件，请把 .vrm 放到 " + folder + " 后再运行。");
        foreach (var path in paths) UseUniversalPipelineMaterials(path);

        var scene = EditorSceneManager.OpenScene(ScenePath);
        var switcher = UnityEngine.Object.FindObjectOfType<CharacterModelSwitcher>(true);
        var controller = UnityEngine.Object.FindObjectOfType<VoiceChatController>(true);
        if (switcher == null || controller == null) throw new InvalidOperationException("主场景缺少语音控制器或模型切换器。");

        var roots = (switcher.modelRoots ?? Array.Empty<GameObject>()).Where(r => r != null).ToList();
        var names = roots.Select(r => DisplayName(switcher, r)).ToList();
        var reference = roots.FirstOrDefault(r => r.GetComponent<Vrm10Instance>() == null);
        var referenceAnimator = reference != null ? reference.GetComponentInChildren<Animator>(true) : null;
        var talkController = referenceAnimator != null ? HumanoidOnly(referenceAnimator.runtimeAnimatorController) : null;
        var report = new StringBuilder();

        foreach (var path in paths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || prefab.GetComponent<Vrm10Instance>() == null)
            { report.AppendLine("跳过 " + path + "：导入失败或不是 VRM。"); continue; }
            string name = Path.GetFileNameWithoutExtension(path);
            int index = roots.FindIndex(r => r.name == name && r.GetComponent<Vrm10Instance>() != null);
            GameObject root;
            if (index < 0)
            {
                root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                root.name = name;
                if (reference != null)
                {
                    root.transform.SetParent(reference.transform.parent, false);
                    root.transform.SetPositionAndRotation(reference.transform.position, reference.transform.rotation);
                }
                roots.Add(root); names.Add(name); index = roots.Count - 1;
            }
            else root = roots[index];

            var animator = root.GetComponent<Animator>();
            if (animator == null) animator = root.AddComponent<Animator>();
            if (talkController != null) animator.runtimeAnimatorController = talkController;
            if (referenceAnimator != null) { animator.applyRootMotion = referenceAnimator.applyRootMotion; animator.cullingMode = referenceAnimator.cullingMode; }
            // VRM skinned meshes keep their bounds relative to the static "Root" bone, while the reused talk/idle clips
            // move the hips by ~0.3 m; a camera framing only the face would then cull it. Recompute bounds per frame.
            foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            { skinned.updateWhenOffscreen = true; EditorUtility.SetDirty(skinned); }
            var driver = root.GetComponent<VrmMouthDriver>();
            if (driver == null) driver = root.AddComponent<VrmMouthDriver>();
            driver.audioSource = controller.ttsAudioSource;
            root.SetActive(index == switcher.currentModelIndex);
            EditorUtility.SetDirty(root); EditorUtility.SetDirty(animator); EditorUtility.SetDirty(driver);

            var vowels = prefab.GetComponent<Vrm10Instance>().Vrm.Expression;
            int vowelCount = new[] { vowels.Aa, vowels.Ih, vowels.Ou, vowels.Ee, vowels.Oh }.Count(e => e != null);
            report.AppendLine(name + "：口型元音 " + vowelCount + "/5，说话动作 " + (talkController != null ? talkController.name : "无（参考角色的动画不是 Humanoid）"));
        }

        switcher.modelRoots = roots.ToArray();
        switcher.modelDisplayNames = names.ToArray();
        switcher.RefreshButtonLabel();
        EditorUtility.SetDirty(switcher);
        EditorSceneManager.MarkSceneDirty(scene);
        if (saveScene) EditorSceneManager.SaveScene(scene);
        return "VRM_INSTALL_PASS 已加入模型切换（共 " + roots.Count + " 个角色）：\n" + report;
    }

    private static void UseUniversalPipelineMaterials(string path)
    {
        var importer = AssetImporter.GetAtPath(path);
        if (importer == null) { AssetDatabase.ImportAsset(path); importer = AssetImporter.GetAtPath(path); }
        if (importer != null && ForceUniversalPipeline(importer)) importer.SaveAndReimport();
    }

    // The importer's "Auto" mode follows the active pipeline asset; when that asset fails to load it falls back to
    // built-in shaders that this URP project does not include, and the import fails. Only "Auto" is overridden.
    internal static bool ForceUniversalPipeline(AssetImporter importer)
    {
        var property = new SerializedObject(importer).FindProperty("RenderPipeline");
        if (property == null || property.intValue != 0) return false;
        property.intValue = UniversalRenderPipeline;
        property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    private sealed class ImportDefaults : AssetPostprocessor
    {
        private void OnPreprocessAsset()
        {
            if (assetPath.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase)) ForceUniversalPipeline(assetImporter);
        }
    }

    private static string DisplayName(CharacterModelSwitcher switcher, GameObject root)
    {
        int i = Array.IndexOf(switcher.modelRoots, root);
        return switcher.modelDisplayNames != null && i >= 0 && i < switcher.modelDisplayNames.Length && !string.IsNullOrWhiteSpace(switcher.modelDisplayNames[i])
            ? switcher.modelDisplayNames[i] : root.name;
    }

    // Humanoid clips retarget onto the VRM skeleton; generic clips would target CC bone paths that do not exist.
    private static RuntimeAnimatorController HumanoidOnly(RuntimeAnimatorController controller)
    {
        if (controller == null) return null;
        var clips = controller.animationClips;
        return clips.Length > 0 && clips.All(c => c != null && c.humanMotion) ? controller : null;
    }
}
