param(
    [switch]$LibraryOnly,
    [Guid]$RunId,
    [ValidateSet('A', 'B', 'C')]
    [string]$PositionId,
    [ValidateRange(0, 3)]
    [int]$SequenceIndex,
    [string]$MountCommandId,
    [DateTime]$MountCommandIssuedUtc,
    [DateTime]$MountCommandCompletedUtc,
    [ValidateRange(0.0, 360.0)]
    [double]$CommandedRightAscensionDegrees,
    [ValidateRange(-90.0, 90.0)]
    [double]$CommandedDeclinationDegrees,
    [ValidateSet('pierEast', 'pierWest')]
    [string]$ExpectedPierSide,
    [ValidateRange(-90.0, 90.0)]
    [double]$SiteLatitudeDegrees,
    [ValidateRange(-180.0, 180.0)]
    [double]$SiteLongitudeDegrees,
    [ValidateRange(-500.0, 10000.0)]
    [double]$SiteElevationMeters,
    [ValidateRange(100, 30000)]
    [int]$ExposureMilliseconds = 15000,
    [ValidateRange(0.1, 20.0)]
    [double]$AstapFieldOfViewDegrees = 2.0,
    [ValidateRange(1.0, 30.0)]
    [double]$AstapSearchRadiusDegrees = 10.0,
    [ValidateRange(0.0, 1000.0)]
    [double]$FitsTimestampUncertaintyMilliseconds = 250.0,
    [string]$OutputDirectory,
    [string]$NinaApiBase = 'http://127.0.0.1:1888/v2/api',
    [string]$Phd2Host = '127.0.0.1',
    [int]$Phd2Port = 4400,
    [string]$AstapPath = 'C:\Program Files\astap\astap_cli.exe'
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-CreateNewUtf8([string]$Path, [string]$Text) {
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Text)
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
}

function Convert-FitsScalar([string]$Value) {
    $valueWithoutComment = ($Value -split '/', 2)[0].Trim()
    if ($valueWithoutComment.StartsWith("'")) {
        return $valueWithoutComment.Trim("'").Trim()
    }
    if ($valueWithoutComment -eq 'T') { return $true }
    if ($valueWithoutComment -eq 'F') { return $false }
    $number = 0.0
    if ([double]::TryParse($valueWithoutComment,
            [Globalization.NumberStyles]::Float,
            [Globalization.CultureInfo]::InvariantCulture,
            [ref]$number)) { return $number }
    return $valueWithoutComment
}

function Read-FitsHeader([string]$Path) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open,
        [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $header = [ordered]@{}
        $cardBytes = [byte[]]::new(80)
        $cards = 0
        while ($cards -lt 3600) {
            $read = $stream.Read($cardBytes, 0, 80)
            if ($read -ne 80) { throw 'FITS header ended before the END card.' }
            $cards++
            $card = [Text.Encoding]::ASCII.GetString($cardBytes)
            $keyword = $card.Substring(0, 8).Trim()
            if ($keyword -eq 'END') { break }
            if ($card[8] -eq '=' -and $keyword) {
                $header[$keyword] = Convert-FitsScalar $card.Substring(10)
            }
        }
        if ($cards -ge 3600 -or -not $header.Contains('SIMPLE')) {
            throw 'FITS primary header is missing SIMPLE or END.'
        }
        return $header
    } finally { $stream.Dispose() }
}

function Read-AstapCardFile([string]$Path) {
    $header = [ordered]@{}
    foreach ($line in [IO.File]::ReadAllLines($Path)) {
        if ($line.Length -lt 9) { continue }
        $keyword = $line.Substring(0, [Math]::Min(8, $line.Length)).Trim()
        $equals = $line.IndexOf('=')
        if ($keyword -and $equals -ge 0) {
            $header[$keyword] = Convert-FitsScalar $line.Substring($equals + 1)
        }
    }
    return $header
}

function Read-AstapIni([string]$Path) {
    $values = [ordered]@{}
    foreach ($line in [IO.File]::ReadAllLines($Path)) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith(';') -or $trimmed.StartsWith('[')) { continue }
        $equals = $trimmed.IndexOf('=')
        if ($equals -gt 0) {
            $values[$trimmed.Substring(0, $equals).Trim().ToUpperInvariant()] =
                Convert-FitsScalar $trimmed.Substring($equals + 1)
        }
    }
    return $values
}

function Get-AstapSolution([string]$WcsPath, [string]$IniPath) {
    if (-not (Test-Path -LiteralPath $WcsPath -PathType Leaf) -or
            -not (Test-Path -LiteralPath $IniPath -PathType Leaf)) {
        throw 'ASTAP did not create both .wcs and .ini solver outputs.'
    }
    $wcs = Read-AstapCardFile $WcsPath
    $ini = Read-AstapIni $IniPath
    $solvedText = if ($ini.Contains('PLTSOLVD')) { [string]$ini['PLTSOLVD'] } else { '' }
    if ($solvedText -ne 'T' -and $solvedText -ne 'True' -and $solvedText -ne 'true') {
        throw 'ASTAP output does not declare PLTSOLVD=T.'
    }
    foreach ($required in @('CRVAL1', 'CRVAL2')) {
        if (-not $wcs.Contains($required)) { throw "ASTAP WCS is missing $required." }
    }
    $pa = $null
    foreach ($key in @('CROTA2', 'CROTA1')) {
        if ($wcs.Contains($key)) { $pa = [double]$wcs[$key]; break }
    }
    if ($null -eq $pa -and $wcs.Contains('CD1_1') -and $wcs.Contains('CD1_2')) {
        $pa = [Math]::Atan2([double]$wcs['CD1_2'], [double]$wcs['CD1_1']) * 180.0 / [Math]::PI
    }
    if ($null -eq $pa -or -not [double]::IsFinite([double]$pa)) {
        throw 'ASTAP WCS does not contain a finite orientation.'
    }
    return [pscustomobject]@{
        RightAscensionDegrees = [double]$wcs['CRVAL1']
        DeclinationDegrees = [double]$wcs['CRVAL2']
        PositionAngleDegrees = [double]$pa
    }
}

function Get-BlindAstapArguments(
        [string]$FitsPath,
        [double]$FieldOfViewDegrees,
        [string]$OutputBase) {
    return @(
        '-f', $FitsPath,
        '-fov', $FieldOfViewDegrees.ToString(
            'R', [Globalization.CultureInfo]::InvariantCulture),
        '-o', $OutputBase,
        '-wcs',
        '-log'
    )
}

function Convert-UtcToJulianDate([DateTime]$Utc) {
    $value = if ($Utc.Kind -eq [DateTimeKind]::Utc) { $Utc } else { $Utc.ToUniversalTime() }
    return $value.ToOADate() + 2415018.5
}

function Convert-J2000ToTopocentricVector(
        [double]$RightAscensionDegrees,
        [double]$DeclinationDegrees,
        [DateTime]$ObservationUtc,
        [double]$LatitudeDegrees,
        [double]$LongitudeDegrees,
        [double]$ElevationMeters) {
    $transform = New-Object -ComObject ASCOM.Astrometry.Transform.Transform
    try {
        $transform.SiteLatitude = $LatitudeDegrees
        $transform.SiteLongitude = $LongitudeDegrees
        $transform.SiteElevation = $ElevationMeters
        $transform.SitePressure = 0.0
        $transform.SiteTemperature = 20.0
        $transform.Refraction = $false
        $transform.JulianDateUTC = Convert-UtcToJulianDate $ObservationUtc
        $transform.SetJ2000($RightAscensionDegrees / 15.0, $DeclinationDegrees)
        $azimuth = [double]$transform.AzimuthTopocentric
        $altitude = [double]$transform.ElevationTopocentric
        if (-not [double]::IsFinite($azimuth) -or -not [double]::IsFinite($altitude)) {
            throw 'ASCOM transform returned non-finite horizontal coordinates.'
        }
        $azimuthRadians = $azimuth * [Math]::PI / 180.0
        $altitudeRadians = $altitude * [Math]::PI / 180.0
        $cosAltitude = [Math]::Cos($altitudeRadians)
        return [pscustomobject]@{
            AzimuthDegrees = $azimuth
            AltitudeDegrees = $altitude
            X = $cosAltitude * [Math]::Cos($azimuthRadians)
            Y = -$cosAltitude * [Math]::Sin($azimuthRadians)
            Z = [Math]::Sin($altitudeRadians)
        }
    } finally {
        if ($transform -and $transform.PSObject.Methods.Name -contains 'Dispose') { $transform.Dispose() }
        if ($transform) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($transform) }
    }
}

function Connect-Phd2 {
    $client = [Net.Sockets.TcpClient]::new()
    $client.Connect($Phd2Host, $Phd2Port)
    $stream = $client.GetStream()
    $stream.ReadTimeout = 1000
    $stream.WriteTimeout = 3000
    $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8)
    $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
    $writer.NewLine = "`r`n"
    $writer.AutoFlush = $true
    return [pscustomobject]@{ Client = $client; Reader = $reader; Writer = $writer; NextId = 0 }
}

function Invoke-Phd2($Connection, [string]$Method, [object]$Parameters = $null,
        [int]$TimeoutSeconds = 30) {
    $Connection.NextId++
    $id = $Connection.NextId
    $message = [ordered]@{ method = $Method; id = $id }
    if ($null -ne $Parameters) { $message.params = $Parameters }
    $Connection.Writer.WriteLine(($message | ConvertTo-Json -Depth 10 -Compress))
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

function Get-NinaMountInfo {
    $response = Invoke-RestMethod -Uri "$NinaApiBase/equipment/mount/info" -TimeoutSec 5
    if (-not [bool]$response.Success -or $null -eq $response.Response) {
        throw 'NINA mount telemetry is unavailable.'
    }
    return $response.Response
}

function Test-NorthBalconyAzimuth([double]$Azimuth) {
    $wrapped = (($Azimuth % 360.0) + 360.0) % 360.0
    return $wrapped -ge 270.0 -or $wrapped -le 10.0
}

function Assert-MountStationary($Mount) {
    if (-not [bool]$Mount.Connected -or [bool]$Mount.Slewing -or
            -not [bool]$Mount.TrackingEnabled -or [bool]$Mount.AtPark) {
        throw 'Mount must be connected, tracking, unparked, and stationary.'
    }
    if ([string]$Mount.SideOfPier -ne $ExpectedPierSide) {
        throw "Mount pier side '$($Mount.SideOfPier)' does not match '$ExpectedPierSide'."
    }
    if (-not (Test-NorthBalconyAzimuth ([double]$Mount.Azimuth)) -or
            [double]$Mount.Altitude -lt 40.0 -or [double]$Mount.Altitude -gt 55.0) {
        throw 'Mount is outside the qualified witness envelope (north sector, altitude 40..55 degrees).'
    }
}

if ($LibraryOnly) { return }

foreach ($required in @{
        RunId = $RunId; PositionId = $PositionId; MountCommandId = $MountCommandId;
        OutputDirectory = $OutputDirectory; ExpectedPierSide = $ExpectedPierSide }) {
    if ($null -eq $required.Value -or [string]::IsNullOrWhiteSpace([string]$required.Value)) {
        throw "Required acquisition argument '$($required.Key)' is missing."
    }
}
if ($RunId -eq [Guid]::Empty) { throw 'RunId cannot be empty.' }
if ($PositionId -ne @('A', 'B', 'C', 'A')[$SequenceIndex]) {
    throw 'PositionId does not match the required A/B/C/A sequence index.'
}
foreach ($time in @($MountCommandIssuedUtc, $MountCommandCompletedUtc)) {
    if ($time.Kind -ne [DateTimeKind]::Utc) { throw 'Mount command times must be explicit UTC values.' }
}
if ($MountCommandIssuedUtc -gt $MountCommandCompletedUtc) {
    throw 'Mount command completion precedes issue time.'
}
if (-not (Test-Path -LiteralPath $AstapPath -PathType Leaf)) { throw "ASTAP CLI is missing: $AstapPath" }
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null

$prefix = '{0}-{1}-{2}' -f $RunId.ToString('D'), $SequenceIndex, $PositionId
$pointPath = Join-Path $OutputDirectory "$prefix-point.json"
$fitsPath = Join-Path $OutputDirectory "$prefix-phd2.fits"
$astapBase = Join-Path $OutputDirectory "$prefix-astap"
$astapWcs = "$astapBase.wcs"
$astapIni = "$astapBase.ini"
$astapStdout = "$astapBase.stdout.txt"
$astapStderr = "$astapBase.stderr.txt"
foreach ($path in @($pointPath, $fitsPath, $astapWcs, $astapIni, $astapStdout, $astapStderr)) {
    if (Test-Path -LiteralPath $path) { throw "Create-new evidence path already exists: $path" }
}

$initialMount = Get-NinaMountInfo
Assert-MountStationary $initialMount
$connection = $null
$originalGuideOutput = $null
$guideOutputChanged = $false
$temporaryPhd2Image = $null
try {
    $connection = Connect-Phd2
    $state = (Invoke-Phd2 $connection 'get_app_state').result
    if ([string]$state -ne 'Stopped') { throw "PHD2 must be Stopped; current state is '$state'." }
    $connected = (Invoke-Phd2 $connection 'get_connected').result
    if (-not [bool]$connected) { throw 'PHD2 camera is not connected.' }
    $originalGuideOutput = [bool](Invoke-Phd2 $connection 'get_guide_output_enabled').result
    if ($originalGuideOutput) {
        [void](Invoke-Phd2 $connection 'set_guide_output_enabled' @($false))
        $guideOutputChanged = $true
    }
    if ([bool](Invoke-Phd2 $connection 'get_guide_output_enabled').result) {
        throw 'PHD2 guide output did not disable.'
    }

    [void](Invoke-Phd2 $connection 'capture_single_frame' @($ExposureMilliseconds) 45)
    $save = (Invoke-Phd2 $connection 'save_image' $null 15).result
    $temporaryPhd2Image = if ($save -is [string]) { $save } elseif ($save.PSObject.Properties.Name -contains 'filename') { [string]$save.filename } else { $null }
    if (-not $temporaryPhd2Image -or -not (Test-Path -LiteralPath $temporaryPhd2Image -PathType Leaf)) {
        throw 'PHD2 save_image did not return an existing FITS path.'
    }
    $source = [IO.File]::Open($temporaryPhd2Image, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $destination = [IO.File]::Open($fitsPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try { $source.CopyTo($destination) } finally { $destination.Dispose(); $source.Dispose() }

    $finalMount = Get-NinaMountInfo
    Assert-MountStationary $finalMount
    if ([Math]::Abs([double]$finalMount.Declination - [double]$initialMount.Declination) -gt 0.05) {
        throw 'Mount declination changed during the witness exposure.'
    }

    $fits = Read-FitsHeader $fitsPath
    if (-not $fits.Contains('DATE-OBS')) { throw 'PHD2 FITS is missing DATE-OBS.' }
    $dateObs = [DateTime]::Parse([string]$fits['DATE-OBS'], [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal)
    $exposureSeconds = if ($fits.Contains('EXPTIME')) { [double]$fits['EXPTIME'] } elseif ($fits.Contains('EXPOSURE')) { [double]$fits['EXPOSURE'] } else { $ExposureMilliseconds / 1000.0 }
    if ($exposureSeconds -le 0 -or [Math]::Abs($exposureSeconds * 1000.0 - $ExposureMilliseconds) -gt 1000.0) {
        throw 'PHD2 FITS exposure does not match the requested exposure.'
    }
    $observationUtc = $dateObs.AddSeconds($exposureSeconds / 2.0)
    if ($dateObs -lt $MountCommandCompletedUtc) { throw 'FITS exposure began before the mount command completed.' }

    $arguments = Get-BlindAstapArguments `
        $fitsPath $AstapFieldOfViewDegrees $astapBase
    $startProcess = @{
        FilePath = $AstapPath; ArgumentList = $arguments; Wait = $true;
        PassThru = $true; NoNewWindow = $true;
        RedirectStandardOutput = $astapStdout; RedirectStandardError = $astapStderr
    }
    $solver = Start-Process @startProcess
    if ($solver.ExitCode -ne 0) { throw "ASTAP exited with code $($solver.ExitCode)." }
    $solution = Get-AstapSolution $astapWcs $astapIni
    $vector = Convert-J2000ToTopocentricVector $solution.RightAscensionDegrees $solution.DeclinationDegrees $observationUtc $SiteLatitudeDegrees $SiteLongitudeDegrees $SiteElevationMeters
    $astapVersion = (& $AstapPath -h 2>&1 | Select-Object -First 1).ToString().Trim()

    $receipt = [ordered]@{
        schemaVersion = 3; runId = $RunId.ToString('D'); positionId = $PositionId; sequenceIndex = $SequenceIndex
        mountCommandId = $MountCommandId; mountCommandIssuedUtc = $MountCommandIssuedUtc.ToString('O')
        mountCommandCompletedUtc = $MountCommandCompletedUtc.ToString('O')
        commandedRightAscensionDegrees = $CommandedRightAscensionDegrees
        commandedDeclinationDegrees = $CommandedDeclinationDegrees
        trackingEnabled = $true; slewing = $false; phd2AppState = [string]$state; guideOutputEnabled = $false
        captureStartedUtc = $dateObs.ToString('O'); exposureSeconds = $exposureSeconds
        observationUtc = $observationUtc.ToString('O'); fitsDateObsUtc = $dateObs.ToString('O')
        fitsDateObsConvention = 'exposure-start'; fitsTimestampUncertaintyMilliseconds = $FitsTimestampUncertaintyMilliseconds
        sourceImageSha256 = Get-Sha256 $fitsPath; solverOutputSha256 = Get-Sha256 $astapWcs
        solverIdentity = $astapVersion; solverBinarySha256 = Get-Sha256 $AstapPath
        solverHintPolicy = 'blind-no-mount-hint'
        sourceCoordinateFrame = 'icrs-observation-epoch'
        siteLatitudeDegrees = $SiteLatitudeDegrees
        siteLongitudeDegrees = $SiteLongitudeDegrees
        siteElevationMeters = $SiteElevationMeters
        astapFieldOfViewDegrees = $AstapFieldOfViewDegrees
        mountAzimuthDegrees = [double]$finalMount.Azimuth; mountAltitudeDegrees = [double]$finalMount.Altitude
        solvedRightAscensionDegrees = $solution.RightAscensionDegrees
        solvedDeclinationDegrees = $solution.DeclinationDegrees; positionAngleDegrees = $solution.PositionAngleDegrees
        vectorX = $vector.X; vectorY = $vector.Y; vectorZ = $vector.Z; sideOfPier = [string]$finalMount.SideOfPier
    }
    Write-CreateNewUtf8 $pointPath ($receipt | ConvertTo-Json -Depth 8)
    [pscustomobject]@{ Status = 'captured'; PointReceiptPath = $pointPath; PointReceiptSha256 = Get-Sha256 $pointPath; GrantsMotionAuthority = $false } | ConvertTo-Json -Compress
} finally {
    if ($connection) {
        try {
            if ($null -ne $originalGuideOutput) {
                [void](Invoke-Phd2 $connection 'set_guide_output_enabled' @([bool]$originalGuideOutput) 8)
                $restored = [bool](Invoke-Phd2 $connection 'get_guide_output_enabled' $null 8).result
                if ($restored -ne [bool]$originalGuideOutput) { throw 'PHD2 guide-output restoration did not verify.' }
            }
        } finally { $connection.Client.Dispose() }
    }
    if ($temporaryPhd2Image -and (Test-Path -LiteralPath $temporaryPhd2Image) -and (Test-Path -LiteralPath $fitsPath)) {
        Remove-Item -LiteralPath $temporaryPhd2Image -Force
    }
}
