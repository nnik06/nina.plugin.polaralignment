Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$toolsRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$runnerPath = Join-Path $toolsRoot 'run_synchronized_pa_discriminator.ps1'
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

Describe 'synchronized PA discriminator contract' {
    It 'starts only existing passive acquisition tools' {
        $text.Contains("ipolar_slew_capture.ps1") | Should Be $true
        $text.Contains("run_guarded_static_solve_series.ps1") | Should Be $true
        $text.Contains("tppa_phd2_supervisor.ps1") | Should Be $true
        $text.Contains('-Mode Phd2Drift') | Should Be $true
        $text.Contains('-RequirePdaNearPole `$false') | Should Be $true
        $text.Contains('-Phd2CaptureAttempts 1') | Should Be $true
    }

    It 'contains no telescope or UPAS movement command' {
        $text.Contains('/equipment/mount/slew') | Should Be $false
        $text.Contains('/equipment/mount/home') | Should Be $false
        $text.Contains('/equipment/mount/park') | Should Be $false
        $text.Contains('MoveCloser') | Should Be $false
        $text.Contains('$J=') | Should Be $false
    }

    It 'fails closed on fixed mount geometry and pier side' {
        $text.Contains('Assert-FixedMount') | Should Be $true
        $text.Contains('$Mount.Slewing') | Should Be $true
        $text.Contains('$Mount.TrackingEnabled') | Should Be $true
        $text.Contains('$Mount.SideOfPier') | Should Be $true
        $text.Contains('MaximumEquatorialDriftDegrees') | Should Be $true
        $text.Contains('MinimumAzimuthDegrees') | Should Be $true
        $text.Contains('MaximumAltitudeDegrees') | Should Be $true
        $text.Contains('Get-SignedCircularDegreesDelta') | Should Be $true
    }

    It 'records a shared UTC and monotonic timeline' {
        $text.Contains('[Diagnostics.Stopwatch]::StartNew()') | Should Be $true
        $text.Contains('MonotonicElapsedSeconds') | Should Be $true
        $text.Contains('[DateTime]::UtcNow') | Should Be $true
        $text.Contains('mount-telemetry.jsonl') | Should Be $true
        $text.Contains('Get-ClockProbe') | Should Be $true
        $text.Contains('Qualified100Milliseconds') | Should Be $true
    }

    It 'requires explicit atmosphere provenance without connecting Weather' {
        $text.Contains('AtmosphereObservedUtc') | Should Be $true
        $text.Contains('AtmosphereSource') | Should Be $true
        $text.Contains('PressureHpa') | Should Be $true
        $text.Contains('RelativeHumidityPercent') | Should Be $true
        $text.Contains('/equipment/weather') | Should Be $false
        $text.Contains('[DateTimeOffset]$AtmosphereObservedUtc') | Should Be $true
        $text.Contains('$AtmosphereObservedUtc.Offset -ne [TimeSpan]::Zero') | Should Be $true
        $text.Contains('$AtmosphereObservedUtc.UtcDateTime.ToString') | Should Be $true
    }

    It 'requires structural coverage and immutable artifact hashes' {
        $text.Contains('$minimumFraction = 0.80') | Should Be $true
        $text.Contains('CoverageQualified') | Should Be $true
        $text.Contains('Get-Sha256') | Should Be $true
        $text.Contains('[IO.FileMode]::CreateNew') | Should Be $true
        $text.Contains('Artifacts = $artifacts') | Should Be $true
    }

    It 'cannot grant movement or absolute-accuracy authority' {
        $text.Contains('GrantsMountMotionAuthority = $false') | Should Be $true
        $text.Contains('GrantsUpasAuthority = $false') | Should Be $true
        $text.Contains('GrantsAbsoluteAccuracyClaim = $false') | Should Be $true
        $text.Contains("processed-window-capture-not-raw-sensor") | Should Be $true
    }
        $text.Contains("EvidenceMode = 'PassiveUnguidedTracking'") | Should Be $true
}
