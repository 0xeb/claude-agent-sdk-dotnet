// Claude Agent SDK for .NET
// Port of claude-agent-sdk-python/_internal/transport/subprocess_cli.py

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

    private readonly object _prompt;
    private readonly bool _isStreaming;
    private readonly ClaudeAgentOptions _options;
    private readonly string _cliPath;
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
        _cliPath = options.CliPath ?? FindCli();
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

    private List<string> BuildCommand()
    {
        var cmd = new List<string> { _cliPath, "--output-format", "stream-json", "--verbose" };

        // System prompt
        if (_options.SystemPrompt == null)
        {
            cmd.AddRange(["--system-prompt", ""]);
        }
        else
        {
            cmd.AddRange(["--system-prompt", _options.SystemPrompt]);
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
                var mcpConfig = new { mcpServers = servers };
                cmd.AddRange(["--mcp-config", JsonSerializer.Serialize(mcpConfig)]);
            }
            else
            {
                cmd.AddRange(["--mcp-config", _options.McpServers.ToString()!]);
            }
        }

        if (_options.IncludePartialMessages)
            cmd.Add("--include-partial-messages");

        if (_options.ForkSession)
            cmd.Add("--fork-session");

        if (_options.Agents != null && _options.Agents.Count > 0)
        {
            var agentsJson = JsonSerializer.Serialize(_options.Agents);
            cmd.AddRange(["--agents", agentsJson]);
        }

        var sourcesValue = _options.SettingSources != null
            ? string.Join(",", _options.SettingSources.Select(s => s.ToString().ToLowerInvariant()))
            : "";
        cmd.AddRange(["--setting-sources", sourcesValue]);

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

        // Resolve thinking config → --max-thinking-tokens
        // `Thinking` takes precedence over the deprecated `MaxThinkingTokens`
#pragma warning disable CS0618 // MaxThinkingTokens is obsolete
        int? resolvedMaxThinkingTokens = _options.MaxThinkingTokens;
#pragma warning restore CS0618
        if (_options.Thinking is not null)
        {
            resolvedMaxThinkingTokens = _options.Thinking switch
            {
                ThinkingConfigAdaptive => resolvedMaxThinkingTokens ?? 32_000,
                ThinkingConfigEnabled e => e.BudgetTokens,
                ThinkingConfigDisabled => 0,
                _ => resolvedMaxThinkingTokens
            };
        }
        if (resolvedMaxThinkingTokens.HasValue)
            cmd.AddRange(["--max-thinking-tokens", resolvedMaxThinkingTokens.Value.ToString()]);

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

        // Set environment
        foreach (var (key, value) in _options.Env)
            startInfo.Environment[key] = value;
        startInfo.Environment["CLAUDE_CODE_ENTRYPOINT"] = "sdk-dotnet";
        startInfo.Environment["CLAUDE_AGENT_SDK_VERSION"] = GetType().Assembly.GetName().Version?.ToString() ?? "0.1.0";

        if (_options.EnableFileCheckpointing)
            startInfo.Environment["CLAUDE_CODE_ENABLE_SDK_FILE_CHECKPOINTING"] = "true";

        if (_cwd != null)
            startInfo.Environment["PWD"] = _cwd;

        try
        {
            _process = Process.Start(startInfo)
                ?? throw new CliConnectionException("Failed to start Claude Code process");

            _stdin = _process.StandardInput;
            _stdout = _process.StandardOutput;
            if (shouldReadStderr)
            {
                _stderr = _process.StandardError;
                _stderrTask = Task.Run(() => HandleStderrAsync(cancellationToken), cancellationToken);
            }

            // Handle stdin based on mode
            if (!_isStreaming)
            {
                // String mode: close stdin immediately
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

                _options.StderrCallback?.Invoke(line);
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
                    Console.Error.WriteLine(
                        $"Warning: Claude Code version {version} is unsupported in the Agent SDK. " +
                        $"Minimum required version is {MinimumClaudeCodeVersion}. " +
                        "Some features may not work correctly."
                    );
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

        // Terminate process
        if (!_process.HasExited)
        {
            try
            {
                _process.Kill();
                await _process.WaitForExitAsync();
            }
            catch { }
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
