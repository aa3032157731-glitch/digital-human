using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

public sealed class AdvancedSettingsPanel : MonoBehaviour
{
    [Header("Source")]
    public VoiceChatController controller;

    [Header("Panel")]
    public GameObject panelRoot;
    public Button settingsButton;
    public Button switchModelButton;
    public Text statusText;
    public CharacterModelSwitcher modelSwitcher;
    public bool startVisible;
    public string defaultPageName = "Page_ApiKeys";

    private readonly List<Binding> bindings = new List<Binding>();
    private readonly Dictionary<string, GameObject> pagesByName = new Dictionary<string, GameObject>();
    private RuntimeVoiceChatSettings workingSettings;
    private bool initialized;

    private void Awake()
    {
        Initialize();
    }

    private void OnEnable()
    {
        Initialize();
        LoadFromController();
    }

    public void TogglePanel()
    {
        Initialize();
        SetPanelVisible(panelRoot == null || !panelRoot.activeSelf);
    }

    public void OpenPanel()
    {
        Initialize();
        SetPanelVisible(true);
    }

    public void ClosePanel()
    {
        Initialize();
        SetPanelVisible(false);
    }

    public void ShowPageByName(string pageName)
    {
        Initialize();
        if (string.IsNullOrWhiteSpace(pageName))
        {
            return;
        }

        foreach (var page in pagesByName)
        {
            if (page.Value != null)
            {
                page.Value.SetActive(string.Equals(page.Key, pageName, StringComparison.Ordinal));
            }
        }
    }

    public void SaveAndApply()
    {
        Initialize();
        if (controller == null)
        {
            SetStatus("未绑定 VoiceChatController");
            return;
        }

        ApplyControlsToSettings();
        controller.ApplyRuntimeSettings(workingSettings);

        if (VoiceChatRuntimeConfigStore.SaveSettings(workingSettings))
        {
            LoadFromController();
            SetStatus("已保存：" + VoiceChatRuntimeConfigStore.SettingsFilePath);
        }
        else
        {
            SetStatus("保存失败，请查看 Unity Console");
        }
    }

    public void ReloadSaved()
    {
        Initialize();
        if (controller == null)
        {
            SetStatus("未绑定 VoiceChatController");
            return;
        }

        var saved = VoiceChatRuntimeConfigStore.LoadOrCreate(controller);
        controller.ApplyRuntimeSettings(saved);
        workingSettings = controller.CaptureRuntimeSettings();
        PushSettingsToControls();
        SetStatus("已重新读取：" + VoiceChatRuntimeConfigStore.SettingsFilePath);
    }

    public void RebuildConfigFileFromCurrentValues()
    {
        Initialize();
        if (controller == null)
        {
            SetStatus("未绑定 VoiceChatController");
            return;
        }

        VoiceChatRuntimeConfigStore.DeleteSavedSettings();
        workingSettings = controller.CaptureRuntimeSettings();
        VoiceChatRuntimeConfigStore.SaveSettings(workingSettings);
        PushSettingsToControls();
        SetStatus("已用当前场景默认值重建配置文件");
    }

    public void LoadFromController()
    {
        Initialize();
        if (controller == null)
        {
            SetStatus("未绑定 VoiceChatController");
            return;
        }

        workingSettings = controller.CaptureRuntimeSettings();
        PushSettingsToControls();
        SetStatus("配置文件：" + VoiceChatRuntimeConfigStore.SettingsFilePath);
    }

    private void Initialize()
    {
        if (initialized)
        {
            return;
        }

        if (controller == null)
        {
            controller = FindObjectOfType<VoiceChatController>();
        }

        if (panelRoot == null)
        {
            panelRoot = FindGameObject("AdvancedSettingsPanel");
        }

        if (settingsButton == null)
        {
            settingsButton = FindComponent<Button>("AdvancedSettingsButton");
        }

        if (switchModelButton == null)
        {
            switchModelButton = FindComponent<Button>("SwitchModelButton");
        }

        if (statusText == null)
        {
            statusText = FindComponent<Text>("StatusText");
        }

        if (modelSwitcher == null)
        {
            modelSwitcher = FindObjectOfType<CharacterModelSwitcher>(true);
        }

        BuildBindings();
        CachePages();
        WireButtons();

        initialized = true;
        ShowPageByName(string.IsNullOrWhiteSpace(defaultPageName) ? "Page_ApiKeys" : defaultPageName);
        SetPanelVisible(startVisible);
    }

    private void SetPanelVisible(bool visible)
    {
        if (panelRoot == null)
        {
            return;
        }

        panelRoot.SetActive(visible);
        if (visible)
        {
            LoadFromController();
        }
    }

    private void CachePages()
    {
        pagesByName.Clear();
        var transforms = GetComponentsInChildren<Transform>(true);
        foreach (var child in transforms)
        {
            if (child == null || !child.name.StartsWith("Page_", StringComparison.Ordinal))
            {
                continue;
            }

            pagesByName[child.name] = child.gameObject;
        }
    }

    private void WireButtons()
    {
        WireButton(settingsButton, TogglePanel);
        WireButton(FindComponent<Button>("CloseButton"), ClosePanel);
        WireButton(FindComponent<Button>("ApplyButton"), SaveAndApply);
        WireButton(FindComponent<Button>("ReloadButton"), ReloadSaved);
        WireButton(FindComponent<Button>("ResetButton"), RebuildConfigFileFromCurrentValues);
        WireButton(switchModelButton, ToggleModel);
        WirePageButton("Tab_ApiKeys", "Page_ApiKeys");
        WireTtsPageButton("Tab_TtsSuper", "Page_TtsSuper", TTSProvider.XunfeiSuperSmart);
        WireTtsPageButton("Tab_TtsOnline", "Page_TtsOnline", TTSProvider.XunfeiOnline);
        WireTtsPageButton("Tab_TtsDoubao", "Page_TtsDoubao", TTSProvider.Doubao);
        WirePageButton("Tab_Asr", "Page_Asr");
        WirePageButton("Tab_DeepSeek", "Page_DeepSeek");
        WirePageButton("Tab_BaiduSearch", "Page_BaiduSearch");
        WirePageButton("Tab_Features", "Page_Features");
        WirePageButton("Tab_Memory", "Page_Memory");
        WirePageButton("Tab_Knowledge", "Page_Knowledge");
        WireTtsProviderButton("Button_TTSProvider_XunfeiSuperSmart", TTSProvider.XunfeiSuperSmart);
        WireTtsProviderButton("Button_TTSProvider_XunfeiOnline", TTSProvider.XunfeiOnline);
        WireTtsProviderButton("Button_TTSProvider_Doubao", TTSProvider.Doubao);
    }

    private void WirePageButton(string buttonName, string pageName)
    {
        var button = FindComponent<Button>(buttonName);
        WireButton(button, () => ShowPageByName(pageName));
    }

    private void WireTtsPageButton(string buttonName, string pageName, TTSProvider provider)
    {
        var button = FindComponent<Button>(buttonName);
        WireButton(button, () =>
        {
            ShowPageByName(pageName);
            SelectTtsProvider(provider);
        });
    }

    private void WireTtsProviderButton(string buttonName, TTSProvider provider)
    {
        var button = FindComponent<Button>(buttonName);
        WireButton(button, () => SelectTtsProvider(provider));
    }

    private void SelectTtsProvider(TTSProvider provider)
    {
        Initialize();
        ApplyControlsToSettings();
        workingSettings.EnsureDefaults();
        workingSettings.features.ttsProvider = provider.ToString();
        PushSettingsToControls();
        SetStatus("TTS 提供方已切换为：" + GetTtsProviderDisplayName(provider) + "，点击“应用并保存”后生效");
    }

    private static string GetTtsProviderDisplayName(TTSProvider provider)
    {
        switch (provider)
        {
            case TTSProvider.XunfeiOnline:
                return "讯飞语音合成";
            case TTSProvider.Doubao:
                return "豆包语音合成";
            case TTSProvider.XunfeiSuperSmart:
            default:
                return "讯飞超拟人";
        }
    }

    public void ToggleModel()
    {
        Initialize();
        if (modelSwitcher == null)
        {
            SetStatus("未绑定 CharacterModelSwitcher");
            return;
        }

        modelSwitcher.ToggleModel();
        SetStatus("当前模型：" + modelSwitcher.GetCurrentModelName());
    }

    private static void WireButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null || action == null)
        {
            return;
        }

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private void BuildBindings()
    {
        bindings.Clear();

        BindInput("DeepSeek_ApiKey", s => s.deepSeek.apiKey, (s, v) => s.deepSeek.apiKey = v);
        BindInput("BaiduSearch_ApiKey", s => s.baiduSearch.apiKey, (s, v) => s.baiduSearch.apiKey = v);
        BindInput("XFASR_AppId", s => s.xunfeiAsr.appId, (s, v) => s.xunfeiAsr.appId = v);
        BindInput("XFASR_ApiKey", s => s.xunfeiAsr.apiKey, (s, v) => s.xunfeiAsr.apiKey = v);
        BindInput("XFASR_ApiSecret", s => s.xunfeiAsr.apiSecret, (s, v) => s.xunfeiAsr.apiSecret = v);
        BindInput("XFTTS_AppId", s => s.xunfeiSuperSmartTts.appId, (s, v) => s.xunfeiSuperSmartTts.appId = v);
        BindInput("XFTTS_ApiKey", s => s.xunfeiSuperSmartTts.apiKey, (s, v) => s.xunfeiSuperSmartTts.apiKey = v);
        BindInput("XFTTS_ApiSecret", s => s.xunfeiSuperSmartTts.apiSecret, (s, v) => s.xunfeiSuperSmartTts.apiSecret = v);
        BindInput("XFOnlineTTS_AppId", s => s.xunfeiOnlineTts.appId, (s, v) => s.xunfeiOnlineTts.appId = v);
        BindInput("XFOnlineTTS_ApiKey", s => s.xunfeiOnlineTts.apiKey, (s, v) => s.xunfeiOnlineTts.apiKey = v);
        BindInput("XFOnlineTTS_ApiSecret", s => s.xunfeiOnlineTts.apiSecret, (s, v) => s.xunfeiOnlineTts.apiSecret = v);
        BindInput("DoubaoTTS_ApiKey", s => s.doubaoTts.apiKey, (s, v) => s.doubaoTts.apiKey = v);
        BindInput("DoubaoTTS_AppId", s => s.doubaoTts.appId, (s, v) => s.doubaoTts.appId = v);
        BindInput("DoubaoTTS_AccessKey", s => s.doubaoTts.accessKey, (s, v) => s.doubaoTts.accessKey = v);

        BindInput("XFTTS_WebsocketUrl", s => s.xunfeiSuperSmartTts.websocketUrl, (s, v) => s.xunfeiSuperSmartTts.websocketUrl = v);
        BindInput("XFTTS_ServiceId", s => s.xunfeiSuperSmartTts.serviceId, (s, v) => s.xunfeiSuperSmartTts.serviceId = v);
        BindInput("XFTTS_VoiceName", s => s.xunfeiSuperSmartTts.voiceName, (s, v) => s.xunfeiSuperSmartTts.voiceName = v);
        BindInput("XFTTS_AudioEncoding", s => s.xunfeiSuperSmartTts.audioEncoding, (s, v) => s.xunfeiSuperSmartTts.audioEncoding = v);
        BindIntInput("XFTTS_SampleRate", s => s.xunfeiSuperSmartTts.sampleRate, (s, v) => s.xunfeiSuperSmartTts.sampleRate = v);
        BindIntInput("XFTTS_Speed", s => s.xunfeiSuperSmartTts.speed, (s, v) => s.xunfeiSuperSmartTts.speed = v);
        BindIntInput("XFTTS_Volume", s => s.xunfeiSuperSmartTts.volume, (s, v) => s.xunfeiSuperSmartTts.volume = v);
        BindIntInput("XFTTS_Pitch", s => s.xunfeiSuperSmartTts.pitch, (s, v) => s.xunfeiSuperSmartTts.pitch = v);
        BindInput("XFTTS_OralLevel", s => s.xunfeiSuperSmartTts.oralLevel, (s, v) => s.xunfeiSuperSmartTts.oralLevel = v);
        BindToggle("XFTTS_SparkAssist", s => s.xunfeiSuperSmartTts.sparkAssist, (s, v) => s.xunfeiSuperSmartTts.sparkAssist = v);
        BindToggle("XFTTS_StopSplit", s => s.xunfeiSuperSmartTts.stopSplit, (s, v) => s.xunfeiSuperSmartTts.stopSplit = v);
        BindToggle("XFTTS_RemainWrittenStyle", s => s.xunfeiSuperSmartTts.remainWrittenStyle, (s, v) => s.xunfeiSuperSmartTts.remainWrittenStyle = v);
        BindIntInput("XFTTS_MinChunkChars", s => s.xunfeiSuperSmartTts.minChunkChars, (s, v) => s.xunfeiSuperSmartTts.minChunkChars = v);
        BindIntInput("XFTTS_MaxChunkChars", s => s.xunfeiSuperSmartTts.maxChunkChars, (s, v) => s.xunfeiSuperSmartTts.maxChunkChars = v);
        BindIntInput("XFTTS_TextPollIntervalMs", s => s.xunfeiSuperSmartTts.textPollIntervalMs, (s, v) => s.xunfeiSuperSmartTts.textPollIntervalMs = v);

        BindInput("XFOnlineTTS_WebsocketUrl", s => s.xunfeiOnlineTts.websocketUrl, (s, v) => s.xunfeiOnlineTts.websocketUrl = v);
        BindInput("XFOnlineTTS_VoiceName", s => s.xunfeiOnlineTts.voiceName, (s, v) => s.xunfeiOnlineTts.voiceName = v);
        BindInput("XFOnlineTTS_AudioEncoding", s => s.xunfeiOnlineTts.audioEncoding, (s, v) => s.xunfeiOnlineTts.audioEncoding = v);
        BindInput("XFOnlineTTS_AudioFormat", s => s.xunfeiOnlineTts.audioFormat, (s, v) => s.xunfeiOnlineTts.audioFormat = v);
        BindIntInput("XFOnlineTTS_SampleRate", s => s.xunfeiOnlineTts.sampleRate, (s, v) => s.xunfeiOnlineTts.sampleRate = v);
        BindInput("XFOnlineTTS_TextEncoding", s => s.xunfeiOnlineTts.textEncoding, (s, v) => s.xunfeiOnlineTts.textEncoding = v);
        BindIntInput("XFOnlineTTS_Speed", s => s.xunfeiOnlineTts.speed, (s, v) => s.xunfeiOnlineTts.speed = v);
        BindIntInput("XFOnlineTTS_Volume", s => s.xunfeiOnlineTts.volume, (s, v) => s.xunfeiOnlineTts.volume = v);
        BindIntInput("XFOnlineTTS_Pitch", s => s.xunfeiOnlineTts.pitch, (s, v) => s.xunfeiOnlineTts.pitch = v);
        BindIntInput("XFOnlineTTS_BackgroundSound", s => s.xunfeiOnlineTts.backgroundSound, (s, v) => s.xunfeiOnlineTts.backgroundSound = v);
        BindInput("XFOnlineTTS_EnglishPronunciation", s => s.xunfeiOnlineTts.englishPronunciation, (s, v) => s.xunfeiOnlineTts.englishPronunciation = v);
        BindInput("XFOnlineTTS_NumberReadMode", s => s.xunfeiOnlineTts.numberReadMode, (s, v) => s.xunfeiOnlineTts.numberReadMode = v);
        BindIntInput("XFOnlineTTS_MinChunkChars", s => s.xunfeiOnlineTts.minChunkChars, (s, v) => s.xunfeiOnlineTts.minChunkChars = v);
        BindIntInput("XFOnlineTTS_MaxChunkChars", s => s.xunfeiOnlineTts.maxChunkChars, (s, v) => s.xunfeiOnlineTts.maxChunkChars = v);
        BindIntInput("XFOnlineTTS_TextPollIntervalMs", s => s.xunfeiOnlineTts.textPollIntervalMs, (s, v) => s.xunfeiOnlineTts.textPollIntervalMs = v);

        BindInput("DoubaoTTS_Endpoint", s => s.doubaoTts.endpoint, (s, v) => s.doubaoTts.endpoint = v);
        BindInput("DoubaoTTS_ResourceId", s => s.doubaoTts.resourceId, (s, v) => s.doubaoTts.resourceId = v);
        BindInput("DoubaoTTS_UserId", s => s.doubaoTts.userId, (s, v) => s.doubaoTts.userId = v);
        BindInput("DoubaoTTS_Speaker", s => s.doubaoTts.speaker, (s, v) => s.doubaoTts.speaker = v);
        BindInput("DoubaoTTS_Model", s => s.doubaoTts.model, (s, v) => s.doubaoTts.model = v);
        BindInput("DoubaoTTS_AudioFormat", s => s.doubaoTts.audioFormat, (s, v) => s.doubaoTts.audioFormat = v);
        BindIntInput("DoubaoTTS_SampleRate", s => s.doubaoTts.sampleRate, (s, v) => s.doubaoTts.sampleRate = v);
        BindIntInput("DoubaoTTS_SpeechRate", s => s.doubaoTts.speechRate, (s, v) => s.doubaoTts.speechRate = v);
        BindIntInput("DoubaoTTS_LoudnessRate", s => s.doubaoTts.loudnessRate, (s, v) => s.doubaoTts.loudnessRate = v);
        BindInput("DoubaoTTS_Emotion", s => s.doubaoTts.emotion, (s, v) => s.doubaoTts.emotion = v);
        BindIntInput("DoubaoTTS_EmotionScale", s => s.doubaoTts.emotionScale, (s, v) => s.doubaoTts.emotionScale = v);
        BindToggle("DoubaoTTS_EnableVoiceInstruction", s => s.doubaoTts.enableVoiceInstruction, (s, v) => s.doubaoTts.enableVoiceInstruction = v);
        BindInput("DoubaoTTS_VoiceInstruction", s => s.doubaoTts.voiceInstruction, (s, v) => s.doubaoTts.voiceInstruction = v);
        BindToggle("DoubaoTTS_EnableDeepSeekVoiceInstruction", s => s.doubaoTts.enableDeepSeekVoiceInstruction, (s, v) => s.doubaoTts.enableDeepSeekVoiceInstruction = v);
        BindInput("DoubaoTTS_DeepSeekVoiceInstructionPrompt", s => s.doubaoTts.deepSeekVoiceInstructionPrompt, (s, v) => s.doubaoTts.deepSeekVoiceInstructionPrompt = v);
        BindToggle("DoubaoTTS_UseSectionId", s => s.doubaoTts.useSectionId, (s, v) => s.doubaoTts.useSectionId = v);
        BindIntInput("DoubaoTTS_MinChunkChars", s => s.doubaoTts.minChunkChars, (s, v) => s.doubaoTts.minChunkChars = v);
        BindIntInput("DoubaoTTS_MaxChunkChars", s => s.doubaoTts.maxChunkChars, (s, v) => s.doubaoTts.maxChunkChars = v);
        BindIntInput("DoubaoTTS_TextPollIntervalMs", s => s.doubaoTts.textPollIntervalMs, (s, v) => s.doubaoTts.textPollIntervalMs = v);
        BindIntInput("DoubaoTTS_RequestTimeoutSeconds", s => s.doubaoTts.requestTimeoutSeconds, (s, v) => s.doubaoTts.requestTimeoutSeconds = v);

        BindInput("XFASR_WebsocketUrl", s => s.xunfeiAsr.websocketUrl, (s, v) => s.xunfeiAsr.websocketUrl = v);
        BindInput("XFASR_ResId", s => s.xunfeiAsr.resId, (s, v) => s.xunfeiAsr.resId = v);
        BindInput("XFASR_Domain", s => s.xunfeiAsr.domain, (s, v) => s.xunfeiAsr.domain = v);
        BindInput("XFASR_Language", s => s.xunfeiAsr.language, (s, v) => s.xunfeiAsr.language = v);
        BindInput("XFASR_Accent", s => s.xunfeiAsr.accent, (s, v) => s.xunfeiAsr.accent = v);
        BindIntInput("XFASR_VadEosMs", s => s.xunfeiAsr.vadEosMs, (s, v) => s.xunfeiAsr.vadEosMs = v);
        BindInput("XFASR_DynamicCorrection", s => s.xunfeiAsr.dynamicCorrection, (s, v) => s.xunfeiAsr.dynamicCorrection = v);
        BindToggle("XFASR_EnablePunctuation", s => s.xunfeiAsr.enablePunctuation, (s, v) => s.xunfeiAsr.enablePunctuation = v);
        BindIntInput("XFASR_LanguageTypeControl", s => s.xunfeiAsr.languageTypeControl, (s, v) => s.xunfeiAsr.languageTypeControl = v);
        BindInput("XFASR_MicrophoneDevice", s => s.xunfeiAsr.microphoneDevice, (s, v) => s.xunfeiAsr.microphoneDevice = v);
        BindIntInput("XFASR_SampleRate", s => s.xunfeiAsr.sampleRate, (s, v) => s.xunfeiAsr.sampleRate = v);
        BindIntInput("XFASR_FrameBytes", s => s.xunfeiAsr.frameBytes, (s, v) => s.xunfeiAsr.frameBytes = v);
        BindIntInput("XFASR_FrameIntervalMs", s => s.xunfeiAsr.frameIntervalMs, (s, v) => s.xunfeiAsr.frameIntervalMs = v);
        BindIntInput("XFASR_MaxRecordSeconds", s => s.xunfeiAsr.maxRecordSeconds, (s, v) => s.xunfeiAsr.maxRecordSeconds = v);

        BindInput("DeepSeek_ChatCompletionsUrl", s => s.deepSeek.chatCompletionsUrl, (s, v) => s.deepSeek.chatCompletionsUrl = v);
        BindInput("DeepSeek_Model", s => s.deepSeek.model, (s, v) => s.deepSeek.model = v);
        BindInput("DeepSeek_SystemPrompt", s => s.deepSeek.systemPrompt, (s, v) => s.deepSeek.systemPrompt = v);
        BindFloatInput("DeepSeek_Temperature", s => s.deepSeek.temperature, (s, v) => s.deepSeek.temperature = v);
        BindFloatInput("DeepSeek_TopP", s => s.deepSeek.topP, (s, v) => s.deepSeek.topP = v);
        BindIntInput("DeepSeek_MaxTokens", s => s.deepSeek.maxTokens, (s, v) => s.deepSeek.maxTokens = v);
        BindIntInput("DeepSeek_MaxHistoryMessages", s => s.deepSeek.maxHistoryMessages, (s, v) => s.deepSeek.maxHistoryMessages = v);
        BindToggle("DeepSeek_DisableThinking", s => s.deepSeek.disableThinking, (s, v) => s.deepSeek.disableThinking = v);
        BindToggle("DeepSeek_LogRequestPayloadToConsole", s => s.deepSeek.logRequestPayloadToConsole, (s, v) => s.deepSeek.logRequestPayloadToConsole = v);
        BindIntInput("DeepSeek_RequestPayloadLogChunkCharacters", s => s.deepSeek.requestPayloadLogChunkCharacters, (s, v) => s.deepSeek.requestPayloadLogChunkCharacters = v);

        BindInput("BaiduSearch_Endpoint", s => s.baiduSearch.endpoint, (s, v) => s.baiduSearch.endpoint = v);
        BindInput("BaiduSearch_AuthorizationHeaderName", s => s.baiduSearch.authorizationHeaderName, (s, v) => s.baiduSearch.authorizationHeaderName = v);
        BindInput("BaiduSearch_AlternateAuthorizationHeaderName", s => s.baiduSearch.alternateAuthorizationHeaderName, (s, v) => s.baiduSearch.alternateAuthorizationHeaderName = v);
        BindInput("BaiduSearch_Edition", s => s.baiduSearch.edition, (s, v) => s.baiduSearch.edition = v);
        BindInput("BaiduSearch_SearchSource", s => s.baiduSearch.searchSource, (s, v) => s.baiduSearch.searchSource = v);
        BindIntInput("BaiduSearch_MaxResults", s => s.baiduSearch.maxResults, (s, v) => s.baiduSearch.maxResults = v);
        BindIntInput("BaiduSearch_WebTopK", s => s.baiduSearch.webTopK, (s, v) => s.baiduSearch.webTopK = v);
        BindIntInput("BaiduSearch_VideoTopK", s => s.baiduSearch.videoTopK, (s, v) => s.baiduSearch.videoTopK = v);
        BindIntInput("BaiduSearch_ImageTopK", s => s.baiduSearch.imageTopK, (s, v) => s.baiduSearch.imageTopK = v);
        BindIntInput("BaiduSearch_AladdinTopK", s => s.baiduSearch.aladdinTopK, (s, v) => s.baiduSearch.aladdinTopK = v);
        BindIntInput("BaiduSearch_MaxQueryCharacters", s => s.baiduSearch.maxQueryCharacters, (s, v) => s.baiduSearch.maxQueryCharacters = v);
        BindIntInput("BaiduSearch_MaxContextCharacters", s => s.baiduSearch.maxContextCharacters, (s, v) => s.baiduSearch.maxContextCharacters = v);
        BindToggle("BaiduSearch_SafeSearch", s => s.baiduSearch.safeSearch, (s, v) => s.baiduSearch.safeSearch = v);
        BindToggle("BaiduSearch_AutoSortFreshQueries", s => s.baiduSearch.autoSortFreshQueries, (s, v) => s.baiduSearch.autoSortFreshQueries = v);
        BindIntInput("BaiduSearch_TimeoutSeconds", s => s.baiduSearch.timeoutSeconds, (s, v) => s.baiduSearch.timeoutSeconds = v);

        BindInput("Feature_TtsProvider", s => s.features.ttsProvider, (s, v) => s.features.ttsProvider = v);
        BindInput("Feature_TalkBoolParameter", s => s.features.talkBoolParameter, (s, v) => s.features.talkBoolParameter = v);
        BindInput("Feature_PushToTalkKey", s => s.features.pushToTalkKey, (s, v) => s.features.pushToTalkKey = v);
        BindToggle("Feature_EnablePushToTalk", s => s.features.enablePushToTalk, (s, v) => s.features.enablePushToTalk = v);
        BindToggle("Feature_ListenOnStart", s => s.features.listenOnStart, (s, v) => s.features.listenOnStart = v);

        BindToggle("Memory_EnablePersistentConversation", s => s.memory.enablePersistentConversation, (s, v) => s.memory.enablePersistentConversation = v);
        BindInput("Memory_PersistentDataFolder", s => s.memory.persistentDataFolder, (s, v) => s.memory.persistentDataFolder = v);
        BindInput("Memory_ConversationsFolderName", s => s.memory.conversationsFolderName, (s, v) => s.memory.conversationsFolderName = v);
        BindToggle("Memory_LoadLatestConversationOnStart", s => s.memory.loadLatestConversationOnStart, (s, v) => s.memory.loadLatestConversationOnStart = v);
        BindToggle("Memory_IncludeInterruptedAssistantRepliesInPrompt", s => s.memory.includeInterruptedAssistantRepliesInPrompt, (s, v) => s.memory.includeInterruptedAssistantRepliesInPrompt = v);
        BindIntInput("Memory_MaxRelevantHistoryMessages", s => s.memory.maxRelevantHistoryMessages, (s, v) => s.memory.maxRelevantHistoryMessages = v);
        BindIntInput("Memory_MaxRelevantHistoryCharacters", s => s.memory.maxRelevantHistoryCharacters, (s, v) => s.memory.maxRelevantHistoryCharacters = v);
        BindFloatInput("Memory_StreamingHistorySaveIntervalSeconds", s => s.memory.streamingHistorySaveIntervalSeconds, (s, v) => s.memory.streamingHistorySaveIntervalSeconds = v);

        BindToggle("Knowledge_EnableKnowledgeBase", s => s.knowledge.enableKnowledgeBase, (s, v) => s.knowledge.enableKnowledgeBase = v);
        BindInput("Knowledge_KnowledgeBaseFolderName", s => s.knowledge.knowledgeBaseFolderName, (s, v) => s.knowledge.knowledgeBaseFolderName = v);
        BindToggle("Knowledge_RefreshKnowledgeBaseOnStart", s => s.knowledge.refreshKnowledgeBaseOnStart, (s, v) => s.knowledge.refreshKnowledgeBaseOnStart = v);
        BindToggle("Knowledge_InjectFullKnowledgeBaseOnNewConversation", s => s.knowledge.injectFullKnowledgeBaseOnNewConversation, (s, v) => s.knowledge.injectFullKnowledgeBaseOnNewConversation = v);
        BindIntInput("Knowledge_MaxInitialKnowledgeBaseCharacters", s => s.knowledge.maxInitialKnowledgeBaseCharacters, (s, v) => s.knowledge.maxInitialKnowledgeBaseCharacters = v);

        BindToggle("AgentTools_EnableAgentTools", s => s.agentTools.enableAgentTools, (s, v) => s.agentTools.enableAgentTools = v);
        BindToggle("AgentTools_EnableDateTimeTool", s => s.agentTools.enableDateTimeTool, (s, v) => s.agentTools.enableDateTimeTool = v);
        BindToggle("AgentTools_EnableWebSearchTool", s => s.agentTools.enableWebSearchTool, (s, v) => s.agentTools.enableWebSearchTool = v);
        BindIntInput("AgentTools_MaxAgentToolRounds", s => s.agentTools.maxAgentToolRounds, (s, v) => s.agentTools.maxAgentToolRounds = v);
    }

    private void BindInput(string key, Func<RuntimeVoiceChatSettings, string> getter, Action<RuntimeVoiceChatSettings, string> setter)
    {
        var input = FindComponent<InputField>("Input_" + key);
        if (input == null)
        {
            return;
        }

        var binding = new InputBinding(input, getter, setter);
        bindings.Add(binding);
    }

    private void BindIntInput(string key, Func<RuntimeVoiceChatSettings, int> getter, Action<RuntimeVoiceChatSettings, int> setter)
    {
        BindInput(key, s => getter(s).ToString(CultureInfo.InvariantCulture), (s, v) =>
        {
            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                setter(s, parsed);
            }
        });
    }

    private void BindFloatInput(string key, Func<RuntimeVoiceChatSettings, float> getter, Action<RuntimeVoiceChatSettings, float> setter)
    {
        BindInput(key, s => VoiceChatRuntimeConfigStore.FormatFloat(getter(s)), (s, v) =>
        {
            if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                setter(s, parsed);
            }
        });
    }

    private void BindToggle(string key, Func<RuntimeVoiceChatSettings, bool> getter, Action<RuntimeVoiceChatSettings, bool> setter)
    {
        var toggle = FindComponent<Toggle>("Toggle_" + key);
        if (toggle == null)
        {
            return;
        }

        var binding = new ToggleBinding(toggle, getter, setter);
        bindings.Add(binding);
    }

    private void PushSettingsToControls()
    {
        if (workingSettings == null)
        {
            workingSettings = new RuntimeVoiceChatSettings();
        }

        workingSettings.EnsureDefaults();

        foreach (var binding in bindings)
        {
            binding.Push(workingSettings);
        }
    }

    private void ApplyControlsToSettings()
    {
        if (workingSettings == null)
        {
            workingSettings = new RuntimeVoiceChatSettings();
        }

        workingSettings.EnsureDefaults();
        foreach (var binding in bindings)
        {
            binding.Pull(workingSettings);
        }
    }

    private T FindComponent<T>(string objectName) where T : Component
    {
        var go = FindGameObject(objectName);
        return go != null ? go.GetComponent<T>() : null;
    }

    private GameObject FindGameObject(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
        {
            return null;
        }

        var transforms = GetComponentsInChildren<Transform>(true);
        foreach (var child in transforms)
        {
            if (child != null && child.name == objectName)
            {
                return child.gameObject;
            }
        }

        return null;
    }

    private void SetStatus(string text)
    {
        if (statusText != null)
        {
            statusText.text = text ?? string.Empty;
        }
    }

    private abstract class Binding
    {
        public abstract void Push(RuntimeVoiceChatSettings settings);
        public abstract void Pull(RuntimeVoiceChatSettings settings);
    }

    private sealed class InputBinding : Binding
    {
        private readonly InputField input;
        private readonly Func<RuntimeVoiceChatSettings, string> getter;
        private readonly Action<RuntimeVoiceChatSettings, string> setter;

        public InputBinding(InputField input, Func<RuntimeVoiceChatSettings, string> getter, Action<RuntimeVoiceChatSettings, string> setter)
        {
            this.input = input;
            this.getter = getter;
            this.setter = setter;
        }

        public override void Push(RuntimeVoiceChatSettings settings)
        {
            if (input != null)
            {
                input.text = getter(settings) ?? string.Empty;
            }
        }

        public override void Pull(RuntimeVoiceChatSettings settings)
        {
            if (input != null)
            {
                setter(settings, input.text);
            }
        }
    }

    private sealed class ToggleBinding : Binding
    {
        private readonly Toggle toggle;
        private readonly Func<RuntimeVoiceChatSettings, bool> getter;
        private readonly Action<RuntimeVoiceChatSettings, bool> setter;

        public ToggleBinding(Toggle toggle, Func<RuntimeVoiceChatSettings, bool> getter, Action<RuntimeVoiceChatSettings, bool> setter)
        {
            this.toggle = toggle;
            this.getter = getter;
            this.setter = setter;
        }

        public override void Push(RuntimeVoiceChatSettings settings)
        {
            if (toggle != null)
            {
                toggle.isOn = getter(settings);
            }
        }

        public override void Pull(RuntimeVoiceChatSettings settings)
        {
            if (toggle != null)
            {
                setter(settings, toggle.isOn);
            }
        }
    }
}
