using System.Text.Json;
using Claude.AgentSdk.Builders;
using Claude.AgentSdk.Mcp;

namespace Claude.AgentSdk;

/// <summary>
/// Fluent builder for creating <see cref="ClaudeAgentOptions"/>.
/// </summary>
/// <example>
/// <code>
/// var options = Claude.Options()
///     .SystemPrompt("You are a helpful assistant.")
///     .Model("claude-sonnet-4-20250514")
///     .MaxTurns(10)
///     .AllowTools("Bash", "Read", "Write")
///     .Hooks(h => h.PreToolUse("Bash", CheckCommand))
///     .Build();
/// </code>
/// </example>
public sealed class ClaudeAgentOptionsBuilder
{
    private IReadOnlyList<string>? _tools;
    private readonly List<string> _allowedTools = [];
    private readonly List<string> _disallowedTools = [];
    private string? _systemPrompt;
    private object? _mcpServers;
    private PermissionMode? _permissionMode;
    private bool _continueConversation;
    private string? _resume;
    private int? _maxTurns;
    private decimal? _maxBudgetUsd;
    private string? _model;
    private string? _fallbackModel;
    private readonly List<string> _betas = [];
    private string? _permissionPromptToolName;
    private string? _cwd;
    private string? _cliPath;
    private string? _settings;
    private readonly List<string> _addDirs = [];
    private readonly Dictionary<string, string> _env = [];
    private readonly Dictionary<string, string?> _extraArgs = [];
    private int? _maxBufferSize;
    private Action<string>? _stderrCallback;
    private CanUseToolCallback? _canUseTool;
    private IReadOnlyDictionary<HookEvent, IReadOnlyList<HookMatcher>>? _hooks;
    private string? _user;
    private bool _includePartialMessages;
    private bool _forkSession;
    private IReadOnlyDictionary<string, AgentDefinition>? _agents;
    private IReadOnlyList<SettingSource>? _settingSources;
    private SandboxSettings? _sandbox;
    private readonly List<SdkPluginConfig> _plugins = [];
    private int? _maxThinkingTokens;
    private JsonElement? _outputFormat;
    private bool _enableFileCheckpointing;

    /// <summary>Set the system prompt.</summary>
    public ClaudeAgentOptionsBuilder SystemPrompt(string prompt)
    {
        _systemPrompt = prompt;
        return this;
    }

    /// <summary>Set the model to use.</summary>
    public ClaudeAgentOptionsBuilder Model(string model)
    {
        _model = model;
        return this;
    }

    /// <summary>Set the fallback model.</summary>
    public ClaudeAgentOptionsBuilder FallbackModel(string model)
    {
        _fallbackModel = model;
        return this;
    }

    /// <summary>Set maximum number of turns.</summary>
    public ClaudeAgentOptionsBuilder MaxTurns(int turns)
    {
        _maxTurns = turns;
        return this;
    }

    /// <summary>Set maximum budget in USD.</summary>
    public ClaudeAgentOptionsBuilder MaxBudget(decimal usd)
    {
        _maxBudgetUsd = usd;
        return this;
    }

    /// <summary>Set the base set of tools to enable.</summary>
    public ClaudeAgentOptionsBuilder Tools(params string[] tools)
    {
        _tools = tools.ToList();
        return this;
    }

    /// <summary>Add tools to the allowed list.</summary>
    public ClaudeAgentOptionsBuilder AllowTools(params string[] tools)
    {
        _allowedTools.AddRange(tools);
        return this;
    }

    /// <summary>Add tools to the disallowed list.</summary>
    public ClaudeAgentOptionsBuilder DisallowTools(params string[] tools)
    {
        _disallowedTools.AddRange(tools);
        return this;
    }

    /// <summary>Set the working directory.</summary>
    public ClaudeAgentOptionsBuilder Cwd(string path)
    {
        _cwd = path;
        return this;
    }

    /// <summary>Set the CLI path.</summary>
    public ClaudeAgentOptionsBuilder CliPath(string path)
    {
        _cliPath = path;
        return this;
    }

    /// <summary>Set the settings path or JSON.</summary>
    public ClaudeAgentOptionsBuilder Settings(string settings)
    {
        _settings = settings;
        return this;
    }

    /// <summary>Add directories to include.</summary>
    public ClaudeAgentOptionsBuilder AddDirs(params string[] dirs)
    {
        _addDirs.AddRange(dirs);
        return this;
    }

    /// <summary>Set an environment variable.</summary>
    public ClaudeAgentOptionsBuilder Env(string key, string value)
    {
        _env[key] = value;
        return this;
    }

    /// <summary>Set multiple environment variables.</summary>
    public ClaudeAgentOptionsBuilder Env(IEnumerable<KeyValuePair<string, string>> variables)
    {
        foreach (var (key, value) in variables)
            _env[key] = value;
        return this;
    }

    /// <summary>Add an extra CLI argument.</summary>
    public ClaudeAgentOptionsBuilder ExtraArg(string key, string? value = null)
    {
        _extraArgs[key] = value;
        return this;
    }

    /// <summary>Set the permission mode.</summary>
    public ClaudeAgentOptionsBuilder PermissionMode(PermissionMode mode)
    {
        _permissionMode = mode;
        return this;
    }

    /// <summary>Enable accept-edits permission mode.</summary>
    public ClaudeAgentOptionsBuilder AcceptEdits()
    {
        _permissionMode = AgentSdk.PermissionMode.AcceptEdits;
        return this;
    }

    /// <summary>Enable bypass-permissions mode (dangerous).</summary>
    public ClaudeAgentOptionsBuilder BypassPermissions()
    {
        _permissionMode = AgentSdk.PermissionMode.BypassPermissions;
        return this;
    }

    /// <summary>Continue from previous conversation.</summary>
    public ClaudeAgentOptionsBuilder ContinueConversation(bool value = true)
    {
        _continueConversation = value;
        return this;
    }

    /// <summary>Resume a session by ID.</summary>
    public ClaudeAgentOptionsBuilder Resume(string sessionId)
    {
        _resume = sessionId;
        return this;
    }

    /// <summary>Enable beta features.</summary>
    public ClaudeAgentOptionsBuilder Betas(params string[] betas)
    {
        _betas.AddRange(betas);
        return this;
    }

    /// <summary>Set the permission prompt tool name.</summary>
    public ClaudeAgentOptionsBuilder PermissionPromptToolName(string name)
    {
        _permissionPromptToolName = name;
        return this;
    }

    /// <summary>Set the maximum buffer size.</summary>
    public ClaudeAgentOptionsBuilder MaxBufferSize(int size)
    {
        _maxBufferSize = size;
        return this;
    }

    /// <summary>Set the stderr callback.</summary>
    public ClaudeAgentOptionsBuilder OnStderr(Action<string> callback)
    {
        _stderrCallback = callback;
        return this;
    }

    /// <summary>Set the user identifier.</summary>
    public ClaudeAgentOptionsBuilder User(string user)
    {
        _user = user;
        return this;
    }

    /// <summary>Include partial messages during streaming.</summary>
    public ClaudeAgentOptionsBuilder IncludePartialMessages(bool value = true)
    {
        _includePartialMessages = value;
        return this;
    }

    /// <summary>Fork session when resuming.</summary>
    public ClaudeAgentOptionsBuilder ForkSession(bool value = true)
    {
        _forkSession = value;
        return this;
    }

    /// <summary>Set the setting sources to load.</summary>
    public ClaudeAgentOptionsBuilder SettingSources(params SettingSource[] sources)
    {
        _settingSources = sources.ToList();
        return this;
    }

    /// <summary>Add a plugin configuration.</summary>
    public ClaudeAgentOptionsBuilder Plugin(string type, string path)
    {
        _plugins.Add(new SdkPluginConfig(type, path));
        return this;
    }

    /// <summary>Set maximum thinking tokens.</summary>
    public ClaudeAgentOptionsBuilder MaxThinkingTokens(int tokens)
    {
        _maxThinkingTokens = tokens;
        return this;
    }

    /// <summary>Set the output format for structured outputs.</summary>
    public ClaudeAgentOptionsBuilder OutputFormat(JsonElement format)
    {
        _outputFormat = format;
        return this;
    }

    /// <summary>Enable file checkpointing.</summary>
    public ClaudeAgentOptionsBuilder EnableFileCheckpointing(bool value = true)
    {
        _enableFileCheckpointing = value;
        return this;
    }

    /// <summary>Set the tool permission callback.</summary>
    public ClaudeAgentOptionsBuilder CanUseTool(CanUseToolCallback callback)
    {
        _canUseTool = callback;
        return this;
    }

    /// <summary>Allow all tool calls without prompting.</summary>
    public ClaudeAgentOptionsBuilder AllowAllTools()
    {
        _canUseTool = (_, _, _, _) => Task.FromResult<PermissionResult>(new PermissionResultAllow());
        return this;
    }

    /// <summary>Configure hooks using a builder.</summary>
    public ClaudeAgentOptionsBuilder Hooks(Action<HooksBuilder> configure)
    {
        var builder = new HooksBuilder();
        configure(builder);
        _hooks = builder.Build();
        return this;
    }

    /// <summary>Configure agents using a builder.</summary>
    public ClaudeAgentOptionsBuilder Agents(Action<AgentsBuilder> configure)
    {
        var builder = new AgentsBuilder();
        configure(builder);
        _agents = builder.Build();
        return this;
    }

    /// <summary>Configure MCP servers using a builder.</summary>
    public ClaudeAgentOptionsBuilder McpServers(Action<McpServerRegistry> configure)
    {
        var registry = new McpServerRegistry();
        configure(registry);
        _mcpServers = registry;
        return this;
    }

    /// <summary>Set MCP servers directly.</summary>
    public ClaudeAgentOptionsBuilder McpServers(object servers)
    {
        _mcpServers = servers;
        return this;
    }

    /// <summary>Configure sandbox settings using a builder.</summary>
    public ClaudeAgentOptionsBuilder Sandbox(Action<SandboxBuilder> configure)
    {
        var builder = new SandboxBuilder();
        configure(builder);
        _sandbox = builder.Build();
        return this;
    }

    /// <summary>Build the <see cref="ClaudeAgentOptions"/> instance.</summary>
    public ClaudeAgentOptions Build()
    {
        return new ClaudeAgentOptions
        {
            Tools = _tools,
            AllowedTools = _allowedTools.Count > 0 ? _allowedTools : [],
            DisallowedTools = _disallowedTools.Count > 0 ? _disallowedTools : [],
            SystemPrompt = _systemPrompt,
            McpServers = _mcpServers,
            PermissionMode = _permissionMode,
            ContinueConversation = _continueConversation,
            Resume = _resume,
            MaxTurns = _maxTurns,
            MaxBudgetUsd = _maxBudgetUsd,
            Model = _model,
            FallbackModel = _fallbackModel,
            Betas = _betas.Count > 0 ? _betas : [],
            PermissionPromptToolName = _permissionPromptToolName,
            Cwd = _cwd,
            CliPath = _cliPath,
            Settings = _settings,
            AddDirs = _addDirs.Count > 0 ? _addDirs : [],
            Env = _env.Count > 0 ? _env : new Dictionary<string, string>(),
            ExtraArgs = _extraArgs.Count > 0 ? _extraArgs : new Dictionary<string, string?>(),
            MaxBufferSize = _maxBufferSize,
            StderrCallback = _stderrCallback,
            CanUseTool = _canUseTool,
            Hooks = _hooks,
            User = _user,
            IncludePartialMessages = _includePartialMessages,
            ForkSession = _forkSession,
            Agents = _agents,
            SettingSources = _settingSources,
            Sandbox = _sandbox,
            Plugins = _plugins.Count > 0 ? _plugins : [],
            MaxThinkingTokens = _maxThinkingTokens,
            OutputFormat = _outputFormat,
            EnableFileCheckpointing = _enableFileCheckpointing
        };
    }
}
