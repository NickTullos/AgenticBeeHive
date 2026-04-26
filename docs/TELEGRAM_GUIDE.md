# Telegram Integration Guide — ABHive

This guide covers using the Telegram bot for remote workflow management in ABHive.

## Table of Contents

1. [Overview](#overview)
2. [Setup](#setup)
3. [Configuration](#configuration)
4. [Available Commands](#available-commands)
5. [Command Examples](#command-examples)
6. [Real-time Updates](#real-time-updates)
7. [Security Considerations](#security-considerations)
8. [Troubleshooting](#troubleshooting)
9. [Advanced Configuration](#advanced-configuration)
10. [Best Practices](#best-practices)

---

## Overview

The Telegram integration allows you to:

- **Monitor workflow status** from anywhere with internet access
- **Start, stop, and control workflows** via bot commands
- **Receive real-time updates** and notifications about workflow progress
- **Advance tickets** without opening the web interface
- **Get status reports** on demand

---

## Setup

### Prerequisites

- Telegram account
- Bot token from @BotFather
- Chat ID for your Telegram account

### Step 1: Creating a Bot

1. Open Telegram and search for **@BotFather**
2. Send `/newbot` command
3. Follow instructions to create your bot (choose a name and username)
4. Copy the **API token** you receive (format: `1234567890:ABCdefGHIjklMNOpqrsTUVwxyz`)

### Step 2: Getting Your Chat ID

1. Start a chat with your new bot
2. Send any message to the bot
3. Visit: `https://api.telegram.org/bot<YOUR_TOKEN>/getUpdates`
   - Replace `<YOUR_TOKEN>` with your actual bot token
4. Find your `chat.id` in the response JSON:

```json
{
  "result": [
    {
      "update_id": 123456,
      "message": {
        "message_id": 1,
        "from": {
          "id": 987654321,
          "is_bot": false,
          "first_name": "Your Name"
        },
        "chat": {
          "id": 987654321,
          "first_name": "Your Name",
          "type": "private"
        },
        "date": 1234567890,
        "text": "Hello"
      }
    }
  ]
}
```

Your chat ID is `987654321` in this example.

### Step 3: Enabling the Bot in ABHive

Add the following to `appsettings.json` in `src/ABHive.Web/`:

```json
{
  "TelegramEnabled": true,
  "TelegramBotToken": "1234567890:ABCdefGHIjklMNOpqrsTUVwxyz",
  "TelegramChatId": 987654321,
  "TelegramPollTimeoutSeconds": 20,
  "TelegramSwitchContextMessageCount": 5
}
```

> ⚠️ **Security**: Never commit `appsettings.json` if it contains your bot token or chat ID. These are secrets that should be kept private.

---

## Configuration

### Required Settings

| Setting | Type | Required | Description |
|---------|------|----------|-------------|
| `TelegramEnabled` | bool | Yes | Enable/disable Telegram integration |
| `TelegramBotToken` | string | Yes | Bot API token from @BotFather |
| `TelegramChatId` | long | Yes | Your Telegram chat ID |

### Optional Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `TelegramPollTimeoutSeconds` | int | `20` | Polling interval in seconds |
| `TelegramSwitchContextMessageCount` | int | `5` | Messages before context switch |

### Example Configuration

```json
{
  "TelegramEnabled": true,
  "TelegramBotToken": "YOUR_BOT_TOKEN_HERE",
  "TelegramChatId": 987654321,
  "TelegramPollTimeoutSeconds": 20,
  "TelegramSwitchContextMessageCount": 5
}
```

---

## Available Commands

### Basic Commands

| Command | Description |
|---------|-------------|
| `/start` | Start interaction with bot (shows welcome message) |
| `/help` | Show all available commands |

### Workflow Control

| Command | Description |
|---------|-------------|
| `/status` | Current workflow status, step number, and progress |
| `/startworkflow` | Start or resume the workflow |
| `/continue` | Continue to next step (same as clicking "Continue" in UI) |
| `/stop` | Stop current workflow execution |
| `/reset` | Reset workflow state completely |

### Ticket Management

| Command | Description |
|---------|-------------|
| `/ticket` | Advance to next ticket (only in ticket iteration mode) |

---

## Command Examples

### Check Status

**Send:**
```
/status
```

**Response:**
```
Status: Ready
Step: 3 of 10
Step name: Implement feature X
Waiting for input: no
Workflow running: false
Connected clients: 2
```

When workflow is running:
```
Status: Busy
Step: 5 of 10
Step name: Run tests
Waiting for input: yes
Workflow running: true
Connected clients: 1
```

### Start Workflow

**Send:**
```
/startworkflow
```

**Response:**
```
Workflow start/resume requested.
The workflow will begin processing the next step.
```

### Continue to Next Step

**Send:**
```
/continue
```

**Response:**
```
Workflow continue/resume requested.
The workflow will continue to the next step.
```

### Stop Workflow

**Send:**
```
/stop
```

**Response:**
```
Workflow stop requested.
The current step will complete, then the workflow will stop.
```

### Reset Workflow State

**Send:**
```
/reset
```

**Response:**
```
Workflow state has been reset.
The workflow is now in Ready state.
```

### Advance to Next Ticket

**Send (in ticket iteration mode):**
```
/ticket
```

**Response (if tickets remain):**
```
Continuing to next ticket: TICKET-123. 2 tickets remaining.
```

**Response (if no more tickets):**
```
No more tickets to process for this step.
The workflow will continue normally.
```

---

## Real-time Updates

The bot provides automatic updates when:

| Event | Update Sent |
|-------|-------------|
| **Workflow starts** | "Workflow started: processing step 1 of 10" |
| **Step begins** | "Step 3 of 10: Implement feature X — in progress" |
| **Step completes** | "Step 3 of 10: Implement feature X — completed (1.2s)" |
| **LLM response generated** | "LLM response: [summary of response]" |
| **User input requested** | "Waiting for input: please provide details for feature X" |
| **Error occurs** | "Error: [error message] — manual intervention required" |
| **Workflow completes** | "Workflow completed: 10 steps, 10 successful, 0 failed" |
| **Tool call executed** | "Tool executed: Bash — ls -la (output truncated)" |

### Update Format

Updates are sent as formatted messages:

```
🔔 ABHive Update

Status: Busy
Step: 3/10 — Implement feature X
Tool: Bash — Running `dotnet build`
Time: 1.2s elapsed
```

---

## Security Considerations

### 1. Chat ID Whitelisting

Only messages from the configured `TelegramChatId` are processed. This prevents unauthorized users from controlling your workflow.

**How it works:**
- When a message is received, the bot checks if the sender's chat ID matches `TelegramChatId`
- If it doesn't match, the message is ignored
- If it matches, the command is processed

### 2. Bot Token Privacy

**Never share your bot token publicly.** If your token is exposed:
- Someone could control your workflows
- Someone could read your workflow status
- Someone could send commands that affect your system

**If your token is compromised:**
1. Contact @BotFather
2. Send `/revoke` to generate a new token
3. Update `appsettings.json` with the new token
4. Restart the application

### 3. Rate Limiting

The bot has built-in rate limiting to prevent abuse:
- Commands are throttled to prevent rapid successive calls
- Polling interval prevents excessive API calls to Telegram

### 4. Logging

All commands are logged for audit purposes:
- Command text and timestamp
- Sender chat ID
- Action taken
- Result of the command

---

## Troubleshooting

### Bot Doesn't Respond

| Cause | Solution |
|-------|----------|
| `TelegramEnabled` is false | Set `TelegramEnabled` to `true` in `appsettings.json` |
| Bot token is incorrect | Verify token matches what @BotFather provided |
| Chat ID is incorrect | Re-check chat ID from `getUpdates` API |
| Server not running | Ensure ABHive is running and TelegramBotService started |
| Network issues | Check server can reach `api.telegram.org` |

### Commands Not Working

| Cause | Solution |
|-------|----------|
| Workflow not running | Some commands require workflow to be in a specific state |
| Wrong chat ID | Verify your chat ID matches the configured `TelegramChatId` |
| Step configuration | Some commands only work in specific step configurations |
| Rate limited | Wait a few seconds and try again |

### No Updates Received

| Cause | Solution |
|-------|----------|
| Polling disabled | Check `TelegramPollTimeoutSeconds` is set > 0 |
| Network issues | Ensure server can reach Telegram API |
| Firewall blocking | Check firewall settings for outbound HTTPS connections |
| Token expired | Verify bot token is still valid |

### Error Messages from Bot

| Error | Meaning | Solution |
|-------|---------|----------|
| "Chat ID not authorized" | Message from unauthorized chat | Send from the configured chat ID |
| "Workflow not running" | Attempted to control non-running workflow | Start workflow first with `/startworkflow` |
| "Invalid command" | Unknown command sent | Use `/help` to see available commands |
| "Rate limited" | Too many rapid commands | Wait and try again |

---

## Advanced Configuration

### Polling Interval

Adjust `TelegramPollTimeoutSeconds` in `appsettings.json`:

```json
"TelegramPollTimeoutSeconds": 30  // Default: 20 seconds
```

**Trade-offs:**
- **Lower values** (10-15s): More responsive, higher API usage
- **Higher values** (30-60s): Less responsive, lower API usage

### Context Switch Threshold

Adjust `TelegramSwitchContextMessageCount` in `appsettings.json`:

```json
"TelegramSwitchContextMessageCount": 10  // Default: 5 messages
```

This controls how many messages the bot processes before switching context. Higher values mean the bot maintains context longer for multi-step conversations.

### Multiple Chat IDs

Currently, only one chat ID is supported. If you need multiple users:

1. **Option A**: Use a Telegram group and configure the group's chat ID
2. **Option B**: Modify the source code to support multiple chat IDs

### Custom Bot Commands

The bot supports the following commands by default:

| Command | Handler |
|---------|---------|
| `/start` | `HandleStartCommand()` |
| `/help` | `HandleHelpCommand()` |
| `/status` | `HandleStatusCommand()` |
| `/startworkflow` | `HandleStartWorkflowCommand()` |
| `/continue` | `HandleContinueCommand()` |
| `/stop` | `HandleStopCommand()` |
| `/reset` | `HandleResetCommand()` |
| `/ticket` | `HandleTicketCommand()` |

To add custom commands, extend the command handler in `TelegramBotService.cs`.

---

## Best Practices

### 1. Use `/status` Frequently

Check workflow progress regularly:
```
/status
```

### 2. Send `/stop` Before Making Changes

Before modifying workflow files, stop the workflow:
```
/stop
```

### 3. Keep Track of Tickets

Use the `/ticket` command to monitor ticket progress:
```
/ticket
```

### 4. Monitor Logs for Issues

Check `logs/metrics.json` for workflow execution details:
```bash
tail -f logs/metrics.json
```

### 5. Secure Your Bot Token

- Store `appsettings.json` in a secure location
- Never commit it to version control
- Use environment variables for production deployments

### 6. Test with a Dev Bot First

Before using with your production bot:
1. Create a test bot with @BotFather
2. Test all commands
3. Verify updates are received
4. Then switch to your production bot

### 7. Document Your Commands

Create a personal cheat sheet of commands you use frequently:

```
📋 ABHive Telegram Commands

📊 Status:     /status
▶️  Start:      /startworkflow
⏸️  Continue:   /continue
⏹️  Stop:       /stop
🔄 Reset:       /reset
🎫 Ticket:      /ticket
❓ Help:        /help
```

---

## Quick Reference

| Command | Description | When to Use |
|---------|-------------|-------------|
| `/status` | Check workflow status | Any time |
| `/startworkflow` | Start/resume workflow | Before running |
| `/continue` | Advance to next step | After step completes |
| `/stop` | Stop workflow | Before making changes |
| `/reset` | Reset state | When stuck |
| `/ticket` | Next ticket | In ticket iteration mode |
| `/help` | Show commands | When unsure |

---

*For more information, see the [User's Guide](USERS_GUIDE.md) and [Telegram Integration Guide](TELEGRAM_GUIDE.md).*
