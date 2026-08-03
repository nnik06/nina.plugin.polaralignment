#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$PluginDllPath,
    [Parameter(Mandatory)][string]$QualificationCoreDllPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$SourceCommit,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$PluginVersion
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function FullFile([string]$Path, [string]$Label) {
    $full = [IO.Path]::GetFullPath($Path)
    Require ([IO.File]::Exists($full)) "$Label does not exist: $full"
    return $full
}

$repo = [IO.Path]::GetFullPath($RepositoryRoot)
Require ([IO.Directory]::Exists($repo)) "RepositoryRoot does not exist: $repo"
$plugin = FullFile $PluginDllPath 'PluginDllPath'
$core = FullFile $QualificationCoreDllPath 'QualificationCoreDllPath'
$output = [IO.Path]::GetFullPath($OutputDirectory)
Require (-not [IO.Directory]::Exists($output) -and -not [IO.File]::Exists($output)) `
    "Refusing to overwrite field packet output: $output"

$head = (& git -C $repo rev-parse HEAD).Trim()
Require ($LASTEXITCODE -eq 0 -and $head -ceq $SourceCommit.ToLowerInvariant()) `
    "Repository HEAD does not match SourceCommit: expected $($SourceCommit.ToLowerInvariant()), found $head"
$trackedDirty = @(& git -C $repo status --porcelain=v1 --untracked-files=no)
Require ($LASTEXITCODE -eq 0 -and $trackedDirty.Count -eq 0) `
    "Repository has tracked changes: $($trackedDirty -join '; ')"

$templateRoot = Join-Path $repo 'tools\field_packet_templates'
$manifestTool = Join-Path $repo 'tools\new_tppa_runtime_manifest.ps1'
Require ([IO.Directory]::Exists($templateRoot)) "Field packet template directory is missing: $templateRoot"
Require ([IO.File]::Exists($manifestTool)) "Runtime manifest tool is missing: $manifestTool"

$parent = [IO.Path]::GetDirectoryName($output)
Require (-not [string]::IsNullOrWhiteSpace($parent)) 'OutputDirectory must have a parent directory.'
[void][IO.Directory]::CreateDirectory($parent)
$staging = Join-Path $parent ('.tppa-field-packet-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($staging)

try {
    Copy-Item -LiteralPath $plugin -Destination (Join-Path $staging 'NINA.Plugins.PolarAlignment.dll')
    Copy-Item -LiteralPath $core -Destination (Join-Path $staging 'NINA.Plugins.PolarAlignment.QualificationCore.dll')

    & $manifestTool `
        -PluginDirectory $staging `
        -SourceCommit $SourceCommit.ToLowerInvariant() `
        -PluginVersion $PluginVersion `
        -OutputPath (Join-Path $staging 'TPPA.runtime-manifest.json') | Out-Null

    $pluginSha = (Get-FileHash -Algorithm SHA256 (Join-Path $staging 'NINA.Plugins.PolarAlignment.dll')).Hash
    $coreSha = (Get-FileHash -Algorithm SHA256 (Join-Path $staging 'NINA.Plugins.PolarAlignment.QualificationCore.dll')).Hash
    $manifestSha = (Get-FileHash -Algorithm SHA256 (Join-Path $staging 'TPPA.runtime-manifest.json')).Hash
    $tokens = [ordered]@{
        '__PLUGIN_VERSION__' = $PluginVersion
        '__SOURCE_COMMIT__' = $SourceCommit.ToLowerInvariant()
        '__PLUGIN_SHA256__' = $pluginSha
        '__PLUGIN_SHA256_LOWER__' = $pluginSha.ToLowerInvariant()
        '__CORE_SHA256__' = $coreSha
        '__MANIFEST_SHA256__' = $manifestSha
    }

    $templateNames = @(
        'COVARIANCE_PREREQUISITE.md',
        'FIELD_CAMPAIGN_MATRIX.md',
        'FIELD_READINESS.md',
        'verify_field_preflight.ps1')
    foreach ($name in $templateNames) {
        $source = Join-Path $templateRoot $name
        Require ([IO.File]::Exists($source)) "Required field packet template is missing: $name"
        $text = [IO.File]::ReadAllText($source)
        foreach ($pair in $tokens.GetEnumerator()) { $text = $text.Replace($pair.Key, $pair.Value) }
        Require ($text -notmatch '__[A-Z0-9_]+__') "Unresolved field packet token remains in $name"
        [IO.File]::WriteAllText((Join-Path $staging $name), $text, [Text.UTF8Encoding]::new($false))
    }

    $packetNames = @(
        'COVARIANCE_PREREQUISITE.md',
        'FIELD_CAMPAIGN_MATRIX.md',
        'FIELD_READINESS.md',
        'NINA.Plugins.PolarAlignment.dll',
        'NINA.Plugins.PolarAlignment.QualificationCore.dll',
        'TPPA.runtime-manifest.json',
        'verify_field_preflight.ps1')
    $hashLines = foreach ($name in $packetNames) {
        $hash = (Get-FileHash -Algorithm SHA256 (Join-Path $staging $name)).Hash
        "$hash  $name"
    }
    [IO.File]::WriteAllText(
        (Join-Path $staging 'HASHES.sha256'),
        ($hashLines -join "`n") + "`n",
        [Text.UTF8Encoding]::new($false))

    Move-Item -LiteralPath $staging -Destination $output
    [pscustomobject]@{
        SchemaVersion = 1
        PacketRoot = $output
        SourceCommit = $SourceCommit.ToLowerInvariant()
        PluginVersion = $PluginVersion
        PluginSha256 = $pluginSha
        QualificationCoreSha256 = $coreSha
        RuntimeManifestSha256 = $manifestSha
        PacketChecksumManifestSha256 = (Get-FileHash -Algorithm SHA256 (Join-Path $output 'HASHES.sha256')).Hash
    }
} catch {
    if ([IO.Directory]::Exists($staging)) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
    throw
}
