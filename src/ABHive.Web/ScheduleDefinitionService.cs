using System.Text.Json;
using System.Text.RegularExpressions;

namespace ABHive.Web;

public class ScheduleDefinitionService
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _scheduleRoot;
    private readonly object _writeLock = new();

    public ScheduleDefinitionService(string scheduleRoot)
    {
        _scheduleRoot = Path.GetFullPath(scheduleRoot);
        Directory.CreateDirectory(_scheduleRoot);
    }

    public string ScheduleRoot => _scheduleRoot;

    public IReadOnlyList<ScheduleSummary> ListSchedules()
    {
        if (!Directory.Exists(_scheduleRoot))
        {
            return Array.Empty<ScheduleSummary>();
        }

        var summaries = new List<ScheduleSummary>();
        foreach (var directory in Directory.GetDirectories(_scheduleRoot))
        {
            var scheduleName = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(scheduleName) || !ScheduleConstants.NamePattern.IsMatch(scheduleName))
            {
                continue;
            }

            var lookup = GetScheduleLookup(scheduleName);
            if (lookup.Status == "ready" && lookup.Definition != null)
            {
                var definition = lookup.Definition;
                summaries.Add(new ScheduleSummary
                {
                    ScheduleName = definition.ScheduleName,
                    WorkflowTypeId = definition.WorkflowTypeId,
                    TriggerType = NormalizeTriggerType(definition.Trigger?.Type),
                    ScheduleType = NormalizeScheduleType(definition.ScheduleType),
                    UpdatedUtc = definition.UpdatedUtc,
                    Status = "ready",
                    Error = null
                });
                continue;
            }

            summaries.Add(new ScheduleSummary
            {
                ScheduleName = scheduleName,
                WorkflowTypeId = string.Empty,
                TriggerType = string.Empty,
                ScheduleType = string.Empty,
                UpdatedUtc = DateTime.UtcNow,
                Status = "corrupted",
                Error = lookup.Error ?? "Schedule definition is missing or invalid."
            });
        }

        return summaries
            .OrderBy(item => item.ScheduleName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public ScheduleDefinition? GetSchedule(string scheduleName)
    {
        return GetScheduleLookup(scheduleName).Definition;
    }

    public ScheduleLookupResult GetScheduleLookup(string scheduleName)
    {
        if (!TryValidateScheduleName(scheduleName, out var normalizedName, out var validationError))
        {
            return new ScheduleLookupResult
            {
                Status = "invalid_name",
                Definition = null,
                Error = validationError
            };
        }

        var scheduleDirectory = GetScheduleDirectory(normalizedName);
        if (!Directory.Exists(scheduleDirectory))
        {
            return new ScheduleLookupResult
            {
                Status = "not_found",
                Definition = null,
                Error = $"Schedule '{normalizedName}' was not found."
            };
        }

        var definitionPath = GetScheduleJsonPath(normalizedName);
        if (!File.Exists(definitionPath))
        {
            return new ScheduleLookupResult
            {
                Status = "corrupted",
                Definition = null,
                Error = $"Missing schedule definition file '{Path.GetFileName(definitionPath)}'."
            };
        }

        var load = TryLoadSchedule(normalizedName);
        if (load.Definition == null)
        {
            return new ScheduleLookupResult
            {
                Status = "corrupted",
                Definition = null,
                Error = load.Error ?? "Schedule definition could not be parsed."
            };
        }

        return new ScheduleLookupResult
        {
            Status = "ready",
            Definition = load.Definition,
            Error = null
        };
    }

    public bool SaveSchedule(ScheduleDefinition definition, out string error)
    {
        if (!ValidateDefinition(definition, out var normalizedDefinition, out error))
        {
            return false;
        }

        lock (_writeLock)
        {
            var scheduleDirectory = GetScheduleDirectory(normalizedDefinition.ScheduleName);
            Directory.CreateDirectory(scheduleDirectory);

            var jsonPath = GetScheduleJsonPath(normalizedDefinition.ScheduleName);
            if (File.Exists(jsonPath))
            {
                var existing = TryLoadSchedule(normalizedDefinition.ScheduleName);
                normalizedDefinition.CreatedUtc = existing.Definition?.CreatedUtc ?? normalizedDefinition.CreatedUtc;
            }

            normalizedDefinition.UpdatedUtc = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(normalizedDefinition, _jsonOptions);
            WriteAllTextAtomic(jsonPath, json);
            return true;
        }
    }

    public bool TryValidateScheduleName(string? scheduleName, out string normalizedScheduleName, out string error)
    {
        normalizedScheduleName = (scheduleName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedScheduleName))
        {
            error = "Schedule name is required.";
            return false;
        }

        if (!ScheduleConstants.NamePattern.IsMatch(normalizedScheduleName))
        {
            error = "Schedule name must contain only letters, numbers, dashes (-), or underscores (_).";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public string GetScheduleDirectory(string scheduleName)
    {
        return Path.Combine(_scheduleRoot, scheduleName);
    }

    public string GetScheduleJsonPath(string scheduleName)
    {
        return Path.Combine(GetScheduleDirectory(scheduleName), "schedule.json");
    }

    public static string BuildBenchmarkFolderName(string serverName, string modelName)
    {
        var raw = $"{(serverName ?? string.Empty).Trim()}-{(modelName ?? string.Empty).Trim()}";
        var sanitized = Regex.Replace(raw, @"[^A-Za-z0-9_-]", "_");
        sanitized = sanitized.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
        sanitized = Regex.Replace(sanitized, @"\s+", "_");
        sanitized = Regex.Replace(sanitized, @"_+", "_").Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "server-model" : sanitized;
    }

    private (ScheduleDefinition? Definition, string? Error) TryLoadSchedule(string scheduleName)
    {
        var jsonPath = GetScheduleJsonPath(scheduleName);
        if (!File.Exists(jsonPath))
        {
            return (null, $"Missing schedule definition file '{Path.GetFileName(jsonPath)}'.");
        }

        try
        {
            var json = File.ReadAllText(jsonPath);
            var definition = JsonSerializer.Deserialize<ScheduleDefinition>(json, _jsonOptions);
            if (definition == null)
            {
                return (null, "Schedule definition JSON is empty or invalid.");
            }

            if (!ValidateDefinition(definition, out var normalized, out var validationError))
            {
                return (null, validationError);
            }

            return (normalized, null);
        }
        catch (Exception ex)
        {
            return (null, $"Schedule definition JSON parse failed: {ex.Message}");
        }
    }

    private bool ValidateDefinition(
        ScheduleDefinition? definition,
        out ScheduleDefinition normalizedDefinition,
        out string error)
    {
        normalizedDefinition = new ScheduleDefinition();

        if (definition == null)
        {
            error = "Schedule definition is required.";
            return false;
        }

        if (!TryValidateScheduleName(definition.ScheduleName, out var normalizedName, out error))
        {
            return false;
        }

        var workflowTypeId = (definition.WorkflowTypeId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(workflowTypeId))
        {
            error = "workflowTypeId is required.";
            return false;
        }

        if (!ScheduleConstants.NamePattern.IsMatch(workflowTypeId))
        {
            error = "workflowTypeId must contain only letters, numbers, dashes (-), or underscores (_).";
            return false;
        }

        var selectedStepFileNames = (definition.SelectedStepFileNames ?? new List<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var stepFileName in selectedStepFileNames)
        {
            if (!IsSafeStepFileName(stepFileName))
            {
                error = "selectedStepFileNames must contain safe .md filenames only.";
                return false;
            }
        }

        var triggerType = NormalizeTriggerType(definition.Trigger?.Type);
        if (triggerType == "specificTime")
        {
            var specificTimeLocal = (definition.Trigger?.SpecificTimeLocal ?? string.Empty).Trim();
            if (!TimeSpan.TryParseExact(specificTimeLocal, "c", null, out _) &&
                !TimeSpan.TryParseExact(specificTimeLocal, @"hh\:mm\:ss", null, out _))
            {
                error = "specificTimeLocal must use HH:mm:ss.";
                return false;
            }
        }
        else if (triggerType == "frequency")
        {
            var hours = Math.Max(0, definition.Trigger?.IntervalHours ?? 0);
            var minutes = Math.Max(0, definition.Trigger?.IntervalMinutes ?? 0);
            var seconds = Math.Max(0, definition.Trigger?.IntervalSeconds ?? 0);
            if (hours == 0 && minutes == 0 && seconds == 0)
            {
                error = "Frequency interval must be greater than zero.";
                return false;
            }
        }

        var scheduleType = NormalizeScheduleType(definition.ScheduleType);
        var benchmarkSelections = (definition.BenchmarkSelections ?? new List<ScheduleSelection>())
            .Where(IsValidSelection)
            .Select(selection => new ScheduleSelection
            {
                ServerId = selection.ServerId.Trim(),
                ModelId = selection.ModelId.Trim()
            })
            .DistinctBy(selection => $"{selection.ServerId}|{selection.ModelId}")
            .ToList();

        ScheduleSelection? regularSelection = null;
        if (IsValidSelection(definition.RegularSelection))
        {
            regularSelection = new ScheduleSelection
            {
                ServerId = definition.RegularSelection!.ServerId.Trim(),
                ModelId = definition.RegularSelection.ModelId.Trim()
            };
        }

        if (scheduleType == "regular" && regularSelection == null)
        {
            error = "Regular schedules require one server/model selection.";
            return false;
        }

        if (scheduleType == "benchmark" && benchmarkSelections.Count == 0)
        {
            error = "Benchmark schedules require one or more server/model selections.";
            return false;
        }

        normalizedDefinition = new ScheduleDefinition
        {
            ScheduleName = normalizedName,
            WorkflowTypeId = workflowTypeId,
            SelectedStepFileNames = selectedStepFileNames,
            Trigger = new ScheduleTrigger
            {
                Type = triggerType,
                SpecificTimeLocal = (definition.Trigger?.SpecificTimeLocal ?? string.Empty).Trim(),
                IntervalHours = Math.Max(0, definition.Trigger?.IntervalHours ?? 0),
                IntervalMinutes = Math.Max(0, definition.Trigger?.IntervalMinutes ?? 0),
                IntervalSeconds = Math.Max(0, definition.Trigger?.IntervalSeconds ?? 0)
            },
            ScheduleType = scheduleType,
            RegularSelection = regularSelection,
            BenchmarkSelections = benchmarkSelections,
            CreatedUtc = definition.CreatedUtc == default ? DateTime.UtcNow : definition.CreatedUtc,
            UpdatedUtc = definition.UpdatedUtc == default ? DateTime.UtcNow : definition.UpdatedUtc
        };

        error = string.Empty;
        return true;
    }

    private static bool IsValidSelection(ScheduleSelection? selection)
    {
        if (selection == null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(selection.ServerId) &&
               !string.IsNullOrWhiteSpace(selection.ModelId);
    }

    private static bool IsSafeStepFileName(string stepFileName)
    {
        if (string.IsNullOrWhiteSpace(stepFileName))
        {
            return false;
        }

        var trimmed = stepFileName.Trim();
        if (!trimmed.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(trimmed, Path.GetFileName(trimmed), StringComparison.Ordinal))
        {
            return false;
        }

        return !trimmed.Contains("..", StringComparison.Ordinal);
    }

    public static string NormalizeTriggerType(string? triggerType)
    {
        var value = (triggerType ?? string.Empty).Trim();
        return value switch
        {
            "specificTime" => "specificTime",
            "frequency" => "frequency",
            _ => "immediate"
        };
    }

    public static string NormalizeScheduleType(string? scheduleType)
    {
        var value = (scheduleType ?? string.Empty).Trim();
        return string.Equals(value, "benchmark", StringComparison.OrdinalIgnoreCase)
            ? "benchmark"
            : "regular";
    }

    private static void WriteAllTextAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempPath, content);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(tempPath, path);
    }
}
