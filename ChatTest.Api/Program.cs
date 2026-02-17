using ChatTest.Api.Endpoints;
using ChatTest.Api.Services;
using Microsoft.Extensions.AI;
using OpenAI;

var builder = WebApplication.CreateBuilder(args);

// ── Aspire ServiceDefaults (OTel, health checks, resilience) ─────────
builder.AddServiceDefaults();

// ── Application services ─────────────────────────────────────────────
builder.Services.AddSingleton<ChatSessionService>();

// ── Microsoft Agent Framework ────────────────────────────────────────
var aiConfig = builder.Configuration.GetSection("AI");
var modelId = aiConfig["ModelId"] ?? "gpt-4.1";
var apiKey = aiConfig["ApiKey"] ?? "lm-studio";
var endpoint = aiConfig["Endpoint"];

var clientOptions = new OpenAIClientOptions();
if (!string.IsNullOrEmpty(endpoint))
    clientOptions.Endpoint = new Uri(endpoint);

var openAiClient = new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), clientOptions);
IChatClient chatClient = openAiClient
    .GetChatClient(modelId)
    .AsIChatClient();

// ── MCP Tools ────────────────────────────────────────────────────────
var mcpService = new McpToolService();
await mcpService.InitializeAsync(builder.Configuration, LoggerFactory.Create(b => b.AddConsole()));
builder.Services.AddSingleton(mcpService);

var agent = chatClient.AsAIAgent(
    instructions: "You are a helpful assistant.",
    tools: [.. mcpService.Tools]);

builder.Services.AddSingleton(agent);

// ── CORS (needed when not using Angular proxy) ───────────────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// ── Middleware & endpoints ────────────────────────────────────────────
app.UseCors();
app.MapDefaultEndpoints();             // Aspire health checks
app.MapChatEndpoints();

app.Run();
