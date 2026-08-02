#requires -Version 7.0
param(
    [Parameter(Mandatory = $true)]
    [string]$PluginDirectory,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string]$SourceCommit,
    [string]$PluginVersion = '',
    [string]$OutputPath = ''
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath $PluginDirectory).Path
$pluginPath = Join-Path $root 'NINA.Plugins.PolarAlignment.dll'
$corePath = Join-Path $root 'NINA.Plugins.PolarAlignment.QualificationCore.dll'
foreach ($requiredPath in @($pluginPath, $corePath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "TPPA runtime artifact is missing: $requiredPath"
    }
}

if (-not $PluginVersion) {
    try {
        $PluginVersion = [Reflection.AssemblyName]::GetAssemblyName($pluginPath).Version.ToString()
    } catch {
        throw "Unable to read the TPPA plugin version from '$pluginPath': $($_.Exception.Message)"
    }
}
if ($PluginVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "TPPA plugin version must contain four numeric components; received '$PluginVersion'."
}
if (-not $OutputPath) {
    $OutputPath = Join-Path $root 'TPPA.runtime-manifest.json'
}

$manifest = [ordered]@{
    schemaVersion = 1
    packageId = 'NINA.Plugins.PolarAlignment'
    pluginVersion = $PluginVersion
    sourceCommit = $SourceCommit.ToLowerInvariant()
    artifacts = @(
        [ordered]@{
            name = 'NINA.Plugins.PolarAlignment.dll'
            sha256 = (Get-FileHash -LiteralPath $pluginPath -Algorithm SHA256).Hash
        },
        [ordered]@{
            name = 'NINA.Plugins.PolarAlignment.QualificationCore.dll'
            sha256 = (Get-FileHash -LiteralPath $corePath -Algorithm SHA256).Hash
        }
    )
}

$json = $manifest | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText($OutputPath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
$resolvedOutput = (Get-Item -LiteralPath $OutputPath).FullName
[pscustomobject]@{
    RuntimeManifestPath = $resolvedOutput
    RuntimeManifestSha256 = (Get-FileHash -LiteralPath $resolvedOutput -Algorithm SHA256).Hash
    SourceCommit = $manifest.sourceCommit
    PluginVersion = $manifest.pluginVersion
}
