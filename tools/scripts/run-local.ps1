[CmdletBinding()]
param(
    [string]$Url = "http://127.0.0.1:5078",
    [switch]$NoBuild,
    [string]$LogPath
)

. (Join-Path $PSScriptRoot "Common.ps1")

$repoRoot = Get-WvWAnalystRoot
$projectPath = Get-WvWAnalystApiProjectPath
$nuGetConfigPath = Get-WvWAnalystNuGetConfigPath
$apiExecutablePath = Get-WvWAnalystApiExecutablePath
$dotnetPath = Get-WvWAnalystDotnetPath

Set-WvWAnalystDotnetEnvironment

$transcriptStarted = $false

try {
    if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
        $logDirectory = Split-Path -Parent $LogPath
        if (-not [string]::IsNullOrWhiteSpace($logDirectory)) {
            New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
        }
        Start-Transcript -Path $LogPath -Append | Out-Null
        $transcriptStarted = $true
    }

    Set-Location (Split-Path -Parent $projectPath)

    if (-not $NoBuild.IsPresent) {
        & $dotnetPath build $projectPath --configfile $nuGetConfigPath -p:RestoreIgnoreFailedSources=true
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }

    if (-not (Test-Path -LiteralPath $apiExecutablePath)) {
        throw "Unable to locate $apiExecutablePath. Build the project first or omit -NoBuild."
    }

    & $apiExecutablePath --urls $Url
    exit $LASTEXITCODE
}
finally {
    if ($transcriptStarted) {
        Stop-Transcript | Out-Null
    }
}
