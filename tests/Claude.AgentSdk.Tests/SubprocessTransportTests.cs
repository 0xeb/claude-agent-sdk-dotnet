// Port of claude-agent-sdk-python/tests/test_transport.py
// Verifies the CLI flags emitted by SubprocessTransport.BuildCommand()
// for system prompt configurations.

using Claude.AgentSdk.Transport;
using Xunit;

namespace Claude.AgentSdk.Tests;

public sealed class SubprocessTransportTests
{
    private static ClaudeAgentOptionsBuilder OptionsWithDummyCli() =>
        Claude.Options().CliPath("dummy-claude");

    private static SubprocessTransport MakeTransport(ClaudeAgentOptions options) =>
        new(prompt: "test", options: options);

    [Fact]
    public void BuildCommand_WithStringSystemPrompt_EmitsSystemPromptFlag()
    {
        var options = OptionsWithDummyCli().SystemPrompt("You are helpful.").Build();
        var cmd = MakeTransport(options).BuildCommand();

        Assert.Contains("--system-prompt", cmd);
        Assert.Contains("You are helpful.", cmd);
        Assert.DoesNotContain("--append-system-prompt", cmd);
    }

    [Fact]
    public void BuildCommand_WithAppendSystemPrompt_EmitsAppendFlagOnly()
    {
        var options = OptionsWithDummyCli().AppendSystemPrompt("Be concise.").Build();
        var cmd = MakeTransport(options).BuildCommand();

        Assert.Contains("--append-system-prompt", cmd);
        Assert.Contains("Be concise.", cmd);
        Assert.DoesNotContain("--system-prompt", cmd);
    }

    [Fact]
    public void BuildCommand_WithEmptyAppendSystemPrompt_StillEmitsAppendFlag()
    {
        // Python parity: `"append" in dict` emits the flag regardless of value.
        var options = OptionsWithDummyCli().AppendSystemPrompt("").Build();
        var cmd = MakeTransport(options).BuildCommand();

        Assert.Contains("--append-system-prompt", cmd);
        Assert.DoesNotContain("--system-prompt", cmd);
    }

    [Fact]
    public void BuildCommand_WithPresetNoAppend_EmitsNoSystemPromptFlags()
    {
        var options = OptionsWithDummyCli()
            .SystemPrompt(SystemPromptPreset.ClaudeCode())
            .Build();
        var cmd = MakeTransport(options).BuildCommand();

        Assert.DoesNotContain("--system-prompt", cmd);
        Assert.DoesNotContain("--append-system-prompt", cmd);
    }

    [Fact]
    public void BuildCommand_WithNullSystemPrompt_EmitsEmptySystemPromptFlag()
    {
        var options = OptionsWithDummyCli().Build();
        var cmd = MakeTransport(options).BuildCommand();

        var idx = cmd.IndexOf("--system-prompt");
        Assert.NotEqual(-1, idx);
        Assert.Equal("", cmd[idx + 1]);
        Assert.DoesNotContain("--append-system-prompt", cmd);
    }
}
