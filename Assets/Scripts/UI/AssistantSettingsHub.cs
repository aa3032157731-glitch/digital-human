using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

public sealed class AssistantSettingsHub : MonoBehaviour
{
    public static bool ModalOpen { get; private set; }
    public bool IsOpen => overlay != null && overlay.gameObject.activeSelf;
    public RuntimeVoiceChatSettings Draft => draft;
    private AdvancedSettingsPanel owner;
    private VoiceChatController controller;
    private RectTransform canvas, overlay, window, content, confirmation;
    private Text heading, status;
    private RuntimeVoiceChatSettings draft;
    private string baseline, page = "providers", category = "聊天", speechText = "你好，我是你的数字人助手，很高兴与你见面。";
    private readonly HashSet<string> expandedSections = new HashSet<string>();
    private readonly ServiceCatalog catalog = new ServiceCatalog();
    private CancellationTokenSource listCts, previewCts, testCts;
    private int pageGeneration;
    private bool previewRunning;
    private readonly Dictionary<string, string> invalid = new Dictionary<string, string>();
    private readonly Dictionary<string, string> raw = new Dictionary<string, string>();
    private readonly Dictionary<string, string> labels = new Dictionary<string, string>();

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
        overlay.gameObject.AddComponent<Image>().color = new Color(0.04f, .10f, .15f, .48f);
        window = AssistantUi.Rect(overlay, "SettingsWindow"); window.anchorMin = window.anchorMax = window.pivot = new Vector2(.5f, .5f);
        AssistantUi.Background(window, AssistantUi.Surface);
        var sidebar = AssistantUi.Stack(window, "Navigation", false, 20, 12);
        sidebar.anchorMin = Vector2.zero; sidebar.anchorMax = new Vector2(0, 1); sidebar.pivot = Vector2.zero; sidebar.sizeDelta = new Vector2(206, 0);
        AssistantUi.Background(sidebar, Color.white);
        AssistantUi.Label(sidebar, "数字人", 27, AssistantUi.Accent, 52);
        AssistantUi.Label(sidebar, "设置", 15, AssistantUi.Muted, 26);
        AssistantUi.Button(sidebar, "服务来源", () => ShowProviders());
        AssistantUi.Button(sidebar, "机体模块", ShowModules);
        AssistantUi.Button(sidebar, "通用设置", ShowGeneral);
        AssistantUi.Label(sidebar, "配置服务 · 选择能力", 13, AssistantUi.Muted, 54);
        heading = AssistantUi.Label(window, "服务来源", 28, AssistantUi.Ink);
        heading.rectTransform.anchorMin = new Vector2(0, 1); heading.rectTransform.anchorMax = Vector2.one; heading.rectTransform.pivot = new Vector2(.5f, 1);
        heading.rectTransform.offsetMin = new Vector2(238, -78); heading.rectTransform.offsetMax = new Vector2(-140, -24);
        var close = AssistantUi.Button(window, "关闭", RequestClose); var closeRect = (RectTransform)close.transform;
        closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = Vector2.one; closeRect.anchoredPosition = new Vector2(-24, -26); closeRect.sizeDelta = new Vector2(88, 42);
        content = AssistantUi.Scroll(window, "SettingsContent", out var scroll);
        AssistantUi.Fill((RectTransform)scroll.transform, 236, 106, 28, 94);
        var footer = AssistantUi.Stack(window, "Footer", true, 0, 12); footer.anchorMin = Vector2.zero; footer.anchorMax = new Vector2(1, 0); footer.pivot = Vector2.zero;
        footer.offsetMin = new Vector2(238, 22); footer.offsetMax = new Vector2(-28, 74);
        status = AssistantUi.Label(footer, "修改后点击保存并应用", 14, AssistantUi.Muted, 48); AssistantUi.Size(status, 48, -1, 1);
        AssistantUi.Size(AssistantUi.Button(footer, "重新读取", Reload), 46, 104);
        AssistantUi.Size(AssistantUi.Button(footer, "保存并应用", () => Save(), true), 46, 146);
        Resize(); overlay.gameObject.SetActive(false);
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
            case "uri": return "Base URL / 完整请求地址";
            case "resourceEndpoint": return "资源终结点（可选，用于音色列表）";
            case "displayName": return "配置名称";
            case "model": return "模型名称 / 接入点 ID";
            case "voiceName": case "speaker": return "音色 ID";
            case "continuousLipSync": return "连续口型（按小句准备）";
            case "mouthIntensity": return "嘴部强度（0–1.5）";
            case "jawAmplitude": return "下颌幅度（0–1.2）";
            case "lipSyncOffsetMs": return "口型提前量（毫秒，-200–200）";
            case "systemPrompt": return "系统提示词";
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
        }
        return labels.TryGetValue(name.ToLowerInvariant(), out var value) ? value : name;
    }
    public void Open()
    {
        if (IsOpen) return;
        draft = controller != null ? controller.CaptureRuntimeSettings() : VoiceChatRuntimeConfigStore.LoadSettings() ?? new RuntimeVoiceChatSettings();
        draft.EnsureDefaults(); baseline = JsonConvert.SerializeObject(draft); raw.Clear(); invalid.Clear();
        overlay.gameObject.SetActive(true); ModalOpen = true; ShowProviders(); SetStatus("修改后点击保存并应用");
        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
    }
    private bool Dirty => invalid.Count > 0 || JsonConvert.SerializeObject(draft) != baseline;
    public void RequestClose()
    {
        if (!IsOpen) return;
        if (!Dirty) { CloseNow(); return; }
        Confirm("有尚未保存的修改", "保存后生效；放弃修改将恢复打开设置时的配置。", () => { if (Save()) CloseNow(); }, "保存并关闭", CloseNow, "放弃修改");
    }
    private void CloseNow()
    {
        CancelOperations(); overlay.gameObject.SetActive(false); ModalOpen = false;
        if (confirmation != null) AssistantUi.Remove(confirmation.gameObject);
    }
    public void Reload()
    {
        Confirm("重新读取配置？", "当前未保存的修改将被丢弃。", () =>
        {
            var loaded = VoiceChatRuntimeConfigStore.LoadSettings();
            if (loaded == null) { SetStatus("配置读取失败，当前草稿已保留。"); return; }
            draft = loaded; baseline = JsonConvert.SerializeObject(draft); raw.Clear(); invalid.Clear(); ShowProviders(); SetStatus("已读取；点击保存并应用后启用。");
        }, "重新读取");
    }
    public bool Save()
    {
        if (draft == null) return false;
        if (invalid.Count > 0) { SetStatus("请修正无效数值：" + invalid.First().Value); return false; }
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
            if (!VoiceChatRuntimeConfigStore.SaveSettings(candidate)) { SetStatus("保存失败，当前运行配置未改变。"); return false; }
            CancelOperations(); controller?.InterruptAssistantOutput(); controller?.ApplyRuntimeSettings(candidate);
            draft = candidate; baseline = JsonConvert.SerializeObject(draft); raw.Clear(); invalid.Clear();
            // Rebind controls to the newly committed snapshot.
            ShowModules(); SetStatus("已保存并应用，下次启动自动恢复。"); return true;
        }
        catch (Exception ex) { SetStatus(ex is InvalidOperationException ? ex.Message : "保存失败，请检查配置。"); return false; }
    }
    private void BeginPage(string title, string id)
    {
        CancelOperations(); pageGeneration++; page = id; heading.text = title; AssistantUi.Clear(content);
        ((ScrollRect)content.parent.parent.GetComponent<ScrollRect>()).verticalNormalizedPosition = 1;
    }
    private void SetStatus(string message) { if (status != null) status.text = message; }
    private void Back(Action action) => AssistantUi.Button(content, "‹  返回", action, false, 36);
    private RectTransform Section(string title, Transform parent = null)
    {
        var box = AssistantUi.Stack(parent ?? content, title, false, 18, 10); AssistantUi.Background(box, new Color32(237, 242, 245, 255));
        AssistantUi.Label(box, title, 22, AssistantUi.Ink, 34); return box;
    }
    private RectTransform Row(Transform parent, string name = "Cards")
    {
        var r = AssistantUi.Stack(parent, name, true, 0, 14); return r;
    }
    private RectTransform Foldout(string title, Transform parent = null)
    {
        var box = AssistantUi.Stack(parent ?? content, title, false, 16, 8); AssistantUi.Background(box, new Color32(237, 242, 245, 255));
        RectTransform fields = null;
        AssistantUi.Button(box, title + "  ›", () =>
        {
            bool visible = !fields.gameObject.activeSelf; fields.gameObject.SetActive(visible);
            if (visible) expandedSections.Add(title); else expandedSections.Remove(title);
        }, false, 40);
        fields = AssistantUi.Stack(box, "Fields", false, 0, 10); fields.gameObject.SetActive(expandedSections.Contains(title)); return fields;
    }
    private void Grid<T>(Transform parent, IEnumerable<T> values, Action<Transform, T> render, int columns = 2)
    {
        columns = Mathf.Min(columns, Mathf.Max(1, Mathf.FloorToInt((window.rect.width - 270) / 330)));
        int index = 0; RectTransform row = null;
        foreach (var item in values)
        {
            if (index++ % columns == 0) row = Row(parent);
            var cell = AssistantUi.Stack(row, "Cell", false, 0, 6); AssistantUi.Size(cell, -1, 0, 1); render(cell, item);
        }
    }
    private static string Category(string kind) => ServiceAccounts.IsChat(kind) ? "聊天" : kind == "ASR" ? "语音识别" : kind == "Search" ? "网络搜索" : "语音合成";
    private void ShowProviders()
    {
        BeginPage("服务来源", "providers");
        var tip = Section("连接你的服务");
        AssistantUi.Label(tip, "在这里配置地址与密钥，再到「机体模块」选择模型和音色。", 17, AssistantUi.Muted, 40);
        var tabs = Row(content, "Categories"); AssistantUi.Size(tabs, 44);
        foreach (var item in new[] { "聊天", "语音合成", "语音识别", "网络搜索" })
        { var name = item; AssistantUi.Button(tabs, name, () => { category = name; ShowProviders(); }, category == name); }
        Grid(content, ServiceAccounts.Kinds.Where(k => Category(k) == category), (parent, kind) =>
        {
            var accounts = draft.serviceAccounts.Where(a => a.kind == kind).ToList();
            var configured = accounts.Count(ServiceAccounts.Configured);
            AssistantUi.Card(parent, ServiceAccounts.Name(kind), configured > 0 ? configured + " 个账号已配置  ·  管理服务" : "尚未配置  ·  添加账号", () => ShowAccounts(kind), configured > 0);
        }, 3);
    }
    private void ShowAccounts(string kind)
    {
        BeginPage(ServiceAccounts.Name(kind), "accounts"); Back(ShowProviders);
        AssistantUi.Button(content, "+ 添加服务账号", () => { var a = ServiceAccounts.New(kind); a.displayName += " " + (draft.serviceAccounts.Count(x => x.kind == kind) + 1); draft.serviceAccounts.Add(a); EditAccount(a); }, true);
        Grid(content, draft.serviceAccounts.Where(a => a.kind == kind), (parent, a) => AssistantUi.Card(parent, a.displayName, ServiceAccounts.Configured(a) ? "已配置  ·  点击编辑" : "待完善  ·  点击配置", () => EditAccount(a), ServiceAccounts.Configured(a)));
    }
    private void EditAccount(ServiceAccount account)
    {
        BeginPage(account.displayName, "account"); Back(() => ShowAccounts(account.kind));
        var basic = Section("基础配置");
        AssistantUi.Label(basic, "账号名称", 16, AssistantUi.Muted, 24);
        AssistantUi.Input(basic, account.displayName, v => account.displayName = v);
        foreach (var name in ServiceAccounts.Fields(account.kind).Where(n => n == "apiKey" || n == "appId" || n == "apiSecret" || n == "accessKey" || n == "region")) AccountField(basic, account, name);
        var advanced = Foldout("高级配置");
        foreach (var name in ServiceAccounts.Fields(account.kind).Where(n => n != "apiKey" && n != "appId" && n != "apiSecret" && n != "accessKey" && n != "region")) AccountField(advanced, account, name);
        AssistantUi.Label(content, "保存凭据不会自动切换正在使用的模型。", 15, AssistantUi.Muted, 34);
        AssistantUi.Button(content, "删除此账号", () =>
        {
            if (draft.modelProfiles.Any(p => p.serviceAccountId == account.id) || (account.selected && (account.kind == draft.features.ttsProvider || account.kind == "ASR" || account.kind == "Search")))
            { SetStatus("此账号仍被模块引用，请先切换模块账号或移除对应模型配置。"); return; }
            draft.serviceAccounts.Remove(account); ShowAccounts(account.kind);
        });
    }
    private void AccountField(Transform parent, ServiceAccount account, string name)
    {
        AssistantUi.Label(parent, Label(name), 16, AssistantUi.Muted, 24);
        bool secret = name == "apiKey" || name == "apiSecret" || name == "accessKey";
        var input = AssistantUi.Input(parent, account.Get(name), v => account.Set(name, v.Trim()), secret);
        if (secret) AssistantUi.Toggle(parent, "显示密钥", false, visible => { input.contentType = visible ? InputField.ContentType.Standard : InputField.ContentType.Password; input.ForceLabelUpdate(); });
    }
    private void ShowModules()
    {
        BeginPage("机体模块", "modules");
        AssistantUi.Label(content, "为数字人选择正在使用的能力", 17, AssistantUi.Muted, 40);
        var active = draft.modelProfiles.Find(p => p.id == draft.activeModelProfileId);
        Grid(content, new[] { "意识", "发声", "听觉", "网络搜索", "记忆", "知识库" }, (parent, module) =>
        {
            string description = module == "意识" ? (active?.model ?? "选择语言模型") : module == "发声" ? ServiceAccounts.Name(draft.features.ttsProvider) : module == "听觉" ? "语音识别与麦克风" : module == "网络搜索" ? "搜索服务与工具调用" : module == "记忆" ? "历史消息与持久化" : "本地资料与上下文";
            AssistantUi.Card(parent, module, description, () => OpenModule(module));
        });
    }
    private void OpenModule(string module)
    {
        if (module == "意识") { ShowChat(); return; }
        if (module == "发声") { ShowSpeech(); return; }
        BeginPage(module, "module"); Back(ShowModules);
        if (module == "听觉")
        {
            AccountPicker(content, "ASR", () => OpenModule(module));
            ObjectFields(Section("识别设置"), draft.xunfeiAsr, ServiceAccounts.Fields("ASR"));
        }
        if (module == "网络搜索")
        {
            AccountPicker(content, "Search", () => OpenModule(module));
            ObjectFields(Section("搜索设置"), draft.baiduSearch, ServiceAccounts.Fields("Search"));
            ObjectFields(Section("工具调用"), draft.agentTools);
        }
        if (module == "记忆") ObjectFields(Section("记忆设置"), draft.memory);
        if (module == "知识库") ObjectFields(Section("知识库设置"), draft.knowledge);
    }
    private void AccountPicker(Transform parent, string kind, Action redraw)
    {
        AssistantUi.Label(parent, "选择服务账号", 21, AssistantUi.Ink, 34);
        Grid(parent, draft.serviceAccounts.Where(a => a.kind == kind), (cell, a) => AssistantUi.Card(cell, a.displayName,
            ServiceAccounts.Configured(a) ? (a.selected ? "当前选择" : "可选择") : "待配置 · 点击完善", () =>
            {
                if (!ServiceAccounts.Configured(a)) { EditAccount(a); return; }
                foreach (var other in draft.serviceAccounts.Where(x => x.kind == kind)) other.selected = other == a;
                ServiceAccounts.Resolve(draft); redraw();
            }, a.selected));
        AssistantUi.Button(parent, "管理服务账号  ›", () => ShowAccounts(kind), false, 36);
    }
    private void ShowChat()
    {
        BeginPage("意识", "chat"); Back(ShowModules);
        var active = draft.modelProfiles.Find(p => p.id == draft.activeModelProfileId);
        var profiles = Section("模型配置");
        Grid(profiles, draft.modelProfiles, (parent, p) => AssistantUi.Card(parent, p.displayName, string.IsNullOrWhiteSpace(p.model) ? "待选择模型" : p.model, () => { draft.activeModelProfileId = p.id; ShowChat(); }, p == active));
        AssistantUi.Button(profiles, "+ 添加模型配置", () =>
        {
            var p = ChatModelProfile.Create(active.preset); p.serviceAccountId = active.serviceAccountId; p.displayName = "新模型配置";
            draft.modelProfiles.Add(p); draft.activeModelProfileId = p.id; ShowChat();
        });
        var sources = Section("选择聊天服务来源");
        Grid(sources, draft.serviceAccounts.Where(a => ServiceAccounts.IsChat(a.kind)), (parent, a) => AssistantUi.Card(parent, a.displayName,
            ServiceAccounts.Configured(a) ? ServiceAccounts.Name(a.kind) : "待配置 · 点击完善", () =>
            {
                if (!ServiceAccounts.Configured(a)) { EditAccount(a); return; }
                active.serviceAccountId = a.id; active.preset = a.kind; ShowChat();
            }, active.serviceAccountId == a.id));
        AssistantUi.Button(sources, "添加 / 配置服务来源  ›", ShowProviders, false, 36);
        var account = draft.serviceAccounts.Find(a => a.id == active.serviceAccountId);
        var models = Section("模型"); Field(models, active, "displayName"); Field(models, active, "model");
        if (account != null) CatalogPicker(models, account, entry => { SetField(active, "model", entry.id); ShowChat(); });
        var advanced = Foldout("模型参数");
        ObjectFields(advanced, active, new[] { "id", "serviceAccountId", "apiKey", "uri", "preset", "displayName", "model" });
        Field(advanced, draft.deepSeek, "systemPrompt"); Field(advanced, draft.deepSeek, "maxHistoryMessages");
        AssistantUi.Button(content, "测试当前模型连接", TestChat, true);
        if (draft.modelProfiles.Count > 1) AssistantUi.Button(content, "删除此模型配置", () =>
        { ForgetFields(active); draft.modelProfiles.Remove(active); draft.activeModelProfileId = draft.modelProfiles[0].id; ShowChat(); });
    }
    private void ShowSpeech()
    {
        BeginPage("发声", "speech"); Back(ShowModules);
        var row = AssistantUi.Stack(content, "SpeechColumns", window.rect.width >= 1100, 0, 14);
        var left = AssistantUi.Stack(row, "Selection", false, 16, 12); AssistantUi.Size(left, -1, 0, 1); AssistantUi.Background(left, new Color32(237, 242, 245, 255));
        var right = AssistantUi.Stack(row, "Playground", false, 16, 12); AssistantUi.Size(right, -1, 0, .8f);
        AssistantUi.Label(left, "选择语音合成服务", 22, AssistantUi.Ink, 38);
        Grid(left, new[] { "AzureSpeech", "Doubao", "XunfeiSuperSmart", "XunfeiOnline" }, (cell, kind) =>
            AssistantUi.Button(cell, ServiceAccounts.Name(kind), () => { draft.features.ttsProvider = kind; ShowSpeech(); }, draft.features.ttsProvider == kind, 50));
        string selected = draft.features.ttsProvider;
        AccountPicker(left, selected, ShowSpeech);
        object target = ServiceAccounts.Target(draft, selected);
        string voiceField = selected == "Doubao" ? "speaker" : "voiceName";
        if (selected == "Doubao") Field(left, target, "model");
        Field(left, target, voiceField);
        var account = draft.serviceAccounts.Find(a => a.kind == selected && a.selected);
        if (account != null) CatalogPicker(left, account, entry =>
        {
            SetField(target, voiceField, entry.id);
            if (selected == "AzureSpeech") { SetField(draft.azureTts, "language", entry.language); SetField(draft.azureTts, "style", ""); }
            ShowSpeech();
        });
        AssistantUi.Label(right, "语音实验平台", 24, AssistantUi.Ink, 42);
        AssistantUi.Label(right, "使用当前草稿试听，不改变已启用的配置。", 16, AssistantUi.Muted, 54);
        AssistantUi.Input(right, speechText, v => speechText = v, false, true);
        var buttons = Row(right); AssistantUi.Size(buttons, 42);
        AssistantUi.Button(buttons, "试听声音", Preview, true); AssistantUi.Button(buttons, "停止", () => { previewCts?.Cancel(); SetStatus("试听已停止"); });
        AssistantUi.Label(right, "试听将结束当前回复播放；测试内容不写入聊天记录。", 14, AssistantUi.Muted, 64);
        if (selected == "AzureSpeech" && account != null)
        {
            var voice = catalog.Cached(account).Find(v => v.id == draft.azureTts.voiceName);
            if (voice != null && voice.styles.Length > 0)
            {
                AssistantUi.Label(right, "此音色支持的风格", 18);
                Grid(right, new[] { "" }.Concat(voice.styles), (parent, style) => AssistantUi.Button(parent, style.Length == 0 ? "默认" : style, () => { SetField(draft.azureTts, "style", style); ShowSpeech(); }, draft.azureTts.style == style));
            }
        }
        var parameters = Foldout("声音参数");
        ObjectFields(parameters, target, ServiceAccounts.Fields(selected).Concat(new[] { voiceField, "model", "continuousLipSync" }).ToArray());
        if (selected == "AzureSpeech")
        {
            var mouth = Section("口型与同步"); Field(mouth, draft.azureTts, "continuousLipSync");
            AssistantUi.Label(mouth, "小句语音与嘴部动画准备好后播放；关闭可恢复原口型。", 15, AssistantUi.Muted, 44);
            AssistantUi.Label(mouth, "test · 嘴部", 17); ObjectFields(mouth, draft.testMouth);
            AssistantUi.Label(mouth, "girl · 嘴部", 17); ObjectFields(mouth, draft.girlMouth);
            AssistantUi.Button(mouth, "验证口型 · 固定语句", () => { speechText = "爸爸妈妈，乌云与圆月。今天是2026年，Hello, nice to meet you."; Preview(); });
            AssistantUi.Label(mouth, "最近口型状态：" + controller.MouthStatus, 15, AssistantUi.Muted, 40);
        }
        var buffer = Foldout("音频缓冲 · 高级设置"); ObjectFields(buffer, draft.playback);
    }
    private void ShowGeneral()
    {
        BeginPage("通用设置", "general");
        AssistantUi.Button(content, "切换数字人外观", owner.ToggleModel);
        ObjectFields(Section("交互设置"), draft.features, new[] { "ttsProvider" });
        var diagnostics = Section("请求诊断"); Field(diagnostics, draft.deepSeek, "logRequestPayloadToConsole"); Field(diagnostics, draft.deepSeek, "requestPayloadLogChunkCharacters");
        AssistantUi.Label(content, "配置迁移时会保留原文件备份；重新读取不会立即改变运行配置。", 15, AssistantUi.Muted, 60);
    }
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
        AssistantUi.Label(parent, Label(name), 16, AssistantUi.Muted, 27);
        var current = Convert.ToString(field.GetValue(target), CultureInfo.InvariantCulture);
        var input = AssistantUi.Input(parent, raw.TryGetValue(key, out var original) ? original : current, value =>
        {
            raw[key] = value;
            object parsed = value;
            if (field.FieldType == typeof(int))
            {
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) { invalid[key] = Label(name); SetStatus("请输入有效整数：" + Label(name)); return; }
                parsed = n;
            }
            else if (field.FieldType == typeof(float))
            {
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || float.IsNaN(n) || float.IsInfinity(n)) { invalid[key] = Label(name); SetStatus("请输入有效数值：" + Label(name)); return; }
                parsed = n;
            }
            field.SetValue(target, parsed); invalid.Remove(key);
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
    private void CatalogPicker(Transform parent, ServiceAccount account, Action<CatalogEntry> select)
    {
        if (!ServiceCatalog.Supported(account.kind)) { AssistantUi.Label(parent, "此服务保留手动 ID，可使用账号控制台中的模型或音色。", 14, AssistantUi.Muted, 54); return; }
        string search = ""; int limit = 12; var data = catalog.Cached(account);
        var feedback = AssistantUi.Label(parent, "", 14, AssistantUi.Muted, 42);
        var searchInput = AssistantUi.Input(parent, "", null);
        var hint = AssistantUi.Label(searchInput.transform, "搜索名称、ID 或语言…", 15, AssistantUi.Muted); AssistantUi.Fill(hint.rectTransform, 12, 7, 12, 7); searchInput.placeholder = hint;
        var list = AssistantUi.Stack(parent, "Catalog", false, 0, 8);
        Action render = null;
        render = () =>
        {
            AssistantUi.Clear(list);
            var filtered = data.Where(item => ((item.name ?? "") + item.id + item.language).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            foreach (var item in filtered.Take(limit)) AssistantUi.Button(list, string.IsNullOrEmpty(item.language) ? item.id : item.name + " · " + item.language, () => select(item), false, 42);
            if (filtered.Count > limit) AssistantUi.Button(list, "显示更多（" + filtered.Count + "）", () => { limit += 24; render(); });
        };
        searchInput.onValueChanged.AddListener(value => { search = value; limit = 12; render(); });
        Action<bool> fetch = async force =>
        {
            listCts?.Cancel(); var cancellation = new CancellationTokenSource(); listCts = cancellation; int generation = pageGeneration;
            feedback.text = "正在加载列表…";
            try
            {
                var result = await catalog.FetchAsync(account.Copy(), force, cancellation.Token);
                if (this == null || generation != pageGeneration || cancellation.IsCancellationRequested || feedback == null) return;
                data = result; feedback.text = data.Count == 0 ? "列表为空，可手动填写 ID。" : "已加载 " + data.Count + " 项；也可填写自定义 ID。"; render();
            }
            catch (Exception ex)
            {
                if (this != null && generation == pageGeneration && feedback != null && !cancellation.IsCancellationRequested)
                    feedback.text = ex is InvalidOperationException ? ex.Message : "加载超时或网络不可用，保留缓存与手动输入。";
            }
            finally { if (ReferenceEquals(listCts, cancellation)) listCts = null; cancellation.Dispose(); }
        };
        AssistantUi.Button(parent, "刷新在线列表", () => fetch(true), false, 36); render();
        if (ServiceAccounts.Configured(account)) fetch(false); else feedback.text = "请先在服务来源中配置账号。";
    }
    private async void Preview()
    {
        if (previewRunning || invalid.Count > 0) { SetStatus("请先停止试听或修正无效数值。"); return; }
        var cancellation = new CancellationTokenSource(); previewCts = cancellation; previewRunning = true; int generation = pageGeneration;
        SetStatus("正在合成与播放…");
        try { await controller.PreviewSpeechAsync(draft, speechText, cancellation.Token); if (generation == pageGeneration) SetStatus(cancellation.IsCancellationRequested ? "试听已停止" : "试听完成 · " + controller.MouthStatus + "；保存后正式启用。"); }
        catch (Exception ex) { if (this != null && generation == pageGeneration) SetStatus(cancellation.IsCancellationRequested ? "试听已停止" : ex is InvalidOperationException ? ex.Message : "试听失败，请检查账号、音色与网络。"); }
        finally { previewRunning = false; if (ReferenceEquals(previewCts, cancellation)) previewCts = null; cancellation.Dispose(); }
    }
    private async void TestChat()
    {
        if (invalid.Count > 0) { SetStatus("请先修正无效数值。"); return; }
        testCts?.Cancel(); var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20)); testCts = cancellation; int generation = pageGeneration;
        try
        {
            var snapshot = JsonConvert.DeserializeObject<RuntimeVoiceChatSettings>(JsonConvert.SerializeObject(draft)); ServiceAccounts.Resolve(snapshot);
            var profile = snapshot.modelProfiles.Find(p => p.id == snapshot.activeModelProfileId); profile.Validate(); profile.enableTools = false;
            SetStatus("正在测试连接…");
            var response = await new ChatCompletionService(profile, "请仅回复 OK。").CompleteChatAsync(new List<ChatCompletionService.ChatMessage> { new ChatCompletionService.ChatMessage("user", "连接测试，请回复 OK。") }, cancellation.Token);
            if (this != null && generation == pageGeneration) SetStatus(string.IsNullOrWhiteSpace(response.content) ? "连接成功，未返回正文。" : "连接成功，可以保存并启用。");
        }
        catch (Exception ex) { if (this != null && generation == pageGeneration) SetStatus(cancellation.IsCancellationRequested ? "测试已取消或超时。" : ex is InvalidOperationException ? ex.Message : "连接失败，请检查服务配置。"); }
        finally { if (ReferenceEquals(testCts, cancellation)) testCts = null; cancellation.Dispose(); }
    }
    private void Confirm(string title, string message, Action yes, string yesText, Action secondary = null, string secondaryText = "")
    {
        if (confirmation != null) AssistantUi.Remove(confirmation.gameObject);
        confirmation = AssistantUi.Rect(overlay, "Confirmation"); AssistantUi.Fill(confirmation); confirmation.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .25f);
        var box = AssistantUi.Stack(confirmation, "Dialog", false, 24, 14); AssistantUi.Background(box, AssistantUi.Surface);
        box.anchorMin = box.anchorMax = box.pivot = new Vector2(.5f, .5f); box.sizeDelta = new Vector2(600, 260);
        AssistantUi.Label(box, title, 25, null, 40); AssistantUi.Label(box, message, 17, AssistantUi.Muted, 64);
        var row = Row(box); AssistantUi.Size(row, 44);
        AssistantUi.Button(row, yesText, () => { AssistantUi.Remove(confirmation.gameObject); yes(); }, true);
        if (secondary != null) AssistantUi.Button(row, secondaryText, () => { AssistantUi.Remove(confirmation.gameObject); secondary(); });
        AssistantUi.Button(row, "继续编辑", () => AssistantUi.Remove(confirmation.gameObject));
    }
    private void CancelOperations() { listCts?.Cancel(); testCts?.Cancel(); previewCts?.Cancel(); }
    private void Resize()
    {
        if (window == null) return;
        var bounds = canvas.rect.size;
        window.sizeDelta = new Vector2(Mathf.Min(1440, bounds.x - 64), Mathf.Min(900, bounds.y - 64));
    }
    private void Update()
    {
        Resize();
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            if (confirmation != null && confirmation.gameObject.activeSelf) AssistantUi.Remove(confirmation.gameObject);
            else RequestClose();
        }
    }
    private void OnDestroy() { CancelOperations(); catalog.Dispose(); ModalOpen = false; }
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
