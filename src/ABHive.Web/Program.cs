using ABHive;
using ABHive.Application;
using ABHive.Infrastructure;
using ABHive.Presentation;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Builder;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ABHive.Web;

public class Program
{
public static async Task Main(string[] args)
{
var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel - use default binding (will be overridden by appsettings if present)
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    // serverOptions.ListenAnyIP(8335);
});

// Configuration
var configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
var defaultsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.defaults.json");
var settingsFileWriteLock = new SemaphoreSlim(1, 1);
AppSettings settings;

// Auto-create appsettings.json if it doesn't exist
if (!File.Exists(configPath))
{
    if (File.Exists(defaultsPath))
    {
        File.Copy(defaultsPath, configPath);
        Console.WriteLine($"[AppSettings] Created {configPath} from defaults template.");
    }
    else
    {
        // Fallback: create minimal valid config from AppSettings defaults
        Console.WriteLine($"[AppSettings] No appsettings.json or defaults template found. Using built-in defaults.");
    }
}

if (File.Exists(configPath))
{
    var json = await File.ReadAllTextAsync(configPath);
    
    using var doc = System.Text.Json.JsonDocument.Parse(json);
    var root = doc.RootElement;
    
    settings = new AppSettings();
    
    if (root.TryGetProperty("LmStudioUrl", out var lmUrl))
        settings.LmStudioUrl = lmUrl.GetString() ?? "http://localhost:1234";

    if (root.TryGetProperty("LlmApiKey", out var llmApiKey))
        settings.LlmApiKey = llmApiKey.GetString() ?? "";

    if (root.TryGetProperty("LlmServers", out var llmServers) && llmServers.ValueKind == JsonValueKind.Array)
        settings.LlmServers = ParseLlmServers(llmServers);

    if (root.TryGetProperty("ActiveLlmServerId", out var activeLlmServerId))
        settings.ActiveLlmServerId = activeLlmServerId.GetString() ?? "";

    if (root.TryGetProperty("ActiveLlmModelId", out var activeLlmModelId))
        settings.ActiveLlmModelId = activeLlmModelId.GetString() ?? "";
    
    if (root.TryGetProperty("ModelName", out var modelName))
        settings.ModelName = modelName.GetString() ?? "default";
    
    if (root.TryGetProperty("StepsDirectory", out var stepsDir))
        settings.StepsDirectory = stepsDir.GetString() ?? "./workflowtypes/chat";

    if (root.TryGetProperty("WorkflowTypesDirectory", out var workflowTypesDir))
        settings.WorkflowTypesDirectory = workflowTypesDir.GetString() ?? "./workflowtypes";
    
    if (root.TryGetProperty("LogFilePath", out var logPath))
        settings.LogFilePath = logPath.GetString() ?? "./logs/metrics.json";
    
    if (root.TryGetProperty("DefaultToolTimeoutMs", out var timeout))
        settings.DefaultToolTimeoutMs = timeout.GetUInt32() == 0 ? 180000 : (int)timeout.GetUInt32();

    if (root.TryGetProperty("LlmInactivityTimeoutMs", out var llmTimeout))
        settings.LlmInactivityTimeoutMs = llmTimeout.GetInt32();

    if (root.TryGetProperty("LlmTemperature", out var llmTemperature))
        settings.LlmTemperature = llmTemperature.GetDouble();

    if (root.TryGetProperty("LlmTopP", out var llmTopP))
        settings.LlmTopP = llmTopP.GetDouble();

    if (root.TryGetProperty("LlmTopK", out var llmTopK))
        settings.LlmTopK = llmTopK.GetInt32();

    if (root.TryGetProperty("LlmMaxTokens", out var llmMaxTokens))
        settings.LlmMaxTokens = llmMaxTokens.GetInt32();

    if (root.TryGetProperty("LlmFrequencyPenalty", out var llmFrequencyPenalty))
        settings.LlmFrequencyPenalty = llmFrequencyPenalty.GetDouble();

    if (root.TryGetProperty("LlmPresencePenalty", out var llmPresencePenalty))
        settings.LlmPresencePenalty = llmPresencePenalty.GetDouble();

    if (root.TryGetProperty("LlmStopSequences", out var llmStopSequences))
        settings.LlmStopSequences = llmStopSequences.GetString() ?? "";

    if (root.TryGetProperty("TelegramEnabled", out var telegramEnabled))
        settings.TelegramEnabled = telegramEnabled.GetBoolean();

    if (root.TryGetProperty("TelegramBotToken", out var telegramBotToken))
        settings.TelegramBotToken = telegramBotToken.GetString() ?? "";

    if (root.TryGetProperty("TelegramChatId", out var telegramChatId) && telegramChatId.TryGetInt64(out var chatId))
        settings.TelegramChatId = chatId;

    if (root.TryGetProperty("TelegramPollTimeoutSeconds", out var telegramTimeout))
        settings.TelegramPollTimeoutSeconds = telegramTimeout.GetInt32();

    if (root.TryGetProperty("TelegramSwitchContextMessageCount", out var telegramSwitchMessageCount))
        settings.TelegramSwitchContextMessageCount = telegramSwitchMessageCount.GetInt32();

    if (root.TryGetProperty("ProjectRootDirectory", out var projectRootDirectory))
        settings.ProjectRootDirectory = projectRootDirectory.GetString() ?? "./projects";

    if (root.TryGetProperty("CurrentVersion", out var currentVersion))
        settings.CurrentVersion = currentVersion.GetString() ?? "0.0.0";
    
    if (root.TryGetProperty("ToolConfigs", out var toolConfigs))
    {
        var tools = new Dictionary<string, ToolConfig>();
        foreach (var prop in toolConfigs.EnumerateObject())
        {
            var config = new ToolConfig
            {
                Name = prop.Name,
                Enabled = prop.Value.GetProperty("Enabled").GetBoolean()
            };
            tools[prop.Name] = config;
        }
        settings.ToolConfigs = tools;
    }
}
else
{
    settings = new AppSettings();
}

settings.ValidateAndSet();
settings.WorkflowTypesDirectory = ResolveWorkflowDirectory(
    settings.WorkflowTypesDirectory,
    Directory.GetCurrentDirectory(),
    AppContext.BaseDirectory,
    "workflowtypes");
settings.StepsDirectory = ResolveWorkflowDirectory(
    settings.StepsDirectory,
    Directory.GetCurrentDirectory(),
    AppContext.BaseDirectory,
    "workflowtypes");
string projectsRoot;
try
{
    projectsRoot = ResolveProjectsRoot(settings.ProjectRootDirectory, Directory.GetCurrentDirectory());
}
catch
{
    settings.ProjectRootDirectory = "./projects";
    projectsRoot = ResolveProjectsRoot(settings.ProjectRootDirectory, Directory.GetCurrentDirectory());
}
EnsureWorkspaceDirectories(projectsRoot);

// Services
builder.Services.AddSingleton<AppSettings>(settings);
builder.Services.AddSingleton<WorkflowTypeCatalog>();
builder.Services.AddSingleton<VersionService>();
builder.Services.AddSingleton(sp =>
    new ProjectWorkspaceService(projectsRoot));
builder.Services.AddSingleton<ProjectDashboardService>();
builder.Services.AddSingleton<TicketIterationStatusResolver>();
builder.Services.AddSingleton<IToolCache, ToolCache>();
builder.Services.AddHttpClient("llm", client =>
{
    client.BaseAddress = new Uri(settings.LmStudioUrl);
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient("telegram", client =>
{
    client.BaseAddress = new Uri("https://api.telegram.org/");
});
builder.Services.AddTransient<ILLMClient>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient("llm");
    return new LLMClient(httpClient, settings, args.Contains("--debug"));
});
builder.Services.AddTransient<IToolExecutor, ToolExecutor>();
builder.Services.AddTransient<IStepConversationService, StepConversationService>();
builder.Services.AddTransient<IMetricsLogger, MetricsLogger>();
builder.Services.AddTransient<ABHive.Application.IConsoleOutputFormatter, WebOutputFormatter>();
builder.Services.AddSingleton<WorkflowStateStore>();

// Application services
builder.Services.AddTransient<IWorkflowOrchestrator>(sp =>
{
    var settings = sp.GetRequiredService<AppSettings>();
    var llmClient = sp.GetRequiredService<ILLMClient>();
    var toolExecutor = sp.GetRequiredService<IToolExecutor>();
    var stepConversationService = sp.GetRequiredService<IStepConversationService>();
    var formatter = sp.GetRequiredService<ABHive.Application.IConsoleOutputFormatter>();
    var metricsLogger = sp.GetService<IMetricsLogger>();
    var webSocketHandler = sp.GetRequiredService<WebSocketHandler>();
    var orchestrator = new WorkflowOrchestrator(settings, llmClient, toolExecutor, stepConversationService, formatter, metricsLogger, args.Contains("--debug"))
    {
        SendLlmResponseAsync = webSocketHandler.PublishLlmResponseAsync,
        SendLlmResponseWithReasoningAsync = webSocketHandler.PublishLlmResponseAsync,
        SendToolRequestAsync = webSocketHandler.PublishToolRequestAsync,
        SendToolExecutionAsync = webSocketHandler.PublishToolExecutionAsync
    };
    return orchestrator;
});

builder.Services.AddSingleton<WebSocketHandler>(sp =>
    new WebSocketHandler(
        sp.GetRequiredService<AppSettings>(),
        sp.GetRequiredService<WorkflowStateStore>(),
        sp.GetRequiredService<WorkflowTypeCatalog>(),
        sp.GetRequiredService<TicketIterationStatusResolver>(),
        sp.GetRequiredService<IServiceScopeFactory>(),
        args.Contains("--debug")));
builder.Services.AddHostedService<TelegramBotService>();
builder.Services.AddCors();

// Web Host
var app = builder.Build();
_ = app.Services.GetRequiredService<ProjectWorkspaceService>();
var startupStateStore = app.Services.GetRequiredService<WorkflowStateStore>();
var startupSnapshot = startupStateStore.GetHydrationAsync().GetAwaiter().GetResult().Snapshot;
if (!string.IsNullOrWhiteSpace(startupSnapshot.SelectedProjectDirectory))
{
    settings.SelectedProjectName = startupSnapshot.SelectedProjectName;
    settings.SelectedProjectDirectory = startupSnapshot.SelectedProjectDirectory;
}

// Enable CORS (for browser WebSocket connections)
app.UseCors(builder =>
{
    builder.AllowAnyOrigin()
           .AllowAnyMethod()
           .AllowAnyHeader();
});

// Use WebSockets middleware (required for WebSocket support)
app.UseWebSockets();

// Handle WebSocket upgrade requests BEFORE static files and routing
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/ws/agent" && context.WebSockets.IsWebSocketRequest)
    {
        var webSocketHandler = context.RequestServices.GetRequiredService<WebSocketHandler>();
        using var socket = await context.WebSockets.AcceptWebSocketAsync();

        await webSocketHandler.HandleWebSocketAsync(socket);
        return;
    }
    
    await next(context);
});

// Static files - serve from bin/ClientApp directory
var clientAppPath = Path.Combine(AppContext.BaseDirectory, "ClientApp");
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(clientAppPath),
});
app.UseRouting();

// API endpoints - Minimal APIs
app.MapGet("/api/status", async (WebSocketHandler webSocketHandler, WorkflowStateStore workflowStateStore) =>
{
    var hydration = await workflowStateStore.GetHydrationAsync();
    var snapshot = hydration.Snapshot;

    return Results.Ok(new
    {
        workflowRunning = snapshot.WorkflowRunning,
        busy = snapshot.Busy,
        awaitingUserInput = snapshot.AwaitingUserInput,
        canResume = snapshot.CanResume,
        nextStepToRun = snapshot.NextStepToRun,
        isCurrentStepTicketIteration = snapshot.IsCurrentStepTicketIteration,
        ticketHeaderStatus = MapTicketHeaderStatus(snapshot.TicketHeaderStatus),
        currentStep = snapshot.CurrentStep,
        totalSteps = snapshot.TotalSteps,
        currentStepName = snapshot.CurrentStepName,
        ticketProgress = snapshot.TicketProgress == null ? null : new
        {
            isTicketIterationStep = snapshot.TicketProgress.IsTicketIterationStep,
            stepKey = snapshot.TicketProgress.StepKey,
            stepName = snapshot.TicketProgress.StepName,
            ticketId = snapshot.TicketProgress.TicketId,
            ticketTitle = snapshot.TicketProgress.TicketTitle,
            attempt = snapshot.TicketProgress.Attempt,
            maxAttempts = snapshot.TicketProgress.MaxAttempts,
            totalTickets = snapshot.TicketProgress.TotalTickets,
            completedTickets = snapshot.TicketProgress.CompletedTickets,
            remainingTickets = snapshot.TicketProgress.RemainingTickets,
            retryExhausted = snapshot.TicketProgress.RetryExhausted,
            status = snapshot.TicketProgress.Status,
            lastUpdatedUtc = snapshot.TicketProgress.LastUpdatedUtc
        },
        status = snapshot.Status,
        color = snapshot.Color,
        connectedClients = webSocketHandler.ConnectedClients
    });
});

app.MapGet("/api/workflow/state", async (WorkflowStateStore workflowStateStore, AppSettings appSettings) =>
{
    var hydration = await workflowStateStore.GetHydrationAsync();
    var snapshot = hydration.Snapshot;

    return Results.Ok(new
    {
        snapshot = new
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
            ticketHeaderStatus = MapTicketHeaderStatus(snapshot.TicketHeaderStatus),
            currentStep = snapshot.CurrentStep,
            totalSteps = snapshot.TotalSteps,
            currentStepName = snapshot.CurrentStepName,
            ticketProgress = snapshot.TicketProgress == null ? null : new
            {
                isTicketIterationStep = snapshot.TicketProgress.IsTicketIterationStep,
                stepKey = snapshot.TicketProgress.StepKey,
                stepName = snapshot.TicketProgress.StepName,
                ticketId = snapshot.TicketProgress.TicketId,
                ticketTitle = snapshot.TicketProgress.TicketTitle,
                attempt = snapshot.TicketProgress.Attempt,
                maxAttempts = snapshot.TicketProgress.MaxAttempts,
                totalTickets = snapshot.TicketProgress.TotalTickets,
                completedTickets = snapshot.TicketProgress.CompletedTickets,
                remainingTickets = snapshot.TicketProgress.RemainingTickets,
                retryExhausted = snapshot.TicketProgress.RetryExhausted,
                status = snapshot.TicketProgress.Status,
                lastUpdatedUtc = snapshot.TicketProgress.LastUpdatedUtc
            },
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
            },
            appVersion = appSettings.CurrentVersion
        },
        history = hydration.History.Select(item => item.Message).ToList()
    });
});

app.MapGet("/api/workflow/view/current", async (WorkflowStateStore workflowStateStore, AppSettings appSettings) =>
{
    var hydration = await workflowStateStore.GetHydrationAsync();
    var snapshot = hydration.Snapshot;

    var currentStepNumber = snapshot.CurrentStep;
    var selectedTicketId =
        snapshot.TicketHeaderStatus?.CurrentTicketId ??
        snapshot.TicketProgress?.TicketId ??
        string.Empty;

    object? step = null;
    object? ticket = null;

    var stepsDirectory = !string.IsNullOrWhiteSpace(snapshot.SelectedWorkflowStepsDirectory)
        ? snapshot.SelectedWorkflowStepsDirectory
        : appSettings.StepsDirectory;

    var resolvedStepsDirectory = ResolveWorkflowDirectory(
        stepsDirectory,
        Directory.GetCurrentDirectory(),
        AppContext.BaseDirectory,
        "workflowtypes");

    if (!string.IsNullOrWhiteSpace(resolvedStepsDirectory) &&
        Directory.Exists(resolvedStepsDirectory) &&
        currentStepNumber > 0)
    {
        var markdownFiles = Directory.GetFiles(resolvedStepsDirectory, "*.md")
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToList();

        if (currentStepNumber <= markdownFiles.Count)
        {
            var stepPath = markdownFiles[currentStepNumber - 1];
            var stepName = Path.GetFileName(stepPath);
            var stepContent = File.ReadAllText(stepPath);
            var metadata = LoadStepMetadata(stepPath);
            var isTicketIterationStep = string.Equals(metadata.ExecutionMode, "ticketIteration", StringComparison.OrdinalIgnoreCase);

            step = new
            {
                stepNumber = currentStepNumber,
                stepName,
                content = stepContent,
                isTicketIterationStep
            };

            if (isTicketIterationStep && !string.IsNullOrWhiteSpace(selectedTicketId))
            {
                var ticketSourceTemplate = string.IsNullOrWhiteSpace(metadata.TicketSource)
                    ? "{{TICKETS_DIR}}/tickets.json"
                    : metadata.TicketSource;
                var completedSourceTemplate = string.IsNullOrWhiteSpace(metadata.CompletedSource)
                    ? "{{TICKETS_DIR}}/completed.json"
                    : metadata.CompletedSource;

                var ticketSourcePath = ApplyProjectPathTokens(ticketSourceTemplate, snapshot.SelectedProjectDirectory);
                var completedSourcePath = ApplyProjectPathTokens(completedSourceTemplate, snapshot.SelectedProjectDirectory);
                var skippedSourcePath = DeriveSkippedSourcePath(completedSourcePath);

                var completedIds = LoadCompletedTicketIds(completedSourcePath, out _);
                var skippedIds = LoadSkippedTicketIds(skippedSourcePath, out _);
                skippedIds.UnionWith(LoadLegacySkippedTicketIdsFromCompleted(completedSourcePath));

                var detail = LoadTicketDetail(ticketSourcePath, selectedTicketId, out _);
                if (detail != null)
                {
                    ticket = new
                    {
                        ticketId = detail.Id,
                        title = detail.Title,
                        description = detail.Description,
                        priority = detail.Priority,
                        type = detail.Type,
                        status = completedIds.Contains(detail.Id)
                            ? "Completed"
                            : skippedIds.Contains(detail.Id)
                                ? "Skipped"
                                : "Open",
                        dependencies = detail.Dependencies,
                        definitionOfDone = detail.DefinitionOfDone
                    };
                }
            }
        }
    }

    return Results.Ok(new
    {
        currentStep = step,
        currentTicket = ticket
    });
});

app.MapGet("/api/workflowtypes", (WorkflowTypeCatalog workflowTypeCatalog) =>
{
    var items = workflowTypeCatalog.GetWorkflowTypes()
        .Select(item => new
        {
            id = item.Id,
            name = item.Name,
            stepCount = item.StepCount
        })
        .ToList();

    return Results.Ok(new { workflowTypes = items });
});

app.MapGet("/api/projects", (ProjectWorkspaceService projectWorkspaceService) =>
{
    var projects = projectWorkspaceService.ListProjects()
        .Select(project => new
        {
            name = project.ProjectName,
            projectDirectory = project.ProjectDirectory,
            goalsDirectory = project.GoalsDirectory,
            filesDirectory = project.FilesDirectory,
            solutionDirectory = project.SolutionDirectory,
            planningDirectory = project.PlanningDirectory,
            designDirectory = project.DesignDirectory,
            ticketsDirectory = project.TicketsDirectory
        })
        .ToList();

    return Results.Ok(new { projects });
});

app.MapGet("/api/dashboard/projects", (ProjectDashboardService dashboardService) =>
{
    var projects = dashboardService.GetProjectDashboards();
    return Results.Ok(new { projects });
});

app.MapPost("/api/dashboard/tickets/reopen", (DashboardTicketActionRequest request, ProjectWorkspaceService projectWorkspaceService) =>
{
    var projectName = (request.ProjectName ?? string.Empty).Trim();
    var ticketId = (request.TicketId ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(projectName) || string.IsNullOrWhiteSpace(ticketId))
    {
        return Results.BadRequest(new { success = false, message = "projectName and ticketId are required." });
    }

    if (!projectWorkspaceService.TryResolveWorkspace(projectName, out var workspace, out var error))
    {
        return Results.BadRequest(new { success = false, message = error });
    }

    var completedPath = Path.Combine(workspace.TicketsDirectory, "completed.json");
    var skippedPath = Path.Combine(workspace.TicketsDirectory, "skipped.json");

    DashboardTicketFileOps.TryRemoveCompletedTicket(completedPath, ticketId, out _);
    DashboardTicketFileOps.TryRemoveLegacySkippedTicketFromCompleted(completedPath, ticketId, out _);
    DashboardTicketFileOps.TryRemoveSkippedTicket(skippedPath, ticketId, out _);

    return Results.Ok(new { success = true });
});

app.MapPost("/api/dashboard/tickets/resume", (DashboardTicketActionRequest request, ProjectWorkspaceService projectWorkspaceService) =>
{
    var projectName = (request.ProjectName ?? string.Empty).Trim();
    var ticketId = (request.TicketId ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(projectName) || string.IsNullOrWhiteSpace(ticketId))
    {
        return Results.BadRequest(new { success = false, message = "projectName and ticketId are required." });
    }

    if (!projectWorkspaceService.TryResolveWorkspace(projectName, out var workspace, out var error))
    {
        return Results.BadRequest(new { success = false, message = error });
    }

    var completedPath = Path.Combine(workspace.TicketsDirectory, "completed.json");
    var skippedPath = Path.Combine(workspace.TicketsDirectory, "skipped.json");

    DashboardTicketFileOps.TryRemoveSkippedTicket(skippedPath, ticketId, out _);
    DashboardTicketFileOps.TryRemoveLegacySkippedTicketFromCompleted(completedPath, ticketId, out _);

    return Results.Ok(new { success = true });
});

app.MapPost("/api/projects", (CreateProjectRequest request, ProjectWorkspaceService projectWorkspaceService) =>
{
    var result = projectWorkspaceService.CreateProject(request.ProjectName);
    if (!result.Success)
    {
        return Results.BadRequest(new { success = false, message = result.Error });
    }

    var workspace = result.Workspace!;
    return Results.Ok(new
    {
        success = true,
        created = result.CreatedNew,
        alreadyExists = result.AlreadyExists,
        project = new
        {
            name = workspace.ProjectName,
            projectDirectory = workspace.ProjectDirectory,
            goalsDirectory = workspace.GoalsDirectory,
            filesDirectory = workspace.FilesDirectory,
            solutionDirectory = workspace.SolutionDirectory,
            planningDirectory = workspace.PlanningDirectory,
            designDirectory = workspace.DesignDirectory,
            ticketsDirectory = workspace.TicketsDirectory
        }
    });
});

app.MapGet("/api/workflow/project", async (WorkflowStateStore workflowStateStore) =>
{
    var hydration = await workflowStateStore.GetHydrationAsync();
    return Results.Ok(new
    {
        selectedProjectName = hydration.Snapshot.SelectedProjectName,
        selectedProjectDirectory = hydration.Snapshot.SelectedProjectDirectory
    });
});

app.MapGet("/api/workflow/project/switch-options", async (
    WorkflowStateStore workflowStateStore,
    ProjectWorkspaceService projectWorkspaceService) =>
{
    var projects = projectWorkspaceService.ListProjects();
    var summaries = await workflowStateStore.GetProjectStateSummariesAsync(projects);

    return Results.Ok(new
    {
        projects = summaries.Select(item => new
        {
            projectName = item.ProjectName,
            isActive = item.IsActive,
            hasStateFile = item.HasStateFile,
            currentStep = item.CurrentStep,
            totalSteps = item.TotalSteps,
            currentStepName = item.CurrentStepName,
            status = item.Status,
            canResume = item.CanResume,
            workflowRunning = item.WorkflowRunning,
            awaitingUserInput = item.AwaitingUserInput,
            lastUpdatedUtc = item.LastUpdatedUtc
        }).ToList()
    });
});

app.MapPost("/api/workflow/project/select", async (
    SelectProjectRequest request,
    ProjectWorkspaceService projectWorkspaceService,
    WorkflowStateStore workflowStateStore,
    AppSettings appSettings,
    WebSocketHandler webSocketHandler) =>
{
    if (!projectWorkspaceService.TryResolveWorkspace(request.ProjectName, out var workspace, out var error))
    {
        return Results.BadRequest(new { success = false, message = error });
    }

    var hydration = await workflowStateStore.GetHydrationAsync();
    var snapshot = hydration.Snapshot;
    if (snapshot.Busy)
    {
        return Results.Conflict(new
        {
            success = false,
            message = "Cannot switch project while the LLM is busy."
        });
    }

    if (snapshot.WorkflowRunning || snapshot.AwaitingUserInput || snapshot.CanResume)
    {
        await webSocketHandler.PrepareForProjectSwitchAsync();
    }

    await workflowStateStore.SwitchProjectAsync(workspace.ProjectName, workspace.ProjectDirectory);
    var switchedHydration = await workflowStateStore.GetHydrationAsync();
    var switchedSnapshot = switchedHydration.Snapshot;

    appSettings.SelectedProjectName = switchedSnapshot.SelectedProjectName;
    appSettings.SelectedProjectDirectory = switchedSnapshot.SelectedProjectDirectory;

    return Results.Ok(new
    {
        success = true,
        selectedProjectName = switchedSnapshot.SelectedProjectName,
        selectedProjectDirectory = switchedSnapshot.SelectedProjectDirectory,
        snapshot = new
        {
            runId = switchedSnapshot.RunId,
            status = switchedSnapshot.Status,
            color = switchedSnapshot.Color,
            workflowRunning = switchedSnapshot.WorkflowRunning,
            busy = switchedSnapshot.Busy,
            awaitingUserInput = switchedSnapshot.AwaitingUserInput,
            canResume = switchedSnapshot.CanResume,
            nextStepToRun = switchedSnapshot.NextStepToRun,
            selectedWorkflowTypeId = switchedSnapshot.SelectedWorkflowTypeId,
            selectedWorkflowTypeName = switchedSnapshot.SelectedWorkflowTypeName,
            selectedProjectName = switchedSnapshot.SelectedProjectName,
            selectedProjectDirectory = switchedSnapshot.SelectedProjectDirectory,
            isCurrentStepTicketIteration = switchedSnapshot.IsCurrentStepTicketIteration,
            ticketHeaderStatus = MapTicketHeaderStatus(switchedSnapshot.TicketHeaderStatus),
            currentStep = switchedSnapshot.CurrentStep,
            totalSteps = switchedSnapshot.TotalSteps,
            currentStepName = switchedSnapshot.CurrentStepName,
            ticketProgress = switchedSnapshot.TicketProgress == null ? null : new
            {
                isTicketIterationStep = switchedSnapshot.TicketProgress.IsTicketIterationStep,
                stepKey = switchedSnapshot.TicketProgress.StepKey,
                stepName = switchedSnapshot.TicketProgress.StepName,
                ticketId = switchedSnapshot.TicketProgress.TicketId,
                ticketTitle = switchedSnapshot.TicketProgress.TicketTitle,
                attempt = switchedSnapshot.TicketProgress.Attempt,
                maxAttempts = switchedSnapshot.TicketProgress.MaxAttempts,
                totalTickets = switchedSnapshot.TicketProgress.TotalTickets,
                completedTickets = switchedSnapshot.TicketProgress.CompletedTickets,
                remainingTickets = switchedSnapshot.TicketProgress.RemainingTickets,
                retryExhausted = switchedSnapshot.TicketProgress.RetryExhausted,
                status = switchedSnapshot.TicketProgress.Status,
                lastUpdatedUtc = switchedSnapshot.TicketProgress.LastUpdatedUtc
            },
            hasHistory = switchedSnapshot.HasHistory,
            lastUpdatedUtc = switchedSnapshot.LastUpdatedUtc,
            metrics = new
            {
                totalSteps = switchedSnapshot.Metrics.TotalSteps,
                successfulSteps = switchedSnapshot.Metrics.SuccessfulSteps,
                failedSteps = switchedSnapshot.Metrics.FailedSteps,
                totalDurationMs = switchedSnapshot.Metrics.TotalDurationMs,
                averageStepDurationMs = switchedSnapshot.Metrics.AverageStepDurationMs,
                totalTokensUsed = switchedSnapshot.Metrics.TotalTokensUsed
            }
        },
        history = switchedHydration.History.Select(item => item.Message).ToList()
    });
});

app.MapPost("/api/messages", async (SendMessageRequest request, WebSocketHandler webSocketHandler) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { success = false, message = "Message cannot be empty" });
    }

    var queued = await webSocketHandler.QueueUserMessageAsync(request.Message);
    if (!queued)
    {
        return Results.Conflict(new { success = false, message = "The workflow is not currently waiting for feedback." });
    }

    return Results.Ok(new { success = true, message = "Message queued" });
});

app.MapPost("/api/files/upload", async (
    HttpRequest request,
    WorkflowStateStore workflowStateStore,
    ProjectWorkspaceService projectWorkspaceService) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { success = false, message = "Expected multipart form upload." });
    }

    var form = await request.ReadFormAsync();
    if (form.Files.Count == 0)
    {
        return Results.BadRequest(new { success = false, message = "No files were uploaded." });
    }

    var snapshot = (await workflowStateStore.GetHydrationAsync()).Snapshot;
    var projectRoot = !string.IsNullOrWhiteSpace(snapshot.SelectedProjectDirectory)
        ? snapshot.SelectedProjectDirectory
        : Path.Combine(projectWorkspaceService.ProjectsRoot, "default");
    var filesDirectory = Path.Combine(projectRoot, "files");
    Directory.CreateDirectory(filesDirectory);

    var savedFiles = new List<object>();

    foreach (var file in form.Files)
    {
        if (file.Length <= 0)
        {
            continue;
        }

        var originalFileName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            continue;
        }

        var safeFileName = SanitizeFileName(originalFileName);
        var destinationPath = GetUniqueFilePath(filesDirectory, safeFileName);

        await using var stream = File.Create(destinationPath);
        await file.CopyToAsync(stream);

        var savedName = Path.GetFileName(destinationPath);
        savedFiles.Add(new
        {
            fileName = savedName,
            relativePath = $"./files/{savedName}",
            size = file.Length
        });
    }

    if (savedFiles.Count == 0)
    {
        return Results.BadRequest(new { success = false, message = "No valid files were uploaded." });
    }

    return Results.Ok(new
    {
        success = true,
        files = savedFiles
    });
});

app.MapPost("/api/workflow/continue", async (WebSocketHandler webSocketHandler) =>
{
    var queued = await webSocketHandler.QueueContinueAsync();
    if (!queued)
    {
        return Results.Conflict(new { success = false, message = "The workflow is not currently waiting to continue." });
    }

    return Results.Ok(new { success = true, message = "Continuing workflow" });
});

app.MapGet("/api/workflow/skip/options", async (WorkflowStateStore workflowStateStore, AppSettings appSettings) =>
{
    var hydration = await workflowStateStore.GetHydrationAsync();
    var snapshot = hydration.Snapshot;

    var stepsDirectory = !string.IsNullOrWhiteSpace(snapshot.SelectedWorkflowStepsDirectory)
        ? snapshot.SelectedWorkflowStepsDirectory
        : appSettings.StepsDirectory;

    var resolvedStepsDirectory = ResolveWorkflowDirectory(
        stepsDirectory,
        Directory.GetCurrentDirectory(),
        AppContext.BaseDirectory,
        "workflowtypes");

    if (string.IsNullOrWhiteSpace(resolvedStepsDirectory) || !Directory.Exists(resolvedStepsDirectory))
    {
        return Results.Ok(new
        {
            success = false,
            message = "Workflow steps directory is not available.",
            steps = Array.Empty<object>()
        });
    }

    var markdownFiles = Directory.GetFiles(resolvedStepsDirectory, "*.md")
        .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
        .ToList();

    var items = new List<object>(markdownFiles.Count);
    for (var i = 0; i < markdownFiles.Count; i++)
    {
        var stepNumber = i + 1;
        var mdPath = markdownFiles[i];
        var fileName = Path.GetFileName(mdPath);
        var metadata = LoadStepMetadata(mdPath);
        var isTicketIteration = string.Equals(metadata.ExecutionMode, "ticketIteration", StringComparison.OrdinalIgnoreCase);

        object? ticketInfo = null;
        if (isTicketIteration)
        {
            var ticketSourceTemplate = string.IsNullOrWhiteSpace(metadata.TicketSource)
                ? "{{TICKETS_DIR}}/tickets.json"
                : metadata.TicketSource;
            var completedSourceTemplate = string.IsNullOrWhiteSpace(metadata.CompletedSource)
                ? "{{TICKETS_DIR}}/completed.json"
                : metadata.CompletedSource;

            var ticketSourcePath = ApplyProjectPathTokens(ticketSourceTemplate, snapshot.SelectedProjectDirectory);
            var completedSourcePath = ApplyProjectPathTokens(completedSourceTemplate, snapshot.SelectedProjectDirectory);
            var skippedSourcePath = DeriveSkippedSourcePath(completedSourcePath);
            var requestedSourcePath = DeriveRequestedSourcePath(completedSourcePath);

            var tickets = LoadTickets(ticketSourcePath, out var ticketError);
            var completedIds = LoadCompletedTicketIds(completedSourcePath, out var completedError);
            var skippedIds = LoadSkippedTicketIds(skippedSourcePath, out var skippedError);
            var legacySkippedIds = LoadLegacySkippedTicketIdsFromCompleted(completedSourcePath);
            var requestedTicketId = LoadRequestedTicketId(requestedSourcePath);

            ticketInfo = new
            {
                ticketSourcePath,
                completedSourcePath,
                skippedSourcePath,
                requestedSourcePath,
                warning = ticketError ?? completedError ?? skippedError,
                tickets = tickets.Select(ticket => new
                {
                    ticketId = ticket.TicketId,
                    title = ticket.Title,
                    completed = completedIds.Contains(ticket.TicketId),
                    skipped = skippedIds.Contains(ticket.TicketId) || legacySkippedIds.Contains(ticket.TicketId),
                    requested = string.Equals(ticket.TicketId, requestedTicketId, StringComparison.Ordinal)
                }).ToList()
            };
        }

        items.Add(new
        {
            stepNumber,
            fileName,
            stepName = fileName,
            isTicketIterationStep = isTicketIteration,
            ticketInfo
        });
    }

    return Results.Ok(new
    {
        success = true,
        stepsDirectory = resolvedStepsDirectory,
        currentStep = snapshot.CurrentStep,
        totalSteps = markdownFiles.Count,
        isCurrentStepTicketIteration = snapshot.IsCurrentStepTicketIteration,
        steps = items
    });
});

app.MapGet("/api/workflow/steps/{stepNumber:int}/content", async (int stepNumber, WorkflowStateStore workflowStateStore, AppSettings appSettings) =>
{
    var hydration = await workflowStateStore.GetHydrationAsync();
    var snapshot = hydration.Snapshot;

    var stepsDirectory = !string.IsNullOrWhiteSpace(snapshot.SelectedWorkflowStepsDirectory)
        ? snapshot.SelectedWorkflowStepsDirectory
        : appSettings.StepsDirectory;

    var resolvedStepsDirectory = ResolveWorkflowDirectory(
        stepsDirectory,
        Directory.GetCurrentDirectory(),
        AppContext.BaseDirectory,
        "workflowtypes");

    if (string.IsNullOrWhiteSpace(resolvedStepsDirectory) || !Directory.Exists(resolvedStepsDirectory))
    {
        return Results.NotFound(new { success = false, message = "Workflow steps directory is not available." });
    }

    var markdownFiles = Directory.GetFiles(resolvedStepsDirectory, "*.md")
        .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
        .ToList();

    if (stepNumber < 1 || stepNumber > markdownFiles.Count)
    {
        return Results.NotFound(new { success = false, message = $"Step {stepNumber} not found. Available steps: 1-{markdownFiles.Count}." });
    }

    var stepPath = markdownFiles[stepNumber - 1];
    var stepName = Path.GetFileName(stepPath);
    var stepContent = File.ReadAllText(stepPath);
    var metadata = LoadStepMetadata(stepPath);
    var isTicketIterationStep = string.Equals(metadata.ExecutionMode, "ticketIteration", StringComparison.OrdinalIgnoreCase);

    return Results.Ok(new
    {
        success = true,
        stepNumber,
        stepName,
        content = stepContent,
        isTicketIterationStep
    });
});

app.MapPost("/api/workflow/skip/step", async (SkipToStepRequest request, WebSocketHandler webSocketHandler) =>
{
    var result = await webSocketHandler.SkipToStepAsync(request.StepNumber);
    return result.Success
        ? Results.Ok(new { success = true, message = result.Message })
        : Results.Conflict(new { success = false, message = result.Message });
});

app.MapPost("/api/workflow/skip/ticket", async (SkipTicketRequest request, WebSocketHandler webSocketHandler) =>
{
    var result = await webSocketHandler.SkipTicketAsync(request.StepNumber, request.TicketId);
    return result.Success
        ? Results.Ok(new { success = true, message = result.Message })
        : Results.Conflict(new { success = false, message = result.Message });
});

app.MapPost("/api/workflow/skip/ticket/resume", async (ResumeSkippedTicketRequest request, WebSocketHandler webSocketHandler) =>
{
    var result = await webSocketHandler.ResumeSkippedTicketAsync(request.StepNumber, request.TicketId);
    return result.Success
        ? Results.Ok(new { success = true, message = result.Message })
        : Results.Conflict(new { success = false, message = result.Message });
});

app.MapPost("/api/workflow/skip/ticket/reopen", async (ResumeSkippedTicketRequest request, WebSocketHandler webSocketHandler) =>
{
    var result = await webSocketHandler.ReopenCompletedTicketAsync(request.StepNumber, request.TicketId);
    return result.Success
        ? Results.Ok(new { success = true, message = result.Message })
        : Results.Conflict(new { success = false, message = result.Message });
});

app.MapPost("/api/workflow/skip/ticket/start", async (ResumeSkippedTicketRequest request, WebSocketHandler webSocketHandler) =>
{
    var result = await webSocketHandler.StartSpecificTicketAsync(request.StepNumber, request.TicketId);
    return result.Success
        ? Results.Ok(new { success = true, message = result.Message })
        : Results.Conflict(new { success = false, message = result.Message });
});

app.MapPost("/api/stop", async (WebSocketHandler webSocketHandler) =>
{
    await webSocketHandler.StopWorkflowAsync();
    return Results.Ok(new { success = true });
});

app.MapPost("/api/workflow/start", async (StartWorkflowRequest? request, WebSocketHandler webSocketHandler) =>
{
    var started = await webSocketHandler.StartWorkflowAsync(request?.WorkflowTypeId);
    if (!started)
    {
        return Results.BadRequest(new { success = false, message = "A valid workflow type must be selected before starting." });
    }

    return Results.Ok(new { success = true, message = "Workflow started" });
});

app.MapPost("/api/workflow/stop", async (WebSocketHandler webSocketHandler) =>
{
    await webSocketHandler.StopWorkflowAsync();
    return Results.Ok(new { success = true, message = "Workflow stop requested" });
});

app.MapPost("/api/workflow/reset", async (WebSocketHandler webSocketHandler) =>
{
    await webSocketHandler.ResetWorkflowAsync();
    return Results.Ok(new { success = true, message = "Workflow reset" });
});

app.MapGet("/api/metrics", async (WebSocketHandler webSocketHandler, WorkflowStateStore workflowStateStore) =>
{
    var metrics = webSocketHandler.LastMetrics;
    if (metrics == null)
    {
        var hydration = await workflowStateStore.GetHydrationAsync();
        metrics = hydration.Snapshot.Metrics;
    }

    return Results.Ok(new
    {
        totalSteps = metrics?.TotalSteps ?? 0,
        successfulSteps = metrics?.SuccessfulSteps ?? 0,
        failedSteps = metrics?.FailedSteps ?? 0,
        totalDurationMs = metrics?.TotalDurationMs ?? 0L,
        averageStepDurationMs = metrics?.AverageStepDurationMs ?? 0.0,
        totalTokensUsed = metrics?.TotalTokensUsed ?? 0
    });
});

app.MapGet("/api/settings", (AppSettings appSettings) =>
{
    var activeServer = appSettings.GetActiveLlmServer();
    var activeModel = appSettings.GetActiveLlmModel();

    return Results.Ok(new
    {
        settings = new
        {
            llmServers = appSettings.LlmServers.Select(server => new
            {
                id = server.Id,
                name = server.Name,
                baseUrl = server.BaseUrl,
                apiKey = server.ApiKey,
                defaultModelId = server.DefaultModelId,
                models = server.Models.Select(model => new
                {
                    id = model.Id,
                    name = model.Name
                }).ToList()
            }).ToList(),
            activeLlmServerId = appSettings.ActiveLlmServerId,
            activeLlmModelId = appSettings.ActiveLlmModelId,
            lmStudioUrl = activeServer.BaseUrl,
            llmApiKey = activeServer.ApiKey,
            modelName = activeModel.Name,
            stepsDirectory = appSettings.StepsDirectory,
            workflowTypesDirectory = appSettings.WorkflowTypesDirectory,
            logFilePath = appSettings.LogFilePath,
            defaultToolTimeoutMs = appSettings.DefaultToolTimeoutMs,
            llmInactivityTimeoutMs = appSettings.LlmInactivityTimeoutMs,
            telegramEnabled = appSettings.TelegramEnabled,
            telegramBotToken = appSettings.TelegramBotToken,
            telegramChatId = appSettings.TelegramChatId,
            telegramPollTimeoutSeconds = appSettings.TelegramPollTimeoutSeconds,
            telegramSwitchContextMessageCount = appSettings.TelegramSwitchContextMessageCount,
            projectRootDirectory = appSettings.ProjectRootDirectory,
            bashEnabled = appSettings.ToolConfigs.TryGetValue("Bash", out var bashConfig) && bashConfig.Enabled,
            webFetchEnabled = appSettings.ToolConfigs.TryGetValue("WebFetch", out var webFetchConfig) && webFetchConfig.Enabled,
            readFileEnabled = appSettings.ToolConfigs.TryGetValue("ReadFile", out var readFileConfig) && readFileConfig.Enabled,
            writeFileEnabled = appSettings.ToolConfigs.TryGetValue("WriteFile", out var writeFileConfig) && writeFileConfig.Enabled,
            llmTemperature = appSettings.LlmTemperature,
            llmTopP = appSettings.LlmTopP,
            llmTopK = appSettings.LlmTopK,
            llmMaxTokens = appSettings.LlmMaxTokens,
            llmFrequencyPenalty = appSettings.LlmFrequencyPenalty,
            llmPresencePenalty = appSettings.LlmPresencePenalty,
            llmStopSequences = appSettings.LlmStopSequences
        },
        reloadInfo = new
        {
            hotReloadNotes = new[]
            {
                "LLM URL/model/tool settings are applied for new workflow runs and future step message turns.",
                "Changes do not alter in-flight requests that already started."
            },
            restartRequiredNotes = new[]
            {
                "Telegram integration settings currently require a server restart to reliably apply."
            }
        }
    });
});

app.MapPost("/api/settings", async (
    SaveSettingsRequest request,
    AppSettings appSettings,
    WorkflowStateStore workflowStateStore,
    ProjectWorkspaceService projectWorkspaceService) =>
{
    var llmServersRequest = request.LlmServers ?? new List<SaveLlmServerRequest>();
    if (llmServersRequest.Count == 0 &&
        !string.IsNullOrWhiteSpace(request.LmStudioUrl) &&
        !string.IsNullOrWhiteSpace(request.ModelName))
    {
        llmServersRequest = new List<SaveLlmServerRequest>
        {
            new()
            {
                Id = "default-server",
                Name = "Default Server",
                BaseUrl = request.LmStudioUrl?.Trim() ?? string.Empty,
                ApiKey = request.LlmApiKey?.Trim() ?? string.Empty,
                DefaultModelId = "default-model",
                Models = new List<SaveLlmModelRequest>
                {
                    new()
                    {
                        Id = "default-model",
                        Name = request.ModelName?.Trim() ?? "default"
                    }
                }
            }
        };
    }

    if (llmServersRequest.Count == 0)
    {
        return Results.BadRequest(new { success = false, message = "At least one LLM server is required." });
    }

    var normalizedServers = new List<LlmServerSettings>();
    var seenServerIds = new HashSet<string>(StringComparer.Ordinal);

    for (var serverIndex = 0; serverIndex < llmServersRequest.Count; serverIndex++)
    {
        var server = llmServersRequest[serverIndex];
        var serverId = string.IsNullOrWhiteSpace(server.Id) ? $"server-{serverIndex + 1}" : server.Id.Trim();
        var serverName = string.IsNullOrWhiteSpace(server.Name) ? serverId : server.Name.Trim();
        var baseUrl = server.BaseUrl?.Trim() ?? string.Empty;
        var apiKey = server.ApiKey?.Trim() ?? string.Empty;

        if (!seenServerIds.Add(serverId))
        {
            return Results.BadRequest(new { success = false, message = $"Duplicate LLM server id '{serverId}'." });
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return Results.BadRequest(new { success = false, message = $"LLM server '{serverName}' has an invalid BaseUrl." });
        }

        var incomingModels = server.Models ?? new List<SaveLlmModelRequest>();
        if (incomingModels.Count == 0)
        {
            return Results.BadRequest(new { success = false, message = $"LLM server '{serverName}' must contain at least one model." });
        }

        var seenModelIds = new HashSet<string>(StringComparer.Ordinal);
        var normalizedModels = new List<LlmModelSettings>();
        for (var modelIndex = 0; modelIndex < incomingModels.Count; modelIndex++)
        {
            var model = incomingModels[modelIndex];
            var modelId = string.IsNullOrWhiteSpace(model.Id) ? $"{serverId}-model-{modelIndex + 1}" : model.Id.Trim();
            var modelName = model.Name?.Trim() ?? string.Empty;

            if (!seenModelIds.Add(modelId))
            {
                return Results.BadRequest(new { success = false, message = $"Duplicate model id '{modelId}' in server '{serverName}'." });
            }

            if (string.IsNullOrWhiteSpace(modelName))
            {
                return Results.BadRequest(new { success = false, message = $"Model name is required for server '{serverName}'." });
            }

            normalizedModels.Add(new LlmModelSettings
            {
                Id = modelId,
                Name = modelName
            });
        }

        var defaultModelId = server.DefaultModelId?.Trim() ?? "";
        if (!normalizedModels.Any(model => string.Equals(model.Id, defaultModelId, StringComparison.Ordinal)))
        {
            defaultModelId = normalizedModels[0].Id;
        }

        normalizedServers.Add(new LlmServerSettings
        {
            Id = serverId,
            Name = serverName,
            BaseUrl = baseUrl,
            ApiKey = apiKey,
            Models = normalizedModels,
            DefaultModelId = defaultModelId
        });
    }

    var activeServerId = request.ActiveLlmServerId?.Trim() ?? "";
    if (!normalizedServers.Any(server => string.Equals(server.Id, activeServerId, StringComparison.Ordinal)))
    {
        activeServerId = normalizedServers[0].Id;
    }

    var selectedServer = normalizedServers.First(server => string.Equals(server.Id, activeServerId, StringComparison.Ordinal));
    var activeModelId = request.ActiveLlmModelId?.Trim() ?? "";
    if (!selectedServer.Models.Any(model => string.Equals(model.Id, activeModelId, StringComparison.Ordinal)))
    {
        activeModelId = selectedServer.DefaultModelId;
    }

    var activeModel = selectedServer.Models.First(model => string.Equals(model.Id, activeModelId, StringComparison.Ordinal));

    var stepsDirectory = request.StepsDirectory?.Trim() ?? string.Empty;
    var workflowTypesDirectory = request.WorkflowTypesDirectory?.Trim() ?? string.Empty;
    var logFilePath = request.LogFilePath?.Trim() ?? string.Empty;
    var projectRootDirectory = request.ProjectRootDirectory?.Trim() ?? string.Empty;
    var telegramBotToken = request.TelegramBotToken?.Trim() ?? string.Empty;

    if (string.IsNullOrWhiteSpace(stepsDirectory))
    {
        return Results.BadRequest(new { success = false, message = "StepsDirectory is required." });
    }

    if (string.IsNullOrWhiteSpace(workflowTypesDirectory))
    {
        return Results.BadRequest(new { success = false, message = "WorkflowTypesDirectory is required." });
    }

    if (string.IsNullOrWhiteSpace(logFilePath))
    {
        return Results.BadRequest(new { success = false, message = "LogFilePath is required." });
    }

    if (string.IsNullOrWhiteSpace(projectRootDirectory))
    {
        return Results.BadRequest(new { success = false, message = "ProjectRootDirectory is required." });
    }

    if (request.DefaultToolTimeoutMs <= 0)
    {
        return Results.BadRequest(new { success = false, message = "DefaultToolTimeoutMs must be greater than zero." });
    }

    if (request.LlmInactivityTimeoutMs < 0)
    {
        return Results.BadRequest(new { success = false, message = "LlmInactivityTimeoutMs cannot be negative." });
    }

    if (request.TelegramPollTimeoutSeconds <= 0)
    {
        return Results.BadRequest(new { success = false, message = "TelegramPollTimeoutSeconds must be greater than zero." });
    }

    if (request.TelegramSwitchContextMessageCount < 0 || request.TelegramSwitchContextMessageCount > 25)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "TelegramSwitchContextMessageCount must be between 0 and 25."
        });
    }

    if (request.TelegramEnabled)
    {
        if (string.IsNullOrWhiteSpace(telegramBotToken))
        {
            return Results.BadRequest(new { success = false, message = "TelegramBotToken is required when TelegramEnabled is true." });
        }

        if (request.TelegramChatId == 0)
        {
            return Results.BadRequest(new { success = false, message = "TelegramChatId is required when TelegramEnabled is true." });
        }
    }

    var telegramSettingsChanged =
        appSettings.TelegramEnabled != request.TelegramEnabled ||
        !string.Equals(appSettings.TelegramBotToken, telegramBotToken, StringComparison.Ordinal) ||
        appSettings.TelegramChatId != request.TelegramChatId ||
        appSettings.TelegramPollTimeoutSeconds != request.TelegramPollTimeoutSeconds;
    string currentProjectsRoot;
    string requestedProjectsRoot;
    try
    {
        currentProjectsRoot = ResolveProjectsRoot(appSettings.ProjectRootDirectory, Directory.GetCurrentDirectory());
        requestedProjectsRoot = ResolveProjectsRoot(projectRootDirectory, Directory.GetCurrentDirectory());
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { success = false, message = $"ProjectRootDirectory is invalid: {ex.Message}" });
    }

    if (!Directory.Exists(requestedProjectsRoot))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = $"ProjectRootDirectory does not exist: {requestedProjectsRoot}"
        });
    }

    var projectRootChanged = !string.Equals(currentProjectsRoot, requestedProjectsRoot, StringComparison.Ordinal);

    await settingsFileWriteLock.WaitAsync();
    try
    {
        JsonObject root;
        if (File.Exists(configPath))
        {
            var existingJson = await File.ReadAllTextAsync(configPath);
            root = JsonNode.Parse(existingJson)?.AsObject() ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        root["LlmApiKey"] = selectedServer.ApiKey;
        root["ModelName"] = activeModel.Name;
        root["ActiveLlmServerId"] = activeServerId;
        root["ActiveLlmModelId"] = activeModelId;
        root["LlmServers"] = new JsonArray(normalizedServers
            .Select(server => (JsonNode)new JsonObject
            {
                ["Id"] = server.Id,
                ["Name"] = server.Name,
                ["BaseUrl"] = server.BaseUrl,
                ["ApiKey"] = server.ApiKey,
                ["DefaultModelId"] = server.DefaultModelId,
                ["Models"] = new JsonArray(server.Models
                    .Select(model => (JsonNode)new JsonObject
                    {
                        ["Id"] = model.Id,
                        ["Name"] = model.Name
                    }).ToArray())
            }).ToArray());
        root["LmStudioUrl"] = selectedServer.BaseUrl;
        root["StepsDirectory"] = stepsDirectory;
        root["WorkflowTypesDirectory"] = workflowTypesDirectory;
        root["LogFilePath"] = logFilePath;
        root["DefaultToolTimeoutMs"] = request.DefaultToolTimeoutMs;
        root["LlmInactivityTimeoutMs"] = request.LlmInactivityTimeoutMs;
        root["TelegramEnabled"] = request.TelegramEnabled;
        root["TelegramBotToken"] = telegramBotToken;
        root["TelegramChatId"] = request.TelegramChatId;
        root["TelegramPollTimeoutSeconds"] = request.TelegramPollTimeoutSeconds;
        root["TelegramSwitchContextMessageCount"] = request.TelegramSwitchContextMessageCount;
        root["ProjectRootDirectory"] = projectRootDirectory;
        root["ToolConfigs"] = new JsonObject
        {
            ["Bash"] = new JsonObject
            {
                ["Name"] = "Bash",
                ["Enabled"] = request.BashEnabled
            },
            ["WebFetch"] = new JsonObject
            {
                ["Name"] = "WebFetch",
                ["Enabled"] = request.WebFetchEnabled
            },
            ["ReadFile"] = new JsonObject
            {
                ["Name"] = "ReadFile",
                ["Enabled"] = request.ReadFileEnabled
            },
            ["WriteFile"] = new JsonObject
            {
                ["Name"] = "WriteFile",
                ["Enabled"] = request.WriteFileEnabled
            }
        };
        root["LlmTemperature"] = request.LlmTemperature ?? appSettings.LlmTemperature;
        root["LlmTopP"] = request.LlmTopP ?? appSettings.LlmTopP;
        root["LlmTopK"] = request.LlmTopK ?? appSettings.LlmTopK;
        root["LlmMaxTokens"] = request.LlmMaxTokens ?? appSettings.LlmMaxTokens;
        root["LlmFrequencyPenalty"] = request.LlmFrequencyPenalty ?? appSettings.LlmFrequencyPenalty;
        root["LlmPresencePenalty"] = request.LlmPresencePenalty ?? appSettings.LlmPresencePenalty;
        root["LlmStopSequences"] = request.LlmStopSequences ?? appSettings.LlmStopSequences;

        var updatedJson = root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(configPath, $"{updatedJson}{Environment.NewLine}");
    }
    finally
    {
        settingsFileWriteLock.Release();
    }

    appSettings.LlmServers = normalizedServers;
    appSettings.ActiveLlmServerId = activeServerId;
    appSettings.ActiveLlmModelId = activeModelId;
    appSettings.LmStudioUrl = selectedServer.BaseUrl;
    appSettings.LlmApiKey = selectedServer.ApiKey;
    appSettings.ModelName = activeModel.Name;
    appSettings.StepsDirectory = stepsDirectory.Trim();
    appSettings.WorkflowTypesDirectory = workflowTypesDirectory.Trim();
    appSettings.LogFilePath = logFilePath;
    appSettings.DefaultToolTimeoutMs = request.DefaultToolTimeoutMs;
    appSettings.LlmInactivityTimeoutMs = request.LlmInactivityTimeoutMs;
    appSettings.TelegramEnabled = request.TelegramEnabled;
    appSettings.TelegramBotToken = telegramBotToken;
    appSettings.TelegramChatId = request.TelegramChatId;
    appSettings.TelegramPollTimeoutSeconds = request.TelegramPollTimeoutSeconds;
    appSettings.TelegramSwitchContextMessageCount = request.TelegramSwitchContextMessageCount;
    appSettings.ProjectRootDirectory = projectRootDirectory;
    appSettings.ToolConfigs = new Dictionary<string, ToolConfig>
    {
        ["Bash"] = new ToolConfig { Name = "Bash", Enabled = request.BashEnabled },
        ["WebFetch"] = new ToolConfig { Name = "WebFetch", Enabled = request.WebFetchEnabled },
        ["ReadFile"] = new ToolConfig { Name = "ReadFile", Enabled = request.ReadFileEnabled },
        ["WriteFile"] = new ToolConfig { Name = "WriteFile", Enabled = request.WriteFileEnabled }
    };
    appSettings.LlmTemperature = request.LlmTemperature ?? appSettings.LlmTemperature;
    appSettings.LlmTopP = request.LlmTopP ?? appSettings.LlmTopP;
    appSettings.LlmTopK = request.LlmTopK ?? appSettings.LlmTopK;
    appSettings.LlmMaxTokens = request.LlmMaxTokens ?? appSettings.LlmMaxTokens;
    appSettings.LlmFrequencyPenalty = request.LlmFrequencyPenalty ?? appSettings.LlmFrequencyPenalty;
    appSettings.LlmPresencePenalty = request.LlmPresencePenalty ?? appSettings.LlmPresencePenalty;
    appSettings.LlmStopSequences = request.LlmStopSequences ?? appSettings.LlmStopSequences;
    appSettings.ValidateAndSet();
    projectWorkspaceService.UpdateProjectsRoot(ResolveProjectsRoot(appSettings.ProjectRootDirectory, Directory.GetCurrentDirectory()));

    if (projectRootChanged)
    {
        var hydration = await workflowStateStore.GetHydrationAsync();
        var snapshot = hydration.Snapshot;
        if (snapshot.WorkflowRunning || snapshot.Busy || snapshot.AwaitingUserInput || snapshot.CanResume)
        {
            return Results.Conflict(new
            {
                success = false,
                message = "Cannot change project directory while a workflow is active or resumable. Stop or reset the workflow first."
            });
        }

        await workflowStateStore.UpdateProjectsRootAsync(requestedProjectsRoot);
        var updatedHydration = await workflowStateStore.GetHydrationAsync();
        appSettings.SelectedProjectName = updatedHydration.Snapshot.SelectedProjectName;
        appSettings.SelectedProjectDirectory = updatedHydration.Snapshot.SelectedProjectDirectory;
    }

    var restartReasons = new List<string>();
    if (telegramSettingsChanged)
    {
        restartReasons.Add("Telegram service configuration changed.");
    }

    return Results.Ok(new
    {
        success = true,
        message = "Settings saved to appsettings.json.",
        restartRequired = restartReasons.Count > 0,
        restartReasons,
        appliedForFutureWork = new[]
        {
            "Changes apply to future workflow runs and future message turns.",
            "In-flight workflow calls already running are not interrupted."
        }
    });
});

app.MapPost("/api/settings/test-connection", async (TestLlmConnectionRequest request) =>
{
    var lmStudioUrl = request.LmStudioUrl?.Trim() ?? string.Empty;
    var llmApiKey = request.LlmApiKey?.Trim() ?? string.Empty;
    if (!Uri.TryCreate(lmStudioUrl, UriKind.Absolute, out var lmUri) ||
        (lmUri.Scheme != Uri.UriSchemeHttp && lmUri.Scheme != Uri.UriSchemeHttps))
    {
        return Results.BadRequest(new { success = false, message = "LmStudioUrl must be a valid HTTP/HTTPS URL." });
    }

    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        using var response = await SendOpenAiRequestAsync(
            client,
            lmUri,
            llmApiKey,
            HttpMethod.Get,
            "models",
            null,
            CancellationToken.None);
        if (response.IsSuccessStatusCode)
        {
            return Results.Ok(new { success = true, message = "Connection successful." });
        }

        var body = (await response.Content.ReadAsStringAsync()).Trim();
        var shortBody = body.Length > 240 ? body[..240] : body;
        return Results.Ok(new
        {
            success = false,
            message = $"Connection failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
            details = string.IsNullOrWhiteSpace(shortBody) ? null : shortBody
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { success = false, message = $"Connection failed: {ex.Message}" });
    }
});

app.MapPost("/api/settings/test-model", async (TestLlmModelRequest request, CancellationToken ct) =>
{
    var baseUrl = request.LlmServerUrl?.Trim() ?? string.Empty;
    var llmApiKey = request.LlmApiKey?.Trim() ?? string.Empty;
    var modelId = request.ModelId?.Trim() ?? string.Empty;
    var modelName = request.ModelName?.Trim() ?? string.Empty;

    if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
        (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
    {
        return Results.BadRequest(new { success = false, message = "LlmServerUrl must be a valid HTTP/HTTPS URL." });
    }

    if (string.IsNullOrWhiteSpace(modelId) && string.IsNullOrWhiteSpace(modelName))
    {
        return Results.BadRequest(new { success = false, message = "ModelId is required." });
    }

    // Keep this fast for a settings UX.
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    linkedCts.CancelAfter(TimeSpan.FromSeconds(12));

    static JsonObject BuildResponsesPayload(string model) => new()
    {
        ["model"] = model,
        ["input"] = "Nick is sooo cool, but reply with exactly: ok"
    };

    static JsonObject BuildChatCompletionsPayload(string model) => new()
    {
        ["model"] = model,
        ["messages"] = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = "Nick is sooo cool, but reply with exactly: ok"
            }
        },
        ["temperature"] = 0,
        ["max_tokens"] = 16
    };

    async Task<(bool Success, string Endpoint, string ModelUsed, string? Snippet, string? Details)> TryTestAsync(
        HttpClient client,
        string model)
    {
        // Prefer the newer Responses API when available; fall back to Chat Completions.
        using (var responses = await SendOpenAiRequestAsync(
                   client,
                   baseUri,
                   llmApiKey,
                   HttpMethod.Post,
                   "responses",
                   BuildResponsesPayload(model),
                   linkedCts.Token))
        {
            var body = await responses.Content.ReadAsStringAsync(linkedCts.Token);
            if (responses.IsSuccessStatusCode)
            {
                var snippet = TryExtractResponsesText(body) ?? TryExtractChatCompletionText(body);
                return (true, "responses", model, snippet, null);
            }

            // If the endpoint isn't supported, try chat completions.
            if ((int)responses.StatusCode != 404)
            {
                var shortBody = body.Trim();
                if (shortBody.Length > 300) shortBody = shortBody[..300];
                return (false, "responses", model, null, $"HTTP {(int)responses.StatusCode} {responses.ReasonPhrase}{(string.IsNullOrWhiteSpace(shortBody) ? "" : $": {shortBody}")}");
            }
        }

        using (var chat = await SendOpenAiRequestAsync(
                   client,
                   baseUri,
                   llmApiKey,
                   HttpMethod.Post,
                   "chat/completions",
                   BuildChatCompletionsPayload(model),
                   linkedCts.Token))
        {
            var body = await chat.Content.ReadAsStringAsync(linkedCts.Token);
            if (chat.IsSuccessStatusCode)
            {
                var snippet = TryExtractChatCompletionText(body) ?? TryExtractResponsesText(body);
                return (true, "chat/completions", model, snippet, null);
            }

            var shortBody = body.Trim();
            if (shortBody.Length > 300) shortBody = shortBody[..300];
            return (false, "chat/completions", model, null, $"HTTP {(int)chat.StatusCode} {chat.ReasonPhrase}{(string.IsNullOrWhiteSpace(shortBody) ? "" : $": {shortBody}")}");
        }
    }

    try
    {
        using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        // Try modelId first (matches typical OpenAI usage). If that fails, try modelName as a fallback.
        var primary = !string.IsNullOrWhiteSpace(modelId) ? modelId : modelName;
        var secondary = !string.IsNullOrWhiteSpace(modelName) && !string.Equals(modelName, primary, StringComparison.Ordinal)
            ? modelName
            : (!string.IsNullOrWhiteSpace(modelId) && !string.Equals(modelId, primary, StringComparison.Ordinal) ? modelId : string.Empty);

        var attempt1 = await TryTestAsync(client, primary);
        if (attempt1.Success)
        {
            return Results.Ok(new
            {
                success = true,
                endpoint = attempt1.Endpoint,
                modelUsed = attempt1.ModelUsed,
                outputSnippet = attempt1.Snippet
            });
        }

        if (!string.IsNullOrWhiteSpace(secondary))
        {
            var attempt2 = await TryTestAsync(client, secondary);
            if (attempt2.Success)
            {
                return Results.Ok(new
                {
                    success = true,
                    endpoint = attempt2.Endpoint,
                    modelUsed = attempt2.ModelUsed,
                    outputSnippet = attempt2.Snippet,
                    message = $"Primary model identifier '{primary}' failed; fallback '{secondary}' succeeded."
                });
            }

            return Results.Ok(new
            {
                success = false,
                message = "Model test failed.",
                details = $"Tried '{primary}': {attempt1.Details ?? "unknown error"}. Tried '{secondary}': {attempt2.Details ?? "unknown error"}."
            });
        }

        return Results.Ok(new
        {
            success = false,
            message = "Model test failed.",
            details = attempt1.Details
        });
    }
    catch (OperationCanceledException)
    {
        return Results.Ok(new { success = false, message = "Model test timed out." });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { success = false, message = $"Model test failed: {ex.Message}" });
    }
});

app.MapPost("/api/settings/llm-models", async (FetchLlmModelsRequest request) =>
{
    var lmStudioUrl = request.LlmServerUrl?.Trim() ?? string.Empty;
    var llmApiKey = request.LlmApiKey?.Trim() ?? string.Empty;

    if (!Uri.TryCreate(lmStudioUrl, UriKind.Absolute, out var lmUri) ||
        (lmUri.Scheme != Uri.UriSchemeHttp && lmUri.Scheme != Uri.UriSchemeHttps))
    {
        return Results.BadRequest(new { success = false, message = "LlmServerUrl must be a valid HTTP/HTTPS URL." });
    }

    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var response = await SendOpenAiRequestAsync(
            client,
            lmUri,
            llmApiKey,
            HttpMethod.Get,
            "models",
            null,
            CancellationToken.None);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var shortBody = responseBody.Length > 300 ? responseBody[..300] : responseBody;
            return Results.Ok(new
            {
                success = false,
                message = $"Failed to fetch models: HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                details = string.IsNullOrWhiteSpace(shortBody) ? null : shortBody
            });
        }

        var models = ParseModelNames(responseBody);
        if (models.Count == 0)
        {
            return Results.Ok(new
            {
                success = false,
                message = "Connected, but no models were returned from /v1/models."
            });
        }

        return Results.Ok(new
        {
            success = true,
            models
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new
        {
            success = false,
            message = $"Failed to fetch models: {ex.Message}"
        });
    }
});

app.MapPost("/api/settings/validate-project-root", (ValidateProjectRootRequest request) =>
{
    var raw = request.ProjectRootDirectory?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(raw))
    {
        return Results.Ok(new { success = false, message = "ProjectRootDirectory is required." });
    }

    try
    {
        var resolved = ResolveProjectsRoot(raw, Directory.GetCurrentDirectory());
        if (!Directory.Exists(resolved))
        {
            return Results.Ok(new
            {
                success = false,
                message = $"Directory does not exist: {resolved}"
            });
        }

        return Results.Ok(new
        {
            success = true,
            resolvedDirectory = resolved
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new
        {
            success = false,
            message = $"Invalid directory path: {ex.Message}"
        });
    }
});

app.MapGet("/api/llm/options", (AppSettings appSettings) =>
{
    var activeServer = appSettings.GetActiveLlmServer();
    var activeModel = appSettings.GetActiveLlmModel();

    return Results.Ok(new
    {
        servers = appSettings.LlmServers.Select(server => new
        {
            id = server.Id,
            name = server.Name,
            baseUrl = server.BaseUrl,
            defaultModelId = server.DefaultModelId,
            models = server.Models.Select(model => new
            {
                id = model.Id,
                name = model.Name
            }).ToList()
        }).ToList(),
        activeServerId = appSettings.ActiveLlmServerId,
        activeModelId = appSettings.ActiveLlmModelId,
        currentServerName = activeServer.Name,
        currentModelName = activeModel.Name
    });
});

app.MapGet("/api/version/local", (VersionService versionService) =>
{
    var local = versionService.GetLocalVersionManifest();
    return Results.Ok(new
    {
        appName = local.AppName,
        version = local.Version,
        channel = local.Channel,
        updateManifestUrl = local.UpdateManifestUrl,
        releaseNotesUrl = local.ReleaseNotesUrl,
        publishedAtUtc = local.PublishedAtUtc,
        assets = local.Assets
    });
});

app.MapGet("/api/version/check", async (VersionService versionService, CancellationToken ct) =>
{
    var check = await versionService.GetVersionCheckAsync(ct);
    return Results.Ok(new
    {
        currentVersion = check.CurrentVersion,
        latestVersion = check.LatestVersion,
        updateAvailable = check.UpdateAvailable,
        releaseNotesUrl = check.ReleaseNotesUrl,
        error = check.Error
    });
});

app.MapGet("/api/llm/health", async (AppSettings appSettings, IHttpClientFactory httpClientFactory, CancellationToken ct) =>
{
    var activeServer = appSettings.GetActiveLlmServer();
    var baseUrl = (activeServer.BaseUrl ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(baseUrl))
    {
        return Results.Ok(new
        {
            ok = false,
            baseUrl = "",
            statusCode = (int?)null,
            message = "No active LLM server URL is configured."
        });
    }

    if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
        (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
    {
        return Results.Ok(new
        {
            ok = false,
            baseUrl,
            statusCode = (int?)null,
            message = "Active LLM server URL is invalid."
        });
    }

    try
    {
        // Use a short timeout so the UI can quickly show a friendly setup prompt.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(6));

        var client = httpClientFactory.CreateClient();
        client.Timeout = Timeout.InfiniteTimeSpan;

        using var response = await SendOpenAiRequestAsync(
            client,
            baseUri,
            activeServer.ApiKey,
            HttpMethod.Get,
            "models",
            null,
            linkedCts.Token);
        if (!response.IsSuccessStatusCode)
        {
            return Results.Ok(new
            {
                ok = false,
                baseUrl,
                statusCode = (int)response.StatusCode,
                message = $"LLM server returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
            });
        }

        // We only need to know it's reachable; do not parse full payload.
        return Results.Ok(new
        {
            ok = true,
            baseUrl,
            statusCode = (int)response.StatusCode,
            message = "LLM server reachable."
        });
    }
    catch (OperationCanceledException)
    {
        return Results.Ok(new
        {
            ok = false,
            baseUrl,
            statusCode = (int?)null,
            message = "Timed out while trying to reach the LLM server."
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new
        {
            ok = false,
            baseUrl,
            statusCode = (int?)null,
            message = $"Failed to reach the LLM server: {ex.Message}"
        });
    }
});

app.MapPost("/api/llm/select", (SelectLlmRequest request, AppSettings appSettings) =>
{
    if (!appSettings.TrySetActiveLlmSelection(request.ServerId, request.ModelId, out var error))
    {
        return Results.BadRequest(new { success = false, message = error });
    }

    var activeServer = appSettings.GetActiveLlmServer();
    var activeModel = appSettings.GetActiveLlmModel();
    return Results.Ok(new
    {
        success = true,
        activeServerId = appSettings.ActiveLlmServerId,
        activeModelId = appSettings.ActiveLlmModelId,
        currentServerName = activeServer.Name,
        currentModelName = activeModel.Name,
        message = "Active LLM selection updated."
    });
});

// Default route - serve index.html for any non-file routes
app.Map("/", () => Results.Redirect("/index.html"));
app.MapFallbackToFile("/index.html");

// Run
Console.WriteLine("Starting Agentic LLM Web Server...");
Console.WriteLine($"Configuration: LM Studio at {settings.LmStudioUrl}");
Console.WriteLine($"Steps directory: {settings.StepsDirectory}");
Console.WriteLine($"Workflow types directory: {settings.WorkflowTypesDirectory}");
Console.WriteLine($"Projects directory: {ResolveProjectsRoot(settings.ProjectRootDirectory, Directory.GetCurrentDirectory())}");
Console.WriteLine();
Console.WriteLine("Server starting on http://localhost:8335");
Console.WriteLine("Press Ctrl+C to stop");

try
{
    // Run the app - this should block until shutdown
    await app.RunAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"ERROR: {ex.Message}");
    throw;
}

// This line should never be reached in normal operation
}

static List<LlmServerSettings> ParseLlmServers(JsonElement llmServersElement)
{
    var servers = new List<LlmServerSettings>();

    foreach (var serverElement in llmServersElement.EnumerateArray())
    {
        if (serverElement.ValueKind != JsonValueKind.Object)
        {
            continue;
        }

        var server = new LlmServerSettings
        {
            Id = ReadString(serverElement, "Id"),
            Name = ReadString(serverElement, "Name"),
            BaseUrl = ReadString(serverElement, "BaseUrl"),
            ApiKey = ReadString(serverElement, "ApiKey"),
            DefaultModelId = ReadString(serverElement, "DefaultModelId"),
            Models = new List<LlmModelSettings>()
        };

        if (serverElement.TryGetProperty("Models", out var modelsElement) && modelsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var modelElement in modelsElement.EnumerateArray())
            {
                if (modelElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                server.Models.Add(new LlmModelSettings
                {
                    Id = ReadString(modelElement, "Id"),
                    Name = ReadString(modelElement, "Name")
                });
            }
        }

        servers.Add(server);
    }

    return servers;
}

static List<string> ParseModelNames(string json)
{
    var names = new List<string>();

    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;

    if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
    {
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (item.TryGetProperty("id", out var idProp))
            {
                var id = idProp.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    names.Add(id.Trim());
                }
            }
        }
    }
    else if (root.ValueKind == JsonValueKind.Array)
    {
        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    names.Add(value.Trim());
                }
            }
            else if (item.ValueKind == JsonValueKind.Object &&
                     item.TryGetProperty("id", out var idProp))
            {
                var id = idProp.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    names.Add(id.Trim());
                }
            }
        }
    }

    return names
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

static string ReadString(JsonElement element, string propertyName)
{
    return element.TryGetProperty(propertyName, out var value) ? value.GetString() ?? "" : "";
}

static Uri BuildOpenAiV1Uri(Uri baseUri, string pathUnderV1)
{
    var basePath = baseUri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    var v1Base = basePath.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
        ? $"{basePath}/"
        : $"{basePath}/v1/";

    var normalizedPath = (pathUnderV1 ?? string.Empty).TrimStart('/');
    return new Uri($"{v1Base}{normalizedPath}", UriKind.Absolute);
}

static Task<HttpResponseMessage> SendOpenAiRequestAsync(
    HttpClient client,
    Uri baseUri,
    string? apiKey,
    HttpMethod method,
    string pathUnderV1,
    JsonNode? jsonBody,
    CancellationToken ct)
{
    var requestUri = BuildOpenAiV1Uri(baseUri, pathUnderV1);
    var request = new HttpRequestMessage(method, requestUri);

    if (!string.IsNullOrWhiteSpace(apiKey))
    {
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey.Trim());
    }

    if (jsonBody != null)
    {
        request.Content = new StringContent(
            jsonBody.ToJsonString(),
            System.Text.Encoding.UTF8,
            "application/json");
    }

    return client.SendAsync(request, ct);
}

static string? TryExtractChatCompletionText(string json)
{
    try
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.Object &&
                message.TryGetProperty("content", out var contentProp) &&
                contentProp.ValueKind == JsonValueKind.String)
            {
                var text = (contentProp.GetString() ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                text = text.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
                return text.Length > 80 ? text[..80] : text;
            }
        }
    }
    catch
    {
        // Ignore parse failures.
    }

    return null;
}

static string? TryExtractResponsesText(string json)
{
    try
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var outputItem in output.EnumerateArray())
        {
            if (outputItem.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!outputItem.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (contentItem.TryGetProperty("type", out var typeProp) &&
                    typeProp.ValueKind == JsonValueKind.String &&
                    string.Equals(typeProp.GetString(), "output_text", StringComparison.OrdinalIgnoreCase) &&
                    contentItem.TryGetProperty("text", out var textProp) &&
                    textProp.ValueKind == JsonValueKind.String)
                {
                    var text = (textProp.GetString() ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    text = text.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
                    return text.Length > 80 ? text[..80] : text;
                }
            }
        }
    }
    catch
    {
        // Ignore parse failures.
    }

    return null;
}

static string ResolveProjectsRoot(string configuredRootDirectory, string currentDirectory)
{
    var configured = string.IsNullOrWhiteSpace(configuredRootDirectory)
        ? "./projects"
        : configuredRootDirectory.Trim();

    return Path.IsPathRooted(configured)
        ? Path.GetFullPath(configured)
        : Path.GetFullPath(Path.Combine(currentDirectory, configured));
}

static string ResolveWorkflowDirectory(
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

static StepMetadata LoadStepMetadata(string mdFilePath)
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

static string ApplyProjectPathTokens(string template, string? projectRootDirectory)
{
    var projectRoot = projectRootDirectory ?? string.Empty;
    var goalsDir = Path.Combine(projectRoot, "goals");
    var filesDir = Path.Combine(projectRoot, "files");
    var solutionDir = Path.Combine(projectRoot, "solution");
    var planningDir = Path.Combine(projectRoot, "planning");
    var designDir = Path.Combine(projectRoot, "design");
    var ticketsDir = Path.Combine(projectRoot, "tickets");

    return (template ?? string.Empty)
        .Replace("{{PROJECT_ROOT}}", projectRoot, StringComparison.Ordinal)
        .Replace("{{GOALS_DIR}}", goalsDir, StringComparison.Ordinal)
        .Replace("{{FILES_DIR}}", filesDir, StringComparison.Ordinal)
        .Replace("{{SOLUTION_DIR}}", solutionDir, StringComparison.Ordinal)
        .Replace("{{PLANNING_DIR}}", planningDir, StringComparison.Ordinal)
        .Replace("{{DESIGN_DIR}}", designDir, StringComparison.Ordinal)
        .Replace("{{TICKETS_DIR}}", ticketsDir, StringComparison.Ordinal);
}

static List<TicketSummary> LoadTickets(string ticketSourcePath, out string? error)
{
    error = null;
    try
    {
        if (!File.Exists(ticketSourcePath))
        {
            error = $"Ticket source not found: {ticketSourcePath}";
            return new List<TicketSummary>();
        }

        var json = File.ReadAllText(ticketSourcePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            error = $"Ticket source is empty: {ticketSourcePath}";
            return new List<TicketSummary>();
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            error = $"Ticket source is not a JSON array: {ticketSourcePath}";
            return new List<TicketSummary>();
        }

        var items = new List<TicketSummary>();
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

            var ticketId = (ticketIdElement.GetString() ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(ticketId))
            {
                continue;
            }

            var title = ReadTicketText(element, "title") ??
                        ReadTicketText(element, "summary") ??
                        ReadTicketText(element, "description") ??
                        "(untitled)";

            title = title.Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();

            if (title.Length > 140)
            {
                title = $"{title[..140]}...";
            }

            items.Add(new TicketSummary(ticketId, title));
        }

        return items;
    }
    catch (Exception ex)
    {
        error = $"Ticket source parse failed: {ex.Message}";
        return new List<TicketSummary>();
    }
}

static TicketDetail? LoadTicketDetail(string ticketSourcePath, string ticketId, out string? error)
{
    error = null;
    try
    {
        if (!File.Exists(ticketSourcePath))
        {
            error = $"Ticket source not found: {ticketSourcePath}";
            return null;
        }

        var json = File.ReadAllText(ticketSourcePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            error = $"Ticket source is empty: {ticketSourcePath}";
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            error = $"Ticket source is not a JSON array: {ticketSourcePath}";
            return null;
        }

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

            var currentId = (ticketIdElement.GetString() ?? string.Empty).Trim();
            if (!string.Equals(currentId, ticketId, StringComparison.Ordinal))
            {
                continue;
            }

            return new TicketDetail
            {
                Id = currentId,
                Title = ReadTicketText(element, "title") ??
                        ReadTicketText(element, "summary") ??
                        ReadTicketText(element, "description") ??
                        "(untitled)",
                Description = ReadTicketText(element, "description") ??
                              ReadTicketText(element, "summary") ??
                              string.Empty,
                Priority = ReadTicketText(element, "priority") ?? string.Empty,
                Type = ReadTicketText(element, "type") ?? string.Empty,
                Dependencies = ReadTicketStringArray(element, "dependencies"),
                DefinitionOfDone = ReadTicketStringArray(element, "definition_of_done")
            };
        }

        return null;
    }
    catch (Exception ex)
    {
        error = $"Ticket source parse failed: {ex.Message}";
        return null;
    }
}

static string? ReadTicketText(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var prop))
    {
        return null;
    }

    if (prop.ValueKind == JsonValueKind.String)
    {
        var value = (prop.GetString() ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    return null;
}

static List<string> ReadTicketStringArray(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Array)
    {
        return new List<string>();
    }

    return prop.EnumerateArray()
        .Where(item => item.ValueKind == JsonValueKind.String)
        .Select(item => (item.GetString() ?? string.Empty).Trim())
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .ToList();
}

static HashSet<string> LoadCompletedTicketIds(string completedSourcePath, out string? error)
{
    error = null;
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

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            error = $"Completed source is not a JSON array: {completedSourcePath}";
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

            var ticketId = (ticketIdElement.GetString() ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(ticketId))
            {
                ids.Add(ticketId);
            }
        }

        return ids;
    }
    catch (Exception ex)
    {
        error = $"Completed source parse failed: {ex.Message}";
        return new HashSet<string>(StringComparer.Ordinal);
    }
}

static HashSet<string> LoadLegacySkippedTicketIdsFromCompleted(string completedSourcePath)
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

        using var doc = JsonDocument.Parse(json);
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

            var ticketId = (ticketIdElement.GetString() ?? string.Empty).Trim();
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

static HashSet<string> LoadSkippedTicketIds(string skippedSourcePath, out string? error)
{
    error = null;
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

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            error = $"Skipped source is not a JSON array: {skippedSourcePath}";
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

            var ticketId = (ticketIdElement.GetString() ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(ticketId))
            {
                ids.Add(ticketId);
            }
        }

        return ids;
    }
    catch (Exception ex)
    {
        error = $"Skipped source parse failed: {ex.Message}";
        return new HashSet<string>(StringComparer.Ordinal);
    }
}

static string DeriveSkippedSourcePath(string completedSourcePath)
{
    var directory = Path.GetDirectoryName(completedSourcePath);
    directory = string.IsNullOrWhiteSpace(directory) ? "." : directory;
    return Path.Combine(directory, "skipped.json");
}

static string DeriveRequestedSourcePath(string completedSourcePath)
{
    var directory = Path.GetDirectoryName(completedSourcePath);
    directory = string.IsNullOrWhiteSpace(directory) ? "." : directory;
    return Path.Combine(directory, "requested-ticket.json");
}

static string LoadRequestedTicketId(string requestedSourcePath)
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

static void EnsureWorkspaceDirectories(string projectsRootDirectory)
{
    Directory.CreateDirectory(projectsRootDirectory);
}

static string SanitizeFileName(string fileName)
{
    var invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
    var sanitized = Regex.Replace(fileName, $"[{invalidChars}]", "_");
    return string.IsNullOrWhiteSpace(sanitized) ? "upload.bin" : sanitized;
}

static string GetUniqueFilePath(string directory, string fileName)
{
    var baseName = Path.GetFileNameWithoutExtension(fileName);
    var extension = Path.GetExtension(fileName);
    var candidate = Path.Combine(directory, fileName);
    var suffix = 1;

    while (File.Exists(candidate))
    {
        candidate = Path.Combine(directory, $"{baseName}-{suffix}{extension}");
        suffix++;
    }

    return candidate;
}

static object? MapTicketHeaderStatus(TicketIterationHeaderProgress? status)
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

}

public record SendMessageRequest(string Message);
public record StartWorkflowRequest(string? WorkflowTypeId);
public record CreateProjectRequest(string ProjectName);
public record SelectProjectRequest(string ProjectName);
public record ResumeSkippedTicketRequest(int StepNumber, string TicketId);
public record SaveSettingsRequest(
    string? LmStudioUrl,
    string? LlmApiKey,
    string? ModelName,
    List<SaveLlmServerRequest>? LlmServers,
    string? ActiveLlmServerId,
    string? ActiveLlmModelId,
    string StepsDirectory,
    string WorkflowTypesDirectory,
    string LogFilePath,
    int DefaultToolTimeoutMs,
    int LlmInactivityTimeoutMs,
    bool TelegramEnabled,
    string TelegramBotToken,
    long TelegramChatId,
    int TelegramPollTimeoutSeconds,
    int TelegramSwitchContextMessageCount,
    string ProjectRootDirectory,
    bool BashEnabled,
    bool WebFetchEnabled,
    bool ReadFileEnabled,
    bool WriteFileEnabled,
    double? LlmTemperature,
    double? LlmTopP,
    int? LlmTopK,
    int? LlmMaxTokens,
    double? LlmFrequencyPenalty,
    double? LlmPresencePenalty,
    string? LlmStopSequences);
public record SaveLlmServerRequest
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public List<SaveLlmModelRequest>? Models { get; set; }
    public string? DefaultModelId { get; set; }
}

public record SaveLlmModelRequest
{
    public string? Id { get; set; }
    public string? Name { get; set; }
}

public record SelectLlmRequest(string ServerId, string ModelId);
public record TestLlmConnectionRequest(string LmStudioUrl, string LlmApiKey);
public record TestLlmModelRequest(string? LlmServerUrl, string? LlmApiKey, string? ModelId, string? ModelName);
public record FetchLlmModelsRequest(string LlmServerUrl, string LlmApiKey);
public record ValidateProjectRootRequest(string ProjectRootDirectory);
public record SkipToStepRequest(int StepNumber);
public record SkipTicketRequest(int StepNumber, string TicketId);

public record TicketSummary(string TicketId, string Title);
public class TicketDetail
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Type { get; set; } = "";
    public List<string> Dependencies { get; set; } = new();
    public List<string> DefinitionOfDone { get; set; } = new();
}
