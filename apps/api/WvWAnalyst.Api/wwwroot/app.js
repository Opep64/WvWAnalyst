const DIRECTORY_PATH_KEY = "wvw-analyst.last-directory";
const DIRECTORY_MODE_KEY = "wvw-analyst.last-mode";
const DIRECTORY_MAX_PARALLELISM_KEY = "wvw-analyst.last-max-parallelism";
const ACTIVE_BATCH_JOB_KEY = "wvw-analyst.active-batch-job";

let currentDashboardSnapshot = null;
let currentAnalysisSnapshot = null;
let lastBatchResult = null;
let activeBatchJobId = null;
let batchStatusPollHandle = null;
let activeAnalysisTab = "players";
let fightBrowserSortState = { key: "fightTime", direction: "desc" };
let analysisPlayerSortState = { key: "impact", direction: "desc" };
let analysisClassSortState = { key: "impact", direction: "desc" };
let analysisClassPlayerSortState = { key: "impact", direction: "desc" };
let selectedAnalysisPlayerAccount = null;
let selectedAnalysisClassLabel = null;
let selectedAnalysisLaneKey = null;
let selectedAnalysisBoonId = null;
let activeAnalysisLaneDetailTab = "players";
const MINIMUM_LANE_FILTER_APPEARANCES = 20;
const MINIMUM_PLAYER_TABLE_FIGHTS = 40;

function escapeHtml(value) {
    return String(value ?? "")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll("\"", "&quot;")
        .replaceAll("'", "&#39;");
}

function formatBytes(value) {
    return Number(value ?? 0).toLocaleString();
}

function formatDate(value) {
    if (!value) {
        return "";
    }

    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString();
}

function parseDateValue(value) {
    if (!value) {
        return Number.NEGATIVE_INFINITY;
    }

    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime()) ? Number.NEGATIVE_INFINITY : parsed.getTime();
}

function formatNumber(value, maximumFractionDigits = 0) {
    if (value == null || value === "") {
        return "n/a";
    }

    const numeric = Number(value);
    if (Number.isNaN(numeric)) {
        return String(value);
    }

    return numeric.toLocaleString(undefined, {
        minimumFractionDigits: 0,
        maximumFractionDigits
    });
}

function formatPercent(value, maximumFractionDigits = 1) {
    if (value == null || value === "") {
        return "n/a";
    }

    return `${formatNumber(value, maximumFractionDigits)}%`;
}

function formatSeconds(value, maximumFractionDigits = 1) {
    if (value == null || value === "") {
        return "n/a";
    }

    const numeric = Number(value);
    if (Number.isNaN(numeric)) {
        return String(value);
    }

    return `${formatNumber(numeric, maximumFractionDigits)}s`;
}

function setInnerHtml(selector, html) {
    document.querySelector(selector).innerHTML = html;
}

function buildTagListHtml(items, fallback = "None recorded.") {
    const filtered = (items ?? []).filter(Boolean);
    if (filtered.length === 0) {
        return `<li>${escapeHtml(fallback)}</li>`;
    }

    return filtered
        .map(item => `<li>${escapeHtml(item)}</li>`)
        .join("");
}

function buildStatPairsHtml(items, fallback = "No side summary is stored for this fight yet.") {
    const filtered = (items ?? []).filter(item => item?.label && item?.value != null);
    if (filtered.length === 0) {
        return `<div class="table-note">${escapeHtml(fallback)}</div>`;
    }

    return filtered
        .map(item => `
            <div class="stat-row">
                <dt>${escapeHtml(item.label)}</dt>
                <dd>${escapeHtml(item.value)}</dd>
            </div>
        `)
        .join("");
}

function buildScoreboardRow(label, squadValue, enemyValue) {
    return `
        <tr>
            <td>${escapeHtml(label)}</td>
            <td>${escapeHtml(squadValue)}</td>
            <td>${escapeHtml(enemyValue)}</td>
        </tr>
    `;
}

function buildActionLink(url, label) {
    return url
        ? `<a class="action-link" href="${escapeHtml(url)}" target="_blank" rel="noopener">${escapeHtml(label)}</a>`
        : "";
}

function buildPillarCard(pillar) {
    const meta = [
        pillar.grade ? `Grade ${pillar.grade}` : null,
        typeof pillar.availableMetricCount === "number" && typeof pillar.metricCount === "number"
            ? `${pillar.availableMetricCount}/${pillar.metricCount} metrics`
            : null,
        pillar.adjustmentApplied
            ? `Adjusted ${pillar.adjustedScore}${pillar.adjustedGrade ? ` (${pillar.adjustedGrade})` : ""}`
            : null
    ].filter(Boolean);

    const metricsMarkup = pillar.metrics?.length
        ? pillar.metrics.map(metric => `
            <li>
                <div class="section-heading">
                    <strong>${escapeHtml(metric.label)}</strong>
                    <span class="metric-score">${escapeHtml(String(metric.score))}</span>
                </div>
                <div class="metric-meta">${escapeHtml(metric.value ?? (metric.available ? "Available" : "Neutralized"))}</div>
                ${metric.note ? `<span class="metric-note">${escapeHtml(metric.note)}</span>` : ""}
            </li>
        `).join("")
        : `<li><div class="metric-meta">No metric drilldown was stored for this pillar.</div></li>`;

    return `
        <article class="pillar-card">
            <header>
                <div>
                    <strong>${escapeHtml(pillar.label)}</strong>
                    <p class="workspace-note">${escapeHtml(pillar.summary ?? "No summary recorded.")}</p>
                </div>
                <span class="weight">${escapeHtml(String(pillar.score))}</span>
            </header>
            ${pillar.detail ? `<p class="workspace-note">${escapeHtml(pillar.detail)}</p>` : ""}
            ${meta.length ? `<div class="pillars-meta">${meta.map(item => `<span>${escapeHtml(item)}</span>`).join("")}</div>` : ""}
            ${pillar.adjustmentApplied && pillar.adjustmentDetail ? `<p class="metric-note">${escapeHtml(pillar.adjustmentDetail)}</p>` : ""}
            <ul class="metric-list">${metricsMarkup}</ul>
        </article>
    `;
}

function buildAnalysisLinePath(values, minValue = 0, maxValue = 100) {
    const width = 320;
    const height = 118;
    const numericValues = values
        .map(value => value == null ? null : Number(value))
        .filter(value => value != null && !Number.isNaN(value));

    if (numericValues.length === 0) {
        return "";
    }

    const xStep = numericValues.length <= 1 ? width / 2 : width / (numericValues.length - 1);
    const range = Math.max(1, maxValue - minValue);

    return numericValues
        .map((value, index) => {
            const x = Math.round(index * xStep * 100) / 100;
            const clampedValue = Math.max(minValue, Math.min(maxValue, Number(value)));
            const y = Math.round((height - ((clampedValue - minValue) / range) * height) * 100) / 100;
            return `${index === 0 ? "M" : "L"} ${x} ${y}`;
        })
        .join(" ");
}

function buildAnalysisChartCard(title, valueLabel, values, detail) {
    const path = buildAnalysisLinePath(values);
    const empty = !path;

    return `
        <article class="analysis-card">
            <strong>${escapeHtml(title)}</strong>
            <div class="analysis-card-value">${escapeHtml(valueLabel)}</div>
            ${detail ? `<div class="table-inline-note">${escapeHtml(detail)}</div>` : ""}
            <svg class="analysis-chart" viewBox="0 0 320 118" preserveAspectRatio="none" aria-hidden="true">
                <line class="grid-line" x1="0" y1="29.5" x2="320" y2="29.5"></line>
                <line class="grid-line" x1="0" y1="59" x2="320" y2="59"></line>
                <line class="grid-line" x1="0" y1="88.5" x2="320" y2="88.5"></line>
                ${empty ? "" : `<path d="${escapeHtml(path)}"></path>`}
            </svg>
            ${empty ? `<div class="table-inline-note">No score points available for this selection.</div>` : ""}
        </article>
    `;
}

function buildPlayerRoleLabel(player) {
    const roleMix = (player.roleMix ?? [])
        .filter(role => role?.label)
        .slice(0, 2)
        .map(role => `${role.label} ${formatPercent(role.percent)}`);

    if (roleMix.length > 0) {
        return roleMix.join(" | ");
    }

    return player.contributionProfile
        || player.fitSummary
        || player.demandFitSummary
        || "Unclassified";
}

function buildPlayerTableRow(player) {
    const rowClass = player.isCommander ? "is-commander" : "";
    const playerName = player.character || player.account || `Actor ${player.actorId}`;
    const profession = [player.eliteSpec, player.profession]
        .filter(Boolean)
        .join(" / ");
    const playerMeta = [
        player.account || null,
        player.group ? `Group ${player.group}` : null,
        profession || null,
        player.isCommander ? "Commander" : null
    ].filter(Boolean);

    const pressurePrimary = `${formatNumber(player.damage)} dmg | ${formatNumber(player.downs)} downs | ${formatNumber(player.kills)} kills`;
    const pressureSecondary = player.strips > 0
        ? `${formatNumber(player.strips)} strips`
        : "No recorded strips";

    const supportParts = [];
    if (player.outgoingCleanses > 0) {
        supportParts.push(`${formatNumber(player.outgoingCleanses)} cleanses`);
    }
    if (player.resurrects > 0) {
        supportParts.push(`${formatNumber(player.resurrects)} rezzes`);
    }
    if (player.healing > 0) {
        supportParts.push(`${formatNumber(player.healing)} heal`);
    }
    if (player.barrier > 0) {
        supportParts.push(`${formatNumber(player.barrier)} barrier`);
    }

    const survivalPrimary = `${formatNumber(player.deaths)} deaths | ${formatNumber(player.recoveries)} recovers`;
    const survivalSecondary = `${formatNumber(player.damageTaken)} taken | ${formatNumber(player.receivedCrowdControl)} CC`;

    const positioningPrimary = player.hasPositioningData
        ? `In ${formatPercent(player.inPositionRate)} | Far ${formatPercent(player.tooFarRate)}`
        : "No commander-relative replay sample";
    const positioningSecondary = player.hasPositioningData
        ? `Over ${formatPercent(player.overextendedRate)} | Side ${formatPercent(player.lateralRiskRate)} | ${formatNumber(player.positioningSamples)} samples`
        : `Active ${formatSeconds(player.activeSeconds)} | Combat ${formatSeconds(player.combatSeconds)}`;

    const summaryPrimary = player.keyContributionSummary
        || player.fitSummary
        || player.demandFitSummary
        || "No compact contribution summary was stored.";
    const summarySecondaryBits = [
        player.evaluationConfidenceLabel ? `Confidence ${player.evaluationConfidenceLabel}` : null,
        player.evaluationConfidenceDetail || null,
        ...(player.evidenceSnapshot ?? []).slice(0, 2)
    ].filter(Boolean);

    return `
        <tr class="${rowClass}">
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(playerName)}</strong>
                    ${playerMeta.map(item => `<span class="table-inline-note">${escapeHtml(item)}</span>`).join("")}
                </div>
            </td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(buildPlayerRoleLabel(player))}</strong>
                    ${player.contributionProfile ? `<span class="table-inline-note">${escapeHtml(player.contributionProfile)}</span>` : ""}
                </div>
            </td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(pressurePrimary)}</strong>
                    <span class="table-inline-note">${escapeHtml(pressureSecondary)}</span>
                </div>
            </td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(supportParts.length > 0 ? supportParts.join(" | ") : "No meaningful support output recorded")}</strong>
                    <span class="table-inline-note">Active ${escapeHtml(formatSeconds(player.activeSeconds))} | Combat ${escapeHtml(formatSeconds(player.combatSeconds))}</span>
                </div>
            </td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(survivalPrimary)}</strong>
                    <span class="table-inline-note">${escapeHtml(survivalSecondary)}</span>
                </div>
            </td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(positioningPrimary)}</strong>
                    <span class="table-inline-note">${escapeHtml(positioningSecondary)}</span>
                </div>
            </td>
            <td class="player-summary-cell">
                <div class="table-stack">
                    <strong>${escapeHtml(summaryPrimary)}</strong>
                    ${summarySecondaryBits.map(item => `<span class="table-inline-note">${escapeHtml(item)}</span>`).join("")}
                </div>
            </td>
        </tr>
    `;
}

function getSelectedFightId() {
    return new URL(window.location.href).searchParams.get("fightId");
}

async function loadDashboard() {
    const response = await fetch("/api/dashboard");
    if (!response.ok) {
        throw new Error(`Dashboard request failed with status ${response.status}`);
    }

    return response.json();
}

async function loadFightDetail(fightId) {
    const response = await fetch(`/api/fights/${encodeURIComponent(fightId)}`);
    if (!response.ok) {
        throw new Error(`Fight detail request failed with status ${response.status}`);
    }

    return response.json();
}

async function loadAnalysis(filters = {}) {
    const params = new URLSearchParams();

    if (filters.commander) {
        params.set("commander", filters.commander);
    }
    if (filters.startDate) {
        params.set("startDate", filters.startDate);
    }
    if (filters.endDate) {
        params.set("endDate", filters.endDate);
    }
    if (filters.outcome && filters.outcome !== "all") {
        params.set("outcome", filters.outcome);
    }

    const query = params.toString();
    const response = await fetch(`/api/analysis${query ? `?${query}` : ""}`);
    if (!response.ok) {
        throw new Error(`Analysis request failed with status ${response.status}`);
    }

    return response.json();
}

async function loadBatchJobStatus(jobId) {
    const response = await fetch(`/api/imports/directory/jobs/${encodeURIComponent(jobId)}`);
    if (!response.ok) {
        throw new Error(`Batch status request failed with status ${response.status}`);
    }

    return response.json();
}

function getFightStartValue(fight) {
    return parseDateValue(fight.fightIndex?.timeStartStandard ?? fight.fightIndex?.timeStart ?? null);
}

function getImportedAtValue(fight) {
    return parseDateValue(fight.importedAtUtc ?? null);
}

function getDurationValue(fight) {
    return Number(fight.fightIndex?.durationMilliseconds ?? 0);
}

function getEnemyCountValue(fight) {
    return Number(fight.fightIndex?.enemyPlayerCount ?? fight.fightIndex?.enemyTargetCount ?? 0);
}

function getSquadCountValue(fight) {
    return Number(fight.fightIndex?.squadPlayerCount ?? 0);
}

function getOutcomeCode(fight) {
    return fight.fightIndex?.outcome?.outcomeCode ?? "unknown";
}

function getOutcomeDisplayLabel(fight) {
    return fight.fightIndex?.outcome?.displayLabel ?? "Unavailable";
}

function getExecutionScoreLabel(fight) {
    const execution = fight.fightIndex?.execution;
    if (!execution?.scoreAvailable || typeof execution.overallScore !== "number") {
        return "n/a";
    }

    return execution.grade
        ? `${execution.overallScore} / ${execution.grade}`
        : String(execution.overallScore);
}

function getFightSearchText(fight) {
    const fightIndex = fight.fightIndex;
    return [
        fight.fightId,
        fight.sourceFileName,
        fight.sourceFilePath,
        fight.status,
        fight.notes,
        fightIndex?.fightName,
        fightIndex?.encounterName,
        fightIndex?.recordedBy,
        fightIndex?.recordedAccountBy,
        fightIndex?.eliteInsightsVersion,
        fightIndex?.analystSchemaVersion,
        fightIndex?.indexedFrom,
        fightIndex?.outcome?.displayLabel,
        fightIndex?.outcome?.detail,
        fightIndex?.execution?.grade,
        fightIndex?.execution?.confidenceLabel,
        fightIndex?.execution?.summary,
        fightIndex?.execution?.detail,
        fightIndex?.execution?.strongestPillarLabel,
        fightIndex?.execution?.weakestPillarLabel,
        ...(fightIndex?.commanderDisplayNames ?? []),
        ...(fightIndex?.activeExtensions ?? [])
    ]
        .filter(Boolean)
        .join(" ")
        .toLowerCase();
}

function getFightBrowserSortValue(fight, sortKey) {
    switch (sortKey) {
        case "commander":
            return (fight.fightIndex?.commanderDisplayNames?.join(", ") ?? "").toLowerCase();
        case "duration":
            return getDurationValue(fight);
        case "outcome":
            return getOutcomeDisplayLabel(fight).toLowerCase();
        case "score":
            return Number(fight.fightIndex?.execution?.overallScore ?? Number.NEGATIVE_INFINITY);
        case "squad":
            return getSquadCountValue(fight);
        case "enemy":
            return getEnemyCountValue(fight);
        case "fightTime":
        default:
            return getFightStartValue(fight);
    }
}

function getDefaultFightBrowserSortDirection(sortKey) {
    switch (sortKey) {
        case "commander":
        case "outcome":
            return "asc";
        case "fightTime":
        case "duration":
        case "score":
        case "squad":
        case "enemy":
        default:
            return "desc";
    }
}

function compareFightBrowserValues(leftValue, rightValue) {
    if (typeof leftValue === "number" || typeof rightValue === "number") {
        return Number(leftValue ?? Number.NEGATIVE_INFINITY) - Number(rightValue ?? Number.NEGATIVE_INFINITY);
    }

    return String(leftValue ?? "").localeCompare(String(rightValue ?? ""), undefined, { sensitivity: "base" });
}

function sortFights(fights) {
    const copy = [...fights];
    const { key, direction } = fightBrowserSortState;
    copy.sort((left, right) => {
        const primary = compareFightBrowserValues(
            getFightBrowserSortValue(left, key),
            getFightBrowserSortValue(right, key));
        if (primary !== 0) {
            return direction === "asc" ? primary : -primary;
        }

        return getFightStartValue(right) - getFightStartValue(left)
            || getImportedAtValue(right) - getImportedAtValue(left);
    });

    return copy;
}

function updateFightBrowserSortHeaders() {
    document.querySelectorAll("[data-fight-browser-sort]").forEach(button => {
        const isActive = button.dataset.fightBrowserSort === fightBrowserSortState.key;
        button.classList.toggle("is-active", isActive);
        button.dataset.sortDirection = isActive ? fightBrowserSortState.direction : "";
        button.setAttribute("aria-sort", isActive ? (fightBrowserSortState.direction === "asc" ? "ascending" : "descending") : "none");
    });
}

function setFightBrowserSort(sortKey) {
    if (!sortKey) {
        return;
    }

    if (fightBrowserSortState.key === sortKey) {
        fightBrowserSortState = {
            key: sortKey,
            direction: fightBrowserSortState.direction === "asc" ? "desc" : "asc"
        };
    } else {
        fightBrowserSortState = {
            key: sortKey,
            direction: getDefaultFightBrowserSortDirection(sortKey)
        };
    }

    if (currentDashboardSnapshot) {
        renderFightBrowser(currentDashboardSnapshot, getSelectedFightId());
    } else {
        updateFightBrowserSortHeaders();
    }
}

function buildStatusClass(value) {
    const normalized = String(value ?? "").trim().toLowerCase();
    if (normalized.includes("imported")) {
        return "status status-imported";
    }
    if (normalized.includes("excluded")) {
        return "status status-excluded";
    }
    if (normalized.includes("failed") || normalized.includes("error")) {
        return "status status-failed";
    }
    if (normalized.includes("skipped")) {
        return "status status-skipped";
    }
    return "status status-neutral";
}

function renderWorkspace(snapshot) {
    document.querySelector("#mode-pill").textContent = snapshot.application.mode;
    document.querySelector("#batch-note").innerHTML = snapshot.workspace.parserCliDetected
        ? `Ready to call the EI CLI at <code>${escapeHtml(snapshot.workspace.parserCliPath)}</code>.`
        : escapeHtml(snapshot.workspace.notes);

    const workspaceCards = document.querySelector("#workspace-cards");
    const parserStatus = snapshot.workspace.parserDetected ? "Detected" : "Missing";
    const parserCliStatus = snapshot.workspace.parserCliDetected ? "Ready" : "Missing";
    const combinerStatus = snapshot.workspace.combinerDetected ? "Detected" : "Missing";

    workspaceCards.innerHTML = `
        <article class="workspace-card">
            <strong>Parser workspace</strong>
            <div class="${buildStatusClass(parserStatus)}">${escapeHtml(parserStatus)}</div>
            <p class="workspace-note"><code>${escapeHtml(snapshot.workspace.parserPath)}</code></p>
        </article>
        <article class="workspace-card">
            <strong>Parser CLI</strong>
            <div class="${buildStatusClass(parserCliStatus)}">${escapeHtml(parserCliStatus)}</div>
            <p class="workspace-note"><code>${escapeHtml(snapshot.workspace.parserCliPath ?? "Not found")}</code></p>
        </article>
        <article class="workspace-card">
            <strong>Combiner workspace</strong>
            <div class="${buildStatusClass(combinerStatus)}">${escapeHtml(combinerStatus)}</div>
            <p class="workspace-note"><code>${escapeHtml(snapshot.workspace.combinerPath)}</code></p>
        </article>
        <article class="workspace-card">
            <strong>Storage root</strong>
            <p class="workspace-note"><code>${escapeHtml(snapshot.storage.rootPath)}</code></p>
            <p class="workspace-note">${snapshot.storage.fightFolderCount} fight folders, ${formatBytes(snapshot.storage.totalBytes)} bytes</p>
        </article>
    `;
}

function renderWorkstreams(snapshot) {
    document.querySelector("#workstreams-list").innerHTML = snapshot.workstreams
        .map(item => `
            <article class="workstream">
                <div class="${buildStatusClass(item.status)}">${escapeHtml(item.status)}</div>
                <h3>${escapeHtml(item.name)}</h3>
                <p>${escapeHtml(item.summary)}</p>
            </article>
        `)
        .join("");
}

function renderScorecard(snapshot) {
    document.querySelector("#scorecard-summary").textContent = snapshot.teamFightScorecard.summary;

    setInnerHtml(
        "#context-list",
        snapshot.teamFightScorecard.nonScoredContext
            .map(item => `<li>${escapeHtml(item)}</li>`)
            .join(""));

    setInnerHtml(
        "#outcome-list",
        snapshot.teamFightScorecard.nonScoredOutcomeHeadline
            .map(item => `<li>${escapeHtml(item)}</li>`)
            .join(""));

    setInnerHtml(
        "#pillar-grid",
        snapshot.teamFightScorecard.primaryPillars
            .map(pillar => `
                <article class="pillar-card">
                    <header>
                        <div>
                            <strong>${escapeHtml(pillar.name)}</strong>
                            <p class="workspace-note">${escapeHtml(pillar.summary)}</p>
                        </div>
                        <span class="weight">${pillar.weightPercent}%</span>
                    </header>
                    <ul class="metric-list">
                        ${pillar.metrics.map(metric => `
                            <li>
                                <strong>${escapeHtml(metric.name)}</strong>
                                <div class="metric-meta">${escapeHtml(metric.direction)} is better | normalized by ${escapeHtml(metric.normalization)}</div>
                                <div class="metric-meta">single fight: ${metric.strongForSingleFight ? "strong" : "weak"} | trend: ${metric.strongForTrend ? "strong" : "weak"}</div>
                            </li>
                        `).join("")}
                    </ul>
                </article>
            `)
            .join(""));
}

function renderRetention(snapshot) {
    setInnerHtml("#keep-list", snapshot.retentionPolicy.keepAlways.map(item => `<li>${escapeHtml(item)}</li>`).join(""));
    setInnerHtml("#purge-list", snapshot.retentionPolicy.purgeFirst.map(item => `<li>${escapeHtml(item)}</li>`).join(""));
}

function getAnalysisFiltersFromUi() {
    return {
        commander: document.querySelector("#analysis-commander").value || "",
        startDate: document.querySelector("#analysis-start-date").value || "",
        endDate: document.querySelector("#analysis-end-date").value || "",
        outcome: document.querySelector("#analysis-outcome").value || "all"
    };
}

function renderAnalysisFilterOptions(snapshot, preserveSelection = true) {
    const commanderSelect = document.querySelector("#analysis-commander");
    const startDateInput = document.querySelector("#analysis-start-date");
    const endDateInput = document.querySelector("#analysis-end-date");
    const outcomeSelect = document.querySelector("#analysis-outcome");

    const previousCommander = preserveSelection ? commanderSelect.value : "";
    const selectedCommander = snapshot.selection?.commander ?? previousCommander;
    const commanderOptions = [
        `<option value="">All commanders</option>`,
        ...(snapshot.options?.commanders ?? []).map(commander => `
            <option value="${escapeHtml(commander)}" ${commander === selectedCommander ? "selected" : ""}>${escapeHtml(commander)}</option>
        `)
    ];
    commanderSelect.innerHTML = commanderOptions.join("");

    startDateInput.min = snapshot.options?.minFightDate ?? "";
    startDateInput.max = snapshot.options?.maxFightDate ?? "";
    endDateInput.min = snapshot.options?.minFightDate ?? "";
    endDateInput.max = snapshot.options?.maxFightDate ?? "";

    if (!preserveSelection || !startDateInput.value) {
        startDateInput.value = snapshot.selection?.startDate ?? "";
    }
    if (!preserveSelection || !endDateInput.value) {
        endDateInput.value = snapshot.selection?.endDate ?? "";
    }

    outcomeSelect.value = snapshot.selection?.outcomeCode ?? "all";
}

function buildAnalysisOverviewCards(snapshot) {
    const overview = snapshot.overview ?? {};
    const cards = [
        {
            title: "Average overall",
            value: overview.averageOverallScore != null
                ? `${formatNumber(overview.averageOverallScore, 1)}${overview.averageOverallGrade ? ` / ${overview.averageOverallGrade}` : ""}`
                : "n/a",
            detail: "Execution score across the filtered fights."
        },
        {
            title: "Average cohesion",
            value: overview.averageCohesionScore != null ? formatNumber(overview.averageCohesionScore, 1) : "n/a",
            detail: "Commander-relative positioning and formation discipline."
        },
        {
            title: "Average pressure",
            value: overview.averagePressureScore != null ? formatNumber(overview.averagePressureScore, 1) : "n/a",
            detail: "Burst quality, downs forced, and offensive pressure."
        },
        {
            title: "Average downstate",
            value: overview.averageDownstateScore != null ? formatNumber(overview.averageDownstateScore, 1) : "n/a",
            detail: "Kill conversion, recovery, and down-window control."
        },
        {
            title: "Average resilience",
            value: overview.averageResilienceScore != null ? formatNumber(overview.averageResilienceScore, 1) : "n/a",
            detail: "Burst survival and stabilization under enemy pressure."
        },
        {
            title: "Average sizes",
            value: `${formatNumber(overview.averageSquadSize, 1)} vs ${formatNumber(overview.averageEnemySize, 1)}`,
            detail: "Average squad and enemy player counts."
        },
        {
            title: "Average duration",
            value: formatSeconds(overview.averageDurationSeconds, 1),
            detail: "Average filtered fight duration."
        }
    ];

    return cards.map(card => `
        <article class="analysis-card">
            <strong>${escapeHtml(card.title)}</strong>
            <div class="analysis-card-value">${escapeHtml(card.value)}</div>
            <div class="table-inline-note">${escapeHtml(card.detail)}</div>
        </article>
    `).join("");
}

function renderAnalysisCharts(snapshot) {
    const trends = snapshot.trends ?? [];
    const overallValues = trends.map(point => point.overallScore);
    const cohesionValues = trends.map(point => point.cohesionScore);
    const pressureValues = trends.map(point => point.pressureScore);
    const downstateValues = trends.map(point => point.downstateScore);
    const resilienceValues = trends.map(point => point.resilienceScore);

    const cards = [
        buildAnalysisChartCard("Overall trend", snapshot.overview?.averageOverallScore != null ? formatNumber(snapshot.overview.averageOverallScore, 1) : "n/a", overallValues, `${trends.length} fights in date order.`),
        buildAnalysisChartCard("Cohesion trend", snapshot.overview?.averageCohesionScore != null ? formatNumber(snapshot.overview.averageCohesionScore, 1) : "n/a", cohesionValues, "Cohesion & positioning pillar."),
        buildAnalysisChartCard("Pressure trend", snapshot.overview?.averagePressureScore != null ? formatNumber(snapshot.overview.averagePressureScore, 1) : "n/a", pressureValues, "Pressure & burst pillar."),
        buildAnalysisChartCard("Downstate trend", snapshot.overview?.averageDownstateScore != null ? formatNumber(snapshot.overview.averageDownstateScore, 1) : "n/a", downstateValues, "Downstate control pillar."),
        buildAnalysisChartCard("Resilience trend", snapshot.overview?.averageResilienceScore != null ? formatNumber(snapshot.overview.averageResilienceScore, 1) : "n/a", resilienceValues, "Resilience & stabilization pillar.")
    ];

    setInnerHtml("#analysis-chart-grid", cards.join(""));
}

function getAnalysisPlayerSearchText(player) {
    return [
        player.account,
        player.displayName,
        ...(player.characterNames ?? []),
        ...(player.classesPlayed ?? [])
    ]
        .filter(Boolean)
        .join(" ")
        .toLowerCase();
}

function getAnalysisPlayerSortValue(player, sortKey) {
    switch (sortKey) {
        case "player":
            return String(player.account ?? "").toLowerCase();
        case "appearances":
            return Number(player.fightCount ?? 0);
        case "record":
            return Number(player.winRatePercent ?? 0);
        case "impact":
        default:
            return getSelectedAnalysisPlayerImpactValue(player);
    }
}

function getDefaultAnalysisPlayerSortDirection(sortKey) {
    return sortKey === "player" ? "asc" : "desc";
}

function updateAnalysisPlayerSortHeaders() {
    document.querySelectorAll("[data-analysis-player-sort]").forEach(button => {
        const isActive = button.dataset.analysisPlayerSort === analysisPlayerSortState.key;
        button.classList.toggle("is-active", isActive);
        button.dataset.sortDirection = isActive ? analysisPlayerSortState.direction : "";
        button.setAttribute("aria-sort", isActive ? (analysisPlayerSortState.direction === "asc" ? "ascending" : "descending") : "none");
    });
}

function setAnalysisPlayerSort(sortKey) {
    if (!sortKey) {
        return;
    }

    if (analysisPlayerSortState.key === sortKey) {
        analysisPlayerSortState = {
            key: sortKey,
            direction: analysisPlayerSortState.direction === "asc" ? "desc" : "asc"
        };
    } else {
        analysisPlayerSortState = {
            key: sortKey,
            direction: getDefaultAnalysisPlayerSortDirection(sortKey)
        };
    }

    if (currentAnalysisSnapshot) {
        renderAnalysisPlayers(currentAnalysisSnapshot);
    } else {
        updateAnalysisPlayerSortHeaders();
    }
}

function getAnalysisClassSortValue(classRow, sortKey) {
    switch (sortKey) {
        case "class":
            return String(classRow.classLabel ?? "").toLowerCase();
        case "samples":
            return Number(classRow.sampleCount ?? 0);
        case "record":
            return Number(classRow.winRatePercent ?? 0);
        case "impact":
        default:
            return Number(classRow.contributionScore ?? 0);
    }
}

function getDefaultAnalysisClassSortDirection(sortKey) {
    return sortKey === "class" ? "asc" : "desc";
}

function updateAnalysisClassSortHeaders() {
    document.querySelectorAll("[data-analysis-class-sort]").forEach(button => {
        const isActive = button.dataset.analysisClassSort === analysisClassSortState.key;
        button.classList.toggle("is-active", isActive);
        button.dataset.sortDirection = isActive ? analysisClassSortState.direction : "";
        button.setAttribute("aria-sort", isActive ? (analysisClassSortState.direction === "asc" ? "ascending" : "descending") : "none");
    });
}

function setAnalysisClassSort(sortKey) {
    if (!sortKey) {
        return;
    }

    if (analysisClassSortState.key === sortKey) {
        analysisClassSortState = {
            key: sortKey,
            direction: analysisClassSortState.direction === "asc" ? "desc" : "asc"
        };
    } else {
        analysisClassSortState = {
            key: sortKey,
            direction: getDefaultAnalysisClassSortDirection(sortKey)
        };
    }

    if (currentAnalysisSnapshot) {
        renderAnalysisClasses(currentAnalysisSnapshot);
    } else {
        updateAnalysisClassSortHeaders();
    }
}

function getAnalysisClassPlayerSortValue(player, sortKey) {
    switch (sortKey) {
        case "player":
            return String(player.account ?? "").toLowerCase();
        case "appearances":
            return Number(player.fightCount ?? 0);
        case "record":
            return Number(player.winRatePercent ?? 0);
        case "lanefit":
            return Number(player.averagePrimaryLaneScore ?? 0);
        case "impact":
        default:
            return Number(player.impactScore ?? 0);
    }
}

function getDefaultAnalysisClassPlayerSortDirection(sortKey) {
    return sortKey === "player" ? "asc" : "desc";
}

function updateAnalysisClassPlayerSortHeaders() {
    document.querySelectorAll("[data-analysis-class-player-sort]").forEach(button => {
        const isActive = button.dataset.analysisClassPlayerSort === analysisClassPlayerSortState.key;
        button.classList.toggle("is-active", isActive);
        button.dataset.sortDirection = isActive ? analysisClassPlayerSortState.direction : "";
        button.setAttribute("aria-sort", isActive ? (analysisClassPlayerSortState.direction === "asc" ? "ascending" : "descending") : "none");
    });
}

function setAnalysisClassPlayerSort(sortKey) {
    if (!sortKey) {
        return;
    }

    if (analysisClassPlayerSortState.key === sortKey) {
        analysisClassPlayerSortState = {
            key: sortKey,
            direction: analysisClassPlayerSortState.direction === "asc" ? "desc" : "asc"
        };
    } else {
        analysisClassPlayerSortState = {
            key: sortKey,
            direction: getDefaultAnalysisClassPlayerSortDirection(sortKey)
        };
    }

    if (currentAnalysisSnapshot) {
        renderAnalysisClasses(currentAnalysisSnapshot);
    } else {
        updateAnalysisClassPlayerSortHeaders();
    }
}

function getSelectedAnalysisPlayerLaneKey() {
    return document.querySelector("#analysis-player-lane-filter")?.value ?? "all";
}

function getSelectedAnalysisPlayerLaneLabel() {
    const select = document.querySelector("#analysis-player-lane-filter");
    const option = select?.selectedOptions?.[0];
    return option?.textContent?.trim() || "All lanes";
}

function getAnalysisPlayerLaneOptions(snapshot) {
    const laneMap = new Map();

    (snapshot.topPlayers ?? []).forEach(player => {
        (player.characters ?? [])
            .filter(character => Number(character.totalFightCountAll ?? character.fightCount ?? 0) >= 10)
            .forEach(character => {
                (character.laneContributions ?? []).forEach(lane => {
                    const laneKey = String(lane.laneKey ?? "").trim();
                    const laneLabel = String(lane.laneLabel ?? "").trim();
                    if (!laneKey || !laneLabel || laneMap.has(laneKey)) {
                        return;
                    }

                    laneMap.set(laneKey, laneLabel);
                });
            });
    });

    return [
        { key: "all", label: "All lanes" },
        ...Array.from(laneMap.entries())
            .map(([key, label]) => ({ key, label }))
            .sort((left, right) => left.label.localeCompare(right.label, undefined, { sensitivity: "base" }))
    ];
}

function renderAnalysisPlayerLaneOptions(snapshot) {
    const select = document.querySelector("#analysis-player-lane-filter");
    if (!select) {
        return;
    }

    const previousValue = select.value || "all";
    const options = getAnalysisPlayerLaneOptions(snapshot);
    const nextValue = options.some(option => stringEqualsIgnoreCase(option.key, previousValue))
        ? previousValue
        : "all";

    select.innerHTML = options
        .map(option => `<option value="${escapeHtml(option.key)}">${escapeHtml(option.label)}</option>`)
        .join("");
    select.value = nextValue;
}

function getBestAnalysisPlayerLaneMatch(player, laneKey) {
    if (!laneKey || stringEqualsIgnoreCase(laneKey, "all")) {
        return null;
    }

    const matches = (player.characters ?? [])
        .filter(character => Number(character.totalFightCountAll ?? character.fightCount ?? 0) >= 10)
        .flatMap(character => (character.laneContributions ?? [])
            .filter(lane => stringEqualsIgnoreCase(lane.laneKey, laneKey))
            .map(lane => ({ character, lane })))
        .sort((left, right) => Number(right.lane.overallStrengthPercent ?? 0) - Number(left.lane.overallStrengthPercent ?? 0)
            || Number(right.character.totalFightCountAll ?? right.character.fightCount ?? 0) - Number(left.character.totalFightCountAll ?? left.character.fightCount ?? 0)
            || String(left.character.characterName ?? "").localeCompare(String(right.character.characterName ?? ""), undefined, { sensitivity: "base" }));

    return matches[0] ?? null;
}

function getLaneContributionByKey(collection, laneKey) {
    return (collection ?? []).find(lane => stringEqualsIgnoreCase(lane.laneKey, laneKey)) ?? null;
}

function getSelectedAnalysisPlayerImpactValue(player) {
    const laneKey = getSelectedAnalysisPlayerLaneKey();
    if (stringEqualsIgnoreCase(laneKey, "all")) {
        return Number(player.impactScore ?? 0);
    }

    const match = getBestAnalysisPlayerLaneMatch(player, laneKey);
    return match ? Number(match.lane.overallStrengthPercent ?? 0) : 0;
}

function getSelectedAnalysisPlayerImpactDetail(player) {
    const laneKey = getSelectedAnalysisPlayerLaneKey();
    if (stringEqualsIgnoreCase(laneKey, "all")) {
        return {
            value: Number(player.impactScore ?? 0),
            note: `${formatPercent(player.averageWeightedLaneScore)} weighted lane`
        };
    }

    const laneLabel = getSelectedAnalysisPlayerLaneLabel();
    const match = getBestAnalysisPlayerLaneMatch(player, laneKey);
    if (!match) {
        return {
            value: 0,
            note: `No ${laneLabel} signal on any character with at least 10 total fights`
        };
    }

    return {
        value: Number(match.lane.overallStrengthPercent ?? 0),
        note: `${match.character.characterName} / ${match.character.classLabel} | ${formatPercent(match.lane.appearanceRatePercent)} appearance | ${formatPercent(match.lane.overallSharePercent)} overall share`
    };
}

function shouldIncludeAnalysisPlayerForSelectedLane(player) {
    const laneKey = getSelectedAnalysisPlayerLaneKey();
    if (stringEqualsIgnoreCase(laneKey, "all")) {
        return true;
    }

    const match = getBestAnalysisPlayerLaneMatch(player, laneKey);
    return Number(match?.lane?.totalSamplesAll ?? match?.lane?.samples ?? 0) >= MINIMUM_LANE_FILTER_APPEARANCES;
}

function getQualifiedLanePlayers(snapshot, laneKey) {
    return (snapshot.topPlayers ?? [])
        .filter(player => Number(player.totalFightCountAll ?? player.fightCount ?? 0) >= MINIMUM_PLAYER_TABLE_FIGHTS)
        .map(player => {
            const match = getBestAnalysisPlayerLaneMatch(player, laneKey);
            if (!match || Number(match.lane?.totalSamplesAll ?? match.lane?.samples ?? 0) < MINIMUM_LANE_FILTER_APPEARANCES) {
                return null;
            }

            return {
                account: player.account,
                displayName: player.displayName,
                filteredFightCount: player.fightCount,
                totalFightCount: player.totalFightCountAll ?? player.fightCount,
                characterName: match.character.characterName,
                classLabel: match.character.classLabel,
                lane: match.lane,
                impactScore: Number(match.lane.overallStrengthPercent ?? 0),
                winRatePercent: Number(match.character.winRatePercent ?? 0),
                characterFightCount: Number(match.character.totalFightCountAll ?? match.character.fightCount ?? 0)
            };
        })
        .filter(Boolean)
        .sort((left, right) => Number(right.impactScore ?? 0) - Number(left.impactScore ?? 0)
            || Number(right.lane?.overallSharePercent ?? 0) - Number(left.lane?.overallSharePercent ?? 0)
            || Number(right.lane?.totalSamplesAll ?? right.lane?.samples ?? 0) - Number(left.lane?.totalSamplesAll ?? left.lane?.samples ?? 0)
            || compareFightBrowserValues(String(left.account ?? "").toLowerCase(), String(right.account ?? "").toLowerCase()));
}

function getQualifiedLaneClasses(snapshot, laneKey) {
    return (snapshot.topClasses ?? [])
        .map(classRow => {
            const lane = getLaneContributionByKey(classRow.laneContributions, laneKey);
            if (!lane) {
                return null;
            }

            return {
                classLabel: classRow.classLabel,
                sampleCount: classRow.sampleCount,
                contributionScore: classRow.contributionScore,
                topPlayerDisplayName: classRow.topPlayerDisplayName,
                lane
            };
        })
        .filter(Boolean)
        .sort((left, right) => Number(right.lane?.overallStrengthPercent ?? 0) - Number(left.lane?.overallStrengthPercent ?? 0)
            || Number(right.lane?.overallSharePercent ?? 0) - Number(left.lane?.overallSharePercent ?? 0)
            || Number(right.sampleCount ?? 0) - Number(left.sampleCount ?? 0)
            || compareFightBrowserValues(String(left.classLabel ?? "").toLowerCase(), String(right.classLabel ?? "").toLowerCase()));
}

function getFilteredAnalysisPlayers(snapshot) {
    const searchValue = document.querySelector("#analysis-player-search")?.value.trim().toLowerCase() ?? "";
    const players = (snapshot.topPlayers ?? [])
        .filter(player => Number(player.totalFightCountAll ?? player.fightCount ?? 0) >= 10)
        .filter(player => {
            const matchesSearch = !searchValue || getAnalysisPlayerSearchText(player).includes(searchValue);
            if (!matchesSearch) {
                return false;
            }

            if (searchValue) {
                return true;
            }

            return Number(player.totalFightCountAll ?? player.fightCount ?? 0) >= MINIMUM_PLAYER_TABLE_FIGHTS;
        })
        .filter(player => shouldIncludeAnalysisPlayerForSelectedLane(player));

    const sorted = [...players].sort((left, right) => {
        const primary = compareFightBrowserValues(
            getAnalysisPlayerSortValue(left, analysisPlayerSortState.key),
            getAnalysisPlayerSortValue(right, analysisPlayerSortState.key));
        if (primary !== 0) {
            return analysisPlayerSortState.direction === "asc" ? primary : -primary;
        }

        return Number(right.fightCount ?? 0) - Number(left.fightCount ?? 0)
            || compareFightBrowserValues(String(left.account ?? "").toLowerCase(), String(right.account ?? "").toLowerCase());
    });

    return sorted;
}

function buildAnalysisPlayerRow(player, isSelected) {
    const rowClasses = ["is-clickable"];
    if (isSelected) {
        rowClasses.push("is-selected");
    }

    const characterCount = (player.characterNames ?? []).length;
    const playerNote = player.displayName && !stringEqualsIgnoreCase(player.displayName, player.account)
        ? `Most-played character: ${player.displayName}`
        : null;
    const impactDetail = getSelectedAnalysisPlayerImpactDetail(player);

    return `
        <tr class="${rowClasses.join(" ")}" data-player-account="${escapeHtml(player.account)}">
            <td>
                <div class="table-stack">
                    <strong class="mono">${escapeHtml(player.account)}</strong>
                    ${playerNote ? `<span class="table-inline-note">${escapeHtml(playerNote)}</span>` : ""}
                    <span class="table-inline-note">${escapeHtml(`${characterCount} character${characterCount === 1 ? "" : "s"}`)}</span>
                </div>
            </td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(formatNumber(player.fightCount))}</strong>
                    <span class="table-inline-note">${escapeHtml("Filtered fights")}</span>
                </div>
            </td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(formatPercent(player.winRatePercent))}</strong>
                    <span class="table-inline-note">${escapeHtml(`${player.winCount}-${player.lossCount}-${player.drawCount}`)}</span>
                </div>
            </td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(formatNumber(impactDetail.value, 1))}</strong>
                    <span class="table-inline-note">${escapeHtml(impactDetail.note)}</span>
                </div>
            </td>
            <td>${escapeHtml((player.classesPlayed ?? []).join(", ") || "-")}</td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(player.primaryLaneLabel ?? "Unclassified")}</strong>
                    <span class="table-inline-note">${escapeHtml(`${formatPercent(player.averagePrimaryLaneScore)} primary | ${formatPercent(player.averageWeightedLaneScore)} weighted`)}</span>
                </div>
            </td>
        </tr>
    `;
}

function buildCharacterObservedLead(character) {
    const topLanes = (character.laneContributions ?? []).slice(0, 2).map(lane => lane.laneLabel);
    if (topLanes.length === 0) {
        return "Observed contribution is still unclassified across the filtered fights.";
    }

    const joined = topLanes.length === 1 ? topLanes[0] : `${topLanes[0]} + ${topLanes[1]}`;
    return `Observed contribution leaned ${joined}${character.fightCount < 5 ? ", but the sample is still thin." : " across the filtered fights."}`;
}

function buildCharacterObservedCopy(character) {
    const strongest = character.laneContributions?.[0];
    const second = character.laneContributions?.[1];
    if (!strongest) {
        return "No lane contribution detail was retained for this character.";
    }

    const strongestLabel = `${strongest.laneLabel} (${formatPercent(strongest.overallStrengthPercent)})`;
    const secondLabel = second ? `, then ${second.laneLabel} (${formatPercent(second.overallStrengthPercent)})` : "";
    return `Weighted lane score averaged ${formatPercent(character.averageWeightedLaneScore)} across ${character.fightCount} fights. Strongest collection-wide lane was ${strongestLabel}${secondLabel}.`;
}

function getCharacterLaneMetric(lane, key) {
    return (lane.metrics ?? []).find(metric => stringEqualsIgnoreCase(metric.key, key)) ?? null;
}

function formatCharacterLaneMetricValue(value, unit) {
    switch ((unit ?? "").toLowerCase()) {
        case "seconds":
            return `${formatNumber(value, 1)}s`;
        case "count":
            return formatNumber(value, 1);
        default:
            return formatNumber(value);
    }
}

function formatCharacterLaneMetric(metric) {
    if (!metric) {
        return "0";
    }

    return formatCharacterLaneMetricValue(metric.averagePerAppearance, metric.unit);
}

function formatCharacterLaneMetricPerFight(lane, key, totalFights) {
    const metric = getCharacterLaneMetric(lane, key);
    if (!metric || !totalFights) {
        return "0";
    }

    return formatCharacterLaneMetricValue(metric.totalValue / totalFights, metric.unit);
}

function buildCharacterLaneMetricCopy(lane, totalFights) {
    switch ((lane.laneKey ?? "").toLowerCase()) {
        case "pressure":
            return `${formatCharacterLaneMetricPerFight(lane, "liveTargetDamage", totalFights)} live-target damage and ${formatCharacterLaneMetricPerFight(lane, "preDownDamage", totalFights)} pre-down contribution per filtered fight.`;
        case "conversion":
            return `${formatCharacterLaneMetricPerFight(lane, "finishContributionDamage", totalFights)} finish contribution and ${formatCharacterLaneMetricPerFight(lane, "againstDownedDamage", totalFights)} against-downed damage per filtered fight.`;
        case "strip":
            return `${formatCharacterLaneMetricPerFight(lane, "stripsTotal", totalFights)} strips and ${formatCharacterLaneMetricPerFight(lane, "stripDownContribution", totalFights)} down-linked strips per filtered fight.`;
        case "control":
            return `${formatCharacterLaneMetricPerFight(lane, "effectiveCrowdControlCount", totalFights)} effective CC events and ${formatCharacterLaneMetricPerFight(lane, "crowdControlDownContribution", totalFights)} CC-linked downs per filtered fight.`;
        case "boonsupport":
            return `${formatCharacterLaneMetricPerFight(lane, "totalBoonSupport", totalFights)} total boon-seconds per filtered fight, split between ${formatCharacterLaneMetricPerFight(lane, "defensiveBoonSupport", totalFights)} defensive and ${formatCharacterLaneMetricPerFight(lane, "offensiveBoonSupport", totalFights)} offensive coverage.`;
        case "recovery":
            return `${formatCharacterLaneMetricPerFight(lane, "cleansesTotal", totalFights)} cleanses, ${formatCharacterLaneMetricPerFight(lane, "healingTotal", totalFights)} healing, and ${formatCharacterLaneMetricPerFight(lane, "barrierTotal", totalFights)} barrier per filtered fight.`;
        case "rez":
            return `${formatCharacterLaneMetricPerFight(lane, "squadRecoveryWindowsHelped", totalFights)} recoveries helped, ${formatCharacterLaneMetricPerFight(lane, "rezTimeOnRecoveries", totalFights)} rez time, and ${formatCharacterLaneMetricPerFight(lane, "downedHealingOnRecoveries", totalFights)} downed healing per filtered fight.`;
        default:
            return lane.evidenceLine || "No aggregate lane metrics were retained for this lane.";
    }
}

function buildCharacterLaneCard(lane, totalFights) {
    const pillLabel = lane.rateBand || (lane.overallStrengthPercent >= 40 ? "High" : lane.overallStrengthPercent >= 15 ? "Medium" : "Low");
    const safeTotalFights = totalFights || lane.samples || 0;

    return `
        <article class="analysis-character-lane-card">
            <div class="analysis-character-lane-header">
                <strong>${escapeHtml(lane.laneLabel)}</strong>
                <span class="analysis-character-lane-value">${escapeHtml(formatPercent(lane.overallStrengthPercent))}</span>
            </div>
            <div class="analysis-character-meta">
                <span class="analysis-character-pill">${escapeHtml(pillLabel)}</span>
            </div>
            <p class="analysis-character-copy">${escapeHtml(`${lane.samples} of ${formatNumber(safeTotalFights)} fights | ${formatPercent(lane.appearanceRatePercent)} appearance rate | ${formatPercent(lane.overallSharePercent)} overall share`)}</p>
            <p class="table-inline-note">${escapeHtml(buildCharacterLaneMetricCopy(lane, safeTotalFights))}</p>
        </article>
    `;
}

function buildAnalysisCharacterCard(character) {
    const disciplineValue = character.averageInPositionRate != null
        ? `${formatPercent(character.averageInPositionRate)} in position`
        : "No positioning sample";
    const disciplineCopy = character.averageInPositionRate != null
        ? `${formatPercent(character.averageTooFarRate)} too far, ${formatPercent(character.averageOverextendedRate)} overextended, ${formatPercent(character.averageLateralRiskRate)} lateral risk`
        : "Commander-relative replay samples were not available for this character.";
    const classText = character.classLabel || "Unknown class";

    return `
        <article class="analysis-character-card">
            <div class="analysis-character-header">
                <div>
                    <strong>${escapeHtml(character.characterName)}</strong>
                    <div class="table-inline-note">${escapeHtml(classText)}</div>
                </div>
                <div class="analysis-character-meta">
                    <span class="analysis-character-pill">${escapeHtml(`${character.fightCount} fights`)}</span>
                    <span class="analysis-character-pill">${escapeHtml(`${formatPercent(character.winRatePercent)} wins`)}</span>
                    <span class="analysis-character-pill">${escapeHtml(`Impact ${formatNumber(character.impactScore, 1)}`)}</span>
                    <span class="analysis-character-pill">${escapeHtml(`${character.confidenceLabel ?? "Unknown"} confidence`)}</span>
                </div>
            </div>
            <p class="analysis-character-lead">${escapeHtml(buildCharacterObservedLead(character))}</p>
            <p class="analysis-character-copy">${escapeHtml(buildCharacterObservedCopy(character))}</p>
            ${character.confidenceDetail ? `<p class="table-inline-note">${escapeHtml(character.confidenceDetail)}</p>` : ""}
            <div class="analysis-character-lane-grid">
                ${(character.laneContributions ?? []).length
                    ? character.laneContributions.map(lane => buildCharacterLaneCard(lane, character.fightCount)).join("")
                    : `<article class="analysis-character-lane-card"><p class="analysis-character-copy">No lane contribution cards were retained for this character.</p></article>`}
            </div>
            <div class="analysis-character-footer">
                <div class="analysis-character-stat">
                    <strong>Discipline</strong>
                    <div>${escapeHtml(disciplineValue)}</div>
                    <div class="table-inline-note">${escapeHtml(disciplineCopy)}</div>
                </div>
                <div class="analysis-character-stat">
                    <strong>Survival</strong>
                    <div>${escapeHtml(`${formatNumber(character.averageDeathsPerFight, 1)} deaths/fight`)}</div>
                    <div class="table-inline-note">${escapeHtml(`${formatNumber(character.averageRecoveriesPerFight, 1)} recoveries/fight`)}</div>
                </div>
                <div class="analysis-character-stat">
                    <strong>Participation</strong>
                    <div>${escapeHtml(`${formatPercent(character.averageActivePresencePercent)} active`)}</div>
                    <div class="table-inline-note">${escapeHtml(`${formatPercent(character.averageEngagedPresencePercent)} engaged presence`)}</div>
                </div>
            </div>
        </article>
    `;
}

function renderAnalysisPlayerDetail(player) {
    const container = document.querySelector("#analysis-player-detail");
    if (!player) {
        container.innerHTML = "";
        return;
    }

    container.className = "analysis-player-detail";
    container.innerHTML = `
        <div class="section-heading">
            <div>
                <h3>${escapeHtml(player.account)}</h3>
                <p>Character/class breakdown across the current analysis filter.</p>
            </div>
        </div>
        <div class="analysis-character-grid">
            ${(player.characters ?? []).map(buildAnalysisCharacterCard).join("")}
        </div>
    `;
}

function renderAnalysisPlayers(snapshot) {
    const body = document.querySelector("#analysis-players-body");
    const summary = document.querySelector("#analysis-players-summary");
    renderAnalysisPlayerLaneOptions(snapshot);
    const filteredPlayers = getFilteredAnalysisPlayers(snapshot);
    updateAnalysisPlayerSortHeaders();
    const selectedLaneLabel = getSelectedAnalysisPlayerLaneLabel();
    const hasSearchValue = Boolean(document.querySelector("#analysis-player-search")?.value.trim());
    const laneScopeSummary = stringEqualsIgnoreCase(getSelectedAnalysisPlayerLaneKey(), "all")
        ? "Impact shows overall impact."
        : `Impact shows the best ${selectedLaneLabel} value from any character/spec card with at least 10 total fights, and only players with at least ${MINIMUM_LANE_FILTER_APPEARANCES} total ${selectedLaneLabel} appearances are shown.`;
    const thresholdSummary = hasSearchValue
        ? `Search override is active, so matching players below ${MINIMUM_PLAYER_TABLE_FIGHTS} total imported fights can still appear.`
        : `By default only players with at least ${MINIMUM_PLAYER_TABLE_FIGHTS} total imported fights are shown.`;

    summary.textContent = filteredPlayers.length > 0
        ? `Showing ${filteredPlayers.length} players. ${thresholdSummary} ${laneScopeSummary}`
        : `No players matched the current filters and thresholds. ${thresholdSummary} ${laneScopeSummary}`;

    if (filteredPlayers.length === 0) {
        selectedAnalysisPlayerAccount = null;
        body.innerHTML = `<tr><td colspan="6">No player rows matched the current filters.</td></tr>`;
        renderAnalysisPlayerDetail(null);
        return;
    }

    if (!filteredPlayers.some(player => stringEqualsIgnoreCase(player.account, selectedAnalysisPlayerAccount))) {
        selectedAnalysisPlayerAccount = filteredPlayers[0].account;
    }

    body.innerHTML = filteredPlayers
        .map(player => buildAnalysisPlayerRow(player, stringEqualsIgnoreCase(player.account, selectedAnalysisPlayerAccount)))
        .join("");

    const selectedPlayer = filteredPlayers.find(player => stringEqualsIgnoreCase(player.account, selectedAnalysisPlayerAccount)) ?? filteredPlayers[0];
    renderAnalysisPlayerDetail(selectedPlayer);
}

function buildAnalysisClassRow(classRow) {
    const rowClasses = ["is-clickable"];
    if (stringEqualsIgnoreCase(classRow.classLabel, selectedAnalysisClassLabel)) {
        rowClasses.push("is-selected");
    }

    return `
        <tr class="${rowClasses.join(" ")}" data-class-label="${escapeHtml(classRow.classLabel)}">
            <td><strong>${escapeHtml(classRow.classLabel)}</strong></td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(String(classRow.sampleCount))}</strong>
                    <span class="table-inline-note">${escapeHtml(`${classRow.distinctAccounts} accounts`)}</span>
                </div>
            </td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(formatNumber(classRow.contributionScore, 1))}</strong>
                    <span class="table-inline-note">${escapeHtml(`${formatPercent(classRow.averageWeightedLaneScore)} weighted lane`)}</span>
                </div>
            </td>
            <td>${escapeHtml(`${classRow.winCount}-${classRow.lossCount}-${classRow.drawCount} | ${formatPercent(classRow.winRatePercent)} wins`)}</td>
            <td>${escapeHtml(classRow.topPlayerDisplayName ?? "-")}</td>
        </tr>
    `;
}

function getSortedAnalysisClasses(snapshot) {
    const classes = [...(snapshot.topClasses ?? [])];
    classes.sort((left, right) => {
        const primary = compareFightBrowserValues(
            getAnalysisClassSortValue(left, analysisClassSortState.key),
            getAnalysisClassSortValue(right, analysisClassSortState.key));
        if (primary !== 0) {
            return analysisClassSortState.direction === "asc" ? primary : -primary;
        }

        return Number(right.sampleCount ?? 0) - Number(left.sampleCount ?? 0)
            || compareFightBrowserValues(String(left.classLabel ?? "").toLowerCase(), String(right.classLabel ?? "").toLowerCase());
    });

    return classes;
}

function buildAnalysisClassPlayerRow(player) {
    const playerNote = player.displayName && !stringEqualsIgnoreCase(player.displayName, player.account)
        ? `Most-played character: ${player.displayName}`
        : null;

    return `
        <tr>
            <td>
                <div class="table-stack">
                    <strong class="mono">${escapeHtml(player.account)}</strong>
                    ${playerNote ? `<span class="table-inline-note">${escapeHtml(playerNote)}</span>` : ""}
                </div>
            </td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(formatNumber(player.fightCount))}</strong>
                    <span class="table-inline-note">Class fights</span>
                </div>
            </td>
            <td>${escapeHtml(`${player.winCount}-${player.lossCount}-${player.drawCount} | ${formatPercent(player.winRatePercent)} wins`)}</td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(formatNumber(player.impactScore, 1))}</strong>
                    <span class="table-inline-note">${escapeHtml(`${formatPercent(player.averageWeightedLaneScore)} weighted lane`)}</span>
                </div>
            </td>
            <td>${escapeHtml(`${player.primaryLaneLabel ?? "Unclassified"} | ${formatPercent(player.averagePrimaryLaneScore)} primary`)}</td>
        </tr>
    `;
}

function renderAnalysisClassDetail(classRow) {
    const container = document.querySelector("#analysis-class-detail");
    if (!classRow) {
        container.innerHTML = "";
        return;
    }

    const topPlayerCopy = classRow.topPlayerDisplayName
        ? `Top player requires at least 20 total fights on the class. Current leader: ${classRow.topPlayerDisplayName}.`
        : "No player has reached the 20-total-fight threshold on this class yet.";
    const sortedPlayers = [...(classRow.players ?? [])].sort((left, right) => {
        const primary = compareFightBrowserValues(
            getAnalysisClassPlayerSortValue(left, analysisClassPlayerSortState.key),
            getAnalysisClassPlayerSortValue(right, analysisClassPlayerSortState.key));
        if (primary !== 0) {
            return analysisClassPlayerSortState.direction === "asc" ? primary : -primary;
        }

        return Number(right.impactScore ?? 0) - Number(left.impactScore ?? 0)
            || Number(right.fightCount ?? 0) - Number(left.fightCount ?? 0)
            || compareFightBrowserValues(String(left.account ?? "").toLowerCase(), String(right.account ?? "").toLowerCase());
    });

    container.className = "analysis-player-detail";
    container.innerHTML = `
        <div class="section-heading">
            <div>
                <h3>${escapeHtml(classRow.classLabel)}</h3>
                <p>${escapeHtml(`${classRow.sampleCount} class samples across ${classRow.distinctAccounts} accounts. ${topPlayerCopy}`)}</p>
            </div>
        </div>
        <div class="analysis-character-grid">
            <article class="analysis-character-card">
                <div class="analysis-character-header">
                    <div>
                        <strong>${escapeHtml(classRow.classLabel)}</strong>
                        <div class="table-inline-note">Class lane profile across the current analysis filter</div>
                    </div>
                    <div class="analysis-character-meta">
                        <span class="analysis-character-pill">${escapeHtml(`${classRow.sampleCount} samples`)}</span>
                        <span class="analysis-character-pill">${escapeHtml(`${formatPercent(classRow.winRatePercent)} wins`)}</span>
                        <span class="analysis-character-pill">${escapeHtml(`Impact ${formatNumber(classRow.contributionScore, 1)}`)}</span>
                    </div>
                </div>
                <p class="analysis-character-lead">${escapeHtml(`Observed contribution leaned across ${classRow.classLabel}'s strongest lanes in the filtered fights.`)}</p>
                <p class="analysis-character-copy">${escapeHtml(`Weighted lane score averaged ${formatPercent(classRow.averageWeightedLaneScore)} across ${classRow.sampleCount} class appearances.`)}</p>
                <div class="analysis-character-lane-grid">
                    ${(classRow.laneContributions ?? []).length
                        ? classRow.laneContributions.map(lane => buildCharacterLaneCard(lane, classRow.sampleCount)).join("")
                        : `<article class="analysis-character-lane-card"><p class="analysis-character-copy">No lane contribution cards were retained for this class.</p></article>`}
                </div>
            </article>
        </div>
        <div class="section-heading">
            <div>
                <h3>Players on ${escapeHtml(classRow.classLabel)}</h3>
                <p>Sorted by overall class impact.</p>
            </div>
        </div>
        <div class="table-shell table-shell-scroll table-shell-analysis-players">
            <table class="data-table">
                <thead>
                    <tr>
                        <th><button class="sort-header" type="button" data-analysis-class-player-sort="player">Player</button></th>
                        <th><button class="sort-header" type="button" data-analysis-class-player-sort="appearances">Appearances</button></th>
                        <th><button class="sort-header" type="button" data-analysis-class-player-sort="record">Record</button></th>
                        <th><button class="sort-header" type="button" data-analysis-class-player-sort="impact">Impact</button></th>
                        <th><button class="sort-header" type="button" data-analysis-class-player-sort="lanefit">Lane fit</button></th>
                    </tr>
                </thead>
                <tbody>
                    ${(sortedPlayers.length
                        ? sortedPlayers.map(buildAnalysisClassPlayerRow).join("")
                        : `<tr><td colspan="5">No player rows were retained for this class.</td></tr>`)}
                </tbody>
            </table>
        </div>
    `;

    updateAnalysisClassPlayerSortHeaders();
}

function renderAnalysisClasses(snapshot) {
    const body = document.querySelector("#analysis-classes-body");
    const sortedClasses = getSortedAnalysisClasses(snapshot);
    updateAnalysisClassSortHeaders();

    if (sortedClasses.length === 0) {
        selectedAnalysisClassLabel = null;
        body.innerHTML = `<tr><td colspan="5">No class rows matched the current filters.</td></tr>`;
        renderAnalysisClassDetail(null);
        return;
    }

    if (!sortedClasses.some(classRow => stringEqualsIgnoreCase(classRow.classLabel, selectedAnalysisClassLabel))) {
        selectedAnalysisClassLabel = sortedClasses[0].classLabel;
    }

    body.innerHTML = sortedClasses
        .map(classRow => buildAnalysisClassRow(classRow))
        .join("");

    const selectedClass = sortedClasses.find(classRow => stringEqualsIgnoreCase(classRow.classLabel, selectedAnalysisClassLabel)) ?? sortedClasses[0];
    renderAnalysisClassDetail(selectedClass);
}

function renderAnalysisLanes(snapshot) {
    setInnerHtml(
        "#analysis-lanes-body",
        (snapshot.topLanes?.length
            ? snapshot.topLanes.map(buildAnalysisLaneRow).join("")
            : `<tr><td colspan="7">No lane summaries matched the current filters.</td></tr>`));

    if (!snapshot.topLanes?.length) {
        selectedAnalysisLaneKey = null;
        renderAnalysisLaneDetail(snapshot);
        return;
    }

    if (!snapshot.topLanes.some(lane => stringEqualsIgnoreCase(lane.laneKey, selectedAnalysisLaneKey))) {
        selectedAnalysisLaneKey = snapshot.topLanes[0].laneKey;
    }

    setInnerHtml(
        "#analysis-lanes-body",
        snapshot.topLanes.map(buildAnalysisLaneRow).join(""));
    renderAnalysisLaneDetail(snapshot);
}

function buildAnalysisLaneRow(lane) {
    const rowClasses = ["is-clickable"];
    if (stringEqualsIgnoreCase(lane.laneKey, selectedAnalysisLaneKey)) {
        rowClasses.push("is-selected");
    }

    return `
        <tr class="${rowClasses.join(" ")}" data-lane-key="${escapeHtml(lane.laneKey)}">
            <td><strong>${escapeHtml(lane.laneLabel)}</strong></td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(`${lane.samples} samples`)}</strong>
                    <span class="table-inline-note">${escapeHtml(`${lane.distinctAccounts} accounts | ${lane.distinctClasses} classes`)}</span>
                </div>
            </td>
            <td>${escapeHtml(formatPercent(lane.averageStrengthPercent))}</td>
            <td>${escapeHtml(`${formatPercent(lane.appearanceRatePercent)} fights | ${formatPercent(lane.averageSharePercent)} share`)}</td>
            <td>${escapeHtml(lane.topClassLabel ?? "-")}</td>
            <td>${escapeHtml(lane.topPlayerDisplayName ?? "-")}</td>
            <td>${escapeHtml(lane.evidenceLine ?? "-")}</td>
        </tr>
    `;
}

function setActiveAnalysisLaneDetailTab(tabKey) {
    activeAnalysisLaneDetailTab = tabKey;

    document.querySelectorAll("[data-analysis-lane-detail-tab]").forEach(button => {
        const isActive = button.dataset.analysisLaneDetailTab === tabKey;
        button.classList.toggle("is-active", isActive);
        button.setAttribute("aria-selected", isActive ? "true" : "false");
    });

    document.querySelectorAll("[data-analysis-lane-detail-panel]").forEach(panel => {
        const isActive = panel.dataset.analysisLaneDetailPanel === tabKey;
        panel.classList.toggle("is-active", isActive);
        panel.hidden = !isActive;
    });
}

function buildLaneDetailPlayerRow(player) {
    const playerNote = player.displayName && !stringEqualsIgnoreCase(player.displayName, player.account)
        ? `Most-played character: ${player.displayName}`
        : null;

    return `
        <tr>
            <td>
                <div class="table-stack">
                    <strong class="mono">${escapeHtml(player.account)}</strong>
                    ${playerNote ? `<span class="table-inline-note">${escapeHtml(playerNote)}</span>` : ""}
                    <span class="table-inline-note">${escapeHtml(`${player.characterName} / ${player.classLabel}`)}</span>
                </div>
            </td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(String(player.lane.samples))}</strong>
                    <span class="table-inline-note">${escapeHtml(`${player.lane.totalSamplesAll ?? player.lane.samples} total lane appearances | ${player.totalFightCount} total fights`)}</span>
                </div>
            </td>
            <td>${escapeHtml(formatPercent(player.winRatePercent))}</td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(formatPercent(player.impactScore))}</strong>
                    <span class="table-inline-note">${escapeHtml(`${formatPercent(player.lane.overallSharePercent)} overall share`)}</span>
                </div>
            </td>
            <td>${escapeHtml(`${formatPercent(player.lane.appearanceRatePercent)} appearance | ${player.lane.rateBand ?? "Unrated"}`)}</td>
        </tr>
    `;
}

function buildLaneDetailClassRow(classRow) {
    return `
        <tr>
            <td><strong>${escapeHtml(classRow.classLabel)}</strong></td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(String(classRow.sampleCount))}</strong>
                    <span class="table-inline-note">${escapeHtml("Qualified class samples")}</span>
                </div>
            </td>
            <td>${escapeHtml(formatNumber(classRow.contributionScore, 1))}</td>
            <td>${escapeHtml(formatPercent(classRow.lane.overallStrengthPercent))}</td>
            <td>${escapeHtml(`${formatPercent(classRow.lane.appearanceRatePercent)} appearance | ${formatPercent(classRow.lane.overallSharePercent)} overall share`)}</td>
            <td>${escapeHtml(classRow.topPlayerDisplayName ?? "-")}</td>
        </tr>
    `;
}

function renderAnalysisLaneDetail(snapshot) {
    const container = document.querySelector("#analysis-lane-detail");
    if (!selectedAnalysisLaneKey) {
        container.innerHTML = "";
        return;
    }

    const laneRow = (snapshot.topLanes ?? []).find(lane => stringEqualsIgnoreCase(lane.laneKey, selectedAnalysisLaneKey));
    if (!laneRow) {
        container.innerHTML = "";
        return;
    }

    const lanePlayers = getQualifiedLanePlayers(snapshot, selectedAnalysisLaneKey);
    const laneClasses = getQualifiedLaneClasses(snapshot, selectedAnalysisLaneKey);

    container.className = "analysis-player-detail";
    container.innerHTML = `
        <div class="section-heading">
            <div>
                <h3>${escapeHtml(laneRow.laneLabel)}</h3>
                <p>${escapeHtml(`Best players require at least ${MINIMUM_PLAYER_TABLE_FIGHTS} total fights plus ${MINIMUM_LANE_FILTER_APPEARANCES} total ${laneRow.laneLabel} appearances. Best classes come from the qualified class list.`)}</p>
            </div>
        </div>
        <div class="analysis-character-grid">
            <article class="analysis-character-card">
                <div class="analysis-character-header">
                    <div>
                        <strong>${escapeHtml(laneRow.laneLabel)}</strong>
                        <div class="table-inline-note">Lane profile across the current analysis filter</div>
                    </div>
                    <div class="analysis-character-meta">
                        <span class="analysis-character-pill">${escapeHtml(`${laneRow.samples} samples`)}</span>
                        <span class="analysis-character-pill">${escapeHtml(`${formatPercent(laneRow.averageStrengthPercent)} strength`)}</span>
                        <span class="analysis-character-pill">${escapeHtml(`${formatPercent(laneRow.appearanceRatePercent)} fight coverage`)}</span>
                    </div>
                </div>
                <p class="analysis-character-copy">${escapeHtml(`${formatPercent(laneRow.averageSharePercent)} average share across retained lane samples.`)}</p>
                <p class="table-inline-note">${escapeHtml(laneRow.evidenceLine ?? "No extra lane evidence was retained for this lane.")}</p>
            </article>
        </div>
        <div class="analysis-tabs" role="tablist" aria-label="Lane detail sections">
            <button class="analysis-tab is-active" type="button" data-analysis-lane-detail-tab="players" role="tab" aria-selected="true">Players</button>
            <button class="analysis-tab" type="button" data-analysis-lane-detail-tab="classes" role="tab" aria-selected="false">Classes</button>
        </div>
        <div class="analysis-panel is-active" data-analysis-lane-detail-panel="players" role="tabpanel">
            <div class="table-shell table-shell-scroll table-shell-analysis-players">
                <table class="data-table">
                    <thead>
                        <tr>
                            <th>Player</th>
                            <th>Appearances</th>
                            <th>Record</th>
                            <th>Impact</th>
                            <th>Lane fit</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${lanePlayers.length
                            ? lanePlayers.map(buildLaneDetailPlayerRow).join("")
                            : `<tr><td colspan="5">No players met the current lane thresholds.</td></tr>`}
                    </tbody>
                </table>
            </div>
        </div>
        <div class="analysis-panel" data-analysis-lane-detail-panel="classes" role="tabpanel" hidden>
            <div class="table-shell table-shell-scroll table-shell-analysis-classes">
                <table class="data-table">
                    <thead>
                        <tr>
                            <th>Class</th>
                            <th>Samples</th>
                            <th>Impact</th>
                            <th>Lane strength</th>
                            <th>Lane fit</th>
                            <th>Top player</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${laneClasses.length
                            ? laneClasses.map(buildLaneDetailClassRow).join("")
                            : `<tr><td colspan="6">No classes met the current lane thresholds.</td></tr>`}
                    </tbody>
                </table>
            </div>
        </div>
    `;

    setActiveAnalysisLaneDetailTab(activeAnalysisLaneDetailTab);
}

function buildAnalysisBoonRow(boon) {
    const rowClasses = ["is-clickable"];
    if (String(boon.id ?? "") === String(selectedAnalysisBoonId ?? "")) {
        rowClasses.push("is-selected");
    }

    const stackLabel = boon.stackBased && boon.averageStacks != null
        ? formatNumber(boon.averageStacks, 1)
        : "-";
    const overapplicationLabel = boon.tracksOverapplication && boon.averageOverapplication != null
        ? formatPercent(boon.averageOverapplication)
        : "-";
    const subtitle = boon.topClassLabel
        ? `<span class="table-subtitle">Top provider: ${escapeHtml(boon.topClassLabel)}</span>`
        : "";

    return `
        <tr class="${rowClasses.join(" ")}" data-boon-id="${escapeHtml(String(boon.id ?? ""))}">
            <td>
                <strong>${escapeHtml(boon.name)}</strong>
                ${subtitle}
            </td>
            <td>${escapeHtml(boon.typeLabel)}</td>
            <td>${escapeHtml(String(boon.fightCount))}</td>
            <td>${escapeHtml(formatPercent(boon.averageCoverage))}</td>
            <td>${escapeHtml(stackLabel)}</td>
            <td>${escapeHtml(overapplicationLabel)}</td>
        </tr>
    `;
}

function formatBoonProviderGeneration(provider, boon) {
    return boon.stackBased
        ? formatNumber(provider.averageGeneration, 1)
        : formatPercent(provider.averageGeneration);
}

function formatBoonProviderPresence(provider, boon) {
    if (!boon.stackBased || provider.averageGenerationPresence == null) {
        return "-";
    }

    return formatPercent(provider.averageGenerationPresence);
}

function formatBoonProviderOverstack(provider, boon) {
    return boon.stackBased
        ? formatNumber(provider.averageOverstack, 1)
        : formatPercent(provider.averageOverstack);
}

function buildAnalysisBoonProviderRow(provider, boon) {
    return `
        <tr>
            <td><strong>${escapeHtml(provider.classLabel)}</strong></td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(String(provider.sampleCount ?? 0))}</strong>
                    <span class="table-inline-note">${escapeHtml(`${provider.distinctAccounts ?? 0} accounts`)}</span>
                </div>
            </td>
            <td>${escapeHtml(String(provider.providerAppearanceCount ?? 0))}</td>
            <td>${escapeHtml(formatBoonProviderGeneration(provider, boon))}</td>
            <td>${escapeHtml(formatBoonProviderPresence(provider, boon))}</td>
            <td>${escapeHtml(formatBoonProviderOverstack(provider, boon))}</td>
        </tr>
    `;
}

function renderAnalysisBoonDetail(boon) {
    const container = document.querySelector("#analysis-boon-detail");
    if (!container) {
        return;
    }

    if (!boon) {
        container.innerHTML = "";
        return;
    }

    const providers = boon.classProviders ?? [];
    const generationLabel = boon.stackBased
        ? "Generation is average squad stacks generated per class appearance."
        : "Generation is average squad uptime generated per class appearance.";
    const presenceLabel = boon.stackBased
        ? "Presence shows how often at least one generated stack was present."
        : "Presence does not apply to binary boons.";
    const topProviderTag = boon.topClassLabel ? `Top provider: ${boon.topClassLabel}` : "Top provider: unavailable";

    container.className = "analysis-player-detail";
    container.innerHTML = `
        <div class="section-heading">
            <div>
                <h3>${escapeHtml(boon.name)}</h3>
                <p>Ordered by outgoing squad boon generation across the filtered fights. ${escapeHtml(generationLabel)} ${escapeHtml(presenceLabel)}</p>
            </div>
        </div>
        ${buildTagListHtml([
            `${boon.fightCount} fights`,
            `Threat coverage: ${formatPercent(boon.averageCoverage)}`,
            topProviderTag
        ])}
        <div class="table-shell table-shell-scroll table-shell-analysis-classes">
            <table class="data-table">
                <thead>
                    <tr>
                        <th>Class</th>
                        <th>Samples</th>
                        <th>Provider appearances</th>
                        <th>Generation</th>
                        <th>Presence</th>
                        <th>Overstack</th>
                    </tr>
                </thead>
                <tbody>
                    ${providers.length
                        ? providers.map(provider => buildAnalysisBoonProviderRow(provider, boon)).join("")
                        : `<tr><td colspan="6">No provider data is stored for this boon in the current filtered fights.</td></tr>`}
                </tbody>
            </table>
        </div>
    `;
}

function renderAnalysisBoons(snapshot) {
    const boons = snapshot.topBoons ?? [];
    const body = document.querySelector("#analysis-boons-body");

    if (boons.length === 0) {
        selectedAnalysisBoonId = null;
        body.innerHTML = `<tr><td colspan="6">No threat-boon summaries matched the current filters.</td></tr>`;
        renderAnalysisBoonDetail(null);
        return;
    }

    if (!boons.some(boon => String(boon.id ?? "") === String(selectedAnalysisBoonId ?? ""))) {
        selectedAnalysisBoonId = String(boons[0].id ?? "");
    }

    body.innerHTML = boons.map(buildAnalysisBoonRow).join("");
    const selectedBoon = boons.find(boon => String(boon.id ?? "") === String(selectedAnalysisBoonId ?? "")) ?? boons[0];
    renderAnalysisBoonDetail(selectedBoon);
}

function setActiveAnalysisTab(tabKey) {
    activeAnalysisTab = tabKey;

    document.querySelectorAll("[data-analysis-tab]").forEach(button => {
        const isActive = button.dataset.analysisTab === tabKey;
        button.classList.toggle("is-active", isActive);
        button.setAttribute("aria-selected", isActive ? "true" : "false");
    });

    document.querySelectorAll("[data-analysis-panel]").forEach(panel => {
        const isActive = panel.dataset.analysisPanel === tabKey;
        panel.classList.toggle("is-active", isActive);
        panel.hidden = !isActive;
    });
}

function renderAnalysis(snapshot) {
    currentAnalysisSnapshot = snapshot;

    document.querySelector("#analysis-summary").textContent =
        `${snapshot.scope.filteredFightCount} fights selected from ${snapshot.scope.totalImportedFights} imported fights. Win rate ${formatPercent(snapshot.scope.winRatePercent)}.`;

    renderAnalysisFilterOptions(snapshot);
    setInnerHtml("#analysis-overview-cards", buildAnalysisOverviewCards(snapshot));
    renderAnalysisCharts(snapshot);

    setInnerHtml(
        "#analysis-scope-list",
        buildTagListHtml([
            `Wins: ${snapshot.scope.winCount}`,
            `Losses: ${snapshot.scope.lossCount}`,
            `Draws: ${snapshot.scope.drawCount}`,
            snapshot.selection.commander ? `Commander: ${snapshot.selection.commander}` : "Commander: all",
            snapshot.selection.startDate ? `Start: ${snapshot.selection.startDate}` : "Start: earliest",
            snapshot.selection.endDate ? `End: ${snapshot.selection.endDate}` : "End: latest",
            snapshot.selection.outcomeCode && snapshot.selection.outcomeCode !== "all"
                ? `Outcome filter: ${snapshot.selection.outcomeCode}`
                : "Outcome filter: all"
        ]));

    renderAnalysisPlayers(snapshot);
    renderAnalysisClasses(snapshot);
    renderAnalysisLanes(snapshot);
    renderAnalysisBoons(snapshot);

    setActiveAnalysisTab(activeAnalysisTab);
}

function buildRecentParseRow(fight, selectedFightId) {
    const fightIndex = fight.fightIndex;
    const selectedClass = fight.fightId === selectedFightId ? " is-selected" : "";
    const fightTitle = fightIndex?.fightName ?? fight.sourceFileName ?? fight.fightId;
    const parsedAt = formatDate(fight.importedAtUtc);
    const squadCount = fightIndex?.squadPlayerCount ?? "-";
    const enemyCount = fightIndex?.enemyPlayerCount ?? fightIndex?.enemyTargetCount ?? "-";

    return `
        <tr class="${selectedClass.trim()}">
            <td>${escapeHtml(parsedAt || "-")}</td>
            <td>
                <span class="table-title">${escapeHtml(fightTitle)}</span>
                <span class="table-subtitle mono">${escapeHtml(fight.fightId)}</span>
            </td>
            <td>${escapeHtml(getOutcomeDisplayLabel(fight))}</td>
            <td>${escapeHtml(getExecutionScoreLabel(fight))}</td>
            <td>${escapeHtml(String(squadCount))}</td>
            <td>${escapeHtml(String(enemyCount))}</td>
            <td><span class="${buildStatusClass(fight.status)}">${escapeHtml(fight.status)}</span></td>
            <td>
                <div class="table-actions">
                    <a href="/?fightId=${encodeURIComponent(fight.fightId)}">Dossier</a>
                    ${fight.parserConsoleLogUrl ? `<a href="${escapeHtml(fight.parserConsoleLogUrl)}" target="_blank" rel="noopener">Parser log</a>` : ""}
                </div>
            </td>
        </tr>
    `;
}

function renderRecentParses(snapshot, selectedFightId) {
    const body = document.querySelector("#recent-parses-body");
    const summary = document.querySelector("#recent-parses-summary");
    summary.textContent = `${snapshot.recentParses.length} rows shown.`;

    if (snapshot.recentParses.length === 0) {
        body.innerHTML = `
            <tr>
                <td colspan="8">No stored parses yet. Run a batch parse to build the local fight catalog.</td>
            </tr>
        `;
        return;
    }

    body.innerHTML = snapshot.recentParses
        .map(fight => buildRecentParseRow(fight, selectedFightId))
        .join("");
}

function applyFightBrowserFilters(snapshot) {
    const searchValue = document.querySelector("#fight-browser-search").value.trim().toLowerCase();
    const outcomeValue = document.querySelector("#fight-browser-outcome").value;

    let fights = snapshot.fightBrowser.fights;

    if (searchValue) {
        fights = fights.filter(fight => getFightSearchText(fight).includes(searchValue));
    }

    if (outcomeValue !== "all") {
        fights = fights.filter(fight => getOutcomeCode(fight) === outcomeValue);
    }

    return sortFights(fights);
}

function buildFightBrowserRow(fight, selectedFightId) {
    const fightIndex = fight.fightIndex;
    const rowClasses = [];
    if (fight.fightId === selectedFightId) {
        rowClasses.push("is-selected");
    }
    const fightTime = formatDate(fightIndex?.timeStartStandard ?? fightIndex?.timeStart);
    const commander = fightIndex?.commanderDisplayNames?.join(", ") ?? "-";
    const duration = fightIndex?.duration ?? "-";
    const squadCount = fightIndex?.squadPlayerCount ?? "-";
    const enemyCount = fightIndex?.enemyPlayerCount ?? fightIndex?.enemyTargetCount ?? "-";

    return `
        <tr class="${rowClasses.join(" ")}">
            <td>${escapeHtml(fightTime || "-")}</td>
            <td>${escapeHtml(commander)}</td>
            <td>${escapeHtml(duration)}</td>
            <td>${escapeHtml(getOutcomeDisplayLabel(fight))}</td>
            <td>${escapeHtml(getExecutionScoreLabel(fight))}</td>
            <td>${escapeHtml(String(squadCount))}</td>
            <td>${escapeHtml(String(enemyCount))}</td>
            <td>
                <div class="table-actions">
                    <a href="/?fightId=${encodeURIComponent(fight.fightId)}">Dossier</a>
                    ${fight.parserConsoleLogUrl ? `<a href="${escapeHtml(fight.parserConsoleLogUrl)}" target="_blank" rel="noopener">Parser log</a>` : ""}
                </div>
            </td>
        </tr>
    `;
}

function renderFightBrowser(snapshot, selectedFightId) {
    const summary = document.querySelector("#fight-browser-summary");
    const body = document.querySelector("#fight-browser-body");
    const filteredFights = applyFightBrowserFilters(snapshot);
    updateFightBrowserSortHeaders();

    summary.textContent = snapshot.fightBrowser.failedCount > 0
        ? `Showing ${filteredFights.length} of ${snapshot.fightBrowser.totalCount} imported fights. ${snapshot.fightBrowser.failedCount} parser-failed rows are kept out of the fight browser.`
        : `Showing ${filteredFights.length} of ${snapshot.fightBrowser.totalCount} imported fights.`;

    if (filteredFights.length === 0) {
        body.innerHTML = `
            <tr>
                <td colspan="8">No fights matched the current filters.</td>
            </tr>
        `;
        return;
    }

    body.innerHTML = filteredFights
        .map(fight => buildFightBrowserRow(fight, selectedFightId))
        .join("");
}

function renderFightDossier(detail) {
    const panel = document.querySelector("#fight-dossier-panel");
    panel.hidden = false;

    const fightIndex = detail.fightIndex;
    const outcome = fightIndex?.outcome;
    const execution = fightIndex?.execution;
    const executionContext = execution?.context;
    const executionOutcome = execution?.outcome;
    const squadSide = fightIndex?.squadSide;
    const enemySide = fightIndex?.enemySide;
    const commanderSummary = fightIndex?.commanderSummary;
    const players = fightIndex?.players ?? [];

    document.querySelector("#dossier-title").textContent = fightIndex?.fightName ?? detail.sourceFileName;
    document.querySelector("#dossier-back-link").setAttribute("href", "/");

    const subtitleBits = [
        detail.status,
        fightIndex?.duration ?? null,
        detail.importedAtUtc ? `Parsed ${formatDate(detail.importedAtUtc)}` : null
    ].filter(Boolean);
    document.querySelector("#dossier-subtitle").textContent = subtitleBits.join(" | ");

    const overviewItems = [
        outcome?.displayLabel ? `Outcome: ${outcome.displayLabel}` : null,
        outcome?.decidedBy && outcome.decidedBy !== "none" ? `Outcome decided by: ${outcome.decidedBy}` : null,
        fightIndex?.timeStart ? `Start: ${fightIndex.timeStart}` : null,
        fightIndex?.timeEnd ? `End: ${fightIndex.timeEnd}` : null,
        fightIndex?.mapId != null ? `Map ID: ${fightIndex.mapId}` : null,
        fightIndex ? `Trigger ID: ${fightIndex.triggerId}` : null,
        fightIndex ? (fightIndex.detailedWvW ? "Detailed WvW" : "Non-detailed WvW") : null,
        fightIndex?.eliteInsightsVersion ? `EI: ${fightIndex.eliteInsightsVersion}` : null,
        fightIndex?.analystSchemaVersion ? `Analyst schema: ${fightIndex.analystSchemaVersion}` : null,
        fightIndex?.indexedFrom ? `Indexed from: ${fightIndex.indexedFrom}` : null,
        fightIndex?.arcVersion ? `Arc: ${fightIndex.arcVersion}` : null,
        fightIndex?.gW2Build != null ? `GW2 build: ${fightIndex.gW2Build}` : null
    ].filter(Boolean);

    const participantItems = fightIndex ? [
        `Squad players: ${fightIndex.squadPlayerCount}`,
        `Friendly non-squad: ${fightIndex.friendlyNonSquadCount}`,
        `Enemy players: ${fightIndex.enemyPlayerCount}`,
        `Enemy targets: ${fightIndex.enemyTargetCount}`,
        `Commanders: ${fightIndex.commanderCount}`,
        fightIndex.friendlyTeamId != null ? `Friendly team: ${fightIndex.friendlyTeamId}` : null,
        fightIndex.enemyTeamIds?.length ? `Enemy teams: ${fightIndex.enemyTeamIds.join(", ")}` : null
    ].filter(Boolean) : ["Fight index not available yet."];

    const outcomeItems = [
        outcome?.displayLabel ? `Winner: ${outcome.displayLabel}` : null,
        outcome?.decidedBy && outcome.decidedBy !== "none" ? `Tiebreak: ${outcome.decidedBy}` : null,
        outcome?.detail ?? null,
        executionOutcome ? `Enemy down conversion: ${formatPercent(executionOutcome.enemyDownConversionRate)}` : null,
        executionOutcome ? `Squad recovery: ${formatPercent(executionOutcome.squadRecoveryRate)}` : null,
        executionOutcome?.wipeLabel ? `Wipe: ${executionOutcome.wipeLabel}` : null
    ].filter(Boolean);

    const squadStats = squadSide ? [
        { label: "Players", value: formatNumber(squadSide.playerCount) },
        squadSide.friendlyNonSquadCount ? { label: "Friendly non-squad", value: formatNumber(squadSide.friendlyNonSquadCount) } : null,
        squadSide.effectiveAlliedPlayerCount ? { label: "Effective allies", value: formatNumber(squadSide.effectiveAlliedPlayerCount, 1) } : null,
        { label: "Damage", value: formatNumber(squadSide.damage) },
        { label: "DPS", value: formatNumber(squadSide.dps, 1) },
        { label: "Strips", value: formatNumber(squadSide.strips) },
        { label: "Cleanses", value: formatNumber(squadSide.cleanses) },
        { label: "Resurrects", value: formatNumber(squadSide.resurrects) },
        { label: "Damage taken", value: formatNumber(squadSide.damageTaken) },
        { label: "Received CC", value: formatNumber(squadSide.receivedCrowdControl) }
    ].filter(Boolean) : [];

    const enemyStats = enemySide ? [
        { label: "Players", value: formatNumber(enemySide.playerCount) },
        { label: "Damage", value: formatNumber(enemySide.damage) },
        { label: "DPS", value: formatNumber(enemySide.dps, 1) },
        { label: "Strips", value: formatNumber(enemySide.strips) },
        { label: "Cleanses", value: formatNumber(enemySide.cleanses) },
        { label: "Resurrects", value: formatNumber(enemySide.resurrects) },
        { label: "Damage taken", value: formatNumber(enemySide.damageTaken) },
        { label: "Received CC", value: formatNumber(enemySide.receivedCrowdControl) }
    ].filter(Boolean) : [];

    const scoreboardRows = [
        buildScoreboardRow("Downs", formatNumber(executionOutcome?.squadDowns ?? squadSide?.downs), formatNumber(executionOutcome?.enemyDowns ?? enemySide?.downs)),
        buildScoreboardRow("Kills", formatNumber(executionOutcome?.squadKills ?? squadSide?.kills), formatNumber(executionOutcome?.enemyKills ?? enemySide?.kills)),
        buildScoreboardRow("Deaths", formatNumber(executionOutcome?.squadDeaths ?? squadSide?.deaths), formatNumber(executionOutcome?.enemyDeaths ?? enemySide?.deaths)),
        buildScoreboardRow("Damage", formatNumber(squadSide?.damage), formatNumber(enemySide?.damage)),
        buildScoreboardRow("DPS", formatNumber(squadSide?.dps, 1), formatNumber(enemySide?.dps, 1)),
        buildScoreboardRow("Down conversion", formatPercent(squadSide?.downKillConversionRate), formatPercent(enemySide?.downKillConversionRate)),
        buildScoreboardRow("Strips", formatNumber(squadSide?.strips), formatNumber(enemySide?.strips)),
        buildScoreboardRow("Cleanses", formatNumber(squadSide?.cleanses), formatNumber(enemySide?.cleanses)),
        buildScoreboardRow("Resurrects", formatNumber(squadSide?.resurrects), formatNumber(enemySide?.resurrects)),
        buildScoreboardRow("Damage taken", formatNumber(squadSide?.damageTaken), formatNumber(enemySide?.damageTaken)),
        buildScoreboardRow("Received CC", formatNumber(squadSide?.receivedCrowdControl), formatNumber(enemySide?.receivedCrowdControl))
    ].join("");

    const executionItems = execution ? [
        execution.scoreAvailable && typeof execution.overallScore === "number"
            ? `Overall score: ${execution.overallScore}${execution.grade ? ` (${execution.grade})` : ""}`
            : "Overall score: unavailable",
        execution.summary ? `Summary: ${execution.summary}` : null,
        execution.detail ?? null,
        execution.strongestPillarLabel ? `Strongest pillar: ${execution.strongestPillarLabel}` : null,
        execution.strongestPillarSummary ?? null,
        execution.weakestPillarLabel ? `Weakest pillar: ${execution.weakestPillarLabel}` : null,
        execution.weakestPillarSummary ?? null
    ].filter(Boolean) : ["No compact execution snapshot is stored for this fight yet."];

    const confidenceItems = execution ? [
        execution.confidenceLabel ? `Confidence: ${execution.confidenceLabel}` : null,
        executionContext?.phaseDurationLabel ? `Phase duration: ${executionContext.phaseDurationLabel}` : null,
        executionContext?.enemyFormationStyleLabel ? `Enemy formation: ${executionContext.enemyFormationStyleLabel}` : null,
        executionContext?.enemyFormationStyleDetail ?? null,
        executionContext?.dataConfidenceLabel ? `Data confidence: ${executionContext.dataConfidenceLabel}` : null,
        executionContext?.dataConfidenceDetail ?? null,
        ...(execution.confidenceNotes ?? [])
    ].filter(Boolean) : ["No execution confidence detail is stored for this fight yet."];

    const commanderFocusItems = commanderSummary ? [
        commanderSummary.squadPositioningSamples
            ? `Squad positioning samples: ${formatNumber(commanderSummary.squadPositioningSamples)}`
            : null,
        commanderSummary.squadPositioningSamples
            ? `Squad in position: ${formatPercent(commanderSummary.squadInPositionRate)}`
            : null,
        commanderSummary.squadPositioningSamples
            ? `Too far: ${formatPercent(commanderSummary.squadTooFarRate)}`
            : null,
        commanderSummary.squadPositioningSamples
            ? `Overextended: ${formatPercent(commanderSummary.squadOverextendedRate)}`
            : null,
        commanderSummary.squadPositioningSamples
            ? `Lateral risk: ${formatPercent(commanderSummary.squadLateralRiskRate)}`
            : null,
        typeof commanderSummary.cohesionPillarScore === "number"
            ? `Cohesion pillar: ${commanderSummary.cohesionPillarScore}${commanderSummary.cohesionPillarSummary ? ` | ${commanderSummary.cohesionPillarSummary}` : ""}`
            : commanderSummary.cohesionPillarSummary || null,
        commanderSummary.fitSummary || null,
        commanderSummary.demandFitSummary || null,
        commanderSummary.contributionProfile || null,
        commanderSummary.keyContributionSummary || null,
        commanderSummary.evaluationConfidenceLabel
            ? `Confidence: ${commanderSummary.evaluationConfidenceLabel}${commanderSummary.evaluationConfidenceDetail ? ` | ${commanderSummary.evaluationConfidenceDetail}` : ""}`
            : commanderSummary.evaluationConfidenceDetail || null,
        ...(commanderSummary.evaluationCaveats ?? []).slice(0, 3)
    ].filter(Boolean) : ["No commander-specific summary is stored for this fight yet."];

    const parserItems = [
        `Source file: ${detail.sourceFileName}`,
        detail.sourceFilePath ? `Source path: ${detail.sourceFilePath}` : null,
        `Source size: ${formatBytes(detail.sourceFileBytes)} bytes`,
        `Parser status: ${detail.parserStatus}`,
        `Parser elapsed: ${formatNumber(detail.parserElapsedMilliseconds)} ms`,
        `Parsed: ${detail.parsed ? "yes" : "no"}`,
        detail.parserExecutablePath ? `CLI: ${detail.parserExecutablePath}` : null
    ].filter(Boolean);

    const commanders = fightIndex?.commanderDisplayNames?.length
        ? fightIndex.commanderDisplayNames
        : ["No commander tag was indexed from the stored fight summary."];

    const extensions = fightIndex?.activeExtensions?.length
        ? fightIndex.activeExtensions
        : ["No active extensions were recorded."];

    const playerTableMarkup = players.length
        ? players.map(buildPlayerTableRow).join("")
        : `
            <tr>
                <td colspan="7">No compact squad player summaries are stored for this fight yet.</td>
            </tr>
        `;

    setInnerHtml("#dossier-context-list", buildTagListHtml(overviewItems));
    setInnerHtml("#dossier-participants-list", buildTagListHtml(participantItems));
    setInnerHtml("#dossier-outcome-list", buildTagListHtml(outcomeItems, "No outcome detail is stored for this fight yet."));
    setInnerHtml("#dossier-execution-list", buildTagListHtml(executionItems));
    setInnerHtml("#dossier-confidence-list", buildTagListHtml(confidenceItems));
    setInnerHtml("#dossier-commander-focus-list", buildTagListHtml(commanderFocusItems));
    setInnerHtml("#dossier-parser-list", buildTagListHtml(parserItems));
    setInnerHtml("#dossier-commanders-list", buildTagListHtml(commanders));
    setInnerHtml("#dossier-extensions-list", buildTagListHtml(extensions));

    setInnerHtml("#dossier-artifact-links", [
        buildActionLink(detail.artifactLinks.parserConsoleLogUrl, "Parser log"),
        buildActionLink(detail.artifactLinks.rawLogUrl, "Raw log"),
        buildActionLink(detail.artifactLinks.htmlReportUrl, "Open HTML"),
        buildActionLink(detail.artifactLinks.jsonReportUrl, "Download JSON")
    ].filter(Boolean).join(""));

    document.querySelector("#dossier-squad-title").textContent = squadSide?.displayLabel ?? "Our Squad";
    document.querySelector("#dossier-enemy-title").textContent = enemySide?.displayLabel ?? "Enemy Team";
    setInnerHtml("#dossier-squad-stats", buildStatPairsHtml(squadStats));
    setInnerHtml("#dossier-enemy-stats", buildStatPairsHtml(enemyStats));
    setInnerHtml(
        "#dossier-scoreboard-body",
        scoreboardRows || `
            <tr>
                <td colspan="3">No symmetric outcome or side totals are stored for this fight yet.</td>
            </tr>
        `);
    setInnerHtml(
        "#dossier-pillar-grid",
        execution?.pillars?.length
            ? execution.pillars.map(buildPillarCard).join("")
            : `
                <article class="pillar-card">
                    <strong>Execution pillars</strong>
                    <p class="workspace-note">No pillar drilldown is stored for this fight yet.</p>
                </article>
            `);
    setInnerHtml("#dossier-player-body", playerTableMarkup);
}

function clearFightDossier() {
    document.querySelector("#fight-dossier-panel").hidden = true;
    setInnerHtml("#dossier-pillar-grid", "");
    setInnerHtml("#dossier-player-body", "");
    setInnerHtml("#dossier-commander-focus-list", "");
}

function renderFightDossierError(fightId, error) {
    document.querySelector("#fight-dossier-panel").hidden = false;
    document.querySelector("#dossier-title").textContent = "Fight dossier";
    document.querySelector("#dossier-subtitle").textContent = `Could not load ${fightId}.`;
    setInnerHtml("#dossier-context-list", "");
    setInnerHtml("#dossier-participants-list", "");
    setInnerHtml("#dossier-outcome-list", "");
    setInnerHtml("#dossier-execution-list", "");
    setInnerHtml("#dossier-confidence-list", "");
    setInnerHtml("#dossier-commander-focus-list", "");
    setInnerHtml("#dossier-parser-list", buildTagListHtml([error instanceof Error ? error.message : String(error)]));
    setInnerHtml("#dossier-commanders-list", "");
    setInnerHtml("#dossier-extensions-list", "");
    setInnerHtml("#dossier-artifact-links", "");
    setInnerHtml("#dossier-squad-stats", "");
    setInnerHtml("#dossier-enemy-stats", "");
    setInnerHtml("#dossier-scoreboard-body", "");
    setInnerHtml("#dossier-pillar-grid", "");
    setInnerHtml("#dossier-player-body", "");
}

function renderBatchStatus(result, success) {
    const container = document.querySelector("#batch-status");
    const state = String(result?.state ?? "").toLowerCase();
    const isRunning = state === "running";
    const totalCount = Number(result?.discoveredCount ?? 0);
    const completedCount = Number(result?.completedCount ?? 0);
    const maxParallelism = Number(result?.maxParallelism ?? 0);
    const title = isRunning
        ? `Parsing ${completedCount} / ${totalCount || "?"} logs`
        : success
            ? "Batch complete"
            : "Needs attention";
    const statusClass = isRunning
        ? "status status-neutral"
        : success
            ? "status status-ok"
            : "status status-error";
    const progressBits = [];
    if (totalCount > 0 || isRunning) {
        progressBits.push(`<span class="pill">${escapeHtml(`${completedCount} / ${totalCount || "?"} processed`)}</span>`);
    }
    if (typeof result?.importedCount === "number") {
        progressBits.push(`<span class="pill">${escapeHtml(`${result.importedCount} imported`)}</span>`);
    }
    if (typeof result?.skippedCount === "number") {
        progressBits.push(`<span class="pill">${escapeHtml(`${result.skippedCount} skipped`)}</span>`);
    }
    if (typeof result?.excludedCount === "number") {
        progressBits.push(`<span class="pill">${escapeHtml(`${result.excludedCount} excluded`)}</span>`);
    }
    if (typeof result?.failedCount === "number") {
        progressBits.push(`<span class="pill">${escapeHtml(`${result.failedCount} failed`)}</span>`);
    }
    if (maxParallelism > 0) {
        progressBits.push(`<span class="pill">${escapeHtml(`${maxParallelism} workers`)}</span>`);
    }

    container.innerHTML = `
        <div class="batch-status-grid">
            <div class="batch-status-header">
                <div class="${statusClass}">${escapeHtml(title)}</div>
                <div class="batch-progress-row">${progressBits.join("")}</div>
            </div>
            <p>${escapeHtml(result?.message ?? "No message returned.")}</p>
            ${result?.currentFileName ? `
                <div class="batch-current-file">
                    <strong>${escapeHtml(result.currentFileName)}</strong>
                    ${result.currentFilePath ? `<span class="table-inline-note mono">${escapeHtml(result.currentFilePath)}</span>` : ""}
                </div>
            ` : ""}
        </div>
    `;
}

function renderBatchResults(result) {
    const toolbar = document.querySelector("#batch-results-toolbar");
    const showAllToggle = document.querySelector("#batch-results-show-all");
    const showExcludedToggle = document.querySelector("#batch-results-show-excluded");
    const shell = document.querySelector("#batch-results-shell");
    const body = document.querySelector("#batch-results-body");

    if (!result || !result.items?.length) {
        toolbar.hidden = true;
        shell.hidden = true;
        body.innerHTML = "";
        return;
    }

    const showAll = showAllToggle.checked;
    const showExcluded = showExcludedToggle.checked;
    const items = showAll
        ? result.items
        : result.items.filter(item => stringEqualsIgnoreCase(item.action, "Failed")
            || (showExcluded && stringEqualsIgnoreCase(item.action, "Excluded")));

    toolbar.hidden = false;
    shell.hidden = false;
    if (items.length === 0) {
        const emptyMessage = showExcluded
            ? "No parse errors or filtered-out fights in this batch. Turn on \"Show all parsed files\" to inspect every row."
            : "No parse errors in this batch. Turn on \"Show excluded fights\" to inspect fights that were too short or too small, or \"Show all parsed files\" to inspect every row.";
        body.innerHTML = `
            <tr>
                <td colspan="5">${escapeHtml(emptyMessage)}</td>
            </tr>
        `;
        return;
    }

    body.innerHTML = items
        .map(item => `
            <tr>
                <td>
                    <span class="table-title">${escapeHtml(item.sourceFileName)}</span>
                    <span class="table-subtitle mono">${escapeHtml(item.sourceFilePath)}</span>
                </td>
                <td><span class="${buildStatusClass(item.action)}">${escapeHtml(item.action)}</span></td>
                <td>${item.fightId ? `<a href="/?fightId=${encodeURIComponent(item.fightId)}">${escapeHtml(item.fightId)}</a>` : "-"}</td>
                <td>${escapeHtml(item.parserStatus ?? "-")}</td>
                <td>${escapeHtml(item.message)}</td>
            </tr>
        `)
        .join("");
}

async function handleBatchSubmit(event) {
    event.preventDefault();

    const directoryInput = document.querySelector("#log-directory-input");
    const modeInput = document.querySelector("#directory-mode");
    const maxParallelismInput = document.querySelector("#directory-max-parallelism");

    const directoryPath = directoryInput.value.trim();
    const mode = modeInput.value;
    const rawParallelism = Number.parseInt(maxParallelismInput.value, 10);
    const maxParallelism = Number.isFinite(rawParallelism)
        ? Math.min(16, Math.max(1, rawParallelism))
        : 4;
    maxParallelismInput.value = String(maxParallelism);

    if (!directoryPath) {
        renderBatchStatus({ message: "Enter a directory path before starting a batch parse." }, false);
        renderBatchResults(null);
        return;
    }

    localStorage.setItem(DIRECTORY_PATH_KEY, directoryPath);
    localStorage.setItem(DIRECTORY_MODE_KEY, mode);
    localStorage.setItem(DIRECTORY_MAX_PARALLELISM_KEY, String(maxParallelism));

    setBatchButtonBusy(true);
    renderBatchStatus(
        {
            state: "running",
            message: "Scanning the directory, hashing files, and queuing parser work. Large batches can take a while.",
            maxParallelism,
            discoveredCount: 0,
            completedCount: 0,
            importedCount: 0,
            skippedCount: 0,
            excludedCount: 0,
            failedCount: 0
        },
        true);
    renderBatchResults(null);

    try {
        const response = await fetch("/api/imports/directory/jobs", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                directoryPath,
                mode,
                maxParallelism
            })
        });

        const status = await response.json();
        lastBatchResult = status;
        renderBatchStatus(status, response.ok || response.status === 409);
        renderBatchResults(status);

        if (status?.jobId && (response.ok || response.status === 409)) {
            startBatchJobPolling(status.jobId);
            return;
        }

        setBatchButtonBusy(false);
    } catch (error) {
        const result = {
            message: error instanceof Error ? error.message : String(error),
            items: []
        };
        renderBatchStatus(result, false);
        renderBatchResults(null);
        setBatchButtonBusy(false);
    }
}

function handleFightBrowserChange() {
    if (!currentDashboardSnapshot) {
        return;
    }

    renderFightBrowser(currentDashboardSnapshot, getSelectedFightId());
}

async function refreshAnalysis() {
    const button = document.querySelector("#analysis-apply-button");
    button.disabled = true;

    try {
        const snapshot = await loadAnalysis(getAnalysisFiltersFromUi());
        renderAnalysis(snapshot);
    } catch (error) {
        document.querySelector("#analysis-summary").textContent = error instanceof Error ? error.message : String(error);
        setInnerHtml("#analysis-overview-cards", "");
        setInnerHtml("#analysis-chart-grid", "");
        setInnerHtml("#analysis-scope-list", buildTagListHtml(["Analysis data could not be loaded."]));
        document.querySelector("#analysis-players-summary").textContent = "Analysis data could not be loaded.";
        setInnerHtml("#analysis-players-body", `<tr><td colspan="6">Analysis data could not be loaded.</td></tr>`);
        setInnerHtml("#analysis-player-detail", "");
        setInnerHtml("#analysis-classes-body", `<tr><td colspan="9">Analysis data could not be loaded.</td></tr>`);
        setInnerHtml("#analysis-lanes-body", `<tr><td colspan="7">Analysis data could not be loaded.</td></tr>`);
        setInnerHtml("#analysis-boons-body", `<tr><td colspan="6">Analysis data could not be loaded.</td></tr>`);
        setInnerHtml("#analysis-boon-detail", "");
    } finally {
        button.disabled = false;
    }
}

function hydrateBatchForm() {
    const storedDirectory = localStorage.getItem(DIRECTORY_PATH_KEY);
    const storedMode = localStorage.getItem(DIRECTORY_MODE_KEY);
    const storedMaxParallelism = localStorage.getItem(DIRECTORY_MAX_PARALLELISM_KEY);

    if (storedDirectory) {
        document.querySelector("#log-directory-input").value = storedDirectory;
    }

    if (storedMode === "rebuild-all" || storedMode === "new-only") {
        document.querySelector("#directory-mode").value = storedMode;
    }

    const parsedParallelism = Number.parseInt(storedMaxParallelism ?? "", 10);
    document.querySelector("#directory-max-parallelism").value = String(
        Number.isFinite(parsedParallelism)
            ? Math.min(16, Math.max(1, parsedParallelism))
            : 4);
}

function setBatchButtonBusy(isBusy) {
    const button = document.querySelector("#directory-button");
    button.disabled = isBusy;
    button.textContent = isBusy ? "Parsing..." : "Start batch parse";
}

function stopBatchJobPolling(clearStoredJob = true) {
    if (batchStatusPollHandle) {
        window.clearTimeout(batchStatusPollHandle);
        batchStatusPollHandle = null;
    }

    activeBatchJobId = null;
    if (clearStoredJob) {
        localStorage.removeItem(ACTIVE_BATCH_JOB_KEY);
    }
}

async function pollBatchJob(jobId) {
    try {
        const status = await loadBatchJobStatus(jobId);
        lastBatchResult = status;
        renderBatchStatus(status, status.state !== "failed");
        renderBatchResults(status);

        if (status.state === "running") {
            batchStatusPollHandle = window.setTimeout(() => pollBatchJob(jobId), 1000);
            return;
        }

        stopBatchJobPolling();
        setBatchButtonBusy(false);
        await main();
    } catch (error) {
        stopBatchJobPolling();
        setBatchButtonBusy(false);
        renderBatchStatus({ message: error instanceof Error ? error.message : String(error) }, false);
    }
}

function startBatchJobPolling(jobId) {
    stopBatchJobPolling(false);
    activeBatchJobId = jobId;
    localStorage.setItem(ACTIVE_BATCH_JOB_KEY, jobId);
    setBatchButtonBusy(true);
    void pollBatchJob(jobId);
}

function resumeBatchJobPollingIfNeeded() {
    if (activeBatchJobId || batchStatusPollHandle) {
        return;
    }

    const storedJobId = localStorage.getItem(ACTIVE_BATCH_JOB_KEY);
    if (!storedJobId) {
        setBatchButtonBusy(false);
        return;
    }

    startBatchJobPolling(storedJobId);
}

async function main() {
    const selectedFightId = getSelectedFightId();

    try {
        const [snapshot, analysisSnapshot] = await Promise.all([
            loadDashboard(),
            loadAnalysis(getAnalysisFiltersFromUi())
        ]);
        currentDashboardSnapshot = snapshot;
        currentAnalysisSnapshot = analysisSnapshot;

        renderWorkspace(snapshot);
        renderWorkstreams(snapshot);
        renderScorecard(snapshot);
        renderRecentParses(snapshot, selectedFightId);
        renderFightBrowser(snapshot, selectedFightId);
        renderRetention(snapshot);
        renderAnalysis(analysisSnapshot);

        if (selectedFightId) {
            try {
                const detail = await loadFightDetail(selectedFightId);
                renderFightDossier(detail);
            } catch (error) {
                renderFightDossierError(selectedFightId, error);
            }
        } else {
            clearFightDossier();
        }

        renderBatchResults(lastBatchResult);
        resumeBatchJobPollingIfNeeded();
    } catch (error) {
        document.body.innerHTML = `
            <main class="page-shell">
                <section class="panel panel-wide">
                    <h1>WvWAnalyst</h1>
                    <p>Failed to load the prototype dashboard.</p>
                    <pre>${escapeHtml(error instanceof Error ? error.message : String(error))}</pre>
                </section>
            </main>
        `;
    }
}

document.querySelector("#batch-form").addEventListener("submit", handleBatchSubmit);
document.querySelector("#batch-results-show-all").addEventListener("change", () => renderBatchResults(lastBatchResult));
document.querySelector("#batch-results-show-excluded").addEventListener("change", () => renderBatchResults(lastBatchResult));
document.querySelector("#fight-browser-search").addEventListener("input", handleFightBrowserChange);
document.querySelector("#fight-browser-outcome").addEventListener("change", handleFightBrowserChange);
document.querySelector("#analysis-player-search").addEventListener("input", () => {
    if (currentAnalysisSnapshot) {
        renderAnalysisPlayers(currentAnalysisSnapshot);
    }
});
document.querySelector("#analysis-player-lane-filter").addEventListener("change", () => {
    if (currentAnalysisSnapshot) {
        renderAnalysisPlayers(currentAnalysisSnapshot);
    }
});
document.querySelector("#analysis-players-body").addEventListener("click", event => {
    const row = event.target.closest("tr[data-player-account]");
    if (!row) {
        return;
    }

    selectedAnalysisPlayerAccount = row.dataset.playerAccount ?? null;
    if (currentAnalysisSnapshot) {
        renderAnalysisPlayers(currentAnalysisSnapshot);
    }
});
document.querySelector("#analysis-classes-body").addEventListener("click", event => {
    const row = event.target.closest("tr[data-class-label]");
    if (!row) {
        return;
    }

    selectedAnalysisClassLabel = row.dataset.classLabel ?? null;
    if (currentAnalysisSnapshot) {
        renderAnalysisClasses(currentAnalysisSnapshot);
    }
});
document.querySelector("#analysis-lanes-body").addEventListener("click", event => {
    const row = event.target.closest("tr[data-lane-key]");
    if (!row) {
        return;
    }

    selectedAnalysisLaneKey = row.dataset.laneKey ?? null;
    if (currentAnalysisSnapshot) {
        renderAnalysisLanes(currentAnalysisSnapshot);
    }
});
document.querySelector("#analysis-boons-body").addEventListener("click", event => {
    const row = event.target.closest("tr[data-boon-id]");
    if (!row) {
        return;
    }

    selectedAnalysisBoonId = row.dataset.boonId ?? null;
    if (currentAnalysisSnapshot) {
        renderAnalysisBoons(currentAnalysisSnapshot);
    }
});
document.querySelector("#analysis-class-detail").addEventListener("click", event => {
    const button = event.target.closest("[data-analysis-class-player-sort]");
    if (!button) {
        return;
    }

    setAnalysisClassPlayerSort(button.dataset.analysisClassPlayerSort);
});
document.querySelector("#analysis-lane-detail").addEventListener("click", event => {
    const button = event.target.closest("[data-analysis-lane-detail-tab]");
    if (!button) {
        return;
    }

    setActiveAnalysisLaneDetailTab(button.dataset.analysisLaneDetailTab);
});
document.querySelector("#analysis-apply-button").addEventListener("click", refreshAnalysis);
document.querySelectorAll("[data-fight-browser-sort]").forEach(button => {
    button.addEventListener("click", () => setFightBrowserSort(button.dataset.fightBrowserSort));
});
document.querySelectorAll("[data-analysis-player-sort]").forEach(button => {
    button.addEventListener("click", () => setAnalysisPlayerSort(button.dataset.analysisPlayerSort));
});
document.querySelectorAll("[data-analysis-class-sort]").forEach(button => {
    button.addEventListener("click", () => setAnalysisClassSort(button.dataset.analysisClassSort));
});
document.querySelectorAll("[data-analysis-tab]").forEach(button => {
    button.addEventListener("click", () => setActiveAnalysisTab(button.dataset.analysisTab));
});

hydrateBatchForm();
main();

function stringEqualsIgnoreCase(left, right) {
    return String(left ?? "").localeCompare(String(right ?? ""), undefined, { sensitivity: "accent" }) === 0;
}
