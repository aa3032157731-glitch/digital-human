using UnityEngine;

[CreateAssetMenu(menuName = "Voice Chat/Configs/Doubao TTS Config", fileName = "DoubaoTTSConfig")]
public class DoubaoTTSConfig : ScriptableObject
{
    [Header("Credentials")]
    [Tooltip("Volcengine API Key from the new console. Preferred for Doubao TTS V3.")]
    public string apiKey;

    [Tooltip("Legacy App ID. Leave empty when using apiKey.")]
    public string appId;

    [Tooltip("Legacy Access Key. Leave empty when using apiKey.")]
    public string accessKey;

    [Tooltip("Resource ID selects the billing/model version. Use seed-tts-2.0 for Doubao speech synthesis model 2.0.")]
    public string resourceId = "seed-tts-2.0";

    [Header("Endpoint")]
    [Tooltip("Doubao TTS V3 SSE unidirectional endpoint.")]
    public string endpoint = "https://openspeech.bytedance.com/api/v3/tts/unidirectional/sse";

    [Tooltip("Caller-defined user id sent in user.uid.")]
    public string userId = "custom-digitalman";

    [Header("Voice")]
    [Tooltip("Doubao voice id from the Volcengine console or voice list.")]
    public string speaker = "zh_female_vv_uranus_bigtts";

    [Tooltip("Optional model override. Leave empty for the default model selected by resourceId.")]
    public string model;

    [Tooltip("pcm is required by the current Unity streaming player.")]
    public string audioFormat = "pcm";

    [Tooltip("Output sample rate. Doubao supports values such as 16000 and 24000.")]
    public int sampleRate = 24000;

    [Tooltip("Speech rate. Doubao range is -50 to 100, where 0 is normal.")]
    [Range(-50, 100)]
    public int speechRate;

    [Tooltip("Loudness rate. Doubao range is -50 to 100, where 0 is normal.")]
    [Range(-50, 100)]
    public int loudnessRate;

    [Tooltip("Optional Doubao emotion parameter, for example happy, sad, angry. Only supported by some voices.")]
    public string emotion;

    [Tooltip("Emotion scale after emotion is set. Doubao range is 1 to 5.")]
    [Range(1, 5)]
    public int emotionScale = 4;

    [Header("Voice Instruction")]
    [Tooltip("Send additions.context_texts to Doubao TTS 2.0 so the instruction affects expression but is not spoken.")]
    public bool enableVoiceInstruction = true;

    [TextArea(2, 4)]
    [Tooltip("Static fallback voice instruction sent through additions.context_texts.")]
    public string voiceInstruction = "请用自然、亲切、清晰的语气说话。";

    [Tooltip("Ask DeepSeek for one Doubao voice instruction before streaming the assistant answer.")]
    public bool enableDeepSeekVoiceInstruction;

    [TextArea(3, 8)]
    [Tooltip("System prompt used for the optional DeepSeek voice-instruction classifier.")]
    public string deepSeekVoiceInstructionPrompt =
        "你是数字人语音导演。请根据用户话语和面部情绪上下文，为豆包语音合成模型2.0输出一句中文语音指令。只输出一句话，不要解释，不要包含引号，不要超过45个汉字。";

    [Tooltip("Use a stable section_id across short chunk requests so Doubao can reuse TTS 2.0 context.")]
    public bool useSectionId = true;

    [Header("Streaming Text")]
    [Tooltip("Minimum characters to buffer before sending one Doubao TTS request, unless punctuation is hit.")]
    [Range(1, 120)]
    public int minChunkChars = 18;

    [Tooltip("Maximum characters in one Doubao TTS request.")]
    [Range(20, 500)]
    public int maxChunkChars = 160;

    [Tooltip("Delay used by the text sender while it waits for more DeepSeek deltas.")]
    [Range(5, 200)]
    public int textPollIntervalMs = 20;

    [Tooltip("Maximum seconds to wait for one Doubao TTS request.")]
    [Range(5, 120)]
    public int requestTimeoutSeconds = 30;
}
