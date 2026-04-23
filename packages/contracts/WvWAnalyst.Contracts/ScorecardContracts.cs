namespace WvWAnalyst.Contracts;

public sealed record TeamFightScorecardBlueprintDto(
    string Name,
    string Summary,
    IReadOnlyList<string> NonScoredContext,
    IReadOnlyList<string> NonScoredOutcomeHeadline,
    IReadOnlyList<ScorecardPillarDto> PrimaryPillars,
    IReadOnlyList<string> SupportingExplanationMetrics);

public sealed record ScorecardPillarDto(
    string Name,
    int WeightPercent,
    string Summary,
    IReadOnlyList<ScorecardMetricDto> Metrics);

public sealed record ScorecardMetricDto(
    string Name,
    string Direction,
    string Normalization,
    bool StrongForSingleFight,
    bool StrongForTrend);
