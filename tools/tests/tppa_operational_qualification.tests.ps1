$scriptPath = Join-Path $PSScriptRoot '..\analyze_tppa_operational_qualification.ps1'
$text = [IO.File]::ReadAllText((Resolve-Path $scriptPath))

function New-OperationalFixture(
    [string]$Root,
    [int]$FailRun = 0,
    [switch]$DisableRefraction) {
    $runs = @()
    for ($index = 1; $index -le 5; $index++) {
        $runId = [Guid]::NewGuid()
        $launcher = Join-Path $Root "launcher-$index.log"
        $nina = Join-Path $Root "nina-$index.log"
        $start = [DateTimeOffset]::Parse("2026-08-01T0$index`:00:10+04:00")
        $prewarm = [ordered]@{
            schemaVersion = 1
            elapsedMilliseconds = 12000
            excludedFromVerificationRuntime = $true
        } | ConvertTo-Json -Compress
        @(
            "$($start.AddSeconds(-15).ToString('O')) TPPA_PREWARM $prewarm"
            "$($start.ToString('O')) Verification-only sequence started."
            "$($start.AddSeconds(250).ToString('O')) Verification-only sequence completed with FINISHED status."
        ) | Set-Content -LiteralPath $launcher

        $summary = [ordered]@{
            schemaVersion = 1
            runId = $runId.ToString('D')
            elapsedSeconds = 245
            diagnosticPassed = $true
            repeatabilityPassed = $true
            reciprocityPassed = $true
            expectedSampleCount = 9
            refractionAdjustmentEnabled = -not ($DisableRefraction -and $index -eq 3)
            overdeterminedShadowModelCheck = $false
            initialTotalMinutes = 0.7
            reciprocalTotalMinutes = 0.8
            repeatedForwardTotalMinutes = 0.6
        } | ConvertTo-Json -Compress
        $dataset = [ordered]@{
            schemaVersion = 1
            runId = $runId.ToString('D')
            measurementCompleted = $true
            isComplete = $true
            collectedSampleCount = 9
            expectedSampleCount = 9
            samples = @()
            issues = @()
        } | ConvertTo-Json -Compress
        $lines = [Collections.Generic.List[string]]::new()
        foreach ($sample in 1..9) {
            $attempt = if ($FailRun -eq $index -and $sample -eq 4) { 2 } else { 1 }
            $lines.Add(
                "2026-08-01T01:00:00|INFO| TPPA_SOLVE_TIMING schemaVersion=2; runId=$($runId.ToString('D')); attempt=$attempt; captureMilliseconds=3000; solveMilliseconds=4000; totalMilliseconds=7000; captureSucceeded=true; solveSucceeded=true;")
        }
        $lines.Add("2026-08-01T01:04:00|INFO| TPPA_VERIFICATION_PARTIAL_DATASET $dataset")
        $lines.Add("2026-08-01T01:04:05|INFO| TPPA_VERIFICATION_RUN_SUMMARY $summary")
        $lines | Set-Content -LiteralPath $nina
        $runs += [ordered]@{
            runId = $runId.ToString('D')
            launcherLogPath = [IO.Path]::GetFileName($launcher)
            ninaLogPath = [IO.Path]::GetFileName($nina)
        }
    }
    $manifest = Join-Path $Root 'manifest.json'
    [ordered]@{ schemaVersion = 1; runs = $runs } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifest
    return $manifest
}

Describe 'TPPA operational qualification analyzer contract' {
    It 'requires run-correlated summaries, complete datasets and timing records' {
        $text.Contains('TPPA_VERIFICATION_RUN_SUMMARY') | Should Be $true
        $text.Contains('TPPA_VERIFICATION_PARTIAL_DATASET') | Should Be $true
        $text.Contains('TPPA_SOLVE_TIMING') | Should Be $true
        $text.Contains('refractionAdjustmentEnabled') | Should Be $true
        $text.Contains('AbsoluteAccuracyQualified = $false') | Should Be $true
    }

    It 'qualifies five consecutive warm first-attempt runs without claiming absolute accuracy' {
        $root = Join-Path $TestDrive 'passing'
        New-Item -ItemType Directory -Path $root | Out-Null
        $manifest = New-OperationalFixture $root

        $result = & $scriptPath -ManifestPath $manifest

        $result.SpeedAndInternalConsistencyQualified | Should Be $true
        $result.ReportedSubArcminuteQualified | Should Be $true
        $result.AbsoluteAccuracyQualified | Should Be $false
        $result.OverallGoalQualified | Should Be $false
        $result.PassingConsecutivePrefix | Should Be 5
    }

    It 'fails the consecutive gate on a retry' {
        $root = Join-Path $TestDrive 'retry'
        New-Item -ItemType Directory -Path $root | Out-Null
        $manifest = New-OperationalFixture $root -FailRun 3

        $result = & $scriptPath -ManifestPath $manifest

        $result.SpeedAndInternalConsistencyQualified | Should Be $false
        $result.PassingConsecutivePrefix | Should Be 2
        ($result.Runs[2].Issues -contains 'a solve was retried or did not succeed on its first attempt') | Should Be $true
    }

    It 'fails true-pole qualification when refraction adjustment is disabled' {
        $root = Join-Path $TestDrive 'refraction-off'
        New-Item -ItemType Directory -Path $root | Out-Null
        $manifest = New-OperationalFixture $root -DisableRefraction

        $result = & $scriptPath -ManifestPath $manifest

        $result.SpeedAndInternalConsistencyQualified | Should Be $false
        ($result.Runs[2].Issues -contains 'true-pole refraction adjustment was disabled') | Should Be $true
    }
}
