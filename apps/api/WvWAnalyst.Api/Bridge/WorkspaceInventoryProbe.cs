using WvWAnalyst.Contracts;

namespace WvWAnalyst.Api.Bridge;

public sealed class WorkspaceInventoryProbe
{
    private readonly AppPathService _paths;
    private readonly ParserCliLocator _parserCliLocator;

    public WorkspaceInventoryProbe(AppPathService paths, ParserCliLocator parserCliLocator)
    {
        _paths = paths;
        _parserCliLocator = parserCliLocator;
    }

    public WorkspaceStatusDto Inspect()
    {
        var parserPath = _paths.ParserWorkspacePath;
        var combinerPath = _paths.CombinerWorkspacePath;
        var pendingDirectoryPath = _paths.ConfiguredPendingDirectoryPath;
        var archiveLogDirectoryPath = _paths.ConfiguredArchiveLogDirectoryPath;
        var parserProbe = _parserCliLocator.Probe(parserPath, _paths.ConfiguredParserCliPath);
        var combinerDetected = Directory.Exists(combinerPath);
        var pendingDirectoryConfigured = !string.IsNullOrWhiteSpace(pendingDirectoryPath);
        var pendingDirectoryDetected = pendingDirectoryConfigured && Directory.Exists(pendingDirectoryPath!);
        var archiveLogDirectoryConfigured = !string.IsNullOrWhiteSpace(archiveLogDirectoryPath);
        var archiveLogDirectoryDetected = archiveLogDirectoryConfigured && Directory.Exists(archiveLogDirectoryPath!);

        var notes = parserProbe.ParserCliDetected
            ? $"{parserProbe.Notes} Directory-driven parser ingestion is available in this prototype."
            : $"{parserProbe.Notes} The dashboard can still run, but batch parser ingestion is blocked until the CLI is available.";
        if (!pendingDirectoryConfigured || !archiveLogDirectoryConfigured)
        {
            notes += " Configure Workspace:PendingDirectoryPath and Workspace:ArchiveLogDirectoryPath before using Manage uploads or batch parsing.";
        }

        return new WorkspaceStatusDto(
            ParserPath: parserPath,
            ParserDetected: parserProbe.ParserDetected,
            ParserCliPath: parserProbe.ParserCliPath,
            ParserCliDetected: parserProbe.ParserCliDetected,
            CombinerPath: combinerPath,
            CombinerDetected: combinerDetected,
            PendingDirectoryPath: pendingDirectoryPath,
            PendingDirectoryConfigured: pendingDirectoryConfigured,
            PendingDirectoryDetected: pendingDirectoryDetected,
            ArchiveLogDirectoryPath: archiveLogDirectoryPath,
            ArchiveLogDirectoryConfigured: archiveLogDirectoryConfigured,
            ArchiveLogDirectoryDetected: archiveLogDirectoryDetected,
            Notes: notes);
    }
}
