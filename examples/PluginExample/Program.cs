// Plugin Example
// Port of claude-agent-sdk-python/examples/plugin_example.py
//
// Demonstrates how to use plugins with Claude Code SDK.
// Plugins allow extending Claude Code with custom commands, agents, skills, and hooks.

using System.Text.Json;
using Claude.AgentSdk;
using ClaudeApi = Claude.AgentSdk.Claude;

Console.WriteLine("=== Plugin Example ===");
Console.WriteLine();

// Get the path to the demo plugin
// In production, you can use any path to your plugin directory
var pluginPath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples", "plugins", "demo-plugin")
);

var options = ClaudeApi.Options()
    .Plugin("local", pluginPath)
    .MaxTurns(1) // Limit to one turn for quick demo
    .Build();

Console.WriteLine($"Loading plugin from: {pluginPath}");
Console.WriteLine();

var foundPlugins = false;

await foreach (var message in ClaudeApi.QueryAsync("Hello!", options))
{
    if (message is SystemMessage sys && sys.Subtype == "init")
    {
        Console.WriteLine("System initialized!");

        if (sys.Data.ValueKind == JsonValueKind.Object)
        {
            var keys = new List<string>();
            foreach (var prop in sys.Data.EnumerateObject())
                keys.Add(prop.Name);
            Console.WriteLine($"System message data keys: [{string.Join(", ", keys)}]");
            Console.WriteLine();
        }

        // Check for plugins in the system message
        if (sys.Data.TryGetProperty("plugins", out var pluginsData) &&
            pluginsData.ValueKind == JsonValueKind.Array &&
            pluginsData.GetArrayLength() > 0)
        {
            Console.WriteLine("Plugins loaded:");
            foreach (var plugin in pluginsData.EnumerateArray())
            {
                var name = plugin.TryGetProperty("name", out var n) ? n.GetString() : "unknown";
                var path = plugin.TryGetProperty("path", out var p) ? p.GetString() : "unknown";
                Console.WriteLine($"  - {name} (path: {path})");
            }
            foundPlugins = true;
        }
        else
        {
            Console.WriteLine("Note: Plugin was passed via CLI but may not appear in system message.");
            Console.WriteLine($"Plugin path configured: {pluginPath}");
            foundPlugins = true;
        }
    }
}

if (foundPlugins)
    Console.WriteLine("\nPlugin successfully configured!");
