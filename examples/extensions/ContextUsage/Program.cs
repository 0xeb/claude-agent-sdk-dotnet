// Demonstrates the ContextUsageResponse / ContextUsageCategory shapes used by
// `ClaudeSDKClient.GetContextUsageAsync()` (v0.2.82, Python commit ac900bd).
//
// .NET-only example (non-mirrored). Live CLI usage:
//
//     await using var client = new ClaudeSDKClient(options);
//     await client.ConnectAsync();
//     var usage = await client.GetContextUsageAsync();
//
// This sample fabricates a response to show the shape so users can render
// their own dashboards without needing a running CLI session.

using Claude.AgentSdk;

var usage = new ContextUsageResponse
{
    Categories = new[]
    {
        new ContextUsageCategory { Name = "System prompt", Tokens = 1_200, Color = "blue" },
        new ContextUsageCategory { Name = "Tools",         Tokens = 3_800, Color = "green" },
        new ContextUsageCategory { Name = "Conversation",  Tokens = 18_500, Color = "yellow" },
        new ContextUsageCategory { Name = "Deferred MCP",  Tokens = 4_000, Color = "gray", IsDeferred = true },
    },
    TotalTokens = 27_500,
    MaxTokens = 180_000,
    RawMaxTokens = 200_000,
    Percentage = 27_500.0 / 180_000.0 * 100.0,
    Model = "claude-sonnet-4-20250514",
    IsAutoCompactEnabled = true,
};

Console.WriteLine($"Model           : {usage.Model}");
Console.WriteLine($"Auto-compact    : {usage.IsAutoCompactEnabled}");
Console.WriteLine($"Total tokens    : {usage.TotalTokens:N0} / {usage.MaxTokens:N0}");
Console.WriteLine($"Raw max tokens  : {usage.RawMaxTokens:N0}");
Console.WriteLine($"% of soft limit : {usage.Percentage:F2}%");
Console.WriteLine("Categories:");
foreach (var cat in usage.Categories)
{
    var deferred = cat.IsDeferred == true ? " (deferred)" : "";
    Console.WriteLine($"  - [{cat.Color,-6}] {cat.Name,-16} {cat.Tokens,8:N0}{deferred}");
}
