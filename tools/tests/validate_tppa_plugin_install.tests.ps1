$script = Join-Path $PSScriptRoot '..\validate_tppa_plugin_install.ps1'
function Assert-Throws {
    param([scriptblock]$Action)
    $threw = $false
    try { & $Action } catch { $threw = $true }
    $threw | Should Be $true
}

Describe 'validate_tppa_plugin_install' {
    BeforeEach {
        $root = Join-Path $TestDrive ([Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $root | Out-Null
        $pluginPath = Join-Path $root 'NINA.Plugins.PolarAlignment.dll'
        $corePath = Join-Path $root 'NINA.Plugins.PolarAlignment.QualificationCore.dll'
        $manifestPath = Join-Path $root 'TPPA.runtime-manifest.json'
        Set-Content -LiteralPath $pluginPath -Value 'current'
        Set-Content -LiteralPath $corePath -Value 'core'
        $pluginHash = (Get-FileHash $pluginPath -Algorithm SHA256).Hash
        $coreHash = (Get-FileHash $corePath -Algorithm SHA256).Hash
        $manifest = [ordered]@{
            schemaVersion = 1
            packageId = 'NINA.Plugins.PolarAlignment'
            pluginVersion = '2.2.6.76'
            sourceCommit = '0123456789abcdef0123456789abcdef01234567'
            artifacts = @(
                [ordered]@{ name = 'NINA.Plugins.PolarAlignment.dll'; sha256 = $pluginHash },
                [ordered]@{ name = 'NINA.Plugins.PolarAlignment.QualificationCore.dll'; sha256 = $coreHash }
            )
        }
        [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 4))
        $manifestHash = (Get-FileHash $manifestPath -Algorithm SHA256).Hash
    }

    It 'accepts the build-bound runtime manifest and assembly pair' {
        $result = & $script -PluginDirectory $root `
            -ExpectedRuntimeManifestSha256 $manifestHash
        $result.AssemblyCount | Should Be 2
        $result.Sha256 | Should Be $pluginHash
        $result.QualificationCoreSha256 | Should Be $coreHash
        $result.RuntimeManifestSha256 | Should Be $manifestHash
        $result.SourceCommit | Should Be '0123456789abcdef0123456789abcdef01234567'
        $result.PluginVersion | Should Be '2.2.6.76'
    }

    It 'rejects a missing runtime manifest' {
        Remove-Item -LiteralPath $manifestPath
        Assert-Throws { & $script -PluginDirectory $root -ExpectedRuntimeManifestSha256 $manifestHash }
    }

    It 'rejects a runtime manifest hash mismatch' {
        Assert-Throws { & $script -PluginDirectory $root -ExpectedRuntimeManifestSha256 ('0' * 64) }
    }

    It 'rejects a missing qualification-core dependency' {
        Remove-Item -LiteralPath $corePath
        Assert-Throws { & $script -PluginDirectory $root -ExpectedRuntimeManifestSha256 $manifestHash }
    }

    It 'rejects rollback assemblies inside the live plugin tree' {
        $backup = Join-Path $root 'backup-old'
        New-Item -ItemType Directory -Path $backup | Out-Null
        Set-Content -LiteralPath (Join-Path $backup 'NINA.Plugins.PolarAlignment.dll') -Value 'old'
        Assert-Throws { & $script -PluginDirectory $root -ExpectedRuntimeManifestSha256 $manifestHash }
    }

    It 'rejects main-assembly tampering after manifest creation' {
        Set-Content -LiteralPath $pluginPath -Value 'changed'
        Assert-Throws { & $script -PluginDirectory $root -ExpectedRuntimeManifestSha256 $manifestHash }
    }

    It 'rejects qualification-core tampering after manifest creation' {
        Set-Content -LiteralPath $corePath -Value 'changed'
        Assert-Throws { & $script -PluginDirectory $root -ExpectedRuntimeManifestSha256 $manifestHash }
    }

    It 'rejects duplicate artifact entries even with a newly pinned manifest hash' {
        $manifest.artifacts[1].name = 'NINA.Plugins.PolarAlignment.dll'
        [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 4))
        $duplicateManifestHash = (Get-FileHash $manifestPath -Algorithm SHA256).Hash
        Assert-Throws { & $script -PluginDirectory $root `
                -ExpectedRuntimeManifestSha256 $duplicateManifestHash }
    }

    It 'rejects malformed provenance even with a newly pinned manifest hash' {
        $manifest.sourceCommit = 'not-a-commit'
        [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 4))
        $invalidManifestHash = (Get-FileHash $manifestPath -Algorithm SHA256).Hash
        Assert-Throws { & $script -PluginDirectory $root `
                -ExpectedRuntimeManifestSha256 $invalidManifestHash }
    }
}
