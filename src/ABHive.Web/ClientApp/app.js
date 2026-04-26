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

                while (this.queue.length > 0) {
                    const msg = this.queue.shift();
                    this.send(msg.type, msg.payload);
                }

                if (callback) {
                    callback(true);
                }
            };

            this.socket.onclose = (event) => {
                console.log(`[WS] Disconnected: code=${event.code}, reason="${event.reason}"`);
                this.isConnected = false;
                updateStatus("Disconnected", "red");
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
                    ? payload.message || `HTTP ${response.status}`
                    : payload || `HTTP ${response.status}`;

                throw new Error(message);
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
    progressBar: document.getElementById("progress-bar"),
    stepCounter: document.getElementById("step-counter"),
    stepName: document.getElementById("step-name"),
    viewCurrentStepBtn: document.getElementById("view-current-step-btn"),
    viewCurrentTicketBtn: document.getElementById("view-current-ticket-btn"),
    busyIndicator: document.getElementById("busy-indicator"),
    stopBtn: document.getElementById("stop-btn"),
    resetBtn: document.getElementById("reset-btn"),
    newProjectBtn: document.getElementById("new-project-btn"),
    switchProjectBtn: document.getElementById("switch-project-btn"),
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
    llmSetupDetails: document.getElementById("llm-setup-details"),
    llmSetupSettingsLink: document.getElementById("llm-setup-settings-link"),
    llmSetupRetryBtn: document.getElementById("llm-setup-retry-btn"),
    llmSetupVideoLink: document.getElementById("llm-setup-video-link"),
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
    llmServers: [],
    activeLlmServerId: "",
    activeLlmModelId: "",
    currentLlmModelName: "",
    currentLlmServerName: "",
    llmHealthy: true,
    llmHealthChecked: false,
    llmHealthDetail: "",
    llmHealthBaseUrl: "",
    versionCheckStarted: false,
    isCurrentStepTicketIteration: false,
    ticketHeaderStatus: null,
    ticketProgress: null,
    skipOptions: null
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

        const markdownHtml = renderAgentMarkdown(step.content || "");
        const body = markdownHtml
            ? `<article class="markdown-content text-slate-800">${markdownHtml}</article>`
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

        const markdownHtml = renderAgentMarkdown(step.content || "");
        const body = markdownHtml
            ? `<article class="markdown-content text-slate-800">${markdownHtml}</article>`
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
}

function applyHydration(payload) {
    resetLocalState();
    clearContainers();

    const history = Array.isArray(payload?.history) ? payload.history : [];

    if (history.length === 0) {
        renderEmptyState();
    } else {
        history.forEach((message) => {
            handleMessage(message, { replay: true });
        });
    }

    applySnapshot(payload?.snapshot);
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

function updateBusyState(isBusy) {
    const isNewProjectMode = state.showProjectConfigPanel;
    const hasSelectedProjectForNewRun = Boolean((state.selectedProjectName || state.targetProjectName || "").trim());
    const hasSelectedWorkflowTypeForNewRun = Boolean(state.selectedWorkflowTypeId);
    const hasPausedStepContext = !state.workflowRunning && Number(state.currentStep || 0) > 0;
    const isLlmHealthy = state.llmHealthy !== false;
    const canSendMessage = !isBusy && (
        (state.awaitingUserInput && (state.workflowRunning || state.canResume)) ||
        (!state.awaitingUserInput && state.canResume && !state.workflowRunning) ||
        (!state.awaitingUserInput && hasPausedStepContext)
    ) && isLlmHealthy;
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
    const canSkip = !isBusy && hasWorkflowState;
    const showContinueButton = hasWorkflowState && (hasNextStep || hasTicketContinuation);
    const showSkipButton = hasWorkflowState;
    const hasWorkflowTypes = state.workflowTypes.length > 0;
    const hasWorkflowSelection = !hasWorkflowTypes || Boolean(state.selectedWorkflowTypeId);
    const canStart = isNewProjectMode
        ? (!isBusy && hasSelectedProjectForNewRun && hasSelectedWorkflowTypeForNewRun && isLlmHealthy)
        : (!state.workflowRunning &&
            !state.awaitingUserInput &&
            !state.canResume &&
            isLlmHealthy);
    const hideStartButton = (state.workflowRunning || state.canResume || state.awaitingUserInput || hasTicketContinuation) && !isNewProjectMode;
    const shouldHideConfigByWorkflow = state.workflowRunning || state.canResume || hasTicketContinuation;
    const hideConfigPanel = shouldHideConfigByWorkflow && !state.showProjectConfigPanel;
    const projectSelectionDisabled = ((state.workflowRunning || state.canResume) && !isNewProjectMode) || state.busy;
    const projectCreateDisabled = (state.workflowRunning && !isNewProjectMode) || state.busy;
    const hasProjectOptions = state.projects.length > 0;
    const canSwitchProject = hasProjectOptions && !isBusy;

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
    ui.sendBtn.disabled = !canSendMessage;
    ui.uploadBtn.disabled = !canSendMessage;
    ui.continueBtn.disabled = !canContinue;
    if (ui.skipBtn) {
        ui.skipBtn.disabled = !canSkip;
    }
    ui.stopBtn.disabled = !state.workflowRunning;
    ui.continueBtn.classList.toggle("hidden", !showContinueButton);
    if (ui.skipBtn) {
        ui.skipBtn.classList.toggle("hidden", !showSkipButton);
    }
    ui.resetBtn.classList.toggle("hidden", !hasWorkflowState || isNewProjectMode);
    if (ui.newProjectBtn) {
        ui.newProjectBtn.classList.toggle("hidden", !hasWorkflowState || !hideStartButton);
    }
    ui.switchProjectBtn.classList.toggle("hidden", !hasProjectOptions || isNewProjectMode);
    ui.startBtn.classList.toggle("hidden", hideStartButton);
    ui.startBtn.disabled = !canStart;
    ui.startBtn.textContent = "Start Workflow";
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
    ui.switchProjectBtn.disabled = !canSwitchProject;
    ui.llmServerSelect.disabled = state.busy;
    ui.llmModelInput.disabled = state.busy;
    if (hideConfigPanel) {
        setRequiredFieldValidation(false, false);
    }
    if (ui.newProjectBtn) {
        ui.newProjectBtn.disabled = isBusy;
        ui.newProjectBtn.textContent = state.showProjectConfigPanel ? "Close New Workflow" : "New Workflow";
    }

    syncWorkflowTypeHelp(hasWorkflowTypes, hasWorkflowSelection);
    syncProjectHelp();
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

    syncWorkflowTypePicker();
    updateBusyState(state.busy);
}

function applyProjects(payload) {
    state.projects = Array.isArray(payload?.projects) ? payload.projects : [];

    if (!state.projects.some((project) => project.name === state.selectedProjectName)) {
        state.selectedProjectName = "";
        state.selectedProjectDirectory = "";
    }

    if (!state.projects.some((project) => project.name === state.targetProjectName)) {
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

    const markdownHtml = markedLib.parse(text, { breaks: true, gfm: true });
    return purify.sanitize(markdownHtml, {
        USE_PROFILES: { html: true },
        ALLOWED_ATTR: ["href", "target", "rel", "title", "class"]
    });
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
    const markdownHtml = role === "agent" ? renderAgentMarkdown(content) : null;
    if (markdownHtml) {
        contentDiv.className = "markdown-content";
        contentDiv.innerHTML = markdownHtml;
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

        default:
            console.log("[WS] Unknown message type:", type);
    }
}

async function hydrateFromServer() {
    try {
        const [workflowState, workflowTypes, projects, llmOptions] = await Promise.all([
            apiClient.getWorkflowState(),
            apiClient.getWorkflowTypes(),
            apiClient.getProjects(),
            apiClient.getLlmOptions()
        ]);
        applyHydration(workflowState);
        applyWorkflowTypes(workflowTypes);
        applyProjects(projects);
        applyLlmOptions(llmOptions);
        await refreshLlmHealth(true);
    } catch (error) {
        console.error("[UI] Failed to hydrate workflow state:", error);
        renderEmptyState();
        updateStatus("Disconnected", "red");
        await Promise.all([loadWorkflowTypes(), loadProjects(), loadLlmOptions()]);
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
    if (state.llmHealthy === false) {
        openLlmSetupModal(state.llmHealthDetail || "LLM server is not reachable. Configure it in Settings.");
        logTerminal("[ERROR] Configure an LLM server in Settings before starting.", "red");
        updateBusyState(state.busy);
        return;
    }

    const requestedWorkflowTypeId = state.selectedWorkflowTypeId || "";
    const requestedWorkflowTypeName = state.selectedWorkflowTypeName || "";
    let workflowTypeChanged = state.canResume &&
        Boolean(requestedWorkflowTypeId) &&
        Boolean(state.resumeWorkflowTypeId) &&
        requestedWorkflowTypeId !== state.resumeWorkflowTypeId;
    let forceNewWorkflow = state.showProjectConfigPanel || workflowTypeChanged;

    if (state.awaitingUserInput && state.canResume && !forceNewWorkflow) {
        logTerminal("[SYSTEM] Workflow is already waiting for input. Send a message or click Next Step.", "orange");
        updateBusyState(state.busy);
        return;
    }

    if (!state.canResume || forceNewWorkflow) {
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
            forceNewWorkflow = state.showProjectConfigPanel || workflowTypeChanged;
        }
    } else {
        setRequiredFieldValidation(false, false);
    }

    try {
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

        if (forceNewWorkflow && state.canResume) {
            await apiClient.resetWorkflow();
            handleMessage({ type: "workflow_reset", payload: {}, timestamp: new Date().toISOString() });
        }

        state.showProjectConfigPanel = false;
        ui.startBtn.textContent = "Starting...";
        ui.startBtn.disabled = true;
        await apiClient.startWorkflow((state.canResume && !forceNewWorkflow) ? null : requestedWorkflowTypeId || null);
        logTerminal((state.canResume && !forceNewWorkflow)
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

    await loadSwitchProjectModal();
});

if (ui.newProjectBtn) {
    ui.newProjectBtn.addEventListener("click", () => {
        if (state.busy) {
            return;
        }

        state.showProjectConfigPanel = !state.showProjectConfigPanel;
        updateBusyState(state.busy);

        if (state.showProjectConfigPanel) {
            if (state.selectedWorkflowTypeId) {
                ui.projectSelect?.focus();
            } else {
                ui.workflowTypeSelect?.focus();
            }
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
