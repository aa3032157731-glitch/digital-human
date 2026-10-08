using System;
using System.Text.RegularExpressions;
using UnityEngine;

[Serializable]
public sealed class AzureTTSSettings
{
    public bool continuousLipSync = true;
    public string apiKey = "";
    public string region = "";
    public string voiceName = "zh-CN-XiaoxiaoNeural";
    public string language = "zh-CN";
    public string style = "";
    public float styleDegree = 1;
    public float ratePercent;
    public float pitchPercent;
    public float volumePercent = 100;
    public int firstChunkChars = 12;
    public int nextChunkChars = 24;
    public int maxChunkChars = 80;
    public int firstWaitMs = 180;
    public int nextWaitMs = 300;
    public int idleTimeoutSeconds = 10;
    public int requestTimeoutSeconds = 30;
    public AzureTTSSettings Copy() => (AzureTTSSettings)MemberwiseClone();
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("请填写 Azure Speech Key。");
        region = (region ?? "").Trim().ToLowerInvariant();
        if (!Regex.IsMatch(region, "^[a-z][a-z0-9]{1,39}$")) throw new InvalidOperationException("请填写 Azure 全球版资源区域代码，例如 eastasia。");
        voiceName = (voiceName ?? "").Trim(); language = (language ?? "").Trim(); style = (style ?? "").Trim();
        if (voiceName.Length == 0 || language.Length == 0) throw new InvalidOperationException("Azure 音色和语言不能为空。");
        if (!Finite(styleDegree) || styleDegree < 0.01f || styleDegree > 2 ||
            !Finite(ratePercent) || ratePercent < -50 || ratePercent > 100 ||
            !Finite(pitchPercent) || pitchPercent < -50 || pitchPercent > 50 ||
            !Finite(volumePercent) || volumePercent < 0 || volumePercent > 100)
            throw new InvalidOperationException("Azure 参数范围：风格强度 0.01–2，语速 -50–100%，音调 -50–50%，音量 0–100%。");
        if (firstChunkChars < 1 || nextChunkChars < 1 || maxChunkChars < Math.Max(firstChunkChars, nextChunkChars) || maxChunkChars < 2 || maxChunkChars > 400 ||
            firstWaitMs < 20 || firstWaitMs > 2000 || nextWaitMs < 20 || nextWaitMs > 2000 || idleTimeoutSeconds < 1 || idleTimeoutSeconds > 60 || requestTimeoutSeconds < idleTimeoutSeconds || requestTimeoutSeconds > 120)
            throw new InvalidOperationException("Azure 分段或超时参数不正确。");
    }
    private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
}

[CreateAssetMenu(menuName = "Voice Chat/Configs/Azure TTS Config", fileName = "AzureTTSConfig")]
public sealed class AzureTTSConfig : ScriptableObject
{
    public AzureTTSSettings settings = new AzureTTSSettings();
}

[Serializable]
public sealed class PcmPlaybackSettings
{
    public float startBufferSeconds = 0.12f;
    public float resumeBufferSeconds = 0.2f;
    public int maxQueuedSeconds = 120;
    public bool logDiagnostics;
    public void Apply(StreamingPcmAudioPlayer player)
    {
        if (player == null) return;
        player.startBufferSeconds = Valid(startBufferSeconds, 0.02f, 0.5f, 0.12f);
        player.resumeBufferSeconds = Valid(resumeBufferSeconds, 0.05f, 1f, 0.2f);
        player.maxQueuedSeconds = Mathf.Clamp(maxQueuedSeconds, 5, 120);
        player.logDiagnostics = logDiagnostics;
    }
    private static float Valid(float v, float min, float max, float fallback) => float.IsNaN(v) || float.IsInfinity(v) ? fallback : Mathf.Clamp(v, min, max);
}
