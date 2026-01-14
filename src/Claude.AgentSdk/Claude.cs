// Claude Agent SDK for .NET
// Port of claude-agent-sdk-python/query.py

using System.Runtime.CompilerServices;
using Claude.AgentSdk.Internal;
using Claude.AgentSdk.Mcp;
using Claude.AgentSdk.Transport;

namespace Claude.AgentSdk;

/// <summary>
/// Main entry point for Claude Agent SDK.
/// </summary>
public static class Claude
{
    /// <summary>
    /// Create a new options builder.
    /// </summary>
    /// <example>
    /// <code>
    /// var options = Claude.Options()
    ///     .SystemPrompt("You are a helpful assistant.")
    ///     .Model("claude-sonnet-4-20250514")
    ///     .AllowTools("Bash", "Read")
    ///     .Build();
    /// </code>
    /// </example>
    public static ClaudeAgentOptionsBuilder Options() => new();

    private static bool NeedsControlProtocol(ClaudeAgentOptions options)
    {
        if (options.CanUseTool != null)
            return true;

        if (options.Hooks != null && options.Hooks.Count > 0)
            return true;

        if (options.McpServers is Dictionary<string, object> servers)
        {
            foreach (var (_, config) in servers)
            {
                if (config is McpSdkServerConfig)
                    return true;
            }
        }

        return false;
    }

    private static async Task InitializeSdkMcpServersAsync(
        ClaudeAgentOptions options,
        QueryHandler queryHandler,
        CancellationToken cancellationToken)
    {
        if (options.McpServers is not Dictionary<string, object> servers)
            return;

        foreach (var (name, config) in servers)
        {
            if (config is McpSdkServerConfig sdkConfig)
            {
                var bridge = new SdkMcpBridge(sdkConfig.Handlers, name);
                await bridge.StartAsync(cancellationToken);
                queryHandler.RegisterSdkMcpBridge(name, bridge);
            }
        }
    }

    private static async IAsyncEnumerable<Dictionary<string, object?>> SinglePromptStream(
        string prompt,
        string sessionId = "default",
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield return new Dictionary<string, object?>
        {
            ["type"] = "user",
            ["message"] = new Dictionary<string, object?>
            {
                ["role"] = "user",
                ["content"] = prompt
            },
            ["parent_tool_use_id"] = null,
            ["session_id"] = sessionId
        };
    }

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
    /// var options = Claude.Options()
    ///     .SystemPrompt("You are an expert Python developer")
    ///     .Cwd("/home/user/project")
    ///     .Build();
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

        // Prefer the simpler --print flow when control-protocol features are not in play.
        // If hooks / can_use_tool / in-process MCP are enabled, we must be able to answer control requests.
        if (transport == null && !NeedsControlProtocol(options))
        {
            await using var printTransport = new SubprocessTransport(prompt, options);
            await printTransport.ConnectAsync(cancellationToken);

            await foreach (var json in printTransport.ReadMessagesAsync(cancellationToken))
                yield return MessageParser.Parse(json);

            yield break;
        }

        await foreach (var msg in QueryAsync(SinglePromptStream(prompt, cancellationToken: cancellationToken), options, transport, cancellationToken))
            yield return msg;
    }

    /// <summary>
    /// Query Claude Code with a streaming input prompt (unidirectional).
    /// </summary>
    /// <remarks>
    /// This overload matches the Python SDK behavior where the prompt may be a stream of user messages.
    /// </remarks>
    public static async IAsyncEnumerable<Message> QueryAsync(
        IAsyncEnumerable<Dictionary<string, object?>> prompt,
        ClaudeAgentOptions? options = null,
        ITransport? transport = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= new ClaudeAgentOptions();

        Environment.SetEnvironmentVariable("CLAUDE_CODE_ENTRYPOINT", "sdk-dotnet");

        transport ??= new SubprocessTransport(prompt, options);
        await transport.ConnectAsync(cancellationToken);

        await using var queryHandler = new QueryHandler(transport, options);
        await InitializeSdkMcpServersAsync(options, queryHandler, cancellationToken);
        await queryHandler.StartAsync(cancellationToken);
        await queryHandler.InitializeAsync(cancellationToken);

        var inputTask = queryHandler.StreamInputAsync(prompt, cancellationToken);

        try
        {
            await foreach (var message in queryHandler.ReceiveMessagesAsync(cancellationToken))
                yield return message;
        }
        finally
        {
            try { await inputTask; } catch { }
            await transport.DisposeAsync();
        }
    }
}
