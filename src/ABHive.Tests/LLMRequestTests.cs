using System.Text.Json;
using System.Text.Json.Serialization;
using ABHive;

namespace ABHive.Tests;

public class LLMRequestTests
{
    [Fact]
    public void LLMRequest_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var request = new LLMRequest();

        // Assert
        Assert.Equal("default", request.Model);
        Assert.Equal(0.7, request.Temperature);
        Assert.Equal(1.0, request.TopP);
        Assert.Equal(0, request.TopK);
        Assert.Equal(0, request.MaxTokens);
        Assert.Equal(0.0, request.FrequencyPenalty);
        Assert.Equal(0.0, request.PresencePenalty);
        Assert.Empty(request.StopSequences!);
        Assert.False(request.Stream);
    }

    [Fact]
    public void LLMRequest_Serializes_GenerationParameters_WithCorrectJsonPropertyNames()
    {
        // Arrange
        var request = new LLMRequest
        {
            Model = "test-model",
            Temperature = 0.8,
            TopP = 0.9,
            TopK = 40,
            MaxTokens = 1024,
            FrequencyPenalty = 0.5,
            PresencePenalty = -0.3,
            StopSequences = new[] { "\n", "USER:" },
            Messages = new List<ChatMessage> { new ChatMessage { Role = "user", Content = "hello" } }
        };

        // Act
        var json = JsonSerializer.Serialize(request);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Assert
        Assert.True(root.TryGetProperty("temperature", out _));
        Assert.True(root.TryGetProperty("top_p", out _));
        Assert.True(root.TryGetProperty("top_k", out _));
        Assert.True(root.TryGetProperty("max_tokens", out _));
        Assert.True(root.TryGetProperty("frequency_penalty", out _));
        Assert.True(root.TryGetProperty("presence_penalty", out _));
        Assert.True(root.TryGetProperty("stop", out _));

        Assert.Equal(0.8, root.GetProperty("temperature").GetDouble());
        Assert.Equal(0.9, root.GetProperty("top_p").GetDouble());
        Assert.Equal(40, root.GetProperty("top_k").GetInt32());
        Assert.Equal(1024, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal(0.5, root.GetProperty("frequency_penalty").GetDouble());
        Assert.Equal(-0.3, root.GetProperty("presence_penalty").GetDouble());

        var stopArray = root.GetProperty("stop");
        Assert.True(stopArray.ValueKind == JsonValueKind.Array);
        Assert.Equal(2, stopArray.GetArrayLength());
        Assert.Equal("\n", stopArray[0].GetString());
        Assert.Equal("USER:", stopArray[1].GetString());
    }

    [Fact]
    public void LLMRequest_StopSequences_SerializesAsArray_NotCommaSeparatedString()
    {
        // Arrange
        var request = new LLMRequest
        {
            StopSequences = new[] { "stop1", "stop2", "stop3" }
        };

        // Act
        var json = JsonSerializer.Serialize(request);
        var doc = JsonDocument.Parse(json);
        var stopElement = doc.RootElement.GetProperty("stop");

        // Assert
        Assert.Equal(JsonValueKind.Array, stopElement.ValueKind);
        Assert.Equal(3, stopElement.GetArrayLength());
    }

    [Fact]
    public void LLMRequest_Deserializes_GenerationParameters_Correctly()
    {
        // Arrange
        var json = @"{
            ""model"": ""test-model"",
            ""temperature"": 0.75,
            ""top_p"": 0.95,
            ""top_k"": 50,
            ""max_tokens"": 2048,
            ""frequency_penalty"": 1.0,
            ""presence_penalty"": -1.5,
            ""stop"": [""END"", ""\n""],
            ""messages"": []
        }";

        // Act
        var request = JsonSerializer.Deserialize<LLMRequest>(json);

        // Assert
        Assert.NotNull(request);
        Assert.Equal(0.75, request.Temperature);
        Assert.Equal(0.95, request.TopP);
        Assert.Equal(50, request.TopK);
        Assert.Equal(2048, request.MaxTokens);
        Assert.Equal(1.0, request.FrequencyPenalty);
        Assert.Equal(-1.5, request.PresencePenalty);
        Assert.Equal(2, request.StopSequences!.Length);
        Assert.Equal("END", request.StopSequences[0]);
        Assert.Equal("\n", request.StopSequences[1]);
    }

    [Fact]
    public void LLMRequest_ExistingTemperature_Property_RemainsUnchanged()
    {
        // Arrange & Act
        var request = new LLMRequest();

        // Assert
        Assert.Equal(0.7, request.Temperature);
        request.Temperature = 1.5;
        Assert.Equal(1.5, request.Temperature);
    }

    [Fact]
    public void LLMRequest_AllSevenGenerationParameters_Present()
    {
        // Arrange
        var request = new LLMRequest();

        // Act - verify all 7 parameters exist and are accessible
        var temperature = request.Temperature;
        var topP = request.TopP;
        var topK = request.TopK;
        var maxTokens = request.MaxTokens;
        var frequencyPenalty = request.FrequencyPenalty;
        var presencePenalty = request.PresencePenalty;
        var stopSequences = request.StopSequences;

        // Assert
        Assert.Equal(0.7, temperature);
        Assert.Equal(1.0, topP);
        Assert.Equal(0, topK);
        Assert.Equal(0, maxTokens);
        Assert.Equal(0.0, frequencyPenalty);
        Assert.Equal(0.0, presencePenalty);
        Assert.NotNull(stopSequences);
        Assert.Empty(stopSequences);
    }
}
