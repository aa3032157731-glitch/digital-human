using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class DeepSeekService
{
    public sealed class ToolCall
    {
        public string id;
        public string type;
        public string functionName;
        public string arguments;

        public ToolCall()
        {
        }

        public ToolCall(string id, string type, string functionName, string arguments)
        {
            this.id = id;
            this.type = type;
            this.functionName = functionName;
            this.arguments = arguments;
        }
    }

    public sealed class ChatCompletionResult
    {
        public string content;
        public string finishReason;
        public List<ToolCall> toolCalls = new List<ToolCall>();
    }

    public struct ChatMessage
    {
        public string role;
        public string content;
        public string toolCallId;
        public List<ToolCall> toolCalls;

        public ChatMessage(string role, string content)
        {
            this.role = role;
            this.content = content;
            toolCallId = null;
            toolCalls = null;
        }

        public static ChatMessage AssistantToolCalls(IReadOnlyList<ToolCall> calls, string content = null)
        {
            return new ChatMessage
            {
                role = "assistant",
                content = content,
                toolCalls = calls == null ? new List<ToolCall>() : new List<ToolCall>(calls)
            };
        }

        public static ChatMessage ToolResult(string toolCallId, string content)
        {
            return new ChatMessage
            {
                role = "tool",
                content = content ?? string.Empty,
                toolCallId = toolCallId
            };
        }
    }

    private static readonly HttpClient HttpClient = new HttpClient();
    private readonly DeepSeekConfig config;

    public DeepSeekService(DeepSeekConfig config)
    {
        this.config = config;
    }

    public async Task StreamChatAsync(
        IReadOnlyList<ChatMessage> conversation,
        Action<string> onDelta,
        CancellationToken cancellationToken,
        string additionalSystemPrompt = null)
    {
        ValidateConfig();

        var requestBody = BuildRequestBody(conversation, additionalSystemPrompt, true, null, null);
        LogRequestPayload(requestBody);
        var request = new HttpRequestMessage(HttpMethod.Post, config.chatCompletionsUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent(requestBody.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        using (var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            var bodyStream = await response.Content.ReadAsStreamAsync();

            if (!response.IsSuccessStatusCode)
            {
                using (var reader = new StreamReader(bodyStream, Encoding.UTF8))
                {
                    var errorBody = await reader.ReadToEndAsync();
                    throw new InvalidOperationException($"DeepSeek HTTP {(int)response.StatusCode}: {errorBody}");
                }
            }

            using (var reader = new StreamReader(bodyStream, Encoding.UTF8))
            {
                while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var data = line.Substring("data:".Length).Trim();
                    if (data == "[DONE]")
                    {
                        break;
                    }

                    var delta = ParseDelta(data);
                    if (!string.IsNullOrEmpty(delta))
                    {
                        onDelta?.Invoke(delta);
                    }
                }
            }
        }
    }

    public async Task<ChatCompletionResult> CompleteChatAsync(
        IReadOnlyList<ChatMessage> conversation,
        CancellationToken cancellationToken,
        string additionalSystemPrompt = null,
        JArray tools = null,
        string toolChoice = null)
    {
        ValidateConfig();

        var requestBody = BuildRequestBody(conversation, additionalSystemPrompt, false, tools, toolChoice);
        LogRequestPayload(requestBody);
        var request = new HttpRequestMessage(HttpMethod.Post, config.chatCompletionsUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(requestBody.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

        using (var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken))
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"DeepSeek HTTP {(int)response.StatusCode}: {responseBody}");
            }

            return ParseCompletionResult(responseBody);
        }
    }

    private JObject BuildRequestBody(
        IReadOnlyList<ChatMessage> conversation,
        string additionalSystemPrompt,
        bool stream,
        JArray tools,
        string toolChoice)
    {
        var messages = new JArray();
        var systemPromptBuilder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(config.systemPrompt))
        {
            systemPromptBuilder.AppendLine(config.systemPrompt.Trim());
        }

        if (!string.IsNullOrWhiteSpace(additionalSystemPrompt))
        {
            if (systemPromptBuilder.Length > 0)
            {
                systemPromptBuilder.AppendLine();
            }

            systemPromptBuilder.AppendLine(additionalSystemPrompt.Trim());
        }

        if (systemPromptBuilder.Length > 0)
        {
            messages.Add(new JObject
            {
                ["role"] = "system",
                ["content"] = systemPromptBuilder.ToString().Trim()
            });
        }

        if (conversation != null)
        {
            foreach (var message in conversation)
            {
                if (string.IsNullOrWhiteSpace(message.role))
                {
                    continue;
                }

                var role = message.role.Trim();
                if (role == "tool")
                {
                    if (string.IsNullOrWhiteSpace(message.toolCallId))
                    {
                        continue;
                    }

                    messages.Add(new JObject
                    {
                        ["role"] = "tool",
                        ["content"] = message.content ?? string.Empty,
                        ["tool_call_id"] = message.toolCallId
                    });
                    continue;
                }

                if (role == "assistant" && message.toolCalls != null && message.toolCalls.Count > 0)
                {
                    var toolCalls = new JArray();
                    foreach (var toolCall in message.toolCalls)
                    {
                        if (toolCall == null || string.IsNullOrWhiteSpace(toolCall.id) || string.IsNullOrWhiteSpace(toolCall.functionName))
                        {
                            continue;
                        }

                        toolCalls.Add(new JObject
                        {
                            ["id"] = toolCall.id,
                            ["type"] = string.IsNullOrWhiteSpace(toolCall.type) ? "function" : toolCall.type,
                            ["function"] = new JObject
                            {
                                ["name"] = toolCall.functionName,
                                ["arguments"] = toolCall.arguments ?? "{}"
                            }
                        });
                    }

                    if (toolCalls.Count == 0)
                    {
                        continue;
                    }

                    messages.Add(new JObject
                    {
                        ["role"] = "assistant",
                        ["content"] = message.content == null ? JValue.CreateNull() : new JValue(message.content),
                        ["tool_calls"] = toolCalls
                    });
                    continue;
                }

                if (string.IsNullOrWhiteSpace(message.content))
                {
                    continue;
                }

                messages.Add(new JObject
                {
                    ["role"] = role,
                    ["content"] = message.content
                });
            }
        }

        var body = new JObject
        {
            ["model"] = config.model,
            ["messages"] = messages,
            ["stream"] = stream,
            ["temperature"] = config.temperature,
            ["top_p"] = config.topP,
            ["max_tokens"] = config.maxTokens
        };

        if (tools != null && tools.Count > 0)
        {
            body["tools"] = tools;
            if (!string.IsNullOrWhiteSpace(toolChoice))
            {
                body["tool_choice"] = toolChoice;
            }
        }

        if (config.disableThinking)
        {
            body["thinking"] = new JObject { ["type"] = "disabled" };
        }

        return body;
    }

    private void LogRequestPayload(JObject requestBody)
    {
        if (config == null || !config.logRequestPayloadToConsole || requestBody == null)
        {
            return;
        }

        var payload = requestBody.ToString(Newtonsoft.Json.Formatting.None);
        var chunkSize = Math.Max(200, config.requestPayloadLogChunkCharacters);
        var chunkCount = (payload.Length + chunkSize - 1) / chunkSize;

        Debug.Log($"DeepSeek HTTP request payload JSON BEGIN length={payload.Length}, chunks={chunkCount}, url={config.chatCompletionsUrl}");

        for (var offset = 0; offset < payload.Length; offset += chunkSize)
        {
            var chunkIndex = offset / chunkSize + 1;
            var count = Math.Min(chunkSize, payload.Length - offset);
            Debug.Log($"DeepSeek HTTP request payload JSON [{chunkIndex}/{chunkCount}] {payload.Substring(offset, count)}");
        }

        Debug.Log("DeepSeek HTTP request payload JSON END");
    }

    private static string ParseDelta(string data)
    {
        var root = JObject.Parse(data);
        var choices = root["choices"] as JArray;

        if (choices == null || choices.Count == 0)
        {
            return null;
        }

        return choices[0]?["delta"]?["content"]?.ToString();
    }

    private static ChatCompletionResult ParseCompletionResult(string body)
    {
        var result = new ChatCompletionResult();
        if (string.IsNullOrWhiteSpace(body))
        {
            return result;
        }

        var root = JObject.Parse(body);
        var choices = root["choices"] as JArray;
        if (choices == null || choices.Count == 0)
        {
            return result;
        }

        var choice = choices[0] as JObject;
        result.finishReason = choice?["finish_reason"]?.ToString();
        var message = choice?["message"] as JObject;
        if (message == null)
        {
            return result;
        }

        result.content = message["content"]?.Type == JTokenType.Null ? string.Empty : message["content"]?.ToString();
        var toolCalls = message["tool_calls"] as JArray;
        if (toolCalls == null)
        {
            return result;
        }

        foreach (var token in toolCalls)
        {
            var item = token as JObject;
            if (item == null)
            {
                continue;
            }

            var function = item["function"] as JObject;
            result.toolCalls.Add(new ToolCall(
                item["id"]?.ToString(),
                item["type"]?.ToString(),
                function?["name"]?.ToString(),
                function?["arguments"]?.ToString()));
        }

        return result;
    }

    private void ValidateConfig()
    {
        if (config == null)
        {
            throw new InvalidOperationException("DeepSeekConfig is not assigned.");
        }

        if (string.IsNullOrWhiteSpace(config.apiKey))
        {
            throw new InvalidOperationException("DeepSeekConfig apiKey is empty.");
        }
    }
}
