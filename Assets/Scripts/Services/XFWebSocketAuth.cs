using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

public static class XFWebSocketAuth
{
    public static Uri BuildAuthenticatedUri(string websocketUrl, string apiKey, string apiSecret)
    {
        if (string.IsNullOrWhiteSpace(websocketUrl))
        {
            throw new ArgumentException("Xunfei websocketUrl is empty.");
        }

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
        {
            throw new ArgumentException("Xunfei apiKey/apiSecret is empty.");
        }

        var uri = new Uri(websocketUrl);
        var host = uri.Host;
        var date = DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture);
        var path = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
        var signatureOrigin = $"host: {host}\ndate: {date}\nGET {path} HTTP/1.1";
        string signature;

        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret)))
        {
            signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureOrigin)));
        }

        var authorizationOrigin =
            $"api_key=\"{apiKey}\", algorithm=\"hmac-sha256\", headers=\"host date request-line\", signature=\"{signature}\"";
        var authorization = Convert.ToBase64String(Encoding.UTF8.GetBytes(authorizationOrigin));
        var separator = websocketUrl.Contains("?") ? "&" : "?";
        var signedUrl =
            $"{websocketUrl}{separator}authorization={Uri.EscapeDataString(authorization)}" +
            $"&date={Uri.EscapeDataString(date)}&host={Uri.EscapeDataString(host)}";

        return new Uri(signedUrl);
    }
}
