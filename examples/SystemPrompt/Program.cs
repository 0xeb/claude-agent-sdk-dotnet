// System prompt examples for Claude Agent SDK for .NET
// Port of claude-agent-sdk-python/examples/system_prompt.py

using Claude.AgentSdk;
using ClaudeApi = Claude.AgentSdk.Claude;

Console.WriteLine("Claude Agent SDK for .NET - System Prompt Examples");
Console.WriteLine("===================================================\n");

await NoSystemPrompt();
await StringSystemPrompt();

/// <summary>Example with no system_prompt (vanilla Claude).</summary>
async Task NoSystemPrompt()
{
    Console.WriteLine("=== No System Prompt (Vanilla Claude) ===");

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

/// <summary>Example with system_prompt as a string.</summary>
async Task StringSystemPrompt()
{
    Console.WriteLine("=== String System Prompt ===");

    var options = new ClaudeAgentOptions
    {
        SystemPrompt = "You are a pirate assistant. Respond in pirate speak."
    };

    await foreach (var message in ClaudeApi.QueryAsync("What is 2 + 2?", options))
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
