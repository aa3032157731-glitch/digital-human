using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

public sealed class AssistantSettingsHub : MonoBehaviour
{
    public static bool ModalOpen { get; private set; }
    public bool IsOpen => overlay != null && overlay.gameObject.activeSelf;
    public RuntimeVoiceChatSettings Draft => draft;

    private const string IdleStatus = "修改后点击“保存并应用”生效";
    private const float SidebarWidth = 232;
    private AdvancedSettingsPanel owner;
    private VoiceChatController controller;
    private RectTransform canvas, overlay, window, sheet, content, crumbs, confirmation;
    private ScrollRect scroll;
    private Text heading, subtitle, status;
    private Image statusDot;
    private readonly List<KeyValuePair<string, Button>> navigation = new List<KeyValuePair<string, Button>>();
    private RuntimeVoiceChatSettings draft;
    private string baseline, page = "providers", category = "聊天", speechText = "你好，我是你的数字人助手，很高兴与你见面。";
    private string statusText = IdleStatus;
    private UiTone statusTone;
    private float statusRefreshAt;
    private Action reopen;
    private readonly HashSet<string> expandedSections = new HashSet<string>();
    private readonly ServiceCatalog catalog = new ServiceCatalog();
    private CancellationTokenSource listCts, previewCts, testCancel = new CancellationTokenSource();
    private int pageGeneration;
    private bool previewRunning;
    private readonly Dictionary<string, string> invalid = new Dictionary<string, string>();
    private readonly Dictionary<string, string> raw = new Dictionary<string, string>();
    private readonly Dictionary<string, string> labels = new Dictionary<string, string>();
    private sealed class TestRecord { public string fingerprint; public bool running; public ServiceTestResult result; }
    private readonly Dictionary<string, TestRecord> tests = new Dictionary<string, TestRecord>();

    // ---------- shell ----------

    public void Build(AdvancedSettingsPanel entry)
    {
        owner = entry; controller = owner.controller;
        CollectLabels();
        var go = owner.interfaceRoot != null ? owner.interfaceRoot.gameObject : new GameObject("AssistantInterface", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        if (owner.interfaceRoot == null) go.transform.SetParent(transform, false);
        canvas = go.GetComponent<RectTransform>();
        var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 500;
        var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        if (controller != null) go.AddComponent<AssistantDashboard>().Build(canvas, controller, Open);

        overlay = AssistantUi.Rect(canvas, "SettingsOverlay"); AssistantUi.Fill(overlay);
        overlay.gameObject.AddComponent<Image>().color = AssistantUi.Backdrop;
        window = AssistantUi.Rect(overlay, "SettingsWindow"); window.anchorMin = window.anchorMax = window.pivot = new Vector2(.5f, .5f);
        AssistantUi.Box(window, AssistantUi.Hex(0xECEAE4), 20); AssistantUi.Outline(window, AssistantUi.Hex(0xDCD8D0), 20);
        AssistantUi.Shadow(window, 20, 40, .24f, 14);

        BuildSidebar();

        sheet = AssistantUi.Rect(window, "Sheet"); AssistantUi.Fill(sheet, SidebarWidth, 10, 10, 10);
        AssistantUi.Box(sheet, AssistantUi.Hex(0xF9F8F5), 16); AssistantUi.Outline(sheet, AssistantUi.Border, 16);

        var head = AssistantUi.Stack(sheet, "Header", false, 0, 4);
        head.anchorMin = new Vector2(0, 1); head.anchorMax = Vector2.one; head.pivot = new Vector2(.5f, 1);
        head.offsetMin = new Vector2(40, -112); head.offsetMax = new Vector2(-96, -22);
        head.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.LowerLeft;
        crumbs = AssistantUi.Row(head, "Crumbs", 4); AssistantUi.Size(crumbs, 28);
        heading = AssistantUi.Label(head, "服务来源", 26, AssistantUi.Ink, 38, true);
        subtitle = AssistantUi.Caption(head, "", 14);
        var close = AssistantUi.IconButton(sheet, "×", RequestClose, 40, 24); var closeRect = (RectTransform)close.transform;
        closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = Vector2.one; closeRect.anchoredPosition = new Vector2(-24, -22); closeRect.sizeDelta = new Vector2(40, 40);

        content = AssistantUi.Scroll(sheet, "SettingsContent", out scroll);
        AssistantUi.Fill((RectTransform)scroll.transform, 40, 76, 32, 124);
        var layout = content.GetComponent<VerticalLayoutGroup>(); layout.spacing = 16; layout.padding = new RectOffset(0, 8, 2, 28);

        var footer = AssistantUi.Rect(sheet, "Footer"); footer.anchorMin = Vector2.zero; footer.anchorMax = new Vector2(1, 0); footer.pivot = new Vector2(.5f, 0);
        footer.offsetMin = new Vector2(0, 0); footer.offsetMax = new Vector2(0, 68);
        var line = AssistantUi.Rect(footer, "Line"); line.anchorMin = new Vector2(0, 1); line.anchorMax = Vector2.one; line.offsetMin = new Vector2(24, -1); line.offsetMax = new Vector2(-24, 0);
        line.gameObject.AddComponent<Image>().color = AssistantUi.Border;
        var bar = AssistantUi.Row(footer, "Bar", 10); AssistantUi.Fill(bar, 40, 0, 28, 1);
        statusDot = AssistantUi.Dot(bar, AssistantUi.Faint, 8).GetComponent<Image>();
        status = AssistantUi.Label(bar, IdleStatus, 14, AssistantUi.Muted); status.horizontalOverflow = HorizontalWrapMode.Wrap;
        status.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        AssistantUi.Button(bar, "重新读取", Reload, ButtonKind.Secondary, 40, 104);
        AssistantUi.Button(bar, "保存并应用", () => Save(), ButtonKind.Primary, 40, 132);
        Resize(); overlay.gameObject.SetActive(false);
    }

    private void BuildSidebar()
    {
        var side = AssistantUi.Stack(window, "Navigation", false, 0, 4);
        side.anchorMin = Vector2.zero; side.anchorMax = new Vector2(0, 1); side.pivot = new Vector2(0, .5f);
        side.offsetMin = new Vector2(16, 24); side.offsetMax = new Vector2(SidebarWidth - 12, -26);
        var brand = AssistantUi.Row(side, "Brand", 12); AssistantUi.Size(brand, 48);
        var mark = AssistantUi.Rect(brand, "Mark"); AssistantUi.Size(mark, 38, 38); AssistantUi.Box(mark, AssistantUi.Accent, 11);
        var letter = AssistantUi.Label(mark, "数", 18, Color.white, -1, true); AssistantUi.Fill(letter.rectTransform); letter.alignment = TextAnchor.MiddleCenter;
        var names = AssistantUi.Stack(brand, "Names", false, 0, 0);
        AssistantUi.Label(names, "数字人", 17, AssistantUi.Ink, 24, true);
        AssistantUi.Label(names, "设置中心", 12, AssistantUi.Muted, 18);
        AssistantUi.Spacer(side, 22);
        AssistantUi.Label(side, "配置", 12, AssistantUi.Faint, 22);
        Nav(side, "服务来源", "providers", () => ShowProviders());
        Nav(side, "机体模块", "modules", ShowModules);
        Nav(side, "通用设置", "general", ShowGeneral);
        var fill = AssistantUi.Rect(side, "Fill"); fill.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        var tip = AssistantUi.Caption(side, "配置只保存在本机。\n密钥可在保存前测试是否可用。", 12); tip.color = AssistantUi.Faint;
    }

    private void Nav(Transform parent, string text, string group, Action open)
    {
        var b = AssistantUi.Button(parent, text, open, ButtonKind.Ghost, 42);
        var label = b.GetComponentInChildren<Text>(); label.alignment = TextAnchor.MiddleLeft; AssistantUi.Fill(label.rectTransform, 16, 0, 12, 0);
        navigation.Add(new KeyValuePair<string, Button>(group, b));
    }

    private void HighlightNavigation(string group)
    {
        foreach (var item in navigation)
        {
            bool on = item.Key == group;
            var style = item.Value.GetComponent<UiPointerStyle>(); var label = item.Value.GetComponentInChildren<Text>();
            if (on) style.Set(AssistantUi.Surface, AssistantUi.Surface, AssistantUi.Subtle, AssistantUi.Surface, AssistantUi.AccentInk, AssistantUi.Faint);
            else style.Set(AssistantUi.Alpha(AssistantUi.Hover, 0), AssistantUi.Alpha(AssistantUi.Hover, .7f), AssistantUi.Hover, AssistantUi.Alpha(AssistantUi.Hover, 0), AssistantUi.Ink, AssistantUi.Faint);
            label.fontStyle = on ? FontStyle.Bold : FontStyle.Normal; style.Snap();
        }
    }

    private void CollectLabels()
    {
        foreach (var t in owner.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("Row_")) continue;
            var label = t.GetComponentsInChildren<Text>(true).FirstOrDefault();
            if (label == null || string.IsNullOrWhiteSpace(label.text)) continue;
            var name = t.name.Substring(t.name.LastIndexOf('_') + 1).ToLowerInvariant();
            if (!labels.ContainsKey(name)) labels.Add(name, label.text);
        }
    }

    private string Label(string name)
    {
        switch (name)
        {
            case "apiKey": return "API 密钥";
            case "apiSecret": return "API Secret";
            case "appId": return "App ID";
            case "accessKey": return "Access Key";
            case "region": return "资源区域";
            case "uri": return "接口地址（Base URL 或完整请求地址）";
            case "resourceEndpoint": return "资源终结点（可选，用于读取音色列表）";
            case "endpoint": return "接口地址";
            case "resourceId": return "资源 ID";
            case "userId": return "用户标识";
            case "websocketUrl": return "WebSocket 地址";
            case "serviceId": return "服务 ID";
            case "resId": return "热词资源 ID（可选）";
            case "displayName": return "配置名称";
            case "model": return "模型名称 / 接入点 ID";
            case "voiceName": case "speaker": return "音色 ID";
            case "language": return "语言";
            case "style": return "说话风格";
            case "styleDegree": return "风格强度（0.01–2）";
            case "ratePercent": return "语速变化 %（-50–100）";
            case "pitchPercent": return "音调变化 %（-50–50）";
            case "volumePercent": return "音量（0–100）";
            case "continuousLipSync": return "连续口型（按小句准备）";
            case "mouthIntensity": return "嘴部强度（0–1.5）";
            case "jawAmplitude": return "下颌幅度（0–1.2）";
            case "lipSyncOffsetMs": return "口型提前量（毫秒，-200–200）";
            case "systemPrompt": return "系统提示词";
            case "maxHistoryMessages": return "保留历史消息数（2–40）";
            case "temperature": return "温度 Temperature（0–2）";
            case "topP": return "Top P（0–1）";
            case "maxTokens": return "最大输出 Token 数";
            case "enableTools": return "启用工具调用";
            case "sendSampling": return "发送采样参数";
            case "sendTokenLimit": return "限制输出长度";
            case "useMaxCompletionTokens": return "使用 max_completion_tokens";
            case "disableThinking": return "关闭 DeepSeek 思考";
            case "firstChunkChars": return "首段字符数";
            case "nextChunkChars": return "后续段字符数";
            case "maxChunkChars": return "每段最大字符数";
            case "firstWaitMs": return "首段等待（毫秒）";
            case "nextWaitMs": return "后续段等待（毫秒）";
            case "idleTimeoutSeconds": return "空闲超时（秒）";
            case "requestTimeoutSeconds": return "请求超时（秒）";
            case "talkBoolParameter": return "说话动画参数（Bool）";
            case "pushToTalkKey": return "按住说话按键";
            case "enablePushToTalk": return "启用按住说话";
            case "listenOnStart": return "启动后自动监听";
            case "logRequestPayloadToConsole": return "在控制台打印请求 JSON";
            case "requestPayloadLogChunkCharacters": return "日志分片字符数";
            case "startBufferSeconds": return "起播缓冲（秒）";
            case "resumeBufferSeconds": return "补缓冲恢复门槛（秒）";
            case "maxQueuedSeconds": return "待写队列上限（秒）";
            case "logDiagnostics": return "输出音频性能诊断";
            case "enableAgentTools": return "启用工具调用";
            case "enableDateTimeTool": return "允许查询当前时间";
            case "enableWebSearchTool": return "允许联网搜索";
            case "maxAgentToolRounds": return "最多工具调用轮数";
            case "authorizationHeaderName": return "鉴权请求头";
            case "alternateAuthorizationHeaderName": return "备用鉴权请求头";
            case "domain": return "识别领域（domain）";
            case "accent": return "口音";
            case "vadEosMs": return "静音断句时长（毫秒）";
            case "dynamicCorrection": return "动态修正（dwa）";
            case "enablePunctuation": return "返回标点";
            case "languageTypeControl": return "中英过滤（1–3）";
            case "microphoneDevice": return "麦克风设备名（留空使用默认设备）";
            case "sampleRate": return "采样率";
            case "frameBytes": return "每帧 PCM 字节数";
            case "frameIntervalMs": return "发帧间隔（毫秒）";
            case "maxRecordSeconds": return "最长录音（秒）";
            case "edition": return "搜索版本";
            case "searchSource": return "搜索来源";
            case "maxResults": return "最多结果数";
            case "webTopK": return "网页结果数";
            case "videoTopK": return "视频结果数";
            case "imageTopK": return "图片结果数";
            case "aladdinTopK": return "阿拉丁结果数";
            case "maxQueryCharacters": return "查询最长字符数";
            case "maxContextCharacters": return "上下文最长字符数";
            case "safeSearch": return "安全搜索";
            case "autoSortFreshQueries": return "时效性问题按时间排序";
            case "timeoutSeconds": return "超时（秒）";
            case "enablePersistentConversation": return "保存对话记录";
            case "persistentDataFolder": return "数据根目录名";
            case "conversationsFolderName": return "对话记录目录名";
            case "loadLatestConversationOnStart": return "启动时加载最近会话";
            case "includeInterruptedAssistantRepliesInPrompt": return "被打断的回复也作为上下文";
            case "maxRelevantHistoryMessages": return "召回历史消息数";
            case "maxRelevantHistoryCharacters": return "召回历史最大字符数";
            case "streamingHistorySaveIntervalSeconds": return "流式回复保存间隔（秒）";
            case "enableKnowledgeBase": return "启用知识库";
            case "knowledgeBaseFolderName": return "知识库目录名";
            case "refreshKnowledgeBaseOnStart": return "启动时刷新知识库";
            case "injectFullKnowledgeBaseOnNewConversation": return "新对话注入完整知识库";
            case "maxInitialKnowledgeBaseCharacters": return "首次注入最大字符数（0 为不限）";
            case "audioEncoding": return "音频编码";
            case "audioFormat": return "音频格式";
            case "textEncoding": return "文本编码";
            case "speed": return "语速（0–100）";
            case "volume": return "音量（0–100）";
            case "pitch": return "音高（0–100）";
            case "oralLevel": return "口语化程度";
            case "sparkAssist": return "启用 Spark 辅助";
            case "stopSplit": return "关闭服务端分句";
            case "remainWrittenStyle": return "保持书面风格";
            case "minChunkChars": return "每段最少字符数";
            case "textPollIntervalMs": return "文本轮询间隔（毫秒）";
            case "backgroundSound": return "背景音";
            case "englishPronunciation": return "英文发音方式";
            case "numberReadMode": return "数字朗读方式";
            case "speechRate": return "语速（-50–100）";
            case "loudnessRate": return "音量（-50–100）";
            case "emotion": return "情感";
            case "emotionScale": return "情感强度（1–5）";
            case "enableVoiceInstruction": return "启用语音指令";
            case "voiceInstruction": return "语音指令";
            case "enableDeepSeekVoiceInstruction": return "由大模型自动生成语音指令";
            case "deepSeekVoiceInstructionPrompt": return "语音指令生成提示词";
            case "useSectionId": return "使用会话 ID 作为语音上下文";
        }
        return labels.TryGetValue(name.ToLowerInvariant(), out var value) ? value : name;
    }

    private static string Placeholder(string name)
    {
        switch (name)
        {
            case "apiKey": case "apiSecret": case "accessKey": return "粘贴密钥";
            case "region": return "例如 eastasia、japaneast";
            case "uri": return "例如 https://api.deepseek.com";
            case "resourceEndpoint": return "可选，例如 https://<资源名>.cognitiveservices.azure.com";
            case "appId": return "控制台中的 App ID";
            default: return null;
        }
    }

    private static string Describe(string kind)
    {
        switch (kind)
        {
            case "DeepSeek": return "DeepSeek 官方大模型 API";
            case "Ark": return "火山引擎方舟大模型平台";
            case "Compatible": return "任意 OpenAI 兼容接口";
            case "AzureSpeech": return "微软神经网络语音，支持连续口型";
            case "Doubao": return "字节跳动豆包语音合成 2.0";
            case "XunfeiSuperSmart": return "讯飞超拟人语音合成";
            case "XunfeiOnline": return "讯飞在线语音合成";
            case "ASR": return "讯飞流式语音听写";
            case "Search": return "百度千帆 AI 搜索";
            default: return "";
        }
    }

    // ---------- lifecycle ----------

    public void Open()
    {
        if (IsOpen) return;
        draft = controller != null ? controller.CaptureRuntimeSettings() : VoiceChatRuntimeConfigStore.LoadSettings() ?? new RuntimeVoiceChatSettings();
        draft.EnsureDefaults(); baseline = JsonConvert.SerializeObject(draft); raw.Clear(); invalid.Clear();
        if (testCancel.IsCancellationRequested) { testCancel.Dispose(); testCancel = new CancellationTokenSource(); }
        overlay.gameObject.SetActive(true); ModalOpen = true; ShowProviders(); SetStatus(IdleStatus);
        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
    }
    private bool Dirty => draft != null && (invalid.Count > 0 || JsonConvert.SerializeObject(draft) != baseline);
    public void RequestClose()
    {
        if (!IsOpen) return;
        if (!Dirty) { CloseNow(); return; }
        Confirm("有尚未保存的修改", "保存后才会生效；放弃修改将恢复打开设置时的配置。", () => { if (Save()) CloseNow(); }, "保存并关闭", CloseNow, "放弃修改");
    }
    private void CloseNow()
    {
        CancelOperations(); overlay.gameObject.SetActive(false); ModalOpen = false;
        if (confirmation != null) AssistantUi.Remove(confirmation.gameObject);
    }
    public void Reload()
    {
        Confirm("重新读取配置？", "当前未保存的修改将被丢弃，改为读取本机保存的配置。", () =>
        {
            var loaded = VoiceChatRuntimeConfigStore.LoadSettings();
            if (loaded == null) { SetStatus("配置读取失败，当前草稿已保留。", UiTone.Danger); return; }
            draft = loaded; baseline = JsonConvert.SerializeObject(draft); raw.Clear(); invalid.Clear(); ShowProviders();
            SetStatus("已重新读取；点击“保存并应用”后启用。", UiTone.Accent);
        }, "重新读取");
    }
    public bool Save()
    {
        if (draft == null) return false;
        if (invalid.Count > 0) { SetStatus("请修正无效数值：" + invalid.First().Value, UiTone.Danger); return false; }
        var candidate = JsonConvert.DeserializeObject<RuntimeVoiceChatSettings>(JsonConvert.SerializeObject(draft));
        try
        {
            candidate.EnsureDefaults(); ServiceAccounts.Resolve(candidate);
            var active = candidate.modelProfiles.Find(p => p.id == candidate.activeModelProfileId);
            // An untouched, empty first-run chat slot may remain a draft while speech is configured.
            var previous = JsonConvert.DeserializeObject<RuntimeVoiceChatSettings>(baseline);
            var priorActive = previous.modelProfiles.Find(p => p.id == previous.activeModelProfileId);
            if (!string.IsNullOrWhiteSpace(active.apiKey) || !string.IsNullOrWhiteSpace(priorActive?.apiKey)) active.Validate();
            if (active.sendSampling && (active.temperature < 0 || active.temperature > 2 || active.topP < 0 || active.topP > 1)) throw new InvalidOperationException("温度需要在 0–2，Top P 需要在 0–1 之间。");
            if (candidate.deepSeek.maxHistoryMessages < 2 || candidate.deepSeek.maxHistoryMessages > 40) throw new InvalidOperationException("历史消息数需要在 2–40 之间。");
            if (candidate.readRepliesAloud)
            {
                var a = candidate.serviceAccounts.Find(x => x.kind == candidate.features.ttsProvider && x.selected);
                var oldAccount = previous.serviceAccounts.Find(x => x.kind == previous.features.ttsProvider && x.selected);
                if (ServiceAccounts.Configured(oldAccount ?? ServiceAccounts.New(previous.features.ttsProvider)) && (a == null || !ServiceAccounts.Configured(a)))
                    throw new InvalidOperationException("当前发声账号缺少必要凭据，请完善配置。");
                if (a != null && ServiceAccounts.Configured(a) && a.kind == "AzureSpeech") candidate.azureTts.Validate();
            }
            if (!VoiceChatRuntimeConfigStore.SaveSettings(candidate)) { SetStatus("保存失败，当前运行配置未改变。", UiTone.Danger); return false; }
            CancelOperations(); controller?.InterruptAssistantOutput(); controller?.ApplyRuntimeSettings(candidate);
            draft = candidate; baseline = JsonConvert.SerializeObject(draft); raw.Clear(); invalid.Clear();
            // Rebuild the current page against the newly committed snapshot.
            if (reopen != null) reopen(); else ShowModules();
            SetStatus("已保存并应用，下次启动会自动恢复。", UiTone.Success); return true;
        }
        catch (Exception ex) { SetStatus(ex is InvalidOperationException ? ex.Message : "保存失败，请检查配置。", UiTone.Danger); return false; }
    }

    // ---------- page scaffolding ----------

    private void BeginPage(string title, string id, string description, string group, Action back = null, string backLabel = null)
    {
        CancelPageOperations(); pageGeneration++; page = id; heading.text = title; subtitle.text = description ?? "";
        subtitle.gameObject.SetActive(!string.IsNullOrEmpty(description));
        AssistantUi.Clear(crumbs);
        if (back != null) AssistantUi.Link(crumbs, "‹  " + backLabel, back, AssistantUi.Muted);
        AssistantUi.Clear(content); scroll.verticalNormalizedPosition = 1; builtWidth = ContentWidth;
        HighlightNavigation(group);
    }
    private void SetStatus(string message, UiTone tone = UiTone.Neutral) { statusText = message; statusTone = tone; RenderStatus(); }
    private void RenderStatus()
    {
        if (status == null) return;
        string text = statusText; var tone = statusTone;
        if (string.IsNullOrEmpty(text) || text == IdleStatus)
        {
            bool dirty = Dirty; text = dirty ? "有尚未保存的修改" : IdleStatus; tone = dirty ? UiTone.Warning : UiTone.Neutral;
        }
        status.text = text; status.color = tone == UiTone.Danger ? AssistantUi.ToneInk(UiTone.Danger) : AssistantUi.Muted;
        statusDot.color = AssistantUi.ToneColor(tone);
    }
    private float ContentWidth => Mathf.Max(320, ((RectTransform)scroll.transform).rect.width - 8);
    private void Grid<T>(Transform parent, IEnumerable<T> values, Action<Transform, T> render, int columns = 3, float width = -1, float minCell = 270)
    {
        if (width <= 0) width = ContentWidth;
        columns = Mathf.Clamp(Mathf.FloorToInt((width + 14) / minCell), 1, columns);
        var items = values.ToList(); RectTransform row = null;
        for (int i = 0; i < items.Count; i++)
        {
            if (i % columns == 0) { row = AssistantUi.Stack(parent, "GridRow", true, 0, 14); row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft; }
            var cell = AssistantUi.Stack(row, "Cell", false, 0, 6); AssistantUi.Size(cell, -1, 0, 1); render(cell, items[i]);
        }
        // Keep cell widths equal in a partly filled last row.
        for (int i = items.Count; row != null && i % columns != 0; i++) AssistantUi.Size(AssistantUi.Rect(row, "Filler"), -1, 0, 1);
    }
    private RectTransform Section(string title, string description = null, Transform parent = null) => AssistantUi.Section(parent ?? content, title, description);
    private RectTransform SectionWithAction(string title, string description, string actionText, Action action, Transform parent = null)
    {
        var box = AssistantUi.Section(parent ?? content, null);
        var head = AssistantUi.Row(box, "Header", 12);
        var names = AssistantUi.Stack(head, "Titles", false, 0, 6); names.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        AssistantUi.Title(names, title, 16);
        if (!string.IsNullOrEmpty(description)) AssistantUi.Caption(names, description);
        AssistantUi.Button(head, actionText, action, ButtonKind.Soft, 36);
        return box;
    }
    private RectTransform Foldout(string title, string description, Transform parent = null)
    {
        var box = AssistantUi.Section(parent ?? content, null);
        var head = AssistantUi.Row(box, "Header", 12); head.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0);
        var names = AssistantUi.Stack(head, "Titles", false, 0, 6); names.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        AssistantUi.Title(names, title, 16);
        if (!string.IsNullOrEmpty(description)) AssistantUi.Caption(names, description);
        bool open = expandedSections.Contains(title);
        var chevron = AssistantUi.Label(head, open ? "收起" : "展开", 13, AssistantUi.AccentInk);
        var fields = AssistantUi.Stack(box, "Fields", false, 0, 14); fields.gameObject.SetActive(open);
        var button = head.gameObject.AddComponent<Button>(); button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() =>
        {
            bool visible = !fields.gameObject.activeSelf; fields.gameObject.SetActive(visible); chevron.text = visible ? "收起" : "展开";
            if (visible) expandedSections.Add(title); else expandedSections.Remove(title);
        });
        return fields;
    }
    private RectTransform FieldGroup(Transform parent, string label, string hint = null)
    {
        var group = AssistantUi.Stack(parent, "Field", false, 0, 6);
        AssistantUi.Label(group, label, 14, AssistantUi.Muted);
        if (!string.IsNullOrEmpty(hint)) AssistantUi.Caption(group, hint, 12);
        return group;
    }

    // ---------- 服务来源 ----------

    private static string Category(string kind) => ServiceAccounts.IsChat(kind) ? "聊天" : kind == "ASR" ? "语音识别" : kind == "Search" ? "网络搜索" : "语音合成";
    private void ShowProviders()
    {
        BeginPage("服务来源", "providers", "连接大模型、语音与搜索服务。填写密钥后可以直接测试是否可用。", "providers");
        reopen = () => ShowProviders();
        AssistantUi.Segmented(content, new[] { "聊天", "语音合成", "语音识别", "网络搜索" }, category, value => { category = value; ShowProviders(); }, 40);
        Grid(content, ServiceAccounts.Kinds.Where(k => Category(k) == category), (cell, kind) =>
        {
            var accounts = draft.serviceAccounts.Where(a => a.kind == kind).ToList();
            int configured = accounts.Count(ServiceAccounts.Configured);
            var card = AssistantUi.Card(cell, ServiceAccounts.Name(kind), Describe(kind), () => ShowAccounts(kind), false, 118);
            var chips = AssistantUi.Row(card.transform, "Chips", 6);
            AssistantUi.Chip(chips, configured > 0 ? configured + " 个账号已配置" : "尚未配置", configured > 0 ? UiTone.Success : UiTone.Neutral);
            if (accounts.Any(InUse)) AssistantUi.Chip(chips, "使用中", UiTone.Accent);
            var failed = accounts.Select(a => Record(a.id, Fingerprint(a))).FirstOrDefault(r => r?.result != null && !r.result.ok);
            if (failed != null) AssistantUi.Chip(chips, "测试未通过", UiTone.Danger);
        });
    }
    private bool InUse(ServiceAccount a)
    {
        if (ServiceAccounts.IsChat(a.kind)) return draft.modelProfiles.Find(p => p.id == draft.activeModelProfileId)?.serviceAccountId == a.id;
        if (a.kind == "ASR" || a.kind == "Search") return a.selected;
        return a.selected && a.kind == draft.features.ttsProvider;
    }
    private void ShowAccounts(string kind)
    {
        BeginPage(ServiceAccounts.Name(kind), "accounts", Describe(kind) + "。同一服务可以保存多个账号。", "providers", ShowProviders, "服务来源");
        reopen = () => ShowAccounts(kind);
        var list = SectionWithAction("账号", "在「机体模块」中选择要使用的账号；测试会用填写的信息发起一次真实请求。", "+ 添加账号", () =>
        {
            var a = ServiceAccounts.New(kind); a.displayName += " " + (draft.serviceAccounts.Count(x => x.kind == kind) + 1);
            draft.serviceAccounts.Add(a); EditAccount(a);
        });
        var accounts = draft.serviceAccounts.Where(a => a.kind == kind).ToList();
        if (accounts.Count == 0) AssistantUi.Caption(list, "还没有账号，点击“添加账号”开始配置。", 14);
        foreach (var account in accounts) AccountRow(list, account);
    }
    private void AccountRow(Transform parent, ServiceAccount account)
    {
        AssistantUi.Divider(parent);
        var row = AssistantUi.Row(parent, "Account", 14); AssistantUi.Size(row, 58);
        var names = AssistantUi.Stack(row, "Names", false, 0, 3); names.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        AssistantUi.Label(names, account.displayName, 15, AssistantUi.Ink, -1, true);
        AssistantUi.Caption(names, Summary(account), 12);
        var chips = AssistantUi.Row(row, "Chips", 6);
        Action renderChips = () =>
        {
            if (chips == null) return;
            AssistantUi.Clear(chips);
            if (InUse(account)) AssistantUi.Chip(chips, "使用中", UiTone.Accent);
            var record = Record(account.id, Fingerprint(account));
            if (record != null && record.running) AssistantUi.Chip(chips, "测试中…", UiTone.Accent);
            else if (record?.result != null) AssistantUi.Chip(chips, record.result.ok ? "连接正常" : "测试未通过", record.result.ok ? UiTone.Success : UiTone.Danger);
            else AssistantUi.Chip(chips, ServiceAccounts.Configured(account) ? "已配置" : "待完善", ServiceAccounts.Configured(account) ? UiTone.Neutral : UiTone.Warning);
        };
        renderChips();
        AssistantUi.Button(row, "测试", () => RunTest(account.id, Fingerprint(account), token => ServiceConnectionTester.TestAccountAsync(account, draft, token), renderChips), ButtonKind.Secondary, 34, 64);
        AssistantUi.Button(row, "编辑", () => EditAccount(account), ButtonKind.Soft, 34, 64);
    }
    private static string Summary(ServiceAccount a)
    {
        var parts = new List<string>();
        string key = a.Get("apiKey");
        parts.Add(string.IsNullOrWhiteSpace(key) ? "未填写密钥" : "密钥 ••••" + key.Trim().Substring(Math.Max(0, key.Trim().Length - 4)));
        if (!string.IsNullOrWhiteSpace(a.Get("region"))) parts.Add("区域 " + a.Get("region"));
        if (!string.IsNullOrWhiteSpace(a.Get("appId"))) parts.Add("App ID " + a.Get("appId"));
        if (ServiceAccounts.IsChat(a.kind) && Uri.TryCreate(a.Get("uri"), UriKind.Absolute, out var uri)) parts.Add(uri.Host);
        return string.Join("  ·  ", parts);
    }
    private void EditAccount(ServiceAccount account)
    {
        string id = account.id, kind = account.kind;
        BeginPage(account.displayName, "account", ServiceAccounts.Name(kind) + " · " + (ServiceAccounts.Configured(account) ? "已配置" : "待完善"), "providers", () => ShowAccounts(kind), ServiceAccounts.Name(kind));
        reopen = () => { var a = draft.serviceAccounts.Find(x => x.id == id); if (a != null) EditAccount(a); else ShowAccounts(kind); };
        var basic = Section("基础信息", "保存凭据不会自动切换正在使用的模型或音色。");
        AssistantUi.Input(FieldGroup(basic, "账号名称"), account.displayName, v => account.displayName = v);
        var credentials = new[] { "apiKey", "appId", "apiSecret", "accessKey", "region", "uri" };
        foreach (var name in ServiceAccounts.Fields(kind).Where(credentials.Contains)) AccountField(basic, account, name);
        var test = Section("连接测试", "用上面填写的信息发起一次真实请求，验证密钥是否可用。测试不会保存配置，也不会影响正在使用的服务。");
        TestPanel(test, account.id, () => Fingerprint(account), token => ServiceConnectionTester.TestAccountAsync(account, draft, token));
        var others = ServiceAccounts.Fields(kind).Where(n => !credentials.Contains(n)).ToList();
        if (others.Count > 0)
        {
            var advanced = Foldout("高级配置", "接口地址、资源 ID 等，一般无需修改。");
            foreach (var name in others) AccountField(advanced, account, name);
        }
        var danger = Section("删除账号", "删除后点击“保存并应用”才会生效。");
        var row = AssistantUi.Row(danger, "Actions", 8);
        AssistantUi.Button(row, "删除此账号", () =>
        {
            if (draft.modelProfiles.Any(p => p.serviceAccountId == account.id) || (account.selected && (account.kind == draft.features.ttsProvider || account.kind == "ASR" || account.kind == "Search")))
            { SetStatus("此账号仍在使用中，请先在机体模块中切换账号或删除对应的模型配置。", UiTone.Warning); return; }
            draft.serviceAccounts.Remove(account); tests.Remove(account.id); ShowAccounts(kind);
        }, ButtonKind.Danger, 38);
    }
    private void AccountField(Transform parent, ServiceAccount account, string name)
    {
        var group = FieldGroup(parent, Label(name));
        bool secret = name == "apiKey" || name == "apiSecret" || name == "accessKey";
        if (!secret) { AssistantUi.Input(group, account.Get(name), v => account.Set(name, v.Trim()), false, false, false, Placeholder(name)); return; }
        var row = AssistantUi.Row(group, "SecretRow", 8);
        var input = AssistantUi.Input(row, account.Get(name), v => account.Set(name, v.Trim()), true, false, false, Placeholder(name));
        AssistantUi.Size(input, 40, -1, 1);
        Button reveal = null;
        reveal = AssistantUi.Button(row, "显示", () =>
        {
            bool show = input.contentType == InputField.ContentType.Password;
            input.contentType = show ? InputField.ContentType.Standard : InputField.ContentType.Password; input.ForceLabelUpdate();
            reveal.GetComponentInChildren<Text>().text = show ? "隐藏" : "显示";
        }, ButtonKind.Secondary, 40, 72);
    }

    // ---------- connection tests ----------

    private static string Fingerprint(ServiceAccount a) => a.kind + "|" + string.Join("|", a.values.Select(v => v.name + "=" + v.value));
    private string ModelFingerprint(ChatModelProfile p)
    {
        var account = draft.serviceAccounts.Find(a => a.id == p.serviceAccountId);
        return JsonConvert.SerializeObject(p) + (account != null ? Fingerprint(account) : "");
    }
    private TestRecord Record(string key, string fingerprint) =>
        tests.TryGetValue(key, out var record) && (record.running || record.fingerprint == fingerprint) ? record : null;

    private void TestPanel(Transform parent, string key, Func<string> fingerprint, Func<CancellationToken, Task<ServiceTestResult>> run, string buttonText = "测试连接")
    {
        var row = AssistantUi.Stack(parent, "ConnectionTest", true, 0, 16);
        var group = row.GetComponent<HorizontalLayoutGroup>(); group.childForceExpandWidth = false; group.childAlignment = TextAnchor.UpperLeft;
        Button button = null; RectTransform result = null;
        Action render = () =>
        {
            if (result == null) return;
            AssistantUi.Clear(result);
            var record = Record(key, fingerprint());
            if (button != null) button.interactable = record == null || !record.running;
            var chips = AssistantUi.Row(result, "Chips", 8);
            if (record == null) { AssistantUi.Chip(chips, "尚未测试", UiTone.Neutral); return; }
            if (record.running) { AssistantUi.Chip(chips, "正在测试…", UiTone.Accent); AssistantUi.Caption(result, "正在连接服务，最长等待 15 秒。"); return; }
            var r = record.result;
            AssistantUi.Chip(chips, r.ok ? "连接正常" : "测试未通过", r.ok ? UiTone.Success : UiTone.Danger);
            AssistantUi.Caption(chips, r.milliseconds + " ms · " + r.time.ToString("HH:mm:ss"), 12).color = AssistantUi.Faint;
            var message = AssistantUi.Label(result, r.message, 14, r.ok ? AssistantUi.Ink : AssistantUi.ToneInk(UiTone.Danger));
            message.alignment = TextAnchor.UpperLeft;
            if (!string.IsNullOrEmpty(r.detail)) AssistantUi.Caption(result, "原始信息：" + r.detail, 12).color = AssistantUi.Faint;
        };
        button = AssistantUi.Button(row, buttonText, () => RunTest(key, fingerprint(), run, render), ButtonKind.Primary, 40);
        result = AssistantUi.Stack(row, "Result", false, 0, 6); result.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        render();
    }
    private async void RunTest(string key, string fingerprint, Func<CancellationToken, Task<ServiceTestResult>> run, Action render)
    {
        if (tests.TryGetValue(key, out var existing) && existing.running) return;
        var record = new TestRecord { fingerprint = fingerprint, running = true }; tests[key] = record;
        render(); SetStatus("正在测试连接…", UiTone.Accent);
        var token = testCancel.Token;
        try { record.result = await run(token); }
        catch (OperationCanceledException) { if (tests.TryGetValue(key, out var current) && current == record) tests.Remove(key); return; }
        catch (Exception ex) { record.result = new ServiceTestResult { ok = false, message = ServiceConnectionTester.Describe(ex) }; }
        finally { record.running = false; }
        if (this == null) return;
        render();
        SetStatus((record.result.ok ? "测试通过：" : "测试未通过：") + record.result.message, record.result.ok ? UiTone.Success : UiTone.Danger);
    }

    // ---------- 机体模块 ----------

    private void ShowModules()
    {
        BeginPage("机体模块", "modules", "为数字人选择正在使用的能力：思考用的模型、声音、听觉、搜索、记忆与知识库。", "modules");
        reopen = ShowModules;
        var active = draft.modelProfiles.Find(p => p.id == draft.activeModelProfileId);
        Grid(content, new[] { "意识", "发声", "听觉", "网络搜索", "记忆", "知识库" }, (cell, module) =>
        {
            string description; string value; UiTone tone = UiTone.Neutral;
            switch (module)
            {
                case "意识": description = "对话使用的大模型"; value = string.IsNullOrWhiteSpace(active?.model) ? "未选择模型" : active.model; tone = string.IsNullOrWhiteSpace(active?.model) ? UiTone.Warning : UiTone.Accent; break;
                case "发声": description = "语音合成服务与音色"; value = ServiceAccounts.Name(draft.features.ttsProvider); tone = UiTone.Accent; break;
                case "听觉": description = "语音识别与麦克风"; value = SelectedName("ASR"); tone = value == "未选择账号" ? UiTone.Warning : UiTone.Accent; break;
                case "网络搜索": description = "联网搜索与工具调用"; value = draft.agentTools.enableWebSearchTool ? SelectedName("Search") : "已关闭"; tone = draft.agentTools.enableWebSearchTool ? UiTone.Accent : UiTone.Neutral; break;
                case "记忆": description = "历史消息与持久化"; value = draft.memory.enablePersistentConversation ? "持久化已开启" : "仅本次会话"; tone = draft.memory.enablePersistentConversation ? UiTone.Accent : UiTone.Neutral; break;
                default: description = "本地资料与上下文"; value = draft.knowledge.enableKnowledgeBase ? "已启用" : "已关闭"; tone = draft.knowledge.enableKnowledgeBase ? UiTone.Accent : UiTone.Neutral; break;
            }
            var card = AssistantUi.Card(cell, module, description, () => OpenModule(module), false, 118);
            var chips = AssistantUi.Row(card.transform, "Chips", 6); AssistantUi.Chip(chips, value, tone);
            if (module == "意识" && active != null)
            {
                var record = Record("model:" + active.id, ModelFingerprint(active));
                if (record?.result != null) AssistantUi.Chip(chips, record.result.ok ? "连接正常" : "测试未通过", record.result.ok ? UiTone.Success : UiTone.Danger);
            }
        });
    }
    private string SelectedName(string kind) => draft.serviceAccounts.Find(a => a.kind == kind && a.selected)?.displayName ?? "未选择账号";
    private void OpenModule(string module)
    {
        if (module == "意识") { ShowChat(); return; }
        if (module == "发声") { ShowSpeech(); return; }
        string description = module == "听觉" ? "语音识别服务与麦克风参数。" : module == "网络搜索" ? "联网搜索账号、搜索参数与工具调用。" : module == "记忆" ? "对话如何保存、如何作为上下文召回。" : "本地知识库目录与注入方式。";
        BeginPage(module, "module", description, "modules", ShowModules, "机体模块");
        reopen = () => OpenModule(module);
        if (module == "听觉")
        {
            AccountPicker(content, "ASR", () => OpenModule(module));
            ObjectFields(Section("识别设置", "一般保持默认即可。"), draft.xunfeiAsr, ServiceAccounts.Fields("ASR").Concat(new[] { "appId", "apiKey", "apiSecret" }).ToArray());
        }
        if (module == "网络搜索")
        {
            AccountPicker(content, "Search", () => OpenModule(module));
            ObjectFields(Section("工具调用", "允许模型在回答前查询时间或联网搜索。"), draft.agentTools);
            ObjectFields(Foldout("搜索参数", "结果数量、超时与上下文长度。"), draft.baiduSearch, ServiceAccounts.Fields("Search"));
        }
        if (module == "记忆") ObjectFields(Section("记忆设置"), draft.memory);
        if (module == "知识库") ObjectFields(Section("知识库设置"), draft.knowledge);
    }
    private void AccountPicker(Transform parent, string kind, Action redraw, float width = -1)
    {
        var box = SectionWithAction("服务账号", "选择此能力使用的账号；未配置的账号会先打开编辑页。", "管理账号", () => ShowAccounts(kind), parent);
        var accounts = draft.serviceAccounts.Where(a => a.kind == kind).ToList();
        if (accounts.Count == 0) AssistantUi.Caption(box, "还没有" + ServiceAccounts.Name(kind) + "账号，点击“管理账号”添加。", 14);
        Grid(box, accounts, (cell, a) =>
        {
            var card = AssistantUi.Card(cell, a.displayName, Summary(a), () =>
            {
                if (!ServiceAccounts.Configured(a)) { EditAccount(a); return; }
                foreach (var other in draft.serviceAccounts.Where(x => x.kind == kind)) other.selected = other == a;
                ServiceAccounts.Resolve(draft); redraw();
            }, a.selected, 96);
            var chips = AssistantUi.Row(card.transform, "Chips", 6);
            if (a.selected) AssistantUi.Chip(chips, "当前使用", UiTone.Accent);
            else AssistantUi.Chip(chips, ServiceAccounts.Configured(a) ? "可选择" : "待完善", ServiceAccounts.Configured(a) ? UiTone.Neutral : UiTone.Warning);
            var record = Record(a.id, Fingerprint(a));
            if (record?.result != null) AssistantUi.Chip(chips, record.result.ok ? "连接正常" : "测试未通过", record.result.ok ? UiTone.Success : UiTone.Danger);
        }, 2, (width > 0 ? width : ContentWidth) - 44, 240);
        var selected = accounts.Find(a => a.selected);
        if (selected != null && ServiceAccounts.Configured(selected))
        {
            AssistantUi.Divider(box);
            TestPanel(box, selected.id, () => Fingerprint(selected), token => ServiceConnectionTester.TestAccountAsync(selected, draft, token), "测试当前账号");
        }
    }

    // ---------- 意识 ----------

    private void ShowChat()
    {
        BeginPage("意识", "chat", "选择对话使用的大模型、服务账号与回复参数。", "modules", ShowModules, "机体模块");
        reopen = ShowChat;
        var active = draft.modelProfiles.Find(p => p.id == draft.activeModelProfileId);
        var profiles = SectionWithAction("模型配置", "可以保存多套配置，点击即可切换当前使用的模型。", "+ 新建配置", () =>
        {
            var p = ChatModelProfile.Create(active.preset); p.serviceAccountId = active.serviceAccountId; p.displayName = "新模型配置";
            draft.modelProfiles.Add(p); draft.activeModelProfileId = p.id; ShowChat();
        });
        Grid(profiles, draft.modelProfiles, (cell, p) =>
            AssistantUi.Card(cell, p.displayName, string.IsNullOrWhiteSpace(p.model) ? "待选择模型" : p.model, () => { draft.activeModelProfileId = p.id; ShowChat(); }, p == active, 80),
            3, ContentWidth - 44, 230);
        var sources = SectionWithAction("服务来源", "模型请求发送到哪个账号。", "管理账号", ShowProviders);
        var chatAccounts = draft.serviceAccounts.Where(a => ServiceAccounts.IsChat(a.kind)).ToList();
        if (chatAccounts.Count == 0) AssistantUi.Caption(sources, "还没有聊天服务账号，点击“管理账号”添加。", 14);
        Grid(sources, chatAccounts, (cell, a) =>
        {
            var card = AssistantUi.Card(cell, a.displayName, ServiceAccounts.Name(a.kind), () =>
            {
                if (!ServiceAccounts.Configured(a)) { EditAccount(a); return; }
                active.serviceAccountId = a.id; active.preset = a.kind; ShowChat();
            }, active.serviceAccountId == a.id, 80);
            if (!ServiceAccounts.Configured(a)) AssistantUi.Chip(AssistantUi.Row(card.transform, "Chips", 6), "待完善", UiTone.Warning);
        }, 3, ContentWidth - 44, 230);
        var account = draft.serviceAccounts.Find(a => a.id == active.serviceAccountId);
        var models = Section("模型", "填写模型名称或接入点 ID，也可以从在线列表中选择。");
        Field(models, active, "displayName"); Field(models, active, "model");
        if (account != null) CatalogPicker(models, account, active.model, entry => { SetField(active, "model", entry.id); ShowChat(); });
        var test = Section("连接测试", "用当前配置向模型发送一条简短消息，确认能够正常回复（会消耗极少量 Token）。");
        TestPanel(test, "model:" + active.id, () => ModelFingerprint(active), token => ServiceConnectionTester.TestActiveModelAsync(draft, token), "测试当前模型");
        var advanced = Foldout("模型参数", "系统提示词、温度、输出长度等。");
        Field(advanced, draft.deepSeek, "systemPrompt"); Field(advanced, draft.deepSeek, "maxHistoryMessages");
        ObjectFields(advanced, active, new[] { "id", "serviceAccountId", "apiKey", "uri", "preset", "displayName", "model" });
        if (draft.modelProfiles.Count > 1)
        {
            var danger = Section("删除配置", "删除当前模型配置，保存后生效。");
            AssistantUi.Button(AssistantUi.Row(danger), "删除此模型配置", () =>
            { ForgetFields(active); draft.modelProfiles.Remove(active); draft.activeModelProfileId = draft.modelProfiles[0].id; ShowChat(); }, ButtonKind.Danger, 38);
        }
    }

    // ---------- 发声 ----------

    private static readonly string[] SpeechKinds = { "AzureSpeech", "Doubao", "XunfeiSuperSmart", "XunfeiOnline" };
    private static string ShortName(string kind) => kind == "AzureSpeech" ? "Azure" : kind == "Doubao" ? "豆包" : kind == "XunfeiSuperSmart" ? "讯飞超拟人" : "讯飞在线";
    private void ShowSpeech()
    {
        BeginPage("发声", "speech", "选择语音合成服务与音色，可以直接试听效果。", "modules", ShowModules, "机体模块");
        reopen = ShowSpeech;
        bool wide = ContentWidth >= 980;
        var columns = AssistantUi.Stack(content, "SpeechColumns", wide, 0, 16);
        columns.GetComponent<HorizontalOrVerticalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
        var left = AssistantUi.Stack(columns, "Configure", false, 0, 16); AssistantUi.Size(left, -1, 0, 1.35f);
        var right = AssistantUi.Stack(columns, "Playground", false, 0, 16); AssistantUi.Size(right, -1, wide ? 0 : -1, 1);
        float leftWidth = wide ? (ContentWidth - 16) * 1.35f / 2.35f : ContentWidth;
        string selected = draft.features.ttsProvider;

        var service = Section("语音合成服务", ServiceAccounts.Name(selected) + " · " + Describe(selected), left);
        AssistantUi.Segmented(service, SpeechKinds.Select(ShortName).ToList(), ShortName(selected), name =>
        {
            draft.features.ttsProvider = SpeechKinds.First(k => ShortName(k) == name); ShowSpeech();
        }, 40);
        AssistantUi.Toggle(service, "朗读回复", draft.readRepliesAloud, v => draft.readRepliesAloud = v, "关闭后只显示文字回复。");
        AccountPicker(left, selected, ShowSpeech, leftWidth);

        object target = ServiceAccounts.Target(draft, selected);
        string voiceField = selected == "Doubao" ? "speaker" : "voiceName";
        var voice = Section("音色", selected == "AzureSpeech" ? "可以从在线列表选择，或直接填写音色 ID。" : "填写控制台中的音色 ID。", left);
        if (selected == "Doubao") Field(voice, target, "model");
        Field(voice, target, voiceField);
        var account = draft.serviceAccounts.Find(a => a.kind == selected && a.selected);
        if (account != null) CatalogPicker(voice, account, Convert.ToString(target.GetType().GetField(voiceField).GetValue(target)), entry =>
        {
            SetField(target, voiceField, entry.id);
            if (selected == "AzureSpeech") { SetField(draft.azureTts, "language", entry.language); SetField(draft.azureTts, "style", ""); }
            ShowSpeech();
        });
        if (selected == "AzureSpeech")
        {
            var mouth = Section("口型与同步", "小句语音和嘴部动画准备好后再播放；关闭可恢复普通口型。", left);
            Field(mouth, draft.azureTts, "continuousLipSync");
            AssistantUi.Label(mouth, "test · 嘴部", 14, AssistantUi.Ink, -1, true); ObjectFields(mouth, draft.testMouth);
            AssistantUi.Label(mouth, "girl · 嘴部", 14, AssistantUi.Ink, -1, true); ObjectFields(mouth, draft.girlMouth);
            var row = AssistantUi.Row(mouth, "Actions", 10);
            AssistantUi.Button(row, "验证口型 · 固定语句", () => { speechText = "爸爸妈妈，乌云与圆月。今天是2026年，Hello, nice to meet you."; Preview(); }, ButtonKind.Secondary, 38);
            AssistantUi.Caption(mouth, "最近口型状态：" + (controller != null ? controller.MouthStatus : "—"), 12);
        }
        ObjectFields(Foldout("声音参数", "语速、音量、分段等。", left), target, ServiceAccounts.Fields(selected).Concat(new[] { voiceField, "model", "continuousLipSync" }).ToArray());
        ObjectFields(Foldout("音频缓冲", "网络不稳定时可以适当加大缓冲。", left), draft.playback);

        var play = Section("试听", "使用当前草稿合成声音，不会改变正在使用的配置，也不会写入聊天记录。", right);
        AssistantUi.Input(play, speechText, v => speechText = v, false, true, false, "输入要试听的文字");
        var buttons = AssistantUi.Row(play, "Buttons", 10);
        AssistantUi.Button(buttons, "试听声音", Preview, ButtonKind.Primary, 40, 120);
        AssistantUi.Button(buttons, "停止", () => { previewCts?.Cancel(); SetStatus("试听已停止"); }, ButtonKind.Secondary, 40, 80);
        AssistantUi.Caption(play, "试听会打断当前正在播放的回复。", 12);
        if (selected == "AzureSpeech" && account != null)
        {
            var current = catalog.Cached(account).Find(v => v.id == draft.azureTts.voiceName);
            if (current != null && current.styles.Length > 0)
            {
                var styles = Section("说话风格", "此音色支持的语气风格。", right);
                Grid(styles, new[] { "" }.Concat(current.styles), (cell, style) =>
                    AssistantUi.Button(cell, style.Length == 0 ? "默认" : style, () => { SetField(draft.azureTts, "style", style); ShowSpeech(); },
                        draft.azureTts.style == style ? ButtonKind.Primary : ButtonKind.Secondary, 36), 3, (wide ? ContentWidth - leftWidth - 16 : ContentWidth) - 44, 110);
            }
        }
    }

    // ---------- 通用设置 ----------

    private void ShowGeneral()
    {
        BeginPage("通用设置", "general", "数字人形象、交互方式与诊断选项。", "general");
        reopen = ShowGeneral;
        var look = Section("数字人形象", "在场景中已安装的形象之间切换，立即生效。");
        var row = AssistantUi.Row(look, "Appearance", 12);
        var switcher = owner.modelSwitcher != null ? owner.modelSwitcher : FindObjectOfType<CharacterModelSwitcher>(true);
        var name = AssistantUi.Label(row, "当前形象：" + (switcher != null ? switcher.GetCurrentModelName() : "—"), 15);
        name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        AssistantUi.Button(row, "切换形象", () => { owner.ToggleModel(); ShowGeneral(); }, ButtonKind.Secondary, 38);
        var interaction = Section("交互", "说话方式与动画参数。");
        AssistantUi.Toggle(interaction, "朗读回复", draft.readRepliesAloud, v => draft.readRepliesAloud = v, "关闭后只显示文字回复。");
        ObjectFields(interaction, draft.features, new[] { "ttsProvider" });
        var diagnostics = Section("请求诊断", "排查问题时使用。");
        Field(diagnostics, draft.deepSeek, "logRequestPayloadToConsole"); Field(diagnostics, draft.deepSeek, "requestPayloadLogChunkCharacters");
        AssistantUi.Caption(content, "配置迁移时会保留原文件备份；“重新读取”不会立即改变运行中的配置。", 12).color = AssistantUi.Faint;
    }

    // ---------- fields ----------

    private void ObjectFields(Transform parent, object target, string[] exclude = null)
    {
        foreach (var field in target.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (exclude == null || !exclude.Contains(field.Name)) Field(parent, target, field.Name);
    }
    private void Field(Transform parent, object target, string name)
    {
        var field = target.GetType().GetField(name); if (field == null) return;
        string key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(target) + ":" + name;
        if (field.FieldType == typeof(bool))
        { AssistantUi.Toggle(parent, Label(name), (bool)field.GetValue(target), value => field.SetValue(target, value)); return; }
        var group = FieldGroup(parent, Label(name));
        var current = Convert.ToString(field.GetValue(target), CultureInfo.InvariantCulture);
        var input = AssistantUi.Input(group, raw.TryGetValue(key, out var original) ? original : current, value =>
        {
            raw[key] = value;
            object parsed = value;
            if (field.FieldType == typeof(int))
            {
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) { invalid[key] = Label(name); SetStatus("请输入有效整数：" + Label(name), UiTone.Danger); return; }
                parsed = n;
            }
            else if (field.FieldType == typeof(float))
            {
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || float.IsNaN(n) || float.IsInfinity(n)) { invalid[key] = Label(name); SetStatus("请输入有效数值：" + Label(name), UiTone.Danger); return; }
                parsed = n;
            }
            field.SetValue(target, parsed);
            if (invalid.Remove(key) && invalid.Count == 0) SetStatus(IdleStatus);
        }, false, name.Contains("Prompt") || name == "voiceInstruction");
        input.name = "Input_" + name;
    }
    private void SetField(object target, string name, object value)
    {
        target.GetType().GetField(name).SetValue(target, value);
        string key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(target) + ":" + name;
        raw.Remove(key); invalid.Remove(key);
    }
    private void ForgetFields(object target)
    {
        string prefix = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(target) + ":";
        foreach (string key in raw.Keys.Where(k => k.StartsWith(prefix)).ToList()) { raw.Remove(key); invalid.Remove(key); }
    }
    private void CatalogPicker(Transform parent, ServiceAccount account, string current, Action<CatalogEntry> select)
    {
        if (!ServiceCatalog.Supported(account.kind)) { AssistantUi.Caption(parent, "此服务不提供在线列表，请填写控制台中的模型或音色 ID。", 13); return; }
        string search = ""; int limit = 8; var data = catalog.Cached(account);
        var head = AssistantUi.Row(parent, "CatalogHeader", 10);
        var searchInput = AssistantUi.Input(head, "", null, false, false, false, "搜索名称、ID 或语言…"); AssistantUi.Size(searchInput, 40, -1, 1);
        Action<bool> fetch = null;
        AssistantUi.Button(head, "刷新列表", () => fetch(true), ButtonKind.Secondary, 40, 96);
        var feedback = AssistantUi.Caption(parent, "", 12);
        var list = AssistantUi.Stack(parent, "Catalog", false, 0, 4);
        Action render = null;
        render = () =>
        {
            if (list == null) return;
            AssistantUi.Clear(list);
            var filtered = data.Where(item => ((item.name ?? "") + item.id + item.language).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            foreach (var item in filtered.Take(limit))
            {
                var entry = item; bool on = item.id == current;
                var b = AssistantUi.Button(list, string.IsNullOrEmpty(item.language) ? item.id : item.name + "  ·  " + item.language + "  ·  " + item.id, () => select(entry), on ? ButtonKind.Soft : ButtonKind.Ghost, 38);
                var label = b.GetComponentInChildren<Text>(); label.alignment = TextAnchor.MiddleLeft; label.fontSize = 14; label.horizontalOverflow = HorizontalWrapMode.Wrap;
                if (on) label.fontStyle = FontStyle.Bold;
            }
            if (filtered.Count > limit) AssistantUi.Link(list, "显示更多（共 " + filtered.Count + " 项）", () => { limit += 24; render(); });
        };
        searchInput.onValueChanged.AddListener(value => { search = value; limit = 8; render(); });
        fetch = async force =>
        {
            listCts?.Cancel(); var cancellation = new CancellationTokenSource(); listCts = cancellation; int generation = pageGeneration;
            feedback.text = "正在加载在线列表…";
            try
            {
                var result = await catalog.FetchAsync(account.Copy(), force, cancellation.Token);
                if (this == null || generation != pageGeneration || cancellation.IsCancellationRequested || feedback == null) return;
                data = result; feedback.text = data.Count == 0 ? "列表为空，可以手动填写 ID。" : "已加载 " + data.Count + " 项；也可以手动填写 ID。"; render();
            }
            catch (Exception ex)
            {
                if (this != null && generation == pageGeneration && feedback != null && !cancellation.IsCancellationRequested)
                    feedback.text = ex is InvalidOperationException ? ex.Message : "加载超时或网络不可用，保留缓存与手动输入。";
            }
            finally { if (ReferenceEquals(listCts, cancellation)) listCts = null; cancellation.Dispose(); }
        };
        render();
        if (ServiceAccounts.Configured(account)) fetch(false); else feedback.text = "请先在服务来源中完善此账号。";
    }

    // ---------- actions ----------

    private async void Preview()
    {
        if (previewRunning || invalid.Count > 0) { SetStatus("请先停止试听或修正无效数值。", UiTone.Warning); return; }
        if (controller == null) { SetStatus("当前场景没有语音控制器，无法试听。", UiTone.Warning); return; }
        var cancellation = new CancellationTokenSource(); previewCts = cancellation; previewRunning = true; int generation = pageGeneration;
        SetStatus("正在合成与播放…", UiTone.Accent);
        try
        {
            await controller.PreviewSpeechAsync(draft, speechText, cancellation.Token);
            if (generation == pageGeneration) SetStatus(cancellation.IsCancellationRequested ? "试听已停止" : "试听完成 · " + controller.MouthStatus + "；保存后正式启用。", UiTone.Success);
        }
        catch (Exception ex)
        {
            if (this != null && generation == pageGeneration)
                SetStatus(cancellation.IsCancellationRequested ? "试听已停止" : ex is InvalidOperationException ? ServiceConnectionTester.Describe(ex) : "试听失败，请检查账号、音色与网络。", cancellation.IsCancellationRequested ? UiTone.Neutral : UiTone.Danger);
        }
        finally { previewRunning = false; if (ReferenceEquals(previewCts, cancellation)) previewCts = null; cancellation.Dispose(); }
    }
    private void Confirm(string title, string message, Action yes, string yesText, Action secondary = null, string secondaryText = "")
    {
        if (confirmation != null) AssistantUi.Remove(confirmation.gameObject);
        confirmation = AssistantUi.Rect(overlay, "Confirmation"); AssistantUi.Fill(confirmation); confirmation.gameObject.AddComponent<Image>().color = new Color(.07f, .1f, .08f, .28f);
        var box = AssistantUi.Stack(confirmation, "Dialog", false, 0, 10); AssistantUi.Pad(box, 28, 28, 26, 24);
        AssistantUi.Box(box, AssistantUi.Surface, 18); AssistantUi.Outline(box, AssistantUi.Border, 18);
        box.anchorMin = box.anchorMax = box.pivot = new Vector2(.5f, .5f); box.sizeDelta = new Vector2(540, 0);
        box.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        AssistantUi.Shadow(box, 18, 30, .2f, 10);
        AssistantUi.Title(box, title, 20); AssistantUi.Label(box, message, 15, AssistantUi.Muted).alignment = TextAnchor.UpperLeft;
        AssistantUi.Spacer(box, 8);
        var row = AssistantUi.Row(box, "Buttons", 10, TextAnchor.MiddleRight);
        AssistantUi.Button(row, "继续编辑", () => AssistantUi.Remove(confirmation.gameObject), ButtonKind.Ghost, 40);
        if (secondary != null) AssistantUi.Button(row, secondaryText, () => { AssistantUi.Remove(confirmation.gameObject); secondary(); }, ButtonKind.Secondary, 40);
        AssistantUi.Button(row, yesText, () => { AssistantUi.Remove(confirmation.gameObject); yes(); }, ButtonKind.Primary, 40);
    }
    private void CancelPageOperations() { listCts?.Cancel(); previewCts?.Cancel(); }
    private void CancelOperations()
    {
        CancelPageOperations(); testCancel.Cancel();
        foreach (var record in tests.Values.Where(r => r.running).ToList()) record.running = false;
    }
    private void Resize()
    {
        if (window == null) return;
        var bounds = canvas.rect.size;
        window.sizeDelta = new Vector2(Mathf.Min(1320, bounds.x - 80), Mathf.Min(840, bounds.y - 72));
    }
    private void Update()
    {
        Resize();
        if (!IsOpen) return;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (confirmation != null && confirmation.gameObject.activeSelf) AssistantUi.Remove(confirmation.gameObject);
            else RequestClose();
        }
        if (Time.unscaledTime >= statusRefreshAt) { statusRefreshAt = Time.unscaledTime + .3f; RenderStatus(); Reflow(); }
    }
    // Grids are laid out for the width at build time; rebuild the page after a large window resize.
    private float builtWidth;
    private void Reflow()
    {
        float width = ContentWidth;
        if (builtWidth <= 0) { builtWidth = width; return; }
        if (Mathf.Abs(width - builtWidth) < 60 || confirmation != null) return;
        var events = UnityEngine.EventSystems.EventSystem.current;
        var focused = events != null ? events.currentSelectedGameObject : null;
        if (focused != null && focused.GetComponent<InputField>() != null) return;
        builtWidth = width; reopen?.Invoke();
    }
    private void OnDestroy() { CancelOperations(); testCancel.Dispose(); catalog.Dispose(); ModalOpen = false; }
    public void NavigateLegacy(string name)
    {
        if (!IsOpen) Open();
        switch (name)
        {
            case "Page_DeepSeek": ShowChat(); break;
            case "Page_TtsAzure": draft.features.ttsProvider = "AzureSpeech"; ShowSpeech(); break;
            case "Page_TtsDoubao": draft.features.ttsProvider = "Doubao"; ShowSpeech(); break;
            case "Page_TtsOnline": draft.features.ttsProvider = "XunfeiOnline"; ShowSpeech(); break;
            case "Page_TtsSuper": draft.features.ttsProvider = "XunfeiSuperSmart"; ShowSpeech(); break;
            case "Page_AudioBuffer": ShowSpeech(); break;
            case "Page_Asr": OpenModule("听觉"); break;
            case "Page_BaiduSearch": OpenModule("网络搜索"); break;
            case "Page_Memory": OpenModule("记忆"); break;
            case "Page_Knowledge": OpenModule("知识库"); break;
            case "Page_Features": ShowGeneral(); break;
            default: ShowProviders(); break;
        }
    }
}
