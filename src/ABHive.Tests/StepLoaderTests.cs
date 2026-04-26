using ABHive.Application;
using ABHive.Infrastructure;

namespace ABHive.Tests;

public class StepLoaderTests : IDisposable
{
    private readonly string _testStepsDirectory;
    
    public StepLoaderTests()
    {
        _testStepsDirectory = Path.Combine(Path.GetTempPath(), $"agentic-test-{Guid.NewGuid()}");
        
        Directory.CreateDirectory(_testStepsDirectory);
        
        File.WriteAllText(Path.Combine(_testStepsDirectory, "03-third.md"), "# Third Step\nThis is the third step.");
        File.WriteAllText(Path.Combine(_testStepsDirectory, "01-first.md"), "# First Step\nThis is the first step.");
        File.WriteAllText(Path.Combine(_testStepsDirectory, "02-second.md"), "# Second Step\nThis is the second step.");
    }

    [Fact]
    public async Task LoadStepsAsync_AlphabeticalOrdering_WorksCorrectly()
    {
        var orchestrator = CreateOrchestrator();
        
        var steps = await CallLoadStepsAsync(orchestrator);

        Assert.Equal(3, steps.Count);
        
        Assert.Contains("01-first.md", steps[0].FilePath);
        Assert.Contains("02-second.md", steps[1].FilePath);
        Assert.Contains("03-third.md", steps[2].FilePath);

        Assert.Equal(1, steps[0].Order);
        Assert.Equal(2, steps[1].Order);
        Assert.Equal(3, steps[2].Order);
    }

    [Fact]
    public async Task LoadStepsAsync_ReturnsEmpty_WhenDirectoryMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"agentic-missing-{Guid.NewGuid()}");
        
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);

        var orchestrator = CreateOrchestratorWithStepsDir(tempDir);
        
        var steps = await CallLoadStepsAsync(orchestrator);

        Assert.Empty(steps);
        Assert.False(Directory.Exists(tempDir));
    }

    [Fact]
    public async Task LoadStepsAsync_HandlesMalformedFiles_Gracefully()
    {
        var badFile = Path.Combine(_testStepsDirectory, "bad-file.txt");
        File.WriteAllText(badFile, "");

        var orchestrator = CreateOrchestrator();
        
        var steps = await CallLoadStepsAsync(orchestrator);

        Assert.Equal(3, steps.Count);
        
        File.Delete(badFile);
    }

    private IWorkflowOrchestrator CreateOrchestrator()
    {
        var settings = new AppSettings("http://localhost:1234", "test-model", _testStepsDirectory, "./logs/metrics.json", 30000);
        var llmClient = new LLMClient(new HttpClient(), settings);
        var toolExecutor = new ToolExecutor(settings, new ABHive.ToolCache());
        var stepConversationService = new StepConversationService(settings, llmClient, toolExecutor);

        return new WorkflowOrchestrator(
            settings,
            llmClient,
            toolExecutor,
            stepConversationService,
            new ABHive.Presentation.ConsoleOutputFormatter()
        );
    }

    private IWorkflowOrchestrator CreateOrchestratorWithStepsDir(string stepsDir)
    {
        var settings = new AppSettings("http://localhost:1234", "test-model", stepsDir, "./logs/metrics.json", 30000);
        var llmClient = new LLMClient(new HttpClient(), settings);
        var toolExecutor = new ToolExecutor(settings, new ABHive.ToolCache());
        var stepConversationService = new StepConversationService(settings, llmClient, toolExecutor);

        return new WorkflowOrchestrator(
            settings,
            llmClient,
            toolExecutor,
            stepConversationService,
            new ABHive.Presentation.ConsoleOutputFormatter()
        );
    }

    private async Task<List<Step>> CallLoadStepsAsync(IWorkflowOrchestrator orchestrator)
    {
        var method = typeof(WorkflowOrchestrator).GetMethod("LoadStepsAsync", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (method == null)
            throw new InvalidOperationException("LoadStepsAsync method not found");
        
        var result = await (Task<List<Step>>)method.Invoke(orchestrator, new object[] { CancellationToken.None })!;
        return result;
    }

    public void Dispose()
    {
        if (Directory.Exists(_testStepsDirectory))
            Directory.Delete(_testStepsDirectory, true);
    }
}
