using WvWAnalyst.Contracts;

namespace WvWAnalyst.ParserBridge;

public sealed class WorkspaceInventoryProbe
{
    public WorkspaceStatusDto Inspect(string parserPath, string combinerPath)
    {
        var parserDetected = Directory.Exists(parserPath);
        var combinerDetected = Directory.Exists(combinerPath);

        var notes = parserDetected && combinerDetected
            ? "Parser and combiner workspaces were found locally. Parser integration is intentionally disabled in this first scaffold."
            : "One or more expected neighbor workspaces were not found. The prototype can still run locally, but import and orchestration work has not been added yet.";

        return new WorkspaceStatusDto(
            ParserPath: parserPath,
            ParserDetected: parserDetected,
            CombinerPath: combinerPath,
            CombinerDetected: combinerDetected,
            Notes: notes);
    }
}
