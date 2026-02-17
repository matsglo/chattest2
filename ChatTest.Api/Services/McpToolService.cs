using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace ChatTest.Api.Services;

public sealed class McpServerConfig
{
    public string Name { get; set; } = "";
    public string TransportType { get; set; } = "stdio";
    public string Command { get; set; } = "";
    public string[]? Arguments { get; set; }

    // For HTTP/SSE transports
    public string? Endpoint { get; set; }
}

public sealed class McpToolService : IAsyncDisposable
{
    private readonly List<McpClient> _clients = [];
    private readonly List<AITool> _tools = [];

    public IReadOnlyList<AITool> Tools => _tools;

    public async Task InitializeAsync(
        IConfiguration configuration,
        ILoggerFactory? loggerFactory = null)
    {
        var servers = configuration
            .GetSection("McpServers")
            .Get<List<McpServerConfig>>() ?? [];

        foreach (var serverConfig in servers)
        {
            try
            {
                IClientTransport transport = serverConfig.TransportType.ToLowerInvariant() switch
                {
                    "stdio" => new StdioClientTransport(new StdioClientTransportOptions
                    {
                        Name = serverConfig.Name,
                        Command = serverConfig.Command,
                        Arguments = serverConfig.Arguments?.ToList()
                    }),
                    "http" or "sse" => new HttpClientTransport(new HttpClientTransportOptions
                    {
                        Name = serverConfig.Name,
                        Endpoint = new Uri(serverConfig.Endpoint
                            ?? throw new InvalidOperationException(
                                $"MCP server '{serverConfig.Name}' requires an Endpoint for HTTP transport."))
                    }),
                    _ => throw new InvalidOperationException(
                        $"Unknown MCP transport type: {serverConfig.TransportType}")
                };

                var client = await McpClient.CreateAsync(
                    transport,
                    loggerFactory: loggerFactory);
                _clients.Add(client);

                var tools = await client.ListToolsAsync();
                _tools.AddRange(tools);

                var logger = loggerFactory?.CreateLogger<McpToolService>();
                logger?.LogInformation(
                    "MCP server '{Name}' connected with {ToolCount} tools",
                    serverConfig.Name, tools.Count);
            }
            catch (Exception ex)
            {
                var logger = loggerFactory?.CreateLogger<McpToolService>();
                logger?.LogWarning(ex,
                    "Failed to connect to MCP server '{Name}', skipping",
                    serverConfig.Name);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
            await client.DisposeAsync();
    }
}
