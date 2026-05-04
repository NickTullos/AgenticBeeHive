using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;

namespace ABHive.Web;

public class ScheduleRunnerService : BackgroundService
{
    private readonly object _lock = new();
    private readonly ScheduleDefinitionService _scheduleDefinitionService;
    private readonly WorkflowStateStore _workflowStateStore;
    private readonly WorkflowTypeCatalog _workflowTypeCatalog;
    private readonly WebSocketHandler _webSocketHandler;
    private readonly AppSettings _settings;

    private bool _isActive;
    private bool _isRunInProgress;
    private bool _stopCurrentRunRequested;
    private ScheduleDefinition? _activeSchedule;
    private DateTime? _nextRunLocal;
    private DateTime? _lastFrequencyRunLocal;

    public ScheduleRunnerService(
        ScheduleDefinitionService scheduleDefinitionService,
        WorkflowStateStore workflowStateStore,
        WorkflowTypeCatalog workflowTypeCatalog,
        WebSocketHandler webSocketHandler,
        AppSettings settings)
    {
        _scheduleDefinitionService = scheduleDefinitionService;
        _workflowStateStore = workflowStateStore;
        _workflowTypeCatalog = workflowTypeCatalog;
        _webSocketHandler = webSocketHandler;
        _settings = settings;
    }

    public ScheduleRuntimeState GetRuntimeState()
    {
        lock (_lock)
        {
            return new ScheduleRuntimeState
            {
                IsActive = _isActive,
                ActiveScheduleName = _activeSchedule?.ScheduleName ?? string.Empty,
                NextRunLocal = _nextRunLocal?.ToString("yyyy-MM-dd HH:mm:ss"),
                TriggerType = _activeSchedule?.Trigger?.Type ?? string.Empty,
                ScheduleType = _activeSchedule?.ScheduleType ?? string.Empty
            };
        }
    }

    public async Task<(bool Success, int StatusCode, string Message, bool RequiresConfirmation, string? ConfirmationReason)> StartScheduleAsync(
        string scheduleName,
        bool confirmClearContext = false)
    {
        var lookup = _scheduleDefinitionService.GetScheduleLookup(scheduleName);
        if (lookup.Status == "invalid_name")
        {
            return (false, StatusCodes.Status400BadRequest, lookup.Error ?? "Invalid schedule name.", false, null);
        }

        if (lookup.Status == "corrupted")
        {
            return (false, StatusCodes.Status400BadRequest, lookup.Error ?? $"Schedule '{scheduleName}' is corrupted.", false, null);
        }

        if (lookup.Status != "ready" || lookup.Definition == null)
        {
            return (false, StatusCodes.Status404NotFound, $"Schedule '{scheduleName}' was not found.", false, null);
        }

        var definition = lookup.Definition;
        if (_workflowTypeCatalog.GetWorkflowType(definition.WorkflowTypeId) == null)
        {
            return (false, StatusCodes.Status400BadRequest, $"Workflow type '{definition.WorkflowTypeId}' was not found.", false, null);
        }

        var hydration = await _workflowStateStore.GetHydrationAsync();
        var snapshot = hydration.Snapshot;
        if (snapshot.Busy)
        {
            return (false, StatusCodes.Status409Conflict, "Cannot enter Schedule Mode while the LLM is busy. Wait for the current action to finish and try again.", false, null);
        }

        var requiresContextClear = snapshot.CanResume ||
            snapshot.AwaitingUserInput ||
            snapshot.WorkflowRunning ||
            _webSocketHandler.IsWorkflowRunning;
        if (requiresContextClear && !confirmClearContext)
        {
            const string reason = "Starting this schedule will clear the current paused/resumable workflow context.";
            return (false, StatusCodes.Status409Conflict, reason, true, reason);
        }

        if (requiresContextClear && confirmClearContext)
        {
            await _webSocketHandler.PublishLogAsync(
                "[SCHEDULE] Clearing paused/resumable workflow context before starting schedule.",
                "orange");
            await _webSocketHandler.ResetWorkflowAsync();
        }

        lock (_lock)
        {
            if (_isActive)
            {
                if (!string.Equals(_activeSchedule?.ScheduleName, definition.ScheduleName, StringComparison.Ordinal))
                {
                    return (false, StatusCodes.Status409Conflict, $"Schedule mode is already active for '{_activeSchedule?.ScheduleName}'. Stop it first.", false, null);
                }

                return (true, StatusCodes.Status200OK, $"Schedule '{definition.ScheduleName}' is already active.", false, null);
            }

            _isActive = true;
            _activeSchedule = definition;
            _lastFrequencyRunLocal = null;
            _nextRunLocal = ComputeInitialNextRunLocal(definition, DateTime.Now);
        }

        await _webSocketHandler.PublishLogAsync(
            $"[SCHEDULE] Started '{definition.ScheduleName}' in Schedule Mode ({definition.ScheduleType}, trigger={definition.Trigger.Type}).",
            "cyan");

        return (true, StatusCodes.Status200OK, $"Schedule '{definition.ScheduleName}' started.", false, null);
    }

    public async Task<(bool Success, string Message)> StopScheduleAsync(string reason = "Stopped by user")
    {
        bool changed;
        string scheduleName;
        lock (_lock)
        {
            changed = _isActive;
            scheduleName = _activeSchedule?.ScheduleName ?? string.Empty;
            _isActive = false;
            _activeSchedule = null;
            _nextRunLocal = null;
            _lastFrequencyRunLocal = null;
        }

        if (changed)
        {
            await _webSocketHandler.PublishLogAsync($"[SCHEDULE] {reason}: {scheduleName}", "orange");
            return (true, $"Schedule '{scheduleName}' stopped.");
        }

        return (true, "No active schedule.");
    }

    public async Task<(bool Success, string Message)> StopCurrentScheduledTaskAsync()
    {
        bool isActive;
        bool isRunInProgress;
        string scheduleName;
        lock (_lock)
        {
            isActive = _isActive;
            isRunInProgress = _isRunInProgress;
            scheduleName = _activeSchedule?.ScheduleName ?? string.Empty;
            if (isActive && isRunInProgress)
            {
                _stopCurrentRunRequested = true;
            }
        }

        if (!isActive)
        {
            return (false, "Schedule Mode is not active.");
        }

        if (!isRunInProgress)
        {
            return (false, "No scheduled task is currently running.");
        }

        await _webSocketHandler.PublishLogAsync(
            $"[SCHEDULE] Stop Schedule Task requested for '{scheduleName}'. Stopping current run.",
            "orange");
        await _webSocketHandler.StopWorkflowAsync();
        return (true, "Stop Schedule Task requested.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            ScheduleDefinition? activeSchedule;
            bool shouldRun;
            DateTime nowLocal;

            lock (_lock)
            {
                activeSchedule = _activeSchedule;
                nowLocal = DateTime.Now;
                shouldRun = _isActive && activeSchedule != null && _nextRunLocal.HasValue && nowLocal >= _nextRunLocal.Value;
            }

            if (shouldRun && activeSchedule != null)
            {
                await TriggerScheduleRunAsync(activeSchedule, stoppingToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private async Task TriggerScheduleRunAsync(ScheduleDefinition definition, CancellationToken ct)
    {
        bool runAllowed;
        lock (_lock)
        {
            runAllowed = !_isRunInProgress;
            if (runAllowed)
            {
                _isRunInProgress = true;
                _stopCurrentRunRequested = false;
            }
        }

        if (!runAllowed)
        {
            await _webSocketHandler.PublishLogAsync(
                $"[SCHEDULE] Trigger skipped for '{definition.ScheduleName}' because another scheduled run is still active.",
                "orange");
            UpdateNextRunAfterTrigger(definition, DateTime.Now);
            return;
        }

        try
        {
            var priorHydration = await _workflowStateStore.GetHydrationAsync();
            var priorProjectName = priorHydration.Snapshot.SelectedProjectName;
            var priorProjectDirectory = priorHydration.Snapshot.SelectedProjectDirectory;

            await _webSocketHandler.PublishLogAsync(
                $"[SCHEDULE] Trigger fired for '{definition.ScheduleName}'.",
                "cyan");

            if (string.Equals(definition.ScheduleType, "benchmark", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var selection in definition.BenchmarkSelections)
                {
                    ct.ThrowIfCancellationRequested();
                    var shouldContinue = await RunSingleSelectionAsync(definition, selection, isBenchmark: true, ct);
                    if (!shouldContinue)
                    {
                        break;
                    }
                }
            }
            else if (definition.RegularSelection != null)
            {
                await RunSingleSelectionAsync(definition, definition.RegularSelection, isBenchmark: false, ct);
            }

            UpdateNextRunAfterTrigger(definition, DateTime.Now);

            if (string.Equals(definition.Trigger.Type, "immediate", StringComparison.OrdinalIgnoreCase))
            {
                await StopScheduleAsync("Immediate schedule completed");
            }

            await RestoreProjectContextAsync(priorProjectName, priorProjectDirectory);
        }
        finally
        {
            lock (_lock)
            {
                _isRunInProgress = false;
                _stopCurrentRunRequested = false;
            }
        }
    }

    private async Task<bool> RunSingleSelectionAsync(
        ScheduleDefinition definition,
        ScheduleSelection selection,
        bool isBenchmark,
        CancellationToken ct)
    {
        var server = _settings.LlmServers.FirstOrDefault(item => string.Equals(item.Id, selection.ServerId, StringComparison.Ordinal));
        if (server == null)
        {
            await _webSocketHandler.PublishLogAsync(
                $"[SCHEDULE] Skipping selection '{selection.ServerId}/{selection.ModelId}': server not found.",
                "red");
            return true;
        }

        var model = server.Models.FirstOrDefault(item => string.Equals(item.Id, selection.ModelId, StringComparison.Ordinal));
        if (model == null)
        {
            await _webSocketHandler.PublishLogAsync(
                $"[SCHEDULE] Skipping selection '{selection.ServerId}/{selection.ModelId}': model not found.",
                "red");
            return true;
        }

        if (!_settings.TrySetActiveLlmSelection(selection.ServerId, selection.ModelId, out var selectionError))
        {
            await _webSocketHandler.PublishLogAsync($"[SCHEDULE] Failed to set active LLM selection: {selectionError}", "red");
            return true;
        }

        var scheduleRoot = _scheduleDefinitionService.GetScheduleDirectory(definition.ScheduleName);
        var comboFolder = ScheduleDefinitionService.BuildBenchmarkFolderName(server.Name, model.Name);
        var executionRoot = Path.Combine(scheduleRoot, comboFolder);
        var projectName = $"{definition.ScheduleName}_{comboFolder}";
        var workflowType = _workflowTypeCatalog.GetWorkflowType(definition.WorkflowTypeId);
        if (workflowType == null)
        {
            await _webSocketHandler.PublishLogAsync(
                $"[SCHEDULE] Skipping selection '{server.Name}/{model.Name}': workflow type '{definition.WorkflowTypeId}' not found.",
                "red");
            return true;
        }

        EnsureScheduleWorkspace(executionRoot);
        _settings.SelectedProjectName = projectName;
        _settings.SelectedProjectDirectory = executionRoot;
        await _workflowStateStore.SwitchProjectAsync(projectName, executionRoot);

        var executionWorkflowType = BuildExecutionWorkflowType(definition, workflowType, executionRoot);
        if (executionWorkflowType == null)
        {
            await _webSocketHandler.PublishLogAsync(
                $"[SCHEDULE] Skipping selection '{server.Name}/{model.Name}': none of the selected steps were found in workflow '{definition.WorkflowTypeId}'.",
                "red");
            return true;
        }

        await _webSocketHandler.PublishLogAsync(
            $"[SCHEDULE] Running workflow '{executionWorkflowType.Name}' for {server.Name} / {model.Name} in {executionRoot}",
            "cyan");

        var started = await _webSocketHandler.StartWorkflowAsync(executionWorkflowType);
        if (!started)
        {
            await _webSocketHandler.PublishLogAsync(
                $"[SCHEDULE] Workflow start request was rejected for selection {server.Name}/{model.Name}.",
                "orange");
            return true;
        }

        var autoContinueLogged = false;
        while (!ct.IsCancellationRequested)
        {
            if (IsCurrentRunStopRequested())
            {
                await _webSocketHandler.PublishLogAsync(
                    $"[SCHEDULE] Stop Schedule Task confirmed for {server.Name}/{model.Name}. Ending current scheduled run.",
                    "orange");
                return false;
            }

            var hydration = await _workflowStateStore.GetHydrationAsync();
            var snapshot = hydration.Snapshot;
            var manuallyStopped = IsStoppedStatus(snapshot.Status) &&
                                  !_webSocketHandler.IsWorkflowRunning &&
                                  !_webSocketHandler.IsBusy &&
                                  !snapshot.WorkflowRunning &&
                                  !snapshot.Busy;

            if (manuallyStopped)
            {
                var nextStepNumber = snapshot.CurrentStep + 1;
                if (snapshot.TotalSteps > 0 && nextStepNumber <= snapshot.TotalSteps)
                {
                    await _webSocketHandler.PublishLogAsync(
                        $"[SCHEDULE] Stop detected for {server.Name}/{model.Name}. Skipping step {snapshot.CurrentStep} and continuing at step {nextStepNumber}.",
                        "orange");

                    var skipResult = await _webSocketHandler.SkipToStepAsync(nextStepNumber);
                    if (!skipResult.Success)
                    {
                        await _webSocketHandler.PublishLogAsync(
                            $"[SCHEDULE] Failed to continue after stop for {server.Name}/{model.Name}: {skipResult.Message}",
                            "red");
                        break;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
                    continue;
                }

                await _webSocketHandler.PublishLogAsync(
                    $"[SCHEDULE] Stop detected for {server.Name}/{model.Name} at final step; treating run as finished.",
                    "orange");
                break;
            }

            var waitingForInput = snapshot.AwaitingUserInput || snapshot.CanResume;
            if (waitingForInput)
            {
                var queued = await _webSocketHandler.QueueContinueAsync();
                if (queued)
                {
                    if (!autoContinueLogged)
                    {
                        await _webSocketHandler.PublishLogAsync(
                            $"[SCHEDULE] Auto-continuing paused workflow for {server.Name}/{model.Name}.",
                            "cyan");
                        autoContinueLogged = true;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
                    continue;
                }

                if (!_webSocketHandler.IsWorkflowRunning &&
                    !_webSocketHandler.IsBusy &&
                    !snapshot.WorkflowRunning &&
                    !snapshot.Busy)
                {
                    await _webSocketHandler.PublishLogAsync(
                        $"[SCHEDULE] Unable to auto-continue for {server.Name}/{model.Name}; treating run as finished.",
                        "orange");
                    break;
                }
            }

            var stillRunning = _webSocketHandler.IsWorkflowRunning ||
                               _webSocketHandler.IsBusy ||
                               snapshot.WorkflowRunning ||
                               snapshot.Busy;
            if (!stillRunning && !waitingForInput)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        return !IsCurrentRunStopRequested();
    }

    private bool IsCurrentRunStopRequested()
    {
        lock (_lock)
        {
            return _stopCurrentRunRequested;
        }
    }

    private static bool IsStoppedStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        return status.StartsWith("Stopped", StringComparison.OrdinalIgnoreCase);
    }

    private WorkflowTypeDefinition? BuildExecutionWorkflowType(
        ScheduleDefinition schedule,
        WorkflowTypeDefinition sourceWorkflowType,
        string executionRoot)
    {
        var selectedSteps = (schedule.SelectedStepFileNames ?? new List<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedSteps.Count == 0)
        {
            return sourceWorkflowType;
        }

        var orderedSteps = Directory.GetFiles(sourceWorkflowType.StepsDirectory, "*.md", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Where(path => selectedSteps.Contains(Path.GetFileName(path)))
            .ToList();
        if (orderedSteps.Count == 0)
        {
            return null;
        }

        var filteredRoot = Path.Combine(executionRoot, ".workflow-steps");
        Directory.CreateDirectory(filteredRoot);
        var filteredStepsDirectory = Path.Combine(filteredRoot, sourceWorkflowType.Id);
        if (Directory.Exists(filteredStepsDirectory))
        {
            Directory.Delete(filteredStepsDirectory, recursive: true);
        }
        Directory.CreateDirectory(filteredStepsDirectory);

        foreach (var markdownPath in orderedSteps)
        {
            var fileName = Path.GetFileName(markdownPath);
            File.Copy(markdownPath, Path.Combine(filteredStepsDirectory, fileName), overwrite: true);

            var metadataPath = Path.ChangeExtension(markdownPath, ".json");
            if (!string.IsNullOrWhiteSpace(metadataPath) && File.Exists(metadataPath))
            {
                File.Copy(metadataPath, Path.Combine(filteredStepsDirectory, Path.GetFileName(metadataPath)), overwrite: true);
            }
        }

        return new WorkflowTypeDefinition
        {
            Id = $"{sourceWorkflowType.Id}__schedule",
            Name = sourceWorkflowType.Name,
            StepsDirectory = filteredStepsDirectory,
            StepCount = orderedSteps.Count
        };
    }

    private void UpdateNextRunAfterTrigger(ScheduleDefinition definition, DateTime nowLocal)
    {
        lock (_lock)
        {
            if (string.Equals(definition.Trigger.Type, "frequency", StringComparison.OrdinalIgnoreCase))
            {
                _lastFrequencyRunLocal = nowLocal;
            }

            _nextRunLocal = ComputeNextRunLocal(definition, nowLocal);
        }
    }

    private DateTime? ComputeInitialNextRunLocal(ScheduleDefinition definition, DateTime nowLocal)
    {
        if (string.Equals(definition.Trigger.Type, "immediate", StringComparison.OrdinalIgnoreCase))
        {
            return nowLocal;
        }

        return ComputeNextRunLocal(definition, nowLocal);
    }

    private DateTime? ComputeNextRunLocal(ScheduleDefinition definition, DateTime nowLocal)
    {
        if (string.Equals(definition.Trigger.Type, "specificTime", StringComparison.OrdinalIgnoreCase))
        {
            if (!TimeSpan.TryParse(definition.Trigger.SpecificTimeLocal, out var timeOfDay))
            {
                return nowLocal.AddDays(1);
            }

            var next = nowLocal.Date.Add(timeOfDay);
            if (next <= nowLocal)
            {
                next = next.AddDays(1);
            }

            return next;
        }

        if (string.Equals(definition.Trigger.Type, "frequency", StringComparison.OrdinalIgnoreCase))
        {
            var interval = new TimeSpan(
                definition.Trigger.IntervalHours,
                definition.Trigger.IntervalMinutes,
                definition.Trigger.IntervalSeconds);

            if (interval <= TimeSpan.Zero)
            {
                interval = TimeSpan.FromMinutes(1);
            }

            var baseTime = _lastFrequencyRunLocal ?? nowLocal;
            return baseTime.Add(interval);
        }

        return null;
    }

    private static void EnsureScheduleWorkspace(string rootPath)
    {
        Directory.CreateDirectory(rootPath);
        foreach (var subdirectory in ScheduleConstants.WorkspaceSubdirectories)
        {
            Directory.CreateDirectory(Path.Combine(rootPath, subdirectory));
        }
    }

    private async Task RestoreProjectContextAsync(string priorProjectName, string priorProjectDirectory)
    {
        var normalizedName = (priorProjectName ?? string.Empty).Trim();
        var normalizedDirectory = (priorProjectDirectory ?? string.Empty).Trim();

        await _workflowStateStore.SwitchProjectAsync(normalizedName, normalizedDirectory);
        _settings.SelectedProjectName = normalizedName;
        _settings.SelectedProjectDirectory = normalizedDirectory;
    }
}
