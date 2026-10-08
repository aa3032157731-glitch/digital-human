using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LipSyncProjectTools
{
    public const string Output = ".work/lipsync";
    [MenuItem("Tools/Voice Chat/Install Continuous Mouth Animation")]
    public static void Install()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/main.unity");
        var switcher = UnityEngine.Object.FindObjectOfType<CharacterModelSwitcher>(true);
        var controller = UnityEngine.Object.FindObjectOfType<VoiceChatController>(true);
        if (switcher == null || controller == null) throw new InvalidOperationException("主场景缺少语音控制器或模型切换器。");
        var playback = controller.GetComponent<AlignedMouthPlayback>() ?? controller.gameObject.AddComponent<AlignedMouthPlayback>();
        controller.mouthPlayback = playback; playback.player = controller.streamingPlayer; playback.modelSwitcher = switcher;
        var drivers = new System.Collections.Generic.List<AlignedMouthDriver>();
        Directory.CreateDirectory("Assets/MyCharacter/Config/MouthProfiles");
        var report = new System.Text.StringBuilder("# Mouth mapping coverage\n\nGenerated from the loaded Unity meshes. Azure channel numbers below are zero-based.\n");
        for (int model = 0; model < switcher.modelRoots.Length; ++model)
        {
            var root = switcher.modelRoots[model]; string modelName = model == 0 ? "test" : "girl";
            string path = "Assets/MyCharacter/Config/MouthProfiles/" + modelName + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<MouthProfile>(path);
            if (profile == null) { profile = ScriptableObject.CreateInstance<MouthProfile>(); AssetDatabase.CreateAsset(profile, path); }
            profile.modelName = modelName;
            var maps = new System.Collections.Generic.List<MouthShapeBinding>();
            var aliases = Aliases(model == 1);
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                foreach (var pair in aliases)
                    foreach (var shape in pair.Value)
                    {
                        int index = smr.sharedMesh.GetBlendShapeIndex(shape);
                        if (index < 0) continue;
                        maps.Add(new MouthShapeBinding { channel = pair.Key, shape = shape,
                            rendererPath = AnimationUtility.CalculateTransformPath(smr.transform, root.transform),
                            gain = pair.Key == 18 ? (model == 1 ? .7f : .45f) : 1,
                            neutralWeight = smr.GetBlendShapeWeight(index), approximate = pair.Key == 18 && model == 1 });
                    }
            }
            profile.bindings = maps.ToArray();
            // CC's mesh morph and its exported bone delta are complementary parts of
            // one expression. Reuse the authored delta, not an extra guessed jaw rotation.
            var export = JObject.Parse(File.ReadAllText(model == 0 ? "Assets/MyCharacter/test/Camelmingerup.json" : "Assets/MyCharacter/girl/female4_0.json"));
            string exportName = model == 0 ? "Camelmingerup" : "female4_0";
            var expressions = export[exportName]["Object"][exportName]["Expression"];
            var bones = new System.Collections.Generic.List<MouthBoneBinding>();
            foreach (var pair in aliases)
                foreach (string shape in pair.Value)
                {
                    if (!(expressions[shape]?["Bones"] is JObject boneMap)) continue;
                    foreach (var prop in boneMap.Properties())
                    {
                        if (!(prop.Name == "CC_Base_JawRoot" || prop.Name.StartsWith("CC_Base_Tongue") || prop.Name.StartsWith("CC_Base_Teeth"))) continue;
                        var bone = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == prop.Name);
                        if (bone == null) continue;
                        var p = prop.Value["Translate"].ToObject<float[]>(); var q = prop.Value["Rotation"].ToObject<float[]>();
                        bones.Add(new MouthBoneBinding { channel = pair.Key, bonePath = AnimationUtility.CalculateTransformPath(bone, root.transform),
                            neutralPosition = bone.localPosition, neutralRotation = bone.localRotation,
                            translation = new Vector3(-p[0], p[1], p[2]) * .01f, rotation = new Quaternion(q[0], -q[1], -q[2], q[3]),
                            gain = pair.Key == 18 ? (model == 1 ? .7f : .45f) : 1 });
                    }
                }
            profile.boneBindings = bones.ToArray();
            var jaw = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "CC_Base_JawRoot");
            profile.jawBonePath = jaw == null ? "" : AnimationUtility.CalculateTransformPath(jaw, root.transform);
            profile.jawNeutralRotation = jaw == null ? Quaternion.identity : jaw.localRotation;
            profile.useJawBone = !maps.Exists(m => m.channel == 17);
            profile.coverage = string.Join("\n", Enumerable.Range(0, 55).Where(MouthChannels.IsMouth).Select(i =>
                MouthChannels.Names[i] + ": " + (maps.Any(m => m.channel == i) ? string.Join(", ", maps.Where(m => m.channel == i).Select(m => m.shape).Distinct()) : "UNMAPPED")));
            report.Append("\n## " + modelName + "\n\n```\n" + profile.coverage + "\n```\n");
            report.Append("\nAuthored CC bone corrections: " + bones.Count + "; targets: " + string.Join(", ", bones.Select(b => b.bonePath.Split('/').Last()).Distinct()) + ".\n");
            var driver = root.GetComponent<AlignedMouthDriver>() ?? root.AddComponent<AlignedMouthDriver>();
            driver.profile = profile; drivers.Add(driver); EditorUtility.SetDirty(profile); EditorUtility.SetDirty(driver);
        }
        playback.drivers = drivers.ToArray(); controller.ApplyMouthTuning();
        EditorUtility.SetDirty(controller); EditorUtility.SetDirty(playback);
        EditorSceneManager.MarkSceneDirty(controller.gameObject.scene); EditorSceneManager.SaveScene(controller.gameObject.scene);
        File.WriteAllText("Assets/MyCharacter/Config/MouthProfiles/Coverage.md", report.ToString());
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); Debug.Log("LIPSYNC_INSTALL_PASS");
    }
    public static void InstallBatch()
    {
        try { Install(); EditorApplication.Exit(0); } catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    private static System.Collections.Generic.Dictionary<int, string[]> Aliases(bool girl)
    {
        var map = new System.Collections.Generic.Dictionary<int, string[]> {
            [14] = new[] { girl ? "Jaw_Fwd" : "Jaw_Forward" }, [15] = new[] { girl ? "Jaw_Left" : "Jaw_L" },
            [16] = new[] { girl ? "Jaw_Right" : "Jaw_R" }, [17] = new[] { "Jaw_Open" },
            [18] = girl ? new[] { "Mouth_Lips_Together_UL", "Mouth_Lips_Together_UR", "Mouth_Lips_Together_DL", "Mouth_Lips_Together_DR" } : new[] { "Mouth_Close" },
            [19] = girl ? new[] { "Mouth_Funnel_UL", "Mouth_Funnel_UR", "Mouth_Funnel_DL", "Mouth_Funnel_DR" } : new[] { "Mouth_Funnel_Up_L", "Mouth_Funnel_Up_R", "Mouth_Funnel_Down_L", "Mouth_Funnel_Down_R" },
            [20] = girl ? new[] { "Mouth_Lips_Purse_UL", "Mouth_Lips_Purse_UR", "Mouth_Lips_Purse_DL", "Mouth_Lips_Purse_DR" } : new[] { "Mouth_Pucker_Up_L", "Mouth_Pucker_Up_R", "Mouth_Pucker_Down_L", "Mouth_Pucker_Down_R" },
            [21] = new[] { girl ? "Mouth_Left" : "Mouth_L" }, [22] = new[] { girl ? "Mouth_Right" : "Mouth_R" },
            [23] = new[] { girl ? "Mouth_Corner_Pull_L" : "Mouth_Smile_L" }, [24] = new[] { girl ? "Mouth_Corner_Pull_R" : "Mouth_Smile_R" },
            [25] = new[] { girl ? "Mouth_Corner_Depress_L" : "Mouth_Frown_L" }, [26] = new[] { girl ? "Mouth_Corner_Depress_R" : "Mouth_Frown_R" },
            [27] = new[] { "Mouth_Dimple_L" }, [28] = new[] { "Mouth_Dimple_R" },
            [29] = new[] { "Mouth_Stretch_L" }, [30] = new[] { "Mouth_Stretch_R" },
            [31] = girl ? new[] { "Mouth_LowerLip_RollIn_L", "Mouth_LowerLip_RollIn_R" } : new[] { "Mouth_Roll_In_Lower_L", "Mouth_Roll_In_Lower_R" },
            [32] = girl ? new[] { "Mouth_UpperLip_RollIn_L", "Mouth_UpperLip_RollIn_R" } : new[] { "Mouth_Roll_In_Upper_L", "Mouth_Roll_In_Upper_R" },
            [33] = girl ? new[] { "Jaw_Chin_Raise_DL", "Jaw_Chin_Raise_DR" } : new[] { "Mouth_Shrug_Lower" },
            [34] = girl ? new[] { "Jaw_Chin_Raise_UL", "Jaw_Chin_Raise_UR" } : new[] { "Mouth_Shrug_Upper" },
            [35] = new[] { girl ? "Mouth_Lips_Press_L" : "Mouth_Press_L" }, [36] = new[] { girl ? "Mouth_Lips_Press_R" : "Mouth_Press_R" },
            [37] = new[] { girl ? "Mouth_LowerLip_Depress_L" : "Mouth_Down_Lower_L" }, [38] = new[] { girl ? "Mouth_LowerLip_Depress_R" : "Mouth_Down_Lower_R" },
            [39] = new[] { girl ? "Mouth_UpperLip_Raise_L" : "Mouth_Up_Upper_L" }, [40] = new[] { girl ? "Mouth_UpperLip_Raise_R" : "Mouth_Up_Upper_R" },
            [51] = new[] { "Tongue_Out" }
        };
        return map;
    }
    public static void Inspect()
    {
        try
        {
            Directory.CreateDirectory(Output);
            EditorSceneManager.OpenScene("Assets/Scenes/main.unity");
            var switcher = UnityEngine.Object.FindObjectOfType<CharacterModelSwitcher>(true);
            var roots = new JArray();
            foreach (var root in switcher.modelRoots)
            {
                var renderers = new JArray();
                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.sharedMesh == null) continue;
                    renderers.Add(new JObject { ["path"] = AnimationUtility.CalculateTransformPath(smr.transform, root.transform),
                        ["shapes"] = new JArray(Enumerable.Range(0, smr.sharedMesh.blendShapeCount).Select(smr.sharedMesh.GetBlendShapeName)) });
                }
                roots.Add(new JObject { ["name"] = root.name, ["renderers"] = renderers });
            }
            File.WriteAllText(Output + "/model-inventory.json", roots.ToString());
            var types = new JArray();
            foreach (var type in typeof(CrazyMinnow.SALSA.Salsa).Assembly.GetTypes().Where(t => t.Name.Contains("Expression") || t.Name == "Emoter" || t.Name == "QueueProcessor" || t.Name == "Salsa"))
                types.Add(new JObject { ["type"] = type.FullName, ["fields"] = new JArray(type.GetFields().Select(f => f.Name + ":" + f.FieldType.FullName)),
                    ["methods"] = new JArray(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(m => m.ToString())) });
            File.WriteAllText(Output + "/salsa-api.json", types.ToString());
            Debug.Log("LIPSYNC_INSPECT_PASS path=" + Application.persistentDataPath);
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
