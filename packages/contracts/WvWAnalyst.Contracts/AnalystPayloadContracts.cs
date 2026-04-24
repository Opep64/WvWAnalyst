namespace WvWAnalyst.Contracts;

public sealed class WvWAnalystFightPayloadDto
{
    public WvWAnalystMetaDto Meta { get; set; } = new();

    public WvWAnalystSourceDto Source { get; set; } = new();

    public WvWAnalystFightDto Fight { get; set; } = new();

    public WvWAnalystAvailabilityDto Availability { get; set; } = new();

    public WvWAnalystSideCollectionDto Sides { get; set; } = new();

    public WvWAnalystOutcomeDto Outcome { get; set; } = new();

    public WvWAnalystExecutionDto Execution { get; set; } = new();

    public WvWAnalystCommanderSummaryDto CommanderSummary { get; set; } = new();

    public WvWAnalystDefenseSaveSummaryDto? DefenseSaves { get; set; }

    public IReadOnlyList<WvWAnalystThreatBoonSummaryDto> ThreatBoons { get; set; } = Array.Empty<WvWAnalystThreatBoonSummaryDto>();

    public IReadOnlyList<WvWAnalystTopBurstDto> TopBursts { get; set; } = Array.Empty<WvWAnalystTopBurstDto>();

    public IReadOnlyList<WvWAnalystPlayerSummaryDto> Players { get; set; } = Array.Empty<WvWAnalystPlayerSummaryDto>();
}

public sealed class WvWAnalystMetaDto
{
    public string SchemaVersion { get; set; } = string.Empty;

    public string PayloadType { get; set; } = string.Empty;

    public string DetailLevel { get; set; } = string.Empty;

    public string GeneratedAtUtc { get; set; } = string.Empty;

    public string ParserVersion { get; set; } = string.Empty;
}

public sealed class WvWAnalystSourceDto
{
    public string SourceFileSha256 { get; set; } = string.Empty;

    public string SourceFileName { get; set; } = string.Empty;

    public string LogGuid { get; set; } = string.Empty;
}

public sealed class WvWAnalystFightDto
{
    public string FightId { get; set; } = string.Empty;

    public string Mode { get; set; } = string.Empty;

    public string MapCode { get; set; } = string.Empty;

    public string MapLabel { get; set; } = string.Empty;

    public string EncounterLabel { get; set; } = string.Empty;

    public string StartTimeUtc { get; set; } = string.Empty;

    public string EndTimeUtc { get; set; } = string.Empty;

    public long? DurationMs { get; set; }
}

public sealed class WvWAnalystAvailabilityDto
{
    public bool CombatReplay { get; set; }

    public bool HealingStats { get; set; }

    public bool BarrierStats { get; set; }

    public bool CrowdControlStats { get; set; }

    public bool CommanderDetected { get; set; }
}

public sealed class WvWAnalystSideCollectionDto
{
    public WvWAnalystSideDto Squad { get; set; } = new();

    public WvWAnalystSideDto Enemy { get; set; } = new();
}

public sealed class WvWAnalystSideDto
{
    public string SideId { get; set; } = string.Empty;

    public string DisplayLabel { get; set; } = string.Empty;

    public int PlayerCount { get; set; }

    public int FriendlyNonSquadCount { get; set; }

    public double EffectiveAlliedPlayerCount { get; set; }

    public WvWAnalystCommanderDto Commander { get; set; } = new();

    public WvWAnalystSideTotalsDto Totals { get; set; } = new();
}

public sealed class WvWAnalystCommanderDto
{
    public string Account { get; set; } = string.Empty;

    public string Character { get; set; } = string.Empty;

    public string Profession { get; set; } = string.Empty;

    public string EliteSpec { get; set; } = string.Empty;
}

public sealed class WvWAnalystSideTotalsDto
{
    public double Dps { get; set; }

    public int Downs { get; set; }

    public int Kills { get; set; }

    public double DownKillConversionRate { get; set; }

    public int Cleanses { get; set; }

    public int Resurrects { get; set; }

    public int Deaths { get; set; }

    public long Damage { get; set; }

    public long DamageTaken { get; set; }

    public long Strips { get; set; }

    public int ReceivedCrowdControl { get; set; }

    public double StripsPerMinute { get; set; }

    public double CleansesPerMinute { get; set; }
}

public sealed class WvWAnalystOutcomeDto
{
    public string OutcomeCode { get; set; } = string.Empty;

    public string WinnerSideId { get; set; } = string.Empty;

    public string DisplayLabel { get; set; } = string.Empty;

    public string DecidedBy { get; set; } = string.Empty;

    public IReadOnlyList<string> TieBreakOrder { get; set; } = Array.Empty<string>();
}

public sealed class WvWAnalystExecutionDto
{
    public bool ScoreAvailable { get; set; }

    public int? OverallScore { get; set; }

    public string Grade { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public string StrongestPillarLabel { get; set; } = string.Empty;

    public string StrongestPillarSummary { get; set; } = string.Empty;

    public string WeakestPillarLabel { get; set; } = string.Empty;

    public string WeakestPillarSummary { get; set; } = string.Empty;

    public WvWAnalystExecutionConfidenceDto Confidence { get; set; } = new();

    public WvWAnalystExecutionContextDto Context { get; set; } = new();

    public WvWAnalystExecutionOutcomeDto Outcome { get; set; } = new();

    public IReadOnlyList<WvWAnalystExecutionPillarDto> Pillars { get; set; } = Array.Empty<WvWAnalystExecutionPillarDto>();
}

public sealed class WvWAnalystExecutionConfidenceDto
{
    public string Label { get; set; } = string.Empty;

    public int AvailableMetricCount { get; set; }

    public int TotalMetricCount { get; set; }

    public IReadOnlyList<string> Notes { get; set; } = Array.Empty<string>();
}

public sealed class WvWAnalystExecutionContextDto
{
    public int SquadPlayerCount { get; set; }

    public int EnemyPlayerCount { get; set; }

    public int FriendlyNonSquadCount { get; set; }

    public string PhaseDurationLabel { get; set; } = string.Empty;

    public string EnemyFormationStyleCode { get; set; } = string.Empty;

    public string EnemyFormationStyleLabel { get; set; } = string.Empty;

    public string EnemyFormationStyleDetail { get; set; } = string.Empty;

    public string DataConfidenceLabel { get; set; } = string.Empty;

    public string DataConfidenceDetail { get; set; } = string.Empty;
}

public sealed class WvWAnalystExecutionOutcomeDto
{
    public int SquadDowns { get; set; }

    public int EnemyDowns { get; set; }

    public int SquadKills { get; set; }

    public int EnemyKills { get; set; }

    public int SquadDeaths { get; set; }

    public int EnemyDeaths { get; set; }

    public double EnemyDownConversionRate { get; set; }

    public double SquadRecoveryRate { get; set; }

    public string WipeLabel { get; set; } = string.Empty;
}

public sealed class WvWAnalystExecutionPillarDto
{
    public string PillarId { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public int Score { get; set; }

    public string Grade { get; set; } = string.Empty;

    public int AdjustedScore { get; set; }

    public string AdjustedGrade { get; set; } = string.Empty;

    public bool AdjustmentApplied { get; set; }

    public string AdjustmentDetail { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public int AvailableMetricCount { get; set; }

    public int MetricCount { get; set; }

    public IReadOnlyList<WvWAnalystExecutionMetricDto> Metrics { get; set; } = Array.Empty<WvWAnalystExecutionMetricDto>();
}

public sealed class WvWAnalystExecutionMetricDto
{
    public string MetricId { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public bool HigherIsBetter { get; set; }

    public string Value { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    public bool Available { get; set; }

    public bool Neutralized { get; set; }

    public int Score { get; set; }

    public WvWAnalystExecutionMetricValueDto Values { get; set; } = new();
}

public sealed class WvWAnalystExecutionMetricValueDto
{
    public double? Squad { get; set; }

    public double? Enemy { get; set; }
}

public sealed class WvWAnalystCommanderSummaryDto
{
    public int ActorId { get; set; }

    public bool Available { get; set; }

    public long SquadPositioningSamples { get; set; }

    public double SquadInPositionRate { get; set; }

    public double SquadTooFarRate { get; set; }

    public double SquadOverextendedRate { get; set; }

    public double SquadLateralRiskRate { get; set; }

    public int? CohesionPillarScore { get; set; }

    public string CohesionPillarSummary { get; set; } = string.Empty;

    public string FitSummary { get; set; } = string.Empty;

    public string DemandFitSummary { get; set; } = string.Empty;

    public string ContributionProfile { get; set; } = string.Empty;

    public string KeyContributionSummary { get; set; } = string.Empty;

    public string EvaluationConfidenceLabel { get; set; } = string.Empty;

    public string EvaluationConfidenceDetail { get; set; } = string.Empty;

    public IReadOnlyList<string> EvaluationCaveats { get; set; } = Array.Empty<string>();
}

public sealed class WvWAnalystDefenseSaveSummaryDto
{
    public int SavedCases { get; set; }

    public int BarrierSavedCases { get; set; }

    public int DamageReductionSavedCases { get; set; }

    public int BothSavedCases { get; set; }

    public double TotalBarrierAbsorbed { get; set; }

    public double TotalEstimatedDamageReduction { get; set; }

    public double AverageLowestHealthPercent { get; set; }

    public double LowestLowestHealthPercent { get; set; }

    public double TotalIncomingDamage { get; set; }

    public double TotalIncomingHealing { get; set; }
}

public sealed class WvWAnalystPlayerSummaryDto
{
    public int ActorId { get; set; }

    public string Account { get; set; } = string.Empty;

    public string Character { get; set; } = string.Empty;

    public string Profession { get; set; } = string.Empty;

    public string EliteSpec { get; set; } = string.Empty;

    public string Icon { get; set; } = string.Empty;

    public int Group { get; set; }

    public bool IsCommander { get; set; }

    public double ActiveSeconds { get; set; }

    public double CombatSeconds { get; set; }

    public long Damage { get; set; }

    public int Downs { get; set; }

    public int Kills { get; set; }

    public int Strips { get; set; }

    public int OutgoingCleanses { get; set; }

    public long Healing { get; set; }

    public long Barrier { get; set; }

    public int Resurrects { get; set; }

    public int Deaths { get; set; }

    public int Recoveries { get; set; }

    public long DamageTaken { get; set; }

    public int ReceivedCrowdControl { get; set; }

    public bool HasPositioningData { get; set; }

    public int PositioningSamples { get; set; }

    public double InPositionRate { get; set; }

    public double TooFarRate { get; set; }

    public double OverextendedRate { get; set; }

    public double LateralRiskRate { get; set; }

    public string FitSummary { get; set; } = string.Empty;

    public string DemandFitSummary { get; set; } = string.Empty;

    public string ContributionProfile { get; set; } = string.Empty;

    public string KeyContributionSummary { get; set; } = string.Empty;

    public string EvaluationConfidenceLabel { get; set; } = string.Empty;

    public string EvaluationConfidenceDetail { get; set; } = string.Empty;

    public IReadOnlyList<string> EvaluationCaveats { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> EvidenceSnapshot { get; set; } = Array.Empty<string>();

    public IReadOnlyList<WvWAnalystPlayerRoleMixEntryDto> RoleMix { get; set; } = Array.Empty<WvWAnalystPlayerRoleMixEntryDto>();

    public IReadOnlyList<WvWAnalystPlayerLaneSummaryDto> Lanes { get; set; } = Array.Empty<WvWAnalystPlayerLaneSummaryDto>();

    public IReadOnlyList<WvWAnalystPlayerThreatBoonSummaryDto> ThreatBoons { get; set; } = Array.Empty<WvWAnalystPlayerThreatBoonSummaryDto>();

    public IReadOnlyList<WvWAnalystPlayerProvidedBoonSummaryDto> ProvidedBoons { get; set; } = Array.Empty<WvWAnalystPlayerProvidedBoonSummaryDto>();
}

public sealed class WvWAnalystPlayerRoleMixEntryDto
{
    public string Label { get; set; } = string.Empty;

    public double Percent { get; set; }
}

public sealed class WvWAnalystPlayerLaneSummaryDto
{
    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public double StrengthPercent { get; set; }

    public double SharePercent { get; set; }

    public int WindowsHit { get; set; }

    public int WindowsTotal { get; set; }

    public string WindowLabel { get; set; } = string.Empty;

    public string RateBand { get; set; } = string.Empty;

    public string EvidenceLine { get; set; } = string.Empty;

    public IReadOnlyList<WvWAnalystPlayerLaneMetricDto> Metrics { get; set; } = Array.Empty<WvWAnalystPlayerLaneMetricDto>();
}

public sealed class WvWAnalystPlayerLaneMetricDto
{
    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public double Value { get; set; }

    public string Unit { get; set; } = string.Empty;

    public string Aggregation { get; set; } = string.Empty;
}

public sealed class WvWAnalystThreatBoonSummaryDto
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Icon { get; set; } = string.Empty;

    public bool StackBased { get; set; }

    public bool TracksOverapplication { get; set; }

    public double Coverage { get; set; }

    public double AverageStacks { get; set; }

    public double Overapplication { get; set; }
}

public sealed class WvWAnalystTopBurstDto
{
    public long Time { get; set; }

    public string TimeLabel { get; set; } = string.Empty;

    public long Damage { get; set; }

    public int Strips { get; set; }

    public int Downs { get; set; }

    public int Kills { get; set; }

    public WvWAnalystTopBurstActorDto TopPressure { get; set; } = new();

    public WvWAnalystTopBurstActorDto TopStrips { get; set; } = new();
}

public sealed class WvWAnalystTopBurstActorDto
{
    public int ActorId { get; set; }

    public string Account { get; set; } = string.Empty;

    public string Character { get; set; } = string.Empty;

    public string Profession { get; set; } = string.Empty;

    public string EliteSpec { get; set; } = string.Empty;

    public string Icon { get; set; } = string.Empty;

    public double Amount { get; set; }
}

public sealed class WvWAnalystPlayerThreatBoonSummaryDto
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Icon { get; set; } = string.Empty;

    public bool StackBased { get; set; }

    public bool TracksOverapplication { get; set; }

    public int ThreatenedSamples { get; set; }

    public int ActiveThreatSamples { get; set; }

    public double ThreatStackTotal { get; set; }

    public int OverappliedThreatSamples { get; set; }

    public double Coverage { get; set; }

    public double AverageStacks { get; set; }

    public double Overapplication { get; set; }
}

public sealed class WvWAnalystPlayerProvidedBoonSummaryDto
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Icon { get; set; } = string.Empty;

    public bool StackBased { get; set; }

    public double Generation { get; set; }

    public double GenerationPresence { get; set; }

    public double Overstack { get; set; }
}
