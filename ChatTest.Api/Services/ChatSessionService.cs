using System.Collections.Concurrent;
using ChatTest.Api.Endpoints;
using Microsoft.Extensions.AI;

namespace ChatTest.Api.Services;

public sealed class ChatSession
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "New Chat";
    public string SystemPrompt { get; set; } = "You are a helpful assistant.";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ChatMessage> Messages { get; } = [];

    /// <summary>
    /// Token usage data keyed by message index in the Messages list.
    /// </summary>
    public Dictionary<int, TokenUsage> MessageUsage { get; } = new();
}

public sealed class ChatSessionService
{
    private readonly bool _thinkingEnabled;
    private readonly ConcurrentDictionary<string, ChatSession> _sessions = new();

    private const string ThinkingInstruction =
        " Always wrap your internal reasoning inside <think>...</think> tags before giving your final answer." +
        " The content inside <think> tags will be hidden from the user by default.";

    public ChatSessionService(IConfiguration configuration)
    {
        _thinkingEnabled = configuration.GetValue<bool>("AI:Thinking");
    }

    public bool ThinkingEnabled => _thinkingEnabled;

    public ChatSession Create()
    {
        var session = new ChatSession();
        if (_thinkingEnabled)
            session.SystemPrompt += ThinkingInstruction;
        session.Messages.Add(new ChatMessage(ChatRole.System, session.SystemPrompt));
        _sessions[session.Id] = session;
        return session;
    }

    public ChatSession? Get(string id) =>
        _sessions.GetValueOrDefault(id);

    public List<ChatSession> ListAll() =>
        _sessions.Values
            .OrderByDescending(s => s.UpdatedAt)
            .ToList();

    public bool Delete(string id) =>
        _sessions.TryRemove(id, out _);

    public void AddMessage(string sessionId, ChatRole role, string content)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.Messages.Add(new ChatMessage(role, content));
            session.UpdatedAt = DateTime.UtcNow;
        }
    }
}
