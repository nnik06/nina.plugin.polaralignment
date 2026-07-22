param(
    [ValidateRange(12, 60)] [int]$DriftMinutes = 20,
    [ValidateRange(500, 10000)] [int]$ExposureMilliseconds = 1500,
    [ValidateSet(-1, 1)] [int]$Hemisphere = 1,
    [ValidateSet(-1, 1)] [int]$Mirror = 1,
    [switch]$SkipRecentAutofocus
)

$supervisor = Join-Path $PSScriptRoot 'tppa_phd2_supervisor.ps1'
if (-not (Test-Path -LiteralPath $supervisor -PathType Leaf)) {
    throw "TPPA/PHD2 supervisor not found: $supervisor"
}

# Invoke in-process so Boolean parameters retain their type across PowerShell versions.
& $supervisor `
    -Mode Phd2Drift `
    -DriftMinutes $DriftMinutes `
    -Phd2ExposureMs $ExposureMilliseconds `
    -RequireRecentAutofocus (-not $SkipRecentAutofocus.IsPresent) `
    -PdaHemisphere $Hemisphere `
    -PdaMirror $Mirror

exit $LASTEXITCODE
