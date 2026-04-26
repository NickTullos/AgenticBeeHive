# Project Structure — ABHive

This document explains the organization of the ABHive application, including directory layout, project structure, and file organization.

## Table of Contents

1. [Directory Layout](#directory-layout)
2. [Solution Structure](#solution-structure)
3. [Project Details](#project-details)
4. [Key Components](#key-components)
5. [ClientApp (Frontend)](#clientapp-frontend)
6. [Projects Directory (Workspaces)]#projects-directory-workspaces)
7. [WorkflowTypes Directory](#workflowtypes-directory)
8. [Configuration Files](#configuration-files)
9. [Test Projects](#test-projects)
10. [Documentation](#documentation)

---

## Directory Layout

```
solution/
├── docs/                          # Documentation (this directory)
│   ├── README.md                  # Project overview and index
│   ├── QUICKSTART.md              # Quick start guide
│   ├── USERS_GUIDE.md             # Comprehensive user guide
│   ├── ARCHITECTURE_OVERVIEW.md   # Architecture details
│   ├── PROJECT_STRUCTURE.md       # This file
│   ├── WORKFLOWS.md               # Workflow authoring guide
│   └── TELEGRAM_GUIDE.md          # Telegram integration guide
│
├── src/
│   ├── ABHive/                    # Core domain library + CLI app
│   │   ├── ABHive.csproj          # Project file (Assembly: abHive)
│   │   ├── Application.cs         # Workflow orchestration
│   │   ├── Infrastructure.cs      # LLM client, tool executor
│   │   ├── AppSettings.cs         # Configuration model classes
│   │   ├── DataModels.cs          # Domain data structures
│   │   ├── MetricsLogger.cs       # Metrics tracking
│   │   ├── Presentation.cs        # Console presentation layer
│   │   ├── StepConversationService.cs
│   │   ├── ToolCache.cs
│   │   ├── ToolCallSafety.cs
│   │   ├── WorkspaceContext.cs
│   │   └── appsettings.json       # Source config (review before commit)
│   │
│   ├── ABHive.Web/                # Web application (ASP.NET Core)
│   │   ├── ABHive.Web.csproj      # Project file (Assembly: abHive.Web)
│   │   ├── Program.cs             # Entry point, service registration
│   │   ├── Controllers.cs         # REST API controllers
│   │   ├── WebSocketHandler.cs    # WebSocket communication
│   │   ├── TelegramBotService.cs  # Telegram bot background service
│   │   ├── ProjectDashboardService.cs
│   │   ├── ProjectWorkspaceService.cs
│   │   ├── WorkflowStateStore.cs
│   │   ├── WorkflowTypeCatalog.cs
│   │   ├── TicketIterationStatusResolver.cs
│   │   ├── DashboardTicketFileOps.cs
│   │   ├── WebOutputFormatter.cs
│   │   │
│   │   ├── ClientApp/             # Frontend single-page application
│   │   │   ├── index.html         # Main workflow interface
│   │   │   ├── dashboard.html     # Project dashboard
│   │   │   ├── settings.html      # Configuration page
│   │   │   │
│   │   │   ├── app.js             # Main application logic
│   │   │   ├── dashboard.js       # Dashboard functionality
│   │   │   ├── settings.js        # Settings management
│   │   │   ├── theme.js           # Theme management
│   │   │   │
│   │   │   ├── styles.css         # Custom styling
│   │   │   ├── theme.css          # Theme styles
│   │   │   │
│   │   │   └── lib/               # Local JavaScript libraries
│   │   │       ├── tailwind.min.js    # Tailwind CSS
│   │   │       ├── marked.min.js      # Markdown parser
│   │   │       └── dompurify.min.js   # HTML sanitizer
│   │   │
│   │   ├── docs/                  # Documentation (symlink or copy)
│   │   ├── workflowtypes/         # Workflow definitions (committed)
│   │   ├── projects/              # Project workspaces (default)
│   │   ├── Properties/
│   │   │   └── launchSettings.json
│   │   ├── appsettings.json       # ⚠️ User config (may contain secrets)
│   │   └── appsettings.defaults.json  # Safe default template
│   │
│   ├── ABHive.Tests/              # Unit tests (xUnit)
│   │   ├── ABHive.Tests.csproj
│   │   ├── AppSettingsTests.cs
│   │   ├── LLMClientIntegrationTests.cs
│   │   ├── LLMRequestTests.cs
│   │   ├── ProjectDashboardServiceTests.cs
│   │   ├── StepLoaderTests.cs
│   │   ├── TelegramBotServiceTests.cs
│   │   └── UnitTest1.cs
│   │
│   └── ABHive.IntegrationTests/ # Integration tests (xUnit)
│       ├── ABHive.IntegrationTests.csproj
│       ├── EndToEndWorkflowTests.cs
│       ├── TestConsoleOutputFormatter.cs
│       └── ToolCallingMetadataTests.cs
│
├── ABHive.sln                     # Solution file (4 projects)
├── .gitignore                     # Git ignore rules
└── README.md                      # Project README
```

---

## Solution Structure

The solution (`ABHive.sln`) contains **4 projects**:

| Project | Type | Assembly Name | Description |
|---------|------|---------------|-------------|
| `ABHive` | Class Library / Exe | `abHive` | Core domain logic and CLI app |
| `ABHive.Web` | Web Application | `abHive.Web` | ASP.NET Core web app with UI |
| `ABHive.Tests` | Test Project | — | Unit tests (xUnit) |
| `ABHive.IntegrationTests` | Test Project | — | Integration tests (xUnit) |

### Project Dependencies

```
ABHive.Web
    └── ABHive (project reference)

ABHive.Tests
    ├── ABHive.Web (project reference)
    └── ABHive (project reference)

ABHive.IntegrationTests
    └── ABHive (project reference)
```

---

## Project Details

### ABHive (Core)

**Path**: `src/ABHive/`
**Target Framework**: .NET 7.0
**Assembly Name**: `abHive`
**Output Type**: Exe (console application)

**Key Files:**

| File | Purpose |
|------|---------|
| `Application.cs` | Workflow orchestration, step execution, tool call handling |
| `Infrastructure.cs` | LLM client (HTTP API), tool executor interfaces |
| `AppSettings.cs` | Configuration model classes with validation |
| `DataModels.cs` | Domain data structures (LLMRequest, Step, ToolCall, etc.) |
| `MetricsLogger.cs` | Metrics tracking and persistence to JSON files |
| `Presentation.cs` | Console output formatting |
| `StepConversationService.cs` | Step-level conversation management |
| `ToolCache.cs` | Tool metadata caching |
| `ToolCallSafety.cs` | Tool call safety validation |
| `WorkspaceContext.cs` | Project workspace context management |
| `appsettings.json` | Source configuration (review before commit) |

### ABHive.Web (Web Application)

**Path**: `src/ABHive.Web/`
**Target Framework**: .NET 7.0
**Assembly Name**: `abHive.Web`
**Output Type**: Exe (web application)

**Key Files:**

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point, service registration, middleware configuration |
| `Controllers.cs` | REST API endpoints (`/api/status`, `/api/metrics`) |
| `WebSocketHandler.cs` | Bidirectional WebSocket communication (`/ws/agent`) |
| `TelegramBotService.cs` | Telegram bot background service (polling) |
| `ProjectDashboardService.cs` | Dashboard data aggregation |
| `ProjectWorkspaceService.cs` | Project workspace management |
| `WorkflowStateStore.cs` | In-memory workflow state management |
| `WorkflowTypeCatalog.cs` | Workflow type registry and loading |
| `TicketIterationStatusResolver.cs` | Ticket iteration state resolution |
| `DashboardTicketFileOps.cs` | Ticket file operations for dashboard |
| `WebOutputFormatter.cs` | Custom output formatting |

### ABHive.Tests (Unit Tests)

**Path**: `src/ABHive.Tests/`
**Target Framework**: .NET 7.0
**Test Framework**: xUnit

**Key Files:**

| File | Purpose |
|------|---------|
| `AppSettingsTests.cs` | Tests for AppSettings validation |
| `LLMClientIntegrationTests.cs` | Tests for LLM client functionality |
| `LLMRequestTests.cs` | Tests for LLMRequest serialization |
| `ProjectDashboardServiceTests.cs` | Tests for dashboard service |
| `StepLoaderTests.cs` | Tests for step loading |
| `TelegramBotServiceTests.cs` | Tests for Telegram bot service |

### ABHive.IntegrationTests (Integration Tests)

**Path**: `src/ABHive.IntegrationTests/`
**Target Framework**: .NET 7.0
**Test Framework**: xUnit

**Key Files:**

| File | Purpose |
|------|---------|
| `EndToEndWorkflowTests.cs` | End-to-end workflow tests |
| `TestConsoleOutputFormatter.cs` | Test output formatting |
| `ToolCallingMetadataTests.cs` | Tool calling metadata tests |

---

## Key Components

### WorkflowOrchestrator (Application.cs)

The central orchestrator manages workflow lifecycle:

```
WorkflowOrchestrator
├── ExecuteStepAsync()          # Main step execution
├── LoadStepsAsync()            # Scan for .md files
├── ProcessStepAsync()          # Execute individual steps
├── HandleToolCallsAsync()      # Execute tool calls
└── CollectMetrics()            # Track metrics
```

### LLMClient (Infrastructure.cs)

Handles LLM communication with multi-server support:

```
LLMClient
├── GenerateAsync()             # Send request, get response
├── HandleToolCalls()           # Process tool calls from response
├── GetActiveServer()           # Resolve active LLM server
└── GetActiveModel()            # Resolve active model
```

### WebSocketHandler

Manages WebSocket connections:

```
WebSocketHandler
├── ConnectAsync()              # Handle new connections
├── ProcessMessage()            # Handle incoming messages
├── SendAsync()                 # Send to connected clients
├── BroadcastAsync()            # Broadcast to all clients
└── OnDisconnect()              # Cleanup on disconnect
```

### TelegramBotService

Background service for Telegram integration:

```
TelegramBotService
├── PollUpdates()               # Poll Telegram API
├── ProcessCommand()            # Parse and execute commands
├── SendResponse()              # Send message to chat
├── SendStatusUpdate()          # Send status notification
└── ValidateChatId()            # Whitelist check
```

---

## ClientApp (Frontend)

### Directory Structure

```
ClientApp/
├── index.html         # Main workflow interface
├── dashboard.html     # Project dashboard
├── settings.html      # Configuration page
├── app.js             # Main application logic
├── dashboard.js       # Dashboard functionality
├── settings.js        # Settings management
├── theme.js           # Theme management
├── styles.css         # Custom styling
├── theme.css          # Theme styles
└── lib/               # Local JavaScript libraries
    ├── tailwind.min.js    # Tailwind CSS (local)
    ├── marked.min.js      # Markdown parser (local)
    └── dompurify.min.js   # HTML sanitizer (local)
```

### Key Files

| File | Purpose |
|------|---------|
| `index.html` | Main workflow interface with status, steps, logs, controls |
| `dashboard.html` | Project dashboard with architecture overview, planning, tickets |
| `settings.html` | Application configuration (LLM servers, tools, Telegram) |
| `app.js` | WebSocket client, workflow control, log streaming |
| `dashboard.js` | Dashboard rendering, architecture overview panel, ticket display |
| `settings.js` | Settings form handling, LLM server management, tool config |
| `theme.js` | Light/dark theme toggle, theme persistence |
| `styles.css` | Custom ABHive component styles |
| `theme.css` | Theme-specific CSS variables and styles |

### Libraries (Local Hosting)

All JavaScript libraries are hosted locally for offline capability:

| Library | Purpose | Version |
|---------|---------|---------|
| `tailwind.min.js` | Utility-first CSS framework | Latest |
| `marked.min.js` | Markdown to HTML parser | Latest |
| `dompurify.min.js` | HTML sanitizer | Latest |

---

## Projects Directory (Workspaces)

### Structure

The `projects/` directory is the default location for project workspaces:

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

### Project Workspace Services

| Service | Purpose |
|---------|---------|
| `ProjectWorkspaceService` | Manages project workspace operations |
| `DashboardTicketFileOps` | Ticket file operations for dashboard |
| `TicketIterationStatusResolver` | Resolves ticket iteration state |

### Example Project: helloworld

```
projects/
  helloworld/
    goals/
      goal.md
    planning/
      planning.json
    design/
      architecture-design.json
    solution/
    tickets/
      tickets.json
      completed.json
```

---

## WorkflowTypes Directory

### Structure

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
    Step1_sim.md
    Step2_avg.md
    Step3_hard.md
    Step4_max.md
    Step4_max_with_libs.md
  benchmarks_logic/              # Benchmark logic workflow
    Step1_simple.md
    Step2_avg.md
    Step3_hard.md
  add_epic/                      # Add epic-level features
    Step1.md
    Step2.md
  add_testing/                   # Add testing coverage
    testing.md
  reactor_user_interface/        # Refactor UI components
    refactor.md
  code_review/                   # Code review workflow
    DebugCode.md
```

### Workflow Rules

1. **Step execution order**: Alphabetical by markdown file name
2. **Step files**: Must be `*.md`
3. **Metadata files**: Optional `<StepName>.json` in the same directory as `X.md`
4. **Path tokens**: `{{KEYWORDS}}` are replaced at runtime (see WORKFLOWS.md)

### Built-in Workflow Types

| Workflow | Steps | Description |
|----------|-------|-------------|
| `create_application` | 5 | Guide through architecting a new application |
| `chat` | Variable | General-purpose conversation |
| `debug` | Variable | Debug code issues step by step |
| `new_feature` | Variable | Implement new features |
| `benchmarks_game_design` | 4 | Design benchmarks for performance |
| `benchmarks_logic` | 3 | Implement benchmark logic |
| `add_epic` | 2 | Add epic-level features |
| `add_testing` | 1 | Add testing coverage |
| `reactor_user_interface` | 1 | Refactor UI components |
| `code_review` | 1 | Review and debug code |

---

## Configuration Files

### appsettings.json (ABHive.Web)

Located in `src/ABHive.Web/appsettings.json`. **Never commit this file if it contains secrets.**

**Key Settings:**

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `Kestrel.Endpoints.Http.Url` | string | `http://localhost:5001` | Server listen address |
| `LlmServers` | array | `[]` | Array of LLM server configurations |
| `ActiveLlmServerId` | string | `"default-server"` | Active server ID |
| `ActiveLlmModelId` | string | `"default-model"` | Active model ID |
| `StepsDirectory` | string | `./workflowtypes/chat` | Default workflow directory |
| `WorkflowTypesDirectory` | string | `./workflowtypes` | Root workflow types folder |
| `LogFilePath` | string | `./logs/metrics.json` | Metrics output file |
| `DefaultToolTimeoutMs` | int | `60000` | Tool execution timeout (ms) |
| `LlmTemperature` | double | `0.7` | LLM temperature (0–2) |
| `LlmTopP` | double | `1.0` | LLM top-p (0–1) |
| `LlmTopK` | int | `0` | LLM top-k (0 = disabled) |
| `LlmMaxTokens` | int | `0` | Max tokens (0 = unlimited) |
| `LlmFrequencyPenalty` | double | `0.0` | Frequency penalty (-2–2) |
| `LlmPresencePenalty` | double | `0.0` | Presence penalty (-2–2) |
| `LlmStopSequences` | string | `""` | Stop sequences |
| `TelegramEnabled` | bool | `false` | Enable Telegram bot |
| `TelegramBotToken` | string | `""` | Telegram bot token |
| `TelegramChatId` | long | `0` | Telegram chat ID |
| `TelegramPollTimeoutSeconds` | int | `20` | Polling interval (seconds) |
| `TelegramSwitchContextMessageCount` | int | `5` | Context switch threshold |
| `ToolConfigs` | object | `{}` | Tool enable/disable |
| `ProjectRootDirectory` | string | `./projects` | Project workspaces directory |

### appsettings.defaults.json

Located in `src/ABHive.Web/appsettings.defaults.json`. Safe template with no secrets.

### appsettings.old.json

Old backup file — should be deleted before release.

---

## Test Projects

### ABHive.Tests

Unit tests for core functionality:

```
ABHive.Tests/
├── AppSettingsTests.cs          # Configuration validation
├── LLMClientIntegrationTests.cs # LLM client tests
├── LLMRequestTests.cs           # LLMRequest serialization
├── ProjectDashboardServiceTests.cs # Dashboard service
├── StepLoaderTests.cs           # Step loading
├── TelegramBotServiceTests.cs   # Telegram bot
└── UnitTest1.cs                 # Base test class
```

### ABHive.IntegrationTests

Integration tests for end-to-end scenarios:

```
ABHive.IntegrationTests/
├── EndToEndWorkflowTests.cs     # Full workflow tests
├── TestConsoleOutputFormatter.cs # Test output
└── ToolCallingMetadataTests.cs  # Tool calling metadata
```

---

## Documentation

All documentation is in the `docs/` directory:

| Document | Purpose |
|----------|---------|
| `README.md` | Project overview and documentation index |
| `QUICKSTART.md` | Quick start guide (5 minutes) |
| `USERS_GUIDE.md` | Comprehensive user guide |
| `ARCHITECTURE_OVERVIEW.md` | System architecture details |
| `PROJECT_STRUCTURE.md` | This file — directory layout |
| `WORKFLOWS.md` | Workflow authoring guide |
| `TELEGRAM_GUIDE.md` | Telegram bot integration |

---

## Build Configuration

### Solution File

**Path**: `ABHive.sln`
**Format**: Visual Studio Solution File 12.00
**Projects**: 4 (ABHive, ABHive.Web, ABHive.Tests, ABHive.IntegrationTests)

### Project Files

| Project | File | Target Framework | Assembly |
|---------|------|-----------------|----------|
| ABHive | `ABHive.csproj` | net7.0 | abHive |
| ABHive.Web | `ABHive.Web.csproj` | net7.0 | abHive.Web |
| ABHive.Tests | `ABHive.Tests.csproj` | net7.0 | — |
| ABHive.IntegrationTests | `ABHive.IntegrationTests.csproj` | net7.0 | — |

### Key Packages

| Package | Version | Used By |
|---------|---------|---------|
| Microsoft.Extensions.Hosting | 8.0.0 | ABHive |
| Microsoft.Extensions.Http | 8.0.0 | ABHive |
| Microsoft.AspNetCore.Mvc | 2.1.3 | ABHive.Web |
| Microsoft.AspNetCore.WebSockets | 2.1.1 | ABHive.Web |
| Microsoft.NET.Test.Sdk | 17.8.0+ | Tests |
| xunit | 2.5.3+ | Tests |
| xunit.runner.visualstudio | 2.5.3+ | Tests |

---

## Git Ignore Rules

### .gitignore (Root)

```
bin/
obj/
.vs/
.DS_Store
*.user
*.suo
*.bak
logs/
*.log
```

### .gitignore (ABHive.Web)

```
appsettings.json
appsettings.Production.json
logs/
bin/
obj/
*.user
*.csproj.user
*.suo
*.userosscache
```

### Files to Review Before Committing

| File | Action |
|------|--------|
| `appsettings.json` (ABHive.Web) | Review for secrets, remove before release |
| `appsettings.old.json` | Delete before release |
| `launchSettings.json` | Review hardcoded paths |
| `projects/` | Remove test data before release |
| `bin/`, `obj/`, `.vs/` | Already in .gitignore |

---

*For more information, see the [User's Guide](USERS_GUIDE.md), [Architecture Overview](ARCHITECTURE_OVERVIEW.md), and [README.md](README.md).*
