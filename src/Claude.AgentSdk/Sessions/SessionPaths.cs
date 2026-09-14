// Claude Agent SDK for .NET — Path/UUID helpers shared by the sessions subsystem.
// Reference: reference/claude-agent-sdk-python/src/claude_agent_sdk/_internal/sessions.py
// (project_key_for_directory @ 1418, _sanitize_path @ 103, _validate_uuid @ 68,
// _get_projects_dir @ 129)
// Reference: reference/claude-agent-sdk-python/src/claude_agent_sdk/_internal/session_store.py
// (file_path_to_session_key @ 149)

using System.Text;
using System.Text.RegularExpressions;

namespace Claude.AgentSdk.Sessions;

/// <summary>
/// Path/UUID helpers shared by the sessions subsystem. Mirrors the lower
/// half of Python's <c>_internal/sessions.py</c>.
/// </summary>
public static class SessionPaths
{
    /// <summary>Most filesystems limit a single path component to 255 bytes;
    /// we leave room for the hash suffix and separator (matches Python).</summary>
    public const int MaxSanitizedLength = 200;

    private static readonly Regex UuidRegex = new(
        @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SanitizeRegex = new(@"[^a-zA-Z0-9]", RegexOptions.Compiled);

    /// <summary>Returns true if <paramref name="maybeUuid"/> matches the
    /// canonical 8-4-4-4-12 hex UUID shape (any casing).</summary>
    public static bool ValidateUuid(string? maybeUuid)
        => !string.IsNullOrEmpty(maybeUuid) && UuidRegex.IsMatch(maybeUuid);

    /// <summary>
    /// 32-bit integer hash to base36, matching the CLI's directory naming
    /// (Python's <c>_simple_hash</c>, originally the JS <c>simpleHash</c>).
    /// </summary>
    public static string SimpleHash(string s)
    {
        long h = 0;
        foreach (var ch in s)
        {
            h = ((h << 5) - h + ch) & 0xFFFFFFFFL;
            if (h >= 0x80000000L) h -= 0x100000000L;
        }
        h = Math.Abs(h);
        if (h == 0) return "0";
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        var sb = new StringBuilder();
        while (h > 0)
        {
            sb.Insert(0, digits[(int)(h % 36)]);
            h /= 36;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Make a string safe for use as a directory name (Python
    /// <c>_sanitize_path</c>): non-alphanumerics → hyphens; truncate +
    /// append hash if longer than <see cref="MaxSanitizedLength"/>.
    /// </summary>
    public static string SanitizePath(string name)
    {
        var sanitized = SanitizeRegex.Replace(name, "-");
        if (sanitized.Length <= MaxSanitizedLength) return sanitized;
        return $"{sanitized[..MaxSanitizedLength]}-{SimpleHash(name)}";
    }

    /// <summary>
    /// Compute the project_key (sanitized directory name) used as the
    /// <see cref="SessionKey.ProjectKey"/> for sessions associated with
    /// <paramref name="directory"/>. Mirrors Python
    /// <c>project_key_for_directory</c>.
    /// </summary>
    public static string ProjectKeyForDirectory(string? directory = null)
    {
        var cwd = directory ?? Environment.CurrentDirectory;
        try
        {
            cwd = Path.GetFullPath(cwd);
        }
        catch
        {
            // fall through with the original value — sanitize handles either.
        }
        // NFC normalize like Python does (handles HFS+ decomposed forms).
        cwd = cwd.Normalize(NormalizationForm.FormC);
        return SanitizePath(cwd);
    }

    /// <summary>
    /// Returns the Claude config home directory, respecting
    /// <c>CLAUDE_CONFIG_DIR</c>. Defaults to <c>~/.claude</c>.
    /// </summary>
    public static string GetClaudeConfigHomeDir()
    {
        var overridePath = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (!string.IsNullOrEmpty(overridePath))
            return overridePath.Normalize(NormalizationForm.FormC);
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude").Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Returns the projects directory (<c>$CLAUDE_CONFIG_DIR/projects</c> or
    /// <c>~/.claude/projects</c>). <paramref name="envOverride"/> is
    /// consulted before <see cref="Environment"/> so callers that pass
    /// <c>CLAUDE_CONFIG_DIR</c> to the subprocess via <c>options.Env</c>
    /// resolve the same directory the subprocess will write to.
    /// </summary>
    public static string GetProjectsDir(IReadOnlyDictionary<string, string>? envOverride = null)
    {
        if (envOverride != null
            && envOverride.TryGetValue("CLAUDE_CONFIG_DIR", out var ov)
            && !string.IsNullOrEmpty(ov))
        {
            return Path.Combine(ov.Normalize(NormalizationForm.FormC), "projects");
        }
        return Path.Combine(GetClaudeConfigHomeDir(), "projects");
    }

    /// <summary>
    /// Derive a <see cref="SessionKey"/> from an absolute transcript file
    /// path. Mirrors Python <c>file_path_to_session_key</c>.
    /// Returns <c>null</c> if <paramref name="filePath"/> is not under
    /// <paramref name="projectsDir"/> or has an unrecognized shape.
    /// </summary>
    public static SessionKey? FilePathToSessionKey(string filePath, string projectsDir)
    {
        string rel;
        try
        {
            rel = Path.GetRelativePath(projectsDir, filePath);
        }
        catch (ArgumentException)
        {
            return null;
        }
        if (string.IsNullOrEmpty(rel) || rel == ".") return null;
        if (Path.IsPathRooted(rel)) return null;

        var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Length == 0 || parts[0] == "..") return null;
        if (parts.Length < 2) return null;

        var projectKey = parts[0];
        var second = parts[1];

        if (parts.Length == 2 && second.EndsWith(".jsonl", StringComparison.Ordinal))
        {
            return new SessionKey
            {
                ProjectKey = projectKey,
                SessionId = second[..^".jsonl".Length],
            };
        }

        if (parts.Length >= 4)
        {
            var subpathParts = new List<string>(parts.Length - 2);
            for (int i = 2; i < parts.Length; i++) subpathParts.Add(parts[i]);
            var last = subpathParts[^1];
            if (last.EndsWith(".jsonl", StringComparison.Ordinal))
                subpathParts[^1] = last[..^".jsonl".Length];
            return new SessionKey
            {
                ProjectKey = projectKey,
                SessionId = second,
                Subpath = string.Join('/', subpathParts),
            };
        }

        return null;
    }
}
