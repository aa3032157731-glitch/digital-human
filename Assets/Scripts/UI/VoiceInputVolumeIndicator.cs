using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class VoiceInputVolumeIndicator : MonoBehaviour
{
    public const string RootObjectName = "VoiceInputVolumeIndicator";

    [Header("Source")]
    public VoiceChatController controller;

    [Header("Layout")]
    public RectTransform levelMaskRect;
    public RectTransform levelGradientRect;
    public RectTransform peakMarkerRect;
    public float meterHeight = 118f;
    public float meterWidth = 20f;
    public float labelWidth = 32f;

    [Header("Response")]
    public float restingLevel = 0.025f;
    public float silenceDb = -60f;
    public float noiseFloor = 0.0035f;
    public float inputGain = 8.5f;
    public float attackSpeed = 65f;
    public float releaseSpeed = 22f;
    public float peakReleaseSpeed = 2.2f;
    public float alphaSmoothing = 12f;

    private CanvasGroup canvasGroup;
    private Image levelImage;
    private Image peakMarkerImage;
    private float displayedLevel;
    private float peakLevel;
    private static Sprite meterSprite;
    private static Sprite roundedSprite;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.alpha = 0.62f;
        }

        displayedLevel = restingLevel;
        peakLevel = restingLevel;
        ApplyLevel(displayedLevel);
    }

    private void LateUpdate()
    {
        if (controller == null)
        {
            controller = FindObjectOfType<VoiceChatController>();
        }

        var isTalking = controller != null && controller.isListening;
        var rawLevel = isTalking ? controller.CurrentMicrophoneLevel : 0f;
        var targetLevel = isTalking ? Mathf.Max(restingLevel, AmplitudeToMeterLevel(rawLevel)) : restingLevel;
        var speed = targetLevel > displayedLevel ? attackSpeed : releaseSpeed;
        var levelLerp = 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
        displayedLevel = Mathf.Lerp(displayedLevel, targetLevel, levelLerp);

        if (displayedLevel >= peakLevel)
        {
            peakLevel = displayedLevel;
        }
        else
        {
            var peakLerp = 1f - Mathf.Exp(-peakReleaseSpeed * Time.unscaledDeltaTime);
            peakLevel = Mathf.Lerp(peakLevel, displayedLevel, peakLerp);
        }

        ApplyLevel(displayedLevel);
        ApplyPeakLevel(peakLevel);
        ApplyColors(isTalking);

        if (canvasGroup != null)
        {
            var targetAlpha = isTalking ? 1f : 0.62f;
            var alphaLerp = 1f - Mathf.Exp(-alphaSmoothing * Time.unscaledDeltaTime);
            canvasGroup.alpha = Mathf.Lerp(canvasGroup.alpha, targetAlpha, alphaLerp);
        }
    }

    public static VoiceInputVolumeIndicator Create(VoiceChatController controller, RectTransform exitButton)
    {
        var parent = exitButton != null ? exitButton.parent : FindBestCanvasTransform();
        if (parent == null)
        {
            return null;
        }

        var rootObject = new GameObject(RootObjectName, typeof(RectTransform), typeof(CanvasGroup));
        rootObject.layer = parent.gameObject.layer;
        rootObject.transform.SetParent(parent, false);
        rootObject.transform.SetAsLastSibling();

        var indicator = rootObject.AddComponent<VoiceInputVolumeIndicator>();
        indicator.controller = controller;

        var rootRect = rootObject.GetComponent<RectTransform>();
        ConfigureRootRect(rootRect, exitButton);

        indicator.BuildUi(rootObject.layer);
        indicator.ApplyLevel(indicator.restingLevel);
        return indicator;
    }

    private void BuildUi(int layer)
    {
        var shadowObject = new GameObject("Glow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        shadowObject.layer = layer;
        shadowObject.transform.SetParent(transform, false);

        var shadowRect = shadowObject.GetComponent<RectTransform>();
        shadowRect.anchorMin = new Vector2(0f, 0f);
        shadowRect.anchorMax = new Vector2(0f, 0f);
        shadowRect.pivot = new Vector2(0.5f, 0f);
        shadowRect.anchoredPosition = new Vector2(10f, 0f);
        shadowRect.sizeDelta = new Vector2(meterWidth + 10f, meterHeight + 12f);

        var shadowImage = shadowObject.GetComponent<Image>();
        shadowImage.sprite = GetObsMeterSprite();
        shadowImage.color = new Color(1f, 1f, 1f, 0.14f);
        shadowImage.raycastTarget = false;

        var trackObject = new GameObject("Track", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        trackObject.layer = layer;
        trackObject.transform.SetParent(transform, false);

        var trackRect = trackObject.GetComponent<RectTransform>();
        trackRect.anchorMin = new Vector2(0f, 0f);
        trackRect.anchorMax = new Vector2(0f, 0f);
        trackRect.pivot = new Vector2(0.5f, 0f);
        trackRect.anchoredPosition = new Vector2(10f, 0f);
        trackRect.sizeDelta = new Vector2(meterWidth + 6f, meterHeight + 6f);

        var trackImage = trackObject.GetComponent<Image>();
        trackImage.sprite = GetRoundedSprite();
        trackImage.color = new Color(0.035f, 0.04f, 0.055f, 0.86f);
        trackImage.raycastTarget = false;

        var fullScaleObject = new GameObject("FullScaleTint", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fullScaleObject.layer = layer;
        fullScaleObject.transform.SetParent(trackObject.transform, false);

        var fullScaleRect = fullScaleObject.GetComponent<RectTransform>();
        fullScaleRect.anchorMin = new Vector2(0.5f, 0f);
        fullScaleRect.anchorMax = new Vector2(0.5f, 0f);
        fullScaleRect.pivot = new Vector2(0.5f, 0f);
        fullScaleRect.anchoredPosition = new Vector2(0f, 3f);
        fullScaleRect.sizeDelta = new Vector2(meterWidth, meterHeight);

        var fullScaleImage = fullScaleObject.GetComponent<Image>();
        fullScaleImage.sprite = GetObsMeterSprite();
        fullScaleImage.color = new Color(1f, 1f, 1f, 0.18f);
        fullScaleImage.raycastTarget = false;

        var maskObject = new GameObject("LevelMask", typeof(RectTransform), typeof(RectMask2D));
        maskObject.layer = layer;
        maskObject.transform.SetParent(trackObject.transform, false);

        levelMaskRect = maskObject.GetComponent<RectTransform>();
        levelMaskRect.anchorMin = new Vector2(0.5f, 0f);
        levelMaskRect.anchorMax = new Vector2(0.5f, 0f);
        levelMaskRect.pivot = new Vector2(0.5f, 0f);
        levelMaskRect.anchoredPosition = new Vector2(0f, 3f);
        levelMaskRect.sizeDelta = new Vector2(meterWidth, Mathf.Max(1f, restingLevel * meterHeight));

        var gradientObject = new GameObject("LevelGradient", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        gradientObject.layer = layer;
        gradientObject.transform.SetParent(maskObject.transform, false);

        levelGradientRect = gradientObject.GetComponent<RectTransform>();
        levelGradientRect.anchorMin = new Vector2(0.5f, 0f);
        levelGradientRect.anchorMax = new Vector2(0.5f, 0f);
        levelGradientRect.pivot = new Vector2(0.5f, 0f);
        levelGradientRect.anchoredPosition = Vector2.zero;
        levelGradientRect.sizeDelta = new Vector2(meterWidth, meterHeight);

        levelImage = gradientObject.GetComponent<Image>();
        levelImage.sprite = GetObsMeterSprite();
        levelImage.raycastTarget = false;

        CreateScaleTicks(trackObject.transform, layer);

        var peakObject = new GameObject("PeakMarker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        peakObject.layer = layer;
        peakObject.transform.SetParent(trackObject.transform, false);

        peakMarkerRect = peakObject.GetComponent<RectTransform>();
        peakMarkerRect.anchorMin = new Vector2(0.5f, 0f);
        peakMarkerRect.anchorMax = new Vector2(0.5f, 0f);
        peakMarkerRect.pivot = new Vector2(0.5f, 0.5f);
        peakMarkerRect.sizeDelta = new Vector2(meterWidth + 6f, 2f);

        peakMarkerImage = peakObject.GetComponent<Image>();
        peakMarkerImage.color = new Color(1f, 1f, 1f, 0.62f);
        peakMarkerImage.raycastTarget = false;
    }

    private void ApplyLevel(float level)
    {
        if (levelMaskRect == null)
        {
            return;
        }

        var size = levelMaskRect.sizeDelta;
        size.x = meterWidth;
        size.y = Mathf.Clamp01(level) * meterHeight;
        levelMaskRect.sizeDelta = size;
    }

    private void ApplyPeakLevel(float level)
    {
        if (peakMarkerRect == null)
        {
            return;
        }

        peakMarkerRect.anchoredPosition = new Vector2(0f, 3f + Mathf.Clamp01(level) * meterHeight);
    }

    private void ApplyColors(bool isTalking)
    {
        var activeAlpha = isTalking ? 0.98f : 0.5f;

        if (levelImage != null)
        {
            levelImage.color = new Color(1f, 1f, 1f, activeAlpha);
        }

        if (peakMarkerImage != null)
        {
            peakMarkerImage.color = new Color(1f, 1f, 1f, isTalking ? 0.72f : 0.26f);
        }
    }

    private static void ConfigureRootRect(RectTransform rootRect, RectTransform exitButton)
    {
        rootRect.sizeDelta = new Vector2(72f, 132f);
        rootRect.pivot = new Vector2(0.5f, 0f);

        if (exitButton == null)
        {
            rootRect.anchorMin = new Vector2(1f, 0f);
            rootRect.anchorMax = new Vector2(1f, 0f);
            rootRect.anchoredPosition = new Vector2(-72f, 82f);
            return;
        }

        rootRect.anchorMin = exitButton.anchorMin;
        rootRect.anchorMax = exitButton.anchorMax;
        var exitTopOffset = exitButton.rect.height * (1f - exitButton.pivot.y);
        rootRect.anchoredPosition = exitButton.anchoredPosition + new Vector2(0f, exitTopOffset + 18f);
    }

    private static Transform FindBestCanvasTransform()
    {
        var canvases = FindObjectsOfType<Canvas>();
        Canvas bestCanvas = null;

        for (var i = 0; i < canvases.Length; i++)
        {
            var canvas = canvases[i];
            if (canvas == null || canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                continue;
            }

            if (bestCanvas == null || canvas.sortingOrder > bestCanvas.sortingOrder)
            {
                bestCanvas = canvas;
            }
        }

        return bestCanvas != null ? bestCanvas.transform : null;
    }

    private void CreateScaleTicks(Transform trackParent, int layer)
    {
        for (var db = 0; db >= -60; db -= 6)
        {
            var level = DbToMeterLevel(db);
            var y = 3f + level * meterHeight;

            var tickObject = new GameObject("Tick_" + db + "dB", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            tickObject.layer = layer;
            tickObject.transform.SetParent(trackParent, false);

            var tickRect = tickObject.GetComponent<RectTransform>();
            tickRect.anchorMin = new Vector2(0.5f, 0f);
            tickRect.anchorMax = new Vector2(0.5f, 0f);
            tickRect.pivot = new Vector2(1f, 0.5f);
            tickRect.anchoredPosition = new Vector2(meterWidth * 0.5f + 4f, y);
            tickRect.sizeDelta = new Vector2(db % 12 == 0 ? 8f : 5f, 1f);

            var tickImage = tickObject.GetComponent<Image>();
            tickImage.color = new Color(0.86f, 0.88f, 0.9f, db % 12 == 0 ? 0.7f : 0.42f);
            tickImage.raycastTarget = false;

            var labelObject = new GameObject("Label_" + db + "dB", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelObject.layer = layer;
            labelObject.transform.SetParent(trackParent, false);

            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.5f, 0f);
            labelRect.anchorMax = new Vector2(0.5f, 0f);
            labelRect.pivot = new Vector2(0f, 0.5f);
            labelRect.anchoredPosition = new Vector2(meterWidth * 0.5f + 9f, y);
            labelRect.sizeDelta = new Vector2(labelWidth, 12f);

            var label = labelObject.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 10;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = new Color(0.88f, 0.9f, 0.94f, 0.86f);
            label.raycastTarget = false;
            label.text = db.ToString();
        }
    }

    private float AmplitudeToMeterLevel(float amplitude)
    {
        var adjusted = Mathf.Max(0f, amplitude - noiseFloor) * inputGain;
        if (adjusted <= 0.0001f)
        {
            return 0f;
        }

        var db = Mathf.Clamp(20f * Mathf.Log10(adjusted), silenceDb, 0f);
        return DbToMeterLevel(db);
    }

    private float DbToMeterLevel(float db)
    {
        return Mathf.InverseLerp(silenceDb, 0f, Mathf.Clamp(db, silenceDb, 0f));
    }

    private static Sprite GetRoundedSprite()
    {
        if (roundedSprite != null)
        {
            return roundedSprite;
        }

        const int textureWidth = 64;
        const int textureHeight = 24;
        const float radius = 10f;
        var texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
        texture.name = "Voice Input Volume Rounded";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        for (var y = 0; y < textureHeight; y++)
        {
            for (var x = 0; x < textureWidth; x++)
            {
                var px = Mathf.Abs(x + 0.5f - textureWidth * 0.5f) - (textureWidth * 0.5f - radius);
                var py = Mathf.Abs(y + 0.5f - textureHeight * 0.5f) - (textureHeight * 0.5f - radius);
                var outsideX = Mathf.Max(px, 0f);
                var outsideY = Mathf.Max(py, 0f);
                var outsideDistance = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY);
                var insideDistance = Mathf.Min(Mathf.Max(px, py), 0f);
                var distance = outsideDistance + insideDistance - radius;
                var alpha = Mathf.Clamp01(0.5f - distance);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        roundedSprite = Sprite.Create(texture, new Rect(0f, 0f, textureWidth, textureHeight), new Vector2(0.5f, 0.5f), 100f);
        roundedSprite.name = "Voice Input Volume Rounded Sprite";
        return roundedSprite;
    }

    private static Sprite GetObsMeterSprite()
    {
        if (meterSprite != null)
        {
            return meterSprite;
        }

        const int textureWidth = 8;
        const int textureHeight = 128;
        var texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
        texture.name = "Voice Input Volume OBS Meter";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        for (var y = 0; y < textureHeight; y++)
        {
            var t = y / (float)(textureHeight - 1);
            Color color;
            if (t < 0.68f)
            {
                color = Color.Lerp(new Color(0.1f, 0.88f, 0.22f, 1f), new Color(0.15f, 0.72f, 0.28f, 1f), t / 0.68f);
            }
            else if (t < 0.82f)
            {
                color = Color.Lerp(new Color(0.95f, 0.78f, 0.16f, 1f), new Color(0.98f, 0.62f, 0.12f, 1f), (t - 0.68f) / 0.14f);
            }
            else
            {
                color = Color.Lerp(new Color(0.9f, 0.18f, 0.18f, 1f), new Color(0.72f, 0.08f, 0.18f, 1f), (t - 0.82f) / 0.18f);
            }

            color.a = 1f;

            for (var x = 0; x < textureWidth; x++)
            {
                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply();
        meterSprite = Sprite.Create(texture, new Rect(0f, 0f, textureWidth, textureHeight), new Vector2(0.5f, 0f), 100f);
        meterSprite.name = "Voice Input Volume OBS Meter Sprite";
        return meterSprite;
    }
}

internal static class VoiceInputVolumeIndicatorBootstrap
{
    private const string MainSceneName = "main";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        TryInstall(SceneManager.GetActiveScene());
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TryInstall(scene);
    }

    private static void TryInstall(Scene scene)
    {
        if (UnityEngine.Object.FindObjectOfType<AdvancedSettingsPanel>(true) != null) return;
        if (!scene.IsValid() || !string.Equals(scene.name, MainSceneName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (GameObject.Find(VoiceInputVolumeIndicator.RootObjectName) != null)
        {
            return;
        }

        var controller = UnityEngine.Object.FindObjectOfType<VoiceChatController>();
        var exitButton = FindExitButton();

        if (controller == null && exitButton == null)
        {
            return;
        }

        VoiceInputVolumeIndicator.Create(controller, exitButton);
    }

    private static RectTransform FindExitButton()
    {
        var exitObject = GameObject.Find("Exit");
        return exitObject != null ? exitObject.GetComponent<RectTransform>() : null;
    }
}
