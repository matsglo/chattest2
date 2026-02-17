using ChatTest.Api.Models;
using ChatTest.Api.Services;
using Microsoft.Agents.AI;
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
            ChatClientAgent agent,
            HttpContext httpContext) =>
        {
            var session = sessions.Get(id);
            if (session is null)
                return Results.NotFound();

            // Persist the new user message
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

            var writer = new AiStreamWriter(httpContext.Response);
            writer.SetHeaders();

            var fullResponse = new System.Text.StringBuilder();
            var inThinking = true;
            var tagBuffer = "";

            // Stream via Agent Framework
            await foreach (var update in agent.RunStreamingAsync(
                session.Messages))
            {
                foreach (var content in update.Contents)
                {
                    if (content is not TextContent textContent ||
                        string.IsNullOrEmpty(textContent.Text))
                        continue;

                    fullResponse.Append(textContent.Text);

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
                                // Might be a partial </think> tag — buffer from '<'
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
                                    await writer.WriteTextDeltaAsync(before);
                                inThinking = true;
                                text = text[(startIdx + "<think>".Length)..];
                            }
                            else if (text.Contains('<'))
                            {
                                // Might be a partial <think> tag — buffer from '<'
                                var ltIdx = text.LastIndexOf('<');
                                var before = text[..ltIdx];
                                if (before.Length > 0)
                                    await writer.WriteTextDeltaAsync(before);
                                tagBuffer = text[ltIdx..];
                                text = "";
                            }
                            else
                            {
                                await writer.WriteTextDeltaAsync(text);
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
                    await writer.WriteTextDeltaAsync(tagBuffer);
            }

            // Persist assistant response (strip think tags for storage)
            if (fullResponse.Length > 0)
            {
                var cleanResponse = System.Text.RegularExpressions.Regex.Replace(
                    fullResponse.ToString(), @"<think>[\s\S]*?</think>", "").Trim();
                if (cleanResponse.Length > 0)
                    sessions.AddMessage(id, ChatRole.Assistant, cleanResponse);
            }

            await writer.WriteFinishAsync();

            return Results.Empty;
        });
    }
}
