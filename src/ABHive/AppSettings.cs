using System.Diagnostics;

namespace ABHive;

public class AppSettings
{
    private const string DefaultLmStudioUrl = "http://localhost:1234";
    private const string DefaultModelName = "default";
    private const string DefaultStepsDirectory = "./workflowtypes/chat";
    private const string DefaultWorkflowTypesDirectory = "./workflowtypes";
    private const string DefaultLogFilePath = "./logs/metrics.json";
    private const int DefaultToolTimeoutMsValue = 180000;
    private const int DefaultLlmInactivityTimeoutMsValue = 0;
    private const int DefaultTelegramPollTimeoutSeconds = 20;
    private const int DefaultTelegramSwitchContextMessageCount = 5;
    private const int MaxTelegramSwitchContextMessageCount = 25;
    private const string DefaultCurrentVersion = "0.0.0";

    // LLM Generation Parameter Defaults
    private const double DefaultLlmTemperature = 0.7;
    private const double DefaultLlmTopP = 1.0;
    private const int DefaultLlmTopK = 0;
    private const int DefaultLlmMaxTokens = 0;
    private const double DefaultLlmFrequencyPenalty = 0.0;
    private const double DefaultLlmPresencePenalty = 0.0;
    private const string DefaultLlmStopSequences = "";

    public string LmStudioUrl { get; set; }
    public string LlmApiKey { get; set; }
    public string ModelName { get; set; }
    public List<LlmServerSettings> LlmServers { get; set; }
    public string ActiveLlmServerId { get; set; }
    public string ActiveLlmModelId { get; set; }
    public string StepsDirectory { get; set; }
    public string WorkflowTypesDirectory { get; set; }
    public string LogFilePath { get; set; }
    public int DefaultToolTimeoutMs { get; set; }
    public int LlmInactivityTimeoutMs { get; set; }
    public bool TelegramEnabled { get; set; }
    public string TelegramBotToken { get; set; }
    public long TelegramChatId { get; set; }
    public int TelegramPollTimeoutSeconds { get; set; }
    public int TelegramSwitchContextMessageCount { get; set; }
    public string ProjectRootDirectory { get; set; }
    public string SelectedProjectName { get; set; }
    public string SelectedProjectDirectory { get; set; }
    public string CurrentVersion { get; set; }
    public Dictionary<string, ToolConfig> ToolConfigs { get; set; }

    // LLM Generation Parameters
    public double LlmTemperature { get; set; }
    public double LlmTopP { get; set; }
    public int LlmTopK { get; set; }
    public int LlmMaxTokens { get; set; }
    public double LlmFrequencyPenalty { get; set; }
    public double LlmPresencePenalty { get; set; }
    public string LlmStopSequences { get; set; }

    public AppSettings()
    {
        LmStudioUrl = DefaultLmStudioUrl;
        LlmApiKey = "";
        ModelName = DefaultModelName;
        LlmServers = new List<LlmServerSettings>();
        ActiveLlmServerId = "";
        ActiveLlmModelId = "";
        StepsDirectory = DefaultStepsDirectory;
        WorkflowTypesDirectory = DefaultWorkflowTypesDirectory;
        LogFilePath = DefaultLogFilePath;
        DefaultToolTimeoutMs = DefaultToolTimeoutMsValue;
        LlmInactivityTimeoutMs = DefaultLlmInactivityTimeoutMsValue;
        TelegramEnabled = false;
        TelegramBotToken = "";
        TelegramChatId = 0;
        TelegramPollTimeoutSeconds = DefaultTelegramPollTimeoutSeconds;
        TelegramSwitchContextMessageCount = DefaultTelegramSwitchContextMessageCount;
        ProjectRootDirectory = "./projects";
        SelectedProjectName = "";
        SelectedProjectDirectory = "";
        CurrentVersion = DefaultCurrentVersion;
        
        // LLM Generation Parameter defaults
        LlmTemperature = DefaultLlmTemperature;
        LlmTopP = DefaultLlmTopP;
        LlmTopK = DefaultLlmTopK;
        LlmMaxTokens = DefaultLlmMaxTokens;
        LlmFrequencyPenalty = DefaultLlmFrequencyPenalty;
        LlmPresencePenalty = DefaultLlmPresencePenalty;
        LlmStopSequences = DefaultLlmStopSequences;
        
        ToolConfigs = new Dictionary<string, ToolConfig>
        {
            ["Bash"] = new ToolConfig { Name = "Bash", Enabled = true },
            ["WebFetch"] = new ToolConfig { Name = "WebFetch", Enabled = true },
            ["ReadFile"] = new ToolConfig { Name = "ReadFile", Enabled = false },
            ["WriteFile"] = new ToolConfig { Name = "WriteFile", Enabled = false }
        };
    }

    public AppSettings(string lmStudioUrl, string modelName, string stepsDirectory, string logFilePath, int defaultToolTimeoutMs)
    {
        LmStudioUrl = IsValidUrl(lmStudioUrl) ? lmStudioUrl : DefaultLmStudioUrl;
        LlmApiKey = "";
        ModelName = string.IsNullOrEmpty(modelName) ? DefaultModelName : modelName;
        LlmServers = new List<LlmServerSettings>();
        ActiveLlmServerId = "";
        ActiveLlmModelId = "";
        StepsDirectory = string.IsNullOrEmpty(stepsDirectory) ? DefaultStepsDirectory : stepsDirectory;
        WorkflowTypesDirectory = DefaultWorkflowTypesDirectory;
        LogFilePath = string.IsNullOrEmpty(logFilePath) ? DefaultLogFilePath : logFilePath;
        DefaultToolTimeoutMs = defaultToolTimeoutMs <= 0 ? DefaultToolTimeoutMsValue : defaultToolTimeoutMs;
        LlmInactivityTimeoutMs = DefaultLlmInactivityTimeoutMsValue;
        TelegramEnabled = false;
        TelegramBotToken = "";
        TelegramChatId = 0;
        TelegramPollTimeoutSeconds = DefaultTelegramPollTimeoutSeconds;
        TelegramSwitchContextMessageCount = DefaultTelegramSwitchContextMessageCount;
        ProjectRootDirectory = "./projects";
        SelectedProjectName = "";
        SelectedProjectDirectory = "";
        CurrentVersion = DefaultCurrentVersion;
        
        // LLM Generation Parameter defaults
        LlmTemperature = DefaultLlmTemperature;
        LlmTopP = DefaultLlmTopP;
        LlmTopK = DefaultLlmTopK;
        LlmMaxTokens = DefaultLlmMaxTokens;
        LlmFrequencyPenalty = DefaultLlmFrequencyPenalty;
        LlmPresencePenalty = DefaultLlmPresencePenalty;
        LlmStopSequences = DefaultLlmStopSequences;
        
        ToolConfigs = new Dictionary<string, ToolConfig>
        {
            ["Bash"] = new ToolConfig { Name = "Bash", Enabled = true },
            ["WebFetch"] = new ToolConfig { Name = "WebFetch", Enabled = true },
            ["ReadFile"] = new ToolConfig { Name = "ReadFile", Enabled = false },
            ["WriteFile"] = new ToolConfig { Name = "WriteFile", Enabled = false }
        };
    }

    public void ValidateAndSet()
    {
        var errors = new List<string>();

        if (!IsValidUrl(LmStudioUrl))
            errors.Add($"LmStudioUrl must be a valid HTTP/HTTPS URL. Provided: '{LmStudioUrl}'. Defaulting to '{DefaultLmStudioUrl}'.");
        
        if (string.IsNullOrWhiteSpace(ModelName))
            errors.Add($"ModelName cannot be null or empty. Defaulting to '{DefaultModelName}'.");
        
        if (string.IsNullOrEmpty(StepsDirectory))
            StepsDirectory = DefaultStepsDirectory;

        if (string.IsNullOrEmpty(WorkflowTypesDirectory))
            WorkflowTypesDirectory = DefaultWorkflowTypesDirectory;
        
        if (string.IsNullOrEmpty(LogFilePath))
            LogFilePath = DefaultLogFilePath;

        if (DefaultToolTimeoutMs <= 0)
            errors.Add($"DefaultToolTimeoutMs must be positive. Defaulting to {DefaultToolTimeoutMsValue}ms.");

        if (LlmInactivityTimeoutMs < 0)
            errors.Add($"LlmInactivityTimeoutMs cannot be negative. Defaulting to {DefaultLlmInactivityTimeoutMsValue}ms.");

        if (TelegramEnabled)
        {
            if (string.IsNullOrWhiteSpace(TelegramBotToken))
                errors.Add("TelegramBotToken cannot be empty when TelegramEnabled is true.");

            if (TelegramChatId == 0)
                errors.Add("TelegramChatId must be configured when TelegramEnabled is true.");
        }

        // LLM Generation Parameter Validation
        if (LlmTemperature < 0 || LlmTemperature > 2)
            errors.Add($"LlmTemperature must be between 0 and 2. Provided: {LlmTemperature}. Defaulting to {DefaultLlmTemperature}.");

        if (LlmTopP < 0 || LlmTopP > 1)
            errors.Add($"LlmTopP must be between 0 and 1. Provided: {LlmTopP}. Defaulting to {DefaultLlmTopP}.");

        if (LlmTopK < 0)
            errors.Add($"LlmTopK must be >= 0. Provided: {LlmTopK}. Defaulting to {DefaultLlmTopK}.");

        if (LlmMaxTokens < 0)
            errors.Add($"LlmMaxTokens must be >= 0. Provided: {LlmMaxTokens}. Defaulting to {DefaultLlmMaxTokens}.");

        if (LlmFrequencyPenalty < -2 || LlmFrequencyPenalty > 2)
            errors.Add($"LlmFrequencyPenalty must be between -2 and 2. Provided: {LlmFrequencyPenalty}. Defaulting to {DefaultLlmFrequencyPenalty}.");

        if (LlmPresencePenalty < -2 || LlmPresencePenalty > 2)
            errors.Add($"LlmPresencePenalty must be between -2 and 2. Provided: {LlmPresencePenalty}. Defaulting to {DefaultLlmPresencePenalty}.");

        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                Console.WriteLine($"[AppSettings] {error}");
            }
        }

        LmStudioUrl = IsValidUrl(LmStudioUrl) ? LmStudioUrl : DefaultLmStudioUrl;
        LlmApiKey = LlmApiKey?.Trim() ?? "";
        ModelName = string.IsNullOrWhiteSpace(ModelName) ? DefaultModelName : ModelName.Trim();
        ActiveLlmServerId = ActiveLlmServerId?.Trim() ?? "";
        ActiveLlmModelId = ActiveLlmModelId?.Trim() ?? "";
        LlmServers = LlmServers ?? new List<LlmServerSettings>();
        NormalizeLlmServers();
        SyncLegacyLlmFieldsFromActiveSelection();
        StepsDirectory = string.IsNullOrEmpty(StepsDirectory) ? DefaultStepsDirectory : StepsDirectory;
        WorkflowTypesDirectory = string.IsNullOrEmpty(WorkflowTypesDirectory) ? DefaultWorkflowTypesDirectory : WorkflowTypesDirectory;
        LogFilePath = string.IsNullOrEmpty(LogFilePath) ? DefaultLogFilePath : LogFilePath;
        DefaultToolTimeoutMs = DefaultToolTimeoutMs > 0 ? DefaultToolTimeoutMs : DefaultToolTimeoutMsValue;
        LlmInactivityTimeoutMs = LlmInactivityTimeoutMs >= 0 ? LlmInactivityTimeoutMs : DefaultLlmInactivityTimeoutMsValue;
        TelegramBotToken = TelegramBotToken?.Trim() ?? "";
        ProjectRootDirectory = string.IsNullOrEmpty(ProjectRootDirectory) ? "./projects" : ProjectRootDirectory.Trim();
        SelectedProjectName = SelectedProjectName?.Trim() ?? "";
        SelectedProjectDirectory = SelectedProjectDirectory?.Trim() ?? "";
        CurrentVersion = string.IsNullOrWhiteSpace(CurrentVersion) ? DefaultCurrentVersion : CurrentVersion.Trim();
        TelegramPollTimeoutSeconds = TelegramPollTimeoutSeconds > 0 ? TelegramPollTimeoutSeconds : DefaultTelegramPollTimeoutSeconds;
        TelegramSwitchContextMessageCount = Clamp(
            TelegramSwitchContextMessageCount,
            0,
            MaxTelegramSwitchContextMessageCount,
            DefaultTelegramSwitchContextMessageCount);
        TelegramEnabled = TelegramEnabled && !string.IsNullOrWhiteSpace(TelegramBotToken) && TelegramChatId != 0;

        // Clamp LLM Generation Parameters to valid ranges
        LlmTemperature = ClampDouble(LlmTemperature, 0, 2, DefaultLlmTemperature);
        LlmTopP = ClampDouble(LlmTopP, 0, 1, DefaultLlmTopP);
        LlmTopK = LlmTopK >= 0 ? LlmTopK : DefaultLlmTopK;
        LlmMaxTokens = LlmMaxTokens >= 0 ? LlmMaxTokens : DefaultLlmMaxTokens;
        LlmFrequencyPenalty = ClampDouble(LlmFrequencyPenalty, -2, 2, DefaultLlmFrequencyPenalty);
        LlmPresencePenalty = ClampDouble(LlmPresencePenalty, -2, 2, DefaultLlmPresencePenalty);
        LlmStopSequences = LlmStopSequences ?? DefaultLlmStopSequences;
    }

    private static int Clamp(int value, int min, int max, int defaultValue)
    {
        if (value < min || value > max)
        {
            return defaultValue;
        }

        return value;
    }

    private static double ClampDouble(double value, double min, double max, double defaultValue)
    {
        if (value < min || value > max)
        {
            return defaultValue;
        }

        return value;
    }

    public bool TrySetActiveLlmSelection(string serverId, string modelId, out string error)
    {
        error = "";
        serverId = serverId?.Trim() ?? "";
        modelId = modelId?.Trim() ?? "";

        var server = LlmServers.FirstOrDefault(item => string.Equals(item.Id, serverId, StringComparison.Ordinal));
        if (server == null)
        {
            error = "Selected server was not found.";
            return false;
        }

        var model = server.Models.FirstOrDefault(item => string.Equals(item.Id, modelId, StringComparison.Ordinal));
        if (model == null)
        {
            error = "Selected model was not found.";
            return false;
        }

        ActiveLlmServerId = server.Id;
        ActiveLlmModelId = model.Id;
        SyncLegacyLlmFieldsFromActiveSelection();
        return true;
    }

    public LlmServerSettings GetActiveLlmServer()
    {
        var server = LlmServers.FirstOrDefault(item => string.Equals(item.Id, ActiveLlmServerId, StringComparison.Ordinal));
        if (server != null)
        {
            return server;
        }

        return LlmServers.First();
    }

    public LlmModelSettings GetActiveLlmModel()
    {
        var server = GetActiveLlmServer();
        var model = server.Models.FirstOrDefault(item => string.Equals(item.Id, ActiveLlmModelId, StringComparison.Ordinal));
        if (model != null)
        {
            return model;
        }

        var defaultModel = server.Models.FirstOrDefault(item => string.Equals(item.Id, server.DefaultModelId, StringComparison.Ordinal));
        return defaultModel ?? server.Models.First();
    }

    public string GetActiveLlmServerUrl()
    {
        return GetActiveLlmServer().BaseUrl;
    }

    public string GetActiveLlmApiKey()
    {
        return GetActiveLlmServer().ApiKey;
    }

    public string GetActiveLlmModelName()
    {
        return GetActiveLlmModel().Name;
    }

    private void NormalizeLlmServers()
    {
        if (LlmServers.Count == 0)
        {
            LlmServers.Add(new LlmServerSettings
            {
                Id = "default-server",
                Name = "Default Server",
                BaseUrl = IsValidUrl(LmStudioUrl) ? LmStudioUrl : DefaultLmStudioUrl,
                ApiKey = LlmApiKey,
                Models = new List<LlmModelSettings>
                {
                    new()
                    {
                        Id = "default-model",
                        Name = string.IsNullOrWhiteSpace(ModelName) ? DefaultModelName : ModelName.Trim()
                    }
                },
                DefaultModelId = "default-model"
            });
        }

        for (var serverIndex = 0; serverIndex < LlmServers.Count; serverIndex++)
        {
            var server = LlmServers[serverIndex] ?? new LlmServerSettings();
            server.Id = string.IsNullOrWhiteSpace(server.Id) ? $"server-{serverIndex + 1}" : server.Id.Trim();
            server.Name = string.IsNullOrWhiteSpace(server.Name) ? server.Id : server.Name.Trim();
            server.BaseUrl = IsValidUrl(server.BaseUrl) ? server.BaseUrl.Trim() : DefaultLmStudioUrl;
            server.ApiKey = server.ApiKey?.Trim() ?? "";
            server.Models ??= new List<LlmModelSettings>();

            if (server.Models.Count == 0)
            {
                server.Models.Add(new LlmModelSettings
                {
                    Id = $"model-{serverIndex + 1}-1",
                    Name = string.IsNullOrWhiteSpace(ModelName) ? DefaultModelName : ModelName.Trim()
                });
            }

            for (var modelIndex = 0; modelIndex < server.Models.Count; modelIndex++)
            {
                var model = server.Models[modelIndex] ?? new LlmModelSettings();
                model.Id = string.IsNullOrWhiteSpace(model.Id) ? $"{server.Id}-model-{modelIndex + 1}" : model.Id.Trim();
                model.Name = string.IsNullOrWhiteSpace(model.Name) ? DefaultModelName : model.Name.Trim();
                server.Models[modelIndex] = model;
            }

            var distinctModels = server.Models
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            server.Models = distinctModels;

            if (!server.Models.Any(item => string.Equals(item.Id, server.DefaultModelId, StringComparison.Ordinal)))
            {
                server.DefaultModelId = server.Models[0].Id;
            }

            LlmServers[serverIndex] = server;
        }

        LlmServers = LlmServers
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        if (!LlmServers.Any(item => string.Equals(item.Id, ActiveLlmServerId, StringComparison.Ordinal)))
        {
            ActiveLlmServerId = LlmServers[0].Id;
        }

        var activeServer = LlmServers.First(item => string.Equals(item.Id, ActiveLlmServerId, StringComparison.Ordinal));
        if (!activeServer.Models.Any(item => string.Equals(item.Id, ActiveLlmModelId, StringComparison.Ordinal)))
        {
            ActiveLlmModelId = activeServer.DefaultModelId;
        }
    }

    private void SyncLegacyLlmFieldsFromActiveSelection()
    {
        var activeServer = GetActiveLlmServer();
        var activeModel = GetActiveLlmModel();
        LmStudioUrl = activeServer.BaseUrl;
        LlmApiKey = activeServer.ApiKey;
        ModelName = activeModel.Name;
    }

    private bool IsValidUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        
        return Uri.TryCreate(url, UriKind.Absolute, out var uriResult) &&
               (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
    }
}

public class LlmServerSettings
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public List<LlmModelSettings> Models { get; set; } = new();
    public string DefaultModelId { get; set; } = "";
}

public class LlmModelSettings
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}
