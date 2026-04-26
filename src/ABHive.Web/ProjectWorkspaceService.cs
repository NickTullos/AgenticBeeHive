using System.Text.RegularExpressions;

namespace ABHive.Web;

public class ProjectWorkspaceService
{
    private static readonly Regex ProjectNamePattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);
    private static readonly string[] ProjectSubdirectories = { "goals", "files", "solution", "planning", "design", "tickets" };
    private readonly object _rootLock = new();

    private string _projectsRoot;

    public ProjectWorkspaceService(string projectsRoot)
    {
        _projectsRoot = Path.GetFullPath(projectsRoot);
        Directory.CreateDirectory(_projectsRoot);
    }

    public string ProjectsRoot
    {
        get
        {
            lock (_rootLock)
            {
                return _projectsRoot;
            }
        }
    }

    public void UpdateProjectsRoot(string projectsRoot)
    {
        var normalized = Path.GetFullPath(projectsRoot);
        Directory.CreateDirectory(normalized);

        lock (_rootLock)
        {
            _projectsRoot = normalized;
        }
    }

    public IReadOnlyList<ProjectWorkspace> ListProjects()
    {
        var projectsRoot = ProjectsRoot;
        if (!Directory.Exists(projectsRoot))
        {
            return Array.Empty<ProjectWorkspace>();
        }

        return Directory.GetDirectories(projectsRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Where(name => ProjectNamePattern.IsMatch(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => ToWorkspace(projectsRoot, name))
            .ToList();
    }

    public bool TryResolveWorkspace(string projectName, out ProjectWorkspace workspace, out string error)
    {
        workspace = default!;
        error = string.Empty;

        if (!TryValidateProjectName(projectName, out error))
        {
            return false;
        }

        var candidate = ToWorkspace(ProjectsRoot, projectName);
        if (!Directory.Exists(candidate.ProjectDirectory))
        {
            error = $"Project '{projectName}' does not exist.";
            return false;
        }

        EnsureProjectDirectories(candidate);
        workspace = candidate;
        return true;
    }

    public ProjectCreationResult CreateProject(string projectName)
    {
        if (!TryValidateProjectName(projectName, out var validationError))
        {
            return ProjectCreationResult.Fail(validationError);
        }

        var workspace = ToWorkspace(ProjectsRoot, projectName);
        if (Directory.Exists(workspace.ProjectDirectory))
        {
            return ProjectCreationResult.Exists(workspace);
        }

        Directory.CreateDirectory(workspace.ProjectDirectory);
        EnsureProjectDirectories(workspace);

        return ProjectCreationResult.Created(workspace);
    }

    public bool TryValidateProjectName(string? projectName, out string error)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            error = "Project name is required.";
            return false;
        }

        if (!ProjectNamePattern.IsMatch(projectName))
        {
            error = "Project name must contain only letters, numbers, dashes (-), or underscores (_).";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static ProjectWorkspace ToWorkspace(string projectsRoot, string projectName)
    {
        var projectDirectory = Path.Combine(projectsRoot, projectName);
        return new ProjectWorkspace(
            projectName,
            projectDirectory,
            Path.Combine(projectDirectory, ProjectSubdirectories[0]),
            Path.Combine(projectDirectory, ProjectSubdirectories[1]),
            Path.Combine(projectDirectory, ProjectSubdirectories[2]),
            Path.Combine(projectDirectory, ProjectSubdirectories[3]),
            Path.Combine(projectDirectory, ProjectSubdirectories[4]),
            Path.Combine(projectDirectory, ProjectSubdirectories[5]),
            Path.Combine(projectDirectory, $"{projectName}.json"));
    }

    private static void EnsureProjectDirectories(ProjectWorkspace workspace)
    {
        Directory.CreateDirectory(workspace.GoalsDirectory);
        Directory.CreateDirectory(workspace.FilesDirectory);
        Directory.CreateDirectory(workspace.SolutionDirectory);
        Directory.CreateDirectory(workspace.PlanningDirectory);
        Directory.CreateDirectory(workspace.DesignDirectory);
        Directory.CreateDirectory(workspace.TicketsDirectory);
    }
}

public record ProjectWorkspace(
    string ProjectName,
    string ProjectDirectory,
    string GoalsDirectory,
    string FilesDirectory,
    string SolutionDirectory,
    string PlanningDirectory,
    string DesignDirectory,
    string TicketsDirectory,
    string ProjectStateFilePath);

public record ProjectCreationResult(
    bool Success,
    bool CreatedNew,
    bool AlreadyExists,
    string Error,
    ProjectWorkspace? Workspace)
{
    public static ProjectCreationResult Created(ProjectWorkspace workspace) =>
        new(true, true, false, string.Empty, workspace);

    public static ProjectCreationResult Exists(ProjectWorkspace workspace) =>
        new(true, false, true, string.Empty, workspace);

    public static ProjectCreationResult Fail(string error) =>
        new(false, false, false, error, null);
}
