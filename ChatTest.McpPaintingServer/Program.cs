using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using System.ComponentModel;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<PaintingTools>();

await builder.Build().RunAsync();

[McpServerToolType]
public class PaintingTools
{
    [McpServerTool, Description("Returns a painting image. Use this when the user asks for a painting or wants to see the painting.")]
    public static string GetPainting()
    {
        return "Here is the painting. Display it to the user by including this exact markdown in your response:\n\n![Painting](/api/images/painting.png)";
    }
}
