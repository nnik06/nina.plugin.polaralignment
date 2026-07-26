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
    [string]$ReportDirectory = ""
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
    Appends one hash-chained event to the campaign log.
#>
function Add-CampaignEvent {
    param(
        [Parameter(Mandatory)] [string]$Root,
        [Parameter(Mandatory)] [string]$EventType,
        [Parameter(Mandatory)] $Payload,
        [Parameter(Mandatory)] [datetime]$EventUtc
    )

    $state = Get-CampaignState -Root $Root
    if ($null -ne $state.LastRecordedUtc -and $EventUtc -lt $state.LastRecordedUtc) {
        throw "Refusing to append '$EventType' at $(Format-IPolarUtc -Value $EventUtc): the previous event is timestamped $(Format-IPolarUtc -Value $state.LastRecordedUtc). Campaign event time must not move backwards."
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
    $logPath = Get-IPolarCampaignEventLogPath -CampaignPath $Root
    [IO.File]::AppendAllText($logPath, $line + "`n", [Text.UTF8Encoding]::new($false))
    return $record
}

<#
.SYNOPSIS
    Validates, hashes, and preserves an evidence file inside the campaign.
.DESCRIPTION
    Rejects a missing, empty, duplicated, reused, or stale file, and refuses to
    overwrite an existing preserved copy. The preserved copy is the campaign's
    own evidence; the operator's original is left untouched.
#>
function Import-CampaignArtifact {
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
    if ($PreserveCopy.IsPresent) {
        $artifactDirectory = Join-Path $Root "artifacts"
        [void][IO.Directory]::CreateDirectory($artifactDirectory)
        $destination = Join-Path $artifactDirectory ("{0:D4}-{1}" -f $Sequence, $info.Name)
        if (Test-Path -LiteralPath $destination) {
            throw "Preserved evidence already exists at $destination. Campaign evidence is never overwritten."
        }
        Copy-Item -LiteralPath $full -Destination $destination
        $storedPath = $destination
    }

    return [ordered]@{
        SourcePath = $full
        StoredPath = $storedPath
        Sha256 = $sha
        SizeBytes = [long]$info.Length
        CreatedUtc = Format-IPolarUtc -Value $createdUtc
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
    return (Add-CampaignEvent -Root $root -EventType "Environment" -Payload $payload -EventUtc $eventUtc)
}

function Invoke-RecordDarkFrameCommand {
    $root = Assert-ExistingCampaign
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "-Path to the dark frame is required." }
    $eventUtc = Resolve-EventUtc
    $state = Get-CampaignState -Root $root

    $declaredCreated = if (Test-SuppliedDate -Value $CapturedUtc) { $CapturedUtc } else { [datetime]::MinValue }
    $artifact = Import-CampaignArtifact -Root $root -SourcePath $Path -Sequence $state.NextSequence -EventUtc $eventUtc -DeclaredCreatedUtc $declaredCreated -PreserveCopy

    $payload = [ordered]@{
        SourcePath = $artifact.SourcePath
        StoredPath = $artifact.StoredPath
        Sha256 = $artifact.Sha256
        SizeBytes = $artifact.SizeBytes
        CreatedUtc = $artifact.CreatedUtc
        CapturedUtc = $artifact.CreatedUtc
        Notes = $Notes
    }
    return (Add-CampaignEvent -Root $root -EventType "DarkFrame" -Payload $payload -EventUtc $eventUtc)
}

function Invoke-RecordSolveAttemptCommand {
    $root = Assert-ExistingCampaign
    if ([string]::IsNullOrWhiteSpace($SolveOutcome)) { throw "-SolveOutcome must be Success or Failure. A solve attempt is never recorded with an assumed outcome." }
    $eventUtc = Resolve-EventUtc

    $state = Get-CampaignState -Root $root
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
    return (Add-CampaignEvent -Root $root -EventType "SolveAttempt" -Payload $payload -EventUtc $eventUtc)
}

function Invoke-RecordCalibrationCommand {
    $root = Assert-ExistingCampaign
    $eventUtc = Resolve-EventUtc
    if ($CameraRemovedOrReseated.IsPresent) {
        throw "A fixed-mount calibration cycle must leave the camera untouched. Record a removal with -Command RecordReseat instead."
    }

    $state = Get-CampaignState -Root $root
    $existing = @(Get-IPolarEventsOfType -Events $state.Campaign.Events -EventType "FixedMountCalibration")
    $index = if ($null -ne $CycleIndex) { [int]$CycleIndex } else { $existing.Count + 1 }

    foreach ($position in $RaPositionsDegrees) {
        if ($null -eq (Get-IPolarFiniteNumber $position)) { throw "-RaPositionsDegrees contains a non-finite value." }
    }

    $payload = [ordered]@{
        CycleIndex = $index
        MountingStateId = if ([string]::IsNullOrWhiteSpace($MountingStateId)) { $null } else { $MountingStateId }
        RaPositionsDegrees = @($RaPositionsDegrees)
        CameraRemovedOrReseated = $false
        Residual = New-ResidualRecord
        ManualObservation = $ManualObservation
        Notes = $Notes
    }
    return (Add-CampaignEvent -Root $root -EventType "FixedMountCalibration" -Payload $payload -EventUtc $eventUtc)
}

function Invoke-RecordReseatCommand {
    $root = Assert-ExistingCampaign
    $eventUtc = Resolve-EventUtc

    $state = Get-CampaignState -Root $root
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
    return (Add-CampaignEvent -Root $root -EventType "Reseat" -Payload $payload -EventUtc $eventUtc)
}

function Invoke-RecordArtifactCommand {
    $root = Assert-ExistingCampaign
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "-Path to the screenshot or raw frame is required." }
    $eventUtc = Resolve-EventUtc
    $state = Get-CampaignState -Root $root

    $declaredCreated = if (Test-SuppliedDate -Value $CreatedUtc) { $CreatedUtc } else { [datetime]::MinValue }
    $artifact = Import-CampaignArtifact -Root $root -SourcePath $Path -Sequence $state.NextSequence -EventUtc $eventUtc -DeclaredCreatedUtc $declaredCreated -PreserveCopy

    $payload = [ordered]@{
        ArtifactKind = $ArtifactKind
        SourcePath = $artifact.SourcePath
        StoredPath = $artifact.StoredPath
        Sha256 = $artifact.Sha256
        SizeBytes = $artifact.SizeBytes
        CreatedUtc = $artifact.CreatedUtc
        # A cross/circle reading is the only thing a vendor screenshot supports.
        # No numeric value is inferred from it.
        QualitativeVerdict = $QualitativeVerdict
        ManualObservation = $ManualObservation
        Notes = $Notes
    }
    return (Add-CampaignEvent -Root $root -EventType "Artifact" -Payload $payload -EventUtc $eventUtc)
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
    return (Add-CampaignEvent -Root $root -EventType "TppaArtifactLink" -Payload $payload -EventUtc $eventUtc)
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
    return (Add-CampaignEvent -Root $root -EventType "NoMotionBlock" -Payload $payload -EventUtc $eventUtc)
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
    return (Invoke-IPolarCampaignEvaluation -Campaign $campaign -Policy $policy -EvaluatedUtc $evaluatedUtc)
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
