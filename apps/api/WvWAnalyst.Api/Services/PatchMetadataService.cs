using System.Globalization;
using System.Text.Json;
using WvWAnalyst.Api.Bridge;
using WvWAnalyst.Contracts;

namespace WvWAnalyst.Api.Services;

public sealed class PatchMetadataService
{
    private const string SchemaVersion = "1.0";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AppPathService _paths;
    private readonly object _lock = new();
    private PatchMetadataDto? _cachedMetadata;

    public PatchMetadataService(AppPathService paths)
    {
        _paths = paths;
    }

    private string MetadataPath => Path.Combine(_paths.StorageRootPath, "patch-metadata.json");

    public PatchMetadataDto GetMetadata()
    {
        lock (_lock)
        {
            if (_cachedMetadata is not null)
            {
                return _cachedMetadata;
            }

            _paths.EnsureStorageDirectories();
            if (!File.Exists(MetadataPath))
            {
                _cachedMetadata = NormalizeMetadata(CreateDefaultMetadata(), updateTimestamp: false);
                TryWriteMetadata(_cachedMetadata);
                return _cachedMetadata;
            }

            try
            {
                var json = File.ReadAllText(MetadataPath);
                var metadata = JsonSerializer.Deserialize<PatchMetadataDto>(json, SerializerOptions);
                _cachedMetadata = NormalizeMetadata(metadata ?? CreateDefaultMetadata(), updateTimestamp: false);
            }
            catch (JsonException)
            {
                _cachedMetadata = CreateDefaultMetadata();
            }
            catch (IOException)
            {
                _cachedMetadata = CreateDefaultMetadata();
            }

            return _cachedMetadata;
        }
    }

    public PatchMetadataDto SaveMetadata(PatchMetadataDto metadata)
    {
        var normalized = NormalizeMetadata(metadata, updateTimestamp: true);
        lock (_lock)
        {
            _paths.EnsureStorageDirectories();
            Directory.CreateDirectory(Path.GetDirectoryName(MetadataPath)!);
            File.WriteAllText(MetadataPath, JsonSerializer.Serialize(normalized, SerializerOptions));
            _cachedMetadata = normalized;
        }

        return normalized;
    }

    public PatchEraDto? GetCurrentPatchEra()
    {
        return GetMetadata().PatchEras.FirstOrDefault(era => era.IsCurrent)
            ?? GetMetadata().PatchEras.LastOrDefault();
    }

    public IReadOnlyList<PatchEraDto> GetLastPatchEras(int count)
    {
        return GetMetadata().PatchEras
            .OrderByDescending(era => ParseStartsOn(era.StartsOn) ?? DateOnly.MinValue)
            .Take(Math.Max(0, count))
            .OrderBy(era => ParseStartsOn(era.StartsOn) ?? DateOnly.MinValue)
            .ToArray();
    }

    public PatchEraDto? FindPatchEra(DateOnly? fightDate)
    {
        if (!fightDate.HasValue)
        {
            return null;
        }

        var date = fightDate.Value;
        return GetMetadata().PatchEras
            .Where(era => EraContainsDate(era, date))
            .OrderByDescending(era => ParseStartsOn(era.StartsOn) ?? DateOnly.MinValue)
            .FirstOrDefault();
    }

    private static PatchMetadataDto CreateDefaultMetadata()
    {
        return new PatchMetadataDto(
            SchemaVersion: SchemaVersion,
            UpdatedAtUtc: DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            PatchEras:
            [
                new PatchEraDto(
                    Id: "pre-2026-04-14",
                    Label: "Pre-April 14 2026",
                    StartsOn: null,
                    EndsOn: "2026-04-13",
                    IsCurrent: false,
                    Notes: "Historical baseline before the April 14 2026 balance patch."),
                new PatchEraDto(
                    Id: "2026-04-14-balance",
                    Label: "April 14 2026 Balance Patch",
                    StartsOn: "2026-04-14",
                    EndsOn: null,
                    IsCurrent: true,
                    Notes: "Current post-patch baseline.")
            ],
            PatchImpacts: []);
    }

    private static PatchMetadataDto NormalizeMetadata(PatchMetadataDto metadata, bool updateTimestamp)
    {
        var eraIndex = 0;
        var eras = (metadata.PatchEras ?? Array.Empty<PatchEraDto>())
            .Where(era => era is not null)
            .Select(era =>
            {
                eraIndex++;
                var label = NullIfWhiteSpace(era.Label) ?? $"Patch Era {eraIndex}";
                var id = NullIfWhiteSpace(era.Id) ?? Slugify(label);
                return era with
                {
                    Id = id,
                    Label = label,
                    StartsOn = NormalizeDate(era.StartsOn),
                    EndsOn = NormalizeDate(era.EndsOn),
                    IsCurrent = false,
                    Notes = NullIfWhiteSpace(era.Notes)
                };
            })
            .OrderBy(era => ParseStartsOn(era.StartsOn) ?? DateOnly.MinValue)
            .ThenBy(era => era.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (eras.Length == 0)
        {
            eras = CreateDefaultMetadata().PatchEras.ToArray();
        }

        var currentEraId = ResolveCurrentEraId(eras);
        eras = eras
            .Select(era => era with { IsCurrent = string.Equals(era.Id, currentEraId, StringComparison.OrdinalIgnoreCase) })
            .ToArray();

        var eraIds = new HashSet<string>(eras.Select(era => era.Id), StringComparer.OrdinalIgnoreCase);
        var impactIndex = 0;
        var impacts = (metadata.PatchImpacts ?? Array.Empty<PatchImpactDto>())
            .Where(impact => impact is not null)
            .Select(impact =>
            {
                impactIndex++;
                var classLabel = NullIfWhiteSpace(impact.ClassLabel) ?? "Unknown";
                var patchEraId = eraIds.Contains(impact.PatchEraId) ? impact.PatchEraId : currentEraId;
                var id = NullIfWhiteSpace(impact.Id)
                    ?? Slugify($"{patchEraId}-{classLabel}-{impact.BuildLabel ?? "build"}-{impactIndex}");
                return impact with
                {
                    Id = id,
                    PatchEraId = patchEraId,
                    ClassLabel = classLabel,
                    BuildLabel = NullIfWhiteSpace(impact.BuildLabel),
                    AdoptionExpectation = NullIfWhiteSpace(impact.AdoptionExpectation),
                    Confidence = NullIfWhiteSpace(impact.Confidence) ?? "Medium",
                    Notes = NullIfWhiteSpace(impact.Notes) ?? string.Empty,
                    LaneImpacts = NormalizeLaneImpacts(impact.LaneImpacts)
                };
            })
            .OrderBy(impact => impact.ClassLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(impact => impact.BuildLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PatchMetadataDto(
            SchemaVersion: NullIfWhiteSpace(metadata.SchemaVersion) ?? SchemaVersion,
            UpdatedAtUtc: updateTimestamp
                ? DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                : NullIfWhiteSpace(metadata.UpdatedAtUtc) ?? DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            PatchEras: eras,
            PatchImpacts: impacts);
    }

    private static IReadOnlyList<PatchImpactLaneDeltaDto> NormalizeLaneImpacts(IReadOnlyList<PatchImpactLaneDeltaDto>? laneImpacts)
    {
        return (laneImpacts ?? Array.Empty<PatchImpactLaneDeltaDto>())
            .Where(lane => lane is not null)
            .Select(lane =>
            {
                var laneLabel = NullIfWhiteSpace(lane.LaneLabel) ?? NullIfWhiteSpace(lane.LaneKey) ?? "Lane";
                var laneKey = NullIfWhiteSpace(lane.LaneKey) ?? Slugify(laneLabel);
                return lane with
                {
                    LaneKey = laneKey,
                    LaneLabel = laneLabel,
                    Impact = Math.Clamp(lane.Impact, -3, 3),
                    Notes = NullIfWhiteSpace(lane.Notes)
                };
            })
            .OrderBy(lane => lane.LaneLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveCurrentEraId(IReadOnlyList<PatchEraDto> eras)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        return eras
            .Where(era => EraContainsDate(era, today))
            .OrderByDescending(era => ParseStartsOn(era.StartsOn) ?? DateOnly.MinValue)
            .Select(era => era.Id)
            .FirstOrDefault()
            ?? eras
                .OrderByDescending(era => ParseStartsOn(era.StartsOn) ?? DateOnly.MinValue)
                .Select(era => era.Id)
                .First();
    }

    private static bool EraContainsDate(PatchEraDto era, DateOnly date)
    {
        var start = ParseStartsOn(era.StartsOn);
        var end = ParseStartsOn(era.EndsOn);
        if (start.HasValue && date < start.Value)
        {
            return false;
        }

        if (end.HasValue && date > end.Value)
        {
            return false;
        }

        return true;
    }

    private static string? NormalizeDate(string? value)
    {
        return ParseStartsOn(value)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static DateOnly? ParseStartsOn(string? value)
    {
        return DateOnly.TryParseExact(
            value?.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
    }

    private void TryWriteMetadata(PatchMetadataDto metadata)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MetadataPath)!);
            File.WriteAllText(MetadataPath, JsonSerializer.Serialize(metadata, SerializerOptions));
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

    private static string Slugify(string value)
    {
        Span<char> buffer = stackalloc char[Math.Min(80, Math.Max(1, value.Length))];
        var index = 0;
        var previousDash = false;

        foreach (var character in value)
        {
            if (index >= buffer.Length)
            {
                break;
            }

            if (char.IsLetterOrDigit(character))
            {
                buffer[index++] = char.ToLowerInvariant(character);
                previousDash = false;
                continue;
            }

            if (previousDash || index == 0)
            {
                continue;
            }

            buffer[index++] = '-';
            previousDash = true;
        }

        while (index > 0 && buffer[index - 1] == '-')
        {
            index--;
        }

        return index == 0 ? "item" : new string(buffer[..index]);
    }
}
