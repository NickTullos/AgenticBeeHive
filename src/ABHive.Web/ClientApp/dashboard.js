const ui = {
    refreshBtn: document.getElementById("refresh-btn"),
    lastUpdated: document.getElementById("last-updated"),
    dashboardStatus: document.getElementById("dashboard-status"),
    projectList: document.getElementById("project-list"),
    dashboardContentModal: document.getElementById("dashboard-content-modal"),
    dashboardContentTitle: document.getElementById("dashboard-content-title"),
    dashboardContentSubtitle: document.getElementById("dashboard-content-subtitle"),
    dashboardContentBody: document.getElementById("dashboard-content-body"),
    dashboardContentCloseBtn: document.getElementById("dashboard-content-close-btn"),
    summaryTotal: document.getElementById("summary-total"),
    summaryPending: document.getElementById("summary-pending"),
    summaryPlanned: document.getElementById("summary-planned"),
    summaryInProgress: document.getElementById("summary-inprogress"),
    summaryCompleted: document.getElementById("summary-completed")
};

const state = {
    projects: []
};

function escapeHtml(value) {
    return (value || "")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll("\"", "&quot;")
        .replaceAll("'", "&#39;");
}

function badgeClass(status) {
    switch ((status || "").toLowerCase()) {
        case "completed":
            return "bg-emerald-100 text-emerald-800 border-emerald-200";
        case "in progress":
            return "bg-cyan-100 text-cyan-800 border-cyan-200";
        case "planned":
            return "bg-indigo-100 text-indigo-800 border-indigo-200";
        default:
            return "bg-amber-100 text-amber-800 border-amber-200";
    }
}

function normalizeSectionText(value) {
    const text = (value || "").trim();
    return text ? text : "Pending";
}

function cardToneClass(index) {
    return index % 2 === 0
        ? "bg-white border border-slate-200"
        : "bg-cyan-50/50 border border-cyan-100";
}

function formatUtcDate(value) {
    if (!value) {
        return "Pending";
    }

    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? "Pending" : date.toLocaleString();
}

function renderAssumptions(assumptions) {
    const items = Array.isArray(assumptions) ? assumptions : [];
    if (items.length === 0) {
        return `<p class="text-sm text-slate-500">Pending</p>`;
    }

    return `<ul class="list-disc pl-5 text-sm text-slate-700">${items.map((item) => `<li>${escapeHtml(item)}</li>`).join("")}</ul>`;
}

function renderQa(qa) {
    const rows = Array.isArray(qa) ? qa : [];
    if (rows.length === 0) {
        return `<p class="text-sm text-slate-500">Pending</p>`;
    }

    return `
        <div class="space-y-2">
            ${rows.map((row) => `
                <div class="rounded border border-slate-200 bg-slate-50 p-2">
                    <p class="text-xs uppercase tracking-wide text-slate-500">Question</p>
                    <p class="text-sm text-slate-800">${escapeHtml(row.question || "Pending")}</p>
                    <p class="mt-1 text-xs uppercase tracking-wide text-slate-500">Answer</p>
                    <p class="text-sm text-slate-700">${escapeHtml(row.answer || "Pending")}</p>
                </div>
            `).join("")}
        </div>
    `;
}

function renderTicketModalHtml(ticket) {
    if (!ticket) {
        return `<p class="text-sm text-slate-500">No ticket details are available.</p>`;
    }

    const dependencies = Array.isArray(ticket.dependencies) ? ticket.dependencies.filter(Boolean) : [];
    const definitionOfDone = Array.isArray(ticket.definitionOfDone) ? ticket.definitionOfDone.filter(Boolean) : [];

    return `
        <div class="space-y-4">
            <div class="rounded-lg border border-slate-200 bg-slate-50 p-3">
                <div class="flex flex-wrap items-center gap-2">
                    <span class="inline-flex items-center rounded border border-slate-300 bg-white px-2.5 py-1 text-xs font-semibold text-slate-700">${escapeHtml(ticket.id || "Ticket")}</span>
                    <span class="inline-flex items-center rounded border border-slate-300 bg-white px-2.5 py-1 text-xs font-semibold text-slate-700">Status: ${escapeHtml(ticket.status || "Pending")}</span>
                    <span class="inline-flex items-center rounded border border-slate-300 bg-white px-2.5 py-1 text-xs font-semibold text-slate-700">Priority: ${escapeHtml(ticket.priority || "-")}</span>
                    <span class="inline-flex items-center rounded border border-slate-300 bg-white px-2.5 py-1 text-xs font-semibold text-slate-700">Type: ${escapeHtml(ticket.type || "-")}</span>
                </div>
            </div>
            <div>
                <h4 class="text-xs font-semibold uppercase tracking-wide text-slate-500 mb-2">Description</h4>
                <div class="rounded-lg border border-slate-200 bg-white p-3 text-sm text-slate-800 whitespace-pre-wrap">${escapeHtml(ticket.description || "No description provided.")}</div>
            </div>
            ${dependencies.length > 0 ? `
                <div>
                    <h4 class="text-xs font-semibold uppercase tracking-wide text-slate-500 mb-2">Dependencies</h4>
                    <ul class="list-disc pl-5 text-sm text-slate-700 space-y-1">
                        ${dependencies.map((item) => `<li>${escapeHtml(item)}</li>`).join("")}
                    </ul>
                </div>
            ` : ""}
            ${definitionOfDone.length > 0 ? `
                <div>
                    <h4 class="text-xs font-semibold uppercase tracking-wide text-slate-500 mb-2">Definition Of Done</h4>
                    <ul class="list-disc pl-5 text-sm text-slate-700 space-y-1">
                        ${definitionOfDone.map((item) => `<li>${escapeHtml(item)}</li>`).join("")}
                    </ul>
                </div>
            ` : ""}
        </div>
    `;
}

function openDashboardContentModal(title, subtitle, bodyHtml) {
    if (!ui.dashboardContentModal || !ui.dashboardContentTitle || !ui.dashboardContentSubtitle || !ui.dashboardContentBody) {
        return;
    }

    ui.dashboardContentTitle.textContent = title || "Ticket Viewer";
    ui.dashboardContentSubtitle.textContent = subtitle || "";
    ui.dashboardContentBody.innerHTML = bodyHtml || `<p class="text-sm text-slate-500">No content available.</p>`;
    ui.dashboardContentModal.classList.remove("hidden");
    ui.dashboardContentModal.classList.add("flex");
}

function closeDashboardContentModal() {
    if (!ui.dashboardContentModal) {
        return;
    }

    ui.dashboardContentModal.classList.add("hidden");
    ui.dashboardContentModal.classList.remove("flex");
}

function renderTicketsTable(rows, emptyLabel, projectName, sectionLabel) {
    const tickets = Array.isArray(rows) ? rows : [];
    if (tickets.length === 0) {
        return `<p class="text-sm text-slate-500">${escapeHtml(emptyLabel)}</p>`;
    }

    const actionHeader = sectionLabel === "completed"
        ? "Reopen"
        : sectionLabel === "skipped"
            ? "Unskip"
            : "";

    return `
        <div class="overflow-x-auto">
            <table class="min-w-full text-sm">
                <thead>
                    <tr class="text-left text-slate-500 border-b border-slate-200">
                        <th class="py-1 pr-2">Status</th>
                        <th class="py-1 pr-2">Priority</th>
                        <th class="py-1 pr-2">ID</th>
                        <th class="py-1 pr-2">Title</th>
                        <th class="py-1 pr-2 text-right">View</th>
                        <th class="py-1 pr-2 text-right">${escapeHtml(actionHeader)}</th>
                    </tr>
                </thead>
                <tbody>
                    ${tickets.map((ticket) => `
                        <tr class="border-b border-slate-100 hover:bg-slate-50 transition">
                            <td class="py-1 pr-2">${escapeHtml(ticket.status || "Pending")}</td>
                            <td class="py-1 pr-2">${escapeHtml(ticket.priority || "-")}</td>
                            <td class="py-1 pr-2">${escapeHtml(ticket.id || "-")}</td>
                            <td class="py-1 pr-2">${escapeHtml(ticket.title || "(untitled)")}</td>
                            <td class="py-1 pr-2 text-right">
                                <button type="button"
                                        class="rounded border border-fuchsia-300 bg-fuchsia-50 px-2.5 py-1 text-xs font-semibold text-fuchsia-700 hover:bg-fuchsia-100 transition"
                                        data-ticket-view="1"
                                        data-ticket-project="${escapeHtml(projectName || "")}"
                                        data-ticket-section="${escapeHtml(sectionLabel || "")}"
                                        data-ticket-id="${escapeHtml(ticket.id || "")}">
                                    View Ticket
                                </button>
                            </td>
                            <td class="py-1 pr-2 text-right">
                                ${sectionLabel === "completed" ? `
                                    <button type="button"
                                            class="rounded bg-emerald-600 px-2.5 py-1 text-xs font-semibold text-white hover:bg-emerald-500 transition"
                                            data-ticket-action="reopen"
                                            data-ticket-project="${escapeHtml(projectName || "")}"
                                            data-ticket-section="${escapeHtml(sectionLabel || "")}"
                                            data-ticket-id="${escapeHtml(ticket.id || "")}">
                                        Reopen
                                    </button>
                                ` : sectionLabel === "skipped" ? `
                                    <button type="button"
                                            class="rounded bg-amber-600 px-2.5 py-1 text-xs font-semibold text-white hover:bg-amber-500 transition"
                                            data-ticket-action="resume"
                                            data-ticket-project="${escapeHtml(projectName || "")}"
                                            data-ticket-section="${escapeHtml(sectionLabel || "")}"
                                            data-ticket-id="${escapeHtml(ticket.id || "")}">
                                        Unskip
                                    </button>
                                ` : ``}
                            </td>
                        </tr>
                    `).join("")}
                </tbody>
            </table>
        </div>
    `;
}

// Helper function to render architecture overview with improved UI
function renderArchitectureOverview(design) {
    const overview = design.overview || "";
    const components = Array.isArray(design.components) ? design.components : [];
    const dataModelsCount = Number(design.dataModelsCount || 0);
    const projectStructure = design.projectStructure;
    
    // If no data, show pending
    if (!overview && components.length === 0 && dataModelsCount === 0 && !projectStructure) {
        return `<p class="text-sm text-slate-500">Pending</p>`;
    }
    
    let html = '';
    
    // Overview section - handle as JSON if it's parseable, otherwise as plain text
    let overviewContent;
    try {
        const parsedOverview = JSON.parse(overview);
        if (parsedOverview && typeof parsedOverview === 'object') {
            // It's a valid JSON object, render as key-value list
            const keys = Object.keys(parsedOverview);
            if (keys.length > 0) {
                overviewContent = `
                    <div class="grid grid-cols-1 sm:grid-cols-2 gap-2">
                        ${keys.map(key => `
                            <div class="rounded border border-slate-200 bg-slate-50 p-2">
                                <p class="text-xs font-semibold uppercase tracking-wide text-slate-500 mb-0.5">${escapeHtml(key)}</p>
                                <p class="text-sm text-slate-800 break-all">${escapeHtml(String(parsedOverview[key]))}</p>
                            </div>
                        `).join('')}
                    </div>
                `;
            } else {
                overviewContent = `<p class="text-sm text-slate-500">No properties defined</p>`;
            }
        } else {
            // Not an object, treat as plain text
            overviewContent = `<p class="text-sm text-slate-800 whitespace-pre-wrap leading-relaxed">${escapeHtml(overview)}</p>`;
        }
    } catch (e) {
        // Not valid JSON, treat as plain text
        overviewContent = `<p class="text-sm text-slate-800 whitespace-pre-wrap leading-relaxed">${escapeHtml(overview)}</p>`;
    }
    
    if (overview.trim()) {
        html += `
            <div class="rounded-lg border border-slate-200 bg-white p-3 mb-3">
                <h4 class="text-xs font-semibold uppercase tracking-wide text-slate-500 mb-2">Design Overview</h4>
                ${overviewContent}
            </div>
        `;
    }
    
    // Components and data models in a grid layout
    html += `<div class="grid grid-cols-1 sm:grid-cols-2 gap-3 mb-3">`;
    
    // Components section
    if (components.length > 0) {
        html += `
            <section class="rounded-lg border border-slate-200 bg-cyan-50/30 p-3">
                <div class="flex items-center justify-between mb-2">
                    <h4 class="text-xs font-semibold uppercase tracking-wide text-cyan-700">Components</h4>
                    <span class="inline-flex items-center rounded-full bg-cyan-100 px-2 py-0.5 text-xs font-bold text-cyan-800">${components.length}</span>
                </div>
                <div class="flex flex-wrap gap-2">
                    ${components.map(comp => 
                        `<span class="inline-flex items-center rounded-md bg-white px-2 py-1 text-xs font-medium text-slate-700 ring-1 ring-inset ring-cyan-600/20">${escapeHtml(comp)}</span>`
                    ).join('')}
                </div>
            </section>
        `;
    } else {
        html += `
            <section class="rounded-lg border border-slate-200 bg-white p-3">
                <h4 class="text-xs font-semibold uppercase tracking-wide text-slate-500 mb-1">Components</h4>
                <p class="text-sm text-slate-500 italic">No components defined yet</p>
            </section>
        `;
    }
    
    // Data models section
    html += `
        <section class="rounded-lg border border-slate-200 bg-white p-3">
            <div class="flex items-center justify-between mb-1">
                <h4 class="text-xs font-semibold uppercase tracking-wide text-emerald-700">Data Models</h4>
                <span class="inline-flex items-center rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-bold text-emerald-800">${dataModelsCount}</span>
            </div>
            ${dataModelsCount > 0 
                ? '<p class="text-sm text-slate-700">Defined data structures</p>' 
                : '<p class="text-sm text-slate-500 italic">No data models defined yet</p>'
            }
        </section>
    `;
    
    html += `</div>`;
    
    // Project structure (if available)
    if (projectStructure) {
        try {
            const structureJson = JSON.stringify(projectStructure, null, 2);
            html += `
                <details class="rounded-lg border border-slate-200 bg-slate-50 p-3 mt-3">
                    <summary class="cursor-pointer list-none text-sm font-semibold text-slate-800 hover:text-indigo-700">
                        <span class="inline-flex items-center gap-1">📋 View Project Structure</span>
                    </summary>
                    <pre class="mt-2 overflow-x-auto rounded bg-white p-3 text-xs text-slate-700 whitespace-pre-wrap">${escapeHtml(structureJson)}</pre>
                </details>
            `;
        } catch (e) {
            // If JSON parsing fails, skip
        }
    }
    
    return html;
}

function renderProjectCard(project, index) {
    const status = project.projectStatus || "Pending";
    const planning = project.planning || {};
    const design = project.design || {};
    const tickets = project.tickets || {};
    const projectState = project.projectState || {};
    const counts = tickets.counts || { open: 0, skipped: 0, completed: 0, total: 0 };
    const warnings = Array.isArray(project.warnings) ? project.warnings : [];
    const cardTone = cardToneClass(index);
    const stateSummary = projectState.hasStateFile
        ? `Step ${Number(projectState.currentStep || 0)} of ${Number(projectState.totalSteps || 0)}`
        : "Pending";

    return `
        <article class="rounded-xl shadow p-4 ${cardTone}">
            <div class="flex items-start justify-between gap-2 mb-3">
                <div>
                    <h2 class="text-xl font-bold text-slate-900">${escapeHtml(project.projectName || "(unknown)")}</h2>
                    <p class="text-xs text-slate-500 mt-1">Project Summary and Ticket Health</p>
                </div>
                <span class="rounded border px-2 py-1 text-xs font-semibold ${badgeClass(status)}">${escapeHtml(status)}</span>
            </div>

            <div class="grid grid-cols-1 lg:grid-cols-2 gap-3">
                <section class="rounded border border-slate-200 p-3">
                    <h3 class="text-sm font-semibold text-slate-800 mb-1">Goal Summary</h3>
                    <p class="text-sm text-slate-700">${escapeHtml(normalizeSectionText(project.goalSummary))}</p>
                </section>

                                <section class="rounded border border-slate-200 p-3">
                    <h3 class="text-sm font-semibold text-slate-800 mb-1">Architecture Overview</h3>
                    ${renderArchitectureOverview(design)}
                </section>

                <section class="rounded border border-slate-200 p-3">
                    <h3 class="text-sm font-semibold text-slate-800 mb-1">Planning Q&amp;A</h3>
                    ${renderQa(planning.qa)}
                    <div class="mt-3">
                        <h4 class="text-xs font-semibold uppercase tracking-wide text-slate-500 mb-1">Assumptions</h4>
                        ${renderAssumptions(planning.assumptions)}
                    </div>
                </section>

                <section class="rounded border border-slate-200 p-3">
                    <h3 class="text-sm font-semibold text-slate-800 mb-1">Tickets</h3>
                    <div class="mb-2 text-xs text-slate-500">
                        Open: <span class="font-semibold text-slate-700">${Number(counts.open || 0)}</span>
                        <span class="mx-1">•</span>
                        Skipped: <span class="font-semibold text-slate-700">${Number(counts.skipped || 0)}</span>
                        <span class="mx-1">•</span>
                        Completed: <span class="font-semibold text-slate-700">${Number(counts.completed || 0)}</span>
                        <span class="mx-1">•</span>
                        Total: <span class="font-semibold text-slate-700">${Number(counts.total || 0)}</span>
                    </div>
                    <div class="space-y-2">
                        <div>
                            <h4 class="text-xs font-semibold uppercase tracking-wide text-slate-500 mb-1">Open Tickets</h4>
                            ${renderTicketsTable(tickets.open, "Pending", project.projectName, "open")}
                        </div>
                        <div>
                            <h4 class="text-xs font-semibold uppercase tracking-wide text-slate-500 mb-1">Skipped Tickets</h4>
                            ${renderTicketsTable(tickets.skipped, "Pending", project.projectName, "skipped")}
                        </div>
                        <div>
                            <h4 class="text-xs font-semibold uppercase tracking-wide text-slate-500 mb-1">Completed Tickets</h4>
                            ${renderTicketsTable(tickets.completed, "Pending", project.projectName, "completed")}
                        </div>
                    </div>
                </section>

                <section class="rounded border border-slate-200 p-3 lg:col-span-2">
                    <h3 class="text-sm font-semibold text-slate-800 mb-1">Project State (${escapeHtml(project.projectName || "(unknown)")}.json)</h3>
                    ${projectState.hasStateFile ? `
                        <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-2 text-sm text-slate-700">
                            <p><span class="text-slate-500">Workflow:</span> <span class="font-semibold">${escapeHtml(normalizeSectionText(projectState.workflowTypeName))}</span></p>
                            <p><span class="text-slate-500">Status:</span> <span class="font-semibold">${escapeHtml(normalizeSectionText(projectState.status))}</span></p>
                            <p><span class="text-slate-500">Progress:</span> <span class="font-semibold">${escapeHtml(stateSummary)}</span></p>
                            <p><span class="text-slate-500">Step Name:</span> <span class="font-semibold">${escapeHtml(normalizeSectionText(projectState.currentStepName))}</span></p>
                            <p><span class="text-slate-500">Flags:</span> <span class="font-semibold">${projectState.workflowRunning ? "Running" : "Idle"}${projectState.busy ? " • Busy" : ""}${projectState.awaitingUserInput ? " • Awaiting Input" : ""}${projectState.canResume ? " • Can Resume" : ""}</span></p>
                            <p><span class="text-slate-500">Last Updated:</span> <span class="font-semibold">${escapeHtml(formatUtcDate(projectState.lastUpdatedUtc))}</span></p>
                        </div>
                    ` : `
                        <p class="text-sm text-slate-500">Pending</p>
                    `}
                </section>
            </div>

            ${warnings.length > 0 ? `
                <section class="mt-3 rounded border border-amber-200 bg-amber-50 p-2">
                    <h4 class="text-xs font-semibold uppercase tracking-wide text-amber-700 mb-1">Warnings</h4>
                    <ul class="list-disc pl-4 text-xs text-amber-800">
                        ${warnings.map((warning) => `<li>${escapeHtml(warning)}</li>`).join("")}
                    </ul>
                </section>
            ` : ""}
        </article>
    `;
}

function updateSummary(projects) {
    const items = Array.isArray(projects) ? projects : [];
    const counts = {
        total: items.length,
        pending: 0,
        planned: 0,
        inProgress: 0,
        completed: 0
    };

    items.forEach((project) => {
        const status = (project.projectStatus || "Pending").toLowerCase();
        if (status === "completed") {
            counts.completed++;
        } else if (status === "in progress") {
            counts.inProgress++;
        } else if (status === "planned") {
            counts.planned++;
        } else {
            counts.pending++;
        }
    });

    ui.summaryTotal.textContent = counts.total;
    ui.summaryPending.textContent = counts.pending;
    ui.summaryPlanned.textContent = counts.planned;
    ui.summaryInProgress.textContent = counts.inProgress;
    ui.summaryCompleted.textContent = counts.completed;
}

function findProjectTicket(projectName, sectionLabel, ticketId) {
    const project = state.projects.find((item) => item.projectName === projectName);
    if (!project) {
        return null;
    }

    const source = sectionLabel === "completed"
        ? project.tickets?.completed
        : sectionLabel === "skipped"
            ? project.tickets?.skipped
        : project.tickets?.open;
    const tickets = Array.isArray(source) ? source : [];
    return tickets.find((ticket) => ticket.id === ticketId) || null;
}

async function loadDashboard() {
    ui.refreshBtn.disabled = true;
    ui.dashboardStatus.textContent = "Loading...";

    try {
        const response = await fetch("/api/dashboard/projects");
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        const payload = await response.json();
        const projects = Array.isArray(payload.projects) ? payload.projects : [];
        state.projects = projects;
        updateSummary(projects);

        if (projects.length === 0) {
            ui.projectList.innerHTML = `
                <div class="rounded-lg bg-white p-4 shadow text-slate-600">
                    No projects found. Project status is pending until project data exists.
                </div>
            `;
        } else {
            ui.projectList.innerHTML = projects.map(renderProjectCard).join("");
        }

        ui.dashboardStatus.textContent = "";
        ui.lastUpdated.textContent = `Last updated: ${new Date().toLocaleString()}`;
    } catch (error) {
        ui.dashboardStatus.textContent = "Failed to load dashboard.";
        ui.projectList.innerHTML = `
            <div class="rounded-lg border border-red-200 bg-red-50 p-4 text-red-700">
                Failed to load dashboard data: ${escapeHtml(error.message || "Unknown error")}
            </div>
        `;
    } finally {
        ui.refreshBtn.disabled = false;
    }
}

ui.refreshBtn.addEventListener("click", async () => {
    await loadDashboard();
});

if (ui.dashboardContentCloseBtn) {
    ui.dashboardContentCloseBtn.addEventListener("click", () => {
        closeDashboardContentModal();
    });
}

if (ui.dashboardContentModal) {
    ui.dashboardContentModal.addEventListener("click", (event) => {
        if (event.target === ui.dashboardContentModal) {
            closeDashboardContentModal();
        }
    });
}

if (ui.projectList) {
    ui.projectList.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        const button = target.closest("[data-ticket-id]");
        if (!(button instanceof HTMLElement)) {
            return;
        }

        const action = button.getAttribute("data-ticket-action") || "";
        if (action) {
            const projectName = button.getAttribute("data-ticket-project") || "";
            const ticketId = button.getAttribute("data-ticket-id") || "";
            if (!projectName || !ticketId) {
                return;
            }

            const verb = action === "reopen" ? "reopen" : "resume";
            const ok = window.confirm(`Are you sure you want to ${verb} ticket ${ticketId}?`);
            if (!ok) {
                return;
            }

            const endpoint = action === "reopen"
                ? "/api/dashboard/tickets/reopen"
                : "/api/dashboard/tickets/resume";

            fetch(endpoint, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ projectName, ticketId })
            }).then(async (response) => {
                if (!response.ok) {
                    const payload = await response.json().catch(() => null);
                    throw new Error(payload?.message || `HTTP ${response.status}`);
                }
            }).then(() => {
                void loadDashboard();
            }).catch((error) => {
                window.alert(`Failed to ${verb} ticket: ${error.message || "Unknown error"}`);
            });

            return;
        }

        const isView = Boolean(button.getAttribute("data-ticket-view"));
        if (!isView) {
            return;
        }

        const projectName = button.getAttribute("data-ticket-project") || "";
        const sectionLabel = button.getAttribute("data-ticket-section") || "open";
        const ticketId = button.getAttribute("data-ticket-id") || "";
        const ticket = findProjectTicket(projectName, sectionLabel, ticketId);
        if (!ticket) {
            return;
        }

        openDashboardContentModal(
            ticket.title || "Ticket Viewer",
            `${projectName} • ${ticket.id || "Ticket"}`,
            renderTicketModalHtml(ticket)
        );
    });
}

document.addEventListener("DOMContentLoaded", () => {
    loadDashboard();
});
