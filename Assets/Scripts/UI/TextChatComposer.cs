using UnityEngine;
using UnityEngine.UI;

public sealed class TextChatComposer : MonoBehaviour
{
    private VoiceChatController controller;
    private ChatTextInputField input;
    private Toggle readAloud;
    private Text status;
    private Button send;
    private Button stop;

    public void Build(VoiceChatMemoryPanel panel)
    {
        controller = panel.controller;
        var parent = panel.conversationScrollRect != null ? panel.conversationScrollRect.transform.parent : panel.conversationText.transform.parent;
        foreach (RectTransform child in parent)
        {
            if (child.anchoredPosition.y <= -350) child.anchoredPosition += new Vector2(0, -142);
        }
        panel.expandedHeight += 142;
        var root = ChatUiFactory.Rect(parent, "TextChatComposer", 20, 350, 520, 132);
        input = (ChatTextInputField)ChatUiFactory.Input(root, "输入消息 · Enter 发送，Shift+Enter 换行", 0, 0, 520, 62, true);
        input.characterLimit = 16000;
        input.SubmitMessage = Send;
        send = ChatUiFactory.Button(root, "发送", 0, 70, 80, Send);
        stop = ChatUiFactory.Button(root, "停止", 88, 70, 80, () => controller.InterruptAssistantOutput());
        readAloud = ChatUiFactory.Toggle(root, "朗读回复", 184, 71, 150);
        readAloud.SetIsOnWithoutNotify(controller.readRepliesAloud);
        readAloud.onValueChanged.AddListener(controller.SetReadRepliesAloud);
        status = ChatUiFactory.Label(root, "就绪", 0, 105, 520, 25, 14);
        if (panel.conversationText != null) panel.conversationText.supportRichText = false;
    }

    private void Send()
    {
        if (controller != null && controller.SubmitTextMessage(input.text))
        {
            input.text = "";
            input.ActivateInputField();
        }
    }

    private void Update()
    {
        if (input == null || controller == null) return;
        send.interactable = !string.IsNullOrWhiteSpace(input.text);
        stop.interactable = controller.IsGenerating || controller.isSpeaking;
        readAloud.SetIsOnWithoutNotify(controller.readRepliesAloud);
        status.text = controller.ChatStatus;
    }
}
