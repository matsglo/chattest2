using ChatTest.Api.Endpoints;
using ChatTest.Api.Services;
using Microsoft.Extensions.AI;
using OpenAI;

var builder = WebApplication.CreateBuilder(args);

// ── Aspire ServiceDefaults (OTel, health checks, resilience) ─────────
builder.AddServiceDefaults();

// ── Application services ─────────────────────────────────────────────
builder.Services.AddSingleton<ChatSessionService>();

// ── AI Chat Client ───────────────────────────────────────────────────
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

builder.Services.AddSingleton(chatClient);

// ── MCP Tools ────────────────────────────────────────────────────────
var mcpService = new McpToolService();
await mcpService.InitializeAsync(builder.Configuration, LoggerFactory.Create(b => b.AddConsole()));
builder.Services.AddSingleton(mcpService);

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

// ── Serve images from repo root ─────────────────────────────────────
app.MapGet("/api/images/{filename}", (string filename) =>
{
    // Only allow specific safe filenames
    if (filename != Path.GetFileName(filename))
        return Results.BadRequest();

    var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    var filePath = Path.Combine(repoRoot, filename);

    if (!File.Exists(filePath))
        return Results.NotFound();

    var contentType = Path.GetExtension(filePath).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        ".webp" => "image/webp",
        _ => "application/octet-stream"
    };

    return Results.File(filePath, contentType);
});

app.Run();
