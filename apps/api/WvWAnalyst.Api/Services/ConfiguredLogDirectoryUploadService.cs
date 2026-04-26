using WvWAnalyst.Api.Bridge;
using WvWAnalyst.Contracts;

namespace WvWAnalyst.Api.Services;

public sealed class ConfiguredLogDirectoryUploadService
{
    private static readonly string[] SupportedLogEndings =
    [
        ".evtc",
        ".zevtc",
        ".zip",
    ];

    private readonly AppPathService _paths;
    private readonly ILogger<ConfiguredLogDirectoryUploadService> _logger;
    private int _activeUploadCount;

    public ConfiguredLogDirectoryUploadService(
        AppPathService paths,
        ILogger<ConfiguredLogDirectoryUploadService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task<ConfiguredLogDirectoryUploadResultDto> SaveFilesAsync(IReadOnlyList<IFormFile> files, CancellationToken cancellationToken)
    {
        var directoryPath = _paths.ConfiguredPendingDirectoryPath;
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return new ConfiguredLogDirectoryUploadResultDto(
                Success: false,
                Message: "Workspace:PendingDirectoryPath is not configured.",
                DirectoryPath: null,
                UploadedCount: 0,
                SavedCount: 0,
                SkippedCount: 0,
                Items: []);
        }

        if (files.Count == 0)
        {
            return new ConfiguredLogDirectoryUploadResultDto(
                Success: false,
                Message: "Select or drop one or more log files first.",
                DirectoryPath: directoryPath,
                UploadedCount: 0,
                SavedCount: 0,
                SkippedCount: 0,
                Items: []);
        }

        Directory.CreateDirectory(directoryPath);

        Interlocked.Increment(ref _activeUploadCount);
        try
        {
            var items = new List<ConfiguredLogDirectoryUploadItemDto>(files.Count);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileName = Path.GetFileName(file.FileName ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    items.Add(new ConfiguredLogDirectoryUploadItemDto(
                        FileName: "(unnamed)",
                        Action: "skipped",
                        Message: "Skipped a file with no usable file name.",
                        SavedAs: null));
                    continue;
                }

                if (!IsSupportedLogFile(fileName))
                {
                    items.Add(new ConfiguredLogDirectoryUploadItemDto(
                        FileName: fileName,
                        Action: "skipped",
                        Message: "Only .evtc, .zevtc, and .zip files are accepted.",
                        SavedAs: null));
                    continue;
                }

                if (file.Length <= 0)
                {
                    items.Add(new ConfiguredLogDirectoryUploadItemDto(
                        FileName: fileName,
                        Action: "skipped",
                        Message: "Skipped an empty file.",
                        SavedAs: null));
                    continue;
                }

                var destinationPath = Path.Combine(directoryPath, fileName);
                if (File.Exists(destinationPath))
                {
                    items.Add(new ConfiguredLogDirectoryUploadItemDto(
                        FileName: fileName,
                        Action: "skipped",
                        Message: "Skipped because a file with the same name already exists in the pending upload directory.",
                        SavedAs: null));
                    continue;
                }

                try
                {
                    await using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    await file.CopyToAsync(output, cancellationToken);

                    items.Add(new ConfiguredLogDirectoryUploadItemDto(
                        FileName: fileName,
                        Action: "saved",
                        Message: "Saved to the pending upload directory.",
                        SavedAs: Path.GetFileName(destinationPath)));
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to save uploaded log file {FileName} to {DirectoryPath}", fileName, directoryPath);
                    items.Add(new ConfiguredLogDirectoryUploadItemDto(
                        FileName: fileName,
                        Action: "failed",
                        Message: exception.Message,
                        SavedAs: null));
                }
            }

            var savedCount = items.Count(item => item.Action is "saved");
            var skippedCount = items.Count - savedCount;
            var success = savedCount > 0;
            var message = success
                ? $"Saved {savedCount} of {files.Count} uploaded files to {directoryPath}."
                : $"No uploaded files were saved to {directoryPath}.";

            return new ConfiguredLogDirectoryUploadResultDto(
                Success: success,
                Message: message,
                DirectoryPath: directoryPath,
                UploadedCount: files.Count,
                SavedCount: savedCount,
                SkippedCount: skippedCount,
                Items: items);
        }
        finally
        {
            Interlocked.Decrement(ref _activeUploadCount);
        }
    }

    public bool HasActiveUpload() => Volatile.Read(ref _activeUploadCount) > 0;

    public int GetActiveUploadCount() => Volatile.Read(ref _activeUploadCount);

    private static bool IsSupportedLogFile(string fileName) =>
        SupportedLogEndings.Any(ending => fileName.EndsWith(ending, StringComparison.OrdinalIgnoreCase));
}
