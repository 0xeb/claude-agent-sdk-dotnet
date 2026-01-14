using System.Text.Json;
using Claude.AgentSdk.Mcp;
using Xunit;

namespace Claude.AgentSdk.Tests;

public class McpBridgeTests
{
    #region McpToolDefinition Tests

    [Fact]
    public void McpToolDefinition_CreatesWithRequiredProperties()
    {
        var tool = new McpToolDefinition
        {
            Name = "test_tool",
            Description = "A test tool"
        };

        Assert.Equal("test_tool", tool.Name);
        Assert.Equal("A test tool", tool.Description);
        Assert.Null(tool.InputSchema);
    }

    [Fact]
    public void McpToolDefinition_SupportsInputSchema()
    {
        var schema = JsonSerializer.SerializeToElement(new { type = "object" });

        var tool = new McpToolDefinition
        {
            Name = "typed_tool",
            Description = "A typed tool",
            InputSchema = schema
        };

        Assert.NotNull(tool.InputSchema);
        Assert.Equal(JsonValueKind.Object, tool.InputSchema.Value.ValueKind);
    }

    #endregion

    #region McpToolResult Tests

    [Fact]
    public void McpToolResult_CreatesSuccessResult()
    {
        var result = new McpToolResult
        {
            Content = [new McpContent { Type = "text", Text = "Success!" }],
            IsError = false
        };

        Assert.Single(result.Content);
        Assert.Equal("text", result.Content[0].Type);
        Assert.Equal("Success!", result.Content[0].Text);
        Assert.False(result.IsError);
    }

    [Fact]
    public void McpToolResult_CreatesErrorResult()
    {
        var result = new McpToolResult
        {
            Content = [new McpContent { Type = "text", Text = "Error occurred" }],
            IsError = true
        };

        Assert.True(result.IsError);
    }

    #endregion

    #region McpPromptDefinition Tests

    [Fact]
    public void McpPromptDefinition_CreatesWithArguments()
    {
        var prompt = new McpPromptDefinition
        {
            Name = "code_review",
            Description = "Review code",
            Arguments =
            [
                new McpPromptArgument
                {
                    Name = "code",
                    Description = "Code to review",
                    Required = true
                }
            ]
        };

        Assert.Equal("code_review", prompt.Name);
        Assert.Single(prompt.Arguments!);
        Assert.True(prompt.Arguments![0].Required);
    }

    #endregion

    #region McpResourceDefinition Tests

    [Fact]
    public void McpResourceDefinition_CreatesWithAllProperties()
    {
        var resource = new McpResourceDefinition
        {
            Uri = "file:///test.txt",
            Name = "Test File",
            Description = "A test file",
            MimeType = "text/plain"
        };

        Assert.Equal("file:///test.txt", resource.Uri);
        Assert.Equal("Test File", resource.Name);
        Assert.Equal("text/plain", resource.MimeType);
    }

    #endregion

    #region McpServerHandlers Tests

    [Fact]
    public async Task McpServerHandlers_InvokesListToolsHandler()
    {
        var handlers = new McpServerHandlers
        {
            ListTools = ct => Task.FromResult<IReadOnlyList<McpToolDefinition>>([
                new McpToolDefinition { Name = "add", Description = "Add numbers" }
            ])
        };

        var tools = await handlers.ListTools!(CancellationToken.None);

        Assert.Single(tools);
        Assert.Equal("add", tools[0].Name);
    }

    [Fact]
    public async Task McpServerHandlers_InvokesCallToolHandler()
    {
        var handlers = new McpServerHandlers
        {
            CallTool = (name, args, ct) =>
            {
                var a = args.GetProperty("a").GetInt32();
                var b = args.GetProperty("b").GetInt32();
                return Task.FromResult(new McpToolResult
                {
                    Content = [new McpContent { Type = "text", Text = (a + b).ToString() }]
                });
            }
        };

        var args = JsonSerializer.SerializeToElement(new { a = 5, b = 3 });
        var result = await handlers.CallTool!("add", args, CancellationToken.None);

        Assert.Equal("8", result.Content[0].Text);
    }

    [Fact]
    public async Task McpServerHandlers_InvokesListPromptsHandler()
    {
        var handlers = new McpServerHandlers
        {
            ListPrompts = ct => Task.FromResult<IReadOnlyList<McpPromptDefinition>>([
                new McpPromptDefinition { Name = "summarize", Description = "Summarize text" }
            ])
        };

        var prompts = await handlers.ListPrompts!(CancellationToken.None);

        Assert.Single(prompts);
        Assert.Equal("summarize", prompts[0].Name);
    }

    [Fact]
    public async Task McpServerHandlers_InvokesGetPromptHandler()
    {
        var handlers = new McpServerHandlers
        {
            GetPrompt = (name, args, ct) => Task.FromResult(new McpPromptResult
            {
                Description = $"Prompt: {name}",
                Messages =
                [
                    new McpPromptMessage
                    {
                        Role = "user",
                        Content = new McpContent { Type = "text", Text = "Test message" }
                    }
                ]
            })
        };

        var result = await handlers.GetPrompt!("test", null, CancellationToken.None);

        Assert.Equal("Prompt: test", result.Description);
        Assert.Single(result.Messages);
    }

    [Fact]
    public async Task McpServerHandlers_InvokesListResourcesHandler()
    {
        var handlers = new McpServerHandlers
        {
            ListResources = ct => Task.FromResult<IReadOnlyList<McpResourceDefinition>>([
                new McpResourceDefinition { Uri = "file:///a.txt", Name = "File A" }
            ])
        };

        var resources = await handlers.ListResources!(CancellationToken.None);

        Assert.Single(resources);
        Assert.Equal("file:///a.txt", resources[0].Uri);
    }

    [Fact]
    public async Task McpServerHandlers_InvokesReadResourceHandler()
    {
        var handlers = new McpServerHandlers
        {
            ReadResource = (uri, ct) => Task.FromResult(new McpResourceResult
            {
                Contents =
                [
                    new McpResourceContent
                    {
                        Uri = uri,
                        MimeType = "text/plain",
                        Text = "File content"
                    }
                ]
            })
        };

        var result = await handlers.ReadResource!("file:///a.txt", CancellationToken.None);

        Assert.Single(result.Contents);
        Assert.Equal("File content", result.Contents[0].Text);
    }

    #endregion

    #region SdkMcpBridge Tests

    [Fact]
    public async Task SdkMcpBridge_HandlesInitializeMessage()
    {
        var handlers = new McpServerHandlers
        {
            ListTools = ct => Task.FromResult<IReadOnlyList<McpToolDefinition>>([])
        };

        await using var bridge = new SdkMcpBridge(handlers, "test-server");
        await bridge.StartAsync();

        var request = JsonSerializer.SerializeToElement(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new { }
        });

        var response = await bridge.SendMessageAsync(request);

        Assert.Equal("2.0", response.GetProperty("jsonrpc").GetString());
        Assert.Equal(1, response.GetProperty("id").GetInt32());

        var result = response.GetProperty("result");
        Assert.Equal("2024-11-05", result.GetProperty("protocolVersion").GetString());
        Assert.Equal("test-server", result.GetProperty("serverInfo").GetProperty("name").GetString());
    }

    [Fact]
    public async Task SdkMcpBridge_HandlesToolsListMessage()
    {
        var handlers = new McpServerHandlers
        {
            ListTools = ct => Task.FromResult<IReadOnlyList<McpToolDefinition>>([
                new McpToolDefinition { Name = "add", Description = "Add numbers" },
                new McpToolDefinition { Name = "sub", Description = "Subtract numbers" }
            ])
        };

        await using var bridge = new SdkMcpBridge(handlers, "calculator");
        await bridge.StartAsync();

        var request = JsonSerializer.SerializeToElement(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/list"
        });

        var response = await bridge.SendMessageAsync(request);
        var tools = response.GetProperty("result").GetProperty("tools");

        Assert.Equal(2, tools.GetArrayLength());
    }

    [Fact]
    public async Task SdkMcpBridge_HandlesToolsCallMessage()
    {
        var handlers = new McpServerHandlers
        {
            CallTool = (name, args, ct) =>
            {
                var a = args.GetProperty("a").GetInt32();
                var b = args.GetProperty("b").GetInt32();
                var result = name == "add" ? a + b : a - b;
                return Task.FromResult(new McpToolResult
                {
                    Content = [new McpContent { Type = "text", Text = result.ToString() }]
                });
            }
        };

        await using var bridge = new SdkMcpBridge(handlers, "calculator");
        await bridge.StartAsync();

        var request = JsonSerializer.SerializeToElement(new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "tools/call",
            @params = new { name = "add", arguments = new { a = 10, b = 5 } }
        });

        var response = await bridge.SendMessageAsync(request);
        var result = response.GetProperty("result");

        Assert.Equal("15", result.GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task SdkMcpBridge_HandlesUnsupportedMethod()
    {
        var handlers = new McpServerHandlers();

        await using var bridge = new SdkMcpBridge(handlers, "test");
        await bridge.StartAsync();

        var request = JsonSerializer.SerializeToElement(new
        {
            jsonrpc = "2.0",
            id = 4,
            method = "unsupported/method"
        });

        var response = await bridge.SendMessageAsync(request);

        Assert.True(response.TryGetProperty("error", out var error));
        Assert.Equal(-32603, error.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task SdkMcpBridge_ReturnsEmptyToolsWhenNoHandler()
    {
        var handlers = new McpServerHandlers(); // No ListTools handler

        await using var bridge = new SdkMcpBridge(handlers, "empty");
        await bridge.StartAsync();

        var request = JsonSerializer.SerializeToElement(new
        {
            jsonrpc = "2.0",
            id = 5,
            method = "tools/list"
        });

        var response = await bridge.SendMessageAsync(request);
        var tools = response.GetProperty("result").GetProperty("tools");

        Assert.Equal(0, tools.GetArrayLength());
    }

    [Fact]
    public void SdkMcpBridge_ThrowsOnNullHandlers()
    {
        Assert.Throws<ArgumentNullException>(() => new SdkMcpBridge(null!, "test"));
    }

    [Fact]
    public void SdkMcpBridge_ThrowsOnNullServerName()
    {
        var handlers = new McpServerHandlers();
        Assert.Throws<ArgumentNullException>(() => new SdkMcpBridge(handlers, null!));
    }

    #endregion

    #region McpSdkServerConfig Tests

    [Fact]
    public void McpSdkServerConfig_HasCorrectType()
    {
        var handlers = new McpServerHandlers();
        var config = new McpSdkServerConfig
        {
            Name = "test",
            Handlers = handlers
        };

        Assert.Equal("sdk", config.Type);
        Assert.Equal("test", config.Name);
        Assert.Same(handlers, config.Handlers);
    }

    #endregion
}
