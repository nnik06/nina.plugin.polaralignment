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

function Invoke-Nina([string]$Path, [int]$TimeoutSeconds = 20) {
    Invoke-RestMethod -Uri ($BaseUrl.TrimEnd('/') + $Path) -Method Get -TimeoutSec $TimeoutSeconds
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

function ConvertFrom-DegreesMinutesSeconds([double]$Degrees, [double]$Minutes, [double]$Seconds) {
    $sign = if ($Degrees -lt 0 -or $Minutes -lt 0 -or $Seconds -lt 0) { -1.0 } else { 1.0 }
    $sign * ([Math]::Abs($Degrees) + [Math]::Abs($Minutes) / 60.0 + [Math]::Abs($Seconds) / 3600.0)
}
$resolvedSequence = (Resolve-Path -LiteralPath $SequencePath).Path
$sequenceJson = [IO.File]::ReadAllText($resolvedSequence)
$sequenceDocument = $sequenceJson | ConvertFrom-Json
$verificationInstruction = Find-VerificationInstruction $sequenceDocument
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

$start = Invoke-Nina -Path '/sequence/start?skipValidation=true'
if (-not $start.Success) { throw "Sequence start failed: $($start.Error)" }
Write-RunLog 'Verification-only sequence started.'

$deadline = (Get-Date).AddSeconds($MaximumRuntimeSeconds)
$seenRunning = $false
$guardArmed = $false
try {
    while ((Get-Date) -lt $deadline) {
        $state = Invoke-Nina -Path '/sequence/state'
        $stateJson = $state.Response | ConvertTo-Json -Compress -Depth 20
        if ($stateJson -match '"Status":"RUNNING"') { $seenRunning = $true }

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
            } elseif (-not (Test-BalconyPoint $azimuth $altitude)) {
                throw "Balcony guard violated at a settled pointing: Az=$azimuth Alt=$altitude."
            }
        }

        if ($seenRunning -and $stateJson -notmatch '"Status":"RUNNING"') {
            Write-RunLog 'Verification-only sequence completed.'
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
