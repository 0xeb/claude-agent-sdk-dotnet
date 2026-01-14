// Tools Option example for Claude Agent SDK for .NET
// Port of claude-agent-sdk-python/examples/tools_option.py

using System.Text.Json;
using Claude.AgentSdk;
using ClaudeApi = Claude.AgentSdk.Claude;

Console.WriteLine("Claude Agent SDK for .NET - Tools Option Examples");
Console.WriteLine("==================================================\n");

var examples = new Dictionary<string, Func<Task>>
{
    ["tools_array"] = ExampleToolsArray,
    ["tools_empty"] = ExampleToolsEmpty,
    ["tools_preset"] = ExampleToolsPreset,
};

if (args.Length == 0)
{
    Console.WriteLine("Usage: ToolsOption <example_name>");
    Console.WriteLine("\nAvailable examples:");
    Console.WriteLine("  all - Run all examples");
    foreach (var name in examples.Keys)
        Console.WriteLine($"  {name}");
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

/// <summary>Extract tools list from system message.</summary>
List<string> ExtractTools(SystemMessage msg)
{
    if (msg.Subtype == "init" && msg.Data.ValueKind != JsonValueKind.Undefined)
    {
        if (msg.Data.TryGetProperty("tools", out var toolsEl) &&
            toolsEl.ValueKind == JsonValueKind.Array)
        {
            return toolsEl.EnumerateArray()
                .Select(t => t.GetString() ?? "")
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();
        }
    }
    return [];
}

/// <summary>Example with tools as array of specific tool names.</summary>
async Task ExampleToolsArray()
{
    Console.WriteLine("=== Tools Array Example ===");
    Console.WriteLine("Setting tools=['Read', 'Glob', 'Grep']");
    Console.WriteLine();

    var options = new ClaudeAgentOptions
    {
        Tools = ["Read", "Glob", "Grep"],
        MaxTurns = 1
    };

    await foreach (var message in ClaudeApi.QueryAsync(
        "What tools do you have available? Just list them briefly.", options))
    {
        if (message is SystemMessage sm && sm.Subtype == "init")
        {
            var tools = ExtractTools(sm);
            Console.WriteLine($"Tools from system message: [{string.Join(", ", tools)}]");
            Console.WriteLine();
        }
        else if (message is AssistantMessage am)
        {
            foreach (var block in am.Content)
            {
                if (block is TextBlock tb)
                    Console.WriteLine($"Claude: {tb.Text}");
            }
        }
        else if (message is ResultMessage rm && rm.TotalCostUsd.HasValue)
        {
            Console.WriteLine($"\nCost: ${rm.TotalCostUsd:F4}");
        }
    }

    Console.WriteLine();
}

/// <summary>Example with tools as empty array (disables all built-in tools).</summary>
async Task ExampleToolsEmpty()
{
    Console.WriteLine("=== Tools Empty Array Example ===");
    Console.WriteLine("Setting tools=[] (disables all built-in tools)");
    Console.WriteLine();

    var options = new ClaudeAgentOptions
    {
        Tools = [],  // Empty array
        MaxTurns = 1
    };

    await foreach (var message in ClaudeApi.QueryAsync(
        "What tools do you have available? Just list them briefly.", options))
    {
        if (message is SystemMessage sm && sm.Subtype == "init")
        {
            var tools = ExtractTools(sm);
            Console.WriteLine($"Tools from system message: [{string.Join(", ", tools)}]");
            Console.WriteLine();
        }
        else if (message is AssistantMessage am)
        {
            foreach (var block in am.Content)
            {
                if (block is TextBlock tb)
                    Console.WriteLine($"Claude: {tb.Text}");
            }
        }
        else if (message is ResultMessage rm && rm.TotalCostUsd.HasValue)
        {
            Console.WriteLine($"\nCost: ${rm.TotalCostUsd:F4}");
        }
    }

    Console.WriteLine();
}

/// <summary>Example with default tools (no restriction).</summary>
async Task ExampleToolsPreset()
{
    Console.WriteLine("=== Default Tools Example ===");
    Console.WriteLine("Setting tools to null (default - all Claude Code tools)");
    Console.WriteLine();

    // When Tools is null, Claude Code uses its default tool set
    var options = new ClaudeAgentOptions
    {
        // Tools = null (default) means all tools available
        MaxTurns = 1
    };

    await foreach (var message in ClaudeApi.QueryAsync(
        "What tools do you have available? Just list them briefly.", options))
    {
        if (message is SystemMessage sm && sm.Subtype == "init")
        {
            var tools = ExtractTools(sm);
            var displayTools = tools.Count > 5
                ? $"[{string.Join(", ", tools.Take(5))}...]"
                : $"[{string.Join(", ", tools)}]";
            Console.WriteLine($"Tools from system message ({tools.Count} tools): {displayTools}");
            Console.WriteLine();
        }
        else if (message is AssistantMessage am)
        {
            foreach (var block in am.Content)
            {
                if (block is TextBlock tb)
                    Console.WriteLine($"Claude: {tb.Text}");
            }
        }
        else if (message is ResultMessage rm && rm.TotalCostUsd.HasValue)
        {
            Console.WriteLine($"\nCost: ${rm.TotalCostUsd:F4}");
        }
    }

    Console.WriteLine();
}
