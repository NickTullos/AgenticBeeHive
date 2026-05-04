using ABHive.Infrastructure;

namespace ABHive.Application;

public interface IStepConversationService
{
    Task<StepConversationTurnResult> ProcessUserMessageAsync(
        List<ChatMessage> stepContext,
        string userInput,
        CancellationToken ct = default,
        Func<LLMResponse, Task>? onAssistantToolCallResponseAsync = null,
        Func<string, string, Task>? onToolRequestedAsync = null,
        Func<ToolResult, Task>? onToolResultAsync = null,
        Func<string, Task>? onPseudoToolCallWarningAsync = null);
}

public class StepConversationService : IStepConversationService
{
    private readonly AppSettings _settings;
    private readonly ILLMClient _llmClient;
    private readonly IToolExecutor _toolExecutor;

    public StepConversationService(AppSettings settings, ILLMClient llmClient, IToolExecutor toolExecutor)
    {
        _settings = settings;
        _llmClient = llmClient;
        _toolExecutor = toolExecutor;
    }

    public async Task<StepConversationTurnResult> ProcessUserMessageAsync(
        List<ChatMessage> stepContext,
        string userInput,
        CancellationToken ct = default,
        Func<LLMResponse, Task>? onAssistantToolCallResponseAsync = null,
        Func<string, string, Task>? onToolRequestedAsync = null,
        Func<ToolResult, Task>? onToolResultAsync = null,
        Func<string, Task>? onPseudoToolCallWarningAsync = null)
    {
        var updatedContext = new List<ChatMessage>(stepContext);
        EnsureWorkspaceScopeMessage(updatedContext);

        updatedContext.Add(
            new ChatMessage { Role = "user", Content = userInput }
        );

        var finalResponse = new LLMResponse();
        var allToolResults = new List<ToolResult>();
        var pseudoToolCallRetryCount = 0;
        var startedUtc = DateTime.UtcNow;
        var accumulatedPromptTokens = 0;
        var accumulatedCompletionTokens = 0;
        var accumulatedTotalTokens = 0;

        while (true)
        {
            var request = new LLMRequest
            {
                Model = _settings.GetActiveLlmModelName(),
                Messages = updatedContext,
                Tools = GetAvailableTools()
            };

            var llmResponse = await _llmClient.GenerateAsync(request, ct);
            llmResponse ??= new LLMResponse();
            var responseContent = llmResponse.Content ?? string.Empty;
            finalResponse = llmResponse;
            var promptTokens = llmResponse?.Usage?.PromptTokens ?? 0;
            if (promptTokens <= 0)
            {
                promptTokens = EstimateTokens(string.Join('\n', updatedContext.Select(message => message.Content ?? string.Empty)));
            }

            var completionTokens = llmResponse?.Usage?.CompletionTokens ?? 0;
            if (completionTokens <= 0)
            {
                completionTokens = EstimateTokens(llmResponse?.Content);
            }

            var totalTokens = llmResponse?.Usage?.TotalTokens ?? 0;
            if (totalTokens <= 0)
            {
                totalTokens = promptTokens + completionTokens;
            }

            accumulatedPromptTokens += promptTokens;
            accumulatedCompletionTokens += completionTokens;
            accumulatedTotalTokens += totalTokens;

            updatedContext.Add(new ChatMessage
            {
                Role = "assistant",
                Content = responseContent,
                ToolCalls = llmResponse!.ToolCalls is { Count: > 0 } toolCalls ? toolCalls : null
            });

            if (llmResponse.ToolCalls?.Count > 0)
            {
                if (onAssistantToolCallResponseAsync != null)
                {
                    if (string.IsNullOrWhiteSpace(llmResponse.Content))
                    {
                        llmResponse.Content = "(assistant requested tool call; no textual content)";
                    }

                    await onAssistantToolCallResponseAsync(llmResponse);
                }

                if (onToolRequestedAsync != null)
                {
                    foreach (var toolCall in llmResponse.ToolCalls)
                    {
                        await onToolRequestedAsync(
                            toolCall.Name,
                            BuildToolRequestSummary(toolCall));
                    }
                }

                var toolResults = await _toolExecutor.ExecuteToolsAsync(llmResponse.ToolCalls, ct);
                allToolResults.AddRange(toolResults);

                if (onToolResultAsync != null)
                {
                    foreach (var toolCall in llmResponse.ToolCalls)
                    {
                        var result = toolResults.FirstOrDefault(item => item.ToolId == toolCall.Id)
                            ?? new ToolResult
                            {
                                ToolId = toolCall.Id,
                                ToolName = toolCall.Name,
                                RequestSummary = BuildToolRequestSummary(toolCall),
                                Success = false,
                                Error = "Tool result was not returned."
                            };

                        await onToolResultAsync(result);
                    }
                }

                foreach (var toolCall in llmResponse.ToolCalls)
                {
                    var result = toolResults.FirstOrDefault(t => t.ToolId == toolCall.Id);
                    var toolContent = result?.Success ?? false
                        ? (string.IsNullOrWhiteSpace(result?.Output)
                            ? "(tool executed successfully; no output)"
                            : result!.Output)
                        : $"Error: {result?.Error}";
                    updatedContext.Add(new ChatMessage
                    {
                        Role = "tool",
                        Name = toolCall.Name,
                        ToolCallId = toolCall.Id,
                        Content = toolContent
                    });
                }

                continue;
            }

            if (ToolCallSafety.ContainsPseudoToolCallText(llmResponse.Content, llmResponse.ReasoningContent))
            {
                if (onPseudoToolCallWarningAsync != null)
                {
                    await onPseudoToolCallWarningAsync(ToolCallSafety.BuildStrictWarning("step conversation"));
                }

                if (pseudoToolCallRetryCount == 0)
                {
                    if (onPseudoToolCallWarningAsync != null)
                    {
                        await onPseudoToolCallWarningAsync(ToolCallSafety.BuildAutoCorrectionNotice("step conversation"));
                    }

                    pseudoToolCallRetryCount++;
                    updatedContext.Add(new ChatMessage
                    {
                        Role = "system",
                        Content = ToolCallSafety.RetrySystemInstruction
                    });
                    updatedContext.Add(new ChatMessage
                    {
                        Role = "user",
                        Content = ToolCallSafety.RetryInstruction
                    });
                    continue;
                }
            }

            break;
        }

        return new StepConversationTurnResult
        {
            UpdatedStepContext = updatedContext,
            FinalResponse = finalResponse,
            ToolResults = allToolResults,
            DurationMs = (long)(DateTime.UtcNow - startedUtc).TotalMilliseconds,
            PromptTokens = accumulatedPromptTokens,
            CompletionTokens = accumulatedCompletionTokens,
            TotalTokens = accumulatedTotalTokens
        };
    }

    private static int EstimateTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));
    }

    private void EnsureWorkspaceScopeMessage(List<ChatMessage> messages)
    {
        messages.RemoveAll(message =>
            string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase) &&
            (message.Content?.StartsWith(WorkspaceContext.ScopeMessageMarker, StringComparison.Ordinal) ?? false));

        messages.Insert(0, new ChatMessage
        {
            Role = "system",
            Content = WorkspaceContext.BuildScopeMessage(_settings)
        });
    }

    private static string BuildToolRequestSummary(ToolCall toolCall)
    {
        return toolCall.Name switch
        {
            "execute_bash" => TryExtractJsonString(toolCall.Arguments, "command"),
            "fetch_web_content" => TryExtractJsonString(toolCall.Arguments, "url"),
            _ => string.IsNullOrWhiteSpace(toolCall.Arguments) ? string.Empty : toolCall.Arguments
        };
    }

    private static string TryExtractJsonString(string json, string propertyName)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty(propertyName, out var value))
            {
                return value.GetString() ?? string.Empty;
            }
        }
        catch
        {
            // Fall back to raw arguments.
        }

        return string.IsNullOrWhiteSpace(json) ? string.Empty : json;
    }

    private List<ToolDefinition> GetAvailableTools()
    {
        var availableTools = new List<ToolDefinition>();

        foreach (var config in _settings.ToolConfigs.Values)
        {
            if (!config.Enabled)
            {
                Console.WriteLine($"[WorkflowOrchestrator] Tool '{config.Name}' is disabled");
                continue;
            }

            switch (config.Name)
            {
                case "Bash":
                    availableTools.Add(new ToolDefinition
                    {
                        Function = new FunctionObject
                        {
                            Name = "execute_bash",
                            Description = "Execute a bash command. Arguments: {\"command\": \"your command here\"}",
                            Parameters = new Dictionary<string, object>
                            {
                                ["type"] = "object",
                                ["properties"] = new Dictionary<string, object>
                                {
                                    ["command"] = new Dictionary<string, string> { ["type"] = "string" }
                                },
                                ["required"] = new List<string> { "command" }
                            }
                        }
                    });
                    break;
                case "WebFetch":
                    availableTools.Add(new ToolDefinition
                    {
                        Function = new FunctionObject
                        {
                            Name = "fetch_web_content",
                            Description = "Fetch content from a URL. Arguments: {\"url\": \"https://example.com\"}",
                            Parameters = new Dictionary<string, object>
                            {
                                ["type"] = "object",
                                ["properties"] = new Dictionary<string, object>
                                {
                                    ["url"] = new Dictionary<string, string> { ["type"] = "string" }
                                },
                                ["required"] = new List<string> { "url" }
                            }
                        }
                    });
                    break;
            }
        }

        return availableTools;
    }
}

public class StepConversationTurnResult
{
    public List<ChatMessage> UpdatedStepContext { get; set; } = new();
    public LLMResponse FinalResponse { get; set; } = new();
    public List<ToolResult> ToolResults { get; set; } = new();
    public long DurationMs { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}
