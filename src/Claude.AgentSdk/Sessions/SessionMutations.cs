// Claude Agent SDK for .NET — Session mutations (rename/tag/delete/fork) via ISessionStore.
// Reference: reference/claude-agent-sdk-python/src/claude_agent_sdk/_internal/session_mutations.py
// (rename_session_via_store @ 769, tag_session_via_store @ 810,
// delete_session_via_store @ 851, fork_session_via_store @ 885,
// _sanitize_unicode @ 737, _build_fork_lines @ 348)

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Claude.AgentSdk.Sessions;

/// <summary>Result of a <see cref="SessionMutations.ForkSessionViaStoreAsync"/>.</summary>
public sealed record ForkSessionResult(string SessionId);

/// <summary>
/// SessionStore-backed mutation helpers (async variants from Python's
/// <c>session_mutations.py</c>). The disk-based <c>rename_session</c> /
/// <c>tag_session</c> / <c>delete_session</c> / <c>fork_session</c>
/// functions operate on local <c>~/.claude</c>; the store-backed
/// variants exposed here are the ones routinely used by SDK consumers.
/// </summary>
public static class SessionMutations
{
    private static readonly HashSet<string> TranscriptTypes = new()
    {
        "user", "assistant", "attachment", "system", "progress",
    };

    private static readonly Regex UnicodeStripRegex = new(
        "[\u200b-\u200f\u202a-\u202e\u2066-\u2069\ufeff\ue000-\uf8ff]",
        RegexOptions.Compiled);

    private static readonly HashSet<UnicodeCategory> FormatCategories = new()
    {
        UnicodeCategory.Format,
        UnicodeCategory.PrivateUse,
        UnicodeCategory.OtherNotAssigned,
    };

    private static string IsoNow()
        => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffK", CultureInfo.InvariantCulture)
                          .Replace("+00:00", "Z");

    /// <summary>
    /// Sanitize a string by removing dangerous Unicode characters.
    /// Iteratively applies NFKC normalization and strips Cf/Co/Cn category
    /// characters until no more changes occur (max 10 iterations).
    /// Mirrors Python <c>_sanitize_unicode</c>.
    /// </summary>
    public static string SanitizeUnicode(string value)
    {
        var current = value;
        for (int i = 0; i < 10; i++)
        {
            var previous = current;
            current = current.Normalize(NormalizationForm.FormKC);
            var sb = new StringBuilder(current.Length);
            for (int j = 0; j < current.Length; j++)
            {
                var c = current[j];
                if (!FormatCategories.Contains(CharUnicodeInfo.GetUnicodeCategory(c)))
                    sb.Append(c);
            }
            current = UnicodeStripRegex.Replace(sb.ToString(), "");
            if (current == previous) break;
        }
        return current;
    }

    /// <summary>
    /// Append a <c>custom-title</c> entry to the session's transcript via
    /// <paramref name="store"/>. Mirrors Python <c>rename_session_via_store</c>.
    /// </summary>
    public static async Task RenameSessionViaStoreAsync(
        ISessionStore store,
        string sessionId,
        string title,
        string? directory = null,
        CancellationToken cancellationToken = default)
    {
        if (!SessionPaths.ValidateUuid(sessionId))
            throw new InvalidSessionIdException(sessionId);
        var stripped = title?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(stripped))
            throw new ArgumentException("title must be non-empty", nameof(title));

        var key = new SessionKey
        {
            ProjectKey = SessionPaths.ProjectKeyForDirectory(directory),
            SessionId = sessionId,
        };
        var entry = MakeExtraEntry("custom-title", new Dictionary<string, JsonNode?>
        {
            ["customTitle"] = stripped,
            ["sessionId"] = sessionId,
            ["uuid"] = Guid.NewGuid().ToString(),
            ["timestamp"] = IsoNow(),
        });
        await store.AppendAsync(key, new[] { entry }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Append a <c>tag</c> entry to the session's transcript via
    /// <paramref name="store"/>. Pass <c>null</c> to clear. Mirrors Python
    /// <c>tag_session_via_store</c>.
    /// </summary>
    public static async Task TagSessionViaStoreAsync(
        ISessionStore store,
        string sessionId,
        string? tag,
        string? directory = null,
        CancellationToken cancellationToken = default)
    {
        if (!SessionPaths.ValidateUuid(sessionId))
            throw new InvalidSessionIdException(sessionId);
        if (tag is not null)
        {
            var sanitized = SanitizeUnicode(tag).Trim();
            if (string.IsNullOrEmpty(sanitized))
                throw new ArgumentException("tag must be non-empty (use null to clear)", nameof(tag));
            tag = sanitized;
        }

        var key = new SessionKey
        {
            ProjectKey = SessionPaths.ProjectKeyForDirectory(directory),
            SessionId = sessionId,
        };
        var entry = MakeExtraEntry("tag", new Dictionary<string, JsonNode?>
        {
            ["tag"] = tag ?? string.Empty,
            ["sessionId"] = sessionId,
            ["uuid"] = Guid.NewGuid().ToString(),
            ["timestamp"] = IsoNow(),
        });
        await store.AppendAsync(key, new[] { entry }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Delete a session from <paramref name="store"/>. No-op if the store
    /// does not implement <see cref="ISessionStore.DeleteAsync"/> (matches
    /// Python's WORM-friendly contract).
    /// </summary>
    public static async Task DeleteSessionViaStoreAsync(
        ISessionStore store,
        string sessionId,
        string? directory = null,
        CancellationToken cancellationToken = default)
    {
        if (!SessionPaths.ValidateUuid(sessionId))
            throw new InvalidSessionIdException(sessionId);
        if (!SessionStoreValidation.StoreImplements(store, nameof(ISessionStore.DeleteAsync)))
            return;
        var key = new SessionKey
        {
            ProjectKey = SessionPaths.ProjectKeyForDirectory(directory),
            SessionId = sessionId,
        };
        await store.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fork a session into a new branch with fresh UUIDs via
    /// <paramref name="store"/>. Mirrors Python <c>fork_session_via_store</c>.
    /// </summary>
    public static async Task<ForkSessionResult> ForkSessionViaStoreAsync(
        ISessionStore store,
        string sessionId,
        string? directory = null,
        string? upToMessageId = null,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        if (!SessionPaths.ValidateUuid(sessionId))
            throw new InvalidSessionIdException(sessionId);
        if (upToMessageId is not null && !SessionPaths.ValidateUuid(upToMessageId))
            throw new InvalidSessionIdException(upToMessageId);

        var srcKey = new SessionKey
        {
            ProjectKey = SessionPaths.ProjectKeyForDirectory(directory),
            SessionId = sessionId,
        };
        var loaded = await store.LoadAsync(srcKey, cancellationToken).ConfigureAwait(false);
        if (loaded is null || loaded.Count == 0)
            throw new SessionNotFoundException(sessionId);

        // Partition into transcript entries (with uuid + transcript type) and
        // content-replacement records, mirroring _parse_fork_transcript.
        var transcript = new List<JsonObject>();
        var contentReplacements = new List<JsonNode?>();
        foreach (var e in loaded)
        {
            var obj = SessionSummary.EntryToJsonObject(e);
            var type = StringOf(obj, "type");
            if (TranscriptTypes.Contains(type ?? "") && obj["uuid"] is JsonValue uv && uv.TryGetValue<string>(out _))
            {
                transcript.Add(obj);
            }
            else if (type == "content-replacement"
                     && StringOf(obj, "sessionId") == sessionId
                     && obj["replacements"] is JsonArray repl)
            {
                foreach (var r in repl) contentReplacements.Add(r?.DeepClone());
            }
        }

        var (forkedSessionId, lines) = BuildForkLines(
            transcript, contentReplacements, sessionId, upToMessageId, title,
            () => DeriveTitleFromEntries(loaded));

        var dstKey = new SessionKey
        {
            ProjectKey = srcKey.ProjectKey,
            SessionId = forkedSessionId,
        };
        var dstEntries = new List<SessionStoreEntry>(lines.Count);
        foreach (var line in lines)
        {
            var node = JsonNode.Parse(line);
            if (node is JsonObject obj)
                dstEntries.Add(SessionSummary.JsonObjectToEntry(obj));
        }
        await store.AppendAsync(dstKey, dstEntries, cancellationToken).ConfigureAwait(false);
        return new ForkSessionResult(forkedSessionId);
    }

    // ---- Fork transform --------------------------------------------------

    private static (string ForkedSessionId, List<string> Lines) BuildForkLines(
        List<JsonObject> transcript,
        List<JsonNode?> contentReplacements,
        string sessionId,
        string? upToMessageId,
        string? title,
        Func<string?> deriveTitle)
    {
        // Filter out sidechains.
        transcript = transcript.Where(e => !BoolOf(e, "isSidechain")).ToList();
        if (transcript.Count == 0)
            throw new InvalidOperationException($"Session {sessionId} has no messages to fork");

        if (upToMessageId is not null)
        {
            int cutoff = -1;
            for (int i = 0; i < transcript.Count; i++)
            {
                if (StringOf(transcript[i], "uuid") == upToMessageId) { cutoff = i; break; }
            }
            if (cutoff < 0)
                throw new InvalidOperationException($"Message {upToMessageId} not found in session {sessionId}");
            transcript = transcript.Take(cutoff + 1).ToList();
        }

        var uuidMapping = new Dictionary<string, string>();
        foreach (var e in transcript)
        {
            var u = StringOf(e, "uuid");
            if (u is not null) uuidMapping[u] = Guid.NewGuid().ToString();
        }

        var writable = transcript.Where(e => StringOf(e, "type") != "progress").ToList();
        if (writable.Count == 0)
            throw new InvalidOperationException($"Session {sessionId} has no messages to fork");

        var byUuid = transcript
            .Where(e => StringOf(e, "uuid") is not null)
            .ToDictionary(e => StringOf(e, "uuid")!, e => e);

        var forkedSessionId = Guid.NewGuid().ToString();
        var now = IsoNow();
        var lines = new List<string>(writable.Count + 2);

        for (int i = 0; i < writable.Count; i++)
        {
            var original = writable[i];
            var origUuid = StringOf(original, "uuid")!;
            var newUuid = uuidMapping[origUuid];

            // Resolve parentUuid skipping progress ancestors.
            string? newParent = null;
            var parentId = StringOf(original, "parentUuid");
            while (!string.IsNullOrEmpty(parentId))
            {
                if (!byUuid.TryGetValue(parentId, out var parent)) break;
                if (StringOf(parent, "type") != "progress")
                {
                    uuidMapping.TryGetValue(parentId, out newParent);
                    break;
                }
                parentId = StringOf(parent, "parentUuid");
            }

            var timestamp = (i == writable.Count - 1) ? now : (StringOf(original, "timestamp") ?? now);

            // Remap logicalParentUuid if present.
            var logicalParent = StringOf(original, "logicalParentUuid");
            string? newLogicalParent = logicalParent;
            if (logicalParent is not null && uuidMapping.TryGetValue(logicalParent, out var mapped))
                newLogicalParent = mapped;

            var forked = (JsonObject)original.DeepClone();
            forked["uuid"] = newUuid;
            forked["parentUuid"] = newParent;
            forked["logicalParentUuid"] = newLogicalParent;
            forked["sessionId"] = forkedSessionId;
            forked["timestamp"] = timestamp;
            forked["isSidechain"] = false;
            forked["forkedFrom"] = new JsonObject
            {
                ["sessionId"] = sessionId,
                ["messageUuid"] = origUuid,
            };

            foreach (var leak in new[] { "teamName", "agentName", "slug", "sourceToolAssistantUUID" })
                forked.Remove(leak);

            lines.Add(forked.ToJsonString());
        }

        if (contentReplacements.Count > 0)
        {
            var replArr = new JsonArray();
            foreach (var r in contentReplacements) replArr.Add(r?.DeepClone());
            var crObj = new JsonObject
            {
                ["type"] = "content-replacement",
                ["sessionId"] = forkedSessionId,
                ["replacements"] = replArr,
                ["uuid"] = Guid.NewGuid().ToString(),
                ["timestamp"] = now,
            };
            lines.Add(crObj.ToJsonString());
        }

        var forkTitle = string.IsNullOrWhiteSpace(title) ? null : title!.Trim();
        if (string.IsNullOrEmpty(forkTitle))
            forkTitle = $"{deriveTitle() ?? "Forked session"} (fork)";

        var titleObj = new JsonObject
        {
            ["type"] = "custom-title",
            ["sessionId"] = forkedSessionId,
            ["customTitle"] = forkTitle,
            ["uuid"] = Guid.NewGuid().ToString(),
            ["timestamp"] = now,
        };
        lines.Add(titleObj.ToJsonString());

        return (forkedSessionId, lines);
    }

    private static string? DeriveTitleFromEntries(IReadOnlyList<SessionStoreEntry> raw)
    {
        string? custom = null;
        string? ai = null;
        foreach (var e in raw)
        {
            var obj = SessionSummary.EntryToJsonObject(e);
            var ct = StringOf(obj, "customTitle");
            if (!string.IsNullOrEmpty(ct)) custom = ct;
            var at = StringOf(obj, "aiTitle");
            if (!string.IsNullOrEmpty(at)) ai = at;
        }
        return custom ?? ai;
    }

    private static SessionStoreEntry MakeExtraEntry(string type, Dictionary<string, JsonNode?> fields)
    {
        var obj = new JsonObject { ["type"] = type };
        foreach (var (k, v) in fields) obj[k] = v;
        return SessionSummary.JsonObjectToEntry(obj);
    }

    private static string? StringOf(JsonObject o, string k)
        => o.TryGetPropertyValue(k, out var v) && v is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : null;

    private static bool BoolOf(JsonObject o, string k)
        => o.TryGetPropertyValue(k, out var v) && v is JsonValue jv && jv.TryGetValue<bool>(out var b) && b;
}
