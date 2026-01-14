namespace Claude.AgentSdk.Builders;

/// <summary>
/// Builder for configuring agents.
/// </summary>
public sealed class AgentsBuilder
{
    private readonly Dictionary<string, AgentDefinition> _agents = [];

    /// <summary>Add an agent definition.</summary>
    public AgentsBuilder Add(string name, string description, string prompt, IReadOnlyList<string>? tools = null, string? model = null)
    {
        _agents[name] = new AgentDefinition(description, prompt, tools, model);
        return this;
    }

    /// <summary>Add an agent definition with tools.</summary>
    public AgentsBuilder Add(string name, string description, string prompt, params string[] tools)
    {
        _agents[name] = new AgentDefinition(description, prompt, tools.Length > 0 ? tools : null);
        return this;
    }

    internal IReadOnlyDictionary<string, AgentDefinition> Build()
    {
        return _agents.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
}
