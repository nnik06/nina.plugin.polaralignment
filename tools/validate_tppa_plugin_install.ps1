param(
    [Parameter(Mandatory = $true)]
    [string]$PluginDirectory,
    [string]$ExpectedSha256 = ''
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath $PluginDirectory).Path
$livePath = Join-Path $root 'NINA.Plugins.PolarAlignment.dll'
if (-not (Test-Path -LiteralPath $livePath -PathType Leaf)) {
    throw "TPPA live assembly is missing: $livePath"
}
$livePath = (Get-Item -LiteralPath $livePath).FullName

$assemblies = @(Get-ChildItem -LiteralPath $root -Recurse -File |
    Where-Object {
        $_.Name -like 'NINA.Plugins.PolarAlignment*.dll' -or
        $_.Name -like '*.NINA.Plugins.PolarAlignment.dll'
    })

$duplicates = @($assemblies | Where-Object { $_.FullName -ne $livePath })
if ($duplicates.Count -gt 0) {
    $paths = ($duplicates.FullName | Sort-Object) -join [Environment]::NewLine
    throw "Duplicate TPPA assemblies exist inside the live plugin tree. Move rollback DLLs outside '$root':$([Environment]::NewLine)$paths"
}

$actualHash = (Get-FileHash -LiteralPath $livePath -Algorithm SHA256).Hash
if ($ExpectedSha256 -and $actualHash -ne $ExpectedSha256.ToUpperInvariant()) {
    throw "TPPA assembly hash mismatch. Expected $ExpectedSha256; actual $actualHash."
}

[pscustomobject]@{
    PluginDirectory = $root
    AssemblyPath = $livePath
    Sha256 = $actualHash
    AssemblyCount = $assemblies.Count
}
