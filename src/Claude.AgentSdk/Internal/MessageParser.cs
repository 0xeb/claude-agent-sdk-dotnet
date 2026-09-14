// Claude Agent SDK for .NET
// Port of claude-agent-sdk-python/_internal/message_parser.py

using System.Diagnostics;
using System.Text.Json;

namespace Claude.AgentSdk.Internal;

/// <summary>
/// Parser for CLI output messages into typed Message objects.
/// </summary>
internal static class MessageParser
{
    /// <summary>
    /// Parse message from CLI output into typed Message objects.
    /// Returns <c>null</c> for unknown top-level message types so newer CLI
    /// versions don't break older SDK versions. Python commit 146e3d6.
    /// </summary>
    /// <param name="data">Raw message JSON from CLI output.</param>
    /// <returns>Parsed Message object, or <c>null</c> if the message type is unknown.</returns>
    /// <exception cref="MessageParseException">If parsing fails for a known type.</exception>
    public static Message? ParseOrNull(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            throw new MessageParseException(
                $"Invalid message data type (expected object, got {data.ValueKind})",
                data
            );
        }

        // Hook events arrive as system messages with subtype hook_started/hook_response.
        // Python commit c1182a4.
        if (data.TryGetProperty("type", out var topType) &&
            topType.ValueKind == JsonValueKind.String &&
            topType.GetString() == "system" &&
            data.TryGetProperty("subtype", out var hookSubtype) &&
            hookSubtype.ValueKind == JsonValueKind.String &&
            (hookSubtype.GetString() == "hook_started" || hookSubtype.GetString() == "hook_response"))
        {
            var hookEventName =
                (data.TryGetProperty("hook_event", out var he) && he.ValueKind == JsonValueKind.String ? he.GetString() : null)
                ?? (data.TryGetProperty("hook_name", out var hn) && hn.ValueKind == JsonValueKind.String ? hn.GetString() : null)
                ?? (data.TryGetProperty("hook_event_name", out var hen) && hen.ValueKind == JsonValueKind.String ? hen.GetString() : null)
                ?? string.Empty;
            return new HookEventMessage
            {
                Subtype = hookSubtype.GetString()!,
                Data = data.Clone(),
                HookEventName = hookEventName,
                SessionId = data.TryGetProperty("session_id", out var sid) ? sid.GetString() : null,
                Uuid = data.TryGetProperty("uuid", out var hu) ? hu.GetString() : null
            };
        }

        if (!data.TryGetProperty("type", out var typeElement) ||
            typeElement.ValueKind != JsonValueKind.String)
        {
            throw new MessageParseException("Message missing 'type' field", data);
        }

        var messageType = typeElement.GetString();

        return messageType switch
        {
            "user" => ParseUserMessage(data),
            "assistant" => ParseAssistantMessage(data),
            "system" => ParseSystemMessage(data),
            "result" => ParseResultMessage(data),
            "stream_event" => ParseStreamEvent(data),
            "rate_limit_event" => ParseRateLimitEvent(data),
            _ => LogAndSkipUnknown(messageType)
        };
    }

    private static Message? LogAndSkipUnknown(string? messageType)
    {
        Debug.WriteLine($"[MessageParser] Skipping unknown message type: {messageType}");
        return null;
    }

    /// <summary>
    /// Legacy entry point: parse message and throw on unknown types.
    /// Prefer <see cref="ParseOrNull"/>, which mirrors Python's forward-compatible
    /// behavior of skipping unknown types.
    /// </summary>
    public static Message Parse(JsonElement data)
    {
        var msg = ParseOrNull(data);
        if (msg == null)
        {
            var t = data.TryGetProperty("type", out var te) ? te.GetString() : "<missing>";
            throw new MessageParseException($"Unknown message type: {t}", data);
        }
        return msg;
    }

    private static UserMessage ParseUserMessage(JsonElement data)
    {
        try
        {
            var message = data.GetProperty("message");
            var content = message.GetProperty("content");

            return new UserMessage
            {
                Content = content.Clone(),
                Uuid = data.TryGetProperty("uuid", out var uuid) ? uuid.GetString() : null,
                ParentToolUseId = data.TryGetProperty("parent_tool_use_id", out var pid)
                    ? pid.GetString()
                    : null
            };
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            throw new MessageParseException($"Missing required field in user message: {ex.Message}", data);
        }
    }

    private static AssistantMessage ParseAssistantMessage(JsonElement data)
    {
        try
        {
            var message = data.GetProperty("message");
            var contentArray = message.GetProperty("content");
            var model = message.GetProperty("model").GetString()
                ?? throw new MessageParseException("Missing model in assistant message", data);

            var contentBlocks = new List<ContentBlock>();

            foreach (var block in contentArray.EnumerateArray())
            {
                var blockType = block.GetProperty("type").GetString();
                var contentBlock = blockType switch
                {
                    "text" => (ContentBlock)new TextBlock(block.GetProperty("text").GetString()!),
                    "thinking" => new ThinkingBlock(
                        block.GetProperty("thinking").GetString()!,
                        block.GetProperty("signature").GetString()!
                    ),
                    "tool_use" => new ToolUseBlock(
                        block.GetProperty("id").GetString()!,
                        block.GetProperty("name").GetString()!,
                        block.GetProperty("input").Clone()
                    ),
                    "tool_result" => new ToolResultBlock(
                        block.GetProperty("tool_use_id").GetString()!,
                        block.TryGetProperty("content", out var c) ? c.Clone() : null,
                        block.TryGetProperty("is_error", out var e) ? e.GetBoolean() : null
                    ),
                    // Python commit 6ab97b4: server_tool_use / advisor_tool_result.
                    "server_tool_use" => new ServerToolUseBlock(
                        block.GetProperty("id").GetString()!,
                        block.GetProperty("name").GetString()!,
                        block.GetProperty("input").Clone()
                    ),
                    "advisor_tool_result" => new ServerToolResultBlock(
                        block.GetProperty("tool_use_id").GetString()!,
                        block.GetProperty("content").Clone()
                    ),
                    _ => throw new MessageParseException($"Unknown content block type: {blockType}", data)
                };
                contentBlocks.Add(contentBlock);
            }

            AssistantMessageError? error = null;
            if (message.TryGetProperty("error", out var errorElement) &&
                errorElement.ValueKind == JsonValueKind.String)
            {
                var errorStr = errorElement.GetString();
                error = errorStr switch
                {
                    "authentication_failed" => AssistantMessageError.AuthenticationFailed,
                    "billing_error" => AssistantMessageError.BillingError,
                    "rate_limit" => AssistantMessageError.RateLimit,
                    "invalid_request" => AssistantMessageError.InvalidRequest,
                    "server_error" => AssistantMessageError.ServerError,
                    _ => AssistantMessageError.Unknown
                };
            }

            return new AssistantMessage
            {
                Content = contentBlocks,
                Model = model,
                ParentToolUseId = data.TryGetProperty("parent_tool_use_id", out var pid)
                    ? pid.GetString()
                    : null,
                Error = error,
                // Python commit fc82420: preserve per-turn usage.
                Usage = message.TryGetProperty("usage", out var usage) ? usage.Clone() : null,
                MessageId = message.TryGetProperty("id", out var mid) ? mid.GetString() : null,
                StopReason = message.TryGetProperty("stop_reason", out var sr) ? sr.GetString() : null,
                SessionId = data.TryGetProperty("session_id", out var sid) ? sid.GetString() : null,
                Uuid = data.TryGetProperty("uuid", out var u) ? u.GetString() : null
            };
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            throw new MessageParseException($"Missing required field in assistant message: {ex.Message}", data);
        }
    }

    private static SystemMessage ParseSystemMessage(JsonElement data)
    {
        try
        {
            var subtype = data.GetProperty("subtype").GetString()!;
            var clone = data.Clone();

            // Python commit 9af27d7: task_started / task_progress / task_notification.
            switch (subtype)
            {
                case "task_started":
                    return new TaskStartedMessage
                    {
                        Subtype = subtype,
                        Data = clone,
                        TaskId = data.GetProperty("task_id").GetString()!,
                        Description = data.GetProperty("description").GetString()!,
                        Uuid = data.GetProperty("uuid").GetString()!,
                        SessionId = data.GetProperty("session_id").GetString()!,
                        ToolUseId = data.TryGetProperty("tool_use_id", out var tu) ? tu.GetString() : null,
                        TaskType = data.TryGetProperty("task_type", out var tt) ? tt.GetString() : null
                    };
                case "task_progress":
                    return new TaskProgressMessage
                    {
                        Subtype = subtype,
                        Data = clone,
                        TaskId = data.GetProperty("task_id").GetString()!,
                        Description = data.GetProperty("description").GetString()!,
                        Usage = JsonSerializer.Deserialize<TaskUsage>(data.GetProperty("usage").GetRawText())!,
                        Uuid = data.GetProperty("uuid").GetString()!,
                        SessionId = data.GetProperty("session_id").GetString()!,
                        ToolUseId = data.TryGetProperty("tool_use_id", out var tu2) ? tu2.GetString() : null,
                        LastToolName = data.TryGetProperty("last_tool_name", out var ltn) ? ltn.GetString() : null
                    };
                case "task_notification":
                    var statusStr = data.GetProperty("status").GetString();
                    var status = statusStr switch
                    {
                        "completed" => TaskNotificationStatus.Completed,
                        "failed" => TaskNotificationStatus.Failed,
                        "stopped" => TaskNotificationStatus.Stopped,
                        _ => throw new MessageParseException($"Unknown task_notification status: {statusStr}", data)
                    };
                    return new TaskNotificationMessage
                    {
                        Subtype = subtype,
                        Data = clone,
                        TaskId = data.GetProperty("task_id").GetString()!,
                        Status = status,
                        OutputFile = data.GetProperty("output_file").GetString()!,
                        Summary = data.GetProperty("summary").GetString()!,
                        Uuid = data.GetProperty("uuid").GetString()!,
                        SessionId = data.GetProperty("session_id").GetString()!,
                        ToolUseId = data.TryGetProperty("tool_use_id", out var tu3) ? tu3.GetString() : null,
                        Usage = data.TryGetProperty("usage", out var u) && u.ValueKind != JsonValueKind.Null
                            ? JsonSerializer.Deserialize<TaskUsage>(u.GetRawText())
                            : null
                    };
                case "mirror_error":
                    // Python commit 6e3d54f: SDK-synthesized; never emitted by the CLI directly.
                    SessionKey? key = null;
                    if (data.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.Object)
                    {
                        key = JsonSerializer.Deserialize<SessionKey>(k.GetRawText());
                    }
                    return new MirrorErrorMessage
                    {
                        Subtype = subtype,
                        Data = clone,
                        Key = key,
                        Error = data.TryGetProperty("error", out var er) ? er.GetString() ?? string.Empty : string.Empty
                    };
                default:
                    return new SystemMessage
                    {
                        Subtype = subtype,
                        Data = clone
                    };
            }
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            throw new MessageParseException($"Missing required field in system message: {ex.Message}", data);
        }
    }

    private static ResultMessage ParseResultMessage(JsonElement data)
    {
        try
        {
            DeferredToolUse? deferred = null;
            if (data.TryGetProperty("deferred_tool_use", out var dtu) && dtu.ValueKind == JsonValueKind.Object)
            {
                deferred = new DeferredToolUse(
                    dtu.GetProperty("id").GetString()!,
                    dtu.GetProperty("name").GetString()!,
                    dtu.GetProperty("input").Clone()
                );
            }

            IReadOnlyList<string>? errors = null;
            if (data.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>(errs.GetArrayLength());
                foreach (var item in errs.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        list.Add(item.GetString()!);
                }
                errors = list;
            }

            return new ResultMessage
            {
                Subtype = data.GetProperty("subtype").GetString()!,
                DurationMs = data.GetProperty("duration_ms").GetInt32(),
                DurationApiMs = data.GetProperty("duration_api_ms").GetInt32(),
                IsError = data.GetProperty("is_error").GetBoolean(),
                NumTurns = data.GetProperty("num_turns").GetInt32(),
                SessionId = data.GetProperty("session_id").GetString()!,
                TotalCostUsd = data.TryGetProperty("total_cost_usd", out var cost) && cost.ValueKind == JsonValueKind.Number
                    ? cost.GetDecimal()
                    : null,
                Usage = data.TryGetProperty("usage", out var usage) ? usage.Clone() : null,
                Result = data.TryGetProperty("result", out var result) ? result.GetString() : null,
                StructuredOutput = data.TryGetProperty("structured_output", out var so) ? so.Clone() : null,
                StopReason = data.TryGetProperty("stop_reason", out var sr) ? sr.GetString() : null,
                DeferredToolUse = deferred,
                Errors = errors,
                ApiErrorStatus = data.TryGetProperty("api_error_status", out var aes) && aes.ValueKind == JsonValueKind.Number
                    ? aes.GetInt32()
                    : null,
                Uuid = data.TryGetProperty("uuid", out var uu) ? uu.GetString() : null
            };
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            throw new MessageParseException($"Missing required field in result message: {ex.Message}", data);
        }
    }

    private static StreamEvent ParseStreamEvent(JsonElement data)
    {
        try
        {
            return new StreamEvent
            {
                Uuid = data.GetProperty("uuid").GetString()!,
                SessionId = data.GetProperty("session_id").GetString()!,
                Event = data.GetProperty("event").Clone(),
                ParentToolUseId = data.TryGetProperty("parent_tool_use_id", out var pid)
                    ? pid.GetString()
                    : null
            };
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            throw new MessageParseException($"Missing required field in stream_event message: {ex.Message}", data);
        }
    }

    private static RateLimitEvent ParseRateLimitEvent(JsonElement data)
    {
        try
        {
            var info = data.GetProperty("rate_limit_info");
            var statusStr = info.GetProperty("status").GetString();
            var status = RateLimitEnumHelpers.ParseRateLimitStatus(statusStr)
                ?? throw new MessageParseException($"Unknown rate_limit status: {statusStr}", data);

            RateLimitType? rlType = null;
            if (info.TryGetProperty("rateLimitType", out var rlt) && rlt.ValueKind == JsonValueKind.String)
            {
                rlType = rlt.GetString() switch
                {
                    "five_hour" => RateLimitType.FiveHour,
                    "seven_day" => RateLimitType.SevenDay,
                    "seven_day_opus" => RateLimitType.SevenDayOpus,
                    "seven_day_sonnet" => RateLimitType.SevenDaySonnet,
                    "overage" => RateLimitType.Overage,
                    _ => null
                };
            }

            RateLimitStatus? overageStatus = null;
            if (info.TryGetProperty("overageStatus", out var os) && os.ValueKind == JsonValueKind.String)
                overageStatus = RateLimitEnumHelpers.ParseRateLimitStatus(os.GetString());

            var rli = new RateLimitInfo
            {
                Status = status,
                ResetsAt = info.TryGetProperty("resetsAt", out var ra) && ra.ValueKind == JsonValueKind.Number
                    ? ra.GetInt64() : null,
                RateLimitType = rlType,
                Utilization = info.TryGetProperty("utilization", out var ut) && ut.ValueKind == JsonValueKind.Number
                    ? ut.GetDouble() : null,
                OverageStatus = overageStatus,
                OverageResetsAt = info.TryGetProperty("overageResetsAt", out var ora) && ora.ValueKind == JsonValueKind.Number
                    ? ora.GetInt64() : null,
                OverageDisabledReason = info.TryGetProperty("overageDisabledReason", out var odr) && odr.ValueKind == JsonValueKind.String
                    ? odr.GetString() : null,
                Raw = info.Clone()
            };

            return new RateLimitEvent
            {
                RateLimitInfo = rli,
                Uuid = data.GetProperty("uuid").GetString()!,
                SessionId = data.GetProperty("session_id").GetString()!
            };
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            throw new MessageParseException($"Missing required field in rate_limit_event message: {ex.Message}", data);
        }
    }
}
