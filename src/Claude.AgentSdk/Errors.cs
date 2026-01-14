// Claude Agent SDK for .NET
// Port of claude-agent-sdk-python/_errors.py

using System.Text.Json;

namespace Claude.AgentSdk;

/// <summary>
/// Base exception for all Claude SDK errors.
/// </summary>
public class ClaudeSDKException : Exception
{
    public ClaudeSDKException(string message) : base(message) { }
    public ClaudeSDKException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Raised when unable to connect to Claude Code CLI.
/// </summary>
public class CliConnectionException : ClaudeSDKException
{
    public CliConnectionException(string message) : base(message) { }
    public CliConnectionException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Raised when Claude Code CLI is not found or not installed.
/// </summary>
public class CliNotFoundException : CliConnectionException
{
    public string? CliPath { get; }

    public CliNotFoundException(string message, string? cliPath = null)
        : base(cliPath != null ? $"{message}: {cliPath}" : message)
    {
        CliPath = cliPath;
    }
}

/// <summary>
/// Raised when the CLI process fails.
/// </summary>
public class ProcessException : ClaudeSDKException
{
    public int? ExitCode { get; }
    public string? Stderr { get; }

    public ProcessException(string message, int? exitCode = null, string? stderr = null)
        : base(FormatMessage(message, exitCode, stderr))
    {
        ExitCode = exitCode;
        Stderr = stderr;
    }

    private static string FormatMessage(string message, int? exitCode, string? stderr)
    {
        if (exitCode.HasValue)
            message = $"{message} (exit code: {exitCode})";
        if (!string.IsNullOrEmpty(stderr))
            message = $"{message}\nError output: {stderr}";
        return message;
    }
}

/// <summary>
/// Raised when unable to decode JSON from CLI output.
/// </summary>
public class JsonDecodeException : ClaudeSDKException
{
    public string Line { get; }

    public JsonDecodeException(string line, Exception innerException)
        : base($"Failed to decode JSON: {line[..Math.Min(100, line.Length)]}...", innerException)
    {
        Line = line;
    }
}

/// <summary>
/// Raised when unable to parse a message from CLI output.
/// </summary>
public class MessageParseException : ClaudeSDKException
{
    public JsonElement? RawData { get; }

    public MessageParseException(string message, JsonElement? rawData = null)
        : base(message)
    {
        RawData = rawData;
    }
}
