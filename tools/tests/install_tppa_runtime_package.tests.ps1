$ErrorActionPreference = 'Stop'

function Assert-Throws {
    param([scriptblock] $Action)
    $threw = $false
    try { & $Action } catch { $threw = $true }
    $threw | Should Be $true
}

Describe 'TPPA runtime package installer' {
    BeforeAll {
        $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
        $script:Installer = Join-Path $script:RepoRoot 'tools\install_tppa_runtime_package.ps1'
        $script:Validator = Join-Path $script:RepoRoot 'tools\validate_tppa_plugin_install.ps1'
    }

    BeforeEach {
        $root = Join-Path $TestDrive ([Guid]::NewGuid().ToString('N'))
        $package = Join-Path $root 'package'
        $live = Join-Path $root 'live'
        $archive = Join-Path $root 'archive'
        New-Item -ItemType Directory -Path $package, $live, $archive | Out-Null

        $pluginPath = Join-Path $package 'NINA.Plugins.PolarAlignment.dll'
        $corePath = Join-Path $package 'NINA.Plugins.PolarAlignment.QualificationCore.dll'
        $manifestPath = Join-Path $package 'TPPA.runtime-manifest.json'
        Set-Content -LiteralPath $pluginPath -Value 'new-plugin'
        Set-Content -LiteralPath $corePath -Value 'new-core'
        $pluginHash = (Get-FileHash $pluginPath -Algorithm SHA256).Hash
        $coreHash = (Get-FileHash $corePath -Algorithm SHA256).Hash
        $manifest = [ordered]@{
            schemaVersion = 1
            packageId = 'NINA.Plugins.PolarAlignment'
            pluginVersion = '2.2.6.77'
            sourceCommit = '0123456789abcdef0123456789abcdef01234567'
            artifacts = @(
                [ordered]@{ name = 'NINA.Plugins.PolarAlignment.dll'; sha256 = $pluginHash },
                [ordered]@{ name = 'NINA.Plugins.PolarAlignment.QualificationCore.dll'; sha256 = $coreHash }
            )
        }
        [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 4))
        $manifestHash = (Get-FileHash $manifestPath -Algorithm SHA256).Hash

        Set-Content -LiteralPath (Join-Path $live 'NINA.Plugins.PolarAlignment.dll') -Value 'old-plugin'
        Set-Content -LiteralPath (Join-Path $live 'NINA.Plugins.PolarAlignment.dll.rollback-old') -Value 'rollback'
        Set-Content -LiteralPath (Join-Path $live 'NINA.Plugins.PolarAlignment.dll.config') -Value 'config'
        Set-Content -LiteralPath (Join-Path $live 'Other.Runtime.dll') -Value 'other'
    }

    It 'installs one validated assembly pair and archives stale TPPA deployment files' {
        $result = & $script:Installer `
            -PackageDirectory $package `
            -LivePluginDirectory $live `
            -ExpectedRuntimeManifestSha256 $manifestHash `
            -ArchiveRoot $archive `
            -ValidatorPath $script:Validator `
            -NinaProcessName 'ProcessNameThatCannotExistForTppaTest'

        $result.Installed | Should Be $true
        $result.ArchivedFileCount | Should Be 2
        (Get-Content -Raw (Join-Path $live 'NINA.Plugins.PolarAlignment.dll')).Trim() | Should Be 'new-plugin'
        (Get-Content -Raw (Join-Path $live 'NINA.Plugins.PolarAlignment.QualificationCore.dll')).Trim() | Should Be 'new-core'
        (Test-Path -LiteralPath (Join-Path $live 'NINA.Plugins.PolarAlignment.dll.rollback-old')) | Should Be $false
        (Test-Path -LiteralPath (Join-Path $live 'NINA.Plugins.PolarAlignment.dll.config')) | Should Be $true
        (Test-Path -LiteralPath (Join-Path $live 'Other.Runtime.dll')) | Should Be $true
        @(Get-ChildItem -LiteralPath $result.ArchiveDirectory -Recurse -File).Count | Should Be 2
        (& $script:Validator -PluginDirectory $live -ExpectedRuntimeManifestSha256 $manifestHash).AssemblyCount | Should Be 2
    }

    It 'rejects installation while NINA is running without changing the live tree' {
        $currentProcessName = (Get-Process -Id $PID).ProcessName
        Assert-Throws { & $script:Installer `
                -PackageDirectory $package `
                -LivePluginDirectory $live `
                -ExpectedRuntimeManifestSha256 $manifestHash `
                -ArchiveRoot $archive `
                -ValidatorPath $script:Validator `
                -NinaProcessName $currentProcessName }

        (Get-Content -Raw (Join-Path $live 'NINA.Plugins.PolarAlignment.dll')).Trim() | Should Be 'old-plugin'
        @(Get-ChildItem -LiteralPath $archive -Recurse -File).Count | Should Be 0
    }

    It 'rejects an invalid package before archiving the live runtime' {
        Assert-Throws { & $script:Installer `
                -PackageDirectory $package `
                -LivePluginDirectory $live `
                -ExpectedRuntimeManifestSha256 ('0' * 64) `
                -ArchiveRoot $archive `
                -ValidatorPath $script:Validator `
                -NinaProcessName 'ProcessNameThatCannotExistForTppaTest' }

        (Get-Content -Raw (Join-Path $live 'NINA.Plugins.PolarAlignment.dll')).Trim() | Should Be 'old-plugin'
        @(Get-ChildItem -LiteralPath $archive -Recurse -File).Count | Should Be 0
    }

    It 'restores the prior runtime when final live validation fails' {
        $mockValidator = Join-Path $root 'mock-validator.ps1'
        @'
param([string] $PluginDirectory, [string] $ExpectedRuntimeManifestSha256)
if ((Split-Path $PluginDirectory -Leaf) -eq 'live') { throw 'synthetic live validation failure' }
[pscustomobject]@{
    PluginVersion = '2.2.6.77'
    SourceCommit = '0123456789abcdef0123456789abcdef01234567'
    RuntimeManifestSha256 = $ExpectedRuntimeManifestSha256
    Sha256 = (Get-FileHash (Join-Path $PluginDirectory 'NINA.Plugins.PolarAlignment.dll') -Algorithm SHA256).Hash
    QualificationCoreSha256 = (Get-FileHash (Join-Path $PluginDirectory 'NINA.Plugins.PolarAlignment.QualificationCore.dll') -Algorithm SHA256).Hash
}
'@ | Set-Content -LiteralPath $mockValidator

        Assert-Throws { & $script:Installer `
                -PackageDirectory $package `
                -LivePluginDirectory $live `
                -ExpectedRuntimeManifestSha256 $manifestHash `
                -ArchiveRoot $archive `
                -ValidatorPath $mockValidator `
                -NinaProcessName 'ProcessNameThatCannotExistForTppaTest' }

        (Get-Content -Raw (Join-Path $live 'NINA.Plugins.PolarAlignment.dll')).Trim() | Should Be 'old-plugin'
        (Test-Path -LiteralPath (Join-Path $live 'NINA.Plugins.PolarAlignment.dll.rollback-old')) | Should Be $true
        (Test-Path -LiteralPath (Join-Path $live 'NINA.Plugins.PolarAlignment.QualificationCore.dll')) | Should Be $false
        (Test-Path -LiteralPath (Join-Path $live 'TPPA.runtime-manifest.json')) | Should Be $false
    }
}
