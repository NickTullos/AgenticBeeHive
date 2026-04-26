using System.Text.RegularExpressions;
using System.Text.Json;

using ABHive.Application;

namespace ABHive.Web;

public class WorkflowStateStore
{
    private static readonly Regex ProjectNamePattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private string _projectsRoot;
    private PersistedWorkflowState _state;
    private readonly TicketIterationStatusResolver _ticketIterationStatusResolver;

    public WorkflowStateStore(AppSettings settings, TicketIterationStatusResolver ticketIterationStatusResolver)
    {
        _ticketIterationStatusResolver = ticketIterationStatusResolver;
        _projectsRoot = ResolveProjectsRoot(settings.ProjectRootDirectory);
        Directory.CreateDirectory(_projectsRoot);

        _state = LoadStateForStartup(settings.SelectedProjectName, settings.SelectedProjectDirectory);
        RecoverInterruptedRunIfNeeded();
        SaveSynchronously();
    }

    public async Task<WorkflowHydrationState> GetHydrationAsync()
    {
        await _gate.WaitAsync();
        try
        {
            RefreshTicketIterationHeaderStatusLocked();
            return Clone(new WorkflowHydrationState
            {
                Snapshot = _state.Snapshot,
                History = _state.History
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<List<ProjectWorkflowStateSummary>> GetProjectStateSummariesAsync(IReadOnlyList<ProjectWorkspace> projects)
    {
        await _gate.WaitAsync();
        try
        {
            var summaries = new List<ProjectWorkflowStateSummary>(projects.Count);
            var activeProjectName = _state.Snapshot.SelectedProjectName;

            foreach (var project in projects)
            {
                var projectName = project.ProjectName;
                var stateFilePath = project.ProjectStateFilePath;

                WorkflowRuntimeSnapshot? snapshot = null;
                if (string.Equals(projectName, activeProjectName, StringComparison.Ordinal))
                {
                    snapshot = _state.Snapshot;
                }
                else
                {
                    try
                    {
                        if (File.Exists(stateFilePath))
                        {
                            var json = File.ReadAllText(stateFilePath);
                            var persisted = JsonSerializer.Deserialize<PersistedWorkflowState>(json, _jsonOptions);
                            snapshot = persisted?.Snapshot;
                        }
                    }
                    catch
                    {
                        // Ignore malformed project state files in summary view.
                    }
                }

                summaries.Add(new ProjectWorkflowStateSummary
                {
                    ProjectName = projectName,
                    IsActive = string.Equals(projectName, activeProjectName, StringComparison.Ordinal),
                    HasStateFile = File.Exists(stateFilePath),
                    CurrentStep = snapshot?.CurrentStep ?? 0,
                    TotalSteps = snapshot?.TotalSteps ?? 0,
                    CurrentStepName = snapshot?.CurrentStepName ?? "Not started",
                    Status = snapshot?.Status ?? "Ready",
                    CanResume = snapshot?.CanResume ?? false,
                    WorkflowRunning = snapshot?.WorkflowRunning ?? false,
                    AwaitingUserInput = snapshot?.AwaitingUserInput ?? false,
                    LastUpdatedUtc = snapshot?.LastUpdatedUtc
                });
            }

            return summaries
                .OrderByDescending(item => item.IsActive)
                .ThenBy(item => item.ProjectName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PrepareForNewRunAsync(int totalSteps, WorkflowTypeDefinition workflowType)
    {
        await UpdateAsync(snapshot =>
        {
            snapshot.RunId = Guid.NewGuid().ToString("N");
            snapshot.Status = "Starting workflow...";
            snapshot.Color = "yellow";
            snapshot.WorkflowRunning = true;
            snapshot.Busy = false;
            snapshot.AwaitingUserInput = false;
            snapshot.CurrentStep = 0;
            snapshot.TotalSteps = totalSteps;
            snapshot.CurrentStepName = "Starting...";
            snapshot.Metrics = new WorkflowMetrics();
            snapshot.CanResume = false;
            snapshot.NextStepToRun = 1;
            snapshot.SelectedWorkflowTypeId = workflowType.Id;
            snapshot.SelectedWorkflowTypeName = workflowType.Name;
            snapshot.SelectedWorkflowStepsDirectory = workflowType.StepsDirectory;
            snapshot.TicketProgress = null;
        }, clearHistory: true, clearActiveStep: true);
    }

    public Task SetSelectedProjectAsync(string projectName, string projectDirectory)
    {
        return SwitchProjectAsync(projectName, projectDirectory);
    }

    public async Task SwitchProjectAsync(string projectName, string projectDirectory)
    {
        await _gate.WaitAsync();
        try
        {
            var normalizedName = (projectName ?? string.Empty).Trim();
            var normalizedDirectory = (projectDirectory ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                SaveCurrentProjectStateLocked();
                _state = CreateDefaultState();
                await SaveLockedAsync();
                return;
            }

            if (!ProjectNamePattern.IsMatch(normalizedName))
            {
                throw new InvalidOperationException("Project name must contain only letters, numbers, dashes (-), or underscores (_).");
            }

            if (string.IsNullOrWhiteSpace(normalizedDirectory))
            {
                normalizedDirectory = Path.Combine(_projectsRoot, normalizedName);
            }

            SaveCurrentProjectStateLocked();

            var loadedState = LoadProjectStateLocked(normalizedName, normalizedDirectory);
            loadedState.Snapshot.SelectedProjectName = normalizedName;
            loadedState.Snapshot.SelectedProjectDirectory = normalizedDirectory;
            loadedState.Snapshot.LastUpdatedUtc = DateTime.UtcNow;

            if (loadedState.ActiveStep != null)
            {
                loadedState.ActiveStep.ProjectName = normalizedName;
                loadedState.ActiveStep.ProjectDirectory = normalizedDirectory;
            }

            _state = loadedState;
            await SaveLockedAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateProjectsRootAsync(string projectsRoot)
    {
        await _gate.WaitAsync();
        try
        {
            SaveCurrentProjectStateLocked();

            _projectsRoot = ResolveProjectsRoot(projectsRoot);
            Directory.CreateDirectory(_projectsRoot);

            _state = LoadStateForStartup(string.Empty, string.Empty);
            RecoverInterruptedRunIfNeeded();
            await SaveLockedAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResetAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var selectedProjectName = _state.Snapshot.SelectedProjectName;
            var selectedProjectDirectory = _state.Snapshot.SelectedProjectDirectory;

            _state = CreateDefaultState();
            _state.Snapshot.SelectedProjectName = selectedProjectName;
            _state.Snapshot.SelectedProjectDirectory = selectedProjectDirectory;

            await SaveLockedAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ActiveStepContextState?> GetActiveStepAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return _state.ActiveStep == null ? null : Clone(_state.ActiveStep);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveActiveStepAsync(ActiveStepContextState activeStep)
    {
        await _gate.WaitAsync();
        try
        {
            _state.ActiveStep = Clone(activeStep);
            _state.Snapshot.CurrentStep = activeStep.StepNumber;
            _state.Snapshot.TotalSteps = activeStep.TotalSteps;
            _state.Snapshot.CurrentStepName = activeStep.StepName;
            _state.Snapshot.AwaitingUserInput = true;
            _state.Snapshot.Busy = false;
            _state.Snapshot.CanResume = true;
            _state.Snapshot.NextStepToRun = activeStep.StepNumber;
            _state.Snapshot.SelectedWorkflowTypeId = activeStep.WorkflowTypeId;
            _state.Snapshot.SelectedWorkflowTypeName = activeStep.WorkflowTypeName;
            _state.Snapshot.SelectedWorkflowStepsDirectory = activeStep.WorkflowStepsDirectory;
            _state.Snapshot.SelectedProjectName = activeStep.ProjectName;
            _state.Snapshot.SelectedProjectDirectory = activeStep.ProjectDirectory;
            _state.Snapshot.LastUpdatedUtc = DateTime.UtcNow;
            await SaveLockedAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearActiveStepAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _state.ActiveStep = null;
            _state.Snapshot.LastUpdatedUtc = DateTime.UtcNow;
            await SaveLockedAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync(
        Action<WorkflowRuntimeSnapshot>? updateSnapshot = null,
        AgentMessage? message = null,
        bool clearHistory = false,
        bool appendToHistory = true,
        bool clearActiveStep = false)
    {
        await _gate.WaitAsync();
        try
        {
            if (clearHistory)
            {
                _state.History.Clear();
                _state.NextSequence = 1;
            }

            if (clearActiveStep)
            {
                _state.ActiveStep = null;
            }

            updateSnapshot?.Invoke(_state.Snapshot);
            RefreshTicketIterationHeaderStatusLocked();

            if (message != null && appendToHistory)
            {
                if (string.IsNullOrWhiteSpace(message.timestamp))
                {
                    message.timestamp = DateTime.UtcNow.ToString("O");
                }

                _state.History.Add(new WorkflowHistoryEvent
                {
                    Sequence = _state.NextSequence++,
                    Message = message
                });
            }

            _state.Snapshot.HasHistory = _state.History.Count > 0;
            _state.Snapshot.LastUpdatedUtc = DateTime.UtcNow;

            await SaveLockedAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private PersistedWorkflowState LoadStateForStartup(string fallbackProjectName, string fallbackProjectDirectory)
    {
        var pointer = ReadCurrentProjectPointer();
        var projectName = (pointer?.ProjectName ?? string.Empty).Trim();
        var projectDirectory = (pointer?.ProjectDirectory ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(projectName))
        {
            projectName = (fallbackProjectName ?? string.Empty).Trim();
            projectDirectory = (fallbackProjectDirectory ?? string.Empty).Trim();
        }

        if (string.IsNullOrWhiteSpace(projectName) || !ProjectNamePattern.IsMatch(projectName))
        {
            return CreateDefaultState();
        }

        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            projectDirectory = Path.Combine(_projectsRoot, projectName);
        }

        var state = LoadProjectStateLocked(projectName, projectDirectory);
        state.Snapshot.SelectedProjectName = projectName;
        state.Snapshot.SelectedProjectDirectory = projectDirectory;

        if (state.ActiveStep != null)
        {
            state.ActiveStep.ProjectName = projectName;
            state.ActiveStep.ProjectDirectory = projectDirectory;
        }

        return state;
    }

    private PersistedWorkflowState LoadProjectStateLocked(string projectName, string projectDirectory)
    {
        var path = GetProjectStateFilePath(projectDirectory);

        try
        {
            if (!File.Exists(path))
            {
                return CreateDefaultState();
            }

            var json = File.ReadAllText(path);
            var persisted = JsonSerializer.Deserialize<PersistedWorkflowState>(json, _jsonOptions);
            if (persisted == null)
            {
                return CreateDefaultState();
            }

            persisted.Snapshot ??= new WorkflowRuntimeSnapshot();
            persisted.History ??= new List<WorkflowHistoryEvent>();
            if (persisted.NextSequence <= 0)
            {
                persisted.NextSequence = persisted.History.Count + 1;
            }

            return persisted;
        }
        catch
        {
            return CreateDefaultState();
        }
    }

    private void RecoverInterruptedRunIfNeeded()
    {
        if (_state.ActiveStep != null)
        {
            _state.Snapshot.WorkflowRunning = false;
            _state.Snapshot.Busy = false;
            _state.Snapshot.AwaitingUserInput = true;
            _state.Snapshot.Status = "Recovered step context";
            _state.Snapshot.Color = "orange";
            _state.Snapshot.CanResume = true;
            _state.Snapshot.NextStepToRun = _state.ActiveStep.StepNumber;
            _state.Snapshot.CurrentStep = _state.ActiveStep.StepNumber;
            _state.Snapshot.TotalSteps = _state.ActiveStep.TotalSteps;
            _state.Snapshot.CurrentStepName = _state.ActiveStep.StepName;
            _state.Snapshot.LastUpdatedUtc = DateTime.UtcNow;

            _state.History.Add(new WorkflowHistoryEvent
            {
                Sequence = _state.NextSequence++,
                Message = new AgentMessage
                {
                    type = MessageTypes.Log,
                    timestamp = DateTime.UtcNow.ToString("O"),
                    payload = new
                    {
                        message = $"Recovered step {_state.ActiveStep.StepNumber} context after server restart. Send a message or click Continue to keep working on this step.",
                        color = "orange"
                    }
                }
            });

            _state.Snapshot.HasHistory = true;
            SaveSynchronously();
            return;
        }

        if (!_state.Snapshot.WorkflowRunning && !_state.Snapshot.AwaitingUserInput && !_state.Snapshot.Busy)
        {
            return;
        }

        var nextStepToRun = DetermineNextStepToRun(_state.Snapshot);
        var canResume = nextStepToRun > 0 && _state.Snapshot.TotalSteps > 0;

        _state.Snapshot.WorkflowRunning = false;
        _state.Snapshot.Busy = false;
        _state.Snapshot.AwaitingUserInput = false;
        _state.Snapshot.Status = canResume ? "Ready to resume" : "Stopped";
        _state.Snapshot.Color = "orange";
        _state.Snapshot.CanResume = canResume;
        _state.Snapshot.NextStepToRun = canResume ? nextStepToRun : 0;
        _state.Snapshot.LastUpdatedUtc = DateTime.UtcNow;

        var recoveryMessage = canResume
            ? $"Recovered workflow state after server restart. Continue to resume at step {nextStepToRun} of {_state.Snapshot.TotalSteps}."
            : "Recovered workflow state after server restart. Start a new run or reset the workflow.";

        _state.History.Add(new WorkflowHistoryEvent
        {
            Sequence = _state.NextSequence++,
            Message = new AgentMessage
            {
                type = MessageTypes.Log,
                timestamp = DateTime.UtcNow.ToString("O"),
                payload = new
                {
                    message = recoveryMessage,
                    color = "orange"
                }
            }
        });

        _state.Snapshot.HasHistory = true;
        SaveSynchronously();
    }

    private static int DetermineNextStepToRun(WorkflowRuntimeSnapshot snapshot)
    {
        if (snapshot.TotalSteps <= 0)
        {
            return 0;
        }

        if (snapshot.AwaitingUserInput && snapshot.CurrentStep > 0 && snapshot.CurrentStep < snapshot.TotalSteps)
        {
            return snapshot.CurrentStep + 1;
        }

        if (snapshot.CurrentStep > 0 && snapshot.CurrentStep <= snapshot.TotalSteps)
        {
            return snapshot.CurrentStep;
        }

        return 1;
    }

    private async Task SaveLockedAsync()
    {
        Directory.CreateDirectory(_projectsRoot);

        if (!string.IsNullOrWhiteSpace(_state.Snapshot.SelectedProjectName))
        {
            var statePath = Path.Combine(_state.Snapshot.SelectedProjectDirectory, $"{_state.Snapshot.SelectedProjectName}.json");
            var stateJson = JsonSerializer.Serialize(_state, _jsonOptions);
            await WriteAllTextAtomicAsync(statePath, stateJson);
        }

        await SaveCurrentProjectPointerLockedAsync();
    }

    private void SaveSynchronously()
    {
        Directory.CreateDirectory(_projectsRoot);

        if (!string.IsNullOrWhiteSpace(_state.Snapshot.SelectedProjectName))
        {
            var statePath = Path.Combine(_state.Snapshot.SelectedProjectDirectory, $"{_state.Snapshot.SelectedProjectName}.json");
            var stateJson = JsonSerializer.Serialize(_state, _jsonOptions);
            WriteAllTextAtomic(statePath, stateJson);
        }

        SaveCurrentProjectPointerLocked();
    }

    private void SaveCurrentProjectStateLocked()
    {
        if (string.IsNullOrWhiteSpace(_state.Snapshot.SelectedProjectName))
        {
            return;
        }

        Directory.CreateDirectory(_projectsRoot);
        var path = Path.Combine(_state.Snapshot.SelectedProjectDirectory, $"{_state.Snapshot.SelectedProjectName}.json");
        var json = JsonSerializer.Serialize(_state, _jsonOptions);
        WriteAllTextAtomic(path, json);
        SaveCurrentProjectPointerLocked();
    }

    private async Task SaveCurrentProjectPointerLockedAsync()
    {
        var pointerPath = GetCurrentProjectPointerFilePath();

        if (string.IsNullOrWhiteSpace(_state.Snapshot.SelectedProjectName))
        {
            if (File.Exists(pointerPath))
            {
                File.Delete(pointerPath);
            }

            return;
        }

        var pointer = new CurrentProjectPointer
        {
            ProjectName = _state.Snapshot.SelectedProjectName,
            ProjectDirectory = _state.Snapshot.SelectedProjectDirectory,
            LastUpdatedUtc = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(pointer, _jsonOptions);
        await WriteAllTextAtomicAsync(pointerPath, json);
    }

    private void SaveCurrentProjectPointerLocked()
    {
        var pointerPath = GetCurrentProjectPointerFilePath();

        if (string.IsNullOrWhiteSpace(_state.Snapshot.SelectedProjectName))
        {
            if (File.Exists(pointerPath))
            {
                File.Delete(pointerPath);
            }

            return;
        }

        var pointer = new CurrentProjectPointer
        {
            ProjectName = _state.Snapshot.SelectedProjectName,
            ProjectDirectory = _state.Snapshot.SelectedProjectDirectory,
            LastUpdatedUtc = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(pointer, _jsonOptions);
        WriteAllTextAtomic(pointerPath, json);
    }

    private CurrentProjectPointer? ReadCurrentProjectPointer()
    {
        try
        {
            var path = GetCurrentProjectPointerFilePath();
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            var pointer = JsonSerializer.Deserialize<CurrentProjectPointer>(json, _jsonOptions);
            if (pointer == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(pointer.ProjectName) || !ProjectNamePattern.IsMatch(pointer.ProjectName))
            {
                return null;
            }

            return pointer;
        }
        catch
        {
            return null;
        }
    }

    private string GetProjectStateFilePath(string projectDirectory)
    {
        var projectName = Path.GetFileName(projectDirectory);
        return Path.Combine(projectDirectory, $"{projectName}.json");
    }

    private string GetCurrentProjectPointerFilePath()
    {
        return Path.Combine(_projectsRoot, ".current-project.json");
    }

    private static string ResolveProjectsRoot(string projectRootDirectory)
    {
        var candidate = string.IsNullOrWhiteSpace(projectRootDirectory)
            ? "./projects"
            : projectRootDirectory;

        return Path.GetFullPath(candidate);
    }

    private PersistedWorkflowState CreateDefaultState()
    {
        return new PersistedWorkflowState
        {
            Snapshot = new WorkflowRuntimeSnapshot(),
            History = new List<WorkflowHistoryEvent>(),
            ActiveStep = null,
            NextSequence = 1
        };
    }

    private T Clone<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, _jsonOptions);
        return JsonSerializer.Deserialize<T>(json, _jsonOptions)!;
    }

    private static async Task WriteAllTextAtomicAsync(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.tmp.{Guid.NewGuid():N}";
        await File.WriteAllTextAsync(tempPath, content);
        File.Move(tempPath, path, true);
    }

    private static void WriteAllTextAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.tmp.{Guid.NewGuid():N}";
        File.WriteAllText(tempPath, content);
        File.Move(tempPath, path, true);
    }

    private void RefreshTicketIterationHeaderStatusLocked()
    {
        var resumePoint = _ticketIterationStatusResolver.FindResumePoint(_state.Snapshot);
        if (resumePoint.Found)
        {
            _state.Snapshot.SelectedWorkflowStepsDirectory = resumePoint.StepsDirectory;
            _state.Snapshot.CurrentStep = resumePoint.StepNumber;
            _state.Snapshot.TotalSteps = resumePoint.TotalSteps > 0
                ? resumePoint.TotalSteps
                : _state.Snapshot.TotalSteps;
            _state.Snapshot.CurrentStepName = string.IsNullOrWhiteSpace(resumePoint.StepName)
                ? _state.Snapshot.CurrentStepName
                : resumePoint.StepName;
        }

        var headerStatus = resumePoint.Found && resumePoint.HeaderStatus != null
            ? resumePoint.HeaderStatus
            : _ticketIterationStatusResolver.Resolve(_state.Snapshot);
        _state.Snapshot.IsCurrentStepTicketIteration = headerStatus.IsTicketIterationStep;
        _state.Snapshot.TicketHeaderStatus = headerStatus.IsTicketIterationStep
            ? headerStatus
            : null;

        if (!headerStatus.IsTicketIterationStep)
        {
            return;
        }

        if (_state.Snapshot.CurrentStep > 0 &&
            !_state.Snapshot.WorkflowRunning &&
            !_state.Snapshot.Busy &&
            headerStatus.IsAvailable &&
            headerStatus.RemainingTickets > 0)
        {
            _state.Snapshot.CanResume = true;
            _state.Snapshot.NextStepToRun = _state.Snapshot.CurrentStep;
            if (string.Equals(_state.Snapshot.Status, "Ready", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(_state.Snapshot.Status))
            {
                _state.Snapshot.Status = "Ready for next ticket";
                _state.Snapshot.Color = "orange";
            }
        }

        if (!headerStatus.IsAvailable && !string.IsNullOrWhiteSpace(headerStatus.Warning))
        {
            Console.WriteLine($"[WorkflowStateStore] Ticket header status warning: {headerStatus.Warning}");
        }
    }
}

public class WorkflowHydrationState
{
    public WorkflowRuntimeSnapshot Snapshot { get; set; } = new();
    public List<WorkflowHistoryEvent> History { get; set; } = new();
}

public class ProjectWorkflowStateSummary
{
    public string ProjectName { get; set; } = "";
    public bool IsActive { get; set; }
    public bool HasStateFile { get; set; }
    public int CurrentStep { get; set; }
    public int TotalSteps { get; set; }
    public string CurrentStepName { get; set; } = "Not started";
    public string Status { get; set; } = "Ready";
    public bool CanResume { get; set; }
    public bool WorkflowRunning { get; set; }
    public bool AwaitingUserInput { get; set; }
    public DateTime? LastUpdatedUtc { get; set; }
}

public class WorkflowRuntimeSnapshot
{
    public string RunId { get; set; } = "";
    public string Status { get; set; } = "Ready";
    public string Color { get; set; } = "green";
    public bool WorkflowRunning { get; set; }
    public bool Busy { get; set; }
    public bool AwaitingUserInput { get; set; }
    public int CurrentStep { get; set; }
    public int TotalSteps { get; set; }
    public string CurrentStepName { get; set; } = "Starting...";
    public WorkflowMetrics Metrics { get; set; } = new();
    public bool CanResume { get; set; }
    public int NextStepToRun { get; set; }
    public string SelectedWorkflowTypeId { get; set; } = "";
    public string SelectedWorkflowTypeName { get; set; } = "";
    public string SelectedWorkflowStepsDirectory { get; set; } = "";
    public string SelectedProjectName { get; set; } = "";
    public string SelectedProjectDirectory { get; set; } = "";
    public bool IsCurrentStepTicketIteration { get; set; }
    public TicketIterationHeaderProgress? TicketHeaderStatus { get; set; }
    public TicketIterationProgress? TicketProgress { get; set; }
    public List<TicketIterationAuditEntry> TicketIterationAudits { get; set; } = new();
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    public bool HasHistory { get; set; }
}

public class TicketIterationHeaderProgress
{
    public bool IsTicketIterationStep { get; set; }
    public bool IsAvailable { get; set; }
    public int TotalTickets { get; set; }
    public int CompletedTickets { get; set; }
    public int RemainingTickets { get; set; }
    public int CurrentTicketOrdinal { get; set; }
    public string CurrentTicketId { get; set; } = "";
    public string Warning { get; set; } = "";
}

public class WorkflowHistoryEvent
{
    public long Sequence { get; set; }
    public AgentMessage Message { get; set; } = new();
}

public class ActiveStepContextState
{
    public string RunId { get; set; } = "";
    public int StepNumber { get; set; }
    public int TotalSteps { get; set; }
    public string StepName { get; set; } = "";
    public string StepFilePath { get; set; } = "";
    public string WorkflowTypeId { get; set; } = "";
    public string WorkflowTypeName { get; set; } = "";
    public string WorkflowStepsDirectory { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string ProjectDirectory { get; set; } = "";
    public List<ChatMessage> Messages { get; set; } = new();
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}

internal class PersistedWorkflowState
{
    public WorkflowRuntimeSnapshot Snapshot { get; set; } = new();
    public List<WorkflowHistoryEvent> History { get; set; } = new();
    public ActiveStepContextState? ActiveStep { get; set; }
    public long NextSequence { get; set; }
}

internal class CurrentProjectPointer
{
    public string ProjectName { get; set; } = "";
    public string ProjectDirectory { get; set; } = "";
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}
