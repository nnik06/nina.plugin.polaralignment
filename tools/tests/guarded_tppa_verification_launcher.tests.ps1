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
        $text.Contains('ExpectedRuntimeManifestSha256') | Should Be $true
    }

    It 'requires an altitude margin before starting' {
        $text.Contains('TargetAltitudeMarginDegrees') | Should Be $true
        $text.Contains('lacks the required') | Should Be $true
    }

    It 'fails closed on an in-slew envelope violation' {
        $text.Contains('if ([bool]$mount.Slewing)') | Should Be $true
        $text.Contains('in-slew envelope violation') | Should Be $true
        $text.Contains('/equipment/mount/slew/stop') | Should Be $true
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

    It 'leaves tracking stopped after success or failure' {
        $text.Contains("function Stop-NinaTracking") | Should Be $true
        $text.Contains("Invoke-Nina -Path '/equipment/mount/tracking?mode=4'") | Should Be $true
        $text.Contains('foreach ($attempt in 1..10)') | Should Be $true
        $text.Contains("Invoke-Nina -Path '/equipment/mount/info'") | Should Be $true
        $text.Contains('TrackingEnabled') | Should Be $true
        $text.Contains('$holdSeconds = 10') | Should Be $true
        $text.Contains('$holdDeadline = $holdStarted.AddSeconds($holdSeconds)') | Should Be $true
        $text.Contains("'Tracking stop sustained for {0}s on attempt {1}: {2}'") | Should Be $true
        $success = $text.IndexOf("Write-RunLog 'Verification-only sequence completed with FINISHED status.'")
        $successStop = $text.IndexOf('Stop-NinaTracking', $success)
        $successExit = $text.IndexOf('exit 0', $successStop)
        $successStop | Should BeGreaterThan $success
        $successExit | Should BeGreaterThan $successStop

        $failure = $text.IndexOf("Write-RunLog ('FAIL: '")
        $failureMountStop = $text.IndexOf('Stop-NinaMount', $failure)
        $failureSequenceStop = $text.IndexOf('Stop-NinaSequence', $failureMountStop)
        $failureTerminality = $text.IndexOf('Wait-NinaSequenceTerminality', $failureSequenceStop)
        $failureTrackingStop = $text.IndexOf('Stop-NinaTracking', $failureTerminality)
        $failureMountStop | Should BeGreaterThan $failure
        $failureSequenceStop | Should BeGreaterThan $failureMountStop
        $failureTerminality | Should BeGreaterThan $failureSequenceStop
        $failureTrackingStop | Should BeGreaterThan $failureTerminality
        $text.Contains('CancellationTerminalityTimeoutSeconds') | Should Be $true
        $text.Contains('CancellationTerminalityHoldSeconds') | Should Be $true
        $text.Contains("@('CREATED', 'FINISHED', 'FAILED', 'SKIPPED')") | Should Be $true
    }

    It 'has a hard runtime deadline and pre-start admission reserve' {
        $text.Contains('MaximumRuntimeSeconds') | Should Be $true
        $text.Contains('$deadline') | Should Be $true
        $text.Contains('TPPA_RUNTIME_ADMISSION') | Should Be $true
        $text.Contains('MinimumRuntimePerPointSeconds') | Should Be $true
        $text.Contains('CleanupReserveSeconds') | Should Be $true
        $text.Contains('Verification runtime admission denied before sequence start') | Should Be $true
        $text.IndexOf('TPPA_RUNTIME_ADMISSION') | Should BeLessThan $text.IndexOf("/sequence/start?skipValidation=true")
    }

    It 'supports a separately timed no-slew solve pre-warm' {
        $text.Contains('Invoke-GuardedSolvePrewarm') | Should Be $true
        $text.Contains('TPPA_PREWARM') | Should Be $true
        $text.Contains('ExcludedFromVerificationRuntime = $true') | Should Be $true
        $text.Contains('requires the mount already settled at the validated target') | Should Be $true
        $text.Contains('violated the no-slew pointing hold') | Should Be $true
        $text.IndexOf('if ($PrewarmSolve)') | Should BeLessThan $text.IndexOf("/sequence/start?skipValidation=true")
        $sequenceStart = $text.IndexOf("/sequence/start?skipValidation=true")
        $text.IndexOf('$deadline = (Get-Date).AddSeconds', $sequenceStart) | Should BeGreaterThan $sequenceStart
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
        $text.Contains('Balcony guard armed from pre-start telemetry') | Should Be $true
        $text.Contains('ended before the balcony guard armed') | Should Be $true
        $text.Contains('reported a failed status') | Should Be $true
        $text.Contains('$instructionStatus -ne ''FINISHED''') | Should Be $true
        $text.Contains('Find-CompactSequenceLeafNodes') | Should Be $true
        $text.Contains('Find-VerificationRuntimeStatuses') | Should Be $true
        $text.Contains('TPPA runtime reported terminal verification failure') | Should Be $true
        $text.Contains('$stateJson -match') | Should Be $false
    }
}

Describe 'guarded TPPA verification launcher runtime terminal parsing' {
    BeforeAll {
        . $scriptPath `
            -SequencePath 'functions-only' `
            -PluginDirectory 'functions-only' `
            -ExpectedRuntimeManifestSha256 ('A' * 64) `
            -FunctionsOnly
    }

    It 'recognizes a current terminal failure only after current-run progress' {
        $statuses = @('Verification-only measurements complete: failed')

        (Get-VerificationTerminalOutcome $statuses) | Should Be 'failed'
        (Test-CurrentVerificationTerminalFailure $true $true $statuses) | Should Be $true
    }

    It 'does not fail for a terminal pass' {
        $statuses = @('Verification-only measurements complete: passed')

        (Get-VerificationTerminalOutcome $statuses) | Should Be 'passed'
        (Test-CurrentVerificationTerminalFailure $true $true $statuses) | Should Be $false
    }

    It 'does not accept stale terminal failure before current-run progress' {
        $statuses = @('Verification-only measurements complete: failed')

        (Test-CurrentVerificationTerminalFailure $true $false $statuses) | Should Be $false
    }

    It 'does not parse an incidental failed substring as the outcome' {
        $statuses = @('Verification-only measurements complete: passed, 0 checks failed')

        (Get-VerificationTerminalOutcome $statuses) | Should Be $null
        (Test-CurrentVerificationTerminalFailure $true $true $statuses) | Should Be $false
    }

    It 'accepts only terminal leaf and plugin observations' {
        (Test-NinaSequenceTerminalObservation @('CREATED') @()) | Should Be $true
        (Test-NinaSequenceTerminalObservation @('FINISHED') @('Verification-only measurements complete: passed')) | Should Be $true
        (Test-NinaSequenceTerminalObservation @('RUNNING') @()) | Should Be $false
        (Test-NinaSequenceTerminalObservation @('CREATED') @('Solving verification point 7')) | Should Be $false
        (Test-NinaSequenceTerminalObservation @('CREATED', 'FINISHED') @()) | Should Be $false
    }

    It 'arms from a valid pre-start point including across azimuth zero' {
        (Test-VerificationGuardArmPoint 359.7 48.2 0.2 48.0) | Should Be $true
    }

    It 'rejects a pre-start point outside either one-degree target tolerance' {
        (Test-VerificationGuardArmPoint 313.9 48.0 315.0 48.0) | Should Be $false
        (Test-VerificationGuardArmPoint 315.0 46.9 315.0 48.0) | Should Be $false
    }
}

Describe 'guarded TPPA verification launcher preflight' {
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
        $sequence = Join-Path $TestDrive 'verification.json'
        '{"Items":{"$values":[{"$type":"NINA.Plugins.PolarAlignment.Instructions.PolarAlignment, NINA.Plugins.PolarAlignment","VerificationOnly":true,"MountMotionEnvelopeEnabled":true,"MountMotionMinimumAltitudeDegrees":25,"MountMotionMaximumAltitudeDegrees":55,"MountMotionAzimuthStartDegrees":270,"MountMotionAzimuthEndDegrees":10,"Coordinates":{"AzDegrees":315,"AzMinutes":30,"AzSeconds":0,"AltDegrees":30,"AltMinutes":15,"AltSeconds":0}}]}}' | Set-Content -LiteralPath $sequence
        $result = & $scriptPath -SequencePath $sequence -PluginDirectory $plugin -ExpectedRuntimeManifestSha256 $manifestHash -PreflightOnly
        $result.TargetAzimuthDegrees | Should Be 315.5
        $result.TargetAltitudeDegrees | Should Be 30.25
    }

    It 'rejects an infeasible nine-point runtime before sequence start' {
        $sequence = Join-Path $TestDrive 'runtime-denied.json'
        '{"Items":{"$values":[{"$type":"NINA.Plugins.PolarAlignment.Instructions.PolarAlignment, NINA.Plugins.PolarAlignment","VerificationOnly":true,"VerificationDirectionSampleCount":3,"MountMotionEnvelopeEnabled":true,"MountMotionMinimumAltitudeDegrees":25,"MountMotionMaximumAltitudeDegrees":55,"MountMotionAzimuthStartDegrees":270,"MountMotionAzimuthEndDegrees":10,"Coordinates":{"AzDegrees":315,"AzMinutes":0,"AzSeconds":0,"AltDegrees":35,"AltMinutes":0,"AltSeconds":0}}]}}' | Set-Content -LiteralPath $sequence
        $message = ''
        try {
            & $scriptPath -SequencePath $sequence -PluginDirectory $plugin `
                -ExpectedRuntimeManifestSha256 $manifestHash -MaximumRuntimeSeconds 900 -PreflightOnly
        } catch {
            $message = $_.Exception.Message
        }
        $message | Should Match 'runtime admission denied before sequence start'
        $message | Should Match 'required=1260'
    }

    It 'rejects a target without altitude margin' {
        $sequence = Join-Path $TestDrive 'edge.json'
        '{"$type":"NINA.Plugins.PolarAlignment.Instructions.PolarAlignment, NINA.Plugins.PolarAlignment","VerificationOnly":true,"MountMotionEnvelopeEnabled":true,"MountMotionMinimumAltitudeDegrees":25,"MountMotionMaximumAltitudeDegrees":55,"MountMotionAzimuthStartDegrees":270,"MountMotionAzimuthEndDegrees":10,"Coordinates":{"AzDegrees":315,"AzMinutes":0,"AzSeconds":0,"AltDegrees":25,"AltMinutes":0,"AltSeconds":0}}' | Set-Content -LiteralPath $sequence
        $threw = $false
        try { & $scriptPath -SequencePath $sequence -PluginDirectory $plugin -ExpectedRuntimeManifestSha256 $manifestHash -PreflightOnly } catch { $threw = $true }
        $threw | Should Be $true
    }

    It 'rejects any additional executable instruction' {
        $sequence = Join-Path $TestDrive 'extra-instruction.json'
        '{"Items":[{"$type":"NINA.Plugins.PolarAlignment.Instructions.PolarAlignment, NINA.Plugins.PolarAlignment","VerificationOnly":true,"MountMotionEnvelopeEnabled":true,"MountMotionMinimumAltitudeDegrees":25,"MountMotionMaximumAltitudeDegrees":55,"MountMotionAzimuthStartDegrees":270,"MountMotionAzimuthEndDegrees":10,"Coordinates":{"AzDegrees":315,"AzMinutes":0,"AzSeconds":0,"AltDegrees":30,"AltMinutes":0,"AltSeconds":0}},{"$type":"NINA.Sequencer.SequenceItem.Utility.Wait, NINA.Sequencer","Time":1}]}' | Set-Content -LiteralPath $sequence
        $threw = $false
        try { & $scriptPath -SequencePath $sequence -PluginDirectory $plugin -ExpectedRuntimeManifestSha256 $manifestHash -PreflightOnly } catch { $threw = $true }
        $threw | Should Be $true
    }
    It 'rejects plural NINA condition namespaces behaviorally' {
        $sequence = Join-Path $TestDrive 'loop-condition.json'
        '{"Items":[{"$type":"NINA.Plugins.PolarAlignment.Instructions.PolarAlignment, NINA.Plugins.PolarAlignment","VerificationOnly":true,"MountMotionEnvelopeEnabled":true,"MountMotionMinimumAltitudeDegrees":25,"MountMotionMaximumAltitudeDegrees":55,"MountMotionAzimuthStartDegrees":270,"MountMotionAzimuthEndDegrees":10,"Coordinates":{"AzDegrees":315,"AzMinutes":0,"AzSeconds":0,"AltDegrees":30,"AltMinutes":0,"AltSeconds":0}}],"Conditions":[{"$type":"NINA.Sequencer.Conditions.LoopCondition, NINA.Sequencer"}]}' | Set-Content -LiteralPath $sequence
        $message = ''
        try { & $scriptPath -SequencePath $sequence -PluginDirectory $plugin -ExpectedRuntimeManifestSha256 $manifestHash -PreflightOnly } catch { $message = $_.Exception.Message }
        $message | Should Match 'must not contain triggers or conditions'
    }
    It 'accepts empty native NINA collection wrappers' {
        $sequence = Join-Path $TestDrive 'native-wrappers.json'
        '{"Items":{"$type":"System.Collections.ObjectModel.ObservableCollection`1[[NINA.Sequencer.SequenceItem.ISequenceItem, NINA.Sequencer]], System.ObjectModel","$values":[{"$type":"NINA.Plugins.PolarAlignment.Instructions.PolarAlignment, NINA.Plugins.PolarAlignment","VerificationOnly":true,"MountMotionEnvelopeEnabled":true,"MountMotionMinimumAltitudeDegrees":25,"MountMotionMaximumAltitudeDegrees":55,"MountMotionAzimuthStartDegrees":270,"MountMotionAzimuthEndDegrees":10,"Coordinates":{"AzDegrees":315,"AzMinutes":0,"AzSeconds":0,"AltDegrees":30,"AltMinutes":0,"AltSeconds":0}}]},"Conditions":{"$type":"System.Collections.ObjectModel.ObservableCollection`1[[NINA.Sequencer.Conditions.ISequenceCondition, NINA.Sequencer]], System.ObjectModel","$values":[]},"Triggers":{"$type":"System.Collections.ObjectModel.ObservableCollection`1[[NINA.Sequencer.Trigger.ISequenceTrigger, NINA.Sequencer]], System.ObjectModel","$values":[]}}' | Set-Content -LiteralPath $sequence
        $result = & $scriptPath -SequencePath $sequence -PluginDirectory $plugin -ExpectedRuntimeManifestSha256 $manifestHash -PreflightOnly
        $result.TargetAzimuthDegrees | Should Be 315
        $result.TargetAltitudeDegrees | Should Be 30
    }
}
