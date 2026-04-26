namespace ABHive;

public static class WorkspaceContext
{
    public const string ScopeMessageMarker = "[WORKSPACE_SCOPE]";

    public const string TicketDefinitionJson = @"[
  {
    ""ticket_id"": ""string"",
    ""title"": ""string"",
    ""description"": ""string"",
    ""type"": ""feature | bug | refactor"",
    ""priority"": ""low | medium | high"",
    ""files_to_modify"": [""string""],
    ""inputs"": {},
    ""outputs"": {},
    ""acceptance_criteria"": [""string""],
    ""dependencies"": [""ticket_id""],
    ""test_requirements"": {
      ""unit_tests"": ""boolean"",
      ""integration_tests"": ""boolean""
    },
    ""definition_of_done"": [""string""]
  }
]";

    public static string ResolveProjectRoot(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.SelectedProjectDirectory))
        {
            var fullPath = Path.GetFullPath(settings.SelectedProjectDirectory);

            // Recover gracefully from older state that may have persisted the projects root
            // instead of a specific project directory.
            if (!LooksLikeProjectDirectory(fullPath) &&
                !string.IsNullOrWhiteSpace(settings.SelectedProjectName))
            {
                var candidate = Path.Combine(fullPath, settings.SelectedProjectName);
                if (LooksLikeProjectDirectory(candidate) || Directory.Exists(candidate))
                {
                    fullPath = candidate;
                }
            }

            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        var projectsRoot = !string.IsNullOrWhiteSpace(settings.ProjectRootDirectory)
            ? Path.GetFullPath(settings.ProjectRootDirectory)
            : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "projects"));

        Directory.CreateDirectory(projectsRoot);
        var fallbackProjectDirectory = Path.Combine(projectsRoot, "default");
        Directory.CreateDirectory(fallbackProjectDirectory);
        return fallbackProjectDirectory;
    }

    private static bool LooksLikeProjectDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return false;
        }

        return Directory.Exists(Path.Combine(directoryPath, "goals")) ||
               Directory.Exists(Path.Combine(directoryPath, "files")) ||
               Directory.Exists(Path.Combine(directoryPath, "solution")) ||
               Directory.Exists(Path.Combine(directoryPath, "planning")) ||
               Directory.Exists(Path.Combine(directoryPath, "design")) ||
               Directory.Exists(Path.Combine(directoryPath, "tickets"));
    }

    public static string ResolveGoalsDirectory(AppSettings settings)
    {
        return Path.Combine(ResolveProjectRoot(settings), "goals");
    }

    public static string ResolveFilesDirectory(AppSettings settings)
    {
        return Path.Combine(ResolveProjectRoot(settings), "files");
    }

    public static string ResolveSolutionDirectory(AppSettings settings)
    {
        return Path.Combine(ResolveProjectRoot(settings), "solution");
    }

    public static string ResolvePlanningDirectory(AppSettings settings)
    {
        return Path.Combine(ResolveProjectRoot(settings), "planning");
    }

    public static string ResolveDesignDirectory(AppSettings settings)
    {
        return Path.Combine(ResolveProjectRoot(settings), "design");
    }

    public static string ResolveTicketsDirectory(AppSettings settings)
    {
        return Path.Combine(ResolveProjectRoot(settings), "tickets");
    }

    public static string BuildScopeMessage(AppSettings settings)
    {
        return string.Join('\n',
            ScopeMessageMarker,
            "Project workspace context for this run:",
            $"Project root: {ResolveProjectRoot(settings)}",
            $"Goals directory: {ResolveGoalsDirectory(settings)}",
            $"Files directory: {ResolveFilesDirectory(settings)}",
            $"Solution directory: {ResolveSolutionDirectory(settings)}",
            $"Planning directory: {ResolvePlanningDirectory(settings)}",
            $"Design directory: {ResolveDesignDirectory(settings)}",
            $"Tickets directory: {ResolveTicketsDirectory(settings)}",
            "Do not read or write outside the project root.");
    }

    public static string ApplyPathTokens(string content, AppSettings settings)
    {
        return content
            .Replace("{{PROJECT_ROOT}}", ResolveProjectRoot(settings), StringComparison.Ordinal)
            .Replace("{{GOALS_DIR}}", ResolveGoalsDirectory(settings), StringComparison.Ordinal)
            .Replace("{{FILES_DIR}}", ResolveFilesDirectory(settings), StringComparison.Ordinal)
            .Replace("{{SOLUTION_DIR}}", ResolveSolutionDirectory(settings), StringComparison.Ordinal)
            .Replace("{{PLANNING_DIR}}", ResolvePlanningDirectory(settings), StringComparison.Ordinal)
            .Replace("{{DESIGN_DIR}}", ResolveDesignDirectory(settings), StringComparison.Ordinal)
            .Replace("{{TICKETS_DIR}}", ResolveTicketsDirectory(settings), StringComparison.Ordinal)
            .Replace("{{TICKET_DEFINITION}}", TicketDefinitionJson, StringComparison.Ordinal);
    }
}
