// Claude Agent SDK for .NET
// Port of claude-agent-sdk-python/_internal/message_parser.py

using System.Text.Json;

namespace Claude.AgentSdk.Internal;

/// <summary>
/// Parser for CLI output messages into typed Message objects.
/// </summary>
internal static class MessageParser
{
    /// <summary>
    /// Parse message from CLI output into typed Message objects.
    /// </summary>
    /// <param name="data">Raw message JSON from CLI output.</param>
    /// <returns>Parsed Message object.</returns>
    /// <exception cref="MessageParseException">If parsing fails or message type is unrecognized.</exception>
    public static Message Parse(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            throw new MessageParseException(
                $"Invalid message data type (expected object, got {data.ValueKind})",
                data
            );
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
            _ => throw new MessageParseException($"Unknown message type: {messageType}", data)
        };
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
                Error = error
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
            return new SystemMessage
            {
                Subtype = data.GetProperty("subtype").GetString()!,
                Data = data.Clone()
            };
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
                StructuredOutput = data.TryGetProperty("structured_output", out var so) ? so.Clone() : null
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
}
