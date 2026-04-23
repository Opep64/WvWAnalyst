using System.Globalization;
using WvWAnalyst.Api.Bridge;
using WvWAnalyst.Contracts;

namespace WvWAnalyst.Api.Analysis;

public sealed class FightAnalysisService
{
    private const int MinimumClassSampleCount = 40;
    private const int MinimumClassPlayerFightCount = 20;

    private readonly FightCatalogService _fightCatalog;

    public FightAnalysisService(FightCatalogService fightCatalog)
    {
        _fightCatalog = fightCatalog;
    }

    public FightAnalysisSnapshotDto BuildSnapshot(string? commander, string? startDate, string? endDate, string? outcomeCode)
    {
        var allFights = _fightCatalog.GetFightBrowserSnapshot().Fights
            .Where(fight => fight.FightIndex is not null)
            .Where(fight => string.Equals(fight.Status, "Imported", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var commanderOptions = allFights
            .SelectMany(fight => fight.FightIndex?.CommanderDisplayNames ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

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

        var filteredFights = allFights
            .Where(fight => MatchesCommander(fight, normalizedCommander))
            .Where(fight => MatchesOutcome(fight, normalizedOutcome))
            .Where(fight => MatchesDateRange(fight, normalizedStartDate, normalizedEndDate))
            .OrderBy(fight => GetFightSortValue(fight))
            .ToArray();

        var trackedPlayerSamples = GetTrackedPlayerSamples(allFights);
        var totalPlayerFightCounts = BuildTotalPlayerFightCounts(trackedPlayerSamples);
        var totalCharacterFightCounts = BuildTotalCharacterFightCounts(trackedPlayerSamples);
        var totalCharacterLaneSampleCounts = BuildTotalCharacterLaneSampleCounts(trackedPlayerSamples);
        var totalClassPlayerFightCounts = BuildTotalClassPlayerFightCounts(allFights);

        return new FightAnalysisSnapshotDto(
            Options: new FightAnalysisFilterOptionsDto(
                Commanders: commanderOptions,
                MinFightDate: minDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                MaxFightDate: maxDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            Selection: new FightAnalysisSelectionDto(
                Commander: normalizedCommander,
                StartDate: normalizedStartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                EndDate: normalizedEndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                OutcomeCode: normalizedOutcome),
            Scope: BuildScope(allFights, filteredFights),
            Overview: BuildOverview(filteredFights),
            Trends: BuildTrends(filteredFights),
            TopPlayers: BuildTopPlayers(filteredFights, totalPlayerFightCounts, totalCharacterFightCounts, totalCharacterLaneSampleCounts),
            TopClasses: BuildTopClasses(filteredFights, totalClassPlayerFightCounts),
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
            WinRatePercent: winRate);
    }

    private static FightAnalysisOverviewDto BuildOverview(IReadOnlyList<FightArtifactSummaryDto> fights)
    {
        var executionScores = fights
            .Select(fight => fight.FightIndex?.Execution)
            .Where(execution => execution?.ScoreAvailable == true && execution.OverallScore.HasValue)
            .Select(execution => execution!.OverallScore!.Value)
            .ToArray();

        var pillarLookup = fights
            .SelectMany(fight => fight.FightIndex?.Execution?.Pillars ?? Array.Empty<FightExecutionPillarIndexDto>())
            .GroupBy(pillar => pillar.PillarId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(pillar => (double)pillar.Score).DefaultIfEmpty().Average(),
                StringComparer.OrdinalIgnoreCase);

        var gradeSource = fights
            .Select(fight => fight.FightIndex?.Execution)
            .Where(execution => execution?.ScoreAvailable == true && execution.OverallScore.HasValue)
            .Select(execution => execution!.Grade)
            .Where(grade => !string.IsNullOrWhiteSpace(grade))
            .GroupBy(grade => grade!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?.Key;

        double averageSquadSize = fights.Count == 0 ? 0.0 : Math.Round(fights.Average(fight => fight.FightIndex?.SquadPlayerCount ?? 0), 1);
        double averageEnemySize = fights.Count == 0 ? 0.0 : Math.Round(fights.Average(fight => fight.FightIndex?.EnemyPlayerCount ?? fight.FightIndex?.EnemyTargetCount ?? 0), 1);
        double averageDurationSeconds = fights.Count == 0
            ? 0.0
            : Math.Round(fights.Average(fight => (fight.FightIndex?.DurationMilliseconds ?? 0) / 1000.0), 1);

        return new FightAnalysisOverviewDto(
            AverageOverallScore: executionScores.Length == 0 ? null : Math.Round(executionScores.Average(), 1),
            AverageOverallGrade: gradeSource,
            AverageCohesionScore: GetPillarAverage(pillarLookup, "cohesion-positioning"),
            AveragePressureScore: GetPillarAverage(pillarLookup, "pressure-burst"),
            AverageDownstateScore: GetPillarAverage(pillarLookup, "downstate-control"),
            AverageResilienceScore: GetPillarAverage(pillarLookup, "resilience-stabilization"),
            AverageSquadSize: averageSquadSize,
            AverageEnemySize: averageEnemySize,
            AverageDurationSeconds: averageDurationSeconds);
    }

    private static IReadOnlyList<FightAnalysisTrendPointDto> BuildTrends(IReadOnlyList<FightArtifactSummaryDto> fights)
    {
        return fights
            .Select(fight =>
            {
                var pillars = BuildPillarMap(fight.FightIndex?.Execution?.Pillars);
                return new FightAnalysisTrendPointDto(
                    FightId: fight.FightId,
                    FightName: fight.FightIndex?.FightName ?? fight.SourceFileName ?? fight.FightId,
                    FightDateLabel: FormatFightDateLabel(fight),
                    Commander: fight.FightIndex?.CommanderDisplayNames?.FirstOrDefault(),
                    OutcomeLabel: fight.FightIndex?.Outcome.DisplayLabel ?? "Unavailable",
                    OverallScore: fight.FightIndex?.Execution?.OverallScore,
                    CohesionScore: pillars.TryGetValue("cohesion-positioning", out int cohesion) ? cohesion : null,
                    PressureScore: pillars.TryGetValue("pressure-burst", out int pressure) ? pressure : null,
                    DownstateScore: pillars.TryGetValue("downstate-control", out int downstate) ? downstate : null,
                    ResilienceScore: pillars.TryGetValue("resilience-stabilization", out int resilience) ? resilience : null);
            })
            .ToArray();
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
                var laneScores = mergedFightEntries
                    .Select(entry => ComputePlayerLaneScores(entry.Lanes))
                    .Where(score => score.Primary > 0.0 || score.Weighted > 0.0)
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
                var positioningRates = mergedFightEntries
                    .Where(entry => entry.InPositionRate.HasValue)
                    .Select(entry => entry.InPositionRate!.Value)
                    .ToArray();
                var averageDamage = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => (double)entry.Damage), 0);
                var averageDowns = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Downs), 1);
                var averageKills = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Kills), 1);
                var averageStrips = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Strips), 1);
                var averageCleanses = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.OutgoingCleanses), 1);
                var averageHealing = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => (double)entry.Healing), 0);
                var averageBarrier = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => (double)entry.Barrier), 0);
                var averageResurrects = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Resurrects), 1);
                var averageDeaths = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Deaths), 1);
                var averageRecoveries = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Recoveries), 1);
                double? averageInPositionRate = positioningRates.Length == 0 ? null : Math.Round(positioningRates.Average(), 1);
                var winRatePercent = sampleCount == 0 ? 0.0 : Math.Round(winCount * 100.0 / sampleCount, 1);
                var impactScore = ComputeImpactScore(
                    averageWeightedLaneScore: laneScores.Length == 0 ? 0.0 : laneScores.Average(score => score.Weighted),
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
                    AveragePrimaryLaneScore: laneScores.Length == 0 ? 0.0 : Math.Round(laneScores.Average(score => score.Primary), 1),
                    AverageWeightedLaneScore: laneScores.Length == 0 ? 0.0 : Math.Round(laneScores.Average(score => score.Weighted), 1),
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
        IReadOnlyDictionary<string, int> totalClassPlayerFightCounts)
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
                    .Select(entry => ComputePlayerLaneScores(entry.Lanes))
                    .Where(score => score.Primary > 0.0 || score.Weighted > 0.0)
                    .ToArray();
                var winCount = mergedFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "squad", StringComparison.OrdinalIgnoreCase));
                var lossCount = mergedFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "enemy", StringComparison.OrdinalIgnoreCase));
                var drawCount = mergedFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "draw", StringComparison.OrdinalIgnoreCase));
                var winRatePercent = sampleCount == 0 ? 0.0 : Math.Round(winCount * 100.0 / sampleCount, 1);
                var averageDamage = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => (double)entry.Damage), 0);
                var averageDowns = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Downs), 1);
                var averageKills = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Kills), 1);
                var averageStrips = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Strips), 1);
                var averageCleanses = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.OutgoingCleanses), 1);
                var averageHealing = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => (double)entry.Healing), 0);
                var averageBarrier = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => (double)entry.Barrier), 0);
                var averageResurrects = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Resurrects), 1);
                var averageDeaths = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Deaths), 1);
                var averageRecoveries = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Recoveries), 1);
                var inPositionRates = mergedFightEntries
                    .Where(entry => entry.InPositionRate.HasValue)
                    .Select(entry => entry.InPositionRate!.Value)
                    .ToArray();
                var classPlayers = BuildClassPlayerSummaries(group.Key, entries, totalClassPlayerFightCounts);
                var topPlayerDisplayName = classPlayers
                    .OrderByDescending(player => player.ImpactScore)
                    .ThenByDescending(player => player.FightCount)
                    .ThenBy(player => player.Account, StringComparer.OrdinalIgnoreCase)
                    .Select(player => player.Account)
                    .FirstOrDefault();
                var contributionScore = ComputeImpactScore(
                    averageWeightedLaneScore: laneScores.Length == 0 ? 0.0 : laneScores.Average(score => score.Weighted),
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
                    averageInPositionRate: inPositionRates.Length == 0 ? null : inPositionRates.Average());

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
                    AveragePrimaryLaneScore: laneScores.Length == 0 ? 0.0 : Math.Round(laneScores.Average(score => score.Primary), 1),
                    AverageWeightedLaneScore: laneScores.Length == 0 ? 0.0 : Math.Round(laneScores.Average(score => score.Weighted), 1),
                    LaneContributions: laneContributions,
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
                    .OrderByDescending(classGroup => classGroup.Average(entry => entry.Lane.StrengthPercent))
                    .ThenByDescending(classGroup => classGroup.Count())
                    .Select(classGroup => classGroup.Key)
                    .FirstOrDefault();
                var topPlayerDisplayName = entries
                    .GroupBy(entry => entry.Account, StringComparer.OrdinalIgnoreCase)
                    .Select(playerGroup => new
                    {
                        Display = playerGroup.Select(entry => entry.PlayerDisplayName).FirstOrDefault(),
                        AverageStrength = playerGroup.Average(entry => entry.Lane.StrengthPercent),
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
                    AverageStrengthPercent: Math.Round(entries.Average(entry => entry.Lane.StrengthPercent), 1),
                    AverageSharePercent: Math.Round(entries.Average(entry => entry.Lane.SharePercent), 1),
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

                var laneContributions = mergedFightEntries
                    .SelectMany(entry => entry.Lanes)
                    .GroupBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(laneGroup =>
                    {
                        var laneEntries = laneGroup.ToArray();
                        var sample = laneEntries[0];
                        double averageStrengthPercent = Math.Round(laneEntries.Average(entry => entry.StrengthPercent), 1);
                        double averageSharePercent = Math.Round(laneEntries.Average(entry => entry.SharePercent), 1);
                        double appearanceRatePercent = sampleCount == 0 ? 0.0 : Math.Round(laneEntries.Length * 100.0 / sampleCount, 1);
                        double overallStrengthPercent = sampleCount == 0 ? 0.0 : Math.Round(laneEntries.Sum(entry => entry.StrengthPercent) / sampleCount, 1);
                        double overallSharePercent = sampleCount == 0 ? 0.0 : Math.Round(laneEntries.Sum(entry => entry.SharePercent) / sampleCount, 1);
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
                                .Where(entry => !string.IsNullOrWhiteSpace(entry.EvidenceLine))
                                .OrderByDescending(entry => entry.StrengthPercent)
                                .ThenByDescending(entry => entry.SharePercent)
                                .Select(entry => entry.EvidenceLine)
                                .FirstOrDefault(),
                            Metrics: AggregateLaneMetrics(laneEntries));
                    })
                    .OrderByDescending(lane => lane.OverallStrengthPercent)
                    .ThenByDescending(lane => lane.AppearanceRatePercent)
                    .ThenByDescending(lane => lane.Samples)
                    .ThenBy(lane => lane.LaneLabel, StringComparer.OrdinalIgnoreCase)
                    .Take(6)
                    .ToArray();

                var laneScores = mergedFightEntries
                    .Select(entry => ComputePlayerLaneScores(entry.Lanes))
                    .Where(score => score.Primary > 0.0 || score.Weighted > 0.0)
                    .ToArray();
                var winCount = mergedFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "squad", StringComparison.OrdinalIgnoreCase));
                var lossCount = mergedFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "enemy", StringComparison.OrdinalIgnoreCase));
                var drawCount = mergedFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "draw", StringComparison.OrdinalIgnoreCase));
                var winRatePercent = sampleCount == 0 ? 0.0 : Math.Round(winCount * 100.0 / sampleCount, 1);
                var averageDamage = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => (double)entry.Damage), 0);
                var averageDowns = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Downs), 1);
                var averageKills = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Kills), 1);
                var averageStrips = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Strips), 1);
                var averageCleanses = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.OutgoingCleanses), 1);
                var averageHealing = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => (double)entry.Healing), 0);
                var averageBarrier = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => (double)entry.Barrier), 0);
                var averageResurrects = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Resurrects), 1);
                var averageDeaths = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Deaths), 1);
                var averageRecoveries = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Recoveries), 1);
                var inPositionRates = mergedFightEntries
                    .Where(entry => entry.InPositionRate.HasValue)
                    .Select(entry => entry.InPositionRate!.Value)
                    .ToArray();
                var tooFarRates = mergedFightEntries
                    .Where(entry => entry.TooFarRate.HasValue)
                    .Select(entry => entry.TooFarRate!.Value)
                    .ToArray();
                var overextendedRates = mergedFightEntries
                    .Where(entry => entry.OverextendedRate.HasValue)
                    .Select(entry => entry.OverextendedRate!.Value)
                    .ToArray();
                var lateralRiskRates = mergedFightEntries
                    .Where(entry => entry.LateralRiskRate.HasValue)
                    .Select(entry => entry.LateralRiskRate!.Value)
                    .ToArray();
                double? averageInPositionRate = inPositionRates.Length == 0 ? null : Math.Round(inPositionRates.Average(), 1);
                var impactScore = ComputeImpactScore(
                    averageWeightedLaneScore: laneScores.Length == 0 ? 0.0 : laneScores.Average(score => score.Weighted),
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
                    AveragePrimaryLaneScore: laneScores.Length == 0 ? 0.0 : Math.Round(laneScores.Average(score => score.Primary), 1),
                    AverageWeightedLaneScore: laneScores.Length == 0 ? 0.0 : Math.Round(laneScores.Average(score => score.Weighted), 1),
                    ContributionSummary: contributionSummary,
                    ConfidenceLabel: confidenceLabel,
                    ConfidenceDetail: confidenceDetail,
                    AverageInPositionRate: averageInPositionRate,
                    AverageTooFarRate: tooFarRates.Length == 0 ? null : Math.Round(tooFarRates.Average(), 1),
                    AverageOverextendedRate: overextendedRates.Length == 0 ? null : Math.Round(overextendedRates.Average(), 1),
                    AverageLateralRiskRate: lateralRiskRates.Length == 0 ? null : Math.Round(lateralRiskRates.Average(), 1),
                    AverageDeathsPerFight: averageDeaths,
                    AverageRecoveriesPerFight: averageRecoveries,
                    AverageActivePresencePercent: Math.Round(ComputeAveragePresencePercent(mergedFightEntries, entry => entry.ActiveSeconds), 1),
                    AverageEngagedPresencePercent: Math.Round(ComputeAveragePresencePercent(mergedFightEntries, entry => entry.CombatSeconds), 1),
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
                    .Select(entry => ComputePlayerLaneScores(entry.Lanes))
                    .Where(score => score.Primary > 0.0 || score.Weighted > 0.0)
                    .ToArray();
                var winCount = mergedFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "squad", StringComparison.OrdinalIgnoreCase));
                var lossCount = mergedFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "enemy", StringComparison.OrdinalIgnoreCase));
                var drawCount = mergedFightEntries.Count(entry => string.Equals(entry.Fight.FightIndex?.Outcome.OutcomeCode, "draw", StringComparison.OrdinalIgnoreCase));
                var winRatePercent = sampleCount == 0 ? 0.0 : Math.Round(winCount * 100.0 / sampleCount, 1);
                var averageDamage = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => (double)entry.Damage), 0);
                var averageDowns = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Downs), 1);
                var averageKills = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Kills), 1);
                var averageStrips = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Strips), 1);
                var averageCleanses = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.OutgoingCleanses), 1);
                var averageHealing = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => (double)entry.Healing), 0);
                var averageBarrier = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => (double)entry.Barrier), 0);
                var averageResurrects = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Resurrects), 1);
                var averageDeaths = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Deaths), 1);
                var averageRecoveries = sampleCount == 0 ? 0.0 : Math.Round(mergedFightEntries.Average(entry => entry.Recoveries), 1);
                var inPositionRates = mergedFightEntries
                    .Where(entry => entry.InPositionRate.HasValue)
                    .Select(entry => entry.InPositionRate!.Value)
                    .ToArray();
                var impactScore = ComputeImpactScore(
                    averageWeightedLaneScore: laneScores.Length == 0 ? 0.0 : laneScores.Average(score => score.Weighted),
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
                    averageInPositionRate: inPositionRates.Length == 0 ? null : inPositionRates.Average());
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
                    AveragePrimaryLaneScore: laneScores.Length == 0 ? 0.0 : Math.Round(laneScores.Average(score => score.Primary), 1),
                    AverageWeightedLaneScore: laneScores.Length == 0 ? 0.0 : Math.Round(laneScores.Average(score => score.Weighted), 1));
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
            .SelectMany(entry => entry.Lanes)
            .GroupBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(laneGroup =>
            {
                var laneEntries = laneGroup.ToArray();
                var sample = laneEntries[0];
                double averageStrengthPercent = Math.Round(laneEntries.Average(entry => entry.StrengthPercent), 1);
                double averageSharePercent = Math.Round(laneEntries.Average(entry => entry.SharePercent), 1);
                double appearanceRatePercent = sampleCount == 0 ? 0.0 : Math.Round(laneEntries.Length * 100.0 / sampleCount, 1);
                double overallStrengthPercent = sampleCount == 0 ? 0.0 : Math.Round(laneEntries.Sum(entry => entry.StrengthPercent) / sampleCount, 1);
                double overallSharePercent = sampleCount == 0 ? 0.0 : Math.Round(laneEntries.Sum(entry => entry.SharePercent) / sampleCount, 1);
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
                        .Where(entry => !string.IsNullOrWhiteSpace(entry.EvidenceLine))
                        .OrderByDescending(entry => entry.StrengthPercent)
                        .ThenByDescending(entry => entry.SharePercent)
                        .Select(entry => entry.EvidenceLine)
                        .FirstOrDefault(),
                    Metrics: AggregateLaneMetrics(laneEntries));
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
