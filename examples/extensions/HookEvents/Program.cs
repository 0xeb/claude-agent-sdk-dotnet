// Demonstrates registering callbacks on every hook event surface exposed by
// the SDK. Each callback writes to stdout when it would fire — no `claude`
// CLI subprocess is needed.
//
// .NET-only example. Mirrors the *shape* of Python's `examples/hooks.py` but
// covers every HookEvent value instead of only PreToolUse/PostToolUse.

using System.Text.Json;
using Claude.AgentSdk;
using ClaudeApi = Claude.AgentSdk.Claude;

static Task<HookOutput> Log(string label, JsonElement input)
{
    var tool = input.TryGetProperty("tool_name", out var t) ? t.GetString() : null;
    Console.WriteLine(tool is null
        ? $"[{label}] payload={input.GetRawText()}"
        : $"[{label}] tool={tool}");
    return Task.FromResult(new HookOutput());
}

var options = ClaudeApi.Options()
    .Hooks(h => h
        .PreToolUse("*",     (input, _, _, _) => Log("PreToolUse", input))
        .PostToolUse("*",    (input, _, _, _) => Log("PostToolUse", input))
        .PostToolUseFailure("*", (input, _, _, _) => Log("PostToolUseFailure", input))
        .UserPromptSubmit(   (input, _, _, _) => Log("UserPromptSubmit", input))
        .OnStop(             (input, _, _, _) => Log("Stop", input))
        .OnSubagentStop(     (input, _, _, _) => Log("SubagentStop", input))
        .OnSubagentStart(    (input, _, _, _) => Log("SubagentStart", input))
        .PreCompact(         (input, _, _, _) => Log("PreCompact", input))
        .OnNotification(     (input, _, _, _) => Log("Notification", input))
        .OnPermissionRequest("*", (input, _, _, _) => Log("PermissionRequest", input)))
    .Build();

Console.WriteLine("Registered hook events:");
if (options.Hooks is null)
{
    Console.WriteLine("  (none)");
    return;
}
foreach (var (eventName, matchers) in options.Hooks)
{
    foreach (var m in matchers)
    {
        var pattern = string.IsNullOrEmpty(m.Matcher) ? "(any)" : m.Matcher;
        var count = m.Hooks?.Count ?? 0;
        Console.WriteLine($"  {eventName,-22} matcher={pattern,-8} callbacks={count}");
    }
}

Console.WriteLine();
Console.WriteLine("Wire these into a ClaudeSDKClient:");
Console.WriteLine("    await using var client = new ClaudeSDKClient(options);");
Console.WriteLine("    await client.ConnectAsync();");
Console.WriteLine("    await foreach (var m in client.ReceiveMessagesAsync()) { /* ... */ }");
