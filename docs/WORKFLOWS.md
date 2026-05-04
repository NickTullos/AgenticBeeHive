# Workflow Authoring Guide — ABHive

This guide explains how to author workflow types under `workflowtypes/`, including optional step metadata files (`<StepName>.json`) and supported `{{KEYWORDS}}` path tokens.

## Table of Contents

1. [Overview](#overview)
2. [Workflow Folder Layout](#workflow-folder-layout)
3. [Step Metadata](#step-metadata)
4. `{{KEYWORDS}}` Path Tokens
5. [Example: Ticket Iteration Step](#example-ticket-iteration-step)
6. [Example: Standard Step](#example-standard-step)
7. [Built-in Workflow Types](#built-in-workflow-types)
8. [Creating a Custom Workflow](#creating-a-custom-workflow)
9. [In-App Workflow Builder (v1.0)](#in-app-workflow-builder-v10)
10. [Best Practices](#best-practices)
11. [Troubleshooting](#troubleshooting)

---

## Overview

Workflows in ABHive are collections of Markdown step files organized in directories under `workflowtypes/`. Each workflow type represents a different development task or process.

**Key Concepts:**

- **Workflow Type**: A directory under `workflowtypes/` containing step files
- **Step**: A single `.md` file within a workflow directory
- **Step Metadata**: Optional `<StepName>.json` file controlling step behavior
- **Path Tokens**: `{{KEYWORDS}}` placeholders replaced at runtime

---

## Workflow Folder Layout

A workflow type is a directory under your configured workflow types root (default `./workflowtypes`):

```text
workflowtypes/
  my_workflow/                          # Workflow type name
    Step1.md                            # Step 1 (alphabetical order)
    Step2.md                            # Step 2
    Step3.md                            # Step 3
    Step3.json                          # Optional metadata for Step3
    Step4.md                            # Step 4
    Step4.json                          # Optional metadata for Step4
```

### Rules

1. **Step execution order** is **alphabetical** by markdown file name
2. **Step files** must be `*.md`
3. For step file `X.md`, metadata is optional and must be `X.json` in the same folder
4. Non-`.md` files in the workflow directory are ignored (except `.json` metadata files)

### Naming Convention

We recommend using descriptive names for workflow directories and step files:

```
workflowtypes/
  create_application/                   # Workflow: Create new application
    Architect-Step1.md                  # Step 1: Architect phase
    Architect-Step2.md                  # Step 2: More architect work
    Implement-Step1.md                  # Step 3: Implementation phase
    Implement-Step2.md                  # Step 4: More implementation
    Review-Step1.md                     # Step 5: Review phase
    Review-Step1.json                   # Metadata: ticket iteration mode
```

---

## Step Metadata

If present, `<StepName>.json` controls runtime behavior for that step.

### Supported Fields

```json
{
  "executionMode": "standard | ticketIteration",
  "ticketSource": "{{TICKETS_DIR}}/tickets.json",
  "completedSource": "{{TICKETS_DIR}}/completed.json",
  "maxRetriesPerTicket": 3
}
```

### Field Descriptions

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `executionMode` | string | `"standard"` | Execution mode: `standard` or `ticketIteration` |
| `ticketSource` | string | `"{{TICKETS_DIR}}/tickets.json"` | Path to tickets JSON file |
| `completedSource` | string | `"{{TICKETS_DIR}}/completed.json"` | Path to completed tickets JSON file |
| `maxRetriesPerTicket` | int | `3` | Max retries per ticket in ticket iteration mode |

### Defaults

- `executionMode`: `standard`
- `ticketSource`: `{{TICKETS_DIR}}/tickets.json`
- `completedSource`: `{{TICKETS_DIR}}/completed.json`
- `maxRetriesPerTicket`: `3`

### Behavior

**Missing metadata file**: Step runs as `standard` mode.

**Invalid metadata**: Step falls back to `standard` mode and logs a warning.

**`ticketIteration` mode behavior**:
- Runs one ticket per workflow run
- Selects first incomplete `ticket_id` from `tickets.json` not in `completed.json`
- Treats `completed.json` as the authoritative source for ticket completion status
- Retries same ticket up to `maxRetriesPerTicket` in the same run
- If tickets remain after success, workflow stays resumable at the same step (shows "Next Ticket" button)
- If no tickets remain, workflow completes normally

---

## `{{KEYWORDS}}` Path Tokens

These tokens are replaced at runtime based on the selected project workspace:

| Token | Replaced With |
|-------|---------------|
| `{{PROJECT_ROOT}}` | Selected project root directory |
| `{{GOALS_DIR}}` | `{{PROJECT_ROOT}}/goals` |
| `{{FILES_DIR}}` | `{{PROJECT_ROOT}}/files` |
| `{{SOLUTION_DIR}}` | `{{PROJECT_ROOT}}/solution` |
| `{{PLANNING_DIR}}` | `{{PROJECT_ROOT}}/planning` |
| `{{DESIGN_DIR}}` | `{{PROJECT_ROOT}}/design` |
| `{{TICKETS_DIR}}` | `{{PROJECT_ROOT}}/tickets` |
| `{{TICKET_DEFINITION}}` | Full ticket JSON schema (see below) |

### `{{TICKET_DEFINITION}}` — Ticket Schema

This token injects the complete ticket JSON schema directly into step content. It provides the LLM with the structure and field definitions for tickets, so it knows what data is available without needing to read external files.

**Replaced with:**

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

**Use case:** Include `{{TICKET_DEFINITION}}` in your step's prompt so the LLM understands the ticket structure and can properly parse ticket data.

**Example — Step file that processes tickets:**

```markdown
ROLE:
You are a Senior Software Architect.

PHASE:
TICKET EXECUTION

CONTEXT:
The following JSON schema defines the structure of tickets in this project:

{{TICKET_DEFINITION}}

TASK:
1. Read the next ticket from {{TICKETS_DIR}}/tickets.json
2. Analyze the ticket based on the schema above
3. Process the ticket according to its type and priority
4. Update {{TICKETS_DIR}}/completed.json when done

AVAILABLE FIELDS (from ticket schema):
- ticket_id: Unique identifier for the ticket
- title: Short summary of the ticket
- description: Detailed description of the work needed
- type: One of "feature", "bug", or "refactor"
- priority: One of "low", "medium", or "high"
- files_to_modify: List of files that may need changes
- acceptance_criteria: List of criteria for completion
- dependencies: List of dependent ticket_ids
- test_requirements: Object with unit_tests and integration_tests booleans
- definition_of_done: List of completion criteria
```

### Where Tokens Are Used

You can use these tokens in:

1. **Step markdown** (`*.md`) — In the step content
2. **Step metadata** (`<StepName>.json`) — For `ticketSource` and `completedSource`

### Example

```json
{
  "ticketSource": "{{TICKETS_DIR}}/tickets.json",
  "completedSource": "{{TICKETS_DIR}}/completed.json"
}
```

If the project root is `/Volumes/ExternalDrive/LLMTools/agentic-test/test5`, this resolves to:

```json
{
  "ticketSource": "/Volumes/ExternalDrive/LLMTools/agentic-test/test5/tickets/tickets.json",
  "completedSource": "/Volumes/ExternalDrive/LLMTools/agentic-test/test5/tickets/completed.json"
}
```

---

## Example: Ticket Iteration Step

### Step File: `Architect-Step5.md`

```markdown
ROLE:
You are a Senior Software Architect.

PHASE:
TICKET EXECUTION

RULES:
- Work only one ticket per run.
- Append completed ticket to {{TICKETS_DIR}}/completed.json.
- Read next ticket from {{TICKETS_DIR}}/tickets.json.
- Focus on the ticket title and acceptance criteria.
- Use tools (Bash, ReadFile, WriteFile) as needed.

INPUT:
- ticket_id: The ticket to process
- title: The ticket title
- description: The ticket description
- acceptance_criteria: List of acceptance criteria

OUTPUT:
- Implementation files modified/created
- Test files created
- Summary of changes made
```

### Metadata File: `Architect-Step5.json`

```json
{
  "executionMode": "ticketIteration",
  "ticketSource": "{{TICKETS_DIR}}/tickets.json",
  "completedSource": "{{TICKETS_DIR}}/completed.json",
  "maxRetriesPerTicket": 3
}
```

### Ticket JSON Schema

```json
[
  {
    "ticket_id": "LPM-001",
    "title": "Extend LLMRequest with generation parameters",
    "description": "Add TopP, TopK, MaxTokens, FrequencyPenalty, PresencePenalty, and StopSequences properties...",
    "type": "feature",
    "priority": "high",
    "files_to_modify": ["solution/src/ABHive/DataModels.cs"],
    "inputs": {
      "parameters": {
        "TopP": { "type": "double", "default": 1.0, "range": "0–1" }
      }
    },
    "outputs": {
      "LLMRequest_class": "Contains all 7 generation parameters"
    },
    "acceptance_criteria": [
      "LLMRequest has properties: TopP, TopK, MaxTokens, FrequencyPenalty, PresencePenalty, StopSequences",
      "Each property has [JsonPropertyName] attribute",
      "StopSequences serializes as a JSON array",
      "Default values match the plan"
    ],
    "dependencies": [],
    "test_requirements": {
      "unit_tests": true,
      "integration_tests": false
    },
    "definition_of_done": [
      "DataModels.cs compiles without errors",
      "LLMRequest serializes all 7 params to valid JSON",
      "StopSequences converts comma-separated input into string[]"
    ]
  }
]
```

### Completed Tickets JSON Schema

```json
[
  {
    "ticket_id": "LPM-001",
    "title": "Extend LLMRequest with generation parameters",
    "completed_date": "2026-04-24",
    "files_modified": [
      "solution/src/ABHive/DataModels.cs"
    ]
  }
]
```

### Skipped Tickets JSON Schema

```json
[
  {
    "ticket_id": "LPM-006",
    "skipped_date": "2026-04-24"
  }
]
```

---

## Example: Standard Step

### Step File: `Architect-Step1.md`

```markdown
ROLE:
You are a Senior Software Architect.

PHASE:
OPEN CHAT

INPUT:
- goals/goal.md: Project goals and requirements
- planning/planning.json: Planning documents
- design/design.json: Design documents

TASK:
1. Read the project goals from {{GOALS_DIR}}/goal.md
2. Read the planning documents from {{PLANNING_DIR}}/planning.json
3. Read the design documents from {{DESIGN_DIR}}/design.json
4. Provide a high-level architecture recommendation
5. Identify key components and their responsibilities
6. Suggest technology stack and patterns
```

### No Metadata File

Since `Architect-Step1.json` doesn't exist, this step runs in `standard` mode by default.

---

## Built-in Workflow Types

ABHive includes the following workflow types:

### 1. `create_application` — Create New Application

Guides you through architecting a new application with 5 steps:

| Step | File | Description |
|------|------|-------------|
| 1 | `Architect-Step1.md` | Define architecture |
| 2 | `Architect-Step2.md` | Design data models |
| 3 | `Architect-Step3.md` | Implement core logic |
| 4 | `Architect-Step4.md` | Add testing |
| 5 | `Architect-Step5.md` | Review and finalize (ticket iteration) |

### 2. `chat` — General Conversation

General-purpose conversation and guidance workflow. Step files define the conversation flow and context.

### 3. `debug` — Debug Code Issues

Helps debug code issues step by step. Uses tools like ReadFile to inspect code and Bash to run diagnostics.

### 4. `new_feature` — New Feature Implementation

Implements new features following best practices. Guides through design, implementation, and testing.

### 5. `benchmarks_game_design` — Benchmark Design

Design benchmarks for performance comparison with 4 steps:

| Step | File | Description |
|------|------|-------------|
| 1 | `Step1_sim.md` | Simple benchmark design |
| 2 | `Step2_avg.md` | Average case benchmarks |
| 3 | `Step3_hard.md` | Hard case benchmarks |
| 4 | `Step4_max.md` | Maximum complexity benchmarks |

### 6. `benchmarks_logic` — Benchmark Logic

Implement benchmark logic with 3 steps:

| Step | File | Description |
|------|------|-------------|
| 1 | `Step1_simple.md` | Simple benchmark logic |
| 2 | `Step2_avg.md` | Average case logic |
| 3 | `Step3_hard.md` | Hard case logic |

### 7. `add_epic` — Add Epic-Level Features

Add epic-level features with 2 steps for planning and implementation.

### 8. `add_testing` — Add Testing Coverage

Adds comprehensive test coverage to existing code.

### 9. `reactor_user_interface` — Refactor User Interface

Refactor user interface components.

### 10. `code_review` — Code Review

Review and debug code with structured analysis.

---

## Creating a Custom Workflow

### Step 1: Create Workflow Directory

```bash
mkdir -p workflowtypes/my_custom_workflow
```

### Step 2: Create Step Files

```bash
cat > workflowtypes/my_custom_workflow/Step1.md << 'EOF'
ROLE:
You are a Senior Software Architect.

PHASE:
ANALYSIS

TASK:
1. Read the project goals from {{GOALS_DIR}}/goal.md
2. Analyze the requirements
3. Provide a high-level plan
EOF
```

```bash
cat > workflowtypes/my_custom_workflow/Step2.md << 'EOF'
ROLE:
You are a Senior Software Developer.

PHASE:
IMPLEMENTATION

TASK:
1. Follow the plan from Step1
2. Implement the solution
3. Use Bash tool to create files and run tests
EOF
```

### Step 3 (Optional): Add Metadata

```json
{
  "executionMode": "standard"
}
```

### Step 4: Test the Workflow

1. Start ABHive
2. Select `my_custom_workflow` from the workflow dropdown
3. Click "Start Workflow"
4. Monitor the dashboard for progress

---

## In-App Workflow Builder (v1.0)

ABHive v1.0 includes a browser-based workflow editor directly in `index.html`.

### Open the Builder

1. In the main page, locate **Workflow Type (Required)**.
2. Click **Create & Edit Workflows**.
3. The **Workflow Builder** modal opens with:
   - Workflow list pane
   - Step list pane
   - Markdown + metadata editors

### Create a New Workflow Type

1. In the **Workflow Types** panel, click **Create**.
2. In the popup modal, enter a workflow id (letters, numbers, `-`, `_` only).
3. Click **Create**.

Result:
- A new folder is created under `workflowtypes/<workflowId>/`
- `Step1.md` is scaffolded

### Add Steps to New or Existing Workflows

1. Select a workflow in the left pane.
2. In the **Steps** panel, click **Create**.
3. Enter a step file name when prompted (the suggested `Step{N}.md` is prefilled).
4. Confirm the prompt to create the step.

Notes:
- Step file names must end in `.md`.
- Step execution order stays alphabetical by filename.

### Remove Workflow Types or Steps

- **Delete Workflow** removes the selected workflow directory and all step files.
- **Delete Step** removes the selected `.md` step and its matching `.json` metadata file (if present).
- Both delete actions require a confirmation dialog before execution.

### Edit and Save Step Content

1. Select a step from the steps pane.
2. Edit **Step Markdown (.md)**.
3. Optional: edit **Step Metadata (.json)**.
4. Click **Save Step**.

Save rules:
- Markdown is required.
- Metadata is optional.
- Metadata must be valid JSON when present.
- Clearing metadata removes the `.json` file for that step.
- Unsaved changes trigger a confirmation when switching workflow/step or closing the modal.

### Metadata Examples

`standard`:

```json
{
  "executionMode": "standard"
}
```

`ticketIteration`:

```json
{
  "executionMode": "ticketIteration",
  "ticketSource": "{{TICKETS_DIR}}/tickets.json",
  "completedSource": "{{TICKETS_DIR}}/completed.json",
  "maxRetriesPerTicket": 3
}
```

---

## Best Practices

### Workflow Design

1. **Keep steps focused**: Each step should have a clear, single purpose
2. **Use descriptive names**: Name steps to indicate their phase (Architect-, Implement-, Review-, etc.)
3. **Include context**: Reference relevant files using `{{KEYWORDS}}` tokens
4. **Define output**: Clearly specify what the LLM should produce
5. **Specify tools**: Indicate which tools the LLM should use (Bash, ReadFile, WriteFile, WebFetch)

### Step File Structure

```markdown
ROLE:
[Describe the AI's role/persona]

PHASE:
[Current phase of the workflow]

INPUT:
- [List input files/contexts with {{KEYWORDS}} tokens]

TASK:
1. [Task 1]
2. [Task 2]
3. [Task 3]

OUTPUT:
- [Expected output format]

TOOLS:
- [Recommended tools: Bash, ReadFile, WriteFile, WebFetch]
```

### Metadata Best Practices

1. **Use ticket iteration** for steps that process multiple items
2. **Set `maxRetriesPerTicket`** based on your needs (default: 3)
3. **Use path tokens** in metadata for flexible path references
4. **Document metadata** in comments or separate documentation

### Naming Conventions

| Convention | Example |
|------------|---------|
| Workflow directories | `snake_case` or `camelCase` |
| Step files | `Phase-Number.md` or `Phase-Description.md` |
| Metadata files | `StepName.json` (same name as .md) |
| Path tokens | `{{UPPERCASE_WITH_UNDERSCORES}}` |

---

## Troubleshooting

### Workflow Not Appearing

| Cause | Solution |
|-------|----------|
| Workflow directory doesn't exist | Create directory under `workflowtypes/` |
| No `.md` files in directory | Add at least one `.md` step file |
| Wrong workflow types directory | Check `WorkflowTypesDirectory` in `appsettings.json` |
| Permission issues | Ensure directory is readable |

### Step Not Executing

| Cause | Solution |
|-------|----------|
| Invalid JSON in metadata | Fix syntax errors in `<StepName>.json` |
| Missing step file | Ensure `*.md` files exist in workflow directory |
| Path token resolution failed | Check `ProjectRootDirectory` in `appsettings.json` |
| File not found | Verify referenced files exist at resolved paths |

### Ticket Iteration Not Working

| Cause | Solution |
|-------|----------|
| `executionMode` not set to `ticketIteration` | Add `"executionMode": "ticketIteration"` to metadata |
| No tickets in `tickets.json` | Add tickets to the tickets file |
| All tickets already completed | Complete.json marks tickets as done; remove from completed.json |
| Invalid ticket JSON | Verify ticket JSON matches the schema |

### Path Tokens Not Resolving

| Cause | Solution |
|-------|----------|
| `ProjectRootDirectory` not set | Set `ProjectRootDirectory` in `appsettings.json` |
| Wrong token name | Use correct `{{KEYWORDS}}` token names |
| Directory doesn't exist | Ensure project workspace directory exists |

---

## Related Documentation

- **[User's Guide](USERS_GUIDE.md)** — Comprehensive guide for end users
- **[Telegram Guide](TELEGRAM_GUIDE.md)** — Telegram bot commands and setup
- **[Architecture Overview](ARCHITECTURE_OVERVIEW.md)** — System architecture details
- **[Project Structure](PROJECT_STRUCTURE.md)** — Directory layout and organization
- **[README.md](README.md)** — Project overview and index

---

*For a complete overview of the application, start with the [User's Guide](USERS_GUIDE.md).*
