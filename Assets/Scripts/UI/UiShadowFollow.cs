using UnityEngine;
using UnityEngine.UI;

// Keeps a soft-shadow sibling aligned with a panel whose size, position or visibility changes at runtime.
public sealed class UiShadowFollow : MonoBehaviour
{
    public RectTransform target;
    public float spread;
    public float offsetY;
    private Image image;

    private void LateUpdate() => Sync();

    public void Sync()
    {
        if (target == null) { AssistantUi.Remove(gameObject); return; }
        if (image == null) image = GetComponent<Image>();
        // Stay active ourselves (so LateUpdate keeps running) and only hide the image with the panel.
        bool visible = target.gameObject.activeSelf;
        if (image != null && image.enabled != visible) image.enabled = visible;
        var r = (RectTransform)transform;
        r.anchorMin = target.anchorMin; r.anchorMax = target.anchorMax; r.pivot = target.pivot;
        // Grow by `spread` on every side regardless of the panel's pivot, then drop slightly.
        var shift = new Vector2(spread * (2 * target.pivot.x - 1), spread * (2 * target.pivot.y - 1));
        r.anchoredPosition = target.anchoredPosition + shift + new Vector2(0, -offsetY);
        r.sizeDelta = target.sizeDelta + Vector2.one * spread * 2;
        r.localScale = target.localScale;
    }
}
