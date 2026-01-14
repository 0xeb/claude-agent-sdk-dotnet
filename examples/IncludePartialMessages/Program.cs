// Include Partial Messages example for Claude Agent SDK for .NET
// Port of claude-agent-sdk-python/examples/include_partial_messages.py

using Claude.AgentSdk;

Console.WriteLine("Claude Agent SDK for .NET - Partial Message Streaming Example");
Console.WriteLine("==============================================================\n");

Console.WriteLine("This feature allows you to receive stream events that contain incremental");
Console.WriteLine("updates as Claude generates responses. This is useful for:");
Console.WriteLine("- Building real-time UIs that show text as it's being generated");
Console.WriteLine("- Monitoring tool use progress");
Console.WriteLine("- Getting early results before the full response is complete");
Console.WriteLine();

// Enable partial message streaming
var options = new ClaudeAgentOptions
{
    IncludePartialMessages = true,
    Model = "claude-sonnet-4-5",
    MaxTurns = 2,
    Env = new Dictionary<string, string>
    {
        ["MAX_THINKING_TOKENS"] = "8000"
    }
};

await using var client = new ClaudeSDKClient(options);
await client.ConnectAsync();

// Send a prompt that will generate a streaming response
var prompt = "Think of three jokes, then tell one";
Console.WriteLine($"Prompt: {prompt}");
Console.WriteLine(new string('=', 50) + "\n");

await client.QueryAsync(prompt);

await foreach (var message in client.ReceiveResponseAsync())
{
    switch (message)
    {
        case StreamEvent se:
            Console.WriteLine($"[Stream] UUID: {se.Uuid}");
            var eventStr = se.Event.ToString();
            if (eventStr.Length > 100)
                eventStr = eventStr[..100] + "...";
            Console.WriteLine($"         Event: {eventStr}");
            break;

        case AssistantMessage am:
            foreach (var block in am.Content)
            {
                if (block is TextBlock tb)
                    Console.WriteLine($"Claude: {tb.Text}");
                else if (block is ToolUseBlock tub)
                    Console.WriteLine($"[Tool Use] {tub.Name}");
            }
            break;

        case UserMessage um:
            var text = um.GetTextContent();
            if (text != null)
                Console.WriteLine($"User: {text}");
            break;

        case SystemMessage sm:
            Console.WriteLine($"[System] {sm.Subtype}");
            break;

        case ResultMessage rm:
            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine("Result ended");
            if (rm.TotalCostUsd.HasValue)
                Console.WriteLine($"Cost: ${rm.TotalCostUsd:F6}");
            break;
    }
}
