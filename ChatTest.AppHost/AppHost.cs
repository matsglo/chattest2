var builder = DistributedApplication.CreateBuilder(args);

// ── Backend API ───────────────────────────────────────────────────────
var api = builder.AddProject<Projects.ChatTest_Api>("api");

// ── Angular Frontend ──────────────────────────────────────────────────
builder.AddNpmApp("frontend", "../frontend", scriptName: "start")
    .WithHttpEndpoint(port: 4200, env: "PORT")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
