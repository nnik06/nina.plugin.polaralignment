param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ ($_ -ge 270.0 -and $_ -le 360.0) -or ($_ -ge 0.0 -and $_ -le 10.0) })]
    [double]$TargetAzimuthDegrees,
    [Parameter(Mandatory = $true)]
    [ValidateRange(25.0, 55.0)]
    [double]$TargetAltitudeDegrees,
    [Parameter(Mandatory = $true)]
    [ValidateSet('pierEast', 'pierWest')]
    [string]$ExpectedPierSide,
    [Parameter(Mandatory = $true)]
    [string]$RunId,
    [ValidateRange(5, 30)]
    [int]$BaselineSeconds = 8,
    [ValidateRange(20, 300)]
    [int]$CaptureDurationSeconds = 90,
    [ValidateRange(100, 1000)]
    [int]$CaptureCadenceMilliseconds = 200,
    [ValidateRange(100, 1000)]
    [int]$WatchdogCadenceMilliseconds = 150,
    [string]$Root = 'C:\Users\nnik0\Documents\UPAS\ipolar-slew-stability'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$api = 'http://127.0.0.1:1888/v2/api'
$recorder = Join-Path $PSScriptRoot 'ipolar_slew_capture.ps1'
$starGate = Join-Path $PSScriptRoot 'ipolar_star_observability_gate.ps1'
$slewLauncher = Join-Path $PSScriptRoot 'guarded_balcony_drift_slew.ps1'
$axisEvaluator = Join-Path $PSScriptRoot 'ipolar_slew_axis_evaluator.ps1'
foreach ($required in @($recorder, $starGate, $slewLauncher, $axisEvaluator)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required tool missing: $required" }
}

function Get-MountInfo {
    return (Invoke-RestMethod -Uri "$api/equipment/mount/info" -TimeoutSec 5).Response
}

function Test-SafeAzimuth([double]$Azimuth) {
    return (($Azimuth -ge 270.0 -and $Azimuth -le 360.0) -or ($Azimuth -ge 0.0 -and $Azimuth -le 10.0))
}

function Stop-MountBestEffort {
    try { [void](Invoke-RestMethod -Uri "$api/equipment/mount/slew/stop" -TimeoutSec 5) } catch { }
}

$initial = Get-MountInfo
if (-not [bool]$initial.Connected -or [bool]$initial.Slewing -or -not [bool]$initial.TrackingEnabled -or [bool]$initial.AtPark) {
    throw 'Mount must be connected, tracking, unparked, and idle.'
}
if ([string]$initial.SideOfPier -ne $ExpectedPierSide) {
    throw "Current pier side '$($initial.SideOfPier)' does not match expected '$ExpectedPierSide'."
}
if (-not (Test-SafeAzimuth ([double]$initial.Azimuth)) -or [double]$initial.Altitude -lt 25.0 -or [double]$initial.Altitude -gt 55.0) {
    throw 'Initial pointing is outside the balcony envelope.'
}

$runPath = Join-Path $Root $RunId
$samplesPath = Join-Path $runPath 'samples.jsonl'
$trajectoryPath = Join-Path $runPath 'trajectory.jsonl'
$baselinePath = Join-Path $runPath 'baseline-gate.json'
$axisEvaluationPath = Join-Path $runPath 'axis-evaluation.json'
$recorderStdoutPath = Join-Path $Root "$RunId-recorder.stdout.log"
$recorderStderrPath = Join-Path $Root "$RunId-recorder.stderr.log"
$gateStdoutPath = Join-Path $Root "$RunId-star-gate.stdout.log"
$gateStderrPath = Join-Path $Root "$RunId-star-gate.stderr.log"
$slewStdoutPath = Join-Path $Root "$RunId-slew.stdout.log"
$slewStderrPath = Join-Path $Root "$RunId-slew.stderr.log"
$axisStdoutPath = Join-Path $Root "$RunId-axis.stdout.log"
$axisStderrPath = Join-Path $Root "$RunId-axis.stderr.log"
if (Test-Path -LiteralPath $runPath) {
    throw "Run directory already exists: $runPath"
}
New-Item -ItemType Directory -Force -Path $Root | Out-Null

$pwsh = (Get-Process -Id $PID).Path
$recorderArgs = @('-NoProfile', '-File', $recorder, '-RunId', $RunId, '-DurationSeconds', $CaptureDurationSeconds, '-CadenceMilliseconds', $CaptureCadenceMilliseconds, '-Root', $Root)
$recorderProcess = Start-Process -FilePath $pwsh -ArgumentList $recorderArgs -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput $recorderStdoutPath -RedirectStandardError $recorderStderrPath
$slewProcess = $null
try {
    $baselineDeadline = [DateTime]::UtcNow.AddSeconds($BaselineSeconds + 15)
    do {
        Start-Sleep -Milliseconds 250
        $sampleCount = if (Test-Path -LiteralPath $samplesPath) { @(Get-Content -LiteralPath $samplesPath).Count } else { 0 }
    } while ($sampleCount -lt 5 -and [DateTime]::UtcNow -lt $baselineDeadline -and -not $recorderProcess.HasExited)
    if ($sampleCount -lt 5) { throw "Recorder produced only $sampleCount baseline samples." }

    $gateArgs = @('-NoProfile', '-File', $starGate, '-SamplesPath', $samplesPath,
        '-MinimumFrames', 5, '-OutputPath', $baselinePath)
    $gateProcess = Start-Process -FilePath $pwsh -ArgumentList $gateArgs -Wait -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $gateStdoutPath -RedirectStandardError $gateStderrPath
    if ($gateProcess.ExitCode -ne 0) { throw "iPolar star-observability gate failed; see $baselinePath" }

    $slewArgs = @('-NoProfile', '-File', $slewLauncher, '-TargetAzimuthDegrees', $TargetAzimuthDegrees,
        '-TargetAltitudeDegrees', $TargetAltitudeDegrees, '-PointingToleranceDegrees', 0.5)
    $slewProcess = Start-Process -FilePath $pwsh -ArgumentList $slewArgs -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $slewStdoutPath -RedirectStandardError $slewStderrPath
    $sawSlewing = $false
    $idleSince = $null
    while (-not $slewProcess.HasExited -or $null -eq $idleSince -or ([DateTime]::UtcNow - $idleSince).TotalSeconds -lt 2.0) {
        $sampledUtc = [DateTime]::UtcNow
        $mount = Get-MountInfo
        $safe = [bool]$mount.Connected -and (Test-SafeAzimuth ([double]$mount.Azimuth)) -and
            [double]$mount.Altitude -ge 25.0 -and [double]$mount.Altitude -le 55.0 -and
            [string]$mount.SideOfPier -eq $ExpectedPierSide
        [ordered]@{
            SampledUtc = $sampledUtc.ToString('o'); Connected = [bool]$mount.Connected; Slewing = [bool]$mount.Slewing
            TrackingEnabled = [bool]$mount.TrackingEnabled; SideOfPier = [string]$mount.SideOfPier
            Azimuth = [double]$mount.Azimuth; Altitude = [double]$mount.Altitude; Safe = $safe
        } | ConvertTo-Json -Compress | Add-Content -LiteralPath $trajectoryPath -Encoding utf8
        if (-not $safe) {
            Stop-MountBestEffort
            throw "Trajectory guard failed at Az=$($mount.Azimuth), Alt=$($mount.Altitude), pier=$($mount.SideOfPier)."
        }
        if ([bool]$mount.Slewing) { $sawSlewing = $true; $idleSince = $null }
        elseif ($sawSlewing -and $null -eq $idleSince) { $idleSince = [DateTime]::UtcNow }
        if ($slewProcess.HasExited -and -not $sawSlewing) { throw 'Slew launcher exited without an observed Slewing=true transition.' }
        Start-Sleep -Milliseconds $WatchdogCadenceMilliseconds
    }
    $slewProcess.WaitForExit()
    if ($slewProcess.ExitCode -ne 0) { throw "Guarded slew launcher exited $($slewProcess.ExitCode)." }
} catch {
    Stop-MountBestEffort
    throw
} finally {
    if ($null -ne $slewProcess -and -not $slewProcess.HasExited) { Stop-Process -Id $slewProcess.Id -Force }
    if (-not $recorderProcess.HasExited) { Stop-Process -Id $recorderProcess.Id -Force }
    $recorderProcess.WaitForExit()
}

$axisArgs = @('-NoProfile', '-File', $axisEvaluator, '-SamplesPath', $samplesPath,
    '-OutputPath', $axisEvaluationPath)
$axisProcess = Start-Process -FilePath $pwsh -ArgumentList $axisArgs -Wait -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput $axisStdoutPath -RedirectStandardError $axisStderrPath
if (-not (Test-Path -LiteralPath $axisEvaluationPath -PathType Leaf)) {
    throw "iPolar axis evaluator produced no receipt; exit=$($axisProcess.ExitCode)."
}
$axisEvaluation = [IO.File]::ReadAllText($axisEvaluationPath) | ConvertFrom-Json

$final = Get-MountInfo
$summary = [ordered]@{
    SchemaVersion = 2; RunId = $RunId; SamplesPath = $samplesPath; TrajectoryPath = $trajectoryPath
    BaselineGatePath = $baselinePath; AxisEvaluationPath = $axisEvaluationPath
    RecorderStdoutPath = $recorderStdoutPath; RecorderStderrPath = $recorderStderrPath
    StarGateStdoutPath = $gateStdoutPath; StarGateStderrPath = $gateStderrPath
    SlewStdoutPath = $slewStdoutPath; SlewStderrPath = $slewStderrPath
    AxisStdoutPath = $axisStdoutPath; AxisStderrPath = $axisStderrPath
    ExpectedPierSide = $ExpectedPierSide
    CaptureCadenceMilliseconds = $CaptureCadenceMilliseconds
    FinalAzimuth = [double]$final.Azimuth; FinalAltitude = [double]$final.Altitude
    DifferentialAxisStabilityQualified = [bool]$axisEvaluation.DifferentialAxisStabilityQualified
    GrantsUpasAuthority = $false; GrantsAbsoluteAccuracyClaim = $false
}
$summary | ConvertTo-Json -Depth 4
if ($axisProcess.ExitCode -ne 0) { exit 2 }
