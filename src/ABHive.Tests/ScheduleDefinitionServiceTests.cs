using ABHive.Web;

namespace ABHive.Tests;

public sealed class ScheduleDefinitionServiceTests : IDisposable
{
    private readonly string _root;
    private readonly ScheduleDefinitionService _service;

    public ScheduleDefinitionServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "abhive-schedules-tests", Guid.NewGuid().ToString("N"));
        _service = new ScheduleDefinitionService(_root);
    }

    [Fact]
    public void Constructor_CreatesScheduleRootDirectory()
    {
        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public void SaveSchedule_RoundTrip_Works()
    {
        var definition = new ScheduleDefinition
        {
            ScheduleName = "nightly_build",
            WorkflowTypeId = "chat",
            Trigger = new ScheduleTrigger
            {
                Type = "frequency",
                IntervalHours = 1,
                IntervalMinutes = 30,
                IntervalSeconds = 0
            },
            ScheduleType = "benchmark",
            BenchmarkSelections = new List<ScheduleSelection>
            {
                new() { ServerId = "server-a", ModelId = "model-1" },
                new() { ServerId = "server-b", ModelId = "model-2" }
            }
        };

        var saved = _service.SaveSchedule(definition, out var error);

        Assert.True(saved);
        Assert.True(string.IsNullOrWhiteSpace(error));

        var loaded = _service.GetSchedule("nightly_build");
        Assert.NotNull(loaded);
        Assert.Equal("nightly_build", loaded!.ScheduleName);
        Assert.Equal("chat", loaded.WorkflowTypeId);
        Assert.Equal("frequency", loaded.Trigger.Type);
        Assert.Equal("benchmark", loaded.ScheduleType);
        Assert.Equal(2, loaded.BenchmarkSelections.Count);

        var scheduleJsonPath = Path.Combine(_root, "nightly_build", "schedule.json");
        Assert.True(File.Exists(scheduleJsonPath));

        var scheduleRoot = Path.Combine(_root, "nightly_build");
        foreach (var subdirectory in ScheduleConstants.WorkspaceSubdirectories)
        {
            Assert.False(
                Directory.Exists(Path.Combine(scheduleRoot, subdirectory)),
                $"Did not expect base schedule folder to contain '{subdirectory}'.");
        }
    }

    [Fact]
    public void SaveSchedule_InvalidName_Fails()
    {
        var definition = new ScheduleDefinition
        {
            ScheduleName = "bad/name",
            WorkflowTypeId = "chat",
            Trigger = new ScheduleTrigger { Type = "immediate" },
            ScheduleType = "regular",
            RegularSelection = new ScheduleSelection { ServerId = "server", ModelId = "model" }
        };

        var saved = _service.SaveSchedule(definition, out var error);

        Assert.False(saved);
        Assert.Contains("Schedule name", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BenchmarkFolderName_SanitizesUnsafeCharacters()
    {
        var sanitized = ScheduleDefinitionService.BuildBenchmarkFolderName("LM Studio/Main", "qwen:14b");
        Assert.DoesNotContain("/", sanitized);
        Assert.DoesNotContain(":", sanitized);
        Assert.NotEmpty(sanitized);
    }

    [Fact]
    public void ListSchedules_MarksMissingScheduleJsonAsCorrupted()
    {
        var scheduleDir = Path.Combine(_root, "mylogicscheduler");
        Directory.CreateDirectory(scheduleDir);

        var runtimeStatePath = Path.Combine(scheduleDir, "mylogicscheduler.json");
        File.WriteAllText(runtimeStatePath, """
        {
          "snapshot": { "status": "Ready" }
        }
        """);

        var schedules = _service.ListSchedules();
        var corrupted = schedules.Single(item => item.ScheduleName == "mylogicscheduler");

        Assert.Equal("corrupted", corrupted.Status);
        Assert.Contains("schedule.json", corrupted.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ListSchedules_UsesScheduleJsonEvenWhenRuntimeStateJsonExists()
    {
        var definition = new ScheduleDefinition
        {
            ScheduleName = "schedule_collision",
            WorkflowTypeId = "chat",
            Trigger = new ScheduleTrigger { Type = "immediate" },
            ScheduleType = "regular",
            RegularSelection = new ScheduleSelection { ServerId = "server", ModelId = "model" }
        };

        var saved = _service.SaveSchedule(definition, out var error);
        Assert.True(saved);
        Assert.True(string.IsNullOrWhiteSpace(error));

        var scheduleDir = Path.Combine(_root, "schedule_collision");
        var runtimeStatePath = Path.Combine(scheduleDir, "schedule_collision.json");
        File.WriteAllText(runtimeStatePath, """
        {
          "snapshot": {
            "selectedProjectName": "schedule_collision"
          }
        }
        """);

        var schedules = _service.ListSchedules();
        var summary = schedules.Single(item => item.ScheduleName == "schedule_collision");

        Assert.Equal("ready", summary.Status);
        Assert.Equal("chat", summary.WorkflowTypeId);
        Assert.Null(summary.Error);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
