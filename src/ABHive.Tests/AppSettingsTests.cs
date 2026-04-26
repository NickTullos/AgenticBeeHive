using ABHive;

namespace ABHive.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Constructor_WithValidValues_SetsProperties()
    {
        var settings = new AppSettings(
            "http://localhost:1234",
            "gpt-4",
            "./workflowtypes/chat",
            "./logs/metrics.json",
            60000
        );

        Assert.Equal("http://localhost:1234", settings.LmStudioUrl);
        Assert.Equal("gpt-4", settings.ModelName);
        Assert.Equal("./workflowtypes/chat", settings.StepsDirectory);
        Assert.Equal("./logs/metrics.json", settings.LogFilePath);
        Assert.Equal(60000, settings.DefaultToolTimeoutMs);
        Assert.Equal(0, settings.LlmInactivityTimeoutMs);
    }

    [Fact]
    public void Constructor_WithEmptyValues_AppliesDefaults()
    {
        var settings = new AppSettings();

        Assert.Equal("http://localhost:1234", settings.LmStudioUrl);
        Assert.Equal("default", settings.ModelName);
        Assert.Equal("./workflowtypes/chat", settings.StepsDirectory);
        Assert.Equal("./logs/metrics.json", settings.LogFilePath);
        Assert.Equal(180000, settings.DefaultToolTimeoutMs);
        Assert.Equal(0, settings.LlmInactivityTimeoutMs);
    }

    [Fact]
    public void Constructor_WithInvalidUrl_DefaultsToHttpLocalhost()
    {
        var settings = new AppSettings(
            "invalid-url",
            "test-model",
            "./workflowtypes/chat",
            "./logs/metrics.json",
            30000
        );

        Assert.Equal("http://localhost:1234", settings.LmStudioUrl);
    }

    [Fact]
    public void Constructor_WithEmptyModelName_DefaultsToDefault()
    {
        var settings = new AppSettings(
            "http://localhost:1234",
            "",
            "./workflowtypes/chat",
            "./logs/metrics.json",
            30000
        );

        Assert.Equal("default", settings.ModelName);
    }

    [Fact]
    public void Constructor_WithNullModelName_DefaultsToDefault()
    {
        var settings = new AppSettings(
            "http://localhost:1234",
            null!,
            "./workflowtypes/chat",
            "./logs/metrics.json",
            30000
        );

        Assert.Equal("default", settings.ModelName);
    }

    [Fact]
    public void Constructor_WithNegativeTimeout_DefaultsTo180000()
    {
        var settings = new AppSettings(
            "http://localhost:1234",
            "test-model",
            "./workflowtypes/chat",
            "./logs/metrics.json",
            -1
        );

        Assert.Equal(180000, settings.DefaultToolTimeoutMs);
    }

    [Fact]
    public void Constructor_WithZeroTimeout_DefaultsTo180000()
    {
        var settings = new AppSettings(
            "http://localhost:1234",
            "test-model",
            "./workflowtypes/chat",
            "./logs/metrics.json",
            0
        );

        Assert.Equal(180000, settings.DefaultToolTimeoutMs);
    }

    [Fact]
    public void ValidateAndSet_WithZeroLlmInactivityTimeout_PreservesDisabledTimeout()
    {
        var settings = new AppSettings
        {
            LlmInactivityTimeoutMs = 0
        };

        settings.ValidateAndSet();

        Assert.Equal(0, settings.LlmInactivityTimeoutMs);
    }

    [Fact]
    public void ValidateAndSet_WithNegativeLlmInactivityTimeout_DefaultsToDisabledTimeout()
    {
        var settings = new AppSettings
        {
            LlmInactivityTimeoutMs = -1
        };

        settings.ValidateAndSet();

        Assert.Equal(0, settings.LlmInactivityTimeoutMs);
    }

    [Fact]
    public void Constructor_WithHttpsUrl_Accepts()
    {
        var settings = new AppSettings(
            "https://api.example.com",
            "test-model",
            "./workflowtypes/chat",
            "./logs/metrics.json",
            30000
        );

        Assert.Equal("https://api.example.com", settings.LmStudioUrl);
    }

    [Fact]
    public void Constructor_WithEmptyStepsDirectory_DefaultsToWorkflowTypePath()
    {
        var settings = new AppSettings(
            "http://localhost:1234",
            "test-model",
            "",
            "./logs/metrics.json",
            30000
        );

        Assert.Equal("./workflowtypes/chat", settings.StepsDirectory);
    }

    [Fact]
    public void Constructor_WithEmptyLogFilePath_DefaultsToMetricsJson()
    {
        var settings = new AppSettings(
            "http://localhost:1234",
            "test-model",
            "./workflowtypes/chat",
            "",
            30000
        );

        Assert.Equal("./logs/metrics.json", settings.LogFilePath);
    }
}
