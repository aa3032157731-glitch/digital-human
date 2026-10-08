using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public sealed class AssistantDashboard : MonoBehaviour
{
    private VoiceChatController controller;
    private RectTransform root, body, history, knowledge;
    private Text conversation, state, knowledgeText, meterLabel, collapseLabel;
    private ScrollRect scroll;
    private ChatTextInputField input;
    private Toggle readAloud;
    private Image meter, peak;
    private Button send, stop;
    private bool collapsed, confirmDelete;
    private float level, peakValue, refreshAt, confirmUntil;
    private string lastConversation;
    private Transform canvas;

    public void Build(Transform surface, VoiceChatController source, Action openSettings)
    {
        controller = source; canvas = surface;
        var sceneButton = FindObjectsOfType<Button>(true).FirstOrDefault(b => b.GetComponentsInChildren<Text>(true).Any(t => t.text.Contains("切换场景")));
        // Existing scene-switch callbacks stay bound when the control moves into the new panel.
        if (sceneButton != null) sceneButton.transform.SetParent(surface, false);
        var old = FindObjectOfType<VoiceChatMemoryPanel>(true);
        if (old != null) old.gameObject.SetActive(false);
        var indicator = FindObjectOfType<VoiceInputVolumeIndicator>(true);
        if (indicator != null) indicator.gameObject.SetActive(false);
        root = AssistantUi.Stack(surface, "AssistantDashboard", false, 20, 12);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(1, 1);
        root.anchoredPosition = new Vector2(-24, -24); AssistantUi.Background(root, AssistantUi.Surface);
        var header = AssistantUi.Stack(root, "Header", true, 0, 8); AssistantUi.Size(header, 44);
        var title = AssistantUi.Label(header, "对话助手", 23); AssistantUi.Size(title, 44, -1, 1);
        AssistantUi.Size(AssistantUi.Button(header, "设置", openSettings), 42, 72);
        var fold = AssistantUi.Button(header, "收起", () => { collapsed = !collapsed; body.gameObject.SetActive(!collapsed); collapseLabel.text = collapsed ? "展开" : "收起"; Resize(); });
        AssistantUi.Size(fold, 42, 66); collapseLabel = fold.GetComponentInChildren<Text>();
        body = AssistantUi.Stack(root, "Body"); var bodySize = AssistantUi.Size(body); bodySize.flexibleHeight = 1;
        var tools = AssistantUi.Stack(body, "HistoryTools", true); AssistantUi.Size(tools, 40);
        AssistantUi.Button(tools, "历史会话", () => { knowledge.gameObject.SetActive(false); history.gameObject.SetActive(!history.gameObject.activeSelf); RefreshHistory(); });
        AssistantUi.Button(tools, "新对话", () => { controller.StartNewConversation(); RefreshHistory(); });
        AssistantUi.Button(tools, "清空当前", () => controller.ClearConversationHistory());
        history = AssistantUi.Scroll(body, "History", out var historyScroll); AssistantUi.Size(historyScroll, 160); history.gameObject.transform.parent.parent.gameObject.SetActive(false);
        // Toggle the scroll root together with its content.
        history.gameObject.SetActive(false);
        var conversationContent = AssistantUi.Scroll(body, "Conversation", out scroll);
        var scrollSize = AssistantUi.Size(scroll, 180); scrollSize.flexibleHeight = 1;
        conversation = AssistantUi.Label(conversationContent, "", 18, AssistantUi.Ink, -1);
        conversation.alignment = TextAnchor.UpperLeft;
        var conversationSize = conversation.GetComponent<LayoutElement>(); conversationSize.minHeight = 60;
        input = (ChatTextInputField)AssistantUi.Input(body, "", null, false, true, true);
        input.characterLimit = 16000; input.SubmitMessage = Send;
        var placeholder = AssistantUi.Label(input.transform, "输入消息 · Enter 发送，Shift+Enter 换行", 15, AssistantUi.Muted);
        AssistantUi.Fill(placeholder.rectTransform, 12, 7, 12, 7); placeholder.alignment = TextAnchor.UpperLeft; input.placeholder = placeholder;
        var composer = AssistantUi.Stack(body, "Composer", true, 0, 8); AssistantUi.Size(composer, 40);
        send = AssistantUi.Button(composer, "发送", Send, true); stop = AssistantUi.Button(composer, "停止", () => controller.InterruptAssistantOutput());
        readAloud = AssistantUi.Toggle(composer, "朗读回复", controller.readRepliesAloud, controller.SetReadRepliesAloud); AssistantUi.Size(readAloud, 36, 138);
        state = AssistantUi.Label(body, "就绪", 14, AssistantUi.Muted, 36);
        AssistantUi.Button(body, "记忆与知识库  ›", () => { history.gameObject.SetActive(false); history.parent.parent.gameObject.SetActive(false); knowledge.gameObject.SetActive(!knowledge.gameObject.activeSelf); });
        knowledge = AssistantUi.Stack(body, "Knowledge", false, 8, 6);
        var knowledgeButtons = AssistantUi.Stack(knowledge, "Actions", true); AssistantUi.Size(knowledgeButtons, 36);
        AssistantUi.Button(knowledgeButtons, "刷新知识库", () => controller.RefreshKnowledgeBase(), false, 36);
        AssistantUi.Button(knowledgeButtons, "打开目录", () => Application.OpenURL(new Uri(controller.GetKnowledgeBaseDirectoryPath()).AbsoluteUri), false, 36);
        knowledgeText = AssistantUi.Label(knowledge, "", 14, AssistantUi.Muted, 100); knowledgeText.alignment = TextAnchor.UpperLeft;
        knowledge.gameObject.SetActive(false);
        if (sceneButton != null)
        {
            sceneButton.transform.SetParent(body, false); sceneButton.gameObject.SetActive(true);
            AssistantUi.Size(sceneButton, 38);
            var sceneLabel = sceneButton.GetComponentInChildren<Text>();
            if (sceneLabel != null) { sceneLabel.font = AssistantUi.Font; sceneLabel.color = AssistantUi.Ink; }
        }
        var footer = AssistantUi.Stack(root, "Microphone", true, 0, 8); AssistantUi.Size(footer, 32);
        meterLabel = AssistantUi.Label(footer, "麦克风 · 待机", 13, AssistantUi.Muted); AssistantUi.Size(meterLabel, 30, 110);
        var track = AssistantUi.Rect(footer, "InputLevel"); AssistantUi.Size(track, 12, -1, 1); AssistantUi.Background(track, new Color32(220, 229, 233, 255));
        var fill = AssistantUi.Rect(track, "Level"); AssistantUi.Fill(fill); meter = AssistantUi.Background(fill, new Color32(39, 170, 123, 255));
        fill.anchorMax = new Vector2(.02f, 1);
        var peakRect = AssistantUi.Rect(track, "Peak"); peakRect.sizeDelta = new Vector2(2, 16); peak = AssistantUi.Background(peakRect, AssistantUi.Accent);
        peakRect.anchorMin = peakRect.anchorMax = new Vector2(.02f, .5f);
        Resize();
    }
    private void Resize()
    {
        var size = ((RectTransform)canvas).rect.size;
        root.sizeDelta = new Vector2(Mathf.Min(470, size.x * .38f), collapsed ? 120 : Mathf.Min(970, size.y - 48));
    }
    private void Send()
    {
        if (controller.SubmitTextMessage(input.text)) { input.text = ""; input.ActivateInputField(); }
    }
    private void RefreshHistory()
    {
        history.parent.parent.gameObject.SetActive(history.gameObject.activeSelf);
        if (!history.gameObject.activeSelf) return;
        AssistantUi.Clear(history);
        var actions = AssistantUi.Stack(history, "Actions", true); AssistantUi.Size(actions, 36);
        AssistantUi.Button(actions, "刷新", RefreshHistory, false, 36);
        AssistantUi.Button(actions, confirmDelete ? "确认删除全部" : "删除全部", () =>
        {
            if (confirmDelete && Time.unscaledTime < confirmUntil) { controller.DeleteAllConversationHistory(); confirmDelete = false; }
            else { confirmDelete = true; confirmUntil = Time.unscaledTime + 4; }
            RefreshHistory();
        }, false, 36);
        var sessions = controller.ListConversationSummaries();
        if (sessions.Count == 0) AssistantUi.Label(history, "暂无历史会话", 15, AssistantUi.Muted);
        foreach (var session in sessions)
            AssistantUi.Button(history, session.title, () => { controller.LoadConversation(session.id); history.gameObject.SetActive(false); history.parent.parent.gameObject.SetActive(false); }, false, 36);
    }
    private void Update()
    {
        if (controller == null || root == null) return;
        Resize();
        var target = controller.isListening ? Mathf.Clamp01((20 * Mathf.Log10(Mathf.Max(controller.CurrentMicrophoneLevel * 8.5f, .001f)) + 60) / 60) : .02f;
        level = Mathf.Lerp(level, target, 1 - Mathf.Exp(-Time.unscaledDeltaTime * (target > level ? 65 : 22)));
        peakValue = Mathf.Max(level, peakValue - Time.unscaledDeltaTime * .35f);
        meter.rectTransform.anchorMax = new Vector2(level, 1); meter.color = level > .85 ? new Color32(221, 80, 75, 255) : level > .68 ? new Color32(216, 156, 34, 255) : new Color32(39, 170, 123, 255);
        peak.rectTransform.anchorMin = peak.rectTransform.anchorMax = new Vector2(peakValue, .5f);
        meterLabel.text = controller.isListening ? "麦克风 · 输入中" : "麦克风 · 待机";
        if (Time.unscaledTime < refreshAt) return; refreshAt = Time.unscaledTime + .25f;
        if (confirmDelete && Time.unscaledTime > confirmUntil) { confirmDelete = false; RefreshHistory(); }
        string text = controller.FormatConversationText(80);
        if (text != lastConversation)
        {
            bool stick = scroll.verticalNormalizedPosition < .05f || string.IsNullOrEmpty(lastConversation);
            conversation.text = lastConversation = text;
            Canvas.ForceUpdateCanvases(); if (stick) scroll.verticalNormalizedPosition = 0;
        }
        state.text = controller.ChatStatus; send.interactable = !string.IsNullOrWhiteSpace(input.text);
        stop.interactable = controller.IsGenerating || controller.isSpeaking;
        readAloud.SetIsOnWithoutNotify(controller.readRepliesAloud);
        if (knowledge.gameObject.activeSelf) knowledgeText.text = controller.currentKnowledgeBaseStatus + "\n" + controller.GetKnowledgeBaseFileListText();
    }
}
