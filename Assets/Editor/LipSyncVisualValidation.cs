using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LipSyncVisualValidation
{
    public static async void Render()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/main.unity");
            Directory.CreateDirectory(LipSyncProjectTools.Output + "/poses");
            var switcher = UnityEngine.Object.FindObjectOfType<CharacterModelSwitcher>(true);
            AlignedSpeechSegment sample = File.Exists(LipSyncProjectTools.Output + "/live-sample.json")
                ? Newtonsoft.Json.JsonConvert.DeserializeObject<AlignedSpeechSegment>(File.ReadAllText(LipSyncProjectTools.Output + "/live-sample.json")) : null;
            foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>()) light.enabled = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.65f, .65f, .65f);
            foreach (var root in switcher.modelRoots) root.SetActive(false);
            foreach (var root in switcher.modelRoots)
            {
                if (root.GetComponent<VrmMouthDriver>() != null) continue; // Covered by VrmCharacterValidation.
                root.SetActive(true);
                var driver = root.GetComponent<AlignedMouthDriver>();
                var head = root.GetComponentsInChildren<Transform>().First(t => t.name == "CC_Base_Head");
                var body = root.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.name == "CC_Base_Body");
                float height = body.bounds.size.y;
                var camera = new GameObject("Mouth validation camera").AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.18f, .20f, .23f);
                camera.fieldOfView = 32; camera.nearClipPlane = .001f; camera.farClipPlane = 1000;
                camera.cullingMask = 1 << 31;
                var key = new GameObject("Mouth validation light").AddComponent<Light>(); key.type = LightType.Directional; key.intensity = 1.2f;
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
                var target = head.position + root.transform.up * (height * .015f);
                for (int side = 0; side < 2; ++side)
                {
                    Vector3 direction = Quaternion.AngleAxis(side * 60, root.transform.up) * root.transform.forward;
                    camera.transform.position = target + direction * (height * .32f);
                    camera.transform.LookAt(target, root.transform.up);
                    key.transform.rotation = camera.transform.rotation * Quaternion.Euler(20, -25, 0);
                    for (int pose = 0; pose < (sample?.frames != null ? 12 : 4); ++pose)
                    {
                        var weights = new float[55];
                        if (pose == 1) weights[17] = .55f;
                        if (pose == 2) { weights[17] = .2f; weights[19] = .25f; weights[20] = .75f; }
                        if (pose == 3) { weights[17] = .15f; weights[29] = weights[30] = .55f; }
                        if (pose >= 4 && pose < 8)
                        {
                            int channel = new[] { 17, 20, 18, 29 }[pose - 4];
                            int frame = Enumerable.Range(0, sample.frames.Length).OrderByDescending(i => sample.frames[i][channel]).First();
                            sample.Sample(frame / 60.0, weights);
                        }
                        if (pose >= 8) sample.Sample(new[] { .125, .225, 1.25, 2.9 }[pose - 8], weights);
                        driver.Apply(weights); driver.Render(0);
                        EditorApplication.QueuePlayerLoopUpdate();
                        await System.Threading.Tasks.Task.Delay(150);
                        Capture(camera, LipSyncProjectTools.Output + "/poses/" + driver.profile.modelName + "-" + side + "-" + pose + ".png");
                    }
                }
                driver.Release(true); root.SetActive(false); UnityEngine.Object.DestroyImmediate(camera.gameObject); UnityEngine.Object.DestroyImmediate(key.gameObject);
            }
            Debug.Log("LIPSYNC_POSE_RENDER_PASS liveSample=" + (sample != null)); EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    private static void Capture(Camera camera, string file)
    {
        var rt = new RenderTexture(640, 640, 24); var image = new Texture2D(640, 640, TextureFormat.RGB24, false);
        var old = RenderTexture.active;
        try { camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 640, 640), 0, 0); image.Apply(); File.WriteAllBytes(file, image.EncodeToPNG()); }
        finally { RenderTexture.active = old; camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(rt); }
    }
}
