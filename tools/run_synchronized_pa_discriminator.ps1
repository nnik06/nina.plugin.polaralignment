param(
    [ValidateRange(600, 3600)]
    [int]$DurationSeconds = 1800,
    [ValidateRange(10, 60)]
    [int]$MainSolveCadenceSeconds = 30,
    [ValidateRange(0.5, 5.0)]
    [double]$MainExposureSeconds = 3.0,
    [ValidateRange(500, 5000)]
    [int]$Phd2ExposureMilliseconds = 1500,
    [ValidateRange(2000, 60000)]
    [int]$IPolarCadenceMilliseconds = 60000,
    [ValidateSet('Ordinary', 'Zoom')]
    [string]$IPolarDisplayMode = 'Ordinary',
    [Parameter(Mandatory = $true)]
    [ValidateRange(850.0, 1100.0)]
    [double]$PressureHpa,
    [Parameter(Mandatory = $true)]
    [ValidateRange(-20.0, 60.0)]
    [double]$TemperatureCelsius,
    [Parameter(Mandatory = $true)]
    [ValidateRange(0.0, 100.0)]
    [double]$RelativeHumidityPercent,
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$AtmosphereSource,
    [Parameter(Mandatory = $true)]
    [DateTimeOffset]$AtmosphereObservedUtc,
    [Parameter(Mandatory = $true)]
    [ValidateSet('East', 'West')]
    [string]$ExpectedPierSide,
    [ValidateRange(0.01, 1.0)]
    [double]$MaximumEquatorialDriftDegrees = 0.10,
    [ValidateRange(270.0, 300.0)]
    [double]$MinimumAzimuthDegrees = 270.0,
    [ValidateRange(270.0, 300.0)]
    [double]$MaximumAzimuthDegrees = 300.0,
    [ValidateRange(25.0, 55.0)]
    [double]$MinimumAltitudeDegrees = 25.0,
    [ValidateRange(25.0, 55.0)]
    [double]$MaximumAltitudeDegrees = 55.0,
    [string]$ClockReferenceHost = 'time.windows.com',
    [string]$NinaApiBase = 'http://localhost:1888/v2/api',
    [string]$PowerShellPath = 'pwsh.exe',
    [string]$OutputRoot = 'C:\Users\nnik0\Documents\TPPA-PHD2-tests'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

if ($MinimumAzimuthDegrees -gt $MaximumAzimuthDegrees -or
        $MinimumAltitudeDegrees -gt $MaximumAltitudeDegrees) {
    throw 'Synchronized-discriminator pointing limits are inverted.'
}
if ($AtmosphereObservedUtc.Offset -ne [TimeSpan]::Zero) {
    throw 'AtmosphereObservedUtc must be an explicit UTC value.'
}

$runId = 'synchronized-pa-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
$runDirectory = Join-Path $OutputRoot $runId
if (Test-Path -LiteralPath $runDirectory) {
    throw "Run directory already exists: $runDirectory"
}
[IO.Directory]::CreateDirectory($runDirectory) | Out-Null
$telemetryPath = Join-Path $runDirectory 'mount-telemetry.jsonl'
$manifestPath = Join-Path $runDirectory 'manifest.json'
$failurePath = Join-Path $runDirectory 'failure.json'
$mainRoot = Join-Path $runDirectory 'main-camera'
$phd2Root = Join-Path $runDirectory 'phd2'
$ipolarRoot = Join-Path $runDirectory 'ipolar'
foreach ($path in @($mainRoot, $phd2Root, $ipolarRoot)) {
    [IO.Directory]::CreateDirectory($path) | Out-Null
}

$script:Children = [System.Collections.Generic.List[Diagnostics.Process]]::new()
$script:Stopwatch = [Diagnostics.Stopwatch]::StartNew()

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

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-SignedCircularDegreesDelta([double]$Value, [double]$Reference) {
    $delta = (($Value - $Reference + 540.0) % 360.0) - 180.0
    if ($delta -le -180.0) { return 180.0 }
    $delta
}

function Get-Mount {
    $response = Invoke-RestMethod -Uri "$NinaApiBase/equipment/mount/info" -TimeoutSec 5
    if (-not $response.Success -or $null -eq $response.Response) {
        throw 'NINA mount telemetry is unavailable.'
    }
    $response.Response
}

function Assert-FixedMount($Mount, $InitialMount) {
    $azimuth = [double]$Mount.Azimuth
    $altitude = [double]$Mount.Altitude
    if (-not [bool]$Mount.Connected -or
            [bool]$Mount.Slewing -or
            -not [bool]$Mount.TrackingEnabled -or
            [bool]$Mount.AtPark -or
            [bool]$Mount.AtHome -or
            $azimuth -lt $MinimumAzimuthDegrees -or
            $azimuth -gt $MaximumAzimuthDegrees -or
            $altitude -lt $MinimumAltitudeDegrees -or
            $altitude -gt $MaximumAltitudeDegrees) {
        throw "Fixed-point mount gate failed: connected=$($Mount.Connected), slewing=$($Mount.Slewing), tracking=$($Mount.TrackingEnabled), parked=$($Mount.AtPark), home=$($Mount.AtHome), Az=$azimuth, Alt=$altitude."
    }
    if ([string]$Mount.SideOfPier -ne $ExpectedPierSide -or
            [string]$Mount.SideOfPier -ne [string]$InitialMount.SideOfPier) {
        throw "Fixed-point pier-side gate failed: expected=$ExpectedPierSide, initial=$($InitialMount.SideOfPier), current=$($Mount.SideOfPier)."
    }
    $raDeltaDegrees = [Math]::Abs((Get-SignedCircularDegreesDelta `
        (([double]$Mount.RightAscension) * 15.0) `
        (([double]$InitialMount.RightAscension) * 15.0)))
    $decDeltaDegrees = [Math]::Abs(
        [double]$Mount.Declination - [double]$InitialMount.Declination)
    if ($raDeltaDegrees -gt $MaximumEquatorialDriftDegrees -or
            $decDeltaDegrees -gt $MaximumEquatorialDriftDegrees) {
        throw "Fixed-point no-slew gate failed: dRA=$raDeltaDegrees deg, dDec=$decDeltaDegrees deg."
    }
}

function Get-ClockProbe([string]$Label) {
    $capturedUtc = [DateTime]::UtcNow
    $elapsed = $script:Stopwatch.Elapsed.TotalSeconds
    $raw = @()
    $exitCode = -1
    try {
        $raw = @(& w32tm.exe /stripchart "/computer:$ClockReferenceHost" /samples:1 /dataonly 2>&1)
        $exitCode = $LASTEXITCODE
    } catch {
        $raw = @("Clock probe failed without invalidating the evidence run: $($_.Exception.Message)")
    }
    $text = ($raw | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
    $offsetSeconds = $null
    $match = [regex]::Match($text, '([+-][0-9]+(?:[.][0-9]+)?)s')
    if ($match.Success) {
        $offsetSeconds = [double]::Parse(
            $match.Groups[1].Value,
            [Globalization.CultureInfo]::InvariantCulture)
    }
    [pscustomobject][ordered]@{
        Label = $Label
        CapturedUtc = $capturedUtc.ToString('o')
        MonotonicElapsedSeconds = $elapsed
        ReferenceHost = $ClockReferenceHost
        ExitCode = $exitCode
        OffsetSeconds = $offsetSeconds
        Qualified100Milliseconds = $null -ne $offsetSeconds -and [Math]::Abs($offsetSeconds) -le 0.1
        Raw = $text
    }
}

function Start-PassiveChild(
        [string]$Label,
        [string]$ScriptPath,
        [string[]]$Arguments) {
    if (-not (Test-Path -LiteralPath $ScriptPath -PathType Leaf)) {
        throw "$Label script is missing: $ScriptPath"
    }
    $stdout = Join-Path $runDirectory "$Label-stdout.log"
    $stderr = Join-Path $runDirectory "$Label-stderr.log"
    $argumentList = @('-NoProfile', '-File', $ScriptPath) + $Arguments
    $process = Start-Process `
        -FilePath $PowerShellPath `
        -ArgumentList $argumentList `
        -PassThru `
        -WindowStyle Hidden `
        -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr
    $script:Children.Add($process)
    [pscustomobject][ordered]@{
        Label = $Label
        Process = $process
        StartedUtc = [DateTime]::UtcNow
        MonotonicStartSeconds = $script:Stopwatch.Elapsed.TotalSeconds
        StdoutPath = $stdout
        StderrPath = $stderr
    }
}

function Get-ChildResult($Child) {
    $Child.Process.Refresh()
    [pscustomobject][ordered]@{
        Label = $Child.Label
        ProcessId = $Child.Process.Id
        StartedUtc = $Child.StartedUtc.ToString('o')
        MonotonicStartSeconds = $Child.MonotonicStartSeconds
        HasExited = $Child.Process.HasExited
        ExitCode = if ($Child.Process.HasExited) { $Child.Process.ExitCode } else { $null }
        StdoutPath = $Child.StdoutPath
        StderrPath = $Child.StderrPath
    }
}

function Find-SingleChildDirectory([string]$Root, [string]$Pattern) {
    $directories = @(Get-ChildItem -LiteralPath $Root -Directory -Filter $Pattern)
    if ($directories.Count -ne 1) {
        throw "Expected exactly one '$Pattern' directory under $Root; found $($directories.Count)."
    }
    $directories[0].FullName
}

function Read-Ndjson([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required NDJSON artifact is missing: $Path"
    }
    @([IO.File]::ReadAllLines($Path) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_ | ConvertFrom-Json })
}

function Read-CsvRows([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required CSV artifact is missing: $Path"
    }
    @(Import-Csv -LiteralPath $Path)
}

trap {
    $failure = [ordered]@{
        SchemaVersion = 1
        RunId = $runId
        FailedUtc = [DateTime]::UtcNow.ToString('o')
        MonotonicElapsedSeconds = $script:Stopwatch.Elapsed.TotalSeconds
        Error = $_.Exception.Message
        Stack = $_.ScriptStackTrace
        Children = @($script:Children | ForEach-Object {
            $_.Refresh()
            [ordered]@{
                ProcessId = $_.Id
                HasExited = $_.HasExited
                ExitCode = if ($_.HasExited) { $_.ExitCode } else { $null }
            }
        })
        GrantsMountMotionAuthority = $false
        GrantsUpasAuthority = $false
        GrantsAbsoluteAccuracyClaim = $false
    }
    try {
        if (-not (Test-Path -LiteralPath $failurePath)) {
            Write-CreateNewUtf8 $failurePath ($failure | ConvertTo-Json -Depth 8)
        }
    } catch { }
    throw
}

$camera = (Invoke-RestMethod -Uri "$NinaApiBase/equipment/camera/info" -TimeoutSec 5).Response
if ($null -eq $camera -or -not [bool]$camera.Connected -or [bool]$camera.IsExposing) {
    throw 'Main camera must be connected and idle.'
}
$initialMount = Get-Mount
Assert-FixedMount $initialMount $initialMount

$mainSamples = [Math]::Floor($DurationSeconds / $MainSolveCadenceSeconds)
if ($mainSamples -lt 3 -or $mainSamples -gt 60) {
    throw "Main solve schedule requires $mainSamples samples; supported range is 3..60."
}
$driftMinutes = $DurationSeconds / 60.0
$clockProbes = [System.Collections.Generic.List[object]]::new()
$clockProbes.Add((Get-ClockProbe 'start'))

$ipolarScript = Join-Path $PSScriptRoot 'ipolar_slew_capture.ps1'
$mainScript = Join-Path $PSScriptRoot 'run_guarded_static_solve_series.ps1'
$phd2Script = Join-Path $PSScriptRoot 'tppa_phd2_supervisor.ps1'

$ipolarChild = Start-PassiveChild 'ipolar' $ipolarScript @(
    '-RunId', $runId,
    '-DurationSeconds', [string]$DurationSeconds,
    '-CadenceMilliseconds', [string]$IPolarCadenceMilliseconds,
    '-DeclaredDisplayMode', $IPolarDisplayMode,
    '-Root', $ipolarRoot)
$mainChild = Start-PassiveChild 'main-camera' $mainScript @(
    '-Samples', [string]$mainSamples,
    '-CadenceSeconds', [string]$MainSolveCadenceSeconds,
    '-ExposureSeconds', $MainExposureSeconds.ToString([Globalization.CultureInfo]::InvariantCulture),
    '-MaxEquatorialDriftDegrees', $MaximumEquatorialDriftDegrees.ToString([Globalization.CultureInfo]::InvariantCulture),
    '-OutputRoot', $mainRoot)

$phd2Wrapper = Join-Path $runDirectory 'invoke-phd2-passive.ps1'
$phd2Invocation = @"
& '$phd2Script' ``
    -Mode Phd2Drift ``
    -DriftMinutes $($driftMinutes.ToString([Globalization.CultureInfo]::InvariantCulture)) ``
    -Phd2ExposureMs $Phd2ExposureMilliseconds ``
    -RequireRecentAutofocus `$false ``
    -RequirePdaNearPole `$false ``
    -Phd2CaptureAttempts 1 ``
    -OutputRoot '$phd2Root'
if (-not `$?) { exit 1 }
exit 0
"@
Write-CreateNewUtf8 $phd2Wrapper $phd2Invocation
$phd2Child = Start-PassiveChild 'phd2' $phd2Wrapper @()
$children = @($ipolarChild, $mainChild, $phd2Child)

$midpointCaptured = $false
$deadline = [DateTime]::UtcNow.AddSeconds($DurationSeconds + 180)
while ([DateTime]::UtcNow -lt $deadline) {
    $sampleStarted = $script:Stopwatch.Elapsed.TotalSeconds
    $mount = Get-Mount
    Assert-FixedMount $mount $initialMount
    [ordered]@{
        CapturedUtc = [DateTime]::UtcNow.ToString('o')
        MonotonicElapsedSeconds = $sampleStarted
        Connected = [bool]$mount.Connected
        Slewing = [bool]$mount.Slewing
        TrackingEnabled = [bool]$mount.TrackingEnabled
        TrackingMode = [string]$mount.TrackingMode
        SideOfPier = [string]$mount.SideOfPier
        RightAscensionHours = [double]$mount.RightAscension
        DeclinationDegrees = [double]$mount.Declination
        AzimuthDegrees = [double]$mount.Azimuth
        AltitudeDegrees = [double]$mount.Altitude
    } | ConvertTo-Json -Compress | Add-Content -LiteralPath $telemetryPath -Encoding utf8

    if (-not $midpointCaptured -and
            $script:Stopwatch.Elapsed.TotalSeconds -ge $DurationSeconds / 2.0) {
        $clockProbes.Add((Get-ClockProbe 'midpoint'))
        $midpointCaptured = $true
    }
    $allExited = $true
    foreach ($child in $children) {
        $child.Process.Refresh()
        if (-not $child.Process.HasExited) { $allExited = $false }
    }
    if ($allExited) { break }
    $remainingMilliseconds = 1000.0 -
        (($script:Stopwatch.Elapsed.TotalSeconds - $sampleStarted) * 1000.0)
    if ($remainingMilliseconds -gt 0) {
        Start-Sleep -Milliseconds ([int][Math]::Round($remainingMilliseconds))
    }
}

foreach ($child in $children) {
    $child.Process.Refresh()
    if (-not $child.Process.HasExited) {
        throw "$($child.Label) recorder exceeded its bounded runtime; process $($child.Process.Id) remains running for operator recovery."
    }
    if ($child.Process.ExitCode -ne 0) {
        throw "$($child.Label) recorder failed with exit code $($child.Process.ExitCode)."
    }
}
$clockProbes.Add((Get-ClockProbe 'end'))

$mainDirectory = Find-SingleChildDirectory $mainRoot 'static-solves-*'
$phd2Directory = Find-SingleChildDirectory $phd2Root 'run-*'
$mainRows = Read-Ndjson (Join-Path $mainDirectory 'samples.jsonl')
$ipolarRows = Read-Ndjson (Join-Path (Join-Path $ipolarRoot $runId) 'samples.jsonl')
$phd2Csv = @(Get-ChildItem -LiteralPath $phd2Directory -Filter '*-phd2-guidesteps.csv')
if ($phd2Csv.Count -ne 1) {
    throw "Expected exactly one PHD2 guidestep CSV; found $($phd2Csv.Count)."
}
$phd2Rows = Read-CsvRows $phd2Csv[0].FullName
$telemetryRows = Read-Ndjson $telemetryPath

$minimumFraction = 0.80
$expectedIPolarSamples = [Math]::Floor(
    $DurationSeconds * 1000.0 / $IPolarCadenceMilliseconds)
$expectedPhd2Samples = [Math]::Floor(
    $DurationSeconds * 1000.0 / $Phd2ExposureMilliseconds)
$coverageQualified =
    $mainRows.Count -ge [Math]::Floor($mainSamples * $minimumFraction) -and
    $ipolarRows.Count -ge [Math]::Floor($expectedIPolarSamples * $minimumFraction) -and
    $phd2Rows.Count -ge [Math]::Floor($expectedPhd2Samples * $minimumFraction) -and
    $telemetryRows.Count -ge [Math]::Floor($DurationSeconds * $minimumFraction)
if (-not $coverageQualified) {
    throw "Synchronized evidence coverage is incomplete: main=$($mainRows.Count)/$mainSamples, iPolar=$($ipolarRows.Count)/$expectedIPolarSamples, PHD2=$($phd2Rows.Count)/$expectedPhd2Samples, telemetry=$($telemetryRows.Count)/$DurationSeconds."
}

$artifacts = @(Get-ChildItem -LiteralPath $runDirectory -Recurse -File |
    Where-Object { $_.FullName -ne $manifestPath } |
    Sort-Object FullName |
    ForEach-Object {
        [ordered]@{
            RelativePath = [IO.Path]::GetRelativePath($runDirectory, $_.FullName)
            LengthBytes = $_.Length
            Sha256 = Get-Sha256 $_.FullName
        }
    })
$manifest = [ordered]@{
    SchemaVersion = 1
    RunId = $runId
    StartedUtc = $clockProbes[0].CapturedUtc
    CompletedUtc = [DateTime]::UtcNow.ToString('o')
    MonotonicDurationSeconds = $script:Stopwatch.Elapsed.TotalSeconds
    RequestedDurationSeconds = $DurationSeconds
    InitialMount = $initialMount
    ExpectedPierSide = $ExpectedPierSide
    Atmosphere = [ordered]@{
        Source = $AtmosphereSource
        ObservedUtc = $AtmosphereObservedUtc.UtcDateTime.ToString('o')
        PressureHpa = $PressureHpa
        TemperatureCelsius = $TemperatureCelsius
        RelativeHumidityPercent = $RelativeHumidityPercent
    }
    ClockProbes = @($clockProbes)
    ClockQualified100Milliseconds =
        @($clockProbes | Where-Object { -not $_.Qualified100Milliseconds }).Count -eq 0
    ChildResults = @($children | ForEach-Object { Get-ChildResult $_ })
    EvidenceCounts = [ordered]@{
        MainCameraSolves = $mainRows.Count
        Phd2GuideSteps = $phd2Rows.Count
        IPolarWindowFrames = $ipolarRows.Count
        MountTelemetrySamples = $telemetryRows.Count
    }
    IPolarEvidenceKind = 'processed-window-capture-not-raw-sensor'
    CoverageQualified = $coverageQualified
    Artifacts = $artifacts
    GrantsMountMotionAuthority = $false
    GrantsUpasAuthority = $false
    GrantsAbsoluteAccuracyClaim = $false
}
Write-CreateNewUtf8 $manifestPath ($manifest | ConvertTo-Json -Depth 10)
Write-Host "Synchronized passive PA discriminator complete: $runDirectory"
