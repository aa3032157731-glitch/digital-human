using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class RuntimeVoiceChatSettings
{
    public int schemaVersion = 4;
    public List<ChatModelProfile> modelProfiles = new List<ChatModelProfile>();
    public string activeModelProfileId;
    public bool readRepliesAloud = true;
    public DeepSeekRuntimeSettings deepSeek = new DeepSeekRuntimeSettings();
    public BaiduSearchRuntimeSettings baiduSearch = new BaiduSearchRuntimeSettings();
    public XFASRRuntimeSettings xunfeiAsr = new XFASRRuntimeSettings();
    public XFTTSRuntimeSettings xunfeiSuperSmartTts = new XFTTSRuntimeSettings();
    public XFOnlineTTSRuntimeSettings xunfeiOnlineTts = new XFOnlineTTSRuntimeSettings();
    public DoubaoTTSRuntimeSettings doubaoTts = new DoubaoTTSRuntimeSettings();
    public VoiceFeatureRuntimeSettings features = new VoiceFeatureRuntimeSettings();
    public MemoryRuntimeSettings memory = new MemoryRuntimeSettings();
    public KnowledgeRuntimeSettings knowledge = new KnowledgeRuntimeSettings();
    public AgentToolRuntimeSettings agentTools = new AgentToolRuntimeSettings();

    public void EnsureDefaults()
    {
        var loadedSchemaVersion = schemaVersion;
        if (deepSeek == null) deepSeek = new DeepSeekRuntimeSettings();
        if (baiduSearch == null) baiduSearch = new BaiduSearchRuntimeSettings();
        if (xunfeiAsr == null) xunfeiAsr = new XFASRRuntimeSettings();
        if (xunfeiSuperSmartTts == null) xunfeiSuperSmartTts = new XFTTSRuntimeSettings();
        if (xunfeiOnlineTts == null) xunfeiOnlineTts = new XFOnlineTTSRuntimeSettings();
        if (doubaoTts == null) doubaoTts = new DoubaoTTSRuntimeSettings();
        doubaoTts.EnsureDefaults();
        if (features == null) features = new VoiceFeatureRuntimeSettings();
        if (memory == null) memory = new MemoryRuntimeSettings();
        if (knowledge == null) knowledge = new KnowledgeRuntimeSettings();
        if (agentTools == null) agentTools = new AgentToolRuntimeSettings();

        if (loadedSchemaVersion < 2)
        {
            knowledge.injectFullKnowledgeBaseOnNewConversation = true;
        }

        if (modelProfiles == null) modelProfiles = new List<ChatModelProfile>();
        modelProfiles.RemoveAll(p => p == null);
        if (modelProfiles.Count == 0) modelProfiles.Add(ChatModelProfile.FromLegacy(deepSeek));
        var ids = new HashSet<string>();
        foreach (var profile in modelProfiles)
        {
            if (string.IsNullOrEmpty(profile.id) || !ids.Add(profile.id))
            {
                profile.id = Guid.NewGuid().ToString("N");
                ids.Add(profile.id);
            }
        }
        if (!modelProfiles.Exists(p => p.id == activeModelProfileId)) activeModelProfileId = modelProfiles[0].id;
        schemaVersion = 4;
    }
}

[Serializable]
public sealed class DeepSeekRuntimeSettings
{
    public string apiKey;
    public string chatCompletionsUrl = "https://api.deepseek.com/chat/completions";
    public string model = "deepseek-v4-flash";
    public string systemPrompt;
    public float temperature = 0.7f;
    public float topP = 0.9f;
    public int maxTokens = 1024;
    public int maxHistoryMessages = 12;
    public bool disableThinking = true;
    public bool logRequestPayloadToConsole = true;
    public int requestPayloadLogChunkCharacters = 900;
}

[Serializable]
public sealed class BaiduSearchRuntimeSettings
{
    public string apiKey;
    public string authorizationHeaderName = "Authorization";
    public string alternateAuthorizationHeaderName = "X-Appbuilder-Authorization";
    public string endpoint = "https://qianfan.baidubce.com/v2/ai_search/web_search";
    public string edition = "standard";
    public string searchSource = "baidu_search_v2";
    public int maxResults = 6;
    public int webTopK = 10;
    public int videoTopK;
    public int imageTopK;
    public int aladdinTopK;
    public int maxQueryCharacters = 72;
    public int maxContextCharacters = 2400;
    public bool safeSearch = true;
    public bool autoSortFreshQueries = true;
    public int timeoutSeconds = 8;
}

[Serializable]
public sealed class XFASRRuntimeSettings
{
    public string appId;
    public string apiKey;
    public string apiSecret;
    public string websocketUrl = "wss://iat.xf-yun.com/v1";
    public string resId;
    public string domain = "slm";
    public string language = "zh_cn";
    public string accent = "mandarin";
    public int vadEosMs = 6000;
    public string dynamicCorrection = "wpgs";
    public bool enablePunctuation = true;
    public int languageTypeControl = 1;
    public string microphoneDevice;
    public int sampleRate = 16000;
    public int frameBytes = 1280;
    public int frameIntervalMs = 40;
    public int maxRecordSeconds = 55;
}

[Serializable]
public sealed class XFTTSRuntimeSettings
{
    public string appId;
    public string apiKey;
    public string apiSecret;
    public string websocketUrl = "wss://cbm01.cn-huabei-1.xf-yun.com/v1/private/mcd9m97e6";
    public string serviceId = "mcd9m97e6";
    public string voiceName = "x5_lingxiaoxuan_flow";
    public string audioEncoding = "raw";
    public int sampleRate = 24000;
    public int speed = 50;
    public int volume = 50;
    public int pitch = 50;
    public string oralLevel = "mid";
    public bool sparkAssist = true;
    public bool stopSplit;
    public bool remainWrittenStyle;
    public int minChunkChars = 12;
    public int maxChunkChars = 120;
    public int textPollIntervalMs = 20;
}

[Serializable]
public sealed class XFOnlineTTSRuntimeSettings
{
    public string appId;
    public string apiKey;
    public string apiSecret;
    public string websocketUrl = "wss://tts-api.xfyun.cn/v2/tts";
    public string voiceName = "xiaoyan";
    public string audioEncoding = "raw";
    public string audioFormat = "audio/L16;rate=16000";
    public int sampleRate = 16000;
    public string textEncoding = "UTF8";
    public int speed = 50;
    public int volume = 50;
    public int pitch = 50;
    public int backgroundSound;
    public string englishPronunciation;
    public string numberReadMode;
    public int minChunkChars = 18;
    public int maxChunkChars = 120;
    public int textPollIntervalMs = 20;
}

[Serializable]
public sealed class DoubaoTTSRuntimeSettings
{
    private const string DefaultResourceId = "seed-tts-2.0";
    private const string DefaultEndpoint = "https://openspeech.bytedance.com/api/v3/tts/unidirectional/sse";
    private const string DefaultUserId = "custom-digitalman";
    private const string DefaultSpeaker = "zh_female_vv_uranus_bigtts";
    private const string DefaultAudioFormat = "pcm";
    private const string DefaultVoiceInstruction = "请用自然、亲切、清晰的语气说话。";
    private const string DefaultDeepSeekVoiceInstructionPrompt =
        "你是数字人语音导演。请根据用户话语，为豆包语音合成模型2.0输出一句中文语音指令。只输出一句话，不要解释，不要包含引号，不要超过45个汉字。";

    public string apiKey;
    public string appId;
    public string accessKey;
    public string resourceId = DefaultResourceId;
    public string endpoint = DefaultEndpoint;
    public string userId = DefaultUserId;
    public string speaker = DefaultSpeaker;
    public string model;
    public string audioFormat = DefaultAudioFormat;
    public int sampleRate = 24000;
    public int speechRate;
    public int loudnessRate;
    public string emotion;
    public int emotionScale = 4;
    public bool enableVoiceInstruction = true;
    public string voiceInstruction = DefaultVoiceInstruction;
    public bool enableDeepSeekVoiceInstruction;
    public string deepSeekVoiceInstructionPrompt = DefaultDeepSeekVoiceInstructionPrompt;
    public bool useSectionId = true;
    public int minChunkChars = 18;
    public int maxChunkChars = 160;
    public int textPollIntervalMs = 20;
    public int requestTimeoutSeconds = 30;

    public void EnsureDefaults()
    {
        if (string.IsNullOrWhiteSpace(resourceId)) resourceId = DefaultResourceId;
        if (string.IsNullOrWhiteSpace(endpoint)) endpoint = DefaultEndpoint;
        if (string.IsNullOrWhiteSpace(userId)) userId = DefaultUserId;
        if (string.IsNullOrWhiteSpace(speaker)) speaker = DefaultSpeaker;
        if (string.IsNullOrWhiteSpace(audioFormat)) audioFormat = DefaultAudioFormat;
        if (string.IsNullOrWhiteSpace(voiceInstruction)) voiceInstruction = DefaultVoiceInstruction;
        if (string.IsNullOrWhiteSpace(deepSeekVoiceInstructionPrompt)) deepSeekVoiceInstructionPrompt = DefaultDeepSeekVoiceInstructionPrompt;

        if (enableDeepSeekVoiceInstruction)
        {
            enableVoiceInstruction = true;
        }
    }
}

[Serializable]
public sealed class VoiceFeatureRuntimeSettings
{
    public string ttsProvider = TTSProvider.XunfeiSuperSmart.ToString();
    public string talkBoolParameter = "isTalk";
    public string pushToTalkKey = KeyCode.Space.ToString();
    public bool enablePushToTalk = true;
    public bool listenOnStart;
}

[Serializable]
public sealed class MemoryRuntimeSettings
{
    public bool enablePersistentConversation = true;
    public string persistentDataFolder = "VoiceChat";
    public string conversationsFolderName = "Conversations";
    public bool loadLatestConversationOnStart = true;
    public bool includeInterruptedAssistantRepliesInPrompt;
    public int maxRelevantHistoryMessages = 6;
    public int maxRelevantHistoryCharacters = 1800;
    public float streamingHistorySaveIntervalSeconds = 0.75f;
}

[Serializable]
public sealed class KnowledgeRuntimeSettings
{
    public bool enableKnowledgeBase = true;
    public string knowledgeBaseFolderName = "KnowledgeBase";
    public bool refreshKnowledgeBaseOnStart = true;
    public bool injectFullKnowledgeBaseOnNewConversation = true;
    public int maxInitialKnowledgeBaseCharacters;
}

[Serializable]
public sealed class AgentToolRuntimeSettings
{
    public bool enableAgentTools = true;
    public bool enableDateTimeTool = true;
    public bool enableWebSearchTool = true;
    public int maxAgentToolRounds = 2;
}

public static class VoiceChatRuntimeConfigStore
{
#if UNITY_EDITOR
    // Editor integration tests use an isolated store, never the user's saved credentials.
    internal static string EditorSettingsPathOverride;
#endif
    private const string RuntimeConfigFolder = "RuntimeConfig";
    private const string RuntimeConfigFileName = "voice-chat-settings.json";

    public static string SettingsFilePath
    {
        get
        {
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(EditorSettingsPathOverride)) return EditorSettingsPathOverride;
#endif
            return Path.Combine(Application.persistentDataPath, RuntimeConfigFolder, RuntimeConfigFileName);
        }
    }

    public static RuntimeVoiceChatSettings LoadOrCreate(VoiceChatController controller)
    {
        var loaded = LoadSettings();
        if (loaded != null)
        {
            return loaded;
        }

        var settings = CaptureFrom(controller);
        SaveSettings(settings);
        return settings;
    }

    public static RuntimeVoiceChatSettings LoadSettings()
    {
        try
        {
            var path = SettingsFilePath;
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var settings = JsonUtility.FromJson<RuntimeVoiceChatSettings>(json);
            settings?.EnsureDefaults();
            return settings;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Load runtime voice chat settings failed: {ex.Message}");
            return null;
        }
    }

    public static bool SaveSettings(RuntimeVoiceChatSettings settings)
    {
        if (settings == null)
        {
            return false;
        }

        settings.EnsureDefaults();

        try
        {
            var path = SettingsFilePath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonUtility.ToJson(settings, true));
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Save runtime voice chat settings failed: {ex.Message}");
            return false;
        }
    }

    public static bool DeleteSavedSettings()
    {
        try
        {
            var path = SettingsFilePath;
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Delete runtime voice chat settings failed: {ex.Message}");
            return false;
        }
    }

    public static RuntimeVoiceChatSettings CaptureFrom(VoiceChatController controller)
    {
        var settings = new RuntimeVoiceChatSettings();
        settings.EnsureDefaults();

        if (controller == null)
        {
            return settings;
        }

        CaptureDeepSeek(controller.deepSeekConfig, settings.deepSeek);
        settings.modelProfiles.Clear();
        if (controller.modelProfiles != null)
            foreach (var profile in controller.modelProfiles) settings.modelProfiles.Add(profile.Copy());
        settings.activeModelProfileId = controller.activeModelProfileId;
        settings.readRepliesAloud = controller.readRepliesAloud;
        settings.EnsureDefaults();
        CaptureBaiduSearch(controller.baiduSearchConfig, settings.baiduSearch);
        CaptureAsr(controller.asrConfig, settings.xunfeiAsr);
        CaptureSuperSmartTts(controller.ttsConfig, settings.xunfeiSuperSmartTts);
        CaptureOnlineTts(controller.onlineTtsConfig, settings.xunfeiOnlineTts);
        CaptureDoubaoTts(controller.doubaoTtsConfig, settings.doubaoTts);
        CaptureFeatures(controller, settings.features);
        CaptureMemory(controller, settings.memory);
        CaptureKnowledge(controller, settings.knowledge);
        CaptureAgentTools(controller, settings.agentTools);
        return settings;
    }

    public static void ApplySavedSettings(VoiceChatController controller)
    {
        if (controller == null)
        {
            return;
        }

        EnsureRuntimeConfigInstances(controller);
        ApplyTo(controller, LoadOrCreate(controller));
    }

    public static void EnsureRuntimeConfigInstances(VoiceChatController controller)
    {
        if (controller == null)
        {
            return;
        }

        controller.asrConfig = EnsureRuntimeCopy(controller.asrConfig);
        controller.deepSeekConfig = EnsureRuntimeCopy(controller.deepSeekConfig);
        controller.baiduSearchConfig = EnsureRuntimeCopy(controller.baiduSearchConfig);
        controller.ttsConfig = EnsureRuntimeCopy(controller.ttsConfig);
        controller.onlineTtsConfig = EnsureRuntimeCopy(controller.onlineTtsConfig);
        controller.doubaoTtsConfig = EnsureRuntimeCopy(controller.doubaoTtsConfig);
    }

    public static void ApplyTo(VoiceChatController controller, RuntimeVoiceChatSettings settings)
    {
        if (controller == null || settings == null)
        {
            return;
        }

        settings.EnsureDefaults();
        EnsureRuntimeConfigInstances(controller);
        ApplyDeepSeek(controller.deepSeekConfig, settings.deepSeek);
        controller.modelProfiles = settings.modelProfiles.ConvertAll(p => p.Copy());
        controller.activeModelProfileId = settings.activeModelProfileId;
        controller.readRepliesAloud = settings.readRepliesAloud;
        ApplyBaiduSearch(controller.baiduSearchConfig, settings.baiduSearch);
        ApplyAsr(controller.asrConfig, settings.xunfeiAsr);
        ApplySuperSmartTts(controller.ttsConfig, settings.xunfeiSuperSmartTts);
        ApplyOnlineTts(controller.onlineTtsConfig, settings.xunfeiOnlineTts);
        ApplyDoubaoTts(controller.doubaoTtsConfig, settings.doubaoTts);
        ApplyFeatures(controller, settings.features);
        ApplyMemory(controller, settings.memory);
        ApplyKnowledge(controller, settings.knowledge);
        ApplyAgentTools(controller, settings.agentTools);
    }

    private static T EnsureRuntimeCopy<T>(T source) where T : ScriptableObject
    {
        if (source == null)
        {
            return null;
        }

        if ((source.hideFlags & HideFlags.DontSave) == HideFlags.DontSave)
        {
            return source;
        }

        var copy = UnityEngine.Object.Instantiate(source);
        copy.name = source.name + " (Runtime)";
        copy.hideFlags = HideFlags.DontSave;
        return copy;
    }

    private static void CaptureDeepSeek(DeepSeekConfig source, DeepSeekRuntimeSettings target)
    {
        if (source == null || target == null) return;

        target.apiKey = source.apiKey;
        target.chatCompletionsUrl = source.chatCompletionsUrl;
        target.model = source.model;
        target.systemPrompt = source.systemPrompt;
        target.temperature = source.temperature;
        target.topP = source.topP;
        target.maxTokens = source.maxTokens;
        target.maxHistoryMessages = source.maxHistoryMessages;
        target.disableThinking = source.disableThinking;
        target.logRequestPayloadToConsole = source.logRequestPayloadToConsole;
        target.requestPayloadLogChunkCharacters = source.requestPayloadLogChunkCharacters;
    }

    private static void ApplyDeepSeek(DeepSeekConfig target, DeepSeekRuntimeSettings source)
    {
        if (target == null || source == null) return;

        target.apiKey = source.apiKey;
        target.chatCompletionsUrl = source.chatCompletionsUrl;
        target.model = source.model;
        target.systemPrompt = source.systemPrompt;
        target.temperature = Mathf.Clamp(source.temperature, 0f, 2f);
        target.topP = Mathf.Clamp01(source.topP);
        target.maxTokens = Mathf.Max(1, source.maxTokens);
        target.maxHistoryMessages = Mathf.Clamp(source.maxHistoryMessages, 2, 40);
        target.disableThinking = source.disableThinking;
        target.logRequestPayloadToConsole = source.logRequestPayloadToConsole;
        target.requestPayloadLogChunkCharacters = Mathf.Clamp(source.requestPayloadLogChunkCharacters, 200, 4000);
    }

    private static void CaptureBaiduSearch(BaiduSearchConfig source, BaiduSearchRuntimeSettings target)
    {
        if (source == null || target == null) return;

        target.apiKey = source.apiKey;
        target.authorizationHeaderName = source.authorizationHeaderName;
        target.alternateAuthorizationHeaderName = source.alternateAuthorizationHeaderName;
        target.endpoint = source.endpoint;
        target.edition = source.edition;
        target.searchSource = source.searchSource;
        target.maxResults = source.maxResults;
        target.webTopK = source.webTopK;
        target.videoTopK = source.videoTopK;
        target.imageTopK = source.imageTopK;
        target.aladdinTopK = source.aladdinTopK;
        target.maxQueryCharacters = source.maxQueryCharacters;
        target.maxContextCharacters = source.maxContextCharacters;
        target.safeSearch = source.safeSearch;
        target.autoSortFreshQueries = source.autoSortFreshQueries;
        target.timeoutSeconds = source.timeoutSeconds;
    }

    private static void ApplyBaiduSearch(BaiduSearchConfig target, BaiduSearchRuntimeSettings source)
    {
        if (target == null || source == null) return;

        target.apiKey = source.apiKey;
        target.authorizationHeaderName = string.IsNullOrWhiteSpace(source.authorizationHeaderName) ? "Authorization" : source.authorizationHeaderName;
        target.alternateAuthorizationHeaderName = source.alternateAuthorizationHeaderName;
        target.endpoint = string.IsNullOrWhiteSpace(source.endpoint) ? "https://qianfan.baidubce.com/v2/ai_search/web_search" : source.endpoint;
        target.edition = string.Equals(source.edition, "lite", StringComparison.OrdinalIgnoreCase) ? "lite" : "standard";
        target.searchSource = string.IsNullOrWhiteSpace(source.searchSource) ? "baidu_search_v2" : source.searchSource;
        target.maxResults = Mathf.Clamp(source.maxResults, 1, 20);
        target.webTopK = Mathf.Clamp(source.webTopK, 1, 50);
        target.videoTopK = Mathf.Clamp(source.videoTopK, 0, 10);
        target.imageTopK = Mathf.Clamp(source.imageTopK, 0, 30);
        target.aladdinTopK = Mathf.Clamp(source.aladdinTopK, 0, 5);
        target.maxQueryCharacters = Mathf.Clamp(source.maxQueryCharacters, 20, 200);
        target.maxContextCharacters = Mathf.Clamp(source.maxContextCharacters, 500, 6000);
        target.safeSearch = source.safeSearch;
        target.autoSortFreshQueries = source.autoSortFreshQueries;
        target.timeoutSeconds = Mathf.Clamp(source.timeoutSeconds, 1, 30);
    }

    private static void CaptureAsr(XFASRConfig source, XFASRRuntimeSettings target)
    {
        if (source == null || target == null) return;

        target.appId = source.appId;
        target.apiKey = source.apiKey;
        target.apiSecret = source.apiSecret;
        target.websocketUrl = source.websocketUrl;
        target.resId = source.resId;
        target.domain = source.domain;
        target.language = source.language;
        target.accent = source.accent;
        target.vadEosMs = source.vadEosMs;
        target.dynamicCorrection = source.dynamicCorrection;
        target.enablePunctuation = source.enablePunctuation;
        target.languageTypeControl = source.languageTypeControl;
        target.microphoneDevice = source.microphoneDevice;
        target.sampleRate = source.sampleRate;
        target.frameBytes = source.frameBytes;
        target.frameIntervalMs = source.frameIntervalMs;
        target.maxRecordSeconds = source.maxRecordSeconds;
    }

    private static void ApplyAsr(XFASRConfig target, XFASRRuntimeSettings source)
    {
        if (target == null || source == null) return;

        target.appId = source.appId;
        target.apiKey = source.apiKey;
        target.apiSecret = source.apiSecret;
        target.websocketUrl = source.websocketUrl;
        target.resId = source.resId;
        target.domain = source.domain;
        target.language = source.language;
        target.accent = source.accent;
        target.vadEosMs = Mathf.Clamp(source.vadEosMs, 500, 10000);
        target.dynamicCorrection = source.dynamicCorrection;
        target.enablePunctuation = source.enablePunctuation;
        target.languageTypeControl = Mathf.Clamp(source.languageTypeControl, 1, 3);
        target.microphoneDevice = source.microphoneDevice;
        target.sampleRate = Mathf.Max(8000, source.sampleRate);
        target.frameBytes = Mathf.Max(320, source.frameBytes);
        target.frameIntervalMs = Mathf.Clamp(source.frameIntervalMs, 10, 200);
        target.maxRecordSeconds = Mathf.Clamp(source.maxRecordSeconds, 5, 60);
    }

    private static void CaptureSuperSmartTts(XFTTSConfig source, XFTTSRuntimeSettings target)
    {
        if (source == null || target == null) return;

        target.appId = source.appId;
        target.apiKey = source.apiKey;
        target.apiSecret = source.apiSecret;
        target.websocketUrl = source.websocketUrl;
        target.serviceId = source.serviceId;
        target.voiceName = source.voiceName;
        target.audioEncoding = source.audioEncoding;
        target.sampleRate = source.sampleRate;
        target.speed = source.speed;
        target.volume = source.volume;
        target.pitch = source.pitch;
        target.oralLevel = source.oralLevel;
        target.sparkAssist = source.sparkAssist;
        target.stopSplit = source.stopSplit;
        target.remainWrittenStyle = source.remainWrittenStyle;
        target.minChunkChars = source.minChunkChars;
        target.maxChunkChars = source.maxChunkChars;
        target.textPollIntervalMs = source.textPollIntervalMs;
    }

    private static void ApplySuperSmartTts(XFTTSConfig target, XFTTSRuntimeSettings source)
    {
        if (target == null || source == null) return;

        target.appId = source.appId;
        target.apiKey = source.apiKey;
        target.apiSecret = source.apiSecret;
        target.websocketUrl = source.websocketUrl;
        target.serviceId = source.serviceId;
        target.voiceName = source.voiceName;
        target.audioEncoding = source.audioEncoding;
        target.sampleRate = Mathf.Max(8000, source.sampleRate);
        target.speed = Mathf.Clamp(source.speed, 0, 100);
        target.volume = Mathf.Clamp(source.volume, 0, 100);
        target.pitch = Mathf.Clamp(source.pitch, 0, 100);
        target.oralLevel = source.oralLevel;
        target.sparkAssist = source.sparkAssist;
        target.stopSplit = source.stopSplit;
        target.remainWrittenStyle = source.remainWrittenStyle;
        target.minChunkChars = Mathf.Clamp(source.minChunkChars, 1, 80);
        target.maxChunkChars = Mathf.Clamp(source.maxChunkChars, 20, 400);
        target.textPollIntervalMs = Mathf.Clamp(source.textPollIntervalMs, 5, 200);
    }

    private static void CaptureOnlineTts(XFOnlineTTSConfig source, XFOnlineTTSRuntimeSettings target)
    {
        if (source == null || target == null) return;

        target.appId = source.appId;
        target.apiKey = source.apiKey;
        target.apiSecret = source.apiSecret;
        target.websocketUrl = source.websocketUrl;
        target.voiceName = source.voiceName;
        target.audioEncoding = source.audioEncoding;
        target.audioFormat = source.audioFormat;
        target.sampleRate = source.sampleRate;
        target.textEncoding = source.textEncoding;
        target.speed = source.speed;
        target.volume = source.volume;
        target.pitch = source.pitch;
        target.backgroundSound = source.backgroundSound;
        target.englishPronunciation = source.englishPronunciation;
        target.numberReadMode = source.numberReadMode;
        target.minChunkChars = source.minChunkChars;
        target.maxChunkChars = source.maxChunkChars;
        target.textPollIntervalMs = source.textPollIntervalMs;
    }

    private static void ApplyOnlineTts(XFOnlineTTSConfig target, XFOnlineTTSRuntimeSettings source)
    {
        if (target == null || source == null) return;

        target.appId = source.appId;
        target.apiKey = source.apiKey;
        target.apiSecret = source.apiSecret;
        target.websocketUrl = source.websocketUrl;
        target.voiceName = source.voiceName;
        target.audioEncoding = source.audioEncoding;
        target.audioFormat = source.audioFormat;
        target.sampleRate = Mathf.Max(8000, source.sampleRate);
        target.textEncoding = source.textEncoding;
        target.speed = Mathf.Clamp(source.speed, 0, 100);
        target.volume = Mathf.Clamp(source.volume, 0, 100);
        target.pitch = Mathf.Clamp(source.pitch, 0, 100);
        target.backgroundSound = source.backgroundSound;
        target.englishPronunciation = source.englishPronunciation;
        target.numberReadMode = source.numberReadMode;
        target.minChunkChars = Mathf.Clamp(source.minChunkChars, 1, 80);
        target.maxChunkChars = Mathf.Clamp(source.maxChunkChars, 20, 400);
        target.textPollIntervalMs = Mathf.Clamp(source.textPollIntervalMs, 5, 200);
    }

    private static void CaptureDoubaoTts(DoubaoTTSConfig source, DoubaoTTSRuntimeSettings target)
    {
        if (source == null || target == null) return;

        target.apiKey = source.apiKey;
        target.appId = source.appId;
        target.accessKey = source.accessKey;
        target.resourceId = source.resourceId;
        target.endpoint = source.endpoint;
        target.userId = source.userId;
        target.speaker = string.IsNullOrWhiteSpace(source.speaker) ? "zh_female_vv_uranus_bigtts" : source.speaker;
        target.model = source.model;
        target.audioFormat = source.audioFormat;
        target.sampleRate = source.sampleRate;
        target.speechRate = source.speechRate;
        target.loudnessRate = source.loudnessRate;
        target.emotion = source.emotion;
        target.emotionScale = source.emotionScale;
        target.enableVoiceInstruction = source.enableVoiceInstruction || source.enableDeepSeekVoiceInstruction;
        target.voiceInstruction = string.IsNullOrWhiteSpace(source.voiceInstruction)
            ? "请用自然、亲切、清晰的语气说话。"
            : source.voiceInstruction;
        target.enableDeepSeekVoiceInstruction = source.enableDeepSeekVoiceInstruction;
        target.deepSeekVoiceInstructionPrompt = source.deepSeekVoiceInstructionPrompt;
        target.useSectionId = source.useSectionId;
        target.minChunkChars = source.minChunkChars;
        target.maxChunkChars = source.maxChunkChars;
        target.textPollIntervalMs = source.textPollIntervalMs;
        target.requestTimeoutSeconds = source.requestTimeoutSeconds;
    }

    private static void ApplyDoubaoTts(DoubaoTTSConfig target, DoubaoTTSRuntimeSettings source)
    {
        if (target == null || source == null) return;

        target.apiKey = source.apiKey;
        target.appId = source.appId;
        target.accessKey = source.accessKey;
        target.resourceId = string.IsNullOrWhiteSpace(source.resourceId) ? "seed-tts-2.0" : source.resourceId;
        target.endpoint = string.IsNullOrWhiteSpace(source.endpoint)
            ? "https://openspeech.bytedance.com/api/v3/tts/unidirectional/sse"
            : source.endpoint;
        target.userId = string.IsNullOrWhiteSpace(source.userId) ? "custom-digitalman" : source.userId;
        target.speaker = string.IsNullOrWhiteSpace(source.speaker) ? "zh_female_vv_uranus_bigtts" : source.speaker;
        target.model = source.model;
        target.audioFormat = string.IsNullOrWhiteSpace(source.audioFormat) ? "pcm" : source.audioFormat;
        target.sampleRate = Mathf.Max(8000, source.sampleRate);
        target.speechRate = Mathf.Clamp(source.speechRate, -50, 100);
        target.loudnessRate = Mathf.Clamp(source.loudnessRate, -50, 100);
        target.emotion = source.emotion;
        target.emotionScale = Mathf.Clamp(source.emotionScale, 1, 5);
        target.enableVoiceInstruction = source.enableVoiceInstruction || source.enableDeepSeekVoiceInstruction;
        target.voiceInstruction = string.IsNullOrWhiteSpace(source.voiceInstruction)
            ? "请用自然、亲切、清晰的语气说话。"
            : source.voiceInstruction;
        target.enableDeepSeekVoiceInstruction = source.enableDeepSeekVoiceInstruction;
        target.deepSeekVoiceInstructionPrompt = string.IsNullOrWhiteSpace(source.deepSeekVoiceInstructionPrompt)
            ? "你是数字人语音导演。请根据用户话语，为豆包语音合成模型2.0输出一句中文语音指令。只输出一句话，不要解释，不要包含引号，不要超过45个汉字。"
            : source.deepSeekVoiceInstructionPrompt;
        target.useSectionId = source.useSectionId;
        target.minChunkChars = Mathf.Clamp(source.minChunkChars, 1, 120);
        target.maxChunkChars = Mathf.Clamp(source.maxChunkChars, 20, 500);
        target.textPollIntervalMs = Mathf.Clamp(source.textPollIntervalMs, 5, 200);
        target.requestTimeoutSeconds = Mathf.Clamp(source.requestTimeoutSeconds, 5, 120);
    }

    private static void CaptureFeatures(VoiceChatController source, VoiceFeatureRuntimeSettings target)
    {
        if (source == null || target == null) return;

        target.ttsProvider = source.ttsProvider.ToString();
        target.talkBoolParameter = source.talkBoolParameter;
        target.pushToTalkKey = source.pushToTalkKey.ToString();
        target.enablePushToTalk = source.enablePushToTalk;
        target.listenOnStart = source.listenOnStart;
    }

    private static void ApplyFeatures(VoiceChatController target, VoiceFeatureRuntimeSettings source)
    {
        if (target == null || source == null) return;

        if (TryParseTtsProvider(source.ttsProvider, out var provider))
        {
            target.ttsProvider = provider;
        }

        target.talkBoolParameter = source.talkBoolParameter;

        if (Enum.TryParse(source.pushToTalkKey, true, out KeyCode keyCode))
        {
            target.pushToTalkKey = keyCode;
        }

        target.enablePushToTalk = source.enablePushToTalk;
        target.listenOnStart = source.listenOnStart;
    }

    private static bool TryParseTtsProvider(string value, out TTSProvider provider)
    {
        if (Enum.TryParse(value, true, out provider))
        {
            return true;
        }

        if (string.Equals(value, "SuperSmart", StringComparison.OrdinalIgnoreCase))
        {
            provider = TTSProvider.XunfeiSuperSmart;
            return true;
        }

        if (string.Equals(value, "Online", StringComparison.OrdinalIgnoreCase))
        {
            provider = TTSProvider.XunfeiOnline;
            return true;
        }

        provider = TTSProvider.XunfeiSuperSmart;
        return false;
    }

    private static void CaptureMemory(VoiceChatController source, MemoryRuntimeSettings target)
    {
        if (source == null || target == null) return;

        target.enablePersistentConversation = source.enablePersistentConversation;
        target.persistentDataFolder = source.persistentDataFolder;
        target.conversationsFolderName = source.conversationsFolderName;
        target.loadLatestConversationOnStart = source.loadLatestConversationOnStart;
        target.includeInterruptedAssistantRepliesInPrompt = source.includeInterruptedAssistantRepliesInPrompt;
        target.maxRelevantHistoryMessages = source.maxRelevantHistoryMessages;
        target.maxRelevantHistoryCharacters = source.maxRelevantHistoryCharacters;
        target.streamingHistorySaveIntervalSeconds = source.streamingHistorySaveIntervalSeconds;
    }

    private static void ApplyMemory(VoiceChatController target, MemoryRuntimeSettings source)
    {
        if (target == null || source == null) return;

        target.enablePersistentConversation = source.enablePersistentConversation;
        target.persistentDataFolder = source.persistentDataFolder;
        target.conversationsFolderName = source.conversationsFolderName;
        target.loadLatestConversationOnStart = source.loadLatestConversationOnStart;
        target.includeInterruptedAssistantRepliesInPrompt = source.includeInterruptedAssistantRepliesInPrompt;
        target.maxRelevantHistoryMessages = Mathf.Clamp(source.maxRelevantHistoryMessages, 0, 20);
        target.maxRelevantHistoryCharacters = Mathf.Clamp(source.maxRelevantHistoryCharacters, 0, 8000);
        target.streamingHistorySaveIntervalSeconds = Mathf.Clamp(source.streamingHistorySaveIntervalSeconds, 0.1f, 5f);
    }

    private static void CaptureKnowledge(VoiceChatController source, KnowledgeRuntimeSettings target)
    {
        if (source == null || target == null) return;

        target.enableKnowledgeBase = source.enableKnowledgeBase;
        target.knowledgeBaseFolderName = source.knowledgeBaseFolderName;
        target.refreshKnowledgeBaseOnStart = source.refreshKnowledgeBaseOnStart;
        target.injectFullKnowledgeBaseOnNewConversation = source.injectFullKnowledgeBaseOnNewConversation;
        target.maxInitialKnowledgeBaseCharacters = source.maxInitialKnowledgeBaseCharacters;
    }

    private static void ApplyKnowledge(VoiceChatController target, KnowledgeRuntimeSettings source)
    {
        if (target == null || source == null) return;

        target.enableKnowledgeBase = source.enableKnowledgeBase;
        target.knowledgeBaseFolderName = source.knowledgeBaseFolderName;
        target.refreshKnowledgeBaseOnStart = source.refreshKnowledgeBaseOnStart;
        target.injectFullKnowledgeBaseOnNewConversation = source.injectFullKnowledgeBaseOnNewConversation;
        target.maxInitialKnowledgeBaseCharacters = Mathf.Clamp(source.maxInitialKnowledgeBaseCharacters, 0, 100000);
    }

    private static void CaptureAgentTools(VoiceChatController source, AgentToolRuntimeSettings target)
    {
        if (source == null || target == null) return;

        target.enableAgentTools = source.enableAgentTools;
        target.enableDateTimeTool = source.enableDateTimeTool;
        target.enableWebSearchTool = source.enableWebSearchTool;
        target.maxAgentToolRounds = source.maxAgentToolRounds;
    }

    private static void ApplyAgentTools(VoiceChatController target, AgentToolRuntimeSettings source)
    {
        if (target == null || source == null) return;

        target.enableAgentTools = source.enableAgentTools;
        target.enableDateTimeTool = source.enableDateTimeTool;
        target.enableWebSearchTool = source.enableWebSearchTool;
        target.maxAgentToolRounds = Mathf.Clamp(source.maxAgentToolRounds, 0, 4);
    }

    public static string FormatFloat(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
