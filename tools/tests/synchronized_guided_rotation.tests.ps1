Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$toolsRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$runnerPath = Join-Path $toolsRoot 'run_synchronized_guided_rotation.ps1'
$tokens = $null
$errors = $null
[Management.Automation.Language.Parser]::ParseFile(
    $runnerPath,
    [ref]$tokens,
    [ref]$errors) | Out-Null
if ($errors.Count -gt 0) {
    throw "$runnerPath parse errors: $($errors -join '; ')"
}
$text = [IO.File]::ReadAllText($runnerPath)

Describe 'synchronized guided rotation witness contract' {
    It 'combines static main-camera WCS with read-only guided evidence' {
        $text.Contains('run_guarded_static_solve_series.ps1') | Should Be $true
        $text.Contains('capture_phd2_guided_evidence.ps1') | Should Be $true
        $text.Contains('tppa_phd2_supervisor.ps1') | Should Be $false
        $text.Contains('ipolar_slew_capture.ps1') | Should Be $false
    }

    It 'contains no guiding, telescope, or UPAS state-changing command' {
        foreach ($fragment in @(
                '/equipment/mount/slew', '/equipment/mount/home',
                '/equipment/mount/park', 'set_guide_output_enabled',
                'stop_capture', '$J=', 'MoveCloser')) {
            $text.Contains($fragment) | Should Be $false
        }
    }

    It 'inherits fixed-field, clock, atmosphere, and coverage gates' {
        $text.Contains('Assert-FixedMount') | Should Be $true
        $text.Contains('MaximumEquatorialDriftDegrees') | Should Be $true
        $text.Contains('Test-AzimuthWithinLimits') | Should Be $true
        $text.Contains('$MinimumAzimuthDegrees = 270.0') | Should Be $true
        $text.Contains('$MaximumAzimuthDegrees = 10.0') | Should Be $true
        $text.Contains('$MinimumAzimuthDegrees -le $MaximumAzimuthDegrees') |
            Should Be $true
        $text.Contains('Get-ClockProbe') | Should Be $true
        $text.Contains('Qualified100Milliseconds') | Should Be $true
        $text.Contains('AtmosphereObservedUtc') | Should Be $true
        $text.Contains('/equipment/weather') | Should Be $false
        $text.Contains('$minimumFraction = 0.80') | Should Be $true
        $text.Contains('$phd2Rows.Count -eq [int]$phd2Summary.GuideStepCount') |
            Should Be $true
    }

    It 'emits only schema-3 guided witness authority' {
        $text.Contains('SchemaVersion = 3') | Should Be $true
        $text.Contains("EvidenceMode = 'GuidedTrackingRollWitness'") |
            Should Be $true
        $text.Contains('GuidingContinuityQualified =') | Should Be $true
        $text.Contains('GuideOutputContinuouslyEnabled =') | Should Be $true
        $text.Contains('GuideStepCoverageQualified =') | Should Be $true
        $text.Contains('ActualLongExposureArtifactPresent = $false') |
            Should Be $true
        $text.Contains('GrantsMountMotionAuthority = $false') | Should Be $true
        $text.Contains('GrantsUpasAuthority = $false') | Should Be $true
        $text.Contains('GrantsAbsoluteAccuracyClaim = $false') | Should Be $true
    }

    It 'seals every child and telemetry artifact by hash' {
        $text.Contains('Get-Sha256') | Should Be $true
        $text.Contains('Artifacts = $artifacts') | Should Be $true
        $text.Contains('[IO.FileMode]::CreateNew') | Should Be $true
    }
}
