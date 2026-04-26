using ABHive.Application;
using ABHive.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ABHive.Presentation;

public interface IConsoleOutputFormatter
{
    void ShowStepProgress(int current, int total, Step step);
    string? ShowStepSuccess(StepExecutionResult result, Step step, CancellationToken ct = default);
    void ShowStepFailure(StepExecutionResult result);
}

public class ConsoleOutputFormatter : ABHive.Application.IConsoleOutputFormatter, IConsoleOutputFormatter
{
    private readonly bool _debugMode;

    public ConsoleOutputFormatter(bool debugMode = false)
    {
        _debugMode = debugMode;
    }

    public void ShowStepProgress(int current, int total, Step step)
    {
        var filename = Path.GetFileName(step.FilePath);

        Console.WriteLine();
        Console.WriteLine(new string('=', 40));
        Console.WriteLine($" Step {current} of {total}: {filename}");
        Console.WriteLine(new string('=', 40));
        Console.WriteLine();
    }

    public string? ShowStepSuccess(StepExecutionResult result, Step step, CancellationToken ct = default)
    {
        Console.WriteLine("✓ Step completed successfully");

        if (!result.ToolResultsShown && result.ToolResults?.Count > 0)
        {
            foreach (var toolResult in result.ToolResults)
            {
                Console.WriteLine($"  [Tool] {toolResult.ToolId}");
                Console.WriteLine($"    Status: {(toolResult.Success ? "Success" : "Failed")}");
                if (!string.IsNullOrEmpty(toolResult.Output))
                {
                    var lines = toolResult.Output.Split('\n').Take(5);
                    foreach (var line in lines)
                    {
                        Console.WriteLine($"    Output: {line.Trim()}");
                    }
                }
            }

            result.ToolResultsShown = true;
        }

        if (result.LLMResponse?.Usage.TotalTokens > 0)
        {
            Console.WriteLine($"  Token Usage: {result.LLMResponse.Usage.TotalTokens} total");
        }
        
        // Display LLM response content
        if (!string.IsNullOrEmpty(result.LLMResponse?.Content))
        {
            Console.WriteLine();
            var lines = result.LLMResponse.Content.Split('\n');
            foreach (var line in lines)
            {
                Console.WriteLine(line);
            }
        }
        
        if (_debugMode && result.StepContext.Count > 0)
        {
            Console.WriteLine($"  [DEBUG] Step Context Size: {result.StepContext.Count} messages");
        }
        
        // Check if auto-continue is enabled via config
        if (step.Config?.AutoContinue == true)
        {
            Console.WriteLine("\n[AUTO] Auto-continue enabled, proceeding to next step...");
            return null;
        }

            // Prompt user to continue or ask a question
            Console.Write("\n[PAUSE] Press Enter to continue, or type your question for the agent: ");
            
            string? input;
            try
            {
                if (Console.IsInputRedirected)
                {
                    // For piped input, use Peek() to check for available data
                    if (Console.In.Peek() >= 0)
                    {
                        input = Console.ReadLine() ?? "";
                    }
                    else
                    {
                        // No more input available - simulate empty line to continue
                        Console.WriteLine("[CONTINUE] Proceeding to next step...");
                        input = "";
                    }
                }
                else
                {
                    // Normal console input
                    input = Console.ReadLine() ?? "";
                }
            }
            catch
            {
                // Fallback for any console access issues - treat as EOF
                input = "";
            }

            if (!string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine($"\n[ASKING] Your question will be sent to the agent...");
                return input.Trim();
            }
            else
            {
                Console.WriteLine("\n[CONTINUE] Proceeding to next step...");
                return null;
            }
        
    }

    public void ShowStepFailure(StepExecutionResult result)
    {
        Console.WriteLine("✗ Step failed");
        
        if (!string.IsNullOrEmpty(result.Error))
        {
            var lines = result.Error.Split('\n').Take(5);
            foreach (var line in lines)
            {
                Console.WriteLine($"  Error: {line.Trim()}");
            }
        }
        
        Console.WriteLine();
    }
}

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        
        // Suppress verbose logging from HttpClient unless debug mode is enabled
        bool isDebug = args.Contains("--debug");
        if (!isDebug)
        {
            builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft.AspNetCore.Http.Client", LogLevel.Warning);
        }

        // Read configuration from appsettings.json if it exists
        var configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        AppSettings settings;
        
        if (File.Exists(configPath))
        {
            var json = await File.ReadAllTextAsync(configPath);
            
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            settings = new AppSettings();
            
            if (root.TryGetProperty("LmStudioUrl", out var lmUrl))
                settings.LmStudioUrl = lmUrl.GetString() ?? "http://localhost:1234";
            
            if (root.TryGetProperty("ModelName", out var modelName))
                settings.ModelName = modelName.GetString() ?? "default";
            
            if (root.TryGetProperty("StepsDirectory", out var stepsDir))
                settings.StepsDirectory = stepsDir.GetString() ?? "./workflowtypes/chat";

            if (root.TryGetProperty("WorkflowTypesDirectory", out var workflowTypesDir))
                settings.WorkflowTypesDirectory = workflowTypesDir.GetString() ?? "./workflowtypes";
            
            if (root.TryGetProperty("LogFilePath", out var logPath))
                settings.LogFilePath = logPath.GetString() ?? "./logs/metrics.json";
            
            if (root.TryGetProperty("DefaultToolTimeoutMs", out var timeout))
                settings.DefaultToolTimeoutMs = timeout.GetUInt32() == 0 ? 180000 : (int)timeout.GetUInt32();

            if (root.TryGetProperty("LlmInactivityTimeoutMs", out var llmTimeout))
                settings.LlmInactivityTimeoutMs = llmTimeout.GetInt32();
            
            if (root.TryGetProperty("ToolConfigs", out var toolConfigs))
            {
                var tools = new Dictionary<string, ToolConfig>();
                foreach (var prop in toolConfigs.EnumerateObject())
                {
                    var config = new ToolConfig
                    {
                        Name = prop.Name,
                        Enabled = prop.Value.GetProperty("Enabled").GetBoolean()
                    };
                    tools[prop.Name] = config;
                }
                settings.ToolConfigs = tools;
            }
        }
        else
        {
            settings = new AppSettings();
        }

        settings.ValidateAndSet();

        builder.Services.AddSingleton<AppSettings>(settings);
        builder.Services.AddSingleton<IToolCache, ToolCache>();

        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Http.Client", LogLevel.Warning);

        builder.Services.AddHttpClient("llm", client =>
        {
            client.BaseAddress = new Uri(settings.LmStudioUrl);
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        
        builder.Services.AddTransient<ILLMClient>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("llm");
            return new LLMClient(httpClient, settings, isDebug);
        });

        builder.Services.AddTransient<IToolExecutor, ToolExecutor>();
        builder.Services.AddTransient<IStepConversationService, StepConversationService>();
        
        builder.Services.AddTransient<ABHive.Application.IConsoleOutputFormatter>(sp => 
            new ConsoleOutputFormatter(isDebug));
        builder.Services.AddTransient<IConsoleOutputFormatter>(sp => 
            new ConsoleOutputFormatter(isDebug));
        
        builder.Services.AddTransient<IMetricsLogger, MetricsLogger>();
        
        builder.Services.AddTransient<IWorkflowOrchestrator>(sp =>
        {
            var settings = sp.GetRequiredService<AppSettings>();
            var llmClient = sp.GetRequiredService<ILLMClient>();
            var toolExecutor = sp.GetRequiredService<IToolExecutor>();
            var stepConversationService = sp.GetRequiredService<IStepConversationService>();
            var formatter = sp.GetRequiredService<ABHive.Application.IConsoleOutputFormatter>();
            var metricsLogger = sp.GetService<IMetricsLogger>();
            return new WorkflowOrchestrator(settings, llmClient, toolExecutor, stepConversationService, formatter, metricsLogger, isDebug);
        });

        var host = builder.Build();

        var orchestrator = host.Services.GetRequiredService<IWorkflowOrchestrator>();
        
        try
        {
            var metrics = await orchestrator.RunAsync();
            
            Console.WriteLine(new string('=', 40));
            Console.WriteLine(" WORKFLOW SUMMARY");
            Console.WriteLine(new string('=', 40));
            Console.WriteLine($"Total Steps: {metrics.TotalSteps}");
            Console.WriteLine($"Successful: {metrics.SuccessfulSteps}");
            Console.WriteLine($"Failed: {metrics.FailedSteps}");
            Console.WriteLine($"Average Duration: {metrics.AverageStepDurationMs:F2}ms");
            Console.WriteLine($"Total Tokens Used: {metrics.TotalTokensUsed}");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Workflow failed: {ex.Message}");
            Environment.Exit(1);
        }
    }
}
