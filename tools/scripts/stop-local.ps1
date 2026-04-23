[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot "Common.ps1")

$process = Get-WvWAnalystTrackedProcess
if ($null -eq $process) {
    Remove-WvWAnalystTrackingFiles
    Write-Output "WvWAnalyst is not running."
    return
}

$descendantIds = Get-WvWAnalystDescendantProcessIds -RootProcessId $process.Id

foreach ($childId in $descendantIds | Sort-Object -Descending) {
    try {
        Stop-Process -Id $childId -Force -ErrorAction Stop
    }
    catch {
    }
}

try {
    Stop-Process -Id $process.Id -Force -ErrorAction Stop
}
catch {
}

Remove-WvWAnalystTrackingFiles

Write-Output "WvWAnalyst stopped."
Write-Output "PID: $($process.Id)"
