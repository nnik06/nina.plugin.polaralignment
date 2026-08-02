$scriptPath = Join-Path $PSScriptRoot '..\run_guarded_tppa_drift_validation.ps1'
$text = [IO.File]::ReadAllText((Resolve-Path $scriptPath))

Describe 'guarded TPPA drift-validation launcher static safety contract' {
    It 'requires exclusive drift-validation mode' {
        $text.Contains('The selected sequence is not exclusively DriftValidationOnly') | Should Be $true
        $text.Contains('ConvertFrom-Json') | Should Be $true
        $text.Contains('Find-DriftValidationInstruction') | Should Be $true
    }

    It 'requires the install validator and an expected plugin hash' {
        $text.Contains('validate_tppa_plugin_install.ps1') | Should Be $true
        $text.Contains('ExpectedRuntimeManifestSha256') | Should Be $true
    }

    It 'requires an altitude margin before starting' {
        $text.Contains('TargetAltitudeMarginDegrees') | Should Be $true
        $text.Contains('lacks the required') | Should Be $true
    }

    It 'does not judge transient coordinates while slewing' {
        $text.Contains('if (-not [bool]$mount.Slewing)') | Should Be $true
        $text.Contains('settled pointing') | Should Be $true
    }

    It 'confirms a settled envelope violation against immediate follow-up telemetry' {
        $text.Contains('Confirm-SettledBalconyViolation') | Should Be $true
        $text.Contains('foreach ($attempt in 1..2)') | Should Be $true
        $text.Contains('Ignored one transient out-of-envelope Advanced API sample') | Should Be $true
        $text.Contains('Confirmed settled balcony violation') | Should Be $true
    }

    It 'uses the supported GET stop endpoint' {
        $text.Contains("Invoke-Nina -Path '/sequence/stop'") | Should Be $true
        $text.Contains("-Method Post") | Should Be $false
    }

    It 'has a hard runtime deadline' {
        $text.Contains('MaximumRuntimeSeconds') | Should Be $true
        $text.Contains('$deadline') | Should Be $true
    }

    It 'polls the compact sequence status endpoint' {
        $text.Contains("Invoke-Nina -Path '/sequence/json'") | Should Be $true
        $text.Contains("Invoke-Nina -Path '/sequence/state'") | Should Be $false
        $text.Contains('without image-heavy state') | Should Be $true
    }

    It 'requires one trigger-free executable instruction and a safe pre-start mount' {
        $text.Contains('exactly one executable instruction') | Should Be $true
        $text.Contains('unexpected instruction type') | Should Be $true
        $text.Contains('must not contain triggers or conditions') | Should Be $true
        $text.Contains('requires explicit parked and slewing mount state') | Should Be $true
        $text.Contains('mount is parked') | Should Be $true
        $text.Contains('mount is already slewing') | Should Be $true
    }

    It 'requires the balcony guard to arm and positive instruction completion' {
        $text.Contains('$armDeadline') | Should Be $true
        $text.Contains('ended before the balcony guard armed') | Should Be $true
        $text.Contains('reported a failed status') | Should Be $true
        $text.Contains('$instructionStatus -ne ''FINISHED''') | Should Be $true
        $text.Contains('Find-CompactSequenceLeafNodes') | Should Be $true
        $text.Contains('Find-VerificationRuntimeStatuses') | Should Be $true
        $text.Contains('TPPA runtime reported terminal drift-validation failure') | Should Be $true
        $text.Contains('$stateJson -match') | Should Be $false
    }
}

Describe 'guarded TPPA drift-validation launcher runtime terminal parsing' {
    BeforeAll {
        . $scriptPath `
            -SequencePath 'functions-only' `
            -PluginDirectory 'functions-only' `
            -ExpectedRuntimeManifestSha256 ('A' * 64) `
            -FunctionsOnly
    }

    It 'recognizes a current terminal failure only after current-run progress' {
        $statuses = @('Drift validation rejected: inconsistent tracks')

        (Get-DriftValidationTerminalOutcome $statuses) | Should Be 'failed'
        (Test-CurrentDriftValidationTerminalFailure $true $true $statuses) | Should Be $true
    }

    It 'does not fail for a terminal pass' {
        $statuses = @('Drift validation complete: 12.3 total (report only)')

        (Get-DriftValidationTerminalOutcome $statuses) | Should Be 'passed'
        (Test-CurrentDriftValidationTerminalFailure $true $true $statuses) | Should Be $false
    }

    It 'does not accept stale terminal failure before current-run progress' {
        $statuses = @('Drift validation rejected: inconsistent tracks')

        (Test-CurrentDriftValidationTerminalFailure $true $false $statuses) | Should Be $false
    }

    It 'does not parse an incidental failed substring as the outcome' {
        $statuses = @('Drift validation collecting samples; prior fit failed but acquisition continues')

        (Get-DriftValidationTerminalOutcome $statuses) | Should Be $null
        (Test-CurrentDriftValidationTerminalFailure $true $true $statuses) | Should Be $false
    }
}

Describe 'guarded TPPA drift-validation launcher preflight' {
    BeforeEach {
        $plugin = Join-Path $TestDrive ([Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $plugin | Out-Null
        Set-Content -LiteralPath (Join-Path $plugin 'NINA.Plugins.PolarAlignment.dll') -Value 'current'
        Set-Content -LiteralPath (Join-Path $plugin 'NINA.Plugins.PolarAlignment.QualificationCore.dll') -Value 'core'
        $pluginHash = (Get-FileHash (Join-Path $plugin 'NINA.Plugins.PolarAlignment.dll') -Algorithm SHA256).Hash
        $coreHash = (Get-FileHash (Join-Path $plugin 'NINA.Plugins.PolarAlignment.QualificationCore.dll') -Algorithm SHA256).Hash
        $manifestPath = Join-Path $plugin 'TPPA.runtime-manifest.json'
        $manifest = [ordered]@{
            schemaVersion = 1
            packageId = 'NINA.Plugins.PolarAlignment'
            pluginVersion = '1.2.3.4'
            sourceCommit = '0123456789abcdef0123456789abcdef01234567'
            artifacts = @(
                [ordered]@{ name = 'NINA.Plugins.PolarAlignment.dll'; sha256 = $pluginHash },
                [ordered]@{ name = 'NINA.Plugins.PolarAlignment.QualificationCore.dll'; sha256 = $coreHash }
            )
        }
        [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 4))
        $manifestHash = (Get-FileHash $manifestPath -Algorithm SHA256).Hash
    }

    It 'parses horizontal degrees minutes and seconds structurally' {
        $sequence = Join-Path $TestDrive 'drift-validation.json'
        '{"Items":{"$values":[{"$type":"NINA.Plugins.PolarAlignment.Instructions.PolarAlignment, NINA.Plugins.PolarAlignment","VerificationOnly":false,"DriftValidationOnly":true,"MountMotionEnvelopeEnabled":true,"MountMotionMinimumAltitudeDegrees":25,"MountMotionMaximumAltitudeDegrees":55,"MountMotionAzimuthStartDegrees":270,"MountMotionAzimuthEndDegrees":10,"Coordinates":{"AzDegrees":315,"AzMinutes":30,"AzSeconds":0,"AltDegrees":30,"AltMinutes":15,"AltSeconds":0}}]}}' | Set-Content -LiteralPath $sequence
        $result = & $scriptPath -SequencePath $sequence -PluginDirectory $plugin -ExpectedRuntimeManifestSha256 $manifestHash -PreflightOnly
        $result.TargetAzimuthDegrees | Should Be 315.5
        $result.TargetAltitudeDegrees | Should Be 30.25
    }

    It 'rejects a target without altitude margin' {
        $sequence = Join-Path $TestDrive 'edge.json'
        '{"$type":"NINA.Plugins.PolarAlignment.Instructions.PolarAlignment, NINA.Plugins.PolarAlignment","VerificationOnly":false,"DriftValidationOnly":true,"MountMotionEnvelopeEnabled":true,"MountMotionMinimumAltitudeDegrees":25,"MountMotionMaximumAltitudeDegrees":55,"MountMotionAzimuthStartDegrees":270,"MountMotionAzimuthEndDegrees":10,"Coordinates":{"AzDegrees":315,"AzMinutes":0,"AzSeconds":0,"AltDegrees":25,"AltMinutes":0,"AltSeconds":0}}' | Set-Content -LiteralPath $sequence
        $threw = $false
        try { & $scriptPath -SequencePath $sequence -PluginDirectory $plugin -ExpectedRuntimeManifestSha256 $manifestHash -PreflightOnly } catch { $threw = $true }
        $threw | Should Be $true
    }

    It 'rejects any additional executable instruction' {
        $sequence = Join-Path $TestDrive 'extra-instruction.json'
        '{"Items":[{"$type":"NINA.Plugins.PolarAlignment.Instructions.PolarAlignment, NINA.Plugins.PolarAlignment","VerificationOnly":false,"DriftValidationOnly":true,"MountMotionEnvelopeEnabled":true,"MountMotionMinimumAltitudeDegrees":25,"MountMotionMaximumAltitudeDegrees":55,"MountMotionAzimuthStartDegrees":270,"MountMotionAzimuthEndDegrees":10,"Coordinates":{"AzDegrees":315,"AzMinutes":0,"AzSeconds":0,"AltDegrees":30,"AltMinutes":0,"AltSeconds":0}},{"$type":"NINA.Sequencer.SequenceItem.Utility.Wait, NINA.Sequencer","Time":1}]}' | Set-Content -LiteralPath $sequence
        $threw = $false
        try { & $scriptPath -SequencePath $sequence -PluginDirectory $plugin -ExpectedRuntimeManifestSha256 $manifestHash -PreflightOnly } catch { $threw = $true }
        $threw | Should Be $true
    }
    It 'rejects plural NINA condition namespaces behaviorally' {
        $sequence = Join-Path $TestDrive 'loop-condition.json'
        '{"Items":[{"$type":"NINA.Plugins.PolarAlignment.Instructions.PolarAlignment, NINA.Plugins.PolarAlignment","VerificationOnly":false,"DriftValidationOnly":true,"MountMotionEnvelopeEnabled":true,"MountMotionMinimumAltitudeDegrees":25,"MountMotionMaximumAltitudeDegrees":55,"MountMotionAzimuthStartDegrees":270,"MountMotionAzimuthEndDegrees":10,"Coordinates":{"AzDegrees":315,"AzMinutes":0,"AzSeconds":0,"AltDegrees":30,"AltMinutes":0,"AltSeconds":0}}],"Conditions":[{"$type":"NINA.Sequencer.Conditions.LoopCondition, NINA.Sequencer"}]}' | Set-Content -LiteralPath $sequence
        $message = ''
        try { & $scriptPath -SequencePath $sequence -PluginDirectory $plugin -ExpectedRuntimeManifestSha256 $manifestHash -PreflightOnly } catch { $message = $_.Exception.Message }
        $message | Should Match 'must not contain triggers or conditions'
    }
    It 'accepts empty native NINA collection wrappers' {
        $sequence = Join-Path $TestDrive 'native-wrappers.json'
        '{"Items":{"$type":"System.Collections.ObjectModel.ObservableCollection`1[[NINA.Sequencer.SequenceItem.ISequenceItem, NINA.Sequencer]], System.ObjectModel","$values":[{"$type":"NINA.Plugins.PolarAlignment.Instructions.PolarAlignment, NINA.Plugins.PolarAlignment","VerificationOnly":false,"DriftValidationOnly":true,"MountMotionEnvelopeEnabled":true,"MountMotionMinimumAltitudeDegrees":25,"MountMotionMaximumAltitudeDegrees":55,"MountMotionAzimuthStartDegrees":270,"MountMotionAzimuthEndDegrees":10,"Coordinates":{"AzDegrees":315,"AzMinutes":0,"AzSeconds":0,"AltDegrees":30,"AltMinutes":0,"AltSeconds":0}}]},"Conditions":{"$type":"System.Collections.ObjectModel.ObservableCollection`1[[NINA.Sequencer.Conditions.ISequenceCondition, NINA.Sequencer]], System.ObjectModel","$values":[]},"Triggers":{"$type":"System.Collections.ObjectModel.ObservableCollection`1[[NINA.Sequencer.Trigger.ISequenceTrigger, NINA.Sequencer]], System.ObjectModel","$values":[]}}' | Set-Content -LiteralPath $sequence
        $result = & $scriptPath -SequencePath $sequence -PluginDirectory $plugin -ExpectedRuntimeManifestSha256 $manifestHash -PreflightOnly
        $result.TargetAzimuthDegrees | Should Be 315
        $result.TargetAltitudeDegrees | Should Be 30
    }
}
