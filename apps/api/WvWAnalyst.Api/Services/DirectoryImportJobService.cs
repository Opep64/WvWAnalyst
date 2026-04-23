using System.Collections.Concurrent;
using WvWAnalyst.Api.Bridge;
using WvWAnalyst.Contracts;

namespace WvWAnalyst.Api.Services;

public sealed class DirectoryImportJobService
{
    private readonly ParserImportService _parserImportService;
    private readonly ILogger<DirectoryImportJobService> _logger;
    private readonly ConcurrentDictionary<string, DirectoryImportJobState> _jobs = new(StringComparer.OrdinalIgnoreCase);

    public DirectoryImportJobService(
        ParserImportService parserImportService,
        ILogger<DirectoryImportJobService> logger)
    {
        _parserImportService = parserImportService;
        _logger = logger;
    }

    public bool TryStartJob(DirectoryImportRequestDto request, out DirectoryImportJobStatusDto status)
    {
        var runningJob = _jobs.Values.FirstOrDefault(job => string.Equals(job.State, "running", StringComparison.OrdinalIgnoreCase));
        if (runningJob is not null)
        {
            status = runningJob.ToDto();
            return false;
        }

        var directoryPath = request.DirectoryPath?.Trim() ?? string.Empty;
        var mode = NormalizeMode(request.Mode);
        var maxParallelism = NormalizeMaxParallelism(request.MaxParallelism);
        var jobId = Guid.NewGuid().ToString("N");
        var startedAtUtc = DateTimeOffset.UtcNow;
        var jobState = new DirectoryImportJobState(
            jobId,
            directoryPath,
            mode,
            maxParallelism,
            string.Equals(mode, "rebuild-all", StringComparison.Ordinal),
            startedAtUtc);

        _jobs[jobId] = jobState;
        status = jobState.ToDto();

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _parserImportService.ImportDirectoryAsync(
                    new DirectoryImportRequestDto(directoryPath, mode, maxParallelism),
                    progress => jobState.ApplyProgress(progress),
                    CancellationToken.None);

                jobState.Complete(result);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Directory import job {JobId} failed", jobId);
                jobState.Fail(exception.Message);
            }
        });

        return true;
    }

    public bool TryGetJob(string jobId, out DirectoryImportJobStatusDto status)
    {
        if (_jobs.TryGetValue(jobId, out var jobState))
        {
            status = jobState.ToDto();
            return true;
        }

        status = default!;
        return false;
    }

    private static string NormalizeMode(string? mode)
    {
        return string.Equals(mode, "rebuild-all", StringComparison.OrdinalIgnoreCase)
            ? "rebuild-all"
            : "new-only";
    }

    private static int NormalizeMaxParallelism(int? maxParallelism)
    {
        if (!maxParallelism.HasValue || maxParallelism.Value <= 0)
        {
            return 4;
        }

        return Math.Min(16, maxParallelism.Value);
    }

    private sealed class DirectoryImportJobState
    {
        private readonly object _gate = new();
        private readonly List<DirectoryImportItemDto> _items = [];

        public DirectoryImportJobState(
            string jobId,
            string directoryPath,
            string mode,
            int maxParallelism,
            bool resetCatalog,
            DateTimeOffset startedAtUtc)
        {
            JobId = jobId;
            DirectoryPath = directoryPath;
            Mode = mode;
            MaxParallelism = maxParallelism;
            ResetCatalog = resetCatalog;
            StartedAtUtc = startedAtUtc;
            State = "running";
            Message = "Scanning the directory for supported log files.";
        }

        public string JobId { get; }

        public string DirectoryPath { get; }

        public string Mode { get; }

        public int MaxParallelism { get; }

        public bool ResetCatalog { get; }

        public DateTimeOffset StartedAtUtc { get; }

        public DateTimeOffset? CompletedAtUtc { get; private set; }

        public string State { get; private set; }

        public string Message { get; private set; }

        public int DiscoveredCount { get; private set; }

        public int CompletedCount { get; private set; }

        public int ImportedCount { get; private set; }

        public int SkippedCount { get; private set; }

        public int ExcludedCount { get; private set; }

        public int FailedCount { get; private set; }

        public string? CurrentFileName { get; private set; }

        public string? CurrentFilePath { get; private set; }

        public void ApplyProgress(DirectoryImportProgressUpdate progress)
        {
            lock (_gate)
            {
                if (!string.Equals(State, "running", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                Message = progress.Message;
                DiscoveredCount = progress.DiscoveredCount;
                CompletedCount = progress.CompletedCount;
                ImportedCount = progress.ImportedCount;
                SkippedCount = progress.SkippedCount;
                ExcludedCount = progress.ExcludedCount;
                FailedCount = progress.FailedCount;
                CurrentFileName = progress.CurrentFileName;
                CurrentFilePath = progress.CurrentFilePath;

                if (progress.LatestItem is not null)
                {
                    _items.Add(progress.LatestItem);
                }
            }
        }

        public void Complete(DirectoryImportResultDto result)
        {
            lock (_gate)
            {
                State = result.Success ? "completed" : "failed";
                Message = result.Message;
                DiscoveredCount = result.DiscoveredCount;
                CompletedCount = result.ImportedCount + result.SkippedCount + result.ExcludedCount + result.FailedCount;
                ImportedCount = result.ImportedCount;
                SkippedCount = result.SkippedCount;
                ExcludedCount = result.ExcludedCount;
                FailedCount = result.FailedCount;
                CurrentFileName = null;
                CurrentFilePath = null;
                CompletedAtUtc = DateTimeOffset.UtcNow;
                _items.Clear();
                _items.AddRange(result.Items);
            }
        }

        public void Fail(string message)
        {
            lock (_gate)
            {
                State = "failed";
                Message = message;
                CompletedAtUtc = DateTimeOffset.UtcNow;
                CurrentFileName = null;
                CurrentFilePath = null;
            }
        }

        public DirectoryImportJobStatusDto ToDto()
        {
            lock (_gate)
            {
                return new DirectoryImportJobStatusDto(
                    JobId: JobId,
                    State: State,
                    Message: Message,
                    DirectoryPath: DirectoryPath,
                    Mode: Mode,
                    MaxParallelism: MaxParallelism,
                    ResetCatalog: ResetCatalog,
                    DiscoveredCount: DiscoveredCount,
                    CompletedCount: CompletedCount,
                    ImportedCount: ImportedCount,
                    SkippedCount: SkippedCount,
                    ExcludedCount: ExcludedCount,
                    FailedCount: FailedCount,
                    CurrentFileName: CurrentFileName,
                    CurrentFilePath: CurrentFilePath,
                    StartedAtUtc: StartedAtUtc.ToString("O"),
                    CompletedAtUtc: CompletedAtUtc?.ToString("O"),
                    Items: _items.ToArray());
            }
        }
    }
}
