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

Write-Host 'summarize_tppa_upas_response tests passed'
