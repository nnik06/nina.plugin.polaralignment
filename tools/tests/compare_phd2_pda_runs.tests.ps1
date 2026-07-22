Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"
$script = Join-Path (Split-Path -Parent $PSScriptRoot) "compare_phd2_pda_runs.ps1"
$root = Join-Path ([IO.Path]::GetTempPath()) ("pda-repeatability-" + [guid]::NewGuid().ToString("N"))
function Write-Result([string]$Path, [double]$Magnitude, [double]$Direction, [bool]$Stable = $true) {
    @{ PdaStable=$Stable; PdaErrorArcMinutes=$Magnitude; PdaDisplayAngleDegrees=$Direction; PdaReason="test" } |
        ConvertTo-Json | Set-Content -LiteralPath $Path -Encoding utf8
}
try {
    [void][IO.Directory]::CreateDirectory($root)
    $a = Join-Path $root "a.json"; $b = Join-Path $root "b.json"; $c = Join-Path $root "c.json"; $out = Join-Path $root "out.json"
    Write-Result $a 7.797 -40.39
    Write-Result $b 6.550 -25.98
    Write-Result $c 7.2 -33.0
    & $script -ResultPaths $a,$b,$c -OutputPath $out *> $null
    if ($LASTEXITCODE -ne 2) { throw "Expected field disagreement to exit 2" }
    $failed = Get-Content -LiteralPath $out -Raw | ConvertFrom-Json
    if ($failed.Repeatable -or $failed.CorrectionAuthorized) { throw "Field disagreement must fail closed" }
    Write-Result $a 8.0 179.0
    Write-Result $b 8.1 -179.0
    Write-Result $c 8.05 180.0
    & $script -ResultPaths $a,$b,$c -OutputPath $out *> $null
    if ($LASTEXITCODE -ne 0) { throw "Expected repeatable pair to exit 0" }
    $passed = Get-Content -LiteralPath $out -Raw | ConvertFrom-Json
    if (-not $passed.Repeatable -or $passed.CorrectionAuthorized) { throw "Repeatability must pass without authorizing correction" }
    Write-Host "All compare_phd2_pda_runs tests passed."
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
