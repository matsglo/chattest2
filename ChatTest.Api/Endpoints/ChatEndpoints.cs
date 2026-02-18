using ChatTest.Api.Models;
using ChatTest.Api.Services;
using Microsoft.Extensions.AI;

namespace ChatTest.Api.Endpoints;

public record TokenUsage(long InputTokens, long OutputTokens, int CachedTokens, long TotalTokens);

public static class ChatEndpoints
{

    public static void MapChatEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/chat");

        // ── Session CRUD ──────────────────────────────────────────────

        group.MapGet("/sessions", (ChatSessionService svc) =>
            Results.Ok(svc.ListAll().Select(s => new
            {
                s.Id, s.Title, s.CreatedAt, s.UpdatedAt
            })));

        group.MapPost("/sessions", (ChatSessionService svc) =>
        {
            var session = svc.Create();
            return Results.Created($"/api/chat/sessions/{session.Id}",
                new { session.Id, session.Title });
        });

        group.MapGet("/sessions/{id}", (string id, ChatSessionService svc) =>
        {
            var session = svc.Get(id);
            if (session is null)
                return Results.NotFound();

            return Results.Ok(new
            {
                session.Id,
                session.Title,
                session.CreatedAt,
                session.UpdatedAt,
                MessageCount = session.Messages.Count
            });
        });

        group.MapDelete("/sessions/{id}", (string id, ChatSessionService svc) =>
            svc.Delete(id) ? Results.NoContent() : Results.NotFound());

        group.MapGet("/sessions/{id}/messages", (string id, ChatSessionService svc) =>
        {
            var session = svc.Get(id);
            if (session is null)
                return Results.NotFound();

            // Build a lookup of tool results from Tool messages so we can
            // merge them into the preceding assistant message's tool parts.
            var toolResults = new Dictionary<string, object?>();
            foreach (var msg in session.Messages.Where(m => m.Role == ChatRole.Tool))
            {
                foreach (var c in msg.Contents.OfType<FunctionResultContent>())
                    toolResults[c.CallId] = c.Result;
            }

            var uiMessages = new List<(string id, string role, List<object> parts,
                long inputTokens, long outputTokens, int cachedTokens, long totalTokens)>();

            for (var i = 0; i < session.Messages.Count; i++)
            {
                var msg = session.Messages[i];
                if (msg.Role == ChatRole.System || msg.Role == ChatRole.Tool)
                    continue;

                var parts = new List<object>();

                foreach (var content in msg.Contents)
                {
                    if (content is TextContent tc && !string.IsNullOrEmpty(tc.Text))
                    {
                        parts.Add(new { type = "text", text = tc.Text });
                    }
                    else if (content is FunctionCallContent fc)
                    {
                        var hasResult = toolResults.TryGetValue(fc.CallId, out var result);
                        if (hasResult)
                        {
                            parts.Add(new
                            {
                                type = "dynamic-tool",
                                toolCallId = fc.CallId,
                                toolName = fc.Name,
                                state = "output-available",
                                input = fc.Arguments,
                                output = result
                            });
                        }
                        else
                        {
                            parts.Add(new
                            {
                                type = "dynamic-tool",
                                toolCallId = fc.CallId,
                                toolName = fc.Name,
                                state = "approval-requested",
                                input = fc.Arguments,
                                output = (object?)null,
                                approval = new { id = Guid.NewGuid().ToString("N")[..8] }
                            });
                        }
                    }
                }

                // If the message only had plain text (no Contents list),
                // fall back to m.Text
                if (parts.Count == 0 && !string.IsNullOrEmpty(msg.Text))
                    parts.Add(new { type = "text", text = msg.Text });

                var role = msg.Role == ChatRole.User ? "user" : "assistant";

                // Look up persisted usage for this message index
                session.MessageUsage.TryGetValue(i, out var msgUsage);
                long inp = msgUsage?.InputTokens ?? 0;
                long outp = msgUsage?.OutputTokens ?? 0;
                int cached = msgUsage?.CachedTokens ?? 0;
                long total = msgUsage?.TotalTokens ?? 0;

                // Merge consecutive assistant messages into one UI message
                // so that tool calls and the follow-up text appear in the same bubble.
                if (role == "assistant" && uiMessages.Count > 0 &&
                    uiMessages[^1].role == "assistant")
                {
                    var last = uiMessages[^1];
                    last.parts.AddRange(parts);
                    uiMessages[^1] = (last.id, last.role, last.parts,
                        last.inputTokens + inp, last.outputTokens + outp,
                        last.cachedTokens + cached, last.totalTokens + total);
                }
                else
                {
                    uiMessages.Add((Guid.NewGuid().ToString("N")[..8], role, parts,
                        inp, outp, cached, total));
                }
            }

            return Results.Ok(uiMessages.Select(m =>
            {
                object? metadata = m.totalTokens > 0
                    ? new
                    {
                        usage = new
                        {
                            inputTokens = m.inputTokens,
                            outputTokens = m.outputTokens,
                            cachedTokens = m.cachedTokens,
                            totalTokens = m.totalTokens
                        }
                    }
                    : null;

                return new
                {
                    id = m.id,
                    role = m.role,
                    parts = m.parts,
                    metadata
                };
            }));
        });

        // ── Streaming chat completion ─────────────────────────────────

        group.MapPost("/sessions/{id}/messages", async (
            string id,
            ChatRequest body,
            ChatSessionService sessions,
            IChatClient chatClient,
            McpToolService mcpService,
            HttpContext httpContext) =>
        {
            var session = sessions.Get(id);
            if (session is null)
                return Results.NotFound();

            // Collect tool approvals from all assistant messages (Pass 2)
            var approvals = new List<ToolApprovalInfo>();
            foreach (var msg in body.Messages
                .Where(m => m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)))
            {
                approvals.AddRange(msg.GetToolApprovals());
            }

            // Only persist new user text on Pass 1 (no approvals)
            if (approvals.Count == 0)
            {
                var userMsg = body.Messages.LastOrDefault(m =>
                    m.Role.Equals("user", StringComparison.OrdinalIgnoreCase));
                var userText = userMsg?.GetText();
                if (userText is not null)
                    sessions.AddMessage(id, ChatRole.User, userText);

                // Auto-title from first user message
                if (session.Title == "New Chat" &&
                    session.Messages.Count(m => m.Role == ChatRole.User) <= 1 &&
                    userText is not null)
                {
                    session.Title = userText.Length <= 60
                        ? userText
                        : userText[..60] + "...";
                }
            }

            var writer = new AiStreamWriter(httpContext.Response);
            writer.SetHeaders();

            var chatOptions = new ChatOptions
            {
                Tools = mcpService.Tools.ToList()
            };

            // If we have approvals from Pass 1, execute tools first
            if (approvals.Count > 0)
            {
                // The assistant message with FunctionCallContent was already
                // added to session.Messages at the end of Pass 1.
                // Now add tool results.
                var resultContents = new List<AIContent>();

                foreach (var approval in approvals)
                {
                    if (approval.Approved)
                    {
                        var tool = mcpService.Tools
                            .OfType<AIFunction>()
                            .FirstOrDefault(t => t.Name == approval.ToolName);

                        if (tool is not null)
                        {
                            try
                            {
                                var argsDict = approval.Input.HasValue
                                    ? approval.Input.Value.EnumerateObject()
                                        .ToDictionary(
                                            p => p.Name,
                                            p => (object?)p.Value)
                                    : new Dictionary<string, object?>();

                                var result = await tool.InvokeAsync(
                                    new AIFunctionArguments(argsDict),
                                    httpContext.RequestAborted);

                                resultContents.Add(
                                    new FunctionResultContent(approval.ToolCallId, result));
                                await writer.WriteToolResultAsync(
                                    approval.ToolCallId, result ?? "");
                            }
                            catch (Exception ex)
                            {
                                resultContents.Add(
                                    new FunctionResultContent(
                                        approval.ToolCallId, $"Error: {ex.Message}"));
                                await writer.WriteToolResultAsync(
                                    approval.ToolCallId, $"Error: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        resultContents.Add(
                            new FunctionResultContent(
                                approval.ToolCallId,
                                $"Tool '{approval.ToolName}' was declined by the user."));
                        await writer.WriteToolOutputDeniedAsync(approval.ToolCallId);
                    }
                }

                session.Messages.Add(new ChatMessage(ChatRole.Tool, resultContents));
                session.UpdatedAt = DateTime.UtcNow;
            }

            // Stream LLM response
            var responseText = new System.Text.StringBuilder();
            var thinkingEnabled = sessions.ThinkingEnabled;
            var inThinking = thinkingEnabled;
            var tagBuffer = "";
            var functionCallMap = new Dictionary<string, FunctionCallContent>();
            UsageContent? usageContent = null;

            await foreach (var update in chatClient.GetStreamingResponseAsync(
                session.Messages, chatOptions, httpContext.RequestAborted))
            {
                foreach (var content in update.Contents)
                {
                    // Collect function calls
                    if (content is FunctionCallContent fc)
                    {
                        functionCallMap[fc.CallId] = fc;
                        if (thinkingEnabled) inThinking = true;
                        continue;
                    }

                    if (content is UsageContent uc)
                    {
                        usageContent = uc;
                        continue;
                    }

                    if (content is not TextContent textContent ||
                        string.IsNullOrEmpty(textContent.Text))
                    {
                        if (thinkingEnabled && content is not TextContent)
                            inThinking = true;
                        continue;
                    }

                    // When thinking is disabled, pass text straight through
                    if (!thinkingEnabled)
                    {
                        await writer.WriteTextDeltaAsync(textContent.Text);
                        responseText.Append(textContent.Text);
                        continue;
                    }

                    // Parse <think> tags from the stream
                    var text = tagBuffer + textContent.Text;
                    tagBuffer = "";

                    while (text.Length > 0)
                    {
                        if (inThinking)
                        {
                            var endIdx = text.IndexOf("</think>", StringComparison.Ordinal);
                            if (endIdx >= 0)
                            {
                                var before = text[..endIdx];
                                if (before.Length > 0)
                                    await writer.WriteReasoningDeltaAsync(before);
                                inThinking = false;
                                text = text[(endIdx + "</think>".Length)..];
                            }
                            else if (text.Contains('<'))
                            {
                                var ltIdx = text.LastIndexOf('<');
                                var before = text[..ltIdx];
                                if (before.Length > 0)
                                    await writer.WriteReasoningDeltaAsync(before);
                                tagBuffer = text[ltIdx..];
                                text = "";
                            }
                            else
                            {
                                await writer.WriteReasoningDeltaAsync(text);
                                text = "";
                            }
                        }
                        else
                        {
                            var startIdx = text.IndexOf("<think>", StringComparison.Ordinal);
                            if (startIdx >= 0)
                            {
                                var before = text[..startIdx];
                                if (before.Length > 0)
                                {
                                    await writer.WriteTextDeltaAsync(before);
                                    responseText.Append(before);
                                }
                                inThinking = true;
                                text = text[(startIdx + "<think>".Length)..];
                            }
                            else if (text.Contains('<'))
                            {
                                var ltIdx = text.LastIndexOf('<');
                                var before = text[..ltIdx];
                                if (before.Length > 0)
                                {
                                    await writer.WriteTextDeltaAsync(before);
                                    responseText.Append(before);
                                }
                                tagBuffer = text[ltIdx..];
                                text = "";
                            }
                            else
                            {
                                await writer.WriteTextDeltaAsync(text);
                                responseText.Append(text);
                                text = "";
                            }
                        }
                    }
                }
            }

            // Flush any remaining buffer
            if (tagBuffer.Length > 0)
            {
                if (inThinking)
                    await writer.WriteReasoningDeltaAsync(tagBuffer);
                else
                {
                    await writer.WriteTextDeltaAsync(tagBuffer);
                    responseText.Append(tagBuffer);
                }
            }

            var functionCalls = functionCallMap.Values.ToList();

            if (functionCalls.Count > 0)
            {
                // LLM requested tool calls — persist them and request approval
                var contents = new List<AIContent>();
                var finalText = responseText.ToString().Trim();
                if (finalText.Length > 0)
                    contents.Add(new TextContent(finalText));
                contents.AddRange(functionCalls);

                session.Messages.Add(new ChatMessage(ChatRole.Assistant, contents));
                session.UpdatedAt = DateTime.UtcNow;

                foreach (var fc in functionCalls)
                {
                    await writer.WriteToolCallAsync(
                        fc.CallId,
                        fc.Name,
                        fc.Arguments ?? new Dictionary<string, object?>());
                    await writer.WriteToolApprovalRequestAsync(fc.CallId);
                }
            }
            else
            {
                // No tool calls — persist assistant text response
                var finalResponse = responseText.ToString().Trim();
                if (finalResponse.Length > 0)
                {
                    sessions.AddMessage(id, ChatRole.Assistant, finalResponse);
                }
            }

            if (usageContent?.Details is { } usage)
            {
                var cachedTokens = 0;
                usage.AdditionalCounts?.TryGetValue("CachedInputTokenCount", out cachedTokens);
                var tokenUsage = new TokenUsage(
                    usage.InputTokenCount ?? 0,
                    usage.OutputTokenCount ?? 0,
                    cachedTokens,
                    usage.TotalTokenCount ?? 0);

                // Persist usage for the last assistant message
                var lastAssistantIdx = session.Messages.FindLastIndex(
                    m => m.Role == ChatRole.Assistant);
                if (lastAssistantIdx >= 0)
                    session.MessageUsage[lastAssistantIdx] = tokenUsage;

                await writer.WriteMessageMetadataAsync(new
                {
                    usage = new
                    {
                        inputTokens = tokenUsage.InputTokens,
                        outputTokens = tokenUsage.OutputTokens,
                        cachedTokens = tokenUsage.CachedTokens,
                        totalTokens = tokenUsage.TotalTokens
                    }
                });
            }

            await writer.WriteFinishAsync();

            return Results.Empty;
        });
    }
}
