#requires -Version 7.0
param(
    [string]$BaseUrl = 'http://127.0.0.1:1888/v2/api',
    [Parameter(Mandatory = $true)]
    [string]$SequencePath,
    [Parameter(Mandatory = $true)]
    [string]$PluginDirectory,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string]$ExpectedRuntimeManifestSha256,
    [ValidateRange(270.0, 359.9)]
    [double]$WesternAzimuthMinimumDegrees = 270.0,
    [ValidateRange(0.0, 20.0)]
    [double]$EasternAzimuthMaximumDegrees = 10.0,
    [ValidateRange(0.0, 90.0)]
    [double]$MinimumAltitudeDegrees = 25.0,
    [ValidateRange(0.0, 90.0)]
    [double]$MaximumAltitudeDegrees = 55.0,
    [ValidateRange(0.0, 10.0)]
    [double]$TargetAltitudeMarginDegrees = 2.0,
    [ValidateRange(60, 1800)]
    [int]$MaximumRuntimeSeconds = 1500,
    [ValidateRange(30.0, 300.0)]
    [double]$MinimumRuntimePerPointSeconds = 120.0,
    [ValidateRange(30, 600)]
    [int]$CleanupReserveSeconds = 180,
    [ValidateRange(5, 120)]
    [int]$CancellationTerminalityTimeoutSeconds = 30,
    [ValidateRange(2, 30)]
    [int]$CancellationTerminalityHoldSeconds = 10,
    [string]$LogPath = '',
    [switch]$PrewarmSolve,
    [ValidateRange(0.5, 30.0)]
    [double]$PrewarmExposureSeconds = 3.0,
    [ValidateRange(10, 300)]
    [int]$PrewarmTimeoutSeconds = 180,
    [switch]$PreflightOnly,
    [switch]$FunctionsOnly
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Get-VerificationTerminalOutcome([string[]]$RuntimeStatuses) {
    $outcomes = @(@(
        foreach ($runtimeStatus in @($RuntimeStatuses)) {
            if ($runtimeStatus -match '^Verification-only measurements complete:\s*(passed|failed)\s*$') {
                $Matches[1].ToLowerInvariant()
            }
        }
    ) | Select-Object -Unique)

    if ($outcomes.Count -eq 1) {
        return [string]$outcomes[0]
    }

    return $null
}

function Test-CurrentVerificationTerminalFailure(
    [bool]$SeenRunning,
    [bool]$SeenCurrentRuntimeProgress,
    [string[]]$RuntimeStatuses) {
    return $SeenRunning `
        -and $SeenCurrentRuntimeProgress `
        -and (Get-VerificationTerminalOutcome $RuntimeStatuses) -eq 'failed'
}

function Get-CircularAzimuthDelta([double]$FirstAzimuth, [double]$SecondAzimuth) {
    $first = (($FirstAzimuth % 360.0) + 360.0) % 360.0
    $second = (($SecondAzimuth % 360.0) + 360.0) % 360.0
    $delta = [Math]::Abs($first - $second)
    return [Math]::Min($delta, 360.0 - $delta)
}

function Test-VerificationGuardArmPoint(
    [double]$Azimuth,
    [double]$Altitude,
    [double]$TargetAzimuth,
    [double]$TargetAltitude) {
    if (-not [double]::IsFinite($Azimuth) -or -not [double]::IsFinite($Altitude)) {
        return $false
    }

    return (Get-CircularAzimuthDelta $Azimuth $TargetAzimuth) -le 1.0 `
        -and [Math]::Abs($Altitude - $TargetAltitude) -le 1.0
}

function Test-NinaSequenceTerminalObservation(
    [string[]]$LeafStatuses,
    [string[]]$RuntimeStatuses) {
    $normalizedLeafStatuses = @($LeafStatuses | ForEach-Object {
        ([string]$_).ToUpperInvariant()
    })
    $leafTerminal = $normalizedLeafStatuses.Count -eq 1 -and
        $normalizedLeafStatuses[0] -in @('CREATED', 'FINISHED', 'FAILED', 'SKIPPED')
    $runtimeTerminal = (Get-VerificationTerminalOutcome $RuntimeStatuses) -ne $null
    $pluginTerminal = @($RuntimeStatuses).Count -eq 0 -or $runtimeTerminal
    return $leafTerminal -and $pluginTerminal
}

if ($FunctionsOnly) {
    return
}

if (-not $LogPath) {
    $LogPath = Join-Path $env:USERPROFILE 'Documents\TPPA-PHD2-tests\guarded-verification.log'
}
$logDirectory = Split-Path -Parent $LogPath
if ($logDirectory) { New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null }

function Write-RunLog([string]$Message) {
    Add-Content -LiteralPath $LogPath -Value ('{0:O} {1}' -f (Get-Date), $Message)
}

function New-NinaHttpClient {
    $client = [Net.Http.HttpClient]::new()
    $client.Timeout = [Threading.Timeout]::InfiniteTimeSpan
    $client
}

$script:NinaHttpClient = New-NinaHttpClient

function Invoke-Nina([string]$Path, [int]$TimeoutSeconds = 20) {
    $uri = $BaseUrl.TrimEnd('/') + $Path
    $cts = [Threading.CancellationTokenSource]::new()
    try {
        $cts.CancelAfter([TimeSpan]::FromSeconds($TimeoutSeconds))
        $requestTask = $script:NinaHttpClient.GetStringAsync($uri, $cts.Token)
        $waitMilliseconds = [Math]::Max(1000, ($TimeoutSeconds + 1) * 1000)
        $completed = [Threading.Tasks.Task]::WaitAny(
            [Threading.Tasks.Task[]]@($requestTask),
            $waitMilliseconds)
        if ($completed -ne 0) {
            $cts.Cancel()
            $script:NinaHttpClient.Dispose()
            $script:NinaHttpClient = New-NinaHttpClient
            throw "NINA request $Path exceeded the independent $TimeoutSeconds-second watchdog."
        }
        $json = $requestTask.GetAwaiter().GetResult()
        $json | ConvertFrom-Json
    } catch [OperationCanceledException] {
        $script:NinaHttpClient.Dispose()
        $script:NinaHttpClient = New-NinaHttpClient
        throw "NINA request $Path exceeded the independent $TimeoutSeconds-second watchdog."
    } finally {
        $cts.Dispose()
    }
}

function Invoke-GuardedSolvePrewarm(
    [Parameter(Mandatory = $true)]$InitialMount,
    [double]$TargetAzimuth,
    [double]$TargetAltitude) {
    $cameraResponse = Invoke-Nina -Path '/equipment/camera/info' -TimeoutSeconds 10
    if (-not $cameraResponse.Success -or
        -not $cameraResponse.Response.Connected -or
        [bool]$cameraResponse.Response.IsExposing) {
        throw (
            'TPPA pre-warm requires a connected idle camera: ' +
            "connected=$($cameraResponse.Response.Connected), exposing=$($cameraResponse.Response.IsExposing).")
    }

    $initialAzimuth = ConvertTo-NormalizedAzimuth ([double]$InitialMount.Azimuth)
    $initialAltitude = [double]$InitialMount.Altitude
    if ([bool]$InitialMount.Slewing -or
        -not (Test-BalconyPoint $initialAzimuth $initialAltitude) -or
        -not (Test-VerificationGuardArmPoint `
            $initialAzimuth $initialAltitude $TargetAzimuth $TargetAltitude)) {
        throw (
            'TPPA pre-warm is no-slew and requires the mount already settled at the validated target; ' +
            "Az=$initialAzimuth Alt=$initialAltitude slewing=$($InitialMount.Slewing).")
    }

    $startedUtc = [DateTime]::UtcNow
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $duration = $PrewarmExposureSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)
    $capture = Invoke-Nina -Path (
        "/equipment/camera/capture?solve=true&duration=$duration&waitForResult=true&omitImage=true&imageType=SNAPSHOT") `
        -TimeoutSeconds $PrewarmTimeoutSeconds
    $stopwatch.Stop()

    if (-not $capture.Success -or
        -not $capture.Response.PlateSolveResult.Success) {
        throw 'TPPA pre-warm capture or plate solve failed.'
    }

    $afterResponse = Invoke-Nina -Path '/equipment/mount/info' -TimeoutSeconds 10
    if (-not $afterResponse.Success -or -not $afterResponse.Response.Connected) {
        throw 'TPPA pre-warm cannot verify the post-solve mount state.'
    }
    $after = $afterResponse.Response
    $afterAzimuth = ConvertTo-NormalizedAzimuth ([double]$after.Azimuth)
    $afterAltitude = [double]$after.Altitude
    $azimuthDelta = Get-CircularAzimuthDelta $initialAzimuth $afterAzimuth
    $altitudeDelta = [Math]::Abs($initialAltitude - $afterAltitude)
    if ([bool]$after.Slewing -or
        [string]$after.SideOfPier -ne [string]$InitialMount.SideOfPier -or
        $azimuthDelta -gt 0.10 -or $altitudeDelta -gt 0.10) {
        throw (
            'TPPA pre-warm violated the no-slew pointing hold: ' +
            "dAz=$azimuthDelta deg, dAlt=$altitudeDelta deg, " +
            "pier=$($InitialMount.SideOfPier)->$($after.SideOfPier), slewing=$($after.Slewing).")
    }

    $result = [pscustomobject]@{
        SchemaVersion = 1
        StartedUtc = $startedUtc.ToString('O')
        ElapsedMilliseconds = [Math]::Round($stopwatch.Elapsed.TotalMilliseconds, 1)
        ExposureSeconds = $PrewarmExposureSeconds
        SolveSucceeded = $true
        InitialAzimuthDegrees = $initialAzimuth
        InitialAltitudeDegrees = $initialAltitude
        FinalAzimuthDegrees = $afterAzimuth
        FinalAltitudeDegrees = $afterAltitude
        SideOfPier = [string]$after.SideOfPier
        ExcludedFromVerificationRuntime = $true
    }
    Write-RunLog ('TPPA_PREWARM ' + ($result | ConvertTo-Json -Compress))
    return $result
}

function Stop-NinaSequence {
    try {
        $result = Invoke-Nina -Path '/sequence/stop' -TimeoutSeconds 20
        Write-RunLog ('Stop response: ' + ($result | ConvertTo-Json -Compress -Depth 5))
        return [bool]$result.Success
    } catch {
        Write-RunLog ('Stop warning: ' + $_.Exception.Message)
        return $false
    }
}

function Wait-NinaSequenceTerminality {
    $deadline = (Get-Date).AddSeconds($CancellationTerminalityTimeoutSeconds)
    $holdStarted = $null
    $observations = [Collections.Generic.List[string]]::new()

    do {
        try {
            $state = Invoke-Nina -Path '/sequence/json' -TimeoutSeconds 8
            $leafNodes = if ($state.Success) {
                @(Find-CompactSequenceLeafNodes $state.Response)
            } else {
                @()
            }
            $leafStatuses = @($leafNodes | ForEach-Object {
                ([string]$_.Status).ToUpperInvariant()
            })
            $runtimeStatuses = if ($state.Success) {
                @(Find-VerificationRuntimeStatuses $state.Response | Select-Object -Unique)
            } else {
                @()
            }
            $terminal = [bool]$state.Success -and
                (Test-NinaSequenceTerminalObservation $leafStatuses $runtimeStatuses)
            $observations.Add((
                'leaf={0}; runtime={1}; terminal={2}' -f
                ($leafStatuses -join ','),
                ($runtimeStatuses -join ' | '),
                $terminal))

            if ($terminal) {
                if ($null -eq $holdStarted) { $holdStarted = Get-Date }
                if (((Get-Date) - $holdStarted).TotalSeconds -ge
                        $CancellationTerminalityHoldSeconds) {
                    Write-RunLog (
                        'Sequence/plugin cancellation terminality sustained for {0}s: {1}' -f
                        $CancellationTerminalityHoldSeconds,
                        $observations[$observations.Count - 1])
                    return $true
                }
            } else {
                $holdStarted = $null
            }
        } catch {
            $holdStarted = $null
            $observations.Add('exception=' + $_.Exception.Message)
        }
        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)

    Write-RunLog (
        'Sequence/plugin cancellation terminality not observed: ' +
        (($observations | Select-Object -Last 8) -join '; '))
    return $false
}

function Stop-NinaMount {
    try {
        $result = Invoke-Nina -Path '/equipment/mount/slew/stop' -TimeoutSeconds 10
        Write-RunLog ('Mount stop response: ' + ($result | ConvertTo-Json -Compress -Depth 5))
    } catch {
        Write-RunLog ('Mount stop warning: ' + $_.Exception.Message)
    }
}

function Stop-NinaTracking {
    $failures = [Collections.Generic.List[string]]::new()

    foreach ($attempt in 1..10) {
        Start-Sleep -Seconds 1
        try {
            $result = Invoke-Nina -Path '/equipment/mount/tracking?mode=4' -TimeoutSeconds 10
            if (-not $result.Success) {
                $failures.Add(
                    "attempt $attempt rejected: status=$($result.StatusCode), error=$($result.Error)")
                continue
            }
            # Sequence cancellation and TPPA cleanup can asynchronously restore
            # sidereal tracking after an initially successful stop. Require a
            # sustained off/idle hold before reporting cleanup success.
            $holdSeconds = 10
            $holdStarted = Get-Date
            $holdDeadline = $holdStarted.AddSeconds($holdSeconds)
            $holdPassed = $true
            do {
                Start-Sleep -Seconds 2
                $mount = Invoke-Nina -Path '/equipment/mount/info' -TimeoutSeconds 10
                if (-not $mount.Success -or
                    -not $mount.Response.Connected -or
                    [bool]$mount.Response.Slewing -or
                    [bool]$mount.Response.TrackingEnabled) {
                    $elapsed = ((Get-Date) - $holdStarted).TotalSeconds
                    $failures.Add((
                        'attempt {0} sustained hold failed after {1:F1}s: connected={2}, slewing={3}, tracking={4}' -f
                        $attempt,
                        $elapsed,
                        $mount.Response.Connected,
                        $mount.Response.Slewing,
                        $mount.Response.TrackingEnabled))
                    $holdPassed = $false
                    break
                }
            } while ((Get-Date) -lt $holdDeadline)

            if ($holdPassed) {
                Write-RunLog (
                    'Tracking stop sustained for {0}s on attempt {1}: {2}' -f
                    $holdSeconds,
                    $attempt,
                    ($result | ConvertTo-Json -Compress -Depth 5))
                return $true
            }
        } catch {
            $failures.Add("attempt $attempt exception: $($_.Exception.Message)")
        }
    }

    Write-RunLog ('Tracking stop failed after bounded retries: ' + ($failures -join '; '))
    return $false
}

function ConvertTo-NormalizedAzimuth([double]$Azimuth) {
    (($Azimuth % 360.0) + 360.0) % 360.0
}

function Test-BalconyPoint([double]$Azimuth, [double]$Altitude) {
    $normalizedAzimuth = ConvertTo-NormalizedAzimuth $Azimuth
    $insideAzimuth = $normalizedAzimuth -ge $WesternAzimuthMinimumDegrees -or
        $normalizedAzimuth -le $EasternAzimuthMaximumDegrees
    $insideAzimuth -and $Altitude -ge $MinimumAltitudeDegrees -and $Altitude -le $MaximumAltitudeDegrees
}

function Confirm-SettledBalconyViolation(
    [double]$InitialAzimuth,
    [double]$InitialAltitude) {
    $samples = [Collections.Generic.List[string]]::new()
    $samples.Add(("Az={0:F6},Alt={1:F6}" -f $InitialAzimuth, $InitialAltitude))

    # A July 2026 field run observed one Advanced API Alt=0 sample while NINA's
    # own mount telemetry remained near Alt=30. Confirm a settled violation
    # promptly without turning one transport/update dropout into a false abort.
    foreach ($attempt in 1..2) {
        Start-Sleep -Milliseconds 250
        $response = Invoke-Nina -Path '/equipment/mount/info' -TimeoutSeconds 3
        if (-not $response.Success -or -not $response.Response.Connected) {
            throw 'Connected mount status is unavailable while confirming a balcony violation.'
        }

        $mount = $response.Response
        if ([bool]$mount.Slewing) {
            Write-RunLog (
                "Balcony violation confirmation deferred because the mount resumed slewing; samples=" +
                ($samples -join ';'))
            return $false
        }

        $azimuth = ConvertTo-NormalizedAzimuth ([double]$mount.Azimuth)
        $altitude = [double]$mount.Altitude
        if (-not [double]::IsFinite($azimuth) -or -not [double]::IsFinite($altitude)) {
            throw 'Mount coordinates are non-finite while confirming a balcony violation.'
        }

        $samples.Add(("Az={0:F6},Alt={1:F6}" -f $azimuth, $altitude))
        if (Test-BalconyPoint $azimuth $altitude) {
            Write-RunLog (
                "Ignored one transient out-of-envelope Advanced API sample after immediate telemetry recovery; samples=" +
                ($samples -join ';'))
            return $false
        }
    }

    Write-RunLog ("Confirmed settled balcony violation; samples=" + ($samples -join ';'))
    return $true
}

function Find-VerificationInstruction([object]$Node) {
    if ($null -eq $Node -or $Node -is [string]) { return $null }
    if ($Node.PSObject.Properties['VerificationOnly'] -and
        [bool]$Node.VerificationOnly -and
        $Node.PSObject.Properties['Coordinates']) {
        return $Node
    }
    if ($Node -is [Collections.IEnumerable]) {
        foreach ($item in $Node) {
            $found = Find-VerificationInstruction $item
            if ($null -ne $found) { return $found }
        }
        return $null
    }
    foreach ($property in $Node.PSObject.Properties) {
        $found = Find-VerificationInstruction $property.Value
        if ($null -ne $found) { return $found }
    }
    return $null
}

function Find-SequenceInstructionNodes([object]$Node) {
    if ($null -eq $Node -or $Node -is [string]) { return }
    $typeProperty = $Node.PSObject.Properties['$type']
    $typeName = if ($typeProperty) { [string]$typeProperty.Value } else { '' }
    if ($typeName -notmatch '^System\.Collections\.' -and $typeName -match '\.Instructions\.|\.SequenceItem\.') {
        Write-Output -NoEnumerate $Node
        return
    }
    if ($Node -is [Collections.IEnumerable]) {
        foreach ($item in $Node) {
            Find-SequenceInstructionNodes $item
        }
        return
    }
    foreach ($property in $Node.PSObject.Properties) {
        Find-SequenceInstructionNodes $property.Value
    }
}

function Find-SequenceHazardNodes([object]$Node) {
    if ($null -eq $Node -or $Node -is [string]) { return }
    $typeProperty = $Node.PSObject.Properties['$type']
    $typeName = if ($typeProperty) { [string]$typeProperty.Value } else { '' }
    if ($typeName -notmatch '^System\.Collections\.' -and $typeName -match '\.Triggers?\.|\.Conditions?\.') {
        Write-Output -NoEnumerate $Node
        return
    }
    if ($Node -is [Collections.IEnumerable]) {
        foreach ($item in $Node) { Find-SequenceHazardNodes $item }
        return
    }
    foreach ($property in $Node.PSObject.Properties) {
        Find-SequenceHazardNodes $property.Value
    }
}

function Find-CompactSequenceLeafNodes([object]$Node) {
    if ($null -eq $Node -or $Node -is [string]) { return }
    if ($Node -is [Collections.IEnumerable]) {
        foreach ($item in $Node) { Find-CompactSequenceLeafNodes $item }
        return
    }
    $itemsProperty = $Node.PSObject.Properties['Items']
    if ($itemsProperty) {
        foreach ($item in @($itemsProperty.Value)) { Find-CompactSequenceLeafNodes $item }
        return
    }
    if ($Node.PSObject.Properties['Status']) {
        Write-Output -NoEnumerate $Node
    }
}

function Find-VerificationRuntimeStatuses([object]$Node) {
    if ($null -eq $Node -or $Node -is [string]) { return }
    if ($Node -is [Collections.IEnumerable]) {
        foreach ($item in $Node) { Find-VerificationRuntimeStatuses $item }
        return
    }

    $tppaProperty = $Node.PSObject.Properties['TPAPAVM']
    if ($tppaProperty -and $tppaProperty.Value) {
        $statusProperty = $tppaProperty.Value.PSObject.Properties['Status']
        if ($statusProperty -and $statusProperty.Value) {
            $runtimeStatusProperty = $statusProperty.Value.PSObject.Properties['Status']
            if ($runtimeStatusProperty -and $runtimeStatusProperty.Value) {
                Write-Output ([string]$runtimeStatusProperty.Value)
            }
        }
    }

    foreach ($property in $Node.PSObject.Properties) {
        Find-VerificationRuntimeStatuses $property.Value
    }
}

function ConvertFrom-DegreesMinutesSeconds([double]$Degrees, [double]$Minutes, [double]$Seconds) {
    $sign = if ($Degrees -lt 0 -or $Minutes -lt 0 -or $Seconds -lt 0) { -1.0 } else { 1.0 }
    $sign * ([Math]::Abs($Degrees) + [Math]::Abs($Minutes) / 60.0 + [Math]::Abs($Seconds) / 3600.0)
}
$resolvedSequence = (Resolve-Path -LiteralPath $SequencePath).Path
$sequenceJson = [IO.File]::ReadAllText($resolvedSequence)
$sequenceDocument = $sequenceJson | ConvertFrom-Json
$verificationInstruction = Find-VerificationInstruction $sequenceDocument
$instructionNodes = @(Find-SequenceInstructionNodes $sequenceDocument)
if ($instructionNodes.Count -ne 1) {
    throw "The guarded sequence must contain exactly one executable instruction; found $($instructionNodes.Count)."
}
$hazardNodes = @(Find-SequenceHazardNodes $sequenceDocument)
if ($hazardNodes.Count -ne 0) {
    throw "The guarded sequence must not contain triggers or conditions; found $($hazardNodes.Count)."
}
$instructionType = [string]$instructionNodes[0].'$type'
if ($instructionType -notmatch '^NINA\.Plugins\.PolarAlignment\.Instructions\.PolarAlignment,') {
    throw "The guarded sequence contains an unexpected instruction type: $instructionType"
}
if ($null -eq $verificationInstruction) {
    throw 'The selected sequence is not VerificationOnly.'
}
$directionSampleCountProperty =
    $verificationInstruction.PSObject.Properties['VerificationDirectionSampleCount']
$directionSampleCount = if ($directionSampleCountProperty) {
    [int]$directionSampleCountProperty.Value
} else {
    3
}
$expectedPointCount = 3 * $directionSampleCount
$minimumAdmittedRuntimeSeconds = [Math]::Ceiling(
    $expectedPointCount * $MinimumRuntimePerPointSeconds + $CleanupReserveSeconds)
$runtimeAdmission = [ordered]@{
    SchemaVersion = 1
    ExpectedPointCount = $expectedPointCount
    MinimumRuntimePerPointSeconds = $MinimumRuntimePerPointSeconds
    CleanupReserveSeconds = $CleanupReserveSeconds
    RequiredRuntimeSeconds = $minimumAdmittedRuntimeSeconds
    MaximumRuntimeSeconds = $MaximumRuntimeSeconds
    Admitted = $MaximumRuntimeSeconds -ge $minimumAdmittedRuntimeSeconds
    GrantsMotionAuthority = $false
    GrantsCompletionAuthority = $false
}
Write-RunLog ('TPPA_RUNTIME_ADMISSION ' + ($runtimeAdmission | ConvertTo-Json -Compress))
if (-not $runtimeAdmission.Admitted) {
    throw (
        'Verification runtime admission denied before sequence start: ' +
        "cap=$MaximumRuntimeSeconds s, required=$minimumAdmittedRuntimeSeconds s, " +
        "points=$expectedPointCount, perPoint=$MinimumRuntimePerPointSeconds s, " +
        "cleanupReserve=$CleanupReserveSeconds s.")
}
if ($verificationInstruction.PSObject.Properties['PreSeatAzimuthBeforeMeasurement'] -and
    [bool]$verificationInstruction.PreSeatAzimuthBeforeMeasurement) {
    throw 'Azimuth pre-seat must be disabled for verification-only diagnostics.'
}
$requiredEnvelope = [ordered]@{
    MountMotionMinimumAltitudeDegrees = $MinimumAltitudeDegrees
    MountMotionMaximumAltitudeDegrees = $MaximumAltitudeDegrees
    MountMotionAzimuthStartDegrees = $WesternAzimuthMinimumDegrees
    MountMotionAzimuthEndDegrees = $EasternAzimuthMaximumDegrees
}
if (-not $verificationInstruction.PSObject.Properties['MountMotionEnvelopeEnabled'] -or
    -not [bool]$verificationInstruction.MountMotionEnvelopeEnabled) {
    throw 'The verification sequence must explicitly enable its mount-motion envelope.'
}
foreach ($entry in $requiredEnvelope.GetEnumerator()) {
    $property = $verificationInstruction.PSObject.Properties[$entry.Key]
    if (-not $property) {
        throw "The verification sequence is missing required envelope field $($entry.Key)."
    }
    if ([Math]::Abs([double]$property.Value - [double]$entry.Value) -gt 0.001) {
        throw "Envelope field $($entry.Key)=$($property.Value) does not match required guard value $($entry.Value)."
    }
}


$coordinates = $verificationInstruction.Coordinates
if ($null -eq $coordinates -or
    -not $coordinates.PSObject.Properties['AltDegrees'] -or
    -not $coordinates.PSObject.Properties['AzDegrees']) {
    throw 'The verification sequence must contain an explicit horizontal target.'
}
$targetAltitude = ConvertFrom-DegreesMinutesSeconds `
    ([double]$coordinates.AltDegrees) ([double]$coordinates.AltMinutes) ([double]$coordinates.AltSeconds)
$targetAzimuth = ConvertFrom-DegreesMinutesSeconds `
    ([double]$coordinates.AzDegrees) ([double]$coordinates.AzMinutes) ([double]$coordinates.AzSeconds)
if (-not (Test-BalconyPoint $targetAzimuth $targetAltitude)) {
    throw "Sequence target Az=$targetAzimuth Alt=$targetAltitude is outside the balcony guard."
}
if ($targetAltitude -lt ($MinimumAltitudeDegrees + $TargetAltitudeMarginDegrees) -or
    $targetAltitude -gt ($MaximumAltitudeDegrees - $TargetAltitudeMarginDegrees)) {
    throw "Sequence target altitude $targetAltitude lacks the required $TargetAltitudeMarginDegrees degree guard margin."
}

$installValidator = Join-Path $PSScriptRoot 'validate_tppa_plugin_install.ps1'
if (-not (Test-Path -LiteralPath $installValidator -PathType Leaf)) {
    throw "TPPA install validator is missing: $installValidator"
}
$install = & $installValidator -PluginDirectory $PluginDirectory -ExpectedRuntimeManifestSha256 $ExpectedRuntimeManifestSha256
Write-RunLog "Plugin preflight passed: $($install.AssemblyPath); SHA256=$($install.Sha256)."
if ($PreflightOnly) {
    [pscustomobject]@{
        SequencePath = $resolvedSequence
        TargetAzimuthDegrees = $targetAzimuth
        TargetAltitudeDegrees = $targetAltitude
        VerificationOnly = $true
        PluginSha256 = $install.Sha256
    }
    return
}
$sequenceName = [IO.Path]::GetFileNameWithoutExtension($resolvedSequence)
Write-RunLog "Loading guarded verification sequence $sequenceName at Az=$targetAzimuth Alt=$targetAltitude"
$load = Invoke-Nina -Path ('/sequence/load?sequenceName=' + [uri]::EscapeDataString($sequenceName))
if (-not $load.Success) { throw "Sequence load failed: $($load.Error)" }
$preStartMount = Invoke-Nina -Path '/equipment/mount/info'
if (-not $preStartMount.Success -or -not $preStartMount.Response.Connected) {
    throw 'A connected mount is required before starting guarded verification.'
}
if (-not $preStartMount.Response.PSObject.Properties['AtPark'] -or
    -not $preStartMount.Response.PSObject.Properties['Slewing']) {
    throw 'Guarded verification requires explicit parked and slewing mount state.'
}
if ([bool]$preStartMount.Response.AtPark) {
    throw 'Guarded verification will not start while the mount is parked.'
}
if ([bool]$preStartMount.Response.Slewing) {
    throw 'Guarded verification will not start while the mount is already slewing.'
}

$preStartAzimuth = ConvertTo-NormalizedAzimuth ([double]$preStartMount.Response.Azimuth)
$preStartAltitude = [double]$preStartMount.Response.Altitude
$guardArmed = (Test-BalconyPoint $preStartAzimuth $preStartAltitude) -and
    (Test-VerificationGuardArmPoint `
        $preStartAzimuth `
        $preStartAltitude `
        $targetAzimuth `
        $targetAltitude)
if ($guardArmed) {
    Write-RunLog "Balcony guard armed from pre-start telemetry at Az=$preStartAzimuth Alt=$preStartAltitude."
}

if ($PrewarmSolve) {
    $prewarm = Invoke-GuardedSolvePrewarm `
        -InitialMount $preStartMount.Response `
        -TargetAzimuth $targetAzimuth `
        -TargetAltitude $targetAltitude
    Write-RunLog (
        "Guarded TPPA pre-warm completed in $($prewarm.ElapsedMilliseconds) ms; " +
        'this separately timed phase is excluded from verification runtime.')
}

$start = Invoke-Nina -Path '/sequence/start?skipValidation=true'
if (-not $start.Success) { throw "Sequence start failed: $($start.Error)" }
Write-RunLog 'Verification-only sequence started.'

$deadline = (Get-Date).AddSeconds($MaximumRuntimeSeconds)
$armDeadline = (Get-Date).AddSeconds([Math]::Min(120, $MaximumRuntimeSeconds))
$seenRunning = $false
$seenCurrentRuntimeProgress = $false
try {
    while ((Get-Date) -lt $deadline) {
        # The state route embeds large sequence payloads and can block for minutes.
        # The json route contains the same status tree without image-heavy state.
        $state = Invoke-Nina -Path '/sequence/json' -TimeoutSeconds 8
        if (-not $state.Success) {
            throw "Compact sequence status is unavailable: $($state.Error)"
        }
        $instructionStates = @(Find-CompactSequenceLeafNodes $state.Response)
        if ($instructionStates.Count -ne 1) {
            throw "Compact sequence status must contain exactly one instruction; found $($instructionStates.Count)."
        }
        $instructionStatus = ([string]$instructionStates[0].Status).ToUpperInvariant()
        $isRunning = $instructionStatus -eq 'RUNNING'
        $isFailed = $instructionStatus -eq 'FAILED'
        if ($isRunning) { $seenRunning = $true }
        if (-not $seenRunning -and $isFailed) {
            throw 'Verification-only sequence reported a failed status before entering RUNNING.'
        }
        $runtimeStatuses = @(Find-VerificationRuntimeStatuses $state.Response |
            Select-Object -Unique)
        $terminalOutcome = Get-VerificationTerminalOutcome $runtimeStatuses
        if ($seenRunning -and -not $terminalOutcome -and $runtimeStatuses.Count -gt 0) {
            $seenCurrentRuntimeProgress = $true
        }
        if (Test-CurrentVerificationTerminalFailure `
                -SeenRunning $seenRunning `
                -SeenCurrentRuntimeProgress $seenCurrentRuntimeProgress `
                -RuntimeStatuses $runtimeStatuses) {
            $terminalRuntimeStatus = $runtimeStatuses |
                Where-Object { $_ -match '^Verification-only measurements complete:\s*failed\s*$' } |
                Select-Object -First 1
            throw "TPPA runtime reported terminal verification failure while the sequencer status was $instructionStatus`: $terminalRuntimeStatus"
        }

        $mountResponse = Invoke-Nina -Path '/equipment/mount/info'
        if (-not $mountResponse.Success -or -not $mountResponse.Response.Connected) {
            throw 'Connected mount status is unavailable.'
        }
        $mount = $mountResponse.Response
        $azimuth = ConvertTo-NormalizedAzimuth ([double]$mount.Azimuth)
        $altitude = [double]$mount.Altitude

        $insideBalconyEnvelope = Test-BalconyPoint $azimuth $altitude
        if ([bool]$mount.Slewing) {
            if (-not $insideBalconyEnvelope) {
                throw (
                    "Balcony guard observed an in-slew envelope violation: " +
                    "Az=$azimuth Alt=$altitude.")
            }
        } else {
            if (-not $guardArmed) {
                if (Test-VerificationGuardArmPoint $azimuth $altitude $targetAzimuth $targetAltitude) {
                    $guardArmed = $true
                    Write-RunLog "Balcony guard armed at Az=$azimuth Alt=$altitude."
                }
            } elseif (-not $insideBalconyEnvelope -and
                    (Confirm-SettledBalconyViolation $azimuth $altitude)) {
                throw (
                    "Balcony guard confirmed a persistent violation at a settled pointing: " +
                    "Az=$azimuth Alt=$altitude.")
            }
        }
        if (-not $guardArmed -and (Get-Date) -ge $armDeadline) {
            throw 'Balcony guard did not arm at the validated target within 120 seconds.'
        }

        if ($seenRunning -and -not $isRunning) {
            if (-not $guardArmed) {
                throw 'Verification-only sequence ended before the balcony guard armed.'
            }
            if ($isFailed) {
                throw 'Verification-only sequence reported a failed status.'
            }
            if ($instructionStatus -ne 'FINISHED') {
                throw "Verification-only sequence ended without FINISHED status; observed $instructionStatus."
            }
            Write-RunLog 'Verification-only sequence completed with FINISHED status.'
            if (-not (Stop-NinaTracking)) {
                throw 'Verification-only sequence finished but tracking could not be stopped and verified.'
            }
            exit 0
        }
        Start-Sleep -Seconds 2
    }
    throw "Verification-only sequence exceeded $MaximumRuntimeSeconds seconds."
} catch {
    $failure = $_
    Write-RunLog ('FAIL: ' + $failure.Exception.Message)
    Stop-NinaMount
    $stopAccepted = Stop-NinaSequence
    $terminalityObserved = Wait-NinaSequenceTerminality
    if (-not $stopAccepted -or -not $terminalityObserved) {
        Write-RunLog (
            'CRITICAL: sequence/plugin cancellation terminality was not verified before the final tracking stop.')
    }
    if (-not (Stop-NinaTracking)) {
        Write-RunLog 'CRITICAL: guarded verification cleanup could not stop and verify tracking.'
    }
    throw $failure
}
