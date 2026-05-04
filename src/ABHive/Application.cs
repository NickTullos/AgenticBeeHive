using ABHive.Infrastructure;
using System.Diagnostics;
using System.Text.Json;

namespace ABHive.Application;

public interface IConsoleOutputFormatter
{
    void ShowStepProgress(int current, int total, Step step);
    string? ShowStepSuccess(StepExecutionResult result, Step step, CancellationToken ct = default);
    void ShowStepFailure(StepExecutionResult result);
}

public interface IWebOutputFormatter
{
    Task ShowStepProgressAsync(int current, int total, Step step);
    Task<string?> ShowStepSuccessAsync(StepExecutionResult result, Step step);
    Task ShowStepFailureAsync(StepExecutionResult result);
}

public interface IWorkflowOrchestrator
{
    Task<WorkflowMetrics> RunAsync(CancellationToken ct = default);
    Task<WorkflowMetrics> RunFromStepAsync(int startStepNumber, WorkflowMetrics? existingMetrics = null, CancellationToken ct = default);
}

public class WorkflowOrchestrator : IWorkflowOrchestrator
{
    private readonly AppSettings _settings;
    private readonly ILLMClient _llmClient;
    private readonly IToolExecutor _toolExecutor;
    private readonly IStepConversationService _stepConversationService;
    private readonly IConsoleOutputFormatter _formatter;
    private readonly IMetricsLogger? _metricsLogger;
    private readonly bool _debugMode;
    
    // Optional callback for sending LLM responses to web
    public Func<string, Task>? SendLlmResponseAsync { get; set; }
    public Func<string, string, Task>? SendLlmResponseWithReasoningAsync { get; set; }
    public Func<string, string, Task>? SendToolRequestAsync { get; set; }
    public Func<ToolResult, Task>? SendToolExecutionAsync { get; set; }
    public Func<int, StepConversationTurnResult, Task>? SendConversationTurnStatsAsync { get; set; }

    public WorkflowOrchestrator(
        AppSettings settings,
        ILLMClient llmClient,
        IToolExecutor toolExecutor,
        IStepConversationService stepConversationService,
        IConsoleOutputFormatter formatter,
        IMetricsLogger? metricsLogger = null,
        bool debugMode = false)
    {
        _settings = settings;
        _llmClient = llmClient;
        _toolExecutor = toolExecutor;
        _stepConversationService = stepConversationService;
        _formatter = formatter;
        _metricsLogger = metricsLogger;
        _debugMode = debugMode;
    }

    public Task<WorkflowMetrics> RunAsync(CancellationToken ct = default)
    {
        return RunInternalAsync(1, null, ct);
    }

    public Task<WorkflowMetrics> RunFromStepAsync(int startStepNumber, WorkflowMetrics? existingMetrics = null, CancellationToken ct = default)
    {
        return RunInternalAsync(startStepNumber, existingMetrics, ct);
    }

    private async Task<WorkflowMetrics> RunInternalAsync(int startStepNumber, WorkflowMetrics? existingMetrics, CancellationToken ct)
    {
        Console.WriteLine("[DEBUG] Starting RunAsync...");
        var steps = await LoadStepsAsync(ct);
        Console.WriteLine($"[DEBUG] Loaded {steps.Count} steps...");
        var metrics = existingMetrics != null
            ? CloneWorkflowMetrics(existingMetrics)
            : new WorkflowMetrics();
        metrics.TotalSteps = steps.Count;
        var stepResults = new List<StepExecutionResult>();
        var boundedStartStep = Math.Clamp(startStepNumber, 1, Math.Max(steps.Count, 1));

        for (int i = boundedStartStep - 1; i < steps.Count; i++)
        {
            var step = steps[i];
            var isTicketIterationStep = IsTicketIterationStep(step);
            
            _formatter.ShowStepProgress(i + 1, steps.Count, step);
            
            try
            {
                if (_debugMode) Console.WriteLine($"[DEBUG] Executing step {i+1}...");
                var result = isTicketIterationStep
                    ? await ExecuteTicketIterationStepAsync(step, ct)
                    : await ExecuteStepAsync(step, ct);
                if (_debugMode) Console.WriteLine($"[DEBUG] Step execution completed with Success={result.Success}");
                
                if (result.Success)
                {
                    if (_debugMode) Console.WriteLine("[DEBUG] Step succeeded!");
                    metrics.SuccessfulSteps++;
                    
                    // Loop to handle multiple user questions after step completion
                    while (true)
                    {
                        var userInput = _formatter.ShowStepSuccess(result, step, ct);
                        
                        // If user pressed Enter without text, exit loop and continue to next step
                        if (string.IsNullOrEmpty(userInput))
                            break;
                        
                        Console.WriteLine($"[USER INPUT]: {userInput}");
                        
                        if (_debugMode)
                        {
                            Console.WriteLine($"[DEBUG] Step Context Size: {result.StepContext.Count} messages");
                        }
                        
                        var turnResult = await _stepConversationService.ProcessUserMessageAsync(
                            result.StepContext,
                            userInput,
                            ct,
                            onAssistantToolCallResponseAsync: PublishAssistantToolCallResponseAsync,
                            onToolRequestedAsync: PublishToolRequestAsync,
                            onToolResultAsync: PublishToolExecutionResultAsync,
                            onPseudoToolCallWarningAsync: PublishPseudoToolCallWarningAsync);
                        result.StepContext = turnResult.UpdatedStepContext;
                        result.LLMResponse = turnResult.FinalResponse;
                        result.ToolResults = turnResult.ToolResults;
                        result.ResponseContent += $"\n\n[LLM ANSWER]\n{turnResult.FinalResponse.Content}";
                        
                        // Display the LLM response to user
                        Console.WriteLine();
                        Console.WriteLine("[LLM RESPONSE]");
                        var responseLines = (turnResult.FinalResponse.Content ?? string.Empty).Split('\n');
                        foreach (var line in responseLines)
                        {
                            Console.WriteLine(line);
                        }
                        
                        await PublishFinalLlmResponseAsync(turnResult.FinalResponse);
                        await PublishConversationTurnStatsAsync(i + 1, turnResult);
                    }

                    if (isTicketIterationStep)
                    {
                        metrics.TicketProgress = result.TicketProgress;
                        if (result.TicketProgress?.RemainingTickets > 0)
                        {
                            metrics.ResumeAtStep = true;
                            metrics.ResumeStepNumber = i + 1;
                            stepResults.Add(result);
                            metrics.TotalTokensUsed += result.LLMResponse?.Usage.TotalTokens ?? 0;
                            metrics.TotalDurationMs += result.DurationMs;
                            break;
                        }
                    }
                }
                else
                {
                    metrics.FailedSteps++;
                    _formatter.ShowStepFailure(result);

                    if (isTicketIterationStep)
                    {
                        metrics.TicketProgress = result.TicketProgress;
                        metrics.ResumeAtStep = true;
                        metrics.ResumeStepNumber = i + 1;
                        stepResults.Add(result);
                        metrics.TotalTokensUsed += result.LLMResponse?.Usage.TotalTokens ?? 0;
                        metrics.TotalDurationMs += result.DurationMs;
                        break;
                    }
                }

                stepResults.Add(result);

                metrics.TotalTokensUsed += result.LLMResponse?.Usage.TotalTokens ?? 0;
                metrics.TotalDurationMs += result.DurationMs;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing step {step.Id}: {ex.Message}");
                metrics.FailedSteps++;
                
                var errorResult = new StepExecutionResult
                {
                    StepId = step.Id,
                    StartTime = DateTime.UtcNow,
                    EndTime = DateTime.UtcNow,
                    Success = false,
                    Error = ex.Message
                };
                stepResults.Add(errorResult);
                
                _formatter.ShowStepFailure(errorResult);
            }
        }

        metrics.AverageStepDurationMs = metrics.SuccessfulSteps + metrics.FailedSteps > 0
            ? (double)metrics.TotalDurationMs / (metrics.SuccessfulSteps + metrics.FailedSteps)
            : 0;

        if (_metricsLogger != null)
        {
            await _metricsLogger.LogWorkflowMetricsAsync(metrics, stepResults);
        }

        return metrics;
    }

    private static WorkflowMetrics CloneWorkflowMetrics(WorkflowMetrics metrics)
    {
        return new WorkflowMetrics
        {
            TotalSteps = metrics.TotalSteps,
            SuccessfulSteps = metrics.SuccessfulSteps,
            FailedSteps = metrics.FailedSteps,
            TotalDurationMs = metrics.TotalDurationMs,
            AverageStepDurationMs = metrics.AverageStepDurationMs,
            TotalTokensUsed = metrics.TotalTokensUsed,
            ResumeAtStep = metrics.ResumeAtStep,
            ResumeStepNumber = metrics.ResumeStepNumber,
            TicketProgress = metrics.TicketProgress
        };
    }

    private StepConfig? LoadStepConfig(string mdFilePath)
    {
        var configPath = GetConfigFilePath(mdFilePath);
        Console.WriteLine($"[DEBUG] Checking config: {configPath}, exists: {File.Exists(configPath)}");
        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllTextAsync(configPath).Result;
                return System.Text.Json.JsonSerializer.Deserialize<StepConfig>(json) 
                    ?? new StepConfig { AutoContinue = false };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StepLoader] Failed to parse config file: {ex.Message}");
            }
        }
        return null;
    }

    private static string GetConfigFilePath(string mdFilePath)
    {
        var dir = Path.GetDirectoryName(mdFilePath);
        var name = Path.GetFileNameWithoutExtension(mdFilePath);
        return Path.Combine(dir ?? "", $"{name}.config");
    }

    private static string GetMetadataFilePath(string mdFilePath)
    {
        var dir = Path.GetDirectoryName(mdFilePath);
        var name = Path.GetFileNameWithoutExtension(mdFilePath);
        return Path.Combine(dir ?? "", $"{name}.json");
    }

    private StepMetadata LoadStepMetadata(string mdFilePath)
    {
        var metadataPath = GetMetadataFilePath(mdFilePath);
        if (!File.Exists(metadataPath))
        {
            return new StepMetadata();
        }

        try
        {
            var json = File.ReadAllText(metadataPath);
            var metadata = JsonSerializer.Deserialize<StepMetadata>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            }) ?? new StepMetadata();

            metadata.ExecutionMode = NormalizeExecutionMode(metadata.ExecutionMode);
            metadata.MaxRetriesPerTicket = metadata.MaxRetriesPerTicket > 0 ? metadata.MaxRetriesPerTicket : 3;
            metadata.TicketSource = WorkspaceContext.ApplyPathTokens(
                string.IsNullOrWhiteSpace(metadata.TicketSource) ? "{{TICKETS_DIR}}/tickets.json" : metadata.TicketSource,
                _settings);
            metadata.CompletedSource = WorkspaceContext.ApplyPathTokens(
                string.IsNullOrWhiteSpace(metadata.CompletedSource) ? "{{TICKETS_DIR}}/completed.json" : metadata.CompletedSource,
                _settings);

            return metadata;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StepLoader] Failed to parse metadata file '{Path.GetFileName(metadataPath)}': {ex.Message}");
            return new StepMetadata();
        }
    }

    private static string NormalizeExecutionMode(string? executionMode)
    {
        return string.Equals(executionMode, "ticketIteration", StringComparison.OrdinalIgnoreCase)
            ? "ticketIteration"
            : "standard";
    }

private async Task<List<Step>> LoadStepsAsync(CancellationToken ct = default)
    {

        var steps = new List<Step>();
        var skippedFiles = new List<(string FilePath, string Error)>();
        var stepDir = _settings.StepsDirectory;

        if (!Directory.Exists(stepDir))
        {
            Console.WriteLine($"[StepLoader] Steps directory not found: {stepDir}");
            return steps;
        }

        var mdFiles = Directory.GetFiles(stepDir, "*.md")
            .OrderBy(f => Path.GetFileName(f))
            .ToList();

                for (int i = 0; i < mdFiles.Count; i++)
            {
                try
                {
                    var step = new Step
                {
                    FilePath = mdFiles[i],
                    Content = WorkspaceContext.ApplyPathTokens(await File.ReadAllTextAsync(mdFiles[i], ct), _settings),
                    Order = i + 1,
                        Config = LoadStepConfig(mdFiles[i]),
                        Metadata = LoadStepMetadata(mdFiles[i])
                    };
                    
                    steps.Add(step);
                }
                catch (Exception ex)
            {
                skippedFiles.Add((mdFiles[i], ex.Message));
                Console.WriteLine($"[StepLoader] Failed to load file '{Path.GetFileName(mdFiles[i])}': {ex.Message}");
            }
        }

        if (skippedFiles.Count > 0)
        {
            Console.WriteLine($"[StepLoader] Skipped {skippedFiles.Count} file(s) due to errors");
        }

        return steps;
    }

    private static bool IsTicketIterationStep(Step step)
    {
        return string.Equals(step.Metadata.ExecutionMode, "ticketIteration", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<StepExecutionResult> ExecuteTicketIterationStepAsync(Step step, CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        var stepName = Path.GetFileName(step.FilePath);
        var stepKey = BuildStepKey(step);
        var metadata = step.Metadata ?? new StepMetadata();
        var ticketSource = WorkspaceContext.ApplyPathTokens(
            string.IsNullOrWhiteSpace(metadata.TicketSource) ? "{{TICKETS_DIR}}/tickets.json" : metadata.TicketSource,
            _settings);
        var completedSource = WorkspaceContext.ApplyPathTokens(
            string.IsNullOrWhiteSpace(metadata.CompletedSource) ? "{{TICKETS_DIR}}/completed.json" : metadata.CompletedSource,
            _settings);
        var skippedSource = DeriveSkippedSourcePath(completedSource);
        var requestedSource = DeriveRequestedSourcePath(completedSource);
        var maxAttempts = metadata.MaxRetriesPerTicket > 0 ? metadata.MaxRetriesPerTicket : 3;

        var openTickets = LoadTicketItems(ticketSource);
        var completedIds = LoadCompletedTicketIds(completedSource);
        var skippedIds = LoadSkippedTicketIds(skippedSource);
        skippedIds.UnionWith(LoadLegacySkippedTicketIdsFromCompleted(completedSource));
        var requestedTicketId = LoadRequestedTicketId(requestedSource);

        var totalTickets = openTickets.Count;
        var completedCount = openTickets.Count(ticket => completedIds.Contains(ticket.TicketId));
        var selected = !string.IsNullOrWhiteSpace(requestedTicketId)
            ? openTickets.FirstOrDefault(ticket =>
                string.Equals(ticket.TicketId, requestedTicketId, StringComparison.Ordinal) &&
                !completedIds.Contains(ticket.TicketId) &&
                !skippedIds.Contains(ticket.TicketId))
            : null;
        if (selected == null && !string.IsNullOrWhiteSpace(requestedTicketId))
        {
            TryClearRequestedTicketId(requestedSource);
            requestedTicketId = string.Empty;
        }

        selected ??= openTickets.FirstOrDefault(ticket =>
            !string.IsNullOrWhiteSpace(ticket.TicketId) &&
            !completedIds.Contains(ticket.TicketId) &&
            !skippedIds.Contains(ticket.TicketId));
        var remainingBefore = openTickets.Count(ticket =>
            !string.IsNullOrWhiteSpace(ticket.TicketId) &&
            !completedIds.Contains(ticket.TicketId) &&
            !skippedIds.Contains(ticket.TicketId));

        if (selected == null)
        {
            return new StepExecutionResult
            {
                StepId = step.Id,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                DurationMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds,
                Success = true,
                TicketProgress = new TicketIterationProgress
                {
                    IsTicketIterationStep = true,
                    StepKey = stepKey,
                    StepName = stepName,
                    Attempt = 0,
                    MaxAttempts = maxAttempts,
                    TotalTickets = totalTickets,
                    CompletedTickets = completedCount,
                    RemainingTickets = 0,
                    Status = "Completed",
                    LastUpdatedUtc = DateTime.UtcNow
                }
            };
        }

        var attempt = 1;
        var augmentedPrompt = BuildTicketPrompt(step, selected, attempt, maxAttempts, remainingBefore, totalTickets, completedCount, completedSource);
        var result = await ExecuteStepAsync(step, ct, augmentedPrompt);

        while (attempt < maxAttempts)
        {
            if (IsTicketCompleted(completedSource, selected.TicketId))
            {
                break;
            }

            attempt++;
            var retryInstruction = BuildRetryInstruction(selected, attempt, maxAttempts, completedSource);
            var turnResult = await _stepConversationService.ProcessUserMessageAsync(
                result.StepContext,
                retryInstruction,
                ct,
                onAssistantToolCallResponseAsync: PublishAssistantToolCallResponseAsync,
                onToolRequestedAsync: PublishToolRequestAsync,
                onToolResultAsync: PublishToolExecutionResultAsync,
                onPseudoToolCallWarningAsync: PublishPseudoToolCallWarningAsync);
            var existingPromptTokens = result.LLMResponse?.Usage?.PromptTokens ?? 0;
            var existingCompletionTokens = result.LLMResponse?.Usage?.CompletionTokens ?? 0;
            var existingTotalTokens = result.LLMResponse?.Usage?.TotalTokens ?? 0;
            result.StepContext = turnResult.UpdatedStepContext;
            result.LLMResponse = turnResult.FinalResponse;
            result.LLMResponse.Usage ??= new TokenUsage();
            var retryPromptTokens = result.LLMResponse.Usage.PromptTokens > 0
                ? result.LLMResponse.Usage.PromptTokens
                : EstimateTokens(string.Join('\n', result.StepContext.Select(message => message.Content ?? string.Empty)));
            var retryCompletionTokens = result.LLMResponse.Usage.CompletionTokens > 0
                ? result.LLMResponse.Usage.CompletionTokens
                : EstimateTokens(turnResult.FinalResponse.Content);
            var retryTotalTokens = result.LLMResponse.Usage.TotalTokens > 0
                ? result.LLMResponse.Usage.TotalTokens
                : retryPromptTokens + retryCompletionTokens;

            result.LLMResponse.Usage.PromptTokens = existingPromptTokens + retryPromptTokens;
            result.LLMResponse.Usage.CompletionTokens = existingCompletionTokens + retryCompletionTokens;
            result.LLMResponse.Usage.TotalTokens = existingTotalTokens + retryTotalTokens;
            result.ToolResults.AddRange(turnResult.ToolResults);
            result.ResponseContent += $"\n\n[RETRY {attempt}]\n{turnResult.FinalResponse.Content}";
            await PublishFinalLlmResponseAsync(turnResult.FinalResponse);
            await PublishConversationTurnStatsAsync(step.Order, turnResult);
        }

        var ticketCompleted = IsTicketCompleted(completedSource, selected.TicketId);
        completedIds = LoadCompletedTicketIds(completedSource);
        completedCount = openTickets.Count(ticket => completedIds.Contains(ticket.TicketId));
        skippedIds = LoadSkippedTicketIds(skippedSource);
        skippedIds.UnionWith(LoadLegacySkippedTicketIdsFromCompleted(completedSource));
        var remaining = openTickets.Count(ticket =>
            !string.IsNullOrWhiteSpace(ticket.TicketId) &&
            !completedIds.Contains(ticket.TicketId) &&
            !skippedIds.Contains(ticket.TicketId));

        result.Success = ticketCompleted;
        result.Error = ticketCompleted
            ? ""
            : $"Ticket '{selected.TicketId}' was not appended to completed.json after {maxAttempts} attempts.";
        result.EndTime = DateTime.UtcNow;
        result.DurationMs = (long)(result.EndTime - startTime).TotalMilliseconds;
        if (!string.IsNullOrWhiteSpace(requestedTicketId) &&
            string.Equals(requestedTicketId, selected.TicketId, StringComparison.Ordinal) &&
            ticketCompleted)
        {
            TryClearRequestedTicketId(requestedSource);
        }

        result.TicketProgress = new TicketIterationProgress
        {
            IsTicketIterationStep = true,
            StepKey = stepKey,
            StepName = stepName,
            TicketId = selected.TicketId,
            TicketTitle = selected.Title,
            Attempt = attempt,
            MaxAttempts = maxAttempts,
            TotalTickets = totalTickets,
            CompletedTickets = completedCount,
            RemainingTickets = remaining,
            RetryExhausted = !ticketCompleted && attempt >= maxAttempts,
            Status = ticketCompleted ? "Completed" : "Failed",
            LastUpdatedUtc = DateTime.UtcNow,
            ContextMessages = new List<ChatMessage>(result.StepContext)
        };

        return result;
    }

    private static string BuildStepKey(Step step)
    {
        var folder = Path.GetFileName(Path.GetDirectoryName(step.FilePath) ?? string.Empty);
        var file = Path.GetFileNameWithoutExtension(step.FilePath);
        return string.IsNullOrWhiteSpace(folder) ? file : $"{folder}/{file}";
    }

    private static string BuildRetryInstruction(TicketItem ticket, int attempt, int maxAttempts, string completedSource)
    {
        return string.Join('\n',
            $"Retry attempt {attempt} of {maxAttempts} for ticket '{ticket.TicketId}'.",
            "The ticket is not yet recorded as completed.",
            $"You must complete the implementation and append this ticket to '{completedSource}'.",
            "Do not start another ticket.");
    }

    private static string BuildTicketPrompt(
        Step step,
        TicketItem ticket,
        int attempt,
        int maxAttempts,
        int remainingBefore,
        int totalTickets,
        int completedCount,
        string completedSource)
    {
        return string.Join('\n',
            step.Content,
            "",
            "Ticket iteration context:",
            $"- Ticket progress: {completedCount + 1} of {totalTickets} (remaining before run: {remainingBefore})",
            $"- Attempt: {attempt} of {maxAttempts}",
            $"- Work only this ticket_id: {ticket.TicketId}",
            $"- Ticket title: {ticket.Title}",
            $"- Ticket description: {ticket.Description}",
            $"- You must append this ticket to {completedSource} when done.",
            "- Do not start another ticket.");
    }

    private static List<TicketItem> LoadTicketItems(string ticketSourcePath)
    {
        try
        {
            if (!File.Exists(ticketSourcePath))
            {
                return new List<TicketItem>();
            }

            var json = File.ReadAllText(ticketSourcePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<TicketItem>();
            }

            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new List<TicketItem>();
            }

            var items = new List<TicketItem>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var ticketId = element.TryGetProperty("ticket_id", out var ticketIdElement)
                    ? (ticketIdElement.GetString() ?? "").Trim()
                    : "";
                if (string.IsNullOrWhiteSpace(ticketId))
                {
                    continue;
                }

                var title = element.TryGetProperty("title", out var titleElement)
                    ? (titleElement.GetString() ?? "(untitled)").Trim()
                    : "(untitled)";
                var description = element.TryGetProperty("description", out var descriptionElement)
                    ? (descriptionElement.GetString() ?? "").Trim()
                    : "";

                items.Add(new TicketItem
                {
                    TicketId = ticketId,
                    Title = title,
                    Description = description
                });
            }

            return items;
        }
        catch
        {
            return new List<TicketItem>();
        }
    }

    private static HashSet<string> LoadCompletedTicketIds(string completedSourcePath)
    {
        try
        {
            if (!File.Exists(completedSourcePath))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var json = File.ReadAllText(completedSourcePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                // Back-compat: earlier builds stored skipped tickets in completed.json with {"skipped": true}.
                // Those should not be treated as completed work.
                if (element.TryGetProperty("skipped", out var skippedElement) &&
                    skippedElement.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                if (!element.TryGetProperty("ticket_id", out var ticketIdElement))
                {
                    continue;
                }

                var ticketId = (ticketIdElement.GetString() ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(ticketId))
                {
                    ids.Add(ticketId);
                }
            }

            return ids;
        }
        catch
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static HashSet<string> LoadLegacySkippedTicketIdsFromCompleted(string completedSourcePath)
    {
        try
        {
            if (!File.Exists(completedSourcePath))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var json = File.ReadAllText(completedSourcePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!element.TryGetProperty("skipped", out var skippedElement) ||
                    skippedElement.ValueKind != JsonValueKind.True)
                {
                    continue;
                }

                if (!element.TryGetProperty("ticket_id", out var ticketIdElement))
                {
                    continue;
                }

                var ticketId = (ticketIdElement.GetString() ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(ticketId))
                {
                    ids.Add(ticketId);
                }
            }

            return ids;
        }
        catch
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static HashSet<string> LoadSkippedTicketIds(string skippedSourcePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(skippedSourcePath) || !File.Exists(skippedSourcePath))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var json = File.ReadAllText(skippedSourcePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!element.TryGetProperty("ticket_id", out var ticketIdElement))
                {
                    continue;
                }

                var ticketId = (ticketIdElement.GetString() ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(ticketId))
                {
                    ids.Add(ticketId);
                }
            }

            return ids;
        }
        catch
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static string DeriveSkippedSourcePath(string completedSourcePath)
    {
        var directory = Path.GetDirectoryName(completedSourcePath);
        directory = string.IsNullOrWhiteSpace(directory) ? "." : directory;
        return Path.Combine(directory, "skipped.json");
    }

    private static string DeriveRequestedSourcePath(string completedSourcePath)
    {
        var directory = Path.GetDirectoryName(completedSourcePath);
        directory = string.IsNullOrWhiteSpace(directory) ? "." : directory;
        return Path.Combine(directory, "requested-ticket.json");
    }

    private static string LoadRequestedTicketId(string requestedSourcePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(requestedSourcePath) || !File.Exists(requestedSourcePath))
            {
                return string.Empty;
            }

            var json = File.ReadAllText(requestedSourcePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return string.Empty;
            }

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("ticket_id", out var ticketIdElement))
            {
                return string.Empty;
            }

            return (ticketIdElement.GetString() ?? string.Empty).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TryClearRequestedTicketId(string requestedSourcePath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(requestedSourcePath) && File.Exists(requestedSourcePath))
            {
                File.Delete(requestedSourcePath);
            }
        }
        catch
        {
            // Ignore request cleanup failures; normal ticket ordering still works.
        }
    }

    private static bool IsTicketCompleted(string completedSourcePath, string ticketId)
    {
        if (string.IsNullOrWhiteSpace(ticketId))
        {
            return false;
        }

        var completedIds = LoadCompletedTicketIds(completedSourcePath);
        return completedIds.Contains(ticketId);
    }

    private async Task<StepExecutionResult> ExecuteStepAsync(Step step, CancellationToken ct = default, string? userPromptOverride = null)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            // Initial messages for this step
            var initialMessages = new List<ChatMessage>
            {
                new ChatMessage { Role = "system", Content = ToolCallSafety.SystemPrompt },
                new ChatMessage { Role = "system", Content = WorkspaceContext.BuildScopeMessage(_settings) },
                new ChatMessage { Role = "user", Content = $"Step {step.Order}: {userPromptOverride ?? step.Content}" }
            };

            var allMessages = new List<ChatMessage>(initialMessages);
            string? finalResponseContent = null;
            var allToolResults = new List<ToolResult>();
            var toolResultsPublishedLive = false;
            var pseudoToolCallRetryCount = 0;
            var accumulatedPromptTokens = 0;
            var accumulatedCompletionTokens = 0;
            var accumulatedTotalTokens = 0;

            while (true)
            {
                var stopSequences = string.IsNullOrWhiteSpace(_settings.LlmStopSequences)
                    ? Array.Empty<string>()
                    : _settings.LlmStopSequences.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var request = new LLMRequest
                {
                    Model = _settings.GetActiveLlmModelName(),
                    Messages = allMessages,
                    Tools = GetAvailableTools(),
                    Temperature = _settings.LlmTemperature,
                    TopP = _settings.LlmTopP,
                    TopK = _settings.LlmTopK,
                    MaxTokens = _settings.LlmMaxTokens,
                    FrequencyPenalty = _settings.LlmFrequencyPenalty,
                    PresencePenalty = _settings.LlmPresencePenalty,
                    StopSequences = stopSequences
                };

                var llmResponse = await _llmClient.GenerateAsync(request, ct);
                var turnPromptTokens = llmResponse?.Usage?.PromptTokens ?? 0;
                if (turnPromptTokens <= 0)
                {
                    turnPromptTokens = EstimateTokens(string.Join('\n', allMessages.Select(message => message.Content ?? string.Empty)));
                }

                var turnCompletionTokens = llmResponse?.Usage?.CompletionTokens ?? 0;
                if (turnCompletionTokens <= 0)
                {
                    turnCompletionTokens = EstimateTokens(llmResponse?.Content);
                }

                var turnTotalTokens = llmResponse?.Usage?.TotalTokens ?? 0;
                if (turnTotalTokens <= 0)
                {
                    turnTotalTokens = turnPromptTokens + turnCompletionTokens;
                }

                accumulatedPromptTokens += turnPromptTokens;
                accumulatedCompletionTokens += turnCompletionTokens;
                accumulatedTotalTokens += turnTotalTokens;

                // Track response content from the last call
                finalResponseContent = llmResponse.Content;

                if (llmResponse.ToolCalls?.Count > 0)
                {
                    if (string.IsNullOrWhiteSpace(llmResponse.Content))
                    {
                        llmResponse.Content = "(assistant requested tool call; no textual content)";
                    }

                    await PublishAssistantToolCallResponseAsync(llmResponse);
                    foreach (var toolCall in llmResponse.ToolCalls)
                    {
                        await PublishToolRequestAsync(toolCall.Name, BuildToolRequestSummary(toolCall));
                    }

                    var toolResults = await _toolExecutor.ExecuteToolsAsync(llmResponse.ToolCalls, ct);
                    allToolResults.AddRange(toolResults);

                    if (SendToolExecutionAsync != null)
                    {
                        foreach (var toolCall in llmResponse.ToolCalls)
                        {
                            var toolResult = toolResults.FirstOrDefault(item => item.ToolId == toolCall.Id)
                                ?? new ToolResult
                                {
                                    ToolId = toolCall.Id,
                                    ToolName = toolCall.Name,
                                    RequestSummary = BuildToolRequestSummary(toolCall),
                                    Success = false,
                                    Error = "Tool result was not returned."
                                };

                            await SendToolExecutionAsync(toolResult);
                        }

                        toolResultsPublishedLive = true;
                    }
                    
                    allMessages.Add(new ChatMessage
                    {
                        Role = "assistant",
                        Content = llmResponse.Content ?? string.Empty,
                        ToolCalls = llmResponse.ToolCalls
                    });

                    foreach (var toolCall in llmResponse.ToolCalls)
                    {
                        var result = toolResults.FirstOrDefault(t => t.ToolId == toolCall.Id);
                        var toolContent = result?.Success ?? false
                            ? (string.IsNullOrWhiteSpace(result?.Output)
                                ? "(tool executed successfully; no output)"
                                : result!.Output)
                            : $"Error: {result?.Error}";
                        allMessages.Add(new ChatMessage 
                        { 
                            Role = "tool", 
                            Name = toolCall.Name,
                            ToolCallId = toolCall.Id,
                            Content = toolContent
                        });
                    }

                    // If tools were called, continue the conversation to get final response
                    if (llmResponse.ToolCalls.Count > 0)
                    {
                        continue;
                    }
                }

                if (ToolCallSafety.ContainsPseudoToolCallText(llmResponse.Content, llmResponse.ReasoningContent))
                {
                    await PublishPseudoToolCallWarningAsync(ToolCallSafety.BuildStrictWarning($"workflow step {step.Order}"));

                    if (pseudoToolCallRetryCount == 0)
                    {
                        await PublishPseudoToolCallWarningAsync(ToolCallSafety.BuildAutoCorrectionNotice($"workflow step {step.Order}"));
                        pseudoToolCallRetryCount++;
                        allMessages.Add(new ChatMessage
                        {
                            Role = "system",
                            Content = ToolCallSafety.RetrySystemInstruction
                        });
                        allMessages.Add(new ChatMessage
                        {
                            Role = "user",
                            Content = ToolCallSafety.RetryInstruction
                        });
                        continue;
                    }
                }

                allMessages.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = llmResponse.Content ?? string.Empty
                });

                var endTime = DateTime.UtcNow;
                var response = llmResponse ?? new LLMResponse();
                response.Usage ??= new TokenUsage();
                response.Usage.PromptTokens = accumulatedPromptTokens > 0 ? accumulatedPromptTokens : response.Usage.PromptTokens;
                response.Usage.CompletionTokens = accumulatedCompletionTokens > 0 ? accumulatedCompletionTokens : response.Usage.CompletionTokens;
                response.Usage.TotalTokens = accumulatedTotalTokens > 0
                    ? accumulatedTotalTokens
                    : (response.Usage.TotalTokens > 0 ? response.Usage.TotalTokens : response.Usage.PromptTokens + response.Usage.CompletionTokens);

                return new StepExecutionResult
                {
                    StepId = step.Id,
                    StartTime = startTime,
                    EndTime = endTime,
                    DurationMs = (long)(endTime - startTime).TotalMilliseconds,
                    Success = true,
                    LLMResponse = response,
                    ToolResults = allToolResults,
                    ToolResultsPublishedLive = toolResultsPublishedLive,
                    RequestMessagesContent = allMessages.Select(m => $"[{m.Role}] {m.Content}").ToList(),
                    ResponseContent = finalResponseContent ?? "",
                    StepContext = new List<ChatMessage>(allMessages)
                };
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new StepExecutionResult
            {
                StepId = step.Id,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                Error = ex.Message,
                Success = false
            };
        }
    }

    private static int EstimateTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));
    }

    private sealed class TicketItem
    {
        public string TicketId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
    }

    private List<ToolDefinition> GetAvailableTools()
    {
        var availableTools = new List<ToolDefinition>();

        foreach (var config in _settings.ToolConfigs.Values)
        {
            Console.WriteLine($"[WorkflowOrchestrator] Checking tool: {config.Name}, Enabled: {config.Enabled}");
            
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
                case "ReadFile":
                    availableTools.Add(new ToolDefinition
                    {
                        Function = new FunctionObject
                        {
                            Name = "read_file",
                            Description = "Read file contents from disk. Do not use on directories. Arguments: {\"path\": \"/path/to/file\"}",
                            Parameters = new Dictionary<string, object>
                            {
                                ["type"] = "object",
                                ["properties"] = new Dictionary<string, object>
                                {
                                    ["path"] = new Dictionary<string, string> { ["type"] = "string" }
                                },
                                ["required"] = new List<string> { "path" }
                            }
                        }
                    });
                    break;
                case "WriteFile":
                    availableTools.Add(new ToolDefinition
                    {
                        Function = new FunctionObject
                        {
                            Name = "write_file",
                            Description = "Write content to a file. Arguments: {\"path\": \"/path/to/file\", \"content\": \"file contents\"}",
                            Parameters = new Dictionary<string, object>
                            {
                                ["type"] = "object",
                                ["properties"] = new Dictionary<string, object>
                                {
                                    ["path"] = new Dictionary<string, string> { ["type"] = "string" },
                                    ["content"] = new Dictionary<string, string> { ["type"] = "string" }
                                },
                                ["required"] = new List<string> { "path", "content" }
                            }
                        }
                    });
                    break;
            }
        }

        return availableTools;
    }

    private async Task PublishAssistantToolCallResponseAsync(LLMResponse llmResponse)
    {
        if (SendLlmResponseWithReasoningAsync != null)
        {
            await SendLlmResponseWithReasoningAsync(
                llmResponse.Content ?? string.Empty,
                llmResponse.ReasoningContent ?? string.Empty);
            return;
        }

        if (SendLlmResponseAsync != null && !string.IsNullOrWhiteSpace(llmResponse.Content))
        {
            await SendLlmResponseAsync(llmResponse.Content);
        }
    }

    private async Task PublishFinalLlmResponseAsync(LLMResponse llmResponse)
    {
        if (string.IsNullOrWhiteSpace(llmResponse.Content) && string.IsNullOrWhiteSpace(llmResponse.ReasoningContent))
        {
            return;
        }

        if (SendLlmResponseWithReasoningAsync != null)
        {
            await SendLlmResponseWithReasoningAsync(
                llmResponse.Content ?? string.Empty,
                llmResponse.ReasoningContent ?? string.Empty);
            return;
        }

        if (SendLlmResponseAsync != null && !string.IsNullOrWhiteSpace(llmResponse.Content))
        {
            await SendLlmResponseAsync(llmResponse.Content);
        }
    }

    private async Task PublishConversationTurnStatsAsync(int stepNumber, StepConversationTurnResult turnResult)
    {
        if (SendConversationTurnStatsAsync != null)
        {
            await SendConversationTurnStatsAsync(stepNumber, turnResult);
        }
    }

    private async Task PublishToolRequestAsync(string toolName, string requestSummary)
    {
        if (SendToolRequestAsync != null)
        {
            await SendToolRequestAsync(toolName, requestSummary);
        }
    }

    private async Task PublishToolExecutionResultAsync(ToolResult result)
    {
        if (SendToolExecutionAsync != null)
        {
            await SendToolExecutionAsync(result);
        }
    }

    private async Task PublishPseudoToolCallWarningAsync(string warningMessage)
    {
        if (string.IsNullOrWhiteSpace(warningMessage))
        {
            return;
        }

        Console.WriteLine(warningMessage);

        if (SendLlmResponseAsync != null)
        {
            await SendLlmResponseAsync(warningMessage);
        }
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
            using var document = JsonDocument.Parse(json);
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
}
