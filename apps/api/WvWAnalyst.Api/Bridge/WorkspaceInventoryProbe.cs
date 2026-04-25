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
        var logDirectoryPath = _paths.ConfiguredLogDirectoryPath;
        var parserProbe = _parserCliLocator.Probe(parserPath, _paths.ConfiguredParserCliPath);
        var combinerDetected = Directory.Exists(combinerPath);
        var logDirectoryConfigured = !string.IsNullOrWhiteSpace(logDirectoryPath);
        var logDirectoryDetected = logDirectoryConfigured && Directory.Exists(logDirectoryPath!);

        var notes = parserProbe.ParserCliDetected
            ? $"{parserProbe.Notes} Directory-driven parser ingestion is available in this prototype."
            : $"{parserProbe.Notes} The dashboard can still run, but batch parser ingestion is blocked until the CLI is available.";
        if (!logDirectoryConfigured)
        {
            notes += " Configure Workspace:LogDirectoryPath before using Manage uploads or batch parsing.";
        }

        return new WorkspaceStatusDto(
            ParserPath: parserPath,
            ParserDetected: parserProbe.ParserDetected,
            ParserCliPath: parserProbe.ParserCliPath,
            ParserCliDetected: parserProbe.ParserCliDetected,
            CombinerPath: combinerPath,
            CombinerDetected: combinerDetected,
            LogDirectoryPath: logDirectoryPath,
            LogDirectoryConfigured: logDirectoryConfigured,
            LogDirectoryDetected: logDirectoryDetected,
            Notes: notes);
    }
}
