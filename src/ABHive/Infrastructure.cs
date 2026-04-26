using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ABHive.Infrastructure;

public interface ILLMClient
{
    Task<LLMResponse> GenerateAsync(LLMRequest request, CancellationToken ct = default);
}

public class LLMClient : ILLMClient
{
    private readonly HttpClient _httpClient;
    private readonly AppSettings _settings;
    private readonly bool _debugMode;
    private const int MaxRetries = 3;
    private const int InitialDelayMs = 1000;

    public LLMClient(HttpClient httpClient, AppSettings settings, bool debugMode = false)
    {
        _httpClient = httpClient;
        _settings = settings;
        _debugMode = debugMode;
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        
        // Set BaseAddress if not already set
        if (_httpClient.BaseAddress == null || _httpClient.BaseAddress.OriginalString == "http://localhost:1234/")
        {
            _httpClient.BaseAddress = new Uri(_settings.LmStudioUrl);
        }
    }

    public async Task<LLMResponse> GenerateAsync(LLMRequest request, CancellationToken ct = default)
    {
        Exception? lastException = null;
        var delayMs = InitialDelayMs;
        var streamingRequest = new LLMRequest
        {
            Model = request.Model,
            Messages = request.Messages,
            Tools = request.Tools,
            Temperature = request.Temperature,
            TopP = request.TopP,
            TopK = request.TopK,
            FrequencyPenalty = request.FrequencyPenalty,
            PresencePenalty = request.PresencePenalty,
            StopSequences = request.StopSequences,
            Stream = true
        };

        // Only set MaxTokens if it's greater than 0
        // A value of 0 means "unlimited" and should not be sent to the API
        if (request.MaxTokens > 0)
        {
            streamingRequest.MaxTokens = request.MaxTokens;
        }

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var currentBaseUrl = _settings.GetActiveLlmServerUrl();
                if (!string.IsNullOrWhiteSpace(currentBaseUrl))
                {
                    var currentUri = new Uri(currentBaseUrl);
                    if (_httpClient.BaseAddress == null || _httpClient.BaseAddress != currentUri)
                    {
                        _httpClient.BaseAddress = currentUri;
                    }
                }

                var apiKey = _settings.GetActiveLlmApiKey()?.Trim() ?? string.Empty;
                _httpClient.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(apiKey)
                    ? null
                    : new AuthenticationHeaderValue("Bearer", apiKey);

                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
                {
                    Content = JsonContent.Create(streamingRequest)
                };
                using var response = await WithLlmInactivityTimeoutAsync(
                    () => _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, ct),
                    ct);

                using var cancelRegistration = ct.Register(() =>
                {
                    try
                    {
                        response.Dispose();
                    }
                    catch
                    {
                        // Ignore disposal errors on cancellation.
                    }
                });
                
                if (response.IsSuccessStatusCode)
                {
                    return await ReadLlmResponseAsync(response, ct);
                }
                
                if (IsTransientError(response.StatusCode))
                {
                    lastException = new HttpRequestException($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                    
                    if (attempt < MaxRetries)
                    {
                        if (_debugMode) Console.WriteLine($"[LLMClient] HTTP {(int)response.StatusCode} error. Retrying in {delayMs}ms... (Attempt {attempt}/{MaxRetries})");
                        await Task.Delay(delayMs, ct);
                        delayMs *= 2;
                    }
                }
                else
                {
                    var errorContent = await WithLlmInactivityTimeoutAsync(
                        () => response.Content.ReadAsStringAsync(ct),
                        ct);
                    if (_debugMode)
                        Console.WriteLine($"[LLMClient] Non-retryable error: {response.StatusCode} - {errorContent}");
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                }
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                
                if (attempt < MaxRetries && _debugMode)
                {
                    Console.WriteLine($"[LLMClient] Request failed. Retrying in {delayMs}ms... (Attempt {attempt}/{MaxRetries})");
                    await Task.Delay(delayMs, ct);
                    delayMs *= 2;
                }
            }
            catch (TimeoutException ex)
            {
                lastException = ex;

                if (attempt < MaxRetries)
                {
                    Console.WriteLine($"[LLMClient] LLM inactivity timeout after {FormatTimeout(_settings.LlmInactivityTimeoutMs)}. Retrying in {delayMs}ms... (Attempt {attempt}/{MaxRetries})");
                    await Task.Delay(delayMs, ct);
                    delayMs *= 2;
                }
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                lastException = ex;
                
                if (attempt < MaxRetries)
                {
                    Console.WriteLine($"[LLMClient] Request timeout. Retrying in {delayMs}ms... (Attempt {attempt}/{MaxRetries})");
                    await Task.Delay(delayMs, ct);
                    delayMs *= 2;
                }
            }
        }

        throw new InvalidOperationException($"Failed to get response from LLM after {MaxRetries} attempts", lastException);
    }

    private bool IsTransientError(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.ServiceUnavailable ||
               statusCode == HttpStatusCode.GatewayTimeout ||
               statusCode == HttpStatusCode.BadGateway ||
               (int)statusCode >= 500;
    }

    private async Task<LLMResponse> ReadLlmResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (IsStreamingResponse(response))
        {
            return await ReadStreamingLlmResponseAsync(response, ct);
        }

        var rawResponse = await WithLlmInactivityTimeoutAsync(
            () => response.Content.ReadAsStringAsync(ct),
            ct);

        if (_debugMode) Console.WriteLine($"[DEBUG] Raw Response Status: {response.StatusCode}");
        if (_debugMode) Console.WriteLine($"[DEBUG] Raw Response Content Length: {rawResponse.Length}");
        if (_debugMode && rawResponse.Length > 0)
            Console.WriteLine($"[DEBUG] Raw Response (first 1000 chars): {rawResponse.Substring(0, Math.Min(1000, rawResponse.Length))}");

        var llmResponse = JsonSerializer.Deserialize<LLMResponse>(rawResponse) ?? new LLMResponse();
        LogParsedResponse(llmResponse);
        return llmResponse;
    }

    private async Task<LLMResponse> ReadStreamingLlmResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await WithLlmInactivityTimeoutAsync(
            () => response.Content.ReadAsStreamAsync(ct),
            ct);
        using var reader = new StreamReader(stream);

        var accumulator = new StreamingResponseAccumulator();
        var eventCount = 0;
        var eventDataBuilder = new StringBuilder();
        var sawDone = false;

        bool TryProcessEventData(string rawEventData)
        {
            var payload = rawEventData.Trim();
            if (string.IsNullOrEmpty(payload))
            {
                return false;
            }

            if (string.Equals(payload, "[DONE]", StringComparison.Ordinal))
            {
                sawDone = true;
                return true;
            }

            try
            {
                eventCount++;
                accumulator.Apply(payload);
            }
            catch (JsonException ex)
            {
                if (_debugMode)
                {
                    Console.WriteLine($"[DEBUG] Ignoring malformed stream payload: {ex.Message}");
                    Console.WriteLine($"[DEBUG] Malformed payload: {payload}");
                }
            }

            return false;
        }

        void AppendEventDataLine(string line)
        {
            var dataPart = line.Length > 5 ? line[5..] : string.Empty;
            if (dataPart.StartsWith(" ", StringComparison.Ordinal))
            {
                dataPart = dataPart[1..];
            }

            if (eventDataBuilder.Length > 0)
            {
                eventDataBuilder.Append('\n');
            }

            eventDataBuilder.Append(dataPart);
        }

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await WithLlmInactivityTimeoutAsync(() => reader.ReadLineAsync(), ct);
            if (line == null)
            {
                if (eventDataBuilder.Length > 0)
                {
                    _ = TryProcessEventData(eventDataBuilder.ToString());
                    eventDataBuilder.Clear();
                }
                break;
            }

            if (line.Length == 0)
            {
                if (eventDataBuilder.Length == 0)
                {
                    continue;
                }

                var shouldStop = TryProcessEventData(eventDataBuilder.ToString());
                eventDataBuilder.Clear();
                if (shouldStop)
                {
                    break;
                }
                continue;
            }

            if (line.StartsWith(":", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                AppendEventDataLine(line);
            }
        }

        if (_debugMode) Console.WriteLine($"[DEBUG] Streamed {eventCount} LLM events");
        if (_debugMode && sawDone) Console.WriteLine("[DEBUG] Stream ended with [DONE] marker");
        var llmResponse = accumulator.ToResponse();
        LogParsedResponse(llmResponse);
        return llmResponse;
    }

    private void LogParsedResponse(LLMResponse llmResponse)
    {
        if (_debugMode) Console.WriteLine($"[DEBUG] Parsed Content Length: {llmResponse.Content.Length}");
        if (_debugMode) Console.WriteLine($"[DEBUG] Choices Count: {llmResponse.Choices.Count}");
        if (_debugMode) Console.WriteLine($"[DEBUG] LLM Response Content: {llmResponse.Content}");

        if (!llmResponse.ToolCalls.Any())
        {
            return;
        }

        foreach (var toolCall in llmResponse.ToolCalls)
        {
            string cleanArgs = "[]";
            try
            {
                if (!string.IsNullOrEmpty(toolCall.Arguments))
                {
                    int cmdStart = toolCall.Arguments.IndexOf("\"command\":", StringComparison.Ordinal) + 10;
                    if (cmdStart > 9)
                    {
                        int quoteStart = toolCall.Arguments.IndexOf('"', cmdStart) + 1;
                        int quoteEnd = toolCall.Arguments.LastIndexOf('"');
                        if (quoteStart > 0 && quoteEnd > quoteStart)
                            cleanArgs = toolCall.Arguments.Substring(quoteStart, quoteEnd - quoteStart);
                    }
                }
            }
            catch
            {
            }

            Console.WriteLine($"  [Tool Call] {toolCall.Name}: {cleanArgs}");
        }

        if (_debugMode) Console.WriteLine("[DEBUG] ToolCalls count: " + llmResponse.ToolCalls.Count);
        foreach (var toolCall in llmResponse.ToolCalls)
        {
            if (_debugMode) Console.WriteLine($"[DEBUG] Tool Call ID: '{toolCall.Id}'");
            if (_debugMode) Console.WriteLine($"[DEBUG] Tool Call Name: '{toolCall.Name}'");
            if (_debugMode) Console.WriteLine($"[DEBUG] Tool Call Arguments: '{toolCall.Arguments}'");
        }
    }

    private bool IsStreamingResponse(HttpResponseMessage response)
    {
        return string.Equals(
            response.Content.Headers.ContentType?.MediaType,
            "text/event-stream",
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task<T> WithLlmInactivityTimeoutAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        if (_settings.LlmInactivityTimeoutMs <= 0)
        {
            return await operation();
        }

        try
        {
            return await operation().WaitAsync(
                TimeSpan.FromMilliseconds(_settings.LlmInactivityTimeoutMs),
                ct);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"LLM response became inactive for {FormatTimeout(_settings.LlmInactivityTimeoutMs)}.", ex);
        }
    }

    private static string FormatTimeout(int timeoutMs)
    {
        if (timeoutMs % 1000 == 0)
        {
            return $"{timeoutMs / 1000} seconds";
        }

        return $"{timeoutMs} ms";
    }

    private sealed class StreamingResponseAccumulator
    {
        private readonly SortedDictionary<int, StreamingChoiceAccumulator> _choices = new();
        private string _id = "";
        private string _object = "";
        private string _model = "";
        private int _created;
        private TokenUsage _usage = new();

        public void Apply(string payload)
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.TryGetProperty("id", out var idProperty))
                _id = idProperty.GetString() ?? _id;

            if (root.TryGetProperty("object", out var objectProperty))
                _object = objectProperty.GetString() ?? _object;

            if (root.TryGetProperty("model", out var modelProperty))
                _model = modelProperty.GetString() ?? _model;

            if (root.TryGetProperty("created", out var createdProperty) && createdProperty.TryGetInt32(out var created))
                _created = created;

            if (root.TryGetProperty("usage", out var usageProperty) && usageProperty.ValueKind == JsonValueKind.Object)
                _usage = JsonSerializer.Deserialize<TokenUsage>(usageProperty.GetRawText()) ?? _usage;

            if (!root.TryGetProperty("choices", out var choicesProperty) || choicesProperty.ValueKind != JsonValueKind.Array)
                return;

            foreach (var choiceProperty in choicesProperty.EnumerateArray())
            {
                var choiceIndex = choiceProperty.TryGetProperty("index", out var indexProperty) && indexProperty.TryGetInt32(out var parsedIndex)
                    ? parsedIndex
                    : 0;

                if (!_choices.TryGetValue(choiceIndex, out var choice))
                {
                    choice = new StreamingChoiceAccumulator(choiceIndex);
                    _choices[choiceIndex] = choice;
                }

                choice.Apply(choiceProperty);
            }
        }

        public LLMResponse ToResponse()
        {
            var response = new LLMResponse
            {
                Id = _id,
                Object = string.IsNullOrWhiteSpace(_object) ? "chat.completion" : _object,
                Created = _created,
                Model = _model,
                Usage = _usage,
                Choices = _choices.Values.Select(choice => choice.ToChoice()).ToList()
            };

            if (response.Choices.Count == 0)
            {
                response.Choices.Add(new Choice());
            }

            return response;
        }
    }

    private sealed class StreamingChoiceAccumulator
    {
        private readonly SortedDictionary<int, StreamingToolCallAccumulator> _toolCalls = new();
        private readonly StringBuilder _content = new();
        private readonly StringBuilder _reasoning = new();
        private readonly int _index;
        private string _role = "assistant";
        private string _finishReason = "";

        public StreamingChoiceAccumulator(int index)
        {
            _index = index;
        }

        public void Apply(JsonElement choiceProperty)
        {
            if (choiceProperty.TryGetProperty("finish_reason", out var finishReasonProperty) &&
                finishReasonProperty.ValueKind == JsonValueKind.String)
            {
                _finishReason = finishReasonProperty.GetString() ?? _finishReason;
            }

            if (choiceProperty.TryGetProperty("message", out var messageProperty) && messageProperty.ValueKind == JsonValueKind.Object)
            {
                ApplyMessagePayload(messageProperty);
            }

            if (choiceProperty.TryGetProperty("delta", out var deltaProperty) && deltaProperty.ValueKind == JsonValueKind.Object)
            {
                ApplyMessagePayload(deltaProperty);
            }
        }

        public Choice ToChoice()
        {
            return new Choice
            {
                Index = _index,
                FinishReason = _finishReason,
                Message = new Message
                {
                    Role = _role,
                    Content = _content.ToString(),
                    ReasoningContent = _reasoning.ToString(),
                    ToolCalls = _toolCalls.Values.Select(toolCall => toolCall.ToToolCall()).ToList()
                }
            };
        }

        private void ApplyMessagePayload(JsonElement payload)
        {
            if (payload.TryGetProperty("role", out var roleProperty) && roleProperty.ValueKind == JsonValueKind.String)
                _role = roleProperty.GetString() ?? _role;

            if (payload.TryGetProperty("content", out var contentProperty) && contentProperty.ValueKind == JsonValueKind.String)
                _content.Append(contentProperty.GetString());

            if (payload.TryGetProperty("reasoning_content", out var reasoningProperty) && reasoningProperty.ValueKind == JsonValueKind.String)
                _reasoning.Append(reasoningProperty.GetString());

            if (!payload.TryGetProperty("tool_calls", out var toolCallsProperty) || toolCallsProperty.ValueKind != JsonValueKind.Array)
                return;

            foreach (var toolCallProperty in toolCallsProperty.EnumerateArray())
            {
                var toolIndex = toolCallProperty.TryGetProperty("index", out var indexProperty) && indexProperty.TryGetInt32(out var parsedIndex)
                    ? parsedIndex
                    : _toolCalls.Count;

                if (!_toolCalls.TryGetValue(toolIndex, out var toolCall))
                {
                    toolCall = new StreamingToolCallAccumulator();
                    _toolCalls[toolIndex] = toolCall;
                }

                toolCall.Apply(toolCallProperty);
            }
        }
    }

    private sealed class StreamingToolCallAccumulator
    {
        private readonly StringBuilder _name = new();
        private readonly StringBuilder _arguments = new();
        private string _id = "";
        private string _type = "function";

        public void Apply(JsonElement toolCallProperty)
        {
            if (toolCallProperty.TryGetProperty("id", out var idProperty) && idProperty.ValueKind == JsonValueKind.String)
                _id = idProperty.GetString() ?? _id;

            if (toolCallProperty.TryGetProperty("type", out var typeProperty) && typeProperty.ValueKind == JsonValueKind.String)
                _type = typeProperty.GetString() ?? _type;

            if (!toolCallProperty.TryGetProperty("function", out var functionProperty) || functionProperty.ValueKind != JsonValueKind.Object)
                return;

            if (functionProperty.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String)
                _name.Append(nameProperty.GetString());

            if (functionProperty.TryGetProperty("arguments", out var argumentsProperty) && argumentsProperty.ValueKind == JsonValueKind.String)
                _arguments.Append(argumentsProperty.GetString());
        }

        public ToolCall ToToolCall()
        {
            return new ToolCall
            {
                Id = string.IsNullOrWhiteSpace(_id) ? Guid.NewGuid().ToString() : _id,
                Type = _type,
                Name = _name.ToString(),
                Arguments = _arguments.ToString()
            };
        }
    }
}

public interface IToolExecutor
{
    Task<List<ToolResult>> ExecuteToolsAsync(List<ToolCall> toolCalls, CancellationToken ct = default);
}

public class ToolExecutor : IToolExecutor
{
    private readonly AppSettings _settings;
    private readonly IToolCache _cache;

    public ToolExecutor(AppSettings settings, IToolCache cache)
    {
        _settings = settings;
        _cache = cache;
    }

    public async Task<List<ToolResult>> ExecuteToolsAsync(List<ToolCall> toolCalls, CancellationToken ct = default)
    {
        var results = new List<ToolResult>();

        foreach (var toolCall in toolCalls)
        {
            try
            {
                var result = new ToolResult();

                switch (toolCall.Name)
                {
                    case "execute_bash":
                        result = await ExecuteBashAsync(toolCall, ct);
                        break;
                    case "fetch_web_content":
                        result = await FetchWebContentAsync(toolCall, ct);
                        break;
                    case "read_file":
                        result = await ReadFileAsync(toolCall, ct);
                        break;
                    case "write_file":
                        result = await WriteFileAsync(toolCall, ct);
                        break;
                    default:
                        result.ToolId = toolCall.Id;
                    result.Success = false;
                    result.Error = $"Unknown tool: {toolCall.Name}";
                    break;
                }

                result.ToolId = string.IsNullOrWhiteSpace(result.ToolId) ? toolCall.Id : result.ToolId;
                result.ToolName = string.IsNullOrWhiteSpace(result.ToolName) ? toolCall.Name : result.ToolName;
                result.RequestSummary = string.IsNullOrWhiteSpace(result.RequestSummary)
                    ? BuildRequestSummary(toolCall)
                    : result.RequestSummary;

                results.Add(result);
            }
            catch (Exception ex)
            {
                results.Add(new ToolResult
                {
                    ToolId = toolCall.Id,
                    ToolName = toolCall.Name,
                    RequestSummary = BuildRequestSummary(toolCall),
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        return results;
    }

    private async Task<ToolResult> ExecuteBashAsync(ToolCall toolCall, CancellationToken ct = default)
    {
        string? stdoutPath = null;
        string? stderrPath = null;
        try
        {
            var args = System.Text.Json.JsonSerializer.Deserialize<BashArguments>(toolCall.Arguments);
            if (args == null || string.IsNullOrEmpty(args.Command))
            {
                return new ToolResult
                {
                    ToolId = toolCall.Id,
                    ToolName = toolCall.Name,
                    RequestSummary = string.Empty,
                    Success = false,
                    Error = "Invalid bash arguments: missing command"
                };
            }

            stdoutPath = Path.Combine(Path.GetTempPath(), $"agenticllm-bash-{Guid.NewGuid():N}.stdout");
            stderrPath = Path.Combine(Path.GetTempPath(), $"agenticllm-bash-{Guid.NewGuid():N}.stderr");
            var wrappedCommand =
                "{\n" +
                args.Command + "\n" +
                "} > " + EscapeShellArgument(stdoutPath) +
                " 2> " + EscapeShellArgument(stderrPath);

            var processStartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/bash",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            processStartInfo.ArgumentList.Add("-c");
            processStartInfo.ArgumentList.Add(wrappedCommand);

            using var process = new System.Diagnostics.Process { StartInfo = processStartInfo };
            
            process.Start();
            using var cancellationRegistration = ct.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Best-effort kill on cancellation.
                }
            });

            var timeoutMs = Math.Max(1, _settings.DefaultToolTimeoutMs);
            var waitForExitTask = process.WaitForExitAsync();
            var timeoutTask = Task.Delay(timeoutMs, CancellationToken.None);

            var completedTask = await Task.WhenAny(waitForExitTask, timeoutTask);
            var timedOut = completedTask == timeoutTask;

            if (timedOut)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Best-effort kill for timed out process.
                }

                try
                {
                    await waitForExitTask;
                }
                catch
                {
                    // Ignore exit faults after timeout kill.
                }

                var timedOutStdout = SafeReadFile(stdoutPath).Trim();
                var timedOutStderr = SafeReadFile(stderrPath).Trim();

                return new ToolResult
                {
                    ToolId = toolCall.Id,
                    ToolName = toolCall.Name,
                    RequestSummary = args.Command,
                    Success = false,
                    Output = timedOutStdout,
                    Error = BuildProcessError($"Process timed out after {FormatTimeout(timeoutMs)}", timedOutStderr)
                };
            }

            var stdout = SafeReadFile(stdoutPath).Trim();
            var stderr = SafeReadFile(stderrPath).Trim();

            Console.WriteLine($"[BASH OUTPUT] Command: {args.Command}");
            if (!string.IsNullOrEmpty(stdout))
                Console.WriteLine($"[BASH STDOUT] {stdout}");
            if (!string.IsNullOrEmpty(stderr))
                Console.WriteLine($"[BASH STDERR] {stderr}");
            if (process.ExitCode != 0)
                Console.WriteLine($"[BASH FAILURE] ExitCode={process.ExitCode} WrappedCommand={wrappedCommand}");

            return new ToolResult
            {
                ToolId = toolCall.Id,
                ToolName = toolCall.Name,
                RequestSummary = args.Command,
                Success = process.ExitCode == 0,
                Output = stdout,
                Error = process.ExitCode == 0
                    ? (!string.IsNullOrEmpty(stderr) ? stderr : "")
                    : BuildProcessError($"Process exited with code {process.ExitCode}", stderr)
            };
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            var timeoutMs = Math.Max(1, _settings.DefaultToolTimeoutMs);
            return new ToolResult
            {
                ToolId = toolCall.Id,
                ToolName = toolCall.Name,
                RequestSummary = SafeArguments(toolCall.Arguments),
                Success = false,
                Error = $"Bash execution timed out after {FormatTimeout(timeoutMs)}"
            };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                ToolId = toolCall.Id,
                ToolName = toolCall.Name,
                RequestSummary = SafeArguments(toolCall.Arguments),
                Success = false,
                Error = $"Bash execution failed: {ex.Message}"
            };
        }
        finally
        {
            TryDeleteFile(stdoutPath);
            TryDeleteFile(stderrPath);
        }
    }

    private async Task<ToolResult> FetchWebContentAsync(ToolCall toolCall, CancellationToken ct = default)
    {
        try
        {
            var args = System.Text.Json.JsonSerializer.Deserialize<FetchArguments>(toolCall.Arguments);
            
            if (args == null || string.IsNullOrEmpty(args.Url))
            {
                return new ToolResult
                {
                    ToolId = toolCall.Id,
                    ToolName = toolCall.Name,
                    RequestSummary = "",
                    Success = false,
                    Error = "Invalid fetch arguments: missing URL"
                };
            }

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(_settings.DefaultToolTimeoutMs / 1000.0);
            
            var response = await client.GetAsync(args.Url, ct);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct);
                
                // Truncate if too long
                const int MaxLength = 5000;
                if (content.Length > MaxLength)
                {
                    content = content.Substring(0, MaxLength) + "... [truncated]";
                }

                return new ToolResult
                {
                    ToolId = toolCall.Id,
                    ToolName = toolCall.Name,
                    RequestSummary = args.Url,
                    Success = true,
                    Output = content
                };
            }
            else
            {
                return new ToolResult
                {
                    ToolId = toolCall.Id,
                    ToolName = toolCall.Name,
                    RequestSummary = args.Url,
                    Success = false,
                    Error = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}"
                };
            }
        }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                var timeoutMs = Math.Max(1, _settings.DefaultToolTimeoutMs);
                return new ToolResult
                {
                    ToolId = toolCall.Id,
                    ToolName = toolCall.Name,
                    RequestSummary = SafeArguments(toolCall.Arguments),
                    Success = false,
                    Error = $"Web fetch timed out after {FormatTimeout(timeoutMs)}"
                };
            }
            catch (Exception ex)
            {
                return new ToolResult
                {
                    ToolId = toolCall.Id,
                    ToolName = toolCall.Name,
                RequestSummary = SafeArguments(toolCall.Arguments),
                Success = false,
                Error = $"Web fetch failed: {ex.Message}"
            };
        }
    }

    private async Task<ToolResult> ReadFileAsync(ToolCall toolCall, CancellationToken ct = default)
    {
        try
        {
            var args = System.Text.Json.JsonSerializer.Deserialize<ReadFileArguments>(toolCall.Arguments);
            
            if (args == null || string.IsNullOrEmpty(args.Path))
            {
                return new ToolResult
                {
                    ToolId = toolCall.Id,
                    ToolName = toolCall.Name,
                    RequestSummary = "",
                    Success = false,
                    Error = "Invalid read_file arguments: missing path"
                };
            }

            if (!File.Exists(args.Path))
            {
                return new ToolResult
                {
                    ToolId = toolCall.Id,
                    ToolName = toolCall.Name,
                    RequestSummary = args.Path,
                    Success = false,
                    Error = $"File not found: {args.Path}"
                };
            }

            var content = await File.ReadAllTextAsync(args.Path, ct);
            
            const int MaxLength = 5000;
            if (content.Length > MaxLength)
            {
                content = content.Substring(0, MaxLength) + "... [truncated]";
            }

            return new ToolResult
            {
                ToolId = toolCall.Id,
                ToolName = toolCall.Name,
                RequestSummary = args.Path,
                Success = true,
                Output = content
            };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                ToolId = toolCall.Id,
                ToolName = toolCall.Name,
                RequestSummary = SafeArguments(toolCall.Arguments),
                Success = false,
                Error = $"read_file failed: {ex.Message}"
            };
        }
    }

    private async Task<ToolResult> WriteFileAsync(ToolCall toolCall, CancellationToken ct = default)
    {
        try
        {
            var args = System.Text.Json.JsonSerializer.Deserialize<WriteFileArguments>(toolCall.Arguments);
            
            if (args == null || string.IsNullOrEmpty(args.Path))
            {
                return new ToolResult
                {
                    ToolId = toolCall.Id,
                    ToolName = toolCall.Name,
                    RequestSummary = "",
                    Success = false,
                    Error = "Invalid write_file arguments: missing path"
                };
            }

            var directory = Path.GetDirectoryName(args.Path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(args.Path, args.Content ?? "", ct);

            return new ToolResult
            {
                ToolId = toolCall.Id,
                ToolName = toolCall.Name,
                RequestSummary = args.Path,
                Success = true,
                Output = $"File written successfully: {args.Path}"
            };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                ToolId = toolCall.Id,
                ToolName = toolCall.Name,
                RequestSummary = SafeArguments(toolCall.Arguments),
                Success = false,
                Error = $"write_file failed: {ex.Message}"
            };
        }
    }

    private static string BuildRequestSummary(ToolCall toolCall)
    {
        return toolCall.Name switch
        {
            "execute_bash" => TryExtractJsonString(toolCall.Arguments, "command"),
            "fetch_web_content" => TryExtractJsonString(toolCall.Arguments, "url"),
            "read_file" => TryExtractJsonString(toolCall.Arguments, "path"),
            "write_file" => TryExtractJsonString(toolCall.Arguments, "path"),
            _ => SafeArguments(toolCall.Arguments)
        };
    }

    private static string TryExtractJsonString(string json, string propertyName)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty(propertyName, out var value))
            {
                return value.GetString() ?? string.Empty;
            }
        }
        catch
        {
            // Fall back to raw arguments.
        }

        return SafeArguments(json);
    }

    private static string SafeArguments(string arguments)
    {
        return string.IsNullOrWhiteSpace(arguments) ? string.Empty : arguments;
    }

    private static string FormatTimeout(int timeoutMs)
    {
        if (timeoutMs % 1000 == 0)
        {
            return $"{timeoutMs / 1000} seconds";
        }

        return $"{timeoutMs} ms";
    }

    private static string EscapeShellArgument(string value)
    {
        return "'" + (value ?? string.Empty).Replace("'", "'\"'\"'") + "'";
    }

    private static string SafeReadFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            return File.ReadAllText(path);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static string BuildProcessError(string summary, string stderr)
    {
        return string.IsNullOrWhiteSpace(stderr)
            ? summary
            : $"{summary}\n{stderr}";
    }
}

public class BashArguments
{
    [System.Text.Json.Serialization.JsonPropertyName("command")]
    public string Command { get; set; } = "";
}

public class FetchArguments
{
    public string Url { get; set; } = "";
}

public class ReadFileArguments
{
    [System.Text.Json.Serialization.JsonPropertyName("path")]
    public string Path { get; set; } = "";
}

public class WriteFileArguments
{
    [System.Text.Json.Serialization.JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("content")]
    public string Content { get; set; } = "";
}
