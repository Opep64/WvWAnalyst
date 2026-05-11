using System.IO.Compression;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;
using WvWAnalyst.Api.Analysis;
using WvWAnalyst.Api.Auth;
using WvWAnalyst.Api.Bridge;
using WvWAnalyst.Api.Configuration;
using WvWAnalyst.Api.Services;
using WvWAnalyst.Contracts;
using AppAuthenticationOptions = WvWAnalyst.Api.Configuration.AuthenticationOptions;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppRootResolver.ResolveContentRoot()
});
if (AuthUserCli.TryRun(args, builder.Configuration, builder.Environment.ContentRootPath))
{
    return;
}

const long MaxUploadBodyBytes = 4L * 1024L * 1024L * 1024L;
const string NoStoreCacheControl = "no-store, no-cache, must-revalidate";

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = MaxUploadBodyBytes;
});

builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<WorkspaceOptions>(builder.Configuration.GetSection(WorkspaceOptions.SectionName));
builder.Services.Configure<AppAuthenticationOptions>(builder.Configuration.GetSection(AppAuthenticationOptions.SectionName));
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = MaxUploadBodyBytes;
});
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/json"]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
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
builder.Services.AddSingleton<CommanderFightManagementService>();
builder.Services.AddSingleton<PatchMetadataService>();
builder.Services.AddSingleton<CompHelperConfigService>();
builder.Services.AddSingleton<FightAttributeService>();
builder.Services.AddSingleton<FightAnalysisService>();
builder.Services.AddSingleton<PrototypeDashboardService>();
builder.Services.AddSingleton<AuthUserStore>();
builder.Services.AddSingleton<PasswordHashService>();
builder.Services.AddSingleton<AuditLogService>();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        var authOptions = builder.Configuration.GetSection(AppAuthenticationOptions.SectionName).Get<AppAuthenticationOptions>() ?? new AppAuthenticationOptions();
        options.Cookie.Name = string.IsNullOrWhiteSpace(authOptions.CookieName) ? "WvWAnalyst.Auth" : authOptions.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();
var authenticationOptions = app.Services.GetRequiredService<IOptions<AppAuthenticationOptions>>().Value;
var appPaths = app.Services.GetRequiredService<AppPathService>();
app.Logger.LogInformation(
    "Using content root {ContentRootPath} and storage root {StorageRootPath}",
    app.Environment.ContentRootPath,
    appPaths.StorageRootPath);

app.UseResponseCompression();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.OnStarting(() =>
        {
            ApplyNoStoreHeaders(context.Response);
            return Task.CompletedTask;
        });
    }

    await next();
});

app.UseAuthentication();
app.UseAuthorization();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = static context => ApplyNoStoreHeaders(context.Context.Response)
});

app.Use(async (context, next) =>
{
    if (!authenticationOptions.Enabled ||
        !context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
        IsPublicApiPath(context.Request.Path) ||
        context.User.Identity?.IsAuthenticated == true)
    {
        await next();
        return;
    }

    ApplyNoStoreHeaders(context.Response);
    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    await context.Response.WriteAsJsonAsync(new { message = "Authentication required." });
});

app.MapGet("/api/auth/me", (HttpContext context) =>
    Results.Ok(new AuthStateDto(
        Enabled: authenticationOptions.Enabled,
        Authenticated: !authenticationOptions.Enabled || context.User.Identity?.IsAuthenticated == true,
        Username: context.User.Identity?.IsAuthenticated == true ? context.User.Identity.Name : null)));

app.MapPost("/api/auth/login", async (
    LoginRequestDto request,
    HttpContext context,
    AuthUserStore users,
    PasswordHashService passwordHashService,
    AuditLogService audit) =>
{
    var username = AuthUserStore.NormalizeUsername(request.Username);
    if (!authenticationOptions.Enabled)
    {
        return Results.Ok(new AuthStateDto(false, true, null));
    }

    var user = users.FindUser(username);
    if (user is null || !passwordHashService.VerifyPassword(request.Password ?? string.Empty, user.PasswordHash))
    {
        audit.Write(context, "login", "failure", new { username }, usernameOverride: username.Length == 0 ? "(blank)" : username);
        return Results.Unauthorized();
    }

    var claims = new[]
    {
        new Claim(ClaimTypes.Name, user.Username)
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);
    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        principal,
        new AuthenticationProperties
        {
            IsPersistent = true,
            IssuedUtc = DateTimeOffset.UtcNow,
            AllowRefresh = true
        });

    audit.Write(context, "login", "success", new { username = user.Username }, usernameOverride: user.Username);
    return Results.Ok(new AuthStateDto(true, true, user.Username));
}).DisableAntiforgery();

app.MapPost("/api/auth/logout", async (HttpContext context, AuditLogService audit) =>
{
    var username = context.User.Identity?.Name;
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    audit.Write(context, "logout", "success", null, usernameOverride: username);
    return Results.Ok(new AuthStateDto(authenticationOptions.Enabled, !authenticationOptions.Enabled, null));
}).DisableAntiforgery();

app.MapPost("/api/auth/change-password", (
    ChangePasswordRequestDto request,
    HttpContext context,
    AuthUserStore users,
    PasswordHashService passwordHashService,
    AuditLogService audit) =>
{
    if (!authenticationOptions.Enabled)
    {
        return Results.BadRequest(new { message = "Authentication is disabled." });
    }

    var username = AuthUserStore.NormalizeUsername(context.User.Identity?.Name);
    if (username.Length == 0)
    {
        return Results.Unauthorized();
    }

    var newPassword = request.NewPassword ?? string.Empty;
    var confirmPassword = request.ConfirmPassword ?? string.Empty;
    if (newPassword.Length == 0)
    {
        return Results.BadRequest(new { message = "New password cannot be empty." });
    }

    if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
    {
        return Results.BadRequest(new { message = "New passwords do not match." });
    }

    var user = users.FindUser(username);
    if (user is null || !passwordHashService.VerifyPassword(request.CurrentPassword ?? string.Empty, user.PasswordHash))
    {
        audit.Write(context, "change-password", "failure", new { reason = "current-password-rejected" }, usernameOverride: username);
        return Results.BadRequest(new { message = "Current password was not accepted." });
    }

    users.UpsertUser(user.Username, passwordHashService.HashPassword(newPassword), out _);
    audit.Write(context, "change-password", "success", null, usernameOverride: user.Username);
    return Results.Ok(new { message = "Password updated." });
}).DisableAntiforgery();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    name = "WvWAnalyst",
    mode = "local-first prototype"
}));

app.MapGet("/api/dashboard", (PrototypeDashboardService service) => Results.Ok(service.BuildSnapshot()));
app.MapGet("/api/audit/events", (int? limit, AuditLogService audit) =>
{
    var effectiveLimit = Math.Clamp(limit ?? 100, 1, 500);
    return Results.Ok(new
    {
        limit = effectiveLimit,
        events = audit.ReadRecent(effectiveLimit)
    });
});
app.MapGet("/api/patch-metadata", (PatchMetadataService service) => Results.Ok(service.GetMetadata()));
app.MapPut("/api/patch-metadata", (PatchMetadataDto request, PatchMetadataService service, FightCatalogService catalog) =>
{
    var metadata = service.SaveMetadata(request);
    catalog.InvalidateCatalogCache();
    return Results.Ok(metadata);
});
app.MapGet("/api/comp-helper-config", (CompHelperConfigService service) => Results.Ok(service.GetConfig()));
app.MapPut("/api/comp-helper-config", (CompHelperConfigDto request, CompHelperConfigService service) =>
{
    var config = service.SaveConfig(request);
    return Results.Ok(config);
});
app.MapPost("/api/comp-helper-config/reset", (CompHelperConfigService service) => Results.Ok(service.ResetToDefault()));
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
    string? patchScope,
    string? patchEraIds,
    string? fightAttributes,
    FightAnalysisService service) =>
    Results.Ok(service.BuildSnapshot(
        commander,
        startDate,
        endDate,
        outcome,
        squadIncludeClasses,
        squadExcludeClasses,
        enemyIncludeClasses,
        enemyExcludeClasses,
        patchScope,
        patchEraIds,
        fightAttributes)));
app.MapGet("/api/analysis/players/{account}", (
    string account,
    string? commander,
    string? startDate,
    string? endDate,
    string? outcome,
    string? squadIncludeClasses,
    string? squadExcludeClasses,
    string? enemyIncludeClasses,
    string? enemyExcludeClasses,
    string? patchScope,
    string? patchEraIds,
    string? fightAttributes,
    FightAnalysisService service) =>
{
    var player = service.BuildPlayerDetail(
        account,
        commander,
        startDate,
        endDate,
        outcome,
        squadIncludeClasses,
        squadExcludeClasses,
        enemyIncludeClasses,
        enemyExcludeClasses,
        patchScope,
        patchEraIds,
        fightAttributes);
    return player is null ? Results.NotFound() : Results.Ok(player);
});
app.MapGet("/api/analysis/player-details", (
    string? commander,
    string? startDate,
    string? endDate,
    string? outcome,
    string? squadIncludeClasses,
    string? squadExcludeClasses,
    string? enemyIncludeClasses,
    string? enemyExcludeClasses,
    string? patchScope,
    string? patchEraIds,
    string? fightAttributes,
    FightAnalysisService service) =>
    Results.Ok(service.BuildPlayerDetails(
        commander,
        startDate,
        endDate,
        outcome,
        squadIncludeClasses,
        squadExcludeClasses,
        enemyIncludeClasses,
        enemyExcludeClasses,
        patchScope,
        patchEraIds,
        fightAttributes)));
app.MapGet("/api/fights/{fightId}", (string fightId, FightCatalogService catalog) =>
    catalog.TryGetFightDetail(fightId, out var detail)
        ? Results.Ok(detail)
        : Results.NotFound());
app.MapPost("/api/imports/directory", async (
    DirectoryImportRequestDto request,
    HttpContext httpContext,
    AppPathService paths,
    ConfiguredLogDirectoryUploadService uploadService,
    ParserImportService service,
    AuditLogService audit,
    CancellationToken cancellationToken) =>
{
    if (uploadService.HasActiveUpload())
    {
        var blocked = new
        {
            message = "A log upload is in progress. Wait for uploads to finish before starting a batch parse.",
            activeUploadCount = uploadService.GetActiveUploadCount()
        };
        audit.Write(httpContext, "parse-directory", "blocked", blocked);
        return Results.Conflict(blocked);
    }

    var effectiveRequest = request with
    {
        DirectoryPath = ResolveImportDirectoryPath(request.Mode, paths)
    };
    var result = await service.ImportDirectoryAsync(effectiveRequest, cancellationToken);
    audit.Write(httpContext, "parse-directory", result.Success ? "success" : "failure", new
    {
        effectiveRequest.Mode,
        effectiveRequest.DirectoryPath,
        result.DiscoveredCount,
        result.ImportedCount,
        result.SkippedCount,
        result.ExcludedCount,
        result.FailedCount,
        result.ResetCatalog
    });
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
}).DisableAntiforgery();
app.MapPost("/api/imports/directory/jobs", (
    DirectoryImportRequestDto request,
    HttpContext httpContext,
    AppPathService paths,
    ConfiguredLogDirectoryUploadService uploadService,
    DirectoryImportJobService service,
    AuditLogService audit) =>
{
    if (uploadService.HasActiveUpload())
    {
        var blocked = new DirectoryImportJobStatusDto(
            JobId: string.Empty,
            State: "blocked",
            Message: "A log upload is in progress. Wait for uploads to finish before starting a batch parse.",
            DirectoryPath: ResolveImportDirectoryPath(request.Mode, paths),
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
            Items: []);
        audit.Write(httpContext, "parse-directory-job", "blocked", new
        {
            request.Mode,
            DirectoryPath = ResolveImportDirectoryPath(request.Mode, paths),
            uploadActiveCount = uploadService.GetActiveUploadCount()
        });
        return Results.Conflict(blocked);
    }

    var effectiveRequest = request with
    {
        DirectoryPath = ResolveImportDirectoryPath(request.Mode, paths)
    };
    if (service.TryStartJob(effectiveRequest, out var status))
    {
        audit.Write(httpContext, "parse-directory-job", "started", new
        {
            status.JobId,
            effectiveRequest.Mode,
            effectiveRequest.DirectoryPath,
            status.MaxParallelism,
            status.ResetCatalog
        });
        return Results.Accepted($"/api/imports/directory/jobs/{status.JobId}", status);
    }

    audit.Write(httpContext, "parse-directory-job", "blocked", new
    {
        status.JobId,
        status.State,
        status.Message,
        effectiveRequest.Mode,
        effectiveRequest.DirectoryPath
    });
    return Results.Conflict(status);
}).DisableAntiforgery();
app.MapPost("/api/imports/log-directory/files", async (
    HttpContext httpContext,
    ConfiguredLogDirectoryUploadService service,
    AuditLogService audit,
    CancellationToken cancellationToken) =>
{
    var form = await httpContext.Request.ReadFormAsync(cancellationToken);
    var result = await service.SaveFilesAsync(form.Files.ToArray(), cancellationToken);
    audit.Write(httpContext, "upload-logs", result.Success ? "success" : "failure", new
    {
        result.UploadedCount,
        result.SavedCount,
        result.SkippedCount,
        FailedCount = result.Items.Count(item => string.Equals(item.Action, "failed", StringComparison.OrdinalIgnoreCase)),
        result.DirectoryPath
    });
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
}).DisableAntiforgery();
app.MapPost("/api/manage/reset", (
    HttpContext httpContext,
    AppPathService paths,
    DirectoryImportJobService jobService,
    ConfiguredLogDirectoryUploadService uploadService,
    WorkspaceResetService resetService,
    AuditLogService audit,
    CancellationToken cancellationToken) =>
{
    if (jobService.HasRunningJob())
    {
        var blocked = new WorkspaceResetResultDto(
            Success: false,
            Message: "Wait for the active batch parse to finish before resetting pending uploads, archived logs, and fight artifacts.",
            DirectoryPath: paths.ConfiguredArchiveLogDirectoryPath ?? paths.ConfiguredPendingDirectoryPath,
            DeletedLogFileCount: 0,
            DeletedFightCount: 0,
            DeletedHtmlReportCount: 0,
            DeletedDatabase: false);
        audit.Write(httpContext, "reset-workspace", "blocked", new { blocked.Message });
        return Results.Conflict(blocked);
    }

    if (uploadService.HasActiveUpload())
    {
        var blocked = new WorkspaceResetResultDto(
            Success: false,
            Message: "Wait for active uploads to finish before resetting pending uploads, archived logs, and fight artifacts.",
            DirectoryPath: paths.ConfiguredArchiveLogDirectoryPath ?? paths.ConfiguredPendingDirectoryPath,
            DeletedLogFileCount: 0,
            DeletedFightCount: 0,
            DeletedHtmlReportCount: 0,
            DeletedDatabase: false);
        audit.Write(httpContext, "reset-workspace", "blocked", new { blocked.Message, activeUploadCount = uploadService.GetActiveUploadCount() });
        return Results.Conflict(blocked);
    }

    var result = resetService.Reset(cancellationToken);
    audit.Write(httpContext, "reset-workspace", result.Success ? "success" : "failure", new
    {
        result.DeletedLogFileCount,
        result.DeletedFightCount,
        result.DeletedHtmlReportCount,
        result.DeletedDatabase,
        result.DirectoryPath
    });
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
}).DisableAntiforgery();
app.MapPost("/api/manage/commanders/delete", (
    DeleteCommanderFightsRequestDto request,
    HttpContext httpContext,
    DirectoryImportJobService jobService,
    ConfiguredLogDirectoryUploadService uploadService,
    CommanderFightManagementService service,
    AuditLogService audit,
    CancellationToken cancellationToken) =>
{
    var commander = request.Commander?.Trim() ?? string.Empty;
    if (jobService.HasRunningJob())
    {
        var blocked = new DeleteCommanderFightsResultDto(
            Success: false,
            Message: "Wait for the active batch parse to finish before deleting fights by commander.",
            Commander: commander,
            MatchedFightCount: 0,
            DeletedFightCount: 0,
            DeletedLogFileCount: 0,
            MissingLogFileCount: 0,
            SkippedLogFileCount: 0,
            AnalysisRecalculationSeconds: 0.0);
        audit.Write(httpContext, "delete-commander-fights", "blocked", new { commander, blocked.Message });
        return Results.Conflict(blocked);
    }

    if (uploadService.HasActiveUpload())
    {
        var blocked = new DeleteCommanderFightsResultDto(
            Success: false,
            Message: "Wait for active uploads to finish before deleting fights by commander.",
            Commander: commander,
            MatchedFightCount: 0,
            DeletedFightCount: 0,
            DeletedLogFileCount: 0,
            MissingLogFileCount: 0,
            SkippedLogFileCount: 0,
            AnalysisRecalculationSeconds: 0.0);
        audit.Write(httpContext, "delete-commander-fights", "blocked", new { commander, blocked.Message, activeUploadCount = uploadService.GetActiveUploadCount() });
        return Results.Conflict(blocked);
    }

    var result = service.DeleteCommanderFights(commander, cancellationToken);
    audit.Write(httpContext, "delete-commander-fights", result.Success ? "success" : "failure", new
    {
        result.Commander,
        result.MatchedFightCount,
        result.DeletedFightCount,
        result.DeletedLogFileCount,
        result.MissingLogFileCount,
        result.SkippedLogFileCount
    });
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
}).DisableAntiforgery();
app.MapPost("/api/manage/date-range/delete", (
    DeleteDateRangeFightsRequestDto request,
    HttpContext httpContext,
    DirectoryImportJobService jobService,
    ConfiguredLogDirectoryUploadService uploadService,
    CommanderFightManagementService service,
    AuditLogService audit,
    CancellationToken cancellationToken) =>
{
    var startDate = request.StartDate?.Trim() ?? string.Empty;
    var endDate = request.EndDate?.Trim() ?? string.Empty;
    if (jobService.HasRunningJob())
    {
        var blocked = new DeleteDateRangeFightsResultDto(
            Success: false,
            Message: "Wait for the active batch parse to finish before deleting fights by date range.",
            StartDate: startDate,
            EndDate: endDate,
            MatchedFightCount: 0,
            DeletedFightCount: 0,
            DeletedLogFileCount: 0,
            MissingLogFileCount: 0,
            SkippedLogFileCount: 0,
            AnalysisRecalculationSeconds: 0.0);
        audit.Write(httpContext, "delete-date-range-fights", "blocked", new { startDate, endDate, blocked.Message });
        return Results.Conflict(blocked);
    }

    if (uploadService.HasActiveUpload())
    {
        var blocked = new DeleteDateRangeFightsResultDto(
            Success: false,
            Message: "Wait for active uploads to finish before deleting fights by date range.",
            StartDate: startDate,
            EndDate: endDate,
            MatchedFightCount: 0,
            DeletedFightCount: 0,
            DeletedLogFileCount: 0,
            MissingLogFileCount: 0,
            SkippedLogFileCount: 0,
            AnalysisRecalculationSeconds: 0.0);
        audit.Write(httpContext, "delete-date-range-fights", "blocked", new { startDate, endDate, blocked.Message, activeUploadCount = uploadService.GetActiveUploadCount() });
        return Results.Conflict(blocked);
    }

    var result = service.DeleteDateRangeFights(startDate, endDate, cancellationToken);
    audit.Write(httpContext, "delete-date-range-fights", result.Success ? "success" : "failure", new
    {
        result.StartDate,
        result.EndDate,
        result.MatchedFightCount,
        result.DeletedFightCount,
        result.DeletedLogFileCount,
        result.MissingLogFileCount,
        result.SkippedLogFileCount
    });
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

app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = static context => ApplyNoStoreHeaders(context.Context.Response)
});

app.Run();

static void ApplyNoStoreHeaders(HttpResponse response)
{
    response.Headers["Cache-Control"] = NoStoreCacheControl;
    response.Headers["Pragma"] = "no-cache";
    response.Headers["Expires"] = "0";
}

static bool IsPublicApiPath(PathString path)
{
    var value = path.Value ?? string.Empty;
    return string.Equals(value, "/api/auth/me", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "/api/auth/login", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "/api/auth/logout", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "/api/health", StringComparison.OrdinalIgnoreCase);
}

static string ResolveImportDirectoryPath(string? mode, AppPathService paths)
{
    return string.Equals(mode, "rebuild-all", StringComparison.OrdinalIgnoreCase)
        ? paths.ConfiguredArchiveLogDirectoryPath ?? string.Empty
        : paths.ConfiguredPendingDirectoryPath ?? string.Empty;
}
