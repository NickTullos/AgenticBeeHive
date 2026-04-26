using System.Text.Json;

namespace ABHive.Application;

public interface IMetricsLogger
{
    Task LogWorkflowMetricsAsync(WorkflowMetrics metrics, List<StepExecutionResult> stepResults);
}

public class MetricsLogger : IMetricsLogger
{
    private readonly AppSettings _settings;

    public MetricsLogger(AppSettings settings)
    {
        _settings = settings;
    }

    public async Task LogWorkflowMetricsAsync(WorkflowMetrics metrics, List<StepExecutionResult> stepResults)
    {
        Console.WriteLine($"[METRICS] Starting to write metrics for {stepResults.Count} steps...");
        try
        {
            var logDir = Path.GetDirectoryName(_settings.LogFilePath);
            if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            var logEntry = new WorkflowLogEntry
            {
                Timestamp = DateTime.UtcNow.ToString("O"),
                WorkflowId = Guid.NewGuid().ToString(),
                Metrics = new LogMetrics
                {
                    TotalSteps = metrics.TotalSteps,
                    SuccessfulSteps = metrics.SuccessfulSteps,
                    FailedSteps = metrics.FailedSteps,
                    TotalDurationMs = metrics.TotalDurationMs,
                    AverageStepDurationMs = Math.Round(metrics.AverageStepDurationMs, 2),
                    TotalTokensUsed = metrics.TotalTokensUsed
                },
                StepResults = stepResults.Select(r => new LogStepResult
                {
                    StepId = r.StepId,
                    StartTime = r.StartTime.ToString("O"),
                    EndTime = r.EndTime.ToString("O"),
                    DurationMs = r.DurationMs,
                    Success = r.Success,
                    FullRequestMessages = r.RequestMessagesContent.Select(msg => new LogMessage { Content = msg }).ToList(),
                    FullResponseContent = r.ResponseContent,
                    FullResponseToolCalls = r.LLMResponse?.ToolCalls?.Select(tc => new LogToolCall
                    {
                        Id = tc.Id,
                        Name = tc.Name,
                        Arguments = tc.Arguments
                    }).ToList() ?? new List<LogToolCall>(),
                    ToolCalls = r.ToolResults?.Select(tr => new LogToolCall
                    {
                        Id = tr.ToolId,
                        Name = "",
                        Arguments = "",
                        Success = tr.Success
                    }).ToList() ?? new List<LogToolCall>(),
                    Error = r.Error
                }).ToList()
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(logEntry, options);
            
            Console.WriteLine($"[METRICS] JSON length: {json.Length}, Writing to {_settings.LogFilePath}...");

            await File.WriteAllTextAsync(_settings.LogFilePath, json);
            
            Console.WriteLine($"[METRICS] Metrics written successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[METRICS] Error writing metrics: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}

public class WorkflowLogEntry
{
    public string Timestamp { get; set; } = "";
    public string WorkflowId { get; set; } = "";
    public LogMetrics Metrics { get; set; } = new();
    public List<LogStepResult> StepResults { get; set; } = new();
}

public class LogMetrics
{
    public int TotalSteps { get; set; }
    public int SuccessfulSteps { get; set; }
    public int FailedSteps { get; set; }
    public long TotalDurationMs { get; set; }
    public double AverageStepDurationMs { get; set; }
    public int TotalTokensUsed { get; set; }
}

public class LogStepResult
{
    public string StepId { get; set; } = "";
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
    public long DurationMs { get; set; }
    public bool Success { get; set; }
    public List<LogMessage> FullRequestMessages { get; set; } = new();
    public string FullResponseContent { get; set; } = "";
    public List<LogToolCall> FullResponseToolCalls { get; set; } = new();
    public List<LogToolCall> ToolCalls { get; set; } = new();
    public string Error { get; set; } = "";
}

public class LogMessage
{
    public string Content { get; set; } = "";
}

public class LogToolCall
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Arguments { get; set; } = "";
    public bool? Success { get; set; }
}
