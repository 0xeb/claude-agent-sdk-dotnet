// Stderr Callback example for Claude Agent SDK for .NET
// Port of claude-agent-sdk-python/examples/stderr_callback_example.py

using Claude.AgentSdk;
using ClaudeApi = Claude.AgentSdk.Claude;

Console.WriteLine("Claude Agent SDK for .NET - Stderr Callback Example");
Console.WriteLine("====================================================\n");

Console.WriteLine("This example demonstrates capturing stderr output from the CLI.");
Console.WriteLine("Stderr can contain debug information, errors, and other diagnostic data.\n");

// Collect stderr messages
var stderrMessages = new List<string>();

void StderrCallback(string message)
{
    stderrMessages.Add(message);
    // Optionally print specific messages
    if (message.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"Error detected: {message}");
    }
}

// Create options with stderr callback and enable debug mode
var options = ClaudeApi.Options()
    .OnStderr(StderrCallback)
    .ExtraArg("debug-to-stderr")  // Enable debug output (flag without value)
    .Build();

// Run a query
Console.WriteLine("Running query with stderr capture...");

await foreach (var message in ClaudeApi.QueryAsync("What is 2+2?", options))
{
    if (message is AssistantMessage am)
    {
        foreach (var block in am.Content)
        {
            if (block is TextBlock tb)
                Console.WriteLine($"Response: {tb.Text}");
        }
    }
    else if (message is ResultMessage rm)
    {
        Console.WriteLine($"\nResult: {rm.Subtype}");
        if (rm.TotalCostUsd.HasValue)
            Console.WriteLine($"Cost: ${rm.TotalCostUsd:F6}");
    }
}

// Show what we captured
Console.WriteLine($"\nCaptured {stderrMessages.Count} stderr lines");
if (stderrMessages.Count > 0)
{
    var firstLine = stderrMessages[0];
    if (firstLine.Length > 100)
        firstLine = firstLine[..100] + "...";
    Console.WriteLine($"First stderr line: {firstLine}");
}
