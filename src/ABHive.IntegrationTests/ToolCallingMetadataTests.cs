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

public class ToolCallingMetadataTests : IDisposable
{
    private readonly string _testStepsDirectory;
    private readonly string _testLogFilePath;

    public ToolCallingMetadataTests()
    {
        _testStepsDirectory = Path.Combine(Path.GetTempPath(), $"agentic-tool-steps-{Guid.NewGuid()}");
        _testLogFilePath = Path.Combine(Path.GetTempPath(), $"agentic-tool-logs-{Guid.NewGuid()}.json");

        Directory.CreateDirectory(_testStepsDirectory);
    }

    [Fact]
    public async Task RunAsync_PreservesToolCallMetadataAcrossTurns()
    {
        File.WriteAllText(Path.Combine(_testStepsDirectory, "01-analysis.md"), "# Analysis\nInspect the goal file and summarize it.");

        var appSettings = CreateSettings();
        appSettings.ToolConfigs["WebFetch"] = new ToolConfig { Name = "WebFetch", Enabled = false };

        var mockClient = new ToolAwareMockLLMClient();
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

        var metrics = await orchestrator.RunAsync();

        Assert.True(mockClient.SawLinkedToolResult);
        Assert.Equal(2, mockClient.CallCount);
        Assert.Equal(1, metrics.SuccessfulSteps);
        Assert.Equal(0, metrics.FailedSteps);
    }

    [Fact]
    public async Task ProcessUserMessageAsync_PreservesToolCallMetadataAcrossTurns()
    {
        var appSettings = CreateSettings();
        appSettings.ToolConfigs["WebFetch"] = new ToolConfig { Name = "WebFetch", Enabled = false };

        var mockClient = new ToolAwareMockLLMClient();
        var cache = new ABHive.ToolCache();
        var executor = new ToolExecutor(appSettings, cache);
        var service = new StepConversationService(appSettings, mockClient, executor);

        var result = await service.ProcessUserMessageAsync(
            new List<ChatMessage>
            {
                new ChatMessage { Role = "system", Content = "You are helping with step 1." },
                new ChatMessage { Role = "assistant", Content = "What should I inspect?" }
            },
            "Read the goal file."
        );

        Assert.True(mockClient.SawLinkedToolResult);
        Assert.Equal(2, mockClient.CallCount);
        Assert.Equal("Tool result received.", result.FinalResponse.Content);
        Assert.Contains(result.UpdatedStepContext, message => message.Role == "tool" && message.ToolCallId == ToolAwareMockLLMClient.ToolCallId);
    }

    [Fact]
    public async Task ExecuteToolsAsync_DoesNotPopulateCacheForRepeatedCommands()
    {
        var appSettings = CreateSettings();
        var cache = new ABHive.ToolCache();
        var executor = new ToolExecutor(appSettings, cache);

        var firstResults = await executor.ExecuteToolsAsync(new List<ToolCall>
        {
            new()
            {
                Id = "call_1",
                Name = "execute_bash",
                Arguments = "{\"command\":\"printf 'hello'\"}"
            }
        });

        var secondResults = await executor.ExecuteToolsAsync(new List<ToolCall>
        {
            new()
            {
                Id = "call_2",
                Name = "execute_bash",
                Arguments = "{\"command\":\"printf 'hello'\"}"
            }
        });

        Assert.Single(firstResults);
        Assert.Single(secondResults);
        Assert.Equal("call_1", firstResults[0].ToolId);
        Assert.Equal("call_2", secondResults[0].ToolId);
        Assert.Equal("hello", firstResults[0].Output);
        Assert.Equal("hello", secondResults[0].Output);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task ProcessUserMessageAsync_PseudoToolCallText_EmitsWarningAndRetriesWithoutExecution()
    {
        var appSettings = CreateSettings();
        appSettings.ToolConfigs["WebFetch"] = new ToolConfig { Name = "WebFetch", Enabled = false };

        var mockClient = new PseudoToolCallThenPlainTextMockLLMClient();
        var cache = new ABHive.ToolCache();
        var executor = new ToolExecutor(appSettings, cache);
        var service = new StepConversationService(appSettings, mockClient, executor);
        var warnings = new List<string>();

        var result = await service.ProcessUserMessageAsync(
            new List<ChatMessage>
            {
                new ChatMessage { Role = "system", Content = "You are helping with step 1." }
            },
            "Please run a build.",
            onPseudoToolCallWarningAsync: warning =>
            {
                warnings.Add(warning);
                return Task.CompletedTask;
            });

        Assert.Equal(2, mockClient.CallCount);
        Assert.True(mockClient.SawRetryInstruction);
        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, warning => warning.Contains("not executed for safety", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, warning => warning.Contains("Auto-correction sent to the model", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(result.ToolResults);
        Assert.DoesNotContain(result.UpdatedStepContext, message => message.Role == "tool");
        Assert.Equal("Final plain-text response after strict retry.", result.FinalResponse.Content);
    }

    [Fact]
    public async Task ProcessUserMessageAsync_MixedContentWithValidToolCall_ExecutesToolNormally()
    {
        var appSettings = CreateSettings();
        appSettings.ToolConfigs["WebFetch"] = new ToolConfig { Name = "WebFetch", Enabled = false };

        var mockClient = new MixedContentWithValidToolCallMockLLMClient();
        var cache = new ABHive.ToolCache();
        var executor = new ToolExecutor(appSettings, cache);
        var service = new StepConversationService(appSettings, mockClient, executor);
        var warnings = new List<string>();

        var result = await service.ProcessUserMessageAsync(
            new List<ChatMessage>
            {
                new ChatMessage { Role = "system", Content = "You are helping with step 1." }
            },
            "Run the build and summarize.",
            onPseudoToolCallWarningAsync: warning =>
            {
                warnings.Add(warning);
                return Task.CompletedTask;
            });

        Assert.Equal(2, mockClient.CallCount);
        Assert.Empty(warnings);
        Assert.NotEmpty(result.ToolResults);
        Assert.Contains(result.ToolResults, tool => tool.ToolName == "execute_bash");
        Assert.Contains(result.UpdatedStepContext, message => message.Role == "assistant" && (message.Content ?? string.Empty).Contains("Running build now"));
        Assert.Contains(result.UpdatedStepContext, message => message.Role == "tool" && message.Name == "execute_bash");
        Assert.Equal("Build complete.", result.FinalResponse.Content);
    }

    [Fact]
    public async Task ProcessUserMessageAsync_MalformedPseudoToolTag_EmitsWarningWithoutCrash()
    {
        var appSettings = CreateSettings();
        var mockClient = new MalformedPseudoToolCallThenPlainTextMockLLMClient();
        var cache = new ABHive.ToolCache();
        var executor = new ToolExecutor(appSettings, cache);
        var service = new StepConversationService(appSettings, mockClient, executor);
        var warnings = new List<string>();

        var result = await service.ProcessUserMessageAsync(
            new List<ChatMessage>
            {
                new ChatMessage { Role = "system", Content = "You are helping with step 1." }
            },
            "Try tool usage.",
            onPseudoToolCallWarningAsync: warning =>
            {
                warnings.Add(warning);
                return Task.CompletedTask;
            });

        Assert.Equal(2, mockClient.CallCount);
        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, warning => warning.Contains("not executed for safety", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, warning => warning.Contains("Auto-correction sent to the model", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(result.ToolResults);
        Assert.Equal("Recovered after malformed pseudo call.", result.FinalResponse.Content);
    }

    [Fact]
    public async Task ProcessUserMessageAsync_PseudoToolCallInReasoning_EmitsWarningAndRetriesWithoutExecution()
    {
        var appSettings = CreateSettings();
        var mockClient = new ReasoningPseudoToolCallThenPlainTextMockLLMClient();
        var cache = new ABHive.ToolCache();
        var executor = new ToolExecutor(appSettings, cache);
        var service = new StepConversationService(appSettings, mockClient, executor);
        var warnings = new List<string>();

        var result = await service.ProcessUserMessageAsync(
            new List<ChatMessage>
            {
                new ChatMessage { Role = "system", Content = "You are helping with step 1." }
            },
            "Please inspect files.",
            onPseudoToolCallWarningAsync: warning =>
            {
                warnings.Add(warning);
                return Task.CompletedTask;
            });

        Assert.Equal(2, mockClient.CallCount);
        Assert.True(mockClient.SawRetryInstruction);
        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, warning => warning.Contains("not executed for safety", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, warning => warning.Contains("Auto-correction sent to the model", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("not executed for safety", warnings[0], StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.ToolResults);
        Assert.Equal("Reasoning-only pseudo call was rejected safely.", result.FinalResponse.Content);
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

    private AppSettings CreateSettings()
    {
        var settings = new AppSettings(
            lmStudioUrl: "http://localhost:9999",
            modelName: "test-model",
            stepsDirectory: _testStepsDirectory,
            logFilePath: _testLogFilePath,
            defaultToolTimeoutMs: 5000
        );

        settings.LlmServers = new List<LlmServerSettings>
        {
            new()
            {
                Id = "test-server",
                Name = "Test Server",
                BaseUrl = "http://localhost:9999",
                ApiKey = "",
                DefaultModelId = "test-model-id",
                Models = new List<LlmModelSettings>
                {
                    new()
                    {
                        Id = "test-model-id",
                        Name = "test-model"
                    }
                }
            }
        };
        settings.ActiveLlmServerId = "test-server";
        settings.ActiveLlmModelId = "test-model-id";
        settings.ModelName = "test-model";

        return settings;
    }
}

internal sealed class ToolAwareMockLLMClient : ILLMClient
{
    public const string ToolCallId = "call_step1_goal";

    public int CallCount { get; private set; }
    public bool SawLinkedToolResult { get; private set; }

    public Task<LLMResponse> GenerateAsync(LLMRequest request, CancellationToken ct = default)
    {
        CallCount++;

        if (CallCount == 1)
        {
            return Task.FromResult(new LLMResponse
            {
                Content = string.Empty,
                ToolCalls = new List<ToolCall>
                {
                    new()
                    {
                        Id = ToolCallId,
                        Name = "execute_bash",
                        Arguments = "{\"command\":\"printf 'goal loaded'\"}"
                    }
                }
            });
        }

        SawLinkedToolResult = HasLinkedToolResult(request.Messages);

        if (!SawLinkedToolResult)
        {
            throw new InvalidOperationException("Tool call metadata was not preserved between turns.");
        }

        return Task.FromResult(new LLMResponse
        {
            Content = "Tool result received.",
            ToolCalls = new List<ToolCall>()
        });
    }

    private static bool HasLinkedToolResult(List<ChatMessage> messages)
    {
        var assistantMessage = messages.LastOrDefault(message => message.Role == "assistant");
        var toolMessage = messages.LastOrDefault(message => message.Role == "tool");

        return assistantMessage?.ToolCalls?.Count == 1
            && assistantMessage.ToolCalls[0].Id == ToolCallId
            && assistantMessage.ToolCalls[0].Name == "execute_bash"
            && toolMessage?.ToolCallId == ToolCallId
            && toolMessage.Name == "execute_bash"
            && toolMessage.Content == "goal loaded";
    }
}

internal sealed class PseudoToolCallThenPlainTextMockLLMClient : ILLMClient
{
    public int CallCount { get; private set; }
    public bool SawRetryInstruction { get; private set; }

    public Task<LLMResponse> GenerateAsync(LLMRequest request, CancellationToken ct = default)
    {
        CallCount++;

        if (CallCount == 1)
        {
            return Task.FromResult(new LLMResponse
            {
                Content = "<tool_call><function=execute_bash><parameter=command>dotnet build</parameter></function></tool_call>"
            });
        }

        SawRetryInstruction = request.Messages.Any(message =>
            string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase) &&
            (message.Content ?? string.Empty).Contains("Reissue the same action using structured tool_calls only.", StringComparison.Ordinal));

        return Task.FromResult(new LLMResponse
        {
            Content = "Final plain-text response after strict retry."
        });
    }
}

internal sealed class MixedContentWithValidToolCallMockLLMClient : ILLMClient
{
    public const string ToolCallId = "call_mixed_1";
    public int CallCount { get; private set; }

    public Task<LLMResponse> GenerateAsync(LLMRequest request, CancellationToken ct = default)
    {
        CallCount++;

        if (CallCount == 1)
        {
            return Task.FromResult(new LLMResponse
            {
                Content = "Running build now using a structured call.",
                ToolCalls = new List<ToolCall>
                {
                    new()
                    {
                        Id = ToolCallId,
                        Name = "execute_bash",
                        Arguments = "{\"command\":\"printf 'ok'\"}"
                    }
                }
            });
        }

        return Task.FromResult(new LLMResponse
        {
            Content = "Build complete."
        });
    }
}

internal sealed class MalformedPseudoToolCallThenPlainTextMockLLMClient : ILLMClient
{
    public int CallCount { get; private set; }

    public Task<LLMResponse> GenerateAsync(LLMRequest request, CancellationToken ct = default)
    {
        CallCount++;

        if (CallCount == 1)
        {
            return Task.FromResult(new LLMResponse
            {
                Content = "<tool_call><function=execute_bash"
            });
        }

        return Task.FromResult(new LLMResponse
        {
            Content = "Recovered after malformed pseudo call."
        });
    }
}

internal sealed class ReasoningPseudoToolCallThenPlainTextMockLLMClient : ILLMClient
{
    public int CallCount { get; private set; }
    public bool SawRetryInstruction { get; private set; }

    public Task<LLMResponse> GenerateAsync(LLMRequest request, CancellationToken ct = default)
    {
        CallCount++;

        if (CallCount == 1)
        {
            return Task.FromResult(new LLMResponse
            {
                Content = "I should read project files.",
                ReasoningContent = "<tool_call><function=read_file><parameter=path>/tmp/a.txt</parameter></function></tool_call>"
            });
        }

        SawRetryInstruction = request.Messages.Any(message =>
            string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase) &&
            (message.Content ?? string.Empty).Contains("Reissue the same action using structured tool_calls only.", StringComparison.Ordinal));

        return Task.FromResult(new LLMResponse
        {
            Content = "Reasoning-only pseudo call was rejected safely."
        });
    }
}
