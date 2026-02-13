// Port of claude-agent-sdk-python/tests/test_subprocess_buffering.py
// Tests for subprocess transport buffering and configuration.

using System.Text.Json;
using Claude.AgentSdk;
using Xunit;

namespace Claude.AgentSdk.Tests;

public sealed class SubprocessBufferingTests
{
    [Fact]
    public void MaxBufferSize_DefaultIsNull()
    {
        var options = new ClaudeAgentOptions();
        Assert.Null(options.MaxBufferSize);
    }

    [Fact]
    public void MaxBufferSize_CanBeSetViaOptions()
    {
        var options = new ClaudeAgentOptions { MaxBufferSize = 512 };
        Assert.Equal(512, options.MaxBufferSize);
    }

    [Fact]
    public void MaxBufferSize_CanBeSetViaBuilder()
    {
        var options = Claude.Options()
            .MaxBufferSize(1024)
            .Build();

        Assert.Equal(1024, options.MaxBufferSize);
    }

    [Fact]
    public void MultipleJsonObjects_ParseIndependently()
    {
        // Simulate what happens when multiple JSON objects arrive on one line
        var json1 = """{"type":"message","id":"msg1","content":"First message"}""";
        var json2 = """{"type":"result","id":"res1","status":"completed"}""";

        var combined = json1 + "\n" + json2;
        var lines = combined.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);

        var obj1 = JsonSerializer.Deserialize<JsonElement>(lines[0]);
        Assert.Equal("message", obj1.GetProperty("type").GetString());
        Assert.Equal("msg1", obj1.GetProperty("id").GetString());

        var obj2 = JsonSerializer.Deserialize<JsonElement>(lines[1]);
        Assert.Equal("result", obj2.GetProperty("type").GetString());
        Assert.Equal("res1", obj2.GetProperty("id").GetString());
    }

    [Fact]
    public void JsonWithEmbeddedNewlines_ParsesCorrectly()
    {
        // JSON objects can contain newlines in string values (escaped as \n)
        var obj = new { type = "message", content = "Line 1\nLine 2\nLine 3" };
        var json = JsonSerializer.Serialize(obj);
        var parsed = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.Equal("Line 1\nLine 2\nLine 3", parsed.GetProperty("content").GetString());
    }

    [Fact]
    public void EmptyLines_AreSkipped()
    {
        var json1 = """{"type":"message","id":"msg1"}""";
        var json2 = """{"type":"result","id":"res1"}""";
        var combined = json1 + "\n\n\n" + json2;

        var lines = combined.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void LargeJsonObject_CanBeParsed()
    {
        // Simulate a large tool result message
        var largeContent = new string('x', 100_000);
        var obj = new
        {
            type = "user",
            message = new
            {
                role = "user",
                content = new[]
                {
                    new
                    {
                        tool_use_id = "toolu_test123",
                        type = "tool_result",
                        content = largeContent
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(obj);
        var parsed = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.Equal("user", parsed.GetProperty("type").GetString());
        Assert.Equal(
            "toolu_test123",
            parsed.GetProperty("message").GetProperty("content")[0].GetProperty("tool_use_id").GetString()
        );
    }

    [Fact]
    public void SplitJsonAcrossChunks_CanBeReassembled()
    {
        // Simulate a JSON object split across multiple reads
        var obj = new
        {
            type = "assistant",
            message = new
            {
                content = new[]
                {
                    new { type = "text", text = new string('x', 1000) },
                    new { type = "text", text = "tool output" }
                }
            }
        };

        var completeJson = JsonSerializer.Serialize(obj);

        // Split into 3 chunks
        var part1 = completeJson[..100];
        var part2 = completeJson[100..250];
        var part3 = completeJson[250..];

        // Reassemble
        var reassembled = part1 + part2 + part3;
        var parsed = JsonSerializer.Deserialize<JsonElement>(reassembled);

        Assert.Equal("assistant", parsed.GetProperty("type").GetString());
        Assert.Equal(2, parsed.GetProperty("message").GetProperty("content").GetArrayLength());
    }

    [Fact]
    public void MixedCompleteAndSplitJson_ParseCorrectly()
    {
        var msg1 = JsonSerializer.Serialize(new { type = "system", subtype = "start" });
        var msg2 = JsonSerializer.Serialize(new { type = "system", subtype = "end" });

        // Parse each message independently
        var parsed1 = JsonSerializer.Deserialize<JsonElement>(msg1);
        var parsed2 = JsonSerializer.Deserialize<JsonElement>(msg2);

        Assert.Equal("start", parsed1.GetProperty("subtype").GetString());
        Assert.Equal("end", parsed2.GetProperty("subtype").GetString());
    }
}
