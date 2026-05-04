using System.Text.Json;
using ABHive;
using ABHive.Web;

namespace ABHive.Tests;

public sealed class WorkflowStateStoreStartupTests : IDisposable
{
    private readonly string _root;

    public WorkflowStateStoreStartupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "abhive-workflow-state-startup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task Constructor_IgnoresPointer_WhenProjectDirectoryMissing()
    {
        var scheduleProjectDirectory = Path.Combine(_root, "mylogicscheduler");
        var pointerPath = Path.Combine(_root, ".current-project.json");
        await File.WriteAllTextAsync(pointerPath, JsonSerializer.Serialize(new
        {
            projectName = "mylogicscheduler",
            projectDirectory = scheduleProjectDirectory,
            lastUpdatedUtc = DateTime.UtcNow
        }));

        var settings = new AppSettings
        {
            ProjectRootDirectory = _root
        };

        var resolver = new TicketIterationStatusResolver(settings);
        var store = new WorkflowStateStore(settings, resolver);
        var hydration = await store.GetHydrationAsync();

        Assert.True(string.IsNullOrWhiteSpace(hydration.Snapshot.SelectedProjectName));
        Assert.True(string.IsNullOrWhiteSpace(hydration.Snapshot.SelectedProjectDirectory));
        Assert.False(Directory.Exists(scheduleProjectDirectory));
        Assert.False(File.Exists(pointerPath));
    }

    [Fact]
    public async Task Constructor_LoadsPointer_WhenProjectDirectoryExistsUnderProjectsRoot()
    {
        var projectName = "projectalpha";
        var projectDirectory = Path.Combine(_root, projectName);
        Directory.CreateDirectory(projectDirectory);

        var pointerPath = Path.Combine(_root, ".current-project.json");
        await File.WriteAllTextAsync(pointerPath, JsonSerializer.Serialize(new
        {
            projectName,
            projectDirectory,
            lastUpdatedUtc = DateTime.UtcNow
        }));

        var settings = new AppSettings
        {
            ProjectRootDirectory = _root
        };

        var resolver = new TicketIterationStatusResolver(settings);
        var store = new WorkflowStateStore(settings, resolver);
        var hydration = await store.GetHydrationAsync();

        Assert.Equal(projectName, hydration.Snapshot.SelectedProjectName);
        Assert.Equal(Path.GetFullPath(projectDirectory), hydration.Snapshot.SelectedProjectDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
