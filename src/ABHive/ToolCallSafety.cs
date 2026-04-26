using System.Text.RegularExpressions;

namespace ABHive;

public static class ToolCallSafety
{
    public const string SystemPrompt =
        "You are an agentic AI assistant. Execute the task described in the step by selecting appropriate tools. " +
        "Use only structured OpenAI-compatible tool calls via the tool_calls/function API. " +
        "Do not emit pseudo tool-call text such as <tool_call>, <function=...>, XML tags, or markdown wrappers for tool invocation.";

    private static readonly Regex PseudoToolCallPattern = new(
        @"<\s*/?\s*tool_call\b|<\s*function\s*=|<\s*parameter\s*=|</\s*function\s*>|</\s*parameter\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool ContainsPseudoToolCallText(string? content)
    {
        return !string.IsNullOrWhiteSpace(content) && PseudoToolCallPattern.IsMatch(content);
    }

    public static bool ContainsPseudoToolCallText(string? content, string? reasoningContent)
    {
        return ContainsPseudoToolCallText(content) || ContainsPseudoToolCallText(reasoningContent);
    }

    public static string BuildStrictWarning(string source)
    {
        return $"[TOOL CALL WARNING] The assistant emitted pseudo tool-call text in {source}. " +
               "It was not executed for safety. Ask the model to reissue the action using structured tool_calls only.";
    }

    public static string BuildAutoCorrectionNotice(string source)
    {
        return $"[TOOL CALL WARNING] Auto-correction sent to the model in {source}: structured tool_calls required; pseudo XML tool tags are forbidden.";
    }

    public const string RetrySystemInstruction =
        "Policy enforcement: do not emit pseudo tool-call text (for example <tool_call>, <function=...>, <parameter=...>, XML, or markdown wrappers). " +
        "Use only structured OpenAI-compatible tool_calls/function API entries for tool use.";

    public const string RetryInstruction =
        "Reissue the same action using structured tool_calls only. Do not emit <tool_call>, <function=...>, XML tags, or markdown wrappers.";
}
