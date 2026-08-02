Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$toolsRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$scriptPath = Join-Path $toolsRoot 'analyze_actual_exposure_bracket.ps1'
$programPath = Join-Path $toolsRoot 'TppaQualificationCli\Program.cs'
$policyRoot = Join-Path $toolsRoot 'actual-exposure-policies'
$edgePath = Join-Path $policyRoot 'edgehd925-07-asi2600mm-gain100.template.json'
$gtPath = Join-Path $policyRoot 'gt81iv-08-asi2600mm-gain100.template.json'

$script = [IO.File]::ReadAllText($scriptPath)
$program = [IO.File]::ReadAllText($programPath)
$edge = [IO.File]::ReadAllText($edgePath) | ConvertFrom-Json
$gt = [IO.File]::ReadAllText($gtPath) | ConvertFrom-Json

Describe 'actual 900-second exposure bracket contract' {
    It 'is offline and invokes only copied FITS through pinned ASTAP extract2' {
        $script.Contains('Copy-Item -LiteralPath $item.Source -Destination $copy') |
            Should Be $true
        $script.Contains('& $astap -f $copy -extract2') | Should Be $true
        $script.Contains("ASTAP altered copied FITS bytes") | Should Be $true
        $script.ToLowerInvariant().Contains('upasbridge') | Should Be $false
        $script.ToLowerInvariant().Contains('set_guide_output_enabled') | Should Be $false
        $script.ToLowerInvariant().Contains('slew') | Should Be $false
    }

    It 'requires split controls and hash-bound PHD2 geometry state and policy evidence' {
        $script.Contains('$PreControlFits.Count -lt 5') | Should Be $true
        $script.Contains('$PostControlFits.Count -lt 5') | Should Be $true
        $script.Contains('PolicySourceSha256') | Should Be $true
        $script.Contains('Phd2SummarySha256') | Should Be $true
        $script.Contains('OagGeometryReceiptSha256') | Should Be $true
        $script.Contains('StateReceiptSha256') | Should Be $true
    }

    It 'uses a dedicated non-authoritative CLI receipt command' {
        $program.Contains('"analyze-actual-exposure" => AnalyzeActualExposure(options)') |
            Should Be $true
        $program.Contains('"verify-actual-exposure" => VerifyActualExposure(options)') |
            Should Be $true
        $script.Contains('verify-actual-exposure --manifest $manifestPath --receipt $receiptPath') |
            Should Be $true
        $program.Contains('polarAlignmentInferenceQualified = false') | Should Be $true
        $program.Contains('grantsAbsoluteAccuracyClaim = false') | Should Be $true
        $program.Contains('grantsMotionAuthority = false') | Should Be $true
        $program.Contains('grantsUpasAuthority = false') | Should Be $true
    }

    It 'separates the sampled EdgeHD and undersampled GT81 claim scopes' {
        [Math]::Abs([double]$edge.PixelScaleArcsecondsPerPixel - 0.4715) |
            Should BeLessThan 0.0001
        [Math]::Abs([double]$gt.PixelScaleArcsecondsPerPixel - 2.028) |
            Should BeLessThan 0.001
        [bool]$edge.AbsoluteEccentricityQualified | Should Be $true
        [bool]$gt.AbsoluteEccentricityQualified | Should Be $false
        [int]$edge.MinimumPreControlFrames | Should Be 5
        [int]$edge.MinimumPostControlFrames | Should Be 5
        [int]$gt.MinimumPreControlFrames | Should Be 5
        [int]$gt.MinimumPostControlFrames | Should Be 5
    }
}
