namespace WvWAnalyst.Contracts;

public sealed record OutcomeObservationCachePackImportRequestDto(
    string DirectoryPath,
    bool DryRun = true,
    bool OverwriteExisting = false);

public sealed record OutcomeObservationCachePackImportResultDto(
    bool Success,
    string Message,
    bool DryRun,
    int DiscoveredCount,
    int ValidCount,
    int MatchedCount,
    int AttachedCount,
    int AlreadyCurrentCount,
    int UnmatchedCount,
    int InvalidCount,
    int ConflictCount,
    int DuplicateCount,
    IReadOnlyList<OutcomeObservationCachePackImportItemDto> Items);

public sealed record OutcomeObservationCachePackImportItemDto(
    string CachePath,
    string SourceFileName,
    string SourceFileSha256,
    string? FightId,
    string Action,
    string Message);
