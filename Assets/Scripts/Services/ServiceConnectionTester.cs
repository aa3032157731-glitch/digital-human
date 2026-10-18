using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class ServiceTestResult
{
    public bool ok;
    public string message;
    public string detail;
    public long milliseconds;
    public DateTime time = DateTime.Now;
}

// Checks that credentials work by making one small real request. Nothing is saved: the draft is copied
// and only the tested account is applied to the copy.
public static class ServiceConnectionTester
{
    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    private const int TimeoutSeconds = 15;

    public static Task<ServiceTestResult> TestAccountAsync(ServiceAccount account, RuntimeVoiceChatSettings draft, CancellationToken token) =>
        RunAsync(token, async inner =>
        {
            string missing = Missing(account);
            if (missing != null) throw new InvalidOperationException("请先填写" + missing + "。");
            var snapshot = Snapshot(draft);
            var copy = account.Copy();
            if (ServiceAccounts.IsChat(copy.kind)) return await TestChatAccountAsync(copy, snapshot, inner);
            var target = ServiceAccounts.Target(snapshot, copy.kind);
            foreach (var name in ServiceAccounts.Fields(copy.kind)) target.GetType().GetField(name)?.SetValue(target, copy.Get(name));
            if (copy.kind == "ASR") return await TestAsrAsync(snapshot.xunfeiAsr, inner);
            if (copy.kind == "Search") return await TestSearchAsync(snapshot.baiduSearch, inner);
            return await TestSpeechAsync(snapshot, copy.kind, inner);
        });

    // The active chat model profile as currently drafted (account + model + parameters).
    public static Task<ServiceTestResult> TestActiveModelAsync(RuntimeVoiceChatSettings draft, CancellationToken token) =>
        RunAsync(token, async inner =>
        {
            var snapshot = Snapshot(draft); ServiceAccounts.Resolve(snapshot);
            var profile = snapshot.modelProfiles.Find(p => p.id == snapshot.activeModelProfileId);
            profile.Validate(); profile.enableTools = false; profile.sendTokenLimit = true; profile.maxTokens = Mathf.Clamp(profile.maxTokens, 16, 64);
            var reply = await new ChatCompletionService(profile, "请仅回复 OK。").CompleteChatAsync(
                new List<ChatCompletionService.ChatMessage> { new ChatCompletionService.ChatMessage("user", "连接测试，请回复 OK。") }, inner);
            return string.IsNullOrWhiteSpace(reply.content) ? "连接正常，模型已响应。" : "连接正常，模型 " + profile.model + " 已回复。";
        });

    private static async Task<ServiceTestResult> RunAsync(CancellationToken token, Func<CancellationToken, Task<string>> test)
    {
        var watch = Stopwatch.StartNew();
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
            try
            {
                string message = await test(timeout.Token);
                return new ServiceTestResult { ok = true, message = message, milliseconds = watch.ElapsedMilliseconds };
            }
            catch (Exception ex)
            {
                token.ThrowIfCancellationRequested();
                bool timedOut = timeout.IsCancellationRequested || ex is TimeoutException;
                string message = timedOut ? "连接超时：" + TimeoutSeconds + " 秒内没有响应，请检查网络、地址或区域。" : Describe(ex);
                string raw = Flatten(ex);
                return new ServiceTestResult { ok = false, message = message, detail = timedOut || raw == message ? null : Trim(raw, 180), milliseconds = watch.ElapsedMilliseconds };
            }
        }
    }

    private static RuntimeVoiceChatSettings Snapshot(RuntimeVoiceChatSettings draft)
    {
        var snapshot = JsonConvert.DeserializeObject<RuntimeVoiceChatSettings>(JsonConvert.SerializeObject(draft));
        snapshot.EnsureDefaults(); return snapshot;
    }

    public static string Missing(ServiceAccount a)
    {
        var missing = new List<string>();
        void Need(string field, string label) { if (string.IsNullOrWhiteSpace(a.Get(field))) missing.Add(label); }
        if (ServiceAccounts.IsChat(a.kind)) { Need("apiKey", "API 密钥"); Need("uri", "接口地址"); }
        else if (a.kind == "AzureSpeech") { Need("apiKey", "API 密钥"); if (string.IsNullOrWhiteSpace(a.Get("resourceEndpoint"))) Need("region", "资源区域"); }
        else if (a.kind == "Doubao") { if (string.IsNullOrWhiteSpace(a.Get("apiKey")) && (string.IsNullOrWhiteSpace(a.Get("appId")) || string.IsNullOrWhiteSpace(a.Get("accessKey")))) missing.Add("API 密钥（或 App ID 与 Access Key）"); }
        else if (a.kind == "Search") Need("apiKey", "API 密钥");
        else { Need("appId", "App ID"); Need("apiKey", "API 密钥"); Need("apiSecret", "API Secret"); }
        return missing.Count == 0 ? null : string.Join("、", missing);
    }

    private static async Task<string> TestChatAccountAsync(ServiceAccount account, RuntimeVoiceChatSettings snapshot, CancellationToken token)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Get, ServiceCatalog.Endpoint(account)))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Get("apiKey").Trim());
            using (var response = await Http.SendAsync(request, token))
            {
                int status = (int)response.StatusCode;
                if (response.IsSuccessStatusCode)
                {
                    int count = 0;
                    try { count = (JToken.Parse(await response.Content.ReadAsStringAsync())["data"] as JArray)?.Count ?? 0; } catch (JsonException) { }
                    return count > 0 ? "连接正常，账号可用模型 " + count + " 个。" : "连接正常，密钥有效。";
                }
                if (status == 401 || status == 403) throw new InvalidOperationException("HTTP " + status);
            }
        }
        // Some gateways (for example 火山方舟) do not list models; verify with a tiny chat request instead.
        var profile = snapshot.modelProfiles.FirstOrDefault(p => p.serviceAccountId == account.id && !string.IsNullOrWhiteSpace(p.model));
        if (profile == null) throw new InvalidOperationException("此服务不提供模型列表，无法单独验证密钥。请在「意识」中为它选择模型后，使用“测试当前模型”。");
        profile = profile.Copy(); profile.uri = account.Get("uri"); profile.apiKey = account.Get("apiKey");
        profile.enableTools = false; profile.sendTokenLimit = true; profile.maxTokens = 16;
        await new ChatCompletionService(profile, "请仅回复 OK。").CompleteChatAsync(
            new List<ChatCompletionService.ChatMessage> { new ChatCompletionService.ChatMessage("user", "连接测试，请回复 OK。") }, token);
        return "连接正常，模型 " + profile.model + " 已响应。";
    }

    private static async Task<string> TestSpeechAsync(RuntimeVoiceChatSettings snapshot, string kind, CancellationToken token)
    {
        var service = SpeechServiceFactory.Create(snapshot, kind, false, out var config);
        try
        {
            var audio = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.AudioChunkReceived += bytes => { if (bytes != null && bytes.Length > 0) audio.TrySetResult(true); };
            service.Error += message => audio.TrySetException(new InvalidOperationException(message));
            service.Completed += () => audio.TrySetResult(false);
            await service.StartAsync(token);
            service.EnqueueText("你好。");
            service.CompleteInput();
            var completion = service.WaitForCompletionAsync();
            _ = completion.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            var finished = await Task.WhenAny(audio.Task, completion, Task.Delay(Timeout.Infinite, token));
            token.ThrowIfCancellationRequested();
            if (finished == completion && !audio.Task.IsCompleted) await completion;
            if (!audio.Task.IsCompleted) audio.TrySetResult(false);
            if (!await audio.Task) throw new InvalidOperationException("服务已响应，但没有返回音频，请检查音色或资源 ID。");
            return "连接正常，已收到合成音频。";
        }
        finally
        {
            try { await service.CancelAsync(); } catch (Exception) { }
            service.Dispose(); SpeechServiceFactory.Release(config);
        }
    }

    private static async Task<string> TestAsrAsync(XFASRRuntimeSettings settings, CancellationToken token)
    {
        var config = ScriptableObject.CreateInstance<XFASRConfig>();
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(settings), config);
        var service = new XFASRService(config);
        try
        {
            var failure = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.Error += message => failure.TrySetResult(message);
            await service.StartAsync(token);              // the signed handshake fails here for a wrong key/secret
            service.EnqueueAudio(new byte[6400]);         // 0.2 s of silence, so App ID is checked too
            service.CompleteInput();
            var end = service.WaitForSessionEndAsync();
            _ = end.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            var finished = await Task.WhenAny(end, failure.Task, Task.Delay(TimeSpan.FromSeconds(8), token));
            token.ThrowIfCancellationRequested();
            if (failure.Task.IsCompleted) throw new InvalidOperationException(failure.Task.Result);
            if (finished == end) await end;
            return "连接正常，识别服务已响应。";
        }
        finally
        {
            try { await service.CancelAsync(); } catch (Exception) { }
            service.Dispose(); SpeechServiceFactory.Release(config);
        }
    }

    private static async Task<string> TestSearchAsync(BaiduSearchRuntimeSettings settings, CancellationToken token)
    {
        var config = ScriptableObject.CreateInstance<BaiduSearchConfig>();
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(settings), config);
        try
        {
            var response = await new BaiduSearchService(config).SearchAsync("今天天气", 1, null, false, token);
            return "连接正常，搜索返回 " + response.Results.Count + " 条结果。";
        }
        finally { SpeechServiceFactory.Release(config); }
    }

    // ---------- error description ----------

    public static string Describe(Exception ex)
    {
        string raw = Flatten(ex);
        var xunfei = Regex.Match(raw, @"XF[^\d]*error (\d+)");
        if (xunfei.Success) return Xunfei(xunfei.Groups[1].Value);
        var doubao = Regex.Match(raw, @"Doubao TTS error (\d+)");
        if (doubao.Success) return "豆包返回错误 " + doubao.Groups[1].Value + "，请检查音色、资源 ID 与账号权限。";
        int status = 0;
        var http = Regex.Match(raw, @"(?:HTTP|status code|状态码)\D{0,4}(\d{3})", RegexOptions.IgnoreCase);
        if (http.Success) int.TryParse(http.Groups[1].Value, out status);
        if (status == 401 || status == 403 || Regex.IsMatch(raw, "AuthenticationFailure|Unauthorized|HMAC|signature|illegal access", RegexOptions.IgnoreCase))
            return "鉴权失败：密钥无效，或与区域 / App ID 不匹配。";
        if (status == 429) return "请求过多或额度不足（HTTP 429），请稍后重试或检查用量。";
        if (status == 404) return "接口地址不存在（HTTP 404），请检查接口地址、区域或资源 ID。";
        if (status == 400) return "请求被拒绝（HTTP 400），请检查模型、音色等参数。";
        if (status >= 500) return "服务端暂时不可用（HTTP " + status + "），请稍后重试。";
        if (ex is HttpRequestException || ex is System.Net.WebSockets.WebSocketException || ex is System.Net.Sockets.SocketException
            || Regex.IsMatch(raw, "No such host|Name or service not known|could not be resolved|actively refused|无法连接|未知主机", RegexOptions.IgnoreCase))
            return "无法连接服务器，请检查网络和接口地址。";
        if (ex is InvalidOperationException && Regex.IsMatch(ex.Message, @"\p{IsCJKUnifiedIdeographs}")) return ex.Message;
        return "连接失败：" + Trim(raw, 120);
    }

    private static string Xunfei(string code)
    {
        switch (code)
        {
            case "10105": return "讯飞返回 10105：没有访问权限，请检查 App ID、API Key 及服务是否已开通。";
            case "10106": return "讯飞返回 10106：参数无效，请检查音色、资源等配置。";
            case "10313": return "讯飞返回 10313：App ID 与 API Key 不匹配。";
            case "11200": return "讯飞返回 11200：未开通该服务或授权不足，请在讯飞控制台开通。";
            case "11201": return "讯飞返回 11201：当日调用量已用完。";
            default: return "讯飞返回错误 " + code + "，请检查账号配置与服务开通情况。";
        }
    }

    private static string Flatten(Exception ex)
    {
        if (ex is AggregateException aggregate) ex = aggregate.Flatten().InnerExceptions.FirstOrDefault() ?? ex;
        var parts = new List<string>();
        for (var e = ex; e != null && parts.Count < 3; e = e.InnerException)
            if (!string.IsNullOrWhiteSpace(e.Message) && !parts.Contains(e.Message)) parts.Add(e.Message.Trim());
        return string.Join(" / ", parts);
    }

    private static string Trim(string text, int max) => string.IsNullOrEmpty(text) || text.Length <= max ? text : text.Substring(0, max) + "…";
}
