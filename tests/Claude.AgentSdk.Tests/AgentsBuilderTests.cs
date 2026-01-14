using Claude.AgentSdk.Builders;
using Xunit;

namespace Claude.AgentSdk.Tests;

public sealed class AgentsBuilderTests
{
    [Fact]
    public void Add_RegistersAgent()
    {
        var options = Claude.Options()
            .Agents(a => a.Add("reviewer", "Reviews code", "You are a code reviewer."))
            .Build();

        Assert.NotNull(options.Agents);
        Assert.True(options.Agents.ContainsKey("reviewer"));
        Assert.Equal("Reviews code", options.Agents["reviewer"].Description);
        Assert.Equal("You are a code reviewer.", options.Agents["reviewer"].Prompt);
    }

    [Fact]
    public void Add_WithTools_SetsTools()
    {
        var options = Claude.Options()
            .Agents(a => a.Add("reviewer", "Reviews code", "You are a reviewer.", tools: ["Read", "Grep"]))
            .Build();

        Assert.Equal(["Read", "Grep"], options.Agents!["reviewer"].Tools);
    }

    [Fact]
    public void Add_WithToolsParams_SetsTools()
    {
        var options = Claude.Options()
            .Agents(a => a.Add("reviewer", "Reviews code", "You are a reviewer.", "Read", "Grep"))
            .Build();

        Assert.Equal(["Read", "Grep"], options.Agents!["reviewer"].Tools);
    }

    [Fact]
    public void Add_WithModel_SetsModel()
    {
        var options = Claude.Options()
            .Agents(a => a.Add("fast", "Fast agent", "Be quick.", model: "haiku"))
            .Build();

        Assert.Equal("haiku", options.Agents!["fast"].Model);
    }

    [Fact]
    public void Add_MultipleAgents_RegistersAll()
    {
        var options = Claude.Options()
            .Agents(a => a
                .Add("reviewer", "Reviews code", "Review carefully.")
                .Add("writer", "Writes code", "Write clean code."))
            .Build();

        Assert.Equal(2, options.Agents!.Count);
        Assert.True(options.Agents.ContainsKey("reviewer"));
        Assert.True(options.Agents.ContainsKey("writer"));
    }

    [Fact]
    public void Add_SameName_OverwritesPrevious()
    {
        var options = Claude.Options()
            .Agents(a => a
                .Add("agent", "First", "First prompt.")
                .Add("agent", "Second", "Second prompt."))
            .Build();

        Assert.NotNull(options.Agents);
        Assert.Single(options.Agents);
        Assert.Equal("Second", options.Agents["agent"].Description);
    }

    [Fact]
    public void Add_WithoutTools_ToolsIsNull()
    {
        var options = Claude.Options()
            .Agents(a => a.Add("simple", "Simple agent", "Do stuff."))
            .Build();

        Assert.Null(options.Agents!["simple"].Tools);
    }

    [Fact]
    public void Add_WithEmptyToolsParams_ToolsIsNull()
    {
        var options = Claude.Options()
            .Agents(a => a.Add("simple", "Simple agent", "Do stuff."))
            .Build();

        Assert.Null(options.Agents!["simple"].Tools);
    }
}
