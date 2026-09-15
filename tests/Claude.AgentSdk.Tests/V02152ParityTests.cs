// Python SDK v0.2.152 additions.
//
// Concentrates on the three things the parity review flagged as easy to port
// incorrectly, because none of them would fail visibly without a live CLI.

using System;
using System.Collections.Generic;
using System.Text.Json;
using Claude.AgentSdk;
using Xunit;

namespace Claude.AgentSdk.Tests;

public class V02152ParityTests
{
    // TaskStatus.Terminal spans two vocabularies on purpose: task_notification
    // reports the CLI's mapped "stopped", task_updated reports the raw "killed".
    // A task stopped via TaskStop may only ever report "killed", so checking one
    // vocabulary leaves it tracked as active forever.
    [Theory]
    [InlineData("completed", true)]
    [InlineData("failed", true)]
    [InlineData("stopped", true)]  // task_notification form
    [InlineData("killed", true)]   // task_updated form
    [InlineData("pending", false)]
    [InlineData("running", false)]
    [InlineData("paused", false)]
    [InlineData("", false)]
    [InlineData("Completed", false)]  // wire values are case-sensitive
    [InlineData(null, false)]
    public void TerminalTaskStatus_SpansBothVocabularies(string? status, bool expected)
    {
        Assert.Equal(expected, TaskStatus.IsTerminal(status));
    }

    // ModelUsage is passed through verbatim from the CLI's modelUsage field, so
    // its keys are camelCase even though this SDK is otherwise snake_case.
    // Renaming them to match house style would silently stop parsing.
    [Fact]
    public void ModelUsage_UsesCamelCaseWireKeys()
    {
        var usage = new ModelUsage
        {
            InputTokens = 11,
            OutputTokens = 22,
            CacheReadInputTokens = 33,
            CacheCreationInputTokens = 44,
            WebSearchRequests = 5,
            CostUSD = 1.25,
            ContextWindow = 200000,
            MaxOutputTokens = 8192,
        };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(usage));
        var root = doc.RootElement;

        Assert.Equal(11, root.GetProperty("inputTokens").GetInt32());
        Assert.Equal(33, root.GetProperty("cacheReadInputTokens").GetInt32());
        Assert.Equal(5, root.GetProperty("webSearchRequests").GetInt32());
        Assert.Equal(1.25, root.GetProperty("costUSD").GetDouble());
        Assert.Equal(8192, root.GetProperty("maxOutputTokens").GetInt32());

        // No snake_case aliases: a reader relying on them would read nothing.
        Assert.False(root.TryGetProperty("input_tokens", out _));
        Assert.False(root.TryGetProperty("cost_usd", out _));
    }

    [Fact]
    public void ModelUsage_RoundTripsFromWire()
    {
        const string wire = """
            {"inputTokens":7,"outputTokens":8,"cacheReadInputTokens":9,
             "cacheCreationInputTokens":10,"webSearchRequests":1,"costUSD":0.5,
             "contextWindow":1000,"maxOutputTokens":64}
            """;

        var usage = JsonSerializer.Deserialize<ModelUsage>(wire);

        Assert.NotNull(usage);
        Assert.Equal(7, usage!.InputTokens);
        Assert.Equal(9, usage.CacheReadInputTokens);
        Assert.Equal(0.5, usage.CostUSD);
        Assert.Equal(64, usage.MaxOutputTokens);
    }

    // MessageOriginKind is an open set: upstream documents that newer CLIs may
    // emit kinds not listed, and unrecognized ones are "not human". Modelling
    // Kind as an enum would turn a forward-compatible field into a parse failure.
    [Fact]
    public void MessageOriginKind_IsOpenAndUnknownIsNotHuman()
    {
        Assert.True(new MessageOrigin { Kind = MessageOriginKind.Human }.IsHuman);
        Assert.False(new MessageOrigin { Kind = MessageOriginKind.Peer }.IsHuman);

        // A kind this SDK has never heard of: accepted, and not human.
        var future = JsonSerializer.Deserialize<MessageOrigin>(
            """{"kind":"some-kind-from-a-newer-cli"}""");

        Assert.NotNull(future);
        Assert.Equal("some-kind-from-a-newer-cli", future!.Kind);
        Assert.False(future.IsHuman);
    }

    // NormalizeErrors keeps the structured errors and the exception text in
    // agreement: a bare string is tolerated, blanks and non-strings dropped.
    [Fact]
    public void ResultException_NormalizeErrors()
    {
        static IReadOnlyList<string> Norm(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return ResultException.NormalizeErrors(doc.RootElement.Clone());
        }

        Assert.Equal(new[] { "boom", "bang" }, Norm("""["boom","bang"]"""));

        // Older or buggy emitters send a bare string rather than a list.
        Assert.Equal(new[] { "solo" }, Norm("\"solo\""));

        // Blanks, whitespace-only entries and non-strings dropped; survivors trimmed.
        Assert.Equal(new[] { "padded", "kept" },
            Norm("""["  padded  ","","   ",42,null,"kept"]"""));

        Assert.Empty(Norm("{}"));
        Assert.Empty(Norm("null"));
    }

    // ResultException subclasses ProcessException so existing handlers keep working.
    [Fact]
    public void ResultException_IsCatchableAsProcessException()
    {
        // Explicitly an Action: a throw-only lambda converts to any delegate type,
        // so xUnit would otherwise bind the obsolete Func<Task> overload.
        Action throwing = () => throw new ResultException(
            "run failed", "error_max_turns", "api_error", new[] { "overloaded" }, exitCode: 1);

        var thrown = Assert.Throws<ResultException>(throwing);

        // Caught through the base, as an existing handler would.
        ProcessException asBase = thrown;
        Assert.Equal(1, asBase.ExitCode);

        Assert.Equal("error_max_turns", thrown.Subtype);
        Assert.Equal("api_error", thrown.TerminalReason);
        Assert.Equal(new[] { "overloaded" }, thrown.Errors);
    }

    [Fact]
    public void TaskUpdatedMessage_IsASystemMessage()
    {
        using var patch = JsonDocument.Parse("""{"status":"killed"}""");
        var msg = new TaskUpdatedMessage
        {
            Subtype = "task_updated",
            TaskId = "task-1",
            Patch = patch.RootElement.Clone(),
            Status = TaskUpdatedStatus.Killed,
        };

        // Existing code handling SystemMessage must keep matching.
        Assert.IsAssignableFrom<SystemMessage>(msg);
        Assert.Equal("task_updated", msg.Subtype);
        Assert.True(TaskStatus.IsTerminal(msg.Status));
        Assert.Equal("killed", msg.Patch.GetProperty("status").GetString());
    }

    [Fact]
    public void ConversationResetMessage_KeepsConversationAndSessionIdsDistinct()
    {
        var msg = new ConversationResetMessage
        {
            NewConversationId = "conv-2",
            Uuid = "u-1",
            SessionId = "sess-old",
        };

        // Conflating these would mis-key a UI transcript: the new conversation id
        // is not the session id of subsequent messages.
        Assert.NotEqual(msg.NewConversationId, msg.SessionId);
    }
}
