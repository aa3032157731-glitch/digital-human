using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

// Floating conversation panel: chat bubbles, history, knowledge base, composer and microphone level.
public sealed class AssistantDashboard : MonoBehaviour
{
    private const int MaxMessages = 80;
    private const float BubblePadX = 14, BubblePadY = 9;
    private static readonly string[] Tabs = { "对话", "历史", "知识库" };
    private VoiceChatController controller;
    private Action openSettings;
    private RectTransform canvas, root, body, footer, tabsHost, chatView, historyView, knowledgeView, composer, messages, emptyState, historyList, exitButton;
    private readonly Vector3[] corners = new Vector3[4];
    private ScrollRect conversationScroll;
    private Text stateText, sessionText, statusText, micText, knowledgeText, collapseLabel, deleteLabel;
    private Image stateDot, statusDot, micDot, meter;
    private ChatTextInputField input;
    private Toggle readAloud;
    private UiSwitch readAloudSwitch;
    private Button send, stop;
    private string tab = "对话";
    private bool collapsed, confirmDelete;
    private float level, refreshAt, confirmUntil, lastWidth, lastScale;
    private sealed class Bubble { public RectTransform row, box; public Text text, note; public string role, content, status; }
    private readonly List<Bubble> bubbles = new List<Bubble>();

    public void Build(Transform surface, VoiceChatController source, Action openSettings)
    {
        controller = source; canvas = (RectTransform)surface; this.openSettings = openSettings;
        var sceneButton = FindObjectsOfType<Button>(true).FirstOrDefault(b => b.GetComponentsInChildren<Text>(true).Any(t => t.text.Contains("切换场景")));
        var old = FindObjectOfType<VoiceChatMemoryPanel>(true);
        if (old != null) old.gameObject.SetActive(false);
        var indicator = FindObjectOfType<VoiceInputVolumeIndicator>(true);
        if (indicator != null) indicator.gameObject.SetActive(false);

        root = AssistantUi.Stack(surface, "AssistantDashboard", false, 0, 14); AssistantUi.Pad(root, 20, 20, 18, 16);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(1, 1); root.anchoredPosition = new Vector2(-28, -28);
        AssistantUi.Box(root, AssistantUi.Alpha(Color.white, .97f), 20); AssistantUi.Outline(root, AssistantUi.Border, 20);
        AssistantUi.Shadow(root, 20, 34, .2f, 10);
        BuildHeader();
        body = AssistantUi.Stack(root, "Body", false, 0, 12); body.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        tabsHost = AssistantUi.Stack(body, "Tabs", false, 0, 0);
        BuildChat(); BuildHistory(); BuildKnowledge(); BuildComposer();
        BuildFooter(sceneButton);
        RestyleSceneButtons();
        ShowTab("对话"); Resize(); Refresh();
    }

    // ---------- structure ----------

    private void BuildHeader()
    {
        var header = AssistantUi.Row(root, "Header", 12); AssistantUi.Size(header, 44);
        var avatar = AssistantUi.Rect(header, "Avatar"); AssistantUi.Size(avatar, 40, 40); AssistantUi.Box(avatar, AssistantUi.AccentSoft, 20);
        var initials = AssistantUi.Label(avatar, "AI", 14, AssistantUi.AccentInk, -1, true); AssistantUi.Fill(initials.rectTransform); initials.alignment = TextAnchor.MiddleCenter;
        var names = AssistantUi.Stack(header, "Names", false, 0, 1); names.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        AssistantUi.Label(names, "对话助手", 17, AssistantUi.Ink, 23, true);
        var state = AssistantUi.Row(names, "State", 6); AssistantUi.Size(state, 18);
        stateDot = AssistantUi.Dot(state, AssistantUi.Success, 7).GetComponent<Image>();
        stateText = AssistantUi.Label(state, "在线", 12, AssistantUi.Muted); stateText.horizontalOverflow = HorizontalWrapMode.Overflow;
        AssistantUi.Button(header, "设置", () => openSettings?.Invoke(), ButtonKind.Ghost, 34, 56);
        collapseLabel = AssistantUi.Button(header, "收起", ToggleCollapsed, ButtonKind.Ghost, 34, 56).GetComponentInChildren<Text>();
    }

    private void BuildChat()
    {
        chatView = AssistantUi.Stack(body, "ChatView", false, 0, 6); chatView.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        var toolbar = AssistantUi.Row(chatView, "Toolbar", 4); AssistantUi.Size(toolbar, 28);
        sessionText = AssistantUi.Label(toolbar, "当前会话", 12, AssistantUi.Faint, 28);
        sessionText.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1; sessionText.verticalOverflow = VerticalWrapMode.Truncate;
        AssistantUi.Link(toolbar, "新对话", NewConversation);
        AssistantUi.Link(toolbar, "清空", () => { controller.ClearConversationHistory(); Refresh(); }, AssistantUi.Muted);
        messages = AssistantUi.Scroll(chatView, "Conversation", out conversationScroll);
        conversationScroll.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        var layout = messages.GetComponent<VerticalLayoutGroup>(); layout.spacing = 10; layout.padding = new RectOffset(2, 10, 6, 10);

        emptyState = AssistantUi.Stack(conversationScroll.transform, "Empty", false, 0, 8);
        emptyState.anchorMin = new Vector2(0, .5f); emptyState.anchorMax = new Vector2(1, .5f); emptyState.pivot = new Vector2(.5f, .5f);
        emptyState.sizeDelta = new Vector2(-40, 150);
        emptyState.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
        emptyState.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
        var mark = AssistantUi.Rect(emptyState, "Mark"); AssistantUi.Size(mark, 52, 52); AssistantUi.Box(mark, AssistantUi.AccentSoft, 26);
        var markText = AssistantUi.Label(mark, "AI", 17, AssistantUi.AccentInk, -1, true); AssistantUi.Fill(markText.rectTransform); markText.alignment = TextAnchor.MiddleCenter;
        AssistantUi.Label(emptyState, "你好，有什么可以帮你？", 16, AssistantUi.Ink, -1, true).alignment = TextAnchor.MiddleCenter;
        var hint = AssistantUi.Label(emptyState, "按住空格说话，或在下方输入文字。", 13, AssistantUi.Muted); hint.alignment = TextAnchor.MiddleCenter;
    }

    private void BuildHistory()
    {
        historyView = AssistantUi.Stack(body, "HistoryView", false, 0, 10); historyView.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        var actions = AssistantUi.Row(historyView, "Actions", 8);
        AssistantUi.Button(actions, "新对话", NewConversation, ButtonKind.Soft, 34, 80);
        AssistantUi.Button(actions, "刷新", RefreshHistory, ButtonKind.Secondary, 34, 64);
        AssistantUi.Flexible(actions);
        deleteLabel = AssistantUi.Button(actions, "删除全部", DeleteAll, ButtonKind.Danger, 34, 104).GetComponentInChildren<Text>();
        historyList = AssistantUi.Scroll(historyView, "Sessions", out var historyScroll);
        historyScroll.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        historyList.GetComponent<VerticalLayoutGroup>().spacing = 8; historyList.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(0, 10, 0, 4);
    }

    private void BuildKnowledge()
    {
        knowledgeView = AssistantUi.Stack(body, "KnowledgeView", false, 0, 10); knowledgeView.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        var actions = AssistantUi.Row(knowledgeView, "Actions", 8);
        AssistantUi.Button(actions, "刷新知识库", () => controller.RefreshKnowledgeBase(), ButtonKind.Soft, 34, 104);
        AssistantUi.Button(actions, "打开目录", () => Application.OpenURL(new Uri(controller.GetKnowledgeBaseDirectoryPath()).AbsoluteUri), ButtonKind.Secondary, 34, 88);
        var list = AssistantUi.Scroll(knowledgeView, "Files", out var scroll); scroll.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        list.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(0, 10, 0, 4);
        knowledgeText = AssistantUi.Label(list, "", 13, AssistantUi.Muted); knowledgeText.alignment = TextAnchor.UpperLeft;
    }

    private void BuildComposer()
    {
        composer = AssistantUi.Stack(body, "Composer", false, 0, 8);
        input = (ChatTextInputField)AssistantUi.Input(composer, "", null, false, true, true, "输入消息，Enter 发送 · Shift+Enter 换行");
        AssistantUi.Size(input, 84); input.characterLimit = 16000; input.SubmitMessage = Send;
        var actions = AssistantUi.Row(composer, "Actions", 8); AssistantUi.Size(actions, 36);
        var aloud = AssistantUi.Row(actions, "ReadAloud", 8); aloud.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0);
        readAloud = aloud.gameObject.AddComponent<Toggle>(); readAloud.transition = Selectable.Transition.None;
        readAloudSwitch = AssistantUi.Switch(aloud, readAloud); readAloud.targetGraphic = readAloudSwitch.track;
        AssistantUi.Label(aloud, "朗读回复", 13, AssistantUi.Muted).horizontalOverflow = HorizontalWrapMode.Overflow;
        readAloud.SetIsOnWithoutNotify(controller.readRepliesAloud); readAloudSwitch.Snap();
        readAloud.onValueChanged.AddListener(controller.SetReadRepliesAloud);
        AssistantUi.Flexible(actions);
        stop = AssistantUi.Button(actions, "停止", () => controller.InterruptAssistantOutput(), ButtonKind.Secondary, 34, 64);
        send = AssistantUi.Button(actions, "发送", Send, ButtonKind.Primary, 34, 76);
        var status = AssistantUi.Row(body, "Status", 7);
        status.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
        // Fixed-size holder keeps the dot level with the first line when the status wraps.
        var dotHolder = AssistantUi.Rect(status, "DotHolder"); AssistantUi.Size(dotHolder, 16, 7);
        var dot = AssistantUi.Rect(dotHolder, "Dot"); dot.anchorMin = dot.anchorMax = dot.pivot = new Vector2(0, 1);
        dot.anchoredPosition = new Vector2(0, -5); dot.sizeDelta = new Vector2(7, 7);
        statusDot = AssistantUi.Box(dot, AssistantUi.Faint, 3.5f); statusDot.raycastTarget = false;
        statusText = AssistantUi.Label(status, "就绪", 12, AssistantUi.Muted); statusText.alignment = TextAnchor.UpperLeft;
        statusText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
    }

    private void BuildFooter(Button sceneButton)
    {
        footer = AssistantUi.Row(root, "Footer", 10); AssistantUi.Size(footer, 34);
        micDot = AssistantUi.Dot(footer, AssistantUi.Faint, 8).GetComponent<Image>();
        micText = AssistantUi.Label(footer, "按住空格说话", 12, AssistantUi.Muted); micText.horizontalOverflow = HorizontalWrapMode.Overflow;
        var track = AssistantUi.Rect(footer, "InputLevel"); AssistantUi.Size(track, 6, -1, 1); AssistantUi.Box(track, AssistantUi.Subtle, 3).raycastTarget = false;
        var fill = AssistantUi.Rect(track, "Level"); AssistantUi.Fill(fill); fill.anchorMax = new Vector2(.02f, 1);
        meter = AssistantUi.Box(fill, AssistantUi.Accent, 3); meter.raycastTarget = false;
        if (sceneButton != null)
        {
            sceneButton.transform.SetParent(footer, false); sceneButton.gameObject.SetActive(true);
            AssistantUi.Restyle(sceneButton, ButtonKind.Secondary, 13); AssistantUi.Size(sceneButton, 32, 88);
        }
    }

    // Other scene-level buttons (exit, positioning, model switch) get the same look as the panel.
    private void RestyleSceneButtons()
    {
        foreach (var button in FindObjectsOfType<Button>(true))
        {
            if (button.transform.IsChildOf(canvas)) continue;
            var label = button.GetComponentInChildren<Text>(true);
            if (label == null) continue;
            string text = label.text.Trim();
            if (text == "退出" || text == "定位" || text.StartsWith("切换到")) AssistantUi.Restyle(button, ButtonKind.Secondary, 15);
            if (text == "退出") exitButton = (RectTransform)button.transform;
        }
    }

    // ---------- behaviour ----------

    private void ShowTab(string name)
    {
        tab = name;
        AssistantUi.Clear(tabsHost);
        AssistantUi.Segmented(tabsHost, Tabs, tab, ShowTab, 36);
        chatView.gameObject.SetActive(tab == "对话"); composer.gameObject.SetActive(tab == "对话");
        historyView.gameObject.SetActive(tab == "历史"); knowledgeView.gameObject.SetActive(tab == "知识库");
        if (tab == "历史") RefreshHistory();
        if (tab == "对话") { lastWidth = 0; Refresh(); }
        refreshAt = 0;
    }

    private void ToggleCollapsed()
    {
        collapsed = !collapsed;
        body.gameObject.SetActive(!collapsed); footer.gameObject.SetActive(!collapsed);
        collapseLabel.text = collapsed ? "展开" : "收起"; Resize();
    }

    private void Resize()
    {
        var size = canvas.rect.size;
        float width = Mathf.Clamp(size.x * .28f, 380, 470);
        float bottom = Mathf.Max(28, ReservedBottom(width));
        root.sizeDelta = new Vector2(width, collapsed ? 78 : Mathf.Max(420, size.y - 28 - bottom));
    }

    // The scene's exit button sits in the bottom-right corner; stop the panel above it instead of covering it.
    private float ReservedBottom(float panelWidth)
    {
        if (exitButton == null || !exitButton.gameObject.activeInHierarchy) return 0;
        var exitCanvas = exitButton.GetComponentInParent<Canvas>(); var ownCanvas = canvas.GetComponent<Canvas>();
        if (exitCanvas == null || ownCanvas == null) return 0;
        var exitCamera = exitCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : exitCanvas.worldCamera;
        var ownCamera = ownCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : ownCanvas.worldCamera;
        exitButton.GetWorldCorners(corners);
        var topLeft = RectTransformUtility.WorldToScreenPoint(exitCamera, corners[1]);
        var bottomRight = RectTransformUtility.WorldToScreenPoint(exitCamera, corners[3]);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, topLeft, ownCamera, out var top)) return 0;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, bottomRight, ownCamera, out var right)) return 0;
        float panelLeft = canvas.rect.xMax - 28 - panelWidth;
        if (right.x < panelLeft || top.y - canvas.rect.yMin > canvas.rect.height * .3f) return 0;
        return top.y - canvas.rect.yMin + 14;
    }

    private void Send()
    {
        if (controller.SubmitTextMessage(input.text)) { input.text = ""; input.ActivateInputField(); }
    }

    private void NewConversation()
    {
        controller.StartNewConversation(); ShowTab("对话");
    }

    private void DeleteAll()
    {
        if (confirmDelete && Time.unscaledTime < confirmUntil) { controller.DeleteAllConversationHistory(); confirmDelete = false; }
        else { confirmDelete = true; confirmUntil = Time.unscaledTime + 4; }
        deleteLabel.text = confirmDelete ? "再点一次确认" : "删除全部";
        RefreshHistory();
    }

    private void RefreshHistory()
    {
        AssistantUi.Clear(historyList);
        var sessions = controller.ListConversationSummaries();
        if (sessions.Count == 0) { AssistantUi.Caption(historyList, "暂无历史会话。", 13); return; }
        foreach (var session in sessions)
        {
            var id = session.id;
            AssistantUi.Card(historyList, string.IsNullOrWhiteSpace(session.title) ? "未命名会话" : session.title,
                FormatTime(session.updatedAtUtc) + "  ·  " + session.messageCount + " 条消息",
                () => { controller.LoadConversation(id); ShowTab("对话"); }, id == controller.activeConversationId, 64);
        }
    }

    private static string FormatTime(string utc)
    {
        if (DateTime.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time))
        {
            var local = time.ToLocalTime();
            return local.Date == DateTime.Now.Date ? "今天 " + local.ToString("HH:mm") : local.ToString("MM-dd HH:mm");
        }
        return "最近";
    }

    // Synchronises the bubbles with the controller's conversation; cheap when nothing changed.
    public void Refresh()
    {
        if (controller == null || messages == null) return;
        var lines = controller.GetConversationLines(MaxMessages);
        float width = ((RectTransform)conversationScroll.transform).rect.width - 12;
        // Glyph metrics depend on the canvas scale, so re-measure when either the width or the scale changes.
        var owner = messages.GetComponentInParent<Canvas>(); float scale = owner != null ? owner.scaleFactor : 1;
        bool resized = Mathf.Abs(width - lastWidth) > 1 || Mathf.Abs(scale - lastScale) > .001f; lastWidth = width; lastScale = scale;
        bool stick = conversationScroll.verticalNormalizedPosition < .02f || bubbles.Count == 0;
        bool changed = false;
        while (bubbles.Count > lines.Count) { AssistantUi.Remove(bubbles[bubbles.Count - 1].row.gameObject); bubbles.RemoveAt(bubbles.Count - 1); changed = true; }
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (i < bubbles.Count && bubbles[i].role != line.role) { AssistantUi.Remove(bubbles[i].row.gameObject); bubbles.RemoveAt(i); }
            if (i >= bubbles.Count || bubbles[i].role != line.role) { bubbles.Insert(i, CreateBubble(line.role, i)); changed = true; }
            var bubble = bubbles[i];
            if (bubble.content != line.text || bubble.status != line.status || resized)
            {
                bubble.content = line.text; bubble.status = line.status; bubble.text.text = line.text ?? "";
                string note = line.status == "interrupted" ? "已中断" : line.status == "streaming" ? "正在输入…" : line.status == "failed" ? "发送失败" : "";
                bubble.note.text = note; bubble.note.gameObject.SetActive(note.Length > 0);
                Measure(bubble, width); changed = true;
            }
        }
        emptyState.gameObject.SetActive(lines.Count == 0);
        if (changed)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(messages);
            if (stick) conversationScroll.verticalNormalizedPosition = 0;
        }
    }

    private Bubble CreateBubble(string role, int index)
    {
        bool user = role == "user";
        var row = AssistantUi.Stack(messages, "Message", false, 0, 4); row.SetSiblingIndex(index);
        var group = row.GetComponent<VerticalLayoutGroup>(); group.childForceExpandWidth = false; group.childAlignment = user ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
        var box = AssistantUi.Rect(row, "Bubble"); AssistantUi.Box(box, user ? AssistantUi.Accent : AssistantUi.Hex(0xF1EFEA), 16).raycastTarget = false;
        box.gameObject.AddComponent<LayoutElement>();
        var text = AssistantUi.Label(box, "", 15, user ? Color.white : AssistantUi.Ink);
        AssistantUi.Fill(text.rectTransform, BubblePadX, BubblePadY, BubblePadX, BubblePadY); text.alignment = TextAnchor.UpperLeft; text.lineSpacing = 1.15f;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var note = AssistantUi.Label(row, "", 11, AssistantUi.Faint); note.alignment = user ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
        note.horizontalOverflow = HorizontalWrapMode.Overflow; note.gameObject.SetActive(false);
        return new Bubble { row = row, box = box, text = text, note = note, role = role };
    }

    // Bubbles hug short messages and wrap long ones at ~85% of the panel width.
    private static void Measure(Bubble bubble, float width)
    {
        float max = Mathf.Max(120, width * (bubble.role == "user" ? .8f : .88f)) - BubblePadX * 2;
        var text = bubble.text; var generator = text.cachedTextGeneratorForLayout;
        var single = text.GetGenerationSettings(new Vector2(max, 0)); single.horizontalOverflow = HorizontalWrapMode.Overflow;
        float natural = generator.GetPreferredWidth(text.text, single) / text.pixelsPerUnit;
        float textWidth = Mathf.Clamp(natural + 4, 12, max);
        var wrapped = text.GetGenerationSettings(new Vector2(textWidth, 0)); wrapped.horizontalOverflow = HorizontalWrapMode.Wrap;
        float height = generator.GetPreferredHeight(text.text, wrapped) / text.pixelsPerUnit;
        var e = bubble.box.GetComponent<LayoutElement>();
        e.minWidth = e.preferredWidth = textWidth + BubblePadX * 2;
        e.minHeight = e.preferredHeight = Mathf.Max(38, height + BubblePadY * 2);
    }

    private static string KeyName(KeyCode key) => key == KeyCode.Space ? "空格" : key.ToString();

    private static UiTone StatusTone(string status)
    {
        if (string.IsNullOrEmpty(status)) return UiTone.Neutral;
        if (status.Contains("失败") || status.Contains("不可用") || status.Contains("错误") || status.Contains("无法")) return UiTone.Danger;
        if (status.Contains("正在") || status.Contains("思考") || status.Contains("生成")) return UiTone.Accent;
        if (status.Contains("完成")) return UiTone.Success;
        return UiTone.Neutral;
    }

    private void Update()
    {
        if (controller == null || root == null) return;
        Resize();
        string state; Color dot;
        if (controller.isListening) { state = "正在聆听"; dot = AssistantUi.Accent; }
        else if (controller.IsGenerating) { state = "正在思考"; dot = AssistantUi.Warning; }
        else if (controller.isSpeaking) { state = "正在说话"; dot = AssistantUi.Accent; }
        else { state = "在线"; dot = AssistantUi.Success; }
        stateText.text = state; stateDot.color = dot;
        if (collapsed) return;

        var target = controller.isListening ? Mathf.Clamp01((20 * Mathf.Log10(Mathf.Max(controller.CurrentMicrophoneLevel * 8.5f, .001f)) + 60) / 60) : 0;
        level = Mathf.Lerp(level, target, 1 - Mathf.Exp(-Time.unscaledDeltaTime * (target > level ? 65 : 18)));
        meter.rectTransform.anchorMax = new Vector2(Mathf.Max(.02f, level), 1);
        meter.color = level > .85f ? AssistantUi.Danger : level > .68f ? AssistantUi.Warning : AssistantUi.Accent;
        micDot.color = controller.isListening ? AssistantUi.Accent : AssistantUi.Faint;
        micText.text = controller.isListening ? "正在聆听…" : controller.enablePushToTalk ? "按住" + KeyName(controller.pushToTalkKey) + "说话" : "麦克风待机";

        send.interactable = !string.IsNullOrWhiteSpace(input.text);
        bool busy = controller.IsGenerating || controller.isSpeaking;
        if (stop.gameObject.activeSelf != busy) stop.gameObject.SetActive(busy);
        if (readAloud.isOn != controller.readRepliesAloud) readAloud.SetIsOnWithoutNotify(controller.readRepliesAloud);

        if (Time.unscaledTime < refreshAt) return;
        refreshAt = Time.unscaledTime + .2f;
        if (confirmDelete && Time.unscaledTime > confirmUntil) { confirmDelete = false; deleteLabel.text = "删除全部"; }
        statusText.text = controller.ChatStatus; statusDot.color = AssistantUi.ToneColor(StatusTone(controller.ChatStatus));
        if (tab == "对话") { sessionText.text = controller.ActiveConversationTitle ?? "当前会话"; Refresh(); }
        if (tab == "知识库") knowledgeText.text = controller.currentKnowledgeBaseStatus + "\n\n" + controller.GetKnowledgeBaseFileListText();
    }
}
