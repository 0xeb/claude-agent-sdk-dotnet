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

    #region ThinkingConfig Tests

    [Fact]
    public void ThinkingConfigAdaptive_HasCorrectType()
    {
        IThinkingConfig config = new ThinkingConfigAdaptive();
        Assert.Equal("adaptive", config.Type);
    }

    [Fact]
    public void ThinkingConfigEnabled_HasCorrectTypeAndBudget()
    {
        var config = new ThinkingConfigEnabled(16000);
        Assert.Equal("enabled", config.Type);
        Assert.Equal(16000, config.BudgetTokens);
    }

    [Fact]
    public void ThinkingConfigDisabled_HasCorrectType()
    {
        IThinkingConfig config = new ThinkingConfigDisabled();
        Assert.Equal("disabled", config.Type);
    }

    #endregion

    #region EffortLevel Tests

    [Theory]
    [InlineData(EffortLevel.Low, "low")]
    [InlineData(EffortLevel.Medium, "medium")]
    [InlineData(EffortLevel.High, "high")]
    [InlineData(EffortLevel.Max, "max")]
    public void EffortLevel_ToJsonString_ReturnsCorrectValue(EffortLevel effort, string expected)
    {
        Assert.Equal(expected, effort.ToJsonString());
    }

    #endregion

    #region New HookEvent Tests

    [Fact]
    public void HookEvent_HasAllExpectedValues()
    {
        var values = Enum.GetValues<HookEvent>();
        Assert.Equal(10, values.Length);

        Assert.Contains(HookEvent.PostToolUseFailure, values);
        Assert.Contains(HookEvent.Notification, values);
        Assert.Contains(HookEvent.SubagentStart, values);
        Assert.Contains(HookEvent.PermissionRequest, values);
    }

    [Theory]
    [InlineData(HookEvent.PostToolUseFailure, "PostToolUseFailure")]
    [InlineData(HookEvent.Notification, "Notification")]
    [InlineData(HookEvent.SubagentStart, "SubagentStart")]
    [InlineData(HookEvent.PermissionRequest, "PermissionRequest")]
    public void HookEvent_ToJsonString_ReturnsCorrectValue(HookEvent hookEvent, string expected)
    {
        Assert.Equal(expected, hookEvent.ToJsonString());
    }

    #endregion

    #region HookOutput Async Fields

    [Fact]
    public void HookOutput_AsyncFields_DefaultToNull()
    {
        var output = new HookOutput();
        Assert.Null(output.Async);
        Assert.Null(output.AsyncTimeout);
    }

    [Fact]
    public void HookOutput_AsyncFields_CanBeSet()
    {
        var output = new HookOutput
        {
            Async = true,
            AsyncTimeout = 5000
        };

        Assert.True(output.Async);
        Assert.Equal(5000, output.AsyncTimeout);
    }

    [Fact]
    public void HookOutput_AsyncFields_SerializeCorrectly()
    {
        var output = new HookOutput
        {
            Async = true,
            AsyncTimeout = 3000
        };

        var json = JsonSerializer.Serialize(output);
        Assert.Contains("\"async\":true", json);
        Assert.Contains("\"asyncTimeout\":3000", json);
    }

    #endregion

    #region ClaudeAgentOptions Thinking/Effort Tests

#pragma warning disable CS0618 // Testing obsolete MaxThinkingTokens
    [Fact]
    public void ClaudeAgentOptions_ThinkingAndEffort_DefaultToNull()
    {
        var options = new ClaudeAgentOptions();
        Assert.Null(options.Thinking);
        Assert.Null(options.Effort);
        Assert.Null(options.MaxThinkingTokens);
    }

    [Fact]
    public void ClaudeAgentOptions_CanSetThinkingConfig()
    {
        var options = new ClaudeAgentOptions
        {
            Thinking = new ThinkingConfigAdaptive()
        };
        Assert.IsType<ThinkingConfigAdaptive>(options.Thinking);
    }

    [Fact]
    public void ClaudeAgentOptions_CanSetEffort()
    {
        var options = new ClaudeAgentOptions
        {
            Effort = EffortLevel.High
        };
        Assert.Equal(EffortLevel.High, options.Effort);
    }
#pragma warning restore CS0618

    #endregion
}
