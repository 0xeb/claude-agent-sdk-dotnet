using System.Text.Json;
using Claude.AgentSdk;
using Claude.AgentSdk.Internal;
using Xunit;

namespace Claude.AgentSdk.Tests;

public class MessageParserTests
{
    [Fact]
    public void Parse_UserMessage_WithStringContent()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""
        {
            "type": "user",
            "message": {
                "role": "user",
                "content": "Hello, Claude!"
            }
        }
        """);

        var message = MessageParser.Parse(json);

        Assert.IsType<UserMessage>(message);
        var userMessage = (UserMessage)message;
        Assert.Equal("Hello, Claude!", userMessage.GetTextContent());
    }

    [Fact]
    public void Parse_AssistantMessage_WithTextBlock()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""
        {
            "type": "assistant",
            "message": {
                "role": "assistant",
                "model": "claude-3-opus",
                "content": [
                    {
                        "type": "text",
                        "text": "Hello! How can I help you?"
                    }
                ]
            }
        }
        """);

        var message = MessageParser.Parse(json);

        Assert.IsType<AssistantMessage>(message);
        var assistantMessage = (AssistantMessage)message;
        Assert.Equal("claude-3-opus", assistantMessage.Model);
        Assert.Single(assistantMessage.Content);
        Assert.IsType<TextBlock>(assistantMessage.Content[0]);
        Assert.Equal("Hello! How can I help you?", ((TextBlock)assistantMessage.Content[0]).Text);
    }

    [Fact]
    public void Parse_AssistantMessage_WithToolUseBlock()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""
        {
            "type": "assistant",
            "message": {
                "role": "assistant",
                "model": "claude-3-opus",
                "content": [
                    {
                        "type": "tool_use",
                        "id": "tool_123",
                        "name": "Read",
                        "input": {"file_path": "/tmp/test.txt"}
                    }
                ]
            }
        }
        """);

        var message = MessageParser.Parse(json);

        Assert.IsType<AssistantMessage>(message);
        var assistantMessage = (AssistantMessage)message;
        Assert.Single(assistantMessage.Content);
        Assert.IsType<ToolUseBlock>(assistantMessage.Content[0]);
        var toolUse = (ToolUseBlock)assistantMessage.Content[0];
        Assert.Equal("tool_123", toolUse.Id);
        Assert.Equal("Read", toolUse.Name);
    }

    [Fact]
    public void Parse_ResultMessage()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""
        {
            "type": "result",
            "subtype": "success",
            "duration_ms": 1234,
            "duration_api_ms": 1000,
            "is_error": false,
            "num_turns": 3,
            "session_id": "session_abc",
            "total_cost_usd": 0.05
        }
        """);

        var message = MessageParser.Parse(json);

        Assert.IsType<ResultMessage>(message);
        var resultMessage = (ResultMessage)message;
        Assert.Equal("success", resultMessage.Subtype);
        Assert.Equal(1234, resultMessage.DurationMs);
        Assert.Equal(1000, resultMessage.DurationApiMs);
        Assert.False(resultMessage.IsError);
        Assert.Equal(3, resultMessage.NumTurns);
        Assert.Equal("session_abc", resultMessage.SessionId);
        Assert.Equal(0.05m, resultMessage.TotalCostUsd);
    }

    [Fact]
    public void Parse_SystemMessage()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""
        {
            "type": "system",
            "subtype": "init",
            "data": {"key": "value"}
        }
        """);

        var message = MessageParser.Parse(json);

        Assert.IsType<SystemMessage>(message);
        var systemMessage = (SystemMessage)message;
        Assert.Equal("init", systemMessage.Subtype);
    }

    [Fact]
    public void Parse_ThrowsOnMissingType()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""
        {
            "message": "no type field"
        }
        """);

        Assert.Throws<MessageParseException>(() => MessageParser.Parse(json));
    }

    [Fact]
    public void Parse_ThrowsOnUnknownType()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("""
        {
            "type": "unknown_type"
        }
        """);

        Assert.Throws<MessageParseException>(() => MessageParser.Parse(json));
    }
}
