param(
    [Parameter(Mandatory = $true)]
    [string]$RunDir,
    [string]$NinaLogPath = "",
    [double]$PixelScaleArcsecPerPixel = 0,
    [string]$OutputPath = ""
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

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

function Analyze-GuideCsv {
    param([string]$Path)
    $rows = @(Import-Csv -LiteralPath $Path)
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
        if ([double]::IsNaN($elapsed) -or [double]::IsNaN($raValue) -or [double]::IsNaN($decValue)) { continue }
        [void]$times.Add($elapsed / 60.0)
        [void]$ra.Add($raValue)
        [void]$dec.Add($decValue)
    }

    $raSlope = Get-LinearSlopePerMinute -Minutes $times.ToArray() -Values $ra.ToArray()
    $decSlope = Get-LinearSlopePerMinute -Minutes $times.ToArray() -Values $dec.ToArray()
    $duration = if ($startUtc -and $endUtc) { ($endUtc - $startUtc).TotalMinutes } else { 0 }
    $scale = if ($PixelScaleArcsecPerPixel -gt 0) { $PixelScaleArcsecPerPixel } else { $null }

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
        Note = if ($PixelScaleArcsecPerPixel -gt 0) { "Converted using supplied pixel scale" } else { "PHD2 socket distance units; pass -PixelScaleArcsecPerPixel to convert if these are pixels" }
    }
}

function Read-TppaErrors {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    $results = New-Object System.Collections.Generic.List[object]
    $pattern = '^(?<ts>\d{4}-\d{2}-\d{2}T?\s?\d{2}:\d{2}:\d{2}\.\d+).*TPPA (?<kind>fresh 3-point|correction-loop) calculated error: Az: (?<az>[-+0-9.,]+).*?Alt: (?<alt>[-+0-9.,]+).*?Tot: (?<tot>[-+0-9.,]+)'

    foreach ($line in [IO.File]::ReadLines($Path)) {
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
$tppaErrors = if ($NinaLogPath) { @(Read-TppaErrors -Path $NinaLogPath) } else { @() }

$csvOut = Join-Path $RunDir "phd2-drift-summary.csv"
if ($segments.Count -gt 0) {
    $segments | Export-Csv -LiteralPath $csvOut -NoTypeInformation -Encoding UTF8
} else {
    "File,Label,StartUtc,EndUtc,Rows,DurationMin,DecSlopeUnitsPerMin,RaSlopeUnitsPerMin,DecSlopeArcsecPerMin,RaSlopeArcsecPerMin,Note" |
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
[void]$lines.Add("| Segment | Rows | Duration min | DEC slope | RA slope | Note |")
[void]$lines.Add("| --- | ---: | ---: | ---: | ---: | --- |")
foreach ($s in $segments) {
    $decText = if ($null -ne $s.DecSlopeArcsecPerMin) { "{0:F3} arcsec/min" -f $s.DecSlopeArcsecPerMin } elseif ($null -ne $s.DecSlopeUnitsPerMin) { "{0:F5} units/min" -f $s.DecSlopeUnitsPerMin } else { "n/a" }
    $raText = if ($null -ne $s.RaSlopeArcsecPerMin) { "{0:F3} arcsec/min" -f $s.RaSlopeArcsecPerMin } elseif ($null -ne $s.RaSlopeUnitsPerMin) { "{0:F5} units/min" -f $s.RaSlopeUnitsPerMin } else { "n/a" }
    [void]$lines.Add("| $($s.Label) | $($s.Rows) | $('{0:F2}' -f $s.DurationMin) | $decText | $raText | $($s.Note) |")
}
if ($segments.Count -eq 0) {
    [void]$lines.Add("| none | 0 | 0.00 | n/a | n/a | No PHD2 guide-step CSV files found in the run folder |")
}

[void]$lines.Add("")
[void]$lines.Add("CSV summary: $csvOut")
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
[void]$lines.Add("- If the socket distances are pixels, pass `-PixelScaleArcsecPerPixel` to convert slopes to arcsec/min.")
[void]$lines.Add("- This report does not close the UPAS backlash loop; it verifies whether TPPA's reported errors agree with independent PHD2 drift behavior.")

[IO.File]::WriteAllLines($OutputPath, $lines, [Text.UTF8Encoding]::new($false))
Write-Host "Report written: $OutputPath"
Write-Host "CSV written: $csvOut"
