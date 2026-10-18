using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum ButtonKind { Primary, Secondary, Ghost, Soft, Danger }
public enum UiTone { Neutral, Accent, Success, Warning, Danger }

// Design system for the code-built assistant UI: warm neutral surfaces, one calm green accent,
// soft rounded shapes, a clear type hierarchy and generous spacing.
public static class AssistantUi
{
    public static readonly Color Backdrop = new Color32(18, 24, 21, 115);
    public static readonly Color Canvas = Hex(0xF5F4F0);
    public static readonly Color Surface = Color.white;
    public static readonly Color Subtle = Hex(0xF1EFEA);
    public static readonly Color Hover = Hex(0xE9E6DF);
    public static readonly Color Border = Hex(0xE6E3DC);
    public static readonly Color BorderStrong = Hex(0xD6D2C9);
    public static readonly Color Ink = Hex(0x1E2421);
    public static readonly Color Muted = Hex(0x6A716C);
    public static readonly Color Faint = Hex(0xA0A6A2);
    public static readonly Color Accent = Hex(0x2E7D64);
    public static readonly Color AccentHover = Hex(0x276B56);
    public static readonly Color AccentPressed = Hex(0x215A48);
    public static readonly Color AccentSoft = Hex(0xE6F1EC);
    public static readonly Color AccentSoftHover = Hex(0xD9EAE2);
    public static readonly Color AccentInk = Hex(0x1F5E4A);
    public static readonly Color Success = Hex(0x2F8A5B), SuccessSoft = Hex(0xE5F3EA);
    public static readonly Color Warning = Hex(0xB07719), WarningSoft = Hex(0xFAF0DC);
    public static readonly Color Danger = Hex(0xC2473A), DangerSoft = Hex(0xFBEAE6);
    public const float RadiusLarge = 18, Radius = 12, RadiusSmall = 9;

    private static Font font;
    private static readonly Dictionary<long, Sprite> shapes = new Dictionary<long, Sprite>();
    public static Font Font => font != null ? font : (font = UnityEngine.Font.CreateDynamicFontFromOSFont(
        new[] { "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "Segoe UI", "Arial Unicode MS", "Arial" }, 16));

    public static Color Hex(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
    public static Color Alpha(Color c, float a) { c.a = a; return c; }
    public static Color ToneColor(UiTone tone) => tone == UiTone.Accent ? Accent : tone == UiTone.Success ? Success : tone == UiTone.Warning ? Warning : tone == UiTone.Danger ? Danger : Faint;
    public static Color ToneSoft(UiTone tone) => tone == UiTone.Accent ? AccentSoft : tone == UiTone.Success ? SuccessSoft : tone == UiTone.Warning ? WarningSoft : tone == UiTone.Danger ? DangerSoft : Subtle;
    public static Color ToneInk(UiTone tone) => tone == UiTone.Accent ? AccentInk : tone == UiTone.Success ? Hex(0x23704A) : tone == UiTone.Warning ? Hex(0x8A5C10) : tone == UiTone.Danger ? Hex(0xA33A2F) : Muted;

    // ---------- layout ----------

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
        var e = c.GetComponent<LayoutElement>(); if (e == null) e = c.gameObject.AddComponent<LayoutElement>();
        e.minHeight = e.preferredHeight = height; e.minWidth = e.preferredWidth = width; e.flexibleWidth = flex; return e;
    }
    public static RectTransform Stack(Transform parent, string name, bool horizontal = false, int padding = 0, float spacing = 12)
    {
        var r = Rect(parent, name);
        HorizontalOrVerticalLayoutGroup g = horizontal ? (HorizontalOrVerticalLayoutGroup)r.gameObject.AddComponent<HorizontalLayoutGroup>() : r.gameObject.AddComponent<VerticalLayoutGroup>();
        g.padding = new RectOffset(padding, padding, padding, padding); g.spacing = spacing;
        g.childControlWidth = g.childControlHeight = true; g.childForceExpandWidth = !horizontal; g.childForceExpandHeight = false;
        g.childAlignment = horizontal ? TextAnchor.MiddleLeft : TextAnchor.UpperLeft; return r;
    }
    public static void Pad(RectTransform stack, int left, int right, int top, int bottom) =>
        stack.GetComponent<HorizontalOrVerticalLayoutGroup>().padding = new RectOffset(left, right, top, bottom);
    // Horizontal row whose children keep their preferred widths (chips, button groups).
    public static RectTransform Row(Transform parent, string name = "Row", float spacing = 8, TextAnchor align = TextAnchor.MiddleLeft)
    {
        var r = Stack(parent, name, true, 0, spacing); r.GetComponent<HorizontalLayoutGroup>().childAlignment = align; return r;
    }
    public static RectTransform Spacer(Transform parent, float height = 8)
    {
        var r = Rect(parent, "Spacer"); Size(r, height); return r;
    }
    public static LayoutElement Flexible(Transform parent)
    {
        var e = Rect(parent, "Flexible").gameObject.AddComponent<LayoutElement>(); e.flexibleWidth = 1; return e;
    }
    public static Image Divider(Transform parent)
    {
        var r = Rect(parent, "Divider"); Size(r, 1); var i = r.gameObject.AddComponent<Image>(); i.color = Border; i.raycastTarget = false; return i;
    }

    // ---------- shapes ----------

    // Anti-aliased rounded rectangle (stroke > 0 draws only the outline), generated at 2x for HiDPI screens.
    public static Sprite Shape(float radius, float stroke = 0)
    {
        long key = ((long)Mathf.RoundToInt(radius * 10) << 20) | (long)Mathf.RoundToInt(stroke * 10);
        if (shapes.TryGetValue(key, out var cached) && cached != null) return cached;
        const int scale = 2;
        int r = Mathf.Max(1, Mathf.RoundToInt(radius * scale)), size = r * 2 + 6;
        float half = size / 2f, inner = half - r, width = stroke * scale;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Abs(x + .5f - half) - inner, qy = Mathf.Abs(y + .5f - half) - inner;
                float d = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
                float a = Mathf.Clamp01(.5f - d);
                if (width > 0) a *= Mathf.Clamp01(d + width + .5f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        return shapes[key] = MakeSprite(pixels, size, r + 2);
    }
    // Soft drop shadow; the sprite extends `blur` beyond the panel on every side.
    public static Sprite SoftShadow(float radius, float blur)
    {
        long key = -(((long)Mathf.RoundToInt(radius * 10) << 20) | (long)Mathf.RoundToInt(blur * 10));
        if (shapes.TryGetValue(key, out var cached) && cached != null) return cached;
        const int scale = 2;
        int r = Mathf.RoundToInt(radius * scale), b = Mathf.RoundToInt(blur * scale), size = (r + b) * 2 + 6;
        float half = size / 2f, inner = half - b - r;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Abs(x + .5f - half) - inner, qy = Mathf.Abs(y + .5f - half) - inner;
                float d = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
                float t = Mathf.Clamp01((d + b * .35f) / (b * 1.35f));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Pow(1 - t, 2.2f) * 255));
            }
        return shapes[key] = MakeSprite(pixels, size, r + b + 2);
    }
    private static Sprite MakeSprite(Color32[] pixels, int size, float border)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixels32(pixels); texture.Apply(false, true);
        var sprite = Sprite.Create(texture, new UnityEngine.Rect(0, 0, size, size), new Vector2(.5f, .5f), 200, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        sprite.hideFlags = HideFlags.HideAndDontSave; return sprite;
    }
    public static Image Box(RectTransform r, Color fill, float radius = Radius)
    {
        var i = r.GetComponent<Image>(); if (i == null) i = r.gameObject.AddComponent<Image>();
        i.sprite = Shape(radius); i.type = Image.Type.Sliced; i.color = fill; return i;
    }
    // Outline as a layout-ignored first child: drawn above the fill, below the content.
    public static Image Outline(RectTransform r, Color color, float radius = Radius, float width = 1)
    {
        var o = Rect(r, "Outline"); Fill(o); o.SetAsFirstSibling();
        o.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var i = o.gameObject.AddComponent<Image>(); i.sprite = Shape(radius, width); i.type = Image.Type.Sliced; i.color = color; i.raycastTarget = false; return i;
    }
    public static Image Shadow(RectTransform target, float radius, float blur = 26, float alpha = .14f, float offsetY = 8)
    {
        var shadow = Rect(target.parent, target.name + "Shadow");
        shadow.SetSiblingIndex(target.GetSiblingIndex());
        shadow.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var image = shadow.gameObject.AddComponent<Image>(); image.sprite = SoftShadow(radius, blur); image.type = Image.Type.Sliced;
        image.color = new Color(.06f, .09f, .07f, alpha); image.raycastTarget = false;
        var follow = shadow.gameObject.AddComponent<UiShadowFollow>(); follow.target = target; follow.spread = blur; follow.offsetY = offsetY; follow.Sync();
        return image;
    }

    // ---------- text ----------

    public static Text Label(Transform parent, string text, int size = 15, Color? color = null, float height = -1, bool bold = false)
    {
        var r = Rect(parent, "Label"); var t = r.gameObject.AddComponent<Text>();
        t.font = Font; t.fontSize = size; t.color = color ?? Ink; t.text = text; t.supportRichText = false;
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal; t.lineSpacing = 1.1f;
        t.alignment = TextAnchor.MiddleLeft; t.raycastTarget = false; t.horizontalOverflow = HorizontalWrapMode.Wrap;
        if (height > 0) Size(t, height);
        return t;
    }
    public static Text Title(Transform parent, string text, int size = 17) => Label(parent, text, size, Ink, -1, true);
    public static Text Caption(Transform parent, string text, int size = 13)
    {
        var t = Label(parent, text, size, Muted); t.alignment = TextAnchor.UpperLeft; return t;
    }

    // ---------- controls ----------

    public static Button Button(Transform parent, string text, Action action, ButtonKind kind = ButtonKind.Secondary, float height = 40, float width = -1)
    {
        var r = Rect(parent, text);
        float radius = height >= 44 ? Radius : RadiusSmall;
        var fill = Box(r, Color.white, radius);
        var outline = kind == ButtonKind.Secondary || kind == ButtonKind.Danger ? Outline(r, BorderStrong, radius) : null;
        var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = fill; b.transition = Selectable.Transition.None;
        var label = Label(r, text, height < 34 ? 13 : 15, Ink, -1, kind == ButtonKind.Primary); Fill(label.rectTransform, 12, 0, 12, 0);
        label.alignment = TextAnchor.MiddleCenter; label.horizontalOverflow = HorizontalWrapMode.Overflow;
        var layout = Size(r, height, width);
        if (width < 0) { layout.minWidth = Mathf.Min(72, label.preferredWidth + 28); layout.preferredWidth = label.preferredWidth + 32; }
        Style(b.gameObject, kind, fill, outline, label);
        b.onClick.AddListener(() => action?.Invoke()); return b;
    }
    public static UiPointerStyle Style(GameObject target, ButtonKind kind, Graphic fill, Graphic outline, Graphic label)
    {
        var s = target.GetComponent<UiPointerStyle>(); if (s == null) s = target.AddComponent<UiPointerStyle>();
        s.fill = fill; s.border = outline; s.label = label;
        switch (kind)
        {
            case ButtonKind.Primary: s.Set(Accent, AccentHover, AccentPressed, Hex(0xB8D2C7), Color.white, Alpha(Color.white, .92f)); break;
            case ButtonKind.Soft: s.Set(AccentSoft, AccentSoftHover, Hex(0xCBE1D7), Subtle, AccentInk, Faint); break;
            case ButtonKind.Ghost: s.Set(Alpha(Subtle, 0), Subtle, Hover, Alpha(Subtle, 0), Ink, Faint); break;
            case ButtonKind.Danger: s.Set(Color.white, DangerSoft, Hex(0xF6D9D3), Color.white, Danger, Faint); s.SetBorder(Hex(0xEBCBC5), Hex(0xE2B3AA)); break;
            default: s.Set(Color.white, Subtle, Hover, Color.white, Ink, Faint); s.SetBorder(BorderStrong, Hex(0xC4BFB5)); break;
        }
        s.Snap(); return s;
    }
    public static Button IconButton(Transform parent, string glyph, Action action, float size = 36, int fontSize = 20)
    {
        var b = Button(parent, glyph, action, ButtonKind.Ghost, size, size);
        var label = b.GetComponentInChildren<Text>(); label.fontSize = fontSize; Fill(label.rectTransform);
        b.GetComponent<Image>().sprite = Shape(size / 2); return b;
    }
    // Compact text button, e.g. "‹ 服务来源" or "管理服务账号 ›".
    public static Button Link(Transform parent, string text, Action action, Color? color = null)
    {
        var b = Button(parent, text, action, ButtonKind.Ghost, 30);
        var label = b.GetComponentInChildren<Text>(); label.fontSize = 13; Fill(label.rectTransform, 8, 0, 8, 0);
        var style = b.GetComponent<UiPointerStyle>(); style.labelNormal = color ?? AccentInk; style.Snap();
        var e = b.GetComponent<LayoutElement>(); e.flexibleWidth = 0; e.minWidth = e.preferredWidth = label.preferredWidth + 18; return b;
    }
    // Clickable card with title and description; callers may append a chip row.
    public static Button Card(Transform parent, string title, string subtitle, Action action, bool selected = false, float minHeight = 88)
    {
        var r = Stack(parent, title, false, 0, 6); Pad(r, 18, 18, 15, 15);
        var e = r.gameObject.AddComponent<LayoutElement>(); e.minHeight = minHeight; e.flexibleWidth = 1;
        var fill = Box(r, Color.white, 14); var outline = Outline(r, selected ? Accent : Border, 14, selected ? 1.6f : 1);
        var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = fill; b.transition = Selectable.Transition.None;
        var t = Title(r, title, 16); t.color = selected ? AccentInk : Ink;
        if (!string.IsNullOrEmpty(subtitle)) Caption(r, subtitle);
        var s = Style(r.gameObject, ButtonKind.Secondary, fill, outline, null);
        if (selected) { s.Set(Hex(0xF3F9F6), AccentSoft, AccentSoftHover, Hex(0xF3F9F6), AccentInk, Faint); s.SetBorder(Accent, Accent); }
        else s.SetBorder(Border, BorderStrong);
        s.Snap(); b.onClick.AddListener(() => action?.Invoke()); return b;
    }
    // Small pill with a status dot, e.g. "已配置" or "连接正常".
    public static RectTransform Chip(Transform parent, string text, UiTone tone)
    {
        var r = Row(parent, "Chip", 6); Pad(r, 9, 10, 3, 3); Box(r, ToneSoft(tone), 11).raycastTarget = false;
        var e = r.gameObject.AddComponent<LayoutElement>(); e.minHeight = e.preferredHeight = 24;
        Dot(r, ToneColor(tone), 7);
        var label = Label(r, text, 12, ToneInk(tone)); label.horizontalOverflow = HorizontalWrapMode.Overflow;
        return r;
    }
    public static RectTransform Dot(Transform parent, Color color, float size = 8)
    {
        var dot = Rect(parent, "Dot"); Size(dot, size, size); Box(dot, color, size / 2).raycastTarget = false; return dot;
    }
    public static InputField Input(Transform parent, string value, Action<string> change, bool secret = false, bool multiline = false, bool chat = false, string placeholder = null)
    {
        var r = Rect(parent, "Input"); Size(r, multiline ? 96 : 40);
        var fill = Box(r, Color.white, RadiusSmall + 1); var outline = Outline(r, BorderStrong, RadiusSmall + 1);
        InputField input = chat ? (InputField)r.gameObject.AddComponent<ChatTextInputField>() : r.gameObject.AddComponent<InputField>();
        input.targetGraphic = fill; input.transition = Selectable.Transition.None;
        var t = Label(r, "", 15); Fill(t.rectTransform, 13, 8, 13, 8); t.alignment = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
        input.textComponent = t; input.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
        input.contentType = secret ? InputField.ContentType.Password : InputField.ContentType.Standard;
        input.caretWidth = 2; input.customCaretColor = true; input.caretColor = Accent; input.selectionColor = Alpha(Accent, .22f);
        if (!string.IsNullOrEmpty(placeholder))
        {
            var hint = Label(r, placeholder, 14, Faint); Fill(hint.rectTransform, 13, 8, 13, 8);
            hint.alignment = t.alignment; input.placeholder = hint;
        }
        var focus = r.gameObject.AddComponent<UiFocusBorder>(); focus.input = input; focus.border = outline; focus.Snap();
        input.text = value ?? ""; input.onValueChanged.AddListener(v => change?.Invoke(v)); return input;
    }
    // Settings row: label (and optional hint) on the left, animated switch on the right.
    public static Toggle Toggle(Transform parent, string text, bool value, Action<bool> change, string hint = null)
    {
        var r = Row(parent, text, 12); r.gameObject.AddComponent<LayoutElement>().minHeight = 38;
        r.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0);
        var labels = Stack(r, "Text", false, 0, 4); labels.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        Label(labels, text, 15);
        if (!string.IsNullOrEmpty(hint)) Caption(labels, hint);
        var toggle = r.gameObject.AddComponent<Toggle>(); toggle.transition = Selectable.Transition.None;
        var sw = Switch(r, toggle); toggle.targetGraphic = sw.track;
        toggle.SetIsOnWithoutNotify(value); sw.Snap();
        if (change != null) toggle.onValueChanged.AddListener(v => change(v));
        return toggle;
    }
    public static UiSwitch Switch(Transform parent, Toggle toggle)
    {
        var track = Rect(parent, "Switch"); Size(track, 24, 42);
        var trackImage = Box(track, BorderStrong, 12);
        var knob = Rect(track, "Knob"); knob.anchorMin = knob.anchorMax = knob.pivot = new Vector2(0, .5f); knob.sizeDelta = new Vector2(18, 18);
        Box(knob, Color.white, 9).raycastTarget = false;
        var sw = track.gameObject.AddComponent<UiSwitch>(); sw.toggle = toggle; sw.track = trackImage; sw.knob = knob; return sw;
    }
    // Segmented control: a soft track with one raised selected item; items share the width equally.
    public static RectTransform Segmented(Transform parent, IList<string> items, string selected, Action<string> select, float height = 38)
    {
        var r = Stack(parent, "Segmented", true, 3, 3); Box(r, Subtle, RadiusSmall + 2); Size(r, height);
        foreach (var item in items)
        {
            var name = item; bool on = item == selected;
            var b = Button(r, item, () => select(name), ButtonKind.Ghost, height - 6);
            var e = b.GetComponent<LayoutElement>(); e.minWidth = 0; e.preferredWidth = 0; e.flexibleWidth = 1;
            var style = b.GetComponent<UiPointerStyle>(); var label = b.GetComponentInChildren<Text>();
            if (on)
            {
                style.border = Outline((RectTransform)b.transform, Border, RadiusSmall);
                style.Set(Color.white, Color.white, Color.white, Color.white, Ink, Faint); style.SetBorder(Border, Border);
                label.fontStyle = FontStyle.Bold;
            }
            else style.Set(Alpha(Subtle, 0), Hover, BorderStrong, Alpha(Subtle, 0), Muted, Faint);
            style.Snap();
        }
        return r;
    }
    // White card with a title and optional description; returns the card (a vertical stack).
    public static RectTransform Section(Transform parent, string title, string description = null)
    {
        var box = Stack(parent, string.IsNullOrEmpty(title) ? "Section" : title, false, 0, 12); Pad(box, 22, 22, 18, 20);
        Box(box, Color.white, 14); Outline(box, Border, 14);
        if (!string.IsNullOrEmpty(title))
        {
            var head = Stack(box, "Header", false, 0, 6);
            Title(head, title, 16);
            if (!string.IsNullOrEmpty(description)) Caption(head, description);
        }
        return box;
    }
    public static RectTransform Scroll(Transform parent, string name, out ScrollRect scroll)
    {
        var root = Rect(parent, name); root.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0);
        scroll = root.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.scrollSensitivity = 36; scroll.movementType = ScrollRect.MovementType.Clamped;
        var viewport = Rect(root, "Viewport"); Fill(viewport); viewport.gameObject.AddComponent<RectMask2D>();
        var content = Stack(viewport, "Content", false, 0); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1); content.sizeDelta = Vector2.zero;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport; scroll.content = content;
        var bar = Rect(root, "Scrollbar"); bar.anchorMin = new Vector2(1, 0); bar.anchorMax = Vector2.one; bar.pivot = new Vector2(1, .5f); bar.sizeDelta = new Vector2(5, 0); bar.anchoredPosition = new Vector2(4, 0);
        var area = Rect(bar, "Sliding"); Fill(area);
        var handle = Rect(area, "Handle"); Fill(handle); var handleImage = Box(handle, Alpha(Muted, .3f), 2.5f);
        var scrollbar = bar.gameObject.AddComponent<Scrollbar>(); scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect = handle; scrollbar.targetGraphic = handleImage; scrollbar.transition = Selectable.Transition.None;
        scroll.verticalScrollbar = scrollbar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        return content;
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
    // Restyles a button that lives in the scene (outside the code-built UI) to match the design system.
    public static void Restyle(Button button, ButtonKind kind = ButtonKind.Secondary, int fontSize = 15)
    {
        if (button == null) return;
        var image = button.targetGraphic as Image;
        if (image == null) image = button.GetComponent<Image>();
        if (image == null) image = button.gameObject.AddComponent<Image>();
        image.sprite = Shape(Radius); image.type = Image.Type.Sliced; button.targetGraphic = image; button.transition = Selectable.Transition.None;
        // A previous ColorTint transition leaves its tint on the CanvasRenderer; clear it.
        image.canvasRenderer.SetColor(Color.white);
        Image outline = null;
        if (kind == ButtonKind.Secondary || kind == ButtonKind.Danger)
        {
            var existing = button.transform.Find("Outline");
            outline = existing != null ? existing.GetComponent<Image>() : Outline((RectTransform)button.transform, BorderStrong, Radius);
        }
        var label = button.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            label.font = Font; label.fontSize = fontSize; label.alignment = TextAnchor.MiddleCenter; label.resizeTextForBestFit = false;
            label.fontStyle = kind == ButtonKind.Primary ? FontStyle.Bold : FontStyle.Normal;
        }
        Style(button.gameObject, kind, image, outline, label);
    }
}
