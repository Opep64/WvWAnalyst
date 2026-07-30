namespace WvWAnalyst.Contracts;

public sealed record HistoricalEffectivenessSnapshotDto(
    string MethodVersion,
    string GeneratedAtUtc,
    FightAnalysisSelectionDto Selection,
    HistoricalEffectivenessScopeDto Scope,
    HistoricalEffectivenessMethodologyDto Methodology,
    IReadOnlyList<HistoricalEffectivenessStratumDto> Strata,
    IReadOnlyList<HistoricalEffectivenessReportDto> Reports);

public sealed record HistoricalEffectivenessScopeDto(
    int FilteredFightCount,
    int CacheFightCount,
    int MissingCacheFightCount,
    int ObservationCount,
    int SquadPerspectiveObservationCount,
    int EnemyPerspectiveObservationCount,
    IReadOnlyList<string> AvailabilityNotes);

public sealed record HistoricalEffectivenessMethodologyDto(
    string Summary,
    string Weighting,
    string ConfidenceIntervals,
    string EvidenceScore,
    string AssociationScore,
    string Interpretation,
    int MinimumPairedFights,
    int TargetPairedFights);

public sealed record HistoricalEffectivenessStratumDto(
    string Type,
    string Key,
    string Label,
    int FilteredFightCount,
    int CacheFightCount);

public sealed record HistoricalEffectivenessReportDto(
    string Key,
    string PerspectiveSideId,
    string OpposingSideId,
    string OutcomeFamily,
    string OutcomeLabel,
    string BaselineLabel,
    int OutcomeObservationCount,
    int BaselineObservationCount,
    int OutcomeFightCount,
    int BaselineFightCount,
    int PairedFightCount,
    string ConfidenceLabel,
    IReadOnlyList<HistoricalEffectivenessMetricDto> Metrics,
    IReadOnlyList<HistoricalEffectivenessNamedEffectDto> NamedEffects);

public sealed record HistoricalEffectivenessMetricDto(
    string Key,
    string Label,
    string Group,
    string Unit,
    bool Available,
    string? UnavailableReason,
    double? OutcomeAverage,
    double? BaselineAverage,
    double? Difference,
    double? PercentLift,
    double? Lower95Difference,
    double? Upper95Difference,
    double? PositiveDifferenceFightPercent,
    double? DirectionConsistencyPercent,
    double? StandardizedDifference,
    int OutcomeObservationCount,
    int BaselineObservationCount,
    int OutcomeFightCount,
    int BaselineFightCount,
    int PairedFightCount,
    int NonTieFightCount,
    double EvidenceScore,
    double AssociationScore,
    string ConfidenceLabel,
    string DirectionLabel,
    string Detail);

public sealed record HistoricalEffectivenessNamedEffectDto(
    int? Rank,
    string Key,
    string Name,
    string EffectType,
    long EffectId,
    string Unit,
    bool EligibleForRanking,
    string RankingResult,
    double? OutcomeAverage,
    double? BaselineAverage,
    double? Difference,
    double? PercentLift,
    double? OutcomePresencePercent,
    double? BaselinePresencePercent,
    double? PresenceDifferencePoints,
    double? OutcomeSecondaryAverage,
    double? BaselineSecondaryAverage,
    string? SecondaryUnit,
    double? Lower95Difference,
    double? Upper95Difference,
    double? DirectionConsistencyPercent,
    int OutcomeObservationCount,
    int BaselineObservationCount,
    int PairedFightCount,
    int NonTieFightCount,
    double EvidenceScore,
    double AssociationScore,
    string ConfidenceLabel,
    string DirectionLabel,
    string Detail);
