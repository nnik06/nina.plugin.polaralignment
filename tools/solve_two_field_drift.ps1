param(
    [Parameter(Mandatory = $true)] [string]$FirstSummaryPath,
    [Parameter(Mandatory = $true)] [ValidateRange(270.0, 300.0)] [double]$FirstAzimuthDegrees,
    [Parameter(Mandatory = $true)] [ValidateRange(25.0, 55.0)] [double]$FirstAltitudeDegrees,
    [Parameter(Mandatory = $true)] [string]$SecondSummaryPath,
    [Parameter(Mandatory = $true)] [ValidateRange(270.0, 300.0)] [double]$SecondAzimuthDegrees,
    [Parameter(Mandatory = $true)] [ValidateRange(25.0, 55.0)] [double]$SecondAltitudeDegrees,
    [Parameter(Mandatory = $true)] [ValidateRange(-90.0, 90.0)] [double]$LatitudeDegrees,
    [double]$TppaAzimuthErrorArcMinutes = [double]::NaN,
    [double]$TppaAltitudeErrorArcMinutes = [double]::NaN,
    [ValidateRange(0.001, 1.0)] [double]$MinimumComputableDeterminant = 0.005,
    [ValidateRange(0.001, 1.0)] [double]$MinimumQualifiedDeterminant = 0.04,
    [ValidateRange(1.0, 90.0)] [double]$MinimumQualifiedHourAngleSeparationDegrees = 45.0,
    [ValidateRange(25.0, 55.0)] [double]$MinimumQualifiedAltitudeDegrees = 40.0,
    [ValidateRange(0.1, 60.0)] [double]$MaximumAxisDifferenceArcMinutes = 2.0
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$siderealCoefficient = 0.2625

function Read-StableDrift {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Drift summary not found: $Path" }
    $rows = @(Import-Csv -LiteralPath $Path)
    if ($rows.Count -ne 1) { throw "Expected exactly one drift segment in $Path; found $($rows.Count)." }
    $row = $rows[0]
    if ([string]$row.DriftStable -ne 'True' -or [string]$row.ComparisonEligible -ne 'True') {
        throw "Drift summary is not stable and comparison-eligible: $Path"
    }
    $slope = 0.0
    if (-not [double]::TryParse([string]$row.ComparisonSlopeArcsecPerMin,
            [Globalization.NumberStyles]::Float,
            [Globalization.CultureInfo]::InvariantCulture,
            [ref]$slope)) { throw "Drift summary has no valid arcsec/min slope: $Path" }
    return $slope
}

function Get-HourAngleRadians {
    param([double]$AzimuthDegrees, [double]$AltitudeDegrees, [double]$LatitudeDegrees)
    $radians = [Math]::PI / 180.0
    $azimuth = $AzimuthDegrees * $radians
    $altitude = $AltitudeDegrees * $radians
    $latitude = $LatitudeDegrees * $radians
    return [Math]::Atan2(
        -[Math]::Sin($azimuth) * [Math]::Cos($altitude),
        [Math]::Sin($altitude) * [Math]::Cos($latitude) -
            [Math]::Cos($altitude) * [Math]::Sin($latitude) * [Math]::Cos($azimuth))
}

$firstDrift = Read-StableDrift $FirstSummaryPath
$secondDrift = Read-StableDrift $SecondSummaryPath
$latitude = $LatitudeDegrees * [Math]::PI / 180.0
$firstHourAngle = Get-HourAngleRadians $FirstAzimuthDegrees $FirstAltitudeDegrees $LatitudeDegrees
$secondHourAngle = Get-HourAngleRadians $SecondAzimuthDegrees $SecondAltitudeDegrees $LatitudeDegrees

$a11 = -$siderealCoefficient * [Math]::Sin($firstHourAngle)
$a12 = -$siderealCoefficient * [Math]::Cos($latitude) * [Math]::Cos($firstHourAngle)
$a21 = -$siderealCoefficient * [Math]::Sin($secondHourAngle)
$a22 = -$siderealCoefficient * [Math]::Cos($latitude) * [Math]::Cos($secondHourAngle)
$determinant = $a11 * $a22 - $a12 * $a21
if ([Math]::Abs($determinant) -lt $MinimumComputableDeterminant) {
    throw ("Two-field drift geometry is too ill-conditioned even for diagnostic computation: determinant={0:F6}, minimum={1:F6}." -f
        $determinant, $MinimumComputableDeterminant)
}
$hourAngleSeparation = [Math]::Abs(($secondHourAngle - $firstHourAngle) * 180.0 / [Math]::PI)
if ($hourAngleSeparation -gt 180.0) { $hourAngleSeparation = 360.0 - $hourAngleSeparation }
$geometryQualified = [Math]::Abs($determinant) -ge $MinimumQualifiedDeterminant -and
    $hourAngleSeparation -ge $MinimumQualifiedHourAngleSeparationDegrees -and
    [Math]::Min($FirstAltitudeDegrees, $SecondAltitudeDegrees) -ge $MinimumQualifiedAltitudeDegrees

$altitudeError = ($firstDrift * $a22 - $a12 * $secondDrift) / $determinant
$azimuthError = ($a11 * $secondDrift - $firstDrift * $a21) / $determinant
$totalError = [Math]::Sqrt($altitudeError * $altitudeError + $azimuthError * $azimuthError)
$hasTppa = -not [double]::IsNaN($TppaAzimuthErrorArcMinutes) -and -not [double]::IsNaN($TppaAltitudeErrorArcMinutes)
$azimuthDifference = if ($hasTppa) { [Math]::Abs($azimuthError - $TppaAzimuthErrorArcMinutes) } else { $null }
$altitudeDifference = if ($hasTppa) { [Math]::Abs($altitudeError - $TppaAltitudeErrorArcMinutes) } else { $null }
$signsAgree = if ($hasTppa) {
    [Math]::Sign($azimuthError) -eq [Math]::Sign($TppaAzimuthErrorArcMinutes) -and
    [Math]::Sign($altitudeError) -eq [Math]::Sign($TppaAltitudeErrorArcMinutes)
} else { $false }
$crossMethodAgreement = $geometryQualified -and $hasTppa -and $signsAgree -and
    $azimuthDifference -le $MaximumAxisDifferenceArcMinutes -and
    $altitudeDifference -le $MaximumAxisDifferenceArcMinutes

[pscustomobject]@{
    VerificationOnly = $true
    ActuationEligible = $false
    FirstHourAngleDegrees = $firstHourAngle * 180.0 / [Math]::PI
    SecondHourAngleDegrees = $secondHourAngle * 180.0 / [Math]::PI
    Determinant = $determinant
    HourAngleSeparationDegrees = $hourAngleSeparation
    GeometryQualified = $geometryQualified
    AzimuthErrorArcMinutes = $azimuthError
    AltitudeErrorArcMinutes = $altitudeError
    TotalErrorArcMinutes = $totalError
    TppaProvided = $hasTppa
    TppaSignsAgree = $signsAgree
    AzimuthDifferenceArcMinutes = $azimuthDifference
    AltitudeDifferenceArcMinutes = $altitudeDifference
    CrossMethodAgreement = $crossMethodAgreement
    Reason = if ($geometryQualified) { 'Geometry passes the numeric verification floor, but two-field drift remains verification-only and cannot command UPAS.' } else { 'Geometry is diagnostic-only because conditioning, hour-angle separation, or altitude floor failed; UPAS actuation is forbidden.' }
} | ConvertTo-Json -Depth 4
