using UnityEngine;

[CreateAssetMenu(menuName = "Voice Chat/Configs/DeepSeek Config", fileName = "DeepSeekConfig")]
public class DeepSeekConfig : ScriptableObject
{
    [Header("Credentials")]
    [Tooltip("DeepSeek API key. Sent as Authorization: Bearer <apiKey>.")]
    public string apiKey;

    [Header("Endpoint")]
    [Tooltip("DeepSeek chat completions endpoint.")]
    public string chatCompletionsUrl = "https://api.deepseek.com/chat/completions";

    [Tooltip("Fast non-thinking V4 model.")]
    public string model = "deepseek-v4-flash";

    [Header("Prompt")]
    [TextArea(3, 10)]
    public string systemPrompt = "你是一个实时语音数字人助手。请用自然、简洁的中文回答。";

    [Header("Generation")]
    [Range(0f, 2f)]
    public float temperature = 0.7f;

    [Range(0f, 1f)]
    public float topP = 0.9f;

    [Min(1)]
    public int maxTokens = 1024;

    [Tooltip("Conversation messages retained after each turn. System prompt is not counted.")]
    [Range(2, 40)]
    public int maxHistoryMessages = 12;

    [Tooltip("Send thinking.type=disabled for non-thinking mode.")]
    public bool disableThinking = true;

    [Header("Debug Logging")]
    [Tooltip("Log the final DeepSeek HTTP JSON request body to Unity Console. Authorization headers are never logged.")]
    public bool logRequestPayloadToConsole = true;

    [Tooltip("Maximum characters per Unity Console entry when logging the request body.")]
    [Range(200, 4000)]
    public int requestPayloadLogChunkCharacters = 900;
}
