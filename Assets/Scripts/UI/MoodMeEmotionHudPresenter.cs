using System.Collections.Generic;
using System.Reflection;
using MoodMe;
using UnityEngine;
using UnityEngine.UI;

public class MoodMeEmotionHudPresenter : MonoBehaviour
{
    [Header("MoodMe")]
    public FaceDetector faceDetector;
    public EmotionsManager emotionsManager;

    [Header("Preview")]
    public RawImage previewImage;
    public RectTransform faceBox;
    public Text statusText;
    public bool mirrorPreviewHorizontally;
    public bool flipPreviewVertically;
    public bool mirrorFaceBoxHorizontally;
    public bool flipFaceBoxVertically;

    [Header("Emotion Bars")]
    public RectTransform[] emotionFills;
    public Text[] emotionValueTexts;

    [Header("Foldout")]
    public RectTransform panelRect;
    public Button toggleButton;
    public Text toggleButtonText;
    public bool startExpanded = true;
    public float expandedHeight = 290f;
    public float collapsedHeight = 42f;

    private readonly string[] emotionNames =
    {
        "Happy",
        "Surprised",
        "Scared",
        "Angry",
        "Disgust",
        "Sad",
        "Neutral"
    };

    private FieldInfo predictBoxesField;
    private bool isExpanded;
    private bool hasAppliedInitialExpandedState;

    private void Awake()
    {
        predictBoxesField = typeof(FaceDetector).GetField("predict_boxes", BindingFlags.Instance | BindingFlags.NonPublic);
        InitializeFoldout();
    }

    private void OnEnable()
    {
        InitializeFoldout();
    }

    private void LateUpdate()
    {
        if (!isExpanded)
        {
            return;
        }

        UpdatePreview();
        UpdateFaceBox();
        UpdateEmotionBars();
    }

    public void ToggleExpanded()
    {
        SetExpanded(!isExpanded);
    }

    public void SetExpanded(bool expanded)
    {
        if (!hasAppliedInitialExpandedState || panelRect == null)
        {
            InitializeFoldout();
        }

        isExpanded = expanded;
        ApplyExpandedState();
    }

    private void UpdatePreview()
    {
        if (previewImage == null)
        {
            return;
        }

        if (CameraManager.ExportWebcamTexture != null)
        {
            previewImage.texture = CameraManager.ExportWebcamTexture;
        }

        previewImage.uvRect = new Rect(
            mirrorPreviewHorizontally ? 1f : 0f,
            flipPreviewVertically ? 1f : 0f,
            mirrorPreviewHorizontally ? -1f : 1f,
            flipPreviewVertically ? -1f : 1f);
    }

    private void UpdateFaceBox()
    {
        if (faceBox == null || previewImage == null || faceDetector == null)
        {
            SetFaceBoxVisible(false);
            return;
        }

        if (!TryGetBestFaceBox(out FaceDetectorPostProcessing.FaceInfo bestBox))
        {
            SetFaceBoxVisible(false);
            return;
        }

        FaceDetectorPostProcessing.FaceInfo squareBox = GetBiggestSquare(bestBox);
        float boxWidth = squareBox.x2 - squareBox.x1;
        float boxHeight = squareBox.y2 - squareBox.y1;
        if (boxWidth <= 0f || boxHeight <= 0f)
        {
            SetFaceBoxVisible(false);
            return;
        }

        RectTransform previewRect = previewImage.rectTransform;
        Rect rect = previewRect.rect;
        float imageWidth = Mathf.Max(1f, faceDetector.ImageNetworkWidth);
        float imageHeight = Mathf.Max(1f, faceDetector.ImageNetworkHeight);

        float centerX01 = ((squareBox.x1 + squareBox.x2) * 0.5f) / imageWidth;
        float centerY01FromTop = ((squareBox.y1 + squareBox.y2) * 0.5f) / imageHeight;
        float width = (boxWidth / imageWidth) * rect.width;
        float height = (boxHeight / imageHeight) * rect.height;

        if (mirrorFaceBoxHorizontally)
        {
            centerX01 = 1f - centerX01;
        }

        float centerY01 = 1f - centerY01FromTop;
        if (flipFaceBoxVertically)
        {
            centerY01 = 1f - centerY01;
        }

        faceBox.anchoredPosition = new Vector2(
            (centerX01 - 0.5f) * rect.width,
            (centerY01 - 0.5f) * rect.height);
        faceBox.sizeDelta = new Vector2(width, height);
        SetFaceBoxVisible(true);
    }

    private bool TryGetBestFaceBox(out FaceDetectorPostProcessing.FaceInfo bestBox)
    {
        bestBox = default;
        if (predictBoxesField == null || faceDetector == null)
        {
            return false;
        }

        List<FaceDetectorPostProcessing.FaceInfo> boxes =
            predictBoxesField.GetValue(faceDetector) as List<FaceDetectorPostProcessing.FaceInfo>;
        if (boxes == null || boxes.Count == 0)
        {
            return false;
        }

        bool found = false;
        float bestScore = 0f;
        for (int i = 0; i < boxes.Count; i++)
        {
            if (!found || boxes[i].score > bestScore)
            {
                bestBox = boxes[i];
                bestScore = boxes[i].score;
                found = true;
            }
        }

        return found && bestScore > 0f;
    }

    private FaceDetectorPostProcessing.FaceInfo GetBiggestSquare(FaceDetectorPostProcessing.FaceInfo box)
    {
        float boxWidth = box.x2 - box.x1;
        float boxHeight = box.y2 - box.y1;
        float bigEdge = boxWidth > boxHeight ? boxWidth : boxHeight;
        float smallEdge = boxWidth < boxHeight ? boxWidth : boxHeight;
        float bigX0 = boxWidth > boxHeight ? box.x1 : box.x1 - (bigEdge - smallEdge) * 0.5f;
        float bigY0 = boxWidth < boxHeight ? box.y1 : box.y1 - (bigEdge - smallEdge) * 0.5f;

        return new FaceDetectorPostProcessing.FaceInfo
        {
            x1 = bigX0,
            y1 = bigY0,
            x2 = bigX0 + bigEdge,
            y2 = bigY0 + bigEdge,
            score = box.score
        };
    }

    private void UpdateEmotionBars()
    {
        if (emotionsManager == null)
        {
            SetStatus("Waiting for emotions");
            return;
        }

        float[] values =
        {
            emotionsManager.Happy,
            emotionsManager.Surprised,
            emotionsManager.Scared,
            emotionsManager.Angry,
            emotionsManager.Disgust,
            emotionsManager.Sad,
            emotionsManager.Neutral
        };

        int dominantIndex = 0;
        float dominantValue = values[0];

        for (int i = 0; i < values.Length; i++)
        {
            float value = Mathf.Clamp01(values[i]);
            if (emotionFills != null && i < emotionFills.Length && emotionFills[i] != null)
            {
                emotionFills[i].anchorMax = new Vector2(value, 1f);
            }

            if (emotionValueTexts != null && i < emotionValueTexts.Length && emotionValueTexts[i] != null)
            {
                emotionValueTexts[i].text = Mathf.RoundToInt(value * 100f) + "%";
            }

            if (value > dominantValue)
            {
                dominantIndex = i;
                dominantValue = value;
            }
        }

        SetStatus("Top: " + emotionNames[dominantIndex] + " " + Mathf.RoundToInt(dominantValue * 100f) + "%");
    }

    private void SetFaceBoxVisible(bool visible)
    {
        if (faceBox != null && faceBox.gameObject.activeSelf != visible)
        {
            faceBox.gameObject.SetActive(visible);
        }
    }

    private void SetStatus(string text)
    {
        if (statusText != null)
        {
            statusText.text = text;
        }
    }

    private void InitializeFoldout()
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
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
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

        return child.name == "Title" || child.name == "TitleText" || child.name == "ToggleButton";
    }

    private void SetPanelHeight(float height)
    {
        if (panelRect == null || height <= 0f)
        {
            return;
        }

        Vector2 size = panelRect.sizeDelta;
        Vector2 anchoredPosition = panelRect.anchoredPosition;
        float top = anchoredPosition.y + (1f - panelRect.pivot.y) * size.y;

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
        if (toggleButton == null)
        {
            return;
        }

        toggleButton.onClick.RemoveListener(ToggleExpanded);
        toggleButton.onClick.AddListener(ToggleExpanded);
    }

    private void CreateToggleButton()
    {
        GameObject buttonObject = new GameObject("ToggleButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.layer = gameObject.layer;
        buttonObject.transform.SetParent(transform, false);
        buttonObject.transform.SetAsLastSibling();

        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(1f, 1f);
        buttonRect.anchorMax = new Vector2(1f, 1f);
        buttonRect.pivot = new Vector2(1f, 1f);
        buttonRect.anchoredPosition = new Vector2(-12f, -10f);
        buttonRect.sizeDelta = new Vector2(58f, 20f);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.22f, 0.32f, 0.42f, 0.95f);

        toggleButton = buttonObject.GetComponent<Button>();
        toggleButton.targetGraphic = image;

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        labelObject.layer = gameObject.layer;
        labelObject.transform.SetParent(buttonObject.transform, false);

        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        toggleButtonText = labelObject.GetComponent<Text>();
        toggleButtonText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        toggleButtonText.fontSize = 11;
        toggleButtonText.alignment = TextAnchor.MiddleCenter;
        toggleButtonText.color = new Color(0.96f, 0.98f, 1f, 1f);
        toggleButtonText.raycastTarget = false;
    }
}
