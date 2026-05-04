using System.Text.RegularExpressions;

namespace ABHive.Web;

public static class ScheduleConstants
{
    public static readonly Regex NamePattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);
    public static readonly string[] WorkspaceSubdirectories = { "goals", "files", "solution", "planning", "design", "tickets" };
}

public class ScheduleDefinition
{
    public string ScheduleName { get; set; } = "";
    public string WorkflowTypeId { get; set; } = "";
    public List<string> SelectedStepFileNames { get; set; } = new();
    public ScheduleTrigger Trigger { get; set; } = new();
    public string ScheduleType { get; set; } = "regular";
    public ScheduleSelection? RegularSelection { get; set; }
    public List<ScheduleSelection> BenchmarkSelections { get; set; } = new();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public class ScheduleTrigger
{
    public string Type { get; set; } = "immediate";
    public string SpecificTimeLocal { get; set; } = "";
    public int IntervalHours { get; set; }
    public int IntervalMinutes { get; set; }
    public int IntervalSeconds { get; set; }
}

public class ScheduleSelection
{
    public string ServerId { get; set; } = "";
    public string ModelId { get; set; } = "";
}

public class ScheduleSummary
{
    public string ScheduleName { get; set; } = "";
    public string WorkflowTypeId { get; set; } = "";
    public string TriggerType { get; set; } = "immediate";
    public string ScheduleType { get; set; } = "regular";
    public DateTime UpdatedUtc { get; set; }
    public string Status { get; set; } = "ready";
    public string? Error { get; set; }
}

public class ScheduleLookupResult
{
    public string Status { get; set; } = "not_found";
    public ScheduleDefinition? Definition { get; set; }
    public string? Error { get; set; }
}

public class ScheduleRuntimeState
{
    public bool IsActive { get; set; }
    public string ActiveScheduleName { get; set; } = "";
    public string? NextRunLocal { get; set; }
    public string TriggerType { get; set; } = "";
    public string ScheduleType { get; set; } = "";
}
