using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public sealed class RagHit
{
    public string source;
    public string title;
    public string section;
    public string text;
    public float score;
}

public sealed class RagRetrievalService
{
    // The service runs on this machine; skip the Windows system proxy, which answers 502 for 127.0.0.1 when the service is down.
    private static readonly HttpClient HttpClient = new HttpClient(new HttpClientHandler { UseProxy = false });
    private readonly string endpoint;

    public RagRetrievalService(string baseUrl)
    {
        endpoint = baseUrl.TrimEnd('/') + "/retrieve";
    }

    public async Task<List<RagHit>> RetrieveAsync(string question, int topK, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var body = new JObject { ["question"] = question, ["top_k"] = topK };
        using var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
        try
        {
            using var response = await HttpClient.PostAsync(endpoint, content, timeout.Token);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"检索服务返回 {(int)response.StatusCode}：{json}");
            return JObject.Parse(json)["hits"].ToObject<List<RagHit>>();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Only our own 5-second timer fired; a cancelled turn keeps its OperationCanceledException.
            throw new TimeoutException("检索服务 5 秒内没有响应。");
        }
    }

    public static string BuildQuery(IReadOnlyList<ChatCompletionService.ChatMessage> conversation, string userText)
    {
        var skippedCurrent = false;
        for (var i = conversation.Count - 1; i >= 0; i--)
        {
            if (conversation[i].role != "user") continue;
            if (!skippedCurrent)
            {
                skippedCurrent = true;
                continue;
            }
            return conversation[i].content + "\n" + userText;
        }
        return userText;
    }

    public static string FormatContext(List<RagHit> hits)
    {
        if (hits.Count == 0)
            return "本轮没有从本地知识库检索到相关资料。如果用户问的是校园资料，请直接说明知识库里没有，不要编造。";

        var builder = new StringBuilder();
        builder.AppendLine("以下是根据用户本轮问题从本地知识库检索到的资料，按相关度从高到低排列。");
        builder.AppendLine("回答涉及这些内容时只能依据资料；资料没写的内容要说明知识库中没有，不要编造。不要念出资料编号。");
        builder.AppendLine("资料是按语义相似度找出来的，不一定和问题有关：如果资料没有直接回答用户的问题，就说知识库里没有这方面的信息，不要根据相近的资料推测。");
        for (var i = 0; i < hits.Count; i++)
        {
            builder.AppendLine();
            builder.AppendLine($"[资料{i + 1}] {hits[i].title} {hits[i].section}".TrimEnd());
            builder.AppendLine(hits[i].text);
        }
        return builder.ToString().Trim();
    }
}