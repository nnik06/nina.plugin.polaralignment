#requires -Version 7.0

$ErrorActionPreference = 'Stop'
$ToolPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'test_next_session_readiness.ps1'
$TestRoot = Join-Path ([IO.Path]::GetTempPath()) ("upas-readiness-" + [guid]::NewGuid().ToString("N"))
$PluginRoot = Join-Path $TestRoot 'plugin'
$PluginDll = Join-Path $PluginRoot 'NINA.Plugins.PolarAlignment.dll'
$FakeAdb = Join-Path $TestRoot 'adb.cmd'

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw "ASSERTION FAILED: $Message"
    }
}

function Invoke-Readiness {
    param([string]$AdbPath)
    $arguments = @(
        '-NoProfile',
        '-File', $ToolPath,
        '-PluginDirectory', $PluginRoot,
        '-ExpectedSha256', (Get-FileHash -Algorithm SHA256 $PluginDll).Hash,
        '-AdbTarget', 'TEST-P20',
        '-AdbExecutable', $AdbPath,
        '-RequireAdb'
    )
    $output = & (Get-Process -Id $PID).Path @arguments 2>&1
    [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Result = (($output -join [Environment]::NewLine) | ConvertFrom-Json)
    }
}

try {
    New-Item -ItemType Directory -Force -Path $PluginRoot | Out-Null
    [IO.File]::WriteAllBytes($PluginDll, [byte[]](1, 2, 3, 4))
    [IO.File]::WriteAllText(
        $FakeAdb,
        "@echo off`r`necho device`r`n",
        [Text.ASCIIEncoding]::new()
    )
    $explicit = Invoke-Readiness -AdbPath $FakeAdb
    Assert-True ($explicit.ExitCode -eq 0) 'an explicit working adb path must pass'
    $adbGate = @($explicit.Result.Gates | Where-Object Name -eq 'P20Adb')
    Assert-True ($adbGate.Count -eq 1 -and $adbGate[0].Passed) 'P20Adb must pass'
    Assert-True (
        $adbGate[0].Detail.Contains((Resolve-Path $FakeAdb).Path)
    ) 'P20Adb detail must record the executable used'

    $missing = Invoke-Readiness -AdbPath (Join-Path $TestRoot 'missing-adb.exe')
    Assert-True ($missing.ExitCode -eq 2) 'a missing explicit adb path must fail closed'
    $missingGate = @($missing.Result.Gates | Where-Object Name -eq 'P20Adb')
    Assert-True (
        $missingGate.Count -eq 1 -and
        -not $missingGate[0].Passed -and
        $missingGate[0].Detail.Contains('does not exist')
    ) 'P20Adb must explain the missing configured executable'
    Write-Host 'All next-session readiness tests passed.'
} finally {
    Remove-Item -LiteralPath $TestRoot -Recurse -Force -ErrorAction SilentlyContinue
}
