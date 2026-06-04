namespace WvWAnalyst.Contracts;

public sealed record FightAnalysisSnapshotDto(
    FightAnalysisFilterOptionsDto Options,
    FightAnalysisSelectionDto Selection,
    FightAnalysisScopeDto Scope,
    FightAnalysisOverviewDto Overview,
    IReadOnlyList<FightAnalysisTrendPointDto> Trends,
    IReadOnlyList<FightAnalysisTeamScoreTrendPointDto> NightlyTeamScores,
    IReadOnlyList<FightAnalysisBurstTrendPointDto> BurstTrends,
    IReadOnlyList<FightAnalysisPlayerSummaryRowDto> TopPlayers,
    IReadOnlyList<FightAnalysisClassRowDto> TopClasses,
    IReadOnlyList<FightAnalysisEnemyClassRowDto> TopEnemyClasses,
    IReadOnlyList<FightAnalysisTopFiveCategoryDto> TopFive,
    IReadOnlyList<FightAnalysisLaneRowDto> TopLanes,
    IReadOnlyList<FightAnalysisBoonTrendDto> BoonTrends,
    IReadOnlyList<FightAnalysisBoonRowDto> TopBoons,
    FightAnalysisDifferenceReportDto WinLossDifferences);

public sealed record FightAnalysisFilterOptionsDto(
    IReadOnlyList<string> Commanders,
    IReadOnlyList<string> ClassOptions,
    string? MinFightDate,
    string? MaxFightDate,
    IReadOnlyList<PatchEraDto> PatchEras,
    IReadOnlyList<FightAttributeDefinitionDto> FightAttributes);

public sealed record FightAnalysisSelectionDto(
    string? Commander,
    string? StartDate,
    string? EndDate,
    string OutcomeCode,
    IReadOnlyList<string> SquadIncludeClasses,
    IReadOnlyList<string> SquadExcludeClasses,
    IReadOnlyList<string> EnemyIncludeClasses,
    IReadOnlyList<string> EnemyExcludeClasses,
    string PatchScope,
    IReadOnlyList<string> PatchEraIds,
    IReadOnlyList<string> FightAttributeKeys);

public sealed record FightAnalysisScopeDto(
    int TotalImportedFights,
    int FilteredFightCount,
    int WinCount,
    int LossCount,
    int DrawCount,
    double WinRatePercent,
    IReadOnlyList<FightAnalysisBreakdownDto> PatchEraBreakdown,
    IReadOnlyList<FightAnalysisBreakdownDto> AttributeBreakdown);

public sealed record FightAnalysisBreakdownDto(
    string Key,
    string Label,
    int FightCount);

public sealed record FightAnalysisOverviewDto(
    double? AverageOverallScore,
    string? AverageOverallGrade,
    double? AverageExpectedScore,
    double? AverageContextDelta,
    string? ContextDeltaConfidenceLabel,
    int ContextDeltaSampleCount,
    string? ContextDeltaDetail,
    double? AverageCohesionScore,
    double? AveragePressureScore,
    double? AverageDownstateScore,
    double? AverageSupportScore,
    double AverageSquadSize,
    double AverageEnemySize,
    double AverageDurationSeconds,
    FightAnalysisMitigationSummaryDto? MitigationSummary,
    FightAnalysisObliterateSummaryDto? ObliterateSummary);

public sealed record FightAnalysisDifferenceReportDto(
    int WinFightCount,
    int LossFightCount,
    string ConfidenceLabel,
    string Summary,
    IReadOnlyList<FightAnalysisDifferenceRowDto> TopSignals,
    IReadOnlyList<FightAnalysisDifferenceRowDto> ScoreDifferences,
    IReadOnlyList<FightAnalysisDifferenceRowDto> LaneDifferences,
    IReadOnlyList<FightAnalysisDifferenceRowDto> BoonDifferences,
    IReadOnlyList<FightAnalysisDifferenceRowDto> ClassDifferences,
    IReadOnlyList<FightAnalysisClassDifferenceRowDto> ClassDetails,
    IReadOnlyList<FightAnalysisDifferenceRowDto> EnemyDifferences,
    IReadOnlyList<FightAnalysisDifferenceRowDto> AttributeDifferences);

public sealed record FightAnalysisDifferenceRowDto(
    string Key,
    string Label,
    string Group,
    double? WinValue,
    double? LossValue,
    double? Delta,
    string Unit,
    int WinSampleCount,
    int LossSampleCount,
    string ConfidenceLabel,
    string DirectionLabel,
    string Detail);

public sealed record FightAnalysisClassDifferenceRowDto(
    string ClassLabel,
    int PresentFightCount,
    int PresentWinCount,
    int PresentLossCount,
    double WinWhenPresentPercent,
    double LossWhenPresentPercent,
    double ResultDelta,
    double AverageCountWhenPresent,
    double WinAverageCountWhenPresent,
    double LossAverageCountWhenPresent,
    double CountDeltaWhenPresent,
    double WinCoverageScore,
    double LossCoverageScore,
    double CoverageDelta,
    int WinCoverageSampleCount,
    int LossCoverageSampleCount,
    string ConfidenceLabel,
    string Detail);

public sealed record FightAnalysisMitigationSummaryDto(
    int AvailableFightCount,
    bool HasBarrierCoverageWarnings,
    int TotalSaves,
    int TotalBarrierSaves,
    int TotalDamageReductionSaves,
    int TotalNegatedDamageSaves,
    int TotalBothSaves,
    int TotalMultiSourceSaves,
    double TotalDamageToSquad,
    double TotalHealthDamageToSquad,
    double TotalBarrierAbsorbed,
    double TotalPetMinionAbsorption,
    double TotalEstimatedDamageReduction,
    double TotalEstimatedNegatedDamage,
    double TotalIncomingDamage,
    double TotalIncomingHealing,
    double? AverageLowestHealthPercent,
    double? LowestLowestHealthPercent,
    FightAnalysisBarrierOvercapSummaryDto? BarrierOvercap,
    FightAnalysisReflectSummaryDto? Reflects,
    FightAnalysisShieldOfCourageSummaryDto? ShieldOfCourage,
    IReadOnlyList<FightAnalysisNegatedHitSummaryDto> NegatedHitSummaries);

public sealed record FightAnalysisShieldOfCourageSummaryDto(
    int AvailableFightCount,
    int FightsWithBlockedAttacks,
    int BlockedAttackCount,
    double EstimatedBlockedDamage,
    int FallbackEstimateCount,
    int MaxCoveredPlayers);

public sealed record FightAnalysisBarrierOvercapSummaryDto(
    int AvailableFightCount,
    double RawBarrierEvaluated,
    double EstimatedOvercap,
    double OvercapPercentOfEvaluated,
    int EvaluatedApplicationGroups,
    int OvercapApplicationGroups,
    int HighConfidenceGroups,
    int EstimatedHealthPoolGroups,
    int SkippedNoBarrierStateGroups);

public sealed record FightAnalysisReflectSummaryDto(
    int AvailableFightCount,
    int TotalReflectedProjectiles,
    int TotalLandedHits,
    double TotalLandedDamage,
    int TotalEstimatedMitigatedProjectiles,
    double TotalEstimatedMitigatedDamage,
    int TotalUnestimatedMitigatedProjectiles,
    int TotalDowns,
    int TotalKills,
    FightAnalysisReflectSideSummaryDto SquadToEnemy,
    FightAnalysisReflectSideSummaryDto EnemyToSquad);

public sealed record FightAnalysisReflectSideSummaryDto(
    int ReflectedProjectiles,
    int LandedHits,
    double LandedDamage,
    int EstimatedMitigatedProjectiles,
    double EstimatedMitigatedDamage,
    int HighConfidenceMitigatedProjectiles,
    double HighConfidenceMitigatedDamage,
    int FallbackEstimatedMitigatedProjectiles,
    double FallbackEstimatedMitigatedDamage,
    int UnestimatedMitigatedProjectiles,
    int DownEvents,
    int KillEvents,
    int MatchedDamageEvents);

public sealed record FightAnalysisNegatedHitSummaryDto(
    string Key,
    string Label,
    int NegatedHitCount,
    double EstimatedPreventedDamage,
    int FallbackEstimateCount,
    IReadOnlyList<FightAnalysisEffectCountDto> ContributingEffects);

public sealed record FightAnalysisEffectCountDto(
    string Name,
    int Count);

public sealed record FightAnalysisObliterateSummaryDto(
    int AvailableFightCount,
    int FightsWithObliterateCount,
    double FightsWithObliteratePercent,
    int TotalHitCount,
    int TotalBarrierRemovedHitCount,
    double? BarrierRemovedRatePercent);

public sealed record FightAnalysisTrendPointDto(
    string FightId,
    string FightName,
    string FightDateLabel,
    string? FightDateUtc,
    string? Commander,
    string OutcomeLabel,
    string? PatchEraId,
    string? PatchEraLabel,
    IReadOnlyList<string> AttributeKeys,
    IReadOnlyList<string> AttributeLabels,
    int? OverallScore,
    double? ExpectedScore,
    double? ContextDelta,
    int ExpectedScoreSampleCount,
    string? ExpectedScoreConfidenceLabel,
    string? ExpectedScoreDetail,
    int? CohesionScore,
    int? PressureScore,
    int? DownstateScore,
    int? SupportScore);

public sealed record FightAnalysisTeamScoreTrendPointDto(
    string DateKey,
    string DateLabel,
    int FightCount,
    double AverageOverallScore);

public sealed record FightAnalysisBurstTrendPointDto(
    string FightId,
    string FightName,
    string FightDateLabel,
    string? FightDateUtc,
    string? PatchEraId,
    string? PatchEraLabel,
    FightAnalysisBurstSideTrendDto Squad,
    FightAnalysisBurstSideTrendDto Enemy);

public sealed record FightAnalysisBurstSideTrendDto(
    long? Damage,
    int? Strips,
    int? Downs,
    int? Kills);

public sealed record FightAnalysisTopFiveCategoryDto(
    string Key,
    string Label,
    string Unit,
    string Detail,
    IReadOnlyList<FightAnalysisTopFiveRowDto> Rows);

public sealed record FightAnalysisTopFiveRowDto(
    int Rank,
    string Account,
    string DisplayName,
    double Value,
    string? ValueDetail,
    int FightCount,
    int CharacterSampleCount,
    IReadOnlyList<string> ClassesPlayed,
    IReadOnlyList<FightAnalysisTopFiveCharacterDto> Characters);

public sealed record FightAnalysisTopFiveCharacterDto(
    string CharacterName,
    string ClassLabel,
    string? Icon,
    int FightCount,
    double Value);

public sealed record FightAnalysisPlayerSummaryRowDto(
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
    double AverageCorruptsPerFight,
    double StripCorruptPercent,
    double AverageOutgoingCleansesPerFight,
    double AverageHealingPerFight,
    double AverageBarrierPerFight,
    double AverageResurrectsPerFight,
    double AverageDeathsPerFight,
    double AverageRecoveriesPerFight,
    double? AverageInPositionRate,
    string? ContributionSummary,
    double AverageFightImpactScore,
    int FightImpactSampleCount,
    IReadOnlyList<string> CharacterNames,
    IReadOnlyList<FightAnalysisPlayerLaneSummaryDto> LaneSummaries);

public sealed record FightAnalysisPlayerLaneSummaryDto(
    string LaneKey,
    string LaneLabel,
    string CharacterName,
    string ClassLabel,
    int CharacterFightCount,
    int CharacterTotalFightCountAll,
    double CharacterWinRatePercent,
    double AverageStrengthPercent,
    double AverageSharePercent,
    double OverallStrengthPercent,
    double OverallSharePercent,
    double AppearanceRatePercent,
    int Samples,
    int TotalSamplesAll,
    double AverageCorruptsPerAppearance,
    double StripCorruptPercent);

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
    double AverageCorruptsPerFight,
    double StripCorruptPercent,
    double AverageOutgoingCleansesPerFight,
    double AverageHealingPerFight,
    double AverageBarrierPerFight,
    double AverageResurrectsPerFight,
    double AverageDeathsPerFight,
    double AverageRecoveriesPerFight,
    double? AverageInPositionRate,
    string? ContributionSummary,
    double AverageFightImpactScore,
    int FightImpactSampleCount,
    IReadOnlyList<FightAnalysisFightImpactLaneDto> FightImpactLanes,
    IReadOnlyList<string> CharacterNames,
    IReadOnlyList<FightAnalysisPlayerCharacterDto> Characters,
    IReadOnlyList<FightAnalysisCharacterImpactTrendDto> CharacterImpactTrends);

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
    double AverageStripsPerFight,
    double AverageCorruptsPerFight,
    double StripCorruptPercent,
    double AverageActivePresencePercent,
    double AverageEngagedPresencePercent,
    FightAnalysisCharacterPackageInputsDto PackageInputs,
    IReadOnlyList<string> EvidenceLines,
    double AverageFightImpactScore,
    int FightImpactSampleCount,
    IReadOnlyList<FightAnalysisFightImpactLaneDto> FightImpactLanes,
    IReadOnlyList<FightAnalysisCharacterContextFitDto> ContextFits,
    IReadOnlyList<FightAnalysisCharacterLaneContributionDto> LaneContributions);

public sealed record FightAnalysisCharacterContextFitDto(
    string Key,
    string Label,
    string Group,
    double Delta,
    double AverageFightImpactScore,
    double BaselineFightImpactScore,
    int SampleCount,
    string ConfidenceLabel,
    string Detail);

public sealed record FightAnalysisFightImpactLaneDto(
    string LaneKey,
    string LaneLabel,
    double AverageImpactScore,
    double AverageDemandWeightPercent,
    double AverageStrengthPercent,
    int Samples);

public sealed record FightAnalysisCharacterPackageInputsDto(
    double PressureStrength,
    double HealingPerFight,
    double CleansePerFight,
    double ProtectionGenerationPerFight,
    double ProtectionPresencePerFight,
    double StabilityGenerationPerFight,
    double StabilityPresencePerFight,
    double BarrierPerFight,
    double MightGenerationPerFight,
    double FuryGenerationPerFight,
    double FuryPresencePerFight,
    double QuicknessGenerationPerFight,
    double QuicknessPresencePerFight,
    double ResistanceGenerationPerFight,
    double ResistancePresencePerFight,
    double RegenerationGenerationPerFight,
    double RegenerationPresencePerFight,
    double StripPerFight,
    double ControlStrength,
    double EffectiveCrowdControlPerFight);

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
    int TotalSampleCountAll,
    int DistinctAccounts,
    int WinCount,
    int LossCount,
    int DrawCount,
    double WinRatePercent,
    double ContributionScore,
    double AverageStripsPerFight,
    double AverageCorruptsPerFight,
    double StripCorruptPercent,
    string? TopPlayerDisplayName,
    double AveragePrimaryLaneScore,
    double AverageWeightedLaneScore,
    double AverageFightCoverageScore,
    int FightCoverageSampleCount,
    IReadOnlyList<FightAnalysisClassFightCoverageLaneDto> FightCoverageLanes,
    IReadOnlyList<FightAnalysisCharacterLaneContributionDto> LaneContributions,
    IReadOnlyList<PatchImpactDto> PatchImpacts,
    IReadOnlyList<FightAnalysisClassPlayerRowDto> Players,
    IReadOnlyList<FightAnalysisCharacterImpactTrendDto> CharacterImpactTrends);

public sealed record FightAnalysisClassFightCoverageLaneDto(
    string LaneKey,
    string LaneLabel,
    double AverageCoverageScore,
    double AverageDemandWeightPercent,
    double AverageStrengthPercent,
    int Samples);

public sealed record FightAnalysisEnemyClassRowDto(
    string ClassLabel,
    string? Icon,
    int TotalCount,
    int FightCount,
    int PerformanceSampleCount,
    double? ThreatScore,
    double? AverageDps,
    double? BestDps,
    double? AverageStripsPerMinute,
    double? BestStripsPerMinute,
    int DamageBurstTopCount,
    int StripBurstTopCount);

public sealed record FightAnalysisCharacterImpactTrendDto(
    string Key,
    string CharacterName,
    string ClassLabel,
    string? Account,
    string Label,
    int FightCount,
    IReadOnlyList<FightAnalysisCharacterImpactTrendPointDto> Points);

public sealed record FightAnalysisCharacterImpactTrendPointDto(
    string DateKey,
    string DateLabel,
    int FightCount,
    double ImpactScore);

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
    double AverageFightImpactScore,
    int FightImpactSampleCount,
    string? PrimaryLaneLabel,
    double AveragePrimaryLaneScore,
    double AverageWeightedLaneScore,
    double AverageStripsPerFight,
    double AverageCorruptsPerFight,
    double StripCorruptPercent);

public sealed record FightAnalysisLaneRowDto(
    string LaneKey,
    string LaneLabel,
    int Samples,
    int DistinctAccounts,
    int DistinctClasses,
    double AverageStrengthPercent,
    double AverageSharePercent,
    double AppearanceRatePercent,
    double AverageCorruptsPerAppearance,
    double StripCorruptPercent,
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

public sealed record FightAnalysisBoonTrendDto(
    long Id,
    string Name,
    string? Icon,
    bool StackBased,
    IReadOnlyList<FightAnalysisBoonTrendPointDto> Points);

public sealed record FightAnalysisBoonTrendPointDto(
    string DateKey,
    string DateLabel,
    string BucketType,
    int FightCount,
    double AverageCoverage,
    double? AverageStacks,
    double? TeamScore,
    IReadOnlyList<FightAnalysisBoonTrendProviderDto> TopProviders);

public sealed record FightAnalysisBoonTrendProviderDto(
    string Label,
    string? Account,
    string? ClassLabel,
    int SampleCount,
    int ProviderAppearanceCount,
    double AverageGeneration,
    double? AverageGenerationPresence,
    double AverageOverstack,
    double ProviderScore);

public sealed record FightAnalysisBoonClassProviderDto(
    string ClassLabel,
    int SampleCount,
    int ProviderAppearanceCount,
    int DistinctAccounts,
    double AverageGeneration,
    double? AverageGenerationPresence,
    double AverageOverstack,
    double ProviderScore);
