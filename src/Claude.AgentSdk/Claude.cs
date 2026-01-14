// Claude Agent SDK for .NET
// Port of claude-agent-sdk-python/query.py

using System.Runtime.CompilerServices;
using Claude.AgentSdk.Internal;
using Claude.AgentSdk.Transport;

namespace Claude.AgentSdk;

/// <summary>
/// Main entry point for Claude Agent SDK.
/// </summary>
public static class Claude
{
    /// <summary>
    /// Query Claude Code for one-shot or unidirectional streaming interactions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This function is ideal for simple, stateless queries where you don't need
    /// bidirectional communication or conversation management. For interactive,
    /// stateful conversations, use <see cref="ClaudeSDKClient"/> instead.
    /// </para>
    ///
    /// <para><b>Key differences from ClaudeSDKClient:</b></para>
    /// <list type="bullet">
    ///   <item><description><b>Unidirectional:</b> Send all messages upfront, receive all responses</description></item>
    ///   <item><description><b>Stateless:</b> Each query is independent, no conversation state</description></item>
    ///   <item><description><b>Simple:</b> Fire-and-forget style, no connection management</description></item>
    ///   <item><description><b>No interrupts:</b> Cannot interrupt or send follow-up messages</description></item>
    /// </list>
    ///
    /// <para><b>When to use QueryAsync():</b></para>
    /// <list type="bullet">
    ///   <item><description>Simple one-off questions ("What is 2+2?")</description></item>
    ///   <item><description>Batch processing of independent prompts</description></item>
    ///   <item><description>Code generation or analysis tasks</description></item>
    ///   <item><description>Automated scripts and CI/CD pipelines</description></item>
    ///   <item><description>When you know all inputs upfront</description></item>
    /// </list>
    ///
    /// <para><b>When to use ClaudeSDKClient:</b></para>
    /// <list type="bullet">
    ///   <item><description>Interactive conversations with follow-ups</description></item>
    ///   <item><description>Chat applications or REPL-like interfaces</description></item>
    ///   <item><description>When you need to send messages based on responses</description></item>
    ///   <item><description>When you need interrupt capabilities</description></item>
    ///   <item><description>Long-running sessions with state</description></item>
    /// </list>
    /// </remarks>
    /// <param name="prompt">The prompt to send to Claude.</param>
    /// <param name="options">Optional configuration (defaults to <see cref="ClaudeAgentOptions"/> if null).</param>
    /// <param name="transport">Optional custom transport implementation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of messages from the conversation.</returns>
    /// <example>
    /// <code>
    /// // Simple query
    /// await foreach (var message in Claude.QueryAsync("What is the capital of France?"))
    /// {
    ///     Console.WriteLine(message);
    /// }
    ///
    /// // With options
    /// var options = new ClaudeAgentOptions
    /// {
    ///     SystemPrompt = "You are an expert Python developer",
    ///     Cwd = "/home/user/project"
    /// };
    /// await foreach (var message in Claude.QueryAsync("Create a Python web server", options))
    /// {
    ///     Console.WriteLine(message);
    /// }
    /// </code>
    /// </example>
    public static async IAsyncEnumerable<Message> QueryAsync(
        string prompt,
        ClaudeAgentOptions? options = null,
        ITransport? transport = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= new ClaudeAgentOptions();

        Environment.SetEnvironmentVariable("CLAUDE_CODE_ENTRYPOINT", "sdk-dotnet");

        transport ??= new SubprocessTransport(prompt, options);

        await transport.ConnectAsync(cancellationToken);

        try
        {
            await foreach (var json in transport.ReadMessagesAsync(cancellationToken))
            {
                yield return MessageParser.Parse(json);
            }
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }
}
