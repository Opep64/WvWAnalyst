# Fight catalog regression checks

Run from the WvWAnalyst repository root with .NET 10:

```powershell
dotnet run --project tools/tests/FightCatalogRegressionTests
```

This dependency-free regression runner creates isolated fixtures under the system
temporary directory and removes them when finished. It never uses the configured
application fight store or launches Elite Insights.

The checks cover replacement priority, hash/fingerprint case rules, successful
hash filtering, warm lookups without catalog rereads, identity changes on
overwrite, deletion/reset, partial cancellation, legacy fingerprint hydration,
failed writes, explicit cache invalidation, and concurrent publication. On
Windows, an unrelated manifest is locked to make unwanted catalog rereads fail.
