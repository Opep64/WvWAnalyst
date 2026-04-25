using System.Globalization;
using System.Text.Json;
using WvWAnalyst.Api.Analysis;
using WvWAnalyst.Api.Services;
using WvWAnalyst.Contracts;

namespace WvWAnalyst.Api.Bridge;

public sealed class FightCatalogService
{
    private const int CurrentFightIndexSchemaVersion = 8;

    private static readonly JsonSerializerOptions ManifestSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AppPathService _paths;
    private readonly EliteInsightsFightIndexer _fightIndexer;
    private readonly PatchMetadataService _patchMetadata;
    private readonly FightAttributeService _fightAttributes;
    private readonly object _cacheLock = new();
    private IReadOnlyList<FightArtifactSummaryDto>? _cachedCanonicalSummaries;
    private FightBrowserSnapshotDto? _cachedFightBrowserSnapshot;

    public FightCatalogService(
        AppPathService paths,
        EliteInsightsFightIndexer fightIndexer,
        PatchMetadataService patchMetadata,
        FightAttributeService fightAttributes)
    {
        _paths = paths;
        _fightIndexer = fightIndexer;
        _patchMetadata = patchMetadata;
        _fightAttributes = fightAttributes;
    }

    public string GetFightDirectoryPath(string fightId)
    {
        ValidateFightId(fightId);
        return Path.Combine(_paths.FightsPath, fightId);
    }

    public string CreateFightId(string sourceFileName)
    {
        var stem = Path.GetFileNameWithoutExtension(sourceFileName);
        var slug = Slugify(stem);
        if (slug.Length == 0)
        {
            slug = "fight";
        }

        return $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{slug}";
    }

    public async Task WriteManifestAsync(FightArtifactManifest manifest, CancellationToken cancellationToken)
    {
        var fightDirectoryPath = GetFightDirectoryPath(manifest.FightId);
        Directory.CreateDirectory(fightDirectoryPath);

        var manifestPath = Path.Combine(fightDirectoryPath, "manifest.json");
        await using var stream = new FileStream(manifestPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, manifest, ManifestSerializerOptions, cancellationToken);
        InvalidateCatalogCache();
    }

    public FightArtifactManifest? TryLoadManifest(string fightId)
    {
        try
        {
            var fightDirectoryPath = GetFightDirectoryPath(fightId);
            var manifestPath = Path.Combine(fightDirectoryPath, "manifest.json");
            return TryLoadManifestFromPath(fightDirectoryPath, manifestPath, hydrateDerivedData: true);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public bool TryGetFightSummary(string fightId, out FightArtifactSummaryDto summary)
    {
        summary = default!;

        try
        {
            var directoryPath = GetFightDirectoryPath(fightId);
            var directory = new DirectoryInfo(directoryPath);
            if (!directory.Exists)
            {
                return false;
            }

            var manifest = TryLoadManifest(fightId);
            summary = BuildSummary(directory, manifest);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public IReadOnlyList<FightArtifactSummaryDto> GetRecentParseSummaries(int maxCount)
    {
        return GetCanonicalSummaries()
            .OrderByDescending(item => ParseSummaryImportedAtUtc(item))
            .Take(maxCount)
            .ToList();
    }

    public FightBrowserSnapshotDto GetFightBrowserSnapshot()
    {
        lock (_cacheLock)
        {
            _cachedCanonicalSummaries ??= BuildCanonicalSummaries();
            _cachedFightBrowserSnapshot ??= BuildFightBrowserSnapshot(_cachedCanonicalSummaries);
            return _cachedFightBrowserSnapshot;
        }
    }

    public FightArtifactManifest? TryFindReplacementFight(string? sourceFileSha256, string? fightFingerprint)
    {
        var manifests = EnumerateCatalogItems()
            .Select(item => item.Manifest)
            .Where(manifest => manifest is not null)
            .Cast<FightArtifactManifest>();

        if (!string.IsNullOrWhiteSpace(sourceFileSha256))
        {
            var hashMatch = manifests
                .Where(manifest => string.Equals(manifest.SourceFileSha256, sourceFileSha256, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(manifest => manifest.ImportedAtUtc)
                .FirstOrDefault();

            if (hashMatch is not null)
            {
                return hashMatch;
            }
        }

        if (!string.IsNullOrWhiteSpace(fightFingerprint))
        {
            return manifests
                .Where(manifest => string.Equals(manifest.FightFingerprint, fightFingerprint, StringComparison.Ordinal))
                .OrderByDescending(manifest => manifest.ImportedAtUtc)
                .FirstOrDefault();
        }

        return null;
    }

    public HashSet<string> GetKnownSuccessfulSourceHashes()
    {
        return EnumerateCatalogItems()
            .Select(item => item.Manifest)
            .Where(manifest =>
                manifest is not null &&
                manifest.Parsed &&
                !string.IsNullOrWhiteSpace(manifest.SourceFileSha256))
            .Select(manifest => manifest!.SourceFileSha256!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public void ResetCatalog()
    {
        _paths.EnsureStorageDirectories();

        var fightsRoot = new DirectoryInfo(_paths.FightsPath);
        if (fightsRoot.Exists)
        {
            foreach (var directory in fightsRoot.EnumerateDirectories())
            {
                directory.Delete(recursive: true);
            }
        }

        var databasePath = _paths.DatabasePath;
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }

        InvalidateCatalogCache();
    }

    public bool TryGetFightDetail(string fightId, out FightDetailDto detail)
    {
        detail = default!;
        var manifest = TryLoadManifest(fightId);
        if (manifest is null)
        {
            return false;
        }

        detail = BuildDetail(manifest);
        return true;
    }

    public bool TryGetArtifact(string fightId, FightArtifactKind kind, out string artifactPath, out string contentType)
    {
        artifactPath = string.Empty;
        contentType = "application/octet-stream";

        var manifest = TryLoadManifest(fightId);
        if (manifest is null)
        {
            return false;
        }

        var relativePath = kind switch
        {
            FightArtifactKind.AnalysisJson => manifest.AnalysisJsonArtifactRelativePath,
            FightArtifactKind.Html => manifest.HtmlArtifactRelativePath,
            FightArtifactKind.Json => manifest.JsonArtifactRelativePath,
            FightArtifactKind.ParserConsoleLog => manifest.ParserConsoleLogRelativePath,
            FightArtifactKind.RawLog => manifest.RawLogRelativePath,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var resolvedPath = ResolveArtifactPath(GetFightDirectoryPath(fightId), relativePath);
        if (resolvedPath is null)
        {
            return false;
        }

        artifactPath = resolvedPath;
        contentType = kind switch
        {
            FightArtifactKind.AnalysisJson => resolvedPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? "application/gzip" : "application/json",
            FightArtifactKind.Html => "text/html; charset=utf-8",
            FightArtifactKind.Json => resolvedPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? "application/gzip" : "application/json",
            FightArtifactKind.ParserConsoleLog => "text/plain; charset=utf-8",
            FightArtifactKind.RawLog when resolvedPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || resolvedPath.EndsWith(".zevtc", StringComparison.OrdinalIgnoreCase) => "application/zip",
            _ => "application/octet-stream"
        };
        return true;
    }

    public string GetRelativePath(string fightDirectoryPath, string filePath)
    {
        return Path.GetRelativePath(fightDirectoryPath, filePath);
    }

    public static string? BuildFightFingerprint(FightIndexDto? fightIndex)
    {
        if (fightIndex is null)
        {
            return null;
        }

        var start = fightIndex.TimeStartStandard ?? fightIndex.TimeStart;
        var recorder = fightIndex.RecordedAccountBy ?? fightIndex.RecordedBy ?? string.Empty;
        if (string.IsNullOrWhiteSpace(start) || string.IsNullOrWhiteSpace(fightIndex.FightName))
        {
            return null;
        }

        return string.Join(
            "|",
            fightIndex.FightName.Trim(),
            start.Trim(),
            fightIndex.DurationMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? fightIndex.Duration,
            recorder.Trim());
    }

    private FightArtifactSummaryDto BuildSummary(DirectoryInfo directory, FightArtifactManifest? manifest)
    {
        var artifactCount = EstimateArtifactCount(manifest);
        const long totalBytes = 0;

        if (manifest is null)
        {
            return new FightArtifactSummaryDto(
                FightId: directory.Name,
                Status: "Indexed",
                ArtifactCount: artifactCount,
                TotalBytes: totalBytes,
                Notes: "Artifact folder exists, but it predates manifest-backed parser ingestion.",
                SourceFileName: null,
                SourceFilePath: null,
                ImportedAtUtc: null,
                RawLogRetained: false,
                AnalysisJsonUrl: null,
                HtmlReportUrl: null,
                JsonReportUrl: null,
                ParserConsoleLogUrl: null,
                PatchEra: null,
                Attributes: [],
                FightIndex: null);
        }

        var links = BuildArtifactLinks(manifest);
        var fightIndex = manifest.FightIndex?.Data;

        return new FightArtifactSummaryDto(
            FightId: manifest.FightId,
            Status: manifest.EffectiveStatus,
            ArtifactCount: artifactCount,
            TotalBytes: totalBytes,
            Notes: manifest.ParserStatus,
            SourceFileName: manifest.SourceFileName,
            SourceFilePath: manifest.SourceFilePath,
            ImportedAtUtc: manifest.ImportedAtUtc.ToString("O"),
            RawLogRetained: manifest.RawLogRetained,
            AnalysisJsonUrl: links.AnalysisJsonUrl,
            HtmlReportUrl: links.HtmlReportUrl,
            JsonReportUrl: links.JsonReportUrl,
            ParserConsoleLogUrl: links.ParserConsoleLogUrl,
            PatchEra: ResolvePatchEra(fightIndex, manifest.ImportedAtUtc),
            Attributes: _fightAttributes.BuildAttributes(fightIndex),
            FightIndex: fightIndex);
    }

    private FightDetailDto BuildDetail(FightArtifactManifest manifest)
    {
        var fightIndex = manifest.FightIndex?.Data;
        return new FightDetailDto(
            FightId: manifest.FightId,
            Status: manifest.EffectiveStatus,
            SourceFileName: manifest.SourceFileName,
            SourceFilePath: manifest.SourceFilePath,
            SourceFileBytes: manifest.SourceFileBytes,
            ImportedAtUtc: manifest.ImportedAtUtc.ToString("O"),
            Parsed: manifest.Parsed,
            ParserStatus: manifest.ParserStatus,
            ParserElapsedMilliseconds: manifest.ParserElapsedMilliseconds,
            ParserExecutablePath: manifest.ParserExecutablePath,
            RawLogRetained: manifest.RawLogRetained,
            PatchEra: ResolvePatchEra(fightIndex, manifest.ImportedAtUtc),
            Attributes: _fightAttributes.BuildAttributes(fightIndex),
            FightIndex: fightIndex,
            ArtifactLinks: BuildArtifactLinks(manifest),
            GeneratedArtifacts: manifest.GeneratedArtifactRelativePaths.Select(path => Path.GetFileName(path) ?? path).ToArray());
    }

    private FightArtifactLinksDto BuildArtifactLinks(FightArtifactManifest manifest)
    {
        return new FightArtifactLinksDto(
            AnalysisJsonUrl: manifest.AnalysisJsonArtifactRelativePath is null ? null : $"/api/fights/{manifest.FightId}/artifacts/analysis-json",
            HtmlReportUrl: manifest.HtmlArtifactRelativePath is null ? null : $"/api/fights/{manifest.FightId}/artifacts/html",
            JsonReportUrl: manifest.JsonArtifactRelativePath is null ? null : $"/api/fights/{manifest.FightId}/artifacts/json",
            ParserConsoleLogUrl: !manifest.Parsed && manifest.ParserConsoleLogRelativePath is not null ? $"/api/fights/{manifest.FightId}/artifacts/parser-log" : null,
            RawLogUrl: manifest.RawLogRelativePath is null ? null : $"/api/fights/{manifest.FightId}/artifacts/raw");
    }

    private FightArtifactManifest? TryLoadManifestFromPath(string fightDirectoryPath, string manifestPath, bool hydrateDerivedData)
    {
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<FightArtifactManifest>(json, ManifestSerializerOptions);
            if (manifest is null || !hydrateDerivedData)
            {
                return manifest;
            }

            return EnsureDerivedData(fightDirectoryPath, manifest);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private FightArtifactManifest EnsureDerivedData(string fightDirectoryPath, FightArtifactManifest manifest)
    {
        var updatedManifest = manifest;
        var changed = false;

        var needsFightIndexRefresh =
            updatedManifest.FightIndex is null ||
            updatedManifest.FightIndex.SchemaVersion < CurrentFightIndexSchemaVersion ||
            string.IsNullOrWhiteSpace(updatedManifest.FightIndex.Data.EliteInsightsVersion);

        if (needsFightIndexRefresh)
        {
            var analysisJsonArtifactPath = string.IsNullOrWhiteSpace(updatedManifest.AnalysisJsonArtifactRelativePath)
                ? null
                : ResolveArtifactPath(fightDirectoryPath, updatedManifest.AnalysisJsonArtifactRelativePath);
            var jsonArtifactPath = string.IsNullOrWhiteSpace(updatedManifest.JsonArtifactRelativePath)
                ? null
                : ResolveArtifactPath(fightDirectoryPath, updatedManifest.JsonArtifactRelativePath);
            var indexedFight = _fightIndexer.TryIndexFight(analysisJsonArtifactPath, jsonArtifactPath);
            if (indexedFight is not null)
            {
                updatedManifest = updatedManifest with
                {
                    FightIndex = new FightIndexSnapshot(
                        SchemaVersion: CurrentFightIndexSchemaVersion,
                        IndexedAtUtc: DateTime.UtcNow,
                        Data: indexedFight)
                };
                changed = true;
            }
        }

        if (string.IsNullOrWhiteSpace(updatedManifest.FightFingerprint))
        {
            var fightFingerprint = BuildFightFingerprint(updatedManifest.FightIndex?.Data);
            if (!string.IsNullOrWhiteSpace(fightFingerprint))
            {
                updatedManifest = updatedManifest with
                {
                    FightFingerprint = fightFingerprint
                };
                changed = true;
            }
        }

        if (changed)
        {
            TryWriteManifest(fightDirectoryPath, updatedManifest);
        }

        return updatedManifest;
    }

    private void TryWriteManifest(string fightDirectoryPath, FightArtifactManifest manifest)
    {
        try
        {
            Directory.CreateDirectory(fightDirectoryPath);
            var manifestPath = Path.Combine(fightDirectoryPath, "manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, ManifestSerializerOptions));
            InvalidateCatalogCache();
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private IEnumerable<CatalogItem> EnumerateCatalogItems()
    {
        _paths.EnsureStorageDirectories();

        return new DirectoryInfo(_paths.FightsPath)
            .EnumerateDirectories()
            .Select(directory => new CatalogItem(
                Directory: directory,
                Manifest: TryLoadManifestFromPath(directory.FullName, Path.Combine(directory.FullName, "manifest.json"), hydrateDerivedData: true)));
    }

    private IEnumerable<CatalogItem> EnumerateCanonicalCatalogItems()
    {
        return EnumerateCatalogItems()
            .GroupBy(item => GetCanonicalKey(item))
            .Select(group => group
                .OrderByDescending(item => item.Manifest?.ImportedAtUtc ?? item.Directory.LastWriteTimeUtc)
                .First());
    }

    private static string GetCanonicalKey(CatalogItem item)
    {
        if (item.Manifest is null)
        {
            return $"fight:{item.Directory.Name}";
        }

        if (!string.IsNullOrWhiteSpace(item.Manifest.SourceFileSha256))
        {
            return $"hash:{item.Manifest.SourceFileSha256}";
        }

        if (!string.IsNullOrWhiteSpace(item.Manifest.FightFingerprint))
        {
            return $"fingerprint:{item.Manifest.FightFingerprint}";
        }

        return $"fight:{item.Manifest.FightId}";
    }

    private static DateTime GetFightStartUtc(FightArtifactSummaryDto fight)
    {
        if (fight.FightIndex is { } fightIndex)
        {
            var timestamps = new[]
            {
                fightIndex.TimeStartStandard,
                fightIndex.TimeStart
            };

            foreach (var timestamp in timestamps)
            {
                if (string.IsNullOrWhiteSpace(timestamp))
                {
                    continue;
                }

                if (DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    return parsed.UtcDateTime;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(fight.ImportedAtUtc) &&
            DateTimeOffset.TryParse(fight.ImportedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var importedAt))
        {
            return importedAt.UtcDateTime;
        }

        return DateTime.MinValue;
    }

    private PatchEraDto? ResolvePatchEra(FightIndexDto? fightIndex, DateTime importedAtUtc)
    {
        return _patchMetadata.FindPatchEra(GetFightLocalDate(fightIndex, importedAtUtc));
    }

    private static DateOnly? GetFightLocalDate(FightIndexDto? fightIndex, DateTime importedAtUtc)
    {
        var timestamps = new[]
        {
            fightIndex?.TimeStartStandard,
            fightIndex?.TimeStart
        };

        foreach (var timestamp in timestamps)
        {
            if (string.IsNullOrWhiteSpace(timestamp))
            {
                continue;
            }

            if (DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return DateOnly.FromDateTime(parsed.ToLocalTime().DateTime);
            }
        }

        return DateOnly.FromDateTime(importedAtUtc.ToLocalTime());
    }

    private static string? ResolveArtifactPath(string fightDirectoryPath, string relativePath)
    {
        var resolvedPath = Path.GetFullPath(Path.Combine(fightDirectoryPath, relativePath));
        if (!resolvedPath.StartsWith(fightDirectoryPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(resolvedPath))
        {
            return null;
        }

        return resolvedPath;
    }

    private static long GetDirectorySize(DirectoryInfo directory)
    {
        if (!directory.Exists)
        {
            return 0;
        }

        long totalBytes = 0;
        foreach (var file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            totalBytes += file.Length;
        }

        return totalBytes;
    }

    private IReadOnlyList<FightArtifactSummaryDto> GetCanonicalSummaries()
    {
        lock (_cacheLock)
        {
            _cachedCanonicalSummaries ??= BuildCanonicalSummaries();
            return _cachedCanonicalSummaries;
        }
    }

    private List<FightArtifactSummaryDto> BuildCanonicalSummaries()
    {
        _paths.EnsureStorageDirectories();

        return EnumerateCanonicalCatalogItems()
            .Select(item => BuildSummary(item.Directory, item.Manifest))
            .ToList();
    }

    private static FightBrowserSnapshotDto BuildFightBrowserSnapshot(IReadOnlyList<FightArtifactSummaryDto> allCatalogItems)
    {
        var fights = allCatalogItems
            .Where(fight => string.Equals(fight.Status, "Imported", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(GetFightStartUtc)
            .ToList();

        return new FightBrowserSnapshotDto(
            TotalCount: fights.Count,
            ImportedCount: fights.Count,
            FailedCount: allCatalogItems.Count(fight => string.Equals(fight.Status, "Parser failed", StringComparison.OrdinalIgnoreCase)),
            Fights: fights);
    }

    public void InvalidateCatalogCache()
    {
        lock (_cacheLock)
        {
            _cachedCanonicalSummaries = null;
            _cachedFightBrowserSnapshot = null;
        }
    }

    private static int EstimateArtifactCount(FightArtifactManifest? manifest)
    {
        if (manifest is null)
        {
            return 0;
        }

        return new[]
        {
            manifest.ParserConfigRelativePath,
            manifest.ParserConsoleLogRelativePath,
            manifest.RawLogRelativePath,
            manifest.AnalysisJsonArtifactRelativePath,
            manifest.HtmlArtifactRelativePath,
            manifest.JsonArtifactRelativePath
        }
            .Concat(manifest.GeneratedArtifactRelativePaths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private static DateTime ParseSummaryImportedAtUtc(FightArtifactSummaryDto summary)
    {
        if (!string.IsNullOrWhiteSpace(summary.ImportedAtUtc)
            && DateTimeOffset.TryParse(summary.ImportedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var importedAt))
        {
            return importedAt.UtcDateTime;
        }

        return DateTime.MinValue;
    }

    private static void ValidateFightId(string fightId)
    {
        if (string.IsNullOrWhiteSpace(fightId) || Path.GetFileName(fightId) != fightId)
        {
            throw new ArgumentException("Fight id is invalid.", nameof(fightId));
        }
    }

    private static string Slugify(string value)
    {
        Span<char> buffer = stackalloc char[Math.Min(value.Length, 48)];
        var index = 0;
        var previousDash = false;

        foreach (var character in value)
        {
            if (index >= buffer.Length)
            {
                break;
            }

            if (char.IsLetterOrDigit(character))
            {
                buffer[index++] = char.ToLowerInvariant(character);
                previousDash = false;
                continue;
            }

            if (previousDash || index == 0)
            {
                continue;
            }

            buffer[index++] = '-';
            previousDash = true;
        }

        while (index > 0 && buffer[index - 1] == '-')
        {
            index--;
        }

        return new string(buffer[..index]);
    }

    private sealed record CatalogItem(
        DirectoryInfo Directory,
        FightArtifactManifest? Manifest);
}

public enum FightArtifactKind
{
    AnalysisJson,
    Html,
    Json,
    ParserConsoleLog,
    RawLog
}
