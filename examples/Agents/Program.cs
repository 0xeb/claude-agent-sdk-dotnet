// Custom Agents example for Claude Agent SDK for .NET
// Port of claude-agent-sdk-python/examples/agents.py

using Claude.AgentSdk;
using ClaudeApi = Claude.AgentSdk.Claude;

Console.WriteLine("Claude Agent SDK for .NET - Custom Agents Examples");
Console.WriteLine("===================================================\n");

var examples = new Dictionary<string, Func<Task>>
{
    ["code_reviewer"] = ExampleCodeReviewer,
    ["doc_writer"] = ExampleDocWriter,
    ["multiple_agents"] = ExampleMultipleAgents,
};

if (args.Length == 0)
{
    Console.WriteLine("Usage: Agents <example_name>");
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

/// <summary>Example using a custom code reviewer agent.</summary>
async Task ExampleCodeReviewer()
{
    Console.WriteLine("=== Code Reviewer Agent Example ===\n");

    var options = new ClaudeAgentOptions
    {
        Agents = new Dictionary<string, AgentDefinition>
        {
            ["code-reviewer"] = new AgentDefinition(
                Description: "Reviews code for best practices and potential issues",
                Prompt: "You are a code reviewer. Analyze code for bugs, performance issues, " +
                        "security vulnerabilities, and adherence to best practices. " +
                        "Provide constructive feedback.",
                Tools: ["Read", "Grep"],
                Model: "sonnet"
            )
        }
    };

    await foreach (var message in ClaudeApi.QueryAsync(
        "Use the code-reviewer agent to review the code in src/Claude.AgentSdk/Types.cs",
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

/// <summary>Example using a documentation writer agent.</summary>
async Task ExampleDocWriter()
{
    Console.WriteLine("=== Documentation Writer Agent Example ===\n");

    var options = new ClaudeAgentOptions
    {
        Agents = new Dictionary<string, AgentDefinition>
        {
            ["doc-writer"] = new AgentDefinition(
                Description: "Writes comprehensive documentation",
                Prompt: "You are a technical documentation expert. Write clear, comprehensive " +
                        "documentation with examples. Focus on clarity and completeness.",
                Tools: ["Read", "Write", "Edit"],
                Model: "sonnet"
            )
        }
    };

    await foreach (var message in ClaudeApi.QueryAsync(
        "Use the doc-writer agent to explain what AgentDefinition is used for",
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

/// <summary>Example with multiple custom agents.</summary>
async Task ExampleMultipleAgents()
{
    Console.WriteLine("=== Multiple Agents Example ===\n");

    var options = new ClaudeAgentOptions
    {
        Agents = new Dictionary<string, AgentDefinition>
        {
            ["analyzer"] = new AgentDefinition(
                Description: "Analyzes code structure and patterns",
                Prompt: "You are a code analyzer. Examine code structure, patterns, and architecture.",
                Tools: ["Read", "Grep", "Glob"]
            ),
            ["tester"] = new AgentDefinition(
                Description: "Creates and runs tests",
                Prompt: "You are a testing expert. Write comprehensive tests and ensure code quality.",
                Tools: ["Read", "Write", "Bash"],
                Model: "sonnet"
            )
        },
        SettingSources = [SettingSource.User, SettingSource.Project]
    };

    await foreach (var message in ClaudeApi.QueryAsync(
        "Use the analyzer agent to find all C# files in the examples/ directory",
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
