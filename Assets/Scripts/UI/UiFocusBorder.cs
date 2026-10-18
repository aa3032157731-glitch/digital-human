using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Input field outline: neutral, darker on hover, accent while focused.
public sealed class UiFocusBorder : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public InputField input;
    public Graphic border;
    private bool over;

    public void OnPointerEnter(PointerEventData eventData) => over = true;
    public void OnPointerExit(PointerEventData eventData) => over = false;
    private void OnDisable() { over = false; Snap(); }
    private void Update() => Apply(1 - Mathf.Exp(-Time.unscaledDeltaTime * 24));
    public void Snap() => Apply(1);

    private void Apply(float blend)
    {
        if (border == null) return;
        bool focused = input != null && input.isFocused;
        var target = focused ? AssistantUi.Accent : over ? AssistantUi.Hex(0xC2BDB3) : AssistantUi.BorderStrong;
        border.color = Color.Lerp(border.color, target, blend);
    }
}
