using System.Text.Json;

namespace ChatTest.Api.Services;

/// <summary>
/// Writes the Vercel AI UI Message Stream protocol (v1) to an HttpResponse stream.
/// Format: SSE events with JSON payloads.
///
/// Chunk types:
///   text-start              Start of a text part
///   text-delta              Text content delta
///   text-end                End of a text part
///   reasoning-start         Start of a reasoning part
///   reasoning-delta         Reasoning content delta
///   reasoning-end           End of a reasoning part
///   tool-input-start        Start of a tool call
///   tool-input-available    Tool call input is complete
///   tool-approval-request   Request user approval for a tool call
///   tool-output-available   Tool call result
///   tool-output-denied      Tool call was declined by user
/// </summary>
public sealed class AiStreamWriter(HttpResponse response)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private string? _currentTextPartId;
    private string? _currentReasoningPartId;

    public void SetHeaders()
    {
        response.ContentType = "text/event-stream";
        response.Headers["X-Vercel-AI-UI-Message-Stream"] = "v1";
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
    }

    private async Task WriteSseEventAsync(object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        await response.WriteAsync($"data: {json}\n\n");
        await response.Body.FlushAsync();
    }

    public async Task WriteTextDeltaAsync(string text)
    {
        await EndReasoningPartAsync();

        if (_currentTextPartId is null)
        {
            _currentTextPartId = Guid.NewGuid().ToString("N")[..8];
            await WriteSseEventAsync(new { type = "text-start", id = _currentTextPartId });
        }

        await WriteSseEventAsync(new { type = "text-delta", id = _currentTextPartId, delta = text });
    }

    public async Task WriteReasoningDeltaAsync(string text)
    {
        await EndTextPartAsync();

        if (_currentReasoningPartId is null)
        {
            _currentReasoningPartId = Guid.NewGuid().ToString("N")[..8];
            await WriteSseEventAsync(new { type = "reasoning-start", id = _currentReasoningPartId });
        }

        await WriteSseEventAsync(new { type = "reasoning-delta", id = _currentReasoningPartId, delta = text });
    }

    public async Task WriteToolCallAsync(string toolCallId, string toolName, object args)
    {
        await EndTextPartAsync();
        await EndReasoningPartAsync();
        await WriteSseEventAsync(new
        {
            type = "tool-input-start",
            toolCallId,
            toolName,
            dynamic = true
        });
        await WriteSseEventAsync(new
        {
            type = "tool-input-available",
            toolCallId,
            toolName,
            input = args,
            dynamic = true
        });
    }

    public async Task WriteToolApprovalRequestAsync(string toolCallId)
    {
        await WriteSseEventAsync(new
        {
            type = "tool-approval-request",
            toolCallId,
            approvalId = Guid.NewGuid().ToString("N")[..8]
        });
    }

    public async Task WriteToolResultAsync(string toolCallId, object result)
    {
        await WriteSseEventAsync(new
        {
            type = "tool-output-available",
            toolCallId,
            output = result
        });
    }

    public async Task WriteToolOutputDeniedAsync(string toolCallId)
    {
        await WriteSseEventAsync(new
        {
            type = "tool-output-denied",
            toolCallId
        });
    }

    public async Task WriteMessageMetadataAsync(object metadata)
    {
        await EndTextPartAsync();
        await EndReasoningPartAsync();
        await WriteSseEventAsync(new { type = "message-metadata", messageMetadata = metadata });
    }

    public async Task WriteFinishAsync()
    {
        await EndTextPartAsync();
        await EndReasoningPartAsync();
        await response.WriteAsync("data: [DONE]\n\n");
        await response.Body.FlushAsync();
    }

    private async Task EndTextPartAsync()
    {
        if (_currentTextPartId is not null)
        {
            await WriteSseEventAsync(new { type = "text-end", id = _currentTextPartId });
            _currentTextPartId = null;
        }
    }

    private async Task EndReasoningPartAsync()
    {
        if (_currentReasoningPartId is not null)
        {
            await WriteSseEventAsync(new { type = "reasoning-end", id = _currentReasoningPartId });
            _currentReasoningPartId = null;
        }
    }
}
