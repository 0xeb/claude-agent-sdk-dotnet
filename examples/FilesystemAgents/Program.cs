// Filesystem Agents Example
// Port of claude-agent-sdk-python/examples/filesystem_agents.py
//
// Demonstrates loading agents defined in .claude/agents/ files
// using the setting_sources option.

using System.Text.Json;
using Claude.AgentSdk;
using ClaudeApi = Claude.AgentSdk.Claude;

Console.WriteLine("=== Filesystem Agents Example ===");
Console.WriteLine("Testing: SettingSources with .claude/agents/ directory");
Console.WriteLine();

// Use the SDK repo directory which has .claude/agents/
var sdkDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

var options = ClaudeApi.Options()
    .SettingSources(SettingSource.Project)
    .Cwd(sdkDir)
    .Build();

var messageTypes = new List<string>();
var agentsFound = new List<string>();

await using var client = new ClaudeSDKClient(options);
await client.ConnectAsync();
await client.QueryAsync("Say hello in exactly 3 words");

await foreach (var msg in client.ReceiveResponseAsync())
{
    messageTypes.Add(msg.GetType().Name);

    if (msg is SystemMessage sys && sys.Subtype == "init")
    {
        if (sys.Data.TryGetProperty("agents", out var agents) && agents.ValueKind == JsonValueKind.Array)
        {
            foreach (var agent in agents.EnumerateArray())
            {
                if (agent.ValueKind == JsonValueKind.String)
                    agentsFound.Add(agent.GetString()!);
                else if (agent.TryGetProperty("name", out var name))
                    agentsFound.Add(name.GetString()!);
            }
        }
        Console.WriteLine($"Init message received. Agents loaded: [{string.Join(", ", agentsFound)}]");
    }
    else if (msg is AssistantMessage am)
    {
        foreach (var block in am.Content)
        {
            if (block is TextBlock tb)
                Console.WriteLine($"Assistant: {tb.Text}");
        }
    }
    else if (msg is ResultMessage rm)
    {
        Console.WriteLine($"Result: subtype={rm.Subtype}, cost=${rm.TotalCostUsd ?? 0:F4}");
    }
}

Console.WriteLine();
Console.WriteLine("=== Summary ===");
Console.WriteLine($"Message types received: [{string.Join(", ", messageTypes)}]");
Console.WriteLine($"Total messages: {messageTypes.Count}");

var hasInit = messageTypes.Contains("SystemMessage");
var hasAssistant = messageTypes.Contains("AssistantMessage");
var hasResult = messageTypes.Contains("ResultMessage");

Console.WriteLine();
if (hasInit && hasAssistant && hasResult)
    Console.WriteLine("SUCCESS: Received full response (init, assistant, result)");
else
{
    Console.WriteLine("FAILURE: Did not receive full response");
    Console.WriteLine($"  - Init: {hasInit}");
    Console.WriteLine($"  - Assistant: {hasAssistant}");
    Console.WriteLine($"  - Result: {hasResult}");
}

if (agentsFound.Contains("test-agent"))
    Console.WriteLine("SUCCESS: test-agent was loaded from filesystem");
else
    Console.WriteLine("WARNING: test-agent was NOT loaded (may not exist in .claude/agents/)");
