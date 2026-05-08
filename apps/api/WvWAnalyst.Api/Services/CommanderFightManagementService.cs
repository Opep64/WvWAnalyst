using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using WvWAnalyst.Api.Analysis;
using WvWAnalyst.Api.Bridge;
using WvWAnalyst.Contracts;

namespace WvWAnalyst.Api.Services;

public sealed class CommanderFightManagementService
{
    private static readonly string[] SupportedLogExtensions = [".evtc", ".zevtc", ".zip"];

    private readonly AppPathService _paths;
    private readonly FightCatalogService _fightCatalog;
    private readonly FightAnalysisService _fightAnalysis;
    private readonly ILogger<CommanderFightManagementService> _logger;

    public CommanderFightManagementService(
        AppPathService paths,
        FightCatalogService fightCatalog,
        FightAnalysisService fightAnalysis,
        ILogger<CommanderFightManagementService> logger)
    {
        _paths = paths;
        _fightCatalog = fightCatalog;
        _fightAnalysis = fightAnalysis;
        _logger = logger;
    }

    public DeleteCommanderFightsResultDto DeleteCommanderFights(string? commander, CancellationToken cancellationToken)
    {
        var normalizedCommander = commander?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedCommander))
        {
            return new DeleteCommanderFightsResultDto(
                Success: false,
                Message: "Choose a commander before deleting fights.",
                Commander: string.Empty,
                MatchedFightCount: 0,
                DeletedFightCount: 0,
                DeletedLogFileCount: 0,
                MissingLogFileCount: 0,
                SkippedLogFileCount: 0,
                AnalysisRecalculationSeconds: 0.0);
        }

        var matchingFights = _fightCatalog.GetManagementItems()
            .Where(item => item.CommanderDisplayNames.Any(value => string.Equals(value, normalizedCommander, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (matchingFights.Count == 0)
        {
            return new DeleteCommanderFightsResultDto(
                Success: false,
                Message: $"No stored fights were found for commander {normalizedCommander}.",
                Commander: normalizedCommander,
                MatchedFightCount: 0,
                DeletedFightCount: 0,
                DeletedLogFileCount: 0,
                MissingLogFileCount: 0,
                SkippedLogFileCount: 0,
                AnalysisRecalculationSeconds: 0.0);
        }

        SourceLogDeleteResult logDeleteResult;
        try
        {
            logDeleteResult = DeleteAssociatedSourceLogs(matchingFights, cancellationToken);
        }
        catch (IOException exception)
        {
            return BuildLogDeleteFailure(normalizedCommander, matchingFights.Count, exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return BuildLogDeleteFailure(normalizedCommander, matchingFights.Count, exception.Message);
        }

        if (logDeleteResult.Failures.Count > 0)
        {
            var failureSummary = string.Join(" ", logDeleteResult.Failures.Take(3));
            return new DeleteCommanderFightsResultDto(
                Success: false,
                Message: $"Stopped before deleting stored fight data because one or more associated log files could not be deleted. {failureSummary}",
                Commander: normalizedCommander,
                MatchedFightCount: matchingFights.Count,
                DeletedFightCount: 0,
                DeletedLogFileCount: logDeleteResult.DeletedLogFileCount,
                MissingLogFileCount: logDeleteResult.MissingLogFileCount,
                SkippedLogFileCount: logDeleteResult.SkippedLogFileCount,
                AnalysisRecalculationSeconds: 0.0);
        }

        var deletedFightCount = _fightCatalog.DeleteFightDirectories(
            matchingFights.Select(fight => fight.FightId),
            cancellationToken);

        var analysisSeconds = 0.0;
        string analysisMessage;
        if (deletedFightCount > 0)
        {
            var warmUp = WarmUpAnalysisAfterDeletion($"commander {normalizedCommander}");
            analysisSeconds = warmUp.Seconds;
            analysisMessage = warmUp.Message;
        }
        else
        {
            analysisMessage = "No stored fight folders needed deletion.";
        }

        var messageParts = new List<string>
        {
            $"Deleted {deletedFightCount} stored fight(s) for {normalizedCommander}.",
            $"Deleted {logDeleteResult.DeletedLogFileCount} associated source log file(s)."
        };
        if (logDeleteResult.MissingLogFileCount > 0)
        {
            messageParts.Add($"{logDeleteResult.MissingLogFileCount} source log reference(s) were already missing.");
        }
        if (logDeleteResult.SkippedLogFileCount > 0)
        {
            messageParts.Add($"{logDeleteResult.SkippedLogFileCount} source log reference(s) were outside the configured pending/archive stores and were left alone.");
        }
        messageParts.Add(analysisMessage);

        return new DeleteCommanderFightsResultDto(
            Success: true,
            Message: string.Join(" ", messageParts),
            Commander: normalizedCommander,
            MatchedFightCount: matchingFights.Count,
            DeletedFightCount: deletedFightCount,
            DeletedLogFileCount: logDeleteResult.DeletedLogFileCount,
            MissingLogFileCount: logDeleteResult.MissingLogFileCount,
            SkippedLogFileCount: logDeleteResult.SkippedLogFileCount,
            AnalysisRecalculationSeconds: analysisSeconds);
    }

    public DeleteDateRangeFightsResultDto DeleteDateRangeFights(string? startDate, string? endDate, CancellationToken cancellationToken)
    {
        if (!TryParseDateRange(startDate, endDate, out var parsedStartDate, out var parsedEndDate, out var validationMessage))
        {
            return new DeleteDateRangeFightsResultDto(
                Success: false,
                Message: validationMessage,
                StartDate: startDate?.Trim() ?? string.Empty,
                EndDate: endDate?.Trim() ?? string.Empty,
                MatchedFightCount: 0,
                DeletedFightCount: 0,
                DeletedLogFileCount: 0,
                MissingLogFileCount: 0,
                SkippedLogFileCount: 0,
                AnalysisRecalculationSeconds: 0.0);
        }

        var normalizedStartDate = parsedStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var normalizedEndDate = parsedEndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var matchingFights = _fightCatalog.GetManagementItems()
            .Where(item => item.FightLocalDate is { } fightDate && fightDate >= parsedStartDate && fightDate <= parsedEndDate)
            .ToList();

        if (matchingFights.Count == 0)
        {
            return new DeleteDateRangeFightsResultDto(
                Success: false,
                Message: $"No stored fights were found from {normalizedStartDate} through {normalizedEndDate}.",
                StartDate: normalizedStartDate,
                EndDate: normalizedEndDate,
                MatchedFightCount: 0,
                DeletedFightCount: 0,
                DeletedLogFileCount: 0,
                MissingLogFileCount: 0,
                SkippedLogFileCount: 0,
                AnalysisRecalculationSeconds: 0.0);
        }

        SourceLogDeleteResult logDeleteResult;
        try
        {
            logDeleteResult = DeleteAssociatedSourceLogs(matchingFights, cancellationToken);
        }
        catch (IOException exception)
        {
            return BuildDateRangeLogDeleteFailure(normalizedStartDate, normalizedEndDate, matchingFights.Count, exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return BuildDateRangeLogDeleteFailure(normalizedStartDate, normalizedEndDate, matchingFights.Count, exception.Message);
        }

        if (logDeleteResult.Failures.Count > 0)
        {
            var failureSummary = string.Join(" ", logDeleteResult.Failures.Take(3));
            return new DeleteDateRangeFightsResultDto(
                Success: false,
                Message: $"Stopped before deleting stored fight data because one or more associated log files could not be deleted. {failureSummary}",
                StartDate: normalizedStartDate,
                EndDate: normalizedEndDate,
                MatchedFightCount: matchingFights.Count,
                DeletedFightCount: 0,
                DeletedLogFileCount: logDeleteResult.DeletedLogFileCount,
                MissingLogFileCount: logDeleteResult.MissingLogFileCount,
                SkippedLogFileCount: logDeleteResult.SkippedLogFileCount,
                AnalysisRecalculationSeconds: 0.0);
        }

        var deletedFightCount = _fightCatalog.DeleteFightDirectories(
            matchingFights.Select(fight => fight.FightId),
            cancellationToken);

        var analysisSeconds = 0.0;
        string analysisMessage;
        if (deletedFightCount > 0)
        {
            var warmUp = WarmUpAnalysisAfterDeletion($"date range {normalizedStartDate} through {normalizedEndDate}");
            analysisSeconds = warmUp.Seconds;
            analysisMessage = warmUp.Message;
        }
        else
        {
            analysisMessage = "No stored fight folders needed deletion.";
        }

        var messageParts = new List<string>
        {
            $"Deleted {deletedFightCount} stored fight(s) from {normalizedStartDate} through {normalizedEndDate}.",
            $"Deleted {logDeleteResult.DeletedLogFileCount} associated source log file(s)."
        };
        if (logDeleteResult.MissingLogFileCount > 0)
        {
            messageParts.Add($"{logDeleteResult.MissingLogFileCount} source log reference(s) were already missing.");
        }
        if (logDeleteResult.SkippedLogFileCount > 0)
        {
            messageParts.Add($"{logDeleteResult.SkippedLogFileCount} source log reference(s) were outside the configured pending/archive stores and were left alone.");
        }
        messageParts.Add(analysisMessage);

        return new DeleteDateRangeFightsResultDto(
            Success: true,
            Message: string.Join(" ", messageParts),
            StartDate: normalizedStartDate,
            EndDate: normalizedEndDate,
            MatchedFightCount: matchingFights.Count,
            DeletedFightCount: deletedFightCount,
            DeletedLogFileCount: logDeleteResult.DeletedLogFileCount,
            MissingLogFileCount: logDeleteResult.MissingLogFileCount,
            SkippedLogFileCount: logDeleteResult.SkippedLogFileCount,
            AnalysisRecalculationSeconds: analysisSeconds);
    }

    private static DeleteCommanderFightsResultDto BuildLogDeleteFailure(string commander, int matchedFightCount, string message)
    {
        return new DeleteCommanderFightsResultDto(
            Success: false,
            Message: $"Stopped before deleting stored fight data because an associated source log could not be checked or deleted. {message}",
            Commander: commander,
            MatchedFightCount: matchedFightCount,
            DeletedFightCount: 0,
            DeletedLogFileCount: 0,
            MissingLogFileCount: 0,
            SkippedLogFileCount: 0,
            AnalysisRecalculationSeconds: 0.0);
    }

    private static DeleteDateRangeFightsResultDto BuildDateRangeLogDeleteFailure(string startDate, string endDate, int matchedFightCount, string message)
    {
        return new DeleteDateRangeFightsResultDto(
            Success: false,
            Message: $"Stopped before deleting stored fight data because an associated source log could not be checked or deleted. {message}",
            StartDate: startDate,
            EndDate: endDate,
            MatchedFightCount: matchedFightCount,
            DeletedFightCount: 0,
            DeletedLogFileCount: 0,
            MissingLogFileCount: 0,
            SkippedLogFileCount: 0,
            AnalysisRecalculationSeconds: 0.0);
    }

    private static bool TryParseDateRange(
        string? startDate,
        string? endDate,
        out DateOnly parsedStartDate,
        out DateOnly parsedEndDate,
        out string message)
    {
        parsedStartDate = default;
        parsedEndDate = default;
        var normalizedStartDate = startDate?.Trim();
        var normalizedEndDate = endDate?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedStartDate) || string.IsNullOrWhiteSpace(normalizedEndDate))
        {
            message = "Choose both a start date and an end date before deleting fights.";
            return false;
        }

        if (!DateOnly.TryParseExact(normalizedStartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedStartDate))
        {
            message = "Start date must use YYYY-MM-DD format.";
            return false;
        }

        if (!DateOnly.TryParseExact(normalizedEndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedEndDate))
        {
            message = "End date must use YYYY-MM-DD format.";
            return false;
        }

        if (parsedStartDate > parsedEndDate)
        {
            message = "Start date must be on or before end date.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private AnalysisWarmUpResult WarmUpAnalysisAfterDeletion(string deleteScope)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            _fightAnalysis.BuildSnapshot(
                commander: null,
                startDate: null,
                endDate: null,
                outcomeCode: null,
                squadIncludeClasses: null,
                squadExcludeClasses: null,
                enemyIncludeClasses: null,
                enemyExcludeClasses: null,
                patchScope: null,
                patchEraIds: null,
                fightAttributes: null);
            stopwatch.Stop();
            var seconds = Math.Max(0.1, Math.Round(stopwatch.Elapsed.TotalSeconds, 1));
            return new AnalysisWarmUpResult(seconds, $"Analysis recalculated in {seconds:0.0}s.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Analysis warm-up failed after deleting fights for {DeleteScope}", deleteScope);
            return new AnalysisWarmUpResult(0.0, $"Analysis warm-up failed: {exception.Message}");
        }
    }

    private SourceLogDeleteResult DeleteAssociatedSourceLogs(
        IReadOnlyList<FightCatalogManagementItem> fights,
        CancellationToken cancellationToken)
    {
        var managedRoots = GetManagedLogRoots();
        var hashCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sourceLogPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missingCount = 0;
        var skippedCount = 0;

        foreach (var fight in fights)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceLogPath = FindManagedSourceLogPath(fight, managedRoots, hashCache, cancellationToken);
            if (!string.IsNullOrWhiteSpace(sourceLogPath))
            {
                sourceLogPaths.Add(sourceLogPath);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(fight.SourceFilePath)
                && File.Exists(fight.SourceFilePath)
                && (!IsSupportedLogFile(fight.SourceFilePath) || !IsPathInsideAnyDirectory(fight.SourceFilePath, managedRoots)))
            {
                skippedCount++;
            }
            else
            {
                missingCount++;
            }
        }

        var deletedCount = 0;
        var failures = new List<string>();
        foreach (var sourceLogPath in sourceLogPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Delete(sourceLogPath);
                deletedCount++;
                var root = GetContainingRoot(sourceLogPath, managedRoots);
                if (root is not null)
                {
                    DeleteEmptyDirectoriesUpTo(Path.GetDirectoryName(sourceLogPath), root);
                }
            }
            catch (IOException exception)
            {
                failures.Add($"{Path.GetFileName(sourceLogPath)}: {exception.Message}");
            }
            catch (UnauthorizedAccessException exception)
            {
                failures.Add($"{Path.GetFileName(sourceLogPath)}: {exception.Message}");
            }
        }

        return new SourceLogDeleteResult(deletedCount, missingCount, skippedCount, failures);
    }

    private string? FindManagedSourceLogPath(
        FightCatalogManagementItem fight,
        IReadOnlyList<string> managedRoots,
        IDictionary<string, string> hashCache,
        CancellationToken cancellationToken)
    {
        if (managedRoots.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(fight.SourceFilePath))
        {
            var sourceFilePath = Path.GetFullPath(fight.SourceFilePath);
            if (File.Exists(sourceFilePath)
                && IsSupportedLogFile(sourceFilePath)
                && IsPathInsideAnyDirectory(sourceFilePath, managedRoots)
                && FileHashMatches(sourceFilePath, fight.SourceFileSha256, hashCache, cancellationToken))
            {
                return sourceFilePath;
            }
        }

        foreach (var root in managedRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directPath = Path.Combine(root, fight.SourceFileName);
            if (File.Exists(directPath)
                && IsSupportedLogFile(directPath)
                && FileHashMatches(directPath, fight.SourceFileSha256, hashCache, cancellationToken))
            {
                return Path.GetFullPath(directPath);
            }
        }

        if (string.IsNullOrWhiteSpace(fight.SourceFileSha256))
        {
            return null;
        }

        var sourceStem = Path.GetFileNameWithoutExtension(fight.SourceFileName);
        var sourceExtension = Path.GetExtension(fight.SourceFileName);
        if (string.IsNullOrWhiteSpace(sourceStem) || string.IsNullOrWhiteSpace(sourceExtension))
        {
            return null;
        }

        foreach (var root in managedRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<string> candidatePaths;
            try
            {
                candidatePaths = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Where(path =>
                        string.Equals(Path.GetExtension(path), sourceExtension, StringComparison.OrdinalIgnoreCase)
                        && Path.GetFileNameWithoutExtension(path).StartsWith(sourceStem, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var candidatePath in candidatePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (FileHashMatches(candidatePath, fight.SourceFileSha256, hashCache, cancellationToken))
                {
                    return Path.GetFullPath(candidatePath);
                }
            }
        }

        return null;
    }

    private IReadOnlyList<string> GetManagedLogRoots()
    {
        return new[]
        {
            _paths.ConfiguredPendingDirectoryPath,
            _paths.ConfiguredArchiveLogDirectoryPath
        }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool FileHashMatches(
        string filePath,
        string? expectedSha256,
        IDictionary<string, string> hashCache,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(filePath);
        if (!hashCache.TryGetValue(fullPath, out var actualSha256))
        {
            using var stream = File.OpenRead(fullPath);
            actualSha256 = Convert.ToHexString(SHA256.HashData(stream));
            hashCache[fullPath] = actualSha256;
        }

        return string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedLogFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return SupportedLogExtensions.Any(value => string.Equals(value, extension, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPathInsideAnyDirectory(string path, IReadOnlyList<string> directoryPaths)
    {
        return directoryPaths.Any(directoryPath => IsPathInsideDirectory(path, directoryPath));
    }

    private static string? GetContainingRoot(string path, IReadOnlyList<string> directoryPaths)
    {
        return directoryPaths.FirstOrDefault(directoryPath => IsPathInsideDirectory(path, directoryPath));
    }

    private static bool IsPathInsideDirectory(string path, string directoryPath)
    {
        var normalizedPath = Path.GetFullPath(path);
        var normalizedDirectoryPath = Path.GetFullPath(directoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        return normalizedPath.StartsWith(normalizedDirectoryPath, StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteEmptyDirectoriesUpTo(string? startDirectoryPath, string rootDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(startDirectoryPath))
        {
            return;
        }

        var rootPath = Path.GetFullPath(rootDirectoryPath);
        var currentDirectoryPath = Path.GetFullPath(startDirectoryPath);
        while (IsPathInsideDirectory(currentDirectoryPath, rootPath) && Directory.Exists(currentDirectoryPath))
        {
            try
            {
                if (Directory.EnumerateFileSystemEntries(currentDirectoryPath).Any())
                {
                    return;
                }

                Directory.Delete(currentDirectoryPath);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            currentDirectoryPath = Path.GetDirectoryName(currentDirectoryPath);
            if (string.IsNullOrWhiteSpace(currentDirectoryPath))
            {
                return;
            }
        }
    }

    private sealed record SourceLogDeleteResult(
        int DeletedLogFileCount,
        int MissingLogFileCount,
        int SkippedLogFileCount,
        IReadOnlyList<string> Failures);

    private sealed record AnalysisWarmUpResult(double Seconds, string Message);
}
