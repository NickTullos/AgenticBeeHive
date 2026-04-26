using System.Net.Http.Json;
using System.Text.Json;

using ABHive;
using Microsoft.Extensions.Hosting;

namespace ABHive.Web;

public class TelegramBotService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WebSocketHandler _webSocketHandler;
    private readonly WorkflowStateStore _workflowStateStore;
    private readonly ILogger<TelegramBotService> _logger;
    private readonly TicketIterationStatusResolver _ticketIterationStatusResolver;

    private long _nextUpdateOffset;
    private long _lastForwardedSequence;
    private bool _initializedForwardCursor;
    private string _forwardCursorContextKey = string.Empty;

    public TelegramBotService(
        AppSettings settings,
        IHttpClientFactory httpClientFactory,
        WebSocketHandler webSocketHandler,
        WorkflowStateStore workflowStateStore,
        ILogger<TelegramBotService> logger,
        TicketIterationStatusResolver ticketIterationStatusResolver)
    {
        _settings = settings;
        _httpClientFactory = httpClientFactory;
        _webSocketHandler = webSocketHandler;
        _workflowStateStore = workflowStateStore;
        _logger = logger;
        _ticketIterationStatusResolver = ticketIterationStatusResolver;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.TelegramEnabled)
        {
            _logger.LogInformation("Telegram bot integration is disabled.");
            return;
        }

        InitializeForwardCursor();
        _logger.LogInformation("Telegram bot integration enabled for chat {ChatId}.", _settings.TelegramChatId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollTelegramUpdatesAsync(stoppingToken);
                await ForwardWorkflowMessagesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telegram bot loop failed. Retrying shortly.");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private void InitializeForwardCursor()
    {
        if (_initializedForwardCursor)
        {
            return;
        }

        var hydration = _workflowStateStore.GetHydrationAsync().GetAwaiter().GetResult();
        _forwardCursorContextKey = BuildForwardContextKey(hydration.Snapshot);
        _lastForwardedSequence = hydration.History.LastOrDefault()?.Sequence ?? 0;
        _initializedForwardCursor = true;
    }

    private async Task PollTelegramUpdatesAsync(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("telegram");
        var timeout = Math.Max(1, _settings.TelegramPollTimeoutSeconds);
        var endpoint = $"/bot{_settings.TelegramBotToken}/getUpdates?offset={_nextUpdateOffset}&timeout={timeout}";
        using var response = await client.GetAsync(endpoint, ct);
        response.EnsureSuccessStatusCode();

        var updates = await response.Content.ReadFromJsonAsync<TelegramApiResponse<List<TelegramUpdate>>>(JsonOptions, ct);
        if (updates?.ok != true || updates.result == null)
        {
            return;
        }

        foreach (var update in updates.result)
        {
            _nextUpdateOffset = Math.Max(_nextUpdateOffset, update.update_id + 1);
            await ProcessTelegramUpdateAsync(update, ct);
        }
    }

    private async Task ProcessTelegramUpdateAsync(TelegramUpdate update, CancellationToken ct)
    {
        var message = update.message;
        if (message?.chat == null || string.IsNullOrWhiteSpace(message.text))
        {
            return;
        }

        if (message.chat.id != _settings.TelegramChatId)
        {
            _logger.LogInformation("Ignoring Telegram message from unauthorized chat {ChatId}.", message.chat.id);
            return;
        }

        var text = message.text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (text.StartsWith('/'))
        {
            await ProcessTelegramCommandAsync(text, ct);
            return;
        }

        var queued = await _webSocketHandler.QueueUserMessageAsync(text, MessageSources.Telegram, "Telegram");
        if (queued)
        {
            await SendTelegramMessageAsync("Sent to the active step as Telegram input.", ct);
            await _webSocketHandler.PublishTelegramCommandAsync(text, "Sent to the active step as Telegram input.");
        }
        else
        {
            await SendTelegramMessageAsync("The workflow is not currently waiting for input.", ct);
            await _webSocketHandler.PublishTelegramCommandAsync(text, "The workflow is not currently waiting for input.");
        }
    }

    private async Task ProcessTelegramCommandAsync(string text, CancellationToken ct)
    {
        var command = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();

        switch (command)
        {
            case "/start":
            case "/help":
                await SendTelegramMessageAsync("Commands: /status, /startworkflow, /continue, /stop, /reset, /ticket", ct);
                await _webSocketHandler.PublishTelegramCommandAsync(text, "Commands: /status, /startworkflow, /continue, /stop, /reset, /ticket");
                await _webSocketHandler.PublishTelegramCommandAsync(text, "Help displayed.");
                break;

            case "/status":
                var statusMsg = await BuildStatusMessageAsync();
                await SendTelegramMessageAsync(statusMsg, ct);
                await _webSocketHandler.PublishTelegramCommandAsync(text, statusMsg);
                break;

            case "/startworkflow":
                await _webSocketHandler.StartWorkflowAsync();
                await _webSocketHandler.PublishTelegramCommandAsync(text, "Workflow start/resume requested.");
                break;

            case "/continue":
                if (await _webSocketHandler.QueueContinueAsync())
                {
                    await _webSocketHandler.PublishTelegramCommandAsync(text, "Workflow continue/resume requested.");
                }
                else
                {
                    await _webSocketHandler.PublishTelegramCommandAsync(text, "The workflow is not ready to continue right now.");
                }
                break;

            case "/stop":
                await _webSocketHandler.StopWorkflowAsync();
                await _webSocketHandler.PublishTelegramCommandAsync(text, "Stop requested.");
                break;

            case "/reset":
                await _webSocketHandler.ResetWorkflowAsync();
                await _webSocketHandler.PublishTelegramCommandAsync(text, "Workflow reset.");
                break;

            case "/ticket":
                await HandleTicketCommandAsync(ct);
                break;

            default:
                await SendTelegramMessageAsync("Unknown command. Try /status or /help.", ct);
                await _webSocketHandler.PublishTelegramCommandAsync(text, "Unknown command. Try /status or /help.");
                await _webSocketHandler.PublishTelegramCommandAsync(text, "Unknown command.");
                break;
        }
    }


    private async Task HandleTicketCommandAsync(CancellationToken ct)
    {
        try
        {
            var hydration = await _workflowStateStore.GetHydrationAsync();
            var snapshot = hydration.Snapshot;

            if (!snapshot.IsCurrentStepTicketIteration)
            {
                var msg = "The current step is not configured for ticket iteration.";
                await SendTelegramMessageAsync(msg, ct);
                await _webSocketHandler.PublishTelegramCommandAsync("/ticket", msg);
                return;
            }

            var resumePoint = _ticketIterationStatusResolver.FindResumePoint(snapshot);

            if (!resumePoint.Found || resumePoint.HeaderStatus?.RemainingTickets <= 0)
            {
                var msg = "No more tickets to process for this step.";
                await SendTelegramMessageAsync(msg, ct);
                await _webSocketHandler.PublishTelegramCommandAsync("/ticket", msg);
                return;
            }

            var success = await _webSocketHandler.QueueContinueAsync();

            if (success)
            {
                var headerStatus = resumePoint.HeaderStatus;
                var ticketId = string.IsNullOrWhiteSpace(headerStatus?.CurrentTicketId) 
                    ? $"#{headerStatus?.CurrentTicketOrdinal}" 
                    : headerStatus.CurrentTicketId;
                var remainingTickets = headerStatus?.RemainingTickets ?? 0;
                
                var msg = $"Continuing to next ticket: {ticketId}. {remainingTickets} tickets remaining.";
                await SendTelegramMessageAsync(msg, ct);
                await _webSocketHandler.PublishTelegramCommandAsync("/ticket", msg);
            }
            else
            {
                var msg = "Unable to advance to next ticket. Workflow may need manual intervention.";
                await SendTelegramMessageAsync(msg, ct);
                await _webSocketHandler.PublishTelegramCommandAsync("/ticket", msg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error handling /ticket command");
            var msg = "An error occurred while processing the ticket command.";
            await SendTelegramMessageAsync(msg, ct);
            await _webSocketHandler.PublishTelegramCommandAsync("/ticket", msg);
        }
    }
    private async Task<string> BuildStatusMessageAsync()
    {
        var hydration = await _workflowStateStore.GetHydrationAsync();
        var snapshot = hydration.Snapshot;
        var lines = new List<string>
        {
            $"Status: {snapshot.Status}",
            $"Step: {snapshot.CurrentStep} of {snapshot.TotalSteps}",
            $"Step name: {snapshot.CurrentStepName}"
        };

        if (snapshot.AwaitingUserInput)
        {
            lines.Add("Waiting for input: yes");
        }

        if (snapshot.CanResume && snapshot.NextStepToRun > 0)
        {
            lines.Add($"Resume point: step {snapshot.NextStepToRun}");
        }

        return string.Join('\n', lines);
    }

    private async Task ForwardWorkflowMessagesAsync(CancellationToken ct)
    {
        var hydration = await _workflowStateStore.GetHydrationAsync();
        var currentContextKey = BuildForwardContextKey(hydration.Snapshot);

        if (_initializedForwardCursor &&
            !string.Equals(currentContextKey, _forwardCursorContextKey, StringComparison.Ordinal))
        {
            var replayCount = _settings.TelegramSwitchContextMessageCount;
            var replayMessages = replayCount > 0
                ? SelectLastSwitchReplayMessages(hydration.History, hydration.Snapshot, replayCount)
                : new List<string>();

            _logger.LogInformation(
                "Workflow context switched. Sending last {ReplayCount} message(s) to Telegram to avoid replay spam.",
                replayMessages.Count);

            foreach (var message in replayMessages)
            {
                await SendTelegramMessageAsync(message, ct);
            }

            _forwardCursorContextKey = currentContextKey;
            _lastForwardedSequence = hydration.History.LastOrDefault()?.Sequence ?? 0;
            return;
        }

        var pendingMessages = hydration.History
            .Where(item => item.Sequence > _lastForwardedSequence)
            .OrderBy(item => item.Sequence)
            .ToList();

        foreach (var item in pendingMessages)
        {
            _lastForwardedSequence = item.Sequence;

            var telegramText = BuildTelegramForwardText(item.Message, hydration.Snapshot);
            if (string.IsNullOrWhiteSpace(telegramText))
            {
                continue;
            }

            await SendTelegramMessageAsync(telegramText, ct);
        }
    }

    internal static string BuildForwardContextKey(WorkflowRuntimeSnapshot snapshot)
    {
        var projectName = snapshot.SelectedProjectName ?? string.Empty;
        var workflowTypeId = snapshot.SelectedWorkflowTypeId ?? string.Empty;
        return $"{projectName}::{workflowTypeId}";
    }

    internal static List<string> SelectLastSwitchReplayMessages(
        IReadOnlyList<WorkflowHistoryEvent> history,
        WorkflowRuntimeSnapshot snapshot,
        int count)
    {
        if (count <= 0 || history.Count == 0)
        {
            return new List<string>();
        }

        var result = new List<string>(Math.Min(count, history.Count));

        for (var index = history.Count - 1; index >= 0 && result.Count < count; index--)
        {
            var telegramText = BuildTelegramForwardText(history[index].Message, snapshot);
            if (string.IsNullOrWhiteSpace(telegramText))
            {
                continue;
            }

            result.Add(telegramText);
        }

        result.Reverse();
        return result;
    }

    private static string? BuildTelegramForwardText(AgentMessage message, WorkflowRuntimeSnapshot snapshot)
    {
        var payload = ToJsonElement(message.payload);

        return message.type switch
        {
            MessageTypes.LlmResponse => BuildLlmResponseText(payload),
            //MessageTypes.ToolRequest => "Tool requested.",// BuildToolRequestText(payload),
            //MessageTypes.ToolExecution => BuildToolExecutionText(payload),
            MessageTypes.UserQuestion when !string.Equals(ReadString(payload, "source"), MessageSources.Telegram, StringComparison.OrdinalIgnoreCase)
                => $"Web: {ReadString(payload, "question")}",
            //MessageTypes.Status when string.Equals(ReadString(payload, "status"), "Waiting for input", StringComparison.OrdinalIgnoreCase)
            //    => $"Waiting for input on step {snapshot.CurrentStep} of {snapshot.TotalSteps}.",
            MessageTypes.StepFailed => $"Step {ReadInt(payload, "stepNumber")} failed: {ReadString(payload, "error")}",
            MessageTypes.WorkflowEnd => "Workflow completed.",
            MessageTypes.WorkflowReset => "Workflow reset.",
            _ => null
        };
    }

    private static string BuildToolExecutionText(JsonElement payload)
    {
        var toolName = ReadString(payload, "toolName");
        var status = ReadString(payload, "status");
        var requestSummary = ReadString(payload, "requestSummary");

        var summary = string.IsNullOrWhiteSpace(requestSummary)
            ? string.Empty
            : $"\n{requestSummary}";

        return $"Tool: {toolName} ({status}){summary}";
    }

    private static string BuildToolRequestText(JsonElement payload)
    {
        var toolName = ReadString(payload, "toolName");
        var requestSummary = ReadString(payload, "requestSummary");
        var summary = string.IsNullOrWhiteSpace(requestSummary)
            ? string.Empty
            : $"\n{requestSummary}";

        return $"Tool: {toolName} (Requested){summary}";
    }

    private static string BuildLlmResponseText(JsonElement payload)
    {
        var content = ReadString(payload, "content");
        var reasoning = ReadString(payload, "reasoningContent");

        if (!string.IsNullOrWhiteSpace(content) && !string.IsNullOrWhiteSpace(reasoning))
        {
            return $"{content}\n\nReasoning:\n{reasoning}";
        }

        return !string.IsNullOrWhiteSpace(content) ? content : reasoning;
    }

    private async Task SendTelegramMessageAsync(string message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        foreach (var chunk in ChunkMessage(message, 3500))
        {
            var client = _httpClientFactory.CreateClient("telegram");
            using var response = await client.PostAsJsonAsync(
                $"/bot{_settings.TelegramBotToken}/sendMessage",
                new TelegramSendMessageRequest
                {
                    chat_id = _settings.TelegramChatId,
                    text = chunk
                },
                JsonOptions,
                ct);

            response.EnsureSuccessStatusCode();
        }
    }

    private static IEnumerable<string> ChunkMessage(string message, int maxLength)
    {
        if (message.Length <= maxLength)
        {
            yield return message;
            yield break;
        }

        for (var index = 0; index < message.Length; index += maxLength)
        {
            var length = Math.Min(maxLength, message.Length - index);
            yield return message.Substring(index, length);
        }
    }

    private static JsonElement ToJsonElement(object? payload)
    {
        return JsonSerializer.SerializeToElement(payload, JsonOptions);
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var property)
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int ReadInt(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : 0;
    }
}

internal class TelegramApiResponse<T>
{
    public bool ok { get; set; }
    public T? result { get; set; }
}

internal class TelegramUpdate
{
    public long update_id { get; set; }
    public TelegramMessage? message { get; set; }
}

internal class TelegramMessage
{
    public long message_id { get; set; }
    public TelegramChat? chat { get; set; }
    public string text { get; set; } = "";
}

internal class TelegramChat
{
    public long id { get; set; }
}

internal class TelegramSendMessageRequest
{
    public long chat_id { get; set; }
    public string text { get; set; } = "";
}
