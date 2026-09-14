// Claude Agent SDK for .NET — Incremental session-summary derivation for SessionStore adapters.
// Reference: reference/claude-agent-sdk-python/src/claude_agent_sdk/_internal/session_summary.py

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Claude.AgentSdk.Sessions;

/// <summary>
/// Incremental session-summary derivation for <see cref="ISessionStore"/>
/// adapters. <see cref="FoldSessionSummary"/> lets a store maintain a
/// per-session <see cref="SessionSummaryEntry"/> sidecar incrementally inside
/// <see cref="ISessionStore.AppendAsync"/> so
/// <see cref="ISessionStore.ListSessionSummariesAsync"/> can fetch all
/// metadata in a single call.
/// </summary>
public static class SessionSummary
{
    // Regex patterns shared with sessions.py — auto-generated/system message
    // skip patterns and the <command-name> extractor.
    internal static readonly Regex SkipFirstPromptPattern = new(
        @"^(?:<local-command-stdout>|<session-start-hook>|<tick>|<goal>|" +
        @"\[Request interrupted by user[^\]]*\]|" +
        @"\s*<ide_opened_file>[\s\S]*</ide_opened_file>\s*$|" +
        @"\s*<ide_selection>[\s\S]*</ide_selection>\s*$)",
        RegexOptions.Compiled);

    internal static readonly Regex CommandNameRegex =
        new(@"<command-name>(.*?)</command-name>", RegexOptions.Compiled);

    private static readonly (string Src, string Dst)[] LastWinsFields = new[]
    {
        ("customTitle", "custom_title"),
        ("aiTitle", "ai_title"),
        ("lastPrompt", "last_prompt"),
        ("summary", "summary_hint"),
        ("gitBranch", "git_branch"),
    };

    /// <summary>
    /// Parse an ISO-8601 timestamp to Unix epoch milliseconds; return
    /// <c>null</c> on failure.
    /// </summary>
    public static long? IsoToEpochMs(string? ts)
    {
        if (string.IsNullOrEmpty(ts)) return null;
        if (DateTimeOffset.TryParse(ts, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dt))
        {
            return dt.ToUnixTimeMilliseconds();
        }
        return null;
    }

    private static IEnumerable<string> EntryTextBlocks(JsonObject entry)
    {
        if (!entry.TryGetPropertyValue("message", out var msgNode) || msgNode is not JsonObject msg)
            yield break;
        if (!msg.TryGetPropertyValue("content", out var contentNode))
            yield break;

        if (contentNode is JsonValue jv && jv.TryGetValue<string>(out var s))
        {
            yield return s;
            yield break;
        }
        if (contentNode is JsonArray arr)
        {
            foreach (var block in arr)
            {
                if (block is JsonObject bo
                    && bo.TryGetPropertyValue("type", out var t)
                    && t is JsonValue tv && tv.TryGetValue<string>(out var ts2) && ts2 == "text"
                    && bo.TryGetPropertyValue("text", out var txt)
                    && txt is JsonValue txv && txv.TryGetValue<string>(out var text))
                {
                    yield return text;
                }
            }
        }
    }

    private static void FoldFirstPrompt(JsonObject data, JsonObject entry)
    {
        if (BoolOf(data, "first_prompt_locked")) return;
        if (StringOf(entry, "type") != "user") return;
        if (BoolOf(entry, "isMeta") || BoolOf(entry, "isCompactSummary")) return;

        // Skip tool_result-carrying user messages.
        if (entry.TryGetPropertyValue("message", out var msgNode)
            && msgNode is JsonObject msg
            && msg.TryGetPropertyValue("content", out var contentNode)
            && contentNode is JsonArray arr)
        {
            foreach (var b in arr)
            {
                if (b is JsonObject bo
                    && bo.TryGetPropertyValue("type", out var tn)
                    && tn is JsonValue tv && tv.TryGetValue<string>(out var ts) && ts == "tool_result")
                {
                    return;
                }
            }
        }

        foreach (var raw in EntryTextBlocks(entry))
        {
            var result = raw.Replace("\n", " ").Trim();
            if (string.IsNullOrEmpty(result)) continue;

            var cmdMatch = CommandNameRegex.Match(result);
            if (cmdMatch.Success)
            {
                if (!data.ContainsKey("command_fallback"))
                    data["command_fallback"] = cmdMatch.Groups[1].Value;
                continue;
            }
            if (SkipFirstPromptPattern.IsMatch(result)) continue;
            if (result.Length > 200)
                result = result[..200].TrimEnd() + "\u2026";
            data["first_prompt"] = result;
            data["first_prompt_locked"] = true;
            return;
        }
    }

    /// <summary>
    /// Fold a batch of appended entries into the running summary for
    /// <paramref name="key"/>. Mirrors Python <c>fold_session_summary</c>.
    /// Do not call for keys with a <see cref="SessionKey.Subpath"/> —
    /// subagent transcripts must not contribute to the main summary.
    /// <para>
    /// <c>mtime</c> is NOT touched here; the adapter stamps it after
    /// persisting (storage write time). For a new session (<paramref name="prev"/>
    /// is null) the returned mtime is 0 as a placeholder.
    /// </para>
    /// </summary>
    public static SessionSummaryEntry FoldSessionSummary(
        SessionSummaryEntry? prev,
        SessionKey key,
        IReadOnlyList<SessionStoreEntry> entries)
    {
        JsonObject dataObj;
        long mtime;
        string sessionId;
        if (prev is not null)
        {
            sessionId = prev.SessionId;
            mtime = prev.Mtime;
            dataObj = prev.Data.ValueKind == JsonValueKind.Object
                ? (JsonObject)JsonNode.Parse(prev.Data.GetRawText())!
                : new JsonObject();
        }
        else
        {
            sessionId = key.SessionId;
            mtime = 0;
            dataObj = new JsonObject();
        }

        foreach (var raw in entries)
        {
            var entry = EntryToJsonObject(raw);
            var ms = IsoToEpochMs(StringOf(entry, "timestamp"));

            if (!dataObj.ContainsKey("is_sidechain"))
                dataObj["is_sidechain"] = BoolOf(entry, "isSidechain");
            if (!dataObj.ContainsKey("created_at") && ms is not null)
                dataObj["created_at"] = ms.Value;

            if (!dataObj.ContainsKey("cwd"))
            {
                var cwd = StringOf(entry, "cwd");
                if (!string.IsNullOrEmpty(cwd))
                    dataObj["cwd"] = cwd;
            }

            FoldFirstPrompt(dataObj, entry);

            foreach (var (src, dst) in LastWinsFields)
            {
                var v = StringOf(entry, src);
                if (v is not null) dataObj[dst] = v;
            }

            if (StringOf(entry, "type") == "tag")
            {
                var tagVal = StringOf(entry, "tag");
                if (!string.IsNullOrEmpty(tagVal))
                    dataObj["tag"] = tagVal;
                else
                    dataObj.Remove("tag");
            }
        }

        using var doc = JsonDocument.Parse(dataObj.ToJsonString());
        return new SessionSummaryEntry
        {
            SessionId = sessionId,
            Mtime = mtime,
            Data = doc.RootElement.Clone(),
        };
    }

    /// <summary>
    /// Convert a <see cref="SessionSummaryEntry"/> to <see cref="SDKSessionInfo"/>.
    /// Returns <c>null</c> for sidechain sessions or sessions with no
    /// extractable summary. Mirrors Python <c>summary_entry_to_sdk_info</c>.
    /// </summary>
    public static SDKSessionInfo? SummaryEntryToSdkInfo(
        SessionSummaryEntry entry,
        string? projectPath)
    {
        if (entry.Data.ValueKind != JsonValueKind.Object) return null;
        var data = (JsonObject)JsonNode.Parse(entry.Data.GetRawText())!;
        if (BoolOf(data, "is_sidechain")) return null;

        string? firstPrompt = BoolOf(data, "first_prompt_locked")
            ? StringOf(data, "first_prompt")
            : StringOf(data, "command_fallback");
        if (string.IsNullOrEmpty(firstPrompt)) firstPrompt = null;

        var customTitle = StringOf(data, "custom_title");
        if (string.IsNullOrEmpty(customTitle)) customTitle = StringOf(data, "ai_title");
        if (string.IsNullOrEmpty(customTitle)) customTitle = null;

        var summary = customTitle
            ?? StringOf(data, "last_prompt")
            ?? StringOf(data, "summary_hint")
            ?? firstPrompt;
        if (string.IsNullOrEmpty(summary)) return null;

        long? createdAt = null;
        if (data.TryGetPropertyValue("created_at", out var ca)
            && ca is JsonValue cv && cv.TryGetValue<long>(out var cal))
            createdAt = cal;

        return new SDKSessionInfo
        {
            SessionId = entry.SessionId,
            Summary = summary,
            LastModified = entry.Mtime,
            FileSize = null,
            CustomTitle = customTitle,
            FirstPrompt = firstPrompt,
            GitBranch = NullIfEmpty(StringOf(data, "git_branch")),
            Cwd = NullIfEmpty(StringOf(data, "cwd")) ?? projectPath,
            Tag = NullIfEmpty(StringOf(data, "tag")),
            CreatedAt = createdAt,
        };
    }

    // ---- small JSON helpers --------------------------------------------

    internal static JsonObject EntryToJsonObject(SessionStoreEntry entry)
    {
        var obj = new JsonObject { ["type"] = entry.Type };
        if (entry.Uuid is not null) obj["uuid"] = entry.Uuid;
        if (entry.Timestamp is not null) obj["timestamp"] = entry.Timestamp;
        if (entry.Extras is not null)
        {
            foreach (var (k, v) in entry.Extras)
            {
                obj[k] = JsonNode.Parse(v.GetRawText());
            }
        }
        return obj;
    }

    internal static SessionStoreEntry JsonObjectToEntry(JsonObject obj)
    {
        string type = "";
        string? uuid = null;
        string? ts = null;
        var extras = new Dictionary<string, JsonElement>();
        foreach (var (k, v) in obj)
        {
            switch (k)
            {
                case "type":
                    type = v is JsonValue tv && tv.TryGetValue<string>(out var s) ? s : "";
                    break;
                case "uuid":
                    if (v is JsonValue uv && uv.TryGetValue<string>(out var us)) uuid = us;
                    break;
                case "timestamp":
                    if (v is JsonValue tsv && tsv.TryGetValue<string>(out var tss)) ts = tss;
                    break;
                default:
                    using (var doc = JsonDocument.Parse(v?.ToJsonString() ?? "null"))
                        extras[k] = doc.RootElement.Clone();
                    break;
            }
        }
        return new SessionStoreEntry
        {
            Type = type,
            Uuid = uuid,
            Timestamp = ts,
            Extras = extras.Count > 0 ? extras : null,
        };
    }

    private static string? StringOf(JsonObject obj, string key)
        => obj.TryGetPropertyValue(key, out var v)
           && v is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : null;

    private static bool BoolOf(JsonObject obj, string key)
        => obj.TryGetPropertyValue(key, out var v)
           && v is JsonValue jv && jv.TryGetValue<bool>(out var b) && b;

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
