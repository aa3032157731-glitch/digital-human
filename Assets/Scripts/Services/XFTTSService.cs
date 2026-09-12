using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public sealed class XFTTSService : ITTSService
{
    public event Action<byte[]> AudioChunkReceived;
    public event Action<string> Error;
    public event Action Completed;

    public int OutputSampleRate => config != null ? config.sampleRate : 24000;

    private readonly XFTTSConfig config;
    private readonly ConcurrentQueue<string> textQueue = new ConcurrentQueue<string>();
    private readonly TaskCompletionSource<bool> completionSource = new TaskCompletionSource<bool>();
    private ClientWebSocket webSocket;
    private CancellationTokenSource internalCts;
    private Task sendTask;
    private Task receiveTask;
    private int sequence;
    private bool inputCompleted;
    private bool sentFirstFrame;

    public XFTTSService(XFTTSConfig config)
    {
        this.config = config;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ValidateConfig();

        if (!string.Equals(config.audioEncoding, "raw", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This Unity streaming player expects Xunfei TTS audioEncoding=raw PCM.");
        }

        internalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        webSocket = new ClientWebSocket();
        var uri = XFWebSocketAuth.BuildAuthenticatedUri(config.websocketUrl, config.apiKey, config.apiSecret);

        await webSocket.ConnectAsync(uri, internalCts.Token);

        sendTask = Task.Run(() => SendLoopAsync(internalCts.Token));
        receiveTask = Task.Run(() => ReceiveLoopAsync(internalCts.Token));
    }

    public void EnqueueText(string text)
    {
        if (string.IsNullOrEmpty(text) || inputCompleted)
        {
            return;
        }

        textQueue.Enqueue(text);
    }

    public void CompleteInput()
    {
        inputCompleted = true;
    }

    public Task WaitForCompletionAsync()
    {
        return completionSource.Task;
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
        var pending = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                while (textQueue.TryDequeue(out var delta))
                {
                    pending.Append(delta);
                }

                var shouldFlush = pending.Length >= config.minChunkChars ||
                                  EndsWithSentencePunctuation(pending) ||
                                  (inputCompleted && pending.Length > 0);

                if (shouldFlush)
                {
                    var text = TakeChunk(pending, config.maxChunkChars);
                    var status = sentFirstFrame ? 1 : 0;
                    await SendTextFrameAsync(text, status, cancellationToken);
                    sentFirstFrame = true;
                    continue;
                }

                if (inputCompleted && pending.Length == 0)
                {
                    await SendTextFrameAsync(string.Empty, sentFirstFrame ? 2 : 2, cancellationToken);
                    break;
                }

                await Task.Delay(Math.Max(5, config.textPollIntervalMs), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Error?.Invoke($"XF TTS send failed: {ex.Message}");
            completionSource.TrySetException(ex);
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

            completionSource.TrySetResult(true);
        }
        catch (OperationCanceledException)
        {
            completionSource.TrySetCanceled();
        }
        catch (Exception ex)
        {
            Error?.Invoke($"XF TTS receive failed: {ex.Message}");
            completionSource.TrySetException(ex);
        }
        finally
        {
            Completed?.Invoke();
        }
    }

    private async Task SendTextFrameAsync(string text, int status, CancellationToken cancellationToken)
    {
        var json = BuildFrame(text, status);
        var bytes = Encoding.UTF8.GetBytes(json);
        await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    private string BuildFrame(string text, int status)
    {
        var root = new JObject
        {
            ["header"] = new JObject
            {
                ["app_id"] = config.appId,
                ["status"] = status
            },
            ["payload"] = new JObject
            {
                ["text"] = new JObject
                {
                    ["encoding"] = "utf8",
                    ["compress"] = "raw",
                    ["format"] = "plain",
                    ["status"] = status,
                    ["seq"] = sequence++,
                    ["text"] = EncodeText(text)
                }
            }
        };

        if (status == 0 || !sentFirstFrame)
        {
            root["parameter"] = new JObject
            {
                ["oral"] = new JObject
                {
                    ["oral_level"] = string.IsNullOrWhiteSpace(config.oralLevel) ? "mid" : config.oralLevel,
                    ["spark_assist"] = config.sparkAssist ? 1 : 0,
                    ["stop_split"] = config.stopSplit ? 1 : 0,
                    ["remain"] = config.remainWrittenStyle ? 1 : 0
                },
                ["tts"] = new JObject
                {
                    ["vcn"] = config.voiceName,
                    ["speed"] = config.speed,
                    ["volume"] = config.volume,
                    ["pitch"] = config.pitch,
                    ["bgs"] = 0,
                    ["reg"] = 0,
                    ["rdn"] = 0,
                    ["rhy"] = 0,
                    ["audio"] = new JObject
                    {
                        ["encoding"] = config.audioEncoding,
                        ["sample_rate"] = config.sampleRate,
                        ["channels"] = 1,
                        ["bit_depth"] = 16,
                        ["frame_size"] = 0
                    }
                }
            };
        }

        return root.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string EncodeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
    }

    private bool HandleResponse(string json)
    {
        var root = JObject.Parse(json);
        var header = root["header"] as JObject;
        var code = header?["code"]?.Value<int>() ?? 0;

        if (code != 0)
        {
            var message = header?["message"]?.ToString() ?? "unknown error";
            Error?.Invoke($"XF TTS error {code}: {message}");
            return true;
        }

        var audioBase64 = root["payload"]?["audio"]?["audio"]?.ToString();
        if (!string.IsNullOrEmpty(audioBase64))
        {
            AudioChunkReceived?.Invoke(Convert.FromBase64String(audioBase64));
        }

        var status = header?["status"]?.Value<int>() ?? root["payload"]?["audio"]?["status"]?.Value<int>() ?? -1;
        return status == 2;
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

    private static bool EndsWithSentencePunctuation(StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            return false;
        }

        var c = builder[builder.Length - 1];
        return c == '。' || c == '！' || c == '？' || c == '；' ||
               c == '.' || c == '!' || c == '?' || c == ';' || c == '\n';
    }

    private static string TakeChunk(StringBuilder builder, int maxChars)
    {
        var count = Math.Min(Math.Max(1, maxChars), builder.Length);
        var text = builder.ToString(0, count);
        builder.Remove(0, count);
        return text;
    }

    private void ValidateConfig()
    {
        if (config == null)
        {
            throw new InvalidOperationException("XFTTSConfig is not assigned.");
        }

        if (string.IsNullOrWhiteSpace(config.appId) ||
            string.IsNullOrWhiteSpace(config.apiKey) ||
            string.IsNullOrWhiteSpace(config.apiSecret))
        {
            throw new InvalidOperationException("XFTTSConfig credentials are incomplete.");
        }
    }
}
