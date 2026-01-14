namespace Claude.AgentSdk.Builders;

/// <summary>
/// Fluent builder for configuring hooks.
/// </summary>
/// <example>
/// <code>
/// .Hooks(h => h
///     .PreToolUse("Bash", CheckBashCommand)
///     .PostToolUse("*", LogToolOutput)
///     .OnStop(HandleStop))
/// </code>
/// </example>
public sealed class HooksBuilder
{
    private readonly Dictionary<HookEvent, List<HookMatcher>> _hooks = [];

    /// <summary>Add a PreToolUse hook.</summary>
    public HooksBuilder PreToolUse(string matcher, HookCallback callback, double? timeout = null)
    {
        AddHook(HookEvent.PreToolUse, matcher, callback, timeout);
        return this;
    }

    /// <summary>Add a PreToolUse hook with multiple callbacks.</summary>
    public HooksBuilder PreToolUse(string matcher, params HookCallback[] callbacks)
    {
        AddHook(HookEvent.PreToolUse, matcher, callbacks);
        return this;
    }

    /// <summary>Add a PostToolUse hook.</summary>
    public HooksBuilder PostToolUse(string matcher, HookCallback callback, double? timeout = null)
    {
        AddHook(HookEvent.PostToolUse, matcher, callback, timeout);
        return this;
    }

    /// <summary>Add a PostToolUse hook with multiple callbacks.</summary>
    public HooksBuilder PostToolUse(string matcher, params HookCallback[] callbacks)
    {
        AddHook(HookEvent.PostToolUse, matcher, callbacks);
        return this;
    }

    /// <summary>Add a UserPromptSubmit hook.</summary>
    public HooksBuilder UserPromptSubmit(HookCallback callback, double? timeout = null)
    {
        AddHook(HookEvent.UserPromptSubmit, null, callback, timeout);
        return this;
    }

    /// <summary>Add a Stop hook.</summary>
    public HooksBuilder OnStop(HookCallback callback, double? timeout = null)
    {
        AddHook(HookEvent.Stop, null, callback, timeout);
        return this;
    }

    /// <summary>Add a SubagentStop hook.</summary>
    public HooksBuilder OnSubagentStop(HookCallback callback, double? timeout = null)
    {
        AddHook(HookEvent.SubagentStop, null, callback, timeout);
        return this;
    }

    /// <summary>Add a PreCompact hook.</summary>
    public HooksBuilder PreCompact(HookCallback callback, double? timeout = null)
    {
        AddHook(HookEvent.PreCompact, null, callback, timeout);
        return this;
    }

    /// <summary>Add a hook for any event.</summary>
    public HooksBuilder On(HookEvent hookEvent, string? matcher, HookCallback callback, double? timeout = null)
    {
        AddHook(hookEvent, matcher, callback, timeout);
        return this;
    }

    private void AddHook(HookEvent hookEvent, string? matcher, HookCallback callback, double? timeout = null)
    {
        if (!_hooks.TryGetValue(hookEvent, out var list))
        {
            list = [];
            _hooks[hookEvent] = list;
        }
        list.Add(new HookMatcher(matcher, [callback], timeout));
    }

    private void AddHook(HookEvent hookEvent, string? matcher, HookCallback[] callbacks)
    {
        if (!_hooks.TryGetValue(hookEvent, out var list))
        {
            list = [];
            _hooks[hookEvent] = list;
        }
        list.Add(new HookMatcher(matcher, callbacks.ToList()));
    }

    internal IReadOnlyDictionary<HookEvent, IReadOnlyList<HookMatcher>> Build()
    {
        return _hooks.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<HookMatcher>)kvp.Value.ToList()
        );
    }
}
