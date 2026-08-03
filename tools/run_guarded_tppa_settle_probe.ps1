#requires -Version 7.0
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$RigConfigurationId,
    [Parameter(Mandatory = $true)]
    [ValidateScript({ ($_ -ge 270.0 -and $_ -le 360.0) -or
        ($_ -ge 0.0 -and $_ -le 10.0) })]
    [double]$TargetAzimuthDegrees,
    [Parameter(Mandatory = $true)]
    [ValidateRange(25.0, 55.0)]
    [double]$TargetAltitudeDegrees,
    [ValidateRange(7, 25)]
    [int]$Samples = 13,
    [ValidateRange(3.0, 10.0)]
    [double]$CadenceSeconds = 5.0,
    [ValidateRange(0.5, 3.0)]
    [double]$ExposureSeconds = 1.0,
    [ValidateRange(5.0, 20.0)]
    [double]$MinimumSlewDistanceDegrees = 5.0,
    [ValidateRange(0.05, 2.0)]
    [double]$PointingToleranceDegrees = 0.5,
    [string]$OutputRoot = 'C:\Users\nnik0\Documents\TPPA-PHD2-tests'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$targetAzimuthInsideGuard = ($TargetAzimuthDegrees -ge 271.0 -and $TargetAzimuthDegrees -le 359.0) -or
    ($TargetAzimuthDegrees -ge 0.0 -and $TargetAzimuthDegrees -le 9.0)
if (-not $targetAzimuthInsideGuard -or $TargetAltitudeDegrees -lt 26.0 -or $TargetAltitudeDegrees -gt 54.0) {
    throw 'Settle-probe target must retain a one-degree guard band inside the balcony envelope.'
}

$api = 'http://localhost:1888/v2/api'
$slewScript = Join-Path $PSScriptRoot 'guarded_balcony_drift_slew.ps1'
if (-not (Test-Path -LiteralPath $slewScript -PathType Leaf)) {
    throw "Required guarded slew script is missing: $slewScript"
}

$runId = 'settle-probe-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' +
    ([Guid]::NewGuid().ToString('N').Substring(0, 8))
$runDirectory = Join-Path $OutputRoot $runId
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$receiptPath = Join-Path $runDirectory 'settle-probe.json'
$partialPath = Join-Path $runDirectory 'settle-probe.partial.json'

function Get-SignedCircularDegreesDelta([double]$Value, [double]$Reference) {
    $delta = (($Value - $Reference + 540.0) % 360.0) - 180.0
    if ($delta -le -180.0) { return 180.0 }
    return $delta
}

function Get-Mount {
    return (Invoke-RestMethod -Uri "$api/equipment/mount/info" -TimeoutSec 10).Response
}

function Test-SafeBalconyPoint([double]$Azimuth, [double]$Altitude) {
    $azimuthSafe = ($Azimuth -ge 270.0 -and $Azimuth -le 360.0) -or
        ($Azimuth -ge 0.0 -and $Azimuth -le 10.0)
    return $azimuthSafe -and $Altitude -ge 25.0 -and $Altitude -le 55.0
}

function Assert-StationaryMount($Mount, [string]$Stage) {
    if (-not [bool]$Mount.Connected -or [bool]$Mount.Slewing -or
            -not [bool]$Mount.TrackingEnabled -or [bool]$Mount.AtPark -or
            -not (Test-SafeBalconyPoint ([double]$Mount.Azimuth) ([double]$Mount.Altitude))) {
        throw ("Mount gate failed at {0}: connected={1}, slewing={2}, tracking={3}, " +
            "parked={4}, Az={5:F3}, Alt={6:F3}." -f $Stage, [bool]$Mount.Connected,
            [bool]$Mount.Slewing, [bool]$Mount.TrackingEnabled, [bool]$Mount.AtPark,
            [double]$Mount.Azimuth, [double]$Mount.Altitude)
    }
}

function Get-EquatorialSeparationDegrees($First, $Second) {
    $ra1 = ([double]$First.RightAscension * 15.0) * [Math]::PI / 180.0
    $ra2 = ([double]$Second.RightAscension * 15.0) * [Math]::PI / 180.0
    $dec1 = ([double]$First.Declination) * [Math]::PI / 180.0
    $dec2 = ([double]$Second.Declination) * [Math]::PI / 180.0
    $cosine = [Math]::Sin($dec1) * [Math]::Sin($dec2) +
        [Math]::Cos($dec1) * [Math]::Cos($dec2) * [Math]::Cos($ra2 - $ra1)
    return [Math]::Acos([Math]::Max(-1.0, [Math]::Min(1.0, $cosine))) * 180.0 / [Math]::PI
}

$samplesOut = [Collections.Generic.List[object]]::new()
$startedUtc = [DateTimeOffset]::UtcNow
$preMount = Get-Mount
Assert-StationaryMount $preMount 'pre-slew'

$camera = (Invoke-RestMethod -Uri "$api/equipment/camera/info" -TimeoutSec 10).Response
if (-not [bool]$camera.Connected -or [bool]$camera.IsExposing) {
    throw "Camera must be connected and idle: connected=$($camera.Connected), exposing=$($camera.IsExposing)."
}

try {
    & $slewScript -TargetAzimuthDegrees $TargetAzimuthDegrees `
        -TargetAltitudeDegrees $TargetAltitudeDegrees `
        -PointingToleranceDegrees $PointingToleranceDegrees

    $slewCompletedUtc = [DateTimeOffset]::UtcNow
    $postMount = Get-Mount
    Assert-StationaryMount $postMount 'post-slew'
    $slewDistanceDegrees = Get-EquatorialSeparationDegrees $preMount $postMount
    if ($slewDistanceDegrees -lt $MinimumSlewDistanceDegrees) {
        throw ("Controlled slew distance {0:F3} deg is below required {1:F3} deg." -f
            $slewDistanceDegrees, $MinimumSlewDistanceDegrees)
    }

    $raDeltaDegrees = Get-SignedCircularDegreesDelta `
        (([double]$postMount.RightAscension) * 15.0) `
        (([double]$preMount.RightAscension) * 15.0)
    $slewDirection = if ($raDeltaDegrees -gt 0.0) { 'IncreasingRA' } else { 'DecreasingRA' }

    for ($index = 1; $index -le $Samples; $index++) {
        $scheduledUtc = $slewCompletedUtc.AddSeconds(($index - 1) * $CadenceSeconds)
        $remainingMilliseconds = ($scheduledUtc - [DateTimeOffset]::UtcNow).TotalMilliseconds
        if ($remainingMilliseconds -gt 0.0) {
            Start-Sleep -Milliseconds ([int][Math]::Ceiling($remainingMilliseconds))
        }

        $before = Get-Mount
        Assert-StationaryMount $before "sample-$index-before"
        $captureStartedUtc = [DateTimeOffset]::UtcNow
        $duration = $ExposureSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)
        $uri = "$api/equipment/camera/capture?solve=true&duration=$duration&waitForResult=true&omitImage=true&imageType=SNAPSHOT"
        $capture = Invoke-RestMethod -Uri $uri -TimeoutSec 180
        $captureCompletedUtc = [DateTimeOffset]::UtcNow
        if (-not [bool]$capture.Success -or -not [bool]$capture.Response.PlateSolveResult.Success) {
            throw "Plate solve failed at settle sample $index."
        }

        $after = Get-Mount
        Assert-StationaryMount $after "sample-$index-after"
        $solve = $capture.Response.PlateSolveResult
        $samplesOut.Add([pscustomobject][ordered]@{
            Index = $index
            ScheduledUtc = $scheduledUtc.ToString('O')
            CaptureStartedUtc = $captureStartedUtc.ToString('O')
            CaptureCompletedUtc = $captureCompletedUtc.ToString('O')
            ElapsedFromSlewCompletedSeconds =
                ($captureCompletedUtc - $slewCompletedUtc).TotalSeconds
            SolveRaDegreesJ2000 = [double]$solve.Coordinates.RADegrees
            SolveDecDegreesJ2000 = [double]$solve.Coordinates.Dec
            PixelScaleArcsec = [double]$solve.Pixscale
            PositionAngleDegrees = [double]$solve.PositionAngle
            Flipped = [bool]$solve.Flipped
            MountAzimuthDegrees = [double]$after.Azimuth
            MountAltitudeDegrees = [double]$after.Altitude
            MountSlewing = [bool]$after.Slewing
            MountTracking = [bool]$after.TrackingEnabled
        })
    }

    $receipt = [ordered]@{
        SchemaVersion = 1
        Event = 'tppa-settle-probe'
        RunId = $runId
        RigConfigurationId = $RigConfigurationId
        StartedUtc = $startedUtc.ToString('O')
        SlewCompletedUtc = $slewCompletedUtc.ToString('O')
        CompletedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        TargetAzimuthDegrees = $TargetAzimuthDegrees
        TargetAltitudeDegrees = $TargetAltitudeDegrees
        SlewDistanceDegrees = $slewDistanceDegrees
        RaDeltaDegrees = $raDeltaDegrees
        SlewDirection = $slewDirection
        ExposureSeconds = $ExposureSeconds
        CadenceSeconds = $CadenceSeconds
        PreMount = $preMount
        PostMount = $postMount
        Samples = $samplesOut.ToArray()
    }
    $receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $receiptPath -Encoding utf8
    Write-Host "TPPA settle probe complete: $receiptPath"
} catch {
    try { [void](Invoke-RestMethod -Uri "$api/equipment/mount/slew/stop" -TimeoutSec 5) } catch { }
    [ordered]@{
        SchemaVersion = 1
        Event = 'tppa-settle-probe-failed'
        RunId = $runId
        RigConfigurationId = $RigConfigurationId
        StartedUtc = $startedUtc.ToString('O')
        FailedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        Error = $_.Exception.Message
        Samples = $samplesOut.ToArray()
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $partialPath -Encoding utf8
    throw
}
