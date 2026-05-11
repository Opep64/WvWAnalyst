namespace WvWAnalyst.Api.Bridge;

public static class AppRootResolver
{
    private const string ProjectFileName = "WvWAnalyst.Api.csproj";
    private const string AppSettingsFileName = "appsettings.json";
    private const string WebRootDirectoryName = "wwwroot";

    public static string ResolveContentRoot()
    {
        return FindApiContentRoot(AppContext.BaseDirectory)
            ?? FindApiContentRoot(Directory.GetCurrentDirectory())
            ?? Directory.GetCurrentDirectory();
    }

    private static string? FindApiContentRoot(string startPath)
    {
        var current = GetDirectory(startPath);
        while (current is not null)
        {
            if (IsApiContentRoot(current.FullName))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static DirectoryInfo? GetDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(path);
        return Directory.Exists(fullPath)
            ? new DirectoryInfo(fullPath)
            : new FileInfo(fullPath).Directory;
    }

    private static bool IsApiContentRoot(string path)
    {
        return File.Exists(Path.Combine(path, ProjectFileName)) &&
            File.Exists(Path.Combine(path, AppSettingsFileName)) &&
            Directory.Exists(Path.Combine(path, WebRootDirectoryName));
    }
}
