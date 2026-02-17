using System.Text.Json;

namespace ChatTest.Api.Models;

public sealed record ChatRequest(
    string? Id,
    List<UIMessageDto> Messages);

public sealed record ToolApprovalInfo(
    string ToolCallId,
    string ToolName,
    bool Approved,
    JsonElement? Input);

public sealed record UIMessageDto(
    string? Id,
    string Role,
    List<JsonElement>? Parts)
{
    public string? GetText()
    {
        if (Parts is null) return null;

        var texts = new List<string>();
        foreach (var part in Parts)
        {
            if (part.TryGetProperty("type", out var typeProp) &&
                typeProp.GetString() == "text" &&
                part.TryGetProperty("text", out var textProp))
            {
                var text = textProp.GetString();
                if (text is not null)
                    texts.Add(text);
            }
        }
        return texts.Count > 0 ? string.Join("", texts) : null;
    }

    public List<ToolApprovalInfo> GetToolApprovals()
    {
        var approvals = new List<ToolApprovalInfo>();
        if (Parts is null) return approvals;

        foreach (var part in Parts)
        {
            if (part.TryGetProperty("type", out var typeProp) &&
                typeProp.GetString() == "dynamic-tool" &&
                part.TryGetProperty("state", out var stateProp) &&
                stateProp.GetString() == "approval-responded")
            {
                var toolCallId = part.TryGetProperty("toolCallId", out var tcId)
                    ? tcId.GetString() : null;
                var toolName = part.TryGetProperty("toolName", out var tn)
                    ? tn.GetString() : null;

                var approved = false;
                if (part.TryGetProperty("approval", out var approvalObj) &&
                    approvalObj.TryGetProperty("approved", out var ap))
                {
                    approved = ap.GetBoolean();
                }

                JsonElement? input = part.TryGetProperty("input", out var inputProp)
                    ? inputProp : null;

                if (toolCallId is not null && toolName is not null)
                {
                    approvals.Add(new ToolApprovalInfo(toolCallId, toolName, approved, input));
                }
            }
        }
        return approvals;
    }
}
