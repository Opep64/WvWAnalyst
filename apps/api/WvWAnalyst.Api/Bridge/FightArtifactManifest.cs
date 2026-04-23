using System.Text.Json.Serialization;

namespace WvWAnalyst.Api.Bridge;

public sealed record FightArtifactManifest(
    string FightId,
    string SourceFileName,
    string? SourceFilePath,
    long SourceFileBytes,
    string? SourceFileSha256,
    string? FightFingerprint,
    DateTime ImportedAtUtc,
    bool Parsed,
    string ParserStatus,
    long ParserElapsedMilliseconds,
    string ParserExecutablePath,
    string ParserConfigRelativePath,
    string? ParserConsoleLogRelativePath,
    bool RawLogRetained,
    string? RawLogRelativePath,
    string? AnalysisJsonArtifactRelativePath,
    string? HtmlArtifactRelativePath,
    string? JsonArtifactRelativePath,
    IReadOnlyList<string> GeneratedArtifactRelativePaths,
    FightIndexSnapshot? FightIndex)
{
    [JsonIgnore]
    public string EffectiveStatus => Parsed ? "Imported" : "Parser failed";
}

public sealed record FightIndexSnapshot(
    int SchemaVersion,
    DateTime IndexedAtUtc,
    WvWAnalyst.Contracts.FightIndexDto Data);
