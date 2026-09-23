using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using WvWAnalyst.Api.Analysis;
using WvWAnalyst.Api.Bridge;
using WvWAnalyst.Api.Configuration;
using WvWAnalyst.Api.Services;
using WvWAnalyst.Contracts;

await Run("Cold lookup preserves matching priority, newest match, and case rules", async fixture =>
{
    await fixture.Write("older", "shared", "old-fingerprint", day: 1);
    await fixture.Write("newer", "SHARED", "new-fingerprint", day: 2);
    await fixture.Write("failed", "failed-hash", "same-fingerprint", parsed: false, day: 3);
    await fixture.Write("success", "success-hash", "same-fingerprint", day: 2);

    Equal("newer", fixture.Find("sHaReD", "same-fingerprint"));
    Equal("failed", fixture.Find("missing", "same-fingerprint"));
    Equal(null, fixture.Find(null, "SAME-FINGERPRINT"));
    Equal(null, fixture.Find(null, null));
    SetEqual(["shared", "success-hash"], fixture.Catalog.GetKnownSuccessfulSourceHashes());
    Equal("newer", fixture.Catalog.FindReplacementFightsBySourceHash(
        new HashSet<string>(["shared"], StringComparer.OrdinalIgnoreCase))["shared"].FightId);
});

await Run("Warm lookups and new imports never read unrelated manifests", async fixture =>
{
    await fixture.Write("unrelated", "unrelated-hash", "unrelated-fingerprint");
    fixture.Catalog.GetKnownSuccessfulSourceHashes();

    // A whole-catalog reread would throw while this unrelated manifest is locked.
    using var locked = new FileStream(fixture.ManifestPath("unrelated"), FileMode.Open, FileAccess.Read, FileShare.None);
    for (var index = 0; index < 100; index++)
    {
        Equal(null, fixture.Find($"missing-{index}", $"missing-fingerprint-{index}"));
    }

    var version = fixture.Catalog.CacheVersion;
    await fixture.Write("added", "new-hash", "new-fingerprint");
    Equal(true, fixture.Catalog.CacheVersion > version);
    Equal("added", fixture.Find("new-hash", null));
    Equal("added", fixture.Find("different-source-hash", "new-fingerprint"));
    SetEqual(["unrelated-hash", "new-hash"], fixture.Catalog.GetKnownSuccessfulSourceHashes());
});

await Run("Overwrite removes stale keys and preserves older duplicate candidates", async fixture =>
{
    await fixture.Write("older", "shared", "shared-fingerprint", day: 1);
    await fixture.Write("newer", "shared", "shared-fingerprint", day: 2);
    Equal("newer", fixture.Find("shared", null));
    await fixture.Write("newer", "replacement", "replacement-fingerprint", parsed: false, day: 3);
    Equal("older", fixture.Find("shared", null));
    Equal("older", fixture.Find(null, "shared-fingerprint"));
    Equal("newer", fixture.Find("replacement", null));
    SetEqual(["shared"], fixture.Catalog.GetKnownSuccessfulSourceHashes());
    await fixture.Write("newer", "replacement", "replacement-fingerprint", day: 4);
    SetEqual(["shared", "replacement"], fixture.Catalog.GetKnownSuccessfulSourceHashes());
});

await Run("Deletion and reset invalidate the lookup", async fixture =>
{
    await fixture.Write("older", "shared", "fingerprint", day: 1);
    await fixture.Write("newer", "shared", "fingerprint", day: 2);
    Equal("newer", fixture.Find("shared", null));
    Equal(1, fixture.Catalog.DeleteFightDirectories(["newer"], CancellationToken.None));
    Equal("older", fixture.Find("shared", null));
    fixture.Catalog.ResetCatalog();
    Equal(null, fixture.Find("shared", "fingerprint"));
    SetEqual([], fixture.Catalog.GetKnownSuccessfulSourceHashes());
    await fixture.Write("after-reset", "after-reset", "after-reset");
    Equal("after-reset", fixture.Find("after-reset", null));
});

await Run("Partial deletion cancellation cannot leave stale identities", async fixture =>
{
    await fixture.Write("first", "first-hash", "first-fingerprint");
    await fixture.Write("second", "second-hash", "second-fingerprint");
    fixture.Catalog.GetKnownSuccessfulSourceHashes();
    using var cancellation = new CancellationTokenSource();
    IEnumerable<string> Deletions()
    {
        yield return "first";
        cancellation.Cancel();
        yield return "second";
    }
    await Throws<OperationCanceledException>(() => Task.FromResult(
        fixture.Catalog.DeleteFightDirectories(Deletions(), cancellation.Token)));
    Equal(null, fixture.Find("first-hash", "first-fingerprint"));
    Equal("second", fixture.Find("second-hash", null));
    SetEqual(["second-hash"], fixture.Catalog.GetKnownSuccessfulSourceHashes());
});

await Run("Legacy fingerprint hydration and malformed-manifest handling are preserved", async fixture =>
{
    var index = JsonSerializer.Deserialize<FightIndexDto>("""
        {"fightName":"WvW","eliteInsightsVersion":"test","timeStartStandard":"2026-09-01T00:00:00Z",
         "durationMilliseconds":60000,"recordedAccountBy":"Test.1234"}
        """, Fixture.JsonOptions)!;
    var manifest = Fixture.Manifest("legacy", "legacy-hash", null) with
    {
        FightIndex = new FightIndexSnapshot(16, DateTime.UtcNow, index)
    };
    await fixture.Catalog.WriteManifestAsync(manifest, CancellationToken.None);
    Directory.CreateDirectory(Path.GetDirectoryName(fixture.ManifestPath("malformed"))!);
    await File.WriteAllTextAsync(fixture.ManifestPath("malformed"), "{broken");
    var fingerprint = FightCatalogService.BuildFightFingerprint(index);
    Equal("legacy", fixture.Find("different-hash", fingerprint));
    Equal(fingerprint, fixture.Catalog.TryLoadManifestForUpdate("legacy")!.FightFingerprint);
    SetEqual(["legacy-hash"], fixture.Catalog.GetKnownSuccessfulSourceHashes());
    await fixture.Catalog.WriteManifestAsync(manifest with { FightId = "warm-legacy", ImportedAtUtc = manifest.ImportedAtUtc.AddDays(1) }, CancellationToken.None);
    Equal("warm-legacy", fixture.Find("different-hash", fingerprint));
});

await Run("Cancelled or failed writes retain the previous file and identity", async fixture =>
{
    await fixture.Write("retained", "old-hash", "old-fingerprint");
    fixture.Catalog.GetKnownSuccessfulSourceHashes();
    var previousBytes = await File.ReadAllBytesAsync(fixture.ManifestPath("retained"));
    var replacement = Fixture.Manifest("retained", "new-hash", "new-fingerprint");
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    await Throws<OperationCanceledException>(() => fixture.Catalog.WriteManifestAsync(replacement, cancellation.Token));

    // Windows prevents replacing a destination opened without delete sharing.
    if (OperatingSystem.IsWindows())
    {
        using var locked = new FileStream(fixture.ManifestPath("retained"), FileMode.Open, FileAccess.Read, FileShare.None);
        await Throws<Exception>(() => fixture.Catalog.WriteManifestAsync(replacement, CancellationToken.None),
            exception => exception is IOException or UnauthorizedAccessException);
    }

    var retainedBytes = await File.ReadAllBytesAsync(fixture.ManifestPath("retained"));
    Equal(true, previousBytes.SequenceEqual(retainedBytes));
    Equal("retained", fixture.Find("old-hash", null));
    Equal(null, fixture.Find("new-hash", "new-fingerprint"));
    SetEqual(["old-hash"], fixture.Catalog.GetKnownSuccessfulSourceHashes());
    Equal(0, Directory.GetFiles(fixture.Paths.FightsPath, "*.tmp", SearchOption.AllDirectories).Length);
});

await Run("Explicit invalidation reloads externally changed manifests", async fixture =>
{
    await fixture.Write("updated", "before", "before");
    fixture.Catalog.GetKnownSuccessfulSourceHashes();
    await File.WriteAllTextAsync(fixture.ManifestPath("updated"),
        JsonSerializer.Serialize(Fixture.Manifest("updated", "after", "after"), Fixture.JsonOptions));
    fixture.Catalog.InvalidateCatalogCache();
    Equal(null, fixture.Find("before", "before"));
    Equal("updated", fixture.Find("after", null));
});

await Run("Concurrent manifest publication keeps the warm index complete", async fixture =>
{
    fixture.Catalog.GetKnownSuccessfulSourceHashes();
    await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(async () =>
    {
        await fixture.Write($"fight-{index}", $"hash-{index}", $"fingerprint-{index}");
        Equal($"fight-{index}", fixture.Find($"hash-{index}", null));
        fixture.Catalog.GetKnownSuccessfulSourceHashes();
    })));
    SetEqual(Enumerable.Range(0, 32).Select(index => $"hash-{index}"), fixture.Catalog.GetKnownSuccessfulSourceHashes());
});

Console.WriteLine("All 9 catalog regression checks passed.");

static async Task Run(string name, Func<Fixture, Task> test)
{
    using var fixture = new Fixture();
    await test(fixture);
    Console.WriteLine($"PASS: {name}");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}

static void SetEqual(IEnumerable<string> expected, HashSet<string> actual)
{
    if (!actual.SetEquals(expected))
    {
        throw new InvalidOperationException("Successful source hashes differ from the expected set.");
    }
}

static async Task Throws<T>(Func<Task> action, Func<T, bool>? matches = null) where T : Exception
{
    try { await action(); }
    catch (T exception) when (matches is null || matches(exception)) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

sealed class Fixture : IDisposable
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _testParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WvWAnalyst-CatalogTests"));
    private readonly string _root;
    public AppPathService Paths { get; }
    public FightCatalogService Catalog { get; }

    public Fixture()
    {
        _root = Path.Combine(_testParent, Guid.NewGuid().ToString("N"));
        var environment = new TestEnvironment { ContentRootPath = _root };
        Paths = new AppPathService(environment,
            Options.Create(new StorageOptions { RootPath = _root, FightsPath = "fights", CachePath = "cache", DatabasePath = "db/catalog.db" }),
            Options.Create(new WorkspaceOptions { PendingDirectoryPath = null, ArchiveLogDirectoryPath = null }));
        Catalog = new FightCatalogService(Paths, new EliteInsightsFightIndexer(), new PatchMetadataService(Paths), new FightAttributeService());
    }

    public string ManifestPath(string id) => Path.Combine(Paths.FightsPath, id, "manifest.json");
    public string? Find(string? hash, string? fingerprint) => Catalog.TryFindReplacementFight(hash, fingerprint)?.FightId;
    public Task Write(string id, string hash, string fingerprint, bool parsed = true, int day = 1) =>
        Catalog.WriteManifestAsync(Manifest(id, hash, fingerprint, parsed, day), CancellationToken.None);

    public static FightArtifactManifest Manifest(string id, string hash, string? fingerprint, bool parsed = true, int day = 1) =>
        new(id, id + ".zevtc", null, 100, hash, fingerprint, new DateTime(2026, 9, day, 0, 0, 0, DateTimeKind.Utc),
            parsed, "test", 1, "test-parser", "config", null, false, null, null, null, null, null, null, [], null);

    public void Dispose()
    {
        var resolvedRoot = Path.GetFullPath(_root);
        if (!string.Equals(Path.GetDirectoryName(resolvedRoot), _testParent, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to clean up outside the test directory.");
        }
        if (Directory.Exists(resolvedRoot))
        {
            Directory.Delete(resolvedRoot, recursive: true);
        }
    }
}

sealed class TestEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "FightCatalogRegressionTests";
    public string EnvironmentName { get; set; } = "Testing";
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
