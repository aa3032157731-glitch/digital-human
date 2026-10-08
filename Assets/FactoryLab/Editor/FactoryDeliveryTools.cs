using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEditor.Build.Reporting;
using System.Reflection;
using System.Threading.Tasks;

namespace FactoryLab.Editor
{
    public static class FactoryDeliveryTools
    {
        public static async void VoiceRegression()
        {
            try
            {
                EditorSceneManager.OpenScene(FactoryLabTools.ScenePath);
                const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Static;
                foreach(string name in new[]{"TestData","TestChunker","TestModels","TestNative"})typeof(LipSyncValidation).GetMethod(name,flags).Invoke(null,null);
                await (Task)typeof(LipSyncValidation).GetMethod("TestServiceQueue",flags).Invoke(null,null);
                foreach(string name in new[]{"TestPackets","TestChunker","TestPlayer","TestLegacyTerminal"})typeof(AzureTtsValidation).GetMethod(name,flags).Invoke(null,null);
                await (Task)typeof(AzureTtsValidation).GetMethod("TestService",flags).Invoke(null,null);
                var c=UnityEngine.Object.FindObjectOfType<FactoryLabController>();c.Initialize();c.OpenTeacher();
                var context=typeof(VoiceChatController).GetMethod("BuildRuntimeSystemContext",BindingFlags.NonPublic|BindingFlags.Instance);
                string supplied=(string)context.Invoke(c.teacherVoice,new object[]{"当前实验是什么",0});
                if(!supplied.Contains(c.ExperimentId)||supplied.Contains("校园服务"))throw new Exception("Scene context isolation failed");
                c.CloseModal();
                File.WriteAllText(".work/factory-lab/voice-regression.json",JsonConvert.SerializeObject(new{status="PASS",checks=new[]{"mouth animation samples and interpolation","both copied model mappings and jaw motion","speech native library loading","ordered speech queue and cancellation","PCM packet buffer and ten-minute ring playback","TTS cancellation late callbacks","synthetic TTS success/error/timeout","teacher context replaces unrelated knowledge"},liveNetwork=false},Formatting.Indented));
                Debug.Log("FACTORY_VOICE_REGRESSION_PASS");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        public static void PreviewCourse()
        {
            EditorSceneManager.OpenScene(FactoryLabTools.ScenePath);
            var c=UnityEngine.Object.FindObjectOfType<FactoryLabController>(); c.Initialize();
            c.Simulation.Advance(10000);c.SaveCompletedRun();c.ResetExperiment(20);c.SwapStations(1,3);c.SwapStations(2,3);c.SwapStations(3,4);c.Simulation.Advance(10000);c.SaveCompletedRun();
            c.OpenLayout();FactoryLabTools.SavePreview(c,"course-comparison",1920,1080);c.CloseModal();
            c.OpenLesson(2);FactoryLabTools.SavePreview(c,"course-lesson",1366,768);c.CloseModal();
            c.OpenTeacher();c.AskLocal(FactoryTeaching.Questions[8]);c.teacherCamera.Render();FactoryLabTools.SavePreview(c,"course-teacher",1920,1080);c.CloseModal();
            Debug.Log("FACTORY_COURSE_PREVIEW_PASS");
        }
        public static void PlayCourse()
        {
            SessionState.SetBool("FactoryLab.CourseSmoke",true);
            EditorSceneManager.OpenScene(FactoryLabTools.ScenePath);EditorApplication.EnterPlaymode();
        }
        [InitializeOnLoadMethod]
        private static void RegisterCourseSmoke()
        {
            if(SessionState.GetBool("FactoryLab.CourseSmoke",false))EditorApplication.update+=CourseSmoke;
        }
        private static void CourseSmoke()
        {
            if(!EditorApplication.isPlaying)return;
            var c=UnityEngine.Object.FindObjectOfType<FactoryLabController>();if(c==null||c.Simulation==null)return;
            EditorApplication.update-=CourseSmoke;SessionState.SetBool("FactoryLab.CourseSmoke",false);
            int exit=0;
            try
            {
                FactoryLabController.StorageOverride=Path.GetFullPath(".work/factory-lab/course-test");
                var checks=FactoryAcceptance.Run(c);
                File.WriteAllText(".work/factory-lab/course-playmode.json",JsonConvert.SerializeObject(new{status="PASS",checks,report=c.LastReportPath},Formatting.Indented));
                Debug.Log("FACTORY_COURSE_PLAYMODE_PASS "+checks.Count);
            }
            catch(Exception e){Debug.LogException(e);exit=1;}
            EditorApplication.ExitPlaymode();EditorApplication.delayCall+=()=>EditorApplication.Exit(exit);
        }
        public static void InstallTeacher()
        {
            // Never reparent or save objects in the user's main scene. Work only in a dedicated copy.
            const string sourcePath = "Assets/Scenes/main.unity";
            const string copyPath = "Assets/FactoryLab/Scenes/TeacherSourceCopy.unity";
            var sourceBytes = File.ReadAllBytes(sourcePath);
            File.Copy(sourcePath, copyPath, true); AssetDatabase.Refresh();
            var main = EditorSceneManager.OpenScene(copyPath);
            var voice = UnityEngine.Object.FindObjectOfType<VoiceChatController>(true);
            var sourceSwitcher = UnityEngine.Object.FindObjectOfType<CharacterModelSwitcher>(true);
            if(voice==null||sourceSwitcher==null)throw new Exception("Existing digital human not found");
            var rig=new GameObject("Factory teacher rig");
            var roots=main.GetRootGameObjects();
            foreach(var r in roots.Where(r=>r.name=="Model"||r==voice.gameObject||r.name.EndsWith("_HeadTarget")||r.name.EndsWith("_EyeTarget")))r.transform.SetParent(rig.transform,true);
            var switcher=rig.AddComponent<CharacterModelSwitcher>(); EditorUtility.CopySerialized(sourceSwitcher,switcher);
            switcher.switchButtonLabel=null;switcher.characterPositioners=Array.Empty<CharacterCameraPositioner>();switcher.voiceChatController=voice;
            if(voice.mouthPlayback!=null)voice.mouthPlayback.modelSwitcher=switcher;
            foreach(var positioner in rig.GetComponentsInChildren<CharacterCameraPositioner>(true))UnityEngine.Object.DestroyImmediate(positioner);
            voice.enablePersistentConversation=false;voice.loadLatestConversationOnStart=false;voice.enableKnowledgeBase=false;
            voice.sceneMemoryFolder="FactoryLab/Teacher";
            voice.injectFullKnowledgeBaseOnNewConversation=false;voice.enableAgentTools=false;voice.enablePushToTalk=false;voice.listenOnStart=false;voice.persistentDataFolder="FactoryLab/Teacher";
            // Runtime settings are loaded by the existing controller. Do not embed API credential assets in the deliverable rig.
            voice.asrConfig=EmptyConfig<XFASRConfig>();voice.deepSeekConfig=EmptyConfig<DeepSeekConfig>();voice.ttsConfig=EmptyConfig<XFTTSConfig>();voice.onlineTtsConfig=EmptyConfig<XFOnlineTTSConfig>();voice.doubaoTtsConfig=EmptyConfig<DoubaoTTSConfig>();voice.azureTtsConfig=EmptyConfig<AzureTTSConfig>();voice.baiduSearchConfig=EmptyConfig<BaiduSearchConfig>();
            voice.deepSeekConfig.logRequestPayloadToConsole=false;
            rig.transform.position=new Vector3(100,0,100);
            foreach(var root in switcher.modelRoots)root.transform.rotation=Quaternion.Euler(0,180,0);
            foreach(var animator in rig.GetComponentsInChildren<Animator>(true)){animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.applyRootMotion=false;}
            foreach(var t in rig.GetComponentsInChildren<Transform>(true)){t.gameObject.layer=30;GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);}
            var model=switcher.modelRoots[switcher.currentModelIndex];var renderers=model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            var camera=new GameObject("Teacher portrait camera",typeof(Camera)).GetComponent<Camera>();camera.transform.SetParent(rig.transform,true);
            var focus=bounds.center+Vector3.up*bounds.size.y*.24f;
            camera.transform.position=focus+new Vector3(0,0,-Mathf.Max(1.7f,bounds.size.y));camera.transform.LookAt(focus);
            camera.cullingMask=1<<30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.85f,.90f,.91f);camera.fieldOfView=32;camera.nearClipPlane=.05f;camera.farClipPlane=30;
            var light=new GameObject("Teacher soft light",typeof(Light)).GetComponent<Light>();light.transform.SetParent(rig.transform);light.type=LightType.Directional;light.intensity=1.3f;light.cullingMask=1<<30;light.transform.rotation=Quaternion.Euler(25,-25,0);
            Directory.CreateDirectory("Assets/FactoryLab/Prefabs");
            var prefab=PrefabUtility.SaveAsPrefabAsset(rig,"Assets/FactoryLab/Prefabs/FactoryTeacher.prefab");
            if(prefab==null)throw new Exception("Teacher prefab could not be saved");
            var scene=EditorSceneManager.OpenScene(FactoryLabTools.ScenePath);
            var controller=UnityEngine.Object.FindObjectOfType<FactoryLabController>();
            if(controller.teacherRig!=null)UnityEngine.Object.DestroyImmediate(controller.teacherRig);
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
            controller.teacherRig=instance;controller.teacherVoice=instance.GetComponentInChildren<VoiceChatController>(true);controller.teacherCamera=instance.GetComponentInChildren<Camera>(true);
            controller.sceneCamera.cullingMask &= ~(1<<30);
            foreach(var l in controller.GetComponentsInChildren<Light>())l.cullingMask &= ~(1<<30);
            Directory.CreateDirectory("Assets/FactoryLab/Data/Knowledge");
            foreach(var file in Directory.GetFiles("Plan/demoPlan/knowledge-base","*.txt"))File.Copy(file,"Assets/FactoryLab/Data/Knowledge/"+Path.GetFileName(file),true);
            AssetDatabase.Refresh();
            controller.teachingKnowledge=Directory.GetFiles("Assets/FactoryLab/Data/Knowledge","*.txt").OrderBy(f=>f).Select(f=>AssetDatabase.LoadAssetAtPath<TextAsset>(f)).ToArray();
            instance.SetActive(false);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            if(!sourceBytes.SequenceEqual(File.ReadAllBytes(sourcePath)))throw new Exception("Source scene changed unexpectedly");
            AssetDatabase.DeleteAsset(copyPath);
            Debug.Log("FACTORY_TEACHER_INSTALL_PASS");
        }
        public static void InstallAndBuild(){InstallTeacher();BuildWindows();}
        public static void BuildWindows()
        {
            Directory.CreateDirectory("Builds/FactoryLab");
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[]{FactoryLabTools.ScenePath},locationPathName="Builds/FactoryLab/FactoryLab.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            File.WriteAllText(".work/factory-lab/build-result.json",JsonConvert.SerializeObject(new{result=result.summary.result.ToString(),errors=result.summary.totalErrors,warnings=result.summary.totalWarnings,bytes=result.summary.totalSize},Formatting.Indented));
            if(result.summary.result!=BuildResult.Succeeded)throw new Exception("Windows build failed: "+result.summary.result);
            Debug.Log("FACTORY_WINDOWS_BUILD_PASS");
        }
        private static T EmptyConfig<T>() where T:ScriptableObject
        {
            Directory.CreateDirectory("Assets/FactoryLab/Data/TeacherConfig");
            string path="Assets/FactoryLab/Data/TeacherConfig/"+typeof(T).Name+".asset";
            var value=AssetDatabase.LoadAssetAtPath<T>(path);
            if(value==null){value=ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(value,path);}return value;
        }
        public static void InspectTeacher()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/main.unity");
            var voice = UnityEngine.Object.FindObjectOfType<VoiceChatController>(true);
            var models = UnityEngine.Object.FindObjectOfType<CharacterModelSwitcher>(true);
            Directory.CreateDirectory(".work/factory-lab");
            File.WriteAllText(".work/factory-lab/teacher-structure.json", JsonConvert.SerializeObject(new {
                roots = scene.GetRootGameObjects().Select(r => new { r.name, types = r.GetComponents<Component>().Select(c => c == null ? "missing" : c.GetType().Name), children = r.transform.Cast<Transform>().Select(t => t.name) }),
                voice = voice == null ? "missing" : voice.name, voiceRoot = voice == null ? "" : voice.transform.root.name,
                models = models == null ? null : models.modelRoots.Select(r => new { r.name, root = r.transform.root.name, pos = r.transform.position.ToString() }),
                cameras = UnityEngine.Object.FindObjectsOfType<Camera>(true).Select(c => new { c.name, root = c.transform.root.name, pos = c.transform.position.ToString() })
            }, Formatting.Indented));
            Debug.Log("FACTORY_TEACHER_INSPECT_PASS");
        }
    }
}
