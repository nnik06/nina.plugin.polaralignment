#requires -Version 7.0
param(
    [Parameter(Mandatory)] [string]$ManifestPath,
    [Parameter(Mandatory)] [string]$RepositoryRoot,
    [Parameter(Mandatory)] [string]$OutputPath,
    [DateTimeOffset]$CommissionedUtc = [DateTimeOffset]::UtcNow,
    [ValidateRange(1, 90)] [int]$ValidDays = 14,
    [ValidateRange(1.0, 100.0)] [double]$CovarianceInflation = 2.0
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

function Require-Properties($Value, [string[]]$Names, [string]$Label) {
    if ($null -eq $Value) { throw "$Label is missing." }
    $actual = @($Value.PSObject.Properties.Name | Sort-Object)
    $expected = @($Names | Sort-Object)
    if (($actual -join "`n") -ne ($expected -join "`n")) {
        throw "$Label properties do not match the frozen schema."
    }
}

function Require-Text($Value, [string]$Label) {
    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value)) {
        throw "$Label must be non-empty text."
    }
    return [string]$Value
}

function Require-LowerHex($Value, [int]$Length, [string]$Label) {
    $text = Require-Text $Value $Label
    if ($text -cnotmatch "^[0-9a-f]{$Length}$") { throw "$Label is not canonical lowercase hexadecimal." }
    return $text
}

function Require-Finite($Value, [string]$Label) {
    if ($Value -is [string] -or $null -eq $Value) { throw "$Label must be numeric." }
    $number = [double]$Value
    if ([double]::IsNaN($number) -or [double]::IsInfinity($number)) { throw "$Label must be finite." }
    return $number
}

function Sha256-File([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Normalize([double[]]$Vector, [string]$Label) {
    $norm = [math]::Sqrt($Vector[0] * $Vector[0] + $Vector[1] * $Vector[1] + $Vector[2] * $Vector[2])
    if (-not [double]::IsFinite($norm) -or $norm -le 0.0) { throw "$Label is not a finite nonzero vector." }
    return [double[]]@(($Vector[0] / $norm), ($Vector[1] / $norm), ($Vector[2] / $norm))
}

function Dot([double[]]$A, [double[]]$B) { return $A[0]*$B[0] + $A[1]*$B[1] + $A[2]*$B[2] }
function Cross([double[]]$A, [double[]]$B) {
    return [double[]]@(($A[1]*$B[2]-$A[2]*$B[1]), ($A[2]*$B[0]-$A[0]*$B[2]), ($A[0]*$B[1]-$A[1]*$B[0]))
}

$manifestFull = (Resolve-Path -LiteralPath $ManifestPath).Path
$repositoryFull = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$manifest = Get-Content -LiteralPath $manifestFull -Raw | ConvertFrom-Json
$manifestFields = @(
    'schemaVersion','repositoryHead','pluginAssemblySha256','hardwareConfigurationId',
    'mechanicalStateId','loadProfileId','catalogIdentity','targetSkyArcId',
    'temperatureC','evidenceFiles')
Require-Properties $manifest $manifestFields 'commissioning manifest'
if ($manifest.schemaVersion -isnot [long] -and $manifest.schemaVersion -isnot [int]) { throw 'schemaVersion must be an integer.' }
if ([int]$manifest.schemaVersion -ne 1) { throw 'Unsupported commissioning manifest schema.' }

$repositoryHead = Require-LowerHex $manifest.repositoryHead 40 'repositoryHead'
$actualHead = (& git -C $repositoryFull rev-parse HEAD 2>$null).Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or $actualHead -ne $repositoryHead) { throw 'Repository HEAD does not match the commissioning manifest.' }
$pluginSha = Require-LowerHex $manifest.pluginAssemblySha256 64 'pluginAssemblySha256'
$hardware = Require-LowerHex $manifest.hardwareConfigurationId 64 'hardwareConfigurationId'
$mechanical = Require-LowerHex $manifest.mechanicalStateId 64 'mechanicalStateId'
$loadProfile = Require-Text $manifest.loadProfileId 'loadProfileId'
$catalog = Require-Text $manifest.catalogIdentity 'catalogIdentity'
$skyArc = Require-Text $manifest.targetSkyArcId 'targetSkyArcId'
Require-Properties $manifest.temperatureC @('minimum','maximum') 'temperatureC'
$minimumTemperature = Require-Finite $manifest.temperatureC.minimum 'temperatureC.minimum'
$maximumTemperature = Require-Finite $manifest.temperatureC.maximum 'temperatureC.maximum'
if ($minimumTemperature -lt -100 -or $maximumTemperature -gt 100 -or $minimumTemperature -gt $maximumTemperature) {
    throw 'temperatureC range is invalid.'
}

$entries = @($manifest.evidenceFiles)
if ($entries.Count -lt 20) { throw 'At least 20 preregistered no-motion attempts are required.' }
$seenPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$seenHashes = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$seenRuns = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$seenSources = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$representatives = [Collections.Generic.List[double[]]]::new()
$solverIdentity = $null
$manifestDirectory = Split-Path -Parent $manifestFull

foreach ($entry in $entries) {
    Require-Properties $entry @('path','sha256') 'evidence file entry'
    $entryPath = Require-Text $entry.path 'evidence file path'
    $expectedSha = Require-LowerHex $entry.sha256 64 'evidence file sha256'
    $candidate = if ([IO.Path]::IsPathRooted($entryPath)) { $entryPath } else { Join-Path $manifestDirectory $entryPath }
    $fullPath = (Resolve-Path -LiteralPath $candidate).Path
    if (-not $seenPaths.Add($fullPath)) { throw "Duplicate evidence path: $fullPath" }
    if (-not $seenHashes.Add($expectedSha)) { throw "Duplicate evidence hash: $expectedSha" }
    if ((Sha256-File $fullPath) -ne $expectedSha) { throw "Evidence hash mismatch: $fullPath" }
    $run = Get-Content -LiteralPath $fullPath -Raw | ConvertFrom-Json
    if ([int]$run.schemaVersion -ne 6) { throw "Evidence schema is not 6: $fullPath" }
    $runId = Require-Text $run.runId 'runId'
    if (-not $seenRuns.Add($runId)) { throw "Duplicate runId: $runId" }
    if ((Require-LowerHex $run.pipelineDigest 64 'pipelineDigest') -ne $pluginSha) { throw 'Evidence pipeline does not match the exact plugin DLL.' }
    if ((Require-LowerHex $run.pluginAssembly.sha256 64 'pluginAssembly.sha256') -ne $pluginSha) { throw 'Evidence plugin assembly does not match the exact plugin DLL.' }
    if ((Require-LowerHex $run.hardwareConfigurationId 64 'evidence hardwareConfigurationId') -ne $hardware) { throw 'Evidence hardware configuration mismatch.' }
    if ((Require-LowerHex $run.mechanicalStateId 64 'evidence mechanicalStateId') -ne $mechanical) { throw 'Evidence mechanical state does not match the commissioned physical epoch.' }
    if ($run.refractionAdjustmentEnabled -isnot [bool] -or -not $run.refractionAdjustmentEnabled) { throw 'Every source attempt must enable true-pole refraction adjustment.' }
    if ((Require-Text $run.poleTarget 'poleTarget') -ne 'trueCelestialPole') { throw 'Every source attempt must target the true celestial pole.' }
    $temperature = Require-Finite $run.atmosphereTemperatureCelsius 'atmosphereTemperatureCelsius'
    if ($temperature -lt $minimumTemperature -or $temperature -gt $maximumTemperature) { throw 'Source temperature exits the commissioned range.' }
    $currentSolver = Require-Text $run.solverIdentity 'solverIdentity'
    if ($null -eq $solverIdentity) { $solverIdentity = $currentSolver }
    if ($currentSolver -ne $solverIdentity) { throw 'Source solver identities differ.' }
    $determinations = @($run.determinations)
    if ($determinations.Count -ne 3) { throw 'Each no-motion attempt must contain exactly three determinations.' }
    $sum = [double[]]@(0.0,0.0,0.0)
    foreach ($determination in $determinations) {
        if ([int]$determination.correctionSequenceNumber -ne 0 -or
            $determination.freshSolvesUncached -isnot [bool] -or -not $determination.freshSolvesUncached -or
            $determination.geometryQualified -isnot [bool] -or -not $determination.geometryQualified -or
            $determination.minimumArcSpanQualified -isnot [bool] -or -not $determination.minimumArcSpanQualified -or
            $determination.closureQualified -isnot [bool] -or -not $determination.closureQualified) {
            throw 'A source determination is not qualified stationary no-motion evidence.'
        }
        foreach ($digest in @($determination.sourceVectorDigests)) {
            $source = Require-LowerHex $digest 64 'sourceVectorDigest'
            if (-not $seenSources.Add($source)) { throw "Reused source solve digest: $source" }
        }
        $v = Normalize ([double[]]@(
            (Require-Finite $determination.mountAxisVector.x 'mountAxisVector.x'),
            (Require-Finite $determination.mountAxisVector.y 'mountAxisVector.y'),
            (Require-Finite $determination.mountAxisVector.z 'mountAxisVector.z'))) 'mountAxisVector'
        for ($index=0; $index -lt 3; $index++) { $sum[$index] += $v[$index] }
    }
    $representatives.Add((Normalize $sum "run representative $runId"))
}

$meanSum = [double[]]@(0.0,0.0,0.0)
foreach ($v in $representatives) { for ($index=0; $index -lt 3; $index++) { $meanSum[$index] += $v[$index] } }
$mean = Normalize $meanSum 'campaign mean axis'
$reference = if ([math]::Abs($mean[2]) -lt 0.9) { [double[]]@(0.0,0.0,1.0) } else { [double[]]@(1.0,0.0,0.0) }
$basisX = Normalize (Cross $reference $mean) 'tangent basis X'
$basisY = Normalize (Cross $mean $basisX) 'tangent basis Y'
$samples = foreach ($v in $representatives) {
    $denominator = Dot $v $mean
    [pscustomobject]@{
        X = [math]::Atan2((Dot $v $basisX), $denominator) * 180.0 / [math]::PI
        Y = [math]::Atan2((Dot $v $basisY), $denominator) * 180.0 / [math]::PI
    }
}
$meanX = ($samples | Measure-Object X -Average).Average
$meanY = ($samples | Measure-Object Y -Average).Average
$xx=$xy=$yy=0.0
foreach ($sample in $samples) { $dx=$sample.X-$meanX; $dy=$sample.Y-$meanY; $xx+=$dx*$dx; $xy+=$dx*$dy; $yy+=$dy*$dy }
$denominator = $samples.Count - 1
$xx/=$denominator; $xy/=$denominator; $yy/=$denominator
$largestEigenvalue = 0.5 * (($xx+$yy) + [math]::Sqrt(($xx-$yy)*($xx-$yy) + 4.0*$xy*$xy))
$isotropicFloor = [math]::Max(1e-12, $CovarianceInflation * $largestEigenvalue)

$commissioned = $CommissionedUtc.ToUniversalTime()
$authority = [ordered]@{
    schemaVersion = 1
    authorityId = [Guid]::NewGuid().ToString('D')
    commissionedUtc = $commissioned.ToString('yyyy-MM-ddTHH:mm:ss.fffffffZ')
    validUntilUtc = $commissioned.AddDays($ValidDays).ToString('yyyy-MM-ddTHH:mm:ss.fffffffZ')
    repositoryHead = $repositoryHead
    pluginAssemblySha256 = $pluginSha
    hardwareConfigurationId = $hardware
    mechanicalStateId = $mechanical
    loadProfileId = $loadProfile
    solverIdentity = $solverIdentity
    catalogIdentity = $catalog
    targetSkyArcId = $skyArc
    temperatureC = [ordered]@{ minimum=$minimumTemperature; maximum=$maximumTemperature }
    covarianceFloorSquareDegrees = @(@($isotropicFloor,0.0),@(0.0,$isotropicFloor))
    sharedSystematicIncluded = $false
    sourceCampaignManifestSha256 = Sha256-File $manifestFull
    sourceAttemptCount = $entries.Count
    sourcePassCount = $entries.Count
    confidenceLevel = 0.5
}
$outputFull = [IO.Path]::GetFullPath($OutputPath)
$parent = Split-Path -Parent $outputFull
if (-not [string]::IsNullOrWhiteSpace($parent)) { [IO.Directory]::CreateDirectory($parent) | Out-Null }
[IO.File]::WriteAllText($outputFull, ($authority | ConvertTo-Json -Depth 8 -Compress), [Text.UTF8Encoding]::new($false))
[pscustomobject]@{
    OutputPath = $outputFull
    ArtifactSha256 = Sha256-File $outputFull
    SourceAttemptCount = $entries.Count
    IsotropicCovarianceFloorSquareDegrees = $isotropicFloor
    SharedSystematicIncluded = $false
} | ConvertTo-Json -Depth 4
