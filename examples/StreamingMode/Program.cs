// Streaming mode examples for Claude Agent SDK for .NET
// Port of claude-agent-sdk-python/examples/streaming_mode.py

using Claude.AgentSdk;
using ClaudeApi = Claude.AgentSdk.Claude;

Console.WriteLine("Claude Agent SDK for .NET - Streaming Mode Examples");
Console.WriteLine("====================================================\n");

var examples = new Dictionary<string, Func<Task>>
{
    ["basic_streaming"] = ExampleBasicStreaming,
    ["multi_turn"] = ExampleMultiTurnConversation,
    ["with_interrupt"] = ExampleWithInterrupt,
    ["with_options"] = ExampleWithOptions,
    ["error_handling"] = ExampleErrorHandling,
};

if (args.Length == 0)
{
    Console.WriteLine("Usage: StreamingMode <example_name>");
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

// Helper function to display messages
void DisplayMessage(Message msg)
{
    switch (msg)
    {
        case UserMessage um:
            var text = um.GetTextContent();
            if (text != null)
                Console.WriteLine($"User: {text}");
            break;

        case AssistantMessage am:
            foreach (var block in am.Content)
            {
                if (block is TextBlock tb)
                    Console.WriteLine($"Claude: {tb.Text}");
            }
            break;

        case ResultMessage:
            Console.WriteLine("Result ended");
            break;
    }
}

/// <summary>Basic streaming with context manager.</summary>
async Task ExampleBasicStreaming()
{
    Console.WriteLine("=== Basic Streaming Example ===\n");

    await using var client = new ClaudeSDKClient();
    await client.ConnectAsync();

    Console.WriteLine("User: What is 2+2?");
    await client.QueryAsync("What is 2+2?");

    await foreach (var msg in client.ReceiveResponseAsync())
    {
        DisplayMessage(msg);
    }

    Console.WriteLine();
}

/// <summary>Multi-turn conversation using receive_response helper.</summary>
async Task ExampleMultiTurnConversation()
{
    Console.WriteLine("=== Multi-Turn Conversation Example ===\n");

    await using var client = new ClaudeSDKClient();
    await client.ConnectAsync();

    // First turn
    Console.WriteLine("User: What's the capital of France?");
    await client.QueryAsync("What's the capital of France?");

    await foreach (var msg in client.ReceiveResponseAsync())
    {
        DisplayMessage(msg);
    }

    // Second turn - follow-up
    Console.WriteLine("\nUser: What's the population of that city?");
    await client.QueryAsync("What's the population of that city?");

    await foreach (var msg in client.ReceiveResponseAsync())
    {
        DisplayMessage(msg);
    }

    Console.WriteLine();
}

/// <summary>Demonstrate interrupt capability.</summary>
async Task ExampleWithInterrupt()
{
    Console.WriteLine("=== Interrupt Example ===");
    Console.WriteLine("IMPORTANT: Interrupts require active message consumption.\n");

    await using var client = new ClaudeSDKClient();
    await client.ConnectAsync();

    // Start a long-running task
    Console.WriteLine("User: Count from 1 to 100 slowly");
    await client.QueryAsync("Count from 1 to 100 slowly, with a brief pause between each number");

    // Start consuming messages in background
    using var cts = new CancellationTokenSource();
    var consumeTask = Task.Run(async () =>
    {
        try
        {
            await foreach (var msg in client.ReceiveResponseAsync(cts.Token))
            {
                DisplayMessage(msg);
            }
        }
        catch (OperationCanceledException) { }
    });

    // Wait 2 seconds then send interrupt
    await Task.Delay(2000);
    Console.WriteLine("\n[After 2 seconds, sending interrupt...]");
    await client.InterruptAsync();

    // Cancel consume task and wait
    await cts.CancelAsync();
    await consumeTask;

    // Send new instruction after interrupt
    Console.WriteLine("\nUser: Never mind, just tell me a quick joke");
    await client.QueryAsync("Never mind, just tell me a quick joke");

    await foreach (var msg in client.ReceiveResponseAsync())
    {
        DisplayMessage(msg);
    }

    Console.WriteLine();
}

/// <summary>Use ClaudeAgentOptions to configure the client.</summary>
async Task ExampleWithOptions()
{
    Console.WriteLine("=== Custom Options Example ===\n");

    var options = ClaudeApi.Options()
        .AllowTools("Read", "Write")
        .SystemPrompt("You are a helpful coding assistant.")
        .Env("ANTHROPIC_MODEL", "claude-sonnet-4-5")
        .Build();

    await using var client = new ClaudeSDKClient(options);
    await client.ConnectAsync();

    Console.WriteLine("User: Create a simple hello.txt file with a greeting message");
    await client.QueryAsync("Create a simple hello.txt file with a greeting message");

    var toolUses = new List<string>();
    await foreach (var msg in client.ReceiveResponseAsync())
    {
        if (msg is AssistantMessage am)
        {
            DisplayMessage(msg);
            foreach (var block in am.Content)
            {
                if (block is ToolUseBlock tub)
                    toolUses.Add(tub.Name);
            }
        }
        else
        {
            DisplayMessage(msg);
        }
    }

    if (toolUses.Count > 0)
        Console.WriteLine($"Tools used: {string.Join(", ", toolUses)}");

    Console.WriteLine();
}

/// <summary>Demonstrate proper error handling.</summary>
async Task ExampleErrorHandling()
{
    Console.WriteLine("=== Error Handling Example ===\n");

    var client = new ClaudeSDKClient();

    try
    {
        await client.ConnectAsync();

        Console.WriteLine("User: What is 2+2?");
        await client.QueryAsync("What is 2+2?");

        // Try to receive response with a short timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            var messages = new List<Message>();
            await foreach (var msg in client.ReceiveResponseAsync(cts.Token))
            {
                messages.Add(msg);
                DisplayMessage(msg);
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\nResponse timeout after 10 seconds - demonstrating graceful handling");
        }
    }
    catch (CliConnectionException e)
    {
        Console.WriteLine($"Connection error: {e.Message}");
    }
    catch (Exception e)
    {
        Console.WriteLine($"Unexpected error: {e.Message}");
    }
    finally
    {
        await client.DisconnectAsync();
    }

    Console.WriteLine();
}
