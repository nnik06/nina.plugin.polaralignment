Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$launcherPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'run_phd2_regular_drift_capture.ps1'
$text = [IO.File]::ReadAllText($launcherPath)
$tokens = $null
$errors = $null
[Management.Automation.Language.Parser]::ParseFile($launcherPath, [ref]$tokens, [ref]$errors) | Out-Null
if ($errors.Count -gt 0) { throw "Regular-drift launcher has parse errors: $($errors -join '; ')" }

function Assert-Contains {
    param([string]$Needle, [string]$Message)
    if (-not $text.Contains($Needle)) { throw "Assertion failed: $Message" }
}

Assert-Contains '[double]$MinimumAzimuthDegrees = 270.0' 'western visibility floor must match the measured balcony boundary'
Assert-Contains '[double]$MaximumAzimuthDegrees = 300.0' 'regular drift must remain in the tested western sky sector'
Assert-Contains '[double]$MinimumAltitudeDegrees = 25.0' 'regular drift must respect the balcony altitude floor'
Assert-Contains '[double]$MaximumAltitudeDegrees = 55.0' 'regular drift must respect the balcony altitude ceiling'
Assert-Contains '-RequirePdaNearPole $false' 'ordinary drift capture must not apply the PDA near-pole preflight'
Assert-Contains 'if (-not $?)' 'launcher must map an in-process supervisor failure to a nonzero exit'
if ($text.Contains('exit $LASTEXITCODE')) { throw 'Assertion failed: launcher must not read unset LASTEXITCODE after an in-process script call' }

Write-Host 'All regular-drift launcher static tests passed.'
