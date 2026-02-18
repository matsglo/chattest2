# Claude Code Project Instructions

## .NET Build Workflow with Aspire

When Aspire is running and you need to rebuild a .NET project:

1. **Stop** the resource first using the Aspire MCP: `execute_resource_command(resourceName, "resource-stop")`
2. **Build** the project: `dotnet build <project>.csproj`
3. **Start** the resource again using the Aspire MCP: `execute_resource_command(resourceName, "resource-start")`

The running process locks the binary, so building will fail if you don't stop it first.
