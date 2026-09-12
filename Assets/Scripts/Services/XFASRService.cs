using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public sealed class XFASRService : IDisposable
{
    private struct AudioFrame
    {
        public byte[] Pcm;
        public int Status;
    }

    public event Action<string> PartialResult;
    public event Action<string> FinalResult;
    public event Action<string> Error;
    public event Action SessionEnded;

    private readonly XFASRConfig config;
    private readonly ConcurrentQueue<AudioFrame> sendQueue = new ConcurrentQueue<AudioFrame>();
    private readonly SortedDictionary<int, string> transcriptParts = new SortedDictionary<int, string>();
    private ClientWebSocket webSocket;
    private CancellationTokenSource internalCts;
    private Task sendTask;
    private Task receiveTask;
    private int sequence;
    private bool sentFirstFrame;
    private bool finalFrameQueued;
    private string latestTranscript = string.Empty;

    public XFASRService(XFASRConfig config)
    {
        this.config = config;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ValidateConfig();

        internalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        webSocket = new ClientWebSocket();
        var uri = XFWebSocketAuth.BuildAuthenticatedUri(config.websocketUrl, config.apiKey, config.apiSecret);

        await webSocket.ConnectAsync(uri, internalCts.Token);

        sendTask = Task.Run(() => SendLoopAsync(internalCts.Token));
        receiveTask = Task.Run(() => ReceiveLoopAsync(internalCts.Token));
    }

    public void EnqueueAudio(byte[] pcm16LittleEndian)
    {
        if (pcm16LittleEndian == null || pcm16LittleEndian.Length == 0 || finalFrameQueued)
        {
            return;
        }

        sendQueue.Enqueue(new AudioFrame
        {
            Pcm = pcm16LittleEndian,
            Status = sentFirstFrame ? 1 : 0
        });
        sentFirstFrame = true;
    }

    public void CompleteInput()
    {
        if (finalFrameQueued)
        {
            return;
        }

        finalFrameQueued = true;
        sendQueue.Enqueue(new AudioFrame
        {
            Pcm = Array.Empty<byte>(),
            Status = 2
        });
    }

    public async Task WaitForSessionEndAsync()
    {
        if (receiveTask == null)
        {
            return;
        }

        await receiveTask;
    }

    public async Task CancelAsync()
    {
        try
        {
            internalCts?.Cancel();

            if (webSocket != null && webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "cancel", CancellationToken.None);
            }
        }
        catch
        {
            webSocket?.Abort();
        }
    }

    public void Dispose()
    {
        internalCts?.Cancel();
        webSocket?.Dispose();
        internalCts?.Dispose();
    }

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!sendQueue.TryDequeue(out var frame))
                {
                    await Task.Delay(5, cancellationToken);
                    continue;
                }

                var json = BuildFrame(frame);
                await SendTextAsync(json, cancellationToken);

                if (frame.Status == 2)
                {
                    break;
                }

                await Task.Delay(Math.Max(10, config.frameIntervalMs), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Error?.Invoke($"XF ASR send failed: {ex.Message}");
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   webSocket != null &&
                   webSocket.State == WebSocketState.Open)
            {
                var message = await ReceiveTextAsync(cancellationToken);
                if (string.IsNullOrEmpty(message))
                {
                    break;
                }

                if (HandleResponse(message))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Error?.Invoke($"XF ASR receive failed: {ex.Message}");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(latestTranscript))
            {
                FinalResult?.Invoke(latestTranscript);
            }

            SessionEnded?.Invoke();
        }
    }

    private string BuildFrame(AudioFrame frame)
    {
        var root = new JObject
        {
            ["header"] = new JObject
            {
                ["app_id"] = config.appId,
                ["status"] = frame.Status
            },
            ["payload"] = new JObject
            {
                ["audio"] = new JObject
                {
                    ["encoding"] = "raw",
                    ["sample_rate"] = config.sampleRate,
                    ["channels"] = 1,
                    ["bit_depth"] = 16,
                    ["seq"] = ++sequence,
                    ["status"] = frame.Status,
                    ["audio"] = frame.Pcm == null || frame.Pcm.Length == 0 ? string.Empty : Convert.ToBase64String(frame.Pcm)
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(config.resId))
        {
            root["header"]["res_id"] = config.resId;
        }

        if (frame.Status == 0)
        {
            var iat = new JObject
            {
                ["domain"] = config.domain,
                ["language"] = config.language,
                ["accent"] = config.accent,
                ["eos"] = config.vadEosMs,
                ["result"] = new JObject
                {
                    ["encoding"] = "utf8",
                    ["compress"] = "raw",
                    ["format"] = "json"
                }
            };

            iat["ltc"] = config.languageTypeControl;

            if (!string.IsNullOrWhiteSpace(config.dynamicCorrection))
            {
                iat["dwa"] = config.dynamicCorrection;
            }

            iat["ptt"] = config.enablePunctuation ? 1 : 0;
            root["parameter"] = new JObject { ["iat"] = iat };
        }

        return root.ToString(Newtonsoft.Json.Formatting.None);
    }

    private bool HandleResponse(string json)
    {
        var root = JObject.Parse(json);
        var header = root["header"] as JObject;
        var code = header?["code"]?.Value<int>() ?? 0;

        if (code != 0)
        {
            var message = header?["message"]?.ToString() ?? "unknown error";
            Error?.Invoke($"XF ASR error {code}: {message}");
            return true;
        }

        var status = header?["status"]?.Value<int>() ?? -1;
        var textBase64 = root["payload"]?["result"]?["text"]?.ToString();

        if (!string.IsNullOrEmpty(textBase64))
        {
            var decodedJson = Encoding.UTF8.GetString(Convert.FromBase64String(textBase64));
            var decoded = JObject.Parse(decodedJson);
            ApplyDecodedResult(decoded);
        }

        return status == 2;
    }

    private void ApplyDecodedResult(JObject decoded)
    {
        var sn = decoded["sn"]?.Value<int>() ?? transcriptParts.Count + 1;
        var pgs = decoded["pgs"]?.ToString();
        var rg = decoded["rg"] as JArray;

        if (pgs == "rpl" && rg != null && rg.Count >= 2)
        {
            var from = rg[0].Value<int>();
            var to = rg[1].Value<int>();
            var remove = new List<int>();

            foreach (var key in transcriptParts.Keys)
            {
                if (key >= from && key <= to)
                {
                    remove.Add(key);
                }
            }

            foreach (var key in remove)
            {
                transcriptParts.Remove(key);
            }
        }

        transcriptParts[sn] = ExtractWords(decoded["ws"] as JArray);
        latestTranscript = BuildTranscript();

        if (!string.IsNullOrWhiteSpace(latestTranscript))
        {
            PartialResult?.Invoke(latestTranscript);
        }

        if (decoded["ls"]?.Value<bool>() == true && !string.IsNullOrWhiteSpace(latestTranscript))
        {
            FinalResult?.Invoke(latestTranscript);
        }
    }

    private static string ExtractWords(JArray words)
    {
        if (words == null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        foreach (var wordGroup in words)
        {
            var candidates = wordGroup["cw"] as JArray;
            if (candidates == null || candidates.Count == 0)
            {
                continue;
            }

            var word = candidates[0]?["w"]?.ToString();
            if (!string.IsNullOrEmpty(word))
            {
                builder.Append(word);
            }
        }

        return builder.ToString();
    }

    private string BuildTranscript()
    {
        var builder = new StringBuilder();

        foreach (var pair in transcriptParts)
        {
            builder.Append(pair.Value);
        }

        return builder.ToString();
    }

    private async Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task<string> ReceiveTextAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];

        using (var stream = new MemoryStream())
        {
            while (true)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                stream.Write(buffer, 0, result.Count);

                if (result.EndOfMessage)
                {
                    break;
                }
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    private void ValidateConfig()
    {
        if (config == null)
        {
            throw new InvalidOperationException("XFASRConfig is not assigned.");
        }

        if (string.IsNullOrWhiteSpace(config.appId) ||
            string.IsNullOrWhiteSpace(config.apiKey) ||
            string.IsNullOrWhiteSpace(config.apiSecret))
        {
            throw new InvalidOperationException("XFASRConfig credentials are incomplete.");
        }
    }
}
