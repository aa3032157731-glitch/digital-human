using MoodMe;
using Unity.Barracuda;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class MoodMeEmotionHudSceneInstaller
{
    private const string ScenePath = "Assets/Scenes/main.unity";
    private static readonly string[] EmotionNames =
    {
        "Happy",
        "Surprised",
        "Scared",
        "Angry",
        "Disgust",
        "Sad",
        "Neutral"
    };

    [MenuItem("Tools/MoodMe/Install Emotion HUD")]
    public static void Install()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject runtimeRoot = EnsureRoot("MoodMe Emotion Recognition");
        GameObject runtime = EnsureChild(runtimeRoot.transform, "MoodMeRuntime");

        CameraManager cameraManager = GetOrAdd<CameraManager>(runtime);
        FaceDetector faceDetector = GetOrAdd<FaceDetector>(runtime);
        ManageEmotionsNetwork emotionNetwork = GetOrAdd<ManageEmotionsNetwork>(runtime);
        EmotionsManager emotionsManager = GetOrAdd<EmotionsManager>(runtime);

        cameraManager.DeviceIndex = 0;
        cameraManager.WebcamTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
            "Assets/Plugins/MoodMeBarracuda/Images/webcam_image.png");
        cameraManager.VideoTexure = null;

        faceDetector.Network = AssetDatabase.LoadAssetAtPath<NNModel>(
            "Assets/Plugins/MoodMeBarracuda/Networks/version-RFB-320.onnx");
        faceDetector.ImageInputWidth = 640;
        faceDetector.ImageInputHeight = 480;
        faceDetector.ImageNetworkWidth = 320;
        faceDetector.ImageNetworkHeight = 240;
        faceDetector.ChannelCount = 3;
        faceDetector.DetectionThreshold = 0.7f;
        faceDetector.Device = WorkerFactory.Device.Auto;
        faceDetector.Process = true;
        faceDetector.GUIPreview = false;
        faceDetector.ExportCrop = true;
        faceDetector.ExportCropWidth = 48;
        faceDetector.ExportCropHeight = 48;
        faceDetector.CropFormat = Preprocessing.ValueType.Color32;

        emotionNetwork.EmotionsNNetwork = AssetDatabase.LoadAssetAtPath<NNModel>(
            "Assets/Plugins/MoodMeBarracuda/Networks/face_emotions_model.onnx");
        emotionNetwork.ImageNetworkWidth = 44;
        emotionNetwork.ImageNetworkHeight = 44;
        emotionNetwork.ChannelCount = 3;
        emotionNetwork.Device = WorkerFactory.Device.Auto;
        emotionNetwork.Process = true;

        emotionsManager.EmotionNetworkManager = emotionNetwork;
        emotionsManager.FaceDetectorManager = faceDetector;
        emotionsManager.ProcessEveryNFrames = 15;
        emotionsManager.FilterAllZeros = true;
        emotionsManager.Smoothing = 15;
        emotionsManager.TestMode = false;

        GameObject canvasGo = EnsureRoot("MoodMe Emotion HUD Canvas");
        Canvas canvas = GetOrAdd<Canvas>(canvasGo);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        CanvasScaler scaler = GetOrAdd<CanvasScaler>(canvasGo);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        GetOrAdd<GraphicRaycaster>(canvasGo);

        GameObject panel = EnsureUiChild(canvasGo.transform, "EmotionHUDPanel");
        RectTransform panelRect = ConfigureRect(panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
        panelRect.anchoredPosition = new Vector2(16f, -16f);
        panelRect.sizeDelta = new Vector2(380f, 290f);
        Image panelImage = GetOrAdd<Image>(panel);
        panelImage.color = new Color(0.03f, 0.035f, 0.04f, 0.72f);
        panelImage.raycastTarget = false;

        GameObject preview = EnsureUiChild(panel.transform, "CameraPreview");
        RectTransform previewRect = ConfigureRect(preview, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
        previewRect.anchoredPosition = new Vector2(12f, -36f);
        previewRect.sizeDelta = new Vector2(176f, 132f);
        RawImage rawImage = GetOrAdd<RawImage>(preview);
        rawImage.color = Color.white;
        rawImage.raycastTarget = false;

        GameObject faceBox = EnsureUiChild(preview.transform, "FaceBox");
        RectTransform faceBoxRect = ConfigureRect(faceBox, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        faceBoxRect.anchoredPosition = Vector2.zero;
        faceBoxRect.sizeDelta = Vector2.zero;
        Image faceBoxImage = GetOrAdd<Image>(faceBox);
        faceBoxImage.color = new Color(1f, 0f, 0f, 0.38f);
        faceBoxImage.raycastTarget = false;
        faceBox.SetActive(false);

        Text title = EnsureText(panel.transform, "Title", "Emotion Recognition", 14, FontStyle.Bold, TextAnchor.MiddleLeft);
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.anchoredPosition = new Vector2(12f, -10f);
        titleRect.sizeDelta = new Vector2(-24f, 22f);

        Text status = EnsureText(panel.transform, "StatusText", "Waiting for camera", 12, FontStyle.Normal, TextAnchor.MiddleLeft);
        RectTransform statusRect = status.rectTransform;
        statusRect.anchorMin = new Vector2(0f, 1f);
        statusRect.anchorMax = new Vector2(1f, 1f);
        statusRect.pivot = new Vector2(0f, 1f);
        statusRect.anchoredPosition = new Vector2(12f, -176f);
        statusRect.sizeDelta = new Vector2(-24f, 20f);

        RectTransform[] fills = new RectTransform[EmotionNames.Length];
        Text[] valueTexts = new Text[EmotionNames.Length];
        for (int i = 0; i < EmotionNames.Length; i++)
        {
            CreateEmotionBar(panel.transform, EmotionNames[i], i, fills, valueTexts);
        }

        MoodMeEmotionHudPresenter presenter = GetOrAdd<MoodMeEmotionHudPresenter>(panel);
        presenter.faceDetector = faceDetector;
        presenter.emotionsManager = emotionsManager;
        presenter.previewImage = rawImage;
        presenter.faceBox = faceBoxRect;
        presenter.statusText = status;
        presenter.emotionFills = fills;
        presenter.emotionValueTexts = valueTexts;
        presenter.mirrorPreviewHorizontally = false;
        presenter.flipPreviewVertically = false;
        presenter.mirrorFaceBoxHorizontally = false;
        presenter.flipFaceBoxVertically = false;

        if (Object.FindObjectOfType<EventSystem>() == null)
        {
            GameObject eventSystemGo = EnsureRoot("MoodMe Emotion HUD EventSystem");
            GetOrAdd<EventSystem>(eventSystemGo);
            GetOrAdd<StandaloneInputModule>(eventSystemGo);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("MoodMe Emotion HUD installed into " + ScenePath);
    }

    private static void CreateEmotionBar(Transform parent, string emotionName, int index, RectTransform[] fills, Text[] valueTexts)
    {
        float top = -36f - index * 31f;
        GameObject row = EnsureUiChild(parent, emotionName + "Row");
        RectTransform rowRect = ConfigureRect(row, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f));
        rowRect.anchoredPosition = new Vector2(202f, top);
        rowRect.sizeDelta = new Vector2(-214f, 24f);

        Text label = EnsureText(row.transform, "Label", emotionName, 11, FontStyle.Normal, TextAnchor.MiddleLeft);
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0.5f);
        labelRect.anchorMax = new Vector2(0f, 0.5f);
        labelRect.pivot = new Vector2(0f, 0.5f);
        labelRect.anchoredPosition = new Vector2(0f, 0f);
        labelRect.sizeDelta = new Vector2(64f, 18f);

        GameObject barBg = EnsureUiChild(row.transform, "Bar");
        RectTransform bgRect = ConfigureRect(barBg, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f));
        bgRect.anchoredPosition = new Vector2(70f, 0f);
        bgRect.sizeDelta = new Vector2(-108f, 10f);
        Image bgImage = GetOrAdd<Image>(barBg);
        bgImage.color = new Color(1f, 1f, 1f, 0.16f);
        bgImage.raycastTarget = false;

        GameObject fill = EnsureUiChild(barBg.transform, "Fill");
        RectTransform fillRect = ConfigureRect(fill, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f));
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fillImage = GetOrAdd<Image>(fill);
        fillImage.color = EmotionColor(index);
        fillImage.raycastTarget = false;
        fills[index] = fillRect;

        Text value = EnsureText(row.transform, "Value", "0%", 10, FontStyle.Normal, TextAnchor.MiddleRight);
        RectTransform valueRect = value.rectTransform;
        valueRect.anchorMin = new Vector2(1f, 0.5f);
        valueRect.anchorMax = new Vector2(1f, 0.5f);
        valueRect.pivot = new Vector2(1f, 0.5f);
        valueRect.anchoredPosition = new Vector2(0f, 0f);
        valueRect.sizeDelta = new Vector2(34f, 18f);
        valueTexts[index] = value;
    }

    private static Color EmotionColor(int index)
    {
        switch (index)
        {
            case 0: return new Color(0.22f, 0.78f, 0.42f, 0.95f);
            case 1: return new Color(0.98f, 0.78f, 0.24f, 0.95f);
            case 2: return new Color(0.40f, 0.58f, 0.95f, 0.95f);
            case 3: return new Color(0.95f, 0.26f, 0.20f, 0.95f);
            case 4: return new Color(0.65f, 0.42f, 0.88f, 0.95f);
            case 5: return new Color(0.23f, 0.55f, 0.85f, 0.95f);
            default: return new Color(0.86f, 0.86f, 0.86f, 0.95f);
        }
    }

    private static Text EnsureText(Transform parent, string name, string text, int fontSize, FontStyle fontStyle, TextAnchor alignment)
    {
        GameObject go = EnsureUiChild(parent, name);
        Text uiText = GetOrAdd<Text>(go);
        uiText.text = text;
        uiText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        uiText.fontSize = fontSize;
        uiText.fontStyle = fontStyle;
        uiText.alignment = alignment;
        uiText.color = Color.white;
        uiText.raycastTarget = false;
        return uiText;
    }

    private static RectTransform ConfigureRect(GameObject go, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
    {
        RectTransform rect = GetOrAdd<RectTransform>(go);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        return rect;
    }

    private static GameObject EnsureRoot(string name)
    {
        GameObject existing = GameObject.Find(name);
        if (existing != null)
        {
            return existing;
        }

        return new GameObject(name);
    }

    private static GameObject EnsureChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null)
        {
            return child.gameObject;
        }

        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    private static GameObject EnsureUiChild(Transform parent, string name)
    {
        GameObject go = EnsureChild(parent, name);
        RectTransform rect = go.GetComponent<RectTransform>();
        if (rect == null)
        {
            rect = go.AddComponent<RectTransform>();
        }

        return go;
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        if (component == null)
        {
            component = go.AddComponent<T>();
        }

        return component;
    }
}
