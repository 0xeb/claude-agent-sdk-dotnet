using Claude.AgentSdk.Builders;
using Xunit;

namespace Claude.AgentSdk.Tests;

public sealed class SandboxBuilderTests
{
    [Fact]
    public void Enable_SetsEnabled()
    {
        var options = Claude.Options()
            .Sandbox(s => s.Enable())
            .Build();

        Assert.NotNull(options.Sandbox);
        Assert.True(options.Sandbox.Enabled);
    }

    [Fact]
    public void Disable_SetsDisabled()
    {
        var options = Claude.Options()
            .Sandbox(s => s.Disable())
            .Build();

        Assert.False(options.Sandbox!.Enabled);
    }

    [Fact]
    public void AutoAllowBash_SetsValue()
    {
        var options = Claude.Options()
            .Sandbox(s => s.AutoAllowBash())
            .Build();

        Assert.True(options.Sandbox!.AutoAllowBashIfSandboxed);
    }

    [Fact]
    public void ExcludeCommands_AddsCommands()
    {
        var options = Claude.Options()
            .Sandbox(s => s.ExcludeCommands("rm", "sudo"))
            .Build();

        Assert.Equal(["rm", "sudo"], options.Sandbox!.ExcludedCommands);
    }

    [Fact]
    public void AllowUnsandboxedCommands_SetsValue()
    {
        var options = Claude.Options()
            .Sandbox(s => s.AllowUnsandboxedCommands())
            .Build();

        Assert.True(options.Sandbox!.AllowUnsandboxedCommands);
    }

    [Fact]
    public void Network_ConfiguresNetwork()
    {
        var options = Claude.Options()
            .Sandbox(s => s.Network(n => n
                .AllowLocalBinding()
                .HttpProxyPort(8080)))
            .Build();

        Assert.NotNull(options.Sandbox!.Network);
        Assert.True(options.Sandbox.Network.AllowLocalBinding);
        Assert.Equal(8080, options.Sandbox.Network.HttpProxyPort);
    }

    [Fact]
    public void Network_AllowUnixSockets_AddsSockets()
    {
        var options = Claude.Options()
            .Sandbox(s => s.Network(n => n.AllowUnixSockets("/var/run/docker.sock")))
            .Build();

        Assert.Equal(["/var/run/docker.sock"], options.Sandbox!.Network!.AllowUnixSockets);
    }

    [Fact]
    public void Network_AllowAllUnixSockets_SetsValue()
    {
        var options = Claude.Options()
            .Sandbox(s => s.Network(n => n.AllowAllUnixSockets()))
            .Build();

        Assert.True(options.Sandbox!.Network!.AllowAllUnixSockets);
    }

    [Fact]
    public void IgnoreViolations_ConfiguresViolations()
    {
        var options = Claude.Options()
            .Sandbox(s => s.IgnoreViolations(v => v
                .File("/tmp/*")
                .Network("localhost")))
            .Build();

        Assert.NotNull(options.Sandbox!.IgnoreViolations);
        Assert.Equal(["/tmp/*"], options.Sandbox.IgnoreViolations.File);
        Assert.Equal(["localhost"], options.Sandbox.IgnoreViolations.Network);
    }

    [Fact]
    public void EnableWeakerNestedSandbox_SetsValue()
    {
        var options = Claude.Options()
            .Sandbox(s => s.EnableWeakerNestedSandbox())
            .Build();

        Assert.True(options.Sandbox!.EnableWeakerNestedSandbox);
    }

    [Fact]
    public void CompleteExample_BuildsCorrectSandbox()
    {
        var options = Claude.Options()
            .Sandbox(s => s
                .Enable()
                .AutoAllowBash()
                .ExcludeCommands("rm", "rmdir")
                .Network(n => n
                    .AllowLocalBinding()
                    .HttpProxyPort(8080)
                    .SocksProxyPort(1080))
                .IgnoreViolations(v => v
                    .File("/tmp/*")
                    .Network("*.local")))
            .Build();

        Assert.True(options.Sandbox!.Enabled);
        Assert.True(options.Sandbox.AutoAllowBashIfSandboxed);
        Assert.Equal(["rm", "rmdir"], options.Sandbox.ExcludedCommands);
        Assert.True(options.Sandbox.Network!.AllowLocalBinding);
        Assert.Equal(8080, options.Sandbox.Network.HttpProxyPort);
        Assert.Equal(1080, options.Sandbox.Network.SocksProxyPort);
        Assert.Equal(["/tmp/*"], options.Sandbox.IgnoreViolations!.File);
        Assert.Equal(["*.local"], options.Sandbox.IgnoreViolations.Network);
    }
}
