# Quick Start Guide

Get ABHive running in 5 minutes.

## Prerequisites

- **.NET 7 SDK** installed ([download here](https://dotnet.microsoft.com/download/dotnet/7.0))
- **LM Studio** running locally on `http://localhost:1234` (or configure in `appsettings.json`)
- A modern web browser

## Installation

### 1. Clone or Download

```bash
cd solution
dotnet restore
dotnet build
```

### 2. Configure

Create or edit `src/ABHive.Web/appsettings.json` with your LLM server configuration:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:5001"
      }
    }
  },
  "LlmServers": [
    {
      "Id": "default-server",
      "Name": "LM Studio",
      "BaseUrl": "http://localhost:1234",
      "ApiKey": "",
      "DefaultModelId": "default-model",
      "Models": [
        { "Id": "default-model", "Name": "default" }
      ]
    }
  ],
  "ActiveLlmServerId": "default-server",
  "ActiveLlmModelId": "default-model",
  "StepsDirectory": "./workflowtypes/chat",
  "WorkflowTypesDirectory": "./workflowtypes",
  "LogFilePath": "./logs/metrics.json",
  "DefaultToolTimeoutMs": 60000,
  "LlmTemperature": 0.7,
  "LlmTopP": 1.0,
  "LlmTopK": 0,
  "LlmMaxTokens": 0,
  "LlmFrequencyPenalty": 0.0,
  "LlmPresencePenalty": 0.0,
  "LlmStopSequences": "",
  "TelegramEnabled": false,
  "TelegramBotToken": "",
  "TelegramChatId": 0,
  "ToolConfigs": {
    "Bash": { "Name": "Bash", "Enabled": true },
    "WebFetch": { "Name": "WebFetch", "Enabled": true },
    "ReadFile": { "Name": "ReadFile", "Enabled": true },
    "WriteFile": { "Name": "WriteFile", "Enabled": true }
  },
  "ProjectRootDirectory": "./projects"
}
```

> ⚠️ **Security**: Never commit `appsettings.json` if it contains secrets (Telegram tokens, API keys). Use `appsettings.defaults.json` as a template.

## Running the Application

### Web Application

```bash
cd src/ABHive.Web
dotnet run
```

The server will start on **`http://localhost:5001`**.

Open a browser and navigate to `http://localhost:5001` to access the dashboard.

### Console Application (CLI)

```bash
cd src/ABHive
dotnet run
```

## Using the Web Interface

1. **Open Dashboard** — Navigate to `http://localhost:5001`
2. **Check Status** — Green "Ready" indicator means the system is ready
3. **Select Workflow** — Choose a workflow type from `workflowtypes/`
4. **Start Workflow** — Click "Start Workflow" to begin processing steps
5. **Monitor Progress** — Real-time logs show LLM responses and tool executions
6. **Control** — Use "Stop Workflow" to cancel or "Continue" to advance

### Dashboard Features

- **Project Summary** — Current status and ticket health
- **Architecture Overview** — System design with key-value grid visualization
- **Planning & Q&A** — Review requirements and decisions
- **Tickets** — Track open, skipped, and completed tickets

## API Access

### Status Check

```bash
curl http://localhost:5001/api/status
```

**Response:**
```json
{
  "workflowRunning": false,
  "busy": false,
  "connectedClients": 0
}
```

### Metrics

```bash
curl http://localhost:5001/api/metrics
```

**Response:**
```json
{
  "totalSteps": 0,
  "successfulSteps": 0,
  "failedSteps": 0,
  "totalDurationMs": 0,
  "averageStepDurationMs": 0.0,
  "totalTokensUsed": 0
}
```

## Configuration Reference

### Key Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `Kestrel.Endpoints.Http.Url` | `http://localhost:5001` | Server listen address |
| `LlmServers[].BaseUrl` | `http://localhost:1234` | LM Studio or OpenAI-compatible API URL |
| `ActiveLlmServerId` | `"default-server"` | Active server ID from `LlmServers` array |
| `ActiveLlmModelId` | `"default-model"` | Active model ID from selected server |
| `StepsDirectory` | `./workflowtypes/chat` | Default workflow directory |
| `WorkflowTypesDirectory` | `./workflowtypes` | Root workflow types folder |
| `LogFilePath` | `./logs/metrics.json` | Metrics output file |
| `DefaultToolTimeoutMs` | `60000` | Default tool execution timeout (ms) |
| `LlmTemperature` | `0.7` | LLM temperature (0–2) |
| `LlmTopP` | `1.0` | LLM top-p sampling (0–1) |
| `LlmTopK` | `0` | LLM top-k (0 = disabled) |
| `LlmMaxTokens` | `0` | Max completion tokens (0 = unlimited) |
| `LlmFrequencyPenalty` | `0.0` | Frequency penalty (-2–2) |
| `LlmPresencePenalty` | `0.0` | Presence penalty (-2–2) |
| `LlmStopSequences` | `""` | Stop sequences (comma-separated) |

### Tool Configuration

Enable/disable tools in the `ToolConfigs` section:

```json
"ToolConfigs": {
  "Bash": { "Name": "Bash", "Enabled": true },
  "WebFetch": { "Name": "WebFetch", "Enabled": true },
  "ReadFile": { "Name": "ReadFile", "Enabled": true },
  "WriteFile": { "Name": "WriteFile", "Enabled": true }
}
```

### LLM Servers (Multi-Server Support)

Configure multiple LLM servers:

```json
"LlmServers": [
  {
    "Id": "lm-studio",
    "Name": "LM Studio",
    "BaseUrl": "http://localhost:1234",
    "ApiKey": "",
    "DefaultModelId": "qwen3.6-35b",
    "Models": [
      { "Id": "qwen3.6-35b", "Name": "Qwen 3.6 35B" },
      { "Id": "llama3.3-70b", "Name": "Llama 3.3 70B" }
    ]
  },
  {
    "Id": "llama-cpp",
    "Name": "llama.cpp",
    "BaseUrl": "http://127.0.0.1:1235",
    "ApiKey": "",
    "DefaultModelId": "qwen-coder",
    "Models": [
      { "Id": "qwen-coder", "Name": "Qwen Coder" }
    ]
  }
],
"ActiveLlmServerId": "lm-studio",
"ActiveLlmModelId": "qwen3.6-35b"
```

## Building for Production

### Self-Contained Deployment

```bash
dotnet publish -c Release -o ./publish --self-contained
cd publish
./ABHive.Web
```

### Linux/Server Deployment

```bash
dotnet publish -c Release -o ./publish -r linux-x64 --self-contained
scp -r ./publish user@server:/opt/abhive
ssh user@server "cd /opt/abhive && ./ABHive.Web"
```

## Troubleshooting

### Server won't start
- Check that port `5001` is not in use: `lsof -i :5001`
- Verify .NET 7 SDK/runtime is installed: `dotnet --version`
- Check `appsettings.json` exists and is valid JSON

### WebSocket connection fails
- Ensure browser supports WebSockets (all modern browsers do)
- Check server logs for WebSocket errors
- Verify no firewall blocking localhost connections
- Confirm the server is running on `http://localhost:5001`

### 404 on pages
- Ensure `ClientApp` directory is in the output folder
- Check `appsettings.json` paths are correct
- Verify the web project built successfully

### LM Studio not responding
- Verify LM Studio is running
- Check configured URL matches LM Studio's address (default `http://localhost:1234`)
- Ensure no API key required (or add `ApiKey` to `appsettings.json`)
- Try the `ActiveLlmServerId` / `ActiveLlmModelId` settings

### Port conflicts
- Change `Kestrel.Endpoints.Http.Url` in `appsettings.json` to a different port
- Example: `"http://localhost:5002"`

## Next Steps

1. Read the [User's Guide](USERS_GUIDE.md) for comprehensive documentation
2. Explore [Workflow Authoring](WORKFLOWS.md) to create custom workflows
3. Set up [Telegram Integration](TELEGRAM_GUIDE.md) for remote control
4. Review [Architecture Overview](ARCHITECTURE_OVERVIEW.md) for system details

---

*For issues or questions, please refer to the full documentation or open an issue on GitHub.*
