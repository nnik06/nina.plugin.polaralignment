param(
    [switch]$LibraryOnly,
    [Parameter(Mandatory = $true)]
    [string]$RequestPath,
    [Parameter(Mandatory = $true)]
    [string]$OutcomePath,
    [Parameter(Mandatory = $true)]
    [string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)]
    [string]$CaptureScriptPath,
    [Parameter(Mandatory = $true)]
    [string]$QualificationCliPath
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

function Convert-ExplicitUtc([object]$Value, [string]$Label) {
    $parsed = [DateTime]::Parse(
        [string]$Value,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind)
    if ($parsed.Kind -ne [DateTimeKind]::Utc) {
        throw "$Label must declare UTC with a Z suffix."
    }
    return $parsed
}

function Invoke-QualificationCli([string[]]$Arguments) {
    $output = @(& $QualificationCliPath @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    return [pscustomobject]@{
        ExitCode = $exitCode
        Text = ($output | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
    }
}

if ($LibraryOnly) { return }

foreach ($path in @($RequestPath, $CaptureScriptPath, $QualificationCliPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required observer input is missing: $path"
    }
}
[IO.Directory]::CreateDirectory($EvidenceDirectory) | Out-Null
if (Test-Path -LiteralPath $OutcomePath) {
    throw "Create-new outcome path already exists: $OutcomePath"
}

$request = [IO.File]::ReadAllText($RequestPath) | ConvertFrom-Json
$runId = [Guid]$request.runId
$positionId = [string]$request.positionId
$sequenceIndex = [int]$request.sequenceIndex
$requestDigest = [string]$request.requestDigest
$prefix = '{0}-{1}-{2}' -f $runId.ToString('D'), $sequenceIndex, $positionId
$attemptPath = Join-Path $EvidenceDirectory "$prefix-attempt.json"
$validationPath = Join-Path $EvidenceDirectory "$prefix-request-validation.json"
$captureLogPath = Join-Path $EvidenceDirectory "$prefix-capture-result.json"
$outcomeValidationPath = Join-Path $EvidenceDirectory "$prefix-outcome-validation.json"
$failurePath = Join-Path $EvidenceDirectory "$prefix-observer-failure.json"
$pointPath = Join-Path $EvidenceDirectory "$prefix-point.json"
foreach ($path in @($attemptPath, $validationPath, $captureLogPath,
        $outcomeValidationPath, $failurePath, $pointPath)) {
    if (Test-Path -LiteralPath $path) {
        throw "Create-new witness path already exists: $path"
    }
}

$nowUtc = [DateTime]::UtcNow
$requestValidation = Invoke-QualificationCli @(
    'validate-witness-request', '--request', $RequestPath,
    '--now-utc', $nowUtc.ToString('O'))
Write-CreateNewUtf8 $validationPath $requestValidation.Text
if ($requestValidation.ExitCode -ne 0) {
    throw "Witness request failed headless validation: $($requestValidation.Text)"
}

$observerDigest = Get-Sha256 $PSCommandPath
$captureDigest = Get-Sha256 $CaptureScriptPath
if ($observerDigest -ne ([string]$request.requiredObserverPipelineDigest).ToLowerInvariant()) {
    throw 'Observer script digest does not match the immutable request.'
}
if ($captureDigest -ne ([string]$request.requiredPointCapturePipelineDigest).ToLowerInvariant()) {
    throw 'Point-capture script digest does not match the immutable request.'
}
if ([int]$request.maximumAttempts -ne 1) {
    throw 'Observer accepts only a one-attempt request.'
}
$deadlineUtc = Convert-ExplicitUtc $request.deadlineUtc 'Request deadline'
$minimumRemainingSeconds = ([int]$request.exposureMilliseconds / 1000.0) + 20.0
if (($deadlineUtc - [DateTime]::UtcNow).TotalSeconds -lt $minimumRemainingSeconds) {
    throw 'Insufficient immutable request lifetime remains for one bounded capture and solve.'
}

$attempt = [ordered]@{
    schemaVersion = 1
    requestDigest = $requestDigest
    runId = $runId.ToString('D')
    positionId = $positionId
    sequenceIndex = $sequenceIndex
    attemptNumber = 1
    createdUtc = [DateTime]::UtcNow.ToString('O')
    observerPipelineDigest = $observerDigest
    pointCapturePipelineDigest = $captureDigest
    grantsMotionAuthority = $false
    grantsCompletionAuthority = $false
}
Write-CreateNewUtf8 $attemptPath ($attempt | ConvertTo-Json -Depth 5 -Compress)

$startedUtc = [DateTime]::UtcNow
try {
    $captureParameters = @{
        RunId = $runId
        PositionId = $positionId
        SequenceIndex = $sequenceIndex
        MountCommandId = [string]$request.mountCommandId
        MountCommandIssuedUtc = Convert-ExplicitUtc $request.mountCommandIssuedUtc 'Mount command issue time'
        MountCommandCompletedUtc = Convert-ExplicitUtc $request.mountCommandCompletedUtc 'Mount command completion time'
        CommandedRightAscensionDegrees = [double]$request.commandedRightAscensionDegrees
        CommandedDeclinationDegrees = [double]$request.commandedDeclinationDegrees
        ExpectedPierSide = [string]$request.expectedPierSide
        SiteLatitudeDegrees = [double]$request.siteLatitudeDegrees
        SiteLongitudeDegrees = [double]$request.siteLongitudeDegrees
        SiteElevationMeters = [double]$request.siteElevationMeters
        ExposureMilliseconds = [int]$request.exposureMilliseconds
        AstapFieldOfViewDegrees = [double]$request.astapFieldOfViewDegrees
        FitsTimestampUncertaintyMilliseconds = [double]$request.fitsTimestampUncertaintyMilliseconds
        OutputDirectory = $EvidenceDirectory
    }
    $captureResult = @(& $CaptureScriptPath @captureParameters 2>&1)
    Write-CreateNewUtf8 $captureLogPath (($captureResult |
        ForEach-Object { [string]$_ }) -join [Environment]::NewLine)
    if (-not (Test-Path -LiteralPath $pointPath -PathType Leaf)) {
        throw 'The single capture attempt did not create its point receipt.'
    }

    $completedUtc = [DateTime]::UtcNow
    if ($completedUtc -gt $deadlineUtc) {
        throw 'The single capture completed after the immutable request deadline.'
    }
    $createOutcome = Invoke-QualificationCli @(
        'create-witness-outcome',
        '--request', $RequestPath,
        '--point', $pointPath,
        '--outcome-out', $OutcomePath,
        '--started-utc', $startedUtc.ToString('O'),
        '--completed-utc', $completedUtc.ToString('O'),
        '--observer-pipeline-digest', $observerDigest)
    if ($createOutcome.ExitCode -ne 0) {
        throw "Headless core refused to create the outcome: $($createOutcome.Text)"
    }
    $outcomeValidation = Invoke-QualificationCli @(
        'validate-witness-outcome',
        '--request', $RequestPath,
        '--outcome', $OutcomePath)
    Write-CreateNewUtf8 $outcomeValidationPath $outcomeValidation.Text
    if ($outcomeValidation.ExitCode -ne 0) {
        throw "Created outcome failed independent validation: $($outcomeValidation.Text)"
    }
    [pscustomobject]@{
        Status = 'captured'
        RequestDigest = $requestDigest
        AttemptNumber = 1
        OutcomePath = [IO.Path]::GetFullPath($OutcomePath)
        OutcomeSha256 = Get-Sha256 $OutcomePath
        GrantsMotionAuthority = $false
        GrantsCompletionAuthority = $false
    } | ConvertTo-Json -Compress
} catch {
    $failure = [ordered]@{
        schemaVersion = 1
        requestDigest = $requestDigest
        attemptNumber = 1
        failedUtc = [DateTime]::UtcNow.ToString('O')
        issue = $_.Exception.Message
        grantsMotionAuthority = $false
        grantsCompletionAuthority = $false
    }
    Write-CreateNewUtf8 $failurePath ($failure | ConvertTo-Json -Compress)
    throw
}
