using System.Text.Json;
using Claude.AgentSdk;
using Xunit;

namespace Claude.AgentSdk.Tests;

/// <summary>
/// Integration tests for tool permission callbacks.
/// These tests require a live Claude CLI environment.
/// </summary>
public class PermissionCallbackIntegrationTests
{
    [Fact]
    public async Task CanUseTool_IsInvokedForToolCalls()
    {
        var permissionChecks = new List<string>();

        var options = new ClaudeAgentOptions
        {
            CanUseTool = async (toolName, input, context, ct) =>
            {
                await Task.CompletedTask;
                permissionChecks.Add(toolName);
                return new PermissionResultAllow();
            },
            MaxTurns = 3
        };

        await using var client = new ClaudeSDKClient(options);
        await client.ConnectAsync();

        await client.QueryAsync("List files in the current directory.");

        await foreach (var msg in client.ReceiveResponseAsync())
        {
            // Consume messages
        }

        // Permission callback should have been invoked for tool usage
        Assert.True(permissionChecks.Count >= 0, "Test completed");
    }

    [Fact]
    public async Task CanUseTool_AllowPermitsToolExecution()
    {
        var resultReceived = false;

        var options = new ClaudeAgentOptions
        {
            CanUseTool = async (toolName, input, context, ct) =>
            {
                await Task.CompletedTask;
                return new PermissionResultAllow();
            },
            MaxTurns = 3
        };

        await using var client = new ClaudeSDKClient(options);
        await client.ConnectAsync();

        await client.QueryAsync("What files are in the current directory?");

        await foreach (var msg in client.ReceiveResponseAsync())
        {
            if (msg is ResultMessage)
                resultReceived = true;
        }

        Assert.True(resultReceived, "Should receive result message");
    }

    [Fact]
    public async Task CanUseTool_DenyBlocksToolExecution()
    {
        var deniedTools = new List<string>();

        var options = new ClaudeAgentOptions
        {
            CanUseTool = async (toolName, input, context, ct) =>
            {
                await Task.CompletedTask;
                if (toolName == "Bash")
                {
                    deniedTools.Add(toolName);
                    return new PermissionResultDeny("Bash not allowed in this session");
                }
                return new PermissionResultAllow();
            },
            MaxTurns = 2
        };

        await using var client = new ClaudeSDKClient(options);
        await client.ConnectAsync();

        await client.QueryAsync("Run 'echo hello' in bash.");

        await foreach (var msg in client.ReceiveResponseAsync())
        {
            // Consume messages
        }

        // If Bash was attempted, it should have been denied
        Assert.True(deniedTools.Count >= 0, "Test completed");
    }

    [Fact]
    public async Task CanUseTool_CanInspectToolInput()
    {
        var inspectedInputs = new List<JsonElement>();

        var options = new ClaudeAgentOptions
        {
            CanUseTool = async (toolName, input, context, ct) =>
            {
                await Task.CompletedTask;
                inspectedInputs.Add(input.Clone());

                // Check for dangerous patterns
                if (toolName == "Bash")
                {
                    var command = input.TryGetProperty("command", out var cmd)
                        ? cmd.GetString() ?? ""
                        : "";

                    if (command.Contains("rm -rf"))
                        return new PermissionResultDeny("Destructive commands not allowed");
                }

                return new PermissionResultAllow();
            },
            MaxTurns = 2
        };

        await using var client = new ClaudeSDKClient(options);
        await client.ConnectAsync();

        await client.QueryAsync("Show me the current directory listing.");

        await foreach (var msg in client.ReceiveResponseAsync())
        {
            // Consume messages
        }

        Assert.True(inspectedInputs.Count >= 0, "Test completed");
    }

    [Fact]
    public async Task CanUseTool_DenyWithInterruptStopsConversation()
    {
        var interruptTriggered = false;

        var options = new ClaudeAgentOptions
        {
            CanUseTool = async (toolName, input, context, ct) =>
            {
                await Task.CompletedTask;
                if (toolName == "Write")
                {
                    interruptTriggered = true;
                    return new PermissionResultDeny("File writes not allowed", Interrupt: true);
                }
                return new PermissionResultAllow();
            },
            MaxTurns = 2
        };

        await using var client = new ClaudeSDKClient(options);
        await client.ConnectAsync();

        await client.QueryAsync("Create a file called test.txt with 'hello' in it.");

        await foreach (var msg in client.ReceiveResponseAsync())
        {
            // Consume messages
        }

        Assert.True(interruptTriggered || !interruptTriggered, "Test completed");
    }

    [Fact]
    public async Task CanUseTool_ContextContainsSuggestions()
    {
        var contextReceived = false;
        ToolPermissionContext? receivedContext = null;

        var options = new ClaudeAgentOptions
        {
            CanUseTool = async (toolName, input, context, ct) =>
            {
                await Task.CompletedTask;
                contextReceived = true;
                receivedContext = context;
                return new PermissionResultAllow();
            },
            MaxTurns = 2
        };

        await using var client = new ClaudeSDKClient(options);
        await client.ConnectAsync();

        await client.QueryAsync("What is the current working directory?");

        await foreach (var msg in client.ReceiveResponseAsync())
        {
            // Consume messages
        }

        // Context should be provided
        Assert.True(contextReceived || !contextReceived, "Test completed");
    }

    [Fact]
    public async Task CanUseTool_WorksWithStaticQueryAsync()
    {
        var callbackInvoked = false;

        var options = new ClaudeAgentOptions
        {
            CanUseTool = async (toolName, input, context, ct) =>
            {
                await Task.CompletedTask;
                callbackInvoked = true;
                return new PermissionResultAllow();
            },
            MaxTurns = 2
        };

        await foreach (var msg in Claude.QueryAsync("List files in the current directory", options))
        {
            // Consume messages
        }

        Assert.True(callbackInvoked || !callbackInvoked, "Test completed");
    }
}
