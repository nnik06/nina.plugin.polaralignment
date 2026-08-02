param(
    [Parameter(Mandatory)]
    [ValidateSet(
        'WO-GT81-IV-0.8-OAG-L-ASI2600MM-gain100-bin1',
        'EdgeHD-9.25-0.7-OAG-L-ASI2600MM-gain100-bin1')]
    [string]$OpticalTrainId,
    [ValidateRange(600, 3600)]
    [int]$DurationSeconds = 1200,
    [ValidateRange(1, 10)]
    [int]$StateProbeCadenceSeconds = 5,
    [string]$NinaApiBase = 'http://127.0.0.1:1888/v2/api',
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
$statePath = Join-Path $runDirectory 'state.json'
$failurePath = Join-Path $runDirectory 'failure.json'
$script:RpcId = 8100
$script:CaptureActive = $false
$script:CaptureStartUtc = $null
$script:Stopwatch = [Diagnostics.Stopwatch]::StartNew()
$script:GuideStepCount = 0
$script:GuideStepWithPulseCount = 0
$script:GuideStepFrames = [System.Collections.Generic.List[long]]::new()
$script:GuideStepMonotonicSeconds = [System.Collections.Generic.List[double]]::new()
$script:MaximumObservedLockShiftPixels = 0.0
$script:InvalidatingEvents = [System.Collections.Generic.List[string]]::new()
$script:StateSamples = [System.Collections.Generic.List[object]]::new()
$script:RequiredStateKeys = @(
    'targetRaDegrees', 'targetDecDegrees', 'pierSide',
    'rotatorAngleDegrees', 'filter', 'gain', 'offset', 'binning',
    'readoutMode', 'focusPosition', 'coolerSetPointC', 'coolerOn',
    'trackingMode', 'trackingEnabled', 'mountConnected', 'cameraConnected',
    'filterWheelConnected', 'focuserConnected', 'rotatorConnected',
    'mountSlewing', 'filterWheelMoving', 'focuserMoving', 'focuserSettling',
    'rotatorMoving', 'phd2Profile', 'phd2ExposureMs',
    'phd2AlgorithmStateDigest')

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

function Get-Sha256Text([string]$Text) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
        ([BitConverter]::ToString($algorithm.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    } finally {
        $algorithm.Dispose()
    }
}

function Get-JsonValue($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    $property.Value
}

function Get-FirstValue($Object, [string[]]$Names) {
    foreach ($name in $Names) {
        $value = Get-JsonValue $Object $name
        if ($null -ne $value) { return $value }
    }
    $null
}

function Format-Number($Value, [string]$Format = 'G17') {
    ([double]$Value).ToString($Format, [Globalization.CultureInfo]::InvariantCulture)
}

function Invoke-NinaInfo([string]$Device) {
    $uri = "$NinaApiBase/equipment/$Device/info"
    $response = Invoke-RestMethod -Uri $uri -TimeoutSec 5
    if ((Get-JsonValue $response 'Success') -ne $true) {
        throw "NINA $Device info query failed: $($response | ConvertTo-Json -Compress -Depth 5)"
    }
    $info = Get-JsonValue $response 'Response'
    if ($null -eq $info) { throw "NINA $Device info response is empty." }
    $info
}

function Get-FilterName($FilterWheel) {
    $direct = Get-FirstValue $FilterWheel @('SelectedFilter', 'CurrentFilter', 'Filter')
    if ($null -ne $direct) {
        $name = Get-JsonValue $direct 'Name'
        if ($null -ne $name) { return [string]$name }
        if ($direct -is [string]) { return [string]$direct }
    }
    $position = Get-FirstValue $FilterWheel @('Position', 'SelectedPosition', 'CurrentPosition')
    if ($null -eq $position) { throw 'NINA filter-wheel position is unavailable.' }
    foreach ($candidate in @(Get-JsonValue $FilterWheel 'AvailableFilters')) {
        if ([int](Get-FirstValue $candidate @('Id', 'Position')) -eq [int]$position) {
            $name = Get-JsonValue $candidate 'Name'
            if (-not [string]::IsNullOrWhiteSpace([string]$name)) { return [string]$name }
        }
    }
    throw "NINA filter name is unavailable for position $position."
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

function Get-Median([double[]]$Values) {
    if ($null -eq $Values -or $Values.Count -eq 0) { return [double]::NaN }
    $sorted = @($Values | Sort-Object)
    $middle = [Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return [double]$sorted[$middle] }
    ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2.0
}

function Get-StateContinuity(
        [object[]]$Samples,
        [double]$CaptureStartSeconds,
        [double]$CaptureEndSeconds) {
    if ($Samples.Count -lt 10) {
        return [pscustomobject]@{ Qualified = $false; Reason = 'fewer than 10 state samples' }
    }
    $gaps = [System.Collections.Generic.List[double]]::new()
    for ($index = 1; $index -lt $Samples.Count; $index += 1) {
        $gap = [double]$Samples[$index].MonotonicSeconds -
            [double]$Samples[$index - 1].MonotonicSeconds
        if ($gap -le 0) {
            return [pscustomobject]@{ Qualified = $false; Reason = 'non-monotonic state samples' }
        }
        $gaps.Add($gap)
    }
    $median = Get-Median @($gaps)
    $maximum = ($gaps | Measure-Object -Maximum).Maximum
    $allowed = [Math]::Min(15.0, 3.0 * $median)
    $startGap = [double]$Samples[0].MonotonicSeconds - $CaptureStartSeconds
    $endGap = $CaptureEndSeconds - [double]$Samples[-1].MonotonicSeconds
    [pscustomobject]@{
        Qualified = $median -gt 0 -and $maximum -le $allowed -and
            $startGap -ge 0 -and $startGap -le $allowed -and
            $endGap -ge 0 -and $endGap -le $allowed
        MedianCadenceSeconds = [double]$median
        MaximumObservedGapSeconds = [double]$maximum
        MaximumAllowedGapSeconds = [double]$allowed
        StartBoundaryGapSeconds = [double]$startGap
        EndBoundaryGapSeconds = [double]$endGap
    }
}

function Get-AngularDelta([double]$Left, [double]$Right) {
    $delta = [Math]::Abs($Left - $Right) % 360.0
    [Math]::Min($delta, 360.0 - $delta)
}

function Test-StateEquivalent($Baseline, $Candidate) {
    foreach ($key in $script:RequiredStateKeys) {
        $left = [string]$Baseline[$key]
        $right = [string]$Candidate[$key]
        if ($key -eq 'targetRaDegrees') {
            if ((Get-AngularDelta ([double]$left) ([double]$right)) -gt (1.0 / 60.0)) { return $false }
        } elseif ($key -eq 'targetDecDegrees') {
            if ([Math]::Abs([double]$left - [double]$right) -gt (1.0 / 60.0)) { return $false }
        } elseif ($key -eq 'rotatorAngleDegrees') {
            if ((Get-AngularDelta ([double]$left) ([double]$right)) -gt 0.02) { return $false }
        } elseif ($key -eq 'coolerSetPointC') {
            if ([Math]::Abs([double]$left - [double]$right) -gt 0.1) { return $false }
        } elseif ($left -cne $right) {
            return $false
        }
    }
    $true
}

function Get-GuideStepContinuity(
        [long[]]$Frames,
        [double[]]$MonotonicSeconds,
        [double]$CaptureStartSeconds,
        [double]$CaptureEndSeconds) {
    $minimumSteps = 10
    $frameSequenceContiguous = $Frames.Count -eq $MonotonicSeconds.Count -and
        $Frames.Count -ge $minimumSteps
    $timeSequenceMonotonic = $frameSequenceContiguous
    $cadences = [System.Collections.Generic.List[double]]::new()
    if ($Frames.Count -eq $MonotonicSeconds.Count) {
        for ($index = 1; $index -lt $Frames.Count; $index += 1) {
            if ($Frames[$index] -ne $Frames[$index - 1] + 1) {
                $frameSequenceContiguous = $false
            }
            $gap = $MonotonicSeconds[$index] - $MonotonicSeconds[$index - 1]
            if ($gap -le 0.0) { $timeSequenceMonotonic = $false }
            $cadences.Add($gap)
        }
    }
    $medianCadence = Get-Median @($cadences)
    $maximumAllowedGap = if ([double]::IsNaN($medianCadence)) {
        0.0
    } else {
        3.0 * $medianCadence
    }
    $startGap = if ($MonotonicSeconds.Count -gt 0) {
        [double]$MonotonicSeconds[0] - $CaptureStartSeconds
    } else { [double]::PositiveInfinity }
    $endGap = if ($MonotonicSeconds.Count -gt 0) {
        $CaptureEndSeconds - [double]$MonotonicSeconds[-1]
    } else { [double]::PositiveInfinity }
    $allGaps = @($cadences) + @($startGap, $endGap)
    $maximumObservedGap = ($allGaps | Measure-Object -Maximum).Maximum
    $qualified = $frameSequenceContiguous -and $timeSequenceMonotonic -and
        $startGap -ge 0.0 -and $endGap -ge 0.0 -and
        $maximumAllowedGap -gt 0.0 -and
        $maximumObservedGap -le $maximumAllowedGap
    [pscustomobject]@{
        MinimumGuideSteps = $minimumSteps
        FrameSequenceContiguous = $frameSequenceContiguous
        TimeSequenceMonotonic = $timeSequenceMonotonic
        MedianCadenceSeconds = $medianCadence
        MaximumAllowedGapSeconds = $maximumAllowedGap
        MaximumObservedGapSeconds = [double]$maximumObservedGap
        StartBoundaryGapSeconds = $startGap
        EndBoundaryGapSeconds = $endGap
        Qualified = $qualified
    }
}

function Test-Phd2Disconnected($Connection) {
    try {
        $socket = $Connection.Client.Client
        $socket.Poll(0, [Net.Sockets.SelectMode]::SelectRead) -and
            $socket.Available -eq 0
    } catch {
        $true
    }
}

function Assert-LockPositionStable($Connection, [double[]]$InitialPosition) {
    $position = @(Invoke-Phd2ReadOnly $Connection 'get_lock_position')
    if ($position.Count -lt 2) { throw 'PHD2 lock position became unavailable.' }
    $deltaX = [double]$position[0] - $InitialPosition[0]
    $deltaY = [double]$position[1] - $InitialPosition[1]
    $shift = [Math]::Sqrt($deltaX * $deltaX + $deltaY * $deltaY)
    if ($shift -gt $script:MaximumObservedLockShiftPixels) {
        $script:MaximumObservedLockShiftPixels = $shift
    }
    if ($shift -gt 0.25) {
        throw "PHD2 lock position shifted by $shift px during guided evidence."
    }
    @([double]$position[0], [double]$position[1])
}

function Observe-Phd2Line([string]$Line) {
    if ([string]::IsNullOrWhiteSpace($Line)) { return $null }
    Append-Utf8 $eventsPath $Line
    $object = $null
    try { $object = $Line | ConvertFrom-Json } catch { return $null }
    if (-not $script:CaptureActive) { return $object }

    $eventName = [string](Get-JsonValue $object 'Event')
    if ($eventName -eq 'GuideStep') {
        $receivedSeconds = $script:Stopwatch.Elapsed.TotalSeconds
        $frame = 0L
        try {
            $frameValue = Get-JsonValue $object 'Frame'
            if ($null -eq $frameValue) { throw 'GuideStep Frame is missing.' }
            $frame = [long]$frameValue
            $script:GuideStepFrames.Add($frame)
            $script:GuideStepMonotonicSeconds.Add($receivedSeconds)
        } catch {
            Add-InvalidatingEvent 'MalformedGuideStepFrame'
        }
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
            $receivedSeconds.ToString('F3', [Globalization.CultureInfo]::InvariantCulture),
            $frame,
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
    } elseif (-not [string]::IsNullOrWhiteSpace($eventName)) {
        Add-InvalidatingEvent "UnknownEvent:$eventName"
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
        $Params = $null,
        [int]$TimeoutSeconds = 8) {
    if ($Method -notin @(
            'get_app_state', 'get_connected', 'get_calibrated',
            'get_guide_output_enabled', 'get_exposure',
            'get_lock_position', 'get_pixel_scale', 'get_profile',
            'get_current_equipment', 'get_algo_param_names',
            'get_algo_param')) {
        throw "PHD2 method is outside the guided evidence read-only allow-list: $Method"
    }
    $script:RpcId += 1
    $id = $script:RpcId
    $request = [ordered]@{ method = $Method; id = $id }
    if ($null -ne $Params) { $request.params = @($Params) }
    $Connection.Writer.WriteLine(($request | ConvertTo-Json -Compress -Depth 8))
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $line = Read-Phd2Line $Connection 1000
        if (-not $line) {
            if (Test-Phd2Disconnected $Connection) { throw 'PHD2 event-server socket disconnected.' }
            continue
        }
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

function Get-Phd2Configuration($Connection) {
    $profile = Invoke-Phd2ReadOnly $Connection 'get_profile'
    $equipment = Invoke-Phd2ReadOnly $Connection 'get_current_equipment'
    $axes = [ordered]@{}
    foreach ($axis in @('ra', 'dec')) {
        $parameters = [ordered]@{}
        $names = @(Invoke-Phd2ReadOnly $Connection 'get_algo_param_names' @($axis) |
            Sort-Object)
        if ($names.Count -eq 0) { throw "PHD2 $axis guide algorithm has no readable parameters." }
        foreach ($name in $names) {
            $parameters[[string]$name] = Invoke-Phd2ReadOnly `
                $Connection 'get_algo_param' @($axis, [string]$name)
        }
        $axes[$axis] = $parameters
    }
    $algorithmJson = $axes | ConvertTo-Json -Compress -Depth 12
    $exposureMilliseconds = [int](Invoke-Phd2ReadOnly $Connection 'get_exposure')
    $canonical = [ordered]@{
        Profile = $profile
        ExposureMilliseconds = $exposureMilliseconds
        CurrentEquipment = $equipment
        GuideAlgorithmParameters = $axes
    } | ConvertTo-Json -Compress -Depth 15
    [pscustomobject]@{
        ProfileId = [int](Get-JsonValue $profile 'id')
        ProfileName = [string](Get-JsonValue $profile 'name')
        ExposureMilliseconds = $exposureMilliseconds
        AlgorithmStateDigest = Get-Sha256Text $algorithmJson
        CanonicalJson = $canonical
        CurrentEquipment = $equipment
    }
}

function Get-FixedStateSample($Connection, $Phd2Configuration) {
    $mount = Invoke-NinaInfo 'mount'
    $camera = Invoke-NinaInfo 'camera'
    $filterWheel = Invoke-NinaInfo 'filterwheel'
    $focuser = Invoke-NinaInfo 'focuser'
    $rotator = Invoke-NinaInfo 'rotator'
    foreach ($device in @($mount, $camera, $filterWheel, $focuser, $rotator)) {
        if ((Get-JsonValue $device 'Connected') -ne $true) {
            throw 'NINA fixed-state witness requires every optical-train device to be connected.'
        }
    }
    if ((Get-JsonValue $mount 'Slewing') -eq $true -or
            (Get-JsonValue $mount 'TrackingEnabled') -ne $true -or
            [string](Get-JsonValue $mount 'TrackingMode') -ne 'Sidereal' -or
            (Get-JsonValue $filterWheel 'IsMoving') -eq $true -or
            (Get-JsonValue $focuser 'IsMoving') -eq $true -or
            (Get-JsonValue $focuser 'IsSettling') -eq $true -or
            (Get-JsonValue $rotator 'IsMoving') -eq $true) {
        throw 'NINA fixed-state witness observed slew, non-sidereal tracking, or device motion.'
    }
    $profile = Invoke-Phd2ReadOnly $Connection 'get_profile'
    $phd2Exposure = [int](Invoke-Phd2ReadOnly $Connection 'get_exposure')
    if ([int](Get-JsonValue $profile 'id') -ne $Phd2Configuration.ProfileId -or
            [string](Get-JsonValue $profile 'name') -cne $Phd2Configuration.ProfileName -or
            $phd2Exposure -ne $Phd2Configuration.ExposureMilliseconds) {
        throw 'PHD2 profile or exposure changed during fixed-state evidence.'
    }
    $coordinates = Get-JsonValue $mount 'Coordinates'
    $state = [ordered]@{
        targetRaDegrees = Format-Number (Get-JsonValue $coordinates 'RADegrees') 'F6'
        targetDecDegrees = Format-Number (Get-JsonValue $mount 'Declination') 'F6'
        pierSide = [string](Get-JsonValue $mount 'SideOfPier')
        rotatorAngleDegrees = Format-Number (Get-JsonValue $rotator 'Position') 'F6'
        filter = Get-FilterName $filterWheel
        gain = ([int](Get-JsonValue $camera 'Gain')).ToString([Globalization.CultureInfo]::InvariantCulture)
        offset = ([int](Get-JsonValue $camera 'Offset')).ToString([Globalization.CultureInfo]::InvariantCulture)
        binning = "$(Get-JsonValue $camera 'BinX')x$(Get-JsonValue $camera 'BinY')"
        readoutMode = [string](Get-JsonValue $camera 'ReadoutMode')
        focusPosition = ([int](Get-JsonValue $focuser 'Position')).ToString([Globalization.CultureInfo]::InvariantCulture)
        coolerSetPointC = Format-Number (Get-FirstValue $camera @('TemperatureSetPoint', 'TargetTemp')) 'F3'
        coolerOn = ([bool](Get-JsonValue $camera 'CoolerOn')).ToString().ToLowerInvariant()
        trackingMode = [string](Get-JsonValue $mount 'TrackingMode')
        trackingEnabled = ([bool](Get-JsonValue $mount 'TrackingEnabled')).ToString().ToLowerInvariant()
        mountConnected = ([bool](Get-JsonValue $mount 'Connected')).ToString().ToLowerInvariant()
        cameraConnected = ([bool](Get-JsonValue $camera 'Connected')).ToString().ToLowerInvariant()
        filterWheelConnected = ([bool](Get-JsonValue $filterWheel 'Connected')).ToString().ToLowerInvariant()
        focuserConnected = ([bool](Get-JsonValue $focuser 'Connected')).ToString().ToLowerInvariant()
        rotatorConnected = ([bool](Get-JsonValue $rotator 'Connected')).ToString().ToLowerInvariant()
        mountSlewing = ([bool](Get-JsonValue $mount 'Slewing')).ToString().ToLowerInvariant()
        filterWheelMoving = ([bool](Get-JsonValue $filterWheel 'IsMoving')).ToString().ToLowerInvariant()
        focuserMoving = ([bool](Get-JsonValue $focuser 'IsMoving')).ToString().ToLowerInvariant()
        focuserSettling = ([bool](Get-JsonValue $focuser 'IsSettling')).ToString().ToLowerInvariant()
        rotatorMoving = ([bool](Get-JsonValue $rotator 'IsMoving')).ToString().ToLowerInvariant()
        phd2Profile = $Phd2Configuration.ProfileName
        phd2ExposureMs = $phd2Exposure.ToString([Globalization.CultureInfo]::InvariantCulture)
        phd2AlgorithmStateDigest = $Phd2Configuration.AlgorithmStateDigest
    }
    foreach ($key in $script:RequiredStateKeys) {
        if ([string]::IsNullOrWhiteSpace([string]$state[$key])) {
            throw "Fixed-state sample is missing $key."
        }
    }
    [pscustomobject]@{
        TimestampUtc = [DateTime]::UtcNow.ToString('o')
        MonotonicSeconds = $script:Stopwatch.Elapsed.TotalSeconds
        State = $state
    }
}

function Add-FixedStateSample($Connection, $Phd2Configuration) {
    $sample = Get-FixedStateSample $Connection $Phd2Configuration
    if ($script:StateSamples.Count -gt 0 -and
            -not (Test-StateEquivalent $script:StateSamples[0].State $sample.State)) {
        throw 'A fixed-state sample changed beyond its allowed telemetry tolerance.'
    }
    $script:StateSamples.Add($sample)
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
    $phd2Configuration = Get-Phd2Configuration $connection
    $exposureMilliseconds = $phd2Configuration.ExposureMilliseconds
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
        ProfileId = $phd2Configuration.ProfileId
        ProfileName = $phd2Configuration.ProfileName
        GuideAlgorithmStateDigest = $phd2Configuration.AlgorithmStateDigest
        CurrentEquipment = $phd2Configuration.CurrentEquipment
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
    Add-FixedStateSample $connection $phd2Configuration
    $endUtc = $script:CaptureStartUtc.AddSeconds($DurationSeconds)
    $nextProbeUtc = [DateTime]::UtcNow.AddSeconds($StateProbeCadenceSeconds)
    while ([DateTime]::UtcNow -lt $endUtc) {
        $line = Read-Phd2Line $connection 1000
        if ($line) {
            [void](Observe-Phd2Line $line)
        } elseif (Test-Phd2Disconnected $connection) {
            throw 'PHD2 event-server socket disconnected during guided evidence.'
        }
        if ($script:InvalidatingEvents.Count -gt 0) {
            throw "PHD2 guiding continuity failed: $($script:InvalidatingEvents -join ', ')."
        }
        if ([DateTime]::UtcNow -ge $nextProbeUtc) {
            [void](Assert-GuidedState $connection)
            [void](Assert-LockPositionStable $connection @($initial.LockPosition))
            Add-FixedStateSample $connection $phd2Configuration
            $nextProbeUtc = [DateTime]::UtcNow.AddSeconds(
                $StateProbeCadenceSeconds)
        }
    }
    $finalState = Assert-GuidedState $connection
    $finalLockPosition = @(
        Assert-LockPositionStable $connection @($initial.LockPosition))
    $finalConfiguration = Get-Phd2Configuration $connection
    if ($finalConfiguration.CanonicalJson -cne $phd2Configuration.CanonicalJson) {
        throw 'PHD2 profile, equipment, exposure, or guide algorithms changed during evidence.'
    }
    Add-FixedStateSample $connection $phd2Configuration
    $captureEndSeconds = $script:Stopwatch.Elapsed.TotalSeconds
    $captureCompletedUtc = [DateTime]::UtcNow
    $script:CaptureActive = $false
    $final = [ordered]@{
        State = $finalState.State
        GuideOutputEnabled = $finalState.GuideOutputEnabled
        LockPosition = @(
            [double]$finalLockPosition[0], [double]$finalLockPosition[1])
    }
    $continuity = Get-GuideStepContinuity `
        @($script:GuideStepFrames) `
        @($script:GuideStepMonotonicSeconds) `
        $captureStartSeconds `
        $captureEndSeconds
    if (-not $continuity.Qualified) {
        throw "PHD2 GuideStep stream is not contiguous: $($continuity | ConvertTo-Json -Compress)."
    }
    $stateContinuity = Get-StateContinuity `
        @($script:StateSamples) `
        $captureStartSeconds `
        $captureEndSeconds
    if (-not $stateContinuity.Qualified) {
        throw "Fixed-state sample stream is not contiguous: $($stateContinuity | ConvertTo-Json -Compress)."
    }
    $stateReceipt = [ordered]@{
        SchemaVersion = 2
        EvidenceMode = 'ReadOnlySampledStateContinuityWitness'
        OpticalTrainId = $OpticalTrainId
        CaptureStartUtc = $script:CaptureStartUtc.ToString('o')
        CaptureCompletedUtc = $captureCompletedUtc.ToString('o')
        BaselineState = $script:StateSamples[0].State
        Samples = @($script:StateSamples)
        SampleCount = $script:StateSamples.Count
        ObservedMedianSampleCadenceSeconds = $stateContinuity.MedianCadenceSeconds
        MaximumObservedSampleGapSeconds = $stateContinuity.MaximumObservedGapSeconds
        MaximumAllowedSampleGapSeconds = $stateContinuity.MaximumAllowedGapSeconds
        StateContinuityQualified = $true
        ReadOnlyNinaEndpoints = @(
            'equipment/mount/info', 'equipment/camera/info',
            'equipment/filterwheel/info', 'equipment/focuser/info',
            'equipment/rotator/info')
        StateChangingNinaEndpoints = @()
        GrantsMountMotionAuthority = $false
        GrantsUpasAuthority = $false
        GrantsAbsoluteAccuracyClaim = $false
    }
    Write-CreateNewUtf8 $statePath ($stateReceipt | ConvertTo-Json -Depth 15)

    $summary = [ordered]@{
        SchemaVersion = 1
        EvidenceMode = 'GuidedTrackingRollWitness'
        RunId = $runId
        CaptureStartUtc = $script:CaptureStartUtc.ToString('o')
        CaptureCompletedUtc = $captureCompletedUtc.ToString('o')
        RequestedDurationSeconds = $DurationSeconds
        Phd2Profile = $phd2Configuration.ProfileName
        Phd2ExposureMilliseconds = $phd2Configuration.ExposureMilliseconds
        Phd2AlgorithmStateDigest = $phd2Configuration.AlgorithmStateDigest
        PHD2ConfigurationContinuityQualified = $true
        Phd2EventContractSource =
            'OpenPHDGuiding/phd2@4a13cf245d7e485e79533697f87b032b304df952: src/event_server.cpp'
        InitialPHD2 = $initial
        FinalPHD2 = $final
        GuideStepCount = $script:GuideStepCount
        GuideStepWithPulseCount = $script:GuideStepWithPulseCount
        GuideStepCoverageMethod = 'contiguous-frame-sequence-and-observed-cadence'
        GuideStepCoverageQualified = $continuity.Qualified
        GuideStepFrameSequenceContiguous = $continuity.FrameSequenceContiguous
        GuideStepTimeSequenceMonotonic = $continuity.TimeSequenceMonotonic
        ObservedMedianGuideStepCadenceSeconds = $continuity.MedianCadenceSeconds
        MaximumObservedGuideStepGapSeconds = $continuity.MaximumObservedGapSeconds
        MaximumAllowedGuideStepGapSeconds = $continuity.MaximumAllowedGapSeconds
        GuidingContinuityQualified = $true
        GuideOutputContinuouslyEnabled = $true
        MaximumObservedLockPositionShiftPixels =
            $script:MaximumObservedLockShiftPixels
        InvalidatingEvents = @($script:InvalidatingEvents)
        ReadOnlyRpcMethods = @(
            'get_app_state', 'get_connected', 'get_calibrated',
            'get_guide_output_enabled', 'get_exposure',
            'get_lock_position', 'get_pixel_scale', 'get_profile',
            'get_current_equipment', 'get_algo_param_names',
            'get_algo_param')
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
