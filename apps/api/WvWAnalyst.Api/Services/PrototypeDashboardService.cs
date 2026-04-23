using Microsoft.Extensions.Options;
using WvWAnalyst.Api.Analysis;
using WvWAnalyst.Api.Bridge;
using WvWAnalyst.Api.Configuration;
using WvWAnalyst.Contracts;

namespace WvWAnalyst.Api.Services;

public sealed class PrototypeDashboardService
{
    private readonly AppPathService _paths;
    private readonly FightTeamScorecardBlueprintFactory _scorecardFactory;
    private readonly WorkspaceInventoryProbe _workspaceProbe;
    private readonly FightCatalogService _fightCatalog;

    public PrototypeDashboardService(
        AppPathService paths,
        FightTeamScorecardBlueprintFactory scorecardFactory,
        WorkspaceInventoryProbe workspaceProbe,
        FightCatalogService fightCatalog)
    {
        _paths = paths;
        _scorecardFactory = scorecardFactory;
        _workspaceProbe = workspaceProbe;
        _fightCatalog = fightCatalog;
    }

    public DashboardSnapshotDto BuildSnapshot()
    {
        _paths.EnsureStorageDirectories();

        var rootPath = _paths.StorageRootPath;
        var databasePath = _paths.DatabasePath;
        var fightsPath = _paths.FightsPath;
        var cachePath = _paths.CachePath;

        var fightDirectories = new DirectoryInfo(fightsPath)
            .EnumerateDirectories()
            .OrderByDescending(directory => directory.LastWriteTimeUtc)
            .ToList();

        return new DashboardSnapshotDto(
            Application: new ApplicationInfoDto(
                Name: "WvWAnalyst",
                Mode: "Local-first prototype",
                Summary: "A local analyst shell for batch fight ingestion, compact dossiers, scorecards, and storage-aware WvW review.",
                Capabilities:
                [
                    "Prototype dashboard shell",
                    "Directory-driven parser orchestration",
                    "Process-new and rebuild-all batch modes",
                    "Manifest-backed fight indexing",
                    "analysis.json consumption with compact persisted summaries",
                    "Single-fight dossier view",
                    "Fight-level team scorecard blueprint",
                    "Local artifact storage conventions"
                ]),
            Workspace: _workspaceProbe.Inspect(),
            Storage: new StorageStatusDto(
                RootPath: rootPath,
                DatabasePath: databasePath,
                FightsPath: fightsPath,
                CachePath: cachePath,
                FightFolderCount: fightDirectories.Count,
                TotalBytes: GetDirectorySize(new DirectoryInfo(rootPath))),
            TeamFightScorecard: _scorecardFactory.CreateV1(),
            Workstreams:
            [
                new WorkstreamDto(
                    Name: "Batch ingest",
                    Status: "active",
                    Summary: "The prototype now scans log directories, hashes files, skips already-successful imports in new-only mode, and can rebuild the full local catalog from scratch."),
                new WorkstreamDto(
                    Name: "Fight browser",
                    Status: "active",
                    Summary: "Stored fights are browsed separately from parse recency, with compact tables and client-side sort and filter controls."),
                new WorkstreamDto(
                    Name: "Parser bridge",
                    Status: "active",
                    Summary: "The parser bridge now targets analysis.json only for detailed WvW, consumes that payload into compact manifest data, and does not rely on retained HTML or EI raw JSON."),
                new WorkstreamDto(
                    Name: "Storage management",
                    Status: "active",
                    Summary: "The local catalog is intended to keep compact manifest data and parser logs first while treating parser-generated analysis payloads as temporary ingest artifacts.")
            ],
            RecentParses: _fightCatalog.GetRecentParseSummaries(10),
            FightBrowser: _fightCatalog.GetFightBrowserSnapshot(),
            RetentionPolicy: new ArtifactRetentionPolicyDto(
                Summary: "Keep the smallest durable manifest/index data first, treat parser-generated analysis payloads as ingest-only, and avoid retaining large regenerable outputs.",
                KeepAlways:
                [
                    "Fight metadata index",
                    "Compact execution snapshot",
                    "Parser console log",
                    "Pinned fights"
                ],
                PurgeFirst:
                [
                    "Cache files",
                    "Temporary analysis.json payloads after ingest",
                    "Regenerable HTML and EI raw JSON outputs",
                    "Large raw logs after successful analysis retention"
                ]));
    }

    public TeamFightScorecardBlueprintDto GetTeamFightScorecardBlueprint() => _scorecardFactory.CreateV1();

    private static long GetDirectorySize(DirectoryInfo directory)
    {
        if (!directory.Exists)
        {
            return 0;
        }

        long totalBytes = 0;
        foreach (var file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            totalBytes += file.Length;
        }

        return totalBytes;
    }
}
