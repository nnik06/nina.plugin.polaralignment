#requires -Version 7.5
param(
    [Parameter(Mandatory = $true)]
    [string[]]$LogPath,
    [string]$OutputPath = ''
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$analyzer = Join-Path $PSScriptRoot 'analyze_tppa_fast_alignment_runs.ps1'
if (-not (Test-Path -LiteralPath $analyzer -PathType Leaf)) {
    throw "Fast-alignment analyzer is missing: $analyzer"
}

& $analyzer `
    -LogPath $LogPath `
    -OutputPath $OutputPath `
    -MinimumEligibleRuns 5 `
    -RequiredPassingRuns 4 `
    -RequiredPassRate 0.8 `
    -MinimumNights 1 `
    -MaximumRuntimeSeconds 300.0 `
    -MinimumSettleSeconds 5.0 `
    -MaximumToleranceMinutes 3.0 `
    -MinimumInitialTotalMinutes 0.0 `
    -MaximumInitialTotalMinutes 424.264069 `
    -MinimumMoveCount 0 `
    -MaximumMoveCount 12
