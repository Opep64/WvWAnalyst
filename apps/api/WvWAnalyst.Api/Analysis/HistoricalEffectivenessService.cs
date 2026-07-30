using WvWAnalyst.Api.Bridge;
using WvWAnalyst.Contracts;

namespace WvWAnalyst.Api.Analysis;

public sealed class HistoricalEffectivenessService
{
    public const string CurrentMethodVersion = "historical-effectiveness-v1";

    private const int MinimumPairedFights = 5;
    private const int TargetPairedFights = 20;
    private const double EqualityTolerance = 0.000001;
    private const double Normal95CriticalValue = 1.96;

    private readonly FightAnalysisService _fightAnalysis;
    private readonly FightCatalogService _fightCatalog;
    private readonly FightOutcomeObservationCacheService _observationCache;
    private readonly object _snapshotCacheLock = new();
    private readonly Dictionary<string, HistoricalEffectivenessSnapshotDto> _snapshotCache = new(StringComparer.Ordinal);
    private long _snapshotCatalogVersion = -1;

    public HistoricalEffectivenessService(
        FightAnalysisService fightAnalysis,
        FightCatalogService fightCatalog,
        FightOutcomeObservationCacheService observationCache)
    {
        _fightAnalysis = fightAnalysis;
        _fightCatalog = fightCatalog;
        _observationCache = observationCache;
    }

    public HistoricalEffectivenessSnapshotDto BuildSnapshot(
        string? commander,
        string? startDate,
        string? endDate,
        string? outcomeCode,
        string? squadIncludeClasses,
        string? squadExcludeClasses,
        string? enemyIncludeClasses,
        string? enemyExcludeClasses,
        string? patchScope,
        string? patchEraIds,
        string? fightAttributes)
    {
        string cacheKey = BuildCacheKey(
            commander,
            startDate,
            endDate,
            outcomeCode,
            squadIncludeClasses,
            squadExcludeClasses,
            enemyIncludeClasses,
            enemyExcludeClasses,
            patchScope,
            patchEraIds,
            fightAttributes);
        long catalogVersion = _fightCatalog.CacheVersion;
        lock (_snapshotCacheLock)
        {
            if (_snapshotCatalogVersion != catalogVersion)
            {
                _snapshotCache.Clear();
                _snapshotCatalogVersion = catalogVersion;
            }

            if (_snapshotCache.TryGetValue(cacheKey, out HistoricalEffectivenessSnapshotDto? cached))
            {
                return cached;
            }
        }

        HistoricalEffectivenessSnapshotDto snapshot = BuildSnapshotCore(
            commander,
            startDate,
            endDate,
            outcomeCode,
            squadIncludeClasses,
            squadExcludeClasses,
            enemyIncludeClasses,
            enemyExcludeClasses,
            patchScope,
            patchEraIds,
            fightAttributes);
        long finalCatalogVersion = _fightCatalog.CacheVersion;
        lock (_snapshotCacheLock)
        {
            if (_snapshotCatalogVersion != finalCatalogVersion)
            {
                _snapshotCache.Clear();
                _snapshotCatalogVersion = finalCatalogVersion;
            }

            if (finalCatalogVersion == catalogVersion)
            {
                if (_snapshotCache.Count >= 24)
                {
                    _snapshotCache.Clear();
                }
                _snapshotCache[cacheKey] = snapshot;
            }
        }
        return snapshot;
    }

    private HistoricalEffectivenessSnapshotDto BuildSnapshotCore(
        string? commander,
        string? startDate,
        string? endDate,
        string? outcomeCode,
        string? squadIncludeClasses,
        string? squadExcludeClasses,
        string? enemyIncludeClasses,
        string? enemyExcludeClasses,
        string? patchScope,
        string? patchEraIds,
        string? fightAttributes)
    {
        FightAnalysisSnapshotDto analysis = _fightAnalysis.BuildSnapshot(
            commander,
            startDate,
            endDate,
            outcomeCode,
            squadIncludeClasses,
            squadExcludeClasses,
            enemyIncludeClasses,
            enemyExcludeClasses,
            patchScope,
            patchEraIds,
            fightAttributes);

        IReadOnlyDictionary<string, FightArtifactSummaryDto> summaryLookup = _fightCatalog
            .GetFightBrowserSnapshot()
            .Fights
            .ToDictionary(fight => fight.FightId, StringComparer.OrdinalIgnoreCase);
        var selectedFights = new SelectedFight[analysis.Trends.Count];
        Parallel.For(
            fromInclusive: 0,
            toExclusive: analysis.Trends.Count,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(6, Math.Max(1, Environment.ProcessorCount)),
            },
            index =>
            {
                FightAnalysisTrendPointDto trend = analysis.Trends[index];
                summaryLookup.TryGetValue(trend.FightId, out FightArtifactSummaryDto? summary);
                FightOutcomeObservationCacheDto? cache = null;
                if (_fightCatalog.TryGetArtifact(
                        trend.FightId,
                        FightArtifactKind.OutcomeObservations,
                        out string cachePath,
                        out _))
                {
                    cache = _observationCache.TryRead(cachePath);
                }

                selectedFights[index] = new SelectedFight(
                    trend.FightId,
                    trend,
                    summary,
                    cache);
            });

        FightRecord[] cacheFights = selectedFights
            .Where(fight => fight.Cache is not null)
            .Select(fight => new FightRecord(
                fight.FightId,
                fight.Summary,
                fight.Trend,
                fight.Cache!))
            .ToArray();
        FightObservation[] observations = cacheFights
            .SelectMany(fight => fight.Cache.Observations.Select(observation => new FightObservation(fight, observation)))
            .ToArray();

        HistoricalEffectivenessReportDto[] reports =
        [
            BuildReport(cacheFights, "squad", "enemy", "down-pressure", "down", "ordinary-pressure", "Enemy downs", "Ordinary squad pressure"),
            BuildReport(cacheFights, "enemy", "squad", "down-pressure", "down", "ordinary-pressure", "Squad downs", "Ordinary enemy pressure"),
            BuildReport(cacheFights, "squad", "enemy", "down-conversion", "kill", "recovery", "Enemy kills", "Enemy recoveries"),
            BuildReport(cacheFights, "enemy", "squad", "down-conversion", "kill", "recovery", "Squad kills", "Squad recoveries"),
            BuildReport(cacheFights, "squad", "enemy", "down-recovery", "recovery", "death", "Squad recoveries", "Squad deaths"),
            BuildReport(cacheFights, "enemy", "squad", "down-recovery", "recovery", "death", "Enemy recoveries", "Enemy deaths"),
        ];

        return new HistoricalEffectivenessSnapshotDto(
            MethodVersion: CurrentMethodVersion,
            GeneratedAtUtc: DateTimeOffset.UtcNow.ToString("O"),
            Selection: analysis.Selection,
            Scope: new HistoricalEffectivenessScopeDto(
                FilteredFightCount: selectedFights.Length,
                CacheFightCount: cacheFights.Length,
                MissingCacheFightCount: selectedFights.Length - cacheFights.Length,
                ObservationCount: observations.Length,
                SquadPerspectiveObservationCount: observations.Count(item =>
                    string.Equals(item.Observation.PerspectiveSideId, "squad", StringComparison.OrdinalIgnoreCase)),
                EnemyPerspectiveObservationCount: observations.Count(item =>
                    string.Equals(item.Observation.PerspectiveSideId, "enemy", StringComparison.OrdinalIgnoreCase)),
                AvailabilityNotes: BuildAvailabilityNotes(cacheFights)),
            Methodology: BuildMethodology(),
            Strata: BuildStrata(selectedFights),
            Reports: reports);
    }

    private static string BuildCacheKey(params string?[] values)
    {
        return string.Join('\u001F', values.Select(value => value?.Trim() ?? string.Empty));
    }

    private static HistoricalEffectivenessMethodologyDto BuildMethodology()
    {
        return new HistoricalEffectivenessMethodologyDto(
            Summary: "Each report compares outcome windows with its matching baseline separately for squad and enemy perspectives.",
            Weighting: "Observations are averaged within each fight first, then fights receive equal weight so long or event-heavy fights cannot dominate.",
            ConfidenceIntervals: "Approximate 95% intervals are calculated from the distribution of paired within-fight mean differences.",
            EvidenceScore: "Evidence combines fight support, cohort balance, non-tied fight coverage, and interval precision. Named-effect support counts only fights with a non-zero difference, so tied zero-value fights cannot inflate evidence.",
            AssociationScore: "Association multiplies evidence by difference strength. Named effects combine relative lift with absolute magnitude normalized within conditions or crowd control, preventing tiny sparse effects from outranking materially larger signals.",
            Interpretation: "Results are observational. Unavailable enemy state, healing, barrier, boon, condition, or Stability data is excluded rather than treated as zero.",
            MinimumPairedFights: MinimumPairedFights,
            TargetPairedFights: TargetPairedFights);
    }

    private static IReadOnlyList<string> BuildAvailabilityNotes(IReadOnlyList<FightRecord> fights)
    {
        if (fights.Count == 0)
        {
            return ["No outcome-observation caches are available for the selected fights."];
        }

        var notes = new List<string>();
        AddAvailabilityNote(
            notes,
            fights,
            fight => fight.Cache.Availability.EnemyHealing,
            "Enemy healing");
        AddAvailabilityNote(
            notes,
            fights,
            fight => fight.Cache.Availability.EnemyBarrier,
            "Enemy barrier");
        AddAvailabilityNote(
            notes,
            fights,
            fight => fight.Cache.Availability.ExactEnemyBoonState,
            "Exact enemy boon state");
        AddAvailabilityNote(
            notes,
            fights,
            fight => fight.Cache.Availability.ExactEnemyConditionState,
            "Exact enemy condition state");
        AddAvailabilityNote(
            notes,
            fights,
            fight => fight.Cache.Availability.ExactStabilityState,
            "Exact Stability state");
        AddAvailabilityNote(
            notes,
            fights,
            fight => fight.Cache.Availability.SquadConditionApplications,
            "Squad-attributed condition applications");
        AddAvailabilityNote(
            notes,
            fights,
            fight => fight.Cache.Availability.EnemyConditionApplications,
            "Enemy-attributed condition applications");
        AddAvailabilityNote(
            notes,
            fights,
            fight => fight.Cache.Availability.SquadCrowdControlEvents,
            "Squad-attributed crowd control");
        AddAvailabilityNote(
            notes,
            fights,
            fight => fight.Cache.Availability.EnemyCrowdControlEvents,
            "Enemy-attributed crowd control");
        return notes;
    }

    private static void AddAvailabilityNote(
        List<string> notes,
        IReadOnlyList<FightRecord> fights,
        Func<FightRecord, bool> availability,
        string label)
    {
        int available = fights.Count(availability);
        notes.Add(available switch
        {
            0 => $"{label} is unavailable in all {fights.Count} cached fights.",
            _ when available == fights.Count => $"{label} is available in all {fights.Count} cached fights.",
            _ => $"{label} is available in {available} of {fights.Count} cached fights; unavailable fights are excluded from that metric.",
        });
    }

    private static IReadOnlyList<HistoricalEffectivenessStratumDto> BuildStrata(
        IReadOnlyList<SelectedFight> fights)
    {
        var strata = new List<HistoricalEffectivenessStratumDto>();
        strata.AddRange(fights
            .GroupBy(fight => (
                Key: fight.Trend.PatchEraId ?? "unknown",
                Label: fight.Trend.PatchEraLabel ?? "Unknown patch"))
            .Select(group => new HistoricalEffectivenessStratumDto(
                Type: "patch-era",
                Key: group.Key.Key,
                Label: group.Key.Label,
                FilteredFightCount: group.Count(),
                CacheFightCount: group.Count(fight => fight.Cache is not null))));
        strata.AddRange(fights
            .GroupBy(fight =>
            {
                int? build = fight.Summary?.FightIndex?.GW2Build;
                return build.HasValue && build.Value > 0
                    ? (Key: build.Value.ToString(), Label: $"Game build {build.Value}")
                    : (Key: "unknown", Label: "Unknown game build");
            })
            .Select(group => new HistoricalEffectivenessStratumDto(
                Type: "game-build",
                Key: group.Key.Key,
                Label: group.Key.Label,
                FilteredFightCount: group.Count(),
                CacheFightCount: group.Count(fight => fight.Cache is not null))));
        strata.AddRange(BuildSizeStrata(fights, "squad-size", GetSquadSize));
        strata.AddRange(BuildSizeStrata(fights, "enemy-size", GetEnemySize));

        return strata
            .OrderBy(stratum => stratum.Type, StringComparer.Ordinal)
            .ThenBy(stratum => stratum.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<HistoricalEffectivenessStratumDto> BuildSizeStrata(
        IReadOnlyList<SelectedFight> fights,
        string type,
        Func<SelectedFight, double> getSize)
    {
        return fights
            .GroupBy(fight => GetSizeBand(getSize(fight)))
            .Select(group => new HistoricalEffectivenessStratumDto(
                Type: type,
                Key: group.Key.Key,
                Label: group.Key.Label,
                FilteredFightCount: group.Count(),
                CacheFightCount: group.Count(fight => fight.Cache is not null)));
    }

    private static (string Key, string Label) GetSizeBand(double size)
    {
        return size switch
        {
            <= 0 => ("unknown", "Unknown"),
            <= 20 => ("01-20", "1–20 players"),
            <= 35 => ("21-35", "21–35 players"),
            <= 50 => ("36-50", "36–50 players"),
            _ => ("51-plus", "51+ players"),
        };
    }

    private static double GetSquadSize(SelectedFight fight)
    {
        FightIndexDto? index = fight.Summary?.FightIndex;
        if ((index?.SquadSide?.EffectiveAlliedPlayerCount ?? 0) > 0)
        {
            return index!.SquadSide!.EffectiveAlliedPlayerCount;
        }

        return (index?.SquadSide?.PlayerCount ?? index?.SquadPlayerCount ?? 0)
            + Math.Max(0, index?.FriendlyNonSquadCount ?? 0);
    }

    private static double GetEnemySize(SelectedFight fight)
    {
        FightIndexDto? index = fight.Summary?.FightIndex;
        return (index?.EnemySide?.PlayerCount ?? 0) > 0
            ? index!.EnemySide!.PlayerCount
            : index?.EnemyPlayerCount ?? index?.EnemyTargetCount ?? 0;
    }

    private static HistoricalEffectivenessReportDto BuildReport(
        IReadOnlyList<FightRecord> fights,
        string perspectiveSideId,
        string opposingSideId,
        string outcomeFamily,
        string outcomeCode,
        string baselineCode,
        string outcomeLabel,
        string baselineLabel)
    {
        FightCohort[] cohorts = fights
            .Select(fight =>
            {
                FightOutcomeObservationDto[] familyRows = fight.Cache.Observations
                    .Where(observation =>
                        string.Equals(observation.PerspectiveSideId, perspectiveSideId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(observation.OutcomeFamily, outcomeFamily, StringComparison.Ordinal))
                    .ToArray();
                return new FightCohort(
                    fight,
                    familyRows.Where(observation => string.Equals(observation.OutcomeCode, outcomeCode, StringComparison.Ordinal)).ToArray(),
                    familyRows.Where(observation => string.Equals(observation.OutcomeCode, baselineCode, StringComparison.Ordinal)).ToArray());
            })
            .ToArray();
        FightCohort[] pairedCohorts = cohorts
            .Where(cohort => cohort.OutcomeRows.Length > 0 && cohort.BaselineRows.Length > 0)
            .ToArray();

        MetricDefinition[] definitions = BuildMetricDefinitions(outcomeFamily, perspectiveSideId);
        HistoricalEffectivenessMetricDto[] metrics = definitions
            .Select(definition => BuildMetric(cohorts, definition))
            .ToArray();
        IReadOnlyList<HistoricalEffectivenessNamedEffectDto> namedEffects =
            string.Equals(outcomeFamily, "down-recovery", StringComparison.Ordinal)
                ? []
                : BuildNamedEffects(cohorts, perspectiveSideId);

        return new HistoricalEffectivenessReportDto(
            Key: $"{perspectiveSideId}-{outcomeFamily}",
            PerspectiveSideId: perspectiveSideId,
            OpposingSideId: opposingSideId,
            OutcomeFamily: outcomeFamily,
            OutcomeLabel: outcomeLabel,
            BaselineLabel: baselineLabel,
            OutcomeObservationCount: cohorts.Sum(cohort => cohort.OutcomeRows.Length),
            BaselineObservationCount: cohorts.Sum(cohort => cohort.BaselineRows.Length),
            OutcomeFightCount: cohorts.Count(cohort => cohort.OutcomeRows.Length > 0),
            BaselineFightCount: cohorts.Count(cohort => cohort.BaselineRows.Length > 0),
            PairedFightCount: pairedCohorts.Length,
            ConfidenceLabel: GetReportConfidence(pairedCohorts.Length),
            Metrics: metrics,
            NamedEffects: namedEffects);
    }

    private static MetricDefinition[] BuildMetricDefinitions(string outcomeFamily, string perspectiveSideId)
    {
        bool recovery = string.Equals(outcomeFamily, "down-recovery", StringComparison.Ordinal);
        bool conversion = string.Equals(outcomeFamily, "down-conversion", StringComparison.Ordinal);
        var definitions = new List<MetricDefinition>
        {
            new("active-perspective", "Active perspective players", "Context", "players", row => row.Features.ActivePerspectivePlayers),
            new("active-opposition", "Active opposing players", "Context", "players", row => row.Features.ActiveOpposingPlayers),
            new("pressure-damage-per-player", "Pressure damage per active player", "Pressure", "damage/player", row => row.Features.PressureDamagePerActivePlayer),
            new("boon-removal-per-player", "Boon removals per active player", "Pressure", "removals/player", row => row.Features.BoonRemovalPerActivePlayer),
            new("top-target-share", "Top-target damage share", "Focus", "%", row => row.Features.TopTargetShare),
            new("top-three-target-share", "Top-three target damage share", "Focus", "%", row => row.Features.TopThreeTargetShare),
            new("top-target-contributors", "Top-target contributors", "Focus", "players", row => row.Features.TopTargetContributors),
            new("focused-window-rate", "Focused-window rate", "Focus", "%", row => row.Features.Focused ? 100.0 : 0.0),
            new("strip-sync-rate", "Strip-synced window rate", "Focus", "%", row => row.Features.StripSynced ? 100.0 : 0.0),
            new("target-saturation", "Target saturation", "Focus", "targets", row => row.Features.TargetSaturationCount),
            new("cleanses-per-player", "Cleanses per active player", "Support", "cleanses/player", row => row.Features.CleansesPerActivePlayer),
            new(
                "healing-per-player",
                "Healing per active player",
                "Support",
                "healing/player",
                row => row.Features.HealingPerActivePlayer,
                fight => IsHealingAvailable(fight, perspectiveSideId),
                $"{Capitalize(perspectiveSideId)} healing is not available in the source data."),
            new(
                "barrier-per-player",
                "Barrier per active player",
                "Support",
                "barrier/player",
                row => row.Features.BarrierPerActivePlayer,
                fight => IsBarrierAvailable(fight, perspectiveSideId),
                $"{Capitalize(perspectiveSideId)} barrier is not available in the source data."),
            new(
                "observed-squad-in-position",
                "Observed squad in-position rate",
                "Positioning",
                "%",
                row => row.Features.SquadInPositionRate,
                fight => fight.Cache.Availability.SquadPositioning,
                "Squad positioning is not available in the source data.",
                row => row.Features.SquadPositioningAvailable),
            new(
                "observed-squad-position-risk",
                "Observed squad positioning-risk rate",
                "Positioning",
                "%",
                row => row.Features.SquadPositioningRiskRate,
                fight => fight.Cache.Availability.SquadPositioning,
                "Squad positioning is not available in the source data.",
                row => row.Features.SquadPositioningAvailable),
        };

        if (!recovery)
        {
            definitions.AddRange(
            [
                new MetricDefinition(
                    "effective-cc",
                    "Effective crowd-control events",
                    "Control",
                    "events/window",
                    row => row.Features.EffectiveCrowdControlEvents,
                    fight => IsCrowdControlAvailable(fight, perspectiveSideId),
                    $"{Capitalize(perspectiveSideId)} crowd-control attribution is not available."),
                new MetricDefinition(
                    "cc-duration",
                    "Crowd-control duration",
                    "Control",
                    "seconds/window",
                    row => row.Features.CrowdControlDurationSeconds,
                    fight => IsCrowdControlAvailable(fight, perspectiveSideId),
                    $"{Capitalize(perspectiveSideId)} crowd-control attribution is not available."),
                new MetricDefinition(
                    "vulnerability-bonus-damage",
                    "Estimated Vulnerability bonus damage",
                    "Conditions",
                    "damage/window",
                    row => row.Features.VulnerabilityBonusDamage,
                    fight => IsConditionAvailable(fight, perspectiveSideId),
                    $"{Capitalize(perspectiveSideId)} condition attribution is not available."),
            ]);
        }

        if (conversion)
        {
            bool comparableDownWindowDamage = !string.Equals(
                perspectiveSideId,
                "enemy",
                StringComparison.OrdinalIgnoreCase);
            Func<FightRecord, bool> downWindowAvailability = _ => comparableDownWindowDamage;
            const string unavailableDownWindowReason =
                "Squad recovery events use the recovery-support view and do not export comparable incoming down-window damage.";
            definitions.AddRange(
            [
                new MetricDefinition(
                    "down-window-damage",
                    "Damage into the down window",
                    "Conversion",
                    "damage/down",
                    row => row.Features.OutcomeWindowDamage,
                    downWindowAvailability,
                    unavailableDownWindowReason),
                new MetricDefinition(
                    "down-window-strike-damage",
                    "Strike damage into the down window",
                    "Conversion",
                    "damage/down",
                    row => row.Features.OutcomeWindowStrikeDamage,
                    downWindowAvailability,
                    unavailableDownWindowReason),
                new MetricDefinition(
                    "down-window-condition-damage",
                    "Condition damage into the down window",
                    "Conversion",
                    "damage/down",
                    row => row.Features.OutcomeWindowConditionDamage,
                    downWindowAvailability,
                    unavailableDownWindowReason),
                new MetricDefinition(
                    "down-window-barrier-damage",
                    "Barrier damage into the down window",
                    "Conversion",
                    "damage/down",
                    row => row.Features.OutcomeWindowBarrierDamage,
                    downWindowAvailability,
                    unavailableDownWindowReason),
            ]);
        }

        if (recovery)
        {
            bool recoverySupportAvailable = string.Equals(
                perspectiveSideId,
                "squad",
                StringComparison.OrdinalIgnoreCase);
            Func<FightRecord, bool> recoverySupportAvailability = _ => recoverySupportAvailable;
            const string unavailableRecoverySupportReason =
                "Enemy recovery-support healing, resurrection casts, contributors, and class actions are not reconstructed.";
            definitions.AddRange(
            [
                new MetricDefinition(
                    "downed-healing",
                    "Downed-player healing",
                    "Recovery",
                    "healing/outcome",
                    row => row.Features.DownedHealing,
                    recoverySupportAvailability,
                    unavailableRecoverySupportReason),
                new MetricDefinition(
                    "downed-healing-events",
                    "Downed-healing events",
                    "Recovery",
                    "events/outcome",
                    row => row.Features.DownedHealingEvents,
                    recoverySupportAvailability,
                    unavailableRecoverySupportReason),
                new MetricDefinition(
                    "resurrection-casts",
                    "Resurrection casts",
                    "Recovery",
                    "casts/outcome",
                    row => row.Features.ResurrectionCasts,
                    recoverySupportAvailability,
                    unavailableRecoverySupportReason),
                new MetricDefinition(
                    "resurrection-cast-duration",
                    "Resurrection cast duration",
                    "Recovery",
                    "seconds/outcome",
                    row => row.Features.ResurrectionCastDurationSeconds,
                    recoverySupportAvailability,
                    unavailableRecoverySupportReason),
                new MetricDefinition(
                    "support-contributors",
                    "Recovery support contributors",
                    "Recovery",
                    "players/outcome",
                    row => row.Features.SupportContributors,
                    recoverySupportAvailability,
                    unavailableRecoverySupportReason),
                new MetricDefinition(
                    "class-recovery-actions",
                    "Class-specific recovery actions",
                    "Recovery",
                    "actions/outcome",
                    row => row.Features.ClassRecoveryActions,
                    recoverySupportAvailability,
                    unavailableRecoverySupportReason),
            ]);
        }

        return definitions.ToArray();
    }

    private static HistoricalEffectivenessMetricDto BuildMetric(
        IReadOnlyList<FightCohort> cohorts,
        MetricDefinition definition)
    {
        FightCohort[] availableCohorts = cohorts
            .Where(cohort => definition.Availability(cohort.Fight))
            .Select(cohort => new FightCohort(
                cohort.Fight,
                cohort.OutcomeRows.Where(definition.ObservationAvailability).ToArray(),
                cohort.BaselineRows.Where(definition.ObservationAvailability).ToArray()))
            .ToArray();
        FightCohort[] pairedCohorts = availableCohorts
            .Where(cohort => cohort.OutcomeRows.Length > 0 && cohort.BaselineRows.Length > 0)
            .ToArray();
        ComparisonStatistics? statistics = CalculateStatistics(
            pairedCohorts,
            definition.Selector,
            definition.Selector);
        int outcomeFightCount = availableCohorts.Count(cohort => cohort.OutcomeRows.Length > 0);
        int baselineFightCount = availableCohorts.Count(cohort => cohort.BaselineRows.Length > 0);
        int outcomeObservationCount = pairedCohorts.Sum(cohort => cohort.OutcomeRows.Length);
        int baselineObservationCount = pairedCohorts.Sum(cohort => cohort.BaselineRows.Length);

        if (statistics is null)
        {
            return new HistoricalEffectivenessMetricDto(
                Key: definition.Key,
                Label: definition.Label,
                Group: definition.Group,
                Unit: definition.Unit,
                Available: false,
                UnavailableReason: availableCohorts.Length == 0
                    ? definition.UnavailableReason
                    : "The selected fights do not contain both outcome and baseline observations for this metric.",
                OutcomeAverage: null,
                BaselineAverage: null,
                Difference: null,
                PercentLift: null,
                Lower95Difference: null,
                Upper95Difference: null,
                PositiveDifferenceFightPercent: null,
                DirectionConsistencyPercent: null,
                StandardizedDifference: null,
                OutcomeObservationCount: outcomeObservationCount,
                BaselineObservationCount: baselineObservationCount,
                OutcomeFightCount: outcomeFightCount,
                BaselineFightCount: baselineFightCount,
                PairedFightCount: 0,
                NonTieFightCount: 0,
                EvidenceScore: 0,
                AssociationScore: 0,
                ConfidenceLabel: "Unavailable",
                DirectionLabel: "Unavailable",
                Detail: availableCohorts.Length == 0
                    ? definition.UnavailableReason
                    : "No paired fights were available.");
        }

        return new HistoricalEffectivenessMetricDto(
            Key: definition.Key,
            Label: definition.Label,
            Group: definition.Group,
            Unit: definition.Unit,
            Available: true,
            UnavailableReason: null,
            OutcomeAverage: statistics.OutcomeAverage,
            BaselineAverage: statistics.BaselineAverage,
            Difference: statistics.Difference,
            PercentLift: statistics.PercentLift,
            Lower95Difference: statistics.Lower95Difference,
            Upper95Difference: statistics.Upper95Difference,
            PositiveDifferenceFightPercent: statistics.PositiveDifferenceFightPercent,
            DirectionConsistencyPercent: statistics.DirectionConsistencyPercent,
            StandardizedDifference: statistics.StandardizedDifference,
            OutcomeObservationCount: outcomeObservationCount,
            BaselineObservationCount: baselineObservationCount,
            OutcomeFightCount: outcomeFightCount,
            BaselineFightCount: baselineFightCount,
            PairedFightCount: statistics.PairedFightCount,
            NonTieFightCount: statistics.NonTieFightCount,
            EvidenceScore: statistics.EvidenceScore,
            AssociationScore: statistics.AssociationScore,
            ConfidenceLabel: statistics.ConfidenceLabel,
            DirectionLabel: statistics.DirectionLabel,
            Detail: BuildMetricDetail(statistics));
    }

    private static IReadOnlyList<HistoricalEffectivenessNamedEffectDto> BuildNamedEffects(
        IReadOnlyList<FightCohort> cohorts,
        string perspectiveSideId)
    {
        var conditionKeys = cohorts
            .SelectMany(cohort => cohort.OutcomeRows.Concat(cohort.BaselineRows))
            .SelectMany(row => row.Conditions)
            .Select(effect => new NamedEffectKey("condition", effect.BuffId, effect.Name))
            .Distinct()
            .ToArray();
        var crowdControlKeys = cohorts
            .SelectMany(cohort => cohort.OutcomeRows.Concat(cohort.BaselineRows))
            .SelectMany(row => row.CrowdControl)
            .Select(effect => new NamedEffectKey("cc", effect.SkillId, effect.Name))
            .Distinct()
            .ToArray();

        var effects = new List<HistoricalEffectivenessNamedEffectDto>();
        effects.AddRange(conditionKeys.Select(key => BuildNamedEffect(
            cohorts,
            key,
            perspectiveSideId,
            row => row.Conditions
                .Where(effect => effect.BuffId == key.EffectId && string.Equals(effect.Name, key.Name, StringComparison.Ordinal))
                .Sum(effect => effect.ApplyCount + effect.ExtensionCount),
            row => row.Conditions
                .Where(effect => effect.BuffId == key.EffectId && string.Equals(effect.Name, key.Name, StringComparison.Ordinal))
                .Sum(effect => effect.DirectDamage),
            "applications/window",
            "condition damage/window")));
        effects.AddRange(crowdControlKeys.Select(key => BuildNamedEffect(
            cohorts,
            key,
            perspectiveSideId,
            row => row.CrowdControl
                .Where(effect => effect.SkillId == key.EffectId && string.Equals(effect.Name, key.Name, StringComparison.Ordinal))
                .Sum(effect => effect.EffectiveCount),
            row => row.CrowdControl
                .Where(effect => effect.SkillId == key.EffectId && string.Equals(effect.Name, key.Name, StringComparison.Ordinal))
                .Sum(effect => effect.DurationSeconds),
            "effective events/window",
            "seconds/window")));

        HistoricalEffectivenessNamedEffectDto[] scored = NormalizeNamedAssociationScores(effects);
        HistoricalEffectivenessNamedEffectDto[] sorted = scored
            .OrderByDescending(effect => effect.EligibleForRanking)
            .ThenByDescending(effect => effect.AssociationScore)
            .ThenByDescending(effect => effect.EvidenceScore)
            .ThenBy(effect => effect.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        int rank = 0;
        return sorted
            .Select(effect => effect.EligibleForRanking
                ? effect with
                {
                    Rank = ++rank,
                    RankingResult = "Ranked by association score.",
                }
                : effect)
            .ToArray();
    }

    private static HistoricalEffectivenessNamedEffectDto[] NormalizeNamedAssociationScores(
        IReadOnlyList<HistoricalEffectivenessNamedEffectDto> effects)
    {
        var maximumDifferenceByType = effects
            .Where(effect => effect.Difference.HasValue)
            .GroupBy(effect => effect.EffectType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Max(effect => Math.Abs(effect.Difference!.Value)),
                StringComparer.OrdinalIgnoreCase);

        return effects
            .Select(effect =>
            {
                if (!effect.Difference.HasValue ||
                    !effect.OutcomeAverage.HasValue ||
                    !effect.BaselineAverage.HasValue ||
                    !maximumDifferenceByType.TryGetValue(effect.EffectType, out double maximumDifference) ||
                    maximumDifference <= EqualityTolerance)
                {
                    return effect with { AssociationScore = 0 };
                }

                double relativeStrength = Math.Min(
                    1.0,
                    Math.Abs(effect.Difference.Value) /
                    (Math.Abs(effect.OutcomeAverage.Value) + Math.Abs(effect.BaselineAverage.Value) + EqualityTolerance));
                double magnitudeStrength = Math.Min(
                    1.0,
                    Math.Abs(effect.Difference.Value) / maximumDifference);
                double combinedStrength = Math.Sqrt(relativeStrength * magnitudeStrength);
                return effect with
                {
                    AssociationScore = Round(effect.EvidenceScore * combinedStrength),
                };
            })
            .ToArray();
    }

    private static HistoricalEffectivenessNamedEffectDto BuildNamedEffect(
        IReadOnlyList<FightCohort> cohorts,
        NamedEffectKey key,
        string perspectiveSideId,
        Func<FightOutcomeObservationDto, double> primarySelector,
        Func<FightOutcomeObservationDto, double> secondarySelector,
        string unit,
        string secondaryUnit)
    {
        FightCohort[] availableCohorts = cohorts
            .Where(cohort => key.EffectType == "condition"
                ? IsConditionAvailable(cohort.Fight, perspectiveSideId)
                : IsCrowdControlAvailable(cohort.Fight, perspectiveSideId))
            .Where(cohort => cohort.OutcomeRows.Length > 0 && cohort.BaselineRows.Length > 0)
            .ToArray();
        ComparisonStatistics? statistics = CalculateStatistics(
            availableCohorts,
            primarySelector,
            primarySelector,
            useNonTieFightSupport: true);
        if (statistics is null)
        {
            return new HistoricalEffectivenessNamedEffectDto(
                Rank: null,
                Key: $"{key.EffectType}:{key.EffectId}",
                Name: key.Name,
                EffectType: key.EffectType,
                EffectId: key.EffectId,
                Unit: unit,
                EligibleForRanking: false,
                RankingResult: "No paired fights were available.",
                OutcomeAverage: null,
                BaselineAverage: null,
                Difference: null,
                PercentLift: null,
                OutcomePresencePercent: null,
                BaselinePresencePercent: null,
                PresenceDifferencePoints: null,
                OutcomeSecondaryAverage: null,
                BaselineSecondaryAverage: null,
                SecondaryUnit: secondaryUnit,
                Lower95Difference: null,
                Upper95Difference: null,
                DirectionConsistencyPercent: null,
                OutcomeObservationCount: 0,
                BaselineObservationCount: 0,
                PairedFightCount: 0,
                NonTieFightCount: 0,
                EvidenceScore: 0,
                AssociationScore: 0,
                ConfidenceLabel: "Unavailable",
                DirectionLabel: "Unavailable",
                Detail: "No paired fights were available.");
        }

        FightOutcomeObservationDto[] outcomeRows = availableCohorts.SelectMany(cohort => cohort.OutcomeRows).ToArray();
        FightOutcomeObservationDto[] baselineRows = availableCohorts.SelectMany(cohort => cohort.BaselineRows).ToArray();
        int outcomePresenceCount = outcomeRows.Count(row => primarySelector(row) > EqualityTolerance);
        int baselinePresenceCount = baselineRows.Count(row => primarySelector(row) > EqualityTolerance);
        int totalPresenceCount = outcomePresenceCount + baselinePresenceCount;
        bool eligible = statistics.PairedFightCount >= MinimumPairedFights &&
            statistics.NonTieFightCount >= MinimumPairedFights &&
            totalPresenceCount >= 10;
        string rankingResult = eligible
            ? "Eligible for ranking."
            : statistics.PairedFightCount < MinimumPairedFights
                ? $"Needs at least {MinimumPairedFights} paired fights."
                : statistics.NonTieFightCount < MinimumPairedFights
                    ? $"Below cutoff: changed in only {statistics.NonTieFightCount} paired fights."
                    : $"Below cutoff: present in only {totalPresenceCount} observations.";

        return new HistoricalEffectivenessNamedEffectDto(
            Rank: null,
            Key: $"{key.EffectType}:{key.EffectId}",
            Name: key.Name,
            EffectType: key.EffectType,
            EffectId: key.EffectId,
            Unit: unit,
            EligibleForRanking: eligible,
            RankingResult: rankingResult,
            OutcomeAverage: statistics.OutcomeAverage,
            BaselineAverage: statistics.BaselineAverage,
            Difference: statistics.Difference,
            PercentLift: statistics.PercentLift,
            OutcomePresencePercent: RoundPercent(outcomePresenceCount, outcomeRows.Length),
            BaselinePresencePercent: RoundPercent(baselinePresenceCount, baselineRows.Length),
            PresenceDifferencePoints: Round(
                RoundPercent(outcomePresenceCount, outcomeRows.Length) -
                RoundPercent(baselinePresenceCount, baselineRows.Length)),
            OutcomeSecondaryAverage: Round(availableCohorts.Average(cohort =>
                cohort.OutcomeRows.Average(secondarySelector))),
            BaselineSecondaryAverage: Round(availableCohorts.Average(cohort =>
                cohort.BaselineRows.Average(secondarySelector))),
            SecondaryUnit: secondaryUnit,
            Lower95Difference: statistics.Lower95Difference,
            Upper95Difference: statistics.Upper95Difference,
            DirectionConsistencyPercent: statistics.DirectionConsistencyPercent,
            OutcomeObservationCount: outcomeRows.Length,
            BaselineObservationCount: baselineRows.Length,
            PairedFightCount: statistics.PairedFightCount,
            NonTieFightCount: statistics.NonTieFightCount,
            EvidenceScore: statistics.EvidenceScore,
            AssociationScore: statistics.AssociationScore,
            ConfidenceLabel: statistics.ConfidenceLabel,
            DirectionLabel: statistics.DirectionLabel,
            Detail: $"{BuildMetricDetail(statistics)} Presence is measured separately so frequent zero-value ties cannot inflate the ranking.");
    }

    private static ComparisonStatistics? CalculateStatistics(
        IReadOnlyList<FightCohort> pairedCohorts,
        Func<FightOutcomeObservationDto, double> outcomeSelector,
        Func<FightOutcomeObservationDto, double> baselineSelector,
        bool useNonTieFightSupport = false)
    {
        if (pairedCohorts.Count == 0)
        {
            return null;
        }

        var pairedMeans = pairedCohorts
            .Select(cohort =>
            {
                double outcomeAverage = cohort.OutcomeRows.Average(outcomeSelector);
                double baselineAverage = cohort.BaselineRows.Average(baselineSelector);
                return new PairedMean(outcomeAverage, baselineAverage, outcomeAverage - baselineAverage);
            })
            .ToArray();
        double outcomeMean = pairedMeans.Average(pair => pair.OutcomeAverage);
        double baselineMean = pairedMeans.Average(pair => pair.BaselineAverage);
        double difference = pairedMeans.Average(pair => pair.Difference);
        double[] differences = pairedMeans.Select(pair => pair.Difference).ToArray();
        int positiveCount = differences.Count(value => value > EqualityTolerance);
        int negativeCount = differences.Count(value => value < -EqualityTolerance);
        int tieCount = differences.Length - positiveCount - negativeCount;
        int nonTieCount = positiveCount + negativeCount;
        double? sampleStandardDeviation = CalculateSampleStandardDeviation(differences, difference);
        double? standardError = sampleStandardDeviation.HasValue
            ? sampleStandardDeviation.Value / Math.Sqrt(differences.Length)
            : null;
        double? lower95 = standardError.HasValue
            ? difference - Normal95CriticalValue * standardError.Value
            : null;
        double? upper95 = standardError.HasValue
            ? difference + Normal95CriticalValue * standardError.Value
            : null;
        double? standardized = sampleStandardDeviation > EqualityTolerance
            ? difference / sampleStandardDeviation.Value
            : null;
        double percentLift = Math.Abs(baselineMean) > EqualityTolerance
            ? difference / Math.Abs(baselineMean) * 100.0
            : double.NaN;
        double positivePercent = positiveCount * 100.0 / differences.Length;
        double consistencyPercent = difference switch
        {
            > EqualityTolerance => (positiveCount + tieCount * 0.5) * 100.0 / differences.Length,
            < -EqualityTolerance => (negativeCount + tieCount * 0.5) * 100.0 / differences.Length,
            _ => 50.0,
        };
        int outcomeObservationCount = pairedCohorts.Sum(cohort => cohort.OutcomeRows.Length);
        int baselineObservationCount = pairedCohorts.Sum(cohort => cohort.BaselineRows.Length);
        int supportFightCount = useNonTieFightSupport ? nonTieCount : differences.Length;
        double fightSupport = Math.Min(1.0, supportFightCount / (double)TargetPairedFights);
        double rowBalance = Math.Min(outcomeObservationCount, baselineObservationCount) /
            (double)Math.Max(outcomeObservationCount, baselineObservationCount);
        double nonTieCoverage = nonTieCount / (double)differences.Length;
        double intervalHalfWidth = standardError.HasValue
            ? Normal95CriticalValue * standardError.Value
            : double.PositiveInfinity;
        double precision = double.IsFinite(intervalHalfWidth)
            ? Math.Abs(difference) / (Math.Abs(difference) + intervalHalfWidth + EqualityTolerance)
            : 0.0;
        double evidenceScore = 100.0 * (
            0.45 * fightSupport +
            0.20 * rowBalance +
            0.20 * nonTieCoverage +
            0.15 * precision);
        double relativeDifference = Math.Min(
            1.0,
            Math.Abs(difference) /
            (Math.Abs(outcomeMean) + Math.Abs(baselineMean) + EqualityTolerance));
        double associationScore = evidenceScore * relativeDifference;

        return new ComparisonStatistics(
            OutcomeAverage: Round(outcomeMean),
            BaselineAverage: Round(baselineMean),
            Difference: Round(difference),
            PercentLift: double.IsNaN(percentLift) ? null : Round(percentLift),
            Lower95Difference: lower95.HasValue ? Round(lower95.Value) : null,
            Upper95Difference: upper95.HasValue ? Round(upper95.Value) : null,
            PositiveDifferenceFightPercent: Round(positivePercent),
            DirectionConsistencyPercent: Round(consistencyPercent),
            StandardizedDifference: standardized.HasValue ? Round(standardized.Value) : null,
            PairedFightCount: differences.Length,
            NonTieFightCount: nonTieCount,
            EvidenceScore: Round(evidenceScore),
            AssociationScore: Round(associationScore),
            ConfidenceLabel: GetMetricConfidence(
                supportFightCount,
                nonTieCoverage,
                lower95,
                upper95),
            DirectionLabel: difference switch
            {
                > EqualityTolerance => "Higher in outcome windows",
                < -EqualityTolerance => "Lower in outcome windows",
                _ => "No observed difference",
            });
    }

    private static double? CalculateSampleStandardDeviation(double[] values, double mean)
    {
        if (values.Length < 2)
        {
            return null;
        }

        return Math.Sqrt(values.Sum(value => Math.Pow(value - mean, 2)) / (values.Length - 1));
    }

    private static string GetMetricConfidence(
        int pairedFightCount,
        double nonTieCoverage,
        double? lower95,
        double? upper95)
    {
        if (pairedFightCount < MinimumPairedFights)
        {
            return "Insufficient";
        }

        bool intervalExcludesZero = lower95.HasValue &&
            upper95.HasValue &&
            (lower95.Value > 0 || upper95.Value < 0);
        if (pairedFightCount >= TargetPairedFights && intervalExcludesZero && nonTieCoverage >= 0.6)
        {
            return "High";
        }

        if (pairedFightCount >= 10 && (intervalExcludesZero || nonTieCoverage >= 0.6))
        {
            return "Medium";
        }

        return "Low";
    }

    private static string GetReportConfidence(int pairedFightCount)
    {
        return pairedFightCount switch
        {
            >= TargetPairedFights => "High sample coverage",
            >= 10 => "Medium sample coverage",
            >= MinimumPairedFights => "Low sample coverage",
            _ => "Insufficient paired fights",
        };
    }

    private static string BuildMetricDetail(ComparisonStatistics statistics)
    {
        string interval = statistics.Lower95Difference.HasValue && statistics.Upper95Difference.HasValue
            ? $"Approximate 95% difference interval: {statistics.Lower95Difference:n3} to {statistics.Upper95Difference:n3}."
            : "An interval needs at least two paired fights.";
        return $"{statistics.PairedFightCount} paired fights; {statistics.NonTieFightCount} had a non-zero difference. {interval}";
    }

    private static bool IsHealingAvailable(FightRecord fight, string perspectiveSideId)
    {
        return string.Equals(perspectiveSideId, "squad", StringComparison.OrdinalIgnoreCase)
            ? fight.Cache.Availability.SquadHealing
            : fight.Cache.Availability.EnemyHealing;
    }

    private static bool IsBarrierAvailable(FightRecord fight, string perspectiveSideId)
    {
        return string.Equals(perspectiveSideId, "squad", StringComparison.OrdinalIgnoreCase)
            ? fight.Cache.Availability.SquadBarrier
            : fight.Cache.Availability.EnemyBarrier;
    }

    private static bool IsConditionAvailable(FightRecord fight, string perspectiveSideId)
    {
        return string.Equals(perspectiveSideId, "squad", StringComparison.OrdinalIgnoreCase)
            ? fight.Cache.Availability.SquadConditionApplications
            : fight.Cache.Availability.EnemyConditionApplications;
    }

    private static bool IsCrowdControlAvailable(FightRecord fight, string perspectiveSideId)
    {
        return string.Equals(perspectiveSideId, "squad", StringComparison.OrdinalIgnoreCase)
            ? fight.Cache.Availability.SquadCrowdControlEvents
            : fight.Cache.Availability.EnemyCrowdControlEvents;
    }

    private static double AverageOrZero(
        IReadOnlyList<FightOutcomeObservationDto> rows,
        Func<FightOutcomeObservationDto, double> selector)
    {
        return rows.Count > 0 ? rows.Average(selector) : 0.0;
    }

    private static double RoundPercent(int numerator, int denominator)
    {
        return denominator > 0 ? Round(numerator * 100.0 / denominator) : 0.0;
    }

    private static double Round(double value)
    {
        return Math.Round(value, 3);
    }

    private static string Capitalize(string value)
    {
        return value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
    }

    private sealed record SelectedFight(
        string FightId,
        FightAnalysisTrendPointDto Trend,
        FightArtifactSummaryDto? Summary,
        FightOutcomeObservationCacheDto? Cache);

    private sealed record FightRecord(
        string FightId,
        FightArtifactSummaryDto? Summary,
        FightAnalysisTrendPointDto Trend,
        FightOutcomeObservationCacheDto Cache);

    private sealed record FightObservation(
        FightRecord Fight,
        FightOutcomeObservationDto Observation);

    private sealed record FightCohort(
        FightRecord Fight,
        FightOutcomeObservationDto[] OutcomeRows,
        FightOutcomeObservationDto[] BaselineRows);

    private sealed record MetricDefinition(
        string Key,
        string Label,
        string Group,
        string Unit,
        Func<FightOutcomeObservationDto, double> Selector,
        Func<FightRecord, bool>? IsAvailable = null,
        string UnavailableReason = "This metric is unavailable.",
        Func<FightOutcomeObservationDto, bool>? IsObservationAvailable = null)
    {
        public Func<FightRecord, bool> Availability { get; } = IsAvailable ?? (_ => true);
        public Func<FightOutcomeObservationDto, bool> ObservationAvailability { get; } =
            IsObservationAvailable ?? (_ => true);
    }

    private sealed record NamedEffectKey(
        string EffectType,
        long EffectId,
        string Name);

    private sealed record PairedMean(
        double OutcomeAverage,
        double BaselineAverage,
        double Difference);

    private sealed record ComparisonStatistics(
        double OutcomeAverage,
        double BaselineAverage,
        double Difference,
        double? PercentLift,
        double? Lower95Difference,
        double? Upper95Difference,
        double PositiveDifferenceFightPercent,
        double DirectionConsistencyPercent,
        double? StandardizedDifference,
        int PairedFightCount,
        int NonTieFightCount,
        double EvidenceScore,
        double AssociationScore,
        string ConfidenceLabel,
        string DirectionLabel);
}
