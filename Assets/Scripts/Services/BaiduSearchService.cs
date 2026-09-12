using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class BaiduSearchService
{
    private const string DefaultEndpoint = "https://qianfan.baidubce.com/v2/ai_search/web_search";
    private const string DefaultSearchSource = "baidu_search_v2";
    private static readonly HttpClient HttpClient = new HttpClient();

    private readonly BaiduSearchConfig config;

    public BaiduSearchService(BaiduSearchConfig config)
    {
        this.config = config;
    }

    public async Task<BaiduSearchResponse> SearchAsync(
        string query,
        int maxResults,
        string recencyFilter,
        bool autoSort,
        CancellationToken cancellationToken)
    {
        if (config == null)
        {
            throw new InvalidOperationException("BaiduSearchConfig is not assigned.");
        }

        var apiKey = (config.apiKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Baidu Search API Key is not configured.");
        }

        var endpoint = string.IsNullOrWhiteSpace(config.endpoint) ? DefaultEndpoint : config.endpoint.Trim();
        var trimmedQuery = TrimQuery(query, Mathf.Clamp(config.maxQueryCharacters, 20, 200));
        if (string.IsNullOrWhiteSpace(trimmedQuery))
        {
            throw new InvalidOperationException("Baidu Search query is empty.");
        }

        var payload = BuildPayload(trimmedQuery, maxResults, recencyFilter, autoSort);
        using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
        {
            request.Content = new StringContent(payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            AddAuthorizationHeaders(request, apiKey);

            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(Mathf.Clamp(config.timeoutSeconds, 1, 30)));
                using (var response = await HttpClient.SendAsync(request, timeoutCts.Token))
                {
                    var body = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new BaiduSearchException(
                            $"Baidu Search HTTP {(int)response.StatusCode}",
                            response.StatusCode,
                            body);
                    }

                    return ParseResponse(body, maxResults);
                }
            }
        }
    }

    private JObject BuildPayload(string query, int maxResults, string recencyFilter, bool autoSort)
    {
        var webTopK = Mathf.Clamp(Mathf.Max(config.webTopK, maxResults), 1, 50);
        var payload = new JObject
        {
            ["messages"] = new JArray
            {
                new JObject
                {
                    ["role"] = "user",
                    ["content"] = query
                }
            },
            ["edition"] = NormalizeEdition(config.edition),
            ["search_source"] = string.IsNullOrWhiteSpace(config.searchSource) ? DefaultSearchSource : config.searchSource.Trim(),
            ["resource_type_filter"] = BuildResourceFilter(webTopK),
            ["safe_search"] = config.safeSearch
        };

        if (!string.IsNullOrWhiteSpace(recencyFilter))
        {
            payload["search_recency_filter"] = recencyFilter.Trim();
        }

        if (autoSort && config.autoSortFreshQueries)
        {
            payload["sort"] = new JObject
            {
                ["priority"] = "auto"
            };
        }

        return payload;
    }

    private JArray BuildResourceFilter(int webTopK)
    {
        var resources = new JArray
        {
            new JObject
            {
                ["type"] = "web",
                ["top_k"] = webTopK
            }
        };

        AddResource(resources, "video", config.videoTopK, 10);
        AddResource(resources, "image", config.imageTopK, 30);
        AddResource(resources, "aladdin", config.aladdinTopK, 5);
        return resources;
    }

    private static void AddResource(JArray resources, string type, int topK, int maxTopK)
    {
        var count = Mathf.Clamp(topK, 0, maxTopK);
        if (count <= 0)
        {
            return;
        }

        resources.Add(new JObject
        {
            ["type"] = type,
            ["top_k"] = count
        });
    }

    private void AddAuthorizationHeaders(HttpRequestMessage request, string apiKey)
    {
        var value = "Bearer " + apiKey;
        var primaryHeader = string.IsNullOrWhiteSpace(config.authorizationHeaderName)
            ? "Authorization"
            : config.authorizationHeaderName.Trim();
        request.Headers.TryAddWithoutValidation(primaryHeader, value);

        var alternateHeader = (config.alternateAuthorizationHeaderName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(alternateHeader)
            && !string.Equals(alternateHeader, primaryHeader, StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.TryAddWithoutValidation(alternateHeader, value);
        }
    }

    private static BaiduSearchResponse ParseResponse(string body, int maxResults)
    {
        var root = JObject.Parse(body);
        var errorCode = root["code"]?.ToString();
        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            throw new BaiduSearchException(
                $"Baidu Search error {errorCode}: {root["message"]}",
                null,
                body);
        }

        var response = new BaiduSearchResponse
        {
            RequestId = root["request_id"]?.ToString()
        };

        var references = root["references"] as JArray;
        if (references == null)
        {
            return response;
        }

        foreach (var item in references)
        {
            if (response.Results.Count >= maxResults)
            {
                break;
            }

            var obj = item as JObject;
            if (obj == null)
            {
                continue;
            }

            var result = new BaiduSearchResult
            {
                Id = obj["id"]?.ToString(),
                Title = LimitText(CleanText(obj["title"]?.ToString()), 140),
                Snippet = LimitText(CleanText(obj["content"]?.ToString() ?? obj["snippet"]?.ToString()), 420),
                Url = CleanText(obj["url"]?.ToString()),
                Website = CleanText(obj["website"]?.ToString() ?? obj["web_anchor"]?.ToString()),
                Date = CleanText(obj["date"]?.ToString()),
                Type = CleanText(obj["type"]?.ToString())
            };

            if (string.IsNullOrWhiteSpace(result.Snippet) && string.IsNullOrWhiteSpace(result.Url))
            {
                continue;
            }

            response.Results.Add(result);
        }

        return response;
    }

    private static string NormalizeEdition(string edition)
    {
        if (string.Equals(edition, "lite", StringComparison.OrdinalIgnoreCase))
        {
            return "lite";
        }

        return "standard";
    }

    private static string TrimQuery(string query, int maxCharacters)
    {
        var clean = CleanText(query);
        return LimitBaiduQueryText(clean, maxCharacters);
    }

    private static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decoded = WebUtility.HtmlDecode(text);
        decoded = Regex.Replace(decoded, "<.*?>", string.Empty);
        decoded = Regex.Replace(decoded, "[\\u0000-\\u001F]", string.Empty);
        decoded = Regex.Replace(decoded, "\\s+", " ");
        return decoded.Trim();
    }

    private static string LimitText(string text, int maxCharacters)
    {
        if (string.IsNullOrEmpty(text) || maxCharacters <= 0 || text.Length <= maxCharacters)
        {
            return text ?? string.Empty;
        }

        return text.Substring(0, maxCharacters).TrimEnd() + "...";
    }

    private static string LimitBaiduQueryText(string text, int maxCharacters)
    {
        if (string.IsNullOrEmpty(text) || maxCharacters <= 0)
        {
            return text ?? string.Empty;
        }

        var usedCharacters = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var weight = text[i] <= 0x7f ? 1 : 2;
            if (usedCharacters + weight > maxCharacters)
            {
                return text.Substring(0, i).TrimEnd();
            }

            usedCharacters += weight;
        }

        return text;
    }
}

public sealed class BaiduSearchResponse
{
    public string RequestId;
    public readonly List<BaiduSearchResult> Results = new List<BaiduSearchResult>();
}

public sealed class BaiduSearchResult
{
    public string Id;
    public string Title;
    public string Snippet;
    public string Url;
    public string Website;
    public string Date;
    public string Type;
}

public sealed class BaiduSearchException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public string ResponseBody { get; }

    public BaiduSearchException(string message, HttpStatusCode? statusCode, string responseBody)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
