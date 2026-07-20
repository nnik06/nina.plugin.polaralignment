Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$AnalyzerPath = Join-Path (Split-Path -Parent $PSScriptRoot) "analyze_tppa_phd2_diagnostics.ps1"
$TestRoot = Join-Path ([IO.Path]::GetTempPath()) ("analyze-tppa-phd2-tests-" + [guid]::NewGuid().ToString("N"))

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "Assertion failed: $Message" }
}

function Convert-TestDouble {
    param([string]$Value)
    return [double]::Parse($Value, [Globalization.CultureInfo]::InvariantCulture)
}

function Write-GuideCsv {
    param(
        [string]$Path,
        [int]$StepSeconds,
        [scriptblock]$GetDecValue
    )

    $lines = New-Object System.Collections.Generic.List[string]
    [void]$lines.Add("timestamp_utc,elapsed_s,ra_raw_px,dec_raw_px")
    $startUtc = [datetime]::Parse("2026-01-01T00:00:00Z").ToUniversalTime()
    for ($elapsedSeconds = 0; $elapsedSeconds -le 720; $elapsedSeconds += $StepSeconds) {
        $minutes = $elapsedSeconds / 60.0
        $decValue = & $GetDecValue $minutes
        $timestamp = $startUtc.AddSeconds($elapsedSeconds).ToString("O", [Globalization.CultureInfo]::InvariantCulture)
        $line = "{0},{1},{2},{3}" -f $timestamp,
            $elapsedSeconds.ToString([Globalization.CultureInfo]::InvariantCulture),
            (0.0).ToString("R", [Globalization.CultureInfo]::InvariantCulture),
            ([double]$decValue).ToString("R", [Globalization.CultureInfo]::InvariantCulture)
        [void]$lines.Add($line)
    }
    [IO.File]::WriteAllLines($Path, $lines, [Text.UTF8Encoding]::new($false))
}

try {
    [void][IO.Directory]::CreateDirectory($TestRoot)
    Write-GuideCsv -Path (Join-Path $TestRoot "linear-phd2-guidesteps.csv") -StepSeconds 10 -GetDecValue {
        param($minutes)
        0.2 * $minutes
    }
    Write-GuideCsv -Path (Join-Path $TestRoot "curved-phd2-guidesteps.csv") -StepSeconds 10 -GetDecValue {
        param($minutes)
        0.1 * $minutes + 0.04 * ($minutes - 6.0) * ($minutes - 6.0)
    }
    Write-GuideCsv -Path (Join-Path $TestRoot "accelerating-phd2-guidesteps.csv") -StepSeconds 10 -GetDecValue {
        param($minutes)
        0.05 * $minutes + 0.03 * $minutes * $minutes
    }
    Write-GuideCsv -Path (Join-Path $TestRoot "near-zero-phd2-guidesteps.csv") -StepSeconds 10 -GetDecValue {
        param($minutes)
        $phase = ($minutes - 1.0) % 4.0
        if ($phase -lt 0) { $phase += 4.0 }
        if ($phase -lt 2.0) { 0.01 * $phase } else { 0.01 * (4.0 - $phase) }
    }
    Write-GuideCsv -Path (Join-Path $TestRoot "under-sampled-phd2-guidesteps.csv") -StepSeconds 60 -GetDecValue {
        param($minutes)
        0.2 * $minutes
    }

    $reportPath = Join-Path $TestRoot "report.md"
    & $AnalyzerPath -RunDir $TestRoot -NinaLogPath (Join-Path $TestRoot "missing.log") -OutputPath $reportPath -PixelScaleArcsecPerPixel 2.0 -MinimumStableDriftMinutes 10 -DriftWarmupSeconds 60 -DriftWindowMinutes 2.0

    $summaryPath = Join-Path $TestRoot "phd2-drift-summary.csv"
    $rows = @(Import-Csv -LiteralPath $summaryPath)
    Assert-True ($rows.Count -eq 5) "all five synthetic captures should be analyzed"

    $requiredFields = @(
        "WindowSlopeCount",
        "DecWindowSlopeMinUnitsPerMin",
        "DecWindowSlopeMaxUnitsPerMin",
        "DecWindowSlopeRangeUnitsPerMin",
        "DecWindowSlopeStdDevUnitsPerMin",
        "DecWindowSlopeMinArcsecPerMin",
        "DecWindowSlopeMaxArcsecPerMin",
        "DecWindowSlopeRangeArcsecPerMin",
        "DecWindowSlopeStdDevArcsecPerMin",
        "ComparisonEligible"
    )
    foreach ($field in $requiredFields) {
        Assert-True ($rows[0].PSObject.Properties.Name -contains $field) "summary should contain $field"
    }

    $linear = $rows | Where-Object { $_.Label -eq "linear" }
    Assert-True ($linear.DriftStable -eq "True") "linear drift should be stable"
    Assert-True ($linear.ComparisonEligible -eq "True") "linear drift should be comparison-eligible"
    Assert-True ([int]$linear.WindowSlopeCount -eq 5) "linear capture should have five valid windows"
    Assert-True ([Math]::Abs((Convert-TestDouble $linear.DecWindowSlopeMinUnitsPerMin) - 0.2) -lt 1e-10) "linear minimum window slope should be 0.2 units/min"
    Assert-True ([Math]::Abs((Convert-TestDouble $linear.DecWindowSlopeMaxArcsecPerMin) - 0.4) -lt 1e-10) "pixel scale should convert window slopes to arcsec/min"

    $curved = $rows | Where-Object { $_.Label -eq "curved" }
    Assert-True (-not [string]::IsNullOrWhiteSpace($curved.DecSlopeUnitsPerMin)) "curved drift should still retain its global OLS slope"
    Assert-True ($curved.DriftStable -eq "False") "curved drift should be unstable"
    Assert-True ($curved.ComparisonEligible -eq "False") "curved drift should not be comparison-eligible"
    Assert-True ([string]::IsNullOrWhiteSpace($curved.ComparisonSlopeUnitsPerMin)) "curved drift should not expose a comparison slope"
    Assert-True ($curved.StabilityReason -like "*both signs*") "curved drift should fail the first applicable material-sign criterion"

    $accelerating = $rows | Where-Object { $_.Label -eq "accelerating" }
    Assert-True ((Convert-TestDouble $accelerating.DecWindowSlopeMinUnitsPerMin) -gt 0) "accelerating trace should keep one material sign"
    Assert-True ($accelerating.ComparisonEligible -eq "False") "same-sign curved drift should fail window dispersion"
    Assert-True ($accelerating.StabilityReason -like "*range*exceeds*") "same-sign curved drift should fail the first applicable dispersion criterion"

    $nearZero = $rows | Where-Object { $_.Label -eq "near-zero" }
    Assert-True ((Convert-TestDouble $nearZero.DecWindowSlopeMinUnitsPerMin) -lt 0) "near-zero trace should exercise negative window slopes"
    Assert-True ((Convert-TestDouble $nearZero.DecWindowSlopeMaxUnitsPerMin) -gt 0) "near-zero trace should exercise positive window slopes"
    Assert-True ($nearZero.ComparisonEligible -eq "True") "sub-floor near-zero sign flips should not fail stability"

    $underSampled = $rows | Where-Object { $_.Label -eq "under-sampled" }
    Assert-True ([int]$underSampled.WindowSlopeCount -eq 0) "windows without enough samples or time coverage should be rejected"
    Assert-True ($underSampled.ComparisonEligible -eq "False") "under-sampled capture should not be comparison-eligible"
    Assert-True ($underSampled.StabilityReason -like "Only 0 valid*") "under-sampled capture should fail the valid-window-count criterion"

    $reportText = [IO.File]::ReadAllText($reportPath)
    Assert-True ($reportText.Contains("WindowSlopeCount")) "report should print the new window statistics"
    Assert-True ($reportText.Contains("An unstable capture must not be compared numerically with TPPA.")) "report should contain the explicit comparison warning"
    Assert-True ($reportText.Contains("75%")) "report should disclose the window time-span threshold"

    $verificationLog = Join-Path $TestRoot "verification.log"
    [IO.File]::WriteAllLines($verificationLog, @(
        "2026-01-01T04:00:00.000|INFO|TPPA verification-only initial result: Az: -00 deg 10 min 32 sec, Alt: 00 deg 08 min 09 sec, Tot: 00 deg 13 min 19 sec",
        "2026-01-01T04:02:00.000|INFO|TPPA verification-only verification result: Az: -00 deg 11 min 25 sec, Alt: 00 deg 08 min 34 sec, Tot: 00 deg 14 min 17 sec"
    ), [Text.UTF8Encoding]::new($false))
    $verificationReport = Join-Path $TestRoot "verification-report.md"
    & $AnalyzerPath -RunDir $TestRoot -NinaLogPath $verificationLog -OutputPath $verificationReport -PixelScaleArcsecPerPixel 2.0
    $verificationText = [IO.File]::ReadAllText($verificationReport)
    Assert-True ($verificationText.Contains("verification-only initial result")) "report should include the VerificationOnly initial result"
    Assert-True ($verificationText.Contains("verification-only verification result")) "report should include the VerificationOnly repeat result"
    $validationFailed = $false
    try {
        & $AnalyzerPath -RunDir $TestRoot -NinaLogPath (Join-Path $TestRoot "missing.log") -OutputPath (Join-Path $TestRoot "invalid.md") -DriftWindowMinutes 0.1
    } catch {
        $validationFailed = $true
    }
    Assert-True $validationFailed "DriftWindowMinutes below 0.25 should fail parameter validation"

    Write-Host "All analyze_tppa_phd2_diagnostics tests passed."
} finally {
    if (Test-Path -LiteralPath $TestRoot) {
        Remove-Item -LiteralPath $TestRoot -Recurse -Force
    }
}
