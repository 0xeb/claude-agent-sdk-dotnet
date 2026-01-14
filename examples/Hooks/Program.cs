// Hooks examples for Claude Agent SDK for .NET
// Port of claude-agent-sdk-python/examples/hooks.py

using System.Text.Json;
using Claude.AgentSdk;
using ClaudeApi = Claude.AgentSdk.Claude;

Console.WriteLine("Claude Agent SDK for .NET - Hooks Examples");
Console.WriteLine("==========================================\n");

var examples = new Dictionary<string, Func<Task>>
{
    ["PreToolUse"] = ExamplePreToolUse,
    ["PostToolUse"] = ExamplePostToolUse,
};

if (args.Length == 0)
{
    Console.WriteLine("Usage: Hooks <example_name>");
    Console.WriteLine("\nAvailable examples:");
    Console.WriteLine("  all - Run all examples");
    foreach (var name in examples.Keys)
        Console.WriteLine($"  {name}");
    Console.WriteLine("\nExample descriptions:");
    Console.WriteLine("  PreToolUse  - Block commands using PreToolUse hook");
    Console.WriteLine("  PostToolUse - Review tool output with reason and systemMessage");
    return;
}

var exampleName = args[0];

if (exampleName == "all")
{
    foreach (var example in examples.Values)
    {
        await example();
        Console.WriteLine(new string('-', 50) + "\n");
    }
}
else if (examples.TryGetValue(exampleName, out var exampleFunc))
{
    await exampleFunc();
}
else
{
    Console.WriteLine($"Error: Unknown example '{exampleName}'");
}

// Helper function
void DisplayMessage(Message msg)
{
    if (msg is AssistantMessage am)
    {
        foreach (var block in am.Content)
        {
            if (block is TextBlock tb)
                Console.WriteLine($"Claude: {tb.Text}");
        }
    }
    else if (msg is ResultMessage)
    {
        Console.WriteLine("Result ended");
    }
}

/// <summary>Hook callback to check bash commands.</summary>
Task<HookOutput> CheckBashCommand(JsonElement input, string? toolUseId, HookContext context, CancellationToken ct)
{
    // Get tool_name and tool_input from the input
    if (!input.TryGetProperty("tool_name", out var toolNameEl) ||
        toolNameEl.GetString() != "Bash")
    {
        return Task.FromResult(new HookOutput());
    }

    if (!input.TryGetProperty("tool_input", out var toolInput))
    {
        return Task.FromResult(new HookOutput());
    }

    var command = toolInput.TryGetProperty("command", out var cmdEl)
        ? cmdEl.GetString() ?? ""
        : "";

    var blockPatterns = new[] { "foo.sh" };

    foreach (var pattern in blockPatterns)
    {
        if (command.Contains(pattern))
        {
            Console.WriteLine($"[HOOK] Blocked command: {command}");

            // Return deny decision via hookSpecificOutput
            var hookSpecific = JsonSerializer.SerializeToElement(new
            {
                hookEventName = "PreToolUse",
                permissionDecision = "deny",
                permissionDecisionReason = $"Command contains invalid pattern: {pattern}"
            });

            return Task.FromResult(new HookOutput { HookSpecificOutput = hookSpecific });
        }
    }

    return Task.FromResult(new HookOutput());
}

/// <summary>Hook callback to review tool output.</summary>
Task<HookOutput> ReviewToolOutput(JsonElement input, string? toolUseId, HookContext context, CancellationToken ct)
{
    var toolResponse = input.TryGetProperty("tool_response", out var respEl)
        ? respEl.ToString()
        : "";

    if (toolResponse.Contains("error", StringComparison.OrdinalIgnoreCase))
    {
        var hookSpecific = JsonSerializer.SerializeToElement(new
        {
            hookEventName = "PostToolUse",
            additionalContext = "The command encountered an error. You may want to try a different approach."
        });

        return Task.FromResult(new HookOutput
        {
            SystemMessage = "The command produced an error",
            Reason = "Tool execution failed - consider checking the command syntax",
            HookSpecificOutput = hookSpecific
        });
    }

    return Task.FromResult(new HookOutput());
}

/// <summary>Basic example demonstrating PreToolUse hook protection.</summary>
async Task ExamplePreToolUse()
{
    Console.WriteLine("=== PreToolUse Example ===");
    Console.WriteLine("This example demonstrates how PreToolUse can block some bash commands.\n");

    var options = ClaudeApi.Options()
        .AllowTools("Bash")
        .Hooks(h => h.PreToolUse("Bash", CheckBashCommand))
        .Build();

    await using var client = new ClaudeSDKClient(options);
    await client.ConnectAsync();

    // Test 1: Command with forbidden pattern (will be blocked)
    Console.WriteLine("Test 1: Trying a command that our PreToolUse hook should block...");
    Console.WriteLine("User: Run the bash command: ./foo.sh --help");
    await client.QueryAsync("Run the bash command: ./foo.sh --help");

    await foreach (var msg in client.ReceiveResponseAsync())
    {
        DisplayMessage(msg);
    }

    Console.WriteLine("\n" + new string('=', 50) + "\n");

    // Test 2: Safe command that should work
    Console.WriteLine("Test 2: Trying a command that our PreToolUse hook should allow...");
    Console.WriteLine("User: Run the bash command: echo 'Hello from hooks example!'");
    await client.QueryAsync("Run the bash command: echo 'Hello from hooks example!'");

    await foreach (var msg in client.ReceiveResponseAsync())
    {
        DisplayMessage(msg);
    }

    Console.WriteLine();
}

/// <summary>Demonstrate PostToolUse hook with reason and systemMessage.</summary>
async Task ExamplePostToolUse()
{
    Console.WriteLine("=== PostToolUse Example ===");
    Console.WriteLine("This example shows how PostToolUse can provide feedback.\n");

    var options = ClaudeApi.Options()
        .AllowTools("Bash")
        .Hooks(h => h.PostToolUse("Bash", ReviewToolOutput))
        .Build();

    await using var client = new ClaudeSDKClient(options);
    await client.ConnectAsync();

    Console.WriteLine("User: Run a command that will produce an error: ls /nonexistent_directory");
    await client.QueryAsync("Run this command: ls /nonexistent_directory");

    await foreach (var msg in client.ReceiveResponseAsync())
    {
        DisplayMessage(msg);
    }

    Console.WriteLine();
}
