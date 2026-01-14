// Max Budget example for Claude Agent SDK for .NET
// Port of claude-agent-sdk-python/examples/max_budget_usd.py

using Claude.AgentSdk;
using ClaudeApi = Claude.AgentSdk.Claude;

Console.WriteLine("Claude Agent SDK for .NET - Max Budget Examples");
Console.WriteLine("================================================\n");

Console.WriteLine("This example demonstrates using max_budget_usd to control API costs.\n");

await WithoutBudget();
await WithReasonableBudget();
await WithTightBudget();

Console.WriteLine("Note: Budget checking happens after each API call completes,");
Console.WriteLine("so the final cost may slightly exceed the specified budget.\n");

/// <summary>Example without budget limit.</summary>
async Task WithoutBudget()
{
    Console.WriteLine("=== Without Budget Limit ===");

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
        else if (message is ResultMessage rm)
        {
            if (rm.TotalCostUsd.HasValue)
                Console.WriteLine($"Total cost: ${rm.TotalCostUsd:F4}");
            Console.WriteLine($"Status: {rm.Subtype}");
        }
    }

    Console.WriteLine();
}

/// <summary>Example with budget that won't be exceeded.</summary>
async Task WithReasonableBudget()
{
    Console.WriteLine("=== With Reasonable Budget ($0.10) ===");

    var options = ClaudeApi.Options()
        .MaxBudget(0.10m)  // 10 cents - plenty for a simple query
        .Build();

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
        else if (message is ResultMessage rm)
        {
            if (rm.TotalCostUsd.HasValue)
                Console.WriteLine($"Total cost: ${rm.TotalCostUsd:F4}");
            Console.WriteLine($"Status: {rm.Subtype}");
        }
    }

    Console.WriteLine();
}

/// <summary>Example with very tight budget that will likely be exceeded.</summary>
async Task WithTightBudget()
{
    Console.WriteLine("=== With Tight Budget ($0.0001) ===");

    var options = ClaudeApi.Options()
        .MaxBudget(0.0001m)  // Very small budget - will be exceeded quickly
        .Build();

    await foreach (var message in ClaudeApi.QueryAsync(
        "Read the README.md file and summarize it", options))
    {
        if (message is AssistantMessage am)
        {
            foreach (var block in am.Content)
            {
                if (block is TextBlock tb)
                    Console.WriteLine($"Claude: {tb.Text}");
            }
        }
        else if (message is ResultMessage rm)
        {
            if (rm.TotalCostUsd.HasValue)
                Console.WriteLine($"Total cost: ${rm.TotalCostUsd:F4}");
            Console.WriteLine($"Status: {rm.Subtype}");

            // Check if budget was exceeded
            if (rm.Subtype == "error_max_budget_usd")
            {
                Console.WriteLine("Warning: Budget limit exceeded!");
                Console.WriteLine("Note: The cost may exceed the budget by up to one API call's worth");
            }
        }
    }

    Console.WriteLine();
}
