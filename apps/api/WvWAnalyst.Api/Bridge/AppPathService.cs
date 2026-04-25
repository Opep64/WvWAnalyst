using Microsoft.Extensions.Options;
using WvWAnalyst.Api.Configuration;

namespace WvWAnalyst.Api.Bridge;

public sealed class AppPathService
{
    private readonly string _contentRootPath;
    private readonly StorageOptions _storageOptions;
    private readonly WorkspaceOptions _workspaceOptions;

    public AppPathService(
        IWebHostEnvironment environment,
        IOptions<StorageOptions> storageOptions,
        IOptions<WorkspaceOptions> workspaceOptions)
    {
        _contentRootPath = environment.ContentRootPath;
        _storageOptions = storageOptions.Value;
        _workspaceOptions = workspaceOptions.Value;
    }

    public string StorageRootPath => ResolveConfiguredPath(_storageOptions.RootPath);

    public string DatabasePath => ResolveConfiguredPath(_storageOptions.DatabasePath);

    public string FightsPath => ResolveConfiguredPath(_storageOptions.FightsPath);

    public string CachePath => ResolveConfiguredPath(_storageOptions.CachePath);

    public string ParserWorkspacePath => ResolveConfiguredPath(_workspaceOptions.ParserPath);

    public string CombinerWorkspacePath => ResolveConfiguredPath(_workspaceOptions.CombinerPath);

    public string? ConfiguredLogDirectoryPath =>
        string.IsNullOrWhiteSpace(_workspaceOptions.LogDirectoryPath)
            ? null
            : ResolveConfiguredPath(_workspaceOptions.LogDirectoryPath);

    public string? ConfiguredParserCliPath =>
        string.IsNullOrWhiteSpace(_workspaceOptions.ParserCliPath)
            ? null
            : ResolveConfiguredPath(_workspaceOptions.ParserCliPath);

    public string ResolveConfiguredPath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        return Path.GetFullPath(Path.Combine(_contentRootPath, configuredPath));
    }

    public void EnsureStorageDirectories()
    {
        Directory.CreateDirectory(StorageRootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        Directory.CreateDirectory(FightsPath);
        Directory.CreateDirectory(CachePath);
        if (ConfiguredLogDirectoryPath is { } configuredLogDirectoryPath)
        {
            Directory.CreateDirectory(configuredLogDirectoryPath);
        }
    }
}
