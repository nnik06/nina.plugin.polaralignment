$generator = Join-Path $PSScriptRoot '..\new_tppa_runtime_manifest.ps1'
$validator = Join-Path $PSScriptRoot '..\validate_tppa_plugin_install.ps1'
function Assert-Throws {
    param([scriptblock]$Action)
    $threw = $false
    try { & $Action } catch { $threw = $true }
    $threw | Should Be $true
}

Describe 'TPPA runtime manifest generation' {
    BeforeEach {
        $root = Join-Path $TestDrive ([Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $root | Out-Null
        Set-Content -LiteralPath (Join-Path $root 'NINA.Plugins.PolarAlignment.dll') -Value 'plugin'
        Set-Content -LiteralPath (Join-Path $root 'NINA.Plugins.PolarAlignment.QualificationCore.dll') -Value 'core'
    }

    It 'emits one manifest that binds both runtime artifacts to source provenance' {
        $result = & $generator -PluginDirectory $root `
            -SourceCommit 'fedcba9876543210fedcba9876543210fedcba98' `
            -PluginVersion '2.2.6.76'
        $manifest = [IO.File]::ReadAllText($result.RuntimeManifestPath) | ConvertFrom-Json
        $manifest.artifacts.Count | Should Be 2
        $manifest.sourceCommit | Should Be 'fedcba9876543210fedcba9876543210fedcba98'
        $manifest.pluginVersion | Should Be '2.2.6.76'

        $validated = & $validator -PluginDirectory $root `
            -ExpectedRuntimeManifestSha256 $result.RuntimeManifestSha256
        $validated.RuntimeManifestSha256 | Should Be $result.RuntimeManifestSha256
    }

    It 'rejects generation when either runtime artifact is missing' {
        Remove-Item -LiteralPath (Join-Path $root 'NINA.Plugins.PolarAlignment.QualificationCore.dll')
        Assert-Throws { & $generator -PluginDirectory $root `
                -SourceCommit 'fedcba9876543210fedcba9876543210fedcba98' `
                -PluginVersion '2.2.6.76' }
    }

    It 'rejects a non-four-part plugin version' {
        Assert-Throws { & $generator -PluginDirectory $root `
                -SourceCommit 'fedcba9876543210fedcba9876543210fedcba98' `
                -PluginVersion '2.2.6' }
    }
}
