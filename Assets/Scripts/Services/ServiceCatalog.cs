using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public sealed class CatalogEntry
{
    public string id, name, language;
    public string[] styles = Array.Empty<string>();
}

public sealed class ServiceCatalog : IDisposable
{
    private readonly HttpClient http;
    private readonly Dictionary<string, List<CatalogEntry>> cache = new Dictionary<string, List<CatalogEntry>>();
    public ServiceCatalog(HttpMessageHandler handler = null)
    {
        http = handler == null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = TimeSpan.FromSeconds(20);
    }
    public static bool Supported(string kind) => kind == "DeepSeek" || kind == "Compatible" || kind == "AzureSpeech";
    private static string Key(ServiceAccount account)
    {
        using (var sha = SHA256.Create())
            return account.id + Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("|", account.values.Select(v => v.name + "=" + v.value)))));
    }
    public List<CatalogEntry> Cached(ServiceAccount account) => cache.TryGetValue(Key(account), out var list) ? list : new List<CatalogEntry>();
    public static Uri Endpoint(ServiceAccount account)
    {
        if (account.kind == "AzureSpeech")
        {
            var resource = account.Get("resourceEndpoint").TrimEnd('/');
            if (resource.Length > 0) return new Uri(resource + "/tts/cognitiveservices/voices/list");
            var region = account.Get("region").Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(region, "^[a-z][a-z0-9]{1,39}$")) throw new InvalidOperationException("请先填写 Azure 区域。");
            return new Uri("https://" + region + ".tts.speech.microsoft.com/cognitiveservices/voices/list");
        }
        var builder = new UriBuilder(new ChatModelProfile { uri = account.Get("uri") }.Endpoint);
        builder.Path = builder.Path.Substring(0, builder.Path.Length - "/chat/completions".Length) + "/models";
        return builder.Uri;
    }
    public async Task<List<CatalogEntry>> FetchAsync(ServiceAccount account, bool refresh, CancellationToken token)
    {
        if (!Supported(account.kind)) throw new NotSupportedException("此服务请填写自定义 ID。");
        string key = Key(account);
        if (!refresh && cache.TryGetValue(key, out var prior)) return prior;
        using (var request = new HttpRequestMessage(HttpMethod.Get, Endpoint(account)))
        {
            if (account.kind == "AzureSpeech") request.Headers.Add("Ocp-Apim-Subscription-Key", account.Get("apiKey"));
            else request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", account.Get("apiKey"));
            using (var response = await http.SendAsync(request, token))
            {
                if (!response.IsSuccessStatusCode)
                {
                    int status = (int)response.StatusCode;
                    throw new InvalidOperationException(status == 401 || status == 403 ? "账号鉴权失败，请检查密钥与权限。" : status == 404 || status == 405 ? "此接口不提供列表，请手动填写 ID。" : "列表加载失败（HTTP " + status + "），可重试或手动填写。");
                }
                var json = JToken.Parse(await response.Content.ReadAsStringAsync()); token.ThrowIfCancellationRequested();
                var items = account.kind == "AzureSpeech" ? json as JArray : json["data"] as JArray;
                if (items == null) throw new InvalidOperationException("服务返回的列表格式不兼容，请手动填写 ID。");
                var result = items.Select(item => account.kind == "AzureSpeech"
                    ? new CatalogEntry { id = (string)item["ShortName"], name = (string)item["LocalName"] ?? (string)item["DisplayName"], language = (string)item["Locale"], styles = item["StyleList"]?.Values<string>().ToArray() ?? Array.Empty<string>() }
                    : new CatalogEntry { id = (string)item["id"], name = (string)item["id"] })
                    .Where(item => !string.IsNullOrWhiteSpace(item.id)).GroupBy(item => item.id).Select(g => g.First()).ToList();
                cache[key] = result; return result;
            }
        }
    }
    public void Dispose() => http.Dispose();
}
