using ABHive.Web;
using Xunit;

namespace ABHive.Tests;

public class ProjectDashboardServiceTests : IDisposable
{
    private readonly string _projectsRoot;

    public ProjectDashboardServiceTests()
    {
        _projectsRoot = Path.Combine(Path.GetTempPath(), $"agentic-dashboard-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectsRoot);
    }

    [Fact]
    public void GetProjectDashboards_WithFullJsonContract_ParsesExpectedFields()
    {
        var projectRoot = CreateProject("alpha");
        WriteFile(Path.Combine(projectRoot, "goals", "goal.md"), "# Goal\nBuild a dashboard.");
        WriteFile(Path.Combine(projectRoot, "planning", "planning.json"), """
        {
          "requirements": { "feature": "dashboard" },
          "qa": [
            { "question": "What status?", "answer": "Track tickets" }
          ],
          "assumptions": ["No LLM"]
        }
        """);
        WriteFile(Path.Combine(projectRoot, "design", "architecture-design.json"), """
        {
          "architecture": "Layered API + UI",
          "components": ["API", "UI"],
          "data_models": [{ "name": "Ticket", "fields": { "id": "string" } }],
          "project_structure": { "solution_file": "App.sln" }
        }
        """);
        WriteFile(Path.Combine(projectRoot, "tickets", "tickets.json"), """
        [
          {
            "ticket_id": "T-1",
            "title": "Add API",
            "description": "Build endpoint",
            "priority": "high",
            "type": "feature",
            "dependencies": [],
            "definition_of_done": ["Tests pass"]
          }
        ]
        """);
        WriteFile(Path.Combine(projectRoot, "tickets", "completed.json"), """
        [
          {
            "ticket_id": "T-0",
            "title": "Scaffold project",
            "priority": "low"
          }
        ]
        """);

        var service = CreateService();
        var items = service.GetProjectDashboards();
        var alpha = Assert.Single(items);

        Assert.Equal("alpha", alpha.ProjectName);
        Assert.Equal("In Progress", alpha.ProjectStatus);
        Assert.Contains("Goal", alpha.GoalSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Single(alpha.Planning.Qa);
        Assert.Equal("What status?", alpha.Planning.Qa[0].Question);
        Assert.Equal("Track tickets", alpha.Planning.Qa[0].Answer);
        Assert.NotNull(alpha.Planning.Requirements);
        Assert.Single(alpha.Planning.Assumptions);
        Assert.Equal("Layered API + UI", alpha.Design.Overview);
        Assert.Equal(2, alpha.Design.Components.Count);
        Assert.Equal(1, alpha.Design.DataModelsCount);
        Assert.Single(alpha.Tickets.Open);
        Assert.Single(alpha.Tickets.Completed);
        Assert.Equal(2, alpha.Tickets.Counts.Total);
        Assert.True(alpha.HasAnyData);
        Assert.Empty(alpha.Warnings);
    }

    [Fact]
    public void GetProjectDashboards_WithQuestionsOnly_SetsPendingAnswers()
    {
        var projectRoot = CreateProject("bravo");
        WriteFile(Path.Combine(projectRoot, "planning", "planning.json"), """
        {
          "questions": ["Q1", "Q2"]
        }
        """);

        var service = CreateService();
        var item = Assert.Single(service.GetProjectDashboards());

        Assert.Equal(2, item.Planning.Qa.Count);
        Assert.All(item.Planning.Qa, qa => Assert.Equal("Pending", qa.Answer));
        Assert.Equal("Planned", item.ProjectStatus);
    }

    [Fact]
    public void GetProjectDashboards_WithMalformedDesignJson_AddsWarningButStillReturnsProject()
    {
        var projectRoot = CreateProject("charlie");
        WriteFile(Path.Combine(projectRoot, "design", "architecture-design.json"), "{ bad json");
        WriteFile(Path.Combine(projectRoot, "goals", "goal.md"), "# Goal\nSomething");

        var service = CreateService();
        var item = Assert.Single(service.GetProjectDashboards());

        Assert.NotEmpty(item.Warnings);
        Assert.True(string.IsNullOrWhiteSpace(item.Design.Overview));
        Assert.True(item.HasAnyData);
    }

    [Fact]
    public void GetProjectDashboards_WithNoData_ReturnsPending()
    {
        CreateProject("delta");
        var service = CreateService();

        var item = Assert.Single(service.GetProjectDashboards());
        Assert.Equal("Pending", item.ProjectStatus);
        Assert.False(item.HasAnyData);
        Assert.Equal("Pending", item.GoalSummary);
        Assert.Equal(0, item.Tickets.Counts.Total);
    }

    [Fact]
    public void GetProjectDashboards_StatusDerivation_CoversCompletedAndPlanned()
    {
        var completedRoot = CreateProject("echo");
        WriteFile(Path.Combine(completedRoot, "tickets", "completed.json"), """
        [
          { "ticket_id": "T-1", "title": "Done" }
        ]
        """);

        var plannedRoot = CreateProject("foxtrot");
        WriteFile(Path.Combine(plannedRoot, "planning", "planning.json"), """
        {
          "requirements": { "scope": "v1" },
          "questions": ["Any constraints?"]
        }
        """);

        var service = CreateService();
        var items = service.GetProjectDashboards().OrderBy(item => item.ProjectName).ToList();

        var echo = items.First(item => item.ProjectName == "echo");
        var foxtrot = items.First(item => item.ProjectName == "foxtrot");

        Assert.Equal("Completed", echo.ProjectStatus);
        Assert.Equal("Planned", foxtrot.ProjectStatus);
    }

    private ProjectDashboardService CreateService()
    {
        var workspaceService = new ProjectWorkspaceService(_projectsRoot);
        return new ProjectDashboardService(workspaceService);
    }

    private string CreateProject(string projectName)
    {
        var root = Path.Combine(_projectsRoot, projectName);
        Directory.CreateDirectory(Path.Combine(root, "goals"));
        Directory.CreateDirectory(Path.Combine(root, "planning"));
        Directory.CreateDirectory(Path.Combine(root, "design"));
        Directory.CreateDirectory(Path.Combine(root, "tickets"));
        Directory.CreateDirectory(Path.Combine(root, "solution"));
        Directory.CreateDirectory(Path.Combine(root, "files"));
        return root;
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_projectsRoot))
            {
                Directory.Delete(_projectsRoot, true);
            }
        }
        catch
        {
            // Ignore cleanup errors on temp files.
        }
    }
}
