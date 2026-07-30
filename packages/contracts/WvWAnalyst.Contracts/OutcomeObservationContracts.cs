namespace WvWAnalyst.Contracts;

public sealed class FightOutcomeObservationCacheDto
{
    public int SchemaVersion { get; set; }

    public string FeatureVersion { get; set; } = string.Empty;

    public string FightId { get; set; } = string.Empty;

    public string SourceFileName { get; set; } = string.Empty;

    public string SourceFileSha256 { get; set; } = string.Empty;

    public string AnalystSchemaVersion { get; set; } = string.Empty;

    public string OutcomeMethodVersion { get; set; } = string.Empty;

    public string ParserVersion { get; set; } = string.Empty;

    public ulong GameBuild { get; set; }

    public string ArcVersion { get; set; } = string.Empty;

    public string GeneratedAtUtc { get; set; } = string.Empty;

    public int PressureWindowMs { get; set; }

    public int ControlSeparationMs { get; set; }

    public long? CompetitiveEndTimeMs { get; set; }

    public WvWAnalystOutcomeAnalysisAvailabilityDto Availability { get; set; } = new();

    public FightOutcomeObservationSummaryDto Summary { get; set; } = new();

    public IReadOnlyList<FightOutcomeObservationDto> Observations { get; set; } = Array.Empty<FightOutcomeObservationDto>();
}

public sealed class FightOutcomeObservationSummaryDto
{
    public int ObservationCount { get; set; }

    public int SquadPerspectiveCount { get; set; }

    public int EnemyPerspectiveCount { get; set; }

    public int DownOutcomeCount { get; set; }

    public int OrdinaryPressureCount { get; set; }

    public int ConversionCount { get; set; }

    public int RecoveryCount { get; set; }

    public int NamedConditionCount { get; set; }

    public int NamedCrowdControlCount { get; set; }
}

public sealed class FightOutcomeObservationDto
{
    public string ObservationId { get; set; } = string.Empty;

    public string OutcomeFamily { get; set; } = string.Empty;

    public string FeatureView { get; set; } = string.Empty;

    public string PerspectiveSideId { get; set; } = string.Empty;

    public string OpposingSideId { get; set; } = string.Empty;

    public string OutcomeCode { get; set; } = string.Empty;

    public bool IsOutcome { get; set; }

    public bool Succeeded { get; set; }

    public string EngagementId { get; set; } = string.Empty;

    public long ReferenceTimeMs { get; set; }

    public long WindowStartMs { get; set; }

    public long WindowEndMs { get; set; }

    public int OutcomeCount { get; set; }

    public IReadOnlyList<string> SourceEventIds { get; set; } = Array.Empty<string>();

    public IReadOnlyList<int> OutcomeActorIds { get; set; } = Array.Empty<int>();

    public FightOutcomeFeatureVectorDto Features { get; set; } = new();

    public IReadOnlyList<FightOutcomeConditionFeatureDto> Conditions { get; set; } = Array.Empty<FightOutcomeConditionFeatureDto>();

    public IReadOnlyList<FightOutcomeCrowdControlFeatureDto> CrowdControl { get; set; } = Array.Empty<FightOutcomeCrowdControlFeatureDto>();
}

public sealed class FightOutcomeFeatureVectorDto
{
    public int ActivePerspectivePlayers { get; set; }

    public int ObservedPerspectivePlayers { get; set; }

    public int ActiveOpposingPlayers { get; set; }

    public int ObservedOpposingPlayers { get; set; }

    public long PressureDamage { get; set; }

    public double PressureDamagePerActivePlayer { get; set; }

    public int OutcomeWindowDamage { get; set; }

    public int OutcomeWindowStrikeDamage { get; set; }

    public int OutcomeWindowConditionDamage { get; set; }

    public int OutcomeWindowBarrierDamage { get; set; }

    public int Strips { get; set; }

    public int Corrupts { get; set; }

    public double BoonRemovalPerActivePlayer { get; set; }

    public long Healing { get; set; }

    public double HealingPerActivePlayer { get; set; }

    public long Barrier { get; set; }

    public double BarrierPerActivePlayer { get; set; }

    public int Cleanses { get; set; }

    public double CleansesPerActivePlayer { get; set; }

    public double TopTargetShare { get; set; }

    public double TopThreeTargetShare { get; set; }

    public int TopTargetContributors { get; set; }

    public bool Focused { get; set; }

    public bool StripSynced { get; set; }

    public int TargetSaturationCount { get; set; }

    public int CrowdControlEvents { get; set; }

    public int EffectiveCrowdControlEvents { get; set; }

    public double CrowdControlDurationSeconds { get; set; }

    public double VulnerabilityBonusDamage { get; set; }

    public int DownedHealing { get; set; }

    public int DownedHealingEvents { get; set; }

    public int ResurrectionCasts { get; set; }

    public double ResurrectionCastDurationSeconds { get; set; }

    public int SupportContributors { get; set; }

    public int ClassRecoveryActions { get; set; }

    public bool SquadPositioningAvailable { get; set; }

    public double SquadInPositionRate { get; set; }

    public double SquadPositioningRiskRate { get; set; }
}

public sealed class FightOutcomeConditionFeatureDto
{
    public long BuffId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int ApplyCount { get; set; }

    public int ExtensionCount { get; set; }

    public double DirectDamage { get; set; }
}

public sealed class FightOutcomeCrowdControlFeatureDto
{
    public long SkillId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int EventCount { get; set; }

    public int EffectiveCount { get; set; }

    public double DurationSeconds { get; set; }
}
