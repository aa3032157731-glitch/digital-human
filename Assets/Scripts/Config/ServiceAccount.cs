using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

[Serializable]
public sealed class ServiceValue
{
    public string name;
    public string value;
}

[Serializable]
public sealed class ServiceAccount
{
    public string id = Guid.NewGuid().ToString("N");
    public string kind;
    public string displayName;
    public bool selected;
    public List<ServiceValue> values = new List<ServiceValue>();
    public string Get(string name) => values.Find(v => v.name == name)?.value ?? "";
    public void Set(string name, string value)
    {
        var field = values.Find(v => v.name == name);
        if (field == null) values.Add(new ServiceValue { name = name, value = value ?? "" });
        else field.value = value ?? "";
    }
    public ServiceAccount Copy() => JsonConvert.DeserializeObject<ServiceAccount>(JsonConvert.SerializeObject(this));
}

public static class ServiceAccounts
{
    public static readonly string[] Kinds = { "DeepSeek", "Ark", "Compatible", "AzureSpeech", "Doubao", "XunfeiSuperSmart", "XunfeiOnline", "ASR", "Search" };
    public static string Name(string kind)
    {
        switch (kind)
        {
            case "DeepSeek": return "DeepSeek";
            case "Ark": return "火山方舟";
            case "Compatible": return "OpenAI 兼容接口";
            case "AzureSpeech": return "微软 Azure Speech";
            case "Doubao": return "豆包语音合成";
            case "XunfeiSuperSmart": return "讯飞超拟人";
            case "XunfeiOnline": return "讯飞在线语音";
            case "ASR": return "讯飞语音识别";
            case "Search": return "百度搜索";
            default: return kind;
        }
    }
    public static bool IsChat(string kind) => kind == "DeepSeek" || kind == "Ark" || kind == "Compatible";
    public static string[] Fields(string kind)
    {
        if (IsChat(kind)) return new[] { "apiKey", "uri" };
        switch (kind)
        {
            case "AzureSpeech": return new[] { "apiKey", "region", "resourceEndpoint" };
            case "Doubao": return new[] { "apiKey", "appId", "accessKey", "endpoint", "resourceId", "userId" };
            case "XunfeiSuperSmart": return new[] { "appId", "apiKey", "apiSecret", "websocketUrl", "serviceId" };
            case "XunfeiOnline": return new[] { "appId", "apiKey", "apiSecret", "websocketUrl" };
            case "ASR": return new[] { "appId", "apiKey", "apiSecret", "websocketUrl", "resId" };
            default: return new[] { "apiKey", "endpoint", "authorizationHeaderName", "alternateAuthorizationHeaderName" };
        }
    }
    public static object Target(RuntimeVoiceChatSettings s, string kind)
    {
        switch (kind)
        {
            case "AzureSpeech": return s.azureTts;
            case "Doubao": return s.doubaoTts;
            case "XunfeiSuperSmart": return s.xunfeiSuperSmartTts;
            case "XunfeiOnline": return s.xunfeiOnlineTts;
            case "ASR": return s.xunfeiAsr;
            case "Search": return s.baiduSearch;
            default: return null;
        }
    }
    public static ServiceAccount New(string kind)
    {
        var account = new ServiceAccount { kind = kind, displayName = Name(kind) };
        object defaults = IsChat(kind) ? (object)ChatModelProfile.Create(kind) : Target(new RuntimeVoiceChatSettings(), kind);
        foreach (var name in Fields(kind)) account.Set(name, defaults?.GetType().GetField(name)?.GetValue(defaults) as string);
        return account;
    }
    public static void Migrate(RuntimeVoiceChatSettings s)
    {
        if (s.serviceAccounts == null) s.serviceAccounts = new List<ServiceAccount>();
        s.serviceAccounts.RemoveAll(a => a == null);
        foreach (var p in s.modelProfiles)
        {
            if (!string.IsNullOrEmpty(p.serviceAccountId)) continue;
            var kind = IsChat(p.preset) ? p.preset : "Compatible";
            var account = s.serviceAccounts.Find(a => a.kind == kind && a.Get("uri") == (p.uri ?? "") && a.Get("apiKey") == (p.apiKey ?? ""));
            if (account == null)
            {
                account = New(kind); account.displayName = p.displayName;
                account.Set("uri", p.uri); account.Set("apiKey", p.apiKey);
                s.serviceAccounts.Add(account);
            }
            p.serviceAccountId = account.id;
        }
        foreach (var kind in Kinds.Where(k => !IsChat(k)))
        {
            if (s.schemaVersion >= 6) continue;
            if (s.serviceAccounts.Exists(a => a.kind == kind)) continue;
            var account = New(kind); account.selected = true;
            var target = Target(s, kind);
            foreach (var name in Fields(kind)) account.Set(name, target.GetType().GetField(name)?.GetValue(target) as string);
            s.serviceAccounts.Add(account);
        }
    }
    public static void Resolve(RuntimeVoiceChatSettings s)
    {
        foreach (var p in s.modelProfiles)
        {
            var account = s.serviceAccounts.Find(a => a.id == p.serviceAccountId);
            if (account == null) throw new InvalidOperationException("模型引用的服务账号不存在：" + p.displayName);
            if (!IsChat(account.kind)) throw new InvalidOperationException("请选择聊天服务账号：" + p.displayName);
            p.uri = account.Get("uri"); p.apiKey = account.Get("apiKey"); p.preset = account.kind;
        }
        foreach (var account in s.serviceAccounts.Where(a => a.selected && !IsChat(a.kind)))
        {
            var target = Target(s, account.kind);
            if (target == null) continue;
            foreach (var name in Fields(account.kind)) target.GetType().GetField(name)?.SetValue(target, account.Get(name));
        }
    }
    public static bool Configured(ServiceAccount a)
    {
        if (a.kind == "Doubao") return !string.IsNullOrWhiteSpace(a.Get("apiKey")) || (!string.IsNullOrWhiteSpace(a.Get("appId")) && !string.IsNullOrWhiteSpace(a.Get("accessKey")));
        if (string.IsNullOrWhiteSpace(a.Get("apiKey"))) return false;
        if (a.kind == "AzureSpeech") return !string.IsNullOrWhiteSpace(a.Get("region"));
        if (a.kind.StartsWith("Xunfei") || a.kind == "ASR") return !string.IsNullOrWhiteSpace(a.Get("appId")) && !string.IsNullOrWhiteSpace(a.Get("apiSecret"));
        return true;
    }
}
