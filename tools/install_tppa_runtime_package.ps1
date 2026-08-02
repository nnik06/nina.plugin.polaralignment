[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $PackageDirectory,

    [Parameter(Mandatory = $true)]
    [string] $LivePluginDirectory,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string] $ExpectedRuntimeManifestSha256,

    [Parameter(Mandatory = $true)]
    [string] $ArchiveRoot,

    [string] $ValidatorPath = (Join-Path $PSScriptRoot 'validate_tppa_plugin_install.ps1'),

    [string] $NinaProcessName = 'NINA'
)

$ErrorActionPreference = 'Stop'

function Get-FullPath([string] $Path) {
    return [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar)
}

function Test-DescendantPath([string] $Candidate, [string] $Parent) {
    $prefix = $Parent.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    return $Candidate.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}

$packageRoot = (Resolve-Path -LiteralPath $PackageDirectory).Path
$validator = (Resolve-Path -LiteralPath $ValidatorPath).Path
$liveRoot = Get-FullPath $LivePluginDirectory
$archiveBase = Get-FullPath $ArchiveRoot

if ($packageRoot -eq $liveRoot -or
    (Test-DescendantPath $packageRoot $liveRoot) -or
    (Test-DescendantPath $archiveBase $liveRoot)) {
    throw 'Package and archive locations must be outside the live TPPA plugin tree.'
}

if (@(Get-Process -Name $NinaProcessName -ErrorAction SilentlyContinue).Count -ne 0) {
    throw "NINA process '$NinaProcessName' is running. Close NINA before installing TPPA."
}

$packageValidation = & $validator `
    -PluginDirectory $packageRoot `
    -ExpectedRuntimeManifestSha256 $ExpectedRuntimeManifestSha256

$pluginName = 'NINA.Plugins.PolarAlignment.dll'
$coreName = 'NINA.Plugins.PolarAlignment.QualificationCore.dll'
$manifestName = 'TPPA.runtime-manifest.json'
$runtimeNames = @($pluginName, $coreName, $manifestName)
$timestamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
$archiveDirectory = Join-Path $archiveBase "$timestamp-$($packageValidation.PluginVersion)-$($packageValidation.SourceCommit.Substring(0, 8))"
$transactionId = [Guid]::NewGuid().ToString('N')
$archivedFiles = [Collections.Generic.List[object]]::new()
$stagedFiles = [Collections.Generic.List[object]]::new()

New-Item -ItemType Directory -Path $archiveDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $liveRoot -Force | Out-Null

try {
    $existingCandidates = @(Get-ChildItem -LiteralPath $liveRoot -Recurse -File |
        Where-Object {
            $_.Name -eq $pluginName -or
            $_.Name -eq $coreName -or
            $_.Name -eq $manifestName -or
            ($_.Name -like 'NINA.Plugins.PolarAlignment.dll.*' -and $_.Name -ne 'NINA.Plugins.PolarAlignment.dll.config') -or
            $_.Name -like 'NINA.Plugins.PolarAlignment*.dll' -or
            $_.Name -like '*.NINA.Plugins.PolarAlignment.dll'
        })

    foreach ($item in $existingCandidates) {
        $relativePath = [IO.Path]::GetRelativePath($liveRoot, $item.FullName)
        $archivePath = Join-Path $archiveDirectory $relativePath
        New-Item -ItemType Directory -Path (Split-Path $archivePath -Parent) -Force | Out-Null
        Move-Item -LiteralPath $item.FullName -Destination $archivePath
        $archivedFiles.Add([pscustomobject]@{ LivePath = $item.FullName; ArchivePath = $archivePath })
    }

    foreach ($name in $runtimeNames) {
        $sourcePath = Join-Path $packageRoot $name
        $stagedPath = Join-Path $liveRoot ".$name.installing-$transactionId"
        Copy-Item -LiteralPath $sourcePath -Destination $stagedPath
        $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
        $stagedHash = (Get-FileHash -LiteralPath $stagedPath -Algorithm SHA256).Hash
        if ($sourceHash -ne $stagedHash) {
            throw "Staged TPPA artifact hash mismatch for $name."
        }
        $stagedFiles.Add([pscustomobject]@{ Name = $name; StagedPath = $stagedPath })
    }

    foreach ($entry in $stagedFiles) {
        Move-Item -LiteralPath $entry.StagedPath -Destination (Join-Path $liveRoot $entry.Name)
    }

    $liveValidation = & $validator `
        -PluginDirectory $liveRoot `
        -ExpectedRuntimeManifestSha256 $ExpectedRuntimeManifestSha256

    [pscustomobject]@{
        Installed = $true
        LivePluginDirectory = $liveRoot
        ArchiveDirectory = $archiveDirectory
        ArchivedFileCount = $archivedFiles.Count
        RuntimeManifestSha256 = $liveValidation.RuntimeManifestSha256
        SourceCommit = $liveValidation.SourceCommit
        PluginVersion = $liveValidation.PluginVersion
        PluginSha256 = $liveValidation.Sha256
        QualificationCoreSha256 = $liveValidation.QualificationCoreSha256
    }
} catch {
    $failure = $_
    foreach ($entry in $stagedFiles) {
        if (Test-Path -LiteralPath $entry.StagedPath -PathType Leaf) {
            Remove-Item -LiteralPath $entry.StagedPath -Force
        }
        $installedPath = Join-Path $liveRoot $entry.Name
        if (Test-Path -LiteralPath $installedPath -PathType Leaf) {
            Remove-Item -LiteralPath $installedPath -Force
        }
    }
    foreach ($entry in $archivedFiles) {
        New-Item -ItemType Directory -Path (Split-Path $entry.LivePath -Parent) -Force | Out-Null
        Move-Item -LiteralPath $entry.ArchivePath -Destination $entry.LivePath -Force
    }
    throw "TPPA installation failed and the prior runtime was restored: $($failure.Exception.Message)"
}
