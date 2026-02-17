using System.Text.Json;

namespace ChatTest.Api.Models;

public sealed record ChatRequest(
    string? Id,
    List<UIMessageDto> Messages);

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
}
