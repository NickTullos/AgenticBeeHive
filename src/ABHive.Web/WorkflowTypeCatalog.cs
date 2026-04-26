using ABHive;

namespace ABHive.Web;

public class WorkflowTypeCatalog
{
    private readonly AppSettings _settings;

    public WorkflowTypeCatalog(AppSettings settings)
    {
        _settings = settings;
    }

    public IReadOnlyList<WorkflowTypeDefinition> GetWorkflowTypes()
    {
        var items = new List<WorkflowTypeDefinition>();
        var root = ResolveWorkflowTypesDirectory();

        if (Directory.Exists(root))
        {
            items.AddRange(
                Directory.GetDirectories(root)
                    .Select(BuildDefinition)
                    .Where(item => item != null)
                    .Cast<WorkflowTypeDefinition>()
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase));
        }

        return items;
    }

    public WorkflowTypeDefinition? GetWorkflowType(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return GetWorkflowTypes().FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public WorkflowTypeDefinition? GetDefaultWorkflowType()
    {
        return GetWorkflowTypes().FirstOrDefault();
    }

    private WorkflowTypeDefinition? BuildDefinition(string directoryPath)
    {
        var stepCount = CountMarkdownFiles(directoryPath);
        if (stepCount == 0)
        {
            return null;
        }

        var id = Path.GetFileName(directoryPath);
        return new WorkflowTypeDefinition
        {
            Id = id,
            Name = FormatDisplayName(id),
            StepsDirectory = directoryPath,
            StepCount = stepCount
        };
    }

    private static int CountMarkdownFiles(string directoryPath)
    {
        return Directory.GetFiles(directoryPath, "*.md", SearchOption.TopDirectoryOnly).Length;
    }

    private string ResolveWorkflowTypesDirectory() => ResolveWorkflowDirectory(
        _settings.WorkflowTypesDirectory,
        Directory.GetCurrentDirectory(),
        AppContext.BaseDirectory,
        "workflowtypes");

    private static string FormatDisplayName(string id)
    {
        var parts = id
            .Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..])
            .ToArray();

        return parts.Length > 0 ? string.Join(" ", parts) : id;
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
}

public class WorkflowTypeDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string StepsDirectory { get; set; } = "";
    public int StepCount { get; set; }
}
