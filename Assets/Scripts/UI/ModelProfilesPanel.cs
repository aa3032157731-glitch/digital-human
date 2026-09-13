using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

public sealed class ModelProfilesPanel : MonoBehaviour
{
    private RuntimeVoiceChatSettings settings;
    private int selected;
    private bool loading;
    private Text selection, endpoint, result;
    private InputField title, model, uri, key, temperature, topP, tokens, prompt, history;
    private Toggle tools, sampling, limit, completionTokens, thinking;
    private RectTransform list;
    private Button test;
    private CancellationTokenSource testCancellation;
    private Action save;

    public void Build(Action saveAction)
    {
        save = saveAction;
        foreach (Transform child in transform) child.gameObject.SetActive(false);
        var viewport = ChatUiFactory.Rect(transform, "ModelProfilesViewport", 0, 0, 900, 480);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
        viewport.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.12f, 0.16f, 0.98f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        var content = ChatUiFactory.Rect(viewport, "ModelProfilesContent", 12, 8, 820, 850);
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30;
        selection = ChatUiFactory.Label(content, "模型配置", 0, 0, 760, 32, 20);
        list = ChatUiFactory.Rect(content, "ProfileList", 0, 40, 210, 740);
        ChatUiFactory.Button(content, "+ DeepSeek", 224, 42, 140, () => Add("DeepSeek"));
        ChatUiFactory.Button(content, "+ 火山方舟", 372, 42, 140, () => Add("Ark"));
        ChatUiFactory.Button(content, "+ 兼容接口", 520, 42, 140, () => Add("Compatible"));
        title = Field(content, "配置名称（可选）", 84);
        model = Field(content, "模型名称 / 接入点 ID", 126);
        uri = Field(content, "URI（基础或完整地址）", 168);
        key = Field(content, "API 密钥", 210);
        key.contentType = InputField.ContentType.Password;
        endpoint = ChatUiFactory.Label(content, "", 224, 250, 560, 48, 13);
        ChatUiFactory.Button(content, "保存并启用", 224, 306, 130, SaveAndActivate);
        test = ChatUiFactory.Button(content, "测试连接", 362, 306, 110, TestConnection);
        ChatUiFactory.Button(content, "删除配置", 480, 306, 110, Delete);
        result = ChatUiFactory.Label(content, "切换后沿用当前对话；连接测试不发送历史记录。", 224, 346, 560, 44, 14);
        tools = ChatUiFactory.Toggle(content, "启用工具调用", 224, 398, 220);
        sampling = ChatUiFactory.Toggle(content, "发送采样参数", 454, 398, 220);
        temperature = Field(content, "Temperature / 温度", 436);
        topP = Field(content, "Top P", 478);
        limit = ChatUiFactory.Toggle(content, "限制输出长度", 224, 520, 220);
        completionTokens = ChatUiFactory.Toggle(content, "max_completion_tokens", 454, 520, 330);
        tokens = Field(content, "最大输出 token 数", 556);
        thinking = ChatUiFactory.Toggle(content, "DeepSeek：关闭思考", 224, 598, 330);
        history = Field(content, "保留历史消息数（共用）", 636);
        ChatUiFactory.Label(content, "系统提示词（所有配置共用）", 224, 680, 560, 28);
        prompt = ChatUiFactory.Input(content, "系统提示词", 224, 712, 560, 100);
        uri.onValueChanged.AddListener(_ => UpdateEndpoint());
    }

    private static InputField Field(Transform parent, string label, float y)
    {
        ChatUiFactory.Label(parent, label, 224, y, 210, 32, 14);
        return ChatUiFactory.Input(parent, label, 442, y, 342);
    }

    public void Load(RuntimeVoiceChatSettings value)
    {
        settings = value;
        settings.EnsureDefaults();
        selected = Math.Max(0, settings.modelProfiles.FindIndex(p => p.id == settings.activeModelProfileId));
        ShowSelected();
    }

    public bool Pull()
    {
        if (settings == null || loading) return true;
        var p = settings.modelProfiles[selected];
        if ((sampling.isOn && (!float.TryParse(temperature.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) || t < 0 || t > 2
            || !float.TryParse(topP.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var top) || top < 0 || top > 1))
            || (limit.isOn && (!int.TryParse(tokens.text, out var count) || count < 1))
            || !int.TryParse(history.text, out var h) || h < 2 || h > 40)
        { result.text = "请检查数值：温度 0–2、Top P 0–1、输出长度 >0、历史 2–40。"; return false; }
        p.model = model.text.Trim(); p.uri = uri.text.Trim(); p.apiKey = key.text.Trim();
        p.displayName = string.IsNullOrWhiteSpace(title.text) ? (string.IsNullOrEmpty(p.model) ? "新接口" : p.model) : title.text.Trim();
        p.enableTools = tools.isOn; p.sendSampling = sampling.isOn; p.sendTokenLimit = limit.isOn;
        if (float.TryParse(temperature.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var temp)) p.temperature = temp;
        if (float.TryParse(topP.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var topValue)) p.topP = topValue;
        if (int.TryParse(tokens.text, out var tokenCount)) p.maxTokens = tokenCount;
        p.useMaxCompletionTokens = completionTokens.isOn;
        p.disableThinking = p.preset == "DeepSeek" && thinking.isOn;
        settings.deepSeek.systemPrompt = prompt.text;
        settings.deepSeek.maxHistoryMessages = h;
        return true;
    }

    private void ShowSelected()
    {
        loading = true;
        var p = settings.modelProfiles[selected];
        title.text = p.displayName; model.text = p.model; uri.text = p.uri; key.text = p.apiKey;
        tools.isOn = p.enableTools; sampling.isOn = p.sendSampling; limit.isOn = p.sendTokenLimit;
        completionTokens.isOn = p.useMaxCompletionTokens; thinking.isOn = p.disableThinking;
        thinking.interactable = p.preset == "DeepSeek";
        temperature.text = p.temperature.ToString(CultureInfo.InvariantCulture);
        topP.text = p.topP.ToString(CultureInfo.InvariantCulture); tokens.text = p.maxTokens.ToString();
        prompt.text = settings.deepSeek.systemPrompt;
        history.text = settings.deepSeek.maxHistoryMessages.ToString();
        selection.text = "大模型 · " + p.displayName + (p.id == settings.activeModelProfileId ? "（当前启用）" : "（编辑中）");
        for (int i = list.childCount - 1; i >= 0; i--)
        {
            var child = list.GetChild(i).gameObject;
            child.SetActive(false);
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
        for (int i = 0; i < settings.modelProfiles.Count; i++)
        {
            var index = i;
            var item = settings.modelProfiles[i];
            ChatUiFactory.Button(list, (item.id == settings.activeModelProfileId ? "● " : "") + item.displayName, 0, i * 40, 202, () =>
            { if (!Pull()) return; selected = index; ShowSelected(); });
        }
        var content = list.parent as RectTransform;
        content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(850, settings.modelProfiles.Count * 40 + 70));
        loading = false;
        UpdateEndpoint();
    }

    private void UpdateEndpoint()
    {
        try { endpoint.text = "请求地址：" + new ChatModelProfile { uri = uri.text }.Endpoint; }
        catch { endpoint.text = "请输入基础 URI 或完整的 /chat/completions 地址。"; }
    }

    private void Add(string preset)
    {
        if (!Pull()) return;
        settings.modelProfiles.Add(ChatModelProfile.Create(preset));
        selected = settings.modelProfiles.Count - 1;
        ShowSelected();
    }

    private void Delete()
    {
        if (settings.modelProfiles.Count == 1) { result.text = "至少保留一套模型配置。"; return; }
        var removedActive = settings.modelProfiles[selected].id == settings.activeModelProfileId;
        settings.modelProfiles.RemoveAt(selected);
        selected = Math.Min(selected, settings.modelProfiles.Count - 1);
        if (removedActive) settings.activeModelProfileId = settings.modelProfiles[selected].id;
        ShowSelected();
        result.text = "已从编辑列表移除，点击保存后生效。";
    }

    private void SaveAndActivate()
    {
        if (!Pull()) return;
        try { settings.modelProfiles[selected].Validate(); }
        catch (Exception ex) { result.text = ex.Message; return; }
        settings.activeModelProfileId = settings.modelProfiles[selected].id;
        save?.Invoke();
    }

    private async void TestConnection()
    {
        if (!Pull()) return;
        testCancellation?.Cancel();
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        testCancellation = cancellation;
        test.interactable = false;
        result.text = "正在测试（短请求可能产生少量 API 费用）…";
        try
        {
            var service = new ChatCompletionService(settings.modelProfiles[selected], "请仅回复 OK。");
            var response = await service.CompleteChatAsync(new List<ChatCompletionService.ChatMessage>
                { new ChatCompletionService.ChatMessage("user", "连接测试，请回复 OK。") }, cancellation.Token);
            if (this != null && !cancellation.IsCancellationRequested)
                result.text = string.IsNullOrWhiteSpace(response.content) ? "请求成功，但未返回正文。" : "连接成功，可以保存并启用。";
        }
        catch (Exception ex)
        {
            if (this != null) result.text = cancellation.IsCancellationRequested ? "测试已取消或超时。"
                : ex is InvalidOperationException ? ex.Message : "连接失败，请检查网络和接口地址。";
        }
        finally
        {
            if (this != null) test.interactable = true;
            if (ReferenceEquals(testCancellation, cancellation)) testCancellation = null;
            cancellation.Dispose();
        }
    }

    private void OnDisable() { testCancellation?.Cancel(); }
}
