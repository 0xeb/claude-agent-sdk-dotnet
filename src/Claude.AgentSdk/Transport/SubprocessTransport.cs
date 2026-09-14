// Claude Agent SDK for .NET
// Port of claude-agent-sdk-python/_internal/transport/subprocess_cli.py

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Claude.AgentSdk.Transport;

/// <summary>
/// Subprocess transport implementation using Claude Code CLI.
/// </summary>
public class SubprocessTransport : ITransport
{
    private const int DefaultMaxBufferSize = 1024 * 1024; // 1MB
    private const string MinimumClaudeCodeVersion = "2.0.0";
    private static readonly int CmdLengthLimit = OperatingSystem.IsWindows() ? 8000 : 100000;

    // Python commit f2389ec: track live subprocesses so we can terminate them
    // when the parent process exits. Mirrors Python atexit cleanup.
    private static readonly ConcurrentDictionary<int, Process> _activeChildren = new();

    // Python commit 6384c69: dedupe the unsupported-CLI-version warning per process.
    private static int _versionWarningEmitted; // Interlocked flag

    static SubprocessTransport()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillActiveChildren();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => KillActiveChildren();
    }

    private static void KillActiveChildren()
    {
        foreach (var (pid, proc) in _activeChildren)
        {
            try
            {
                if (!proc.HasExited) proc.Kill(entireProcessTree: true);
            }
            catch { }
            _activeChildren.TryRemove(pid, out _);
        }
    }

    private readonly object _prompt;
    private readonly bool _isStreaming;
    private readonly ClaudeAgentOptions _options;
    private string? _cliPath; // Python commit 19e1f53: deferred CLI discovery to ConnectAsync.
    private readonly string? _cwd;
    private readonly int _maxBufferSize;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly List<string> _tempFiles = [];

    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private StreamReader? _stderr;
    private Task? _stderrTask;
    private bool _ready;
    private Exception? _exitError;

    public bool IsReady => _ready;

    /// <summary>
    /// Create a new subprocess transport.
    /// </summary>
    /// <param name="prompt">The prompt (string for one-shot, or IAsyncEnumerable for streaming).</param>
    /// <param name="options">Configuration options.</param>
    public SubprocessTransport(object prompt, ClaudeAgentOptions options)
    {
        _prompt = prompt;
        _isStreaming = prompt is not string;
        _options = options;
        // Python commit 19e1f53: defer CLI discovery to ConnectAsync so tests
        // and dry-run builders can construct without an installed CLI.
        _cliPath = options.CliPath;
        _cwd = options.Cwd;
        _maxBufferSize = options.MaxBufferSize ?? DefaultMaxBufferSize;
    }

    private static string FindCli()
    {
        // Check bundled CLI first
        var bundledCli = FindBundledCli();
        if (bundledCli != null)
            return bundledCli;

        // Check environment variable
        var cliPathEnv = Environment.GetEnvironmentVariable("CLAUDE_CLI_PATH");
        if (!string.IsNullOrEmpty(cliPathEnv) && File.Exists(cliPathEnv))
            return cliPathEnv;

        // Check PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var pathDirs = pathEnv.Split(Path.PathSeparator);

        // On Windows, npm installs claude.cmd (batch wrapper), not claude.exe
        var cliNames = OperatingSystem.IsWindows()
            ? new[] { "claude.cmd", "claude.exe", "claude" }
            : new[] { "claude" };

        foreach (var dir in pathDirs)
        {
            foreach (var cliName in cliNames)
            {
                var fullPath = Path.Combine(dir, cliName);
                if (File.Exists(fullPath))
                    return fullPath;
            }
        }

        // Check common locations
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var locations = OperatingSystem.IsWindows()
            ? new[]
            {
                // Windows native installation locations
                Path.Combine(home, ".local", "bin", "claude.exe"),
                Path.Combine(localAppData, "Claude", "claude.exe"),
                // Windows npm global locations
                Path.Combine(appData, "npm", "claude.cmd"),
                Path.Combine(appData, "npm", "claude.exe"),
                Path.Combine(home, ".npm-global", "bin", "claude.cmd"),
                Path.Combine(home, ".claude", "local", "claude.exe"),
                Path.Combine(home, "node_modules", ".bin", "claude.cmd"),
            }
            : new[]
            {
                Path.Combine(home, ".npm-global", "bin", "claude"),
                Path.Combine(home, ".local", "bin", "claude"),
                Path.Combine(home, "node_modules", ".bin", "claude"),
                Path.Combine(home, ".yarn", "bin", "claude"),
                Path.Combine(home, ".claude", "local", "claude"),
                "/usr/local/bin/claude"
            };

        foreach (var path in locations)
        {
            if (File.Exists(path))
                return path;
        }

        throw new CliNotFoundException(
            "Claude Code not found. Install with:\n" +
            "  npm install -g @anthropic-ai/claude-code\n" +
            "\nOr provide the path via ClaudeAgentOptions:\n" +
            "  new ClaudeAgentOptions { CliPath = \"/path/to/claude\" }"
        );
    }

    private static string? FindBundledCli()
    {
        var cliName = OperatingSystem.IsWindows() ? "claude.exe" : "claude";
        var assemblyDir = AppContext.BaseDirectory;
        if (string.IsNullOrWhiteSpace(assemblyDir)) return null;

        var bundledPath = Path.Combine(assemblyDir, "_bundled", cliName);
        return File.Exists(bundledPath) ? bundledPath : null;
    }

    /// <summary>
    /// Python commit 1c26bd3 + e621929: compute effective allowed_tools and
    /// setting_sources from Skills option.
    /// </summary>
    /// <remarks>
    /// When Skills == "all" → inject bare "Skill" tool. When Skills is a list
    /// → inject Skill(name) for each entry. setting_sources defaults to
    /// ["user","project"] when unset so CLI discovers installed skills.
    /// Skills==null is a no-op.
    /// </remarks>
    internal (List<string> AllowedTools, List<string>? SettingSources) ApplySkillsDefaults()
    {
        var allowedTools = new List<string>(_options.AllowedTools);
        List<string>? settingSources = _options.SettingSources != null
            ? _options.SettingSources.Select(s => s.ToString().ToLowerInvariant()).ToList()
            : null;

        if (_options.Skills == null)
            return (allowedTools, settingSources);

        if (_options.Skills is string s && s == "all")
        {
            if (!allowedTools.Contains("Skill"))
                allowedTools.Add("Skill");
        }
        else if (_options.Skills is IEnumerable<string> names)
        {
            foreach (var name in names)
            {
                var pattern = $"Skill({name})";
                if (!allowedTools.Contains(pattern))
                    allowedTools.Add(pattern);
            }
        }

        settingSources ??= new List<string> { "user", "project" };
        return (allowedTools, settingSources);
    }

    private static string PermissionModeToCliValue(PermissionMode mode) => mode switch
    {
        PermissionMode.Default => "default",
        PermissionMode.AcceptEdits => "acceptEdits",
        PermissionMode.Plan => "plan",
        PermissionMode.BypassPermissions => "bypassPermissions",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported permission mode")
    };

    private string? BuildSettingsValue()
    {
        var hasSettings = _options.Settings != null;
        var hasSandbox = _options.Sandbox != null;

        if (!hasSettings && !hasSandbox)
            return null;

        if (hasSettings && !hasSandbox)
            return _options.Settings;

        // Merge settings with sandbox
        var settingsObj = new Dictionary<string, object?>();

        if (hasSettings)
        {
            var settingsStr = _options.Settings!.Trim();
            if (settingsStr.StartsWith('{') && settingsStr.EndsWith('}'))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(settingsStr);
                    if (parsed != null)
                        settingsObj = parsed;
                }
                catch (JsonException)
                {
                    // Try as file path
                    if (File.Exists(settingsStr))
                    {
                        var content = File.ReadAllText(settingsStr);
                        var parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(content);
                        if (parsed != null)
                            settingsObj = parsed;
                    }
                }
            }
            else if (File.Exists(settingsStr))
            {
                var content = File.ReadAllText(settingsStr);
                var parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(content);
                if (parsed != null)
                    settingsObj = parsed;
            }
        }

        if (hasSandbox)
            settingsObj["sandbox"] = _options.Sandbox;

        return JsonSerializer.Serialize(settingsObj);
    }

    internal List<string> BuildCommand()
    {
        _cliPath ??= FindCli();
        var cmd = new List<string> { _cliPath, "--output-format", "stream-json", "--verbose" };

        // System prompt
        if (_options.SystemPrompt == null)
        {
            cmd.AddRange(["--system-prompt", ""]);
        }
        else if (_options.SystemPrompt is SystemPromptPreset preset)
        {
            if (preset.Append is not null)
            {
                cmd.AddRange(["--append-system-prompt", preset.Append]);
            }
        }
        else if (_options.SystemPrompt is SystemPromptFile spFile)
        {
            // Python commit reference: SystemPromptFile branch in subprocess_cli.py
            cmd.AddRange(["--system-prompt-file", spFile.Path]);
        }
        else
        {
            cmd.AddRange(["--system-prompt", _options.SystemPrompt.ToString() ?? ""]);
        }

        // Tools
        if (_options.Tools != null)
        {
            if (_options.Tools.Count == 0)
                cmd.AddRange(["--tools", ""]);
            else
                cmd.AddRange(["--tools", string.Join(",", _options.Tools)]);
        }

        if (_options.AllowedTools.Count > 0)
            cmd.AddRange(["--allowedTools", string.Join(",", _options.AllowedTools)]);

        if (_options.MaxTurns.HasValue)
            cmd.AddRange(["--max-turns", _options.MaxTurns.Value.ToString()]);

        if (_options.MaxBudgetUsd.HasValue)
            cmd.AddRange(["--max-budget-usd", _options.MaxBudgetUsd.Value.ToString()]);

        if (_options.DisallowedTools.Count > 0)
            cmd.AddRange(["--disallowedTools", string.Join(",", _options.DisallowedTools)]);

        // Python commit 2e60cec: --task-budget <total>.
        if (_options.TaskBudget != null)
            cmd.AddRange(["--task-budget", _options.TaskBudget.Total.ToString()]);

        if (_options.Model != null)
            cmd.AddRange(["--model", _options.Model]);

        if (_options.FallbackModel != null)
            cmd.AddRange(["--fallback-model", _options.FallbackModel]);

        if (_options.Betas.Count > 0)
            cmd.AddRange(["--betas", string.Join(",", _options.Betas)]);

        var permissionPromptToolName = _options.PermissionPromptToolName;
        if (permissionPromptToolName == null && _options.CanUseTool != null)
            permissionPromptToolName = "stdio";

        if (permissionPromptToolName != null)
            cmd.AddRange(["--permission-prompt-tool", permissionPromptToolName]);

        if (_options.PermissionMode.HasValue)
            cmd.AddRange(["--permission-mode", PermissionModeToCliValue(_options.PermissionMode.Value)]);

        if (_options.ContinueConversation)
            cmd.Add("--continue");

        if (_options.Resume != null)
            cmd.AddRange(["--resume", _options.Resume]);

        // Python commit 5656d20: --session-id forwarding.
        if (!string.IsNullOrEmpty(_options.SessionId))
            cmd.AddRange(["--session-id", _options.SessionId]);

        var settingsValue = BuildSettingsValue();
        if (settingsValue != null)
            cmd.AddRange(["--settings", settingsValue]);

        foreach (var dir in _options.AddDirs)
            cmd.AddRange(["--add-dir", dir]);

        // MCP servers
        if (_options.McpServers != null)
        {
            if (_options.McpServers is Dictionary<string, object> servers)
            {
                // Strip the "instance" field from SDK server configs before
                // serializing — the live SdkMcpServerConfig.Handlers list is
                // delegate state that must not cross the process boundary.
                var serversForCli = new Dictionary<string, object>();
                foreach (var (name, config) in servers)
                {
                    if (config is McpSdkServerConfig sdk)
                    {
                        serversForCli[name] = new Dictionary<string, object?>
                        {
                            ["type"] = "sdk",
                            ["name"] = sdk.Name
                        };
                    }
                    else
                    {
                        serversForCli[name] = config;
                    }
                }
                var mcpConfig = new { mcpServers = serversForCli };
                cmd.AddRange(["--mcp-config", JsonSerializer.Serialize(mcpConfig)]);
            }
            else
            {
                cmd.AddRange(["--mcp-config", _options.McpServers.ToString()!]);
            }
        }

        if (_options.IncludePartialMessages)
            cmd.Add("--include-partial-messages");

        // Python commit c1182a4.
        if (_options.IncludeHookEvents)
            cmd.Add("--include-hook-events");

        // Python commit 32bcc4e.
        if (_options.StrictMcpConfig)
            cmd.Add("--strict-mcp-config");

        if (_options.ForkSession)
            cmd.Add("--fork-session");

        // Python commit 6e3d54f: session mirroring flag (paired with SessionStore).
        if (_options.SessionStore != null)
            cmd.Add("--session-mirror");

        // Agents are sent via initialize request body, not as a CLI flag
        // (Python commit 7c6902b — matches TypeScript SDK).
        // Phase 4B note: the .NET client still emits --agents for back-compat;
        // tracked in Phase 6B handoff.
        if (_options.Agents != null && _options.Agents.Count > 0)
        {
            var agentsJson = JsonSerializer.Serialize(_options.Agents);
            cmd.AddRange(["--agents", agentsJson]);
        }

        // Python commit 1c26bd3 + e621929: compute effective allowedTools and
        // setting-sources from skills configuration.
        var (effectiveAllowedTools, effectiveSettingSources) = ApplySkillsDefaults();

        // Replace the simple --allowedTools above with the skill-aware version.
        // (We already emitted --allowedTools earlier from _options.AllowedTools;
        // detect+rewrite here to keep parity with Python.)
        var allowedIdx = cmd.IndexOf("--allowedTools");
        if (effectiveAllowedTools.Count > 0)
        {
            if (allowedIdx >= 0 && allowedIdx + 1 < cmd.Count)
                cmd[allowedIdx + 1] = string.Join(",", effectiveAllowedTools);
            else
                cmd.AddRange(["--allowedTools", string.Join(",", effectiveAllowedTools)]);
        }

        if (effectiveSettingSources != null)
        {
            // Python commits ab9bcab / e621929: pass through as `--setting-sources=a,b`
            // (single arg). Empty list disables filesystem settings; null means omit
            // the flag entirely.
            cmd.Add($"--setting-sources={string.Join(",", effectiveSettingSources)}");
        }

        foreach (var plugin in _options.Plugins)
        {
            if (plugin.Type == "local")
                cmd.AddRange(["--plugin-dir", plugin.Path]);
        }

        foreach (var (flag, value) in _options.ExtraArgs)
        {
            if (value == null)
                cmd.Add($"--{flag}");
            else
                cmd.AddRange([$"--{flag}", value]);
        }

        // Python commit 6617b9e: emit `--thinking adaptive` / `--thinking disabled`
        // for those variants (not just --max-thinking-tokens). Python commit 32f09c1:
        // forward `--thinking-display` for adaptive/enabled variants.
#pragma warning disable CS0618 // MaxThinkingTokens is obsolete
        if (_options.Thinking is not null)
        {
            switch (_options.Thinking)
            {
                case ThinkingConfigAdaptive:
                    cmd.AddRange(["--thinking", "adaptive"]);
                    break;
                case ThinkingConfigEnabled e:
                    cmd.AddRange(["--max-thinking-tokens", e.BudgetTokens.ToString()]);
                    break;
                case ThinkingConfigDisabled:
                    cmd.AddRange(["--thinking", "disabled"]);
                    break;
            }

            if (_options.Thinking is not ThinkingConfigDisabled &&
                _options.Thinking.Display is { } display)
            {
                cmd.AddRange(["--thinking-display", display]);
            }
        }
        else if (_options.MaxThinkingTokens.HasValue)
        {
            cmd.AddRange(["--max-thinking-tokens", _options.MaxThinkingTokens.Value.ToString()]);
        }
#pragma warning restore CS0618

        if (_options.Effort.HasValue)
            cmd.AddRange(["--effort", _options.Effort.Value.ToJsonString()]);

        if (_options.OutputFormat.HasValue &&
            _options.OutputFormat.Value.TryGetProperty("type", out var typeElement) &&
            typeElement.GetString() == "json_schema" &&
            _options.OutputFormat.Value.TryGetProperty("schema", out var schema))
        {
            cmd.AddRange(["--json-schema", schema.GetRawText()]);
        }

        // Prompt handling - must come after all flags
        if (_isStreaming)
        {
            cmd.AddRange(["--input-format", "stream-json"]);
        }
        else
        {
            cmd.AddRange(["--print", "--", _prompt.ToString()!]);
        }

        // Check if command line is too long (Windows limitation) and spill agents JSON to a temp file if needed.
        var cmdStr = string.Join(" ", cmd);
        if (cmdStr.Length > CmdLengthLimit && _options.Agents != null && _options.Agents.Count > 0)
        {
            try
            {
                var agentsIdx = cmd.IndexOf("--agents");
                if (agentsIdx >= 0 && agentsIdx + 1 < cmd.Count)
                {
                    var agentsJsonValue = cmd[agentsIdx + 1];
                    var tempFile = Path.Combine(Path.GetTempPath(), $"claude-agent-sdk-agents-{Guid.NewGuid():N}.json");
                    File.WriteAllText(tempFile, agentsJsonValue, Encoding.UTF8);
                    _tempFiles.Add(tempFile);
                    cmd[agentsIdx + 1] = $"@{tempFile}";
                }
            }
            catch
            {
                // Best-effort only.
            }
        }

        return cmd;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_process != null)
            return;

        // Python commit 19e1f53: defer CLI discovery to ConnectAsync.
        _cliPath ??= await Task.Run(FindCli, cancellationToken);

        // Check CLI version
        if (Environment.GetEnvironmentVariable("CLAUDE_AGENT_SDK_SKIP_VERSION_CHECK") == null)
            await CheckClaudeVersionAsync(cancellationToken);

        var cmd = BuildCommand();

        var shouldReadStderr = _options.StderrCallback != null ||
                               _options.ExtraArgs.ContainsKey("debug-to-stderr");

        var startInfo = new ProcessStartInfo
        {
            FileName = cmd[0],
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = shouldReadStderr,
            CreateNoWindow = true
        };

        // Add arguments
        for (int i = 1; i < cmd.Count; i++)
            startInfo.ArgumentList.Add(cmd[i]);

        // Set working directory
        if (_cwd != null)
            startInfo.WorkingDirectory = _cwd;

        // Python commit 5839ff9: filter out CLAUDECODE so SDK-spawned subprocesses
        // don't think they're running inside a Claude Code parent.
        startInfo.Environment.Remove("CLAUDECODE");

        // Set environment
        foreach (var (key, value) in _options.Env)
            startInfo.Environment[key] = value;

        // Python commit 6d77aef: default CLAUDE_CODE_ENTRYPOINT only if absent
        // (caller-provided value via options.Env wins).
        if (!_options.Env.ContainsKey("CLAUDE_CODE_ENTRYPOINT"))
            startInfo.Environment["CLAUDE_CODE_ENTRYPOINT"] = "sdk-dotnet";

        startInfo.Environment["CLAUDE_AGENT_SDK_VERSION"] =
            GetType().Assembly.GetName().Version?.ToString() ?? "0.1.0";

        // Python commit bbec84d: propagate W3C trace context (TRACEPARENT/TRACESTATE)
        // from the current Activity to the subprocess. No-op when there's no active
        // Activity; options.Env always wins.
        var activity = System.Diagnostics.Activity.Current;
        if (activity != null && !string.IsNullOrEmpty(activity.Id))
        {
            if (!_options.Env.ContainsKey("TRACEPARENT"))
                startInfo.Environment["TRACEPARENT"] = activity.Id;
            else if (_options.Env.TryGetValue("TRACEPARENT", out var tp))
                startInfo.Environment["TRACEPARENT"] = tp;

            var tracestate = activity.TraceStateString;
            if (!_options.Env.ContainsKey("TRACESTATE"))
            {
                if (!string.IsNullOrEmpty(tracestate))
                    startInfo.Environment["TRACESTATE"] = tracestate;
                else
                    startInfo.Environment.Remove("TRACESTATE");
            }
        }

        if (_options.EnableFileCheckpointing)
            startInfo.Environment["CLAUDE_CODE_ENABLE_SDK_FILE_CHECKPOINTING"] = "true";

        if (_cwd != null)
            startInfo.Environment["PWD"] = _cwd;

        try
        {
            _process = Process.Start(startInfo)
                ?? throw new CliConnectionException("Failed to start Claude Code process");

            // Python commit f2389ec: track for parent-exit cleanup.
            _activeChildren[_process.Id] = _process;

            _stdin = _process.StandardInput;
            _stdout = _process.StandardOutput;
            if (shouldReadStderr)
            {
                _stderr = _process.StandardError;
                _stderrTask = Task.Run(() => HandleStderrAsync(cancellationToken), cancellationToken);
            }

            // Handle stdin based on mode (string-prompt --print path closes
            // stdin immediately; streaming path keeps it open for stream-json input)
            if (!_isStreaming)
            {
                _stdin.Close();
                _stdin = null;
            }

            _ready = true;
        }
        catch (Exception ex) when (ex is not CliConnectionException)
        {
            if (_cwd != null && !Directory.Exists(_cwd))
            {
                _exitError = new CliConnectionException($"Working directory does not exist: {_cwd}");
                throw _exitError;
            }
            _exitError = new CliNotFoundException($"Claude Code not found at: {_cliPath}", _cliPath);
            throw _exitError;
        }
    }

    private async Task HandleStderrAsync(CancellationToken cancellationToken)
    {
        if (_stderr == null) return;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await _stderr.ReadLineAsync(cancellationToken);
                if (line == null) break;

                // Python commit 6bbad5f: isolate per-line so a raise in the user's
                // callback doesn't terminate the loop and silently drop every
                // subsequent line for the rest of the session.
                if (_options.StderrCallback != null)
                {
                    try
                    {
                        _options.StderrCallback(line);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SubprocessTransport] stderr callback threw: {ex.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }

    public async Task WriteAsync(string data, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            if (!_ready || _stdin == null)
                throw new CliConnectionException("Transport is not ready for writing");

            if (_process?.HasExited == true)
                throw new CliConnectionException($"Cannot write to terminated process (exit code: {_process.ExitCode})");

            if (_exitError != null)
                throw new CliConnectionException($"Cannot write to process that exited with error", _exitError);

            await _stdin.WriteAsync(data.AsMemory(), cancellationToken);
            await _stdin.FlushAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not CliConnectionException)
        {
            _ready = false;
            _exitError = new CliConnectionException($"Failed to write to process stdin: {ex.Message}", ex);
            throw _exitError;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task EndInputAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            if (_stdin != null)
            {
                _stdin.Close();
                _stdin = null;
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async IAsyncEnumerable<JsonElement> ReadMessagesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_process == null || _stdout == null)
            throw new CliConnectionException("Not connected");

        var jsonBuffer = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await _stdout.ReadLineAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line == null)
                break;

            var lineStr = line.Trim();
            if (string.IsNullOrEmpty(lineStr))
                continue;

            // Python commit c290bbf: skip non-JSON lines (e.g. [SandboxDebug])
            // when not mid-parse — they corrupt the buffer otherwise.
            if (jsonBuffer.Length == 0 && !lineStr.StartsWith('{'))
            {
                Debug.WriteLine($"[SubprocessTransport] Skipping non-JSON stdout line: {lineStr.Substring(0, Math.Min(200, lineStr.Length))}");
                continue;
            }

            jsonBuffer.Append(lineStr);

            if (jsonBuffer.Length > _maxBufferSize)
            {
                var bufferLength = jsonBuffer.Length;
                jsonBuffer.Clear();
                throw new JsonDecodeException(
                    $"JSON message exceeded maximum buffer size of {_maxBufferSize} bytes",
                    new InvalidOperationException($"Buffer size {bufferLength} exceeds limit {_maxBufferSize}")
                );
            }

            JsonElement json;
            try
            {
                json = JsonSerializer.Deserialize<JsonElement>(jsonBuffer.ToString());
            }
            catch (JsonException)
            {
                // Speculatively decode until we have a full JSON object.
                continue;
            }

            jsonBuffer.Clear();
            yield return json;
        }

        // Flush any remaining buffered JSON at EOF.
        if (jsonBuffer.Length > 0)
        {
            var trailing = default(JsonElement);
            var hasTrailing = false;
            try
            {
                trailing = JsonSerializer.Deserialize<JsonElement>(jsonBuffer.ToString());
                hasTrailing = true;
            }
            catch (JsonException)
            {
                // Ignore incomplete trailing JSON.
            }

            if (hasTrailing)
                yield return trailing;
        }

        // Check process exit
        try
        {
            await _process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) { }

        if (_process.ExitCode != 0)
        {
            _exitError = new ProcessException(
                "Command failed",
                _process.ExitCode,
                "Check stderr output for details"
            );
            throw _exitError;
        }
    }

    private async Task CheckClaudeVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            var startInfo = new ProcessStartInfo
            {
                FileName = _cliPath,
                Arguments = "-v",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return;

            var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            var match = System.Text.RegularExpressions.Regex.Match(output, @"(\d+\.\d+\.\d+)");
            if (match.Success)
            {
                var version = Version.Parse(match.Groups[1].Value);
                var minVersion = Version.Parse(MinimumClaudeCodeVersion);

                if (version < minVersion)
                {
                    // Python commit 6384c69: dedupe the warning per process.
                    if (Interlocked.Exchange(ref _versionWarningEmitted, 1) == 0)
                    {
                        Console.Error.WriteLine(
                            $"Warning: Claude Code version {version} is unsupported in the Agent SDK. " +
                            $"Minimum required version is {MinimumClaudeCodeVersion}. " +
                            "Some features may not work correctly."
                        );
                    }
                }
            }
        }
        catch (Exception)
        {
            // Ignore version check failures
        }
    }

    public async Task CloseAsync()
    {
        // Clean up temp files
        foreach (var tempFile in _tempFiles)
        {
            try { File.Delete(tempFile); } catch { }
        }
        _tempFiles.Clear();

        if (_process == null)
        {
            _ready = false;
            return;
        }

        // Wait for stderr task
        if (_stderrTask != null)
        {
            try { await _stderrTask.WaitAsync(TimeSpan.FromSeconds(1)); }
            catch { }
        }

        // Close stdin
        await _writeLock.WaitAsync();
        try
        {
            _ready = false;
            if (_stdin != null)
            {
                try { _stdin.Close(); } catch { }
                _stdin = null;
            }
        }
        finally
        {
            _writeLock.Release();
        }

        // Python commit 40cc6f5: wait for graceful shutdown after stdin EOF;
        // SIGTERM only if it doesn't exit, force kill if SIGTERM doesn't take.
        try
        {
            if (!_process.HasExited)
            {
                try
                {
                    using var gracefulCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await _process.WaitForExitAsync(gracefulCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Force terminate (SIGTERM equivalent on POSIX; TerminateProcess on Win32).
                    try { _process.Kill(); } catch { }
                    try
                    {
                        using var killCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await _process.WaitForExitAsync(killCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        try { _process.Kill(entireProcessTree: true); } catch { }
                        try { await _process.WaitForExitAsync(); } catch { }
                    }
                }
            }
        }
        finally
        {
            _activeChildren.TryRemove(_process.Id, out _);
        }

        _process.Dispose();
        _process = null;
        _stdout = null;
        _stderr = null;
        _exitError = null;
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        _writeLock.Dispose();
    }
}
