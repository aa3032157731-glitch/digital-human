using UnityEngine;

[CreateAssetMenu(menuName = "Voice Chat/Configs/XF TTS Config", fileName = "XFTTSConfig")]
public class XFTTSConfig : ScriptableObject
{
    [Header("Credentials")]
    [Tooltip("Xunfei AppId.")]
    public string appId;

    [Tooltip("Xunfei API key.")]
    public string apiKey;

    [Tooltip("Xunfei API secret used to sign the WebSocket URL.")]
    public string apiSecret;

    [Header("Endpoint")]
    [Tooltip("Xunfei ultra-human TTS WebSocket endpoint. Use the endpoint assigned to your app if it differs.")]
    public string websocketUrl = "wss://cbm01.cn-huabei-1.xf-yun.com/v1/private/mcd9m97e6";

    [Tooltip("Service identifier required by the private ultra-human TTS endpoint.")]
    public string serviceId = "mcd9m97e6";

    [Header("Voice")]
    [Tooltip("Voice name from your Xunfei console.")]
    public string voiceName = "x5_lingxiaoxuan_flow";

    [Tooltip("raw is recommended here because Unity can stream it into AudioSource without waiting for a full MP3 decode.")]
    public string audioEncoding = "raw";

    [Tooltip("Output sample rate. Keep this aligned with the selected voice/API capability.")]
    public int sampleRate = 24000;

    [Range(0, 100)]
    public int speed = 50;

    [Range(0, 100)]
    public int volume = 50;

    [Range(0, 100)]
    public int pitch = 50;

    [Header("Oral Style")]
    [Tooltip("Oralization level. Xunfei supports high, mid, low.")]
    public string oralLevel = "mid";

    [Tooltip("Enable large-model oralization assistance.")]
    public bool sparkAssist = true;

    [Tooltip("Disable server-side sentence splitting.")]
    public bool stopSplit;

    [Tooltip("Keep written style and remove added fillers/repetitions.")]
    public bool remainWrittenStyle;

    [Header("Streaming Text")]
    [Tooltip("Minimum characters to buffer before sending one TTS text chunk, unless punctuation is hit.")]
    [Range(1, 80)]
    public int minChunkChars = 12;

    [Tooltip("Maximum characters in a single TTS text frame.")]
    [Range(20, 400)]
    public int maxChunkChars = 120;

    [Tooltip("Delay used by the text sender while it waits for more DeepSeek deltas.")]
    [Range(5, 200)]
    public int textPollIntervalMs = 20;
}
