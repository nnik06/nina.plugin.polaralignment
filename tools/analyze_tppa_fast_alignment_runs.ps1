#requires -Version 7.5
param(
    [Parameter(Mandatory = $true)]
    [string[]]$LogPath,
    [string]$OutputPath = '',
    [ValidateRange(1, 100)]
    [int]$MinimumEligibleRuns = 20,
    [ValidateRange(1, 100)]
    [int]$RequiredPassingRuns = 18,
    [ValidateRange(0.01, 1.0)]
    [double]$RequiredPassRate = 0.9,
    [ValidateRange(1, 30)]
    [int]$MinimumNights = 3,
    [ValidateRange(1.0, 1800.0)]
    [double]$MaximumRuntimeSeconds = 300.0,
    [ValidateRange(0.0, 120.0)]
    [double]$MinimumSettleSeconds = 30.0,
    [ValidateRange(0.1, 60.0)]
    [double]$MaximumToleranceMinutes = 3.0,
    [ValidateRange(0.0, 180.0)]
    [double]$MinimumInitialTotalMinutes = 20.0,
    [ValidateRange(0.1, 180.0)]
    [double]$MaximumInitialTotalMinutes = 60.0,
    [ValidateRange(1, 1)]
    [int]$RequiredMoveCount = 1,
    [ValidateRange(0.0, 10.0)]
    [double]$MaximumClockSkewSeconds = 2.0,
    [ValidateRange(0.05, 10.0)]
    [double]$MaximumPostFinalSeparationMinutes = 1.5,
    [ValidateRange(0.05, 10.0)]
    [double]$MaximumInitialPreSeparationMinutes = 1.5
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$marker = 'TPPA_FAST_RUN_EVENT '
$terminalEvents = @('completed', 'failed', 'cancelled', 'abandoned')
$allowedEvents = @('started', 'initial-fresh-determination', 'post-move-response') + $terminalEvents

function ConvertTo-FiniteDouble([object]$Value, [string]$FieldName) {
    $number = 0.0
    if ($null -eq $Value -or $Value -is [string] -or $Value -is [bool] -or
        $Value -isnot [ValueType] -or
        -not [double]::TryParse(
            [string]$Value,
            [Globalization.NumberStyles]::Float,
            [Globalization.CultureInfo]::InvariantCulture,
            [ref]$number) -or
        -not [double]::IsFinite($number)) {
        throw "Field '$FieldName' is not a finite number."
    }
    return $number
}

function ConvertTo-StrictInt([object]$Value, [string]$FieldName) {
    $number = ConvertTo-FiniteDouble $Value $FieldName
    if ([Math]::Truncate($number) -ne $number -or $number -lt [int]::MinValue -or $number -gt [int]::MaxValue) {
        throw "Field '$FieldName' is not a JSON integer."
    }
    return [int]$number
}

function ConvertTo-StrictBoolean([object]$Value, [string]$FieldName) {
    if ($Value -isnot [bool]) { throw "Field '$FieldName' is not a JSON boolean." }
    return [bool]$Value
}

function Get-ValidatedVector(
    [object]$Payload,
    [string]$AzimuthField,
    [string]$AltitudeField,
    [string]$TotalField,
    [string]$Label) {
    $azimuth = ConvertTo-FiniteDouble (Get-PropertyValue $Payload $AzimuthField) "$Label.$AzimuthField"
    $altitude = ConvertTo-FiniteDouble (Get-PropertyValue $Payload $AltitudeField) "$Label.$AltitudeField"
    $total = ConvertTo-FiniteDouble (Get-PropertyValue $Payload $TotalField) "$Label.$TotalField"
    $expected = [Math]::Sqrt($azimuth * $azimuth + $altitude * $altitude)
    if ([Math]::Abs($total - $expected) -gt 0.05) {
        throw "$Label total is inconsistent with its azimuth/altitude components."
    }
    return [pscustomobject]@{ Azimuth = $azimuth; Altitude = $altitude; Total = $total }
}

function Get-PropertyValue([object]$Record, [string]$Name) {
    $property = $Record.PSObject.Properties[$Name]
    if ($null -eq $property) { throw "Required field '$Name' is missing." }
    return $property.Value
}

function ConvertTo-ObservedUtc([object]$Value) {
    if ($Value -isnot [string] -or
        [string]::IsNullOrWhiteSpace([string]$Value) -or
        [string]$Value -notmatch '(?:Z|[+-][0-9]{2}:[0-9]{2})$') {
        throw 'observedUtc must be an ISO-8601 string with an explicit offset.'
    }
    return [DateTimeOffset]::Parse(
        [string]$Value,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
}

$records = [Collections.Generic.List[object]]::new()
$sequence = 0
foreach ($requestedPath in $LogPath) {
    $resolved = (Resolve-Path -LiteralPath $requestedPath).Path
    $lineNumber = 0
    foreach ($line in [IO.File]::ReadLines($resolved)) {
        $lineNumber++
        $index = $line.IndexOf($marker, [StringComparison]::Ordinal)
        if ($index -lt 0) { continue }
        $json = $line.Substring($index + $marker.Length).Trim()
        if (-not $json.StartsWith('{')) {
            throw "Telemetry marker at $($resolved):$lineNumber has no JSON object."
        }
        try {
            $payload = $json | ConvertFrom-Json -DateKind String
        } catch {
            throw "Telemetry JSON at $($resolved):$lineNumber is invalid: $($_.Exception.GetType().Name)."
        }
        $sequence++
        $records.Add([pscustomobject]@{
            Sequence = $sequence
            Source = $resolved
            LineNumber = $lineNumber
            Payload = $payload
        })
    }
}
if ($records.Count -eq 0) { throw 'No TPPA_FAST_RUN_EVENT records were found.' }

$runGroups = $records | Group-Object { [string](Get-PropertyValue $_.Payload 'runId') }
$runResults = [Collections.Generic.List[object]]::new()
foreach ($group in $runGroups) {
    $issues = [Collections.Generic.List[string]]::new()
    $events = @($group.Group | Sort-Object Sequence)
    $runId = [Guid]::Empty
    try { $runId = [Guid]::Parse($group.Name) } catch { $issues.Add('runId is not a valid GUID') }

    $previousElapsed = -1.0
    $previousObserved = [DateTimeOffset]::MinValue
    foreach ($record in $events) {
        $payload = $record.Payload
        try {
            if ((ConvertTo-StrictInt (Get-PropertyValue $payload 'schemaVersion') 'schemaVersion') -ne 1) {
                $issues.Add('schemaVersion is not 1')
            }
            $eventName = [string](Get-PropertyValue $payload 'event')
            if ($allowedEvents -notcontains $eventName) {
                $issues.Add("unknown event '$eventName'")
            }
            $elapsed = ConvertTo-FiniteDouble (Get-PropertyValue $payload 'elapsedSeconds') 'elapsedSeconds'
            if ($elapsed -lt 0.0) { $issues.Add('elapsedSeconds is negative') }
            if ($elapsed -lt $previousElapsed) { $issues.Add('elapsedSeconds is not monotonic') }
            $previousElapsed = $elapsed
            $observed = ConvertTo-ObservedUtc (Get-PropertyValue $payload 'observedUtc')
            if ($observed -lt $previousObserved) { $issues.Add('observedUtc is not monotonic') }
            $previousObserved = $observed
        } catch {
            $issues.Add($_.Exception.Message)
        }
    }

    $started = @($events | Where-Object { [string]$_.Payload.event -eq 'started' })
    $initial = @($events | Where-Object { [string]$_.Payload.event -eq 'initial-fresh-determination' })
    $postMove = @($events | Where-Object { [string]$_.Payload.event -eq 'post-move-response' })
    $terminal = @($events | Where-Object { $terminalEvents -contains [string]$_.Payload.event })
    if ($started.Count -ne 1) { $issues.Add("expected one started event; found $($started.Count)") }
    if ($initial.Count -ne 1) { $issues.Add("expected one initial event; found $($initial.Count)") }
    if ($terminal.Count -ne 1) { $issues.Add("expected one terminal event; found $($terminal.Count)") }
    if ($started.Count -eq 1 -and $started[0].Sequence -ne $events[0].Sequence) {
        $issues.Add('started event is not the first run event')
    }
    if ($started.Count -eq 1 -and $initial.Count -eq 1 -and
        $initial[0].Sequence -le $started[0].Sequence) {
        $issues.Add('initial event does not follow started')
    }
    if ($terminal.Count -eq 1 -and $terminal[0].Sequence -ne $events[-1].Sequence) {
        $issues.Add('terminal event is not the last run event')
    }
    foreach ($post in $postMove) {
        if ($initial.Count -eq 1 -and $post.Sequence -le $initial[0].Sequence) {
            $issues.Add('post-move event does not follow initial')
        }
        if ($terminal.Count -eq 1 -and $post.Sequence -ge $terminal[0].Sequence) {
            $issues.Add('post-move event does not precede terminal')
        }
        try {
            $preVector = Get-ValidatedVector $post.Payload 'preAzimuthMinutes' 'preAltitudeMinutes' 'preTotalMinutes' 'post-move.pre'
            $postVector = Get-ValidatedVector $post.Payload 'postAzimuthMinutes' 'postAltitudeMinutes' 'postTotalMinutes' 'post-move.post'
            $reportedImprovement = ConvertTo-FiniteDouble (Get-PropertyValue $post.Payload 'totalImprovementMinutes') 'post-move.totalImprovementMinutes'
            $requiredImprovement = ConvertTo-FiniteDouble (Get-PropertyValue $post.Payload 'requiredImprovementMinutes') 'post-move.requiredImprovementMinutes'
            $calculatedImprovement = $preVector.Total - $postVector.Total
            if ([Math]::Abs($reportedImprovement - $calculatedImprovement) -gt 0.05) {
                $issues.Add("reported improvement $reportedImprovement arcmin is inconsistent with pre/post totals; expected $calculatedImprovement arcmin")
            }
            if ($requiredImprovement -lt 0.0) {
                $issues.Add('requiredImprovementMinutes is negative')
            } elseif ($reportedImprovement -lt $requiredImprovement) {
                $issues.Add("reported improvement $reportedImprovement arcmin is below required improvement $requiredImprovement arcmin")
            }
        } catch {
            $issues.Add($_.Exception.Message)
        }
    }

    $tolerance = $null
    $refractionEnabled = $false
    $startedUtc = $null
    if ($started.Count -eq 1) {
        try {
            $tolerance = ConvertTo-FiniteDouble (Get-PropertyValue $started[0].Payload 'alignmentToleranceMinutes') 'alignmentToleranceMinutes'
            $settleSeconds = ConvertTo-FiniteDouble (Get-PropertyValue $started[0].Payload 'settleSeconds') 'settleSeconds'
            $exposureSeconds = ConvertTo-FiniteDouble (Get-PropertyValue $started[0].Payload 'exposureSeconds') 'exposureSeconds'
            if ($settleSeconds -lt $MinimumSettleSeconds) {
                $issues.Add("settle $settleSeconds s is below qualified minimum $MinimumSettleSeconds s")
            }
            if ($exposureSeconds -le 0.0) { $issues.Add('exposureSeconds must be positive') }
            if ($tolerance -le 0.0 -or $tolerance -gt $MaximumToleranceMinutes) {
                $issues.Add("alignment tolerance $tolerance arcmin is outside (0,$MaximumToleranceMinutes]")
            }
            $refractionEnabled = ConvertTo-StrictBoolean (Get-PropertyValue $started[0].Payload 'refractionAdjustmentEnabled') 'refractionAdjustmentEnabled'
            if (-not $refractionEnabled) { $issues.Add('true-pole refraction adjustment was disabled') }
            $startedUtc = ConvertTo-ObservedUtc (Get-PropertyValue $started[0].Payload 'observedUtc')
        } catch {
            $issues.Add($_.Exception.Message)
        }
    }

    $initialTotal = $null
    $initialVector = $null
    $eligible = $false
    if ($initial.Count -eq 1) {
        try {
            $initialVector = Get-ValidatedVector $initial[0].Payload 'azimuthMinutes' 'altitudeMinutes' 'totalMinutes' 'initial'
            $initialTotal = $initialVector.Total
            $eligible = $initialTotal -ge $MinimumInitialTotalMinutes -and
                $initialTotal -le $MaximumInitialTotalMinutes
        } catch {
            $issues.Add($_.Exception.Message)
        }
    }

    $terminalName = if ($terminal.Count -eq 1) { [string]$terminal[0].Payload.event } else { $null }
    $completed = $terminalName -eq 'completed'
    $moveCount = $null
    $finalTotal = $null
    if ($completed) {
        try {
            $moveCount = ConvertTo-StrictInt (Get-PropertyValue $terminal[0].Payload 'moveCount') 'completed.moveCount'
            if ($moveCount -ne $RequiredMoveCount) {
                $issues.Add("completed moveCount=$moveCount; required $RequiredMoveCount")
            }
            if ($postMove.Count -ne $moveCount) {
                $issues.Add("post-move event count $($postMove.Count) does not match moveCount $moveCount")
            }
            if ($moveCount -eq 1 -and $postMove.Count -eq 1) {
                $classification = [string](Get-PropertyValue $postMove[0].Payload 'classification')
                if ($classification -ne 'ConvergedCandidate') {
                    $issues.Add("post-move classification '$classification' is not ConvergedCandidate")
                }
                if ($null -ne $initialVector) {
                    $preVector = Get-ValidatedVector $postMove[0].Payload 'preAzimuthMinutes' 'preAltitudeMinutes' 'preTotalMinutes' 'post-move.pre'
                    $initialPreSeparation = [Math]::Sqrt(
                        [Math]::Pow($initialVector.Azimuth - $preVector.Azimuth, 2) +
                        [Math]::Pow($initialVector.Altitude - $preVector.Altitude, 2))
                    if ($initialPreSeparation -gt $MaximumInitialPreSeparationMinutes) {
                        $issues.Add("initial/post-move-pre vector separation $initialPreSeparation arcmin exceeds $MaximumInitialPreSeparationMinutes arcmin")
                    }
                }
            }
            $finalVector = Get-ValidatedVector $terminal[0].Payload 'finalAzimuthMinutes' 'finalAltitudeMinutes' 'finalTotalMinutes' 'completed'
            $finalTotal = $finalVector.Total
            if ($moveCount -eq 1 -and $postMove.Count -eq 1) {
                $postVector = Get-ValidatedVector $postMove[0].Payload 'postAzimuthMinutes' 'postAltitudeMinutes' 'postTotalMinutes' 'post-move.post'
                $postFinalSeparation = [Math]::Sqrt(
                    [Math]::Pow($finalVector.Azimuth - $postVector.Azimuth, 2) +
                    [Math]::Pow($finalVector.Altitude - $postVector.Altitude, 2))
                if ($postFinalSeparation -gt $MaximumPostFinalSeparationMinutes) {
                    $issues.Add("post-move/final vector separation $postFinalSeparation arcmin exceeds $MaximumPostFinalSeparationMinutes arcmin")
                }
            }
            $effectiveLimit = [Math]::Min($MaximumToleranceMinutes, [double]$tolerance)
            if ($finalTotal -gt $effectiveLimit) {
                $issues.Add("final total $finalTotal arcmin exceeds effective limit $effectiveLimit")
            }
            $terminalElapsed = ConvertTo-FiniteDouble (Get-PropertyValue $terminal[0].Payload 'elapsedSeconds') 'completed.elapsedSeconds'
            if ($terminalElapsed -gt $MaximumRuntimeSeconds) {
                $issues.Add("runtime $terminalElapsed s exceeds $MaximumRuntimeSeconds s")
            }
            if ($started.Count -eq 1 -and $null -ne $startedUtc) {
                $terminalUtc = ConvertTo-ObservedUtc (Get-PropertyValue $terminal[0].Payload 'observedUtc')
                $wallElapsed = ($terminalUtc - $startedUtc).TotalSeconds
                if ([Math]::Abs($wallElapsed - $terminalElapsed) -gt $MaximumClockSkewSeconds) {
                    $issues.Add("reported runtime $terminalElapsed s differs from wall-clock runtime $wallElapsed s by more than $MaximumClockSkewSeconds s")
                }
            }
        } catch {
            $issues.Add($_.Exception.Message)
        }
    }

    $passed = $eligible -and $completed -and $issues.Count -eq 0
    $falseSuccess = $completed -and $issues.Count -gt 0
    $night = if ($null -eq $startedUtc) { $null } else {
        $startedUtc.ToOffset([TimeSpan]::FromHours(4)).AddHours(-12).Date.ToString('yyyy-MM-dd')
    }
    $runResults.Add([pscustomobject]@{
        RunId = $runId.ToString('D')
        NightDubai = $night
        Eligible = $eligible
        TerminalEvent = $terminalName
        MoveCount = $moveCount
        InitialTotalMinutes = $initialTotal
        FinalTotalMinutes = $finalTotal
        Passed = $passed
        FalseSuccess = $falseSuccess
        Issues = $issues.ToArray()
    })
}

$eligibleRuns = @($runResults | Where-Object Eligible)
$passingRuns = @($eligibleRuns | Where-Object Passed)
$falseSuccesses = @($runResults | Where-Object FalseSuccess)
$invalidEvidenceRuns = @($runResults | Where-Object { $_.Issues.Count -gt 0 })
$nightCount = @($passingRuns | Where-Object { $_.NightDubai } | Select-Object -ExpandProperty NightDubai -Unique).Count
$passRate = if ($eligibleRuns.Count -eq 0) { 0.0 } else { $passingRuns.Count / [double]$eligibleRuns.Count }
$fastEvidenceQualified =
    $eligibleRuns.Count -ge $MinimumEligibleRuns -and
    $passingRuns.Count -ge $RequiredPassingRuns -and
    $passRate -ge $RequiredPassRate -and
    $nightCount -ge $MinimumNights -and
    $falseSuccesses.Count -eq 0 -and
    $invalidEvidenceRuns.Count -eq 0

$report = [ordered]@{
    SchemaVersion = 1
    GeneratedUtc = [DateTime]::UtcNow.ToString('O')
    MinimumEligibleRuns = $MinimumEligibleRuns
    RequiredPassingRuns = $RequiredPassingRuns
    RequiredPassRate = $RequiredPassRate
    MinimumNights = $MinimumNights
    MaximumRuntimeSeconds = $MaximumRuntimeSeconds
    MinimumSettleSeconds = $MinimumSettleSeconds
    MaximumToleranceMinutes = $MaximumToleranceMinutes
    InitialEligibilityRangeMinutes = @($MinimumInitialTotalMinutes, $MaximumInitialTotalMinutes)
    RequiredMoveCount = $RequiredMoveCount
    MaximumClockSkewSeconds = $MaximumClockSkewSeconds
    MaximumPostFinalSeparationMinutes = $MaximumPostFinalSeparationMinutes
    MaximumInitialPreSeparationMinutes = $MaximumInitialPreSeparationMinutes
    TotalRunCount = $runResults.Count
    EligibleRunCount = $eligibleRuns.Count
    IneligibleRunCount = $runResults.Count - $eligibleRuns.Count
    PassingRunCount = $passingRuns.Count
    PassRate = $passRate
    FalseSuccessCount = $falseSuccesses.Count
    InvalidEvidenceRunCount = $invalidEvidenceRuns.Count
    PassingNightCount = $nightCount
    FastAlignmentEvidenceQualified = $fastEvidenceQualified
    AbsoluteAccuracyQualified = $false
    DeliveredExposureQualified = $false
    OverallGoalQualified = $false
    ScopeNote = 'This verifier qualifies self-reported, logged run lifecycle, timing, true-pole settings and reported convergence only. Eligibility is based on self-reported initial error, and unlogged attempts are undetectable. It does not independently prove absolute PA accuracy, physical hard-limit clearance, delivered-image quality, or absence of selection bias.'
    Runs = $runResults.ToArray()
}

if ($OutputPath) {
    $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
    $directory = Split-Path -Parent $resolvedOutput
    if ($directory) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resolvedOutput -Encoding utf8
}
[pscustomobject]$report