#requires -Version 7.0
param(
    [string]$BaseUrl = 'http://127.0.0.1:1888/v2/api',
    [Parameter(Mandatory = $true)]
    [string]$SequencePath,
    [Parameter(Mandatory = $true)]
    [string]$PluginDirectory,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string]$ExpectedPluginSha256,
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
    [int]$MaximumRuntimeSeconds = 600,
    [string]$LogPath = '',
    [switch]$PreflightOnly
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

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

function Stop-NinaSequence {
    try {
        $result = Invoke-Nina -Path '/sequence/stop' -TimeoutSeconds 20
        Write-RunLog ('Stop response: ' + ($result | ConvertTo-Json -Compress -Depth 5))
    } catch {
        Write-RunLog ('Stop warning: ' + $_.Exception.Message)
    }
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
$install = & $installValidator -PluginDirectory $PluginDirectory -ExpectedSha256 $ExpectedPluginSha256
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

$start = Invoke-Nina -Path '/sequence/start?skipValidation=true'
if (-not $start.Success) { throw "Sequence start failed: $($start.Error)" }
Write-RunLog 'Verification-only sequence started.'

$deadline = (Get-Date).AddSeconds($MaximumRuntimeSeconds)
$armDeadline = (Get-Date).AddSeconds([Math]::Min(120, $MaximumRuntimeSeconds))
$seenRunning = $false
$guardArmed = $false
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

        $mountResponse = Invoke-Nina -Path '/equipment/mount/info'
        if (-not $mountResponse.Success -or -not $mountResponse.Response.Connected) {
            throw 'Connected mount status is unavailable.'
        }
        $mount = $mountResponse.Response
        $azimuth = ConvertTo-NormalizedAzimuth ([double]$mount.Azimuth)
        $altitude = [double]$mount.Altitude

        if (-not [bool]$mount.Slewing) {
            if (-not $guardArmed) {
                $azimuthDelta = [Math]::Abs($azimuth - (ConvertTo-NormalizedAzimuth $targetAzimuth))
                $azimuthDelta = [Math]::Min($azimuthDelta, 360.0 - $azimuthDelta)
                if ($azimuthDelta -le 1.0 -and [Math]::Abs($altitude - $targetAltitude) -le 1.0) {
                    $guardArmed = $true
                    Write-RunLog "Balcony guard armed at Az=$azimuth Alt=$altitude."
                }
            } elseif (-not (Test-BalconyPoint $azimuth $altitude) -and
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
            exit 0
        }
        Start-Sleep -Seconds 2
    }
    throw "Verification-only sequence exceeded $MaximumRuntimeSeconds seconds."
} catch {
    Write-RunLog ('FAIL: ' + $_.Exception.Message)
    Stop-NinaSequence
    throw
}
