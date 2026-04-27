using System.Globalization;
using WvWAnalyst.Api.Bridge;
using WvWAnalyst.Api.Services;
using WvWAnalyst.Contracts;

namespace WvWAnalyst.Api.Analysis;

public sealed class FightAnalysisService
{
    private const int MinimumClassSampleCount = 40;
    private const int MinimumClassPlayerFightCount = 20;
    private const double CleanupPhaseImpactWeight = 0.25;
    private const int MinimumExpectedScoreSampleCount = 5;
    private const int MediumExpectedScoreSampleCount = 15;
    private const int GoodExpectedScoreSampleCount = 30;
    private const double StrictExpectedScoreSizeRatioTolerance = 0.20;
    private const double BroadExpectedScoreSizeRatioTolerance = 0.35;
    private const double StrictExpectedScoreClassMixTolerance = 0.35;
    private const double BroadExpectedScoreClassMixTolerance = 0.55;

    private static readonly string[] ExpectedScoreContextAttributeKeys =
    [
        "three-way",
        "cloudy-fight",
        "stack-clash",
        "organized-enemy",
        "tight-enemy",
        "elite-tight-enemy"
    ];

    private readonly FightCatalogService _fightCatalog;
    private readonly PatchMetadataService _patchMetadata;
    private readonly FightAttributeService _fightAttributes;

    public FightAnalysisService(
        FightCatalogService fightCatalog,
        PatchMetadataService patchMetadata,
        FightAttributeService fightAttributes)
    {
        _fightCatalog = fightCatalog;
        _patchMetadata = patchMetadata;
        _fightAttributes = fightAttributes;
    }

    public FightAnalysisSnapshotDto BuildSnapshot(
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
        var allFights = _fightCatalog.GetFightBrowserSnapshot().Fights
            .Where(fight => fight.FightIndex is not null)
            .Where(fight => string.Equals(fight.Status, "Imported", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var patchMetadata = _patchMetadata.GetMetadata();
        var patchSelection = NormalizePatchSelection(patchScope, patchEraIds, patchMetadata);

        var commanderOptions = allFights
            .SelectMany(fight => fight.FightIndex?.CommanderDisplayNames ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var classOptions = BuildClassOptions(allFights);

        var minDate = allFights
            .Select(GetFightLocalDate)
            .Where(date => date.HasValue)
            .Min();

        var maxDate = allFights
            .Select(GetFightLocalDate)
            .Where(date => date.HasValue)
            .Max();

        string normalizedOutcome = NormalizeOutcomeFilter(outcomeCode);
        string? normalizedCommander = string.IsNullOrWhiteSpace(commander) ? null : commander.Trim();
        DateOnly? normalizedStartDate = ParseDateOnly(startDate);
        DateOnly? normalizedEndDate = ParseDateOnly(endDate);
        var normalizedSquadIncludeClasses = NormalizeClassFilter(squadIncludeClasses);
        var normalizedSquadExcludeClasses = NormalizeClassFilter(squadExcludeClasses);
        var normalizedEnemyIncludeClasses = NormalizeClassFilter(enemyIncludeClasses);
        var normalizedEnemyExcludeClasses = NormalizeClassFilter(enemyExcludeClasses);
        var normalizedFightAttributes = NormalizeTokenFilter(fightAttributes);

        var filteredFights = allFights
            .Where(fight => MatchesCommander(fight, normalizedCommander))
            .Where(fight => MatchesOutcome(fight, normalizedOutcome))
            .Where(fight => MatchesDateRange(fight, normalizedStartDate, normalizedEndDate))
            .Where(fight => MatchesSideClassFilters(fight, "squad", normalizedSquadIncludeClasses, normalizedSquadExcludeClasses))
            .Where(fight => MatchesSideClassFilters(fight, "enemy", normalizedEnemyIncludeClasses, normalizedEnemyExcludeClasses))
            .Where(fight => MatchesPatchSelection(fight, patchSelection))
            .Where(fight => MatchesFightAttributeSelection(fight, normalizedFightAttributes))
            .OrderBy(fight => GetFightSortValue(fight))
            .ToArray();

        var expectedScoreLookup = BuildExpectedScoreLookup(allFights);
        var trackedPlayerSamples = GetTrackedPlayerSamples(allFights);
        var totalPlayerFightCounts = BuildTotalPlayerFightCounts(trackedPlayerSamples);
        var totalCharacterFightCounts = BuildTotalCharacterFightCounts(trackedPlayerSamples);
        var totalCharacterLaneSampleCounts = BuildTotalCharacterLaneSampleCounts(trackedPlayerSamples);
        var totalClassPlayerFightCounts = BuildTotalClassPlayerFightCounts(allFights);

        return new FightAnalysisSnapshotDto(
            Options: new FightAnalysisFilterOptionsDto(
                Commanders: commanderOptions,
                ClassOptions: classOptions,
                MinFightDate: minDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                MaxFightDate: maxDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                PatchEras: patchMetadata.PatchEras,
                FightAttributes: _fightAttributes.GetDefinitions()),
            Selection: new FightAnalysisSelectionDto(
                Commander: normalizedCommander,
                StartDate: normalizedStartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                EndDate: normalizedEndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                OutcomeCode: normalizedOutcome,
                SquadIncludeClasses: normalizedSquadIncludeClasses,
                SquadExcludeClasses: normalizedSquadExcludeClasses,
                EnemyIncludeClasses: normalizedEnemyIncludeClasses,
                EnemyExcludeClasses: normalizedEnemyExcludeClasses,
                PatchScope: patchSelection.Scope,
                PatchEraIds: patchSelection.EraIds,
                FightAttributeKeys: normalizedFightAttributes),
            Scope: BuildScope(allFights, filteredFights),
            Overview: BuildOverview(filteredFights, expectedScoreLookup),
            Trends: BuildTrends(filteredFights, expectedScoreLookup),
            TopPlayers: BuildTopPlayers(filteredFights, totalPlayerFightCounts, totalCharacterFightCounts, totalCharacterLaneSampleCounts),
            TopClasses: BuildTopClasses(filteredFights, totalClassPlayerFightCounts, GetPatchImpactsForSelection(patchMetadata, patchSelection)),
            TopLanes: BuildTopLanes(filteredFights),
            TopBoons: BuildTopBoons(filteredFights));
    }

    private static FightAnalysisScopeDto BuildScope(
        IReadOnlyList<FightArtifactSummaryDto> allFights,
        IReadOnlyList<FightArtifactSummaryDto> filteredFights)
    {
        int wins = filteredFights.Count(fight => string.Equals(fight.FightIndex?.Outcome.OutcomeCode, "squad", StringComparison.OrdinalIgnoreCase));
        int losses = filteredFights.Count(fight => string.Equals(fight.FightIndex?.Outcome.OutcomeCode, "enemy", StringComparison.OrdinalIgnoreCase));
        int draws = filteredFights.Count(fight => string.Equals(fight.FightIndex?.Outcome.OutcomeCode, "draw", StringComparison.OrdinalIgnoreCase));
        double winRate = filteredFights.Count == 0 ? 0.0 : Math.Round(wins * 100.0 / filteredFights.Count, 1);

        return new FightAnalysisScopeDto(
            TotalImportedFights: allFights.Count,
            FilteredFightCount: filteredFights.Count,
            WinCount: wins,
            LossCount: losses,
            DrawCount: draws,
            WinRatePercent: winRate,
            PatchEraBreakdown: BuildPatchEraBreakdown(filteredFights),
            AttributeBreakdown: BuildAttributeBreakdown(filteredFights));
    }

    private static IReadOnlyList<FightAnalysisBreakdownDto> BuildPatchEraBreakdown(IReadOnlyList<FightArtifactSummaryDto> fights)
    {
        return fights
            .GroupBy(fight => fight.PatchEra?.Id ?? "unknown", StringComparer.OrdinalIgnoreCase)
            .Select(group => new FightAnalysisBreakdownDto(
                Key: group.Key,
                Label: group.Select(fight => fight.PatchEra?.Label).FirstOrDefault(label => !string.IsNullOrWhiteSpace(label)) ?? "Unknown patch",
                FightCount: group.Count()))
            .OrderByDescending(entry => entry.FightCount)
            .ThenBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<FightAnalysisBreakdownDto> BuildAttributeBreakdown(IReadOnlyList<FightArtifactSummaryDto> fights)
    {
        return fights
            .SelectMany(fight => fight.Attributes ?? Array.Empty<FightAttributeDto>())
            .GroupBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new FightAnalysisBreakdownDto(
                Key: group.Key,
                Label: group.First().Label,
                FightCount: group.Count()))
            .OrderByDescending(entry => entry.FightCount)
            .ThenBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static FightAnalysisOverviewDto BuildOverview(
        IReadOnlyList<FightArtifactSummaryDto> fights,
        IReadOnlyDictionary<string, FightExpectedScoreResult> expectedScoreLookup)
    {
        var executionScoreSamples = fights
            .Select(fight => new
            {
                Fight = fight,
                Execution = fight.FightIndex?.Execution
            })
            .Where(sample => sample.Execution?.ScoreAvailable == true && sample.Execution.OverallScore.HasValue)
            .ToArray();

        var pillarLookup = fights
            .SelectMany(fight => (fight.FightIndex?.Execution?.Pillars ?? Array.Empty<FightExecutionPillarIndexDto>())
                .Where(pillar => !string.IsNullOrWhiteSpace(pillar.PillarId))
                .Select(pillar => new
                {
                    Fight = fight,
                    Pillar = pillar
                }))
            .GroupBy(sample => sample.Pillar.PillarId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => CleanupAdjustedAverage(group.ToArray(), sample => (double)sample.Pillar.Score, sample => sample.Fight),
                StringComparer.OrdinalIgnoreCase);

        var gradeSource = executionScoreSamples
            .Select(sample => sample.Execution!.Grade)
            .Where(grade => !string.IsNullOrWhiteSpace(grade))
            .GroupBy(grade => grade!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?.Key;

        var expectedScoreSamples = fights
            .Select(fight => new
            {
                Fight = fight,
                Expected = expectedScoreLookup.TryGetValue(fight.FightId, out var expected) ? expected : null
            })
            .Where(sample => sample.Expected is not null)
            .ToArray();
        var expectedScoreResults = expectedScoreSamples
            .Select(sample => sample.Expected!)
            .ToArray();
        var medianExpectedScoreSampleCount = expectedScoreResults.Length == 0
            ? 0
            : (int)Math.Round(CalculateMedian(expectedScoreResults.Select(score => (double)score.SampleCount)));

        double averageSquadSize = fights.Count == 0 ? 0.0 : Math.Round(fights.Average(fight => fight.FightIndex?.SquadPlayerCount ?? 0), 1);
        double averageEnemySize = fights.Count == 0 ? 0.0 : Math.Round(fights.Average(fight => fight.FightIndex?.EnemyPlayerCount ?? fight.FightIndex?.EnemyTargetCount ?? 0), 1);
        double averageDurationSeconds = fights.Count == 0
            ? 0.0
            : Math.Round(fights.Average(fight => (fight.FightIndex?.DurationMilliseconds ?? 0) / 1000.0), 1);
        var mitigationSummary = BuildMitigationSummary(fights);
        var obliterateSummary = BuildObliterateSummary(fights);

        return new FightAnalysisOverviewDto(
            AverageOverallScore: executionScoreSamples.Length == 0
                ? null
                : Math.Round(CleanupAdjustedAverage(
                    executionScoreSamples,
                    sample => (double)sample.Execution!.OverallScore!.Value,
                    sample => sample.Fight), 1),
            AverageOverallGrade: gradeSource,
            AverageExpectedScore: expectedScoreSamples.Length == 0
                ? null
                : Math.Round(CleanupAdjustedAverage(
                    expectedScoreSamples,
                    sample => sample.Expected!.ExpectedScore,
                    sample => sample.Fight), 1),
            AverageContextDelta: expectedScoreSamples.Length == 0
                ? null
                : Math.Round(CleanupAdjustedAverage(
                    expectedScoreSamples,
                    sample => sample.Expected!.ContextDelta,
                    sample => sample.Fight), 1),
            ContextDeltaConfidenceLabel: expectedScoreSamples.Length == 0
                ? null
                : DeriveExpectedScoreConfidenceLabel(medianExpectedScoreSampleCount),
            ContextDeltaSampleCount: expectedScoreSamples.Length,
            ContextDeltaDetail: BuildContextDeltaOverviewDetail(fights.Count, expectedScoreResults),
            AverageCohesionScore: GetPillarAverage(pillarLookup, "cohesion-positioning"),
            AveragePressureScore: GetPillarAverage(pillarLookup, "pressure-burst"),
            AverageDownstateScore: GetPillarAverage(pillarLookup, "downstate-control"),
            AverageResilienceScore: GetPillarAverage(pillarLookup, "resilience-stabilization"),
            AverageSquadSize: averageSquadSize,
            AverageEnemySize: averageEnemySize,
            AverageDurationSeconds: averageDurationSeconds,
            MitigationSummary: mitigationSummary,
            ObliterateSummary: obliterateSummary);
    }

    private static FightAnalysisMitigationSummaryDto? BuildMitigationSummary(IReadOnlyList<FightArtifactSummaryDto> fights)
    {
        var mitigationSamples = fights
            .Select(fight => new
            {
                Mitigation = fight.FightIndex?.MitigationSummary,
                DefenseSaves = fight.FightIndex?.DefenseSaves,
            })
            .Where(sample => sample.Mitigation is not null || sample.DefenseSaves is not null)
            .ToArray();

        if (mitigationSamples.Length == 0)
        {
            return null;
        }

        int totalSaves = mitigationSamples.Sum(sample => sample.Mitigation?.SavedCases ?? sample.DefenseSaves?.SavedCases ?? 0);
        int totalBarrierSaves = mitigationSamples.Sum(sample => sample.Mitigation?.BarrierSavedCases ?? sample.DefenseSaves?.BarrierSavedCases ?? 0);
        int totalDamageReductionSaves = mitigationSamples.Sum(sample => sample.Mitigation?.DamageReductionSavedCases ?? sample.DefenseSaves?.DamageReductionSavedCases ?? 0);
        int totalNegatedDamageSaves = mitigationSamples.Sum(sample => sample.Mitigation?.NegatedDamageSavedCases ?? 0);
        int totalBothSaves = mitigationSamples.Sum(sample => sample.Mitigation?.BothSavedCases ?? sample.DefenseSaves?.BothSavedCases ?? 0);
        int totalMultiSourceSaves = mitigationSamples.Sum(sample => sample.Mitigation?.MultiSourceSavedCases ?? 0);
        double totalDamageToSquad = Math.Round(mitigationSamples.Sum(sample => sample.Mitigation?.TotalDamageToSquad ?? 0), 0);
        double totalHealthDamageToSquad = Math.Round(mitigationSamples.Sum(sample => sample.Mitigation?.HealthDamageToSquad ?? 0), 0);
        double totalBarrierAbsorbed = Math.Round(mitigationSamples.Sum(sample => sample.Mitigation?.TotalBarrierAbsorbed ?? sample.DefenseSaves?.TotalBarrierAbsorbed ?? 0), 0);
        double totalPetMinionAbsorption = Math.Round(mitigationSamples.Sum(sample => sample.Mitigation?.TotalPetMinionAbsorption ?? 0), 0);
        double totalEstimatedDamageReduction = Math.Round(mitigationSamples.Sum(sample => sample.Mitigation?.TotalEstimatedDamageReduction ?? sample.DefenseSaves?.TotalEstimatedDamageReduction ?? 0), 0);
        double totalEstimatedNegatedDamage = Math.Round(mitigationSamples.Sum(sample => sample.Mitigation?.TotalEstimatedNegatedDamage ?? 0), 0);
        double totalIncomingDamage = Math.Round(mitigationSamples.Sum(sample => sample.Mitigation?.TotalIncomingDamage ?? sample.DefenseSaves?.TotalIncomingDamage ?? 0), 0);
        double totalIncomingHealing = Math.Round(mitigationSamples.Sum(sample => sample.Mitigation?.TotalIncomingHealing ?? sample.DefenseSaves?.TotalIncomingHealing ?? 0), 0);
        int weightedHealthSampleCount = mitigationSamples.Sum(sample => Math.Max(0, sample.Mitigation?.SavedCases ?? sample.DefenseSaves?.SavedCases ?? 0));
        double? averageLowestHealthPercent = weightedHealthSampleCount > 0
            ? Math.Round(
                mitigationSamples.Sum(sample =>
                    (sample.Mitigation?.AverageLowestHealthPercent ?? sample.DefenseSaves?.AverageLowestHealthPercent ?? 0)
                    * Math.Max(0, sample.Mitigation?.SavedCases ?? sample.DefenseSaves?.SavedCases ?? 0))
                / weightedHealthSampleCount,
                1)
            : null;
        double? lowestLowestHealthPercent = mitigationSamples
            .Select(sample =>
            {
                int savedCases = Math.Max(0, sample.Mitigation?.SavedCases ?? sample.DefenseSaves?.SavedCases ?? 0);
                return savedCases > 0
                    ? (double?)(sample.Mitigation?.LowestLowestHealthPercent ?? sample.DefenseSaves?.LowestLowestHealthPercent ?? 0)
                    : null;
            })
            .Where(value => value is not null)
            .DefaultIfEmpty(null)
            .Min();
        bool hasBarrierCoverageWarnings = mitigationSamples.Any(sample => sample.Mitigation?.BarrierCoverageMayBeIncomplete ?? false);

        var negatedHitSummaries = mitigationSamples
            .SelectMany(sample => sample.Mitigation?.NegatedHitSummaries ?? Array.Empty<FightNegatedHitSummaryIndexDto>())
            .GroupBy(summary => summary.Key ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(group => new FightAnalysisNegatedHitSummaryDto(
                Key: group.First().Key,
                Label: string.IsNullOrWhiteSpace(group.First().Label) ? "Unknown" : group.First().Label,
                NegatedHitCount: group.Sum(summary => Math.Max(0, summary.NegatedHitCount)),
                EstimatedPreventedDamage: Math.Round(group.Sum(summary => Math.Max(0, summary.EstimatedPreventedDamage)), 0),
                FallbackEstimateCount: group.Sum(summary => Math.Max(0, summary.FallbackEstimateCount)),
                ContributingEffects: group
                    .SelectMany(summary => summary.ContributingEffects ?? Array.Empty<FightEffectCountSummaryIndexDto>())
                    .GroupBy(effect => effect.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Select(effectGroup => new FightAnalysisEffectCountDto(
                        Name: effectGroup.First().Name,
                        Count: effectGroup.Sum(effect => Math.Max(0, effect.Count))))
                    .OrderByDescending(effect => effect.Count)
                    .ThenBy(effect => effect.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .OrderByDescending(summary => summary.EstimatedPreventedDamage)
            .ThenByDescending(summary => summary.NegatedHitCount)
            .ThenBy(summary => summary.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new FightAnalysisMitigationSummaryDto(
            AvailableFightCount: mitigationSamples.Length,
            HasBarrierCoverageWarnings: hasBarrierCoverageWarnings,
            TotalSaves: totalSaves,
            TotalBarrierSaves: totalBarrierSaves,
            TotalDamageReductionSaves: totalDamageReductionSaves,
            TotalNegatedDamageSaves: totalNegatedDamageSaves,
            TotalBothSaves: totalBothSaves,
            TotalMultiSourceSaves: totalMultiSourceSaves,
            TotalDamageToSquad: totalDamageToSquad,
            TotalHealthDamageToSquad: totalHealthDamageToSquad,
            TotalBarrierAbsorbed: totalBarrierAbsorbed,
            TotalPetMinionAbsorption: totalPetMinionAbsorption,
            TotalEstimatedDamageReduction: totalEstimatedDamageReduction,
            TotalEstimatedNegatedDamage: totalEstimatedNegatedDamage,
            TotalIncomingDamage: totalIncomingDamage,
            TotalIncomingHealing: totalIncomingHealing,
            AverageLowestHealthPercent: averageLowestHealthPercent,
            LowestLowestHealthPercent: lowestLowestHealthPercent,
            NegatedHitSummaries: negatedHitSummaries);
    }

    private static FightAnalysisObliterateSummaryDto? BuildObliterateSummary(IReadOnlyList<FightArtifactSummaryDto> fights)
    {
        var obliterateSamples = fights
            .Select(fight => fight.FightIndex?.Obliterate)
            .Where(summary => summary is not null)
            .Cast<FightObliterateIndexDto>()
            .ToArray();

        if (obliterateSamples.Length == 0)
        {
            return null;
        }

        int fightsWithObliterateCount = obliterateSamples.Count(summary => summary.HitCount > 0);
        int totalHitCount = obliterateSamples.Sum(summary => Math.Max(0, summary.HitCount));
        int totalBarrierRemovedHitCount = obliterateSamples.Sum(summary => Math.Max(0, summary.BarrierRemovedHitCount));
        double fightsWithObliteratePercent = Math.Round(fightsWithObliterateCount * 100.0 / obliterateSamples.Length, 1);
        double? barrierRemovedRatePercent = totalHitCount > 0
            ? Math.Round(totalBarrierRemovedHitCount * 100.0 / totalHitCount, 1)
            : null;

        return new FightAnalysisObliterateSummaryDto(
            AvailableFightCount: obliterateSamples.Length,
            FightsWithObliterateCount: fightsWithObliterateCount,
            FightsWithObliteratePercent: fightsWithObliteratePercent,
            TotalHitCount: totalHitCount,
            TotalBarrierRemovedHitCount: totalBarrierRemovedHitCount,
            BarrierRemovedRatePercent: barrierRemovedRatePercent);
    }

    private static IReadOnlyList<FightAnalysisTrendPointDto> BuildTrends(
        IReadOnlyList<FightArtifactSummaryDto> fights,
        IReadOnlyDictionary<string, FightExpectedScoreResult> expectedScoreLookup)
    {
        return fights
            .Select(fight =>
            {
                var pillars = BuildPillarMap(fight.FightIndex?.Execution?.Pillars);
                var fightTimestamp = GetFightDateTimeOffset(fight);
                expectedScoreLookup.TryGetValue(fight.FightId, out var expectedScore);
                return new FightAnalysisTrendPointDto(
                    FightId: fight.FightId,
                    FightName: fight.FightIndex?.FightName ?? fight.SourceFileName ?? fight.FightId,
                    FightDateLabel: FormatFightDateLabel(fight),
                    FightDateUtc: fightTimestamp?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                    Commander: fight.FightIndex?.CommanderDisplayNames?.FirstOrDefault(),
                    OutcomeLabel: fight.FightIndex?.Outcome.DisplayLabel ?? "Unavailable",
                    PatchEraId: fight.PatchEra?.Id,
                    PatchEraLabel: fight.PatchEra?.Label,
                    AttributeKeys: (fight.Attributes ?? Array.Empty<FightAttributeDto>()).Select(attribute => attribute.Key).ToArray(),
                    AttributeLabels: (fight.Attributes ?? Array.Empty<FightAttributeDto>()).Select(attribute => attribute.Label).ToArray(),
                    OverallScore: fight.FightIndex?.Execution?.OverallScore,
                    ExpectedScore: expectedScore is null ? null : Math.Round(expectedScore.ExpectedScore, 1),
                    ContextDelta: expectedScore is null ? null : Math.Round(expectedScore.ContextDelta, 1),
                    ExpectedScoreSampleCount: expectedScore?.SampleCount ?? 0,
                    ExpectedScoreConfidenceLabel: expectedScore?.ConfidenceLabel,
                    ExpectedScoreDetail: expectedScore?.Detail,
                    CohesionScore: pillars.TryGetValue("cohesion-positioning", out int cohesion) ? cohesion : null,
                    PressureScore: pillars.TryGetValue("pressure-burst", out int pressure) ? pressure : null,
                    DownstateScore: pillars.TryGetValue("downstate-control", out int downstate) ? downstate : null,
                    ResilienceScore: pillars.TryGetValue("resilience-stabilization", out int resilience) ? resilience : null);
            })
            .ToArray();
    }

    private static IReadOnlyDictionary<string, FightExpectedScoreResult> BuildExpectedScoreLookup(
        IReadOnlyList<FightArtifactSummaryDto> fights)
    {
        var samples = fights
            .Select(BuildExpectedScoreSample)
            .Where(sample => sample is not null)
            .Cast<FightExpectedScoreSample>()
            .ToArray();
        if (samples.Length <= MinimumExpectedScoreSampleCount)
        {
            return new Dictionary<string, FightExpectedScoreResult>(StringComparer.OrdinalIgnoreCase);
        }

        var results = new Dictionary<string, FightExpectedScoreResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var sample in samples)
        {
            var expectedScore = BuildExpectedScoreForSample(sample, samples);
            if (expectedScore is not null)
            {
                results[sample.FightId] = expectedScore;
            }
        }

        return results;
    }

    private static FightExpectedScoreSample? BuildExpectedScoreSample(FightArtifactSummaryDto fight)
    {
        var fightIndex = fight.FightIndex;
        if (fightIndex?.Execution?.ScoreAvailable != true || fightIndex.Execution.OverallScore is not int overallScore)
        {
            return null;
        }

        double squadSize = GetSquadSize(fight);
        double enemySize = GetEnemySize(fight);
        double sizeRatio = squadSize > 0.0 ? enemySize / squadSize : 0.0;

        return new FightExpectedScoreSample(
            FightId: fight.FightId,
            OverallScore: overallScore,
            CommanderKey: GetExpectedScoreCommanderKey(fight),
            PatchEraId: fight.PatchEra?.Id ?? string.Empty,
            SquadSize: squadSize,
            EnemySize: enemySize,
            SizeRatio: sizeRatio,
            DurationBucket: GetExpectedScoreDurationBucket(GetFightDurationSeconds(fight)),
            DataProfileKey: GetExpectedScoreDataProfileKey(fight),
            ContextAttributeKeys: BuildExpectedScoreContextAttributeSet(fight),
            SquadClassProfile: BuildClassProfile(fightIndex.SquadSide?.Classes));
    }

    private static FightExpectedScoreResult? BuildExpectedScoreForSample(
        FightExpectedScoreSample target,
        IReadOnlyList<FightExpectedScoreSample> samples)
    {
        var comparisonPool = samples
            .Where(sample => !string.Equals(sample.FightId, target.FightId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (comparisonPool.Length < MinimumExpectedScoreSampleCount)
        {
            return null;
        }

        var candidateTiers = new (string Detail, FightExpectedScoreSample[] Candidates)[]
        {
            (
                "same commander, patch, and context",
                comparisonPool
                    .Where(candidate => HasSameCommander(target, candidate)
                        && HasSamePatch(target, candidate)
                        && MatchesStrictExpectedContext(target, candidate))
                    .ToArray()
            ),
            (
                "same commander and context",
                comparisonPool
                    .Where(candidate => HasSameCommander(target, candidate)
                        && MatchesStrictExpectedContext(target, candidate))
                    .ToArray()
            ),
            (
                "same patch and context",
                comparisonPool
                    .Where(candidate => HasSamePatch(target, candidate)
                        && MatchesStrictExpectedContext(target, candidate))
                    .ToArray()
            ),
            (
                "similar fight context",
                comparisonPool
                    .Where(candidate => MatchesStrictExpectedContext(target, candidate))
                    .ToArray()
            ),
            (
                "same patch with broader context",
                comparisonPool
                    .Where(candidate => HasSamePatch(target, candidate)
                        && MatchesBroadExpectedContext(target, candidate))
                    .ToArray()
            ),
            (
                "broader fight context",
                comparisonPool
                    .Where(candidate => MatchesBroadExpectedContext(target, candidate))
                    .ToArray()
            ),
            (
                "overall scored catalog baseline",
                comparisonPool
            )
        };

        foreach (var tier in candidateTiers)
        {
            if (tier.Candidates.Length >= MinimumExpectedScoreSampleCount)
            {
                return BuildExpectedScoreResult(target, tier.Candidates, tier.Detail);
            }
        }

        return null;
    }

    private static FightExpectedScoreResult BuildExpectedScoreResult(
        FightExpectedScoreSample target,
        IReadOnlyList<FightExpectedScoreSample> candidates,
        string detail)
    {
        double expectedScore = Math.Round(CalculateMedian(candidates.Select(candidate => (double)candidate.OverallScore)), 1);
        double contextDelta = Math.Round(target.OverallScore - expectedScore, 1);
        string confidenceLabel = DeriveExpectedScoreConfidenceLabel(candidates.Count);
        string fightLabel = candidates.Count == 1 ? "fight" : "fights";

        return new FightExpectedScoreResult(
            ExpectedScore: expectedScore,
            ContextDelta: contextDelta,
            SampleCount: candidates.Count,
            ConfidenceLabel: confidenceLabel,
            Detail: $"Expected {expectedScore.ToString("0.0", CultureInfo.InvariantCulture)} from {candidates.Count} {fightLabel} using {detail}.");
    }

    private static string BuildContextDeltaOverviewDetail(
        int filteredFightCount,
        IReadOnlyList<FightExpectedScoreResult> expectedScores)
    {
        if (filteredFightCount == 0)
        {
            return "No fights matched the current filters.";
        }

        if (expectedScores.Count == 0)
        {
            return $"No expected-score baseline yet. Each fight needs at least {MinimumExpectedScoreSampleCount} scored comparison fights.";
        }

        int medianSampleCount = (int)Math.Round(CalculateMedian(expectedScores.Select(score => (double)score.SampleCount)));
        string confidenceLabel = DeriveExpectedScoreConfidenceLabel(medianSampleCount);
        string coverage = expectedScores.Count == filteredFightCount
            ? "All filtered fights"
            : $"{expectedScores.Count} of {filteredFightCount} filtered fights";

        return $"{coverage} have expected-score baselines. Typical baseline uses {medianSampleCount} comparison fights ({confidenceLabel.ToLowerInvariant()} confidence).";
    }

    private static bool HasSameCommander(FightExpectedScoreSample target, FightExpectedScoreSample candidate)
    {
        return !string.IsNullOrWhiteSpace(target.CommanderKey)
            && string.Equals(target.CommanderKey, candidate.CommanderKey, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSamePatch(FightExpectedScoreSample target, FightExpectedScoreSample candidate)
    {
        return !string.IsNullOrWhiteSpace(target.PatchEraId)
            && string.Equals(target.PatchEraId, candidate.PatchEraId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesStrictExpectedContext(FightExpectedScoreSample target, FightExpectedScoreSample candidate)
    {
        return Math.Abs(target.SizeRatio - candidate.SizeRatio) <= StrictExpectedScoreSizeRatioTolerance
            && target.DurationBucket == candidate.DurationBucket
            && string.Equals(target.DataProfileKey, candidate.DataProfileKey, StringComparison.OrdinalIgnoreCase)
            && target.ContextAttributeKeys.SetEquals(candidate.ContextAttributeKeys)
            && CalculateClassProfileDistance(target.SquadClassProfile, candidate.SquadClassProfile) <= StrictExpectedScoreClassMixTolerance;
    }

    private static bool MatchesBroadExpectedContext(FightExpectedScoreSample target, FightExpectedScoreSample candidate)
    {
        return Math.Abs(target.SizeRatio - candidate.SizeRatio) <= BroadExpectedScoreSizeRatioTolerance
            && Math.Abs(target.DurationBucket - candidate.DurationBucket) <= 1
            && HasSameContextFlag(target, candidate, "three-way")
            && CalculateClassProfileDistance(target.SquadClassProfile, candidate.SquadClassProfile) <= BroadExpectedScoreClassMixTolerance;
    }

    private static bool HasSameContextFlag(FightExpectedScoreSample target, FightExpectedScoreSample candidate, string attributeKey)
    {
        return target.ContextAttributeKeys.Contains(attributeKey) == candidate.ContextAttributeKeys.Contains(attributeKey);
    }

    private static string DeriveExpectedScoreConfidenceLabel(int sampleCount)
    {
        return sampleCount switch
        {
            >= GoodExpectedScoreSampleCount => "Good",
            >= MediumExpectedScoreSampleCount => "Medium",
            _ => "Low"
        };
    }

    private static double GetSquadSize(FightArtifactSummaryDto fight)
    {
        var fightIndex = fight.FightIndex;
        if (fightIndex is null)
        {
            return 0.0;
        }

        if (fightIndex.SquadPlayerCount > 0)
        {
            return fightIndex.SquadPlayerCount;
        }

        if (fightIndex.SquadSide?.PlayerCount is int sidePlayerCount && sidePlayerCount > 0)
        {
            return sidePlayerCount;
        }

        return fightIndex.PlayerCount > 0 ? fightIndex.PlayerCount : 0.0;
    }

    private static double GetEnemySize(FightArtifactSummaryDto fight)
    {
        var fightIndex = fight.FightIndex;
        if (fightIndex is null)
        {
            return 0.0;
        }

        if (fightIndex.EnemyPlayerCount > 0)
        {
            return fightIndex.EnemyPlayerCount;
        }

        if (fightIndex.EnemyTargetCount > 0)
        {
            return fightIndex.EnemyTargetCount;
        }

        if (fightIndex.EnemySide?.PlayerCount is int sidePlayerCount && sidePlayerCount > 0)
        {
            return sidePlayerCount;
        }

        return 0.0;
    }

    private static string GetExpectedScoreCommanderKey(FightArtifactSummaryDto fight)
    {
        return NormalizeLookupKey(fight.FightIndex?.CommanderDisplayNames?.FirstOrDefault());
    }

    private static int GetExpectedScoreDurationBucket(double durationSeconds)
    {
        return durationSeconds switch
        {
            <= 0.0 => -1,
            <= 90.0 => 0,
            <= 180.0 => 1,
            <= 300.0 => 2,
            _ => 3
        };
    }

    private static string GetExpectedScoreDataProfileKey(FightArtifactSummaryDto fight)
    {
        var mitigation = fight.FightIndex?.MitigationSummary;
        if (mitigation is null)
        {
            return "mitigation:none";
        }

        return mitigation.HasBarrierData
            ? "mitigation:barrier"
            : "mitigation:no-barrier";
    }

    private static HashSet<string> BuildExpectedScoreContextAttributeSet(FightArtifactSummaryDto fight)
    {
        var fightAttributeKeys = (fight.Attributes ?? Array.Empty<FightAttributeDto>())
            .Select(attribute => attribute.Key)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var contextAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var attributeKey in ExpectedScoreContextAttributeKeys)
        {
            if (fightAttributeKeys.Contains(attributeKey))
            {
                contextAttributes.Add(attributeKey);
            }
        }

        return contextAttributes;
    }

    private static IReadOnlyDictionary<string, double> BuildClassProfile(IReadOnlyList<FightSideClassIndexDto>? classes)
    {
        var groupedClasses = (classes ?? Array.Empty<FightSideClassIndexDto>())
            .Where(classEntry => classEntry.Count > 0 && !string.IsNullOrWhiteSpace(classEntry.ClassLabel))
            .GroupBy(classEntry => NormalizeLookupKey(classEntry.ClassLabel), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group => new
            {
                ClassKey = group.Key,
                Count = group.Sum(classEntry => classEntry.Count)
            })
            .ToArray();
        int totalCount = groupedClasses.Sum(classEntry => classEntry.Count);
        if (totalCount <= 0)
        {
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        return groupedClasses.ToDictionary(
            classEntry => classEntry.ClassKey,
            classEntry => classEntry.Count / (double)totalCount,
            StringComparer.OrdinalIgnoreCase);
    }

    private static double CalculateClassProfileDistance(
        IReadOnlyDictionary<string, double> left,
        IReadOnlyDictionary<string, double> right)
    {
        if (left.Count == 0 || right.Count == 0)
        {
            return 0.0;
        }

        var classKeys = left.Keys
            .Concat(right.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return classKeys.Sum(classKey =>
            Math.Abs(GetClassProfileShare(left, classKey) - GetClassProfileShare(right, classKey))) / 2.0;
    }

    private static double GetClassProfileShare(IReadOnlyDictionary<string, double> profile, string classKey)
    {
        return profile.TryGetValue(classKey, out double share) ? share : 0.0;
    }

    private static IReadOnlyList<FightAnalysisPlayerRowDto> BuildTopPlayers(
        IReadOnlyList<FightArtifactSummaryDto> fights,
        IReadOnlyDictionary<string, int> totalPlayerFightCounts,
        IReadOnlyDictionary<string, int> totalCharacterFightCounts,
        IReadOnlyDictionary<string, int> totalCharacterLaneSampleCounts)
    {
        return fights
            .SelectMany(fight => (fight.FightIndex?.Players ?? Array.Empty<FightPlayerIndexDto>())
                .Where(player => !string.IsNullOrWhiteSpace(player.Account))
                .Select(player => new PlayerFightSample(fight, player)))
            .GroupBy(entry => entry.Player.Account!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var orderedPlayers = group.ToArray();
                var mergedFightEntries = MergePlayerFightSamplesByFight(orderedPlayers);
                var sampleCount = mergedFightEntries.Count;
                int totalFightCountAll = totalPlayerFightCounts.TryGetValue(group.Key, out int totalFightCount)
                    ? totalFightCount
                    : sampleCount;
                var characters = BuildPlayerCharacterSummaries(group.Key, orderedPlayers, totalCharacterFightCounts, totalCharacterLaneSampleCounts);
                var primaryCharacter = characters
                    .OrderByDescending(character => character.FightCount)
                    .ThenByDescending(character => character.ImpactScore)
                    .ThenBy(character => character.CharacterName, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                var primaryClassLabel = mergedFightEntries
                    .Select(entry => entry.ClassLabel)
                    .Where(label => !string.IsNullOrWhiteSpace(label))
                    .GroupBy(label => label!, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(labelGroup => labelGroup.Count())
                    .ThenBy(labelGroup => labelGroup.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(labelGroup => labelGroup.Key)
                    .FirstOrDefault();
                var primaryLane = mergedFightEntries
                    .Select(entry => GetPrimaryLane(entry.Lanes))
                    .Where(lane => lane is not null)
                    .GroupBy(lane => lane!.Label, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(lane => lane.Count())
                    .ThenByDescending(lane => lane.Average(entry => entry!.StrengthPercent))
                    .FirstOrDefault();
                var cleanupAdjustedLaneScores = mergedFightEntries
                    .Select(entry => new
                    {
                        Fight = entry.Fight,
                        Score = ComputePlayerLaneScores(entry.Lanes)
                    })
                    .Where(score => score.Score.Primary > 0.0 || score.Score.Weighted > 0.0)
                    .ToArray();
                var classesPlayed = mergedFightEntries
                    .Select(entry => entry.ClassLabel)
                    .Where(label => !string.IsNullOrWhiteSpace(label))
                    .Select(label => label!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var winCount = mergedFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "squad", StringComparison.OrdinalIgnoreCase));
                var lossCount = mergedFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "enemy", StringComparison.OrdinalIgnoreCase));
                var drawCount = mergedFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "draw", StringComparison.OrdinalIgnoreCase));
                var contributionSummary = PickMostCommonNonEmpty(mergedFightEntries
                    .Select(entry => entry.KeyContributionSummary ?? entry.ContributionProfile ?? entry.FitSummary))
                    ?? primaryCharacter?.ContributionSummary;
                var averageDamage = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => (double)entry.Damage, entry => entry.Fight), 0);
                var averageDowns = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Downs, entry => entry.Fight), 1);
                var averageKills = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Kills, entry => entry.Fight), 1);
                var averageStrips = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Strips, entry => entry.Fight), 1);
                var averageCleanses = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.OutgoingCleanses, entry => entry.Fight), 1);
                var averageHealing = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => (double)entry.Healing, entry => entry.Fight), 0);
                var averageBarrier = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => (double)entry.Barrier, entry => entry.Fight), 0);
                var averageResurrects = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Resurrects, entry => entry.Fight), 1);
                var averageDeaths = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Deaths, entry => entry.Fight), 1);
                var averageRecoveries = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Recoveries, entry => entry.Fight), 1);
                double? averageInPositionRate = RoundNullable(CleanupAdjustedAverageOrNull(mergedFightEntries, entry => entry.InPositionRate, entry => entry.Fight), 1);
                var winRatePercent = sampleCount == 0 ? 0.0 : Math.Round(winCount * 100.0 / sampleCount, 1);
                var aggregateImpactScore = ComputeImpactScore(
                    averageWeightedLaneScore: cleanupAdjustedLaneScores.Length == 0
                        ? 0.0
                        : CleanupAdjustedAverage(cleanupAdjustedLaneScores, score => score.Score.Weighted, score => score.Fight),
                    winRatePercent: winRatePercent,
                    averageDamage: averageDamage,
                    averageDowns: averageDowns,
                    averageKills: averageKills,
                    averageStrips: averageStrips,
                    averageCleanses: averageCleanses,
                    averageHealing: averageHealing,
                    averageBarrier: averageBarrier,
                    averageResurrects: averageResurrects,
                    averageDeaths: averageDeaths,
                    averageRecoveries: averageRecoveries,
                    averageInPositionRate: averageInPositionRate);
                var impactScore = ComputeFightWeightedCharacterImpactScore(characters, aggregateImpactScore);

                return new FightAnalysisPlayerRowDto(
                    Account: group.Key,
                    DisplayName: primaryCharacter?.CharacterName ?? group.Key,
                    FightCount: sampleCount,
                    TotalFightCountAll: totalFightCountAll,
                    WinCount: winCount,
                    LossCount: lossCount,
                    DrawCount: drawCount,
                    WinRatePercent: winRatePercent,
                    ClassesPlayed: classesPlayed,
                    PrimaryClassLabel: primaryClassLabel,
                    PrimaryLaneLabel: primaryLane?.Key ?? primaryCharacter?.PrimaryLaneLabel,
                    ImpactScore: impactScore,
                    AveragePrimaryLaneScore: cleanupAdjustedLaneScores.Length == 0
                        ? 0.0
                        : Math.Round(CleanupAdjustedAverage(cleanupAdjustedLaneScores, score => score.Score.Primary, score => score.Fight), 1),
                    AverageWeightedLaneScore: cleanupAdjustedLaneScores.Length == 0
                        ? 0.0
                        : Math.Round(CleanupAdjustedAverage(cleanupAdjustedLaneScores, score => score.Score.Weighted, score => score.Fight), 1),
                    AverageDamagePerFight: averageDamage,
                    AverageDownsPerFight: averageDowns,
                    AverageKillsPerFight: averageKills,
                    AverageStripsPerFight: averageStrips,
                    AverageOutgoingCleansesPerFight: averageCleanses,
                    AverageHealingPerFight: averageHealing,
                    AverageBarrierPerFight: averageBarrier,
                    AverageResurrectsPerFight: averageResurrects,
                    AverageDeathsPerFight: averageDeaths,
                    AverageRecoveriesPerFight: averageRecoveries,
                    AverageInPositionRate: averageInPositionRate,
                    ContributionSummary: string.IsNullOrWhiteSpace(contributionSummary) ? null : contributionSummary,
                    CharacterNames: characters
                        .Select(character => character.CharacterName)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    Characters: characters);
            })
            .OrderByDescending(player => player.ImpactScore)
            .ThenByDescending(player => player.AverageWeightedLaneScore)
            .ThenByDescending(player => player.FightCount)
            .ThenBy(player => player.Account, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<FightAnalysisClassRowDto> BuildTopClasses(
        IReadOnlyList<FightArtifactSummaryDto> fights,
        IReadOnlyDictionary<string, int> totalClassPlayerFightCounts,
        IReadOnlyList<PatchImpactDto> patchImpacts)
    {
        return fights
            .SelectMany(fight => (fight.FightIndex?.Players ?? Array.Empty<FightPlayerIndexDto>())
                .Where(player => !string.IsNullOrWhiteSpace(BuildClassLabel(player)))
                .Select(player => new PlayerFightSample(fight, player)))
            .GroupBy(entry => BuildClassLabel(entry.Player)!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var entries = group.ToArray();
                var mergedFightEntries = entries
                    .GroupBy(entry => entry.Player.Account ?? entry.Player.Character ?? $"Actor {entry.Player.ActorId}", StringComparer.OrdinalIgnoreCase)
                    .SelectMany(playerGroup => MergePlayerFightSamplesByFight(playerGroup.ToArray()))
                    .ToArray();
                var sampleCount = mergedFightEntries.Length;
                var laneContributions = BuildAggregateLaneContributions(mergedFightEntries, sampleCount, 6);
                var laneScores = mergedFightEntries
                    .Select(entry => new
                    {
                        Fight = entry.Fight,
                        Score = ComputePlayerLaneScores(entry.Lanes)
                    })
                    .Where(score => score.Score.Primary > 0.0 || score.Score.Weighted > 0.0)
                    .ToArray();
                var winCount = mergedFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "squad", StringComparison.OrdinalIgnoreCase));
                var lossCount = mergedFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "enemy", StringComparison.OrdinalIgnoreCase));
                var drawCount = mergedFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "draw", StringComparison.OrdinalIgnoreCase));
                var winRatePercent = sampleCount == 0 ? 0.0 : Math.Round(winCount * 100.0 / sampleCount, 1);
                var averageDamage = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => (double)entry.Damage, entry => entry.Fight), 0);
                var averageDowns = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Downs, entry => entry.Fight), 1);
                var averageKills = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Kills, entry => entry.Fight), 1);
                var averageStrips = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Strips, entry => entry.Fight), 1);
                var averageCleanses = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.OutgoingCleanses, entry => entry.Fight), 1);
                var averageHealing = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => (double)entry.Healing, entry => entry.Fight), 0);
                var averageBarrier = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => (double)entry.Barrier, entry => entry.Fight), 0);
                var averageResurrects = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Resurrects, entry => entry.Fight), 1);
                var averageDeaths = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Deaths, entry => entry.Fight), 1);
                var averageRecoveries = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Recoveries, entry => entry.Fight), 1);
                double? averageInPositionRate = CleanupAdjustedAverageOrNull(mergedFightEntries, entry => entry.InPositionRate, entry => entry.Fight);
                var classPlayers = BuildClassPlayerSummaries(group.Key, entries, totalClassPlayerFightCounts);
                var topPlayerDisplayName = classPlayers
                    .OrderByDescending(player => player.ImpactScore)
                    .ThenByDescending(player => player.FightCount)
                    .ThenBy(player => player.Account, StringComparer.OrdinalIgnoreCase)
                    .Select(player => player.Account)
                    .FirstOrDefault();
                var contributionScore = ComputeImpactScore(
                    averageWeightedLaneScore: laneScores.Length == 0
                        ? 0.0
                        : CleanupAdjustedAverage(laneScores, score => score.Score.Weighted, score => score.Fight),
                    winRatePercent: winRatePercent,
                    averageDamage: averageDamage,
                    averageDowns: averageDowns,
                    averageKills: averageKills,
                    averageStrips: averageStrips,
                    averageCleanses: averageCleanses,
                    averageHealing: averageHealing,
                    averageBarrier: averageBarrier,
                    averageResurrects: averageResurrects,
                    averageDeaths: averageDeaths,
                    averageRecoveries: averageRecoveries,
                    averageInPositionRate: averageInPositionRate);

                return new FightAnalysisClassRowDto(
                    ClassLabel: group.Key,
                    SampleCount: sampleCount,
                    DistinctAccounts: classPlayers.Count,
                    WinCount: winCount,
                    LossCount: lossCount,
                    DrawCount: drawCount,
                    WinRatePercent: winRatePercent,
                    ContributionScore: contributionScore,
                    TopPlayerDisplayName: topPlayerDisplayName,
                    AveragePrimaryLaneScore: laneScores.Length == 0
                        ? 0.0
                        : Math.Round(CleanupAdjustedAverage(laneScores, score => score.Score.Primary, score => score.Fight), 1),
                    AverageWeightedLaneScore: laneScores.Length == 0
                        ? 0.0
                        : Math.Round(CleanupAdjustedAverage(laneScores, score => score.Score.Weighted, score => score.Fight), 1),
                    LaneContributions: laneContributions,
                    PatchImpacts: GetPatchImpactsForClass(group.Key, patchImpacts),
                    Players: classPlayers);
            })
            .Where(row => row.SampleCount >= MinimumClassSampleCount)
            .OrderByDescending(row => row.ContributionScore)
            .ThenByDescending(row => row.AverageWeightedLaneScore)
            .ThenByDescending(row => row.SampleCount)
            .ThenBy(row => row.ClassLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<FightAnalysisLaneRowDto> BuildTopLanes(IReadOnlyList<FightArtifactSummaryDto> fights)
    {
        var totalFightCount = fights.Count;

        return fights
            .SelectMany(fight => (fight.FightIndex?.Players ?? Array.Empty<FightPlayerIndexDto>())
                .Where(player => !string.IsNullOrWhiteSpace(player.Account))
                .SelectMany(player => (player.Lanes ?? Array.Empty<FightPlayerLaneIndexDto>()).Select(lane => new
                {
                    Fight = fight,
                    fight.FightId,
                    Account = player.Account!,
                    PlayerDisplayName = NormalizeAnalysisCharacterName(player.Character) ?? player.Account ?? $"Actor {player.ActorId}",
                    ClassLabel = BuildClassLabel(player),
                    Lane = lane
                })))
            .GroupBy(entry => entry.Lane.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var entries = group.ToArray();
                var label = entries.Select(entry => entry.Lane.Label).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? group.Key;
                var distinctFightCount = entries.Select(entry => entry.FightId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                var topClassLabel = entries
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.ClassLabel))
                    .GroupBy(entry => entry.ClassLabel!, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(classGroup => CleanupAdjustedAverage(classGroup.ToArray(), entry => entry.Lane.StrengthPercent, entry => entry.Fight))
                    .ThenByDescending(classGroup => classGroup.Count())
                    .Select(classGroup => classGroup.Key)
                    .FirstOrDefault();
                var topPlayerDisplayName = entries
                    .GroupBy(entry => entry.Account, StringComparer.OrdinalIgnoreCase)
                    .Select(playerGroup => new
                    {
                        Display = playerGroup.Select(entry => entry.PlayerDisplayName).FirstOrDefault(),
                        AverageStrength = CleanupAdjustedAverage(playerGroup.ToArray(), entry => entry.Lane.StrengthPercent, entry => entry.Fight),
                        Samples = playerGroup.Count()
                    })
                    .OrderByDescending(entry => entry.AverageStrength)
                    .ThenByDescending(entry => entry.Samples)
                    .Select(entry => entry.Display)
                    .FirstOrDefault();
                var evidenceLine = entries
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Lane.EvidenceLine))
                    .OrderByDescending(entry => entry.Lane.StrengthPercent)
                    .ThenByDescending(entry => entry.Lane.SharePercent)
                    .Select(entry => entry.Lane.EvidenceLine)
                    .FirstOrDefault();
                return new FightAnalysisLaneRowDto(
                    LaneKey: group.Key,
                    LaneLabel: label,
                    Samples: entries.Length,
                    DistinctAccounts: entries.Select(entry => entry.Account).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    DistinctClasses: entries.Select(entry => entry.ClassLabel).Where(label => !string.IsNullOrWhiteSpace(label)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    AverageStrengthPercent: Math.Round(CleanupAdjustedAverage(entries, entry => entry.Lane.StrengthPercent, entry => entry.Fight), 1),
                    AverageSharePercent: Math.Round(CleanupAdjustedAverage(entries, entry => entry.Lane.SharePercent, entry => entry.Fight), 1),
                    AppearanceRatePercent: totalFightCount == 0 ? 0.0 : Math.Round(distinctFightCount * 100.0 / totalFightCount, 1),
                    TopClassLabel: topClassLabel,
                    TopPlayerDisplayName: topPlayerDisplayName,
                    EvidenceLine: evidenceLine);
            })
            .OrderByDescending(row => row.AverageStrengthPercent)
            .ThenByDescending(row => row.Samples)
            .Take(20)
            .ToArray();
    }

    private static IReadOnlyList<FightAnalysisBoonRowDto> BuildTopBoons(IReadOnlyList<FightArtifactSummaryDto> fights)
    {
        var providerLookup = BuildBoonProviderLookup(fights);

        return fights
            .SelectMany(fight => (fight.FightIndex?.ThreatBoons ?? Array.Empty<FightThreatBoonIndexDto>())
                .Select(boon => new
                {
                    fight.FightId,
                    Boon = boon
                }))
            .GroupBy(entry => entry.Boon.Id)
            .Select(group =>
            {
                var entries = group.ToArray();
                var sample = entries[0].Boon;
                providerLookup.TryGetValue(group.Key, out IReadOnlyList<FightAnalysisBoonClassProviderDto>? classProviders);
                classProviders ??= Array.Empty<FightAnalysisBoonClassProviderDto>();

                return new FightAnalysisBoonRowDto(
                    Id: group.Key,
                    Name: sample.Name,
                    TypeLabel: sample.StackBased ? "Stacking" : "Binary",
                    StackBased: sample.StackBased,
                    TracksOverapplication: sample.TracksOverapplication,
                    FightCount: entries.Select(entry => entry.FightId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    AverageCoverage: Math.Round(entries.Average(entry => entry.Boon.Coverage), 1),
                    AverageStacks: sample.StackBased ? Math.Round(entries.Average(entry => entry.Boon.AverageStacks), 1) : null,
                    AverageOverapplication: sample.TracksOverapplication ? Math.Round(entries.Average(entry => entry.Boon.Overapplication), 1) : null,
                    TopClassLabel: classProviders.FirstOrDefault()?.ClassLabel,
                    ClassProviders: classProviders);
            })
            .OrderByDescending(row => row.AverageCoverage)
            .ThenByDescending(row => row.FightCount)
            .Take(20)
            .ToArray();
    }

    private static IReadOnlyDictionary<long, IReadOnlyList<FightAnalysisBoonClassProviderDto>> BuildBoonProviderLookup(
        IReadOnlyList<FightArtifactSummaryDto> fights)
    {
        return BuildMergedBoonProviderSamples(fights)
            .SelectMany(sample => sample.ProvidedBoons.Select(boon => new
            {
                sample.Account,
                sample.ClassLabel,
                Boon = boon
            }))
            .GroupBy(entry => entry.Boon.Id)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<FightAnalysisBoonClassProviderDto>)group
                    .GroupBy(entry => entry.ClassLabel, StringComparer.OrdinalIgnoreCase)
                    .Select(classGroup =>
                    {
                        var entries = classGroup.ToArray();
                        var sample = entries[0].Boon;
                        int sampleCount = entries.Length;
                        int providerAppearanceCount = entries.Count(entry => entry.Boon.Generation > 0.0 || entry.Boon.GenerationPresence > 0.0);
                        double averageGeneration = sampleCount == 0 ? 0.0 : Math.Round(entries.Average(entry => entry.Boon.Generation), 1);
                        double? averageGenerationPresence = sample.StackBased
                            ? Math.Round(entries.Average(entry => entry.Boon.GenerationPresence), 1)
                            : null;
                        double averageOverstack = sampleCount == 0 ? 0.0 : Math.Round(entries.Average(entry => entry.Boon.Overstack), 1);

                        return new FightAnalysisBoonClassProviderDto(
                            ClassLabel: classGroup.Key,
                            SampleCount: sampleCount,
                            ProviderAppearanceCount: providerAppearanceCount,
                            DistinctAccounts: entries.Select(entry => entry.Account).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                            AverageGeneration: averageGeneration,
                            AverageGenerationPresence: averageGenerationPresence,
                            AverageOverstack: averageOverstack,
                            ProviderScore: averageGeneration);
                    })
                    .Where(row => row.ProviderAppearanceCount > 0 || row.AverageGeneration > 0.0 || row.AverageGenerationPresence.GetValueOrDefault() > 0.0)
                    .OrderByDescending(row => row.ProviderScore)
                    .ThenByDescending(row => row.AverageGenerationPresence ?? 0.0)
                    .ThenByDescending(row => row.ProviderAppearanceCount)
                    .ThenByDescending(row => row.SampleCount)
                    .ThenBy(row => row.ClassLabel, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                EqualityComparer<long>.Default);
    }

    private static IReadOnlyList<MergedPlayerFightProvidedBoonSample> BuildMergedBoonProviderSamples(
        IReadOnlyList<FightArtifactSummaryDto> fights)
    {
        return fights
            .SelectMany(fight => (fight.FightIndex?.Players ?? Array.Empty<FightPlayerIndexDto>())
                .Where(player => !string.IsNullOrWhiteSpace(player.Account))
                .Where(player => !string.IsNullOrWhiteSpace(BuildClassLabel(player)))
                .Where(player => (player.ProvidedBoons?.Count ?? 0) > 0)
                .Select(player => new PlayerFightSample(fight, player)))
            .GroupBy(
                entry => $"{entry.Fight.FightId}\u001F{BuildClassLabel(entry.Player)}\u001F{BuildPlayerIdentityKey(entry.Player)}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => MergePlayerFightProvidedBoonSample(group.ToArray()))
            .Where(sample => sample.ProvidedBoons.Count > 0)
            .ToArray();
    }

    private static IReadOnlyList<FightAnalysisPlayerCharacterDto> BuildPlayerCharacterSummaries(
        string account,
        IReadOnlyList<PlayerFightSample> entries,
        IReadOnlyDictionary<string, int> totalCharacterFightCounts,
        IReadOnlyDictionary<string, int> totalCharacterLaneSampleCounts)
    {
        return entries
            .Select(entry => new
            {
                Entry = entry,
                CharacterName = NormalizeAnalysisCharacterName(entry.Player.Character) ?? "(unknown character)",
                ClassLabel = BuildClassLabel(entry.Player) ?? "Unknown class"
            })
            .GroupBy(
                entry => $"{entry.CharacterName}\u001F{entry.ClassLabel}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var characterName = first.CharacterName;
                var cardClassLabel = first.ClassLabel;
                var characterEntries = group.Select(entry => entry.Entry).ToArray();
                var mergedFightEntries = MergePlayerFightSamplesByFight(characterEntries, characterName);
                var sampleCount = mergedFightEntries.Count;
                string characterAggregateKey = BuildCharacterAggregateKey(account, characterName, cardClassLabel);
                int totalFightCountAll = totalCharacterFightCounts.TryGetValue(characterAggregateKey, out int totalFightCount)
                    ? totalFightCount
                    : sampleCount;
                var classLabels = new[] { cardClassLabel };
                var aggregatedProvidedBoons = AggregateProvidedBoonsByFight(characterEntries, sampleCount);

                var laneContributions = mergedFightEntries
                    .SelectMany(entry => entry.Lanes.Select(lane => new
                    {
                        entry.Fight,
                        Lane = lane
                    }))
                    .GroupBy(entry => entry.Lane.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(laneGroup =>
                    {
                        var laneEntries = laneGroup.ToArray();
                        var sample = laneEntries[0].Lane;
                        double averageStrengthPercent = Math.Round(CleanupAdjustedAverage(laneEntries, entry => entry.Lane.StrengthPercent, entry => entry.Fight), 1);
                        double averageSharePercent = Math.Round(CleanupAdjustedAverage(laneEntries, entry => entry.Lane.SharePercent, entry => entry.Fight), 1);
                        double appearanceRatePercent = sampleCount == 0 ? 0.0 : Math.Round(laneEntries.Length * 100.0 / sampleCount, 1);
                        double overallStrengthPercent = sampleCount == 0 ? 0.0 : Math.Round(laneEntries.Sum(entry => entry.Lane.StrengthPercent * GetFightShapeImpactWeight(entry.Fight)) / sampleCount, 1);
                        double overallSharePercent = sampleCount == 0 ? 0.0 : Math.Round(laneEntries.Sum(entry => entry.Lane.SharePercent * GetFightShapeImpactWeight(entry.Fight)) / sampleCount, 1);
                        return new FightAnalysisCharacterLaneContributionDto(
                            LaneKey: laneGroup.Key,
                            LaneLabel: string.IsNullOrWhiteSpace(sample.Label) ? laneGroup.Key : sample.Label,
                            AverageStrengthPercent: averageStrengthPercent,
                            AverageSharePercent: averageSharePercent,
                            OverallStrengthPercent: overallStrengthPercent,
                            OverallSharePercent: overallSharePercent,
                            AppearanceRatePercent: appearanceRatePercent,
                            Samples: laneEntries.Length,
                            TotalSamplesAll: totalCharacterLaneSampleCounts.TryGetValue(
                                BuildCharacterLaneAggregateKey(account, characterName, cardClassLabel, laneGroup.Key),
                                out int totalLaneSamples)
                                ? totalLaneSamples
                                : laneEntries.Length,
                            RateBand: DeriveAggregateLaneBand(overallStrengthPercent),
                            EvidenceLine: laneEntries
                                .Where(entry => !string.IsNullOrWhiteSpace(entry.Lane.EvidenceLine))
                                .OrderByDescending(entry => entry.Lane.StrengthPercent)
                                .ThenByDescending(entry => entry.Lane.SharePercent)
                                .Select(entry => entry.Lane.EvidenceLine)
                                .FirstOrDefault(),
                            Metrics: AggregateLaneMetrics(laneEntries.Select(entry => entry.Lane).ToArray()));
                    })
                    .OrderByDescending(lane => lane.OverallStrengthPercent)
                    .ThenByDescending(lane => lane.AppearanceRatePercent)
                    .ThenByDescending(lane => lane.Samples)
                    .ThenBy(lane => lane.LaneLabel, StringComparer.OrdinalIgnoreCase)
                    .Take(6)
                    .ToArray();

                var laneScores = mergedFightEntries
                    .Select(entry => new
                    {
                        Fight = entry.Fight,
                        Score = ComputePlayerLaneScores(entry.Lanes)
                    })
                    .Where(score => score.Score.Primary > 0.0 || score.Score.Weighted > 0.0)
                    .ToArray();
                var winCount = mergedFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "squad", StringComparison.OrdinalIgnoreCase));
                var lossCount = mergedFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "enemy", StringComparison.OrdinalIgnoreCase));
                var drawCount = mergedFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "draw", StringComparison.OrdinalIgnoreCase));
                var winRatePercent = sampleCount == 0 ? 0.0 : Math.Round(winCount * 100.0 / sampleCount, 1);
                var averageDamage = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => (double)entry.Damage, entry => entry.Fight), 0);
                var averageDowns = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Downs, entry => entry.Fight), 1);
                var averageKills = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Kills, entry => entry.Fight), 1);
                var averageStrips = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Strips, entry => entry.Fight), 1);
                var averageCleanses = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.OutgoingCleanses, entry => entry.Fight), 1);
                var averageHealing = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => (double)entry.Healing, entry => entry.Fight), 0);
                var averageBarrier = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => (double)entry.Barrier, entry => entry.Fight), 0);
                var averageResurrects = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Resurrects, entry => entry.Fight), 1);
                var averageDeaths = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Deaths, entry => entry.Fight), 1);
                var averageRecoveries = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Recoveries, entry => entry.Fight), 1);
                double? averageInPositionRate = RoundNullable(CleanupAdjustedAverageOrNull(mergedFightEntries, entry => entry.InPositionRate, entry => entry.Fight), 1);
                double? averageTooFarRate = RoundNullable(CleanupAdjustedAverageOrNull(mergedFightEntries, entry => entry.TooFarRate, entry => entry.Fight), 1);
                double? averageOverextendedRate = RoundNullable(CleanupAdjustedAverageOrNull(mergedFightEntries, entry => entry.OverextendedRate, entry => entry.Fight), 1);
                double? averageLateralRiskRate = RoundNullable(CleanupAdjustedAverageOrNull(mergedFightEntries, entry => entry.LateralRiskRate, entry => entry.Fight), 1);
                var impactScore = ComputeImpactScore(
                    averageWeightedLaneScore: laneScores.Length == 0
                        ? 0.0
                        : CleanupAdjustedAverage(laneScores, score => score.Score.Weighted, score => score.Fight),
                    winRatePercent: winRatePercent,
                    averageDamage: averageDamage,
                    averageDowns: averageDowns,
                    averageKills: averageKills,
                    averageStrips: averageStrips,
                    averageCleanses: averageCleanses,
                    averageHealing: averageHealing,
                    averageBarrier: averageBarrier,
                    averageResurrects: averageResurrects,
                    averageDeaths: averageDeaths,
                    averageRecoveries: averageRecoveries,
                    averageInPositionRate: averageInPositionRate);
                var contributionSummary = PickMostCommonNonEmpty(mergedFightEntries
                    .Select(entry => entry.KeyContributionSummary ?? entry.ContributionProfile ?? entry.FitSummary));
                var confidenceLabel = PickMostCommonNonEmpty(mergedFightEntries.Select(entry => entry.EvaluationConfidenceLabel))
                    ?? DeriveCharacterConfidenceLabel(sampleCount);
                var confidenceDetail = PickMostCommonNonEmpty(mergedFightEntries.Select(entry => entry.EvaluationConfidenceDetail));
                var packageInputs = BuildCharacterPackageInputs(
                    laneContributions,
                    aggregatedProvidedBoons,
                    sampleCount,
                    averageHealing,
                    averageCleanses,
                    averageBarrier,
                    averageStrips);
                var evidenceLines = mergedFightEntries
                    .SelectMany(entry => entry.EvidenceSnapshot)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .GroupBy(line => line, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(lineGroup => lineGroup.Count())
                    .ThenBy(lineGroup => lineGroup.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(lineGroup => lineGroup.Key)
                    .Take(4)
                    .ToArray();

                return new FightAnalysisPlayerCharacterDto(
                    CharacterName: characterName,
                    ClassLabel: cardClassLabel,
                    ClassesPlayed: classLabels,
                    FightCount: sampleCount,
                    TotalFightCountAll: totalFightCountAll,
                    WinCount: winCount,
                    LossCount: lossCount,
                    DrawCount: drawCount,
                    WinRatePercent: winRatePercent,
                    ImpactScore: impactScore,
                    PrimaryLaneLabel: laneContributions.FirstOrDefault()?.LaneLabel,
                    AveragePrimaryLaneScore: laneScores.Length == 0
                        ? 0.0
                        : Math.Round(CleanupAdjustedAverage(laneScores, score => score.Score.Primary, score => score.Fight), 1),
                    AverageWeightedLaneScore: laneScores.Length == 0
                        ? 0.0
                        : Math.Round(CleanupAdjustedAverage(laneScores, score => score.Score.Weighted, score => score.Fight), 1),
                    ContributionSummary: contributionSummary,
                    ConfidenceLabel: confidenceLabel,
                    ConfidenceDetail: confidenceDetail,
                    AverageInPositionRate: averageInPositionRate,
                    AverageTooFarRate: averageTooFarRate,
                    AverageOverextendedRate: averageOverextendedRate,
                    AverageLateralRiskRate: averageLateralRiskRate,
                    AverageDeathsPerFight: averageDeaths,
                    AverageRecoveriesPerFight: averageRecoveries,
                    AverageActivePresencePercent: Math.Round(ComputeAveragePresencePercent(mergedFightEntries, entry => entry.ActiveSeconds), 1),
                    AverageEngagedPresencePercent: Math.Round(ComputeAveragePresencePercent(mergedFightEntries, entry => entry.CombatSeconds), 1),
                    PackageInputs: packageInputs,
                    EvidenceLines: evidenceLines,
                    LaneContributions: laneContributions);
            })
            .OrderByDescending(character => character.FightCount)
            .ThenByDescending(character => character.ImpactScore)
            .ThenBy(character => character.ClassLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(character => character.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<FightAnalysisClassPlayerRowDto> BuildClassPlayerSummaries(
        string classLabel,
        IReadOnlyList<PlayerFightSample> entries,
        IReadOnlyDictionary<string, int> totalClassPlayerFightCounts)
    {
        return entries
            .GroupBy(entry => BuildPlayerIdentityKey(entry.Player), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var playerEntries = group.ToArray();
                var mergedFightEntries = MergePlayerFightSamplesByFight(playerEntries);
                var sampleCount = mergedFightEntries.Count;
                int totalFightCountAll = totalClassPlayerFightCounts.TryGetValue(
                    BuildClassPlayerAggregateKey(classLabel, group.Key),
                    out int totalFightCount)
                    ? totalFightCount
                    : sampleCount;
                var laneScores = mergedFightEntries
                    .Select(entry => new
                    {
                        Fight = entry.Fight,
                        Score = ComputePlayerLaneScores(entry.Lanes)
                    })
                    .Where(score => score.Score.Primary > 0.0 || score.Score.Weighted > 0.0)
                    .ToArray();
                var winCount = mergedFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "squad", StringComparison.OrdinalIgnoreCase));
                var lossCount = mergedFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "enemy", StringComparison.OrdinalIgnoreCase));
                var drawCount = mergedFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "draw", StringComparison.OrdinalIgnoreCase));
                var winRatePercent = sampleCount == 0 ? 0.0 : Math.Round(winCount * 100.0 / sampleCount, 1);
                var averageDamage = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => (double)entry.Damage, entry => entry.Fight), 0);
                var averageDowns = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Downs, entry => entry.Fight), 1);
                var averageKills = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Kills, entry => entry.Fight), 1);
                var averageStrips = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Strips, entry => entry.Fight), 1);
                var averageCleanses = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.OutgoingCleanses, entry => entry.Fight), 1);
                var averageHealing = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => (double)entry.Healing, entry => entry.Fight), 0);
                var averageBarrier = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => (double)entry.Barrier, entry => entry.Fight), 0);
                var averageResurrects = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Resurrects, entry => entry.Fight), 1);
                var averageDeaths = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Deaths, entry => entry.Fight), 1);
                var averageRecoveries = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Recoveries, entry => entry.Fight), 1);
                double? averageInPositionRate = CleanupAdjustedAverageOrNull(mergedFightEntries, entry => entry.InPositionRate, entry => entry.Fight);
                var impactScore = ComputeImpactScore(
                    averageWeightedLaneScore: laneScores.Length == 0
                        ? 0.0
                        : CleanupAdjustedAverage(laneScores, score => score.Score.Weighted, score => score.Fight),
                    winRatePercent: winRatePercent,
                    averageDamage: averageDamage,
                    averageDowns: averageDowns,
                    averageKills: averageKills,
                    averageStrips: averageStrips,
                    averageCleanses: averageCleanses,
                    averageHealing: averageHealing,
                    averageBarrier: averageBarrier,
                    averageResurrects: averageResurrects,
                    averageDeaths: averageDeaths,
                    averageRecoveries: averageRecoveries,
                    averageInPositionRate: averageInPositionRate);
                var primaryLane = mergedFightEntries
                    .Select(entry => GetPrimaryLane(entry.Lanes))
                    .Where(lane => lane is not null)
                    .GroupBy(lane => lane!.Label, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(lane => lane.Count())
                    .ThenByDescending(lane => lane.Average(entry => entry!.StrengthPercent))
                    .FirstOrDefault()
                    ?.Key;
                var displayName = playerEntries
                    .Select(entry => NormalizeAnalysisCharacterName(entry.Player.Character))
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                    ?? playerEntries
                        .Select(entry => entry.Player.Account)
                        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                    ?? group.Key;

                return new FightAnalysisClassPlayerRowDto(
                    Account: group.Key,
                    DisplayName: displayName,
                    FightCount: sampleCount,
                    TotalFightCountAll: totalFightCountAll,
                    WinCount: winCount,
                    LossCount: lossCount,
                    DrawCount: drawCount,
                    WinRatePercent: winRatePercent,
                    ImpactScore: impactScore,
                    PrimaryLaneLabel: primaryLane,
                    AveragePrimaryLaneScore: laneScores.Length == 0
                        ? 0.0
                        : Math.Round(CleanupAdjustedAverage(laneScores, score => score.Score.Primary, score => score.Fight), 1),
                    AverageWeightedLaneScore: laneScores.Length == 0
                        ? 0.0
                        : Math.Round(CleanupAdjustedAverage(laneScores, score => score.Score.Weighted, score => score.Fight), 1));
            })
            .Where(player => player.TotalFightCountAll >= MinimumClassPlayerFightCount)
            .OrderByDescending(player => player.ImpactScore)
            .ThenByDescending(player => player.AverageWeightedLaneScore)
            .ThenByDescending(player => player.FightCount)
            .ThenBy(player => player.Account, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<FightAnalysisCharacterLaneContributionDto> BuildAggregateLaneContributions(
        IReadOnlyList<MergedPlayerFightSample> mergedFightEntries,
        int sampleCount,
        int maxCount)
    {
        return mergedFightEntries
            .SelectMany(entry => entry.Lanes.Select(lane => new
            {
                entry.Fight,
                Lane = lane
            }))
            .GroupBy(entry => entry.Lane.Key, StringComparer.OrdinalIgnoreCase)
            .Select(laneGroup =>
            {
                var laneEntries = laneGroup.ToArray();
                var sample = laneEntries[0].Lane;
                double averageStrengthPercent = Math.Round(CleanupAdjustedAverage(laneEntries, entry => entry.Lane.StrengthPercent, entry => entry.Fight), 1);
                double averageSharePercent = Math.Round(CleanupAdjustedAverage(laneEntries, entry => entry.Lane.SharePercent, entry => entry.Fight), 1);
                double appearanceRatePercent = sampleCount == 0 ? 0.0 : Math.Round(laneEntries.Length * 100.0 / sampleCount, 1);
                double overallStrengthPercent = sampleCount == 0 ? 0.0 : Math.Round(laneEntries.Sum(entry => entry.Lane.StrengthPercent * GetFightShapeImpactWeight(entry.Fight)) / sampleCount, 1);
                double overallSharePercent = sampleCount == 0 ? 0.0 : Math.Round(laneEntries.Sum(entry => entry.Lane.SharePercent * GetFightShapeImpactWeight(entry.Fight)) / sampleCount, 1);
                return new FightAnalysisCharacterLaneContributionDto(
                    LaneKey: laneGroup.Key,
                    LaneLabel: string.IsNullOrWhiteSpace(sample.Label) ? laneGroup.Key : sample.Label,
                    AverageStrengthPercent: averageStrengthPercent,
                    AverageSharePercent: averageSharePercent,
                    OverallStrengthPercent: overallStrengthPercent,
                    OverallSharePercent: overallSharePercent,
                    AppearanceRatePercent: appearanceRatePercent,
                    Samples: laneEntries.Length,
                    TotalSamplesAll: laneEntries.Length,
                    RateBand: DeriveAggregateLaneBand(overallStrengthPercent),
                    EvidenceLine: laneEntries
                        .Where(entry => !string.IsNullOrWhiteSpace(entry.Lane.EvidenceLine))
                        .OrderByDescending(entry => entry.Lane.StrengthPercent)
                        .ThenByDescending(entry => entry.Lane.SharePercent)
                        .Select(entry => entry.Lane.EvidenceLine)
                        .FirstOrDefault(),
                    Metrics: AggregateLaneMetrics(laneEntries.Select(entry => entry.Lane).ToArray()));
            })
            .OrderByDescending(lane => lane.OverallStrengthPercent)
            .ThenByDescending(lane => lane.AppearanceRatePercent)
            .ThenByDescending(lane => lane.Samples)
            .ThenBy(lane => lane.LaneLabel, StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .ToArray();
    }

    private static IReadOnlyList<PlayerFightSample> GetTrackedPlayerSamples(IReadOnlyList<FightArtifactSummaryDto> fights)
    {
        return fights
            .SelectMany(fight => (fight.FightIndex?.Players ?? Array.Empty<FightPlayerIndexDto>())
                .Where(player => !string.IsNullOrWhiteSpace(player.Account))
                .Select(player => new PlayerFightSample(fight, player)))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, int> BuildTotalPlayerFightCounts(IReadOnlyList<PlayerFightSample> entries)
    {
        return entries
            .GroupBy(entry => entry.Player.Account!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => MergePlayerFightSamplesByFight(group.ToArray()).Count,
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, int> BuildTotalCharacterFightCounts(IReadOnlyList<PlayerFightSample> entries)
    {
        return entries
            .Select(entry => new
            {
                Entry = entry,
                Account = entry.Player.Account!,
                CharacterName = NormalizeAnalysisCharacterName(entry.Player.Character) ?? "(unknown character)",
                ClassLabel = BuildClassLabel(entry.Player) ?? "Unknown class"
            })
            .GroupBy(
                entry => BuildCharacterAggregateKey(entry.Account, entry.CharacterName, entry.ClassLabel),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var first = group.First();
                    return MergePlayerFightSamplesByFight(
                        group.Select(entry => entry.Entry).ToArray(),
                        first.CharacterName).Count;
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, int> BuildTotalCharacterLaneSampleCounts(IReadOnlyList<PlayerFightSample> entries)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in entries
                     .Select(entry => new
                     {
                         Entry = entry,
                         Account = entry.Player.Account!,
                         CharacterName = NormalizeAnalysisCharacterName(entry.Player.Character) ?? "(unknown character)",
                         ClassLabel = BuildClassLabel(entry.Player) ?? "Unknown class"
                     })
                     .GroupBy(
                         entry => BuildCharacterAggregateKey(entry.Account, entry.CharacterName, entry.ClassLabel),
                         StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            var mergedFightEntries = MergePlayerFightSamplesByFight(
                group.Select(entry => entry.Entry).ToArray(),
                first.CharacterName);

            foreach (var laneGroup in mergedFightEntries
                         .SelectMany(entry => entry.Lanes)
                         .GroupBy(lane => lane.Key, StringComparer.OrdinalIgnoreCase))
            {
                result[BuildCharacterLaneAggregateKey(first.Account, first.CharacterName, first.ClassLabel, laneGroup.Key)] = laneGroup.Count();
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, int> BuildTotalClassPlayerFightCounts(IReadOnlyList<FightArtifactSummaryDto> fights)
    {
        return fights
            .SelectMany(fight => (fight.FightIndex?.Players ?? Array.Empty<FightPlayerIndexDto>())
                .Where(player => !string.IsNullOrWhiteSpace(BuildClassLabel(player)))
                .Select(player => new PlayerFightSample(fight, player)))
            .GroupBy(
                entry => BuildClassPlayerAggregateKey(
                    BuildClassLabel(entry.Player) ?? "Unknown class",
                    BuildPlayerIdentityKey(entry.Player)),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => MergePlayerFightSamplesByFight(group.ToArray()).Count,
                StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildPlayerIdentityKey(FightPlayerIndexDto player)
    {
        if (!string.IsNullOrWhiteSpace(player.Account))
        {
            return player.Account!;
        }

        if (!string.IsNullOrWhiteSpace(player.Character))
        {
            return NormalizeAnalysisCharacterName(player.Character) ?? player.Character;
        }

        return $"Actor {player.ActorId}";
    }

    private static string BuildCharacterAggregateKey(string account, string characterName, string classLabel)
    {
        return $"{account}\u001F{characterName}\u001F{classLabel}";
    }

    private static string BuildCharacterLaneAggregateKey(string account, string characterName, string classLabel, string laneKey)
    {
        return $"{BuildCharacterAggregateKey(account, characterName, classLabel)}\u001F{laneKey}";
    }

    private static string BuildClassPlayerAggregateKey(string classLabel, string playerIdentity)
    {
        return $"{classLabel}\u001F{playerIdentity}";
    }

    private static FightAnalysisCharacterPackageInputsDto BuildCharacterPackageInputs(
        IReadOnlyList<FightAnalysisCharacterLaneContributionDto> laneContributions,
        IReadOnlyDictionary<string, AggregatedProvidedBoonSample> aggregatedProvidedBoons,
        int sampleCount,
        double averageHealing,
        double averageCleanses,
        double averageBarrier,
        double averageStrips)
    {
        var pressureLane = GetLaneContribution(laneContributions, "pressure");
        var controlLane = GetLaneContribution(laneContributions, "control");
        var effectiveCrowdControlPerFight = GetLaneMetricPerFight(laneContributions, "control", "effectiveCrowdControlCount", sampleCount);

        var stability = GetProvidedBoon(aggregatedProvidedBoons, "stability");
        var protection = GetProvidedBoon(aggregatedProvidedBoons, "protection");
        var might = GetProvidedBoon(aggregatedProvidedBoons, "might");
        var fury = GetProvidedBoon(aggregatedProvidedBoons, "fury");
        var quickness = GetProvidedBoon(aggregatedProvidedBoons, "quickness");
        var resistance = GetProvidedBoon(aggregatedProvidedBoons, "resistance");

        return new FightAnalysisCharacterPackageInputsDto(
            PressureStrength: Math.Round(pressureLane?.OverallStrengthPercent ?? 0.0, 1),
            HealingPerFight: Math.Round(averageHealing, 1),
            CleansePerFight: Math.Round(averageCleanses, 1),
            ProtectionGenerationPerFight: Math.Round(protection?.GenerationPerFight ?? 0.0, 1),
            ProtectionPresencePerFight: Math.Round(protection?.PresencePerFight ?? 0.0, 1),
            StabilityGenerationPerFight: Math.Round(stability?.GenerationPerFight ?? 0.0, 1),
            StabilityPresencePerFight: Math.Round(stability?.PresencePerFight ?? 0.0, 1),
            BarrierPerFight: Math.Round(averageBarrier, 1),
            MightGenerationPerFight: Math.Round(might?.GenerationPerFight ?? 0.0, 1),
            FuryGenerationPerFight: Math.Round(fury?.GenerationPerFight ?? 0.0, 1),
            FuryPresencePerFight: Math.Round(fury?.PresencePerFight ?? 0.0, 1),
            QuicknessGenerationPerFight: Math.Round(quickness?.GenerationPerFight ?? 0.0, 1),
            QuicknessPresencePerFight: Math.Round(quickness?.PresencePerFight ?? 0.0, 1),
            ResistanceGenerationPerFight: Math.Round(resistance?.GenerationPerFight ?? 0.0, 1),
            ResistancePresencePerFight: Math.Round(resistance?.PresencePerFight ?? 0.0, 1),
            StripPerFight: Math.Round(averageStrips, 1),
            ControlStrength: Math.Round(controlLane?.OverallStrengthPercent ?? 0.0, 1),
            EffectiveCrowdControlPerFight: Math.Round(effectiveCrowdControlPerFight, 1));
    }

    private static IReadOnlyDictionary<string, AggregatedProvidedBoonSample> AggregateProvidedBoonsByFight(
        IReadOnlyList<PlayerFightSample> entries,
        int sampleCount)
    {
        if (sampleCount <= 0)
        {
            return new Dictionary<string, AggregatedProvidedBoonSample>(StringComparer.OrdinalIgnoreCase);
        }

        return entries
            .GroupBy(entry => entry.Fight.FightId, StringComparer.OrdinalIgnoreCase)
            .Select(group => MergePlayerFightProvidedBoonSample(group.ToArray()))
            .SelectMany(sample => sample.ProvidedBoons)
            .GroupBy(boon => NormalizeLookupKey(boon.Name), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var sample = group.First();
                    return new AggregatedProvidedBoonSample(
                        Key: group.Key,
                        Name: sample.Name,
                        GenerationPerFight: group.Sum(boon => boon.Generation) / sampleCount,
                        PresencePerFight: group.Sum(boon => boon.GenerationPresence) / sampleCount,
                        OverstackPerFight: group.Sum(boon => boon.Overstack) / sampleCount);
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private static FightAnalysisCharacterLaneContributionDto? GetLaneContribution(
        IReadOnlyList<FightAnalysisCharacterLaneContributionDto> laneContributions,
        string laneKey)
    {
        string normalizedLaneKey = NormalizeLookupKey(laneKey);
        return laneContributions.FirstOrDefault(lane =>
            string.Equals(NormalizeLookupKey(lane.LaneKey), normalizedLaneKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(NormalizeLookupKey(lane.LaneLabel), normalizedLaneKey, StringComparison.OrdinalIgnoreCase));
    }

    private static double GetLaneMetricPerFight(
        IReadOnlyList<FightAnalysisCharacterLaneContributionDto> laneContributions,
        string laneKey,
        string metricKey,
        int sampleCount)
    {
        if (sampleCount <= 0)
        {
            return 0.0;
        }

        var lane = GetLaneContribution(laneContributions, laneKey);
        if (lane is null)
        {
            return 0.0;
        }

        string normalizedMetricKey = NormalizeLookupKey(metricKey);
        var metric = (lane.Metrics ?? Array.Empty<FightAnalysisLaneMetricDto>())
            .FirstOrDefault(item =>
                string.Equals(NormalizeLookupKey(item.Key), normalizedMetricKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeLookupKey(item.Label), normalizedMetricKey, StringComparison.OrdinalIgnoreCase));
        return metric is null ? 0.0 : metric.TotalValue / sampleCount;
    }

    private static AggregatedProvidedBoonSample? GetProvidedBoon(
        IReadOnlyDictionary<string, AggregatedProvidedBoonSample> aggregatedProvidedBoons,
        string boonName)
    {
        return aggregatedProvidedBoons.TryGetValue(NormalizeLookupKey(boonName), out var boon)
            ? boon
            : null;
    }

    private static double CleanupAdjustedAverage<T>(
        IReadOnlyList<T> entries,
        Func<T, double> selector,
        Func<T, FightArtifactSummaryDto> fightSelector)
    {
        return CleanupAdjustedAverage(entries, selector, entry => GetFightShapeImpactWeight(fightSelector(entry)));
    }

    private static double CleanupAdjustedAverage<T>(
        IReadOnlyList<T> entries,
        Func<T, double> selector,
        Func<T, double> weightSelector)
    {
        if (entries.Count == 0)
        {
            return 0.0;
        }

        // Player and lane summaries are whole-fight values, so scale each sample by the competitive-time share.
        return entries.Sum(entry => selector(entry) * Clamp(weightSelector(entry), 0.0, 1.0)) / entries.Count;
    }

    private static double? CleanupAdjustedAverageOrNull<T>(
        IReadOnlyList<T> entries,
        Func<T, double?> selector,
        Func<T, FightArtifactSummaryDto> fightSelector)
    {
        var samples = entries
            .Select(entry => new
            {
                Value = selector(entry),
                Weight = GetFightShapeImpactWeight(fightSelector(entry))
            })
            .Where(sample => sample.Value.HasValue)
            .ToArray();

        return samples.Length == 0
            ? null
            : samples.Sum(sample => sample.Value.GetValueOrDefault() * sample.Weight) / samples.Length;
    }

    private static double? RoundNullable(double? value, int digits)
    {
        return value.HasValue ? Math.Round(value.Value, digits) : null;
    }

    private static double CalculateMedian(IEnumerable<double> values)
    {
        var sortedValues = values
            .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
            .OrderBy(value => value)
            .ToArray();
        if (sortedValues.Length == 0)
        {
            return 0.0;
        }

        int middleIndex = sortedValues.Length / 2;
        return sortedValues.Length % 2 == 1
            ? sortedValues[middleIndex]
            : (sortedValues[middleIndex - 1] + sortedValues[middleIndex]) / 2.0;
    }

    private static double GetFightShapeImpactWeight(FightArtifactSummaryDto fight)
    {
        var shape = fight.FightIndex?.FightShape;
        if (shape is null
            || !shape.Available
            || !shape.CleanupStartTimeMs.HasValue
            || string.IsNullOrWhiteSpace(shape.CleanupSide))
        {
            return 1.0;
        }

        long competitiveMs = Math.Max(0, shape.CompetitiveDurationMs);
        long cleanupMs = Math.Max(0, shape.CleanupDurationMs);
        long totalMs = competitiveMs + cleanupMs;
        if (totalMs <= 0)
        {
            return 1.0;
        }

        double adjustedMs = competitiveMs + (cleanupMs * CleanupPhaseImpactWeight);
        return Clamp(adjustedMs / totalMs, CleanupPhaseImpactWeight, 1.0);
    }

    private static string NormalizeLookupKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static double ComputeAveragePresencePercent(
        IReadOnlyList<PlayerFightSample> entries,
        Func<PlayerFightSample, double> selector)
    {
        var presenceValues = entries
            .Select(entry =>
            {
                var durationSeconds = GetFightDurationSeconds((FightArtifactSummaryDto)entry.Fight);
                if (durationSeconds <= 0.0)
                {
                    return (double?)null;
                }

                return Clamp(selector(entry) * 100.0 / durationSeconds, 0.0, 100.0);
            })
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();

        return presenceValues.Length == 0 ? 0.0 : presenceValues.Average();
    }

    private static double ComputeAveragePresencePercent(
        IReadOnlyList<MergedPlayerFightSample> entries,
        Func<MergedPlayerFightSample, double> selector)
    {
        var presenceValues = entries
            .Select(entry =>
            {
                var durationSeconds = GetFightDurationSeconds(entry.Fight);
                if (durationSeconds <= 0.0)
                {
                    return (double?)null;
                }

                return Clamp(selector(entry) * 100.0 / durationSeconds, 0.0, 100.0);
            })
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();

        return presenceValues.Length == 0 ? 0.0 : presenceValues.Average();
    }

    private static double GetFightDurationSeconds(FightArtifactSummaryDto fight)
    {
        if (fight.FightIndex?.DurationMilliseconds is long durationMilliseconds && durationMilliseconds > 0)
        {
            return durationMilliseconds / 1000.0;
        }

        return 0.0;
    }

    private static string? PickMostCommonNonEmpty(IEnumerable<string?> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key)
            .FirstOrDefault();
    }

    private static string DeriveCharacterConfidenceLabel(int sampleCount)
    {
        return sampleCount switch
        {
            >= 12 => "High",
            >= 6 => "Medium",
            _ => "Low"
        };
    }

    private static string DeriveAggregateLaneBand(double overallStrengthPercent)
    {
        return overallStrengthPercent switch
        {
            >= 40.0 => "High",
            >= 15.0 => "Medium",
            _ => "Low"
        };
    }

    private static Dictionary<string, int> BuildPillarMap(IReadOnlyList<FightExecutionPillarIndexDto>? pillars)
    {
        return (pillars ?? Array.Empty<FightExecutionPillarIndexDto>())
            .Where(pillar => !string.IsNullOrWhiteSpace(pillar.PillarId))
            .GroupBy(pillar => pillar.PillarId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Score, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<MergedPlayerFightSample> MergePlayerFightSamplesByFight(
        IReadOnlyList<PlayerFightSample> entries,
        string? normalizedCharacterName = null)
    {
        return entries
            .GroupBy(entry => entry.Fight.FightId, StringComparer.OrdinalIgnoreCase)
            .Select(group => MergePlayerFightSample(group.ToArray(), normalizedCharacterName))
            .ToArray();
    }

    private static MergedPlayerFightProvidedBoonSample MergePlayerFightProvidedBoonSample(IReadOnlyList<PlayerFightSample> entries)
    {
        var anchor = entries[0];

        return new MergedPlayerFightProvidedBoonSample(
            Fight: anchor.Fight,
            Account: PickMostCommonNonEmpty(entries.Select(entry => entry.Player.Account))
                ?? anchor.Player.Account
                ?? BuildPlayerIdentityKey(anchor.Player),
            ClassLabel: PickMostCommonNonEmpty(entries.Select(entry => BuildClassLabel(entry.Player))) ?? "Unknown class",
            ProvidedBoons: entries
                .SelectMany(entry => entry.Player.ProvidedBoons ?? Array.Empty<FightPlayerProvidedBoonIndexDto>())
                .Where(boon => boon.Id != 0 && !string.IsNullOrWhiteSpace(boon.Name))
                .GroupBy(boon => boon.Id)
                .Select(group =>
                {
                    var sample = group.First();
                    return new MergedProvidedBoonSample(
                        Id: group.Key,
                        Name: sample.Name,
                        Icon: sample.Icon,
                        StackBased: group.Any(boon => boon.StackBased),
                        Generation: Math.Round(group.Sum(boon => boon.Generation), 1),
                        GenerationPresence: Math.Round(group.Sum(boon => boon.GenerationPresence), 1),
                        Overstack: Math.Round(group.Sum(boon => boon.Overstack), 1));
                })
                .OrderByDescending(boon => boon.Generation)
                .ThenByDescending(boon => boon.GenerationPresence)
                .ToArray());
    }

    private static IReadOnlyList<FightAnalysisLaneMetricDto> AggregateLaneMetrics(IReadOnlyList<MergedPlayerLaneSample> laneEntries)
    {
        if (laneEntries.Count == 0)
        {
            return Array.Empty<FightAnalysisLaneMetricDto>();
        }

        return laneEntries
            .SelectMany(entry => entry.Metrics ?? Array.Empty<MergedPlayerLaneMetricSample>())
            .Where(metric => !string.IsNullOrWhiteSpace(metric.Label))
            .GroupBy(metric => string.IsNullOrWhiteSpace(metric.Key) ? metric.Label : metric.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var sample = group.First();
                double totalValue = group.Sum(metric => metric.Value);
                return new FightAnalysisLaneMetricDto(
                    Key: sample.Key,
                    Label: sample.Label,
                    TotalValue: Math.Round(totalValue, 1),
                    AveragePerAppearance: Math.Round(totalValue / laneEntries.Count, 1),
                    Unit: sample.Unit);
            })
            .OrderBy(metric => metric.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static MergedPlayerFightSample MergePlayerFightSample(
        IReadOnlyList<PlayerFightSample> entries,
        string? normalizedCharacterName = null)
    {
        var anchor = entries[0];
        int totalPositioningSamples = entries
            .Where(entry => entry.Player.HasPositioningData)
            .Sum(entry => Math.Max(0, entry.Player.PositioningSamples));

        double? BuildWeightedPositionRate(Func<FightPlayerIndexDto, double> selector)
        {
            if (totalPositioningSamples <= 0)
            {
                return null;
            }

            double weightedSum = entries
                .Where(entry => entry.Player.HasPositioningData && entry.Player.PositioningSamples > 0)
                .Sum(entry => selector(entry.Player) * entry.Player.PositioningSamples);
            return weightedSum / totalPositioningSamples;
        }

        return new MergedPlayerFightSample(
            Fight: anchor.Fight,
            Account: PickMostCommonNonEmpty(entries.Select(entry => entry.Player.Account))
                ?? anchor.Player.Account
                ?? $"Actor {anchor.Player.ActorId}",
            CharacterName: normalizedCharacterName
                ?? PickMostCommonNonEmpty(entries.Select(entry => NormalizeAnalysisCharacterName(entry.Player.Character)))
                ?? "(unknown character)",
            ClassLabel: PickMostCommonNonEmpty(entries.Select(entry => BuildClassLabel(entry.Player))) ?? "Unknown class",
            ActiveSeconds: entries.Sum(entry => entry.Player.ActiveSeconds),
            CombatSeconds: entries.Sum(entry => entry.Player.CombatSeconds),
            Damage: entries.Sum(entry => entry.Player.Damage),
            Downs: entries.Sum(entry => entry.Player.Downs),
            Kills: entries.Sum(entry => entry.Player.Kills),
            Strips: entries.Sum(entry => entry.Player.Strips),
            OutgoingCleanses: entries.Sum(entry => entry.Player.OutgoingCleanses),
            Healing: entries.Sum(entry => entry.Player.Healing),
            Barrier: entries.Sum(entry => entry.Player.Barrier),
            Resurrects: entries.Sum(entry => entry.Player.Resurrects),
            Deaths: entries.Sum(entry => entry.Player.Deaths),
            Recoveries: entries.Sum(entry => entry.Player.Recoveries),
            DamageTaken: entries.Sum(entry => entry.Player.DamageTaken),
            ReceivedCrowdControl: entries.Sum(entry => entry.Player.ReceivedCrowdControl),
            HasPositioningData: totalPositioningSamples > 0,
            InPositionRate: BuildWeightedPositionRate(player => player.InPositionRate),
            TooFarRate: BuildWeightedPositionRate(player => player.TooFarRate),
            OverextendedRate: BuildWeightedPositionRate(player => player.OverextendedRate),
            LateralRiskRate: BuildWeightedPositionRate(player => player.LateralRiskRate),
            FitSummary: PickMostCommonNonEmpty(entries.Select(entry => entry.Player.FitSummary)),
            ContributionProfile: PickMostCommonNonEmpty(entries.Select(entry => entry.Player.ContributionProfile)),
            KeyContributionSummary: PickMostCommonNonEmpty(entries.Select(entry => entry.Player.KeyContributionSummary)),
            EvaluationConfidenceLabel: PickMostCommonNonEmpty(entries.Select(entry => entry.Player.EvaluationConfidenceLabel)),
            EvaluationConfidenceDetail: PickMostCommonNonEmpty(entries.Select(entry => entry.Player.EvaluationConfidenceDetail)),
            EvidenceSnapshot: entries
                .SelectMany(entry => entry.Player.EvidenceSnapshot ?? Array.Empty<string>())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Lanes: MergePlayerFightLanes(entries));
    }

    private static IReadOnlyList<MergedPlayerLaneSample> MergePlayerFightLanes(IReadOnlyList<PlayerFightSample> entries)
    {
        return entries
            .SelectMany(entry => (entry.Player.Lanes ?? Array.Empty<FightPlayerLaneIndexDto>())
                .Select(lane => new
                {
                    Lane = lane,
                    Weight = Math.Max(1.0, Math.Max(entry.Player.ActiveSeconds, entry.Player.CombatSeconds))
                }))
            .GroupBy(entry => entry.Lane.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var laneEntries = group.ToArray();
                double totalWeight = laneEntries.Sum(entry => entry.Weight);
                if (totalWeight <= 0.0)
                {
                    totalWeight = laneEntries.Length;
                }

                double WeightedAverage(Func<FightPlayerLaneIndexDto, double> selector)
                {
                    return laneEntries.Sum(entry => selector(entry.Lane) * entry.Weight) / totalWeight;
                }

                var sample = laneEntries[0].Lane;
                return new MergedPlayerLaneSample(
                    Key: group.Key,
                    Label: PickMostCommonNonEmpty(laneEntries.Select(entry => entry.Lane.Label)) ?? sample.Label ?? group.Key,
                    StrengthPercent: Math.Round(WeightedAverage(lane => lane.StrengthPercent), 1),
                    SharePercent: Math.Round(WeightedAverage(lane => lane.SharePercent), 1),
                    RateBand: PickMostCommonNonEmpty(laneEntries.Select(entry => entry.Lane.RateBand)),
                    EvidenceLine: laneEntries
                        .Where(entry => !string.IsNullOrWhiteSpace(entry.Lane.EvidenceLine))
                        .OrderByDescending(entry => entry.Lane.StrengthPercent)
                        .ThenByDescending(entry => entry.Lane.SharePercent)
                        .Select(entry => entry.Lane.EvidenceLine)
                        .FirstOrDefault(),
                    Metrics: laneEntries
                        .SelectMany(entry => entry.Lane.Metrics ?? Array.Empty<FightPlayerLaneMetricIndexDto>())
                        .Where(metric => !string.IsNullOrWhiteSpace(metric.Label))
                        .GroupBy(metric => string.IsNullOrWhiteSpace(metric.Key) ? metric.Label : metric.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(metricGroup =>
                        {
                            var metricSample = metricGroup.First();
                            return new MergedPlayerLaneMetricSample(
                                Key: metricSample.Key,
                                Label: metricSample.Label,
                                Value: Math.Round(metricGroup.Sum(metric => metric.Value), 1),
                                Unit: metricSample.Unit);
                        })
                        .OrderBy(metric => metric.Label, StringComparer.OrdinalIgnoreCase)
                        .ToArray());
            })
            .OrderByDescending(lane => lane.StrengthPercent)
            .ThenByDescending(lane => lane.SharePercent)
            .ToArray();
    }

    private static double? GetPillarAverage(IReadOnlyDictionary<string, double> lookup, string pillarId)
    {
        return lookup.TryGetValue(pillarId, out double value) ? Math.Round(value, 1) : null;
    }

    private static (double Primary, double Weighted) ComputePlayerLaneScores(FightPlayerIndexDto player)
    {
        var orderedLanes = (player.Lanes ?? Array.Empty<FightPlayerLaneIndexDto>())
            .OrderByDescending(lane => lane.StrengthPercent)
            .ThenByDescending(lane => lane.SharePercent)
            .ToArray();

        if (orderedLanes.Length == 0)
        {
            return (0.0, 0.0);
        }

        double primary = orderedLanes[0].StrengthPercent;
        double weighted = primary;
        if (orderedLanes.Length > 1)
        {
            weighted += orderedLanes[1].StrengthPercent * 0.35;
        }

        if (orderedLanes.Length > 2)
        {
            weighted += orderedLanes[2].StrengthPercent * 0.15;
        }

        return (Math.Round(primary, 1), Math.Round(Math.Min(100.0, weighted), 1));
    }

    private static (double Primary, double Weighted) ComputePlayerLaneScores(IReadOnlyList<MergedPlayerLaneSample> lanes)
    {
        var orderedLanes = (lanes ?? Array.Empty<MergedPlayerLaneSample>())
            .OrderByDescending(lane => lane.StrengthPercent)
            .ThenByDescending(lane => lane.SharePercent)
            .ToArray();

        if (orderedLanes.Length == 0)
        {
            return (0.0, 0.0);
        }

        double primary = orderedLanes[0].StrengthPercent;
        double weighted = primary;
        if (orderedLanes.Length > 1)
        {
            weighted += orderedLanes[1].StrengthPercent * 0.35;
        }

        if (orderedLanes.Length > 2)
        {
            weighted += orderedLanes[2].StrengthPercent * 0.15;
        }

        return (Math.Round(primary, 1), Math.Round(Math.Min(100.0, weighted), 1));
    }

    private static FightPlayerLaneIndexDto? GetPrimaryLane(FightPlayerIndexDto player)
    {
        return (player.Lanes ?? Array.Empty<FightPlayerLaneIndexDto>())
            .OrderByDescending(lane => lane.StrengthPercent)
            .ThenByDescending(lane => lane.SharePercent)
            .FirstOrDefault();
    }

    private static MergedPlayerLaneSample? GetPrimaryLane(IReadOnlyList<MergedPlayerLaneSample> lanes)
    {
        return (lanes ?? Array.Empty<MergedPlayerLaneSample>())
            .OrderByDescending(lane => lane.StrengthPercent)
            .ThenByDescending(lane => lane.SharePercent)
            .FirstOrDefault();
    }

    private static string? BuildClassLabel(FightPlayerIndexDto player)
    {
        return !string.IsNullOrWhiteSpace(player.EliteSpec)
            ? player.EliteSpec
            : player.Profession;
    }

    private static string? NormalizeAnalysisCharacterName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith('$'))
        {
            return trimmed;
        }

        var withoutMarker = trimmed[1..].Trim();
        if (string.IsNullOrWhiteSpace(withoutMarker))
        {
            return trimmed;
        }

        int lastSpaceIndex = withoutMarker.LastIndexOf(' ');
        if (lastSpaceIndex > 0 &&
            int.TryParse(
                withoutMarker[(lastSpaceIndex + 1)..],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out _))
        {
            var collapsed = withoutMarker[..lastSpaceIndex].TrimEnd();
            return string.IsNullOrWhiteSpace(collapsed) ? withoutMarker : collapsed;
        }

        return withoutMarker;
    }

    private static double ComputeImpactScore(
        double averageWeightedLaneScore,
        double winRatePercent,
        double averageDamage,
        double averageDowns,
        double averageKills,
        double averageStrips,
        double averageCleanses,
        double averageHealing,
        double averageBarrier,
        double averageResurrects,
        double averageDeaths,
        double averageRecoveries,
        double? averageInPositionRate)
    {
        var pressureScore = Math.Min(
            100.0,
            averageDowns * 20.0
            + averageKills * 12.0
            + averageStrips * 0.22
            + (averageDamage / 300000.0) * 24.0);
        var sustainScore = Math.Min(
            100.0,
            averageCleanses * 0.90
            + (averageHealing / 180000.0) * 22.0
            + (averageBarrier / 120000.0) * 12.0
            + Math.Min(averageResurrects, 1.0) * 4.0);
        var survivalScore = Clamp(
            100.0
            - averageDeaths * 28.0
            + (((averageInPositionRate ?? 55.0) - 50.0) * 0.35),
            0.0,
            100.0);

        return Math.Round(
            Math.Min(
                100.0,
                averageWeightedLaneScore * 0.50
                + pressureScore * 0.20
                + sustainScore * 0.15
                + survivalScore * 0.10
                + winRatePercent * 0.05),
            1);
    }

    private static double ComputeFightWeightedCharacterImpactScore(
        IReadOnlyList<FightAnalysisPlayerCharacterDto> characters,
        double fallbackImpactScore)
    {
        int fightCount = characters.Sum(character => Math.Max(0, character.FightCount));
        if (fightCount <= 0)
        {
            return fallbackImpactScore;
        }

        return Math.Round(
            characters.Sum(character => character.ImpactScore * Math.Max(0, character.FightCount)) / fightCount,
            1);
    }

    private static double Clamp(double value, double minValue, double maxValue)
    {
        return Math.Max(minValue, Math.Min(maxValue, value));
    }

    private static string FormatFightDateLabel(FightArtifactSummaryDto fight)
    {
        var timestamp = GetFightDateTimeOffset(fight);
        return timestamp?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            ?? "Unknown time";
    }

    private static DateTimeOffset? GetFightDateTimeOffset(FightArtifactSummaryDto fight)
    {
        var candidates = new[]
        {
            fight.FightIndex?.TimeStartStandard,
            fight.FightIndex?.TimeStart
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (DateTimeOffset.TryParse(candidate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return parsed;
            }
        }

        if (!string.IsNullOrWhiteSpace(fight.ImportedAtUtc) &&
            DateTimeOffset.TryParse(fight.ImportedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var importedAt))
        {
            return importedAt;
        }

        return null;
    }

    private static long GetFightSortValue(FightArtifactSummaryDto fight)
    {
        return GetFightDateTimeOffset(fight)?.UtcTicks ?? long.MinValue;
    }

    private static DateOnly? GetFightLocalDate(FightArtifactSummaryDto fight)
    {
        var fightTime = GetFightDateTimeOffset(fight)?.ToLocalTime().DateTime;
        return fightTime.HasValue ? DateOnly.FromDateTime(fightTime.Value) : null;
    }

    private static IReadOnlyList<string> BuildClassOptions(IReadOnlyList<FightArtifactSummaryDto> fights)
    {
        return fights
            .SelectMany(fight =>
            {
                var labels = new List<string>();
                labels.AddRange(GetFightSideClassLabels(fight, "squad", out _));
                labels.AddRange(GetFightSideClassLabels(fight, "enemy", out _));
                return labels;
            })
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool MatchesSideClassFilters(
        FightArtifactSummaryDto fight,
        string sideId,
        IReadOnlyCollection<string> requiredClasses,
        IReadOnlyCollection<string> excludedClasses)
    {
        if (requiredClasses.Count == 0 && excludedClasses.Count == 0)
        {
            return true;
        }

        var classLabels = GetFightSideClassLabels(fight, sideId, out bool hasData);
        if (!hasData)
        {
            return false;
        }

        if (requiredClasses.Any(required =>
            !classLabels.Any(label => string.Equals(label, required, StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        if (excludedClasses.Any(excluded =>
            classLabels.Any(label => string.Equals(label, excluded, StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        return true;
    }

    private static IReadOnlyList<string> GetFightSideClassLabels(
        FightArtifactSummaryDto fight,
        string sideId,
        out bool hasData)
    {
        var side = string.Equals(sideId, "enemy", StringComparison.OrdinalIgnoreCase)
            ? fight.FightIndex?.EnemySide
            : fight.FightIndex?.SquadSide;

        var retainedLabels = (side?.Classes ?? Array.Empty<FightSideClassIndexDto>())
            .Select(entry => entry.ClassLabel?.Trim())
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (retainedLabels.Length > 0)
        {
            hasData = true;
            return retainedLabels;
        }

        if (string.Equals(sideId, "squad", StringComparison.OrdinalIgnoreCase))
        {
            var fallbackLabels = (fight.FightIndex?.Players ?? Array.Empty<FightPlayerIndexDto>())
                .Select(BuildClassLabel)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Select(label => label!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (fallbackLabels.Length > 0)
            {
                hasData = true;
                return fallbackLabels;
            }
        }

        hasData = false;
        return Array.Empty<string>();
    }

    private static bool MatchesCommander(FightArtifactSummaryDto fight, string? commander)
    {
        if (string.IsNullOrWhiteSpace(commander))
        {
            return true;
        }

        return (fight.FightIndex?.CommanderDisplayNames ?? Array.Empty<string>())
            .Any(value => string.Equals(value, commander, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesOutcome(FightArtifactSummaryDto fight, string outcomeCode)
    {
        if (string.Equals(outcomeCode, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(fight.FightIndex?.Outcome.OutcomeCode, outcomeCode, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesDateRange(FightArtifactSummaryDto fight, DateOnly? startDate, DateOnly? endDate)
    {
        DateOnly? fightDate = GetFightLocalDate(fight);
        if (!fightDate.HasValue)
        {
            return !startDate.HasValue && !endDate.HasValue;
        }

        if (startDate.HasValue && fightDate.Value < startDate.Value)
        {
            return false;
        }

        if (endDate.HasValue && fightDate.Value > endDate.Value)
        {
            return false;
        }

        return true;
    }

    private static bool MatchesPatchSelection(FightArtifactSummaryDto fight, PatchSelection selection)
    {
        if (selection.EraIds.Count == 0)
        {
            return true;
        }

        var patchEraId = fight.PatchEra?.Id;
        return !string.IsNullOrWhiteSpace(patchEraId)
            && selection.EraIds.Any(eraId => string.Equals(eraId, patchEraId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesFightAttributeSelection(FightArtifactSummaryDto fight, IReadOnlyCollection<string> requiredAttributes)
    {
        if (requiredAttributes.Count == 0)
        {
            return true;
        }

        var attributeKeys = (fight.Attributes ?? Array.Empty<FightAttributeDto>())
            .Select(attribute => attribute.Key)
            .ToArray();
        return requiredAttributes.All(required =>
            attributeKeys.Any(key => string.Equals(key, required, StringComparison.OrdinalIgnoreCase)));
    }

    private static PatchSelection NormalizePatchSelection(string? patchScope, string? patchEraIds, PatchMetadataDto metadata)
    {
        var rawScope = (patchScope ?? string.Empty).Trim();
        var explicitEraIds = NormalizeTokenFilter(patchEraIds);
        if (rawScope.StartsWith("era:", StringComparison.OrdinalIgnoreCase))
        {
            var eraId = rawScope["era:".Length..].Trim();
            explicitEraIds = string.IsNullOrWhiteSpace(eraId) ? explicitEraIds : [eraId];
            rawScope = "era";
        }

        var normalizedScope = rawScope.ToLowerInvariant() switch
        {
            "current" => "current",
            "last2" => "last2",
            "era" => "era",
            _ when explicitEraIds.Length > 0 => "custom",
            _ => "all"
        };

        var eraIds = normalizedScope switch
        {
            "current" => metadata.PatchEras.Where(era => era.IsCurrent).Select(era => era.Id).Take(1).ToArray(),
            "last2" => metadata.PatchEras
                .OrderByDescending(era => ParseDateOnly(era.StartsOn) ?? DateOnly.MinValue)
                .Take(2)
                .Select(era => era.Id)
                .ToArray(),
            "era" or "custom" => explicitEraIds,
            _ => []
        };

        return new PatchSelection(normalizedScope, eraIds);
    }

    private static IReadOnlyList<PatchImpactDto> GetPatchImpactsForSelection(PatchMetadataDto metadata, PatchSelection selection)
    {
        var activeEraIds = selection.EraIds.Count > 0
            ? selection.EraIds
            : metadata.PatchEras.Where(era => era.IsCurrent).Select(era => era.Id).ToArray();

        if (activeEraIds.Count == 0)
        {
            return [];
        }

        return metadata.PatchImpacts
            .Where(impact => activeEraIds.Any(eraId => string.Equals(eraId, impact.PatchEraId, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static IReadOnlyList<PatchImpactDto> GetPatchImpactsForClass(string classLabel, IReadOnlyList<PatchImpactDto> patchImpacts)
    {
        return patchImpacts
            .Where(impact =>
                string.Equals(impact.ClassLabel, classLabel, StringComparison.OrdinalIgnoreCase)
                || string.Equals(impact.BuildLabel, classLabel, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string NormalizeOutcomeFilter(string? outcomeCode)
    {
        var normalized = (outcomeCode ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "squad" => "squad",
            "enemy" => "enemy",
            "draw" => "draw",
            _ => "all"
        };
    }

    private static string[] NormalizeClassFilter(string? value)
    {
        return (value ?? string.Empty)
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] NormalizeTokenFilter(string? value)
    {
        return (value ?? string.Empty)
            .Split([',', ';', '|', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DateOnly? ParseDateOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }
}

internal sealed record PatchSelection(
    string Scope,
    IReadOnlyList<string> EraIds);

internal sealed record FightExpectedScoreSample(
    string FightId,
    int OverallScore,
    string CommanderKey,
    string PatchEraId,
    double SquadSize,
    double EnemySize,
    double SizeRatio,
    int DurationBucket,
    string DataProfileKey,
    HashSet<string> ContextAttributeKeys,
    IReadOnlyDictionary<string, double> SquadClassProfile);

internal sealed record FightExpectedScoreResult(
    double ExpectedScore,
    double ContextDelta,
    int SampleCount,
    string ConfidenceLabel,
    string Detail);

internal sealed record PlayerFightSample(
    FightArtifactSummaryDto Fight,
    FightPlayerIndexDto Player);

internal sealed record MergedPlayerFightSample(
    FightArtifactSummaryDto Fight,
    string Account,
    string CharacterName,
    string ClassLabel,
    double ActiveSeconds,
    double CombatSeconds,
    long Damage,
    int Downs,
    int Kills,
    int Strips,
    int OutgoingCleanses,
    long Healing,
    long Barrier,
    int Resurrects,
    int Deaths,
    int Recoveries,
    long DamageTaken,
    int ReceivedCrowdControl,
    bool HasPositioningData,
    double? InPositionRate,
    double? TooFarRate,
    double? OverextendedRate,
    double? LateralRiskRate,
    string? FitSummary,
    string? ContributionProfile,
    string? KeyContributionSummary,
    string? EvaluationConfidenceLabel,
    string? EvaluationConfidenceDetail,
    IReadOnlyList<string> EvidenceSnapshot,
    IReadOnlyList<MergedPlayerLaneSample> Lanes);

internal sealed record MergedPlayerFightProvidedBoonSample(
    FightArtifactSummaryDto Fight,
    string Account,
    string ClassLabel,
    IReadOnlyList<MergedProvidedBoonSample> ProvidedBoons);

internal sealed record MergedPlayerLaneSample(
    string Key,
    string Label,
    double StrengthPercent,
    double SharePercent,
    string? RateBand,
    string? EvidenceLine,
    IReadOnlyList<MergedPlayerLaneMetricSample> Metrics);

internal sealed record MergedPlayerLaneMetricSample(
    string Key,
    string Label,
    double Value,
    string? Unit);

internal sealed record MergedProvidedBoonSample(
    long Id,
    string Name,
    string? Icon,
    bool StackBased,
    double Generation,
    double GenerationPresence,
    double Overstack);

internal sealed record AggregatedProvidedBoonSample(
    string Key,
    string Name,
    double GenerationPerFight,
    double PresencePerFight,
    double OverstackPerFight);
