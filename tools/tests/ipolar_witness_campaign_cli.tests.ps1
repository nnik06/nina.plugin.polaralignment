Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$ToolPath = Join-Path (Split-Path -Parent $PSScriptRoot) "ipolar_witness_campaign.ps1"
$TestRoot = Join-Path ([IO.Path]::GetTempPath()) ("ipolar-campaign-cli-" + [guid]::NewGuid().ToString("N"))

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "Assertion failed: $Message" }
}

function Assert-Throws {
    param([scriptblock]$Block, [string]$ExpectedFragment, [string]$Message)
    $captured = ""
    try { & $Block | Out-Null } catch { $captured = $_.Exception.Message }
    if ([string]::IsNullOrEmpty($captured)) { throw "Assertion failed: $Message (nothing was thrown)" }
    if (-not [string]::IsNullOrEmpty($ExpectedFragment) -and $captured -notlike "*$ExpectedFragment*") {
        throw "Assertion failed: $Message (message was '$captured')"
    }
}

$BaseUtc = [datetime]::Parse("2026-07-26T18:00:00Z", [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal -bor [Globalization.DateTimeStyles]::AssumeUniversal)
$EvidenceRoot = Join-Path $TestRoot "evidence"
$script:evidenceCounter = 0

function New-EvidenceFile {
    param([string]$Name = "", [string]$Content = "")

    $script:evidenceCounter++
    if ([string]::IsNullOrEmpty($Name)) { $Name = "evidence-$($script:evidenceCounter).bin" }
    if ([string]::IsNullOrEmpty($Content)) { $Content = "iPolar evidence payload $($script:evidenceCounter)" }
    $path = Join-Path $EvidenceRoot $Name
    [IO.File]::WriteAllText($path, $Content, [Text.UTF8Encoding]::new($false))
    return $path
}

<#
.SYNOPSIS
    Builds a complete, well-formed campaign so individual tests can perturb one thing.
#>
function New-CompleteCampaign {
    param(
        [string]$Name,
        [int]$SolveSuccesses = 9,
        [int]$CalibrationCycles = 5,
        [int]$ReseatCycles = 5,
        [switch]$WithoutResiduals,
        [datetime]$BlockStart = [datetime]::MinValue,
        [datetime]$BlockEnd = [datetime]::MinValue
    )

    if ($BlockStart -eq [datetime]::MinValue) { $BlockStart = $BaseUtc.AddMinutes(45) }
    if ($BlockEnd -eq [datetime]::MinValue) { $BlockEnd = $BaseUtc.AddMinutes(90) }
    $campaign = Join-Path $TestRoot $Name

    & $ToolPath -Command Init -CampaignPath $campaign -CampaignId $Name -RecordedUtc $BaseUtc `
        -RepositoryCommit "ccc3720aa9bffb5351c29f2263f20cf0b92a237a" `
        -DeployedPluginSha256 ("0f" * 32) -MountIdentifier "HAE29C-EC" -IPolarIdentifier "ipolar-external-01" `
        -IPolarHardwareVariant "External iPolar" -IPolarAdapter "thumb-screw bracket" -IPolarSoftwareVersion "iPolar 3.6" `
        -MountingStateId "mount-state-a" -PoleConvention TruePole | Out-Null

    & $ToolPath -Command RecordEnvironment -CampaignPath $campaign -RecordedUtc $BaseUtc.AddMinutes(1) `
        -LatitudeDegrees 25.2048 -LongitudeDegrees 55.2708 -ElevationMeters 12 -SiteSource "GNSS fix at the balcony pier" `
        -AbsoluteStationPressureHectopascals 1004.2 -TemperatureCelsius 33.1 -RelativeHumidityPercent 48 `
        -AtmosphereSource "Balcony BME280" -AtmosphereObservedUtc $BaseUtc | Out-Null

    & $ToolPath -Command RecordDarkFrame -CampaignPath $campaign -RecordedUtc $BaseUtc.AddMinutes(2) `
        -Path (New-EvidenceFile) -CapturedUtc $BaseUtc.AddMinutes(1) | Out-Null

    for ($index = 1; $index -le 10; $index++) {
        $outcome = if ($index -le $SolveSuccesses) { "Success" } else { "Failure" }
        & $ToolPath -Command RecordSolveAttempt -CampaignPath $campaign -RecordedUtc $BaseUtc.AddMinutes(2 + $index) `
            -SolveOutcome $outcome -AvailableStarCount (10 + $index) -Reason "pole region" | Out-Null
    }

    for ($index = 1; $index -le $CalibrationCycles; $index++) {
        $arguments = @{
            Command = "RecordCalibration"; CampaignPath = $campaign; RecordedUtc = $BaseUtc.AddMinutes(19 + $index)
            RaPositionsDegrees = @(0.0, 90.0, 180.0); MountingStateId = "mount-state-a"
        }
        if (-not $WithoutResiduals) {
            $arguments.ResidualArcsec = 20.0 + $index
            $arguments.ResidualSource = "iPolar UI numeric readout"
            $arguments.ResidualSourceKind = "DocumentedManualReadout"
        }
        & $ToolPath @arguments | Out-Null
    }

    for ($index = 1; $index -le $ReseatCycles; $index++) {
        $arguments = @{
            Command = "RecordReseat"; CampaignPath = $campaign; RecordedUtc = $BaseUtc.AddMinutes(29 + $index)
            MountingStateIdBefore = "mount-state-a"; MountingStateIdAfter = "mount-state-a$index"
            RecalibrationPerformed = $true
        }
        if (-not $WithoutResiduals) {
            $arguments.ResidualArcsec = 40.0 + 2 * $index
            $arguments.ResidualSource = "iPolar UI numeric readout"
            $arguments.ResidualSourceKind = "DocumentedManualReadout"
        }
        & $ToolPath @arguments | Out-Null
    }

    & $ToolPath -Command RecordArtifact -CampaignPath $campaign -RecordedUtc $BaseUtc.AddMinutes(41) `
        -Path (New-EvidenceFile) -ArtifactKind Screenshot -QualitativeVerdict CrossInsideCircle -CreatedUtc $BaseUtc.AddMinutes(40) | Out-Null

    & $ToolPath -Command LinkTppaArtifact -CampaignPath $campaign -RecordedUtc $BaseUtc.AddMinutes(42) `
        -Path (New-EvidenceFile) -TppaRunId "tppa-2026-07-26-a" -Description "A/B/A leg A result" | Out-Null

    & $ToolPath -Command RecordBlock -CampaignPath $campaign -RecordedUtc $BaseUtc.AddMinutes(91) `
        -BlockId "block-1" -BlockStartUtc $BlockStart -BlockEndUtc $BlockEnd `
        -BlockLegs iPolar, TPPA-A, TPPA-B, TPPA-A, iPolar | Out-Null

    return $campaign
}

function New-PolicyFile {
    param([string]$Name = "policy.json", [double]$MaximumFixedMountScatterArcsec = 5.0, [double]$MaximumReseatScatterArcsec = 8.0)

    $path = Join-Path $TestRoot $Name
    $policy = [ordered]@{
        PolicyId = "ipolar-uncertainty-policy-1"
        Source = "Council decision, provisional"
        MaximumFixedMountScatterArcsec = $MaximumFixedMountScatterArcsec
        MaximumReseatScatterArcsec = $MaximumReseatScatterArcsec
        ReadoutQuantizationArcsec = 1.0
    }
    [IO.File]::WriteAllText($path, ($policy | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    return $path
}

try {
    [void][IO.Directory]::CreateDirectory($TestRoot)
    [void][IO.Directory]::CreateDirectory($EvidenceRoot)

    # --- Help -------------------------------------------------------------------
    $help = (& $ToolPath -Command Help) -join [Environment]::NewLine
    Assert-True ($help.Contains("RecordSolveAttempt")) "help must list every command"
    Assert-True ($help.Contains("Finalize")) "help must list the Finalize command"
    Assert-True ($help.Contains("cannot command UPAS")) "help must state that iPolar has no actuator authority"
    Assert-True ($help.Contains("never derives a")) "help must state that pixels and OCR are not measurements"
    Assert-True ($help.Contains("GroundTruth and CertifiedAbsoluteAccuracy are not emittable")) "help must state the forbidden verdicts"
    Assert-True ($help.Contains("-BlockLegs iPolar,TPPA-A,TPPA-B,TPPA-A,iPolar")) "help must show the required block leg order"

    $comem = Get-Help $ToolPath
    Assert-True (-not [string]::IsNullOrWhiteSpace($comem.Synopsis)) "the script must expose comment-based help"

    # --- Init -------------------------------------------------------------------
    $campaign = New-CompleteCampaign -Name "complete"
    $header = [IO.File]::ReadAllText((Join-Path $campaign "campaign.json")) | ConvertFrom-Json
    Assert-True ($header.SchemaVersion -eq "1.0.0") "the header must record the schema version"
    Assert-True (-not [string]::IsNullOrWhiteSpace($header.GenesisHash)) "the header must record a genesis hash"
    Assert-True ($header.PoleConvention -eq "TruePole") "the header must record the pole convention"
    Assert-True ($header.WitnessRole.Contains("cannot command UPAS")) "the header must record the witness role"

    Assert-Throws { & $ToolPath -Command Init -CampaignPath $campaign -CampaignId "complete" } "already exists" "re-initializing a campaign must be refused"
    Assert-Throws { & $ToolPath -Command Init -CampaignPath (Join-Path $TestRoot "gt") -CampaignId "gt" -RequestedQualificationLevel "GroundTruth" } "not permitted" "requesting ground truth at Init must be refused"
    Assert-Throws { & $ToolPath -Command Init -CampaignPath (Join-Path $TestRoot "ca") -CampaignId "ca" -RequestedQualificationLevel "CertifiedAbsoluteAccuracy" } "not permitted" "requesting certified absolute accuracy at Init must be refused"
    $unsafeCampaignPath = Join-Path $TestRoot "unsafe-id"
    Assert-Throws { & $ToolPath -Command Init -CampaignPath $unsafeCampaignPath -CampaignId "..\escape" } "path separator" "campaign IDs containing path traversal must be refused"
    Assert-True (-not (Test-Path -LiteralPath $unsafeCampaignPath)) "an unsafe campaign ID must be rejected before creating the campaign directory"
    Assert-Throws { & $ToolPath -Command Init -CampaignPath (Join-Path $TestRoot "unsafe-colon") -CampaignId "drive:escape" } "safe file name" "campaign IDs unsafe on Windows must be refused on every host"
    Assert-Throws { & $ToolPath -Command Init -CampaignPath (Join-Path $TestRoot "unsafe-long") -CampaignId ("a" * 101) } "100 characters" "overlong campaign IDs must be refused"
    Assert-Throws { & $ToolPath -Command Init -CampaignPath (Join-Path $TestRoot "unsafe-reserved") -CampaignId "COM1.data" } "reserved Windows device name" "Windows device names must be refused on every host"
    Assert-Throws { & $ToolPath -Command Init -CampaignPath (Join-Path $TestRoot "unsafe-dot") -CampaignId "campaign." } "end with a dot" "trailing-dot campaign IDs must be refused"
    Assert-Throws { & $ToolPath -Command RecordSolveAttempt -CampaignPath (Join-Path $TestRoot "missing") -SolveOutcome Success } "not initialized" "recording into an uninitialized campaign must be refused"

    # --- Append-only log and preserved evidence ---------------------------------
    $eventLines = @([IO.File]::ReadAllLines((Join-Path $campaign "events.jsonl")))
    # 1 init + 1 environment + 1 dark frame + 10 solves + 5 calibrations + 5 reseats + 1 artifact + 1 TPPA link + 1 block
    Assert-True ($eventLines.Count -eq 26) "the complete campaign must contain 26 events (was $($eventLines.Count))"
    $artifacts = @(Get-ChildItem -LiteralPath (Join-Path $campaign "artifacts") -File)
    Assert-True ($artifacts.Count -eq 2) "the dark frame and the screenshot must both be preserved"
    Assert-True (@($artifacts | Where-Object { $_.Name -like "0003-*" }).Count -eq 1) "preserved evidence must be prefixed with its event sequence"

    $sequences = @($eventLines | ForEach-Object { ($_ | ConvertFrom-Json).Sequence })
    for ($index = 0; $index -lt $sequences.Count; $index++) {
        Assert-True ($sequences[$index] -eq ($index + 1)) "event sequence numbers must increase by exactly one"
    }

    # --- Evaluate ---------------------------------------------------------------
    $evaluation = & $ToolPath -Command Evaluate -CampaignPath $campaign -RecordedUtc $BaseUtc.AddMinutes(95)
    Assert-True ($evaluation.QualificationLevel -eq "QuantitativeUnqualified") "numbers without a policy must be QuantitativeUnqualified (was $($evaluation.QualificationLevel))"
    Assert-True ($evaluation.FailedGates.Count -eq 0) "a well-formed campaign must raise no gate"
    Assert-True ($evaluation.IntegrityValid) "a well-formed campaign must have a valid hash chain"
    Assert-True ($evaluation.SolveReliability.Successes -eq 9) "nine successful solves must be counted"
    Assert-True ($evaluation.NumericEvidence.AuthoritativeResidualCount -eq 10) "ten documented residuals must be counted"
    Assert-True ($evaluation.TppaArtifactLinks.Count -eq 1) "the linked TPPA artifact must be reported"

    $policy = New-PolicyFile
    $qualified = & $ToolPath -Command Evaluate -CampaignPath $campaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(95)
    Assert-True ($qualified.QualificationLevel -eq "QualifiedCorroboratingWitness") "a satisfied explicit policy must qualify the witness (was $($qualified.QualificationLevel))"

    $tightPolicy = New-PolicyFile -Name "tight.json" -MaximumFixedMountScatterArcsec 0.25
    $unqualified = & $ToolPath -Command Evaluate -CampaignPath $campaign -PolicyPath $tightPolicy -RecordedUtc $BaseUtc.AddMinutes(95)
    Assert-True ($unqualified.QualificationLevel -eq "QuantitativeUnqualified") "excessive scatter must block qualification"

    $evaluationOut = Join-Path $TestRoot "evaluation.json"
    & $ToolPath -Command Evaluate -CampaignPath $campaign -PolicyPath $policy -OutputPath $evaluationOut -RecordedUtc $BaseUtc.AddMinutes(95) | Out-Null
    Assert-True (Test-Path -LiteralPath $evaluationOut) "-OutputPath must write the evaluation JSON"
    Assert-True (([IO.File]::ReadAllText($evaluationOut) | ConvertFrom-Json).QualificationLevel -eq "QualifiedCorroboratingWitness") "the written evaluation must carry the level"

    # --- Qualitative-only campaign ----------------------------------------------
    $qualitativeCampaign = New-CompleteCampaign -Name "qualitative" -WithoutResiduals
    $qualitativeResult = & $ToolPath -Command Evaluate -CampaignPath $qualitativeCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(95)
    Assert-True ($qualitativeResult.QualificationLevel -eq "QualitativeWitnessOnly") "a campaign without numbers must stay a qualitative witness"

    # --- Solve reliability ------------------------------------------------------
    $eightOfTen = New-CompleteCampaign -Name "eight-of-ten" -SolveSuccesses 8
    $eightResult = & $ToolPath -Command Evaluate -CampaignPath $eightOfTen -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(95)
    Assert-True ($eightResult.QualificationLevel -eq "Rejected") "8 of 10 solves must be rejected"
    Assert-True (@($eightResult.FailedGates | Where-Object { $_.Gate -eq "SolveReliability" }).Count -ge 1) "the solve reliability gate must fire"

    # --- Missing cycles ---------------------------------------------------------
    $fewCycles = New-CompleteCampaign -Name "few-cycles" -CalibrationCycles 2 -ReseatCycles 1
    $fewResult = & $ToolPath -Command Evaluate -CampaignPath $fewCycles -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(95)
    Assert-True ($fewResult.QualificationLevel -eq "Rejected") "too few calibration and reseat cycles must be rejected"

    # --- Reseat inside a block --------------------------------------------------
    $insideBlock = New-CompleteCampaign -Name "reseat-in-block" -BlockStart $BaseUtc.AddMinutes(25) -BlockEnd $BaseUtc.AddMinutes(90)
    $insideResult = & $ToolPath -Command Evaluate -CampaignPath $insideBlock -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(95)
    Assert-True ($insideResult.QualificationLevel -eq "Rejected") "a reseat inside the block window must be rejected"
    Assert-True (@($insideResult.FailedGates | Where-Object { $_.Gate -eq "NoMotionBlock" }).Count -ge 1) "the no-motion block gate must fire"

    # --- Evidence integrity refusals --------------------------------------------
    $guard = Join-Path $TestRoot "guard"
    & $ToolPath -Command Init -CampaignPath $guard -CampaignId "guard" -RecordedUtc $BaseUtc -MountingStateId "state-a" -PoleConvention TruePole -DeployedPluginSha256 ("0f" * 32) | Out-Null

    $shared = New-EvidenceFile -Name "shared.png" -Content "shared evidence"
    & $ToolPath -Command RecordArtifact -CampaignPath $guard -RecordedUtc $BaseUtc.AddMinutes(1) -Path $shared -CreatedUtc $BaseUtc | Out-Null
    Assert-Throws { & $ToolPath -Command RecordArtifact -CampaignPath $guard -RecordedUtc $BaseUtc.AddMinutes(2) -Path $shared -CreatedUtc $BaseUtc.AddMinutes(1) } "same SHA256" "re-recording identical content must be refused"

    $empty = Join-Path $EvidenceRoot "empty.png"
    [IO.File]::WriteAllText($empty, "")
    Assert-Throws { & $ToolPath -Command RecordArtifact -CampaignPath $guard -RecordedUtc $BaseUtc.AddMinutes(3) -Path $empty -CreatedUtc $BaseUtc.AddMinutes(2) } "empty" "an empty evidence file must be refused"

    Assert-Throws { & $ToolPath -Command RecordArtifact -CampaignPath $guard -RecordedUtc $BaseUtc.AddMinutes(3) -Path (Join-Path $EvidenceRoot "absent.png") -CreatedUtc $BaseUtc } "not found" "a missing evidence file must be refused"

    $stale = New-EvidenceFile -Name "stale.png"
    Assert-Throws { & $ToolPath -Command RecordArtifact -CampaignPath $guard -RecordedUtc $BaseUtc.AddMinutes(3) -Path $stale -CreatedUtc $BaseUtc.AddDays(-2) } "minute limit" "a stale capture must be refused"

    $older = New-EvidenceFile -Name "older.png"
    Assert-Throws { & $ToolPath -Command RecordArtifact -CampaignPath $guard -RecordedUtc $BaseUtc.AddMinutes(4) -Path $older -CreatedUtc $BaseUtc.AddMinutes(-30) } "provably newer" "an artifact older than its predecessor must be refused"

    $future = New-EvidenceFile -Name "future.png"
    Assert-Throws { & $ToolPath -Command RecordArtifact -CampaignPath $guard -RecordedUtc $BaseUtc.AddMinutes(4) -Path $future -CreatedUtc $BaseUtc.AddMinutes(30) } "timestamped after" "an artifact from the future must be refused"

    Assert-Throws { & $ToolPath -Command RecordSolveAttempt -CampaignPath $guard -RecordedUtc $BaseUtc.AddDays(-1) -SolveOutcome Success } "must not move backwards" "an event timestamped before its predecessor must be refused"

    # --- Residual provenance refusals -------------------------------------------
    Assert-Throws { & $ToolPath -Command RecordCalibration -CampaignPath $guard -RecordedUtc $BaseUtc.AddMinutes(5) -ResidualArcsec 12.0 } "ResidualSource is required" "an undocumented residual must be refused"
    Assert-Throws { & $ToolPath -Command RecordCalibration -CampaignPath $guard -RecordedUtc $BaseUtc.AddMinutes(5) -ResidualArcsec 12.0 -ResidualSource "somewhere" } "ResidualSourceKind is required" "a residual without a source kind must be refused"
    Assert-Throws { & $ToolPath -Command RecordCalibration -CampaignPath $guard -RecordedUtc $BaseUtc.AddMinutes(5) -CameraRemovedOrReseated } "must leave the camera untouched" "a fixed-mount cycle that moved the camera must be refused"
    Assert-Throws { & $ToolPath -Command RecordSolveAttempt -CampaignPath $guard -RecordedUtc $BaseUtc.AddMinutes(5) } "Success or Failure" "a solve attempt without an outcome must be refused"

    & $ToolPath -Command RecordCalibration -CampaignPath $guard -RecordedUtc $BaseUtc.AddMinutes(5) `
        -ResidualArcsec 12.0 -ResidualSource "cross position read off the screenshot" -ResidualSourceKind Ocr | Out-Null
    $ocrEvaluation = & $ToolPath -Command Evaluate -CampaignPath $guard -RecordedUtc $BaseUtc.AddMinutes(10)
    Assert-True ($ocrEvaluation.NumericEvidence.AuthoritativeResidualCount -eq 0) "an OCR residual must never be authoritative"
    Assert-True ($ocrEvaluation.NumericEvidence.NonAuthoritativeResidualCount -eq 1) "the OCR residual must be reported as rejected"

    # --- Block leg protocol -----------------------------------------------------
    Assert-Throws { & $ToolPath -Command RecordBlock -CampaignPath $guard -RecordedUtc $BaseUtc.AddMinutes(11) -BlockId "b" -BlockStartUtc $BaseUtc -BlockEndUtc $BaseUtc.AddMinutes(10) -BlockLegs TPPA-A, TPPA-B, TPPA-A, TPPA-B, TPPA-A } "open and close with an iPolar leg" "a block that does not bracket with iPolar must be refused"
    Assert-Throws { & $ToolPath -Command RecordBlock -CampaignPath $guard -RecordedUtc $BaseUtc.AddMinutes(11) -BlockId "b" -BlockStartUtc $BaseUtc -BlockEndUtc $BaseUtc.AddMinutes(10) -BlockLegs iPolar, TPPA-A, iPolar } "at least five legs" "a block without three TPPA legs must be refused"
    Assert-Throws { & $ToolPath -Command RecordBlock -CampaignPath $guard -RecordedUtc $BaseUtc.AddMinutes(11) -BlockId "b" -BlockStartUtc $BaseUtc -BlockEndUtc $BaseUtc.AddMinutes(10) -BlockLegs iPolar, TPPA-A, TPPA-A, TPPA-A, iPolar } "requires 'TPPA-B'" "a block without the interleaved B leg must be refused"
    & $ToolPath -Command RecordBlock -CampaignPath $guard -RecordedUtc $BaseUtc.AddMinutes(11) -BlockId "b-long" -BlockStartUtc $BaseUtc -BlockEndUtc $BaseUtc.AddMinutes(10) -BlockLegs iPolar, TPPA-A, TPPA-B, TPPA-A, TPPA-B, TPPA-A, iPolar | Out-Null

    # --- Tampering --------------------------------------------------------------
    $tamper = New-CompleteCampaign -Name "tamper"
    $tamperLog = Join-Path $tamper "events.jsonl"
    $tamperLines = @([IO.File]::ReadAllLines($tamperLog))
    $tamperLines[5] = $tamperLines[5].Replace('"AvailableStarCount":13', '"AvailableStarCount":99')
    Assert-True ($tamperLines[5].Contains('"AvailableStarCount":99')) "the tamper fixture must actually change the payload"
    [IO.File]::WriteAllLines($tamperLog, $tamperLines, [Text.UTF8Encoding]::new($false))
    $tamperResult = & $ToolPath -Command Evaluate -CampaignPath $tamper -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(95)
    Assert-True ($tamperResult.QualificationLevel -eq "Rejected") "an edited event log must be rejected"
    Assert-True (-not $tamperResult.IntegrityValid) "an edited event log must invalidate integrity"
    Assert-True (@($tamperResult.FailedGates | Where-Object { $_.Gate -eq "HashChain" }).Count -ge 1) "the hash chain gate must fire"

    $headerTamper = New-CompleteCampaign -Name "header-tamper"
    $headerPath = Join-Path $headerTamper "campaign.json"
    $headerObject = [IO.File]::ReadAllText($headerPath) | ConvertFrom-Json
    $headerObject.PoleConvention = "ApparentPole"
    [IO.File]::WriteAllText($headerPath, ($headerObject | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    $headerResult = & $ToolPath -Command Evaluate -CampaignPath $headerTamper -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(95)
    Assert-True ($headerResult.QualificationLevel -eq "Rejected") "an edited header must be rejected"
    Assert-True (@($headerResult.FailedGates | Where-Object { $_.Gate -eq "GenesisHash" }).Count -ge 1) "the genesis hash gate must fire"

    # --- Finalize ---------------------------------------------------------------
    $collisionCampaign = New-CompleteCampaign -Name "finalize-collision"
    $collisionReports = Join-Path $collisionCampaign "reports"
    [void][IO.Directory]::CreateDirectory($collisionReports)
    $collisionPath = Join-Path $collisionReports "finalize-collision-report.json"
    [IO.File]::WriteAllText($collisionPath, "reserved", [Text.UTF8Encoding]::new($false))
    Assert-Throws { & $ToolPath -Command Finalize -CampaignPath $collisionCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(96) } "already exists" "a report collision must stop finalization before the immutable event"
    $collisionLines = @([IO.File]::ReadAllLines((Join-Path $collisionCampaign "events.jsonl")))
    $collisionLastEvent = $collisionLines[$collisionLines.Count - 1] | ConvertFrom-Json
    Assert-True ($collisionLastEvent.EventType -ne "CampaignFinalized") "a report collision must leave the campaign recoverable and unfinalized"
    Remove-Item -LiteralPath $collisionPath -Force
    $collisionFinal = & $ToolPath -Command Finalize -CampaignPath $collisionCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(96)
    Assert-True ($collisionFinal.QualificationLevel -eq "QualifiedCorroboratingWitness") "the campaign must finalize successfully after the collision is removed"

    $final = & $ToolPath -Command Finalize -CampaignPath $campaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(96)
    Assert-True ($final.QualificationLevel -eq "QualifiedCorroboratingWitness") "the finalized level must match the evaluation"
    foreach ($reportPath in @($final.ReportJsonPath, $final.ReportMarkdownPath, $final.CouncilPacketPath, $final.ManifestPath)) {
        Assert-True (Test-Path -LiteralPath $reportPath) "Finalize must write $reportPath"
    }

    $reportText = [IO.File]::ReadAllText($final.ReportMarkdownPath)
    Assert-True ($reportText.Contains("QualifiedCorroboratingWitness")) "the report must state the level"
    Assert-True ($reportText.Contains("cannot command UPAS")) "the report must state that iPolar cannot command UPAS"
    Assert-True ($reportText.Contains("open-sky")) "the report must state that open-sky certification is outstanding"
    Assert-True ($reportText.Contains("iPolar -> TPPA-A -> TPPA-B -> TPPA-A -> iPolar")) "the report must show the block leg order"
    Assert-True (-not $reportText.Contains("GroundTruth")) "the report must never claim ground truth"

    $packetText = [IO.File]::ReadAllText($final.CouncilPacketPath)
    Assert-True ($packetText.Contains("contains no raw iPolar imagery")) "the council packet must disclaim raw imagery"
    Assert-True ($packetText.Contains("ccc3720aa9bffb5351c29f2263f20cf0b92a237a")) "the council packet must carry the repository commit"

    $manifest = [IO.File]::ReadAllText($final.ManifestPath) | ConvertFrom-Json
    Assert-True ($manifest.Artifacts.Count -eq 3) "the manifest must list the dark frame, the screenshot, and the linked TPPA artifact"
    Assert-True (-not [string]::IsNullOrWhiteSpace($manifest.EventLogSha256)) "the manifest must hash the event log"
    Assert-True ($manifest.Reports.Count -eq 3) "the manifest must hash all three reports"
    foreach ($report in $manifest.Reports) {
        $actual = (Get-FileHash -LiteralPath $report.Path -Algorithm SHA256).Hash.ToLowerInvariant()
        Assert-True ($actual -eq $report.Sha256) "the manifest hash must match the written report at $($report.Path)"
    }

    $finalizedLines = @([IO.File]::ReadAllLines((Join-Path $campaign "events.jsonl")))
    $lastEvent = $finalizedLines[$finalizedLines.Count - 1] | ConvertFrom-Json
    Assert-True ($lastEvent.EventType -eq "CampaignFinalized") "finalization must be recorded in the event chain"

    $eventCountBeforeResume = $finalizedLines.Count
    Remove-Item -LiteralPath $final.ReportMarkdownPath -Force
    $resumed = & $ToolPath -Command Finalize -CampaignPath $campaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(97)
    Assert-True (Test-Path -LiteralPath $resumed.ReportMarkdownPath) "rerunning Finalize must reconstruct a missing report after an interrupted finalization"
    $eventCountAfterResume = @([IO.File]::ReadAllLines((Join-Path $campaign "events.jsonl"))).Count
    Assert-True ($eventCountAfterResume -eq $eventCountBeforeResume) "resuming finalized report output must not append another event"

    [IO.File]::WriteAllText($resumed.ReportJsonPath, "tampered", [Text.UTF8Encoding]::new($false))
    Assert-Throws { & $ToolPath -Command Finalize -CampaignPath $campaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(98) } "does not match" "resuming Finalize must refuse to overwrite a mismatched existing report"
    $eventCountAfterMismatch = @([IO.File]::ReadAllLines((Join-Path $campaign "events.jsonl"))).Count
    Assert-True ($eventCountAfterMismatch -eq $eventCountBeforeResume) "a mismatched finalized report must not mutate the event chain"

    # --- Preserved evidence is verified byte for byte ----------------------------
    #
    # Recording an artifact hashes the source. These tests prove the preserved copy
    # is what actually gets trusted afterwards: the bytes on disk are rehashed at
    # record time and again at every filesystem-aware evaluation.

    function Get-PreservedPath {
        param([string]$Campaign, [string]$EventType)
        $line = @([IO.File]::ReadAllLines((Join-Path $Campaign "events.jsonl")) | Where-Object { $_ } |
            Where-Object { ($_ | ConvertFrom-Json).EventType -eq $EventType } | Select-Object -First 1)
        return ($line[0] | ConvertFrom-Json).Payload.StoredPath
    }

    function Get-RecordedSha {
        param([string]$Campaign, [string]$EventType)
        $line = @([IO.File]::ReadAllLines((Join-Path $Campaign "events.jsonl")) | Where-Object { $_ } |
            Where-Object { ($_ | ConvertFrom-Json).EventType -eq $EventType } | Select-Object -First 1)
        return ($line[0] | ConvertFrom-Json).Payload.Sha256
    }

    # Destination bytes are verified immediately after the copy: the preserved file
    # exists, matches the recorded hash, and no staging file survives the import.
    $bytesCampaign = New-CompleteCampaign -Name "preserved-bytes"
    foreach ($evidenceType in @("Artifact", "DarkFrame")) {
        $preservedPath = Get-PreservedPath -Campaign $bytesCampaign -EventType $evidenceType
        $recordedSha = Get-RecordedSha -Campaign $bytesCampaign -EventType $evidenceType
        Assert-True (Test-Path -LiteralPath $preservedPath) "$evidenceType must leave a preserved copy at its recorded StoredPath"
        $onDisk = (Get-FileHash -LiteralPath $preservedPath -Algorithm SHA256).Hash.ToLowerInvariant()
        Assert-True ($onDisk -eq $recordedSha) "$evidenceType preserved bytes must match the recorded SHA256 immediately after the copy"
    }
    $staging = @(Get-ChildItem -LiteralPath (Join-Path $bytesCampaign "artifacts") -File -Force | Where-Object { $_.Name -like ".staging-*" })
    Assert-True ($staging.Count -eq 0) "a successful import must leave no staging file behind"

    $bytesBaseline = & $ToolPath -Command Evaluate -CampaignPath $bytesCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(95)
    Assert-True ($bytesBaseline.QualificationLevel -eq "QualifiedCorroboratingWitness") "the byte-verification fixture must start qualified"
    Assert-True ($bytesBaseline.ArtifactIntegrity.Source -eq "FilesystemVerified") "a CLI evaluation must verify evidence against the filesystem"
    Assert-True ($bytesBaseline.ArtifactIntegrity.PreservedFailureCount -eq 0) "an untouched campaign must report no preserved-evidence failure"
    Assert-True ($bytesBaseline.ArtifactIntegrity.VerifiedCount -ge 3) "the dark frame, the screenshot, and the linked TPPA artifact must all be verified"

    # A modified preserved artifact must destroy qualification and integrity.
    $modifiedCampaign = New-CompleteCampaign -Name "artifact-modified"
    $modifiedBefore = & $ToolPath -Command Evaluate -CampaignPath $modifiedCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(95)
    Assert-True ($modifiedBefore.QualificationLevel -eq "QualifiedCorroboratingWitness") "the tamper fixture must start qualified"
    Assert-True ($modifiedBefore.IntegrityValid) "the tamper fixture must start with valid integrity"
    $modifiedPath = Get-PreservedPath -Campaign $modifiedCampaign -EventType "Artifact"
    $originalBytes = [IO.File]::ReadAllBytes($modifiedPath)
    # Same length, different bytes: proves the hash is checked, not just the size.
    $sameLength = New-Object byte[] $originalBytes.Length
    for ($index = 0; $index -lt $originalBytes.Length; $index++) { $sameLength[$index] = [byte](($originalBytes[$index] + 1) % 256) }
    [IO.File]::WriteAllBytes($modifiedPath, $sameLength)
    $modifiedAfter = & $ToolPath -Command Evaluate -CampaignPath $modifiedCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(95)
    Assert-True ($modifiedAfter.QualificationLevel -eq "Rejected") "a modified preserved artifact must reject the campaign (was $($modifiedAfter.QualificationLevel))"
    Assert-True (-not $modifiedAfter.IntegrityValid) "a modified preserved artifact must invalidate integrity"
    Assert-True (@($modifiedAfter.FailedGates | Where-Object { $_.Gate -eq "ArtifactIntegrity" }).Count -ge 1) "the artifact integrity gate must fire on modification"
    Assert-True ($modifiedAfter.ArtifactIntegrity.PreservedFailureCount -eq 1) "exactly one preserved artifact must be reported as failing"
    Assert-Throws { & $ToolPath -Command Finalize -CampaignPath $modifiedCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(96) -ReportDirectory (Join-Path $TestRoot "modified-reports") } "no longer matches the bytes on disk" "finalization must fail closed while evidence is modified"

    # A deleted preserved dark frame must be detected the same way.
    $deletedCampaign = New-CompleteCampaign -Name "artifact-deleted"
    $deletedDark = Get-PreservedPath -Campaign $deletedCampaign -EventType "DarkFrame"
    Remove-Item -LiteralPath $deletedDark -Force
    $deletedAfter = & $ToolPath -Command Evaluate -CampaignPath $deletedCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(95)
    Assert-True ($deletedAfter.QualificationLevel -eq "Rejected") "a deleted preserved dark frame must reject the campaign"
    Assert-True (-not $deletedAfter.IntegrityValid) "a deleted preserved dark frame must invalidate integrity"
    Assert-True ((($deletedAfter.ArtifactIntegrity.Failures) -join " ").Contains("missing")) "the missing dark frame must be named in the failures"

    # A size-changed preserved artifact must be detected.
    $sizeCampaign = New-CompleteCampaign -Name "artifact-resized"
    $sizePath = Get-PreservedPath -Campaign $sizeCampaign -EventType "Artifact"
    [IO.File]::AppendAllText($sizePath, "extra bytes appended after recording")
    $sizeAfter = & $ToolPath -Command Evaluate -CampaignPath $sizeCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(95)
    Assert-True ($sizeAfter.QualificationLevel -eq "Rejected") "a size-changed preserved artifact must reject the campaign"
    Assert-True (-not $sizeAfter.IntegrityValid) "a size-changed preserved artifact must invalidate integrity"
    Assert-True ((($sizeAfter.ArtifactIntegrity.Failures) -join " ").Contains("bytes but was recorded as")) "the size mismatch must be reported explicitly"

    # A linked TPPA artifact is external evidence: absence is a recorded limitation,
    # but a file that is still present and no longer matches fails closed.
    $externalCampaign = New-CompleteCampaign -Name "external-link"
    $externalLine = @([IO.File]::ReadAllLines((Join-Path $externalCampaign "events.jsonl")) | Where-Object { $_ } |
        Where-Object { ($_ | ConvertFrom-Json).EventType -eq "TppaArtifactLink" } | Select-Object -First 1)
    $externalPath = ($externalLine[0] | ConvertFrom-Json).Payload.ArtifactPath
    $externalOriginal = [IO.File]::ReadAllText($externalPath)
    Remove-Item -LiteralPath $externalPath -Force
    $externalMissing = & $ToolPath -Command Evaluate -CampaignPath $externalCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(95)
    Assert-True ($externalMissing.QualificationLevel -eq "QualifiedCorroboratingWitness") "an absent external TPPA artifact is a limitation, not a campaign defect"
    Assert-True ($externalMissing.ArtifactIntegrity.ExternalMissingCount -eq 1) "the absent external artifact must be counted"
    Assert-True ($externalMissing.ArtifactIntegrity.ExternalLimitations.Count -eq 1) "the absent external artifact must be reported as a limitation"
    [IO.File]::WriteAllText($externalPath, $externalOriginal + " altered")
    $externalAltered = & $ToolPath -Command Evaluate -CampaignPath $externalCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(95)
    Assert-True ($externalAltered.QualificationLevel -eq "Rejected") "an altered external TPPA artifact must fail closed"
    Assert-True (-not $externalAltered.IntegrityValid) "an altered external TPPA artifact must invalidate integrity"

    # --- Finalization is terminal ------------------------------------------------
    $terminalCampaign = New-CompleteCampaign -Name "terminal"
    $terminalFinal = & $ToolPath -Command Finalize -CampaignPath $terminalCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(96)
    Assert-True ($terminalFinal.QualificationLevel -eq "QualifiedCorroboratingWitness") "the terminal fixture must finalize"
    $terminalLogPath = Join-Path $terminalCampaign "events.jsonl"
    $terminalBytesBefore = [IO.File]::ReadAllBytes($terminalLogPath)

    $postFinalizationAttempts = @(
        @{ Command = "RecordSolveAttempt"; Arguments = @{ SolveOutcome = "Success"; Reason = "after finalize" } },
        @{ Command = "RecordEnvironment"; Arguments = @{ LatitudeDegrees = 25.2; LongitudeDegrees = 55.27; ElevationMeters = 12; SiteSource = "after" } },
        @{ Command = "RecordCalibration"; Arguments = @{ RaPositionsDegrees = @(0.0) } },
        @{ Command = "RecordReseat"; Arguments = @{ RecalibrationPerformed = $true } },
        @{ Command = "RecordBlock"; Arguments = @{ BlockId = "after"; BlockStartUtc = $BaseUtc.AddMinutes(97); BlockEndUtc = $BaseUtc.AddMinutes(98); BlockLegs = @("iPolar", "TPPA-A", "TPPA-B", "TPPA-A", "iPolar") } },
        @{ Command = "RecordArtifact"; Arguments = @{ Path = (New-EvidenceFile); CreatedUtc = $BaseUtc.AddMinutes(96); QualitativeVerdict = "CrossInsideCircle" } },
        @{ Command = "RecordDarkFrame"; Arguments = @{ Path = (New-EvidenceFile); CapturedUtc = $BaseUtc.AddMinutes(96) } },
        @{ Command = "LinkTppaArtifact"; Arguments = @{ Path = (New-EvidenceFile); TppaRunId = "after" } }
    )
    foreach ($attempt in $postFinalizationAttempts) {
        $arguments = @{ Command = $attempt.Command; CampaignPath = $terminalCampaign; RecordedUtc = $BaseUtc.AddMinutes(97) }
        foreach ($key in $attempt.Arguments.Keys) { $arguments[$key] = $attempt.Arguments[$key] }
        Assert-Throws { & $ToolPath @arguments } "Finalization is terminal" "$($attempt.Command) must be refused after finalization"
    }
    $terminalBytesAfter = [IO.File]::ReadAllBytes($terminalLogPath)
    Assert-True ([Convert]::ToBase64String($terminalBytesBefore) -eq [Convert]::ToBase64String($terminalBytesAfter)) "a refused post-finalization command must not mutate the event log by even one byte"
    $terminalStaging = @(Get-ChildItem -LiteralPath (Join-Path $terminalCampaign "artifacts") -File -Force | Where-Object { $_.Name -like ".staging-*" })
    Assert-True ($terminalStaging.Count -eq 0) "a refused post-finalization artifact command must leave no staging file"

    # Resuming Finalize is the one permitted post-finalization operation, and it
    # reconstructs a missing report byte for byte.
    $terminalMarkdown = [IO.File]::ReadAllText($terminalFinal.ReportMarkdownPath)
    Remove-Item -LiteralPath $terminalFinal.ReportMarkdownPath -Force
    $terminalResume = & $ToolPath -Command Finalize -CampaignPath $terminalCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(99)
    Assert-True (Test-Path -LiteralPath $terminalResume.ReportMarkdownPath) "resume must reconstruct the missing report"
    Assert-True ([IO.File]::ReadAllText($terminalResume.ReportMarkdownPath) -ceq $terminalMarkdown) "the reconstructed report must be byte identical to the original"
    $terminalBytesAfterResume = [IO.File]::ReadAllBytes($terminalLogPath)
    Assert-True ([Convert]::ToBase64String($terminalBytesAfterResume) -eq [Convert]::ToBase64String($terminalBytesBefore)) "resume must not append another event"

    # --- Transactional artifact recording ----------------------------------------
    $orphanCampaign = New-CompleteCampaign -Name "orphan-recovery"
    $orphanEvidence = New-EvidenceFile -Name "orphan-candidate.png"
    $orphanArtifactDir = Join-Path $orphanCampaign "artifacts"
    $orphanBefore = @(Get-ChildItem -LiteralPath $orphanArtifactDir -File -Force).Count
    # The artifact's own CreatedUtc is acceptable, but the event timestamp regresses
    # against the last recorded event, so the rejection happens at append time.
    Assert-Throws { & $ToolPath -Command RecordArtifact -CampaignPath $orphanCampaign -RecordedUtc $BaseUtc.AddMinutes(50) `
        -Path $orphanEvidence -CreatedUtc $BaseUtc.AddMinutes(49) -QualitativeVerdict CrossInsideCircle } "must not move backwards" "a backdated artifact event must be refused"
    $orphanAfter = @(Get-ChildItem -LiteralPath $orphanArtifactDir -File -Force)
    Assert-True ($orphanAfter.Count -eq $orphanBefore) "a refused artifact import must leave no file in the artifacts directory"
    Assert-True (@($orphanAfter | Where-Object { $_.Name -like "*orphan-candidate*" }).Count -eq 0) "a refused artifact import must not leave a committed 000N copy"
    Assert-True (@($orphanAfter | Where-Object { $_.Name -like ".staging-*" }).Count -eq 0) "a refused artifact import must clean up its staging file"

    $orphanRetry = & $ToolPath -Command RecordArtifact -CampaignPath $orphanCampaign -RecordedUtc $BaseUtc.AddMinutes(92) `
        -Path $orphanEvidence -CreatedUtc $BaseUtc.AddMinutes(91) -QualitativeVerdict CrossInsideCircle
    Assert-True ($orphanRetry.EventType -eq "Artifact") "the corrected retry must succeed without manual cleanup"
    Assert-True (Test-Path -LiteralPath $orphanRetry.Payload.StoredPath) "the corrected retry must preserve its evidence"
    $retryOnDisk = (Get-FileHash -LiteralPath $orphanRetry.Payload.StoredPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-True ($retryOnDisk -eq $orphanRetry.Payload.Sha256) "the retried preserved copy must match its recorded hash"

    # A genuine committed collision must still fail closed.
    $collisionEvidence = New-EvidenceFile -Name "collision.png"
    $nextSequence = @([IO.File]::ReadAllLines((Join-Path $orphanCampaign "events.jsonl")) | Where-Object { $_ }).Count + 1
    $squatted = Join-Path $orphanArtifactDir ("{0:D4}-collision.png" -f $nextSequence)
    [IO.File]::WriteAllText($squatted, "pre-existing committed evidence")
    Assert-Throws { & $ToolPath -Command RecordArtifact -CampaignPath $orphanCampaign -RecordedUtc $BaseUtc.AddMinutes(93) `
        -Path $collisionEvidence -CreatedUtc $BaseUtc.AddMinutes(92) -QualitativeVerdict CrossInsideCircle } "already exists" "a committed destination collision must fail closed"
    Assert-True ([IO.File]::ReadAllText($squatted) -eq "pre-existing committed evidence") "a collision must never overwrite committed evidence"

    # --- Concurrent append safety ------------------------------------------------
    #
    # Children must REPORT their outcome. An earlier version of this test swallowed
    # every child exception and never asserted that any writer committed, and it also
    # gave writers 2..6 a CreatedUtc later than their event timestamp, so they were
    # rejected before ever reaching the lock. It therefore passed with a single
    # writer and would have passed with the lock deleted outright.
    $raceHost = if ($PSVersionTable.PSVersion.Major -ge 6) { "pwsh" } else { "powershell" }
    $raceChild = Join-Path $TestRoot "race-child.ps1"
    @'
param($Tool, $Campaign, $Barrier, $ResultFile, $Utc, $Evidence, $Created)
while (-not (Test-Path -LiteralPath $Barrier)) { Start-Sleep -Milliseconds 5 }
try {
    # "NONE" means "no artifact". Windows PowerShell 5.1 rejects an empty string
    # inside Start-Process -ArgumentList, and parses a bare "-" as a parameter
    # name, so a word sentinel is used.
    if ($Evidence -eq "NONE") {
        & $Tool -Command RecordSolveAttempt -CampaignPath $Campaign -RecordedUtc ([datetime]::Parse($Utc).ToUniversalTime()) `
            -SolveOutcome Success -Reason "parallel" | Out-Null
    } else {
        & $Tool -Command RecordArtifact -CampaignPath $Campaign -RecordedUtc ([datetime]::Parse($Utc).ToUniversalTime()) `
            -Path $Evidence -CreatedUtc ([datetime]::Parse($Created).ToUniversalTime()) -QualitativeVerdict CrossInsideCircle | Out-Null
    }
    Set-Content -LiteralPath $ResultFile -Value "OK" -Encoding utf8
} catch {
    Set-Content -LiteralPath $ResultFile -Value ("FAIL:" + $_.Exception.Message) -Encoding utf8
}
'@ | Set-Content -LiteralPath $raceChild -Encoding utf8

    function Invoke-RaceWriters {
        param([string]$Campaign, [int]$WriterCount, [string]$Tag, [scriptblock]$ArgumentFactory)

        $barrier = Join-Path $TestRoot "race-barrier-$Tag.txt"
        $processes = @()
        $resultFiles = @()
        for ($index = 1; $index -le $WriterCount; $index++) {
            $resultFile = Join-Path $TestRoot "race-$Tag-$index.result"
            $resultFiles += $resultFile
            $extra = & $ArgumentFactory $index
            $processes += Start-Process -FilePath $raceHost -PassThru -WindowStyle Hidden -ArgumentList (@(
                "-NoProfile", "-File", $raceChild, $ToolPath, $Campaign, $barrier, $resultFile) + $extra)
        }
        Start-Sleep -Seconds 6
        [IO.File]::WriteAllText($barrier, "go")
        foreach ($process in $processes) { $null = $process.WaitForExit(300000) }

        $succeeded = 0
        $reported = 0
        foreach ($resultFile in $resultFiles) {
            if (-not (Test-Path -LiteralPath $resultFile)) { continue }
            $reported++
            if ([IO.File]::ReadAllText($resultFile).Trim() -eq "OK") { $succeeded++ }
        }
        return [pscustomobject]@{ Reported = $reported; Succeeded = $succeeded; Total = $WriterCount }
    }

    function Assert-RaceChainIntact {
        param([string]$Campaign, [int]$ExpectedEventCount, [string]$Label)

        $lines = @([IO.File]::ReadAllLines((Join-Path $Campaign "events.jsonl")) | Where-Object { $_ })
        $events = @()
        foreach ($line in $lines) { $events += ($line | ConvertFrom-Json) }
        Assert-True ($events.Count -eq $lines.Count) "$Label : every event line must be complete, parseable JSON"
        Assert-True ($lines.Count -eq $ExpectedEventCount) "$Label : event count must equal pre-race count plus successful writers (expected $ExpectedEventCount, got $($lines.Count))"
        $sequences = @($events | ForEach-Object { $_.Sequence })
        Assert-True (@($sequences | Sort-Object -Unique).Count -eq $sequences.Count) "$Label : concurrent writers must never reuse a sequence number"
        for ($index = 0; $index -lt $sequences.Count; $index++) {
            Assert-True ($sequences[$index] -eq ($index + 1)) "$Label : sequences must be contiguous and ordered"
        }
        $staging = @(Get-ChildItem -LiteralPath (Join-Path $Campaign "artifacts") -File -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -like ".staging-*" })
        Assert-True ($staging.Count -eq 0) "$Label : no orphan staging file may survive"
    }

    # Case 1 — every writer is eligible, so the lock must let ALL of them through.
    # Identical event timestamps, and solve attempts carry no artifact-ordering rule.
    $raceCampaign = New-CompleteCampaign -Name "race"
    $raceBefore = @([IO.File]::ReadAllLines((Join-Path $raceCampaign "events.jsonl")) | Where-Object { $_ }).Count
    $raceOutcome = Invoke-RaceWriters -Campaign $raceCampaign -WriterCount 6 -Tag "solve" -ArgumentFactory {
        param($index)
        @($BaseUtc.AddMinutes(92).ToString("o"), "NONE", "NONE")
    }
    Assert-True ($raceOutcome.Reported -eq 6) "all six writers must report an outcome (got $($raceOutcome.Reported))"
    Assert-True ($raceOutcome.Succeeded -eq 6) "every eligible concurrent writer must commit (got $($raceOutcome.Succeeded) of 6)"
    Assert-RaceChainIntact -Campaign $raceCampaign -ExpectedEventCount ($raceBefore + 6) -Label "solve-attempt race"
    $raceEvaluation = & $ToolPath -Command Evaluate -CampaignPath $raceCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(120)
    Assert-True ($raceEvaluation.IntegrityValid) "the hash chain must remain valid after concurrent writers"
    Assert-True (@($raceEvaluation.FailedGates | Where-Object { $_.Gate -eq "HashChain" -or $_.Gate -eq "EventSequence" }).Count -eq 0) "concurrent writers must not corrupt the chain"

    # Case 2 — concurrent artifact imports. Each carries a valid CreatedUtc that is
    # before its event time, so every writer genuinely reaches staging and the lock.
    # Artifact ordering means the surviving set depends on commit order, so assert
    # the invariants that must hold for any interleaving.
    $raceArtifactCampaign = New-CompleteCampaign -Name "race-artifacts"
    $raceArtifactBefore = @([IO.File]::ReadAllLines((Join-Path $raceArtifactCampaign "events.jsonl")) | Where-Object { $_ }).Count
    $raceArtifactFiles = @{}
    for ($index = 1; $index -le 6; $index++) { $raceArtifactFiles[$index] = New-EvidenceFile -Name "race-artifact-$index.png" }
    $artifactOutcome = Invoke-RaceWriters -Campaign $raceArtifactCampaign -WriterCount 6 -Tag "artifact" -ArgumentFactory {
        param($index)
        @($BaseUtc.AddMinutes(110).ToString("o"), $raceArtifactFiles[$index], $BaseUtc.AddMinutes(100 + $index).ToString("o"))
    }
    Assert-True ($artifactOutcome.Reported -eq 6) "all six artifact writers must report an outcome"
    Assert-True ($artifactOutcome.Succeeded -ge 1) "at least one artifact writer must commit under contention"
    Assert-RaceChainIntact -Campaign $raceArtifactCampaign -ExpectedEventCount ($raceArtifactBefore + $artifactOutcome.Succeeded) -Label "artifact race"
    $artifactRaceEvaluation = & $ToolPath -Command Evaluate -CampaignPath $raceArtifactCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(130)
    Assert-True ($artifactRaceEvaluation.ArtifactIntegrity.PreservedFailureCount -eq 0) "every artifact committed under contention must verify byte for byte"
    Assert-True (@($artifactRaceEvaluation.FailedGates | Where-Object { $_.Gate -eq "HashChain" -or $_.Gate -eq "EventSequence" }).Count -eq 0) "artifact contention must not corrupt the chain"

    # --- Report output must never be able to destroy campaign state ---------------
    #
    # Evaluate verifies the recorded evidence and THEN writes its report. If the
    # report path could name campaign state, the write would destroy the very
    # evidence the returned verdict had just certified as intact.
    $outputGuardCampaign = New-CompleteCampaign -Name "output-guard"
    $guardArtifactPath = Get-PreservedPath -Campaign $outputGuardCampaign -EventType "Artifact"
    $guardDarkPath = Get-PreservedPath -Campaign $outputGuardCampaign -EventType "DarkFrame"
    $guardLogPath = Join-Path $outputGuardCampaign "events.jsonl"
    $guardHeaderPath = Join-Path $outputGuardCampaign "campaign.json"
    $guardTargets = @{
        "preserved artifact" = $guardArtifactPath
        "preserved dark frame" = $guardDarkPath
        "event log" = $guardLogPath
        "campaign header" = $guardHeaderPath
        "campaign root file" = (Join-Path $outputGuardCampaign "anything.json")
        "artifacts directory file" = (Join-Path (Join-Path $outputGuardCampaign "artifacts") "report.json")
    }
    foreach ($guardLabel in $guardTargets.Keys) {
        $guardTarget = $guardTargets[$guardLabel]
        $before = if (Test-Path -LiteralPath $guardTarget) { (Get-FileHash -LiteralPath $guardTarget -Algorithm SHA256).Hash } else { "" }
        Assert-Throws { & $ToolPath -Command Evaluate -CampaignPath $outputGuardCampaign -PolicyPath $policy `
            -RecordedUtc $BaseUtc.AddMinutes(95) -OutputPath $guardTarget } "inside the campaign directory" "-OutputPath naming the $guardLabel must be refused"
        if ($before -ne "") {
            $after = (Get-FileHash -LiteralPath $guardTarget -Algorithm SHA256).Hash
            Assert-True ($before -eq $after) "refusing -OutputPath must leave the $guardLabel byte-identical"
        } else {
            Assert-True (-not (Test-Path -LiteralPath $guardTarget)) "refusing -OutputPath must not create a file inside the campaign"
        }
    }
    # The campaign must still be fully intact and qualified afterwards.
    $afterGuard = & $ToolPath -Command Evaluate -CampaignPath $outputGuardCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(95)
    Assert-True ($afterGuard.QualificationLevel -eq "QualifiedCorroboratingWitness") "the guarded campaign must remain qualified"
    Assert-True ($afterGuard.IntegrityValid) "the guarded campaign must remain integrity-valid"
    # A path outside the campaign is still accepted.
    $outsideOutput = Join-Path $TestRoot "outside-evaluation.json"
    & $ToolPath -Command Evaluate -CampaignPath $outputGuardCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(95) -OutputPath $outsideOutput | Out-Null
    Assert-True (Test-Path -LiteralPath $outsideOutput) "-OutputPath outside the campaign must still be written"

    # Finalize must not be able to drop reports onto evidence or campaign state.
    $reportDirCampaign = New-CompleteCampaign -Name "report-dir-guard"
    Assert-Throws { & $ToolPath -Command Finalize -CampaignPath $reportDirCampaign -PolicyPath $policy `
        -RecordedUtc $BaseUtc.AddMinutes(96) -ReportDirectory (Join-Path $reportDirCampaign "artifacts") } "preserved evidence" "-ReportDirectory inside artifacts must be refused"
    Assert-Throws { & $ToolPath -Command Finalize -CampaignPath $reportDirCampaign -PolicyPath $policy `
        -RecordedUtc $BaseUtc.AddMinutes(96) -ReportDirectory $reportDirCampaign } "campaign root" "-ReportDirectory equal to the campaign root must be refused"
    $reportDirLast = (@([IO.File]::ReadAllLines((Join-Path $reportDirCampaign "events.jsonl")) | Where-Object { $_ })[-1] | ConvertFrom-Json)
    Assert-True ($reportDirLast.EventType -ne "CampaignFinalized") "a refused report directory must not finalize the campaign"

    # --- Resume must be pinned to the sealed finalization ------------------------
    $pinCampaign = New-CompleteCampaign -Name "resume-pinned"
    $pinFinal = & $ToolPath -Command Finalize -CampaignPath $pinCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(96)
    Assert-True ($pinFinal.QualificationLevel -eq "QualifiedCorroboratingWitness") "the pinning fixture must finalize qualified"
    $pinMarkdownBytes = [IO.File]::ReadAllBytes($pinFinal.ReportMarkdownPath)

    # Byte-level resume: a report re-encoded with a BOM has identical TEXT but
    # different BYTES, and must be refused by the report comparison itself.
    [IO.File]::WriteAllText($pinFinal.ReportMarkdownPath, [Text.Encoding]::UTF8.GetString($pinMarkdownBytes), [Text.UTF8Encoding]::new($true))
    Assert-Throws { & $ToolPath -Command Finalize -CampaignPath $pinCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(97) } "byte for byte" "a re-encoded report must be refused on bytes, not accepted as identical text"
    [IO.File]::WriteAllBytes($pinFinal.ReportMarkdownPath, $pinMarkdownBytes)

    # Resuming under a different policy must be refused rather than minting reports
    # whose verdict contradicts the sealed finalization event.
    $otherPolicy = New-PolicyFile -Name "other-policy.json"
    $otherPolicyText = [IO.File]::ReadAllText($otherPolicy) -replace 'ipolar-uncertainty-policy-1', 'a-different-policy'
    [IO.File]::WriteAllText($otherPolicy, $otherPolicyText, [Text.UTF8Encoding]::new($false))
    Remove-Item -LiteralPath $pinFinal.ReportMarkdownPath -Force
    Assert-Throws { & $ToolPath -Command Finalize -CampaignPath $pinCampaign -PolicyPath $otherPolicy -RecordedUtc $BaseUtc.AddMinutes(98) } "records policy" "resuming under a different policy must be refused"
    # With the original policy it reconstructs byte-identically.
    $pinResume = & $ToolPath -Command Finalize -CampaignPath $pinCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(99)
    $pinResumedBytes = [IO.File]::ReadAllBytes($pinResume.ReportMarkdownPath)
    Assert-True ([Convert]::ToBase64String($pinResumedBytes) -eq [Convert]::ToBase64String($pinMarkdownBytes)) "resume must reconstruct the report byte for byte"

    # A non-terminal chain must not be given fresh reports even with no baseline.
    $nonTerminalCampaign = New-CompleteCampaign -Name "non-terminal-resume"
    $nonTerminalFinal = & $ToolPath -Command Finalize -CampaignPath $nonTerminalCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(96)
    $nonTerminalLog = Join-Path $nonTerminalCampaign "events.jsonl"
    $nonTerminalLast = (@([IO.File]::ReadAllLines($nonTerminalLog) | Where-Object { $_ })[-1] | ConvertFrom-Json)
    $forgedEvent = [ordered]@{
        Sequence = $nonTerminalLast.Sequence + 1
        RecordedUtc = "2026-07-26T19:40:00.0000000Z"
        EventType = "SolveAttempt"
        PreviousEventHash = $nonTerminalLast.EventHash
        Payload = [ordered]@{ AttemptIndex = 99; Succeeded = $true; AvailableStarCount = 20; Reason = "forged"; ManualObservation = ""; Notes = "" }
        EventHash = ("0" * 64)
    }
    [IO.File]::AppendAllText($nonTerminalLog, (($forgedEvent | ConvertTo-Json -Depth 12 -Compress) + "`n"))
    Remove-Item -LiteralPath (Split-Path -Parent $nonTerminalFinal.ReportJsonPath) -Recurse -Force
    Assert-Throws { & $ToolPath -Command Finalize -CampaignPath $nonTerminalCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(100) } "" "a non-terminal chain must not receive regenerated reports even with no baseline"
    Assert-True (-not (Test-Path -LiteralPath $nonTerminalFinal.ReportJsonPath)) "no report may be minted for a mutated chain"

    # --- A linked TPPA artifact cannot be double-counted -------------------------
    $linkCampaign = New-CompleteCampaign -Name "link-duplicate"
    $linkEvidence = New-EvidenceFile -Name "tppa-link-once.json"
    & $ToolPath -Command LinkTppaArtifact -CampaignPath $linkCampaign -RecordedUtc $BaseUtc.AddMinutes(92) `
        -Path $linkEvidence -TppaRunId "tppa-run-x" -Description "first link" | Out-Null
    Assert-Throws { & $ToolPath -Command LinkTppaArtifact -CampaignPath $linkCampaign -RecordedUtc $BaseUtc.AddMinutes(93) `
        -Path $linkEvidence -TppaRunId "tppa-run-y" -Description "same file again" } "same SHA256" "linking the same file twice must be refused"

    # --- Cross-host consistency ---------------------------------------------------
    # A campaign written by one PowerShell host must verify under the other, with
    # the same chain head hash and the same verdict.
    $otherHost = if ($PSVersionTable.PSVersion.Major -ge 6) { "powershell" } else { "pwsh" }
    $otherHostPath = (Get-Command $otherHost -ErrorAction SilentlyContinue)
    if ($null -ne $otherHostPath) {
        $crossEvaluation = & $ToolPath -Command Evaluate -CampaignPath $bytesCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(95)
        $crossOutput = Join-Path $TestRoot "cross-host-evaluation.json"
        & $otherHostPath.Source -NoProfile -File $ToolPath -Command Evaluate -CampaignPath $bytesCampaign `
            -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(95) -OutputPath $crossOutput *> $null
        Assert-True (Test-Path -LiteralPath $crossOutput) "the other PowerShell host must produce an evaluation"
        $crossOther = [IO.File]::ReadAllText($crossOutput) | ConvertFrom-Json
        Assert-True ($crossOther.ChainHeadHash -eq $crossEvaluation.ChainHeadHash) "both PowerShell hosts must compute the same chain head hash"
        Assert-True ($crossOther.QualificationLevel -eq $crossEvaluation.QualificationLevel) "both PowerShell hosts must reach the same verdict"
        Assert-True ($crossOther.IntegrityValid -eq $crossEvaluation.IntegrityValid) "both PowerShell hosts must agree on integrity"
        Assert-True ($crossOther.ArtifactIntegrity.PreservedFailureCount -eq 0) "the other host must verify the preserved bytes too"
    } else {
        Write-Host "  (skipped cross-host check: $otherHost is not available)"
    }

    # --- Rejected campaigns exit non-zero ---------------------------------------
    & $ToolPath -Command Evaluate -CampaignPath $eightOfTen -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(95) *> $null
    Assert-True ($LASTEXITCODE -eq 2) "a rejected evaluation must exit 2 (was $LASTEXITCODE)"
    & $ToolPath -Command Evaluate -CampaignPath $qualitativeCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(95) *> $null
    Assert-True ($LASTEXITCODE -eq 0) "an accepted evaluation must exit 0 (was $LASTEXITCODE)"

    Write-Host "All ipolar_witness_campaign CLI tests passed."
} finally {
    if (Test-Path -LiteralPath $TestRoot) { Remove-Item -LiteralPath $TestRoot -Recurse -Force }
}
