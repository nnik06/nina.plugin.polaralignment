#requires -Version 7.0
param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,
    [string]$OutputPath = '',
    [ValidateRange(1, 20)]
    [int]$RequiredConsecutiveRuns = 5,
    [ValidateRange(30.0, 1800.0)]
    [double]$MaximumRuntimeSeconds = 300.0,
    [ValidateRange(0.1, 60.0)]
    [double]$MaximumReportedTotalMinutes = 1.0
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Resolve-EvidencePath([string]$Path, [string]$BaseDirectory) {
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $BaseDirectory $Path))
}

function Get-SingleLauncherEvent([string[]]$Lines, [string]$Text) {
    $matches = @($Lines | Where-Object { $_ -match ('^(?<time>\S+)\s+' + [regex]::Escape($Text) + '$') })
    if ($matches.Count -ne 1) {
        throw "Expected one launcher event '$Text'; found $($matches.Count)."
    }
    [DateTimeOffset]::Parse($matches[0].Split(' ', 2)[0], [Globalization.CultureInfo]::InvariantCulture)
}

function Get-JsonMarkerRecords([string[]]$Lines, [string]$Marker) {
    foreach ($line in $Lines) {
        $index = $line.IndexOf($Marker, [StringComparison]::Ordinal)
        if ($index -lt 0) { continue }
        $json = $line.Substring($index + $Marker.Length).Trim()
        if (-not $json.StartsWith('{')) { throw "Marker $Marker has no JSON object." }
        $json | ConvertFrom-Json
    }
}

function ConvertFrom-TimingFields([string]$Payload) {
    $values = @{}
    foreach ($segment in $Payload.Split(';')) {
        $pair = $segment.Trim().Split('=', 2)
        if ($pair.Count -eq 2) { $values[$pair[0]] = $pair[1] }
    }
    [pscustomobject]$values
}

function Get-TimingRecords([string[]]$Lines) {
    foreach ($line in $Lines) {
        $marker = 'TPPA_SOLVE_TIMING '
        $index = $line.IndexOf($marker, [StringComparison]::Ordinal)
        if ($index -lt 0) { continue }
        ConvertFrom-TimingFields $line.Substring($index + $marker.Length)
    }
}

$resolvedManifest = (Resolve-Path -LiteralPath $ManifestPath).Path
$manifestDirectory = Split-Path -Parent $resolvedManifest
$manifest = [IO.File]::ReadAllText($resolvedManifest) | ConvertFrom-Json
if (-not $manifest.PSObject.Properties['schemaVersion'] -or [int]$manifest.schemaVersion -ne 1) {
    throw 'TPPA operational qualification manifest schemaVersion must be 1.'
}
$runEntries = @($manifest.runs)
if ($runEntries.Count -lt $RequiredConsecutiveRuns) {
    throw "Manifest has $($runEntries.Count) runs; $RequiredConsecutiveRuns are required."
}

$seenRunIds = [Collections.Generic.HashSet[Guid]]::new()
$results = [Collections.Generic.List[object]]::new()
$previousStart = $null

foreach ($entry in $runEntries) {
    $runId = [Guid]::Parse([string]$entry.runId)
    if (-not $seenRunIds.Add($runId)) { throw "Duplicate run id in manifest: $runId" }

    $launcherPath = Resolve-EvidencePath ([string]$entry.launcherLogPath) $manifestDirectory
    $ninaPath = Resolve-EvidencePath ([string]$entry.ninaLogPath) $manifestDirectory
    if (-not (Test-Path -LiteralPath $launcherPath -PathType Leaf)) { throw "Launcher log is missing: $launcherPath" }
    if (-not (Test-Path -LiteralPath $ninaPath -PathType Leaf)) { throw "NINA log is missing: $ninaPath" }
    $launcherLines = [IO.File]::ReadAllLines($launcherPath)
    $ninaLines = [IO.File]::ReadAllLines($ninaPath)
    $issues = [Collections.Generic.List[string]]::new()

    $launcherStart = Get-SingleLauncherEvent $launcherLines 'Verification-only sequence started.'
    $launcherFinish = Get-SingleLauncherEvent $launcherLines 'Verification-only sequence completed with FINISHED status.'
    if ($launcherFinish -le $launcherStart) { $issues.Add('launcher completion does not follow start') }
    if ($null -ne $previousStart -and $launcherStart -le $previousStart) {
        $issues.Add('manifest runs are not in strictly increasing start order')
    }
    $previousStart = $launcherStart
    $launcherElapsed = ($launcherFinish - $launcherStart).TotalSeconds
    if ($launcherElapsed -gt $MaximumRuntimeSeconds) {
        $issues.Add("launcher runtime $launcherElapsed s exceeds $MaximumRuntimeSeconds s")
    }
    if ($launcherLines | Where-Object { $_ -match '\sFAIL:' }) {
        $issues.Add('launcher log contains FAIL')
    }

    $prewarmRecords = @($launcherLines | ForEach-Object {
        $marker = 'TPPA_PREWARM '
        $index = $_.IndexOf($marker, [StringComparison]::Ordinal)
        if ($index -ge 0) {
            [pscustomobject]@{
                LineTime = [DateTimeOffset]::Parse($_.Split(' ', 2)[0], [Globalization.CultureInfo]::InvariantCulture)
                Data = $_.Substring($index + $marker.Length) | ConvertFrom-Json
            }
        }
    })
    if ($prewarmRecords.Count -ne 1) {
        $issues.Add("expected one separately timed pre-warm; found $($prewarmRecords.Count)")
        $prewarmElapsed = $null
    } else {
        $prewarmElapsed = [double]$prewarmRecords[0].Data.elapsedMilliseconds / 1000.0
        if ($prewarmRecords[0].LineTime -ge $launcherStart) { $issues.Add('pre-warm was not completed before sequence start') }
        if (-not [bool]$prewarmRecords[0].Data.excludedFromVerificationRuntime) {
            $issues.Add('pre-warm does not declare separate runtime accounting')
        }
    }

    $summaries = @(Get-JsonMarkerRecords $ninaLines 'TPPA_VERIFICATION_RUN_SUMMARY ' |
        Where-Object { [Guid]::Parse([string]$_.runId) -eq $runId })
    $datasets = @(Get-JsonMarkerRecords $ninaLines 'TPPA_VERIFICATION_PARTIAL_DATASET ' |
        Where-Object { [Guid]::Parse([string]$_.runId) -eq $runId })
    $timings = @(Get-TimingRecords $ninaLines |
        Where-Object { $_.PSObject.Properties['runId'] -and [string]$_.runId -eq $runId.ToString('D') })

    if ($summaries.Count -ne 1) { $issues.Add("expected one run summary; found $($summaries.Count)") }
    if ($datasets.Count -ne 1) { $issues.Add("expected one final dataset receipt; found $($datasets.Count)") }

    $summary = if ($summaries.Count -eq 1) { $summaries[0] } else { $null }
    $dataset = if ($datasets.Count -eq 1) { $datasets[0] } else { $null }
    $expectedSamples = 9
    if ($null -ne $summary) {
        $expectedSamples = [int]$summary.expectedSampleCount
        if (-not [bool]$summary.diagnosticPassed) { $issues.Add('internal repeatability/reciprocity verdict failed') }
        if (-not [bool]$summary.refractionAdjustmentEnabled) { $issues.Add('true-pole refraction adjustment was disabled') }
        if ([bool]$summary.overdeterminedShadowModelCheck) { $issues.Add('operational run used the metrology-only five-position shadow mode') }
        if ($expectedSamples -ne 9) { $issues.Add("operational run expected $expectedSamples samples instead of 9") }
        if ([double]$summary.elapsedSeconds -gt $MaximumRuntimeSeconds) {
            $issues.Add('plugin verification runtime exceeds the operational limit')
        }
        foreach ($name in 'initialTotalMinutes','reciprocalTotalMinutes','repeatedForwardTotalMinutes') {
            if ([double]$summary.$name -gt $MaximumReportedTotalMinutes) {
                $issues.Add("$name=$($summary.$name) arcmin exceeds $MaximumReportedTotalMinutes arcmin")
            }
        }
    }
    if ($null -ne $dataset) {
        if (-not [bool]$dataset.isComplete -or -not [bool]$dataset.measurementCompleted) {
            $issues.Add('verification dataset is partial')
        }
        if ([int]$dataset.collectedSampleCount -ne $expectedSamples -or
            [int]$dataset.expectedSampleCount -ne $expectedSamples) {
            $issues.Add('verification dataset sample count disagrees with the run contract')
        }
    }
    if ($timings.Count -ne $expectedSamples) {
        $issues.Add("expected $expectedSamples correlated solve timings; found $($timings.Count)")
    }
    foreach ($timing in $timings) {
        if ([int]$timing.schemaVersion -ne 2 -or [int]$timing.attempt -ne 1 -or
            [string]$timing.captureSucceeded -ne 'true' -or [string]$timing.solveSucceeded -ne 'true') {
            $issues.Add('a solve was retried or did not succeed on its first attempt')
            break
        }
    }

    $results.Add([pscustomobject]@{
        RunId = $runId.ToString('D')
        LauncherStarted = $launcherStart.ToString('O')
        LauncherElapsedSeconds = [Math]::Round($launcherElapsed, 3)
        PrewarmElapsedSeconds = if ($null -eq $prewarmElapsed) { $null } else { [Math]::Round($prewarmElapsed, 3) }
        PluginElapsedSeconds = if ($null -eq $summary) { $null } else { [Math]::Round([double]$summary.elapsedSeconds, 3) }
        ReportedTotalsArcmin = if ($null -eq $summary) { $null } else { @(
            [double]$summary.initialTotalMinutes,
            [double]$summary.reciprocalTotalMinutes,
            [double]$summary.repeatedForwardTotalMinutes) }
        FirstAttemptSolveCount = @($timings | Where-Object {
            [int]$_.attempt -eq 1 -and [string]$_.captureSucceeded -eq 'true' -and [string]$_.solveSucceeded -eq 'true'
        }).Count
        Passed = $issues.Count -eq 0
        Issues = $issues.ToArray()
    })
}

$passingPrefix = 0
foreach ($result in $results) {
    if (-not $result.Passed) { break }
    $passingPrefix++
}
$speedQualified = $results.Count -ge $RequiredConsecutiveRuns -and $passingPrefix -ge $RequiredConsecutiveRuns
$report = [ordered]@{
    SchemaVersion = 1
    GeneratedUtc = [DateTime]::UtcNow.ToString('O')
    RequiredConsecutiveRuns = $RequiredConsecutiveRuns
    MaximumRuntimeSeconds = $MaximumRuntimeSeconds
    MaximumReportedTotalMinutes = $MaximumReportedTotalMinutes
    SpeedAndInternalConsistencyQualified = $speedQualified
    ReportedSubArcminuteQualified = $speedQualified
    AbsoluteAccuracyQualified = $false
    OverallGoalQualified = $false
    AbsoluteAccuracyIssue = 'A calibrated independent true-pole witness with a complete uncertainty budget is still required.'
    PassingConsecutivePrefix = $passingPrefix
    Runs = $results.ToArray()
}

if ($OutputPath) {
    $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
    $directory = Split-Path -Parent $resolvedOutput
    if ($directory) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resolvedOutput -Encoding utf8
}
[pscustomobject]$report
