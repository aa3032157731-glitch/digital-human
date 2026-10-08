using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace FactoryLab.Editor
{
    public static class FactoryLabTools
    {
        public const string ScenePath = "Assets/FactoryLab/Scenes/FactoryLab.unity";
        private const string DataPath = "Assets/FactoryLab/Data/";
        private const string Evidence = ".work/factory-lab";

        [MenuItem("Tools/Factory Lab/Generate scene and validate")]
        public static void GenerateAndValidate()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(DataPath); Directory.CreateDirectory("Assets/FactoryLab/Scenes");
            Directory.CreateDirectory("Assets/FactoryLab/Materials"); Directory.CreateDirectory(Evidence);
            foreach (string name in new[] { "factory-case.json", "layouts.json" })
                File.Copy("Plan/demoPlan/configs/" + name, DataPath + name, true);
            AssetDatabase.Refresh();
            Validate();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var config = Read();
            var colors = new[] { new Color(.84f, .87f, .86f), new Color(.96f, .96f, .91f), new Color(.36f, .45f, .49f),
                FactoryGeometry.Teal, new Color(.98f, .67f, .19f), FactoryGeometry.Navy, new Color(.66f, .43f, .23f), new Color(.98f, .68f, .17f) };
            var materials = new Material[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                string path = "Assets/FactoryLab/Materials/Palette" + i + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    var shader = Shader.Find(i == 7 ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
                    if (shader == null) throw new Exception("Missing URP shader");
                    material = new Material(shader); AssetDatabase.CreateAsset(material, path);
                }
                material.color = colors[i]; if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .2f);
                EditorUtility.SetDirty(material); materials[i] = material;
            }
            var root = new GameObject("FactoryLab");
            var geometry = FactoryGeometry.Empty(root.transform, "Generated factory", Vector3.zero);
            var views = FactoryGeometry.Build(geometry, config, materials);
            var camera = new GameObject("Factory Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.tag = "MainCamera"; camera.transform.SetParent(root.transform);
            camera.backgroundColor = new Color(.89f, .92f, .93f); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.fieldOfView = 42; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            camera.rect = new Rect(.21f, .24f, .78f, .57f);
            var background = new GameObject("Interface background", typeof(Camera)).GetComponent<Camera>();
            background.transform.SetParent(root.transform); background.depth = -10;
            background.cullingMask = 0; background.clearFlags = CameraClearFlags.SolidColor;
            background.backgroundColor = camera.backgroundColor;
            camera.transform.position = new Vector3(13, 13, -9); camera.transform.LookAt(new Vector3(6, 0, 4));
            var light = new GameObject("Factory daylight", typeof(Light)).GetComponent<Light>();
            light.transform.SetParent(root.transform); light.type = LightType.Directional; light.intensity = 1.25f;
            light.shadows = LightShadows.Soft; light.transform.rotation = Quaternion.Euler(48, -30, 0);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.7f, .76f, .8f);
            RenderSettings.skybox = null; RenderSettings.fog = false;
            var controller = root.AddComponent<FactoryLabController>();
            controller.caseData = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath + "factory-case.json");
            controller.layoutData = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath + "layouts.json");
            controller.palette = materials; controller.sceneCamera = camera; controller.views = views;
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.SaveAssets();
            if (views.Length != 6 || views.Any(v => v.inputSlots.Length != 4 || v.outputSlot == null || v.workSlot == null)) throw new Exception("Scene slot binding failed");
            Debug.Log("FACTORY_GENERATE_PASS scene=" + ScenePath);
        }

        private static FactoryConfiguration Read(string layout = "baseline") => FactoryConfiguration.Read(File.ReadAllText(DataPath + "factory-case.json"), File.ReadAllText(DataPath + "layouts.json"), layout);
        private static void Check(bool value, string message) { if (!value) throw new Exception("FACTORY TEST: " + message); }
        private static void Near(double actual, double expected, string message) => Check(Math.Abs(actual - expected) < 1e-6, message + " actual=" + actual + " expected=" + expected);

        public static void Validate()
        {
            Directory.CreateDirectory(Evidence);
            var checks = new List<string>(); var runs = new List<object>();
            foreach (var layout in new[] { "baseline", "reference-improved" })
            {
                var config = Read(layout);
                Near(config.ProductDistance(), layout == "baseline" ? 34 : 18, "handoff route distance");
                var single = Finish(config, 1, .13);
                Near(single.Time, layout == "baseline" ? 174 : 158, "single piece elapsed");
                Near(single.LoadedDistance, config.ProductDistance(), "single piece material distance");
                Near(single.EmptyDistance, 0, "single piece no empty reposition");
                Check(single.Events.Count(e => e.subject == "LAMP-01" && e.activity == "transport") == 5, "five transfers");
                checks.Add(layout + ": single piece analytic time and distance pass");
                string signature = null;
                foreach (double delta in new[] { .017, .085, .17, 100000.0 })
                {
                    var sim = Finish(config, 20, delta); VerifyLedger(sim);
                    string current = JsonConvert.SerializeObject(sim.Events);
                    if (signature != null) Check(current == signature, "frame and speed independence");
                    signature = current;
                    if (delta == .17)
                    {
                        runs.Add(new { layout, quantity = 20, sim.Time, sim.LoadedDistance, sim.EmptyDistance,
                            totalWaitSeconds = sim.Products.Sum(p => p.WaitingSeconds), queues = sim.Stations.Select(s => s.MaxInputCount).ToArray(), events = sim.Events.Count });
                        File.WriteAllText(Evidence + "/" + layout + "-20-events.json", JsonConvert.SerializeObject(sim.Events, Formatting.Indented));
                    }
                }
                checks.Add(layout + ": 20 products, ledger, capacity and playback reproducibility pass");
                var compact = Read(layout); for (int i = 1; i < 5; i++) compact.Stations[i].Capacity = 1;
                VerifyLedger(Finish(compact, 20, 1)); checks.Add(layout + ": capacity-one backpressure and conservation pass");
            }
            var source = JObject.Parse(File.ReadAllText(DataPath + "layouts.json"));
            int permutations = 0;
            foreach (var order in Permute(new[] { "B", "C", "D", "E" }))
            {
                var clone = (JObject)source.DeepClone(); var positions = clone["layouts"][0]["positions"];
                for (int i = 0; i < 4; i++) positions[FactoryConfiguration.Route[i + 1]] = order[i];
                var config = FactoryConfiguration.Read(File.ReadAllText(DataPath + "factory-case.json"), clone.ToString());
                VerifyLedger(Finish(config, 20, 3)); permutations++;
            }
            Check(permutations == 24, "all legal permutations"); checks.Add("24 legal workstation permutations complete without deadlock");
            var zero = Read(); foreach (var s in zero.Stations) s.Cycle = 0; zero.LoadSeconds = zero.UnloadSeconds = 0;
            VerifyLedger(Finish(zero, 20, 2)); checks.Add("zero service and handling times terminate correctly");
            var bad = Read(); bad.Speed = 0;
            bool rejected = false; try { new FactorySimulation(bad, 20); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "zero speed rejected"); checks.Add("invalid speed rejected");
            File.WriteAllText(Evidence + "/validation.json", JsonConvert.SerializeObject(new { status = "PASS", checks, runs }, Formatting.Indented));
            Debug.Log("FACTORY_SIMULATION_PASS " + checks.Count + " groups; " + JsonConvert.SerializeObject(runs));
        }

        private static FactorySimulation Finish(FactoryConfiguration config, int quantity, double step)
        {
            var sim = new FactorySimulation(config, quantity); int count = 0;
            while (!sim.Complete)
            {
                sim.Advance(step);
                if (++count > 2000000 || sim.Time > 100000) throw new Exception("Simulation did not finish");
            }
            sim.AssertInvariants(); return sim;
        }

        private static void VerifyLedger(FactorySimulation sim)
        {
            Check(sim.FinishedCount == sim.Products.Length, "finished quantity");
            foreach (var product in sim.Products)
            {
                var rows = sim.Events.Where(e => e.subject == product.Id && e.endSeconds.HasValue).ToArray();
                Check(rows.Count(e => e.activity == "operation") == 3 && rows.Count(e => e.activity == "inspection") == 1, "all operations exactly once");
                var movements = rows.Where(e => e.activity == "transport").ToArray();
                Check(movements.Length == 5, "all transports exactly once");
                for (int i = 0; i < 5; i++) Check(movements[i].from == FactoryConfiguration.Route[i] && movements[i].to == FactoryConfiguration.Route[i + 1], "route order");
                for (int i = 1; i < rows.Length; i++) Near(rows[i - 1].endSeconds.Value, rows[i].startSeconds, "continuous material timeline");
                Check(rows.All(e => e.endSeconds.Value >= e.startSeconds), "nonnegative activity duration");
                Near(rows.Sum(e => e.endSeconds.Value - e.startSeconds), product.FinishedAt.Value, "timeline sum from inventory at t=0");
                Near(rows.Where(e => e.activity == "delay").Sum(e => e.endSeconds.Value - e.startSeconds), product.WaitingSeconds, "delay totals");
                Near(movements.Sum(e => e.distanceM), sim.Config.ProductDistance(), "product route distance");
            }
            Check(sim.Stations.Skip(1).Take(4).All(s => s.Completed == sim.Products.Length), "station completion counts");
        }

        private static IEnumerable<string[]> Permute(string[] values)
        {
            if (values.Length == 0) { yield return Array.Empty<string>(); yield break; }
            for (int i = 0; i < values.Length; i++)
                foreach (var rest in Permute(values.Where((_, j) => j != i).ToArray())) yield return new[] { values[i] }.Concat(rest).ToArray();
        }

        public static void Preview()
        {
            Validate();
            EditorSceneManager.OpenScene(ScenePath);
            var controller = UnityEngine.Object.FindObjectOfType<FactoryLabController>(); controller.Initialize();
            SavePreview(controller, "overview", 1920, 1080);
            controller.Simulation.Advance(230); controller.RefreshView(); SavePreview(controller, "running", 1920, 1080);
            controller.SetView(1); SavePreview(controller, "topdown", 1366, 768);
            controller.SetView(2); controller.SelectStation(3); SavePreview(controller, "inspection", 1920, 1080);
            Debug.Log("FACTORY_PREVIEW_PASS");
        }

        public static void SavePreview(FactoryLabController controller, string name, int width, int height)
        {
            Directory.CreateDirectory(Evidence);
            var target = new RenderTexture(width, height, 24); var worldCamera = controller.sceneCamera;
            var viewport = worldCamera.rect;
            var worldTarget = new RenderTexture(Mathf.RoundToInt(width * viewport.width), Mathf.RoundToInt(height * viewport.height), 24);
            worldCamera.rect = new Rect(0, 0, 1, 1);
            worldCamera.targetTexture = worldTarget; worldCamera.Render();
            var backdrop = new GameObject("Preview scene viewport", typeof(RectTransform), typeof(RawImage));
            var viewportRect = backdrop.GetComponent<RectTransform>(); viewportRect.SetParent(controller.Hud.transform, false); viewportRect.SetAsFirstSibling();
            viewportRect.anchorMin = viewport.min; viewportRect.anchorMax = viewport.max;
            viewportRect.offsetMin = viewportRect.offsetMax = Vector2.zero;
            backdrop.GetComponent<RawImage>().texture = worldTarget;
            var ui = new GameObject("Preview UI Camera").AddComponent<Camera>();
            ui.cullingMask = 1 << 5; ui.clearFlags = CameraClearFlags.SolidColor; ui.backgroundColor = worldCamera.backgroundColor; ui.nearClipPlane = .1f; ui.farClipPlane = 10;
            ui.transform.position = new Vector3(100, 100, -100); ui.transform.rotation = Quaternion.identity;
            worldCamera.cullingMask &= ~(1 << 5);
            foreach (var transform in controller.Hud.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = 5;
            controller.Hud.renderMode = RenderMode.ScreenSpaceCamera; controller.Hud.worldCamera = ui; controller.Hud.planeDistance = 1;
            ui.targetTexture = target; Canvas.ForceUpdateCanvases();
            var previous = RenderTexture.active; RenderTexture.active = target; GL.Clear(true, true, new Color(.89f, .92f, .93f));
            ui.Render(); RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
            File.WriteAllBytes(Evidence + "/" + name + ".png", texture.EncodeToPNG());
            RenderTexture.active = previous; worldCamera.targetTexture = null; ui.targetTexture = null;
            worldCamera.rect = viewport;
            controller.Hud.renderMode = RenderMode.ScreenSpaceOverlay; controller.Hud.worldCamera = null;
            UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(ui.gameObject);
            UnityEngine.Object.DestroyImmediate(backdrop); UnityEngine.Object.DestroyImmediate(worldTarget);
        }

        private static int smokeStage;
        private static double smokeMark, pausedTime;
        public static void PlayModeSmoke()
        {
            Directory.CreateDirectory(Evidence); SessionState.SetBool("FactoryLab.Smoke", true);
            EditorSceneManager.OpenScene(ScenePath); EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        private static void RegisterSmoke()
        {
            if (SessionState.GetBool("FactoryLab.Smoke", false)) { smokeStage = 0; smokeMark = EditorApplication.timeSinceStartup; EditorApplication.update += SmokeTick; }
        }

        private static void SmokeTick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup - smokeMark < .5) return;
            try
            {
                var controller = UnityEngine.Object.FindObjectOfType<FactoryLabController>();
                if (controller == null || controller.Simulation == null) return;
                if (smokeStage == 0)
                {
                    controller.GetComponentsInChildren<Button>().Single(b => b.name == "单件验证").onClick.Invoke();
                    controller.SetSpeed(10); controller.StartExperiment(); smokeStage++; smokeMark = EditorApplication.timeSinceStartup;
                }
                else if (smokeStage == 1)
                {
                    Check(controller.Simulation.Time > 0, "runtime clock advanced");
                    controller.PauseExperiment(); pausedTime = controller.Simulation.Time; smokeStage++; smokeMark = EditorApplication.timeSinceStartup;
                }
                else
                {
                    Near(controller.Simulation.Time, pausedTime, "paused clock stable"); Near(Time.timeScale, 1, "global timescale unchanged");
                    controller.ResetExperiment(1); controller.Simulation.Advance(10000); controller.RefreshView();
                    Near(controller.Simulation.Time, 174, "runtime single product");
                    controller.SetView(1); Check(controller.sceneCamera.orthographic, "topdown");
                    controller.SelectStation(3); controller.SetView(2); Check(!controller.sceneCamera.orthographic, "closeup");
                    controller.ResetExperiment(20); Check(controller.Simulation.Events.Count == 0 && controller.Simulation.Time == 0 && !controller.Running, "runtime reset");
                    Physics.SyncTransforms();
                    for (int s = 0; s < 6; s++)
                    {
                        var view = controller.views[s];
                        Check(Physics.Raycast(view.transform.position + Vector3.up * 5, Vector3.down, out var hit, 10) && hit.collider.GetComponentInParent<FactoryStationView>() == view, "station raycast " + s);
                        controller.SelectStation(s);
                        Check(controller.views.Count(v => v.selection.activeSelf) == 1 && view.selection.activeSelf, "station highlight " + s);
                    }
                    controller.Simulation.Advance(10000); controller.RefreshView();
                    VerifyLedger(controller.Simulation); Near(controller.Simulation.Time, 1771, "runtime full order");
                    controller.GetComponentsInChildren<Button>().Single(b => b.name == "导出事件").onClick.Invoke();
                    var exported = JObject.Parse(File.ReadAllText(controller.LastExportPath + ".json"));
                    Check((int)exported["finished"] == 20 && (bool)exported["completed"], "exported result");
                    Check(((JArray)exported["events"]).Count == controller.Simulation.Events.Count, "exported ledger");
                    Check(File.ReadAllLines(controller.LastExportPath + ".csv").Length == controller.Simulation.Events.Count + 1, "CSV rows");
                    File.WriteAllText(Evidence + "/playmode-smoke.json", JsonConvert.SerializeObject(new { status = "PASS", exportPath = controller.LastExportPath,
                        checks = new[] { "UI single-piece button", "live clock", "pause across frames", "global timescale preserved", "single-piece result", "camera modes", "20-piece reset", "six station raycasts and highlights", "20-piece completion", "UI export button", "JSON result and ledger", "CSV rows" } }, Formatting.Indented));
                    Debug.Log("FACTORY_PLAYMODE_PASS"); EndSmoke(0);
                }
            }
            catch (Exception ex) { Debug.LogException(ex); EndSmoke(1); }
        }
        private static void EndSmoke(int code)
        {
            SessionState.SetBool("FactoryLab.Smoke", false); EditorApplication.update -= SmokeTick;
            EditorApplication.ExitPlaymode(); EditorApplication.delayCall += () => EditorApplication.Exit(code);
        }
    }
}
