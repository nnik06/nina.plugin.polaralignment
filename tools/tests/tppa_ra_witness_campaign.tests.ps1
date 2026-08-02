$ErrorActionPreference = 'Stop'

Describe 'TPPA RA witness campaign' {
    BeforeAll {
        $scriptPath = Join-Path $PSScriptRoot '..\run_guarded_tppa_ra_witness_campaign.ps1'
        $slewPath = Join-Path $PSScriptRoot '..\guarded_equatorial_witness_slew.ps1'
        . $scriptPath -LibraryOnly
        $scriptText = [IO.File]::ReadAllText($scriptPath)
        $slewText = [IO.File]::ReadAllText($slewPath)
    }

    It 'preflights every A/B/C/A leg at one-degree-or-finer spacing' {
        $mount = [pscustomobject]@{
            SiderealTime = 0.0
            SiteLatitude = 25.0
        }
        $script:ExpectedPierSide = 'pierEast'
        $start = [DateTime]::SpecifyKind([DateTime]'2026-08-01T20:00:00', 'Utc')
        $plan = New-WitnessTrajectoryPreflight $mount 355.0 60.0 22.5 $false $start
        $plan.IsSafe | Should Be $true
        $plan.Samples.Count | Should BeGreaterThan 45
        $plan.TotalArcDegrees | Should Be 45.0
        $plan.MaximumSampleStepDegrees | Should Be 1.0
        @($plan.Samples | Select-Object -ExpandProperty SegmentId -Unique).Count | Should Be 7
        @($plan.Samples | Where-Object { -not $_.PredictedPierSide }).Count | Should Be 0
        $plan.CaptureStartSeconds[1] | Should Be 110.0
    }

    It 'rejects a trajectory that leaves the north balcony sector' {
        $mount = [pscustomobject]@{
            SiderealTime = 12.0
            SiteLatitude = 25.0
        }
        $script:ExpectedPierSide = 'pierEast'
        $start = [DateTime]::SpecifyKind([DateTime]'2026-08-01T20:00:00', 'Utc')
        $plan = New-WitnessTrajectoryPreflight $mount 0.0 0.0 25.0 $true $start
        $plan.IsSafe | Should Be $false
        $plan.Issues.Count | Should BeGreaterThan 0
    }

    It 'keeps motion and completion authority out of the campaign' {
        $scriptText.Contains('grantsUpasAuthority=$false') | Should Be $true
        $scriptText.Contains('grantsCompletionAuthority=$false') | Should Be $true
        $scriptText.Contains('grantsAbsoluteAccuracyClaim=$false') | Should Be $true
        $scriptText.Contains('create-witness-request') | Should Be $true
        $scriptText.Contains('validate-witness-outcome') | Should Be $true
        $scriptText.Contains('realized-preflight.json') | Should Be $true
        $scriptText.Contains('Wait-GuardedSlewProcess $cleanup') | Should Be $true
        $scriptText.Contains('/equipment/mount/slew/stop') | Should Be $true
        $scriptText.Contains('Stop-MountAndVerifyIdle') | Should Be $true
        $scriptText.Contains('Wait-GuardedWitnessOutcome') | Should Be $true
        $scriptText.Contains('Assert-SkyModelMatchesMount') | Should Be $true
        $scriptText.Contains('Stop-Process -Id $service.Id -Force') | Should Be $false
        $scriptText.Contains('left alive so capture cleanup and PHD2 restoration can finish') | Should Be $true
        $scriptText.Contains('Get-Phd2ControlSnapshot') | Should Be $true
        $scriptText.Contains('Set-Phd2GuideOutputAndVerify') | Should Be $true
        $scriptText.Contains('guideOutputRestored=$guideOutputRestored') | Should Be $true
        $scriptText.Contains('/equipment/switch') | Should Be $false
        $slewText.Contains('waitForResult=true') | Should Be $true
        $slewText.Contains('grantsUpasAuthority = $false') | Should Be $true
    }

    It 'rejects a mount horizontal position that disagrees with the RA/LST model' {
        $mount = [pscustomobject]@{
            RightAscension = 0.0
            Declination = 25.0
            SiteLatitude = 25.0
            SiderealTime = 0.0
            Azimuth = 90.0
            Altitude = 10.0
        }
        $threw = $false
        try { Assert-SkyModelMatchesMount $mount } catch { $threw = $true }
        $threw | Should Be $true
    }

    It 'verifies idle after issuing an emergency stop' {
        Mock Invoke-RestMethod { [pscustomobject]@{ Success=$true } }
        Mock Get-MountInfo {
            [pscustomobject]@{
                Connected=$true; Slewing=$false; SideOfPier='pierEast'
                Azimuth=0.0; Altitude=45.0
            }
        }
        $trajectory = Join-Path $TestDrive 'stop-trajectory.jsonl'
        { Stop-MountAndVerifyIdle 'test-stop' $trajectory } | Should Not Throw
        Assert-MockCalled Invoke-RestMethod 1 -Exactly
        Assert-MockCalled Get-MountInfo 1 -Exactly
    }

    It 'stops and rejects unexpected motion during the capture dwell' {
        $script:ExpectedPierSide = 'pierEast'
        Mock Get-MountInfo {
            [pscustomobject]@{
                Connected=$true; Slewing=$true; SideOfPier='pierEast'
                Azimuth=0.0; Altitude=45.0
            }
        }
        Mock Stop-MountAndVerifyIdle { }
        $service = [pscustomobject]@{ HasExited=$false }
        $outcome = Join-Path $TestDrive 'missing-outcome.json'
        $trajectory = Join-Path $TestDrive 'capture-trajectory.jsonl'
        $threw = $false
        try {
            Wait-GuardedWitnessOutcome $service $outcome (
                [DateTime]::UtcNow.AddSeconds(1)) 'test-capture' $trajectory
        } catch { $threw = $true }
        $threw | Should Be $true
        Assert-MockCalled Stop-MountAndVerifyIdle 1 -Exactly
    }
}
