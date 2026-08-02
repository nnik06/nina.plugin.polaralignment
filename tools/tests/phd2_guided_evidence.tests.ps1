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
foreach ($functionName in @('Get-Median', 'Get-GuideStepContinuity')) {
    $functionAst = $ast.Find({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq $functionName
    }, $true)
    if ($null -eq $functionAst) { throw "Missing function $functionName" }
    . ([scriptblock]::Create($functionAst.Extent.Text))
}
$text = [IO.File]::ReadAllText($toolPath)

Describe 'read-only guided PHD2 evidence collector contract' {
    It 'uses a second client with a strict read-only RPC allow-list' {
        $text.Contains('Connect-Phd2') | Should Be $true
        foreach ($method in @(
                'get_app_state', 'get_connected', 'get_calibrated',
                'get_guide_output_enabled', 'get_exposure',
                'get_lock_position', 'get_pixel_scale')) {
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
