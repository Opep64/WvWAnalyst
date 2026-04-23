namespace WvWAnalyst.Api.Bridge;

public sealed class ParserCliLocator
{
    private static readonly string[] CandidateRelativePaths =
    [
        Path.Combine("GW2EI.bin", "Debug", "CLI", "GuildWars2EliteInsights-CLI.exe"),
        Path.Combine("GW2EI.bin", "Release", "CLI", "GuildWars2EliteInsights-CLI.exe")
    ];

    public ParserCliProbeResult Probe(string parserPath, string? configuredCliPath)
    {
        var parserDetected = Directory.Exists(parserPath);
        string? parserCliPath = null;
        var notes = new List<string>();

        if (!parserDetected)
        {
            notes.Add("Parser workspace was not found.");
        }

        if (!string.IsNullOrWhiteSpace(configuredCliPath))
        {
            if (File.Exists(configuredCliPath))
            {
                parserCliPath = configuredCliPath;
                notes.Add("Configured parser CLI path is available.");
            }
            else
            {
                notes.Add("Configured parser CLI path was not found, so the workspace was searched.");
            }
        }

        if (parserCliPath is null && parserDetected)
        {
            foreach (var relativePath in CandidateRelativePaths)
            {
                var candidatePath = Path.Combine(parserPath, relativePath);
                if (File.Exists(candidatePath))
                {
                    parserCliPath = candidatePath;
                    notes.Add($"Parser CLI found at {relativePath}.");
                    break;
                }
            }
        }

        if (parserCliPath is null && parserDetected)
        {
            parserCliPath = Directory
                .EnumerateFiles(parserPath, "GuildWars2EliteInsights-CLI.exe", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (parserCliPath is not null)
            {
                notes.Add("Parser CLI was found by recursive search.");
            }
        }

        if (parserCliPath is null && parserDetected)
        {
            notes.Add("Parser CLI executable was not found in the parser workspace.");
        }

        if (notes.Count == 0)
        {
            notes.Add("Parser workspace and CLI are available.");
        }

        return new ParserCliProbeResult(
            ParserPath: parserPath,
            ParserDetected: parserDetected,
            ParserCliPath: parserCliPath,
            ParserCliDetected: parserCliPath is not null,
            Notes: string.Join(" ", notes));
    }
}

public sealed record ParserCliProbeResult(
    string ParserPath,
    bool ParserDetected,
    string? ParserCliPath,
    bool ParserCliDetected,
    string Notes);
