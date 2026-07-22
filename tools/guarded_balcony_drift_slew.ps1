param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(270.0, 300.0)]
    [double]$TargetAzimuthDegrees,
    [Parameter(Mandatory = $true)]
    [ValidateRange(25.0, 55.0)]
    [double]$TargetAltitudeDegrees,
    [ValidateRange(0.05, 2.0)]
    [double]$PointingToleranceDegrees = 0.5
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$base = 'http://localhost:1888/v2/api'
$mount = (Invoke-RestMethod -Uri "$base/equipment/mount/info" -TimeoutSec 10).Response
if (-not $mount.Connected -or $mount.Slewing -or -not $mount.TrackingEnabled) {
    throw 'Mount must be connected, tracking, and idle before a guarded balcony slew.'
}

$toRadians = [Math]::PI / 180.0
$toDegrees = 180.0 / [Math]::PI
$latitude = [double]$mount.SiteLatitude * $toRadians
$azimuth = $TargetAzimuthDegrees * $toRadians
$altitude = $TargetAltitudeDegrees * $toRadians

$sinDeclination = [Math]::Sin($altitude) * [Math]::Sin($latitude) +
    [Math]::Cos($altitude) * [Math]::Cos($latitude) * [Math]::Cos($azimuth)
$declination = [Math]::Asin([Math]::Max(-1.0, [Math]::Min(1.0, $sinDeclination)))
$hourAngle = [Math]::Atan2(
    -[Math]::Sin($azimuth) * [Math]::Cos($altitude),
    [Math]::Sin($altitude) * [Math]::Cos($latitude) -
        [Math]::Cos($altitude) * [Math]::Sin($latitude) * [Math]::Cos($azimuth))
$raDegrees = ((([double]$mount.SiderealTime * 15.0) - ($hourAngle * $toDegrees)) % 360.0 + 360.0) % 360.0
$declinationDegrees = $declination * $toDegrees

$ra = [uri]::EscapeDataString($raDegrees.ToString([Globalization.CultureInfo]::InvariantCulture))
$dec = [uri]::EscapeDataString($declinationDegrees.ToString([Globalization.CultureInfo]::InvariantCulture))
$uri = "$base/equipment/mount/slew?ra=$ra&dec=$dec&waitForResult=true&center=false&rotate=false"
Write-Host ("Guarded balcony slew: target Az={0:F2}, Alt={1:F2}, RA={2:F6} deg, Dec={3:F6} deg." -f
    $TargetAzimuthDegrees, $TargetAltitudeDegrees, $raDegrees, $declinationDegrees)
$slew = Invoke-RestMethod -Uri $uri -TimeoutSec 180
if (-not $slew.Success) { throw "NINA slew failed: $($slew.Error)" }

$actual = (Invoke-RestMethod -Uri "$base/equipment/mount/info" -TimeoutSec 10).Response
$azimuthError = [Math]::Abs([double]$actual.Azimuth - $TargetAzimuthDegrees)
$altitudeError = [Math]::Abs([double]$actual.Altitude - $TargetAltitudeDegrees)
if ($actual.Slewing -or -not $actual.TrackingEnabled -or
    [double]$actual.Azimuth -lt 270.0 -or [double]$actual.Azimuth -gt 300.0 -or
    [double]$actual.Altitude -lt 25.0 -or [double]$actual.Altitude -gt 55.0 -or
    $azimuthError -gt $PointingToleranceDegrees -or $altitudeError -gt $PointingToleranceDegrees) {
    throw ("Post-slew guard failed: Az={0:F2}, Alt={1:F2}, dAz={2:F2}, dAlt={3:F2}." -f
        [double]$actual.Azimuth, [double]$actual.Altitude, $azimuthError, $altitudeError)
}

Write-Host ("Guarded balcony slew complete: Az={0:F2}, Alt={1:F2}, Dec={2:F3}, tracking={3}." -f
    [double]$actual.Azimuth, [double]$actual.Altitude, [double]$actual.Declination, [bool]$actual.TrackingEnabled)
