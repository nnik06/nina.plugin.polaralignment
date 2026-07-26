Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

. (Join-Path (Split-Path -Parent $PSScriptRoot) "ipolar_campaign_evaluator.ps1")

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "Assertion failed: $Message" }
}

function Assert-Level {
    param($Evaluation, [string]$Expected, [string]$Message)
    if ($Evaluation.QualificationLevel -ne $Expected) {
        $gateText = ($Evaluation.FailedGates | ForEach-Object { "$($_.Gate): $($_.Reason)" }) -join " | "
        throw "Assertion failed: $Message (was '$($Evaluation.QualificationLevel)'; gates: $gateText)"
    }
}

function Assert-Gate {
    param($Evaluation, [string]$Gate, [string]$Message)
    $hit = @($Evaluation.FailedGates | Where-Object { $_.Gate -eq $Gate })
    if ($hit.Count -eq 0) {
        $gateText = ($Evaluation.FailedGates | ForEach-Object { $_.Gate }) -join ", "
        throw "Assertion failed: $Message (gates raised: $gateText)"
    }
}

function Assert-NoGate {
    param($Evaluation, [string]$Gate, [string]$Message)
    $hit = @($Evaluation.FailedGates | Where-Object { $_.Gate -eq $Gate })
    if ($hit.Count -ne 0) { throw "Assertion failed: $Message (reason: $($hit[0].Reason))" }
}

# Normalizes through the JSON reader so tests see exactly what Read-IPolarCampaign sees.
function ConvertTo-TestObject {
    param($Value)
    return (($Value | ConvertTo-Json -Depth 12 -Compress) | ConvertFrom-Json)
}

$BaseUtc = [datetime]::Parse("2026-07-26T18:00:00Z", [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal -bor [Globalization.DateTimeStyles]::AssumeUniversal)
$EvaluationUtc = $BaseUtc.AddMinutes(95)

function New-TestHeader {
    param([hashtable]$Overrides = @{})

    $header = [ordered]@{
        SchemaVersion = "1.0.0"
        CampaignId = "test-campaign"
        CreatedUtc = Format-IPolarUtc -Value $BaseUtc
        RepositoryCommit = "ccc3720aa9bffb5351c29f2263f20cf0b92a237a"
        DeployedPluginSha256 = "0f2b1c9d3e4a5b6c7d8e9f0a1b2c3d4e5f60718293a4b5c6d7e8f9a0b1c2d3e4"
        MountIdentifier = "HAE29C-EC"
        IPolarIdentifier = "ipolar-external-01"
        IPolarHardwareVariant = "External iPolar with thumb-screw bracket"
        IPolarAdapter = "HAE29 external bracket"
        IPolarSoftwareVersion = "iPolar 3.6"
        MountingStateId = "mount-state-a"
        PoleConvention = "TruePole"
        RequestedQualificationLevel = "Unspecified"
    }
    foreach ($key in $Overrides.Keys) { $header[$key] = $Overrides[$key] }
    $header.GenesisHash = Get-IPolarGenesisHash -Header $header
    return (ConvertTo-TestObject $header)
}

function New-TestResidual {
    param([double]$ValueArcsec, [string]$SourceKind = "DocumentedManualReadout")
    return [ordered]@{
        ValueArcsec = $ValueArcsec
        Source = "iPolar UI numeric readout, transcribed by the operator"
        SourceKind = $SourceKind
        IsManual = ($SourceKind -eq "DocumentedManualReadout")
    }
}

<#
.SYNOPSIS
    Produces the standard well-formed campaign specification tests mutate.
#>
function New-TestSpecs {
    param(
        [int]$SolveAttempts = 10,
        [int]$SolveSuccesses = 9,
        [int]$CalibrationCycles = 5,
        [int]$ReseatCycles = 5,
        [switch]$WithoutResiduals,
        [double]$CalibrationSpreadArcsec = 1.0,
        [double]$ReseatSpreadArcsec = 2.0,
        [switch]$WithoutDarkFrame,
        [switch]$WithoutEnvironment,
        [datetime]$BlockStart = [datetime]::MinValue,
        [datetime]$BlockEnd = [datetime]::MinValue
    )

    if ($BlockStart -eq [datetime]::MinValue) { $BlockStart = $BaseUtc.AddMinutes(45) }
    if ($BlockEnd -eq [datetime]::MinValue) { $BlockEnd = $BaseUtc.AddMinutes(90) }

    $specs = New-Object System.Collections.Generic.List[object]
    [void]$specs.Add(@{ Type = "CampaignInitialized"; Utc = $BaseUtc; Payload = [ordered]@{ CampaignId = "test-campaign"; PoleConvention = "TruePole"; MountingStateId = "mount-state-a"; Notes = "" } })

    if (-not $WithoutEnvironment) {
        [void]$specs.Add(@{ Type = "Environment"; Utc = $BaseUtc.AddMinutes(1); Payload = [ordered]@{
            Site = [ordered]@{ LatitudeDegrees = 25.2048; LongitudeDegrees = 55.2708; ElevationMeters = 12.0; Source = "GNSS fix logged at the balcony pier" }
            Atmosphere = [ordered]@{ AbsoluteStationPressureHectopascals = 1004.2; TemperatureCelsius = 33.1; RelativeHumidityPercent = 48.0; Source = "Balcony BME280"; ObservedUtc = Format-IPolarUtc -Value $BaseUtc; AgeMinutes = 1.0 }
            Notes = ""
        } })
    }

    if (-not $WithoutDarkFrame) {
        [void]$specs.Add(@{ Type = "DarkFrame"; Utc = $BaseUtc.AddMinutes(2); Payload = [ordered]@{
            SourcePath = "C:\\evidence\\dark.fit"; StoredPath = "C:\\campaign\\artifacts\\0003-dark.fit"
            Sha256 = "a1" * 32; SizeBytes = 2048
            CreatedUtc = Format-IPolarUtc -Value $BaseUtc.AddMinutes(1)
            CapturedUtc = Format-IPolarUtc -Value $BaseUtc.AddMinutes(1)
            Notes = ""
        } })
    }

    for ($index = 1; $index -le $SolveAttempts; $index++) {
        [void]$specs.Add(@{ Type = "SolveAttempt"; Utc = $BaseUtc.AddMinutes(2 + $index); Payload = [ordered]@{
            AttemptIndex = $index
            Succeeded = ($index -le $SolveSuccesses)
            AvailableStarCount = (10 + $index)
            Reason = if ($index -le $SolveSuccesses) { "Solved" } else { "Too few stars in the pole region" }
            ManualObservation = ""
            Notes = ""
        } })
    }

    for ($index = 1; $index -le $CalibrationCycles; $index++) {
        $payload = [ordered]@{
            CycleIndex = $index
            MountingStateId = "mount-state-a"
            RaPositionsDegrees = @(0.0, 90.0, 180.0)
            CameraRemovedOrReseated = $false
            Residual = $null
            ManualObservation = ""
            Notes = ""
        }
        if (-not $WithoutResiduals) { $payload.Residual = New-TestResidual -ValueArcsec (20.0 + $index * $CalibrationSpreadArcsec) }
        [void]$specs.Add(@{ Type = "FixedMountCalibration"; Utc = $BaseUtc.AddMinutes(19 + $index); Payload = $payload })
    }

    for ($index = 1; $index -le $ReseatCycles; $index++) {
        $payload = [ordered]@{
            ReseatIndex = $index
            MountingStateIdBefore = "mount-state-a"
            MountingStateIdAfter = "mount-state-a$index"
            CameraRemovedOrReseated = $true
            RecalibrationPerformed = $true
            RaPositionsDegrees = @(0.0, 90.0, 180.0)
            Residual = $null
            ManualObservation = ""
            Notes = ""
        }
        if (-not $WithoutResiduals) { $payload.Residual = New-TestResidual -ValueArcsec (40.0 + $index * $ReseatSpreadArcsec) }
        [void]$specs.Add(@{ Type = "Reseat"; Utc = $BaseUtc.AddMinutes(29 + $index); Payload = $payload })
    }

    [void]$specs.Add(@{ Type = "Artifact"; Utc = $BaseUtc.AddMinutes(41); Payload = [ordered]@{
        ArtifactKind = "Screenshot"; SourcePath = "C:\\evidence\\shot1.png"; StoredPath = "C:\\campaign\\artifacts\\0024-shot1.png"
        Sha256 = "b2" * 32; SizeBytes = 40960
        CreatedUtc = Format-IPolarUtc -Value $BaseUtc.AddMinutes(40)
        QualitativeVerdict = "CrossInsideCircle"; ManualObservation = ""; Notes = ""
    } })

    [void]$specs.Add(@{ Type = "TppaArtifactLink"; Utc = $BaseUtc.AddMinutes(42); Payload = [ordered]@{
        TppaRunId = "tppa-2026-07-26-a"; ArtifactPath = "C:\\tppa\\run-a\\result.json"; Sha256 = "c3" * 32
        SizeBytes = 512; CreatedUtc = Format-IPolarUtc -Value $BaseUtc.AddMinutes(41); Description = "A/B/A leg A"; Notes = ""
    } })

    [void]$specs.Add(@{ Type = "NoMotionBlock"; Utc = $BaseUtc.AddMinutes(91); Payload = [ordered]@{
        BlockId = "block-1"
        StartUtc = Format-IPolarUtc -Value $BlockStart
        EndUtc = Format-IPolarUtc -Value $BlockEnd
        Legs = @(
            [ordered]@{ Order = 1; Label = "iPolar"; Method = "IPolar" }
            [ordered]@{ Order = 2; Label = "TPPA-A"; Method = "Tppa" }
            [ordered]@{ Order = 3; Label = "TPPA-B"; Method = "Tppa" }
            [ordered]@{ Order = 4; Label = "TPPA-A"; Method = "Tppa" }
            [ordered]@{ Order = 5; Label = "iPolar"; Method = "IPolar" }
        )
        Notes = ""
    } })

    return $specs.ToArray()
}

function New-TestCampaign {
    param($Header, $Specs)

    $previous = Get-IPolarGenesisHash -Header $Header
    $sequence = 1
    $events = New-Object System.Collections.Generic.List[object]
    foreach ($spec in $Specs) {
        $record = [ordered]@{
            Sequence = $sequence
            RecordedUtc = Format-IPolarUtc -Value $spec.Utc
            EventType = $spec.Type
            PreviousEventHash = $previous
            Payload = (ConvertTo-TestObject $spec.Payload)
        }
        $record.EventHash = Get-IPolarEventHash -Event $record
        $previous = $record.EventHash
        $sequence++
        [void]$events.Add((ConvertTo-TestObject $record))
    }
    return [pscustomobject]@{
        CampaignPath = "C:\\campaign"
        Header = $Header
        Events = $events.ToArray()
    }
}

function Invoke-TestEvaluation {
    param($Campaign, $Policy = $null)
    return (Invoke-IPolarCampaignEvaluation -Campaign $Campaign -Policy $Policy -EvaluatedUtc $EvaluationUtc)
}

function New-TestPolicy {
    param([double]$MaximumFixedMountScatterArcsec = 5.0, [double]$MaximumReseatScatterArcsec = 8.0, [double]$ReadoutQuantizationArcsec = 1.0)
    return (ConvertTo-TestObject ([ordered]@{
        PolicyId = "ipolar-uncertainty-policy-1"
        Source = "Council decision, provisional, derived from this campaign's own repeatability data"
        MaximumFixedMountScatterArcsec = $MaximumFixedMountScatterArcsec
        MaximumReseatScatterArcsec = $MaximumReseatScatterArcsec
        ReadoutQuantizationArcsec = $ReadoutQuantizationArcsec
    }))
}

$allLevels = New-Object System.Collections.Generic.List[string]

function Register-Level { param($Evaluation) [void]$allLevels.Add($Evaluation.QualificationLevel) }

# --- Canonical serialization and hashing -----------------------------------------

$sample = [ordered]@{ zulu = "2026-07-26T10:00:00Z"; alpha = 1.5; bravo = @(1, 2); charlie = [ordered]@{ inner = $true; nested = $null } }
$canonicalDirect = ConvertTo-IPolarCanonicalJson $sample
$canonicalRoundTripped = ConvertTo-IPolarCanonicalJson (ConvertTo-TestObject $sample)
Assert-True ($canonicalDirect -eq $canonicalRoundTripped) "canonical JSON must be identical before and after a JSON round trip"
Assert-True ($canonicalDirect.StartsWith('{"alpha":1.5,"bravo":[1,2],"charlie":')) "canonical JSON must sort keys ordinally"
Assert-True ($canonicalDirect.Contains('"2026-07-26T10:00:00.0000000Z"')) "canonical JSON must normalize UTC timestamps to the round-trip form"

$reordered = [ordered]@{ charlie = [ordered]@{ nested = $null; inner = $true }; bravo = @(1, 2); alpha = 1.5; zulu = "2026-07-26T10:00:00Z" }
Assert-True ((ConvertTo-IPolarCanonicalJson $reordered) -eq $canonicalDirect) "canonical JSON must not depend on key insertion order"
Assert-True ((Get-IPolarTextSha256 -Text $canonicalDirect) -eq (Get-IPolarTextSha256 -Text (ConvertTo-IPolarCanonicalJson $reordered))) "hashes must match for logically identical payloads"

$throwsOnNonFinite = $false
try { [void](ConvertTo-IPolarCanonicalJson ([ordered]@{ v = [double]::NaN })) } catch { $throwsOnNonFinite = $true }
Assert-True $throwsOnNonFinite "canonical serialization must refuse a non-finite number"

# Known-answer fixture. Windows PowerShell 5.1 and PowerShell 7 disagree about
# whether an ISO timestamp reads back as a string or a DateTime and whether 1.5
# reads back as Decimal or Double. Pinning the canonical text and its hash keeps a
# campaign written on one host verifiable on the other.
$fixture = [ordered]@{ CampaignId = "fixture"; Value = 1.5; When = "2026-07-26T10:00:00Z"; Flag = $true; Items = @(1, 2); Absent = $null }
$expectedCanonical = '{"Absent":null,"CampaignId":"fixture","Flag":true,"Items":[1,2],"Value":1.5,"When":"2026-07-26T10:00:00.0000000Z"}'
$expectedFixtureSha = "25ddcd065128e217755116d425458342d5c5df8b8492f75213eda1e3bd1af606"
Assert-True ((ConvertTo-IPolarCanonicalJson $fixture) -eq $expectedCanonical) "the canonical wire format must not change"
Assert-True ((ConvertTo-IPolarCanonicalJson (ConvertTo-TestObject $fixture)) -eq $expectedCanonical) "the canonical wire format must survive a JSON round trip"
Assert-True ((Get-IPolarTextSha256 -Text $expectedCanonical) -eq $expectedFixtureSha) "the fixture hash must not change"

# A non-terminating binary64 value catches the .NET Framework versus modern
# .NET difference in the older "R" formatter. G17 must remain byte-identical.
$nonTerminatingFixture = [ordered]@{ Value = ([double]0.1 + [double]0.2) }
$expectedNonTerminatingCanonical = '{"Value":0.30000000000000004}'
$expectedNonTerminatingSha = "b1c4a65c52617d72b4e7e483f741ebe20ce38bf2008355345a7d011c03fde2b1"
Assert-True ((ConvertTo-IPolarCanonicalJson $nonTerminatingFixture) -eq $expectedNonTerminatingCanonical) "non-terminating binary64 values must use the cross-host G17 form"
Assert-True ((Get-IPolarTextSha256 -Text $expectedNonTerminatingCanonical) -eq $expectedNonTerminatingSha) "the non-terminating fixture hash must not change"

# --- Valid qualitative campaign ---------------------------------------------------

$qualitative = New-TestCampaign -Header (New-TestHeader) -Specs (New-TestSpecs -WithoutResiduals)
$result = Invoke-TestEvaluation -Campaign $qualitative
Register-Level $result
Assert-Level $result "QualitativeWitnessOnly" "a complete campaign with only cross/circle evidence is a qualitative witness"
Assert-True ($result.FailedGates.Count -eq 0) "the qualitative campaign must raise no gate"
Assert-True ($result.IntegrityValid) "the qualitative campaign hash chain must validate"
Assert-True ($result.NumericEvidence.AuthoritativeResidualCount -eq 0) "no numeric residual may be counted"
Assert-True ($result.QualitativeEvidence.VerdictCounts.CrossInsideCircle -eq 1) "the cross/circle verdict must be counted"
Assert-True ($result.SolveReliability.Passed) "9 of 10 solves must pass the reliability gate"
Assert-True ($null -eq $result.FixedMountCalibration.SampleStdDevArcsec) "scatter must be absent, not zero, when no numbers exist"

# --- Valid numerical but unqualified campaign ------------------------------------

$numerical = New-TestCampaign -Header (New-TestHeader) -Specs (New-TestSpecs)
$result = Invoke-TestEvaluation -Campaign $numerical
Register-Level $result
Assert-Level $result "QuantitativeUnqualified" "numbers without an explicit uncertainty policy cannot be qualified"
Assert-True ($result.NumericEvidence.AuthoritativeResidualCount -eq 10) "all ten documented residuals must be counted"
Assert-True ($result.NumericEvidence.ManualResidualCount -eq 10) "manually transcribed residuals must be marked manual"
Assert-True ([Math]::Abs($result.FixedMountCalibration.SampleStdDevArcsec - 1.5811388300841898) -lt 1e-9) "fixed-mount sample standard deviation must be computed"
Assert-True ([Math]::Abs($result.ReseatRepeatability.SampleStdDevArcsec - 3.1622776601683795) -lt 1e-9) "reseat sample standard deviation must be computed"
Assert-True (-not $result.UncertaintyPolicy.Supplied) "the policy must be reported as absent"
Assert-True (($result.UncertaintyPolicy.Reasons -join " ").Contains("never been characterized")) "the report must say iPolar uncertainty is uncharacterized"

# --- Qualified corroborating witness ----------------------------------------------

$result = Invoke-TestEvaluation -Campaign $numerical -Policy (New-TestPolicy)
Register-Level $result
Assert-Level $result "QualifiedCorroboratingWitness" "an explicit satisfied policy promotes the campaign to a qualified witness"
Assert-True ($result.UncertaintyPolicy.Satisfied) "the policy must be reported as satisfied"
Assert-True ($result.FailedGates.Count -eq 0) "a qualified campaign must raise no gate"
Assert-True ($result.AbsoluteCertification.Contains("open-sky")) "even a qualified witness must state that open-sky certification is outstanding"
Assert-True ($result.ControlAuthority.Contains("cannot command UPAS")) "the evaluation must state that iPolar cannot command UPAS"

# --- Excessive scatter -------------------------------------------------------------

$result = Invoke-TestEvaluation -Campaign $numerical -Policy (New-TestPolicy -MaximumFixedMountScatterArcsec 0.5)
Register-Level $result
Assert-Level $result "QuantitativeUnqualified" "excessive fixed-mount scatter must block qualification"
Assert-True (($result.UncertaintyPolicy.Reasons -join " ").Contains("Fixed-mount calibration scatter")) "the fixed-mount scatter failure must be explained"

$result = Invoke-TestEvaluation -Campaign $numerical -Policy (New-TestPolicy -MaximumReseatScatterArcsec 0.5)
Register-Level $result
Assert-Level $result "QuantitativeUnqualified" "excessive reseat scatter must block qualification"
Assert-True (($result.UncertaintyPolicy.Reasons -join " ").Contains("Reseat scatter")) "the reseat scatter failure must be explained"

$widelySpread = New-TestCampaign -Header (New-TestHeader) -Specs (New-TestSpecs -CalibrationCycles 5 -ReseatCycles 5 -CalibrationSpreadArcsec 40.0)
$result = Invoke-TestEvaluation -Campaign $widelySpread -Policy (New-TestPolicy)
Register-Level $result
Assert-Level $result "QuantitativeUnqualified" "a campaign whose fixed-mount scatter exceeds the policy cannot be qualified"

# --- Policy without measured quantization ------------------------------------------

$result = Invoke-TestEvaluation -Campaign $numerical -Policy (New-TestPolicy -ReadoutQuantizationArcsec 0.0)
Register-Level $result
Assert-Level $result "QuantitativeUnqualified" "a policy without measured readout quantization cannot qualify a campaign"
Assert-True (($result.UncertaintyPolicy.Reasons -join " ").Contains("vendor resolution claim")) "the quantization failure must warn against assuming the vendor figure"

# --- Solve reliability -------------------------------------------------------------

$eightOfTen = New-TestCampaign -Header (New-TestHeader) -Specs (New-TestSpecs -SolveAttempts 10 -SolveSuccesses 8)
$result = Invoke-TestEvaluation -Campaign $eightOfTen -Policy (New-TestPolicy)
Register-Level $result
Assert-Level $result "Rejected" "8 of 10 successful solves must be rejected"
Assert-Gate $result "SolveReliability" "the solve reliability gate must fire"
Assert-True ($result.SolveReliability.Successes -eq 8) "the evaluation must report the actual success count"

$tooFewAttempts = New-TestCampaign -Header (New-TestHeader) -Specs (New-TestSpecs -SolveAttempts 6 -SolveSuccesses 6)
$result = Invoke-TestEvaluation -Campaign $tooFewAttempts
Register-Level $result
Assert-Level $result "Rejected" "fewer than ten solve attempts must be rejected"
Assert-Gate $result "SolveReliability" "the attempt-count gate must fire"

# --- Dark frame ---------------------------------------------------------------------

$noDark = New-TestCampaign -Header (New-TestHeader) -Specs (New-TestSpecs -WithoutDarkFrame)
$result = Invoke-TestEvaluation -Campaign $noDark
Register-Level $result
Assert-Level $result "Rejected" "a campaign without a dark frame must be rejected"
Assert-Gate $result "DarkFrame" "the dark frame gate must fire"
Assert-True ($result.MissingPrerequisites -contains "DarkFrame") "the missing dark frame must be listed as a prerequisite"

$staleSpecs = New-TestSpecs
foreach ($spec in $staleSpecs) {
    if ($spec.Type -eq "DarkFrame") {
        $spec.Payload.CapturedUtc = Format-IPolarUtc -Value $BaseUtc.AddDays(-3)
        $spec.Payload.CreatedUtc = Format-IPolarUtc -Value $BaseUtc.AddDays(-3)
    }
}
$staleDark = New-TestCampaign -Header (New-TestHeader) -Specs $staleSpecs
$result = Invoke-TestEvaluation -Campaign $staleDark
Register-Level $result
Assert-Level $result "Rejected" "a stale dark frame must be rejected"
Assert-Gate $result "DarkFrame" "the stale dark frame gate must fire"

# --- Duplicate / reused artifact -----------------------------------------------------

$duplicateSpecs = New-TestSpecs
$duplicateList = New-Object System.Collections.Generic.List[object]
foreach ($spec in $duplicateSpecs) { [void]$duplicateList.Add($spec) }
[void]$duplicateList.Add(@{ Type = "Artifact"; Utc = $BaseUtc.AddMinutes(92); Payload = [ordered]@{
    ArtifactKind = "Screenshot"; SourcePath = "C:\\evidence\\shot2.png"; StoredPath = "C:\\campaign\\artifacts\\0027-shot2.png"
    Sha256 = "b2" * 32
    SizeBytes = 40960
    CreatedUtc = Format-IPolarUtc -Value $BaseUtc.AddMinutes(50)
    QualitativeVerdict = "CrossInsideCircle"; ManualObservation = ""; Notes = ""
} })
$duplicate = New-TestCampaign -Header (New-TestHeader) -Specs $duplicateList.ToArray()
$result = Invoke-TestEvaluation -Campaign $duplicate
Register-Level $result
Assert-Level $result "Rejected" "a reused screenshot must be rejected"
Assert-Gate $result "ArtifactProvenance" "the duplicate hash gate must fire"
Assert-True ($result.Artifacts.DuplicateSha256.Count -eq 1) "the duplicated hash must be reported"

# --- Artifact that is not newer than its predecessor -----------------------------------

$staleOrderSpecs = New-TestSpecs
$staleOrderList = New-Object System.Collections.Generic.List[object]
foreach ($spec in $staleOrderSpecs) { [void]$staleOrderList.Add($spec) }
[void]$staleOrderList.Add(@{ Type = "Artifact"; Utc = $BaseUtc.AddMinutes(92); Payload = [ordered]@{
    ArtifactKind = "Screenshot"; SourcePath = "C:\\evidence\\shot3.png"; StoredPath = "C:\\campaign\\artifacts\\0027-shot3.png"
    Sha256 = "d4" * 32
    SizeBytes = 40960
    CreatedUtc = Format-IPolarUtc -Value $BaseUtc.AddMinutes(10)
    QualitativeVerdict = "CrossOutsideCircle"; ManualObservation = ""; Notes = ""
} })
$staleOrder = New-TestCampaign -Header (New-TestHeader) -Specs $staleOrderList.ToArray()
$result = Invoke-TestEvaluation -Campaign $staleOrder
Register-Level $result
Assert-Level $result "Rejected" "an artifact older than its predecessor must be rejected"
Assert-Gate $result "ArtifactFreshness" "the artifact freshness gate must fire"

# --- Empty artifact --------------------------------------------------------------------

$emptySpecs = New-TestSpecs
foreach ($spec in $emptySpecs) { if ($spec.Type -eq "Artifact") { $spec.Payload.SizeBytes = 0 } }
$result = Invoke-TestEvaluation -Campaign (New-TestCampaign -Header (New-TestHeader) -Specs $emptySpecs)
Register-Level $result
Assert-Level $result "Rejected" "an empty artifact must be rejected"
Assert-Gate $result "ArtifactProvenance" "the empty artifact gate must fire"

# --- Non-monotonic event sequence --------------------------------------------------------

$outOfOrder = New-TestCampaign -Header (New-TestHeader) -Specs (New-TestSpecs)
$events = $outOfOrder.Events
$events[6].Sequence = 99
$result = Invoke-TestEvaluation -Campaign $outOfOrder
Register-Level $result
Assert-Level $result "Rejected" "a broken sequence must be rejected"
Assert-Gate $result "EventSequence" "the event sequence gate must fire"
Assert-True (-not $result.IntegrityValid) "a broken sequence must invalidate integrity"

$backwardsTime = New-TestCampaign -Header (New-TestHeader) -Specs (New-TestSpecs)
$backwardsTime.Events[6].RecordedUtc = Format-IPolarUtc -Value $BaseUtc.AddDays(-1)
$result = Invoke-TestEvaluation -Campaign $backwardsTime
Register-Level $result
Assert-Level $result "Rejected" "an event timestamped before its predecessor must be rejected"
Assert-Gate $result "EventTimestamp" "the event timestamp gate must fire"

# --- Hash tampering ----------------------------------------------------------------------

$tampered = New-TestCampaign -Header (New-TestHeader) -Specs (New-TestSpecs)
$tampered.Events[5].Payload.AvailableStarCount = 999
$result = Invoke-TestEvaluation -Campaign $tampered
Register-Level $result
Assert-Level $result "Rejected" "an edited payload must be rejected"
Assert-Gate $result "HashChain" "the hash chain gate must fire on an edited payload"
Assert-True (-not $result.IntegrityValid) "an edited payload must invalidate integrity"

$rehashed = New-TestCampaign -Header (New-TestHeader) -Specs (New-TestSpecs)
$rehashed.Events[5].Payload.AvailableStarCount = 999
$rehashed.Events[5].EventHash = Get-IPolarEventHash -Event $rehashed.Events[5]
$result = Invoke-TestEvaluation -Campaign $rehashed
Register-Level $result
Assert-Level $result "Rejected" "re-hashing one event must still break the chain of its successor"
Assert-Gate $result "HashChain" "the successor chain link must fail after a re-hash"

$headerTampered = New-TestCampaign -Header (New-TestHeader) -Specs (New-TestSpecs)
$headerTampered.Header.CampaignId = "renamed-campaign"
$result = Invoke-TestEvaluation -Campaign $headerTampered
Register-Level $result
Assert-Level $result "Rejected" "an edited header must be rejected"
Assert-Gate $result "GenesisHash" "the genesis hash gate must fire on an edited header"

# --- Reseat inside an A/B/A block ----------------------------------------------------------

$reseatInside = New-TestCampaign -Header (New-TestHeader) -Specs (New-TestSpecs -BlockStart $BaseUtc.AddMinutes(25) -BlockEnd $BaseUtc.AddMinutes(90))
$result = Invoke-TestEvaluation -Campaign $reseatInside
Register-Level $result
Assert-Level $result "Rejected" "a reseat inside a no-motion block must be rejected"
Assert-Gate $result "NoMotionBlock" "the no-motion block gate must fire"
Assert-True ($result.NoMotionBlocks[0].ReseatSequencesInsideBlock.Count -eq 5) "every offending reseat must be listed"

# --- Missing site or atmosphere ---------------------------------------------------------------

$noEnvironment = New-TestCampaign -Header (New-TestHeader) -Specs (New-TestSpecs -WithoutEnvironment)
$result = Invoke-TestEvaluation -Campaign $noEnvironment
Register-Level $result
Assert-Level $result "Rejected" "a campaign without site or atmosphere must be rejected"
Assert-Gate $result "Site" "the site gate must fire"
Assert-Gate $result "Atmosphere" "the atmosphere gate must fire"

$partialSpecs = New-TestSpecs
foreach ($spec in $partialSpecs) { if ($spec.Type -eq "Environment") { $spec.Payload.Site.ElevationMeters = $null } }
$result = Invoke-TestEvaluation -Campaign (New-TestCampaign -Header (New-TestHeader) -Specs $partialSpecs)
Register-Level $result
Assert-Level $result "Rejected" "an absent elevation must be rejected rather than coerced to zero"
Assert-Gate $result "Site" "the incomplete site gate must fire"
Assert-True ($null -eq $result.Site.ElevationMeters) "an unknown elevation must stay null, never zero"

$noAtmosphereSpecs = New-TestSpecs
foreach ($spec in $noAtmosphereSpecs) { if ($spec.Type -eq "Environment") { $spec.Payload.Atmosphere.AbsoluteStationPressureHectopascals = $null } }
$result = Invoke-TestEvaluation -Campaign (New-TestCampaign -Header (New-TestHeader) -Specs $noAtmosphereSpecs)
Register-Level $result
Assert-Level $result "Rejected" "an absent station pressure must be rejected"
Assert-Gate $result "Atmosphere" "the incomplete atmosphere gate must fire"

$staleAtmosphereSpecs = New-TestSpecs
foreach ($spec in $staleAtmosphereSpecs) { if ($spec.Type -eq "Environment") { $spec.Payload.Atmosphere.AgeMinutes = 900.0 } }
$result = Invoke-TestEvaluation -Campaign (New-TestCampaign -Header (New-TestHeader) -Specs $staleAtmosphereSpecs)
Register-Level $result
Assert-Level $result "Rejected" "a stale atmosphere reading must be rejected"
Assert-Gate $result "Atmosphere" "the stale atmosphere gate must fire"

# --- Unknown pole convention ---------------------------------------------------------------------

$unknownPole = New-TestCampaign -Header (New-TestHeader -Overrides @{ PoleConvention = "Unknown" }) -Specs (New-TestSpecs)
$result = Invoke-TestEvaluation -Campaign $unknownPole -Policy (New-TestPolicy)
Register-Level $result
Assert-Level $result "Rejected" "an unknown pole convention must be rejected"
Assert-Gate $result "PoleConvention" "the pole convention gate must fire"
Assert-True ($result.MissingPrerequisites -contains "PoleConvention") "the pole convention must be listed as missing"

$apparentPole = New-TestCampaign -Header (New-TestHeader -Overrides @{ PoleConvention = "ApparentPole" }) -Specs (New-TestSpecs)
$result = Invoke-TestEvaluation -Campaign $apparentPole -Policy (New-TestPolicy)
Register-Level $result
Assert-Level $result "QualifiedCorroboratingWitness" "an apparent-pole convention is a valid declared convention"

# --- Missing calibration and reseat cycles ------------------------------------------------------------

$fewCalibrations = New-TestCampaign -Header (New-TestHeader) -Specs (New-TestSpecs -CalibrationCycles 3)
$result = Invoke-TestEvaluation -Campaign $fewCalibrations -Policy (New-TestPolicy)
Register-Level $result
Assert-Level $result "Rejected" "fewer than five fixed-mount calibration cycles must be rejected"
Assert-Gate $result "FixedMountCalibration" "the calibration cycle gate must fire"

$fewReseats = New-TestCampaign -Header (New-TestHeader) -Specs (New-TestSpecs -ReseatCycles 2)
$result = Invoke-TestEvaluation -Campaign $fewReseats -Policy (New-TestPolicy)
Register-Level $result
Assert-Level $result "Rejected" "fewer than five reseat cycles must be rejected"
Assert-Gate $result "ReseatRepeatability" "the reseat cycle gate must fire"

$noRecalibrationSpecs = New-TestSpecs
foreach ($spec in $noRecalibrationSpecs) { if ($spec.Type -eq "Reseat") { $spec.Payload.RecalibrationPerformed = $false } }
$result = Invoke-TestEvaluation -Campaign (New-TestCampaign -Header (New-TestHeader) -Specs $noRecalibrationSpecs)
Register-Level $result
Assert-Level $result "Rejected" "a reseat without recalibration must be rejected"
Assert-Gate $result "ReseatRepeatability" "the recalibration gate must fire"

$movedCameraSpecs = New-TestSpecs
foreach ($spec in $movedCameraSpecs) { if ($spec.Type -eq "FixedMountCalibration") { $spec.Payload.CameraRemovedOrReseated = $true } }
$result = Invoke-TestEvaluation -Campaign (New-TestCampaign -Header (New-TestHeader) -Specs $movedCameraSpecs)
Register-Level $result
Assert-Level $result "Rejected" "a fixed-mount cycle that moved the camera must be rejected"
Assert-Gate $result "FixedMountCalibration" "the fixed-mount camera gate must fire"

# --- Non-finite and implausible values ---------------------------------------------------------------------

$nonFiniteSpecs = New-TestSpecs
$firstCalibration = $true
foreach ($spec in $nonFiniteSpecs) {
    if ($spec.Type -eq "FixedMountCalibration" -and $firstCalibration) {
        $spec.Payload.Residual.ValueArcsec = "NaN"
        $firstCalibration = $false
    }
}
$result = Invoke-TestEvaluation -Campaign (New-TestCampaign -Header (New-TestHeader) -Specs $nonFiniteSpecs)
Register-Level $result
Assert-Level $result "Rejected" "a non-finite value anywhere in a payload must be rejected"
Assert-Gate $result "FiniteValues" "the finite-values gate must fire"

$implausibleSpecs = New-TestSpecs
$firstReseat = $true
foreach ($spec in $implausibleSpecs) {
    if ($spec.Type -eq "Reseat" -and $firstReseat) {
        $spec.Payload.Residual.ValueArcsec = 400000.0
        $firstReseat = $false
    }
}
$result = Invoke-TestEvaluation -Campaign (New-TestCampaign -Header (New-TestHeader) -Specs $implausibleSpecs) -Policy (New-TestPolicy)
Register-Level $result
Assert-True ($result.NumericEvidence.NonAuthoritativeResidualCount -eq 1) "an implausible residual must be excluded from the numeric evidence"
Assert-True ($result.ReseatRepeatability.NumericResidualCount -eq 4) "an implausible residual must not enter the scatter statistics"
Assert-True (($result.NumericEvidence.RejectedResiduals -join " ").Contains("plausible range")) "the implausible residual must be explained"

# --- OCR and screenshot pixels are never authoritative -----------------------------------------------------------

$ocrSpecs = New-TestSpecs
foreach ($spec in $ocrSpecs) {
    if ($spec.Type -eq "FixedMountCalibration" -or $spec.Type -eq "Reseat") { $spec.Payload.Residual.SourceKind = "Ocr" }
}
$result = Invoke-TestEvaluation -Campaign (New-TestCampaign -Header (New-TestHeader) -Specs $ocrSpecs) -Policy (New-TestPolicy)
Register-Level $result
Assert-Level $result "QualitativeWitnessOnly" "OCR-derived numbers must never count as numeric evidence"
Assert-True ($result.NumericEvidence.AuthoritativeResidualCount -eq 0) "no OCR residual may be authoritative"
Assert-True ($result.NumericEvidence.NonAuthoritativeResidualCount -eq 10) "every OCR residual must be reported as rejected"

$pixelSpecs = New-TestSpecs
foreach ($spec in $pixelSpecs) {
    if ($spec.Type -eq "FixedMountCalibration" -or $spec.Type -eq "Reseat") { $spec.Payload.Residual.SourceKind = "ScreenshotPixelMeasurement" }
}
$result = Invoke-TestEvaluation -Campaign (New-TestCampaign -Header (New-TestHeader) -Specs $pixelSpecs) -Policy (New-TestPolicy)
Register-Level $result
Assert-Level $result "QualitativeWitnessOnly" "screen pixels are not calibrated angles"

$unsourcedSpecs = New-TestSpecs
foreach ($spec in $unsourcedSpecs) {
    if ($spec.Type -eq "FixedMountCalibration" -or $spec.Type -eq "Reseat") { $spec.Payload.Residual.Source = "" }
}
$result = Invoke-TestEvaluation -Campaign (New-TestCampaign -Header (New-TestHeader) -Specs $unsourcedSpecs) -Policy (New-TestPolicy)
Register-Level $result
Assert-Level $result "QualitativeWitnessOnly" "an undocumented number is not evidence"

# --- Attempt to promote iPolar to ground truth -------------------------------------------------------------------

foreach ($forbidden in (Get-IPolarForbiddenQualificationLevels)) {
    $promotion = New-TestCampaign -Header (New-TestHeader -Overrides @{ RequestedQualificationLevel = $forbidden }) -Specs (New-TestSpecs)
    $result = Invoke-TestEvaluation -Campaign $promotion -Policy (New-TestPolicy)
    Register-Level $result
    Assert-Level $result "Rejected" "a campaign requesting '$forbidden' must be rejected"
    Assert-Gate $result "ForbiddenQualificationRequest" "the forbidden qualification gate must fire for '$forbidden'"
}

$unknownRequest = New-TestCampaign -Header (New-TestHeader -Overrides @{ RequestedQualificationLevel = "AbsolutelyPerfect" }) -Specs (New-TestSpecs)
$result = Invoke-TestEvaluation -Campaign $unknownRequest -Policy (New-TestPolicy)
Register-Level $result
Assert-Level $result "Rejected" "an unrecognized requested level must be rejected"

# --- Schema version handling -----------------------------------------------------------------------------------------

$futureMajor = New-TestCampaign -Header (New-TestHeader -Overrides @{ SchemaVersion = "2.0.0" }) -Specs (New-TestSpecs)
$result = Invoke-TestEvaluation -Campaign $futureMajor -Policy (New-TestPolicy)
Register-Level $result
Assert-Level $result "Rejected" "an unsupported schema major version must be rejected"
Assert-Gate $result "SchemaVersion" "the schema version gate must fire"

$compatibleMinor = New-TestCampaign -Header (New-TestHeader -Overrides @{ SchemaVersion = "1.4.2" }) -Specs (New-TestSpecs)
$result = Invoke-TestEvaluation -Campaign $compatibleMinor -Policy (New-TestPolicy)
Register-Level $result
Assert-Level $result "QualifiedCorroboratingWitness" "a compatible minor schema version must be accepted"
Assert-NoGate $result "SchemaVersion" "a compatible minor version must not raise the schema gate"

$missingSchema = New-TestCampaign -Header (New-TestHeader -Overrides @{ SchemaVersion = "" }) -Specs (New-TestSpecs)
$result = Invoke-TestEvaluation -Campaign $missingSchema
Register-Level $result
Assert-Level $result "Rejected" "a campaign without a schema version must be rejected"

# --- No evidence at all -------------------------------------------------------------------------------------------------

$noVerdictSpecs = New-TestSpecs -WithoutResiduals
$noVerdictList = New-Object System.Collections.Generic.List[object]
foreach ($spec in $noVerdictSpecs) { if ($spec.Type -ne "Artifact") { [void]$noVerdictList.Add($spec) } }
$result = Invoke-TestEvaluation -Campaign (New-TestCampaign -Header (New-TestHeader) -Specs $noVerdictList.ToArray())
Register-Level $result
Assert-Level $result "Rejected" "a campaign with neither numbers nor a cross/circle verdict must be rejected"
Assert-Gate $result "Evidence" "the evidence gate must fire"

# --- Reporting ------------------------------------------------------------------------------------------------------------

$reportEvaluation = Invoke-TestEvaluation -Campaign $numerical -Policy (New-TestPolicy)
$reportText = (New-IPolarCampaignReportMarkdown -Evaluation $reportEvaluation) -join [Environment]::NewLine
Assert-True ($reportText.Contains("# iPolar Witness Campaign Report")) "the report must have a title"
Assert-True ($reportText.Contains("QualifiedCorroboratingWitness")) "the report must state the qualification level"
Assert-True ($reportText.Contains("cannot command UPAS")) "the report must state that iPolar cannot command UPAS"
Assert-True ($reportText.Contains("open-sky")) "the report must state that open-sky certification is outstanding"
Assert-True ($reportText.Contains("not a characterized statistical uncertainty")) "the report must reject the vendor figure as an uncertainty"
Assert-True ($reportText.Contains("tppa-2026-07-26-a")) "the report must list linked TPPA artifacts"
Assert-True ($reportText.Contains("iPolar -> TPPA-A -> TPPA-B -> TPPA-A -> iPolar")) "the report must show the block leg order"
Assert-True (-not $reportText.Contains("GroundTruth")) "the report must never contain a ground-truth verdict"

$packetText = (New-IPolarCouncilPacketMarkdown -Evaluation $reportEvaluation) -join [Environment]::NewLine
Assert-True ($packetText.Contains("Council Packet")) "the council packet must be titled"
Assert-True ($packetText.Contains("contains no raw iPolar imagery")) "the council packet must state that no raw imagery is included"
Assert-True ($packetText.Contains("ccc3720aa9bffb5351c29f2263f20cf0b92a237a")) "the council packet must carry the repository commit"
Assert-True ($packetText.Contains("cannot command UPAS")) "the council packet must repeat the control-authority constraint"

$rejectedReport = (New-IPolarCampaignReportMarkdown -Evaluation (Invoke-TestEvaluation -Campaign $eightOfTen)) -join [Environment]::NewLine
Assert-True ($rejectedReport.Contains("| SolveReliability |")) "a rejected report must list every failed gate with its reason"

# --- Finalization must be terminal ---------------------------------------------------------------------------------------------

function New-FinalizedSpecs {
    param([switch]$WithTrailingEvent, [switch]$Duplicate)

    $specs = New-Object System.Collections.Generic.List[object]
    foreach ($spec in (New-TestSpecs)) { [void]$specs.Add($spec) }
    $finalizedPayload = [ordered]@{
        QualificationLevelAtFinalization = "QuantitativeUnqualified"
        ChainHeadHashBeforeFinalization = ""
        FailedGateCount = 0
        Artifacts = @()
        PolicyId = $null
        Notes = ""
    }
    [void]$specs.Add(@{ Type = "CampaignFinalized"; Utc = $BaseUtc.AddMinutes(92); Payload = $finalizedPayload })
    if ($Duplicate) {
        [void]$specs.Add(@{ Type = "CampaignFinalized"; Utc = $BaseUtc.AddMinutes(93); Payload = $finalizedPayload })
    }
    if ($WithTrailingEvent) {
        [void]$specs.Add(@{ Type = "SolveAttempt"; Utc = $BaseUtc.AddMinutes(94); Payload = [ordered]@{
            AttemptIndex = 99; Succeeded = $true; AvailableStarCount = 20; Reason = "appended after finalization"
            ManualObservation = ""; Notes = ""
        } })
    }
    return $specs.ToArray()
}

$terminalFinalized = New-TestCampaign -Header (New-TestHeader) -Specs (New-FinalizedSpecs)
$result = Invoke-TestEvaluation -Campaign $terminalFinalized -Policy (New-TestPolicy)
Register-Level $result
Assert-Level $result "QualifiedCorroboratingWitness" "a campaign finalized at the chain tail stays valid"
Assert-True ($result.Finalization.Finalized) "the evaluation must report the campaign as finalized"
Assert-True ($result.Finalization.IsTerminal) "a tail finalization must be reported as terminal"
Assert-NoGate $result "Finalization" "a tail finalization must raise no gate"

$nonTailFinalized = New-TestCampaign -Header (New-TestHeader) -Specs (New-FinalizedSpecs -WithTrailingEvent)
$result = Invoke-TestEvaluation -Campaign $nonTailFinalized -Policy (New-TestPolicy)
Register-Level $result
Assert-Level $result "Rejected" "an event appended after CampaignFinalized must reject the campaign"
Assert-Gate $result "Finalization" "the finalization gate must fire when an event follows finalization"
Assert-True (-not $result.IntegrityValid) "a non-terminal finalization must invalidate integrity"
Assert-True (-not $result.Finalization.IsTerminal) "a non-tail finalization must not be reported as terminal"

$duplicateFinalized = New-TestCampaign -Header (New-TestHeader) -Specs (New-FinalizedSpecs -Duplicate)
$result = Invoke-TestEvaluation -Campaign $duplicateFinalized -Policy (New-TestPolicy)
Register-Level $result
Assert-Level $result "Rejected" "two CampaignFinalized events must reject the campaign"
Assert-Gate $result "Finalization" "the finalization gate must fire on a duplicate finalization"
Assert-True (-not $result.IntegrityValid) "a duplicate finalization must invalidate integrity"

# --- Byte-level evidence facts ---------------------------------------------------------------------------------------------------

$unverified = Invoke-TestEvaluation -Campaign $numerical -Policy (New-TestPolicy)
Assert-True ($unverified.ArtifactIntegrity.Source -eq "NotSupplied") "an evaluation without supplied facts must say so"
Assert-True ($unverified.ArtifactIntegrity.Note.Contains("were not compared")) "an unverified evaluation must disclose that bytes were not compared"

function New-IntegrityFacts {
    param([object[]]$Entries)

    $preservedFailures = 0
    $externalMismatches = 0
    $externalMissing = 0
    $verified = 0
    foreach ($entry in $Entries) {
        if ($entry.Status -eq "Verified") { $verified++ }
        elseif ($entry.Status -eq "ExternalMissing") { $externalMissing++ }
        elseif ($entry.EvidenceClass -eq "External") { $externalMismatches++ }
        else { $preservedFailures++ }
    }
    return (ConvertTo-TestObject ([ordered]@{
        Source = "FilesystemVerified"
        CheckedCount = $Entries.Count
        VerifiedCount = $verified
        PreservedFailureCount = $preservedFailures
        ExternalMismatchCount = $externalMismatches
        ExternalMissingCount = $externalMissing
        Entries = $Entries
    }))
}

$cleanFacts = New-IntegrityFacts -Entries @(
    [ordered]@{ Sequence = 3; EventType = "DarkFrame"; EvidenceClass = "Preserved"; Status = "Verified"; Reason = "ok" }
    [ordered]@{ Sequence = 24; EventType = "Artifact"; EvidenceClass = "Preserved"; Status = "Verified"; Reason = "ok" }
)
$result = Invoke-IPolarCampaignEvaluation -Campaign $numerical -Policy (New-TestPolicy) -EvaluatedUtc $EvaluationUtc -ArtifactIntegrity $cleanFacts
Register-Level $result
Assert-Level $result "QualifiedCorroboratingWitness" "verified evidence must not disturb a qualified campaign"
Assert-True ($result.ArtifactIntegrity.Source -eq "FilesystemVerified") "the evaluation must record that bytes were verified"
Assert-True ($result.IntegrityValid) "verified evidence must keep integrity valid"

foreach ($failingStatus in @("Modified", "Missing", "SizeMismatch", "Unreadable", "NoRecordedHash", "NoRecordedPath")) {
    $facts = New-IntegrityFacts -Entries @(
        [ordered]@{ Sequence = 3; EventType = "DarkFrame"; EvidenceClass = "Preserved"; Status = "Verified"; Reason = "ok" }
        [ordered]@{ Sequence = 24; EventType = "Artifact"; EvidenceClass = "Preserved"; Status = $failingStatus; Reason = "evidence failed as $failingStatus" }
    )
    $result = Invoke-IPolarCampaignEvaluation -Campaign $numerical -Policy (New-TestPolicy) -EvaluatedUtc $EvaluationUtc -ArtifactIntegrity $facts
    Register-Level $result
    Assert-Level $result "Rejected" "preserved evidence with status '$failingStatus' must reject the campaign"
    Assert-Gate $result "ArtifactIntegrity" "the artifact integrity gate must fire for '$failingStatus'"
    Assert-True (-not $result.IntegrityValid) "IntegrityValid must be false when preserved evidence fails as '$failingStatus'"
}

# A DarkFrame failure must be treated exactly like an Artifact failure.
$darkFacts = New-IntegrityFacts -Entries @(
    [ordered]@{ Sequence = 3; EventType = "DarkFrame"; EvidenceClass = "Preserved"; Status = "Modified"; Reason = "dark frame bytes changed" }
    [ordered]@{ Sequence = 24; EventType = "Artifact"; EvidenceClass = "Preserved"; Status = "Verified"; Reason = "ok" }
)
$result = Invoke-IPolarCampaignEvaluation -Campaign $numerical -Policy (New-TestPolicy) -EvaluatedUtc $EvaluationUtc -ArtifactIntegrity $darkFacts
Register-Level $result
Assert-Level $result "Rejected" "a modified dark frame must reject the campaign just like a modified artifact"
Assert-True (-not $result.IntegrityValid) "a modified dark frame must invalidate integrity"

# External evidence: absence is a limitation, alteration fails closed.
$externalMissingFacts = New-IntegrityFacts -Entries @(
    [ordered]@{ Sequence = 25; EventType = "TppaArtifactLink"; EvidenceClass = "External"; Status = "ExternalMissing"; Reason = "linked file is gone" }
)
$result = Invoke-IPolarCampaignEvaluation -Campaign $numerical -Policy (New-TestPolicy) -EvaluatedUtc $EvaluationUtc -ArtifactIntegrity $externalMissingFacts
Register-Level $result
Assert-Level $result "QualifiedCorroboratingWitness" "an absent external artifact must not reject the campaign"
Assert-True ($result.IntegrityValid) "an absent external artifact must not invalidate integrity"
Assert-True ($result.ArtifactIntegrity.ExternalLimitations.Count -eq 1) "the absent external artifact must be recorded as a limitation"
Assert-NoGate $result "ArtifactIntegrity" "an absent external artifact must raise no integrity gate"

$externalAlteredFacts = New-IntegrityFacts -Entries @(
    [ordered]@{ Sequence = 25; EventType = "TppaArtifactLink"; EvidenceClass = "External"; Status = "Modified"; Reason = "linked file changed" }
)
$result = Invoke-IPolarCampaignEvaluation -Campaign $numerical -Policy (New-TestPolicy) -EvaluatedUtc $EvaluationUtc -ArtifactIntegrity $externalAlteredFacts
Register-Level $result
Assert-Level $result "Rejected" "an altered external artifact must fail closed"
Assert-Gate $result "ArtifactIntegrity" "the integrity gate must fire for an altered external artifact"
Assert-True (-not $result.IntegrityValid) "an altered external artifact must invalidate integrity"

$integrityReport = (New-IPolarCampaignReportMarkdown -Evaluation (Invoke-IPolarCampaignEvaluation -Campaign $numerical -Policy (New-TestPolicy) -EvaluatedUtc $EvaluationUtc -ArtifactIntegrity $cleanFacts)) -join [Environment]::NewLine
Assert-True ($integrityReport.Contains("## Evidence integrity")) "the report must contain an evidence integrity section"
Assert-True ($integrityReport.Contains("FilesystemVerified")) "the report must state how evidence was verified"

# --- Filesystem evidence verifier ------------------------------------------------------------------------------------------------

$verifierRoot = Join-Path ([IO.Path]::GetTempPath()) ("ipolar-verifier-" + [guid]::NewGuid().ToString("N"))
try {
    [void][IO.Directory]::CreateDirectory($verifierRoot)
    $goodPath = Join-Path $verifierRoot "good.bin"
    $modifiedPath = Join-Path $verifierRoot "modified.bin"
    $resizedPath = Join-Path $verifierRoot "resized.bin"
    $externalGonePath = Join-Path $verifierRoot "external-gone.bin"
    [IO.File]::WriteAllText($goodPath, "good evidence bytes")
    [IO.File]::WriteAllText($modifiedPath, "aaaaaaaaaaaaaaaaaaa")
    [IO.File]::WriteAllText($resizedPath, "resized evidence bytes")

    $goodSha = (Get-FileHash -LiteralPath $goodPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $goodSize = [long](Get-Item -LiteralPath $goodPath).Length
    $modifiedSize = [long](Get-Item -LiteralPath $modifiedPath).Length
    $resizedSha = (Get-FileHash -LiteralPath $resizedPath -Algorithm SHA256).Hash.ToLowerInvariant()

    # Same size, different bytes, so only the hash can catch it.
    [IO.File]::WriteAllText($modifiedPath, "bbbbbbbbbbbbbbbbbbb")
    [IO.File]::AppendAllText($resizedPath, " grown")

    $verifierCampaign = [pscustomobject]@{
        CampaignPath = $verifierRoot
        Header = New-TestHeader
        Events = @(
            (ConvertTo-TestObject ([ordered]@{ Sequence = 1; EventType = "DarkFrame"; Payload = [ordered]@{ StoredPath = $goodPath; Sha256 = $goodSha; SizeBytes = $goodSize } }))
            (ConvertTo-TestObject ([ordered]@{ Sequence = 2; EventType = "Artifact"; Payload = [ordered]@{ StoredPath = $modifiedPath; Sha256 = ("de" * 32); SizeBytes = $modifiedSize } }))
            (ConvertTo-TestObject ([ordered]@{ Sequence = 3; EventType = "Artifact"; Payload = [ordered]@{ StoredPath = $resizedPath; Sha256 = $resizedSha; SizeBytes = 22 } }))
            (ConvertTo-TestObject ([ordered]@{ Sequence = 4; EventType = "Artifact"; Payload = [ordered]@{ StoredPath = (Join-Path $verifierRoot "absent.bin"); Sha256 = ("ab" * 32); SizeBytes = 10 } }))
            (ConvertTo-TestObject ([ordered]@{ Sequence = 5; EventType = "TppaArtifactLink"; Payload = [ordered]@{ ArtifactPath = $externalGonePath; Sha256 = ("cd" * 32); SizeBytes = 10 } }))
            (ConvertTo-TestObject ([ordered]@{ Sequence = 6; EventType = "SolveAttempt"; Payload = [ordered]@{ AttemptIndex = 1; Succeeded = $true } }))
        )
    }

    $facts = Test-IPolarPreservedEvidence -Campaign $verifierCampaign
    Assert-True ($facts.CheckedCount -eq 5) "the verifier must check every evidence-bearing event and ignore the rest"
    Assert-True ($facts.VerifiedCount -eq 1) "only the untouched file must verify"
    Assert-True ($facts.PreservedFailureCount -eq 3) "the modified, resized, and absent preserved files must all fail"
    Assert-True ($facts.ExternalMissingCount -eq 1) "the absent external file must be classified as external"

    $bySequence = @{}
    foreach ($entry in $facts.Entries) { $bySequence[[int]$entry.Sequence] = $entry }
    Assert-True ($bySequence[1].Status -eq "Verified") "the untouched dark frame must be Verified"
    Assert-True ($bySequence[2].Status -eq "Modified") "a same-size byte change must be reported as Modified"
    Assert-True ($bySequence[3].Status -eq "SizeMismatch") "a size change must be reported as SizeMismatch"
    Assert-True ($bySequence[4].Status -eq "Missing") "an absent preserved file must be reported as Missing"
    Assert-True ($bySequence[5].Status -eq "ExternalMissing") "an absent linked file must be reported as ExternalMissing"
    Assert-True ($bySequence[1].EvidenceClass -eq "Preserved") "campaign-owned evidence must be classified Preserved"
    Assert-True ($bySequence[5].EvidenceClass -eq "External") "linked TPPA evidence must be classified External"
    Assert-True ($bySequence[2].ActualSha256 -ne $bySequence[2].ExpectedSha256) "the verifier must report the actual hash it computed"

    # The verifier's own facts must drive the evaluator to the same conclusion.
    $verifierDriven = Invoke-IPolarCampaignEvaluation -Campaign $numerical -Policy (New-TestPolicy) -EvaluatedUtc $EvaluationUtc -ArtifactIntegrity $facts
    Register-Level $verifierDriven
    Assert-Level $verifierDriven "Rejected" "facts produced by the real verifier must reject a campaign with broken evidence"
    Assert-True (-not $verifierDriven.IntegrityValid) "facts produced by the real verifier must invalidate integrity"
} finally {
    if (Test-Path -LiteralPath $verifierRoot) { Remove-Item -LiteralPath $verifierRoot -Recurse -Force }
}

# --- Forbidden verdicts are unreachable --------------------------------------------------------------------------------------

foreach ($level in $allLevels) {
    Assert-True ((Get-IPolarQualificationLevels) -contains $level) "the evaluator emitted an unknown level '$level'"
    Assert-True ((Get-IPolarForbiddenQualificationLevels) -notcontains $level) "the evaluator must never emit '$level'"
}
Assert-True ($allLevels.Count -ge 35) "the suite must exercise a broad set of campaigns (was $($allLevels.Count))"

Write-Host "All ipolar_campaign_evaluator tests passed ($($allLevels.Count) campaign evaluations)."
