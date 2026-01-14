// Claude Agent SDK for .NET
// Port of claude-agent-sdk-python/client.py

using System.Runtime.CompilerServices;
using System.Text.Json;
using Claude.AgentSdk.Internal;
using Claude.AgentSdk.Mcp;
using Claude.AgentSdk.Transport;

namespace Claude.AgentSdk;

/// <summary>
/// Client for bidirectional, interactive conversations with Claude Code.
/// </summary>
/// <remarks>
/// <para>
/// This client provides full control over the conversation flow with support
/// for streaming, interrupts, and dynamic message sending. For simple one-shot
/// queries, consider using the <see cref="Claude.QueryAsync"/> method instead.
/// </para>
///
/// <para><b>Key features:</b></para>
/// <list type="bullet">
///   <item><description><b>Bidirectional:</b> Send and receive messages at any time</description></item>
///   <item><description><b>Stateful:</b> Maintains conversation context across messages</description></item>
///   <item><description><b>Interactive:</b> Send follow-ups based on responses</description></item>
///   <item><description><b>Control flow:</b> Support for interrupts and session management</description></item>
/// </list>
///
/// <para><b>When to use ClaudeSDKClient:</b></para>
/// <list type="bullet">
///   <item><description>Building chat interfaces or conversational UIs</description></item>
///   <item><description>Interactive debugging or exploration sessions</description></item>
///   <item><description>Multi-turn conversations with context</description></item>
///   <item><description>When you need to react to Claude's responses</description></item>
///   <item><description>Real-time applications with user input</description></item>
///   <item><description>When you need interrupt capabilities</description></item>
/// </list>
///
/// <para><b>When to use QueryAsync() instead:</b></para>
/// <list type="bullet">
///   <item><description>Simple one-off questions</description></item>
///   <item><description>Batch processing of prompts</description></item>
///   <item><description>Fire-and-forget automation scripts</description></item>
///   <item><description>When all inputs are known upfront</description></item>
///   <item><description>Stateless operations</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// await using var client = new ClaudeSDKClient();
/// await client.ConnectAsync();
///
/// await client.QueryAsync("What is 2+2?");
///
/// await foreach (var message in client.ReceiveResponseAsync())
/// {
///     if (message is AssistantMessage am)
///     {
///         foreach (var block in am.Content)
///         {
///             if (block is TextBlock tb)
///                 Console.WriteLine(tb.Text);
///         }
///     }
/// }
/// </code>
/// </example>
public class ClaudeSDKClient : IAsyncDisposable
{
    private readonly ClaudeAgentOptions _options;
    private readonly ITransport? _customTransport;
    private ITransport? _transport;
    private QueryHandler? _queryHandler;

    /// <summary>
    /// Initialize Claude SDK client.
    /// </summary>
    /// <param name="options">Configuration options.</param>
    /// <param name="transport">Optional custom transport implementation.</param>
    public ClaudeSDKClient(ClaudeAgentOptions? options = null, ITransport? transport = null)
    {
        _options = options ?? new ClaudeAgentOptions();
        _customTransport = transport;

        Environment.SetEnvironmentVariable("CLAUDE_CODE_ENTRYPOINT", "sdk-dotnet-client");
    }

    /// <summary>
    /// Connect to Claude with an optional prompt or message stream.
    /// </summary>
    /// <param name="prompt">
    /// Optional initial prompt. Can be a string or null for interactive mode.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ConnectAsync(
        string? prompt = null,
        CancellationToken cancellationToken = default)
    {
        // Validate permission settings
        if (_options.CanUseTool != null)
        {
            if (prompt != null)
            {
                throw new ArgumentException(
                    "can_use_tool callback requires streaming mode. " +
                    "Please provide prompt as null for interactive mode."
                );
            }

            if (_options.PermissionPromptToolName != null)
            {
                throw new ArgumentException(
                    "can_use_tool callback cannot be used with permission_prompt_tool_name. " +
                    "Please use one or the other."
                );
            }
        }

        // Create transport
        _transport = _customTransport ?? new SubprocessTransport(
            prompt ?? (object)CreateEmptyStream(),
            _options.CanUseTool != null
                ? new ClaudeAgentOptions
                {
                    Tools = _options.Tools,
                    AllowedTools = _options.AllowedTools,
                    SystemPrompt = _options.SystemPrompt,
                    McpServers = _options.McpServers,
                    PermissionMode = _options.PermissionMode,
                    ContinueConversation = _options.ContinueConversation,
                    Resume = _options.Resume,
                    MaxTurns = _options.MaxTurns,
                    MaxBudgetUsd = _options.MaxBudgetUsd,
                    DisallowedTools = _options.DisallowedTools,
                    Model = _options.Model,
                    FallbackModel = _options.FallbackModel,
                    Betas = _options.Betas,
                    PermissionPromptToolName = "stdio", // Required for control protocol
                    Cwd = _options.Cwd,
                    CliPath = _options.CliPath,
                    Settings = _options.Settings,
                    AddDirs = _options.AddDirs,
                    Env = _options.Env,
                    ExtraArgs = _options.ExtraArgs,
                    MaxBufferSize = _options.MaxBufferSize,
                    StderrCallback = _options.StderrCallback,
                    CanUseTool = _options.CanUseTool,
                    Hooks = _options.Hooks,
                    User = _options.User,
                    IncludePartialMessages = _options.IncludePartialMessages,
                    ForkSession = _options.ForkSession,
                    Agents = _options.Agents,
                    SettingSources = _options.SettingSources,
                    Sandbox = _options.Sandbox,
                    Plugins = _options.Plugins,
                    MaxThinkingTokens = _options.MaxThinkingTokens,
                    OutputFormat = _options.OutputFormat,
                    EnableFileCheckpointing = _options.EnableFileCheckpointing
                }
                : _options
        );

        await _transport.ConnectAsync(cancellationToken);

        // Calculate initialize timeout
        var timeoutMs = int.TryParse(
            Environment.GetEnvironmentVariable("CLAUDE_CODE_STREAM_CLOSE_TIMEOUT"),
            out var ms
        ) ? ms : 60000;
        var initializeTimeout = TimeSpan.FromMilliseconds(Math.Max(timeoutMs, 60000));

        // Create query handler
        _queryHandler = new QueryHandler(_transport, _options, initializeTimeout);

        // Initialize SDK MCP servers (in-process) BEFORE starting query handler
        // This ensures bridges are ready when the CLI sends MCP messages
        await InitializeSdkMcpServersAsync(cancellationToken);

        await _queryHandler.StartAsync(cancellationToken);
        await _queryHandler.InitializeAsync(cancellationToken);
    }

    private async Task InitializeSdkMcpServersAsync(CancellationToken cancellationToken)
    {
        if (_options.McpServers == null || _queryHandler == null)
            return;

        // Check if McpServers is a dictionary
        if (_options.McpServers is not Dictionary<string, object> servers)
            return;

        foreach (var (name, config) in servers)
        {
            // Check for SDK server configurations
            if (config is McpSdkServerConfig sdkConfig)
            {
                var bridge = new SdkMcpBridge(sdkConfig.Handlers, name);
                await bridge.StartAsync(cancellationToken);
                _queryHandler.RegisterSdkMcpBridge(name, bridge);
            }
        }
    }

    private static async IAsyncEnumerable<Dictionary<string, object?>> CreateEmptyStream()
    {
        await Task.CompletedTask;
        yield break;
    }

    /// <summary>
    /// Receive all messages from Claude.
    /// </summary>
    public async IAsyncEnumerable<Message> ReceiveMessagesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_queryHandler == null)
            throw new CliConnectionException("Not connected. Call ConnectAsync() first.");

        await foreach (var message in _queryHandler.ReceiveMessagesAsync(cancellationToken))
        {
            yield return message;
        }
    }

    /// <summary>
    /// Receive messages from Claude until and including a ResultMessage.
    /// </summary>
    /// <remarks>
    /// This async iterator yields all messages in sequence and automatically terminates
    /// after yielding a ResultMessage (which indicates the response is complete).
    /// It's a convenience method over ReceiveMessagesAsync() for single-response workflows.
    /// </remarks>
    public async IAsyncEnumerable<Message> ReceiveResponseAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var message in ReceiveMessagesAsync(cancellationToken))
        {
            yield return message;
            if (message is ResultMessage)
                yield break;
        }
    }

    /// <summary>
    /// Send a new request in streaming mode.
    /// </summary>
    /// <param name="prompt">The message to send to Claude.</param>
    /// <param name="sessionId">Session identifier for the conversation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task QueryAsync(
        string prompt,
        string sessionId = "default",
        CancellationToken cancellationToken = default)
    {
        if (_queryHandler == null || _transport == null)
            throw new CliConnectionException("Not connected. Call ConnectAsync() first.");

        var message = new
        {
            type = "user",
            message = new { role = "user", content = prompt },
            parent_tool_use_id = (string?)null,
            session_id = sessionId
        };

        await _transport.WriteAsync(JsonSerializer.Serialize(message) + "\n", cancellationToken);
    }

    /// <summary>
    /// Send interrupt signal.
    /// </summary>
    public async Task InterruptAsync(CancellationToken cancellationToken = default)
    {
        if (_queryHandler == null)
            throw new CliConnectionException("Not connected. Call ConnectAsync() first.");

        await _queryHandler.InterruptAsync(cancellationToken);
    }

    /// <summary>
    /// Change permission mode during conversation.
    /// </summary>
    /// <param name="mode">
    /// The permission mode to set:
    /// <list type="bullet">
    ///   <item><description>'default': CLI prompts for dangerous tools</description></item>
    ///   <item><description>'acceptEdits': Auto-accept file edits</description></item>
    ///   <item><description>'bypassPermissions': Allow all tools (use with caution)</description></item>
    /// </list>
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SetPermissionModeAsync(string mode, CancellationToken cancellationToken = default)
    {
        if (_queryHandler == null)
            throw new CliConnectionException("Not connected. Call ConnectAsync() first.");

        await _queryHandler.SetPermissionModeAsync(mode, cancellationToken);
    }

    /// <summary>
    /// Change the AI model during conversation.
    /// </summary>
    /// <param name="model">The model to use, or null to use default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SetModelAsync(string? model = null, CancellationToken cancellationToken = default)
    {
        if (_queryHandler == null)
            throw new CliConnectionException("Not connected. Call ConnectAsync() first.");

        await _queryHandler.SetModelAsync(model, cancellationToken);
    }

    /// <summary>
    /// Rewind tracked files to their state at a specific user message.
    /// </summary>
    /// <param name="userMessageId">UUID of the user message to rewind to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Requires <see cref="ClaudeAgentOptions.EnableFileCheckpointing"/> to be true.
    /// </remarks>
    public async Task RewindFilesAsync(string userMessageId, CancellationToken cancellationToken = default)
    {
        if (_queryHandler == null)
            throw new CliConnectionException("Not connected. Call ConnectAsync() first.");

        await _queryHandler.RewindFilesAsync(userMessageId, cancellationToken);
    }

    /// <summary>
    /// Get server initialization info including available commands and output styles.
    /// </summary>
    /// <returns>Dictionary with server info, or null if not in streaming mode.</returns>
    public JsonElement? GetServerInfo()
    {
        if (_queryHandler == null)
            throw new CliConnectionException("Not connected. Call ConnectAsync() first.");

        return _queryHandler.GetInitializationResult();
    }

    /// <summary>
    /// Disconnect from Claude.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_queryHandler != null)
        {
            await _queryHandler.CloseAsync();
            _queryHandler = null;
        }
        _transport = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_queryHandler != null)
            await _queryHandler.DisposeAsync();
        if (_transport != null)
            await _transport.DisposeAsync();
    }
}
