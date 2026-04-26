using ABHive.Web;
using Xunit;

namespace ABHive.Tests;

public class TelegramBotServiceTests
{
    [Fact]
    public void BuildForwardContextKey_IncludesProjectAndWorkflowType()
    {
        var snapshot = new WorkflowRuntimeSnapshot
        {
            SelectedProjectName = "alpha",
            SelectedWorkflowTypeId = "wf-1"
        };

        Assert.Equal("alpha::wf-1", TelegramBotService.BuildForwardContextKey(snapshot));
    }

    [Fact]
    public void SelectLastSwitchReplayMessages_TakesLastNForwardable_InOrder()
    {
        var snapshot = new WorkflowRuntimeSnapshot
        {
            SelectedProjectName = "alpha",
            SelectedWorkflowTypeId = "wf-1"
        };

        var history = new List<WorkflowHistoryEvent>
        {
            new()
            {
                Sequence = 1,
                Message = new AgentMessage
                {
                    type = MessageTypes.LlmResponse,
                    payload = new { content = "one", reasoningContent = "" }
                }
            },
            new()
            {
                Sequence = 2,
                Message = new AgentMessage
                {
                    type = MessageTypes.ToolRequest,
                    payload = new { toolName = "Bash", status = "Requested" }
                }
            },
            new()
            {
                Sequence = 3,
                Message = new AgentMessage
                {
                    type = MessageTypes.UserQuestion,
                    payload = new { question = "q1", source = MessageSources.Web }
                }
            },
            new()
            {
                Sequence = 4,
                Message = new AgentMessage
                {
                    type = MessageTypes.StepFailed,
                    payload = new { stepNumber = 2, error = "boom" }
                }
            },
            new()
            {
                Sequence = 5,
                Message = new AgentMessage
                {
                    type = MessageTypes.UserQuestion,
                    payload = new { question = "from telegram", source = MessageSources.Telegram }
                }
            },
            new()
            {
                Sequence = 6,
                Message = new AgentMessage
                {
                    type = MessageTypes.WorkflowEnd,
                    payload = new { }
                }
            }
        };

        var replay = TelegramBotService.SelectLastSwitchReplayMessages(history, snapshot, 3);

        Assert.Equal(3, replay.Count);
        Assert.Equal("Web: q1", replay[0]);
        Assert.Equal("Step 2 failed: boom", replay[1]);
        Assert.Equal("Workflow completed.", replay[2]);
    }

    [Fact]
    public void SelectLastSwitchReplayMessages_WithZeroCount_ReturnsEmpty()
    {
        var snapshot = new WorkflowRuntimeSnapshot();
        var history = new List<WorkflowHistoryEvent>
        {
            new()
            {
                Sequence = 1,
                Message = new AgentMessage { type = MessageTypes.WorkflowEnd, payload = new { } }
            }
        };

        var replay = TelegramBotService.SelectLastSwitchReplayMessages(history, snapshot, 0);
        Assert.Empty(replay);
    }
}

