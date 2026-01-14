// MCP Calculator Example - In-process MCP server with Claude Agent SDK for .NET
// This example demonstrates how to create an in-process MCP server with tools
// that Claude can use during conversation.

using System.Text.Json;
using Claude.AgentSdk;
using Claude.AgentSdk.Mcp;

Console.WriteLine("Claude Agent SDK for .NET - MCP Calculator Example");
Console.WriteLine("===================================================\n");

// Define the calculator tool schema
var addSchema = JsonSerializer.SerializeToElement(new
{
    type = "object",
    properties = new
    {
        a = new { type = "number", description = "First number" },
        b = new { type = "number", description = "Second number" }
    },
    required = new[] { "a", "b" }
});

var multiplySchema = JsonSerializer.SerializeToElement(new
{
    type = "object",
    properties = new
    {
        a = new { type = "number", description = "First number" },
        b = new { type = "number", description = "Second number" }
    },
    required = new[] { "a", "b" }
});

// Create MCP server handlers
var handlers = new McpServerHandlers
{
    // List available tools
    ListTools = ct => Task.FromResult<IReadOnlyList<McpToolDefinition>>(
    [
        new McpToolDefinition
        {
            Name = "add",
            Description = "Add two numbers together",
            InputSchema = addSchema
        },
        new McpToolDefinition
        {
            Name = "multiply",
            Description = "Multiply two numbers",
            InputSchema = multiplySchema
        }
    ]),

    // Handle tool calls
    CallTool = async (name, args, ct) =>
    {
        await Task.CompletedTask;

        var a = args.GetProperty("a").GetDouble();
        var b = args.GetProperty("b").GetDouble();

        var result = name switch
        {
            "add" => a + b,
            "multiply" => a * b,
            _ => throw new NotSupportedException($"Unknown tool: {name}")
        };

        Console.WriteLine($"[MCP] Tool '{name}' called with ({a}, {b}) = {result}");

        return new McpToolResult
        {
            Content =
            [
                new McpContent
                {
                    Type = "text",
                    Text = result.ToString()
                }
            ]
        };
    }
};

// Create SDK MCP server configuration
var mcpConfig = new McpSdkServerConfig
{
    Name = "calculator",
    Handlers = handlers
};

// Create options with the MCP server
var options = new ClaudeAgentOptions
{
    McpServers = new Dictionary<string, object>
    {
        ["calculator"] = mcpConfig
    },
    SystemPrompt = "You have access to a calculator MCP server. Use the 'add' and 'multiply' tools to perform calculations when asked.",
    // Allow all tool calls without prompting for permission
    CanUseTool = async (toolName, input, context, ct) =>
    {
        await Task.CompletedTask;
        return new PermissionResultAllow();
    }
};

Console.WriteLine("Starting conversation with in-process MCP calculator...\n");

try
{
    // Create client and connect
    await using var client = new ClaudeSDKClient(options);
    await client.ConnectAsync();

    // Send a query that will use the calculator tools
    await client.QueryAsync("What is 25 + 17? Then multiply the result by 3.");

    // Process response
    await foreach (var message in client.ReceiveResponseAsync())
    {
        switch (message)
        {
            case AssistantMessage am:
                foreach (var block in am.Content)
                {
                    switch (block)
                    {
                        case TextBlock tb:
                            Console.Write(tb.Text);
                            break;
                        case ToolUseBlock tu:
                            Console.WriteLine($"\n[Using tool: {tu.Name}]");
                            break;
                    }
                }
                break;

            case ResultMessage rm:
                Console.WriteLine($"\n\n--- Query Complete ---");
                Console.WriteLine($"Duration: {rm.DurationMs}ms");
                Console.WriteLine($"Turns: {rm.NumTurns}");
                if (rm.TotalCostUsd.HasValue)
                    Console.WriteLine($"Cost: ${rm.TotalCostUsd:F4}");
                break;

            case SystemMessage sm:
                if (sm.Subtype == "mcp_tool_result")
                    Console.WriteLine("[MCP tool result received]");
                break;
        }
    }
}
catch (CliNotFoundException ex)
{
    Console.WriteLine($"Error: Claude Code CLI not found.");
    Console.WriteLine(ex.Message);
    Console.WriteLine("\nMake sure Claude Code is installed:");
    Console.WriteLine("  npm install -g @anthropic-ai/claude-code");
}
catch (ClaudeSDKException ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

Console.WriteLine("\nExample complete.");
