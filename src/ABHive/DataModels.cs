using System.Text.Json.Serialization;

namespace ABHive;

public class LLMRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "default";
    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = new();
    [JsonPropertyName("tools")]
    public List<ToolDefinition> Tools { get; set; } = new();
    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.7;
    [JsonPropertyName("top_p")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TopP { get; set; } = null;
    [JsonPropertyName("top_k")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TopK { get; set; } = null;
    [JsonPropertyName("max_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; set; } = null;
    [JsonPropertyName("frequency_penalty")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? FrequencyPenalty { get; set; } = null;
    [JsonPropertyName("presence_penalty")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? PresencePenalty { get; set; } = null;
    [JsonPropertyName("stop")]
    public string[] StopSequences { get; set; } = Array.Empty<string>();
    [JsonPropertyName("stream")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Stream { get; set; }
}

public class LLMResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("object")]
    public string Object { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("created")]
    public int Created { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("model")]
    public string Model { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("choices")]
    public List<Choice> Choices { get; set; } = new();
    
    [System.Text.Json.Serialization.JsonPropertyName("usage")]
    public TokenUsage Usage { get; set; } = new();

    public string Content { 
        get => Choices.Count > 0 ? Choices[0].Message.Content : ""; 
        set { 
            if (Choices.Count == 0) Choices.Add(new Choice()); 
            Choices[0].Message.Content = value; 
        } 
    }
    
    public List<ToolCall> ToolCalls { 
        get => Choices.Count > 0 ? Choices[0].Message.ToolCalls : new List<ToolCall>(); 
        set { 
            if (Choices.Count == 0) Choices.Add(new Choice()); 
            Choices[0].Message.ToolCalls = value; 
        } 
    }

    public string ReasoningContent
    {
        get => Choices.Count > 0 ? Choices[0].Message.ReasoningContent : "";
        set
        {
            if (Choices.Count == 0) Choices.Add(new Choice());
            Choices[0].Message.ReasoningContent = value;
        }
    }
}

public class Choice
{
    [System.Text.Json.Serialization.JsonPropertyName("index")]
    public int Index { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("message")]
    public Message Message { get; set; } = new();
    
    [System.Text.Json.Serialization.JsonPropertyName("finish_reason")]
    public string FinishReason { get; set; } = "";
}

public class Message
{
    [System.Text.Json.Serialization.JsonPropertyName("role")]
    public string Role { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("content")]
    public string Content { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("tool_calls")]
    public List<ToolCall> ToolCalls { get; set; } = new();
    
    [System.Text.Json.Serialization.JsonPropertyName("reasoning_content")]
    public string ReasoningContent { get; set; } = "";
}

public class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public string? Content { get; set; } = "";

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; set; }

    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ToolCall>? ToolCalls { get; set; }
}

public enum MessageRole { System, User, Assistant, Tool }

public class ToolFunction
{
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "";
}

public class ToolCall
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string Type { get; set; } = "function";
    
    [System.Text.Json.Serialization.JsonPropertyName("function")]
    public ToolFunction Function { get; set; } = new();
    
    // Convenience properties for backward compatibility
    public string Name {
        get => Function?.Name ?? "";
        set {
            if (Function == null) Function = new();
            Function.Name = value;
        }
    }
    
    public string Arguments {
        get => Function?.Arguments ?? "";
        set {
            if (Function == null) Function = new();
            Function.Arguments = value;
        }
    }
}

public class ToolResult
{
    public string ToolId { get; set; } = "";
    public string ToolName { get; set; } = "";
    public string RequestSummary { get; set; } = "";
    public bool Success { get; set; }
    public string Output { get; set; } = "";
    public string Error { get; set; } = "";
}

public class TokenUsage
{
    [System.Text.Json.Serialization.JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}

public class ToolDefinition
{
    public string Type { get; set; } = "function";
    public FunctionObject Function { get; set; } = new();
}

public class FunctionObject
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public Dictionary<string, object> Parameters { get; set; } = new();
}

public partial class Step
{
    public string FilePath { get; set; } = "";
    public string Content { get; set; } = "";
    public int Order { get; set; }
    public string Id => Guid.NewGuid().ToString();
    public StepMetadata Metadata { get; set; } = new();
}

public class StepExecutionResult
{
    public string StepId { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public long DurationMs { get; set; }
    public bool Success { get; set; }
    public LLMResponse? LLMResponse { get; set; }
    public List<ToolResult> ToolResults { get; set; } = new();
    public string Error { get; set; } = "";
    public List<string> RequestMessagesContent { get; set; } = new();
    public string ResponseContent { get; set; } = "";
    public string? UserQuestion { get; set; }
    public bool ToolResultsShown { get; set; }
    public bool ToolResultsPublishedLive { get; set; }
    public List<ChatMessage> StepContext { get; set; } = new();
    public TicketIterationProgress? TicketProgress { get; set; }
}

public class WorkflowMetrics
{
    public int TotalSteps { get; set; }
    public int SuccessfulSteps { get; set; }
    public int FailedSteps { get; set; }
    public long TotalDurationMs { get; set; }
    public double AverageStepDurationMs { get; set; }
    public int TotalTokensUsed { get; set; }
    public bool ResumeAtStep { get; set; }
    public int ResumeStepNumber { get; set; }
    public TicketIterationProgress? TicketProgress { get; set; }
}

public class CachedToolResult
{
    public string CacheKey { get; set; } = "";
    public ToolResult Result { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class ToolConfig
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
}

public class StepMetadata
{
    [JsonPropertyName("executionMode")]
    public string ExecutionMode { get; set; } = "standard";

    [JsonPropertyName("ticketSource")]
    public string TicketSource { get; set; } = "{{TICKETS_DIR}}/tickets.json";

    [JsonPropertyName("completedSource")]
    public string CompletedSource { get; set; } = "{{TICKETS_DIR}}/completed.json";

    [JsonPropertyName("maxRetriesPerTicket")]
    public int MaxRetriesPerTicket { get; set; } = 3;
}

public class TicketIterationProgress
{
    public bool IsTicketIterationStep { get; set; }
    public string StepKey { get; set; } = "";
    public string StepName { get; set; } = "";
    public string TicketId { get; set; } = "";
    public string TicketTitle { get; set; } = "";
    public int Attempt { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public int TotalTickets { get; set; }
    public int CompletedTickets { get; set; }
    public int RemainingTickets { get; set; }
    public bool RetryExhausted { get; set; }
    public string Status { get; set; } = "";
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<ChatMessage> ContextMessages { get; set; } = new();
}

public class TicketIterationAuditEntry
{
    public string StepKey { get; set; } = "";
    public string StepName { get; set; } = "";
    public string TicketId { get; set; } = "";
    public string TicketTitle { get; set; } = "";
    public int AttemptsUsed { get; set; }
    public string Status { get; set; } = "";
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<ChatMessage> Messages { get; set; } = new();
}

public class StepConfig
{
    [System.Text.Json.Serialization.JsonPropertyName("autoContinue")]
    public bool AutoContinue { get; set; } = false;
    
    [System.Text.Json.Serialization.JsonPropertyName("AutoContinue")]
    public bool AutoContinuePascal { get; set; }
}

public partial class Step
{
    public StepConfig? Config { get; set; }
}
