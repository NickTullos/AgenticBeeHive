using ABHive.Application;
using ABHive.Infrastructure;
using ABHive.Presentation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ABHive.IntegrationTests;

public class EndToEndWorkflowTests : IDisposable
{
    private readonly string _testStepsDirectory;
    private readonly string _testLogFilePath;

    public EndToEndWorkflowTests()
    {
        _testStepsDirectory = Path.Combine(Path.GetTempPath(), $"agentic-test-steps-{Guid.NewGuid()}");
        _testLogFilePath = Path.Combine(Path.GetTempPath(), $"agentic-test-logs-{Guid.NewGuid()}.json");
        
        Directory.CreateDirectory(_testStepsDirectory);
    }

    [Fact]
    public async Task RunAsync_SuccessfullyExecutesAllSteps()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_testStepsDirectory, "01-greeting.md"), "# Greeting\nSay hello!");
        File.WriteAllText(Path.Combine(_testStepsDirectory, "02-summary.md"), "# Summary\nProvide a summary.");

        var appSettings = new AppSettings(
            lmStudioUrl: "http://localhost:9999",
            modelName: "test-model",
            stepsDirectory: _testStepsDirectory,
            logFilePath: _testLogFilePath,
            defaultToolTimeoutMs: 5000
        );

        var mockClient = new MockLLMClient();
        var cache = new ABHive.ToolCache();
        var executor = new ToolExecutor(appSettings, cache);
        var stepConversationService = new StepConversationService(appSettings, mockClient, executor);
        var formatter = new TestConsoleOutputFormatter();
        IMetricsLogger? metricsLogger = new MetricsLogger(appSettings);

        var orchestrator = new WorkflowOrchestrator(
            appSettings,
            mockClient,
            executor,
            stepConversationService,
            formatter,
            metricsLogger
        );

        // Act
        var metrics = await orchestrator.RunAsync();

        // Assert
        Assert.Equal(2, metrics.TotalSteps);
        Assert.Equal(2, metrics.SuccessfulSteps);
        Assert.Equal(0, metrics.FailedSteps);
    }

    [Fact]
    public async Task RunAsync_WritesMetricsLogFile()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_testStepsDirectory, "01-test.md"), "# Test\nTest step.");

        var appSettings = new AppSettings(
            lmStudioUrl: "http://localhost:9999",
            modelName: "test-model",
            stepsDirectory: _testStepsDirectory,
            logFilePath: _testLogFilePath,
            defaultToolTimeoutMs: 5000
        );

        var mockClient = new MockLLMClient();
        var cache = new ABHive.ToolCache();
        var executor = new ToolExecutor(appSettings, cache);
        var stepConversationService = new StepConversationService(appSettings, mockClient, executor);
        var formatter = new TestConsoleOutputFormatter();
        IMetricsLogger? metricsLogger = new MetricsLogger(appSettings);

        var orchestrator = new WorkflowOrchestrator(
            appSettings,
            mockClient,
            executor,
            stepConversationService,
            formatter,
            metricsLogger
        );

        // Act
        await orchestrator.RunAsync();

        // Assert
        Assert.True(File.Exists(_testLogFilePath));
        
        var json = await File.ReadAllTextAsync(_testLogFilePath);
        Assert.Contains("Timestamp", json);
        Assert.Contains("Metrics", json);
        Assert.Contains("StepResults", json);
    }

    [Fact]
    public async Task RunAsync_HandlesStepErrorsGracefully()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_testStepsDirectory, "01-fail.md"), "# Fail\nFail step.");
        
        var appSettings = new AppSettings(
            lmStudioUrl: "http://localhost:9999",
            modelName: "test-model",
            stepsDirectory: _testStepsDirectory,
            logFilePath: _testLogFilePath,
            defaultToolTimeoutMs: 5000
        );

        var mockClient = new MockLLMClient(throwExceptionOnStep1: true);
        var cache = new ABHive.ToolCache();
        var executor = new ToolExecutor(appSettings, cache);
        var stepConversationService = new StepConversationService(appSettings, mockClient, executor);
        var formatter = new TestConsoleOutputFormatter();

        var orchestrator = new WorkflowOrchestrator(
            appSettings,
            mockClient,
            executor,
            stepConversationService,
            formatter
        );

        // Act
        var metrics = await orchestrator.RunAsync();

        // Assert
        Assert.Equal(1, metrics.TotalSteps);
        Assert.Equal(0, metrics.SuccessfulSteps);
        Assert.Equal(1, metrics.FailedSteps);
    }

    [Fact]
    public async Task RunAsync_FiltersDisabledTools()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_testStepsDirectory, "01-test.md"), "# Test\nTest step.");

        var appSettings = new AppSettings(
            lmStudioUrl: "http://localhost:9999",
            modelName: "test-model",
            stepsDirectory: _testStepsDirectory,
            logFilePath: _testLogFilePath,
            defaultToolTimeoutMs: 5000
        );
        
        // Disable Bash tool
        appSettings.ToolConfigs["Bash"] = new ToolConfig { Name = "Bash", Enabled = false };

        var mockClient = new MockLLMClient();
        var cache = new ABHive.ToolCache();
        var executor = new ToolExecutor(appSettings, cache);
        var stepConversationService = new StepConversationService(appSettings, mockClient, executor);
        var formatter = new TestConsoleOutputFormatter();

        var orchestrator = new WorkflowOrchestrator(
            appSettings,
            mockClient,
            executor,
            stepConversationService,
            formatter
        );

        // Act
        var metrics = await orchestrator.RunAsync();

        // Assert
        Assert.Equal(1, metrics.TotalSteps);
    }

    [Fact]
    public async Task RunFromStepAsync_ResumesFromRequestedStep()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_testStepsDirectory, "01-first.md"), "# First\nRun the first step.");
        File.WriteAllText(Path.Combine(_testStepsDirectory, "02-second.md"), "# Second\nRun the second step.");
        File.WriteAllText(Path.Combine(_testStepsDirectory, "03-third.md"), "# Third\nRun the third step.");

        var appSettings = new AppSettings(
            lmStudioUrl: "http://localhost:9999",
            modelName: "test-model",
            stepsDirectory: _testStepsDirectory,
            logFilePath: _testLogFilePath,
            defaultToolTimeoutMs: 5000
        );

        var mockClient = new MockLLMClient();
        var cache = new ABHive.ToolCache();
        var executor = new ToolExecutor(appSettings, cache);
        var stepConversationService = new StepConversationService(appSettings, mockClient, executor);
        var formatter = new TestConsoleOutputFormatter();

        var orchestrator = new WorkflowOrchestrator(
            appSettings,
            mockClient,
            executor,
            stepConversationService,
            formatter
        );

        var existingMetrics = new WorkflowMetrics
        {
            TotalSteps = 3,
            SuccessfulSteps = 1
        };

        // Act
        var metrics = await orchestrator.RunFromStepAsync(2, existingMetrics);

        // Assert
        Assert.Equal(3, metrics.TotalSteps);
        Assert.Equal(3, metrics.SuccessfulSteps);
        Assert.Equal(0, metrics.FailedSteps);
        Assert.Equal(2, mockClient.CallCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testStepsDirectory))
        {
            Directory.Delete(_testStepsDirectory, true);
        }
        
        if (File.Exists(_testLogFilePath))
        {
            File.Delete(_testLogFilePath);
        }
    }
}

public class MockLLMClient : ILLMClient
{
    private readonly bool _throwExceptionOnStep1;
    public int CallCount { get; private set; }

    public MockLLMClient(bool throwExceptionOnStep1 = false)
    {
        _throwExceptionOnStep1 = throwExceptionOnStep1;
    }

    public Task<LLMResponse> GenerateAsync(LLMRequest request, CancellationToken ct = default)
    {
        CallCount++;

        if (_throwExceptionOnStep1 && request.Messages.Any(m => m.Content.Contains("Fail")))
        {
            return Task.FromException<LLMResponse>(new InvalidOperationException("Simulated LLM error"));
        }

        return Task.FromResult(new LLMResponse
        {
            Content = "Test response",
            ToolCalls = new List<ToolCall>()
        });
    }
}
