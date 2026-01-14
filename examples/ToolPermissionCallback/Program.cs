// Tool Permission Callback example for Claude Agent SDK for .NET
// Port of claude-agent-sdk-python/examples/tool_permission_callback.py

using System.Text.Json;
using Claude.AgentSdk;

Console.WriteLine("Claude Agent SDK for .NET - Tool Permission Callback Example");
Console.WriteLine("=============================================================\n");

Console.WriteLine("This example demonstrates how to:");
Console.WriteLine("1. Allow/deny tools based on type");
Console.WriteLine("2. Modify tool inputs for safety");
Console.WriteLine("3. Log tool usage");
Console.WriteLine("=" + new string('=', 59) + "\n");

// Track tool usage for demonstration
var toolUsageLog = new List<Dictionary<string, object?>>();

// Permission callback to control tool access
Task<PermissionResult> MyPermissionCallback(
    string toolName,
    JsonElement inputData,
    ToolPermissionContext context,
    CancellationToken ct)
{
    // Log the tool request
    toolUsageLog.Add(new Dictionary<string, object?>
    {
        ["tool"] = toolName,
        ["input"] = inputData.ToString(),
        ["suggestions"] = context.Suggestions
    });

    Console.WriteLine($"\nTool Permission Request: {toolName}");
    Console.WriteLine($"   Input: {inputData}");

    // Always allow read operations
    if (toolName is "Read" or "Glob" or "Grep")
    {
        Console.WriteLine($"   Automatically allowing {toolName} (read-only operation)");
        return Task.FromResult<PermissionResult>(new PermissionResultAllow());
    }

    // Deny write operations to system directories
    if (toolName is "Write" or "Edit" or "MultiEdit")
    {
        var filePath = inputData.TryGetProperty("file_path", out var fpEl)
            ? fpEl.GetString() ?? ""
            : "";

        if (filePath.StartsWith("/etc/") || filePath.StartsWith("/usr/") ||
            filePath.StartsWith("C:\\Windows"))
        {
            Console.WriteLine($"   Denying write to system directory: {filePath}");
            return Task.FromResult<PermissionResult>(new PermissionResultDeny(
                $"Cannot write to system directory: {filePath}"
            ));
        }

        // Redirect writes to a safe directory (modify input)
        if (!filePath.StartsWith("/tmp/") && !filePath.StartsWith("./") &&
            !filePath.StartsWith("C:\\Temp"))
        {
            var fileName = Path.GetFileName(filePath);
            var safePath = $"./safe_output/{fileName}";
            Console.WriteLine($"   Redirecting write from {filePath} to {safePath}");

            // Create modified input
            var modifiedInput = new Dictionary<string, object>();
            foreach (var prop in inputData.EnumerateObject())
            {
                modifiedInput[prop.Name] = prop.Name == "file_path"
                    ? safePath
                    : prop.Value.ToString()!;
            }

            return Task.FromResult<PermissionResult>(new PermissionResultAllow(
                JsonSerializer.SerializeToElement(modifiedInput)
            ));
        }
    }

    // Check dangerous bash commands
    if (toolName == "Bash")
    {
        var command = inputData.TryGetProperty("command", out var cmdEl)
            ? cmdEl.GetString() ?? ""
            : "";

        var dangerousCommands = new[] { "rm -rf", "sudo", "chmod 777", "dd if=", "mkfs", "format" };

        foreach (var dangerous in dangerousCommands)
        {
            if (command.Contains(dangerous, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"   Denying dangerous command: {command}");
                return Task.FromResult<PermissionResult>(new PermissionResultDeny(
                    $"Dangerous command pattern detected: {dangerous}"
                ));
            }
        }

        // Allow but log the command
        Console.WriteLine($"   Allowing bash command: {command}");
        return Task.FromResult<PermissionResult>(new PermissionResultAllow());
    }

    // For all other tools, allow by default (in real code you might prompt the user)
    Console.WriteLine($"   Unknown tool: {toolName} - allowing by default");
    return Task.FromResult<PermissionResult>(new PermissionResultAllow());
}

// Configure options with our callback
var options = new ClaudeAgentOptions
{
    CanUseTool = MyPermissionCallback,
    PermissionMode = PermissionMode.Default,
    Cwd = "."
};

// Create client and send a query that will use multiple tools
await using var client = new ClaudeSDKClient(options);
await client.ConnectAsync();

Console.WriteLine("Sending query to Claude...");
await client.QueryAsync(
    "Please do the following:\n" +
    "1. List the files in the current directory\n" +
    "2. Create a simple hello world text file at hello.txt\n" +
    "3. Show me what's in the file"
);

Console.WriteLine("\nReceiving response...");
var messageCount = 0;

await foreach (var msg in client.ReceiveResponseAsync())
{
    messageCount++;

    if (msg is AssistantMessage am)
    {
        foreach (var block in am.Content)
        {
            if (block is TextBlock tb)
                Console.WriteLine($"\nClaude: {tb.Text}");
        }
    }
    else if (msg is ResultMessage rm)
    {
        Console.WriteLine("\nTask completed!");
        Console.WriteLine($"   Duration: {rm.DurationMs}ms");
        if (rm.TotalCostUsd.HasValue)
            Console.WriteLine($"   Cost: ${rm.TotalCostUsd:F4}");
        Console.WriteLine($"   Messages processed: {messageCount}");
    }
}

// Print tool usage summary
Console.WriteLine("\n" + new string('=', 60));
Console.WriteLine("Tool Usage Summary");
Console.WriteLine(new string('=', 60));
for (var i = 0; i < toolUsageLog.Count; i++)
{
    var usage = toolUsageLog[i];
    Console.WriteLine($"\n{i + 1}. Tool: {usage["tool"]}");
    Console.WriteLine($"   Input: {usage["input"]}");
}
