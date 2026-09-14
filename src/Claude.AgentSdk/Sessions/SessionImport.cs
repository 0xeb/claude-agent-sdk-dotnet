// Claude Agent SDK for .NET — Replay a local on-disk session transcript into a SessionStore.
// Reference: reference/claude-agent-sdk-python/src/claude_agent_sdk/_internal/session_import.py

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claude.AgentSdk.Sessions;

/// <summary>
/// Replay a local on-disk session transcript into a
/// <see cref="ISessionStore"/>. Mirrors Python <c>import_session_to_store</c>.
/// </summary>
public static class SessionImport
{
    /// <summary>
    /// Stream-read <paramref name="sessionFilePath"/> line-by-line and flush
    /// to <see cref="ISessionStore.AppendAsync"/> in batches of
    /// <paramref name="batchSize"/> entries (or 1 MiB of line text,
    /// whichever comes first). Skips blank lines.
    /// </summary>
    public static async Task ImportSessionFileAsync(
        string sessionFilePath,
        SessionKey key,
        ISessionStore store,
        int batchSize = TranscriptMirrorBatcher.DefaultMaxPendingEntries,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0) batchSize = TranscriptMirrorBatcher.DefaultMaxPendingEntries;

        var batch = new List<SessionStoreEntry>();
        long nbytes = 0;
        using var sr = new StreamReader(sessionFilePath, Encoding.UTF8);
        string? line;
        while ((line = await sr.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
            if (string.IsNullOrEmpty(line)) continue;
            JsonNode? node;
            try { node = JsonNode.Parse(line); }
            catch (JsonException) { continue; }
            if (node is not JsonObject obj) continue;
            batch.Add(SessionSummary.JsonObjectToEntry(obj));
            nbytes += line.Length;
            if (batch.Count >= batchSize || nbytes >= TranscriptMirrorBatcher.DefaultMaxPendingBytes)
            {
                await store.AppendAsync(key, batch, cancellationToken).ConfigureAwait(false);
                batch = new List<SessionStoreEntry>();
                nbytes = 0;
            }
        }
        if (batch.Count > 0)
            await store.AppendAsync(key, batch, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Replay a local session transcript into a <see cref="ISessionStore"/>.
    /// Mirrors Python <c>import_session_to_store</c>.
    /// </summary>
    /// <param name="sessionId">UUID of the session to import.</param>
    /// <param name="store">Destination <see cref="ISessionStore"/>.</param>
    /// <param name="directory">
    /// Project directory under which to find the session JSONL. Searched as
    /// <c>{projects_dir}/{sanitize(directory)}/{sessionId}.jsonl</c>. If
    /// <c>null</c>, all project directories are scanned.
    /// </param>
    /// <param name="includeSubagents">
    /// If <c>true</c> (default), also import subagent transcripts under
    /// <c>&lt;sessionId&gt;/subagents/**</c> and their <c>.meta.json</c> sidecars.
    /// </param>
    /// <param name="batchSize">Max entries per <see cref="ISessionStore.AppendAsync"/> call.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static async Task ImportSessionToStoreAsync(
        string sessionId,
        ISessionStore store,
        string? directory = null,
        bool includeSubagents = true,
        int batchSize = TranscriptMirrorBatcher.DefaultMaxPendingEntries,
        CancellationToken cancellationToken = default)
    {
        if (!SessionPaths.ValidateUuid(sessionId))
            throw new InvalidSessionIdException(sessionId);

        var resolved = ResolveSessionFilePath(sessionId, directory);
        if (resolved is null)
            throw new SessionNotFoundException(sessionId);

        // Key under the on-disk project directory name (file_path_to_session_key parity).
        var projectKey = new DirectoryInfo(Path.GetDirectoryName(resolved)!).Name;
        var mainKey = new SessionKey { ProjectKey = projectKey, SessionId = sessionId };
        await ImportSessionFileAsync(resolved, mainKey, store, batchSize, cancellationToken).ConfigureAwait(false);

        if (!includeSubagents) return;

        var sessionDir = Path.Combine(Path.GetDirectoryName(resolved)!, sessionId);
        var subagentsDir = Path.Combine(sessionDir, "subagents");
        if (!Directory.Exists(subagentsDir)) return;

        foreach (var filePath in CollectJsonlFiles(subagentsDir))
        {
            var rel = Path.GetRelativePath(sessionDir, filePath);
            var relNoExt = rel[..^".jsonl".Length];
            var subKey = new SessionKey
            {
                ProjectKey = projectKey,
                SessionId = sessionId,
                Subpath = relNoExt.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/'),
            };
            await ImportSessionFileAsync(filePath, subKey, store, batchSize, cancellationToken).ConfigureAwait(false);

            // Sidecar metadata file.
            var metaPath = filePath[..^".jsonl".Length] + ".meta.json";
            if (File.Exists(metaPath))
            {
                try
                {
                    var metaText = await File.ReadAllTextAsync(metaPath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
                    var metaNode = JsonNode.Parse(metaText) as JsonObject;
                    if (metaNode is not null)
                    {
                        metaNode["type"] = "agent_metadata";
                        var entry = SessionSummary.JsonObjectToEntry(metaNode);
                        await store.AppendAsync(subKey, new[] { entry }, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (FileNotFoundException) { /* fine */ }
            }
        }
    }

    /// <summary>
    /// Search the projects directory tree for the JSONL for
    /// <paramref name="sessionId"/>. If <paramref name="directory"/> is
    /// given, only that project directory is checked.
    /// </summary>
    public static string? ResolveSessionFilePath(string sessionId, string? directory)
    {
        var projectsDir = SessionPaths.GetProjectsDir();
        var fileName = sessionId + ".jsonl";

        if (directory is not null)
        {
            var projectKey = SessionPaths.SanitizePath(Path.GetFullPath(directory));
            var candidate = Path.Combine(projectsDir, projectKey, fileName);
            if (File.Exists(candidate) && new FileInfo(candidate).Length > 0)
                return candidate;
            return null;
        }

        if (!Directory.Exists(projectsDir)) return null;
        foreach (var pdir in Directory.EnumerateDirectories(projectsDir))
        {
            var candidate = Path.Combine(pdir, fileName);
            if (File.Exists(candidate) && new FileInfo(candidate).Length > 0)
                return candidate;
        }
        return null;
    }

    private static IEnumerable<string> CollectJsonlFiles(string baseDir)
    {
        if (!Directory.Exists(baseDir)) yield break;
        // Sort per directory for deterministic order across platforms.
        var stack = new Stack<string>();
        stack.Push(baseDir);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            IEnumerable<string> subdirs;
            IEnumerable<string> files;
            try
            {
                subdirs = Directory.EnumerateDirectories(current).OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal);
                files = Directory.EnumerateFiles(current, "*.jsonl").OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal);
            }
            catch
            {
                continue;
            }
            foreach (var d in subdirs.Reverse()) stack.Push(d);
            foreach (var f in files) yield return f;
        }
    }
}
