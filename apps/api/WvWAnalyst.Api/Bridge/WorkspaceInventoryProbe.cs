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
        var parserProbe = _parserCliLocator.Probe(parserPath, _paths.ConfiguredParserCliPath);
        var combinerDetected = Directory.Exists(combinerPath);

        var notes = parserProbe.ParserCliDetected
            ? $"{parserProbe.Notes} Directory-driven parser ingestion is available in this prototype."
            : $"{parserProbe.Notes} The dashboard can still run, but batch parser ingestion is blocked until the CLI is available.";

        return new WorkspaceStatusDto(
            ParserPath: parserPath,
            ParserDetected: parserProbe.ParserDetected,
            ParserCliPath: parserProbe.ParserCliPath,
            ParserCliDetected: parserProbe.ParserCliDetected,
            CombinerPath: combinerPath,
            CombinerDetected: combinerDetected,
            Notes: notes);
    }
}
