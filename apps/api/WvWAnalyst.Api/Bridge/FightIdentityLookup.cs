namespace WvWAnalyst.Api.Bridge;

// Keep only identity metadata: retaining full manifests here would duplicate the
// much larger fight/player data already held by the analysis catalog.
internal sealed class FightIdentityLookup
{
    private readonly Dictionary<string, Entry> _byFightId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Entry>> _byHash = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Entry>> _byFingerprint = new(StringComparer.Ordinal);
    private long _nextOrder;

    public void Upsert(FightArtifactManifest manifest)
    {
        if (_byFightId.Remove(manifest.FightId, out var previous))
        {
            Remove(_byHash, previous.SourceHash, previous);
            Remove(_byFingerprint, previous.Fingerprint, previous);
        }

        var entry = new Entry(
            manifest.FightId,
            manifest.SourceFileSha256,
            string.IsNullOrWhiteSpace(manifest.FightFingerprint)
                ? FightCatalogService.BuildFightFingerprint(manifest.FightIndex?.Data)
                : manifest.FightFingerprint,
            manifest.ImportedAtUtc,
            manifest.Parsed,
            previous?.Order ?? _nextOrder++);
        _byFightId[entry.FightId] = entry;
        Add(_byHash, entry.SourceHash, entry);
        Add(_byFingerprint, entry.Fingerprint, entry);
    }

    public string? FindReplacement(string? sourceHash, string? fingerprint)
    {
        // A hash match takes precedence even when a fingerprint match is newer.
        return FindNewest(_byHash, sourceHash) ?? FindNewest(_byFingerprint, fingerprint);
    }

    public HashSet<string> GetSuccessfulSourceHashes()
    {
        return _byFightId.Values
            .Where(entry => entry.Parsed && !string.IsNullOrWhiteSpace(entry.SourceHash))
            .Select(entry => entry.SourceHash!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string? FindNewest(Dictionary<string, List<Entry>> lookup, string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || !lookup.TryGetValue(key, out var entries))
        {
            return null;
        }

        return entries
            .OrderByDescending(entry => entry.ImportedAtUtc)
            .ThenBy(entry => entry.Order)
            .First().FightId;
    }

    private static void Add(Dictionary<string, List<Entry>> lookup, string? key, Entry entry)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!lookup.TryGetValue(key, out var entries))
        {
            entries = [];
            lookup[key] = entries;
        }
        entries.Add(entry);
    }

    private static void Remove(Dictionary<string, List<Entry>> lookup, string? key, Entry entry)
    {
        if (string.IsNullOrWhiteSpace(key) || !lookup.TryGetValue(key, out var entries))
        {
            return;
        }

        entries.Remove(entry);
        if (entries.Count == 0)
        {
            lookup.Remove(key);
        }
    }

    private sealed record Entry(
        string FightId,
        string? SourceHash,
        string? Fingerprint,
        DateTime ImportedAtUtc,
        bool Parsed,
        long Order);
}
