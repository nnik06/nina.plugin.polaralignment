param(
    [ValidateRange(600, 3600)]
    [int]$DurationSeconds = 1200,
    [ValidateRange(10, 120)]
    [int]$StateProbeCadenceSeconds = 30,
    [string]$Phd2Host = '127.0.0.1',
    [ValidateRange(1, 65535)]
    [int]$Phd2Port = 4400,
    [string]$OutputRoot = 'C:\Users\nnik0\Documents\TPPA-PHD2-tests'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$runId = 'guided-phd2-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
$runDirectory = Join-Path $OutputRoot $runId
if (Test-Path -LiteralPath $runDirectory) {
    throw "Guided PHD2 evidence directory already exists: $runDirectory"
}
[IO.Directory]::CreateDirectory($runDirectory) | Out-Null
$eventsPath = Join-Path $runDirectory 'events.jsonl'
$stepsPath = Join-Path $runDirectory 'guidesteps.csv'
$summaryPath = Join-Path $runDirectory 'summary.json'
$failurePath = Join-Path $runDirectory 'failure.json'
$script:RpcId = 8100
$script:CaptureActive = $false
$script:CaptureStartUtc = $null
$script:Stopwatch = [Diagnostics.Stopwatch]::StartNew()
$script:GuideStepCount = 0
$script:GuideStepWithPulseCount = 0
$script:InvalidatingEvents = [System.Collections.Generic.List[string]]::new()

function Write-CreateNewUtf8([string]$Path, [string]$Text) {
    $stream = [IO.File]::Open(
        $Path,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write,
        [IO.FileShare]::Read)
    try {
        $writer = [IO.StreamWriter]::new(
            $stream,
            [Text.UTF8Encoding]::new($false))
        try { $writer.Write($Text) } finally { $writer.Dispose() }
    } finally { $stream.Dispose() }
}

function Append-Utf8([string]$Path, [string]$Text) {
    [IO.File]::AppendAllText(
        $Path,
        $Text + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
}

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-JsonValue($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    $property.Value
}

function ConvertTo-CsvCell($Value) {
    if ($null -eq $Value) { return '' }
    $text = [string]$Value
    if ($text.Contains('"') -or $text.Contains(',') -or
            $text.Contains("`n") -or $text.Contains("`r")) {
        return '"' + $text.Replace('"', '""') + '"'
    }
    $text
}

function Add-InvalidatingEvent([string]$Reason) {
    if (-not $script:InvalidatingEvents.Contains($Reason)) {
        $script:InvalidatingEvents.Add($Reason)
    }
}

function Observe-Phd2Line([string]$Line) {
    if ([string]::IsNullOrWhiteSpace($Line)) { return $null }
    Append-Utf8 $eventsPath $Line
    $object = $null
    try { $object = $Line | ConvertFrom-Json } catch { return $null }
    if (-not $script:CaptureActive) { return $object }

    $eventName = [string](Get-JsonValue $object 'Event')
    if ($eventName -eq 'GuideStep') {
        $script:GuideStepCount += 1
        $raDuration = Get-JsonValue $object 'RADuration'
        $decDuration = Get-JsonValue $object 'DECDuration'
        $hasPulse = $false
        try {
            $hasPulse = ($null -ne $raDuration -and [double]$raDuration -gt 0.0) -or
                ($null -ne $decDuration -and [double]$decDuration -gt 0.0)
        } catch {
            Add-InvalidatingEvent 'MalformedGuidePulse'
        }
        if ($hasPulse) { $script:GuideStepWithPulseCount += 1 }
        $cells = @(
            [DateTime]::UtcNow.ToString('o'),
            $script:Stopwatch.Elapsed.TotalSeconds.ToString(
                'F3', [Globalization.CultureInfo]::InvariantCulture),
            (Get-JsonValue $object 'Frame'),
            (Get-JsonValue $object 'dx'),
            (Get-JsonValue $object 'dy'),
            (Get-JsonValue $object 'RADistanceRaw'),
            (Get-JsonValue $object 'DECDistanceRaw'),
            (Get-JsonValue $object 'RADistanceGuide'),
            (Get-JsonValue $object 'DECDistanceGuide'),
            $raDuration,
            $decDuration,
            (Get-JsonValue $object 'SNR'),
            (Get-JsonValue $object 'HFD'),
            (Get-JsonValue $object 'StarMass'),
            $Line)
        Append-Utf8 $stepsPath (($cells | ForEach-Object {
            ConvertTo-CsvCell $_
        }) -join ',')
    } elseif ($eventName -eq 'AppState') {
        $state = [string](Get-JsonValue $object 'State')
        if ($state -ne 'Guiding') { Add-InvalidatingEvent "AppState:$state" }
    } elseif (@(
            'StarLost', 'StarSelected', 'GuidingDithered', 'LockPositionSet',
            'LockPositionShiftLimitReached', 'LockPositionLost',
            'GuidingStopped', 'StartGuiding', 'StartCalibration',
            'Calibrating', 'CalibrationComplete', 'CalibrationFailed',
            'CalibrationDataFlipped', 'SettleBegin', 'Settling',
            'SettleDone', 'Paused', 'Resumed', 'LoopingExposures',
            'LoopingExposuresStopped', 'SingleFrameComplete',
            'GuideParamChange', 'ConfigurationChange', 'Alert') -contains
            $eventName) {
        Add-InvalidatingEvent $eventName
    }
    $object
}

function Connect-Phd2 {
    $client = [Net.Sockets.TcpClient]::new()
    $client.Connect($Phd2Host, $Phd2Port)
    $stream = $client.GetStream()
    $stream.ReadTimeout = 1000
    $stream.WriteTimeout = 3000
    $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8)
    $writer = [IO.StreamWriter]::new(
        $stream, [Text.UTF8Encoding]::new($false))
    $writer.NewLine = "`r`n"
    $writer.AutoFlush = $true
    [pscustomobject]@{
        Client = $client
        Reader = $reader
        Writer = $writer
    }
}

function Read-Phd2Line($Connection, [int]$TimeoutMilliseconds = 1000) {
    $Connection.Client.GetStream().ReadTimeout = $TimeoutMilliseconds
    try { $Connection.Reader.ReadLine() } catch { $null }
}

function Invoke-Phd2ReadOnly(
        $Connection,
        [string]$Method,
        [int]$TimeoutSeconds = 8) {
    if ($Method -notin @(
            'get_app_state', 'get_connected', 'get_calibrated',
            'get_guide_output_enabled', 'get_exposure',
            'get_lock_position', 'get_pixel_scale')) {
        throw "PHD2 method is outside the guided evidence read-only allow-list: $Method"
    }
    $script:RpcId += 1
    $id = $script:RpcId
    $Connection.Writer.WriteLine((
        [ordered]@{ method = $Method; id = $id } |
            ConvertTo-Json -Compress))
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $line = Read-Phd2Line $Connection 1000
        if (-not $line) { continue }
        $object = Observe-Phd2Line $line
        if ($null -ne $object -and
                $object.PSObject.Properties.Name -contains 'id' -and
                [int]$object.id -eq $id) {
            if ($object.PSObject.Properties.Name -contains 'error') {
                throw "PHD2 $Method failed: $($object.error | ConvertTo-Json -Compress)"
            }
            return $object.result
        }
    }
    throw "Timed out waiting for PHD2 $Method response."
}

function Assert-GuidedState($Connection) {
    $state = [string](Invoke-Phd2ReadOnly $Connection 'get_app_state')
    $guideOutput = [bool](
        Invoke-Phd2ReadOnly $Connection 'get_guide_output_enabled')
    if ($state -ne 'Guiding' -or -not $guideOutput) {
        throw "PHD2 guided evidence gate failed: state=$state guideOutput=$guideOutput."
    }
    [pscustomobject]@{ State = $state; GuideOutputEnabled = $guideOutput }
}

Write-CreateNewUtf8 $stepsPath (
    'timestamp_utc,monotonic_s,frame,camera_dx_px,camera_dy_px,' +
    'ra_raw_px,dec_raw_px,ra_guide_px,dec_guide_px,ra_ms,dec_ms,' +
    'snr,hfd,star_mass,event_json' + [Environment]::NewLine)

$connection = $null
$initial = $null
$final = $null
$completed = $false
try {
    $connection = Connect-Phd2
    $drainDeadline = [DateTime]::UtcNow.AddSeconds(2)
    while ([DateTime]::UtcNow -lt $drainDeadline) {
        $line = Read-Phd2Line $connection 250
        if ($line) { [void](Observe-Phd2Line $line) }
    }

    $initialState = Assert-GuidedState $connection
    $connected = [bool](Invoke-Phd2ReadOnly $connection 'get_connected')
    $calibrated = [bool](Invoke-Phd2ReadOnly $connection 'get_calibrated')
    $exposureMilliseconds = [int](
        Invoke-Phd2ReadOnly $connection 'get_exposure')
    $pixelScale = [double](
        Invoke-Phd2ReadOnly $connection 'get_pixel_scale')
    $lockPosition = @(Invoke-Phd2ReadOnly $connection 'get_lock_position')
    if (-not $connected -or -not $calibrated -or
            $exposureMilliseconds -le 0 -or $pixelScale -le 0.0 -or
            $lockPosition.Count -lt 2) {
        throw 'PHD2 guided evidence preflight is incomplete.'
    }
    $initial = [ordered]@{
        State = $initialState.State
        GuideOutputEnabled = $initialState.GuideOutputEnabled
        Connected = $connected
        Calibrated = $calibrated
        ExposureMilliseconds = $exposureMilliseconds
        PixelScaleArcsecPerPixel = $pixelScale
        LockPosition = @([double]$lockPosition[0], [double]$lockPosition[1])
    }

    $script:CaptureStartUtc = [DateTime]::UtcNow
    $captureStartSeconds = $script:Stopwatch.Elapsed.TotalSeconds
    Append-Utf8 $eventsPath (([ordered]@{
        Event = 'GuidedEvidenceMark'
        RunId = $runId
        CaptureStartUtc = $script:CaptureStartUtc.ToString('o')
        MonotonicStartSeconds = $captureStartSeconds
    } | ConvertTo-Json -Compress))
    $script:CaptureActive = $true
    $endUtc = $script:CaptureStartUtc.AddSeconds($DurationSeconds)
    $nextProbeUtc = [DateTime]::UtcNow.AddSeconds($StateProbeCadenceSeconds)
    while ([DateTime]::UtcNow -lt $endUtc) {
        $line = Read-Phd2Line $connection 1000
        if ($line) { [void](Observe-Phd2Line $line) }
        if ($script:InvalidatingEvents.Count -gt 0) {
            throw "PHD2 guiding continuity failed: $($script:InvalidatingEvents -join ', ')."
        }
        if ([DateTime]::UtcNow -ge $nextProbeUtc) {
            [void](Assert-GuidedState $connection)
            $nextProbeUtc = [DateTime]::UtcNow.AddSeconds(
                $StateProbeCadenceSeconds)
        }
    }
    $finalState = Assert-GuidedState $connection
    $script:CaptureActive = $false
    $finalLockPosition = @(
        Invoke-Phd2ReadOnly $connection 'get_lock_position')
    $final = [ordered]@{
        State = $finalState.State
        GuideOutputEnabled = $finalState.GuideOutputEnabled
        LockPosition = @(
            [double]$finalLockPosition[0], [double]$finalLockPosition[1])
    }
    $lockDeltaX =
        [double]$final.LockPosition[0] - [double]$initial.LockPosition[0]
    $lockDeltaY =
        [double]$final.LockPosition[1] - [double]$initial.LockPosition[1]
    $lockShift = [Math]::Sqrt(
        $lockDeltaX * $lockDeltaX + $lockDeltaY * $lockDeltaY)
    if ($lockShift -gt 0.25) {
        throw "PHD2 lock position shifted by $lockShift px during guided evidence."
    }
    $expectedSteps = [Math]::Floor(
        $DurationSeconds * 1000.0 / $initial.ExposureMilliseconds)
    $coverageQualified = $script:GuideStepCount -ge
        [Math]::Floor($expectedSteps * 0.60)
    if (-not $coverageQualified) {
        throw "PHD2 GuideStep coverage is incomplete: $($script:GuideStepCount)/$expectedSteps."
    }

    $summary = [ordered]@{
        SchemaVersion = 1
        EvidenceMode = 'GuidedTrackingRollWitness'
        RunId = $runId
        CaptureStartUtc = $script:CaptureStartUtc.ToString('o')
        CaptureCompletedUtc = [DateTime]::UtcNow.ToString('o')
        RequestedDurationSeconds = $DurationSeconds
        Phd2EventContractSource =
            'OpenPHDGuiding/phd2@4a13cf245d7e485e79533697f87b032b304df952: src/event_server.cpp'
        InitialPHD2 = $initial
        FinalPHD2 = $final
        GuideStepCount = $script:GuideStepCount
        GuideStepWithPulseCount = $script:GuideStepWithPulseCount
        ExpectedGuideSteps = $expectedSteps
        GuideStepCoverageQualified = $coverageQualified
        GuidingContinuityQualified = $true
        GuideOutputContinuouslyEnabled = $true
        LockPositionShiftPixels = $lockShift
        InvalidatingEvents = @($script:InvalidatingEvents)
        ReadOnlyRpcMethods = @(
            'get_app_state', 'get_connected', 'get_calibrated',
            'get_guide_output_enabled', 'get_exposure',
            'get_lock_position', 'get_pixel_scale')
        StateChangingRpcMethods = @()
        EventsSha256 = Get-Sha256 $eventsPath
        GuideStepsSha256 = Get-Sha256 $stepsPath
        GrantsMountMotionAuthority = $false
        GrantsUpasAuthority = $false
        GrantsAbsoluteAccuracyClaim = $false
    }
    Write-CreateNewUtf8 $summaryPath ($summary | ConvertTo-Json -Depth 10)
    $completed = $true
    Write-Host "Guided PHD2 evidence complete: $runDirectory"
} catch {
    $script:CaptureActive = $false
    $failure = [ordered]@{
        SchemaVersion = 1
        EvidenceMode = 'GuidedTrackingRollWitness'
        RunId = $runId
        FailedUtc = [DateTime]::UtcNow.ToString('o')
        Error = $_.Exception.Message
        InitialPHD2 = $initial
        FinalPHD2 = $final
        GuideStepCount = $script:GuideStepCount
        InvalidatingEvents = @($script:InvalidatingEvents)
        StateChangingRpcMethods = @()
        GrantsMountMotionAuthority = $false
        GrantsUpasAuthority = $false
        GrantsAbsoluteAccuracyClaim = $false
    }
    if (-not (Test-Path -LiteralPath $failurePath)) {
        Write-CreateNewUtf8 $failurePath ($failure | ConvertTo-Json -Depth 8)
    }
    throw
} finally {
    if ($null -ne $connection) {
        try { $connection.Client.Close() } catch { }
    }
    if (-not $completed -and -not (Test-Path -LiteralPath $failurePath)) {
        Write-CreateNewUtf8 $failurePath (([ordered]@{
            SchemaVersion = 1
            RunId = $runId
            FailedUtc = [DateTime]::UtcNow.ToString('o')
            Error = 'Guided evidence ended without a terminal receipt.'
            StateChangingRpcMethods = @()
        } | ConvertTo-Json -Depth 4))
    }
}
