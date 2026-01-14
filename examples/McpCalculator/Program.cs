// MCP Calculator Example - In-process MCP server with Claude Agent SDK for .NET
// This example demonstrates how to create an in-process MCP server with tools
// that Claude can use during conversation.

using Claude.AgentSdk;
using Claude.AgentSdk.Mcp;
using ClaudeApi = Claude.AgentSdk.Claude;

Console.WriteLine("Claude Agent SDK for .NET - MCP Calculator Example");      
Console.WriteLine("===================================================\n");   

// Create options with the MCP server
var options = ClaudeApi.Options()
    .McpServers(m => m.AddSdk("calculator", s => s
        .Tool("add", (double a, double b) =>
        {
            var result = a + b;
            Console.WriteLine($"[MCP] Tool 'add' called with ({a}, {b}) = {result}");
            return result;
        }, "Add two numbers together")
        .Tool("multiply", (double a, double b) =>
        {
            var result = a * b;
            Console.WriteLine($"[MCP] Tool 'multiply' called with ({a}, {b}) = {result}");
            return result;
        }, "Multiply two numbers")))
    .SystemPrompt("You have access to a calculator MCP server. Use the 'add' and 'multiply' tools to perform calculations when asked.")
    .AllowAllTools()
    .Build();

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
