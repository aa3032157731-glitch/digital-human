using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UniVRM10;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Batch validation for VRM characters: installs the .vrm files of a test folder into an unsaved copy of the
// main scene, then in play mode drives them through the Azure continuous timeline and through audio analysis.
// Usage: -executeMethod VrmCharacterValidation.Run [-vrmFolder Assets/...] ; output in .work/vrm-test.
[InitializeOnLoad]
public static class VrmCharacterValidation
{
    private const string Output = ".work/vrm-test";
    private const string FlagKey = "VrmCharacterValidation";

    static VrmCharacterValidation()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(FlagKey, false))
            { SessionState.SetBool(FlagKey, false); RunChecks(); }
        };
    }

    public static void Run()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-vrmFolder");
            string folder = at >= 0 && at + 1 < args.Length ? args[at + 1] : VrmCharacterInstaller.VrmFolder;
            Directory.CreateDirectory(Output);
            Debug.Log(VrmCharacterInstaller.Install(folder, false));

            var switcher = UnityEngine.Object.FindObjectOfType<CharacterModelSwitcher>(true);
            var report = new JObject();
            foreach (var root in switcher.modelRoots.Where(r => r.GetComponent<Vrm10Instance>() != null))
            {
                var animator = root.GetComponent<Animator>();
                var shaders = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null)
                    .Select(m => m.shader.name).Distinct().ToArray();
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman) throw new Exception(root.name + " 没有 Humanoid Avatar");
                if (root.GetComponent<VrmMouthDriver>() == null) throw new Exception(root.name + " 缺少 VrmMouthDriver");
                if (shaders.Any(s => s.Contains("InternalError"))) throw new Exception(root.name + " 材质着色器无效");
                if (root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r => !r.updateWhenOffscreen)) throw new Exception(root.name + " 网格包围盒不会随动画更新");
                report[root.name] = new JObject
                {
                    ["humanoid"] = true,
                    ["controller"] = animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : null,
                    ["shaders"] = new JArray(shaders),
                };
            }
            if (!report.HasValues) throw new Exception("没有安装任何 VRM 角色");
            File.WriteAllText(Output + "/install.json", report.ToString());

            // Isolate the model roots so microphone, network and saved settings are untouched in play mode.
            var roots = switcher.modelRoots;
            foreach (var root in roots) root.transform.SetParent(null);
            var kept = new HashSet<GameObject>(roots);
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (!kept.Contains(root)) UnityEngine.Object.DestroyImmediate(root);
            var host = new GameObject("Isolated Model Switcher");
            var isolated = host.AddComponent<CharacterModelSwitcher>(); isolated.modelRoots = roots; isolated.applyOnStart = false;
            new GameObject("Isolated Audio Listener", typeof(AudioListener));
            foreach (var b in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true))
                if (!(b is VrmMouthDriver) && !(b is AlignedMouthDriver) && !(b is CharacterModelSwitcher)
                    && b.GetType().Namespace != "UniVRM10" && b.GetType().Namespace != "UniGLTF" && b.GetType().Namespace != "CrazyMinnow.SALSA")
                    b.enabled = false;
            SessionState.SetBool(FlagKey, true); EditorApplication.EnterPlaymode();
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    private sealed class Feed : IAlignedTTSService
    {
        public event Action<byte[]> AudioChunkReceived { add { } remove { } }
        public event Action<string> Error { add { } remove { } }
        public event Action Completed;
        public event Action<AlignedSpeechSegment> SegmentReady;
        public int OutputSampleRate => 24000;
        public void Send(AlignedSpeechSegment s) => SegmentReady?.Invoke(s);
        public void AcknowledgeSegment(int n) { }
        public Task StartAsync(CancellationToken token) => Task.CompletedTask;
        public void EnqueueText(string text) { }
        public void CompleteInput() => Completed?.Invoke();
        public Task WaitForCompletionAsync() => Task.CompletedTask;
        public Task CancelAsync() => Task.CompletedTask;
        public void Dispose() { }
    }

    private static async void RunChecks()
    {
        try
        {
            var result = new JObject();
            // Compile shaders synchronously so captures never show placeholder materials.
            ShaderUtil.allowAsyncCompilation = false;
            // The project's URP asset may be an unfetched Git LFS pointer; run with a temporary, in-memory URP asset
            // so MToon10 URP materials can be checked. Restored before exit.
            var savedPipeline = GraphicsSettings.defaultRenderPipeline;
            bool temporaryUrp = GraphicsSettings.currentRenderPipeline == null;
            if (temporaryUrp) GraphicsSettings.defaultRenderPipeline = TemporaryUrp();
            try
            {
                var switcher = UnityEngine.Object.FindObjectOfType<CharacterModelSwitcher>(true);
                switcher.voiceChatController = null;
                var host = new GameObject("Isolated Mouth Playback", typeof(AudioSource), typeof(StreamingPcmAudioPlayer), typeof(AlignedMouthPlayback));
                var player = host.GetComponent<StreamingPcmAudioPlayer>(); player.clipSeconds = 5;
                host.GetComponent<AudioSource>().volume = 0;
                var mouth = host.GetComponent<AlignedMouthPlayback>(); mouth.player = player; mouth.modelSwitcher = switcher;
                var speech = new GameObject("Validation Speech", typeof(AudioSource)).GetComponent<AudioSource>();
                // Optional speech sample (PCM16 WAV) for the audio-analysis check; skipped when absent.
                string speechPath = Path.Combine(Output, "v_sent.wav");
                speech.clip = File.Exists(speechPath) ? LoadWav(speechPath) : null;

                for (int index = 0; index < switcher.modelRoots.Length; ++index)
                {
                    var root = switcher.modelRoots[index];
                    var vrm = root.GetComponent<Vrm10Instance>();
                    if (vrm == null) continue;
                    switcher.currentModelIndex = index;
                    for (int j = 0; j < switcher.modelRoots.Length; ++j) switcher.modelRoots[j].SetActive(j == index);
                    var driver = root.GetComponent<VrmMouthDriver>();
                    var expression = vrm.Runtime.Expression;
                    var entry = new JObject();
                    await Frames(5);

                    // 1. Azure continuous timeline through the real AlignedMouthPlayback.
                    driver.audioSource = host.GetComponent<AudioSource>();
                    var feed = new Feed(); long id = player.BeginStream(24000); mouth.Begin(feed, id);
                    feed.Send(Segment(1.2f)); feed.CompleteInput();
                    await Until(() => player.IsAudible, 3); await Task.Delay(400);
                    float alignedPeak = MaxVowel(expression);
                    entry["alignedStatus"] = mouth.Status; entry["alignedPeak"] = alignedPeak;
                    if (mouth.Status != "连续口型" || alignedPeak < .2f) throw new Exception(root.name + " 连续口型没有驱动 VRM 元音：" + mouth.Status + " " + alignedPeak);
                    await Until(() => player.State == PcmPlaybackState.Completed, 4); await Task.Delay(500);
                    entry["releasedPeak"] = MaxVowel(expression);
                    if (MaxVowel(expression) > .05f) throw new Exception(root.name + " 说完后嘴没有闭上");

                    // 2. Audio analysis (Xunfei / Doubao / plain Azure) on real speech.
                    if (speech.clip == null) entry["audio"] = "skipped: no " + speechPath;
                    else
                    {
                        driver.audioSource = speech; speech.Play();
                        var dominant = new System.Text.StringBuilder(); int open = 0, frames = 0; float sourceRms = 0;
                        var buffer = new float[1024];
                        while (speech.isPlaying)
                        {
                            await Frames(1);
                            speech.GetOutputData(buffer, 0); sourceRms = Mathf.Max(sourceRms, Mathf.Sqrt(buffer.Average(v => v * v)));
                            var weights = Vowels.Select(k => expression.GetWeight(k)).ToArray();
                            ++frames; if (weights.Max() > .2f) ++open;
                            if (frames % 6 == 0) dominant.Append(weights.Max() < .1f ? '_' : "AIUEO"[Array.IndexOf(weights, weights.Max())]);
                        }
                        entry["audioOutputRms"] = sourceRms; entry["openFrameRatio"] = frames == 0 ? 0 : open / (float)frames; entry["vowels"] = dominant.ToString();
                        if (sourceRms > .001f && (open < frames * .2f || dominant.ToString().Where(char.IsLetter).Distinct().Count() < 3))
                            throw new Exception(root.name + " 音频口型变化不足：" + dominant);
                    }

                    entry["captureWithTemporaryUrp"] = temporaryUrp;
                    await Capture(root, driver, Path.Combine(Output, root.name));
                    await Task.Delay(100);
                    result[root.name] = entry;
                }
            }
            finally { GraphicsSettings.defaultRenderPipeline = savedPipeline; }
            File.WriteAllText(Path.Combine(Output, "playmode.json"), result.ToString());
            Debug.Log("VRM_VALIDATION_PASS " + result.ToString(Newtonsoft.Json.Formatting.None));
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    private static readonly ExpressionKey[] Vowels = { ExpressionKey.Aa, ExpressionKey.Ih, ExpressionKey.Ou, ExpressionKey.Ee, ExpressionKey.Oh };
    private static float MaxVowel(Vrm10RuntimeExpression e) => Vowels.Max(k => e.ActualWeights.TryGetValue(k, out var w) ? w : 0);

    private static AlignedSpeechSegment Segment(float seconds)
    {
        var pcm = new byte[(int)(seconds * 24000) * 2];
        for (int i = 0; i < pcm.Length / 2; ++i) { short v = (short)(Math.Sin(i * Math.PI * 440 / 24000) * 4000); pcm[i * 2] = (byte)v; pcm[i * 2 + 1] = (byte)(v >> 8); }
        var frames = new float[(int)Math.Ceiling(seconds * 60)][];
        for (int i = 0; i < frames.Length; ++i) { frames[i] = new float[55]; frames[i][17] = .45f + .25f * (float)Math.Sin(i * .2); frames[i][20] = i % 40 < 20 ? .5f : 0; }
        return new AlignedSpeechSegment { sequence = 0, pcm = pcm, frames = frames, mode = MouthAnimationMode.BlendShapes };
    }

    private static async Task Frames(int count)
    {
        for (int start = Time.frameCount; Time.frameCount < start + count;) await Task.Yield();
    }

    private static async Task Until(Func<bool> predicate, double seconds)
    {
        double until = Time.realtimeSinceStartupAsDouble + seconds;
        while (!predicate()) { if (Time.realtimeSinceStartupAsDouble > until) throw new Exception("VRM 口型验证超时"); await Task.Delay(10); }
    }

    private static AudioClip LoadWav(string path)
    {
        var bytes = File.ReadAllBytes(path);
        int pos = 12, rate = 0, dataAt = -1, dataLength = 0;
        while (pos + 8 <= bytes.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4); int size = BitConverter.ToInt32(bytes, pos + 4);
            if (id == "fmt ") rate = BitConverter.ToInt32(bytes, pos + 12);
            if (id == "data") { dataAt = pos + 8; dataLength = Math.Min(size, bytes.Length - dataAt); break; }
            pos += 8 + size + (size & 1);
        }
        if (dataAt < 0 || rate == 0) throw new Exception("无效的测试音频：" + path);
        var samples = new float[dataLength / 2];
        for (int i = 0; i < samples.Length; ++i) samples[i] = BitConverter.ToInt16(bytes, dataAt + i * 2) / 32768f;
        var clip = AudioClip.Create("validation speech", samples.Length, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    // Face close-ups (closed, aa, ou, ih through the same Apply path as Azure) plus a full-body view from the
    // root's forward axis. Cameras render in the normal frame so they see the final animated, skinned pose.
    private static async Task Capture(GameObject root, VrmMouthDriver driver, string prefix)
    {
        var animator = root.GetComponent<Animator>();
        var face = NewCamera("VRM validation face camera", 30).gameObject.AddComponent<FollowFace>();
        face.animator = animator;
        var full = NewCamera("VRM validation body camera", 30);
        full.transform.position = root.transform.position + Vector3.up * .8f + root.transform.forward * 3.4f;
        full.transform.LookAt(root.transform.position + Vector3.up * .8f, Vector3.up);
        var light = new GameObject("VRM validation light").AddComponent<Light>();
        light.type = LightType.Directional; light.transform.rotation = full.transform.rotation * Quaternion.Euler(15, -20, 0);
        var poses = new Dictionary<string, (int, float)[]>
        {
            ["closed"] = new (int, float)[0],
            ["aa"] = new[] { (17, .8f) },
            ["ou"] = new[] { (17, .1f), (20, .9f) },
            ["ih"] = new[] { (17, .05f), (29, .8f), (30, .8f) },
        };
        try
        {
            await Frames(3);
            Save(full, prefix + "-body.png");
            foreach (var pose in poses)
            {
                var channels = new float[55];
                foreach (var (channel, value) in pose.Value) channels[channel] = value;
                driver.Apply(channels);
                await Frames(20);
                Save(face.GetComponent<Camera>(), prefix + "-" + pose.Key + ".png");
            }
        }
        finally
        {
            driver.Release(true);
            foreach (var c in new[] { face.GetComponent<Camera>(), full }) { var rt = c.targetTexture; c.targetTexture = null; UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(c.gameObject); }
            UnityEngine.Object.Destroy(light.gameObject);
        }
    }

    private static Camera NewCamera(string name, float fov)
    {
        var camera = new GameObject(name).AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.18f, .2f, .23f);
        camera.fieldOfView = fov; camera.nearClipPlane = .01f;
        camera.targetTexture = new RenderTexture(512, 512, 24);
        return camera;
    }

    private static void Save(Camera camera, string file)
    {
        var image = new Texture2D(512, 512, TextureFormat.RGB24, false);
        var old = RenderTexture.active;
        try { RenderTexture.active = camera.targetTexture; image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); image.Apply(); File.WriteAllBytes(file, image.EncodeToPNG()); }
        finally { RenderTexture.active = old; UnityEngine.Object.Destroy(image); }
    }

    // Runs after Animator, VrmMouthDriver and Vrm10Instance so the camera frames the pose that is actually rendered.
    [DefaultExecutionOrder(32000)]
    private sealed class FollowFace : MonoBehaviour
    {
        public Animator animator;
        private void LateUpdate()
        {
            var left = animator.GetBoneTransform(HumanBodyBones.LeftEye); var right = animator.GetBoneTransform(HumanBodyBones.RightEye);
            if (left == null || right == null) { left = right = animator.GetBoneTransform(HumanBodyBones.Head); }
            var forward = left != right ? Vector3.Cross(right.position - left.position, Vector3.up).normalized : animator.transform.forward;
            var target = (left.position + right.position) * .5f - Vector3.up * .04f;
            transform.position = target + forward * .5f;
            transform.LookAt(target, Vector3.up);
        }
    }

    private static UniversalRenderPipelineAsset TemporaryUrp()
    {
        var data = ScriptableObject.CreateInstance<UniversalRendererData>();
        ResourceReloader.ReloadAllNullIn(data, UniversalRenderPipelineAsset.packagePath);
        return UniversalRenderPipelineAsset.Create(data);
    }

}
