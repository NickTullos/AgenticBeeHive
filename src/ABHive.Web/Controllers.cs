using ABHive.Application;
using Microsoft.AspNetCore.Mvc;

namespace ABHive.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MessagesController : ControllerBase
{
    private readonly WebSocketHandler _webSocketHandler;
    private readonly IWorkflowOrchestrator _orchestrator;
    
    public MessagesController(WebSocketHandler webSocketHandler, IWorkflowOrchestrator orchestrator)
    {
        _webSocketHandler = webSocketHandler;
        _orchestrator = orchestrator;
    }
    
    [HttpPost]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest("Message cannot be empty");
        
        // For now, just log the message
        // In a full implementation, this would queue the message for when LLM is idle
        await _webSocketHandler.PublishLogAsync($"Received user question: {request.Message}", "cyan");
        
        return Ok(new { success = true, message = "Message queued for processing" });
    }
    
    public class SendMessageRequest
    {
        public string Message { get; set; } = "";
    }
}

public class StatusController : ControllerBase
{
    private readonly WebSocketHandler _webSocketHandler;
    
    public StatusController(WebSocketHandler webSocketHandler)
    {
        _webSocketHandler = webSocketHandler;
    }
    
    [HttpGet]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            workflowRunning = false, // Would need to track this properly
            busy = false,
            connectedClients = _webSocketHandler.IsConnected ? 1 : 0
        });
    }
}

public class WorkflowController : ControllerBase
{
    private readonly WebSocketHandler _webSocketHandler;
    
    public WorkflowController(WebSocketHandler webSocketHandler)
    {
        _webSocketHandler = webSocketHandler;
    }
    
    [HttpPost("start")]
    public IActionResult Start()
    {
        // This would trigger the WebSocket handler
        return Ok(new { success = true, message = "Start command sent" });
    }
    
    [HttpPost("stop")]
    public IActionResult Stop()
    {
        return Ok(new { success = true, message = "Stop command sent" });
    }
}

public class MetricsController : ControllerBase
{
    private readonly IMetricsLogger? _metricsLogger;
    
    public MetricsController(IMetricsLogger? metricsLogger = null)
    {
        _metricsLogger = metricsLogger;
    }
    
    [HttpGet]
    public IActionResult GetMetrics()
    {
        // Return default metrics if logger not available
        return Ok(new
        {
            totalSteps = 0,
            successfulSteps = 0,
            failedSteps = 0,
            totalDurationMs = 0L,
            averageStepDurationMs = 0.0,
            totalTokensUsed = 0
        });
    }
}
