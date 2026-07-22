Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$scriptPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'guarded_balcony_drift_slew.ps1'
$text = [IO.File]::ReadAllText($scriptPath)
$tokens = $null
$errors = $null
[Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$errors) | Out-Null
if ($errors.Count -gt 0) { throw "Guarded slew has parse errors: $($errors -join '; ')" }

foreach ($required in @(
    '[ValidateRange(270.0, 300.0)]',
    '[ValidateRange(25.0, 55.0)]',
    "-not `$mount.TrackingEnabled",
    '`$mount.Slewing',
    'waitForResult=true',
    '`$azimuthError -gt `$PointingToleranceDegrees',
    '`$altitudeError -gt `$PointingToleranceDegrees'
)) {
    $needle = $required.Replace('`$', '$')
    if (-not $text.Contains($needle)) { throw "Assertion failed: missing $needle" }
}

Write-Host 'All guarded balcony drift slew static tests passed.'
