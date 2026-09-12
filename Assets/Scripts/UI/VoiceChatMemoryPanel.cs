using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

public sealed class VoiceChatMemoryPanel : MonoBehaviour
{
    [Header("Source")]
    public VoiceChatController controller;

    [Header("Plain UI Text")]
    public Text conversationText;
    public Text knowledgeStatusText;
    public Text knowledgeFilesText;
    public Text pathsText;

    [Header("Foldout")]
    public RectTransform panelRect;
    public GameObject contentRoot;
    public Button toggleButton;
    public Text toggleButtonText;
    public bool startExpanded = true;
    public float expandedHeight = 640f;
    public float collapsedHeight = 52f;

    [Header("Display")]
    [Range(10, 200)]
    public int maxDisplayedMessages = 80;

    [Range(0.1f, 5f)]
    public float refreshIntervalSeconds = 0.5f;

    [Header("Conversation Scroll")]
    public ScrollRect conversationScrollRect;
    public bool autoBuildConversationScrollView = true;
    [Range(0f, 0.5f)]
    public float autoStickToBottomThreshold = 0.05f;

    [Header("Conversation Selection")]
    public Dropdown conversationDropdown;
    public Button loadConversationButton;
    public Button refreshConversationListButton;
    public Button deleteAllConversationsButton;
    public Text deleteAllConversationsButtonText;
    public bool autoBindConversationSelector = true;
    public bool requireDeleteAllConfirmation = true;
    public float deleteAllConfirmSeconds = 4f;

    private float lastRefreshTime;
    private bool isExpanded;
    private bool hasAppliedInitialExpandedState;
    private string lastConversationValue;
    private readonly List<ChatSessionSummary> conversationSummaries = new List<ChatSessionSummary>();
    private bool suppressConversationDropdownCallback;
    private bool deleteAllConfirmationPending;
    private float deleteAllConfirmationDeadline;

    private void Awake()
    {
        InitializeUi();
    }

    private void OnEnable()
    {
        InitializeUi();
        RefreshAll();
        RefreshConversationList();
    }

    private void Update()
    {
        if (deleteAllConfirmationPending && Time.unscaledTime > deleteAllConfirmationDeadline)
        {
            ResetDeleteAllConfirmation();
        }

        if (Time.unscaledTime - lastRefreshTime < refreshIntervalSeconds)
        {
            return;
        }

        lastRefreshTime = Time.unscaledTime;
        RefreshAll();
    }

    public void RefreshAll()
    {
        if (controller == null)
        {
            return;
        }

        RefreshConversation();
        RefreshKnowledgeTexts();
        RefreshPaths();
    }

    public void RefreshConversation()
    {
        if (controller == null)
        {
            return;
        }

        SetConversationText(controller.FormatConversationText(maxDisplayedMessages));
    }

    public void RefreshKnowledgeTexts()
    {
        if (controller == null)
        {
            return;
        }

        SetText(knowledgeStatusText, controller.currentKnowledgeBaseStatus);
        SetText(knowledgeFilesText, controller.GetKnowledgeBaseFileListText());
    }

    public void RefreshPaths()
    {
        if (controller == null)
        {
            return;
        }

        var text = "聊天记录目录：" + controller.GetConversationDirectoryPath()
            + "\n知识库目录：" + controller.GetKnowledgeBaseDirectoryPath();
        SetText(pathsText, text);
    }

    public void RefreshKnowledgeBase()
    {
        if (controller == null)
        {
            return;
        }

        controller.RefreshKnowledgeBase();
        RefreshKnowledgeTexts();
        RefreshPaths();
    }

    public void StartNewConversation()
    {
        if (controller == null)
        {
            return;
        }

        controller.StartNewConversation();
        RefreshAll();
        RefreshConversationList();
    }

    public void ClearConversationHistory()
    {
        if (controller == null)
        {
            return;
        }

        controller.ClearConversationHistory();
        RefreshAll();
        RefreshConversationList();
    }

    public void RefreshConversationList()
    {
        EnsureConversationSelector();
        conversationSummaries.Clear();

        if (conversationDropdown == null)
        {
            return;
        }

        if (controller != null)
        {
            conversationSummaries.AddRange(controller.ListConversationSummaries());
        }

        conversationDropdown.ClearOptions();
        if (conversationSummaries.Count == 0)
        {
            conversationDropdown.AddOptions(new List<string> { "\u65E0\u5386\u53F2\u5BF9\u8BDD" });
            SetConversationDropdownValue(0);
            conversationDropdown.interactable = false;
        }
        else
        {
            var options = new List<string>();
            foreach (var summary in conversationSummaries)
            {
                options.Add(FormatConversationOption(summary));
            }

            conversationDropdown.AddOptions(options);
            var activeIndex = FindActiveConversationIndex();
            SetConversationDropdownValue(activeIndex >= 0 ? activeIndex : 0);
            conversationDropdown.interactable = true;
        }

        UpdateConversationSelectorButtons();
    }

    public void LoadSelectedConversation()
    {
        if (controller == null || conversationDropdown == null)
        {
            return;
        }

        var index = conversationDropdown.value;
        if (index < 0 || index >= conversationSummaries.Count)
        {
            return;
        }

        ResetDeleteAllConfirmation();
        if (controller.LoadConversation(conversationSummaries[index].id))
        {
            RefreshAll();
            RefreshConversationList();
        }
    }

    public void DeleteAllConversations()
    {
        if (controller == null || conversationSummaries.Count == 0)
        {
            return;
        }

        if (requireDeleteAllConfirmation && !deleteAllConfirmationPending)
        {
            deleteAllConfirmationPending = true;
            deleteAllConfirmationDeadline = Time.unscaledTime + Mathf.Max(1f, deleteAllConfirmSeconds);
            UpdateDeleteAllButtonLabel();
            return;
        }

        controller.DeleteAllConversationHistory();
        lastConversationValue = null;
        ResetDeleteAllConfirmation();
        RefreshAll();
        RefreshConversationList();
    }

    public void SetConversationText(string text)
    {
        var wasAtBottom = ShouldStickConversationToBottom();
        var changed = SetText(conversationText, text);

        if (changed)
        {
            lastConversationValue = text ?? string.Empty;
            RefreshConversationLayout(wasAtBottom);
        }
    }

    public void SetKnowledgeStatusText(string text)
    {
        SetText(knowledgeStatusText, text);
        RefreshKnowledgeTexts();
    }

    public void ToggleExpanded()
    {
        SetExpanded(!isExpanded);
    }

    public void SetExpanded(bool expanded)
    {
        if (!hasAppliedInitialExpandedState || panelRect == null)
        {
            InitializeUi();
        }

        isExpanded = expanded;
        ApplyExpandedState();

        if (isExpanded)
        {
            RefreshAll();
        }
    }

    private void InitializeUi()
    {
        if (panelRect == null)
        {
            panelRect = GetComponent<RectTransform>();
        }

        if (expandedHeight <= 0f && panelRect != null)
        {
            expandedHeight = panelRect.sizeDelta.y;
        }

        if (toggleButton == null)
        {
            toggleButton = transform.Find("ToggleButton")?.GetComponent<Button>();
        }

        if (toggleButton == null)
        {
            CreateToggleButton();
        }

        if (toggleButtonText == null && toggleButton != null)
        {
            toggleButtonText = toggleButton.GetComponentInChildren<Text>(true);
        }

        WireToggleButton();
        EnsureConversationSelector();
        EnsureConversationScrollView();

        if (!hasAppliedInitialExpandedState)
        {
            isExpanded = startExpanded;
            ApplyExpandedState();
            hasAppliedInitialExpandedState = true;
        }
    }

    private void ApplyExpandedState()
    {
        SetFoldoutContentVisible(isExpanded);
        SetPanelHeight(isExpanded ? expandedHeight : collapsedHeight);
        UpdateToggleButtonLabel();
    }

    private void SetFoldoutContentVisible(bool visible)
    {
        if (contentRoot != null)
        {
            contentRoot.SetActive(visible);
            return;
        }

        for (var i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            if (ShouldStayVisibleWhenCollapsed(child))
            {
                continue;
            }

            child.gameObject.SetActive(visible);
        }
    }

    private bool ShouldStayVisibleWhenCollapsed(Transform child)
    {
        if (child == null)
        {
            return true;
        }

        if (toggleButton != null && child == toggleButton.transform)
        {
            return true;
        }

        return child.name == "TitleText" || child.name == "ToggleButton";
    }

    private void SetPanelHeight(float height)
    {
        if (panelRect == null || height <= 0f)
        {
            return;
        }

        var size = panelRect.sizeDelta;
        var anchoredPosition = panelRect.anchoredPosition;
        var top = anchoredPosition.y + (1f - panelRect.pivot.y) * size.y;

        size.y = height;
        anchoredPosition.y = top - (1f - panelRect.pivot.y) * size.y;

        panelRect.sizeDelta = size;
        panelRect.anchoredPosition = anchoredPosition;
    }

    private void UpdateToggleButtonLabel()
    {
        if (toggleButtonText != null)
        {
            toggleButtonText.text = isExpanded ? "\u6536\u8d77" : "\u5c55\u5f00";
        }
    }

    private void WireToggleButton()
    {
        if (toggleButton == null || HasPersistentToggleListener(toggleButton))
        {
            return;
        }

        toggleButton.onClick.RemoveListener(ToggleExpanded);
        toggleButton.onClick.AddListener(ToggleExpanded);
    }

    private bool HasPersistentToggleListener(Button button)
    {
        var events = button.onClick;
        for (var i = 0; i < events.GetPersistentEventCount(); i++)
        {
            if (events.GetPersistentTarget(i) == this
                && events.GetPersistentMethodName(i) == nameof(ToggleExpanded))
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureConversationSelector()
    {
        if (!autoBindConversationSelector)
        {
            return;
        }

        if (conversationDropdown == null)
        {
            conversationDropdown = transform.Find("ConversationDropdown")?.GetComponent<Dropdown>();
        }

        if (loadConversationButton == null)
        {
            loadConversationButton = transform.Find("LoadConversationButton")?.GetComponent<Button>();
        }

        if (refreshConversationListButton == null)
        {
            refreshConversationListButton = transform.Find("RefreshConversationListButton")?.GetComponent<Button>();
        }

        if (deleteAllConversationsButton == null)
        {
            deleteAllConversationsButton = transform.Find("DeleteAllConversationsButton")?.GetComponent<Button>();
        }

        if (deleteAllConversationsButtonText == null && deleteAllConversationsButton != null)
        {
            deleteAllConversationsButtonText = deleteAllConversationsButton.GetComponentInChildren<Text>(true);
        }

        WireConversationSelector();
        UpdateDeleteAllButtonLabel();
    }

    private void WireConversationSelector()
    {
        if (conversationDropdown != null)
        {
            conversationDropdown.onValueChanged.RemoveListener(HandleConversationDropdownChanged);
            conversationDropdown.onValueChanged.AddListener(HandleConversationDropdownChanged);
        }

        WireButton(loadConversationButton, LoadSelectedConversation);
        WireButton(refreshConversationListButton, RefreshConversationList);
        WireButton(deleteAllConversationsButton, DeleteAllConversations);
    }

    private void HandleConversationDropdownChanged(int value)
    {
        if (!suppressConversationDropdownCallback)
        {
            ResetDeleteAllConfirmation();
        }
    }

    private void SetConversationDropdownValue(int value)
    {
        if (conversationDropdown == null)
        {
            return;
        }

        suppressConversationDropdownCallback = true;
        conversationDropdown.value = value;
        conversationDropdown.RefreshShownValue();
        suppressConversationDropdownCallback = false;
    }

    private void UpdateConversationSelectorButtons()
    {
        var hasConversations = conversationSummaries.Count > 0;
        if (loadConversationButton != null)
        {
            loadConversationButton.interactable = controller != null && hasConversations;
        }

        if (refreshConversationListButton != null)
        {
            refreshConversationListButton.interactable = controller != null;
        }

        if (deleteAllConversationsButton != null)
        {
            deleteAllConversationsButton.interactable = controller != null && hasConversations;
        }

        if (!hasConversations)
        {
            ResetDeleteAllConfirmation();
        }
    }

    private void ResetDeleteAllConfirmation()
    {
        if (!deleteAllConfirmationPending)
        {
            UpdateDeleteAllButtonLabel();
            return;
        }

        deleteAllConfirmationPending = false;
        UpdateDeleteAllButtonLabel();
    }

    private void UpdateDeleteAllButtonLabel()
    {
        if (deleteAllConversationsButtonText == null)
        {
            return;
        }

        deleteAllConversationsButtonText.text = deleteAllConfirmationPending ? "\u786E\u8BA4\u5220" : "\u5220\u5168\u90E8";
    }

    private int FindActiveConversationIndex()
    {
        if (controller == null || string.IsNullOrWhiteSpace(controller.activeConversationId))
        {
            return -1;
        }

        for (var i = 0; i < conversationSummaries.Count; i++)
        {
            if (conversationSummaries[i] != null && conversationSummaries[i].id == controller.activeConversationId)
            {
                return i;
            }
        }

        return -1;
    }

    private static string FormatConversationOption(ChatSessionSummary summary)
    {
        if (summary == null)
        {
            return "\u65E0\u6548\u5BF9\u8BDD";
        }

        var time = FormatConversationTime(summary.updatedAtUtc, summary.id);
        var title = string.IsNullOrWhiteSpace(summary.title) ? "\u672A\u547D\u540D\u5BF9\u8BDD" : summary.title.Trim();
        title = TextSearchUtility.CompactWhitespace(title);
        if (title.Length > 16)
        {
            title = title.Substring(0, 16) + "...";
        }

        return $"{time} | {summary.messageCount}\u6761 | {title}";
    }

    private static string FormatConversationTime(string utcValue, string sessionId)
    {
        if (!string.IsNullOrWhiteSpace(utcValue)
            && System.DateTime.TryParse(
                utcValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsedUtc))
        {
            return parsedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }

        if (!string.IsNullOrWhiteSpace(sessionId)
            && System.DateTime.TryParseExact(
                sessionId.Length >= 16 ? sessionId.Substring(0, 16) : sessionId,
                "yyyy-MM-dd_HH-mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedId))
        {
            return parsedId.ToString("yyyy-MM-dd HH:mm");
        }

        return "\u672A\u77E5\u65F6\u95F4";
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

    private void CreateToggleButton()
    {
        var buttonObject = new GameObject("ToggleButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.layer = gameObject.layer;
        buttonObject.transform.SetParent(transform, false);
        buttonObject.transform.SetAsLastSibling();

        var buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(1f, 1f);
        buttonRect.anchorMax = new Vector2(1f, 1f);
        buttonRect.pivot = new Vector2(1f, 1f);
        buttonRect.anchoredPosition = new Vector2(-20f, -14f);
        buttonRect.sizeDelta = new Vector2(76f, 28f);

        var image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.22f, 0.32f, 0.42f, 0.95f);

        toggleButton = buttonObject.GetComponent<Button>();
        toggleButton.targetGraphic = image;

        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        labelObject.layer = gameObject.layer;
        labelObject.transform.SetParent(buttonObject.transform, false);

        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        toggleButtonText = labelObject.GetComponent<Text>();
        toggleButtonText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        toggleButtonText.fontSize = 14;
        toggleButtonText.alignment = TextAnchor.MiddleCenter;
        toggleButtonText.color = new Color(0.96f, 0.98f, 1f, 1f);
        toggleButtonText.raycastTarget = false;
    }

    private void EnsureConversationScrollView()
    {
        if (!autoBuildConversationScrollView || conversationText == null)
        {
            return;
        }

        var textRect = conversationText.rectTransform;
        if (conversationScrollRect == null)
        {
            conversationScrollRect = textRect.GetComponentInParent<ScrollRect>();
        }

        if (conversationScrollRect == null)
        {
            BuildConversationScrollView(textRect);
        }

        ConfigureConversationTextForScroll();
    }

    private void BuildConversationScrollView(RectTransform textRect)
    {
        var originalParent = textRect.parent as RectTransform;
        if (originalParent == null)
        {
            return;
        }

        var siblingIndex = textRect.GetSiblingIndex();
        var scrollObject = new GameObject("ConversationScrollView", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
        scrollObject.layer = textRect.gameObject.layer;
        scrollObject.transform.SetParent(originalParent, false);
        scrollObject.transform.SetSiblingIndex(siblingIndex);

        var scrollRectTransform = scrollObject.GetComponent<RectTransform>();
        CopyRectTransform(textRect, scrollRectTransform);

        var scrollImage = scrollObject.GetComponent<Image>();
        scrollImage.color = new Color(0f, 0f, 0f, 0.12f);
        scrollImage.raycastTarget = true;

        var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D));
        viewportObject.layer = textRect.gameObject.layer;
        viewportObject.transform.SetParent(scrollObject.transform, false);

        var viewportRect = viewportObject.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(6f, 6f);
        viewportRect.offsetMax = new Vector2(-6f, -6f);

        var viewportImage = viewportObject.GetComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0f);
        viewportImage.raycastTarget = true;

        textRect.SetParent(viewportObject.transform, false);
        textRect.anchorMin = new Vector2(0f, 1f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.pivot = new Vector2(0.5f, 1f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(0f, textRect.sizeDelta.y);

        conversationScrollRect = scrollObject.GetComponent<ScrollRect>();
        conversationScrollRect.content = textRect;
        conversationScrollRect.viewport = viewportRect;
        conversationScrollRect.horizontal = false;
        conversationScrollRect.vertical = true;
        conversationScrollRect.movementType = ScrollRect.MovementType.Clamped;
        conversationScrollRect.inertia = true;
        conversationScrollRect.scrollSensitivity = 24f;
    }

    private void ConfigureConversationTextForScroll()
    {
        conversationText.horizontalOverflow = HorizontalWrapMode.Wrap;
        conversationText.verticalOverflow = VerticalWrapMode.Overflow;
        conversationText.alignment = TextAnchor.UpperLeft;

        var fitter = conversationText.GetComponent<ContentSizeFitter>();
        if (fitter == null)
        {
            fitter = conversationText.gameObject.AddComponent<ContentSizeFitter>();
        }

        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private bool ShouldStickConversationToBottom()
    {
        if (conversationScrollRect == null || string.IsNullOrEmpty(lastConversationValue))
        {
            return true;
        }

        return conversationScrollRect.verticalNormalizedPosition <= autoStickToBottomThreshold;
    }

    private void RefreshConversationLayout(bool stickToBottom)
    {
        if (conversationScrollRect == null || conversationText == null)
        {
            return;
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(conversationText.rectTransform);
        Canvas.ForceUpdateCanvases();

        if (stickToBottom)
        {
            conversationScrollRect.velocity = Vector2.zero;
            conversationScrollRect.verticalNormalizedPosition = 0f;
        }
    }

    private static bool SetText(Text target, string value)
    {
        if (target == null)
        {
            return false;
        }

        var next = value ?? string.Empty;
        if (target.text == next)
        {
            return false;
        }

        target.text = next;
        return true;
    }

    private static void CopyRectTransform(RectTransform source, RectTransform target)
    {
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.anchoredPosition = source.anchoredPosition;
        target.sizeDelta = source.sizeDelta;
        target.pivot = source.pivot;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;
    }
}
