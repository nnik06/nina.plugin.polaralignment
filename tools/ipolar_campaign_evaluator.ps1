<#
.SYNOPSIS
    Pure evaluation library for iOptron iPolar witness campaigns.

.DESCRIPTION
    Defines the versioned campaign schema helpers, the canonical-JSON hash chain,
    the qualification evaluator, and the report builders used by
    tools/ipolar_witness_campaign.ps1.

    This file is a dot-source library and deliberately declares no parameters, so
    that dot-sourcing it can never overwrite a caller's variables. Use
    tools/ipolar_witness_campaign.ps1 -Command Evaluate to evaluate a campaign
    from the command line.

    The evaluator is offline and read-only. It never contacts NINA, PHD2, the
    mount, UPASBridge, a Switch, or any other hardware, and it never emits a
    ground-truth or certified-absolute-accuracy verdict.

.EXAMPLE
    . .\tools\ipolar_campaign_evaluator.ps1
    $campaign = Read-IPolarCampaign -CampaignPath .\campaigns\ipolar-001
    Invoke-IPolarCampaignEvaluation -Campaign $campaign -EvaluatedUtc ([datetime]::UtcNow)
#>

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

# --- Schema and policy constants -------------------------------------------------

function Get-IPolarSchemaVersion { return "1.0.0" }
function Get-IPolarSupportedSchemaMajor { return 1 }

<#
.SYNOPSIS
    Default screening policy.
.DESCRIPTION
    These are collection-completeness gates only. They deliberately contain no
    TPPA/iPolar agreement threshold and no assumed iPolar uncertainty, because
    the vendor's stated resolution figure is a marketing/resolution statement and
    has never been characterized as a one-sigma statistical uncertainty.
#>
function Get-IPolarDefaultScreeningPolicy {
    return [ordered]@{
        MinimumSolveAttempts = 10
        MinimumSolveSuccesses = 9
        MinimumFixedMountCalibrationCycles = 5
        MinimumReseatCycles = 5
        MaximumAtmosphereAgeMinutes = 180.0
        MaximumDarkFrameAgeHours = 24.0
        MaximumPlausibleResidualArcsec = 36000.0
    }
}

function Get-IPolarAuthoritativeResidualSourceKinds {
    return @("VendorNumericExport", "DocumentedManualReadout")
}

function Get-IPolarKnownResidualSourceKinds {
    return @("VendorNumericExport", "DocumentedManualReadout", "Ocr", "ScreenshotPixelMeasurement", "Unknown")
}

function Get-IPolarValidPoleConventions {
    return @("TruePole", "ApparentPole", "Unknown")
}

function Get-IPolarQualitativeVerdicts {
    return @("CrossInsideCircle", "CrossOutsideCircle", "Indeterminate")
}

<#
.SYNOPSIS
    Qualification levels this evaluator is permitted to emit.
#>
function Get-IPolarQualificationLevels {
    return @("Rejected", "QualitativeWitnessOnly", "QuantitativeUnqualified", "QualifiedCorroboratingWitness")
}

<#
.SYNOPSIS
    Verdicts that must never be produced or requested for iPolar evidence.
#>
function Get-IPolarForbiddenQualificationLevels {
    return @("GroundTruth", "CertifiedAbsoluteAccuracy")
}

function Get-IPolarEventTypes {
    return @(
        "CampaignInitialized",
        "Environment",
        "DarkFrame",
        "SolveAttempt",
        "FixedMountCalibration",
        "Reseat",
        "Artifact",
        "TppaArtifactLink",
        "NoMotionBlock",
        "CampaignFinalized"
    )
}

function Get-IPolarControlAuthorityStatement {
    return "iPolar is a diagnostic witness only. It cannot command UPAS, NINA, the mount, a Switch output, or any other hardware, and this tooling contains no actuator path."
}

function Get-IPolarCertificationOutstandingStatement {
    return "Absolute certification remains outstanding. A qualified open-sky drift reference is still required before any absolute sub-arcminute accuracy claim."
}

# --- Canonical serialization and hashing -----------------------------------------

<#
.SYNOPSIS
    Converts a value to a UTC DateTime without guessing a non-UTC offset silently.
.DESCRIPTION
    Strings carrying an explicit offset are honoured. A string or DateTime with no
    zone information is treated as UTC, which is the documented campaign contract:
    every recorded instant is UTC.
#>
function ConvertTo-IPolarUtc {
    param([Parameter(Mandatory)] $Value)

    if ($Value -is [datetime]) {
        switch ($Value.Kind) {
            ([DateTimeKind]::Utc) { return $Value }
            ([DateTimeKind]::Local) { return $Value.ToUniversalTime() }
            default { return [datetime]::SpecifyKind($Value, [DateTimeKind]::Utc) }
        }
    }
    if ($Value -is [datetimeoffset]) { return $Value.UtcDateTime }

    $text = [string]$Value
    if ([string]::IsNullOrWhiteSpace($text)) { throw "Cannot convert an empty value to a UTC timestamp." }

    $styles = [Globalization.DateTimeStyles]::AdjustToUniversal -bor [Globalization.DateTimeStyles]::AssumeUniversal
    return [datetime]::Parse($text, [Globalization.CultureInfo]::InvariantCulture, $styles)
}

<#
.SYNOPSIS
    Formats an instant in the single UTC form the campaign hash chain relies on.
#>
function Format-IPolarUtc {
    param([Parameter(Mandatory)] $Value)

    return (ConvertTo-IPolarUtc -Value $Value).ToString("o", [Globalization.CultureInfo]::InvariantCulture)
}

function Test-IPolarUtcText {
    param([string]$Text)

    if ([string]::IsNullOrEmpty($Text)) { return $false }
    return $Text -match '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?Z$'
}

function ConvertTo-IPolarJsonString {
    param([string]$Value)

    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append('"')
    foreach ($character in $Value.ToCharArray()) {
        $code = [int]$character
        switch ($character) {
            '"' { [void]$builder.Append('\"'); continue }
            '\' { [void]$builder.Append('\\'); continue }
            "`b" { [void]$builder.Append('\b'); continue }
            "`f" { [void]$builder.Append('\f'); continue }
            "`n" { [void]$builder.Append('\n'); continue }
            "`r" { [void]$builder.Append('\r'); continue }
            "`t" { [void]$builder.Append('\t'); continue }
            default {
                if ($code -lt 32 -or $code -gt 126) {
                    [void]$builder.Append(('\u{0:x4}' -f $code))
                } else {
                    [void]$builder.Append($character)
                }
            }
        }
    }
    [void]$builder.Append('"')
    return $builder.ToString()
}

<#
.SYNOPSIS
    Produces a host-stable canonical JSON rendering used for hashing.
.DESCRIPTION
    Object keys are sorted with ordinal comparison, numbers are normalized through
    invariant round-trip formatting, and every instant is normalized to the single
    UTC round-trip form. This keeps event hashes identical across Windows
    PowerShell 5.1 and PowerShell 7, whose JSON readers disagree about whether an
    ISO timestamp is a string or a DateTime and whether 1.5 is Decimal or Double.
#>
function Get-IPolarOrdinalSortedNames {
    param([string[]]$Names)

    $sorted = [string[]]@($Names)
    [Array]::Sort($sorted, [StringComparer]::Ordinal)
    return $sorted
}

function ConvertTo-IPolarCanonicalJson {
    param($Value)

    if ($null -eq $Value) { return "null" }

    # PSCustomObject must be tested before the primitive checks because its
    # note properties live on the PSObject wrapper, not on the base object.
    if ($Value -is [System.Management.Automation.PSCustomObject]) {
        $properties = @{}
        foreach ($property in $Value.PSObject.Properties) { $properties[$property.Name] = $property.Value }
        $parts = New-Object System.Collections.Generic.List[string]
        foreach ($name in (Get-IPolarOrdinalSortedNames -Names @($properties.Keys))) {
            [void]$parts.Add((ConvertTo-IPolarJsonString $name) + ":" + (ConvertTo-IPolarCanonicalJson $properties[$name]))
        }
        return "{" + ($parts -join ",") + "}"
    }

    if ($Value -is [bool]) { if ($Value) { return "true" } else { return "false" } }

    if ($Value -is [datetime] -or $Value -is [datetimeoffset]) {
        return (ConvertTo-IPolarJsonString (Format-IPolarUtc -Value $Value))
    }

    if ($Value -is [string]) {
        if (Test-IPolarUtcText -Text $Value) {
            return (ConvertTo-IPolarJsonString (Format-IPolarUtc -Value $Value))
        }
        return (ConvertTo-IPolarJsonString $Value)
    }

    # Fully qualified type names: [short], [uint32], and [uint64] are not type
    # accelerators in Windows PowerShell 5.1.
    if ($Value -is [System.Int32] -or $Value -is [System.Int64] -or $Value -is [System.Int16] -or
        $Value -is [System.Byte] -or $Value -is [System.SByte] -or
        $Value -is [System.UInt16] -or $Value -is [System.UInt32] -or $Value -is [System.UInt64]) {
        return ([long]$Value).ToString([Globalization.CultureInfo]::InvariantCulture)
    }

    if ($Value -is [System.Double] -or $Value -is [System.Single] -or $Value -is [System.Decimal]) {
        $number = [double]$Value
        if ([double]::IsNaN($number) -or [double]::IsInfinity($number)) {
            throw "Non-finite numbers cannot be serialized into a campaign record."
        }
        # G17 pins the full IEEE-754 binary64 value on both .NET Framework
        # (Windows PowerShell 5.1) and modern .NET (PowerShell 7). The older
        # round-trip "R" formatter does not emit identical text on both hosts.
        return $number.ToString("G17", [Globalization.CultureInfo]::InvariantCulture)
    }

    if ($Value -is [System.Collections.IDictionary]) {
        $parts = New-Object System.Collections.Generic.List[string]
        foreach ($name in (Get-IPolarOrdinalSortedNames -Names @($Value.Keys | ForEach-Object { [string]$_ }))) {
            [void]$parts.Add((ConvertTo-IPolarJsonString $name) + ":" + (ConvertTo-IPolarCanonicalJson $Value[$name]))
        }
        return "{" + ($parts -join ",") + "}"
    }

    if ($Value -is [System.Collections.IEnumerable]) {
        $parts = New-Object System.Collections.Generic.List[string]
        foreach ($item in $Value) { [void]$parts.Add((ConvertTo-IPolarCanonicalJson $item)) }
        return "[" + ($parts -join ",") + "]"
    }

    return (ConvertTo-IPolarJsonString ([string]$Value))
}

function Get-IPolarTextSha256 {
    param([Parameter(Mandatory)] [string]$Text)

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace("-", "").ToLowerInvariant()
    } finally {
        $sha.Dispose()
    }
}

<#
.SYNOPSIS
    Computes the genesis hash binding the event chain to the campaign header.
.DESCRIPTION
    Covers every header field except GenesisHash itself, so that editing any
    recorded identity - pole convention, mounting state, iPolar software version,
    deployed plugin hash - after the fact is detectable.
#>
function Get-IPolarGenesisHash {
    param([Parameter(Mandatory)] $Header)

    $material = @{}
    if ($Header -is [System.Collections.IDictionary]) {
        foreach ($key in @($Header.Keys)) {
            if ([string]$key -eq "GenesisHash") { continue }
            $material[[string]$key] = $Header[$key]
        }
    } else {
        foreach ($property in $Header.PSObject.Properties) {
            if ($property.Name -eq "GenesisHash") { continue }
            $material[$property.Name] = $property.Value
        }
    }
    return (Get-IPolarTextSha256 -Text (ConvertTo-IPolarCanonicalJson $material))
}

<#
.SYNOPSIS
    Computes the chained hash of a single campaign event.
#>
function Get-IPolarEventHash {
    param([Parameter(Mandatory)] $Event)

    $material = [ordered]@{
        Sequence = Get-IPolarProperty -InputObject $Event -Name "Sequence"
        RecordedUtc = Get-IPolarProperty -InputObject $Event -Name "RecordedUtc"
        EventType = Get-IPolarProperty -InputObject $Event -Name "EventType"
        PreviousEventHash = Get-IPolarProperty -InputObject $Event -Name "PreviousEventHash"
        Payload = Get-IPolarProperty -InputObject $Event -Name "Payload"
    }
    return (Get-IPolarTextSha256 -Text (ConvertTo-IPolarCanonicalJson $material))
}

# --- Property and number access --------------------------------------------------

<#
.SYNOPSIS
    Reads a property without throwing under Set-StrictMode when it is absent.
#>
function Get-IPolarProperty {
    param($InputObject, [Parameter(Mandatory)] [string]$Name, $Default = $null)

    if ($null -eq $InputObject) { return $Default }
    if ($InputObject -is [System.Collections.IDictionary]) {
        if ($InputObject.Contains($Name)) { return $InputObject[$Name] }
        return $Default
    }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) { return $Default }
    if ($null -eq $property.Value) { return $Default }
    return $property.Value
}

<#
.SYNOPSIS
    Returns a finite double, or $null when the value is absent or not finite.
.DESCRIPTION
    An unknown value is never coerced to zero. Callers distinguish "absent" from
    "zero" by testing for $null.
#>
function Get-IPolarFiniteNumber {
    param($Value)

    if ($null -eq $Value) { return $null }
    if ($Value -is [bool]) { return $null }
    if ($Value -is [string]) {
        $parsed = 0.0
        if (-not [double]::TryParse($Value, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) { return $null }
        $Value = $parsed
    }
    try { $number = [double]$Value } catch { return $null }
    if ([double]::IsNaN($number) -or [double]::IsInfinity($number)) { return $null }
    return $number
}

<#
.SYNOPSIS
    Detects non-finite numbers anywhere in a payload tree.
#>
function Get-IPolarNonFinitePaths {
    param($Value, [string]$Path = "")

    $found = New-Object System.Collections.Generic.List[string]
    if ($null -eq $Value) { return @() }

    if ($Value -is [System.Management.Automation.PSCustomObject]) {
        foreach ($property in $Value.PSObject.Properties) {
            foreach ($hit in (Get-IPolarNonFinitePaths -Value $property.Value -Path ("$Path/" + $property.Name))) { [void]$found.Add($hit) }
        }
        return @($found)
    }
    if ($Value -is [string]) {
        # ConvertTo-Json renders NaN and Infinity as strings, so the text forms
        # must be rejected as well as the live double values.
        if (@("NaN", "Infinity", "-Infinity") -contains $Value) { [void]$found.Add($Path) }
        return @($found)
    }
    if ($Value -is [System.Double] -or $Value -is [System.Single]) {
        $number = [double]$Value
        if ([double]::IsNaN($number) -or [double]::IsInfinity($number)) { [void]$found.Add($Path) }
        return @($found)
    }
    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($key in @($Value.Keys)) {
            foreach ($hit in (Get-IPolarNonFinitePaths -Value $Value[$key] -Path ("$Path/$key"))) { [void]$found.Add($hit) }
        }
        return @($found)
    }
    if ($Value -is [System.Collections.IEnumerable]) {
        $index = 0
        foreach ($item in $Value) {
            foreach ($hit in (Get-IPolarNonFinitePaths -Value $item -Path ("$Path[$index]"))) { [void]$found.Add($hit) }
            $index++
        }
        return @($found)
    }
    return @($found)
}

function Get-IPolarSampleStatistics {
    param([double[]]$Values)

    $result = [ordered]@{
        Count = 0
        MeanArcsec = $null
        SampleStdDevArcsec = $null
        PeakToPeakArcsec = $null
    }
    if ($null -eq $Values) { return $result }
    $result.Count = $Values.Count
    if ($Values.Count -lt 1) { return $result }

    $mean = ($Values | Measure-Object -Average).Average
    $result.MeanArcsec = $mean
    $result.PeakToPeakArcsec = ($Values | Measure-Object -Maximum).Maximum - ($Values | Measure-Object -Minimum).Minimum
    if ($Values.Count -ge 2) {
        $sum = 0.0
        foreach ($value in $Values) { $sum += ($value - $mean) * ($value - $mean) }
        $result.SampleStdDevArcsec = [Math]::Sqrt($sum / ($Values.Count - 1))
    }
    return $result
}

# --- Campaign reading ------------------------------------------------------------

function Get-IPolarCampaignHeaderPath {
    param([Parameter(Mandatory)] [string]$CampaignPath)
    return (Join-Path $CampaignPath "campaign.json")
}

function Get-IPolarCampaignEventLogPath {
    param([Parameter(Mandatory)] [string]$CampaignPath)
    return (Join-Path $CampaignPath "events.jsonl")
}

<#
.SYNOPSIS
    Reads a campaign directory into the header/event object the evaluator consumes.
#>
function Read-IPolarCampaign {
    param([Parameter(Mandatory)] [string]$CampaignPath)

    $root = [IO.Path]::GetFullPath($CampaignPath)
    $headerPath = Get-IPolarCampaignHeaderPath -CampaignPath $root
    if (-not (Test-Path -LiteralPath $headerPath -PathType Leaf)) { throw "Campaign header not found: $headerPath" }

    $header = [IO.File]::ReadAllText($headerPath) | ConvertFrom-Json
    $eventLogPath = Get-IPolarCampaignEventLogPath -CampaignPath $root
    $events = New-Object System.Collections.Generic.List[object]
    if (Test-Path -LiteralPath $eventLogPath -PathType Leaf) {
        $lineNumber = 0
        foreach ($line in [IO.File]::ReadAllLines($eventLogPath)) {
            $lineNumber++
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            try { [void]$events.Add(($line | ConvertFrom-Json)) }
            catch { throw "Campaign event log line $lineNumber is not valid JSON: $($_.Exception.Message)" }
        }
    }

    # ToArray() rather than @(...): the array subexpression operator throws
    # "Argument types do not match" on List[object] in both Windows PowerShell 5.1
    # and PowerShell 7. Every List[object] in this file is converted the same way.
    return [pscustomobject]@{
        CampaignPath = $root
        Header = $header
        Events = $events.ToArray()
    }
}

function Read-IPolarPolicy {
    param([Parameter(Mandatory)] [string]$PolicyPath)

    $full = [IO.Path]::GetFullPath($PolicyPath)
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "Uncertainty policy not found: $full" }
    return ([IO.File]::ReadAllText($full) | ConvertFrom-Json)
}

# --- Evaluation ------------------------------------------------------------------

function Add-IPolarGate {
    param([Parameter(Mandatory)] $Gates, [Parameter(Mandatory)] [string]$Gate, [Parameter(Mandatory)] [string]$Reason)

    [void]$Gates.Add([ordered]@{ Gate = $Gate; Reason = $Reason })
}

function Get-IPolarEventsOfType {
    param($Events, [Parameter(Mandatory)] [string]$EventType)

    return @($Events | Where-Object { (Get-IPolarProperty -InputObject $_ -Name "EventType") -eq $EventType })
}

<#
.SYNOPSIS
    Extracts an authoritative numeric residual from an event payload.
.DESCRIPTION
    Returns a descriptor with Authoritative=$false when the residual is absent,
    unsourced, non-finite, implausible, or derived from OCR or screenshot pixel
    measurement. Screen pixels and OCR are never calibrated angles.
#>
function Get-IPolarResidualDescriptor {
    param($Residual, [Parameter(Mandatory)] [double]$MaximumPlausibleArcsec)

    $descriptor = [ordered]@{
        Present = $false
        Authoritative = $false
        ValueArcsec = $null
        Source = $null
        SourceKind = $null
        IsManual = $false
        Reason = "No numerical residual was supplied."
    }
    if ($null -eq $Residual) { return $descriptor }

    $descriptor.Present = $true
    $descriptor.Source = [string](Get-IPolarProperty -InputObject $Residual -Name "Source")
    $descriptor.SourceKind = [string](Get-IPolarProperty -InputObject $Residual -Name "SourceKind")
    $descriptor.IsManual = [bool](Get-IPolarProperty -InputObject $Residual -Name "IsManual" -Default $false)
    $value = Get-IPolarFiniteNumber (Get-IPolarProperty -InputObject $Residual -Name "ValueArcsec")

    if ($null -eq $value) {
        $descriptor.Reason = "Residual value is missing or not finite."
        return $descriptor
    }
    $descriptor.ValueArcsec = $value
    if ([Math]::Abs($value) -gt $MaximumPlausibleArcsec) {
        $descriptor.Reason = "Residual $value arcsec is outside the plausible range of +/-$MaximumPlausibleArcsec arcsec."
        return $descriptor
    }
    if ([string]::IsNullOrWhiteSpace($descriptor.Source)) {
        $descriptor.Reason = "Residual has no documented source."
        return $descriptor
    }
    if ((Get-IPolarAuthoritativeResidualSourceKinds) -notcontains $descriptor.SourceKind) {
        $descriptor.Reason = "Residual source kind '$($descriptor.SourceKind)' is not an authoritative numeric readout."
        return $descriptor
    }

    $descriptor.Authoritative = $true
    $descriptor.Reason = "Residual accepted from documented source kind '$($descriptor.SourceKind)'."
    return $descriptor
}

<#
.SYNOPSIS
    Evaluates a campaign and returns its qualification verdict.
.DESCRIPTION
    Pure: performs no file, network, or hardware access and reads no clock. The
    caller supplies EvaluatedUtc.

    The evaluator can only emit Rejected, QualitativeWitnessOnly,
    QuantitativeUnqualified, or QualifiedCorroboratingWitness. It cannot emit
    GroundTruth or CertifiedAbsoluteAccuracy under any input.
#>
function Invoke-IPolarCampaignEvaluation {
    param(
        [Parameter(Mandatory)] $Campaign,
        $Policy = $null,
        $ScreeningPolicy = $null,
        [Parameter(Mandatory)] [datetime]$EvaluatedUtc
    )

    if ($null -eq $ScreeningPolicy) { $ScreeningPolicy = Get-IPolarDefaultScreeningPolicy }
    $screen = @{}
    foreach ($key in (Get-IPolarDefaultScreeningPolicy).Keys) {
        $supplied = Get-IPolarProperty -InputObject $ScreeningPolicy -Name $key
        if ($null -eq $supplied) { $screen[$key] = (Get-IPolarDefaultScreeningPolicy)[$key] } else { $screen[$key] = $supplied }
    }

    $gates = New-Object System.Collections.Generic.List[object]
    $missing = New-Object System.Collections.Generic.List[string]
    $header = Get-IPolarProperty -InputObject $Campaign -Name "Header"
    $events = @(Get-IPolarProperty -InputObject $Campaign -Name "Events" -Default @())

    # --- Schema version -----------------------------------------------------
    $schemaVersion = [string](Get-IPolarProperty -InputObject $header -Name "SchemaVersion")
    $schemaMajor = $null
    if ([string]::IsNullOrWhiteSpace($schemaVersion)) {
        Add-IPolarGate $gates "SchemaVersion" "Campaign header has no SchemaVersion."
    } else {
        $majorText = ($schemaVersion -split '\.')[0]
        $parsedMajor = 0
        if ([int]::TryParse($majorText, [ref]$parsedMajor)) { $schemaMajor = $parsedMajor }
        if ($null -eq $schemaMajor) {
            Add-IPolarGate $gates "SchemaVersion" "Campaign SchemaVersion '$schemaVersion' is not parseable."
        } elseif ($schemaMajor -ne (Get-IPolarSupportedSchemaMajor)) {
            Add-IPolarGate $gates "SchemaVersion" "Campaign SchemaVersion '$schemaVersion' is not supported by evaluator schema $(Get-IPolarSchemaVersion)."
        }
    }

    # --- Requested qualification level --------------------------------------
    $requestedLevel = [string](Get-IPolarProperty -InputObject $header -Name "RequestedQualificationLevel" -Default "Unspecified")
    if ((Get-IPolarForbiddenQualificationLevels) -contains $requestedLevel) {
        Add-IPolarGate $gates "ForbiddenQualificationRequest" "Campaign requests qualification level '$requestedLevel'. iPolar can never be promoted to ground truth or certified absolute accuracy by this evaluator."
    } elseif ($requestedLevel -ne "Unspecified" -and (Get-IPolarQualificationLevels) -notcontains $requestedLevel) {
        Add-IPolarGate $gates "ForbiddenQualificationRequest" "Campaign requests unknown qualification level '$requestedLevel'."
    }

    # --- Hash chain and ordering --------------------------------------------
    $expectedPrevious = Get-IPolarGenesisHash -Header $header
    $recordedGenesis = [string](Get-IPolarProperty -InputObject $header -Name "GenesisHash")
    if ([string]::IsNullOrWhiteSpace($recordedGenesis)) {
        Add-IPolarGate $gates "GenesisHash" "Campaign header has no GenesisHash."
    } elseif ($recordedGenesis -ne $expectedPrevious) {
        Add-IPolarGate $gates "GenesisHash" "Campaign header GenesisHash does not match the header contents. The header was edited after creation."
    }

    $expectedSequence = 1
    $previousRecordedUtc = $null
    $chainValid = $true
    foreach ($campaignEvent in $events) {
        $sequence = Get-IPolarFiniteNumber (Get-IPolarProperty -InputObject $campaignEvent -Name "Sequence")
        $eventType = [string](Get-IPolarProperty -InputObject $campaignEvent -Name "EventType")
        $label = "sequence $sequence ($eventType)"

        if ($null -eq $sequence -or [int]$sequence -ne $expectedSequence) {
            Add-IPolarGate $gates "EventSequence" "Event at position $expectedSequence declares sequence '$sequence'. Campaign event sequence must increase by exactly one."
            $chainValid = $false
            break
        }
        if ((Get-IPolarEventTypes) -notcontains $eventType) {
            Add-IPolarGate $gates "EventType" "Event $label has an unknown event type."
            $chainValid = $false
        }

        $recordedUtcRaw = Get-IPolarProperty -InputObject $campaignEvent -Name "RecordedUtc"
        $recordedUtc = $null
        try { $recordedUtc = ConvertTo-IPolarUtc -Value $recordedUtcRaw } catch { $recordedUtc = $null }
        if ($null -eq $recordedUtc) {
            Add-IPolarGate $gates "EventTimestamp" "Event $label has no parseable UTC timestamp."
            $chainValid = $false
        } elseif ($null -ne $previousRecordedUtc -and $recordedUtc -lt $previousRecordedUtc) {
            Add-IPolarGate $gates "EventTimestamp" "Event $label is timestamped before its predecessor. Campaign event time must not move backwards."
            $chainValid = $false
        }
        if ($null -ne $recordedUtc) { $previousRecordedUtc = $recordedUtc }

        $declaredPrevious = [string](Get-IPolarProperty -InputObject $campaignEvent -Name "PreviousEventHash")
        if ($declaredPrevious -ne $expectedPrevious) {
            Add-IPolarGate $gates "HashChain" "Event $label does not chain to its predecessor. The event log was edited or reordered."
            $chainValid = $false
        }

        $declaredHash = [string](Get-IPolarProperty -InputObject $campaignEvent -Name "EventHash")
        $computedHash = $null
        try { $computedHash = Get-IPolarEventHash -Event $campaignEvent } catch { $computedHash = $null }
        if ($null -eq $computedHash) {
            Add-IPolarGate $gates "HashChain" "Event $label could not be hashed. Its payload contains a non-serializable value."
            $chainValid = $false
        } elseif ($declaredHash -ne $computedHash) {
            Add-IPolarGate $gates "HashChain" "Event $label content hash does not match its recorded EventHash. The event was edited after it was written."
            $chainValid = $false
        }

        $nonFinite = @(Get-IPolarNonFinitePaths -Value (Get-IPolarProperty -InputObject $campaignEvent -Name "Payload") -Path "Payload")
        foreach ($path in $nonFinite) {
            Add-IPolarGate $gates "FiniteValues" "Event $label carries a non-finite value at $path."
        }

        if ($null -ne $computedHash) { $expectedPrevious = $computedHash } else { $chainValid = $false; break }
        $expectedSequence++
    }

    # --- Campaign identity --------------------------------------------------
    foreach ($required in @("CampaignId", "RepositoryCommit", "DeployedPluginSha256", "MountingStateId")) {
        if ([string]::IsNullOrWhiteSpace([string](Get-IPolarProperty -InputObject $header -Name $required))) {
            [void]$missing.Add($required)
            Add-IPolarGate $gates "CampaignIdentity" "Campaign header is missing $required."
        }
    }

    # --- Pole convention ----------------------------------------------------
    $poleConvention = [string](Get-IPolarProperty -InputObject $header -Name "PoleConvention" -Default "Unknown")
    if ((Get-IPolarValidPoleConventions) -notcontains $poleConvention) {
        Add-IPolarGate $gates "PoleConvention" "Pole convention '$poleConvention' is not a recognized value."
    } elseif ($poleConvention -eq "Unknown") {
        [void]$missing.Add("PoleConvention")
        Add-IPolarGate $gates "PoleConvention" "Pole convention is Unknown. A comparison against TPPA is meaningless until the true-pole/apparent-pole convention of the iPolar readout is stated."
    }

    # --- Environment --------------------------------------------------------
    $environmentEvents = @(Get-IPolarEventsOfType -Events $events -EventType "Environment")
    $siteComplete = $false
    $atmosphereComplete = $false
    $atmosphereAgeMinutes = $null
    $siteSummary = [ordered]@{ LatitudeDegrees = $null; LongitudeDegrees = $null; ElevationMeters = $null; Source = $null }
    $atmosphereSummary = [ordered]@{ AbsoluteStationPressureHectopascals = $null; TemperatureCelsius = $null; RelativeHumidityPercent = $null; Source = $null; ObservedUtc = $null; AgeMinutes = $null }

    if ($environmentEvents.Count -eq 0) {
        [void]$missing.Add("Environment")
        Add-IPolarGate $gates "Site" "No environment event was recorded. Site coordinates are required."
        Add-IPolarGate $gates "Atmosphere" "No environment event was recorded. Station pressure, temperature, and humidity are required."
    } else {
        $latest = $environmentEvents[$environmentEvents.Count - 1]
        $payload = Get-IPolarProperty -InputObject $latest -Name "Payload"
        $site = Get-IPolarProperty -InputObject $payload -Name "Site"
        $atmosphere = Get-IPolarProperty -InputObject $payload -Name "Atmosphere"

        $latitude = Get-IPolarFiniteNumber (Get-IPolarProperty -InputObject $site -Name "LatitudeDegrees")
        $longitude = Get-IPolarFiniteNumber (Get-IPolarProperty -InputObject $site -Name "LongitudeDegrees")
        $elevation = Get-IPolarFiniteNumber (Get-IPolarProperty -InputObject $site -Name "ElevationMeters")
        $siteSource = [string](Get-IPolarProperty -InputObject $site -Name "Source")
        $siteSummary.LatitudeDegrees = $latitude
        $siteSummary.LongitudeDegrees = $longitude
        $siteSummary.ElevationMeters = $elevation
        $siteSummary.Source = $siteSource

        $siteProblems = New-Object System.Collections.Generic.List[string]
        if ($null -eq $latitude) { [void]$siteProblems.Add("latitude") } elseif ($latitude -lt -90.0 -or $latitude -gt 90.0) { [void]$siteProblems.Add("latitude out of range") }
        if ($null -eq $longitude) { [void]$siteProblems.Add("longitude") } elseif ($longitude -lt -180.0 -or $longitude -gt 180.0) { [void]$siteProblems.Add("longitude out of range") }
        if ($null -eq $elevation) { [void]$siteProblems.Add("elevation") } elseif ($elevation -lt -500.0 -or $elevation -gt 9000.0) { [void]$siteProblems.Add("elevation out of range") }
        if ([string]::IsNullOrWhiteSpace($siteSource)) { [void]$siteProblems.Add("source") }
        if ($siteProblems.Count -gt 0) {
            [void]$missing.Add("Site")
            Add-IPolarGate $gates "Site" "Site record is incomplete or implausible: $($siteProblems -join ', ')."
        } else { $siteComplete = $true }

        $pressure = Get-IPolarFiniteNumber (Get-IPolarProperty -InputObject $atmosphere -Name "AbsoluteStationPressureHectopascals")
        $temperature = Get-IPolarFiniteNumber (Get-IPolarProperty -InputObject $atmosphere -Name "TemperatureCelsius")
        $humidity = Get-IPolarFiniteNumber (Get-IPolarProperty -InputObject $atmosphere -Name "RelativeHumidityPercent")
        $atmosphereSource = [string](Get-IPolarProperty -InputObject $atmosphere -Name "Source")
        $atmosphereAgeMinutes = Get-IPolarFiniteNumber (Get-IPolarProperty -InputObject $atmosphere -Name "AgeMinutes")
        $atmosphereSummary.AbsoluteStationPressureHectopascals = $pressure
        $atmosphereSummary.TemperatureCelsius = $temperature
        $atmosphereSummary.RelativeHumidityPercent = $humidity
        $atmosphereSummary.Source = $atmosphereSource
        $atmosphereSummary.ObservedUtc = [string](Get-IPolarProperty -InputObject $atmosphere -Name "ObservedUtc")
        $atmosphereSummary.AgeMinutes = $atmosphereAgeMinutes

        $atmosphereProblems = New-Object System.Collections.Generic.List[string]
        if ($null -eq $pressure) { [void]$atmosphereProblems.Add("absolute station pressure") } elseif ($pressure -lt 300.0 -or $pressure -gt 1100.0) { [void]$atmosphereProblems.Add("station pressure out of range") }
        if ($null -eq $temperature) { [void]$atmosphereProblems.Add("temperature") } elseif ($temperature -lt -90.0 -or $temperature -gt 70.0) { [void]$atmosphereProblems.Add("temperature out of range") }
        if ($null -eq $humidity) { [void]$atmosphereProblems.Add("relative humidity") } elseif ($humidity -lt 0.0 -or $humidity -gt 100.0) { [void]$atmosphereProblems.Add("relative humidity out of range") }
        if ([string]::IsNullOrWhiteSpace($atmosphereSource)) { [void]$atmosphereProblems.Add("source") }
        if ($null -eq $atmosphereAgeMinutes) { [void]$atmosphereProblems.Add("age") }
        if ($atmosphereProblems.Count -gt 0) {
            [void]$missing.Add("Atmosphere")
            Add-IPolarGate $gates "Atmosphere" "Atmosphere record is incomplete or implausible: $($atmosphereProblems -join ', ')."
        } elseif ($atmosphereAgeMinutes -gt [double]$screen.MaximumAtmosphereAgeMinutes) {
            Add-IPolarGate $gates "Atmosphere" "Atmosphere reading is $([Math]::Round($atmosphereAgeMinutes, 1)) minutes old, beyond the $($screen.MaximumAtmosphereAgeMinutes) minute limit."
        } else { $atmosphereComplete = $true }
    }

    # --- Artifacts and dark frame -------------------------------------------
    $artifactEvents = @(Get-IPolarEventsOfType -Events $events -EventType "Artifact")
    $darkFrameEvents = @(Get-IPolarEventsOfType -Events $events -EventType "DarkFrame")
    $seenHashes = @{}
    $previousArtifactCreatedUtc = $null
    $artifactCount = 0
    $duplicateHashes = New-Object System.Collections.Generic.List[string]
    $qualitativeVerdictCounts = [ordered]@{}
    foreach ($verdict in (Get-IPolarQualitativeVerdicts)) { $qualitativeVerdictCounts[$verdict] = 0 }

    $orderedArtifactBearingEvents = @($events | Where-Object {
        $type = Get-IPolarProperty -InputObject $_ -Name "EventType"
        $type -eq "DarkFrame" -or $type -eq "Artifact"
    })

    foreach ($campaignEvent in $orderedArtifactBearingEvents) {
        $eventType = [string](Get-IPolarProperty -InputObject $campaignEvent -Name "EventType")
        $sequence = Get-IPolarProperty -InputObject $campaignEvent -Name "Sequence"
        $payload = Get-IPolarProperty -InputObject $campaignEvent -Name "Payload"
        $label = "sequence $sequence ($eventType)"

        $sha = [string](Get-IPolarProperty -InputObject $payload -Name "Sha256")
        $sizeBytes = Get-IPolarFiniteNumber (Get-IPolarProperty -InputObject $payload -Name "SizeBytes")
        $createdRaw = Get-IPolarProperty -InputObject $payload -Name "CreatedUtc"
        $storedPath = [string](Get-IPolarProperty -InputObject $payload -Name "StoredPath")

        if ([string]::IsNullOrWhiteSpace($sha)) {
            Add-IPolarGate $gates "ArtifactProvenance" "Artifact at $label has no SHA256."
        } elseif ($seenHashes.ContainsKey($sha)) {
            [void]$duplicateHashes.Add($sha)
            Add-IPolarGate $gates "ArtifactProvenance" "Artifact at $label reuses the content already recorded at $($seenHashes[$sha]). A reused capture cannot serve as a second independent observation."
        } else {
            $seenHashes[$sha] = $label
        }

        if ($null -eq $sizeBytes) {
            Add-IPolarGate $gates "ArtifactProvenance" "Artifact at $label has no size."
        } elseif ($sizeBytes -le 0) {
            Add-IPolarGate $gates "ArtifactProvenance" "Artifact at $label is empty."
        }
        if ([string]::IsNullOrWhiteSpace($storedPath)) {
            Add-IPolarGate $gates "ArtifactProvenance" "Artifact at $label has no preserved copy path."
        }

        $createdUtc = $null
        try { if ($null -ne $createdRaw) { $createdUtc = ConvertTo-IPolarUtc -Value $createdRaw } } catch { $createdUtc = $null }
        if ($null -eq $createdUtc) {
            Add-IPolarGate $gates "ArtifactProvenance" "Artifact at $label has no parseable creation time."
        } else {
            if ($null -ne $previousArtifactCreatedUtc -and $createdUtc -le $previousArtifactCreatedUtc) {
                Add-IPolarGate $gates "ArtifactFreshness" "Artifact at $label was created at or before the preceding artifact. Each capture must be provably newer than the one before it."
            }
            $previousArtifactCreatedUtc = $createdUtc
        }

        if ($eventType -eq "Artifact") {
            $artifactCount++
            $verdict = [string](Get-IPolarProperty -InputObject $payload -Name "QualitativeVerdict")
            if ((Get-IPolarQualitativeVerdicts) -contains $verdict) { $qualitativeVerdictCounts[$verdict] = $qualitativeVerdictCounts[$verdict] + 1 }
        }
    }

    $darkFrameSummary = [ordered]@{ Present = $false; CapturedUtc = $null; Sha256 = $null; AgeHours = $null; Stale = $null }
    if ($darkFrameEvents.Count -eq 0) {
        [void]$missing.Add("DarkFrame")
        Add-IPolarGate $gates "DarkFrame" "No dark frame was recorded. iPolar solve evidence without a dark frame reference is not admissible."
    } else {
        $latestDark = $darkFrameEvents[$darkFrameEvents.Count - 1]
        $darkPayload = Get-IPolarProperty -InputObject $latestDark -Name "Payload"
        $darkFrameSummary.Present = $true
        $darkFrameSummary.Sha256 = [string](Get-IPolarProperty -InputObject $darkPayload -Name "Sha256")
        $darkCaptured = $null
        try { $darkCaptured = ConvertTo-IPolarUtc -Value (Get-IPolarProperty -InputObject $darkPayload -Name "CapturedUtc") } catch { $darkCaptured = $null }
        if ($null -eq $darkCaptured) {
            Add-IPolarGate $gates "DarkFrame" "Dark frame has no parseable capture time."
        } else {
            $darkFrameSummary.CapturedUtc = Format-IPolarUtc -Value $darkCaptured
            $solveEventsForAge = @(Get-IPolarEventsOfType -Events $events -EventType "SolveAttempt")
            $referenceUtc = $EvaluatedUtc
            if ($solveEventsForAge.Count -gt 0) {
                $lastSolveUtc = $null
                try { $lastSolveUtc = ConvertTo-IPolarUtc -Value (Get-IPolarProperty -InputObject $solveEventsForAge[$solveEventsForAge.Count - 1] -Name "RecordedUtc") } catch { $lastSolveUtc = $null }
                if ($null -ne $lastSolveUtc) { $referenceUtc = $lastSolveUtc }
            }
            $ageHours = ($referenceUtc - $darkCaptured).TotalHours
            $darkFrameSummary.AgeHours = $ageHours
            $darkFrameSummary.Stale = ($ageHours -gt [double]$screen.MaximumDarkFrameAgeHours -or $ageHours -lt 0.0)
            if ($ageHours -lt 0.0) {
                Add-IPolarGate $gates "DarkFrame" "Dark frame is timestamped after the evidence it supports."
            } elseif ($ageHours -gt [double]$screen.MaximumDarkFrameAgeHours) {
                Add-IPolarGate $gates "DarkFrame" "Dark frame is $([Math]::Round($ageHours, 2)) hours old, beyond the $($screen.MaximumDarkFrameAgeHours) hour limit."
            }
        }
    }

    # --- Solve reliability --------------------------------------------------
    $solveEvents = @(Get-IPolarEventsOfType -Events $events -EventType "SolveAttempt")
    $solveSuccesses = 0
    $solveFailures = 0
    foreach ($solveEvent in $solveEvents) {
        $payload = Get-IPolarProperty -InputObject $solveEvent -Name "Payload"
        if ([bool](Get-IPolarProperty -InputObject $payload -Name "Succeeded" -Default $false)) { $solveSuccesses++ } else { $solveFailures++ }
    }
    $solveReliability = [ordered]@{
        Attempts = $solveEvents.Count
        Successes = $solveSuccesses
        Failures = $solveFailures
        SuccessRate = $null
        MinimumAttempts = [int]$screen.MinimumSolveAttempts
        MinimumSuccesses = [int]$screen.MinimumSolveSuccesses
        Passed = $false
    }
    if ($solveEvents.Count -gt 0) { $solveReliability.SuccessRate = [double]$solveSuccesses / [double]$solveEvents.Count }
    if ($solveEvents.Count -lt [int]$screen.MinimumSolveAttempts) {
        Add-IPolarGate $gates "SolveReliability" "Campaign has $($solveEvents.Count) solve attempts, below the required $($screen.MinimumSolveAttempts)."
    }
    if ($solveSuccesses -lt [int]$screen.MinimumSolveSuccesses) {
        Add-IPolarGate $gates "SolveReliability" "Campaign has $solveSuccesses successful solves, below the required $($screen.MinimumSolveSuccesses)."
    }
    $solveReliability.Passed = ($solveEvents.Count -ge [int]$screen.MinimumSolveAttempts -and $solveSuccesses -ge [int]$screen.MinimumSolveSuccesses)

    # --- Calibration and reseat repeatability --------------------------------
    $maximumPlausible = [double]$screen.MaximumPlausibleResidualArcsec
    $calibrationEvents = @(Get-IPolarEventsOfType -Events $events -EventType "FixedMountCalibration")
    $reseatEvents = @(Get-IPolarEventsOfType -Events $events -EventType "Reseat")

    $calibrationResiduals = New-Object System.Collections.Generic.List[double]
    $manualResidualCount = 0
    $nonAuthoritativeResidualCount = 0
    $residualSources = New-Object System.Collections.Generic.List[string]
    $residualRejections = New-Object System.Collections.Generic.List[string]

    foreach ($calibrationEvent in $calibrationEvents) {
        $payload = Get-IPolarProperty -InputObject $calibrationEvent -Name "Payload"
        if ([bool](Get-IPolarProperty -InputObject $payload -Name "CameraRemovedOrReseated" -Default $false)) {
            Add-IPolarGate $gates "FixedMountCalibration" "Fixed-mount calibration at sequence $(Get-IPolarProperty -InputObject $calibrationEvent -Name 'Sequence') reports the camera was removed or reseated. A fixed-mount cycle must leave the camera untouched."
        }
        $descriptor = Get-IPolarResidualDescriptor -Residual (Get-IPolarProperty -InputObject $payload -Name "Residual") -MaximumPlausibleArcsec $maximumPlausible
        if ($descriptor.Authoritative) {
            [void]$calibrationResiduals.Add([double]$descriptor.ValueArcsec)
            if (-not [string]::IsNullOrWhiteSpace($descriptor.Source)) { [void]$residualSources.Add($descriptor.Source) }
            if ($descriptor.IsManual) { $manualResidualCount++ }
        } elseif ($descriptor.Present) {
            $nonAuthoritativeResidualCount++
            [void]$residualRejections.Add("Fixed-mount calibration at sequence $(Get-IPolarProperty -InputObject $calibrationEvent -Name 'Sequence'): $($descriptor.Reason)")
        }
    }

    $reseatResiduals = New-Object System.Collections.Generic.List[double]
    $reseatRecalibrated = 0
    foreach ($reseatEvent in $reseatEvents) {
        $payload = Get-IPolarProperty -InputObject $reseatEvent -Name "Payload"
        if ([bool](Get-IPolarProperty -InputObject $payload -Name "RecalibrationPerformed" -Default $false)) { $reseatRecalibrated++ }
        $descriptor = Get-IPolarResidualDescriptor -Residual (Get-IPolarProperty -InputObject $payload -Name "Residual") -MaximumPlausibleArcsec $maximumPlausible
        if ($descriptor.Authoritative) {
            [void]$reseatResiduals.Add([double]$descriptor.ValueArcsec)
            if (-not [string]::IsNullOrWhiteSpace($descriptor.Source)) { [void]$residualSources.Add($descriptor.Source) }
            if ($descriptor.IsManual) { $manualResidualCount++ }
        } elseif ($descriptor.Present) {
            $nonAuthoritativeResidualCount++
            [void]$residualRejections.Add("Reseat at sequence $(Get-IPolarProperty -InputObject $reseatEvent -Name 'Sequence'): $($descriptor.Reason)")
        }
    }

    if ($calibrationEvents.Count -lt [int]$screen.MinimumFixedMountCalibrationCycles) {
        Add-IPolarGate $gates "FixedMountCalibration" "Campaign has $($calibrationEvents.Count) fixed-mount camera-centre calibration cycles, below the required $($screen.MinimumFixedMountCalibrationCycles)."
    }
    if ($reseatEvents.Count -lt [int]$screen.MinimumReseatCycles) {
        Add-IPolarGate $gates "ReseatRepeatability" "Campaign has $($reseatEvents.Count) remove/reseat/recalibration cycles, below the required $($screen.MinimumReseatCycles)."
    }
    if ($reseatEvents.Count -gt 0 -and $reseatRecalibrated -lt $reseatEvents.Count) {
        Add-IPolarGate $gates "ReseatRepeatability" "$($reseatEvents.Count - $reseatRecalibrated) reseat cycles have no recorded recalibration. Mounting-transfer qualification requires recalibration after every reseat."
    }

    $calibrationStatistics = Get-IPolarSampleStatistics -Values @($calibrationResiduals)
    $reseatStatistics = Get-IPolarSampleStatistics -Values @($reseatResiduals)

    $fixedMountSummary = [ordered]@{
        Cycles = $calibrationEvents.Count
        MinimumCycles = [int]$screen.MinimumFixedMountCalibrationCycles
        NumericResidualCount = $calibrationStatistics.Count
        MeanArcsec = $calibrationStatistics.MeanArcsec
        SampleStdDevArcsec = $calibrationStatistics.SampleStdDevArcsec
        PeakToPeakArcsec = $calibrationStatistics.PeakToPeakArcsec
        ScatterAvailable = ($null -ne $calibrationStatistics.SampleStdDevArcsec)
        Passed = ($calibrationEvents.Count -ge [int]$screen.MinimumFixedMountCalibrationCycles)
    }
    $reseatSummary = [ordered]@{
        Cycles = $reseatEvents.Count
        MinimumCycles = [int]$screen.MinimumReseatCycles
        RecalibratedCycles = $reseatRecalibrated
        NumericResidualCount = $reseatStatistics.Count
        MeanArcsec = $reseatStatistics.MeanArcsec
        SampleStdDevArcsec = $reseatStatistics.SampleStdDevArcsec
        PeakToPeakArcsec = $reseatStatistics.PeakToPeakArcsec
        ScatterAvailable = ($null -ne $reseatStatistics.SampleStdDevArcsec)
        Passed = ($reseatEvents.Count -ge [int]$screen.MinimumReseatCycles -and $reseatRecalibrated -eq $reseatEvents.Count)
    }

    # --- No-motion A/B/A blocks ---------------------------------------------
    $blockEvents = @(Get-IPolarEventsOfType -Events $events -EventType "NoMotionBlock")
    $blockSummaries = New-Object System.Collections.Generic.List[object]
    foreach ($blockEvent in $blockEvents) {
        $payload = Get-IPolarProperty -InputObject $blockEvent -Name "Payload"
        $blockId = [string](Get-IPolarProperty -InputObject $payload -Name "BlockId")
        $startUtc = $null
        $endUtc = $null
        try { $startUtc = ConvertTo-IPolarUtc -Value (Get-IPolarProperty -InputObject $payload -Name "StartUtc") } catch { $startUtc = $null }
        try { $endUtc = ConvertTo-IPolarUtc -Value (Get-IPolarProperty -InputObject $payload -Name "EndUtc") } catch { $endUtc = $null }
        $legs = @(Get-IPolarProperty -InputObject $payload -Name "Legs" -Default @())
        $legLabels = @($legs | ForEach-Object { [string](Get-IPolarProperty -InputObject $_ -Name "Label") })

        $reseatsInside = New-Object System.Collections.Generic.List[string]
        if ($null -eq $startUtc -or $null -eq $endUtc) {
            Add-IPolarGate $gates "NoMotionBlock" "Block '$blockId' has no parseable start/end window."
        } elseif ($endUtc -lt $startUtc) {
            Add-IPolarGate $gates "NoMotionBlock" "Block '$blockId' ends before it starts."
        } else {
            foreach ($reseatEvent in $reseatEvents) {
                $reseatUtc = $null
                try { $reseatUtc = ConvertTo-IPolarUtc -Value (Get-IPolarProperty -InputObject $reseatEvent -Name "RecordedUtc") } catch { $reseatUtc = $null }
                if ($null -ne $reseatUtc -and $reseatUtc -ge $startUtc -and $reseatUtc -le $endUtc) {
                    [void]$reseatsInside.Add([string](Get-IPolarProperty -InputObject $reseatEvent -Name "Sequence"))
                }
            }
            if ($reseatsInside.Count -gt 0) {
                Add-IPolarGate $gates "NoMotionBlock" "Block '$blockId' contains reseat events at sequence $($reseatsInside -join ', '). iPolar must never be reseated inside a no-motion A/B/A block."
            }
        }

        [void]$blockSummaries.Add([ordered]@{
            BlockId = $blockId
            StartUtc = if ($null -ne $startUtc) { Format-IPolarUtc -Value $startUtc } else { $null }
            EndUtc = if ($null -ne $endUtc) { Format-IPolarUtc -Value $endUtc } else { $null }
            LegSequence = $legLabels
            ReseatSequencesInsideBlock = @($reseatsInside)
        })
    }

    # --- TPPA links ---------------------------------------------------------
    $tppaLinkEvents = @(Get-IPolarEventsOfType -Events $events -EventType "TppaArtifactLink")
    $tppaLinks = New-Object System.Collections.Generic.List[object]
    foreach ($linkEvent in $tppaLinkEvents) {
        $payload = Get-IPolarProperty -InputObject $linkEvent -Name "Payload"
        [void]$tppaLinks.Add([ordered]@{
            TppaRunId = [string](Get-IPolarProperty -InputObject $payload -Name "TppaRunId")
            ArtifactPath = [string](Get-IPolarProperty -InputObject $payload -Name "ArtifactPath")
            Sha256 = [string](Get-IPolarProperty -InputObject $payload -Name "Sha256")
            Description = [string](Get-IPolarProperty -InputObject $payload -Name "Description")
        })
    }

    # --- Evidence classification --------------------------------------------
    $authoritativeResidualCount = $calibrationStatistics.Count + $reseatStatistics.Count
    $qualitativeCount = 0
    foreach ($verdict in (Get-IPolarQualitativeVerdicts)) { $qualitativeCount += $qualitativeVerdictCounts[$verdict] }

    $numericEvidence = [ordered]@{
        AuthoritativeResidualCount = $authoritativeResidualCount
        NonAuthoritativeResidualCount = $nonAuthoritativeResidualCount
        ManualResidualCount = $manualResidualCount
        Sources = @($residualSources | Sort-Object -Unique)
        RejectedResiduals = @($residualRejections)
    }
    $qualitativeEvidence = [ordered]@{
        ArtifactCount = $artifactCount
        VerdictCounts = $qualitativeVerdictCounts
        TotalVerdicts = $qualitativeCount
    }

    # --- Uncertainty policy --------------------------------------------------
    $policySummary = [ordered]@{
        Supplied = ($null -ne $Policy)
        PolicyId = $null
        Source = $null
        MaximumFixedMountScatterArcsec = $null
        MaximumReseatScatterArcsec = $null
        ReadoutQuantizationArcsec = $null
        Satisfied = $false
        Reasons = @()
    }
    $policyReasons = New-Object System.Collections.Generic.List[string]
    if ($null -eq $Policy) {
        [void]$policyReasons.Add("No explicit uncertainty policy was supplied. iPolar readout uncertainty has never been characterized, so no agreement threshold may be assumed.")
    } else {
        $policySummary.PolicyId = [string](Get-IPolarProperty -InputObject $Policy -Name "PolicyId")
        $policySummary.Source = [string](Get-IPolarProperty -InputObject $Policy -Name "Source")
        $maximumFixed = Get-IPolarFiniteNumber (Get-IPolarProperty -InputObject $Policy -Name "MaximumFixedMountScatterArcsec")
        $maximumReseat = Get-IPolarFiniteNumber (Get-IPolarProperty -InputObject $Policy -Name "MaximumReseatScatterArcsec")
        $quantization = Get-IPolarFiniteNumber (Get-IPolarProperty -InputObject $Policy -Name "ReadoutQuantizationArcsec")
        $policySummary.MaximumFixedMountScatterArcsec = $maximumFixed
        $policySummary.MaximumReseatScatterArcsec = $maximumReseat
        $policySummary.ReadoutQuantizationArcsec = $quantization

        if ([string]::IsNullOrWhiteSpace($policySummary.PolicyId)) { [void]$policyReasons.Add("Policy has no PolicyId.") }
        if ([string]::IsNullOrWhiteSpace($policySummary.Source)) { [void]$policyReasons.Add("Policy has no documented Source. An uncertainty policy must state who set it and on what evidence.") }
        if ($null -eq $maximumFixed -or $maximumFixed -le 0.0) { [void]$policyReasons.Add("Policy has no positive MaximumFixedMountScatterArcsec.") }
        if ($null -eq $maximumReseat -or $maximumReseat -le 0.0) { [void]$policyReasons.Add("Policy has no positive MaximumReseatScatterArcsec.") }
        if ($null -eq $quantization -or $quantization -le 0.0) { [void]$policyReasons.Add("Policy has no positive ReadoutQuantizationArcsec. Readout quantization must be measured, not assumed from the vendor resolution claim.") }

        if (-not $fixedMountSummary.ScatterAvailable) {
            [void]$policyReasons.Add("Fixed-mount calibration scatter is not computable. At least two authoritative numeric residuals are required.")
        } elseif ($null -ne $maximumFixed -and $fixedMountSummary.SampleStdDevArcsec -gt $maximumFixed) {
            [void]$policyReasons.Add("Fixed-mount calibration scatter $([Math]::Round($fixedMountSummary.SampleStdDevArcsec, 3)) arcsec exceeds the policy limit of $maximumFixed arcsec.")
        }
        if (-not $reseatSummary.ScatterAvailable) {
            [void]$policyReasons.Add("Reseat scatter is not computable. At least two authoritative numeric residuals are required.")
        } elseif ($null -ne $maximumReseat -and $reseatSummary.SampleStdDevArcsec -gt $maximumReseat) {
            [void]$policyReasons.Add("Reseat scatter $([Math]::Round($reseatSummary.SampleStdDevArcsec, 3)) arcsec exceeds the policy limit of $maximumReseat arcsec.")
        }
    }
    $policySummary.Satisfied = ($null -ne $Policy -and $policyReasons.Count -eq 0)
    $policySummary.Reasons = @($policyReasons)

    # --- Level determination -------------------------------------------------
    $level = "Rejected"
    if ($gates.Count -eq 0 -and $chainValid) {
        if ($authoritativeResidualCount -eq 0) {
            if ($qualitativeCount -gt 0) {
                $level = "QualitativeWitnessOnly"
            } else {
                $level = "Rejected"
                Add-IPolarGate $gates "Evidence" "Campaign contains neither authoritative numerical evidence nor a qualitative cross/circle verdict."
            }
        } elseif ($policySummary.Satisfied) {
            $level = "QualifiedCorroboratingWitness"
        } else {
            $level = "QuantitativeUnqualified"
        }
    }

    if ((Get-IPolarForbiddenQualificationLevels) -contains $level) {
        throw "Internal invariant violated: the iPolar evaluator must never emit '$level'."
    }

    return [pscustomobject]@{
        SchemaVersion = Get-IPolarSchemaVersion
        CampaignSchemaVersion = $schemaVersion
        CampaignId = [string](Get-IPolarProperty -InputObject $header -Name "CampaignId")
        EvaluatedUtc = Format-IPolarUtc -Value $EvaluatedUtc
        QualificationLevel = $level
        IntegrityValid = ($chainValid -and -not (@($gates | Where-Object { @("HashChain", "EventSequence", "EventTimestamp", "GenesisHash", "EventType") -contains $_.Gate }).Count -gt 0))
        ChainHeadHash = $expectedPrevious
        Provenance = [ordered]@{
            RepositoryCommit = [string](Get-IPolarProperty -InputObject $header -Name "RepositoryCommit")
            DeployedPluginSha256 = [string](Get-IPolarProperty -InputObject $header -Name "DeployedPluginSha256")
            MountIdentifier = [string](Get-IPolarProperty -InputObject $header -Name "MountIdentifier")
            IPolarIdentifier = [string](Get-IPolarProperty -InputObject $header -Name "IPolarIdentifier")
            IPolarHardwareVariant = [string](Get-IPolarProperty -InputObject $header -Name "IPolarHardwareVariant")
            IPolarAdapter = [string](Get-IPolarProperty -InputObject $header -Name "IPolarAdapter")
            IPolarSoftwareVersion = [string](Get-IPolarProperty -InputObject $header -Name "IPolarSoftwareVersion")
            MountingStateId = [string](Get-IPolarProperty -InputObject $header -Name "MountingStateId")
            CreatedUtc = [string](Get-IPolarProperty -InputObject $header -Name "CreatedUtc")
            EventCount = $events.Count
        }
        PoleConvention = $poleConvention
        Site = $siteSummary
        Atmosphere = $atmosphereSummary
        DarkFrame = $darkFrameSummary
        SolveReliability = $solveReliability
        FixedMountCalibration = $fixedMountSummary
        ReseatRepeatability = $reseatSummary
        NumericEvidence = $numericEvidence
        QualitativeEvidence = $qualitativeEvidence
        Artifacts = [ordered]@{
            Count = $artifactCount
            DuplicateSha256 = @($duplicateHashes | Sort-Object -Unique)
        }
        NoMotionBlocks = $blockSummaries.ToArray()
        TppaArtifactLinks = $tppaLinks.ToArray()
        UncertaintyPolicy = $policySummary
        MissingPrerequisites = @($missing | Sort-Object -Unique)
        FailedGates = $gates.ToArray()
        ControlAuthority = Get-IPolarControlAuthorityStatement
        AbsoluteCertification = Get-IPolarCertificationOutstandingStatement
    }
}

# --- Reporting -------------------------------------------------------------------

function Format-IPolarReportNumber {
    param($Value, [string]$Format = "F3", [string]$Unit = "")

    if ($null -eq $Value) { return "not available" }
    $number = Get-IPolarFiniteNumber $Value
    if ($null -eq $number) { return "not available" }
    $text = $number.ToString($Format, [Globalization.CultureInfo]::InvariantCulture)
    if ([string]::IsNullOrEmpty($Unit)) { return $text }
    return "$text $Unit"
}

function Format-IPolarReportText {
    param($Value)

    if ($null -eq $Value) { return "not recorded" }
    $text = [string]$Value
    if ([string]::IsNullOrWhiteSpace($text)) { return "not recorded" }
    return $text
}

function Get-IPolarQualificationMeaning {
    param([Parameter(Mandatory)] [string]$Level)

    switch ($Level) {
        "Rejected" { return "The campaign failed at least one hard gate. It supports no iPolar claim at all." }
        "QualitativeWitnessOnly" { return "Visual cross/circle evidence only. It can corroborate direction of agreement qualitatively and cannot certify any numerical result." }
        "QuantitativeUnqualified" { return "Numerical evidence exists but iPolar readout uncertainty is not characterized. No TPPA/iPolar agreement threshold may be applied." }
        "QualifiedCorroboratingWitness" { return "Calibration repeatability, reseat uncertainty, pole convention, and readout provenance all pass an explicitly supplied policy. iPolar corroborates TPPA. It is still not ground truth and still cannot certify absolute accuracy." }
        default { return "Unknown qualification level." }
    }
}

<#
.SYNOPSIS
    Builds the full Markdown report lines from an evaluation object.
#>
function New-IPolarCampaignReportMarkdown {
    param([Parameter(Mandatory)] $Evaluation)

    $lines = New-Object System.Collections.Generic.List[string]
    [void]$lines.Add("# iPolar Witness Campaign Report")
    [void]$lines.Add("")
    [void]$lines.Add("Campaign: $(Format-IPolarReportText $Evaluation.CampaignId)")
    [void]$lines.Add("Campaign schema: $(Format-IPolarReportText $Evaluation.CampaignSchemaVersion)")
    [void]$lines.Add("Evaluated: $($Evaluation.EvaluatedUtc)")
    [void]$lines.Add("Event chain head: $(Format-IPolarReportText $Evaluation.ChainHeadHash)")
    [void]$lines.Add("")
    [void]$lines.Add("## Qualification")
    [void]$lines.Add("")
    [void]$lines.Add("**$($Evaluation.QualificationLevel)** - $(Get-IPolarQualificationMeaning -Level $Evaluation.QualificationLevel)")
    [void]$lines.Add("")
    [void]$lines.Add($Evaluation.ControlAuthority)
    [void]$lines.Add("")
    [void]$lines.Add($Evaluation.AbsoluteCertification)
    [void]$lines.Add("")
    [void]$lines.Add("The manufacturer's stated iPolar resolution is a vendor specification, not a characterized statistical uncertainty. This evaluator never treats it as a one-sigma value and applies no TPPA/iPolar agreement threshold.")
    [void]$lines.Add("")
    [void]$lines.Add("## Provenance")
    [void]$lines.Add("")
    [void]$lines.Add("| Field | Value |")
    [void]$lines.Add("| --- | --- |")
    foreach ($name in $Evaluation.Provenance.Keys) {
        [void]$lines.Add("| $name | $(Format-IPolarReportText $Evaluation.Provenance[$name]) |")
    }
    [void]$lines.Add("| Integrity valid | $($Evaluation.IntegrityValid) |")
    [void]$lines.Add("| Pole convention | $(Format-IPolarReportText $Evaluation.PoleConvention) |")
    [void]$lines.Add("")
    [void]$lines.Add("## Site and atmosphere")
    [void]$lines.Add("")
    [void]$lines.Add("| Field | Value |")
    [void]$lines.Add("| --- | --- |")
    [void]$lines.Add("| Latitude | $(Format-IPolarReportNumber $Evaluation.Site.LatitudeDegrees 'F6' 'deg') |")
    [void]$lines.Add("| Longitude | $(Format-IPolarReportNumber $Evaluation.Site.LongitudeDegrees 'F6' 'deg') |")
    [void]$lines.Add("| Elevation | $(Format-IPolarReportNumber $Evaluation.Site.ElevationMeters 'F1' 'm') |")
    [void]$lines.Add("| Site source | $(Format-IPolarReportText $Evaluation.Site.Source) |")
    [void]$lines.Add("| Absolute station pressure | $(Format-IPolarReportNumber $Evaluation.Atmosphere.AbsoluteStationPressureHectopascals 'F1' 'hPa') |")
    [void]$lines.Add("| Temperature | $(Format-IPolarReportNumber $Evaluation.Atmosphere.TemperatureCelsius 'F1' 'C') |")
    [void]$lines.Add("| Relative humidity | $(Format-IPolarReportNumber $Evaluation.Atmosphere.RelativeHumidityPercent 'F1' '%') |")
    [void]$lines.Add("| Atmosphere source | $(Format-IPolarReportText $Evaluation.Atmosphere.Source) |")
    [void]$lines.Add("| Atmosphere age | $(Format-IPolarReportNumber $Evaluation.Atmosphere.AgeMinutes 'F1' 'min') |")
    [void]$lines.Add("| Dark frame captured | $(Format-IPolarReportText $Evaluation.DarkFrame.CapturedUtc) |")
    [void]$lines.Add("| Dark frame SHA256 | $(Format-IPolarReportText $Evaluation.DarkFrame.Sha256) |")
    [void]$lines.Add("| Dark frame age | $(Format-IPolarReportNumber $Evaluation.DarkFrame.AgeHours 'F2' 'h') |")
    [void]$lines.Add("")
    [void]$lines.Add("## Solve reliability")
    [void]$lines.Add("")
    [void]$lines.Add("| Attempts | Successes | Failures | Success rate | Required attempts | Required successes | Passed |")
    [void]$lines.Add("| ---: | ---: | ---: | ---: | ---: | ---: | --- |")
    [void]$lines.Add("| $($Evaluation.SolveReliability.Attempts) | $($Evaluation.SolveReliability.Successes) | $($Evaluation.SolveReliability.Failures) | $(Format-IPolarReportNumber $Evaluation.SolveReliability.SuccessRate 'F3') | $($Evaluation.SolveReliability.MinimumAttempts) | $($Evaluation.SolveReliability.MinimumSuccesses) | $($Evaluation.SolveReliability.Passed) |")
    [void]$lines.Add("")
    [void]$lines.Add("## Repeatability")
    [void]$lines.Add("")
    [void]$lines.Add("| Experiment | Cycles | Required | Numeric residuals | Mean | Sample SD | Peak to peak | Passed |")
    [void]$lines.Add("| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |")
    [void]$lines.Add("| Fixed-mount camera-centre calibration | $($Evaluation.FixedMountCalibration.Cycles) | $($Evaluation.FixedMountCalibration.MinimumCycles) | $($Evaluation.FixedMountCalibration.NumericResidualCount) | $(Format-IPolarReportNumber $Evaluation.FixedMountCalibration.MeanArcsec 'F3' 'arcsec') | $(Format-IPolarReportNumber $Evaluation.FixedMountCalibration.SampleStdDevArcsec 'F3' 'arcsec') | $(Format-IPolarReportNumber $Evaluation.FixedMountCalibration.PeakToPeakArcsec 'F3' 'arcsec') | $($Evaluation.FixedMountCalibration.Passed) |")
    [void]$lines.Add("| Remove / reseat / recalibrate | $($Evaluation.ReseatRepeatability.Cycles) | $($Evaluation.ReseatRepeatability.MinimumCycles) | $($Evaluation.ReseatRepeatability.NumericResidualCount) | $(Format-IPolarReportNumber $Evaluation.ReseatRepeatability.MeanArcsec 'F3' 'arcsec') | $(Format-IPolarReportNumber $Evaluation.ReseatRepeatability.SampleStdDevArcsec 'F3' 'arcsec') | $(Format-IPolarReportNumber $Evaluation.ReseatRepeatability.PeakToPeakArcsec 'F3' 'arcsec') | $($Evaluation.ReseatRepeatability.Passed) |")
    [void]$lines.Add("")
    [void]$lines.Add("## Evidence status")
    [void]$lines.Add("")
    [void]$lines.Add("- Authoritative numeric residuals: $($Evaluation.NumericEvidence.AuthoritativeResidualCount)")
    [void]$lines.Add("- Residuals rejected as non-authoritative: $($Evaluation.NumericEvidence.NonAuthoritativeResidualCount)")
    [void]$lines.Add("- Manually entered residuals among the authoritative set: $($Evaluation.NumericEvidence.ManualResidualCount)")
    [void]$lines.Add("- Qualitative cross/circle verdicts: $($Evaluation.QualitativeEvidence.TotalVerdicts) across $($Evaluation.QualitativeEvidence.ArtifactCount) artifacts")
    foreach ($verdict in $Evaluation.QualitativeEvidence.VerdictCounts.Keys) {
        [void]$lines.Add("  - $verdict : $($Evaluation.QualitativeEvidence.VerdictCounts[$verdict])")
    }
    if ($Evaluation.NumericEvidence.RejectedResiduals.Count -gt 0) {
        [void]$lines.Add("")
        [void]$lines.Add("Rejected numeric readouts:")
        foreach ($rejection in $Evaluation.NumericEvidence.RejectedResiduals) { [void]$lines.Add("- $rejection") }
    }
    [void]$lines.Add("")
    [void]$lines.Add("## Uncertainty policy")
    [void]$lines.Add("")
    [void]$lines.Add("- Supplied: $($Evaluation.UncertaintyPolicy.Supplied)")
    [void]$lines.Add("- Policy id: $(Format-IPolarReportText $Evaluation.UncertaintyPolicy.PolicyId)")
    [void]$lines.Add("- Source: $(Format-IPolarReportText $Evaluation.UncertaintyPolicy.Source)")
    [void]$lines.Add("- Satisfied: $($Evaluation.UncertaintyPolicy.Satisfied)")
    foreach ($reason in $Evaluation.UncertaintyPolicy.Reasons) { [void]$lines.Add("- $reason") }
    [void]$lines.Add("")
    [void]$lines.Add("## No-motion A/B/A blocks")
    [void]$lines.Add("")
    if ($Evaluation.NoMotionBlocks.Count -eq 0) {
        [void]$lines.Add("No no-motion block was recorded.")
    } else {
        [void]$lines.Add("| Block | Start | End | Leg sequence | Reseats inside block |")
        [void]$lines.Add("| --- | --- | --- | --- | --- |")
        foreach ($block in $Evaluation.NoMotionBlocks) {
            $legText = if ($block.LegSequence.Count -gt 0) { $block.LegSequence -join " -> " } else { "none" }
            $reseatText = if ($block.ReseatSequencesInsideBlock.Count -gt 0) { $block.ReseatSequencesInsideBlock -join ", " } else { "none" }
            [void]$lines.Add("| $(Format-IPolarReportText $block.BlockId) | $(Format-IPolarReportText $block.StartUtc) | $(Format-IPolarReportText $block.EndUtc) | $legText | $reseatText |")
        }
    }
    [void]$lines.Add("")
    [void]$lines.Add("## Linked TPPA artifacts")
    [void]$lines.Add("")
    if ($Evaluation.TppaArtifactLinks.Count -eq 0) {
        [void]$lines.Add("No TPPA artifact was linked.")
    } else {
        [void]$lines.Add("| TPPA run | Artifact | SHA256 | Description |")
        [void]$lines.Add("| --- | --- | --- | --- |")
        foreach ($link in $Evaluation.TppaArtifactLinks) {
            [void]$lines.Add("| $(Format-IPolarReportText $link.TppaRunId) | $(Format-IPolarReportText $link.ArtifactPath) | $(Format-IPolarReportText $link.Sha256) | $(Format-IPolarReportText $link.Description) |")
        }
    }
    [void]$lines.Add("")
    [void]$lines.Add("## Missing prerequisites")
    [void]$lines.Add("")
    if ($Evaluation.MissingPrerequisites.Count -eq 0) {
        [void]$lines.Add("None.")
    } else {
        foreach ($item in $Evaluation.MissingPrerequisites) { [void]$lines.Add("- $item") }
    }
    [void]$lines.Add("")
    [void]$lines.Add("## Failed gates")
    [void]$lines.Add("")
    if ($Evaluation.FailedGates.Count -eq 0) {
        [void]$lines.Add("None.")
    } else {
        [void]$lines.Add("| Gate | Reason |")
        [void]$lines.Add("| --- | --- |")
        foreach ($gate in $Evaluation.FailedGates) { [void]$lines.Add("| $($gate.Gate) | $($gate.Reason) |") }
    }
    [void]$lines.Add("")
    return @($lines)
}

<#
.SYNOPSIS
    Builds the compact council-review packet.
.DESCRIPTION
    Carries the same facts as the full report but references artifacts only by
    path and SHA256, so no proprietary vendor imagery is redistributed.
#>
function New-IPolarCouncilPacketMarkdown {
    param([Parameter(Mandatory)] $Evaluation)

    $lines = New-Object System.Collections.Generic.List[string]
    [void]$lines.Add("# iPolar Witness Campaign - Council Packet")
    [void]$lines.Add("")
    [void]$lines.Add("Campaign: $(Format-IPolarReportText $Evaluation.CampaignId)")
    [void]$lines.Add("Evaluated: $($Evaluation.EvaluatedUtc)")
    [void]$lines.Add("Repository commit: $(Format-IPolarReportText $Evaluation.Provenance.RepositoryCommit)")
    [void]$lines.Add("Deployed plugin SHA256: $(Format-IPolarReportText $Evaluation.Provenance.DeployedPluginSha256)")
    [void]$lines.Add("Event chain head: $(Format-IPolarReportText $Evaluation.ChainHeadHash)")
    [void]$lines.Add("Integrity valid: $($Evaluation.IntegrityValid)")
    [void]$lines.Add("")
    [void]$lines.Add("This packet contains no raw iPolar imagery. Artifacts are referenced by path and SHA256 only.")
    [void]$lines.Add("")
    [void]$lines.Add("## Verdict")
    [void]$lines.Add("")
    [void]$lines.Add("**$($Evaluation.QualificationLevel)**")
    [void]$lines.Add("")
    [void]$lines.Add(( Get-IPolarQualificationMeaning -Level $Evaluation.QualificationLevel ))
    [void]$lines.Add("")
    [void]$lines.Add("## Facts")
    [void]$lines.Add("")
    [void]$lines.Add("| Fact | Value |")
    [void]$lines.Add("| --- | --- |")
    [void]$lines.Add("| Pole convention | $(Format-IPolarReportText $Evaluation.PoleConvention) |")
    [void]$lines.Add("| Solve attempts / successes | $($Evaluation.SolveReliability.Attempts) / $($Evaluation.SolveReliability.Successes) |")
    [void]$lines.Add("| Fixed-mount cycles | $($Evaluation.FixedMountCalibration.Cycles) |")
    [void]$lines.Add("| Fixed-mount scatter (sample SD) | $(Format-IPolarReportNumber $Evaluation.FixedMountCalibration.SampleStdDevArcsec 'F3' 'arcsec') |")
    [void]$lines.Add("| Reseat cycles | $($Evaluation.ReseatRepeatability.Cycles) |")
    [void]$lines.Add("| Reseat scatter (sample SD) | $(Format-IPolarReportNumber $Evaluation.ReseatRepeatability.SampleStdDevArcsec 'F3' 'arcsec') |")
    [void]$lines.Add("| Authoritative numeric residuals | $($Evaluation.NumericEvidence.AuthoritativeResidualCount) |")
    [void]$lines.Add("| Qualitative verdicts | $($Evaluation.QualitativeEvidence.TotalVerdicts) |")
    [void]$lines.Add("| Uncertainty policy satisfied | $($Evaluation.UncertaintyPolicy.Satisfied) |")
    [void]$lines.Add("| Failed gates | $($Evaluation.FailedGates.Count) |")
    [void]$lines.Add("")
    [void]$lines.Add("## Failed gates")
    [void]$lines.Add("")
    if ($Evaluation.FailedGates.Count -eq 0) {
        [void]$lines.Add("None.")
    } else {
        foreach ($gate in $Evaluation.FailedGates) { [void]$lines.Add("- **$($gate.Gate)**: $($gate.Reason)") }
    }
    [void]$lines.Add("")
    [void]$lines.Add("## Standing constraints")
    [void]$lines.Add("")
    [void]$lines.Add("- $($Evaluation.ControlAuthority)")
    [void]$lines.Add("- $($Evaluation.AbsoluteCertification)")
    [void]$lines.Add("- The vendor resolution figure is not a characterized one-sigma uncertainty and is used nowhere in this evaluation.")
    [void]$lines.Add("- UI-only evidence can corroborate qualitatively. It cannot certify an absolute numerical result.")
    [void]$lines.Add("")
    return @($lines)
}
