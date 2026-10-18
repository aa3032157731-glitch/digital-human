using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Smooth hover / press / disabled colours for code-built controls. uGUI's ColorTint multiplies the base
// colour, which cannot light up transparent ghost buttons or recolour outlines and labels.
public sealed class UiPointerStyle : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public Graphic fill, border, label;
    public Color normal = Color.white, hover = Color.white, pressed = Color.white, disabled = Color.white;
    public Color borderNormal, borderHover;
    public Color labelNormal = Color.black, labelDisabled = Color.gray;
    private Selectable selectable;
    private bool over, down;

    public void Set(Color normal, Color hover, Color pressed, Color disabled, Color label, Color labelDisabled)
    {
        this.normal = normal; this.hover = hover; this.pressed = pressed; this.disabled = disabled;
        labelNormal = label; this.labelDisabled = labelDisabled;
    }
    public void SetBorder(Color normal, Color hover) { borderNormal = normal; borderHover = hover; }

    public void OnPointerEnter(PointerEventData eventData) => over = true;
    public void OnPointerExit(PointerEventData eventData) { over = false; down = false; }
    public void OnPointerDown(PointerEventData eventData) { if (eventData.button == PointerEventData.InputButton.Left) down = true; }
    public void OnPointerUp(PointerEventData eventData) => down = false;

    private void OnDisable() { over = down = false; Snap(); }
    private void Update() => Apply(1 - Mathf.Exp(-Time.unscaledDeltaTime * 24));
    public void Snap() => Apply(1);

    private void Apply(float blend)
    {
        if (selectable == null) selectable = GetComponent<Selectable>();
        bool interactable = selectable == null || selectable.IsInteractable();
        var target = !interactable ? disabled : down ? pressed : over ? hover : normal;
        if (fill != null) fill.color = Color.Lerp(fill.color, target, blend);
        if (border != null) border.color = Color.Lerp(border.color, over && interactable ? borderHover : borderNormal, blend);
        if (label != null) label.color = Color.Lerp(label.color, interactable ? labelNormal : labelDisabled, blend);
    }
}
