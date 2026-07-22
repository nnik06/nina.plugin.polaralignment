Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$toolDir = Split-Path -Parent $PSScriptRoot
$solver = Join-Path $toolDir 'solve_two_field_drift.ps1'
$temp = Join-Path ([IO.Path]::GetTempPath()) ('two-field-drift-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temp)
try {
    $header = 'DriftStable,ComparisonEligible,ComparisonSlopeArcsecPerMin'
    [IO.File]::WriteAllLines((Join-Path $temp 'one.csv'), @($header, 'True,True,6.883438'))
    [IO.File]::WriteAllLines((Join-Path $temp 'two.csv'), @($header, 'True,True,5.67345142424163'))
    $result = & $solver -FirstSummaryPath (Join-Path $temp 'one.csv') -FirstAzimuthDegrees 270.0 `
        -FirstAltitudeDegrees 45.23 -SecondSummaryPath (Join-Path $temp 'two.csv') `
        -SecondAzimuthDegrees 299.93 -SecondAltitudeDegrees 29.90 `
        -LatitudeDegrees 25 -TppaAzimuthErrorArcMinutes -31 -TppaAltitudeErrorArcMinutes -9.66 | ConvertFrom-Json
    if (-not $result.VerificationOnly -or $result.ActuationEligible) { throw 'solver must always remain verification-only' }
    if ([Math]::Abs([double]$result.AzimuthErrorArcMinutes - -24.935) -gt 0.02) { throw 'unexpected azimuth solution' }
    if ([Math]::Abs([double]$result.AltitudeErrorArcMinutes - -14.866) -gt 0.02) { throw 'unexpected altitude solution' }
    if ($result.CrossMethodAgreement -or $result.GeometryQualified) { throw 'the weak, low-altitude field pair must remain unqualified' }
    if (-not $result.TppaSignsAgree) { throw 'expected matching diagnostic signs' }

    [IO.File]::WriteAllLines((Join-Path $temp 'bad.csv'), @($header, 'False,True,5.0'))
    $failedClosed = $false
    try { & $solver -FirstSummaryPath (Join-Path $temp 'bad.csv') -FirstAzimuthDegrees 270 -FirstAltitudeDegrees 45 `
        -SecondSummaryPath (Join-Path $temp 'two.csv') -SecondAzimuthDegrees 300 -SecondAltitudeDegrees 30 -LatitudeDegrees 25 | Out-Null }
    catch { $failedClosed = $true }
    if (-not $failedClosed) { throw 'unstable input must fail closed' }
} finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'All two-field drift solver tests passed.'
