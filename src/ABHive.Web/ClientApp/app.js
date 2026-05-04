const FAVICON_READY_SVG = `data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='none' stroke='%233b82f6' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'%3E%3Cline x1='12' y1='2' x2='12' y2='5'/%3E%3Ccircle cx='12' cy='2' r='1.1' fill='%233b82f6' stroke='none'/%3E%3Crect x='5' y='6' width='14' height='11' rx='3'/%3E%3Ccircle cx='9.5' cy='11.2' r='1.1' fill='%233b82f6' stroke='none'/%3E%3Ccircle cx='14.5' cy='11.2' r='1.1' fill='%233b82f6' stroke='none'/%3E%3Cline x1='9' y1='14.2' x2='15' y2='14.2'/%3E%3Cline x1='3.5' y1='10.5' x2='5' y2='10.5'/%3E%3Cline x1='19' y1='10.5' x2='20.5' y2='10.5'/%3E%3C/svg%3E`;
const FAVICON_BUSY_SVG = `data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='none' stroke='%23ef4444' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'%3E%3Cline x1='12' y1='2' x2='12' y2='5'/%3E%3Ccircle cx='12' cy='2' r='1.1' fill='%23ef4444' stroke='none'/%3E%3Crect x='5' y='6' width='14' height='11' rx='3'/%3E%3Ccircle cx='9.5' cy='11.2' r='1.1' fill='%23ef4444' stroke='none'/%3E%3Ccircle cx='14.5' cy='11.2' r='1.1' fill='%23ef4444' stroke='none'/%3E%3Cline x1='9' y1='14.2' x2='15' y2='14.2'/%3E%3Cline x1='3.5' y1='10.5' x2='5' y2='10.5'/%3E%3Cline x1='19' y1='10.5' x2='20.5' y2='10.5'/%3E%3C/svg%3E`;

// Agentic LLM Web Client
class WebSocketClient {
    constructor(url) {
        this.socket = null;
        this.url = url;
        this.queue = [];
        this.isConnected = false;
    }

    connect(callback) {
        try {
            console.log(`[WS] Connecting to ${this.url}`);
            this.socket = new WebSocket(this.url);

            this.socket.onopen = () => {
                console.log("[WS] Connected");
                this.isConnected = true;
                state.wsDisconnected = false;
                closeLlmSetupModal();
                closeWebSocketReconnectModal();

                while (this.queue.length > 0) {
                    const msg = this.queue.shift();
                    this.send(msg.type, msg.payload);
                }

                if (callback) {
                    callback(true);
                }

                updateBusyState(state.busy);
            };

            this.socket.onclose = (event) => {
                console.log(`[WS] Disconnected: code=${event.code}, reason="${event.reason}"`);
                this.isConnected = false;
                state.wsDisconnected = true;
                updateStatus("Disconnected", "red");
                openWebSocketReconnectModal(event.reason || "WebSocket connection closed.");
                updateBusyState(state.busy);
            };

            this.socket.onerror = (error) => {
                console.error("[WS] Error:", error.message || "Unknown error");
            };

            this.socket.onmessage = (event) => {
                const data = JSON.parse(event.data);
                handleMessage(data);
            };
        } catch (error) {
            console.error("[WS] Connection failed:", error);
            state.wsDisconnected = true;
            updateBusyState(state.busy);
            if (callback) {
                callback(false, error.message);
            }
        }
    }

    send(type, payload = {}) {
        if (!this.isConnected) {
            this.queue.push({ type, payload });
            return;
        }

        const message = { type, ...payload };
        this.socket.send(JSON.stringify(message));
    }

    close() {
        if (this.socket) {
            this.socket.close();
        }
    }
}

class APIClient {
    constructor(baseUrl = "") {
        this.baseUrl = baseUrl;
    }

    async get(endpoint) {
        return this.request(`${this.baseUrl}${endpoint}`);
    }

    async post(endpoint, data) {
        return this.request(`${this.baseUrl}${endpoint}`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(data)
        });
    }

    async delete(endpoint) {
        return this.request(`${this.baseUrl}${endpoint}`, {
            method: "DELETE"
        });
    }

    async getStatus() {
        return this.get("/api/status");
    }

    async getWorkflowState() {
        return this.get("/api/workflow/state");
    }

    async getCurrentViewContent() {
        return this.get("/api/workflow/view/current");
    }

    async getWorkflowTypes() {
        return this.get("/api/workflowtypes");
    }

    async createWorkflowType(workflowTypeId) {
        return this.post("/api/workflowtypes", { workflowTypeId });
    }

    async getWorkflowTypeSteps(workflowTypeId) {
        const encodedWorkflowTypeId = encodeURIComponent(workflowTypeId || "");
        return this.get(`/api/workflowtypes/${encodedWorkflowTypeId}/steps`);
    }

    async addWorkflowTypeStep(workflowTypeId, stepFileName) {
        const encodedWorkflowTypeId = encodeURIComponent(workflowTypeId || "");
        return this.post(`/api/workflowtypes/${encodedWorkflowTypeId}/steps`, { stepFileName });
    }

    async getWorkflowTypeStep(workflowTypeId, stepFileName) {
        const encodedWorkflowTypeId = encodeURIComponent(workflowTypeId || "");
        const encodedStepFileName = encodeURIComponent(stepFileName || "");
        return this.get(`/api/workflowtypes/${encodedWorkflowTypeId}/steps/${encodedStepFileName}`);
    }

    async saveWorkflowTypeStep(workflowTypeId, stepFileName, markdownContent, metadataJsonContent) {
        const encodedWorkflowTypeId = encodeURIComponent(workflowTypeId || "");
        const encodedStepFileName = encodeURIComponent(stepFileName || "");
        return this.request(`${this.baseUrl}/api/workflowtypes/${encodedWorkflowTypeId}/steps/${encodedStepFileName}`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ markdownContent, metadataJsonContent })
        });
    }

    async deleteWorkflowType(workflowTypeId) {
        const encodedWorkflowTypeId = encodeURIComponent(workflowTypeId || "");
        return this.delete(`/api/workflowtypes/${encodedWorkflowTypeId}`);
    }

    async deleteWorkflowTypeStep(workflowTypeId, stepFileName) {
        const encodedWorkflowTypeId = encodeURIComponent(workflowTypeId || "");
        const encodedStepFileName = encodeURIComponent(stepFileName || "");
        return this.delete(`/api/workflowtypes/${encodedWorkflowTypeId}/steps/${encodedStepFileName}`);
    }

    async getProjects() {
        return this.get("/api/projects");
    }

    async getLlmOptions() {
        return this.get("/api/llm/options");
    }

    async getLlmHealth() {
        return this.get("/api/llm/health");
    }

    async checkVersion() {
        return this.get("/api/version/check");
    }

    async selectLlm(serverId, modelId) {
        return this.post("/api/llm/select", { serverId, modelId });
    }

    async createProject(projectName) {
        return this.post("/api/projects", { projectName });
    }

    async getSchedules() {
        return this.get("/api/schedules");
    }

    async getSchedule(scheduleName) {
        return this.get(`/api/schedules/${encodeURIComponent(scheduleName || "")}`);
    }

    async createSchedule(payload) {
        return this.post("/api/schedules", payload);
    }

    async updateSchedule(scheduleName, payload) {
        return this.request(`${this.baseUrl}/api/schedules/${encodeURIComponent(scheduleName || "")}`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        });
    }

    async startSchedule(scheduleName, confirmClearContext = false) {
        return this.post(`/api/schedules/${encodeURIComponent(scheduleName || "")}/start`, {
            confirmClearContext: Boolean(confirmClearContext)
        });
    }

    async stopSchedule() {
        return this.post("/api/schedules/stop", {});
    }

    async stopScheduleTask() {
        return this.post("/api/schedules/stop-task", {});
    }

    async selectProject(projectName) {
        return this.post("/api/workflow/project/select", { projectName });
    }

    async getProjectSwitchOptions() {
        return this.get("/api/workflow/project/switch-options");
    }

    async sendMessage(message) {
        return this.post("/api/messages", { message });
    }

    async uploadFiles(files) {
        const formData = new FormData();
        Array.from(files).forEach((file) => {
            formData.append("files", file, file.name);
        });

        return this.request(`${this.baseUrl}/api/files/upload`, {
            method: "POST",
            body: formData
        });
    }

    async continueWorkflow() {
        return this.post("/api/workflow/continue", {});
    }

    async getSkipOptions() {
        return this.get("/api/workflow/skip/options");
    }

    async getStepContent(stepNumber) {
        return this.get(`/api/workflow/steps/${stepNumber}/content`);
    }

    async skipToStep(stepNumber) {
        return this.post("/api/workflow/skip/step", { stepNumber });
    }

    async skipTicket(stepNumber, ticketId) {
        return this.post("/api/workflow/skip/ticket", { stepNumber, ticketId });
    }

    async resumeSkippedTicket(stepNumber, ticketId) {
        return this.post("/api/workflow/skip/ticket/resume", { stepNumber, ticketId });
    }

    async reopenCompletedTicket(stepNumber, ticketId) {
        return this.post("/api/workflow/skip/ticket/reopen", { stepNumber, ticketId });
    }

    async startSpecificTicket(stepNumber, ticketId) {
        return this.post("/api/workflow/skip/ticket/start", { stepNumber, ticketId });
    }

    async startWorkflow(workflowTypeId) {
        return this.post("/api/workflow/start", { workflowTypeId });
    }

    async resetWorkflow() {
        return this.post("/api/workflow/reset", {});
    }

    async stop() {
        return this.post("/api/stop", {});
    }

    async getMetrics() {
        return this.get("/api/metrics");
    }

    async request(url, options) {
        try {
            const response = await fetch(url, options);
            const contentType = response.headers.get("content-type") || "";
            const payload = contentType.includes("application/json")
                ? await response.json()
                : await response.text();

            if (!response.ok) {
                const message = typeof payload === "object" && payload !== null
                    ? payload.error?.message || payload.message || `HTTP ${response.status}`
                    : payload || `HTTP ${response.status}`;
                const error = new Error(message);
                error.status = response.status;
                error.details = payload;
                throw error;
            }

            return payload;
        } catch (error) {
            console.error("[API] Request error:", error);
            throw error;
        }
    }
}

let wsClient;
let apiClient;

const STATUS_COLORS = {
    green: "#22c55e",
    red: "#ef4444",
    yellow: "#eab308",
    cyan: "#06b6d4",
    orange: "#f97316",
    gray: "#6b7280"
};

const TERMINAL_COLORS = {
    green: "#4ade80",
    red: "#f87171",
    yellow: "#facc15",
    blue: "#60a5fa",
    cyan: "#22d3ee",
    orange: "#fb923c",
    gray: "#9ca3af"
};

const CHAT_THEME = {
    user: {
        background: "#dbeafe",
        label: "#2563eb",
        align: "flex-end"
    },
    telegram: {
        background: "#dcfce7",
        label: "#15803d",
        align: "flex-end"
    },
    tool: {
        background: "#eef2ff",
        label: "#4338ca",
        border: "#c7d2fe",
        align: "flex-start"
    },
    agent: {
        background: "#f3f4f6",
        label: "#4b5563",
        align: "flex-start"
    }
};

const ui = {
    status: document.getElementById("status-indicator"),
    stepPanel: document.getElementById("step-panel"),
    updateAvailableBadge: document.getElementById("update-available-badge"),
    activeModeBadge: document.getElementById("active-mode-badge"),
    progressBar: document.getElementById("progress-bar"),
    stepCounter: document.getElementById("step-counter"),
    stepName: document.getElementById("step-name"),
    viewCurrentStepBtn: document.getElementById("view-current-step-btn"),
    viewCurrentTicketBtn: document.getElementById("view-current-ticket-btn"),
    busyIndicator: document.getElementById("busy-indicator"),
    stopBtn: document.getElementById("stop-btn"),
    stopScheduleTaskBtn: document.getElementById("stop-schedule-task-btn"),
    resetBtn: document.getElementById("reset-btn"),
    newOpenBtn: document.getElementById("new-open-btn"),
    newProjectBtn: document.getElementById("new-project-btn"),
    scheduleBtn: document.getElementById("schedule-btn"),
    switchProjectBtn: document.getElementById("switch-project-btn"),
    startNewModal: document.getElementById("start-new-modal"),
    startNewCloseBtn: document.getElementById("start-new-close-btn"),
    newWorkflowModal: document.getElementById("new-workflow-modal"),
    newWorkflowCloseBtn: document.getElementById("new-workflow-close-btn"),
    switchProjectModal: document.getElementById("switch-project-modal"),
    switchProjectCloseBtn: document.getElementById("switch-project-close-btn"),
    switchProjectList: document.getElementById("switch-project-list"),
    workflowConfigPanel: document.getElementById("workflow-config-panel"),
    workflowTypeSelect: document.getElementById("workflow-type-select"),
    workflowTypeHelp: document.getElementById("workflow-type-help"),
    projectSelect: document.getElementById("project-select"),
    createProjectOpenBtn: document.getElementById("create-project-open-btn"),
    projectHelp: document.getElementById("project-help"),
    projectNameInput: document.getElementById("project-name-input"),
    createProjectBtn: document.getElementById("create-project-btn"),
    createProjectModal: document.getElementById("create-project-modal"),
    createProjectCloseBtn: document.getElementById("create-project-close-btn"),
    createProjectCancelBtn: document.getElementById("create-project-cancel-btn"),
    scheduleModal: document.getElementById("schedule-modal"),
    scheduleCloseBtn: document.getElementById("schedule-close-btn"),
    scheduleRefreshBtn: document.getElementById("schedule-refresh-btn"),
    scheduleList: document.getElementById("schedule-list"),
    scheduleStatus: document.getElementById("schedule-status"),
    scheduleStopBtn: document.getElementById("schedule-stop-btn"),
    scheduleOpenEditorBtn: document.getElementById("schedule-open-editor-btn"),
    scheduleEditorModal: document.getElementById("schedule-editor-modal"),
    scheduleEditorCloseBtn: document.getElementById("schedule-editor-close-btn"),
    scheduleEditorCancelBtn: document.getElementById("schedule-editor-cancel-btn"),
    scheduleEditorSaveBtn: document.getElementById("schedule-editor-save-btn"),
    scheduleEditorExistingSelect: document.getElementById("schedule-editor-existing-select"),
    scheduleEditorNameInput: document.getElementById("schedule-editor-name-input"),
    scheduleEditorWorkflowSelect: document.getElementById("schedule-editor-workflow-select"),
    scheduleEditorStepsGroup: document.getElementById("schedule-editor-steps-group"),
    scheduleEditorStepsList: document.getElementById("schedule-editor-steps-list"),
    scheduleEditorTriggerType: document.getElementById("schedule-editor-trigger-type"),
    scheduleEditorScheduleType: document.getElementById("schedule-editor-schedule-type"),
    scheduleEditorSpecificTimeGroup: document.getElementById("schedule-editor-specific-time-group"),
    scheduleEditorSpecificTime: document.getElementById("schedule-editor-specific-time"),
    scheduleEditorFrequencyGroup: document.getElementById("schedule-editor-frequency-group"),
    scheduleEditorIntervalHours: document.getElementById("schedule-editor-interval-hours"),
    scheduleEditorIntervalMinutes: document.getElementById("schedule-editor-interval-minutes"),
    scheduleEditorIntervalSeconds: document.getElementById("schedule-editor-interval-seconds"),
    scheduleEditorRegularGroup: document.getElementById("schedule-editor-regular-group"),
    scheduleEditorRegularServer: document.getElementById("schedule-editor-regular-server"),
    scheduleEditorRegularModel: document.getElementById("schedule-editor-regular-model"),
    scheduleEditorBenchmarkGroup: document.getElementById("schedule-editor-benchmark-group"),
    scheduleEditorBenchmarkList: document.getElementById("schedule-editor-benchmark-list"),
    scheduleEditorStatus: document.getElementById("schedule-editor-status"),
    manageWorkflowTypesBtn: document.getElementById("manage-workflow-types-btn"),
    workflowEditorModal: document.getElementById("workflow-editor-modal"),
    workflowEditorCloseBtn: document.getElementById("workflow-editor-close-btn"),
    workflowEditorOpenCreateBtn: document.getElementById("workflow-editor-open-create-btn"),
    workflowEditorCreateModal: document.getElementById("workflow-editor-create-modal"),
    workflowEditorCreateCloseBtn: document.getElementById("workflow-editor-create-close-btn"),
    workflowEditorCreateCancelBtn: document.getElementById("workflow-editor-create-cancel-btn"),
    workflowEditorCreateSubmitBtn: document.getElementById("workflow-editor-create-submit-btn"),
    workflowEditorCreateNameInput: document.getElementById("workflow-editor-create-name-input"),
    workflowEditorWorkflowsList: document.getElementById("workflow-editor-workflows-list"),
    workflowEditorStepsList: document.getElementById("workflow-editor-steps-list"),
    workflowEditorSelectedWorkflow: document.getElementById("workflow-editor-selected-workflow"),
    workflowEditorSelectedStep: document.getElementById("workflow-editor-selected-step"),
    workflowEditorAddStepBtn: document.getElementById("workflow-editor-add-step-btn"),
    workflowEditorEditorsGrid: document.getElementById("workflow-editor-editors-grid"),
    workflowEditorCreateIterationBtn: document.getElementById("workflow-editor-create-iteration-btn"),
    workflowEditorMetadataPanel: document.getElementById("workflow-editor-metadata-panel"),
    workflowEditorMarkdown: document.getElementById("workflow-editor-markdown"),
    workflowEditorMetadata: document.getElementById("workflow-editor-metadata"),
    workflowEditorStatus: document.getElementById("workflow-editor-status"),
    workflowEditorSaveBtn: document.getElementById("workflow-editor-save-btn"),
    chatContainer: document.getElementById("chat-container"),
    fileUploadInput: document.getElementById("file-upload-input"),
    uploadBtn: document.getElementById("upload-btn"),
    selectedFiles: document.getElementById("selected-files"),
    userInput: document.getElementById("user-input"),
    continueBtn: document.getElementById("continue-btn"),
    skipBtn: document.getElementById("skip-btn"),
    sendBtn: document.getElementById("send-btn"),
    terminal: document.getElementById("terminal"),
    llmServerSelect: document.getElementById("llm-server-select"),
    llmModelInput: document.getElementById("llm-model-input"),
    llmModelDropdown: document.getElementById("llm-model-dropdown"),
    startBtn: document.getElementById("start-btn"),
    skipModal: document.getElementById("skip-modal"),
    skipCloseBtn: document.getElementById("skip-close-btn"),
    skipModalSubtitle: document.getElementById("skip-modal-subtitle"),
    skipModalStatus: document.getElementById("skip-modal-status"),
    skipStepsList: document.getElementById("skip-steps-list"),
    llmSetupModal: document.getElementById("llm-setup-modal"),
    llmSetupCloseBtn: document.getElementById("llm-setup-close-btn"),
    llmSetupTitle: document.getElementById("llm-setup-title"),
    llmSetupSubtitle: document.getElementById("llm-setup-subtitle"),
    llmSetupDetails: document.getElementById("llm-setup-details"),
    llmSetupSettingsLink: document.getElementById("llm-setup-settings-link"),
    llmSetupRetryBtn: document.getElementById("llm-setup-retry-btn"),
    llmSetupVideoLink: document.getElementById("llm-setup-video-link"),
    wsReconnectModal: document.getElementById("ws-reconnect-modal"),
    wsReconnectCloseBtn: document.getElementById("ws-reconnect-close-btn"),
    wsReconnectRetryBtn: document.getElementById("ws-reconnect-retry-btn"),
    wsReconnectDetails: document.getElementById("ws-reconnect-details"),
    contentViewModal: document.getElementById("content-view-modal"),
    contentViewTitle: document.getElementById("content-view-title"),
    contentViewSubtitle: document.getElementById("content-view-subtitle"),
    contentViewBody: document.getElementById("content-view-body"),
    contentViewCloseBtn: document.getElementById("content-view-close-btn"),
    metrics: {
        total: document.getElementById("total-steps"),
        success: document.getElementById("successful-steps"),
        failed: document.getElementById("failed-steps")
    }
};

const state = {
    workflowRunning: false,
    busy: false,
    awaitingUserInput: false,
    canResume: false,
    nextStepToRun: 0,
    currentStep: 0,
    totalSteps: 0,
    currentStepName: "Starting...",
    statusText: "Ready",
    statusColor: "green",
    workflowTypes: [],
    selectedWorkflowTypeId: "",
    selectedWorkflowTypeName: "",
    resumeWorkflowTypeId: "",
    projects: [],
    selectedProjectName: "",
    selectedProjectDirectory: "",
    targetProjectName: "",
    showProjectConfigPanel: false,
    newWorkflowIntentActive: false,
    llmServers: [],
    activeLlmServerId: "",
    activeLlmModelId: "",
    currentLlmModelName: "",
    currentLlmServerName: "",
    activeMode: "workflow",
    scheduleState: null,
    schedules: [],
    scheduleModalOpen: false,
    scheduleEditorOpen: false,
    scheduleEditorLoading: false,
    scheduleEditorCurrentName: "",
    scheduleEditorLockedName: "",
    scheduleEditorWorkflowSteps: [],
    scheduleEditorSelectedStepFileNames: [],
    scheduleEditorBenchmarkSelectionKeys: [],
    llmHealthy: true,
    llmHealthChecked: false,
    llmHealthDetail: "",
    llmHealthBaseUrl: "",
    wsDisconnected: false,
    versionCheckStarted: false,
    isCurrentStepTicketIteration: false,
    ticketHeaderStatus: null,
    ticketProgress: null,
    skipOptions: null,
    workflowEditorOpen: false,
    workflowEditorLoading: false,
    workflowEditorDeletingWorkflowId: "",
    workflowEditorDeletingStepFileName: "",
    workflowEditorDeleteWorkflowVisibleId: "",
    workflowEditorDeleteStepVisibleFileName: "",
    workflowEditorSelectedWorkflowId: "",
    workflowEditorSelectedWorkflowName: "",
    workflowEditorSteps: [],
    workflowEditorSelectedStepFileName: "",
    workflowEditorMarkdownDraft: "",
    workflowEditorMetadataDraft: "",
    workflowEditorHasTicketIteration: false,
    workflowEditorDirty: false
};

function hideUpdateBadge() {
    if (!ui.updateAvailableBadge) {
        return;
    }

    ui.updateAvailableBadge.classList.add("hidden");
    ui.updateAvailableBadge.removeAttribute("href");
}

function showUpdateBadge(releaseNotesUrl) {
    if (!ui.updateAvailableBadge || !releaseNotesUrl) {
        return;
    }

    ui.updateAvailableBadge.href = releaseNotesUrl;
    ui.updateAvailableBadge.classList.remove("hidden");
}

async function checkForUpdatesOnStartup() {
    if (!apiClient || state.versionCheckStarted) {
        return;
    }

    state.versionCheckStarted = true;
    hideUpdateBadge();

    try {
        const result = await apiClient.checkVersion();
        if (result?.updateAvailable && result?.releaseNotesUrl) {
            showUpdateBadge(result.releaseNotesUrl);
        }

        if (result?.error && typeof logTerminal === "function") {
            logTerminal(`[VERSION] Update check failed: ${result.error}`, "orange");
        }
    } catch {
        // Keep startup resilient and silent for transport-level failures.
    }
}

function openLlmSetupModal(detailText) {
    if (!ui.llmSetupModal) {
        return;
    }

    if (ui.llmSetupDetails) {
        ui.llmSetupDetails.textContent = detailText || "LLM server is not reachable.";
    }

    if (ui.llmSetupVideoLink) {
        const videoUrl = (window.AGENTIC_LLM_SETUP_VIDEO_URL || "").trim();
        if (videoUrl) {
            ui.llmSetupVideoLink.href = videoUrl;
            ui.llmSetupVideoLink.classList.remove("hidden");
        } else {
            ui.llmSetupVideoLink.classList.add("hidden");
        }
    }

    ui.llmSetupModal.classList.remove("hidden");
    ui.llmSetupModal.classList.add("flex");
}

function openWebSocketReconnectModal(detailText) {
    if (!ui.wsReconnectModal) {
        return;
    }

    if (ui.wsReconnectDetails) {
        ui.wsReconnectDetails.textContent = detailText || "WebSocket connection is disconnected.";
    }

    ui.wsReconnectModal.classList.remove("hidden");
    ui.wsReconnectModal.classList.add("flex");
}

function closeWebSocketReconnectModal() {
    if (!ui.wsReconnectModal) {
        return;
    }

    ui.wsReconnectModal.classList.add("hidden");
    ui.wsReconnectModal.classList.remove("flex");
}

function closeLlmSetupModal() {
    if (!ui.llmSetupModal) {
        return;
    }

    ui.llmSetupModal.classList.add("hidden");
    ui.llmSetupModal.classList.remove("flex");
}

async function refreshLlmHealth(showModalOnFailure = true) {
    if (!apiClient) {
        return;
    }

    // Guard against redundant checks while the UI is hydrating.
    if (state._llmHealthInFlight) {
        return;
    }
    state._llmHealthInFlight = true;

    try {
        const health = await apiClient.getLlmHealth();
        const ok = Boolean(health?.ok);
        const baseUrl = (health?.baseUrl || "").trim();
        const message = (health?.message || "").trim();

        state.llmHealthy = ok;
        state.llmHealthChecked = true;
        state.llmHealthBaseUrl = baseUrl;
        state.llmHealthDetail = message || (ok ? "LLM server reachable." : "LLM server is not reachable.");

        if (!ok && showModalOnFailure) {
            const detail = baseUrl
                ? `${state.llmHealthDetail} (${baseUrl})`
                : state.llmHealthDetail;
            if (typeof logTerminal === "function") {
                logTerminal(`[LLM] Setup required: ${detail}`, "orange");
            }
            openLlmSetupModal(detail);
        } else if (ok) {
            if (typeof logTerminal === "function" && !state.llmHealthy) {
                logTerminal(`[LLM] Connected: ${baseUrl || "LLM server"}`, "green");
            }
            closeLlmSetupModal();
        }
    } catch (error) {
        // If the API is reachable but health check fails unexpectedly, keep it simple and actionable.
        state.llmHealthy = false;
        state.llmHealthChecked = true;
        state.llmHealthDetail = error?.message ? `LLM health check failed: ${error.message}` : "LLM health check failed.";
        if (typeof logTerminal === "function") {
            logTerminal(`[LLM] Setup required: ${state.llmHealthDetail}`, "orange");
        }
        if (showModalOnFailure) {
            openLlmSetupModal(state.llmHealthDetail);
        }
    } finally {
        state._llmHealthInFlight = false;
        updateBusyState(state.busy);
    }
}

function openSkipModal() {
    if (!ui.skipModal) {
        return;
    }

    ui.skipModal.classList.remove("hidden");
    ui.skipModal.classList.add("flex");

    if (ui.skipModalStatus) {
        ui.skipModalStatus.textContent = "Loading...";
        ui.skipModalStatus.className = "text-sm text-slate-600";
    }

    if (ui.skipStepsList) {
        ui.skipStepsList.innerHTML = "";
    }

    if (ui.skipModalSubtitle) {
        const stepLabel = state.currentStep > 0 ? `Step ${state.currentStep}` : "Step (unknown)";
        const ticketLabel = state.isCurrentStepTicketIteration
            ? (state.ticketHeaderStatus?.currentTicketId ? ` • Ticket ${state.ticketHeaderStatus.currentTicketId}` : " • Ticket (unknown)")
            : "";
        ui.skipModalSubtitle.textContent = `Currently at ${stepLabel}${ticketLabel}`;
    }

    void loadSkipOptions();
}

function closeSkipModal() {
    if (!ui.skipModal) {
        return;
    }

    ui.skipModal.classList.add("hidden");
    ui.skipModal.classList.remove("flex");
}

function openCreateProjectModal() {
    if (!ui.createProjectModal) {
        return;
    }

    ui.createProjectModal.classList.remove("hidden");
    ui.createProjectModal.classList.add("flex");
    if (ui.projectNameInput) {
        ui.projectNameInput.value = "";
        ui.projectNameInput.focus();
    }
}

function closeCreateProjectModal() {
    if (!ui.createProjectModal) {
        return;
    }

    ui.createProjectModal.classList.add("hidden");
    ui.createProjectModal.classList.remove("flex");
    if (ui.projectNameInput) {
        ui.projectNameInput.value = "";
    }
}

function updateActiveModeBadge() {
    if (!ui.activeModeBadge) {
        return;
    }

    const isScheduleMode = state.activeMode === "schedule" || Boolean(state.scheduleState?.isActive);
    ui.activeModeBadge.textContent = isScheduleMode ? "Schedule Mode" : "Workflow Mode";
    ui.activeModeBadge.className = isScheduleMode
        ? "rounded bg-purple-700 px-2 py-0.5 text-xs font-semibold text-purple-100"
        : "rounded bg-slate-700 px-2 py-0.5 text-xs font-semibold text-cyan-200";
}

function openScheduleModal() {
    if (!ui.scheduleModal) {
        return;
    }

    state.scheduleModalOpen = true;
    ui.scheduleModal.classList.remove("hidden");
    ui.scheduleModal.classList.add("flex");
    void loadSchedules();
}

function closeScheduleModal() {
    if (!ui.scheduleModal) {
        return;
    }

    state.scheduleModalOpen = false;
    ui.scheduleModal.classList.add("hidden");
    ui.scheduleModal.classList.remove("flex");
}

function openScheduleEditorModal() {
    if (!ui.scheduleEditorModal) {
        return;
    }

    state.scheduleEditorOpen = true;
    state.scheduleEditorLockedName = "";
    state.scheduleEditorWorkflowSteps = [];
    state.scheduleEditorSelectedStepFileNames = [];
    state.scheduleEditorBenchmarkSelectionKeys = [];
    ui.scheduleEditorModal.classList.remove("hidden");
    ui.scheduleEditorModal.classList.add("flex");
    renderScheduleEditorSelectors();
    if (ui.scheduleEditorExistingSelect) {
        ui.scheduleEditorExistingSelect.value = "";
    }
    if (ui.scheduleEditorNameInput) {
        ui.scheduleEditorNameInput.value = "";
        ui.scheduleEditorNameInput.focus();
    }
    if (ui.scheduleEditorSpecificTime) {
        ui.scheduleEditorSpecificTime.value = "09:00:00";
    }
    if (ui.scheduleEditorIntervalHours) ui.scheduleEditorIntervalHours.value = "0";
    if (ui.scheduleEditorIntervalMinutes) ui.scheduleEditorIntervalMinutes.value = "0";
    if (ui.scheduleEditorIntervalSeconds) ui.scheduleEditorIntervalSeconds.value = "0";
    syncScheduleEditorVisibility();
    syncScheduleEditorLockState();
    setScheduleEditorStatus("Configure and save schedule.", "neutral");
}

function openScheduleEditorForRecreate(scheduleName) {
    openScheduleEditorModal();
    const normalizedName = (scheduleName || "").trim();
    if (!normalizedName) {
        return;
    }

    state.scheduleEditorCurrentName = "";
    state.scheduleEditorSelectedStepFileNames = [];
    if (ui.scheduleEditorExistingSelect) {
        ui.scheduleEditorExistingSelect.value = "";
    }
    if (ui.scheduleEditorNameInput) {
        ui.scheduleEditorNameInput.value = normalizedName;
        ui.scheduleEditorNameInput.focus();
    }
    setScheduleEditorStatus(`Recreate '${normalizedName}' by selecting workflow and saving.`, "warning");
}

async function openScheduleEditorForEdit(scheduleName) {
    openScheduleEditorModal();
    const normalizedName = (scheduleName || "").trim();
    if (!normalizedName) {
        return;
    }

    state.scheduleEditorLockedName = normalizedName;
    state.scheduleEditorCurrentName = normalizedName;
    syncScheduleEditorLockState();
    if (ui.scheduleEditorExistingSelect) {
        ui.scheduleEditorExistingSelect.value = normalizedName;
    }
    await loadScheduleIntoEditor(normalizedName);
}

function closeScheduleEditorModal() {
    if (!ui.scheduleEditorModal) {
        return;
    }

    state.scheduleEditorOpen = false;
    state.scheduleEditorLockedName = "";
    ui.scheduleEditorModal.classList.add("hidden");
    ui.scheduleEditorModal.classList.remove("flex");
}

function syncScheduleEditorLockState() {
    const isLocked = Boolean((state.scheduleEditorLockedName || "").trim());

    if (ui.scheduleEditorNameInput) {
        ui.scheduleEditorNameInput.readOnly = isLocked;
        ui.scheduleEditorNameInput.classList.toggle("bg-slate-100", isLocked);
        ui.scheduleEditorNameInput.classList.toggle("text-slate-500", isLocked);
        ui.scheduleEditorNameInput.classList.toggle("cursor-not-allowed", isLocked);
    }

    if (ui.scheduleEditorExistingSelect) {
        ui.scheduleEditorExistingSelect.disabled = isLocked;
        ui.scheduleEditorExistingSelect.classList.toggle("bg-slate-100", isLocked);
        ui.scheduleEditorExistingSelect.classList.toggle("text-slate-500", isLocked);
        ui.scheduleEditorExistingSelect.classList.toggle("cursor-not-allowed", isLocked);
    }
}

function setScheduleEditorStatus(message, tone = "neutral") {
    if (!ui.scheduleEditorStatus) {
        return;
    }

    const classByTone = {
        neutral: "text-sm text-slate-600",
        success: "text-sm text-emerald-700",
        warning: "text-sm text-amber-700",
        error: "text-sm text-red-700"
    };

    ui.scheduleEditorStatus.textContent = message || "";
    ui.scheduleEditorStatus.className = classByTone[tone] || classByTone.neutral;
}

function getServerById(serverId) {
    return (state.llmServers || []).find((server) => server.id === serverId) || null;
}

function updateScheduleEditorRegularModelOptions() {
    if (!ui.scheduleEditorRegularServer || !ui.scheduleEditorRegularModel) {
        return;
    }

    const serverId = ui.scheduleEditorRegularServer.value || "";
    const server = getServerById(serverId);
    const models = Array.isArray(server?.models) ? server.models : [];

    ui.scheduleEditorRegularModel.innerHTML = "";
    if (models.length === 0) {
        const option = document.createElement("option");
        option.value = "";
        option.textContent = "(no models)";
        ui.scheduleEditorRegularModel.appendChild(option);
        return;
    }

    models.forEach((model) => {
        const option = document.createElement("option");
        option.value = model.id;
        option.textContent = model.name;
        ui.scheduleEditorRegularModel.appendChild(option);
    });
}

function renderScheduleEditorSelectors() {
    if (!ui.scheduleEditorExistingSelect || !ui.scheduleEditorWorkflowSelect || !ui.scheduleEditorRegularServer) {
        return;
    }

    ui.scheduleEditorExistingSelect.innerHTML = '<option value="">(new schedule)</option>';
    state.schedules
        .filter((schedule) => (schedule.status || "ready") === "ready")
        .forEach((schedule) => {
        const option = document.createElement("option");
        option.value = schedule.scheduleName;
        option.textContent = schedule.scheduleName;
        ui.scheduleEditorExistingSelect.appendChild(option);
    });

    ui.scheduleEditorWorkflowSelect.innerHTML = "";
    (state.workflowTypes || []).forEach((workflowType) => {
        const option = document.createElement("option");
        option.value = workflowType.id;
        option.textContent = workflowType.name;
        ui.scheduleEditorWorkflowSelect.appendChild(option);
    });

    ui.scheduleEditorRegularServer.innerHTML = "";
    (state.llmServers || []).forEach((server) => {
        const option = document.createElement("option");
        option.value = server.id;
        option.textContent = server.name;
        ui.scheduleEditorRegularServer.appendChild(option);
    });

    updateScheduleEditorRegularModelOptions();
    renderScheduleEditorBenchmarkList();
    const selectedWorkflowTypeId = ui.scheduleEditorWorkflowSelect?.value || "";
    void loadScheduleEditorWorkflowSteps(selectedWorkflowTypeId, state.scheduleEditorSelectedStepFileNames);
}

function renderScheduleEditorStepList() {
    if (!ui.scheduleEditorStepsList) {
        return;
    }

    const steps = state.scheduleEditorWorkflowSteps || [];
    if (steps.length <= 1) {
        ui.scheduleEditorStepsList.innerHTML = '<p class="text-slate-500">This workflow has a single step and will always run it.</p>';
        return;
    }

    const selected = new Set(state.scheduleEditorSelectedStepFileNames || []);
    ui.scheduleEditorStepsList.innerHTML = steps.map((step, index) => {
        const stepFileName = step.stepFileName || "";
        const checked = selected.has(stepFileName) ? "checked" : "";
        return `
            <label class="flex items-center gap-2 rounded px-2 py-1 hover:bg-slate-50">
                <input type="checkbox" class="schedule-step-checkbox" data-step-file-name="${escapeHtml(stepFileName)}" ${checked}>
                <span>${index + 1}. ${escapeHtml(stepFileName)}</span>
            </label>
        `;
    }).join("");
}

async function loadScheduleEditorWorkflowSteps(workflowTypeId, preferredSelectedStepFileNames = []) {
    state.scheduleEditorWorkflowSteps = [];
    state.scheduleEditorSelectedStepFileNames = [];
    if (!workflowTypeId) {
        renderScheduleEditorStepList();
        syncScheduleEditorVisibility();
        return;
    }

    try {
        const result = await apiClient.getWorkflowTypeSteps(workflowTypeId);
        const steps = Array.isArray(result?.steps) ? result.steps : [];
        state.scheduleEditorWorkflowSteps = steps;

        const normalizedPreferred = new Set((preferredSelectedStepFileNames || []).filter((item) => typeof item === "string" && item.trim().length > 0));
        const availableStepNames = steps.map((item) => item.stepFileName).filter((item) => typeof item === "string" && item.trim().length > 0);
        if (steps.length > 1) {
            if (normalizedPreferred.size > 0) {
                state.scheduleEditorSelectedStepFileNames = availableStepNames.filter((stepFileName) => normalizedPreferred.has(stepFileName));
            } else {
                state.scheduleEditorSelectedStepFileNames = [...availableStepNames];
            }
        } else {
            state.scheduleEditorSelectedStepFileNames = [];
        }
    } catch (error) {
        state.scheduleEditorWorkflowSteps = [];
        state.scheduleEditorSelectedStepFileNames = [];
        logTerminal(`[ERROR] Failed to load workflow steps for schedule editor: ${error.message}`, "red");
    }

    renderScheduleEditorStepList();
    syncScheduleEditorVisibility();
}

function renderScheduleEditorBenchmarkList(selectedKeys = state.scheduleEditorBenchmarkSelectionKeys) {
    if (!ui.scheduleEditorBenchmarkList) {
        return;
    }

    const keySet = new Set(selectedKeys || []);
    const rows = [];
    (state.llmServers || []).forEach((server) => {
        (server.models || []).forEach((model) => {
            const key = `${server.id}|${model.id}`;
            const checked = keySet.has(key) ? "checked" : "";
            rows.push(`
                <label class="flex items-center gap-2 rounded px-2 py-1 hover:bg-slate-50">
                    <input type="checkbox" class="schedule-benchmark-checkbox" data-key="${key}" ${checked}>
                    <span>${escapeHtml(server.name)} / ${escapeHtml(model.name)}</span>
                </label>
            `);
        });
    });

    ui.scheduleEditorBenchmarkList.innerHTML = rows.length > 0
        ? rows.join("")
        : '<p class="text-slate-500">No LLM server/model combos available.</p>';
}

function syncScheduleEditorVisibility() {
    const triggerType = ui.scheduleEditorTriggerType?.value || "immediate";
    const scheduleType = ui.scheduleEditorScheduleType?.value || "regular";

    if (ui.scheduleEditorSpecificTimeGroup) {
        ui.scheduleEditorSpecificTimeGroup.classList.toggle("hidden", triggerType !== "specificTime");
    }
    if (ui.scheduleEditorFrequencyGroup) {
        ui.scheduleEditorFrequencyGroup.classList.toggle("hidden", triggerType !== "frequency");
    }
    if (ui.scheduleEditorRegularGroup) {
        ui.scheduleEditorRegularGroup.classList.toggle("hidden", scheduleType !== "regular");
    }
    if (ui.scheduleEditorBenchmarkGroup) {
        ui.scheduleEditorBenchmarkGroup.classList.toggle("hidden", scheduleType !== "benchmark");
    }
    if (ui.scheduleEditorStepsGroup) {
        ui.scheduleEditorStepsGroup.classList.toggle("hidden", (state.scheduleEditorWorkflowSteps || []).length <= 1);
    }
}

function normalizeScheduleState(scheduleState) {
    state.scheduleState = scheduleState || null;
    state.activeMode = state.scheduleState?.isActive ? "schedule" : "workflow";
    updateActiveModeBadge();

    if (ui.scheduleStatus) {
        const runtime = state.scheduleState;
        if (runtime?.isActive) {
            const nextRun = runtime.nextRunLocal ? ` • Next: ${runtime.nextRunLocal}` : "";
            ui.scheduleStatus.textContent = `Active: ${runtime.activeScheduleName || "(unknown)"} (${runtime.triggerType || "trigger"})${nextRun}`;
        } else {
            ui.scheduleStatus.textContent = "No active schedule.";
        }
    }
}

function renderScheduleList() {
    if (!ui.scheduleList) {
        return;
    }

    if (!state.schedules || state.schedules.length === 0) {
        ui.scheduleList.innerHTML = '<p class="text-slate-500">No schedules found. Click "Create" to add one.</p>';
        return;
    }

    const activeScheduleName = state.scheduleState?.activeScheduleName || "";
    ui.scheduleList.innerHTML = state.schedules.map((schedule) => {
        const status = (schedule.status || "ready").toLowerCase();
        const isCorrupted = status === "corrupted";
        const isActive = activeScheduleName === schedule.scheduleName;
        const detailText = isCorrupted
            ? `Corrupted • ${escapeHtml(schedule.error || "Missing or invalid schedule.json")}`
            : `${escapeHtml(schedule.workflowTypeId)} • ${escapeHtml(schedule.scheduleType)} • ${escapeHtml(schedule.triggerType)}`;
        return `
            <div class="flex items-center justify-between border-b border-slate-200 px-2 py-2">
                <div>
                    <p class="font-semibold text-slate-800">${escapeHtml(schedule.scheduleName)}</p>
                    <p class="text-xs ${isCorrupted ? "text-amber-700" : "text-slate-500"}">${detailText}</p>
                </div>
                <div class="flex items-center gap-2">
                    ${isActive ? '<span class="rounded bg-purple-100 px-2 py-0.5 text-xs font-semibold text-purple-700">Active</span>' : ""}
                    ${isCorrupted ? '<span class="rounded bg-amber-100 px-2 py-0.5 text-xs font-semibold text-amber-700">Corrupted</span>' : ""}
                    <button class="schedule-start-btn rounded bg-cyan-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-cyan-500 transition disabled:opacity-50"
                            data-schedule-name="${escapeHtml(schedule.scheduleName)}"
                            ${isActive || isCorrupted ? "disabled" : ""}>
                        Start
                    </button>
                    ${!isCorrupted ? `
                        <button class="schedule-edit-btn rounded border border-slate-300 bg-white px-3 py-1.5 text-xs font-semibold text-slate-700 hover:bg-slate-50 transition"
                                data-schedule-name="${escapeHtml(schedule.scheduleName)}">
                            Edit
                        </button>` : ""}
                    ${isCorrupted ? `
                        <button class="schedule-recreate-btn rounded border border-amber-300 bg-amber-50 px-3 py-1.5 text-xs font-semibold text-amber-700 hover:bg-amber-100 transition"
                                data-schedule-name="${escapeHtml(schedule.scheduleName)}">
                            Recreate
                        </button>` : ""}
                </div>
            </div>
        `;
    }).join("");
}

async function loadSchedules() {
    try {
        const result = await apiClient.getSchedules();
        state.schedules = Array.isArray(result?.schedules) ? result.schedules : [];
        normalizeScheduleState(result?.scheduleState || null);
        renderScheduleList();
        if (ui.scheduleStopBtn) {
            ui.scheduleStopBtn.disabled = !state.scheduleState?.isActive;
        }
        if (state.scheduleEditorOpen) {
            renderScheduleEditorSelectors();
        }
        updateBusyState(state.busy);
    } catch (error) {
        logTerminal(`[ERROR] Failed to load schedules: ${error.message}`, "red");
    }
}

async function startSchedule(scheduleName) {
    if (!scheduleName) {
        return;
    }

    try {
        const result = await apiClient.startSchedule(scheduleName);
        logTerminal(`[SCHEDULE] ${result.message || `Started ${scheduleName}`}`, "cyan");
        await refreshWorkflowHeaderStateAfterScheduleStart();
        await loadSchedules();
        closeScheduleModal();
        updateBusyState(state.busy);
    } catch (error) {
        if (error?.details?.requiresConfirmation) {
            const reason = error.details.confirmationReason || "Starting this schedule will clear the current paused/resumable workflow context.";
            const confirmStart = window.confirm(`${reason}\n\nContinue?`);
            if (!confirmStart) {
                logTerminal("[SCHEDULE] Start canceled by user.", "orange");
                return;
            }

            try {
                const confirmedResult = await apiClient.startSchedule(scheduleName, true);
                logTerminal(`[SCHEDULE] ${confirmedResult.message || `Started ${scheduleName}`}`, "cyan");
                await refreshWorkflowHeaderStateAfterScheduleStart();
                await loadSchedules();
                closeScheduleModal();
                updateBusyState(state.busy);
                return;
            } catch (confirmedError) {
                logTerminal(`[ERROR] Failed to start schedule: ${confirmedError.message}`, "red");
                return;
            }
        }

        logTerminal(`[ERROR] Failed to start schedule: ${error.message}`, "red");
    }
}

async function refreshWorkflowHeaderStateAfterScheduleStart() {
    const maxAttempts = 8;
    for (let attempt = 0; attempt < maxAttempts; attempt++) {
        try {
            const workflowState = await apiClient.getWorkflowState();
            applyHydration(workflowState);

            const hasProject = Boolean((state.selectedProjectName || "").trim());
            const hasWorkflow = Boolean((state.selectedWorkflowTypeName || "").trim());
            if (hasProject || hasWorkflow || state.workflowRunning || state.canResume) {
                return;
            }
        } catch {
            // Best effort refresh only.
        }

        await new Promise((resolve) => setTimeout(resolve, 250));
    }
}

async function stopSchedule() {
    try {
        const result = await apiClient.stopSchedule();
        logTerminal(`[SCHEDULE] ${result.message || "Stopped schedule."}`, "orange");
        await loadSchedules();
        updateBusyState(state.busy);
    } catch (error) {
        logTerminal(`[ERROR] Failed to stop schedule: ${error.message}`, "red");
    }
}

async function loadScheduleIntoEditor(scheduleName) {
    if (!scheduleName) {
        state.scheduleEditorCurrentName = "";
        state.scheduleEditorWorkflowSteps = [];
        state.scheduleEditorSelectedStepFileNames = [];
        state.scheduleEditorBenchmarkSelectionKeys = [];
        if (ui.scheduleEditorNameInput) {
            ui.scheduleEditorNameInput.value = "";
        }
        renderScheduleEditorBenchmarkList();
        renderScheduleEditorStepList();
        syncScheduleEditorVisibility();
        return;
    }

    try {
        const result = await apiClient.getSchedule(scheduleName);
        const schedule = result?.schedule;
        if (!schedule) {
            return;
        }

        state.scheduleEditorCurrentName = schedule.scheduleName || "";
        if (ui.scheduleEditorNameInput) {
            ui.scheduleEditorNameInput.value = schedule.scheduleName || "";
        }
        if (ui.scheduleEditorWorkflowSelect) {
            ui.scheduleEditorWorkflowSelect.value = schedule.workflowTypeId || "";
        }
        state.scheduleEditorSelectedStepFileNames = Array.isArray(schedule.selectedStepFileNames)
            ? schedule.selectedStepFileNames.filter((item) => typeof item === "string" && item.trim().length > 0)
            : [];
        if (ui.scheduleEditorTriggerType) {
            ui.scheduleEditorTriggerType.value = schedule.trigger?.type || "immediate";
        }
        if (ui.scheduleEditorScheduleType) {
            ui.scheduleEditorScheduleType.value = schedule.scheduleType || "regular";
        }
        if (ui.scheduleEditorSpecificTime) {
            ui.scheduleEditorSpecificTime.value = schedule.trigger?.specificTimeLocal || "09:00:00";
        }
        if (ui.scheduleEditorIntervalHours) {
            ui.scheduleEditorIntervalHours.value = schedule.trigger?.intervalHours ?? 0;
        }
        if (ui.scheduleEditorIntervalMinutes) {
            ui.scheduleEditorIntervalMinutes.value = schedule.trigger?.intervalMinutes ?? 0;
        }
        if (ui.scheduleEditorIntervalSeconds) {
            ui.scheduleEditorIntervalSeconds.value = schedule.trigger?.intervalSeconds ?? 0;
        }
        if (ui.scheduleEditorRegularServer) {
            ui.scheduleEditorRegularServer.value = schedule.regularSelection?.serverId || ui.scheduleEditorRegularServer.value || "";
            updateScheduleEditorRegularModelOptions();
        }
        if (ui.scheduleEditorRegularModel) {
            ui.scheduleEditorRegularModel.value = schedule.regularSelection?.modelId || ui.scheduleEditorRegularModel.value || "";
        }

        state.scheduleEditorBenchmarkSelectionKeys = Array.isArray(schedule.benchmarkSelections)
            ? schedule.benchmarkSelections.map((item) => `${item.serverId}|${item.modelId}`)
            : [];
        await loadScheduleEditorWorkflowSteps(schedule.workflowTypeId || "", state.scheduleEditorSelectedStepFileNames);
        renderScheduleEditorBenchmarkList();
        syncScheduleEditorVisibility();
        setScheduleEditorStatus(`Loaded schedule '${scheduleName}'.`, "success");
    } catch (error) {
        logTerminal(`[ERROR] Failed to load schedule '${scheduleName}': ${error.message}`, "red");
        setScheduleEditorStatus(`Failed to load schedule '${scheduleName}'.`, "error");
    }
}

function buildScheduleEditorPayload() {
    const scheduleName = (ui.scheduleEditorNameInput?.value || "").trim();
    const workflowTypeId = (ui.scheduleEditorWorkflowSelect?.value || "").trim();
    const triggerType = ui.scheduleEditorTriggerType?.value || "immediate";
    const scheduleType = ui.scheduleEditorScheduleType?.value || "regular";
    const specificTimeLocal = (ui.scheduleEditorSpecificTime?.value || "").trim();
    const intervalHours = Number(ui.scheduleEditorIntervalHours?.value || 0);
    const intervalMinutes = Number(ui.scheduleEditorIntervalMinutes?.value || 0);
    const intervalSeconds = Number(ui.scheduleEditorIntervalSeconds?.value || 0);

    const regularSelection = {
        serverId: ui.scheduleEditorRegularServer?.value || "",
        modelId: ui.scheduleEditorRegularModel?.value || ""
    };
    const benchmarkSelections = state.scheduleEditorBenchmarkSelectionKeys.map((key) => {
        const [serverId, modelId] = key.split("|");
        return { serverId, modelId };
    });

    return {
        scheduleName,
        workflowTypeId,
        selectedStepFileNames: state.scheduleEditorSelectedStepFileNames,
        trigger: {
            type: triggerType,
            specificTimeLocal,
            intervalHours,
            intervalMinutes,
            intervalSeconds
        },
        scheduleType,
        regularSelection,
        benchmarkSelections
    };
}

async function saveScheduleFromEditor() {
    const payload = buildScheduleEditorPayload();
    const namePattern = /^[A-Za-z0-9_-]+$/;

    if (!namePattern.test(payload.scheduleName)) {
        setScheduleEditorStatus("Schedule name must contain only letters, numbers, dashes (-), or underscores (_).", "error");
        return;
    }

    if (!payload.workflowTypeId) {
        setScheduleEditorStatus("Workflow type is required.", "error");
        return;
    }

    if ((state.scheduleEditorWorkflowSteps || []).length > 1 && (payload.selectedStepFileNames || []).length === 0) {
        setScheduleEditorStatus("Select at least one step for multi-step workflows.", "error");
        return;
    }

    const originalText = ui.scheduleEditorSaveBtn?.textContent || "Save Schedule";
    if (ui.scheduleEditorSaveBtn) {
        ui.scheduleEditorSaveBtn.textContent = "Saving...";
        ui.scheduleEditorSaveBtn.disabled = true;
    }

    try {
        if (state.schedules.some((item) => item.scheduleName === payload.scheduleName)) {
            await apiClient.updateSchedule(payload.scheduleName, payload);
        } else {
            await apiClient.createSchedule(payload);
        }

        setScheduleEditorStatus(`Saved schedule '${payload.scheduleName}'.`, "success");
        logTerminal(`[SCHEDULE] Saved ${payload.scheduleName}`, "green");
        await loadSchedules();
        state.scheduleEditorCurrentName = payload.scheduleName;
        if (ui.scheduleEditorExistingSelect) {
            ui.scheduleEditorExistingSelect.value = payload.scheduleName;
        }
        closeScheduleEditorModal();
        openScheduleModal();
    } catch (error) {
        setScheduleEditorStatus(`Failed to save schedule: ${error.message}`, "error");
    } finally {
        if (ui.scheduleEditorSaveBtn) {
            ui.scheduleEditorSaveBtn.textContent = originalText;
            ui.scheduleEditorSaveBtn.disabled = false;
        }
    }
}

function setWorkflowEditorStatus(message, tone = "neutral") {
    if (!ui.workflowEditorStatus) {
        return;
    }

    const classByTone = {
        neutral: "text-sm text-slate-600",
        success: "text-sm text-emerald-700",
        warning: "text-sm text-amber-700",
        error: "text-sm text-red-700"
    };

    ui.workflowEditorStatus.textContent = message || "";
    ui.workflowEditorStatus.className = classByTone[tone] || classByTone.neutral;
}

function setWorkflowEditorDirty(isDirty) {
    state.workflowEditorDirty = Boolean(isDirty);
    if (state.workflowEditorDirty) {
        setWorkflowEditorStatus("You have unsaved changes.", "warning");
    } else if (state.workflowEditorSelectedStepFileName) {
        setWorkflowEditorStatus("Step loaded.", "neutral");
    } else {
        setWorkflowEditorStatus("Make a selection to begin editing.", "neutral");
    }
}

function updateWorkflowEditorSelectionLabels() {
    if (ui.workflowEditorSelectedWorkflow) {
        ui.workflowEditorSelectedWorkflow.textContent = state.workflowEditorSelectedWorkflowName || state.workflowEditorSelectedWorkflowId || "(none)";
    }

    if (ui.workflowEditorSelectedStep) {
        ui.workflowEditorSelectedStep.textContent = state.workflowEditorSelectedStepFileName || "(none)";
    }
}

function hasTicketIterationExecutionMode(metadataJsonContent) {
    if (typeof metadataJsonContent !== "string" || metadataJsonContent.trim().length === 0) {
        return false;
    }

    try {
        const payload = JSON.parse(metadataJsonContent);
        if (!payload || typeof payload !== "object") {
            return false;
        }

        const executionMode = typeof payload.executionMode === "string"
            ? payload.executionMode.trim().toLowerCase()
            : "";
        return executionMode === "ticketiteration";
    } catch {
        return false;
    }
}

function buildDefaultTicketIterationMetadataContent() {
    return JSON.stringify({
        executionMode: "ticketIteration",
        ticketSource: "{{TICKETS_DIR}}/tickets.json",
        completedSource: "{{TICKETS_DIR}}/completed.json",
        maxRetriesPerTicket: 3
    }, null, 2);
}

function syncWorkflowEditorIterationLayout() {
    const hasStep = Boolean(state.workflowEditorSelectedStepFileName);
    const showMetadataPanel = hasStep && state.workflowEditorHasTicketIteration;
    const showCreateIterationButton = hasStep && !state.workflowEditorHasTicketIteration;

    if (ui.workflowEditorMetadataPanel) {
        ui.workflowEditorMetadataPanel.classList.toggle("hidden", !showMetadataPanel);
    }

    if (ui.workflowEditorEditorsGrid) {
        ui.workflowEditorEditorsGrid.classList.toggle("xl:grid-cols-2", showMetadataPanel);
        ui.workflowEditorEditorsGrid.classList.toggle("xl:grid-cols-1", !showMetadataPanel);
    }

    if (ui.workflowEditorCreateIterationBtn) {
        ui.workflowEditorCreateIterationBtn.classList.toggle("hidden", !showCreateIterationButton);
    }
}

function syncWorkflowEditorControls() {
    const hasWorkflow = Boolean(state.workflowEditorSelectedWorkflowId);
    const hasStep = Boolean(state.workflowEditorSelectedStepFileName);
    const isLocked = state.workflowEditorLoading;
    const canEditStep = hasWorkflow && hasStep && !isLocked;

    if (ui.workflowEditorOpenCreateBtn) {
        ui.workflowEditorOpenCreateBtn.disabled = isLocked;
    }

    if (ui.workflowEditorAddStepBtn) {
        ui.workflowEditorAddStepBtn.disabled = !hasWorkflow || isLocked;
    }

    if (ui.workflowEditorSaveBtn) {
        ui.workflowEditorSaveBtn.disabled = !canEditStep;
    }

    if (ui.workflowEditorMarkdown) {
        ui.workflowEditorMarkdown.disabled = !canEditStep;
    }

    if (ui.workflowEditorMetadata) {
        ui.workflowEditorMetadata.disabled = !canEditStep;
    }

    if (ui.workflowEditorCreateSubmitBtn) {
        ui.workflowEditorCreateSubmitBtn.disabled = isLocked;
    }

    if (ui.workflowEditorCreateIterationBtn) {
        ui.workflowEditorCreateIterationBtn.disabled = !hasStep || state.workflowEditorHasTicketIteration || isLocked;
    }

    if (ui.manageWorkflowTypesBtn) {
        ui.manageWorkflowTypesBtn.disabled = state.busy;
    }

    syncWorkflowEditorIterationLayout();
    updateWorkflowEditorSelectionLabels();
}

function renderWorkflowEditorWorkflowList() {
    if (!ui.workflowEditorWorkflowsList) {
        return;
    }

    if (state.workflowTypes.length === 0) {
        ui.workflowEditorWorkflowsList.innerHTML = `<div class="text-sm text-slate-500">No workflow types found.</div>`;
        return;
    }

    ui.workflowEditorWorkflowsList.innerHTML = state.workflowTypes.map((workflowType, index) => {
        const isSelected = workflowType.id === state.workflowEditorSelectedWorkflowId;
        const rowBackgroundClass = index % 2 === 0 ? "bg-white" : "bg-slate-50";
        const selectButtonClass = isSelected
            ? "w-full rounded border border-indigo-300 bg-indigo-50 px-2 py-2 text-left text-sm font-semibold text-indigo-800"
            : `w-full rounded border border-slate-200 ${rowBackgroundClass} px-2 py-2 text-left text-sm text-slate-700 hover:bg-slate-100`;
        const isDeletingSelectedWorkflow = isSelected && state.workflowEditorDeletingWorkflowId === workflowType.id;
        const deleteButtonDisabled = isDeletingSelectedWorkflow ? "disabled" : "";
        const showDeleteButton = isSelected && state.workflowEditorDeleteWorkflowVisibleId === workflowType.id;
        const workflowDeleteButton = showDeleteButton
            ? `
                <button type="button"
                        data-workflow-editor-action="delete-workflow"
                        data-workflow-id="${escapeHtml(workflowType.id)}"
                        class="rounded bg-red-600 px-2.5 py-2 text-xs font-semibold text-white hover:bg-red-500 transition disabled:opacity-50"
                        ${deleteButtonDisabled}>
                    Delete
                </button>
            `
            : "";
        return `
            <div class="flex items-start gap-2">
                <button type="button"
                        data-workflow-editor-action="select-workflow"
                        data-workflow-id="${escapeHtml(workflowType.id)}"
                        class="${selectButtonClass}">
                    <div>${escapeHtml(workflowType.name || workflowType.id)}</div>
                    <div class="text-xs ${isSelected ? "text-indigo-700" : "text-slate-500"}">${Number(workflowType.stepCount || 0)} step${Number(workflowType.stepCount || 0) === 1 ? "" : "s"}</div>
                </button>
                ${workflowDeleteButton}
            </div>
        `;
    }).join("<div class=\"h-2\"></div>");
}

function scrollWorkflowEditorWorkflowIntoView(workflowTypeId, behavior = "smooth") {
    if (!ui.workflowEditorWorkflowsList || !workflowTypeId) {
        return;
    }

    const workflowButtons = ui.workflowEditorWorkflowsList.querySelectorAll("[data-workflow-editor-action=\"select-workflow\"]");
    for (const button of workflowButtons) {
        if (!(button instanceof HTMLElement)) {
            continue;
        }

        if ((button.dataset.workflowId || "").trim() !== workflowTypeId) {
            continue;
        }

        button.scrollIntoView({ behavior, block: "nearest" });
        break;
    }
}

function renderWorkflowEditorStepList() {
    if (!ui.workflowEditorStepsList) {
        return;
    }

    if (!state.workflowEditorSelectedWorkflowId) {
        ui.workflowEditorStepsList.innerHTML = `<div class="text-sm text-slate-500">Select a workflow type.</div>`;
        return;
    }

    if (state.workflowEditorSteps.length === 0) {
        ui.workflowEditorStepsList.innerHTML = `<div class="text-sm text-slate-500">No steps yet. Add your first step.</div>`;
        return;
    }

    ui.workflowEditorStepsList.innerHTML = state.workflowEditorSteps.map((step, index) => {
        const isSelected = step.stepFileName === state.workflowEditorSelectedStepFileName;
        const rowBackgroundClass = index % 2 === 0 ? "bg-white" : "bg-slate-50";
        const selectButtonClass = isSelected
            ? "w-full rounded border border-emerald-300 bg-emerald-50 px-2 py-2 text-left text-sm font-semibold text-emerald-800"
            : `w-full rounded border border-slate-200 ${rowBackgroundClass} px-2 py-2 text-left text-sm text-slate-700 hover:bg-slate-100`;
        const metadataTag = step.hasMetadata
            ? `<span class="ml-2 inline-flex items-center rounded border border-indigo-200 bg-indigo-50 px-2 py-0.5 text-[10px] font-semibold text-indigo-700">json</span>`
            : "";
        const isDeletingSelectedStep = isSelected && state.workflowEditorDeletingStepFileName === step.stepFileName;
        const showDeleteButton = isSelected && state.workflowEditorDeleteStepVisibleFileName === step.stepFileName;
        const deleteButton = showDeleteButton
            ? `
                <button type="button"
                        data-workflow-editor-action="delete-step"
                        data-step-file-name="${escapeHtml(step.stepFileName)}"
                        class="rounded bg-red-600 px-2.5 py-2 text-xs font-semibold text-white hover:bg-red-500 transition disabled:opacity-50"
                        ${isDeletingSelectedStep ? "disabled" : ""}>
                    Delete
                </button>
            `
            : "";
        return `
            <div class="flex items-start gap-2">
                <button type="button"
                        data-workflow-editor-action="select-step"
                        data-step-file-name="${escapeHtml(step.stepFileName)}"
                        class="${selectButtonClass}">
                    <div>
                        ${index + 1}. ${escapeHtml(step.stepFileName)}
                        ${metadataTag}
                    </div>
                </button>
                ${deleteButton}
            </div>
        `;
    }).join("<div class=\"h-2\"></div>");
}

function suggestWorkflowEditorStepName() {
    const existing = new Set(state.workflowEditorSteps.map((item) => (item.stepFileName || "").toLowerCase()));
    let nextIndex = state.workflowEditorSteps.length + 1;
    while (true) {
        const candidate = `Step${nextIndex}.md`;
        if (!existing.has(candidate.toLowerCase())) {
            return candidate;
        }
        nextIndex += 1;
    }
}

function setWorkflowEditorLoading(isLoading) {
    state.workflowEditorLoading = Boolean(isLoading);
    syncWorkflowEditorControls();
}

function confirmWorkflowEditorDiscard(nextActionLabel = "continue") {
    if (!state.workflowEditorDirty) {
        return true;
    }

    return window.confirm(`You have unsaved changes. Discard changes and ${nextActionLabel}?`);
}

function applyWorkflowEditorStepContent(payload) {
    state.workflowEditorSelectedStepFileName = payload?.stepFileName || state.workflowEditorSelectedStepFileName;
    state.workflowEditorMarkdownDraft = payload?.markdownContent || "";
    state.workflowEditorMetadataDraft = payload?.metadataJsonContent || "";
    state.workflowEditorHasTicketIteration = hasTicketIterationExecutionMode(state.workflowEditorMetadataDraft);

    if (ui.workflowEditorMarkdown) {
        ui.workflowEditorMarkdown.value = state.workflowEditorMarkdownDraft;
    }

    if (ui.workflowEditorMetadata) {
        ui.workflowEditorMetadata.value = state.workflowEditorMetadataDraft;
    }

    setWorkflowEditorDirty(false);
    renderWorkflowEditorStepList();
    syncWorkflowEditorControls();
}

async function loadWorkflowEditorSteps(workflowTypeId) {
    const payload = await apiClient.getWorkflowTypeSteps(workflowTypeId);
    state.workflowEditorSteps = Array.isArray(payload?.steps) ? payload.steps : [];
    renderWorkflowEditorStepList();
}

async function selectWorkflowEditorStep(stepFileName, { skipDirtyGuard = false, revealDeleteButton = false } = {}) {
    if (!state.workflowEditorSelectedWorkflowId) {
        return;
    }

    if (stepFileName === state.workflowEditorSelectedStepFileName) {
        state.workflowEditorDeleteStepVisibleFileName = revealDeleteButton ? stepFileName : "";
        renderWorkflowEditorStepList();
        return;
    }

    if (!skipDirtyGuard && state.workflowEditorSelectedStepFileName && stepFileName !== state.workflowEditorSelectedStepFileName) {
        const ok = confirmWorkflowEditorDiscard("switch steps");
        if (!ok) {
            return;
        }
    }

    state.workflowEditorDeleteStepVisibleFileName = "";
    renderWorkflowEditorStepList();
    setWorkflowEditorLoading(true);
    try {
        const payload = await apiClient.getWorkflowTypeStep(state.workflowEditorSelectedWorkflowId, stepFileName);
        applyWorkflowEditorStepContent(payload);
        state.workflowEditorDeleteStepVisibleFileName = revealDeleteButton ? payload.stepFileName : "";
        renderWorkflowEditorStepList();
        setWorkflowEditorStatus(`Loaded ${payload.stepFileName}.`, "neutral");
    } catch (error) {
        setWorkflowEditorStatus(`Failed to load step: ${error.message}`, "error");
        logTerminal(`[ERROR] Failed to load step: ${error.message}`, "red");
    } finally {
        setWorkflowEditorLoading(false);
    }
}

async function selectWorkflowEditorWorkflow(workflowTypeId, { skipDirtyGuard = false, preferredStepFileName = "", revealDeleteButton = false, scrollIntoView = false } = {}) {
    if (!workflowTypeId) {
        return;
    }

    if (workflowTypeId === state.workflowEditorSelectedWorkflowId) {
        state.workflowEditorDeleteWorkflowVisibleId = revealDeleteButton ? workflowTypeId : "";
        renderWorkflowEditorWorkflowList();
        if (scrollIntoView) {
            scrollWorkflowEditorWorkflowIntoView(workflowTypeId);
        }
        return;
    }

    if (!skipDirtyGuard && state.workflowEditorSelectedWorkflowId && workflowTypeId !== state.workflowEditorSelectedWorkflowId) {
        const ok = confirmWorkflowEditorDiscard("switch workflows");
        if (!ok) {
            return;
        }
    }

    const workflow = state.workflowTypes.find((item) => item.id === workflowTypeId);
    state.workflowEditorSelectedWorkflowId = workflowTypeId;
    state.workflowEditorSelectedWorkflowName = workflow?.name || workflowTypeId;
    state.workflowEditorDeleteWorkflowVisibleId = revealDeleteButton ? workflowTypeId : "";
    state.workflowEditorDeleteStepVisibleFileName = "";
    state.workflowEditorSelectedStepFileName = "";
    state.workflowEditorMarkdownDraft = "";
    state.workflowEditorMetadataDraft = "";
    state.workflowEditorHasTicketIteration = false;
    if (ui.workflowEditorMarkdown) {
        ui.workflowEditorMarkdown.value = "";
    }
    if (ui.workflowEditorMetadata) {
        ui.workflowEditorMetadata.value = "";
    }
    setWorkflowEditorDirty(false);
    renderWorkflowEditorWorkflowList();
    if (scrollIntoView) {
        scrollWorkflowEditorWorkflowIntoView(workflowTypeId);
    }
    renderWorkflowEditorStepList();
    syncWorkflowEditorControls();
    setWorkflowEditorStatus("Loading steps...", "neutral");

    setWorkflowEditorLoading(true);
    try {
        await loadWorkflowEditorSteps(workflowTypeId);
        const firstStep = preferredStepFileName || state.workflowEditorSteps[0]?.stepFileName || "";
        if (firstStep) {
            await selectWorkflowEditorStep(firstStep, { skipDirtyGuard: true });
        } else {
            setWorkflowEditorStatus("No steps found. Add a step to begin.", "warning");
        }
    } catch (error) {
        state.workflowEditorSteps = [];
        renderWorkflowEditorStepList();
        setWorkflowEditorStatus(`Failed to load steps: ${error.message}`, "error");
        logTerminal(`[ERROR] Failed to load workflow steps: ${error.message}`, "red");
    } finally {
        setWorkflowEditorLoading(false);
    }
}

function openWorkflowEditorCreateModal() {
    if (!ui.workflowEditorCreateModal) {
        return;
    }

    ui.workflowEditorCreateModal.classList.remove("hidden");
    ui.workflowEditorCreateModal.classList.add("flex");
    if (ui.workflowEditorCreateNameInput) {
        ui.workflowEditorCreateNameInput.value = "";
        ui.workflowEditorCreateNameInput.focus();
    }
}

function closeWorkflowEditorCreateModal() {
    if (!ui.workflowEditorCreateModal) {
        return;
    }

    ui.workflowEditorCreateModal.classList.add("hidden");
    ui.workflowEditorCreateModal.classList.remove("flex");
    if (ui.workflowEditorCreateNameInput) {
        ui.workflowEditorCreateNameInput.value = "";
    }
}

async function createWorkflowTypeFromEditor() {
    const workflowTypeId = (ui.workflowEditorCreateNameInput?.value || "").trim();

    if (!/^[A-Za-z0-9_-]+$/.test(workflowTypeId)) {
        setWorkflowEditorStatus("Workflow id must match: letters, numbers, dashes, underscores.", "error");
        return;
    }

    setWorkflowEditorLoading(true);
    try {
        const result = await apiClient.createWorkflowType(workflowTypeId);
        await loadWorkflowTypes();
        renderWorkflowEditorWorkflowList();
        syncWorkflowTypePicker();
        state.selectedWorkflowTypeId = workflowTypeId;
        state.selectedWorkflowTypeName = state.workflowTypes.find((item) => item.id === workflowTypeId)?.name || workflowTypeId;
        syncWorkflowTypePicker();
        setRequiredFieldValidation(!((state.selectedProjectName || state.targetProjectName || "").trim()), !state.selectedWorkflowTypeId);
        updateBusyState(state.busy);
        closeWorkflowEditorCreateModal();

        logTerminal(`[WORKFLOW] Created workflow type ${workflowTypeId}`, "green");
        setWorkflowEditorStatus("Workflow created successfully.", "success");
        const firstStep = result?.createdFiles?.find((fileName) => fileName.toLowerCase().endsWith(".md")) || "Step1.md";
        await selectWorkflowEditorWorkflow(workflowTypeId, {
            skipDirtyGuard: true,
            preferredStepFileName: firstStep,
            scrollIntoView: true
        });
    } catch (error) {
        setWorkflowEditorStatus(`Failed to create workflow: ${error.message}`, "error");
        logTerminal(`[ERROR] Failed to create workflow type: ${error.message}`, "red");
    } finally {
        setWorkflowEditorLoading(false);
    }
}

async function addStepFromWorkflowEditor() {
    if (!state.workflowEditorSelectedWorkflowId) {
        setWorkflowEditorStatus("Select a workflow type before adding a step.", "warning");
        return;
    }

    if (state.workflowEditorDirty) {
        const ok = confirmWorkflowEditorDiscard("add a new step");
        if (!ok) {
            return;
        }
    }

    const suggestedStepFileName = suggestWorkflowEditorStepName();
    const enteredStepFileName = window.prompt("Enter a step file name (.md)", suggestedStepFileName);
    if (enteredStepFileName === null) {
        return;
    }

    const trimmedStepFileName = enteredStepFileName.trim();
    const stepFileName = trimmedStepFileName.length > 0
        ? trimmedStepFileName
        : suggestedStepFileName;

    setWorkflowEditorLoading(true);
    try {
        const result = await apiClient.addWorkflowTypeStep(state.workflowEditorSelectedWorkflowId, stepFileName || null);
        await loadWorkflowTypes();
        syncWorkflowTypePicker();
        await loadWorkflowEditorSteps(state.workflowEditorSelectedWorkflowId);
        state.workflowEditorDeleteStepVisibleFileName = "";
        applyWorkflowEditorStepContent({
            stepFileName: result?.step?.stepFileName,
            markdownContent: result?.step?.markdownContent,
            metadataJsonContent: result?.step?.metadataJsonContent
        });

        setWorkflowEditorStatus(`Added ${result?.step?.stepFileName || "new step"}.`, "success");
        logTerminal(`[WORKFLOW] Added step ${result?.step?.stepFileName || "(unknown)"}`, "green");
    } catch (error) {
        setWorkflowEditorStatus(`Failed to add step: ${error.message}`, "error");
        logTerminal(`[ERROR] Failed to add workflow step: ${error.message}`, "red");
    } finally {
        setWorkflowEditorLoading(false);
    }
}

async function createTicketIterationForCurrentStep() {
    const workflowTypeId = state.workflowEditorSelectedWorkflowId;
    const stepFileName = state.workflowEditorSelectedStepFileName;
    if (!workflowTypeId || !stepFileName) {
        setWorkflowEditorStatus("Select a step before creating ticket iteration.", "warning");
        return;
    }

    if (state.workflowEditorHasTicketIteration) {
        setWorkflowEditorStatus("Ticket iteration already exists for this step.", "neutral");
        return;
    }

    const markdownContent = ui.workflowEditorMarkdown?.value ?? state.workflowEditorMarkdownDraft ?? "";
    const metadataJsonContent = buildDefaultTicketIterationMetadataContent();

    setWorkflowEditorLoading(true);
    try {
        await apiClient.saveWorkflowTypeStep(
            workflowTypeId,
            stepFileName,
            markdownContent,
            metadataJsonContent
        );

        state.workflowEditorMarkdownDraft = markdownContent;
        state.workflowEditorMetadataDraft = metadataJsonContent;
        state.workflowEditorHasTicketIteration = true;

        if (ui.workflowEditorMetadata) {
            ui.workflowEditorMetadata.value = metadataJsonContent;
        }

        setWorkflowEditorDirty(false);
        await loadWorkflowTypes();
        await loadWorkflowEditorSteps(workflowTypeId);
        setWorkflowEditorStatus("Ticket iteration created.", "success");
        logTerminal(`[WORKFLOW] Ticket iteration created for ${workflowTypeId}/${stepFileName}`, "green");
    } catch (error) {
        setWorkflowEditorStatus(`Failed to create ticket iteration: ${error.message}`, "error");
        logTerminal(`[ERROR] Failed to create ticket iteration: ${error.message}`, "red");
    } finally {
        setWorkflowEditorLoading(false);
    }
}

async function saveWorkflowEditorStep() {
    if (!state.workflowEditorSelectedWorkflowId || !state.workflowEditorSelectedStepFileName) {
        setWorkflowEditorStatus("Select a step before saving.", "warning");
        return;
    }

    const markdownContent = ui.workflowEditorMarkdown?.value ?? "";
    const metadataRaw = ui.workflowEditorMetadata?.value ?? "";
    const metadataTrimmed = metadataRaw.trim();

    if (metadataTrimmed.length > 0) {
        try {
            JSON.parse(metadataTrimmed);
        } catch (error) {
            setWorkflowEditorStatus(`Metadata JSON is invalid: ${error.message}`, "error");
            return;
        }
    }

    setWorkflowEditorLoading(true);
    try {
        await apiClient.saveWorkflowTypeStep(
            state.workflowEditorSelectedWorkflowId,
            state.workflowEditorSelectedStepFileName,
            markdownContent,
            metadataTrimmed.length > 0 ? metadataRaw : ""
        );

        state.workflowEditorMarkdownDraft = markdownContent;
        state.workflowEditorMetadataDraft = metadataTrimmed.length > 0 ? metadataRaw : "";
        state.workflowEditorHasTicketIteration = hasTicketIterationExecutionMode(state.workflowEditorMetadataDraft);
        setWorkflowEditorDirty(false);
        await loadWorkflowTypes();
        await loadWorkflowEditorSteps(state.workflowEditorSelectedWorkflowId);
        setWorkflowEditorStatus("Step saved.", "success");
        logTerminal(`[WORKFLOW] Saved ${state.workflowEditorSelectedWorkflowId}/${state.workflowEditorSelectedStepFileName}`, "green");
    } catch (error) {
        setWorkflowEditorStatus(`Failed to save step: ${error.message}`, "error");
        logTerminal(`[ERROR] Failed to save step: ${error.message}`, "red");
    } finally {
        setWorkflowEditorLoading(false);
    }
}

async function deleteWorkflowFromEditor() {
    const workflowTypeId = state.workflowEditorSelectedWorkflowId;
    if (!workflowTypeId) {
        setWorkflowEditorStatus("Select a workflow type before deleting.", "warning");
        return;
    }
    if (state.workflowEditorDeletingWorkflowId === workflowTypeId) {
        return;
    }

    if (state.workflowEditorDirty) {
        const discard = confirmWorkflowEditorDiscard("delete this workflow");
        if (!discard) {
            return;
        }
    }

    const confirmed = window.confirm(`Delete workflow type "${workflowTypeId}" and all steps? This cannot be undone.`);
    if (!confirmed) {
        return;
    }

    state.workflowEditorDeletingWorkflowId = workflowTypeId;
    state.workflowEditorDeletingStepFileName = "";
    state.workflowEditorDeleteWorkflowVisibleId = workflowTypeId;
    state.workflowEditorDeleteStepVisibleFileName = "";
    renderWorkflowEditorWorkflowList();
    renderWorkflowEditorStepList();
    setWorkflowEditorLoading(true);
    try {
        await apiClient.deleteWorkflowType(workflowTypeId);

        if (state.selectedWorkflowTypeId === workflowTypeId) {
            state.selectedWorkflowTypeId = "";
            state.selectedWorkflowTypeName = "";
        }

        state.workflowEditorSelectedWorkflowId = "";
        state.workflowEditorSelectedWorkflowName = "";
        state.workflowEditorDeleteWorkflowVisibleId = "";
        state.workflowEditorDeleteStepVisibleFileName = "";
        state.workflowEditorSelectedStepFileName = "";
        state.workflowEditorSteps = [];
        state.workflowEditorMarkdownDraft = "";
        state.workflowEditorMetadataDraft = "";
        state.workflowEditorHasTicketIteration = false;
        if (ui.workflowEditorMarkdown) {
            ui.workflowEditorMarkdown.value = "";
        }
        if (ui.workflowEditorMetadata) {
            ui.workflowEditorMetadata.value = "";
        }
        setWorkflowEditorDirty(false);

        await loadWorkflowTypes();
        syncWorkflowTypePicker();
        renderWorkflowEditorWorkflowList();
        renderWorkflowEditorStepList();
        setRequiredFieldValidation(!((state.selectedProjectName || state.targetProjectName || "").trim()), !state.selectedWorkflowTypeId);
        updateBusyState(state.busy);

        const nextWorkflowId = state.workflowTypes[0]?.id || "";
        if (nextWorkflowId) {
            await selectWorkflowEditorWorkflow(nextWorkflowId, { skipDirtyGuard: true });
        } else {
            setWorkflowEditorStatus("Workflow deleted. No workflow types remain.", "success");
        }

        logTerminal(`[WORKFLOW] Deleted workflow type ${workflowTypeId}`, "green");
    } catch (error) {
        setWorkflowEditorStatus(`Failed to delete workflow: ${error.message}`, "error");
        logTerminal(`[ERROR] Failed to delete workflow type: ${error.message}`, "red");
    } finally {
        state.workflowEditorDeletingWorkflowId = "";
        renderWorkflowEditorWorkflowList();
        renderWorkflowEditorStepList();
        setWorkflowEditorLoading(false);
    }
}

async function deleteStepFromEditor() {
    const workflowTypeId = state.workflowEditorSelectedWorkflowId;
    const stepFileName = state.workflowEditorSelectedStepFileName;
    if (!workflowTypeId || !stepFileName) {
        setWorkflowEditorStatus("Select a step before deleting.", "warning");
        return;
    }
    if (state.workflowEditorDeletingStepFileName === stepFileName) {
        return;
    }

    if (state.workflowEditorDirty) {
        const discard = confirmWorkflowEditorDiscard("delete this step");
        if (!discard) {
            return;
        }
    }

    const confirmed = window.confirm(`Delete step "${stepFileName}" from "${workflowTypeId}"? This cannot be undone.`);
    if (!confirmed) {
        return;
    }

    state.workflowEditorDeletingStepFileName = stepFileName;
    state.workflowEditorDeleteStepVisibleFileName = stepFileName;
    renderWorkflowEditorStepList();
    setWorkflowEditorLoading(true);
    try {
        await apiClient.deleteWorkflowTypeStep(workflowTypeId, stepFileName);

        await loadWorkflowTypes();
        syncWorkflowTypePicker();
        await loadWorkflowEditorSteps(workflowTypeId);

        const nextStep = state.workflowEditorSteps[0]?.stepFileName || "";
        if (nextStep) {
            await selectWorkflowEditorStep(nextStep, { skipDirtyGuard: true });
            setWorkflowEditorStatus(`Deleted ${stepFileName}.`, "success");
        } else {
            state.workflowEditorSelectedStepFileName = "";
            state.workflowEditorDeleteStepVisibleFileName = "";
            state.workflowEditorMarkdownDraft = "";
            state.workflowEditorMetadataDraft = "";
            state.workflowEditorHasTicketIteration = false;
            if (ui.workflowEditorMarkdown) {
                ui.workflowEditorMarkdown.value = "";
            }
            if (ui.workflowEditorMetadata) {
                ui.workflowEditorMetadata.value = "";
            }
            setWorkflowEditorDirty(false);
            setWorkflowEditorStatus("Step deleted. This workflow has no steps.", "success");
            renderWorkflowEditorStepList();
            syncWorkflowEditorControls();
        }

        logTerminal(`[WORKFLOW] Deleted step ${workflowTypeId}/${stepFileName}`, "green");
    } catch (error) {
        setWorkflowEditorStatus(`Failed to delete step: ${error.message}`, "error");
        logTerminal(`[ERROR] Failed to delete step: ${error.message}`, "red");
    } finally {
        state.workflowEditorDeletingStepFileName = "";
        renderWorkflowEditorStepList();
        setWorkflowEditorLoading(false);
    }
}

async function openWorkflowEditorModal() {
    if (!ui.workflowEditorModal) {
        return;
    }

    if (state.workflowEditorOpen) {
        return;
    }

    state.workflowEditorOpen = true;
    state.workflowEditorDeletingWorkflowId = "";
    state.workflowEditorDeletingStepFileName = "";
    state.workflowEditorDeleteWorkflowVisibleId = "";
    state.workflowEditorDeleteStepVisibleFileName = "";
    closeWorkflowEditorCreateModal();
    ui.workflowEditorModal.classList.remove("hidden");
    ui.workflowEditorModal.classList.add("flex");
    setWorkflowEditorStatus("Loading workflow types...", "neutral");
    setWorkflowEditorLoading(true);

    try {
        await loadWorkflowTypes();
        renderWorkflowEditorWorkflowList();
        const initialWorkflowId = state.workflowEditorSelectedWorkflowId
            || state.selectedWorkflowTypeId
            || state.workflowTypes[0]?.id
            || "";
        if (initialWorkflowId) {
            await selectWorkflowEditorWorkflow(initialWorkflowId, {
                skipDirtyGuard: true,
                scrollIntoView: true
            });
        } else {
            setWorkflowEditorStatus("Create a workflow type to get started.", "warning");
            state.workflowEditorSteps = [];
            renderWorkflowEditorStepList();
        }
    } finally {
        setWorkflowEditorLoading(false);
        syncWorkflowEditorControls();
    }
}

function closeWorkflowEditorModal() {
    if (!ui.workflowEditorModal || !state.workflowEditorOpen) {
        return;
    }

    if (!confirmWorkflowEditorDiscard("close the workflow editor")) {
        return;
    }

    state.workflowEditorOpen = false;
    state.workflowEditorDeletingWorkflowId = "";
    state.workflowEditorDeletingStepFileName = "";
    state.workflowEditorDeleteWorkflowVisibleId = "";
    state.workflowEditorDeleteStepVisibleFileName = "";
    closeWorkflowEditorCreateModal();
    ui.workflowEditorModal.classList.add("hidden");
    ui.workflowEditorModal.classList.remove("flex");
    setWorkflowEditorDirty(false);
    setWorkflowEditorLoading(false);
}

function openContentViewModal(title, subtitle, bodyHtml) {
    if (!ui.contentViewModal || !ui.contentViewTitle || !ui.contentViewSubtitle || !ui.contentViewBody) {
        return;
    }

    ui.contentViewTitle.textContent = title || "Viewer";
    ui.contentViewSubtitle.textContent = subtitle || "";
    ui.contentViewBody.innerHTML = bodyHtml || `<p class="text-sm text-slate-500">No content available.</p>`;
    ui.contentViewModal.classList.remove("hidden");
    ui.contentViewModal.classList.add("flex");
}

function closeContentViewModal() {
    if (!ui.contentViewModal) {
        return;
    }

    ui.contentViewModal.classList.add("hidden");
    ui.contentViewModal.classList.remove("flex");
}

function renderDefinitionListSection(label, items) {
    const rows = Array.isArray(items) ? items.filter(Boolean) : [];
    if (rows.length === 0) {
        return "";
    }

    return `
        <div>
            <h4 class="text-xs font-semibold uppercase tracking-wide text-slate-500 mb-2">${escapeHtml(label)}</h4>
            <ul class="list-disc pl-5 text-sm text-slate-700 space-y-1">
                ${rows.map((item) => `<li>${escapeHtml(item)}</li>`).join("")}
            </ul>
        </div>
    `;
}

function renderTicketViewerHtml(ticket) {
    if (!ticket) {
        return `<p class="text-sm text-slate-500">No ticket is available for this step.</p>`;
    }

    const description = escapeHtml(ticket.description || "No description provided.");
    const status = escapeHtml(ticket.status || "Open");
    const priority = escapeHtml(ticket.priority || "Unspecified");
    const type = escapeHtml(ticket.type || "Unspecified");
    const dependencies = renderDefinitionListSection("Dependencies", ticket.dependencies);
    const definitionOfDone = renderDefinitionListSection("Definition Of Done", ticket.definitionOfDone);

    return `
        <div class="space-y-4">
            <div class="rounded-lg border border-slate-200 bg-slate-50 p-3">
                <div class="flex flex-wrap items-center gap-2">
                    <span class="inline-flex items-center rounded border border-slate-300 bg-white px-2.5 py-1 text-xs font-semibold text-slate-700">${escapeHtml(ticket.ticketId || "Ticket")}</span>
                    <span class="inline-flex items-center rounded border border-slate-300 bg-white px-2.5 py-1 text-xs font-semibold text-slate-700">Status: ${status}</span>
                    <span class="inline-flex items-center rounded border border-slate-300 bg-white px-2.5 py-1 text-xs font-semibold text-slate-700">Priority: ${priority}</span>
                    <span class="inline-flex items-center rounded border border-slate-300 bg-white px-2.5 py-1 text-xs font-semibold text-slate-700">Type: ${type}</span>
                </div>
            </div>
            <div>
                <h4 class="text-xs font-semibold uppercase tracking-wide text-slate-500 mb-2">Description</h4>
                <div class="rounded-lg border border-slate-200 bg-white p-3 text-sm text-slate-800 whitespace-pre-wrap">${description}</div>
            </div>
            ${dependencies}
            ${definitionOfDone}
        </div>
    `;
}

async function openCurrentStepViewer() {
    if (!apiClient) {
        return;
    }

    try {
        const payload = await apiClient.getCurrentViewContent();
        const step = payload?.currentStep;
        if (!step) {
            openContentViewModal("Current Step", "Workflow step details", `<p class="text-sm text-slate-500">No current step is available yet.</p>`);
            return;
        }

        const markdownRender = renderAgentMarkdown(step.content || "");
        const body = markdownRender?.html
            ? `<article class="markdown-content text-slate-800">${markdownRender.html}</article>`
            : `<pre class="whitespace-pre-wrap text-sm text-slate-800">${escapeHtml(step.content || "")}</pre>`;
        openContentViewModal(
            step.stepName || "Current Step",
            `Step ${Number(step.stepNumber || 0)}${step.isTicketIterationStep ? " • Ticket iteration" : ""}`,
            body
        );
    } catch (error) {
        openContentViewModal("Current Step", "Workflow step details", `<p class="text-sm text-red-700">Failed to load step content: ${escapeHtml(error.message || "Unknown error")}</p>`);
    }
}

async function openCurrentTicketViewer() {
    if (!apiClient) {
        return;
    }

    try {
        const payload = await apiClient.getCurrentViewContent();
        const ticket = payload?.currentTicket;
        openContentViewModal(
            ticket?.title || "Current Ticket",
            ticket?.ticketId ? `Ticket ${ticket.ticketId}` : "Current ticket details",
            renderTicketViewerHtml(ticket)
        );
    } catch (error) {
        openContentViewModal("Current Ticket", "Workflow ticket details", `<p class="text-sm text-red-700">Failed to load ticket content: ${escapeHtml(error.message || "Unknown error")}</p>`);
    }
}

async function loadSkipOptions() {
    if (!apiClient) {
        return;
    }

    try {
        const payload = await apiClient.getSkipOptions();
        state.skipOptions = payload;
        renderSkipOptions(payload);
    } catch (error) {
        if (ui.skipModalStatus) {
            ui.skipModalStatus.textContent = `Failed to load skip options: ${error.message}`;
            ui.skipModalStatus.className = "text-sm text-red-700";
        }
    }
}

async function viewStepContent(stepNumber) {
    if (!apiClient) {
        return;
    }

    try {
        const payload = await apiClient.getStepContent(stepNumber);
        const step = payload;
        if (!step || !step.success) {
            openContentViewModal("Step File", `Step ${stepNumber}`, `<p class="text-sm text-slate-500">Step ${stepNumber} is not available.</p>`);
            return;
        }

        const markdownRender = renderAgentMarkdown(step.content || "");
        const body = markdownRender?.html
            ? `<article class="markdown-content text-slate-800">${markdownRender.html}</article>`
            : `<pre class="whitespace-pre-wrap text-sm text-slate-800">${escapeHtml(step.content || "")}</pre>`;
        openContentViewModal(
            step.stepName || `Step ${stepNumber}`,
            `Step ${Number(step.stepNumber || 0)}${step.isTicketIterationStep ? " • Ticket iteration" : ""}`,
            body
        );
    } catch (error) {
        openContentViewModal("Step File", `Step ${stepNumber}`, `<p class="text-sm text-red-700">Failed to load step content: ${escapeHtml(error.message || "Unknown error")}</p>`);
    }
}

function renderSkipOptions(payload) {
    if (!ui.skipStepsList || !ui.skipModalStatus) {
        return;
    }

    const steps = Array.isArray(payload?.steps) ? payload.steps : [];
    if (!payload?.success || steps.length === 0) {
        ui.skipModalStatus.textContent = payload?.message || "No steps available.";
        ui.skipModalStatus.className = "text-sm text-slate-600";
        ui.skipStepsList.innerHTML = "";
        return;
    }

    ui.skipModalStatus.textContent = "Select a step to skip to, or expand a ticket step to skip or resume a ticket.";
    ui.skipModalStatus.className = "text-sm text-slate-600";

    const html = steps.map((step) => {
        const stepNumber = Number(step.stepNumber || 0);
        const fileName = escapeHtml(step.fileName || step.stepName || `Step ${stepNumber}`);
        const isTicket = Boolean(step.isTicketIterationStep);
        const isCurrent = stepNumber === Number(state.currentStep || 0);
        const badge = isTicket
            ? `<span class="ml-2 inline-flex items-center rounded border border-indigo-200 bg-indigo-50 px-2 py-0.5 text-[11px] font-semibold text-indigo-700">Tickets</span>`
            : `<span class="ml-2 inline-flex items-center rounded border border-slate-200 bg-slate-50 px-2 py-0.5 text-[11px] font-semibold text-slate-700">Step</span>`;
        const currentBadge = isCurrent
            ? `<span class="ml-2 inline-flex items-center rounded border border-amber-200 bg-amber-50 px-2 py-0.5 text-[11px] font-semibold text-amber-800">Current</span>`
            : "";

        if (!isTicket) {
            return `
                <div class="mb-2 rounded-lg border border-slate-200 bg-white p-3">
                    <div class="flex items-start justify-between gap-3">
                        <div class="min-w-0">
                            <div class="text-sm font-semibold text-slate-900 truncate">Step ${stepNumber}: ${fileName}${badge}${currentBadge}</div>
                        </div>
                        <div class="shrink-0 flex items-center gap-2">
                            <button type="button"
                                    data-action="view-step"
                                    data-step-number="${stepNumber}"
                                    class="rounded border border-slate-300 bg-white px-3 py-1.5 text-xs font-semibold text-slate-700 hover:bg-slate-100 transition">
                                View
                            </button>
                            <button type="button"
                                    data-action="skip-step"
                                    data-step-number="${stepNumber}"
                                    class="rounded bg-slate-900 px-3 py-1.5 text-xs font-semibold text-white hover:bg-slate-800 transition">
                                Start Step
                            </button>
                        </div>
                    </div>
                </div>
            `;
        }

        const ticketInfo = step.ticketInfo || {};
        const warning = ticketInfo.warning ? `<div class="mt-2 text-xs text-amber-700">${escapeHtml(ticketInfo.warning)}</div>` : "";
        const tickets = Array.isArray(ticketInfo.tickets) ? ticketInfo.tickets : [];
        const ticketRows = tickets.length === 0
            ? `<div class="text-sm text-slate-500">No tickets found.</div>`
            : tickets.map((ticket) => {
                const ticketId = escapeHtml(ticket.ticketId || "");
                const title = escapeHtml(ticket.title || "(untitled)");
                const completed = Boolean(ticket.completed);
                const skipped = Boolean(ticket.skipped);
                const requested = Boolean(ticket.requested);
                const stateBadge = completed
                    ? `<span class="ml-2 inline-flex items-center rounded border border-emerald-200 bg-emerald-50 px-2 py-0.5 text-[11px] font-semibold text-emerald-700">Completed</span>`
                    : skipped
                        ? `<span class="ml-2 inline-flex items-center rounded border border-amber-200 bg-amber-50 px-2 py-0.5 text-[11px] font-semibold text-amber-800">Skipped</span>`
                        : "";
                const selectedBadge = requested
                    ? `<span class="ml-2 inline-flex items-center rounded border border-cyan-200 bg-cyan-50 px-2 py-0.5 text-[11px] font-semibold text-cyan-800">Selected</span>`
                    : "";
                const action = completed ? "reopen-ticket" : skipped ? "resume-ticket" : "skip-ticket";
                const buttonLabel = completed ? "Reopen" : skipped ? "Resume" : "Skip ticket";
                const buttonClass = completed
                    ? "bg-emerald-600 text-white hover:bg-emerald-500"
                    : skipped
                        ? "bg-amber-600 text-white hover:bg-amber-500"
                        : "bg-indigo-600 text-white hover:bg-indigo-500";
                const startLabel = completed ? "Reopen & Start" : requested ? "Starting..." : "Start now";
                const startDisabled = requested ? "disabled" : "";
                const startClass = requested
                    ? "bg-cyan-100 text-cyan-700 cursor-not-allowed"
                    : "bg-cyan-600 text-white hover:bg-cyan-500";
                return `
                    <div class="flex items-start justify-between gap-3 rounded border border-slate-200 bg-white p-2">
                        <div class="min-w-0">
                            <div class="text-xs font-semibold text-slate-700">${ticketId}${stateBadge}${selectedBadge}</div>
                            <div class="text-sm text-slate-900">${title}</div>
                        </div>
                        <div class="shrink-0 flex items-center gap-2">
                            <button type="button"
                                    data-action="start-ticket"
                                    data-step-number="${stepNumber}"
                                    data-ticket-id="${ticketId}"
                                    class="rounded px-3 py-1.5 text-xs font-semibold ${startClass} transition"
                                    ${startDisabled}>
                                ${startLabel}
                            </button>
                            <button type="button"
                                    data-action="${action}"
                                    data-step-number="${stepNumber}"
                                    data-ticket-id="${ticketId}"
                                    class="rounded px-3 py-1.5 text-xs font-semibold ${buttonClass} transition">
                                ${buttonLabel}
                            </button>
                        </div>
                    </div>
                `;
            }).join("");

        return `
            <details class="mb-2 rounded-lg border border-slate-200 bg-slate-50">
                <summary class="cursor-pointer list-none p-3 hover:bg-slate-100 transition">
                    <div class="flex items-start justify-between gap-3">
                        <div class="min-w-0">
                            <div class="text-sm font-semibold text-slate-900 truncate">Step ${stepNumber}: ${fileName}${badge}${currentBadge}</div>
                            <div class="mt-0.5 text-xs text-slate-500">Expand to view tickets</div>
                        </div>
                        <div class="shrink-0 flex items-center gap-2">
                            <button type="button"
                                    data-action="view-step"
                                    data-step-number="${stepNumber}"
                                    class="rounded border border-slate-300 bg-white px-3 py-1.5 text-xs font-semibold text-slate-700 hover:bg-slate-100 transition">
                                View
                            </button>
                            <button type="button"
                                    data-action="skip-step"
                                    data-step-number="${stepNumber}"
                                    class="rounded bg-slate-900 px-3 py-1.5 text-xs font-semibold text-white hover:bg-slate-800 transition">
                                Start Step
                            </button>
                        </div>
                    </div>
                </summary>
                <div class="p-3 pt-0 space-y-2">
                    ${warning}
                    ${ticketRows}
                </div>
            </details>
        `;
    }).join("");

    ui.skipStepsList.innerHTML = html;

    // Expand all details panels in the skip modal
    ui.skipStepsList.querySelectorAll("details").forEach((details) => {
        details.open = true;
    });
}

function normalizeColor(color, fallback = "gray") {
    return STATUS_COLORS[color] ? color : fallback;
}

function formatTime(value) {
    const date = value ? new Date(value) : new Date();
    if (Number.isNaN(date.getTime())) {
        return new Date().toLocaleTimeString();
    }

    return date.toLocaleTimeString();
}

function clearContainers() {
    ui.chatContainer.innerHTML = "";
    ui.terminal.innerHTML = "";
}

function renderEmptyState() {
    ui.chatContainer.innerHTML = `<div class="text-gray-500 italic">Waiting for workflow to start...</div>`;
    ui.terminal.innerHTML = `
        <div class="text-green-400">[SYSTEM] Agentic LLM Web Interface initialized</div>
        <div class="text-gray-400">[SYSTEM] Click 'Start Workflow' to begin</div>
    `;
}

function resetLocalState() {
    state.workflowRunning = false;
    state.busy = false;
    state.awaitingUserInput = false;
    state.canResume = false;
    state.nextStepToRun = 0;
    state.currentStep = 0;
    state.totalSteps = 0;
    state.currentStepName = "Starting...";
    state.statusText = "Ready";
    state.statusColor = "green";
    state.selectedWorkflowTypeId = "";
    state.selectedWorkflowTypeName = "";
    state.resumeWorkflowTypeId = "";
    state.selectedProjectName = "";
    state.selectedProjectDirectory = "";
    state.targetProjectName = "";
    state.showProjectConfigPanel = false;
    state.newWorkflowIntentActive = false;
    state.activeMode = "workflow";
    state.scheduleState = null;
    state.isCurrentStepTicketIteration = false;
    state.ticketHeaderStatus = null;
    state.ticketProgress = null;
}

function applySnapshot(snapshot) {
    if (!snapshot) {
        return;
    }

    state.workflowRunning = Boolean(snapshot.workflowRunning);
    state.busy = Boolean(snapshot.busy);
    state.awaitingUserInput = Boolean(snapshot.awaitingUserInput);
    state.canResume = Boolean(snapshot.canResume);
    state.nextStepToRun = snapshot.nextStepToRun || 0;
    if (typeof snapshot.selectedWorkflowTypeId === "string") {
        // This tracks what the server thinks is the "resume" workflow for the current project.
        state.resumeWorkflowTypeId = snapshot.selectedWorkflowTypeId;
    }
    state.selectedWorkflowTypeId = snapshot.selectedWorkflowTypeId || state.selectedWorkflowTypeId;
    state.selectedWorkflowTypeName = snapshot.selectedWorkflowTypeName || state.selectedWorkflowTypeName;
    state.selectedProjectName = snapshot.selectedProjectName || state.selectedProjectName;
    state.selectedProjectDirectory = snapshot.selectedProjectDirectory || state.selectedProjectDirectory;
    state.targetProjectName = state.selectedProjectName || state.targetProjectName;
    state.isCurrentStepTicketIteration = Boolean(snapshot.isCurrentStepTicketIteration);
    state.ticketHeaderStatus = snapshot.ticketHeaderStatus || null;
    state.currentStep = snapshot.currentStep || 0;
    state.totalSteps = snapshot.totalSteps || 0;
    state.currentStepName = snapshot.currentStepName || state.currentStepName;
    state.ticketProgress = snapshot.ticketProgress || null;
    const hasScheduleState = Object.prototype.hasOwnProperty.call(snapshot, "scheduleState");
    if (hasScheduleState) {
        normalizeScheduleState(snapshot.scheduleState || null);
    }

    if (typeof snapshot.activeMode === "string" && snapshot.activeMode.trim().length > 0) {
        const nextActiveMode = snapshot.activeMode.trim().toLowerCase();
        const isScheduleCurrentlyActive = Boolean(state.scheduleState?.isActive);
        if (nextActiveMode === "schedule" || hasScheduleState || !isScheduleCurrentlyActive) {
            state.activeMode = nextActiveMode;
        }
    }
    state.statusText = snapshot.status || state.statusText;
    state.statusColor = normalizeColor(snapshot.color || state.statusColor, "green");
    const resumableTicketIteration = state.canResume &&
        Boolean(state.ticketProgress?.isTicketIterationStep) &&
        Number(state.ticketProgress?.remainingTickets || 0) > 0;
    if (resumableTicketIteration) {
        state.showProjectConfigPanel = false;
    }

    updateProgress(state.currentStep, state.totalSteps);
    ui.stepName.textContent = state.currentStepName || "Starting...";
    updateStatus(state.statusText, state.statusColor);
    updateBusyState(state.busy);
    updateMetrics(snapshot.metrics);
    syncWorkflowTypePicker();
    syncProjectPicker();

    // Display app version in the header
    const versionEl = document.getElementById("app-version");
    if (versionEl && snapshot.appVersion) {
        versionEl.textContent = "v" + snapshot.appVersion;
    }

    updateActiveModeBadge();
}

function applyHydration(payload) {
    const snapshot = payload?.snapshot || {};
    const hydrationIncludesMode = Object.prototype.hasOwnProperty.call(snapshot, "activeMode") ||
        Object.prototype.hasOwnProperty.call(snapshot, "scheduleState");
    const previousActiveMode = state.activeMode;
    const previousScheduleState = state.scheduleState;

    resetLocalState();
    if (!hydrationIncludesMode) {
        state.activeMode = previousActiveMode || "workflow";
        state.scheduleState = previousScheduleState || null;
    }

    clearContainers();

    const history = Array.isArray(payload?.history) ? payload.history : [];

    if (history.length === 0) {
        renderEmptyState();
    } else {
        history.forEach((message) => {
            handleMessage(message, { replay: true });
        });
    }

    applySnapshot(snapshot);
}

function updateStatus(text, color) {
    state.statusText = text || state.statusText;
    state.statusColor = normalizeColor(color || state.statusColor, "green");

    const dotColor = STATUS_COLORS[state.statusColor] || STATUS_COLORS.gray;
    ui.status.innerHTML = "";

    const dot = document.createElement("span");
    dot.style.display = "inline-block";
    dot.style.width = "0.75rem";
    dot.style.height = "0.75rem";
    dot.style.borderRadius = "9999px";
    dot.style.backgroundColor = dotColor;
    if (state.statusColor === "green") {
        dot.className = "animate-pulse";
    }

    const label = document.createElement("span");
    label.textContent = text;

    ui.status.className = "flex items-center gap-2";
    ui.status.append(dot, label);
}

function updateProgress(current, total) {
    const percentage = total > 0 ? (current / total) * 100 : 0;
    ui.progressBar.style.width = `${percentage}%`;
    const projectLabel = state.selectedProjectName || "(none)";
    const workflowLabel = state.selectedWorkflowTypeName || "(none)";
    const llmModelLabel = state.currentLlmModelName || "(none)";
    const llmServerLabel = state.currentLlmServerName || "(none)";
    const ticketHeader = state.ticketHeaderStatus;
    const showTicketProgress = Boolean(state.isCurrentStepTicketIteration);
    const ticketSummary = showTicketProgress
        ? (ticketHeader?.isAvailable
            ? Number(ticketHeader.remainingTickets || 0) === 0
                ? " completed"
                : ` ${Number(ticketHeader.remainingTickets|| 0)} Remaining`
            : "pending")
        : "";
    ui.stepCounter.innerHTML = `
        <span class="text-gray-400">Project:</span>
        <span class="font-semibold text-white">${escapeHtml(projectLabel)}</span>
        <span class="text-gray-500 mx-1">•</span>
        <span class="text-gray-400">Workflow:</span>
        <span class="font-semibold text-cyan-300">${escapeHtml(workflowLabel)}</span>
        <span class="text-gray-500 mx-1">•</span>
        <span class="text-gray-400">Step:</span>
        <span class="font-semibold text-amber-300">${current} of ${total}</span>
        ${showTicketProgress ? `
            <span class="text-gray-500 mx-1">•</span>
            <span class="text-gray-400">Tickets:</span>
            <span class="font-semibold text-fuchsia-300">${escapeHtml(ticketSummary)}</span>
            <span class="text-gray-500 mx-1">•</span>
            <span class="text-gray-400">LLM:</span>
            <span class="font-semibold text-emerald-300">${escapeHtml(llmModelLabel)}</span>
            <span class="text-gray-500">@</span>
            <span class="font-semibold text-emerald-200">${escapeHtml(llmServerLabel)}</span>
        ` : `
            <span class="text-gray-500 mx-1">•</span>
            <span class="text-gray-400">LLM:</span>
            <span class="font-semibold text-emerald-300">${escapeHtml(llmModelLabel)}</span>
            <span class="text-gray-500">@</span>
            <span class="font-semibold text-emerald-200">${escapeHtml(llmServerLabel)}</span>
        `}
    `;
}

function syncStepPanelVisibility() {
    const showStepPanel = state.workflowRunning || state.canResume || state.currentStep > 0 || state.totalSteps > 0;
    ui.stepPanel.classList.toggle("hidden", !showStepPanel);
}

function escapeHtml(value) {
    return (value || "")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll("\"", "&quot;")
        .replaceAll("'", "&#39;");
}

function setRequiredFieldValidation(projectInvalid, workflowInvalid) {
    ui.projectSelect.classList.toggle("border-red-500", projectInvalid);
    ui.projectSelect.classList.toggle("ring-2", projectInvalid);
    ui.projectSelect.classList.toggle("ring-red-500", projectInvalid);

    ui.workflowTypeSelect.classList.toggle("border-red-500", workflowInvalid);
    ui.workflowTypeSelect.classList.toggle("ring-2", workflowInvalid);
    ui.workflowTypeSelect.classList.toggle("ring-red-500", workflowInvalid);
}

function getIndexUiMode(isBusy) {
    const scheduleModeActive = state.activeMode === "schedule" || Boolean(state.scheduleState?.isActive);
    const hasWorkflowState = state.workflowRunning || state.canResume || state.currentStep > 0 || state.totalSteps > 0;
    const hasSelectedProjectForNewRun = Boolean((state.selectedProjectName || state.targetProjectName || "").trim());
    const hasSelectedWorkflowTypeForNewRun = Boolean(state.selectedWorkflowTypeId);
    const missingStartSelection = !hasSelectedProjectForNewRun || !hasSelectedWorkflowTypeForNewRun;

    if (state.wsDisconnected) {
        return "websocketDisconnected";
    }

    if (scheduleModeActive) {
        if (state.awaitingUserInput || (state.canResume && !state.workflowRunning && !isBusy)) {
            return "scheduleWaitingForInput";
        }

        if (state.workflowRunning || isBusy || hasWorkflowState) {
            return "scheduleRunning";
        }

        return "scheduleWaiting";
    }

    // New Workflow setup must override paused/waiting states so Start Workflow
    // is available after the user explicitly enters setup mode.
    if (state.showProjectConfigPanel) {
        return "newWorkflowSetup";
    }

    // Waiting-for-input must override workflowRunning because status updates keep
    // workflowRunning=true while paused for user feedback.
    if (state.awaitingUserInput) {
        return "waitingForInput";
    }

    if (state.workflowRunning && isBusy) {
        return "workflowProcessing";
    }

    if (state.workflowRunning) {
        return "workflowRunning";
    }

    if (state.canResume || (!state.workflowRunning && state.currentStep > 0)) {
        return "pausedResumable";
    }

    if (state.llmHealthy === false) {
        return "llmSetupRequired";
    }

    if (missingStartSelection) {
        return "projectSelection";
    }

    return "idle";
}

function setButtonVisibility(button, visible, enabled = true) {
    if (!button) {
        return;
    }

    button.classList.toggle("hidden", !visible);
    button.disabled = !visible || !enabled;
}

function updateBusyState(isBusy) {
    const isNewProjectMode = state.showProjectConfigPanel;
    const hasSelectedProjectForNewRun = Boolean((state.selectedProjectName || state.targetProjectName || "").trim());
    const hasSelectedWorkflowTypeForNewRun = Boolean(state.selectedWorkflowTypeId);
    const isLlmHealthy = state.llmHealthy !== false;
    const hasNextStep = state.totalSteps > 0 && state.currentStep > 0 && state.currentStep < state.totalSteps;
    const ticketRemaining = Number(
        state.ticketProgress?.remainingTickets ??
        state.ticketHeaderStatus?.remainingTickets ??
        0
    );
    const isTicketIterationContext = Boolean(
        state.ticketProgress?.isTicketIterationStep ||
        state.ticketHeaderStatus?.isTicketIterationStep ||
        state.isCurrentStepTicketIteration
    );
    const hasTicketContinuation = isTicketIterationContext && ticketRemaining > 0;
    const canContinue = !isBusy && (hasNextStep || hasTicketContinuation) && (state.awaitingUserInput || state.canResume);
    const hasWorkflowState = state.workflowRunning || state.canResume || state.currentStep > 0 || state.totalSteps > 0;
    const hasWorkflowTypes = state.workflowTypes.length > 0;
    const hasWorkflowSelection = !hasWorkflowTypes || Boolean(state.selectedWorkflowTypeId);
    const uiMode = getIndexUiMode(isBusy);
    const isDisconnectedMode = uiMode === "websocketDisconnected";
    const isScheduleModeState = uiMode === "scheduleWaiting" || uiMode === "scheduleRunning" || uiMode === "scheduleWaitingForInput";
    const isProcessingMode = uiMode === "workflowProcessing" || uiMode === "workflowRunning" || uiMode === "scheduleRunning";
    const isConversationMode = uiMode === "waitingForInput" || uiMode === "pausedResumable" || uiMode === "scheduleWaitingForInput";
    const showStartButton = uiMode === "idle" ||
        uiMode === "projectSelection" ||
        uiMode === "newWorkflowSetup" ||
        uiMode === "pausedResumable" ||
        uiMode === "scheduleWaiting" ||
        uiMode === "scheduleWaitingForInput" ||
        uiMode === "llmSetupRequired";
    const enableStartButton = showStartButton && !isBusy;
    const showContinueButton = isConversationMode && hasWorkflowState && (hasNextStep || hasTicketContinuation) && canContinue;
    const showSkipButton = isConversationMode && hasWorkflowState;
    const canSkip = showSkipButton && !isBusy;
    const showSendUpload = !isDisconnectedMode && uiMode !== "llmSetupRequired";
    const disableSendUpload = uiMode === "workflowRunning" || uiMode === "scheduleRunning";
    const canSendMessage = showSendUpload && !disableSendUpload;
    const shouldHideConfigByWorkflow = state.workflowRunning || state.canResume || hasTicketContinuation;
    const hideConfigPanel = shouldHideConfigByWorkflow && !state.showProjectConfigPanel;
    const projectSelectionDisabled = ((state.workflowRunning || state.canResume) && !isNewProjectMode) || state.busy;
    const projectCreateDisabled = (state.workflowRunning && !isNewProjectMode) || state.busy;
    const hasProjectOptions = state.projects.length > 0;
    const canSwitchProject = hasProjectOptions && !isBusy && (
        uiMode === "idle" ||
        uiMode === "projectSelection" ||
        uiMode === "waitingForInput" ||
        uiMode === "pausedResumable" ||
        uiMode === "scheduleWaiting"
    );
    const showSwitchProject = hasProjectOptions && (
        uiMode === "idle" ||
        uiMode === "projectSelection" ||
        uiMode === "waitingForInput" ||
        uiMode === "pausedResumable" ||
        uiMode === "scheduleWaiting"
    );
    const showScheduleButton = !isDisconnectedMode;
    const canOpenSchedule = showScheduleButton && !isBusy;
    const showNewOpenButton = !isDisconnectedMode;
    const canOpenNew = showNewOpenButton && !isBusy;
    const showResetButton = uiMode === "waitingForInput" ||
        uiMode === "pausedResumable" ||
        uiMode === "scheduleWaitingForInput";
    const canReset = showResetButton && !isBusy;
    const showStopButton = isProcessingMode;
    const canStop = showStopButton && state.workflowRunning;
    const showStopAllButton = isProcessingMode && (isScheduleModeState || Number(state.totalSteps || 0) > 1);
    const canStopAll = showStopAllButton && state.workflowRunning;

    const faviconLink = document.getElementById("favicon-link");
    if (faviconLink) {
        faviconLink.href = isBusy ? FAVICON_BUSY_SVG : FAVICON_READY_SVG;
    }

    if (isBusy) {
        ui.busyIndicator.classList.remove("hidden");
    } else {
        ui.busyIndicator.classList.add("hidden");
    }

    ui.userInput.disabled = !canSendMessage;
    setButtonVisibility(ui.sendBtn, showSendUpload, canSendMessage);
    setButtonVisibility(ui.uploadBtn, showSendUpload, canSendMessage);
    setButtonVisibility(ui.continueBtn, showContinueButton, canContinue);
    setButtonVisibility(ui.skipBtn, showSkipButton, canSkip);
    setButtonVisibility(ui.stopBtn, showStopButton, canStop);
    setButtonVisibility(ui.stopScheduleTaskBtn, showStopAllButton, canStopAll);
    setButtonVisibility(ui.resetBtn, showResetButton, canReset);
    setButtonVisibility(ui.newOpenBtn, showNewOpenButton, canOpenNew);
    setButtonVisibility(ui.startBtn, showStartButton, enableStartButton);
    ui.startBtn.textContent = "Start Workflow";
    if (uiMode === "llmSetupRequired") {
        ui.startBtn.title = "LLM server setup is required before starting.";
    } else if (isNewProjectMode || uiMode === "projectSelection") {
        if (!hasSelectedProjectForNewRun || !hasSelectedWorkflowTypeForNewRun) {
            ui.startBtn.title = "Select Project and Workflow Type to start.";
        } else if (!isLlmHealthy) {
            ui.startBtn.title = "LLM server setup is required before starting.";
        } else {
            ui.startBtn.title = "";
        }
    } else {
        ui.startBtn.title = !isLlmHealthy ? "LLM server setup is required before starting." : "";
    }
    ui.continueBtn.textContent = hasTicketContinuation ? "Next Ticket" : "Next Step";
    if (ui.skipBtn) {
        ui.skipBtn.textContent = hasTicketContinuation ? "Skip Ticket" : "Skip Step";
    }
    if (ui.viewCurrentStepBtn) {
        ui.viewCurrentStepBtn.disabled = !(state.currentStep > 0);
    }
    if (ui.viewCurrentTicketBtn) {
        const hasCurrentTicket = Boolean(
            state.isCurrentStepTicketIteration &&
            (state.ticketHeaderStatus?.currentTicketId || state.ticketProgress?.ticketId)
        );
        ui.viewCurrentTicketBtn.disabled = !hasCurrentTicket;
        ui.viewCurrentTicketBtn.classList.toggle("hidden", !state.isCurrentStepTicketIteration);
    }
    ui.workflowConfigPanel.classList.toggle("hidden", hideConfigPanel);
    syncStepPanelVisibility();
    ui.workflowTypeSelect.disabled = ((state.workflowRunning || state.canResume) && !isNewProjectMode) || state.busy;
    ui.projectSelect.disabled = projectSelectionDisabled;
    if (ui.createProjectOpenBtn) {
        ui.createProjectOpenBtn.disabled = projectCreateDisabled;
    }
    if (ui.projectNameInput) {
        ui.projectNameInput.disabled = projectCreateDisabled;
    }
    if (ui.createProjectBtn) {
        ui.createProjectBtn.disabled = projectCreateDisabled;
    }
    if (ui.newProjectBtn) {
        ui.newProjectBtn.disabled = isBusy;
    }
    if (ui.scheduleBtn) {
        ui.scheduleBtn.disabled = !canOpenSchedule;
    }
    if (ui.switchProjectBtn) {
        ui.switchProjectBtn.disabled = !canSwitchProject;
    }
    ui.llmServerSelect.disabled = state.busy;
    ui.llmModelInput.disabled = state.busy;
    if (hideConfigPanel) {
        setRequiredFieldValidation(false, false);
    }
    if (ui.scheduleStopBtn) {
        ui.scheduleStopBtn.disabled = !state.scheduleState?.isActive || isBusy;
    }
    if (ui.manageWorkflowTypesBtn) {
        ui.manageWorkflowTypesBtn.disabled = isBusy;
    }

    syncWorkflowTypeHelp(hasWorkflowTypes, hasWorkflowSelection);
    syncProjectHelp();
    syncWorkflowEditorControls();
}

function syncWorkflowTypeHelp(hasWorkflowTypes = state.workflowTypes.length > 0, hasWorkflowSelection = !hasWorkflowTypes || Boolean(state.selectedWorkflowTypeId)) {
    if (!hasWorkflowTypes) {
        ui.workflowTypeHelp.textContent = "No workflow templates were found. The server will use the configured steps directory.";
        return;
    }

    if (state.canResume && !state.showProjectConfigPanel) {
        ui.workflowTypeHelp.textContent = state.selectedWorkflowTypeName
            ? `Resuming the saved "${state.selectedWorkflowTypeName}" workflow.`
            : "Resuming the saved workflow.";
        return;
    }

    ui.workflowTypeHelp.textContent = hasWorkflowSelection
        ? "Select the workflow template to run before starting."
        : "Choose a workflow type before starting.";
}

function syncWorkflowTypePicker() {
    const select = ui.workflowTypeSelect;
    const existingValue = state.selectedWorkflowTypeId || select.value;
    select.innerHTML = "";

    if (state.workflowTypes.length === 0) {
        const option = document.createElement("option");
        option.value = "";
        option.textContent = "Use configured steps directory";
        select.appendChild(option);
        select.value = "";
        syncWorkflowTypeHelp(false, true);
        return;
    }

    const placeholder = document.createElement("option");
    placeholder.value = "";
    placeholder.textContent = "Select a workflow type";
    select.appendChild(placeholder);

    state.workflowTypes.forEach((workflowType) => {
        const option = document.createElement("option");
        option.value = workflowType.id;
        option.textContent = `${workflowType.name} (${workflowType.stepCount} step${workflowType.stepCount === 1 ? "" : "s"})`;
        select.appendChild(option);
    });

    const selected = state.workflowTypes.some((workflowType) => workflowType.id === existingValue)
        ? existingValue
        : "";

    state.selectedWorkflowTypeId = selected;
    state.selectedWorkflowTypeName = state.workflowTypes.find((workflowType) => workflowType.id === selected)?.name || state.selectedWorkflowTypeName || "";
    select.value = selected;
    syncWorkflowTypeHelp(true, Boolean(selected));
}

function syncProjectHelp() {
    if (state.projects.length === 0) {
        ui.projectHelp.textContent = "Project is required. No projects found yet, create one using letters and numbers only.";
        return;
    }

    if (state.selectedProjectName && state.targetProjectName && state.targetProjectName !== state.selectedProjectName) {
        ui.projectHelp.textContent = `Active project: ${state.selectedProjectName}. Target project: ${state.targetProjectName}. Click Switch Project to load it.`;
        return;
    }

    if (state.selectedProjectName) {
        ui.projectHelp.textContent = `Active project: ${state.selectedProjectName}`;
        return;
    }

    ui.projectHelp.textContent = "Project is required. Select a target project and click Switch Project.";
}

function syncProjectPicker() {
    const select = ui.projectSelect;
    const existingValue = state.targetProjectName || state.selectedProjectName || select.value;
    select.innerHTML = "";

    if (state.projects.length === 0) {
        const option = document.createElement("option");
        option.value = "";
        option.textContent = "No projects available";
        select.appendChild(option);
        select.value = "";
        syncProjectHelp();
        return;
    }

    const placeholder = document.createElement("option");
    placeholder.value = "";
    placeholder.textContent = "Select a project";
    select.appendChild(placeholder);

    state.projects.forEach((project) => {
        const option = document.createElement("option");
        option.value = project.name;
        option.textContent = project.name;
        select.appendChild(option);
    });

    const selected = state.projects.some((project) => project.name === existingValue)
        ? existingValue
        : "";

    state.targetProjectName = selected;
    select.value = selected;
    syncProjectHelp();
}

function applyWorkflowTypes(payload) {
    state.workflowTypes = Array.isArray(payload?.workflowTypes) ? payload.workflowTypes : [];
    if (!state.workflowTypes.some((workflowType) => workflowType.id === state.selectedWorkflowTypeId)) {
        state.selectedWorkflowTypeId = "";
        state.selectedWorkflowTypeName = "";
    }

    if (!state.workflowTypes.some((workflowType) => workflowType.id === state.workflowEditorSelectedWorkflowId)) {
        state.workflowEditorSelectedWorkflowId = "";
        state.workflowEditorSelectedWorkflowName = "";
        state.workflowEditorSteps = [];
        state.workflowEditorSelectedStepFileName = "";
        state.workflowEditorMarkdownDraft = "";
        state.workflowEditorMetadataDraft = "";
        state.workflowEditorHasTicketIteration = false;
        if (ui.workflowEditorMarkdown) {
            ui.workflowEditorMarkdown.value = "";
        }
        if (ui.workflowEditorMetadata) {
            ui.workflowEditorMetadata.value = "";
        }
        setWorkflowEditorDirty(false);
    } else {
        state.workflowEditorSelectedWorkflowName = state.workflowTypes.find((workflowType) => workflowType.id === state.workflowEditorSelectedWorkflowId)?.name
            || state.workflowEditorSelectedWorkflowName;
    }

    syncWorkflowTypePicker();
    if (state.workflowEditorOpen) {
        renderWorkflowEditorWorkflowList();
        renderWorkflowEditorStepList();
        syncWorkflowEditorControls();
    }
    updateBusyState(state.busy);
}

function applyProjects(payload) {
    state.projects = Array.isArray(payload?.projects) ? payload.projects : [];
    const scheduleModeActive = state.activeMode === "schedule" || Boolean(state.scheduleState?.isActive);
    const shouldPreserveActiveScheduleProject = scheduleModeActive && (state.workflowRunning || state.currentStep > 0 || state.totalSteps > 0);

    if (!shouldPreserveActiveScheduleProject && !state.projects.some((project) => project.name === state.selectedProjectName)) {
        state.selectedProjectName = "";
        state.selectedProjectDirectory = "";
    }

    if (!shouldPreserveActiveScheduleProject && !state.projects.some((project) => project.name === state.targetProjectName)) {
        state.targetProjectName = state.selectedProjectName || "";
    }

    syncProjectPicker();
    updateBusyState(state.busy);
}

function formatProjectStepSummary(project) {
    const currentStep = Number(project?.currentStep || 0);
    const totalSteps = Number(project?.totalSteps || 0);
    if (currentStep > 0 && totalSteps > 0) {
        return `Step ${currentStep} of ${totalSteps}`;
    }

    if (totalSteps > 0) {
        return `Step 0 of ${totalSteps}`;
    }

    return "Not started";
}

function formatProjectUpdatedTime(value) {
    if (!value) {
        return "Never";
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
        return "Unknown";
    }

    return date.toLocaleString();
}

function closeSwitchProjectModal() {
    if (!ui.switchProjectModal) {
        return;
    }

    ui.switchProjectModal.classList.add("hidden");
    ui.switchProjectModal.classList.remove("flex");
}

function openStartNewModal() {
    if (!ui.startNewModal) {
        return;
    }

    ui.startNewModal.classList.remove("hidden");
    ui.startNewModal.classList.add("flex");
}

function closeStartNewModal() {
    if (!ui.startNewModal) {
        return;
    }

    ui.startNewModal.classList.add("hidden");
    ui.startNewModal.classList.remove("flex");
}

function openNewWorkflowModal() {
    if (!ui.newWorkflowModal) {
        return;
    }

    state.showProjectConfigPanel = true;
    state.newWorkflowIntentActive = true;
    ui.newWorkflowModal.classList.remove("hidden");
    ui.newWorkflowModal.classList.add("flex");
    updateBusyState(state.busy);
}

function closeNewWorkflowModal() {
    if (!ui.newWorkflowModal) {
        return;
    }

    state.showProjectConfigPanel = false;
    state.newWorkflowIntentActive = false;
    ui.newWorkflowModal.classList.add("hidden");
    ui.newWorkflowModal.classList.remove("flex");
    updateBusyState(state.busy);
}

function openSwitchProjectModal() {
    if (!ui.switchProjectModal) {
        return;
    }

    ui.switchProjectModal.classList.remove("hidden");
    ui.switchProjectModal.classList.add("flex");
}

async function loadSwitchProjectModal() {
    if (!ui.switchProjectList) {
        return;
    }

    openSwitchProjectModal();
    ui.switchProjectList.innerHTML = `
        <div class="rounded border border-gray-200 bg-gray-50 p-3 text-sm text-gray-600">
            Loading project states...
        </div>
    `;

    try {
        const payload = await apiClient.getProjectSwitchOptions();
        const projects = Array.isArray(payload?.projects) ? payload.projects : [];

        if (projects.length === 0) {
            ui.switchProjectList.innerHTML = `
                <div class="rounded border border-gray-200 bg-gray-50 p-3 text-sm text-gray-600">
                    No projects found.
                </div>
            `;
            return;
        }

        ui.switchProjectList.innerHTML = projects.map((project) => {
            const isActive = Boolean(project.isActive);
            const status = escapeHtml(project.status || "Ready");
            const stepSummary = escapeHtml(formatProjectStepSummary(project));
            const stepName = escapeHtml(project.currentStepName || "Not started");
            const updated = escapeHtml(formatProjectUpdatedTime(project.lastUpdatedUtc));
            const projectName = escapeHtml(project.projectName || "");

            return `
                <div class="mb-2 rounded-lg border border-gray-200 bg-white p-3">
                    <div class="flex items-start justify-between gap-3">
                        <div class="min-w-0">
                            <div class="flex items-center gap-2">
                                <h4 class="text-sm font-semibold text-gray-900">${projectName}</h4>
                                ${isActive ? `<span class="rounded bg-emerald-100 px-2 py-0.5 text-xs font-semibold text-emerald-800">Active</span>` : ""}
                            </div>
                            <p class="mt-1 text-xs text-gray-600">${stepSummary} • ${stepName}</p>
                            <p class="mt-1 text-xs text-gray-500">Status: ${status} • Updated: ${updated}</p>
                        </div>
                        <button
                            type="button"
                            class="switch-project-row-btn rounded px-3 py-1.5 text-xs font-semibold transition ${isActive ? "bg-gray-200 text-gray-500 cursor-default" : "bg-cyan-600 text-white hover:bg-cyan-500"}"
                            data-project-name="${projectName}"
                            ${isActive ? "disabled" : ""}>
                            ${isActive ? "Current" : "Switch"}
                        </button>
                    </div>
                </div>
            `;
        }).join("");

        const switchButtons = ui.switchProjectList.querySelectorAll(".switch-project-row-btn");
        switchButtons.forEach((button) => {
            button.addEventListener("click", async () => {
                const projectName = button.getAttribute("data-project-name") || "";
                if (!projectName) {
                    return;
                }

                const switched = await selectProject(projectName);
                if (switched) {
                    closeSwitchProjectModal();
                }
            });
        });
    } catch (error) {
        ui.switchProjectList.innerHTML = `
            <div class="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-700">
                Failed to load project states: ${escapeHtml(error.message || "Unknown error")}
            </div>
        `;
    }
}

function syncLlmHeader() {
    updateProgress(state.currentStep, state.totalSteps);
}

function getSelectedServer() {
    return state.llmServers.find((server) => server.id === state.activeLlmServerId) || null;
}

function syncLlmSelectors() {
    const serverSelect = ui.llmServerSelect;
    const previousServerId = state.activeLlmServerId || serverSelect.value;

    serverSelect.innerHTML = "";
    // Clear the autocomplete dropdown
    ui.llmModelDropdown.innerHTML = "";
    ui.llmModelDropdown.classList.add("hidden");

    if (state.llmServers.length === 0) {
        const emptyServer = document.createElement("option");
        emptyServer.value = "";
        emptyServer.textContent = "No LLM servers";
        serverSelect.appendChild(emptyServer);
        serverSelect.value = "";

        ui.llmModelInput.value = "";
        state.activeLlmServerId = "";
        state.activeLlmModelId = "";
        state.currentLlmModelName = "";
        state.currentLlmServerName = "";
        syncLlmHeader();
        return;
    }

    state.llmServers.forEach((server) => {
        const option = document.createElement("option");
        option.value = server.id;
        option.textContent = server.name;
        serverSelect.appendChild(option);
    });

    const selectedServer = state.llmServers.some((server) => server.id === previousServerId)
        ? state.llmServers.find((server) => server.id === previousServerId)
        : state.llmServers[0];

    state.activeLlmServerId = selectedServer?.id || "";
    serverSelect.value = state.activeLlmServerId;

    const models = Array.isArray(selectedServer?.models) ? selectedServer.models : [];
    // Store models for client-side filtering
    state.activeServerModels = models;

    const selectedModel = models.some((model) => model.id === state.activeLlmModelId)
        ? models.find((model) => model.id === state.activeLlmModelId)
        : models.find((model) => model.id === selectedServer.defaultModelId) || models[0];

    state.activeLlmModelId = selectedModel?.id || "";
    // Update the input field with the selected model name
    ui.llmModelInput.value = selectedModel?.name || "";
    state.currentLlmModelName = selectedModel?.name || state.currentLlmModelName || "";
    state.currentLlmServerName = selectedServer?.name || state.currentLlmServerName || "";
    syncLlmHeader();
}

function applyLlmOptions(payload) {
    state.llmServers = Array.isArray(payload?.servers) ? payload.servers : [];
    state.activeLlmServerId = payload?.activeServerId || state.activeLlmServerId;
    state.activeLlmModelId = payload?.activeModelId || state.activeLlmModelId;
    state.currentLlmModelName = payload?.currentModelName || state.currentLlmModelName;
    state.currentLlmServerName = payload?.currentServerName || state.currentLlmServerName;
    syncLlmSelectors();
    updateBusyState(state.busy);
}

function renderAgentMarkdown(content) {
    const text = content || "";
    const markedLib = window.marked;
    const purify = window.DOMPurify;

    if (!markedLib || !purify) {
        return null;
    }

    const tokens = markedLib.lexer(text, { breaks: true, gfm: true });
    const hadRawHtml = neutralizeRawHtmlTokens(tokens);
    const markdownHtml = markedLib.parser(tokens, { breaks: true, gfm: true });
    const sanitizedHtml = purify.sanitize(markdownHtml, {
        USE_PROFILES: { html: true },
        ALLOWED_ATTR: ["href", "target", "rel", "title", "class"]
    });
    return {
        html: sanitizedHtml,
        hadRawHtml
    };
}

function neutralizeRawHtmlTokens(tokens) {
    let foundRawHtml = false;

    if (!Array.isArray(tokens)) {
        return foundRawHtml;
    }

    for (const token of tokens) {
        if (!token || typeof token !== "object") {
            continue;
        }

        if (token.type === "html") {
            const rawHtml = token.raw || token.text || "";
            token.type = "text";
            token.text = escapeHtml(rawHtml);
            token.raw = token.text;
            foundRawHtml = true;
        }

        if (Array.isArray(token.tokens)) {
            foundRawHtml = neutralizeRawHtmlTokens(token.tokens) || foundRawHtml;
        }
    }

    return foundRawHtml;
}

function tryParseStructuredJson(content) {
    if (typeof content !== "string") {
        return null;
    }

    let candidate = content.trim();
    if (!candidate) {
        return null;
    }

    // Handle fenced JSON blocks from the model, e.g. ```json ... ```
    const fencedMatch = candidate.match(/^```(?:json)?\s*([\s\S]*?)\s*```$/i);
    if (fencedMatch && fencedMatch[1]) {
        candidate = fencedMatch[1].trim();
    }

    try {
        const parsed = JSON.parse(candidate);
        if (parsed && typeof parsed === "object") {
            return parsed;
        }
    } catch {
        // Not structured JSON.
    }

    return null;
}

function addChatMessage(role, content, timestamp, source = "web", sourceLabel = "") {
    if (ui.chatContainer.firstElementChild?.classList?.contains("text-gray-500")) {
        ui.chatContainer.innerHTML = "";
    }

    const isTelegram = role === "user" && source === "telegram";
    const theme = role === "user"
        ? (isTelegram ? CHAT_THEME.telegram : CHAT_THEME.user)
        : CHAT_THEME.agent;
    const card = document.createElement("div");
    card.className = "log-entry";
    card.style.padding = "0.75rem";
    card.style.borderRadius = "0.5rem";
    card.style.maxWidth = "96%";
    card.style.backgroundColor = theme.background;
    card.style.alignSelf = theme.align;

    const roleLabel = document.createElement("div");
    roleLabel.style.fontSize = "0.75rem";
    roleLabel.style.fontWeight = "600";
    roleLabel.style.marginBottom = "0.25rem";
    roleLabel.style.color = theme.label;
    const labelText = role === "user"
        ? (isTelegram ? sourceLabel || "Telegram" : "You")
        : "Agent";
    roleLabel.textContent = `${labelText} • ${formatTime(timestamp)}`;

    const contentDiv = document.createElement("div");
    contentDiv.style.color = "#1f2937";
    const markdownRender = role === "agent" ? renderAgentMarkdown(content) : null;
    if (markdownRender?.html) {
        contentDiv.className = "markdown-content";
        contentDiv.innerHTML = markdownRender.html;

        if (markdownRender.hadRawHtml) {
            const marker = document.createElement("div");
            marker.style.marginTop = "0.5rem";
            marker.style.fontSize = "0.75rem";
            marker.style.fontWeight = "600";
            marker.style.color = "#92400e";
            marker.textContent = "⚠ HTML content was neutralized for safety.";
            contentDiv.appendChild(marker);
        }
    } else {
        contentDiv.style.whiteSpace = "pre-wrap";
        contentDiv.style.overflowWrap = "anywhere";
        contentDiv.style.wordBreak = "break-word";
        contentDiv.textContent = content;
    }

    card.append(roleLabel, contentDiv);
    ui.chatContainer.appendChild(card);
    ui.chatContainer.scrollTop = ui.chatContainer.scrollHeight;
}

function renderSelectedFiles(files) {
    if (!files || files.length === 0) {
        ui.selectedFiles.classList.add("hidden");
        ui.selectedFiles.textContent = "";
        return;
    }

    const names = Array.from(files).map((file) => file.name).join(", ");
    ui.selectedFiles.textContent = `Selected: ${names}`;
    ui.selectedFiles.classList.remove("hidden");
}

function addToolExecutionMessage(toolName, success, output, timestamp, requestSummary = "") {
    if (ui.chatContainer.firstElementChild?.classList?.contains("text-gray-500")) {
        ui.chatContainer.innerHTML = "";
    }

    const wrapper = document.createElement("div");
    wrapper.className = "log-entry";
    wrapper.style.alignSelf = CHAT_THEME.tool.align;
    wrapper.style.maxWidth = "98%";

    const details = document.createElement("details");
    details.style.backgroundColor = CHAT_THEME.tool.background;
    details.style.border = `1px solid ${CHAT_THEME.tool.border}`;
    details.style.borderRadius = "0.5rem";
    details.style.padding = "0.75rem";

    const summary = document.createElement("summary");
    summary.style.cursor = "pointer";
    summary.style.fontWeight = "600";
    summary.style.color = CHAT_THEME.tool.label;
    summary.textContent = `Tool • ${toolName || "Unknown"} • ${success ? "Success" : "Failed"} • ${formatTime(timestamp)}`;

    const body = document.createElement("div");
    body.style.marginTop = "0.75rem";
    body.style.color = "#1f2937";
    body.style.whiteSpace = "pre-wrap";
    body.style.overflowWrap = "anywhere";
    body.style.wordBreak = "break-word";

    const sections = [];
    if (requestSummary) {
        sections.push(`Request\n${requestSummary}`);
    }
    sections.push(`${success ? "Result" : "Error"}\n${output || "(no output)"}`);
    body.textContent = sections.join("\n\n");

    details.append(summary, body);
    wrapper.appendChild(details);
    ui.chatContainer.appendChild(wrapper);
    ui.chatContainer.scrollTop = ui.chatContainer.scrollHeight;
}

function addStructuredJsonMessage(content, timestamp) {
    if (ui.chatContainer.firstElementChild?.classList?.contains("text-gray-500")) {
        ui.chatContainer.innerHTML = "";
    }

    const wrapper = document.createElement("div");
    wrapper.className = "log-entry";
    wrapper.style.alignSelf = CHAT_THEME.tool.align;
    wrapper.style.maxWidth = "96%";

    const details = document.createElement("details");
    details.style.backgroundColor = CHAT_THEME.tool.background;
    details.style.border = `1px solid ${CHAT_THEME.tool.border}`;
    details.style.borderRadius = "0.5rem";
    details.style.padding = "0.75rem";

    const summary = document.createElement("summary");
    summary.style.cursor = "pointer";
    summary.style.fontWeight = "600";
    summary.style.color = CHAT_THEME.tool.label;
    summary.textContent = `Agent JSON Output • ${formatTime(timestamp)}`;

    const body = document.createElement("div");
    body.style.marginTop = "0.75rem";
    body.style.color = "#1f2937";
    body.style.whiteSpace = "pre-wrap";
    body.style.overflowWrap = "anywhere";
    body.style.wordBreak = "break-word";
    body.textContent = content || "(empty json)";

    details.append(summary, body);
    wrapper.appendChild(details);
    ui.chatContainer.appendChild(wrapper);
    ui.chatContainer.scrollTop = ui.chatContainer.scrollHeight;
}

function addReasoningMessage(content, timestamp) {
    if (ui.chatContainer.firstElementChild?.classList?.contains("text-gray-500")) {
        ui.chatContainer.innerHTML = "";
    }

    const wrapper = document.createElement("div");
    wrapper.className = "log-entry";
    wrapper.style.alignSelf = CHAT_THEME.tool.align;
    wrapper.style.maxWidth = "96%";

    const details = document.createElement("details");
    details.style.backgroundColor = "#f8fafc";
    details.style.border = "1px solid #cbd5e1";
    details.style.borderRadius = "0.5rem";
    details.style.padding = "0.75rem";

    const summary = document.createElement("summary");
    summary.style.cursor = "pointer";
    summary.style.fontWeight = "600";
    summary.style.color = "#334155";
    summary.textContent = `Agent Reasoning • ${formatTime(timestamp)}`;

    const body = document.createElement("div");
    body.style.marginTop = "0.75rem";
    body.style.color = "#1f2937";
    body.style.whiteSpace = "pre-wrap";
    body.style.overflowWrap = "anywhere";
    body.style.wordBreak = "break-word";
    body.textContent = content || "(no reasoning content)";

    details.append(summary, body);
    wrapper.appendChild(details);
    ui.chatContainer.appendChild(wrapper);
    ui.chatContainer.scrollTop = ui.chatContainer.scrollHeight;
}

function addToolRequestMessage(toolName, requestSummary, timestamp) {
    if (ui.chatContainer.firstElementChild?.classList?.contains("text-gray-500")) {
        ui.chatContainer.innerHTML = "";
    }

    const wrapper = document.createElement("div");
    wrapper.className = "log-entry";
    wrapper.style.alignSelf = CHAT_THEME.tool.align;
    wrapper.style.maxWidth = "98%";

    const details = document.createElement("details");
    details.style.backgroundColor = "#ecfeff";
    details.style.border = "1px solid #67e8f9";
    details.style.borderRadius = "0.5rem";
    details.style.padding = "0.75rem";

    const summary = document.createElement("summary");
    summary.style.cursor = "pointer";
    summary.style.fontWeight = "600";
    summary.style.color = "#155e75";
    summary.textContent = `Tool • ${toolName || "Unknown"} • Requested • ${formatTime(timestamp)}`;

    const body = document.createElement("div");
    body.style.marginTop = "0.75rem";
    body.style.color = "#1f2937";
    body.style.whiteSpace = "pre-wrap";
    body.style.overflowWrap = "anywhere";
    body.style.wordBreak = "break-word";
    body.textContent = requestSummary || "(no request details)";

    details.append(summary, body);
    wrapper.appendChild(details);
    ui.chatContainer.appendChild(wrapper);
    ui.chatContainer.scrollTop = ui.chatContainer.scrollHeight;
}

function addLoadedStepMessage(stepName, content, timestamp) {
    if (ui.chatContainer.firstElementChild?.classList?.contains("text-gray-500")) {
        ui.chatContainer.innerHTML = "";
    }

    const wrapper = document.createElement("div");
    wrapper.className = "log-entry";
    wrapper.style.alignSelf = CHAT_THEME.tool.align;
    wrapper.style.maxWidth = "98%";

    const details = document.createElement("details");
    details.style.backgroundColor = CHAT_THEME.tool.background;
    details.style.border = `1px solid ${CHAT_THEME.tool.border}`;
    details.style.borderRadius = "0.5rem";
    details.style.padding = "0.75rem";

    const summary = document.createElement("summary");
    summary.style.cursor = "pointer";
    summary.style.fontWeight = "600";
    summary.style.color = CHAT_THEME.tool.label;
    summary.textContent = `Loaded Step • ${stepName || "Unknown"} • ${formatTime(timestamp)}`;

    const body = document.createElement("div");
    body.style.marginTop = "0.75rem";
    body.style.color = "#1f2937";
    body.style.whiteSpace = "pre-wrap";
    body.style.overflowWrap = "anywhere";
    body.style.wordBreak = "break-word";
    body.textContent = content || "(no step content)";

    details.append(summary, body);
    wrapper.appendChild(details);
    ui.chatContainer.appendChild(wrapper);
    ui.chatContainer.scrollTop = ui.chatContainer.scrollHeight;
}

function logTerminal(message, color, timestamp) {
    const entry = document.createElement("div");
    entry.className = "terminal-line log-entry";
    entry.style.color = TERMINAL_COLORS[color] || TERMINAL_COLORS.gray;
    entry.textContent = `[${formatTime(timestamp)}] ${message}`;

    ui.terminal.appendChild(entry);
    ui.terminal.scrollTop = ui.terminal.scrollHeight;
}

function logTerminalLink(message, path, color, timestamp) {
    const entry = document.createElement("div");
    entry.className = "terminal-line log-entry";
    entry.style.color = TERMINAL_COLORS[color] || TERMINAL_COLORS.gray;

    const prefix = document.createElement("span");
    prefix.textContent = `[${formatTime(timestamp)}] ${message} `;
    entry.appendChild(prefix);

    const link = document.createElement("a");
    link.textContent = path;
    link.href = `file://${path}`;
    link.target = "_blank";
    link.rel = "noopener noreferrer";
    link.style.color = "#93c5fd";
    link.style.textDecoration = "underline";
    link.style.wordBreak = "break-all";
    entry.appendChild(link);

    ui.terminal.appendChild(entry);
    ui.terminal.scrollTop = ui.terminal.scrollHeight;
}

function updateMetrics(metrics) {
    if (!metrics) {
        return;
    }

    ui.metrics.total.textContent = metrics.totalSteps || 0;
    ui.metrics.success.textContent = metrics.successfulSteps || 0;
    ui.metrics.failed.textContent = metrics.failedSteps || 0;
}

function syncWorkflowStateFromStatus(statusText) {
    const status = statusText || "";
    const isStopped = status === "Stopped" || status.startsWith("Stopped");

    if (
        status === "Starting workflow..." ||
        status === "Running" ||
        status === "Waiting for input" ||
        status === "Workflow already running" ||
        status === "Stopping..." ||
        status.startsWith("Resuming at step ")
    ) {
        state.workflowRunning = true;
        return;
    }

    if (
        status === "Ready" ||
        status === "Ready for next ticket" ||
        isStopped ||
        status === "No workflow is available to resume"
    ) {
        state.workflowRunning = false;
        state.canResume = status === "Ready for next ticket";
        state.nextStepToRun = state.canResume ? state.currentStep : 0;
    }
}

function handleMessage(data, options = {}) {
    const { replay = false } = options;
    const { type, payload = {}, timestamp } = data;

    switch (type) {
        case "hydrate":
            applyHydration(payload);
            break;

        case "workflow_reset":
            resetLocalState();
            clearContainers();
            renderEmptyState();
            updateProgress(0, 0);
            ui.stepName.textContent = "Starting...";
            updateMetrics({ totalSteps: 0, successfulSteps: 0, failedSteps: 0 });
            updateStatus("Ready", "green");
            updateBusyState(false);
            loadWorkflowTypes();
            loadProjects();
            loadLlmOptions();
            break;

        case "status":
            if (payload.status === "Waiting for input") {
                state.awaitingUserInput = true;
            } else if (
                payload.status === "Running" ||
                payload.status === "Starting workflow..." ||
                payload.status === "Ready" ||
                payload.status === "Ready for next ticket" ||
                (payload.status || "").startsWith("Stopped") ||
                payload.status === "Stopping..." ||
                payload.status === "Workflow already running" ||
                payload.status === "No workflow is available to resume" ||
                (payload.status || "").startsWith("Resuming at step ")
            ) {
                state.awaitingUserInput = false;
            }
            if (payload.status === "Ready for next ticket") {
                state.canResume = true;
                state.nextStepToRun = state.currentStep;
                state.showProjectConfigPanel = false;
            } else if (payload.status === "Ready" || (payload.status || "").startsWith("Stopped")) {
                state.canResume = false;
                state.nextStepToRun = 0;
            }
            syncWorkflowStateFromStatus(payload.status);
            updateStatus(payload.status, payload.color);
            updateBusyState(state.busy);
            if ((payload.status || "").startsWith("Stopped") || payload.status === "Ready") {
                void loadSchedules();
            }
            break;

        case "workflow_start":
            state.workflowRunning = true;
            state.awaitingUserInput = false;
            state.canResume = false;
            state.nextStepToRun = 0;
            state.ticketProgress = null;
            state.totalSteps = payload.totalSteps || 0;
            logTerminal(`[WORKFLOW] Started with ${state.totalSteps} steps`, "cyan", timestamp);
            updateProgress(0, state.totalSteps);
            updateBusyState(state.busy);
            break;

        case "workflow_resume":
            state.workflowRunning = true;
            state.awaitingUserInput = false;
            state.canResume = false;
            state.nextStepToRun = 0;
            if (payload.ticketProgress) {
                state.ticketProgress = payload.ticketProgress;
            }
            state.totalSteps = payload.totalSteps || state.totalSteps;
            logTerminal(`[WORKFLOW] Resuming at step ${payload.startStep || state.currentStep}/${state.totalSteps}`, "cyan", timestamp);
            updateBusyState(state.busy);
            break;

        case "workflow_end": {
            state.workflowRunning = false;
            state.ticketProgress = payload.ticketProgress || state.ticketProgress;
            const ticketRemaining = Number(state.ticketProgress?.remainingTickets || 0);
            if (ticketRemaining > 0) {
                state.showProjectConfigPanel = false;
            }
            if (ticketRemaining <= 0) {
                state.canResume = false;
                state.nextStepToRun = 0;
            }
            const total = payload.totalSteps || state.totalSteps;
            logTerminal(ticketRemaining > 0 ? "[WORKFLOW] Ticket run completed. Ready for next ticket." : "[WORKFLOW] Completed", "green", timestamp);
            updateProgress(total, total);
            ui.startBtn.disabled = false;
            ui.startBtn.textContent = "Start Workflow";
            updateBusyState(state.busy);
            void loadSchedules();
            if (!replay) {
                apiClient.getMetrics().then((metrics) => {
                    updateMetrics(metrics);
                }).catch(() => {
                    // Metrics are not available yet.
                });
            }
            break;
        }

        case "step_start": {
            state.workflowRunning = true;
            state.awaitingUserInput = false;
            state.currentStep = payload.stepNumber || 0;
            state.isCurrentStepTicketIteration = Boolean(payload.isTicketIterationStep);
            state.ticketHeaderStatus = payload.ticketHeaderStatus || (state.isCurrentStepTicketIteration ? state.ticketHeaderStatus : null);
            const total = payload.totalSteps || state.totalSteps;
            logTerminal(`[STEP ${state.currentStep}/${total}] Starting: ${payload.name || "Unknown"}`, "yellow", timestamp);
            updateProgress(state.currentStep, total);
            ui.stepName.textContent = payload.name || "Unknown";
            addLoadedStepMessage(payload.name || "Unknown", payload.content || "", timestamp);
            updateBusyState(state.busy);
            break;
        }

        case "step_complete":
            state.workflowRunning = true;
            state.awaitingUserInput = true;
            state.ticketProgress = payload.ticketProgress || state.ticketProgress;
            state.ticketHeaderStatus = payload.ticketHeaderStatus || state.ticketHeaderStatus;
            if (payload.isOpenChatStep === true) {
                logTerminal("[SYSTEM] Waiting for input. Type a message to continue.", "cyan", timestamp);
            } else {
                logTerminal(`[STEP ${payload.stepNumber}] Completed successfully`, "green", timestamp);
                logTerminal(
                    Number(state.ticketProgress?.remainingTickets || 0) > 0
                        ? "[SYSTEM] Step paused. Click Next Ticket when you are ready."
                        : "[SYSTEM] Step paused. Send feedback or click Next Step.",
                    "cyan",
                    timestamp);
            }
            updateProgress(state.currentStep, state.totalSteps);
            updateBusyState(state.busy);
            break;

        case "step_failed":
            state.ticketProgress = payload.ticketProgress || state.ticketProgress;
            state.ticketHeaderStatus = payload.ticketHeaderStatus || state.ticketHeaderStatus;
            logTerminal(`[STEP ${payload.stepNumber}] Failed: ${payload.error}`, "red", timestamp);
            updateProgress(state.currentStep, state.totalSteps);
            break;

        case "llm_response":
        {
            const reasoningText = typeof payload.reasoningContent === "string" ? payload.reasoningContent : "";
            const contentText = typeof payload.content === "string" ? payload.content : "";

            if (reasoningText.trim().length > 0) {
                addReasoningMessage(reasoningText, timestamp);
            }

            if (tryParseStructuredJson(contentText)) {
                addStructuredJsonMessage(contentText, timestamp);
                break;
            }

            if (contentText.trim().length > 0) {
                addChatMessage("agent", contentText, timestamp);
                if (contentText.startsWith("[TOOL CALL WARNING]")) {
                    logTerminal(contentText, "orange", timestamp);
                }
            } else if (reasoningText.trim().length === 0) {
                logTerminal("[LLM] Received an empty response chunk.", "orange", timestamp);
            }
            break;
        }

        case "user_question":
            addChatMessage("user", payload.question, timestamp, payload.source || "web", payload.sourceLabel || "");
            break;

        case "tool_request":
            addToolRequestMessage(
                payload.toolName,
                payload.requestSummary || "",
                timestamp
            );
            logTerminal(`[TOOL] ${payload.toolName}: Requested`, "cyan", timestamp);
            break;

        case "tool_execution":
            addToolExecutionMessage(
                payload.toolName,
                Boolean(payload.success),
                payload.output,
                timestamp,
                payload.requestSummary || ""
            );
            logTerminal(`[TOOL] ${payload.toolName}: ${payload.status}`, payload.success ? "blue" : "orange", timestamp);
            if (!payload.success && typeof payload.output === "string" && payload.output.toLowerCase().includes("timed out")) {
                logTerminal(`[TIMEOUT] ${payload.toolName} exceeded the configured tool timeout`, "red", timestamp);
            }
            break;

        case "busy":
            state.busy = Boolean(payload.isBusy);
            updateBusyState(payload.isBusy);
            break;

        case "log":
            logTerminal(`[LOG] ${payload.message}`, payload.color || "gray", timestamp);
            break;

        case "log_link":
            if (payload.path) {
                logTerminalLink(payload.message || "[LOG] Open file", payload.path, payload.color || "cyan", timestamp);
            } else {
                logTerminal(`[LOG] ${payload.message || "Link unavailable."}`, payload.color || "gray", timestamp);
            }
            break;

        default:
            console.log("[WS] Unknown message type:", type);
    }
}

async function hydrateFromServer() {
    try {
        const [workflowState, workflowTypes, projects, llmOptions, schedules] = await Promise.all([
            apiClient.getWorkflowState(),
            apiClient.getWorkflowTypes(),
            apiClient.getProjects(),
            apiClient.getLlmOptions(),
            apiClient.getSchedules()
        ]);
        applyHydration(workflowState);
        applyWorkflowTypes(workflowTypes);
        applyProjects(projects);
        applyLlmOptions(llmOptions);
        state.schedules = Array.isArray(schedules?.schedules) ? schedules.schedules : [];
        normalizeScheduleState(schedules?.scheduleState || workflowState?.snapshot?.scheduleState || null);
        renderScheduleList();
        await refreshLlmHealth(true);
    } catch (error) {
        console.error("[UI] Failed to hydrate workflow state:", error);
        renderEmptyState();
        updateStatus("Disconnected", "red");
        await Promise.all([loadWorkflowTypes(), loadProjects(), loadLlmOptions(), loadSchedules()]);
    }
}

async function loadWorkflowTypes() {
    try {
        const workflowTypes = await apiClient.getWorkflowTypes();
        applyWorkflowTypes(workflowTypes);
    } catch (error) {
        console.error("[UI] Failed to load workflow types:", error);
        state.workflowTypes = [];
        syncWorkflowTypePicker();
        updateBusyState(state.busy);
    }
}

async function loadProjects() {
    try {
        const projects = await apiClient.getProjects();
        applyProjects(projects);
    } catch (error) {
        console.error("[UI] Failed to load projects:", error);
        state.projects = [];
        syncProjectPicker();
        updateBusyState(state.busy);
    }
}

async function loadLlmOptions() {
    try {
        const llmOptions = await apiClient.getLlmOptions();
        applyLlmOptions(llmOptions);
        await refreshLlmHealth(true);
    } catch (error) {
        console.error("[UI] Failed to load LLM options:", error);
        state.llmServers = [];
        syncLlmSelectors();
        closeLlmSetupModal();
        updateBusyState(state.busy);
    }
}

async function selectActiveLlm(serverId, modelId) {
    if (!serverId || !modelId) {
        return false;
    }

    try {
        const result = await apiClient.selectLlm(serverId, modelId);
        state.activeLlmServerId = result.activeServerId || serverId;
        state.activeLlmModelId = result.activeModelId || modelId;
        state.currentLlmServerName = result.currentServerName || state.currentLlmServerName;
        state.currentLlmModelName = result.currentModelName || state.currentLlmModelName;
        syncLlmSelectors();
        logTerminal(`[LLM] Active model set to ${state.currentLlmModelName} @ ${state.currentLlmServerName}`, "cyan");
        await refreshLlmHealth(true);
        return true;
    } catch (error) {
        logTerminal(`[ERROR] Failed to switch LLM selection: ${error.message}`, "red");
        await loadLlmOptions();
        return false;
    }
}

async function selectProject(projectName) {
    if (!projectName) {
        return false;
    }

    try {
        const result = await apiClient.selectProject(projectName);
        if (result?.snapshot) {
            applyHydration({
                snapshot: result.snapshot,
                history: Array.isArray(result.history) ? result.history : []
            });
        } else {
            state.selectedProjectName = result.selectedProjectName || projectName;
            state.selectedProjectDirectory = result.selectedProjectDirectory || "";
        }
        state.targetProjectName = state.selectedProjectName;
        syncProjectPicker();
        setRequiredFieldValidation(!((state.selectedProjectName || state.targetProjectName || "").trim()), !state.selectedWorkflowTypeId);
        updateBusyState(state.busy);
        logTerminal(`[PROJECT] Switched to ${state.selectedProjectName}`, "green");
        return true;
    } catch (error) {
        logTerminal(`[ERROR] Failed to switch project: ${error.message}`, "red");
        syncProjectPicker();
        updateBusyState(state.busy);
        return false;
    }
}

async function createProjectFromInput() {
    const projectName = (ui.projectNameInput.value || "").trim();
    const projectNamePattern = /^[A-Za-z0-9_-]+$/;

    if (!projectNamePattern.test(projectName)) {
        logTerminal("[ERROR] Project name must contain only letters, numbers, dashes (-), or underscores (_)", "red");
        return;
    }

    const originalText = ui.createProjectBtn.textContent;
    ui.createProjectBtn.textContent = "Creating...";
    ui.createProjectBtn.disabled = true;

    try {
        const result = await apiClient.createProject(projectName);
        await loadProjects();
        closeCreateProjectModal();

        const createdProjectName = result?.project?.name || projectName;
        logTerminal(result.created
            ? `[PROJECT] Created ${createdProjectName}`
            : `[PROJECT] ${createdProjectName} already exists`, "cyan");
        state.targetProjectName = createdProjectName;
        // Automatically select the newly created project
        const selected = await selectProject(createdProjectName);
        if (selected) {
            // Clear canResume so user can start workflow immediately
            state.canResume = false;
            syncProjectPicker();
            updateBusyState(state.busy);
            logTerminal(`[PROJECT] Selected ${createdProjectName}`, "green");
        } else {
            logTerminal("[PROJECT] Failed to select the new project.", "orange");
        }
    } catch (error) {
        logTerminal(`[ERROR] Failed to create project: ${error.message}`, "red");
    } finally {
        ui.createProjectBtn.textContent = originalText;
        updateBusyState(state.busy);
    }
}

async function uploadSelectedFiles() {
    const files = ui.fileUploadInput.files;
    if (!files || files.length === 0) {
        ui.fileUploadInput.click();
        return;
    }

    ui.uploadBtn.disabled = true;
    ui.uploadBtn.textContent = "Uploading...";

    try {
        const result = await apiClient.uploadFiles(files);
        const uploadedFiles = Array.isArray(result?.files) ? result.files : [];
        if (uploadedFiles.length > 0) {
            const summary = uploadedFiles.map((file) => file.relativePath).join("\n");
            const llmNotification = [
                `I uploaded file${uploadedFiles.length === 1 ? "" : "s"} for this workflow:`,
                ...uploadedFiles.map((file) => `- ${file.fileName}: ${file.relativePath}`)
            ].join("\n");

            try {
                await apiClient.sendMessage(llmNotification);
                logTerminal("[SYSTEM] Uploaded file details sent to the agent", "cyan");
            } catch (sendError) {
                addChatMessage("agent", `Uploaded file${uploadedFiles.length === 1 ? "" : "s"}:\n${summary}`, new Date().toISOString());
                logTerminal(`[SYSTEM] Files uploaded, but the agent could not be notified yet: ${sendError.message}`, "orange");
            }

            logTerminal(`[FILES] Uploaded ${uploadedFiles.length} file${uploadedFiles.length === 1 ? "" : "s"} to ./files`, "green");
        }

        ui.fileUploadInput.value = "";
        renderSelectedFiles([]);
    } catch (error) {
        logTerminal(`[ERROR] Failed to upload files: ${error.message}`, "red");
    } finally {
        ui.uploadBtn.textContent = "Upload";
        updateBusyState(state.busy);
    }
}

function initClient() {
    const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
    const wsUrl = `${protocol}//${window.location.host}/ws/agent`;

    wsClient = new WebSocketClient(wsUrl);
    apiClient = new APIClient(window.location.origin);
    void checkForUpdatesOnStartup();

    hydrateFromServer().finally(() => {
        wsClient.connect((connected, error) => {
            if (connected) {
                logTerminal("[SYSTEM] WebSocket connected", "green");
                if (!state.workflowRunning && state.currentStep === 0) {
                    updateStatus("Connected", "green");
                }
            } else {
                logTerminal(`[SYSTEM] WebSocket connection failed: ${error}`, "red");
                updateStatus("Disconnected", "red");
            }
        });
    });
}

async function startWorkflow() {
    if (state.activeMode === "schedule" || state.scheduleState?.isActive) {
        try {
            const schedules = await apiClient.getSchedules();
            state.schedules = Array.isArray(schedules?.schedules) ? schedules.schedules : state.schedules;
            normalizeScheduleState(schedules?.scheduleState || null);
            renderScheduleList();
        } catch {
            // keep current client state if refresh fails
        }

        if (state.activeMode === "schedule" || state.scheduleState?.isActive) {
            logTerminal("[SYSTEM] Manual start is disabled while Schedule Mode is active. Stop the schedule first.", "orange");
            updateStatus("Schedule Mode active", "orange");
            updateBusyState(state.busy);
            return;
        }
    }

    if (state.llmHealthy === false) {
        openLlmSetupModal(state.llmHealthDetail || "LLM server is not reachable. Configure it in Settings.");
        logTerminal("[ERROR] Configure an LLM server in Settings before starting.", "red");
        updateBusyState(state.busy);
        return;
    }

    const startFromNewWorkflowPanel = Boolean(state.showProjectConfigPanel || state.newWorkflowIntentActive);
    const requestedWorkflowTypeId = state.selectedWorkflowTypeId || "";
    const requestedWorkflowTypeName = state.selectedWorkflowTypeName || "";
    let workflowTypeChanged = state.canResume &&
        Boolean(requestedWorkflowTypeId) &&
        Boolean(state.resumeWorkflowTypeId) &&
        requestedWorkflowTypeId !== state.resumeWorkflowTypeId;
    let forceNewWorkflow = startFromNewWorkflowPanel || workflowTypeChanged;
    let canSafelyResume = state.canResume && (state.currentStep > 0 || state.awaitingUserInput);
    let shouldResume = canSafelyResume && !forceNewWorkflow;

    if (state.awaitingUserInput && state.canResume && !forceNewWorkflow) {
        logTerminal("[SYSTEM] Workflow is already waiting for input. Send a message or click Next Step.", "orange");
        updateBusyState(state.busy);
        return;
    }

    if (!shouldResume) {
        const intendedProject = (state.selectedProjectName || state.targetProjectName || "").trim();
        const missingProject = !intendedProject;
        const missingWorkflowType = !requestedWorkflowTypeId;

        if (missingProject || missingWorkflowType) {
            setRequiredFieldValidation(missingProject, missingWorkflowType);
            logTerminal("[ERROR] Select Project and Workflow Type before starting", "red");
            updateBusyState(state.busy);
            return;
        }

        setRequiredFieldValidation(false, false);

        if (state.targetProjectName && state.targetProjectName !== state.selectedProjectName) {
            const switched = await selectProject(state.targetProjectName);
            if (!switched) {
                logTerminal("[ERROR] Failed to switch to the selected project before starting.", "red");
                updateBusyState(state.busy);
                return;
            }

            // Project hydration can restore the project's previously-saved workflow type.
            // Re-apply what the user explicitly chose before we decide resume vs new run.
            state.selectedWorkflowTypeId = requestedWorkflowTypeId;
            state.selectedWorkflowTypeName = requestedWorkflowTypeName;
            syncWorkflowTypePicker();
            updateBusyState(state.busy);

            workflowTypeChanged = state.canResume &&
                Boolean(requestedWorkflowTypeId) &&
                Boolean(state.resumeWorkflowTypeId) &&
                requestedWorkflowTypeId !== state.resumeWorkflowTypeId;
            // Keep explicit "New Workflow" intent even after project hydration resets panel flags.
            forceNewWorkflow = startFromNewWorkflowPanel || workflowTypeChanged;
            canSafelyResume = state.canResume && (state.currentStep > 0 || state.awaitingUserInput);
            shouldResume = canSafelyResume && !forceNewWorkflow;
        }
    } else {
        setRequiredFieldValidation(false, false);
    }

    try {
        logTerminal("[SYSTEM] Start Workflow requested", "cyan");
        if (workflowTypeChanged) {
            const resumeName = state.workflowTypes.find((item) => item.id === state.resumeWorkflowTypeId)?.name || state.resumeWorkflowTypeId;
            const startName = requestedWorkflowTypeName || requestedWorkflowTypeId;
            const ok = window.confirm(
                `This project has an existing resumable workflow (${resumeName}).\n\nStarting ${startName} will reset the current workflow state.\n\nContinue?`
            );
            if (!ok) {
                ui.startBtn.textContent = "Start Workflow";
                ui.startBtn.disabled = false;
                updateBusyState(state.busy);
                return;
            }
        }

        ui.startBtn.textContent = "Starting...";
        ui.startBtn.disabled = true;
        await apiClient.startWorkflow(shouldResume ? null : requestedWorkflowTypeId || null);
        closeNewWorkflowModal();
        logTerminal(shouldResume
            ? "[SYSTEM] Workflow resume requested"
            : `[SYSTEM] Starting ${requestedWorkflowTypeName || "workflow"}`, "cyan");
    } catch (error) {
        logTerminal(`[ERROR] Failed to start workflow: ${error.message}`, "red");
        ui.startBtn.textContent = "Start Workflow";
        updateBusyState(state.busy);
    }
}

ui.startBtn.addEventListener("click", async () => {
    console.log("[UI] Start workflow button clicked");

    if (!wsClient || !wsClient.isConnected) {
        console.log("[UI] WebSocket not connected, trying to connect...");
        logTerminal("[SYSTEM] WebSocket not connected, connecting...", "orange");
        wsClient.connect((connected, error) => {
            if (connected) {
                console.log("[UI] WebSocket connected");
                logTerminal("[SYSTEM] WebSocket connected", "green");
                startWorkflow();
            } else {
                console.log(`[UI] Failed to connect: ${error}`);
                logTerminal(`[ERROR] Failed to connect: ${error}`, "red");
            }
        });
    } else {
        startWorkflow();
    }
});

ui.sendBtn.addEventListener("click", async () => {
    const message = ui.userInput.value.trim();
    if (!message) {
        return;
    }

    try {
        await apiClient.sendMessage(message);
        ui.userInput.value = "";
        logTerminal("[SYSTEM] Message queued for agent", "cyan");
    } catch (error) {
        logTerminal(`[ERROR] Failed to send message: ${error.message}`, "red");
    }
});

ui.uploadBtn.addEventListener("click", async () => {
    if (!ui.fileUploadInput.files || ui.fileUploadInput.files.length === 0) {
        ui.fileUploadInput.click();
        return;
    }

    await uploadSelectedFiles();
});

ui.fileUploadInput.addEventListener("change", () => {
    renderSelectedFiles(ui.fileUploadInput.files);
    if (ui.fileUploadInput.files && ui.fileUploadInput.files.length > 0) {
        uploadSelectedFiles();
    }
});

ui.continueBtn.addEventListener("click", async () => {
    try {
        await apiClient.continueWorkflow();
        logTerminal(state.canResume
            ? "[SYSTEM] Resuming from the saved checkpoint"
            : "[SYSTEM] Advancing to the next step", "cyan");
    } catch (error) {
        logTerminal(`[ERROR] Failed to continue: ${error.message}`, "red");
    }
});

if (ui.skipBtn) {
    ui.skipBtn.addEventListener("click", () => {
        if (state.busy) {
            logTerminal("[SYSTEM] Wait for the LLM to finish before skipping.", "orange");
            return;
        }

        openSkipModal();
    });
}

ui.resetBtn.addEventListener("click", async () => {
    const shouldReset = window.confirm("Reset this workflow? This clears the current workflow state.");
    if (!shouldReset) {
        return;
    }

    try {
        await apiClient.resetWorkflow();
        handleMessage({ type: "workflow_reset", payload: {}, timestamp: new Date().toISOString() });
    } catch (error) {
        logTerminal(`[ERROR] Failed to reset workflow: ${error.message}`, "red");
    }
});

ui.workflowTypeSelect.addEventListener("change", (event) => {
    state.selectedWorkflowTypeId = event.target.value || "";
    state.selectedWorkflowTypeName = state.workflowTypes.find((workflowType) => workflowType.id === state.selectedWorkflowTypeId)?.name || "";
    const projectMissing = !((state.selectedProjectName || state.targetProjectName || "").trim());
    setRequiredFieldValidation(projectMissing, !state.selectedWorkflowTypeId);
    updateBusyState(state.busy);
});

ui.projectSelect.addEventListener("change", async (event) => {
    const projectName = event.target.value || "";
    if (!projectName) {
        state.targetProjectName = "";
        syncProjectHelp();
        const projectMissing = !((state.selectedProjectName || state.targetProjectName || "").trim());
        setRequiredFieldValidation(projectMissing, !state.selectedWorkflowTypeId);
        updateBusyState(state.busy);
        return;
    }

    state.targetProjectName = projectName;
    syncProjectHelp();
    const projectMissing = !((state.selectedProjectName || state.targetProjectName || "").trim());
    setRequiredFieldValidation(projectMissing, !state.selectedWorkflowTypeId);
    updateBusyState(state.busy);
});

ui.switchProjectBtn.addEventListener("click", async () => {
    if (state.busy) {
        logTerminal("[SYSTEM] Wait for the LLM to finish before switching projects.", "orange");
        return;
    }

    closeStartNewModal();
    await loadSwitchProjectModal();
});

if (ui.newProjectBtn) {
    ui.newProjectBtn.addEventListener("click", () => {
        if (state.busy) {
            return;
        }

        closeStartNewModal();
        if (!state.targetProjectName && state.selectedProjectName) {
            state.targetProjectName = state.selectedProjectName;
        }
        if (!state.selectedWorkflowTypeId && state.workflowTypes.length > 0) {
            state.selectedWorkflowTypeId = state.workflowTypes[0].id || "";
            state.selectedWorkflowTypeName = state.workflowTypes[0].name || "";
            syncWorkflowTypePicker();
        }
        openNewWorkflowModal();
        if (state.selectedWorkflowTypeId) {
            ui.projectSelect?.focus();
        } else {
            ui.workflowTypeSelect?.focus();
        }
    });
}

if (ui.scheduleBtn) {
    ui.scheduleBtn.addEventListener("click", () => {
        if (state.busy) {
            return;
        }

        closeStartNewModal();
        openScheduleModal();
    });
}

if (ui.newOpenBtn) {
    ui.newOpenBtn.addEventListener("click", () => {
        if (state.busy) {
            return;
        }

        openStartNewModal();
    });
}

if (ui.startNewCloseBtn) {
    ui.startNewCloseBtn.addEventListener("click", () => {
        closeStartNewModal();
    });
}

if (ui.startNewModal) {
    ui.startNewModal.addEventListener("click", (event) => {
        if (event.target === ui.startNewModal) {
            closeStartNewModal();
        }
    });
}

if (ui.newWorkflowCloseBtn) {
    ui.newWorkflowCloseBtn.addEventListener("click", () => {
        closeNewWorkflowModal();
    });
}

if (ui.newWorkflowModal) {
    ui.newWorkflowModal.addEventListener("click", (event) => {
        if (event.target === ui.newWorkflowModal) {
            closeNewWorkflowModal();
        }
    });
}

if (ui.createProjectOpenBtn) {
    ui.createProjectOpenBtn.addEventListener("click", () => {
        if (state.busy) {
            return;
        }

        openCreateProjectModal();
    });
}

if (ui.manageWorkflowTypesBtn) {
    ui.manageWorkflowTypesBtn.addEventListener("click", async () => {
        if (state.busy) {
            return;
        }

        await openWorkflowEditorModal();
    });
}

if (ui.scheduleCloseBtn) {
    ui.scheduleCloseBtn.addEventListener("click", () => {
        closeScheduleModal();
    });
}

if (ui.scheduleModal) {
    ui.scheduleModal.addEventListener("click", (event) => {
        if (event.target === ui.scheduleModal) {
            closeScheduleModal();
        }
    });
}

if (ui.scheduleRefreshBtn) {
    ui.scheduleRefreshBtn.addEventListener("click", async () => {
        await loadSchedules();
    });
}

if (ui.scheduleStopBtn) {
    ui.scheduleStopBtn.addEventListener("click", async () => {
        await stopSchedule();
    });
}

if (ui.scheduleOpenEditorBtn) {
    ui.scheduleOpenEditorBtn.addEventListener("click", () => {
        openScheduleEditorModal();
    });
}

if (ui.scheduleList) {
    ui.scheduleList.addEventListener("click", async (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        const recreateButton = target.closest(".schedule-recreate-btn");
        if (recreateButton instanceof HTMLElement) {
            const scheduleName = (recreateButton.dataset.scheduleName || "").trim();
            openScheduleEditorForRecreate(scheduleName);
            return;
        }

        const button = target.closest(".schedule-start-btn");
        if (button instanceof HTMLElement) {
            const scheduleName = (button.dataset.scheduleName || "").trim();
            await startSchedule(scheduleName);
            return;
        }

        const editButton = target.closest(".schedule-edit-btn");
        if (editButton instanceof HTMLElement) {
            const scheduleName = (editButton.dataset.scheduleName || "").trim();
            await openScheduleEditorForEdit(scheduleName);
        }
    });
}

if (ui.scheduleEditorCloseBtn) {
    ui.scheduleEditorCloseBtn.addEventListener("click", () => {
        closeScheduleEditorModal();
    });
}

if (ui.scheduleEditorCancelBtn) {
    ui.scheduleEditorCancelBtn.addEventListener("click", () => {
        closeScheduleEditorModal();
    });
}

if (ui.scheduleEditorModal) {
    ui.scheduleEditorModal.addEventListener("click", (event) => {
        if (event.target === ui.scheduleEditorModal) {
            closeScheduleEditorModal();
        }
    });
}

if (ui.scheduleEditorSaveBtn) {
    ui.scheduleEditorSaveBtn.addEventListener("click", async () => {
        await saveScheduleFromEditor();
    });
}

if (ui.scheduleEditorExistingSelect) {
    ui.scheduleEditorExistingSelect.addEventListener("change", async () => {
        await loadScheduleIntoEditor(ui.scheduleEditorExistingSelect.value || "");
    });
}

if (ui.scheduleEditorWorkflowSelect) {
    ui.scheduleEditorWorkflowSelect.addEventListener("change", async () => {
        await loadScheduleEditorWorkflowSteps(ui.scheduleEditorWorkflowSelect.value || "", []);
    });
}

if (ui.scheduleEditorTriggerType) {
    ui.scheduleEditorTriggerType.addEventListener("change", () => {
        syncScheduleEditorVisibility();
    });
}

if (ui.scheduleEditorScheduleType) {
    ui.scheduleEditorScheduleType.addEventListener("change", () => {
        syncScheduleEditorVisibility();
    });
}

if (ui.scheduleEditorRegularServer) {
    ui.scheduleEditorRegularServer.addEventListener("change", () => {
        updateScheduleEditorRegularModelOptions();
    });
}

if (ui.scheduleEditorBenchmarkList) {
    ui.scheduleEditorBenchmarkList.addEventListener("change", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLInputElement)) {
            return;
        }

        if (!target.classList.contains("schedule-benchmark-checkbox")) {
            return;
        }

        const key = (target.dataset.key || "").trim();
        if (!key) {
            return;
        }

        const next = new Set(state.scheduleEditorBenchmarkSelectionKeys);
        if (target.checked) {
            next.add(key);
        } else {
            next.delete(key);
        }
        state.scheduleEditorBenchmarkSelectionKeys = Array.from(next);
    });
}

if (ui.scheduleEditorStepsList) {
    ui.scheduleEditorStepsList.addEventListener("change", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLInputElement)) {
            return;
        }

        if (!target.classList.contains("schedule-step-checkbox")) {
            return;
        }

        const stepFileName = (target.dataset.stepFileName || "").trim();
        if (!stepFileName) {
            return;
        }

        const next = new Set(state.scheduleEditorSelectedStepFileNames);
        if (target.checked) {
            next.add(stepFileName);
        } else {
            next.delete(stepFileName);
        }
        state.scheduleEditorSelectedStepFileNames = Array.from(next);
    });
}

ui.switchProjectCloseBtn.addEventListener("click", () => {
    closeSwitchProjectModal();
});

ui.switchProjectModal.addEventListener("click", (event) => {
    if (event.target === ui.switchProjectModal) {
        closeSwitchProjectModal();
    }
});

if (ui.skipCloseBtn) {
    ui.skipCloseBtn.addEventListener("click", () => {
        closeSkipModal();
    });
}

if (ui.skipModal) {
    ui.skipModal.addEventListener("click", (event) => {
        if (event.target === ui.skipModal) {
            closeSkipModal();
        }
    });
}

if (ui.createProjectCloseBtn) {
    ui.createProjectCloseBtn.addEventListener("click", () => {
        closeCreateProjectModal();
    });
}

if (ui.createProjectCancelBtn) {
    ui.createProjectCancelBtn.addEventListener("click", () => {
        closeCreateProjectModal();
    });
}

if (ui.createProjectModal) {
    ui.createProjectModal.addEventListener("click", (event) => {
        if (event.target === ui.createProjectModal) {
            closeCreateProjectModal();
        }
    });
}

if (ui.workflowEditorCloseBtn) {
    ui.workflowEditorCloseBtn.addEventListener("click", () => {
        closeWorkflowEditorModal();
    });
}

if (ui.workflowEditorModal) {
    ui.workflowEditorModal.addEventListener("click", (event) => {
        if (event.target === ui.workflowEditorModal) {
            closeWorkflowEditorModal();
        }
    });
}

if (ui.workflowEditorOpenCreateBtn) {
    ui.workflowEditorOpenCreateBtn.addEventListener("click", () => {
        openWorkflowEditorCreateModal();
    });
}

if (ui.workflowEditorAddStepBtn) {
    ui.workflowEditorAddStepBtn.addEventListener("click", async () => {
        await addStepFromWorkflowEditor();
    });
}

if (ui.workflowEditorCreateIterationBtn) {
    ui.workflowEditorCreateIterationBtn.addEventListener("click", async () => {
        await createTicketIterationForCurrentStep();
    });
}

if (ui.workflowEditorSaveBtn) {
    ui.workflowEditorSaveBtn.addEventListener("click", async () => {
        await saveWorkflowEditorStep();
    });
}

if (ui.workflowEditorCreateSubmitBtn) {
    ui.workflowEditorCreateSubmitBtn.addEventListener("click", async () => {
        await createWorkflowTypeFromEditor();
    });
}

if (ui.workflowEditorCreateCloseBtn) {
    ui.workflowEditorCreateCloseBtn.addEventListener("click", () => {
        closeWorkflowEditorCreateModal();
    });
}

if (ui.workflowEditorCreateCancelBtn) {
    ui.workflowEditorCreateCancelBtn.addEventListener("click", () => {
        closeWorkflowEditorCreateModal();
    });
}

if (ui.workflowEditorCreateModal) {
    ui.workflowEditorCreateModal.addEventListener("click", (event) => {
        if (event.target === ui.workflowEditorCreateModal) {
            closeWorkflowEditorCreateModal();
        }
    });
}

if (ui.workflowEditorCreateNameInput) {
    ui.workflowEditorCreateNameInput.addEventListener("keypress", async (event) => {
        if (event.key === "Enter") {
            event.preventDefault();
            await createWorkflowTypeFromEditor();
        }
    });
}

if (ui.workflowEditorWorkflowsList) {
    ui.workflowEditorWorkflowsList.addEventListener("click", async (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        const button = target.closest("[data-workflow-editor-action]");
        if (!(button instanceof HTMLElement)) {
            return;
        }

        const action = (button.dataset.workflowEditorAction || "").trim();
        const workflowId = (button.dataset.workflowId || "").trim();
        if (!action || !workflowId) {
            return;
        }

        if (action === "select-workflow") {
            await selectWorkflowEditorWorkflow(workflowId, { revealDeleteButton: true });
            return;
        }

        if (action === "delete-workflow") {
            if (workflowId !== state.workflowEditorSelectedWorkflowId) {
                await selectWorkflowEditorWorkflow(workflowId, { revealDeleteButton: true });
            }
            await deleteWorkflowFromEditor();
        }
    });
}

if (ui.workflowEditorStepsList) {
    ui.workflowEditorStepsList.addEventListener("click", async (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        const button = target.closest("[data-workflow-editor-action]");
        if (!(button instanceof HTMLElement)) {
            return;
        }

        const action = (button.dataset.workflowEditorAction || "").trim();
        const stepFileName = (button.dataset.stepFileName || "").trim();
        if (!action || !stepFileName) {
            return;
        }

        if (action === "select-step") {
            await selectWorkflowEditorStep(stepFileName, { revealDeleteButton: true });
            return;
        }

        if (action === "delete-step") {
            if (stepFileName !== state.workflowEditorSelectedStepFileName) {
                await selectWorkflowEditorStep(stepFileName, { revealDeleteButton: true });
            }
            await deleteStepFromEditor();
        }
    });
}

if (ui.workflowEditorMarkdown) {
    ui.workflowEditorMarkdown.addEventListener("input", () => {
        if (!state.workflowEditorSelectedStepFileName) {
            return;
        }
        setWorkflowEditorDirty(true);
    });
}

if (ui.workflowEditorMetadata) {
    ui.workflowEditorMetadata.addEventListener("input", () => {
        if (!state.workflowEditorSelectedStepFileName) {
            return;
        }
        setWorkflowEditorDirty(true);
    });
}

if (ui.contentViewCloseBtn) {
    ui.contentViewCloseBtn.addEventListener("click", () => {
        closeContentViewModal();
    });
}

if (ui.contentViewModal) {
    ui.contentViewModal.addEventListener("click", (event) => {
        if (event.target === ui.contentViewModal) {
            closeContentViewModal();
        }
    });
}

if (ui.viewCurrentStepBtn) {
    ui.viewCurrentStepBtn.addEventListener("click", async () => {
        await openCurrentStepViewer();
    });
}

if (ui.viewCurrentTicketBtn) {
    ui.viewCurrentTicketBtn.addEventListener("click", async () => {
        await openCurrentTicketViewer();
    });
}

if (ui.skipStepsList) {
    ui.skipStepsList.addEventListener("click", async (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        const action = target.dataset.action;
        if (!action) {
            return;
        }

        event.preventDefault();
        event.stopPropagation();

        if (action === "skip-step") {
            const stepNumber = Number(target.dataset.stepNumber || 0);
            if (!stepNumber) {
                return;
            }

            try {
                await apiClient.skipToStep(stepNumber);
                logTerminal(`[SYSTEM] Skipping to step ${stepNumber}`, "cyan");
                closeSkipModal();
            } catch (error) {
                logTerminal(`[ERROR] Failed to skip step: ${error.message}`, "red");
                if (ui.skipModalStatus) {
                    ui.skipModalStatus.textContent = `Failed to skip step: ${error.message}`;
                    ui.skipModalStatus.className = "text-sm text-red-700";
                }
            }

            return;
        }

        if (action === "view-step") {
            const stepNumber = Number(target.dataset.stepNumber || 0);
            if (!stepNumber) {
                return;
            }

            try {
                await viewStepContent(stepNumber);
            } catch (error) {
                logTerminal(`[ERROR] Failed to view step: ${error.message}`, "red");
            }

            return;
        }

        if (action === "skip-ticket") {
            const stepNumber = Number(target.dataset.stepNumber || 0);
            const ticketId = (target.dataset.ticketId || "").trim();
            if (!stepNumber || !ticketId) {
                return;
            }

            try {
                await apiClient.skipTicket(stepNumber, ticketId);
                logTerminal(`[SYSTEM] Skipping ticket ${ticketId}`, "cyan");
                closeSkipModal();
            } catch (error) {
                logTerminal(`[ERROR] Failed to skip ticket: ${error.message}`, "red");
                if (ui.skipModalStatus) {
                    ui.skipModalStatus.textContent = `Failed to skip ticket: ${error.message}`;
                    ui.skipModalStatus.className = "text-sm text-red-700";
                }
            }
        }

        if (action === "start-ticket") {
            const stepNumber = Number(target.dataset.stepNumber || 0);
            const ticketId = (target.dataset.ticketId || "").trim();
            if (!stepNumber || !ticketId) {
                return;
            }

            try {
                await apiClient.startSpecificTicket(stepNumber, ticketId);
                logTerminal(`[SYSTEM] Starting ticket ${ticketId}`, "cyan");
                closeSkipModal();
            } catch (error) {
                logTerminal(`[ERROR] Failed to start ticket: ${error.message}`, "red");
                if (ui.skipModalStatus) {
                    ui.skipModalStatus.textContent = `Failed to start ticket: ${error.message}`;
                    ui.skipModalStatus.className = "text-sm text-red-700";
                }
            }

            return;
        }

        if (action === "resume-ticket") {
            const stepNumber = Number(target.dataset.stepNumber || 0);
            const ticketId = (target.dataset.ticketId || "").trim();
            if (!stepNumber || !ticketId) {
                return;
            }

            try {
                await apiClient.resumeSkippedTicket(stepNumber, ticketId);
                logTerminal(`[SYSTEM] Resuming ticket ${ticketId}`, "cyan");
                await loadSkipOptions();
            } catch (error) {
                logTerminal(`[ERROR] Failed to resume ticket: ${error.message}`, "red");
                if (ui.skipModalStatus) {
                    ui.skipModalStatus.textContent = `Failed to resume ticket: ${error.message}`;
                    ui.skipModalStatus.className = "text-sm text-red-700";
                }
            }
        }

        if (action === "reopen-ticket") {
            const stepNumber = Number(target.dataset.stepNumber || 0);
            const ticketId = (target.dataset.ticketId || "").trim();
            if (!stepNumber || !ticketId) {
                return;
            }

            try {
                await apiClient.reopenCompletedTicket(stepNumber, ticketId);
                logTerminal(`[SYSTEM] Reopening ticket ${ticketId}`, "cyan");
                closeSkipModal();
            } catch (error) {
                logTerminal(`[ERROR] Failed to reopen ticket: ${error.message}`, "red");
                if (ui.skipModalStatus) {
                    ui.skipModalStatus.textContent = `Failed to reopen ticket: ${error.message}`;
                    ui.skipModalStatus.className = "text-sm text-red-700";
                }
            }
        }
    });
}

ui.llmServerSelect.addEventListener("change", async (event) => {
    const serverId = event.target.value || "";
    const server = state.llmServers.find((item) => item.id === serverId);
    if (!server) {
        return;
    }

    const models = Array.isArray(server.models) ? server.models : [];
    const preferredModel = models.find((model) => model.id === server.defaultModelId) || models[0];
    if (!preferredModel) {
        return;
    }

    await selectActiveLlm(server.id, preferredModel.id);
});

// --- Model Autocomplete Logic ---

function renderModelDropdown(query) {
    const models = state.activeServerModels || [];
    const dropdown = ui.llmModelDropdown;

    dropdown.innerHTML = "";

    if (!query || query.length === 0) {
        // Show all models if query is empty
        if (models.length === 0) {
            const noResults = document.createElement("div");
            noResults.className = "px-3 py-2 text-xs text-gray-400 italic";
            noResults.textContent = "No models available";
            dropdown.appendChild(noResults);
        } else {
            models.forEach((model) => {
                createModelDropdownItem(model, dropdown);
            });
        }
        dropdown.classList.remove("hidden");
        return;
    }

    const filtered = models.filter(model =>
        model.name.toLowerCase().includes(query.toLowerCase())
    );

    if (filtered.length === 0) {
        const noResults = document.createElement("div");
        noResults.className = "px-3 py-2 text-xs text-gray-400 italic";
        noResults.textContent = "No models found";
        dropdown.appendChild(noResults);
    } else {
        filtered.forEach((model) => {
            createModelDropdownItem(model, dropdown);
        });
    }

    dropdown.classList.remove("hidden");
}

function createModelDropdownItem(model, dropdown) {
    const div = document.createElement("div");
    div.className = "px-3 py-2 text-xs text-white hover:bg-gray-700 cursor-pointer truncate transition-colors";
    div.textContent = model.name;
    div.dataset.modelId = model.id;

    div.addEventListener("mousedown", async (e) => {
        e.preventDefault(); // Prevent input from losing focus before click is processed
        const serverId = state.activeLlmServerId;
        if (!serverId) {
            return;
        }

        const success = await selectActiveLlm(serverId, model.id);
        if (success) {
            ui.llmModelInput.value = model.name;
            ui.llmModelDropdown.classList.add("hidden");
            ui.llmModelDropdown.innerHTML = "";
        }
    });

    dropdown.appendChild(div);
}

// Wire up the autocomplete input
if (ui.llmModelInput) {
    ui.llmModelInput.addEventListener("input", (e) => {
        renderModelDropdown(e.target.value);
    });

    ui.llmModelInput.addEventListener("focus", () => {
        renderModelDropdown(ui.llmModelInput.value);
    });

    ui.llmModelInput.addEventListener("keydown", (e) => {
        if (e.key === "Escape") {
            ui.llmModelDropdown.classList.add("hidden");
            ui.llmModelDropdown.innerHTML = "";
            ui.llmModelInput.blur();
        }
    });

    // Close dropdown when clicking outside
    document.addEventListener("click", (e) => {
        if (!ui.llmModelInput.contains(e.target) && !ui.llmModelDropdown.contains(e.target)) {
            ui.llmModelDropdown.classList.add("hidden");
            ui.llmModelDropdown.innerHTML = "";
        }
    });
}

ui.createProjectBtn.addEventListener("click", async () => {
    await createProjectFromInput();
});

ui.projectNameInput.addEventListener("keypress", async (event) => {
    if (event.key === "Enter") {
        event.preventDefault();
        await createProjectFromInput();
    }
});

ui.userInput.addEventListener("keypress", (event) => {
    if (event.key === "Enter") {
        ui.sendBtn.click();
    }
});

ui.stopBtn.addEventListener("click", async () => {
    try {
        await apiClient.stop();
        logTerminal("[SYSTEM] Stop request sent", "orange");
    } catch (error) {
        logTerminal(`[ERROR] Failed to stop: ${error.message}`, "red");
    }
});

if (ui.stopScheduleTaskBtn) {
    ui.stopScheduleTaskBtn.addEventListener("click", async () => {
        try {
            const isScheduleMode = state.activeMode === "schedule" || Boolean(state.scheduleState?.isActive);
            if (isScheduleMode) {
                try {
                    const stopTaskResult = await apiClient.stopScheduleTask();
                    logTerminal(`[SCHEDULE] ${stopTaskResult.message || "Stop Schedule Task requested."}`, "orange");
                } catch (stopTaskError) {
                    logTerminal(`[SCHEDULE] Stop current task note: ${stopTaskError.message}`, "orange");
                }

                const stopScheduleResult = await apiClient.stopSchedule();
                logTerminal(`[SCHEDULE] ${stopScheduleResult.message || "Stopped schedule."}`, "orange");
                await loadSchedules();
                return;
            }

            await apiClient.resetWorkflow();
            logTerminal("[SYSTEM] Stop All requested. Workflow fully stopped.", "orange");
        } catch (error) {
            logTerminal(`[ERROR] Failed to stop all: ${error.message}`, "red");
        }
    });
}

document.addEventListener("DOMContentLoaded", () => {
    renderEmptyState();
    initClient();
});

if (ui.llmSetupCloseBtn) {
    ui.llmSetupCloseBtn.addEventListener("click", () => {
        closeLlmSetupModal();
    });
}

if (ui.llmSetupModal) {
    ui.llmSetupModal.addEventListener("click", (event) => {
        if (event.target === ui.llmSetupModal) {
            closeLlmSetupModal();
        }
    });
}

if (ui.llmSetupRetryBtn) {
    ui.llmSetupRetryBtn.addEventListener("click", async () => {
        await refreshLlmHealth(true);
    });
}

if (ui.wsReconnectRetryBtn) {
    ui.wsReconnectRetryBtn.addEventListener("click", () => {
        wsClient.connect((connected, error) => {
            if (connected) {
                logTerminal("[SYSTEM] WebSocket reconnected", "green");
                updateStatus("Connected", "green");
                closeWebSocketReconnectModal();
            } else {
                const message = error || "WebSocket reconnect failed.";
                if (ui.wsReconnectDetails) {
                    ui.wsReconnectDetails.textContent = message;
                }
                logTerminal(`[ERROR] ${message}`, "red");
            }
        });
    });
}

if (ui.wsReconnectCloseBtn) {
    ui.wsReconnectCloseBtn.addEventListener("click", () => {
        closeWebSocketReconnectModal();
    });
}
