Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$toolsRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$tool = Join-Path $toolsRoot 'new_derived_oag_geometry_receipt.ps1'

function Write-TestWcs([string]$Path, [double]$Ra, [double]$Dec,
        [int]$Width, [int]$Height, [double]$ScaleArcseconds,
        [string[]]$Additional = @(),
        [string]$ObservationUtc = '2026-08-02T20:00:00Z') {
    $scaleDegrees = $ScaleArcseconds / 3600.0
    $lines = @(
        'NAXIS   = 2',
        "NAXIS1  = $Width",
        "NAXIS2  = $Height",
        "CTYPE1  = 'RA---TAN'",
        "CTYPE2  = 'DEC--TAN'",
        "DATE-OBS= '$ObservationUtc'",
        ('CRVAL1  = ' + $Ra.ToString('R', [Globalization.CultureInfo]::InvariantCulture)),
        ('CRVAL2  = ' + $Dec.ToString('R', [Globalization.CultureInfo]::InvariantCulture)),
        ('CRPIX1  = ' + (0.5 * ($Width + 1)).ToString('R', [Globalization.CultureInfo]::InvariantCulture)),
        ('CRPIX2  = ' + (0.5 * ($Height + 1)).ToString('R', [Globalization.CultureInfo]::InvariantCulture)),
        ('CD1_1   = ' + (-$scaleDegrees).ToString('R', [Globalization.CultureInfo]::InvariantCulture)),
        'CD1_2   = 0',
        'CD2_1   = 0',
        ('CD2_2   = ' + $scaleDegrees.ToString('R', [Globalization.CultureInfo]::InvariantCulture))
    ) + $Additional
    [IO.File]::WriteAllLines($Path, $lines, [Text.UTF8Encoding]::new($false))
}

Describe 'Derived ASTAP WCS OAG geometry receipt' {
    It 'creates a portable schema-v2 receipt from two strict WCS sources' {
        $bundle = Join-Path $TestDrive 'bundle'
        $sources = Join-Path $bundle 'wcs'
        [IO.Directory]::CreateDirectory($sources) | Out-Null
        $main = Join-Path $sources 'main.wcs'
        $guide = Join-Path $sources 'guide.wcs'
        $output = Join-Path $bundle 'oag-geometry.json'
        Write-TestWcs $main 120.0 30.0 6248 4176 0.4715
        Write-TestWcs $guide 120.0002 30.0001 1920 1080 0.501

        & $tool -MainWcsPath $main -GuideWcsPath $guide -OutputPath $output | Out-Null

        $receipt = [IO.File]::ReadAllText($output) | ConvertFrom-Json
        $receipt.SchemaVersion | Should Be 2
        $receipt.GeometryProvenance | Should Be 'derived-astap-wcs'
        $receipt.MainSolutionSource | Should Be (Join-Path 'wcs' 'main.wcs')
        $receipt.GuideSolutionSource | Should Be (Join-Path 'wcs' 'guide.wcs')
        $receipt.MainScaleModel | Should Be 'CD'
        $receipt.GuideScaleModel | Should Be 'CD'
        $receipt.GuideToFarthestMainCornerUpperBoundPixels | Should BeGreaterThan 0
        $receipt.GrantsMotionAuthority | Should Be $false
        $receipt.GrantsAbsolutePolarAccuracyClaim | Should Be $false
        $bytes = [IO.File]::ReadAllBytes($output)
        ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and
            $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) | Should Be $false
    }

    It 'survives relocation with both source bindings intact' {
        $bundle = Join-Path $TestDrive 'before-move'
        $sources = Join-Path $bundle 'wcs'
        [IO.Directory]::CreateDirectory($sources) | Out-Null
        $main = Join-Path $sources 'main.wcs'
        $guide = Join-Path $sources 'guide.wcs'
        $output = Join-Path $bundle 'oag-geometry.json'
        Write-TestWcs $main 120.0 30.0 6248 4176 0.4715
        Write-TestWcs $guide 120.0002 30.0001 1920 1080 0.501
        & $tool -MainWcsPath $main -GuideWcsPath $guide -OutputPath $output | Out-Null

        $relocated = Join-Path $TestDrive 'after-move'
        [IO.Directory]::Move($bundle, $relocated)
        $movedReceiptPath = Join-Path $relocated 'oag-geometry.json'
        $receipt = [IO.File]::ReadAllText($movedReceiptPath) | ConvertFrom-Json
        $movedMain = Join-Path $relocated $receipt.MainSolutionSource
        $movedGuide = Join-Path $relocated $receipt.GuideSolutionSource

        [IO.File]::Exists($movedMain) | Should Be $true
        [IO.File]::Exists($movedGuide) | Should Be $true
        (Get-FileHash $movedMain -Algorithm SHA256).Hash.ToLowerInvariant() |
            Should Be $receipt.MainSolutionSha256
        (Get-FileHash $movedGuide -Algorithm SHA256).Hash.ToLowerInvariant() |
            Should Be $receipt.GuideSolutionSha256
    }

    It 'rejects distorted WCS without output or temporary residue' {
        $bundle = Join-Path $TestDrive 'distorted'
        [IO.Directory]::CreateDirectory($bundle) | Out-Null
        $main = Join-Path $bundle 'main.wcs'
        $guide = Join-Path $bundle 'guide.wcs'
        $output = Join-Path $bundle 'oag-geometry.json'
        Write-TestWcs $main 120.0 30.0 6248 4176 0.4715 @('PV1_0   = 0')
        Write-TestWcs $guide 120.0 30.0 1920 1080 0.501

        $rejected = $false
        try {
            & $tool -MainWcsPath $main -GuideWcsPath $guide -OutputPath $output |
                Out-Null
        } catch {
            $rejected = $true
        }

        $rejected | Should Be $true
        [IO.File]::Exists($output) | Should Be $false
        @(Get-ChildItem -LiteralPath $bundle -Filter '*.new-*').Count | Should Be 0
    }

    It 'refuses overwrite and preserves the existing receipt bytes' {
        $bundle = Join-Path $TestDrive 'overwrite'
        [IO.Directory]::CreateDirectory($bundle) | Out-Null
        $main = Join-Path $bundle 'main.wcs'
        $guide = Join-Path $bundle 'guide.wcs'
        $output = Join-Path $bundle 'oag-geometry.json'
        Write-TestWcs $main 120.0 30.0 6248 4176 0.4715
        Write-TestWcs $guide 120.0 30.0 1920 1080 0.501
        [IO.File]::WriteAllText($output, 'preserve',
            [Text.UTF8Encoding]::new($false))

        $rejected = $false
        try {
            & $tool -MainWcsPath $main -GuideWcsPath $guide -OutputPath $output |
                Out-Null
        } catch {
            $rejected = $true
        }

        $rejected | Should Be $true
        [IO.File]::ReadAllText($output) | Should Be 'preserve'
        @(Get-ChildItem -LiteralPath $bundle -Filter '*.new-*').Count | Should Be 0
    }

    It 'rejects a mismatched declared source hash' {
        $bundle = Join-Path $TestDrive 'hash'
        [IO.Directory]::CreateDirectory($bundle) | Out-Null
        $main = Join-Path $bundle 'main.wcs'
        $guide = Join-Path $bundle 'guide.wcs'
        $output = Join-Path $bundle 'oag-geometry.json'
        Write-TestWcs $main 120.0 30.0 6248 4176 0.4715
        Write-TestWcs $guide 120.0 30.0 1920 1080 0.501

        $rejected = $false
        try {
            & $tool -MainWcsPath $main -GuideWcsPath $guide -OutputPath $output -MainSolutionSha256 ('0' * 64) |
                Out-Null
        } catch {
            $rejected = $true
        }

        $rejected | Should Be $true
        [IO.File]::Exists($output) | Should Be $false
    }

    It 'rejects main and guide WCS observations more than sixty seconds apart' {
        $bundle = Join-Path $TestDrive 'stale-pair'
        [IO.Directory]::CreateDirectory($bundle) | Out-Null
        $main = Join-Path $bundle 'main.wcs'
        $guide = Join-Path $bundle 'guide.wcs'
        $output = Join-Path $bundle 'oag-geometry.json'
        Write-TestWcs $main 120.0 30.0 6248 4176 0.4715
        Write-TestWcs $guide 120.0 30.0 1920 1080 0.501 `
            -ObservationUtc '2026-08-02T20:02:00Z'

        $rejected = $false
        try {
            & $tool -MainWcsPath $main -GuideWcsPath $guide -OutputPath $output |
                Out-Null
        } catch { $rejected = $true }

        $rejected | Should Be $true
        [IO.File]::Exists($output) | Should Be $false
    }

    It 'produces the same numeric geometry under a comma-decimal culture' {
        $bundle = Join-Path $TestDrive 'culture'
        [IO.Directory]::CreateDirectory($bundle) | Out-Null
        $main = Join-Path $bundle 'main.wcs'
        $guide = Join-Path $bundle 'guide.wcs'
        $output = Join-Path $bundle 'oag-geometry.json'
        Write-TestWcs $main 120.0 30.0 6248 4176 0.4715
        Write-TestWcs $guide 120.0002 30.0001 1920 1080 0.501
        $originalCulture = [Threading.Thread]::CurrentThread.CurrentCulture
        $originalUiCulture = [Threading.Thread]::CurrentThread.CurrentUICulture
        try {
            [Threading.Thread]::CurrentThread.CurrentCulture =
                [Globalization.CultureInfo]::GetCultureInfo('de-DE')
            [Threading.Thread]::CurrentThread.CurrentUICulture =
                [Globalization.CultureInfo]::GetCultureInfo('de-DE')
            & $tool -MainWcsPath $main -GuideWcsPath $guide -OutputPath $output |
                Out-Null
        } finally {
            [Threading.Thread]::CurrentThread.CurrentCulture = $originalCulture
            [Threading.Thread]::CurrentThread.CurrentUICulture = $originalUiCulture
        }

        $receipt = [IO.File]::ReadAllText($output) | ConvertFrom-Json
        [double]$receipt.MainPixelScaleXArcseconds | Should BeGreaterThan 0.47
        [double]$receipt.MainPixelScaleXArcseconds | Should BeLessThan 0.48
    }
}
