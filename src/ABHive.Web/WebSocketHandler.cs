using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using ABHive;
using ABHive.Application;
using Microsoft.Extensions.DependencyInjection;

namespace ABHive.Web;

public class MessageTypes
{
    public const string Status = "status";
    public const string Hydrate = "hydrate";
    public const string WorkflowReset = "workflow_reset";
    public const string WorkflowStart = "workflow_start";
    public const string WorkflowResume = "workflow_resume";
    public const string WorkflowEnd = "workflow_end";
    public const string StepStart = "step_start";
    public const string StepComplete = "step_complete";
    public const string StepFailed = "step_failed";
    public const string LlmResponse = "llm_response";
    public const string UserQuestion = "user_question";
    public const string ToolRequest = "tool_request";
    public const string ToolExecution = "tool_execution";
    public const string Busy = "busy";
    public const string Log = "log";
    public const string LogLink = "log_link";
}

public class AgentMessage
{
    public string type { get; set; } = "";
    public string timestamp { get; set; } = "";
    public object? payload { get; set; }
}

public static class MessageSources
{
    public const string Web = "web";
    public const string Telegram = "telegram";
}

public class UserQuestionPayload
{
    public string question { get; set; } = "";
    public string source { get; set; } = MessageSources.Web;
    public string sourceLabel { get; set; } = "Web";
}

public class WebSocketHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan StopWaitTimeout = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<Guid, WebSocket> _webSockets = new();
    private readonly ConcurrentQueue<UserInput> _messageQueue = new();
    private readonly SemaphoreSlim _messageSignal = new(0);
    private readonly object _queueLock = new();

    private bool _isWorkflowRunning;
    private bool _isLlmBusy;
    private bool _isAwaitingUserInput;
    private readonly object _busyLock = new();

    private readonly AppSettings _settings;
    private readonly WorkflowStateStore _workflowStateStore;
    private readonly WorkflowTypeCatalog _workflowTypeCatalog;
    private readonly TicketIterationStatusResolver _ticketIterationStatusResolver;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly bool _debugMode;
    private WorkflowMetrics? _lastMetrics;
    private string _lastRunProjectDirectory = string.Empty;
    private string _lastRunProjectName = string.Empty;

    private CancellationTokenSource? _workflowCts;
    private Task? _workflowTask;
    private readonly object _workflowLock = new();

    public WebSocketHandler(
        AppSettings settings,
        WorkflowStateStore workflowStateStore,
        WorkflowTypeCatalog workflowTypeCatalog,
        TicketIterationStatusResolver ticketIterationStatusResolver,
        IServiceScopeFactory scopeFactory,
        bool debugMode = false)
    {
        _settings = settings;
        _workflowStateStore = workflowStateStore;
        _workflowTypeCatalog = workflowTypeCatalog;
        _ticketIterationStatusResolver = ticketIterationStatusResolver;
        _scopeFactory = scopeFactory;
        _debugMode = debugMode;
    }

    public bool IsConnected => _webSockets.Count > 0;
    public int ConnectedClients => _webSockets.Count;
    public WorkflowMetrics? LastMetrics => _lastMetrics;

    public bool IsWorkflowRunning
    {
        get
        {
            lock (_workflowLock)
            {
                return _isWorkflowRunning;
            }
        }
    }

    public bool IsBusy
    {
        get
        {
            lock (_busyLock)
            {
                return _isLlmBusy;
            }
        }
    }

    public bool IsAwaitingUserInput
    {
        get
        {
            lock (_queueLock)
            {
                return _isAwaitingUserInput;
            }
        }
    }

    public async Task HandleWebSocketAsync(WebSocket socket)
    {
        var id = Guid.NewGuid();
        _webSockets.TryAdd(id, socket);

        await SendHydrationAsync(socket);

        if (_debugMode)
        {
            Console.WriteLine($"[WebSocket] Client connected: {id}");
        }

        var buffer = new byte[1024];

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await HandleClientMessageAsync(message);
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }
            }
        }
        catch (Exception ex)
        {
            if (_debugMode)
            {
                Console.WriteLine($"[WebSocket] Error: {ex.Message}");
            }
        }
        finally
        {
            _webSockets.TryRemove(id, out _);

            if (_debugMode)
            {
                Console.WriteLine($"[WebSocket] Client disconnected: {id}");
            }
        }
    }

    private async Task HandleClientMessageAsync(string message)
    {
        try
        {
            var jsonDoc = JsonDocument.Parse(message);
            var root = jsonDoc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp))
            {
                return;
            }

            var type = typeProp.GetString() ?? "";

            switch (type)
            {
                case "start_workflow":
                    await StartWorkflowAsync(root.TryGetProperty("workflowTypeId", out var workflowTypeIdProp)
                        ? workflowTypeIdProp.GetString()
                        : null);
                    break;

                case "stop_workflow":
                    await StopWorkflowAsync();
                    break;

                case "send_message":
                    if (root.TryGetProperty("message", out var msgProp))
                    {
                        var messageText = msgProp.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(messageText))
                        {
                            await QueueUserMessageAsync(messageText);
                        }
                    }
                    break;

                case "ping":
                    await SendToSocketsAsync(CreateMessage(MessageTypes.Status, new { status = "Connected", color = "green" }));
                    break;
            }
        }
        catch (Exception ex)
        {
            if (_debugMode)
            {
                Console.WriteLine($"[WebSocket] Message parsing error: {ex.Message}");
            }
        }
    }

    public Task<bool> StartWorkflowAsync(string? workflowTypeId = null)
    {
        return StartWorkflowAsyncInternal(workflowTypeId, null);
    }

    public Task<bool> StartWorkflowAsync(WorkflowTypeDefinition workflowType)
    {
        return StartWorkflowAsyncInternal(workflowType?.Id, workflowType);
    }

    private async Task<bool> StartWorkflowAsyncInternal(string? workflowTypeId, WorkflowTypeDefinition? workflowTypeOverride)
    {
        var hydration = await _workflowStateStore.GetHydrationAsync();
        var activeStep = await _workflowStateStore.GetActiveStepAsync();
        // If the UI explicitly selected a workflow type, treat this as "start new" and do not
        // automatically resume a paused step context (which would make it appear like Step 1
        // instantly completed when the user expects a fresh start).
        if (activeStep != null && string.IsNullOrWhiteSpace(workflowTypeId))
        {
            ApplyWorkflowDirectory(activeStep.WorkflowStepsDirectory);
            ApplyProjectContext(activeStep.ProjectName, activeStep.ProjectDirectory);
            await ResumeActiveStepContextAsync(activeStep);
            return true;
        }

        // If the caller explicitly provided a workflow type, treat this as a "start new" request
        // and do not silently resume an existing snapshot.
        if (hydration.Snapshot.CanResume && string.IsNullOrWhiteSpace(workflowTypeId))
        {
            ApplyWorkflowDirectory(hydration.Snapshot.SelectedWorkflowStepsDirectory);
            ApplyProjectContext(hydration.Snapshot.SelectedProjectName, hydration.Snapshot.SelectedProjectDirectory);
            await ResumeWorkflowExecutionAsync(hydration.Snapshot);
            return true;
        }
        else if (activeStep != null && !string.IsNullOrWhiteSpace(workflowTypeId))
        {
            await PublishLogAsync(
                "Starting a new workflow and discarding the paused step context. Use Continue to resume a paused step instead.",
                "orange");
        }

        var workflowType = workflowTypeOverride ?? ResolveWorkflowTypeForStart(workflowTypeId, hydration.Snapshot);
        if (workflowType == null)
        {
            await PublishAsync(
                CreateMessage(MessageTypes.Status, new { status = "Select a workflow type to begin", color = "orange" }),
                snapshot =>
                {
                    snapshot.Status = "Select a workflow type to begin";
                    snapshot.Color = "orange";
                    snapshot.WorkflowRunning = false;
                    snapshot.Busy = false;
                    snapshot.AwaitingUserInput = false;
                });
            return false;
        }

        ApplyWorkflowType(workflowType);

        var totalSteps = CountSteps();
        bool alreadyRunning;
        lock (_workflowLock)
        {
            alreadyRunning = _isWorkflowRunning || _workflowTask != null;

            if (!alreadyRunning)
            {
                _workflowCts?.Cancel();
                _workflowCts = new CancellationTokenSource();
                _isWorkflowRunning = true;
            }
        }

        if (alreadyRunning)
        {
            await PublishAsync(
                CreateMessage(MessageTypes.Status, new { status = "Workflow already running", color = "orange" }),
                snapshot =>
                {
                    snapshot.Status = "Workflow already running";
                    snapshot.Color = "orange";
                });
            return false;
        }

        await _workflowStateStore.PrepareForNewRunAsync(totalSteps, workflowType);
        _lastMetrics = null;

        await PublishAsync(
            CreateMessage(MessageTypes.Status, new { status = "Starting workflow...", color = "yellow" }),
            snapshot =>
            {
                snapshot.Status = "Starting workflow...";
                snapshot.Color = "yellow";
                snapshot.WorkflowRunning = true;
                snapshot.Busy = false;
                snapshot.AwaitingUserInput = false;
                snapshot.TotalSteps = totalSteps;
                snapshot.CurrentStep = 0;
                snapshot.CurrentStepName = "Starting...";
                snapshot.SelectedWorkflowTypeId = workflowType.Id;
                snapshot.SelectedWorkflowTypeName = workflowType.Name;
                snapshot.SelectedWorkflowStepsDirectory = workflowType.StepsDirectory;
            });

        StartWorkflowTask(1, null);
        return true;
    }

    private async Task RunWorkflowAsync(CancellationToken ct, int startStepNumber = 1, WorkflowMetrics? existingMetrics = null)
    {
        try
        {
            var startHydration = await _workflowStateStore.GetHydrationAsync();
            _lastRunProjectDirectory = startHydration.Snapshot.SelectedProjectDirectory ?? string.Empty;
            _lastRunProjectName = startHydration.Snapshot.SelectedProjectName ?? string.Empty;

            lock (_queueLock)
            {
                _isAwaitingUserInput = false;
            }

            var totalSteps = CountSteps();

            if (startStepNumber <= 1)
            {
                await PublishAsync(
                    CreateMessage(MessageTypes.WorkflowStart, new { totalSteps }),
                    snapshot =>
                    {
                        snapshot.TotalSteps = totalSteps;
                        snapshot.WorkflowRunning = true;
                        snapshot.Busy = false;
                        snapshot.AwaitingUserInput = false;
                        snapshot.CanResume = false;
                        snapshot.NextStepToRun = 1;
                    });
            }
            else
            {
                await PublishAsync(
                    CreateMessage(MessageTypes.WorkflowResume, new { totalSteps, startStep = startStepNumber }),
                    snapshot =>
                    {
                        snapshot.TotalSteps = totalSteps;
                        snapshot.WorkflowRunning = true;
                        snapshot.Busy = false;
                        snapshot.AwaitingUserInput = false;
                        snapshot.CanResume = false;
                        snapshot.NextStepToRun = startStepNumber;
                    });
            }

            await PublishAsync(
                CreateMessage(MessageTypes.Status, new { status = "Running", color = "cyan" }),
                snapshot =>
                {
                    snapshot.Status = "Running";
                    snapshot.Color = "cyan";
                    snapshot.WorkflowRunning = true;
                    snapshot.Busy = true;
                });

            using var scope = _scopeFactory.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IWorkflowOrchestrator>();
            var metrics = startStepNumber <= 1
                ? await orchestrator.RunAsync(ct)
                : await orchestrator.RunFromStepAsync(startStepNumber, existingMetrics, ct);
            _lastMetrics = metrics;

            lock (_queueLock)
            {
                _isAwaitingUserInput = false;
            }

            await PublishBusyStateAsync(false);

            await PublishAsync(
                CreateMessage(
                    MessageTypes.WorkflowEnd,
                    new
                    {
                        totalSteps = metrics.TotalSteps,
                        successfulSteps = metrics.SuccessfulSteps,
                        failedSteps = metrics.FailedSteps,
                        ticketProgress = ToClientTicketProgress(metrics.TicketProgress)
                    }),
                snapshot =>
                {
                    var hasTicketResume = metrics.ResumeAtStep && metrics.ResumeStepNumber > 0;
                    snapshot.WorkflowRunning = false;
                    snapshot.Busy = false;
                    snapshot.AwaitingUserInput = false;
                    snapshot.CurrentStep = hasTicketResume ? metrics.ResumeStepNumber : metrics.TotalSteps;
                    snapshot.TotalSteps = metrics.TotalSteps;
                    snapshot.CurrentStepName = hasTicketResume
                        ? $"Step {metrics.ResumeStepNumber}"
                        : "Completed";
                    snapshot.Metrics = CloneMetrics(metrics);
                    snapshot.CanResume = hasTicketResume;
                    snapshot.NextStepToRun = hasTicketResume ? metrics.ResumeStepNumber : 0;
                    ApplyTicketProgressSnapshot(snapshot, metrics.TicketProgress);
                });

            await PublishAsync(
                CreateMessage(MessageTypes.Status, new
                {
                    status = metrics.ResumeAtStep && metrics.ResumeStepNumber > 0
                        ? "Ready for next ticket"
                        : "Ready",
                    color = metrics.ResumeAtStep && metrics.ResumeStepNumber > 0 ? "orange" : "green"
                }),
                snapshot =>
                {
                    var hasTicketResume = metrics.ResumeAtStep && metrics.ResumeStepNumber > 0;
                    snapshot.Status = hasTicketResume ? "Ready for next ticket" : "Ready";
                    snapshot.Color = hasTicketResume ? "orange" : "green";
                    snapshot.WorkflowRunning = false;
                    snapshot.CanResume = hasTicketResume;
                    snapshot.NextStepToRun = hasTicketResume ? metrics.ResumeStepNumber : 0;
                });

            await PublishStatsLinkIfAvailableAsync("completed", _lastRunProjectDirectory);
        }
        catch (OperationCanceledException)
        {
            lock (_queueLock)
            {
                _isAwaitingUserInput = false;
            }

            await PublishBusyStateAsync(false);
            await PublishAsync(
                CreateMessage(MessageTypes.Status, new { status = "Stopped", color = "orange" }),
                snapshot =>
                {
                    snapshot.Status = "Stopped";
                    snapshot.Color = "orange";
                    snapshot.WorkflowRunning = false;
                    snapshot.Busy = false;
                    snapshot.AwaitingUserInput = false;
                    snapshot.CanResume = snapshot.CurrentStep > 0 && snapshot.CurrentStep <= snapshot.TotalSteps;
                    snapshot.NextStepToRun = snapshot.CurrentStep > 0 ? snapshot.CurrentStep : 0;
                });
        }
    }

    public async Task<bool> QueueUserMessageAsync(string message, string source = MessageSources.Web, string? sourceLabel = null)
    {
        var input = new UserInput
        {
            Kind = UserInputKind.Message,
            Message = message,
            Timestamp = DateTime.UtcNow
        };
        var queued = EnqueueUserInput(input);

        if (queued)
        {
            await PublishUserQuestionAsync(message, source, sourceLabel);
            return true;
        }

        var activeStep = await _workflowStateStore.GetActiveStepAsync();
        if (activeStep != null)
        {
            await PublishUserQuestionAsync(message, source, sourceLabel);
            await ResumeActiveStepContextAsync(activeStep, input);
            return true;
        }

        var hydration = await _workflowStateStore.GetHydrationAsync();
        var snapshot = hydration.Snapshot;
        var canRecoverStepContext = (snapshot.CanResume && (snapshot.NextStepToRun > 0 || snapshot.CurrentStep > 0)) ||
                                    (!snapshot.WorkflowRunning && snapshot.CurrentStep > 0 && snapshot.TotalSteps > 0);
        if (canRecoverStepContext)
        {
            var recoveredStep = await TryCreateRecoveredActiveStepContextAsync(snapshot);
            if (recoveredStep != null)
            {
                await PublishUserQuestionAsync(message, source, sourceLabel);
                await ResumeActiveStepContextAsync(recoveredStep, input);
                return true;
            }
        }

        return false;
    }

    public async Task<bool> QueueContinueAsync()
    {
        var queued = EnqueueUserInput(new UserInput
        {
            Kind = UserInputKind.Continue,
            Timestamp = DateTime.UtcNow
        });

        if (queued)
        {
            return true;
        }

        var activeStep = await _workflowStateStore.GetActiveStepAsync();
        if (activeStep != null)
        {
            await ResumeActiveStepContextAsync(activeStep, new UserInput
            {
                Kind = UserInputKind.Continue,
                Timestamp = DateTime.UtcNow
            });
            return true;
        }

        var hydration = await _workflowStateStore.GetHydrationAsync();
        if (hydration.Snapshot.CanResume)
        {
            await ResumeWorkflowExecutionAsync(hydration.Snapshot);
            return true;
        }

        var snapshot = hydration.Snapshot;
        var resumePoint = _ticketIterationStatusResolver.FindResumePoint(snapshot);
        if (!snapshot.WorkflowRunning &&
            !snapshot.Busy &&
            resumePoint.Found &&
            resumePoint.HeaderStatus != null)
        {
            await ResumeWorkflowExecutionAsync(new WorkflowRuntimeSnapshot
            {
                CanResume = true,
                NextStepToRun = resumePoint.StepNumber,
                CurrentStep = resumePoint.StepNumber,
                TotalSteps = resumePoint.TotalSteps,
                CurrentStepName = resumePoint.StepName,
                SelectedWorkflowStepsDirectory = resumePoint.StepsDirectory,
                SelectedProjectName = snapshot.SelectedProjectName,
                SelectedProjectDirectory = snapshot.SelectedProjectDirectory,
                IsCurrentStepTicketIteration = true,
                TicketHeaderStatus = resumePoint.HeaderStatus,
                Metrics = CloneMetrics(snapshot.Metrics)
            });
            return true;
        }

	        return false;
	    }

	    public async Task<(bool Success, string Message)> SkipToStepAsync(int stepNumber)
	    {
	        if (stepNumber <= 0)
	        {
	            return (false, "Step number must be greater than zero.");
	        }

	        await StopAndDrainRuntimeAsync();
	        await _workflowStateStore.ClearActiveStepAsync();

	        var hydration = await _workflowStateStore.GetHydrationAsync();
	        var snapshot = hydration.Snapshot;
	        ApplyWorkflowDirectory(snapshot.SelectedWorkflowStepsDirectory);
	        ApplyProjectContext(snapshot.SelectedProjectName, snapshot.SelectedProjectDirectory);

	        var totalSteps = CountSteps();
	        if (totalSteps <= 0)
	        {
	            return (false, "Cannot skip: workflow steps directory is not available.");
	        }

	        if (stepNumber > totalSteps)
	        {
	            return (false, $"Cannot skip: step {stepNumber} is beyond total steps ({totalSteps}).");
	        }

	        var stepName = ResolveStepName(stepNumber) ?? $"Step {stepNumber}";
	        await PublishLogAsync($"Skipping to step {stepNumber}: {stepName}", "orange");

	        await ResumeWorkflowExecutionAsync(new WorkflowRuntimeSnapshot
	        {
	            CanResume = true,
	            NextStepToRun = stepNumber,
	            CurrentStep = stepNumber,
	            TotalSteps = totalSteps,
	            CurrentStepName = stepName,
	            SelectedWorkflowTypeId = snapshot.SelectedWorkflowTypeId,
	            SelectedWorkflowTypeName = snapshot.SelectedWorkflowTypeName,
	            SelectedWorkflowStepsDirectory = snapshot.SelectedWorkflowStepsDirectory,
	            SelectedProjectName = snapshot.SelectedProjectName,
	            SelectedProjectDirectory = snapshot.SelectedProjectDirectory,
	            Metrics = CloneMetrics(snapshot.Metrics)
	        });

	        return (true, $"Skipping to step {stepNumber}.");
	    }

	    public async Task<(bool Success, string Message)> SkipTicketAsync(int stepNumber, string ticketId)
	    {
	        ticketId = (ticketId ?? string.Empty).Trim();
	        if (stepNumber <= 0)
	        {
	            return (false, "Step number must be greater than zero.");
	        }

	        if (string.IsNullOrWhiteSpace(ticketId))
	        {
	            return (false, "Ticket id is required.");
	        }

	        await StopAndDrainRuntimeAsync();
	        await _workflowStateStore.ClearActiveStepAsync();

	        var hydration = await _workflowStateStore.GetHydrationAsync();
	        var snapshot = hydration.Snapshot;
	        ApplyWorkflowDirectory(snapshot.SelectedWorkflowStepsDirectory);
	        ApplyProjectContext(snapshot.SelectedProjectName, snapshot.SelectedProjectDirectory);

	        var totalSteps = CountSteps();
	        if (totalSteps <= 0)
	        {
	            return (false, "Cannot skip ticket: workflow steps directory is not available.");
	        }

	        if (stepNumber > totalSteps)
	        {
	            return (false, $"Cannot skip ticket: step {stepNumber} is beyond total steps ({totalSteps}).");
	        }

	        var stepPath = ResolveStepFilePath(stepNumber);
	        if (string.IsNullOrWhiteSpace(stepPath) || !File.Exists(stepPath))
	        {
	            return (false, $"Cannot skip ticket: step {stepNumber} file not found.");
	        }

	        var metadata = LoadStepMetadata(stepPath);
	        if (!string.Equals(metadata.ExecutionMode, "ticketIteration", StringComparison.OrdinalIgnoreCase))
	        {
	            return (false, $"Step {stepNumber} is not configured for ticket iteration.");
	        }

	        var completedPath = ApplyPathTokens(
	            string.IsNullOrWhiteSpace(metadata.CompletedSource) ? "{{TICKETS_DIR}}/completed.json" : metadata.CompletedSource,
	            snapshot);
	        var skippedPath = DeriveSkippedSourcePath(completedPath);

	        // If already completed (not just legacy-skipped), don't allow skipping.
	        var completedIds = LoadCompletedTicketIds(completedPath);
	        if (completedIds.Contains(ticketId))
	        {
	            return (false, $"Ticket '{ticketId}' is already completed.");
	        }

	        var appended = TryAppendSkippedTicket(skippedPath, ticketId, out var appendError);
	        if (!appended)
	        {
	            return (false, appendError ?? "Failed to mark ticket as skipped.");
	        }

	        await PublishLogAsync($"Skipping ticket {ticketId} at step {stepNumber}.", "orange");

	        var stepName = ResolveStepName(stepNumber) ?? $"Step {stepNumber}";
	        var headerStatus = _ticketIterationStatusResolver.Resolve(snapshot, stepNumber);

	        await PublishAsync(
	            CreateMessage(MessageTypes.Status, new { status = "Ready", color = "green" }),
	            current =>
	            {
	                current.Status = "Ready";
	                current.Color = "green";
	                current.WorkflowRunning = false;
	                current.Busy = false;
	                current.AwaitingUserInput = false;
	                current.CanResume = false;
	                current.NextStepToRun = 0;
	                current.CurrentStep = stepNumber;
	                current.TotalSteps = totalSteps;
	                current.CurrentStepName = stepName;
	                current.IsCurrentStepTicketIteration = true;
	                current.TicketHeaderStatus = headerStatus.IsTicketIterationStep ? headerStatus : null;
	            });
	        await PublishBusyStateAsync(false);

	        return (true, $"Skipped ticket {ticketId} and left the workflow paused.");
	    }

	    public async Task<(bool Success, string Message)> ResumeSkippedTicketAsync(int stepNumber, string ticketId)
	    {
	        ticketId = (ticketId ?? string.Empty).Trim();
	        if (stepNumber <= 0)
	        {
	            return (false, "Step number must be greater than zero.");
	        }

	        if (string.IsNullOrWhiteSpace(ticketId))
	        {
	            return (false, "Ticket id is required.");
	        }

	        await StopAndDrainRuntimeAsync();
	        await _workflowStateStore.ClearActiveStepAsync();

	        var hydration = await _workflowStateStore.GetHydrationAsync();
	        var snapshot = hydration.Snapshot;
	        ApplyWorkflowDirectory(snapshot.SelectedWorkflowStepsDirectory);
	        ApplyProjectContext(snapshot.SelectedProjectName, snapshot.SelectedProjectDirectory);

	        var totalSteps = CountSteps();
	        if (totalSteps <= 0)
	        {
	            return (false, "Cannot resume ticket: workflow steps directory is not available.");
	        }

	        if (stepNumber > totalSteps)
	        {
	            return (false, $"Cannot resume ticket: step {stepNumber} is beyond total steps ({totalSteps}).");
	        }

	        var stepPath = ResolveStepFilePath(stepNumber);
	        if (string.IsNullOrWhiteSpace(stepPath) || !File.Exists(stepPath))
	        {
	            return (false, $"Cannot resume ticket: step {stepNumber} file not found.");
	        }

	        var metadata = LoadStepMetadata(stepPath);
	        if (!string.Equals(metadata.ExecutionMode, "ticketIteration", StringComparison.OrdinalIgnoreCase))
	        {
	            return (false, $"Step {stepNumber} is not configured for ticket iteration.");
	        }

	        var completedPath = ApplyPathTokens(
	            string.IsNullOrWhiteSpace(metadata.CompletedSource) ? "{{TICKETS_DIR}}/completed.json" : metadata.CompletedSource,
	            snapshot);
	        var skippedPath = DeriveSkippedSourcePath(completedPath);

	        var removed = TryRemoveSkippedTicket(skippedPath, ticketId, out var removeError);
	        if (!removed)
	        {
	            return (false, removeError ?? "Failed to resume ticket.");
	        }

	        // Clean up earlier builds that stored skipped tickets inside completed.json.
	        TryRemoveLegacySkippedTicketFromCompleted(completedPath, ticketId, out _);

	        await PublishLogAsync($"Resuming ticket {ticketId} at step {stepNumber}.", "orange");

	        await ResumeWorkflowExecutionAsync(new WorkflowRuntimeSnapshot
	        {
	            CanResume = true,
	            NextStepToRun = stepNumber,
	            CurrentStep = stepNumber,
	            TotalSteps = totalSteps,
	            CurrentStepName = ResolveStepName(stepNumber) ?? $"Step {stepNumber}",
	            SelectedWorkflowTypeId = snapshot.SelectedWorkflowTypeId,
	            SelectedWorkflowTypeName = snapshot.SelectedWorkflowTypeName,
	            SelectedWorkflowStepsDirectory = snapshot.SelectedWorkflowStepsDirectory,
	            SelectedProjectName = snapshot.SelectedProjectName,
	            SelectedProjectDirectory = snapshot.SelectedProjectDirectory,
	            IsCurrentStepTicketIteration = true,
	            Metrics = CloneMetrics(snapshot.Metrics)
	        });

	        return (true, $"Resumed ticket {ticketId}.");
	    }

	    public async Task<(bool Success, string Message)> ReopenCompletedTicketAsync(int stepNumber, string ticketId)
	    {
	        ticketId = (ticketId ?? string.Empty).Trim();
	        if (stepNumber <= 0)
	        {
	            return (false, "Step number must be greater than zero.");
	        }

	        if (string.IsNullOrWhiteSpace(ticketId))
	        {
	            return (false, "Ticket id is required.");
	        }

	        await StopAndDrainRuntimeAsync();
	        await _workflowStateStore.ClearActiveStepAsync();

	        var hydration = await _workflowStateStore.GetHydrationAsync();
	        var snapshot = hydration.Snapshot;
	        ApplyWorkflowDirectory(snapshot.SelectedWorkflowStepsDirectory);
	        ApplyProjectContext(snapshot.SelectedProjectName, snapshot.SelectedProjectDirectory);

	        var totalSteps = CountSteps();
	        if (totalSteps <= 0)
	        {
	            return (false, "Cannot reopen ticket: workflow steps directory is not available.");
	        }

	        if (stepNumber > totalSteps)
	        {
	            return (false, $"Cannot reopen ticket: step {stepNumber} is beyond total steps ({totalSteps}).");
	        }

	        var stepPath = ResolveStepFilePath(stepNumber);
	        if (string.IsNullOrWhiteSpace(stepPath) || !File.Exists(stepPath))
	        {
	            return (false, $"Cannot reopen ticket: step {stepNumber} file not found.");
	        }

	        var metadata = LoadStepMetadata(stepPath);
	        if (!string.Equals(metadata.ExecutionMode, "ticketIteration", StringComparison.OrdinalIgnoreCase))
	        {
	            return (false, $"Step {stepNumber} is not configured for ticket iteration.");
	        }

	        var completedPath = ApplyPathTokens(
	            string.IsNullOrWhiteSpace(metadata.CompletedSource) ? "{{TICKETS_DIR}}/completed.json" : metadata.CompletedSource,
	            snapshot);
	        var skippedPath = DeriveSkippedSourcePath(completedPath);

	        var removed = TryRemoveCompletedTicket(completedPath, ticketId, out var removeError);
	        if (!removed)
	        {
	            return (false, removeError ?? "Failed to reopen ticket.");
	        }

	        // Ensure the reopened ticket is active again, not still deferred.
	        TryRemoveSkippedTicket(skippedPath, ticketId, out _);
	        TryRemoveLegacySkippedTicketFromCompleted(completedPath, ticketId, out _);

	        await PublishLogAsync($"Reopening ticket {ticketId} at step {stepNumber}.", "orange");

	        var stepName = ResolveStepName(stepNumber) ?? $"Step {stepNumber}";
	        var headerStatus = _ticketIterationStatusResolver.Resolve(snapshot, stepNumber);

	        await PublishAsync(
	            CreateMessage(MessageTypes.Status, new { status = "Ready", color = "green" }),
	            current =>
	            {
	                current.Status = "Ready";
	                current.Color = "green";
	                current.WorkflowRunning = false;
	                current.Busy = false;
	                current.AwaitingUserInput = false;
	                current.CanResume = false;
	                current.NextStepToRun = 0;
	                current.CurrentStep = stepNumber;
	                current.TotalSteps = totalSteps;
	                current.CurrentStepName = stepName;
	                current.IsCurrentStepTicketIteration = true;
	                current.TicketHeaderStatus = headerStatus.IsTicketIterationStep ? headerStatus : null;
	            });
	        await PublishBusyStateAsync(false);

	        return (true, $"Reopened ticket {ticketId} and returned it to the backlog.");
	    }

	    public async Task<(bool Success, string Message)> StartSpecificTicketAsync(int stepNumber, string ticketId)
	    {
	        ticketId = (ticketId ?? string.Empty).Trim();
	        if (stepNumber <= 0)
	        {
	            return (false, "Step number must be greater than zero.");
	        }

	        if (string.IsNullOrWhiteSpace(ticketId))
	        {
	            return (false, "Ticket id is required.");
	        }

	        await StopAndDrainRuntimeAsync();
	        await _workflowStateStore.ClearActiveStepAsync();

	        var hydration = await _workflowStateStore.GetHydrationAsync();
	        var snapshot = hydration.Snapshot;
	        ApplyWorkflowDirectory(snapshot.SelectedWorkflowStepsDirectory);
	        ApplyProjectContext(snapshot.SelectedProjectName, snapshot.SelectedProjectDirectory);

	        var totalSteps = CountSteps();
	        if (totalSteps <= 0)
	        {
	            return (false, "Cannot start ticket: workflow steps directory is not available.");
	        }

	        if (stepNumber > totalSteps)
	        {
	            return (false, $"Cannot start ticket: step {stepNumber} is beyond total steps ({totalSteps}).");
	        }

	        var stepPath = ResolveStepFilePath(stepNumber);
	        if (string.IsNullOrWhiteSpace(stepPath) || !File.Exists(stepPath))
	        {
	            return (false, $"Cannot start ticket: step {stepNumber} file not found.");
	        }

	        var metadata = LoadStepMetadata(stepPath);
	        if (!string.Equals(metadata.ExecutionMode, "ticketIteration", StringComparison.OrdinalIgnoreCase))
	        {
	            return (false, $"Step {stepNumber} is not configured for ticket iteration.");
	        }

	        var completedPath = ApplyPathTokens(
	            string.IsNullOrWhiteSpace(metadata.CompletedSource) ? "{{TICKETS_DIR}}/completed.json" : metadata.CompletedSource,
	            snapshot);
	        var skippedPath = DeriveSkippedSourcePath(completedPath);
	        var requestedPath = DeriveRequestedSourcePath(completedPath);

	        // Normalize the ticket into an active state before requesting it as the next ticket.
	        TryRemoveCompletedTicket(completedPath, ticketId, out var removeCompletedError);
	        if (!string.IsNullOrWhiteSpace(removeCompletedError))
	        {
	            return (false, removeCompletedError);
	        }

	        TryRemoveSkippedTicket(skippedPath, ticketId, out var removeSkippedError);
	        if (!string.IsNullOrWhiteSpace(removeSkippedError))
	        {
	            return (false, removeSkippedError);
	        }

	        TryRemoveLegacySkippedTicketFromCompleted(completedPath, ticketId, out _);

	        var requested = TryWriteRequestedTicket(requestedPath, ticketId, out var requestError);
	        if (!requested)
	        {
	            return (false, requestError ?? "Failed to request ticket start.");
	        }

	        await PublishLogAsync($"Starting ticket {ticketId} at step {stepNumber}.", "orange");

	        await ResumeWorkflowExecutionAsync(new WorkflowRuntimeSnapshot
	        {
	            CanResume = true,
	            NextStepToRun = stepNumber,
	            CurrentStep = stepNumber,
	            TotalSteps = totalSteps,
	            CurrentStepName = ResolveStepName(stepNumber) ?? $"Step {stepNumber}",
	            SelectedWorkflowTypeId = snapshot.SelectedWorkflowTypeId,
	            SelectedWorkflowTypeName = snapshot.SelectedWorkflowTypeName,
	            SelectedWorkflowStepsDirectory = snapshot.SelectedWorkflowStepsDirectory,
	            SelectedProjectName = snapshot.SelectedProjectName,
	            SelectedProjectDirectory = snapshot.SelectedProjectDirectory,
	            IsCurrentStepTicketIteration = true,
	            Metrics = CloneMetrics(snapshot.Metrics)
	        });

	        return (true, $"Starting ticket {ticketId}.");
	    }

	    public async Task StopWorkflowAsync()
	    {
	        Task? workflowTask;
	        CancellationTokenSource? workflowCts;

        lock (_workflowLock)
        {
            workflowTask = _workflowTask;
            workflowCts = _workflowCts;
        }

        // Normal case: actively running task can be cancelled.
        if (workflowTask != null && !workflowTask.IsCompleted && workflowCts != null)
        {
            await PublishAsync(
                CreateMessage(MessageTypes.Status, new { status = "Stopping...", color = "orange" }),
                snapshot =>
                {
                    snapshot.Status = "Stopping...";
                    snapshot.Color = "orange";
                    snapshot.WorkflowRunning = true;
                    snapshot.Busy = true;
                    snapshot.AwaitingUserInput = false;
                });

            workflowCts.Cancel();

            var completed = await WaitForTaskCompletionAsync(workflowTask, StopWaitTimeout);
            if (!completed)
            {
                await PublishLogAsync(
                    $"Stop timed out after {(int)StopWaitTimeout.TotalSeconds} seconds. Forcing stop state; consider restarting if the backend is still active.",
                    "orange");
            }

            await ForceStopRuntimeStateAsync(completed ? "Stopped" : "Stopped (timeout)", "orange");
            return;
        }

        // Recovery case: no live task, but UI/persisted snapshot may still be stuck in busy/running.
        lock (_workflowLock)
        {
            _workflowTask = null;
            _workflowCts = null;
            _isWorkflowRunning = false;
        }

        lock (_queueLock)
        {
            _isAwaitingUserInput = false;
            while (_messageQueue.TryDequeue(out _))
            {
                // Drain stale queued input.
            }
        }

        while (_messageSignal.Wait(0))
        {
            // Drain signal count so the next run starts cleanly.
        }

        await ForceStopRuntimeStateAsync("Stopped", "orange");
    }

    public async Task ResetWorkflowAsync()
    {
        await StopAndDrainRuntimeAsync();
        _lastMetrics = null;
        await _workflowStateStore.ResetAsync();
        await SendToSocketsAsync(CreateMessage(MessageTypes.WorkflowReset, new { }));
    }

    public async Task PrepareForProjectSwitchAsync()
    {
        await StopAndDrainRuntimeAsync();
        _lastMetrics = null;
    }

    private async Task StopAndDrainRuntimeAsync()
    {
        Task? workflowTask;
        CancellationTokenSource? workflowCts;

        lock (_workflowLock)
        {
            workflowTask = _workflowTask;
            workflowCts = _workflowCts;
        }

        workflowCts?.Cancel();

        if (workflowTask != null)
        {
            try
            {
                var completed = await WaitForTaskCompletionAsync(workflowTask, StopWaitTimeout);
                if (!completed)
                {
                    await PublishLogAsync(
                        $"Stop timed out after {(int)StopWaitTimeout.TotalSeconds} seconds during drain. Forcing stop state; consider restarting if the backend is still active.",
                        "orange");
                }
            }
            catch
            {
                // Ignore reset-time cancellations.
            }
        }

        lock (_workflowLock)
        {
            _workflowTask = null;
            _workflowCts = null;
            _isWorkflowRunning = false;
        }

        lock (_queueLock)
        {
            _isAwaitingUserInput = false;
            while (_messageQueue.TryDequeue(out _))
            {
                // Drain queued user input.
            }
        }

        while (_messageSignal.Wait(0))
        {
            // Drain signal count so the next run starts cleanly.
        }

	        lock (_busyLock)
	        {
	            _isLlmBusy = false;
	        }
	        await PublishBusyStateAsync(false);
	    }

        private static async Task<bool> WaitForTaskCompletionAsync(Task task, TimeSpan timeout)
        {
            try
            {
                await task.WaitAsync(timeout);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch
            {
                return true;
            }
        }

	    private string? ResolveStepFilePath(int stepNumber)
	    {
	        var stepsDirectory = ResolveStepsDirectory();
	        if (!Directory.Exists(stepsDirectory))
	        {
	            return null;
	        }

	        var files = Directory.GetFiles(stepsDirectory, "*.md")
	            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
	            .ToList();

	        if (stepNumber <= 0 || stepNumber > files.Count)
	        {
	            return null;
	        }

	        return files[stepNumber - 1];
	    }

	    private string? ResolveStepName(int stepNumber)
	    {
	        var filePath = ResolveStepFilePath(stepNumber);
	        if (string.IsNullOrWhiteSpace(filePath))
	        {
	            return null;
	        }

	        return Path.GetFileName(filePath);
	    }

	    private static StepMetadata LoadStepMetadata(string mdFilePath)
	    {
	        var metadataPath = Path.Combine(
	            Path.GetDirectoryName(mdFilePath) ?? string.Empty,
	            $"{Path.GetFileNameWithoutExtension(mdFilePath)}.json");

	        if (!File.Exists(metadataPath))
	        {
	            return new StepMetadata();
	        }

	        try
	        {
	            var json = File.ReadAllText(metadataPath);
	            var metadata = JsonSerializer.Deserialize<StepMetadata>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
	                ?? new StepMetadata();
	            metadata.ExecutionMode = string.Equals(metadata.ExecutionMode, "ticketIteration", StringComparison.OrdinalIgnoreCase)
	                ? "ticketIteration"
	                : "standard";
	            return metadata;
	        }
	        catch
	        {
	            return new StepMetadata();
	        }
	    }

	    private static string ApplyPathTokens(string pathTemplate, WorkflowRuntimeSnapshot snapshot)
	    {
	        var projectRoot = snapshot.SelectedProjectDirectory ?? string.Empty;
	        var goalsDir = Path.Combine(projectRoot, "goals");
	        var filesDir = Path.Combine(projectRoot, "files");
	        var solutionDir = Path.Combine(projectRoot, "solution");
	        var planningDir = Path.Combine(projectRoot, "planning");
	        var designDir = Path.Combine(projectRoot, "design");
	        var ticketsDir = Path.Combine(projectRoot, "tickets");

	        return (pathTemplate ?? string.Empty)
	            .Replace("{{PROJECT_ROOT}}", projectRoot, StringComparison.Ordinal)
	            .Replace("{{GOALS_DIR}}", goalsDir, StringComparison.Ordinal)
	            .Replace("{{FILES_DIR}}", filesDir, StringComparison.Ordinal)
	            .Replace("{{SOLUTION_DIR}}", solutionDir, StringComparison.Ordinal)
	            .Replace("{{PLANNING_DIR}}", planningDir, StringComparison.Ordinal)
	            .Replace("{{DESIGN_DIR}}", designDir, StringComparison.Ordinal)
	            .Replace("{{TICKETS_DIR}}", ticketsDir, StringComparison.Ordinal);
	    }

	    private static bool TryAppendSkippedTicket(string skippedSourcePath, string ticketId, out string? error)
	    {
	        error = null;
	        try
	        {
	            Directory.CreateDirectory(Path.GetDirectoryName(skippedSourcePath) ?? ".");

	            JsonArray array;
	            if (File.Exists(skippedSourcePath))
	            {
	                var existing = File.ReadAllText(skippedSourcePath);
	                if (string.IsNullOrWhiteSpace(existing))
	                {
	                    array = new JsonArray();
	                }
	                else
	                {
	                    var parsed = JsonNode.Parse(existing);
	                    array = parsed as JsonArray ?? new JsonArray();
	                }
	            }
	            else
	            {
	                array = new JsonArray();
	            }

	            foreach (var element in array)
	            {
	                if (element is JsonObject obj &&
	                    obj.TryGetPropertyValue("ticket_id", out var existingIdNode) &&
	                    string.Equals(existingIdNode?.GetValue<string>()?.Trim(), ticketId, StringComparison.Ordinal))
	                {
	                    return true;
	                }
	            }

	            array.Add(new JsonObject
	            {
	                ["ticket_id"] = ticketId,
	                ["skipped_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd")
	            });

	            var serialized = array.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)
	            {
	                WriteIndented = true
	            });

	            WriteAllTextAtomic(skippedSourcePath, $"{serialized}{Environment.NewLine}");
	            return true;
	        }
	        catch (Exception ex)
	        {
	            error = ex.Message;
	            return false;
	        }
	    }

	    private static bool TryRemoveSkippedTicket(string skippedSourcePath, string ticketId, out string? error)
	    {
	        error = null;
	        try
	        {
	            if (string.IsNullOrWhiteSpace(skippedSourcePath) || !File.Exists(skippedSourcePath))
	            {
	                return true;
	            }

	            var existing = File.ReadAllText(skippedSourcePath);
	            if (string.IsNullOrWhiteSpace(existing))
	            {
	                return true;
	            }

	            var parsed = JsonNode.Parse(existing);
	            if (parsed is not JsonArray array)
	            {
	                return true;
	            }

	            var changed = false;
	            for (var i = array.Count - 1; i >= 0; i--)
	            {
	                if (array[i] is JsonObject obj &&
	                    obj.TryGetPropertyValue("ticket_id", out var existingIdNode) &&
	                    string.Equals(existingIdNode?.GetValue<string>()?.Trim(), ticketId, StringComparison.Ordinal))
	                {
	                    array.RemoveAt(i);
	                    changed = true;
	                }
	            }

	            if (!changed)
	            {
	                return true;
	            }

	            var serialized = array.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)
	            {
	                WriteIndented = true
	            });

	            WriteAllTextAtomic(skippedSourcePath, $"{serialized}{Environment.NewLine}");
	            return true;
	        }
	        catch (Exception ex)
	        {
	            error = ex.Message;
	            return false;
	        }
	    }

	    private static bool TryRemoveLegacySkippedTicketFromCompleted(string completedSourcePath, string ticketId, out string? error)
	    {
	        error = null;
	        try
	        {
	            if (string.IsNullOrWhiteSpace(completedSourcePath) || !File.Exists(completedSourcePath))
	            {
	                return true;
	            }

	            var existing = File.ReadAllText(completedSourcePath);
	            if (string.IsNullOrWhiteSpace(existing))
	            {
	                return true;
	            }

	            var parsed = JsonNode.Parse(existing);
	            if (parsed is not JsonArray array)
	            {
	                return true;
	            }

	            var changed = false;
	            for (var i = array.Count - 1; i >= 0; i--)
	            {
	                if (array[i] is not JsonObject obj)
	                {
	                    continue;
	                }

	                if (!obj.TryGetPropertyValue("ticket_id", out var existingIdNode) ||
	                    !string.Equals(existingIdNode?.GetValue<string>()?.Trim(), ticketId, StringComparison.Ordinal))
	                {
	                    continue;
	                }

	                if (obj.TryGetPropertyValue("skipped", out var skippedNode) &&
	                    skippedNode is JsonValue skippedValue &&
	                    skippedValue.TryGetValue<bool>(out var skipped) &&
	                    skipped)
	                {
	                    array.RemoveAt(i);
	                    changed = true;
	                }
	            }

	            if (!changed)
	            {
	                return true;
	            }

	            var serialized = array.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)
	            {
	                WriteIndented = true
	            });

	            WriteAllTextAtomic(completedSourcePath, $"{serialized}{Environment.NewLine}");
	            return true;
	        }
	        catch (Exception ex)
	        {
	            error = ex.Message;
	            return false;
	        }
	    }

	    private static bool TryRemoveCompletedTicket(string completedSourcePath, string ticketId, out string? error)
	    {
	        error = null;
	        try
	        {
	            if (string.IsNullOrWhiteSpace(completedSourcePath) || !File.Exists(completedSourcePath))
	            {
	                return true;
	            }

	            var existing = File.ReadAllText(completedSourcePath);
	            if (string.IsNullOrWhiteSpace(existing))
	            {
	                return true;
	            }

	            var parsed = JsonNode.Parse(existing);
	            if (parsed is not JsonArray array)
	            {
	                return true;
	            }

	            var changed = false;
	            for (var i = array.Count - 1; i >= 0; i--)
	            {
	                if (array[i] is not JsonObject obj)
	                {
	                    continue;
	                }

	                if (!obj.TryGetPropertyValue("ticket_id", out var existingIdNode) ||
	                    !string.Equals(existingIdNode?.GetValue<string>()?.Trim(), ticketId, StringComparison.Ordinal))
	                {
	                    continue;
	                }

	                if (obj.TryGetPropertyValue("skipped", out var skippedNode) &&
	                    skippedNode is JsonValue skippedValue &&
	                    skippedValue.TryGetValue<bool>(out var skipped) &&
	                    skipped)
	                {
	                    continue;
	                }

	                array.RemoveAt(i);
	                changed = true;
	            }

	            if (!changed)
	            {
	                return true;
	            }

	            var serialized = array.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)
	            {
	                WriteIndented = true
	            });

	            WriteAllTextAtomic(completedSourcePath, $"{serialized}{Environment.NewLine}");
	            return true;
	        }
	        catch (Exception ex)
	        {
	            error = ex.Message;
	            return false;
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

	    private static bool TryWriteRequestedTicket(string requestedSourcePath, string ticketId, out string? error)
	    {
	        error = null;
	        try
	        {
	            Directory.CreateDirectory(Path.GetDirectoryName(requestedSourcePath) ?? ".");
	            var obj = new JsonObject
	            {
	                ["ticket_id"] = ticketId,
	                ["requested_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd")
	            };

	            var serialized = obj.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)
	            {
	                WriteIndented = true
	            });

	            WriteAllTextAtomic(requestedSourcePath, $"{serialized}{Environment.NewLine}");
	            return true;
	        }
	        catch (Exception ex)
	        {
	            error = ex.Message;
	            return false;
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

	            var parsed = JsonNode.Parse(json);
	            if (parsed is not JsonArray array)
	            {
	                return new HashSet<string>(StringComparer.Ordinal);
	            }

	            var ids = new HashSet<string>(StringComparer.Ordinal);
	            foreach (var element in array)
	            {
	                if (element is not JsonObject obj)
	                {
	                    continue;
	                }

	                // Back-compat: earlier builds stored skipped tickets in completed.json with {"skipped": true}.
	                // Those should not be treated as completed work.
	                if (obj.TryGetPropertyValue("skipped", out var skippedNode) &&
	                    skippedNode is JsonValue skippedValue &&
	                    skippedValue.TryGetValue<bool>(out var skipped) &&
	                    skipped)
	                {
	                    continue;
	                }

	                if (!obj.TryGetPropertyValue("ticket_id", out var ticketIdNode))
	                {
	                    continue;
	                }

	                var ticketId = (ticketIdNode?.GetValue<string>() ?? string.Empty).Trim();
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

	    private static void WriteAllTextAtomic(string path, string content)
	    {
	        var directory = Path.GetDirectoryName(path);
	        if (!string.IsNullOrWhiteSpace(directory))
	        {
	            Directory.CreateDirectory(directory);
	        }

	        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
	        File.WriteAllText(tempPath, content);

	        if (File.Exists(path))
	        {
	            File.Replace(tempPath, path, null);
	        }
	        else
	        {
	            File.Move(tempPath, path);
	        }
	    }

	    private async Task ResumeWorkflowExecutionAsync(WorkflowRuntimeSnapshot snapshot)
	    {
	        ApplyWorkflowDirectory(snapshot.SelectedWorkflowStepsDirectory);
	        ApplyProjectContext(snapshot.SelectedProjectName, snapshot.SelectedProjectDirectory);

        if (!snapshot.CanResume || snapshot.NextStepToRun <= 0)
        {
            await PublishAsync(
                CreateMessage(MessageTypes.Status, new { status = "No workflow is available to resume", color = "orange" }),
                current =>
                {
                    current.Status = "No workflow is available to resume";
                    current.Color = "orange";
                });
            return;
        }

        bool alreadyRunning;
        lock (_workflowLock)
        {
            alreadyRunning = _isWorkflowRunning || _workflowTask != null;

            if (!alreadyRunning)
            {
                _workflowCts?.Cancel();
                _workflowCts = new CancellationTokenSource();
                _isWorkflowRunning = true;
            }
        }

        if (alreadyRunning)
        {
            await PublishAsync(
                CreateMessage(MessageTypes.Status, new { status = "Workflow already running", color = "orange" }),
                current =>
                {
                    current.Status = "Workflow already running";
                    current.Color = "orange";
                });
            return;
        }

        _lastMetrics = CloneMetrics(snapshot.Metrics);

        await PublishAsync(
            CreateMessage(MessageTypes.Status, new { status = $"Resuming at step {snapshot.NextStepToRun}...", color = "yellow" }),
            current =>
            {
                current.Status = $"Resuming at step {snapshot.NextStepToRun}...";
                current.Color = "yellow";
                current.WorkflowRunning = true;
                current.Busy = false;
                current.AwaitingUserInput = false;
                current.CanResume = false;
            });

        StartWorkflowTask(snapshot.NextStepToRun, CloneMetrics(snapshot.Metrics));
    }

    private async Task ResumeActiveStepContextAsync(ActiveStepContextState activeStep, UserInput? initialInput = null)
    {
        ApplyWorkflowDirectory(activeStep.WorkflowStepsDirectory);
        ApplyProjectContext(activeStep.ProjectName, activeStep.ProjectDirectory);

        bool alreadyRunning;
        lock (_workflowLock)
        {
            alreadyRunning = _isWorkflowRunning || _workflowTask != null;

            if (!alreadyRunning)
            {
                _workflowCts?.Cancel();
                _workflowCts = new CancellationTokenSource();
                _isWorkflowRunning = true;
            }
        }

        if (alreadyRunning)
        {
            if (initialInput != null)
            {
                EnqueueUserInput(initialInput);
            }

            return;
        }

        _lastMetrics ??= CloneMetrics((await _workflowStateStore.GetHydrationAsync()).Snapshot.Metrics);

        StartRecoveredActiveStepTask(activeStep, initialInput);
    }

    private async Task RunRecoveredActiveStepAsync(ActiveStepContextState activeStep, UserInput? initialInput, CancellationToken ct)
    {
        try
        {
            var pendingInput = initialInput;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (pendingInput == null)
                {
                    await OpenUserInputWindowAsync();
                    pendingInput = await WaitForUserInputAsync(ct);
                }
                else
                {
                    await PublishAsync(
                        CreateMessage(MessageTypes.Status, new { status = "Running", color = "cyan" }),
                        snapshot =>
                        {
                            snapshot.Status = "Running";
                            snapshot.Color = "cyan";
                            snapshot.WorkflowRunning = true;
                            snapshot.AwaitingUserInput = false;
                            snapshot.CanResume = false;
                        });
                    await PublishBusyStateAsync(true);
                }

                if (pendingInput.Kind == UserInputKind.Continue)
                {
                    await _workflowStateStore.ClearActiveStepAsync();

                    if (activeStep.StepNumber >= activeStep.TotalSteps)
                    {
                        await CompleteWorkflowFromCurrentSnapshotAsync();
                        return;
                    }

                    var hydration = await _workflowStateStore.GetHydrationAsync();
                    await RunWorkflowAsync(ct, activeStep.StepNumber + 1, CloneMetrics(hydration.Snapshot.Metrics));
                    return;
                }

                using var scope = _scopeFactory.CreateScope();
                var stepConversationService = scope.ServiceProvider.GetRequiredService<IStepConversationService>();
                var turnResult = await stepConversationService.ProcessUserMessageAsync(
                    activeStep.Messages,
                    pendingInput.Message,
                    ct,
                    onAssistantToolCallResponseAsync: async llmResponse =>
                    {
                        await PublishLlmResponseAsync(
                            llmResponse.Content,
                            llmResponse.ReasoningContent);
                    },
                    onToolRequestedAsync: PublishToolRequestAsync,
                    onToolResultAsync: PublishToolExecutionAsync,
                    onPseudoToolCallWarningAsync: async warningMessage =>
                    {
                        await PublishLogAsync(warningMessage, "orange");
                        await PublishLlmResponseAsync(warningMessage, string.Empty);
                    });

                activeStep.Messages = turnResult.UpdatedStepContext;
                activeStep.LastUpdatedUtc = DateTime.UtcNow;
                await _workflowStateStore.SaveActiveStepAsync(activeStep);

                if (!string.IsNullOrWhiteSpace(turnResult.FinalResponse.Content) ||
                    !string.IsNullOrWhiteSpace(turnResult.FinalResponse.ReasoningContent))
                {
                    await PublishLlmResponseAsync(
                        turnResult.FinalResponse.Content,
                        turnResult.FinalResponse.ReasoningContent);
                }

                await PublishConversationTurnTokenStatsAsync(activeStep.StepNumber, turnResult);

                pendingInput = null;
            }
        }
        catch (OperationCanceledException)
        {
            lock (_queueLock)
            {
                _isAwaitingUserInput = false;
            }

            await PublishBusyStateAsync(false);
            await PublishAsync(
                CreateMessage(MessageTypes.Status, new { status = "Stopped", color = "orange" }),
                snapshot =>
                {
                    snapshot.Status = "Stopped";
                    snapshot.Color = "orange";
                    snapshot.WorkflowRunning = false;
                    snapshot.Busy = false;
                    snapshot.AwaitingUserInput = false;
                    snapshot.CanResume = true;
                    snapshot.NextStepToRun = activeStep.StepNumber;
                });
        }
    }

    public async Task<UserInput> WaitForUserInputAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            UserInput? input = null;
            lock (_queueLock)
            {
                if (_messageQueue.TryDequeue(out var queuedInput))
                {
                    input = queuedInput;
                    _isAwaitingUserInput = false;
                }
            }

            if (input != null)
            {
                await PublishAsync(
                    CreateMessage(MessageTypes.Status, new { status = "Running", color = "cyan" }),
                    snapshot =>
                    {
                        snapshot.Status = "Running";
                        snapshot.Color = "cyan";
                        snapshot.AwaitingUserInput = false;
                        snapshot.WorkflowRunning = true;
                    });
                await PublishBusyStateAsync(true);
                return input;
            }

            await _messageSignal.WaitAsync(ct);
        }
    }

    public async Task OpenUserInputWindowAsync()
    {
        lock (_queueLock)
        {
            _isAwaitingUserInput = true;
        }

        await PublishAsync(
            CreateMessage(MessageTypes.Status, new { status = "Waiting for input", color = "yellow" }),
            snapshot =>
            {
                snapshot.Status = "Waiting for input";
                snapshot.Color = "yellow";
                snapshot.AwaitingUserInput = true;
                snapshot.WorkflowRunning = true;
                snapshot.CanResume = snapshot.CurrentStep > 0;
                snapshot.NextStepToRun = snapshot.CurrentStep;
            });
        await PublishBusyStateAsync(false);
    }

    public async Task PublishStepStartAsync(int stepNumber, int totalSteps, string stepName, string stepContent, bool isTicketIterationStep)
    {
        lock (_queueLock)
        {
            _isAwaitingUserInput = false;
        }

        var hydration = await _workflowStateStore.GetHydrationAsync();
        var headerStatus = _ticketIterationStatusResolver.Resolve(hydration.Snapshot, stepNumber);

        await PublishAsync(
            CreateMessage(MessageTypes.StepStart, new
            {
                stepNumber,
                totalSteps,
                name = stepName,
                content = stepContent,
                isTicketIterationStep = isTicketIterationStep,
                ticketHeaderStatus = ToClientTicketHeaderStatus(headerStatus)
            }),
            snapshot =>
            {
                snapshot.CurrentStep = stepNumber;
                snapshot.TotalSteps = totalSteps;
                snapshot.CurrentStepName = stepName;
                snapshot.IsCurrentStepTicketIteration = isTicketIterationStep;
                snapshot.TicketHeaderStatus = isTicketIterationStep ? headerStatus : null;
                snapshot.WorkflowRunning = true;
                snapshot.AwaitingUserInput = false;
                snapshot.CanResume = true;
                snapshot.NextStepToRun = stepNumber;
            });

        await PublishBusyStateAsync(true);
    }

    public async Task PublishStepCompleteAsync(int stepNumber, StepExecutionResult result, bool isOpenChatStep = false)
    {
        var hydration = await _workflowStateStore.GetHydrationAsync();
        var headerStatus = _ticketIterationStatusResolver.Resolve(hydration.Snapshot, stepNumber);

        await PublishAsync(
            CreateMessage(MessageTypes.StepComplete, new
            {
                stepNumber,
                isOpenChatStep,
                ticketProgress = ToClientTicketProgress(result.TicketProgress),
                ticketHeaderStatus = ToClientTicketHeaderStatus(headerStatus)
            }),
            snapshot =>
            {
                snapshot.CurrentStep = stepNumber;
                snapshot.CurrentStepName = $"Step {stepNumber}";
                snapshot.IsCurrentStepTicketIteration = headerStatus.IsTicketIterationStep;
                snapshot.TicketHeaderStatus = headerStatus.IsTicketIterationStep ? headerStatus : null;
                snapshot.CanResume = true;
                snapshot.NextStepToRun = stepNumber;
                ApplyStepMetrics(snapshot.Metrics, result, success: true);
                ApplyTicketProgressSnapshot(snapshot, result.TicketProgress);
            });

        await WriteStepBenchmarkReportAsync(stepNumber, result, "completed");
        await PublishPerMessageTokenStatsAsync(stepNumber, result, "completed");
    }

    public async Task PublishStepFailedAsync(int stepNumber, string error, StepExecutionResult result)
    {
        lock (_queueLock)
        {
            _isAwaitingUserInput = false;
        }

        var hydration = await _workflowStateStore.GetHydrationAsync();
        var headerStatus = _ticketIterationStatusResolver.Resolve(hydration.Snapshot, stepNumber);

        await PublishAsync(
            CreateMessage(MessageTypes.StepFailed, new
            {
                stepNumber,
                error,
                ticketProgress = ToClientTicketProgress(result.TicketProgress),
                ticketHeaderStatus = ToClientTicketHeaderStatus(headerStatus)
            }),
            snapshot =>
            {
                snapshot.CurrentStep = stepNumber;
                snapshot.CurrentStepName = $"Step {stepNumber} failed";
                snapshot.IsCurrentStepTicketIteration = headerStatus.IsTicketIterationStep;
                snapshot.TicketHeaderStatus = headerStatus.IsTicketIterationStep ? headerStatus : null;
                snapshot.CanResume = true;
                snapshot.NextStepToRun = stepNumber;
                ApplyStepMetrics(snapshot.Metrics, result, success: false);
                ApplyTicketProgressSnapshot(snapshot, result.TicketProgress);
            });

        await WriteStepBenchmarkReportAsync(stepNumber, result, "failed");
        await PublishPerMessageTokenStatsAsync(stepNumber, result, "failed");
        await PublishBusyStateAsync(false);
    }

    public Task PublishLlmResponseAsync(string content)
    {
        return PublishLlmResponseAsync(content, string.Empty);
    }

    public async Task PublishLlmResponseAsync(string content, string reasoningContent)
    {
        await PublishAsync(CreateMessage(MessageTypes.LlmResponse, new { content, reasoningContent }));
    }

    public async Task PublishUserQuestionAsync(string question, string source = MessageSources.Web, string? sourceLabel = null)
    {
        await PublishAsync(CreateMessage(MessageTypes.UserQuestion, new UserQuestionPayload
        {
            question = question,
            source = string.IsNullOrWhiteSpace(source) ? MessageSources.Web : source,
            sourceLabel = sourceLabel ?? (string.Equals(source, MessageSources.Telegram, StringComparison.OrdinalIgnoreCase) ? "Telegram" : "Web")
        }));
    }

    public async Task PublishToolRequestAsync(string toolName, string requestSummary = "")
    {
        await PublishAsync(CreateMessage(
            MessageTypes.ToolRequest,
            new
            {
                toolName,
                status = "Requested",
                requestSummary
            }),
            snapshot =>
            {
                snapshot.Status = "Running";
                snapshot.Color = "cyan";
                snapshot.WorkflowRunning = true;
                snapshot.Busy = true;
                snapshot.AwaitingUserInput = false;
            });
    }

    public async Task PublishToolExecutionAsync(string toolName, bool success, string output, string requestSummary = "")
    {
        await PublishAsync(CreateMessage(
            MessageTypes.ToolExecution,
            new
            {
                toolName,
                success,
                status = success ? "Success" : "Failed",
                output,
                requestSummary
            }),
            snapshot =>
            {
                snapshot.Status = "Running";
                snapshot.Color = "cyan";
                snapshot.WorkflowRunning = true;
                snapshot.Busy = true;
                snapshot.AwaitingUserInput = false;
            });
    }

    public Task PublishToolExecutionAsync(ToolResult toolResult)
    {
        var output = BuildToolExecutionDisplay(toolResult);
        var publishTask = PublishToolExecutionAsync(
            toolResult.ToolName,
            toolResult.Success,
            output,
            toolResult.RequestSummary);

        if (!toolResult.Success && IsTimeoutError(output))
        {
            return Task.WhenAll(
                publishTask,
                PublishLogAsync($"Tool timeout: {toolResult.ToolName} exceeded the configured timeout.", "red"));
        }

        return publishTask;
    }

    private static string BuildToolExecutionDisplay(ToolResult toolResult)
    {
        if (toolResult.Success)
        {
            return !string.IsNullOrWhiteSpace(toolResult.Output)
                ? toolResult.Output
                : toolResult.Error;
        }

        if (string.IsNullOrWhiteSpace(toolResult.Output))
        {
            return toolResult.Error;
        }

        if (string.IsNullOrWhiteSpace(toolResult.Error))
        {
            return toolResult.Output;
        }

        return $"{toolResult.Error}\n\nStdout:\n{toolResult.Output}";
    }

    public async Task PublishLogAsync(string message, string color = "gray")
    {
        await PublishAsync(CreateMessage(MessageTypes.Log, new { message, color }));
    }

    public async Task PublishTelegramCommandAsync(string command, string responseMessage)
    {
        await PublishUserQuestionAsync(responseMessage, MessageSources.Telegram, "Telegram Command");
        await PublishLogAsync(responseMessage, "cyan");
    }

    public async Task PersistActiveStepContextAsync(ActiveStepContextState activeStep)
    {
        var hydration = await _workflowStateStore.GetHydrationAsync();
        if (string.IsNullOrWhiteSpace(activeStep.RunId))
        {
            activeStep.RunId = hydration.Snapshot.RunId;
        }

        if (string.IsNullOrWhiteSpace(activeStep.WorkflowTypeId))
        {
            activeStep.WorkflowTypeId = hydration.Snapshot.SelectedWorkflowTypeId;
        }

        if (string.IsNullOrWhiteSpace(activeStep.WorkflowTypeName))
        {
            activeStep.WorkflowTypeName = hydration.Snapshot.SelectedWorkflowTypeName;
        }

        if (string.IsNullOrWhiteSpace(activeStep.WorkflowStepsDirectory))
        {
            activeStep.WorkflowStepsDirectory = hydration.Snapshot.SelectedWorkflowStepsDirectory;
        }

        if (string.IsNullOrWhiteSpace(activeStep.ProjectName))
        {
            activeStep.ProjectName = hydration.Snapshot.SelectedProjectName;
        }

        if (string.IsNullOrWhiteSpace(activeStep.ProjectDirectory))
        {
            activeStep.ProjectDirectory = hydration.Snapshot.SelectedProjectDirectory;
        }

        await _workflowStateStore.SaveActiveStepAsync(activeStep);
    }

    public async Task ClearActiveStepContextAsync()
    {
        await _workflowStateStore.ClearActiveStepAsync();
    }

    private bool EnqueueUserInput(UserInput input)
    {
        lock (_queueLock)
        {
            if (!_isWorkflowRunning || !_isAwaitingUserInput)
            {
                return false;
            }

            _messageQueue.Enqueue(input);
        }

        _messageSignal.Release();

        if (_debugMode)
        {
            var description = input.Kind == UserInputKind.Continue ? "[continue]" : input.Message;
            Console.WriteLine($"[WebSocket] User input queued: {description}");
        }

        return true;
    }

    private async Task CompleteWorkflowFromCurrentSnapshotAsync()
    {
        var hydration = await _workflowStateStore.GetHydrationAsync();
        var metrics = CloneMetrics(hydration.Snapshot.Metrics);
        _lastMetrics = metrics;

        lock (_queueLock)
        {
            _isAwaitingUserInput = false;
        }

        await PublishBusyStateAsync(false);

        await PublishAsync(
            CreateMessage(
                MessageTypes.WorkflowEnd,
                new
                {
                    totalSteps = metrics.TotalSteps,
                    successfulSteps = metrics.SuccessfulSteps,
                    failedSteps = metrics.FailedSteps
                }),
            snapshot =>
            {
                snapshot.WorkflowRunning = false;
                snapshot.Busy = false;
                snapshot.AwaitingUserInput = false;
                snapshot.CurrentStep = metrics.TotalSteps;
                snapshot.TotalSteps = metrics.TotalSteps;
                snapshot.CurrentStepName = "Completed";
                snapshot.Metrics = CloneMetrics(metrics);
                snapshot.CanResume = false;
                snapshot.NextStepToRun = 0;
            });

        await PublishAsync(
            CreateMessage(MessageTypes.Status, new { status = "Ready", color = "green" }),
            snapshot =>
            {
                snapshot.Status = "Ready";
                snapshot.Color = "green";
                snapshot.WorkflowRunning = false;
                snapshot.CanResume = false;
                snapshot.NextStepToRun = 0;
            });
    }

    private async Task PublishAsync(AgentMessage message, Action<WorkflowRuntimeSnapshot>? updateSnapshot = null)
    {
        await _workflowStateStore.UpdateAsync(updateSnapshot, message);
        await SendToSocketsAsync(message);
    }

    private async Task SendToSocketsAsync(AgentMessage message)
    {
        EnsureTimestamp(message);

        var json = JsonSerializer.Serialize(message, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        foreach (var socket in _webSockets.Values.Where(s => s.State == WebSocketState.Open))
        {
            try
            {
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                if (_debugMode)
                {
                    Console.WriteLine($"[WebSocket] Send error: {ex.Message}");
                }
            }
        }
    }

    private async Task SendToSocketAsync(WebSocket socket, AgentMessage message)
    {
        EnsureTimestamp(message);

        if (socket.State != WebSocketState.Open)
        {
            return;
        }

        var json = JsonSerializer.Serialize(message, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task PublishBusyStateAsync(bool isBusy)
    {
        lock (_busyLock)
        {
            _isLlmBusy = isBusy;
        }

        await PublishAsync(
            CreateMessage(MessageTypes.Busy, new { isBusy }),
            snapshot =>
            {
                snapshot.Busy = isBusy;
                if (isBusy)
                {
                    snapshot.AwaitingUserInput = false;
                }
            });
    }

    private async Task SendHydrationAsync(WebSocket socket)
    {
        var hydration = await _workflowStateStore.GetHydrationAsync();
        await SendToSocketAsync(socket, CreateMessage(
            MessageTypes.Hydrate,
            new
            {
                snapshot = ToClientSnapshot(hydration.Snapshot),
                history = hydration.History.Select(item => item.Message).ToList()
            }));
    }

    private static object ToClientSnapshot(WorkflowRuntimeSnapshot snapshot)
    {
        return new
        {
            runId = snapshot.RunId,
            status = snapshot.Status,
            color = snapshot.Color,
            workflowRunning = snapshot.WorkflowRunning,
            busy = snapshot.Busy,
            awaitingUserInput = snapshot.AwaitingUserInput,
            canResume = snapshot.CanResume,
            nextStepToRun = snapshot.NextStepToRun,
            selectedWorkflowTypeId = snapshot.SelectedWorkflowTypeId,
            selectedWorkflowTypeName = snapshot.SelectedWorkflowTypeName,
            selectedProjectName = snapshot.SelectedProjectName,
            selectedProjectDirectory = snapshot.SelectedProjectDirectory,
            isCurrentStepTicketIteration = snapshot.IsCurrentStepTicketIteration,
            ticketHeaderStatus = ToClientTicketHeaderStatus(snapshot.TicketHeaderStatus),
            currentStep = snapshot.CurrentStep,
            totalSteps = snapshot.TotalSteps,
            currentStepName = snapshot.CurrentStepName,
            ticketProgress = ToClientTicketProgress(snapshot.TicketProgress),
            hasHistory = snapshot.HasHistory,
            lastUpdatedUtc = snapshot.LastUpdatedUtc,
            metrics = new
            {
                totalSteps = snapshot.Metrics.TotalSteps,
                successfulSteps = snapshot.Metrics.SuccessfulSteps,
                failedSteps = snapshot.Metrics.FailedSteps,
                totalDurationMs = snapshot.Metrics.TotalDurationMs,
                averageStepDurationMs = snapshot.Metrics.AverageStepDurationMs,
                totalTokensUsed = snapshot.Metrics.TotalTokensUsed
            }
        };
    }

    private void StartWorkflowTask(int startStepNumber, WorkflowMetrics? existingMetrics)
    {
        var token = _workflowCts?.Token ?? CancellationToken.None;
        Task? currentTask = null;
        currentTask = Task.Run(async () =>
        {
            try
            {
                await RunWorkflowAsync(token, startStepNumber, existingMetrics);
            }
            catch (Exception ex)
            {
                await HandleUnhandledWorkflowErrorAsync(ex.Message);
            }
            finally
            {
                lock (_workflowLock)
                {
                    if (ReferenceEquals(_workflowTask, currentTask))
                    {
                        _isWorkflowRunning = false;
                        _workflowTask = null;
                    }
                }
            }
        });

        lock (_workflowLock)
        {
            _workflowTask = currentTask;
        }
    }

    private void StartRecoveredActiveStepTask(ActiveStepContextState activeStep, UserInput? initialInput)
    {
        var token = _workflowCts?.Token ?? CancellationToken.None;
        Task? currentTask = null;
        currentTask = Task.Run(async () =>
        {
            try
            {
                await RunRecoveredActiveStepAsync(activeStep, initialInput, token);
            }
            catch (Exception ex)
            {
                await HandleUnhandledWorkflowErrorAsync(ex.Message);
            }
            finally
            {
                lock (_workflowLock)
                {
                    if (ReferenceEquals(_workflowTask, currentTask))
                    {
                        _isWorkflowRunning = false;
                        _workflowTask = null;
                    }
                }
            }
        });

        lock (_workflowLock)
        {
            _workflowTask = currentTask;
        }
    }

    private AgentMessage CreateMessage(string type, object payload)
    {
        return new AgentMessage
        {
            type = type,
            payload = payload,
            timestamp = DateTime.UtcNow.ToString("O")
        };
    }

    private static WorkflowMetrics CloneMetrics(WorkflowMetrics metrics)
    {
        return new WorkflowMetrics
        {
            TotalSteps = metrics.TotalSteps,
            SuccessfulSteps = metrics.SuccessfulSteps,
            FailedSteps = metrics.FailedSteps,
            TotalDurationMs = metrics.TotalDurationMs,
            AverageStepDurationMs = metrics.AverageStepDurationMs,
            TotalTokensUsed = metrics.TotalTokensUsed
        };
    }

    private static void ApplyStepMetrics(WorkflowMetrics metrics, StepExecutionResult result, bool success)
    {
        metrics.TotalSteps = Math.Max(metrics.TotalSteps, metrics.SuccessfulSteps + metrics.FailedSteps + (success ? 1 : 0));

        if (success)
        {
            metrics.SuccessfulSteps++;
        }
        else
        {
            metrics.FailedSteps++;
        }

        metrics.TotalDurationMs += Math.Max(result.DurationMs, 0);
        metrics.TotalTokensUsed += result.LLMResponse?.Usage.TotalTokens ?? 0;

        var processedSteps = metrics.SuccessfulSteps + metrics.FailedSteps;
        metrics.AverageStepDurationMs = processedSteps > 0
            ? (double)metrics.TotalDurationMs / processedSteps
            : 0;
    }

    private static object? ToClientTicketProgress(TicketIterationProgress? progress)
    {
        if (progress == null || !progress.IsTicketIterationStep)
        {
            return null;
        }

        return new
        {
            isTicketIterationStep = progress.IsTicketIterationStep,
            stepKey = progress.StepKey,
            stepName = progress.StepName,
            ticketId = progress.TicketId,
            ticketTitle = progress.TicketTitle,
            attempt = progress.Attempt,
            maxAttempts = progress.MaxAttempts,
            totalTickets = progress.TotalTickets,
            completedTickets = progress.CompletedTickets,
            remainingTickets = progress.RemainingTickets,
            retryExhausted = progress.RetryExhausted,
            status = progress.Status,
            lastUpdatedUtc = progress.LastUpdatedUtc
        };
    }

    private static object? ToClientTicketHeaderStatus(TicketIterationHeaderProgress? status)
    {
        if (status == null || !status.IsTicketIterationStep)
        {
            return null;
        }

        return new
        {
            isTicketIterationStep = status.IsTicketIterationStep,
            isAvailable = status.IsAvailable,
            totalTickets = status.TotalTickets,
            completedTickets = status.CompletedTickets,
            remainingTickets = status.RemainingTickets,
            currentTicketOrdinal = status.CurrentTicketOrdinal,
            currentTicketId = status.CurrentTicketId,
            warning = status.Warning
        };
    }

    private static void ApplyTicketProgressSnapshot(WorkflowRuntimeSnapshot snapshot, TicketIterationProgress? progress)
    {
        if (progress == null)
        {
            return;
        }

        snapshot.TicketProgress = new TicketIterationProgress
        {
            IsTicketIterationStep = progress.IsTicketIterationStep,
            StepKey = progress.StepKey,
            StepName = progress.StepName,
            TicketId = progress.TicketId,
            TicketTitle = progress.TicketTitle,
            Attempt = progress.Attempt,
            MaxAttempts = progress.MaxAttempts,
            TotalTickets = progress.TotalTickets,
            CompletedTickets = progress.CompletedTickets,
            RemainingTickets = progress.RemainingTickets,
            RetryExhausted = progress.RetryExhausted,
            Status = progress.Status,
            LastUpdatedUtc = progress.LastUpdatedUtc,
            ContextMessages = new List<ChatMessage>(progress.ContextMessages ?? new List<ChatMessage>())
        };

        if (string.IsNullOrWhiteSpace(progress.TicketId))
        {
            return;
        }

        snapshot.TicketIterationAudits ??= new List<TicketIterationAuditEntry>();
        var existing = snapshot.TicketIterationAudits.FirstOrDefault(entry =>
            string.Equals(entry.StepKey, progress.StepKey, StringComparison.Ordinal) &&
            string.Equals(entry.TicketId, progress.TicketId, StringComparison.Ordinal));

        if (existing == null)
        {
            snapshot.TicketIterationAudits.Add(new TicketIterationAuditEntry
            {
                StepKey = progress.StepKey,
                StepName = progress.StepName,
                TicketId = progress.TicketId,
                TicketTitle = progress.TicketTitle,
                AttemptsUsed = progress.Attempt,
                Status = progress.Status,
                LastUpdatedUtc = progress.LastUpdatedUtc,
                Messages = new List<ChatMessage>(progress.ContextMessages ?? new List<ChatMessage>())
            });
            return;
        }

        existing.StepName = progress.StepName;
        existing.TicketTitle = progress.TicketTitle;
        existing.AttemptsUsed = progress.Attempt;
        existing.Status = progress.Status;
        existing.LastUpdatedUtc = progress.LastUpdatedUtc;
        existing.Messages = new List<ChatMessage>(progress.ContextMessages ?? new List<ChatMessage>());
    }

    private static void EnsureTimestamp(AgentMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.timestamp))
        {
            message.timestamp = DateTime.UtcNow.ToString("O");
        }
    }

    private static bool IsTimeoutError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
    }

    public enum UserInputKind
    {
        Message,
        Continue
    }

    private static string ResolveWorkflowDirectory(
        string configuredDirectory,
        string currentDirectory,
        string appBaseDirectory,
        string preferredFolderName)
    {
        var configured = string.IsNullOrWhiteSpace(configuredDirectory)
            ? $"./{preferredFolderName}"
            : configuredDirectory.Trim();

        if (Path.IsPathRooted(configured))
        {
            return Path.GetFullPath(configured);
        }

        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(currentDirectory, configured)),
            Path.GetFullPath(Path.Combine(appBaseDirectory, configured)),
            Path.GetFullPath(Path.Combine(appBaseDirectory, "..", "..", "..", configured)),
            Path.GetFullPath(Path.Combine(appBaseDirectory, "..", "..", "..", "..", configured))
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates[0];
    }

    public class UserInput
    {
        public UserInputKind Kind { get; set; }
        public string Message { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }

    private int CountSteps()
    {
        var stepsDirectory = ResolveStepsDirectory();
        if (!Directory.Exists(stepsDirectory))
        {
            return 0;
        }

        return Directory.GetFiles(stepsDirectory, "*.md").Length;
    }

    private string ResolveStepsDirectory() => ResolveWorkflowDirectory(
        _settings.StepsDirectory,
        Directory.GetCurrentDirectory(),
        AppContext.BaseDirectory,
        "workflowtypes");

    private string ResolveWorkflowTypesDirectory() => ResolveWorkflowDirectory(
        _settings.WorkflowTypesDirectory,
        Directory.GetCurrentDirectory(),
        AppContext.BaseDirectory,
        "workflowtypes");

    private WorkflowTypeDefinition? ResolveWorkflowTypeForStart(string? requestedWorkflowTypeId, WorkflowRuntimeSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(requestedWorkflowTypeId))
        {
            return _workflowTypeCatalog.GetWorkflowType(requestedWorkflowTypeId);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.SelectedWorkflowTypeId))
        {
            var selected = _workflowTypeCatalog.GetWorkflowType(snapshot.SelectedWorkflowTypeId);
            if (selected != null)
            {
                return selected;
            }
        }

        return _workflowTypeCatalog.GetDefaultWorkflowType();
    }

    private void ApplyWorkflowType(WorkflowTypeDefinition workflowType)
    {
        ApplyWorkflowDirectory(workflowType.StepsDirectory);
    }

    private void ApplyWorkflowDirectory(string? stepsDirectory)
    {
        if (!string.IsNullOrWhiteSpace(stepsDirectory))
        {
            _settings.StepsDirectory = stepsDirectory;
        }
    }

    private void ApplyProjectContext(string? projectName, string? projectDirectory)
    {
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            _settings.SelectedProjectName = projectName;
        }

        if (!string.IsNullOrWhiteSpace(projectDirectory))
        {
            _settings.SelectedProjectDirectory = projectDirectory;
        }
    }

    private async Task<ActiveStepContextState?> TryCreateRecoveredActiveStepContextAsync(WorkflowRuntimeSnapshot snapshot)
    {
        var stepNumber = snapshot.NextStepToRun > 0 ? snapshot.NextStepToRun : snapshot.CurrentStep;
        if (stepNumber <= 0)
        {
            return null;
        }

        var stepsDirectory = !string.IsNullOrWhiteSpace(snapshot.SelectedWorkflowStepsDirectory)
            ? snapshot.SelectedWorkflowStepsDirectory
            : _settings.StepsDirectory;
        if (string.IsNullOrWhiteSpace(stepsDirectory) || !Directory.Exists(stepsDirectory))
        {
            return null;
        }

        ApplyWorkflowDirectory(stepsDirectory);
        ApplyProjectContext(snapshot.SelectedProjectName, snapshot.SelectedProjectDirectory);

        var stepFiles = Directory.GetFiles(stepsDirectory, "*.md")
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToList();

        if (stepNumber > stepFiles.Count || stepNumber <= 0)
        {
            return null;
        }

        var stepFilePath = stepFiles[stepNumber - 1];
        var stepContent = WorkspaceContext.ApplyPathTokens(await File.ReadAllTextAsync(stepFilePath), _settings);
        var systemPrompt = ToolCallSafety.SystemPrompt;
        var scopeMessage = WorkspaceContext.BuildScopeMessage(_settings);
        var stepPrompt = $"Step {stepNumber}: {stepContent}";

        var recovered = new ActiveStepContextState
        {
            RunId = snapshot.RunId,
            StepNumber = stepNumber,
            TotalSteps = snapshot.TotalSteps,
            StepName = Path.GetFileName(stepFilePath),
            StepFilePath = stepFilePath,
            WorkflowTypeId = snapshot.SelectedWorkflowTypeId,
            WorkflowTypeName = snapshot.SelectedWorkflowTypeName,
            WorkflowStepsDirectory = stepsDirectory,
            ProjectName = snapshot.SelectedProjectName,
            ProjectDirectory = snapshot.SelectedProjectDirectory,
            LastUpdatedUtc = DateTime.UtcNow,
            Messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "system", Content = scopeMessage },
                new() { Role = "user", Content = stepPrompt }
            }
        };

        await _workflowStateStore.SaveActiveStepAsync(recovered);
        await PublishAsync(
            CreateMessage(MessageTypes.Log, new
            {
                message = $"Rebuilt context for step {stepNumber} from workflow files so you can continue with a message.",
                color = "orange"
            }),
            current =>
            {
                current.Status = "Recovered step context";
                current.Color = "orange";
                current.WorkflowRunning = false;
                current.Busy = false;
                current.AwaitingUserInput = true;
                current.CanResume = true;
                current.NextStepToRun = stepNumber;
            });

        return recovered;
    }

    private async Task HandleUnhandledWorkflowErrorAsync(string errorMessage)
    {
        lock (_queueLock)
        {
            _isAwaitingUserInput = false;
            while (_messageQueue.TryDequeue(out _))
            {
                // Drain stale input after fatal workflow error.
            }
        }

        while (_messageSignal.Wait(0))
        {
            // Drain signal count after fatal workflow error.
        }

        await PublishBusyStateAsync(false);
        await PublishAsync(
            CreateMessage(MessageTypes.Log, new { message = $"Workflow error: {errorMessage}", color = "red" }),
            snapshot =>
            {
                snapshot.WorkflowRunning = false;
                snapshot.Busy = false;
                snapshot.AwaitingUserInput = false;
                snapshot.CanResume = snapshot.CurrentStep > 0 && snapshot.CurrentStep <= snapshot.TotalSteps;
                snapshot.NextStepToRun = snapshot.CanResume ? snapshot.CurrentStep : 0;
            });

        await PublishAsync(
            CreateMessage(MessageTypes.Status, new { status = "Stopped (error)", color = "red" }),
            snapshot =>
            {
                snapshot.Status = "Stopped (error)";
                snapshot.Color = "red";
                snapshot.WorkflowRunning = false;
                snapshot.Busy = false;
                snapshot.AwaitingUserInput = false;
                snapshot.CanResume = snapshot.CurrentStep > 0 && snapshot.CurrentStep <= snapshot.TotalSteps;
                snapshot.NextStepToRun = snapshot.CanResume ? snapshot.CurrentStep : 0;
            });

        await PublishStatsLinkIfAvailableAsync("failed", _lastRunProjectDirectory);
    }

    private async Task ForceStopRuntimeStateAsync(string status, string color)
    {
        lock (_workflowLock)
        {
            _isWorkflowRunning = false;
            _workflowTask = null;
            _workflowCts = null;
        }

        lock (_queueLock)
        {
            _isAwaitingUserInput = false;
            while (_messageQueue.TryDequeue(out _))
            {
                // Drain stale input after stop.
            }
        }

        while (_messageSignal.Wait(0))
        {
            // Drain signal count after stop.
        }

        var activeStep = await _workflowStateStore.GetActiveStepAsync();
        var hasActiveStep = activeStep != null;
        var resumeStep = hasActiveStep ? activeStep!.StepNumber : 0;

        await PublishBusyStateAsync(false);
        await PublishAsync(
            CreateMessage(MessageTypes.Status, new { status, color }),
            snapshot =>
            {
                snapshot.Status = status;
                snapshot.Color = color;
                snapshot.WorkflowRunning = false;
                snapshot.Busy = false;
                snapshot.AwaitingUserInput = hasActiveStep;
                snapshot.CanResume = hasActiveStep || (snapshot.CurrentStep > 0 && snapshot.CurrentStep <= snapshot.TotalSteps);
                snapshot.NextStepToRun = hasActiveStep
                    ? resumeStep
                    : (snapshot.CanResume ? snapshot.CurrentStep : 0);
            });

        await PublishStatsLinkIfAvailableAsync("stopped", _lastRunProjectDirectory);

    async Task PublishTelegramCommandAsync(string command, string responseMessage)
    {
        // Publish the command itself as a user message from Telegram source
        await PublishUserQuestionAsync(command, MessageSources.Telegram, "Telegram");
        
        // Also publish the response as a log message (cyan terminal output)
        await PublishLogAsync(responseMessage, "cyan");
    }

    }

    private async Task PublishStatsLinkIfAvailableAsync(string status, string? projectRootOverride = null)
    {
        if (!_settings.EnableBenchmarkHtmlOutput)
        {
            await PublishLogAsync("[STATS] benchmark.html output is disabled in settings.", "gray");
            return;
        }

        var hydration = await _workflowStateStore.GetHydrationAsync();
        var projectRoot = string.IsNullOrWhiteSpace(projectRootOverride)
            ? hydration.Snapshot.SelectedProjectDirectory
            : projectRootOverride;
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            await PublishLogAsync("[STATS] No selected project directory; benchmark report link not available.", "gray");
            return;
        }

        var fullProjectRoot = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(fullProjectRoot))
        {
            await PublishLogAsync($"[STATS] Project directory does not exist; benchmark report link not available: {fullProjectRoot}", "gray");
            return;
        }

        var statsPath = Path.GetFullPath(Path.Combine(fullProjectRoot, "stats", "benchmark.html"));
        if (!IsSubPathOf(statsPath, fullProjectRoot))
        {
            await PublishLogAsync("[STATS] Stats link was blocked because the resolved path escaped project scope.", "red");
            return;
        }

        if (!File.Exists(statsPath))
        {
            await EnsureBenchmarkHtmlReportExistsAsync(fullProjectRoot, statsPath, status);
        }

        if (!File.Exists(statsPath))
        {
            await PublishLogAsync($"[STATS] benchmark.html not found for this run: {statsPath}", "gray");
            return;
        }

        await PublishAsync(CreateMessage(MessageTypes.LogLink, new
        {
            message = "[STATS] Open benchmark report",
            path = statsPath,
            color = "cyan",
            status
        }));
    }

    private async Task EnsureBenchmarkHtmlReportExistsAsync(string projectRoot, string statsPath, string status)
    {
        try
        {
            var hydration = await _workflowStateStore.GetHydrationAsync();
            var snapshot = hydration.Snapshot;
            var metrics = snapshot.Metrics ?? new WorkflowMetrics();
            var now = DateTimeOffset.Now;
            var statsDirectory = Path.GetDirectoryName(statsPath);
            if (string.IsNullOrWhiteSpace(statsDirectory))
            {
                return;
            }

            Directory.CreateDirectory(statsDirectory);

            var html = $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="UTF-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
              <title>Benchmark Report</title>
              <style>
                body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; margin: 24px; color: #0f172a; background: #f8fafc; }
                .card { background: white; border: 1px solid #e2e8f0; border-radius: 10px; padding: 16px; margin-bottom: 12px; }
                .title { font-size: 1.25rem; font-weight: 700; margin-bottom: 8px; }
                .grid { display: grid; grid-template-columns: repeat(2, minmax(0,1fr)); gap: 8px; }
                .label { color: #475569; font-size: 0.9rem; }
                .value { font-weight: 600; }
              </style>
            </head>
            <body>
              <div class="card">
                <div class="title">Benchmark Report</div>
                <div class="grid">
                  <div><span class="label">Generated:</span> <span class="value">{{WebUtility.HtmlEncode(now.ToString("yyyy-MM-dd HH:mm:ss zzz"))}}</span></div>
                  <div><span class="label">Status:</span> <span class="value">{{WebUtility.HtmlEncode(status)}}</span></div>
                  <div><span class="label">Project:</span> <span class="value">{{WebUtility.HtmlEncode(snapshot.SelectedProjectName ?? "")}}</span></div>
                  <div><span class="label">Workflow Type:</span> <span class="value">{{WebUtility.HtmlEncode(snapshot.SelectedWorkflowTypeName ?? snapshot.SelectedWorkflowTypeId ?? "")}}</span></div>
                  <div><span class="label">Server:</span> <span class="value">{{WebUtility.HtmlEncode(_settings.GetActiveLlmServer().Name)}}</span></div>
                  <div><span class="label">Model:</span> <span class="value">{{WebUtility.HtmlEncode(_settings.GetActiveLlmModelName())}}</span></div>
                  <div><span class="label">Total Steps:</span> <span class="value">{{metrics.TotalSteps}}</span></div>
                  <div><span class="label">Successful Steps:</span> <span class="value">{{metrics.SuccessfulSteps}}</span></div>
                  <div><span class="label">Failed Steps:</span> <span class="value">{{metrics.FailedSteps}}</span></div>
                  <div><span class="label">Total Duration (ms):</span> <span class="value">{{metrics.TotalDurationMs}}</span></div>
                  <div><span class="label">Average Step Duration (ms):</span> <span class="value">{{metrics.AverageStepDurationMs:F2}}</span></div>
                  <div><span class="label">Total Tokens Used:</span> <span class="value">{{metrics.TotalTokensUsed}}</span></div>
                  <div><span class="label">Tokens/Sec:</span> <span class="value">{{FormatTokensPerSecond(metrics.TotalTokensUsed, metrics.TotalDurationMs)}}</span></div>
                </div>
              </div>
            </body>
            </html>
            """;

            await File.WriteAllTextAsync(statsPath, html);
            await PublishLogAsync($"[STATS] Created benchmark report: {statsPath}", "cyan");
        }
        catch (Exception ex)
        {
            await PublishLogAsync($"[STATS] Failed to generate benchmark.html automatically: {ex.Message}", "red");
        }
    }

    private static bool IsSubPathOf(string candidatePath, string rootPath)
    {
        var root = EnsureTrailingSeparator(Path.GetFullPath(rootPath));
        var candidate = EnsureTrailingSeparator(Path.GetFullPath(candidatePath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return candidate.StartsWith(root, comparison);
    }

    private static string EnsureTrailingSeparator(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (value.EndsWith(Path.DirectorySeparatorChar) || value.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return value;
        }

        return value + Path.DirectorySeparatorChar;
    }

    private async Task WriteStepBenchmarkReportAsync(int stepNumber, StepExecutionResult result, string status)
    {
        if (!_settings.EnableBenchmarkHtmlOutput)
        {
            return;
        }

        var hydration = await _workflowStateStore.GetHydrationAsync();
        var projectRoot = hydration.Snapshot.SelectedProjectDirectory;
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return;
        }

        var fullProjectRoot = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(fullProjectRoot))
        {
            return;
        }

        var statsDirectory = Path.GetFullPath(Path.Combine(fullProjectRoot, "stats"));
        if (!IsSubPathOf(statsDirectory, fullProjectRoot))
        {
            return;
        }

        Directory.CreateDirectory(statsDirectory);
        var stepPath = Path.GetFullPath(Path.Combine(statsDirectory, $"step{stepNumber}-benchmark.html"));
        if (!IsSubPathOf(stepPath, fullProjectRoot))
        {
            return;
        }

        var usage = result.LLMResponse?.Usage ?? new TokenUsage();
        var promptTokens = usage.PromptTokens > 0 ? usage.PromptTokens : EstimateTokens(result.RequestMessagesContent);
        var completionTokens = usage.CompletionTokens > 0 ? usage.CompletionTokens : EstimateTokens(result.ResponseContent);
        var totalTokens = usage.TotalTokens > 0 ? usage.TotalTokens : promptTokens + completionTokens;
        var tps = FormatTokensPerSecond(completionTokens, result.DurationMs);
        var now = DateTimeOffset.Now;

        var html = $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="UTF-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1.0" />
          <title>Step {{stepNumber}} Benchmark</title>
          <style>
            body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; margin: 24px; color: #0f172a; background: #f8fafc; }
            .card { background: white; border: 1px solid #e2e8f0; border-radius: 10px; padding: 16px; margin-bottom: 12px; }
            .title { font-size: 1.25rem; font-weight: 700; margin-bottom: 8px; }
            .grid { display: grid; grid-template-columns: repeat(2, minmax(0,1fr)); gap: 8px; }
            .label { color: #475569; font-size: 0.9rem; }
            .value { font-weight: 600; }
          </style>
        </head>
        <body>
          <div class="card">
            <div class="title">Step {{stepNumber}} Benchmark</div>
            <div class="grid">
              <div><span class="label">Generated:</span> <span class="value">{{WebUtility.HtmlEncode(now.ToString("yyyy-MM-dd HH:mm:ss zzz"))}}</span></div>
              <div><span class="label">Status:</span> <span class="value">{{WebUtility.HtmlEncode(status)}}</span></div>
              <div><span class="label">Project:</span> <span class="value">{{WebUtility.HtmlEncode(hydration.Snapshot.SelectedProjectName ?? "")}}</span></div>
              <div><span class="label">Workflow Type:</span> <span class="value">{{WebUtility.HtmlEncode(hydration.Snapshot.SelectedWorkflowTypeName ?? hydration.Snapshot.SelectedWorkflowTypeId ?? "")}}</span></div>
              <div><span class="label">Server:</span> <span class="value">{{WebUtility.HtmlEncode(_settings.GetActiveLlmServer().Name)}}</span></div>
              <div><span class="label">Model:</span> <span class="value">{{WebUtility.HtmlEncode(_settings.GetActiveLlmModelName())}}</span></div>
              <div><span class="label">Duration (ms):</span> <span class="value">{{result.DurationMs}}</span></div>
              <div><span class="label">Prompt Tokens:</span> <span class="value">{{promptTokens}}</span></div>
              <div><span class="label">Completion Tokens:</span> <span class="value">{{completionTokens}}</span></div>
              <div><span class="label">Total Tokens:</span> <span class="value">{{totalTokens}}</span></div>
              <div><span class="label">Tokens/Sec:</span> <span class="value">{{tps}}</span></div>
            </div>
          </div>
        </body>
        </html>
        """;

        await File.WriteAllTextAsync(stepPath, html);
    }

    private static int EstimateTokens(IEnumerable<string>? texts)
    {
        if (texts == null)
        {
            return 0;
        }

        var totalChars = texts.Where(item => !string.IsNullOrWhiteSpace(item)).Sum(item => item.Length);
        return totalChars <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(totalChars / 4.0));
    }

    private static int EstimateTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));
    }

    private async Task PublishPerMessageTokenStatsAsync(int stepNumber, StepExecutionResult result, string status)
    {
        var usage = result.LLMResponse?.Usage ?? new TokenUsage();
        var promptTokens = usage.PromptTokens > 0 ? usage.PromptTokens : EstimateTokens(result.RequestMessagesContent);
        var completionTokens = usage.CompletionTokens > 0 ? usage.CompletionTokens : EstimateTokens(result.ResponseContent);
        var totalTokens = usage.TotalTokens > 0 ? usage.TotalTokens : promptTokens + completionTokens;
        var tps = FormatTokensPerSecond(completionTokens, result.DurationMs);
        var durationSeconds = result.DurationMs > 0 ? (result.DurationMs / 1000.0).ToString("F2") : "0.00";

        await PublishLogAsync(
            $"[STATS] Step {stepNumber} message ({status}): prompt={promptTokens}, completion={completionTokens}, total={totalTokens}, duration={durationSeconds}s, tok/s={tps}",
            "cyan");
    }

    public async Task PublishConversationTurnTokenStatsAsync(int stepNumber, StepConversationTurnResult result)
    {
        var promptTokens = result.PromptTokens > 0 ? result.PromptTokens : 0;
        var completionTokens = result.CompletionTokens > 0
            ? result.CompletionTokens
            : EstimateTokens(result.FinalResponse?.Content);
        var totalTokens = result.TotalTokens > 0 ? result.TotalTokens : promptTokens + completionTokens;
        var tps = FormatTokensPerSecond(completionTokens, result.DurationMs);
        var durationSeconds = result.DurationMs > 0 ? (result.DurationMs / 1000.0).ToString("F2") : "0.00";

        await PublishLogAsync(
            $"[STATS] Step {stepNumber} message (reply): prompt={promptTokens}, completion={completionTokens}, total={totalTokens}, duration={durationSeconds}s, tok/s={tps}",
            "cyan");
    }

    private static string FormatTokensPerSecond(int completionTokens, long durationMs)
    {
        if (completionTokens <= 0 || durationMs <= 0)
        {
            return "n/a";
        }

        var perSecond = completionTokens / Math.Max(durationMs / 1000.0, 0.001);
        return $"{perSecond:F2}";
    }
}
