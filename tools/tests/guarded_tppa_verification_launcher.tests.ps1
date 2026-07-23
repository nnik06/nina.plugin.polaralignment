$scriptPath = Join-Path $PSScriptRoot '..\run_guarded_tppa_verification.ps1'
$text = [IO.File]::ReadAllText((Resolve-Path $scriptPath))

Describe 'guarded TPPA verification launcher static safety contract' {
    It 'requires verification-only mode' {
        $text.Contains('The selected sequence is not VerificationOnly') | Should Be $true
        $text.Contains('ConvertFrom-Json') | Should Be $true
        $text.Contains('Find-VerificationInstruction') | Should Be $true
    }

    It 'requires the install validator and an expected plugin hash' {
        $text.Contains('validate_tppa_plugin_install.ps1') | Should Be $true
        $text.Contains('ExpectedPluginSha256') | Should Be $true
    }
    It 'requires an altitude margin before starting' {
        $text.Contains('TargetAltitudeMarginDegrees') | Should Be $true
        $text.Contains('lacks the required') | Should Be $true
    }

    It 'does not judge transient coordinates while slewing' {
        $text.Contains('if (-not [bool]$mount.Slewing)') | Should Be $true
        $text.Contains('settled pointing') | Should Be $true
    }

    It 'uses the supported GET stop endpoint' {
        $text.Contains("Invoke-Nina -Path '/sequence/stop'") | Should Be $true
        $text.Contains("-Method Post") | Should Be $false
    }

    It 'has a hard runtime deadline' {
        $text.Contains('MaximumRuntimeSeconds') | Should Be $true
        $text.Contains('$deadline') | Should Be $true
    }
}

Describe 'guarded TPPA verification launcher preflight' {
    BeforeEach {
        $plugin = Join-Path $TestDrive ([Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $plugin | Out-Null
        Set-Content -LiteralPath (Join-Path $plugin 'NINA.Plugins.PolarAlignment.dll') -Value 'current'
        $pluginHash = (Get-FileHash (Join-Path $plugin 'NINA.Plugins.PolarAlignment.dll') -Algorithm SHA256).Hash
    }

    It 'parses horizontal degrees minutes and seconds structurally' {
        $sequence = Join-Path $TestDrive 'verification.json'
        '{"Items":{"$values":[{"VerificationOnly":true,"Coordinates":{"AzDegrees":315,"AzMinutes":30,"AzSeconds":0,"AltDegrees":30,"AltMinutes":15,"AltSeconds":0}}]}}' | Set-Content -LiteralPath $sequence
        $result = & $scriptPath -SequencePath $sequence -PluginDirectory $plugin -ExpectedPluginSha256 $pluginHash -PreflightOnly
        $result.TargetAzimuthDegrees | Should Be 315.5
        $result.TargetAltitudeDegrees | Should Be 30.25
    }

    It 'rejects a target without altitude margin' {
        $sequence = Join-Path $TestDrive 'edge.json'
        '{"VerificationOnly":true,"Coordinates":{"AzDegrees":315,"AzMinutes":0,"AzSeconds":0,"AltDegrees":25,"AltMinutes":0,"AltSeconds":0}}' | Set-Content -LiteralPath $sequence
        $threw = $false
        try { & $scriptPath -SequencePath $sequence -PluginDirectory $plugin -ExpectedPluginSha256 $pluginHash -PreflightOnly } catch { $threw = $true }
        $threw | Should Be $true
    }
}