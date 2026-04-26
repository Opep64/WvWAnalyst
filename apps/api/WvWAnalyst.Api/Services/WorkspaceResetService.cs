using WvWAnalyst.Api.Bridge;
using WvWAnalyst.Contracts;

namespace WvWAnalyst.Api.Services;

public sealed class WorkspaceResetService
{
    private static readonly string[] SupportedLogEndings =
    [
        ".evtc",
        ".zevtc",
        ".zip",
    ];

    private readonly AppPathService _paths;
    private readonly FightCatalogService _fightCatalog;
    private readonly ILogger<WorkspaceResetService> _logger;

    public WorkspaceResetService(
        AppPathService paths,
        FightCatalogService fightCatalog,
        ILogger<WorkspaceResetService> logger)
    {
        _paths = paths;
        _fightCatalog = fightCatalog;
        _logger = logger;
    }

    public WorkspaceResetResultDto Reset(CancellationToken cancellationToken)
    {
        var pendingDirectoryPath = _paths.ConfiguredPendingDirectoryPath;
        var archiveLogDirectoryPath = _paths.ConfiguredArchiveLogDirectoryPath;
        try
        {
            _paths.EnsureStorageDirectories();

            var deletedLogFileCount = DeleteConfiguredLogFiles(pendingDirectoryPath, cancellationToken) +
                DeleteConfiguredLogFiles(archiveLogDirectoryPath, cancellationToken);
            var deletedFightCount = CountFightDirectories();
            var deletedHtmlReportCount = CountStoredHtmlReports();
            var deletedDatabase = File.Exists(_paths.DatabasePath);

            _fightCatalog.ResetCatalog();

            var message = BuildResetMessage(
                pendingDirectoryPath,
                archiveLogDirectoryPath,
                deletedLogFileCount,
                deletedFightCount,
                deletedHtmlReportCount,
                deletedDatabase);

            return new WorkspaceResetResultDto(
                Success: true,
                Message: message,
                DirectoryPath: archiveLogDirectoryPath ?? pendingDirectoryPath,
                DeletedLogFileCount: deletedLogFileCount,
                DeletedFightCount: deletedFightCount,
                DeletedHtmlReportCount: deletedHtmlReportCount,
                DeletedDatabase: deletedDatabase);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to reset the configured logs and stored fight artifacts.");
            return new WorkspaceResetResultDto(
                Success: false,
                Message: exception.Message,
                DirectoryPath: archiveLogDirectoryPath ?? pendingDirectoryPath,
                DeletedLogFileCount: 0,
                DeletedFightCount: 0,
                DeletedHtmlReportCount: 0,
                DeletedDatabase: false);
        }
    }

    private int DeleteConfiguredLogFiles(string? directoryPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return 0;
        }

        var supportedFiles = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
            .Where(IsSupportedLogFile)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var filePath in supportedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(filePath);
        }

        DeleteEmptyDirectories(directoryPath);
        return supportedFiles.Length;
    }

    private int CountFightDirectories()
    {
        var fightsRoot = new DirectoryInfo(_paths.FightsPath);
        return fightsRoot.Exists
            ? fightsRoot.EnumerateDirectories().Count()
            : 0;
    }

    private int CountStoredHtmlReports()
    {
        var fightsRoot = new DirectoryInfo(_paths.FightsPath);
        return fightsRoot.Exists
            ? fightsRoot.EnumerateFiles("*.html", SearchOption.AllDirectories).Count()
            : 0;
    }

    private static string BuildResetMessage(
        string? pendingDirectoryPath,
        string? archiveLogDirectoryPath,
        int deletedLogFileCount,
        int deletedFightCount,
        int deletedHtmlReportCount,
        bool deletedDatabase)
    {
        var summaryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(pendingDirectoryPath) && !string.IsNullOrWhiteSpace(archiveLogDirectoryPath))
        {
            summaryParts.Add($"Deleted {deletedLogFileCount} supported log file(s) from the pending queue at {pendingDirectoryPath} and the archived log store at {archiveLogDirectoryPath}.");
        }
        else if (!string.IsNullOrWhiteSpace(pendingDirectoryPath))
        {
            summaryParts.Add($"Deleted {deletedLogFileCount} supported log file(s) from {pendingDirectoryPath}.");
        }
        else if (!string.IsNullOrWhiteSpace(archiveLogDirectoryPath))
        {
            summaryParts.Add($"Deleted {deletedLogFileCount} supported log file(s) from {archiveLogDirectoryPath}.");
        }
        else
        {
            summaryParts.Add("No configured pending or archive log directory was set, so no uploaded log files were removed.");
        }

        summaryParts.Add($"Cleared {deletedFightCount} stored fight folder(s).");

        if (deletedHtmlReportCount > 0)
        {
            summaryParts.Add($"Removed {deletedHtmlReportCount} retained HTML report(s).");
        }

        if (deletedDatabase)
        {
            summaryParts.Add("Deleted the local catalog database.");
        }

        return string.Join(" ", summaryParts);
    }

    private static void DeleteEmptyDirectories(string directoryPath)
    {
        foreach (var subdirectoryPath in Directory.GetDirectories(directoryPath))
        {
            DeleteEmptyDirectories(subdirectoryPath);
            if (!Directory.EnumerateFileSystemEntries(subdirectoryPath).Any())
            {
                Directory.Delete(subdirectoryPath);
            }
        }
    }

    private static bool IsSupportedLogFile(string filePath) =>
        SupportedLogEndings.Any(ending => filePath.EndsWith(ending, StringComparison.OrdinalIgnoreCase));
}
