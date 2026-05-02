using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using WvWAnalyst.Contracts;

namespace WvWAnalyst.Api.Bridge;

public sealed class ParserImportService
{
    private const int OutputExcerptLineCount = 12;
    private const int DefaultMaxParallelism = 4;
    private const int MaxAllowedParallelism = 16;
    private const int MinimumIncludedParticipantsPerSide = 10;
    private const int OrganizedEnemyMovementScoreThreshold = 72;
    private static readonly TimeSpan MinimumIncludedFightDuration = TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MinimumIncludedOrganizedEnemyFightDuration = TimeSpan.FromSeconds(45);

    private static readonly JsonSerializerOptions ConsoleResultSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] SupportedLogEndings =
    [
        ".evtc",
        ".zevtc",
        ".zip",
    ];

    private readonly AppPathService _paths;
    private readonly ParserCliLocator _parserCliLocator;
    private readonly FightCatalogService _fightCatalog;
    private readonly EliteInsightsFightIndexer _fightIndexer;
    private readonly ILogger<ParserImportService> _logger;
    private readonly SemaphoreSlim _catalogCommitGate = new(1, 1);
    private readonly SemaphoreSlim _archiveMoveGate = new(1, 1);

    public ParserImportService(
        AppPathService paths,
        ParserCliLocator parserCliLocator,
        FightCatalogService fightCatalog,
        EliteInsightsFightIndexer fightIndexer,
        ILogger<ParserImportService> logger)
    {
        _paths = paths;
        _parserCliLocator = parserCliLocator;
        _fightCatalog = fightCatalog;
        _fightIndexer = fightIndexer;
        _logger = logger;
    }

    public async Task<DirectoryImportResultDto> ImportDirectoryAsync(DirectoryImportRequestDto request, CancellationToken cancellationToken)
    {
        return await ImportDirectoryAsync(request, reportProgress: null, cancellationToken);
    }

    public async Task<DirectoryImportResultDto> ImportDirectoryAsync(
        DirectoryImportRequestDto request,
        Action<DirectoryImportProgressUpdate>? reportProgress,
        CancellationToken cancellationToken)
    {
        var directoryPath = request.DirectoryPath?.Trim() ?? string.Empty;
        var mode = NormalizeMode(request.Mode);
        var maxParallelism = NormalizeMaxParallelism(request.MaxParallelism);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return new DirectoryImportResultDto(
                Success: false,
                Message: "Enter a directory path before starting a batch parse.",
                DirectoryPath: directoryPath,
                Mode: mode,
                MaxParallelism: maxParallelism,
                ResetCatalog: false,
                DiscoveredCount: 0,
                ImportedCount: 0,
                SkippedCount: 0,
                ExcludedCount: 0,
                FailedCount: 0,
                Items: []);
        }

        reportProgress?.Invoke(new DirectoryImportProgressUpdate(
            Message: "Scanning the directory for supported log files.",
            DiscoveredCount: 0,
            CompletedCount: 0,
            ImportedCount: 0,
            SkippedCount: 0,
            ExcludedCount: 0,
            FailedCount: 0,
            CurrentFileName: null,
            CurrentFilePath: null,
            LatestItem: null));

        var fullDirectoryPath = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(fullDirectoryPath))
        {
            return new DirectoryImportResultDto(
                Success: false,
                Message: $"Directory not found: {fullDirectoryPath}",
                DirectoryPath: fullDirectoryPath,
                Mode: mode,
                MaxParallelism: maxParallelism,
                ResetCatalog: false,
                DiscoveredCount: 0,
                ImportedCount: 0,
                SkippedCount: 0,
                ExcludedCount: 0,
                FailedCount: 0,
                Items: []);
        }

        _paths.EnsureStorageDirectories();

        var parserProbe = _parserCliLocator.Probe(_paths.ParserWorkspacePath, _paths.ConfiguredParserCliPath);
        if (!parserProbe.ParserCliDetected || parserProbe.ParserCliPath is null)
        {
            return new DirectoryImportResultDto(
                Success: false,
                Message: parserProbe.Notes,
                DirectoryPath: fullDirectoryPath,
                Mode: mode,
                MaxParallelism: maxParallelism,
                ResetCatalog: false,
                DiscoveredCount: 0,
                ImportedCount: 0,
                SkippedCount: 0,
                ExcludedCount: 0,
                FailedCount: 0,
                Items: []);
        }

        var archiveHandledLogs = ShouldArchiveHandledLogs(mode, fullDirectoryPath);
        var resetCatalog = string.Equals(mode, "rebuild-all", StringComparison.Ordinal);
        if (resetCatalog)
        {
            _fightCatalog.ResetCatalog();
        }

        var files = Directory.EnumerateFiles(fullDirectoryPath, "*", SearchOption.AllDirectories)
            .Where(IsSupportedLogFile)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        reportProgress?.Invoke(new DirectoryImportProgressUpdate(
            Message: $"Parsing 0 / {files.Length} logs.",
            DiscoveredCount: files.Length,
            CompletedCount: 0,
            ImportedCount: 0,
            SkippedCount: 0,
            ExcludedCount: 0,
            FailedCount: 0,
            CurrentFileName: null,
            CurrentFilePath: null,
            LatestItem: null));

        var reservedHashes = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        if (!resetCatalog)
        {
            foreach (var hash in _fightCatalog.GetKnownSuccessfulSourceHashes())
            {
                reservedHashes.TryAdd(hash, 0);
            }
        }

        var items = new DirectoryImportItemDto?[files.Length];
        var importedCount = 0;
        var skippedCount = 0;
        var excludedCount = 0;
        var failedCount = 0;
        var completedCount = 0;

        await Parallel.ForEachAsync(
            files.Select((path, index) => new IndexedLogFile(index, path)),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallelism,
                CancellationToken = cancellationToken
            },
            async (entry, parallelCancellationToken) =>
            {
                var filePath = entry.Path;
                var sourceFileName = Path.GetFileName(filePath);

                reportProgress?.Invoke(new DirectoryImportProgressUpdate(
                    Message: $"Parsing {Volatile.Read(ref completedCount)} / {files.Length} logs.",
                    DiscoveredCount: files.Length,
                    CompletedCount: Volatile.Read(ref completedCount),
                    ImportedCount: Volatile.Read(ref importedCount),
                    SkippedCount: Volatile.Read(ref skippedCount),
                    ExcludedCount: Volatile.Read(ref excludedCount),
                    FailedCount: Volatile.Read(ref failedCount),
                    CurrentFileName: sourceFileName,
                    CurrentFilePath: filePath,
                    LatestItem: null));

                var sourceHash = await ComputeFileSha256Async(filePath, parallelCancellationToken);

                DirectoryImportItemDto item;
                if (!reservedHashes.TryAdd(sourceHash, 0))
                {
                    if (archiveHandledLogs)
                    {
                        await DiscardPendingDuplicateAsync(filePath);
                    }

                    Interlocked.Increment(ref skippedCount);
                    item = new DirectoryImportItemDto(
                        SourceFileName: sourceFileName,
                        SourceFilePath: filePath,
                        Action: "skipped",
                        ReasonCode: "duplicate-hash",
                        Message: string.Equals(mode, "new-only", StringComparison.Ordinal)
                            ? "Skipped because this exact file hash was already imported successfully or already queued in this batch."
                            : "Skipped because this exact file hash was already queued in this batch.",
                        FightId: null,
                        ParserStatus: null,
                        ParserElapsedMilliseconds: null);
                }
                else
                {
                    var result = await ImportLogFileAsync(filePath, sourceHash, parserProbe.ParserCliPath, archiveHandledLogs, parallelCancellationToken);
                    if (string.Equals(result.Action, "imported", StringComparison.OrdinalIgnoreCase))
                    {
                        Interlocked.Increment(ref importedCount);
                        item = new DirectoryImportItemDto(
                            SourceFileName: sourceFileName,
                            SourceFilePath: result.FinalSourceFilePath ?? result.Fight?.SourceFilePath ?? filePath,
                            Action: "imported",
                            ReasonCode: null,
                            Message: result.Message,
                            FightId: result.Fight?.FightId,
                            ParserStatus: result.ParserStatus,
                            ParserElapsedMilliseconds: result.ParserElapsedMilliseconds);
                    }
                    else if (string.Equals(result.Action, "excluded", StringComparison.OrdinalIgnoreCase))
                    {
                        Interlocked.Increment(ref excludedCount);
                        item = new DirectoryImportItemDto(
                            SourceFileName: sourceFileName,
                            SourceFilePath: result.FinalSourceFilePath ?? filePath,
                            Action: "excluded",
                            ReasonCode: result.ReasonCode,
                            Message: result.Message,
                            FightId: null,
                            ParserStatus: result.ParserStatus,
                            ParserElapsedMilliseconds: result.ParserElapsedMilliseconds);
                    }
                    else
                    {
                        Interlocked.Increment(ref failedCount);
                        item = new DirectoryImportItemDto(
                            SourceFileName: sourceFileName,
                            SourceFilePath: result.FinalSourceFilePath ?? result.Fight?.SourceFilePath ?? filePath,
                            Action: "failed",
                            ReasonCode: null,
                            Message: result.Message,
                            FightId: result.Fight?.FightId,
                            ParserStatus: result.ParserStatus,
                            ParserElapsedMilliseconds: result.ParserElapsedMilliseconds);
                    }
                }

                items[entry.Index] = item;
                var completedAfterUpdate = Interlocked.Increment(ref completedCount);
                reportProgress?.Invoke(new DirectoryImportProgressUpdate(
                    Message: $"Parsing {completedAfterUpdate} / {files.Length} logs.",
                    DiscoveredCount: files.Length,
                    CompletedCount: completedAfterUpdate,
                    ImportedCount: Volatile.Read(ref importedCount),
                    SkippedCount: Volatile.Read(ref skippedCount),
                    ExcludedCount: Volatile.Read(ref excludedCount),
                    FailedCount: Volatile.Read(ref failedCount),
                    CurrentFileName: sourceFileName,
                    CurrentFilePath: filePath,
                    LatestItem: item));
            });

        var success = failedCount == 0;
        var message = BuildDirectoryImportMessage(files.Length, importedCount, skippedCount, excludedCount, failedCount, mode, fullDirectoryPath);

        return new DirectoryImportResultDto(
            Success: success,
            Message: message,
            DirectoryPath: fullDirectoryPath,
            Mode: mode,
            MaxParallelism: maxParallelism,
            ResetCatalog: resetCatalog,
            DiscoveredCount: files.Length,
            ImportedCount: importedCount,
            SkippedCount: skippedCount,
            ExcludedCount: excludedCount,
            FailedCount: failedCount,
            Items: items.Where(item => item is not null).Cast<DirectoryImportItemDto>().ToArray());
    }

    private async Task<FightImportResultDto> ImportLogFileAsync(
        string logFilePath,
        string sourceFileSha256,
        string parserExecutablePath,
        bool archiveHandledLog,
        CancellationToken cancellationToken)
    {
        var sourceFileName = Path.GetFileName(logFilePath);
        var sourceFileInfo = new FileInfo(logFilePath);
        if (!sourceFileInfo.Exists)
        {
            return new FightImportResultDto(
                Success: false,
                Action: "failed",
                ReasonCode: null,
                Message: $"File not found: {logFilePath}",
                Fight: null,
                FinalSourceFilePath: logFilePath,
                ParserExecutablePath: parserExecutablePath,
                ParserStatus: "Input file missing.",
                ParserElapsedMilliseconds: null,
                GeneratedFiles: [],
                OutputExcerpt: []);
        }
        var sourceFileBytes = sourceFileInfo.Length;

        var operationId = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}";
        var importCacheDirectoryPath = Path.Combine(_paths.CachePath, "imports", operationId);
        var stagedParserOutputDirectoryPath = Path.Combine(importCacheDirectoryPath, "parser");
        var stagedParserConfigPath = Path.Combine(stagedParserOutputDirectoryPath, "parser-import.conf");
        var stagedParserConsoleLogPath = Path.Combine(stagedParserOutputDirectoryPath, "parser-output.log");

        Directory.CreateDirectory(stagedParserOutputDirectoryPath);

        try
        {
            var parserConfig = BuildParserConfig(stagedParserOutputDirectoryPath);
            await File.WriteAllTextAsync(stagedParserConfigPath, parserConfig, Encoding.UTF8, cancellationToken);

            var parserRun = await RunParserAsync(
                parserExecutablePath,
                stagedParserConfigPath,
                logFilePath,
                stagedParserConsoleLogPath,
                cancellationToken);

                var stagedAnalysisArtifactPath = FindArtifactPathByEnding(
                    stagedParserOutputDirectoryPath,
                    ".analysis.json",
                    ".analysis.json.gz");

                FightIndexSnapshot? fightIndex = null;
                string? fightFingerprint = null;

            if (!string.IsNullOrWhiteSpace(stagedAnalysisArtifactPath))
            {
                var indexedFight = _fightIndexer.TryIndexFight(stagedAnalysisArtifactPath, eliteInsightsJsonPath: null);
                if (indexedFight is not null)
                {
                    fightIndex = new FightIndexSnapshot(
                        SchemaVersion: 8,
                        IndexedAtUtc: DateTime.UtcNow,
                        Data: indexedFight);
                    fightFingerprint = FightCatalogService.BuildFightFingerprint(indexedFight);
                }
            }

            var parserReportedSuccess = parserRun.ConsoleResult?.Parsed == true && parserRun.ExitCode == 0;
            var unsupportedParserOutputDecision = fightIndex is null
                ? EvaluateUnsupportedParserOutput(parserRun, stagedAnalysisArtifactPath)
                : null;
            var exclusionDecision = unsupportedParserOutputDecision ?? (fightIndex is null ? null : EvaluateImportExclusion(fightIndex.Data));
            var parseSucceeded = parserReportedSuccess && fightIndex is not null && exclusionDecision is null;
            var parserStatus = BuildParserStatus(parserRun, parseSucceeded, stagedAnalysisArtifactPath, exclusionDecision);

            await _catalogCommitGate.WaitAsync(cancellationToken);
            try
            {
                var existingManifest = _fightCatalog.TryFindReplacementFight(sourceFileSha256, fightFingerprint);

                if (exclusionDecision is not null)
                {
                    var excludedSourceFilePath = archiveHandledLog
                        ? await FinalizePendingLogFileAsync(logFilePath, sourceFileSha256, cancellationToken)
                        : logFilePath;

                    return new FightImportResultDto(
                        Success: false,
                        Action: "excluded",
                        ReasonCode: exclusionDecision.ReasonCode,
                        Message: $"Excluded {sourceFileName} because {exclusionDecision.Detail}",
                        Fight: null,
                        FinalSourceFilePath: excludedSourceFilePath,
                        ParserExecutablePath: parserExecutablePath,
                        ParserStatus: parserStatus,
                        ParserElapsedMilliseconds: parserRun.ConsoleResult?.Elapsed ?? parserRun.DurationMilliseconds,
                        GeneratedFiles: GetGeneratedFileNames(parserRun.ConsoleResult?.GeneratedFiles),
                        OutputExcerpt: parserRun.OutputLines.TakeLast(OutputExcerptLineCount).ToArray());
                }

                if (!parseSucceeded && existingManifest is not null)
                {
                    var retainedSourceFilePath = archiveHandledLog
                        ? await FinalizePendingLogFileAsync(logFilePath, sourceFileSha256, cancellationToken)
                        : logFilePath;

                    _logger.LogWarning(
                        "Reparse failed for {SourceFileName}; keeping existing artifacts for fight {FightId}",
                        sourceFileName,
                        existingManifest.FightId);

                    _fightCatalog.TryGetFightSummary(existingManifest.FightId, out var existingSummary);

                    return new FightImportResultDto(
                        Success: false,
                        Action: "failed",
                        ReasonCode: null,
                        Message: $"Reparse failed for {sourceFileName}. Kept the existing stored artifacts for fight {existingManifest.FightId}.",
                        Fight: existingSummary,
                        FinalSourceFilePath: retainedSourceFilePath,
                        ParserExecutablePath: parserExecutablePath,
                        ParserStatus: parserStatus,
                        ParserElapsedMilliseconds: parserRun.ConsoleResult?.Elapsed ?? parserRun.DurationMilliseconds,
                        GeneratedFiles: GetGeneratedFileNames(parserRun.ConsoleResult?.GeneratedFiles),
                        OutputExcerpt: parserRun.OutputLines.TakeLast(OutputExcerptLineCount).ToArray());
                }

                if (!string.IsNullOrWhiteSpace(stagedAnalysisArtifactPath) && File.Exists(stagedAnalysisArtifactPath))
                {
                    File.Delete(stagedAnalysisArtifactPath);
                }

                if (parseSucceeded && File.Exists(stagedParserConsoleLogPath))
                {
                    File.Delete(stagedParserConsoleLogPath);
                }

                var finalSourceFilePath = archiveHandledLog
                    ? await FinalizePendingLogFileAsync(logFilePath, sourceFileSha256, cancellationToken)
                    : logFilePath;
                var fightId = existingManifest?.FightId ?? _fightCatalog.CreateFightId(sourceFileName);
                var fightDirectoryPath = _fightCatalog.GetFightDirectoryPath(fightId);
                var finalParserOutputDirectoryPath = Path.Combine(fightDirectoryPath, "parser");
                Directory.CreateDirectory(fightDirectoryPath);

                ReplaceDirectory(stagedParserOutputDirectoryPath, finalParserOutputDirectoryPath);

                var generatedArtifactRelativePaths = GetRelativeGeneratedArtifacts(
                    fightDirectoryPath,
                    finalParserOutputDirectoryPath,
                    parserRun.ConsoleResult?.GeneratedFiles);
                var finalHtmlArtifactPath = FindArtifactPathByEnding(finalParserOutputDirectoryPath, ".html");
                var htmlArtifactRelativePath = string.IsNullOrWhiteSpace(finalHtmlArtifactPath)
                    ? null
                    : _fightCatalog.GetRelativePath(fightDirectoryPath, finalHtmlArtifactPath);

                var importedAtUtc = DateTime.UtcNow;
                var manifest = new FightArtifactManifest(
                    FightId: fightId,
                    SourceFileName: sourceFileName,
                    SourceFilePath: finalSourceFilePath,
                    SourceFileBytes: sourceFileBytes,
                    SourceFileSha256: sourceFileSha256,
                    FightFingerprint: fightFingerprint,
                    ImportedAtUtc: importedAtUtc,
                    Parsed: parseSucceeded,
                    ParserStatus: parserStatus,
                    ParserElapsedMilliseconds: parserRun.ConsoleResult?.Elapsed ?? parserRun.DurationMilliseconds,
                    ParserExecutablePath: parserExecutablePath,
                    ParserConfigRelativePath: _fightCatalog.GetRelativePath(fightDirectoryPath, Path.Combine(finalParserOutputDirectoryPath, "parser-import.conf")),
                    ParserConsoleLogRelativePath: parseSucceeded
                        ? null
                        : _fightCatalog.GetRelativePath(fightDirectoryPath, Path.Combine(finalParserOutputDirectoryPath, "parser-output.log")),
                    RawLogRetained: false,
                    RawLogRelativePath: null,
                    AnalysisJsonArtifactRelativePath: null,
                    HtmlArtifactRelativePath: htmlArtifactRelativePath,
                    JsonArtifactRelativePath: null,
                    GeneratedArtifactRelativePaths: generatedArtifactRelativePaths,
                    FightIndex: fightIndex);

                await _fightCatalog.WriteManifestAsync(manifest, cancellationToken);
                _fightCatalog.TryGetFightSummary(fightId, out var summary);

                var message = existingManifest is null
                    ? (parseSucceeded
                        ? $"Imported {sourceFileName} into fight {fightId}."
                        : $"Parser did not produce a usable analyst payload for {sourceFileName}.")
                    : (parseSucceeded
                        ? $"Reparsed {sourceFileName} and replaced the stored artifacts for fight {fightId}."
                        : $"Parser did not produce a usable analyst payload for {sourceFileName}.");

                return new FightImportResultDto(
                    Success: parseSucceeded,
                    Action: parseSucceeded ? "imported" : "failed",
                    ReasonCode: null,
                    Message: message,
                    Fight: summary,
                    FinalSourceFilePath: finalSourceFilePath,
                    ParserExecutablePath: parserExecutablePath,
                    ParserStatus: manifest.ParserStatus,
                    ParserElapsedMilliseconds: manifest.ParserElapsedMilliseconds,
                    GeneratedFiles: GetGeneratedFileNames(parserRun.ConsoleResult?.GeneratedFiles),
                    OutputExcerpt: parserRun.OutputLines.TakeLast(OutputExcerptLineCount).ToArray());
            }
            finally
            {
                _catalogCommitGate.Release();
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Parser import failed for {SourceFileName}", sourceFileName);
            var finalSourceFilePath = logFilePath;

            if (archiveHandledLog)
            {
                try
                {
                    finalSourceFilePath = await FinalizePendingLogFileAsync(logFilePath, sourceFileSha256, cancellationToken);
                }
                catch (Exception archiveException)
                {
                    _logger.LogWarning(archiveException, "Failed to archive handled pending log {SourceFileName}", sourceFileName);
                }
            }

            var outputExcerpt = File.Exists(stagedParserConsoleLogPath)
                ? (await File.ReadAllLinesAsync(stagedParserConsoleLogPath, cancellationToken)).TakeLast(OutputExcerptLineCount).ToArray()
                : [];

            return new FightImportResultDto(
                Success: false,
                Action: "failed",
                ReasonCode: null,
                Message: exception.Message,
                Fight: null,
                FinalSourceFilePath: finalSourceFilePath,
                ParserExecutablePath: parserExecutablePath,
                ParserStatus: "Import failed before parser completion.",
                ParserElapsedMilliseconds: null,
                GeneratedFiles: [],
                OutputExcerpt: outputExcerpt);
        }
        finally
        {
            TryDeleteDirectory(importCacheDirectoryPath);
        }
    }

    private static bool IsSupportedLogFile(string path)
    {
        return SupportedLogEndings.Any(ending => path.EndsWith(ending, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeMode(string? mode)
    {
        return string.Equals(mode, "rebuild-all", StringComparison.OrdinalIgnoreCase)
            ? "rebuild-all"
            : "new-only";
    }

    private static int NormalizeMaxParallelism(int? maxParallelism)
    {
        if (!maxParallelism.HasValue || maxParallelism.Value <= 0)
        {
            return DefaultMaxParallelism;
        }

        return Math.Min(MaxAllowedParallelism, maxParallelism.Value);
    }

    private static string BuildDirectoryImportMessage(int discoveredCount, int importedCount, int skippedCount, int excludedCount, int failedCount, string mode, string directoryPath)
    {
        var scope = string.Equals(mode, "rebuild-all", StringComparison.Ordinal)
            ? "rebuild-all"
            : "new-only";

        return $"Scanned {discoveredCount} supported log files in {directoryPath} using {scope}: {importedCount} imported, {skippedCount} skipped, {excludedCount} excluded, {failedCount} failed.";
    }

    private static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var buffer = new byte[1024 * 80];
        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (bytesRead <= 0)
            {
                break;
            }

            sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
        }

        sha256.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
    }

    private bool ShouldArchiveHandledLogs(string mode, string directoryPath)
    {
        if (!string.Equals(mode, "new-only", StringComparison.Ordinal))
        {
            return false;
        }

        var pendingDirectoryPath = _paths.ConfiguredPendingDirectoryPath;
        if (string.IsNullOrWhiteSpace(pendingDirectoryPath))
        {
            return false;
        }

        return IsSameDirectory(directoryPath, pendingDirectoryPath);
    }

    private async Task<string> FinalizePendingLogFileAsync(string sourceFilePath, string sourceFileSha256, CancellationToken cancellationToken)
    {
        var pendingDirectoryPath = _paths.ConfiguredPendingDirectoryPath;
        var archiveDirectoryPath = _paths.ConfiguredArchiveLogDirectoryPath;
        if (string.IsNullOrWhiteSpace(pendingDirectoryPath) ||
            string.IsNullOrWhiteSpace(archiveDirectoryPath) ||
            !File.Exists(sourceFilePath))
        {
            return sourceFilePath;
        }

        var fullSourcePath = Path.GetFullPath(sourceFilePath);
        if (!IsPathInsideDirectory(fullSourcePath, pendingDirectoryPath))
        {
            return fullSourcePath;
        }

        await _archiveMoveGate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(fullSourcePath))
            {
                return fullSourcePath;
            }

            Directory.CreateDirectory(archiveDirectoryPath);

            var existingManifest = _fightCatalog.TryFindReplacementFight(sourceFileSha256, fightFingerprint: null);
            if (!string.IsNullOrWhiteSpace(existingManifest?.SourceFilePath) && File.Exists(existingManifest.SourceFilePath))
            {
                File.Delete(fullSourcePath);
                DeleteEmptyDirectoriesUpTo(Path.GetDirectoryName(fullSourcePath), pendingDirectoryPath);
                return existingManifest.SourceFilePath!;
            }

            var sourceFileName = Path.GetFileName(fullSourcePath);
            var preferredDestinationPath = Path.Combine(archiveDirectoryPath, sourceFileName);
            if (File.Exists(preferredDestinationPath))
            {
                var existingHash = await ComputeFileSha256Async(preferredDestinationPath, cancellationToken);
                if (string.Equals(existingHash, sourceFileSha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(fullSourcePath);
                    DeleteEmptyDirectoriesUpTo(Path.GetDirectoryName(fullSourcePath), pendingDirectoryPath);
                    return preferredDestinationPath;
                }
            }

            var destinationPath = BuildUniqueArchiveLogPath(archiveDirectoryPath, sourceFileName, sourceFileSha256);
            File.Move(fullSourcePath, destinationPath);
            DeleteEmptyDirectoriesUpTo(Path.GetDirectoryName(fullSourcePath), pendingDirectoryPath);
            return destinationPath;
        }
        finally
        {
            _archiveMoveGate.Release();
        }
    }

    private async Task DiscardPendingDuplicateAsync(string sourceFilePath)
    {
        var pendingDirectoryPath = _paths.ConfiguredPendingDirectoryPath;
        if (string.IsNullOrWhiteSpace(pendingDirectoryPath) || !File.Exists(sourceFilePath))
        {
            return;
        }

        var fullSourcePath = Path.GetFullPath(sourceFilePath);
        if (!IsPathInsideDirectory(fullSourcePath, pendingDirectoryPath))
        {
            return;
        }

        try
        {
            File.Delete(fullSourcePath);
            DeleteEmptyDirectoriesUpTo(Path.GetDirectoryName(fullSourcePath), pendingDirectoryPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        await Task.CompletedTask;
    }

    private static string BuildUniqueArchiveLogPath(string archiveDirectoryPath, string sourceFileName, string sourceFileSha256)
    {
        var directPath = Path.Combine(archiveDirectoryPath, sourceFileName);
        if (!File.Exists(directPath))
        {
            return directPath;
        }

        var stem = Path.GetFileNameWithoutExtension(sourceFileName);
        var extension = Path.GetExtension(sourceFileName);
        var suffix = sourceFileSha256.Length >= 8
            ? sourceFileSha256[..8]
            : sourceFileSha256;
        var candidatePath = Path.Combine(archiveDirectoryPath, $"{stem}-{suffix}{extension}");
        if (!File.Exists(candidatePath))
        {
            return candidatePath;
        }

        for (var attempt = 2; ; attempt++)
        {
            candidatePath = Path.Combine(archiveDirectoryPath, $"{stem}-{suffix}-{attempt}{extension}");
            if (!File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }
    }

    private static bool IsSameDirectory(string leftPath, string rightPath) =>
        string.Equals(
            Path.GetFullPath(leftPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(rightPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsPathInsideDirectory(string candidatePath, string directoryPath)
    {
        var normalizedDirectoryPath = Path.GetFullPath(directoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var normalizedCandidatePath = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return normalizedCandidatePath.StartsWith(normalizedDirectoryPath, StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteEmptyDirectoriesUpTo(string? startDirectoryPath, string rootDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(startDirectoryPath))
        {
            return;
        }

        var normalizedRootDirectoryPath = Path.GetFullPath(rootDirectoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var currentDirectoryPath = Path.GetFullPath(startDirectoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        while (currentDirectoryPath.Length >= normalizedRootDirectoryPath.Length &&
            currentDirectoryPath.StartsWith(normalizedRootDirectoryPath, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(currentDirectoryPath, normalizedRootDirectoryPath, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (Directory.Exists(currentDirectoryPath) &&
                !Directory.EnumerateFileSystemEntries(currentDirectoryPath).Any())
            {
                Directory.Delete(currentDirectoryPath);
                currentDirectoryPath = Path.GetDirectoryName(currentDirectoryPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? string.Empty;
                continue;
            }

            break;
        }
    }

    private static string BuildParserConfig(string outputDirectoryPath)
    {
        var settings = new[]
        {
            "SaveAtOut=false",
            $"OutLocation={outputDirectoryPath}",
            "SaveOutHTML=true",
            "SaveOutJSON=false",
            "SaveOutAnalystJSON=true",
            "SaveOutCSV=false",
            "SaveOutTrace=false",
            "ParseMultipleLogs=false",
            "SingleThreaded=true",
            "ParsePhases=true",
            "ParseCombatReplay=true",
            "ComputeDamageModifiers=true",
            "RawTimelineArrays=false",
            "DetailledWvW=true",
            "CompressRaw=false",
            "IndentJSON=false",
            "HtmlExternalScripts=false",
            "UploadToDPSReports=false",
            "UploadToWingman=false"
        };

        return string.Join(Environment.NewLine, settings) + Environment.NewLine;
    }

    private async Task<ParserRunResult> RunParserAsync(
        string parserExecutablePath,
        string parserConfigPath,
        string logFilePath,
        string parserConsoleLogPath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = parserExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(parserExecutablePath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(parserConfigPath);
        startInfo.ArgumentList.Add(logFilePath);

        using var process = new Process
        {
            StartInfo = startInfo
        };

        var stopwatch = Stopwatch.StartNew();
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        stopwatch.Stop();

        var outputLines = SplitLines(stdout)
            .Concat(SplitLines(stderr).Select(line => $"stderr: {line}"))
            .ToArray();

        await File.WriteAllTextAsync(
            parserConsoleLogPath,
            string.Join(Environment.NewLine, outputLines),
            Encoding.UTF8,
            cancellationToken);

        var consoleResult = ParseConsoleResult(stdout);
        return new ParserRunResult(
            ExitCode: process.ExitCode,
            DurationMilliseconds: stopwatch.ElapsedMilliseconds,
            OutputLines: outputLines,
            ConsoleResult: consoleResult);
    }

    private static ParserConsoleResult? ParseConsoleResult(string stdout)
    {
        foreach (var line in SplitLines(stdout).Reverse())
        {
            const string prefix = "Processed - ";
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var payload = line[prefix.Length..];
            try
            {
                return JsonSerializer.Deserialize<ParserConsoleResult>(payload, ConsoleResultSerializerOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return null;
    }

    private static string BuildParserStatus(
        ParserRunResult parserRun,
        bool parseSucceeded,
        string? stagedAnalysisArtifactPath,
        ExclusionDecision? exclusionDecision)
    {
        if (exclusionDecision is not null)
        {
            return exclusionDecision.Status;
        }

        if (parseSucceeded)
        {
            return parserRun.ConsoleResult?.Status ?? $"Parser exit code {parserRun.ExitCode}";
        }

        if (parserRun.ConsoleResult?.Parsed == true && parserRun.ExitCode == 0)
        {
            if (string.IsNullOrWhiteSpace(stagedAnalysisArtifactPath))
            {
                return "Parser completed, but no analysis.json payload was produced. Detailed WvW analyst export is only available when the parser can build a detailed WvW summary.";
            }

            return "Parser completed and wrote analysis.json, but the payload could not be indexed.";
        }

        return parserRun.ConsoleResult?.Status ?? $"Parser exit code {parserRun.ExitCode}";
    }

    private static ExclusionDecision? EvaluateUnsupportedParserOutput(ParserRunResult parserRun, string? stagedAnalysisArtifactPath)
    {
        if (parserRun.ConsoleResult?.Parsed != true || parserRun.ExitCode != 0 || !string.IsNullOrWhiteSpace(stagedAnalysisArtifactPath))
        {
            return null;
        }

        var outputKind = ExtractParserOutputKind(parserRun.ConsoleResult);
        if (string.Equals(outputKind, "detailed_gh", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(outputKind, "gh", StringComparison.OrdinalIgnoreCase))
        {
            var label = string.IsNullOrWhiteSpace(outputKind) ? "Guild Hall" : $"Guild Hall ({outputKind})";
            return new ExclusionDecision(
                ReasonCode: "unsupported-guild-hall",
                Status: $"Excluded after parsing: EI classified this as {label}, so no WvWAnalyst analysis.json was produced.",
                Detail: $"EI classified the log as {label}, not a detailed WvW map log; WvWAnalyst imports only detailed WvW fights.");
        }

        if (!string.IsNullOrWhiteSpace(outputKind) &&
            !string.Equals(outputKind, "detailed_wvw", StringComparison.OrdinalIgnoreCase))
        {
            return new ExclusionDecision(
                ReasonCode: "unsupported-parser-output",
                Status: $"Excluded after parsing: EI produced {outputKind} output, which does not include WvWAnalyst analysis.json.",
                Detail: $"EI completed this log as {outputKind}, not detailed_wvw; WvWAnalyst imports only detailed WvW fights.");
        }

        return null;
    }

    private static string? ExtractParserOutputKind(ParserConsoleResult consoleResult)
    {
        if (!string.IsNullOrWhiteSpace(consoleResult.Status))
        {
            var tokens = consoleResult.Status.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length > 0 && LooksLikeParserOutputKind(tokens[^1]))
            {
                return tokens[^1];
            }
        }

        var generatedFiles = consoleResult.GeneratedFiles ?? Array.Empty<string>();
        foreach (var generatedFile in generatedFiles)
        {
            var outputKind = ExtractParserOutputKindFromFileName(generatedFile);
            if (!string.IsNullOrWhiteSpace(outputKind))
            {
                return outputKind;
            }
        }

        return null;
    }

    private static string? ExtractParserOutputKindFromFileName(string filePath)
    {
        var stem = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrWhiteSpace(stem))
        {
            return null;
        }

        if (stem.EndsWith(".analysis", StringComparison.OrdinalIgnoreCase))
        {
            stem = Path.GetFileNameWithoutExtension(stem);
        }

        var tokens = stem.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < tokens.Length - 1; index++)
        {
            if (string.Equals(tokens[index], "detailed", StringComparison.OrdinalIgnoreCase))
            {
                return $"detailed_{tokens[index + 1]}";
            }
        }

        return tokens.FirstOrDefault(LooksLikeParserOutputKind);
    }

    private static bool LooksLikeParserOutputKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains('_', StringComparison.Ordinal) ||
            string.Equals(value, "wvw", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "gh", StringComparison.OrdinalIgnoreCase);
    }

    private static ExclusionDecision? EvaluateImportExclusion(FightIndexDto fightIndex)
    {
        var reasons = new List<string>();
        var tooShort = false;
        var tooSmall = false;
        var squadParticipants = Math.Max(0, fightIndex.SquadPlayerCount);
        var enemyParticipants = Math.Max(0, fightIndex.EnemyPlayerCount > 0 ? fightIndex.EnemyPlayerCount : fightIndex.EnemyTargetCount);
        var hasMinimumParticipants = squadParticipants >= MinimumIncludedParticipantsPerSide
            && enemyParticipants >= MinimumIncludedParticipantsPerSide;
        var organizedEnemyDurationMinimum = IsOrganizedEnemyFight(fightIndex) && hasMinimumParticipants
            ? MinimumIncludedOrganizedEnemyFightDuration
            : MinimumIncludedFightDuration;

        if (fightIndex.DurationMilliseconds is long durationMilliseconds &&
            durationMilliseconds > 0 &&
            durationMilliseconds < organizedEnemyDurationMinimum.TotalMilliseconds)
        {
            tooShort = true;
            reasons.Add($"the fight lasted {BuildCompactDurationLabel(durationMilliseconds)}, below the {BuildCompactDurationLabel((long)organizedEnemyDurationMinimum.TotalMilliseconds)} minimum");
        }

        if (squadParticipants > 0 && enemyParticipants > 0 &&
            (squadParticipants < MinimumIncludedParticipantsPerSide || enemyParticipants < MinimumIncludedParticipantsPerSide))
        {
            tooSmall = true;
            reasons.Add($"the fight size was {squadParticipants} squad vs {enemyParticipants} enemy participants, below the {MinimumIncludedParticipantsPerSide}-per-side minimum");
        }

        if (!tooShort && !tooSmall)
        {
            return null;
        }

        var reasonCode = tooShort && tooSmall
            ? "too-short-and-too-small"
            : tooShort
                ? "too-short"
                : "too-small";
        var status = tooShort && tooSmall
            ? $"Excluded after parsing: shorter than {BuildCompactDurationLabel((long)organizedEnemyDurationMinimum.TotalMilliseconds)} and fewer than {MinimumIncludedParticipantsPerSide} participants per side."
            : tooShort
                ? $"Excluded after parsing: shorter than {BuildCompactDurationLabel((long)organizedEnemyDurationMinimum.TotalMilliseconds)}."
                : $"Excluded after parsing: fewer than {MinimumIncludedParticipantsPerSide} participants per side.";

        return new ExclusionDecision(
            ReasonCode: reasonCode,
            Status: status,
            Detail: string.Join(" and ", reasons));
    }

    private static bool IsOrganizedEnemyFight(FightIndexDto fightIndex)
    {
        return fightIndex.Execution?.Context?.EnemyMovementScore is int enemyMovementScore
            && enemyMovementScore >= OrganizedEnemyMovementScoreThreshold;
    }

    private static string BuildCompactDurationLabel(long durationMilliseconds)
    {
        if (durationMilliseconds <= 0)
        {
            return "0s";
        }

        var duration = TimeSpan.FromMilliseconds(durationMilliseconds);
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{duration.Minutes}m {duration.Seconds:D2}s";
        }

        return $"{duration.Seconds}s";
    }

    private static IReadOnlyList<string> GetGeneratedFileNames(IReadOnlyList<string>? generatedFiles)
    {
        return generatedFiles?.Select(path => Path.GetFileName(path) ?? path).ToArray() ?? [];
    }

    private static IReadOnlyList<string> GetRelativeGeneratedArtifacts(
        string fightDirectoryPath,
        string parserOutputDirectoryPath,
        IReadOnlyList<string>? generatedFiles)
    {
        if (generatedFiles is null || generatedFiles.Count == 0)
        {
            return [];
        }

        return generatedFiles
            .Select(path => Path.GetFileName(path))
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Select(fileName => Path.Combine(parserOutputDirectoryPath, fileName!))
            .Where(File.Exists)
            .Select(path => Path.GetRelativePath(fightDirectoryPath, path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? FindArtifactPathByEnding(string parserOutputDirectoryPath, params string[] endings)
    {
        var parserDirectory = new DirectoryInfo(parserOutputDirectoryPath);
        if (!parserDirectory.Exists)
        {
            return null;
        }

        return parserDirectory
            .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(file => endings.Any(ending => file.Name.EndsWith(ending, StringComparison.OrdinalIgnoreCase)))
            ?.FullName;
    }

    private static IReadOnlyList<string> SplitLines(string? text)
    {
        return (text ?? string.Empty)
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static void ReplaceDirectory(string sourceDirectoryPath, string destinationDirectoryPath)
    {
        var destinationParent = Path.GetDirectoryName(destinationDirectoryPath);
        if (!string.IsNullOrWhiteSpace(destinationParent))
        {
            Directory.CreateDirectory(destinationParent);
        }

        TryDeleteDirectory(destinationDirectoryPath);

        if (Directory.Exists(sourceDirectoryPath))
        {
            Directory.Move(sourceDirectoryPath, destinationDirectoryPath);
        }
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        try
        {
            Directory.Delete(directoryPath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record ParserRunResult(
        int ExitCode,
        long DurationMilliseconds,
        IReadOnlyList<string> OutputLines,
        ParserConsoleResult? ConsoleResult);

    private sealed record ParserConsoleResult(
        string FileName,
        bool Parsed,
        string Status,
        IReadOnlyList<string> GeneratedFiles,
        long Elapsed);

    private sealed record ExclusionDecision(
        string ReasonCode,
        string Status,
        string Detail);

    private sealed record IndexedLogFile(
        int Index,
        string Path);
}

public sealed record DirectoryImportProgressUpdate(
    string Message,
    int DiscoveredCount,
    int CompletedCount,
    int ImportedCount,
    int SkippedCount,
    int ExcludedCount,
    int FailedCount,
    string? CurrentFileName,
    string? CurrentFilePath,
    DirectoryImportItemDto? LatestItem);
