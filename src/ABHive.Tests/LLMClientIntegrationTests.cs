using ABHive.Application;
using ABHive.Infrastructure;

namespace ABHive.Tests;

public class LLMClientIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    
    public LLMClientIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"agentic-integration-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task GenerateAsync_WithNoServer_ThrowsException()
    {
        var settings = new AppSettings("http://localhost:9999", "test-model", _tempDir, "./logs/metrics.json", 30000);
        
        using var httpClient = new HttpClient();
        httpClient.BaseAddress = new Uri(settings.LmStudioUrl);
        
        var client = new LLMClient(httpClient, settings);
        var request = new LLMRequest 
        { 
            Messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "user", Content = "Test" }
            },
            Tools = new List<ToolDefinition>()
        };

        await Assert.ThrowsAnyAsync<Exception>(() => client.GenerateAsync(request, ct: CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }
}
