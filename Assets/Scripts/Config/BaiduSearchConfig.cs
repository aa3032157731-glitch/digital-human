using UnityEngine;

[CreateAssetMenu(menuName = "Voice Chat/Configs/Baidu Search Config", fileName = "BaiduSearchConfig")]
public class BaiduSearchConfig : ScriptableObject
{
    [Header("Credentials")]
    [Tooltip("Baidu Qianfan AI Search API key.")]
    public string apiKey;

    [Tooltip("Primary authorization header. Qianfan API docs use Authorization: Bearer <API Key>.")]
    public string authorizationHeaderName = "Authorization";

    [Tooltip("Compatibility authorization header. Some Baidu examples use X-Appbuilder-Authorization: Bearer <API Key>. Leave empty to disable.")]
    public string alternateAuthorizationHeaderName = "X-Appbuilder-Authorization";

    [Header("Endpoint")]
    [Tooltip("Baidu Qianfan web search endpoint.")]
    public string endpoint = "https://qianfan.baidubce.com/v2/ai_search/web_search";

    [Tooltip("Search edition. Baidu supports standard and lite.")]
    public string edition = "standard";

    [Tooltip("Search engine source. Keep baidu_search_v2 unless Baidu changes the API.")]
    public string searchSource = "baidu_search_v2";

    [Header("Search")]
    [Tooltip("Maximum result count exposed to the assistant.")]
    [Range(1, 20)]
    public int maxResults = 6;

    [Tooltip("Baidu web resource top_k. Baidu allows up to 50 for web.")]
    [Range(1, 50)]
    public int webTopK = 10;

    [Tooltip("Baidu video resource top_k. Use 0 to disable.")]
    [Range(0, 10)]
    public int videoTopK;

    [Tooltip("Baidu image resource top_k. Use 0 to disable.")]
    [Range(0, 30)]
    public int imageTopK;

    [Tooltip("Baidu aladdin resource top_k. Use 0 to disable.")]
    [Range(0, 5)]
    public int aladdinTopK;

    [Tooltip("Trim query text before sending. Baidu documents a 72-character retrieval limit.")]
    [Range(20, 200)]
    public int maxQueryCharacters = 72;

    [Tooltip("Maximum characters from search results exposed to DeepSeek.")]
    [Range(500, 6000)]
    public int maxContextCharacters = 2400;

    [Tooltip("Enable Baidu safe search.")]
    public bool safeSearch = true;

    [Tooltip("Use Baidu automatic sort for fresh/current queries.")]
    public bool autoSortFreshQueries = true;

    [Header("Timeout")]
    [Tooltip("Maximum seconds to wait for one Baidu search request.")]
    [Range(1, 30)]
    public int timeoutSeconds = 8;
}
