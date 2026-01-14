// Basic query example for Claude Agent SDK for .NET

using Claude.AgentSdk;
using ClaudeApi = Claude.AgentSdk.Claude;

Console.WriteLine("Claude Agent SDK for .NET - Basic Query Example");
Console.WriteLine("================================================\n");

// Simple one-shot query
Console.WriteLine("Querying Claude...\n");

try
{
    await foreach (var message in ClaudeApi.QueryAsync("What is 2 + 2? Please respond briefly."))
    {
        switch (message)
        {
            case AssistantMessage am:
                foreach (var block in am.Content)
                {
                    if (block is TextBlock tb)
                        Console.Write(tb.Text);
                }
                break;

            case ResultMessage rm:
                Console.WriteLine($"\n\n--- Query Complete ---");
                Console.WriteLine($"Duration: {rm.DurationMs}ms");
                Console.WriteLine($"API Duration: {rm.DurationApiMs}ms");
                Console.WriteLine($"Turns: {rm.NumTurns}");
                if (rm.TotalCostUsd.HasValue)
                    Console.WriteLine($"Cost: ${rm.TotalCostUsd:F4}");
                Console.WriteLine($"Session ID: {rm.SessionId}");
                break;

            case SystemMessage sm:
                Console.WriteLine($"[System: {sm.Subtype}]");
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
