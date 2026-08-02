param(
    [switch]$LibraryOnly,
    [Guid]$RunId = [Guid]::Empty,
    [ValidateSet('pierEast', 'pierWest')]
    [string]$ExpectedPierSide,
    [ValidateRange(22.5, 40.0)]
    [double]$LegDistanceDegrees = 22.5,
    [switch]$Westward,
    [ValidateRange(100, 30000)]
    [int]$ExposureMilliseconds = 15000,
    [ValidateRange(0.1, 20.0)]
    [double]$AstapFieldOfViewDegrees = 2.0,
    [ValidateRange(0.0, 1000.0)]
    [double]$FitsTimestampUncertaintyMilliseconds = 250.0,
    [ValidateRange(100, 1000)]
    [int]$WatchdogCadenceMilliseconds = 150,
    [string]$Root = 'C:\Users\nnik0\Documents\TPPA-PHD2-tests\ra-witness',
    [string]$NinaApiBase = 'http://127.0.0.1:1888/v2/api',
    [string]$Phd2Host = '127.0.0.1',
    [ValidateRange(1, 65535)]
    [int]$Phd2Port = 4400,
    [string]$QualificationCliPath = 'C:\Tools\TPPAField\TppaQualificationCli.exe',
    [string]$ObserverScriptPath = 'C:\Tools\TPPAField\run_tppa_ra_witness_observer.ps1',
    [string]$ObserverServicePath = 'C:\Tools\TPPAField\run_tppa_ra_witness_observer_service.ps1',
    [string]$CaptureScriptPath = 'C:\Tools\TPPAField\capture_phd2_astap_witness_point.ps1',
    [string]$SlewScriptPath = 'C:\Tools\TPPAField\guarded_equatorial_witness_slew.ps1'
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

function Convert-NormalizedDegrees([double]$Value) {
    return (($Value % 360.0) + 360.0) % 360.0
}

function Test-NorthBalconyAzimuth([double]$Azimuth) {
    $value = Convert-NormalizedDegrees $Azimuth
    return $value -ge 270.0 -or $value -le 10.0
}

function Convert-EquatorialToHorizontal(
        [double]$RightAscensionDegrees,
        [double]$DeclinationDegrees,
        [double]$LatitudeDegrees,
        [double]$SiderealTimeHours) {
    $toRadians = [Math]::PI / 180.0
    $latitude = $LatitudeDegrees * $toRadians
    $declination = $DeclinationDegrees * $toRadians
    $hourAngle = ((Convert-NormalizedDegrees (
        $SiderealTimeHours * 15.0 - $RightAscensionDegrees + 180.0)) - 180.0) * $toRadians
    $sinAltitude = [Math]::Sin($declination) * [Math]::Sin($latitude) +
        [Math]::Cos($declination) * [Math]::Cos($latitude) * [Math]::Cos($hourAngle)
    $altitude = [Math]::Asin([Math]::Max(-1.0, [Math]::Min(1.0, $sinAltitude)))
    $cosAltitude = [Math]::Max(1e-12, [Math]::Cos($altitude))
    $sinAzimuth = -[Math]::Sin($hourAngle) * [Math]::Cos($declination) / $cosAltitude
    $cosAzimuth = ([Math]::Sin($declination) -
        [Math]::Sin($altitude) * [Math]::Sin($latitude)) /
        ($cosAltitude * [Math]::Cos($latitude))
    $azimuth = [Math]::Atan2($sinAzimuth, $cosAzimuth) / $toRadians
    return [pscustomobject]@{
        AzimuthDegrees = Convert-NormalizedDegrees $azimuth
        AltitudeDegrees = $altitude / $toRadians
    }
}

function Get-PredictedPierSide(
        [double]$RightAscensionDegrees,
        [double]$SiderealTimeHours) {
    $hourAngle = (Convert-NormalizedDegrees (
        $SiderealTimeHours * 15.0 - $RightAscensionDegrees + 180.0)) - 180.0
    if ([Math]::Abs($hourAngle) -lt 5.0) { return 'pierUnknown' }
    if ($hourAngle -lt 0.0) { return 'pierWest' }
    return 'pierEast'
}

function New-WitnessTrajectoryPreflight(
        $InitialMount,
        [double]$PointARightAscensionDegrees,
        [double]$DeclinationDegrees,
        [double]$LegDegrees,
        [bool]$EastDirection,
        [DateTime]$StartUtc) {
    if ($StartUtc.Kind -ne [DateTimeKind]::Utc) { throw 'Preflight start must be UTC.' }
    $sign = if ($EastDirection) { 1.0 } else { -1.0 }
    # The observer owns an immutable 60-second request window. Model the whole
    # window plus dispatch margin so sidereal drift cannot outrun the preflight.
    $captureSeconds = [Math]::Max(65.0, $ExposureMilliseconds / 1000.0 + 50.0)
    $moveSeconds = 45.0
    $segments = @(
        [pscustomobject]@{ Id='A-capture'; From=0.0; To=0.0; Start=0.0; End=$captureSeconds },
        [pscustomobject]@{ Id='A-B-slew'; From=0.0; To=$sign*$LegDegrees; Start=$captureSeconds; End=$captureSeconds+$moveSeconds },
        [pscustomobject]@{ Id='B-capture'; From=$sign*$LegDegrees; To=$sign*$LegDegrees; Start=$captureSeconds+$moveSeconds; End=2*$captureSeconds+$moveSeconds },
        [pscustomobject]@{ Id='B-C-slew'; From=$sign*$LegDegrees; To=$sign*2*$LegDegrees; Start=2*$captureSeconds+$moveSeconds; End=2*$captureSeconds+2*$moveSeconds },
        [pscustomobject]@{ Id='C-capture'; From=$sign*2*$LegDegrees; To=$sign*2*$LegDegrees; Start=2*$captureSeconds+2*$moveSeconds; End=3*$captureSeconds+2*$moveSeconds },
        [pscustomobject]@{ Id='C-A-return-slew'; From=$sign*2*$LegDegrees; To=0.0; Start=3*$captureSeconds+2*$moveSeconds; End=3*$captureSeconds+4*$moveSeconds },
        [pscustomobject]@{ Id='A-return-capture'; From=0.0; To=0.0; Start=3*$captureSeconds+4*$moveSeconds; End=4*$captureSeconds+4*$moveSeconds }
    )
    $samples = [Collections.Generic.List[object]]::new()
    $minimumAltitude = [double]::PositiveInfinity
    foreach ($segment in $segments) {
        $steps = [Math]::Max(1, [int][Math]::Ceiling([Math]::Abs($segment.To - $segment.From)))
        for ($step = 0; $step -le $steps; $step++) {
            $fraction = $step / [double]$steps
            $offset = $segment.From + ($segment.To - $segment.From) * $fraction
            $elapsed = $segment.Start + ($segment.End - $segment.Start) * $fraction
            $sidereal = [double]$InitialMount.SiderealTime + ($elapsed / 3600.0) * 1.00273790935
            $ra = Convert-NormalizedDegrees ($PointARightAscensionDegrees + $offset)
            $horizontal = Convert-EquatorialToHorizontal $ra $DeclinationDegrees (
                [double]$InitialMount.SiteLatitude) $sidereal
            $predictedPierSide = Get-PredictedPierSide $ra $sidereal
            $minimumAltitude = [Math]::Min($minimumAltitude, $horizontal.AltitudeDegrees)
            $safe = (Test-NorthBalconyAzimuth $horizontal.AzimuthDegrees) -and
                $horizontal.AltitudeDegrees -ge 40.0 -and $horizontal.AltitudeDegrees -le 55.0 -and
                $predictedPierSide -eq $ExpectedPierSide
            $samples.Add([pscustomobject]@{
                SegmentId = $segment.Id
                OffsetDegrees = $offset
                PredictedUtc = $StartUtc.AddSeconds($elapsed).ToString('O')
                RightAscensionDegrees = $ra
                DeclinationDegrees = $DeclinationDegrees
                AzimuthDegrees = $horizontal.AzimuthDegrees
                AltitudeDegrees = $horizontal.AltitudeDegrees
                PredictedPierSide = $predictedPierSide
                Safe = $safe
            })
        }
    }
    $totalArc = 2.0 * $LegDegrees
    $condition = 1.0 / (2.0 * [Math]::Pow(
        [Math]::Sin($totalArc * [Math]::PI / 360.0), 2.0))
    $issues = [Collections.Generic.List[string]]::new()
    if ($totalArc -lt 45.0) { $issues.Add('Total RA arc is below 45 degrees.') }
    if ($condition -gt 4.0) { $issues.Add('Design condition proxy exceeds 4.') }
    if (@($samples | Where-Object { -not $_.Safe }).Count -gt 0) {
        $issues.Add('At least one full-trajectory sample is outside north-AZ and ALT 40..55.')
    }
    return [pscustomobject]@{
        SchemaVersion = 1
        IsSafe = $issues.Count -eq 0
        TotalArcDegrees = $totalArc
        DesignConditionProxy = $condition
        MinimumPredictedAltitudeDegrees = $minimumAltitude
        MaximumSampleStepDegrees = 1.0
        CaptureStartSeconds = @(
            0.0,
            ($captureSeconds + $moveSeconds),
            (2.0*$captureSeconds + 2.0*$moveSeconds),
            (3.0*$captureSeconds + 4.0*$moveSeconds))
        ExpectedPierSide = $ExpectedPierSide
        Samples = @($samples)
        Issues = @($issues)
        GrantsUpasAuthority = $false
        GrantsCompletionAuthority = $false
    }
}

function Write-CreateNewUtf8([string]$Path, [string]$Text) {
    $parent = Split-Path -Parent ([IO.Path]::GetFullPath($Path))
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Text)
    $stream = [IO.FileStream]::new(
        $Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
}

function Get-MountInfo {
    $response = Invoke-RestMethod -Uri "$NinaApiBase/equipment/mount/info" -TimeoutSec 5
    if (-not [bool]$response.Success -or $null -eq $response.Response) {
        throw 'NINA mount telemetry is unavailable.'
    }
    return $response.Response
}

function Get-CircularDistance([double]$First, [double]$Second) {
    $delta = [Math]::Abs((Convert-NormalizedDegrees $First) -
        (Convert-NormalizedDegrees $Second))
    return [Math]::Min($delta, 360.0 - $delta)
}

function Test-MountInsideWitnessEnvelope($Mount, [bool]$RequireStationary) {
    return [bool]$Mount.Connected -and
        (-not $RequireStationary -or -not [bool]$Mount.Slewing) -and
        (Test-NorthBalconyAzimuth ([double]$Mount.Azimuth)) -and
        [double]$Mount.Altitude -ge 40.0 -and [double]$Mount.Altitude -le 55.0 -and
        [string]$Mount.SideOfPier -eq $ExpectedPierSide
}

function Write-MountWatchdogSample($Mount, [string]$CommandId,
        [string]$TrajectoryPath, [bool]$Safe) {
    [ordered]@{
        sampledUtc=[DateTime]::UtcNow.ToString('O')
        commandId=$CommandId
        slewing=[bool]$Mount.Slewing
        sideOfPier=[string]$Mount.SideOfPier
        azimuthDegrees=[double]$Mount.Azimuth
        altitudeDegrees=[double]$Mount.Altitude
        safe=$Safe
    } | ConvertTo-Json -Compress | Add-Content -LiteralPath $TrajectoryPath -Encoding utf8
}

function Stop-MountAndVerifyIdle([string]$CommandId, [string]$TrajectoryPath) {
    [void](Invoke-RestMethod -Uri "$NinaApiBase/equipment/mount/slew/stop" -TimeoutSec 5)
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $mount = Get-MountInfo
        Write-MountWatchdogSample $mount "$CommandId-stop" $TrajectoryPath (-not [bool]$mount.Slewing)
        if (-not [bool]$mount.Slewing) { return }
        Start-Sleep -Milliseconds $WatchdogCadenceMilliseconds
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Mount stop command did not reach a verified idle state within 10 seconds.'
}

function Stop-MountBestEffort([string]$CommandId = 'best-effort',
        [string]$TrajectoryPath = $null) {
    try {
        if ($TrajectoryPath) {
            Stop-MountAndVerifyIdle $CommandId $TrajectoryPath
        } else {
            [void](Invoke-RestMethod -Uri "$NinaApiBase/equipment/mount/slew/stop" -TimeoutSec 5)
        }
    } catch { }
}

function Assert-SkyModelMatchesMount($Mount) {
    $raDegrees = Convert-NormalizedDegrees ([double]$Mount.RightAscension * 15.0)
    $projected = Convert-EquatorialToHorizontal $raDegrees ([double]$Mount.Declination) (
        [double]$Mount.SiteLatitude) ([double]$Mount.SiderealTime)
    $azimuthError = Get-CircularDistance $projected.AzimuthDegrees ([double]$Mount.Azimuth)
    $altitudeError = [Math]::Abs($projected.AltitudeDegrees - [double]$Mount.Altitude)
    if ($azimuthError -gt 1.0 -or $altitudeError -gt 1.0) {
        throw ("Mount sky-model cross-check failed (az={0:F3} deg, alt={1:F3} deg). " +
            'Refuse witness motion because RA units, LST, site, or coordinate conventions may disagree.') -f (
            $azimuthError), $altitudeError
    }
}

function Connect-Phd2Control {
    $client = [Net.Sockets.TcpClient]::new()
    $client.Connect($Phd2Host, $Phd2Port)
    $stream = $client.GetStream()
    $stream.ReadTimeout = 1000
    $stream.WriteTimeout = 3000
    $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8)
    $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
    $writer.NewLine = "`r`n"
    $writer.AutoFlush = $true
    return [pscustomobject]@{ Client=$client; Reader=$reader; Writer=$writer; NextId=0 }
}

function Invoke-Phd2Control($Connection, [string]$Method,
        [object]$Parameters = $null, [int]$TimeoutSeconds = 10) {
    $Connection.NextId++
    $id = $Connection.NextId
    $message = [ordered]@{ method=$Method; id=$id }
    if ($null -ne $Parameters) { $message.params = $Parameters }
    $Connection.Writer.WriteLine(($message | ConvertTo-Json -Depth 5 -Compress))
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        try { $line = $Connection.Reader.ReadLine() } catch [IO.IOException] { continue }
        if (-not $line) { continue }
        try { $response = $line | ConvertFrom-Json } catch { continue }
        if ($response.PSObject.Properties.Name -contains 'id' -and [int]$response.id -eq $id) {
            if ($response.PSObject.Properties.Name -contains 'error') {
                throw "PHD2 $Method failed: $($response.error | ConvertTo-Json -Compress)"
            }
            return $response
        }
    }
    throw "Timed out waiting for PHD2 $Method response."
}

function Get-Phd2ControlSnapshot {
    $connection = Connect-Phd2Control
    try {
        return [pscustomobject]@{
            SampledUtc=[DateTime]::UtcNow.ToString('O')
            AppState=[string](Invoke-Phd2Control $connection 'get_app_state').result
            GuideOutputEnabled=[bool](Invoke-Phd2Control $connection 'get_guide_output_enabled').result
        }
    } finally {
        $connection.Client.Dispose()
    }
}

function Set-Phd2GuideOutputAndVerify([bool]$Enabled) {
    $connection = Connect-Phd2Control
    try {
        [void](Invoke-Phd2Control $connection 'set_guide_output_enabled' @($Enabled))
        $actual = [bool](Invoke-Phd2Control $connection 'get_guide_output_enabled').result
        if ($actual -ne $Enabled) { throw 'PHD2 guide-output reconciliation did not verify.' }
    } finally {
        $connection.Client.Dispose()
    }
}

function Wait-GuardedSlewProcess(
        $Process,
        [string]$CommandId,
        [string]$TrajectoryPath,
        [bool]$AllowNoTransition) {
    $sawSlewing = $false
    $idleSince = $null
    while (-not $Process.HasExited -or $null -eq $idleSince -or
            ([DateTime]::UtcNow - $idleSince).TotalSeconds -lt 2.0) {
        $mount = Get-MountInfo
        $safe = Test-MountInsideWitnessEnvelope $mount $false
        Write-MountWatchdogSample $mount $CommandId $TrajectoryPath $safe
        if (-not $safe) {
            Stop-MountAndVerifyIdle $CommandId $TrajectoryPath
            throw 'Live trajectory watchdog observed an unsafe mount state.'
        }
        if ([bool]$mount.Slewing) { $sawSlewing = $true; $idleSince = $null }
        elseif ($sawSlewing -and $null -eq $idleSince) { $idleSince = [DateTime]::UtcNow }
        elseif (-not $sawSlewing -and $Process.HasExited -and $AllowNoTransition) {
            $idleSince = [DateTime]::UtcNow.AddSeconds(-2)
        }
        if ($Process.HasExited -and -not $sawSlewing -and -not $AllowNoTransition) {
            throw 'Witness slew exited without an observed Slewing=true transition.'
        }
        Start-Sleep -Milliseconds $WatchdogCadenceMilliseconds
    }
}

function Wait-GuardedWitnessOutcome($Service, [string]$OutcomePath,
        [DateTime]$DeadlineUtc, [string]$CommandId, [string]$TrajectoryPath) {
    do {
        Start-Sleep -Milliseconds $WatchdogCadenceMilliseconds
        $mount = Get-MountInfo
        $safe = Test-MountInsideWitnessEnvelope $mount $true
        Write-MountWatchdogSample $mount "$CommandId-capture" $TrajectoryPath $safe
        if (-not $safe) {
            Stop-MountAndVerifyIdle $CommandId $TrajectoryPath
            throw 'Capture-period watchdog observed motion or an unsafe mount state.'
        }
        if ($Service.HasExited -and -not (Test-Path -LiteralPath $OutcomePath)) {
            throw 'Observer service exited before producing the requested outcome.'
        }
    } while (-not (Test-Path -LiteralPath $OutcomePath -PathType Leaf) -and
        [DateTime]::UtcNow -lt $DeadlineUtc.AddSeconds(2))
    if (-not (Test-Path -LiteralPath $OutcomePath -PathType Leaf)) {
        throw 'Witness outcome was not produced before the immutable deadline.'
    }
}

if ($LibraryOnly) { return }
if ($RunId -eq [Guid]::Empty) { throw 'RunId is required.' }
if ([string]::IsNullOrWhiteSpace($ExpectedPierSide)) { throw 'ExpectedPierSide is required.' }
foreach ($path in @($QualificationCliPath, $ObserverScriptPath, $ObserverServicePath,
        $CaptureScriptPath, $SlewScriptPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required campaign component is missing: $path" }
}

$initial = Get-MountInfo
if (-not [bool]$initial.Connected -or [bool]$initial.Slewing -or
        -not [bool]$initial.TrackingEnabled -or [bool]$initial.AtPark) {
    throw 'Campaign requires a connected, tracking, unparked, idle mount.'
}
if ([string]$initial.SideOfPier -ne $ExpectedPierSide) {
    throw "Initial pier side '$($initial.SideOfPier)' does not match '$ExpectedPierSide'."
}
if (-not (Test-NorthBalconyAzimuth ([double]$initial.Azimuth)) -or
        [double]$initial.Altitude -lt 40.0 -or [double]$initial.Altitude -gt 55.0) {
    throw 'Initial mount position is outside the qualified witness envelope.'
}
Assert-SkyModelMatchesMount $initial
$initialPhd2 = Get-Phd2ControlSnapshot
if ($initialPhd2.AppState -ne 'Stopped') {
    throw "PHD2 must be Stopped before the witness campaign; current state is '$($initialPhd2.AppState)'."
}

$pointARa = Convert-NormalizedDegrees ([double]$initial.RightAscension * 15.0)
$declination = [double]$initial.Declination
$eastDirection = -not $Westward.IsPresent
$runPath = Join-Path $Root $RunId.ToString('D')
$requestDirectory = Join-Path $runPath 'requests'
$outcomeDirectory = Join-Path $runPath 'outcomes'
$evidenceRoot = Join-Path $runPath 'evidence'
foreach ($directory in @($runPath, $requestDirectory, $outcomeDirectory, $evidenceRoot)) {
    [IO.Directory]::CreateDirectory($directory) | Out-Null
}
$preflight = New-WitnessTrajectoryPreflight $initial $pointARa $declination (
    $LegDistanceDegrees) $eastDirection ([DateTime]::UtcNow)
$preflightPath = Join-Path $runPath 'trajectory-preflight.json'
Write-CreateNewUtf8 $preflightPath ($preflight | ConvertTo-Json -Depth 8)
if (-not [bool]$preflight.IsSafe) {
    throw "Full A/B/C/A preflight failed: $($preflight.Issues -join '; ')"
}

$observerHash = (Get-FileHash -LiteralPath $ObserverScriptPath -Algorithm SHA256).Hash.ToLowerInvariant()
$captureHash = (Get-FileHash -LiteralPath $CaptureScriptPath -Algorithm SHA256).Hash.ToLowerInvariant()
$stopFile = Join-Path $runPath 'observer.stop'
$pwsh = (Get-Process -Id $PID).Path
$serviceArgs = @(
    '-NoProfile', '-File', $ObserverServicePath,
    '-RequestDirectory', $requestDirectory,
    '-OutcomeDirectory', $outcomeDirectory,
    '-EvidenceRoot', $evidenceRoot,
    '-ObserverScriptPath', $ObserverScriptPath,
    '-CaptureScriptPath', $CaptureScriptPath,
    '-QualificationCliPath', $QualificationCliPath,
    '-StopFilePath', $stopFile,
    '-MaximumRequests', 4,
    '-MaximumRuntimeMinutes', 15
)
$service = Start-Process -FilePath $pwsh -ArgumentList $serviceArgs -PassThru -WindowStyle Hidden
$trajectoryPath = Join-Path $runPath 'actual-trajectory.jsonl'
$positions = @('A','B','C','A')
$sign = if ($eastDirection) { 1.0 } else { -1.0 }
$offsets = @(0.0, $sign*$LegDistanceDegrees, $sign*2.0*$LegDistanceDegrees, 0.0)
$completedPoints = 0
$unsafeObserved = $false
$serviceUnresolved = $false
$finalPhd2 = $null
$guideOutputRestored = $false
$stateVerificationIssue = $null
try {
    for ($index = 0; $index -lt 4; $index++) {
        $position = $positions[$index]
        $targetRa = Convert-NormalizedDegrees ($pointARa + $offsets[$index])
        $commandId = "$($RunId.ToString('D'))-$index-$position"
        $commandReceiptPath = Join-Path $runPath "$commandId-command.json"
        $realizedMount = Get-MountInfo
        $stageOffsetSeconds = [double]$preflight.CaptureStartSeconds[$index]
        $realizedPlanMount = [pscustomobject]@{
            SiderealTime = [double]$realizedMount.SiderealTime -
                ($stageOffsetSeconds / 3600.0) * 1.00273790935
            SiteLatitude = [double]$realizedMount.SiteLatitude
        }
        $realizedPlan = New-WitnessTrajectoryPreflight $realizedPlanMount $pointARa (
            $declination) $LegDistanceDegrees $eastDirection (
            [DateTime]::UtcNow.AddSeconds(-$stageOffsetSeconds))
        $realizedPlanPath = Join-Path $runPath "$index-$position-realized-preflight.json"
        Write-CreateNewUtf8 $realizedPlanPath ($realizedPlan | ConvertTo-Json -Depth 8)
        if (-not [bool]$realizedPlan.IsSafe) {
            throw "Realized-time trajectory re-preflight failed before $position/$index."
        }
        $slewArgs = @(
            '-NoProfile', '-File', $SlewScriptPath,
            '-RightAscensionDegrees', $targetRa.ToString('R', [Globalization.CultureInfo]::InvariantCulture),
            '-DeclinationDegrees', $declination.ToString('R', [Globalization.CultureInfo]::InvariantCulture),
            '-ExpectedPierSide', $ExpectedPierSide,
            '-CommandId', $commandId,
            '-ReceiptPath', $commandReceiptPath,
            '-NinaApiBase', $NinaApiBase
        )
        $slew = Start-Process -FilePath $pwsh -ArgumentList $slewArgs -PassThru -WindowStyle Hidden
        try {
            Wait-GuardedSlewProcess $slew $commandId $trajectoryPath ($index -eq 0)
        } catch {
            if ($_.Exception.Message -like '*unsafe mount state*') { $unsafeObserved = $true }
            throw
        }
        $slew.WaitForExit()
        if ($slew.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $commandReceiptPath -PathType Leaf)) {
            throw "Witness mount command failed closed; exit=$($slew.ExitCode)."
        }
        $command = [IO.File]::ReadAllText($commandReceiptPath) | ConvertFrom-Json
        $requestedUtc = [DateTime]::UtcNow
        $deadlineUtc = $requestedUtc.AddSeconds(60)
        $prefix = "$($RunId.ToString('D'))-$index-$position"
        $specPath = Join-Path $runPath "$prefix-request-spec.json"
        $requestPath = Join-Path $requestDirectory "$prefix.witness-request.ready.json"
        $outcomePath = Join-Path $outcomeDirectory "$prefix-outcome.json"
        $spec = [ordered]@{
            runId=$RunId.ToString('D')
            positionId=$position
            sequenceIndex=$index
            nonce=[Guid]::NewGuid().ToString('D')
            mountCommandId=$commandId
            mountCommandIssuedUtc=[string]$command.issuedUtc
            mountCommandCompletedUtc=[string]$command.completedUtc
            requestedUtc=$requestedUtc.ToString('O')
            deadlineUtc=$deadlineUtc.ToString('O')
            commandedRightAscensionDegrees=$targetRa
            commandedDeclinationDegrees=$declination
            expectedPierSide=$ExpectedPierSide
            siteLatitudeDegrees=[double]$initial.SiteLatitude
            siteLongitudeDegrees=[double]$initial.SiteLongitude
            siteElevationMeters=[double]$initial.SiteElevation
            exposureMilliseconds=$ExposureMilliseconds
            astapFieldOfViewDegrees=$AstapFieldOfViewDegrees
            fitsTimestampUncertaintyMilliseconds=$FitsTimestampUncertaintyMilliseconds
            requiredObserverPipelineDigest=$observerHash
            requiredPointCapturePipelineDigest=$captureHash
        }
        Write-CreateNewUtf8 $specPath ($spec | ConvertTo-Json -Depth 5)
        $requestArguments = @(
            'create-witness-request',
            '--spec', $specPath,
            '--request-out', $requestPath
        )
        $requestResult = @(& $QualificationCliPath @requestArguments 2>&1)
        if ($LASTEXITCODE -ne 0) {
            throw "Qualification core refused the witness request: $($requestResult -join [Environment]::NewLine)"
        }
        Wait-GuardedWitnessOutcome $service $outcomePath $deadlineUtc $commandId $trajectoryPath
        $outcomeArguments = @(
            'validate-witness-outcome',
            '--request', $requestPath,
            '--outcome', $outcomePath
        )
        $outcomeValidation = @(& $QualificationCliPath @outcomeArguments 2>&1)
        if ($LASTEXITCODE -ne 0) {
            throw "Qualification core rejected the witness outcome: $($outcomeValidation -join [Environment]::NewLine)"
        }
        $completedPoints++
    }
} catch {
    Stop-MountBestEffort 'campaign-failure' $trajectoryPath
    throw
} finally {
    if (-not $unsafeObserved -and $completedPoints -gt 0 -and $completedPoints -lt 4) {
        try {
            $cleanupId = "$($RunId.ToString('D'))-cleanup-A"
            $cleanupReceipt = Join-Path $runPath "$cleanupId-command.json"
            $cleanupArgs = @(
                '-NoProfile', '-File', $SlewScriptPath,
                '-RightAscensionDegrees', $pointARa.ToString('R', [Globalization.CultureInfo]::InvariantCulture),
                '-DeclinationDegrees', $declination.ToString('R', [Globalization.CultureInfo]::InvariantCulture),
                '-ExpectedPierSide', $ExpectedPierSide,
                '-CommandId', $cleanupId,
                '-ReceiptPath', $cleanupReceipt,
                '-NinaApiBase', $NinaApiBase
            )
            $cleanup = Start-Process -FilePath $pwsh -ArgumentList $cleanupArgs -PassThru -WindowStyle Hidden
            Wait-GuardedSlewProcess $cleanup $cleanupId $trajectoryPath $false
            $cleanup.WaitForExit()
            if ($cleanup.ExitCode -ne 0 -or
                    -not (Test-Path -LiteralPath $cleanupReceipt -PathType Leaf)) {
                throw "Guarded return-to-A cleanup failed; exit=$($cleanup.ExitCode)."
            }
        } catch {
            [ordered]@{
                failedUtc=[DateTime]::UtcNow.ToString('O')
                issue=$_.Exception.Message
                grantsUpasAuthority=$false
                grantsCompletionAuthority=$false
            } | ConvertTo-Json -Compress | Set-Content -LiteralPath (
                Join-Path $runPath 'cleanup-failure.json') -Encoding utf8
        }
    }
    if (-not $service.HasExited) {
        if (-not (Test-Path -LiteralPath $stopFile)) {
            Write-CreateNewUtf8 $stopFile ([DateTime]::UtcNow.ToString('O'))
        }
        if (-not $service.WaitForExit(70000)) {
            $serviceUnresolved = $true
            [ordered]@{
                observedUtc=[DateTime]::UtcNow.ToString('O')
                issue='Observer service did not exit after stop request; it was left alive so capture cleanup and PHD2 restoration can finish.'
                serviceProcessId=$service.Id
                grantsUpasAuthority=$false
                grantsCompletionAuthority=$false
            } | ConvertTo-Json -Compress | Set-Content -LiteralPath (
                Join-Path $runPath 'observer-service-unresolved.json') -Encoding utf8
        }
    }
    if (-not $serviceUnresolved) {
        try {
            $finalPhd2 = Get-Phd2ControlSnapshot
            if ($finalPhd2.GuideOutputEnabled -ne $initialPhd2.GuideOutputEnabled) {
                Set-Phd2GuideOutputAndVerify ([bool]$initialPhd2.GuideOutputEnabled)
                $finalPhd2 = Get-Phd2ControlSnapshot
            }
            $guideOutputRestored = $finalPhd2.GuideOutputEnabled -eq (
                $initialPhd2.GuideOutputEnabled)
            if ($finalPhd2.AppState -ne $initialPhd2.AppState) {
                $stateVerificationIssue = "PHD2 app state changed from '$($initialPhd2.AppState)' to '$($finalPhd2.AppState)'."
            } elseif (-not $guideOutputRestored) {
                $stateVerificationIssue = 'PHD2 guide output did not match its initial state.'
            }
        } catch {
            $stateVerificationIssue = $_.Exception.Message
        }
    }
}

if ($serviceUnresolved) {
    throw 'Observer service remained active after its bounded stop wait; final PHD2 state cannot be reconciled safely.'
}
if ($stateVerificationIssue) {
    throw "Final PHD2 state verification failed: $stateVerificationIssue"
}

$summary = [ordered]@{
    schemaVersion=1
    runId=$RunId.ToString('D')
    status='captured-report-only'
    completedPoints=$completedPoints
    preflightPath=$preflightPath
    preflightSha256=(Get-FileHash -LiteralPath $preflightPath -Algorithm SHA256).Hash
    actualTrajectoryPath=$trajectoryPath
    initialPhd2AppState=$initialPhd2.AppState
    finalPhd2AppState=$finalPhd2.AppState
    initialGuideOutputEnabled=$initialPhd2.GuideOutputEnabled
    finalGuideOutputEnabled=$finalPhd2.GuideOutputEnabled
    guideOutputRestored=$guideOutputRestored
    grantsUpasAuthority=$false
    grantsCompletionAuthority=$false
    grantsAbsoluteAccuracyClaim=$false
}
$summaryPath = Join-Path $runPath 'campaign-summary.json'
Write-CreateNewUtf8 $summaryPath ($summary | ConvertTo-Json -Depth 5)
$summary | ConvertTo-Json -Depth 5
