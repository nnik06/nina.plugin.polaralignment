$scriptPath = Join-Path $PSScriptRoot '..\analyze_tppa_fast_alignment_runs.ps1'
$scriptText = [IO.File]::ReadAllText((Resolve-Path $scriptPath))
$repositoryHead = '0123456789abcdef0123456789abcdef01234567'
$pluginAssemblySha256 = '11' * 32
$covarianceAuthorityId = 'aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee'
$covarianceAuthoritySha256 = '22' * 32
$cadenceAuthorityId = 'bbbbbbbb-cccc-4ddd-8eee-ffffffffffff'
$cadenceAuthoritySha256 = '33' * 32
$qualifiedSettleSeconds = 15.0
$qualifiedFreshDeterminationSeconds = 45.0
$mechanicalStateId = '44' * 32
$preregisteredCampaignId = '12345678-1234-4abc-8def-1234567890ab'
$loadProfileId = 'commissioned-load-profile-v1'

function New-FastRunLog(
    [string]$Path,
    [int]$RunCount = 20,
    [int]$PassingRunCount = 18,
    [int]$RefractionOffRun = 0,
    [int]$RefractionStringRun = 0,
    [int]$OutOfOrderRun = 0,
    [int]$DuplicateTerminalRun = 0,
    [int]$NonfiniteFinalRun = 0,
    [int]$NumericStringRun = 0,
    [int]$OffsetlessTimestampRun = 0,
    [int]$WallClockMismatchRun = 0,
    [int]$PostFinalMismatchRun = 0,
    [int]$ImprovementMismatchRun = 0,
    [int]$PreMismatchRun = 0,
    [int]$RuntimeExceededRun = 0,
    [int]$FinalAboveToleranceRun = 0,
    [int]$InitialOutsideRangeRun = 0,
    [switch]$UseObjectiveStrata) {
    $lines = [Collections.Generic.List[string]]::new()
    $stratifiedTotals = [Collections.Generic.List[double]]::new()
    if ($UseObjectiveStrata) {
        $bounds = @(0.0, 30.0, 60.0, 120.0, 180.0, 300.0, 424.264069)
        $counts = [int[]]::new(6)
        $minimum = [Math]::Floor($RunCount / 6)
        if ($minimum -lt 1) { throw 'Stratified synthetic logs require at least six runs.' }
        for ($stratum = 0; $stratum -lt 6; $stratum++) { $counts[$stratum] = $minimum }
        $remainderOrder = @(0, 5, 1, 4, 2, 3)
        for ($extra = 0; $extra -lt ($RunCount - 6 * $minimum); $extra++) {
            $counts[$remainderOrder[$extra]]++
        }
        for ($stratum = 0; $stratum -lt 6; $stratum++) {
            $midpoint = ($bounds[$stratum] + $bounds[$stratum + 1]) / 2.0
            for ($sample = 0; $sample -lt $counts[$stratum]; $sample++) { $stratifiedTotals.Add($midpoint) }
        }
    }
    for ($index = 1; $index -le $RunCount; $index++) {
        $runId = [Guid]::NewGuid().ToString('D')
        $nightOffset = [Math]::Floor(($index - 1) / 7)
        $start = [DateTimeOffset]::Parse('2026-08-03T18:00:00Z').AddDays($nightOffset).AddMinutes($index)
        $refraction = if ($index -eq $RefractionStringRun) { 'false' } else { $index -ne $RefractionOffRun }
        $initialTotal = if ($index -eq $InitialOutsideRangeRun) {
            [Math]::Sqrt(2.0 * 325.0 * 325.0)
        } elseif ($UseObjectiveStrata) {
            $stratifiedTotals[$index - 1]
        } else {
            24.0
        }
        $initialAzimuth = if ($index -eq $InitialOutsideRangeRun) { 325.0 } else { 0.8 * $initialTotal }
        $initialAltitude = if ($index -eq $InitialOutsideRangeRun) { 325.0 } else { 0.6 * $initialTotal }
        $events = @(
            [ordered]@{
                schemaVersion = 1; runId = $runId; event = 'started'
                observedUtc = if ($index -eq $OffsetlessTimestampRun) { $start.ToString('yyyy-MM-ddTHH:mm:ss') } else { $start.ToString('O') }
                elapsedSeconds = 0.0
                settleSeconds = 30.0; exposureSeconds = 1.0
                alignmentToleranceMinutes = 3.0
                refractionAdjustmentEnabled = $refraction
                repositoryHead = $repositoryHead; pluginAssemblySha256 = $pluginAssemblySha256
                covarianceAuthorityId = $covarianceAuthorityId; covarianceAuthoritySha256 = $covarianceAuthoritySha256
                cadenceAuthorityId = $cadenceAuthorityId; cadenceAuthoritySha256 = $cadenceAuthoritySha256
                qualifiedSettleSeconds = $qualifiedSettleSeconds
                qualifiedFreshDeterminationSeconds = $qualifiedFreshDeterminationSeconds
                mechanicalStateId = $mechanicalStateId
                loadProfileId = $loadProfileId; tppaCampaignId = [Guid]::NewGuid().ToString('D')
                preregisteredCampaignId = $preregisteredCampaignId
            }
            [ordered]@{
                schemaVersion = 1; runId = $runId; event = 'initial-fresh-determination'
                observedUtc = $start.AddSeconds(75).ToString('O'); elapsedSeconds = 75.0
                azimuthMinutes = $initialAzimuth; altitudeMinutes = $initialAltitude; totalMinutes = $initialTotal
            }
        )
        if ($index -eq $OutOfOrderRun) { [array]::Reverse($events) }
        foreach ($event in $events) {
            $lines.Add("2026-08-03|INFO| TPPA_FAST_RUN_EVENT $($event | ConvertTo-Json -Compress)")
        }

        if ($index -le $PassingRunCount) {
            $preAzimuth = if ($index -eq $PreMismatchRun) { 3.0 } else { $initialAzimuth }
            $preAltitude = if ($index -eq $PreMismatchRun) { 0.0 } else { $initialAltitude }
            $preTotal = if ($index -eq $PreMismatchRun) { 3.0 } else { $initialTotal }
            $postAzimuth = if ($index -eq $FinalAboveToleranceRun) { 3.2 } else { 1.6 }
            $postAltitude = if ($index -eq $FinalAboveToleranceRun) { 2.4 } else { 1.2 }
            $postTotal = if ($index -eq $FinalAboveToleranceRun) { 4.0 } else { 2.0 }
            $improvement = $preTotal - $postTotal
            if ($index -eq $ImprovementMismatchRun) { $improvement += 1.0 }
            $post = [ordered]@{
                schemaVersion = 1; runId = $runId; event = 'post-move-response'
                observedUtc = $start.AddSeconds(165).ToString('O'); elapsedSeconds = 165.0
                classification = 'ConvergedCandidate'
                preAzimuthMinutes = $preAzimuth; preAltitudeMinutes = $preAltitude; preTotalMinutes = $preTotal
                postAzimuthMinutes = $postAzimuth; postAltitudeMinutes = $postAltitude; postTotalMinutes = $postTotal
                totalImprovementMinutes = $improvement; requiredImprovementMinutes = 0.5
            }
            $finalAzimuth = if ($index -eq $PostFinalMismatchRun) { 8.0 } else { $postAzimuth }
            $finalAltitude = if ($index -eq $PostFinalMismatchRun) { 6.0 } else { $postAltitude }
            $finalTotal = if ($index -eq $PostFinalMismatchRun) { 10.0 } elseif ($index -eq $NonfiniteFinalRun) { 'NaN' } elseif ($index -eq $NumericStringRun) { '2.0' } else { $postTotal }
            $completedSeconds = if ($index -eq $RuntimeExceededRun) { 301.0 } else { 250.0 }
            $completedUtc = if ($index -eq $WallClockMismatchRun) { $start.AddMinutes(50) } else { $start.AddSeconds($completedSeconds) }
            $completed = [ordered]@{
                schemaVersion = 1; runId = $runId; event = 'completed'
                observedUtc = $completedUtc.ToString('O'); elapsedSeconds = $completedSeconds
                outcome = 'fresh-confirmed-within-tolerance'; moveCount = 1
                finalAzimuthMinutes = $finalAzimuth; finalAltitudeMinutes = $finalAltitude
                finalTotalMinutes = $finalTotal
            }
            $lines.Add("2026-08-03|INFO| TPPA_FAST_RUN_EVENT $($post | ConvertTo-Json -Compress)")
            $lines.Add("2026-08-03|INFO| TPPA_FAST_RUN_EVENT $($completed | ConvertTo-Json -Compress)")
            if ($index -eq $DuplicateTerminalRun) {
                $abandoned = [ordered]@{
                    schemaVersion = 1; runId = $runId; event = 'abandoned'
                    observedUtc = $start.AddSeconds(251).ToString('O'); elapsedSeconds = 251.0
                    outcome = 'no-terminal-event'
                }
                $lines.Add("2026-08-03|INFO| TPPA_FAST_RUN_EVENT $($abandoned | ConvertTo-Json -Compress)")
            }
        } else {
            $failed = [ordered]@{
                schemaVersion = 1; runId = $runId; event = 'failed'
                observedUtc = $start.AddSeconds(170).ToString('O'); elapsedSeconds = 170.0
                outcome = 'exception'; exceptionType = 'SequenceEntityFailedException'
            }
            $lines.Add("2026-08-03|INFO| TPPA_FAST_RUN_EVENT $($failed | ConvertTo-Json -Compress)")
        }
    }
    $lines | Set-Content -LiteralPath $Path -Encoding utf8
}

function New-FastCampaignManifest([string]$Path, [string[]]$Logs, [int]$ExpectedAttempts = 20) {
    foreach ($logPath in $Logs) {
        $text = [IO.File]::ReadAllText($logPath)
        $rewritten = [regex]::Replace($text, '"settleSeconds":30(?:\.0)?', '"settleSeconds":15.0')
        if ($rewritten -eq $text) { throw "sealed fixture contains no unconditional settle telemetry: $logPath" }
        [IO.File]::WriteAllText($logPath, $rewritten, [Text.UTF8Encoding]::new($false))
    }
    $creator = Join-Path $PSScriptRoot '..\new_tppa_fast_alignment_campaign.ps1'
    $created = & $creator -CampaignId $preregisteredCampaignId -OpticalTrainId 'WO-GT81-IV-0.8-OAG-L-ASI2600MM-gain100-bin1' `
        -RepositoryHead $repositoryHead -PluginAssemblySha256 $pluginAssemblySha256 -CovarianceAuthorityId $covarianceAuthorityId -CovarianceAuthoritySha256 $covarianceAuthoritySha256 -CadenceAuthorityId $cadenceAuthorityId -CadenceAuthoritySha256 $cadenceAuthoritySha256 -QualifiedSettleSeconds $qualifiedSettleSeconds -QualifiedFreshDeterminationSeconds $qualifiedFreshDeterminationSeconds -MechanicalStateId $mechanicalStateId -LoadProfileId $loadProfileId -LogPath $Logs -OutputPath $Path -ExpectedAttemptCount $ExpectedAttempts `
        -CampaignEndUtc ([DateTimeOffset]::UtcNow.AddDays(4))
    $created.CampaignId | Should Be $preregisteredCampaignId
    $created.SetForNextNinaLaunch | Should Match 'TPPA_PREREGISTERED_CAMPAIGN_ID'
    $manifest = [IO.File]::ReadAllText($Path) | ConvertFrom-Json
    $manifest.CreatedUtc = '2026-08-03T17:00:00Z'
    $manifest.CampaignStartUtc = '2026-08-03T17:00:00Z'
    $manifest.CampaignEndUtc = '2026-08-07T23:00:00Z'
    [IO.File]::WriteAllText($Path, ($manifest | ConvertTo-Json -Depth 6) + "`r`n", [Text.UTF8Encoding]::new($false))
    return [pscustomobject]@{ Path = $Path; Sha256 = (Get-FileHash $Path -Algorithm SHA256).Hash }
}

Describe 'TPPA fast alignment evidence analyzer contract' {
    It 'pins the limited claim boundary' {
        $scriptText.Contains('FastAlignmentEvidenceQualified') | Should Be $true
        $scriptText.Contains('AbsoluteAccuracyQualified = $false') | Should Be $true
        $scriptText.Contains('DeliveredExposureQualified = $false') | Should Be $true
        $scriptText.Contains('OverallGoalQualified = $false') | Should Be $true
    }

    It 'qualifies eighteen of twenty eligible one-move runs across three Dubai nights' {
        $log = Join-Path $TestDrive 'qualified.log'
        New-FastRunLog $log -UseObjectiveStrata

        $manifest = Join-Path $TestDrive 'campaign.json'
        $sealed = New-FastCampaignManifest $manifest @($log)
        $result = & $scriptPath -LogPath $log -CampaignManifestPath $manifest `
            -ExpectedCampaignManifestSha256 $sealed.Sha256

        $result.MinimumEligibleRuns | Should Be 20
        $result.RequiredPassingRuns | Should Be 16
        $result.RequiredPassRate | Should Be 0.8
        $result.EligibleRunCount | Should Be 20
        $result.PassingRunCount | Should Be 18
        $result.PassingNightCount | Should Be 3
        $result.FalseSuccessCount | Should Be 0
        $result.InvalidEvidenceRunCount | Should Be 0
        $result.FastAlignmentEvidenceQualified | Should Be $true
        $result.CampaignPopulationComplete | Should Be $true
        $result.CampaignStrataComplete | Should Be $true
        $result.CampaignStrata.Count | Should Be 6
        @($result.CampaignStrata.MinimumSuccessfulAttempts) | Should Be @(2, 1, 1, 1, 1, 2)
        $result.PopulationAttemptCount | Should Be 20
        $result.PopulationPassRate | Should Be 0.9
        $result.PopulationPassRateWilson95Lower | Should BeGreaterThan 0.69
        $result.PopulationPassRateWilson95Lower | Should BeLessThan 0.71
        $result.PopulationPassRateWilson95Upper | Should BeGreaterThan 0.96
        $result.PopulationPassRateWilson95Upper | Should BeLessThan 0.98
        $result.PreregisteredCampaignPassRateMet | Should Be $true
        $result.AbsoluteAccuracyQualified | Should Be $false
        $result.DeliveredExposureQualified | Should Be $false
        $result.OverallGoalQualified | Should Be $false
    }

    It 'rejects a complete denominator concentrated in only the easiest starting stratum' {
        $log = Join-Path $TestDrive 'unstratified.log'
        New-FastRunLog $log
        $manifest = Join-Path $TestDrive 'unstratified-campaign.json'
        $sealed = New-FastCampaignManifest $manifest @($log)
        $result = & $scriptPath -LogPath $log -CampaignManifestPath $manifest `
            -ExpectedCampaignManifestSha256 $sealed.Sha256
        $result.CampaignPopulationComplete | Should Be $true
        $result.PopulationPassRate | Should Be 0.9
        $result.PopulationPassRateWilson95Lower | Should BeGreaterThan 0.69
        $result.PopulationPassRateWilson95Lower | Should BeLessThan 0.71
        $result.PopulationPassRateWilson95Upper | Should BeGreaterThan 0.96
        $result.PopulationPassRateWilson95Upper | Should BeLessThan 0.98
        $result.CampaignStrataComplete | Should Be $false
        $result.PreregisteredCampaignPassRateMet | Should Be $false
    }

    It 'does not promote supplied-log screening to population reliability without preregistration' {
        $log = Join-Path $TestDrive 'screening-only.log'
        New-FastRunLog $log
        $result = & $scriptPath -LogPath $log
        $result.FastAlignmentEvidenceQualified | Should Be $true
        $result.CampaignManifestProvided | Should Be $false
        $result.PreregisteredCampaignPassRateMet | Should Be $false
    }

    It 'fails population reliability when one preregistered attempt has no telemetry' {
        $log = Join-Path $TestDrive 'missing-attempt.log'
        New-FastRunLog $log -RunCount 19 -PassingRunCount 18
        $manifest = Join-Path $TestDrive 'missing-attempt-campaign.json'
        $sealed = New-FastCampaignManifest $manifest @($log) 20
        $result = & $scriptPath -LogPath $log -CampaignManifestPath $manifest `
            -ExpectedCampaignManifestSha256 $sealed.Sha256
        $result.CampaignPopulationComplete | Should Be $false
        $result.PreregisteredCampaignPassRateMet | Should Be $false
        ($result.CampaignIssues -join ' ') | Should Match 'expected 20 attempts.*19 distinct run IDs'
    }

    It 'rejects omission of a preregistered log file' {
        $first = Join-Path $TestDrive 'first.log'
        $second = Join-Path $TestDrive 'second.log'
        New-FastRunLog $first -RunCount 10 -PassingRunCount 9
        New-FastRunLog $second -RunCount 10 -PassingRunCount 9
        $manifest = Join-Path $TestDrive 'two-log-campaign.json'
        $sealed = New-FastCampaignManifest $manifest @($first, $second) 20
        $result = & $scriptPath -LogPath $first -CampaignManifestPath $manifest `
            -ExpectedCampaignManifestSha256 $sealed.Sha256
        $result.CampaignPopulationComplete | Should Be $false
        $result.PreregisteredCampaignPassRateMet | Should Be $false
        ($result.CampaignIssues -join ' ') | Should Match 'does not exactly match'
    }

    It 'rejects a campaign policy changed after preregistration' {
        $log = Join-Path $TestDrive 'policy-mismatch.log'
        New-FastRunLog $log
        $manifest = Join-Path $TestDrive 'policy-mismatch-campaign.json'
        $sealed = New-FastCampaignManifest $manifest @($log)
        $result = & $scriptPath -LogPath $log -CampaignManifestPath $manifest `
            -ExpectedCampaignManifestSha256 $sealed.Sha256 -RequiredPassRate 0.9
        $result.CampaignPopulationComplete | Should Be $false
        $result.PreregisteredCampaignPassRateMet | Should Be $false
        ($result.CampaignIssues -join ' ') | Should Match 'RequiredPassRate=.*does not match analyzer policy'
    }

    It 'seals settle and move-count eligibility policy before the campaign' {
        $log = Join-Path $TestDrive 'eligibility-policy.log'
        New-FastRunLog $log -UseObjectiveStrata
        $manifest = Join-Path $TestDrive 'eligibility-policy-campaign.json'
        $sealed = New-FastCampaignManifest $manifest @($log)

        $minimumMoves = & $scriptPath -LogPath $log -CampaignManifestPath $manifest `
            -ExpectedCampaignManifestSha256 $sealed.Sha256 -MinimumMoveCount 1
        ($minimumMoves.CampaignIssues -join ' ') | Should Match 'MinimumMoveCount=.*does not match analyzer policy'

        $maximumMoves = & $scriptPath -LogPath $log -CampaignManifestPath $manifest `
            -ExpectedCampaignManifestSha256 $sealed.Sha256 -MaximumMoveCount 1
        ($maximumMoves.CampaignIssues -join ' ') | Should Match 'MaximumMoveCount=.*does not match analyzer policy'
    }

    It 'rejects a run produced by a different plugin binary than the sealed campaign' {
        $log = Join-Path $TestDrive 'binary-mismatch.log'
        New-FastRunLog $log -UseObjectiveStrata
        $manifest = Join-Path $TestDrive 'binary-mismatch-campaign.json'
        $sealed = New-FastCampaignManifest $manifest @($log)
        $document = [IO.File]::ReadAllText($manifest) | ConvertFrom-Json
        $document.PluginAssemblySha256 = '33' * 32
        [IO.File]::WriteAllText($manifest, ($document | ConvertTo-Json -Depth 6) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
        $hash = (Get-FileHash $manifest -Algorithm SHA256).Hash
        $result = & $scriptPath -LogPath $log -CampaignManifestPath $manifest -ExpectedCampaignManifestSha256 $hash
        $result.PreregisteredCampaignPassRateMet | Should Be $false
        @($result.Runs | Where-Object { $_.Issues -contains 'started event exact-build identity does not match preregistered campaign' }).Count | Should Be 20
    }
    It 'rejects a cadence authority different from the sealed campaign' {
        $log = Join-Path $TestDrive 'cadence-mismatch.log'
        New-FastRunLog $log -UseObjectiveStrata
        $manifest = Join-Path $TestDrive 'cadence-mismatch-campaign.json'
        $sealed = New-FastCampaignManifest $manifest @($log)
        $document = [IO.File]::ReadAllText($manifest) | ConvertFrom-Json
        $document.CadenceAuthoritySha256 = '99' * 32
        [IO.File]::WriteAllText($manifest, ($document | ConvertTo-Json -Depth 6) + "`r`n", [Text.UTF8Encoding]::new($false))
        $hash = (Get-FileHash $manifest -Algorithm SHA256).Hash
        $result = & $scriptPath -LogPath $log -CampaignManifestPath $manifest -ExpectedCampaignManifestSha256 $hash
        $result.PreregisteredCampaignPassRateMet | Should Be $false
        @($result.Runs | Where-Object { $_.Issues -contains 'started event exact-build identity does not match preregistered campaign' }).Count | Should Be 20
    }
    It 'rejects runtime attempts from a different preregistered campaign' {
        $log = Join-Path $TestDrive 'campaign-id-mismatch.log'
        New-FastRunLog $log -UseObjectiveStrata
        $manifest = Join-Path $TestDrive 'campaign-id-mismatch-campaign.json'
        $sealed = New-FastCampaignManifest $manifest @($log)
        $document = [IO.File]::ReadAllText($manifest) | ConvertFrom-Json
        $document.CampaignId = '87654321-4321-4cba-8fed-ba0987654321'
        [IO.File]::WriteAllText($manifest, ($document | ConvertTo-Json -Depth 6) + "`r`n", [Text.UTF8Encoding]::new($false))
        $hash = (Get-FileHash $manifest -Algorithm SHA256).Hash
        $result = & $scriptPath -LogPath $log -CampaignManifestPath $manifest -ExpectedCampaignManifestSha256 $hash
        $result.PreregisteredCampaignPassRateMet | Should Be $false
        @($result.Runs | Where-Object { $_.Issues -contains 'started event exact-build identity does not match preregistered campaign' }).Count | Should Be 20
    }

    It 'rejects a run from a different mechanical epoch than the sealed campaign' {
        $log = Join-Path $TestDrive 'epoch-mismatch.log'
        New-FastRunLog $log -UseObjectiveStrata
        $manifest = Join-Path $TestDrive 'epoch-mismatch-campaign.json'
        $sealed = New-FastCampaignManifest $manifest @($log)
        $document = [IO.File]::ReadAllText($manifest) | ConvertFrom-Json
        $document.MechanicalStateId = '55' * 32
        [IO.File]::WriteAllText($manifest, ($document | ConvertTo-Json -Depth 6) + "`r`n", [Text.UTF8Encoding]::new($false))
        $hash = (Get-FileHash $manifest -Algorithm SHA256).Hash
        $result = & $scriptPath -LogPath $log -CampaignManifestPath $manifest -ExpectedCampaignManifestSha256 $hash
        $result.PreregisteredCampaignPassRateMet | Should Be $false
        @($result.Runs | Where-Object { $_.Issues -contains 'started event exact-build identity does not match preregistered campaign' }).Count | Should Be 20
    }

    It 'rejects a legacy schema v3 manifest from the exact-build-and-epoch verdict' {
        $log = Join-Path $TestDrive 'legacy-schema.log'
        New-FastRunLog $log -UseObjectiveStrata
        $manifest = Join-Path $TestDrive 'legacy-schema-campaign.json'
        $sealed = New-FastCampaignManifest $manifest @($log)
        $document = [IO.File]::ReadAllText($manifest) | ConvertFrom-Json
        $document.SchemaVersion = 4
        [IO.File]::WriteAllText($manifest, ($document | ConvertTo-Json -Depth 6) + "`r`n", [Text.UTF8Encoding]::new($false))
        $hash = (Get-FileHash $manifest -Algorithm SHA256).Hash
        $result = & $scriptPath -LogPath $log -CampaignManifestPath $manifest -ExpectedCampaignManifestSha256 $hash
        ($result.CampaignIssues -join ' ') | Should Match 'SchemaVersion is not 5; legacy manifests cannot qualify'
    }

    It 'rejects a campaign manifest whose external hash anchor does not match' {
        $log = Join-Path $TestDrive 'hash-mismatch.log'
        New-FastRunLog $log
        $manifest = Join-Path $TestDrive 'hash-mismatch-campaign.json'
        $sealed = New-FastCampaignManifest $manifest @($log)
        $result = & $scriptPath -LogPath $log -CampaignManifestPath $manifest `
            -ExpectedCampaignManifestSha256 ('0' * 64)
        $result.PreregisteredCampaignPassRateMet | Should Be $false
        ($result.CampaignIssues -join ' ') | Should Match 'SHA-256 mismatch'
    }

    It 'rejects a backdated campaign whose creation follows its run window' {
        $log = Join-Path $TestDrive 'late-manifest.log'
        New-FastRunLog $log
        $manifest = Join-Path $TestDrive 'late-manifest-campaign.json'
        $sealed = New-FastCampaignManifest $manifest @($log)
        $document = [IO.File]::ReadAllText($manifest) | ConvertFrom-Json
        $document.CreatedUtc = '2026-08-04T00:00:00Z'
        [IO.File]::WriteAllText($manifest, ($document | ConvertTo-Json -Depth 6) + "`r`n", [Text.UTF8Encoding]::new($false))
        $hash = (Get-FileHash $manifest -Algorithm SHA256).Hash
        $result = & $scriptPath -LogPath $log -CampaignManifestPath $manifest `
            -ExpectedCampaignManifestSha256 $hash
        $result.PreregisteredCampaignPassRateMet | Should Be $false
        ($result.CampaignIssues -join ' ') | Should Match 'time window must begin at or after creation'
    }

    It 'pins MinimumEligibleRuns inside the sealed campaign policy' {
        $log = Join-Path $TestDrive 'minimum-policy.log'
        New-FastRunLog $log
        $manifest = Join-Path $TestDrive 'minimum-policy-campaign.json'
        $sealed = New-FastCampaignManifest $manifest @($log)
        $result = & $scriptPath -LogPath $log -CampaignManifestPath $manifest `
            -ExpectedCampaignManifestSha256 $sealed.Sha256 -MinimumEligibleRuns 8
        $result.PreregisteredCampaignPassRateMet | Should Be $false
        ($result.CampaignIssues -join ' ') | Should Match 'MinimumEligibleRuns=.*does not match analyzer policy'
    }

    It 'ignores telemetry outside the sealed campaign time window' {
        $log = Join-Path $TestDrive 'window.log'
        New-FastRunLog $log -UseObjectiveStrata
        $outside = [ordered]@{
            schemaVersion = 1; runId = [Guid]::NewGuid().ToString('D'); event = 'admission-rejected'
            observedUtc = '2026-08-08T18:00:00Z'; elapsedSeconds = 0.1
            stage = 'configuration'; reasonCode = 'outside-window'
        }
        Add-Content $log ("outside TPPA_FAST_RUN_EVENT " + ($outside | ConvertTo-Json -Compress))
        $manifest = Join-Path $TestDrive 'window-campaign.json'
        $sealed = New-FastCampaignManifest $manifest @($log)
        $result = & $scriptPath -LogPath $log -CampaignManifestPath $manifest `
            -ExpectedCampaignManifestSha256 $sealed.Sha256
        $result.CampaignPopulationComplete | Should Be $true
        $result.ExcludedOutOfWindowRecordCount | Should Be 1
        $result.PreregisteredCampaignPassRateMet | Should Be $true
    }

    It 'retains a structured configuration admission rejection without corrupting evidence' {
        $log = Join-Path $TestDrive 'admission-rejected.log'
        New-FastRunLog $log
        $runId = [Guid]::NewGuid().ToString('D')
        $rejected = [ordered]@{
            schemaVersion = 1; runId = $runId; event = 'admission-rejected'
            observedUtc = '2026-08-06T18:00:00Z'; elapsedSeconds = 0.1
            stage = 'configuration'; reasonCode = 'fast-runtime-configuration-ineligible'
        }
        Add-Content -LiteralPath $log -Value (
            "2026-08-06|INFO| TPPA_FAST_RUN_EVENT " + ($rejected | ConvertTo-Json -Compress))

        $result = & $scriptPath -LogPath $log

        $result.AdmissionRejectedCount | Should Be 1
        $result.InvalidEvidenceRunCount | Should Be 0
        @($result.Runs | Where-Object AdmissionRejected).Count | Should Be 1
    }

    It 'accepts a coherent three-move full-envelope run and checks the inter-move chain' {
        $log = Join-Path $TestDrive 'three-move.log'
        $runId = [Guid]::NewGuid().ToString('D')
        $start = [DateTimeOffset]::Parse('2026-08-03T18:00:00Z')
        $events = @(
            [ordered]@{
                schemaVersion = 1; runId = $runId; event = 'started'
                observedUtc = $start.ToString('O'); elapsedSeconds = 0.0
                settleSeconds = 5.0; exposureSeconds = 1.0
                alignmentToleranceMinutes = 3.0; refractionAdjustmentEnabled = $true
                repositoryHead = $repositoryHead; pluginAssemblySha256 = $pluginAssemblySha256
                covarianceAuthorityId = $covarianceAuthorityId; covarianceAuthoritySha256 = $covarianceAuthoritySha256
                cadenceAuthorityId = $cadenceAuthorityId; cadenceAuthoritySha256 = $cadenceAuthoritySha256
                qualifiedSettleSeconds = $qualifiedSettleSeconds
                qualifiedFreshDeterminationSeconds = $qualifiedFreshDeterminationSeconds
                mechanicalStateId = $mechanicalStateId
                loadProfileId = $loadProfileId; tppaCampaignId = [Guid]::NewGuid().ToString('D')
                preregisteredCampaignId = $preregisteredCampaignId
            }
            [ordered]@{
                schemaVersion = 1; runId = $runId; event = 'initial-fresh-determination'
                observedUtc = $start.AddSeconds(60).ToString('O'); elapsedSeconds = 60.0
                azimuthMinutes = 300.0; altitudeMinutes = 300.0; totalMinutes = 424.264069
            }
            [ordered]@{
                schemaVersion = 1; runId = $runId; event = 'post-move-response'
                observedUtc = $start.AddSeconds(130).ToString('O'); elapsedSeconds = 130.0
                classification = 'Improved'
                preAzimuthMinutes = 300.0; preAltitudeMinutes = 300.0; preTotalMinutes = 424.264069
                postAzimuthMinutes = 150.0; postAltitudeMinutes = 150.0; postTotalMinutes = 212.132034
                totalImprovementMinutes = 212.132035; requiredImprovementMinutes = 0.5
            }
            [ordered]@{
                schemaVersion = 1; runId = $runId; event = 'post-move-response'
                observedUtc = $start.AddSeconds(200).ToString('O'); elapsedSeconds = 200.0
                classification = 'Improved'
                preAzimuthMinutes = 150.0; preAltitudeMinutes = 150.0; preTotalMinutes = 212.132034
                postAzimuthMinutes = 24.0; postAltitudeMinutes = 18.0; postTotalMinutes = 30.0
                totalImprovementMinutes = 182.132034; requiredImprovementMinutes = 0.5
            }
            [ordered]@{
                schemaVersion = 1; runId = $runId; event = 'post-move-response'
                observedUtc = $start.AddSeconds(260).ToString('O'); elapsedSeconds = 260.0
                classification = 'ConvergedCandidate'
                preAzimuthMinutes = 24.0; preAltitudeMinutes = 18.0; preTotalMinutes = 30.0
                postAzimuthMinutes = 1.6; postAltitudeMinutes = 1.2; postTotalMinutes = 2.0
                totalImprovementMinutes = 28.0; requiredImprovementMinutes = 0.5
            }
            [ordered]@{
                schemaVersion = 1; runId = $runId; event = 'completed'
                observedUtc = $start.AddSeconds(300).ToString('O'); elapsedSeconds = 300.0
                outcome = 'fresh-confirmed-within-tolerance'; moveCount = 3
                finalAzimuthMinutes = 1.6; finalAltitudeMinutes = 1.2; finalTotalMinutes = 2.0
            }
        )
        $events | ForEach-Object {
            "2026-08-03|INFO| TPPA_FAST_RUN_EVENT $($_ | ConvertTo-Json -Compress)"
        } | Set-Content -LiteralPath $log -Encoding utf8

        $result = & $scriptPath -LogPath $log
        $run = $result.Runs | Where-Object RunId -eq $runId
        $run.MoveCount | Should Be 3
        $run.Passed | Should Be $true
        $run.Issues.Count | Should Be 0
        $result.FastAlignmentEvidenceQualified | Should Be $false
    }
    It 'fails the campaign on a completed run with refraction disabled' {
        $log = Join-Path $TestDrive 'refraction-off.log'
        New-FastRunLog $log -RefractionOffRun 4

        $result = & $scriptPath -LogPath $log

        $result.FastAlignmentEvidenceQualified | Should Be $false
        $result.FalseSuccessCount | Should Be 1
        @($result.Runs | Where-Object { $_.Issues -contains 'true-pole refraction adjustment was disabled' }).Count | Should Be 1
    }

    It 'rejects a string masquerading as a refraction boolean' {
        $log = Join-Path $TestDrive 'refraction-string.log'
        New-FastRunLog $log -RefractionStringRun 4

        $result = & $scriptPath -LogPath $log

        $result.FastAlignmentEvidenceQualified | Should Be $false
        @($result.Runs | Where-Object { ($_.Issues -join ' ') -match 'not a JSON boolean' }).Count | Should Be 1
    }

    It 'fails closed when initial evidence precedes the started event' {
        $log = Join-Path $TestDrive 'out-of-order.log'
        New-FastRunLog $log -OutOfOrderRun 5

        $result = & $scriptPath -LogPath $log

        $result.FastAlignmentEvidenceQualified | Should Be $false
        @($result.Runs | Where-Object { $_.Issues -contains 'started event is not the first run event' }).Count | Should Be 1
    }

    It 'fails closed on duplicate terminal evidence' {
        $log = Join-Path $TestDrive 'duplicate-terminal.log'
        New-FastRunLog $log -DuplicateTerminalRun 5

        $result = & $scriptPath -LogPath $log

        $result.FastAlignmentEvidenceQualified | Should Be $false
        $result.InvalidEvidenceRunCount | Should Be 1
        @($result.Runs | Where-Object { $_.Issues -contains 'expected one terminal event; found 2' }).Count | Should Be 1
    }

    It 'rejects a nonfinite final total as a false success' {
        $log = Join-Path $TestDrive 'nonfinite.log'
        New-FastRunLog $log -NonfiniteFinalRun 6

        $result = & $scriptPath -LogPath $log

        $result.FastAlignmentEvidenceQualified | Should Be $false
        $result.FalseSuccessCount | Should Be 1
        @($result.Runs | Where-Object { ($_.Issues -join ' ') -match 'not a finite number' }).Count | Should Be 1
    }

    It 'requires a ninety percent pass rate rather than unbounded retries' {
        $log = Join-Path $TestDrive 'low-pass-rate.log'
        New-FastRunLog $log -RunCount 11 -PassingRunCount 8
        $result = & $scriptPath -LogPath $log
        $result.EligibleRunCount | Should Be 11
        $result.PassingRunCount | Should Be 8
        $result.FastAlignmentEvidenceQualified | Should Be $false
    }

    It 'counts distinct successful observing nights using a noon boundary' {
        $log = Join-Path $TestDrive 'night-boundary.log'
        New-FastRunLog $log
        $text = [IO.File]::ReadAllText($log)
        $text = $text.Replace('2026-08-04T18:', '2026-08-03T20:').Replace('2026-08-05T18:', '2026-08-04T20:')
        [IO.File]::WriteAllText($log, $text)
        $result = & $scriptPath -LogPath $log
        $result.PassingNightCount | Should Be 2
        $result.FastAlignmentEvidenceQualified | Should Be $false
    }

    It 'rejects a reported runtime that disagrees with wall clock' {
        $log = Join-Path $TestDrive 'wall-clock-mismatch.log'
        New-FastRunLog $log -WallClockMismatchRun 3
        $result = & $scriptPath -LogPath $log
        $result.FastAlignmentEvidenceQualified | Should Be $false
        @($result.Runs | Where-Object { ($_.Issues -join ' ') -match 'differs from wall-clock runtime' }).Count | Should Be 1
    }

    It 'rejects a contradictory post-move and final vector' {
        $log = Join-Path $TestDrive 'post-final-mismatch.log'
        New-FastRunLog $log -PostFinalMismatchRun 3
        $result = & $scriptPath -LogPath $log
        $result.FastAlignmentEvidenceQualified | Should Be $false
        @($result.Runs | Where-Object { ($_.Issues -join ' ') -match 'post-move/final vector separation' }).Count | Should Be 1
    }

    It 'rejects a JSON string masquerading as a number' {
        $log = Join-Path $TestDrive 'numeric-string.log'
        New-FastRunLog $log -NumericStringRun 3
        $result = & $scriptPath -LogPath $log
        $result.FastAlignmentEvidenceQualified | Should Be $false
        @($result.Runs | Where-Object { ($_.Issues -join ' ') -match 'not a finite number' }).Count | Should Be 1
    }

    It 'rejects timestamps without an explicit offset' {
        $log = Join-Path $TestDrive 'offsetless.log'
        New-FastRunLog $log -OffsetlessTimestampRun 3
        $result = & $scriptPath -LogPath $log
        $result.FastAlignmentEvidenceQualified | Should Be $false
        @($result.Runs | Where-Object { ($_.Issues -join ' ') -match 'explicit offset' }).Count | Should Be 1
    }

    It 'rejects improvement scalars inconsistent with pre and post vectors' {
        $log = Join-Path $TestDrive 'improvement-mismatch.log'
        New-FastRunLog $log -ImprovementMismatchRun 3
        $result = & $scriptPath -LogPath $log
        $result.FastAlignmentEvidenceQualified | Should Be $false
        @($result.Runs | Where-Object { ($_.Issues -join ' ') -match 'reported improvement.*inconsistent' }).Count | Should Be 1
    }

    It 'rejects a post-move pre vector inconsistent with the initial vector' {
        $log = Join-Path $TestDrive 'initial-pre-mismatch.log'
        New-FastRunLog $log -PreMismatchRun 3
        $result = & $scriptPath -LogPath $log
        $result.FastAlignmentEvidenceQualified | Should Be $false
        @($result.Runs | Where-Object { ($_.Issues -join ' ') -match 'initial/post-move-pre vector separation' }).Count | Should Be 1
    }

    It 'rejects a completed run beyond the five minute runtime' {
        $log = Join-Path $TestDrive 'runtime-exceeded.log'
        New-FastRunLog $log -RuntimeExceededRun 3
        $result = & $scriptPath -LogPath $log
        $result.FastAlignmentEvidenceQualified | Should Be $false
        @($result.Runs | Where-Object { ($_.Issues -join ' ') -match 'runtime 301 s exceeds 300 s' }).Count | Should Be 1
    }

    It 'rejects a final vector above the configured alignment tolerance' {
        $log = Join-Path $TestDrive 'final-above-tolerance.log'
        New-FastRunLog $log -FinalAboveToleranceRun 3
        $result = & $scriptPath -LogPath $log
        $result.FastAlignmentEvidenceQualified | Should Be $false
        @($result.Runs | Where-Object { ($_.Issues -join ' ') -match 'final total 4 arcmin exceeds effective limit 3' }).Count | Should Be 1
    }

    It 'excludes runs outside the initial eligibility window' {
        $log = Join-Path $TestDrive 'initial-outside-range.log'
        New-FastRunLog $log -RunCount 10 -PassingRunCount 8 -InitialOutsideRangeRun 10
        $result = & $scriptPath -LogPath $log
        $result.TotalRunCount | Should Be 10
        $result.EligibleRunCount | Should Be 9
        $result.IneligibleRunCount | Should Be 1
        $result.FastAlignmentEvidenceQualified | Should Be $false
    }
}
