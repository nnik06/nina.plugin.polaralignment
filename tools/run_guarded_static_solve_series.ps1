param(
    [ValidateRange(3, 60)]
    [int]$Samples = 10,
    [ValidateRange(10, 300)]
    [int]$CadenceSeconds = 60,
    [ValidateRange(0.5, 30.0)]
    [double]$ExposureSeconds = 3.0,
    [ValidateRange(0.01, 1.0)]
    [double]$MaxEquatorialDriftDegrees = 0.10,
    [string]$OutputRoot = 'C:\Users\nnik0\Documents\TPPA-PHD2-tests'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$api = 'http://localhost:1888/v2/api'
$runId = 'static-solves-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
$runDirectory = Join-Path $OutputRoot $runId
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$jsonlPath = Join-Path $runDirectory 'samples.jsonl'
$summaryPath = Join-Path $runDirectory 'summary.json'

function Get-Mount {
    (Invoke-RestMethod -Uri "$api/equipment/mount/info" -TimeoutSec 10).Response
}

function Assert-SafeMount {
    param(
        [Parameter(Mandatory = $true)]$Mount,
        [Parameter(Mandatory = $true)]$InitialMount
    )

    $az = [double]$Mount.Azimuth
    $alt = [double]$Mount.Altitude
    $azSafe = ($az -ge 270.0 -and $az -le 360.0) -or ($az -ge 0.0 -and $az -le 10.0)
    if (-not $Mount.Connected -or $Mount.Slewing -or -not $Mount.TrackingEnabled -or
        -not $azSafe -or $alt -lt 25.0 -or $alt -gt 55.0) {
        throw "Mount safety gate failed: connected=$($Mount.Connected), slewing=$($Mount.Slewing), tracking=$($Mount.TrackingEnabled), Az=$az, Alt=$alt."
    }

    $raDeltaDegrees = [Math]::Abs(([double]$Mount.RightAscension - [double]$InitialMount.RightAscension) * 15.0)
    $decDeltaDegrees = [Math]::Abs([double]$Mount.Declination - [double]$InitialMount.Declination)
    if ($raDeltaDegrees -gt $MaxEquatorialDriftDegrees -or $decDeltaDegrees -gt $MaxEquatorialDriftDegrees) {
        throw "No-slew gate failed: dRA=$raDeltaDegrees deg, dDec=$decDeltaDegrees deg."
    }
    if ([string]$Mount.SideOfPier -ne [string]$InitialMount.SideOfPier) {
        throw "No-slew gate failed: pier side changed from $($InitialMount.SideOfPier) to $($Mount.SideOfPier)."
    }
}

$camera = (Invoke-RestMethod -Uri "$api/equipment/camera/info" -TimeoutSec 10).Response
if (-not $camera.Connected -or $camera.IsExposing) {
    throw "Camera must be connected and idle: connected=$($camera.Connected), exposing=$($camera.IsExposing)."
}

$initialMount = Get-Mount
Assert-SafeMount -Mount $initialMount -InitialMount $initialMount
$samplesOut = [System.Collections.Generic.List[object]]::new()

for ($index = 1; $index -le $Samples; $index++) {
    $startedUtc = [DateTime]::UtcNow
    $before = Get-Mount
    Assert-SafeMount -Mount $before -InitialMount $initialMount

    $duration = $ExposureSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)
    $uri = "$api/equipment/camera/capture?solve=true&duration=$duration&waitForResult=true&omitImage=true&imageType=SNAPSHOT"
    $capture = Invoke-RestMethod -Uri $uri -TimeoutSec 180
    if (-not $capture.Success -or -not $capture.Response.PlateSolveResult.Success) {
        throw "Plate solve failed at sample $index."
    }

    $after = Get-Mount
    Assert-SafeMount -Mount $after -InitialMount $initialMount
    $solve = $capture.Response.PlateSolveResult
    $sample = [ordered]@{
        Index = $index
        StartedUtc = $startedUtc.ToString('o')
        CompletedUtc = [DateTime]::UtcNow.ToString('o')
        MountRaHours = [double]$after.RightAscension
        MountDecDegrees = [double]$after.Declination
        MountAzDegrees = [double]$after.Azimuth
        MountAltDegrees = [double]$after.Altitude
        SideOfPier = [string]$after.SideOfPier
        SolveRaDegreesJ2000 = [double]$solve.Coordinates.RADegrees
        SolveDecDegreesJ2000 = [double]$solve.Coordinates.Dec
        PixelScaleArcsec = [double]$solve.Pixscale
        PositionAngleDegrees = [double]$solve.PositionAngle
    }
    $samplesOut.Add([pscustomobject]$sample)
    $sample | ConvertTo-Json -Compress | Add-Content -LiteralPath $jsonlPath -Encoding utf8
    Write-Host ("Sample {0}/{1}: solve RA={2:F8} deg Dec={3:F8} deg; mount Az={4:F3} Alt={5:F3}." -f
        $index, $Samples, $sample.SolveRaDegreesJ2000, $sample.SolveDecDegreesJ2000,
        $sample.MountAzDegrees, $sample.MountAltDegrees)

    if ($index -lt $Samples) {
        $elapsedSeconds = ([DateTime]::UtcNow - $startedUtc).TotalSeconds
        $waitSeconds = [Math]::Max(0.0, $CadenceSeconds - $elapsedSeconds)
        Start-Sleep -Milliseconds ([int][Math]::Round($waitSeconds * 1000.0))
    }
}

$first = $samplesOut[0]
$cosDec = [Math]::Cos(([double]$first.SolveDecDegreesJ2000) * [Math]::PI / 180.0)
$offsets = foreach ($sample in $samplesOut) {
    [pscustomobject]@{
        Index = $sample.Index
        ElapsedMinutes = ([DateTime]$sample.StartedUtc - [DateTime]$first.StartedUtc).TotalMinutes
        DeltaRaArcsec = ([double]$sample.SolveRaDegreesJ2000 - [double]$first.SolveRaDegreesJ2000) * $cosDec * 3600.0
        DeltaDecArcsec = ([double]$sample.SolveDecDegreesJ2000 - [double]$first.SolveDecDegreesJ2000) * 3600.0
    }
}

function Get-LinearFit {
    param(
        [Parameter(Mandatory = $true)]$Points,
        [Parameter(Mandatory = $true)][string]$ValueProperty
    )

    $meanTime = ($Points.ElapsedMinutes | Measure-Object -Average).Average
    $values = @($Points | ForEach-Object { [double]$_.$ValueProperty })
    $meanValue = ($values | Measure-Object -Average).Average
    $numerator = 0.0
    $denominator = 0.0
    for ($index = 0; $index -lt $Points.Count; $index++) {
        $timeDelta = [double]$Points[$index].ElapsedMinutes - $meanTime
        $numerator += $timeDelta * ($values[$index] - $meanValue)
        $denominator += $timeDelta * $timeDelta
    }
    if ($denominator -le 0.0) { throw 'Static solve timestamps do not span time.' }

    $slope = $numerator / $denominator
    $intercept = $meanValue - $slope * $meanTime
    $residualSumSquares = 0.0
    foreach ($point in $Points) {
        $residual = [double]$point.$ValueProperty -
            ($intercept + $slope * [double]$point.ElapsedMinutes)
        $residualSumSquares += $residual * $residual
    }
    [pscustomobject]@{
        SlopeArcsecPerMinute = $slope
        ResidualRmsArcsec = [Math]::Sqrt($residualSumSquares / $Points.Count)
        SpanArcsec = ($values | Measure-Object -Maximum).Maximum -
            ($values | Measure-Object -Minimum).Minimum
    }
}

$raFit = Get-LinearFit -Points @($offsets) -ValueProperty 'DeltaRaArcsec'
$decFit = Get-LinearFit -Points @($offsets) -ValueProperty 'DeltaDecArcsec'
$summary = [ordered]@{
    RunId = $runId
    Samples = $Samples
    CadenceSeconds = $CadenceSeconds
    ExposureSeconds = $ExposureSeconds
    InitialMount = $initialMount
    OffsetsFromFirstSolveArcsec = @($offsets)
    LinearFit = [ordered]@{
        RightAscension = $raFit
        Declination = $decFit
        VectorSlopeArcsecPerMinute = [Math]::Sqrt(
            $raFit.SlopeArcsecPerMinute * $raFit.SlopeArcsecPerMinute +
            $decFit.SlopeArcsecPerMinute * $decFit.SlopeArcsecPerMinute)
    }
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding utf8
Write-Host "Static solve series complete: $runDirectory"
