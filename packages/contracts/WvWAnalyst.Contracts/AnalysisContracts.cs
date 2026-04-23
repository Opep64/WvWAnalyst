namespace WvWAnalyst.Contracts;

public sealed record FightAnalysisSnapshotDto(
    FightAnalysisFilterOptionsDto Options,
    FightAnalysisSelectionDto Selection,
    FightAnalysisScopeDto Scope,
    FightAnalysisOverviewDto Overview,
    IReadOnlyList<FightAnalysisTrendPointDto> Trends,
    IReadOnlyList<FightAnalysisPlayerRowDto> TopPlayers,
    IReadOnlyList<FightAnalysisClassRowDto> TopClasses,
    IReadOnlyList<FightAnalysisLaneRowDto> TopLanes,
    IReadOnlyList<FightAnalysisBoonRowDto> TopBoons);

public sealed record FightAnalysisFilterOptionsDto(
    IReadOnlyList<string> Commanders,
    string? MinFightDate,
    string? MaxFightDate);

public sealed record FightAnalysisSelectionDto(
    string? Commander,
    string? StartDate,
    string? EndDate,
    string OutcomeCode);

public sealed record FightAnalysisScopeDto(
    int TotalImportedFights,
    int FilteredFightCount,
    int WinCount,
    int LossCount,
    int DrawCount,
    double WinRatePercent);

public sealed record FightAnalysisOverviewDto(
    double? AverageOverallScore,
    string? AverageOverallGrade,
    double? AverageCohesionScore,
    double? AveragePressureScore,
    double? AverageDownstateScore,
    double? AverageResilienceScore,
    double AverageSquadSize,
    double AverageEnemySize,
    double AverageDurationSeconds);

public sealed record FightAnalysisTrendPointDto(
    string FightId,
    string FightName,
    string FightDateLabel,
    string? Commander,
    string OutcomeLabel,
    int? OverallScore,
    int? CohesionScore,
    int? PressureScore,
    int? DownstateScore,
    int? ResilienceScore);

public sealed record FightAnalysisPlayerRowDto(
    string Account,
    string DisplayName,
    int FightCount,
    int TotalFightCountAll,
    int WinCount,
    int LossCount,
    int DrawCount,
    double WinRatePercent,
    IReadOnlyList<string> ClassesPlayed,
    string? PrimaryClassLabel,
    string? PrimaryLaneLabel,
    double ImpactScore,
    double AveragePrimaryLaneScore,
    double AverageWeightedLaneScore,
    double AverageDamagePerFight,
    double AverageDownsPerFight,
    double AverageKillsPerFight,
    double AverageStripsPerFight,
    double AverageOutgoingCleansesPerFight,
    double AverageHealingPerFight,
    double AverageBarrierPerFight,
    double AverageResurrectsPerFight,
    double AverageDeathsPerFight,
    double AverageRecoveriesPerFight,
    double? AverageInPositionRate,
    string? ContributionSummary,
    IReadOnlyList<string> CharacterNames,
    IReadOnlyList<FightAnalysisPlayerCharacterDto> Characters);

public sealed record FightAnalysisPlayerCharacterDto(
    string CharacterName,
    string ClassLabel,
    IReadOnlyList<string> ClassesPlayed,
    int FightCount,
    int TotalFightCountAll,
    int WinCount,
    int LossCount,
    int DrawCount,
    double WinRatePercent,
    double ImpactScore,
    string? PrimaryLaneLabel,
    double AveragePrimaryLaneScore,
    double AverageWeightedLaneScore,
    string? ContributionSummary,
    string? ConfidenceLabel,
    string? ConfidenceDetail,
    double? AverageInPositionRate,
    double? AverageTooFarRate,
    double? AverageOverextendedRate,
    double? AverageLateralRiskRate,
    double AverageDeathsPerFight,
    double AverageRecoveriesPerFight,
    double AverageActivePresencePercent,
    double AverageEngagedPresencePercent,
    IReadOnlyList<string> EvidenceLines,
    IReadOnlyList<FightAnalysisCharacterLaneContributionDto> LaneContributions);

public sealed record FightAnalysisCharacterLaneContributionDto(
    string LaneKey,
    string LaneLabel,
    double AverageStrengthPercent,
    double AverageSharePercent,
    double OverallStrengthPercent,
    double OverallSharePercent,
    double AppearanceRatePercent,
    int Samples,
    int TotalSamplesAll,
    string? RateBand,
    string? EvidenceLine,
    IReadOnlyList<FightAnalysisLaneMetricDto> Metrics);

public sealed record FightAnalysisLaneMetricDto(
    string Key,
    string Label,
    double TotalValue,
    double AveragePerAppearance,
    string? Unit);

public sealed record FightAnalysisClassRowDto(
    string ClassLabel,
    int SampleCount,
    int DistinctAccounts,
    int WinCount,
    int LossCount,
    int DrawCount,
    double WinRatePercent,
    double ContributionScore,
    string? TopPlayerDisplayName,
    double AveragePrimaryLaneScore,
    double AverageWeightedLaneScore,
    IReadOnlyList<FightAnalysisCharacterLaneContributionDto> LaneContributions,
    IReadOnlyList<FightAnalysisClassPlayerRowDto> Players);

public sealed record FightAnalysisClassPlayerRowDto(
    string Account,
    string DisplayName,
    int FightCount,
    int TotalFightCountAll,
    int WinCount,
    int LossCount,
    int DrawCount,
    double WinRatePercent,
    double ImpactScore,
    string? PrimaryLaneLabel,
    double AveragePrimaryLaneScore,
    double AverageWeightedLaneScore);

public sealed record FightAnalysisLaneRowDto(
    string LaneKey,
    string LaneLabel,
    int Samples,
    int DistinctAccounts,
    int DistinctClasses,
    double AverageStrengthPercent,
    double AverageSharePercent,
    double AppearanceRatePercent,
    string? TopClassLabel,
    string? TopPlayerDisplayName,
    string? EvidenceLine);

public sealed record FightAnalysisBoonRowDto(
    long Id,
    string Name,
    string TypeLabel,
    bool StackBased,
    bool TracksOverapplication,
    int FightCount,
    double AverageCoverage,
    double? AverageStacks,
    double? AverageOverapplication,
    string? TopClassLabel,
    IReadOnlyList<FightAnalysisBoonClassProviderDto> ClassProviders);

public sealed record FightAnalysisBoonClassProviderDto(
    string ClassLabel,
    int SampleCount,
    int ProviderAppearanceCount,
    int DistinctAccounts,
    double AverageGeneration,
    double? AverageGenerationPresence,
    double AverageOverstack,
    double ProviderScore);
