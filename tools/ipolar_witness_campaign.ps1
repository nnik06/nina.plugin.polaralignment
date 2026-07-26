<#
.SYNOPSIS
    Offline recorder and qualifier for iOptron iPolar witness campaigns.

.DESCRIPTION
    Builds an append-only, hash-chained evidence log for the iPolar side of the
    TPPA absolute-accuracy campaign, then evaluates it against explicit gates and
    emits JSON plus Markdown reports.

    iPolar is a diagnostic witness. This tool contains no actuator path. It never
    contacts NINA, PHD2, the mount, UPASBridge, a Switch, a Weather device, a
    Safety Monitor, or the vendor application, and it never clicks vendor UI or
    derives a measurement from screen pixels or OCR.

    Every command appends one event to <CampaignPath>\events.jsonl. Each event
    carries its sequence number, UTC timestamp, the hash of its predecessor, and
    its own content hash, so a later edit to any earlier event is detectable.

.PARAMETER Command
    Campaign operation to perform. Use Help for the command reference.

.PARAMETER CampaignPath
    Campaign directory. Init creates it; every other command requires it to exist.

.PARAMETER RecordedUtc
    Overrides the event timestamp. Use when transcribing an observation that was
    made earlier. Event time may never move backwards.

.PARAMETER PolicyPath
    Explicit uncertainty policy JSON for Evaluate and Finalize. Without it a
    campaign can never exceed QuantitativeUnqualified, because iPolar readout
    uncertainty has never been characterized.

.EXAMPLE
    .\tools\ipolar_witness_campaign.ps1 -Command Help

.EXAMPLE
    .\tools\ipolar_witness_campaign.ps1 -Command Init -CampaignPath .\campaigns\ipolar-001 `
        -CampaignId ipolar-001 -MountIdentifier HAE29C-EC -IPolarIdentifier ipolar-ext-01 `
        -MountingStateId mount-state-a -PoleConvention TruePole -DeployedPluginPath "$env:LOCALAPPDATA\NINA\Plugins\3.0.0\Three Point Polar Alignment\NINA.Plugins.PolarAlignment.dll"

.EXAMPLE
    .\tools\ipolar_witness_campaign.ps1 -Command RecordSolveAttempt -CampaignPath .\campaigns\ipolar-001 `
        -SolveOutcome Success -AvailableStarCount 14 -Reason "Pole region clear"

.EXAMPLE
    .\tools\ipolar_witness_campaign.ps1 -Command Finalize -CampaignPath .\campaigns\ipolar-001 -PolicyPath .\policy.json
#>
param(
    [Parameter(Mandatory)]
    [ValidateSet("Help", "Init", "RecordEnvironment", "RecordDarkFrame", "RecordSolveAttempt", "RecordCalibration",
        "RecordReseat", "RecordArtifact", "LinkTppaArtifact", "RecordBlock", "Evaluate", "Finalize")]
    [string]$Command,

    [string]$CampaignPath = "",
    [datetime]$RecordedUtc = [datetime]::MinValue,

    # Init
    [string]$CampaignId = "",
    [string]$RepositoryCommit = "",
    [string]$DeployedPluginSha256 = "",
    [string]$DeployedPluginPath = "",
    [string]$MountIdentifier = "",
    [string]$IPolarIdentifier = "",
    [string]$IPolarHardwareVariant = "",
    [string]$IPolarAdapter = "",
    [string]$IPolarSoftwareVersion = "",
    [string]$MountingStateId = "",
    [ValidateSet("TruePole", "ApparentPole", "Unknown")]
    [string]$PoleConvention = "Unknown",
    [string]$RequestedQualificationLevel = "Unspecified",

    # RecordEnvironment
    [nullable[double]]$LatitudeDegrees = $null,
    [nullable[double]]$LongitudeDegrees = $null,
    [nullable[double]]$ElevationMeters = $null,
    [string]$SiteSource = "",
    [nullable[double]]$AbsoluteStationPressureHectopascals = $null,
    [nullable[double]]$TemperatureCelsius = $null,
    [nullable[double]]$RelativeHumidityPercent = $null,
    [string]$AtmosphereSource = "",
    [datetime]$AtmosphereObservedUtc = [datetime]::MinValue,

    # RecordDarkFrame / RecordArtifact / LinkTppaArtifact
    [string]$Path = "",
    [datetime]$CapturedUtc = [datetime]::MinValue,
    [datetime]$CreatedUtc = [datetime]::MinValue,
    [ValidateSet("Screenshot", "RawFrame")]
    [string]$ArtifactKind = "Screenshot",
    [ValidateSet("CrossInsideCircle", "CrossOutsideCircle", "Indeterminate")]
    [string]$QualitativeVerdict = "Indeterminate",
    [ValidateRange(1.0, 100000.0)]
    [double]$MaximumArtifactAgeMinutes = 240.0,
    [string]$TppaRunId = "",
    [string]$Description = "",

    # RecordSolveAttempt
    [ValidateSet("Success", "Failure")]
    [string]$SolveOutcome = "",
    [nullable[int]]$AvailableStarCount = $null,
    [string]$Reason = "",

    # RecordCalibration / RecordReseat
    [nullable[int]]$CycleIndex = $null,
    [double[]]$RaPositionsDegrees = @(),
    [switch]$CameraRemovedOrReseated,
    [switch]$RecalibrationPerformed,
    [string]$MountingStateIdBefore = "",
    [string]$MountingStateIdAfter = "",
    [nullable[double]]$ResidualArcsec = $null,
    [string]$ResidualSource = "",
    [ValidateSet("VendorNumericExport", "DocumentedManualReadout", "Ocr", "ScreenshotPixelMeasurement", "Unknown")]
    [string]$ResidualSourceKind = "Unknown",
    [switch]$ResidualIsManual,
    [string]$ManualObservation = "",
    [string]$Notes = "",

    # RecordBlock
    [string]$BlockId = "",
    [datetime]$BlockStartUtc = [datetime]::MinValue,
    [datetime]$BlockEndUtc = [datetime]::MinValue,
    [string[]]$BlockLegs = @(),

    # Evaluate / Finalize
    [string]$PolicyPath = "",
    [string]$OutputPath = "",
    [string]$ReportDirectory = "",

    # Concurrency
    [ValidateRange(1.0, 600.0)]
    [double]$LockTimeoutSeconds = 30.0
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "ipolar_campaign_evaluator.ps1")

# --- Helpers ---------------------------------------------------------------------

function Test-SuppliedDate {
    param([datetime]$Value)
    return ($Value -ne [datetime]::MinValue)
}

function Resolve-EventUtc {
    if (Test-SuppliedDate -Value $RecordedUtc) { return (ConvertTo-IPolarUtc -Value $RecordedUtc) }
    return [datetime]::UtcNow
}

function Assert-CampaignPath {
    if ([string]::IsNullOrWhiteSpace($CampaignPath)) { throw "-CampaignPath is required for command '$Command'." }
    return [IO.Path]::GetFullPath($CampaignPath)
}

function Assert-IPolarCampaignId {
    param([Parameter(Mandatory)] [string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "-CampaignId is required."
    }
    if ($Value.Length -gt 100) {
        throw "-CampaignId must be 100 characters or fewer."
    }
    if ($Value.Contains("/") -or $Value.Contains("\")) {
        throw "-CampaignId '$Value' must not contain a path separator."
    }
    if ($Value -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$' -or $Value -eq "." -or $Value -eq "..") {
        throw "-CampaignId '$Value' is not a safe file name. Use 1-100 ASCII letters, digits, dots, underscores, or hyphens, beginning with a letter or digit."
    }
    if ($Value.EndsWith(".")) {
        throw "-CampaignId '$Value' must not end with a dot."
    }
    $windowsStem = $Value.Split(".")[0]
    if ($windowsStem -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$') {
        throw "-CampaignId '$Value' uses a reserved Windows device name."
    }
    return $Value
}

function Assert-ExistingCampaign {
    $root = Assert-CampaignPath
    if (-not (Test-Path -LiteralPath (Get-IPolarCampaignHeaderPath -CampaignPath $root) -PathType Leaf)) {
        throw "Campaign not initialized at $root. Run -Command Init first."
    }
    return $root
}

function Get-FileSha256 {
    param([Parameter(Mandatory)] [string]$LiteralPath)
    return (Get-FileHash -LiteralPath $LiteralPath -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-Utf8NoBom {
    param([Parameter(Mandatory)] [string]$LiteralPath, [Parameter(Mandatory)] [string]$Content)
    [IO.File]::WriteAllText($LiteralPath, $Content, [Text.UTF8Encoding]::new($false))
}

<#
.SYNOPSIS
    Appends one UTF-8 line without a BOM and flushes it to the storage device.
.DESCRIPTION
    Bytes are written directly rather than through AppendAllText so the encoding
    can never acquire a BOM, and Flush($true) forces the write past the OS cache
    so a crash cannot leave a torn event line behind.
#>
function Add-Utf8LineDurable {
    param([Parameter(Mandatory)] [string]$LiteralPath, [Parameter(Mandatory)] [string]$Line)

    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Line + "`n")
    $stream = [IO.File]::Open($LiteralPath, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try {
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
    } finally {
        $stream.Dispose()
    }
}

function Get-CampaignLockPath {
    param([Parameter(Mandatory)] [string]$Root)
    return (Join-Path $Root ".campaign.lock")
}

<#
.SYNOPSIS
    Acquires the campaign-scoped interprocess mutation lock.
.DESCRIPTION
    Opening the lock file with FileShare::None gives a lock that is honoured across
    processes and is released by the operating system even if a writer is killed,
    which a marker file could not guarantee. Acquisition is bounded; a writer that
    cannot get in fails with a clear error rather than corrupting the chain.
#>
function Enter-CampaignLock {
    param([Parameter(Mandatory)] [string]$Root, [double]$TimeoutSeconds = 30.0)

    [void][IO.Directory]::CreateDirectory($Root)
    $lockPath = Get-CampaignLockPath -Root $Root
    $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSeconds)
    $attempt = 0
    while ($true) {
        try {
            return [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        } catch [IO.IOException] {
            if ([datetime]::UtcNow -ge $deadline) {
                throw "Timed out after $TimeoutSeconds seconds waiting for the campaign lock at $lockPath. Another campaign command is still running. Retry, or raise -LockTimeoutSeconds."
            }
            $attempt++
            # Small randomised backoff so many contenders do not retry in lockstep.
            Start-Sleep -Milliseconds (15 + (Get-Random -Minimum 0 -Maximum 40))
        }
    }
}

<#
.SYNOPSIS
    Runs one campaign mutation under the campaign lock.
.DESCRIPTION
    Every state change - artifact reservation, artifact copy, event append, and
    finalization - runs inside a single lock scope, so campaign state read at the
    start of the scope is still authoritative at the moment of append.
#>
function Invoke-CampaignMutation {
    param([Parameter(Mandatory)] [string]$Root, [Parameter(Mandatory)] [scriptblock]$Body)

    $lock = Enter-CampaignLock -Root $Root -TimeoutSeconds $LockTimeoutSeconds
    try {
        return (& $Body)
    } finally {
        $lock.Dispose()
    }
}

function Write-Utf8NoBomOnce {
    param([Parameter(Mandatory)] [string]$LiteralPath, [Parameter(Mandatory)] [string]$Content)

    if (Test-Path -LiteralPath $LiteralPath) {
        $existing = [IO.File]::ReadAllText($LiteralPath)
        if ($existing -cne $Content) {
            throw "Existing finalized output does not match the reconstructed content at $LiteralPath. Refusing to overwrite it."
        }
        return
    }

    Write-Utf8NoBom -LiteralPath $LiteralPath -Content $Content
}

<#
.SYNOPSIS
    Normalizes a payload through the JSON reader so its stored representation and
    its hashed representation are provably identical.
#>
function ConvertTo-StoredPayload {
    param($Payload)

    $json = $Payload | ConvertTo-Json -Depth 12 -Compress
    return ($json | ConvertFrom-Json)
}

function Get-CampaignState {
    param([Parameter(Mandatory)] [string]$Root)

    $campaign = Read-IPolarCampaign -CampaignPath $Root
    $lastHash = Get-IPolarGenesisHash -Header $campaign.Header
    $lastUtc = $null
    $sequence = 0
    foreach ($campaignEvent in $campaign.Events) {
        $sequence++
        $lastHash = [string](Get-IPolarProperty -InputObject $campaignEvent -Name "EventHash")
        try { $lastUtc = ConvertTo-IPolarUtc -Value (Get-IPolarProperty -InputObject $campaignEvent -Name "RecordedUtc") } catch { }
    }
    return [pscustomobject]@{
        Campaign = $campaign
        NextSequence = $sequence + 1
        PreviousEventHash = $lastHash
        LastRecordedUtc = $lastUtc
    }
}

<#
.SYNOPSIS
    Validates campaign state and rejects an append that must not happen.
.DESCRIPTION
    Callers run this before committing any preserved evidence, so a rejected event
    never leaves a partially imported artifact behind. Must be called under the
    campaign lock; the state it returns is only authoritative while the lock is
    held.
#>
function Assert-AppendableCampaignState {
    param(
        [Parameter(Mandatory)] [string]$Root,
        [Parameter(Mandatory)] [string]$EventType,
        [Parameter(Mandatory)] [datetime]$EventUtc
    )

    $state = Get-CampaignState -Root $Root

    $finalized = @(Get-IPolarEventsOfType -Events $state.Campaign.Events -EventType "CampaignFinalized")
    if ($finalized.Count -gt 0 -and $EventType -ne "CampaignFinalized") {
        $finalizedUtc = Format-IPolarUtc -Value (Get-IPolarProperty -InputObject $finalized[0] -Name "RecordedUtc")
        throw "Campaign was finalized at $finalizedUtc (sequence $(Get-IPolarProperty -InputObject $finalized[0] -Name 'Sequence')). Finalization is terminal: '$EventType' and every other command is refused afterwards."
    }
    if ($finalized.Count -gt 0 -and $EventType -eq "CampaignFinalized") {
        throw "Campaign already contains a CampaignFinalized event. Finalization may occur exactly once."
    }

    if ($null -ne $state.LastRecordedUtc -and $EventUtc -lt $state.LastRecordedUtc) {
        throw "Refusing to append '$EventType' at $(Format-IPolarUtc -Value $EventUtc): the previous event is timestamped $(Format-IPolarUtc -Value $state.LastRecordedUtc). Campaign event time must not move backwards."
    }

    return $state
}

<#
.SYNOPSIS
    Appends one hash-chained event to the campaign log.
.DESCRIPTION
    Must be called under the campaign lock. Re-reads and revalidates campaign state
    immediately before the append so the sequence number and predecessor hash are
    the live ones, never a stale snapshot.
#>
function Add-CampaignEvent {
    param(
        [Parameter(Mandatory)] [string]$Root,
        [Parameter(Mandatory)] [string]$EventType,
        [Parameter(Mandatory)] $Payload,
        [Parameter(Mandatory)] [datetime]$EventUtc,
        [nullable[int]]$ExpectedSequence = $null
    )

    $state = Assert-AppendableCampaignState -Root $Root -EventType $EventType -EventUtc $EventUtc

    if ($null -ne $ExpectedSequence -and $state.NextSequence -ne [int]$ExpectedSequence) {
        throw "Campaign state changed between artifact reservation (sequence $([int]$ExpectedSequence)) and append (sequence $($state.NextSequence)). Refusing to append."
    }

    $storedPayload = ConvertTo-StoredPayload -Payload $Payload
    $record = [ordered]@{
        Sequence = $state.NextSequence
        RecordedUtc = Format-IPolarUtc -Value $EventUtc
        EventType = $EventType
        PreviousEventHash = $state.PreviousEventHash
        Payload = $storedPayload
    }
    $record.EventHash = Get-IPolarEventHash -Event $record

    $line = $record | ConvertTo-Json -Depth 12 -Compress
    Add-Utf8LineDurable -LiteralPath (Get-IPolarCampaignEventLogPath -CampaignPath $Root) -Line $line
    return $record
}

<#
.SYNOPSIS
    Validates and stages an evidence file without committing it.
.DESCRIPTION
    Rejects a missing, empty, duplicated, reused, or stale file, and refuses to
    overwrite an existing preserved copy. The operator's original is left
    untouched.

    The preserved copy is written to a private staging file whose name is unique to
    this invocation, and the staged bytes are rehashed and restatted before the
    reservation is returned. Nothing appears at the committed 000N path until
    Complete-CampaignArtifactReservation promotes it, which happens only after the
    event has been appended. A rejected or interrupted import therefore leaves no
    file at the committed path and never blocks a corrected retry.
#>
function New-CampaignArtifactReservation {
    param(
        [Parameter(Mandatory)] [string]$Root,
        [Parameter(Mandatory)] [string]$SourcePath,
        [Parameter(Mandatory)] [int]$Sequence,
        [Parameter(Mandatory)] [datetime]$EventUtc,
        [datetime]$DeclaredCreatedUtc = [datetime]::MinValue,
        [switch]$PreserveCopy
    )

    $full = [IO.Path]::GetFullPath($SourcePath)
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "Evidence file not found: $full" }

    $info = Get-Item -LiteralPath $full
    if ($info.Length -le 0) { throw "Evidence file is empty and cannot be recorded: $full" }

    if (Test-SuppliedDate -Value $DeclaredCreatedUtc) {
        $createdUtc = ConvertTo-IPolarUtc -Value $DeclaredCreatedUtc
    } else {
        $createdUtc = ConvertTo-IPolarUtc -Value $info.LastWriteTimeUtc
    }

    $ageMinutes = ($EventUtc - $createdUtc).TotalMinutes
    if ($ageMinutes -gt $MaximumArtifactAgeMinutes) {
        throw "Evidence file $full is $([Math]::Round($ageMinutes, 1)) minutes old, beyond the $MaximumArtifactAgeMinutes minute limit. Recording a stale capture as fresh evidence is not permitted."
    }
    if ($ageMinutes -lt 0.0) {
        throw "Evidence file $full is timestamped after the event that records it."
    }

    $sha = Get-FileSha256 -LiteralPath $full

    $campaign = Read-IPolarCampaign -CampaignPath $Root
    $previousCreatedUtc = $null
    foreach ($campaignEvent in $campaign.Events) {
        $type = [string](Get-IPolarProperty -InputObject $campaignEvent -Name "EventType")
        if ($type -ne "Artifact" -and $type -ne "DarkFrame" -and $type -ne "TppaArtifactLink") { continue }
        $payload = Get-IPolarProperty -InputObject $campaignEvent -Name "Payload"
        $existingSha = [string](Get-IPolarProperty -InputObject $payload -Name "Sha256")
        if ($existingSha -eq $sha) {
            throw "Evidence file $full has the same SHA256 as the file already recorded at sequence $(Get-IPolarProperty -InputObject $campaignEvent -Name 'Sequence'). A reused capture cannot serve as a second independent observation."
        }
        if ($type -eq "TppaArtifactLink") { continue }
        $existingCreated = Get-IPolarProperty -InputObject $payload -Name "CreatedUtc"
        if ($null -ne $existingCreated) {
            try {
                $parsed = ConvertTo-IPolarUtc -Value $existingCreated
                if ($null -eq $previousCreatedUtc -or $parsed -gt $previousCreatedUtc) { $previousCreatedUtc = $parsed }
            } catch { }
        }
    }
    if ($null -ne $previousCreatedUtc -and $createdUtc -le $previousCreatedUtc) {
        throw "Evidence file $full was created at $(Format-IPolarUtc -Value $createdUtc), which is not after the preceding artifact at $(Format-IPolarUtc -Value $previousCreatedUtc). Each capture must be provably newer than the one before it."
    }

    $storedPath = $full
    $stagingPath = $null
    if ($PreserveCopy.IsPresent) {
        $artifactDirectory = Join-Path $Root "artifacts"
        [void][IO.Directory]::CreateDirectory($artifactDirectory)
        $destination = Join-Path $artifactDirectory ("{0:D4}-{1}" -f $Sequence, $info.Name)
        if (Test-Path -LiteralPath $destination) {
            throw "Preserved evidence already exists at $destination. Campaign evidence is never overwritten."
        }

        $stagingPath = Join-Path $artifactDirectory (".staging-" + [guid]::NewGuid().ToString("N") + ".part")
        Copy-Item -LiteralPath $full -Destination $stagingPath
        Assert-PreservedBytes -LiteralPath $stagingPath -ExpectedSha256 $sha -ExpectedSizeBytes ([long]$info.Length) -Stage "staged copy"
        $storedPath = $destination
    }

    return [pscustomobject]@{
        SourcePath = $full
        StoredPath = $storedPath
        StagingPath = $stagingPath
        Sha256 = $sha
        SizeBytes = [long]$info.Length
        CreatedUtc = Format-IPolarUtc -Value $createdUtc
        Promoted = $false
    }
}

<#
.SYNOPSIS
    Confirms that bytes at a path match an expected SHA256 and size.
#>
function Assert-PreservedBytes {
    param(
        [Parameter(Mandatory)] [string]$LiteralPath,
        [Parameter(Mandatory)] [string]$ExpectedSha256,
        [Parameter(Mandatory)] [long]$ExpectedSizeBytes,
        [Parameter(Mandatory)] [string]$Stage
    )

    if (-not (Test-Path -LiteralPath $LiteralPath -PathType Leaf)) {
        throw "Preserved evidence verification failed: the $Stage at $LiteralPath does not exist."
    }
    $actualSize = [long](Get-Item -LiteralPath $LiteralPath).Length
    if ($actualSize -ne $ExpectedSizeBytes) {
        throw "Preserved evidence verification failed: the $Stage at $LiteralPath is $actualSize bytes but the source is $ExpectedSizeBytes bytes."
    }
    $actualSha = Get-FileSha256 -LiteralPath $LiteralPath
    if ($actualSha -ne $ExpectedSha256) {
        throw "Preserved evidence verification failed: the $Stage at $LiteralPath hashes to $actualSha but the source hashes to $ExpectedSha256."
    }
}

<#
.SYNOPSIS
    Promotes a staged evidence copy to its committed path and reverifies it.
.DESCRIPTION
    Called only after the corresponding event has been appended, so the committed
    000N path is created only for evidence that a recorded event refers to.
#>
function Complete-CampaignArtifactReservation {
    param([Parameter(Mandatory)] $Reservation)

    if ([string]::IsNullOrEmpty($Reservation.StagingPath)) { return }
    if (Test-Path -LiteralPath $Reservation.StoredPath) {
        throw "Preserved evidence already exists at $($Reservation.StoredPath). Campaign evidence is never overwritten."
    }
    [IO.File]::Move($Reservation.StagingPath, $Reservation.StoredPath)
    $Reservation.Promoted = $true
    Assert-PreservedBytes -LiteralPath $Reservation.StoredPath -ExpectedSha256 $Reservation.Sha256 -ExpectedSizeBytes $Reservation.SizeBytes -Stage "preserved copy"
}

<#
.SYNOPSIS
    Discards this invocation's staging file, and only that file.
.DESCRIPTION
    Committed evidence is never touched. Once a reservation has been promoted this
    is a no-op, because the file is then real campaign evidence.
#>
function Remove-CampaignArtifactReservation {
    param([Parameter(Mandatory)] $Reservation)

    if ($Reservation.Promoted) { return }
    if ([string]::IsNullOrEmpty($Reservation.StagingPath)) { return }
    if (Test-Path -LiteralPath $Reservation.StagingPath) {
        Remove-Item -LiteralPath $Reservation.StagingPath -Force -ErrorAction SilentlyContinue
    }
}

function New-ResidualRecord {
    if ($null -eq $ResidualArcsec) { return $null }
    $value = Get-IPolarFiniteNumber $ResidualArcsec
    if ($null -eq $value) { throw "-ResidualArcsec must be a finite number. An unknown residual must be omitted, never recorded as zero." }
    if ([string]::IsNullOrWhiteSpace($ResidualSource)) { throw "-ResidualSource is required whenever -ResidualArcsec is supplied. An undocumented number is not evidence." }
    if ($ResidualSourceKind -eq "Unknown") { throw "-ResidualSourceKind is required whenever -ResidualArcsec is supplied." }

    $isManual = $ResidualIsManual.IsPresent
    if ($ResidualSourceKind -eq "DocumentedManualReadout") { $isManual = $true }

    return [ordered]@{
        ValueArcsec = $value
        Source = $ResidualSource
        SourceKind = $ResidualSourceKind
        IsManual = $isManual
    }
}

function Get-RepositoryCommit {
    if (-not [string]::IsNullOrWhiteSpace($RepositoryCommit)) { return $RepositoryCommit }
    try {
        $repositoryRoot = Split-Path -Parent $PSScriptRoot
        $commit = (& git -C $repositoryRoot rev-parse HEAD 2>$null)
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($commit)) { return $commit.Trim() }
    } catch { }
    return ""
}

function Assert-BlockLegSequence {
    param([string[]]$Legs)

    if ($Legs.Count -lt 5) {
        throw "A no-motion block needs at least five legs in the order iPolar, TPPA A, TPPA B, TPPA A, iPolar. Received $($Legs.Count)."
    }
    if ($Legs[0] -ne "iPolar" -or $Legs[$Legs.Count - 1] -ne "iPolar") {
        throw "A no-motion block must open and close with an iPolar leg so the witness brackets the TPPA measurements."
    }
    $interior = @($Legs[1..($Legs.Count - 2)])
    if ($interior.Count % 2 -eq 0) {
        throw "The TPPA legs inside a no-motion block must alternate A, B, A, so their count must be odd. Received $($interior.Count)."
    }
    for ($index = 0; $index -lt $interior.Count; $index++) {
        $expected = if ($index % 2 -eq 0) { "TPPA-A" } else { "TPPA-B" }
        if ($interior[$index] -ne $expected) {
            throw "Leg $($index + 2) of the block is '$($interior[$index])' but the A/B/A protocol requires '$expected'."
        }
    }
}

function Show-CommandHelp {
    $lines = @(
        "iPolar witness campaign recorder - offline, read-only with respect to all hardware.",
        "",
        "iPolar is a diagnostic witness. This tool has no actuator path and cannot command UPAS,",
        "NINA, the mount, or any Switch output. It never automates vendor UI and never derives a",
        "measurement from screen pixels or OCR.",
        "",
        "Commands:",
        "  Help                Show this reference.",
        "  Init                Create a campaign and write its header and genesis hash.",
        "  RecordEnvironment   Record site coordinates and absolute station atmosphere.",
        "  RecordDarkFrame     Record and preserve the iPolar dark frame.",
        "  RecordSolveAttempt  Record one solve attempt, its outcome, star count, and reason.",
        "  RecordCalibration   Record one fixed-mount camera-centre calibration cycle.",
        "  RecordReseat        Record one remove/reseat/recalibration cycle.",
        "  RecordArtifact      Record and preserve a screenshot or raw frame with a qualitative verdict.",
        "  LinkTppaArtifact    Link a TPPA run artifact by path and hash for later A/B/A comparison.",
        "  RecordBlock         Record one no-motion A/B/A block window and its leg order.",
        "  Evaluate            Evaluate the campaign and print the evaluation JSON.",
        "  Finalize            Write the immutable JSON report, Markdown report, council packet, and manifest.",
        "",
        "Qualification levels this tool can emit:",
        "  Rejected, QualitativeWitnessOnly, QuantitativeUnqualified, QualifiedCorroboratingWitness.",
        "  GroundTruth and CertifiedAbsoluteAccuracy are not emittable under any input.",
        "",
        "Examples:",
        "  .\ipolar_witness_campaign.ps1 -Command Init -CampaignPath .\campaigns\ipolar-001 \",
        "      -CampaignId ipolar-001 -MountIdentifier HAE29C-EC -MountingStateId mount-state-a \",
        "      -PoleConvention TruePole",
        "",
        "  .\ipolar_witness_campaign.ps1 -Command RecordEnvironment -CampaignPath .\campaigns\ipolar-001 \",
        "      -LatitudeDegrees 25.2 -LongitudeDegrees 55.27 -ElevationMeters 12 -SiteSource 'GNSS fix' \",
        "      -AbsoluteStationPressureHectopascals 1004.2 -TemperatureCelsius 33.1 \",
        "      -RelativeHumidityPercent 48 -AtmosphereSource 'Balcony BME280' -AtmosphereObservedUtc 2026-07-26T18:00:00Z",
        "",
        "  .\ipolar_witness_campaign.ps1 -Command RecordBlock -CampaignPath .\campaigns\ipolar-001 \",
        "      -BlockId block-1 -BlockStartUtc 2026-07-26T18:30:00Z -BlockEndUtc 2026-07-26T19:10:00Z \",
        "      -BlockLegs iPolar,TPPA-A,TPPA-B,TPPA-A,iPolar",
        "",
        "  .\ipolar_witness_campaign.ps1 -Command Finalize -CampaignPath .\campaigns\ipolar-001 -PolicyPath .\policy.json",
        "",
        "Run Get-Help .\ipolar_witness_campaign.ps1 -Full for the parameter reference."
    )
    $lines -join [Environment]::NewLine
}

# --- Commands --------------------------------------------------------------------

function Invoke-InitCommand {
    $root = Assert-CampaignPath
    $safeCampaignId = Assert-IPolarCampaignId -Value $CampaignId
    $headerPath = Get-IPolarCampaignHeaderPath -CampaignPath $root
    if (Test-Path -LiteralPath $headerPath) { throw "Campaign already exists at $headerPath. A campaign is never re-initialized in place." }
    if ((Get-IPolarForbiddenQualificationLevels) -contains $RequestedQualificationLevel) {
        throw "-RequestedQualificationLevel '$RequestedQualificationLevel' is not permitted. iPolar is a corroborating witness and can never be recorded as ground truth or certified absolute accuracy."
    }
    if ($RequestedQualificationLevel -ne "Unspecified" -and (Get-IPolarQualificationLevels) -notcontains $RequestedQualificationLevel) {
        throw "-RequestedQualificationLevel '$RequestedQualificationLevel' is not a recognized level."
    }

    $pluginSha = $DeployedPluginSha256
    if ([string]::IsNullOrWhiteSpace($pluginSha) -and -not [string]::IsNullOrWhiteSpace($DeployedPluginPath)) {
        $pluginFull = [IO.Path]::GetFullPath($DeployedPluginPath)
        if (-not (Test-Path -LiteralPath $pluginFull -PathType Leaf)) { throw "Deployed plugin not found: $pluginFull" }
        $pluginSha = Get-FileSha256 -LiteralPath $pluginFull
    }

    $eventUtc = Resolve-EventUtc
    [void][IO.Directory]::CreateDirectory($root)

    return (Invoke-CampaignMutation -Root $root -Body {
    # Re-checked under the lock: two concurrent Init calls must not both create it.
    if (Test-Path -LiteralPath $headerPath) { throw "Campaign already exists at $headerPath. A campaign is never re-initialized in place." }

    $header = [ordered]@{
        SchemaVersion = Get-IPolarSchemaVersion
        CampaignId = $safeCampaignId
        CreatedUtc = Format-IPolarUtc -Value $eventUtc
        RepositoryCommit = Get-RepositoryCommit
        DeployedPluginSha256 = $pluginSha
        MountIdentifier = $MountIdentifier
        IPolarIdentifier = $IPolarIdentifier
        IPolarHardwareVariant = $IPolarHardwareVariant
        IPolarAdapter = $IPolarAdapter
        IPolarSoftwareVersion = $IPolarSoftwareVersion
        MountingStateId = $MountingStateId
        PoleConvention = $PoleConvention
        RequestedQualificationLevel = $RequestedQualificationLevel
        WitnessRole = Get-IPolarControlAuthorityStatement
    }
    $header.GenesisHash = Get-IPolarGenesisHash -Header $header
    Write-Utf8NoBom -LiteralPath (Get-IPolarCampaignHeaderPath -CampaignPath $root) -Content ($header | ConvertTo-Json -Depth 6)

    $payload = [ordered]@{
        CampaignId = $safeCampaignId
        PoleConvention = $PoleConvention
        MountingStateId = $MountingStateId
        Notes = $Notes
    }
    return (Add-CampaignEvent -Root $root -EventType "CampaignInitialized" -Payload $payload -EventUtc $eventUtc)
    })
}

function Invoke-RecordEnvironmentCommand {
    $root = Assert-ExistingCampaign
    $eventUtc = Resolve-EventUtc

    $observedUtc = $null
    $ageMinutes = $null
    if (Test-SuppliedDate -Value $AtmosphereObservedUtc) {
        $observedUtc = ConvertTo-IPolarUtc -Value $AtmosphereObservedUtc
        $ageMinutes = ($eventUtc - $observedUtc).TotalMinutes
    }

    $payload = [ordered]@{
        Site = [ordered]@{
            LatitudeDegrees = $LatitudeDegrees
            LongitudeDegrees = $LongitudeDegrees
            ElevationMeters = $ElevationMeters
            Source = $SiteSource
        }
        Atmosphere = [ordered]@{
            AbsoluteStationPressureHectopascals = $AbsoluteStationPressureHectopascals
            TemperatureCelsius = $TemperatureCelsius
            RelativeHumidityPercent = $RelativeHumidityPercent
            Source = $AtmosphereSource
            ObservedUtc = if ($null -ne $observedUtc) { Format-IPolarUtc -Value $observedUtc } else { $null }
            AgeMinutes = $ageMinutes
        }
        Notes = $Notes
    }
    return (Invoke-CampaignMutation -Root $root -Body {
        Add-CampaignEvent -Root $root -EventType "Environment" -Payload $payload -EventUtc $eventUtc
    })
}

function Invoke-RecordDarkFrameCommand {
    $root = Assert-ExistingCampaign
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "-Path to the dark frame is required." }
    $eventUtc = Resolve-EventUtc
    $declaredCreated = if (Test-SuppliedDate -Value $CapturedUtc) { $CapturedUtc } else { [datetime]::MinValue }

    return (Invoke-CampaignMutation -Root $root -Body {
        # Every reason to refuse this event is evaluated before the preserved copy
        # is committed, so a rejection leaves nothing behind at the 000N path.
        $state = Assert-AppendableCampaignState -Root $root -EventType "DarkFrame" -EventUtc $eventUtc
        $reservation = New-CampaignArtifactReservation -Root $root -SourcePath $Path -Sequence $state.NextSequence `
            -EventUtc $eventUtc -DeclaredCreatedUtc $declaredCreated -PreserveCopy
        try {
            $payload = [ordered]@{
                SourcePath = $reservation.SourcePath
                StoredPath = $reservation.StoredPath
                Sha256 = $reservation.Sha256
                SizeBytes = $reservation.SizeBytes
                CreatedUtc = $reservation.CreatedUtc
                CapturedUtc = $reservation.CreatedUtc
                Notes = $Notes
            }
            $record = Add-CampaignEvent -Root $root -EventType "DarkFrame" -Payload $payload -EventUtc $eventUtc -ExpectedSequence $state.NextSequence
            Complete-CampaignArtifactReservation -Reservation $reservation
            return $record
        } finally {
            Remove-CampaignArtifactReservation -Reservation $reservation
        }
    })
}

function Invoke-RecordSolveAttemptCommand {
    $root = Assert-ExistingCampaign
    if ([string]::IsNullOrWhiteSpace($SolveOutcome)) { throw "-SolveOutcome must be Success or Failure. A solve attempt is never recorded with an assumed outcome." }
    $eventUtc = Resolve-EventUtc

    return (Invoke-CampaignMutation -Root $root -Body {
        $state = Assert-AppendableCampaignState -Root $root -EventType "SolveAttempt" -EventUtc $eventUtc
        $existing = @(Get-IPolarEventsOfType -Events $state.Campaign.Events -EventType "SolveAttempt")
        $attemptIndex = if ($null -ne $CycleIndex) { [int]$CycleIndex } else { $existing.Count + 1 }

        $payload = [ordered]@{
            AttemptIndex = $attemptIndex
            Succeeded = ($SolveOutcome -eq "Success")
            AvailableStarCount = $AvailableStarCount
            Reason = $Reason
            ManualObservation = $ManualObservation
            Notes = $Notes
        }
        Add-CampaignEvent -Root $root -EventType "SolveAttempt" -Payload $payload -EventUtc $eventUtc -ExpectedSequence $state.NextSequence
    })
}

function Invoke-RecordCalibrationCommand {
    $root = Assert-ExistingCampaign
    $eventUtc = Resolve-EventUtc
    if ($CameraRemovedOrReseated.IsPresent) {
        throw "A fixed-mount calibration cycle must leave the camera untouched. Record a removal with -Command RecordReseat instead."
    }

    foreach ($position in $RaPositionsDegrees) {
        if ($null -eq (Get-IPolarFiniteNumber $position)) { throw "-RaPositionsDegrees contains a non-finite value." }
    }

    return (Invoke-CampaignMutation -Root $root -Body {
        $state = Assert-AppendableCampaignState -Root $root -EventType "FixedMountCalibration" -EventUtc $eventUtc
        $existing = @(Get-IPolarEventsOfType -Events $state.Campaign.Events -EventType "FixedMountCalibration")
        $index = if ($null -ne $CycleIndex) { [int]$CycleIndex } else { $existing.Count + 1 }

        $payload = [ordered]@{
            CycleIndex = $index
            MountingStateId = if ([string]::IsNullOrWhiteSpace($MountingStateId)) { $null } else { $MountingStateId }
            RaPositionsDegrees = @($RaPositionsDegrees)
            CameraRemovedOrReseated = $false
            Residual = New-ResidualRecord
            ManualObservation = $ManualObservation
            Notes = $Notes
        }
        Add-CampaignEvent -Root $root -EventType "FixedMountCalibration" -Payload $payload -EventUtc $eventUtc -ExpectedSequence $state.NextSequence
    })
}

function Invoke-RecordReseatCommand {
    $root = Assert-ExistingCampaign
    $eventUtc = Resolve-EventUtc

    return (Invoke-CampaignMutation -Root $root -Body {
        $state = Assert-AppendableCampaignState -Root $root -EventType "Reseat" -EventUtc $eventUtc
        $existing = @(Get-IPolarEventsOfType -Events $state.Campaign.Events -EventType "Reseat")
        $index = if ($null -ne $CycleIndex) { [int]$CycleIndex } else { $existing.Count + 1 }

        $payload = [ordered]@{
            ReseatIndex = $index
            MountingStateIdBefore = if ([string]::IsNullOrWhiteSpace($MountingStateIdBefore)) { $null } else { $MountingStateIdBefore }
            MountingStateIdAfter = if ([string]::IsNullOrWhiteSpace($MountingStateIdAfter)) { $null } else { $MountingStateIdAfter }
            CameraRemovedOrReseated = $true
            RecalibrationPerformed = $RecalibrationPerformed.IsPresent
            RaPositionsDegrees = @($RaPositionsDegrees)
            Residual = New-ResidualRecord
            ManualObservation = $ManualObservation
            Notes = $Notes
        }
        Add-CampaignEvent -Root $root -EventType "Reseat" -Payload $payload -EventUtc $eventUtc -ExpectedSequence $state.NextSequence
    })
}

function Invoke-RecordArtifactCommand {
    $root = Assert-ExistingCampaign
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "-Path to the screenshot or raw frame is required." }
    $eventUtc = Resolve-EventUtc
    $declaredCreated = if (Test-SuppliedDate -Value $CreatedUtc) { $CreatedUtc } else { [datetime]::MinValue }

    return (Invoke-CampaignMutation -Root $root -Body {
        $state = Assert-AppendableCampaignState -Root $root -EventType "Artifact" -EventUtc $eventUtc
        $reservation = New-CampaignArtifactReservation -Root $root -SourcePath $Path -Sequence $state.NextSequence `
            -EventUtc $eventUtc -DeclaredCreatedUtc $declaredCreated -PreserveCopy
        try {
            $payload = [ordered]@{
                ArtifactKind = $ArtifactKind
                SourcePath = $reservation.SourcePath
                StoredPath = $reservation.StoredPath
                Sha256 = $reservation.Sha256
                SizeBytes = $reservation.SizeBytes
                CreatedUtc = $reservation.CreatedUtc
                # A cross/circle reading is the only thing a vendor screenshot supports.
                # No numeric value is inferred from it.
                QualitativeVerdict = $QualitativeVerdict
                ManualObservation = $ManualObservation
                Notes = $Notes
            }
            $record = Add-CampaignEvent -Root $root -EventType "Artifact" -Payload $payload -EventUtc $eventUtc -ExpectedSequence $state.NextSequence
            Complete-CampaignArtifactReservation -Reservation $reservation
            return $record
        } finally {
            Remove-CampaignArtifactReservation -Reservation $reservation
        }
    })
}

function Invoke-LinkTppaArtifactCommand {
    $root = Assert-ExistingCampaign
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "-Path to the TPPA artifact is required." }
    if ([string]::IsNullOrWhiteSpace($TppaRunId)) { throw "-TppaRunId is required so the link can be traced back to a specific TPPA run." }
    $eventUtc = Resolve-EventUtc

    $full = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "TPPA artifact not found: $full" }
    $info = Get-Item -LiteralPath $full
    if ($info.Length -le 0) { throw "TPPA artifact is empty: $full" }

    $payload = [ordered]@{
        TppaRunId = $TppaRunId
        ArtifactPath = $full
        Sha256 = Get-FileSha256 -LiteralPath $full
        SizeBytes = [long]$info.Length
        CreatedUtc = Format-IPolarUtc -Value $info.LastWriteTimeUtc
        Description = $Description
        Notes = $Notes
    }
    return (Invoke-CampaignMutation -Root $root -Body {
        Add-CampaignEvent -Root $root -EventType "TppaArtifactLink" -Payload $payload -EventUtc $eventUtc
    })
}

function Invoke-RecordBlockCommand {
    $root = Assert-ExistingCampaign
    if ([string]::IsNullOrWhiteSpace($BlockId)) { throw "-BlockId is required." }
    if (-not (Test-SuppliedDate -Value $BlockStartUtc) -or -not (Test-SuppliedDate -Value $BlockEndUtc)) {
        throw "-BlockStartUtc and -BlockEndUtc are required so reseat events can be tested against the block window."
    }
    Assert-BlockLegSequence -Legs @($BlockLegs)

    $startUtc = ConvertTo-IPolarUtc -Value $BlockStartUtc
    $endUtc = ConvertTo-IPolarUtc -Value $BlockEndUtc
    if ($endUtc -lt $startUtc) { throw "-BlockEndUtc precedes -BlockStartUtc." }
    $eventUtc = Resolve-EventUtc

    $legs = New-Object System.Collections.Generic.List[object]
    $order = 1
    foreach ($leg in $BlockLegs) {
        $method = if ($leg -eq "iPolar") { "IPolar" } else { "Tppa" }
        [void]$legs.Add([ordered]@{ Order = $order; Label = $leg; Method = $method })
        $order++
    }

    $payload = [ordered]@{
        BlockId = $BlockId
        StartUtc = Format-IPolarUtc -Value $startUtc
        EndUtc = Format-IPolarUtc -Value $endUtc
        Legs = $legs.ToArray()
        Notes = $Notes
    }
    return (Invoke-CampaignMutation -Root $root -Body {
        Add-CampaignEvent -Root $root -EventType "NoMotionBlock" -Payload $payload -EventUtc $eventUtc
    })
}

function Get-CampaignEvaluation {
    param(
        [Parameter(Mandatory)] [string]$Root,
        [datetime]$EvaluationUtc = [datetime]::MinValue
    )

    $campaign = Read-IPolarCampaign -CampaignPath $Root
    $policy = $null
    if (-not [string]::IsNullOrWhiteSpace($PolicyPath)) { $policy = Read-IPolarPolicy -PolicyPath $PolicyPath }
    $evaluatedUtc = if (Test-SuppliedDate -Value $EvaluationUtc) {
        ConvertTo-IPolarUtc -Value $EvaluationUtc
    } elseif (Test-SuppliedDate -Value $RecordedUtc) {
        ConvertTo-IPolarUtc -Value $RecordedUtc
    } else { [datetime]::UtcNow }

    # The CLI is the filesystem boundary: it verifies the preserved bytes and hands
    # the resulting facts to the pure evaluator, which never touches a disk itself.
    $artifactIntegrity = Test-IPolarPreservedEvidence -Campaign $campaign
    return (Invoke-IPolarCampaignEvaluation -Campaign $campaign -Policy $policy -EvaluatedUtc $evaluatedUtc -ArtifactIntegrity $artifactIntegrity)
}

function Invoke-EvaluateCommand {
    $root = Assert-ExistingCampaign
    $evaluation = Get-CampaignEvaluation -Root $root
    $json = $evaluation | ConvertTo-Json -Depth 12
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        Write-Utf8NoBom -LiteralPath ([IO.Path]::GetFullPath($OutputPath)) -Content $json
    }
    return $evaluation
}

function Invoke-FinalizeCommand {
    $root = Assert-ExistingCampaign
    return (Invoke-CampaignMutation -Root $root -Body { Invoke-FinalizeUnderLock -Root $root })
}

<#
.SYNOPSIS
    Finalization body. Must run under the campaign lock.
.DESCRIPTION
    Appending the CampaignFinalized event and writing the reports is one mutation.
    Re-running Finalize on an already finalized campaign is the single permitted
    post-finalization operation: it appends nothing and reconstructs any report
    that a previous interrupted run did not write, byte for byte.
#>
function Invoke-FinalizeUnderLock {
    param([Parameter(Mandatory)] [string]$Root)

    $root = $Root
    $state = Get-CampaignState -Root $root
    $alreadyFinalized = @(Get-IPolarEventsOfType -Events $state.Campaign.Events -EventType "CampaignFinalized")
    if ($alreadyFinalized.Count -gt 1) {
        throw "Campaign contains more than one CampaignFinalized event. The immutable event chain is invalid."
    }
    $isFinalizationResume = ($alreadyFinalized.Count -eq 1)

    $eventUtc = if ($isFinalizationResume) {
        ConvertTo-IPolarUtc -Value (Get-IPolarProperty -InputObject $alreadyFinalized[0] -Name "RecordedUtc")
    } else { Resolve-EventUtc }
    $preliminary = Get-CampaignEvaluation -Root $root -EvaluationUtc $eventUtc

    # A campaign may legitimately finalize as Rejected - a documented rejection is a
    # real result. Evidence that no longer matches what was recorded is different:
    # finalization mints an immutable manifest of hashes, and stamping that over
    # bytes known to have changed would enshrine a false record. Refuse outright.
    # An external TPPA artifact that is merely absent is a recorded limitation, not
    # a failure, and does not block finalization.
    if ($preliminary.ArtifactIntegrity.Failures.Count -gt 0) {
        $failureText = ($preliminary.ArtifactIntegrity.Failures -join "; ")
        throw "Refusing to finalize: recorded evidence no longer matches the bytes on disk. $failureText"
    }

    $campaignSlug = Assert-IPolarCampaignId -Value ([string]$preliminary.CampaignId)

    # Validate the complete report destination before appending the immutable
    # finalization event. A path error or ordinary collision must remain
    # recoverable and must not brick a campaign without its reports.
    $reports = if ([string]::IsNullOrWhiteSpace($ReportDirectory)) { Join-Path $root "reports" } else { [IO.Path]::GetFullPath($ReportDirectory) }
    [void][IO.Directory]::CreateDirectory($reports)
    $jsonPath = Join-Path $reports "$campaignSlug-report.json"
    $markdownPath = Join-Path $reports "$campaignSlug-report.md"
    $packetPath = Join-Path $reports "$campaignSlug-council-packet.md"
    $manifestPath = Join-Path $reports "$campaignSlug-manifest.json"
    if (-not $isFinalizationResume) {
        foreach ($existing in @($jsonPath, $markdownPath, $packetPath, $manifestPath)) {
            if (Test-Path -LiteralPath $existing) { throw "Report already exists at $existing. A finalized report is never overwritten." }
        }
    }

    $artifactManifest = New-Object System.Collections.Generic.List[object]
    foreach ($campaignEvent in $state.Campaign.Events) {
        $type = [string](Get-IPolarProperty -InputObject $campaignEvent -Name "EventType")
        if ($type -ne "Artifact" -and $type -ne "DarkFrame" -and $type -ne "TppaArtifactLink") { continue }
        $payload = Get-IPolarProperty -InputObject $campaignEvent -Name "Payload"
        $storedPath = Get-IPolarProperty -InputObject $payload -Name "StoredPath"
        if ($null -eq $storedPath) { $storedPath = Get-IPolarProperty -InputObject $payload -Name "ArtifactPath" }
        [void]$artifactManifest.Add([ordered]@{
            Sequence = Get-IPolarProperty -InputObject $campaignEvent -Name "Sequence"
            EventType = $type
            StoredPath = [string]$storedPath
            Sha256 = [string](Get-IPolarProperty -InputObject $payload -Name "Sha256")
            SizeBytes = Get-IPolarProperty -InputObject $payload -Name "SizeBytes"
        })
    }

    $finalizePayload = [ordered]@{
        QualificationLevelAtFinalization = $preliminary.QualificationLevel
        ChainHeadHashBeforeFinalization = $state.PreviousEventHash
        FailedGateCount = $preliminary.FailedGates.Count
        Artifacts = $artifactManifest.ToArray()
        PolicyId = $preliminary.UncertaintyPolicy.PolicyId
        Notes = $Notes
    }
    if ($isFinalizationResume) {
        $evaluation = $preliminary
    } else {
        [void](Add-CampaignEvent -Root $root -EventType "CampaignFinalized" -Payload $finalizePayload -EventUtc $eventUtc)
        $evaluation = Get-CampaignEvaluation -Root $root -EvaluationUtc $eventUtc
    }

    $jsonContent = $evaluation | ConvertTo-Json -Depth 12
    $markdownContent = (New-IPolarCampaignReportMarkdown -Evaluation $evaluation) -join [Environment]::NewLine
    $packetContent = (New-IPolarCouncilPacketMarkdown -Evaluation $evaluation) -join [Environment]::NewLine
    Write-Utf8NoBomOnce -LiteralPath $jsonPath -Content $jsonContent
    Write-Utf8NoBomOnce -LiteralPath $markdownPath -Content $markdownContent
    Write-Utf8NoBomOnce -LiteralPath $packetPath -Content $packetContent

    $manifest = [ordered]@{
        SchemaVersion = Get-IPolarSchemaVersion
        CampaignId = $evaluation.CampaignId
        FinalizedUtc = Format-IPolarUtc -Value $eventUtc
        QualificationLevel = $evaluation.QualificationLevel
        ChainHeadHash = $evaluation.ChainHeadHash
        EventLogSha256 = Get-FileSha256 -LiteralPath (Get-IPolarCampaignEventLogPath -CampaignPath $root)
        HeaderSha256 = Get-FileSha256 -LiteralPath (Get-IPolarCampaignHeaderPath -CampaignPath $root)
        Reports = @(
            [ordered]@{ Path = $jsonPath; Sha256 = Get-FileSha256 -LiteralPath $jsonPath }
            [ordered]@{ Path = $markdownPath; Sha256 = Get-FileSha256 -LiteralPath $markdownPath }
            [ordered]@{ Path = $packetPath; Sha256 = Get-FileSha256 -LiteralPath $packetPath }
        )
        Artifacts = $artifactManifest.ToArray()
        ControlAuthority = Get-IPolarControlAuthorityStatement
        AbsoluteCertification = Get-IPolarCertificationOutstandingStatement
    }
    $manifestContent = $manifest | ConvertTo-Json -Depth 12
    Write-Utf8NoBomOnce -LiteralPath $manifestPath -Content $manifestContent

    return [pscustomobject]@{
        QualificationLevel = $evaluation.QualificationLevel
        ReportJsonPath = $jsonPath
        ReportMarkdownPath = $markdownPath
        CouncilPacketPath = $packetPath
        ManifestPath = $manifestPath
        FailedGateCount = $evaluation.FailedGates.Count
    }
}

# --- Dispatch --------------------------------------------------------------------

switch ($Command) {
    "Help" { Show-CommandHelp; exit 0 }
    "Init" { $result = Invoke-InitCommand }
    "RecordEnvironment" { $result = Invoke-RecordEnvironmentCommand }
    "RecordDarkFrame" { $result = Invoke-RecordDarkFrameCommand }
    "RecordSolveAttempt" { $result = Invoke-RecordSolveAttemptCommand }
    "RecordCalibration" { $result = Invoke-RecordCalibrationCommand }
    "RecordReseat" { $result = Invoke-RecordReseatCommand }
    "RecordArtifact" { $result = Invoke-RecordArtifactCommand }
    "LinkTppaArtifact" { $result = Invoke-LinkTppaArtifactCommand }
    "RecordBlock" { $result = Invoke-RecordBlockCommand }
    "Evaluate" { $result = Invoke-EvaluateCommand }
    "Finalize" { $result = Invoke-FinalizeCommand }
    default { throw "Unhandled command '$Command'." }
}

$result

if ($Command -eq "Evaluate" -or $Command -eq "Finalize") {
    if ($result.QualificationLevel -eq "Rejected") { exit 2 }
}
exit 0
