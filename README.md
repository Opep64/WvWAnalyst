# WvWAnalyst

`WvWAnalyst` is a local-first Guild Wars 2 WvW analysis app. It batch-processes ArcDPS logs through a WvWAnalyst-enabled Elite Insights build, stores compact fight records locally, and provides fight browsing, dossiers, filtered analysis, Top 5 leaderboards, comp helper views, and local activity auditing.

The app does **not** require the exact root path `D:\GW2\EICode`. Any root path can work if the repositories keep this sibling layout:

```text
<root>\
  GW2-Elite-Insights-Parser\
  WvWAnalyst\
```

For the commands below, set `$Root` to your workspace root:

```powershell
$Root = 'D:\GW2\EICode'   # Change this to your root if different.
$WvWAnalystRoot = Join-Path $Root 'WvWAnalyst'
$EiRoot = Join-Path $Root 'GW2-Elite-Insights-Parser'
```

## Files You May Need To Edit

Main app configuration:

```text
<root>\WvWAnalyst\apps\api\WvWAnalyst.Api\appsettings.json
```

Edit this for storage paths, EI paths, pending/archive log directories, and authentication on/off.

NuGet package source configuration:

```text
<root>\WvWAnalyst\NuGet.Config
```

You normally do not need to edit this. The helper scripts set the package cache dynamically under `<root>\.dotnet\packages`. Edit this only if you need a different NuGet package source.

Patch metadata:

```text
<root>\WvWAnalyst\storage\patch-metadata.json
```

Usually edit this through the Manage tab. It stores patch eras and perceived class/build impact notes.

Comp Helper saved configuration:

```text
<root>\WvWAnalyst\storage\comp-helper-config.json
```

Usually edit this through the Analysis Comp Helper UI.

Authentication users:

```text
<root>\WvWAnalyst\storage\auth-users.json
```

Do not hand-edit this unless you are recovering from a broken file. Use the user CLI commands below. This file stores password hashes, not plaintext passwords.

Activity audit logs:

```text
<root>\WvWAnalyst\storage\audit\audit-yyyy-MM.jsonl
```

These are viewable from the Manage tab Activity Log. They can contain usernames and local activity details.

## Required Dependencies

Install these before building or running:

- Windows with PowerShell 5.1 or newer.
- Git.
- A modern browser such as Chrome, Edge, or Firefox.
- .NET SDK 10.0 for `WvWAnalyst` (`net10.0` projects).
- .NET SDK 8.0, or compatible .NET 8 targeting support, for the Elite Insights CLI (`net8.0`).
- A WvWAnalyst-enabled Elite Insights checkout from the fork used by this workspace.

Clone the expected repositories:

```powershell
cd $Root
git clone https://github.com/Opep64/GW2-Elite-Insights-Parser.git
git clone https://github.com/Opep64/WvWAnalyst.git
```

The EI fork should have `GW2EIBuilders\WvWAnalystBuilder.cs` and the `SaveOutAnalystJSON` setting. A stock EI build without that analyst export will not provide the data WvWAnalyst imports.

## Build Elite Insights

WvWAnalyst runs the EI CLI at this default relative path:

```text
<root>\GW2-Elite-Insights-Parser\GW2EI.bin\Debug\CLI\GuildWars2EliteInsights-CLI.exe
```

Build the EI CLI:

```powershell
dotnet build (Join-Path $EiRoot 'GW2EIParserCLI\GW2EIParserCLI.csproj') -c Debug
```

If you build EI in another configuration or location, update `Workspace:ParserCliPath` in:

```text
<root>\WvWAnalyst\apps\api\WvWAnalyst.Api\appsettings.json
```

## Build WvWAnalyst

The helper scripts build the API automatically, but you can build it directly:

```powershell
$env:DOTNET_CLI_HOME = Join-Path $Root '.dotnet'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:APPDATA = Join-Path $Root '.dotnet\appdata'
$env:NUGET_PACKAGES = Join-Path $Root '.dotnet\packages'
$env:MSBuildEnableWorkloadResolver = 'false'

dotnet build (Join-Path $WvWAnalystRoot 'apps\api\WvWAnalyst.Api\WvWAnalyst.Api.csproj') `
  --configfile (Join-Path $WvWAnalystRoot 'NuGet.Config') `
  -p:RestoreIgnoreFailedSources=true
```

The default debug build output is:

```text
<root>\WvWAnalyst\apps\api\WvWAnalyst.Api\bin\Debug\net10.0\WvWAnalyst.Api.dll
```

## Start, Check, And Stop WvWAnalyst

Start WvWAnalyst in the background:

```powershell
& powershell -ExecutionPolicy Bypass -File (Join-Path $WvWAnalystRoot 'tools\scripts\start-local.ps1')
```

Start on a specific URL:

```powershell
& powershell -ExecutionPolicy Bypass -File (Join-Path $WvWAnalystRoot 'tools\scripts\start-local.ps1') -Url http://127.0.0.1:5078
```

Start without rebuilding first:

```powershell
& powershell -ExecutionPolicy Bypass -File (Join-Path $WvWAnalystRoot 'tools\scripts\start-local.ps1') -NoBuild
```

Check status:

```powershell
& powershell -ExecutionPolicy Bypass -File (Join-Path $WvWAnalystRoot 'tools\scripts\status-local.ps1')
```

Stop the tracked WvWAnalyst process and child parser processes:

```powershell
& powershell -ExecutionPolicy Bypass -File (Join-Path $WvWAnalystRoot 'tools\scripts\stop-local.ps1')
```

Run in the foreground instead of as a tracked background process:

```powershell
& powershell -ExecutionPolicy Bypass -File (Join-Path $WvWAnalystRoot 'tools\scripts\run-local.ps1')
```

The start script writes process metadata and logs under:

```text
<root>\WvWAnalyst\storage\cache\
```

Useful log files:

```text
<root>\WvWAnalyst\storage\cache\wvw-analyst.log
<root>\WvWAnalyst\storage\cache\wvw-analyst.error.log
```

## Configuration

Main configuration file:

```text
<root>\WvWAnalyst\apps\api\WvWAnalyst.Api\appsettings.json
```

Default storage configuration:

```json
"Storage": {
  "RootPath": "..\\..\\..\\storage",
  "DatabasePath": "..\\..\\..\\storage\\db\\wvw-analyst.db",
  "FightsPath": "..\\..\\..\\storage\\fights",
  "CachePath": "..\\..\\..\\storage\\cache"
}
```

Default workspace configuration:

```json
"Workspace": {
  "ParserPath": "..\\..\\..\\..\\GW2-Elite-Insights-Parser",
  "ParserCliPath": "..\\..\\..\\..\\GW2-Elite-Insights-Parser\\GW2EI.bin\\Debug\\CLI\\GuildWars2EliteInsights-CLI.exe",
  "PendingDirectoryPath": "..\\..\\..\\storage\\incoming",
  "ArchiveLogDirectoryPath": "..\\..\\..\\storage\\logs"
}
```

Relative paths in `appsettings.json` are resolved from:

```text
<root>\WvWAnalyst\apps\api\WvWAnalyst.Api
```

So `..\..\..\storage` resolves to:

```text
<root>\WvWAnalyst\storage
```

The default sibling EI path works for any root as long as `GW2-Elite-Insights-Parser` sits beside `WvWAnalyst`.

## Import Workflow

1. Start WvWAnalyst.
2. Open the URL printed by `start-local.ps1`, usually `http://127.0.0.1:5078`.
3. Use the `Manage` tab.
4. Drop or select `.evtc`, `.zevtc`, or `.zip` log files. They are saved into `storage\incoming`.
5. Run `Process only new files` to process pending uploads and skip already-successful archived hashes.
6. Run `Process all and rebuild catalog` to rebuild from `storage\logs`.

During import, WvWAnalyst creates a temporary EI config that enables the settings it needs:

```text
DetailledWvW=true
ParseCombatReplay=true
SaveOutHTML=true
SaveOutJSON=false
SaveOutAnalystJSON=true
```

Successful imports retain compact fight records and the EI HTML report. The temporary `*.analysis.json` payload is ingested and then removed.

Fights are excluded from the retained catalog if they are not detailed WvW fights, are under `1m 05s`, have fewer than `10` squad participants, or have fewer than `10` enemy participants.

## Authentication

Authentication is controlled in:

```text
<root>\WvWAnalyst\apps\api\WvWAnalyst.Api\appsettings.json
```

Relevant section:

```json
"Authentication": {
  "Enabled": true,
  "UsersPath": "..\\..\\..\\storage\\auth-users.json",
  "CookieName": "WvWAnalyst.Auth"
}
```

Turn authentication on:

```json
"Enabled": true
```

Turn authentication off:

```json
"Enabled": false
```

Restart WvWAnalyst after changing `appsettings.json`.

You can also override the setting for the current PowerShell session:

```powershell
$env:Authentication__Enabled = 'true'
& powershell -ExecutionPolicy Bypass -File (Join-Path $WvWAnalystRoot 'tools\scripts\start-local.ps1')
```

Use `'false'` instead of `'true'` to disable auth for that session.

For a direct one-off `dotnet` run, pass:

```powershell
--Authentication:Enabled=true
```

or:

```powershell
--Authentication:Enabled=false
```

When auth is enabled, the UI shows a login panel. After login, the top-right header shows the signed-in user, a `Change password` button, and a `Logout` button.

## Manage Users

Build WvWAnalyst first, then use the built API assembly as a small user-management CLI.

Default debug build:

```powershell
$ApiDll = Join-Path $WvWAnalystRoot 'apps\api\WvWAnalyst.Api\bin\Debug\net10.0\WvWAnalyst.Api.dll'
```

List users:

```powershell
dotnet $ApiDll users list
```

Add a user:

```powershell
dotnet $ApiDll users add creig
```

Reset a user's password:

```powershell
dotnet $ApiDll users reset-password creig
```

Remove a user:

```powershell
dotnet $ApiDll users remove creig
```

The CLI prompts for passwords and stores only password hashes in:

```text
<root>\WvWAnalyst\storage\auth-users.json
```

If you built Release instead of Debug, use:

```text
<root>\WvWAnalyst\apps\api\WvWAnalyst.Api\bin\Release\net10.0\WvWAnalyst.Api.dll
```

## Activity Log

WvWAnalyst writes audit events for logins, logouts, password changes, uploads, parse starts, blocked parse attempts, resets, and delete actions.

Files are stored as JSON Lines:

```text
<root>\WvWAnalyst\storage\audit\audit-yyyy-MM.jsonl
```

The `Manage` tab includes an `Activity Log` section with a refresh button for recent activity.

## UI Overview

- `Manage`
  - Upload logs.
  - Start batch parsing.
  - Reset stored state.
  - Delete fights by commander or date range.
  - Edit patch metadata.
  - View recent activity.
- `Fight Browser`
  - Browse imported fights.
  - Filter by commander, date, outcome, patch scope, attributes, and squad/enemy classes.
  - Open fight dossiers and retained EI HTML.
- `Analysis`
  - Overview trends.
  - Player, class, lane, boon, enemy, and comp helper views.
  - `Top 5` cards with export to standalone HTML.

## Storage Notes

Durable storage:

```text
<root>\WvWAnalyst\storage\fights
<root>\WvWAnalyst\storage\logs
<root>\WvWAnalyst\storage\patch-metadata.json
<root>\WvWAnalyst\storage\comp-helper-config.json
<root>\WvWAnalyst\storage\auth-users.json
<root>\WvWAnalyst\storage\audit
```

Transient storage:

```text
<root>\WvWAnalyst\storage\cache
<root>\WvWAnalyst\storage\incoming
```

Stop WvWAnalyst before copying `storage` to another machine. If a copied install behaves oddly, clear `storage\cache` first.

`storage\auth-users.json` and `storage\audit` can contain sensitive local information. Do not publish them.

## Troubleshooting

Check whether WvWAnalyst is running:

```powershell
& powershell -ExecutionPolicy Bypass -File (Join-Path $WvWAnalystRoot 'tools\scripts\status-local.ps1')
```

Read recent logs:

```powershell
Get-Content (Join-Path $WvWAnalystRoot 'storage\cache\wvw-analyst.log') -Tail 80
Get-Content (Join-Path $WvWAnalystRoot 'storage\cache\wvw-analyst.error.log') -Tail 80
```

If the app cannot find EI, rebuild EI and verify:

```text
<root>\GW2-Elite-Insights-Parser\GW2EI.bin\Debug\CLI\GuildWars2EliteInsights-CLI.exe
```

If imported data looks stale after parser payload changes, run `Process all and rebuild catalog`.

If auth is enabled and you cannot log in, use:

```powershell
dotnet $ApiDll users reset-password creig
```
