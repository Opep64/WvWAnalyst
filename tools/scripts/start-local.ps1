[CmdletBinding()]
param(
    [string]$Url = "http://127.0.0.1:5078",
    [switch]$NoBuild,
    [int]$WaitSeconds = 20
)

. (Join-Path $PSScriptRoot "Common.ps1")

$existingProcess = Get-WvWAnalystTrackedProcess
if ($null -ne $existingProcess) {
    Write-Output "WvWAnalyst is already running with PID $($existingProcess.Id)."
    return
}

Remove-WvWAnalystTrackingFiles
Set-WvWAnalystDotnetEnvironment

$projectPath = Get-WvWAnalystApiProjectPath
$projectDirectory = Split-Path -Parent $projectPath
$apiAssemblyPath = Get-WvWAnalystApiAssemblyPath
$nuGetConfigPath = Get-WvWAnalystNuGetConfigPath
$dotnetPath = Get-WvWAnalystDotnetPath
$logPath = Get-WvWAnalystLogPath
$errorLogPath = Get-WvWAnalystErrorLogPath
$pidPath = Get-WvWAnalystPidPath
$metadataPath = Get-WvWAnalystMetadataPath

foreach ($path in @($logPath, $errorLogPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

if (-not $NoBuild.IsPresent) {
    & $dotnetPath build $projectPath --configfile $nuGetConfigPath -p:RestoreIgnoreFailedSources=true
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

if (-not (Test-Path -LiteralPath $apiAssemblyPath)) {
    throw "Unable to locate $apiAssemblyPath. Build the project first or omit -NoBuild."
}

# Windows PowerShell in this environment exposes both Path and PATH, which
# causes Start-Process to throw before the app even launches.
[System.Environment]::SetEnvironmentVariable("PATH", $null, "Process")

$process = Start-Process -FilePath $dotnetPath `
    -ArgumentList @($apiAssemblyPath, "--urls", $Url) `
    -WorkingDirectory $projectDirectory `
    -WindowStyle Hidden `
    -RedirectStandardOutput $logPath `
    -RedirectStandardError $errorLogPath `
    -PassThru

Set-Content -LiteralPath $pidPath -Value $process.Id

$metadata = [pscustomobject]@{
    pid = $process.Id
    url = $Url
    logPath = $logPath
    errorLogPath = $errorLogPath
    startedAtUtc = [DateTime]::UtcNow.ToString("o")
}
$metadata | ConvertTo-Json | Set-Content -LiteralPath $metadataPath

$healthUrl = ($Url.TrimEnd("/")) + "/api/health"
$healthy = $false

for ($attempt = 0; $attempt -lt $WaitSeconds; $attempt++) {
    Start-Sleep -Seconds 1

    $currentProcess = Get-WvWAnalystTrackedProcess
    if ($null -eq $currentProcess) {
        break
    }

    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $healthUrl -TimeoutSec 2
        if ($response.StatusCode -eq 200) {
            $healthy = $true
            break
        }
    }
    catch {
    }
}

if (-not $healthy) {
    $currentProcess = Get-WvWAnalystTrackedProcess
    if ($null -ne $currentProcess) {
        Write-Warning "WvWAnalyst started with PID $($currentProcess.Id), but the health endpoint did not respond within $WaitSeconds seconds."
    }
    else {
        Write-Warning "WvWAnalyst exited before it reported healthy."
    }

    if (Test-Path -LiteralPath $logPath) {
        Write-Output ""
        Write-Output "Recent log output:"
        Get-Content -LiteralPath $logPath -Tail 20
    }

    if (Test-Path -LiteralPath $errorLogPath) {
        Write-Output ""
        Write-Output "Recent error output:"
        Get-Content -LiteralPath $errorLogPath -Tail 20
    }

    return
}

Write-Output "WvWAnalyst started."
Write-Output "PID: $($process.Id)"
Write-Output "URL: $Url"
Write-Output "Log: $logPath"
Write-Output "Error Log: $errorLogPath"
