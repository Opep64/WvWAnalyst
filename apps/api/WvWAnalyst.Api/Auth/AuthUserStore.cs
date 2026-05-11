using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WvWAnalyst.Api.Bridge;
using WvWAnalyst.Api.Configuration;

namespace WvWAnalyst.Api.Auth;

public sealed class AuthUserStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _syncRoot = new();
    private readonly string _usersPath;

    public AuthUserStore(AppPathService paths, IOptions<AuthenticationOptions> options)
        : this(paths.ResolveConfiguredPath(options.Value.UsersPath))
    {
    }

    public AuthUserStore(string usersPath)
    {
        _usersPath = usersPath;
    }

    public string UsersPath => _usersPath;

    public IReadOnlyList<AuthUserRecord> GetUsers()
    {
        lock (_syncRoot)
        {
            return ReadFile().Users
                .OrderBy(user => user.Username, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public AuthUserRecord? FindUser(string? username)
    {
        var normalized = NormalizeUsername(username);
        if (normalized.Length == 0)
        {
            return null;
        }

        lock (_syncRoot)
        {
            return ReadFile().Users.FirstOrDefault(user => string.Equals(user.Username, normalized, StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool UpsertUser(string username, string passwordHash, out bool created)
    {
        var normalized = NormalizeUsername(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalized);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        lock (_syncRoot)
        {
            var file = ReadFile();
            var users = file.Users.ToList();
            int existingIndex = users.FindIndex(user => string.Equals(user.Username, normalized, StringComparison.OrdinalIgnoreCase));
            created = existingIndex < 0;
            var record = new AuthUserRecord(normalized, passwordHash, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            if (existingIndex >= 0)
            {
                users[existingIndex] = record;
            }
            else
            {
                users.Add(record);
            }

            WriteFile(new AuthUsersFile(users.OrderBy(user => user.Username, StringComparer.OrdinalIgnoreCase).ToArray()));
            return true;
        }
    }

    public bool RemoveUser(string username)
    {
        var normalized = NormalizeUsername(username);
        if (normalized.Length == 0)
        {
            return false;
        }

        lock (_syncRoot)
        {
            var file = ReadFile();
            var users = file.Users
                .Where(user => !string.Equals(user.Username, normalized, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (users.Length == file.Users.Count)
            {
                return false;
            }

            WriteFile(new AuthUsersFile(users));
            return true;
        }
    }

    public static string NormalizeUsername(string? username)
    {
        return (username ?? string.Empty).Trim();
    }

    public static string ResolveUsersPath(string contentRootPath, string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        return Path.GetFullPath(Path.Combine(contentRootPath, configuredPath));
    }

    private AuthUsersFile ReadFile()
    {
        if (!File.Exists(_usersPath))
        {
            return new AuthUsersFile(Array.Empty<AuthUserRecord>());
        }

        using var stream = File.OpenRead(_usersPath);
        return JsonSerializer.Deserialize<AuthUsersFile>(stream, SerializerOptions)
            ?? new AuthUsersFile(Array.Empty<AuthUserRecord>());
    }

    private void WriteFile(AuthUsersFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_usersPath)!);
        var temporaryPath = $"{_usersPath}.tmp";
        using (var stream = File.Create(temporaryPath))
        {
            JsonSerializer.Serialize(stream, file, SerializerOptions);
        }

        File.Move(temporaryPath, _usersPath, overwrite: true);
    }
}

public sealed record AuthUsersFile(IReadOnlyList<AuthUserRecord> Users);

public sealed record AuthUserRecord(string Username, string PasswordHash, string UpdatedAtUtc);
