using System.Globalization;
using System.Text.Json;
using WvWAnalyst.Api.Bridge;

namespace WvWAnalyst.Api.Services;

public sealed class AuditLogService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly object _syncRoot = new();
    private readonly AppPathService _paths;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(AppPathService paths, ILogger<AuditLogService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public void Write(HttpContext context, string action, string status, object? details = null, string? usernameOverride = null)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var auditDirectory = Path.Combine(_paths.StorageRootPath, "audit");
            Directory.CreateDirectory(auditDirectory);
            var auditPath = Path.Combine(auditDirectory, $"audit-{now:yyyy-MM}.jsonl");
            var auditEvent = new AuditLogEvent(
                TimestampUtc: now.ToString("O", CultureInfo.InvariantCulture),
                User: usernameOverride ?? GetUsername(context),
                Action: action,
                Status: status,
                RemoteIp: context.Connection.RemoteIpAddress?.ToString(),
                UserAgent: context.Request.Headers.UserAgent.ToString(),
                Details: details);
            var line = JsonSerializer.Serialize(auditEvent, SerializerOptions);
            lock (_syncRoot)
            {
                File.AppendAllText(auditPath, line + Environment.NewLine);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(exception, "Failed to write audit event {Action}.", action);
        }
    }

    public IReadOnlyList<AuditLogEvent> ReadRecent(int limit)
    {
        var effectiveLimit = Math.Clamp(limit, 1, 500);
        var auditDirectory = Path.Combine(_paths.StorageRootPath, "audit");
        if (!Directory.Exists(auditDirectory))
        {
            return [];
        }

        var events = new List<AuditLogEvent>(effectiveLimit);
        var files = Directory
            .EnumerateFiles(auditDirectory, "audit-*.jsonl", SearchOption.TopDirectoryOnly)
            .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        lock (_syncRoot)
        {
            foreach (var file in files)
            {
                foreach (var line in File.ReadLines(file).Where(line => !string.IsNullOrWhiteSpace(line)).Reverse())
                {
                    if (TryReadEvent(line, out var auditEvent))
                    {
                        events.Add(auditEvent);
                        if (events.Count >= effectiveLimit)
                        {
                            return events;
                        }
                    }
                }
            }
        }

        return events;
    }

    private bool TryReadEvent(string line, out AuditLogEvent auditEvent)
    {
        try
        {
            auditEvent = JsonSerializer.Deserialize<AuditLogEvent>(line, SerializerOptions)
                ?? new AuditLogEvent(string.Empty, string.Empty, string.Empty, string.Empty, null, null, null);
            return !string.IsNullOrWhiteSpace(auditEvent.TimestampUtc);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Skipped malformed audit log entry.");
            auditEvent = new AuditLogEvent(string.Empty, string.Empty, string.Empty, string.Empty, null, null, null);
            return false;
        }
    }

    private static string GetUsername(HttpContext context)
    {
        return context.User.Identity?.IsAuthenticated == true
            ? context.User.Identity.Name ?? "(unknown)"
            : "(anonymous)";
    }
}

public sealed record AuditLogEvent(
    string TimestampUtc,
    string User,
    string Action,
    string Status,
    string? RemoteIp,
    string? UserAgent,
    object? Details);
