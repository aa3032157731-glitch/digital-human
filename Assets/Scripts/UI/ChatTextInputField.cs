using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class ChatTextInputField : InputField
{
    public System.Action SubmitMessage;
    private float lastCompositionTime = -10;
    private readonly Event keyEvent = new Event();

    protected override void LateUpdate()
    {
        base.LateUpdate();
        if (!string.IsNullOrEmpty(Input.compositionString)) lastCompositionTime = Time.unscaledTime;
    }

    public override void OnUpdateSelected(BaseEventData eventData)
    {
        if (!isFocused) return;
        while (Event.PopEvent(keyEvent))
        {
            if (keyEvent.rawType != EventType.KeyDown) continue;
            if (keyEvent.keyCode == KeyCode.Return || keyEvent.keyCode == KeyCode.KeypadEnter)
            {
                if (!string.IsNullOrEmpty(Input.compositionString) || Time.unscaledTime - lastCompositionTime < 0.15f)
                    continue;
                if (!keyEvent.shift) { SubmitMessage?.Invoke(); continue; }
            }
            if (KeyPressed(keyEvent) == EditState.Finish) DeactivateInputField();
            UpdateLabel();
        }
        eventData.Use();
    }
}
