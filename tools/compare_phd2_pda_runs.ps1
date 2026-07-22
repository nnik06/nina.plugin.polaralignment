param(
    [Parameter(Mandatory)] [ValidateCount(2, 20)] [string[]]$ResultPaths,
    [ValidateRange(0.0, 60.0)] [double]$MagnitudeFloorArcMinutes = 0.5,
    [ValidateRange(0.0, 1.0)] [double]$MagnitudeFraction = 0.15,
    [ValidateRange(0.0, 180.0)] [double]$DirectionLimitDegrees = 7.5,
    [string]$OutputPath
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$captures = @($ResultPaths | ForEach-Object {
    $path = [IO.Path]::GetFullPath($_)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "PDA result not found: $path" }
    $result = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    [pscustomobject]@{
        Path = $path
        Stable = [bool]$result.PdaStable
        MagnitudeArcMinutes = [double]$result.PdaErrorArcMinutes
        DirectionDegrees = [double]$result.PdaDisplayAngleDegrees
        Reason = [string]$result.PdaReason
    }
})

function Get-CircularDifference([double]$First, [double]$Second) {
    $difference = [Math]::Abs($First - $Second) % 360.0
    if ($difference -gt 180.0) { return 360.0 - $difference }
    return $difference
}

$meanMagnitude = ($captures | Measure-Object MagnitudeArcMinutes -Average).Average
$maxMagnitudeDifference = 0.0
$maxDirectionDifference = 0.0
for ($left = 0; $left -lt $captures.Count; $left++) {
    for ($right = $left + 1; $right -lt $captures.Count; $right++) {
        $maxMagnitudeDifference = [Math]::Max($maxMagnitudeDifference,
            [Math]::Abs($captures[$left].MagnitudeArcMinutes - $captures[$right].MagnitudeArcMinutes))
        $maxDirectionDifference = [Math]::Max($maxDirectionDifference,
            (Get-CircularDifference $captures[$left].DirectionDegrees $captures[$right].DirectionDegrees))
    }
}

$magnitudeLimit = [Math]::Max($MagnitudeFloorArcMinutes, $meanMagnitude * $MagnitudeFraction)
$repeatable = $false
if ($captures.Stable -contains $false) {
    $reason = "At least one capture failed its within-capture stability gate."
} elseif ($maxMagnitudeDifference -gt $magnitudeLimit) {
    $reason = "Cross-capture magnitude disagreement $($maxMagnitudeDifference.ToString('F3'))' exceeds $($magnitudeLimit.ToString('F3'))'."
} elseif ($maxDirectionDifference -gt $DirectionLimitDegrees) {
    $reason = "Cross-capture direction disagreement $($maxDirectionDifference.ToString('F2')) deg exceeds $($DirectionLimitDegrees.ToString('F2')) deg."
} else {
    $repeatable = $true
    $reason = "Independent captures passed magnitude and direction repeatability gates."
}

$report = [ordered]@{
    CaptureCount = $captures.Count
    MeanMagnitudeArcMinutes = $meanMagnitude
    MaximumMagnitudeDifferenceArcMinutes = $maxMagnitudeDifference
    MagnitudeLimitArcMinutes = $magnitudeLimit
    MaximumDirectionDifferenceDegrees = $maxDirectionDifference
    DirectionLimitDegrees = $DirectionLimitDegrees
    Repeatable = $repeatable
    CorrectionAuthorized = $false
    Reason = $reason
    CorrectionReason = if ($repeatable) {
        "Repeatability alone is insufficient: camera-to-UPAS direction calibration and an independent TPPA verification are still required."
    } else {
        "PDA correction denied because independent captures are not repeatable."
    }
    Captures = $captures
}

$json = $report | ConvertTo-Json -Depth 6
if ($OutputPath) { [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $json, [Text.UTF8Encoding]::new($false)) }
$json
if (-not $repeatable) { exit 2 }
exit 0
