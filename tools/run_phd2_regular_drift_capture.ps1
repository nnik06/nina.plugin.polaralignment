param(
    [ValidateRange(12, 60)] [int]$DriftMinutes = 12,
    [ValidateRange(500, 10000)] [int]$ExposureMilliseconds = 1500,
    [ValidateRange(270.0, 300.0)] [double]$MinimumAzimuthDegrees = 270.0,
    [ValidateRange(270.0, 300.0)] [double]$MaximumAzimuthDegrees = 300.0,
    [ValidateRange(25.0, 55.0)] [double]$MinimumAltitudeDegrees = 25.0,
    [ValidateRange(25.0, 55.0)] [double]$MaximumAltitudeDegrees = 55.0,
    [ValidateRange(0.1, 20.0)] [double]$MaximumAbsoluteDeclinationDegrees = 5.0
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
if ($MinimumAzimuthDegrees -gt $MaximumAzimuthDegrees -or $MinimumAltitudeDegrees -gt $MaximumAltitudeDegrees) {
    throw 'Regular-drift pointing limits are inverted.'
}

$base = 'http://localhost:1888/v2/api'
$mount = (Invoke-RestMethod -Uri "$base/equipment/mount/info" -TimeoutSec 10).Response
if (-not $mount.Connected -or -not $mount.TrackingEnabled -or $mount.Slewing) {
    throw 'Mount must be connected, tracking, and idle for regular drift capture.'
}
$azimuth = [double]$mount.Azimuth
$altitude = [double]$mount.Altitude
$declination = [double]$mount.Declination
if ($azimuth -lt $MinimumAzimuthDegrees -or $azimuth -gt $MaximumAzimuthDegrees -or
    $altitude -lt $MinimumAltitudeDegrees -or $altitude -gt $MaximumAltitudeDegrees -or
    [Math]::Abs($declination) -gt $MaximumAbsoluteDeclinationDegrees) {
    throw ("Regular-drift preflight failed: Az={0:F2}, Alt={1:F2}, Dec={2:F3}." -f $azimuth, $altitude, $declination)
}

$supervisor = Join-Path $PSScriptRoot 'tppa_phd2_supervisor.ps1'
if (-not (Test-Path -LiteralPath $supervisor -PathType Leaf)) {
    throw "TPPA/PHD2 supervisor not found: $supervisor"
}

Write-Host ("Regular-drift preflight passed: Az={0:F2}, Alt={1:F2}, Dec={2:F3}." -f $azimuth, $altitude, $declination)
& $supervisor `
    -Mode Phd2Drift `
    -DriftMinutes $DriftMinutes `
    -Phd2ExposureMs $ExposureMilliseconds `
    -RequireRecentAutofocus $false `
    -RequirePdaNearPole $false `
    -Phd2CaptureAttempts 1

if (-not $?) {
    exit 1
}
exit 0
