using System.Text.Json;
using ABHive;

namespace ABHive.Web;

public class TicketIterationStatusResolver
{
    private readonly AppSettings _settings;

    public TicketIterationStatusResolver(AppSettings settings)
    {
        _settings = settings;
    }

    public TicketIterationHeaderProgress Resolve(WorkflowRuntimeSnapshot snapshot, int? stepNumberOverride = null)
    {
        var workflow = ResolveWorkflow(snapshot);
        var stepNumber = stepNumberOverride ?? snapshot.CurrentStep;
        if (!workflow.IsAvailable || stepNumber <= 0 || stepNumber > workflow.MarkdownFiles.Count)
        {
            return new TicketIterationHeaderProgress
            {
                IsTicketIterationStep = false,
                IsAvailable = false,
                Warning = string.IsNullOrWhiteSpace(workflow.Warning) ? "Step directory unavailable." : workflow.Warning
            };
        }

        return ResolveForStep(snapshot, workflow, stepNumber);
    }

    public TicketIterationResumePoint FindResumePoint(WorkflowRuntimeSnapshot snapshot)
    {
        var workflow = ResolveWorkflow(snapshot);
        if (!workflow.IsAvailable)
        {
            return new TicketIterationResumePoint
            {
                Found = false,
                Warning = workflow.Warning
            };
        }

        for (var i = 0; i < workflow.MarkdownFiles.Count; i++)
        {
            var stepNumber = i + 1;
            var headerStatus = ResolveForStep(snapshot, workflow, stepNumber);
            if (!headerStatus.IsTicketIterationStep || !headerStatus.IsAvailable || headerStatus.RemainingTickets <= 0)
            {
                continue;
            }

            return new TicketIterationResumePoint
            {
                Found = true,
                StepNumber = stepNumber,
                TotalSteps = workflow.MarkdownFiles.Count,
                StepName = Path.GetFileName(workflow.MarkdownFiles[i]),
                StepsDirectory = workflow.StepsDirectory,
                HeaderStatus = headerStatus
            };
        }

        return new TicketIterationResumePoint
        {
            Found = false,
            TotalSteps = workflow.MarkdownFiles.Count,
            StepsDirectory = workflow.StepsDirectory
        };
    }

    private TicketIterationHeaderProgress ResolveForStep(
        WorkflowRuntimeSnapshot snapshot,
        ResolvedWorkflowDirectory workflow,
        int stepNumber)
    {
        var mdFilePath = workflow.MarkdownFiles[stepNumber - 1];
        var metadata = ResolveStepMetadata(mdFilePath, workflow.StepsDirectory);
        var isTicketIterationStep = string.Equals(
            metadata.ExecutionMode,
            "ticketIteration",
            StringComparison.OrdinalIgnoreCase);

        if (!isTicketIterationStep)
        {
            return new TicketIterationHeaderProgress
            {
                IsTicketIterationStep = false,
                IsAvailable = false
            };
        }

        var ticketSourceTemplate = string.IsNullOrWhiteSpace(metadata.TicketSource)
            ? "{{TICKETS_DIR}}/tickets.json"
            : metadata.TicketSource;
        var completedSourceTemplate = string.IsNullOrWhiteSpace(metadata.CompletedSource)
            ? "{{TICKETS_DIR}}/completed.json"
            : metadata.CompletedSource;

        var ticketSource = ApplyPathTokens(ticketSourceTemplate, snapshot);
        var completedSource = ApplyPathTokens(completedSourceTemplate, snapshot);
        var skippedSource = DeriveSkippedSourcePath(completedSource);
        var requestedSource = DeriveRequestedSourcePath(completedSource);

        var tickets = LoadTicketIds(ticketSource, out var ticketError);
        if (ticketError != null)
        {
            return new TicketIterationHeaderProgress
            {
                IsTicketIterationStep = true,
                IsAvailable = false,
                Warning = ticketError
            };
        }

        var completion = LoadCompletedAndLegacySkippedIds(completedSource, out var completedError);
        if (completedError != null)
        {
            return new TicketIterationHeaderProgress
            {
                IsTicketIterationStep = true,
                IsAvailable = false,
                Warning = completedError
            };
        }

        var skippedIds = LoadSkippedIds(skippedSource, out var skippedError);
        if (skippedError != null)
        {
            return new TicketIterationHeaderProgress
            {
                IsTicketIterationStep = true,
                IsAvailable = false,
                Warning = skippedError
            };
        }

        skippedIds.UnionWith(completion.LegacySkippedIds);
        var requestedTicketId = LoadRequestedTicketId(requestedSource);

        var completedCount = tickets.Count(ticketId => completion.CompletedIds.Contains(ticketId));
        var remaining = tickets.Count(ticketId => !completion.CompletedIds.Contains(ticketId) && !skippedIds.Contains(ticketId));
        var firstIncompleteIndex = !string.IsNullOrWhiteSpace(requestedTicketId)
            ? tickets.FindIndex(ticketId =>
                string.Equals(ticketId, requestedTicketId, StringComparison.Ordinal) &&
                !completion.CompletedIds.Contains(ticketId) &&
                !skippedIds.Contains(ticketId))
            : -1;
        if (firstIncompleteIndex < 0)
        {
            firstIncompleteIndex = tickets.FindIndex(ticketId => !completion.CompletedIds.Contains(ticketId) && !skippedIds.Contains(ticketId));
        }

        return new TicketIterationHeaderProgress
        {
            IsTicketIterationStep = true,
            IsAvailable = true,
            TotalTickets = tickets.Count,
            CompletedTickets = completedCount,
            RemainingTickets = remaining,
            CurrentTicketOrdinal = firstIncompleteIndex >= 0 ? firstIncompleteIndex + 1 : 0,
            CurrentTicketId = firstIncompleteIndex >= 0 ? tickets[firstIncompleteIndex] : string.Empty
        };
    }

    private ResolvedWorkflowDirectory ResolveWorkflow(WorkflowRuntimeSnapshot snapshot)
    {
        var stepsDirectory = ResolveStepsDirectory(snapshot);
        if (string.IsNullOrWhiteSpace(stepsDirectory))
        {
            return new ResolvedWorkflowDirectory
            {
                IsAvailable = false,
                Warning = "Step directory unavailable."
            };
        }

        var markdownFiles = Directory.GetFiles(stepsDirectory, "*.md")
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToList();
        if (markdownFiles.Count == 0)
        {
            return new ResolvedWorkflowDirectory
            {
                IsAvailable = false,
                StepsDirectory = stepsDirectory,
                Warning = $"No workflow step files found in {stepsDirectory}"
            };
        }

        return new ResolvedWorkflowDirectory
        {
            IsAvailable = true,
            StepsDirectory = stepsDirectory,
            MarkdownFiles = markdownFiles
        };
    }

    private string ResolveStepsDirectory(WorkflowRuntimeSnapshot snapshot)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        AddDirectoryCandidates(candidates, seen, snapshot.SelectedWorkflowStepsDirectory);

        if (!string.IsNullOrWhiteSpace(snapshot.SelectedWorkflowTypeId))
        {
            var resolvedWorkflowTypesDir = ResolveWorkflowDirectory(
                _settings.WorkflowTypesDirectory,
                Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory,
                "workflowtypes");
            
            if (!string.IsNullOrWhiteSpace(resolvedWorkflowTypesDir))
            {
                AddDirectoryCandidates(
                    candidates,
                    seen,
                    Path.Combine(resolvedWorkflowTypesDir, snapshot.SelectedWorkflowTypeId));
            }
        }

        return candidates.FirstOrDefault(Directory.Exists) ?? string.Empty;
    }

    private static void AddDirectoryCandidates(
        ICollection<string> candidates,
        ISet<string> seen,
        string? configuredDirectory)
    {
        if (string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return;
        }

        var trimmed = configuredDirectory.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            TryAddCandidate(candidates, seen, Path.GetFullPath(trimmed));
            return;
        }

        TryAddCandidate(candidates, seen, Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), trimmed)));
        TryAddCandidate(candidates, seen, Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, trimmed)));
        TryAddCandidate(candidates, seen, Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", trimmed)));
        TryAddCandidate(candidates, seen, Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", trimmed)));
    }

    private static void TryAddCandidate(ICollection<string> candidates, ISet<string> seen, string candidate)
    {
        if (seen.Add(candidate))
        {
            candidates.Add(candidate);
        }
    }

    private static StepMetadata ResolveStepMetadata(string mdFilePath, string stepsDirectory)
    {
        var metadataPath = Path.Combine(
            Path.GetDirectoryName(mdFilePath) ?? stepsDirectory,
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

    private static List<string> LoadTicketIds(string ticketSourcePath, out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(ticketSourcePath))
            {
                error = $"Ticket source not found: {ticketSourcePath}";
                return new List<string>();
            }

            var json = File.ReadAllText(ticketSourcePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                error = $"Ticket source is empty: {ticketSourcePath}";
                return new List<string>();
            }

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = $"Ticket source is not a JSON array: {ticketSourcePath}";
                return new List<string>();
            }

            var ids = new List<string>();
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
            error = $"Ticket source parse failed: {ex.Message}";
            return new List<string>();
        }
    }

    private static HashSet<string> LoadCompletedIds(string completedSourcePath, out string? error)
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

    private static CompletedLoadResult LoadCompletedAndLegacySkippedIds(string completedSourcePath, out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(completedSourcePath))
            {
                return new CompletedLoadResult(
                    new HashSet<string>(StringComparer.Ordinal),
                    new HashSet<string>(StringComparer.Ordinal));
            }

            var json = File.ReadAllText(completedSourcePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new CompletedLoadResult(
                    new HashSet<string>(StringComparer.Ordinal),
                    new HashSet<string>(StringComparer.Ordinal));
            }

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = $"Completed source is not a JSON array: {completedSourcePath}";
                return new CompletedLoadResult(
                    new HashSet<string>(StringComparer.Ordinal),
                    new HashSet<string>(StringComparer.Ordinal));
            }

            var completed = new HashSet<string>(StringComparer.Ordinal);
            var legacySkipped = new HashSet<string>(StringComparer.Ordinal);
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

                if (element.TryGetProperty("skipped", out var skippedElement) &&
                    skippedElement.ValueKind == JsonValueKind.True)
                {
                    legacySkipped.Add(ticketId);
                }
                else
                {
                    completed.Add(ticketId);
                }
            }

            return new CompletedLoadResult(completed, legacySkipped);
        }
        catch (Exception ex)
        {
            error = $"Completed source parse failed: {ex.Message}";
            return new CompletedLoadResult(
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal));
        }
    }

    private static HashSet<string> LoadSkippedIds(string skippedSourcePath, out string? error)
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

    private static string ApplyPathTokens(string path, WorkflowRuntimeSnapshot snapshot)
    {
        var projectRoot = snapshot.SelectedProjectDirectory ?? string.Empty;
        var goalsDir = Path.Combine(projectRoot, "goals");
        var filesDir = Path.Combine(projectRoot, "files");
        var solutionDir = Path.Combine(projectRoot, "solution");
        var planningDir = Path.Combine(projectRoot, "planning");
        var designDir = Path.Combine(projectRoot, "design");
        var ticketsDir = Path.Combine(projectRoot, "tickets");

        return path
            .Replace("{{PROJECT_ROOT}}", projectRoot, StringComparison.Ordinal)
            .Replace("{{GOALS_DIR}}", goalsDir, StringComparison.Ordinal)
            .Replace("{{FILES_DIR}}", filesDir, StringComparison.Ordinal)
            .Replace("{{SOLUTION_DIR}}", solutionDir, StringComparison.Ordinal)
            .Replace("{{PLANNING_DIR}}", planningDir, StringComparison.Ordinal)
            .Replace("{{DESIGN_DIR}}", designDir, StringComparison.Ordinal)
            .Replace("{{TICKETS_DIR}}", ticketsDir, StringComparison.Ordinal);
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

    private record CompletedLoadResult(HashSet<string> CompletedIds, HashSet<string> LegacySkippedIds);
}

public class TicketIterationResumePoint
{
    public bool Found { get; set; }
    public int StepNumber { get; set; }
    public int TotalSteps { get; set; }
    public string StepName { get; set; } = "";
    public string StepsDirectory { get; set; } = "";
    public TicketIterationHeaderProgress? HeaderStatus { get; set; }
    public string Warning { get; set; } = "";
}

internal class ResolvedWorkflowDirectory
{
    public bool IsAvailable { get; set; }
    public string StepsDirectory { get; set; } = "";
    public List<string> MarkdownFiles { get; set; } = new();
    public string Warning { get; set; } = "";
}
