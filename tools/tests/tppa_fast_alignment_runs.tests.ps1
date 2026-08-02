$scriptPath = Join-Path $PSScriptRoot '..\analyze_tppa_fast_alignment_runs.ps1'
$scriptText = [IO.File]::ReadAllText((Resolve-Path $scriptPath))

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
    [int]$InitialOutsideRangeRun = 0) {
    $lines = [Collections.Generic.List[string]]::new()
    for ($index = 1; $index -le $RunCount; $index++) {
        $runId = [Guid]::NewGuid().ToString('D')
        $nightOffset = [Math]::Floor(($index - 1) / 7)
        $start = [DateTimeOffset]::Parse('2026-08-03T18:00:00Z').AddDays($nightOffset).AddMinutes($index)
        $refraction = if ($index -eq $RefractionStringRun) { 'false' } else { $index -ne $RefractionOffRun }
        $initialAzimuth = if ($index -eq $InitialOutsideRangeRun) { 64.0 } else { 24.0 }
        $initialAltitude = if ($index -eq $InitialOutsideRangeRun) { 48.0 } else { 18.0 }
        $initialTotal = if ($index -eq $InitialOutsideRangeRun) { 80.0 } else { 30.0 }
        $events = @(
            [ordered]@{
                schemaVersion = 1; runId = $runId; event = 'started'
                observedUtc = if ($index -eq $OffsetlessTimestampRun) { $start.ToString('yyyy-MM-ddTHH:mm:ss') } else { $start.ToString('O') }
                elapsedSeconds = 0.0
                settleSeconds = 30.0; exposureSeconds = 1.0
                alignmentToleranceMinutes = 3.0
                refractionAdjustmentEnabled = $refraction
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

Describe 'TPPA fast alignment evidence analyzer contract' {
    It 'pins the limited claim boundary' {
        $scriptText.Contains('FastAlignmentEvidenceQualified') | Should Be $true
        $scriptText.Contains('AbsoluteAccuracyQualified = $false') | Should Be $true
        $scriptText.Contains('DeliveredExposureQualified = $false') | Should Be $true
        $scriptText.Contains('OverallGoalQualified = $false') | Should Be $true
    }

    It 'qualifies eighteen of twenty eligible one-move runs across three Dubai nights' {
        $log = Join-Path $TestDrive 'qualified.log'
        New-FastRunLog $log

        $result = & $scriptPath -LogPath $log

        $result.EligibleRunCount | Should Be 20
        $result.PassingRunCount | Should Be 18
        $result.PassingNightCount | Should Be 3
        $result.FalseSuccessCount | Should Be 0
        $result.InvalidEvidenceRunCount | Should Be 0
        $result.FastAlignmentEvidenceQualified | Should Be $true
        $result.AbsoluteAccuracyQualified | Should Be $false
        $result.DeliveredExposureQualified | Should Be $false
        $result.OverallGoalQualified | Should Be $false
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
        New-FastRunLog $log -RunCount 21 -PassingRunCount 18
        $result = & $scriptPath -LogPath $log
        $result.EligibleRunCount | Should Be 21
        $result.PassingRunCount | Should Be 18
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
        New-FastRunLog $log -InitialOutsideRangeRun 20
        $result = & $scriptPath -LogPath $log
        $result.TotalRunCount | Should Be 20
        $result.EligibleRunCount | Should Be 19
        $result.IneligibleRunCount | Should Be 1
        $result.FastAlignmentEvidenceQualified | Should Be $false
    }
}
