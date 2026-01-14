using Xunit;

namespace Claude.AgentSdk.Tests;

public class ErrorsTests
{
    [Fact]
    public void ClaudeSDKException_HasCorrectMessage()
    {
        var ex = new ClaudeSDKException("Test error");
        Assert.Equal("Test error", ex.Message);
    }

    [Fact]
    public void ClaudeSDKException_PreservesInnerException()
    {
        var inner = new InvalidOperationException("Inner error");
        var ex = new ClaudeSDKException("Outer error", inner);

        Assert.Equal("Outer error", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void CliNotFoundException_IncludesPath()
    {
        var ex = new CliNotFoundException("CLI not found", "/usr/bin/claude");

        Assert.Contains("/usr/bin/claude", ex.Message);
        Assert.Equal("/usr/bin/claude", ex.CliPath);
    }

    [Fact]
    public void CliNotFoundException_WorksWithoutPath()
    {
        var ex = new CliNotFoundException("CLI not found");

        Assert.Equal("CLI not found", ex.Message);
        Assert.Null(ex.CliPath);
    }

    [Fact]
    public void ProcessException_IncludesExitCode()
    {
        var ex = new ProcessException("Process failed", exitCode: 1);

        Assert.Contains("exit code: 1", ex.Message);
        Assert.Equal(1, ex.ExitCode);
    }

    [Fact]
    public void ProcessException_IncludesStderr()
    {
        var ex = new ProcessException("Process failed", stderr: "Error output");

        Assert.Contains("Error output", ex.Message);
        Assert.Equal("Error output", ex.Stderr);
    }

    [Fact]
    public void ProcessException_IncludesBoth()
    {
        var ex = new ProcessException("Process failed", exitCode: 2, stderr: "Error details");

        Assert.Contains("exit code: 2", ex.Message);
        Assert.Contains("Error details", ex.Message);
        Assert.Equal(2, ex.ExitCode);
        Assert.Equal("Error details", ex.Stderr);
    }

    [Fact]
    public void JsonDecodeException_TruncatesLongLines()
    {
        var longLine = new string('x', 200);
        var inner = new Exception("Parse error");
        var ex = new JsonDecodeException(longLine, inner);

        Assert.True(ex.Message.Length < longLine.Length + 50);
        Assert.Contains("...", ex.Message);
        Assert.Equal(longLine, ex.Line);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void MessageParseException_IncludesRawData()
    {
        var data = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{}");
        var ex = new MessageParseException("Parse failed", data);

        Assert.Equal("Parse failed", ex.Message);
        Assert.NotNull(ex.RawData);
    }

    [Fact]
    public void ExceptionHierarchy_IsCorrect()
    {
        Assert.True(typeof(CliConnectionException).IsSubclassOf(typeof(ClaudeSDKException)));
        Assert.True(typeof(CliNotFoundException).IsSubclassOf(typeof(CliConnectionException)));
        Assert.True(typeof(ProcessException).IsSubclassOf(typeof(ClaudeSDKException)));
        Assert.True(typeof(JsonDecodeException).IsSubclassOf(typeof(ClaudeSDKException)));
        Assert.True(typeof(MessageParseException).IsSubclassOf(typeof(ClaudeSDKException)));
    }
}
