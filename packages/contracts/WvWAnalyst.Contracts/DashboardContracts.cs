namespace WvWAnalyst.Contracts;

public sealed record DashboardSnapshotDto(
    ApplicationInfoDto Application,
    WorkspaceStatusDto Workspace,
    StorageStatusDto Storage,
    TeamFightScorecardBlueprintDto TeamFightScorecard,
    IReadOnlyList<WorkstreamDto> Workstreams,
    IReadOnlyList<FightArtifactSummaryDto> RecentParses,
    FightBrowserSnapshotDto FightBrowser,
    ManageActivityStatusDto ManageActivity,
    ArtifactRetentionPolicyDto RetentionPolicy);

public sealed record ApplicationInfoDto(
    string Name,
    string Mode,
    string Summary,
    IReadOnlyList<string> Capabilities);

public sealed record WorkspaceStatusDto(
    string ParserPath,
    bool ParserDetected,
    string? ParserCliPath,
    bool ParserCliDetected,
    string CombinerPath,
    bool CombinerDetected,
    string? LogDirectoryPath,
    bool LogDirectoryConfigured,
    bool LogDirectoryDetected,
    string Notes);

public sealed record StorageStatusDto(
    string RootPath,
    string DatabasePath,
    string FightsPath,
    string CachePath,
    int FightFolderCount,
    long TotalBytes);

public sealed record WorkstreamDto(
    string Name,
    string Status,
    string Summary);

public sealed record FightArtifactSummaryDto(
    string FightId,
    string Status,
    int ArtifactCount,
    long TotalBytes,
    string Notes,
    string? SourceFileName,
    string? SourceFilePath,
    string? ImportedAtUtc,
    bool RawLogRetained,
    string? AnalysisJsonUrl,
    string? HtmlReportUrl,
    string? JsonReportUrl,
    string? ParserConsoleLogUrl,
    FightIndexDto? FightIndex);

public sealed record FightBrowserSnapshotDto(
    int TotalCount,
    int ImportedCount,
    int FailedCount,
    IReadOnlyList<FightArtifactSummaryDto> Fights);

public sealed record FightImportResultDto(
    bool Success,
    string Action,
    string? ReasonCode,
    string Message,
    FightArtifactSummaryDto? Fight,
    string? ParserExecutablePath,
    string? ParserStatus,
    long? ParserElapsedMilliseconds,
    IReadOnlyList<string> GeneratedFiles,
    IReadOnlyList<string> OutputExcerpt);

public sealed record DirectoryImportRequestDto(
    string DirectoryPath,
    string Mode,
    int? MaxParallelism);

public sealed record DirectoryImportResultDto(
    bool Success,
    string Message,
    string DirectoryPath,
    string Mode,
    int MaxParallelism,
    bool ResetCatalog,
    int DiscoveredCount,
    int ImportedCount,
    int SkippedCount,
    int ExcludedCount,
    int FailedCount,
    IReadOnlyList<DirectoryImportItemDto> Items);

public sealed record DirectoryImportJobStatusDto(
    string JobId,
    string State,
    string Message,
    string DirectoryPath,
    string Mode,
    int MaxParallelism,
    bool ResetCatalog,
    int DiscoveredCount,
    int CompletedCount,
    int ImportedCount,
    int SkippedCount,
    int ExcludedCount,
    int FailedCount,
    string? CurrentFileName,
    string? CurrentFilePath,
    string? StartedAtUtc,
    string? CompletedAtUtc,
    IReadOnlyList<DirectoryImportItemDto> Items);

public sealed record ManageActivityStatusDto(
    bool ParseRunning,
    bool UploadRunning,
    int ActiveUploadCount,
    string Summary,
    DirectoryImportJobStatusDto? ActiveBatchJob);

public sealed record ConfiguredLogDirectoryUploadResultDto(
    bool Success,
    string Message,
    string? DirectoryPath,
    int UploadedCount,
    int SavedCount,
    int SkippedCount,
    IReadOnlyList<ConfiguredLogDirectoryUploadItemDto> Items);

public sealed record ConfiguredLogDirectoryUploadItemDto(
    string FileName,
    string Action,
    string Message,
    string? SavedAs);

public sealed record WorkspaceResetResultDto(
    bool Success,
    string Message,
    string? DirectoryPath,
    int DeletedLogFileCount,
    int DeletedFightCount,
    int DeletedHtmlReportCount,
    bool DeletedDatabase);

public sealed record DirectoryImportItemDto(
    string SourceFileName,
    string SourceFilePath,
    string Action,
    string? ReasonCode,
    string Message,
    string? FightId,
    string? ParserStatus,
    long? ParserElapsedMilliseconds);

public sealed record FightDetailDto(
    string FightId,
    string Status,
    string SourceFileName,
    string? SourceFilePath,
    long SourceFileBytes,
    string ImportedAtUtc,
    bool Parsed,
    string ParserStatus,
    long ParserElapsedMilliseconds,
    string ParserExecutablePath,
    bool RawLogRetained,
    FightIndexDto? FightIndex,
    FightArtifactLinksDto ArtifactLinks,
    IReadOnlyList<string> GeneratedArtifacts);

public sealed record FightArtifactLinksDto(
    string? AnalysisJsonUrl,
    string? HtmlReportUrl,
    string? JsonReportUrl,
    string? ParserConsoleLogUrl,
    string? RawLogUrl);

public sealed record FightIndexDto(
    string FightName,
    string? EncounterName,
    string? EliteInsightsVersion,
    string? AnalystSchemaVersion,
    string IndexedFrom,
    int TriggerId,
    int? MapId,
    bool DetailedWvW,
    bool Success,
    FightOutcomeDto Outcome,
    FightSideIndexDto? SquadSide,
    FightSideIndexDto? EnemySide,
    FightCommanderIndexDto? CommanderSummary,
    FightDefenseSaveIndexDto? DefenseSaves,
    FightMitigationSummaryIndexDto? MitigationSummary,
    FightObliterateIndexDto? Obliterate,
    IReadOnlyList<FightThreatBoonIndexDto> ThreatBoons,
    IReadOnlyList<FightTopBurstIndexDto> TopBursts,
    IReadOnlyList<FightPlayerIndexDto> Players,
    FightExecutionIndexDto? Execution,
    string Duration,
    long? DurationMilliseconds,
    string TimeStart,
    string TimeEnd,
    string? TimeStartStandard,
    string? TimeEndStandard,
    int PlayerCount,
    int SquadPlayerCount,
    int FriendlyNonSquadCount,
    int EnemyTargetCount,
    int EnemyPlayerCount,
    int CommanderCount,
    int? FriendlyTeamId,
    IReadOnlyList<int> EnemyTeamIds,
    IReadOnlyList<string> CommanderDisplayNames,
    IReadOnlyList<string> ActiveExtensions,
    string? ArcVersion,
    int? GW2Build,
    string? RecordedBy,
    string? RecordedAccountBy);

public sealed record FightExecutionIndexDto(
    bool ScoreAvailable,
    int? OverallScore,
    string? Grade,
    string? ConfidenceLabel,
    IReadOnlyList<string> ConfidenceNotes,
    string? Summary,
    string? Detail,
    string? StrongestPillarLabel,
    string? StrongestPillarSummary,
    string? WeakestPillarLabel,
    string? WeakestPillarSummary,
    FightExecutionContextIndexDto? Context,
    FightExecutionOutcomeIndexDto? Outcome,
    IReadOnlyList<FightExecutionPillarIndexDto> Pillars);

public sealed record FightExecutionPillarIndexDto(
    string PillarId,
    string Label,
    int Score,
    string? Grade,
    int AdjustedScore,
    string? AdjustedGrade,
    bool AdjustmentApplied,
    string? AdjustmentDetail,
    string? Summary,
    string? Detail,
    int AvailableMetricCount,
    int MetricCount,
    IReadOnlyList<FightExecutionMetricIndexDto> Metrics);

public sealed record FightExecutionMetricIndexDto(
    string Label,
    string? Value,
    string? Note,
    bool Available,
    int Score);

public sealed record FightExecutionContextIndexDto(
    int SquadPlayerCount,
    int EnemyPlayerCount,
    int FriendlyNonSquadCount,
    string? PhaseDurationLabel,
    string? EnemyFormationStyleLabel,
    string? EnemyFormationStyleDetail,
    string? DataConfidenceLabel,
    string? DataConfidenceDetail);

public sealed record FightExecutionOutcomeIndexDto(
    int SquadDowns,
    int EnemyDowns,
    int SquadKills,
    int EnemyKills,
    int SquadDeaths,
    int EnemyDeaths,
    double EnemyDownConversionRate,
    double SquadRecoveryRate,
    string? WipeLabel);

public sealed record FightSideIndexDto(
    string SideId,
    string DisplayLabel,
    int PlayerCount,
    int FriendlyNonSquadCount,
    double EffectiveAlliedPlayerCount,
    double Dps,
    int Downs,
    int Kills,
    double DownKillConversionRate,
    int Cleanses,
    int Resurrects,
    int Deaths,
    long Damage,
    long DamageTaken,
    long Strips,
    int ReceivedCrowdControl,
    double StripsPerMinute,
    double CleansesPerMinute,
    IReadOnlyList<FightSideClassIndexDto> Classes);

public sealed record FightSideClassIndexDto(
    string ClassLabel,
    string? Icon,
    int Count);

public sealed record FightCommanderIndexDto(
    int ActorId,
    bool Available,
    long SquadPositioningSamples,
    double SquadInPositionRate,
    double SquadTooFarRate,
    double SquadOverextendedRate,
    double SquadLateralRiskRate,
    int? CohesionPillarScore,
    string? CohesionPillarSummary,
    string? FitSummary,
    string? DemandFitSummary,
    string? ContributionProfile,
    string? KeyContributionSummary,
    string? EvaluationConfidenceLabel,
    string? EvaluationConfidenceDetail,
    IReadOnlyList<string> EvaluationCaveats);

public sealed record FightDefenseSaveIndexDto(
    int SavedCases,
    int BarrierSavedCases,
    int DamageReductionSavedCases,
    int BothSavedCases,
    double TotalBarrierAbsorbed,
    double TotalEstimatedDamageReduction,
    double AverageLowestHealthPercent,
    double LowestLowestHealthPercent,
    double TotalIncomingDamage,
    double TotalIncomingHealing);

public sealed record FightMitigationSummaryIndexDto(
    bool HasBarrierData,
    bool BarrierCoverageMayBeIncomplete,
    double TotalDamageToSquad,
    double HealthDamageToSquad,
    double TotalBarrierAbsorbed,
    double BarrierAbsorptionPercent,
    double TotalPetMinionAbsorption,
    double PetMinionAbsorptionPercent,
    int SavedCases,
    int BarrierSavedCases,
    int DamageReductionSavedCases,
    int NegatedDamageSavedCases,
    int BothSavedCases,
    int MultiSourceSavedCases,
    double TotalBarrierAbsorbedInSaves,
    double TotalEstimatedDamageReduction,
    double TotalEstimatedNegatedDamage,
    double AverageLowestHealthPercent,
    double LowestLowestHealthPercent,
    double TotalIncomingDamage,
    double TotalIncomingHealing,
    IReadOnlyList<FightNegatedHitSummaryIndexDto> NegatedHitSummaries);

public sealed record FightNegatedHitSummaryIndexDto(
    string Key,
    string Label,
    int NegatedHitCount,
    double EstimatedPreventedDamage,
    int FallbackEstimateCount,
    IReadOnlyList<FightEffectCountSummaryIndexDto> ContributingEffects);

public sealed record FightEffectCountSummaryIndexDto(
    string Name,
    int Count);

public sealed record FightObliterateIndexDto(
    int HitCount,
    int BarrierRemovedHitCount);

public sealed record FightPlayerIndexDto(
    int ActorId,
    string? Account,
    string? Character,
    string? Profession,
    string? EliteSpec,
    string? Icon,
    int Group,
    bool IsCommander,
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
    int PositioningSamples,
    double InPositionRate,
    double TooFarRate,
    double OverextendedRate,
    double LateralRiskRate,
    string? FitSummary,
    string? DemandFitSummary,
    string? ContributionProfile,
    string? KeyContributionSummary,
    string? EvaluationConfidenceLabel,
    string? EvaluationConfidenceDetail,
    IReadOnlyList<string> EvaluationCaveats,
    IReadOnlyList<string> EvidenceSnapshot,
    IReadOnlyList<FightPlayerRoleMixIndexDto> RoleMix,
    IReadOnlyList<FightPlayerLaneIndexDto> Lanes,
    IReadOnlyList<FightPlayerThreatBoonIndexDto> ThreatBoons,
    IReadOnlyList<FightPlayerProvidedBoonIndexDto> ProvidedBoons);

public sealed record FightPlayerRoleMixIndexDto(
    string Label,
    double Percent);

public sealed record FightPlayerLaneIndexDto(
    string Key,
    string Label,
    double StrengthPercent,
    double SharePercent,
    int WindowsHit,
    int WindowsTotal,
    string? WindowLabel,
    string? RateBand,
    string? EvidenceLine,
    IReadOnlyList<FightPlayerLaneMetricIndexDto> Metrics);

public sealed record FightPlayerLaneMetricIndexDto(
    string Key,
    string Label,
    double Value,
    string? Unit,
    string? Aggregation);

public sealed record FightThreatBoonIndexDto(
    long Id,
    string Name,
    string? Icon,
    bool StackBased,
    bool TracksOverapplication,
    double Coverage,
    double AverageStacks,
    double Overapplication);

public sealed record FightTopBurstIndexDto(
    long Time,
    string? TimeLabel,
    long Damage,
    int Strips,
    int Downs,
    int Kills,
    FightTopBurstActorIndexDto? TopPressure,
    FightTopBurstActorIndexDto? TopStrips);

public sealed record FightTopBurstActorIndexDto(
    int ActorId,
    string? Account,
    string? Character,
    string? Profession,
    string? EliteSpec,
    string? Icon,
    double Amount);

public sealed record FightPlayerThreatBoonIndexDto(
    long Id,
    string Name,
    string? Icon,
    bool StackBased,
    bool TracksOverapplication,
    int ThreatenedSamples,
    int ActiveThreatSamples,
    double ThreatStackTotal,
    int OverappliedThreatSamples,
    double Coverage,
    double AverageStacks,
    double Overapplication);

public sealed record FightPlayerProvidedBoonIndexDto(
    long Id,
    string Name,
    string? Icon,
    bool StackBased,
    double Generation,
    double GenerationPresence,
    double Overstack);

public sealed record FightOutcomeDto(
    string OutcomeCode,
    string? WinnerSideId,
    string DisplayLabel,
    string DecidedBy,
    string Source,
    string Detail);

public sealed record ArtifactRetentionPolicyDto(
    string Summary,
    IReadOnlyList<string> KeepAlways,
    IReadOnlyList<string> PurgeFirst);
