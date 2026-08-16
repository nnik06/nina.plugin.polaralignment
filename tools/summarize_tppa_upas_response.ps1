#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$LogPath,

    [ValidateRange(1, 100)]
    [int]$MinimumSamplesPerAxis = 1,

    [ValidateRange(2, 100)]
    [int]$MinimumSamplesForUncertainty = 3,

    [ValidateRange(0.001, 1.0)]
    [double]$MaximumRelativeUncertainty = 0.10,

    [ValidateRange(1.0, 100.0)]
    [double]$MaximumConditionNumber = 5.0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$number = '[+-]?(?:\d+(?:\.\d+)?|\.\d+)'
$pattern = [regex]::new(
    "TPPA_UPAS_FRESH_RESPONSE_SAMPLE axis=(?<axis>X|Y|mixed), X=(?<x>$number), Y=(?<y>$number), " +
    "deltaAz=(?<deltaAz>$number)', deltaAlt=(?<deltaAlt>$number)'",
    [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
$rejectedPattern = [regex]::new(
    "TPPA_UPAS_PROBE_REJECTED axis=(?<axis>X|Y), attemptedUnits=(?<attempted>$number), " +
    "rejectedAttempts=(?<rejected>\d+)/(?<maximum>\d+), nextProbeUnits=(?<next>$number)",
    [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)

function Convert-InvariantDouble([string]$Value) {
    return [double]::Parse($Value, [Globalization.CultureInfo]::InvariantCulture)
}

function Get-Median([double[]]$Values) {
    if ($Values.Count -eq 0) { return $null }
    $ordered = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($ordered.Count / 2)
    if ($ordered.Count % 2 -eq 1) { return $ordered[$middle] }
    return ($ordered[$middle - 1] + $ordered[$middle]) / 2.0
}

function Get-Maximum([double[]]$Values) {
    if ($Values.Count -eq 0) { return $null }
    return ($Values | Measure-Object -Maximum).Maximum
}

function Get-ConditionNumber(
        [double]$A00,
        [double]$A01,
        [double]$A10,
        [double]$A11) {
    $sumSquares = $A00 * $A00 + $A01 * $A01 + $A10 * $A10 + $A11 * $A11
    $determinant = $A00 * $A11 - $A01 * $A10
    $discriminant = [Math]::Sqrt([Math]::Max(
        0.0,
        $sumSquares * $sumSquares - 4.0 * $determinant * $determinant))
    $maximumSquared = ($sumSquares + $discriminant) / 2.0
    $minimumSquared = ($sumSquares - $discriminant) / 2.0
    if ($minimumSquared -le 1e-18 -or $maximumSquared -le 0.0) {
        return [double]::PositiveInfinity
    }
    return [Math]::Sqrt($maximumSquared / $minimumSquared)
}

$samples = [System.Collections.Generic.List[object]]::new()
$rejectedProbes = [System.Collections.Generic.List[object]]::new()
foreach ($line in Get-Content -LiteralPath $LogPath) {
    $rejectedMatch = $rejectedPattern.Match($line)
    if ($rejectedMatch.Success) {
        [void]$rejectedProbes.Add([pscustomobject]@{
            Axis = $rejectedMatch.Groups['axis'].Value
            AttemptedUnits = Convert-InvariantDouble $rejectedMatch.Groups['attempted'].Value
            RejectedAttempts = [int]$rejectedMatch.Groups['rejected'].Value
            MaximumRejectedAttempts = [int]$rejectedMatch.Groups['maximum'].Value
            NextProbeUnits = Convert-InvariantDouble $rejectedMatch.Groups['next'].Value
        })
    }
    $match = $pattern.Match($line)
    if (-not $match.Success) { continue }

    $axis = $match.Groups['axis'].Value
    if ($axis -eq 'mixed') { continue }

    $x = Convert-InvariantDouble $match.Groups['x'].Value
    $y = Convert-InvariantDouble $match.Groups['y'].Value
    $deltaAz = Convert-InvariantDouble $match.Groups['deltaAz'].Value
    $deltaAlt = Convert-InvariantDouble $match.Groups['deltaAlt'].Value
    $units = if ($axis -eq 'X') { $x } else { $y }
    if ([Math]::Abs($units) -le 1e-9) { continue }

    [void]$samples.Add([pscustomobject]@{
        Axis = $axis
        XUnits = $x
        YUnits = $y
        DeltaAzArcMinutes = $deltaAz
        DeltaAltArcMinutes = $deltaAlt
        AzimuthArcMinutesPerUnit = $deltaAz / $units
        AltitudeArcMinutesPerUnit = $deltaAlt / $units
    })
}

function Get-AxisSummary([string]$Axis) {
    $axisSamples = @($samples | Where-Object Axis -eq $Axis)
    $azimuthResponses = @($axisSamples | ForEach-Object { [double]$_.AzimuthArcMinutesPerUnit })
    $altitudeResponses = @($axisSamples | ForEach-Object { [double]$_.AltitudeArcMinutesPerUnit })
    $medianAzimuth = Get-Median $azimuthResponses
    $medianAltitude = Get-Median $altitudeResponses
    $medianMagnitude = if ($null -ne $medianAzimuth -and $null -ne $medianAltitude) {
        [Math]::Sqrt($medianAzimuth * $medianAzimuth + $medianAltitude * $medianAltitude)
    } else { $null }
    $relativeDeviations = [System.Collections.Generic.List[double]]::new()
    $directionConsistent = $axisSamples.Count -gt 0
    if ($null -ne $medianMagnitude -and $medianMagnitude -gt 1e-12) {
        foreach ($sample in $axisSamples) {
            $differenceAzimuth = [double]$sample.AzimuthArcMinutesPerUnit - $medianAzimuth
            $differenceAltitude = [double]$sample.AltitudeArcMinutesPerUnit - $medianAltitude
            $differenceSquared = $differenceAzimuth * $differenceAzimuth `
                + $differenceAltitude * $differenceAltitude
            [void]$relativeDeviations.Add(
                [Math]::Sqrt($differenceSquared) / $medianMagnitude)
            $dot = [double]$sample.AzimuthArcMinutesPerUnit * $medianAzimuth `
                + [double]$sample.AltitudeArcMinutesPerUnit * $medianAltitude
            if ($dot -le 0.0) { $directionConsistent = $false }
        }
    } else {
        $directionConsistent = $false
    }
    $axisRejected = @($rejectedProbes | Where-Object Axis -eq $Axis)
    $largestRejectedAttempt = Get-Maximum @($axisRejected |
        ForEach-Object { [Math]::Abs([double]$_.AttemptedUnits) })
    $maximumRejectedCount = Get-Maximum @($axisRejected |
        ForEach-Object { [double]$_.RejectedAttempts })
    return [pscustomobject]@{
        Axis = $Axis
        SampleCount = $axisSamples.Count
        MedianAzimuthArcMinutesPerUnit = $medianAzimuth
        MedianAltitudeArcMinutesPerUnit = $medianAltitude
        MedianVectorMagnitudeArcMinutesPerUnit = $medianMagnitude
        MaximumRelativeVectorDeviation = Get-Maximum @($relativeDeviations)
        DirectionConsistent = $directionConsistent
        UncertaintySampleFloorMet = $axisSamples.Count -ge $MinimumSamplesForUncertainty
        RejectedProbeCount = $axisRejected.Count
        LargestRejectedAttemptUnits = $largestRejectedAttempt
        MaximumRejectedAttempts = $maximumRejectedCount
        MinimumRequiredSamplesMet = $axisSamples.Count -ge $MinimumSamplesPerAxis
    }
}

$xSummary = Get-AxisSummary X
$ySummary = Get-AxisSummary Y
$complete = $xSummary.MinimumRequiredSamplesMet -and $ySummary.MinimumRequiredSamplesMet
$matrixConditionNumber = if ($complete) {
    Get-ConditionNumber `
        $xSummary.MedianAzimuthArcMinutesPerUnit `
        $ySummary.MedianAzimuthArcMinutesPerUnit `
        $xSummary.MedianAltitudeArcMinutesPerUnit `
        $ySummary.MedianAltitudeArcMinutesPerUnit
} else { $null }
$relativeUncertainty = if (($xSummary.UncertaintySampleFloorMet -and
        $ySummary.UncertaintySampleFloorMet -and
        $null -ne $xSummary.MaximumRelativeVectorDeviation -and
        $null -ne $ySummary.MaximumRelativeVectorDeviation)) {
    [Math]::Max(
        [double]$xSummary.MaximumRelativeVectorDeviation,
        [double]$ySummary.MaximumRelativeVectorDeviation)
} else { $null }
$clampLimitedRecoveryQualified = ($complete -and
    $null -ne $relativeUncertainty -and
    $relativeUncertainty -le $MaximumRelativeUncertainty -and
    $null -ne $matrixConditionNumber -and
    [double]::IsFinite([double]$matrixConditionNumber) -and
    $matrixConditionNumber -le $MaximumConditionNumber -and
    $xSummary.DirectionConsistent -and
    $ySummary.DirectionConsistent)
$clampLimitedRecoveryIssues = [System.Collections.Generic.List[string]]::new()
if (-not $complete) {
    [void]$clampLimitedRecoveryIssues.Add(
        "The isolated 2x2 response does not meet the requested per-axis sample floor of $MinimumSamplesPerAxis.")
}
if (-not $xSummary.UncertaintySampleFloorMet -or
        -not $ySummary.UncertaintySampleFloorMet) {
    [void]$clampLimitedRecoveryIssues.Add(
        "At least $MinimumSamplesForUncertainty accepted isolated samples per axis are required to estimate response uncertainty.")
}
if (-not $xSummary.DirectionConsistent -or -not $ySummary.DirectionConsistent) {
    [void]$clampLimitedRecoveryIssues.Add(
        'At least one axis contains a direction-inconsistent response sample.')
}
if ($null -ne $matrixConditionNumber -and
        (-not [double]::IsFinite([double]$matrixConditionNumber) -or
         $matrixConditionNumber -gt $MaximumConditionNumber)) {
    [void]$clampLimitedRecoveryIssues.Add(
        "The median response-matrix condition number exceeds $MaximumConditionNumber.")
}
if ($null -ne $relativeUncertainty -and
        $relativeUncertainty -gt $MaximumRelativeUncertainty) {
    [void]$clampLimitedRecoveryIssues.Add(
        "The maximum observed relative vector deviation exceeds $MaximumRelativeUncertainty.")
}

[pscustomobject]@{
    Schema = 'tppa-upas-fresh-response-summary/v2'
    SourceLog = (Resolve-Path -LiteralPath $LogPath).Path
    MinimumSamplesPerAxis = $MinimumSamplesPerAxis
    MinimumSamplesForUncertainty = $MinimumSamplesForUncertainty
    TwoByTwoResponseComplete = $complete
    MatrixConditionNumber = $matrixConditionNumber
    MeasuredRelativeResponseUncertainty = $relativeUncertainty
    MaximumQualifiedRelativeUncertainty = $MaximumRelativeUncertainty
    MaximumQualifiedConditionNumber = $MaximumConditionNumber
    ClampLimitedRecoveryQualified = $clampLimitedRecoveryQualified
    ClampLimitedRecoveryIssues = @($clampLimitedRecoveryIssues)
    MatrixDegreesPerUnit = if ($complete) {
        [pscustomobject]@{
            Azimuth = [pscustomobject]@{
                X = $xSummary.MedianAzimuthArcMinutesPerUnit / 60.0
                Y = $ySummary.MedianAzimuthArcMinutesPerUnit / 60.0
            }
            Altitude = [pscustomobject]@{
                X = $xSummary.MedianAltitudeArcMinutesPerUnit / 60.0
                Y = $ySummary.MedianAltitudeArcMinutesPerUnit / 60.0
            }
        }
    } else { $null }
    XColumn = $xSummary
    YColumn = $ySummary
    Samples = @($samples)
    RejectedProbes = @($rejectedProbes)
} | ConvertTo-Json -Depth 5

if (-not $complete) {
    Write-Warning 'A complete 2x2 response needs the requested number of isolated X and Y samples. This summary is diagnostic only and grants no motion authority.'
} elseif (-not $clampLimitedRecoveryQualified) {
    Write-Warning ('Clamp-limited recovery is not qualified: ' +
        ($clampLimitedRecoveryIssues -join ' '))
}
