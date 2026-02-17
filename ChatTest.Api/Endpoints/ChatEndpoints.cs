using ChatTest.Api.Models;
using ChatTest.Api.Services;
using Microsoft.Extensions.AI;

namespace ChatTest.Api.Endpoints;

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

            var messages = session.Messages
                .Where(m => m.Role != ChatRole.System)
                .Select(m => new
                {
                    id = Guid.NewGuid().ToString("N")[..8],
                    role = m.Role == ChatRole.User ? "user" : "assistant",
                    parts = new[] { new { type = "text", text = m.Text } }
                });

            return Results.Ok(messages);
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
            var inThinking = true;
            var tagBuffer = "";
            var functionCallMap = new Dictionary<string, FunctionCallContent>();

            await foreach (var update in chatClient.GetStreamingResponseAsync(
                session.Messages, chatOptions, httpContext.RequestAborted))
            {
                foreach (var content in update.Contents)
                {
                    // Collect function calls
                    if (content is FunctionCallContent fc)
                    {
                        functionCallMap[fc.CallId] = fc;
                        inThinking = true;
                        continue;
                    }

                    if (content is not TextContent textContent ||
                        string.IsNullOrEmpty(textContent.Text))
                    {
                        if (content is not TextContent)
                            inThinking = true;
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

            await writer.WriteFinishAsync();

            return Results.Empty;
        });
    }
}
