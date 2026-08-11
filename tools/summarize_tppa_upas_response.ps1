#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$LogPath,

    [ValidateRange(1, 100)]
    [int]$MinimumSamplesPerAxis = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$number = '[+-]?(?:\d+(?:\.\d+)?|\.\d+)'
$pattern = [regex]::new(
    "TPPA_UPAS_FRESH_RESPONSE_SAMPLE axis=(?<axis>X|Y|mixed), X=(?<x>$number), Y=(?<y>$number), " +
    "deltaAz=(?<deltaAz>$number)', deltaAlt=(?<deltaAlt>$number)'",
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

$samples = [System.Collections.Generic.List[object]]::new()
foreach ($line in Get-Content -LiteralPath $LogPath) {
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
    return [pscustomobject]@{
        Axis = $Axis
        SampleCount = $axisSamples.Count
        MedianAzimuthArcMinutesPerUnit = Get-Median $azimuthResponses
        MedianAltitudeArcMinutesPerUnit = Get-Median $altitudeResponses
        MinimumRequiredSamplesMet = $axisSamples.Count -ge $MinimumSamplesPerAxis
    }
}

$xSummary = Get-AxisSummary X
$ySummary = Get-AxisSummary Y
$complete = $xSummary.MinimumRequiredSamplesMet -and $ySummary.MinimumRequiredSamplesMet

[pscustomobject]@{
    Schema = 'tppa-upas-fresh-response-summary/v1'
    SourceLog = (Resolve-Path -LiteralPath $LogPath).Path
    MinimumSamplesPerAxis = $MinimumSamplesPerAxis
    TwoByTwoResponseComplete = $complete
    XColumn = $xSummary
    YColumn = $ySummary
    Samples = @($samples)
} | ConvertTo-Json -Depth 5

if (-not $complete) {
    Write-Warning 'A complete 2x2 response needs the requested number of isolated X and Y samples. This summary is diagnostic only and grants no motion authority.'
}
