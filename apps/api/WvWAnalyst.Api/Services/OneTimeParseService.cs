using System.Globalization;
using System.Text.Json;
using WvWAnalyst.Api.Analysis;
using WvWAnalyst.Api.Bridge;
using WvWAnalyst.Contracts;

namespace WvWAnalyst.Api.Services;

public sealed class OneTimeParseService
{
    public const int MaxRetainedParses = 25;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly string[] SupportedLogEndings = [".evtc", ".zevtc", ".zip"];

    private readonly AppPathService _paths;
    private readonly ParserImportService _parser;
    private readonly FightAttributeService _fightAttributes;
    private readonly ILogger<OneTimeParseService> _logger;
    private readonly SemaphoreSlim _parseGate = new(1, 1);

    public OneTimeParseService(
        AppPathService paths,
        ParserImportService parser,
        FightAttributeService fightAttributes,
        ILogger<OneTimeParseService> logger)
    {
        _paths = paths;
        _parser = parser;
        _fightAttributes = fightAttributes;
        _logger = logger;
    }

    public OneTimeParseSnapshotDto GetSnapshot()
    {
        _paths.EnsureStorageDirectories();
        var fights = new DirectoryInfo(_paths.OneTimeFightsPath)
            .EnumerateDirectories()
            .Select(TryLoadManifest)
            .Where(manifest => manifest is not null)
            .Select(manifest => manifest!.Fight)
            .OrderByDescending(fight => ParseImportedAtUtc(fight.ImportedAtUtc))
            .Take(MaxRetainedParses)
            .ToList();

        return new OneTimeParseSnapshotDto(
            MaxRetained: MaxRetainedParses,
            Count: fights.Count,
            Fights: fights);
    }

    public async Task<OneTimeParseBatchResultDto> ParseAsync(
        IReadOnlyList<IFormFile> files,
        CancellationToken cancellationToken)
    {
        await _parseGate.WaitAsync(cancellationToken);
        try
        {
            _paths.EnsureStorageDirectories();
            if (files.Count == 0)
            {
                return BuildResult(
                    success: false,
                    message: "Select or drop one or more log files first.",
                    uploadedCount: 0,
                    items: []);
            }

            var items = new List<OneTimeParseItemResultDto>(files.Count);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                items.Add(await ParseFileAsync(file, cancellationToken));
            }

            var importedCount = items.Count(item => item.Action == "imported");
            var failedCount = items.Count - importedCount;
            var message = $"Processed {files.Count} uploaded log{(files.Count == 1 ? "" : "s")}: {importedCount} retained, {failedCount} failed. The one-time workspace keeps the newest {MaxRetainedParses} parses.";
            return BuildResult(
                success: importedCount > 0 && failedCount == 0,
                message: message,
                uploadedCount: files.Count,
                items: items);
        }
        finally
        {
            _parseGate.Release();
        }
    }

    public bool TryGetArtifact(
        string fightId,
        OneTimeParseArtifactKind kind,
        out string artifactPath,
        out string contentType)
    {
        artifactPath = string.Empty;
        contentType = "application/octet-stream";
        if (!IsValidFightId(fightId))
        {
            return false;
        }

        var fightDirectory = new DirectoryInfo(Path.Combine(_paths.OneTimeFightsPath, fightId));
        var manifest = TryLoadManifest(fightDirectory);
        var relativePath = kind switch
        {
            OneTimeParseArtifactKind.Html => manifest?.HtmlArtifactRelativePath,
            OneTimeParseArtifactKind.PressurePreview => manifest?.PressurePreviewArtifactRelativePath,
            _ => null
        };
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var resolvedPath = Path.GetFullPath(Path.Combine(fightDirectory.FullName, relativePath));
        var directoryPrefix = fightDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolvedPath.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(resolvedPath))
        {
            return false;
        }

        artifactPath = resolvedPath;
        contentType = kind == OneTimeParseArtifactKind.Html
            ? "text/html; charset=utf-8"
            : "image/svg+xml; charset=utf-8";
        return true;
    }

    private async Task<OneTimeParseItemResultDto> ParseFileAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var sourceFileName = Path.GetFileName(file.FileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(sourceFileName) || !IsSupportedLogFile(sourceFileName))
        {
            return new OneTimeParseItemResultDto(
                SourceFileName: string.IsNullOrWhiteSpace(sourceFileName) ? "(unnamed)" : sourceFileName,
                Action: "failed",
                Message: "Only .evtc, .zevtc, and .zip files are accepted.",
                FightId: null,
                ParserElapsedMilliseconds: null);
        }
        if (file.Length <= 0)
        {
            return new OneTimeParseItemResultDto(
                SourceFileName: sourceFileName,
                Action: "failed",
                Message: "The uploaded file is empty.",
                FightId: null,
                ParserElapsedMilliseconds: null);
        }

        var operationId = Guid.NewGuid().ToString("N");
        var uploadDirectoryPath = Path.Combine(_paths.CachePath, "one-time-uploads", operationId);
        var uploadedLogPath = Path.Combine(uploadDirectoryPath, sourceFileName);
        var fightId = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{operationId}";
        var fightDirectoryPath = Path.Combine(_paths.OneTimeFightsPath, fightId);
        Directory.CreateDirectory(uploadDirectoryPath);

        try
        {
            await using (var output = new FileStream(uploadedLogPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await file.CopyToAsync(output, cancellationToken);
            }

            var parserResult = await _parser.ParseOneTimeLogAsync(uploadedLogPath, fightDirectoryPath, cancellationToken);
            if (!parserResult.Success)
            {
                TryDeleteDirectory(fightDirectoryPath);
                return new OneTimeParseItemResultDto(
                    SourceFileName: sourceFileName,
                    Action: "failed",
                    Message: parserResult.Message,
                    FightId: null,
                    ParserElapsedMilliseconds: parserResult.ParserElapsedMilliseconds);
            }

            var importedAtUtc = DateTime.UtcNow;
            var fight = BuildFight(fightId, sourceFileName, importedAtUtc, parserResult);
            var manifest = new OneTimeParseManifest(
                Fight: fight,
                HtmlArtifactRelativePath: parserResult.HtmlArtifactRelativePath,
                PressurePreviewArtifactRelativePath: parserResult.PressurePreviewArtifactRelativePath);
            await WriteManifestAsync(fightDirectoryPath, manifest, cancellationToken);
            TrimToRetentionLimit();

            return new OneTimeParseItemResultDto(
                SourceFileName: sourceFileName,
                Action: "imported",
                Message: parserResult.Message,
                FightId: fightId,
                ParserElapsedMilliseconds: parserResult.ParserElapsedMilliseconds);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed one-time parse for {SourceFileName}", sourceFileName);
            TryDeleteDirectory(fightDirectoryPath);
            return new OneTimeParseItemResultDto(
                SourceFileName: sourceFileName,
                Action: "failed",
                Message: exception.Message,
                FightId: null,
                ParserElapsedMilliseconds: null);
        }
        finally
        {
            TryDeleteDirectory(uploadDirectoryPath);
        }
    }

    private OneTimeParseFightDto BuildFight(
        string fightId,
        string sourceFileName,
        DateTime importedAtUtc,
        OneTimeParserResult parserResult)
    {
        var index = parserResult.FightIndex;
        return new OneTimeParseFightDto(
            FightId: fightId,
            SourceFileName: sourceFileName,
            ImportedAtUtc: importedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            PressurePreviewUrl: parserResult.PressurePreviewArtifactRelativePath is null
                ? null
                : $"/api/one-time-parses/{fightId}/artifacts/pressure-preview",
            HtmlReportUrl: parserResult.HtmlArtifactRelativePath is null
                ? null
                : $"/api/one-time-parses/{fightId}/artifacts/html",
            Attributes: _fightAttributes.BuildAttributes(index)
                .Where(attribute => attribute.Key.Equals("three-way", StringComparison.OrdinalIgnoreCase) ||
                    attribute.Key.Equals("organized-enemy", StringComparison.OrdinalIgnoreCase) ||
                    attribute.Key.Equals("cloudy-fight", StringComparison.OrdinalIgnoreCase))
                .ToList(),
            FightIndex: index is null
                ? null
                : new NightOverviewFightIndexDto(
                    Outcome: index.Outcome,
                    FightShape: index.FightShape,
                    Execution: index.Execution is null
                        ? null
                        : new NightOverviewExecutionDto(
                            ScoreAvailable: index.Execution.ScoreAvailable,
                            OverallScore: index.Execution.OverallScore,
                            Grade: index.Execution.Grade),
                    Duration: index.Duration,
                    TimeStart: index.TimeStart,
                    TimeStartStandard: index.TimeStartStandard,
                    SquadPlayerCount: index.SquadPlayerCount,
                    EnemyTargetCount: index.EnemyTargetCount,
                    EnemyPlayerCount: index.EnemyPlayerCount,
                    CommanderDisplayNames: index.CommanderDisplayNames));
    }

    private OneTimeParseBatchResultDto BuildResult(
        bool success,
        string message,
        int uploadedCount,
        IReadOnlyList<OneTimeParseItemResultDto> items)
    {
        return new OneTimeParseBatchResultDto(
            Success: success,
            Message: message,
            UploadedCount: uploadedCount,
            ImportedCount: items.Count(item => item.Action == "imported"),
            FailedCount: items.Count(item => item.Action != "imported"),
            Items: items,
            Snapshot: GetSnapshot());
    }

    private void TrimToRetentionLimit()
    {
        var retainedDirectories = new DirectoryInfo(_paths.OneTimeFightsPath)
            .EnumerateDirectories()
            .Select(directory => new
            {
                Directory = directory,
                Manifest = TryLoadManifest(directory)
            })
            .OrderByDescending(item => item.Manifest is null
                ? item.Directory.LastWriteTimeUtc
                : ParseImportedAtUtc(item.Manifest.Fight.ImportedAtUtc))
            .ToList();

        foreach (var item in retainedDirectories.Skip(MaxRetainedParses))
        {
            TryDeleteDirectory(item.Directory.FullName);
        }
    }

    private static async Task WriteManifestAsync(
        string fightDirectoryPath,
        OneTimeParseManifest manifest,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(fightDirectoryPath);
        var manifestPath = Path.Combine(fightDirectoryPath, "manifest.json");
        await using var stream = new FileStream(manifestPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, manifest, SerializerOptions, cancellationToken);
    }

    private static OneTimeParseManifest? TryLoadManifest(DirectoryInfo directory)
    {
        var manifestPath = Path.Combine(directory.FullName, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(manifestPath);
            return JsonSerializer.Deserialize<OneTimeParseManifest>(stream, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static DateTime ParseImportedAtUtc(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : DateTime.MinValue;

    private static bool IsSupportedLogFile(string fileName) =>
        SupportedLogEndings.Any(ending => fileName.EndsWith(ending, StringComparison.OrdinalIgnoreCase));

    private static bool IsValidFightId(string fightId) =>
        !string.IsNullOrWhiteSpace(fightId) &&
        string.Equals(fightId, Path.GetFileName(fightId), StringComparison.Ordinal) &&
        fightId.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');

    private static void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record OneTimeParseManifest(
        OneTimeParseFightDto Fight,
        string? HtmlArtifactRelativePath,
        string? PressurePreviewArtifactRelativePath);
}

public enum OneTimeParseArtifactKind
{
    Html,
    PressurePreview
}
