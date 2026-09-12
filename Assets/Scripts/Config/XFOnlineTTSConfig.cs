using UnityEngine;

[CreateAssetMenu(menuName = "Voice Chat/Configs/XF Online TTS Config", fileName = "XFOnlineTTSConfig")]
public class XFOnlineTTSConfig : ScriptableObject
{
    [Header("Credentials")]
    [Tooltip("Xunfei AppId.")]
    public string appId;

    [Tooltip("Xunfei API key.")]
    public string apiKey;

    [Tooltip("Xunfei API secret used to sign the WebSocket URL.")]
    public string apiSecret;

    [Header("Endpoint")]
    [Tooltip("Xunfei online TTS WebSocket endpoint.")]
    public string websocketUrl = "wss://tts-api.xfyun.cn/v2/tts";

    [Header("Voice")]
    [Tooltip("Voice name from the online TTS service, for example xiaoyan.")]
    public string voiceName = "xiaoyan";

    [Tooltip("raw is required by the current Unity PCM streaming player.")]
    public string audioEncoding = "raw";

    [Tooltip("Xunfei audio format. Use audio/L16;rate=16000 for raw 16 kHz PCM.")]
    public string audioFormat = "audio/L16;rate=16000";

    [Tooltip("Output sample rate. Keep this aligned with audioFormat.")]
    public int sampleRate = 16000;

    [Tooltip("Text encoding sent to Xunfei.")]
    public string textEncoding = "UTF8";

    [Range(0, 100)]
    public int speed = 50;

    [Range(0, 100)]
    public int volume = 50;

    [Range(0, 100)]
    public int pitch = 50;

    [Tooltip("Background sound. 0 disables background audio.")]
    public int backgroundSound = 0;

    [Tooltip("English pronunciation mode. Leave empty to use service default.")]
    public string englishPronunciation;

    [Tooltip("Number reading mode. Leave empty to use service default.")]
    public string numberReadMode;

    [Header("Streaming Text")]
    [Tooltip("Online TTS accepts one text payload per WebSocket session. DeepSeek deltas are batched into short sessions by this threshold.")]
    [Range(1, 80)]
    public int minChunkChars = 18;

    [Tooltip("Maximum characters in one online TTS synthesis request.")]
    [Range(20, 400)]
    public int maxChunkChars = 120;

    [Tooltip("Delay used by the text sender while it waits for more DeepSeek deltas.")]
    [Range(5, 200)]
    public int textPollIntervalMs = 20;
}
