#requires -Version 7.0
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$tool = Join-Path $repositoryRoot 'tools\summarize_tppa_upas_response.ps1'
$fixture = Join-Path $repositoryRoot 'tools\tests\data\tppa_upas_response_samples.log'

$summary = @(& $tool -LogPath $fixture -MinimumSamplesPerAxis 1) | ConvertFrom-Json
if (-not $summary.TwoByTwoResponseComplete) {
    throw 'Expected isolated X and Y fixture samples to form a complete 2x2 response.'
}

if ($summary.XColumn.SampleCount -ne 2 -or
    $summary.YColumn.SampleCount -ne 1 -or
    $summary.XColumn.MedianAzimuthArcMinutesPerUnit -ne 2 -or
    $summary.XColumn.MedianAltitudeArcMinutesPerUnit -ne 0.2 -or
    $summary.YColumn.MedianAzimuthArcMinutesPerUnit -ne 0.5 -or
    $summary.YColumn.MedianAltitudeArcMinutesPerUnit -ne 2) {
    throw 'Unexpected response-summary result for the fixture.'
}

$qualifiedLog = [IO.Path]::GetTempFileName()
$inconsistentLog = [IO.Path]::GetTempFileName()
try {
    $qualifiedLines = @(
        "TPPA_UPAS_FRESH_RESPONSE_SAMPLE axis=X, X=10, Y=0, deltaAz=20', deltaAlt=2'",
        "TPPA_UPAS_FRESH_RESPONSE_SAMPLE axis=X, X=10, Y=0, deltaAz=21', deltaAlt=2.1'",
        "TPPA_UPAS_FRESH_RESPONSE_SAMPLE axis=X, X=-10, Y=0, deltaAz=-19.5', deltaAlt=-1.95'",
        "TPPA_UPAS_FRESH_RESPONSE_SAMPLE axis=Y, X=0, Y=8, deltaAz=4', deltaAlt=16'",
        "TPPA_UPAS_FRESH_RESPONSE_SAMPLE axis=Y, X=0, Y=8, deltaAz=4.16', deltaAlt=15.84'",
        "TPPA_UPAS_FRESH_RESPONSE_SAMPLE axis=Y, X=0, Y=-8, deltaAz=-3.92', deltaAlt=-16.08'",
        'TPPA_UPAS_PROBE_REJECTED axis=X, attemptedUnits=4, rejectedAttempts=1/3, nextProbeUnits=8, responseFloorArcMinutes=0.5'
    )
    [IO.File]::WriteAllLines($qualifiedLog, $qualifiedLines)
    $qualified = @(& $tool -LogPath $qualifiedLog -MinimumSamplesPerAxis 3) |
        ConvertFrom-Json
    if (-not $qualified.ClampLimitedRecoveryQualified -or
        [Math]::Abs($qualified.MeasuredRelativeResponseUncertainty - 0.05) -gt 1e-9 -or
        $qualified.MatrixConditionNumber -gt 5 -or
        $qualified.MatrixDegreesPerUnit.Azimuth.X -ne (2.0 / 60.0) -or
        $qualified.XColumn.LargestRejectedAttemptUnits -ne 4 -or
        $qualified.XColumn.RejectedProbeCount -ne 1) {
        throw 'A stable three-sample-per-axis response must produce a qualified conservative uncertainty verdict.'
    }

    $inconsistentLines = @($qualifiedLines)
    $inconsistentLines[2] =
        "TPPA_UPAS_FRESH_RESPONSE_SAMPLE axis=X, X=10, Y=0, deltaAz=-20', deltaAlt=-2'"
    [IO.File]::WriteAllLines($inconsistentLog, $inconsistentLines)
    $inconsistent = @(& $tool -LogPath $inconsistentLog -MinimumSamplesPerAxis 3) |
        ConvertFrom-Json
    if ($inconsistent.ClampLimitedRecoveryQualified -or
        $inconsistent.XColumn.DirectionConsistent) {
        throw 'A direction-inconsistent response sample must deny clamp-limited recovery.'
    }
} finally {
    Remove-Item -LiteralPath $qualifiedLog -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $inconsistentLog -Force -ErrorAction SilentlyContinue
}

Write-Host 'summarize_tppa_upas_response tests passed'
