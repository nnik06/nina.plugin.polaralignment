Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$toolsRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$toolPath = Join-Path $toolsRoot 'capture_phd2_guided_evidence.ps1'
$tokens = $null
$errors = $null
[Management.Automation.Language.Parser]::ParseFile(
    $toolPath,
    [ref]$tokens,
    [ref]$errors) | Out-Null
if ($errors.Count -gt 0) {
    throw "$toolPath parse errors: $($errors -join '; ')"
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
        $text.Contains('$lockShift -gt 0.25') | Should Be $true
        $text.Contains('$script:InvalidatingEvents.Count -gt 0') |
            Should Be $true
    }

    It 'qualifies coverage from GuideStep events rather than correction pulses' {
        $text.Contains('$script:GuideStepCount -ge') | Should Be $true
        $text.Contains('$expectedSteps * 0.60') | Should Be $true
        $text.Contains('GuideStepCoverageQualified = $coverageQualified') |
            Should Be $true
        $text.Contains('$script:GuideStepWithPulseCount -ge') |
            Should Be $false
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
