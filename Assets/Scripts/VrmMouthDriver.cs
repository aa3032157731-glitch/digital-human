using UniVRM10;
using UnityEngine;

// VRM characters only expose five vowel mouth expressions (aa/ih/ou/ee/oh), so they cannot use the
// CC blend-shape profiles or SALSA setups. This driver feeds those vowels from the Azure continuous
// timeline when AlignedMouthPlayback owns the mouth, and otherwise analyses the shared TTS AudioSource.
// Runs before Vrm10Instance (order 11000), which applies expression weights in its LateUpdate.
[DefaultExecutionOrder(10000)]
[RequireComponent(typeof(Vrm10Instance))]
public sealed class VrmMouthDriver : MonoBehaviour, IAlignedMouthTarget
{
    [Tooltip("TTS AudioSource. Leave empty to use VoiceChatController.ttsAudioSource.")]
    public AudioSource audioSource;
    public MouthTuning tuning = new MouthTuning { mouthIntensity = 1, jawAmplitude = 1 };

    [Header("Audio Lip Sync")]
    [Tooltip("Loudness relative to recent peak below which the mouth stays closed.")]
    [Range(0, .5f)] public float silenceGate = .12f;
    [Tooltip("Mouth opening speed (1/s).")]
    public float attackSpeed = 30;
    [Tooltip("Mouth closing speed (1/s).")]
    public float releaseSpeed = 14;

    [Header("Idle")]
    public bool autoBlink = true;
    public Vector2 blinkIntervalSeconds = new Vector2(2.5f, 6f);

    private static readonly ExpressionKey[] Vowels =
        { ExpressionKey.Aa, ExpressionKey.Ih, ExpressionKey.Ou, ExpressionKey.Ee, ExpressionKey.Oh };
    // (openness, frontness) per vowel, measured on zh-CN TTS speech; same order as Vowels.
    private static readonly Vector2[] VowelCentroids =
        { new Vector2(.82f, .52f), new Vector2(.10f, .55f), new Vector2(.36f, .05f), new Vector2(.48f, .81f), new Vector2(.80f, .08f) };
    private const float CentroidSigma = .18f;
    private const int SpectrumSize = 1024;

    private readonly float[] samples = new float[1024];
    private readonly float[] spectrum = new float[SpectrumSize];
    private readonly float[] target = new float[5];
    private readonly float[] current = new float[5];
    private Vrm10Instance vrm;
    private bool aligned;
    private float peak = .03f;
    private float nextBlink, blinkStart = -1;

    public MouthTuning Tuning => tuning;
    public bool HasMapping => Instance != null && Instance.Vrm != null && Instance.Vrm.Expression.Aa != null;
    private Vrm10Instance Instance => vrm != null ? vrm : vrm = GetComponent<Vrm10Instance>();

    private void Awake()
    {
        if (audioSource == null)
        {
            var controller = FindObjectOfType<VoiceChatController>(true);
            if (controller != null) audioSource = controller.ttsAudioSource;
        }
        nextBlink = Time.time + Random.Range(blinkIntervalSeconds.x, blinkIntervalSeconds.y);
    }

    // Azure continuous timeline: ARKit-style channels (see MouthChannels) folded onto five vowels.
    public void Apply(float[] c)
    {
        aligned = true;
        float open = Mathf.Clamp01(c[17] * 1.3f + (c[37] + c[38]) * .15f);
        float wide = Mathf.Clamp01((c[29] + c[30]) * .5f + (c[23] + c[24]) * .25f + (c[39] + c[40]) * .15f);
        float closure = Mathf.Clamp01(c[18] + (c[35] + c[36]) * .5f);
        float oh = Mathf.Clamp01(c[19]) * Mathf.Clamp01(open * 1.5f + .2f);
        float ou = Mathf.Clamp01(c[20]) * (1 - open * .5f);
        float ih = wide * (1 - Mathf.Clamp01(open * 1.5f));
        float ee = wide * Mathf.Clamp01(open * 1.5f);
        float aa = Mathf.Max(0, open - .6f * (oh + ou) - .3f * (ih + ee));
        float scale = (1 - closure * .8f) * tuning.mouthIntensity;
        target[0] = aa * scale * tuning.jawAmplitude;
        target[1] = ih * scale; target[2] = ou * scale; target[3] = ee * scale; target[4] = oh * scale;
    }

    public void Release(bool immediate = false)
    {
        aligned = false;
        System.Array.Clear(target, 0, target.Length);
        if (immediate) System.Array.Clear(current, 0, current.Length);
    }

    private void OnDisable() { Release(true); }

    private void LateUpdate()
    {
        var instance = Instance;
        if (instance == null || instance.Vrm == null) return;
        tuning.Sanitize();
        float dt = Time.deltaTime;
        if (!aligned) AnalyzeAudio(dt);
        var expression = instance.Runtime.Expression;
        for (int i = 0; i < Vowels.Length; ++i)
        {
            // Continuous data is already coarticulated, so only audio analysis gets the slower smoothing.
            float speed = aligned ? 60 : target[i] > current[i] ? attackSpeed : releaseSpeed;
            current[i] = Mathf.Clamp01(Mathf.Lerp(current[i], target[i], 1 - Mathf.Exp(-speed * dt)));
            expression.SetWeight(Vowels[i], current[i]);
        }
        if (autoBlink) expression.SetWeight(ExpressionKey.Blink, BlinkWeight());
    }

    private void AnalyzeAudio(float dt)
    {
        System.Array.Clear(target, 0, target.Length);
        if (audioSource == null || !audioSource.isPlaying) return;
        audioSource.GetOutputData(samples, 0);
        float sum = 0;
        foreach (var s in samples) sum += s * s;
        float rms = Mathf.Sqrt(sum / samples.Length);
        // Normalise against a slowly decaying recent peak so quiet and loud TTS voices both open the mouth fully.
        peak = Mathf.Max(Mathf.Max(rms, .03f), peak * Mathf.Exp(-.35f * dt));
        float level = Mathf.Clamp01((rms / peak - silenceGate) / .6f);
        if (level <= .01f) return;

        audioSource.GetSpectrumData(spectrum, 0, FFTWindow.Hamming);
        float hz = AudioSettings.outputSampleRate * .5f / SpectrumSize;
        float lo = Band(200, 600, hz), mid = Band(600, 1300, hz), hi = Band(1700, 3500, hz);
        var feature = new Vector2(mid / (lo + mid + 1e-6f), hi / (lo + mid + hi + 1e-6f) * 2.5f);
        float total = 0;
        for (int i = 0; i < VowelCentroids.Length; ++i)
        {
            target[i] = Mathf.Exp(-(feature - VowelCentroids[i]).sqrMagnitude / (2 * CentroidSigma * CentroidSigma));
            total += target[i];
        }
        float scale = total > 1e-6f ? level * tuning.mouthIntensity / total : 0;
        for (int i = 0; i < target.Length; ++i) target[i] *= scale;
        target[0] *= tuning.jawAmplitude;
    }

    private float Band(float fromHz, float toHz, float hz)
    {
        float sum = 0;
        for (int i = Mathf.Max(0, (int)(fromHz / hz)), end = Mathf.Min(SpectrumSize, (int)(toHz / hz)); i < end; ++i) sum += spectrum[i];
        return sum;
    }

    private float BlinkWeight()
    {
        if (Time.time >= nextBlink)
        {
            blinkStart = Time.time;
            nextBlink = Time.time + Random.Range(blinkIntervalSeconds.x, blinkIntervalSeconds.y);
        }
        if (blinkStart < 0) return 0;
        float t = Time.time - blinkStart;
        if (t < .06f) return t / .06f;
        if (t < .1f) return 1;
        return Mathf.Clamp01(1 - (t - .1f) / .12f);
    }
}
