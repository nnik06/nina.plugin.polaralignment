param(
    [Parameter(Mandatory = $true)]
    [string]$PluginDirectory,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string]$ExpectedRuntimeManifestSha256
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath $PluginDirectory).Path
$manifestPath = Join-Path $root 'TPPA.runtime-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "TPPA runtime manifest is missing: $manifestPath"
}
$manifestPath = (Get-Item -LiteralPath $manifestPath).FullName
$manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
if ($manifestHash -ne $ExpectedRuntimeManifestSha256.ToUpperInvariant()) {
    throw "TPPA runtime manifest hash mismatch. Expected $ExpectedRuntimeManifestSha256; actual $manifestHash."
}

try {
    $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
} catch {
    throw "TPPA runtime manifest is not valid JSON: $($_.Exception.Message)"
}
if ([int]$manifest.schemaVersion -ne 1 -or
    [string]$manifest.packageId -ne 'NINA.Plugins.PolarAlignment' -or
    [string]$manifest.sourceCommit -notmatch '^[0-9A-Fa-f]{40}$' -or
    [string]$manifest.pluginVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw 'TPPA runtime manifest identity or provenance fields are invalid.'
}
$artifactEntries = @($manifest.artifacts)
if ($artifactEntries.Count -ne 2) {
    throw "TPPA runtime manifest must contain exactly two artifacts; found $($artifactEntries.Count)."
}
$manifestHashes = @{}
foreach ($artifact in $artifactEntries) {
    $name = [string]$artifact.name
    $sha256 = [string]$artifact.sha256
    if ($name -notin @(
            'NINA.Plugins.PolarAlignment.dll',
            'NINA.Plugins.PolarAlignment.QualificationCore.dll') -or
        $sha256 -notmatch '^[0-9A-Fa-f]{64}$' -or
        $manifestHashes.ContainsKey($name)) {
        throw "TPPA runtime manifest contains an invalid or duplicate artifact entry: $name"
    }
    $manifestHashes[$name] = $sha256.ToUpperInvariant()
}

$livePath = Join-Path $root 'NINA.Plugins.PolarAlignment.dll'
$corePath = Join-Path $root 'NINA.Plugins.PolarAlignment.QualificationCore.dll'
foreach ($requiredPath in @($livePath, $corePath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "TPPA runtime assembly is missing: $requiredPath"
    }
}
$livePath = (Get-Item -LiteralPath $livePath).FullName
$corePath = (Get-Item -LiteralPath $corePath).FullName

$assemblies = @(Get-ChildItem -LiteralPath $root -Recurse -File |
    Where-Object {
        $_.Name -like 'NINA.Plugins.PolarAlignment*.dll' -or
        $_.Name -like '*.NINA.Plugins.PolarAlignment.dll'
    })
$expectedPaths = @($livePath, $corePath)
$duplicates = @($assemblies | Where-Object { $expectedPaths -notcontains $_.FullName })
if ($duplicates.Count -gt 0) {
    $paths = ($duplicates.FullName | Sort-Object) -join [Environment]::NewLine
    throw "Duplicate TPPA assemblies exist inside the live plugin tree. Move rollback DLLs outside '$root':$([Environment]::NewLine)$paths"
}

$actualHash = (Get-FileHash -LiteralPath $livePath -Algorithm SHA256).Hash
if ($actualHash -ne $manifestHashes['NINA.Plugins.PolarAlignment.dll']) {
    throw "TPPA assembly hash does not match the build-bound runtime manifest. Actual $actualHash."
}
$coreHash = (Get-FileHash -LiteralPath $corePath -Algorithm SHA256).Hash
if ($coreHash -ne $manifestHashes['NINA.Plugins.PolarAlignment.QualificationCore.dll']) {
    throw "TPPA qualification-core assembly hash does not match the build-bound runtime manifest. Actual $coreHash."
}

[pscustomobject]@{
    PluginDirectory = $root
    AssemblyPath = $livePath
    Sha256 = $actualHash
    QualificationCoreAssemblyPath = $corePath
    QualificationCoreSha256 = $coreHash
    RuntimeManifestPath = $manifestPath
    RuntimeManifestSha256 = $manifestHash
    SourceCommit = ([string]$manifest.sourceCommit).ToLowerInvariant()
    PluginVersion = [string]$manifest.pluginVersion
    AssemblyCount = $assemblies.Count
}
