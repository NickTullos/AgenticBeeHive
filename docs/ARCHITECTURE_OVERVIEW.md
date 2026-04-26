# Architecture Overview — ABHive

This document explains the architecture of ABHive, including system design, component relationships, and UI features.

## Table of Contents

1. [System Architecture](#system-architecture)
2. [Component Diagram](#component-diagram)
3. [Layered Architecture](#layered-architecture)
4. [Key Components](#key-components)
5. [Data Models](#data-models)
6. [UI Architecture](#ui-architecture)
7. [Architecture Overview Panel](#architecture-overview-panel)
8. [Data Flow](#data-flow)
9. [Design Patterns](#design-patterns)
10. [Extensibility](#extensibility)

---

## System Architecture

ABHive follows a **layered architecture** pattern with the following layers:

```
┌─────────────────────────────────────────────────────────┐
│                   PRESENTATION LAYER                     │
│  ┌─────────────┐  ┌──────────────┐  ┌───────────────┐  │
│  │   Web UI     │  │ Telegram Bot │  │   Console     │  │
│  │  (Dashboard) │  │   (Service)  │  │   (CLI)       │  │
│  └──────┬──────┘  └──────┬───────┘  └───────┬───────┘  │
├─────────┼────────────────┼──────────────────┼──────────┤
│         │                │                  │          │
│  ┌──────▼────────────────▼──────────────────▼───────┐  │
│  │              APPLICATION LAYER                     │  │
│  │  ┌─────────────────────────────────────────────┐  │  │
│  │  │          Workflow Orchestrator               │  │  │
│  │  │  - StepLoader (loads .md files)             │  │  │
│  │  │  - WorkflowTypeCatalog (workflow registry)  │  │  │
│  │  │  - WorkflowStateStore (state management)    │  │  │
│  │  └─────────────────────────────────────────────┘  │  │
│  └────────────────────────┬──────────────────────────┘  │
├────────────────────────────┼────────────────────────────┤
│                            │                            │
│  ┌────────────────────────▼──────────────────────────┐  │
│  │              DOMAIN LAYER                          │  │
│  │  ┌─────────────────────────────────────────────┐  │  │
│  │  │     Core Business Logic                      │  │  │
│  │  │     - LLMRequest / LLMResponse              │  │  │
│  │  │     - Step / StepExecutionResult            │  │  │
│  │  │     - ToolDefinition / ToolCall             │  │  │
│  │  │     - AppSettings / ToolConfig              │  │  │
│  │  │     - WorkflowMetrics                         │  │  │
│  │  └─────────────────────────────────────────────┘  │  │
│  └────────────────────────┬──────────────────────────┘  │
├────────────────────────────┼────────────────────────────┤
│                            │                            │
│  ┌────────────────────────▼──────────────────────────┐  │
│  │            INFRASTRUCTURE LAYER                    │  │
│  │  ┌─────────────┐  ┌──────────────┐  ┌─────────┐  │  │
│  │  │ LLM Client  │  │ ToolExecutor │  │ Metrics │  │  │
│  │  │ (HTTP API)  │  │ (Bash,       │  │ Logger  │  │  │
│  │  │             │  │  WebFetch,    │  │         │  │  │
│  │  │             │  │  ReadFile,    │  │         │  │  │
│  │  │             │  │  WriteFile)   │  │         │  │  │
│  │  └─────────────┘  └──────────────┘  └─────────┘  │  │
│  └──────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
```

---

## Component Diagram

```
┌──────────────────────────────────────────────────────────────────────┐
│                        ABHive System                                 │
│                                                                      │
│  ┌─────────────────┐    ┌─────────────────┐    ┌────────────────┐  │
│  │   ABHive.Web    │    │   ABHive (Core) │    │   ABHive.Tests │  │
│  │                 │    │                 │    │                │  │
│  │  - Program.cs   │───▶│  - Application │◀───│  - Unit Tests  │  │
│  │  - Controllers  │    │  - Infrastructure│    │                │  │
│  │  - WebSocket    │    │  - DataModels  │    │                │  │
│  │  - TelegramBot  │    │  - MetricsLog  │    │                │  │
│  │  - DashboardSvc │    │  - StepConvSvc │    │                │  │
│  │  - WorkspaceSvc │    │  - ToolCache   │    │                │  │
│  │  - StateStore   │    │  - ToolSafety  │    │                │  │
│  │  - TypeCatalog  │    │  - WorkspaceCtx│    │                │  │
│  │  - StatusResolv │    └─────────────────┘    └────────────────┘  │
│  └─────────────────┘                      │
│         │                                 │
│         │  HTTP / WebSocket               │  CLI
│         ▼                                 ▼
│  ┌─────────────────┐              ┌─────────────────┐
│  │   Web Browser    │              │   Console/CLI    │
│  └─────────────────┘              └─────────────────┘
│
│  ┌─────────────────┐    ┌─────────────────┐
│  │  LLM Servers     │    │  Telegram API    │
│  │  (LM Studio,     │    │                 │
│  │   llama.cpp,     │    │                 │
│  │   OpenAI compat) │    │                 │
│  └─────────────────┘    └─────────────────┘
└──────────────────────────────────────────────────────────────────────┘
```

---

## Layered Architecture

### 1. Presentation Layer

**ABHive.Web** — ASP.NET Core web application

- **Program.cs** — Entry point, service registration, middleware configuration
- **Controllers.cs** — REST API endpoints (`/api/status`, `/api/metrics`)
- **WebSocketHandler.cs** — Bidirectional WebSocket communication (`/ws/agent`)
- **TelegramBotService.cs** — Telegram bot background service (polling)
- **ProjectDashboardService.cs** — Dashboard data aggregation
- **ProjectWorkspaceService.cs** — Project workspace management
- **WorkflowStateStore.cs** — In-memory workflow state management
- **WorkflowTypeCatalog.cs** — Workflow type registry and loading
- **TicketIterationStatusResolver.cs** — Ticket iteration state resolution
- **DashboardTicketFileOps.cs** — Ticket file operations for dashboard
- **WebOutputFormatter.cs** — Custom output formatting

**ClientApp/** — Frontend single-page application

- `index.html` — Main workflow interface
- `dashboard.html` — Project dashboard with architecture overview
- `settings.html` — Application configuration page
- `app.js` — Main application logic, WebSocket client
- `dashboard.js` — Dashboard functionality, rendering
- `settings.js` — Settings management
- `theme.js` / `theme.css` — Theme management
- `styles.css` — Custom styling
- `lib/` — Local JavaScript libraries (tailwind.min.js, marked.min.js, dompurify.min.js)

### 2. Application Layer

**WorkflowOrchestrator** — Core workflow execution engine

- `ExecuteStepAsync()` — Main step execution loop
- `LoadStepsAsync()` — Scan directories for `.md` files
- `ProcessStepAsync()` — Execute individual steps with LLM and tools
- `HandleToolCallsAsync()` — Execute tool calls from LLM responses
- `CollectMetrics()` — Track timing, token usage, success/failure rates

**StepLoader** — Step file discovery and parsing

- Scans directories for `*.md` files
- Loads optional `<StepName>.json` metadata files
- Replaces `{{KEYWORDS}}` path tokens in step content

### 3. Domain Layer

**ABHive (Core)** — Domain models and business logic

- `DataModels.cs` — Domain data structures:
  - `LLMRequest` / `LLMResponse` — LLM communication models
  - `Step` / `StepExecutionResult` — Step execution tracking
  - `ChatMessage` — Message role/content model
  - `ToolDefinition` / `ToolCall` — Tool system models
  - `ToolResult` — Tool execution results
  - `TokenUsage` — Token counting
  - `WorkflowMetrics` — Aggregated metrics
  - `ToolConfig` — Tool configuration
  - `AppSettings` — Application configuration model
  - `LlmServerConfig` / `LlmModelConfig` — LLM server configuration

- `Application.cs` — Workflow orchestration logic
- `Infrastructure.cs` — LLM client and tool executor interfaces
- `AppSettings.cs` — Configuration model classes with validation
- `MetricsLogger.cs` — Metrics tracking and persistence
- `StepConversationService.cs` — Step-level conversation management
- `ToolCache.cs` — Tool metadata caching
- `ToolCallSafety.cs` — Tool call safety validation
- `WorkspaceContext.cs` — Project workspace context management

### 4. Infrastructure Layer

**LLMClient** — HTTP client for OpenAI-compatible APIs

- Connects to `LlmServers` array (multi-server support)
- Uses `ActiveLlmServerId` / `ActiveLlmModelId` for selection
- Handles streaming and non-streaming responses
- Passes LLM generation parameters (Temperature, TopP, TopK, MaxTokens, penalties)
- Serializes `null` nullable properties as omitted (via `[JsonIgnore]`)

**ToolExecutor** — Tool execution system

- **Bash**: Executes shell commands on the host system
- **WebFetch**: Retrieves web content (Markdown or text) from URLs
- **ReadFile**: Reads file contents from the local filesystem
- **WriteFile**: Writes content to files on the local filesystem

Each tool is configured via `ToolConfigs` in `appsettings.json` and can be enabled/disabled.

**MetricsLogger** — Metrics persistence

- Writes metrics to `LogFilePath` (default `./logs/metrics.json`)
- Tracks timing, token usage, success/failure rates
- JSON-formatted output for structured metrics

---

## Key Components

### WorkflowOrchestrator

The central orchestrator manages the lifecycle of workflow execution:

1. **Initialization**: Loads workflow types from `workflowtypes/` directory
2. **Step Loading**: Scans for `*.md` files in the selected workflow directory
3. **Step Execution**: For each step:
   - Loads step content and optional metadata
   - Replaces `{{KEYWORDS}}` path tokens
   - Sends LLM request with step content as system/user messages
   - Processes LLM response (content or tool calls)
   - Executes any tool calls
   - Feeds tool results back to LLM
   - Logs metrics for the step
4. **State Management**: Tracks current step, workflow status, and metrics
5. **Error Handling**: Manual intervention on failure

### LLMClient

Handles all LLM communication:

- **Multi-server support**: Configures multiple LLM servers in `LlmServers` array
- **Model selection**: Uses `ActiveLlmServerId` and `ActiveLlmModelId` to select the active server and model
- **Generation parameters**: Passes Temperature, TopP, TopK, MaxTokens, FrequencyPenalty, PresencePenalty, StopSequences
- **Null handling**: Nullable properties with `null` values are omitted from JSON via `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`
- **API compatibility**: Works with LM Studio and any OpenAI-compatible API

### ToolExecutor

Executes tools dynamically based on LLM requests:

- **Bash**: Executes shell commands with timeout and error capture
- **WebFetch**: HTTP GET requests with content type detection
- **ReadFile**: Reads files with path validation
- **WriteFile**: Writes files with directory creation and content encoding

### TelegramBotService

Background service for Telegram integration:

- **Polling**: Polls Telegram API at `TelegramPollTimeoutSeconds` intervals
- **Commands**: Handles `/start`, `/help`, `/status`, `/startworkflow`, `/continue`, `/stop`, `/reset`, `/ticket`
- **Updates**: Sends automatic status updates for workflow events
- **Security**: Whitelist by `TelegramChatId`, rate limiting, command logging

---

## Data Models

### LLMRequest

```csharp
public class LLMRequest
{
    public string Model { get; set; }
    public List<ChatMessage> Messages { get; set; }
    public List<ToolDefinition> Tools { get; set; }
    public double Temperature { get; set; }
    
    // Generation parameters (nullable, omitted when null)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TopP { get; set; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TopK { get; set; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; set; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? FrequencyPenalty { get; set; }
    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? PresencePenalty { get; set; }
}
```

### Step

```csharp
public class Step
{
    public string Id { get; set; }
    public string FilePath { get; set; }
    public string Content { get; set; }
    public int Order { get; set; }
    public StepStatus Status { get; set; }
}
```

### StepExecutionResult

```csharp
public class StepExecutionResult
{
    public string StepId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public long DurationMs { get; set; }
    public bool Success { get; set; }
    public LLMResponse LLMResponse { get; set; }
    public List<ToolResult> ToolResults { get; set; }
    public string Error { get; set; }
}
```

### WorkflowMetrics

```csharp
public class WorkflowMetrics
{
    public int TotalSteps { get; set; }
    public int SuccessfulSteps { get; set; }
    public int FailedSteps { get; set; }
    public long TotalDurationMs { get; set; }
    public double AverageStepDurationMs { get; set; }
    public int TotalTokensUsed { get; set; }
}
```

### LlmServerConfig

```csharp
public class LlmServerConfig
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string BaseUrl { get; set; }
    public string ApiKey { get; set; }
    public string DefaultModelId { get; set; }
    public List<LlmModelConfig> Models { get; set; }
}
```

---

## UI Architecture

### Dashboard Architecture

The dashboard uses a **component-based architecture** with the following sections:

1. **Project Summary** — Displays current workflow status, step progress, and ticket health
2. **Architecture Overview** — Renders design JSON with key-value grid for JSON data, component chips, and data model badges
3. **Planning & Q&A** — Displays planning.json content with formatted paragraphs
4. **Tickets** — Shows open, skipped, and completed tickets with status indicators

### Architecture Overview Panel

The **Architecture Overview** panel intelligently renders content:

- **JSON Objects**: Rendered as a 2-column key-value grid
- **Plain Text**: Displayed as formatted paragraphs
- **Empty/No Data**: Shows "Pending" status

**Component Visualization:**
- Components displayed as cyan-themed chips/badges
- Count badge showing total number of components
- Flex-wrap for responsive layout

**Data Models Display:**
- Emerald/green color scheme
- Count badge with data model count
- Clear visual distinction from components

**Project Structure Viewer:**
- Expandable section for full project structure JSON
- Collapsible to save space
- Monospace font for readability

### Settings Page

The settings page provides a comprehensive configuration interface:

1. **LLM Servers** — Add, edit, remove servers; manage models
2. **Active Model** — Select active server and model
3. **LLM Generation Parameters** — Temperature, TopP, TopK, MaxTokens, penalties, StopSequences
4. **Tool Configuration** — Enable/disable Bash, WebFetch, ReadFile, WriteFile
5. **Telegram Settings** — Enable/disable bot, configure token and chat ID
6. **Path Settings** — Workflow types directory, project root directory
7. **Other Settings** — Timeout, polling interval, switch context count

### Theme System

- **theme.js** / **theme.css** — Theme management with light/dark mode
- **Tailwind CSS** — Utility-first CSS framework (loaded locally)
- **Custom styling** — ABHive-specific components and layouts

---

## Architecture Overview Panel

### What Changed?

The Architecture Overview panel provides an enhanced presentation of design information, especially when your Design Overview contains JSON data.

### Key Features

#### 1. Dynamic Content Rendering

The panel intelligently detects and formats content:

- **JSON Objects**: Rendered as a key-value grid
- **Plain Text**: Displayed as formatted paragraphs
- **Empty/No Data**: Shows "Pending" status

#### 2. Component Visualization

Components (if defined) are displayed as:
- Individual chips/badges in a cyan-themed container
- Count badge showing total number of components
- Flex-wrap for responsive layout on smaller screens

#### 3. Data Models Display

Data models are shown with:
- Emerald/green color scheme
- Count badge with data model count
- Clear visual distinction from components

#### 4. Project Structure Viewer

An expandable section that shows:
- Full project structure JSON (when available)
- Collapsible to save space
- Monospace font for readability

### How It Works

#### For JSON Data

If your `Design Overview` contains:

```json
{
  "architecture": "Microservices",
  "framework": ".NET 7",
  "database": "PostgreSQL"
}
```

It renders as:

```
┌─────────────────┬─────────────────┐
│ architecture    │ Microservices   │
├─────────────────┼─────────────────┤
│ framework       │ .NET 7          │
├─────────────────┼─────────────────┤
│ database        │ PostgreSQL      │
└─────────────────┴─────────────────┘
```

#### For Plain Text

If your `Design Overview` contains:

```
The system uses a microservices architecture with .NET 7 backend.
Database is PostgreSQL, and caching is handled by Redis.
```

It renders as a formatted paragraph.

### Configuration

#### Setting Up Design Data

1. Create or update `design/design.json` in your project
2. Include the fields mentioned above
3. The dashboard will automatically format it correctly

#### Example File

```json
{
  "architecture": {
    "type": "Layered Architecture",
    "layers": [
      "Presentation Layer (Console)",
      "Application Layer (Workflow Orchestrator)",
      "Domain Layer (Core Business Logic)",
      "Infrastructure Layer (LLM API, Tool Execution)"
    ],
    "patterns": [
      "Dependency Injection",
      "Strategy Pattern (for tool selection)",
      "Factory Pattern (for step processing)"
    ]
  },
  "components": [
    "StepLoader",
    "WorkflowOrchestrator",
    "LLMClient",
    "ToolExecutor",
    "ContextManager",
    "MetricsCollector"
  ],
  "data_models": [
    { "name": "Step", "fields": { "Id": "string", "FilePath": "string" } },
    { "name": "LLMRequest", "fields": { "Model": "string", "Messages": "List<ChatMessage>" } }
  ],
  "project_structure": {
    "layers": ["presentation", "business", "data"],
    "technology_stack": [".NET 7", "PostgreSQL", "Redis"]
  }
}
```

### Benefits

1. **More Readable**: Key-value format is easier to scan than raw JSON
2. **Visual Hierarchy**: Different sections have distinct visual styles
3. **Expandable Details**: Project structure doesn't clutter the view by default
4. **Responsive Design**: Works well on mobile and desktop screens
5. **User-Friendly**: Non-technical users can understand the architecture

### Technical Implementation

The enhancement is implemented in:
- File: `ClientApp/dashboard.js`
- Function: `renderArchitectureOverview(design)`

### Key Functions

1. **JSON Detection**: Tries to parse overview text as JSON
2. **Grid Rendering**: Creates 2-column grid for key-value pairs
3. **Fallback**: Falls back to plain text if not valid JSON
4. **Component Display**: Renders components as chips with count
5. **Data Models**: Shows emerald-themed section with count

### Future Enhancements

Potential future improvements:
- Add dependency visualization between components
- Show data flow diagrams
- Integrate with architecture documentation tools
- Export architecture to various formats (PlantUML, Mermaid, etc.)

---

## Data Flow

### Workflow Execution Flow

```
User Request (UI/Telegram/CLI)
        │
        ▼
┌──────────────────┐
│  WorkflowStateStore │  ← In-memory state management
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  WorkflowOrchestrator │
│  - LoadStepsAsync()   │
│  - ExecuteStepAsync() │
│  - ProcessStepAsync() │
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  LLMClient        │  ← HTTP API to LLM server
│  - GenerateAsync() │
│  - HandleToolCalls │
└────────┬─────────┘
         │
    ┌────┴────┐
    ▼         ▼
┌────────┐ ┌────────┐
│ Bash   │ │WebFetch│
│ReadFile│ │WriteFile│
└────────┘ └────────┘
    │         │
    └────┬────┘
         ▼
┌──────────────────┐
│  MetricsLogger    │  ← logs/metrics.json
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  WebSocketHandler │  ← Real-time updates to UI
└────────┬─────────┘
         │
         ▼
   Web Browser / UI
```

### Telegram Bot Flow

```
Telegram Bot (Polling)
        │
        ▼
┌──────────────────┐
│  TelegramBotService │
│  - ParseCommands()  │
│  - ValidateChatId() │
│  - SendUpdates()    │
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  WorkflowStateStore │  ← Read/modify state
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  WorkflowOrchestrator │
│  - StartWorkflow()  │
│  - ContinueStep()   │
│  - StopWorkflow()   │
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│  TelegramBotService │
│  - SendResponse()   │
│  - SendUpdates()    │
└──────────────────┘
```

---

## Design Patterns

### Dependency Injection

- Used throughout the application for loose coupling
- Services registered in `Program.cs` of ABHive.Web
- Core services: `WorkflowOrchestrator`, `LLMClient`, `MetricsLogger`, `TelegramBotService`

### Strategy Pattern

- **Tool Selection**: LLM dynamically selects tools based on step requirements
- **Tool Execution**: Each tool implements a common interface with different strategies
- **LLM Server Selection**: Supports multiple LLM server strategies

### Factory Pattern

- **Step Processing**: Factory creates step instances from `.md` files
- **Workflow Loading**: Factory loads workflow types from `workflowtypes/` directory
- **Tool Creation**: Factory creates tool instances from configuration

### State Pattern

- **WorkflowStateStore**: Manages workflow state (running, paused, completed, error)
- **Step Status**: Each step tracks its own status (pending, running, completed, failed)
- **Ticket Iteration**: Manages ticket state (open, completed, skipped)

### Observer Pattern

- **WebSocket Updates**: Real-time status updates to connected clients
- **Telegram Updates**: Automatic status notifications via Telegram bot
- **Metrics Updates**: Periodic metrics aggregation and logging

### Repository Pattern

- **ProjectWorkspaceService**: Abstracts project workspace file operations
- **DashboardTicketFileOps**: Encapsulates ticket file read/write operations
- **StepLoader**: Abstracts step file discovery and loading

---

## Extensibility

### Adding New Tools

1. Create a new class implementing the tool interface
2. Register in `ToolConfigs` in `appsettings.json`
3. Update `ToolExecutor` to handle the new tool type
4. Add tool definition to `LLMRequest.Tools`

### Adding New LLM Servers

1. Add new entry to `LlmServers` array in `appsettings.json`
2. Configure `BaseUrl`, `ApiKey`, and `Models`
3. Set `ActiveLlmServerId` and `ActiveLlmModelId` to use the new server
4. The `LLMClient` automatically supports any OpenAI-compatible API

### Adding New Workflow Types

1. Create a new directory under `workflowtypes/`
2. Add `*.md` step files (alphabetical order determines execution order)
3. Optionally add `<StepName>.json` metadata files for each step
4. The workflow will be automatically discovered by `WorkflowTypeCatalog`

### Adding New UI Components

1. Add HTML to the appropriate page (`index.html`, `dashboard.html`, `settings.html`)
2. Add CSS styles to `styles.css` or `theme.css`
3. Add JavaScript logic to the appropriate file (`app.js`, `dashboard.js`, `settings.js`)
4. Use Tailwind CSS classes for styling

---

## Technical Details

### Project Structure

```
solution/
├── ABHive.sln                          # Solution file
├── src/
│   ├── ABHive/                         # Core domain library + CLI app
│   │   ├── ABHive.csproj
│   │   ├── Application.cs              # Workflow orchestration
│   │   ├── Infrastructure.cs           # LLM client, tool executor
│   │   ├── AppSettings.cs              # Configuration model
│   │   ├── DataModels.cs               # Domain data models
│   │   ├── MetricsLogger.cs            # Metrics tracking
│   │   ├── Presentation.cs             # Console presentation
│   │   ├── StepConversationService.cs
│   │   ├── ToolCache.cs
│   │   ├── ToolCallSafety.cs
│   │   ├── WorkspaceContext.cs
│   │   └── appsettings.json
│   ├── ABHive.Web/                     # Web application
│   │   ├── ABHive.Web.csproj
│   │   ├── Program.cs                  # Entry point
│   │   ├── Controllers.cs              # API controllers
│   │   ├── WebSocketHandler.cs         # WebSocket communication
│   │   ├── TelegramBotService.cs       # Telegram bot service
│   │   ├── ProjectDashboardService.cs
│   │   ├── ProjectWorkspaceService.cs
│   │   ├── WorkflowStateStore.cs
│   │   ├── WorkflowTypeCatalog.cs
│   │   ├── TicketIterationStatusResolver.cs
│   │   ├── DashboardTicketFileOps.cs
│   │   ├── WebOutputFormatter.cs
│   │   ├── ClientApp/                  # Frontend
│   │   ├── docs/                       # Documentation
│   │   ├── workflowtypes/              # Workflow definitions
│   │   ├── projects/                   # Project workspaces
│   │   ├── appsettings.json            # User config (secrets)
│   │   ├── appsettings.defaults.json   # Default template
│   │   └── Properties/
│   ├── ABHive.Tests/                   # Unit tests
│   └── ABHive.IntegrationTests/        # Integration tests
├── docs/                               # Documentation
├── .gitignore                          # Git ignore rules
└── README.md                           # Project README
```

### Build Configuration

- **Target Framework**: .NET 7.0 (net7.0)
- **Implicit Usings**: Enabled
- **Nullable**: Enabled
- **Test Framework**: xUnit (for ABHive.Tests and ABHive.IntegrationTests)

### Key Packages

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.Extensions.Hosting | 8.0.0 | Core hosting infrastructure |
| Microsoft.Extensions.Http | 8.0.0 | HTTP client factory |
| Microsoft.AspNetCore.Mvc | 2.1.3 | MVC framework |
| Microsoft.AspNetCore.WebSockets | 2.1.1 | WebSocket support |
| xunit | 2.5.3+ | Unit testing framework |

---

*For more information, see the [User's Guide](USERS_GUIDE.md), [Project Structure](PROJECT_STRUCTURE.md), and [Workflow Authoring](WORKFLOWS.md).*
