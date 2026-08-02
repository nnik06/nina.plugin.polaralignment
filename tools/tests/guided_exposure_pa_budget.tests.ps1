Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$toolsRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$tool = Join-Path $toolsRoot 'calculate_guided_exposure_pa_budget.ps1'

Describe 'guided-exposure PA field-rotation budget' {
    It 'bounds a three-arcminute 900-second OAG exposure below half a pixel at 8000 pixels' {
        $result = & $tool `
            -ExposureSeconds 900 `
            -PolarErrorArcminutes 3 `
            -GuideToFarthestCornerPixels 8000 `
            -AllowedSmearPixels 0.5 `
            -ImagingPixelScaleArcseconds 0.471

        $result.Model | Should Be 'conservative-guided-field-rotation-upper-bound'
        $result.FieldRotationArcseconds | Should BeGreaterThan 11.80
        $result.FieldRotationArcseconds | Should BeLessThan 11.82
        $result.MaximumSmearPixels | Should BeGreaterThan 0.45
        $result.MaximumSmearPixels | Should BeLessThan 0.47
        $result.MaximumSmearArcseconds | Should BeLessThan 0.22
        $result.MaximumPolarErrorArcminutesForAllowedSmear | Should BeGreaterThan 3.27
        $result.MaximumPolarErrorArcminutesForAllowedSmear | Should BeLessThan 3.28
    }

    It 'scales monotonically with exposure, radius, and polar error' {
        $base = & $tool -ExposureSeconds 900 -PolarErrorArcminutes 3 `
            -GuideToFarthestCornerPixels 8000
        $longer = & $tool -ExposureSeconds 1800 -PolarErrorArcminutes 3 `
            -GuideToFarthestCornerPixels 8000
        $farther = & $tool -ExposureSeconds 900 -PolarErrorArcminutes 3 `
            -GuideToFarthestCornerPixels 9000
        $worse = & $tool -ExposureSeconds 900 -PolarErrorArcminutes 4 `
            -GuideToFarthestCornerPixels 8000

        $longer.MaximumSmearPixels | Should BeGreaterThan $base.MaximumSmearPixels
        $farther.MaximumSmearPixels | Should BeGreaterThan $base.MaximumSmearPixels
        $worse.MaximumSmearPixels | Should BeGreaterThan $base.MaximumSmearPixels
    }

    It 'labels the result as an operational bound rather than absolute PA truth' {
        $result = & $tool -ExposureSeconds 900 -PolarErrorArcminutes 3 `
            -GuideToFarthestCornerPixels 8000
        $result.AssumesGuideOutputRemovesTranslation | Should Be $true
        $result.GrantsAbsolutePolarAccuracyClaim | Should Be $false
    }
}
