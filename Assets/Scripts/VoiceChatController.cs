using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum TTSProvider
{
    XunfeiSuperSmart,
    XunfeiOnline,
    Doubao
}

public class VoiceChatController : MonoBehaviour
{
    private const string ToolGetCurrentDateTime = "get_current_datetime";
    private const string ToolWebSearch = "web_search";
    private const string InitialKnowledgeBaseInjectionMarker = "[InitialLocalKnowledgeBaseInjection]";
    private const int TalkAnimationVariantCount = 2;

    [Header("Configs")]
    public XFASRConfig asrConfig;
    public DeepSeekConfig deepSeekConfig;
    [NonSerialized] public List<ChatModelProfile> modelProfiles = new List<ChatModelProfile>();
    [NonSerialized] public string activeModelProfileId;
    public bool readRepliesAloud = true;
    public bool IsGenerating { get; private set; }
    public string ChatStatus { get; private set; } = "就绪";
    private int asrGeneration;
    private bool asrStarting;

    public ChatModelProfile ActiveModelProfile => modelProfiles.Find(p => p.id == activeModelProfileId);

    public bool SubmitTextMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        try { if (ActiveModelProfile == null) throw new InvalidOperationException("请先配置大模型接口。"); ActiveModelProfile.Validate(); }
        catch (Exception ex) { ChatStatus = ex.Message; return false; }
        CancelVoiceInput();
        InterruptAssistantOutput();
        _ = RunAssistantTurnAsync(text.Trim());
        return true;
    }

    private void CancelVoiceInput()
    {
        ++asrGeneration;
        asrStarting = false;
        asrFinalHandled = true;
        asrCts?.Cancel();
        StopMicrophone();
        isListening = false;
        asrService?.Dispose();
        asrService = null;
    }

    public void SetReadRepliesAloud(bool enabled)
    {
        readRepliesAloud = enabled;
        if (!enabled)
        {
            streamingPlayer?.StopAndClear();
            isSpeaking = false;
            SetTalkAnimation(false);
            if (ttsService != null) _ = ttsService.CancelAsync();
        }
        VoiceChatRuntimeConfigStore.SaveSettings(CaptureRuntimeSettings());
    }

    public BaiduSearchConfig baiduSearchConfig;

    [Header("TTS Provider")]
    [Tooltip("Select the active text-to-speech provider.")]
    public TTSProvider ttsProvider = TTSProvider.XunfeiSuperSmart;

    [Tooltip("Xunfei ultra-human TTS config.")]
    public XFTTSConfig ttsConfig;

    [Tooltip("Xunfei online TTS config.")]
    public XFOnlineTTSConfig onlineTtsConfig;

    [Tooltip("Doubao speech synthesis model 2.0 config.")]
    public DoubaoTTSConfig doubaoTtsConfig;

    [Header("Audio Output")]
    [Tooltip("AudioSource used by the TTS stream. Bind SALSA.audioSrc to this same AudioSource.")]
    public AudioSource ttsAudioSource;

    [Tooltip("Dynamic PCM player that feeds Xunfei raw PCM into the AudioSource.")]
    public StreamingPcmAudioPlayer streamingPlayer;

    [Header("Talk Animation")]
    [Tooltip("Animator that owns the isTalk bool. Leave empty to auto-find it in the scene.")]
    public Animator talkAnimator;

    [Tooltip("Bool parameter used by the character Animator Controller while TTS audio is playing.")]
    public string talkBoolParameter = "isTalk";

    [Tooltip("Optional int parameter used by the character Animator Controller to choose a talk animation variant.")]
    public string talkVariantParameter = "talkAnimationIndex";

    [Tooltip("Find the first active or inactive Animator with the talk bool when no Animator is assigned.")]
    public bool autoFindTalkAnimator = true;

    [Header("Input")]
    [Tooltip("Hold this key to talk. Release to finish ASR and send the text to DeepSeek.")]
    public KeyCode pushToTalkKey = KeyCode.Space;

    [Tooltip("When enabled, key down/up controls microphone recording.")]
    public bool enablePushToTalk = true;

    [Tooltip("Start listening automatically when the scene starts.")]
    public bool listenOnStart;

    [Header("Debug Output")]
    [TextArea(2, 4)]
    public string currentAsrText;

    [TextArea(2, 8)]
    public string currentAssistantText;

    [Header("Memory Storage")]
    [Tooltip("Persist chat sessions under Application.persistentDataPath/<Persistent Data Folder>/<Conversations Folder>.")]
    public bool enablePersistentConversation = true;

    [Tooltip("Root folder under Application.persistentDataPath used by chat history and knowledge base.")]
    public string persistentDataFolder = "VoiceChat";

    [Tooltip("Conversation JSON folder under the persistent data root.")]
    public string conversationsFolderName = "Conversations";

    [Tooltip("Load the most recently updated conversation when the scene starts.")]
    public bool loadLatestConversationOnStart = true;

    [Tooltip("Allow interrupted assistant text to be sent back to DeepSeek as context. Disabled by default to avoid partial replies becoming authoritative.")]
    public bool includeInterruptedAssistantRepliesInPrompt;

    [Tooltip("Older messages outside the normal recent window can be recalled by keyword relevance.")]
    [Range(0, 20)]
    public int maxRelevantHistoryMessages = 6;

    [Tooltip("Maximum characters used by older recalled history snippets.")]
    [Range(0, 8000)]
    public int maxRelevantHistoryCharacters = 1800;

    [Tooltip("Throttle interval for writing streaming assistant deltas to disk.")]
    [Range(0.1f, 5f)]
    public float streamingHistorySaveIntervalSeconds = 0.75f;

    [TextArea(4, 12)]
    public string currentConversationText;

    public string activeConversationId;
    public string conversationDirectoryPath;

    [Header("Knowledge Base")]
    [Tooltip("Load user knowledge files from Application.persistentDataPath/<Persistent Data Folder>/<Knowledge Base Folder>.")]
    public bool enableKnowledgeBase = true;

    [Tooltip("Knowledge base folder under the persistent data root. Put .txt and .json files here at runtime.")]
    public string knowledgeBaseFolderName = "KnowledgeBase";

    [Tooltip("Refresh the knowledge base index when the scene starts.")]
    public bool refreshKnowledgeBaseOnStart = true;

    [Tooltip("Store the complete local knowledge base as a hidden system message before the first user message in a new saved conversation.")]
    public bool injectFullKnowledgeBaseOnNewConversation = true;

    [Tooltip("Maximum characters for the first-turn full knowledge-base injection. Use 0 for no limit.")]
    [Range(0, 100000)]
    public int maxInitialKnowledgeBaseCharacters;

    [TextArea(2, 5)]
    public string currentKnowledgeBaseStatus;

    [Header("Agent Tools")]
    [Tooltip("Let DeepSeek decide whether to call local tools before the final streamed answer.")]
    public bool enableAgentTools = true;

    [Tooltip("Expose a deterministic current date/time tool. Use this for questions like today, now, this week, or this year.")]
    public bool enableDateTimeTool = true;

    [Tooltip("Expose web search as a model-selected tool for fresh, external, or official information.")]
    public bool enableWebSearchTool = true;

    [Tooltip("Maximum tool-call decision rounds before the final answer is streamed.")]
    [Range(0, 4)]
    public int maxAgentToolRounds = 2;

    [TextArea(2, 5)]
    public string currentAgentToolStatus;

    [TextArea(2, 5)]
    public string currentWebSearchStatus;

    [TextArea(4, 12)]
    public string currentWebSearchContext;

    public bool isListening;
    public bool isSpeaking;

    public float CurrentMicrophoneLevel { get; private set; }

    [Header("Unity Events")]
    public UnityEvent<string> onAsrPartial;
    public UnityEvent<string> onAsrFinal;
    public UnityEvent<string> onAssistantDelta;
    public UnityEvent<string> onError;
    public UnityEvent<string> onConversationChanged;
    public UnityEvent<string> onKnowledgeBaseChanged;

    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
    private readonly List<ChatCompletionService.ChatMessage> history = new List<ChatCompletionService.ChatMessage>();
    private readonly List<byte> micPcmBuffer = new List<byte>(8192);
    private readonly object conversationLock = new object();
    private CancellationTokenSource asrCts;
    private CancellationTokenSource turnCts;
    private XFASRService asrService;
    private ITTSService ttsService;
    private StreamingPcmAudioPlayer subscribedStreamingPlayer;
    private ChatHistoryStore chatHistoryStore;
    private KnowledgeBaseStore knowledgeBaseStore;
    private ChatSessionRecord activeSession;
    private int conversationStoreGeneration;
    private System.DateTime lastStreamingHistorySaveUtc = System.DateTime.MinValue;
    private Coroutine microphoneCoroutine;
    private AudioClip microphoneClip;
    private int lastMicPosition;
    private bool asrFinalHandled;
    private int ttsPlaybackGeneration;
    private int talkBoolHash;
    private int talkVariantHash;
    private bool hasTalkBoolParameter;
    private bool hasTalkVariantParameter;
    private bool talkAnimatorWarningShown;

    private void Awake()
    {
        VoiceChatRuntimeConfigStore.ApplySavedSettings(this);

        if (ttsAudioSource == null)
        {
            ttsAudioSource = GetComponent<AudioSource>();
        }

        if (streamingPlayer == null)
        {
            streamingPlayer = GetComponent<StreamingPcmAudioPlayer>();
        }

        if (streamingPlayer == null)
        {
            streamingPlayer = gameObject.AddComponent<StreamingPcmAudioPlayer>();
        }

        if (streamingPlayer.audioSource == null)
        {
            streamingPlayer.audioSource = ttsAudioSource != null ? ttsAudioSource : GetComponent<AudioSource>();
        }

        ResolveTalkAnimator();
        SetTalkAnimation(false);
        InitializeMemoryStores();
    }

    private void OnEnable()
    {
        SubscribeStreamingPlayer();
    }

    private void OnDisable()
    {
        UnsubscribeStreamingPlayer();
        SetTalkAnimation(false);
    }

    private void Start()
    {
        if (listenOnStart)
        {
            StartListening();
        }
    }

    private void Update()
    {
        while (mainThreadActions.TryDequeue(out var action))
        {
            action?.Invoke();
        }

        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected != null && selected.GetComponentInParent<InputField>() != null)
        {
            if (Input.GetKeyUp(pushToTalkKey) && isListening) StopListening();
            return;
        }
        if (!enablePushToTalk)
        {
            return;
        }

        if (Input.GetKeyDown(pushToTalkKey))
        {
            StartListening();
        }

        if (Input.GetKeyUp(pushToTalkKey))
        {
            if (asrStarting) CancelVoiceInput();
            else StopListening();
        }
    }

    private async void OnDestroy()
    {
        ++asrGeneration;
        InterruptAssistantOutput();
        UnsubscribeStreamingPlayer();
        SetTalkAnimation(false);
        StopMicrophone();
        asrCts?.Cancel();
        turnCts?.Cancel();

        if (asrService != null)
        {
            await asrService.CancelAsync();
            asrService.Dispose();
        }

        if (ttsService != null)
        {
            await ttsService.CancelAsync();
            ttsService.Dispose();
        }
    }

    public async void StartListening()
    {
        if (isListening || asrStarting) return;
        var generation = ++asrGeneration;
        asrStarting = true;
        InterruptAssistantOutput();
        currentAsrText = string.Empty;
        asrFinalHandled = false;
        micPcmBuffer.Clear();

        try
        {
            asrCts = new CancellationTokenSource();
            asrService = new XFASRService(asrConfig);
            asrService.PartialResult += text => EnqueueMainThread(() => { if (generation == asrGeneration) HandleAsrPartial(text); });
            asrService.FinalResult += text => EnqueueMainThread(() => { if (generation == asrGeneration) HandleAsrFinal(text); });
            asrService.Error += message => EnqueueMainThread(() => { if (generation == asrGeneration) ReportError(message); });
            asrService.SessionEnded += () => EnqueueMainThread(() => { if (generation == asrGeneration) isListening = false; });

            await asrService.StartAsync(asrCts.Token);
            if (generation != asrGeneration) return;
            asrStarting = false;
            StartMicrophone();
            isListening = true;
        }
        catch (Exception ex)
        {
            if (generation != asrGeneration) return;
            asrStarting = false;
            isListening = false;
            ReportError($"Start listening failed: {ex.Message}");
        }
    }

    public async void StopListening()
    {
        var generation = asrGeneration;
        if (!isListening)
        {
            return;
        }

        StopMicrophone();
        isListening = false;

        try
        {
            asrService?.CompleteInput();

            if (asrService != null)
            {
                await asrService.WaitForSessionEndAsync();
            }
        }
        catch (Exception ex)
        {
            if (generation == asrGeneration) ReportError($"Stop listening failed: {ex.Message}");
        }
    }

    public void InterruptAssistantOutput()
    {
        turnCts?.Cancel();
        ttsPlaybackGeneration++;
        IsGenerating = false;
        ChatStatus = "已停止";
        isSpeaking = false;
        SetTalkAnimation(false);

        if (streamingPlayer != null)
        {
            streamingPlayer.StopAndClear();
        }

        if (ttsService != null)
        {
            _ = ttsService.CancelAsync();
            ttsService = null;
        }
    }

    public void ClearConversationHistory()
    {
        EnsureMemoryStores();
        InterruptAssistantOutput();

        lock (conversationLock)
        {
            conversationStoreGeneration++;
            history.Clear();

            if (enablePersistentConversation && activeSession != null)
            {
                activeSession.messages.Clear();
                chatHistoryStore?.SaveSession(activeSession);
            }
        }

        UpdateConversationDisplay();
    }

    public void StartNewConversation()
    {
        EnsureMemoryStores();
        InterruptAssistantOutput();

        lock (conversationLock)
        {
            conversationStoreGeneration++;
            history.Clear();

            if (enablePersistentConversation && chatHistoryStore != null)
            {
                activeSession = chatHistoryStore.CreateSession();
                activeConversationId = activeSession.id;
            }
        }

        UpdateConversationDisplay();
    }

    public void LoadLatestConversation()
    {
        EnsureMemoryStores();
        InterruptAssistantOutput();
        if (!enablePersistentConversation || chatHistoryStore == null)
        {
            return;
        }

        lock (conversationLock)
        {
            conversationStoreGeneration++;
            activeSession = chatHistoryStore.LoadLatestOrCreate();
            activeConversationId = activeSession.id;
            RebuildRuntimeHistoryFromSession();
        }

        UpdateConversationDisplay();
    }

    public List<ChatSessionSummary> ListConversationSummaries()
    {
        EnsureMemoryStores();
        if (!enablePersistentConversation || chatHistoryStore == null)
        {
            return new List<ChatSessionSummary>();
        }

        return chatHistoryStore.ListSessions();
    }

    public bool LoadConversation(string sessionId)
    {
        EnsureMemoryStores();
        if (!enablePersistentConversation || chatHistoryStore == null || string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        InterruptAssistantOutput();

        lock (conversationLock)
        {
            conversationStoreGeneration++;
            activeSession = chatHistoryStore.LoadSession(sessionId);
            activeConversationId = activeSession.id;
            RebuildRuntimeHistoryFromSession();
        }

        UpdateConversationDisplay();
        return true;
    }

    public void DeleteAllConversationHistory()
    {
        EnsureMemoryStores();
        InterruptAssistantOutput();

        try
        {
            lock (conversationLock)
            {
                conversationStoreGeneration++;
                history.Clear();
                activeSession = null;
                activeConversationId = string.Empty;

                if (enablePersistentConversation && chatHistoryStore != null)
                {
                    chatHistoryStore.DeleteAllSessions();
                }
            }

            UpdateConversationDisplay();
        }
        catch (System.Exception ex)
        {
            ReportError($"Delete conversations failed: {ex.Message}");
        }
    }

    public void RefreshKnowledgeBase()
    {
        EnsureMemoryStores();
        if (!enableKnowledgeBase || knowledgeBaseStore == null)
        {
            currentKnowledgeBaseStatus = "知识库未启用。";
            onKnowledgeBaseChanged?.Invoke(currentKnowledgeBaseStatus);
            return;
        }

        try
        {
            knowledgeBaseStore.Refresh();
            currentKnowledgeBaseStatus = knowledgeBaseStore.LastRefreshSummary;
        }
        catch (System.Exception ex)
        {
            currentKnowledgeBaseStatus = $"知识库刷新失败：{ex.Message}";
            ReportError(currentKnowledgeBaseStatus);
        }

        onKnowledgeBaseChanged?.Invoke(currentKnowledgeBaseStatus);
    }

    public string GetConversationDirectoryPath()
    {
        EnsureMemoryStores();
        return conversationDirectoryPath;
    }

    public string GetKnowledgeBaseDirectoryPath()
    {
        EnsureMemoryStores();
        return knowledgeBaseStore != null ? knowledgeBaseStore.RootDirectory : BuildPersistentPath(knowledgeBaseFolderName);
    }

    public string GetKnowledgeBaseFileListText()
    {
        EnsureMemoryStores();
        if (knowledgeBaseStore == null)
        {
            return "知识库未初始化。";
        }

        return knowledgeBaseStore.FormatDocumentList();
    }

    public RuntimeVoiceChatSettings CaptureRuntimeSettings()
    {
        VoiceChatRuntimeConfigStore.EnsureRuntimeConfigInstances(this);
        return VoiceChatRuntimeConfigStore.CaptureFrom(this);
    }

    public void ApplyRuntimeSettings(RuntimeVoiceChatSettings settings)
    {
        if (settings == null)
        {
            return;
        }

        var previousPersistentDataFolder = persistentDataFolder;
        var previousConversationsFolderName = conversationsFolderName;
        var previousKnowledgeBaseFolderName = knowledgeBaseFolderName;
        var previousEnablePersistentConversation = enablePersistentConversation;
        var previousEnableKnowledgeBase = enableKnowledgeBase;

        VoiceChatRuntimeConfigStore.ApplyTo(this, settings);

        var shouldReinitializeStores =
            previousEnablePersistentConversation != enablePersistentConversation ||
            previousEnableKnowledgeBase != enableKnowledgeBase ||
            !string.Equals(previousPersistentDataFolder, persistentDataFolder, StringComparison.Ordinal) ||
            !string.Equals(previousConversationsFolderName, conversationsFolderName, StringComparison.Ordinal) ||
            !string.Equals(previousKnowledgeBaseFolderName, knowledgeBaseFolderName, StringComparison.Ordinal);

        if (shouldReinitializeStores)
        {
            ReinitializeMemoryStores();
        }
    }

    private void StartMicrophone()
    {
        var device = string.IsNullOrWhiteSpace(asrConfig.microphoneDevice) ? null : asrConfig.microphoneDevice;
        microphoneClip = Microphone.Start(device, true, asrConfig.maxRecordSeconds, asrConfig.sampleRate);
        lastMicPosition = 0;
        CurrentMicrophoneLevel = 0f;

        if (microphoneCoroutine != null)
        {
            StopCoroutine(microphoneCoroutine);
        }

        microphoneCoroutine = StartCoroutine(MicrophonePumpCoroutine(device));
    }

    private void StopMicrophone()
    {
        if (microphoneCoroutine != null)
        {
            StopCoroutine(microphoneCoroutine);
            microphoneCoroutine = null;
        }

        var device = asrConfig == null || string.IsNullOrWhiteSpace(asrConfig.microphoneDevice)
            ? null
            : asrConfig.microphoneDevice;

        if (Microphone.IsRecording(device))
        {
            ReadMicrophoneSamples(device);
            Microphone.End(device);
        }

        FlushMicFrames(forceAll: true);
        CurrentMicrophoneLevel = 0f;
    }

    private IEnumerator MicrophonePumpCoroutine(string device)
    {
        while (Microphone.GetPosition(device) <= 0)
        {
            yield return null;
        }

        var wait = new WaitForSeconds(0.016f);
        var frameIntervalSeconds = Mathf.Max(0.01f, asrConfig.frameIntervalMs / 1000f);
        var nextFrameFlushTime = Time.unscaledTime;

        while (isListening && asrService != null)
        {
            ReadMicrophoneSamples(device);

            if (Time.unscaledTime >= nextFrameFlushTime)
            {
                FlushMicFrames(forceAll: false);
                nextFrameFlushTime = Time.unscaledTime + frameIntervalSeconds;
            }

            yield return wait;
        }
    }

    private void ReadMicrophoneSamples(string device)
    {
        if (microphoneClip == null)
        {
            return;
        }

        var currentPosition = Microphone.GetPosition(device);
        if (currentPosition < 0 || currentPosition == lastMicPosition)
        {
            return;
        }

        if (currentPosition > lastMicPosition)
        {
            ReadMicRange(lastMicPosition, currentPosition - lastMicPosition);
        }
        else
        {
            ReadMicRange(lastMicPosition, microphoneClip.samples - lastMicPosition);
            ReadMicRange(0, currentPosition);
        }

        lastMicPosition = currentPosition;
    }

    private void ReadMicRange(int startSample, int sampleCount)
    {
        if (sampleCount <= 0)
        {
            return;
        }

        var samples = new float[sampleCount * microphoneClip.channels];
        microphoneClip.GetData(samples, startSample);
        var sumSquares = 0f;
        var peak = 0f;
        var monoSampleCount = 0;

        for (var i = 0; i < samples.Length; i += microphoneClip.channels)
        {
            var mono = samples[i];
            sumSquares += mono * mono;
            peak = Mathf.Max(peak, Mathf.Abs(mono));
            monoSampleCount++;

            var pcm = FloatToPcm16(mono);
            micPcmBuffer.Add((byte)(pcm & 0xff));
            micPcmBuffer.Add((byte)((pcm >> 8) & 0xff));
        }

        if (monoSampleCount > 0)
        {
            var rms = Mathf.Sqrt(sumSquares / monoSampleCount);
            var instantLevel = Mathf.Clamp01(Mathf.Max(rms * 1.35f, peak * 0.55f));
            var blend = instantLevel > CurrentMicrophoneLevel ? 0.86f : 0.42f;
            CurrentMicrophoneLevel = Mathf.Lerp(CurrentMicrophoneLevel, instantLevel, blend);
        }
    }

    private void FlushMicFrames(bool forceAll)
    {
        if (asrService == null)
        {
            return;
        }

        var frameBytes = Mathf.Max(320, asrConfig.frameBytes);

        while (micPcmBuffer.Count >= frameBytes || (forceAll && micPcmBuffer.Count > 0))
        {
            var count = forceAll && micPcmBuffer.Count < frameBytes ? micPcmBuffer.Count : frameBytes;
            var frame = micPcmBuffer.GetRange(0, count).ToArray();
            micPcmBuffer.RemoveRange(0, count);
            asrService.EnqueueAudio(frame);
        }
    }

    private static short FloatToPcm16(float sample)
    {
        sample = Mathf.Clamp(sample, -1f, 1f);
        return (short)Mathf.RoundToInt(sample * short.MaxValue);
    }

    private void HandleAsrPartial(string text)
    {
        currentAsrText = text;
        onAsrPartial?.Invoke(text);
    }

    private void HandleAsrFinal(string text)
    {
        if (asrFinalHandled || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        asrFinalHandled = true;
        currentAsrText = text;
        onAsrFinal?.Invoke(text);
        SubmitTextMessage(text);
    }

    private async Task RunAssistantTurnAsync(string userText)
    {
        var cancellation = new CancellationTokenSource();
        turnCts = cancellation;
        var token = cancellation.Token;
        var generation = ++ttsPlaybackGeneration;
        var model = ActiveModelProfile.Copy();
        var service = new ChatCompletionService(model, deepSeekConfig);
        ITTSService audio = null;
        ChatMessageRecord record = null;
        ChatSessionRecord session = null;
        var conversationGeneration = 0;
        var answer = new StringBuilder();
        bool audioFailed = false;
        bool speak = readRepliesAloud;
        IsGenerating = true;
        ChatStatus = "正在回答…";
        currentAssistantText = string.Empty;
        try
        {
            EnsureMemoryStores();
            EnsureInitialKnowledgeBaseInjected();
            AppendUserMessage(userText);
            var conversation = BuildConversation(userText);
            var context = BuildRuntimeSystemContext(userText, conversation.Count);
            record = BeginAssistantMessage(out session, out conversationGeneration);
            conversation = await ResolveAgentToolCallsAsync(service, conversation, context, userText, token);
            token.ThrowIfCancellationRequested();
            if (speak)
            {
                try
                {
                    var instruction = await ResolveDoubaoVoiceInstructionAsync(service, userText, token);
                    token.ThrowIfCancellationRequested();
                    audio = CreateTtsService(instruction);
                    ttsService = audio;
                    audio.AudioChunkReceived += bytes => EnqueueMainThread(() =>
                    {
                        if (generation == ttsPlaybackGeneration && readRepliesAloud && !audioFailed)
                            streamingPlayer?.AppendPcm16LittleEndian(bytes);
                    });
                    audio.Error += message => EnqueueMainThread(() =>
                    {
                        if (generation != ttsPlaybackGeneration) return;
                        audioFailed = true;
                        ChatStatus = "朗读不可用，继续文字回答";
                        streamingPlayer?.StopAndClear();
                    });
                    audio.Completed += () => EnqueueMainThread(() =>
                    {
                        if (generation == ttsPlaybackGeneration) streamingPlayer?.EndStream();
                    });
                    streamingPlayer?.BeginStream(audio.OutputSampleRate, 1);
                    var start = audio.StartAsync(token);
                    if (await Task.WhenAny(start, Task.Delay(8000, token)) != start)
                        throw new TimeoutException();
                    await start;
                }
                catch (Exception) when (!token.IsCancellationRequested)
                {
                    audioFailed = true;
                    ChatStatus = "朗读不可用，继续文字回答";
                    streamingPlayer?.StopAndClear();
                    if (audio != null) { _ = audio.CancelAsync(); audio.Dispose(); audio = null; }
                    if (generation == ttsPlaybackGeneration) ttsService = null;
                }
            }
            token.ThrowIfCancellationRequested();
            await service.StreamChatAsync(conversation, delta =>
            {
                token.ThrowIfCancellationRequested();
                if (generation != ttsPlaybackGeneration) return;
                answer.Append(delta);
                AppendAssistantDelta(session, conversationGeneration, record, delta);
                if (audio != null && !audioFailed && readRepliesAloud)
                {
                    try { audio.EnqueueText(delta); }
                    catch { audioFailed = true; }
                }
                EnqueueMainThread(() =>
                {
                    if (generation != ttsPlaybackGeneration) return;
                    currentAssistantText += delta;
                    onAssistantDelta?.Invoke(delta);
                });
            }, token, context);
            token.ThrowIfCancellationRequested();
            CompleteAssistantMessage(session, conversationGeneration, record, answer.ToString(), "completed");
            if (!enablePersistentConversation && answer.Length > 0) AddHistory(userText, answer.ToString());
            if (audio != null && !audioFailed && readRepliesAloud)
            {
                try
                {
                    audio.CompleteInput();
                    var finish = audio.WaitForCompletionAsync();
                    if (await Task.WhenAny(finish, Task.Delay(60000, token)) == finish) await finish;
                    else { audioFailed = true; _ = audio.CancelAsync(); }
                }
                catch (Exception) when (!token.IsCancellationRequested) { audioFailed = true; }
            }
            token.ThrowIfCancellationRequested();
            ChatStatus = audioFailed ? "回复完成；朗读不可用" : "就绪";
        }
        catch (Exception) when (token.IsCancellationRequested || generation != ttsPlaybackGeneration)
        {
            if (record != null && record.status != "completed")
                CompleteAssistantMessage(session, conversationGeneration, record, answer.ToString(), "interrupted");
        }
        catch (Exception ex)
        {
            if (record != null) CompleteAssistantMessage(session, conversationGeneration, record, answer.ToString(), "error");
            ChatStatus = ex is InvalidOperationException ? ex.Message : "模型连接失败或超时，请检查接口配置和网络。";
            streamingPlayer?.StopAndClear();
        }
        finally
        {
            audio?.Dispose();
            if (generation == ttsPlaybackGeneration)
            {
                if (ReferenceEquals(ttsService, audio)) ttsService = null;
                IsGenerating = false;
                turnCts = null;
            }
            cancellation.Dispose();
        }
    }

    private async Task<string> ResolveDoubaoVoiceInstructionAsync(
        ChatCompletionService deepSeek,
        string userText,
        CancellationToken cancellationToken)
    {
        if (ttsProvider != TTSProvider.Doubao)
        {
            return string.Empty;
        }

        if (doubaoTtsConfig == null)
        {
            LogDoubaoVoiceInstruction("disabled-no-config", string.Empty);
            return string.Empty;
        }

        if (!doubaoTtsConfig.enableVoiceInstruction && !doubaoTtsConfig.enableDeepSeekVoiceInstruction)
        {
            LogDoubaoVoiceInstruction("disabled", string.Empty);
            return string.Empty;
        }

        var fallback = doubaoTtsConfig.voiceInstruction;
        if (!doubaoTtsConfig.enableDeepSeekVoiceInstruction || deepSeek == null)
        {
            LogDoubaoVoiceInstruction("fallback", fallback);
            return fallback;
        }

        try
        {
            var prompt = BuildDoubaoVoiceInstructionUserPrompt(userText, fallback);
            var messages = new List<ChatCompletionService.ChatMessage>
            {
                new ChatCompletionService.ChatMessage("user", prompt)
            };
            var result = await deepSeek.CompleteChatAsync(
                messages,
                cancellationToken,
                doubaoTtsConfig.deepSeekVoiceInstructionPrompt?.Replace("用户话语和面部情绪上下文", "用户话语"));

            var instruction = SanitizeDoubaoVoiceInstruction(result?.content);
            if (string.IsNullOrWhiteSpace(instruction))
            {
                LogDoubaoVoiceInstruction("deepseek-empty-fallback", fallback);
                return fallback;
            }

            LogDoubaoVoiceInstruction("deepseek", instruction);
            return instruction;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Doubao voice instruction analysis failed, using fallback instruction. Reason: {ex.Message}");
            LogDoubaoVoiceInstruction("deepseek-error-fallback", fallback);
            return fallback;
        }
    }

    private static void LogDoubaoVoiceInstruction(string source, string instruction)
    {
        Debug.Log($"Doubao voice instruction ({source}): {instruction}");
    }

    private static string BuildDoubaoVoiceInstructionUserPrompt(
        string userText,
        string fallback)
    {
        var builder = new StringBuilder();
        builder.AppendLine("请为本轮数字人回复选择豆包语音合成模型2.0的语音指令。");
        builder.AppendLine("目标：只控制朗读情绪、语气、语速和音量倾向，不改变回答内容。");
        builder.AppendLine("用户原话：");
        builder.AppendLine(userText ?? string.Empty);
        builder.AppendLine();
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            builder.AppendLine();
            builder.AppendLine("可用兜底指令：");
            builder.AppendLine(fallback);
        }

        return builder.ToString().TrimEnd();
    }

    private static string SanitizeDoubaoVoiceInstruction(string instruction)
    {
        if (string.IsNullOrWhiteSpace(instruction))
        {
            return string.Empty;
        }

        var clean = instruction.Trim();
        clean = clean.Trim('`', '"', '\'', '“', '”', '‘', '’');
        var lineBreak = clean.IndexOfAny(new[] { '\r', '\n' });
        if (lineBreak >= 0)
        {
            clean = clean.Substring(0, lineBreak).Trim();
        }

        if (clean.StartsWith("context_texts", StringComparison.OrdinalIgnoreCase))
        {
            var index = clean.IndexOf(':');
            if (index < 0)
            {
                index = clean.IndexOf('=');
            }

            if (index >= 0 && index + 1 < clean.Length)
            {
                clean = clean.Substring(index + 1).Trim();
                clean = clean.Trim('[', ']', '"', '\'', '“', '”');
            }
        }

        const int maxCharacters = 90;
        if (clean.Length > maxCharacters)
        {
            clean = clean.Substring(0, maxCharacters).TrimEnd();
        }

        return clean;
    }

    private ITTSService CreateTtsService(string doubaoVoiceInstruction)
    {
        switch (ttsProvider)
        {
            case TTSProvider.XunfeiOnline:
                return new XFOnlineTTSService(onlineTtsConfig);
            case TTSProvider.Doubao:
                return new DoubaoTTSService(doubaoTtsConfig, doubaoVoiceInstruction, GetDoubaoSectionId());
            case TTSProvider.XunfeiSuperSmart:
            default:
                return new XFTTSService(ttsConfig);
        }
    }

    private string GetDoubaoSectionId()
    {
        if (doubaoTtsConfig == null || !doubaoTtsConfig.useSectionId)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(activeConversationId))
        {
            return activeConversationId;
        }

        return Guid.NewGuid().ToString("N");
    }

    private void SubscribeStreamingPlayer()
    {
        if (streamingPlayer == null || subscribedStreamingPlayer == streamingPlayer)
        {
            return;
        }

        UnsubscribeStreamingPlayer();
        subscribedStreamingPlayer = streamingPlayer;
        subscribedStreamingPlayer.PlaybackStarted += HandleTtsPlaybackStarted;
        subscribedStreamingPlayer.PlaybackStopped += HandleTtsPlaybackStopped;
    }

    private void UnsubscribeStreamingPlayer()
    {
        if (subscribedStreamingPlayer == null)
        {
            return;
        }

        subscribedStreamingPlayer.PlaybackStarted -= HandleTtsPlaybackStarted;
        subscribedStreamingPlayer.PlaybackStopped -= HandleTtsPlaybackStopped;
        subscribedStreamingPlayer = null;
    }

    private void HandleTtsPlaybackStarted()
    {
        isSpeaking = true;
        SetTalkAnimation(true);
    }

    private void HandleTtsPlaybackStopped()
    {
        isSpeaking = false;
        SetTalkAnimation(false);
    }

    public void SetTalkAnimator(Animator animator)
    {
        if (talkAnimator == animator)
        {
            hasTalkBoolParameter = false;
            hasTalkVariantParameter = false;
            if (talkAnimator == null)
            {
                talkAnimatorWarningShown = false;
                return;
            }

            ResolveTalkAnimator();
            SetTalkAnimation(isSpeaking);
            return;
        }

        if (talkAnimator != null && hasTalkBoolParameter)
        {
            TrySetAnimatorBool(talkAnimator, talkBoolHash, false);
        }

        talkAnimator = animator;
        hasTalkBoolParameter = false;
        hasTalkVariantParameter = false;
        talkAnimatorWarningShown = false;
        if (talkAnimator == null)
        {
            return;
        }

        ResolveTalkAnimator();
        SetTalkAnimation(isSpeaking);
    }

    private bool ResolveTalkAnimator()
    {
        if (string.IsNullOrWhiteSpace(talkBoolParameter))
        {
            hasTalkBoolParameter = false;
            hasTalkVariantParameter = false;
            return false;
        }

        talkBoolHash = Animator.StringToHash(talkBoolParameter);
        talkVariantHash = string.IsNullOrWhiteSpace(talkVariantParameter)
            ? 0
            : Animator.StringToHash(talkVariantParameter);

        if (talkAnimator != null && AnimatorHasBoolParameter(talkAnimator, talkBoolHash))
        {
            hasTalkBoolParameter = true;
            hasTalkVariantParameter = AnimatorHasIntParameter(talkAnimator, talkVariantHash);
            return true;
        }

        if (!autoFindTalkAnimator)
        {
            hasTalkBoolParameter = false;
            hasTalkVariantParameter = false;
            return false;
        }

        var animators = FindObjectsOfType<Animator>(true);
        foreach (var animator in animators)
        {
            if (AnimatorHasBoolParameter(animator, talkBoolHash))
            {
                talkAnimator = animator;
                hasTalkBoolParameter = true;
                hasTalkVariantParameter = AnimatorHasIntParameter(talkAnimator, talkVariantHash);
                return true;
            }
        }

        hasTalkBoolParameter = false;
        hasTalkVariantParameter = false;
        return false;
    }

    private void SetTalkAnimation(bool value)
    {
        if (talkAnimator == null)
        {
            hasTalkBoolParameter = false;
            hasTalkVariantParameter = false;
        }

        if (!hasTalkBoolParameter && !ResolveTalkAnimator())
        {
            if (value && !talkAnimatorWarningShown)
            {
                talkAnimatorWarningShown = true;
                Debug.LogWarning($"No Animator with bool parameter '{talkBoolParameter}' was found for TTS talk animation.");
            }

            return;
        }

        if (value
            && hasTalkVariantParameter
            && !TrySetAnimatorInteger(talkAnimator, talkVariantHash, UnityEngine.Random.Range(0, TalkAnimationVariantCount)))
        {
            hasTalkVariantParameter = false;
        }

        if (!TrySetAnimatorBool(talkAnimator, talkBoolHash, value))
        {
            hasTalkBoolParameter = false;
            hasTalkVariantParameter = false;
        }
    }

    private static bool AnimatorHasBoolParameter(Animator animator, int parameterHash)
    {
        return AnimatorHasParameter(animator, parameterHash, AnimatorControllerParameterType.Bool);
    }

    private static bool AnimatorHasIntParameter(Animator animator, int parameterHash)
    {
        return parameterHash != 0
            && AnimatorHasParameter(animator, parameterHash, AnimatorControllerParameterType.Int);
    }

    private static bool AnimatorHasParameter(Animator animator, int parameterHash, AnimatorControllerParameterType parameterType)
    {
        if (!AnimatorIsUsable(animator))
        {
            return false;
        }

        try
        {
            foreach (var parameter in animator.parameters)
            {
                if (parameter.nameHash == parameterHash && parameter.type == parameterType)
                {
                    return true;
                }
            }
        }
        catch (Exception)
        {
            return false;
        }

        return false;
    }

    private static bool AnimatorIsUsable(Animator animator)
    {
        return animator != null
            && animator.isActiveAndEnabled
            && animator.gameObject.activeInHierarchy
            && animator.runtimeAnimatorController != null;
    }

    private static bool TrySetAnimatorBool(Animator animator, int parameterHash, bool value)
    {
        if (!AnimatorHasBoolParameter(animator, parameterHash))
        {
            return false;
        }

        animator.SetBool(parameterHash, value);
        return true;
    }

    private static bool TrySetAnimatorInteger(Animator animator, int parameterHash, int value)
    {
        if (!AnimatorHasIntParameter(animator, parameterHash))
        {
            return false;
        }

        animator.SetInteger(parameterHash, value);
        return true;
    }

    private void EnsureInitialKnowledgeBaseInjected()
    {
        if (!injectFullKnowledgeBaseOnNewConversation || !enablePersistentConversation || !enableKnowledgeBase)
        {
            return;
        }

        lock (conversationLock)
        {
            EnsureActiveSession();
            if (activeSession == null
                || chatHistoryStore == null
                || SessionHasInitialKnowledgeBaseInjection(activeSession)
                || SessionHasUserOrAssistantMessages(activeSession))
            {
                return;
            }

            var systemMessage = BuildInitialKnowledgeBaseSystemMessage();
            if (string.IsNullOrWhiteSpace(systemMessage))
            {
                return;
            }

            chatHistoryStore.AppendMessage(activeSession, "system", systemMessage, "completed");
            activeConversationId = activeSession.id;
            RebuildRuntimeHistoryFromSession();
        }
    }

    private string BuildInitialKnowledgeBaseSystemMessage()
    {
        if (knowledgeBaseStore == null)
        {
            return string.Empty;
        }

        if (knowledgeBaseStore.DocumentCount == 0)
        {
            knowledgeBaseStore.Refresh();
            currentKnowledgeBaseStatus = knowledgeBaseStore.LastRefreshSummary;
            EnqueueMainThread(() => onKnowledgeBaseChanged?.Invoke(currentKnowledgeBaseStatus));
        }

        var fullContext = knowledgeBaseStore.BuildFullContext(maxInitialKnowledgeBaseCharacters);
        if (string.IsNullOrWhiteSpace(fullContext))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine(InitialKnowledgeBaseInjectionMarker);
        builder.AppendLine("以下内容是本会话第一次用户发言前注入的本地知识库完整快照。它不是用户消息，也不是需要直接朗读的内容。");
        builder.AppendLine("你仍然要和用户自然对话；只有当用户问题涉及知识库资料时才使用这些内容。涉及北方工业大学、校园楼宇、服务、地点、流程、联系方式等问题时，优先依据知识库回答。");
        builder.AppendLine("如果知识库没有覆盖用户问题，请明确说明知识库中未找到对应信息，不要编造。");
        builder.AppendLine();
        builder.AppendLine(fullContext);
        return builder.ToString().Trim();
    }

    private static bool SessionHasInitialKnowledgeBaseInjection(ChatSessionRecord session)
    {
        if (session == null || session.messages == null)
        {
            return false;
        }

        foreach (var message in session.messages)
        {
            if (message != null
                && message.role == "system"
                && !string.IsNullOrEmpty(message.content)
                && message.content.Contains(InitialKnowledgeBaseInjectionMarker))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SessionHasUserOrAssistantMessages(ChatSessionRecord session)
    {
        if (session == null || session.messages == null)
        {
            return false;
        }

        foreach (var message in session.messages)
        {
            if (message != null && (message.role == "user" || message.role == "assistant"))
            {
                return true;
            }
        }

        return false;
    }

    private void InitializeMemoryStores()
    {
        conversationDirectoryPath = BuildPersistentPath(conversationsFolderName);
        chatHistoryStore = new ChatHistoryStore(conversationDirectoryPath);
        chatHistoryStore.EnsureRootDirectory();

        knowledgeBaseStore = new KnowledgeBaseStore(BuildPersistentPath(knowledgeBaseFolderName));
        knowledgeBaseStore.EnsureRootDirectory();

        if (enablePersistentConversation && loadLatestConversationOnStart)
        {
            LoadLatestConversation();
        }
        else
        {
            UpdateConversationDisplay();
        }

        if (enableKnowledgeBase && refreshKnowledgeBaseOnStart)
        {
            RefreshKnowledgeBase();
        }
        else
        {
            currentKnowledgeBaseStatus = $"知识库目录：{knowledgeBaseStore.RootDirectory}";
            onKnowledgeBaseChanged?.Invoke(currentKnowledgeBaseStatus);
        }
    }

    private void EnsureMemoryStores()
    {
        if (chatHistoryStore != null && knowledgeBaseStore != null)
        {
            return;
        }

        InitializeMemoryStores();
    }

    private void ReinitializeMemoryStores()
    {
        chatHistoryStore = null;
        knowledgeBaseStore = null;
        activeSession = null;
        activeConversationId = string.Empty;
        conversationDirectoryPath = string.Empty;
        InitializeMemoryStores();
    }

    private string BuildPersistentPath(string childFolder)
    {
        var root = string.IsNullOrWhiteSpace(persistentDataFolder) ? "VoiceChat" : persistentDataFolder.Trim();
        var child = string.IsNullOrWhiteSpace(childFolder) ? "Data" : childFolder.Trim();
        return Path.Combine(Application.persistentDataPath, root, child);
    }

    private void RebuildRuntimeHistoryFromSession()
    {
        history.Clear();
        if (activeSession == null || activeSession.messages == null)
        {
            return;
        }

        foreach (var message in activeSession.messages)
        {
            if (message == null || !message.IsPromptEligible(includeInterruptedAssistantRepliesInPrompt))
            {
                continue;
            }

            history.Add(new ChatCompletionService.ChatMessage(message.role, GetDisplayMessageContent(message.role, message.content)));
        }
    }

    private void AppendUserMessage(string userText)
    {
        if (enablePersistentConversation)
        {
            lock (conversationLock)
            {
                EnsureActiveSession();
                chatHistoryStore.AppendMessage(activeSession, "user", userText, "completed");
                activeConversationId = activeSession.id;
                RebuildRuntimeHistoryFromSession();
            }

            UpdateConversationDisplay();
            return;
        }

        history.Add(new ChatCompletionService.ChatMessage("user", userText));
        UpdateConversationDisplay();
    }

    private ChatMessageRecord BeginAssistantMessage(out ChatSessionRecord session, out int conversationGeneration)
    {
        session = null;
        conversationGeneration = 0;

        if (!enablePersistentConversation)
        {
            return null;
        }

        lock (conversationLock)
        {
            EnsureActiveSession();
            session = activeSession;
            conversationGeneration = conversationStoreGeneration;
            lastStreamingHistorySaveUtc = System.DateTime.UtcNow;
            var record = chatHistoryStore.AppendMessage(activeSession, "assistant", string.Empty, "streaming");
            RebuildRuntimeHistoryFromSession();
            return record;
        }
    }

    private void AppendAssistantDelta(
        ChatSessionRecord assistantSession,
        int conversationGeneration,
        ChatMessageRecord assistantRecord,
        string delta)
    {
        if (!enablePersistentConversation || assistantSession == null || assistantRecord == null || string.IsNullOrEmpty(delta))
        {
            return;
        }

        var shouldSave = false;
        var shouldRefreshDisplay = false;
        lock (conversationLock)
        {
            if (conversationGeneration != conversationStoreGeneration)
            {
                return;
            }

            assistantRecord.content += delta;
            assistantRecord.updatedAtUtc = System.DateTime.UtcNow.ToString("o");
            var now = System.DateTime.UtcNow;
            if ((now - lastStreamingHistorySaveUtc).TotalSeconds >= streamingHistorySaveIntervalSeconds)
            {
                lastStreamingHistorySaveUtc = now;
                shouldSave = true;
                shouldRefreshDisplay = ReferenceEquals(assistantSession, activeSession);
            }
        }

        if (shouldSave)
        {
            SaveSessionNoThrow(assistantSession, conversationGeneration);

            if (shouldRefreshDisplay)
            {
                EnqueueMainThread(UpdateConversationDisplay);
            }
        }
    }

    private void CompleteAssistantMessage(
        ChatSessionRecord assistantSession,
        int conversationGeneration,
        ChatMessageRecord assistantRecord,
        string assistantText,
        string status)
    {
        if (enablePersistentConversation)
        {
            if (assistantSession == null || assistantRecord == null)
            {
                return;
            }

            var shouldRefreshDisplay = false;
            lock (conversationLock)
            {
                if (conversationGeneration != conversationStoreGeneration)
                {
                    return;
                }

                assistantRecord.content = assistantText ?? string.Empty;
                assistantRecord.status = status;
                assistantRecord.updatedAtUtc = System.DateTime.UtcNow.ToString("o");
                chatHistoryStore.SaveSession(assistantSession);
                shouldRefreshDisplay = ReferenceEquals(assistantSession, activeSession);
                if (shouldRefreshDisplay)
                {
                    RebuildRuntimeHistoryFromSession();
                }
            }

            if (shouldRefreshDisplay)
            {
                EnqueueMainThread(UpdateConversationDisplay);
            }
        }
    }

    private void SaveSessionNoThrow(ChatSessionRecord session, int conversationGeneration)
    {
        try
        {
            lock (conversationLock)
            {
                if (session != null && conversationGeneration == conversationStoreGeneration)
                {
                    chatHistoryStore?.SaveSession(session);
                }
            }
        }
        catch (System.Exception ex)
        {
            EnqueueMainThread(() => ReportError($"Save conversation failed: {ex.Message}"));
        }
    }

    private void EnsureActiveSession()
    {
        if (activeSession != null || chatHistoryStore == null)
        {
            return;
        }

        activeSession = loadLatestConversationOnStart
            ? chatHistoryStore.LoadLatestOrCreate()
            : chatHistoryStore.CreateSession();
        activeConversationId = activeSession.id;
    }

    private List<ChatCompletionService.ChatMessage> BuildConversation(string userText)
    {
        var source = new List<ChatCompletionService.ChatMessage>();

        if (enablePersistentConversation)
        {
            lock (conversationLock)
            {
                EnsureActiveSession();
                foreach (var message in activeSession.messages)
                {
                    if (message == null || !message.IsPromptEligible(includeInterruptedAssistantRepliesInPrompt))
                    {
                        continue;
                    }

                    source.Add(new ChatCompletionService.ChatMessage(message.role, GetDisplayMessageContent(message.role, message.content)));
                }
            }
        }
        else
        {
            source.AddRange(history);
        }

        var systemMessages = source.FindAll(message => message.role == "system");
        var regularMessages = source.FindAll(message => message.role != "system");

        var maxMessages = deepSeekConfig != null ? deepSeekConfig.maxHistoryMessages : 12;
        maxMessages = Mathf.Max(1, maxMessages);
        if (regularMessages.Count <= maxMessages)
        {
            return source;
        }

        var clipped = regularMessages.GetRange(regularMessages.Count - maxMessages, maxMessages);
        if (systemMessages.Count == 0)
        {
            return clipped;
        }

        var conversation = new List<ChatCompletionService.ChatMessage>(systemMessages.Count + clipped.Count);
        conversation.AddRange(systemMessages);
        conversation.AddRange(clipped);
        return conversation;
    }

    private string BuildRuntimeSystemContext(string userText, int promptMessageCount)
    {
        var builder = new StringBuilder();

        currentWebSearchContext = string.Empty;
        currentWebSearchStatus = string.Empty;
        currentAgentToolStatus = string.Empty;

        if (enableAgentTools)
        {
            builder.AppendLine("你可以在最终回答前调用工具。请由你判断是否需要工具，不要让用户替你判断。");
            builder.AppendLine("如果用户问今天、现在、本周、本月、今年、当前日期或当前时间，必须先调用 get_current_datetime。");
            builder.AppendLine("如果用户问较新的事实、新闻、公告、政策、价格、天气、比赛、开放时间、官网信息或任何可能变化的信息，应调用 web_search。");
            builder.AppendLine("如果用户问校园服务、北方工业大学、办事流程、校内规定或本地资料，应优先依据本会话已注入的本地知识库快照回答；资料不足时再调用 web_search。");
            builder.AppendLine("最终回答只能基于用户问题、对话上下文和工具结果。没有工具结果时，不要声称已经联网或已经查询。");
            builder.AppendLine("工具结果可能是 JSON，回答时提炼成自然中文，不要原样输出 JSON。");
            builder.AppendLine();
        }

        var historyContext = BuildRelevantHistoryContext(userText, promptMessageCount);
        if (!string.IsNullOrWhiteSpace(historyContext))
        {
            builder.AppendLine(historyContext);
        }

        var context = builder.ToString().Trim();
        return context;
    }

    private async Task<List<ChatCompletionService.ChatMessage>> ResolveAgentToolCallsAsync(
        ChatCompletionService deepSeek,
        List<ChatCompletionService.ChatMessage> conversation,
        string runtimeSystemContext,
        string userText,
        CancellationToken cancellationToken)
    {
        if (!enableAgentTools || deepSeek == null || !deepSeek.SupportsTools || conversation == null)
        {
            currentAgentToolStatus = enableAgentTools ? string.Empty : "工具决策：已关闭。";
            return conversation;
        }

        var tools = BuildAgentToolDefinitions();
        if (tools.Count == 0 || maxAgentToolRounds <= 0)
        {
            currentAgentToolStatus = "工具决策：没有启用的工具。";
            return conversation;
        }

        var toolConversation = new List<ChatCompletionService.ChatMessage>(conversation);
        var rounds = Mathf.Clamp(maxAgentToolRounds, 0, 4);
        currentAgentToolStatus = "工具决策：等待模型判断。";

        try
        {
            for (var round = 0; round < rounds; round++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await deepSeek.CompleteChatAsync(
                    toolConversation,
                    cancellationToken,
                    runtimeSystemContext,
                    tools,
                    "auto");

                if (result == null || result.toolCalls == null || result.toolCalls.Count == 0)
                {
                    currentAgentToolStatus = round == 0
                        ? "工具决策：模型判断无需调用工具。"
                        : currentAgentToolStatus;
                    break;
                }

                currentAgentToolStatus = $"工具决策：第 {round + 1} 轮调用 {FormatToolCallNames(result.toolCalls)}。";
                toolConversation.Add(ChatCompletionService.ChatMessage.AssistantToolCalls(result.toolCalls, result.content));

                foreach (var toolCall in result.toolCalls)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var toolResult = await ExecuteAgentToolCallAsync(toolCall, userText, cancellationToken);
                    toolConversation.Add(ChatCompletionService.ChatMessage.ToolResult(toolCall.id, toolResult));
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            currentAgentToolStatus = $"工具决策：失败，已回退为普通回答。原因：{ex.Message}";
            Debug.LogWarning(currentAgentToolStatus);
            return conversation;
        }

        return toolConversation;
    }

    private JArray BuildAgentToolDefinitions()
    {
        var tools = new JArray();

        if (enableDateTimeTool)
        {
            tools.Add(new JObject
            {
                ["type"] = "function",
                ["function"] = new JObject
                {
                    ["name"] = ToolGetCurrentDateTime,
                    ["description"] = "获取本机当前日期、时间、星期和时区。凡是用户问今天、现在、本周、本月、今年或当前时间，都应调用此工具。",
                    ["parameters"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["timezone"] = new JObject
                            {
                                ["type"] = "string",
                                ["description"] = "可选。用户指定的时区；未指定时使用本机本地时区。"
                            }
                        },
                        ["additionalProperties"] = false
                    }
                }
            });
        }

        if (enableWebSearchTool)
        {
            tools.Add(new JObject
            {
                ["type"] = "function",
                ["function"] = new JObject
                {
                    ["name"] = ToolWebSearch,
                    ["description"] = "使用百度千帆百度搜索检索较新、外部、官网、实时或可能变化的信息。新闻、热点、时事、最新动态、官网、学校、机构资料都可以调用此工具。不要用它查询本机当前日期时间；当前日期时间应调用 get_current_datetime。",
                    ["parameters"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["query"] = new JObject
                            {
                                ["type"] = "string",
                                ["description"] = "搜索词。应简洁，包含必要实体名；查询学校信息时优先包含“北方工业大学”。"
                            },
                            ["max_results"] = new JObject
                            {
                                ["type"] = "integer",
                                ["description"] = "可选。返回结果数，范围 1 到 10。"
                            }
                        },
                        ["required"] = new JArray("query"),
                        ["additionalProperties"] = false
                    }
                }
            });
        }

        return tools;
    }

    private async Task<string> ExecuteAgentToolCallAsync(
        ChatCompletionService.ToolCall toolCall,
        string fallbackQuery,
        CancellationToken cancellationToken)
    {
        if (toolCall == null || string.IsNullOrWhiteSpace(toolCall.functionName))
        {
            return BuildToolError("unknown", "工具调用为空。");
        }

        var arguments = ParseToolArguments(toolCall.arguments);
        switch (toolCall.functionName)
        {
            case ToolGetCurrentDateTime:
                return BuildCurrentDateTimeToolResult(arguments);
            case ToolWebSearch:
                return await BuildWebSearchToolResultAsync(arguments, fallbackQuery, cancellationToken);
            default:
                return BuildToolError(toolCall.functionName, "未知工具。");
        }
    }

    private string BuildCurrentDateTimeToolResult(JObject arguments)
    {
        var now = DateTimeOffset.Now;
        var culture = CultureInfo.GetCultureInfo("zh-CN");
        var result = new JObject
        {
            ["tool"] = ToolGetCurrentDateTime,
            ["timezone_id"] = TimeZoneInfo.Local.Id,
            ["timezone_display_name"] = TimeZoneInfo.Local.DisplayName,
            ["utc_offset"] = now.Offset.ToString(),
            ["local_datetime"] = now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            ["date"] = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["time"] = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            ["year"] = now.Year,
            ["month"] = now.Month,
            ["day"] = now.Day,
            ["weekday"] = now.DayOfWeek.ToString(),
            ["weekday_zh"] = culture.DateTimeFormat.GetDayName(now.DayOfWeek)
        };

        var requestedTimezone = GetStringArgument(arguments, "timezone", string.Empty);
        if (!string.IsNullOrWhiteSpace(requestedTimezone))
        {
            result["requested_timezone"] = requestedTimezone;
            result["note"] = "当前实现使用本机本地时区；如需指定时区换算，需要扩展时区映射。";
        }

        currentAgentToolStatus = $"工具调用：{ToolGetCurrentDateTime} 已返回 {result["date"]} {result["time"]}。";
        return result.ToString(Newtonsoft.Json.Formatting.None);
    }

    private async Task<string> BuildWebSearchToolResultAsync(
        JObject arguments,
        string fallbackQuery,
        CancellationToken cancellationToken)
    {
        var query = GetStringArgument(arguments, "query", fallbackQuery);
        if (string.IsNullOrWhiteSpace(query))
        {
            currentWebSearchStatus = "联网搜索：查询为空。";
            currentWebSearchContext = string.Empty;
            return BuildToolError(ToolWebSearch, currentWebSearchStatus);
        }

        if (baiduSearchConfig == null)
        {
            currentWebSearchStatus = "联网搜索：BaiduSearchConfig 未绑定。";
            currentWebSearchContext = string.Empty;
            return BuildToolError(ToolWebSearch, currentWebSearchStatus);
        }

        var configuredMaxResults = Mathf.Clamp(baiduSearchConfig.maxResults, 1, 20);
        var maxResults = Mathf.Clamp(
            GetIntArgument(arguments, "max_results", configuredMaxResults),
            1,
            configuredMaxResults);
        var recencyFilter = GetBaiduSearchRecencyFilter(query);
        var autoSort = IsFreshWebSearchQuery(query);

        try
        {
            var baiduSearch = new BaiduSearchService(baiduSearchConfig);
            var response = await baiduSearch.SearchAsync(query, maxResults, recencyFilter, autoSort, cancellationToken);
            currentWebSearchContext = FormatWebSearchContext(
                query,
                response.Results,
                Mathf.Clamp(baiduSearchConfig.maxContextCharacters, 500, 6000));
            currentWebSearchStatus = response.Results.Count == 0
                ? $"联网搜索：百度千帆百度搜索未找到可用结果。查询：{query}"
                : $"联网搜索：百度千帆百度搜索找到 {response.Results.Count} 条结果。查询：{query}";
            currentAgentToolStatus = $"工具调用：{ToolWebSearch}，{currentWebSearchStatus}";

            return BuildWebSearchToolJson(query, response, currentWebSearchStatus, currentWebSearchContext);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            currentWebSearchStatus = $"联网搜索：请求超时。查询：{query}";
            currentWebSearchContext = string.Empty;
            Debug.LogWarning(currentWebSearchStatus);
            return BuildToolError(ToolWebSearch, currentWebSearchStatus);
        }
        catch (Exception ex)
        {
            var baiduError = ex as BaiduSearchException;
            var responseBody = baiduError == null ? string.Empty : LimitText(baiduError.ResponseBody, 300);
            currentWebSearchStatus = $"联网搜索：百度千帆百度搜索请求失败。查询：{query}；原因：{ex.Message}";
            currentWebSearchContext = string.Empty;
            Debug.LogWarning(currentWebSearchStatus);
            return BuildToolError(ToolWebSearch, string.IsNullOrWhiteSpace(responseBody)
                ? currentWebSearchStatus
                : currentWebSearchStatus + " " + responseBody);
        }
    }

    private static string GetBaiduSearchRecencyFilter(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        if (Regex.IsMatch(query, "(半年|六个月|6个月|semiyear|half year)", RegexOptions.IgnoreCase))
        {
            return "semiyear";
        }

        if (Regex.IsMatch(query, "(今年|一年|近一年|year|annual)", RegexOptions.IgnoreCase))
        {
            return "year";
        }

        if (Regex.IsMatch(query, "(本月|近一个月|最近30天|30天|month)", RegexOptions.IgnoreCase))
        {
            return "month";
        }

        if (IsFreshWebSearchQuery(query))
        {
            return "week";
        }

        return string.Empty;
    }

    private static bool IsFreshWebSearchQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        return Regex.IsMatch(
            query,
            "(新闻|热点|热搜|时事|动态|今日|今天|最新|最近|近期|本周|突发|价格|天气|比赛|开放时间|公告|政策|news|headline|latest|recent|today|breaking)",
            RegexOptions.IgnoreCase);
    }

    private static string BuildWebSearchToolJson(
        string query,
        BaiduSearchResponse response,
        string status,
        string context)
    {
        var resultArray = new JArray();
        var results = response?.Results ?? new List<BaiduSearchResult>();
        foreach (var result in results)
        {
            resultArray.Add(new JObject
            {
                ["id"] = result.Id,
                ["title"] = result.Title,
                ["snippet"] = result.Snippet,
                ["url"] = result.Url,
                ["website"] = result.Website,
                ["date"] = result.Date,
                ["type"] = result.Type
            });
        }

        return new JObject
        {
            ["tool"] = ToolWebSearch,
            ["query"] = query,
            ["provider"] = "baidu_qianfan_web_search",
            ["request_id"] = response?.RequestId,
            ["found"] = results.Count > 0,
            ["status"] = status,
            ["results"] = resultArray,
            ["context"] = context ?? string.Empty
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string FormatWebSearchContext(string query, List<BaiduSearchResult> results, int maxCharacters)
    {
        if (results == null || results.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("以下是本轮联网搜索结果。回答应先参考这些结果；如果结果不明确或互相冲突，应说明不确定处。");
        builder.AppendLine($"搜索词：{query}");

        var usedCharacters = builder.Length;
        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            var block = new StringBuilder();
            block.AppendLine($"[联网搜索 {i + 1}] {result.Title}");
            if (!string.IsNullOrWhiteSpace(result.Snippet))
            {
                block.AppendLine(result.Snippet);
            }

            if (!string.IsNullOrWhiteSpace(result.Url))
            {
                block.AppendLine($"来源：{result.Url}");
            }

            if (!string.IsNullOrWhiteSpace(result.Website)
                || !string.IsNullOrWhiteSpace(result.Date)
                || !string.IsNullOrWhiteSpace(result.Type))
            {
                var metadata = new List<string>();
                if (!string.IsNullOrWhiteSpace(result.Website))
                {
                    metadata.Add("站点：" + result.Website);
                }

                if (!string.IsNullOrWhiteSpace(result.Date))
                {
                    metadata.Add("时间：" + result.Date);
                }

                if (!string.IsNullOrWhiteSpace(result.Type))
                {
                    metadata.Add("类型：" + result.Type);
                }

                block.AppendLine(string.Join("；", metadata.ToArray()));
            }

            var text = block.ToString().TrimEnd();
            if (usedCharacters + text.Length > maxCharacters)
            {
                var remaining = maxCharacters - usedCharacters;
                if (remaining <= 120)
                {
                    break;
                }

                text = LimitText(text, remaining);
            }

            builder.AppendLine(text);
            builder.AppendLine();
            usedCharacters = builder.Length;
            if (usedCharacters >= maxCharacters)
            {
                break;
            }
        }

        return builder.ToString().Trim();
    }

    private static JObject ParseToolArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return new JObject();
        }

        try
        {
            var token = JToken.Parse(arguments);
            return token as JObject ?? new JObject();
        }
        catch
        {
            return new JObject();
        }
    }

    private static string GetStringArgument(JObject arguments, string name, string fallback)
    {
        var value = arguments?[name]?.ToString();
        return string.IsNullOrWhiteSpace(value) ? (fallback ?? string.Empty) : value.Trim();
    }

    private static int GetIntArgument(JObject arguments, string name, int fallback)
    {
        if (arguments == null || arguments[name] == null)
        {
            return fallback;
        }

        return int.TryParse(arguments[name].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private static string BuildToolError(string toolName, string message)
    {
        return new JObject
        {
            ["tool"] = toolName,
            ["ok"] = false,
            ["error"] = message ?? string.Empty
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string FormatToolCallNames(IReadOnlyList<ChatCompletionService.ToolCall> toolCalls)
    {
        if (toolCalls == null || toolCalls.Count == 0)
        {
            return "无工具";
        }

        var names = new List<string>();
        foreach (var toolCall in toolCalls)
        {
            if (toolCall == null || string.IsNullOrWhiteSpace(toolCall.functionName))
            {
                continue;
            }

            names.Add(toolCall.functionName);
        }

        return names.Count == 0 ? "未知工具" : string.Join(", ", names.ToArray());
    }

    private static string LimitText(string text, int maxCharacters)
    {
        if (string.IsNullOrEmpty(text) || maxCharacters <= 0 || text.Length <= maxCharacters)
        {
            return text ?? string.Empty;
        }

        return text.Substring(0, maxCharacters).TrimEnd() + "...";
    }

    private string BuildRelevantHistoryContext(string userText, int promptMessageCount)
    {
        if (!enablePersistentConversation || maxRelevantHistoryMessages <= 0 || maxRelevantHistoryCharacters <= 0)
        {
            return string.Empty;
        }

        List<ChatMessageRecord> relevant;
        lock (conversationLock)
        {
            if (activeSession == null || activeSession.messages == null)
            {
                return string.Empty;
            }

            relevant = ChatHistoryStore.FindRelevantMessages(
                activeSession.messages,
                userText,
                maxRelevantHistoryMessages,
                maxRelevantHistoryCharacters,
                promptMessageCount,
                includeInterruptedAssistantRepliesInPrompt);
        }

        if (relevant.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("以下是与当前问题可能相关的较早聊天记录，用于延续长期记忆。");
        foreach (var message in relevant)
        {
            var label = message.role == "user" ? "用户" : "助手";
            builder.AppendLine($"[{label}] {message.content}");
        }

        return builder.ToString().Trim();
    }

    private void AddHistory(string userText, string assistantText)
    {
        history.Add(new ChatCompletionService.ChatMessage("assistant", assistantText));

        var maxMessages = deepSeekConfig != null ? deepSeekConfig.maxHistoryMessages : 12;
        var maxRuntimeMessages = Mathf.Max(maxMessages * 4, maxMessages);
        while (history.Count > maxRuntimeMessages)
        {
            history.RemoveAt(0);
        }

        UpdateConversationDisplay();
    }

    private void UpdateConversationDisplay()
    {
        currentConversationText = FormatConversationText(80);
        onConversationChanged?.Invoke(currentConversationText);
    }

    public string FormatConversationText(int maxMessages)
    {
        EnsureMemoryStores();
        var builder = new StringBuilder();

        if (enablePersistentConversation && activeSession != null)
        {
            builder.AppendLine($"会话：{activeSession.title}");
            builder.AppendLine($"目录：{conversationDirectoryPath}");

            var messages = activeSession.messages ?? new List<ChatMessageRecord>();
            var start = Mathf.Max(0, messages.Count - Mathf.Max(1, maxMessages));
            for (var i = start; i < messages.Count; i++)
            {
                var message = messages[i];
                if (message == null
                    || !IsDisplayableConversationRole(message.role)
                    || string.IsNullOrWhiteSpace(message.content))
                {
                    continue;
                }

                var label = message.role == "user" ? "用户" : "助手";
                var status = message.status == "completed" ? string.Empty : $" ({message.status})";
                var displayContent = GetDisplayMessageContent(message.role, message.content);
                builder.AppendLine($"{label}{status}：{displayContent}");
            }

            return builder.ToString().TrimEnd();
        }

        foreach (var message in history)
        {
            if (!IsDisplayableConversationRole(message.role))
            {
                continue;
            }

            var label = message.role == "user" ? "用户" : "助手";
            var displayContent = GetDisplayMessageContent(message.role, message.content);
            builder.AppendLine($"{label}：{displayContent}");
        }

        return builder.ToString().TrimEnd();
    }

    private static bool IsDisplayableConversationRole(string role)
    {
        return role == "user" || role == "assistant";
    }

    private static string GetDisplayMessageContent(string role, string content)
    {
        if (role != "user" || string.IsNullOrEmpty(content))
        {
            return content;
        }

        return StripEmotionPromptBlock(content);
    }

    private static string StripEmotionPromptBlock(string content)
    {
        const string marker = "[Emotion recognition captured during this utterance]";
        var markerIndex = content.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return content;
        }

        return content.Substring(0, markerIndex).TrimEnd('\r', '\n');
    }

    private void ReportError(string message)
    {
        Debug.LogError(message);
        onError?.Invoke(message);
    }

    private void EnqueueMainThread(Action action)
    {
        mainThreadActions.Enqueue(action);
    }
}
