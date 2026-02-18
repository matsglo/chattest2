# ChatTest

A streaming AI chat application built with an **Angular** frontend and a **.NET** backend, orchestrated by **.NET Aspire**.

## Project Structure

```
ChatTest.sln
├── ChatTest.AppHost/          # .NET Aspire orchestrator
├── ChatTest.Api/              # .NET 10 Web API (backend)
├── ChatTest.ServiceDefaults/  # Shared Aspire defaults (OpenTelemetry, health checks, resilience)
└── frontend/                  # Angular 21 SPA (Tailwind CSS, Vercel AI SDK)
```

### ChatTest.AppHost

The Aspire AppHost is the single entry point that launches the entire stack. It registers the .NET API as a project resource and the Angular frontend as an npm app, wiring them together with service discovery:

```csharp
var api = builder.AddProject<Projects.ChatTest_Api>("api");

builder.AddNpmApp("frontend", "../frontend", scriptName: "start")
    .WithHttpEndpoint(port: 4200, env: "PORT")
    .WithReference(api)
    .WaitFor(api);
```

Running the AppHost starts both the backend and `ng serve`, gives you the Aspire dashboard with distributed traces, health checks, and structured logs out of the box.

### ChatTest.Api

A .NET 10 minimal API that:

- Manages in-memory **chat sessions** (CRUD + message history).
- Connects to any **OpenAI-compatible LLM** (OpenAI, LM Studio, etc.) via `Microsoft.Extensions.AI` and the `OpenAI` .NET SDK.
- Loads **MCP (Model Context Protocol) tool servers** at startup (stdio or HTTP transport) and exposes their tools to the LLM.
- Streams responses to the frontend using the **Vercel AI UI Message Stream v1** protocol over Server-Sent Events.
- Implements a **two-pass tool approval flow**: the LLM requests tool calls, the user approves or declines them in the UI, and approved tools are executed before the final response is streamed.

### ChatTest.ServiceDefaults

The standard Aspire shared project providing OpenTelemetry tracing/metrics, health check endpoints, HTTP resilience policies, and service discovery for all backend services.

### Frontend

An Angular 21 app styled with Tailwind CSS that provides:

- A session sidebar for creating, switching, and deleting chat sessions.
- A streaming chat UI with markdown rendering (via `marked`).
- Collapsible reasoning/thinking sections.
- Tool call approval UI (Allow / Always Allow / Decline).

## Vercel AI SDK Integration

The frontend uses the [Vercel AI SDK](https://sdk.vercel.ai/) (`ai` and `@ai-sdk/angular` packages) to manage all chat state and streaming communication.

### Frontend (Angular)

**Key imports from the SDK:**

- **`Chat`** (`@ai-sdk/angular`) — Manages chat state (messages, status, errors) as Angular signals. Provides `sendMessage()` to send user input and `addToolApprovalResponse()` for the tool approval workflow.
- **`DefaultChatTransport`** (`ai`) — HTTP transport that connects to the .NET backend at `/api/chat/sessions/{id}/messages` and automatically handles SSE stream parsing.
- **`UIMessage`** (`ai`) — The standard message type used across all components. Messages contain typed `parts` (`text`, `reasoning`, `dynamic-tool`) that the UI renders differently.
- **`lastAssistantMessageIsCompleteWithApprovalResponses`** (`ai`) — A built-in sending strategy that automatically sends a follow-up request after the user responds to all tool approval prompts.

**Chat initialization:**

```typescript
private createChat(api: string, messages?: UIMessage[]): Chat {
  return new Chat({
    transport: new DefaultChatTransport({ api }),
    messages,
    sendAutomaticallyWhen: lastAssistantMessageIsCompleteWithApprovalResponses,
    onError: (err) => console.error('Chat error:', err),
    onFinish: () => { /* reload sessions, auto-approve always-allowed tools */ }
  });
}
```

### Backend (.NET) — Wire Protocol

The .NET backend doesn't use the JavaScript SDK directly. Instead, `AiStreamWriter` implements the **Vercel AI UI Message Stream v1** protocol so the Angular frontend's `DefaultChatTransport` can consume the stream natively:

- Sets `X-Vercel-AI-UI-Message-Stream: v1` and `Content-Type: text/event-stream` headers.
- Emits SSE events: `text-start`, `text-delta`, `text-end`, `reasoning-start`, `reasoning-delta`, `reasoning-end`, `tool-input-start`, `tool-input-available`, `tool-approval-request`, `tool-output-available`, `tool-output-denied`.

### Tool Approval Flow

1. The LLM requests tool calls during streaming.
2. The backend emits `tool-approval-request` SSE events.
3. The frontend renders **Allow / Always Allow / Decline** buttons on each tool call.
4. The user's responses are sent back via `chat.addToolApprovalResponse()`.
5. The SDK automatically fires a follow-up request (Pass 2) thanks to `lastAssistantMessageIsCompleteWithApprovalResponses`.
6. The backend executes approved tools, sends results to the LLM, and streams the final response.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) (LTS)
- An OpenAI-compatible API endpoint (OpenAI, LM Studio, etc.)

## Getting Started

1. **Configure the AI endpoint** in `ChatTest.Api/appsettings.json`:

   ```json
   {
     "AI": {
       "ModelId": "gpt-4.1",
       "Endpoint": "http://localhost:1234/v1"
     }
   }
   ```

2. **Install frontend dependencies:**

   ```bash
   cd frontend && npm install
   ```

3. **Run via Aspire:**

   ```bash
   dotnet run --project ChatTest.AppHost
   ```

   This starts both the API and the Angular dev server. Open the Aspire dashboard URL printed in the console to see endpoints, traces, and logs.
