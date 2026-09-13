using UnityEngine;
using UnityEngine.UI;

public static class ChatUiFactory
{
    public static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    public static Text Label(Transform parent, string value, float x, float y, float width, float height, int size = 16)
    {
        var text = Rect(parent, "Label", x, y, width, height).gameObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.text = value;
        text.color = new Color(0.94f, 0.96f, 0.98f);
        text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false;
        text.supportRichText = false;
        return text;
    }

    public static Button Button(Transform parent, string value, float x, float y, float width, UnityEngine.Events.UnityAction action)
    {
        var rect = Rect(parent, value, x, y, width, 32);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.20f, 0.36f, 0.46f, 1);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        Label(rect, value, 2, 0, width - 4, 32).alignment = TextAnchor.MiddleCenter;
        button.onClick.AddListener(action);
        return button;
    }

    public static InputField Input(Transform parent, string name, float x, float y, float width, float height = 32, bool chat = false)
    {
        var rect = Rect(parent, name, x, y, width, height);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.97f, 0.98f, 1f);
        var input = chat ? rect.gameObject.AddComponent<ChatTextInputField>() : rect.gameObject.AddComponent<InputField>();
        input.targetGraphic = image;
        var text = Label(rect, "", 8, 4, width - 16, height - 8);
        text.color = new Color(0.07f, 0.12f, 0.17f);
        input.textComponent = text;
        var placeholder = Label(rect, name, 8, 4, width - 16, height - 8, 14);
        placeholder.color = new Color(0.4f, 0.44f, 0.48f);
        input.placeholder = placeholder;
        input.lineType = height > 40 ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
        if (height > 40) text.alignment = placeholder.alignment = TextAnchor.UpperLeft;
        return input;
    }

    public static Toggle Toggle(Transform parent, string label, float x, float y, float width)
    {
        var rect = Rect(parent, label, x, y, width, 30);
        var toggle = rect.gameObject.AddComponent<Toggle>();
        var box = Rect(rect, "Box", 0, 4, 22, 22).gameObject.AddComponent<Image>();
        box.color = new Color(0.15f, 0.22f, 0.28f);
        var mark = Rect(box.transform, "Check", 4, 4, 14, 14).gameObject.AddComponent<Image>();
        mark.color = new Color(0.6f, 0.92f, 0.5f);
        toggle.targetGraphic = box;
        toggle.graphic = mark;
        Label(rect, label, 30, 0, width - 30, 30);
        return toggle;
    }
}
