using ABHive;
using ABHive.Application;

namespace ABHive.Web;

public class WebOutputFormatter : ABHive.Application.IConsoleOutputFormatter
{
    private readonly WebSocketHandler _webSocketHandler;
    private int _currentStepNumber;
    private int _totalSteps;
    private bool _stepCompletionPublished;

    public WebOutputFormatter(WebSocketHandler webSocketHandler)
    {
        _webSocketHandler = webSocketHandler;
    }

    public void ShowStepProgress(int current, int total, Step step)
    {
        _currentStepNumber = current;
        _totalSteps = total;
        _stepCompletionPublished = false;
        var stepName = Path.GetFileName(step.FilePath);
        var isTicketIterationStep = string.Equals(step.Metadata?.ExecutionMode, "ticketIteration", StringComparison.OrdinalIgnoreCase);
        RunSafely(_webSocketHandler.ClearActiveStepContextAsync());
        RunSafely(_webSocketHandler.PublishStepStartAsync(current, total, stepName, step.Content, isTicketIterationStep));
    }

    public string? ShowStepSuccess(StepExecutionResult result, Step step, CancellationToken ct = default)
    {
        if (!_stepCompletionPublished)
        {
            if (!result.ToolResultsShown && !result.ToolResultsPublishedLive && result.ToolResults?.Count > 0)
            {
                foreach (var toolResult in result.ToolResults)
                {
                    var output = !string.IsNullOrWhiteSpace(toolResult.Output)
                        ? toolResult.Output
                        : toolResult.Error;

                    RunSafely(_webSocketHandler.PublishToolExecutionAsync(
                        toolResult.ToolName,
                        toolResult.Success,
                        output,
                        toolResult.RequestSummary));
                }

                result.ToolResultsShown = true;
            }

            if (!string.IsNullOrWhiteSpace(result.LLMResponse?.Content) ||
                !string.IsNullOrWhiteSpace(result.LLMResponse?.ReasoningContent))
            {
                RunSafely(_webSocketHandler.PublishLlmResponseAsync(
                    result.LLMResponse?.Content ?? string.Empty,
                    result.LLMResponse?.ReasoningContent ?? string.Empty));
            }
            else
            {
                RunSafely(_webSocketHandler.PublishLogAsync(
                    "LLM returned an empty response for this step.",
                    "orange"));
            }

            RunSafely(_webSocketHandler.PublishStepCompleteAsync(_currentStepNumber, result, IsOpenChatStep(step)));
            _stepCompletionPublished = true;
        }

        var isTicketIterationStep = string.Equals(step.Metadata?.ExecutionMode, "ticketIteration", StringComparison.OrdinalIgnoreCase);
        if (isTicketIterationStep)
        {
            _webSocketHandler.ClearActiveStepContextAsync().GetAwaiter().GetResult();
            return null;
        }

        var stepName = Path.GetFileName(step.FilePath);
        _webSocketHandler.PersistActiveStepContextAsync(new ActiveStepContextState
        {
            RunId = "",
            StepNumber = _currentStepNumber,
            TotalSteps = _totalSteps,
            StepName = stepName,
            StepFilePath = step.FilePath,
            Messages = result.StepContext,
            LastUpdatedUtc = DateTime.UtcNow
        }).GetAwaiter().GetResult();

        if (ct.IsCancellationRequested)
        {
            _webSocketHandler.ClearActiveStepContextAsync().GetAwaiter().GetResult();
            return null;
        }

        _webSocketHandler.OpenUserInputWindowAsync().GetAwaiter().GetResult();

        WebSocketHandler.UserInput input;
        try
        {
            input = _webSocketHandler.WaitForUserInputAsync(ct).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Skip/Stop cancels the in-flight workflow token. Treat that as a normal interruption.
            _webSocketHandler.ClearActiveStepContextAsync().GetAwaiter().GetResult();
            return null;
        }
        if (input.Kind == WebSocketHandler.UserInputKind.Message)
        {
            return input.Message;
        }

        _webSocketHandler.ClearActiveStepContextAsync().GetAwaiter().GetResult();
        return null;
    }

    public void ShowStepFailure(StepExecutionResult result)
    {
        var error = string.IsNullOrWhiteSpace(result.Error)
            ? "Unknown error"
            : result.Error;

        RunSafely(_webSocketHandler.ClearActiveStepContextAsync());
        RunSafely(_webSocketHandler.PublishStepFailedAsync(_currentStepNumber, error, result));
    }

    private bool IsOpenChatStep(Step step)
    {
        if (_totalSteps != 1)
        {
            return false;
        }

        var workflowDirectoryName = Path.GetFileName(Path.GetDirectoryName(step.FilePath) ?? string.Empty);
        var stepName = Path.GetFileNameWithoutExtension(step.FilePath);
        return string.Equals(workflowDirectoryName, "chat", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(stepName, "chat", StringComparison.OrdinalIgnoreCase);
    }

    private static void RunSafely(Task task)
    {
        try
        {
            task.GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore publish failures so they do not interrupt workflow execution.
        }
    }
}
