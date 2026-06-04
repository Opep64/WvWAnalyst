using System.Globalization;
using System.Text;
using WvWAnalyst.Api.Bridge;
using WvWAnalyst.Api.Services;
using WvWAnalyst.Contracts;

namespace WvWAnalyst.Api.Analysis;

public sealed class FightAnalysisService
{
    private const int MaximumSnapshotCacheEntries = 12;
    private const int MinimumClassSampleCount = 40;
    private const int MinimumClassPlayerFightCount = 20;
    private const double CleanupPhaseImpactWeight = 0.25;
    private const int MinimumExpectedScoreSampleCount = 5;
    private const int MediumExpectedScoreSampleCount = 15;
    private const int GoodExpectedScoreSampleCount = 30;
    private const int MinimumCharacterContextBaselineSampleCount = 6;
    private const int MinimumCharacterContextBucketSampleCount = 4;
    private const double MinimumCharacterContextDeltaMagnitude = 2.0;
    private const int MaximumCharacterContextFitCount = 6;
    private const string PreventionLaneKey = "prevention";
    private const string PreventionValueMetricKey = "preventionValue";
    private const string StripTotalMetricKey = "stripsTotal";
    private const string StripCorruptsMetricKey = "stripCorruptsTotal";
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

    private static readonly FightAnalysisTrackedBoon[] BoonTrendDefinitions =
    [
        new(1122, "Stability", true),
        new(717, "Protection", false),
        new(873, "Resolution", false),
        new(26980, "Resistance", false),
        new(718, "Regeneration", false),
        new(743, "Aegis", false),
        new(740, "Might", true),
        new(725, "Fury", false),
        new(1187, "Quickness", false)
    ];

    private readonly FightCatalogService _fightCatalog;
    private readonly PatchMetadataService _patchMetadata;
    private readonly FightAttributeService _fightAttributes;
    private readonly object _snapshotCacheLock = new();
    private readonly Dictionary<string, CachedFightAnalysisSnapshot> _snapshotCache = new(StringComparer.Ordinal);
    private long _snapshotCacheAccessCounter;

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
        => GetOrBuildSnapshot(
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
            fightAttributes).Snapshot;

    public FightAnalysisPlayerRowDto? BuildPlayerDetail(
        string account,
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
        if (string.IsNullOrWhiteSpace(account))
        {
            return null;
        }

        var snapshot = GetOrBuildSnapshot(
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
        return snapshot.PlayerDetails
            .FirstOrDefault(player => string.Equals(player.Account, account.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<FightAnalysisPlayerRowDto> BuildPlayerDetails(
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
        => GetOrBuildSnapshot(
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
            fightAttributes).PlayerDetails;

    private CachedFightAnalysisSnapshot GetOrBuildSnapshot(
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
        var cacheKey = BuildSnapshotCacheKey(
            _fightCatalog.CacheVersion,
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
        if (TryGetCachedSnapshot(cacheKey, out var cachedSnapshot))
        {
            return cachedSnapshot;
        }

        var snapshot = BuildSnapshotUncached(
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
        CacheSnapshot(cacheKey, snapshot);
        return snapshot;
    }

    private CachedFightAnalysisSnapshot BuildSnapshotUncached(
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
        var fightAttributeDefinitions = _fightAttributes.GetDefinitions();
        var trackedPlayerSamples = GetTrackedPlayerSamples(allFights);
        var totalPlayerFightCounts = BuildTotalPlayerFightCounts(trackedPlayerSamples);
        var totalCharacterFightCounts = BuildTotalCharacterFightCounts(trackedPlayerSamples);
        var totalCharacterLaneSampleCounts = BuildTotalCharacterLaneSampleCounts(trackedPlayerSamples);
        var totalClassSampleCounts = BuildTotalClassSampleCounts(allFights);
        var totalClassPlayerFightCounts = BuildTotalClassPlayerFightCounts(allFights);
        var patchImpactsForSelection = GetPatchImpactsForSelection(patchMetadata, patchSelection);
        var topPlayerDetails = BuildTopPlayers(
            filteredFights,
            totalPlayerFightCounts,
            totalCharacterFightCounts,
            totalCharacterLaneSampleCounts);

        var snapshot = new FightAnalysisSnapshotDto(
            Options: new FightAnalysisFilterOptionsDto(
                Commanders: commanderOptions,
                ClassOptions: classOptions,
                MinFightDate: minDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                MaxFightDate: maxDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                PatchEras: patchMetadata.PatchEras,
                FightAttributes: fightAttributeDefinitions),
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
            NightlyTeamScores: BuildNightlyTeamScores(filteredFights),
            BurstTrends: BuildBurstTrends(filteredFights),
            TopPlayers: BuildPlayerSummaryRows(topPlayerDetails),
            TopClasses: BuildTopClasses(filteredFights, totalClassSampleCounts, totalClassPlayerFightCounts, patchImpactsForSelection),
            TopEnemyClasses: BuildTopEnemyClasses(filteredFights),
            TopFive: BuildTopFive(filteredFights),
            TopLanes: BuildTopLanes(filteredFights),
            BoonTrends: BuildBoonTrends(filteredFights),
            TopBoons: BuildTopBoons(filteredFights),
            WinLossDifferences: BuildWinLossDifferences(
                filteredFights,
                expectedScoreLookup,
                fightAttributeDefinitions));

        return new CachedFightAnalysisSnapshot(snapshot, topPlayerDetails, 0);
    }

    private bool TryGetCachedSnapshot(string cacheKey, out CachedFightAnalysisSnapshot snapshot)
    {
        lock (_snapshotCacheLock)
        {
            if (_snapshotCache.TryGetValue(cacheKey, out var cached))
            {
                cached.LastAccessOrder = ++_snapshotCacheAccessCounter;
                snapshot = cached;
                return true;
            }
        }

        snapshot = default!;
        return false;
    }

    private void CacheSnapshot(string cacheKey, CachedFightAnalysisSnapshot snapshot)
    {
        lock (_snapshotCacheLock)
        {
            snapshot.LastAccessOrder = ++_snapshotCacheAccessCounter;
            _snapshotCache[cacheKey] = snapshot;
            while (_snapshotCache.Count > MaximumSnapshotCacheEntries)
            {
                var oldestKey = _snapshotCache
                    .OrderBy(entry => entry.Value.LastAccessOrder)
                    .Select(entry => entry.Key)
                    .FirstOrDefault();
                if (oldestKey is null)
                {
                    break;
                }

                _snapshotCache.Remove(oldestKey);
            }
        }
    }

    private static string BuildSnapshotCacheKey(
        long catalogVersion,
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
        var keyBuilder = new StringBuilder();
        AppendKeyPart(keyBuilder, "catalog", catalogVersion.ToString(CultureInfo.InvariantCulture));
        AppendKeyPart(keyBuilder, "commander", NormalizeCacheValue(commander));
        AppendKeyPart(keyBuilder, "start", NormalizeCacheValue(startDate));
        AppendKeyPart(keyBuilder, "end", NormalizeCacheValue(endDate));
        AppendKeyPart(keyBuilder, "outcome", NormalizeOutcomeFilter(outcomeCode));
        AppendKeyPart(keyBuilder, "squadInclude", string.Join(",", NormalizeClassFilter(squadIncludeClasses)));
        AppendKeyPart(keyBuilder, "squadExclude", string.Join(",", NormalizeClassFilter(squadExcludeClasses)));
        AppendKeyPart(keyBuilder, "enemyInclude", string.Join(",", NormalizeClassFilter(enemyIncludeClasses)));
        AppendKeyPart(keyBuilder, "enemyExclude", string.Join(",", NormalizeClassFilter(enemyExcludeClasses)));
        AppendKeyPart(keyBuilder, "patchScope", NormalizeCacheValue(patchScope));
        AppendKeyPart(keyBuilder, "patchEraIds", string.Join(",", NormalizeTokenFilter(patchEraIds)));
        AppendKeyPart(keyBuilder, "attributes", string.Join(",", NormalizeTokenFilter(fightAttributes)));
        return keyBuilder.ToString();
    }

    private static string NormalizeCacheValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }

    private static void AppendKeyPart(StringBuilder builder, string name, string value)
    {
        builder
            .Append(name)
            .Append('=')
            .Append(value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal))
            .Append('|');
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
        var averageOverallScore = executionScoreSamples.Length == 0
            ? null
            : (double?)Math.Round(CleanupAdjustedAverage(
                executionScoreSamples,
                sample => (double)sample.Execution!.OverallScore!.Value,
                sample => sample.Fight), 1);

        return new FightAnalysisOverviewDto(
            AverageOverallScore: averageOverallScore,
            AverageOverallGrade: averageOverallScore.HasValue ? ScoreToGrade(averageOverallScore.Value) : null,
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
            AverageSupportScore: GetPillarAverage(pillarLookup, "support-mitigation"),
            AverageSquadSize: averageSquadSize,
            AverageEnemySize: averageEnemySize,
            AverageDurationSeconds: averageDurationSeconds,
            MitigationSummary: mitigationSummary,
            ObliterateSummary: obliterateSummary);
    }

    private static string ScoreToGrade(double score)
    {
        return score switch
        {
            >= 90.0 => "A",
            >= 82.0 => "A-",
            >= 74.0 => "B+",
            >= 66.0 => "B",
            >= 58.0 => "B-",
            >= 50.0 => "C+",
            >= 42.0 => "C",
            >= 34.0 => "D",
            _ => "F",
        };
    }

    private static FightAnalysisDifferenceReportDto BuildWinLossDifferences(
        IReadOnlyList<FightArtifactSummaryDto> filteredFights,
        IReadOnlyDictionary<string, FightExpectedScoreResult> expectedScoreLookup,
        IReadOnlyList<FightAttributeDefinitionDto> attributeDefinitions)
    {
        var wins = filteredFights
            .Where(IsSquadWin)
            .ToArray();
        var losses = filteredFights
            .Where(IsEnemyWin)
            .ToArray();
        var confidenceLabel = DeriveDifferenceReportConfidence(wins.Length, losses.Length);

        if (wins.Length == 0 || losses.Length == 0)
        {
            return new FightAnalysisDifferenceReportDto(
                WinFightCount: wins.Length,
                LossFightCount: losses.Length,
                ConfidenceLabel: confidenceLabel,
                Summary: "This report needs at least one win and one loss inside the current filter.",
                TopSignals: Array.Empty<FightAnalysisDifferenceRowDto>(),
                ScoreDifferences: Array.Empty<FightAnalysisDifferenceRowDto>(),
                LaneDifferences: Array.Empty<FightAnalysisDifferenceRowDto>(),
                BoonDifferences: Array.Empty<FightAnalysisDifferenceRowDto>(),
                ClassDifferences: Array.Empty<FightAnalysisDifferenceRowDto>(),
                ClassDetails: Array.Empty<FightAnalysisClassDifferenceRowDto>(),
                EnemyDifferences: Array.Empty<FightAnalysisDifferenceRowDto>(),
                AttributeDifferences: Array.Empty<FightAnalysisDifferenceRowDto>());
        }

        var winOverview = BuildOverview(wins, expectedScoreLookup);
        var lossOverview = BuildOverview(losses, expectedScoreLookup);
        var scoreDifferences = BuildScoreDifferenceRows(winOverview, lossOverview, wins.Length, losses.Length);
        var laneDifferences = BuildLaneDifferenceRows(BuildTopLanes(wins), BuildTopLanes(losses), wins.Length, losses.Length);
        var boonDifferences = BuildBoonDifferenceRows(BuildTopBoons(wins), BuildTopBoons(losses), wins.Length, losses.Length);
        var classDetails = BuildClassDetailDifferenceRows(wins, losses);
        var classDifferences = BuildClassRecordDifferenceRows(classDetails);
        var enemyDifferences = BuildEnemyDifferenceRows(BuildTopEnemyClasses(wins), BuildTopEnemyClasses(losses), wins.Length, losses.Length);
        var attributeDifferences = BuildAttributeDifferenceRows(wins, losses, attributeDefinitions);
        var topSignals = scoreDifferences
            .Concat(laneDifferences)
            .Concat(boonDifferences)
            .Concat(classDifferences)
            .Concat(enemyDifferences)
            .Concat(attributeDifferences)
            .Where(row => row.Delta.HasValue)
            .OrderByDescending(GetDifferenceSortMagnitude)
            .ThenBy(row => row.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Label, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();

        return new FightAnalysisDifferenceReportDto(
            WinFightCount: wins.Length,
            LossFightCount: losses.Length,
            ConfidenceLabel: confidenceLabel,
            Summary: BuildWinLossDifferenceSummary(wins.Length, losses.Length, confidenceLabel, topSignals),
            TopSignals: topSignals,
            ScoreDifferences: scoreDifferences,
            LaneDifferences: laneDifferences,
            BoonDifferences: boonDifferences,
            ClassDifferences: classDifferences,
            ClassDetails: classDetails,
            EnemyDifferences: enemyDifferences,
            AttributeDifferences: attributeDifferences);
    }

    private static IReadOnlyList<FightAnalysisDifferenceRowDto> BuildScoreDifferenceRows(
        FightAnalysisOverviewDto wins,
        FightAnalysisOverviewDto losses,
        int winFightCount,
        int lossFightCount)
    {
        return
        [
            BuildDifferenceRow("overall", "Overall score", "Score", wins.AverageOverallScore, losses.AverageOverallScore, "score", winFightCount, lossFightCount, winFightCount, lossFightCount, "Average Analyst overall score."),
            BuildDifferenceRow("expected", "Expected score", "Score", wins.AverageExpectedScore, losses.AverageExpectedScore, "score", wins.ContextDeltaSampleCount, losses.ContextDeltaSampleCount, winFightCount, lossFightCount, "Average expected score from similar fight contexts."),
            BuildDifferenceRow("context-delta", "Context delta", "Score", wins.AverageContextDelta, losses.AverageContextDelta, "score", wins.ContextDeltaSampleCount, losses.ContextDeltaSampleCount, winFightCount, lossFightCount, "Actual score minus expected score for similar fight contexts."),
            BuildDifferenceRow("cohesion", "Cohesion", "Score", wins.AverageCohesionScore, losses.AverageCohesionScore, "score", winFightCount, lossFightCount, winFightCount, lossFightCount, "Average cohesion and positioning pillar."),
            BuildDifferenceRow("pressure", "Pressure", "Score", wins.AveragePressureScore, losses.AveragePressureScore, "score", winFightCount, lossFightCount, winFightCount, lossFightCount, "Average pressure and burst pillar."),
            BuildDifferenceRow("downstate", "Downstate", "Score", wins.AverageDownstateScore, losses.AverageDownstateScore, "score", winFightCount, lossFightCount, winFightCount, lossFightCount, "Average downstate and conversion pillar."),
            BuildDifferenceRow("support", "Support", "Score", wins.AverageSupportScore, losses.AverageSupportScore, "score", winFightCount, lossFightCount, winFightCount, lossFightCount, "Average support and mitigation pillar."),
            BuildDifferenceRow("squad-size", "Squad size", "Context", wins.AverageSquadSize, losses.AverageSquadSize, "players", winFightCount, lossFightCount, winFightCount, lossFightCount, "Average squad size in the selected fights."),
            BuildDifferenceRow("enemy-size", "Enemy size", "Context", wins.AverageEnemySize, losses.AverageEnemySize, "players", winFightCount, lossFightCount, winFightCount, lossFightCount, "Average enemy size in the selected fights."),
            BuildDifferenceRow("duration", "Duration", "Context", wins.AverageDurationSeconds, losses.AverageDurationSeconds, "seconds", winFightCount, lossFightCount, winFightCount, lossFightCount, "Average fight duration.")
        ];
    }

    private static IReadOnlyList<FightAnalysisDifferenceRowDto> BuildLaneDifferenceRows(
        IReadOnlyList<FightAnalysisLaneRowDto> winRows,
        IReadOnlyList<FightAnalysisLaneRowDto> lossRows,
        int winFightCount,
        int lossFightCount)
    {
        var winLookup = winRows.ToDictionary(row => row.LaneKey, StringComparer.OrdinalIgnoreCase);
        var lossLookup = lossRows.ToDictionary(row => row.LaneKey, StringComparer.OrdinalIgnoreCase);
        return winLookup.Keys
            .Concat(lossLookup.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key =>
            {
                winLookup.TryGetValue(key, out var winRow);
                lossLookup.TryGetValue(key, out var lossRow);
                return BuildDifferenceRow(
                    key,
                    winRow?.LaneLabel ?? lossRow?.LaneLabel ?? key,
                    "Lane",
                    winRow?.AverageStrengthPercent,
                    lossRow?.AverageStrengthPercent,
                    "%",
                    winRow?.Samples ?? 0,
                    lossRow?.Samples ?? 0,
                    winFightCount,
                    lossFightCount,
                    "Average lane strength from player lane contribution samples.");
            })
            .OrderByDescending(row => Math.Abs(row.Delta ?? 0.0))
            .ThenBy(row => row.Label, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }

    private static IReadOnlyList<FightAnalysisDifferenceRowDto> BuildBoonDifferenceRows(
        IReadOnlyList<FightAnalysisBoonRowDto> winRows,
        IReadOnlyList<FightAnalysisBoonRowDto> lossRows,
        int winFightCount,
        int lossFightCount)
    {
        var winLookup = winRows.ToDictionary(row => row.Id);
        var lossLookup = lossRows.ToDictionary(row => row.Id);
        return winLookup.Keys
            .Concat(lossLookup.Keys)
            .Distinct()
            .Select(key =>
            {
                winLookup.TryGetValue(key, out var winRow);
                lossLookup.TryGetValue(key, out var lossRow);
                return BuildDifferenceRow(
                    key.ToString(CultureInfo.InvariantCulture),
                    winRow?.Name ?? lossRow?.Name ?? $"Boon {key}",
                    "Boon",
                    winRow?.AverageCoverage,
                    lossRow?.AverageCoverage,
                    "%",
                    winRow?.FightCount ?? 0,
                    lossRow?.FightCount ?? 0,
                    winFightCount,
                    lossFightCount,
                    "Average threat-window boon coverage.");
            })
            .OrderByDescending(row => Math.Abs(row.Delta ?? 0.0))
            .ThenBy(row => row.Label, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }

    private static IReadOnlyList<FightAnalysisDifferenceRowDto> BuildClassRecordDifferenceRows(
        IReadOnlyList<FightAnalysisClassDifferenceRowDto> classDetails)
    {
        return classDetails
            .Where(row => row.PresentFightCount >= 10)
            .Select(row => BuildDifferenceRow(
                row.ClassLabel,
                row.ClassLabel,
                "Class",
                row.WinWhenPresentPercent,
                row.LossWhenPresentPercent,
                "%",
                row.PresentWinCount,
                row.PresentLossCount,
                row.PresentFightCount,
                row.PresentFightCount,
                "Win/loss split across fights where this class appeared.",
                allowZeroSamples: true))
            .OrderByDescending(row => Math.Abs(row.Delta ?? 0.0))
            .ThenBy(row => row.Label, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }

    private static IReadOnlyList<FightAnalysisClassDifferenceRowDto> BuildClassDetailDifferenceRows(
        IReadOnlyList<FightArtifactSummaryDto> wins,
        IReadOnlyList<FightArtifactSummaryDto> losses)
    {
        var winLookup = BuildClassSideDifferenceStats(wins);
        var lossLookup = BuildClassSideDifferenceStats(losses);
        return winLookup.Keys
            .Concat(lossLookup.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key =>
            {
                winLookup.TryGetValue(key, out var winStats);
                lossLookup.TryGetValue(key, out var lossStats);
                winStats ??= ClassDifferenceSideStats.Empty(key);
                lossStats ??= ClassDifferenceSideStats.Empty(key);
                int presentFightCount = winStats.FightCount + lossStats.FightCount;
                int presentTotalCount = winStats.TotalCount + lossStats.TotalCount;
                double winWhenPresentPercent = presentFightCount == 0
                    ? 0.0
                    : Math.Round(winStats.FightCount * 100.0 / presentFightCount, 1);
                double lossWhenPresentPercent = presentFightCount == 0
                    ? 0.0
                    : Math.Round(lossStats.FightCount * 100.0 / presentFightCount, 1);
                double averageCountWhenPresent = presentFightCount == 0
                    ? 0.0
                    : Math.Round(presentTotalCount * 1.0 / presentFightCount, 1);

                return new FightAnalysisClassDifferenceRowDto(
                    ClassLabel: key,
                    PresentFightCount: presentFightCount,
                    PresentWinCount: winStats.FightCount,
                    PresentLossCount: lossStats.FightCount,
                    WinWhenPresentPercent: winWhenPresentPercent,
                    LossWhenPresentPercent: lossWhenPresentPercent,
                    ResultDelta: Math.Round(winWhenPresentPercent - lossWhenPresentPercent, 1),
                    AverageCountWhenPresent: averageCountWhenPresent,
                    WinAverageCountWhenPresent: winStats.AverageCountWhenPresent,
                    LossAverageCountWhenPresent: lossStats.AverageCountWhenPresent,
                    CountDeltaWhenPresent: Math.Round(winStats.AverageCountWhenPresent - lossStats.AverageCountWhenPresent, 1),
                    WinCoverageScore: winStats.AverageCoverage,
                    LossCoverageScore: lossStats.AverageCoverage,
                    CoverageDelta: Math.Round(winStats.AverageCoverage - lossStats.AverageCoverage, 1),
                    WinCoverageSampleCount: winStats.CoverageSampleCount,
                    LossCoverageSampleCount: lossStats.CoverageSampleCount,
                    ConfidenceLabel: DeriveClassDetailDifferenceConfidence(presentFightCount),
                    Detail: "Only fights where this class appeared are counted here. Average count is the class count in those present fights; coverage is still split by win/loss when available.");
            })
            .OrderByDescending(row => row.PresentFightCount)
            .ThenByDescending(row => Math.Abs(row.ResultDelta))
            .ThenBy(row => row.ClassLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, ClassDifferenceSideStats> BuildClassSideDifferenceStats(
        IReadOnlyList<FightArtifactSummaryDto> fights)
    {
        return fights
            .SelectMany(fight => (fight.FightIndex?.SquadSide?.Classes ?? Array.Empty<FightSideClassIndexDto>())
                .Where(classEntry => !string.IsNullOrWhiteSpace(classEntry.ClassLabel) && classEntry.Count > 0)
                .Select(classEntry => new
                {
                    Fight = fight,
                    Class = classEntry
                }))
            .GroupBy(entry => entry.Class.ClassLabel, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var entries = group.ToArray();
                    var fightCount = entries
                        .Select(entry => entry.Fight.FightId)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();
                    var totalCount = entries.Sum(entry => Math.Max(0, entry.Class.Count));
                    var coverageEntries = entries
                        .Where(entry => entry.Class.FightCoverageScore > 0.0)
                        .ToArray();
                    return new ClassDifferenceSideStats(
                        ClassLabel: group.Key,
                        FightCount: fightCount,
                        TotalCount: totalCount,
                        CoverageSampleCount: coverageEntries.Length,
                        AverageCountWhenPresent: fightCount == 0 ? 0.0 : Math.Round(totalCount * 1.0 / fightCount, 1),
                        AverageCoverage: coverageEntries.Length == 0
                            ? 0.0
                            : Math.Round(CleanupAdjustedAverage(coverageEntries, entry => entry.Class.FightCoverageScore, entry => entry.Fight), 1));
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<FightAnalysisDifferenceRowDto> BuildEnemyDifferenceRows(
        IReadOnlyList<FightAnalysisEnemyClassRowDto> winRows,
        IReadOnlyList<FightAnalysisEnemyClassRowDto> lossRows,
        int winFightCount,
        int lossFightCount)
    {
        var winLookup = winRows.ToDictionary(row => row.ClassLabel, StringComparer.OrdinalIgnoreCase);
        var lossLookup = lossRows.ToDictionary(row => row.ClassLabel, StringComparer.OrdinalIgnoreCase);
        return winLookup.Keys
            .Concat(lossLookup.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key =>
            {
                winLookup.TryGetValue(key, out var winRow);
                lossLookup.TryGetValue(key, out var lossRow);
                var winValue = winFightCount == 0 || winRow is null ? (double?)null : Math.Round(winRow.TotalCount * 1.0 / winFightCount, 2);
                var lossValue = lossFightCount == 0 || lossRow is null ? (double?)null : Math.Round(lossRow.TotalCount * 1.0 / lossFightCount, 2);
                return BuildDifferenceRow(
                    key,
                    key,
                    "Enemy",
                    winValue,
                    lossValue,
                    "per fight",
                    winRow?.FightCount ?? 0,
                    lossRow?.FightCount ?? 0,
                    winFightCount,
                    lossFightCount,
                    "Average enemy class count per selected fight.");
            })
            .OrderByDescending(row => Math.Abs(row.Delta ?? 0.0))
            .ThenBy(row => row.Label, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }

    private static IReadOnlyList<FightAnalysisDifferenceRowDto> BuildAttributeDifferenceRows(
        IReadOnlyList<FightArtifactSummaryDto> wins,
        IReadOnlyList<FightArtifactSummaryDto> losses,
        IReadOnlyList<FightAttributeDefinitionDto> attributeDefinitions)
    {
        var definitionLookup = attributeDefinitions
            .Where(definition => !string.IsNullOrWhiteSpace(definition.Key))
            .ToDictionary(definition => definition.Key, StringComparer.OrdinalIgnoreCase);
        var winCounts = CountFightAttributes(wins);
        var lossCounts = CountFightAttributes(losses);
        return winCounts.Keys
            .Concat(lossCounts.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key =>
            {
                winCounts.TryGetValue(key, out int winCount);
                lossCounts.TryGetValue(key, out int lossCount);
                definitionLookup.TryGetValue(key, out var definition);
                return BuildDifferenceRow(
                    key,
                    definition?.Label ?? key,
                    "Fight Attribute",
                    wins.Count == 0 ? null : Math.Round(winCount * 100.0 / wins.Count, 1),
                    losses.Count == 0 ? null : Math.Round(lossCount * 100.0 / losses.Count, 1),
                    "%",
                    winCount,
                    lossCount,
                    wins.Count,
                    losses.Count,
                    "Share of selected fights with this attribute.",
                    allowZeroSamples: true);
            })
            .OrderByDescending(row => Math.Abs(row.Delta ?? 0.0))
            .ThenBy(row => row.Label, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }

    private static FightAnalysisDifferenceRowDto BuildDifferenceRow(
        string key,
        string label,
        string group,
        double? winValue,
        double? lossValue,
        string unit,
        int winSampleCount,
        int lossSampleCount,
        int winFightCount,
        int lossFightCount,
        string detail,
        bool allowZeroSamples = false)
    {
        double? roundedWinValue = winValue.HasValue ? Math.Round(winValue.Value, 1) : null;
        double? roundedLossValue = lossValue.HasValue ? Math.Round(lossValue.Value, 1) : null;
        double? delta = roundedWinValue.HasValue && roundedLossValue.HasValue
            ? Math.Round(roundedWinValue.Value - roundedLossValue.Value, 1)
            : null;

        return new FightAnalysisDifferenceRowDto(
            Key: key,
            Label: label,
            Group: group,
            WinValue: roundedWinValue,
            LossValue: roundedLossValue,
            Delta: delta,
            Unit: unit,
            WinSampleCount: winSampleCount,
            LossSampleCount: lossSampleCount,
            ConfidenceLabel: DeriveDifferenceRowConfidence(winFightCount, lossFightCount, winSampleCount, lossSampleCount, allowZeroSamples),
            DirectionLabel: BuildDifferenceDirectionLabel(delta),
            Detail: detail);
    }

    private static Dictionary<string, int> CountFightAttributes(IReadOnlyList<FightArtifactSummaryDto> fights)
    {
        return fights
            .SelectMany(fight => (fight.Attributes ?? Array.Empty<FightAttributeDto>())
                .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Key))
                .Select(attribute => attribute.Key))
            .GroupBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSquadWin(FightArtifactSummaryDto fight)
    {
        return string.Equals(fight.FightIndex?.Outcome.OutcomeCode, "squad", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEnemyWin(FightArtifactSummaryDto fight)
    {
        return string.Equals(fight.FightIndex?.Outcome.OutcomeCode, "enemy", StringComparison.OrdinalIgnoreCase);
    }

    private static string DeriveDifferenceReportConfidence(int winFightCount, int lossFightCount)
    {
        var minimum = Math.Min(winFightCount, lossFightCount);
        if (minimum >= 15)
        {
            return "High";
        }

        return minimum >= 6 ? "Medium" : "Low";
    }

    private static string DeriveDifferenceRowConfidence(
        int winFightCount,
        int lossFightCount,
        int winSampleCount,
        int lossSampleCount,
        bool allowZeroSamples)
    {
        var minimumFightCount = Math.Min(winFightCount, lossFightCount);
        var minimumSampleCount = Math.Min(winSampleCount, lossSampleCount);
        if (minimumFightCount < 6)
        {
            return "Low";
        }

        if (!allowZeroSamples && minimumSampleCount < 3)
        {
            return "Low";
        }

        if (minimumFightCount >= 15 && (allowZeroSamples || minimumSampleCount >= 10))
        {
            return "High";
        }

        return "Medium";
    }

    private static string DeriveClassDetailDifferenceConfidence(int presentFightCount)
    {
        if (presentFightCount >= 30)
        {
            return "High";
        }

        return presentFightCount >= 10 ? "Medium" : "Low";
    }

    private static string BuildDifferenceDirectionLabel(double? delta)
    {
        if (!delta.HasValue || Math.Abs(delta.Value) < 0.05)
        {
            return "Even";
        }

        return delta.Value > 0.0 ? "Higher in wins" : "Higher in losses";
    }

    private static double GetDifferenceSortMagnitude(FightAnalysisDifferenceRowDto row)
    {
        var magnitude = Math.Abs(row.Delta ?? 0.0);
        return row.Unit switch
        {
            "seconds" => magnitude / 15.0,
            "players" => magnitude * 10.0,
            "per fight" => magnitude * 12.0,
            _ => magnitude
        };
    }

    private static string BuildWinLossDifferenceSummary(
        int winFightCount,
        int lossFightCount,
        string confidenceLabel,
        IReadOnlyList<FightAnalysisDifferenceRowDto> topSignals)
    {
        if (topSignals.Count == 0)
        {
            return $"Compared {winFightCount} wins against {lossFightCount} losses. No comparable metric differences were available.";
        }

        var strongest = topSignals.Take(3)
            .Select(row => $"{row.Label} {FormatSignedDifference(row.Delta, row.Unit)}")
            .ToArray();
        return $"Compared {winFightCount} wins against {lossFightCount} losses with {confidenceLabel.ToLowerInvariant()} confidence. Strongest separators: {string.Join(", ", strongest)}.";
    }

    private static string FormatSignedDifference(double? value, string unit)
    {
        if (!value.HasValue)
        {
            return "n/a";
        }

        var sign = value.Value > 0.0 ? "+" : string.Empty;
        var suffix = unit switch
        {
            "%" => "%",
            "seconds" => " sec",
            "players" => " players",
            "per fight" => "/fight",
            _ => string.Empty
        };
        return $"{sign}{value.Value.ToString("0.#", CultureInfo.InvariantCulture)}{suffix}";
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

        var barrierOvercapSamples = mitigationSamples
            .Select(sample => sample.Mitigation?.BarrierOvercap)
            .Where(summary => summary is { Available: true })
            .Cast<FightBarrierOvercapSummaryIndexDto>()
            .ToArray();
        FightAnalysisBarrierOvercapSummaryDto? barrierOvercap = null;
        if (barrierOvercapSamples.Length > 0)
        {
            double rawBarrierEvaluated = Math.Round(barrierOvercapSamples.Sum(summary => Math.Max(0, summary.RawBarrierEvaluated)), 0);
            double estimatedOvercap = Math.Round(barrierOvercapSamples.Sum(summary => Math.Max(0, summary.EstimatedOvercap)), 0);
            barrierOvercap = new FightAnalysisBarrierOvercapSummaryDto(
                AvailableFightCount: barrierOvercapSamples.Length,
                RawBarrierEvaluated: rawBarrierEvaluated,
                EstimatedOvercap: estimatedOvercap,
                OvercapPercentOfEvaluated: rawBarrierEvaluated > 0 ? Math.Round(estimatedOvercap * 100.0 / rawBarrierEvaluated, 1) : 0,
                EvaluatedApplicationGroups: barrierOvercapSamples.Sum(summary => Math.Max(0, summary.EvaluatedApplicationGroups)),
                OvercapApplicationGroups: barrierOvercapSamples.Sum(summary => Math.Max(0, summary.OvercapApplicationGroups)),
                HighConfidenceGroups: barrierOvercapSamples.Sum(summary => Math.Max(0, summary.HighConfidenceGroups)),
                EstimatedHealthPoolGroups: barrierOvercapSamples.Sum(summary => Math.Max(0, summary.EstimatedHealthPoolGroups)),
                SkippedNoBarrierStateGroups: barrierOvercapSamples.Sum(summary => Math.Max(0, summary.SkippedNoBarrierStateGroups)));
        }

        var reflectSamples = mitigationSamples
            .Select(sample => sample.Mitigation?.Reflects)
            .Where(summary => summary is { HasMissileData: true })
            .Cast<FightReflectSummaryIndexDto>()
            .ToArray();
        FightAnalysisReflectSummaryDto? reflects = null;
        if (reflectSamples.Length > 0)
        {
            reflects = new FightAnalysisReflectSummaryDto(
                AvailableFightCount: reflectSamples.Length,
                TotalReflectedProjectiles: reflectSamples.Sum(summary => Math.Max(0, summary.TotalReflectedProjectiles)),
                TotalLandedHits: reflectSamples.Sum(summary => Math.Max(0, summary.TotalLandedHits)),
                TotalLandedDamage: Math.Round(reflectSamples.Sum(summary => Math.Max(0, summary.TotalLandedDamage)), 0),
                TotalEstimatedMitigatedProjectiles: reflectSamples.Sum(summary => Math.Max(0, summary.TotalEstimatedMitigatedProjectiles)),
                TotalEstimatedMitigatedDamage: Math.Round(reflectSamples.Sum(summary => Math.Max(0, summary.TotalEstimatedMitigatedDamage)), 0),
                TotalUnestimatedMitigatedProjectiles: reflectSamples.Sum(summary => Math.Max(0, summary.TotalUnestimatedMitigatedProjectiles)),
                TotalDowns: reflectSamples.Sum(summary => Math.Max(0, summary.TotalDowns)),
                TotalKills: reflectSamples.Sum(summary => Math.Max(0, summary.TotalKills)),
                SquadToEnemy: BuildReflectSideSummary(reflectSamples.Select(summary => summary.SquadToEnemy)),
                EnemyToSquad: BuildReflectSideSummary(reflectSamples.Select(summary => summary.EnemyToSquad)));
        }

        var shieldOfCourageSamples = mitigationSamples
            .Select(sample => sample.Mitigation?.ShieldOfCourage)
            .Where(summary => summary is { Available: true })
            .Cast<FightShieldOfCourageSummaryIndexDto>()
            .ToArray();
        FightAnalysisShieldOfCourageSummaryDto? shieldOfCourage = null;
        if (shieldOfCourageSamples.Length > 0)
        {
            shieldOfCourage = new FightAnalysisShieldOfCourageSummaryDto(
                AvailableFightCount: shieldOfCourageSamples.Length,
                FightsWithBlockedAttacks: shieldOfCourageSamples.Count(summary => summary.BlockedAttackCount > 0),
                BlockedAttackCount: shieldOfCourageSamples.Sum(summary => Math.Max(0, summary.BlockedAttackCount)),
                EstimatedBlockedDamage: Math.Round(shieldOfCourageSamples.Sum(summary => Math.Max(0, summary.EstimatedBlockedDamage)), 0),
                FallbackEstimateCount: shieldOfCourageSamples.Sum(summary => Math.Max(0, summary.FallbackEstimateCount)),
                MaxCoveredPlayers: shieldOfCourageSamples.Max(summary => Math.Max(0, summary.MaxCoveredPlayers)));
        }

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
            BarrierOvercap: barrierOvercap,
            Reflects: reflects,
            ShieldOfCourage: shieldOfCourage,
            NegatedHitSummaries: negatedHitSummaries);
    }

    private static FightAnalysisReflectSideSummaryDto BuildReflectSideSummary(IEnumerable<FightReflectSideSummaryIndexDto?> sides)
    {
        var sideSamples = sides
            .Where(side => side is not null)
            .Cast<FightReflectSideSummaryIndexDto>()
            .ToArray();

        return new FightAnalysisReflectSideSummaryDto(
            ReflectedProjectiles: sideSamples.Sum(side => Math.Max(0, side.ReflectedProjectiles)),
            LandedHits: sideSamples.Sum(side => Math.Max(0, side.LandedHits)),
            LandedDamage: Math.Round(sideSamples.Sum(side => Math.Max(0, side.LandedDamage)), 0),
            EstimatedMitigatedProjectiles: sideSamples.Sum(side => Math.Max(0, side.EstimatedMitigatedProjectiles)),
            EstimatedMitigatedDamage: Math.Round(sideSamples.Sum(side => Math.Max(0, side.EstimatedMitigatedDamage)), 0),
            HighConfidenceMitigatedProjectiles: sideSamples.Sum(side => Math.Max(0, side.HighConfidenceMitigatedProjectiles)),
            HighConfidenceMitigatedDamage: Math.Round(sideSamples.Sum(side => Math.Max(0, side.HighConfidenceMitigatedDamage)), 0),
            FallbackEstimatedMitigatedProjectiles: sideSamples.Sum(side => Math.Max(0, side.FallbackEstimatedMitigatedProjectiles)),
            FallbackEstimatedMitigatedDamage: Math.Round(sideSamples.Sum(side => Math.Max(0, side.FallbackEstimatedMitigatedDamage)), 0),
            UnestimatedMitigatedProjectiles: sideSamples.Sum(side => Math.Max(0, side.UnestimatedMitigatedProjectiles)),
            DownEvents: sideSamples.Sum(side => Math.Max(0, side.DownEvents)),
            KillEvents: sideSamples.Sum(side => Math.Max(0, side.KillEvents)),
            MatchedDamageEvents: sideSamples.Sum(side => Math.Max(0, side.MatchedDamageEvents)));
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
                    SupportScore: pillars.TryGetValue("support-mitigation", out int support) ? support : null);
            })
            .ToArray();
    }

    private static IReadOnlyList<FightAnalysisTeamScoreTrendPointDto> BuildNightlyTeamScores(
        IReadOnlyList<FightArtifactSummaryDto> fights)
    {
        return fights
            .Select(fight => new
            {
                Fight = fight,
                Date = GetFightLocalDate(fight),
                Execution = fight.FightIndex?.Execution
            })
            .Where(sample => sample.Date.HasValue
                && sample.Execution?.ScoreAvailable == true
                && sample.Execution.OverallScore.HasValue)
            .GroupBy(sample => sample.Date!.Value)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var samples = group.ToArray();
                var averageScore = CalculateTeamScoreAverage(samples.Select(sample => sample.Fight).ToArray()) ?? 0.0;

                return new FightAnalysisTeamScoreTrendPointDto(
                    DateKey: group.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    DateLabel: group.Key.ToString("MMM d", CultureInfo.InvariantCulture),
                    FightCount: samples.Length,
                    AverageOverallScore: averageScore);
            })
            .ToArray();
    }

    private static double? CalculateTeamScoreAverage(IReadOnlyList<FightArtifactSummaryDto> fights)
    {
        var scoreSamples = fights
            .Select(fight => new
            {
                Fight = fight,
                Execution = fight.FightIndex?.Execution
            })
            .Where(sample => sample.Execution?.ScoreAvailable == true && sample.Execution.OverallScore.HasValue)
            .ToArray();

        return scoreSamples.Length == 0
            ? null
            : Math.Round(CleanupAdjustedAverage(
                scoreSamples,
                sample => (double)sample.Execution!.OverallScore!.Value,
                sample => sample.Fight), 1);
    }

    private static IReadOnlyList<FightAnalysisBurstTrendPointDto> BuildBurstTrends(IReadOnlyList<FightArtifactSummaryDto> fights)
    {
        return fights
            .Select(fight =>
            {
                var fightTimestamp = GetFightDateTimeOffset(fight);
                return new FightAnalysisBurstTrendPointDto(
                    FightId: fight.FightId,
                    FightName: fight.FightIndex?.FightName ?? fight.SourceFileName ?? fight.FightId,
                    FightDateLabel: FormatFightDateLabel(fight),
                    FightDateUtc: fightTimestamp?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                    PatchEraId: fight.PatchEra?.Id,
                    PatchEraLabel: fight.PatchEra?.Label,
                    Squad: BuildBurstSideTrend(fight.FightIndex?.TopBursts),
                    Enemy: BuildBurstSideTrend(fight.FightIndex?.EnemyTopBursts));
            })
            .Where(point => HasBurstTrendData(point.Squad) || HasBurstTrendData(point.Enemy))
            .ToArray();
    }

    private static FightAnalysisBurstSideTrendDto BuildBurstSideTrend(IReadOnlyList<FightTopBurstIndexDto>? bursts)
    {
        var retainedBursts = (bursts ?? Array.Empty<FightTopBurstIndexDto>())
            .Where(burst => burst.Damage > 0 || burst.Strips > 0 || burst.Downs > 0 || burst.Kills > 0)
            .ToArray();

        if (retainedBursts.Length == 0)
        {
            return new FightAnalysisBurstSideTrendDto(null, null, null, null);
        }

        return new FightAnalysisBurstSideTrendDto(
            Damage: retainedBursts.Max(burst => burst.Damage),
            Strips: retainedBursts.Max(burst => burst.Strips),
            Downs: retainedBursts.Max(burst => burst.Downs),
            Kills: retainedBursts.Max(burst => burst.Kills));
    }

    private static bool HasBurstTrendData(FightAnalysisBurstSideTrendDto side)
        => side.Damage is not null || side.Strips is not null || side.Downs is not null || side.Kills is not null;

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

    private static IReadOnlyList<FightAnalysisTopFiveCategoryDto> BuildTopFive(IReadOnlyList<FightArtifactSummaryDto> fights)
    {
        var samples = fights
            .SelectMany(fight => (fight.FightIndex?.Players ?? Array.Empty<FightPlayerIndexDto>())
                .Where(player => !string.IsNullOrWhiteSpace(player.Account))
                .Select(player => new PlayerFightSample(fight, player)))
            .ToArray();
        int minimumAppearanceFightCount = BuildTopFiveMinimumAppearanceFightCount(fights.Count);
        IReadOnlyDictionary<string, int> accountFightCounts = BuildTopFiveAccountFightCounts(samples);

        return
        [
            BuildTopFiveSumCategory(
                samples,
                minimumAppearanceFightCount,
                key: "damage",
                label: "Pain Train",
                unit: "damage",
                detail: "Total target damage to enemy players across the selected fights.",
                selector: sample => sample.Player.Damage),
            BuildTopFiveSumCategory(
                samples,
                minimumAppearanceFightCount,
                key: "down-contribution",
                label: "Down Payment",
                unit: "count",
                detail: "Total down contribution against enemy players across the selected fights.",
                selector: sample => sample.Player.DownContribution),
            BuildTopFiveSumCategory(
                samples,
                minimumAppearanceFightCount,
                key: "damage-to-downed",
                label: "Finish the Job",
                unit: "damage",
                detail: "Total damage dealt to enemy players while they were downed across the selected fights.",
                selector: sample => sample.Player.DamageToDownedTargets),
            BuildTopFiveSumCategory(
                samples,
                minimumAppearanceFightCount,
                key: "downs",
                label: "Sit Down",
                unit: "count",
                detail: "Enemy player downs credited across the selected fights.",
                selector: sample => sample.Player.Downs),
            BuildTopFiveSumCategory(
                samples,
                minimumAppearanceFightCount,
                key: "kills",
                label: "And Stay Down",
                unit: "count",
                detail: "Enemy player kills credited across the selected fights.",
                selector: sample => sample.Player.Kills),
            BuildTopFiveWeightedAverageCategory(
                samples,
                minimumAppearanceFightCount,
                key: "in-position",
                label: "Tag Hugger",
                unit: "percent",
                detail: "Weighted in-position percentage using each player's positioning samples across the selected fights.",
                selector: sample => sample.Player.InPositionRate,
                weightSelector: sample => sample.Player.HasPositioningData ? Math.Max(sample.Player.PositioningSamples, 0) : 0),
            BuildTopFiveSumCategory(
                samples,
                minimumAppearanceFightCount,
                key: "stability-produced",
                label: "Stand Your Ground",
                unit: "boon",
                detail: "Total Stability generation across the selected fights.",
                selector: sample => GetProvidedBoonGeneration(sample.Player, "stability")),
            BuildTopFiveSumCategory(
                samples,
                minimumAppearanceFightCount,
                key: "protection-produced",
                label: "Protected Assets",
                unit: "boon",
                detail: "Total Protection generation across the selected fights.",
                selector: sample => GetProvidedBoonGeneration(sample.Player, "protection")),
            BuildTopFiveSumCategory(
                samples,
                minimumAppearanceFightCount,
                key: "healing",
                label: "Health Department",
                unit: "healing",
                detail: "Total outgoing healing across the selected fights.",
                selector: sample => sample.Player.Healing),
            BuildTopFiveSumCategory(
                samples,
                minimumAppearanceFightCount,
                key: "downed-healing",
                label: "Emergency Room",
                unit: "healing",
                detail: "Total outgoing healing applied to downed allies across the selected fights.",
                selector: sample => sample.Player.DownedHealing),
            BuildTopFiveSumCategory(
                samples,
                minimumAppearanceFightCount,
                key: "illusion-of-life-rezzes",
                label: "Encore",
                unit: "count",
                detail: "Successful squad recovery windows where this player applied Illusion of Life.",
                selector: sample => sample.Player.IllusionOfLifeRezzes),
            BuildTopFiveSumCategory(
                samples,
                minimumAppearanceFightCount,
                key: "cleanses",
                label: "Squeaky Clean",
                unit: "count",
                detail: "Total outgoing condition cleanses across the selected fights.",
                selector: sample => sample.Player.OutgoingCleanses),
            BuildTopFiveSumCategory(
                samples,
                minimumAppearanceFightCount,
                key: "strips",
                label: "Strip Miner",
                unit: "count",
                detail: "Total boon strips across the selected fights.",
                selector: sample => sample.Player.Strips),
            BuildTopFiveSumCategory(
                samples,
                minimumAppearanceFightCount,
                key: "boon-corruptions",
                label: "Corrupting Influence",
                unit: "count",
                detail: "Total boon corruptions attributed across the selected fights.",
                selector: sample => sample.Player.Corrupts),
            BuildTopFiveSumCategory(
                samples,
                minimumAppearanceFightCount,
                key: "barrier",
                label: "Shields Up",
                unit: "barrier",
                detail: "Total outgoing barrier across the selected fights.",
                selector: sample => sample.Player.Barrier),
            BuildTopFiveSumCategory(
                samples,
                minimumAppearanceFightCount,
                key: "pet-damage-absorbed",
                label: "Pet Insurance",
                unit: "damage",
                detail: "Incoming damage absorbed by owned pets and minions across the selected fights.",
                selector: sample => sample.Player.PetDamageAbsorbed),
            BuildTopFiveSumCategory(
                samples,
                minimumAppearanceFightCount,
                key: "damage-reflected-on-enemy",
                label: "Back Atcha",
                unit: "damage",
                detail: "Enemy damage from squad-to-enemy reflected projectiles.",
                selector: sample => sample.Player.DamageReflectedOnEnemy),
            BuildTopFiveSumCategory(
                samples,
                minimumAppearanceFightCount,
                key: "mystic-rebuke-damage",
                label: "Mystic Payback",
                unit: "damage",
                detail: "Total Mystic Rebuke damage to enemy players across the selected fights.",
                selector: sample => sample.Player.MysticRebukeDamage),
            BuildTopFiveWinLossCategory(samples, minimumAppearanceFightCount),
            BuildTopFiveBurstActorCategory(
                fights,
                accountFightCounts,
                minimumAppearanceFightCount,
                key: "top-damage-bursts",
                label: "Focus Fire",
                detail: "Number of selected-fight squad burst windows where this account appeared in the top damage actors.",
                actorSelector: burst => burst.TopPressureActors ?? Array.Empty<FightTopBurstActorIndexDto>()),
            BuildTopFiveBurstActorCategory(
                fights,
                accountFightCounts,
                minimumAppearanceFightCount,
                key: "top-strip-bursts",
                label: "Boon Breaker",
                detail: "Number of selected-fight squad burst windows where this account appeared in the top strip actors.",
                actorSelector: burst => burst.TopStripActors ?? Array.Empty<FightTopBurstActorIndexDto>()),
            BuildTopFiveSumCategory(
                samples,
                minimumAppearanceFightCount,
                key: "pulls",
                label: "Get Over Here",
                unit: "count",
                detail: "Total outgoing knockback/pull crowd-control events against enemy players across the selected fights.",
                selector: sample => sample.Player.Pulls),
            BuildTopFiveDistinctClassesCategory(samples, minimumAppearanceFightCount)
        ];
    }

    private static FightAnalysisTopFiveCategoryDto BuildTopFiveSumCategory(
        IReadOnlyList<PlayerFightSample> samples,
        int minimumAppearanceFightCount,
        string key,
        string label,
        string unit,
        string detail,
        Func<PlayerFightSample, double> selector)
    {
        var rows = samples
            .GroupBy(sample => sample.Player.Account!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var entries = group.ToArray();
                double value = RoundTopFiveValue(entries.Sum(selector), unit);
                var characters = BuildTopFiveSumCharacters(entries, selector, unit);
                var classesPlayed = entries
                    .Select(entry => BuildClassLabel(entry.Player))
                    .Where(label => !string.IsNullOrWhiteSpace(label))
                    .Select(label => label!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new
                {
                    Account = group.Key,
                    DisplayName = PickMostCommonNonEmpty(entries.Select(entry => NormalizeAnalysisCharacterName(entry.Player.Character))) ?? group.Key,
                    Value = value,
                    ValueDetail = (string?)null,
                    FightCount = entries.Select(entry => entry.Fight.FightId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    CharacterSampleCount = entries.Length,
                    ClassesPlayed = classesPlayed,
                    Characters = characters
                };
            })
            .Where(row => row.Value > 0.0 && row.FightCount >= minimumAppearanceFightCount)
            .OrderByDescending(row => row.Value)
            .ThenByDescending(row => row.FightCount)
            .ThenBy(row => row.Account, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select((row, index) => new FightAnalysisTopFiveRowDto(
                Rank: index + 1,
                Account: row.Account,
                DisplayName: row.DisplayName,
                Value: row.Value,
                ValueDetail: row.ValueDetail,
                FightCount: row.FightCount,
                CharacterSampleCount: row.CharacterSampleCount,
                ClassesPlayed: row.ClassesPlayed,
                Characters: row.Characters))
            .ToArray();

        return new FightAnalysisTopFiveCategoryDto(
            Key: key,
            Label: label,
            Unit: unit,
            Detail: detail,
            Rows: rows);
    }

    private static FightAnalysisTopFiveCategoryDto BuildTopFiveWeightedAverageCategory(
        IReadOnlyList<PlayerFightSample> samples,
        int minimumAppearanceFightCount,
        string key,
        string label,
        string unit,
        string detail,
        Func<PlayerFightSample, double> selector,
        Func<PlayerFightSample, double> weightSelector)
    {
        var rows = samples
            .GroupBy(sample => sample.Player.Account!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var entries = group.ToArray();
                double weight = entries.Sum(weightSelector);
                double value = weight <= 0.0 ? 0.0 : RoundTopFiveValue(entries.Sum(entry => selector(entry) * weightSelector(entry)) / weight, unit);
                var classesPlayed = entries
                    .Select(entry => BuildClassLabel(entry.Player))
                    .Where(classLabel => !string.IsNullOrWhiteSpace(classLabel))
                    .Select(classLabel => classLabel!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(classLabel => classLabel, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new
                {
                    Account = group.Key,
                    DisplayName = PickMostCommonNonEmpty(entries.Select(entry => NormalizeAnalysisCharacterName(entry.Player.Character))) ?? group.Key,
                    Value = value,
                    Weight = weight,
                    FightCount = entries.Select(entry => entry.Fight.FightId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    CharacterSampleCount = entries.Length,
                    ClassesPlayed = classesPlayed,
                    Characters = BuildTopFiveWeightedAverageCharacters(entries, selector, weightSelector, unit)
                };
            })
            .Where(row => row.Weight > 0.0 && row.FightCount >= minimumAppearanceFightCount)
            .OrderByDescending(row => row.Value)
            .ThenByDescending(row => row.Weight)
            .ThenBy(row => row.Account, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select((row, index) => new FightAnalysisTopFiveRowDto(
                Rank: index + 1,
                Account: row.Account,
                DisplayName: row.DisplayName,
                Value: row.Value,
                ValueDetail: null,
                FightCount: row.FightCount,
                CharacterSampleCount: row.CharacterSampleCount,
                ClassesPlayed: row.ClassesPlayed,
                Characters: row.Characters))
            .ToArray();

        return new FightAnalysisTopFiveCategoryDto(
            Key: key,
            Label: label,
            Unit: unit,
            Detail: detail,
            Rows: rows);
    }

    private static FightAnalysisTopFiveCategoryDto BuildTopFiveDistinctClassesCategory(IReadOnlyList<PlayerFightSample> samples, int minimumAppearanceFightCount)
    {
        var rows = samples
            .GroupBy(sample => sample.Player.Account!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var entries = group.ToArray();
                var classesPlayed = entries
                    .Select(entry => BuildClassLabel(entry.Player))
                    .Where(classLabel => !string.IsNullOrWhiteSpace(classLabel))
                    .Select(classLabel => classLabel!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(classLabel => classLabel, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new
                {
                    Account = group.Key,
                    DisplayName = PickMostCommonNonEmpty(entries.Select(entry => NormalizeAnalysisCharacterName(entry.Player.Character))) ?? group.Key,
                    Value = (double)classesPlayed.Length,
                    FightCount = entries.Select(entry => entry.Fight.FightId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    CharacterSampleCount = entries.Length,
                    ClassesPlayed = classesPlayed,
                    Characters = BuildTopFiveSumCharacters(entries, _ => 1.0, "count")
                };
            })
            .Where(row => row.Value > 0.0 && row.FightCount >= minimumAppearanceFightCount)
            .OrderByDescending(row => row.Value)
            .ThenByDescending(row => row.FightCount)
            .ThenBy(row => row.Account, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select((row, index) => new FightAnalysisTopFiveRowDto(
                Rank: index + 1,
                Account: row.Account,
                DisplayName: row.DisplayName,
                Value: row.Value,
                ValueDetail: string.Join(", ", row.ClassesPlayed),
                FightCount: row.FightCount,
                CharacterSampleCount: row.CharacterSampleCount,
                ClassesPlayed: row.ClassesPlayed,
                Characters: row.Characters))
            .ToArray();

        return new FightAnalysisTopFiveCategoryDto(
            Key: "classes-played",
            Label: "Can't Make Up My Mind",
            Unit: "count",
            Detail: "Distinct professions or elite specializations played by each Guild Wars 2 account in the selected fights.",
            Rows: rows);
    }

    private static FightAnalysisTopFiveCategoryDto BuildTopFiveWinLossCategory(IReadOnlyList<PlayerFightSample> samples, int minimumAppearanceFightCount)
    {
        var rows = samples
            .GroupBy(sample => sample.Player.Account!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var entries = group.ToArray();
                var fights = entries
                    .GroupBy(entry => entry.Fight.FightId, StringComparer.OrdinalIgnoreCase)
                    .Select(fightGroup => fightGroup.First().Fight)
                    .ToArray();
                int wins = fights.Count(fight => GetSquadWinResult(fight) == true);
                int losses = fights.Count(fight => GetSquadWinResult(fight) == false);
                int draws = fights.Length - wins - losses;
                int decided = wins + losses;
                double value = decided == 0 ? 0.0 : Math.Round(wins * 100.0 / decided, 1);
                var classesPlayed = entries
                    .Select(entry => BuildClassLabel(entry.Player))
                    .Where(classLabel => !string.IsNullOrWhiteSpace(classLabel))
                    .Select(classLabel => classLabel!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(classLabel => classLabel, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new
                {
                    Account = group.Key,
                    DisplayName = PickMostCommonNonEmpty(entries.Select(entry => NormalizeAnalysisCharacterName(entry.Player.Character))) ?? group.Key,
                    Value = value,
                    Wins = wins,
                    Losses = losses,
                    Draws = draws,
                    Decided = decided,
                    FightCount = fights.Length,
                    CharacterSampleCount = entries.Length,
                    ClassesPlayed = classesPlayed,
                    Characters = BuildTopFiveSumCharacters(entries, _ => 1.0, "count")
                };
            })
            .Where(row => row.Decided > 0 && row.FightCount >= minimumAppearanceFightCount)
            .OrderByDescending(row => row.Value)
            .ThenByDescending(row => row.Wins)
            .ThenByDescending(row => row.FightCount)
            .ThenBy(row => row.Account, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select((row, index) => new FightAnalysisTopFiveRowDto(
                Rank: index + 1,
                Account: row.Account,
                DisplayName: row.DisplayName,
                Value: row.Value,
                ValueDetail: $"{row.Wins}-{row.Losses}-{row.Draws} W-L-D",
                FightCount: row.FightCount,
                CharacterSampleCount: row.CharacterSampleCount,
                ClassesPlayed: row.ClassesPlayed,
                Characters: row.Characters))
            .ToArray();

        return new FightAnalysisTopFiveCategoryDto(
            Key: "win-loss-record",
            Label: "Victory Lap",
            Unit: "percent",
            Detail: "Squad win rate in selected fights where the account appeared. Draws and unknown outcomes are shown but not counted in the percentage.",
            Rows: rows);
    }

    private static FightAnalysisTopFiveCategoryDto BuildTopFiveBurstActorCategory(
        IReadOnlyList<FightArtifactSummaryDto> fights,
        IReadOnlyDictionary<string, int> accountFightCounts,
        int minimumAppearanceFightCount,
        string key,
        string label,
        string detail,
        Func<FightTopBurstIndexDto, IReadOnlyList<FightTopBurstActorIndexDto>> actorSelector)
    {
        var samples = fights
            .SelectMany(fight => (fight.FightIndex?.TopBursts ?? Array.Empty<FightTopBurstIndexDto>())
                .SelectMany(burst => actorSelector(burst)
                    .Where(actor => !string.IsNullOrWhiteSpace(actor.Account))
                    .Select(actor => new TopFiveActorSample(
                        Fight: fight,
                        Account: actor.Account!,
                        CharacterName: NormalizeAnalysisCharacterName(actor.Character) ?? "(unknown character)",
                        ClassLabel: BuildClassLabel(actor.Profession, actor.EliteSpec) ?? "Unknown class",
                        Icon: actor.Icon,
                        Value: 1.0))))
            .ToArray();

        var rows = samples
            .GroupBy(sample => sample.Account, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var entries = group.ToArray();
                var classesPlayed = entries
                    .Select(entry => entry.ClassLabel)
                    .Where(classLabel => !string.IsNullOrWhiteSpace(classLabel))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(classLabel => classLabel, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new
                {
                    Account = group.Key,
                    DisplayName = PickMostCommonNonEmpty(entries.Select(entry => entry.CharacterName)) ?? group.Key,
                    Value = entries.Sum(entry => entry.Value),
                    PlayerAppearanceFightCount = accountFightCounts.TryGetValue(group.Key, out int fightCount) ? fightCount : 0,
                    FightCount = entries.Select(entry => entry.Fight.FightId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    CharacterSampleCount = entries.Length,
                    ClassesPlayed = classesPlayed,
                    Characters = BuildTopFiveActorCharacters(entries)
                };
            })
            .Where(row => row.Value > 0.0 && row.PlayerAppearanceFightCount >= minimumAppearanceFightCount)
            .OrderByDescending(row => row.Value)
            .ThenByDescending(row => row.FightCount)
            .ThenBy(row => row.Account, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select((row, index) => new FightAnalysisTopFiveRowDto(
                Rank: index + 1,
                Account: row.Account,
                DisplayName: row.DisplayName,
                Value: row.Value,
                ValueDetail: null,
                FightCount: row.FightCount,
                CharacterSampleCount: row.CharacterSampleCount,
                ClassesPlayed: row.ClassesPlayed,
                Characters: row.Characters))
            .ToArray();

        return new FightAnalysisTopFiveCategoryDto(
            Key: key,
            Label: label,
            Unit: "count",
            Detail: detail,
            Rows: rows);
    }

    private static IReadOnlyList<FightAnalysisTopFiveCharacterDto> BuildTopFiveSumCharacters(
        IReadOnlyList<PlayerFightSample> entries,
        Func<PlayerFightSample, double> selector,
        string unit)
    {
        return entries
            .GroupBy(entry => new
            {
                CharacterName = NormalizeAnalysisCharacterName(entry.Player.Character) ?? "(unknown character)",
                ClassLabel = BuildClassLabel(entry.Player) ?? "Unknown class"
            })
            .Select(group =>
            {
                var samples = group.ToArray();
                return new FightAnalysisTopFiveCharacterDto(
                    CharacterName: group.Key.CharacterName,
                    ClassLabel: group.Key.ClassLabel,
                    Icon: PickMostCommonNonEmpty(samples.Select(sample => sample.Player.Icon)),
                    FightCount: samples.Select(sample => sample.Fight.FightId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    Value: RoundTopFiveValue(samples.Sum(selector), unit));
            })
            .Where(character => character.Value > 0.0)
            .OrderByDescending(character => character.Value)
            .ThenByDescending(character => character.FightCount)
            .ThenBy(character => character.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<FightAnalysisTopFiveCharacterDto> BuildTopFiveWeightedAverageCharacters(
        IReadOnlyList<PlayerFightSample> entries,
        Func<PlayerFightSample, double> selector,
        Func<PlayerFightSample, double> weightSelector,
        string unit)
    {
        return entries
            .GroupBy(entry => new
            {
                CharacterName = NormalizeAnalysisCharacterName(entry.Player.Character) ?? "(unknown character)",
                ClassLabel = BuildClassLabel(entry.Player) ?? "Unknown class"
            })
            .Select(group =>
            {
                var samples = group.ToArray();
                double weight = samples.Sum(weightSelector);
                double value = weight <= 0.0 ? 0.0 : RoundTopFiveValue(samples.Sum(sample => selector(sample) * weightSelector(sample)) / weight, unit);
                return new FightAnalysisTopFiveCharacterDto(
                    CharacterName: group.Key.CharacterName,
                    ClassLabel: group.Key.ClassLabel,
                    Icon: PickMostCommonNonEmpty(samples.Select(sample => sample.Player.Icon)),
                    FightCount: samples.Select(sample => sample.Fight.FightId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    Value: value);
            })
            .Where(character => character.Value > 0.0)
            .OrderByDescending(character => character.Value)
            .ThenByDescending(character => character.FightCount)
            .ThenBy(character => character.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<FightAnalysisTopFiveCharacterDto> BuildTopFiveActorCharacters(IReadOnlyList<TopFiveActorSample> entries)
    {
        return entries
            .GroupBy(entry => new
            {
                entry.CharacterName,
                entry.ClassLabel
            })
            .Select(group =>
            {
                var samples = group.ToArray();
                return new FightAnalysisTopFiveCharacterDto(
                    CharacterName: group.Key.CharacterName,
                    ClassLabel: group.Key.ClassLabel,
                    Icon: PickMostCommonNonEmpty(samples.Select(sample => sample.Icon)),
                    FightCount: samples.Select(sample => sample.Fight.FightId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    Value: Math.Round(samples.Sum(sample => sample.Value), 0));
            })
            .Where(character => character.Value > 0.0)
            .OrderByDescending(character => character.Value)
            .ThenByDescending(character => character.FightCount)
            .ThenBy(character => character.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static double GetProvidedBoonGeneration(FightPlayerIndexDto player, string boonName)
    {
        return (player.ProvidedBoons ?? Array.Empty<FightPlayerProvidedBoonIndexDto>())
            .Where(boon => string.Equals(boon.Name, boonName, StringComparison.OrdinalIgnoreCase))
            .Sum(boon => boon.Generation);
    }

    private static bool? GetSquadWinResult(FightArtifactSummaryDto fight)
    {
        FightOutcomeDto? outcome = fight.FightIndex?.Outcome;
        string? outcomeCode = outcome?.OutcomeCode;
        if (string.Equals(outcomeCode, "squad", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(outcomeCode, "enemy", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? winnerSideId = outcome?.WinnerSideId;
        if (string.Equals(winnerSideId, "squad", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(winnerSideId, "enemy", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    private static int BuildTopFiveMinimumAppearanceFightCount(int filteredFightCount)
    {
        return filteredFightCount <= 0
            ? 1
            : Math.Max(1, (int)Math.Ceiling(filteredFightCount * 0.10));
    }

    private static IReadOnlyDictionary<string, int> BuildTopFiveAccountFightCounts(IReadOnlyList<PlayerFightSample> samples)
    {
        return samples
            .GroupBy(sample => sample.Player.Account!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(sample => sample.Fight.FightId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static double RoundTopFiveValue(double value, string unit)
    {
        return string.Equals(unit, "percent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(unit, "boon", StringComparison.OrdinalIgnoreCase)
            ? Math.Round(value, 1)
            : Math.Round(value, 0);
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
                var characterImpactTrends = BuildCharacterImpactTrends(orderedPlayers, includeAccountForDuplicateLabels: false);
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
                var fightImpact = ComputeAverageFightImpact(mergedFightEntries);
                var fightImpactLanes = BuildFightImpactLanes(mergedFightEntries, 5);
                var averageDamage = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => (double)entry.Damage, entry => entry.Fight), 0);
                var averageDowns = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Downs, entry => entry.Fight), 1);
                var averageKills = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Kills, entry => entry.Fight), 1);
                var averageStrips = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Strips, entry => entry.Fight), 1);
                var averageCorrupts = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Corrupts, entry => entry.Fight), 1);
                var stripCorruptPercent = ComputePercent(averageCorrupts, averageStrips);
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
                    AverageCorruptsPerFight: averageCorrupts,
                    StripCorruptPercent: stripCorruptPercent,
                    AverageOutgoingCleansesPerFight: averageCleanses,
                    AverageHealingPerFight: averageHealing,
                    AverageBarrierPerFight: averageBarrier,
                    AverageResurrectsPerFight: averageResurrects,
                    AverageDeathsPerFight: averageDeaths,
                    AverageRecoveriesPerFight: averageRecoveries,
                    AverageInPositionRate: averageInPositionRate,
                    ContributionSummary: string.IsNullOrWhiteSpace(contributionSummary) ? null : contributionSummary,
                    AverageFightImpactScore: fightImpact.Average,
                    FightImpactSampleCount: fightImpact.SampleCount,
                    FightImpactLanes: fightImpactLanes,
                    CharacterNames: characters
                        .Select(character => character.CharacterName)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    Characters: characters,
                    CharacterImpactTrends: characterImpactTrends);
            })
            .OrderByDescending(player => player.ImpactScore)
            .ThenByDescending(player => player.AverageWeightedLaneScore)
            .ThenByDescending(player => player.FightCount)
            .ThenBy(player => player.Account, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<FightAnalysisPlayerSummaryRowDto> BuildPlayerSummaryRows(
        IReadOnlyList<FightAnalysisPlayerRowDto> players)
        => players
            .Select(BuildPlayerSummaryRow)
            .ToArray();

    private static FightAnalysisPlayerSummaryRowDto BuildPlayerSummaryRow(FightAnalysisPlayerRowDto player)
    {
        var laneSummaries = player.Characters
            .Where(character => Math.Max(character.TotalFightCountAll, character.FightCount) >= 10)
            .SelectMany(character => character.LaneContributions.Select(lane => new FightAnalysisPlayerLaneSummaryDto(
                LaneKey: lane.LaneKey,
                LaneLabel: lane.LaneLabel,
                CharacterName: character.CharacterName,
                ClassLabel: character.ClassLabel,
                CharacterFightCount: character.FightCount,
                CharacterTotalFightCountAll: character.TotalFightCountAll,
                CharacterWinRatePercent: character.WinRatePercent,
                AverageStrengthPercent: lane.AverageStrengthPercent,
                AverageSharePercent: lane.AverageSharePercent,
                OverallStrengthPercent: lane.OverallStrengthPercent,
                OverallSharePercent: lane.OverallSharePercent,
                AppearanceRatePercent: lane.AppearanceRatePercent,
                Samples: lane.Samples,
                TotalSamplesAll: lane.TotalSamplesAll,
                AverageCorruptsPerAppearance: GetAnalysisLaneMetricAverage(lane.Metrics, StripCorruptsMetricKey),
                StripCorruptPercent: ComputePercent(
                    GetAnalysisLaneMetricTotal(lane.Metrics, StripCorruptsMetricKey),
                    GetAnalysisLaneMetricTotal(lane.Metrics, StripTotalMetricKey)))))
            .OrderByDescending(lane => lane.OverallStrengthPercent)
            .ThenByDescending(lane => lane.TotalSamplesAll)
            .ThenBy(lane => lane.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(lane => lane.LaneLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new FightAnalysisPlayerSummaryRowDto(
            Account: player.Account,
            DisplayName: player.DisplayName,
            FightCount: player.FightCount,
            TotalFightCountAll: player.TotalFightCountAll,
            WinCount: player.WinCount,
            LossCount: player.LossCount,
            DrawCount: player.DrawCount,
            WinRatePercent: player.WinRatePercent,
            ClassesPlayed: player.ClassesPlayed,
            PrimaryClassLabel: player.PrimaryClassLabel,
            PrimaryLaneLabel: player.PrimaryLaneLabel,
            ImpactScore: player.ImpactScore,
            AveragePrimaryLaneScore: player.AveragePrimaryLaneScore,
            AverageWeightedLaneScore: player.AverageWeightedLaneScore,
            AverageDamagePerFight: player.AverageDamagePerFight,
            AverageDownsPerFight: player.AverageDownsPerFight,
            AverageKillsPerFight: player.AverageKillsPerFight,
            AverageStripsPerFight: player.AverageStripsPerFight,
            AverageCorruptsPerFight: player.AverageCorruptsPerFight,
            StripCorruptPercent: player.StripCorruptPercent,
            AverageOutgoingCleansesPerFight: player.AverageOutgoingCleansesPerFight,
            AverageHealingPerFight: player.AverageHealingPerFight,
            AverageBarrierPerFight: player.AverageBarrierPerFight,
            AverageResurrectsPerFight: player.AverageResurrectsPerFight,
            AverageDeathsPerFight: player.AverageDeathsPerFight,
            AverageRecoveriesPerFight: player.AverageRecoveriesPerFight,
            AverageInPositionRate: player.AverageInPositionRate,
            ContributionSummary: player.ContributionSummary,
            AverageFightImpactScore: player.AverageFightImpactScore,
            FightImpactSampleCount: player.FightImpactSampleCount,
            CharacterNames: player.CharacterNames,
            LaneSummaries: laneSummaries);
    }

    private static IReadOnlyList<FightAnalysisClassRowDto> BuildTopClasses(
        IReadOnlyList<FightArtifactSummaryDto> fights,
        IReadOnlyDictionary<string, int> totalClassSampleCounts,
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
                int totalSampleCountAll = totalClassSampleCounts.TryGetValue(group.Key, out int totalClassSamples)
                    ? totalClassSamples
                    : sampleCount;
                var laneContributions = BuildAggregateLaneContributions(mergedFightEntries, sampleCount, 6);
                var fightCoverageSamples = BuildClassFightCoverageSamples(fights, group.Key);
                var fightCoverage = ComputeAverageFightCoverage(fightCoverageSamples);
                var fightCoverageLanes = BuildClassFightCoverageLanes(fightCoverageSamples, 5);
                var laneScores = mergedFightEntries
                    .Select(entry => new
                    {
                        Fight = entry.Fight,
                        Score = ComputePlayerLaneScores(entry.Lanes)
                    })
                    .Where(score => score.Score.Primary > 0.0 || score.Score.Weighted > 0.0)
                    .ToArray();
                var classFightEntries = mergedFightEntries
                    .GroupBy(entry => entry.Fight.FightId, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToArray();
                var winCount = classFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "squad", StringComparison.OrdinalIgnoreCase));
                var lossCount = classFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "enemy", StringComparison.OrdinalIgnoreCase));
                var drawCount = classFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "draw", StringComparison.OrdinalIgnoreCase));
                var winRatePercent = classFightEntries.Length == 0 ? 0.0 : Math.Round(winCount * 100.0 / classFightEntries.Length, 1);
                var averageDamage = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => (double)entry.Damage, entry => entry.Fight), 0);
                var averageDowns = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Downs, entry => entry.Fight), 1);
                var averageKills = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Kills, entry => entry.Fight), 1);
                var averageStrips = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Strips, entry => entry.Fight), 1);
                var averageCorrupts = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Corrupts, entry => entry.Fight), 1);
                var stripCorruptPercent = ComputePercent(averageCorrupts, averageStrips);
                var averageCleanses = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.OutgoingCleanses, entry => entry.Fight), 1);
                var averageHealing = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => (double)entry.Healing, entry => entry.Fight), 0);
                var averageBarrier = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => (double)entry.Barrier, entry => entry.Fight), 0);
                var averageResurrects = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Resurrects, entry => entry.Fight), 1);
                var averageDeaths = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Deaths, entry => entry.Fight), 1);
                var averageRecoveries = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Recoveries, entry => entry.Fight), 1);
                double? averageInPositionRate = CleanupAdjustedAverageOrNull(mergedFightEntries, entry => entry.InPositionRate, entry => entry.Fight);
                var classPlayers = BuildClassPlayerSummaries(group.Key, entries, totalClassPlayerFightCounts);
                var characterImpactTrends = BuildCharacterImpactTrends(entries, includeAccountForDuplicateLabels: true);
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
                    TotalSampleCountAll: totalSampleCountAll,
                    DistinctAccounts: classPlayers.Count,
                    WinCount: winCount,
                    LossCount: lossCount,
                    DrawCount: drawCount,
                    WinRatePercent: winRatePercent,
                    ContributionScore: contributionScore,
                    AverageStripsPerFight: averageStrips,
                    AverageCorruptsPerFight: averageCorrupts,
                    StripCorruptPercent: stripCorruptPercent,
                    TopPlayerDisplayName: topPlayerDisplayName,
                    AveragePrimaryLaneScore: laneScores.Length == 0
                        ? 0.0
                        : Math.Round(CleanupAdjustedAverage(laneScores, score => score.Score.Primary, score => score.Fight), 1),
                    AverageWeightedLaneScore: laneScores.Length == 0
                        ? 0.0
                        : Math.Round(CleanupAdjustedAverage(laneScores, score => score.Score.Weighted, score => score.Fight), 1),
                    AverageFightCoverageScore: fightCoverage.Average,
                    FightCoverageSampleCount: fightCoverage.SampleCount,
                    FightCoverageLanes: fightCoverageLanes,
                    LaneContributions: laneContributions,
                    PatchImpacts: GetPatchImpactsForClass(group.Key, patchImpacts),
                    Players: classPlayers,
                    CharacterImpactTrends: characterImpactTrends);
            })
            .Where(row => row.TotalSampleCountAll >= MinimumClassSampleCount)
            .OrderByDescending(row => row.ContributionScore)
            .ThenByDescending(row => row.AverageWeightedLaneScore)
            .ThenByDescending(row => row.SampleCount)
            .ThenBy(row => row.ClassLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<FightAnalysisEnemyClassRowDto> BuildTopEnemyClasses(IReadOnlyList<FightArtifactSummaryDto> fights)
    {
        var aggregates = new Dictionary<string, EnemyClassPerformanceAggregate>(StringComparer.OrdinalIgnoreCase);

        foreach (var fight in fights)
        {
            var fightIndex = fight.FightIndex;
            if (fightIndex is null)
            {
                continue;
            }

            foreach (var classRow in fightIndex.EnemySide?.Classes ?? Array.Empty<FightSideClassIndexDto>())
            {
                if (string.IsNullOrWhiteSpace(classRow.ClassLabel) || classRow.Count <= 0)
                {
                    continue;
                }

                var aggregate = GetEnemyClassAggregate(aggregates, classRow.ClassLabel);
                aggregate.Icon ??= classRow.Icon;
                aggregate.TotalCount += classRow.Count;
                aggregate.FightIds.Add(fight.FightId);
            }

            foreach (var enemyPlayer in fightIndex.EnemyPlayers ?? Array.Empty<FightEnemyPlayerIndexDto>())
            {
                var classLabel = BuildClassLabel(enemyPlayer.Profession, enemyPlayer.EliteSpec);
                if (string.IsNullOrWhiteSpace(classLabel))
                {
                    continue;
                }

                var aggregate = GetEnemyClassAggregate(aggregates, classLabel);
                aggregate.Icon ??= enemyPlayer.Icon;
                aggregate.FightIds.Add(fight.FightId);
                aggregate.PerformanceSampleCount++;
                aggregate.TotalDps += enemyPlayer.Dps;
                aggregate.BestDps = Math.Max(aggregate.BestDps, enemyPlayer.Dps);
                aggregate.TotalStripsPerMinute += enemyPlayer.StripsPerMinute;
                aggregate.BestStripsPerMinute = Math.Max(aggregate.BestStripsPerMinute, enemyPlayer.StripsPerMinute);
            }

            foreach (var burst in fightIndex.EnemyTopBursts ?? Array.Empty<FightTopBurstIndexDto>())
            {
                foreach (var classLabel in GetDistinctBurstClassLabels(burst.TopPressureActors))
                {
                    var aggregate = GetEnemyClassAggregate(aggregates, classLabel);
                    aggregate.FightIds.Add(fight.FightId);
                    aggregate.DamageBurstTopCount++;
                }

                foreach (var classLabel in GetDistinctBurstClassLabels(burst.TopStripActors))
                {
                    var aggregate = GetEnemyClassAggregate(aggregates, classLabel);
                    aggregate.FightIds.Add(fight.FightId);
                    aggregate.StripBurstTopCount++;
                }
            }
        }

        var rows = aggregates.Values
            .Select(aggregate => aggregate.ToRow())
            .ToArray();
        return ApplyEnemyThreatScores(rows)
            .OrderByDescending(row => row.TotalCount)
            .ThenByDescending(row => row.FightCount)
            .ThenBy(row => row.ClassLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static EnemyClassPerformanceAggregate GetEnemyClassAggregate(
        Dictionary<string, EnemyClassPerformanceAggregate> aggregates,
        string classLabel)
    {
        var normalized = classLabel.Trim();
        if (!aggregates.TryGetValue(normalized, out var aggregate))
        {
            aggregate = new EnemyClassPerformanceAggregate(normalized);
            aggregates[normalized] = aggregate;
        }

        return aggregate;
    }

    private static IReadOnlyList<string> GetDistinctBurstClassLabels(IReadOnlyList<FightTopBurstActorIndexDto>? actors)
    {
        return (actors ?? Array.Empty<FightTopBurstActorIndexDto>())
            .Select(actor => BuildClassLabel(actor.Profession, actor.EliteSpec))
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<FightAnalysisEnemyClassRowDto> ApplyEnemyThreatScores(IReadOnlyList<FightAnalysisEnemyClassRowDto> rows)
    {
        if (rows.Count == 0)
        {
            return rows;
        }

        double maxAverageDps = rows.Max(row => row.AverageDps ?? 0.0);
        double maxBestDps = rows.Max(row => row.BestDps ?? 0.0);
        double maxAverageStrips = rows.Max(row => row.AverageStripsPerMinute ?? 0.0);
        double maxBestStrips = rows.Max(row => row.BestStripsPerMinute ?? 0.0);
        double maxDamageBurstRate = rows.Max(GetDamageBurstRate);
        double maxStripBurstRate = rows.Max(GetStripBurstRate);

        return rows
            .Select(row =>
            {
                if (row.PerformanceSampleCount <= 0 &&
                    row.DamageBurstTopCount <= 0 &&
                    row.StripBurstTopCount <= 0)
                {
                    return row with { ThreatScore = null };
                }

                double score =
                    0.35 * NormalizeEnemyThreatMetric(row.AverageDps, maxAverageDps) +
                    0.20 * NormalizeEnemyThreatMetric(row.BestDps, maxBestDps) +
                    0.20 * NormalizeEnemyThreatMetric(row.AverageStripsPerMinute, maxAverageStrips) +
                    0.10 * NormalizeEnemyThreatMetric(row.BestStripsPerMinute, maxBestStrips) +
                    0.10 * NormalizeEnemyThreatMetric(GetDamageBurstRate(row), maxDamageBurstRate) +
                    0.05 * NormalizeEnemyThreatMetric(GetStripBurstRate(row), maxStripBurstRate);

                return row with { ThreatScore = Math.Round(score * 100.0, 1) };
            })
            .ToArray();
    }

    private static double GetDamageBurstRate(FightAnalysisEnemyClassRowDto row)
    {
        return row.TotalCount <= 0 ? 0.0 : row.DamageBurstTopCount / (double)row.TotalCount;
    }

    private static double GetStripBurstRate(FightAnalysisEnemyClassRowDto row)
    {
        return row.TotalCount <= 0 ? 0.0 : row.StripBurstTopCount / (double)row.TotalCount;
    }

    private static double NormalizeEnemyThreatMetric(double? value, double maxValue)
    {
        if (!value.HasValue || value.Value <= 0 || maxValue <= 0)
        {
            return 0.0;
        }

        return Math.Clamp(value.Value / maxValue, 0.0, 1.0);
    }

    private static double NormalizeEnemyThreatMetric(double value, double maxValue)
    {
        if (value <= 0 || maxValue <= 0)
        {
            return 0.0;
        }

        return Math.Clamp(value / maxValue, 0.0, 1.0);
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
                bool usePreventionValueRanking = IsPreventionLane(group.Key)
                    && entries.Any(entry => GetLaneMetricValue(entry.Lane.Metrics, PreventionValueMetricKey) > 0.0);
                var topClassLabel = entries
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.ClassLabel))
                    .GroupBy(entry => entry.ClassLabel!, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(classGroup => CleanupAdjustedAverage(
                        classGroup.ToArray(),
                        entry => usePreventionValueRanking
                            ? GetLaneMetricValue(entry.Lane.Metrics, PreventionValueMetricKey)
                            : entry.Lane.StrengthPercent,
                        entry => entry.Fight))
                    .ThenByDescending(classGroup => classGroup.Count())
                    .Select(classGroup => classGroup.Key)
                    .FirstOrDefault();
                var topPlayerDisplayName = entries
                    .GroupBy(entry => entry.Account, StringComparer.OrdinalIgnoreCase)
                    .Select(playerGroup => new
                    {
                        Display = playerGroup.Select(entry => entry.PlayerDisplayName).FirstOrDefault(),
                        AverageStrength = CleanupAdjustedAverage(
                            playerGroup.ToArray(),
                            entry => usePreventionValueRanking
                                ? GetLaneMetricValue(entry.Lane.Metrics, PreventionValueMetricKey)
                                : entry.Lane.StrengthPercent,
                            entry => entry.Fight),
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
                double averageCorrupts = Math.Round(CleanupAdjustedAverage(
                    entries,
                    entry => GetLaneMetricValue(entry.Lane.Metrics, StripCorruptsMetricKey),
                    entry => entry.Fight), 1);
                double averageStrips = Math.Round(CleanupAdjustedAverage(
                    entries,
                    entry => GetLaneMetricValue(entry.Lane.Metrics, StripTotalMetricKey),
                    entry => entry.Fight), 1);
                return new FightAnalysisLaneRowDto(
                    LaneKey: group.Key,
                    LaneLabel: label,
                    Samples: entries.Length,
                    DistinctAccounts: entries.Select(entry => entry.Account).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    DistinctClasses: entries.Select(entry => entry.ClassLabel).Where(label => !string.IsNullOrWhiteSpace(label)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    AverageStrengthPercent: Math.Round(CleanupAdjustedAverage(entries, entry => entry.Lane.StrengthPercent, entry => entry.Fight), 1),
                    AverageSharePercent: Math.Round(CleanupAdjustedAverage(entries, entry => entry.Lane.SharePercent, entry => entry.Fight), 1),
                    AppearanceRatePercent: totalFightCount == 0 ? 0.0 : Math.Round(distinctFightCount * 100.0 / totalFightCount, 1),
                    AverageCorruptsPerAppearance: averageCorrupts,
                    StripCorruptPercent: ComputePercent(averageCorrupts, averageStrips),
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

    private static IReadOnlyList<FightAnalysisBoonTrendDto> BuildBoonTrends(IReadOnlyList<FightArtifactSummaryDto> fights)
    {
        var nightGroups = fights
            .Select(fight => new
            {
                Fight = fight,
                Date = GetFightLocalDate(fight)
            })
            .Where(entry => entry.Date.HasValue)
            .GroupBy(entry => entry.Date!.Value)
            .OrderBy(group => group.Key)
            .Select(group => new
            {
                Date = group.Key,
                Fights = group.Select(entry => entry.Fight).ToArray()
            })
            .ToArray();

        var providerSamples = BuildMergedBoonProviderSamples(fights);
        var providerSamplesByDate = providerSamples
            .Select(sample => new
            {
                Sample = sample,
                Date = GetFightLocalDate(sample.Fight)
            })
            .Where(entry => entry.Date.HasValue)
            .GroupBy(entry => entry.Date!.Value)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MergedPlayerFightProvidedBoonSample>)group.Select(entry => entry.Sample).ToArray());
        var providerSamplesByFightId = providerSamples
            .GroupBy(sample => sample.Fight.FightId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MergedPlayerFightProvidedBoonSample>)group.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var buckets = nightGroups.Length == 1
            ? nightGroups[0].Fights
                .OrderBy(GetFightSortValue)
                .Select(fight =>
                {
                    providerSamplesByFightId.TryGetValue(fight.FightId, out var fightProviderSamples);
                    fightProviderSamples ??= Array.Empty<MergedPlayerFightProvidedBoonSample>();
                    return new BoonTrendBucket(
                        Key: BuildBoonTrendFightKey(fight),
                        Label: BuildBoonTrendFightLabel(fight),
                        BucketType: "fight",
                        Fights: new[] { fight },
                        ProviderSamples: fightProviderSamples);
                })
                .ToArray()
            : nightGroups
                .Select(night =>
                {
                    providerSamplesByDate.TryGetValue(night.Date, out var nightProviderSamples);
                    nightProviderSamples ??= Array.Empty<MergedPlayerFightProvidedBoonSample>();
                    return new BoonTrendBucket(
                        Key: night.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Label: night.Date.ToString("MMM d", CultureInfo.InvariantCulture),
                        BucketType: "night",
                        Fights: night.Fights,
                        ProviderSamples: nightProviderSamples);
                })
                .ToArray();

        return BoonTrendDefinitions
            .Select(definition => new FightAnalysisBoonTrendDto(
                Id: definition.Id,
                Name: definition.Name,
                Icon: FindBoonTrendIcon(definition, fights, providerSamples),
                StackBased: definition.StackBased,
                Points: buckets
                    .Select(bucket => BuildBoonTrendPoint(definition, bucket))
                    .Where(point => point is not null)
                    .Cast<FightAnalysisBoonTrendPointDto>()
                    .ToArray()))
            .ToArray();
    }

    private static string BuildBoonTrendFightKey(FightArtifactSummaryDto fight)
    {
        var timestamp = GetFightDateTimeOffset(fight);
        var timestampKey = timestamp?.UtcDateTime.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) ?? "unknown-time";
        return $"{timestampKey}-{fight.FightId}";
    }

    private static string BuildBoonTrendFightLabel(FightArtifactSummaryDto fight)
    {
        var timestamp = GetFightDateTimeOffset(fight);
        return timestamp?.ToLocalTime().ToString("h:mm tt", CultureInfo.InvariantCulture)
            ?? fight.FightIndex?.FightName
            ?? fight.SourceFileName
            ?? fight.FightId;
    }

    private static FightAnalysisBoonTrendPointDto? BuildBoonTrendPoint(
        FightAnalysisTrackedBoon definition,
        BoonTrendBucket bucket)
    {
        var fightsWithBoonData = bucket.Fights
            .Where(fight => (fight.FightIndex?.ThreatBoons?.Count ?? 0) > 0)
            .ToArray();
        if (fightsWithBoonData.Length == 0)
        {
            return null;
        }

        var boonValues = fightsWithBoonData
            .Select(fight => (fight.FightIndex?.ThreatBoons ?? Array.Empty<FightThreatBoonIndexDto>())
                .FirstOrDefault(boon => boon.Id == definition.Id))
            .ToArray();
        double averageCoverage = Math.Round(boonValues.Average(boon => boon?.Coverage ?? 0.0), 1);
        double? averageStacks = definition.StackBased
            ? Math.Round(boonValues.Average(boon => boon?.AverageStacks ?? 0.0), 1)
            : null;

        return new FightAnalysisBoonTrendPointDto(
            DateKey: bucket.Key,
            DateLabel: bucket.Label,
            BucketType: bucket.BucketType,
            FightCount: fightsWithBoonData.Length,
            AverageCoverage: averageCoverage,
            AverageStacks: averageStacks,
            TeamScore: CalculateTeamScoreAverage(bucket.Fights),
            TopProviders: BuildBoonTrendProviders(definition, bucket.ProviderSamples));
    }

    private static IReadOnlyList<FightAnalysisBoonTrendProviderDto> BuildBoonTrendProviders(
        FightAnalysisTrackedBoon definition,
        IReadOnlyList<MergedPlayerFightProvidedBoonSample> providerSamples)
    {
        return providerSamples
            .SelectMany(sample => sample.ProvidedBoons
                .Where(boon => boon.Id == definition.Id)
                .Select(boon => new
                {
                    sample.ClassLabel,
                    Boon = boon
                }))
            .GroupBy(entry => entry.ClassLabel, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var entries = group.ToArray();
                var first = entries[0];
                int sampleCount = entries.Length;
                int providerAppearanceCount = entries.Count(entry =>
                    entry.Boon.Generation > 0.0
                    || entry.Boon.GenerationPresence > 0.0
                    || entry.Boon.Overstack > 0.0);
                double averageGeneration = sampleCount == 0 ? 0.0 : Math.Round(entries.Average(entry => entry.Boon.Generation), 1);
                double? averageGenerationPresence = definition.StackBased
                    ? Math.Round(entries.Average(entry => entry.Boon.GenerationPresence), 1)
                    : null;
                double averageOverstack = sampleCount == 0 ? 0.0 : Math.Round(entries.Average(entry => entry.Boon.Overstack), 1);

                return new FightAnalysisBoonTrendProviderDto(
                    Label: string.IsNullOrWhiteSpace(first.ClassLabel) ? "Unknown class" : first.ClassLabel,
                    Account: null,
                    ClassLabel: first.ClassLabel,
                    SampleCount: sampleCount,
                    ProviderAppearanceCount: providerAppearanceCount,
                    AverageGeneration: averageGeneration,
                    AverageGenerationPresence: averageGenerationPresence,
                    AverageOverstack: averageOverstack,
                    ProviderScore: averageGeneration);
            })
            .Where(provider => provider.ProviderAppearanceCount > 0 || provider.AverageGeneration > 0.0 || provider.AverageGenerationPresence.GetValueOrDefault() > 0.0)
            .OrderByDescending(provider => provider.ProviderScore)
            .ThenByDescending(provider => provider.AverageGenerationPresence ?? 0.0)
            .ThenByDescending(provider => provider.ProviderAppearanceCount)
            .ThenBy(provider => provider.Label, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();
    }

    private static string? FindBoonTrendIcon(
        FightAnalysisTrackedBoon definition,
        IReadOnlyList<FightArtifactSummaryDto> fights,
        IReadOnlyList<MergedPlayerFightProvidedBoonSample> providerSamples)
    {
        var threatIcon = fights
            .SelectMany(fight => fight.FightIndex?.ThreatBoons ?? Array.Empty<FightThreatBoonIndexDto>())
            .Where(boon => boon.Id == definition.Id)
            .Select(boon => boon.Icon)
            .FirstOrDefault(icon => !string.IsNullOrWhiteSpace(icon));
        if (!string.IsNullOrWhiteSpace(threatIcon))
        {
            return threatIcon;
        }

        return providerSamples
            .SelectMany(sample => sample.ProvidedBoons)
            .Where(boon => boon.Id == definition.Id)
            .Select(boon => boon.Icon)
            .FirstOrDefault(icon => !string.IsNullOrWhiteSpace(icon));
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
                var averageCorrupts = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Corrupts, entry => entry.Fight), 1);
                var stripCorruptPercent = ComputePercent(averageCorrupts, averageStrips);
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
                var fightImpact = ComputeAverageFightImpact(mergedFightEntries);
                var fightImpactLanes = BuildFightImpactLanes(mergedFightEntries, 5);
                var contextFits = BuildCharacterContextFits(mergedFightEntries);
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
                    AverageStripsPerFight: averageStrips,
                    AverageCorruptsPerFight: averageCorrupts,
                    StripCorruptPercent: stripCorruptPercent,
                    AverageActivePresencePercent: Math.Round(ComputeAveragePresencePercent(mergedFightEntries, entry => entry.ActiveSeconds), 1),
                    AverageEngagedPresencePercent: Math.Round(ComputeAveragePresencePercent(mergedFightEntries, entry => entry.CombatSeconds), 1),
                    PackageInputs: packageInputs,
                    EvidenceLines: evidenceLines,
                    AverageFightImpactScore: fightImpact.Average,
                    FightImpactSampleCount: fightImpact.SampleCount,
                    FightImpactLanes: fightImpactLanes,
                    ContextFits: contextFits,
                    LaneContributions: laneContributions);
            })
            .OrderByDescending(character => character.FightCount)
            .ThenByDescending(character => character.ImpactScore)
            .ThenBy(character => character.ClassLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(character => character.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<FightAnalysisCharacterImpactTrendDto> BuildCharacterImpactTrends(
        IReadOnlyList<PlayerFightSample> entries,
        bool includeAccountForDuplicateLabels)
    {
        var trendGroups = entries
            .Select(entry => new
            {
                Entry = entry,
                Account = BuildPlayerIdentityKey(entry.Player),
                CharacterName = NormalizeAnalysisCharacterName(entry.Player.Character) ?? "(unknown character)",
                ClassLabel = BuildClassLabel(entry.Player) ?? "Unknown class"
            })
            .GroupBy(
                entry => BuildCharacterImpactTrendKey(entry.Account, entry.CharacterName, entry.ClassLabel),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var characterEntries = group.Select(entry => entry.Entry).ToArray();
                var mergedFightEntries = MergePlayerFightSamplesByFight(characterEntries, first.CharacterName);
                return new
                {
                    first.Account,
                    first.CharacterName,
                    first.ClassLabel,
                    MergedFightEntries = mergedFightEntries,
                    Points = BuildCharacterImpactTrendPoints(mergedFightEntries)
                };
            })
            .Where(trend => trend.Points.Count > 0)
            .ToArray();

        var duplicateCharacterNames = trendGroups
            .GroupBy(trend => trend.CharacterName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return trendGroups
            .Select(trend =>
            {
                var label = trend.CharacterName;
                if (duplicateCharacterNames.Contains(trend.CharacterName))
                {
                    var discriminator = includeAccountForDuplicateLabels && !string.IsNullOrWhiteSpace(trend.Account)
                        ? trend.Account
                        : trend.ClassLabel;
                    label = $"{trend.CharacterName} ({discriminator})";
                }

                return new FightAnalysisCharacterImpactTrendDto(
                    Key: BuildCharacterImpactTrendKey(trend.Account, trend.CharacterName, trend.ClassLabel),
                    CharacterName: trend.CharacterName,
                    ClassLabel: trend.ClassLabel,
                    Account: trend.Account,
                    Label: label,
                    FightCount: trend.MergedFightEntries.Count,
                    Points: trend.Points);
            })
            .OrderByDescending(trend => trend.FightCount)
            .ThenByDescending(trend => trend.Points.Average(point => point.ImpactScore))
            .ThenBy(trend => trend.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<FightAnalysisCharacterImpactTrendPointDto> BuildCharacterImpactTrendPoints(
        IReadOnlyList<MergedPlayerFightSample> mergedFightEntries)
    {
        return mergedFightEntries
            .Select(entry => new
            {
                Entry = entry,
                Date = GetFightLocalDate(entry.Fight)
            })
            .Where(entry => entry.Date.HasValue)
            .GroupBy(entry => entry.Date!.Value)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var nightEntries = group.Select(entry => entry.Entry).ToArray();
                return new FightAnalysisCharacterImpactTrendPointDto(
                    DateKey: group.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    DateLabel: group.Key.ToString("MMM d", CultureInfo.InvariantCulture),
                    FightCount: nightEntries.Length,
                    ImpactScore: ComputeImpactScoreForMergedEntries(nightEntries));
            })
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
                var averageCorrupts = sampleCount == 0 ? 0.0 : Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Corrupts, entry => entry.Fight), 1);
                var stripCorruptPercent = ComputePercent(averageCorrupts, averageStrips);
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
                var fightImpact = ComputeAverageFightImpact(mergedFightEntries);
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
                    AverageFightImpactScore: fightImpact.Average,
                    FightImpactSampleCount: fightImpact.SampleCount,
                    PrimaryLaneLabel: primaryLane,
                    AveragePrimaryLaneScore: laneScores.Length == 0
                        ? 0.0
                        : Math.Round(CleanupAdjustedAverage(laneScores, score => score.Score.Primary, score => score.Fight), 1),
                    AverageWeightedLaneScore: laneScores.Length == 0
                        ? 0.0
                        : Math.Round(CleanupAdjustedAverage(laneScores, score => score.Score.Weighted, score => score.Fight), 1),
                    AverageStripsPerFight: averageStrips,
                    AverageCorruptsPerFight: averageCorrupts,
                    StripCorruptPercent: stripCorruptPercent);
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

    private static (double Average, int SampleCount) ComputeAverageFightImpact(IReadOnlyList<MergedPlayerFightSample> mergedFightEntries)
    {
        var samples = mergedFightEntries
            .Where(entry => entry.FightImpactScore > 0.0)
            .ToArray();
        return samples.Length == 0
            ? (0.0, 0)
            : (Math.Round(CleanupAdjustedAverage(samples, entry => entry.FightImpactScore, entry => entry.Fight), 1), samples.Length);
    }

    private static IReadOnlyList<FightAnalysisCharacterContextFitDto> BuildCharacterContextFits(IReadOnlyList<MergedPlayerFightSample> mergedFightEntries)
    {
        var baseline = ComputeAverageFightImpact(mergedFightEntries);
        if (baseline.SampleCount < MinimumCharacterContextBaselineSampleCount || baseline.Average <= 0.0)
        {
            return Array.Empty<FightAnalysisCharacterContextFitDto>();
        }

        var fits = new List<FightAnalysisCharacterContextFitDto>();

        foreach (var patchGroup in mergedFightEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Fight.PatchEra?.Id))
            .GroupBy(entry => entry.Fight.PatchEra!.Id, StringComparer.OrdinalIgnoreCase))
        {
            var patchEra = patchGroup.Select(entry => entry.Fight.PatchEra).FirstOrDefault(era => era is not null);
            string label = patchEra?.IsCurrent == true
                ? "Current patch"
                : patchEra?.Label ?? patchGroup.Key;
            AddCharacterContextFit(
                fits,
                $"patch:{patchGroup.Key}",
                label,
                "Patch",
                patchGroup.ToArray(),
                baseline);
        }

        foreach (var attributeGroup in mergedFightEntries
            .SelectMany(entry => (entry.Fight.Attributes ?? Array.Empty<FightAttributeDto>())
                .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Key)
                    && !string.Equals(attribute.Group, "Data Quality", StringComparison.OrdinalIgnoreCase))
                .Select(attribute => new
                {
                    Entry = entry,
                    Attribute = attribute
                }))
            .GroupBy(item => item.Attribute.Key, StringComparer.OrdinalIgnoreCase))
        {
            var attribute = attributeGroup.Select(item => item.Attribute).First();
            AddCharacterContextFit(
                fits,
                $"attribute:{attributeGroup.Key}",
                attribute.Label,
                attribute.Group,
                attributeGroup.Select(item => item.Entry).ToArray(),
                baseline);
        }

        foreach (var commanderGroup in mergedFightEntries
            .Select(entry => new
            {
                Entry = entry,
                Commander = entry.Fight.FightIndex?.CommanderDisplayNames?
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Commander))
            .GroupBy(item => item.Commander!, StringComparer.OrdinalIgnoreCase))
        {
            AddCharacterContextFit(
                fits,
                $"commander:{NormalizeLookupKey(commanderGroup.Key)}",
                commanderGroup.Key,
                "Commander",
                commanderGroup.Select(item => item.Entry).ToArray(),
                baseline);
        }

        var positiveFits = fits
            .Where(fit => fit.Delta > 0.0)
            .OrderByDescending(fit => fit.Delta)
            .ThenByDescending(fit => fit.SampleCount)
            .ThenBy(fit => fit.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(fit => fit.Label, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumCharacterContextFitCount / 2);
        var negativeFits = fits
            .Where(fit => fit.Delta < 0.0)
            .OrderBy(fit => fit.Delta)
            .ThenByDescending(fit => fit.SampleCount)
            .ThenBy(fit => fit.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(fit => fit.Label, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumCharacterContextFitCount / 2);

        return positiveFits
            .Concat(negativeFits)
            .ToArray();
    }

    private static void AddCharacterContextFit(
        List<FightAnalysisCharacterContextFitDto> fits,
        string key,
        string label,
        string group,
        IReadOnlyList<MergedPlayerFightSample> entries,
        (double Average, int SampleCount) baseline)
    {
        var bucket = ComputeAverageFightImpact(entries);
        if (bucket.SampleCount < MinimumCharacterContextBucketSampleCount || bucket.SampleCount >= baseline.SampleCount)
        {
            return;
        }

        double delta = Math.Round(bucket.Average - baseline.Average, 1);
        if (Math.Abs(delta) < MinimumCharacterContextDeltaMagnitude)
        {
            return;
        }

        string confidenceLabel = DeriveCharacterContextConfidenceLabel(bucket.SampleCount);
        string fightLabel = bucket.SampleCount == 1 ? "fight" : "fights";
        fits.Add(new FightAnalysisCharacterContextFitDto(
            Key: key,
            Label: label,
            Group: group,
            Delta: delta,
            AverageFightImpactScore: bucket.Average,
            BaselineFightImpactScore: baseline.Average,
            SampleCount: bucket.SampleCount,
            ConfidenceLabel: confidenceLabel,
            Detail: $"{label}: Fight Impact {bucket.Average.ToString("0.0", CultureInfo.InvariantCulture)}/100 vs baseline {baseline.Average.ToString("0.0", CultureInfo.InvariantCulture)}/100 across {bucket.SampleCount} {fightLabel} ({confidenceLabel.ToLowerInvariant()} confidence)."));
    }

    private static string DeriveCharacterContextConfidenceLabel(int sampleCount)
    {
        return sampleCount switch
        {
            >= 12 => "High",
            >= 6 => "Medium",
            _ => "Low"
        };
    }

    private static IReadOnlyList<FightAnalysisFightImpactLaneDto> BuildFightImpactLanes(
        IReadOnlyList<MergedPlayerFightSample> mergedFightEntries,
        int maxCount)
    {
        return mergedFightEntries
            .SelectMany(entry => entry.FightImpactLanes.Select(lane => new
            {
                entry.Fight,
                Lane = lane
            }))
            .GroupBy(entry => entry.Lane.Key, StringComparer.OrdinalIgnoreCase)
            .Select(laneGroup =>
            {
                var laneEntries = laneGroup.ToArray();
                var sample = laneEntries[0].Lane;
                return new FightAnalysisFightImpactLaneDto(
                    LaneKey: laneGroup.Key,
                    LaneLabel: string.IsNullOrWhiteSpace(sample.Label) ? laneGroup.Key : sample.Label,
                    AverageImpactScore: Math.Round(CleanupAdjustedAverage(laneEntries, entry => entry.Lane.ImpactScore, entry => entry.Fight), 1),
                    AverageDemandWeightPercent: Math.Round(CleanupAdjustedAverage(laneEntries, entry => entry.Lane.DemandWeightPercent, entry => entry.Fight), 1),
                    AverageStrengthPercent: Math.Round(CleanupAdjustedAverage(laneEntries, entry => entry.Lane.StrengthPercent, entry => entry.Fight), 1),
                    Samples: laneEntries.Length);
            })
            .Where(lane => lane.AverageImpactScore > 0.0 || lane.AverageStrengthPercent > 0.0)
            .OrderByDescending(lane => lane.AverageImpactScore)
            .ThenByDescending(lane => lane.AverageDemandWeightPercent)
            .ThenByDescending(lane => lane.AverageStrengthPercent)
            .ThenBy(lane => lane.LaneLabel, StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .ToArray();
    }

    private static IReadOnlyList<ClassFightCoverageSample> BuildClassFightCoverageSamples(
        IReadOnlyList<FightArtifactSummaryDto> fights,
        string classLabel)
    {
        return fights
            .Select(fight => new ClassFightCoverageSample(
                Fight: fight,
                ClassEntry: (fight.FightIndex?.SquadSide?.Classes ?? Array.Empty<FightSideClassIndexDto>())
                    .FirstOrDefault(entry => string.Equals(entry.ClassLabel, classLabel, StringComparison.OrdinalIgnoreCase))!))
            .Where(sample => sample.ClassEntry is not null && sample.ClassEntry.FightCoverageScore > 0.0)
            .ToArray();
    }

    private static (double Average, int SampleCount) ComputeAverageFightCoverage(IReadOnlyList<ClassFightCoverageSample> coverageSamples)
    {
        return coverageSamples.Count == 0
            ? (0.0, 0)
            : (Math.Round(CleanupAdjustedAverage(coverageSamples, sample => sample.ClassEntry.FightCoverageScore, sample => sample.Fight), 1), coverageSamples.Count);
    }

    private static IReadOnlyList<FightAnalysisClassFightCoverageLaneDto> BuildClassFightCoverageLanes(
        IReadOnlyList<ClassFightCoverageSample> coverageSamples,
        int maxCount)
    {
        return coverageSamples
            .SelectMany(sample => (sample.ClassEntry.FightCoverageLanes ?? Array.Empty<FightSideClassCoverageLaneIndexDto>()).Select(lane => new
            {
                sample.Fight,
                Lane = lane
            }))
            .GroupBy(entry => entry.Lane.Key, StringComparer.OrdinalIgnoreCase)
            .Select(laneGroup =>
            {
                var laneEntries = laneGroup.ToArray();
                var sample = laneEntries[0].Lane;
                return new FightAnalysisClassFightCoverageLaneDto(
                    LaneKey: laneGroup.Key,
                    LaneLabel: string.IsNullOrWhiteSpace(sample.Label) ? laneGroup.Key : sample.Label,
                    AverageCoverageScore: Math.Round(CleanupAdjustedAverage(laneEntries, entry => entry.Lane.CoverageScore, entry => entry.Fight), 1),
                    AverageDemandWeightPercent: Math.Round(CleanupAdjustedAverage(laneEntries, entry => entry.Lane.DemandWeightPercent, entry => entry.Fight), 1),
                    AverageStrengthPercent: Math.Round(CleanupAdjustedAverage(laneEntries, entry => entry.Lane.StrengthPercent, entry => entry.Fight), 1),
                    Samples: laneEntries.Length);
            })
            .Where(lane => lane.AverageCoverageScore > 0.0 || lane.AverageStrengthPercent > 0.0)
            .OrderByDescending(lane => lane.AverageCoverageScore)
            .ThenByDescending(lane => lane.AverageDemandWeightPercent)
            .ThenByDescending(lane => lane.AverageStrengthPercent)
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

    private static IReadOnlyDictionary<string, int> BuildTotalClassSampleCounts(IReadOnlyList<FightArtifactSummaryDto> fights)
    {
        return fights
            .SelectMany(fight => (fight.FightIndex?.Players ?? Array.Empty<FightPlayerIndexDto>())
                .Where(player => !string.IsNullOrWhiteSpace(BuildClassLabel(player)))
                .Select(player => new PlayerFightSample(fight, player)))
            .GroupBy(entry => BuildClassLabel(entry.Player) ?? "Unknown class", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(entry => BuildPlayerIdentityKey(entry.Player), StringComparer.OrdinalIgnoreCase)
                    .SelectMany(playerGroup => MergePlayerFightSamplesByFight(playerGroup.ToArray()))
                    .Count(),
                StringComparer.OrdinalIgnoreCase);
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

    private static string BuildCharacterImpactTrendKey(string account, string characterName, string classLabel)
    {
        var rawKey = BuildCharacterAggregateKey(account, characterName, classLabel);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(rawKey))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
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
        var regeneration = GetProvidedBoon(aggregatedProvidedBoons, "regeneration");

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
            RegenerationGenerationPerFight: Math.Round(regeneration?.GenerationPerFight ?? 0.0, 1),
            RegenerationPresencePerFight: Math.Round(regeneration?.PresencePerFight ?? 0.0, 1),
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

    private static bool IsPreventionLane(string? laneKey)
        => string.Equals(NormalizeLookupKey(laneKey), PreventionLaneKey, StringComparison.OrdinalIgnoreCase);

    private static double GetLaneMetricValue(
        IReadOnlyList<FightPlayerLaneMetricIndexDto>? metrics,
        string metricKey)
    {
        string normalizedMetricKey = NormalizeLookupKey(metricKey);
        return (metrics ?? Array.Empty<FightPlayerLaneMetricIndexDto>())
            .Where(metric =>
                string.Equals(NormalizeLookupKey(metric.Key), normalizedMetricKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeLookupKey(metric.Label), normalizedMetricKey, StringComparison.OrdinalIgnoreCase))
            .Select(metric => metric.Value)
            .FirstOrDefault();
    }

    private static double GetAnalysisLaneMetricTotal(
        IReadOnlyList<FightAnalysisLaneMetricDto>? metrics,
        string metricKey)
    {
        string normalizedMetricKey = NormalizeLookupKey(metricKey);
        return (metrics ?? Array.Empty<FightAnalysisLaneMetricDto>())
            .Where(metric =>
                string.Equals(NormalizeLookupKey(metric.Key), normalizedMetricKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeLookupKey(metric.Label), normalizedMetricKey, StringComparison.OrdinalIgnoreCase))
            .Select(metric => metric.TotalValue)
            .FirstOrDefault();
    }

    private static double GetAnalysisLaneMetricAverage(
        IReadOnlyList<FightAnalysisLaneMetricDto>? metrics,
        string metricKey)
    {
        string normalizedMetricKey = NormalizeLookupKey(metricKey);
        return (metrics ?? Array.Empty<FightAnalysisLaneMetricDto>())
            .Where(metric =>
                string.Equals(NormalizeLookupKey(metric.Key), normalizedMetricKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeLookupKey(metric.Label), normalizedMetricKey, StringComparison.OrdinalIgnoreCase))
            .Select(metric => metric.AveragePerAppearance)
            .FirstOrDefault();
    }

    private static double ComputePercent(double numerator, double denominator)
    {
        return denominator <= 0.0
            ? 0.0
            : Math.Round(numerator * 100.0 / denominator, 1);
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

        double MergeFightImpactScore()
        {
            var impactEntries = entries
                .Where(entry => entry.Player.FightImpactScore > 0.0)
                .Select(entry => new
                {
                    Score = entry.Player.FightImpactScore,
                    Weight = Math.Max(1.0, Math.Max(entry.Player.ActiveSeconds, entry.Player.CombatSeconds))
                })
                .ToArray();
            double totalWeight = impactEntries.Sum(entry => entry.Weight);
            return impactEntries.Length == 0 || totalWeight <= 0.0
                ? 0.0
                : Math.Round(impactEntries.Sum(entry => entry.Score * entry.Weight) / totalWeight, 1);
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
            Corrupts: entries.Sum(entry => entry.Player.Corrupts),
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
            FightImpactScore: MergeFightImpactScore(),
            FightImpactLanes: MergePlayerFightImpactLanes(entries),
            Lanes: MergePlayerFightLanes(entries));
    }

    private static IReadOnlyList<MergedPlayerFightImpactLaneSample> MergePlayerFightImpactLanes(IReadOnlyList<PlayerFightSample> entries)
    {
        return entries
            .SelectMany(entry => (entry.Player.FightImpactLanes ?? Array.Empty<FightPlayerFightImpactLaneIndexDto>())
                .Where(lane => !string.IsNullOrWhiteSpace(lane.Label))
                .Select(lane => new
                {
                    Lane = lane,
                    Weight = Math.Max(1.0, Math.Max(entry.Player.ActiveSeconds, entry.Player.CombatSeconds))
                }))
            .GroupBy(entry => string.IsNullOrWhiteSpace(entry.Lane.Key) ? entry.Lane.Label : entry.Lane.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var laneEntries = group.ToArray();
                double totalWeight = laneEntries.Sum(entry => entry.Weight);
                if (totalWeight <= 0.0)
                {
                    totalWeight = laneEntries.Length;
                }

                double WeightedAverage(Func<FightPlayerFightImpactLaneIndexDto, double> selector)
                {
                    return laneEntries.Sum(entry => selector(entry.Lane) * entry.Weight) / totalWeight;
                }

                var sample = laneEntries[0].Lane;
                return new MergedPlayerFightImpactLaneSample(
                    Key: group.Key,
                    Label: PickMostCommonNonEmpty(laneEntries.Select(entry => entry.Lane.Label)) ?? sample.Label ?? group.Key,
                    StrengthPercent: Math.Round(WeightedAverage(lane => lane.StrengthPercent), 1),
                    SharePercent: Math.Round(WeightedAverage(lane => lane.SharePercent), 1),
                    DemandScorePercent: Math.Round(WeightedAverage(lane => lane.DemandScorePercent), 1),
                    DemandLabel: PickMostCommonNonEmpty(laneEntries.Select(entry => entry.Lane.DemandLabel)),
                    DemandWeightPercent: Math.Round(WeightedAverage(lane => lane.DemandWeightPercent), 1),
                    ImpactScore: Math.Round(WeightedAverage(lane => lane.ImpactScore), 1),
                    EvidenceLine: PickMostCommonNonEmpty(laneEntries.Select(entry => entry.Lane.EvidenceLine)));
            })
            .OrderByDescending(lane => lane.ImpactScore)
            .ThenByDescending(lane => lane.DemandScorePercent)
            .ThenByDescending(lane => lane.StrengthPercent)
            .ToArray();
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
        return BuildClassLabel(player.Profession, player.EliteSpec);
    }

    private static string? BuildClassLabel(string? profession, string? eliteSpec)
    {
        if (!string.IsNullOrWhiteSpace(eliteSpec) &&
            !string.Equals(eliteSpec, profession, StringComparison.OrdinalIgnoreCase))
        {
            return eliteSpec.Trim();
        }

        return string.IsNullOrWhiteSpace(profession) ? null : profession.Trim();
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

    private static double ComputeImpactScoreForMergedEntries(IReadOnlyList<MergedPlayerFightSample> mergedFightEntries)
    {
        var sampleCount = mergedFightEntries.Count;
        if (sampleCount == 0)
        {
            return 0.0;
        }

        var laneScores = mergedFightEntries
            .Select(entry => new
            {
                entry.Fight,
                Score = ComputePlayerLaneScores(entry.Lanes)
            })
            .Where(score => score.Score.Primary > 0.0 || score.Score.Weighted > 0.0)
            .ToArray();
        var winCount = mergedFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "squad", StringComparison.OrdinalIgnoreCase));
        var winRatePercent = Math.Round(winCount * 100.0 / sampleCount, 1);
        var averageDamage = Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => (double)entry.Damage, entry => entry.Fight), 0);
        var averageDowns = Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Downs, entry => entry.Fight), 1);
        var averageKills = Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Kills, entry => entry.Fight), 1);
        var averageStrips = Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Strips, entry => entry.Fight), 1);
        var averageCleanses = Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.OutgoingCleanses, entry => entry.Fight), 1);
        var averageHealing = Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => (double)entry.Healing, entry => entry.Fight), 0);
        var averageBarrier = Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => (double)entry.Barrier, entry => entry.Fight), 0);
        var averageResurrects = Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Resurrects, entry => entry.Fight), 1);
        var averageDeaths = Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Deaths, entry => entry.Fight), 1);
        var averageRecoveries = Math.Round(CleanupAdjustedAverage(mergedFightEntries, entry => entry.Recoveries, entry => entry.Fight), 1);
        double? averageInPositionRate = RoundNullable(CleanupAdjustedAverageOrNull(mergedFightEntries, entry => entry.InPositionRate, entry => entry.Fight), 1);

        return ComputeImpactScore(
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

internal sealed class CachedFightAnalysisSnapshot
{
    public CachedFightAnalysisSnapshot(
        FightAnalysisSnapshotDto snapshot,
        IReadOnlyList<FightAnalysisPlayerRowDto> playerDetails,
        long lastAccessOrder)
    {
        Snapshot = snapshot;
        PlayerDetails = playerDetails;
        LastAccessOrder = lastAccessOrder;
    }

    public FightAnalysisSnapshotDto Snapshot { get; }

    public IReadOnlyList<FightAnalysisPlayerRowDto> PlayerDetails { get; }

    public long LastAccessOrder { get; set; }
}

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

internal sealed class EnemyClassPerformanceAggregate
{
    public EnemyClassPerformanceAggregate(string classLabel)
    {
        ClassLabel = classLabel;
    }

    public string ClassLabel { get; }
    public string? Icon { get; set; }
    public int TotalCount { get; set; }
    public HashSet<string> FightIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int PerformanceSampleCount { get; set; }
    public double TotalDps { get; set; }
    public double BestDps { get; set; }
    public double TotalStripsPerMinute { get; set; }
    public double BestStripsPerMinute { get; set; }
    public int DamageBurstTopCount { get; set; }
    public int StripBurstTopCount { get; set; }

    public FightAnalysisEnemyClassRowDto ToRow()
    {
        double? averageDps = PerformanceSampleCount <= 0
            ? null
            : Math.Round(TotalDps / PerformanceSampleCount, 1);
        double? averageStripsPerMinute = PerformanceSampleCount <= 0
            ? null
            : Math.Round(TotalStripsPerMinute / PerformanceSampleCount, 1);

        return new FightAnalysisEnemyClassRowDto(
            ClassLabel: ClassLabel,
            Icon: Icon,
            TotalCount: TotalCount > 0 ? TotalCount : PerformanceSampleCount,
            FightCount: FightIds.Count,
            PerformanceSampleCount: PerformanceSampleCount,
            ThreatScore: null,
            AverageDps: averageDps,
            BestDps: PerformanceSampleCount <= 0 ? null : Math.Round(BestDps, 1),
            AverageStripsPerMinute: averageStripsPerMinute,
            BestStripsPerMinute: PerformanceSampleCount <= 0 ? null : Math.Round(BestStripsPerMinute, 1),
            DamageBurstTopCount: DamageBurstTopCount,
            StripBurstTopCount: StripBurstTopCount);
    }
}

internal sealed record FightExpectedScoreResult(
    double ExpectedScore,
    double ContextDelta,
    int SampleCount,
    string ConfidenceLabel,
    string Detail);

internal sealed record FightAnalysisTrackedBoon(
    long Id,
    string Name,
    bool StackBased);

internal sealed record PlayerFightSample(
    FightArtifactSummaryDto Fight,
    FightPlayerIndexDto Player);

internal sealed record TopFiveActorSample(
    FightArtifactSummaryDto Fight,
    string Account,
    string CharacterName,
    string ClassLabel,
    string? Icon,
    double Value);

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
    int Corrupts,
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
    double FightImpactScore,
    IReadOnlyList<MergedPlayerFightImpactLaneSample> FightImpactLanes,
    IReadOnlyList<MergedPlayerLaneSample> Lanes);

internal sealed record MergedPlayerFightImpactLaneSample(
    string Key,
    string Label,
    double StrengthPercent,
    double SharePercent,
    double DemandScorePercent,
    string? DemandLabel,
    double DemandWeightPercent,
    double ImpactScore,
    string? EvidenceLine);

internal sealed record ClassFightCoverageSample(
    FightArtifactSummaryDto Fight,
    FightSideClassIndexDto ClassEntry);

internal sealed record ClassDifferenceSideStats(
    string ClassLabel,
    int FightCount,
    int TotalCount,
    int CoverageSampleCount,
    double AverageCountWhenPresent,
    double AverageCoverage)
{
    public static ClassDifferenceSideStats Empty(string classLabel)
    {
        return new ClassDifferenceSideStats(classLabel, 0, 0, 0, 0.0, 0.0);
    }
}

internal sealed record MergedPlayerFightProvidedBoonSample(
    FightArtifactSummaryDto Fight,
    string Account,
    string ClassLabel,
    IReadOnlyList<MergedProvidedBoonSample> ProvidedBoons);

internal sealed record BoonTrendBucket(
    string Key,
    string Label,
    string BucketType,
    IReadOnlyList<FightArtifactSummaryDto> Fights,
    IReadOnlyList<MergedPlayerFightProvidedBoonSample> ProviderSamples);

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
