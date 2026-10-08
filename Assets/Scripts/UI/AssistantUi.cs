using System;
using UnityEngine;
using UnityEngine.UI;

public static class AssistantUi
{
    public static readonly Color Ink = new Color32(29, 48, 62, 255);
    public static readonly Color Muted = new Color32(100, 119, 130, 255);
    public static readonly Color Accent = new Color32(0, 126, 164, 255);
    public static readonly Color Selected = new Color32(224, 246, 251, 255);
    public static readonly Color Surface = new Color32(245, 248, 250, 255);
    private static Font font;
    private static Sprite rounded;
    public static Font Font => font != null ? font : (font = UnityEngine.Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial Unicode MS", "Arial" }, 18));
    public static RectTransform Rect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.layer = parent.gameObject.layer;
        var r = go.GetComponent<RectTransform>(); r.SetParent(parent, false); return r;
    }
    public static void Fill(RectTransform r, float left = 0, float bottom = 0, float right = 0, float top = 0)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(left, bottom); r.offsetMax = new Vector2(-right, -top);
    }
    public static LayoutElement Size(Component c, float height = -1, float width = -1, float flex = 0)
    {
        var e = c.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
        e.minHeight = e.preferredHeight = height; e.minWidth = e.preferredWidth = width; e.flexibleWidth = flex; return e;
    }
    public static Image Background(RectTransform r, Color color)
    {
        var i = r.gameObject.AddComponent<Image>(); i.color = color;
        if (rounded == null)
        {
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                float dx = Mathf.Max(8 - x, x - 23, 0), dy = Mathf.Max(8 - y, y - 23, 0);
                texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(8.5f - Mathf.Sqrt(dx * dx + dy * dy))));
            }
            texture.Apply(); rounded = Sprite.Create(texture, new Rect(0, 0, 32, 32), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(10, 10, 10, 10));
        }
        i.sprite = rounded; i.type = Image.Type.Sliced; return i;
    }
    public static RectTransform Stack(Transform parent, string name, bool horizontal = false, int padding = 0, float spacing = 12)
    {
        var r = Rect(parent, name);
        HorizontalOrVerticalLayoutGroup g = horizontal ? (HorizontalOrVerticalLayoutGroup)r.gameObject.AddComponent<HorizontalLayoutGroup>() : r.gameObject.AddComponent<VerticalLayoutGroup>();
        g.padding = new RectOffset(padding, padding, padding, padding); g.spacing = spacing;
        g.childControlWidth = g.childControlHeight = true; g.childForceExpandWidth = !horizontal; g.childForceExpandHeight = false;
        g.childAlignment = TextAnchor.UpperLeft; return r;
    }
    public static Text Label(Transform parent, string text, int size = 18, Color? color = null, float height = 30)
    {
        var r = Rect(parent, "Label"); var t = r.gameObject.AddComponent<Text>();
        t.font = Font; t.fontSize = size; t.color = color ?? Ink; t.text = text; t.supportRichText = false;
        t.alignment = TextAnchor.MiddleLeft; t.raycastTarget = false; t.horizontalOverflow = HorizontalWrapMode.Wrap;
        Size(t, height); return t;
    }
    public static Button Button(Transform parent, string text, Action action, bool selected = false, float height = 42)
    {
        var r = Rect(parent, text); Size(r, height, -1, 1);
        var bg = Background(r, selected ? Selected : Color.white);
        var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = bg;
        var colors = b.colors; colors.highlightedColor = new Color(.88f, .96f, .98f); colors.pressedColor = new Color(.75f, .9f, .95f); colors.selectedColor = colors.highlightedColor; b.colors = colors;
        var label = Label(r, text, 17, selected ? Accent : Ink); Fill(label.rectTransform, 12, 4, 12, 4); label.alignment = TextAnchor.MiddleCenter;
        b.onClick.AddListener(() => action?.Invoke()); return b;
    }
    public static Button Card(Transform parent, string title, string subtitle, Action action, bool selected = false)
    {
        var b = Button(parent, title, action, selected, 94);
        var t = b.GetComponentInChildren<Text>(); t.alignment = TextAnchor.UpperLeft; t.fontSize = 20;
        Fill(t.rectTransform, 18, 39, 18, 13);
        var sub = Label(b.transform, subtitle, 15, selected ? Accent : Muted); Fill(sub.rectTransform, 18, 10, 18, 47);
        return b;
    }
    public static InputField Input(Transform parent, string value, Action<string> change, bool secret = false, bool multiline = false, bool chat = false)
    {
        var r = Rect(parent, "Input"); Size(r, multiline ? 100 : 42);
        var bg = Background(r, Color.white); InputField input = chat ? (InputField)r.gameObject.AddComponent<ChatTextInputField>() : r.gameObject.AddComponent<InputField>(); input.targetGraphic = bg;
        var t = Label(r, "", 17); Fill(t.rectTransform, 12, 7, 12, 7); t.alignment = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
        input.textComponent = t; input.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
        input.contentType = secret ? InputField.ContentType.Password : InputField.ContentType.Standard;
        input.text = value ?? ""; input.onValueChanged.AddListener(v => change?.Invoke(v)); return input;
    }
    public static Toggle Toggle(Transform parent, string text, bool value, Action<bool> change)
    {
        var r = Rect(parent, text); Size(r, 36); var toggle = r.gameObject.AddComponent<Toggle>();
        var box = Rect(r, "Box"); box.anchorMin = box.anchorMax = new Vector2(0, .5f); box.pivot = new Vector2(0, .5f); box.sizeDelta = new Vector2(24, 24);
        var image = Background(box, Color.white); var mark = Rect(box, "Check"); Fill(mark, 5, 5, 5, 5); var graphic = Background(mark, Accent);
        toggle.targetGraphic = image; toggle.graphic = graphic; toggle.toggleTransition = UnityEngine.UI.Toggle.ToggleTransition.None; toggle.SetIsOnWithoutNotify(value);
        graphic.canvasRenderer.SetAlpha(value ? 1 : 0);
        var label = Label(r, text, 17); Fill(label.rectTransform, 36, 0, 0, 0); toggle.onValueChanged.AddListener(v => change(v)); return toggle;
    }
    public static RectTransform Scroll(Transform parent, string name, out ScrollRect scroll)
    {
        var root = Rect(parent, name); root.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, .01f);
        scroll = root.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.scrollSensitivity = 32; scroll.movementType = ScrollRect.MovementType.Clamped;
        var viewport = Rect(root, "Viewport"); Fill(viewport); viewport.gameObject.AddComponent<RectMask2D>();
        var content = Stack(viewport, "Content", false, 2); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1); content.sizeDelta = Vector2.zero;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport; scroll.content = content; return content;
    }
    public static void Clear(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i).gameObject; child.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(child); else UnityEngine.Object.DestroyImmediate(child);
        }
    }
    public static void Remove(GameObject target)
    {
        if (target == null) return;
        target.SetActive(false);
        if (Application.isPlaying) UnityEngine.Object.Destroy(target); else UnityEngine.Object.DestroyImmediate(target);
    }
}
