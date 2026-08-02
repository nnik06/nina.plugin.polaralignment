$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scriptPath = Join-Path $repo 'tools\capture_phd2_astap_witness_point.ps1'

Describe 'PHD2 ASTAP witness point acquisition' {
    BeforeAll {
        . $scriptPath -LibraryOnly
    }

    It 'parses a strict synthetic FITS exposure-start header' {
        $path = Join-Path $TestDrive 'point.fits'
        $cards = @(
            'SIMPLE  =                    T',
            "DATE-OBS= '2026-08-01T18:00:00.000Z'",
            'EXPTIME =                 15.0',
            'END'
        ) | ForEach-Object { $_.PadRight(80) }
        $text = ($cards -join '').PadRight(2880)
        [IO.File]::WriteAllBytes($path, [Text.Encoding]::ASCII.GetBytes($text))
        $header = Read-FitsHeader $path
        $header['SIMPLE'] | Should Be $true
        $header['DATE-OBS'] | Should Be '2026-08-01T18:00:00.000Z'
        [double]$header['EXPTIME'] | Should Be 15.0
    }

    It 'parses a solved ASTAP WCS and rejects an unsolved sidecar' {
        $wcs = Join-Path $TestDrive 'point.wcs'
        $ini = Join-Path $TestDrive 'point.ini'
        [IO.File]::WriteAllText($wcs, @'
CRVAL1  = 123.456
CRVAL2  = 45.678
CROTA2  = -12.5
'@)
        [IO.File]::WriteAllText($ini, "PLTSOLVD=T`n")
        $solution = Get-AstapSolution $wcs $ini
        $solution.RightAscensionDegrees | Should Be 123.456
        $solution.DeclinationDegrees | Should Be 45.678
        $solution.PositionAngleDegrees | Should Be -12.5
        $invalidIni = Join-Path $TestDrive 'point-invalid.ini'
        [IO.File]::WriteAllText($invalidIni, "PLTSOLVD=F`n")
        $threw = $false
        try { Get-AstapSolution $wcs $invalidIni | Out-Null } catch { $threw = $true }
        $threw | Should Be $true
    }

    It 'contains fail-closed stationary, guide-output and no-motion contracts' {
        $text = [IO.File]::ReadAllText($scriptPath)
        $text | Should Match "PHD2 must be Stopped"
        $text | Should Match "get_guide_output_enabled"
        $text | Should Match "set_guide_output_enabled"
        $text | Should Match "Mount must be connected, tracking, unparked, and stationary"
        $text | Should Not Match '/equipment/mount/slew/coordinates'
        $text | Should Match "fitsDateObsConvention = 'exposure-start'"
        $text.Contains('$transform.Refraction = $false') | Should Be $true
    }

    It 'builds a blind ASTAP solve without mount-coordinate hints' {
        $arguments = Get-BlindAstapArguments `
            'C:\evidence\point.fits' 2.0 'C:\evidence\point-astap'
        ($arguments -contains '-f') | Should Be $true
        ($arguments -contains '-fov') | Should Be $true
        ($arguments -contains '-ra') | Should Be $false
        ($arguments -contains '-spd') | Should Be $false
        ($arguments -contains '-r') | Should Be $false
        ([IO.File]::ReadAllText($scriptPath)) |
            Should Match "solverHintPolicy = 'blind-no-mount-hint'"
        ([IO.File]::ReadAllText($scriptPath)) |
            Should Match 'schemaVersion = 3'
        ([IO.File]::ReadAllText($scriptPath)) |
            Should Match 'siteLatitudeDegrees = \$SiteLatitudeDegrees'
        ([IO.File]::ReadAllText($scriptPath)) |
            Should Match 'astapFieldOfViewDegrees = \$AstapFieldOfViewDegrees'
    }
}
