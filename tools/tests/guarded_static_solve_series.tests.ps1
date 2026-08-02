$scriptPath = Join-Path $PSScriptRoot '..\run_guarded_static_solve_series.ps1'
$text = [IO.File]::ReadAllText((Resolve-Path $scriptPath))

Describe 'guarded static solve series safety contract' {
    It 'uses a supported camera capture with a plate-solve result' {
        $text.Contains('/equipment/camera/capture?solve=true') | Should Be $true
        $text.Contains('waitForResult=true') | Should Be $true
        $text.Contains('PlateSolveResult.Success') | Should Be $true
    }

    It 'never commands telescope or polar-alignment movement' {
        $text.Contains('/equipment/mount/slew') | Should Be $false
        $text.Contains('MoveAxis') | Should Be $false
        $text.Contains('UPAS') | Should Be $false
    }

    It 'fails closed on live mount and balcony state' {
        $text.Contains('Assert-SafeMount') | Should Be $true
        $text.Contains('$Mount.Slewing') | Should Be $true
        $text.Contains('$Mount.TrackingEnabled') | Should Be $true
        $text.Contains('$Mount.SideOfPier') | Should Be $true
        $text.Contains('$alt -lt 25.0') | Should Be $true
        $text.Contains('$alt -gt 55.0') | Should Be $true
        $text.Contains('$az -ge 270.0') | Should Be $true
        $text.Contains('$az -le 10.0') | Should Be $true
    }

    It 'records every raw solve before producing offsets' {
        $text.Contains('samples.jsonl') | Should Be $true
        $text.Contains('SolveRaDegreesJ2000') | Should Be $true
        $text.Contains('SolveDecDegreesJ2000') | Should Be $true
        $text.Contains('OffsetsFromFirstSolveArcsec') | Should Be $true
        $text.Contains('SlopeArcsecPerMinute') | Should Be $true
        $text.Contains('ResidualRmsArcsec') | Should Be $true
        $text.Contains('VectorSlopeArcsecPerMinute') | Should Be $true
        $text.Contains('LinearFit') | Should Be $true
        $text.Contains('Get-SignedCircularDegreesDelta') | Should Be $true
        $text.Contains('SolveRaDegreesJ2000)') | Should Be $true
    }

    It 'treats the zero-degree RA boundary as a small circular delta' {
        $match = [regex]::Match(
            $text,
            '(?ms)^function Get-SignedCircularDegreesDelta.*?^}')
        $match.Success | Should Be $true
        . ([scriptblock]::Create($match.Value))

        $forward = Get-SignedCircularDegreesDelta 0.15 359.85
        $reverse = Get-SignedCircularDegreesDelta 359.85 0.15
        [Math]::Abs($forward - 0.30) | Should BeLessThan 0.000001
        [Math]::Abs($reverse + 0.30) | Should BeLessThan 0.000001
        (Get-SignedCircularDegreesDelta 180.0 0.0) | Should Be 180.0
    }
}
