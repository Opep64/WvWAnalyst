using System.Globalization;
using System.Text.Json;
using WvWAnalyst.Api.Bridge;
using WvWAnalyst.Contracts;

namespace WvWAnalyst.Api.Services;

public sealed class CompHelperConfigService
{
    private const string SchemaVersion = "1.0";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly CompHelperLaneTargetDto[] DefaultLaneTargets =
    [
        new("pressure", "Pressure", 85, 125, 1.30),
        new("boonsupport", "Boon Support", 80, 120, 1.25),
        new("recovery", "Recovery", 70, 105, 1.15),
        new("prevention", "Prevention", 60, 95, 1.05),
        new("conversion", "Conversion", 60, 95, 1.00),
        new("strip", "Strip", 55, 85, 0.95),
        new("control", "Control", 50, 80, 0.90),
        new("rez", "Rez", 25, 50, 0.55)
    ];

    private static readonly CompHelperPackageTargetDto[] DefaultPackageTargets =
    [
        new("stability", "Stability", 60, 95, 1.50, true, false),
        new("healing", "Healing", 60, 95, 1.45, true, true),
        new("cleanse", "Cleanse", 55, 90, 1.40, true, true),
        new("protection", "Protection", 50, 85, 1.20, true, true),
        new("pressure-package", "Pressure", 75, 115, 1.35, true, true),
        new("barrier", "Barrier", 0, 65, 0.55, false, false),
        new("might", "Might", 0, 85, 0.70, false, true),
        new("strip-package", "Strip", 40, 75, 0.90, false, true),
        new("fury", "Fury", 0, 65, 0.55, false, true),
        new("quickness", "Quickness", 0, 65, 0.55, false, true),
        new("resistance", "Resistance", 0, 45, 0.35, false, true),
        new("cc", "CC (combined)", 35, 65, 0.70, false, true)
    ];

    private readonly AppPathService _paths;
    private readonly object _lock = new();
    private CompHelperConfigDto? _cachedConfig;

    public CompHelperConfigService(AppPathService paths)
    {
        _paths = paths;
    }

    private string ConfigPath => Path.Combine(_paths.StorageRootPath, "comp-helper-config.json");
    private string DefaultConfigPath => Path.Combine(_paths.ContentRootPath, "Configuration", "comp-helper-defaults.json");

    public CompHelperConfigDto GetConfig()
    {
        lock (_lock)
        {
            if (_cachedConfig is not null)
            {
                return _cachedConfig;
            }

            _paths.EnsureStorageDirectories();
            if (!File.Exists(ConfigPath))
            {
                _cachedConfig = CreateDefaultConfig(updateTimestamp: false);
                TryWriteConfig(_cachedConfig);
                return _cachedConfig;
            }

            try
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<CompHelperConfigDto>(json, SerializerOptions);
                _cachedConfig = NormalizeConfig(config ?? CreateDefaultConfig(updateTimestamp: false), CreateDefaultConfig(updateTimestamp: false), updateTimestamp: false);
            }
            catch (JsonException)
            {
                _cachedConfig = CreateDefaultConfig(updateTimestamp: false);
            }
            catch (IOException)
            {
                _cachedConfig = CreateDefaultConfig(updateTimestamp: false);
            }

            return _cachedConfig;
        }
    }

    public CompHelperConfigDto SaveConfig(CompHelperConfigDto config)
    {
        var normalized = NormalizeConfig(config, CreateDefaultConfig(updateTimestamp: false), updateTimestamp: true);
        lock (_lock)
        {
            _paths.EnsureStorageDirectories();
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(normalized, SerializerOptions));
            _cachedConfig = normalized;
        }

        return normalized;
    }

    public CompHelperConfigDto ResetToDefault()
    {
        var defaults = CreateDefaultConfig(updateTimestamp: true);
        lock (_lock)
        {
            _paths.EnsureStorageDirectories();
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(defaults, SerializerOptions));
            _cachedConfig = defaults;
        }

        return defaults;
    }

    private CompHelperConfigDto CreateDefaultConfig(bool updateTimestamp)
    {
        var hardcodedDefaults = CreateHardcodedDefaultConfig(updateTimestamp: false);
        var shippedDefaults = TryReadShippedDefaultConfig() ?? hardcodedDefaults;
        return NormalizeConfig(shippedDefaults, hardcodedDefaults, updateTimestamp);
    }

    private static CompHelperConfigDto CreateHardcodedDefaultConfig(bool updateTimestamp)
    {
        return new CompHelperConfigDto(
            SchemaVersion: SchemaVersion,
            UpdatedAtUtc: updateTimestamp
                ? DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                : "2026-04-30T00:00:00.0000000Z",
            LaneTargets: DefaultLaneTargets,
            PackageTargets: DefaultPackageTargets);
    }

    private CompHelperConfigDto? TryReadShippedDefaultConfig()
    {
        try
        {
            if (!File.Exists(DefaultConfigPath))
            {
                return null;
            }

            var json = File.ReadAllText(DefaultConfigPath);
            return JsonSerializer.Deserialize<CompHelperConfigDto>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static CompHelperConfigDto NormalizeConfig(CompHelperConfigDto config, CompHelperConfigDto defaults, bool updateTimestamp)
    {
        var laneLookup = (config.LaneTargets ?? Array.Empty<CompHelperLaneTargetDto>())
            .Where(target => !string.IsNullOrWhiteSpace(target.Key))
            .GroupBy(target => target.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var packageLookup = (config.PackageTargets ?? Array.Empty<CompHelperPackageTargetDto>())
            .Where(target => !string.IsNullOrWhiteSpace(target.Key))
            .GroupBy(target => target.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        var defaultLaneTargets = defaults.LaneTargets.Count > 0 ? defaults.LaneTargets : DefaultLaneTargets;
        var defaultPackageTargets = defaults.PackageTargets.Count > 0 ? defaults.PackageTargets : DefaultPackageTargets;

        var laneTargets = defaultLaneTargets
            .Select(defaultTarget => laneLookup.TryGetValue(defaultTarget.Key, out var target)
                ? NormalizeLaneTarget(target, defaultTarget)
                : defaultTarget)
            .ToArray();
        var packageTargets = defaultPackageTargets
            .Select(defaultTarget => packageLookup.TryGetValue(defaultTarget.Key, out var target)
                ? NormalizePackageTarget(target, defaultTarget)
                : defaultTarget)
            .ToArray();

        return new CompHelperConfigDto(
            SchemaVersion: string.IsNullOrWhiteSpace(config.SchemaVersion) ? SchemaVersion : config.SchemaVersion.Trim(),
            UpdatedAtUtc: updateTimestamp
                ? DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                : string.IsNullOrWhiteSpace(config.UpdatedAtUtc)
                    ? DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                    : config.UpdatedAtUtc.Trim(),
            LaneTargets: laneTargets,
            PackageTargets: packageTargets);
    }

    private static CompHelperLaneTargetDto NormalizeLaneTarget(CompHelperLaneTargetDto target, CompHelperLaneTargetDto defaultTarget)
    {
        return new CompHelperLaneTargetDto(
            Key: defaultTarget.Key,
            Label: NullIfWhiteSpace(target.Label) ?? defaultTarget.Label,
            Floor: ClampTargetValue(target.Floor, defaultTarget.Floor),
            Target: ClampTargetValue(target.Target, defaultTarget.Target),
            Weight: ClampWeight(target.Weight, defaultTarget.Weight));
    }

    private static CompHelperPackageTargetDto NormalizePackageTarget(CompHelperPackageTargetDto target, CompHelperPackageTargetDto defaultTarget)
    {
        return new CompHelperPackageTargetDto(
            Key: defaultTarget.Key,
            Label: NullIfWhiteSpace(target.Label) ?? defaultTarget.Label,
            Floor: ClampTargetValue(target.Floor, defaultTarget.Floor),
            Target: ClampTargetValue(target.Target, defaultTarget.Target),
            Weight: ClampWeight(target.Weight, defaultTarget.Weight),
            Mandatory: target.Mandatory,
            AllowOvercap: target.AllowOvercap);
    }

    private static double ClampTargetValue(double value, double fallback)
    {
        return double.IsFinite(value) ? Math.Round(Math.Clamp(value, 0, 500), 1) : fallback;
    }

    private static double ClampWeight(double value, double fallback)
    {
        return double.IsFinite(value) ? Math.Round(Math.Clamp(value, 0.05, 10), 2) : fallback;
    }

    private void TryWriteConfig(CompHelperConfigDto config)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, SerializerOptions));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
