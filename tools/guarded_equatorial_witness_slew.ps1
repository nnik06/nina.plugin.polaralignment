param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(0.0, 360.0)]
    [double]$RightAscensionDegrees,
    [Parameter(Mandatory = $true)]
    [ValidateRange(-90.0, 90.0)]
    [double]$DeclinationDegrees,
    [Parameter(Mandatory = $true)]
    [ValidateSet('pierEast', 'pierWest')]
    [string]$ExpectedPierSide,
    [Parameter(Mandatory = $true)]
    [string]$CommandId,
    [Parameter(Mandatory = $true)]
    [string]$ReceiptPath,
    [ValidateRange(0.05, 2.0)]
    [double]$PointingToleranceDegrees = 0.5,
    [string]$NinaApiBase = 'http://127.0.0.1:1888/v2/api'
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

function Get-CircularDistance([double]$Left, [double]$Right) {
    $distance = [Math]::Abs($Left - $Right) % 360.0
    if ($distance -gt 180.0) { $distance = 360.0 - $distance }
    return $distance
}

function Write-CreateNewUtf8([string]$Path, [string]$Text) {
    $parent = Split-Path -Parent ([IO.Path]::GetFullPath($Path))
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Text)
    $stream = [IO.FileStream]::new(
        $Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
}

$initial = (Invoke-RestMethod -Uri "$NinaApiBase/equipment/mount/info" -TimeoutSec 10).Response
if (-not [bool]$initial.Connected -or [bool]$initial.Slewing -or
        -not [bool]$initial.TrackingEnabled -or [bool]$initial.AtPark) {
    throw 'Witness slew requires a connected, tracking, unparked, idle mount.'
}
if ([string]$initial.SideOfPier -ne $ExpectedPierSide) {
    throw "Initial pier side '$($initial.SideOfPier)' does not match '$ExpectedPierSide'."
}

$issuedUtc = [DateTime]::UtcNow
$ra = [uri]::EscapeDataString($RightAscensionDegrees.ToString(
    'R', [Globalization.CultureInfo]::InvariantCulture))
$dec = [uri]::EscapeDataString($DeclinationDegrees.ToString(
    'R', [Globalization.CultureInfo]::InvariantCulture))
$uri = "$NinaApiBase/equipment/mount/slew?ra=$ra&dec=$dec&waitForResult=true&center=false&rotate=false"
$response = Invoke-RestMethod -Uri $uri -TimeoutSec 180
if (-not [bool]$response.Success) { throw "NINA witness slew failed: $($response.Error)" }
$completedUtc = [DateTime]::UtcNow

$actual = (Invoke-RestMethod -Uri "$NinaApiBase/equipment/mount/info" -TimeoutSec 10).Response
if ([bool]$actual.Slewing -or -not [bool]$actual.TrackingEnabled -or
        [bool]$actual.AtPark -or [string]$actual.SideOfPier -ne $ExpectedPierSide) {
    throw 'Post-slew mount state is not stationary, tracking, unparked, and on the expected pier side.'
}
$actualRaDegrees = (([double]$actual.RightAscension * 15.0) % 360.0 + 360.0) % 360.0
$raError = Get-CircularDistance $actualRaDegrees $RightAscensionDegrees
$decError = [Math]::Abs([double]$actual.Declination - $DeclinationDegrees)
if ($raError -gt $PointingToleranceDegrees -or $decError -gt $PointingToleranceDegrees) {
    throw ("Post-slew equatorial error exceeds tolerance: dRA={0:F3}, dDec={1:F3} deg." -f
        $raError, $decError)
}

$receipt = [ordered]@{
    schemaVersion = 1
    commandId = $CommandId
    issuedUtc = $issuedUtc.ToString('O')
    completedUtc = $completedUtc.ToString('O')
    commandedRightAscensionDegrees = $RightAscensionDegrees
    commandedDeclinationDegrees = $DeclinationDegrees
    expectedPierSide = $ExpectedPierSide
    actualRightAscensionDegrees = $actualRaDegrees
    actualDeclinationDegrees = [double]$actual.Declination
    actualAzimuthDegrees = [double]$actual.Azimuth
    actualAltitudeDegrees = [double]$actual.Altitude
    grantsUpasAuthority = $false
    grantsCompletionAuthority = $false
}
Write-CreateNewUtf8 $ReceiptPath ($receipt | ConvertTo-Json -Depth 4 -Compress)
$receipt | ConvertTo-Json -Depth 4 -Compress
