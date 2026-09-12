using UnityEngine;

[CreateAssetMenu(menuName = "Voice Chat/Configs/XF ASR Config", fileName = "XFASRConfig")]
public class XFASRConfig : ScriptableObject
{
    [Header("Credentials")]
    [Tooltip("Xunfei AppId.")]
    public string appId;

    [Tooltip("Xunfei API key.")]
    public string apiKey;

    [Tooltip("Xunfei API secret used to sign the WebSocket URL.")]
    public string apiSecret;

    [Header("Endpoint")]
    [Tooltip("Streaming Chinese ASR large model WebSocket endpoint.")]
    public string websocketUrl = "wss://iat.xf-yun.com/v1";

    [Header("Recognition")]
    [Tooltip("Optional application-level hotword resource id, for example hot_words. Leave empty if unused.")]
    public string resId;

    [Tooltip("ASR domain. Keep slm for the Chinese ASR large model unless your Xunfei console says otherwise.")]
    public string domain = "slm";

    [Tooltip("Recognition language.")]
    public string language = "zh_cn";

    [Tooltip("Mandarin accent setting used by Xunfei IAT.")]
    public string accent = "mandarin";

    [Tooltip("End-of-speech timeout in milliseconds.")]
    [Range(500, 10000)]
    public int vadEosMs = 6000;

    [Tooltip("Enable dynamic correction. wpgs gives replace/append partial results.")]
    public string dynamicCorrection = "wpgs";

    [Tooltip("Whether to return punctuation.")]
    public bool enablePunctuation = true;

    [Tooltip("Chinese/English filtering. 1: no filtering, 2: Chinese only, 3: English only.")]
    [Range(1, 3)]
    public int languageTypeControl = 1;

    [Header("Microphone")]
    [Tooltip("Unity microphone device name. Leave empty to use the default device.")]
    public string microphoneDevice;

    [Tooltip("Requested microphone sample rate. Xunfei stream ASR commonly uses 16 kHz mono PCM.")]
    public int sampleRate = 16000;

    [Tooltip("PCM bytes per WebSocket frame. 1280 bytes is about 40 ms at 16 kHz, 16-bit mono.")]
    [Min(320)]
    public int frameBytes = 1280;

    [Tooltip("Delay between sent audio frames.")]
    [Range(10, 200)]
    public int frameIntervalMs = 40;

    [Tooltip("Maximum microphone recording length for one recognition turn.")]
    [Range(5, 60)]
    public int maxRecordSeconds = 55;
}
