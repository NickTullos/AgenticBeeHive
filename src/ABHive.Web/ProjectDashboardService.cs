using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ABHive.Web;

public class ProjectDashboardService
{
    private static readonly string[] GoalExtensions = { ".md", ".txt", ".json" };
    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = false
    };

    private readonly ProjectWorkspaceService _workspaceService;

    public ProjectDashboardService(ProjectWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService;
    }

    public IReadOnlyList<ProjectDashboardDto> GetProjectDashboards()
    {
        var projects = _workspaceService.ListProjects();
        var items = new List<ProjectDashboardDto>();

        foreach (var project in projects)
        {
            var warnings = new List<string>();
            var goalSummary = ExtractGoalSummary(project, warnings);
            var planning = ExtractPlanning(project, warnings);
            var design = ExtractDesign(project, warnings);
            var tickets = ExtractTickets(project, warnings);
            var projectState = ExtractProjectState(project, warnings);

            var hasAnyData =
                !string.IsNullOrWhiteSpace(goalSummary) ||
                planning.Qa.Count > 0 ||
                planning.Requirements != null ||
                planning.Assumptions.Count > 0 ||
                !string.IsNullOrWhiteSpace(design.Overview) ||
                design.Components.Count > 0 ||
                design.DataModelsCount > 0 ||
                tickets.Counts.Total > 0 ||
                projectState.HasStateFile;

            var status = DeriveProjectStatus(hasAnyData, planning, design, tickets);

            items.Add(new ProjectDashboardDto
            {
                ProjectName = project.ProjectName,
                ProjectStatus = status,
                GoalSummary = string.IsNullOrWhiteSpace(goalSummary) ? "Pending" : goalSummary,
                Planning = planning,
                Design = design,
                Tickets = tickets,
                ProjectState = projectState,
                HasAnyData = hasAnyData,
                Warnings = warnings
            });
        }

        return items;
    }

    private static string DeriveProjectStatus(bool hasAnyData, ProjectPlanningDto planning, ProjectDesignDto design, ProjectTicketsDto tickets)
    {
        if (!hasAnyData)
        {
            return "Pending";
        }

        if (tickets.Counts.Open > 0 || tickets.Counts.Skipped > 0)
        {
            return "In Progress";
        }

        if (tickets.Counts.Open == 0 && tickets.Counts.Skipped == 0 && tickets.Counts.Completed > 0)
        {
            return "Completed";
        }

        var hasPlanningOrDesign = planning.Qa.Count > 0 ||
                                  planning.Requirements != null ||
                                  planning.Assumptions.Count > 0 ||
                                  !string.IsNullOrWhiteSpace(design.Overview) ||
                                  design.Components.Count > 0 ||
                                  design.DataModelsCount > 0;

        return hasPlanningOrDesign ? "Planned" : "Pending";
    }

    private static string? ExtractGoalSummary(ProjectWorkspace project, List<string> warnings)
    {
        if (!Directory.Exists(project.GoalsDirectory))
        {
            return null;
        }

        var files = Directory.GetFiles(project.GoalsDirectory)
            .Where(path => GoalExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var file in files)
        {
            try
            {
                var text = File.ReadAllText(file);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    var node = JsonNode.Parse(text);
                    if (node is JsonObject obj)
                    {
                        var explicitSummary = GetStringValue(obj, "summary") ??
                                              GetStringValue(obj, "goal") ??
                                              GetStringValue(obj, "primary_goal");
                        if (!string.IsNullOrWhiteSpace(explicitSummary))
                        {
                            return explicitSummary.Trim();
                        }

                        return CompactJson(obj);
                    }

                    return CompactJson(node);
                }

                return ExtractHeadingAndParagraph(text);
            }
            catch (Exception ex)
            {
                warnings.Add($"Goal parse failed ({Path.GetFileName(file)}): {ex.Message}");
            }
        }

        return null;
    }

    private static ProjectPlanningDto ExtractPlanning(ProjectWorkspace project, List<string> warnings)
    {
        var result = new ProjectPlanningDto();
        var planningJsonPath = Path.Combine(project.PlanningDirectory, "planning.json");

        if (!File.Exists(planningJsonPath))
        {
            return result;
        }

        try
        {
            var text = File.ReadAllText(planningJsonPath);
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            var node = JsonNode.Parse(text);
            if (node is not JsonObject obj)
            {
                warnings.Add("Planning parse warning: planning.json is not a JSON object.");
                return result;
            }

            if (obj["requirements"] is JsonNode requirementsNode)
            {
                result.Requirements = requirementsNode.DeepClone();
            }

            if (obj["assumptions"] is JsonArray assumptionsArray)
            {
                result.Assumptions = assumptionsArray
                    .Select(item => item?.GetValue<string>())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item!.Trim())
                    .ToList();
            }

            var qa = TryParseQaArray(obj);
            if (qa.Count > 0)
            {
                result.Qa = qa;
                return result;
            }

            qa = TryParseQuestionsAndAnswers(obj);
            if (qa.Count > 0)
            {
                result.Qa = qa;
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Planning parse failed (planning.json): {ex.Message}");
        }

        return result;
    }

    private static List<ProjectQaItemDto> TryParseQaArray(JsonObject obj)
    {
        var items = new List<ProjectQaItemDto>();
        if (obj["qa"] is not JsonArray qaArray)
        {
            return items;
        }

        foreach (var row in qaArray)
        {
            if (row is not JsonObject qaObj)
            {
                continue;
            }

            var question = GetStringValue(qaObj, "question");
            if (string.IsNullOrWhiteSpace(question))
            {
                continue;
            }

            var answer = GetStringValue(qaObj, "answer");
            items.Add(new ProjectQaItemDto
            {
                Question = question.Trim(),
                Answer = string.IsNullOrWhiteSpace(answer) ? "Pending" : answer.Trim()
            });
        }

        return items;
    }

    private static List<ProjectQaItemDto> TryParseQuestionsAndAnswers(JsonObject obj)
    {
        var questions = new List<string>();
        var answers = new List<string?>();

        if (obj["questions"] is JsonArray questionsArray)
        {
            questions = questionsArray
                .Select(item => item?.GetValue<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!.Trim())
                .ToList();
        }

        if (questions.Count == 0)
        {
            return new List<ProjectQaItemDto>();
        }

        if (obj["answers"] is JsonArray answersArray)
        {
            answers = answersArray
                .Select(item => item?.GetValue<string>())
                .ToList();
        }
        else if (obj["answers"] is JsonObject answersObj)
        {
            answers = questions.Select(question =>
            {
                if (answersObj[question] != null)
                {
                    return answersObj[question]!.ToString();
                }

                return (string?)null;
            }).ToList();
        }
        else
        {
            answers = Enumerable.Repeat<string?>(null, questions.Count).ToList();
        }

        var items = new List<ProjectQaItemDto>();
        for (var i = 0; i < questions.Count; i++)
        {
            var answer = i < answers.Count ? answers[i] : null;
            items.Add(new ProjectQaItemDto
            {
                Question = questions[i],
                Answer = string.IsNullOrWhiteSpace(answer) ? "Pending" : answer.Trim()
            });
        }

        return items;
    }

    private static ProjectDesignDto ExtractDesign(ProjectWorkspace project, List<string> warnings)
    {
        var result = new ProjectDesignDto();
        var architectureJsonPath = Path.Combine(project.DesignDirectory, "architecture-design.json");

        if (File.Exists(architectureJsonPath))
        {
            try
            {
                var text = File.ReadAllText(architectureJsonPath);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var node = JsonNode.Parse(text);
                    if (node is JsonObject obj)
                    {
                        result.Overview = BuildDesignOverview(obj);
                        result.Components = ParseStringArray(obj["components"]);
                        result.DataModelsCount = (obj["data_models"] as JsonArray)?.Count ?? 0;
                        result.ProjectStructure = obj["project_structure"]?.DeepClone();
                    }
                    else
                    {
                        warnings.Add("Design parse warning: architecture-design.json is not a JSON object.");
                    }
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Design parse failed (architecture-design.json): {ex.Message}");
            }
        }

        if (!string.IsNullOrWhiteSpace(result.Overview))
        {
            return result;
        }

        if (!Directory.Exists(project.DesignDirectory))
        {
            return result;
        }

        var fallbackFile = Directory.GetFiles(project.DesignDirectory, "architecture*.md")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (fallbackFile == null)
        {
            return result;
        }

        try
        {
            var markdown = File.ReadAllText(fallbackFile);
            if (!string.IsNullOrWhiteSpace(markdown))
            {
                result.Overview = ExtractHeadingAndParagraph(markdown);
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Design parse failed ({Path.GetFileName(fallbackFile)}): {ex.Message}");
        }

        return result;
    }

    private static string BuildDesignOverview(JsonObject obj)
    {
        var architectureNode = obj["architecture"];
        if (architectureNode == null)
        {
            return "";
        }

        if (architectureNode is JsonValue value && value.TryGetValue<string>(out var asString))
        {
            return string.IsNullOrWhiteSpace(asString) ? "" : asString.Trim();
        }

        return CompactJson(architectureNode);
    }

    private static ProjectTicketsDto ExtractTickets(ProjectWorkspace project, List<string> warnings)
    {
        var open = new List<ProjectTicketDto>();
        var skipped = new List<ProjectTicketDto>();
        var completed = new List<ProjectTicketDto>();
        var ticketsJsonPath = Path.Combine(project.TicketsDirectory, "tickets.json");
        var completedJsonPath = Path.Combine(project.TicketsDirectory, "completed.json");
        var skippedJsonPath = Path.Combine(project.TicketsDirectory, "skipped.json");

        if (File.Exists(ticketsJsonPath))
        {
            var tickets = ParseTicketJsonFile(ticketsJsonPath, "Open", warnings);
            var completedIds = LoadCompletedIds(completedJsonPath, warnings);
            var skippedIds = LoadSkippedIds(skippedJsonPath, warnings);
            skippedIds.UnionWith(LoadLegacySkippedIdsFromCompleted(completedJsonPath, warnings));

            foreach (var ticket in tickets)
            {
                var id = ticket.Id;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (completedIds.Contains(id))
                {
                    ticket.Status = "Completed";
                    completed.Add(ticket);
                    continue;
                }

                if (skippedIds.Contains(id))
                {
                    ticket.Status = "Skipped";
                    skipped.Add(ticket);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(ticket.Status))
                {
                    ticket.Status = "Open";
                }
                open.Add(ticket);
            }
        }
        else if (Directory.Exists(project.TicketsDirectory))
        {
            // Fallback: markdown tickets (legacy). No completion/skipped tracking in this mode.
            var markdownTickets = Directory.GetFiles(project.TicketsDirectory, "*.md")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var index = 1;
            foreach (var ticketFile in markdownTickets)
            {
                try
                {
                    var markdown = File.ReadAllText(ticketFile);
                    if (string.IsNullOrWhiteSpace(markdown))
                    {
                        continue;
                    }

                    open.Add(ParseMarkdownTicket(ticketFile, markdown, index++));
                }
                catch (Exception ex)
                {
                    warnings.Add($"Ticket parse failed ({Path.GetFileName(ticketFile)}): {ex.Message}");
                }
            }
        }

        return new ProjectTicketsDto
        {
            Open = open,
            Skipped = skipped,
            Completed = completed,
            Counts = new ProjectTicketCountsDto
            {
                Open = open.Count,
                Skipped = skipped.Count,
                Completed = completed.Count,
                Total = open.Count + skipped.Count + completed.Count
            }
        };
    }

    private static HashSet<string> LoadCompletedIds(string path, List<string> warnings)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var node = JsonNode.Parse(text);
            if (node is not JsonArray array)
            {
                warnings.Add($"Ticket parse warning ({Path.GetFileName(path)}): expected JSON array.");
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in array)
            {
                if (row is not JsonObject obj)
                {
                    continue;
                }

                if (obj["skipped"] is JsonValue skippedNode &&
                    skippedNode.TryGetValue<bool>(out var skipped) &&
                    skipped)
                {
                    continue;
                }

                var id = GetStringValue(obj, "ticket_id")?.Trim();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }
        catch (Exception ex)
        {
            warnings.Add($"Ticket parse failed ({Path.GetFileName(path)}): {ex.Message}");
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static HashSet<string> LoadLegacySkippedIdsFromCompleted(string path, List<string> warnings)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var node = JsonNode.Parse(text);
            if (node is not JsonArray array)
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in array)
            {
                if (row is not JsonObject obj)
                {
                    continue;
                }

                if (obj["skipped"] is not JsonValue skippedNode ||
                    !skippedNode.TryGetValue<bool>(out var skipped) ||
                    !skipped)
                {
                    continue;
                }

                var id = GetStringValue(obj, "ticket_id")?.Trim();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }
        catch (Exception ex)
        {
            warnings.Add($"Ticket parse failed ({Path.GetFileName(path)}): {ex.Message}");
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static HashSet<string> LoadSkippedIds(string path, List<string> warnings)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var node = JsonNode.Parse(text);
            if (node is not JsonArray array)
            {
                warnings.Add($"Ticket parse warning ({Path.GetFileName(path)}): expected JSON array.");
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in array)
            {
                if (row is not JsonObject obj)
                {
                    continue;
                }

                var id = GetStringValue(obj, "ticket_id")?.Trim();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }
        catch (Exception ex)
        {
            warnings.Add($"Ticket parse failed ({Path.GetFileName(path)}): {ex.Message}");
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static ProjectStateDto ExtractProjectState(ProjectWorkspace project, List<string> warnings)
    {
        var result = new ProjectStateDto();
        var projectRoot = Path.GetDirectoryName(project.ProjectDirectory);
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return result;
        }

        var stateFilePath = project.ProjectStateFilePath;
        if (!File.Exists(stateFilePath))
        {
            return result;
        }

        result.HasStateFile = true;

        try
        {
            var text = File.ReadAllText(stateFilePath);
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            var node = JsonNode.Parse(text);
            if (node is not JsonObject rootObj)
            {
                warnings.Add($"Project state parse warning ({Path.GetFileName(stateFilePath)}): expected JSON object.");
                return result;
            }

            if (rootObj["snapshot"] is not JsonObject snapshot)
            {
                return result;
            }

            result.WorkflowTypeName = GetStringValue(snapshot, "selectedWorkflowTypeName") ?? "Pending";
            result.Status = GetStringValue(snapshot, "status") ?? "Ready";
            result.CurrentStep = snapshot["currentStep"]?.GetValue<int>() ?? 0;
            result.TotalSteps = snapshot["totalSteps"]?.GetValue<int>() ?? 0;
            result.CurrentStepName = GetStringValue(snapshot, "currentStepName") ?? "Not started";
            result.CanResume = snapshot["canResume"]?.GetValue<bool>() ?? false;
            result.WorkflowRunning = snapshot["workflowRunning"]?.GetValue<bool>() ?? false;
            result.AwaitingUserInput = snapshot["awaitingUserInput"]?.GetValue<bool>() ?? false;
            result.Busy = snapshot["busy"]?.GetValue<bool>() ?? false;

            if (snapshot["lastUpdatedUtc"] is JsonValue lastUpdatedNode &&
                lastUpdatedNode.TryGetValue<string>(out var lastUpdatedText) &&
                DateTime.TryParse(lastUpdatedText, out var lastUpdatedUtc))
            {
                result.LastUpdatedUtc = lastUpdatedUtc;
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Project state parse failed ({Path.GetFileName(stateFilePath)}): {ex.Message}");
        }

        return result;
    }

    private static List<ProjectTicketDto> ParseTicketJsonFile(string path, string defaultStatus, List<string> warnings)
    {
        var items = new List<ProjectTicketDto>();
        try
        {
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return items;
            }

            var node = JsonNode.Parse(text);
            if (node is not JsonArray array)
            {
                warnings.Add($"Ticket parse warning ({Path.GetFileName(path)}): expected JSON array.");
                return items;
            }

            var index = 1;
            foreach (var row in array)
            {
                if (row is not JsonObject obj)
                {
                    index++;
                    continue;
                }

                // Back-compat: earlier builds stored skipped tickets in completed.json with {"skipped": true}.
                // Those should not show up as completed tickets in the project dashboard.
                if (string.Equals(defaultStatus, "Completed", StringComparison.OrdinalIgnoreCase) &&
                    obj["skipped"] is JsonValue skippedNode &&
                    skippedNode.TryGetValue<bool>(out var skipped) &&
                    skipped)
                {
                    index++;
                    continue;
                }

                var status = GetStringValue(obj, "status");
                items.Add(new ProjectTicketDto
                {
                    Id = GetStringValue(obj, "ticket_id") ?? $"ticket-{index}",
                    Title = GetStringValue(obj, "title") ?? "(untitled)",
                    Description = GetStringValue(obj, "description"),
                    Priority = GetStringValue(obj, "priority"),
                    Type = GetStringValue(obj, "type"),
                    Status = string.IsNullOrWhiteSpace(status) ? defaultStatus : status.Trim(),
                    Dependencies = ParseStringArray(obj["dependencies"]),
                    DefinitionOfDone = ParseStringArray(obj["definition_of_done"])
                });

                index++;
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Ticket parse failed ({Path.GetFileName(path)}): {ex.Message}");
        }

        return items;
    }

    private static ProjectTicketDto ParseMarkdownTicket(string filePath, string markdown, int index)
    {
        var titleMatch = Regex.Match(markdown, @"^\s*#\s*(.+)$", RegexOptions.Multiline);
        var title = titleMatch.Success ? titleMatch.Groups[1].Value.Trim() : Path.GetFileNameWithoutExtension(filePath);

        var statusMatch = Regex.Match(markdown, @"^\s*##\s*Status\s*$([\s\S]*?)(^\s*##\s+|\z)", RegexOptions.Multiline);
        var status = "Open";
        if (statusMatch.Success)
        {
            var statusText = statusMatch.Groups[1].Value.Trim();
            if (statusText.Contains("complete", StringComparison.OrdinalIgnoreCase))
            {
                status = "Completed";
            }
            else if (statusText.Contains("progress", StringComparison.OrdinalIgnoreCase))
            {
                status = "In Progress";
            }
            else if (statusText.Contains("block", StringComparison.OrdinalIgnoreCase))
            {
                status = "Blocked";
            }
        }

        return new ProjectTicketDto
        {
            Id = $"ticket-{index}",
            Title = title,
            Status = status
        };
    }

    private static string ExtractHeadingAndParagraph(string text)
    {
        var lines = text.Split('\n')
            .Select(line => line.Trim())
            .ToList();

        var heading = lines.FirstOrDefault(line => line.StartsWith("#", StringComparison.Ordinal)) ?? "";
        heading = heading.TrimStart('#').Trim();

        var paragraphLines = lines
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#", StringComparison.Ordinal))
            .Take(3)
            .ToList();

        var paragraph = string.Join(' ', paragraphLines).Trim();
        if (!string.IsNullOrWhiteSpace(heading) && !string.IsNullOrWhiteSpace(paragraph))
        {
            return $"{heading}: {paragraph}";
        }

        if (!string.IsNullOrWhiteSpace(heading))
        {
            return heading;
        }

        return string.IsNullOrWhiteSpace(paragraph) ? "Pending" : paragraph;
    }

    private static string CompactJson(JsonNode? node)
    {
        if (node == null)
        {
            return "Pending";
        }

        var text = node.ToJsonString(JsonWriteOptions);
        return text.Length > 280 ? $"{text[..280]}..." : text;
    }

    private static List<string> ParseStringArray(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return new List<string>();
        }

        return array
            .Select(item => item?.GetValue<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!.Trim())
            .ToList();
    }

    private static string? GetStringValue(JsonObject obj, string propertyName)
    {
        if (!obj.TryGetPropertyValue(propertyName, out var node) || node == null)
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var asString))
        {
            return asString;
        }

        return node.ToJsonString(JsonWriteOptions);
    }
}

public class ProjectDashboardDto
{
    public string ProjectName { get; set; } = "";
    public string ProjectStatus { get; set; } = "Pending";
    public string GoalSummary { get; set; } = "Pending";
    public ProjectPlanningDto Planning { get; set; } = new();
    public ProjectDesignDto Design { get; set; } = new();
    public ProjectTicketsDto Tickets { get; set; } = new();
    public ProjectStateDto ProjectState { get; set; } = new();
    public bool HasAnyData { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public class ProjectStateDto
{
    public bool HasStateFile { get; set; }
    public string WorkflowTypeName { get; set; } = "Pending";
    public string Status { get; set; } = "Ready";
    public int CurrentStep { get; set; }
    public int TotalSteps { get; set; }
    public string CurrentStepName { get; set; } = "Not started";
    public bool CanResume { get; set; }
    public bool WorkflowRunning { get; set; }
    public bool AwaitingUserInput { get; set; }
    public bool Busy { get; set; }
    public DateTime? LastUpdatedUtc { get; set; }
}

public class ProjectPlanningDto
{
    public List<ProjectQaItemDto> Qa { get; set; } = new();
    public JsonNode? Requirements { get; set; }
    public List<string> Assumptions { get; set; } = new();
}

public class ProjectQaItemDto
{
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "Pending";
}

public class ProjectDesignDto
{
    public string Overview { get; set; } = "";
    public List<string> Components { get; set; } = new();
    public int DataModelsCount { get; set; }
    public JsonNode? ProjectStructure { get; set; }
}

public class ProjectTicketsDto
{
    public List<ProjectTicketDto> Open { get; set; } = new();
    public List<ProjectTicketDto> Skipped { get; set; } = new();
    public List<ProjectTicketDto> Completed { get; set; } = new();
    public ProjectTicketCountsDto Counts { get; set; } = new();
}

public class ProjectTicketCountsDto
{
    public int Open { get; set; }
    public int Skipped { get; set; }
    public int Completed { get; set; }
    public int Total { get; set; }
}

public class ProjectTicketDto
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? Priority { get; set; }
    public string? Type { get; set; }
    public string Status { get; set; } = "Open";
    public List<string> Dependencies { get; set; } = new();
    public List<string> DefinitionOfDone { get; set; } = new();
}
