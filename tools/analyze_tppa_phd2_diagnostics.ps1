param(
    [Parameter(Mandatory = $true)]
    [string]$RunDir,
    [string]$NinaLogPath = "",
    [double]$PixelScaleArcsecPerPixel = 0,
    [double]$MinimumStableDriftMinutes = 10,
    [double]$DriftWarmupSeconds = 60,
    [ValidateRange(0.25, 1440.0)]
    [double]$DriftWindowMinutes = 2.0,
    [ValidateRange(3, 1000)]
    [int]$MinimumConsistentDriftWindows = 3,
    [string]$OutputPath = ""
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$MinimumDriftWindowSamples = 3
$MinimumDriftWindowTimeSpanFraction = 0.75
$MaterialWindowSlopeFloorUnitsPerMin = 0.02
$WindowSlopeRangeFloorUnitsPerMin = 0.1
$WindowSlopeRangeMeanFraction = 0.75
$WindowSlopeStdDevFloorUnitsPerMin = 0.05
$WindowSlopeStdDevMeanFraction = 0.35

function Get-LatestNinaLogPath {
    $logDir = Join-Path $env:LOCALAPPDATA "NINA\Logs"
    Get-ChildItem -LiteralPath $logDir -File -Filter "*.log" -ErrorAction Stop |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

function Try-ParseDateTime {
    param([string]$Text)
    $dt = [datetime]::MinValue
    if ([datetime]::TryParse($Text, [ref]$dt)) { return $dt }
    return $null
}

function Convert-ToDouble {
    param($Value)
    if ($null -eq $Value) { return [double]::NaN }
    $text = ([string]$Value).Trim()
    if ($text.Length -eq 0) { return [double]::NaN }
    $number = 0.0
    if ([double]::TryParse($text, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$number)) {
        return $number
    }
    if ([double]::TryParse($text, [ref]$number)) { return $number }
    return [double]::NaN
}

function Get-LinearSlopePerMinute {
    param([double[]]$Minutes, [double[]]$Values)
    $n = [Math]::Min($Minutes.Count, $Values.Count)
    if ($n -lt 3) { return $null }

    $sumX = 0.0
    $sumY = 0.0
    $sumXY = 0.0
    $sumX2 = 0.0
    $used = 0
    for ($i = 0; $i -lt $n; $i++) {
        $x = $Minutes[$i]
        $y = $Values[$i]
        if ([double]::IsNaN($x) -or [double]::IsNaN($y)) { continue }
        $sumX += $x
        $sumY += $y
        $sumXY += $x * $y
        $sumX2 += $x * $x
        $used += 1
    }

    if ($used -lt 3) { return $null }
    $denom = $used * $sumX2 - $sumX * $sumX
    if ([Math]::Abs($denom) -lt 1e-12) { return $null }
    return ($used * $sumXY - $sumX * $sumY) / $denom
}


function Convert-AngleTextToArcseconds {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    $matches = [regex]::Matches($Text, '\d+(?:\.\d+)?')
    if ($matches.Count -lt 3) { return $null }
    $degrees = Convert-ToDouble $matches[0].Value
    $minutes = Convert-ToDouble $matches[1].Value
    $seconds = Convert-ToDouble $matches[2].Value
    if ([double]::IsNaN($degrees) -or [double]::IsNaN($minutes) -or [double]::IsNaN($seconds)) { return $null }
    $sign = if ($Text.TrimStart().StartsWith('-')) { -1.0 } else { 1.0 }
    return $sign * ($degrees * 3600.0 + $minutes * 60.0 + $seconds)
}

function Get-SampleStandardDeviation {
    param([double[]]$Values)
    if ($Values.Count -lt 2) { return $null }
    $mean = ($Values | Measure-Object -Average).Average
    $sumSquares = 0.0
    foreach ($value in $Values) { $sumSquares += ($value - $mean) * ($value - $mean) }
    return [Math]::Sqrt($sumSquares / ($Values.Count - 1))
}

function Get-FixedWindowSlopesPerMinute {
    param(
        [double[]]$Minutes,
        [double[]]$Values,
        [double]$WindowStartMinutes,
        [double]$WindowMinutes,
        [int]$MinimumSamples,
        [double]$MinimumTimeSpanFraction
    )
    $results = New-Object System.Collections.Generic.List[double]
    $n = [Math]::Min($Minutes.Count, $Values.Count)
    if ($n -lt $MinimumSamples -or $WindowMinutes -le 0) { return $results.ToArray() }

    $lastMinute = ($Minutes | Measure-Object -Maximum).Maximum
    $candidateWindowCount = [int][Math]::Ceiling(($lastMinute - $WindowStartMinutes) / $WindowMinutes)
    for ($windowIndex = 0; $windowIndex -lt $candidateWindowCount; $windowIndex++) {
        $windowStart = $WindowStartMinutes + $windowIndex * $WindowMinutes
        $windowEnd = $windowStart + $WindowMinutes
        $windowTimes = New-Object System.Collections.Generic.List[double]
        $windowValues = New-Object System.Collections.Generic.List[double]
        for ($i = 0; $i -lt $n; $i++) {
            if ($Minutes[$i] -lt $windowStart -or $Minutes[$i] -ge $windowEnd) { continue }
            [void]$windowTimes.Add($Minutes[$i])
            [void]$windowValues.Add($Values[$i])
        }

        if ($windowTimes.Count -lt $MinimumSamples) { continue }
        $sampledTimeSpan = ($windowTimes | Measure-Object -Maximum).Maximum - ($windowTimes | Measure-Object -Minimum).Minimum
        if ($sampledTimeSpan -lt $WindowMinutes * $MinimumTimeSpanFraction) { continue }
        $slope = Get-LinearSlopePerMinute -Minutes $windowTimes.ToArray() -Values $windowValues.ToArray()
        if ($null -ne $slope) { [void]$results.Add([double]$slope) }
    }

    return $results.ToArray()
}

function Read-TppaStabilityMeasurements {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    $reader = [IO.StreamReader]::new($stream)
    try { $rows = @($reader.ReadToEnd() | ConvertFrom-Csv) } finally { $reader.Dispose(); $stream.Dispose() }
    $pattern = 'TPPA fresh 3-point calculated error: Az: (?<az>.*?), Alt: (?<alt>.*?), Tot: (?<tot>.*)$'
    $results = New-Object System.Collections.Generic.List[object]
    $seenFreshLines = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($row in $rows) {
        $freshLine = [string]$row.fresh_error_log_line
        if (-not $seenFreshLines.Add($freshLine)) { continue }
        $m = [regex]::Match($freshLine, $pattern)
        $captured = Try-ParseDateTime ([string]$row.captured_local)
        if (-not $m.Success -or $null -eq $captured) { continue }
        $azArcsec = Convert-AngleTextToArcseconds $m.Groups['az'].Value
        $altArcsec = Convert-AngleTextToArcseconds $m.Groups['alt'].Value
        $totalArcsec = Convert-AngleTextToArcseconds $m.Groups['tot'].Value
        if ($null -eq $azArcsec -or $null -eq $altArcsec -or $null -eq $totalArcsec) { continue }
        [void]$results.Add([pscustomobject]@{ Time = $captured; AzArcsec = [double]$azArcsec; AltArcsec = [double]$altArcsec; TotalArcsec = [double]$totalArcsec })
    }
    return $results
}

function Get-TppaStabilitySummary {
    param([object[]]$Measurements)
    if ($Measurements.Count -eq 0) { return $null }
    $start = $Measurements[0].Time
    $elapsedMinutes = @($Measurements | ForEach-Object { ($_.Time - $start).TotalMinutes })
    $az = @($Measurements | ForEach-Object { [double]$_.AzArcsec })
    $alt = @($Measurements | ForEach-Object { [double]$_.AltArcsec })
    $total = @($Measurements | ForEach-Object { [double]$_.TotalArcsec })
    [pscustomobject]@{
        Count = $Measurements.Count; DurationMinutes = ($Measurements[-1].Time - $start).TotalMinutes
        AzMeanArcsec = ($az | Measure-Object -Average).Average; AltMeanArcsec = ($alt | Measure-Object -Average).Average; TotalMeanArcsec = ($total | Measure-Object -Average).Average
        AzStdDevArcsec = Get-SampleStandardDeviation -Values $az; AltStdDevArcsec = Get-SampleStandardDeviation -Values $alt; TotalStdDevArcsec = Get-SampleStandardDeviation -Values $total
        AzRangeArcsec = (($az | Measure-Object -Maximum).Maximum - ($az | Measure-Object -Minimum).Minimum); AltRangeArcsec = (($alt | Measure-Object -Maximum).Maximum - ($alt | Measure-Object -Minimum).Minimum); TotalRangeArcsec = (($total | Measure-Object -Maximum).Maximum - ($total | Measure-Object -Minimum).Minimum)
        AzSlopeArcsecPerHour = (Get-LinearSlopePerMinute -Minutes $elapsedMinutes -Values $az) * 60.0; AltSlopeArcsecPerHour = (Get-LinearSlopePerMinute -Minutes $elapsedMinutes -Values $alt) * 60.0; TotalSlopeArcsecPerHour = (Get-LinearSlopePerMinute -Minutes $elapsedMinutes -Values $total) * 60.0
    }
}
function Analyze-GuideCsv {
    param([string]$Path)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    $reader = [IO.StreamReader]::new($stream)
    try {
        $rows = @($reader.ReadToEnd() | ConvertFrom-Csv)
    } finally {
        $reader.Dispose()
        $stream.Dispose()
    }
    if ($rows.Count -lt 3) {
        return [pscustomobject]@{
            File = [IO.Path]::GetFileName($Path)
            Label = [IO.Path]::GetFileNameWithoutExtension($Path).Replace("-phd2-guidesteps", "")
            StartUtc = $null
            EndUtc = $null
            Rows = $rows.Count
            DurationMin = 0
            DecSlopeUnitsPerMin = $null
            RaSlopeUnitsPerMin = $null
            DecSlopeArcsecPerMin = $null
            RaSlopeArcsecPerMin = $null
            DecSlopeFirstHalfUnitsPerMin = $null
            DecSlopeSecondHalfUnitsPerMin = $null
            DecSlopeWindowSeconds = $DriftWindowMinutes * 60.0
            DecSlopeWindowCount = 0
            DecSlopeWindowMeanUnitsPerMin = $null
            DecSlopeWindowMinUnitsPerMin = $null
            DecSlopeWindowMaxUnitsPerMin = $null
            DecSlopeWindowRangeUnitsPerMin = $null
            DecSlopeWindowStdDevUnitsPerMin = $null
            WindowSlopeCount = 0
            DecWindowSlopeMinUnitsPerMin = $null
            DecWindowSlopeMaxUnitsPerMin = $null
            DecWindowSlopeRangeUnitsPerMin = $null
            DecWindowSlopeStdDevUnitsPerMin = $null
            DecWindowSlopeMinArcsecPerMin = $null
            DecWindowSlopeMaxArcsecPerMin = $null
            DecWindowSlopeRangeArcsecPerMin = $null
            DecWindowSlopeStdDevArcsecPerMin = $null
            DriftStable = $false
            StabilityReason = "Too few guide steps"
            ComparisonEligible = $false
            ComparisonSlopeUnitsPerMin = $null
            ComparisonSlopeArcsecPerMin = $null
            Note = "Too few guide steps"
        }
    }

    $times = New-Object System.Collections.Generic.List[double]
    $ra = New-Object System.Collections.Generic.List[double]
    $dec = New-Object System.Collections.Generic.List[double]
    $startUtc = Try-ParseDateTime $rows[0].timestamp_utc
    $endUtc = Try-ParseDateTime $rows[$rows.Count - 1].timestamp_utc

    foreach ($row in $rows) {
        $elapsed = Convert-ToDouble $row.elapsed_s
        $raValue = Convert-ToDouble $row.ra_raw_px
        $decValue = Convert-ToDouble $row.dec_raw_px
        if ([double]::IsNaN($elapsed) -or [double]::IsNaN($raValue) -or [double]::IsNaN($decValue) -or $elapsed -lt $DriftWarmupSeconds) { continue }
        [void]$times.Add($elapsed / 60.0)
        [void]$ra.Add($raValue)
        [void]$dec.Add($decValue)
    }

    $raSlope = Get-LinearSlopePerMinute -Minutes $times.ToArray() -Values $ra.ToArray()
    $decSlope = Get-LinearSlopePerMinute -Minutes $times.ToArray() -Values $dec.ToArray()
    $duration = if ($startUtc -and $endUtc) { ($endUtc - $startUtc).TotalMinutes } else { 0 }
    $scale = if ($PixelScaleArcsecPerPixel -gt 0) { $PixelScaleArcsecPerPixel } else { $null }
    $firstTimes = New-Object System.Collections.Generic.List[double]
    $firstDec = New-Object System.Collections.Generic.List[double]
    $secondTimes = New-Object System.Collections.Generic.List[double]
    $secondDec = New-Object System.Collections.Generic.List[double]
    if ($times.Count -gt 0) {
        $midpoint = ($times[0] + $times[$times.Count - 1]) / 2.0
        for ($i = 0; $i -lt $times.Count; $i++) {
            if ($times[$i] -le $midpoint) {
                [void]$firstTimes.Add($times[$i])
                [void]$firstDec.Add($dec[$i])
            } else {
                [void]$secondTimes.Add($times[$i])
                [void]$secondDec.Add($dec[$i])
            }
        }
    }
    $firstDecSlope = Get-LinearSlopePerMinute -Minutes $firstTimes.ToArray() -Values $firstDec.ToArray()
    $secondDecSlope = Get-LinearSlopePerMinute -Minutes $secondTimes.ToArray() -Values $secondDec.ToArray()
    $windowSlopes = @(Get-FixedWindowSlopesPerMinute -Minutes $times.ToArray() -Values $dec.ToArray() -WindowStartMinutes ($DriftWarmupSeconds / 60.0) -WindowMinutes $DriftWindowMinutes -MinimumSamples $MinimumDriftWindowSamples -MinimumTimeSpanFraction $MinimumDriftWindowTimeSpanFraction)
    $windowSlopeCount = $windowSlopes.Count
    $windowSlopeMean = if ($windowSlopeCount -gt 0) { ($windowSlopes | Measure-Object -Average).Average } else { $null }
    $windowSlopeMin = if ($windowSlopeCount -gt 0) { ($windowSlopes | Measure-Object -Minimum).Minimum } else { $null }
    $windowSlopeMax = if ($windowSlopeCount -gt 0) { ($windowSlopes | Measure-Object -Maximum).Maximum } else { $null }
    $windowSlopeRange = if ($windowSlopeCount -gt 0) { $windowSlopeMax - $windowSlopeMin } else { $null }
    $windowSlopeStdDev = if ($windowSlopeCount -gt 1) { Get-SampleStandardDeviation -Values ([double[]]$windowSlopes) } else { $null }
    $driftStable = $false
    $stabilityReason = "DEC drift stability has not been established"
    if ($duration -lt $MinimumStableDriftMinutes) {
        $stabilityReason = "Capture is shorter than the $MinimumStableDriftMinutes minute stability minimum"
    } elseif ($windowSlopeCount -lt $MinimumConsistentDriftWindows) {
        $stabilityReason = "Only $windowSlopeCount valid $([Math]::Round($DriftWindowMinutes, 2))-minute DEC slope windows were available after warmup; at least $MinimumConsistentDriftWindows are required (each needs $MinimumDriftWindowSamples samples spanning at least $([Math]::Round($MinimumDriftWindowTimeSpanFraction * 100.0))% of the window)"
    } else {
        $hasMaterialPositiveSlope = @($windowSlopes | Where-Object { $_ -gt $MaterialWindowSlopeFloorUnitsPerMin }).Count -gt 0
        $hasMaterialNegativeSlope = @($windowSlopes | Where-Object { $_ -lt -$MaterialWindowSlopeFloorUnitsPerMin }).Count -gt 0
        $allowedRange = [Math]::Max($WindowSlopeRangeFloorUnitsPerMin, [Math]::Abs($windowSlopeMean) * $WindowSlopeRangeMeanFraction)
        $allowedStdDev = [Math]::Max($WindowSlopeStdDevFloorUnitsPerMin, [Math]::Abs($windowSlopeMean) * $WindowSlopeStdDevMeanFraction)
        if ($hasMaterialPositiveSlope -and $hasMaterialNegativeSlope) {
            $stabilityReason = "Fixed-window DEC drift slopes have both signs above the $MaterialWindowSlopeFloorUnitsPerMin units/min material-sign floor"
        } elseif ($windowSlopeRange -gt $allowedRange) {
            $stabilityReason = "Fixed-window DEC drift slope range $([Math]::Round($windowSlopeRange, 5)) exceeds the consistency limit $([Math]::Round($allowedRange, 5))"
        } elseif ($windowSlopeStdDev -gt $allowedStdDev) {
            $stabilityReason = "Fixed-window DEC drift slope standard deviation $([Math]::Round($windowSlopeStdDev, 5)) exceeds the consistency limit $([Math]::Round($allowedStdDev, 5))"
        } else {
            $driftStable = $true
            $stabilityReason = "$windowSlopeCount valid $([Math]::Round($DriftWindowMinutes, 2))-minute DEC slope windows satisfy the sign, range, and sample standard-deviation limits"
        }
    }
    $comparisonEligible = $driftStable -and $null -ne $decSlope
    $comparisonSlope = if ($comparisonEligible) { $decSlope } else { $null }

    [pscustomobject]@{
        File = [IO.Path]::GetFileName($Path)
        Label = [IO.Path]::GetFileNameWithoutExtension($Path).Replace("-phd2-guidesteps", "")
        StartUtc = $startUtc
        EndUtc = $endUtc
        Rows = $rows.Count
        DurationMin = $duration
        DecSlopeUnitsPerMin = $decSlope
        RaSlopeUnitsPerMin = $raSlope
        DecSlopeArcsecPerMin = if ($scale -and $null -ne $decSlope) { $decSlope * $scale } else { $null }
        RaSlopeArcsecPerMin = if ($scale -and $null -ne $raSlope) { $raSlope * $scale } else { $null }
        DecSlopeFirstHalfUnitsPerMin = $firstDecSlope
        DecSlopeSecondHalfUnitsPerMin = $secondDecSlope
        DecSlopeWindowSeconds = $DriftWindowMinutes * 60.0
        DecSlopeWindowCount = $windowSlopeCount
        DecSlopeWindowMeanUnitsPerMin = $windowSlopeMean
        DecSlopeWindowMinUnitsPerMin = $windowSlopeMin
        DecSlopeWindowMaxUnitsPerMin = $windowSlopeMax
        DecSlopeWindowRangeUnitsPerMin = $windowSlopeRange
        DecSlopeWindowStdDevUnitsPerMin = $windowSlopeStdDev
        WindowSlopeCount = $windowSlopeCount
        DecWindowSlopeMinUnitsPerMin = $windowSlopeMin
        DecWindowSlopeMaxUnitsPerMin = $windowSlopeMax
        DecWindowSlopeRangeUnitsPerMin = $windowSlopeRange
        DecWindowSlopeStdDevUnitsPerMin = $windowSlopeStdDev
        DecWindowSlopeMinArcsecPerMin = if ($scale -and $null -ne $windowSlopeMin) { $windowSlopeMin * $scale } else { $null }
        DecWindowSlopeMaxArcsecPerMin = if ($scale -and $null -ne $windowSlopeMax) { $windowSlopeMax * $scale } else { $null }
        DecWindowSlopeRangeArcsecPerMin = if ($scale -and $null -ne $windowSlopeRange) { $windowSlopeRange * $scale } else { $null }
        DecWindowSlopeStdDevArcsecPerMin = if ($scale -and $null -ne $windowSlopeStdDev) { $windowSlopeStdDev * $scale } else { $null }
        DriftStable = $driftStable
        StabilityReason = $stabilityReason
        ComparisonEligible = $comparisonEligible
        ComparisonSlopeUnitsPerMin = $comparisonSlope
        ComparisonSlopeArcsecPerMin = if ($scale -and $null -ne $comparisonSlope) { $comparisonSlope * $scale } else { $null }
        Note = if ($PixelScaleArcsecPerPixel -gt 0) { "Converted using supplied pixel scale" } else { "PHD2 socket distance units; pass -PixelScaleArcsecPerPixel to convert if these are pixels" }
    }
}

function Read-TppaErrors {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    $results = New-Object System.Collections.Generic.List[object]
    $pattern = '^(?<ts>\d{4}-\d{2}-\d{2}T?\s?\d{2}:\d{2}:\d{2}\.\d+).*TPPA (?<kind>(?:completion-verification )?fresh 3-point|correction-loop) calculated error: Az: (?<az>.*?), Alt: (?<alt>.*?), Tot: (?<tot>.*)$'

    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    $reader = [IO.StreamReader]::new($stream)
    try {
        while (-not $reader.EndOfStream) {
            $line = $reader.ReadLine()
            $m = [regex]::Match($line, $pattern)
            if (-not $m.Success) { continue }
            $stamp = Try-ParseDateTime $m.Groups["ts"].Value
            [void]$results.Add([pscustomobject]@{
                Time = $stamp
                Kind = $m.Groups["kind"].Value
                AzText = $m.Groups["az"].Value
                AltText = $m.Groups["alt"].Value
                TotalText = $m.Groups["tot"].Value
                Line = $line
            })
        }
    } finally {
        $reader.Dispose()
        $stream.Dispose()
    }
    return $results
}

if (-not (Test-Path -LiteralPath $RunDir)) {
    throw "Run directory not found: $RunDir"
}

if (-not $OutputPath) {
    $OutputPath = Join-Path $RunDir "tppa-phd2-diagnostic-report.md"
}

if (-not $NinaLogPath) {
    try { $NinaLogPath = Get-LatestNinaLogPath } catch { $NinaLogPath = "" }
}

$guideCsvs = @(Get-ChildItem -LiteralPath $RunDir -File -Filter "*-phd2-guidesteps.csv" -ErrorAction SilentlyContinue | Sort-Object Name)
$segments = @($guideCsvs | ForEach-Object { Analyze-GuideCsv -Path $_.FullName })
$stabilityMeasurementsPath = Join-Path $RunDir "tppa-stability-measurements.csv"
$stabilityMeasurements = @(Read-TppaStabilityMeasurements -Path $stabilityMeasurementsPath)
$stabilitySummary = Get-TppaStabilitySummary -Measurements $stabilityMeasurements
$tppaErrors = if ($NinaLogPath) { @(Read-TppaErrors -Path $NinaLogPath) } else { @() }

$runTimes = New-Object System.Collections.Generic.List[datetime]
foreach ($measurement in $stabilityMeasurements) { [void]$runTimes.Add($measurement.Time) }
foreach ($segment in $segments) {
    if ($null -ne $segment.StartUtc) { [void]$runTimes.Add($segment.StartUtc.ToLocalTime()) }
    if ($null -ne $segment.EndUtc) { [void]$runTimes.Add($segment.EndUtc.ToLocalTime()) }
}
if ($runTimes.Count -gt 0) {
    $runStart = ($runTimes | Measure-Object -Minimum).Minimum.AddMinutes(-2)
    $runEnd = ($runTimes | Measure-Object -Maximum).Maximum.AddMinutes(2)
    $tppaErrors = @($tppaErrors | Where-Object { $null -ne $_.Time -and $_.Time -ge $runStart -and $_.Time -le $runEnd })
}

$csvOut = Join-Path $RunDir "phd2-drift-summary.csv"
if ($segments.Count -gt 0) {
    $segments | Export-Csv -LiteralPath $csvOut -NoTypeInformation -Encoding UTF8
} else {
    "File,Label,StartUtc,EndUtc,Rows,DurationMin,DecSlopeUnitsPerMin,RaSlopeUnitsPerMin,DecSlopeArcsecPerMin,RaSlopeArcsecPerMin,DecSlopeFirstHalfUnitsPerMin,DecSlopeSecondHalfUnitsPerMin,DecSlopeWindowSeconds,DecSlopeWindowCount,DecSlopeWindowMeanUnitsPerMin,DecSlopeWindowMinUnitsPerMin,DecSlopeWindowMaxUnitsPerMin,DecSlopeWindowRangeUnitsPerMin,DecSlopeWindowStdDevUnitsPerMin,WindowSlopeCount,DecWindowSlopeMinUnitsPerMin,DecWindowSlopeMaxUnitsPerMin,DecWindowSlopeRangeUnitsPerMin,DecWindowSlopeStdDevUnitsPerMin,DecWindowSlopeMinArcsecPerMin,DecWindowSlopeMaxArcsecPerMin,DecWindowSlopeRangeArcsecPerMin,DecWindowSlopeStdDevArcsecPerMin,DriftStable,StabilityReason,ComparisonEligible,ComparisonSlopeUnitsPerMin,ComparisonSlopeArcsecPerMin,Note" |
        Set-Content -LiteralPath $csvOut -Encoding UTF8
}

$lines = New-Object System.Collections.Generic.List[string]
[void]$lines.Add("# TPPA / PHD2 Diagnostic Analysis")
[void]$lines.Add("")
[void]$lines.Add("Run folder: $RunDir")
if ($NinaLogPath) {
    $ninaLogDisplay = $NinaLogPath
} else {
    $ninaLogDisplay = "not found"
}
[void]$lines.Add("NINA log: $ninaLogDisplay")
[void]$lines.Add("Generated: $((Get-Date).ToString('O'))")
[void]$lines.Add("")
[void]$lines.Add("## PHD2 Drift Segments")
[void]$lines.Add("")
[void]$lines.Add("An unstable capture must not be compared numerically with TPPA. The whole-capture OLS fields remain in the CSV for diagnostics, but only captures marked comparison-eligible have a comparison slope below.")
[void]$lines.Add("")
[void]$lines.Add("| Segment | Rows | Duration min | DEC comparison slope | RA slope | Comparison eligible | Stability reason |")
[void]$lines.Add("| --- | ---: | ---: | ---: | ---: | --- | --- |")
foreach ($s in $segments) {
    $decText = if ($null -ne $s.ComparisonSlopeArcsecPerMin) { "{0:F3} arcsec/min" -f $s.ComparisonSlopeArcsecPerMin } elseif ($null -ne $s.ComparisonSlopeUnitsPerMin) { "{0:F5} units/min" -f $s.ComparisonSlopeUnitsPerMin } else { "ineligible" }
    $raText = if ($null -ne $s.RaSlopeArcsecPerMin) { "{0:F3} arcsec/min" -f $s.RaSlopeArcsecPerMin } elseif ($null -ne $s.RaSlopeUnitsPerMin) { "{0:F5} units/min" -f $s.RaSlopeUnitsPerMin } else { "n/a" }
    $eligibleText = if ($s.ComparisonEligible) { "yes" } else { "no" }
    [void]$lines.Add("| $($s.Label) | $($s.Rows) | $('{0:F2}' -f $s.DurationMin) | $decText | $raText | $eligibleText | $($s.StabilityReason) |")
}
if ($segments.Count -eq 0) {
    [void]$lines.Add("| none | 0 | 0.00 | n/a | n/a | no | No PHD2 guide-step CSV files found in the run folder |")
}

[void]$lines.Add("")
[void]$lines.Add("### Post-warmup fixed-window DEC slopes")
[void]$lines.Add("")
[void]$lines.Add("| Segment | Window | WindowSlopeCount | Min units/min | Max units/min | Range units/min | Sample SD units/min | Min arcsec/min | Max arcsec/min | Range arcsec/min | Sample SD arcsec/min |")
[void]$lines.Add("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |")
foreach ($s in $segments) {
    $windowMinutesText = "{0:F2} min" -f ($s.DecSlopeWindowSeconds / 60.0)
    $windowMinText = if ($null -ne $s.DecWindowSlopeMinUnitsPerMin) { "{0:F5}" -f $s.DecWindowSlopeMinUnitsPerMin } else { "n/a" }
    $windowMaxText = if ($null -ne $s.DecWindowSlopeMaxUnitsPerMin) { "{0:F5}" -f $s.DecWindowSlopeMaxUnitsPerMin } else { "n/a" }
    $windowRangeText = if ($null -ne $s.DecWindowSlopeRangeUnitsPerMin) { "{0:F5}" -f $s.DecWindowSlopeRangeUnitsPerMin } else { "n/a" }
    $windowSdText = if ($null -ne $s.DecWindowSlopeStdDevUnitsPerMin) { "{0:F5}" -f $s.DecWindowSlopeStdDevUnitsPerMin } else { "n/a" }
    $windowMinArcsecText = if ($null -ne $s.DecWindowSlopeMinArcsecPerMin) { "{0:F3}" -f $s.DecWindowSlopeMinArcsecPerMin } else { "n/a" }
    $windowMaxArcsecText = if ($null -ne $s.DecWindowSlopeMaxArcsecPerMin) { "{0:F3}" -f $s.DecWindowSlopeMaxArcsecPerMin } else { "n/a" }
    $windowRangeArcsecText = if ($null -ne $s.DecWindowSlopeRangeArcsecPerMin) { "{0:F3}" -f $s.DecWindowSlopeRangeArcsecPerMin } else { "n/a" }
    $windowSdArcsecText = if ($null -ne $s.DecWindowSlopeStdDevArcsecPerMin) { "{0:F3}" -f $s.DecWindowSlopeStdDevArcsecPerMin } else { "n/a" }
    [void]$lines.Add("| $($s.Label) | $windowMinutesText | $($s.WindowSlopeCount) | $windowMinText | $windowMaxText | $windowRangeText | $windowSdText | $windowMinArcsecText | $windowMaxArcsecText | $windowRangeArcsecText | $windowSdArcsecText |")
}
if ($segments.Count -eq 0) {
    [void]$lines.Add("| none | $('{0:F2}' -f $DriftWindowMinutes) min | 0 | n/a | n/a | n/a | n/a | n/a | n/a | n/a | n/a |")
}

[void]$lines.Add("")
[void]$lines.Add("CSV summary: $csvOut")
[void]$lines.Add("")
[void]$lines.Add("## Fixed-Rig TPPA Repeatability")
[void]$lines.Add("")
if ($null -eq $stabilitySummary) {
    [void]$lines.Add("No run-scoped TPPA stability measurements were found.")
} else {
    [void]$lines.Add("| Axis | Mean | Sample SD | Peak-to-peak | Linear trend |")
    [void]$lines.Add("| --- | ---: | ---: | ---: | ---: |")
    $azTrend = if ($null -eq $stabilitySummary.AzSlopeArcsecPerHour) { "n/a" } else { "{0:F1} arcsec/hour" -f $stabilitySummary.AzSlopeArcsecPerHour }
    $altTrend = if ($null -eq $stabilitySummary.AltSlopeArcsecPerHour) { "n/a" } else { "{0:F1} arcsec/hour" -f $stabilitySummary.AltSlopeArcsecPerHour }
    $totalTrend = if ($null -eq $stabilitySummary.TotalSlopeArcsecPerHour) { "n/a" } else { "{0:F1} arcsec/hour" -f $stabilitySummary.TotalSlopeArcsecPerHour }
    [void]$lines.Add("| Azimuth | $('{0:F1}' -f $stabilitySummary.AzMeanArcsec) arcsec | $('{0:F1}' -f $stabilitySummary.AzStdDevArcsec) arcsec | $('{0:F1}' -f $stabilitySummary.AzRangeArcsec) arcsec | $azTrend |")
    [void]$lines.Add("| Altitude | $('{0:F1}' -f $stabilitySummary.AltMeanArcsec) arcsec | $('{0:F1}' -f $stabilitySummary.AltStdDevArcsec) arcsec | $('{0:F1}' -f $stabilitySummary.AltRangeArcsec) arcsec | $altTrend |")
    [void]$lines.Add("| Total | $('{0:F1}' -f $stabilitySummary.TotalMeanArcsec) arcsec | $('{0:F1}' -f $stabilitySummary.TotalStdDevArcsec) arcsec | $('{0:F1}' -f $stabilitySummary.TotalRangeArcsec) arcsec | $totalTrend |")
    [void]$lines.Add("")
    [void]$lines.Add("Measurements: $($stabilitySummary.Count) over $('{0:F1}' -f $stabilitySummary.DurationMinutes) minutes.")
}
[void]$lines.Add("")
[void]$lines.Add("## TPPA Error Lines")
[void]$lines.Add("")
if (@($tppaErrors).Count -eq 0) {
    [void]$lines.Add("No TPPA fresh/correction error lines were found in the selected NINA log.")
} else {
    [void]$lines.Add("| Time | Kind | Az | Alt | Total |")
    [void]$lines.Add("| --- | --- | ---: | ---: | ---: |")
    foreach ($e in $tppaErrors) {
        [void]$lines.Add("| $($e.Time) | $($e.Kind) | $($e.AzText) | $($e.AltText) | $($e.TotalText) |")
    }
}

[void]$lines.Add("")
[void]$lines.Add("## Interpretation Notes")
[void]$lines.Add("")
[void]$lines.Add("- For Guiding Assistant style captures with guide output disabled, the raw DEC slope is the useful sanity check.")
[void]$lines.Add("- The legacy whole-segment DEC slope remains in the CSV, but it is exposed as a TPPA comparison slope only when enough fixed post-warmup windows are mutually consistent.")
[void]$lines.Add("- A valid window contains at least $MinimumDriftWindowSamples samples spanning at least $([Math]::Round($MinimumDriftWindowTimeSpanFraction * 100.0))% of its configured $DriftWindowMinutes minute duration; at least $MinimumConsistentDriftWindows valid windows are required.")
[void]$lines.Add("- Sign disagreement is material only when slopes exceed both +$MaterialWindowSlopeFloorUnitsPerMin and -$MaterialWindowSlopeFloorUnitsPerMin units/min, so insignificant near-zero sign flips do not fail stability.")
[void]$lines.Add("- Window slope range must be at most max($WindowSlopeRangeFloorUnitsPerMin units/min, $([Math]::Round($WindowSlopeRangeMeanFraction * 100.0))% of the absolute window-slope mean); sample SD must be at most max($WindowSlopeStdDevFloorUnitsPerMin units/min, $([Math]::Round($WindowSlopeStdDevMeanFraction * 100.0))% of that mean).")
[void]$lines.Add("- An unstable capture must not be compared numerically with TPPA.")
[void]$lines.Add("- If the socket distances are pixels, pass `-PixelScaleArcsecPerPixel` to convert slopes to arcsec/min.")
[void]$lines.Add("- This report does not close the UPAS backlash loop; it verifies whether TPPA's reported errors agree with independent PHD2 drift behavior.")

[IO.File]::WriteAllLines($OutputPath, $lines, [Text.UTF8Encoding]::new($false))
Write-Host "Report written: $OutputPath"
Write-Host "CSV written: $csvOut"
