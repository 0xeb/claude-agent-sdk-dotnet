// Port of claude-agent-sdk-python/tests/test_streaming_client.py
// Tests for ClaudeSDKClient streaming functionality.

using System.Text.Json;
using Claude.AgentSdk;
using Claude.AgentSdk.Transport;
using Xunit;

namespace Claude.AgentSdk.Tests;

public sealed class StreamingClientTests
{
    [Fact]
    public async Task Client_NotConnected_QueryThrows()
    {
        var client = new ClaudeSDKClient();

        var ex = await Assert.ThrowsAsync<CliConnectionException>(
            () => client.QueryAsync("Test"));

        Assert.Contains("Not connected", ex.Message);
    }

    [Fact]
    public async Task Client_NotConnected_InterruptThrows()
    {
        var client = new ClaudeSDKClient();

        var ex = await Assert.ThrowsAsync<CliConnectionException>(
            () => client.InterruptAsync());

        Assert.Contains("Not connected", ex.Message);
    }

    [Fact]
    public async Task Client_NotConnected_ReceiveMessagesThrows()
    {
        var client = new ClaudeSDKClient();

        await Assert.ThrowsAsync<CliConnectionException>(async () =>
        {
            await foreach (var _ in client.ReceiveMessagesAsync())
            {
            }
        });
    }

    [Fact]
    public async Task Client_NotConnected_ReceiveResponseThrows()
    {
        var client = new ClaudeSDKClient();

        await Assert.ThrowsAsync<CliConnectionException>(async () =>
        {
            await foreach (var _ in client.ReceiveResponseAsync())
            {
            }
        });
    }

    [Fact]
    public async Task Client_NotConnected_GetMcpStatusThrows()
    {
        var client = new ClaudeSDKClient();

        await Assert.ThrowsAsync<CliConnectionException>(
            () => client.GetMcpStatusAsync());
    }

    [Fact]
    public async Task Client_WithOptions_PassesOptionsThrough()
    {
        var options = new ClaudeAgentOptions
        {
            Cwd = "/custom/path",
            AllowedTools = ["Read", "Write"],
            SystemPrompt = "Be helpful"
        };

        var client = new ClaudeSDKClient(options);

        // The client should store the options internally
        // (we can't easily test transport creation without a mock,
        //  but we can verify the client was constructed successfully)
        Assert.NotNull(client);
    }

    [Fact]
    public async Task Client_DisconnectWithoutConnect_DoesNotThrow()
    {
        var client = new ClaudeSDKClient();

        // Should not throw
        await client.DisconnectAsync();
    }

    [Fact]
    public async Task Client_DisposeWithoutConnect_DoesNotThrow()
    {
        var client = new ClaudeSDKClient();

        // Should not throw
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Client_CanUseTool_WithPrompt_ThrowsOnConnect()
    {
        // CanUseTool requires streaming mode; providing a prompt is not allowed
        var options = new ClaudeAgentOptions
        {
            CanUseTool = (_, _, _, _) => Task.FromResult<PermissionResult>(new PermissionResultAllow())
        };

        var client = new ClaudeSDKClient(options);

        // Connecting with a prompt and CanUseTool should throw ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.ConnectAsync("Hello"));
    }

    [Fact]
    public async Task Client_CanUseTool_WithPermissionPromptToolName_ThrowsOnConnect()
    {
        // CanUseTool and PermissionPromptToolName are mutually exclusive
        var options = new ClaudeAgentOptions
        {
            CanUseTool = (_, _, _, _) => Task.FromResult<PermissionResult>(new PermissionResultAllow()),
            PermissionPromptToolName = "CustomTool"
        };

        var client = new ClaudeSDKClient(options);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.ConnectAsync());
    }
}
