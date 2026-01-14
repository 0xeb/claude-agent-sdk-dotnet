# Claude Agent SDK for .NET

A modern .NET library for interacting with the Claude Code CLI, providing both a simple one-shot `QueryAsync()` API and a full bidirectional client with control-protocol support.

[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-blue)]()
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0-blue)]()
[![License](https://img.shields.io/badge/license-MIT-green)]()

**Disclaimer:** This is an independent, unofficial port and is not affiliated with or endorsed by Anthropic, PBC.

## Features

- Simple `Claude.QueryAsync()` API for one-shot requests
- `ClaudeSDKClient` for multi-turn, bidirectional conversations
- Control protocol support (interrupts, modes, dynamic model switching)
- Hook system (PreToolUse, PostToolUse, UserPromptSubmit)
- Tool permission callbacks with allow/deny control
- In-process MCP server support (tools, prompts, resources)
- Cross-platform: Windows, Linux, macOS
- AOT-compatible with source-generated JSON serialization
- Well-tested: 82 tests (63 unit + 19 integration)

## Prerequisites

- .NET 8.0 or .NET 9.0
- Claude Code CLI >= 2.0.0:
  ```bash
  npm install -g @anthropic-ai/claude-code
  ```

### CLI Path Resolution

The SDK discovers the Claude Code CLI in this order:

1. `ClaudeAgentOptions.CliPath` (explicit path)
2. `CLAUDE_CLI_PATH` environment variable
3. `PATH` search for `claude` (or `claude.cmd` on Windows)

## Quick Start

### One-Shot Query

```csharp
using Claude.AgentSdk;

await foreach (var msg in Claude.QueryAsync("What is 2+2?"))
{
    if (msg is AssistantMessage am)
        foreach (var block in am.Content)
            if (block is TextBlock tb)
                Console.Write(tb.Text);
}
```

### Multi-Turn Conversation

```csharp
using Claude.AgentSdk;

await using var client = new ClaudeSDKClient();
await client.ConnectAsync();

await client.QueryAsync("Write a Python hello world");

await foreach (var msg in client.ReceiveResponseAsync())
{
    if (msg is AssistantMessage am)
        foreach (var block in am.Content)
            if (block is TextBlock tb)
                Console.Write(tb.Text);
}
```

### With Options

```csharp
var options = new ClaudeAgentOptions
{
    SystemPrompt = "You are a helpful coding assistant.",
    MaxTurns = 5,
    Model = "claude-sonnet-4-20250514",
    PermissionMode = PermissionMode.AcceptEdits
};

await foreach (var msg in Claude.QueryAsync("Explain async/await", options))
{
    // handle messages
}
```

### Tool Permission Callback

```csharp
var options = new ClaudeAgentOptions
{
    CanUseTool = async (toolName, input, context, ct) =>
    {
        if (toolName == "Bash" && input.GetProperty("command").GetString()?.Contains("rm") == true)
            return new PermissionResultDeny("Destructive commands not allowed");
        return new PermissionResultAllow();
    }
};
```

### Hooks

```csharp
var options = new ClaudeAgentOptions
{
    Hooks = new Dictionary<HookEvent, IReadOnlyList<HookMatcher>>
    {
        [HookEvent.PreToolUse] = [
            new HookMatcher(
                Matcher: "Bash",
                Hooks: [(input, toolUseId, ctx, ct) =>
                {
                    Console.WriteLine($"[Hook] Bash: {input}");
                    return Task.FromResult(new HookOutput { Continue = true });
                }]
            )
        ]
    }
};
```

### MCP Tools (In-Process)

```csharp
var handlers = new McpServerHandlers
{
    ListTools = ct => Task.FromResult<IReadOnlyList<McpToolDefinition>>([
        new McpToolDefinition
        {
            Name = "add",
            Description = "Add two numbers",
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new { a = new { type = "number" }, b = new { type = "number" } },
                required = new[] { "a", "b" }
            })
        }
    ]),
    CallTool = (name, args, ct) =>
    {
        var a = args.GetProperty("a").GetDouble();
        var b = args.GetProperty("b").GetDouble();
        return Task.FromResult(new McpToolResult
        {
            Content = [new McpContent { Type = "text", Text = (a + b).ToString() }]
        });
    }
};

var options = new ClaudeAgentOptions
{
    McpServers = new Dictionary<string, object>
    {
        ["calculator"] = new McpSdkServerConfig { Name = "calculator", Handlers = handlers }
    }
};
```

## Configuration

`ClaudeAgentOptions` mirrors the Python SDK's options:

| Property | Type | Description |
|----------|------|-------------|
| `SystemPrompt` | `string?` | Custom system prompt |
| `MaxTurns` | `int?` | Maximum conversation turns |
| `MaxBudgetUsd` | `decimal?` | Spending limit in USD |
| `Model` | `string?` | Model to use |
| `FallbackModel` | `string?` | Fallback model |
| `PermissionMode` | `PermissionMode?` | Default, AcceptEdits, BypassPermissions |
| `McpServers` | `object?` | MCP server configurations |
| `CanUseTool` | `CanUseToolCallback?` | Tool permission callback |
| `Hooks` | `IReadOnlyDictionary<...>?` | Event hooks |
| `AllowedTools` | `IReadOnlyList<string>` | Whitelist tools |
| `DisallowedTools` | `IReadOnlyList<string>` | Blacklist tools |
| `Cwd` | `string?` | Working directory |
| `CliPath` | `string?` | Explicit CLI path |

## Message Types

- `AssistantMessage` - Claude's response with `Content` blocks
- `UserMessage` - User input
- `SystemMessage` - System notifications
- `ResultMessage` - Query completion with cost/duration info

### Content Blocks

- `TextBlock` - Text content
- `ThinkingBlock` - Extended thinking (with signature)
- `ToolUseBlock` - Tool invocation
- `ToolResultBlock` - Tool output

## Examples

See the `examples/` directory:

| Example | Description |
|---------|-------------|
| `BasicQuery` | Simple one-shot query |
| `StreamingMode` | Interactive bidirectional client |
| `SystemPrompt` | Custom system prompts |
| `McpCalculator` | In-process MCP tools |
| `McpPrompts` | MCP prompt templates |
| `Hooks` | Pre/post tool use hooks |
| `ToolPermissionCallback` | Permission control |
| `Agents` | Agent configurations |
| `MaxBudget` | Spending limits |

## Status & Parity

- **Current version:** 0.1.0
- **Status:** Production ready
- **Parity:** Designed to match the Python Claude Agent SDK API, behavior, and ergonomics
- **Tests:** 82 tests (63 unit + 19 integration)

**Canonical rule:** The Python `claude-agent-sdk` is the canonical reference. This .NET port tracks its behavior and API.

## Installation

### NuGet Package (Coming Soon)

```bash
dotnet add package Claude.AgentSdk
```

### From Source

```bash
git clone https://github.com/anthropics/claude-agent-sdk-dotnet.git
cd claude-agent-sdk-dotnet
dotnet build
```

## Related Projects

| Project | Language | Description |
|---------|----------|-------------|
| [claude-agent-sdk-python](https://github.com/anthropics/claude-agent-sdk-python) | Python | Official Python SDK (canonical reference) |
| [claude-agent-sdk-cpp](https://github.com/0xeb/claude-agent-sdk-cpp) | C++ | C++ port with full feature parity |

## License

Licensed under the MIT License. See `LICENSE` for details.

This is a .NET port of [claude-agent-sdk-python](https://github.com/anthropics/claude-agent-sdk-python) by Anthropic, PBC.

## Support

- Issues: Use the GitHub issue tracker
- Examples: See `examples/`
- Tests: See `tests/`
