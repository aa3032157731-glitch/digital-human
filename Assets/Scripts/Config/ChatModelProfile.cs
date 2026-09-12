using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class ChatModelProfile
{
    public string id = Guid.NewGuid().ToString("N");
    public string displayName = "兼容接口";
    public string preset = "Compatible";
    public string model = "";
    public string uri = "";
    public string apiKey = "";
    public bool enableTools;
    public bool sendSampling;
    public float temperature = 0.7f;
    public float topP = 0.9f;
    public bool sendTokenLimit;
    public int maxTokens = 1024;
    public bool useMaxCompletionTokens;
    public bool disableThinking;

    public ChatModelProfile Copy() => JsonUtility.FromJson<ChatModelProfile>(JsonUtility.ToJson(this));

    public string Endpoint
    {
        get
        {
            var value = (uri ?? "").Trim().TrimEnd('/');
            if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)
                || (parsed.Scheme != "http" && parsed.Scheme != "https")
                || !string.IsNullOrEmpty(parsed.UserInfo) || !string.IsNullOrEmpty(parsed.Fragment))
                throw new InvalidOperationException("请输入有效的 HTTP/HTTPS 接口地址。");
            var builder = new UriBuilder(parsed);
            if (!builder.Path.TrimEnd('/').EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                builder.Path = builder.Path.TrimEnd('/') + "/chat/completions";
            return builder.Uri.AbsoluteUri;
        }
    }

    public void Validate()
    {
        _ = Endpoint;
        if (string.IsNullOrWhiteSpace(model)) throw new InvalidOperationException("请填写模型名称或接入点 ID。");
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("请填写 API 密钥。");
        if (sendTokenLimit && maxTokens < 1) throw new InvalidOperationException("输出长度必须大于零。");
    }

    public static ChatModelProfile FromLegacy(DeepSeekRuntimeSettings legacy)
    {
        return new ChatModelProfile
        {
            displayName = "DeepSeek（原配置）", preset = "DeepSeek",
            uri = legacy.chatCompletionsUrl, model = legacy.model, apiKey = legacy.apiKey,
            enableTools = true, sendSampling = true, temperature = legacy.temperature, topP = legacy.topP,
            sendTokenLimit = true, maxTokens = legacy.maxTokens, disableThinking = legacy.disableThinking
        };
    }

    public static ChatModelProfile Create(string kind)
    {
        return new ChatModelProfile
        {
            preset = kind,
            displayName = kind == "Ark" ? "火山方舟" : kind == "DeepSeek" ? "DeepSeek" : "兼容接口",
            uri = kind == "Ark" ? "https://ark.cn-beijing.volces.com/api/v3"
                : kind == "DeepSeek" ? "https://api.deepseek.com" : "https://api.openai.com/v1"
        };
    }
}
