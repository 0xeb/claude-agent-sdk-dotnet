// MCP Prompts Example - In-process MCP server with prompts
// This example demonstrates how to create an MCP server that provides prompt templates.

using System.Text.Json;
using Claude.AgentSdk;
using Claude.AgentSdk.Mcp;

Console.WriteLine("Claude Agent SDK for .NET - MCP Prompts Example");
Console.WriteLine("================================================\n");

// Create MCP server handlers with prompts
var handlers = new McpServerHandlers
{
    // List available prompts
    ListPrompts = ct => Task.FromResult<IReadOnlyList<McpPromptDefinition>>(
    [
        new McpPromptDefinition
        {
            Name = "code_review",
            Description = "Generate a code review prompt for the given code",
            Arguments =
            [
                new McpPromptArgument
                {
                    Name = "language",
                    Description = "Programming language of the code",
                    Required = true
                },
                new McpPromptArgument
                {
                    Name = "code",
                    Description = "The code to review",
                    Required = true
                }
            ]
        },
        new McpPromptDefinition
        {
            Name = "explain_concept",
            Description = "Explain a programming concept at a specified level",
            Arguments =
            [
                new McpPromptArgument
                {
                    Name = "concept",
                    Description = "The concept to explain",
                    Required = true
                },
                new McpPromptArgument
                {
                    Name = "level",
                    Description = "Expertise level (beginner, intermediate, advanced)",
                    Required = false
                }
            ]
        }
    ]),

    // Get a specific prompt
    GetPrompt = (name, args, ct) =>
    {
        args ??= new Dictionary<string, string>();

        var result = name switch
        {
            "code_review" => new McpPromptResult
            {
                Description = $"Code review for {args.GetValueOrDefault("language", "unknown")} code",
                Messages =
                [
                    new McpPromptMessage
                    {
                        Role = "user",
                        Content = new McpContent
                        {
                            Type = "text",
                            Text = $"Please review the following {args.GetValueOrDefault("language", "")} code:\n\n```{args.GetValueOrDefault("language", "")}\n{args.GetValueOrDefault("code", "")}\n```\n\nProvide feedback on:\n1. Code quality and style\n2. Potential bugs or issues\n3. Performance considerations\n4. Suggestions for improvement"
                        }
                    }
                ]
            },
            "explain_concept" => new McpPromptResult
            {
                Description = $"Explanation of {args.GetValueOrDefault("concept", "concept")}",
                Messages =
                [
                    new McpPromptMessage
                    {
                        Role = "user",
                        Content = new McpContent
                        {
                            Type = "text",
                            Text = $"Please explain the concept of '{args.GetValueOrDefault("concept", "")}' at a {args.GetValueOrDefault("level", "intermediate")} level."
                        }
                    }
                ]
            },
            _ => throw new NotSupportedException($"Unknown prompt: {name}")
        };

        Console.WriteLine($"[MCP] Prompt '{name}' requested");
        return Task.FromResult(result);
    }
};

// Create SDK MCP server configuration
var mcpConfig = new McpSdkServerConfig
{
    Name = "prompts",
    Handlers = handlers
};

// Create options with the MCP server
var options = new ClaudeAgentOptions
{
    McpServers = new Dictionary<string, object>
    {
        ["prompts"] = mcpConfig
    },
    SystemPrompt = "You have access to an MCP prompt server. Use the available prompts to structure your responses."
};

Console.WriteLine("MCP Prompts server configured with the following prompts:");
Console.WriteLine("  - code_review: Generate code review prompts");
Console.WriteLine("  - explain_concept: Explain programming concepts\n");

Console.WriteLine("This example shows how to configure MCP prompts.");
Console.WriteLine("The prompts would be available to Claude when requested.\n");

// Show the prompt definitions
Console.WriteLine("Available prompts:");
var prompts = await handlers.ListPrompts!(CancellationToken.None);
foreach (var prompt in prompts)
{
    Console.WriteLine($"\n  {prompt.Name}: {prompt.Description}");
    if (prompt.Arguments != null)
    {
        Console.WriteLine("    Arguments:");
        foreach (var arg in prompt.Arguments)
        {
            var req = arg.Required ? " (required)" : "";
            Console.WriteLine($"      - {arg.Name}: {arg.Description}{req}");
        }
    }
}

// Demonstrate getting a prompt
Console.WriteLine("\n\nDemonstrating prompt retrieval...");
var codeReviewPrompt = await handlers.GetPrompt!(
    "code_review",
    new Dictionary<string, string>
    {
        ["language"] = "csharp",
        ["code"] = "public int Add(int a, int b) => a + b;"
    },
    CancellationToken.None
);

Console.WriteLine($"\nGenerated prompt for 'code_review':");
Console.WriteLine($"  Description: {codeReviewPrompt.Description}");
Console.WriteLine($"  Message preview: {codeReviewPrompt.Messages[0].Content.Text?[..Math.Min(100, codeReviewPrompt.Messages[0].Content.Text?.Length ?? 0)]}...");

Console.WriteLine("\nExample complete.");
