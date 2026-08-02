[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$PolicyPath,
    [Parameter(Mandatory)] [string[]]$PreControlFits,
    [Parameter(Mandatory)] [string]$LongExposureFits,
    [Parameter(Mandatory)] [string[]]$PostControlFits,
    [Parameter(Mandatory)] [string]$Phd2SummaryPath,
    [Parameter(Mandatory)] [string]$OagGeometryReceiptPath,
    [Parameter(Mandatory)] [string]$StateReceiptPath,
    [Parameter(Mandatory)] [string]$AstapPath,
    [Parameter(Mandatory)] [string]$OutputDirectory,
    [double]$Phd2CoverageMarginSeconds = 5.0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require-File([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label is missing: $Path"
    }
    return (Resolve-Path -LiteralPath $Path).Path
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-CreateNewUtf8([string]$Path, [string]$Content) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try {
        $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
        try { $writer.Write($Content) } finally { $writer.Dispose() }
    } finally { $stream.Dispose() }
}

if ($PreControlFits.Count -lt 5 -or $PostControlFits.Count -lt 5) {
    throw 'At least five independent controls are required on each side of the 900-second frame.'
}
$policyPath = Require-File $PolicyPath 'Policy'
$phd2Path = Require-File $Phd2SummaryPath 'PHD2 summary'
$geometryPath = Require-File $OagGeometryReceiptPath 'OAG geometry receipt'
$statePath = Require-File $StateReceiptPath 'State receipt'
$astap = Require-File $AstapPath 'ASTAP executable'
$longPath = Require-File $LongExposureFits 'Long-exposure FITS'
$pre = @($PreControlFits | ForEach-Object { Require-File $_ 'Pre-control FITS' })
$post = @($PostControlFits | ForEach-Object { Require-File $_ 'Post-control FITS' })

if (Test-Path -LiteralPath $OutputDirectory) {
    throw "Output directory already exists: $OutputDirectory"
}
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$artifactRoot = Join-Path $OutputDirectory 'artifacts'
[void][IO.Directory]::CreateDirectory($artifactRoot)

$policy = [IO.File]::ReadAllText($policyPath) | ConvertFrom-Json
$all = @()
for ($index = 0; $index -lt $pre.Count; $index++) {
    $all += [pscustomobject]@{ Role='PreControl'; Source=$pre[$index]; Name=('pre-{0:d2}.fits' -f ($index + 1)) }
}
$all += [pscustomobject]@{ Role='LongExposure'; Source=$longPath; Name='long-900s.fits' }
for ($index = 0; $index -lt $post.Count; $index++) {
    $all += [pscustomobject]@{ Role='PostControl'; Source=$post[$index]; Name=('post-{0:d2}.fits' -f ($index + 1)) }
}

$frames = @()
foreach ($item in $all) {
    $copy = Join-Path $artifactRoot $item.Name
    Copy-Item -LiteralPath $item.Source -Destination $copy
    $before = Get-Sha256 $copy
    & $astap -f $copy -extract2
    if ($LASTEXITCODE -ne 0) { throw "ASTAP -extract2 failed for $($item.Name)." }
    $csv = [IO.Path]::ChangeExtension($copy, '.csv')
    if (-not (Test-Path -LiteralPath $csv -PathType Leaf)) {
        throw "ASTAP did not create the expected catalog: $csv"
    }
    if ((Get-Sha256 $copy) -ne $before) {
        throw "ASTAP altered copied FITS bytes: $copy"
    }
    $frames += [ordered]@{
        Role = $item.Role
        FitsPath = $copy
        FitsSha256 = $before
        AstapCsvPath = $csv
        AstapCsvSha256 = Get-Sha256 $csv
    }
}

$campaignId = 'actual-exposure-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$manifest = [ordered]@{
    SchemaVersion = 1
    CampaignId = $campaignId
    CreatedUtc = [DateTime]::UtcNow.ToString('o')
    Policy = $policy
    PolicySourcePath = $policyPath
    PolicySourceSha256 = Get-Sha256 $policyPath
    Frames = $frames
    AstapExecutablePath = $astap
    AstapExecutableSha256 = Get-Sha256 $astap
    AstapInvocationContract = 'ASTAP command-line -f <copied-fits> -extract2; exact seven-column CSV schema validated by QualificationCore'
    Phd2SummaryPath = $phd2Path
    Phd2SummarySha256 = Get-Sha256 $phd2Path
    OagGeometryReceiptPath = $geometryPath
    OagGeometryReceiptSha256 = Get-Sha256 $geometryPath
    StateReceiptPath = $statePath
    StateReceiptSha256 = Get-Sha256 $statePath
    MinimumPhd2CoverageMarginSeconds = $Phd2CoverageMarginSeconds
}
$manifestPath = Join-Path $OutputDirectory 'manifest.json'
Write-CreateNewUtf8 $manifestPath ($manifest | ConvertTo-Json -Depth 12)

$cliProject = Join-Path $PSScriptRoot 'TppaQualificationCli\TppaQualificationCli.csproj'
$receiptPath = Join-Path $OutputDirectory 'actual-exposure-receipt.json'
& dotnet run --project $cliProject -c Release --no-build -- `
    analyze-actual-exposure --manifest $manifestPath --receipt-out $receiptPath
$exitCode = $LASTEXITCODE
if ($exitCode -notin @(0, 1)) {
    throw "Actual-exposure evidence CLI failed with exit code $exitCode."
}
& dotnet run --project $cliProject -c Release --no-build -- `
    verify-actual-exposure --manifest $manifestPath --receipt $receiptPath
$verifyExitCode = $LASTEXITCODE
if ($verifyExitCode -notin @(0, 1)) {
    throw "Actual-exposure receipt verification failed with exit code $verifyExitCode."
}
if ($verifyExitCode -ne $exitCode) {
    throw "Actual-exposure analysis and receipt verification qualification disagree."
}
exit $exitCode
