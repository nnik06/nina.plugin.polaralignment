param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1.0, 7200.0)]
    [double]$ExposureSeconds,
    [Parameter(Mandatory = $true)]
    [ValidateRange(0.0, 120.0)]
    [double]$PolarErrorArcminutes,
    [Parameter(Mandatory = $true)]
    [ValidateRange(1.0, 100000.0)]
    [double]$GuideToFarthestCornerPixels,
    [ValidateRange(0.01, 10.0)]
    [double]$AllowedSmearPixels = 0.5,
    [ValidateRange(0.01, 10.0)]
    [double]$ImagingPixelScaleArcseconds = 1.0,
    [ValidateSet('Object', 'Json')]
    [string]$OutputFormat = 'Object'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# The angular-velocity difference between the true and misaligned tracking
# axes is 2*w*sin(error/2). Its line-of-sight component, which rotates the
# guided field around the guide star, cannot exceed that magnitude.
$siderealDaySeconds = 86164.0905
$siderealRateRadiansPerSecond = 2.0 * [Math]::PI / $siderealDaySeconds
$polarErrorRadians =
    $PolarErrorArcminutes * [Math]::PI / (180.0 * 60.0)
$rotationRadians =
    2.0 * $siderealRateRadiansPerSecond * $ExposureSeconds *
    [Math]::Sin($polarErrorRadians / 2.0)
$smearPixels =
    2.0 * $GuideToFarthestCornerPixels *
    [Math]::Sin([Math]::Abs($rotationRadians) / 2.0)

$allowedChordRatio =
    [Math]::Min(1.0, $AllowedSmearPixels /
        (2.0 * $GuideToFarthestCornerPixels))
$allowedRotationRadians = 2.0 * [Math]::Asin($allowedChordRatio)
$allowedAxisRatio =
    [Math]::Min(1.0, $allowedRotationRadians /
        (2.0 * $siderealRateRadiansPerSecond * $ExposureSeconds))
$maximumPolarErrorRadians = 2.0 * [Math]::Asin($allowedAxisRatio)

$result = [pscustomobject][ordered]@{
    SchemaVersion = 1
    Model = 'conservative-guided-field-rotation-upper-bound'
    ExposureSeconds = $ExposureSeconds
    PolarErrorArcminutes = $PolarErrorArcminutes
    GuideToFarthestCornerPixels = $GuideToFarthestCornerPixels
    FieldRotationArcseconds =
        [Math]::Abs($rotationRadians) * 180.0 / [Math]::PI * 3600.0
    MaximumSmearPixels = $smearPixels
    ImagingPixelScaleArcseconds = $ImagingPixelScaleArcseconds
    MaximumSmearArcseconds = $smearPixels * $ImagingPixelScaleArcseconds
    AllowedSmearPixels = $AllowedSmearPixels
    MaximumPolarErrorArcminutesForAllowedSmear =
        $maximumPolarErrorRadians * 180.0 / [Math]::PI * 60.0
    AssumesGuideOutputRemovesTranslation = $true
    GrantsAbsolutePolarAccuracyClaim = $false
}

if ($OutputFormat -eq 'Json') {
    $result | ConvertTo-Json -Depth 4
} else {
    $result
}
