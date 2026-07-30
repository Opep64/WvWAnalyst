using WvWAnalyst.Api.Bridge;
using WvWAnalyst.Contracts;

namespace WvWAnalyst.Api.Analysis;

public sealed class OutcomeObservationCachePackImportService
{
    private readonly FightCatalogService _catalog;
    private readonly FightOutcomeObservationCacheService _cacheService;
    private readonly ILogger<OutcomeObservationCachePackImportService> _logger;

    public OutcomeObservationCachePackImportService(
        FightCatalogService catalog,
        FightOutcomeObservationCacheService cacheService,
        ILogger<OutcomeObservationCachePackImportService> logger)
    {
        _catalog = catalog;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<OutcomeObservationCachePackImportResultDto> ImportAsync(
        OutcomeObservationCachePackImportRequestDto request,
        CancellationToken cancellationToken)
    {
        string rootPath;
        try
        {
            rootPath = Path.GetFullPath(request.DirectoryPath ?? string.Empty);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure(request, "The cache-pack directory path is invalid.");
        }

        if (!Directory.Exists(rootPath))
        {
            return Failure(request, "The cache-pack directory does not exist.");
        }

        string[] cachePaths;
        try
        {
            cachePaths = Directory
                .EnumerateFiles(rootPath, "outcome-observations.json.gz", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Unable to enumerate outcome-observation cache pack at {RootPath}.", rootPath);
            return Failure(request, "The cache-pack directory could not be read.");
        }

        var candidates = new List<ImportCandidate>(cachePaths.Length);
        var items = new List<OutcomeObservationCachePackImportItemDto>(cachePaths.Length);
        var validCaches = new List<(string CachePath, FightOutcomeObservationCacheDto Cache)>(cachePaths.Length);
        var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string cachePath in cachePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FightOutcomeObservationCacheDto? cache = _cacheService.TryRead(cachePath);
            string validationMessage = Validate(cache);
            if (validationMessage.Length > 0)
            {
                items.Add(Item(cachePath, cache, null, "invalid", validationMessage));
                continue;
            }

            if (!seenHashes.Add(cache!.SourceFileSha256))
            {
                items.Add(Item(
                    cachePath,
                    cache,
                    null,
                    "duplicate",
                    "Another cache in this pack has the same source-file hash."));
                continue;
            }

            validCaches.Add((cachePath, cache));
        }

        IReadOnlyDictionary<string, FightArtifactManifest> manifests =
            _catalog.FindReplacementFightsBySourceHash(seenHashes);
        foreach ((string cachePath, FightOutcomeObservationCacheDto cache) in validCaches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            manifests.TryGetValue(cache.SourceFileSha256, out FightArtifactManifest? manifest);
            if (manifest is null ||
                !manifest.Parsed ||
                !string.Equals(manifest.SourceFileSha256, cache.SourceFileSha256, StringComparison.OrdinalIgnoreCase))
            {
                items.Add(Item(
                    cachePath,
                    cache,
                    null,
                    "unmatched",
                    "No successfully imported fight has this source-file hash."));
                continue;
            }

            string? existingPath = ResolveExistingCachePath(manifest);
            if (existingPath is not null)
            {
                FightOutcomeObservationCacheDto? existing = _cacheService.TryRead(existingPath);
                if (CachesMatch(existing, cache))
                {
                    items.Add(Item(
                        cachePath,
                        cache,
                        manifest.FightId,
                        "already-current",
                        "The matching fight already has this derived cache."));
                    continue;
                }

                if (!request.OverwriteExisting)
                {
                    items.Add(Item(
                        cachePath,
                        cache,
                        manifest.FightId,
                        "conflict",
                        "The matching fight already has a different derived cache; overwrite was not authorized."));
                    continue;
                }
            }

            candidates.Add(new ImportCandidate(cachePath, cache, manifest));
        }

        bool hasBlockingEntry = items.Any(item =>
            item.Action is "invalid" or "duplicate" or "unmatched" or "conflict");
        if (!request.DryRun && hasBlockingEntry)
        {
            items.AddRange(candidates.Select(candidate => Item(
                candidate.CachePath,
                candidate.Cache,
                candidate.Manifest.FightId,
                "blocked",
                "Not attached because another cache in the pack failed preflight.")));
            candidates.Clear();
        }

        if (request.DryRun)
        {
            items.AddRange(candidates.Select(candidate => Item(
                candidate.CachePath,
                candidate.Cache,
                candidate.Manifest.FightId,
                "attach",
                "Verified and ready to attach.")));
        }
        else
        {
            foreach (ImportCandidate candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    FightArtifactManifest currentManifest =
                        _catalog.TryLoadManifestForUpdate(candidate.Manifest.FightId) ?? candidate.Manifest;
                    string fightDirectoryPath = _catalog.GetFightDirectoryPath(currentManifest.FightId);
                    string? relativePath = await _cacheService.WriteAsync(
                        fightDirectoryPath,
                        candidate.Cache,
                        currentManifest.FightId,
                        currentManifest.SourceFileName,
                        currentManifest.SourceFileSha256!,
                        cancellationToken);

                    await _catalog.WriteManifestAsync(
                        currentManifest with { OutcomeObservationCacheRelativePath = relativePath },
                        cancellationToken);
                    items.Add(Item(
                        candidate.CachePath,
                        candidate.Cache,
                        currentManifest.FightId,
                        "attached",
                        "Derived cache attached to the existing fight."));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    _logger.LogError(
                        exception,
                        "Unable to attach outcome-observation cache {CachePath} to fight {FightId}.",
                        candidate.CachePath,
                        candidate.Manifest.FightId);
                    items.Add(Item(
                        candidate.CachePath,
                        candidate.Cache,
                        candidate.Manifest.FightId,
                        "failed",
                        "The derived cache could not be written."));
                }
            }
        }

        items = items
            .OrderBy(item => item.SourceFileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.CachePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int attachedCount = items.Count(item => item.Action == "attached");
        int readyCount = items.Count(item => item.Action == "attach");
        int invalidCount = items.Count(item => item.Action is "invalid" or "failed");
        int conflictCount = items.Count(item => item.Action == "conflict");
        int unmatchedCount = items.Count(item => item.Action == "unmatched");
        int duplicateCount = items.Count(item => item.Action == "duplicate");
        bool success =
            invalidCount == 0 &&
            conflictCount == 0 &&
            unmatchedCount == 0 &&
            duplicateCount == 0;
        string message = hasBlockingEntry
            ? "The cache pack failed preflight; no verified entries from this request were attached."
            : request.DryRun
                ? $"{readyCount} cache(s) are verified and ready to attach; no files were changed."
                : $"{attachedCount} cache(s) were attached to existing fights.";

        return new OutcomeObservationCachePackImportResultDto(
            Success: success,
            Message: message,
            DryRun: request.DryRun,
            DiscoveredCount: cachePaths.Length,
            ValidCount: cachePaths.Length - items.Count(item => item.Action == "invalid"),
            MatchedCount: items.Count(item => item.FightId is not null),
            AttachedCount: attachedCount,
            AlreadyCurrentCount: items.Count(item => item.Action == "already-current"),
            UnmatchedCount: unmatchedCount,
            InvalidCount: invalidCount,
            ConflictCount: conflictCount,
            DuplicateCount: duplicateCount,
            Items: items);
    }

    private string? ResolveExistingCachePath(FightArtifactManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.OutcomeObservationCacheRelativePath))
        {
            return null;
        }

        string fightDirectoryPath = _catalog.GetFightDirectoryPath(manifest.FightId);
        string fullPath = Path.GetFullPath(
            Path.Combine(fightDirectoryPath, manifest.OutcomeObservationCacheRelativePath));
        string rootPath = Path.GetFullPath(fightDirectoryPath) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath)
            ? fullPath
            : null;
    }

    private static bool CachesMatch(
        FightOutcomeObservationCacheDto? existing,
        FightOutcomeObservationCacheDto candidate)
    {
        return existing is not null &&
            existing.SchemaVersion == candidate.SchemaVersion &&
            string.Equals(existing.FeatureVersion, candidate.FeatureVersion, StringComparison.Ordinal) &&
            string.Equals(existing.SourceFileSha256, candidate.SourceFileSha256, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.OutcomeMethodVersion, candidate.OutcomeMethodVersion, StringComparison.Ordinal) &&
            string.Equals(existing.ParserVersion, candidate.ParserVersion, StringComparison.Ordinal) &&
            existing.GameBuild == candidate.GameBuild &&
            existing.Summary.ObservationCount == candidate.Summary.ObservationCount &&
            existing.Observations.Count == candidate.Observations.Count;
    }

    private static string Validate(FightOutcomeObservationCacheDto? cache)
    {
        if (cache is null)
        {
            return "The file is not a readable outcome-observation cache.";
        }
        if (cache.SchemaVersion != FightOutcomeObservationCacheService.CurrentSchemaVersion ||
            !string.Equals(
                cache.FeatureVersion,
                FightOutcomeObservationCacheService.CurrentFeatureVersion,
                StringComparison.Ordinal))
        {
            return "The cache schema or feature version is not supported.";
        }
        if (string.IsNullOrWhiteSpace(cache.SourceFileSha256))
        {
            return "The cache has no source-file hash.";
        }
        if (cache.SourceFileSha256.Length != 64 ||
            cache.SourceFileSha256.Any(character => !Uri.IsHexDigit(character)))
        {
            return "The cache source-file hash is not a valid SHA-256 value.";
        }
        if (cache.Observations is null || cache.Summary is null ||
            cache.Summary.ObservationCount != cache.Observations.Count)
        {
            return "The cache observation count is inconsistent.";
        }
        return string.Empty;
    }

    private static OutcomeObservationCachePackImportResultDto Failure(
        OutcomeObservationCachePackImportRequestDto request,
        string message) =>
        new(
            Success: false,
            Message: message,
            DryRun: request.DryRun,
            DiscoveredCount: 0,
            ValidCount: 0,
            MatchedCount: 0,
            AttachedCount: 0,
            AlreadyCurrentCount: 0,
            UnmatchedCount: 0,
            InvalidCount: 0,
            ConflictCount: 0,
            DuplicateCount: 0,
            Items: []);

    private static OutcomeObservationCachePackImportItemDto Item(
        string cachePath,
        FightOutcomeObservationCacheDto? cache,
        string? fightId,
        string action,
        string message) =>
        new(
            CachePath: cachePath,
            SourceFileName: cache?.SourceFileName ?? Path.GetFileName(cachePath),
            SourceFileSha256: cache?.SourceFileSha256 ?? string.Empty,
            FightId: fightId,
            Action: action,
            Message: message);

    private sealed record ImportCandidate(
        string CachePath,
        FightOutcomeObservationCacheDto Cache,
        FightArtifactManifest Manifest);
}
