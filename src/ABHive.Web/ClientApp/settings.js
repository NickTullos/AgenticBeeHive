const ui = {
    form: document.getElementById("settings-form"),
    reloadBtn: document.getElementById("reload-btn"),
    saveBtn: document.getElementById("save-btn"),
    saveStatus: document.getElementById("save-status"),
    testConnectionBtn: document.getElementById("test-connection-btn"),
    testConnectionStatus: document.getElementById("test-connection-status"),
    testModelBtn: document.getElementById("test-model-btn"),
    testModelStatus: document.getElementById("test-model-status"),
    addServerBtn: document.getElementById("add-server-btn"),
    llmServersContainer: document.getElementById("llm-servers-container"),
    activeServerSelect: document.getElementById("active-llm-server-id"),
    activeModelSelect: document.getElementById("active-llm-model-int"),
    telegramEnabled: document.getElementById("telegram-enabled"),
    telegramBotToken: document.getElementById("telegram-bot-token"),
    telegramBotPasswordWrapper: null,
    telegramBotToggle: null,
    telegramChatId: document.getElementById("telegram-chat-id"),
    telegramPollTimeoutSeconds: document.getElementById("telegram-poll-timeout-seconds"),
    telegramSwitchContextMessageCount: document.getElementById("telegram-switch-context-message-count"),
    stepsDirectory: document.getElementById("steps-directory"),
    workflowTypesDirectory: document.getElementById("workflow-types-directory"),
    logFilePath: document.getElementById("log-file-int"),
    defaultToolTimeoutMs: document.getElementById("default-tool-timeout-ms"),
    projectRootDirectoryText: document.getElementById("project-root-directory-text"),
    browseProjectBtn: document.getElementById("browse-project-btn"),
    bashEnabled: document.getElementById("bash-enabled"),
    webFetchEnabled: document.getElementById("web-fetch-enabled"),
    readFileEnabled: document.getElementById("read-file-enabled"),
    writeFileEnabled: document.getElementById("write-file-enabled"),
    // LLM Generation Parameters UI elements
    llmTemperature: document.getElementById("llm-temperature"),
    llmTemperatureValue: document.getElementById("llm-temperature-value"),
    llmTopP: document.getElementById("llm-top-p"),
    llmTopPValue: document.getElementById("llm-top-p-value"),
    llmTopK: document.getElementById("llm-top-k"),
    llmMaxTokens: document.getElementById("llm-max-tokens"),
    llmFrequencyPenalty: document.getElementById("llm-frequency-penalty"),
    llmFrequencyPenaltyValue: document.getElementById("llm-frequency-penalty-value"),
    llmPresencePenalty: document.getElementById("llm-presence-penalty"),
    llmPresencePenaltyValue: document.getElementById("llm-presence-penalty-value"),
    llmStopSequences: document.getElementById("llm-stop-sequences"),
    llmResetBtn: document.getElementById("llm-reset-btn")
};

// LLM generation parameter defaults
const LLM_PARAM_DEFAULTS = {
    llmTemperature: 0.7,
    llmTopP: 1.0,
    llmTopK: 0,
    llmMaxTokens: 0,
    llmFrequencyPenalty: 0,
    llmPresencePenalty: 0,
    llmStopSequences: ""
};

const state = {
    llmServers: [],
    activeLlmServerId: "",
    activeLlmModelId: ""
};

function setBusy(isBusy) {
    ui.saveBtn.disabled = isBusy;
    ui.reloadBtn.disabled = isBusy;
    ui.testConnectionBtn.disabled = isBusy;
    if (ui.testModelBtn) {
        ui.testModelBtn.disabled = isBusy;
    }
    ui.addServerBtn.disabled = isBusy;
}

function setProjectRootValidationState(valid) {
    ui.projectRootDirectoryText.classList.toggle("border-red-500", !valid);
    ui.projectRootDirectoryText.classList.toggle("ring-2", !valid);
    ui.projectRootDirectoryText.classList.toggle("ring-red-500", !valid);
}

function showStatus(kind, lines) {
    const classMap = {
        success: "border-green-300 bg-green-50 text-green-800",
        warning: "border-amber-300 bg-amber-50 text-amber-900",
        error: "border-red-300 bg-red-50 text-red-800"
    };

    ui.saveStatus.className = `rounded border px-3 py-2 text-sm ${classMap[kind] ?? classMap.success}`;
    ui.saveStatus.innerHTML = lines.map((line) => `<div>${escapeHtml(line)}</div>`).join("");
    ui.saveStatus.classList.remove("hidden");
}

function hideStatus() {
    ui.saveStatus.classList.add("hidden");
}

function escapeHtml(value) {
    return (value || "")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
}

function togglePasswordVisibility(inputElement, toggleButton) {
    const isPassword = inputElement.type === "password";
    console.log("Toggling password visibility, current type:", inputElement.type, "isPassword:", isPassword);
    
    // Toggle input type
    inputElement.type = isPassword ? "text" : "password";
    
    // Toggle eye icon
    const pathElements = toggleButton.querySelectorAll("path");
    if (pathElements.length >= 2) {
        if (isPassword) {
            // Show eye (visible)
            console.log("Switching to visible (eye)");
            pathElements[0].setAttribute("d", "M15 12a3 3 0 11-6 0 3 3 0 016 0z");
            pathElements[1].setAttribute("d", "M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z");
        } else {
            // Show eye-slash (hidden)
            console.log("Switching to hidden (eye-slash)");
            pathElements[0].setAttribute("d", "M13.875 18.825A10.05 10.05 0 0112 19c-4.478 0-8.268-2.943-9.543-7a9.97 9.97 0 011.563-3.029m5.858.908a3 3 0 114.243 4.243M9.878 9.878l4.242 4.242M9.88 9.88l-3.29-3.29m7.532 7.532l3.29 3.29M3 3l3.59 3.59m0 0A9.953 9.953 0 0112 5c4.478 0 8.268 2.943 9.543 7a10.025 10.025 0 01-4.132 5.411m0 0L21 21");
            pathElements[1].setAttribute("d", "M13.875 18.825A10.05 10.05 0 0112 19c-4.478 0-8.268-2.943-9.543-7a9.97 9.97 0 011.563-3.029m5.858.908a3 3 0 114.243 4.243M9.878 9.878l4.242 4.242M9.88 9.88l-3.29-3.29m7.532 7.532l3.29 3.29M3 3l3.59 3.59m0 0A9.953 9.953 0 0112 5c4.478 0 8.268 2.943 9.543 7a10.025 10.025 0 01-4.132 5.411m0 0L21 21");
        }
    }
}

function makeId(prefix, nameHint = "") {
    const normalized = (nameHint || "").toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "");
    const suffix = Math.floor(Math.random() * 100000).toString(36);
    return `${prefix}-${normalized || "item"}-${suffix}`;
}

function getServerById(serverId) {
    return state.llmServers.find((server) => server.id === serverId) || null;
}

function ensureValidActiveSelection() {
    if (state.llmServers.length === 0) {
        state.activeLlmServerId = "";
        state.activeLlmModelId = "";
        return;
    }

    if (!state.llmServers.some((server) => server.id === state.activeLlmServerId)) {
        state.activeLlmServerId = state.llmServers[0].id;
    }

    const activeServer = getServerById(state.activeLlmServerId);
    if (!activeServer) {
        return;
    }

    if (!Array.isArray(activeServer.models) || activeServer.models.length === 0) {
        activeServer.models = [{ id: makeId("model"), name: "default" }];
    }

    if (!activeServer.models.some((model) => model.id === activeServer.defaultModelId)) {
        activeServer.defaultModelId = activeServer.models[0].id;
    }

    if (!activeServer.models.some((model) => model.id === state.activeLlmModelId)) {
        state.activeLlmModelId = activeServer.defaultModelId;
    }
}

function renderActiveSelectors() {
    ensureValidActiveSelection();

    ui.activeServerSelect.innerHTML = "";
    state.llmServers.forEach((server) => {
        const option = document.createElement("option");
        option.value = server.id;
        option.textContent = server.name || server.id;
        ui.activeServerSelect.appendChild(option);
    });

    if (state.activeLlmServerId) {
        ui.activeServerSelect.value = state.activeLlmServerId;
    }

    ui.activeModelSelect.innerHTML = "";
    const activeServer = getServerById(state.activeLlmServerId);
    const models = Array.isArray(activeServer?.models) ? activeServer.models : [];
    models.forEach((model) => {
        const option = document.createElement("option");
        option.value = model.id;
        option.textContent = model.name;
        ui.activeModelSelect.appendChild(option);
    });

    if (state.activeLlmModelId) {
        ui.activeModelSelect.value = state.activeLlmModelId;
    }
}

function renderServers() {
    ensureValidActiveSelection();
    ui.llmServersContainer.innerHTML = "";

    if (state.llmServers.length === 0) {
        const empty = document.createElement("div");
        empty.className = "rounded border border-dashed border-gray-300 bg-gray-50 p-3 text-sm text-gray-600";
        empty.textContent = "No LLM servers configured yet.";
        ui.llmServersContainer.appendChild(empty);
        renderActiveSelectors();
        return;
    }

    state.llmServers.forEach((server, serverIndex) => {
        const card = document.createElement("div");
        card.className = "rounded-lg border border-gray-200 bg-gray-50 p-4";
        card.dataset.serverId = server.id;

        const modelsHtml = (server.models || []).map((model, modelIndex) => `
            <div class="grid grid-cols-1 md:grid-cols-6 gap-2 items-end" data-model-id="${escapeHtml(model.id)}">
                <label class="md:col-span-2 block text-sm">
                    <span class="text-xs text-gray-600">Model ID</span>
                    <input type="text" data-field="model-id" data-server-id="${escapeHtml(server.id)}" data-model-index="${modelIndex}" class="mt-1 w-full rounded border border-gray-300 px-2 py-1.5 text-sm" value="${escapeHtml(model.id)}">
                </label>
                <label class="md:col-span-3 block text-sm">
                    <span class="text-xs text-gray-600">Model Name</span>
                    <input type="text" data-field="model-name" data-server-id="${escapeHtml(server.id)}" data-model-index="${modelIndex}" class="mt-1 w-full rounded border border-gray-300 px-2 py-1.5 text-sm" value="${escapeHtml(model.name)}">
                </label>
                <button type="button" data-action="remove-model" data-server-id="${escapeHtml(server.id)}" data-model-index="${modelIndex}" class="rounded bg-red-600 px-2 py-1.5 text-xs text-white hover:bg-red-700 transition">Remove</button>
            </div>
        `).join("");

        card.innerHTML = `
            <div class="flex items-center justify-between gap-2 mb-3">
                <h3 class="font-semibold text-gray-800">Server ${serverIndex + 1}</h3>
                <button type="button" data-action="remove-server" data-server-id="${escapeHtml(server.id)}" class="rounded bg-red-600 px-2 py-1.5 text-xs text-white hover:bg-red-700 transition">Remove Server</button>
            </div>
            <div class="grid grid-cols-1 md:grid-cols-2 gap-3 mb-3">
                <label class="block text-sm">
                    <span class="text-sm font-medium text-gray-700">Server ID</span>
                    <input type="text" data-field="server-id" data-server-id="${escapeHtml(server.id)}" class="mt-1 w-full rounded border border-gray-300 px-3 py-2 text-sm" value="${escapeHtml(server.id)}">
                </label>
                <label class="block text-sm">
                    <span class="text-sm font-medium text-gray-700">Server Name</span>
                    <input type="text" data-field="server-name" data-server-id="${escapeHtml(server.id)}" class="mt-1 w-full rounded border border-gray-300 px-3 py-2 text-sm" value="${escapeHtml(server.name)}">
                </label>
                <label class="block text-sm">
                    <span class="text-sm font-medium text-gray-700">Base URL</span>
                    <input type="url" data-field="base-url" data-server-id="${escapeHtml(server.id)}" class="mt-1 w-full rounded border border-gray-300 px-3 py-2 text-sm" value="${escapeHtml(server.baseUrl)}">
                </label>
                <label class="block text-sm">
                    <span class="text-sm font-medium text-gray-700">API Key</span>
                    <div id="api-key-wrapper-${escapeHtml(server.id)}" class="mt-1 relative w-full">
                        <input type="password" autocomplete="off" data-field="api-key" data-server-id="${escapeHtml(server.id)}" class="w-full rounded border border-gray-300 px-3 py-2 pr-10 text-sm" value="${escapeHtml(server.apiKey || "")}">
                    </div>
                </label>
            </div>
            <div class="mb-3">
                <label class="block text-sm">
                    <span class="text-sm font-medium text-gray-700">Default Model for This Server</span>
                    <select data-field="default-model-id" data-server-id="${escapeHtml(server.id)}" class="mt-1 w-full rounded border border-gray-300 px-3 py-2 text-sm">
                        ${(server.models || []).map((model) => `<option value="${escapeHtml(model.id)}" ${model.id === server.defaultModelId ? "selected" : ""}>${escapeHtml(model.name)}</option>`).join("")}
                    </select>
                </label>
            </div>
            <div class="space-y-2">
                ${modelsHtml}
            </div>
            <div class="mt-3">
                <button type="button" data-action="fetch-models" data-server-id="${escapeHtml(server.id)}" class="rounded bg-indigo-600 px-3 py-2 text-xs text-white hover:bg-indigo-500 transition mr-2">Fetch Models</button>
                <button type="button" data-action="add-model" data-server-id="${escapeHtml(server.id)}" class="rounded bg-slate-600 px-3 py-2 text-xs text-white hover:bg-slate-500 transition">Add Model</button>
            </div>
        `;

        ui.llmServersContainer.appendChild(card);
        
        // Set up API key toggle for this server
        const apiKeyWrapper = document.getElementById(`api-key-wrapper-${server.id}`);
        if (apiKeyWrapper) {
            const input = card.querySelector(`input[data-field="api-key"][data-server-id="${server.id}"]`);
            const toggleBtn = document.createElement("button");
            toggleBtn.type = "button";
            toggleBtn.className = "absolute right-0 top-0 h-full px-3 text-gray-500 hover:text-gray-700 focus:outline-none transition";
            toggleBtn.innerHTML = `
                <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
                </svg>
            `;
            toggleBtn.addEventListener("click", () => {
                console.log(`Toggling API key for server ${server.id}`);
                togglePasswordVisibility(input, toggleBtn);
            });
            apiKeyWrapper.appendChild(toggleBtn);
        }
    });

    renderActiveSelectors();
}

function applySettings(settings) {
    state.llmServers = Array.isArray(settings.llmServers) ? settings.llmServers.map((server, serverIndex) => ({
        id: (server.id || makeId("server", server.name || `server-${serverIndex + 1}`)).trim(),
        name: (server.name || `Server ${serverIndex + 1}`).trim(),
        baseUrl: (server.baseUrl || "").trim(),
        apiKey: server.apiKey || "",
        defaultModelId: (server.defaultModelId || "").trim(),
        models: (Array.isArray(server.models) ? server.models : []).map((model, modelIndex) => ({
            id: (model.id || makeId("model", model.name || `model-${modelIndex + 1}`)).trim(),
            name: (model.name || "default").trim()
        }))
    })) : [];

    if (state.llmServers.length === 0 && settings.lmStudioUrl && settings.modelName) {
        state.llmServers = [{
            id: "default-server",
            name: "Default Server",
            baseUrl: settings.lmStudioUrl,
            apiKey: settings.llmApiKey || "",
            defaultModelId: "default-model",
            models: [{ id: "default-model", name: settings.modelName }]
        }];
    }

    state.activeLlmServerId = settings.activeLlmServerId || "";
    state.activeLlmModelId = settings.activeLlmModelId || "";

    ui.telegramEnabled.checked = Boolean(settings.telegramEnabled);
    ui.telegramBotToken.value = settings.telegramBotToken ?? "";
    ui.telegramChatId.value = settings.telegramChatId ?? 0;
    ui.telegramPollTimeoutSeconds.value = settings.telegramPollTimeoutSeconds ?? 20;
    ui.telegramSwitchContextMessageCount.value = settings.telegramSwitchContextMessageCount ?? 5;
    ui.stepsDirectory.value = settings.stepsDirectory ?? "";
    ui.workflowTypesDirectory.value = settings.workflowTypesDirectory ?? "";
    ui.logFilePath.value = settings.logFilePath ?? "";
    ui.defaultToolTimeoutMs.value = settings.defaultToolTimeoutMs ?? 180000;
    ui.projectRootDirectoryText.value = settings.projectRootDirectory ?? "./projects";
    ui.bashEnabled.checked = Boolean(settings.bashEnabled);
    ui.webFetchEnabled.checked = Boolean(settings.webFetchEnabled);
    ui.readFileEnabled.checked = Boolean(settings.readFileEnabled);
    ui.writeFileEnabled.checked = Boolean(settings.writeFileEnabled);

    // Apply LLM generation parameters (graceful fallback to defaults if not present)
    if (ui.llmTemperature) {
        ui.llmTemperature.value = settings.llmTemperature ?? LLM_PARAM_DEFAULTS.llmTemperature;
    }
    if (ui.llmTopP) {
        ui.llmTopP.value = settings.llmTopP ?? LLM_PARAM_DEFAULTS.llmTopP;
    }
    if (ui.llmTopK) {
        ui.llmTopK.value = settings.llmTopK ?? LLM_PARAM_DEFAULTS.llmTopK;
    }
    if (ui.llmMaxTokens) {
        ui.llmMaxTokens.value = settings.llmMaxTokens ?? LLM_PARAM_DEFAULTS.llmMaxTokens;
    }
    if (ui.llmFrequencyPenalty) {
        ui.llmFrequencyPenalty.value = settings.llmFrequencyPenalty ?? LLM_PARAM_DEFAULTS.llmFrequencyPenalty;
    }
    if (ui.llmPresencePenalty) {
        ui.llmPresencePenalty.value = settings.llmPresencePenalty ?? LLM_PARAM_DEFAULTS.llmPresencePenalty;
    }
    if (ui.llmStopSequences) {
        ui.llmStopSequences.value = settings.llmStopSequences ?? LLM_PARAM_DEFAULTS.llmStopSequences;
    }

    // Update slider value displays
    updateLlmSliderDisplays();

    renderServers();
    
    // Set up Telegram token toggle
    if (!ui.telegramBotPasswordWrapper) {
        const wrapper = document.getElementById("telegram-token-wrapper");
        
        const input = document.getElementById("telegram-bot-token");
        input.type = "password";
        
        ui.telegramBotPasswordWrapper = wrapper;
        
        const toggleBtn = document.createElement("button");
        toggleBtn.type = "button";
        toggleBtn.id = "toggle-telegram-token";
        toggleBtn.className = "absolute right-0 top-0 h-full px-3 text-gray-500 hover:text-gray-700 focus:outline-none transition";
        toggleBtn.innerHTML = `
            <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
            </svg>
        `;
        
        toggleBtn.addEventListener("click", () => {
            console.log("Toggle button clicked!");
            togglePasswordVisibility(ui.telegramBotToken, toggleBtn);
        });
        
        wrapper.appendChild(toggleBtn);
        ui.telegramBotToggle = toggleBtn;
    }
}

/**
 * Update the live value display for all LLM parameter sliders.
 */
function updateLlmSliderDisplays() {
    if (ui.llmTemperature && ui.llmTemperatureValue) {
        ui.llmTemperatureValue.textContent = ui.llmTemperature.value;
    }
    if (ui.llmTopP && ui.llmTopPValue) {
        ui.llmTopPValue.textContent = ui.llmTopP.value;
    }
    if (ui.llmFrequencyPenalty && ui.llmFrequencyPenaltyValue) {
        ui.llmFrequencyPenaltyValue.textContent = ui.llmFrequencyPenalty.value;
    }
    if (ui.llmPresencePenalty && ui.llmPresencePenaltyValue) {
        ui.llmPresencePenaltyValue.textContent = ui.llmPresencePenalty.value;
    }
}

/**
 * Reset all LLM generation parameter inputs to their default values.
 */
function resetLlmParamsToDefaults() {
    if (ui.llmTemperature) ui.llmTemperature.value = LLM_PARAM_DEFAULTS.llmTemperature;
    if (ui.llmTopP) ui.llmTopP.value = LLM_PARAM_DEFAULTS.llmTopP;
    if (ui.llmTopK) ui.llmTopK.value = LLM_PARAM_DEFAULTS.llmTopK;
    if (ui.llmMaxTokens) ui.llmMaxTokens.value = LLM_PARAM_DEFAULTS.llmMaxTokens;
    if (ui.llmFrequencyPenalty) ui.llmFrequencyPenalty.value = LLM_PARAM_DEFAULTS.llmFrequencyPenalty;
    if (ui.llmPresencePenalty) ui.llmPresencePenalty.value = LLM_PARAM_DEFAULTS.llmPresencePenalty;
    if (ui.llmStopSequences) ui.llmStopSequences.value = LLM_PARAM_DEFAULTS.llmStopSequences;
    updateLlmSliderDisplays();
}

function addServer() {
    state.llmServers.push({
        id: makeId("server"),
        name: "New Server",
        baseUrl: "http://127.0.0.1:1234",
        apiKey: "",
        defaultModelId: "",
        models: [{ id: makeId("model"), name: "default" }]
    });

    if (!state.activeLlmServerId) {
        state.activeLlmServerId = state.llmServers[0].id;
    }

    renderServers();
}

function removeServer(serverId) {
    state.llmServers = state.llmServers.filter((server) => server.id !== serverId);
    renderServers();
}

function addModel(serverId) {
    const server = getServerById(serverId);
    if (!server) {
        return;
    }

    server.models.push({ id: makeId("model"), name: "new-model" });
    if (!server.defaultModelId) {
        server.defaultModelId = server.models[0].id;
    }
    renderServers();
}

function removeModel(serverId, modelIndex) {
    const server = getServerById(serverId);
    if (!server) {
        return;
    }

    if (server.models.length <= 1) {
        showStatus("error", ["Each server must have at least one model."]);
        return;
    }

    const removed = server.models.splice(modelIndex, 1)[0];
    if (removed && removed.id === server.defaultModelId) {
        server.defaultModelId = server.models[0].id;
    }
    renderServers();
}

function updateServerField(serverId, field, value) {
    const server = getServerById(serverId);
    if (!server) {
        return;
    }

    if (field === "server-id") {
        const trimmed = value.trim() || makeId("server");
        const oldId = server.id;
        server.id = trimmed;
        if (state.activeLlmServerId === oldId) {
            state.activeLlmServerId = server.id;
        }
        renderServers();
    } else if (field === "server-name") {
        server.name = value;
        renderActiveSelectors();
    } else if (field === "base-url") {
        server.baseUrl = value;
    } else if (field === "api-key") {
        server.apiKey = value;
    } else if (field === "default-model-id") {
        server.defaultModelId = value;
        if (state.activeLlmServerId === server.id) {
            state.activeLlmModelId = value;
        }
        renderActiveSelectors();
    }
}

function updateModelField(serverId, modelIndex, field, value) {
    const server = getServerById(serverId);
    if (!server || !server.models[modelIndex]) {
        return;
    }

    if (field === "model-id") {
        const oldId = server.models[modelIndex].id;
        server.models[modelIndex].id = value.trim() || makeId("model");
        if (server.defaultModelId === oldId) {
            server.defaultModelId = server.models[modelIndex].id;
        }
        if (state.activeLlmModelId === oldId && state.activeLlmServerId === serverId) {
            state.activeLlmModelId = server.models[modelIndex].id;
        }
        renderServers();
    } else if (field === "model-name") {
        server.models[modelIndex].name = value;
        renderActiveSelectors();
    }
}

function buildPayload() {
    ensureValidActiveSelection();
    const activeServer = getServerById(state.activeLlmServerId);
    const activeModel = activeServer?.models?.find((model) => model.id === state.activeLlmModelId) || activeServer?.models?.[0] || null;

    const llmServers = state.llmServers.map((server) => ({
        id: (server.id || "").trim(),
        name: (server.name || "").trim(),
        baseUrl: (server.baseUrl || "").trim(),
        apiKey: server.apiKey || "",
        defaultModelId: (server.defaultModelId || "").trim(),
        models: (server.models || []).map((model) => ({
            id: (model.id || "").trim(),
            name: (model.name || "").trim()
        }))
    }));

    return {
        llmServers,
        activeLlmServerId: state.activeLlmServerId,
        activeLlmModelId: state.activeLlmModelId,
        lmStudioUrl: activeServer?.baseUrl || "",
        llmApiKey: activeServer?.apiKey || "",
        modelName: activeModel?.name || "",
        telegramEnabled: ui.telegramEnabled.checked,
        telegramBotToken: ui.telegramBotToken.value.trim(),
        telegramChatId: Number(ui.telegramChatId.value || 0),
        telegramPollTimeoutSeconds: Number(ui.telegramPollTimeoutSeconds.value || 0),
        telegramSwitchContextMessageCount: Number(ui.telegramSwitchContextMessageCount.value || 0),
        stepsDirectory: ui.stepsDirectory.value.trim(),
        workflowTypesDirectory: ui.workflowTypesDirectory.value.trim(),
        logFilePath: ui.logFilePath.value.trim(),
        defaultToolTimeoutMs: Number(ui.defaultToolTimeoutMs.value || 0),
        projectRootDirectory: ui.projectRootDirectoryText.value.trim(),
        bashEnabled: ui.bashEnabled.checked,
        webFetchEnabled: ui.webFetchEnabled.checked,
        readFileEnabled: ui.readFileEnabled.checked,
        writeFileEnabled: ui.writeFileEnabled.checked,
        // LLM generation parameters
        llmTemperature: Number(ui.llmTemperature?.value ?? LLM_PARAM_DEFAULTS.llmTemperature),
        llmTopP: Number(ui.llmTopP?.value ?? LLM_PARAM_DEFAULTS.llmTopP),
        llmTopK: Number(ui.llmTopK?.value ?? LLM_PARAM_DEFAULTS.llmTopK),
        llmMaxTokens: Number(ui.llmMaxTokens?.value ?? LLM_PARAM_DEFAULTS.llmMaxTokens),
        llmFrequencyPenalty: Number(ui.llmFrequencyPenalty?.value ?? LLM_PARAM_DEFAULTS.llmFrequencyPenalty),
        llmPresencePenalty: Number(ui.llmPresencePenalty?.value ?? LLM_PARAM_DEFAULTS.llmPresencePenalty),
        llmStopSequences: (ui.llmStopSequences?.value ?? LLM_PARAM_DEFAULTS.llmStopSequences).toString()
    };
}

async function loadSettings() {
    hideStatus();
    setBusy(true);

    try {
        const response = await fetch("/api/settings");
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        const payload = await response.json();
        applySettings(payload.settings ?? {});
    } catch (error) {
        showStatus("error", [`Failed to load settings: ${error.message}`]);
    } finally {
        setBusy(false);
    }
}

async function testConnection() {
    ensureValidActiveSelection();
    const server = getServerById(state.activeLlmServerId);
    if (!server) {
        ui.testConnectionStatus.textContent = "No active server selected.";
        ui.testConnectionStatus.className = "text-sm text-red-700";
        return;
    }

    ui.testConnectionStatus.textContent = "Testing connection...";
    ui.testConnectionStatus.className = "text-sm text-gray-600";
    ui.testConnectionBtn.disabled = true;

    try {
        const response = await fetch("/api/settings/test-connection", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ lmStudioUrl: server.baseUrl, llmApiKey: server.apiKey || "" })
        });

        const payload = await response.json();
        if (!response.ok) {
            throw new Error(payload.message || `HTTP ${response.status}`);
        }

        if (payload.success) {
            ui.testConnectionStatus.textContent = "Connection successful.";
            ui.testConnectionStatus.className = "text-sm text-green-700";
            return;
        }

        const detailText = payload.details ? ` ${payload.details}` : "";
        ui.testConnectionStatus.textContent = `${payload.message}${detailText}`;
        ui.testConnectionStatus.className = "text-sm text-red-700";
    } catch (error) {
        ui.testConnectionStatus.textContent = `Connection test failed: ${error.message}`;
        ui.testConnectionStatus.className = "text-sm text-red-700";
    } finally {
        ui.testConnectionBtn.disabled = false;
    }
}

async function saveSettings() {
    hideStatus();
    setBusy(true);

    try {
        const rootPath = (ui.projectRootDirectoryText.value || "").trim();
        const projectRootValidation = await validateProjectRootDirectory(rootPath);
        if (!projectRootValidation.success) {
            setProjectRootValidationState(false);
            throw new Error(projectRootValidation.message || "Project root directory is invalid.");
        }

        setProjectRootValidationState(true);

        const response = await fetch("/api/settings", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(buildPayload())
        });

        const payload = await response.json();
        if (!response.ok || payload.success === false) {
            throw new Error(payload.message || `HTTP ${response.status}`);
        }

        if (payload.restartRequired) {
            const reasons = Array.isArray(payload.restartReasons) ? payload.restartReasons : [];
            showStatus("warning", [
                "Settings saved.",
                "Server restart required for full effect.",
                ...reasons
            ]);
        } else {
            showStatus("success", [
                "Settings saved.",
                "Changes apply to future workflow runs and future message turns."
            ]);
        }
    } catch (error) {
        showStatus("error", [`Failed to save settings: ${error.message}`]);
    } finally {
        setBusy(false);
    }
}

async function validateProjectRootDirectory(projectRootDirectory) {
    try {
        const response = await fetch("/api/settings/validate-project-root", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ projectRootDirectory })
        });

        const payload = await response.json();
        if (!response.ok) {
            return {
                success: false,
                message: payload.message || `HTTP ${response.status}`
            };
        }

        return {
            success: Boolean(payload.success),
            message: payload.message || "",
            resolvedDirectory: payload.resolvedDirectory || ""
        };
    } catch (error) {
        return {
            success: false,
            message: error.message || "Failed to validate directory."
        };
    }
}

function suggestPathFromPickedFolder(currentPath, folderName) {
    const trimmedCurrent = (currentPath || "").trim();
    const name = (folderName || "").trim();
    if (!name) {
        return trimmedCurrent;
    }

    if (!trimmedCurrent) {
        return `./${name}`;
    }

    const normalized = trimmedCurrent.replaceAll("\\", "/");
    const slashIndex = normalized.lastIndexOf("/");
    if (slashIndex < 0) {
        return `./${name}`;
    }

    const parent = normalized.slice(0, slashIndex);
    return parent ? `${parent}/${name}` : `./${name}`;
}

async function browseProjectDirectory() {
    const currentValue = (ui.projectRootDirectoryText.value || "./projects").trim();

    if (typeof window.showDirectoryPicker === "function") {
        try {
            const handle = await window.showDirectoryPicker({ mode: "read" });
            const suggested = suggestPathFromPickedFolder(currentValue, handle.name);
            ui.projectRootDirectoryText.value = suggested;
            const validation = await validateProjectRootDirectory(suggested);
            setProjectRootValidationState(Boolean(validation.success));

            if (validation.success) {
                showStatus("success", [`Directory selected: ${validation.resolvedDirectory || suggested}`]);
            } else {
                showStatus("warning", [
                    `Selected folder name: ${handle.name}`,
                    "Browser picker does not expose full absolute paths. Confirm or edit the directory path before saving."
                ]);
            }
            return;
        } catch (error) {
            if (error?.name !== "AbortError") {
                showStatus("warning", [`Folder picker unavailable: ${error.message}. Enter path manually.`]);
            }
            return;
        }
    }

    const entered = window.prompt("Enter the projects root directory path:", currentValue);
    if (entered === null) {
        return;
    }

    const trimmed = entered.trim();
    if (!trimmed) {
        return;
    }

    ui.projectRootDirectoryText.value = trimmed;
    const validation = await validateProjectRootDirectory(trimmed);
    setProjectRootValidationState(Boolean(validation.success));
    if (!validation.success) {
        showStatus("error", [validation.message || "Project root directory is invalid."]);
    }
}

async function fetchModelsForServer(serverId) {
    const server = getServerById(serverId);
    if (!server) {
        showStatus("error", ["Cannot fetch models: server not found."]);
        return;
    }

    const baseUrl = (server.baseUrl || "").trim();
    if (!baseUrl) {
        showStatus("error", ["Enter a server Base URL before fetching models."]);
        return;
    }

    setBusy(true);
    hideStatus();

    try {
        const response = await fetch("/api/settings/llm-models", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                llmServerUrl: baseUrl,
                llmApiKey: server.apiKey || ""
            })
        });

        const payload = await response.json();
        if (!response.ok || payload.success === false) {
            const detail = payload.details ? ` ${payload.details}` : "";
            throw new Error(`${payload.message || `HTTP ${response.status}`}${detail}`);
        }

        const incoming = Array.isArray(payload.models) ? payload.models : [];
        const existingIds = new Set((server.models || []).map((model) => model.id));
        let added = 0;

        incoming.forEach((modelName) => {
            const normalized = (modelName || "").trim();
            if (!normalized || existingIds.has(normalized)) {
                return;
            }

            server.models.push({
                id: normalized,
                name: normalized
            });
            existingIds.add(normalized);
            added++;
        });

        if (!server.defaultModelId && server.models.length > 0) {
            server.defaultModelId = server.models[0].id;
        }

        renderServers();
        showStatus("success", [
            `Fetched ${incoming.length} model(s) from ${server.name || server.id}.`,
            added > 0 ? `Added ${added} new model(s) to this server.` : "All fetched models were already in the list."
        ]);
    } catch (error) {
        showStatus("error", [`Failed to fetch models: ${error.message}`]);
    } finally {
        setBusy(false);
    }
}

async function testModel() {
    ensureValidActiveSelection();
    const server = getServerById(state.activeLlmServerId);
    if (!server) {
        if (ui.testModelStatus) {
            ui.testModelStatus.textContent = "No active server selected.";
            ui.testModelStatus.className = "text-sm text-red-700";
        }
        return;
    }

    const model = (server.models || []).find((item) => item.id === state.activeLlmModelId) || null;
    const modelId = (model?.id || state.activeLlmModelId || "").trim();
    const modelName = (model?.name || "").trim();
    if (!modelId) {
        if (ui.testModelStatus) {
            ui.testModelStatus.textContent = "No active model selected.";
            ui.testModelStatus.className = "text-sm text-red-700";
        }
        return;
    }

    if (!ui.testModelBtn || !ui.testModelStatus) {
        return;
    }

    ui.testModelStatus.textContent = "Testing model...";
    ui.testModelStatus.className = "text-sm text-gray-600";
    ui.testModelBtn.disabled = true;

    try {
        const response = await fetch("/api/settings/test-model", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                llmServerUrl: server.baseUrl,
                llmApiKey: server.apiKey || "",
                modelId,
                modelName
            })
        });

        const payload = await response.json();
        if (!response.ok) {
            throw new Error(payload.message || `HTTP ${response.status}`);
        }

        if (payload.success) {
            const endpoint = payload.endpoint ? ` via ${payload.endpoint}` : "";
            const usedModel = payload.modelUsed ? ` (${payload.modelUsed})` : "";
            const snippet = payload.outputSnippet ? `: ${payload.outputSnippet}` : "";
            ui.testModelStatus.textContent = `Model test OK${endpoint}${usedModel}${snippet}`;
            ui.testModelStatus.className = "text-sm text-green-700";
            return;
        }

        const detailText = payload.details ? ` ${payload.details}` : "";
        ui.testModelStatus.textContent = `${payload.message}${detailText}`;
        ui.testModelStatus.className = "text-sm text-red-700";
    } catch (error) {
        ui.testModelStatus.textContent = `Model test failed: ${error.message}`;
        ui.testModelStatus.className = "text-sm text-red-700";
    } finally {
        ui.testModelBtn.disabled = false;
    }
}

ui.form.addEventListener("submit", async (event) => {
    event.preventDefault();
    await saveSettings();
});

ui.reloadBtn.addEventListener("click", async () => {
    await loadSettings();
});

ui.testConnectionBtn.addEventListener("click", async () => {
    await testConnection();
});

if (ui.testModelBtn) {
    ui.testModelBtn.addEventListener("click", async () => {
        await testModel();
    });
}

ui.addServerBtn.addEventListener("click", () => {
    addServer();
});

ui.activeServerSelect.addEventListener("change", (event) => {
    state.activeLlmServerId = event.target.value || "";
    ensureValidActiveSelection();
    renderActiveSelectors();
});

ui.activeModelSelect.addEventListener("change", (event) => {
    state.activeLlmModelId = event.target.value || "";
    ensureValidActiveSelection();
    renderActiveSelectors();
});

ui.llmServersContainer.addEventListener("click", (event) => {
    const target = event.target;
    if (!(target instanceof HTMLElement)) {
        return;
    }

    const action = target.dataset.action;
    if (action === "remove-server") {
        removeServer(target.dataset.serverId || "");
    } else if (action === "fetch-models") {
        void fetchModelsForServer(target.dataset.serverId || "");
    } else if (action === "add-model") {
        addModel(target.dataset.serverId || "");
    } else if (action === "remove-model") {
        const serverId = target.dataset.serverId || "";
        const modelIndex = Number(target.dataset.modelIndex || -1);
        if (!Number.isNaN(modelIndex) && modelIndex >= 0) {
            removeModel(serverId, modelIndex);
        }
    }
});

ui.llmServersContainer.addEventListener("input", (event) => {
    const target = event.target;
    if (!(target instanceof HTMLInputElement)) {
        return;
    }

    const field = target.dataset.field;
    const serverId = target.dataset.serverId || "";
    if (!field || !serverId) {
        return;
    }

    if (field === "server-id" || field === "model-id") {
        return;
    }

    if (field === "model-id" || field === "model-name") {
        const modelIndex = Number(target.dataset.modelIndex || -1);
        if (!Number.isNaN(modelIndex) && modelIndex >= 0) {
            updateModelField(serverId, modelIndex, field, target.value);
        }
        return;
    }

    updateServerField(serverId, field, target.value);
});

ui.llmServersContainer.addEventListener("change", (event) => {
    const target = event.target;
    if (!(target instanceof HTMLInputElement || target instanceof HTMLSelectElement)) {
        return;
    }

    const field = target.dataset.field;
    const serverId = target.dataset.serverId || "";
    if (!field || !serverId) {
        return;
    }

    if (field === "model-id" || field === "model-name") {
        const modelIndex = Number(target.dataset.modelIndex || -1);
        if (!Number.isNaN(modelIndex) && modelIndex >= 0) {
            updateModelField(serverId, modelIndex, field, target.value);
        }
        return;
    }

    updateServerField(serverId, field, target.value);
});

ui.browseProjectBtn.addEventListener("click", async () => {
    await browseProjectDirectory();
});

ui.projectRootDirectoryText.addEventListener("blur", async () => {
    const value = (ui.projectRootDirectoryText.value || "").trim();
    if (!value) {
        setProjectRootValidationState(false);
        return;
    }

    const validation = await validateProjectRootDirectory(value);
    setProjectRootValidationState(Boolean(validation.success));
});

// Wire LLM parameter slider input events for live value display
if (ui.llmTemperature) {
    ui.llmTemperature.addEventListener("input", updateLlmSliderDisplays);
}
if (ui.llmTopP) {
    ui.llmTopP.addEventListener("input", updateLlmSliderDisplays);
}
if (ui.llmFrequencyPenalty) {
    ui.llmFrequencyPenalty.addEventListener("input", updateLlmSliderDisplays);
}
if (ui.llmPresencePenalty) {
    ui.llmPresencePenalty.addEventListener("input", updateLlmSliderDisplays);
}

// Wire LLM reset to defaults button
if (ui.llmResetBtn) {
    ui.llmResetBtn.addEventListener("click", () => {
        resetLlmParamsToDefaults();
    });
}

void loadSettings();
initAccordion();

// Accordion functionality
function toggleAccordion(header) {
    const content = header.nextElementSibling;
    const icon = header.querySelector('.accordion-icon');
    
    if (content.classList.contains('hidden')) {
        content.classList.remove('hidden');
        header.setAttribute('aria-expanded', 'true');
        if (icon) {
            icon.style.transform = 'rotate(180deg)';
        }
    } else {
        content.classList.add('hidden');
        header.setAttribute('aria-expanded', 'false');
        if (icon) {
            icon.style.transform = 'rotate(0deg)';
        }
    }
}

function initAccordion() {
    const headers = document.querySelectorAll('.accordion-header');
    headers.forEach(header => {
        header.addEventListener('click', () => toggleAccordion(header));
    });
}
