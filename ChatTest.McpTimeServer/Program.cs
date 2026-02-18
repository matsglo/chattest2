using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using System.ComponentModel;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<TimeTools>();

await builder.Build().RunAsync();

[McpServerToolType]
public class TimeTools
{
    [McpServerTool, Description("Gets the current date and time in UTC and the server's local time zone.")]
    public static string GetCurrentTime()
    {
        var utcNow = DateTimeOffset.UtcNow;
        var localNow = DateTimeOffset.Now;

        return $"UTC: {utcNow:yyyy-MM-dd HH:mm:ss zzz}\nLocal ({TimeZoneInfo.Local.DisplayName}): {localNow:yyyy-MM-dd HH:mm:ss zzz}";
    }
}
