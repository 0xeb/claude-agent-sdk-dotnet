// Quick start example for Claude Agent SDK for .NET
// Port of claude-agent-sdk-python/examples/quick_start.py

using Claude.AgentSdk;
using ClaudeApi = Claude.AgentSdk.Claude;

Console.WriteLine("Claude Agent SDK for .NET - Quick Start Examples");
Console.WriteLine("=================================================\n");

await BasicExample();
await WithOptionsExample();
await WithToolsExample();

/// <summary>Basic example - simple question.</summary>
async Task BasicExample()
{
    Console.WriteLine("=== Basic Example ===");

    await foreach (var message in ClaudeApi.QueryAsync("What is 2 + 2?"))
    {
        if (message is AssistantMessage am)
        {
            foreach (var block in am.Content)
            {
                if (block is TextBlock tb)
                    Console.WriteLine($"Claude: {tb.Text}");
            }
        }
    }
    Console.WriteLine();
}

/// <summary>Example with custom options.</summary>
async Task WithOptionsExample()
{
    Console.WriteLine("=== With Options Example ===");

    var options = new ClaudeAgentOptions
    {
        SystemPrompt = "You are a helpful assistant that explains things simply.",
        MaxTurns = 1
    };

    await foreach (var message in ClaudeApi.QueryAsync(
        "Explain what C# is in one sentence.",
        options))
    {
        if (message is AssistantMessage am)
        {
            foreach (var block in am.Content)
            {
                if (block is TextBlock tb)
                    Console.WriteLine($"Claude: {tb.Text}");
            }
        }
    }
    Console.WriteLine();
}

/// <summary>Example using tools.</summary>
async Task WithToolsExample()
{
    Console.WriteLine("=== With Tools Example ===");

    var options = new ClaudeAgentOptions
    {
        AllowedTools = ["Read", "Write"],
        SystemPrompt = "You are a helpful file assistant."
    };

    await foreach (var message in ClaudeApi.QueryAsync(
        "Create a file called hello.txt with 'Hello, World!' in it",
        options))
    {
        if (message is AssistantMessage am)
        {
            foreach (var block in am.Content)
            {
                if (block is TextBlock tb)
                    Console.WriteLine($"Claude: {tb.Text}");
            }
        }
        else if (message is ResultMessage rm && rm.TotalCostUsd > 0)
        {
            Console.WriteLine($"\nCost: ${rm.TotalCostUsd:F4}");
        }
    }
    Console.WriteLine();
}
