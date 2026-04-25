using Microsoft.AspNetCore.Http.Features;
using WvWAnalyst.Api.Analysis;
using WvWAnalyst.Api.Bridge;
using WvWAnalyst.Api.Configuration;
using WvWAnalyst.Api.Services;
using WvWAnalyst.Contracts;

var builder = WebApplication.CreateBuilder(args);
const long MaxUploadBodyBytes = 4L * 1024L * 1024L * 1024L;

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = MaxUploadBodyBytes;
});

builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<WorkspaceOptions>(builder.Configuration.GetSection(WorkspaceOptions.SectionName));
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = MaxUploadBodyBytes;
});

builder.Services.AddSingleton<AppPathService>();
builder.Services.AddSingleton<FightTeamScorecardBlueprintFactory>();
builder.Services.AddSingleton<ParserCliLocator>();
builder.Services.AddSingleton<EliteInsightsFightIndexer>();
builder.Services.AddSingleton<WorkspaceInventoryProbe>();
builder.Services.AddSingleton<FightCatalogService>();
builder.Services.AddSingleton<ParserImportService>();
builder.Services.AddSingleton<DirectoryImportJobService>();
builder.Services.AddSingleton<ConfiguredLogDirectoryUploadService>();
builder.Services.AddSingleton<WorkspaceResetService>();
builder.Services.AddSingleton<FightAnalysisService>();
builder.Services.AddSingleton<PrototypeDashboardService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    name = "WvWAnalyst",
    mode = "local-first prototype"
}));

app.MapGet("/api/dashboard", (PrototypeDashboardService service) => Results.Ok(service.BuildSnapshot()));
app.MapGet("/api/analysis/team-fight-scorecard", (PrototypeDashboardService service) => Results.Ok(service.GetTeamFightScorecardBlueprint()));
app.MapGet("/api/analysis", (
    string? commander,
    string? startDate,
    string? endDate,
    string? outcome,
    string? squadIncludeClasses,
    string? squadExcludeClasses,
    string? enemyIncludeClasses,
    string? enemyExcludeClasses,
    FightAnalysisService service) =>
    Results.Ok(service.BuildSnapshot(
        commander,
        startDate,
        endDate,
        outcome,
        squadIncludeClasses,
        squadExcludeClasses,
        enemyIncludeClasses,
        enemyExcludeClasses)));
app.MapGet("/api/fights/{fightId}", (string fightId, FightCatalogService catalog) =>
    catalog.TryGetFightDetail(fightId, out var detail)
        ? Results.Ok(detail)
        : Results.NotFound());
app.MapPost("/api/imports/directory", async (DirectoryImportRequestDto request, AppPathService paths, ConfiguredLogDirectoryUploadService uploadService, ParserImportService service, CancellationToken cancellationToken) =>
{
    if (uploadService.HasActiveUpload())
    {
        return Results.Conflict(new
        {
            message = "A log upload is in progress. Wait for uploads to finish before starting a batch parse.",
            activeUploadCount = uploadService.GetActiveUploadCount()
        });
    }

    var effectiveRequest = request with
    {
        DirectoryPath = paths.ConfiguredLogDirectoryPath ?? string.Empty
    };
    var result = await service.ImportDirectoryAsync(effectiveRequest, cancellationToken);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
}).DisableAntiforgery();
app.MapPost("/api/imports/directory/jobs", (DirectoryImportRequestDto request, AppPathService paths, ConfiguredLogDirectoryUploadService uploadService, DirectoryImportJobService service) =>
{
    if (uploadService.HasActiveUpload())
    {
        return Results.Conflict(new DirectoryImportJobStatusDto(
            JobId: string.Empty,
            State: "blocked",
            Message: "A log upload is in progress. Wait for uploads to finish before starting a batch parse.",
            DirectoryPath: paths.ConfiguredLogDirectoryPath ?? string.Empty,
            Mode: string.Equals(request.Mode, "rebuild-all", StringComparison.OrdinalIgnoreCase) ? "rebuild-all" : "new-only",
            MaxParallelism: request.MaxParallelism is > 0 ? Math.Min(16, request.MaxParallelism.Value) : 4,
            ResetCatalog: false,
            DiscoveredCount: 0,
            CompletedCount: 0,
            ImportedCount: 0,
            SkippedCount: 0,
            ExcludedCount: 0,
            FailedCount: 0,
            CurrentFileName: null,
            CurrentFilePath: null,
            StartedAtUtc: null,
            CompletedAtUtc: null,
            Items: []));
    }

    var effectiveRequest = request with
    {
        DirectoryPath = paths.ConfiguredLogDirectoryPath ?? string.Empty
    };
    if (service.TryStartJob(effectiveRequest, out var status))
    {
        return Results.Accepted($"/api/imports/directory/jobs/{status.JobId}", status);
    }

    return Results.Conflict(status);
}).DisableAntiforgery();
app.MapPost("/api/imports/log-directory/files", async (HttpRequest request, ConfiguredLogDirectoryUploadService service, CancellationToken cancellationToken) =>
{
    var form = await request.ReadFormAsync(cancellationToken);
    var result = await service.SaveFilesAsync(form.Files.ToArray(), cancellationToken);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
}).DisableAntiforgery();
app.MapPost("/api/manage/reset", (AppPathService paths, DirectoryImportJobService jobService, ConfiguredLogDirectoryUploadService uploadService, WorkspaceResetService resetService, CancellationToken cancellationToken) =>
{
    if (jobService.HasRunningJob())
    {
        return Results.Conflict(new WorkspaceResetResultDto(
            Success: false,
            Message: "Wait for the active batch parse to finish before resetting stored logs and fight artifacts.",
            DirectoryPath: paths.ConfiguredLogDirectoryPath,
            DeletedLogFileCount: 0,
            DeletedFightCount: 0,
            DeletedHtmlReportCount: 0,
            DeletedDatabase: false));
    }

    if (uploadService.HasActiveUpload())
    {
        return Results.Conflict(new WorkspaceResetResultDto(
            Success: false,
            Message: "Wait for active uploads to finish before resetting stored logs and fight artifacts.",
            DirectoryPath: paths.ConfiguredLogDirectoryPath,
            DeletedLogFileCount: 0,
            DeletedFightCount: 0,
            DeletedHtmlReportCount: 0,
            DeletedDatabase: false));
    }

    var result = resetService.Reset(cancellationToken);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
}).DisableAntiforgery();
app.MapGet("/api/imports/directory/jobs/{jobId}", (string jobId, DirectoryImportJobService service) =>
    service.TryGetJob(jobId, out var status)
        ? Results.Ok(status)
        : Results.NotFound());
app.MapGet("/api/fights/{fightId}/artifacts/html", (string fightId, HttpContext httpContext, FightCatalogService catalog) =>
{
    if (!catalog.TryGetArtifact(fightId, FightArtifactKind.Html, out var artifactPath, out var contentType))
    {
        return Results.NotFound();
    }

    // EI fight pages reference a number of third-party image hosts. When the same
    // HTML is opened from disk those requests often succeed, but when served from
    // localhost some hosts reject hotlinks based on the document referrer. Match
    // the standalone-file behavior by suppressing the referrer for subresource
    // requests from the artifact page.
    httpContext.Response.Headers["Referrer-Policy"] = "no-referrer";

    return Results.File(artifactPath, contentType);
});
app.MapGet("/api/fights/{fightId}/artifacts/analysis-json", (string fightId, FightCatalogService catalog) =>
    catalog.TryGetArtifact(fightId, FightArtifactKind.AnalysisJson, out var artifactPath, out var contentType)
        ? Results.File(artifactPath, contentType, fileDownloadName: Path.GetFileName(artifactPath))
        : Results.NotFound());
app.MapGet("/api/fights/{fightId}/artifacts/json", (string fightId, FightCatalogService catalog) =>
    catalog.TryGetArtifact(fightId, FightArtifactKind.Json, out var artifactPath, out var contentType)
        ? Results.File(artifactPath, contentType, fileDownloadName: Path.GetFileName(artifactPath))
        : Results.NotFound());
app.MapGet("/api/fights/{fightId}/artifacts/parser-log", (string fightId, FightCatalogService catalog) =>
    catalog.TryGetArtifact(fightId, FightArtifactKind.ParserConsoleLog, out var artifactPath, out var contentType)
        ? Results.File(artifactPath, contentType, fileDownloadName: Path.GetFileName(artifactPath))
        : Results.NotFound());
app.MapGet("/api/fights/{fightId}/artifacts/raw", (string fightId, FightCatalogService catalog) =>
    catalog.TryGetArtifact(fightId, FightArtifactKind.RawLog, out var artifactPath, out var contentType)
        ? Results.File(artifactPath, contentType, fileDownloadName: Path.GetFileName(artifactPath))
        : Results.NotFound());

app.MapFallbackToFile("index.html");

app.Run();
