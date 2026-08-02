Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$toolsRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$toolPath = Join-Path $toolsRoot 'capture_phd2_guided_evidence.ps1'
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    $toolPath,
    [ref]$tokens,
    [ref]$errors)
if ($errors.Count -gt 0) {
    throw "$toolPath parse errors: $($errors -join '; ')"
}
foreach ($functionName in @(
        'Get-Median', 'Get-StateContinuity', 'Get-AngularDelta',
        'Test-StateEquivalent', 'Get-GuideStepContinuity')) {
    $functionAst = $ast.Find({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq $functionName
    }, $true)
    if ($null -eq $functionAst) { throw "Missing function $functionName" }
    . ([scriptblock]::Create($functionAst.Extent.Text))
}
$text = [IO.File]::ReadAllText($toolPath)
$script:RequiredStateKeys = @(
    'targetRaDegrees', 'targetDecDegrees', 'pierSide',
    'rotatorAngleDegrees', 'filter', 'gain', 'offset', 'binning',
    'readoutMode', 'focusPosition', 'coolerSetPointC', 'coolerOn',
    'trackingMode', 'trackingEnabled', 'mountConnected', 'cameraConnected',
    'filterWheelConnected', 'focuserConnected', 'rotatorConnected',
    'mountSlewing', 'filterWheelMoving', 'focuserMoving', 'focuserSettling',
    'rotatorMoving', 'phd2Profile', 'phd2ExposureMs',
    'phd2AlgorithmStateDigest')

Describe 'read-only guided PHD2 evidence collector contract' {
    It 'uses a second client with a strict read-only RPC allow-list' {
        $text.Contains('Connect-Phd2') | Should Be $true
        foreach ($method in @(
                'get_app_state', 'get_connected', 'get_calibrated',
                'get_guide_output_enabled', 'get_exposure',
                'get_lock_position', 'get_pixel_scale', 'get_profile',
                'get_current_equipment', 'get_algo_param_names',
                'get_algo_param')) {
            $text.Contains("'$method'") | Should Be $true
        }
        foreach ($method in @(
                'guide', 'stop_capture', 'set_guide_output_enabled',
                'dither', 'set_lock_position', 'pause')) {
            $text.Contains("'$method'") | Should Be $false
        }
        $text.Contains('StateChangingRpcMethods = @()') | Should Be $true
    }

    It 'requires guiding, guide output, calibration, scale, and lock position' {
        $text.Contains("`$state -ne 'Guiding'") | Should Be $true
        $text.Contains('-not $guideOutput') | Should Be $true
        $text.Contains('-not $connected -or -not $calibrated') | Should Be $true
        $text.Contains('$pixelScale -le 0.0') | Should Be $true
        $text.Contains('$lockPosition.Count -lt 2') | Should Be $true
    }

    It 'fails closed on any discontinuity relevant to delivered rotation' {
        foreach ($eventName in @(
                'StarLost', 'StarSelected', 'GuidingDithered', 'LockPositionSet',
                'LockPositionShiftLimitReached', 'LockPositionLost',
                'GuidingStopped', 'StartGuiding', 'StartCalibration',
                'Calibrating', 'CalibrationComplete', 'CalibrationFailed',
                'CalibrationDataFlipped', 'SettleBegin', 'Settling',
                'SettleDone', 'Paused', 'Resumed', 'LoopingExposures',
                'LoopingExposuresStopped', 'SingleFrameComplete',
                'GuideParamChange', 'ConfigurationChange', 'Alert')) {
            $text.Contains("'$eventName'") | Should Be $true
        }
        $text.Contains('$shift -gt 0.25') | Should Be $true
        $text.Contains('$script:InvalidatingEvents.Count -gt 0') |
            Should Be $true
        $text.Contains('UnknownEvent:$eventName') | Should Be $true
        $text.Contains('Test-Phd2Disconnected') | Should Be $true
        $text.Contains('Assert-LockPositionStable') | Should Be $true
    }

    It 'qualifies a contiguous GuideStep stream rather than correction pulses' {
        $text.Contains('FrameSequenceContiguous') | Should Be $true
        $text.Contains('MaximumObservedGuideStepGapSeconds') | Should Be $true
        $text.Contains('GuideStepCoverageQualified = $continuity.Qualified') |
            Should Be $true
        $text.Contains('$expectedSteps * 0.60') | Should Be $false
        $text.Contains('$script:GuideStepWithPulseCount -ge') |
            Should Be $false
    }

    It 'accepts a contiguous cadence-bounded GuideStep stream' {
        $result = Get-GuideStepContinuity `
            ([long[]](100..109)) `
            ([double[]](1..10)) `
            0.0 `
            11.0
        $result.Qualified | Should Be $true
        $result.FrameSequenceContiguous | Should Be $true
    }

    It 'rejects a missing GuideStep frame despite adequate aggregate count' {
        $frames = [long[]](100, 101, 102, 103, 104, 200, 201, 202, 203, 204)
        $result = Get-GuideStepContinuity $frames ([double[]](1..10)) 0.0 11.0
        $result.Qualified | Should Be $false
        $result.FrameSequenceContiguous | Should Be $false
    }

    It 'rejects a long blind interval despite contiguous frame numbers' {
        $times = [double[]](1, 2, 3, 4, 5, 20, 21, 22, 23, 24)
        $result = Get-GuideStepContinuity ([long[]](100..109)) $times 0.0 25.0
        $result.Qualified | Should Be $false
        ($result.MaximumObservedGapSeconds -gt
            $result.MaximumAllowedGapSeconds) | Should Be $true
    }

    It 'samples and seals the complete NINA and PHD2 fixed state' {
        $text.Contains("[ValidateRange(1, 10)]") | Should Be $true
        $text.Contains("StateProbeCadenceSeconds = 5") | Should Be $true
        foreach ($device in @('mount', 'camera', 'filterwheel', 'focuser', 'rotator')) {
            $text.Contains("Invoke-NinaInfo '$device'") | Should Be $true
        }
        foreach ($key in @(
                'targetRaDegrees', 'targetDecDegrees', 'pierSide',
                'rotatorAngleDegrees', 'filter', 'gain', 'offset', 'binning',
                'readoutMode', 'focusPosition', 'coolerSetPointC',
                'trackingMode', 'phd2Profile', 'phd2ExposureMs',
                'phd2AlgorithmStateDigest')) {
            $text.Contains("'$key'") | Should Be $true
        }
        $text.Contains("EvidenceMode = 'ReadOnlySampledStateContinuityWitness'") |
            Should Be $true
        $text.Contains('PHD2ConfigurationContinuityQualified = $true') |
            Should Be $true
        $text.Contains('Write-CreateNewUtf8 $statePath') | Should Be $true
        $text.Contains('StateChangingNinaEndpoints = @()') | Should Be $true
    }

    It 'fails closed on fixed-state changes and cadence gaps' {
        $text.Contains('Test-StateEquivalent') | Should Be $true
        $text.Contains("throw 'A fixed-state sample changed") | Should Be $true
        $text.Contains('[Math]::Min(15.0, 3.0 * $median)') | Should Be $true
        $text.Contains('$Samples.Count -lt 10') | Should Be $true
        $text.Contains('Get-Phd2Configuration') | Should Be $true
        $text.Contains('$finalConfiguration.CanonicalJson -cne') | Should Be $true
    }

    It 'accepts a cadence-bounded sampled state stream' {
        $samples = @()
        foreach ($second in 1, 6, 11, 16, 21, 26, 31, 36, 41, 46) {
            $samples += [pscustomobject]@{ MonotonicSeconds = [double]$second }
        }
        $result = Get-StateContinuity $samples 0.0 47.0
        $result.Qualified | Should Be $true
        $result.MaximumAllowedGapSeconds | Should Be 15.0
    }

    It 'rejects a sampled state blind interval' {
        $samples = @()
        foreach ($second in 1, 6, 11, 16, 21, 41, 46, 51, 56, 61) {
            $samples += [pscustomobject]@{ MonotonicSeconds = [double]$second }
        }
        (Get-StateContinuity $samples 0.0 62.0).Qualified | Should Be $false
    }

    It 'detects a transient state change even when the final state returns' {
        $baseline = [ordered]@{
            targetRaDegrees='100'; targetDecDegrees='20'; pierSide='pierWest'
            rotatorAngleDegrees='0'; filter='Oiii'; gain='100'; offset='50'
            binning='1x1'; readoutMode='0'; focusPosition='12345'
            coolerSetPointC='-10'; coolerOn='true'; trackingMode='Sidereal'
            trackingEnabled='true'; mountConnected='true'; cameraConnected='true'
            filterWheelConnected='true'; focuserConnected='true'; rotatorConnected='true'
            mountSlewing='false'; filterWheelMoving='false'; focuserMoving='false'
            focuserSettling='false'; rotatorMoving='false'; phd2Profile='OAG-L'
            phd2ExposureMs='1500'; phd2AlgorithmStateDigest=('c' * 64)
        }
        $changed = [ordered]@{}
        foreach ($key in $baseline.Keys) { $changed[$key] = $baseline[$key] }
        $changed.filter = 'Ha'
        (Test-StateEquivalent $baseline $baseline) | Should Be $true
        (Test-StateEquivalent $baseline $changed) | Should Be $false
        (Test-StateEquivalent $baseline $baseline) | Should Be $true
    }

    It 'seals evidence without granting PA or movement authority' {
        $text.Contains('EventsSha256 = Get-Sha256 $eventsPath') | Should Be $true
        $text.Contains('GuideStepsSha256 = Get-Sha256 $stepsPath') | Should Be $true
        $text.Contains('GrantsMountMotionAuthority = $false') | Should Be $true
        $text.Contains('GrantsUpasAuthority = $false') | Should Be $true
        $text.Contains('GrantsAbsoluteAccuracyClaim = $false') | Should Be $true
        $text.Contains('4a13cf245d7e485e79533697f87b032b304df952') |
            Should Be $true
    }
}
