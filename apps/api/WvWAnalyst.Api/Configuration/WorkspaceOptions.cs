namespace WvWAnalyst.Api.Configuration;

public sealed class WorkspaceOptions
{
    public const string SectionName = "Workspace";

    public string ParserPath { get; set; } = @"..\..\..\..\GW2-Elite-Insights-Parser";
    public string? ParserCliPath { get; set; } = @"..\..\..\..\GW2-Elite-Insights-Parser\GW2EI.bin\Debug\CLI\GuildWars2EliteInsights-CLI.exe";
    public string CombinerPath { get; set; } = @"..\..\..\..\GW2_EI_log_combiner";
    public string? LogDirectoryPath { get; set; } = @"..\..\..\storage\logs";
}
