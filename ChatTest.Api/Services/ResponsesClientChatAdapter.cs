#pragma warning disable OPENAI001 // OpenAI Responses API is experimental

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

namespace ChatTest.Api.Services;

/// <summary>
/// Adapter that wraps <see cref="ResponsesClient"/> (OpenAI Responses API)
/// behind the <see cref="IChatClient"/> abstraction from Microsoft.Extensions.AI.
/// </summary>
public sealed class ResponsesClientChatAdapter : IChatClient
{
    private readonly ResponsesClient _client;
    private readonly string _model;

    public ResponsesClientChatAdapter(ResponsesClient client, string model)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    // ── IChatClient.GetResponseAsync ──────────────────────────────────
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var allContents = new List<AIContent>();
        UsageDetails? usage = null;

        await foreach (var update in GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            foreach (var content in update.Contents)
            {
                if (content is UsageContent uc)
                {
                    usage = uc.Details;
                    continue;
                }
                allContents.Add(content);
            }
        }

        var assistantMessage = new ChatMessage(ChatRole.Assistant, allContents);
        return new ChatResponse(new List<ChatMessage> { assistantMessage })
        {
            Usage = usage
        };
    }

    // ── IChatClient.GetStreamingResponseAsync ─────────────────────────
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var createOptions = BuildCreateResponseOptions(messages, options);

        await foreach (var update in _client.CreateResponseStreamingAsync(createOptions, cancellationToken))
        {
            // Text delta
            if (update is StreamingResponseOutputTextDeltaUpdate textDelta)
            {
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = new List<AIContent> { new TextContent(textDelta.Delta) }
                };
                continue;
            }

            // Output item completed — may contain function calls
            if (update is StreamingResponseOutputItemDoneUpdate itemDone)
            {
                if (itemDone.Item is FunctionCallResponseItem funcCall)
                {
                    var fc = new FunctionCallContent(
                        funcCall.CallId,
                        funcCall.FunctionName,
                        ParseArguments(funcCall.FunctionArguments));

                    yield return new ChatResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = new List<AIContent> { fc }
                    };
                }
                continue;
            }

            // Response completed — extract usage
            if (update is StreamingResponseCompletedUpdate completed)
            {
                var resp = completed.Response;
                if (resp.Usage is { } u)
                {
                    var usageDetails = new UsageDetails
                    {
                        InputTokenCount = u.InputTokenCount,
                        OutputTokenCount = u.OutputTokenCount,
                        TotalTokenCount = u.TotalTokenCount
                    };

                    // Include cached token count if available
                    if (u.InputTokenDetails?.CachedTokenCount is { } cached && cached > 0)
                    {
                        usageDetails.AdditionalCounts =
                            new AdditionalPropertiesDictionary<long> { ["CachedInputTokenCount"] = cached };
                    }

                    yield return new ChatResponseUpdate
                    {
                        Contents = new List<AIContent> { new UsageContent(usageDetails) }
                    };
                }
            }
        }
    }

    // ── IChatClient.GetService ────────────────────────────────────────
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey is null && serviceType == typeof(ResponsesClient))
            return _client;

        return null;
    }

    // ── IDisposable ──────────────────────────────────────────────────
    public void Dispose()
    {
        // ResponsesClient doesn't implement IDisposable; nothing to clean up.
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Private helpers
    // ═══════════════════════════════════════════════════════════════════

    private CreateResponseOptions BuildCreateResponseOptions(
        IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var opts = new CreateResponseOptions
        {
            Model = _model
        };

        // Convert M.E.AI ChatMessages → ResponseItem input items
        foreach (var msg in messages)
        {
            foreach (var item in ConvertMessage(msg))
            {
                opts.InputItems.Add(item);
            }
        }

        // Map tools
        if (options?.Tools is { Count: > 0 })
        {
            foreach (var tool in options.Tools)
            {
                if (tool is AIFunction aiFunc)
                {
                    var parameters = aiFunc.JsonSchema is { } schema
                        ? BinaryData.FromString(schema.ToString())
                        : BinaryData.FromString("{}");

                    opts.Tools.Add(ResponseTool.CreateFunctionTool(
                        aiFunc.Name,
                        parameters,
                        strictModeEnabled: false,
                        functionDescription: aiFunc.Description));
                }
            }
        }

        // Map common options
        if (options?.Temperature is { } temp)
            opts.Temperature = temp;

        if (options?.MaxOutputTokens is { } maxTokens)
            opts.MaxOutputTokenCount = maxTokens;

        return opts;
    }

    private static IEnumerable<ResponseItem> ConvertMessage(ChatMessage msg)
    {
        if (msg.Role == ChatRole.System)
        {
            var text = msg.Text ?? CombineTextContents(msg);
            if (!string.IsNullOrEmpty(text))
                yield return ResponseItem.CreateDeveloperMessageItem(text);
            yield break;
        }

        if (msg.Role == ChatRole.User)
        {
            var text = msg.Text ?? CombineTextContents(msg);
            if (!string.IsNullOrEmpty(text))
                yield return ResponseItem.CreateUserMessageItem(text);
            yield break;
        }

        if (msg.Role == ChatRole.Assistant)
        {
            // Text content
            var textParts = new StringBuilder();
            foreach (var content in msg.Contents)
            {
                if (content is TextContent tc && !string.IsNullOrEmpty(tc.Text))
                    textParts.Append(tc.Text);
            }
            if (textParts.Length > 0)
                yield return ResponseItem.CreateAssistantMessageItem(textParts.ToString());

            // Function calls
            foreach (var content in msg.Contents)
            {
                if (content is FunctionCallContent fc)
                {
                    var argsJson = fc.Arguments is { Count: > 0 }
                        ? JsonSerializer.Serialize(fc.Arguments)
                        : "{}";
                    yield return ResponseItem.CreateFunctionCallItem(
                        fc.CallId, fc.Name, BinaryData.FromString(argsJson));
                }
            }
            yield break;
        }

        if (msg.Role == ChatRole.Tool)
        {
            foreach (var content in msg.Contents)
            {
                if (content is FunctionResultContent frc)
                {
                    var resultStr = frc.Result is string s
                        ? s
                        : JsonSerializer.Serialize(frc.Result);
                    yield return ResponseItem.CreateFunctionCallOutputItem(
                        frc.CallId, resultStr);
                }
            }
            yield break;
        }

        // Fallback: treat as user message
        var fallbackText = msg.Text ?? CombineTextContents(msg);
        if (!string.IsNullOrEmpty(fallbackText))
            yield return ResponseItem.CreateUserMessageItem(fallbackText);
    }

    private static string CombineTextContents(ChatMessage msg)
    {
        var sb = new StringBuilder();
        foreach (var content in msg.Contents)
        {
            if (content is TextContent tc && !string.IsNullOrEmpty(tc.Text))
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(tc.Text);
            }
        }
        return sb.ToString();
    }

    private static IDictionary<string, object?>? ParseArguments(BinaryData? argsData)
    {
        if (argsData is null)
            return null;

        var argsJson = argsData.ToString();
        if (string.IsNullOrWhiteSpace(argsJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(argsJson);
        }
        catch
        {
            return new Dictionary<string, object?> { ["raw"] = argsJson };
        }
    }
}
