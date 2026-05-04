using ABHive;
using ABHive.Web;

namespace ABHive.Tests;

public sealed class WorkflowTypeEditorServiceTests : IDisposable
{
    private readonly string _workflowTypesRoot;
    private readonly WorkflowTypeEditorService _service;

    public WorkflowTypeEditorServiceTests()
    {
        _workflowTypesRoot = Path.Combine(Path.GetTempPath(), $"abhive-workflow-editor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workflowTypesRoot);

        var settings = new AppSettings
        {
            WorkflowTypesDirectory = _workflowTypesRoot
        };
        settings.ValidateAndSet();

        _service = new WorkflowTypeEditorService(settings);
    }

    [Fact]
    public void CreateWorkflowType_DuplicateRejected()
    {
        var first = _service.CreateWorkflowType("my_flow", "standard");
        var second = _service.CreateWorkflowType("my_flow", "standard");

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Equal(WorkflowTypeEditorErrorKind.Conflict, second.ErrorKind);
    }

    [Fact]
    public void AddStep_UsesDefaultSuggestionAndSupportsExplicitName()
    {
        _service.CreateWorkflowType("my_flow", "standard");

        var secondStep = _service.AddStep("my_flow", null, "standard");
        var explicitStep = _service.AddStep("my_flow", "Review.md", "blank");
        var list = _service.GetSteps("my_flow");

        Assert.True(secondStep.Success);
        Assert.Equal("Step2.md", secondStep.StepFileName);
        Assert.True(explicitStep.Success);
        Assert.Equal("Review.md", explicitStep.StepFileName);
        Assert.True(list.Success);
        Assert.Equal(new[] { "Review.md", "Step1.md", "Step2.md" }, list.Steps.Select(step => step.StepFileName).ToArray());
    }

    [Fact]
    public void SaveStep_ValidatesMetadataAndDeletesWhenEmpty()
    {
        _service.CreateWorkflowType("meta_flow", "ticketIteration");

        var invalidMetadata = _service.SaveStep("meta_flow", "Step1.md", "# Step", "{bad json}");
        Assert.False(invalidMetadata.Success);
        Assert.Equal(WorkflowTypeEditorErrorKind.Validation, invalidMetadata.ErrorKind);

        var validMetadata = _service.SaveStep("meta_flow", "Step1.md", "# Step", "{\"executionMode\":\"ticketIteration\"}");
        Assert.True(validMetadata.Success);
        Assert.True(File.Exists(Path.Combine(_workflowTypesRoot, "meta_flow", "Step1.json")));

        var clearMetadata = _service.SaveStep("meta_flow", "Step1.md", "# Step", "   ");
        Assert.True(clearMetadata.Success);
        Assert.False(File.Exists(Path.Combine(_workflowTypesRoot, "meta_flow", "Step1.json")));
    }

    [Fact]
    public void StepFileNameValidation_BlocksTraversalAttempts()
    {
        _service.CreateWorkflowType("safe_flow", "standard");

        var addTraversal = _service.AddStep("safe_flow", "../hijack.md", "standard");
        var readTraversal = _service.GetStepContent("safe_flow", "../Step1.md");

        Assert.False(addTraversal.Success);
        Assert.Equal(WorkflowTypeEditorErrorKind.Validation, addTraversal.ErrorKind);
        Assert.False(readTraversal.Success);
        Assert.Equal(WorkflowTypeEditorErrorKind.Validation, readTraversal.ErrorKind);
    }

    [Fact]
    public void ListSteps_IncludesMetadataFlagAndFileName()
    {
        _service.CreateWorkflowType("ticket_flow", "ticketIteration");

        var steps = _service.GetSteps("ticket_flow");

        Assert.True(steps.Success);
        var firstStep = Assert.Single(steps.Steps);
        Assert.Equal("Step1.md", firstStep.StepFileName);
        Assert.True(firstStep.HasMetadata);
        Assert.Equal("Step1.json", firstStep.MetadataFileName);
    }

    [Fact]
    public void DeleteStep_RemovesMarkdownAndMetadata()
    {
        _service.CreateWorkflowType("delete_step_flow", "ticketIteration");

        var result = _service.DeleteStep("delete_step_flow", "Step1.md");

        Assert.True(result.Success);
        Assert.False(File.Exists(Path.Combine(_workflowTypesRoot, "delete_step_flow", "Step1.md")));
        Assert.False(File.Exists(Path.Combine(_workflowTypesRoot, "delete_step_flow", "Step1.json")));
    }

    [Fact]
    public void DeleteWorkflow_RemovesWorkflowDirectory()
    {
        _service.CreateWorkflowType("delete_workflow_flow", "standard");
        _service.AddStep("delete_workflow_flow", "Step2.md", "standard");

        var result = _service.DeleteWorkflowType("delete_workflow_flow");

        Assert.True(result.Success);
        Assert.False(Directory.Exists(Path.Combine(_workflowTypesRoot, "delete_workflow_flow")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_workflowTypesRoot))
        {
            Directory.Delete(_workflowTypesRoot, true);
        }
    }
}
