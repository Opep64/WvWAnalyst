# WvWAnalyst

`WvWAnalyst` is a local-first prototype for organized WvW fight analysis.

The prototype lives beside the existing parser and combiner. The current goal is to prove a better analysis workflow built around:

- batch directory ingestion
- one-fight dossiers
- fight execution scorecards
- commander and player trend analysis
- storage-aware local log management

## Prototype shape

- `apps/api`
  Local `ASP.NET Core` app that serves both the UI shell and JSON endpoints
- `packages/contracts`
  Shared DTOs and scorecard payload types
- `packages/analytics`
  Cross-fight analysis definitions and scorecard blueprints
- `packages/parser-bridge`
  Local workspace detection and future parser orchestration hooks
- `storage`
  Local database, per-fight artifacts, and cache folders

## Current status

The current prototype now includes a first-pass parser-side `analysis.json` export contract for detailed WvW fights and a local analyst shell that is ready to consume it. It currently supports:

- local app shell
- parser and combiner workspace discovery
- directory-driven parser invocation against the existing EI CLI
- `new-only` and `rebuild-all` batch modes
- source-log hashing so already-successful files can be skipped in `new-only`
- per-fight manifest files under `storage/fights/<fight-id>/`
- manifest-backed fight indexing from `analysis.json` when available, with EI JSON fallback for older stored fights
- temporary `analysis.json` ingest with a compact persisted fight summary
- recent parses ordered by parse time
- a fight browser sorted and filtered by fight metadata
- single-fight dossier data via `/api/fights/<fight-id>`
- compact table views for recent parses and the fight browser
- a dark theme UI shell
- direct links to retained parser artifacts like parser console logs, with older HTML/JSON links still available on legacy stored fights
- a canonical v1 fight-level team scorecard blueprint

What it still does not do yet:

- verify the new parser export end to end in this sandboxed environment
- ingest many fights into trend views
- manage long-term retention rules beyond folder conventions

## Local run

Use a repo-local `DOTNET_CLI_HOME` and `NuGet.Config` so the prototype stays self-contained inside this workspace:

```powershell
$env:DOTNET_CLI_HOME = 'D:\GW2\EICode\.dotnet'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'

dotnet restore D:\GW2\EICode\WvWAnalyst\WvWAnalyst.slnx --configfile D:\GW2\EICode\WvWAnalyst\NuGet.Config
dotnet run --project D:\GW2\EICode\WvWAnalyst\apps\api\WvWAnalyst.Api\WvWAnalyst.Api.csproj --no-restore
```

The app serves the local dashboard shell and JSON endpoints from the same process.

## Helper scripts

The prototype also includes local run helpers under `tools/scripts`:

```powershell
PowerShell -ExecutionPolicy Bypass -File D:\GW2\EICode\WvWAnalyst\tools\scripts\start-local.ps1
PowerShell -ExecutionPolicy Bypass -File D:\GW2\EICode\WvWAnalyst\tools\scripts\status-local.ps1
PowerShell -ExecutionPolicy Bypass -File D:\GW2\EICode\WvWAnalyst\tools\scripts\stop-local.ps1
```

Useful options:

- `start-local.ps1 -NoBuild`
- `start-local.ps1 -Url http://127.0.0.1:5078`

The start script writes a PID file and process metadata to `storage/cache/`, and the run output is appended to `storage/cache/wvw-analyst.log`.

## Batch flow

1. Start the app locally.
2. Open the dashboard in a browser.
3. Enter a local log directory that contains `.evtc`, `.zevtc`, or `.zip` files.
4. Choose `Process only new files` to skip already-successful hashes, or `Process all and rebuild catalog` to clear local stored fights and start over.
5. The prototype runs the EI CLI per file with `analysis.json` output enabled and legacy HTML/JSON outputs disabled for new imports.
6. `analysis.json` is consumed into a compact manifest-backed fight summary and then deleted.
7. `Recent Parses` shows stored parser results by parse time, and `Fight Browser` shows stored fights by fight metadata.
8. Older fights that were imported before this shift can still expose retained HTML/JSON links until you rebuild the catalog.
