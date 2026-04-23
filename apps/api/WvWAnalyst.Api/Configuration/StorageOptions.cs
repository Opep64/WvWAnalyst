namespace WvWAnalyst.Api.Configuration;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string RootPath { get; set; } = @"..\..\..\storage";
    public string DatabasePath { get; set; } = @"..\..\..\storage\db\wvw-analyst.db";
    public string FightsPath { get; set; } = @"..\..\..\storage\fights";
    public string CachePath { get; set; } = @"..\..\..\storage\cache";
}
