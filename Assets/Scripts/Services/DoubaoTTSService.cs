using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public sealed class DoubaoTTSService : ITTSService
{
    public event Action<byte[]> AudioChunkReceived;
    public event Action<string> Error;
    public event Action Completed;

    public int OutputSampleRate => config != null ? config.sampleRate : 24000;

    private static readonly HttpClient HttpClient = new HttpClient();

    private readonly DoubaoTTSConfig config;
    private readonly string voiceInstruction;
    private readonly string sectionId;
    private readonly ConcurrentQueue<string> textQueue = new ConcurrentQueue<string>();
    private readonly TaskCompletionSource<bool> completionSource = new TaskCompletionSource<bool>();
    private CancellationTokenSource internalCts;
    private bool inputCompleted;

    public DoubaoTTSService(DoubaoTTSConfig config, string voiceInstruction, string sectionId)
    {
        this.config = config;
        this.voiceInstruction = voiceInstruction;
        this.sectionId = sectionId;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ValidateConfig();

        if (!string.Equals(config.audioFormat, "pcm", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This Unity streaming player expects Doubao TTS audioFormat=pcm.");
        }

        internalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => WorkerLoopAsync(internalCts.Token));
        return Task.CompletedTask;
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

    public Task CancelAsync()
    {
        internalCts?.Cancel();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        internalCts?.Cancel();
        internalCts?.Dispose();
    }

    private async Task WorkerLoopAsync(CancellationToken cancellationToken)
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
                    await SynthesizeOnceAsync(text, cancellationToken);
                    continue;
                }

                if (inputCompleted && pending.Length == 0 && textQueue.IsEmpty)
                {
                    break;
                }

                await Task.Delay(Math.Max(5, config.textPollIntervalMs), cancellationToken);
            }

            completionSource.TrySetResult(true);
        }
        catch (OperationCanceledException)
        {
            completionSource.TrySetCanceled();
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Doubao TTS failed: {ex.Message}");
            completionSource.TrySetException(ex);
        }
        finally
        {
            Completed?.Invoke();
        }
    }

    private async Task SynthesizeOnceAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, config.requestTimeoutSeconds)));

            using (var request = new HttpRequestMessage(HttpMethod.Post, config.endpoint))
            {
                AddHeaders(request);
                var payload = BuildPayload(text);
                request.Content = new StringContent(payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

                using (var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token))
                {
                    var bodyStream = await response.Content.ReadAsStreamAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        using (var reader = new StreamReader(bodyStream, Encoding.UTF8))
                        {
                            var errorBody = await reader.ReadToEndAsync();
                            throw new InvalidOperationException($"Doubao TTS HTTP {(int)response.StatusCode}: {errorBody}");
                        }
                    }

                    await ReadSseAsync(bodyStream, timeoutCts.Token);
                }
            }
        }
    }

    private void AddHeaders(HttpRequestMessage request)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.TryAddWithoutValidation("X-Api-Request-Id", Guid.NewGuid().ToString("N"));
        request.Headers.TryAddWithoutValidation("X-Api-Resource-Id", string.IsNullOrWhiteSpace(config.resourceId) ? "seed-tts-2.0" : config.resourceId.Trim());

        if (!string.IsNullOrWhiteSpace(config.apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-Api-Key", config.apiKey.Trim());
            return;
        }

        request.Headers.TryAddWithoutValidation("X-Api-App-Id", config.appId.Trim());
        request.Headers.TryAddWithoutValidation("X-Api-Access-Key", config.accessKey.Trim());
    }

    private JObject BuildPayload(string text)
    {
        var audioParams = new JObject
        {
            ["format"] = string.IsNullOrWhiteSpace(config.audioFormat) ? "pcm" : config.audioFormat.Trim(),
            ["sample_rate"] = Math.Max(8000, config.sampleRate),
            ["speech_rate"] = Clamp(config.speechRate, -50, 100),
            ["loudness_rate"] = Clamp(config.loudnessRate, -50, 100)
        };

        if (!string.IsNullOrWhiteSpace(config.emotion))
        {
            audioParams["emotion"] = config.emotion.Trim();
            audioParams["emotion_scale"] = Clamp(config.emotionScale, 1, 5);
        }

        var reqParams = new JObject
        {
            ["text"] = text,
            ["speaker"] = config.speaker.Trim(),
            ["audio_params"] = audioParams
        };

        if (!string.IsNullOrWhiteSpace(config.model))
        {
            reqParams["model"] = config.model.Trim();
        }

        var additions = new JObject();
        if (!string.IsNullOrWhiteSpace(voiceInstruction))
        {
            additions["context_texts"] = new JArray(voiceInstruction.Trim());
        }

        if (config.useSectionId && !string.IsNullOrWhiteSpace(sectionId))
        {
            additions["section_id"] = sectionId;
        }

        if (additions.Count > 0)
        {
            reqParams["additions"] = additions.ToString(Newtonsoft.Json.Formatting.None);
            UnityEngine.Debug.Log("Doubao TTS additions: " + reqParams["additions"]);
        }

        return new JObject
        {
            ["user"] = new JObject
            {
                ["uid"] = string.IsNullOrWhiteSpace(config.userId) ? "custom-digitalman" : config.userId.Trim()
            },
            ["namespace"] = "BidirectionalTTS",
            ["req_params"] = reqParams
        };
    }

    private async Task ReadSseAsync(Stream bodyStream, CancellationToken cancellationToken)
    {
        using (var reader = new StreamReader(bodyStream, Encoding.UTF8))
        {
            var dataBuilder = new StringBuilder();

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (line == null)
                {
                    break;
                }

                if (line.Length == 0)
                {
                    if (dataBuilder.Length > 0)
                    {
                        if (HandleResponse(dataBuilder.ToString()))
                        {
                            return;
                        }

                        dataBuilder.Length = 0;
                    }

                    continue;
                }

                if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    dataBuilder.Append(line.Substring("data:".Length).Trim());
                    continue;
                }

                if (line.StartsWith("{", StringComparison.Ordinal))
                {
                    if (HandleResponse(line.Trim()))
                    {
                        return;
                    }
                }
            }

            if (dataBuilder.Length > 0)
            {
                HandleResponse(dataBuilder.ToString());
            }
        }
    }

    private bool HandleResponse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[DONE]")
        {
            return true;
        }

        var root = JObject.Parse(json);
        var code = root["code"]?.Value<int>() ?? 0;
        var message = root["message"]?.ToString();

        if (code != 0 && code != 20000000)
        {
            Error?.Invoke($"Doubao TTS error {code}: {message}");
            return true;
        }

        var audioBase64 = root["data"]?.ToString();
        if (!string.IsNullOrWhiteSpace(audioBase64))
        {
            AudioChunkReceived?.Invoke(Convert.FromBase64String(audioBase64));
        }

        return code == 20000000;
    }

    private static bool EndsWithSentencePunctuation(StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            return false;
        }

        var c = builder[builder.Length - 1];
        return c == '\u3002' || c == '\uff01' || c == '\uff1f' || c == '\uff1b' ||
               c == '.' || c == '!' || c == '?' || c == ';' || c == '\n';
    }

    private static string TakeChunk(StringBuilder builder, int maxChars)
    {
        var count = Math.Min(Math.Max(1, maxChars), builder.Length);
        var text = builder.ToString(0, count);
        builder.Remove(0, count);
        return text;
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(max, Math.Max(min, value));
    }

    private void ValidateConfig()
    {
        if (config == null)
        {
            throw new InvalidOperationException("DoubaoTTSConfig is not assigned.");
        }

        if (string.IsNullOrWhiteSpace(config.endpoint))
        {
            throw new InvalidOperationException("DoubaoTTSConfig endpoint is empty.");
        }

        if (string.IsNullOrWhiteSpace(config.speaker))
        {
            throw new InvalidOperationException("DoubaoTTSConfig speaker is empty.");
        }

        if (string.IsNullOrWhiteSpace(config.apiKey) &&
            (string.IsNullOrWhiteSpace(config.appId) || string.IsNullOrWhiteSpace(config.accessKey)))
        {
            throw new InvalidOperationException("DoubaoTTSConfig credentials are incomplete.");
        }
    }
}
