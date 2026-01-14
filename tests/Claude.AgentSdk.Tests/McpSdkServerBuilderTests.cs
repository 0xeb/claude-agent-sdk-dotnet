using System.Text.Json;
using Claude.AgentSdk.Mcp;
using Xunit;

namespace Claude.AgentSdk.Tests;

public sealed class McpSdkServerBuilderTests
{
    [Fact]
    public async Task Tool_WithPrimitiveParameters_GeneratesSchemaAndInvokes()
    {
        var servers = McpServers.Sdk(
            "calculator",
            s => s.Tool("add", (double a, double b) => a + b, "Add two numbers")
        );

        var config = Assert.IsType<McpSdkServerConfig>(servers["calculator"]);
        Assert.NotNull(config.Handlers);

        var tools = await config.Handlers.ListTools!(CancellationToken.None);
        Assert.Single(tools);
        Assert.Equal("add", tools[0].Name);
        Assert.Equal("Add two numbers", tools[0].Description);

        var schema = tools[0].InputSchema!.Value;
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.Equal("number", schema.GetProperty("properties").GetProperty("a").GetProperty("type").GetString());
        Assert.Equal("number", schema.GetProperty("properties").GetProperty("b").GetProperty("type").GetString());

        var result = await config.Handlers.CallTool!(
            "add",
            JsonSerializer.SerializeToElement(new { A = 2, B = 3 }),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Single(result.Content);
        Assert.Equal("text", result.Content[0].Type);
        Assert.Equal("5", result.Content[0].Text);
    }

    private sealed record AddArgs(double A, double B);

    [Fact]
    public async Task Tool_WithArgsObject_BindsWholeArgsObject()
    {
        var servers = McpServers.Sdk(
            "calculator",
            s => s.Tool("add", (AddArgs args) => args.A + args.B)
        );

        var config = Assert.IsType<McpSdkServerConfig>(servers["calculator"]);
        var tools = await config.Handlers.ListTools!(CancellationToken.None);
        Assert.Single(tools);

        var schema = tools[0].InputSchema!.Value;
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.True(schema.GetProperty("properties").TryGetProperty("a", out _));
        Assert.True(schema.GetProperty("properties").TryGetProperty("b", out _));

        var result = await config.Handlers.CallTool!(
            "add",
            JsonSerializer.SerializeToElement(new { A = 10, B = 20 }),
            CancellationToken.None
        );

        Assert.Equal("30", result.Content[0].Text);
    }
}
