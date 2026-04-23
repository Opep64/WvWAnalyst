function Get-WvWAnalystRoot {
    return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
}

function Get-WvWAnalystApiProjectPath {
    return Join-Path (Get-WvWAnalystRoot) "apps\api\WvWAnalyst.Api\WvWAnalyst.Api.csproj"
}

function Get-WvWAnalystNuGetConfigPath {
    return Join-Path (Get-WvWAnalystRoot) "NuGet.Config"
}

function Get-WvWAnalystApiExecutablePath {
    return Join-Path (Get-WvWAnalystRoot) "apps\api\WvWAnalyst.Api\bin\Debug\net10.0\WvWAnalyst.Api.exe"
}

function Get-WvWAnalystApiAssemblyPath {
    return Join-Path (Get-WvWAnalystRoot) "apps\api\WvWAnalyst.Api\bin\Debug\net10.0\WvWAnalyst.Api.dll"
}

function Get-WvWAnalystCachePath {
    return Join-Path (Get-WvWAnalystRoot) "storage\cache"
}

function Get-WvWAnalystLogPath {
    return Join-Path (Get-WvWAnalystCachePath) "wvw-analyst.log"
}

function Get-WvWAnalystErrorLogPath {
    return Join-Path (Get-WvWAnalystCachePath) "wvw-analyst.error.log"
}

function Get-WvWAnalystPidPath {
    return Join-Path (Get-WvWAnalystCachePath) "wvw-analyst.pid"
}

function Get-WvWAnalystMetadataPath {
    return Join-Path (Get-WvWAnalystCachePath) "wvw-analyst.process.json"
}

function Get-WvWAnalystDotnetPath {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $defaultPath = Join-Path ${env:ProgramFiles} "dotnet\dotnet.exe"
    if (Test-Path -LiteralPath $defaultPath) {
        return $defaultPath
    }

    throw "Unable to locate dotnet.exe."
}

function Set-WvWAnalystDotnetEnvironment {
    $repoRoot = Get-WvWAnalystRoot
    $dotnetRoot = Join-Path (Split-Path -Parent $repoRoot) ".dotnet"

    $env:DOTNET_CLI_HOME = $dotnetRoot
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
    $env:DOTNET_NOLOGO = "1"
    $env:APPDATA = Join-Path $dotnetRoot "appdata"
    $env:NUGET_PACKAGES = Join-Path $dotnetRoot "packages"
    $env:MSBuildEnableWorkloadResolver = "false"

    New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME, $env:APPDATA, $env:NUGET_PACKAGES, (Get-WvWAnalystCachePath) | Out-Null
}

function Get-WvWAnalystTrackedProcess {
    $metadata = Get-WvWAnalystProcessMetadata
    if ($null -ne $metadata -and $metadata.PSObject.Properties.Name -contains "pid") {
        $pidValue = [string]$metadata.pid
    }
    else {
        $pidPath = Get-WvWAnalystPidPath
        if (-not (Test-Path -LiteralPath $pidPath)) {
            return $null
        }

        $pidValue = Get-Content -LiteralPath $pidPath -ErrorAction SilentlyContinue | Select-Object -First 1
    }

    if ([string]::IsNullOrWhiteSpace($pidValue)) {
        return $null
    }

    $pidNumber = 0
    if (-not [int]::TryParse($pidValue, [ref]$pidNumber)) {
        return $null
    }

    $process = Get-Process -Id $pidNumber -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        return $null
    }

    if ($null -eq $metadata) {
        return $process
    }

    if ($metadata.PSObject.Properties.Name -contains "processName") {
        if (-not [string]::Equals($process.ProcessName, [string]$metadata.processName, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $null
        }
    }

    if ($metadata.PSObject.Properties.Name -contains "processStartTimeUtc") {
        try {
            $expectedStart = [DateTime]::Parse([string]$metadata.processStartTimeUtc).ToUniversalTime()
            $actualStart = $process.StartTime.ToUniversalTime()
            if ([Math]::Abs(($actualStart - $expectedStart).TotalSeconds) -gt 1) {
                return $null
            }
        }
        catch {
            return $null
        }
    }

    return $process
}

function Get-WvWAnalystProcessMetadata {
    $metadataPath = Get-WvWAnalystMetadataPath
    if (-not (Test-Path -LiteralPath $metadataPath)) {
        return $null
    }

    try {
        return Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

function Test-WvWAnalystHealth {
    param(
        [string]$Url
    )

    if ([string]::IsNullOrWhiteSpace($Url)) {
        return $false
    }

    try {
        $healthUrl = ($Url.TrimEnd("/")) + "/api/health"
        $response = Invoke-WebRequest -UseBasicParsing -Uri $healthUrl -TimeoutSec 2
        return $response.StatusCode -eq 200
    }
    catch {
        return $false
    }
}

function Remove-WvWAnalystTrackingFiles {
    $paths = @(
        (Get-WvWAnalystPidPath),
        (Get-WvWAnalystMetadataPath)
    )

    foreach ($path in $paths) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
        }
    }
}

function Get-WvWAnalystDescendantProcessIds {
    param(
        [Parameter(Mandatory = $true)]
        [int]$RootProcessId
    )

    try {
        $allProcesses = Get-CimInstance Win32_Process -ErrorAction Stop
    }
    catch {
        return @()
    }
    $childrenByParent = @{}

    foreach ($process in $allProcesses) {
        $parentId = [int]$process.ParentProcessId
        if (-not $childrenByParent.ContainsKey($parentId)) {
            $childrenByParent[$parentId] = New-Object System.Collections.Generic.List[int]
        }
        $childrenByParent[$parentId].Add([int]$process.ProcessId)
    }

    $result = New-Object System.Collections.Generic.List[int]
    $queue = New-Object System.Collections.Generic.Queue[int]
    $queue.Enqueue($RootProcessId)

    while ($queue.Count -gt 0) {
        $current = $queue.Dequeue()
        if (-not $childrenByParent.ContainsKey($current)) {
            continue
        }

        foreach ($childId in $childrenByParent[$current]) {
            if (-not $result.Contains($childId)) {
                $result.Add($childId)
                $queue.Enqueue($childId)
            }
        }
    }

    return $result.ToArray()
}
