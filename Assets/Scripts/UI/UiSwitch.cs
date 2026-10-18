using UnityEngine;
using UnityEngine.UI;

// Pill switch drawn for a Toggle: the knob slides and the track fades to the accent colour.
public sealed class UiSwitch : MonoBehaviour
{
    public Toggle toggle;
    public Image track;
    public RectTransform knob;
    private float position;

    private void Update()
    {
        if (toggle == null) return;
        position = Mathf.MoveTowards(position, toggle.isOn ? 1 : 0, Time.unscaledDeltaTime * 7);
        Render();
    }
    public void Snap() { if (toggle != null) position = toggle.isOn ? 1 : 0; Render(); }

    private void Render()
    {
        if (track == null || knob == null) return;
        float eased = position * position * (3 - 2 * position);
        var color = Color.Lerp(AssistantUi.BorderStrong, AssistantUi.Accent, eased);
        if (toggle != null && !toggle.IsInteractable()) color.a = .45f;
        track.color = color;
        // Anchor-relative, so it is correct before layout has sized the track.
        knob.anchorMin = knob.anchorMax = knob.pivot = new Vector2(eased, .5f);
        knob.anchoredPosition = new Vector2(Mathf.Lerp(3, -3, eased), 0);
    }
}
