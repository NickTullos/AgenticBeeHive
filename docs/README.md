# ABHive — Agentic BeeHive a LLM Programming Application

> A self-hosted web application for agentic LLM programming that loads MD files, executes steps using a local LLM, dynamically invokes tools, and provides real-time dashboard + Telegram bot integration.

## 📥 Precompiled Binaries

Download ready-to-run binaries for your platform:

| Platform | Download |
|----------|----------|
| **Windows** (x64 / arm64) | [Download](https://github.com/NickTullos/AgenticBeeHive/tree/main/build/dist/windows) |
| **macOS** (Intel / Apple Silicon) | [Download](https://github.com/NickTullos/AgenticBeeHive/tree/main/build/dist/macos) |
| **Linux** (x64 / arm64) | [Download](https://github.com/NickTullos/AgenticBeeHive/tree/main/build/dist/linux) |

> **Note:** Open your OS folder and select the architecture zip (`x64` or `arm64`). All builds are under [build/dist](https://github.com/NickTullos/AgenticBeeHive/tree/main/build/dist). During release builds, bump only `solution/src/version.json` `version` (canonical source); scripts auto-sync `assets` names and project MSBuild version metadata. `appsettings.json` `CurrentVersion` is legacy fallback only.

## 📚 Documentation Index
All documentation is located in the `docs/` directory:

| Document | Description |
|----------|-------------|
| **[User's Guide](USERS_GUIDE.md)** | Comprehensive guide for end users — installation, usage, configuration |
| **[Quick Start](QUICKSTART.md)** | Get running in 5 minutes |
| **[Architecture Overview](ARCHITECTURE_OVERVIEW.md)** | System design, components, and UI features |
| **[Project Structure](PROJECT_STRUCTURE.md)** | Directory layout and organization |
| **[Workflow Authoring](WORKFLOWS.md)** | Creating custom workflows and steps |
| **[Telegram Guide](TELEGRAM_GUIDE.md)** | Bot setup, commands, and best practices |
| **[Release Builds](RELEASE_BUILDS.md)** | Standalone build scripts, versioning, and update-check flow |

## 🚀 Quick Links

- **Get Started**: Read [QUICKSTART.md](QUICKSTART.md) first
- **Full Documentation**: See [User's Guide](USERS_GUIDE.md)
- **API Reference**: See [User's Guide](USERS_GUIDE.md#api-reference) for REST and WebSocket endpoints
- **Workflow Authoring**: See [WORKFLOWS.md](WORKFLOWS.md)
- **Release Packaging**: See [RELEASE_BUILDS.md](RELEASE_BUILDS.md)

## 🏗️ Project Overview

ABHive is built with:

- **Backend**: .NET 7, ASP.NET Core with Kestrel web server
- **Frontend**: HTML5, ES6+ JavaScript, Tailwind CSS (all JS libraries hosted locally)
- **Real-time**: WebSocket protocol for browser-server communication
- **Self-hosted**: Standalone executable with no external dependencies

### Key Features

1. **LLM Integration** — Connects to local models via LM Studio or any OpenAI-compatible API
2. **Multi-Server Support** — Configure multiple LLM servers with different models
3. **Step Processing** — Scans directories for `.md` files, processes one at a time
4. **Tool System** — Built-in tools: `Bash`, `WebFetch`, `ReadFile`, `WriteFile`
5. **Agent Workflow** — LLM decides which tools to call for each step
6. **Web Dashboard** — Real-time progress tracking with architecture visualization
7. **Telegram Bot** — Remote workflow management via bot commands
8. **Ticket Iteration** — Process one ticket at a time with automatic progression
9. **Error Handling** — Manual intervention on failure with clear error messages
10. **Output** — Console + file logging with detailed metrics

## 📂 Repository Structure

```
solution/
├── docs/                          # Documentation (this directory)
│   ├── README.md                  # This file
│   ├── QUICKSTART.md              # Quick start guide
│   ├── USERS_GUIDE.md             # Comprehensive user guide
│   ├── ARCHITECTURE_OVERVIEW.md   # Architecture details
│   ├── PROJECT_STRUCTURE.md       # Directory layout
│   ├── WORKFLOWS.md               # Workflow authoring guide
│   ├── TELEGRAM_GUIDE.md          # Telegram bot integration
│   └── RELEASE_BUILDS.md          # Standalone release build/version guide
├── src/
│   ├── ABHive/                    # Core domain library + CLI app
│   │   ├── ABHive.csproj
│   │   ├── Application.cs         # Workflow orchestration
│   │   ├── Infrastructure.cs      # LLM client, tool executor
│   │   ├── AppSettings.cs         # Configuration model
│   │   ├── DataModels.cs          # Domain data models
│   │   ├── MetricsLogger.cs       # Metrics tracking
│   │   ├── Presentation.cs        # Console presentation
│   │   ├── StepConversationService.cs
│   │   ├── ToolCache.cs
│   │   ├── ToolCallSafety.cs
│   │   ├── WorkspaceContext.cs
│   │   └── appsettings.json       # Source config (review before commit)
│   ├── ABHive.Web/                # Web application (ASP.NET Core)
│   │   ├── ABHive.Web.csproj
│   │   ├── Program.cs             # Entry point, service registration
│   │   ├── Controllers.cs         # API controllers
│   │   ├── WebSocketHandler.cs    # WebSocket communication
│   │   ├── TelegramBotService.cs  # Telegram bot background service
│   │   ├── ProjectDashboardService.cs
│   │   ├── ProjectWorkspaceService.cs
│   │   ├── WorkflowStateStore.cs
│   │   ├── WorkflowTypeCatalog.cs
│   │   ├── TicketIterationStatusResolver.cs
│   │   ├── DashboardTicketFileOps.cs
│   │   ├── WebOutputFormatter.cs
│   │   ├── ClientApp/             # Frontend (HTML/JS/CSS)
│   │   │   ├── index.html         # Main workflow interface
│   │   │   ├── dashboard.html     # Project dashboard
│   │   │   ├── settings.html      # Configuration page
│   │   │   ├── app.js             # Main application logic
│   │   │   ├── dashboard.js       # Dashboard functionality
│   │   │   ├── settings.js        # Settings management
│   │   │   ├── theme.js           # Theme management
│   │   │   ├── styles.css         # Custom styling
│   │   │   ├── theme.css          # Theme styles
│   │   │   └── lib/               # Local JS libraries
│   │   │       ├── tailwind.min.js
│   │   │       ├── marked.min.js
│   │   │       └── dompurify.min.js
│   │   ├── docs/                  # Documentation (symlink to parent docs/)
│   │   ├── workflowtypes/         # Workflow definitions (committed)
│   │   ├── projects/              # Project workspaces (default location)
│   │   ├── appsettings.json       # ⚠️ User config (may contain secrets)
│   │   ├── appsettings.defaults.json  # Safe default template
│   │   └── Properties/
│   │       └── launchSettings.json
│   ├── ABHive.Tests/              # Unit tests (xUnit)
│   └── ABHive.IntegrationTests/   # Integration tests (xUnit)
├── ABHive.sln                     # Solution file
├── .gitignore                     # Git ignore rules
└── README.md                      # This file
```

## 🛠️ Getting Started

### Prerequisites

- .NET 7 SDK installed
- LM Studio running locally (or configure in `appsettings.json`)

### Build & Run

```bash
cd solution
dotnet restore
dotnet build
dotnet run --project src/ABHive.Web
```

The server will start on `http://localhost:5001`.

### Configuration

Create `appsettings.json` in the `src/ABHive.Web/` output directory:

```json
{
  "LlmServers": [
    {
      "Id": "default-server",
      "Name": "Default Server",
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

> ⚠️ **Important**: Never commit `appsettings.json` if it contains secrets. Use `appsettings.defaults.json` as a template and keep your config file in `.gitignore`.

## 📖 Documentation Map

### For New Users
1. Read [QUICKSTART.md](QUICKSTART.md)
2. Read [User's Guide](USERS_GUIDE.md)
3. Set up your first project workspace
4. Try the web dashboard at `http://localhost:5001`

### For Intermediate Users
1. Master workflow authoring — see [WORKFLOWS.md](WORKFLOWS.md)
2. Configure Telegram integration — see [Telegram Guide](TELEGRAM_GUIDE.md)
3. Customize dashboard features
4. Optimize performance with LLM parameters

### For Advanced Users
1. Build custom tools
2. Create custom workflows
3. Integrate with CI/CD
4. Contribute to the project

## 🔌 API Reference

### REST Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/status` | GET | Current workflow status |
| `/api/metrics` | GET | Workflow execution metrics |

**Status Response Example:**
```json
{
  "workflowRunning": false,
  "busy": false,
  "connectedClients": 0
}
```

**Metrics Response Example:**
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

### WebSocket

- **Endpoint**: `/ws/agent`
- **Protocol**: Bidirectional WebSocket
- **Purpose**: Real-time progress updates, command sending, log streaming
- **Details**: See [User's Guide](USERS_GUIDE.md#api-reference) for full protocol specification

## 📋 Ticket Iteration

ABHive supports a ticket iteration mode for processing one ticket at a time:

1. **Configuration**: Steps can be configured for `ticketIteration` mode
2. **Ticket Source**: Load tickets from `tickets.json`
3. **Completion Tracking**: Mark tickets complete in `completed.json`
4. **Automatic Progression**: Workflow advances to next incomplete ticket

See [User's Guide](USERS_GUIDE.md#ticket-iteration) for details.

## 🔧 Troubleshooting

### Server won't start
- Check that port `5001` is not in use: `lsof -i :5001`
- Check LM Studio is running at configured URL
- Verify .NET 7 SDK/runtime is installed

### WebSocket connection fails
- Ensure browser supports WebSockets (all modern browsers do)
- Check server logs for WebSocket errors
- Verify no firewall blocking localhost connections

### 404 on pages
- Ensure `ClientApp` directory is in the output folder
- Check `appsettings.json` paths are correct

### LM Studio not responding
- Verify LM Studio is running
- Check configured URL matches LM Studio's address
- Ensure no API key required (or add to `appsettings.json`)

## 📝 Logs

Check `logs/metrics.json` for workflow execution details and errors.

## 📅 Documentation Status

| Document | Status | Last Updated |
|----------|--------|--------------|
| README.md | ✅ Complete | April 2026 |
| QUICKSTART.md | ✅ Updated | April 2026 |
| User's Guide | ✅ Updated | April 2026 |
| Telegram Guide | ✅ Updated | April 2026 |
| Architecture Overview | ✅ Updated | April 2026 |
| Project Structure | ✅ Updated | April 2026 |
| WORKFLOWS.md | ✅ Updated | April 2026 |

## 🤝 Contributing

Found something missing or unclear? Please:
1. Check existing issues
2. Create a new issue describing the gap
3. Submit a pull request with your improvements!

## 📄 License

[Add your license information here]

---

*ABHive — Agentic LLM Programming Application*
*Last updated: April 2026*
