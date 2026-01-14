// Setting Sources example for Claude Agent SDK for .NET
// Port of claude-agent-sdk-python/examples/setting_sources.py

using System.Text.Json;
using Claude.AgentSdk;
using ClaudeApi = Claude.AgentSdk.Claude;

Console.WriteLine("Claude Agent SDK for .NET - Setting Sources Examples");
Console.WriteLine("=====================================================\n");

Console.WriteLine("Setting sources determine where Claude Code loads configurations from:");
Console.WriteLine("- \"user\": Global user settings (~/.claude/)");
Console.WriteLine("- \"project\": Project-level settings (.claude/ in project)");
Console.WriteLine("- \"local\": Local gitignored settings (.claude-local/)");
Console.WriteLine();
Console.WriteLine("IMPORTANT: When setting_sources is not provided (null), NO settings");
Console.WriteLine("are loaded by default. This creates an isolated environment.");
Console.WriteLine(new string('=', 60) + "\n");

var examples = new Dictionary<string, Func<Task>>
{
    ["default"] = ExampleDefault,
    ["user_only"] = ExampleUserOnly,
    ["project_and_user"] = ExampleProjectAndUser,
};

if (args.Length == 0)
{
    Console.WriteLine("Usage: SettingSources <example_name>");
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

/// <summary>Extract slash command names from system message.</summary>
List<string> ExtractSlashCommands(SystemMessage msg)
{
    if (msg.Subtype == "init" && msg.Data.ValueKind != JsonValueKind.Undefined)
    {
        if (msg.Data.TryGetProperty("slash_commands", out var commandsEl) &&
            commandsEl.ValueKind == JsonValueKind.Array)
        {
            return commandsEl.EnumerateArray()
                .Select(c => c.GetString() ?? "")
                .Where(c => !string.IsNullOrEmpty(c))
                .ToList();
        }
    }
    return [];
}

/// <summary>Default behavior - no settings loaded.</summary>
async Task ExampleDefault()
{
    Console.WriteLine("=== Default Behavior Example ===");
    Console.WriteLine("Setting sources: None (default)");
    Console.WriteLine("Expected: No custom slash commands will be available\n");

    var sdkDir = Directory.GetParent(Directory.GetCurrentDirectory())?.Parent?.FullName
                 ?? Directory.GetCurrentDirectory();

    var options = ClaudeApi.Options()
        .Cwd(sdkDir)
        // No SettingSources - isolated environment
        .Build();

    await using var client = new ClaudeSDKClient(options);
    await client.ConnectAsync();

    await client.QueryAsync("What is 2 + 2?");

    await foreach (var msg in client.ReceiveResponseAsync())
    {
        if (msg is SystemMessage sm && sm.Subtype == "init")
        {
            var commands = ExtractSlashCommands(sm);
            Console.WriteLine($"Available slash commands: [{string.Join(", ", commands)}]");
            if (commands.Contains("commit"))
                Console.WriteLine("X /commit is available (unexpected)");
            else
                Console.WriteLine("OK /commit is NOT available (expected - no settings loaded)");
            break;
        }
    }

    Console.WriteLine();
}

/// <summary>Load only user-level settings, excluding project settings.</summary>
async Task ExampleUserOnly()
{
    Console.WriteLine("=== User Settings Only Example ===");
    Console.WriteLine("Setting sources: ['user']");
    Console.WriteLine("Expected: Project slash commands (like /commit) will NOT be available\n");

    var sdkDir = Directory.GetParent(Directory.GetCurrentDirectory())?.Parent?.FullName
                 ?? Directory.GetCurrentDirectory();

    var options = ClaudeApi.Options()
        .SettingSources(SettingSource.User)
        .Cwd(sdkDir)
        .Build();

    await using var client = new ClaudeSDKClient(options);
    await client.ConnectAsync();

    await client.QueryAsync("What is 2 + 2?");

    await foreach (var msg in client.ReceiveResponseAsync())
    {
        if (msg is SystemMessage sm && sm.Subtype == "init")
        {
            var commands = ExtractSlashCommands(sm);
            Console.WriteLine($"Available slash commands: [{string.Join(", ", commands)}]");
            if (commands.Contains("commit"))
                Console.WriteLine("X /commit is available (unexpected)");
            else
                Console.WriteLine("OK /commit is NOT available (expected)");
            break;
        }
    }

    Console.WriteLine();
}

/// <summary>Load both project and user settings.</summary>
async Task ExampleProjectAndUser()
{
    Console.WriteLine("=== Project + User Settings Example ===");
    Console.WriteLine("Setting sources: ['user', 'project']");
    Console.WriteLine("Expected: Project slash commands (like /commit) WILL be available\n");

    var sdkDir = Directory.GetParent(Directory.GetCurrentDirectory())?.Parent?.FullName
                 ?? Directory.GetCurrentDirectory();

    var options = ClaudeApi.Options()
        .SettingSources(SettingSource.User, SettingSource.Project)
        .Cwd(sdkDir)
        .Build();

    await using var client = new ClaudeSDKClient(options);
    await client.ConnectAsync();

    await client.QueryAsync("What is 2 + 2?");

    await foreach (var msg in client.ReceiveResponseAsync())
    {
        if (msg is SystemMessage sm && sm.Subtype == "init")
        {
            var commands = ExtractSlashCommands(sm);
            Console.WriteLine($"Available slash commands: [{string.Join(", ", commands)}]");
            if (commands.Contains("commit"))
                Console.WriteLine("OK /commit is available (expected)");
            else
                Console.WriteLine("X /commit is NOT available (unexpected)");
            break;
        }
    }

    Console.WriteLine();
}
