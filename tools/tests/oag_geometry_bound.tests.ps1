Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$toolsRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$tool = Join-Path $toolsRoot 'calculate_oag_geometry_bound.ps1'
$common = @{
    MainCenterRightAscensionDegrees = 120.0
    MainCenterDeclinationDegrees = 30.0
    GuideCenterRightAscensionDegrees = 120.0
    GuideCenterDeclinationDegrees = 30.0
    MainPixelScaleArcseconds = 0.471
    GuidePixelScaleArcseconds = 0.501
    MainSolutionSource = 'main-sample.json'
    GuideSolutionSource = 'guide.wcs'
}

Describe 'OAG guide-to-main-corner geometry bound' {
    It 'uses the full guide sensor when no lock position is supplied' {
        $result = & $tool @common
        $expectedMainHalfDiagonal = 0.5 * [Math]::Sqrt(6248 * 6248 + 4176 * 4176)
        $expectedGuideRadius = 0.5 * [Math]::Sqrt(1920 * 1920 + 1080 * 1080)

        $result.CenterSeparationArcseconds | Should BeLessThan 0.01
        [Math]::Abs($result.MainHalfDiagonalPixels - $expectedMainHalfDiagonal) | Should BeLessThan 0.000001
        [Math]::Abs($result.GuideRadialPixels - $expectedGuideRadius) | Should BeLessThan 0.000001
        $result.GuideRadialEvidenceKind | Should Be 'full-guide-sensor-upper-bound'
        $result.GuideToFarthestMainCornerUpperBoundPixels | Should BeGreaterThan $expectedMainHalfDiagonal
    }

    It 'tightens the bound when the measured guide lock offset is supplied' {
        $full = & $tool @common
        $locked = & $tool @common `
            -GuideLockOffsetXFromCenterPixels 100 `
            -GuideLockOffsetYFromCenterPixels -50

        $locked.GuideRadialEvidenceKind | Should Be 'measured-lock-offset-upper-bound'
        [Math]::Abs($locked.GuideRadialPixels - [Math]::Sqrt(12500.0)) | Should BeLessThan 0.000001
        $locked.GuideToFarthestMainCornerUpperBoundPixels | Should BeLessThan $full.GuideToFarthestMainCornerUpperBoundPixels
    }

    It 'computes spherical centre separation without depending on position-angle conventions' {
        $offset = @{} + $common
        $offset.GuideCenterRightAscensionDegrees = 120.1
        $result = & $tool @offset
        $expectedArcseconds = 0.1 * [Math]::Cos(30.0 * [Math]::PI / 180.0) * 3600.0

        [Math]::Abs($result.CenterSeparationArcseconds - $expectedArcseconds) | Should BeLessThan 0.2
        $result.UsesOrientationConvention | Should Be $false
    }

    It 'requires lock offsets as a pair and validates optional hashes' {
        $pairRejected = $false
        try {
            & $tool @common -GuideLockOffsetXFromCenterPixels 10 | Out-Null
        } catch {
            $pairRejected = $true
        }
        $pairRejected | Should Be $true
        $hashRejected = $false
        try {
            & $tool @common -MainSolutionSha256 xyz | Out-Null
        } catch {
            $hashRejected = $true
        }
        $hashRejected | Should Be $true
        $valid = & $tool @common `
            -MainSolutionSha256 ('a' * 64) `
            -GuideSolutionSha256 ('B' * 64)
        $valid.MainSolutionSha256 | Should Be ('a' * 64)
        $valid.GuideSolutionSha256 | Should Be ('b' * 64)
    }

    It 'writes a create-new receipt with automatically sealed source artifacts' {
        $main = Join-Path $TestDrive 'main-solution.json'
        $guide = Join-Path $TestDrive 'guide-solution.wcs'
        $output = Join-Path $TestDrive 'geometry-receipt.json'
        [IO.File]::WriteAllText($main, 'main', [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($guide, 'guide', [Text.UTF8Encoding]::new($false))
        $sealed = @{} + $common
        $sealed.MainSolutionSource = $main
        $sealed.GuideSolutionSource = $guide

        & $tool @sealed -OutputPath $output | Out-Null

        $receipt = [IO.File]::ReadAllText($output) | ConvertFrom-Json
        $receipt.MainSolutionSource | Should Be ([IO.Path]::GetFileName($main))
        $receipt.GuideSolutionSource | Should Be ([IO.Path]::GetFileName($guide))
        $receipt.MainSolutionSha256 | Should Be `
            (Get-FileHash $main -Algorithm SHA256).Hash.ToLowerInvariant()
        $receipt.GuideSolutionSha256 | Should Be `
            (Get-FileHash $guide -Algorithm SHA256).Hash.ToLowerInvariant()
        $receipt.GeometryProvenance | Should Be 'operator-attested-diagnostic'
        $receipt.FormulaVersion | Should Be `
            'orientation-independent-spherical-triangle-upper-bound/v1-diagnostic'
    }

    It 'refuses to overwrite a sealed geometry receipt' {
        $main = Join-Path $TestDrive 'overwrite-main.json'
        $guide = Join-Path $TestDrive 'overwrite-guide.wcs'
        $output = Join-Path $TestDrive 'existing-receipt.json'
        [IO.File]::WriteAllText($main, 'main', [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($guide, 'guide', [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($output, 'preserve', [Text.UTF8Encoding]::new($false))
        $sealed = @{} + $common
        $sealed.MainSolutionSource = $main
        $sealed.GuideSolutionSource = $guide

        $rejected = $false
        try {
            & $tool @sealed -OutputPath $output | Out-Null
        } catch {
            $rejected = $true
        }
        $rejected | Should Be $true
        [IO.File]::ReadAllText($output) | Should Be 'preserve'
    }

    It 'rejects a declared source hash that does not match sealed bytes' {
        $main = Join-Path $TestDrive 'hash-main.json'
        $guide = Join-Path $TestDrive 'hash-guide.wcs'
        $output = Join-Path $TestDrive 'hash-receipt.json'
        [IO.File]::WriteAllText($main, 'main', [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($guide, 'guide', [Text.UTF8Encoding]::new($false))
        $sealed = @{} + $common
        $sealed.MainSolutionSource = $main
        $sealed.GuideSolutionSource = $guide

        $rejected = $false
        try {
            & $tool @sealed -MainSolutionSha256 ('0' * 64) `
                -OutputPath $output | Out-Null
        } catch {
            $rejected = $true
        }
        $rejected | Should Be $true
        Test-Path -LiteralPath $output | Should Be $false
    }

    It 'does not grant movement or absolute-accuracy authority' {
        $result = & $tool @common
        $result.GrantsMotionAuthority | Should Be $false
        $result.GrantsAbsolutePolarAccuracyClaim | Should Be $false
    }
}
