# User's Guide — ABHive

Welcome to **ABHive** (Agentic LLM Programming Application)! This guide will help you get started with the application, configure it, and use all its features.

## Table of Contents

1. [Overview](#overview)
2. [Quick Start](#quick-start)
3. [Using the Web Interface](#using-the-web-interface)
4. [Configuration](#configuration)
5. [LLM Server Configuration](#llm-server-configuration)
6. [LLM Generation Parameters](#llm-generation-parameters)
7. [Tool Configuration](#tool-configuration)
8. [Telegram Integration](#telegram-integration)
9. [Workflow Types](#workflow-types)
10. [Ticket Iteration](#ticket-iteration)
11. [API Reference](#api-reference)
12. [Troubleshooting](#troubleshooting)
13. [Appendix](#appendix)

---

## Overview

ABHive is a self-hosted web application that automates software development tasks using local LLMs. It processes workflow steps, manages tickets, and integrates with tools like Bash, WebFetch, ReadFile, and WriteFile.

### Key Features

- **Local LLM Execution** — Connect to LM Studio or any OpenAI-compatible API
- **Multi-Server Support** — Configure and switch between multiple LLM servers
- **Step Processing** — Scan directories for `.md` files, process one at a time
- **Tool System** — Built-in tools: `Bash`, `WebFetch`, `ReadFile`, `WriteFile`
- **Agent Workflow** — LLM dynamically selects and invokes tools
- **Real-Time Dashboard** — Monitor progress with architecture visualization
- **Telegram Bot** — Remote workflow management via bot commands
- **Ticket Iteration** — Process one ticket at a time with automatic progression
- **Metrics & Logging** — Detailed execution metrics in `logs/metrics.json`

---

## Quick Start

### Prerequisites

- .NET 7 runtime or SDK installed
- LM Studio running locally (or configure in `appsettings.json`)
- Web browser

### Installation & Running

1. **Navigate to the web project**:
   ```bash
   cd solution/src/ABHive.Web
   ```

2. **Configure LM Studio**: Ensure it's accessible at `http://localhost:1234` (default)

3. **Run the application**:
   ```bash
   dotnet run
   ```

4. **Open in browser**: Navigate to `http://localhost:5001`

### First Steps

1. The dashboard shows your project status (green "Ready" indicator)
2. Select a workflow type from the dropdown (e.g., `create_application`, `chat`, `debug`)
3. Click **"Start Workflow"** to begin processing steps
4. Monitor progress in real-time in the dashboard
5. When a ticket step is complete, use the `/ticket` Telegram command or click "Continue" to proceed

---

## Using the Web Interface

### Dashboard (`/dashboard.html`)

The dashboard provides an overview of your project:

- **Project Summary**: See current status and ticket health
- **Architecture Overview**: View system design and components (with key-value grid for JSON data)
- **Planning & Q&A**: Review requirements and decisions
- **Tickets**: Track open, skipped, and completed tickets

### Main Interface (`/index.html`)

The main workflow interface shows:

- **Status Indicator**: Green "Ready", Orange "Busy", etc.
- **Step Progress**: Current step number and name
- **Real-time Logs**: LLM responses and tool executions
- **Control Buttons**:
  - **Start Workflow**: Begin or resume processing
  - **Stop Workflow**: Cancel current operation
  - **Send Message**: Provide input when asked

### Settings (`/settings.html`)

Configure application settings:

- **LLM Servers**: Add, edit, or remove LLM server connections
- **Active Model**: Select which model to use
- **Tool Configurations**: Enable/disable `Bash`, `WebFetch`, `ReadFile`, `WriteFile`
- **LLM Generation Parameters**: Temperature, TopP, TopK, MaxTokens, penalties
- **Telegram Bot Settings**: Enable/disable and configure bot
- **Path Settings**: Workflow types directory, project root directory

---

## Configuration

### appsettings.json

Create this file in `src/ABHive.Web/`:

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
  "LlmInactivityTimeoutMs": 0,
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
  "TelegramPollTimeoutSeconds": 20,
  "TelegramSwitchContextMessageCount": 5,
  "ToolConfigs": {
    "Bash": { "Name": "Bash", "Enabled": true },
    "WebFetch": { "Name": "WebFetch", "Enabled": true },
    "ReadFile": { "Name": "ReadFile", "Enabled": true },
    "WriteFile": { "Name": "WriteFile", "Enabled": true }
  },
  "ProjectRootDirectory": "./projects"
}
```

### Configuration Reference

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `Kestrel.Endpoints.Http.Url` | string | `http://localhost:5001` | Server listen address |
| `LlmServers` | array | `[]` | Array of LLM server configurations |
| `ActiveLlmServerId` | string | `"default-server"` | ID of active server from `LlmServers` |
| `ActiveLlmModelId` | string | `"default-model"` | ID of active model from selected server |
| `StepsDirectory` | string | `./workflowtypes/chat` | Default workflow directory |
| `WorkflowTypesDirectory` | string | `./workflowtypes` | Root workflow types folder |
| `LogFilePath` | string | `./logs/metrics.json` | Metrics output file |
| `DefaultToolTimeoutMs` | int | `60000` | Default tool execution timeout (ms) |
| `LlmInactivityTimeoutMs` | int | `0` | LLM inactivity timeout (0 = disabled) |
| `LlmTemperature` | double | `0.7` | LLM temperature (range: 0–2) |
| `LlmTopP` | double | `1.0` | LLM top-p sampling (range: 0–1) |
| `LlmTopK` | int | `0` | LLM top-k (0 = disabled) |
| `LlmMaxTokens` | int | `0` | Max completion tokens (0 = unlimited) |
| `LlmFrequencyPenalty` | double | `0.0` | Frequency penalty (range: -2–2) |
| `LlmPresencePenalty` | double | `0.0` | Presence penalty (range: -2–2) |
| `LlmStopSequences` | string | `""` | Stop sequences (comma-separated) |
| `TelegramEnabled` | bool | `false` | Enable Telegram bot |
| `TelegramBotToken` | string | `""` | Telegram bot API token |
| `TelegramChatId` | long | `0` | Telegram chat ID for messages |
| `TelegramPollTimeoutSeconds` | int | `20` | Telegram polling interval (seconds) |
| `TelegramSwitchContextMessageCount` | int | `5` | Messages before context switch |
| `ToolConfigs` | object | `{}` | Tool enable/disable configuration |
| `ProjectRootDirectory` | string | `./projects` | Default project workspaces directory |

---

## LLM Server Configuration

ABHive supports **multiple LLM servers**. Configure them in `appsettings.json`:

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
      { "Id": "llama3.3-70b", "Name": "Llama 3.3 70B" },
      { "Id": "glm-4.7-flash", "Name": "GLM 4.7 Flash" }
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
  },
  {
    "Id": "openai-compatible",
    "Name": "OpenAI Compatible",
    "BaseUrl": "https://api.example.com/v1",
    "ApiKey": "your-api-key-here",
    "DefaultModelId": "gpt-4",
    "Models": [
      { "Id": "gpt-4", "Name": "GPT-4" },
      { "Id": "gpt-3.5-turbo", "Name": "GPT-3.5 Turbo" }
    ]
  }
],
"ActiveLlmServerId": "lm-studio",
"ActiveLlmModelId": "qwen3.6-35b"
```

### Server Configuration Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `Id` | string | Yes | Unique identifier for this server |
| `Name` | string | Yes | Display name for this server |
| `BaseUrl` | string | Yes | Base URL of the OpenAI-compatible API |
| `ApiKey` | string | No | API key for authentication (leave empty for LM Studio) |
| `DefaultModelId` | string | Yes | Default model ID from the Models array |
| `Models` | array | Yes | Array of available models on this server |

### Model Configuration Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `Id` | string | Yes | Unique model identifier |
| `Name` | string | Yes | Display name for the model |

---

## LLM Generation Parameters

These parameters control how the LLM generates responses. They can be set in `appsettings.json` or via the Settings page.

### Parameters

| Parameter | Type | Default | Range | Description |
|-----------|------|---------|-------|-------------|
| `LlmTemperature` | double | `0.7` | 0–2 | Controls randomness (lower = more deterministic) |
| `LlmTopP` | double | `1.0` | 0–1 | Nucleus sampling threshold |
| `LlmTopK` | int | `0` | ≥ 0 | Top-k sampling (0 = disabled) |
| `LlmMaxTokens` | int | `0` | ≥ 0 | Maximum completion tokens (0 = unlimited) |
| `LlmFrequencyPenalty` | double | `0.0` | -2–2 | Penalizes repeated tokens |
| `LlmPresencePenalty` | double | `0.0` | -2–2 | Penalizes tokens based on presence |
| `LlmStopSequences` | string | `""` | — | Stop generation when these sequences appear |

### Notes

- **StopSequences** is serialized as a JSON array of strings to the LLM API
- Parameters with `null` values are omitted from the JSON payload sent to the LLM
- Validation clamps values to their valid ranges when set programmatically

---

## Tool Configuration

ABHive includes four built-in tools. Enable or disable them in `appsettings.json`:

```json
"ToolConfigs": {
  "Bash": { "Name": "Bash", "Enabled": true },
  "WebFetch": { "Name": "WebFetch", "Enabled": true },
  "ReadFile": { "Name": "ReadFile", "Enabled": true },
  "WriteFile": { "Name": "WriteFile", "Enabled": true }
}
```

### Tool Descriptions

| Tool | Description |
|------|-------------|
| **Bash** | Executes shell commands on the host system |
| **WebFetch** | Retrieves web content (Markdown or text) from URLs |
| **ReadFile** | Reads file contents from the local filesystem |
| **WriteFile** | Writes content to files on the local filesystem |

### Tool Execution

- The LLM dynamically selects which tools to call for each step
- Tool results are fed back to the LLM for further processing
- Each tool call has a configurable timeout (`DefaultToolTimeoutMs`)
- Errors are captured and reported to the user for manual intervention

---

## Telegram Integration

When enabled, the Telegram bot provides remote control and status updates.

### Setup

1. **Create a bot** with @BotFather in Telegram
2. **Get your chat ID** by messaging your bot and checking `https://api.telegram.org/bot<YOUR_TOKEN>/getUpdates`
3. **Enable in `appsettings.json`**:

```json
{
  "TelegramEnabled": true,
  "TelegramBotToken": "1234567890:ABCdefGHIjklMNOpqrsTUVwxyz",
  "TelegramChatId": 987654321,
  "TelegramPollTimeoutSeconds": 20,
  "TelegramSwitchContextMessageCount": 5
}
```

### Available Commands

| Command | Description |
|---------|-------------|
| `/start` | Start interaction with bot |
| `/help` | Show all available commands |
| `/status` | Current workflow status, step number, and progress |
| `/startworkflow` | Start or resume the workflow |
| `/continue` | Continue to next step (same as clicking "Continue" in UI) |
| `/stop` | Stop current workflow execution |
| `/reset` | Reset workflow state completely |
| `/ticket` | Advance to next ticket (in ticket iteration mode) |

### Command Examples

**Check Status:**
```
/status
```
**Response:**
```
Status: Ready
Step: 3 of 10
Step name: Implement feature X
Waiting for input: no
```

**Advance to Next Ticket:**
```
/ticket
```
**Response:**
```
Continuing to next ticket: TICKET-123. 2 tickets remaining.
```

### Real-time Updates

The bot provides automatic updates when:
- Workflow starts or completes
- Steps begin and complete
- LLM responses are generated
- User input is requested
- Errors occur

### Security Considerations

1. **Whitelist Chat IDs**: Only messages from configured `TelegramChatId` are processed
2. **Bot Token Privacy**: Never share your bot token publicly
3. **Rate Limiting**: Bot has built-in rate limiting to prevent abuse
4. **Logging**: All commands are logged for audit purposes

### Troubleshooting

| Issue | Solution |
|-------|----------|
| Bot doesn't respond | Verify `TelegramEnabled` is true, check bot token is correct |
| Commands not working | Verify workflow is running or ready, check step configuration |
| No updates received | Check network allows outbound connections to Telegram API |
| Wrong chat ID | Verify `TelegramChatId` matches your Telegram account |

---

## Workflow Types

The application supports different workflow types for various development tasks. Workflows are stored in the `workflowtypes/` directory.

### Built-in Workflow Types

| Workflow | Description | Steps |
|----------|-------------|-------|
| **create_application** | Guides you through architecting a new application | 5 steps |
| **chat** | General-purpose conversation and guidance | Variable |
| **debug** | Helps debug code issues step by step | Variable |
| **new_feature** | Implement new features following best practices | Variable |
| **benchmarks_game_design** | Design benchmarks for performance comparison | 4 steps |
| **benchmarks_logic** | Implement benchmark logic | 3 steps |
| **add_epic** | Add epic-level features | 2 steps |
| **add_testing** | Add comprehensive test coverage | 1 step |
| **reactor_user_interface** | Refactor user interface components | 1 step |
| **code_review** | Review and debug code | 1 step |

### Workflow Folder Layout

```
workflowtypes/
  create_application/
    Architect-Step1.md
    Architect-Step2.md
    Architect-Step3.md
    Architect-Step4.md
    Architect-Step5.md
    Architect-Step5.json   <- optional step metadata
```

**Rules:**
- Step execution order is **alphabetical** by markdown file name
- Step files must be `*.md`
- For step file `X.md`, metadata is optional and must be `X.json` in the same folder

### Workflow Authoring

See [WORKFLOWS.md](WORKFLOWS.md) for complete details on creating custom workflows.

---

## Ticket Iteration

Ticket iteration allows processing one ticket at a time with automatic progression.

### How It Works

1. **Configuration**: Steps can be configured for `ticketIteration` mode
2. **Ticket Source**: Load tickets from `tickets.json`
3. **Completion Tracking**: Mark tickets complete in `completed.json`
4. **Automatic Progression**: Workflow advances to next incomplete ticket

### Step Metadata Configuration

For a step file `X.md`, create `X.json` in the same directory:

```json
{
  "executionMode": "ticketIteration",
  "ticketSource": "{{TICKETS_DIR}}/tickets.json",
  "completedSource": "{{TICKETS_DIR}}/completed.json",
  "maxRetriesPerTicket": 3
}
```

**Defaults:**
- `executionMode`: `standard`
- `ticketSource`: `{{TICKETS_DIR}}/tickets.json`
- `completedSource`: `{{TICKETS_DIR}}/completed.json`
- `maxRetriesPerTicket`: `3`

### Behavior in Ticket Iteration Mode

- Runs one ticket per workflow run
- Selects first incomplete `ticket_id` from `tickets.json` not in `completed.json`
- Treats `completed.json` as the authoritative source for ticket completion status
- Retries same ticket up to `maxRetriesPerTicket` in the same run
- If tickets remain after success, workflow stays resumable at the same step (`Next Ticket`)
- If no tickets remain, workflow completes normally

### Using `/ticket` Command

When a step is in ticket iteration mode:
- Send `/ticket` in Telegram to advance to the next ticket
- The bot confirms: `"Continuing to next ticket: TICKET-123. 2 tickets remaining."`

### Ticket JSON Schema

```json
[
  {
    "ticket_id": "string",
    "title": "string",
    "description": "string",
    "type": "feature | bug | refactor",
    "priority": "low | medium | high",
    "files_to_modify": ["string"],
    "inputs": {},
    "outputs": {},
    "acceptance_criteria": ["string"],
    "dependencies": ["ticket_id"],
    "test_requirements": {
      "unit_tests": "boolean",
      "integration_tests": "boolean"
    },
    "definition_of_done": ["string"]
  }
]
```

### Path Tokens

| Token | Replaced With |
|-------|---------------|
| `{{PROJECT_ROOT}}` | Selected project root directory |
| `{{GOALS_DIR}}` | `{{PROJECT_ROOT}}/goals` |
| `{{FILES_DIR}}` | `{{PROJECT_ROOT}}/files` |
| `{{SOLUTION_DIR}}` | `{{PROJECT_ROOT}}/solution` |
| `{{PLANNING_DIR}}` | `{{PROJECT_ROOT}}/planning` |
| `{{DESIGN_DIR}}` | `{{PROJECT_ROOT}}/design` |
| `{{TICKETS_DIR}}` | `{{PROJECT_ROOT}}/tickets` |

---

## API Reference

### WebSocket (`/ws/agent`)

The WebSocket endpoint provides real-time communication between the browser and server.

**Connection:**
```
ws://localhost:5001/ws/agent
```

**Message Format (JSON):**

**Client → Server:**
```json
{
  "type": "command",
  "command": "startworkflow",
  "payload": {
    "workflowType": "create_application"
  }
}
```

**Server → Client:**
```json
{
  "type": "status",
  "data": {
    "workflowRunning": true,
    "busy": true,
    "currentStep": 3,
    "totalSteps": 10,
    "stepName": "Implement feature X"
  }
}
```

**Message Types:**

| Type | Direction | Description |
|------|-----------|-------------|
| `command` | Client → Server | Send commands (start, stop, continue, etc.) |
| `status` | Server → Client | Current workflow status |
| `log` | Server → Client | Log messages from LLM/tool execution |
| `error` | Server → Client | Error messages |
| `metrics` | Server → Client | Workflow metrics updates |

### REST API Endpoints

#### GET `/api/status`

Returns current workflow status.

**Response:**
```json
{
  "workflowRunning": false,
  "busy": false,
  "connectedClients": 0
}
```

#### GET `/api/metrics`

Returns workflow execution metrics.

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

---

## Troubleshooting

### Common Issues

#### Server won't start

| Cause | Solution |
|-------|----------|
| Port 5001 in use | Check with `lsof -i :5001`, change port in `appsettings.json` |
| .NET 7 not installed | Install .NET 7 SDK from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/7.0) |
| Invalid `appsettings.json` | Verify JSON is valid, check for syntax errors |

#### WebSocket connection fails

| Cause | Solution |
|-------|----------|
| Browser doesn't support WebSockets | Use a modern browser (Chrome, Firefox, Safari, Edge) |
| Firewall blocking localhost | Check firewall settings, allow localhost connections |
| Server not running | Verify server is running, check logs for errors |

#### 404 on pages

| Cause | Solution |
|-------|----------|
| ClientApp not in output | Verify `ClientApp` directory exists in build output |
| Wrong paths in appsettings | Check `appsettings.json` paths are correct |
| Build failed | Run `dotnet build` and check for errors |

#### LM Studio not responding

| Cause | Solution |
|-------|----------|
| LM Studio not running | Start LM Studio and load a model |
| Wrong URL | Verify configured URL matches LM Studio's address |
| API key required | Add `ApiKey` to `appsettings.json` if needed |
| Model not loaded | Ensure a model is loaded in LM Studio |

### Logs

Check `logs/metrics.json` for workflow execution details and errors.

The metrics file contains:
- Step execution timing
- Token usage per step
- Tool call results
- Error messages
- Workflow start/end timestamps

---

## Appendix

### Project Workspace Directory Layout

Each project workspace follows this structure:

```
projects/
  my-project/                    # Project name (directory)
    goals/                       # Project goals and requirements
      goal.md                    # Main goal document
    planning/                    # Planning documents
      planning.json              # Planning metadata
    design/                      # Design documents
      design.json                # Architecture overview
    solution/                    # Solution code
    tickets/                     # Ticket management
      tickets.json               # Open tickets
      completed.json             # Completed tickets
      skipped.json               # Skipped tickets
    files/                       # Supplementary files
```

### Workflow Types Directory

```
workflowtypes/
  create_application/            # Create new application workflow
    Architect-Step1.md           # Step 1: Define architecture
    Architect-Step2.md           # Step 2: Design data models
    Architect-Step3.md           # Step 3: Implement core logic
    Architect-Step4.md           # Step 4: Add testing
    Architect-Step5.md           # Step 5: Review and finalize
    Architect-Step5.json         # Metadata for ticket iteration
  chat/                          # General conversation workflow
  debug/                         # Debug code issues workflow
  new_feature/                   # New feature implementation
  benchmarks_game_design/        # Benchmark design workflow
  benchmarks_logic/              # Benchmark logic workflow
  add_epic/                      # Add epic-level features
  add_testing/                   # Add testing coverage
  reactor_user_interface/        # Refactor UI components
  code_review/                   # Code review workflow
```

### Quick Reference

| URL | Description |
|-----|-------------|
| `http://localhost:5001` | Web dashboard |
| `http://localhost:5001/api/status` | REST status endpoint |
| `http://localhost:5001/api/metrics` | REST metrics endpoint |
| `ws://localhost:5001/ws/agent` | WebSocket endpoint |
| `http://localhost:1234` | Default LM Studio URL |

---

*For more information, see [QUICKSTART.md](QUICKSTART.md), [WORKFLOWS.md](WORKFLOWS.md), and [TELEGRAM_GUIDE.md](TELEGRAM_GUIDE.md).*
