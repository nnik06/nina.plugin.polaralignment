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
    Assert-Throws { & $ToolPath -Command Finalize -CampaignPath $modifiedCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(96) -ReportDirectory (Join-Path $TestRoot "modified-reports") } "" "finalization must fail closed while evidence is modified"

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
    $raceCampaign = New-CompleteCampaign -Name "race"
    $raceBarrier = Join-Path $TestRoot "race-barrier.txt"
    $raceChild = Join-Path $TestRoot "race-child.ps1"
    @'
param($Tool, $Campaign, $Barrier, $Utc, $Evidence, $Created)
while (-not (Test-Path -LiteralPath $Barrier)) { Start-Sleep -Milliseconds 5 }
try {
    & $Tool -Command RecordArtifact -CampaignPath $Campaign -RecordedUtc ([datetime]::Parse($Utc).ToUniversalTime()) `
        -Path $Evidence -CreatedUtc ([datetime]::Parse($Created).ToUniversalTime()) -QualitativeVerdict CrossInsideCircle | Out-Null
} catch { }
'@ | Set-Content -LiteralPath $raceChild -Encoding utf8

    $writerCount = 6
    $raceHost = if ($PSVersionTable.PSVersion.Major -ge 6) { "pwsh" } else { "powershell" }
    $raceProcesses = @()
    for ($index = 1; $index -le $writerCount; $index++) {
        $raceEvidence = New-EvidenceFile -Name "race-$index.png"
        $raceProcesses += Start-Process -FilePath $raceHost -PassThru -WindowStyle Hidden -ArgumentList @(
            "-NoProfile", "-File", $raceChild, $ToolPath, $raceCampaign, $raceBarrier,
            $BaseUtc.AddMinutes(92).ToString("o"), $raceEvidence, $BaseUtc.AddMinutes(91 + $index).ToString("o"))
    }
    Start-Sleep -Seconds 6
    [IO.File]::WriteAllText($raceBarrier, "go")
    foreach ($raceProcess in $raceProcesses) { $null = $raceProcess.WaitForExit(240000) }

    $raceLines = @([IO.File]::ReadAllLines((Join-Path $raceCampaign "events.jsonl")) | Where-Object { $_ })
    $raceEvents = @()
    foreach ($raceLine in $raceLines) { $raceEvents += ($raceLine | ConvertFrom-Json) }
    Assert-True ($raceEvents.Count -eq $raceLines.Count) "every event line must be complete, parseable JSON after concurrent writers"
    $raceSequences = @($raceEvents | ForEach-Object { $_.Sequence })
    Assert-True (@($raceSequences | Sort-Object -Unique).Count -eq $raceSequences.Count) "concurrent writers must never reuse a sequence number"
    for ($index = 0; $index -lt $raceSequences.Count; $index++) {
        Assert-True ($raceSequences[$index] -eq ($index + 1)) "concurrent writers must produce a contiguous ordered sequence"
    }
    $raceStaging = @(Get-ChildItem -LiteralPath (Join-Path $raceCampaign "artifacts") -File -Force | Where-Object { $_.Name -like ".staging-*" })
    Assert-True ($raceStaging.Count -eq 0) "losing contenders must leave no orphan staging file"
    $raceEvaluation = & $ToolPath -Command Evaluate -CampaignPath $raceCampaign -PolicyPath $policy -RecordedUtc $BaseUtc.AddMinutes(120)
    Assert-True ($raceEvaluation.IntegrityValid) "the hash chain must remain valid after concurrent writers"
    Assert-True (@($raceEvaluation.FailedGates | Where-Object { $_.Gate -eq "HashChain" -or $_.Gate -eq "EventSequence" }).Count -eq 0) "concurrent writers must not corrupt the chain"
    Assert-True ($raceEvaluation.ArtifactIntegrity.PreservedFailureCount -eq 0) "every artifact committed under contention must verify byte for byte"

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
