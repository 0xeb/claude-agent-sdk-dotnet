using System.Text.Json;
using Claude.AgentSdk.Builders;
using Xunit;

namespace Claude.AgentSdk.Tests;

public sealed class HooksBuilderTests
{
    private static Task<HookOutput> DummyCallback(JsonElement input, string? toolUseId, HookContext context, CancellationToken ct)
        => Task.FromResult(new HookOutput());

    [Fact]
    public void PreToolUse_RegistersHook()
    {
        var options = Claude.Options()
            .Hooks(h => h.PreToolUse("Bash", DummyCallback))
            .Build();

        Assert.NotNull(options.Hooks);
        Assert.True(options.Hooks.ContainsKey(HookEvent.PreToolUse));
        Assert.Single(options.Hooks[HookEvent.PreToolUse]);
        Assert.Equal("Bash", options.Hooks[HookEvent.PreToolUse][0].Matcher);
    }

    [Fact]
    public void PostToolUse_RegistersHook()
    {
        var options = Claude.Options()
            .Hooks(h => h.PostToolUse("*", DummyCallback))
            .Build();

        Assert.NotNull(options.Hooks);
        Assert.True(options.Hooks.ContainsKey(HookEvent.PostToolUse));
        Assert.Equal("*", options.Hooks[HookEvent.PostToolUse][0].Matcher);
    }

    [Fact]
    public void MultipleHooks_SameEvent_RegistersAll()
    {
        var options = Claude.Options()
            .Hooks(h => h
                .PreToolUse("Bash", DummyCallback)
                .PreToolUse("Read", DummyCallback))
            .Build();

        Assert.NotNull(options.Hooks);
        Assert.Equal(2, options.Hooks[HookEvent.PreToolUse].Count);
        Assert.Equal("Bash", options.Hooks[HookEvent.PreToolUse][0].Matcher);
        Assert.Equal("Read", options.Hooks[HookEvent.PreToolUse][1].Matcher);
    }

    [Fact]
    public void MultipleHookTypes_RegistersSeparately()
    {
        var options = Claude.Options()
            .Hooks(h => h
                .PreToolUse("Bash", DummyCallback)
                .PostToolUse("Bash", DummyCallback)
                .OnStop(DummyCallback))
            .Build();

        Assert.NotNull(options.Hooks);
        Assert.Equal(3, options.Hooks.Count);
        Assert.True(options.Hooks.ContainsKey(HookEvent.PreToolUse));
        Assert.True(options.Hooks.ContainsKey(HookEvent.PostToolUse));
        Assert.True(options.Hooks.ContainsKey(HookEvent.Stop));
    }

    [Fact]
    public void PreToolUse_WithTimeout_SetsTimeout()
    {
        var options = Claude.Options()
            .Hooks(h => h.PreToolUse("Bash", DummyCallback, timeout: 5000))
            .Build();

        Assert.Equal(5000, options.Hooks![HookEvent.PreToolUse][0].Timeout);
    }

    [Fact]
    public void PreToolUse_WithMultipleCallbacks_RegistersAll()
    {
        var options = Claude.Options()
            .Hooks(h => h.PreToolUse("Bash", DummyCallback, DummyCallback))
            .Build();

        Assert.Equal(2, options.Hooks![HookEvent.PreToolUse][0].Hooks!.Count);
    }

    [Fact]
    public void OnStop_RegistersWithNullMatcher()
    {
        var options = Claude.Options()
            .Hooks(h => h.OnStop(DummyCallback))
            .Build();

        Assert.Null(options.Hooks![HookEvent.Stop][0].Matcher);
    }

    [Fact]
    public void UserPromptSubmit_Registers()
    {
        var options = Claude.Options()
            .Hooks(h => h.UserPromptSubmit(DummyCallback))
            .Build();

        Assert.True(options.Hooks!.ContainsKey(HookEvent.UserPromptSubmit));
    }

    [Fact]
    public void On_GenericMethod_Works()
    {
        var options = Claude.Options()
            .Hooks(h => h.On(HookEvent.PreCompact, null, DummyCallback))
            .Build();

        Assert.True(options.Hooks!.ContainsKey(HookEvent.PreCompact));
    }
}
