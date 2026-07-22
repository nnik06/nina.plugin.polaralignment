$script = Join-Path $PSScriptRoot '..\validate_tppa_plugin_install.ps1'

Describe 'validate_tppa_plugin_install' {
    BeforeEach {
        $root = Join-Path $TestDrive ([Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $root | Out-Null
        Set-Content -LiteralPath (Join-Path $root 'NINA.Plugins.PolarAlignment.dll') -Value 'current'
    }

    It 'accepts one live assembly with the expected hash' {
        $hash = (Get-FileHash (Join-Path $root 'NINA.Plugins.PolarAlignment.dll') -Algorithm SHA256).Hash
        $result = & $script -PluginDirectory $root -ExpectedSha256 $hash
        $result.AssemblyCount | Should Be 1
        $result.Sha256 | Should Be $hash
    }

    It 'rejects rollback assemblies inside the live plugin tree' {
        $backup = Join-Path $root 'backup-old'
        New-Item -ItemType Directory -Path $backup | Out-Null
        Set-Content -LiteralPath (Join-Path $backup 'NINA.Plugins.PolarAlignment.dll') -Value 'old'
        $threw = $false
        try { & $script -PluginDirectory $root } catch { $threw = $true }
        $threw | Should Be $true
    }

    It 'rejects a hash mismatch' {
        $threw = $false
        try { & $script -PluginDirectory $root -ExpectedSha256 ('0' * 64) } catch { $threw = $true }
        $threw | Should Be $true
    }
}
