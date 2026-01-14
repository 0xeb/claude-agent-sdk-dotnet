using System.Text.Json;
using Xunit;

namespace Claude.AgentSdk.Tests;

public class TypesTests
{
    [Fact]
    public void TextBlock_SerializesCorrectly()
    {
        var block = new TextBlock("Hello, world!");
        var json = JsonSerializer.Serialize(block, ClaudeJsonContext.Default.TextBlock);

        Assert.Contains("\"text\"", json);
        Assert.Contains("Hello, world!", json);
    }

    [Fact]
    public void ThinkingBlock_SerializesCorrectly()
    {
        var block = new ThinkingBlock("I'm thinking...", "signature123");
        var json = JsonSerializer.Serialize(block, ClaudeJsonContext.Default.ThinkingBlock);

        Assert.Contains("thinking", json);
        Assert.Contains("signature", json);
    }

    [Fact]
    public void ClaudeAgentOptions_HasCorrectDefaults()
    {
        var options = new ClaudeAgentOptions();

        Assert.Empty(options.AllowedTools);
        Assert.Empty(options.DisallowedTools);
        Assert.Empty(options.Betas);
        Assert.Empty(options.AddDirs);
        Assert.Empty(options.Env);
        Assert.Empty(options.ExtraArgs);
        Assert.Empty(options.Plugins);
        Assert.False(options.ContinueConversation);
        Assert.False(options.IncludePartialMessages);
        Assert.False(options.ForkSession);
        Assert.False(options.EnableFileCheckpointing);
        Assert.Null(options.Tools);
        Assert.Null(options.SystemPrompt);
        Assert.Null(options.PermissionMode);
    }

    [Fact]
    public void PermissionUpdate_ToDictionary_HandlesAddRules()
    {
        var update = new PermissionUpdate(
            PermissionUpdateType.AddRules,
            Rules: new[] { new PermissionRuleValue("Bash", "rm -rf") },
            Behavior: PermissionBehavior.Deny,
            Destination: PermissionUpdateDestination.Session
        );

        var dict = update.ToDictionary();

        Assert.Equal("addRules", dict["type"]);
        Assert.Equal("session", dict["destination"]);
        Assert.NotNull(dict["rules"]);
        Assert.Equal("deny", dict["behavior"]);
    }

    [Fact]
    public void PermissionUpdate_ToDictionary_HandlesSetMode()
    {
        var update = new PermissionUpdate(
            PermissionUpdateType.SetMode,
            Mode: PermissionMode.AcceptEdits
        );

        var dict = update.ToDictionary();

        Assert.Equal("setMode", dict["type"]);
        Assert.Equal("acceptEdits", dict["mode"]);
    }

    [Fact]
    public void PermissionResultAllow_HasCorrectBehavior()
    {
        var result = new PermissionResultAllow();
        Assert.Equal("allow", result.Behavior);
    }

    [Fact]
    public void PermissionResultDeny_HasCorrectBehavior()
    {
        var result = new PermissionResultDeny("Not allowed", true);
        Assert.Equal("deny", result.Behavior);
        Assert.Equal("Not allowed", result.Message);
        Assert.True(result.Interrupt);
    }
}
