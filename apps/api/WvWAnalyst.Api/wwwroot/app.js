const DIRECTORY_MAX_PARALLELISM_KEY = "wvw-analyst.last-max-parallelism";
const ACTIVE_BATCH_JOB_KEY = "wvw-analyst.active-batch-job";
const ACTIVE_APP_TAB_KEY = "wvw-analyst.active-app-tab";
const FIGHT_SHAPE_DIAGNOSTICS_KEY = "wvw-analyst.show-fight-shape-diagnostics";
const ANALYSIS_TREND_MODE_KEY = "wvw-analyst.analysis-trend-mode";
const ANALYSIS_TREND_SMOOTHING_KEY = "wvw-analyst.analysis-trend-smoothing";
const DEFAULT_BATCH_STATUS_MESSAGE = "No batch parse has been run in this browser session yet.";
let currentDashboardSnapshot = null;
let currentAnalysisSnapshot = null;
let currentAnalysisPlayerDetailsByAccount = new Map();
let currentAnalysisPlayerDetailPromisesByAccount = new Map();
let currentAnalysisAllPlayerDetails = null;
let currentAnalysisAllPlayerDetailsPromise = null;
let currentPatchMetadata = null;
let currentCompHelperConfig = null;
let lastBatchResult = null;
let activeBatchJobId = null;
let batchStatusPollHandle = null;
let manageActivityRefreshHandle = null;
let analysisLoadPromise = null;
let showFightBrowserTopBursts = false;
let showFightShapeDiagnostics = localStorage.getItem(FIGHT_SHAPE_DIAGNOSTICS_KEY) === "true";
let logFileUploadBusy = false;
let manageResetBusy = false;
let manageCommanderDeleteBusy = false;
let activeAppTab = "manage";
let activeAnalysisTab = "overview";
let fightBrowserSortState = { key: "fightTime", direction: "desc" };
let analysisPlayerSortState = { key: "performance", direction: "desc" };
let analysisClassSortState = { key: "performance", direction: "desc" };
let analysisClassPlayerSortState = { key: "performance", direction: "desc" };
let analysisEnemySortState = { key: "total", direction: "desc" };
let selectedAnalysisPlayerAccount = null;
let selectedAnalysisClassLabel = null;
let selectedAnalysisLaneKeys = [];
let selectedAnalysisBoonId = null;
let selectedAnalysisBoonTrendIds = null;
let selectedAnalysisBurstComparisonIds = null;
let selectedAnalysisPlayerImpactTrendIds = null;
let selectedAnalysisPlayerImpactTrendOwner = null;
let selectedAnalysisClassImpactTrendIds = null;
let selectedAnalysisClassImpactTrendOwner = null;
let analysisImpactTrendLegendExpanded = { player: false, class: false };
let lockedCompHelperCandidateIds = [];
let compHelperProfileKey = "balanced";
let compHelperCandidateTierKey = "best";
let compHelperFavoredLaneKeys = [];
let compHelperFavoredPackageKeys = [];
let activeAnalysisLaneDetailTab = "players";
const MINIMUM_LANE_FILTER_APPEARANCES = 20;
const MINIMUM_PLAYER_TABLE_FIGHTS = 40;
const PREVENTION_VALUE_METRIC_KEY = "preventionValue";
const STRIP_TOTAL_METRIC_KEY = "stripsTotal";
const STRIP_CORRUPTS_METRIC_KEY = "stripCorruptsTotal";
const ANALYSIS_TREND_ROLLING_WINDOW = 5;
let analysisTrendMode = "fight";
let analysisTrendSmoothingWindow = ANALYSIS_TREND_ROLLING_WINDOW;
const SHOW_ANALYSIS_OBLITERATE_CARD = false;
const ANALYSIS_TREND_MODE_OPTIONS = {
    fight: {
        key: "fight",
        label: "Per fight",
        description: "Raw fight scores in fight-date order.",
        unitSingular: "fight",
        unitPlural: "fights",
        recentWindow: 10,
        usesMedianBuckets: false
    },
    week: {
        key: "week",
        label: "Weekly median",
        description: "Median score per local calendar week.",
        unitSingular: "week",
        unitPlural: "weeks",
        recentWindow: 4,
        usesMedianBuckets: true
    },
    month: {
        key: "month",
        label: "Monthly median",
        description: "Median score per local calendar month.",
        unitSingular: "month",
        unitPlural: "months",
        recentWindow: 3,
        usesMedianBuckets: true
    }
};
const ANALYSIS_TREND_SMOOTHING_OPTIONS = [0, 3, 5, 8];
const ANALYSIS_TREND_METRICS = [
    { key: "overallScore", title: "Overall trend", averageKey: "averageOverallScore", fallbackValue: "n/a", detail: "Execution score across the selected trend buckets.", comparisonLabel: "Overall" },
    { key: "contextDelta", title: "Context delta trend", averageKey: "averageContextDelta", fallbackValue: "n/a", detail: "Actual execution score minus expected score for similar fights.", comparisonLabel: "Context delta", signed: true, positiveLabel: "Better", negativeLabel: "Worse", rangeMode: "signed" },
    { key: "cohesionScore", title: "Cohesion trend", averageKey: "averageCohesionScore", fallbackValue: "n/a", detail: "Cohesion & positioning pillar.", comparisonLabel: "Cohesion" },
    { key: "pressureScore", title: "Pressure trend", averageKey: "averagePressureScore", fallbackValue: "n/a", detail: "Pressure & burst pillar.", comparisonLabel: "Pressure" },
    { key: "downstateScore", title: "Downstate trend", averageKey: "averageDownstateScore", fallbackValue: "n/a", detail: "Downstate control pillar.", comparisonLabel: "Downstate" },
    { key: "supportScore", title: "Support trend", averageKey: "averageSupportScore", fallbackValue: "n/a", detail: "Support & mitigation pillar.", comparisonLabel: "Support" }
];
const ANALYSIS_BURST_TREND_METRICS = [
    { key: "damage", title: "Best burst damage", unit: "damage", detail: "Highest retained burst damage from either side." },
    { key: "strips", title: "Best burst strips", unit: "strips", detail: "Highest retained boon strips in a burst window." },
    { key: "downs", title: "Best burst downs", unit: "downs", detail: "Highest retained downs created in a burst window." },
    { key: "kills", title: "Best burst kills", unit: "kills", detail: "Highest retained kills secured in a burst window." }
];
const ANALYSIS_BURST_COMPARISON_SERIES = [
    { id: "squad-damage", sideKey: "squad", metricKey: "damage", label: "Squad damage", color: "#38bdf8" },
    { id: "enemy-damage", sideKey: "enemy", metricKey: "damage", label: "Enemy damage", color: "#fb7185" },
    { id: "squad-strips", sideKey: "squad", metricKey: "strips", label: "Squad strips", color: "#22c55e" },
    { id: "enemy-strips", sideKey: "enemy", metricKey: "strips", label: "Enemy strips", color: "#f97316" },
    { id: "squad-downs", sideKey: "squad", metricKey: "downs", label: "Squad downs", color: "#a78bfa" },
    { id: "enemy-downs", sideKey: "enemy", metricKey: "downs", label: "Enemy downs", color: "#f472b6" },
    { id: "squad-kills", sideKey: "squad", metricKey: "kills", label: "Squad kills", color: "#facc15" },
    { id: "enemy-kills", sideKey: "enemy", metricKey: "kills", label: "Enemy kills", color: "#94a3b8" }
];
const ANALYSIS_BOON_TREND_COLORS = ["#5eead4", "#facc15", "#a78bfa", "#fb7185", "#60a5fa", "#f97316", "#34d399", "#f472b6"];
const COMP_HELPER_TEAM_SIZE = 5;
const COMP_HELPER_MAX_LOCKED_CARDS = 5;
const COMP_HELPER_MIN_TOTAL_FIGHTS = 20;
const COMP_HELPER_MIN_FILTERED_FIGHTS = 5;
const COMP_HELPER_SEARCH_POOL_LIMIT = 72;
const COMP_HELPER_BEAM_WIDTH = 48;
const COMP_HELPER_SUGGESTION_COUNT = 5;
const COMP_HELPER_DIMINISHING_WEIGHTS = [1.0, 0.7, 0.45, 0.25, 0.1];
const COMP_HELPER_CANDIDATE_TIER_OPTIONS = {
    best: {
        key: "best",
        label: "Best",
        targetPercentile: 100,
        summary: "Best cards are active."
    },
    p75: {
        key: "p75",
        label: "75th percentile",
        targetPercentile: 75,
        summary: "Suggestions center on cards closest to the 75th-percentile fit band."
    },
    p50: {
        key: "p50",
        label: "50th percentile",
        targetPercentile: 50,
        summary: "Suggestions center on cards closest to the 50th-percentile fit band."
    }
};
const DEFAULT_COMP_HELPER_LANE_TARGETS = [
    { key: "pressure", label: "Pressure", floor: 85, target: 125, weight: 1.30 },
    { key: "boonsupport", label: "Boon Support", floor: 80, target: 120, weight: 1.25 },
    { key: "recovery", label: "Recovery", floor: 70, target: 105, weight: 1.15 },
    { key: "prevention", label: "Prevention", floor: 60, target: 95, weight: 1.05 },
    { key: "conversion", label: "Conversion", floor: 60, target: 95, weight: 1.00 },
    { key: "strip", label: "Strip", floor: 55, target: 85, weight: 0.95 },
    { key: "control", label: "Control", floor: 50, target: 80, weight: 0.90 },
    { key: "rez", label: "Rez", floor: 25, target: 50, weight: 0.55 }
];
const ANALYSIS_LANE_DISPLAY_ORDER = [
    { key: "pressure", label: "Pressure", aliases: ["pressure"] },
    { key: "conversion", label: "Conversion", aliases: ["conversion"] },
    { key: "strip", label: "Boon Strip", aliases: ["strip", "boonstrip"] },
    { key: "recovery", label: "Recovery", aliases: ["recovery"] },
    { key: "prevention", label: "Prevention", aliases: ["prevention"] },
    { key: "boonsupport", label: "Boon Support", aliases: ["boonsupport"] },
    { key: "control", label: "Control", aliases: ["control"] },
    { key: "rez", label: "Rez", aliases: ["rez"] }
];
const ANALYSIS_LANE_ORDER_LOOKUP = new Map(ANALYSIS_LANE_DISPLAY_ORDER.flatMap((entry, index) =>
    [entry.key, entry.label, ...(entry.aliases ?? [])]
        .map(alias => [normalizeAnalysisLaneOrderToken(alias), { ...entry, index }])));
const DEFAULT_COMP_HELPER_PACKAGE_TARGETS = [
    { key: "stability", label: "Stability", floor: 60, target: 95, weight: 1.50, mandatory: true, allowOvercap: false },
    { key: "healing", label: "Healing", floor: 60, target: 95, weight: 1.45, mandatory: true, allowOvercap: true },
    { key: "cleanse", label: "Cleanse", floor: 55, target: 90, weight: 1.40, mandatory: true, allowOvercap: true },
    { key: "protection", label: "Protection", floor: 50, target: 85, weight: 1.20, mandatory: true, allowOvercap: true },
    { key: "pressure-package", label: "Pressure", floor: 75, target: 115, weight: 1.35, mandatory: true, allowOvercap: true },
    { key: "barrier", label: "Barrier", floor: 0, target: 65, weight: 0.55, mandatory: false, allowOvercap: false },
    { key: "might", label: "Might", floor: 0, target: 85, weight: 0.70, mandatory: false, allowOvercap: true },
    { key: "strip-package", label: "Strip", floor: 40, target: 75, weight: 0.90, mandatory: false, allowOvercap: true },
    { key: "fury", label: "Fury", floor: 0, target: 65, weight: 0.55, mandatory: false, allowOvercap: true },
    { key: "quickness", label: "Quickness", floor: 0, target: 65, weight: 0.55, mandatory: false, allowOvercap: true },
    { key: "resistance", label: "Resistance", floor: 0, target: 45, weight: 0.35, mandatory: false, allowOvercap: true },
    { key: "regeneration", label: "Regeneration", floor: 0, target: 65, weight: 0.45, mandatory: false, allowOvercap: true },
    { key: "cc", label: "CC (combined)", floor: 35, target: 65, weight: 0.70, mandatory: false, allowOvercap: true }
];
let compHelperLaneTargets = cloneCompHelperTargets(DEFAULT_COMP_HELPER_LANE_TARGETS);
let compHelperPackageTargets = cloneCompHelperTargets(DEFAULT_COMP_HELPER_PACKAGE_TARGETS);
const COMP_HELPER_PROFILE_FAVORITES = {
    balanced: {
        lanes: [],
        packages: []
    },
    offense: {
        lanes: ["pressure", "conversion", "strip", "control"],
        packages: ["pressure-package", "might", "strip-package", "fury", "quickness", "cc"]
    },
    defense: {
        lanes: ["boonsupport", "recovery", "prevention", "rez"],
        packages: ["stability", "healing", "cleanse", "protection", "barrier", "resistance", "regeneration"]
    },
    custom: {
        lanes: [],
        packages: []
    }
};

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

async function readApiPayload(response) {
    const contentType = response.headers.get("content-type") ?? "";
    if (contentType.includes("application/json")) {
        return response.json();
    }

    const text = await response.text();
    return {
        message: text || `Request failed with status ${response.status}.`
    };
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

function buildDossierClassListHtml(items, fallback = "No enemy class summary is stored for this fight yet.") {
    const filtered = (items ?? []).filter(item => item?.classLabel && item?.count > 0);
    if (filtered.length === 0) {
        return `<p class="dossier-empty-note">${escapeHtml(fallback)}</p>`;
    }

    return `
        <div class="dossier-class-list">
            ${filtered.map(item => `
                <div class="dossier-class-row">
                    ${item.icon
                        ? `<img class="dossier-class-icon" src="${escapeHtml(item.icon)}" alt="" loading="lazy" referrerpolicy="no-referrer" onerror="this.style.display='none'">`
                        : `<span class="dossier-class-icon" aria-hidden="true"></span>`}
                    <span class="dossier-class-name">${escapeHtml(item.classLabel)}</span>
                    <span class="dossier-class-count">x${escapeHtml(formatNumber(item.count))}</span>
                </div>
            `).join("")}
        </div>
    `;
}

function buildDossierFactListHtml(items, fallback = "Nothing recorded.") {
    const filtered = (items ?? []).filter(Boolean);
    if (filtered.length === 0) {
        return `<p class="dossier-empty-note">${escapeHtml(fallback)}</p>`;
    }

    return `
        <div class="dossier-fact-list">
            ${filtered.map(item => {
                const text = String(item ?? "").trim();
                const separatorIndex = text.indexOf(": ");
                if (separatorIndex > 0 && separatorIndex < 40) {
                    const label = text.slice(0, separatorIndex);
                    const value = text.slice(separatorIndex + 2);
                    return `
                        <div class="dossier-fact-row">
                            <span class="dossier-fact-label">${escapeHtml(label)}</span>
                            <span class="dossier-fact-value">${escapeHtml(value)}</span>
                        </div>
                    `;
                }

                return `
                    <div class="dossier-fact-row dossier-fact-row-full">
                        <span class="dossier-fact-value">${escapeHtml(text)}</span>
                    </div>
                `;
            }).join("")}
        </div>
    `;
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

function normalizeAnalysisTrendMode(value) {
    const normalized = String(value ?? "").trim().toLowerCase();
    return ANALYSIS_TREND_MODE_OPTIONS[normalized]
        ? normalized
        : "fight";
}

function normalizeAnalysisTrendSmoothingWindow(value) {
    const parsed = Number.parseInt(value, 10);
    return ANALYSIS_TREND_SMOOTHING_OPTIONS.includes(parsed)
        ? parsed
        : ANALYSIS_TREND_ROLLING_WINDOW;
}

function formatSignedNumber(value, maximumFractionDigits = 1) {
    if (value == null) {
        return "n/a";
    }

    const numeric = Number(value);
    if (Number.isNaN(numeric)) {
        return String(value);
    }

    if (numeric === 0) {
        return formatNumber(0, maximumFractionDigits);
    }

    return `${numeric > 0 ? "+" : "-"}${formatNumber(Math.abs(numeric), maximumFractionDigits)}`;
}

function formatAnalysisMetricValue(metric, value, maximumFractionDigits = 1) {
    if (value == null) {
        return metric?.fallbackValue ?? "n/a";
    }

    return metric?.signed
        ? formatSignedNumber(value, maximumFractionDigits)
        : formatNumber(value, maximumFractionDigits);
}

function resolveAnalysisChartRange(metric, values) {
    if (metric?.rangeMode !== "signed") {
        return { minValue: 0, maxValue: 100 };
    }

    const numericValues = values
        .map(value => Number(value))
        .filter(value => Number.isFinite(value));
    const maxMagnitude = numericValues.length === 0
        ? 10
        : Math.max(10, ...numericValues.map(value => Math.abs(value)));
    const roundedMagnitude = Math.min(100, Math.ceil(maxMagnitude / 5) * 5);
    return {
        minValue: -roundedMagnitude,
        maxValue: roundedMagnitude
    };
}

function buildAnalysisLinePath(values, minValue = 0, maxValue = 100) {
    const width = 320;
    const height = 118;
    const normalizedValues = values.map(value => {
        if (value == null) {
            return null;
        }

        const numeric = Number(value);
        return Number.isNaN(numeric) ? null : numeric;
    });

    if (!normalizedValues.some(value => value != null)) {
        return "";
    }

    const xStep = normalizedValues.length <= 1 ? width / 2 : width / (normalizedValues.length - 1);
    const range = Math.max(1, maxValue - minValue);
    let started = false;

    return normalizedValues
        .map((value, index) => {
            if (value == null) {
                started = false;
                return "";
            }

            const x = Math.round(index * xStep * 100) / 100;
            const clampedValue = Math.max(minValue, Math.min(maxValue, value));
            const y = Math.round((height - ((clampedValue - minValue) / range) * height) * 100) / 100;
            const command = started ? "L" : "M";
            started = true;
            return `${command} ${x} ${y}`;
        })
        .filter(Boolean)
        .join(" ");
}

function buildAnalysisPointMarkup(values, minValue = 0, maxValue = 100) {
    const width = 320;
    const height = 118;
    const normalizedValues = values.map(value => {
        if (value == null) {
            return null;
        }

        const numeric = Number(value);
        return Number.isNaN(numeric) ? null : numeric;
    });

    if (!normalizedValues.some(value => value != null)) {
        return "";
    }

    const xStep = normalizedValues.length <= 1 ? width / 2 : width / (normalizedValues.length - 1);
    const range = Math.max(1, maxValue - minValue);

    return normalizedValues
        .map((value, index) => {
            if (value == null) {
                return "";
            }

            const x = Math.round(index * xStep * 100) / 100;
            const clampedValue = Math.max(minValue, Math.min(maxValue, value));
            const y = Math.round((height - ((clampedValue - minValue) / range) * height) * 100) / 100;
            return `<circle class="point" cx="${x}" cy="${y}" r="2.7"></circle>`;
        })
        .filter(Boolean)
        .join("");
}

function getAnalysisChartX(index, pointCount) {
    const width = 320;
    if (pointCount <= 1) {
        return width / 2;
    }

    return index * (width / (pointCount - 1));
}

function buildRollingAverage(values, windowSize = ANALYSIS_TREND_ROLLING_WINDOW) {
    const normalizedValues = values.map(value => {
        if (value == null) {
            return null;
        }

        const numeric = Number(value);
        return Number.isNaN(numeric) ? null : numeric;
    });
    if (windowSize <= 1) {
        return normalizedValues;
    }

    const effectiveWindow = Math.max(1, Math.min(windowSize, normalizedValues.filter(value => value != null).length));

    return normalizedValues.map((value, index) => {
        if (value == null) {
            return null;
        }

        let total = 0;
        let count = 0;
        for (let cursor = Math.max(0, index - effectiveWindow + 1); cursor <= index; cursor += 1) {
            const candidate = normalizedValues[cursor];
            if (candidate == null) {
                continue;
            }

            total += candidate;
            count += 1;
        }

        return count > 0 ? total / count : null;
    });
}

function getAnalysisTrendPointDate(point, fallbackIndex) {
    const candidates = [
        point?.fightDateUtc,
        point?.fightDateLabel
    ];

    for (const candidate of candidates) {
        if (!candidate) {
            continue;
        }

        const parsed = new Date(candidate);
        if (!Number.isNaN(parsed.getTime())) {
            return parsed;
        }
    }

    return Number.isFinite(fallbackIndex)
        ? new Date((fallbackIndex + 1) * 60000)
        : null;
}

function buildAnalysisTrendBucketStart(date, mode) {
    const normalizedMode = normalizeAnalysisTrendMode(mode);
    const bucketStart = new Date(date.getFullYear(), date.getMonth(), date.getDate());

    if (normalizedMode === "month") {
        bucketStart.setDate(1);
        return bucketStart;
    }

    const dayOffset = (bucketStart.getDay() + 6) % 7;
    bucketStart.setDate(bucketStart.getDate() - dayOffset);
    return bucketStart;
}

function buildAnalysisTrendBucketKey(bucketStart, mode) {
    const normalizedMode = normalizeAnalysisTrendMode(mode);
    if (normalizedMode === "month") {
        return `${bucketStart.getFullYear()}-${String(bucketStart.getMonth() + 1).padStart(2, "0")}`;
    }

    return `${bucketStart.getFullYear()}-${String(bucketStart.getMonth() + 1).padStart(2, "0")}-${String(bucketStart.getDate()).padStart(2, "0")}`;
}

function formatAnalysisLocalDateKey(date) {
    if (!(date instanceof Date) || Number.isNaN(date.getTime())) {
        return "";
    }

    return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}

function formatAnalysisTrendBucketLabel(bucketStart, mode) {
    const normalizedMode = normalizeAnalysisTrendMode(mode);
    if (normalizedMode === "month") {
        return bucketStart.toLocaleDateString(undefined, {
            year: "numeric",
            month: "short"
        });
    }

    const bucketEnd = new Date(bucketStart.getFullYear(), bucketStart.getMonth(), bucketStart.getDate() + 6);
    return `${bucketStart.toLocaleDateString(undefined, { month: "short", day: "numeric" })} - ${bucketEnd.toLocaleDateString(undefined, { month: "short", day: "numeric" })}`;
}

function calculateMedian(values) {
    const normalizedValues = values
        .map(value => Number(value))
        .filter(value => Number.isFinite(value))
        .sort((left, right) => left - right);

    if (normalizedValues.length === 0) {
        return null;
    }

    const middleIndex = Math.floor(normalizedValues.length / 2);
    if (normalizedValues.length % 2 === 1) {
        return normalizedValues[middleIndex];
    }

    return (normalizedValues[middleIndex - 1] + normalizedValues[middleIndex]) / 2;
}

function buildAggregatedTrendPoints(points, mode) {
    const normalizedMode = normalizeAnalysisTrendMode(mode);
    if (normalizedMode === "fight") {
        return points.map((point, index) => ({
            ...point,
            bucketKey: point.fightId ?? `fight-${index}`,
            bucketLabel: point.fightDateLabel ?? `Fight ${index + 1}`,
            fightCount: 1
        }));
    }

    const bucketMap = new Map();
    points.forEach((point, index) => {
        const pointDate = getAnalysisTrendPointDate(point, index);
        if (!pointDate) {
            return;
        }

        const bucketStart = buildAnalysisTrendBucketStart(pointDate, normalizedMode);
        const bucketKey = buildAnalysisTrendBucketKey(bucketStart, normalizedMode);
        if (!bucketMap.has(bucketKey)) {
            bucketMap.set(bucketKey, {
                bucketKey,
                bucketStart,
                points: []
            });
        }

        bucketMap.get(bucketKey).points.push(point);
    });

    return Array.from(bucketMap.values())
        .sort((left, right) => left.bucketStart.getTime() - right.bucketStart.getTime())
        .map(bucket => ({
            bucketKey: bucket.bucketKey,
            bucketLabel: formatAnalysisTrendBucketLabel(bucket.bucketStart, normalizedMode),
            bucketStartDateKey: formatAnalysisLocalDateKey(bucket.bucketStart),
            fightCount: bucket.points.length,
            overallScore: calculateMedian(bucket.points.map(point => point.overallScore)),
            expectedScore: calculateMedian(bucket.points.map(point => point.expectedScore)),
            contextDelta: calculateMedian(bucket.points.map(point => point.contextDelta)),
            cohesionScore: calculateMedian(bucket.points.map(point => point.cohesionScore)),
            pressureScore: calculateMedian(bucket.points.map(point => point.pressureScore)),
            downstateScore: calculateMedian(bucket.points.map(point => point.downstateScore)),
            supportScore: calculateMedian(bucket.points.map(point => point.supportScore))
        }));
}

function buildAnalysisTrendSummary(rawPoints, aggregatedPoints, mode, smoothingWindow) {
    const modeOption = ANALYSIS_TREND_MODE_OPTIONS[normalizeAnalysisTrendMode(mode)];
    if (rawPoints.length === 0) {
        return "No fights matched the current filters.";
    }

    const sourceSummary = modeOption.usesMedianBuckets
        ? `${aggregatedPoints.length} ${aggregatedPoints.length === 1 ? modeOption.unitSingular : modeOption.unitPlural} from ${rawPoints.length} fights. Bucket values use medians.`
        : `${rawPoints.length} fights in date order.`;
    const smoothingSummary = smoothingWindow > 1
        ? ` ${smoothingWindow}-${modeOption.usesMedianBuckets ? "bucket" : "fight"} trailing average is overlaid.`
        : " Raw values are shown without smoothing.";
    const patchLabels = [...new Set(rawPoints.map(point => point.patchEraLabel).filter(Boolean))];
    const patchSummary = patchLabels.length === 0
        ? ""
        : patchLabels.length <= 2
            ? ` Patch eras: ${patchLabels.join(", ")}.`
            : ` Patch eras: ${patchLabels.slice(0, 2).join(", ")} +${patchLabels.length - 2} more.`;

    return `${modeOption.description} ${sourceSummary}${smoothingSummary}${patchSummary}`;
}

function startOfLocalDay(date) {
    return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

function addLocalDays(date, dayOffset) {
    return new Date(date.getFullYear(), date.getMonth(), date.getDate() + dayOffset);
}

function parseAnalysisLocalDate(value) {
    if (!value) {
        return null;
    }

    const text = String(value).trim();
    const dateOnlyMatch = text.match(/^(\d{4})-(\d{2})-(\d{2})$/);
    if (dateOnlyMatch) {
        return new Date(Number(dateOnlyMatch[1]), Number(dateOnlyMatch[2]) - 1, Number(dateOnlyMatch[3]));
    }

    const parsed = new Date(text);
    return Number.isNaN(parsed.getTime()) ? null : parsed;
}

function getAnalysisTrendMarkerPointDate(point, index) {
    const bucketStartDate = parseAnalysisLocalDate(point?.bucketStartDateKey);
    if (bucketStartDate) {
        return bucketStartDate;
    }

    return getAnalysisTrendPointDate(point, index);
}

function resolveAnalysisDateMarkerX(pointDates, markerDate) {
    const datedPoints = pointDates
        .map((date, index) => {
            if (!(date instanceof Date) || Number.isNaN(date.getTime())) {
                return null;
            }

            return {
                index,
                time: startOfLocalDay(date).getTime()
            };
        })
        .filter(Boolean);

    if (datedPoints.length === 0 || !(markerDate instanceof Date) || Number.isNaN(markerDate.getTime())) {
        return null;
    }

    const markerTime = startOfLocalDay(markerDate).getTime();
    const matchingPoints = datedPoints.filter(point => point.time === markerTime);
    if (matchingPoints.length > 0) {
        const averageIndex = matchingPoints.reduce((total, point) => total + point.index, 0) / matchingPoints.length;
        return Math.round(getAnalysisChartX(averageIndex, pointDates.length) * 100) / 100;
    }

    const firstPoint = datedPoints[0];
    const lastPoint = datedPoints[datedPoints.length - 1];
    if (markerTime < firstPoint.time || markerTime > lastPoint.time) {
        return null;
    }

    let leftPoint = firstPoint;
    for (let index = 1; index < datedPoints.length; index += 1) {
        const rightPoint = datedPoints[index];
        if (rightPoint.time < markerTime) {
            leftPoint = rightPoint;
            continue;
        }

        if (rightPoint.time === leftPoint.time) {
            return Math.round(getAnalysisChartX(rightPoint.index, pointDates.length) * 100) / 100;
        }

        const ratio = (markerTime - leftPoint.time) / (rightPoint.time - leftPoint.time);
        const markerIndex = leftPoint.index + ((rightPoint.index - leftPoint.index) * ratio);
        return Math.round(getAnalysisChartX(markerIndex, pointDates.length) * 100) / 100;
    }

    return null;
}

function formatAnalysisDateMarkerLabel(date) {
    return date.toLocaleDateString(undefined, {
        month: "short",
        day: "numeric"
    });
}

function buildAnalysisDateMarkers(points, patchEras) {
    const pointDates = (points ?? []).map((point, index) => getAnalysisTrendMarkerPointDate(point, index));
    const datedPointTimes = pointDates
        .map(date => {
            if (!(date instanceof Date) || Number.isNaN(date.getTime())) {
                return null;
            }

            return startOfLocalDay(date).getTime();
        })
        .filter(value => value != null)
        .sort((left, right) => left - right);
    const firstPointTime = datedPointTimes[0] ?? null;
    const markersByDate = new Map();

    (patchEras ?? []).forEach(era => {
        const markerDate = parseAnalysisLocalDate(era?.startsOn);
        if (!markerDate) {
            return;
        }

        let x = resolveAnalysisDateMarkerX(pointDates, markerDate);
        let activeAtStart = false;
        if (x == null && firstPointTime != null) {
            const markerTime = startOfLocalDay(markerDate).getTime();
            const eraEndDate = parseAnalysisLocalDate(era?.endsOn);
            const eraEndTime = eraEndDate ? startOfLocalDay(eraEndDate).getTime() : Infinity;
            activeAtStart = markerTime < firstPointTime && firstPointTime <= eraEndTime;
            if (activeAtStart) {
                x = 0;
            }
        }

        if (x == null) {
            return;
        }

        const dateKey = formatAnalysisLocalDateKey(markerDate);
        if (markersByDate.has(dateKey)) {
            return;
        }

        markersByDate.set(dateKey, {
            x,
            label: formatAnalysisDateMarkerLabel(markerDate),
            title: `${era?.label ? `${era.label} (${dateKey})` : dateKey}${activeAtStart ? "; active at start of selected fights" : ""}`
        });
    });

    return [...markersByDate.values()].sort((left, right) => left.x - right.x);
}

function buildAnalysisDateMarkerMarkup(markers, height = 118) {
    if ((markers ?? []).length === 0) {
        return "";
    }

    let previousLabelX = -Infinity;
    let labelLane = 0;

    return markers
        .map(marker => {
            const markerX = Math.max(2, Math.min(318, Number(marker.x)));
            if (!Number.isFinite(markerX)) {
                return "";
            }

            labelLane = markerX - previousLabelX < 58 ? (labelLane + 1) % 2 : 0;
            previousLabelX = markerX;

            const labelAnchor = markerX > 276 ? "end" : "start";
            const labelX = labelAnchor === "end"
                ? Math.max(6, markerX - 5)
                : Math.min(314, markerX + 5);
            const labelY = 13 + (labelLane * 13);

            return `
                <g class="date-marker">
                    <line class="date-marker-line" x1="${markerX}" y1="0" x2="${markerX}" y2="${height}">
                        <title>${escapeHtml(marker.title)}</title>
                    </line>
                    <text class="date-marker-label" x="${labelX}" y="${labelY}" text-anchor="${labelAnchor}">${escapeHtml(marker.label)}</text>
                </g>
            `;
        })
        .filter(Boolean)
        .join("");
}

function getAnalysisTrendComparisonWindow(mode, pointCount) {
    const modeOption = ANALYSIS_TREND_MODE_OPTIONS[normalizeAnalysisTrendMode(mode)];
    if (pointCount < 2) {
        return 0;
    }

    return Math.max(1, Math.min(modeOption.recentWindow, Math.floor(pointCount / 2)));
}

function buildPerFightTrendComparison(points, metricKey) {
    const datedPoints = points
        .map((point, index) => ({
            date: getAnalysisTrendPointDate(point, index),
            value: Number(point?.[metricKey])
        }))
        .filter(point => point.date && Number.isFinite(point.value))
        .sort((left, right) => left.date.getTime() - right.date.getTime());

    if (datedPoints.length < 2) {
        return null;
    }

    const latestDay = startOfLocalDay(datedPoints[datedPoints.length - 1].date);
    const recentStart = addLocalDays(latestDay, -9);
    const priorStart = addLocalDays(recentStart, -10);
    const recentValues = datedPoints
        .filter(point => point.date >= recentStart)
        .map(point => point.value);
    const priorValues = datedPoints
        .filter(point => point.date >= priorStart && point.date < recentStart)
        .map(point => point.value);

    return {
        recentValues,
        priorValues,
        comparisonLabel: `Last 10 days (${recentValues.length} fights) vs previous 10 days (${priorValues.length} fights).`,
        insufficientMessage: "Need at least one scored fight in both the last 10 days and the previous 10 days."
    };
}

function buildBucketTrendComparison(points, mode, metricKey) {
    const numericPoints = points
        .map(point => Number(point?.[metricKey]))
        .filter(value => Number.isFinite(value));
    const comparisonWindow = getAnalysisTrendComparisonWindow(mode, numericPoints.length);
    const modeOption = ANALYSIS_TREND_MODE_OPTIONS[normalizeAnalysisTrendMode(mode)];

    if (comparisonWindow <= 0) {
        return null;
    }

    const unitLabel = comparisonWindow === 1 ? modeOption.unitSingular : modeOption.unitPlural;

    return {
        recentValues: numericPoints.slice(-comparisonWindow),
        priorValues: numericPoints.slice(-comparisonWindow * 2, -comparisonWindow),
        comparisonLabel: `Last ${comparisonWindow} ${unitLabel} vs previous ${comparisonWindow} ${unitLabel}.`,
        insufficientMessage: `Need at least two comparable ${modeOption.unitPlural} to compare direction.`
    };
}

function buildAnalysisTrendDeltaCard(metric, aggregatedPoints, mode) {
    const modeOption = ANALYSIS_TREND_MODE_OPTIONS[normalizeAnalysisTrendMode(mode)];
    const comparison = mode === "fight"
        ? buildPerFightTrendComparison(aggregatedPoints, metric.key)
        : buildBucketTrendComparison(aggregatedPoints, mode, metric.key);

    if (!comparison || comparison.recentValues.length === 0 || comparison.priorValues.length === 0) {
        return `
            <article class="analysis-card analysis-delta-card">
                <strong>${escapeHtml(metric.comparisonLabel)}</strong>
                <div class="analysis-delta-direction is-flat">Not enough data</div>
                <div class="table-inline-note">${escapeHtml(comparison?.insufficientMessage ?? `Need at least two comparable ${modeOption.unitPlural} to compare direction.`)}</div>
            </article>
        `;
    }

    const { recentValues, priorValues } = comparison;
    const recentAverage = recentValues.reduce((total, value) => total + value, 0) / recentValues.length;
    const priorAverage = priorValues.reduce((total, value) => total + value, 0) / priorValues.length;
    const delta = recentAverage - priorAverage;
    const directionClass = Math.abs(delta) < 0.75
        ? "is-flat"
        : delta > 0
            ? "is-up"
            : "is-down";
    const directionLabel = Math.abs(delta) < 0.75
        ? "Flat"
        : delta > 0
            ? (metric.positiveLabel ?? "Up")
            : (metric.negativeLabel ?? "Down");

    return `
        <article class="analysis-card analysis-delta-card">
            <strong>${escapeHtml(metric.comparisonLabel)}</strong>
            <div class="analysis-delta-direction ${directionClass}">${escapeHtml(directionLabel)}</div>
            <div class="analysis-delta-metric">${escapeHtml(formatSignedNumber(delta, 1))}</div>
            <div class="table-inline-note">${escapeHtml(comparison.comparisonLabel)}</div>
            <div class="analysis-delta-pair">
                <span>Recent ${escapeHtml(formatAnalysisMetricValue(metric, recentAverage, 1))}</span>
                <span>Prior ${escapeHtml(formatAnalysisMetricValue(metric, priorAverage, 1))}</span>
            </div>
        </article>
    `;
}

function buildAnalysisChartCard(title, valueLabel, values, detail, smoothingWindow = ANALYSIS_TREND_ROLLING_WINDOW, showPoints = false, minValue = 0, maxValue = 100, dateMarkers = []) {
    const normalizedSmoothingWindow = normalizeAnalysisTrendSmoothingWindow(smoothingWindow);
    const rawPath = normalizedSmoothingWindow > 1 ? buildAnalysisLinePath(values, minValue, maxValue) : "";
    const smoothedValues = buildRollingAverage(values, normalizedSmoothingWindow);
    const smoothPath = buildAnalysisLinePath(smoothedValues, minValue, maxValue);
    const pointMarkup = showPoints ? buildAnalysisPointMarkup(values, minValue, maxValue) : "";
    const dateMarkerMarkup = buildAnalysisDateMarkerMarkup(dateMarkers);
    const empty = !rawPath && !smoothPath;
    const numericCount = values.filter(value => value != null && !Number.isNaN(Number(value))).length;
    const effectiveWindow = normalizedSmoothingWindow > 1
        ? Math.max(1, Math.min(normalizedSmoothingWindow, numericCount))
        : 1;
    const smoothingDetail = normalizedSmoothingWindow > 1 && effectiveWindow >= 2
        ? `${effectiveWindow}-point trailing average`
        : null;

    return `
        <article class="analysis-card">
            <strong>${escapeHtml(title)}</strong>
            <div class="analysis-card-value">${escapeHtml(valueLabel)}</div>
            ${detail ? `<div class="table-inline-note">${escapeHtml(detail)}</div>` : ""}
            ${smoothingDetail ? `<div class="table-inline-note">${escapeHtml(smoothingDetail)}</div>` : ""}
            <svg class="analysis-chart" viewBox="0 0 320 118" preserveAspectRatio="none" aria-hidden="true">
                <line class="grid-line" x1="0" y1="29.5" x2="320" y2="29.5"></line>
                <line class="grid-line" x1="0" y1="59" x2="320" y2="59"></line>
                <line class="grid-line" x1="0" y1="88.5" x2="320" y2="88.5"></line>
                ${dateMarkerMarkup}
                ${rawPath ? `<path class="raw-line" d="${escapeHtml(rawPath)}"></path>` : ""}
                ${smoothPath ? `<path class="trend-line" d="${escapeHtml(smoothPath)}"></path>` : ""}
                ${pointMarkup}
            </svg>
            ${empty ? `<div class="table-inline-note">No score points available for this selection.</div>` : ""}
        </article>
    `;
}

function getAnalysisBurstMetricValue(point, sideKey, metricKey) {
    const value = point?.[sideKey]?.[metricKey];
    const numeric = Number(value);
    return Number.isFinite(numeric) ? numeric : null;
}

function getAnalysisMaxNumeric(values) {
    const numericValues = (values ?? [])
        .map(value => Number(value))
        .filter(value => Number.isFinite(value));

    return numericValues.length === 0 ? null : Math.max(...numericValues);
}

function buildAnalysisBurstSideAggregate(points, sideKey) {
    return ANALYSIS_BURST_TREND_METRICS.reduce((side, metric) => {
        side[metric.key] = getAnalysisMaxNumeric(points.map(point => getAnalysisBurstMetricValue(point, sideKey, metric.key)));
        return side;
    }, {});
}

function buildAggregatedBurstTrendPoints(points, mode) {
    const normalizedMode = normalizeAnalysisTrendMode(mode);
    if (normalizedMode === "fight") {
        return (points ?? []).map((point, index) => ({
            ...point,
            bucketKey: point.fightId ?? `fight-${index}`,
            bucketLabel: point.fightDateLabel ?? `Fight ${index + 1}`,
            fightCount: 1
        }));
    }

    const bucketMap = new Map();
    (points ?? []).forEach((point, index) => {
        const pointDate = getAnalysisTrendPointDate(point, index);
        if (!pointDate) {
            return;
        }

        const bucketStart = buildAnalysisTrendBucketStart(pointDate, normalizedMode);
        const bucketKey = buildAnalysisTrendBucketKey(bucketStart, normalizedMode);
        if (!bucketMap.has(bucketKey)) {
            bucketMap.set(bucketKey, {
                bucketKey,
                bucketStart,
                points: []
            });
        }

        bucketMap.get(bucketKey).points.push(point);
    });

    return Array.from(bucketMap.values())
        .sort((left, right) => left.bucketStart.getTime() - right.bucketStart.getTime())
        .map(bucket => ({
            bucketKey: bucket.bucketKey,
            bucketLabel: formatAnalysisTrendBucketLabel(bucket.bucketStart, normalizedMode),
            bucketStartDateKey: formatAnalysisLocalDateKey(bucket.bucketStart),
            fightCount: bucket.points.length,
            squad: buildAnalysisBurstSideAggregate(bucket.points, "squad"),
            enemy: buildAnalysisBurstSideAggregate(bucket.points, "enemy")
        }));
}

function buildAnalysisBurstTrendSummary(rawPoints, aggregatedPoints, mode) {
    if ((rawPoints ?? []).length === 0) {
        return "No retained burst data matched the current filters.";
    }

    const modeOption = ANALYSIS_TREND_MODE_OPTIONS[normalizeAnalysisTrendMode(mode)];
    if (modeOption.usesMedianBuckets) {
        return `${aggregatedPoints.length} ${aggregatedPoints.length === 1 ? modeOption.unitSingular : modeOption.unitPlural} from ${rawPoints.length} fights with retained burst data. Bucket values show the best retained value per metric.`;
    }

    return `${rawPoints.length} fights with retained burst data in date order. Values show the best retained value per metric for each side.`;
}

function buildAnalysisPointMarkupWithClass(values, minValue, maxValue, className) {
    const height = 118;
    const normalizedValues = (values ?? []).map(value => {
        if (value == null) {
            return null;
        }

        const numeric = Number(value);
        return Number.isNaN(numeric) ? null : numeric;
    });

    if (!normalizedValues.some(value => value != null)) {
        return "";
    }

    const range = Math.max(1, maxValue - minValue);
    return normalizedValues
        .map((value, index) => {
            if (value == null) {
                return "";
            }

            const x = Math.round(getAnalysisChartX(index, normalizedValues.length) * 100) / 100;
            const clampedValue = Math.max(minValue, Math.min(maxValue, value));
            const y = Math.round((height - ((clampedValue - minValue) / range) * height) * 100) / 100;
            return `<circle class="${escapeHtml(className)}" cx="${x}" cy="${y}" r="2.5"></circle>`;
        })
        .filter(Boolean)
        .join("");
}

function buildAnalysisBurstSeriesMarkup(values, sideKey, minValue, maxValue, smoothingWindow, showPoints) {
    const normalizedSmoothingWindow = normalizeAnalysisTrendSmoothingWindow(smoothingWindow);
    const rawPath = normalizedSmoothingWindow > 1 ? buildAnalysisLinePath(values, minValue, maxValue) : "";
    const displayValues = normalizedSmoothingWindow > 1
        ? buildRollingAverage(values, normalizedSmoothingWindow)
        : values;
    const trendPath = buildAnalysisLinePath(displayValues, minValue, maxValue);
    const pointMarkup = showPoints
        ? buildAnalysisPointMarkupWithClass(values, minValue, maxValue, `burst-point burst-point-${sideKey}`)
        : "";

    return `
        ${rawPath ? `<path class="burst-line burst-line-raw burst-line-${sideKey}" d="${escapeHtml(rawPath)}"></path>` : ""}
        ${trendPath ? `<path class="burst-line burst-line-${sideKey}" d="${escapeHtml(trendPath)}"></path>` : ""}
        ${pointMarkup}
    `;
}

function resolveAnalysisBurstChartMaxValue(metric, values) {
    const maxValue = getAnalysisMaxNumeric(values) ?? 1;
    if (metric.key === "damage") {
        const step = maxValue >= 1_000_000 ? 250_000 : 50_000;
        return Math.max(step, Math.ceil((maxValue * 1.08) / step) * step);
    }

    const step = maxValue <= 5 ? 1 : maxValue <= 20 ? 2 : 5;
    return Math.max(step, Math.ceil((maxValue * 1.15) / step) * step);
}

function formatAnalysisBurstTrendValue(metric, value) {
    if (value == null) {
        return "n/a";
    }

    return formatNumber(value, 0);
}

function buildAnalysisBurstTrendCard(metric, aggregatedPoints, dateMarkers) {
    const squadValues = (aggregatedPoints ?? []).map(point => getAnalysisBurstMetricValue(point, "squad", metric.key));
    const enemyValues = (aggregatedPoints ?? []).map(point => getAnalysisBurstMetricValue(point, "enemy", metric.key));
    const maxValue = resolveAnalysisBurstChartMaxValue(metric, [...squadValues, ...enemyValues]);
    const squadBest = getAnalysisMaxNumeric(squadValues);
    const enemyBest = getAnalysisMaxNumeric(enemyValues);
    const empty = squadBest == null && enemyBest == null;
    const showPoints = analysisTrendMode !== "fight" || (aggregatedPoints ?? []).length <= 24;
    const pointText = analysisTrendMode === "fight"
        ? `${(aggregatedPoints ?? []).length} fights in date order.`
        : `${(aggregatedPoints ?? []).length} ${(aggregatedPoints ?? []).length === 1 ? ANALYSIS_TREND_MODE_OPTIONS[analysisTrendMode].unitSingular : ANALYSIS_TREND_MODE_OPTIONS[analysisTrendMode].unitPlural}.`;

    return `
        <article class="analysis-card analysis-burst-chart-card">
            <strong>${escapeHtml(metric.title)}</strong>
            <div class="analysis-card-value analysis-burst-card-value">
                <span>Squad ${escapeHtml(formatAnalysisBurstTrendValue(metric, squadBest))}</span>
                <span>Enemy ${escapeHtml(formatAnalysisBurstTrendValue(metric, enemyBest))}</span>
            </div>
            <div class="table-inline-note">${escapeHtml(`${metric.detail} ${pointText}`)}</div>
            <svg class="analysis-chart analysis-burst-chart" viewBox="0 0 320 118" preserveAspectRatio="none" aria-hidden="true">
                <line class="grid-line" x1="0" y1="29.5" x2="320" y2="29.5"></line>
                <line class="grid-line" x1="0" y1="59" x2="320" y2="59"></line>
                <line class="grid-line" x1="0" y1="88.5" x2="320" y2="88.5"></line>
                ${buildAnalysisDateMarkerMarkup(dateMarkers)}
                ${buildAnalysisBurstSeriesMarkup(squadValues, "squad", 0, maxValue, analysisTrendSmoothingWindow, showPoints)}
                ${buildAnalysisBurstSeriesMarkup(enemyValues, "enemy", 0, maxValue, analysisTrendSmoothingWindow, showPoints)}
            </svg>
            ${empty ? `<div class="table-inline-note">No ${escapeHtml(metric.unit)} burst values available for this selection.</div>` : ""}
        </article>
    `;
}

function ensureAnalysisBurstComparisonSelection() {
    const validIds = ANALYSIS_BURST_COMPARISON_SERIES.map(series => series.id);
    if (selectedAnalysisBurstComparisonIds === null) {
        selectedAnalysisBurstComparisonIds = new Set(validIds);
        return selectedAnalysisBurstComparisonIds;
    }

    selectedAnalysisBurstComparisonIds = new Set(
        Array.from(selectedAnalysisBurstComparisonIds)
            .filter(id => validIds.includes(id)));
    return selectedAnalysisBurstComparisonIds;
}

function buildAnalysisBurstComparisonControls(selectedIds) {
    return ANALYSIS_BURST_COMPARISON_SERIES.map(series => {
        const checked = selectedIds?.has(series.id) ? "checked" : "";
        return `
            <label class="analysis-burst-comparison-option ${checked ? "is-active" : ""}" style="--burst-color: ${escapeHtml(series.color)}">
                <input type="checkbox" data-analysis-burst-comparison-id="${escapeHtml(series.id)}" ${checked}>
                <span class="analysis-burst-comparison-swatch"></span>
                <span>${escapeHtml(series.label)}</span>
            </label>
        `;
    }).join("");
}

function getAnalysisBurstMetricMaxima(aggregatedPoints) {
    return ANALYSIS_BURST_TREND_METRICS.reduce((maxima, metric) => {
        maxima[metric.key] = getAnalysisMaxNumeric([
            ...(aggregatedPoints ?? []).map(point => getAnalysisBurstMetricValue(point, "squad", metric.key)),
            ...(aggregatedPoints ?? []).map(point => getAnalysisBurstMetricValue(point, "enemy", metric.key))
        ]);
        return maxima;
    }, {});
}

function getAnalysisBurstSeriesData(aggregatedPoints) {
    const maxima = getAnalysisBurstMetricMaxima(aggregatedPoints);
    return ANALYSIS_BURST_COMPARISON_SERIES.map(series => {
        const metric = ANALYSIS_BURST_TREND_METRICS.find(item => item.key === series.metricKey);
        const metricMax = maxima[series.metricKey];
        const rawValues = (aggregatedPoints ?? []).map(point => getAnalysisBurstMetricValue(point, series.sideKey, series.metricKey));
        const values = rawValues.map(value => {
            if (value == null || !metricMax || metricMax <= 0) {
                return null;
            }

            return Math.max(0, Math.min(100, (value / metricMax) * 100));
        });

        return {
            ...series,
            metric,
            metricMax,
            rawValues,
            values
        };
    });
}

function getAnalysisBurstComparisonChartLayout(pointCount) {
    const width = 640;
    const height = 260;
    const plot = {
        left: 42,
        top: 24,
        right: 628,
        bottom: 220
    };
    plot.width = plot.right - plot.left;
    plot.height = plot.bottom - plot.top;
    plot.xForIndex = index => plot.left + (pointCount <= 1 ? plot.width / 2 : index * (plot.width / (pointCount - 1)));
    plot.yForValue = value => plot.bottom - ((Math.max(0, Math.min(100, value)) / 100) * plot.height);
    return { width, height, plot };
}

function buildAnalysisBurstComparisonPath(values, chart) {
    let started = false;
    return (values ?? [])
        .map((value, index) => {
            if (value == null) {
                started = false;
                return "";
            }

            const x = Math.round(chart.plot.xForIndex(index) * 100) / 100;
            const y = Math.round(chart.plot.yForValue(value) * 100) / 100;
            const command = started ? "L" : "M";
            started = true;
            return `${command} ${x} ${y}`;
        })
        .filter(Boolean)
        .join(" ");
}

function buildAnalysisBurstComparisonPointMarkup(series, aggregatedPoints, chart, showPoints) {
    if (!showPoints) {
        return "";
    }

    return (series.values ?? []).map((value, index) => {
        if (value == null) {
            return "";
        }

        const point = aggregatedPoints[index];
        const rawValue = series.rawValues[index];
        const x = Math.round(chart.plot.xForIndex(index) * 100) / 100;
        const y = Math.round(chart.plot.yForValue(value) * 100) / 100;
        const metricUnit = series.metric?.unit ?? series.metricKey;
        const label = `${series.label} ${point?.bucketLabel ?? point?.fightDateLabel ?? ""}: ${formatNumber(rawValue, 0)} ${metricUnit}, ${formatNumber(value, 0)} normalized`;
        return `
            <circle class="analysis-burst-comparison-point"
                cx="${x}"
                cy="${y}"
                r="3"
                style="--burst-color: ${escapeHtml(series.color)}">
                <title>${escapeHtml(label)}</title>
            </circle>
        `;
    }).filter(Boolean).join("");
}

function buildAnalysisBurstComparisonGrid(chart) {
    return [100, 75, 50, 25, 0].map(value => {
        const y = Math.round(chart.plot.yForValue(value) * 100) / 100;
        return `
            <g class="analysis-burst-comparison-grid-row">
                <line class="grid-line" x1="${chart.plot.left}" y1="${y}" x2="${chart.plot.right}" y2="${y}"></line>
                <text class="analysis-burst-comparison-axis-label" x="8" y="${y + 4}">${value}</text>
            </g>
        `;
    }).join("");
}

function buildAnalysisBurstComparisonMarkerMarkup(markers, chart) {
    if ((markers ?? []).length === 0) {
        return "";
    }

    return markers.map(marker => {
        const markerX = chart.plot.left + ((Math.max(0, Math.min(320, Number(marker.x))) / 320) * chart.plot.width);
        if (!Number.isFinite(markerX)) {
            return "";
        }

        const labelAnchor = markerX > chart.plot.right - 42 ? "end" : "start";
        const labelX = labelAnchor === "end" ? markerX - 5 : markerX + 5;
        return `
            <g class="date-marker analysis-burst-comparison-date-marker">
                <line class="date-marker-line" x1="${markerX}" y1="${chart.plot.top}" x2="${markerX}" y2="${chart.plot.bottom}">
                    <title>${escapeHtml(marker.title)}</title>
                </line>
                <text class="date-marker-label" x="${labelX}" y="16" text-anchor="${labelAnchor}">${escapeHtml(marker.label)}</text>
            </g>
        `;
    }).filter(Boolean).join("");
}

function buildAnalysisBurstComparisonChart(aggregatedPoints, dateMarkers, selectedIds) {
    const selectedSeries = getAnalysisBurstSeriesData(aggregatedPoints)
        .filter(series => selectedIds?.has(series.id));
    if ((aggregatedPoints ?? []).length === 0) {
        return `<div class="analysis-boon-trend-empty">No retained burst data available for this selection.</div>`;
    }

    if (selectedSeries.length === 0) {
        return `<div class="analysis-boon-trend-empty">No burst components selected.</div>`;
    }

    const chart = getAnalysisBurstComparisonChartLayout(aggregatedPoints.length);
    const normalizedSmoothingWindow = normalizeAnalysisTrendSmoothingWindow(analysisTrendSmoothingWindow);
    const showPoints = analysisTrendMode !== "fight" || aggregatedPoints.length <= 36;
    const seriesMarkup = selectedSeries.map(series => {
        const displayValues = normalizedSmoothingWindow > 1
            ? buildRollingAverage(series.values, normalizedSmoothingWindow)
            : series.values;
        const path = buildAnalysisBurstComparisonPath(displayValues, chart);
        return `
            <g class="analysis-burst-comparison-series">
                ${path ? `<path class="analysis-burst-comparison-line" d="${escapeHtml(path)}" style="--burst-color: ${escapeHtml(series.color)}"></path>` : ""}
                ${buildAnalysisBurstComparisonPointMarkup(series, aggregatedPoints, chart, showPoints)}
            </g>
        `;
    }).join("");
    const smoothingLabel = normalizedSmoothingWindow > 1
        ? `${normalizedSmoothingWindow}-point trailing average`
        : "Raw values";

    return `
        <svg class="analysis-burst-comparison-chart" viewBox="0 0 ${chart.width} ${chart.height}" preserveAspectRatio="xMidYMid meet" aria-hidden="false">
            <title>${escapeHtml(`Normalized burst comparison, ${smoothingLabel}`)}</title>
            ${buildAnalysisBurstComparisonGrid(chart)}
            ${buildAnalysisBurstComparisonMarkerMarkup(dateMarkers, chart)}
            ${seriesMarkup}
            <text class="analysis-burst-comparison-axis-caption" x="${chart.plot.left}" y="244">${escapeHtml(smoothingLabel)}</text>
        </svg>
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

function getConfiguredLogDirectoryPath() {
    return currentDashboardSnapshot?.workspace?.pendingDirectoryPath ?? "";
}

function isConfiguredLogDirectoryAvailable() {
    return Boolean(currentDashboardSnapshot?.workspace?.pendingDirectoryConfigured && getConfiguredLogDirectoryPath());
}

function getArchiveLogDirectoryPath() {
    return currentDashboardSnapshot?.workspace?.archiveLogDirectoryPath ?? "";
}

function isArchiveLogDirectoryAvailable() {
    return Boolean(currentDashboardSnapshot?.workspace?.archiveLogDirectoryConfigured && getArchiveLogDirectoryPath());
}

function getBatchDirectoryPath(mode) {
    return mode === "rebuild-all"
        ? getArchiveLogDirectoryPath()
        : getConfiguredLogDirectoryPath();
}

function isBatchDirectoryAvailable(mode) {
    return mode === "rebuild-all"
        ? isArchiveLogDirectoryAvailable()
        : isConfiguredLogDirectoryAvailable();
}

function normalizeAppTab(value) {
    switch (String(value ?? "").trim().toLowerCase()) {
        case "fight-browser":
            return "fight-browser";
        case "analysis":
            return "analysis";
        default:
            return "manage";
    }
}

function getRequestedAppTab() {
    const requested = new URL(window.location.href).searchParams.get("tab");
    return requested ? normalizeAppTab(requested) : null;
}

function getDashboardUrl(tabKey = null) {
    const normalizedTab = normalizeAppTab(tabKey);
    if (normalizedTab === "manage") {
        return "/";
    }

    const params = new URLSearchParams();
    params.set("tab", normalizedTab);
    return `/?${params.toString()}`;
}

function buildFightDossierUrl(fightId) {
    const params = new URLSearchParams();
    params.set("tab", "fight-browser");
    params.set("fightId", fightId);
    return `/?${params.toString()}`;
}

function resolveInitialAppTab() {
    if (getSelectedFightId()) {
        return "fight-browser";
    }

    return getRequestedAppTab()
        ?? normalizeAppTab(localStorage.getItem(ACTIVE_APP_TAB_KEY))
        ?? "manage";
}

function setActiveAppTab(tabKey, options = {}) {
    const { persist = true, loadAnalysis = true } = options;
    const normalizedTab = normalizeAppTab(tabKey);
    activeAppTab = normalizedTab;

    document.querySelectorAll("[data-app-tab]").forEach(button => {
        const isActive = button.dataset.appTab === normalizedTab;
        button.classList.toggle("is-active", isActive);
        button.setAttribute("aria-selected", isActive ? "true" : "false");
    });

    document.querySelectorAll("[data-app-panel]").forEach(panel => {
        panel.classList.toggle("is-app-hidden", panel.dataset.appPanel !== normalizedTab);
    });

    if (persist) {
        localStorage.setItem(ACTIVE_APP_TAB_KEY, normalizedTab);
    }

    if (loadAnalysis && normalizedTab === "analysis") {
        void ensureAnalysisLoaded();
    }
}

async function loadDashboard() {
    const response = await fetch("/api/dashboard");
    if (!response.ok) {
        throw new Error(`Dashboard request failed with status ${response.status}`);
    }

    return response.json();
}

async function loadPatchMetadata() {
    const response = await fetch("/api/patch-metadata");
    if (!response.ok) {
        throw new Error(`Patch metadata request failed with status ${response.status}`);
    }

    return response.json();
}

async function savePatchMetadata(metadata) {
    const response = await fetch("/api/patch-metadata", {
        method: "PUT",
        headers: {
            "Content-Type": "application/json"
        },
        body: JSON.stringify(metadata)
    });
    const payload = await readApiPayload(response);
    if (!response.ok) {
        throw new Error(payload?.message ?? `Patch metadata save failed with status ${response.status}`);
    }

    return payload;
}

async function loadCompHelperConfig() {
    const response = await fetch("/api/comp-helper-config");
    if (!response.ok) {
        throw new Error(`Comp Helper config request failed with status ${response.status}`);
    }

    return response.json();
}

async function saveCompHelperConfig(config) {
    const response = await fetch("/api/comp-helper-config", {
        method: "PUT",
        headers: {
            "Content-Type": "application/json"
        },
        body: JSON.stringify(config)
    });
    const payload = await readApiPayload(response);
    if (!response.ok) {
        throw new Error(payload?.message ?? `Comp Helper config save failed with status ${response.status}`);
    }

    return payload;
}

async function resetCompHelperConfig() {
    const response = await fetch("/api/comp-helper-config/reset", { method: "POST" });
    const payload = await readApiPayload(response);
    if (!response.ok) {
        throw new Error(payload?.message ?? `Comp Helper config reset failed with status ${response.status}`);
    }

    return payload;
}

async function deleteCommanderFights(commander) {
    const response = await fetch("/api/manage/commanders/delete", {
        method: "POST",
        headers: {
            "Content-Type": "application/json"
        },
        body: JSON.stringify({ commander })
    });
    const payload = await readApiPayload(response);
    if (!response.ok) {
        const error = new Error(payload?.message ?? `Commander fight delete failed with status ${response.status}`);
        error.payload = payload;
        throw error;
    }

    return payload;
}

async function loadFightDetail(fightId) {
    const response = await fetch(`/api/fights/${encodeURIComponent(fightId)}`);
    if (!response.ok) {
        throw new Error(`Fight detail request failed with status ${response.status}`);
    }

    return response.json();
}

function buildAnalysisQueryString(filters = {}) {
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
    if (filters.patchScope && filters.patchScope !== "all") {
        params.set("patchScope", filters.patchScope);
    }
    const fightAttributes = joinKeyFilterValues(filters.fightAttributes);
    if (fightAttributes) {
        params.set("fightAttributes", fightAttributes);
    }
    const squadIncludeClasses = joinClassFilterLabels(filters.squadIncludeClasses);
    const squadExcludeClasses = joinClassFilterLabels(filters.squadExcludeClasses);
    const enemyIncludeClasses = joinClassFilterLabels(filters.enemyIncludeClasses);
    const enemyExcludeClasses = joinClassFilterLabels(filters.enemyExcludeClasses);
    if (squadIncludeClasses) {
        params.set("squadIncludeClasses", squadIncludeClasses);
    }
    if (squadExcludeClasses) {
        params.set("squadExcludeClasses", squadExcludeClasses);
    }
    if (enemyIncludeClasses) {
        params.set("enemyIncludeClasses", enemyIncludeClasses);
    }
    if (enemyExcludeClasses) {
        params.set("enemyExcludeClasses", enemyExcludeClasses);
    }

    return params.toString();
}

async function loadAnalysis(filters = {}) {
    const query = buildAnalysisQueryString(filters);
    const response = await fetch(`/api/analysis${query ? `?${query}` : ""}`);
    if (!response.ok) {
        throw new Error(`Analysis request failed with status ${response.status}`);
    }

    return response.json();
}

async function loadAnalysisPlayerDetail(account, filters = {}) {
    const query = buildAnalysisQueryString(filters);
    const response = await fetch(`/api/analysis/players/${encodeURIComponent(account)}${query ? `?${query}` : ""}`);
    if (!response.ok) {
        throw new Error(`Player detail request failed with status ${response.status}`);
    }

    return response.json();
}

async function loadAnalysisPlayerDetails(filters = {}) {
    const query = buildAnalysisQueryString(filters);
    const response = await fetch(`/api/analysis/player-details${query ? `?${query}` : ""}`);
    if (!response.ok) {
        throw new Error(`Player details request failed with status ${response.status}`);
    }

    return response.json();
}

function resetAnalysisPlayerDetailState() {
    currentAnalysisPlayerDetailsByAccount = new Map();
    currentAnalysisPlayerDetailPromisesByAccount = new Map();
    currentAnalysisAllPlayerDetails = null;
    currentAnalysisAllPlayerDetailsPromise = null;
}

function getAnalysisPlayerAccountKey(account) {
    return String(account ?? "").trim().toLowerCase();
}

function cacheAnalysisPlayerDetail(player) {
    const key = getAnalysisPlayerAccountKey(player?.account);
    if (!key) {
        return;
    }

    currentAnalysisPlayerDetailsByAccount.set(key, player);
}

function getCachedAnalysisPlayerDetail(account) {
    const key = getAnalysisPlayerAccountKey(account);
    return key ? currentAnalysisPlayerDetailsByAccount.get(key) ?? null : null;
}

async function ensureAnalysisPlayerDetail(account) {
    const key = getAnalysisPlayerAccountKey(account);
    if (!key) {
        return null;
    }

    const cached = currentAnalysisPlayerDetailsByAccount.get(key);
    if (cached) {
        return cached;
    }

    const pending = currentAnalysisPlayerDetailPromisesByAccount.get(key);
    if (pending) {
        return pending;
    }

    const detailMap = currentAnalysisPlayerDetailsByAccount;
    const promiseMap = currentAnalysisPlayerDetailPromisesByAccount;
    const promise = loadAnalysisPlayerDetail(account, getAnalysisFiltersFromUi())
        .then(player => {
            if (detailMap === currentAnalysisPlayerDetailsByAccount) {
                cacheAnalysisPlayerDetail(player);
            }

            return player;
        })
        .finally(() => {
            if (promiseMap === currentAnalysisPlayerDetailPromisesByAccount) {
                promiseMap.delete(key);
            }
        });

    currentAnalysisPlayerDetailPromisesByAccount.set(key, promise);
    return promise;
}

async function ensureAnalysisAllPlayerDetails() {
    if (currentAnalysisAllPlayerDetails) {
        return currentAnalysisAllPlayerDetails;
    }

    if (currentAnalysisAllPlayerDetailsPromise) {
        return currentAnalysisAllPlayerDetailsPromise;
    }

    const detailMap = currentAnalysisPlayerDetailsByAccount;
    const promise = loadAnalysisPlayerDetails(getAnalysisFiltersFromUi())
        .then(players => {
            if (detailMap === currentAnalysisPlayerDetailsByAccount) {
                currentAnalysisAllPlayerDetails = players ?? [];
                currentAnalysisAllPlayerDetails.forEach(cacheAnalysisPlayerDetail);
            }

            return players ?? [];
        })
        .finally(() => {
            if (currentAnalysisAllPlayerDetailsPromise === promise) {
                currentAnalysisAllPlayerDetailsPromise = null;
            }
        });

    currentAnalysisAllPlayerDetailsPromise = promise;
    return promise;
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

function getTopBurstActorName(actor) {
    if (!actor) {
        return "-";
    }

    return actor.character
        || actor.account
        || (actor.actorId ? `Actor ${actor.actorId}` : "-");
}

function buildFightBrowserTopBurstActorCell(actor, unitLabel) {
    if (!actor) {
        return "-";
    }

    const lines = [
        `<strong>${escapeHtml(getTopBurstActorName(actor))}</strong>`
    ];
    if (actor.amount > 0) {
        lines.push(`<span class="table-inline-note">${escapeHtml(`${formatNumber(actor.amount)} ${unitLabel}`)}</span>`);
    }

    const specLabel = [actor.eliteSpec, actor.profession]
        .filter(Boolean)
        .join(" / ");
    if (specLabel) {
        lines.push(`<span class="table-inline-note">${escapeHtml(specLabel)}</span>`);
    }

    return `<div class="table-stack">${lines.join("")}</div>`;
}

function buildFightBrowserTopBurstEntries(filteredFights) {
    const allEntries = filteredFights
        .flatMap(fight => (fight.fightIndex?.topBursts ?? []).map(burst => ({ fight, burst })));

    allEntries.sort((left, right) =>
        Number(right.burst?.damage ?? 0) - Number(left.burst?.damage ?? 0)
        || Number(right.burst?.strips ?? 0) - Number(left.burst?.strips ?? 0)
        || Number(right.burst?.downs ?? 0) - Number(left.burst?.downs ?? 0)
        || Number(right.burst?.kills ?? 0) - Number(left.burst?.kills ?? 0)
        || getFightStartValue(right.fight) - getFightStartValue(left.fight)
        || Number(right.burst?.time ?? 0) - Number(left.burst?.time ?? 0));

    const retainedEntries = allEntries.slice(0, 500);
    return {
        allCount: allEntries.length,
        retainedEntries,
        displayedEntries: retainedEntries.slice(0, 25)
    };
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

function normalizeClassFilterLabels(values) {
    const rawValues = Array.isArray(values)
        ? values
        : String(values ?? "").split(/[,\n;\r]+/);

    return [...new Set(rawValues
        .map(label => String(label ?? "").trim())
        .filter(Boolean))]
        .sort((left, right) => left.localeCompare(right, undefined, { sensitivity: "base" }));
}

function joinClassFilterLabels(labels) {
    return normalizeClassFilterLabels(labels).join(", ");
}

function normalizeKeyFilterValues(values) {
    const rawValues = Array.isArray(values)
        ? values
        : String(values ?? "").split(/[,\n;\r|]+/);

    return [...new Set(rawValues
        .map(value => String(value ?? "").trim())
        .filter(Boolean))];
}

function joinKeyFilterValues(values) {
    return normalizeKeyFilterValues(values).join(",");
}

function getPatchErasFromSource(source) {
    return source?.patchEras
        ?? source?.patchMetadata?.patchEras
        ?? currentPatchMetadata?.patchEras
        ?? [];
}

function getCurrentPatchEra(eras) {
    return (eras ?? []).find(era => era?.isCurrent)
        ?? [...(eras ?? [])].sort((left, right) => String(right?.startsOn ?? "").localeCompare(String(left?.startsOn ?? "")))[0]
        ?? null;
}

function getLastPatchEraIds(eras, count = 2) {
    return [...(eras ?? [])]
        .sort((left, right) => String(right?.startsOn ?? "").localeCompare(String(left?.startsOn ?? "")))
        .slice(0, count)
        .map(era => era?.id)
        .filter(Boolean);
}

function renderPatchScopeOptions(selector, eras, selectedValue = "all") {
    const select = document.querySelector(selector);
    if (!select) {
        return;
    }

    const previousValue = selectedValue || select.value || "all";
    const currentEra = getCurrentPatchEra(eras);
    const options = [
        { value: "all", label: "All patches" },
        { value: "current", label: currentEra ? `Current: ${currentEra.label}` : "Current patch" },
        { value: "last2", label: "Last 2 patches" },
        ...(eras ?? []).map(era => ({ value: `era:${era.id}`, label: era.label }))
    ];
    const nextValue = options.some(option => option.value === previousValue)
        ? previousValue
        : "all";

    select.innerHTML = options
        .map(option => `<option value="${escapeHtml(option.value)}" ${option.value === nextValue ? "selected" : ""}>${escapeHtml(option.label)}</option>`)
        .join("");
    select.value = nextValue;
}

function getSelectedAttributeFilterValues(selector) {
    const container = document.querySelector(selector);
    if (!container) {
        return [];
    }

    return normalizeKeyFilterValues(Array.from(container.querySelectorAll("input[type='checkbox']:checked"))
        .map(input => input.value));
}

function updateAttributeFilterSelectionUi(selector) {
    const container = document.querySelector(selector);
    if (!container) {
        return;
    }

    container.querySelectorAll(".attribute-filter-option").forEach(option => {
        const input = option.querySelector("input[type='checkbox']");
        option.classList.toggle("is-active", Boolean(input?.checked));
    });
    updateAttributeFilterSummary(selector);
}

function clearSelectedAttributeFilterValues(selector) {
    const container = document.querySelector(selector);
    if (!container) {
        return;
    }

    container.querySelectorAll("input[type='checkbox']").forEach(input => {
        input.checked = false;
    });
    updateAttributeFilterSelectionUi(selector);
}

function getAttributeFilterSummarySelector(selector) {
    if (selector.includes("fight-browser")) {
        return "#fight-browser-attribute-summary";
    }

    if (selector.includes("analysis")) {
        return "#analysis-attribute-summary";
    }

    return "";
}

function buildAttributeFilterSummaryText(values) {
    const labels = normalizeKeyFilterValues(values);
    if (labels.length === 0) {
        return "Any attribute";
    }

    if (labels.length === 1) {
        return "1 selected";
    }

    return `${labels.length} selected`;
}

function updateAttributeFilterSummary(selector, values = null) {
    const summarySelector = getAttributeFilterSummarySelector(selector);
    const summary = summarySelector ? document.querySelector(summarySelector) : null;
    if (!summary) {
        return;
    }

    summary.textContent = buildAttributeFilterSummaryText(values ?? getSelectedAttributeFilterValues(selector));
}

function renderAttributeFilterBox(selector, definitions, selectedValues) {
    const container = document.querySelector(selector);
    if (!container) {
        return;
    }

    const selected = new Set(normalizeKeyFilterValues(selectedValues));
    const grouped = new Map();
    (definitions ?? []).forEach(definition => {
        if (!definition?.key || !definition?.label) {
            return;
        }

        const group = definition.group || "Attributes";
        if (!grouped.has(group)) {
            grouped.set(group, []);
        }

        grouped.get(group).push(definition);
    });

    if (grouped.size === 0) {
        container.innerHTML = `<p class="class-filter-empty">No fight attributes are available yet.</p>`;
        updateAttributeFilterSummary(selector, []);
        return;
    }

    container.innerHTML = Array.from(grouped.entries())
        .map(([group, items]) => `
            <div class="attribute-filter-group">
                <strong>${escapeHtml(group)}</strong>
                <div class="attribute-filter-options">
                    ${items.map(item => {
                        const inputId = `${selector.slice(1)}-${item.key}`;
                        return `
                            <label class="attribute-filter-option ${selected.has(item.key) ? "is-active" : ""}" for="${escapeHtml(inputId)}">
                                <input id="${escapeHtml(inputId)}" type="checkbox" value="${escapeHtml(item.key)}" ${selected.has(item.key) ? "checked" : ""}>
                                <span>${escapeHtml(item.label)}</span>
                            </label>
                        `;
                    }).join("")}
                </div>
            </div>
        `)
        .join("");
    updateAttributeFilterSelectionUi(selector);
}

function buildAttributePills(attributes, limit = 4) {
    const retained = (attributes ?? []).filter(attribute => attribute?.label);
    if (retained.length === 0) {
        return `<span class="table-inline-note">-</span>`;
    }

    const visible = retained.slice(0, limit);
    const hiddenCount = Math.max(0, retained.length - visible.length);
    return `
        <div class="attribute-pill-list">
            ${visible.map(attribute => `<span class="attribute-pill" title="${escapeHtml(attribute.detail ?? attribute.label)}">${escapeHtml(attribute.label)}</span>`).join("")}
            ${hiddenCount > 0 ? `<span class="attribute-pill attribute-pill-muted">+${escapeHtml(String(hiddenCount))}</span>` : ""}
        </div>
    `;
}

function matchesPatchScope(fight, patchScope, eras) {
    const normalized = String(patchScope || "all").trim();
    if (!normalized || normalized === "all") {
        return true;
    }

    const fightEraId = fight?.patchEra?.id;
    if (!fightEraId) {
        return false;
    }

    if (normalized === "current") {
        return stringEqualsIgnoreCase(fightEraId, getCurrentPatchEra(eras)?.id);
    }

    if (normalized === "last2") {
        return getLastPatchEraIds(eras, 2).some(eraId => stringEqualsIgnoreCase(fightEraId, eraId));
    }

    if (normalized.startsWith("era:")) {
        return stringEqualsIgnoreCase(fightEraId, normalized.slice(4));
    }

    return true;
}

function matchesFightAttributeFilters(fight, requiredAttributeKeys) {
    const required = normalizeKeyFilterValues(requiredAttributeKeys);
    if (required.length === 0) {
        return true;
    }

    const fightKeys = (fight?.attributes ?? []).map(attribute => attribute.key);
    return required.every(requiredKey =>
        fightKeys.some(fightKey => stringEqualsIgnoreCase(fightKey, requiredKey)));
}

function buildFightPlayerClassLabel(player) {
    return String(player?.eliteSpec ?? player?.profession ?? "").trim();
}

function getClassFilterEmptySummary(selector) {
    return selector.includes("lacks")
        ? "None excluded"
        : "Any class";
}

function buildClassFilterSummaryText(selector, values) {
    const labels = normalizeClassFilterLabels(values);
    if (labels.length === 0) {
        return getClassFilterEmptySummary(selector);
    }

    if (labels.length <= 2) {
        return labels.join(", ");
    }

    return `${labels[0]}, ${labels[1]} +${labels.length - 2} more`;
}

function updateClassFilterGroupSummary(selector, values = null) {
    const summary = document.querySelector(`${selector}-summary`);
    if (!summary) {
        return;
    }

    const selectedValues = values ?? getSelectedClassFilterValues(selector);
    summary.textContent = buildClassFilterSummaryText(selector, selectedValues);
}

function getSelectedClassFilterValues(selector) {
    const container = document.querySelector(selector);
    if (!container) {
        return [];
    }

    return normalizeClassFilterLabels(Array.from(container.querySelectorAll("input[type='checkbox']:checked"))
        .map(input => input.value));
}

function setSelectedClassFilterValues(selector, values) {
    const container = document.querySelector(selector);
    if (!container) {
        return;
    }

    const selectedValues = new Set(normalizeClassFilterLabels(values)
        .map(label => label.toLocaleLowerCase()));

    container.querySelectorAll("input[type='checkbox']").forEach(input => {
        input.checked = selectedValues.has(String(input.value ?? "").trim().toLocaleLowerCase());
    });

    updateClassFilterGroupSummary(selector, values);
}

function clearSelectedClassFilterValues(selector) {
    setSelectedClassFilterValues(selector, []);
}

function getFightSideClassLabels(fight, sideKey) {
    const fightIndex = fight?.fightIndex ?? {};
    const side = stringEqualsIgnoreCase(sideKey, "enemy")
        ? fightIndex.enemySide
        : fightIndex.squadSide;
    const retainedLabels = [...new Set((side?.classes ?? [])
        .map(entry => String(entry?.classLabel ?? "").trim())
        .filter(Boolean))]
        .sort((left, right) => left.localeCompare(right, undefined, { sensitivity: "base" }));

    if (retainedLabels.length > 0) {
        return { labels: retainedLabels, hasData: true };
    }

    if (!stringEqualsIgnoreCase(sideKey, "enemy")) {
        const fallbackLabels = [...new Set((fightIndex.players ?? [])
            .map(buildFightPlayerClassLabel)
            .filter(Boolean))]
            .sort((left, right) => left.localeCompare(right, undefined, { sensitivity: "base" }));

        if (fallbackLabels.length > 0) {
            return { labels: fallbackLabels, hasData: true };
        }
    }

    return { labels: [], hasData: false };
}

function collectClassOptionsFromFights(fights) {
    return [...new Set((fights ?? [])
        .flatMap(fight => [
            ...getFightSideClassLabels(fight, "squad").labels,
            ...getFightSideClassLabels(fight, "enemy").labels
        ])
        .filter(Boolean))]
        .sort((left, right) => left.localeCompare(right, undefined, { sensitivity: "base" }));
}

function renderClassFilterGroup(selector, options, selectedValues) {
    const container = document.querySelector(selector);
    if (!container) {
        return;
    }

    const normalizedOptions = normalizeClassFilterLabels(options);
    const selected = new Set(normalizeClassFilterLabels(selectedValues)
        .map(label => label.toLocaleLowerCase()));

    if (normalizedOptions.length === 0) {
        container.innerHTML = `<p class="class-filter-empty">No retained class data is available yet.</p>`;
        updateClassFilterGroupSummary(selector, ["No class data"]);
        return;
    }

    container.innerHTML = normalizedOptions
        .map((option, index) => `
            <label class="class-filter-option" for="${escapeHtml(`${selector.slice(1)}-${index}`)}">
                <input id="${escapeHtml(`${selector.slice(1)}-${index}`)}" type="checkbox" value="${escapeHtml(option)}" ${selected.has(option.toLocaleLowerCase()) ? "checked" : ""}>
                <span>${escapeHtml(option)}</span>
            </label>
        `)
        .join("");

    updateClassFilterGroupSummary(selector, selectedValues);
}

function getFightBrowserClassFiltersFromUi() {
    return {
        squadIncludeClasses: getSelectedClassFilterValues("#fight-browser-squad-has"),
        squadExcludeClasses: getSelectedClassFilterValues("#fight-browser-squad-lacks"),
        enemyIncludeClasses: getSelectedClassFilterValues("#fight-browser-enemy-has"),
        enemyExcludeClasses: getSelectedClassFilterValues("#fight-browser-enemy-lacks")
    };
}

function renderFightBrowserClassFilters(options, selectedFilters) {
    renderClassFilterGroup("#fight-browser-squad-has", options, selectedFilters?.squadIncludeClasses ?? []);
    renderClassFilterGroup("#fight-browser-squad-lacks", options, selectedFilters?.squadExcludeClasses ?? []);
    renderClassFilterGroup("#fight-browser-enemy-has", options, selectedFilters?.enemyIncludeClasses ?? []);
    renderClassFilterGroup("#fight-browser-enemy-lacks", options, selectedFilters?.enemyExcludeClasses ?? []);
}

function formatDateInputValue(value) {
    const parsed = value instanceof Date ? value : new Date(value);
    if (Number.isNaN(parsed.getTime())) {
        return "";
    }

    const year = parsed.getFullYear();
    const month = String(parsed.getMonth() + 1).padStart(2, "0");
    const day = String(parsed.getDate()).padStart(2, "0");
    return `${year}-${month}-${day}`;
}

function getFightLocalDateString(fight) {
    const rawValue = fight.fightIndex?.timeStartStandard ?? fight.fightIndex?.timeStart ?? null;
    return rawValue ? formatDateInputValue(rawValue) : "";
}

function renderFightBrowserFilterOptions(fights, preserveSelection = true) {
    const commanderSelect = document.querySelector("#fight-browser-commander");
    const startDateInput = document.querySelector("#fight-browser-start-date");
    const endDateInput = document.querySelector("#fight-browser-end-date");

    const previousCommander = preserveSelection ? commanderSelect.value : "";
    const previousStartDate = preserveSelection ? startDateInput.value : "";
    const previousEndDate = preserveSelection ? endDateInput.value : "";

    const commanders = [...new Set(fights
        .flatMap(fight => fight.fightIndex?.commanderDisplayNames ?? [])
        .filter(Boolean))]
        .sort((left, right) => left.localeCompare(right, undefined, { sensitivity: "base" }));

    commanderSelect.innerHTML = [
        `<option value="">All commanders</option>`,
        ...commanders.map(commander => `
            <option value="${escapeHtml(commander)}" ${stringEqualsIgnoreCase(commander, previousCommander) ? "selected" : ""}>${escapeHtml(commander)}</option>
        `)
    ].join("");

    const fightDates = fights
        .map(getFightLocalDateString)
        .filter(Boolean)
        .sort((left, right) => left.localeCompare(right));
    const minFightDate = fightDates[0] ?? "";
    const maxFightDate = fightDates[fightDates.length - 1] ?? "";

    startDateInput.min = minFightDate;
    startDateInput.max = maxFightDate;
    endDateInput.min = minFightDate;
    endDateInput.max = maxFightDate;

    startDateInput.value = previousStartDate;
    endDateInput.value = previousEndDate;
}

function renderAnalysisClassFilters(options, selectedFilters) {
    renderClassFilterGroup("#analysis-squad-has", options, selectedFilters?.squadIncludeClasses ?? []);
    renderClassFilterGroup("#analysis-squad-lacks", options, selectedFilters?.squadExcludeClasses ?? []);
    renderClassFilterGroup("#analysis-enemy-has", options, selectedFilters?.enemyIncludeClasses ?? []);
    renderClassFilterGroup("#analysis-enemy-lacks", options, selectedFilters?.enemyExcludeClasses ?? []);
}

function clearFightBrowserClassFilters() {
    clearSelectedClassFilterValues("#fight-browser-squad-has");
    clearSelectedClassFilterValues("#fight-browser-squad-lacks");
    clearSelectedClassFilterValues("#fight-browser-enemy-has");
    clearSelectedClassFilterValues("#fight-browser-enemy-lacks");
}

function clearAnalysisClassFilters() {
    clearSelectedClassFilterValues("#analysis-squad-has");
    clearSelectedClassFilterValues("#analysis-squad-lacks");
    clearSelectedClassFilterValues("#analysis-enemy-has");
    clearSelectedClassFilterValues("#analysis-enemy-lacks");
}

function collapseAnalysisFilterDrawers() {
    document.querySelectorAll(".analysis-workbench details").forEach(details => {
        details.open = false;
    });
}

function resetAnalysisFiltersToDefaults() {
    document.querySelector("#analysis-commander").value = "";
    document.querySelector("#analysis-start-date").value = "";
    document.querySelector("#analysis-end-date").value = "";
    document.querySelector("#analysis-outcome").value = "all";
    document.querySelector("#analysis-patch-scope").value = "all";
    clearSelectedAttributeFilterValues("#analysis-attribute-filters");
    clearAnalysisClassFilters();
    collapseAnalysisFilterDrawers();
    currentAnalysisSnapshot = null;
}

function buildAnalysisScopeStaticChip(label) {
    return `
        <li>
            <span class="analysis-scope-chip-static">${escapeHtml(label)}</span>
        </li>
    `;
}

function buildAnalysisScopeClearChip(key, label) {
    return `
        <li>
            <button class="analysis-scope-chip" type="button" data-analysis-scope-clear="${escapeHtml(key)}" title="${escapeHtml(`Remove ${label}`)}">
                <span>${escapeHtml(label)}</span>
                <span class="analysis-scope-chip-clear" aria-hidden="true">x</span>
            </button>
        </li>
    `;
}

function buildAnalysisPatchScopeChipLabel(selection) {
    const patchScope = String(selection?.patchScope ?? "all");
    if (!patchScope || patchScope === "all") {
        return null;
    }

    const eraIds = selection?.patchEraIds ?? [];
    if (patchScope === "era" && eraIds.length > 0) {
        return `Patch: ${eraIds.join(", ")}`;
    }

    return `Patch: ${patchScope}`;
}

function buildAnalysisScopeChipListHtml(snapshot) {
    const selection = snapshot?.selection ?? {};
    const chips = [
        buildAnalysisScopeStaticChip(`${snapshot.scope.filteredFightCount} fights`),
        buildAnalysisScopeStaticChip(`${snapshot.scope.winCount}-${snapshot.scope.lossCount}-${snapshot.scope.drawCount} record`)
    ];
    const removableChips = [];

    if (selection.commander) {
        removableChips.push(buildAnalysisScopeClearChip("commander", `Commander: ${selection.commander}`));
    }

    if (selection.startDate) {
        removableChips.push(buildAnalysisScopeClearChip("startDate", `Start: ${selection.startDate}`));
    }

    if (selection.endDate) {
        removableChips.push(buildAnalysisScopeClearChip("endDate", `End: ${selection.endDate}`));
    }

    if (selection.outcomeCode && selection.outcomeCode !== "all") {
        removableChips.push(buildAnalysisScopeClearChip("outcome", `Outcome: ${selection.outcomeCode}`));
    }

    const patchLabel = buildAnalysisPatchScopeChipLabel(selection);
    if (patchLabel) {
        removableChips.push(buildAnalysisScopeClearChip("patch", patchLabel));
    }

    if ((selection.fightAttributeKeys?.length ?? 0) > 0) {
        removableChips.push(buildAnalysisScopeClearChip("attributes", `Attributes: ${selection.fightAttributeKeys.join(", ")}`));
    }

    if ((selection.squadIncludeClasses?.length ?? 0) > 0) {
        removableChips.push(buildAnalysisScopeClearChip("squadInclude", `Our side has: ${joinClassFilterLabels(selection.squadIncludeClasses)}`));
    }

    if ((selection.squadExcludeClasses?.length ?? 0) > 0) {
        removableChips.push(buildAnalysisScopeClearChip("squadExclude", `Our side lacks: ${joinClassFilterLabels(selection.squadExcludeClasses)}`));
    }

    if ((selection.enemyIncludeClasses?.length ?? 0) > 0) {
        removableChips.push(buildAnalysisScopeClearChip("enemyInclude", `Enemy has: ${joinClassFilterLabels(selection.enemyIncludeClasses)}`));
    }

    if ((selection.enemyExcludeClasses?.length ?? 0) > 0) {
        removableChips.push(buildAnalysisScopeClearChip("enemyExclude", `Enemy lacks: ${joinClassFilterLabels(selection.enemyExcludeClasses)}`));
    }

    return [
        ...chips,
        ...(removableChips.length > 0 ? removableChips : [buildAnalysisScopeStaticChip("All optional filters")])
    ].join("");
}

function clearAnalysisScopeFilter(key) {
    switch (key) {
        case "commander":
            document.querySelector("#analysis-commander").value = "";
            break;
        case "startDate":
            document.querySelector("#analysis-start-date").value = "";
            break;
        case "endDate":
            document.querySelector("#analysis-end-date").value = "";
            break;
        case "outcome":
            document.querySelector("#analysis-outcome").value = "all";
            break;
        case "patch":
            document.querySelector("#analysis-patch-scope").value = "all";
            break;
        case "attributes":
            clearSelectedAttributeFilterValues("#analysis-attribute-filters");
            break;
        case "squadInclude":
            clearSelectedClassFilterValues("#analysis-squad-has");
            break;
        case "squadExclude":
            clearSelectedClassFilterValues("#analysis-squad-lacks");
            break;
        case "enemyInclude":
            clearSelectedClassFilterValues("#analysis-enemy-has");
            break;
        case "enemyExclude":
            clearSelectedClassFilterValues("#analysis-enemy-lacks");
            break;
        default:
            return false;
    }

    currentAnalysisSnapshot = null;
    return true;
}

function matchesFightSideClassFilters(fight, sideKey, requiredClasses, excludedClasses) {
    if ((requiredClasses?.length ?? 0) === 0 && (excludedClasses?.length ?? 0) === 0) {
        return true;
    }

    const { labels, hasData } = getFightSideClassLabels(fight, sideKey);
    if (!hasData) {
        return false;
    }

    if ((requiredClasses ?? []).some(required =>
        !labels.some(label => stringEqualsIgnoreCase(label, required)))) {
        return false;
    }

    if ((excludedClasses ?? []).some(excluded =>
        labels.some(label => stringEqualsIgnoreCase(label, excluded)))) {
        return false;
    }

    return true;
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

function renderPatchMetadata(metadata) {
    currentPatchMetadata = metadata ?? currentPatchMetadata;
    const summary = document.querySelector("#patch-metadata-summary");
    const textarea = document.querySelector("#patch-metadata-json");
    if (!summary || !textarea || !currentPatchMetadata) {
        return;
    }

    const patchEras = currentPatchMetadata.patchEras ?? [];
    const patchImpacts = currentPatchMetadata.patchImpacts ?? [];
    const currentEra = getCurrentPatchEra(patchEras);
    summary.textContent = `${patchEras.length} patch eras, ${patchImpacts.length} perceived impact notes${currentEra ? `, current: ${currentEra.label}` : ""}.`;
    textarea.value = JSON.stringify(currentPatchMetadata, null, 2);
}

function renderPatchMetadataStatus(message, success = true) {
    const status = document.querySelector("#patch-metadata-status");
    if (!status) {
        return;
    }

    status.classList.toggle("import-status-error", !success);
    status.textContent = message;
}

function renderWorkspace(snapshot) {
    document.querySelector("#mode-pill").textContent = snapshot.application.mode;
    renderPatchMetadata(snapshot.patchMetadata);
    const pendingDirectory = snapshot.workspace.pendingDirectoryPath ?? "Not configured";
    const archiveDirectory = snapshot.workspace.archiveLogDirectoryPath ?? "Not configured";
    document.querySelector("#configured-log-directory-input").value = pendingDirectory;
    document.querySelector("#archive-log-directory-input").value = archiveDirectory;
    const manageSummary = snapshot.manageActivity && (snapshot.manageActivity.parseRunning || snapshot.manageActivity.uploadRunning)
        ? ` ${escapeHtml(snapshot.manageActivity.summary)}`
        : "";
    document.querySelector("#batch-note").innerHTML = snapshot.workspace.parserCliDetected
        ? `Ready to call the EI CLI at <code>${escapeHtml(snapshot.workspace.parserCliPath)}</code>. Pending queue: <code>${escapeHtml(pendingDirectory)}</code>. Archived logs: <code>${escapeHtml(archiveDirectory)}</code>.${manageSummary}`
        : `${escapeHtml(snapshot.workspace.notes)}${manageSummary}`;

    syncManageControls();
}

function getAnalysisFiltersFromUi() {
    return {
        commander: document.querySelector("#analysis-commander").value || "",
        startDate: document.querySelector("#analysis-start-date").value || "",
        endDate: document.querySelector("#analysis-end-date").value || "",
        outcome: document.querySelector("#analysis-outcome").value || "all",
        patchScope: document.querySelector("#analysis-patch-scope").value || "all",
        fightAttributes: getSelectedAttributeFilterValues("#analysis-attribute-filters"),
        squadIncludeClasses: getSelectedClassFilterValues("#analysis-squad-has"),
        squadExcludeClasses: getSelectedClassFilterValues("#analysis-squad-lacks"),
        enemyIncludeClasses: getSelectedClassFilterValues("#analysis-enemy-has"),
        enemyExcludeClasses: getSelectedClassFilterValues("#analysis-enemy-lacks")
    };
}

function renderAnalysisFilterOptions(snapshot, preserveSelection = true) {
    const commanderSelect = document.querySelector("#analysis-commander");
    const startDateInput = document.querySelector("#analysis-start-date");
    const endDateInput = document.querySelector("#analysis-end-date");
    const outcomeSelect = document.querySelector("#analysis-outcome");
    const patchScopeSelect = document.querySelector("#analysis-patch-scope");

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
    renderPatchScopeOptions(
        "#analysis-patch-scope",
        snapshot.options?.patchEras ?? [],
        snapshot.selection?.patchScope === "era" && (snapshot.selection?.patchEraIds?.length ?? 0) === 1
            ? `era:${snapshot.selection.patchEraIds[0]}`
            : snapshot.selection?.patchScope ?? patchScopeSelect?.value ?? "all");
    renderAttributeFilterBox(
        "#analysis-attribute-filters",
        snapshot.options?.fightAttributes ?? [],
        snapshot.selection?.fightAttributeKeys ?? getSelectedAttributeFilterValues("#analysis-attribute-filters"));
    renderAnalysisClassFilters(snapshot.options?.classOptions ?? [], {
        squadIncludeClasses: snapshot.selection?.squadIncludeClasses ?? [],
        squadExcludeClasses: snapshot.selection?.squadExcludeClasses ?? [],
        enemyIncludeClasses: snapshot.selection?.enemyIncludeClasses ?? [],
        enemyExcludeClasses: snapshot.selection?.enemyExcludeClasses ?? []
    });
}

function buildAnalysisOverviewStandardCard(card) {
    return `
        <article class="analysis-card">
            <strong>${escapeHtml(card.title)}</strong>
            <div class="analysis-card-value">${escapeHtml(card.value)}</div>
            <div class="table-inline-note">${escapeHtml(card.detail)}</div>
            ${card.lines?.length
                ? `<div class="table-stack">${card.lines.map(line => `<span class="table-inline-note">${escapeHtml(line)}</span>`).join("")}</div>`
                : ""}
        </article>
    `;
}

function formatAveragePerFilteredFight(total, filteredFightCount, maximumFractionDigits = 0) {
    const count = Number(filteredFightCount ?? 0);
    if (count <= 0) {
        return "n/a";
    }

    return `${formatNumber(Number(total ?? 0) / count, maximumFractionDigits)}/fight`;
}

function buildAnalysisAvailabilityLine(availableFightCount, filteredFightCount) {
    const available = Number(availableFightCount ?? 0);
    const filtered = Number(filteredFightCount ?? 0);
    return available === filtered
        ? "Available across all filtered fights."
        : `Available in ${formatNumber(available)} of ${formatNumber(filtered)} filtered fights.`;
}

function formatMitigationEffectCounts(effectCounts, limit = 4) {
    if (!Array.isArray(effectCounts) || effectCounts.length === 0) {
        return "No effect detail retained.";
    }

    const visible = effectCounts
        .filter(effect => effect && effect.name)
        .slice(0, limit)
        .map(effect => `${effect.name} (${formatNumber(effect.count)})`);
    const remaining = effectCounts.length - visible.length;
    return remaining > 0
        ? `${visible.join(", ")} + ${formatNumber(remaining)} more`
        : visible.join(", ");
}

function buildAnalysisMitigationOverviewCard(summary, filteredFightCount) {
    const negatedSummaries = Array.isArray(summary.negatedHitSummaries) ? summary.negatedHitSummaries : [];
    const negatedHitCount = negatedSummaries.reduce((total, entry) => total + Number(entry.negatedHitCount ?? 0), 0);
    const fallbackCount = negatedSummaries.reduce((total, entry) => total + Number(entry.fallbackEstimateCount ?? 0), 0);
    const negatedHitDamage = negatedSummaries.reduce((total, entry) => total + Number(entry.estimatedPreventedDamage ?? 0), 0);
    const savedCaseNegatedDamage = Number(summary.totalEstimatedNegatedDamage ?? 0);
    const hasNegatedHitSummary = negatedSummaries.length > 0;
    const displayedNegatedDamage = hasNegatedHitSummary ? negatedHitDamage : savedCaseNegatedDamage;
    const totalPrevention = Number(summary.totalBarrierAbsorbed ?? 0)
        + Number(summary.totalEstimatedDamageReduction ?? 0)
        + displayedNegatedDamage
        + Number(summary.totalPetMinionAbsorption ?? 0);
    const availabilityDetail = summary.availableFightCount === filteredFightCount
        ? "Aggregated across all filtered fights."
        : `Available in ${formatNumber(summary.availableFightCount)} of ${formatNumber(filteredFightCount)} filtered fights.`;
    const headerLines = [
        availabilityDetail,
        summary.totalDamageToSquad > 0
            ? `${formatNumber(summary.totalDamageToSquad, 0)} player-targeted damage | ${formatNumber(summary.totalHealthDamageToSquad, 0)} health-only after barrier`
            : null,
        summary.totalIncomingDamage > 0 || summary.totalIncomingHealing > 0
            ? `${formatNumber(summary.totalIncomingDamage, 0)} incoming damage and ${formatNumber(summary.totalIncomingHealing, 0)} incoming healing inside saved-player samples`
            : null,
        summary.hasBarrierCoverageWarnings
            ? "Barrier coverage was incomplete in at least one sampled fight."
            : null
    ].filter(Boolean);

    const rows = [
        {
            label: "Total prevention",
            value: formatNumber(totalPrevention, 0),
            meta: `${formatNumber(summary.totalSaves)} saves`,
            notes: "Full-fight barrier, negated hits, pet/minion absorption, plus saved-case damage reduction."
        },
        {
            label: "Barrier absorbed",
            value: formatNumber(summary.totalBarrierAbsorbed, 0),
            meta: `${formatNumber(summary.totalBarrierSaves)} barrier saves`,
            notes: summary.totalDamageToSquad > 0
                ? `${formatPercent(summary.totalBarrierAbsorbed * 100 / summary.totalDamageToSquad)} of player-targeted damage`
                : "Barrier absorbed on squad players."
        },
        {
            label: "Saved-case damage reduction",
            value: formatNumber(summary.totalEstimatedDamageReduction, 0),
            meta: `${formatNumber(summary.totalDamageReductionSaves)} reduction saves`,
            notes: "Estimated prevented damage from reduction-style effects inside saved-player windows."
        },
        {
            label: hasNegatedHitSummary ? "Negated hits" : "Saved-case negation",
            value: formatNumber(displayedNegatedDamage, 0),
            meta: hasNegatedHitSummary
                ? `${formatNumber(negatedHitCount)} hits`
                : `${formatNumber(summary.totalNegatedDamageSaves)} negation saves`,
            notes: fallbackCount > 0
                ? `${formatNumber(fallbackCount)} fallback negation estimates were used.`
                : hasNegatedHitSummary
                    ? "All negated hits used tracked skill-based estimates."
                    : "Estimated negated damage inside saved-player windows."
        },
        ...negatedSummaries.map(entry => ({
            label: entry.label,
            value: formatNumber(entry.estimatedPreventedDamage, 0),
            meta: `${formatNumber(entry.negatedHitCount)} hits`,
            notes: `${formatNumber(entry.fallbackEstimateCount)} fallbacks | Effects: ${formatMitigationEffectCounts(entry.contributingEffects)}`,
            isSubrow: true
        })),
        {
            label: "Pet / minion absorption",
            value: formatNumber(summary.totalPetMinionAbsorption, 0),
            meta: "Absorption on owned entities",
            notes: summary.totalDamageToSquad + summary.totalPetMinionAbsorption > 0
                ? `${formatPercent(summary.totalPetMinionAbsorption * 100 / (summary.totalDamageToSquad + summary.totalPetMinionAbsorption))} of combined incoming player + pet/minion damage`
                : "Damage absorbed away from squad players."
        },
        {
            label: "Saved cases",
            value: formatNumber(summary.totalSaves),
            meta: `${formatNumber(summary.totalBothSaves)} both | ${formatNumber(summary.totalMultiSourceSaves)} multi-source`,
            notes: summary.averageLowestHealthPercent != null && summary.lowestLowestHealthPercent != null
                ? `${formatPercent(summary.averageLowestHealthPercent)} average lowest health | ${formatPercent(summary.lowestLowestHealthPercent)} lowest observed`
                : "No saved-player low-health detail retained."
        },
        ...(hasNegatedHitSummary && (savedCaseNegatedDamage > 0 || Number(summary.totalNegatedDamageSaves ?? 0) > 0) ? [{
            label: "Saved-case negation",
            value: formatNumber(savedCaseNegatedDamage, 0),
            meta: `${formatNumber(summary.totalNegatedDamageSaves)} negation saves`,
            notes: "Subset of negated damage that occurred inside saved-player windows.",
            isSubrow: true
        }] : [])
    ];

    return `
        <details class="analysis-card analysis-card--wide analysis-card--table analysis-collapsible-card">
            <summary class="analysis-collapsible-summary">
                <span>
                    <strong>Mitigation details</strong>
                    <span class="table-inline-note">${escapeHtml(availabilityDetail)}</span>
                </span>
                <span class="analysis-card-value">${escapeHtml(formatNumber(totalPrevention, 0))}</span>
            </summary>
            <div class="table-stack analysis-collapsible-body">
                ${headerLines.slice(1).map(line => `<span class="table-inline-note">${escapeHtml(line)}</span>`).join("")}
            </div>
            <div class="analysis-mitigation-table-wrap">
                <table class="data-table data-table-compact analysis-mitigation-table">
                    <thead>
                        <tr>
                            <th>Category</th>
                            <th>Total</th>
                            <th>Cases / Hits</th>
                            <th>Notes</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${rows.map(row => `
                            <tr class="${row.isSubrow ? "analysis-mitigation-subrow" : ""}">
                                <td>${escapeHtml(row.label)}</td>
                                <td>${escapeHtml(row.value)}</td>
                                <td>${escapeHtml(row.meta)}</td>
                                <td>${escapeHtml(row.notes)}</td>
                            </tr>
                        `).join("")}
                    </tbody>
                </table>
            </div>
        </details>
    `;
}

function buildAnalysisOverviewCards(snapshot) {
    const overview = snapshot.overview ?? {};
    const obliterateSummary = overview.obliterateSummary ?? null;
    const mitigationSummary = overview.mitigationSummary ?? null;
    const barrierOvercap = mitigationSummary?.barrierOvercap ?? null;
    const reflects = mitigationSummary?.reflects ?? null;
    const filteredFightCount = Number(snapshot.scope?.filteredFightCount ?? 0);
    const cards = [
        {
            title: "Average overall",
            value: overview.averageOverallScore != null
                ? `${formatNumber(overview.averageOverallScore, 1)}${overview.averageOverallGrade ? ` / ${overview.averageOverallGrade}` : ""}`
                : "n/a",
            detail: "Execution score across the filtered fights."
        },
        {
            title: "Context delta",
            value: overview.averageContextDelta != null
                ? formatSignedNumber(overview.averageContextDelta, 1)
                : "n/a",
            detail: overview.contextDeltaDetail ?? "Actual execution score minus expected score for similar fights.",
            lines: overview.averageExpectedScore != null
                ? [
                    `Expected ${formatNumber(overview.averageExpectedScore, 1)}`,
                    `${overview.contextDeltaConfidenceLabel ?? "Low"} confidence | ${formatNumber(overview.contextDeltaSampleCount ?? 0)} baselines`
                ]
                : []
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
            title: "Average support",
            value: overview.averageSupportScore != null ? formatNumber(overview.averageSupportScore, 1) : "n/a",
            detail: "Healing, cleanses, boons, prevention, and saved-player mitigation."
        },
        ...(barrierOvercap ? [{
            title: "Barrier overcap",
            value: formatAveragePerFilteredFight(barrierOvercap.estimatedOvercap, filteredFightCount),
            detail: "Estimated barrier waste per filtered fight.",
            lines: [
                `${formatNumber(barrierOvercap.estimatedOvercap, 0)} total overcap | ${formatPercent(barrierOvercap.overcapPercentOfEvaluated)} of evaluated barrier`,
                `${formatNumber(barrierOvercap.overcapApplicationGroups)} overcap groups from ${formatNumber(barrierOvercap.evaluatedApplicationGroups)} evaluated applications`,
                buildAnalysisAvailabilityLine(barrierOvercap.availableFightCount, filteredFightCount)
            ]
        }] : []),
        ...(reflects?.squadToEnemy ? [{
            title: "Squad reflects",
            value: formatAveragePerFilteredFight(reflects.squadToEnemy.landedDamage, filteredFightCount),
            detail: "Landed return damage onto enemy per filtered fight.",
            lines: [
                `${formatNumber(reflects.squadToEnemy.landedDamage, 0)} total landed | ${formatNumber(reflects.squadToEnemy.estimatedMitigatedDamage, 0)} mitigated estimate`,
                `${formatNumber(reflects.squadToEnemy.reflectedProjectiles)} detected | ${formatNumber(reflects.squadToEnemy.landedHits)} landed hits | ${formatNumber(reflects.squadToEnemy.downEvents)} downs | ${formatNumber(reflects.squadToEnemy.killEvents)} kills`,
                buildAnalysisAvailabilityLine(reflects.availableFightCount, filteredFightCount)
            ]
        }] : []),
        ...(reflects?.enemyToSquad ? [{
            title: "Enemy reflects",
            value: formatAveragePerFilteredFight(reflects.enemyToSquad.landedDamage, filteredFightCount),
            detail: "Landed reflected damage back onto squad per filtered fight.",
            lines: [
                `${formatNumber(reflects.enemyToSquad.landedDamage, 0)} total landed | ${formatNumber(reflects.enemyToSquad.estimatedMitigatedDamage, 0)} enemy mitigation estimate`,
                `${formatNumber(reflects.enemyToSquad.reflectedProjectiles)} detected | ${formatNumber(reflects.enemyToSquad.landedHits)} landed hits | ${formatNumber(reflects.enemyToSquad.downEvents)} downs | ${formatNumber(reflects.enemyToSquad.killEvents)} kills`,
                buildAnalysisAvailabilityLine(reflects.availableFightCount, filteredFightCount)
            ]
        }] : []),
        {
            title: "Average sizes",
            value: `${formatNumber(overview.averageSquadSize, 1)} vs ${formatNumber(overview.averageEnemySize, 1)}`,
            detail: "Average squad and enemy player counts."
        }
    ];

    if (SHOW_ANALYSIS_OBLITERATE_CARD && obliterateSummary) {
        const availabilityDetail = obliterateSummary.availableFightCount === filteredFightCount
            ? "Aggregated across all filtered fights."
            : `Available in ${formatNumber(obliterateSummary.availableFightCount)} of ${formatNumber(filteredFightCount)} filtered fights.`;

        cards.push({
            title: "Obliterate",
            value: formatPercent(obliterateSummary.fightsWithObliteratePercent),
            detail: availabilityDetail,
            lines: [
                `${formatNumber(obliterateSummary.fightsWithObliterateCount)} of ${formatNumber(obliterateSummary.availableFightCount)} fights with at least one Obliterate hit`,
                `${formatNumber(obliterateSummary.totalHitCount)} total hits | ${formatNumber(obliterateSummary.totalBarrierRemovedHitCount)} removed barrier`,
                obliterateSummary.barrierRemovedRatePercent != null
                    ? `${formatPercent(obliterateSummary.barrierRemovedRatePercent)} of Obliterate hits removed barrier`
                    : "No Obliterate hits in the filtered fights."
            ]
        });
    }

    return cards.map(card => typeof card === "string" ? card : buildAnalysisOverviewStandardCard(card)).join("");
}

function formatAnalysisDifferenceValue(row, value) {
    if (value == null) {
        return "n/a";
    }

    switch (row.unit) {
        case "%":
            return formatPercent(value);
        case "seconds":
            return formatSeconds(value);
        case "players":
            return `${formatNumber(value, 1)} players`;
        case "per fight":
            return `${formatNumber(value, 1)}/fight`;
        case "score":
            return formatNumber(value, 1);
        default:
            return formatNumber(value, 1);
    }
}

function formatAnalysisDifferenceDelta(row) {
    if (row.delta == null) {
        return "n/a";
    }

    const suffix = row.unit === "%"
        ? "%"
        : row.unit === "seconds"
            ? "s"
            : row.unit === "players"
                ? " players"
                : row.unit === "per fight"
                    ? "/fight"
                    : "";
    return `${formatSignedNumber(row.delta, 1)}${suffix}`;
}

function getAnalysisDifferenceDirectionClass(row) {
    const delta = Number(row.delta ?? 0);
    if (Math.abs(delta) < 0.05) {
        return "is-flat";
    }

    return delta > 0 ? "is-up" : "is-down";
}

function clampScorePercent(value) {
    const numeric = Number(value);
    if (!Number.isFinite(numeric)) {
        return 0;
    }

    return Math.max(0, Math.min(100, numeric));
}

function buildAnalysisDifferenceTopSignalCard(row) {
    return `
        <article class="analysis-card analysis-delta-card">
            <strong>${escapeHtml(`${row.group}: ${row.label}`)}</strong>
            <div class="analysis-delta-direction ${getAnalysisDifferenceDirectionClass(row)}">${escapeHtml(row.directionLabel ?? "Even")}</div>
            <div class="analysis-delta-metric">${escapeHtml(formatAnalysisDifferenceDelta(row))}</div>
            <div class="table-inline-note">${escapeHtml(row.detail ?? "Compared across wins and losses.")}</div>
            <div class="analysis-delta-pair">
                <span>Wins ${escapeHtml(formatAnalysisDifferenceValue(row, row.winValue))}</span>
                <span>Losses ${escapeHtml(formatAnalysisDifferenceValue(row, row.lossValue))}</span>
                <span>${escapeHtml(row.confidenceLabel ?? "Low")} confidence</span>
            </div>
        </article>
    `;
}

function buildAnalysisDifferenceTableRows(rows, emptyMessage) {
    if (!Array.isArray(rows) || rows.length === 0) {
        return `<tr><td colspan="5">${escapeHtml(emptyMessage)}</td></tr>`;
    }

    return rows.map(row => `
        <tr>
            <td>
                <strong>${escapeHtml(row.label)}</strong>
                <span class="table-inline-note">${escapeHtml(row.detail ?? "")}</span>
            </td>
            <td>
                <strong>${escapeHtml(formatAnalysisDifferenceValue(row, row.winValue))}</strong>
                <span class="table-inline-note">${escapeHtml(`${formatNumber(row.winSampleCount ?? 0)} samples`)}</span>
            </td>
            <td>
                <strong>${escapeHtml(formatAnalysisDifferenceValue(row, row.lossValue))}</strong>
                <span class="table-inline-note">${escapeHtml(`${formatNumber(row.lossSampleCount ?? 0)} samples`)}</span>
            </td>
            <td>
                <span class="analysis-difference-delta ${getAnalysisDifferenceDirectionClass(row)}">${escapeHtml(formatAnalysisDifferenceDelta(row))}</span>
                <span class="table-inline-note">${escapeHtml(row.directionLabel ?? "Even")}</span>
            </td>
            <td>${escapeHtml(row.confidenceLabel ?? "Low")}</td>
        </tr>
    `).join("");
}

function formatDifferenceDelta(value, suffix = "") {
    return `${formatSignedNumber(value, 1)}${suffix}`;
}

function buildAnalysisClassDifferenceMetric(winLabel, lossLabel, deltaLabel, directionClass) {
    return `
        <div class="analysis-class-difference-metric">
            <span>Wins <strong>${escapeHtml(winLabel)}</strong></span>
            <span>Losses <strong>${escapeHtml(lossLabel)}</strong></span>
            <span class="analysis-difference-delta ${directionClass}">${escapeHtml(deltaLabel)}</span>
        </div>
    `;
}

function buildAnalysisClassDifferenceTableRows(rows, emptyMessage) {
    if (!Array.isArray(rows) || rows.length === 0) {
        return `<tr><td colspan="5">${escapeHtml(emptyMessage)}</td></tr>`;
    }

    return rows.map(row => {
        const resultDirection = getAnalysisDifferenceDirectionClass({ delta: row.resultDelta });
        const countDirection = getAnalysisDifferenceDirectionClass({ delta: row.countDeltaWhenPresent });
        const coverageDirection = getAnalysisDifferenceDirectionClass({ delta: row.coverageDelta });
        return `
            <tr>
                <td>
                    <strong>${escapeHtml(row.classLabel)}</strong>
                    <span class="table-inline-note">${escapeHtml(`${formatNumber(row.presentFightCount ?? 0)} present fights`)}</span>
                </td>
                <td>
                    ${buildAnalysisClassDifferenceMetric(
                        `${formatPercent(row.winWhenPresentPercent)} (${formatNumber(row.presentWinCount ?? 0)} fights)`,
                        `${formatPercent(row.lossWhenPresentPercent)} (${formatNumber(row.presentLossCount ?? 0)} fights)`,
                        formatDifferenceDelta(row.resultDelta, " pts"),
                        resultDirection)}
                </td>
                <td>
                    ${buildAnalysisClassDifferenceMetric(
                        `${formatNumber(row.winAverageCountWhenPresent, 1)}/fight`,
                        `${formatNumber(row.lossAverageCountWhenPresent, 1)}/fight`,
                        formatDifferenceDelta(row.countDeltaWhenPresent),
                        countDirection)}
                    <span class="table-inline-note">${escapeHtml(`All present ${formatNumber(row.averageCountWhenPresent, 1)}/fight`)}</span>
                </td>
                <td>
                    ${buildAnalysisClassDifferenceMetric(
                        `${formatNumber(row.winCoverageScore, 1)} (${formatNumber(row.winCoverageSampleCount ?? 0)} samples)`,
                        `${formatNumber(row.lossCoverageScore, 1)} (${formatNumber(row.lossCoverageSampleCount ?? 0)} samples)`,
                        formatDifferenceDelta(row.coverageDelta),
                        coverageDirection)}
                </td>
                <td>${escapeHtml(row.confidenceLabel ?? "Low")}</td>
            </tr>
        `;
    }).join("");
}

function renderAnalysisDifferences(snapshot) {
    const report = snapshot.winLossDifferences ?? null;
    if (!report) {
        document.querySelector("#analysis-differences-summary").textContent = "No win/loss difference report is available for this analysis snapshot.";
        setInnerHtml("#analysis-differences-top-signals", "");
        ["score", "lane", "attribute", "boon", "class", "enemy"].forEach(key => {
            setInnerHtml(`#analysis-differences-${key}-body`, `<tr><td colspan="5">No difference data available.</td></tr>`);
        });
        return;
    }

    document.querySelector("#analysis-differences-summary").textContent =
        `${report.summary} Wins ${formatNumber(report.winFightCount)} | Losses ${formatNumber(report.lossFightCount)} | ${report.confidenceLabel} confidence.`;
    setInnerHtml(
        "#analysis-differences-top-signals",
        (report.topSignals ?? []).length > 0
            ? report.topSignals.map(buildAnalysisDifferenceTopSignalCard).join("")
            : `<article class="analysis-card"><strong>No separators yet</strong><div class="table-inline-note">${escapeHtml(report.summary)}</div></article>`);
    setInnerHtml("#analysis-differences-score-body", buildAnalysisDifferenceTableRows(report.scoreDifferences, "No score differences available."));
    setInnerHtml("#analysis-differences-lane-body", buildAnalysisDifferenceTableRows(report.laneDifferences, "No lane differences available."));
    setInnerHtml("#analysis-differences-attribute-body", buildAnalysisDifferenceTableRows(report.attributeDifferences, "No fight attribute differences available."));
    setInnerHtml("#analysis-differences-boon-body", buildAnalysisDifferenceTableRows(report.boonDifferences, "No boon differences available."));
    setInnerHtml("#analysis-differences-class-body", buildAnalysisClassDifferenceTableRows(report.classDetails ?? [], "No class differences available."));
    setInnerHtml("#analysis-differences-enemy-body", buildAnalysisDifferenceTableRows(report.enemyDifferences, "No enemy differences available."));
}

function buildAnalysisPillarOutcomeRow(row) {
    const winScore = clampScorePercent(row.winValue);
    const lossScore = clampScorePercent(row.lossValue);
    const directionClass = getAnalysisDifferenceDirectionClass(row);
    const detail = row.detail ? `${row.detail} ` : "";

    return `
        <div class="analysis-pillar-outcome-row">
            <div class="analysis-pillar-outcome-label">
                <strong>${escapeHtml(row.label)}</strong>
                <span>${escapeHtml(row.confidenceLabel ?? "Low")} confidence</span>
            </div>
            <div class="analysis-pillar-outcome-bars">
                <div class="analysis-pillar-barline">
                    <span>Wins</span>
                    <div class="analysis-pillar-track" aria-hidden="true">
                        <i class="analysis-pillar-fill analysis-pillar-fill-win" style="width: ${winScore}%"></i>
                    </div>
                    <strong>${escapeHtml(formatAnalysisDifferenceValue(row, row.winValue))}</strong>
                </div>
                <div class="analysis-pillar-barline">
                    <span>Losses</span>
                    <div class="analysis-pillar-track" aria-hidden="true">
                        <i class="analysis-pillar-fill analysis-pillar-fill-loss" style="width: ${lossScore}%"></i>
                    </div>
                    <strong>${escapeHtml(formatAnalysisDifferenceValue(row, row.lossValue))}</strong>
                </div>
            </div>
            <div class="analysis-pillar-outcome-delta">
                <strong class="analysis-difference-delta ${directionClass}">${escapeHtml(formatAnalysisDifferenceDelta(row))}</strong>
                <span>${escapeHtml(`${detail}${formatNumber(row.winSampleCount ?? 0)} wins | ${formatNumber(row.lossSampleCount ?? 0)} losses`)}</span>
            </div>
        </div>
    `;
}

function buildAnalysisPillarOutcomeComparison(snapshot) {
    const report = snapshot.winLossDifferences ?? null;
    const pillarKeys = new Set(["cohesion", "pressure", "downstate", "support"]);
    const rows = (report?.scoreDifferences ?? [])
        .filter(row => pillarKeys.has(String(row.key ?? "").toLowerCase()));
    const hasEnoughResults = Number(report?.winFightCount ?? 0) > 0 && Number(report?.lossFightCount ?? 0) > 0;

    if (!report || rows.length === 0 || !hasEnoughResults) {
        return `
            <article class="analysis-card analysis-pillar-outcome-card">
                <div class="analysis-pillar-outcome-header">
                    <div>
                        <strong>Pillar split by result</strong>
                        <span class="table-inline-note">Needs at least one win and one loss in the current filter.</span>
                    </div>
                </div>
            </article>
        `;
    }

    return `
        <article class="analysis-card analysis-pillar-outcome-card">
            <div class="analysis-pillar-outcome-header">
                <div>
                    <strong>Pillar split by result</strong>
                    <span class="table-inline-note">${escapeHtml(`Wins ${formatNumber(report.winFightCount)} | Losses ${formatNumber(report.lossFightCount)} | ${report.confidenceLabel ?? "Low"} confidence`)}</span>
                </div>
                <div class="analysis-pillar-outcome-legend" aria-hidden="true">
                    <span><i class="analysis-pillar-legend-win"></i>Wins</span>
                    <span><i class="analysis-pillar-legend-loss"></i>Losses</span>
                </div>
            </div>
            <div class="analysis-pillar-outcome-grid">
                ${rows.map(buildAnalysisPillarOutcomeRow).join("")}
            </div>
        </article>
    `;
}

function renderAnalysisPillarOutcomeComparison(snapshot) {
    setInnerHtml("#analysis-pillar-outcome-card", buildAnalysisPillarOutcomeComparison(snapshot));
}

function renderAnalysisMitigation(snapshot) {
    const mitigationSummary = snapshot.overview?.mitigationSummary ?? null;
    if (!mitigationSummary) {
        setInnerHtml("#analysis-mitigation-card", "");
        return;
    }

    const filteredFightCount = Number(snapshot.scope?.filteredFightCount ?? 0);
    setInnerHtml("#analysis-mitigation-card", buildAnalysisMitigationOverviewCard(mitigationSummary, filteredFightCount));
}

function renderAnalysisCharts(snapshot) {
    const trends = snapshot.trends ?? [];
    const aggregatedPoints = buildAggregatedTrendPoints(trends, analysisTrendMode);
    const dateMarkers = buildAnalysisDateMarkers(aggregatedPoints, getPatchErasFromSource(snapshot));
    const showPoints = analysisTrendMode !== "fight" || aggregatedPoints.length <= 24;

    document.querySelector("#analysis-trend-summary").textContent =
        buildAnalysisTrendSummary(trends, aggregatedPoints, analysisTrendMode, analysisTrendSmoothingWindow);

    const comparisonCards = ANALYSIS_TREND_METRICS
        .map(metric => buildAnalysisTrendDeltaCard(metric, aggregatedPoints, analysisTrendMode));
    setInnerHtml("#analysis-trend-delta-grid", comparisonCards.join(""));

    const cards = ANALYSIS_TREND_METRICS.map(metric => {
        const values = aggregatedPoints.map(point => point?.[metric.key] ?? null);
        const averageValue = snapshot.overview?.[metric.averageKey];
        const chartRange = resolveAnalysisChartRange(metric, values);
        const detail = analysisTrendMode === "fight"
            ? `${metric.detail} ${trends.length} fights in date order.`
            : `${metric.detail} ${aggregatedPoints.length} ${aggregatedPoints.length === 1 ? ANALYSIS_TREND_MODE_OPTIONS[analysisTrendMode].unitSingular : ANALYSIS_TREND_MODE_OPTIONS[analysisTrendMode].unitPlural} from ${trends.length} fights.`;

        return buildAnalysisChartCard(
            metric.title,
            formatAnalysisMetricValue(metric, averageValue, 1),
            values,
            detail,
            analysisTrendSmoothingWindow,
            showPoints,
            chartRange.minValue,
            chartRange.maxValue,
            dateMarkers);
    });

    setInnerHtml("#analysis-chart-grid", cards.join(""));
}

function renderAnalysisBurstTrends(snapshot) {
    const burstPoints = snapshot.burstTrends ?? [];
    const aggregatedPoints = buildAggregatedBurstTrendPoints(burstPoints, analysisTrendMode);
    const dateMarkers = buildAnalysisDateMarkers(aggregatedPoints, getPatchErasFromSource(snapshot));
    const selectedIds = ensureAnalysisBurstComparisonSelection();
    const summary = document.querySelector("#analysis-burst-trend-summary");
    if (summary) {
        summary.textContent = buildAnalysisBurstTrendSummary(burstPoints, aggregatedPoints, analysisTrendMode);
    }

    setInnerHtml("#analysis-burst-comparison-controls", buildAnalysisBurstComparisonControls(selectedIds));
    setInnerHtml("#analysis-burst-comparison-chart", buildAnalysisBurstComparisonChart(aggregatedPoints, dateMarkers, selectedIds));

    const cards = ANALYSIS_BURST_TREND_METRICS
        .map(metric => buildAnalysisBurstTrendCard(metric, aggregatedPoints, dateMarkers))
        .join("");
    setInnerHtml("#analysis-burst-chart-grid", cards);
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
        case "fightImpact":
            return Number(player.averageFightImpactScore ?? 0);
        case "corrupts":
            return Number(player.averageCorruptsPerFight ?? 0);
        case "performance":
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
        case "fightCoverage":
            return Number(classRow.averageFightCoverageScore ?? 0);
        case "corrupts":
            return Number(classRow.averageCorruptsPerFight ?? 0);
        case "performance":
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
        case "fightImpact":
            return Number(player.averageFightImpactScore ?? 0);
        case "corrupts":
            return Number(player.averageCorruptsPerFight ?? 0);
        case "performance":
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

function getAnalysisEnemySortValue(row, sortKey) {
    switch (sortKey) {
        case "class":
            return String(row.classLabel ?? "").toLowerCase();
        case "fights":
            return Number(row.fightCount ?? 0);
        case "threat":
            return Number(row.threatScore ?? Number.NEGATIVE_INFINITY);
        case "avg-dps":
            return Number(row.averageDps ?? Number.NEGATIVE_INFINITY);
        case "best-dps":
            return Number(row.bestDps ?? Number.NEGATIVE_INFINITY);
        case "avg-strips":
            return Number(row.averageStripsPerMinute ?? Number.NEGATIVE_INFINITY);
        case "best-strips":
            return Number(row.bestStripsPerMinute ?? Number.NEGATIVE_INFINITY);
        case "damage-bursts":
            return Number(row.damageBurstTopCount ?? 0);
        case "strip-bursts":
            return Number(row.stripBurstTopCount ?? 0);
        case "total":
        default:
            return Number(row.totalCount ?? 0);
    }
}

function getDefaultAnalysisEnemySortDirection(sortKey) {
    return sortKey === "class" ? "asc" : "desc";
}

function updateAnalysisEnemySortHeaders() {
    document.querySelectorAll("[data-analysis-enemy-sort]").forEach(button => {
        const isActive = button.dataset.analysisEnemySort === analysisEnemySortState.key;
        button.classList.toggle("is-active", isActive);
        button.dataset.sortDirection = isActive ? analysisEnemySortState.direction : "";
        button.setAttribute("aria-sort", isActive ? (analysisEnemySortState.direction === "asc" ? "ascending" : "descending") : "none");
    });
}

function setAnalysisEnemySort(sortKey) {
    if (!sortKey) {
        return;
    }

    if (analysisEnemySortState.key === sortKey) {
        analysisEnemySortState = {
            key: sortKey,
            direction: analysisEnemySortState.direction === "asc" ? "desc" : "asc"
        };
    } else {
        analysisEnemySortState = {
            key: sortKey,
            direction: getDefaultAnalysisEnemySortDirection(sortKey)
        };
    }

    if (currentAnalysisSnapshot) {
        renderAnalysisEnemies(currentAnalysisSnapshot);
    } else {
        updateAnalysisEnemySortHeaders();
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

function normalizeAnalysisPlayerLaneSummary(lane, character = null) {
    return {
        ...lane,
        characterName: lane.characterName ?? character?.characterName ?? "",
        classLabel: lane.classLabel ?? character?.classLabel ?? "",
        characterFightCount: Number(lane.characterFightCount ?? character?.fightCount ?? 0),
        characterTotalFightCountAll: Number(lane.characterTotalFightCountAll ?? character?.totalFightCountAll ?? character?.fightCount ?? 0),
        characterWinRatePercent: Number(lane.characterWinRatePercent ?? character?.winRatePercent ?? 0)
    };
}

function getAnalysisPlayerLaneSummaries(player) {
    const directSummaries = (player?.laneSummaries ?? [])
        .map(lane => normalizeAnalysisPlayerLaneSummary(lane));
    if (directSummaries.length > 0) {
        return directSummaries;
    }

    return (player?.characters ?? [])
        .filter(character => Number(character.totalFightCountAll ?? character.fightCount ?? 0) >= 10)
        .flatMap(character => (character.laneContributions ?? [])
            .map(lane => normalizeAnalysisPlayerLaneSummary(lane, character)));
}

function getAnalysisPlayerCharacterLaneSummaryGroups(player) {
    const groups = new Map();
    for (const lane of getAnalysisPlayerLaneSummaries(player)) {
        const characterName = String(lane.characterName ?? "").trim();
        const classLabel = String(lane.classLabel ?? "").trim();
        if (!characterName || Number(lane.characterTotalFightCountAll ?? lane.characterFightCount ?? 0) < 10) {
            continue;
        }

        const key = `${characterName.toLowerCase()}\u001f${classLabel.toLowerCase()}`;
        if (!groups.has(key)) {
            groups.set(key, {
                character: {
                    characterName,
                    classLabel,
                    fightCount: Number(lane.characterFightCount ?? 0),
                    totalFightCountAll: Number(lane.characterTotalFightCountAll ?? lane.characterFightCount ?? 0),
                    winRatePercent: Number(lane.characterWinRatePercent ?? 0)
                },
                laneContributions: []
            });
        }

        groups.get(key).laneContributions.push(lane);
    }

    return Array.from(groups.values());
}

function getAnalysisPlayerLaneOptions(snapshot) {
    const laneMap = new Map();

    (snapshot.topPlayers ?? []).forEach(player => {
        getAnalysisPlayerLaneSummaries(player).forEach(lane => {
            const laneKey = String(lane.laneKey ?? "").trim();
            const laneLabel = String(lane.laneLabel ?? "").trim();
            if (!laneKey || !laneLabel || laneMap.has(laneKey)) {
                return;
            }

            laneMap.set(laneKey, laneLabel);
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

    const matches = getAnalysisPlayerLaneSummaries(player)
        .filter(lane => stringEqualsIgnoreCase(lane.laneKey, laneKey))
        .map(lane => ({
            character: {
                characterName: lane.characterName,
                classLabel: lane.classLabel,
                fightCount: Number(lane.characterFightCount ?? 0),
                totalFightCountAll: Number(lane.characterTotalFightCountAll ?? lane.characterFightCount ?? 0),
                winRatePercent: Number(lane.characterWinRatePercent ?? 0)
            },
            lane
        }))
        .sort((left, right) => Number(right.lane.overallStrengthPercent ?? 0) - Number(left.lane.overallStrengthPercent ?? 0)
            || Number(right.character.totalFightCountAll ?? right.character.fightCount ?? 0) - Number(left.character.totalFightCountAll ?? left.character.fightCount ?? 0)
            || String(left.character.characterName ?? "").localeCompare(String(right.character.characterName ?? ""), undefined, { sensitivity: "base" }));

    return matches[0] ?? null;
}

function getAnalysisPlayerLaneAppearanceTotal(player, laneKey) {
    if (!laneKey || stringEqualsIgnoreCase(laneKey, "all")) {
        return Number(player?.totalFightCountAll ?? player?.fightCount ?? 0);
    }

    return getAnalysisPlayerLaneSummaries(player)
        .filter(lane => stringEqualsIgnoreCase(lane.laneKey, laneKey))
        .reduce((sum, lane) => sum + Number(lane.totalSamplesAll ?? lane.samples ?? 0), 0);
}

function getAnalysisPlayerCombinedLaneAppearanceTotal(player, selectedLaneRows) {
    if (!selectedLaneRows?.length) {
        return 0;
    }

    const selectedKeys = new Set(selectedLaneRows.map(lane => normalizeAnalysisLaneOrderToken(lane.laneKey)));
    return getAnalysisPlayerLaneSummaries(player)
        .filter(lane => selectedKeys.has(normalizeAnalysisLaneOrderToken(lane.laneKey)))
        .reduce((sum, lane) => sum + Number(lane.totalSamplesAll ?? lane.samples ?? 0), 0);
}

function getLaneContributionByKey(collection, laneKey) {
    return (collection ?? []).find(lane => stringEqualsIgnoreCase(lane.laneKey, laneKey)) ?? null;
}

function normalizeAnalysisLaneOrderToken(value) {
    return String(value ?? "").trim().toLocaleLowerCase().replace(/[^a-z0-9]/g, "");
}

function isAnalysisPreventionLane(laneKey) {
    return normalizeAnalysisLaneOrderToken(laneKey) === "prevention";
}

function isAnalysisStripLane(laneKey) {
    const token = normalizeAnalysisLaneOrderToken(laneKey);
    return token === "strip" || token === "boonstrip";
}

function getLaneMetricByKey(lane, metricKey) {
    return (lane?.metrics ?? []).find(metric => stringEqualsIgnoreCase(metric.key, metricKey)) ?? null;
}

function getLaneAverageMetricValue(lane, metricKey) {
    const value = Number(getLaneMetricByKey(lane, metricKey)?.averagePerAppearance);
    return Number.isFinite(value) ? value : null;
}

function getPreventionAverageValue(lane) {
    return getLaneAverageMetricValue(lane, PREVENTION_VALUE_METRIC_KEY);
}

function getLaneStripCorruptStats(lane) {
    const averageCorrupts = Number.isFinite(Number(lane?.averageCorruptsPerAppearance))
        ? Number(lane.averageCorruptsPerAppearance)
        : Number(getLaneAverageMetricValue(lane, STRIP_CORRUPTS_METRIC_KEY) ?? 0);
    const averageStrips = Number(getLaneAverageMetricValue(lane, STRIP_TOTAL_METRIC_KEY) ?? 0);
    const stripCorruptPercent = Number.isFinite(Number(lane?.stripCorruptPercent))
        ? Number(lane.stripCorruptPercent)
        : averageStrips > 0
        ? Math.round((averageCorrupts * 1000) / averageStrips) / 10
        : 0;

    return {
        averageCorruptsPerAppearance: averageCorrupts,
        stripCorruptPercent
    };
}

function buildStripCorruptStack(averageCorrupts, corruptPercent, averageLabel = "fight") {
    const corrupts = Number(averageCorrupts ?? 0);
    const percent = Number(corruptPercent ?? 0);
    return `
        <div class="table-stack">
            <strong>${escapeHtml(formatNumber(corrupts, 1))}</strong>
            <span class="table-inline-note">${escapeHtml(`${formatPercent(percent)} of strips | avg/${averageLabel}`)}</span>
        </div>
    `;
}

function buildStripCorruptPercentStack(corruptPercent) {
    const percent = Number(corruptPercent ?? 0);
    return `
        <div class="table-stack">
            <strong>${escapeHtml(formatPercent(percent))}</strong>
            <span class="table-inline-note">of strips</span>
        </div>
    `;
}

function getAnalysisLaneOrderEntry(lane) {
    const tokens = [
        lane?.laneKey,
        lane?.laneLabel,
        lane?.key,
        lane?.label
    ]
        .map(normalizeAnalysisLaneOrderToken)
        .filter(Boolean);

    for (const token of tokens) {
        const match = ANALYSIS_LANE_ORDER_LOOKUP.get(token);
        if (match) {
            return match;
        }
    }

    return null;
}

function getAnalysisLaneDisplayLabel(lane) {
    return getAnalysisLaneOrderEntry(lane)?.label
        ?? lane?.laneLabel
        ?? lane?.label
        ?? lane?.laneKey
        ?? lane?.key
        ?? "Lane";
}

function compareAnalysisLaneDisplayOrder(left, right) {
    const leftOrder = getAnalysisLaneOrderEntry(left)?.index ?? Number.MAX_SAFE_INTEGER;
    const rightOrder = getAnalysisLaneOrderEntry(right)?.index ?? Number.MAX_SAFE_INTEGER;
    return leftOrder - rightOrder
        || getAnalysisLaneDisplayLabel(left).localeCompare(getAnalysisLaneDisplayLabel(right), undefined, { sensitivity: "base" });
}

function getOrderedAnalysisLanes(lanes) {
    return [...(lanes ?? [])].sort(compareAnalysisLaneDisplayOrder);
}

function getDefaultAnalysisLaneKey(availableLanes) {
    const pressureLane = availableLanes.find(lane => getAnalysisLaneOrderEntry(lane)?.key === "pressure");
    return pressureLane?.laneKey ?? availableLanes[0]?.laneKey ?? null;
}

function getSelectedAnalysisLaneRows(snapshot) {
    const availableLanes = getOrderedAnalysisLanes(snapshot?.topLanes ?? []);
    const normalizedSelected = [...new Set((selectedAnalysisLaneKeys ?? [])
        .filter(Boolean)
        .map(key => String(key)))]
        .filter(key => availableLanes.some(lane => stringEqualsIgnoreCase(lane.laneKey, key)));

    if (normalizedSelected.length === 0 && availableLanes.length > 0) {
        const defaultKey = getDefaultAnalysisLaneKey(availableLanes);
        selectedAnalysisLaneKeys = defaultKey ? [defaultKey] : [];
    } else {
        selectedAnalysisLaneKeys = normalizedSelected;
    }

    return availableLanes
        .filter(lane => selectedAnalysisLaneKeys.some(key => stringEqualsIgnoreCase(key, lane.laneKey)));
}

function isAnalysisLaneSelected(snapshot, laneKey) {
    return getSelectedAnalysisLaneRows(snapshot)
        .some(lane => stringEqualsIgnoreCase(lane.laneKey, laneKey));
}

function setSelectedAnalysisLaneKeys(snapshot, laneKeys) {
    const availableLanes = getOrderedAnalysisLanes(snapshot?.topLanes ?? []);
    const nextKeys = [...new Set((laneKeys ?? [])
        .filter(Boolean)
        .map(key => String(key)))]
        .filter(key => availableLanes.some(lane => stringEqualsIgnoreCase(lane.laneKey, key)));

    if (nextKeys.length === 0 && availableLanes.length > 0) {
        const defaultKey = getDefaultAnalysisLaneKey(availableLanes);
        selectedAnalysisLaneKeys = defaultKey ? [defaultKey] : [];
        return;
    }

    selectedAnalysisLaneKeys = nextKeys;
}

function toggleSelectedAnalysisLane(snapshot, laneKey) {
    if (!laneKey) {
        return;
    }

    const currentKeys = getSelectedAnalysisLaneRows(snapshot).map(lane => lane.laneKey);
    const isSelected = currentKeys.some(key => stringEqualsIgnoreCase(key, laneKey));
    if (isSelected) {
        if (currentKeys.length <= 1) {
            return;
        }

        setSelectedAnalysisLaneKeys(
            snapshot,
            currentKeys.filter(key => !stringEqualsIgnoreCase(key, laneKey)));
        return;
    }

    setSelectedAnalysisLaneKeys(snapshot, [...currentKeys, laneKey]);
}

function buildAnalysisLaneSelectionToggle(lane, isActive) {
    const inputId = `analysis-lane-toggle-${lane.laneKey}`;
    return `
        <label class="comp-helper-favorite ${isActive ? "is-active" : ""}" for="${escapeHtml(inputId)}">
            <input
                id="${escapeHtml(inputId)}"
                type="checkbox"
                data-analysis-lane-toggle="${escapeHtml(lane.laneKey)}"
                ${isActive ? "checked" : ""}>
            <span>${escapeHtml(getAnalysisLaneDisplayLabel(lane))}</span>
        </label>
    `;
}

function buildCombinedLaneContribution(selectedLaneRows, laneContributions) {
    if (!selectedLaneRows?.length) {
        return null;
    }

    const selectedEntries = selectedLaneRows.map(selectedLane => {
        const match = getLaneContributionByKey(laneContributions, selectedLane.laneKey);
        return {
            selectedLane,
            lane: match
        };
    });
    const matchedEntries = selectedEntries.filter(entry => entry.lane);
    const divisor = Math.max(1, selectedEntries.length);
    const averageMetric = selector => Math.round((selectedEntries
        .reduce((sum, entry) => sum + Number(entry.lane ? selector(entry.lane) : 0), 0) / divisor) * 10) / 10;
    const strongestMatch = [...matchedEntries]
        .sort((left, right) => Number(right.lane?.overallStrengthPercent ?? 0) - Number(left.lane?.overallStrengthPercent ?? 0))[0];

    return {
        laneKey: selectedLaneRows.map(lane => lane.laneKey).join("+"),
        laneLabel: selectedLaneRows.map(getAnalysisLaneDisplayLabel).join(" + "),
        averageStrengthPercent: averageMetric(lane => lane.averageStrengthPercent),
        averageSharePercent: averageMetric(lane => lane.averageSharePercent),
        overallStrengthPercent: averageMetric(lane => lane.overallStrengthPercent),
        overallSharePercent: averageMetric(lane => lane.overallSharePercent),
        appearanceRatePercent: averageMetric(lane => lane.appearanceRatePercent),
        averageCorruptsPerAppearance: averageMetric(lane => getLaneStripCorruptStats(lane).averageCorruptsPerAppearance),
        stripCorruptPercent: averageMetric(lane => getLaneStripCorruptStats(lane).stripCorruptPercent),
        samples: matchedEntries.reduce((sum, entry) => sum + Number(entry.lane?.samples ?? 0), 0),
        totalSamplesAll: matchedEntries.reduce((sum, entry) => sum + Number(entry.lane?.totalSamplesAll ?? entry.lane?.samples ?? 0), 0),
        matchedLaneCount: matchedEntries.length,
        selectedLaneCount: selectedLaneRows.length,
        coverageRatePercent: Math.round((matchedEntries.length / divisor) * 1000) / 10,
        strongestLaneLabel: strongestMatch?.lane ? getAnalysisLaneDisplayLabel(strongestMatch.lane) : null
    };
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
            note: "Analyst performance score"
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

function getAverageFightImpactDetail(item) {
    const score = Number(item?.averageFightImpactScore ?? 0);
    const samples = Number(item?.fightImpactSampleCount ?? 0);
    if (score <= 0 || samples <= 0) {
        return {
            value: "-",
            note: "Unavailable"
        };
    }

    return {
        value: `${formatNumber(score, 1)}/100`,
        note: `${formatNumber(samples)} fight${samples === 1 ? "" : "s"}`
    };
}

function getAverageFightCoverageDetail(item) {
    const score = Number(item?.averageFightCoverageScore ?? 0);
    const samples = Number(item?.fightCoverageSampleCount ?? 0);
    if (score <= 0 || samples <= 0) {
        return {
            value: "-",
            note: "Unavailable"
        };
    }

    return {
        value: `${formatNumber(score, 1)}/100`,
        note: `${formatNumber(samples)} fight${samples === 1 ? "" : "s"}`
    };
}

function buildFightImpactNote(item) {
    const score = Number(item?.averageFightImpactScore ?? 0);
    const samples = Number(item?.fightImpactSampleCount ?? 0);
    if (score <= 0 || samples <= 0) {
        return "";
    }

    return `Fight Impact ${formatNumber(score, 1)}/100 over ${formatNumber(samples)} fight${samples === 1 ? "" : "s"}`;
}

function buildFightCoverageNote(item) {
    const score = Number(item?.averageFightCoverageScore ?? 0);
    const samples = Number(item?.fightCoverageSampleCount ?? 0);
    if (score <= 0 || samples <= 0) {
        return "";
    }

    return `Fight Coverage ${formatNumber(score, 1)}/100 over ${formatNumber(samples)} fight${samples === 1 ? "" : "s"}`;
}

function buildDemandAdjustedLanePills(lanes, valueKey, labelPrefix) {
    const retained = (lanes ?? [])
        .filter(lane => Number(lane?.[valueKey] ?? 0) > 0)
        .slice(0, 4);
    if (retained.length === 0) {
        return "";
    }

    return `
        <div class="attribute-pill-list">
            ${retained.map(lane => {
                const label = lane.laneLabel ?? lane.laneKey ?? "Lane";
                const value = Number(lane[valueKey] ?? 0);
                const demandWeight = Number(lane.averageDemandWeightPercent ?? 0);
                const title = `${labelPrefix} ${formatNumber(value, 1)} points | ${formatPercent(demandWeight)} demand weight`;
                return `<span class="attribute-pill" title="${escapeHtml(title)}">${escapeHtml(`${label} ${formatNumber(value, 1)}`)}</span>`;
            }).join("")}
        </div>
    `;
}

function shouldIncludeAnalysisPlayerForSelectedLane(player) {
    const laneKey = getSelectedAnalysisPlayerLaneKey();
    if (stringEqualsIgnoreCase(laneKey, "all")) {
        return true;
    }

    return getAnalysisPlayerLaneAppearanceTotal(player, laneKey) >= MINIMUM_LANE_FILTER_APPEARANCES;
}

function getQualifiedLanePlayers(snapshot, laneKey) {
    return (snapshot.topPlayers ?? [])
        .filter(player => Number(player.totalFightCountAll ?? player.fightCount ?? 0) >= MINIMUM_PLAYER_TABLE_FIGHTS)
        .map(player => {
            const match = getBestAnalysisPlayerLaneMatch(player, laneKey);
            const accountLaneAppearances = getAnalysisPlayerLaneAppearanceTotal(player, laneKey);
            if (!match || accountLaneAppearances < MINIMUM_LANE_FILTER_APPEARANCES) {
                return null;
            }

            return {
                account: player.account,
                displayName: player.displayName,
                filteredFightCount: player.fightCount,
                totalFightCount: player.totalFightCountAll ?? player.fightCount,
                characterName: match.character.characterName,
                classLabel: match.character.classLabel,
                lane: {
                    ...match.lane,
                    accountTotalSamplesAll: accountLaneAppearances,
                    matchedLaneCount: 1,
                    selectedLaneCount: 1,
                    coverageRatePercent: 100
                },
                impactScore: Number(match.lane.overallStrengthPercent ?? 0),
                rankingScore: Number(match.lane.overallStrengthPercent ?? 0),
                winRatePercent: Number(match.character.winRatePercent ?? 0),
                characterFightCount: Number(match.character.totalFightCountAll ?? match.character.fightCount ?? 0)
            };
        })
        .filter(Boolean)
        .sort((left, right) => Number(right.rankingScore ?? 0) - Number(left.rankingScore ?? 0)
            || Number(right.lane?.overallSharePercent ?? 0) - Number(left.lane?.overallSharePercent ?? 0)
            || Number(right.lane?.totalSamplesAll ?? right.lane?.samples ?? 0) - Number(left.lane?.totalSamplesAll ?? left.lane?.samples ?? 0)
            || compareFightBrowserValues(String(left.account ?? "").toLowerCase(), String(right.account ?? "").toLowerCase()));
}

function getQualifiedCombinedLanePlayers(snapshot, selectedLaneRows) {
    return (snapshot.topPlayers ?? [])
        .filter(player => Number(player.totalFightCountAll ?? player.fightCount ?? 0) >= MINIMUM_PLAYER_TABLE_FIGHTS)
        .map(player => {
            const accountLaneAppearances = getAnalysisPlayerCombinedLaneAppearanceTotal(player, selectedLaneRows);
            if (accountLaneAppearances < MINIMUM_LANE_FILTER_APPEARANCES) {
                return null;
            }

            const bestCharacter = getAnalysisPlayerCharacterLaneSummaryGroups(player)
                .map(group => {
                    const lane = buildCombinedLaneContribution(selectedLaneRows, group.laneContributions ?? []);
                    if (!lane) {
                        return null;
                    }

                    const rankingScore = Math.round((
                        Number(lane.overallStrengthPercent ?? 0) * 0.70
                        + Number(lane.overallSharePercent ?? 0) * 0.20
                        + Number(lane.coverageRatePercent ?? 0) * 0.10) * 10) / 10;

                    return {
                        character: group.character,
                        lane,
                        rankingScore
                    };
                })
                .filter(Boolean)
                .sort((left, right) => Number(right.rankingScore ?? 0) - Number(left.rankingScore ?? 0)
                    || Number(right.lane?.overallStrengthPercent ?? 0) - Number(left.lane?.overallStrengthPercent ?? 0)
                    || Number(right.lane?.coverageRatePercent ?? 0) - Number(left.lane?.coverageRatePercent ?? 0)
                    || Number(right.lane?.overallSharePercent ?? 0) - Number(left.lane?.overallSharePercent ?? 0)
                    || Number(right.character?.totalFightCountAll ?? right.character?.fightCount ?? 0) - Number(left.character?.totalFightCountAll ?? left.character?.fightCount ?? 0)
                    || compareFightBrowserValues(String(left.character?.characterName ?? "").toLowerCase(), String(right.character?.characterName ?? "").toLowerCase()))[0];

            if (!bestCharacter) {
                return null;
            }

            return {
                account: player.account,
                displayName: player.displayName,
                filteredFightCount: player.fightCount,
                totalFightCount: player.totalFightCountAll ?? player.fightCount,
                characterName: bestCharacter.character.characterName,
                classLabel: bestCharacter.character.classLabel,
                lane: {
                    ...bestCharacter.lane,
                    accountTotalSamplesAll: accountLaneAppearances
                },
                impactScore: Number(bestCharacter.lane.overallStrengthPercent ?? 0),
                rankingScore: Number(bestCharacter.rankingScore ?? 0),
                winRatePercent: Number(bestCharacter.character.winRatePercent ?? 0),
                characterFightCount: Number(bestCharacter.character.totalFightCountAll ?? bestCharacter.character.fightCount ?? 0)
            };
        })
        .filter(Boolean)
        .sort((left, right) => Number(right.rankingScore ?? 0) - Number(left.rankingScore ?? 0)
            || Number(right.lane?.coverageRatePercent ?? 0) - Number(left.lane?.coverageRatePercent ?? 0)
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

            const preventionAverageValue = isAnalysisPreventionLane(laneKey)
                ? getPreventionAverageValue(lane)
                : null;
            const usesPreventionValue = preventionAverageValue !== null;
            const rankingScore = usesPreventionValue
                ? preventionAverageValue
                : Number(lane.overallStrengthPercent ?? 0);
            const corruptStats = getLaneStripCorruptStats(lane);

            return {
                classLabel: classRow.classLabel,
                sampleCount: classRow.sampleCount,
                impactScore: Number(lane.overallStrengthPercent ?? 0),
                rankingScore,
                rankingMetric: usesPreventionValue ? PREVENTION_VALUE_METRIC_KEY : "overallStrengthPercent",
                topPlayerDisplayName: classRow.topPlayerDisplayName,
                lane: {
                    ...lane,
                    ...corruptStats,
                    matchedLaneCount: 1,
                    selectedLaneCount: 1,
                    coverageRatePercent: 100
                }
            };
        })
        .filter(Boolean)
        .sort((left, right) => Number(right.rankingScore ?? 0) - Number(left.rankingScore ?? 0)
            || Number(right.lane?.overallStrengthPercent ?? 0) - Number(left.lane?.overallStrengthPercent ?? 0)
            || Number(right.lane?.overallSharePercent ?? 0) - Number(left.lane?.overallSharePercent ?? 0)
            || Number(right.sampleCount ?? 0) - Number(left.sampleCount ?? 0)
            || compareFightBrowserValues(String(left.classLabel ?? "").toLowerCase(), String(right.classLabel ?? "").toLowerCase()));
}

function getQualifiedCombinedLaneClasses(snapshot, selectedLaneRows) {
    return (snapshot.topClasses ?? [])
        .map(classRow => {
            const lane = buildCombinedLaneContribution(selectedLaneRows, classRow.laneContributions ?? []);
            if (!lane || Number(lane.totalSamplesAll ?? 0) < MINIMUM_LANE_FILTER_APPEARANCES) {
                return null;
            }

            const rankingScore = Math.round((
                Number(lane.overallStrengthPercent ?? 0) * 0.70
                + Number(lane.overallSharePercent ?? 0) * 0.20
                + Number(lane.coverageRatePercent ?? 0) * 0.10) * 10) / 10;

            return {
                classLabel: classRow.classLabel,
                sampleCount: classRow.sampleCount,
                impactScore: Number(lane.overallStrengthPercent ?? 0),
                rankingScore,
                topPlayerDisplayName: classRow.topPlayerDisplayName,
                lane
            };
        })
        .filter(Boolean)
        .sort((left, right) => Number(right.rankingScore ?? 0) - Number(left.rankingScore ?? 0)
            || Number(right.lane?.coverageRatePercent ?? 0) - Number(left.lane?.coverageRatePercent ?? 0)
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

function normalizeCompLaneKey(value) {
    return String(value ?? "")
        .replaceAll(/[^a-z0-9]+/gi, "")
        .toLowerCase();
}

function clampNumeric(value, minValue, maxValue) {
    return Math.max(minValue, Math.min(maxValue, value));
}

function getCompHelperTotalReliability(totalFightCount) {
    if (totalFightCount >= 80) {
        return 1.0;
    }
    if (totalFightCount >= 40) {
        return 0.93;
    }
    return 0.84;
}

function getCompHelperFilteredReliability(filteredFightCount) {
    if (filteredFightCount >= 20) {
        return 1.0;
    }
    if (filteredFightCount >= 10) {
        return 0.93;
    }
    if (filteredFightCount >= 5) {
        return 0.82;
    }
    return 0.65;
}

function getCompHelperPercentileScale(values, percentile = 0.85) {
    const positives = values
        .map(value => Number(value ?? 0))
        .filter(value => Number.isFinite(value) && value > 0)
        .sort((left, right) => left - right);
    if (positives.length === 0) {
        return 1;
    }

    const index = Math.min(positives.length - 1, Math.max(0, Math.ceil(positives.length * percentile) - 1));
    return Math.max(1, positives[index]);
}

function getCompHelperNormalizedPackageValue(value, scale) {
    return clampNumeric((Number(value ?? 0) / Math.max(1, Number(scale ?? 1))) * 100, 0, 100);
}

function buildCompHelperPackageNormalizers(candidates) {
    return {
        healing: getCompHelperPercentileScale(candidates.map(candidate => candidate.packageInputs?.healingPerFight)),
        cleanse: getCompHelperPercentileScale(candidates.map(candidate => candidate.packageInputs?.cleansePerFight)),
        protectionGeneration: getCompHelperPercentileScale(candidates.map(candidate => candidate.packageInputs?.protectionGenerationPerFight)),
        protectionPresence: getCompHelperPercentileScale(candidates.map(candidate => candidate.packageInputs?.protectionPresencePerFight)),
        stabilityGeneration: getCompHelperPercentileScale(candidates.map(candidate => candidate.packageInputs?.stabilityGenerationPerFight)),
        stabilityPresence: getCompHelperPercentileScale(candidates.map(candidate => candidate.packageInputs?.stabilityPresencePerFight)),
        barrier: getCompHelperPercentileScale(candidates.map(candidate => candidate.packageInputs?.barrierPerFight)),
        might: getCompHelperPercentileScale(candidates.map(candidate => candidate.packageInputs?.mightGenerationPerFight)),
        furyGeneration: getCompHelperPercentileScale(candidates.map(candidate => candidate.packageInputs?.furyGenerationPerFight)),
        furyPresence: getCompHelperPercentileScale(candidates.map(candidate => candidate.packageInputs?.furyPresencePerFight)),
        quicknessGeneration: getCompHelperPercentileScale(candidates.map(candidate => candidate.packageInputs?.quicknessGenerationPerFight)),
        quicknessPresence: getCompHelperPercentileScale(candidates.map(candidate => candidate.packageInputs?.quicknessPresencePerFight)),
        resistanceGeneration: getCompHelperPercentileScale(candidates.map(candidate => candidate.packageInputs?.resistanceGenerationPerFight)),
        resistancePresence: getCompHelperPercentileScale(candidates.map(candidate => candidate.packageInputs?.resistancePresencePerFight)),
        regenerationGeneration: getCompHelperPercentileScale(candidates.map(candidate => candidate.packageInputs?.regenerationGenerationPerFight)),
        regenerationPresence: getCompHelperPercentileScale(candidates.map(candidate => candidate.packageInputs?.regenerationPresencePerFight)),
        strip: getCompHelperPercentileScale(candidates.map(candidate => candidate.packageInputs?.stripPerFight)),
        effectiveCrowdControl: getCompHelperPercentileScale(candidates.map(candidate => candidate.packageInputs?.effectiveCrowdControlPerFight))
    };
}

function buildCompHelperPackageScores(packageInputs, laneMap, normalizers) {
    const pressurePackage = clampNumeric(Number(packageInputs?.pressureStrength ?? 0), 0, 100);
    const controlStrength = clampNumeric(Number(packageInputs?.controlStrength ?? 0), 0, 100);
    const stripStrength = clampNumeric(Number(laneMap?.strip?.overallStrengthPercent ?? 0), 0, 100);

    const healing = getCompHelperNormalizedPackageValue(packageInputs?.healingPerFight, normalizers.healing);
    const cleanse = getCompHelperNormalizedPackageValue(packageInputs?.cleansePerFight, normalizers.cleanse);
    const protectionGeneration = getCompHelperNormalizedPackageValue(packageInputs?.protectionGenerationPerFight, normalizers.protectionGeneration);
    const protectionPresence = getCompHelperNormalizedPackageValue(packageInputs?.protectionPresencePerFight, normalizers.protectionPresence);
    const stabilityGeneration = getCompHelperNormalizedPackageValue(packageInputs?.stabilityGenerationPerFight, normalizers.stabilityGeneration);
    const stabilityPresence = getCompHelperNormalizedPackageValue(packageInputs?.stabilityPresencePerFight, normalizers.stabilityPresence);
    const barrier = getCompHelperNormalizedPackageValue(packageInputs?.barrierPerFight, normalizers.barrier);
    const might = getCompHelperNormalizedPackageValue(packageInputs?.mightGenerationPerFight, normalizers.might);
    const furyGeneration = getCompHelperNormalizedPackageValue(packageInputs?.furyGenerationPerFight, normalizers.furyGeneration);
    const furyPresence = getCompHelperNormalizedPackageValue(packageInputs?.furyPresencePerFight, normalizers.furyPresence);
    const quicknessGeneration = getCompHelperNormalizedPackageValue(packageInputs?.quicknessGenerationPerFight, normalizers.quicknessGeneration);
    const quicknessPresence = getCompHelperNormalizedPackageValue(packageInputs?.quicknessPresencePerFight, normalizers.quicknessPresence);
    const resistanceGeneration = getCompHelperNormalizedPackageValue(packageInputs?.resistanceGenerationPerFight, normalizers.resistanceGeneration);
    const resistancePresence = getCompHelperNormalizedPackageValue(packageInputs?.resistancePresencePerFight, normalizers.resistancePresence);
    const regenerationGeneration = getCompHelperNormalizedPackageValue(packageInputs?.regenerationGenerationPerFight, normalizers.regenerationGeneration);
    const regenerationPresence = getCompHelperNormalizedPackageValue(packageInputs?.regenerationPresencePerFight, normalizers.regenerationPresence);
    const stripPerFight = getCompHelperNormalizedPackageValue(packageInputs?.stripPerFight, normalizers.strip);
    const effectiveCrowdControl = getCompHelperNormalizedPackageValue(packageInputs?.effectiveCrowdControlPerFight, normalizers.effectiveCrowdControl);

    return {
        stability: Math.round((stabilityGeneration * 0.65 + stabilityPresence * 0.35) * 10) / 10,
        healing: Math.round(healing * 10) / 10,
        cleanse: Math.round(cleanse * 10) / 10,
        protection: Math.round((protectionGeneration * 0.30 + protectionPresence * 0.70) * 10) / 10,
        "pressure-package": Math.round(pressurePackage * 10) / 10,
        barrier: Math.round(barrier * 10) / 10,
        might: Math.round(might * 10) / 10,
        "strip-package": Math.round((stripPerFight * 0.55 + stripStrength * 0.45) * 10) / 10,
        fury: Math.round((furyGeneration * 0.45 + furyPresence * 0.55) * 10) / 10,
        quickness: Math.round((quicknessGeneration * 0.55 + quicknessPresence * 0.45) * 10) / 10,
        resistance: Math.round((resistanceGeneration * 0.25 + resistancePresence * 0.75) * 10) / 10,
        regeneration: Math.round((regenerationGeneration * 0.25 + regenerationPresence * 0.75) * 10) / 10,
        cc: Math.round((effectiveCrowdControl * 0.55 + controlStrength * 0.45) * 10) / 10
    };
}

function buildCompHelperCandidateId(account, characterName, classLabel) {
    return `${account}::${characterName}::${classLabel}`;
}

function cloneCompHelperTargets(targets) {
    return (targets ?? []).map(target => ({ ...target }));
}

function formatCompHelperConfigNumber(value, maximumFractionDigits = 1) {
    const numeric = Number(value);
    if (!Number.isFinite(numeric)) {
        return "0";
    }

    return numeric.toFixed(maximumFractionDigits).replace(/\.?0+$/, "");
}

function cloneCompHelperConfig(config) {
    return {
        schemaVersion: config?.schemaVersion ?? "1.0",
        updatedAtUtc: config?.updatedAtUtc ?? new Date().toISOString(),
        laneTargets: cloneCompHelperTargets(config?.laneTargets ?? DEFAULT_COMP_HELPER_LANE_TARGETS),
        packageTargets: cloneCompHelperTargets(config?.packageTargets ?? DEFAULT_COMP_HELPER_PACKAGE_TARGETS)
    };
}

function applyCompHelperConfig(config) {
    currentCompHelperConfig = cloneCompHelperConfig(config);
    compHelperLaneTargets = cloneCompHelperTargets(currentCompHelperConfig.laneTargets);
    compHelperPackageTargets = cloneCompHelperTargets(currentCompHelperConfig.packageTargets);
}

function getCompHelperProfileFavorites(profileKey) {
    return COMP_HELPER_PROFILE_FAVORITES[profileKey] ?? COMP_HELPER_PROFILE_FAVORITES.balanced;
}

function getCompHelperCandidateTier() {
    return COMP_HELPER_CANDIDATE_TIER_OPTIONS[compHelperCandidateTierKey]
        ?? COMP_HELPER_CANDIDATE_TIER_OPTIONS.best;
}

function syncCompHelperCandidateTierControl() {
    const select = document.querySelector("#analysis-comp-helper-candidate-tier");
    if (select) {
        select.value = getCompHelperCandidateTier().key;
    }
}

function syncCompHelperProfileControl() {
    const select = document.querySelector("#analysis-comp-helper-profile");
    if (select) {
        select.value = compHelperProfileKey;
    }
}

function applyCompHelperProfile(profileKey) {
    const resolvedProfileKey = COMP_HELPER_PROFILE_FAVORITES[profileKey] ? profileKey : "balanced";
    const profile = getCompHelperProfileFavorites(resolvedProfileKey);
    compHelperProfileKey = resolvedProfileKey;
    compHelperFavoredLaneKeys = [...profile.lanes];
    compHelperFavoredPackageKeys = [...profile.packages];
    syncCompHelperProfileControl();
}

function syncCompHelperProfileFromFavorites() {
    const balanced = getCompHelperProfileFavorites("balanced");
    const offense = getCompHelperProfileFavorites("offense");
    const defense = getCompHelperProfileFavorites("defense");
    const lanesKey = [...compHelperFavoredLaneKeys].sort().join("|");
    const packagesKey = [...compHelperFavoredPackageKeys].sort().join("|");
    const matchesProfile = profile => lanesKey === [...profile.lanes].sort().join("|")
        && packagesKey === [...profile.packages].sort().join("|");

    if (matchesProfile(balanced)) {
        compHelperProfileKey = "balanced";
    } else if (matchesProfile(offense)) {
        compHelperProfileKey = "offense";
    } else if (matchesProfile(defense)) {
        compHelperProfileKey = "defense";
    } else {
        compHelperProfileKey = "custom";
    }

    syncCompHelperProfileControl();
}

function toggleCompHelperFavorite(listKey, valueKey) {
    const source = listKey === "lanes" ? compHelperFavoredLaneKeys : compHelperFavoredPackageKeys;
    const next = source.includes(valueKey)
        ? source.filter(key => key !== valueKey)
        : [...source, valueKey];

    if (listKey === "lanes") {
        compHelperFavoredLaneKeys = next;
    } else {
        compHelperFavoredPackageKeys = next;
    }

    syncCompHelperProfileFromFavorites();
}

function buildCompHelperLaneTargets() {
    const favoredLaneKeySet = new Set(compHelperFavoredLaneKeys);
    return compHelperLaneTargets.map(target => {
        if (!favoredLaneKeySet.has(target.key)) {
            return target;
        }

        return {
            ...target,
            floor: target.target,
            weight: Math.round(target.weight * 1.65 * 100) / 100,
            favored: true
        };
    });
}

function buildCompHelperPackageTargets() {
    const favoredPackageKeySet = new Set(compHelperFavoredPackageKeys);
    return compHelperPackageTargets.map(target => {
        if (!favoredPackageKeySet.has(target.key)) {
            return target;
        }

        return {
            ...target,
            floor: Math.max(target.floor, target.target),
            weight: Math.round(target.weight * 1.65 * 100) / 100,
            favored: true
        };
    });
}

function buildCompHelperFavoriteToggle(target, groupKey, isActive) {
    const inputId = `comp-helper-favorite-${groupKey}-${target.key}`;
    return `
        <label class="comp-helper-favorite ${isActive ? "is-active" : ""}" for="${escapeHtml(inputId)}">
            <input
                id="${escapeHtml(inputId)}"
                type="checkbox"
                data-comp-helper-favorite-group="${escapeHtml(groupKey)}"
                data-comp-helper-favorite-key="${escapeHtml(target.key)}"
                ${isActive ? "checked" : ""}>
            <span>${escapeHtml(target.label)}</span>
        </label>
    `;
}

function getCompHelperCandidateSearchText(candidate) {
    return [
        candidate.account,
        candidate.displayName,
        candidate.characterName,
        candidate.classLabel,
        ...(candidate.classesPlayed ?? []),
        ...(candidate.topLaneLabels ?? [])
    ]
        .filter(Boolean)
        .join(" ")
        .toLowerCase();
}

function buildCompHelperCandidates(snapshot) {
    const candidates = (snapshot.topPlayers ?? [])
        .flatMap(player => (player.characters ?? [])
            .map(character => ({ player, character })))
        .map(({ player, character }) => {
            const totalFightCount = Number(character.totalFightCountAll ?? character.fightCount ?? 0);
            const filteredFightCount = Number(character.fightCount ?? 0);
            if (totalFightCount < COMP_HELPER_MIN_TOTAL_FIGHTS || filteredFightCount < COMP_HELPER_MIN_FILTERED_FIGHTS) {
                return null;
            }

            const totalReliability = getCompHelperTotalReliability(totalFightCount);
            const filteredReliability = getCompHelperFilteredReliability(filteredFightCount);
            const reliability = Math.round(totalReliability * filteredReliability * 1000) / 1000;

            const laneMap = {};
            for (const target of compHelperLaneTargets) {
                laneMap[target.key] = {
                    key: target.key,
                    label: target.label,
                    overallStrengthPercent: 0,
                    overallSharePercent: 0,
                    appearanceRatePercent: 0,
                    totalSamplesAll: 0,
                    samples: 0
                };
            }

            for (const lane of character.laneContributions ?? []) {
                const laneKey = normalizeCompLaneKey(lane.laneKey ?? lane.laneLabel);
                if (!laneKey || !laneMap[laneKey]) {
                    continue;
                }

                laneMap[laneKey] = {
                    key: laneKey,
                    label: lane.laneLabel ?? laneMap[laneKey].label,
                    overallStrengthPercent: Number(lane.overallStrengthPercent ?? 0),
                    overallSharePercent: Number(lane.overallSharePercent ?? 0),
                    appearanceRatePercent: Number(lane.appearanceRatePercent ?? 0),
                    totalSamplesAll: Number(lane.totalSamplesAll ?? lane.samples ?? 0),
                    samples: Number(lane.samples ?? 0)
                };
            }

            const effectiveLaneScores = {};
            for (const target of compHelperLaneTargets) {
                effectiveLaneScores[target.key] = Math.round((laneMap[target.key]?.overallStrengthPercent ?? 0) * reliability * 10) / 10;
            }

            const orderedLanes = Object.values(laneMap)
                .sort((left, right) => Number(right.overallStrengthPercent ?? 0) - Number(left.overallStrengthPercent ?? 0)
                    || Number(right.appearanceRatePercent ?? 0) - Number(left.appearanceRatePercent ?? 0)
                    || compareFightBrowserValues(String(left.label ?? "").toLowerCase(), String(right.label ?? "").toLowerCase()));
            const topLaneLabels = orderedLanes
                .filter(lane => Number(lane.overallStrengthPercent ?? 0) > 0)
                .slice(0, 3)
                .map(lane => lane.label);
            const effectiveLaneValues = Object.values(effectiveLaneScores)
                .map(value => Number(value ?? 0))
                .sort((left, right) => right - left);
            const priorityScore = Math.round((
                (effectiveLaneValues[0] ?? 0) * 1.0
                + (effectiveLaneValues[1] ?? 0) * 0.75
                + (effectiveLaneValues[2] ?? 0) * 0.45
                + Number(character.impactScore ?? 0) * 0.18
                + Number(character.winRatePercent ?? 0) * 0.04) * 10) / 10;

            return {
                id: buildCompHelperCandidateId(player.account, character.characterName, character.classLabel),
                account: player.account,
                displayName: player.displayName,
                characterName: character.characterName,
                classLabel: character.classLabel,
                classesPlayed: character.classesPlayed ?? [],
                filteredFightCount,
                totalFightCountAll: totalFightCount,
                winRatePercent: Number(character.winRatePercent ?? 0),
                impactScore: Number(character.impactScore ?? 0),
                averageWeightedLaneScore: Number(character.averageWeightedLaneScore ?? 0),
                averagePrimaryLaneScore: Number(character.averagePrimaryLaneScore ?? 0),
                primaryLaneLabel: character.primaryLaneLabel ?? topLaneLabels[0] ?? "Unclassified",
                contributionSummary: character.contributionSummary ?? null,
                confidenceLabel: character.confidenceLabel ?? null,
                confidenceDetail: character.confidenceDetail ?? null,
                packageInputs: character.packageInputs ?? null,
                totalReliability,
                filteredReliability,
                reliability,
                laneMap,
                effectiveLaneScores,
                topLaneLabels,
                priorityScore
            };
        })
        .filter(Boolean)
        .sort((left, right) => Number(right.priorityScore ?? 0) - Number(left.priorityScore ?? 0)
            || Number(right.impactScore ?? 0) - Number(left.impactScore ?? 0)
            || Number(right.totalFightCountAll ?? 0) - Number(left.totalFightCountAll ?? 0)
            || compareFightBrowserValues(String(left.account ?? "").toLowerCase(), String(right.account ?? "").toLowerCase()));

    const packageNormalizers = buildCompHelperPackageNormalizers(candidates);
    for (const candidate of candidates) {
        candidate.packageScores = buildCompHelperPackageScores(candidate.packageInputs ?? {}, candidate.laneMap, packageNormalizers);
        const topPackageValues = Object.values(candidate.packageScores ?? {})
            .map(value => Number(value ?? 0))
            .sort((left, right) => right - left);
        const packagePriority = (topPackageValues[0] ?? 0) * 0.8
            + (topPackageValues[1] ?? 0) * 0.45
            + (topPackageValues[2] ?? 0) * 0.2;
        candidate.priorityScore = Math.round((Number(candidate.priorityScore ?? 0) * 0.65 + packagePriority * 0.35) * 10) / 10;
    }

    const orderedCandidates = candidates.sort((left, right) => Number(right.priorityScore ?? 0) - Number(left.priorityScore ?? 0)
        || Number(right.impactScore ?? 0) - Number(left.impactScore ?? 0)
        || Number(right.totalFightCountAll ?? 0) - Number(left.totalFightCountAll ?? 0)
        || compareFightBrowserValues(String(left.account ?? "").toLowerCase(), String(right.account ?? "").toLowerCase()));

    const maxIndex = Math.max(1, orderedCandidates.length - 1);
    orderedCandidates.forEach((candidate, index) => {
        candidate.priorityPercentile = orderedCandidates.length <= 1
            ? 100
            : Math.round((((maxIndex - index) / maxIndex) * 100) * 10) / 10;
    });

    return orderedCandidates;
}

function getCompHelperCandidateSnapshot(snapshot) {
    return {
        ...(snapshot ?? {}),
        topPlayers: currentAnalysisAllPlayerDetails ?? snapshot?.topPlayers ?? []
    };
}

function getCompHelperCandidates(snapshot) {
    const candidates = buildCompHelperCandidates(getCompHelperCandidateSnapshot(snapshot));
    const candidateIds = new Set(candidates.map(candidate => candidate.id));
    lockedCompHelperCandidateIds = lockedCompHelperCandidateIds.filter(id => candidateIds.has(id));
    return candidates;
}

function getLockedCompHelperCandidates(candidates) {
    const lookup = new Map(candidates.map(candidate => [candidate.id, candidate]));
    return lockedCompHelperCandidateIds
        .map(id => lookup.get(id) ?? null)
        .filter(Boolean);
}

function compareCompHelperCandidatesForTier(left, right, targetPercentile) {
    if (targetPercentile >= 100) {
        return Number(right.priorityScore ?? 0) - Number(left.priorityScore ?? 0)
            || Number(right.priorityPercentile ?? 0) - Number(left.priorityPercentile ?? 0)
            || Number(right.impactScore ?? 0) - Number(left.impactScore ?? 0)
            || Number(right.totalFightCountAll ?? 0) - Number(left.totalFightCountAll ?? 0)
            || compareFightBrowserValues(String(left.account ?? "").toLowerCase(), String(right.account ?? "").toLowerCase());
    }

    const leftDistance = Math.abs(Number(left.priorityPercentile ?? 0) - targetPercentile);
    const rightDistance = Math.abs(Number(right.priorityPercentile ?? 0) - targetPercentile);
    return leftDistance - rightDistance
        || Number(right.priorityScore ?? 0) - Number(left.priorityScore ?? 0)
        || Number(right.impactScore ?? 0) - Number(left.impactScore ?? 0)
        || Number(right.totalFightCountAll ?? 0) - Number(left.totalFightCountAll ?? 0)
        || compareFightBrowserValues(String(left.account ?? "").toLowerCase(), String(right.account ?? "").toLowerCase());
}

function getCompHelperTierOrderedCandidates(candidates) {
    const tier = getCompHelperCandidateTier();
    return [...candidates].sort((left, right) =>
        compareCompHelperCandidatesForTier(left, right, Number(tier.targetPercentile ?? 100)));
}

function getCompHelperSearchPoolCandidates(candidates, lockedCandidates) {
    const lockedAccounts = new Set(lockedCandidates.map(candidate => candidate.account));
    const unlockedCandidates = getCompHelperTierOrderedCandidates(candidates)
        .filter(candidate => !lockedAccounts.has(candidate.account));
    return unlockedCandidates.slice(0, COMP_HELPER_SEARCH_POOL_LIMIT);
}

function evaluateCompHelperTeam(members) {
    const laneTargets = buildCompHelperLaneTargets();
    const packageTargets = buildCompHelperPackageTargets();
    const maxCoverageScore = laneTargets.reduce((sum, lane) => sum + lane.weight * 100, 0);
    const maxPackageScore = packageTargets.reduce((sum, pkg) => sum + pkg.weight * 100, 0);
    const laneResults = laneTargets.map(target => {
        const sortedContributors = [...members]
            .map(member => ({
                member,
                effectiveScore: Number(member.effectiveLaneScores?.[target.key] ?? 0),
                sharePercent: Number(member.laneMap?.[target.key]?.overallSharePercent ?? 0)
            }))
            .filter(entry => entry.effectiveScore > 0)
            .sort((left, right) => right.effectiveScore - left.effectiveScore
                || right.sharePercent - left.sharePercent
                || compareFightBrowserValues(String(left.member.id ?? ""), String(right.member.id ?? "")));
        const coverage = sortedContributors.reduce((sum, entry, index) =>
            sum + entry.effectiveScore * (COMP_HELPER_DIMINISHING_WEIGHTS[index] ?? 0), 0);
        const coverageOfTargetPercent = target.target > 0
            ? clampNumeric((coverage / target.target) * 100, 0, 115)
            : 0;
        const coverageOfFloorPercent = target.floor > 0
            ? clampNumeric((coverage / target.floor) * 100, 0, 140)
            : 0;
        const laneValue = Math.min(1.15, target.target > 0 ? coverage / target.target : 0) * 100 * target.weight;
        const deficitPenalty = coverage >= target.floor
            ? 0
            : target.weight * 120 * Math.pow(1 - coverage / target.floor, 2);
        const status = coverage >= target.target
            ? "target"
            : coverage >= target.floor
                ? "floor"
                : "deficit";

        return {
            key: target.key,
            label: target.label,
            floor: target.floor,
            target: target.target,
            weight: target.weight,
            favored: target.favored === true,
            coverage: Math.round(coverage * 10) / 10,
            coverageOfTargetPercent: Math.round(coverageOfTargetPercent * 10) / 10,
            coverageOfFloorPercent: Math.round(coverageOfFloorPercent * 10) / 10,
            status,
            topContributors: sortedContributors.slice(0, 2).map(entry => ({
                account: entry.member.account,
                characterName: entry.member.characterName,
                classLabel: entry.member.classLabel,
                score: Math.round(entry.effectiveScore * 10) / 10
            })),
            laneValue,
            deficitPenalty
        };
    });

    const packageResults = packageTargets.map(target => {
        const sortedContributors = [...members]
            .map(member => ({
                member,
                effectiveScore: Number(member.packageScores?.[target.key] ?? 0)
            }))
            .filter(entry => entry.effectiveScore > 0)
            .sort((left, right) => right.effectiveScore - left.effectiveScore
                || compareFightBrowserValues(String(left.member.id ?? ""), String(right.member.id ?? "")));
        const coverage = sortedContributors.reduce((sum, entry, index) =>
            sum + entry.effectiveScore * (COMP_HELPER_DIMINISHING_WEIGHTS[index] ?? 0), 0);
        const maxCoverageRatio = target.allowOvercap === false ? 1.0 : 1.15;
        const maxCoveragePercent = target.allowOvercap === false ? 100 : 115;
        const coverageOfTargetPercent = target.target > 0
            ? clampNumeric((coverage / target.target) * 100, 0, maxCoveragePercent)
            : 0;
        const coverageOfFloorPercent = target.floor > 0
            ? clampNumeric((coverage / target.floor) * 100, 0, 140)
            : 0;
        const packageValue = Math.min(maxCoverageRatio, target.target > 0 ? coverage / target.target : 0) * 100 * target.weight;
        const deficitPenalty = target.floor <= 0 || coverage >= target.floor
            ? 0
            : target.weight * (target.mandatory ? 160 : 90) * Math.pow(1 - coverage / target.floor, 2);
        const status = coverage >= target.target
            ? "target"
            : target.floor <= 0 || coverage >= target.floor
                ? "floor"
                : "deficit";

        return {
            key: target.key,
            label: target.label,
            floor: target.floor,
            target: target.target,
            weight: target.weight,
            mandatory: target.mandatory,
            allowOvercap: target.allowOvercap !== false,
            favored: target.favored === true,
            coverage: Math.round(coverage * 10) / 10,
            coverageOfTargetPercent: Math.round(coverageOfTargetPercent * 10) / 10,
            coverageOfFloorPercent: Math.round(coverageOfFloorPercent * 10) / 10,
            status,
            topContributors: sortedContributors.slice(0, 2).map(entry => ({
                account: entry.member.account,
                characterName: entry.member.characterName,
                classLabel: entry.member.classLabel,
                score: Math.round(entry.effectiveScore * 10) / 10
            })),
            packageValue,
            deficitPenalty
        };
    });

    const coverageScore = laneResults.reduce((sum, lane) => sum + lane.laneValue, 0);
    const deficitPenaltyScore = laneResults.reduce((sum, lane) => sum + lane.deficitPenalty, 0);
    const packageCoverageScore = packageResults.reduce((sum, pkg) => sum + pkg.packageValue, 0);
    const packagePenaltyScore = packageResults.reduce((sum, pkg) => sum + pkg.deficitPenalty, 0);
    const riskPenalty = members.reduce((sum, member) => sum + ((1 - Number(member.reliability ?? 0.7)) * 5.0), 0);
    const laneNormalizedScore = clampNumeric(
        (coverageScore / Math.max(1, maxCoverageScore)) * 100
        - (deficitPenaltyScore / Math.max(1, maxCoverageScore)) * 100,
        0,
        100);
    const packageNormalizedScore = clampNumeric(
        (packageCoverageScore / Math.max(1, maxPackageScore)) * 100
        - (packagePenaltyScore / Math.max(1, maxPackageScore)) * 100,
        0,
        100);
    const mandatoryPackageDeficitCount = packageResults.filter(pkg => pkg.mandatory && pkg.status === "deficit").length;
    const normalizedScore = clampNumeric(
        laneNormalizedScore * 0.58
        + packageNormalizedScore * 0.42
        - mandatoryPackageDeficitCount * 6
        - riskPenalty,
        0,
        100);
    const strongestLane = [...laneResults]
        .sort((left, right) => right.coverageOfTargetPercent - left.coverageOfTargetPercent
            || right.coverage - left.coverage)[0] ?? null;
    const weakestLane = [...laneResults]
        .sort((left, right) => left.coverageOfFloorPercent - right.coverageOfFloorPercent
            || left.coverage - right.coverage)[0] ?? null;
    const strongestPackage = [...packageResults]
        .sort((left, right) => right.coverageOfTargetPercent - left.coverageOfTargetPercent
            || right.coverage - left.coverage)[0] ?? null;
    const weakestPackage = [...packageResults]
        .sort((left, right) => left.coverageOfFloorPercent - right.coverageOfFloorPercent
            || left.coverage - right.coverage)[0] ?? null;

    return {
        score: Math.round(normalizedScore * 10) / 10,
        laneNormalizedScore: Math.round(laneNormalizedScore * 10) / 10,
        packageNormalizedScore: Math.round(packageNormalizedScore * 10) / 10,
        laneResults,
        packageResults,
        deficitLabels: laneResults.filter(lane => lane.status === "deficit").map(lane => lane.label),
        packageDeficitLabels: packageResults.filter(pkg => pkg.status === "deficit").map(pkg => pkg.label),
        mandatoryPackageDeficitCount,
        strongestLane,
        weakestLane,
        strongestPackage,
        weakestPackage
    };
}

function scoreCompHelperBeamState(members) {
    const evaluation = evaluateCompHelperTeam(members);
    const laneTargets = buildCompHelperLaneTargets();
    const packageTargets = buildCompHelperPackageTargets();
    const totalWeight = laneTargets.reduce((sum, lane) => sum + lane.weight, 0);
    const totalPackageWeight = packageTargets.reduce((sum, pkg) => sum + pkg.weight, 0);
    const floorProgress = evaluation.laneResults.reduce((sum, lane) =>
        sum + Math.min(1, lane.coverage / lane.floor) * lane.weight, 0) / Math.max(1, totalWeight);
    const packageFloorProgress = evaluation.packageResults.reduce((sum, pkg) =>
        sum + Math.min(1, pkg.coverage / Math.max(1, pkg.floor || pkg.target)) * pkg.weight, 0) / Math.max(1, totalPackageWeight);
    const distinctLaneCount = evaluation.laneResults.filter(lane => lane.coverage >= Math.max(10, lane.floor * 0.35)).length;
    const favoredLaneHits = evaluation.laneResults.filter(lane => lane.favored && lane.coverage >= lane.target).length;
    const favoredPackageHits = evaluation.packageResults.filter(pkg => pkg.favored && pkg.coverage >= pkg.target).length;
    return evaluation.score
        + floorProgress * 32
        + packageFloorProgress * 22
        + distinctLaneCount * 1.8
        + favoredLaneHits * 8
        + favoredPackageHits * 8
        + members.length * 1.5;
}

function buildCompHelperTeamKey(members) {
    return [...members]
        .map(member => member.id)
        .sort((left, right) => compareFightBrowserValues(left, right))
        .join("|");
}

function buildCompHelperSearchPool(candidates, lockedCandidates) {
    const pool = getCompHelperSearchPoolCandidates(candidates, lockedCandidates);
    return [...lockedCandidates, ...pool];
}

function selectDiverseCompHelperSuggestions(suggestions) {
    const accepted = [];
    const remaining = suggestions.map(suggestion => ({ ...suggestion, adjustedScore: suggestion.score }));

    while (accepted.length < COMP_HELPER_SUGGESTION_COUNT && remaining.length > 0) {
        remaining.sort((left, right) => Number(right.adjustedScore ?? 0) - Number(left.adjustedScore ?? 0));
        const next = remaining.shift();
        if (!next) {
            break;
        }

        accepted.push(next);
        const nextIds = new Set(next.members.map(member => member.id));
        for (const suggestion of remaining) {
            const overlap = suggestion.members.filter(member => nextIds.has(member.id)).length;
            suggestion.adjustedScore = suggestion.score - Math.max(0, overlap - 2) * 4;
        }
    }

    return accepted;
}

function buildCompHelperSuggestionSummary(suggestion, lockedCandidates) {
    const evaluation = suggestion.evaluation;
    const packageDeficits = evaluation.packageDeficitLabels ?? [];
    if (lockedCandidates.length === 0) {
        const strongest = evaluation.strongestLane?.label ?? "its strongest lanes";
        if (evaluation.deficitLabels.length === 0 && packageDeficits.length === 0) {
            return `Built from scratch to cover all lane floors and mandatory packages, with ${strongest} leading the profile.`;
        }

        const missingBits = [...evaluation.deficitLabels, ...packageDeficits].slice(0, 3).join(", ");
        return `Built from scratch around ${strongest}. Still light on ${missingBits}.`;
    }

    if (suggestion.addedMembers.length === 0) {
        return evaluation.deficitLabels.length === 0 && packageDeficits.length === 0
            ? "Fully locked 5-player group. Lane floors and mandatory packages are currently met."
            : `Fully locked 5-player group. Still light on ${[...evaluation.deficitLabels, ...packageDeficits].slice(0, 3).join(", ")}.`;
    }

    const improvementLabels = suggestion.improvements
        .filter(item => item.delta > 0.1)
        .slice(0, 2)
        .map(item => item.label);
    if (improvementLabels.length === 0) {
        return `Filled around the locked core with ${suggestion.addedMembers.map(member => member.classLabel).join(", ")} while covering package gaps.`;
    }

    return `Filled around the locked core by lifting ${improvementLabels.join(" and ")}.`;
}

function searchCompHelperSuggestions(snapshot, candidates) {
    const lockedCandidates = getLockedCompHelperCandidates(candidates);
    if (lockedCandidates.length > COMP_HELPER_TEAM_SIZE) {
        return { lockedCandidates, suggestions: [], shortage: 0 };
    }

    const searchPool = buildCompHelperSearchPool(candidates, lockedCandidates)
        .filter(candidate => !lockedCandidates.some(locked => locked.id === candidate.id));
    const remainingSlots = COMP_HELPER_TEAM_SIZE - lockedCandidates.length;
    if (remainingSlots <= 0) {
        const evaluation = evaluateCompHelperTeam(lockedCandidates);
        return {
            lockedCandidates,
            suggestions: [{
                title: "Locked group",
                score: evaluation.score,
                members: lockedCandidates,
                addedMembers: [],
                evaluation,
                improvements: evaluation.laneResults.map(lane => ({ key: lane.key, label: lane.label, delta: lane.coverage }))
            }],
            shortage: 0
        };
    }

    const uniqueAccounts = new Set(searchPool.map(candidate => candidate.account));
    if (uniqueAccounts.size < remainingSlots) {
        return { lockedCandidates, suggestions: [], shortage: remainingSlots - uniqueAccounts.size };
    }

    let beam = [{
        members: [...lockedCandidates],
        usedAccounts: new Set(lockedCandidates.map(candidate => candidate.account)),
        beamScore: scoreCompHelperBeamState(lockedCandidates)
    }];

    for (let step = 0; step < remainingSlots; step += 1) {
        const nextBeam = [];
        const seenKeys = new Set();

        for (const state of beam) {
            for (const candidate of searchPool) {
                if (state.usedAccounts.has(candidate.account)) {
                    continue;
                }

                const members = [...state.members, candidate];
                const key = buildCompHelperTeamKey(members);
                if (seenKeys.has(key)) {
                    continue;
                }

                seenKeys.add(key);
                nextBeam.push({
                    members,
                    usedAccounts: new Set([...state.usedAccounts, candidate.account]),
                    beamScore: scoreCompHelperBeamState(members)
                });
            }
        }

        nextBeam.sort((left, right) => Number(right.beamScore ?? 0) - Number(left.beamScore ?? 0));
        beam = nextBeam.slice(0, COMP_HELPER_BEAM_WIDTH);
        if (beam.length === 0) {
            break;
        }
    }

    const lockedEvaluation = evaluateCompHelperTeam(lockedCandidates);
    const suggestions = beam
        .filter(state => state.members.length === COMP_HELPER_TEAM_SIZE)
        .map((state, index) => {
            const evaluation = evaluateCompHelperTeam(state.members);
            const addedMembers = state.members.filter(member => !lockedCandidates.some(locked => locked.id === member.id));
            const improvements = [
                ...evaluation.laneResults
                    .map(lane => {
                        const baseLane = lockedEvaluation.laneResults.find(item => item.key === lane.key);
                        return {
                            key: lane.key,
                            label: lane.label,
                            delta: Math.round((lane.coverage - Number(baseLane?.coverage ?? 0)) * 10) / 10
                        };
                    }),
                ...evaluation.packageResults
                    .map(pkg => {
                        const basePackage = lockedEvaluation.packageResults.find(item => item.key === pkg.key);
                        return {
                            key: `pkg:${pkg.key}`,
                            label: pkg.label,
                            delta: Math.round((pkg.coverage - Number(basePackage?.coverage ?? 0)) * 10) / 10
                        };
                    })
            ]
                .map(item => {
                    return {
                        key: item.key,
                        label: item.label,
                        delta: item.delta
                    };
                })
                .sort((left, right) => right.delta - left.delta);

            return {
                title: lockedCandidates.length === 0 ? `Balanced comp ${index + 1}` : `Locked-core fit ${index + 1}`,
                score: evaluation.score,
                members: state.members,
                addedMembers,
                evaluation,
                improvements
            };
        })
        .sort((left, right) => Number(right.score ?? 0) - Number(left.score ?? 0));

    return {
        lockedCandidates,
        suggestions: selectDiverseCompHelperSuggestions(suggestions),
        shortage: 0
    };
}

function getCompHelperFilteredCandidates(candidates) {
    const orderedCandidates = getCompHelperTierOrderedCandidates(candidates);
    const searchValue = document.querySelector("#analysis-comp-helper-search")?.value.trim().toLowerCase() ?? "";
    if (!searchValue) {
        return orderedCandidates;
    }

    return orderedCandidates.filter(candidate => getCompHelperCandidateSearchText(candidate).includes(searchValue));
}

function buildCompHelperLockPill(candidate) {
    return `
        <div class="comp-helper-lock">
            <div class="comp-helper-lock-copy">
                <strong class="mono">${escapeHtml(candidate.account)}</strong>
                <span class="table-inline-note">${escapeHtml(`${candidate.characterName} / ${candidate.classLabel}`)}</span>
            </div>
            <button type="button" data-comp-helper-unlock="${escapeHtml(candidate.id)}">Remove</button>
        </div>
    `;
}

function buildCompHelperCandidateActionLabel(candidate, lockedCandidates, isLocked) {
    if (isLocked) {
        return "Locked";
    }

    const lockedByAccount = lockedCandidates.find(locked => stringEqualsIgnoreCase(locked.account, candidate.account));
    if (lockedByAccount) {
        return "Replace";
    }

    return "Lock";
}

function buildCompHelperCandidateRow(candidate, lockedCandidates) {
    const isLocked = lockedCandidates.some(locked => locked.id === candidate.id);
    const lockedByOtherAccountCard = lockedCandidates.find(locked => stringEqualsIgnoreCase(locked.account, candidate.account) && locked.id !== candidate.id);
    const disableLock = !isLocked && lockedCandidates.length >= COMP_HELPER_MAX_LOCKED_CARDS && !lockedByOtherAccountCard;
    const actionLabel = buildCompHelperCandidateActionLabel(candidate, lockedCandidates, isLocked);
    const topLaneCopy = candidate.topLaneLabels?.length
        ? candidate.topLaneLabels.join(", ")
        : "Unclassified";

    return `
        <tr>
            <td>
                <button
                    class="comp-helper-candidate-action"
                    type="button"
                    data-comp-helper-toggle="${escapeHtml(candidate.id)}"
                    ${disableLock ? "disabled" : ""}>${escapeHtml(actionLabel)}</button>
            </td>
            <td>
                <div class="table-stack">
                    <strong class="mono">${escapeHtml(candidate.account)}</strong>
                    ${candidate.displayName && !stringEqualsIgnoreCase(candidate.displayName, candidate.account)
                        ? `<span class="table-inline-note">${escapeHtml(`Most-played character: ${candidate.displayName}`)}</span>`
                        : ""}
                </div>
            </td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(`${candidate.characterName} / ${candidate.classLabel}`)}</strong>
                    <span class="table-inline-note">${escapeHtml(topLaneCopy)}</span>
                </div>
            </td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(formatNumber(candidate.filteredFightCount))}</strong>
                    <span class="table-inline-note">Current filter</span>
                </div>
            </td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(formatNumber(candidate.totalFightCountAll))}</strong>
                    <span class="table-inline-note">Imported total</span>
                </div>
            </td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(candidate.primaryLaneLabel ?? "Unclassified")}</strong>
                    <span class="table-inline-note">${escapeHtml(`${formatPercent(candidate.averagePrimaryLaneScore)} primary`)}</span>
                </div>
            </td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(`Fit ${formatNumber(candidate.priorityScore, 1)}`)}</strong>
                    <span class="table-inline-note">${escapeHtml(`Performance ${formatNumber(candidate.impactScore, 1)} | reliability ${formatPercent(candidate.reliability * 100, 0)}`)}</span>
                </div>
            </td>
        </tr>
    `;
}

function getCompHelperTopPackageLabel(member) {
    const ordered = compHelperPackageTargets
        .map(pkg => ({
            label: pkg.label,
            score: Number(member.packageScores?.[pkg.key] ?? 0)
        }))
        .sort((left, right) => right.score - left.score);
    return ordered[0]?.score > 0 ? `${ordered[0].label} ${formatNumber(ordered[0].score, 1)}` : "No package signal";
}

function buildCompHelperMemberCard(member, lockedIds) {
    const memberClasses = ["comp-helper-member"];
    if (lockedIds.has(member.id)) {
        memberClasses.push("is-locked");
    }

    return `
        <article class="${memberClasses.join(" ")}">
            <strong>${escapeHtml(`${member.characterName} / ${member.classLabel}`)}</strong>
            <div class="table-inline-note mono">${escapeHtml(member.account)}</div>
            <div class="comp-helper-member-meta">
                <span>${escapeHtml(lockedIds.has(member.id) ? "Locked" : "Added")}</span>
                <span>${escapeHtml(`${formatNumber(member.filteredFightCount)} filtered`)}</span>
                <span>${escapeHtml(`${formatPercent(member.winRatePercent)} wins`)}</span>
            </div>
            <div class="table-inline-note">${escapeHtml(`Primary lane: ${member.primaryLaneLabel ?? "Unclassified"} | top package: ${getCompHelperTopPackageLabel(member)}`)}</div>
        </article>
    `;
}

function buildCompHelperLaneRow(lane) {
    const rowClass = lane.status === "target"
        ? "is-target"
        : lane.status === "deficit"
            ? "is-deficit"
            : "";
    const contributorCopy = lane.topContributors?.length
        ? lane.topContributors.map(contributor => `${contributor.classLabel} ${formatNumber(contributor.score, 1)}`).join(" | ")
        : "No visible contributors";
    const label = lane.favored ? `${lane.label} *` : lane.label;
    const targetCopy = lane.favored ? "favored target" : "target";

    return `
        <div class="comp-helper-lane-row ${rowClass}">
            <strong>${escapeHtml(label)}</strong>
            <div class="comp-helper-lane-bar">
                <span class="comp-helper-lane-fill" style="width: ${escapeHtml(`${clampNumeric(lane.coverageOfTargetPercent, 0, 115)}%`)}"></span>
            </div>
            <div class="table-stack">
                <strong>${escapeHtml(`${formatPercent(lane.coverageOfTargetPercent)} of ${targetCopy}`)}</strong>
                <span class="table-inline-note">${escapeHtml(`${formatPercent(lane.coverageOfFloorPercent)} of floor | ${contributorCopy}`)}</span>
            </div>
        </div>
    `;
}

function buildCompHelperSuggestionCard(suggestion, lockedCandidates, displayIndex) {
    const lockedIds = new Set(lockedCandidates.map(candidate => candidate.id));
    const summary = buildCompHelperSuggestionSummary(suggestion, lockedCandidates);
    const deficitCopy = suggestion.evaluation.deficitLabels.length === 0
        ? "All v1 lane floors are currently met."
        : `Below floor: ${suggestion.evaluation.deficitLabels.join(", ")}.`;
    const packageDeficitCopy = suggestion.evaluation.packageDeficitLabels.length === 0
        ? "Mandatory packages are currently covered."
        : `Package gaps: ${suggestion.evaluation.packageDeficitLabels.join(", ")}.`;

    return `
        <article class="comp-helper-card">
            <div class="comp-helper-card-header">
                <div>
                    <strong>${escapeHtml(suggestion.title.replace(/\d+$/, String(displayIndex + 1)))}</strong>
                    <p class="workspace-note comp-helper-section-note">${escapeHtml(summary)}</p>
                </div>
                <div class="comp-helper-score">${escapeHtml(formatNumber(suggestion.score, 1))}</div>
            </div>
            <ul class="tag-list">
                <li>${escapeHtml(`Strongest: ${suggestion.evaluation.strongestLane?.label ?? "n/a"}`)}</li>
                <li>${escapeHtml(`Weakest: ${suggestion.evaluation.weakestLane?.label ?? "n/a"}`)}</li>
                <li>${escapeHtml(deficitCopy)}</li>
                <li>${escapeHtml(`Packages ${formatNumber(suggestion.evaluation.packageNormalizedScore, 1)}`)}</li>
                <li>${escapeHtml(`Strongest package: ${suggestion.evaluation.strongestPackage?.label ?? "n/a"}`)}</li>
                <li>${escapeHtml(`Weakest package: ${suggestion.evaluation.weakestPackage?.label ?? "n/a"}`)}</li>
                <li>${escapeHtml(packageDeficitCopy)}</li>
            </ul>
            <div class="comp-helper-member-grid">
                ${suggestion.members.map(member => buildCompHelperMemberCard(member, lockedIds)).join("")}
            </div>
            <p class="table-inline-note">Lane coverage</p>
            <div class="comp-helper-lane-grid">
                ${suggestion.evaluation.laneResults.map(buildCompHelperLaneRow).join("")}
            </div>
            <p class="table-inline-note">Package coverage</p>
            <div class="comp-helper-lane-grid">
                ${suggestion.evaluation.packageResults.map(buildCompHelperLaneRow).join("")}
            </div>
        </article>
    `;
}

function renderCompHelperConfigEditor() {
    const laneBody = document.querySelector("#analysis-comp-helper-config-lanes-body");
    const packageBody = document.querySelector("#analysis-comp-helper-config-packages-body");
    const updated = document.querySelector("#analysis-comp-helper-config-updated");
    if (!laneBody || !packageBody || !currentCompHelperConfig) {
        return;
    }

    const buildNumberInput = (kind, key, field, value, step = "1") => `
        <input
            class="compact-number-input"
            type="number"
            min="0"
            step="${escapeHtml(step)}"
            value="${escapeHtml(formatCompHelperConfigNumber(value, step === "0.01" ? 2 : 1))}"
            data-comp-helper-config-kind="${escapeHtml(kind)}"
            data-comp-helper-config-key="${escapeHtml(key)}"
            data-comp-helper-config-field="${escapeHtml(field)}">
    `;
    const buildTextInput = (kind, key, field, value) => `
        <input
            class="compact-text-input"
            type="text"
            value="${escapeHtml(value)}"
            data-comp-helper-config-kind="${escapeHtml(kind)}"
            data-comp-helper-config-key="${escapeHtml(key)}"
            data-comp-helper-config-field="${escapeHtml(field)}">
    `;
    const buildCheckbox = (kind, key, field, isChecked) => `
        <input
            type="checkbox"
            ${isChecked ? "checked" : ""}
            data-comp-helper-config-kind="${escapeHtml(kind)}"
            data-comp-helper-config-key="${escapeHtml(key)}"
            data-comp-helper-config-field="${escapeHtml(field)}">
    `;

    laneBody.innerHTML = (currentCompHelperConfig.laneTargets ?? [])
        .map(target => `
            <tr>
                <td><code>${escapeHtml(target.key)}</code></td>
                <td>${buildTextInput("lane", target.key, "label", target.label)}</td>
                <td>${buildNumberInput("lane", target.key, "floor", target.floor)}</td>
                <td>${buildNumberInput("lane", target.key, "target", target.target)}</td>
                <td>${buildNumberInput("lane", target.key, "weight", target.weight, "0.01")}</td>
            </tr>
        `)
        .join("");
    packageBody.innerHTML = (currentCompHelperConfig.packageTargets ?? [])
        .map(target => `
            <tr>
                <td><code>${escapeHtml(target.key)}</code></td>
                <td>${buildTextInput("package", target.key, "label", target.label)}</td>
                <td>${buildNumberInput("package", target.key, "floor", target.floor)}</td>
                <td>${buildNumberInput("package", target.key, "target", target.target)}</td>
                <td>${buildNumberInput("package", target.key, "weight", target.weight, "0.01")}</td>
                <td>${buildCheckbox("package", target.key, "mandatory", target.mandatory === true)}</td>
                <td>${buildCheckbox("package", target.key, "allowOvercap", target.allowOvercap !== false)}</td>
            </tr>
        `)
        .join("");

    if (updated) {
        updated.textContent = currentCompHelperConfig.updatedAtUtc
            ? `Loaded ${new Date(currentCompHelperConfig.updatedAtUtc).toLocaleString()}`
            : "";
    }
}

function renderCompHelperConfigStatus(message, success = true) {
    const status = document.querySelector("#analysis-comp-helper-config-status");
    if (!status) {
        return;
    }

    status.classList.toggle("import-status-error", !success);
    status.textContent = message;
}

function updateCompHelperConfigValue(input) {
    if (!currentCompHelperConfig) {
        return false;
    }

    const kind = input.dataset.compHelperConfigKind;
    const key = input.dataset.compHelperConfigKey;
    const field = input.dataset.compHelperConfigField;
    const targets = kind === "lane"
        ? currentCompHelperConfig.laneTargets
        : currentCompHelperConfig.packageTargets;
    const target = (targets ?? []).find(item => stringEqualsIgnoreCase(item.key, key));
    if (!target || !field) {
        return false;
    }

    if (input.type === "checkbox") {
        target[field] = input.checked;
    } else if (input.type === "number") {
        const numeric = Number(input.value);
        target[field] = Number.isFinite(numeric) ? numeric : 0;
    } else {
        target[field] = input.value.trim() || target.key;
    }

    applyCompHelperConfig(currentCompHelperConfig);
    renderCompHelperConfigStatus("Unsaved Comp Helper config changes are active.");
    return true;
}

function renderAnalysisCompHelperPlaceholder(message) {
    const summary = document.querySelector("#analysis-comp-helper-summary");
    const locksContainer = document.querySelector("#analysis-comp-helper-locks");
    const candidatesBody = document.querySelector("#analysis-comp-helper-candidates-body");
    const suggestionsContainer = document.querySelector("#analysis-comp-helper-suggestions");
    if (summary) {
        summary.textContent = message;
    }
    if (locksContainer) {
        locksContainer.innerHTML = "";
    }
    if (candidatesBody) {
        candidatesBody.innerHTML = `<tr><td colspan="7">${escapeHtml(message)}</td></tr>`;
    }
    if (suggestionsContainer) {
        suggestionsContainer.innerHTML = "";
    }
}

function renderAnalysisCompHelper(snapshot) {
    const summary = document.querySelector("#analysis-comp-helper-summary");
    const locksContainer = document.querySelector("#analysis-comp-helper-locks");
    const candidatesBody = document.querySelector("#analysis-comp-helper-candidates-body");
    const suggestionsContainer = document.querySelector("#analysis-comp-helper-suggestions");
    const favoredLanesContainer = document.querySelector("#analysis-comp-helper-favored-lanes");
    const favoredPackagesContainer = document.querySelector("#analysis-comp-helper-favored-packages");
    if (!summary || !locksContainer || !candidatesBody || !suggestionsContainer || !favoredLanesContainer || !favoredPackagesContainer) {
        return;
    }

    if (!currentAnalysisAllPlayerDetails) {
        const message = currentAnalysisAllPlayerDetailsPromise
            ? "Loading Comp Helper player cards..."
            : "Loading Comp Helper player cards on demand...";
        renderAnalysisCompHelperPlaceholder(message);
        void ensureAnalysisAllPlayerDetails()
            .then(() => {
                if (snapshot === currentAnalysisSnapshot && activeAnalysisTab === "comp-helper") {
                    renderAnalysisCompHelper(snapshot);
                }
            })
            .catch(error => {
                if (snapshot === currentAnalysisSnapshot) {
                    renderAnalysisCompHelperPlaceholder(error instanceof Error ? error.message : String(error));
                }
            });
        return;
    }

    const compSnapshot = getCompHelperCandidateSnapshot(snapshot);
    const candidates = getCompHelperCandidates(compSnapshot);
    const lockedCandidates = getLockedCompHelperCandidates(candidates);
    const filteredCandidates = getCompHelperFilteredCandidates(candidates);
    const searchResult = searchCompHelperSuggestions(compSnapshot, candidates);
    syncCompHelperProfileControl();
    syncCompHelperCandidateTierControl();
    favoredLanesContainer.innerHTML = compHelperLaneTargets
        .map(target => buildCompHelperFavoriteToggle(target, "lanes", compHelperFavoredLaneKeys.includes(target.key)))
        .join("");
    favoredPackagesContainer.innerHTML = compHelperPackageTargets
        .map(target => buildCompHelperFavoriteToggle(target, "packages", compHelperFavoredPackageKeys.includes(target.key)))
        .join("");
    const lockedCopy = lockedCandidates.length === 0
        ? `No locked cards. V2 is searching from scratch over cards with at least ${COMP_HELPER_MIN_FILTERED_FIGHTS} filtered fights and ${COMP_HELPER_MIN_TOTAL_FIGHTS} imported total fights.`
        : `Locked ${lockedCandidates.length} of ${COMP_HELPER_TEAM_SIZE} cards. Suggestions fill the remaining ${Math.max(0, COMP_HELPER_TEAM_SIZE - lockedCandidates.length)} slots with lane and package coverage in mind.`;
    const shortageCopy = searchResult.shortage > 0
        ? `Not enough unique unlocked accounts remain to fill the team. Need ${searchResult.shortage} more account${searchResult.shortage === 1 ? "" : "s"}.`
        : null;
    const profileCopy = compHelperProfileKey === "custom"
        ? "Custom priorities are active."
        : `${compHelperProfileKey.charAt(0).toUpperCase()}${compHelperProfileKey.slice(1)} profile is active.`;
    const candidateTier = getCompHelperCandidateTier();
    const tierPoolCount = getCompHelperSearchPoolCandidates(candidates, lockedCandidates).length;
    const tierCopy = candidateTier.key === "best"
        ? candidateTier.summary
        : `${candidateTier.summary} The solver checks up to ${tierPoolCount} cards nearest that tier.`;
    const favoredLaneCopy = compHelperFavoredLaneKeys.length > 0
        ? `Favored lanes: ${compHelperLaneTargets.filter(target => compHelperFavoredLaneKeys.includes(target.key)).map(target => target.label).join(", ")}.`
        : "No extra lanes are favored.";
    const favoredPackageCopy = compHelperFavoredPackageKeys.length > 0
        ? `Favored packages: ${compHelperPackageTargets.filter(target => compHelperFavoredPackageKeys.includes(target.key)).map(target => target.label).join(", ")}.`
        : "No extra packages are favored.";
    const mandatoryPackages = compHelperPackageTargets.filter(target => target.mandatory).map(target => target.label);
    const secondaryPackages = compHelperPackageTargets.filter(target => !target.mandatory).map(target => target.label);
    const mandatoryPackageCopy = mandatoryPackages.length
        ? `Required packages: ${mandatoryPackages.join(", ")}.`
        : "No packages are marked required.";
    const secondaryPackageCopy = secondaryPackages.length
        ? `Secondary packages include ${secondaryPackages.join(", ")}.`
        : "No secondary packages are configured.";

    summary.textContent = `${filteredCandidates.length} candidate cards available. ${profileCopy} ${tierCopy} ${lockedCopy} ${mandatoryPackageCopy} ${secondaryPackageCopy} ${favoredLaneCopy} ${favoredPackageCopy}${shortageCopy ? ` ${shortageCopy}` : ""}`;

    locksContainer.innerHTML = lockedCandidates.length > 0
        ? lockedCandidates.map(buildCompHelperLockPill).join("")
        : `<div class="table-inline-note">No cards locked yet. Lock 1 to 4 cards to fill around an existing core, leave it empty to build from scratch, or lock all 5 to score a fixed group.</div>`;

    candidatesBody.innerHTML = filteredCandidates.length > 0
        ? filteredCandidates.map(candidate => buildCompHelperCandidateRow(candidate, lockedCandidates)).join("")
        : `<tr><td colspan="7">No candidate cards matched the current comp-helper thresholds or search.</td></tr>`;

    suggestionsContainer.className = "comp-helper-suggestions";
    suggestionsContainer.innerHTML = searchResult.suggestions.length > 0
        ? searchResult.suggestions.map((suggestion, index) => buildCompHelperSuggestionCard(suggestion, lockedCandidates, index)).join("")
        : `<article class="comp-helper-card"><strong>No complete comp suggestions yet.</strong><p class="workspace-note comp-helper-section-note">${escapeHtml(shortageCopy ?? "Try clearing some locks or widening the current analysis filters so more candidate cards qualify.")}</p></article>`;
}

function toggleCompHelperCandidateLock(candidateId) {
    if (!currentAnalysisSnapshot || !candidateId) {
        return;
    }

    const candidates = getCompHelperCandidates(currentAnalysisSnapshot);
    const candidate = candidates.find(item => item.id === candidateId);
    if (!candidate) {
        return;
    }

    if (lockedCompHelperCandidateIds.includes(candidateId)) {
        lockedCompHelperCandidateIds = lockedCompHelperCandidateIds.filter(id => id !== candidateId);
        renderAnalysisCompHelper(currentAnalysisSnapshot);
        return;
    }

    const existingByAccount = candidates.find(item =>
        lockedCompHelperCandidateIds.includes(item.id)
        && stringEqualsIgnoreCase(item.account, candidate.account));
    if (existingByAccount) {
        lockedCompHelperCandidateIds = lockedCompHelperCandidateIds.filter(id => id !== existingByAccount.id);
        lockedCompHelperCandidateIds.push(candidateId);
        renderAnalysisCompHelper(currentAnalysisSnapshot);
        return;
    }

    if (lockedCompHelperCandidateIds.length >= COMP_HELPER_MAX_LOCKED_CARDS) {
        renderAnalysisCompHelper(currentAnalysisSnapshot);
        return;
    }

    lockedCompHelperCandidateIds = [...lockedCompHelperCandidateIds, candidateId];
    renderAnalysisCompHelper(currentAnalysisSnapshot);
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
    const fightImpactDetail = getAverageFightImpactDetail(player);

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
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(fightImpactDetail.value)}</strong>
                    <span class="table-inline-note">${escapeHtml(fightImpactDetail.note)}</span>
                </div>
            </td>
            <td>${buildStripCorruptStack(player.averageCorruptsPerFight, player.stripCorruptPercent)}</td>
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
            return `${formatCharacterLaneMetricPerFight(lane, "stripsTotal", totalFights)} strips, ${formatCharacterLaneMetricPerFight(lane, "stripCorruptsTotal", totalFights)} corrupts, and ${formatCharacterLaneMetricPerFight(lane, "stripDownContribution", totalFights)} down-linked strips per filtered fight.`;
        case "control":
            return `${formatCharacterLaneMetricPerFight(lane, "effectiveCrowdControlCount", totalFights)} effective CC events and ${formatCharacterLaneMetricPerFight(lane, "crowdControlDownContribution", totalFights)} CC-linked downs per filtered fight.`;
        case "boonsupport":
            return `${formatCharacterLaneMetricPerFight(lane, "totalBoonSupport", totalFights)} total boon-seconds per filtered fight, split between ${formatCharacterLaneMetricPerFight(lane, "defensiveBoonSupport", totalFights)} defensive and ${formatCharacterLaneMetricPerFight(lane, "offensiveBoonSupport", totalFights)} offensive coverage.`;
        case "recovery":
            return `${formatCharacterLaneMetricPerFight(lane, "cleansesTotal", totalFights)} cleanses and ${formatCharacterLaneMetricPerFight(lane, "healingTotal", totalFights)} healing per filtered fight.`;
        case "prevention": {
            const preventionValueMetric = getCharacterLaneMetric(lane, "preventionValue");
            return preventionValueMetric
                ? `${formatCharacterLaneMetricPerFight(lane, "preventionValue", totalFights)} prevention value per filtered fight from ${formatCharacterLaneMetricPerFight(lane, "barrierTotal", totalFights)} barrier, ${formatCharacterLaneMetricPerFight(lane, "negatedDamageTotal", totalFights)} estimated negated damage, ${formatCharacterLaneMetricPerFight(lane, "petAbsorptionTotal", totalFights)} pet/minion absorption, and reduced defensive condition pressure.`
                : `${formatCharacterLaneMetricPerFight(lane, "barrierTotal", totalFights)} barrier, ${formatCharacterLaneMetricPerFight(lane, "negatedDamageTotal", totalFights)} estimated negated damage, and ${formatCharacterLaneMetricPerFight(lane, "petAbsorptionTotal", totalFights)} pet/minion absorption per filtered fight.`;
        }
        case "rez":
            return `${formatCharacterLaneMetricPerFight(lane, "squadRecoveryWindowsHelped", totalFights)} recoveries helped, ${formatCharacterLaneMetricPerFight(lane, "rezTimeOnRecoveries", totalFights)} rez time, and ${formatCharacterLaneMetricPerFight(lane, "downedHealingOnRecoveries", totalFights)} downed healing per filtered fight.`;
        default:
            return lane.evidenceLine || "No aggregate lane metrics were retained for this lane.";
    }
}

function buildAnalysisPlayerCompCandidateLookup(snapshot) {
    const lookup = new Map();
    if (!snapshot || !currentAnalysisAllPlayerDetails) {
        return lookup;
    }

    for (const candidate of buildCompHelperCandidates({
        ...snapshot,
        topPlayers: currentAnalysisAllPlayerDetails
    })) {
        lookup.set(candidate.id, candidate);
    }

    return lookup;
}

function getAnalysisCharacterCompCandidate(player, character, compCandidateLookup) {
    if (!player || !character || !compCandidateLookup) {
        return null;
    }

    const candidateId = buildCompHelperCandidateId(player.account, character.characterName, character.classLabel);
    return compCandidateLookup.get(candidateId) ?? null;
}

function getAnalysisCharacterBestLaneItems(character) {
    return [...(character.laneContributions ?? [])]
        .filter(lane => Number(lane.overallStrengthPercent ?? 0) > 0)
        .sort((left, right) => Number(right.overallStrengthPercent ?? 0) - Number(left.overallStrengthPercent ?? 0)
            || Number(right.appearanceRatePercent ?? 0) - Number(left.appearanceRatePercent ?? 0)
            || compareFightBrowserValues(String(left.laneLabel ?? left.laneKey ?? "").toLowerCase(), String(right.laneLabel ?? right.laneKey ?? "").toLowerCase()))
        .slice(0, 3)
        .map(lane => ({
            label: lane.laneLabel ?? lane.laneKey ?? "Lane",
            value: formatPercent(lane.overallStrengthPercent, 0),
            title: `${lane.laneLabel ?? lane.laneKey ?? "Lane"} strength ${formatPercent(lane.overallStrengthPercent)} | ${formatPercent(lane.appearanceRatePercent)} appearance rate`
        }));
}

function getAnalysisCharacterReliableLaneItems(character) {
    return [...(character.laneContributions ?? [])]
        .filter(lane => Number(lane.samples ?? 0) >= 5
            && Number(lane.appearanceRatePercent ?? 0) >= 35
            && Number(lane.overallStrengthPercent ?? 0) > 0)
        .sort((left, right) => Number(right.appearanceRatePercent ?? 0) - Number(left.appearanceRatePercent ?? 0)
            || Number(right.overallStrengthPercent ?? 0) - Number(left.overallStrengthPercent ?? 0)
            || Number(right.samples ?? 0) - Number(left.samples ?? 0)
            || compareFightBrowserValues(String(left.laneLabel ?? left.laneKey ?? "").toLowerCase(), String(right.laneLabel ?? right.laneKey ?? "").toLowerCase()))
        .slice(0, 3)
        .map(lane => ({
            label: lane.laneLabel ?? lane.laneKey ?? "Lane",
            value: `${formatPercent(lane.appearanceRatePercent, 0)} seen`,
            title: `${lane.samples} filtered fight${Number(lane.samples ?? 0) === 1 ? "" : "s"} | ${formatPercent(lane.overallStrengthPercent)} strength`
        }));
}

function getAnalysisCharacterPackageItems(compCandidate) {
    const scores = compCandidate?.packageScores;
    if (!scores) {
        return [];
    }

    return compHelperPackageTargets
        .map(target => ({
            label: target.label,
            score: Number(scores[target.key] ?? 0),
            title: `${target.label} package ${formatNumber(scores[target.key] ?? 0, 1)} | ${formatNumber(compCandidate.filteredFightCount)} filtered fights`
        }))
        .filter(item => item.score >= 35)
        .sort((left, right) => right.score - left.score
            || compareFightBrowserValues(left.label.toLowerCase(), right.label.toLowerCase()))
        .slice(0, 4)
        .map(item => ({
            label: item.label,
            value: formatNumber(item.score, 0),
            title: item.title
        }));
}

function getAnalysisCharacterContextFitItems(character, direction) {
    const isPositive = direction === "positive";
    return [...(character.contextFits ?? [])]
        .filter(fit => Number.isFinite(Number(fit.delta)))
        .filter(fit => isPositive ? Number(fit.delta ?? 0) > 0 : Number(fit.delta ?? 0) < 0)
        .sort((left, right) => isPositive
            ? Number(right.delta ?? 0) - Number(left.delta ?? 0)
                || Number(right.sampleCount ?? 0) - Number(left.sampleCount ?? 0)
                || compareFightBrowserValues(String(left.label ?? "").toLowerCase(), String(right.label ?? "").toLowerCase())
            : Number(left.delta ?? 0) - Number(right.delta ?? 0)
                || Number(right.sampleCount ?? 0) - Number(left.sampleCount ?? 0)
                || compareFightBrowserValues(String(left.label ?? "").toLowerCase(), String(right.label ?? "").toLowerCase()))
        .slice(0, 2)
        .map(fit => {
            const delta = Number(fit.delta ?? 0);
            const sampleCount = Number(fit.sampleCount ?? 0);
            const detail = fit.detail
                || `${fit.label}: ${formatSignedNumber(delta, 1)} Fight Impact delta over ${formatNumber(sampleCount)} fights`;
            return {
                label: fit.label ?? "Context",
                value: `${formatSignedNumber(delta, 1)} ${formatNumber(sampleCount, 0)}f`,
                title: detail,
                className: isPositive ? "attribute-pill-positive" : "attribute-pill-negative"
            };
        });
}

function buildAnalysisCharacterContextRow(label, items) {
    if (!items.length) {
        return "";
    }

    return `
        <div class="analysis-character-context-row">
            <strong>${escapeHtml(label)}</strong>
            <div class="attribute-pill-list">
                ${items.map(item => {
                    const text = item.value ? `${item.label} ${item.value}` : item.label;
                    const classes = ["attribute-pill", item.className].filter(Boolean).join(" ");
                    return `<span class="${escapeHtml(classes)}" title="${escapeHtml(item.title ?? text)}">${escapeHtml(text)}</span>`;
                }).join("")}
            </div>
        </div>
    `;
}

function buildAnalysisCharacterContextPanel(character, compCandidate) {
    const rows = [
        buildAnalysisCharacterContextRow("Best lanes", getAnalysisCharacterBestLaneItems(character)),
        buildAnalysisCharacterContextRow("Reliable", getAnalysisCharacterReliableLaneItems(character)),
        buildAnalysisCharacterContextRow("Packages", getAnalysisCharacterPackageItems(compCandidate)),
        buildAnalysisCharacterContextRow("Best fits", getAnalysisCharacterContextFitItems(character, "positive")),
        buildAnalysisCharacterContextRow("Risk fits", getAnalysisCharacterContextFitItems(character, "negative"))
    ].filter(Boolean);

    if (rows.length === 0) {
        return "";
    }

    return `<div class="analysis-character-context">${rows.join("")}</div>`;
}

function buildAnalysisCharacterLaneDetails(character) {
    const lanes = character.laneContributions ?? [];
    const laneCount = lanes.length;

    return `
        <details class="analysis-character-lane-details">
            <summary>
                <span>Lane evidence</span>
                <span>${escapeHtml(`${laneCount} lane${laneCount === 1 ? "" : "s"}`)}</span>
            </summary>
            <div class="analysis-character-lane-grid">
                ${laneCount
                    ? lanes.map(lane => buildCharacterLaneCard(lane, character.fightCount)).join("")
                    : `<article class="analysis-character-lane-card"><p class="analysis-character-copy">No lane contribution cards were retained for this character.</p></article>`}
            </div>
        </details>
    `;
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

function getAnalysisImpactTrendIds(trends) {
    return (trends ?? []).map(trend => String(trend.key ?? ""));
}

function getAnalysisImpactTrendsByPointCount(trends) {
    return [...(trends ?? [])].sort((left, right) =>
        Number((right.points ?? []).length) - Number((left.points ?? []).length)
        || Number(right.fightCount ?? 0) - Number(left.fightCount ?? 0)
        || compareFightBrowserValues(String(left.label ?? left.characterName ?? "").toLowerCase(), String(right.label ?? right.characterName ?? "").toLowerCase()));
}

function getDefaultAnalysisImpactTrendIds(trends, maximumCount = null) {
    const ordered = getAnalysisImpactTrendsByPointCount(trends);
    const retained = maximumCount == null
        ? ordered
        : ordered.slice(0, Math.max(0, maximumCount));

    return retained.map(trend => String(trend.key ?? ""));
}

function getAnalysisImpactTrendColor(index) {
    return ANALYSIS_BOON_TREND_COLORS[index % ANALYSIS_BOON_TREND_COLORS.length];
}

function getAnalysisImpactTrendSelection(context) {
    return context === "class"
        ? selectedAnalysisClassImpactTrendIds
        : selectedAnalysisPlayerImpactTrendIds;
}

function setAnalysisImpactTrendSelection(context, ids) {
    if (context === "class") {
        selectedAnalysisClassImpactTrendIds = new Set(ids);
        return;
    }

    selectedAnalysisPlayerImpactTrendIds = new Set(ids);
}

function ensureAnalysisPlayerImpactTrendSelection(player, trends) {
    const ids = getAnalysisImpactTrendIds(trends);
    const owner = String(player?.account ?? "");
    if (selectedAnalysisPlayerImpactTrendIds === null || !stringEqualsIgnoreCase(selectedAnalysisPlayerImpactTrendOwner, owner)) {
        selectedAnalysisPlayerImpactTrendOwner = owner;
        selectedAnalysisPlayerImpactTrendIds = new Set(getDefaultAnalysisImpactTrendIds(trends));
        return;
    }

    const validIds = new Set(ids);
    selectedAnalysisPlayerImpactTrendIds = new Set(
        Array.from(selectedAnalysisPlayerImpactTrendIds)
            .filter(id => validIds.has(id)));
}

function ensureAnalysisClassImpactTrendSelection(classRow, trends) {
    const ids = getAnalysisImpactTrendIds(trends);
    const owner = String(classRow?.classLabel ?? "");
    if (selectedAnalysisClassImpactTrendIds === null || !stringEqualsIgnoreCase(selectedAnalysisClassImpactTrendOwner, owner)) {
        selectedAnalysisClassImpactTrendOwner = owner;
        selectedAnalysisClassImpactTrendIds = new Set(getDefaultAnalysisImpactTrendIds(trends, 5));
        return;
    }

    const validIds = new Set(ids);
    selectedAnalysisClassImpactTrendIds = new Set(
        Array.from(selectedAnalysisClassImpactTrendIds)
            .filter(id => validIds.has(id)));
}

function getAnalysisImpactTrendNights(trends) {
    const nightMap = new Map();
    (trends ?? []).forEach(trend => {
        (trend.points ?? []).forEach(point => {
            if (!point?.dateKey || nightMap.has(point.dateKey)) {
                return;
            }

            nightMap.set(point.dateKey, point.dateLabel ?? point.dateKey);
        });
    });

    return Array.from(nightMap.entries())
        .sort((left, right) => left[0].localeCompare(right[0]))
        .map(([dateKey, dateLabel]) => ({ dateKey, dateLabel }));
}

function buildAnalysisImpactTrendSummary(trends, selectedIds) {
    const nightCount = getAnalysisImpactTrendNights(trends).length;
    const selectedCount = Array.from(selectedIds ?? []).length;
    const characterCount = trends?.length ?? 0;

    if (characterCount === 0 || nightCount === 0) {
        return "No character impact trend data matched the current filters.";
    }

    return `${selectedCount} of ${characterCount} characters selected | ${nightCount} ${nightCount === 1 ? "night" : "nights"}`;
}

function buildAnalysisImpactTrendLegend(trends, selectedIds, context) {
    return getAnalysisImpactTrendsByPointCount(trends).map(trend => {
        const id = String(trend.key ?? "");
        const checked = selectedIds?.has(id) ? "checked" : "";
        const trendIndex = (trends ?? []).findIndex(item => String(item.key ?? "") === id);
        const color = getAnalysisImpactTrendColor(Math.max(0, trendIndex));
        const accountMarkup = trend.account
            ? `<span class="analysis-impact-trend-account">${escapeHtml(trend.account)}</span>`
            : "";
        return `
            <label class="analysis-boon-trend-option ${checked ? "is-active" : ""}">
                <input type="checkbox" data-analysis-impact-trend-context="${escapeHtml(context)}" data-analysis-impact-trend-id="${escapeHtml(id)}" ${checked}>
                <span class="analysis-boon-trend-swatch" style="--boon-color: ${escapeHtml(color)}"></span>
                <span class="analysis-impact-trend-label">${escapeHtml(trend.label ?? trend.characterName ?? id)}</span>
                ${accountMarkup}
            </label>
        `;
    }).join("");
}

function getAnalysisImpactTrendAxis(trends) {
    const values = (trends ?? [])
        .flatMap(trend => trend.points ?? [])
        .map(point => Number(point.impactScore))
        .filter(value => !Number.isNaN(value));

    if (values.length === 0) {
        return { min: 0, max: 100 };
    }

    const minimum = Math.max(0, Math.min(...values) - 10);
    const maximum = Math.min(100, Math.max(...values) + 10);

    if (maximum - minimum < 1) {
        return {
            min: Math.max(0, minimum - 5),
            max: Math.min(100, maximum + 5)
        };
    }

    return { min: minimum, max: maximum };
}

function getAnalysisImpactTrendGridValues(axis) {
    const range = Math.max(1, axis.max - axis.min);
    return Array.from({ length: 5 }, (_, index) =>
        Math.round((axis.max - (range * index / 4)) * 10) / 10);
}

function buildAnalysisImpactTrendPath(trend, nightIndex, chart) {
    const pointByNight = new Map((trend.points ?? []).map(point => [point.dateKey, point]));
    let started = false;

    return Array.from(nightIndex.entries())
        .map(([dateKey, index]) => {
            const point = pointByNight.get(dateKey);
            if (!point) {
                started = false;
                return "";
            }

            const x = chart.xForIndex(index);
            const y = chart.yForValue(point.impactScore);
            const command = started ? "L" : "M";
            started = true;
            return `${command} ${x} ${y}`;
        })
        .filter(Boolean)
        .join(" ");
}

function buildAnalysisImpactTrendChart(trends, selectedIds, context) {
    const nights = getAnalysisImpactTrendNights(trends);
    const selectedTrends = (trends ?? [])
        .filter(trend => selectedIds?.has(String(trend.key ?? "")));

    if (nights.length === 0) {
        return `<div class="analysis-boon-trend-empty">No character impact trend data available.</div>`;
    }

    if (selectedTrends.length === 0) {
        return `<div class="analysis-boon-trend-empty">No characters selected.</div>`;
    }

    const width = 760;
    const height = 260;
    const plot = { left: 44, top: 18, right: 18, bottom: 38 };
    const plotWidth = width - plot.left - plot.right;
    const plotHeight = height - plot.top - plot.bottom;
    const nightIndex = new Map(nights.map((night, index) => [night.dateKey, index]));
    const axis = getAnalysisImpactTrendAxis(selectedTrends);
    const axisRange = Math.max(1, axis.max - axis.min);
    const chart = {
        xForIndex: index => Math.round((plot.left + (nights.length <= 1 ? plotWidth / 2 : index * plotWidth / (nights.length - 1))) * 100) / 100,
        yForValue: value => {
            const clamped = Math.max(axis.min, Math.min(axis.max, Number(value) || 0));
            return Math.round((plot.top + (axis.max - clamped) * plotHeight / axisRange) * 100) / 100;
        }
    };
    const gridValues = getAnalysisImpactTrendGridValues(axis);
    const axisIndexes = getAnalysisBoonTrendAxisIndexes(nights.length);

    const gridMarkup = gridValues.map(value => {
        const y = chart.yForValue(value);
        return `
            <line class="grid-line" x1="${plot.left}" y1="${y}" x2="${width - plot.right}" y2="${y}"></line>
            <text class="axis-label y-axis-label" x="${plot.left - 10}" y="${y + 4}">${escapeHtml(formatNumber(value, axisRange <= 20 ? 1 : 0))}</text>
        `;
    }).join("");

    const axisMarkup = axisIndexes.map(index => {
        const night = nights[index];
        const x = chart.xForIndex(index);
        return `<text class="axis-label x-axis-label" x="${x}" y="${height - 12}">${escapeHtml(night.dateLabel)}</text>`;
    }).join("");

    const seriesMarkup = selectedTrends.map(trend => {
        const trendIndex = (trends ?? []).findIndex(item => String(item.key ?? "") === String(trend.key ?? ""));
        const color = getAnalysisImpactTrendColor(Math.max(0, trendIndex));
        const path = buildAnalysisImpactTrendPath(trend, nightIndex, chart);
        const points = (trend.points ?? []).map(point => {
            if (!nightIndex.has(point.dateKey)) {
                return "";
            }

            const x = chart.xForIndex(nightIndex.get(point.dateKey));
            const y = chart.yForValue(point.impactScore);
            const label = `${trend.label ?? trend.characterName} ${point.dateLabel}: Performance ${formatNumber(point.impactScore, 1)}`;
            return `
                <circle class="analysis-boon-trend-point"
                    cx="${x}"
                    cy="${y}"
                    r="4.2"
                    style="--boon-color: ${escapeHtml(color)}"
                    tabindex="0"
                    role="img"
                    aria-label="${escapeHtml(label)}"
                    data-analysis-impact-trend-point
                    data-analysis-impact-context="${escapeHtml(context)}"
                    data-trend-key="${escapeHtml(String(trend.key ?? ""))}"
                    data-date-key="${escapeHtml(point.dateKey)}"></circle>
            `;
        }).join("");

        return `
            ${path ? `<path class="analysis-boon-trend-line" d="${escapeHtml(path)}" style="--boon-color: ${escapeHtml(color)}"></path>` : ""}
            ${points}
        `;
    }).join("");

    return `
        <svg class="analysis-boon-trend-chart" viewBox="0 0 ${width} ${height}" preserveAspectRatio="xMidYMid meet" aria-hidden="false">
            ${gridMarkup}
            ${axisMarkup}
            ${seriesMarkup}
        </svg>
    `;
}

function buildAnalysisImpactTrendCard(context, title, trends, selectedIds) {
    const prefix = context === "class" ? "analysis-class-impact-trend" : "analysis-player-impact-trend";
    const isExpanded = Boolean(analysisImpactTrendLegendExpanded[context]);
    const showLegendToggle = (trends?.length ?? 0) > 5;
    return `
        <section class="analysis-card analysis-card--wide analysis-boon-trend-card analysis-impact-trend-card">
            <div class="section-heading">
                <div>
                    <h3>${escapeHtml(title)}</h3>
                    <p id="${prefix}-summary">${escapeHtml(buildAnalysisImpactTrendSummary(trends, selectedIds))}</p>
                </div>
                <div class="analysis-boon-trend-actions">
                    <button class="action-link action-link-button" type="button" data-analysis-impact-trend-context="${escapeHtml(context)}" data-analysis-impact-trend-action="all">Add all</button>
                    <button class="action-link action-link-button" type="button" data-analysis-impact-trend-context="${escapeHtml(context)}" data-analysis-impact-trend-action="none">Clear all</button>
                </div>
            </div>
            <div class="analysis-boon-trend-legend analysis-impact-trend-legend ${isExpanded ? "is-expanded" : "is-collapsed"}" id="${prefix}-legend">
                ${buildAnalysisImpactTrendLegend(trends, selectedIds, context)}
            </div>
            ${showLegendToggle
                ? `<button class="action-link action-link-button analysis-impact-trend-toggle" type="button" data-analysis-impact-trend-context="${escapeHtml(context)}" data-analysis-impact-trend-toggle aria-expanded="${isExpanded ? "true" : "false"}">${escapeHtml(isExpanded ? "Show fewer characters" : `Show all ${trends.length} characters`)}</button>`
                : ""}
            <div class="analysis-boon-trend-chart-wrap" id="${prefix}-wrap">
                <div class="analysis-boon-trend-tooltip" id="${prefix}-tooltip" hidden></div>
                <div id="${prefix}-chart">${buildAnalysisImpactTrendChart(trends, selectedIds, context)}</div>
            </div>
        </section>
    `;
}

function getCurrentAnalysisImpactTrends(context) {
    if (!currentAnalysisSnapshot) {
        return [];
    }

    if (context === "class") {
        const classRow = (currentAnalysisSnapshot.topClasses ?? [])
            .find(row => stringEqualsIgnoreCase(row.classLabel, selectedAnalysisClassLabel));
        return classRow?.characterImpactTrends ?? [];
    }

    const player = getCachedAnalysisPlayerDetail(selectedAnalysisPlayerAccount);
    return player?.characterImpactTrends ?? [];
}

function findAnalysisImpactTrendPoint(context, trendKey, dateKey) {
    const trend = getCurrentAnalysisImpactTrends(context)
        .find(item => String(item.key ?? "") === String(trendKey ?? ""));
    const point = trend?.points?.find(item => item.dateKey === dateKey) ?? null;
    return { trend, point };
}

function buildAnalysisImpactTrendTooltipHtml(context, trend, point) {
    const detailParts = [trend.classLabel];
    if (context === "class" && trend.account) {
        detailParts.push(trend.account);
    }

    return `
        <strong>${escapeHtml(trend.label ?? trend.characterName ?? "Character")}</strong>
        <div class="analysis-boon-trend-tooltip-value">${escapeHtml(formatNumber(point.impactScore, 1))}</div>
        <div class="table-inline-note">${escapeHtml(`${point.dateLabel ?? point.dateKey} | ${point.fightCount} ${point.fightCount === 1 ? "fight" : "fights"}`)}</div>
        <div class="table-inline-note">${escapeHtml(detailParts.filter(Boolean).join(" | "))}</div>
    `;
}

function showAnalysisImpactTrendTooltip(event) {
    const target = event.target.closest("[data-analysis-impact-trend-point]");
    if (!target) {
        return;
    }

    const context = target.dataset.analysisImpactContext === "class" ? "class" : "player";
    const tooltip = document.querySelector(`#analysis-${context}-impact-trend-tooltip`);
    const wrap = document.querySelector(`#analysis-${context}-impact-trend-wrap`);
    if (!tooltip || !wrap) {
        return;
    }

    const { trend, point } = findAnalysisImpactTrendPoint(context, target.dataset.trendKey, target.dataset.dateKey);
    if (!trend || !point) {
        return;
    }

    tooltip.innerHTML = buildAnalysisImpactTrendTooltipHtml(context, trend, point);
    tooltip.hidden = false;

    const wrapRect = wrap.getBoundingClientRect();
    const targetRect = target.getBoundingClientRect();
    const targetX = targetRect.left + (targetRect.width / 2) - wrapRect.left;
    const targetY = targetRect.top - wrapRect.top;
    const tooltipWidth = tooltip.offsetWidth || 260;
    const tooltipHeight = tooltip.offsetHeight || 120;
    const left = Math.max(8, Math.min(wrapRect.width - tooltipWidth - 8, targetX + 12));
    const top = Math.max(8, Math.min(wrapRect.height - tooltipHeight - 8, targetY - tooltipHeight - 10));

    tooltip.style.left = `${left}px`;
    tooltip.style.top = `${top}px`;
}

function hideAnalysisImpactTrendTooltip(context) {
    const contexts = context ? [context] : ["player", "class"];
    contexts.forEach(item => {
        const tooltip = document.querySelector(`#analysis-${item}-impact-trend-tooltip`);
        if (!tooltip) {
            return;
        }

        tooltip.hidden = true;
        tooltip.innerHTML = "";
    });
}

function renderAnalysisImpactTrendContext(context) {
    if (!currentAnalysisSnapshot) {
        return;
    }

    if (context === "class") {
        const classRow = (currentAnalysisSnapshot.topClasses ?? [])
            .find(row => stringEqualsIgnoreCase(row.classLabel, selectedAnalysisClassLabel));
        renderAnalysisClassDetail(classRow ?? null);
        return;
    }

    if (activeAnalysisTab === "players") {
        renderSelectedAnalysisPlayerDetail();
    } else {
        renderAnalysisPlayerDetailMessage("Open Players to load character cards for the selected player.");
    }
}

function handleAnalysisImpactTrendSelectionChange(event) {
    const input = event.target.closest("[data-analysis-impact-trend-id]");
    if (!input || !currentAnalysisSnapshot) {
        return;
    }

    const context = input.dataset.analysisImpactTrendContext === "class" ? "class" : "player";
    const id = String(input.dataset.analysisImpactTrendId ?? "");
    const selectedIds = new Set(getAnalysisImpactTrendSelection(context) ?? []);
    if (input.checked) {
        selectedIds.add(id);
    } else {
        selectedIds.delete(id);
    }

    setAnalysisImpactTrendSelection(context, selectedIds);
    renderAnalysisImpactTrendContext(context);
}

function handleAnalysisImpactTrendActionClick(event) {
    const toggle = event.target.closest("[data-analysis-impact-trend-toggle]");
    if (toggle && currentAnalysisSnapshot) {
        const context = toggle.dataset.analysisImpactTrendContext === "class" ? "class" : "player";
        analysisImpactTrendLegendExpanded = {
            ...analysisImpactTrendLegendExpanded,
            [context]: !analysisImpactTrendLegendExpanded[context]
        };
        renderAnalysisImpactTrendContext(context);
        return;
    }

    const button = event.target.closest("[data-analysis-impact-trend-action]");
    if (!button || !currentAnalysisSnapshot) {
        return;
    }

    const context = button.dataset.analysisImpactTrendContext === "class" ? "class" : "player";
    const trends = getCurrentAnalysisImpactTrends(context);
    const ids = button.dataset.analysisImpactTrendAction === "all"
        ? getAnalysisImpactTrendIds(trends)
        : [];
    setAnalysisImpactTrendSelection(context, ids);
    renderAnalysisImpactTrendContext(context);
}

function buildAnalysisCharacterCard(character, player = null, compCandidateLookup = null) {
    const disciplineValue = character.averageInPositionRate != null
        ? `${formatPercent(character.averageInPositionRate)} in position`
        : "No positioning sample";
    const disciplineCopy = character.averageInPositionRate != null
        ? `${formatPercent(character.averageTooFarRate)} too far, ${formatPercent(character.averageOverextendedRate)} overextended, ${formatPercent(character.averageLateralRiskRate)} lateral risk`
        : "Commander-relative replay samples were not available for this character.";
    const classText = character.classLabel || "Unknown class";
    const fightImpactNote = buildFightImpactNote(character);
    const fightImpactPills = buildDemandAdjustedLanePills(character.fightImpactLanes, "averageImpactScore", "Fight Impact");
    const compCandidate = getAnalysisCharacterCompCandidate(player, character, compCandidateLookup);
    const contextPanel = buildAnalysisCharacterContextPanel(character, compCandidate);

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
                    <span class="analysis-character-pill">${escapeHtml(`Performance ${formatNumber(character.impactScore, 1)}`)}</span>
                    <span class="analysis-character-pill">${escapeHtml(`${formatNumber(character.averageCorruptsPerFight, 1)} corrupts/fight`)}</span>
                    <span class="analysis-character-pill">${escapeHtml(`${formatPercent(character.stripCorruptPercent)} strip corrupt`)}</span>
                    ${fightImpactNote ? `<span class="analysis-character-pill">${escapeHtml(`Fight Impact ${formatNumber(character.averageFightImpactScore, 1)}/100`)}</span>` : ""}
                    <span class="analysis-character-pill">${escapeHtml(`${character.confidenceLabel ?? "Unknown"} confidence`)}</span>
                </div>
            </div>
            <p class="analysis-character-lead">${escapeHtml(buildCharacterObservedLead(character))}</p>
            <p class="analysis-character-copy">${escapeHtml(buildCharacterObservedCopy(character))}</p>
            ${fightImpactNote ? `<p class="analysis-character-copy">${escapeHtml(fightImpactNote)}.</p>${fightImpactPills}` : ""}
            ${contextPanel}
            <div class="analysis-character-discipline analysis-character-stat">
                <strong>Discipline</strong>
                <div>${escapeHtml(disciplineValue)}</div>
                <div class="table-inline-note">${escapeHtml(disciplineCopy)}</div>
            </div>
            ${character.confidenceDetail ? `<p class="table-inline-note">${escapeHtml(character.confidenceDetail)}</p>` : ""}
            ${buildAnalysisCharacterLaneDetails(character)}
            <div class="analysis-character-footer">
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

function renderAnalysisPlayerDetailMessage(message) {
    const container = document.querySelector("#analysis-player-detail");
    if (!container) {
        return;
    }

    container.className = "analysis-player-detail";
    container.innerHTML = `<p class="workspace-note">${escapeHtml(message)}</p>`;
}

function renderAnalysisPlayerDetail(player) {
    const container = document.querySelector("#analysis-player-detail");
    if (!player) {
        container.innerHTML = "";
        return;
    }

    const impactTrends = player.characterImpactTrends ?? [];
    ensureAnalysisPlayerImpactTrendSelection(player, impactTrends);
    const selectedIds = selectedAnalysisPlayerImpactTrendIds ?? new Set();
    const compCandidateLookup = buildAnalysisPlayerCompCandidateLookup(currentAnalysisSnapshot);

    container.className = "analysis-player-detail";
    container.innerHTML = `
        <div class="section-heading">
            <div>
                <h3>${escapeHtml(player.account)}</h3>
                <p>Character/class breakdown across the current analysis filter.</p>
            </div>
        </div>
        ${buildAnalysisImpactTrendCard("player", "Character Performance", impactTrends, selectedIds)}
        <div class="analysis-character-grid">
            ${(player.characters ?? []).map(character => buildAnalysisCharacterCard(character, player, compCandidateLookup)).join("")}
        </div>
    `;
    hideAnalysisImpactTrendTooltip("player");
}

function renderSelectedAnalysisPlayerDetail() {
    if (!selectedAnalysisPlayerAccount) {
        renderAnalysisPlayerDetail(null);
        return;
    }

    const selectedAccount = selectedAnalysisPlayerAccount;
    const cachedPlayer = getCachedAnalysisPlayerDetail(selectedAccount);
    if (cachedPlayer) {
        renderAnalysisPlayerDetail(cachedPlayer);
        return;
    }

    renderAnalysisPlayerDetailMessage(`Loading ${selectedAccount} character cards...`);
    void ensureAnalysisPlayerDetail(selectedAccount)
        .then(player => {
            if (currentAnalysisSnapshot && stringEqualsIgnoreCase(selectedAnalysisPlayerAccount, selectedAccount)) {
                renderAnalysisPlayerDetail(player);
            }
        })
        .catch(error => {
            if (stringEqualsIgnoreCase(selectedAnalysisPlayerAccount, selectedAccount)) {
                renderAnalysisPlayerDetailMessage(error instanceof Error ? error.message : String(error));
            }
        });
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
        ? "Performance is the existing Analyst score across each player's character/spec cards; Fight Impact is the separate parser-derived average."
        : `Performance shows the best ${selectedLaneLabel} value from any character/spec card with at least 10 total fights, and only players with at least ${MINIMUM_LANE_FILTER_APPEARANCES} total ${selectedLaneLabel} appearances across their Guild Wars ID are shown. Fight Impact remains the overall demand-adjusted average.`;
    const thresholdSummary = hasSearchValue
        ? `Search override is active, so matching players below ${MINIMUM_PLAYER_TABLE_FIGHTS} total imported fights can still appear.`
        : `By default only players with at least ${MINIMUM_PLAYER_TABLE_FIGHTS} total imported fights are shown.`;

    summary.textContent = filteredPlayers.length > 0
        ? `Showing ${filteredPlayers.length} players. ${thresholdSummary} ${laneScopeSummary}`
        : `No players matched the current filters and thresholds. ${thresholdSummary} ${laneScopeSummary}`;

    if (filteredPlayers.length === 0) {
        selectedAnalysisPlayerAccount = null;
        body.innerHTML = `<tr><td colspan="8">No player rows matched the current filters.</td></tr>`;
        renderAnalysisPlayerDetail(null);
        return;
    }

    if (!filteredPlayers.some(player => stringEqualsIgnoreCase(player.account, selectedAnalysisPlayerAccount))) {
        selectedAnalysisPlayerAccount = filteredPlayers[0].account;
    }

    body.innerHTML = filteredPlayers
        .map(player => buildAnalysisPlayerRow(player, stringEqualsIgnoreCase(player.account, selectedAnalysisPlayerAccount)))
        .join("");

    if (activeAnalysisTab === "players") {
        renderSelectedAnalysisPlayerDetail();
    } else {
        renderAnalysisPlayerDetailMessage("Open Players to load character cards for the selected player.");
    }
}

function buildAnalysisClassRow(classRow) {
    const rowClasses = ["is-clickable"];
    if (stringEqualsIgnoreCase(classRow.classLabel, selectedAnalysisClassLabel)) {
        rowClasses.push("is-selected");
    }
    const fightCoverageDetail = getAverageFightCoverageDetail(classRow);

    return `
        <tr class="${rowClasses.join(" ")}" data-class-label="${escapeHtml(classRow.classLabel)}">
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(classRow.classLabel)}</strong>
                    ${buildPatchImpactPills(classRow.patchImpacts, 2)
                        ? `<div class="attribute-pill-list">${buildPatchImpactPills(classRow.patchImpacts, 2)}</div>`
                        : ""}
                </div>
            </td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(String(classRow.sampleCount))}</strong>
                    <span class="table-inline-note">${escapeHtml("Selected samples")}</span>
                    <span class="table-inline-note">${escapeHtml(`${formatNumber(classRow.totalSampleCountAll ?? classRow.sampleCount)} total | ${classRow.distinctAccounts} accounts`)}</span>
                </div>
            </td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(formatNumber(classRow.contributionScore, 1))}</strong>
                    <span class="table-inline-note">${escapeHtml(`${formatPercent(classRow.averageWeightedLaneScore)} weighted lane`)}</span>
                </div>
            </td>
            <td>${buildStripCorruptStack(classRow.averageCorruptsPerFight, classRow.stripCorruptPercent)}</td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(fightCoverageDetail.value)}</strong>
                    <span class="table-inline-note">${escapeHtml(fightCoverageDetail.note)}</span>
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

function buildPatchImpactPills(impacts, limit = 3) {
    const retained = (impacts ?? []).filter(impact => impact?.classLabel);
    if (retained.length === 0) {
        return "";
    }

    const laneDeltas = retained
        .flatMap(impact => (impact.laneImpacts ?? []).map(lane => ({ impact, lane })))
        .filter(entry => Number(entry.lane?.impact ?? 0) !== 0)
        .sort((left, right) => Math.abs(Number(right.lane.impact ?? 0)) - Math.abs(Number(left.lane.impact ?? 0)))
        .slice(0, limit);

    if (laneDeltas.length === 0) {
        return retained
            .slice(0, limit)
            .map(impact => `<span class="attribute-pill attribute-pill-muted">${escapeHtml(`${impact.buildLabel ?? impact.classLabel}: ${impact.adoptionExpectation ?? impact.confidence}`)}</span>`)
            .join("");
    }

    return laneDeltas
        .map(({ impact, lane }) => {
            const value = Number(lane.impact ?? 0);
            const sign = value > 0 ? "+" : "";
            const className = value > 0 ? "attribute-pill-positive" : "attribute-pill-negative";
            const title = [impact.notes, lane.notes].filter(Boolean).join(" ");
            return `<span class="attribute-pill ${className}" title="${escapeHtml(title)}">${escapeHtml(`${lane.laneLabel} ${sign}${value}`)}</span>`;
        })
        .join("");
}

function buildPatchImpactDetail(impacts) {
    const retained = (impacts ?? []).filter(impact => impact?.classLabel);
    if (retained.length === 0) {
        return "";
    }

    return `
        <div class="patch-impact-panel">
            ${retained.map(impact => `
                <article class="patch-impact-card">
                    <div class="table-stack">
                        <strong>${escapeHtml(`${impact.classLabel}${impact.buildLabel ? ` / ${impact.buildLabel}` : ""}`)}</strong>
                        <span class="table-inline-note">${escapeHtml(`${impact.adoptionExpectation ?? "Adoption unchanged"} | ${impact.confidence} confidence`)}</span>
                    </div>
                    <div class="attribute-pill-list">${buildPatchImpactPills([impact], 8)}</div>
                    ${impact.notes ? `<p class="table-inline-note">${escapeHtml(impact.notes)}</p>` : ""}
                </article>
            `).join("")}
        </div>
    `;
}

function buildAnalysisClassPlayerRow(player) {
    const playerNote = player.displayName && !stringEqualsIgnoreCase(player.displayName, player.account)
        ? `Most-played character: ${player.displayName}`
        : null;
    const fightImpactDetail = getAverageFightImpactDetail(player);

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
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(fightImpactDetail.value)}</strong>
                    <span class="table-inline-note">${escapeHtml(fightImpactDetail.note)}</span>
                </div>
            </td>
            <td>${buildStripCorruptStack(player.averageCorruptsPerFight, player.stripCorruptPercent)}</td>
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
    const impactTrends = classRow.characterImpactTrends ?? [];
    ensureAnalysisClassImpactTrendSelection(classRow, impactTrends);
    const selectedIds = selectedAnalysisClassImpactTrendIds ?? new Set();
    const fightCoverageNote = buildFightCoverageNote(classRow);
    const fightCoveragePills = buildDemandAdjustedLanePills(classRow.fightCoverageLanes, "averageCoverageScore", "Fight Coverage");

    container.className = "analysis-player-detail";
    container.innerHTML = `
        <div class="section-heading">
            <div>
                <h3>${escapeHtml(classRow.classLabel)}</h3>
                <p>${escapeHtml(`${classRow.sampleCount} selected class samples, ${formatNumber(classRow.totalSampleCountAll ?? classRow.sampleCount)} total, across ${classRow.distinctAccounts} accounts. ${topPlayerCopy}`)}</p>
            </div>
        </div>
        ${buildAnalysisImpactTrendCard("class", "Character Performance", impactTrends, selectedIds)}
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
                        <span class="analysis-character-pill">${escapeHtml(`Performance ${formatNumber(classRow.contributionScore, 1)}`)}</span>
                        <span class="analysis-character-pill">${escapeHtml(`${formatNumber(classRow.averageCorruptsPerFight, 1)} corrupts/fight`)}</span>
                        <span class="analysis-character-pill">${escapeHtml(`${formatPercent(classRow.stripCorruptPercent)} strip corrupt`)}</span>
                        ${fightCoverageNote ? `<span class="analysis-character-pill">${escapeHtml(`Fight Coverage ${formatNumber(classRow.averageFightCoverageScore, 1)}/100`)}</span>` : ""}
                    </div>
                </div>
                <p class="analysis-character-lead">${escapeHtml(`Observed contribution leaned across ${classRow.classLabel}'s strongest lanes in the filtered fights.`)}</p>
                <p class="analysis-character-copy">${escapeHtml(`Weighted lane score averaged ${formatPercent(classRow.averageWeightedLaneScore)} across ${classRow.sampleCount} class appearances.`)}</p>
                ${fightCoverageNote ? `<p class="analysis-character-copy">${escapeHtml(`${fightCoverageNote}. This is separate from the raw lane capability cards below.`)}</p>${fightCoveragePills}` : ""}
                <div class="analysis-character-lane-grid">
                    ${(classRow.laneContributions ?? []).length
                        ? classRow.laneContributions.map(lane => buildCharacterLaneCard(lane, classRow.sampleCount)).join("")
                        : `<article class="analysis-character-lane-card"><p class="analysis-character-copy">No lane contribution cards were retained for this class.</p></article>`}
                </div>
            </article>
        </div>
        ${buildPatchImpactDetail(classRow.patchImpacts)}
        <div class="section-heading">
            <div>
                <h3>Players on ${escapeHtml(classRow.classLabel)}</h3>
                <p>Sorted by overall class performance.</p>
            </div>
        </div>
        <div class="table-shell table-shell-scroll table-shell-analysis-players">
            <table class="data-table">
                <thead>
                    <tr>
                        <th><button class="sort-header" type="button" data-analysis-class-player-sort="player">Player</button></th>
                        <th><button class="sort-header" type="button" data-analysis-class-player-sort="appearances">Appearances</button></th>
                        <th><button class="sort-header" type="button" data-analysis-class-player-sort="record">Record</button></th>
                        <th><button class="sort-header" type="button" data-analysis-class-player-sort="performance">Performance</button></th>
                        <th><button class="sort-header" type="button" data-analysis-class-player-sort="fightImpact">Fight Impact</button></th>
                        <th><button class="sort-header" type="button" data-analysis-class-player-sort="corrupts">Corrupts</button></th>
                        <th><button class="sort-header" type="button" data-analysis-class-player-sort="lanefit">Lane fit</button></th>
                    </tr>
                </thead>
                <tbody>
                    ${(sortedPlayers.length
                        ? sortedPlayers.map(buildAnalysisClassPlayerRow).join("")
                        : `<tr><td colspan="7">No player rows were retained for this class.</td></tr>`)}
                </tbody>
            </table>
        </div>
    `;

    updateAnalysisClassPlayerSortHeaders();
    hideAnalysisImpactTrendTooltip("class");
}

function renderAnalysisClasses(snapshot) {
    const body = document.querySelector("#analysis-classes-body");
    const sortedClasses = getSortedAnalysisClasses(snapshot);
    updateAnalysisClassSortHeaders();

    if (sortedClasses.length === 0) {
        selectedAnalysisClassLabel = null;
        body.innerHTML = `<tr><td colspan="7">No class rows matched the current filters.</td></tr>`;
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

function buildAnalysisEnemyClassRow(row) {
    const iconMarkup = row.icon
        ? `<img class="analysis-enemy-class-icon" src="${escapeHtml(row.icon)}" alt="" loading="lazy" referrerpolicy="no-referrer" onerror="this.style.display='none'">`
        : `<span class="analysis-enemy-class-icon" aria-hidden="true"></span>`;

    return `
        <tr>
            <td>
                <div class="analysis-enemy-class-cell">
                    ${iconMarkup}
                    <strong>${escapeHtml(row.classLabel ?? "Unknown class")}</strong>
                </div>
            </td>
            <td><strong>${escapeHtml(formatNumber(row.totalCount ?? 0))}</strong></td>
            <td>${escapeHtml(formatNumber(row.fightCount ?? 0))}</td>
            <td>${escapeHtml(formatNumber(row.threatScore, 1))}</td>
            <td>${escapeHtml(formatNumber(row.averageDps, 1))}</td>
            <td>${escapeHtml(formatNumber(row.bestDps, 1))}</td>
            <td>${escapeHtml(formatNumber(row.averageStripsPerMinute, 1))}</td>
            <td>${escapeHtml(formatNumber(row.bestStripsPerMinute, 1))}</td>
            <td>${escapeHtml(formatNumber(row.damageBurstTopCount ?? 0))}</td>
            <td>${escapeHtml(formatNumber(row.stripBurstTopCount ?? 0))}</td>
        </tr>
    `;
}

function getSortedAnalysisEnemies(snapshot) {
    const rows = [...(snapshot.topEnemyClasses ?? [])];
    rows.sort((left, right) => {
        const primary = compareFightBrowserValues(
            getAnalysisEnemySortValue(left, analysisEnemySortState.key),
            getAnalysisEnemySortValue(right, analysisEnemySortState.key));
        if (primary !== 0) {
            return analysisEnemySortState.direction === "asc" ? primary : -primary;
        }

        return Number(right.totalCount ?? 0) - Number(left.totalCount ?? 0)
            || Number(right.fightCount ?? 0) - Number(left.fightCount ?? 0)
            || compareFightBrowserValues(String(left.classLabel ?? "").toLowerCase(), String(right.classLabel ?? "").toLowerCase());
    });

    return rows;
}

function renderAnalysisEnemies(snapshot) {
    const rows = getSortedAnalysisEnemies(snapshot);
    const summary = document.querySelector("#analysis-enemies-summary");
    const body = document.querySelector("#analysis-enemies-body");
    const totalCount = rows.reduce((sum, row) => sum + Number(row.totalCount ?? 0), 0);
    const performanceCount = rows.reduce((sum, row) => sum + Number(row.performanceSampleCount ?? 0), 0);
    updateAnalysisEnemySortHeaders();

    if (summary) {
        if (rows.length === 0) {
            summary.textContent = "No enemy class summaries matched the current filters.";
        } else if (performanceCount > 0) {
            summary.textContent = `${rows.length} enemy classes across ${formatNumber(totalCount)} total faced class entries. Performance metrics cover ${formatNumber(performanceCount)} enemy player instances.`;
        } else {
            summary.textContent = `${rows.length} enemy classes across ${formatNumber(totalCount)} total faced class entries. Reparse with the updated EI parser to populate DPS, strips, burst, and threat metrics.`;
        }
    }

    if (!body) {
        return;
    }

    body.innerHTML = rows.length > 0
        ? rows.map(buildAnalysisEnemyClassRow).join("")
        : `<tr><td colspan="10">No enemy class summaries matched the current filters.</td></tr>`;
}

function renderAnalysisLanes(snapshot) {
    const laneSelectionContainer = document.querySelector("#analysis-lane-selection");
    const orderedLanes = getOrderedAnalysisLanes(snapshot.topLanes ?? []);
    getSelectedAnalysisLaneRows(snapshot);
    setInnerHtml(
        "#analysis-lanes-body",
        (orderedLanes.length
            ? orderedLanes.map(buildAnalysisLaneRow).join("")
            : `<tr><td colspan="6">No lane summaries matched the current filters.</td></tr>`));

    if (laneSelectionContainer) {
        laneSelectionContainer.innerHTML = orderedLanes.length > 0
            ? orderedLanes.map(lane => buildAnalysisLaneSelectionToggle(lane, isAnalysisLaneSelected(snapshot, lane.laneKey))).join("")
            : `<span class="table-inline-note">No lane toggles are available for this selection.</span>`;
    }

    if (orderedLanes.length === 0) {
        selectedAnalysisLaneKeys = [];
        renderAnalysisLaneDetail(snapshot);
        return;
    }

    setInnerHtml(
        "#analysis-lanes-body",
        orderedLanes.map(buildAnalysisLaneRow).join(""));
    renderAnalysisLaneDetail(snapshot);
}

function buildAnalysisLaneRow(lane) {
    const rowClasses = ["is-clickable"];
    if ((selectedAnalysisLaneKeys ?? []).some(key => stringEqualsIgnoreCase(key, lane.laneKey))) {
        rowClasses.push("is-selected");
    }

    return `
        <tr class="${rowClasses.join(" ")}" data-lane-key="${escapeHtml(lane.laneKey)}">
            <td><strong>${escapeHtml(getAnalysisLaneDisplayLabel(lane))}</strong></td>
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

function buildLaneDetailPlayerRow(player, showCorrupts = false) {
    const playerNote = player.displayName && !stringEqualsIgnoreCase(player.displayName, player.account)
        ? `Most-played character: ${player.displayName}`
        : null;
    const laneCoverageNote = Number(player.lane?.selectedLaneCount ?? 1) > 1
        ? `${player.lane.matchedLaneCount}/${player.lane.selectedLaneCount} lanes | ${formatPercent(player.lane.overallSharePercent)} overall share`
        : `${formatPercent(player.lane.overallSharePercent)} overall share`;
    const laneFitNote = Number(player.lane?.selectedLaneCount ?? 1) > 1
        ? `${formatPercent(player.lane.appearanceRatePercent)} avg appearance | ${formatPercent(player.lane.coverageRatePercent)} lane coverage`
        : `${formatPercent(player.lane.appearanceRatePercent)} appearance | ${player.lane.rateBand ?? "Unrated"}`;

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
                    <strong>${escapeHtml(formatPercent(player.rankingScore ?? player.impactScore))}</strong>
                    <span class="table-inline-note">${escapeHtml(laneCoverageNote)}</span>
                </div>
            </td>
            ${showCorrupts ? `<td>${buildStripCorruptPercentStack(player.lane?.stripCorruptPercent)}</td>` : ""}
            <td>${escapeHtml(laneFitNote)}</td>
        </tr>
    `;
}

function buildLaneDetailClassRow(classRow, showCorrupts = false) {
    const usesPreventionValue = classRow.rankingMetric === PREVENTION_VALUE_METRIC_KEY;
    const rankingValue = Number(classRow.rankingScore ?? classRow.impactScore ?? 0);
    const laneFitNote = Number(classRow.lane?.selectedLaneCount ?? 1) > 1
        ? `${formatPercent(classRow.lane.appearanceRatePercent)} avg appearance | ${formatPercent(classRow.lane.coverageRatePercent)} lane coverage`
        : `${formatPercent(classRow.lane.appearanceRatePercent)} appearance | ${formatPercent(classRow.lane.overallSharePercent)} overall share`;
    const impactNote = usesPreventionValue
        ? `${formatNumber(rankingValue, 0)} avg prevention/player | ${formatPercent(classRow.lane.overallSharePercent)} overall share`
        : Number(classRow.lane?.selectedLaneCount ?? 1) > 1
        ? `${formatPercent(classRow.lane.overallSharePercent)} overall share | ${classRow.lane.matchedLaneCount}/${classRow.lane.selectedLaneCount} lanes`
        : `${formatPercent(classRow.lane.overallSharePercent)} overall share`;
    const performanceValue = usesPreventionValue
        ? formatNumber(rankingValue, 0)
        : formatPercent(rankingValue);

    return `
        <tr>
            <td><strong>${escapeHtml(classRow.classLabel)}</strong></td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(String(classRow.sampleCount))}</strong>
                    <span class="table-inline-note">${escapeHtml("Qualified class samples")}</span>
                </div>
            </td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(performanceValue)}</strong>
                    <span class="table-inline-note">${escapeHtml(impactNote)}</span>
                </div>
            </td>
            ${showCorrupts ? `<td>${buildStripCorruptPercentStack(classRow.lane?.stripCorruptPercent)}</td>` : ""}
            <td>${escapeHtml(formatPercent(classRow.lane.overallStrengthPercent))}</td>
            <td>${escapeHtml(laneFitNote)}</td>
            <td>${escapeHtml(classRow.topPlayerDisplayName ?? "-")}</td>
        </tr>
    `;
}

function renderAnalysisLaneDetail(snapshot) {
    const container = document.querySelector("#analysis-lane-detail");
    const selectedLaneRows = getSelectedAnalysisLaneRows(snapshot);
    if (selectedLaneRows.length === 0) {
        container.innerHTML = "";
        return;
    }

    const laneLabel = selectedLaneRows.map(getAnalysisLaneDisplayLabel).join(" + ");
    const isCombinedLaneView = selectedLaneRows.length > 1;
    const showStripCorrupts = selectedLaneRows.length === 1 && isAnalysisStripLane(selectedLaneRows[0].laneKey);
    const combinedLane = {
        laneLabel,
        samples: selectedLaneRows.reduce((sum, lane) => sum + Number(lane.samples ?? 0), 0),
        averageStrengthPercent: Math.round((selectedLaneRows.reduce((sum, lane) => sum + Number(lane.averageStrengthPercent ?? 0), 0) / Math.max(1, selectedLaneRows.length)) * 10) / 10,
        appearanceRatePercent: Math.round((selectedLaneRows.reduce((sum, lane) => sum + Number(lane.appearanceRatePercent ?? 0), 0) / Math.max(1, selectedLaneRows.length)) * 10) / 10,
        averageSharePercent: Math.round((selectedLaneRows.reduce((sum, lane) => sum + Number(lane.averageSharePercent ?? 0), 0) / Math.max(1, selectedLaneRows.length)) * 10) / 10,
        averageCorruptsPerAppearance: Math.round((selectedLaneRows.reduce((sum, lane) => sum + Number(lane.averageCorruptsPerAppearance ?? 0), 0) / Math.max(1, selectedLaneRows.length)) * 10) / 10,
        stripCorruptPercent: Math.round((selectedLaneRows.reduce((sum, lane) => sum + Number(lane.stripCorruptPercent ?? 0), 0) / Math.max(1, selectedLaneRows.length)) * 10) / 10,
        evidenceLine: isCombinedLaneView
            ? "Selected lanes are scored together; missing lanes count against the combined fit so balanced cards rise."
            : selectedLaneRows[0].evidenceLine
    };
    const lanePlayers = isCombinedLaneView
        ? getQualifiedCombinedLanePlayers(snapshot, selectedLaneRows)
        : getQualifiedLanePlayers(snapshot, selectedLaneRows[0].laneKey);
    const laneClasses = isCombinedLaneView
        ? getQualifiedCombinedLaneClasses(snapshot, selectedLaneRows)
        : getQualifiedLaneClasses(snapshot, selectedLaneRows[0].laneKey);
    const classPerformanceHeader = laneClasses.some(classRow => classRow.rankingMetric === PREVENTION_VALUE_METRIC_KEY)
        ? "Avg prevention"
        : "Performance";
    const laneThresholdCopy = isCombinedLaneView
        ? `Best players require at least ${MINIMUM_PLAYER_TABLE_FIGHTS} total fights plus ${MINIMUM_LANE_FILTER_APPEARANCES} total appearances across the selected lanes for their Guild Wars ID. Best classes come from the qualified class list and are scored on the selected lanes together.`
        : `Best players require at least ${MINIMUM_PLAYER_TABLE_FIGHTS} total fights plus ${MINIMUM_LANE_FILTER_APPEARANCES} total ${getAnalysisLaneDisplayLabel(selectedLaneRows[0])} appearances for their Guild Wars ID. Best classes come from the qualified class list.`;
    const laneProfileCopy = isCombinedLaneView
        ? `${formatPercent(combinedLane.averageSharePercent)} average share across the selected lanes.`
        : `${formatPercent(combinedLane.averageSharePercent)} average share across retained lane samples.`;

    container.className = "analysis-player-detail";
    container.innerHTML = `
        <div class="section-heading">
            <div>
                <h3>${escapeHtml(laneLabel)}</h3>
                <p>${escapeHtml(laneThresholdCopy)}</p>
            </div>
        </div>
        <div class="analysis-character-grid">
            <article class="analysis-character-card">
                <div class="analysis-character-header">
                    <div>
                        <strong>${escapeHtml(laneLabel)}</strong>
                        <div class="table-inline-note">${escapeHtml(isCombinedLaneView ? "Combined lane profile across the current analysis filter" : "Lane profile across the current analysis filter")}</div>
                    </div>
                    <div class="analysis-character-meta">
                        <span class="analysis-character-pill">${escapeHtml(`${combinedLane.samples} samples`)}</span>
                        <span class="analysis-character-pill">${escapeHtml(`${formatPercent(combinedLane.averageStrengthPercent)} strength`)}</span>
                        ${showStripCorrupts ? `<span class="analysis-character-pill">${escapeHtml(`${formatPercent(combinedLane.stripCorruptPercent)} strip corrupt`)}</span>` : ""}
                        <span class="analysis-character-pill">${escapeHtml(`${formatPercent(combinedLane.appearanceRatePercent)} fight coverage`)}</span>
                        ${isCombinedLaneView ? `<span class="analysis-character-pill">${escapeHtml(`${selectedLaneRows.length} lanes selected`)}</span>` : ""}
                    </div>
                </div>
                <p class="analysis-character-copy">${escapeHtml(laneProfileCopy)}</p>
                <p class="table-inline-note">${escapeHtml(combinedLane.evidenceLine ?? "No extra lane evidence was retained for this lane.")}</p>
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
                            <th>Performance</th>
                            ${showStripCorrupts ? "<th>Corrupts</th>" : ""}
                            <th>Lane fit</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${lanePlayers.length
                            ? lanePlayers.map(player => buildLaneDetailPlayerRow(player, showStripCorrupts)).join("")
                            : `<tr><td colspan="${showStripCorrupts ? 6 : 5}">No players met the current lane thresholds.</td></tr>`}
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
                            <th>${escapeHtml(classPerformanceHeader)}</th>
                            ${showStripCorrupts ? "<th>Corrupts</th>" : ""}
                            <th>Lane strength</th>
                            <th>Lane fit</th>
                            <th>Top player</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${laneClasses.length
                            ? laneClasses.map(classRow => buildLaneDetailClassRow(classRow, showStripCorrupts)).join("")
                            : `<tr><td colspan="${showStripCorrupts ? 7 : 6}">No classes met the current lane thresholds.</td></tr>`}
                    </tbody>
                </table>
            </div>
        </div>
    `;

    setActiveAnalysisLaneDetailTab(activeAnalysisLaneDetailTab);
}

function getAnalysisBoonTrendColor(index) {
    return ANALYSIS_BOON_TREND_COLORS[index % ANALYSIS_BOON_TREND_COLORS.length];
}

function getAnalysisBoonTrendIds(trends) {
    return (trends ?? []).map(trend => String(trend.id ?? ""));
}

function ensureAnalysisBoonTrendSelection(trends) {
    const ids = getAnalysisBoonTrendIds(trends);
    if (selectedAnalysisBoonTrendIds === null) {
        selectedAnalysisBoonTrendIds = new Set(ids);
        return;
    }

    const validIds = new Set(ids);
    selectedAnalysisBoonTrendIds = new Set(
        Array.from(selectedAnalysisBoonTrendIds)
            .filter(id => validIds.has(id)));
}

function buildAnalysisBoonTrendSummary(trends) {
    const nightCount = new Set(
        (trends ?? [])
            .flatMap(trend => trend.points ?? [])
            .map(point => point.dateKey)
            .filter(Boolean)).size;
    const selectedCount = Array.from(selectedAnalysisBoonTrendIds ?? []).length;
    const boonCount = trends?.length ?? 0;

    if (boonCount === 0 || nightCount === 0) {
        return "No boon trend data matched the current filters.";
    }

    return `${selectedCount} of ${boonCount} boons selected | ${nightCount} ${nightCount === 1 ? "night" : "nights"}`;
}

function buildAnalysisBoonTrendLegend(trends) {
    return (trends ?? []).map((trend, index) => {
        const id = String(trend.id ?? "");
        const checked = selectedAnalysisBoonTrendIds?.has(id) ? "checked" : "";
        const color = getAnalysisBoonTrendColor(index);
        return `
            <label class="analysis-boon-trend-option ${checked ? "is-active" : ""}">
                <input type="checkbox" data-analysis-boon-trend-id="${escapeHtml(id)}" ${checked}>
                <span class="analysis-boon-trend-swatch" style="--boon-color: ${escapeHtml(color)}"></span>
                <span>${escapeHtml(trend.name ?? id)}</span>
            </label>
        `;
    }).join("");
}

function getAnalysisBoonTrendNights(trends) {
    const nightMap = new Map();
    (trends ?? []).forEach(trend => {
        (trend.points ?? []).forEach(point => {
            if (!point?.dateKey || nightMap.has(point.dateKey)) {
                return;
            }

            nightMap.set(point.dateKey, point.dateLabel ?? point.dateKey);
        });
    });

    return Array.from(nightMap.entries())
        .sort((left, right) => left[0].localeCompare(right[0]))
        .map(([dateKey, dateLabel]) => ({ dateKey, dateLabel }));
}

function getAnalysisBoonTrendAxisIndexes(length) {
    if (length <= 0) {
        return [];
    }

    if (length <= 6) {
        return Array.from({ length }, (_, index) => index);
    }

    const indexes = new Set([0, length - 1]);
    const targetCount = 6;
    for (let index = 1; index < targetCount - 1; index += 1) {
        indexes.add(Math.round(index * (length - 1) / (targetCount - 1)));
    }

    return Array.from(indexes).sort((left, right) => left - right);
}

function buildAnalysisBoonTrendPath(trend, nightIndex, chart) {
    const pointByNight = new Map((trend.points ?? []).map(point => [point.dateKey, point]));
    let started = false;

    return Array.from(nightIndex.entries())
        .map(([dateKey, index]) => {
            const point = pointByNight.get(dateKey);
            if (!point) {
                started = false;
                return "";
            }

            const x = chart.xForIndex(index);
            const y = chart.yForValue(point.averageCoverage);
            const command = started ? "L" : "M";
            started = true;
            return `${command} ${x} ${y}`;
        })
        .filter(Boolean)
        .join(" ");
}

function buildAnalysisBoonTrendProvidersHtml(boon, providers) {
    if (!providers?.length) {
        return `<div class="table-inline-note">No provider data for this night.</div>`;
    }

    return providers.slice(0, 3).map((provider, index) => {
        const value = boon?.stackBased
            ? `${formatNumber(provider.averageGeneration, 1)} gen${provider.averageGenerationPresence != null ? ` | ${formatPercent(provider.averageGenerationPresence)} presence` : ""}`
            : formatPercent(provider.averageGeneration);
        return `
            <div class="analysis-boon-trend-provider">
                <span>${escapeHtml(String(index + 1))}. ${escapeHtml(provider.label ?? "Unknown provider")}</span>
                <strong>${escapeHtml(value)}</strong>
            </div>
        `;
    }).join("");
}

function buildAnalysisBoonTrendTooltipHtml(boon, point) {
    const stackLine = boon?.stackBased && point.averageStacks != null
        ? `<div class="table-inline-note">${escapeHtml(`Average stacks ${formatNumber(point.averageStacks, 1)}`)}</div>`
        : "";

    return `
        <strong>${escapeHtml(boon?.name ?? "Boon")}</strong>
        <div class="analysis-boon-trend-tooltip-value">${escapeHtml(formatPercent(point.averageCoverage))}</div>
        <div class="table-inline-note">${escapeHtml(`${point.dateLabel ?? point.dateKey} | ${point.fightCount} ${point.fightCount === 1 ? "fight" : "fights"}`)}</div>
        ${stackLine}
        <div class="analysis-boon-trend-provider-list">
            ${buildAnalysisBoonTrendProvidersHtml(boon, point.topProviders ?? [])}
        </div>
    `;
}

function findAnalysisBoonTrendPoint(boonId, dateKey) {
    const boon = (currentAnalysisSnapshot?.boonTrends ?? [])
        .find(trend => String(trend.id ?? "") === String(boonId ?? ""));
    const point = boon?.points?.find(item => item.dateKey === dateKey) ?? null;
    return { boon, point };
}

function showAnalysisBoonTrendTooltip(event) {
    const target = event.target.closest("[data-analysis-boon-trend-point]");
    const tooltip = document.querySelector("#analysis-boon-trend-tooltip");
    const wrap = document.querySelector(".analysis-boon-trend-chart-wrap");
    if (!target || !tooltip || !wrap) {
        return;
    }

    const { boon, point } = findAnalysisBoonTrendPoint(target.dataset.boonId, target.dataset.dateKey);
    if (!boon || !point) {
        return;
    }

    tooltip.innerHTML = buildAnalysisBoonTrendTooltipHtml(boon, point);
    tooltip.hidden = false;

    const wrapRect = wrap.getBoundingClientRect();
    const targetRect = target.getBoundingClientRect();
    const targetX = targetRect.left + (targetRect.width / 2) - wrapRect.left;
    const targetY = targetRect.top - wrapRect.top;
    const tooltipWidth = tooltip.offsetWidth || 260;
    const tooltipHeight = tooltip.offsetHeight || 150;
    const left = Math.max(8, Math.min(wrapRect.width - tooltipWidth - 8, targetX + 12));
    const top = Math.max(8, Math.min(wrapRect.height - tooltipHeight - 8, targetY - tooltipHeight - 10));

    tooltip.style.left = `${left}px`;
    tooltip.style.top = `${top}px`;
}

function hideAnalysisBoonTrendTooltip() {
    const tooltip = document.querySelector("#analysis-boon-trend-tooltip");
    if (!tooltip) {
        return;
    }

    tooltip.hidden = true;
    tooltip.innerHTML = "";
}

function buildAnalysisBoonTrendChart(trends) {
    const nights = getAnalysisBoonTrendNights(trends);
    const selectedTrends = (trends ?? [])
        .filter(trend => selectedAnalysisBoonTrendIds?.has(String(trend.id ?? "")));

    if (nights.length === 0) {
        return `<div class="analysis-boon-trend-empty">No boon trend data available.</div>`;
    }

    if (selectedTrends.length === 0) {
        return `<div class="analysis-boon-trend-empty">No boons selected.</div>`;
    }

    const width = 760;
    const height = 260;
    const plot = { left: 44, top: 18, right: 18, bottom: 38 };
    const plotWidth = width - plot.left - plot.right;
    const plotHeight = height - plot.top - plot.bottom;
    const nightIndex = new Map(nights.map((night, index) => [night.dateKey, index]));
    const chart = {
        xForIndex: index => Math.round((plot.left + (nights.length <= 1 ? plotWidth / 2 : index * plotWidth / (nights.length - 1))) * 100) / 100,
        yForValue: value => Math.round((plot.top + (100 - Math.max(0, Math.min(100, Number(value) || 0))) * plotHeight / 100) * 100) / 100
    };
    const gridValues = [100, 75, 50, 25, 0];
    const axisIndexes = getAnalysisBoonTrendAxisIndexes(nights.length);

    const gridMarkup = gridValues.map(value => {
        const y = chart.yForValue(value);
        return `
            <line class="grid-line" x1="${plot.left}" y1="${y}" x2="${width - plot.right}" y2="${y}"></line>
            <text class="axis-label y-axis-label" x="${plot.left - 10}" y="${y + 4}">${escapeHtml(String(value))}</text>
        `;
    }).join("");

    const axisMarkup = axisIndexes.map(index => {
        const night = nights[index];
        const x = chart.xForIndex(index);
        return `<text class="axis-label x-axis-label" x="${x}" y="${height - 12}">${escapeHtml(night.dateLabel)}</text>`;
    }).join("");

    const seriesMarkup = selectedTrends.map(trend => {
        const trendIndex = (trends ?? []).findIndex(item => String(item.id ?? "") === String(trend.id ?? ""));
        const color = getAnalysisBoonTrendColor(Math.max(0, trendIndex));
        const path = buildAnalysisBoonTrendPath(trend, nightIndex, chart);
        const points = (trend.points ?? []).map(point => {
            if (!nightIndex.has(point.dateKey)) {
                return "";
            }

            const x = chart.xForIndex(nightIndex.get(point.dateKey));
            const y = chart.yForValue(point.averageCoverage);
            const label = `${trend.name} ${point.dateLabel}: ${formatPercent(point.averageCoverage)}`;
            return `
                <circle class="analysis-boon-trend-point"
                    cx="${x}"
                    cy="${y}"
                    r="4.2"
                    style="--boon-color: ${escapeHtml(color)}"
                    tabindex="0"
                    role="img"
                    aria-label="${escapeHtml(label)}"
                    data-analysis-boon-trend-point
                    data-boon-id="${escapeHtml(String(trend.id ?? ""))}"
                    data-date-key="${escapeHtml(point.dateKey)}"></circle>
            `;
        }).join("");

        return `
            ${path ? `<path class="analysis-boon-trend-line" d="${escapeHtml(path)}" style="--boon-color: ${escapeHtml(color)}"></path>` : ""}
            ${points}
        `;
    }).join("");

    return `
        <svg class="analysis-boon-trend-chart" viewBox="0 0 ${width} ${height}" preserveAspectRatio="xMidYMid meet" aria-hidden="false">
            ${gridMarkup}
            ${axisMarkup}
            ${seriesMarkup}
        </svg>
    `;
}

function renderAnalysisBoonTrends(snapshot) {
    const trends = snapshot.boonTrends ?? [];
    ensureAnalysisBoonTrendSelection(trends);

    const summary = document.querySelector("#analysis-boon-trend-summary");
    if (summary) {
        summary.textContent = buildAnalysisBoonTrendSummary(trends);
    }

    setInnerHtml("#analysis-boon-trend-legend", buildAnalysisBoonTrendLegend(trends));
    setInnerHtml("#analysis-boon-trend-chart", buildAnalysisBoonTrendChart(trends));
    hideAnalysisBoonTrendTooltip();
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
    renderAnalysisBoonTrends(snapshot);

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

function resetAnalysisPanelScroll(tabKey) {
    const panel = document.querySelector(`[data-analysis-panel="${tabKey}"]`);
    if (!panel) {
        return;
    }

    panel.scrollTop = 0;
    panel.querySelectorAll(".table-shell-scroll, .analysis-split-detail").forEach(element => {
        element.scrollTop = 0;
    });

    const panelRect = panel.getBoundingClientRect();
    const workbench = document.querySelector(".analysis-workbench");
    const workbenchRect = workbench?.getBoundingClientRect();
    const workbenchBottom = workbenchRect && workbenchRect.bottom > 0 && workbenchRect.top < window.innerHeight
        ? workbenchRect.bottom
        : 0;
    const targetViewportTop = workbenchBottom > 0 ? workbenchBottom + 12 : 12;
    const scrollDelta = panelRect.top - targetViewportTop;

    if (Math.abs(scrollDelta) > 1) {
        window.scrollTo({
            top: Math.max(0, window.scrollY + scrollDelta),
            left: window.scrollX,
            behavior: "auto"
        });
    }
}

function setActiveAnalysisTab(tabKey, options = {}) {
    const { resetScroll = false } = options;
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

    if (resetScroll) {
        requestAnimationFrame(() => resetAnalysisPanelScroll(tabKey));
    }

    if (tabKey === "comp-helper" && currentAnalysisSnapshot) {
        renderAnalysisCompHelper(currentAnalysisSnapshot);
    } else if (tabKey === "players" && currentAnalysisSnapshot) {
        renderAnalysisPlayers(currentAnalysisSnapshot);
    }
}

function renderAnalysisLoading(message = "Loading analysis...") {
    document.querySelector("#analysis-summary").textContent = message;
    setInnerHtml("#analysis-overview-cards", "");
    setInnerHtml("#analysis-pillar-outcome-card", "");
    document.querySelector("#analysis-trend-summary").textContent = message;
    setInnerHtml("#analysis-trend-delta-grid", "");
    setInnerHtml("#analysis-chart-grid", "");
    document.querySelector("#analysis-burst-trend-summary").textContent = message;
    setInnerHtml("#analysis-burst-comparison-controls", "");
    setInnerHtml("#analysis-burst-comparison-chart", "");
    setInnerHtml("#analysis-burst-chart-grid", "");
    setInnerHtml("#analysis-mitigation-card", "");
    document.querySelector("#analysis-differences-summary").textContent = message;
    setInnerHtml("#analysis-differences-top-signals", "");
    ["score", "lane", "attribute", "boon", "class", "enemy"].forEach(key => {
        setInnerHtml(`#analysis-differences-${key}-body`, `<tr><td colspan="5">${escapeHtml(message)}</td></tr>`);
    });
    setInnerHtml("#analysis-scope-list", buildAnalysisScopeStaticChip(message));
    document.querySelector("#analysis-players-summary").textContent = message;
    setInnerHtml("#analysis-players-body", `<tr><td colspan="8">${escapeHtml(message)}</td></tr>`);
    setInnerHtml("#analysis-player-detail", "");
    setInnerHtml("#analysis-classes-body", `<tr><td colspan="7">${escapeHtml(message)}</td></tr>`);
    setInnerHtml("#analysis-class-detail", "");
    document.querySelector("#analysis-enemies-summary").textContent = message;
    setInnerHtml("#analysis-enemies-body", `<tr><td colspan="10">${escapeHtml(message)}</td></tr>`);
    setInnerHtml("#analysis-lanes-body", `<tr><td colspan="6">${escapeHtml(message)}</td></tr>`);
    setInnerHtml("#analysis-lane-selection", "");
    setInnerHtml("#analysis-lane-detail", "");
    document.querySelector("#analysis-boon-trend-summary").textContent = message;
    setInnerHtml("#analysis-boon-trend-legend", "");
    setInnerHtml("#analysis-boon-trend-chart", "");
    setInnerHtml("#analysis-boon-trend-tooltip", "");
    setInnerHtml("#analysis-boons-body", `<tr><td colspan="6">${escapeHtml(message)}</td></tr>`);
    setInnerHtml("#analysis-boon-detail", "");
    document.querySelector("#analysis-comp-helper-summary").textContent = message;
    setInnerHtml("#analysis-comp-helper-locks", "");
    setInnerHtml("#analysis-comp-helper-candidates-body", `<tr><td colspan="7">${escapeHtml(message)}</td></tr>`);
    setInnerHtml("#analysis-comp-helper-suggestions", "");
}

function renderAnalysisError(message) {
    renderAnalysisLoading(message);
}

function renderAnalysis(snapshot) {
    currentAnalysisSnapshot = snapshot;
    resetAnalysisPlayerDetailState();

    document.querySelector("#analysis-summary").textContent =
        `${snapshot.scope.filteredFightCount} fights selected from ${snapshot.scope.totalImportedFights} imported fights. Win rate ${formatPercent(snapshot.scope.winRatePercent)}.`;

    renderAnalysisFilterOptions(snapshot);
    setInnerHtml("#analysis-overview-cards", buildAnalysisOverviewCards(snapshot));
    renderAnalysisDifferences(snapshot);
    renderAnalysisPillarOutcomeComparison(snapshot);
    renderAnalysisCharts(snapshot);
    renderAnalysisBurstTrends(snapshot);
    renderAnalysisMitigation(snapshot);

    setInnerHtml("#analysis-scope-list", buildAnalysisScopeChipListHtml(snapshot));

    renderAnalysisPlayers(snapshot);
    renderAnalysisClasses(snapshot);
    renderAnalysisEnemies(snapshot);
    renderAnalysisLanes(snapshot);
    renderAnalysisBoons(snapshot);
    renderAnalysisCompHelperPlaceholder("Open Comp Helper to load candidate player cards for the current filter.");

    setActiveAnalysisTab(activeAnalysisTab);
}

async function ensureAnalysisLoaded(force = false) {
    if (!force && currentAnalysisSnapshot) {
        return currentAnalysisSnapshot;
    }

    if (analysisLoadPromise) {
        return analysisLoadPromise;
    }

    renderAnalysisLoading(force ? "Refreshing analysis..." : "Loading analysis...");
    analysisLoadPromise = loadAnalysis(getAnalysisFiltersFromUi())
        .then(snapshot => {
            renderAnalysis(snapshot);
            return snapshot;
        })
        .catch(error => {
            renderAnalysisError(error instanceof Error ? error.message : String(error));
            throw error;
        })
        .finally(() => {
            analysisLoadPromise = null;
        });

    return analysisLoadPromise;
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
                    <a href="${escapeHtml(buildFightDossierUrl(fight.fightId))}">Summary</a>
                    ${fight.htmlReportUrl ? `<a href="${escapeHtml(fight.htmlReportUrl)}" target="_blank" rel="noopener">HTML</a>` : ""}
                    ${fight.parserConsoleLogUrl ? `<a href="${escapeHtml(fight.parserConsoleLogUrl)}" target="_blank" rel="noopener">Parser log</a>` : ""}
                </div>
            </td>
        </tr>
    `;
}

function buildManageCommanderGroups(snapshot) {
    const groupsByCommander = new Map();
    const fights = snapshot?.fightBrowser?.fights ?? [];
    for (const fight of fights) {
        const commanders = fight.fightIndex?.commanderDisplayNames ?? [];
        for (const commanderValue of commanders) {
            const commander = String(commanderValue ?? "").trim();
            if (!commander) {
                continue;
            }

            if (!groupsByCommander.has(commander.toLocaleLowerCase())) {
                groupsByCommander.set(commander.toLocaleLowerCase(), {
                    commander,
                    fightCount: 0
                });
            }

            groupsByCommander.get(commander.toLocaleLowerCase()).fightCount += 1;
        }
    }

    return [...groupsByCommander.values()]
        .sort((left, right) =>
            right.fightCount - left.fightCount
            || left.commander.localeCompare(right.commander, undefined, { sensitivity: "base" }));
}

function renderManageCommanderFights(snapshot) {
    const body = document.querySelector("#manage-commanders-body");
    const summary = document.querySelector("#manage-commanders-summary");
    if (!body || !summary) {
        return;
    }

    const commanderGroups = buildManageCommanderGroups(snapshot);
    const fightAssignmentCount = commanderGroups.reduce((total, group) => total + group.fightCount, 0);
    summary.textContent = commanderGroups.length > 0
        ? `${formatNumber(commanderGroups.length)} commander(s), ${formatNumber(fightAssignmentCount)} commander fight assignment(s).`
        : "No commander-tagged fights are stored.";

    if (commanderGroups.length === 0) {
        body.innerHTML = `
            <tr>
                <td colspan="3">No commander-tagged fights are stored yet.</td>
            </tr>
        `;
        return;
    }

    body.innerHTML = commanderGroups
        .map(group => `
            <tr>
                <td>
                    <span class="table-title">${escapeHtml(group.commander)}</span>
                </td>
                <td><strong>${escapeHtml(formatNumber(group.fightCount))}</strong></td>
                <td>
                    <div class="table-actions">
                        <button class="action-link action-link-button danger-button manage-commander-delete-button" type="button" data-manage-commander-delete="${escapeHtml(group.commander)}" data-manage-commander-fights="${escapeHtml(String(group.fightCount))}">Delete</button>
                    </div>
                </td>
            </tr>
        `)
        .join("");

    syncManageControls();
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
    const commanderValue = document.querySelector("#fight-browser-commander").value || "";
    const startDateValue = document.querySelector("#fight-browser-start-date").value || "";
    const endDateValue = document.querySelector("#fight-browser-end-date").value || "";
    const outcomeValue = document.querySelector("#fight-browser-outcome").value;
    const classFilters = getFightBrowserClassFiltersFromUi();
    const patchScope = document.querySelector("#fight-browser-patch-scope")?.value ?? "all";
    const attributeFilters = getSelectedAttributeFilterValues("#fight-browser-attribute-filters");
    const patchEras = snapshot.patchMetadata?.patchEras ?? [];

    let fights = snapshot.fightBrowser.fights;

    if (outcomeValue !== "all") {
        fights = fights.filter(fight => getOutcomeCode(fight) === outcomeValue);
    }

    if (commanderValue) {
        fights = fights.filter(fight =>
            (fight.fightIndex?.commanderDisplayNames ?? [])
                .some(commander => stringEqualsIgnoreCase(commander, commanderValue)));
    }

    if (startDateValue || endDateValue) {
        fights = fights.filter(fight => {
            const fightDate = getFightLocalDateString(fight);
            if (!fightDate) {
                return !startDateValue && !endDateValue;
            }

            if (startDateValue && fightDate < startDateValue) {
                return false;
            }

            if (endDateValue && fightDate > endDateValue) {
                return false;
            }

            return true;
        });
    }

    fights = fights
        .filter(fight => matchesFightSideClassFilters(fight, "squad", classFilters.squadIncludeClasses, classFilters.squadExcludeClasses))
        .filter(fight => matchesFightSideClassFilters(fight, "enemy", classFilters.enemyIncludeClasses, classFilters.enemyExcludeClasses))
        .filter(fight => matchesPatchScope(fight, patchScope, patchEras))
        .filter(fight => matchesFightAttributeFilters(fight, attributeFilters));

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
            <td data-fight-shape-diagnostics ${showFightShapeDiagnostics ? "" : "hidden"}>${buildFightShapeBrowserCell(fightIndex?.fightShape)}</td>
            <td>${escapeHtml(String(squadCount))}</td>
            <td>${escapeHtml(String(enemyCount))}</td>
            <td>${buildAttributePills(fight.attributes)}</td>
            <td>
                <div class="table-actions">
                    <a href="${escapeHtml(buildFightDossierUrl(fight.fightId))}">Summary</a>
                    ${fight.htmlReportUrl ? `<a href="${escapeHtml(fight.htmlReportUrl)}" target="_blank" rel="noopener">HTML</a>` : ""}
                    ${fight.parserConsoleLogUrl ? `<a href="${escapeHtml(fight.parserConsoleLogUrl)}" target="_blank" rel="noopener">Parser log</a>` : ""}
                </div>
            </td>
        </tr>
    `;
}

function buildFightShapeBrowserCell(shape) {
    if (!shape) {
        return `<span class="yi-shape-chip yi-shape-chip-missing">No probe</span>`;
    }

    if (!shape.available) {
        return `<span class="yi-shape-chip yi-shape-chip-unknown" title="${escapeHtml(shape.detectionLabel ?? "No conservative cleanup boundary detected.")}">Unknown</span>`;
    }

    const cleanupStart = shape.cleanupStartTimeMs != null
        ? formatSeconds(Number(shape.cleanupStartTimeMs) / 1000, 0)
        : "n/a";
    const side = formatShapeSide(shape.cleanupSide);
    const title = [
        shape.detectionLabel,
        `cleanup ${cleanupStart}`,
        `side ${side}`,
        `confidence ${formatPercent(Number(shape.confidence ?? 0) * 100, 0)}`,
        shape.rules?.length ? `rules ${shape.rules.join(", ")}` : null
    ].filter(Boolean).join(" | ");

    return `<span class="yi-shape-chip yi-shape-chip-detected" title="${escapeHtml(title)}">${escapeHtml(side)} ${escapeHtml(cleanupStart)}</span>`;
}

function formatShapeSide(side) {
    if (side === "squad") {
        return "Squad";
    }
    if (side === "enemy") {
        return "Enemy";
    }
    return "Unknown";
}

function buildFightShapeDiagnosticsHtml(shape) {
    if (!shape) {
        return `<p class="workspace-note">No fight-shape diagnostic payload is stored for this fight.</p>`;
    }

    const cleanupStart = shape.cleanupStartTimeMs != null ? formatSeconds(Number(shape.cleanupStartTimeMs) / 1000, 1) : "n/a";
    const competitiveDuration = formatSeconds(Number(shape.competitiveDurationMs ?? 0) / 1000, 1);
    const cleanupDuration = formatSeconds(Number(shape.cleanupDurationMs ?? 0) / 1000, 1);
    const headline = shape.available
        ? `${formatShapeSide(shape.cleanupSide)} cleanup candidate at ${cleanupStart}`
        : "No conservative cleanup boundary";
    const confidence = formatPercent(Number(shape.confidence ?? 0) * 100, 0);

    return `
        <div class="yi-shape-headline">
            <strong>${escapeHtml(headline)}</strong>
            <span>${escapeHtml(confidence)} confidence</span>
        </div>
        <div class="yi-shape-grid">
            ${buildYiShapeMetric("Competitive", competitiveDuration)}
            ${buildYiShapeMetric("Cleanup", cleanupDuration)}
            ${buildYiShapeMetric("Cleanup share", formatPercent(shape.cleanupPercent))}
            ${buildYiShapeMetric("Losing side", formatShapeSide(shape.losingSide))}
        </div>
        <div class="yi-shape-rules">${buildYiShapeRules(shape.rules ?? [])}</div>
        ${buildFightShapeBestCandidateHtml(shape)}
        ${buildFightShapeStateHtml(shape)}
        <p class="workspace-note yi-shape-note">${escapeHtml(shape.detectionLabel ?? "")}</p>
    `;
}

function buildYiShapeMetric(label, value) {
    return `
        <div class="yi-shape-metric">
            <span>${escapeHtml(label)}</span>
            <strong>${escapeHtml(value)}</strong>
        </div>
    `;
}

function buildYiShapeRules(rules) {
    if (!rules.length) {
        return `<span class="yi-shape-rule">no rules</span>`;
    }
    return rules.map(rule => `<span class="yi-shape-rule">${escapeHtml(rule.replaceAll("_", " "))}</span>`).join("");
}

function buildFightShapeStateHtml(shape) {
    const squad = shape.squadAtCleanupStart;
    const enemy = shape.enemyAtCleanupStart;
    const before = shape.atCleanupStart;
    const after = shape.afterCleanupStart;
    if (!squad && !enemy && !before && !after) {
        return "";
    }

    return `
        <div class="yi-shape-state-grid">
            ${buildYiShapeSideState("Squad at boundary", squad)}
            ${buildYiShapeSideState("Enemy at boundary", enemy)}
            ${buildYiShapeEventState("Before boundary", before)}
            ${buildYiShapeEventState("After boundary", after)}
        </div>
    `;
}

function buildFightShapeBestCandidateHtml(shape) {
    if (!shape?.bestCandidateTimeMs && !shape?.bestCandidateReason && !shape?.bestCandidateDetail) {
        return "";
    }
    const candidateTime = shape.bestCandidateTimeMs != null ? formatSeconds(Number(shape.bestCandidateTimeMs) / 1000, 1) : "n/a";
    const candidateSide = formatShapeSide(shape.bestCandidateCleanupSide);
    const candidateConfidence = formatPercent(Number(shape.bestCandidateConfidence ?? 0) * 100, 0);
    return `
        <div class="yi-shape-best-candidate">
            <strong>${escapeHtml(`Best candidate: ${candidateTime} (${candidateSide}, ${candidateConfidence})`)}</strong>
            <span>${escapeHtml(shape.bestCandidateReason ?? "")}</span>
            <span>${escapeHtml(shape.bestCandidateDetail ?? "")}</span>
        </div>
    `;
}

function buildYiShapeSideState(title, state) {
    if (!state) {
        return "";
    }
    return `
        <div class="yi-shape-state-card">
            <strong>${escapeHtml(title)}</strong>
            <span>${escapeHtml(`${formatNumber(state.active)} active / ${formatNumber(state.known)} known`)}</span>
            <span>${escapeHtml(`${formatNumber(getFightShapeCombatCapable(state))} combat-capable`)}</span>
            <span>${escapeHtml(`${formatNumber(state.downed)} downed, ${formatNumber(state.deadOrDc)} dead/DC`)}</span>
            <span>${escapeHtml(`${formatNumber(state.removed)} removed, ${formatNumber(state.farFromFight)} far, ${formatNumber(state.unobserved)} unobserved`)}</span>
        </div>
    `;
}

function getFightShapeCombatCapable(state) {
    if (!state) {
        return "";
    }
    return Number(state.active ?? 0) + Number(state.downed ?? 0);
}

function buildYiShapeEventState(title, snapshot) {
    if (!snapshot) {
        return "";
    }
    return `
        <div class="yi-shape-state-card">
            <strong>${escapeHtml(title)}</strong>
            <span>${escapeHtml(`Downs S/E: ${formatNumber(snapshot.squadMembersDowned)} / ${formatNumber(snapshot.enemyPlayersDowned)}`)}</span>
            <span>${escapeHtml(`Kills S/E: ${formatNumber(snapshot.squadKillsSecured)} / ${formatNumber(snapshot.enemyKillsSecured)}`)}</span>
            <span>${escapeHtml(`Damage S/E: ${formatNumber(snapshot.squadDamage)} / ${formatNumber(snapshot.enemyDamage)}`)}</span>
        </div>
    `;
}

function buildFightShapeExportTsv(fights) {
    const headers = [
        "fightId",
        "fightTime",
        "fightName",
        "sourceFile",
        "commander",
        "outcome",
        "score",
        "durationSec",
        "squadPlayers",
        "enemyPlayers",
        "shapeStatus",
        "cleanupSide",
        "losingSide",
        "confidence",
        "cleanupStartSec",
        "competitiveDurationSec",
        "cleanupDurationSec",
        "cleanupPercent",
        "rules",
        "detectionLabel",
        "bestCandidateSec",
        "bestCandidateSide",
        "bestCandidateConfidence",
        "bestCandidateReason",
        "bestCandidateDetail",
        "squadActiveAtBoundary",
        "squadCombatCapableAtBoundary",
        "squadKnownAtBoundary",
        "squadDownedAtBoundary",
        "squadDeadOrDcAtBoundary",
        "squadRemovedAtBoundary",
        "squadFarFromFightAtBoundary",
        "squadUnobservedAtBoundary",
        "enemyActiveAtBoundary",
        "enemyCombatCapableAtBoundary",
        "enemyKnownAtBoundary",
        "enemyDownedAtBoundary",
        "enemyDeadOrDcAtBoundary",
        "enemyRemovedAtBoundary",
        "enemyFarFromFightAtBoundary",
        "enemyUnobservedAtBoundary",
        "beforeSquadDowns",
        "beforeEnemyDowns",
        "beforeSquadKills",
        "beforeEnemyKills",
        "beforeSquadRecoveries",
        "beforeEnemyRecoveries",
        "beforeSquadDamage",
        "beforeEnemyDamage",
        "afterSquadDowns",
        "afterEnemyDowns",
        "afterSquadKills",
        "afterEnemyKills",
        "afterSquadRecoveries",
        "afterEnemyRecoveries",
        "afterSquadDamage",
        "afterEnemyDamage",
        "analystSchema"
    ];
    const rows = fights.map(fight => buildFightShapeExportRow(fight));
    return [
        headers.join("\t"),
        ...rows.map(row => row.map(formatTsvCell).join("\t"))
    ].join("\n");
}

function buildFightShapeExportRow(fight) {
    const fightIndex = fight.fightIndex ?? {};
    const shape = fightIndex.fightShape;
    const squadState = shape?.squadAtCleanupStart;
    const enemyState = shape?.enemyAtCleanupStart;
    const before = shape?.atCleanupStart;
    const after = shape?.afterCleanupStart;

    return [
        fight.fightId,
        fightIndex.timeStartStandard ?? fightIndex.timeStart,
        fightIndex.fightName,
        fight.sourceFileName,
        (fightIndex.commanderDisplayNames ?? []).join(", "),
        fightIndex.outcome?.displayLabel ?? fightIndex.outcome?.outcomeCode,
        fightIndex.execution?.overallScore,
        millisecondsToSeconds(fightIndex.durationMilliseconds),
        fightIndex.squadPlayerCount,
        fightIndex.enemyPlayerCount ?? fightIndex.enemyTargetCount,
        getFightShapeStatus(shape),
        shape?.cleanupSide,
        shape?.losingSide,
        shape?.confidence,
        millisecondsToSeconds(shape?.cleanupStartTimeMs),
        millisecondsToSeconds(shape?.competitiveDurationMs),
        millisecondsToSeconds(shape?.cleanupDurationMs),
        shape?.cleanupPercent,
        (shape?.rules ?? []).join("|"),
        shape?.detectionLabel,
        millisecondsToSeconds(shape?.bestCandidateTimeMs),
        shape?.bestCandidateCleanupSide,
        shape?.bestCandidateConfidence,
        shape?.bestCandidateReason,
        shape?.bestCandidateDetail,
        squadState?.active,
        getFightShapeCombatCapable(squadState),
        squadState?.known,
        squadState?.downed,
        squadState?.deadOrDc,
        squadState?.removed,
        squadState?.farFromFight,
        squadState?.unobserved,
        enemyState?.active,
        getFightShapeCombatCapable(enemyState),
        enemyState?.known,
        enemyState?.downed,
        enemyState?.deadOrDc,
        enemyState?.removed,
        enemyState?.farFromFight,
        enemyState?.unobserved,
        before?.squadMembersDowned,
        before?.enemyPlayersDowned,
        before?.squadKillsSecured,
        before?.enemyKillsSecured,
        before?.squadRecoveries,
        before?.enemyRecoveries,
        before?.squadDamage,
        before?.enemyDamage,
        after?.squadMembersDowned,
        after?.enemyPlayersDowned,
        after?.squadKillsSecured,
        after?.enemyKillsSecured,
        after?.squadRecoveries,
        after?.enemyRecoveries,
        after?.squadDamage,
        after?.enemyDamage,
        fightIndex.analystSchemaVersion
    ];
}

function getFightShapeStatus(shape) {
    if (!shape) {
        return "missing";
    }
    return shape.available ? "detected" : "unknown";
}

function millisecondsToSeconds(value) {
    if (value == null || value === "") {
        return "";
    }
    const numericValue = Number(value);
    if (!Number.isFinite(numericValue)) {
        return "";
    }
    return Math.round(numericValue / 100) / 10;
}

function formatTsvCell(value) {
    return String(value ?? "")
        .replaceAll("\t", " ")
        .replaceAll("\r", " ")
        .replaceAll("\n", " ")
        .trim();
}

function renderFightShapeExport(filteredFights) {
    const textarea = document.querySelector("#fight-shape-export-textarea");
    const summary = document.querySelector("#fight-shape-export-summary");
    const copyButton = document.querySelector("#fight-shape-export-copy");
    if (!textarea || !summary || !copyButton) {
        return;
    }

    if (!showFightShapeDiagnostics) {
        textarea.value = "";
        summary.textContent = "";
        copyButton.disabled = true;
        return;
    }

    const detectedCount = filteredFights.filter(fight => fight.fightIndex?.fightShape?.available).length;
    const unknownCount = filteredFights.filter(fight => {
        const shape = fight.fightIndex?.fightShape;
        return shape && !shape.available;
    }).length;
    const missingCount = filteredFights.filter(fight => !fight.fightIndex?.fightShape).length;
    textarea.value = buildFightShapeExportTsv(filteredFights);
    summary.textContent = `${filteredFights.length} filtered fights | ${detectedCount} detected | ${unknownCount} unknown | ${missingCount} missing payload.`;
    copyButton.disabled = filteredFights.length === 0;
}

async function copyFightShapeExport() {
    const textarea = document.querySelector("#fight-shape-export-textarea");
    const copyButton = document.querySelector("#fight-shape-export-copy");
    if (!textarea || !copyButton) {
        return;
    }

    textarea.focus();
    textarea.select();
    try {
        await navigator.clipboard.writeText(textarea.value);
    } catch {
        document.execCommand("copy");
    }

    const previousText = copyButton.textContent;
    copyButton.textContent = "Copied";
    window.setTimeout(() => {
        copyButton.textContent = previousText;
    }, 1200);
}

function buildFightBrowserTopBurstRow(entry) {
    const burst = entry.burst;
    const fight = entry.fight;
    const fightTime = formatDate(fight.fightIndex?.timeStartStandard ?? fight.fightIndex?.timeStart);
    const logFile = fight.sourceFileName ?? fight.fightId;
    const burstTime = burst.timeLabel || formatSeconds(Number(burst.time ?? 0) / 1000, 3);

    return `
        <tr>
            <td>${escapeHtml(fightTime || "-")}</td>
            <td>
                <div class="table-stack">
                    <strong>${escapeHtml(logFile)}</strong>
                    <span class="table-inline-note">${escapeHtml(fight.fightId)}</span>
                    ${fight.htmlReportUrl
                        ? `<span class="table-inline-note"><a href="${escapeHtml(fight.htmlReportUrl)}" target="_blank" rel="noopener">HTML</a></span>`
                        : ""}
                </div>
            </td>
            <td>${escapeHtml(burstTime)}</td>
            <td>${buildFightBrowserTopBurstActorCell(burst.topPressure, "damage")}</td>
            <td>${escapeHtml(formatNumber(burst.damage))}</td>
            <td>${buildFightBrowserTopBurstActorCell(burst.topStrips, "strips")}</td>
            <td>${escapeHtml(formatNumber(burst.strips))}</td>
            <td>${escapeHtml(formatNumber(burst.downs))}</td>
            <td>${escapeHtml(formatNumber(burst.kills))}</td>
        </tr>
    `;
}

function renderFightBrowserTopBursts(filteredFights) {
    const panel = document.querySelector("#fight-browser-top-bursts-panel");
    const summary = document.querySelector("#fight-browser-top-bursts-summary");
    const body = document.querySelector("#fight-browser-top-bursts-body");
    const toggle = document.querySelector("#fight-browser-top-bursts-toggle");

    toggle.textContent = showFightBrowserTopBursts ? "Hide Top Bursts" : "Top Bursts";
    panel.hidden = !showFightBrowserTopBursts;

    const burstState = buildFightBrowserTopBurstEntries(filteredFights);

    if (burstState.displayedEntries.length === 0) {
        summary.textContent = filteredFights.length === 0
            ? "No fights matched the current Fight Browser filters."
            : "No retained top-burst snapshots matched the current Fight Browser filters.";
        body.innerHTML = `
            <tr>
                <td colspan="9">${escapeHtml(filteredFights.length === 0
                    ? "No fights matched the current filters."
                    : "No retained top-burst snapshots are available for the current filter set. Rebuild the catalog after reparsing with the current parser if these fights predate burst export.")}</td>
            </tr>
        `;
        return;
    }

    summary.textContent = burstState.allCount > 500
        ? `Showing top ${burstState.displayedEntries.length} of 500 retained bursts from ${filteredFights.length} filtered fights.`
        : `Showing top ${burstState.displayedEntries.length} of ${burstState.retainedEntries.length} retained bursts from ${filteredFights.length} filtered fights.`;

    body.innerHTML = burstState.displayedEntries
        .map(buildFightBrowserTopBurstRow)
        .join("");
}

function renderFightBrowser(snapshot, selectedFightId) {
    const summary = document.querySelector("#fight-browser-summary");
    const body = document.querySelector("#fight-browser-body");
    syncFightShapeDiagnosticsVisibility();
    renderFightBrowserFilterOptions(snapshot.fightBrowser.fights);
    renderPatchScopeOptions(
        "#fight-browser-patch-scope",
        snapshot.patchMetadata?.patchEras ?? [],
        document.querySelector("#fight-browser-patch-scope")?.value ?? "all");
    renderAttributeFilterBox(
        "#fight-browser-attribute-filters",
        snapshot.fightAttributeDefinitions ?? [],
        getSelectedAttributeFilterValues("#fight-browser-attribute-filters"));
    renderFightBrowserClassFilters(
        collectClassOptionsFromFights(snapshot.fightBrowser.fights),
        getFightBrowserClassFiltersFromUi());
    const filteredFights = applyFightBrowserFilters(snapshot);
    updateFightBrowserSortHeaders();
    renderFightBrowserTopBursts(filteredFights);
    renderFightShapeExport(filteredFights);

    summary.textContent = snapshot.fightBrowser.failedCount > 0
        ? `Showing ${filteredFights.length} of ${snapshot.fightBrowser.totalCount} imported fights. ${snapshot.fightBrowser.failedCount} parser-failed rows are kept out of the fight browser.`
        : `Showing ${filteredFights.length} of ${snapshot.fightBrowser.totalCount} imported fights.`;

    if (filteredFights.length === 0) {
        body.innerHTML = `
            <tr>
                <td colspan="${showFightShapeDiagnostics ? 10 : 9}">No fights matched the current filters.</td>
            </tr>
        `;
        return;
    }

    body.innerHTML = filteredFights
        .map(fight => buildFightBrowserRow(fight, selectedFightId))
        .join("");
}

function toggleFightBrowserTopBursts() {
    showFightBrowserTopBursts = !showFightBrowserTopBursts;
    if (currentDashboardSnapshot) {
        renderFightBrowser(currentDashboardSnapshot, getSelectedFightId());
    }
}

function renderFightDossier(detail) {
    const panel = document.querySelector("#fight-dossier-panel");
    panel.hidden = false;
    setActiveAppTab("fight-browser");

    const fightIndex = detail.fightIndex;
    const outcome = fightIndex?.outcome;
    const execution = fightIndex?.execution;
    const executionContext = execution?.context;
    const executionOutcome = execution?.outcome;
    const executionPillars = (execution?.pillars ?? [])
        .filter(pillar => pillar?.pillarId !== "resilience-stabilization");
    const fightShape = fightIndex?.fightShape;
    const squadSide = fightIndex?.squadSide;
    const enemySide = fightIndex?.enemySide;
    const commanderSummary = fightIndex?.commanderSummary;
    const players = fightIndex?.players ?? [];

    document.querySelector("#dossier-title").textContent = fightIndex?.fightName ?? detail.sourceFileName;
    document.querySelector("#dossier-back-link").setAttribute("href", getDashboardUrl("fight-browser"));

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
        executionOutcome?.crowdControlDataAvailable ? `Incoming CC: ${formatNumber(executionOutcome.incomingCrowdControl)}` : null,
        executionOutcome?.crowdControlDataAvailable ? `Outgoing CC: ${formatNumber(executionOutcome.outgoingCrowdControl)}` : null,
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
        typeof executionContext?.enemyMovementScore === "number"
            ? `Enemy movement: ${executionContext.enemyMovementScore} / 100${executionContext.enemyMovementScoreLabel ? ` (${executionContext.enemyMovementScoreLabel})` : ""}`
            : executionContext?.enemyMovementScoreLabel ? `Enemy movement: ${executionContext.enemyMovementScoreLabel}` : null,
        executionContext?.enemyMovementScoreDetail ?? null,
        executionContext?.threeWayDetected
            ? `Fight type: ${executionContext.threeWayLabel || "3-way"}`
            : null,
        executionContext?.threeWayDetail ?? null,
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

    setInnerHtml("#dossier-context-list", buildDossierFactListHtml(overviewItems));
    setInnerHtml("#dossier-participants-list", buildDossierFactListHtml(participantItems));
    setInnerHtml("#dossier-outcome-list", buildDossierFactListHtml(outcomeItems, "No outcome detail is stored for this fight yet."));
    setInnerHtml("#dossier-execution-list", buildDossierFactListHtml(executionItems));
    setInnerHtml("#dossier-confidence-list", buildDossierFactListHtml(confidenceItems));
    setInnerHtml("#dossier-commander-focus-list", buildDossierFactListHtml(commanderFocusItems));
    setInnerHtml("#dossier-parser-list", buildDossierFactListHtml(parserItems));
    setInnerHtml("#dossier-commanders-list", buildDossierFactListHtml(commanders));
    setInnerHtml("#dossier-extensions-list", buildDossierFactListHtml(extensions));
    setInnerHtml("#dossier-fight-shape", buildFightShapeDiagnosticsHtml(fightShape));

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
    setInnerHtml("#dossier-enemy-classes", buildDossierClassListHtml(enemySide?.classes ?? []));
    setInnerHtml(
        "#dossier-scoreboard-body",
        scoreboardRows || `
            <tr>
                <td colspan="3">No symmetric outcome or side totals are stored for this fight yet.</td>
            </tr>
        `);
    setInnerHtml(
        "#dossier-pillar-grid",
        executionPillars.length
            ? executionPillars.map(buildPillarCard).join("")
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
    setInnerHtml("#dossier-fight-shape", "");
    setInnerHtml("#dossier-enemy-classes", "");
}

function renderFightDossierError(fightId, error) {
    setActiveAppTab("fight-browser");
    document.querySelector("#fight-dossier-panel").hidden = false;
    document.querySelector("#dossier-title").textContent = "Fight Summary";
    document.querySelector("#dossier-subtitle").textContent = `Could not load ${fightId}.`;
    document.querySelector("#dossier-back-link").setAttribute("href", getDashboardUrl("fight-browser"));
    setInnerHtml("#dossier-context-list", "");
    setInnerHtml("#dossier-participants-list", "");
    setInnerHtml("#dossier-outcome-list", "");
    setInnerHtml("#dossier-execution-list", "");
    setInnerHtml("#dossier-confidence-list", "");
    setInnerHtml("#dossier-fight-shape", "");
    setInnerHtml("#dossier-commander-focus-list", "");
    setInnerHtml("#dossier-parser-list", buildTagListHtml([error instanceof Error ? error.message : String(error)]));
    setInnerHtml("#dossier-commanders-list", "");
    setInnerHtml("#dossier-extensions-list", "");
    setInnerHtml("#dossier-artifact-links", "");
    setInnerHtml("#dossier-squad-stats", "");
    setInnerHtml("#dossier-enemy-stats", "");
    setInnerHtml("#dossier-enemy-classes", "");
    setInnerHtml("#dossier-scoreboard-body", "");
    setInnerHtml("#dossier-pillar-grid", "");
    setInnerHtml("#dossier-player-body", "");
}

function renderBatchStatus(result, success) {
    const container = document.querySelector("#batch-status");
    const state = String(result?.state ?? "").toLowerCase();
    const isAnalysisWarmup = state === "warming-analysis";
    const isRunning = state === "running" || isAnalysisWarmup;
    const isBlocked = state === "blocked";
    const isCatalogRebuild = Boolean(result?.resetCatalog);
    const totalCount = Number(result?.discoveredCount ?? 0);
    const completedCount = Number(result?.completedCount ?? 0);
    const maxParallelism = Number(result?.maxParallelism ?? 0);
    const title = isAnalysisWarmup
        ? "Recalculating Analysis"
        : isRunning
        ? `${isCatalogRebuild ? "Rebuilding catalog" : "Parsing"} ${completedCount} / ${totalCount || "?"} logs`
        : isBlocked
            ? "Manage busy"
            : success
                ? (isCatalogRebuild ? "Catalog rebuild complete" : "Batch complete")
                : "Needs attention";
    const statusClass = isRunning || isBlocked
        ? "status status-neutral"
        : success
            ? "status status-ok"
            : "status status-error";
    const progressBits = [];
    if (isCatalogRebuild) {
        progressBits.push(`<span class="pill">catalog rebuild</span>`);
    }
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
                <td>${item.fightId ? `<a href="${escapeHtml(buildFightDossierUrl(item.fightId))}">${escapeHtml(item.fightId)}</a>` : "-"}</td>
                <td>${escapeHtml(item.parserStatus ?? "-")}</td>
                <td>${escapeHtml(item.message)}</td>
            </tr>
        `)
        .join("");
}

function stopManageActivityRefresh() {
    if (manageActivityRefreshHandle) {
        window.clearTimeout(manageActivityRefreshHandle);
        manageActivityRefreshHandle = null;
    }
}

function scheduleManageActivityRefresh(snapshot) {
    stopManageActivityRefresh();

    if (snapshot?.manageActivity?.uploadRunning && !snapshot.manageActivity.parseRunning) {
        manageActivityRefreshHandle = window.setTimeout(() => {
            if (!activeBatchJobId && !batchStatusPollHandle) {
                void main();
            }
        }, 1500);
    }
}

function syncSharedManageState(snapshot) {
    const manageActivity = snapshot?.manageActivity;
    if (!manageActivity) {
        return;
    }

    if (manageActivity.parseRunning && manageActivity.activeBatchJob?.jobId) {
        lastBatchResult = manageActivity.activeBatchJob;
        renderBatchStatus(lastBatchResult, true);
        renderBatchResults(lastBatchResult);

        if (!activeBatchJobId && !batchStatusPollHandle) {
            startBatchJobPolling(manageActivity.activeBatchJob.jobId);
        }

        return;
    }

    if (manageActivity.uploadRunning) {
        if (!activeBatchJobId && !batchStatusPollHandle) {
            renderBatchStatus(
                {
                    state: "blocked",
                    message: manageActivity.summary,
                    resetCatalog: false,
                    maxParallelism: 0,
                    discoveredCount: 0,
                    completedCount: 0,
                    importedCount: 0,
                    skippedCount: 0,
                    excludedCount: 0,
                    failedCount: 0
                },
                true);
        }
        return;
    }

    if (!lastBatchResult && !activeBatchJobId && !batchStatusPollHandle) {
        document.querySelector("#batch-status").textContent = DEFAULT_BATCH_STATUS_MESSAGE;
        renderBatchResults(null);
    }
}

function syncFightShapeDiagnosticsVisibility() {
    const toggle = document.querySelector("#manage-show-fight-shape-diagnostics");
    if (toggle) {
        toggle.checked = showFightShapeDiagnostics;
    }

    document.querySelectorAll("[data-fight-shape-diagnostics]").forEach(element => {
        element.hidden = !showFightShapeDiagnostics;
    });
}

function setFightShapeDiagnosticsVisible(isVisible) {
    showFightShapeDiagnostics = Boolean(isVisible);
    localStorage.setItem(FIGHT_SHAPE_DIAGNOSTICS_KEY, showFightShapeDiagnostics ? "true" : "false");
    // The cleanup probe TSV and fight-shape dossier/browser widgets are intentionally retained for future tuning,
    // but they stay hidden unless this local Manage-page switch is enabled.
    syncFightShapeDiagnosticsVisibility();
    if (currentDashboardSnapshot) {
        renderFightBrowser(currentDashboardSnapshot, getSelectedFightId());
    }
}

function syncManageControls() {
    const selectedMode = document.querySelector("#directory-mode")?.value || "new-only";
    const hasConfiguredDirectory = isBatchDirectoryAvailable(selectedMode);
    const hasPendingDirectory = isConfiguredLogDirectoryAvailable();
    const parseButton = document.querySelector("#directory-button");
    const uploadInput = document.querySelector("#log-file-upload-input");
    const uploadButton = document.querySelector("#log-file-upload-button");
    const dropzone = document.querySelector("#log-file-dropzone");
    const resetButton = document.querySelector("#manage-reset-button");
    const commanderDeleteButtons = document.querySelectorAll(".manage-commander-delete-button");
    const sharedManageActivity = currentDashboardSnapshot?.manageActivity ?? null;
    const sharedParseRunning = Boolean(sharedManageActivity?.parseRunning);
    const sharedUploadRunning = Boolean(sharedManageActivity?.uploadRunning);
    const parseBusy = parseButton?.dataset.busy === "true";
    const disableManageActions = parseBusy || logFileUploadBusy || manageResetBusy || manageCommanderDeleteBusy;

    if (parseButton) {
        parseButton.disabled = disableManageActions || sharedParseRunning || sharedUploadRunning || !hasConfiguredDirectory;
    }

    if (uploadInput) {
        uploadInput.disabled = disableManageActions || !hasPendingDirectory;
    }

    if (uploadButton) {
        uploadButton.disabled = disableManageActions || !hasPendingDirectory;
    }

    if (dropzone) {
        const isDisabled = disableManageActions || !hasPendingDirectory;
        dropzone.classList.toggle("is-disabled", isDisabled);
        dropzone.setAttribute("aria-disabled", isDisabled ? "true" : "false");
    }

    if (resetButton) {
        resetButton.disabled = disableManageActions || sharedParseRunning || sharedUploadRunning;
    }

    commanderDeleteButtons.forEach(button => {
        button.disabled = disableManageActions || sharedParseRunning || sharedUploadRunning;
    });
}

function setLogFileUploadBusy(isBusy) {
    logFileUploadBusy = isBusy;
    const button = document.querySelector("#log-file-upload-button");
    if (button) {
        button.textContent = isBusy ? "Uploading..." : "Select files";
    }

    syncManageControls();
}

function setManageResetBusy(isBusy) {
    manageResetBusy = isBusy;
    const button = document.querySelector("#manage-reset-button");
    if (button) {
        button.textContent = isBusy ? "Resetting..." : "Delete logs and reset state";
    }

    syncManageControls();
}

function setManageCommanderDeleteBusy(isBusy) {
    manageCommanderDeleteBusy = isBusy;
    syncManageControls();
}

function renderLogFileUploadResult(result, success) {
    const container = document.querySelector("#log-file-upload-status");
    const summary = document.querySelector("#log-file-upload-summary");
    const items = Array.isArray(result?.items) ? result.items : [];
    const statusClass = success ? "status status-ok" : "status status-error";
    const title = success ? "Files added" : "Needs attention";

    if (!result) {
        container.textContent = "No files have been added to the pending upload directory in this browser session yet.";
        summary.textContent = "No files added in this browser session yet.";
        return;
    }

    const counts = [];
    if (typeof result.uploadedCount === "number") {
        counts.push(`<span class="pill">${escapeHtml(`${result.uploadedCount} uploaded`)}</span>`);
    }
    if (typeof result.savedCount === "number") {
        counts.push(`<span class="pill">${escapeHtml(`${result.savedCount} saved`)}</span>`);
    }
    if (typeof result.skippedCount === "number" && result.skippedCount > 0) {
        counts.push(`<span class="pill">${escapeHtml(`${result.skippedCount} skipped`)}</span>`);
    }
    if (typeof result.failedCount === "number" && result.failedCount > 0) {
        counts.push(`<span class="pill">${escapeHtml(`${result.failedCount} failed`)}</span>`);
    }

    const itemMarkup = items.length
        ? `
            <ul class="upload-item-list">
                ${items.map(item => {
                    const itemClass = item.action === "failed"
                        ? "status status-error"
                        : (item.action === "skipped"
                            ? "status status-neutral"
                            : "status status-ok");
                    const suffix = item.savedAs && item.savedAs !== item.fileName
                        ? ` (${item.savedAs})`
                        : "";
                    return `
                        <li>
                            <span class="${itemClass}">${escapeHtml(item.action)}</span>
                            <span>${escapeHtml(`${item.fileName}${suffix} - ${item.message}`)}</span>
                        </li>
                    `;
                }).join("")}
            </ul>
        `
        : "";

    container.innerHTML = `
        <div class="batch-status-grid">
            <div class="batch-status-header">
                <div class="${statusClass}">${escapeHtml(title)}</div>
                <div class="batch-progress-row">${counts.join("")}</div>
            </div>
            <p>${escapeHtml(result.message ?? "No message returned.")}</p>
            ${itemMarkup}
        </div>
    `;

    summary.textContent = success
        ? `${formatNumber(result.savedCount ?? 0)} file(s) added to ${result.directoryPath ?? "the pending upload directory"}.`
        : (result.message ?? "No files were added.");
}

function renderLogFileUploadProgress(progress) {
    const container = document.querySelector("#log-file-upload-status");
    const summary = document.querySelector("#log-file-upload-summary");
    const processedCount = Math.max(0, Number(progress?.processedCount) || 0);
    const totalCount = Math.max(0, Number(progress?.totalCount) || 0);
    const savedCount = Math.max(0, Number(progress?.savedCount) || 0);
    const skippedCount = Math.max(0, Number(progress?.skippedCount) || 0);
    const failedCount = Math.max(0, Number(progress?.failedCount) || 0);
    const directoryPath = progress?.directoryPath ?? getConfiguredLogDirectoryPath();
    const currentFileName = progress?.currentFileName ?? "";
    const nextFileOrdinal = totalCount > 0 ? Math.min(processedCount + 1, totalCount) : 0;
    const progressPercent = totalCount > 0
        ? Math.max(0, Math.min(100, Math.round((processedCount / totalCount) * 100)))
        : 0;
    const counts = [
        `<span class="pill">${escapeHtml(`${processedCount} of ${totalCount} completed`)}</span>`,
        `<span class="pill">${escapeHtml(`${savedCount} saved`)}</span>`
    ];

    if (skippedCount > 0) {
        counts.push(`<span class="pill">${escapeHtml(`${skippedCount} skipped`)}</span>`);
    }
    if (failedCount > 0) {
        counts.push(`<span class="pill">${escapeHtml(`${failedCount} failed`)}</span>`);
    }

    container.innerHTML = `
        <div class="batch-status-grid">
            <div class="batch-status-header">
                <div class="status status-neutral">Uploading</div>
                <div class="batch-progress-row">${counts.join("")}</div>
            </div>
            <div class="upload-progress-track" aria-hidden="true">
                <div class="upload-progress-fill" style="width: ${progressPercent}%;"></div>
            </div>
            <p>${escapeHtml(progress?.message ?? `Uploading file ${nextFileOrdinal} of ${totalCount}${currentFileName ? `: ${currentFileName}` : ""}.`)}</p>
        </div>
    `;

    summary.textContent = totalCount > 0
        ? `Uploading file ${nextFileOrdinal} of ${totalCount} to ${directoryPath}${currentFileName ? `: ${currentFileName}` : ""}`
        : `Uploading files to ${directoryPath}...`;
}

function mergeLogFileUploadResult(aggregate, batchResult) {
    if (!batchResult || typeof batchResult !== "object") {
        return aggregate;
    }

    if (typeof batchResult.directoryPath === "string" && batchResult.directoryPath.trim().length > 0) {
        aggregate.directoryPath = batchResult.directoryPath;
    }

    aggregate.savedCount += Number(batchResult.savedCount) || 0;
    aggregate.skippedCount += Number(batchResult.skippedCount) || 0;

    const batchItems = Array.isArray(batchResult.items) ? batchResult.items : [];
    aggregate.failedCount += batchItems.filter(item => item?.action === "failed").length;
    aggregate.items.push(...batchItems);
    return aggregate;
}

function buildLogFileUploadCompletionMessage(result, totalCount, stoppedMessage = null) {
    const directoryPath = result.directoryPath ?? getConfiguredLogDirectoryPath() ?? "the pending upload directory";
    const messageParts = [];

    if (stoppedMessage) {
        messageParts.push(stoppedMessage);
    } else if (result.savedCount > 0) {
        messageParts.push(`Saved ${result.savedCount} of ${totalCount} uploaded files to ${directoryPath}.`);
    } else {
        messageParts.push(`No uploaded files were saved to ${directoryPath}.`);
    }

    if (result.skippedCount > 0) {
        messageParts.push(`Skipped ${result.skippedCount}.`);
    }
    if (result.failedCount > 0) {
        messageParts.push(`Failed ${result.failedCount}.`);
    }

    return messageParts.join(" ");
}

function renderManageResetResult(result, success) {
    const container = document.querySelector("#manage-reset-status");
    if (!result) {
        container.textContent = "No reset has been run in this browser session yet.";
        return;
    }

    const counts = [];
    if (typeof result.deletedLogFileCount === "number") {
        counts.push(`<span class="pill">${escapeHtml(`${result.deletedLogFileCount} logs deleted`)}</span>`);
    }
    if (typeof result.deletedFightCount === "number") {
        counts.push(`<span class="pill">${escapeHtml(`${result.deletedFightCount} fights cleared`)}</span>`);
    }
    if (typeof result.deletedHtmlReportCount === "number" && result.deletedHtmlReportCount > 0) {
        counts.push(`<span class="pill">${escapeHtml(`${result.deletedHtmlReportCount} HTML removed`)}</span>`);
    }
    if (result.deletedDatabase) {
        counts.push(`<span class="pill">database cleared</span>`);
    }

    container.innerHTML = `
        <div class="batch-status-grid">
            <div class="batch-status-header">
                <div class="${success ? "status status-ok" : "status status-error"}">${escapeHtml(success ? "Reset complete" : "Needs attention")}</div>
                <div class="batch-progress-row">${counts.join("")}</div>
            </div>
            <p>${escapeHtml(result.message ?? "No message returned.")}</p>
        </div>
    `;
}

function renderManageCommanderDeleteResult(result, success) {
    const container = document.querySelector("#manage-commanders-status");
    if (!container) {
        return;
    }

    if (!result) {
        container.textContent = "No commander fights have been deleted in this browser session yet.";
        return;
    }

    const counts = [];
    if (typeof result.matchedFightCount === "number") {
        counts.push(`<span class="pill">${escapeHtml(`${result.matchedFightCount} matched`)}</span>`);
    }
    if (typeof result.deletedFightCount === "number") {
        counts.push(`<span class="pill">${escapeHtml(`${result.deletedFightCount} fights deleted`)}</span>`);
    }
    if (typeof result.deletedLogFileCount === "number") {
        counts.push(`<span class="pill">${escapeHtml(`${result.deletedLogFileCount} logs deleted`)}</span>`);
    }
    if (typeof result.missingLogFileCount === "number" && result.missingLogFileCount > 0) {
        counts.push(`<span class="pill">${escapeHtml(`${result.missingLogFileCount} logs missing`)}</span>`);
    }
    if (typeof result.skippedLogFileCount === "number" && result.skippedLogFileCount > 0) {
        counts.push(`<span class="pill">${escapeHtml(`${result.skippedLogFileCount} logs skipped`)}</span>`);
    }
    if (typeof result.analysisRecalculationSeconds === "number" && result.analysisRecalculationSeconds > 0) {
        counts.push(`<span class="pill">${escapeHtml(`${formatNumber(result.analysisRecalculationSeconds, 1)}s analysis`)}</span>`);
    }

    container.innerHTML = `
        <div class="batch-status-grid">
            <div class="batch-status-header">
                <div class="${success ? "status status-ok" : "status status-error"}">${escapeHtml(success ? "Commander fights deleted" : "Needs attention")}</div>
                <div class="batch-progress-row">${counts.join("")}</div>
            </div>
            <p>${escapeHtml(result.message ?? "No message returned.")}</p>
        </div>
    `;
}

function buildRebuildAllConfirmationMessage(directoryPath) {
    return [
        "Are you sure you want to rebuild the catalog from the archived logs?",
        `This will clear the current stored fight catalog and reparse every supported log under:\n${directoryPath}`,
        "Pending uploads are not part of this archive walk. All stored fight artifacts will be regenerated from the archived log store."
    ].join("\n\n");
}

function buildManageResetConfirmationMessage() {
    const pendingDirectoryPath = getConfiguredLogDirectoryPath().trim();
    const archiveDirectoryPath = getArchiveLogDirectoryPath().trim();
    return [
        "Are you sure you want to delete all logs and reset the stored state?",
        pendingDirectoryPath || archiveDirectoryPath
            ? `This deletes every .evtc, .zevtc, and .zip file under:\n${pendingDirectoryPath || "(pending queue not configured)"}\n\nand\n\n${archiveDirectoryPath || "(archive log store not configured)"}`
            : "No configured pending or archive log directory is set, so this will only clear the stored fight catalog.",
        "Retained HTML reports, parser logs, and catalog entries will also be removed. This cannot be undone."
    ].join("\n\n");
}

function buildCommanderDeleteConfirmationMessage(commander, fightCount) {
    const archiveDirectoryPath = getArchiveLogDirectoryPath().trim();
    return [
        `Delete ${fightCount} stored fight${fightCount === 1 ? "" : "s"} for ${commander}?`,
        archiveDirectoryPath
            ? `This deletes the stored fight data and the associated source log files under:\n${archiveDirectoryPath}`
            : "This deletes the stored fight data and any associated source log files found in the configured log stores.",
        "A rebuild-all parse will not bring these fights back unless the source logs are restored. This cannot be undone."
    ].join("\n\n");
}

async function uploadLogFiles(fileList) {
    const files = Array.from(fileList ?? []).filter(Boolean);
    if (files.length === 0) {
        renderLogFileUploadResult({
            message: "Select or drop one or more log files first.",
            uploadedCount: 0,
            savedCount: 0,
            skippedCount: 0,
            items: []
        }, false);
        return;
    }

    if (!isConfiguredLogDirectoryAvailable()) {
        renderLogFileUploadResult({
            message: "Workspace:PendingDirectoryPath is not configured. Update appsettings.json before adding files.",
            uploadedCount: files.length,
            savedCount: 0,
            skippedCount: files.length,
            items: []
        }, false);
        return;
    }

    const directoryPath = getConfiguredLogDirectoryPath();
    const aggregateResult = {
        directoryPath,
        uploadedCount: files.length,
        savedCount: 0,
        skippedCount: 0,
        failedCount: 0,
        items: []
    };
    let processedCount = 0;

    setLogFileUploadBusy(true);
    renderLogFileUploadProgress({
        processedCount,
        totalCount: files.length,
        savedCount: 0,
        skippedCount: 0,
        failedCount: 0,
        directoryPath,
        currentFileName: files[0]?.name ?? "",
        message: `Preparing to upload file 1 of ${files.length}${files[0]?.name ? `: ${files[0].name}` : ""}.`
    });

    try {
        for (let fileIndex = 0; fileIndex < files.length; fileIndex += 1) {
            const file = files[fileIndex];
            const formData = new FormData();
            formData.append("files", file, file.name);

            renderLogFileUploadProgress({
                processedCount,
                totalCount: files.length,
                savedCount: aggregateResult.savedCount,
                skippedCount: aggregateResult.skippedCount,
                failedCount: aggregateResult.failedCount,
                directoryPath: aggregateResult.directoryPath,
                currentFileName: file.name,
                message: `Uploading file ${fileIndex + 1} of ${files.length}: ${file.name}`
            });

            const response = await fetch("/api/imports/log-directory/files", {
                method: "POST",
                body: formData
            });
            const fileResult = await readApiPayload(response);
            mergeLogFileUploadResult(aggregateResult, fileResult);
            processedCount += 1;
        }

        aggregateResult.message = buildLogFileUploadCompletionMessage(aggregateResult, files.length);
        renderLogFileUploadResult(aggregateResult, aggregateResult.savedCount > 0 && aggregateResult.failedCount === 0);
    } catch (error) {
        aggregateResult.message = buildLogFileUploadCompletionMessage(
            aggregateResult,
            files.length,
            `Upload stopped after ${processedCount} of ${files.length} files. ${error instanceof Error ? error.message : String(error)}`
        );
        renderLogFileUploadResult({
            ...aggregateResult,
            uploadedCount: files.length,
            savedCount: aggregateResult.savedCount,
            skippedCount: aggregateResult.skippedCount,
            failedCount: aggregateResult.failedCount,
            items: aggregateResult.items,
            message: aggregateResult.message
        }, false);
    } finally {
        document.querySelector("#log-file-upload-input").value = "";
        setLogFileUploadBusy(false);
    }
}

async function handleManageReset() {
    if (manageResetBusy || manageCommanderDeleteBusy) {
        return;
    }

    if (!window.confirm(buildManageResetConfirmationMessage())) {
        return;
    }

    setManageResetBusy(true);
    document.querySelector("#manage-reset-status").textContent = "Deleting logs and clearing stored fight artifacts...";

    try {
        const response = await fetch("/api/manage/reset", {
            method: "POST"
        });
        const result = await readApiPayload(response);
        renderManageResetResult(result, response.ok);

        if (response.ok) {
            lastBatchResult = null;
            stopBatchJobPolling();
            setBatchButtonBusy(false);
            document.querySelector("#batch-status").textContent = DEFAULT_BATCH_STATUS_MESSAGE;
            renderBatchResults(null);
            renderLogFileUploadResult(null);
            await main();
        }
    } catch (error) {
        renderManageResetResult({
            message: error instanceof Error ? error.message : String(error),
            deletedLogFileCount: 0,
            deletedFightCount: 0,
            deletedHtmlReportCount: 0,
            deletedDatabase: false
        }, false);
    } finally {
        setManageResetBusy(false);
    }
}

async function handleManageCommanderDelete(button) {
    if (manageCommanderDeleteBusy || !button) {
        return;
    }

    const commander = button.dataset.manageCommanderDelete ?? "";
    const fightCount = Number(button.dataset.manageCommanderFights) || 0;
    if (!commander) {
        renderManageCommanderDeleteResult({
            message: "No commander was selected.",
            matchedFightCount: 0,
            deletedFightCount: 0,
            deletedLogFileCount: 0,
            missingLogFileCount: 0,
            skippedLogFileCount: 0,
            analysisRecalculationSeconds: 0
        }, false);
        return;
    }

    if (!window.confirm(buildCommanderDeleteConfirmationMessage(commander, fightCount))) {
        return;
    }

    setManageCommanderDeleteBusy(true);
    button.textContent = "Deleting...";
    renderManageCommanderDeleteResult({
        message: `Deleting fights for ${commander} and removing associated source logs...`,
        matchedFightCount: fightCount,
        deletedFightCount: 0,
        deletedLogFileCount: 0,
        missingLogFileCount: 0,
        skippedLogFileCount: 0,
        analysisRecalculationSeconds: 0
    }, true);

    try {
        const result = await deleteCommanderFights(commander);
        renderManageCommanderDeleteResult(result, true);
        if (result.success) {
            currentAnalysisSnapshot = null;
            resetAnalysisPlayerDetailState();
            await main();
            renderManageCommanderDeleteResult(result, true);
        }
    } catch (error) {
        const payload = error?.payload ?? {
            message: error instanceof Error ? error.message : String(error),
            commander,
            matchedFightCount: fightCount,
            deletedFightCount: 0,
            deletedLogFileCount: 0,
            missingLogFileCount: 0,
            skippedLogFileCount: 0,
            analysisRecalculationSeconds: 0
        };
        renderManageCommanderDeleteResult(payload, false);
    } finally {
        button.textContent = "Delete";
        setManageCommanderDeleteBusy(false);
    }
}

async function handlePatchMetadataSave() {
    const button = document.querySelector("#patch-metadata-save-button");
    const textarea = document.querySelector("#patch-metadata-json");
    if (!textarea) {
        return;
    }

    let metadata;
    try {
        metadata = JSON.parse(textarea.value || "{}");
    } catch (error) {
        renderPatchMetadataStatus(error instanceof Error ? error.message : String(error), false);
        return;
    }

    button.disabled = true;
    renderPatchMetadataStatus("Saving patch metadata...");
    try {
        const saved = await savePatchMetadata(metadata);
        renderPatchMetadata(saved);
        renderPatchMetadataStatus("Patch metadata saved.");
        currentAnalysisSnapshot = null;
        await main();
    } catch (error) {
        renderPatchMetadataStatus(error instanceof Error ? error.message : String(error), false);
    } finally {
        button.disabled = false;
    }
}

async function handlePatchMetadataReload() {
    const button = document.querySelector("#patch-metadata-reload-button");
    button.disabled = true;
    renderPatchMetadataStatus("Reloading patch metadata...");
    try {
        const metadata = await loadPatchMetadata();
        renderPatchMetadata(metadata);
        renderPatchMetadataStatus("Patch metadata reloaded.");
        currentAnalysisSnapshot = null;
        await main();
    } catch (error) {
        renderPatchMetadataStatus(error instanceof Error ? error.message : String(error), false);
    } finally {
        button.disabled = false;
    }
}

async function handleCompHelperConfigSave() {
    const button = document.querySelector("#analysis-comp-helper-config-save");
    if (!currentCompHelperConfig) {
        return;
    }

    button.disabled = true;
    renderCompHelperConfigStatus("Saving Comp Helper config...");
    try {
        const saved = await saveCompHelperConfig(currentCompHelperConfig);
        applyCompHelperConfig(saved);
        renderCompHelperConfigEditor();
        renderCompHelperConfigStatus("Comp Helper config saved.");
        if (currentAnalysisSnapshot) {
            renderAnalysisCompHelper(currentAnalysisSnapshot);
        }
    } catch (error) {
        renderCompHelperConfigStatus(error instanceof Error ? error.message : String(error), false);
    } finally {
        button.disabled = false;
    }
}

async function handleCompHelperConfigReload() {
    const button = document.querySelector("#analysis-comp-helper-config-reload");
    button.disabled = true;
    renderCompHelperConfigStatus("Reloading saved Comp Helper config...");
    try {
        const config = await loadCompHelperConfig();
        applyCompHelperConfig(config);
        renderCompHelperConfigEditor();
        renderCompHelperConfigStatus("Saved Comp Helper config reloaded.");
        if (currentAnalysisSnapshot) {
            renderAnalysisCompHelper(currentAnalysisSnapshot);
        }
    } catch (error) {
        renderCompHelperConfigStatus(error instanceof Error ? error.message : String(error), false);
    } finally {
        button.disabled = false;
    }
}

async function handleCompHelperConfigReset() {
    const button = document.querySelector("#analysis-comp-helper-config-reset");
    button.disabled = true;
    renderCompHelperConfigStatus("Reverting Comp Helper config to shipped defaults...");
    try {
        const config = await resetCompHelperConfig();
        applyCompHelperConfig(config);
        renderCompHelperConfigEditor();
        renderCompHelperConfigStatus("Shipped Comp Helper defaults restored and saved.");
        if (currentAnalysisSnapshot) {
            renderAnalysisCompHelper(currentAnalysisSnapshot);
        }
    } catch (error) {
        renderCompHelperConfigStatus(error instanceof Error ? error.message : String(error), false);
    } finally {
        button.disabled = false;
    }
}

async function handleBatchSubmit(event) {
    event.preventDefault();

    const modeInput = document.querySelector("#directory-mode");
    const maxParallelismInput = document.querySelector("#directory-max-parallelism");
    const mode = modeInput.value;
    const directoryPath = getBatchDirectoryPath(mode).trim();
    const rawParallelism = Number.parseInt(maxParallelismInput.value, 10);
    const maxParallelism = Number.isFinite(rawParallelism)
        ? Math.min(16, Math.max(1, rawParallelism))
        : 4;
    maxParallelismInput.value = String(maxParallelism);

    if (!directoryPath) {
        const requiredSetting = mode === "rebuild-all"
            ? "Workspace:ArchiveLogDirectoryPath"
            : "Workspace:PendingDirectoryPath";
        const requiredLabel = mode === "rebuild-all"
            ? "a rebuild-all parse"
            : "a new-only parse";
        renderBatchStatus({ message: `Configure ${requiredSetting} in appsettings.json before starting ${requiredLabel}.` }, false);
        renderBatchResults(null);
        return;
    }

    if (mode === "rebuild-all" && !window.confirm(buildRebuildAllConfirmationMessage(directoryPath))) {
        return;
    }

    localStorage.setItem(DIRECTORY_MAX_PARALLELISM_KEY, String(maxParallelism));

    setBatchButtonBusy(true);
    renderBatchStatus(
        {
            state: "running",
            message: mode === "rebuild-all"
                ? "Scanning the archived log store, hashing files, and queuing parser work. Full rebuilds can take a while."
                : "Scanning the pending queue, hashing files, and queuing parser work. New-only batches finish after the queue is drained.",
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

        if (response.status === 409 && String(status?.state ?? "").toLowerCase() === "blocked") {
            if (currentDashboardSnapshot?.manageActivity) {
                currentDashboardSnapshot.manageActivity = {
                    ...currentDashboardSnapshot.manageActivity,
                    parseRunning: false,
                    uploadRunning: true,
                    summary: status.message ?? currentDashboardSnapshot.manageActivity.summary,
                    activeBatchJob: null
                };
                renderWorkspace(currentDashboardSnapshot);
                scheduleManageActivityRefresh(currentDashboardSnapshot);
            }
        }

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
    collapseAnalysisFilterDrawers();
    setAnalysisRefreshControlsBusy(true);

    try {
        await ensureAnalysisLoaded(true);
    } catch (error) {
        renderAnalysisError(error instanceof Error ? error.message : String(error));
    } finally {
        setAnalysisRefreshControlsBusy(false);
    }
}

function setAnalysisRefreshControlsBusy(isBusy) {
    document.querySelectorAll("#analysis-apply-button, #analysis-clear-filters-button").forEach(button => {
        button.disabled = isBusy;
    });
}

function updateAnalysisTrendControls(mode, smoothingWindow) {
    analysisTrendMode = normalizeAnalysisTrendMode(mode);
    analysisTrendSmoothingWindow = normalizeAnalysisTrendSmoothingWindow(smoothingWindow);

    localStorage.setItem(ANALYSIS_TREND_MODE_KEY, analysisTrendMode);
    localStorage.setItem(ANALYSIS_TREND_SMOOTHING_KEY, String(analysisTrendSmoothingWindow));

    document.querySelector("#analysis-trend-mode").value = analysisTrendMode;
    document.querySelector("#analysis-trend-smoothing").value = String(analysisTrendSmoothingWindow);

    if (currentAnalysisSnapshot) {
        renderAnalysisCharts(currentAnalysisSnapshot);
        renderAnalysisBurstTrends(currentAnalysisSnapshot);
    }
}

function handleAnalysisBurstComparisonSelectionChange(event) {
    const input = event.target.closest("[data-analysis-burst-comparison-id]");
    if (!input) {
        return;
    }

    const id = String(input.dataset.analysisBurstComparisonId ?? "");
    const selectedIds = new Set(selectedAnalysisBurstComparisonIds ?? ANALYSIS_BURST_COMPARISON_SERIES.map(series => series.id));
    if (input.checked) {
        selectedIds.add(id);
    } else {
        selectedIds.delete(id);
    }

    selectedAnalysisBurstComparisonIds = selectedIds;
    if (currentAnalysisSnapshot) {
        renderAnalysisBurstTrends(currentAnalysisSnapshot);
    }
}

function hydrateBatchForm() {
    const storedMaxParallelism = localStorage.getItem(DIRECTORY_MAX_PARALLELISM_KEY);
    localStorage.removeItem("wvw-analyst.last-directory");
    localStorage.removeItem("wvw-analyst.last-mode");
    document.querySelector("#directory-mode").value = "new-only";

    const parsedParallelism = Number.parseInt(storedMaxParallelism ?? "", 10);
    document.querySelector("#directory-max-parallelism").value = String(
        Number.isFinite(parsedParallelism)
            ? Math.min(16, Math.max(1, parsedParallelism))
            : 4);

    syncFightShapeDiagnosticsVisibility();
    syncManageControls();
}

function hydrateAnalysisTrendControls() {
    analysisTrendMode = normalizeAnalysisTrendMode(localStorage.getItem(ANALYSIS_TREND_MODE_KEY));
    analysisTrendSmoothingWindow = normalizeAnalysisTrendSmoothingWindow(localStorage.getItem(ANALYSIS_TREND_SMOOTHING_KEY));

    document.querySelector("#analysis-trend-mode").value = analysisTrendMode;
    document.querySelector("#analysis-trend-smoothing").value = String(analysisTrendSmoothingWindow);
}

function setBatchButtonBusy(isBusy) {
    const button = document.querySelector("#directory-button");
    button.dataset.busy = isBusy ? "true" : "false";
    button.textContent = isBusy ? "Parsing..." : "Start batch parse";
    syncManageControls();
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

        if (status.state === "running" || status.state === "warming-analysis") {
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
    stopManageActivityRefresh();
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
    currentAnalysisSnapshot = null;

    try {
        const [snapshot, compHelperConfig] = await Promise.all([
            loadDashboard(),
            loadCompHelperConfig()
        ]);
        currentDashboardSnapshot = snapshot;
        applyCompHelperConfig(compHelperConfig);

        renderWorkspace(snapshot);
        renderCompHelperConfigEditor();
        renderManageCommanderFights(snapshot);
        renderRecentParses(snapshot, selectedFightId);
        renderFightBrowser(snapshot, selectedFightId);
        renderAnalysisLoading("Open the Analysis tab to load analysis.");

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

        syncSharedManageState(snapshot);
        resumeBatchJobPollingIfNeeded();
        scheduleManageActivityRefresh(snapshot);
        renderBatchResults(lastBatchResult);

        if (activeAppTab === "analysis") {
            await ensureAnalysisLoaded();
        }
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
document.querySelector("#directory-mode").addEventListener("change", () => {
    syncManageControls();
});
document.querySelector("#manage-reset-button").addEventListener("click", () => {
    void handleManageReset();
});
document.querySelector("#manage-commanders-body").addEventListener("click", event => {
    const button = event.target.closest("[data-manage-commander-delete]");
    if (!button) {
        return;
    }

    void handleManageCommanderDelete(button);
});
document.querySelector("#log-file-upload-button").addEventListener("click", () => {
    if (!isConfiguredLogDirectoryAvailable() || logFileUploadBusy || manageResetBusy || manageCommanderDeleteBusy) {
        return;
    }

    document.querySelector("#log-file-upload-input").click();
});
document.querySelector("#log-file-upload-input").addEventListener("change", event => {
    void uploadLogFiles(event.target.files);
});
document.querySelector("#log-file-dropzone").addEventListener("click", event => {
    if (event.target.closest("#log-file-upload-button")) {
        return;
    }

    if (!isConfiguredLogDirectoryAvailable() || logFileUploadBusy || manageResetBusy || manageCommanderDeleteBusy) {
        return;
    }

    document.querySelector("#log-file-upload-input").click();
});
document.querySelector("#log-file-dropzone").addEventListener("keydown", event => {
    if (event.key !== "Enter" && event.key !== " ") {
        return;
    }

    event.preventDefault();
    if (!isConfiguredLogDirectoryAvailable() || logFileUploadBusy || manageResetBusy || manageCommanderDeleteBusy) {
        return;
    }

    document.querySelector("#log-file-upload-input").click();
});
["dragenter", "dragover"].forEach(eventName => {
    document.querySelector("#log-file-dropzone").addEventListener(eventName, event => {
        event.preventDefault();
        if (!isConfiguredLogDirectoryAvailable() || logFileUploadBusy || manageResetBusy || manageCommanderDeleteBusy) {
            return;
        }

        event.currentTarget.classList.add("is-dragging");
    });
});
["dragleave", "dragend", "drop"].forEach(eventName => {
    document.querySelector("#log-file-dropzone").addEventListener(eventName, event => {
        event.preventDefault();
        event.currentTarget.classList.remove("is-dragging");
    });
});
document.querySelector("#log-file-dropzone").addEventListener("drop", event => {
    if (!isConfiguredLogDirectoryAvailable() || logFileUploadBusy || manageResetBusy || manageCommanderDeleteBusy) {
        return;
    }

    void uploadLogFiles(event.dataTransfer?.files);
});
document.querySelector("#batch-results-show-all").addEventListener("change", () => renderBatchResults(lastBatchResult));
document.querySelector("#batch-results-show-excluded").addEventListener("change", () => renderBatchResults(lastBatchResult));
document.querySelector("#manage-show-fight-shape-diagnostics").addEventListener("change", event => {
    setFightShapeDiagnosticsVisible(event.target.checked);
});
document.querySelector("#patch-metadata-save-button").addEventListener("click", () => void handlePatchMetadataSave());
document.querySelector("#patch-metadata-reload-button").addEventListener("click", () => void handlePatchMetadataReload());
document.querySelector("#analysis-comp-helper-config-save").addEventListener("click", () => void handleCompHelperConfigSave());
document.querySelector("#analysis-comp-helper-config-reload").addEventListener("click", () => void handleCompHelperConfigReload());
document.querySelector("#analysis-comp-helper-config-reset").addEventListener("click", () => void handleCompHelperConfigReset());
document.querySelector(".comp-helper-config-editor").addEventListener("input", event => {
    const input = event.target.closest("[data-comp-helper-config-field]");
    if (!input) {
        return;
    }

    if (updateCompHelperConfigValue(input) && currentAnalysisSnapshot) {
        renderAnalysisCompHelper(currentAnalysisSnapshot);
    }
});
document.querySelector(".comp-helper-config-editor").addEventListener("change", event => {
    const input = event.target.closest("[data-comp-helper-config-field]");
    if (!input) {
        return;
    }

    if (updateCompHelperConfigValue(input) && currentAnalysisSnapshot) {
        renderAnalysisCompHelper(currentAnalysisSnapshot);
    }
});
document.querySelector("#fight-browser-commander").addEventListener("change", handleFightBrowserChange);
document.querySelector("#fight-browser-start-date").addEventListener("change", handleFightBrowserChange);
document.querySelector("#fight-browser-end-date").addEventListener("change", handleFightBrowserChange);
document.querySelector("#fight-browser-outcome").addEventListener("change", handleFightBrowserChange);
document.querySelector("#fight-browser-patch-scope").addEventListener("change", handleFightBrowserChange);
document.querySelector("#fight-browser-attribute-filters").addEventListener("change", () => {
    updateAttributeFilterSelectionUi("#fight-browser-attribute-filters");
    handleFightBrowserChange();
});
document.querySelector("#fight-browser-clear-attribute-filters").addEventListener("click", () => {
    clearSelectedAttributeFilterValues("#fight-browser-attribute-filters");
    handleFightBrowserChange();
});
document.querySelector("#fight-browser-class-filters").addEventListener("change", handleFightBrowserChange);
document.querySelector("#fight-browser-class-filters").addEventListener("click", event => {
    const button = event.target.closest("[data-class-filter-clear]");
    if (!button) {
        return;
    }

    clearSelectedClassFilterValues(`#${button.dataset.classFilterClear}`);
    handleFightBrowserChange();
});
document.querySelector("#fight-browser-clear-class-filters").addEventListener("click", () => {
    clearFightBrowserClassFilters();
    handleFightBrowserChange();
});
document.querySelector("#fight-shape-export-copy").addEventListener("click", () => void copyFightShapeExport());
document.querySelector("#analysis-class-filters").addEventListener("change", event => {
    const box = event.target.closest(".class-filter-box");
    if (!box) {
        return;
    }

    updateClassFilterGroupSummary(`#${box.id}`);
});
document.querySelector("#fight-browser-top-bursts-toggle").addEventListener("click", toggleFightBrowserTopBursts);
document.querySelector("#analysis-class-filters").addEventListener("click", event => {
    const button = event.target.closest("[data-class-filter-clear]");
    if (!button) {
        return;
    }

    clearSelectedClassFilterValues(`#${button.dataset.classFilterClear}`);
});
document.querySelector("#analysis-clear-class-filters").addEventListener("click", () => {
    clearAnalysisClassFilters();
});
document.querySelector("#analysis-patch-scope").addEventListener("change", () => {
    currentAnalysisSnapshot = null;
});
document.querySelector("#analysis-attribute-filters").addEventListener("change", () => {
    updateAttributeFilterSelectionUi("#analysis-attribute-filters");
    currentAnalysisSnapshot = null;
});
document.querySelector("#analysis-clear-attribute-filters").addEventListener("click", () => {
    clearSelectedAttributeFilterValues("#analysis-attribute-filters");
    currentAnalysisSnapshot = null;
});
document.querySelector("#analysis-scope-list").addEventListener("click", event => {
    const button = event.target.closest("[data-analysis-scope-clear]");
    if (!button) {
        return;
    }

    if (clearAnalysisScopeFilter(button.dataset.analysisScopeClear)) {
        void refreshAnalysis();
    }
});
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
document.querySelector("#analysis-trend-mode").addEventListener("change", event => {
    updateAnalysisTrendControls(event.target.value, analysisTrendSmoothingWindow);
});
document.querySelector("#analysis-trend-smoothing").addEventListener("change", event => {
    updateAnalysisTrendControls(analysisTrendMode, event.target.value);
});
document.querySelector("#analysis-burst-comparison-controls").addEventListener("change", handleAnalysisBurstComparisonSelectionChange);
document.querySelector("#analysis-comp-helper-search").addEventListener("input", () => {
    if (currentAnalysisSnapshot) {
        renderAnalysisCompHelper(currentAnalysisSnapshot);
    }
});
document.querySelector("#analysis-comp-helper-profile").addEventListener("change", event => {
    applyCompHelperProfile(event.target.value);
    if (currentAnalysisSnapshot) {
        renderAnalysisCompHelper(currentAnalysisSnapshot);
    }
});
document.querySelector("#analysis-comp-helper-candidate-tier").addEventListener("change", event => {
    const nextValue = String(event.target.value ?? "").toLowerCase();
    compHelperCandidateTierKey = COMP_HELPER_CANDIDATE_TIER_OPTIONS[nextValue]
        ? nextValue
        : "best";
    if (currentAnalysisSnapshot) {
        renderAnalysisCompHelper(currentAnalysisSnapshot);
    }
});
document.querySelector("#analysis-comp-helper-clear-locks").addEventListener("click", () => {
    lockedCompHelperCandidateIds = [];
    if (currentAnalysisSnapshot) {
        renderAnalysisCompHelper(currentAnalysisSnapshot);
    }
});
document.querySelector("#analysis-comp-helper-locks").addEventListener("click", event => {
    const button = event.target.closest("[data-comp-helper-unlock]");
    if (!button) {
        return;
    }

    toggleCompHelperCandidateLock(button.dataset.compHelperUnlock);
});
document.querySelector("#analysis-comp-helper-candidates-body").addEventListener("click", event => {
    const button = event.target.closest("[data-comp-helper-toggle]");
    if (!button || button.disabled) {
        return;
    }

    toggleCompHelperCandidateLock(button.dataset.compHelperToggle);
});
document.querySelector("#analysis-comp-helper-favored-lanes").addEventListener("change", event => {
    const input = event.target.closest("[data-comp-helper-favorite-group]");
    if (!input) {
        return;
    }

    toggleCompHelperFavorite(input.dataset.compHelperFavoriteGroup, input.dataset.compHelperFavoriteKey);
    if (currentAnalysisSnapshot) {
        renderAnalysisCompHelper(currentAnalysisSnapshot);
    }
});
document.querySelector("#analysis-comp-helper-favored-packages").addEventListener("change", event => {
    const input = event.target.closest("[data-comp-helper-favorite-group]");
    if (!input) {
        return;
    }

    toggleCompHelperFavorite(input.dataset.compHelperFavoriteGroup, input.dataset.compHelperFavoriteKey);
    if (currentAnalysisSnapshot) {
        renderAnalysisCompHelper(currentAnalysisSnapshot);
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

    setSelectedAnalysisLaneKeys(currentAnalysisSnapshot, [row.dataset.laneKey ?? null]);
    if (currentAnalysisSnapshot) {
        renderAnalysisLanes(currentAnalysisSnapshot);
    }
});
document.querySelector("#analysis-lane-selection").addEventListener("change", event => {
    const input = event.target.closest("[data-analysis-lane-toggle]");
    if (!input || !currentAnalysisSnapshot) {
        return;
    }

    toggleSelectedAnalysisLane(currentAnalysisSnapshot, input.dataset.analysisLaneToggle);
    renderAnalysisLanes(currentAnalysisSnapshot);
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
document.querySelector("#analysis-boon-trend-legend").addEventListener("change", event => {
    const input = event.target.closest("[data-analysis-boon-trend-id]");
    if (!input || !currentAnalysisSnapshot) {
        return;
    }

    const id = String(input.dataset.analysisBoonTrendId ?? "");
    if (!selectedAnalysisBoonTrendIds) {
        selectedAnalysisBoonTrendIds = new Set();
    }

    if (input.checked) {
        selectedAnalysisBoonTrendIds.add(id);
    } else {
        selectedAnalysisBoonTrendIds.delete(id);
    }

    renderAnalysisBoonTrends(currentAnalysisSnapshot);
});
document.querySelector("[data-analysis-boon-trend-action='all']").addEventListener("click", () => {
    if (!currentAnalysisSnapshot) {
        return;
    }

    selectedAnalysisBoonTrendIds = new Set(getAnalysisBoonTrendIds(currentAnalysisSnapshot.boonTrends ?? []));
    renderAnalysisBoonTrends(currentAnalysisSnapshot);
});
document.querySelector("[data-analysis-boon-trend-action='none']").addEventListener("click", () => {
    if (!currentAnalysisSnapshot) {
        return;
    }

    selectedAnalysisBoonTrendIds = new Set();
    renderAnalysisBoonTrends(currentAnalysisSnapshot);
});
document.querySelector("#analysis-boon-trend-chart").addEventListener("mouseover", showAnalysisBoonTrendTooltip);
document.querySelector("#analysis-boon-trend-chart").addEventListener("focusin", showAnalysisBoonTrendTooltip);
document.querySelector("#analysis-boon-trend-chart").addEventListener("mouseout", event => {
    if (event.target.closest("[data-analysis-boon-trend-point]")) {
        hideAnalysisBoonTrendTooltip();
    }
});
document.querySelector("#analysis-boon-trend-chart").addEventListener("focusout", hideAnalysisBoonTrendTooltip);
document.querySelector("#analysis-player-detail").addEventListener("change", handleAnalysisImpactTrendSelectionChange);
document.querySelector("#analysis-class-detail").addEventListener("change", handleAnalysisImpactTrendSelectionChange);
document.querySelector("#analysis-player-detail").addEventListener("click", handleAnalysisImpactTrendActionClick);
document.querySelector("#analysis-class-detail").addEventListener("click", handleAnalysisImpactTrendActionClick);
document.querySelector("#analysis-player-detail").addEventListener("mouseover", showAnalysisImpactTrendTooltip);
document.querySelector("#analysis-class-detail").addEventListener("mouseover", showAnalysisImpactTrendTooltip);
document.querySelector("#analysis-player-detail").addEventListener("focusin", showAnalysisImpactTrendTooltip);
document.querySelector("#analysis-class-detail").addEventListener("focusin", showAnalysisImpactTrendTooltip);
document.querySelector("#analysis-player-detail").addEventListener("mouseout", event => {
    if (event.target.closest("[data-analysis-impact-trend-point]")) {
        hideAnalysisImpactTrendTooltip("player");
    }
});
document.querySelector("#analysis-class-detail").addEventListener("mouseout", event => {
    if (event.target.closest("[data-analysis-impact-trend-point]")) {
        hideAnalysisImpactTrendTooltip("class");
    }
});
document.querySelector("#analysis-player-detail").addEventListener("focusout", () => hideAnalysisImpactTrendTooltip("player"));
document.querySelector("#analysis-class-detail").addEventListener("focusout", () => hideAnalysisImpactTrendTooltip("class"));
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
document.querySelector("#analysis-clear-filters-button").addEventListener("click", () => {
    resetAnalysisFiltersToDefaults();
    void refreshAnalysis();
});
document.querySelectorAll("[data-app-tab]").forEach(button => {
    button.addEventListener("click", () => setActiveAppTab(button.dataset.appTab));
});
document.querySelectorAll("[data-fight-browser-sort]").forEach(button => {
    button.addEventListener("click", () => setFightBrowserSort(button.dataset.fightBrowserSort));
});
document.querySelectorAll("[data-analysis-player-sort]").forEach(button => {
    button.addEventListener("click", () => setAnalysisPlayerSort(button.dataset.analysisPlayerSort));
});
document.querySelectorAll("[data-analysis-class-sort]").forEach(button => {
    button.addEventListener("click", () => setAnalysisClassSort(button.dataset.analysisClassSort));
});
document.querySelectorAll("[data-analysis-enemy-sort]").forEach(button => {
    button.addEventListener("click", () => setAnalysisEnemySort(button.dataset.analysisEnemySort));
});
document.querySelectorAll("[data-analysis-tab]").forEach(button => {
    button.addEventListener("click", () => setActiveAnalysisTab(button.dataset.analysisTab, { resetScroll: true }));
});

hydrateBatchForm();
hydrateAnalysisTrendControls();
applyCompHelperProfile("balanced");
setActiveAppTab(resolveInitialAppTab(), { persist: false, loadAnalysis: false });
main();

function stringEqualsIgnoreCase(left, right) {
    return String(left ?? "").localeCompare(String(right ?? ""), undefined, { sensitivity: "accent" }) === 0;
}
