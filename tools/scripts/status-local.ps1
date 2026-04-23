[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot "Common.ps1")

$process = Get-WvWAnalystTrackedProcess
$metadataPath = Get-WvWAnalystMetadataPath

if ($null -eq $process) {
    Remove-WvWAnalystTrackingFiles
    Write-Output "WvWAnalyst is not running."
    Write-Output "Log: $(Get-WvWAnalystLogPath)"
    return
}

$metadata = $null
if (Test-Path -LiteralPath $metadataPath) {
    $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
}

Write-Output "WvWAnalyst is running."
Write-Output "PID: $($process.Id)"

if ($null -ne $metadata) {
    Write-Output "URL: $($metadata.url)"
    Write-Output "Started (UTC): $($metadata.startedAtUtc)"
    Write-Output "Log: $($metadata.logPath)"
    if ($metadata.PSObject.Properties.Name -contains "errorLogPath") {
        Write-Output "Error Log: $($metadata.errorLogPath)"
    }
}
else {
    Write-Output "Log: $(Get-WvWAnalystLogPath)"
    Write-Output "Error Log: $(Get-WvWAnalystErrorLogPath)"
}
