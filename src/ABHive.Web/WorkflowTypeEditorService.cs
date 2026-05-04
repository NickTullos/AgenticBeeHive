using System.Text.Json;
using System.Text.RegularExpressions;
using ABHive;

namespace ABHive.Web;

public class WorkflowTypeEditorService
{
    private static readonly Regex WorkflowTypeIdPattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);
    private readonly AppSettings _settings;
    private readonly object _writeLock = new();

    public WorkflowTypeEditorService(AppSettings settings)
    {
        _settings = settings;
    }

    public WorkflowTypeCreateResult CreateWorkflowType(string workflowTypeId, string? template)
    {
        if (!TryValidateWorkflowTypeId(workflowTypeId, out var normalizedWorkflowTypeId, out var validationError))
        {
            return WorkflowTypeCreateResult.Fail(validationError);
        }

        var resolvedTemplate = NormalizeTemplate(template);
        var workflowName = FormatDisplayName(normalizedWorkflowTypeId);

        var root = ResolveWorkflowTypesDirectory();
        Directory.CreateDirectory(root);

        var workflowDirectory = Path.Combine(root, normalizedWorkflowTypeId);
        List<string> createdFiles;
        lock (_writeLock)
        {
            if (Directory.Exists(workflowDirectory))
            {
                return WorkflowTypeCreateResult.Duplicate(normalizedWorkflowTypeId);
            }

            Directory.CreateDirectory(workflowDirectory);

            var stepFileName = "Step1.md";
            var stepPath = Path.Combine(workflowDirectory, stepFileName);
            var markdown = BuildMarkdownTemplate(resolvedTemplate, workflowName, 1);
            WriteAllTextAtomic(stepPath, markdown);

            createdFiles = new List<string> { stepFileName };

            if (string.Equals(resolvedTemplate, "ticketIteration", StringComparison.OrdinalIgnoreCase))
            {
                var metadataFileName = "Step1.json";
                var metadataPath = Path.Combine(workflowDirectory, metadataFileName);
                WriteAllTextAtomic(metadataPath, BuildTicketIterationMetadataTemplate());
                createdFiles.Add(metadataFileName);
            }
        }

        return WorkflowTypeCreateResult.Created(normalizedWorkflowTypeId, workflowName, createdFiles);
    }

    public WorkflowTypeStepListResult GetSteps(string workflowTypeId)
    {
        if (!TryValidateWorkflowTypeId(workflowTypeId, out var normalizedWorkflowTypeId, out var validationError))
        {
            return WorkflowTypeStepListResult.Validation(validationError);
        }

        var workflowDirectory = Path.Combine(ResolveWorkflowTypesDirectory(), normalizedWorkflowTypeId);
        if (!Directory.Exists(workflowDirectory))
        {
            return WorkflowTypeStepListResult.NotFound($"Workflow type '{normalizedWorkflowTypeId}' was not found.");
        }

        var steps = Directory.GetFiles(workflowDirectory, "*.md", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileName(path))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name =>
            {
                var metadataFileName = $"{Path.GetFileNameWithoutExtension(name)}.json";
                var metadataPath = Path.Combine(workflowDirectory, metadataFileName);
                return new WorkflowTypeStepSummary
                {
                    StepFileName = name,
                    HasMetadata = File.Exists(metadataPath),
                    MetadataFileName = metadataFileName
                };
            })
            .ToList();

        return WorkflowTypeStepListResult.Ok(normalizedWorkflowTypeId, steps);
    }

    public WorkflowTypeStepCreateResult AddStep(string workflowTypeId, string? stepFileName, string? template)
    {
        if (!TryValidateWorkflowTypeId(workflowTypeId, out var normalizedWorkflowTypeId, out var workflowValidationError))
        {
            return WorkflowTypeStepCreateResult.Validation(workflowValidationError);
        }

        var workflowDirectory = Path.Combine(ResolveWorkflowTypesDirectory(), normalizedWorkflowTypeId);
        if (!Directory.Exists(workflowDirectory))
        {
            return WorkflowTypeStepCreateResult.NotFound($"Workflow type '{normalizedWorkflowTypeId}' was not found.");
        }

        var resolvedTemplate = NormalizeTemplate(template);
        List<string> createdFiles;
        string createdStepFileName;
        string markdownContent;
        string? metadataContent = null;

        lock (_writeLock)
        {
            var existingSteps = Directory.GetFiles(workflowDirectory, "*.md", SearchOption.TopDirectoryOnly)
                .Select(path => Path.GetFileName(path))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            if (string.IsNullOrWhiteSpace(stepFileName))
            {
                createdStepFileName = SuggestStepFileName(existingSteps);
            }
            else if (!TryValidateStepFileName(stepFileName, out var normalizedStepFileName, out var stepValidationError))
            {
                return WorkflowTypeStepCreateResult.Validation(stepValidationError);
            }
            else
            {
                createdStepFileName = normalizedStepFileName;
            }

            var createdStepPath = Path.Combine(workflowDirectory, createdStepFileName);
            if (File.Exists(createdStepPath))
            {
                return WorkflowTypeStepCreateResult.Conflict($"Step '{createdStepFileName}' already exists.");
            }

            var stepNumber = existingSteps.Count + 1;
            markdownContent = BuildMarkdownTemplate(
                resolvedTemplate,
                FormatDisplayName(normalizedWorkflowTypeId),
                stepNumber);

            WriteAllTextAtomic(createdStepPath, markdownContent);
            createdFiles = new List<string> { createdStepFileName };

            if (string.Equals(resolvedTemplate, "ticketIteration", StringComparison.OrdinalIgnoreCase))
            {
                var metadataFileName = $"{Path.GetFileNameWithoutExtension(createdStepFileName)}.json";
                var metadataPath = Path.Combine(workflowDirectory, metadataFileName);
                metadataContent = BuildTicketIterationMetadataTemplate();
                WriteAllTextAtomic(metadataPath, metadataContent);
                createdFiles.Add(metadataFileName);
            }
        }

        return WorkflowTypeStepCreateResult.Created(
            normalizedWorkflowTypeId,
            createdStepFileName,
            markdownContent,
            metadataContent,
            createdFiles);
    }

    public WorkflowTypeStepContentResult GetStepContent(string workflowTypeId, string stepFileName)
    {
        if (!TryValidateWorkflowTypeId(workflowTypeId, out var normalizedWorkflowTypeId, out var workflowValidationError))
        {
            return WorkflowTypeStepContentResult.Validation(workflowValidationError);
        }

        if (!TryValidateStepFileName(stepFileName, out var normalizedStepFileName, out var stepValidationError))
        {
            return WorkflowTypeStepContentResult.Validation(stepValidationError);
        }

        var workflowDirectory = Path.Combine(ResolveWorkflowTypesDirectory(), normalizedWorkflowTypeId);
        if (!Directory.Exists(workflowDirectory))
        {
            return WorkflowTypeStepContentResult.NotFound($"Workflow type '{normalizedWorkflowTypeId}' was not found.");
        }

        var markdownPath = Path.Combine(workflowDirectory, normalizedStepFileName);
        if (!File.Exists(markdownPath))
        {
            return WorkflowTypeStepContentResult.NotFound($"Step '{normalizedStepFileName}' was not found.");
        }

        var markdownContent = File.ReadAllText(markdownPath);
        var metadataPath = Path.Combine(workflowDirectory, $"{Path.GetFileNameWithoutExtension(normalizedStepFileName)}.json");
        var hasMetadata = File.Exists(metadataPath);
        var metadataContent = hasMetadata ? File.ReadAllText(metadataPath) : null;

        return WorkflowTypeStepContentResult.Found(
            normalizedWorkflowTypeId,
            normalizedStepFileName,
            markdownContent,
            hasMetadata,
            metadataContent);
    }

    public WorkflowTypeSaveStepResult SaveStep(string workflowTypeId, string stepFileName, string markdownContent, string? metadataJsonContent)
    {
        if (!TryValidateWorkflowTypeId(workflowTypeId, out var normalizedWorkflowTypeId, out var workflowValidationError))
        {
            return WorkflowTypeSaveStepResult.Validation(workflowValidationError);
        }

        if (!TryValidateStepFileName(stepFileName, out var normalizedStepFileName, out var stepValidationError))
        {
            return WorkflowTypeSaveStepResult.Validation(stepValidationError);
        }

        if (markdownContent is null)
        {
            return WorkflowTypeSaveStepResult.Validation("Markdown content is required.");
        }

        string? normalizedMetadataContent = null;
        if (!string.IsNullOrWhiteSpace(metadataJsonContent))
        {
            try
            {
                using var document = JsonDocument.Parse(metadataJsonContent);
                normalizedMetadataContent = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
            }
            catch (JsonException ex)
            {
                return WorkflowTypeSaveStepResult.Validation($"Metadata JSON is invalid: {ex.Message}");
            }
        }

        var workflowDirectory = Path.Combine(ResolveWorkflowTypesDirectory(), normalizedWorkflowTypeId);
        if (!Directory.Exists(workflowDirectory))
        {
            return WorkflowTypeSaveStepResult.NotFound($"Workflow type '{normalizedWorkflowTypeId}' was not found.");
        }

        var markdownPath = Path.Combine(workflowDirectory, normalizedStepFileName);
        if (!File.Exists(markdownPath))
        {
            return WorkflowTypeSaveStepResult.NotFound($"Step '{normalizedStepFileName}' was not found.");
        }

        lock (_writeLock)
        {
            WriteAllTextAtomic(markdownPath, markdownContent);

            var metadataPath = Path.Combine(workflowDirectory, $"{Path.GetFileNameWithoutExtension(normalizedStepFileName)}.json");
            if (string.IsNullOrWhiteSpace(normalizedMetadataContent))
            {
                if (File.Exists(metadataPath))
                {
                    File.Delete(metadataPath);
                }
            }
            else
            {
                WriteAllTextAtomic(metadataPath, normalizedMetadataContent + Environment.NewLine);
            }
        }

        return WorkflowTypeSaveStepResult.Saved(normalizedWorkflowTypeId, normalizedStepFileName);
    }

    public WorkflowTypeDeleteWorkflowResult DeleteWorkflowType(string workflowTypeId)
    {
        if (!TryValidateWorkflowTypeId(workflowTypeId, out var normalizedWorkflowTypeId, out var validationError))
        {
            return WorkflowTypeDeleteWorkflowResult.Validation(validationError);
        }

        var workflowDirectory = Path.Combine(ResolveWorkflowTypesDirectory(), normalizedWorkflowTypeId);
        if (!Directory.Exists(workflowDirectory))
        {
            return WorkflowTypeDeleteWorkflowResult.NotFound($"Workflow type '{normalizedWorkflowTypeId}' was not found.");
        }

        lock (_writeLock)
        {
            if (!Directory.Exists(workflowDirectory))
            {
                return WorkflowTypeDeleteWorkflowResult.NotFound($"Workflow type '{normalizedWorkflowTypeId}' was not found.");
            }

            Directory.Delete(workflowDirectory, recursive: true);
        }

        return WorkflowTypeDeleteWorkflowResult.Deleted(normalizedWorkflowTypeId);
    }

    public WorkflowTypeDeleteStepResult DeleteStep(string workflowTypeId, string stepFileName)
    {
        if (!TryValidateWorkflowTypeId(workflowTypeId, out var normalizedWorkflowTypeId, out var workflowValidationError))
        {
            return WorkflowTypeDeleteStepResult.Validation(workflowValidationError);
        }

        if (!TryValidateStepFileName(stepFileName, out var normalizedStepFileName, out var stepValidationError))
        {
            return WorkflowTypeDeleteStepResult.Validation(stepValidationError);
        }

        var workflowDirectory = Path.Combine(ResolveWorkflowTypesDirectory(), normalizedWorkflowTypeId);
        if (!Directory.Exists(workflowDirectory))
        {
            return WorkflowTypeDeleteStepResult.NotFound($"Workflow type '{normalizedWorkflowTypeId}' was not found.");
        }

        var markdownPath = Path.Combine(workflowDirectory, normalizedStepFileName);
        if (!File.Exists(markdownPath))
        {
            return WorkflowTypeDeleteStepResult.NotFound($"Step '{normalizedStepFileName}' was not found.");
        }

        lock (_writeLock)
        {
            if (!File.Exists(markdownPath))
            {
                return WorkflowTypeDeleteStepResult.NotFound($"Step '{normalizedStepFileName}' was not found.");
            }

            File.Delete(markdownPath);

            var metadataPath = Path.Combine(workflowDirectory, $"{Path.GetFileNameWithoutExtension(normalizedStepFileName)}.json");
            if (File.Exists(metadataPath))
            {
                File.Delete(metadataPath);
            }
        }

        return WorkflowTypeDeleteStepResult.Deleted(normalizedWorkflowTypeId, normalizedStepFileName);
    }

    public bool TryValidateWorkflowTypeId(string? workflowTypeId, out string normalizedWorkflowTypeId, out string error)
    {
        normalizedWorkflowTypeId = (workflowTypeId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedWorkflowTypeId))
        {
            error = "workflowTypeId is required.";
            return false;
        }

        if (!WorkflowTypeIdPattern.IsMatch(normalizedWorkflowTypeId))
        {
            error = "workflowTypeId must contain only letters, numbers, dashes (-), or underscores (_).";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public bool TryValidateStepFileName(string? stepFileName, out string normalizedStepFileName, out string error)
    {
        normalizedStepFileName = (stepFileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedStepFileName))
        {
            error = "stepFileName is required.";
            return false;
        }

        if (!string.Equals(Path.GetFileName(normalizedStepFileName), normalizedStepFileName, StringComparison.Ordinal))
        {
            error = "stepFileName must be a file name only (no directory paths).";
            return false;
        }

        if (normalizedStepFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "stepFileName contains invalid file name characters.";
            return false;
        }

        if (!normalizedStepFileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            error = "stepFileName must end with '.md'.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string SuggestStepFileName(IReadOnlyCollection<string> existingStepFileNames)
    {
        var existing = new HashSet<string>(existingStepFileNames, StringComparer.OrdinalIgnoreCase);
        var nextIndex = existing.Count + 1;
        while (true)
        {
            var candidate = $"Step{nextIndex}.md";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }

            nextIndex++;
        }
    }

    private static string BuildMarkdownTemplate(string template, string workflowName, int stepNumber)
    {
        var heading = $"# {workflowName} - Step {stepNumber}";
        var resolvedTemplate = NormalizeTemplate(template);

        if (string.Equals(resolvedTemplate, "blank", StringComparison.OrdinalIgnoreCase))
        {
            return $"{heading}{Environment.NewLine}{Environment.NewLine}";
        }

        if (string.Equals(resolvedTemplate, "ticketIteration", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join(Environment.NewLine, new[]
            {
                heading,
                string.Empty,
                "ROLE:",
                "You are a senior software engineer.",
                string.Empty,
                "PHASE:",
                "TICKET EXECUTION",
                string.Empty,
                "TASK:",
                "- Read the next available ticket.",
                "- Implement the change in the project solution.",
                "- Verify acceptance criteria before finishing the step.",
                string.Empty
            });
        }

        return string.Join(Environment.NewLine, new[]
        {
            heading,
            string.Empty,
            "ROLE:",
            "You are a senior software engineer.",
            string.Empty,
            "TASK:",
            "- Implement this workflow step.",
            "- Keep changes minimal and focused.",
            "- Summarize what was completed.",
            string.Empty,
            "OUTPUT:",
            "- Files changed",
            "- Verification performed",
            string.Empty
        });
    }

    private static string BuildTicketIterationMetadataTemplate()
    {
        return JsonSerializer.Serialize(new
        {
            executionMode = "ticketIteration",
            ticketSource = "{{TICKETS_DIR}}/tickets.json",
            completedSource = "{{TICKETS_DIR}}/completed.json",
            maxRetriesPerTicket = 3
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        }) + Environment.NewLine;
    }

    private static string NormalizeTemplate(string? template)
    {
        var normalized = (template ?? "standard").Trim();
        if (string.Equals(normalized, "blank", StringComparison.OrdinalIgnoreCase))
        {
            return "blank";
        }

        if (string.Equals(normalized, "ticketIteration", StringComparison.OrdinalIgnoreCase))
        {
            return "ticketIteration";
        }

        return "standard";
    }

    private string ResolveWorkflowTypesDirectory() => ResolveWorkflowDirectory(
        _settings.WorkflowTypesDirectory,
        Directory.GetCurrentDirectory(),
        AppContext.BaseDirectory,
        "workflowtypes");

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

    private static string FormatDisplayName(string id)
    {
        var parts = id
            .Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..])
            .ToArray();

        return parts.Length > 0 ? string.Join(" ", parts) : id;
    }

    private static void WriteAllTextAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Target path directory cannot be resolved.");
        }

        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempPath, content ?? string.Empty);
        File.Move(tempPath, path, overwrite: true);
    }
}

public enum WorkflowTypeEditorErrorKind
{
    None,
    Validation,
    NotFound,
    Conflict
}

public class WorkflowTypeStepSummary
{
    public string StepFileName { get; set; } = "";
    public bool HasMetadata { get; set; }
    public string MetadataFileName { get; set; } = "";
}

public record WorkflowTypeCreateResult(
    bool Success,
    bool AlreadyExists,
    WorkflowTypeEditorErrorKind ErrorKind,
    string Message,
    string WorkflowTypeId,
    string WorkflowTypeName,
    List<string> CreatedFiles)
{
    public static WorkflowTypeCreateResult Created(string workflowTypeId, string workflowTypeName, List<string> createdFiles) =>
        new(true, false, WorkflowTypeEditorErrorKind.None, "Workflow type created.", workflowTypeId, workflowTypeName, createdFiles);

    public static WorkflowTypeCreateResult Duplicate(string workflowTypeId) =>
        new(false, true, WorkflowTypeEditorErrorKind.Conflict, $"Workflow type '{workflowTypeId}' already exists.", workflowTypeId, string.Empty, new List<string>());

    public static WorkflowTypeCreateResult Fail(string message) =>
        new(false, false, WorkflowTypeEditorErrorKind.Validation, message, string.Empty, string.Empty, new List<string>());
}

public record WorkflowTypeStepListResult(
    bool Success,
    WorkflowTypeEditorErrorKind ErrorKind,
    string Message,
    string WorkflowTypeId,
    List<WorkflowTypeStepSummary> Steps)
{
    public static WorkflowTypeStepListResult Ok(string workflowTypeId, List<WorkflowTypeStepSummary> steps) =>
        new(true, WorkflowTypeEditorErrorKind.None, string.Empty, workflowTypeId, steps);

    public static WorkflowTypeStepListResult NotFound(string message) =>
        new(false, WorkflowTypeEditorErrorKind.NotFound, message, string.Empty, new List<WorkflowTypeStepSummary>());

    public static WorkflowTypeStepListResult Validation(string message) =>
        new(false, WorkflowTypeEditorErrorKind.Validation, message, string.Empty, new List<WorkflowTypeStepSummary>());
}

public record WorkflowTypeStepCreateResult(
    bool Success,
    WorkflowTypeEditorErrorKind ErrorKind,
    string Message,
    string WorkflowTypeId,
    string StepFileName,
    string MarkdownContent,
    bool HasMetadata,
    string? MetadataJsonContent,
    List<string> CreatedFiles)
{
    public static WorkflowTypeStepCreateResult Created(
        string workflowTypeId,
        string stepFileName,
        string markdownContent,
        string? metadataJsonContent,
        List<string> createdFiles) =>
        new(
            true,
            WorkflowTypeEditorErrorKind.None,
            "Step created.",
            workflowTypeId,
            stepFileName,
            markdownContent,
            !string.IsNullOrWhiteSpace(metadataJsonContent),
            metadataJsonContent,
            createdFiles);

    public static WorkflowTypeStepCreateResult NotFound(string message) =>
        new(false, WorkflowTypeEditorErrorKind.NotFound, message, string.Empty, string.Empty, string.Empty, false, null, new List<string>());

    public static WorkflowTypeStepCreateResult Conflict(string message) =>
        new(false, WorkflowTypeEditorErrorKind.Conflict, message, string.Empty, string.Empty, string.Empty, false, null, new List<string>());

    public static WorkflowTypeStepCreateResult Validation(string message) =>
        new(false, WorkflowTypeEditorErrorKind.Validation, message, string.Empty, string.Empty, string.Empty, false, null, new List<string>());
}

public record WorkflowTypeStepContentResult(
    bool Success,
    WorkflowTypeEditorErrorKind ErrorKind,
    string Message,
    string WorkflowTypeId,
    string StepFileName,
    string MarkdownContent,
    bool HasMetadata,
    string? MetadataJsonContent)
{
    public static WorkflowTypeStepContentResult Found(
        string workflowTypeId,
        string stepFileName,
        string markdownContent,
        bool hasMetadata,
        string? metadataJsonContent) =>
        new(true, WorkflowTypeEditorErrorKind.None, string.Empty, workflowTypeId, stepFileName, markdownContent, hasMetadata, metadataJsonContent);

    public static WorkflowTypeStepContentResult NotFound(string message) =>
        new(false, WorkflowTypeEditorErrorKind.NotFound, message, string.Empty, string.Empty, string.Empty, false, null);

    public static WorkflowTypeStepContentResult Validation(string message) =>
        new(false, WorkflowTypeEditorErrorKind.Validation, message, string.Empty, string.Empty, string.Empty, false, null);
}

public record WorkflowTypeSaveStepResult(
    bool Success,
    WorkflowTypeEditorErrorKind ErrorKind,
    string Message,
    string WorkflowTypeId,
    string StepFileName)
{
    public static WorkflowTypeSaveStepResult Saved(string workflowTypeId, string stepFileName) =>
        new(true, WorkflowTypeEditorErrorKind.None, "Step saved.", workflowTypeId, stepFileName);

    public static WorkflowTypeSaveStepResult NotFound(string message) =>
        new(false, WorkflowTypeEditorErrorKind.NotFound, message, string.Empty, string.Empty);

    public static WorkflowTypeSaveStepResult Validation(string message) =>
        new(false, WorkflowTypeEditorErrorKind.Validation, message, string.Empty, string.Empty);

    public static WorkflowTypeSaveStepResult Conflict(string message) =>
        new(false, WorkflowTypeEditorErrorKind.Conflict, message, string.Empty, string.Empty);
}

public record WorkflowTypeDeleteWorkflowResult(
    bool Success,
    WorkflowTypeEditorErrorKind ErrorKind,
    string Message,
    string WorkflowTypeId)
{
    public static WorkflowTypeDeleteWorkflowResult Deleted(string workflowTypeId) =>
        new(true, WorkflowTypeEditorErrorKind.None, "Workflow type deleted.", workflowTypeId);

    public static WorkflowTypeDeleteWorkflowResult NotFound(string message) =>
        new(false, WorkflowTypeEditorErrorKind.NotFound, message, string.Empty);

    public static WorkflowTypeDeleteWorkflowResult Validation(string message) =>
        new(false, WorkflowTypeEditorErrorKind.Validation, message, string.Empty);
}

public record WorkflowTypeDeleteStepResult(
    bool Success,
    WorkflowTypeEditorErrorKind ErrorKind,
    string Message,
    string WorkflowTypeId,
    string StepFileName)
{
    public static WorkflowTypeDeleteStepResult Deleted(string workflowTypeId, string stepFileName) =>
        new(true, WorkflowTypeEditorErrorKind.None, "Step deleted.", workflowTypeId, stepFileName);

    public static WorkflowTypeDeleteStepResult NotFound(string message) =>
        new(false, WorkflowTypeEditorErrorKind.NotFound, message, string.Empty, string.Empty);

    public static WorkflowTypeDeleteStepResult Validation(string message) =>
        new(false, WorkflowTypeEditorErrorKind.Validation, message, string.Empty, string.Empty);
}
