Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$toolsRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$legEvaluator = Join-Path $toolsRoot 'ipolar_slew_axis_evaluator.ps1'
$campaignEvaluator = Join-Path $toolsRoot 'ipolar_pier_side_campaign_evaluator.ps1'

Describe 'iPolar slew-axis evaluator' {
    BeforeAll {
        Add-Type -AssemblyName System.Drawing
        $fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('ipolar-axis-' + [guid]::NewGuid().ToString('N'))
        $pwsh = (Get-Process -Id $PID).Path

        function New-AxisFixture([string]$name, [double]$radialNoisePixels, [double]$angleStepRadians = 0.150) {
            $runRoot = Join-Path $fixtureRoot $name
            $frames = Join-Path $runRoot 'frames'
            New-Item -ItemType Directory -Force -Path $frames | Out-Null
            $samples = Join-Path $runRoot 'samples.jsonl'
            $centerX = 750.0
            $centerY = 360.0
            $radii = @(180.0, 230.0, 285.0, 340.0, 395.0, 450.0)
            $phaseOffsets = @(0.0, 0.5, 1.0, 1.5, 2.0, 2.5)
            for ($index = 0; $index -lt 14; $index++) {
                $namePart = '{0:D4}-axis.png' -f ($index + 1)
                $path = Join-Path $frames $namePart
                $bitmap = [Drawing.Bitmap]::new(1295, 735)
                $graphics = [Drawing.Graphics]::FromImage($bitmap)
                try {
                    $graphics.Clear([Drawing.Color]::FromArgb(12, 12, 12))
                    for ($star = 0; $star -lt $radii.Count; $star++) {
                        $noise = if (($index % 2) -eq 0) { $radialNoisePixels } else { -$radialNoisePixels }
                        $radius = $radii[$star] + $noise
                        $angle = $phaseOffsets[$star] + $angleStepRadians * $index
                        $x = $centerX + $radius * [Math]::Cos($angle)
                        $y = $centerY + $radius * [Math]::Sin($angle)
                        $graphics.FillEllipse([Drawing.Brushes]::White, [single]($x - 2), [single]($y - 2), 5, 5)
                    }
                    $bitmap.SetPixel($index + 1, 1, [Drawing.Color]::FromArgb($index + 1, 0, 0))
                    $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
                } finally { $graphics.Dispose(); $bitmap.Dispose() }
                [ordered]@{
                    Index = $index + 1
                    Frame = $namePart
                    FrameSha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
                    CaptureError = $null
                    Mount = [ordered]@{ Slewing = $true; SideOfPier = 'pierWest' }
                } | ConvertTo-Json -Compress -Depth 4 | Add-Content -LiteralPath $samples -Encoding utf8
            }
            return $samples
        }
    }

    AfterAll {
        if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
    }

    It 'qualifies a well-conditioned circular stellar arc below 30 arcsec maximum residual' {
        $samples = New-AxisFixture -name 'stable' -radialNoisePixels 0.05
        $output = Join-Path (Split-Path -Parent $samples) 'axis-evaluation.json'
        & $pwsh -NoProfile -File $legEvaluator -SamplesPath $samples -OutputPath $output | Out-Null
        $result = [IO.File]::ReadAllText($output) | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0) { throw "Stable fixture failed: $($result.Issues -join '; ')" }
        $result.DifferentialAxisStabilityQualified | Should Be $true
        $result.MaximumResidualArcsec | Should BeLessThan 30.0
        $result.JackknifeCenterStandardErrorArcsec | Should BeLessThan 15.0
        $result.LeaveOneOutCenterMaximumArcsec | Should BeLessThan 30.0
        $result.GrantsAbsoluteAccuracyClaim | Should Be $false
    }

    It 'rejects an arc with radial instability beyond the configured bound' {
        $samples = New-AxisFixture -name 'unstable' -radialNoisePixels 2.0
        $output = Join-Path (Split-Path -Parent $samples) 'axis-evaluation.json'
        & $pwsh -NoProfile -File $legEvaluator -SamplesPath $samples -OutputPath $output | Out-Null
        $LASTEXITCODE | Should Be 2
        $result = [IO.File]::ReadAllText($output) | ConvertFrom-Json
        $result.DifferentialAxisStabilityQualified | Should Be $false
        $result.Issues.Count | Should BeGreaterThan 0
    }

    It 'rejects a short arc whose residual is small but fitted center is unstable' {
        $samples = New-AxisFixture -name 'short-center-unstable' -radialNoisePixels 0.05 -angleStepRadians 0.015
        $output = Join-Path (Split-Path -Parent $samples) 'axis-evaluation.json'
        & $pwsh -NoProfile -File $legEvaluator -SamplesPath $samples -OutputPath $output | Out-Null
        $LASTEXITCODE | Should Be 2
        $result = [IO.File]::ReadAllText($output) | ConvertFrom-Json
        $result.DifferentialAxisStabilityQualified | Should Be $false
        ($result.Issues -join ' ') | Should Match 'axis-center'
    }
}

Describe 'iPolar reciprocal pier-side campaign evaluator' {
    BeforeEach {
        $campaignRoot = Join-Path ([IO.Path]::GetTempPath()) ('ipolar-campaign-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Force -Path $campaignRoot | Out-Null
    }

    AfterEach {
        if (Test-Path -LiteralPath $campaignRoot) { Remove-Item -LiteralPath $campaignRoot -Recurse -Force }
    }

    function New-LegResult([string]$name, [double]$x, [double]$y, [string]$side) {
        $path = Join-Path $campaignRoot "$name.json"
        [ordered]@{
            SchemaVersion = 1
            RunId = $name
            SamplesPath = (Join-Path $campaignRoot "$name-samples.jsonl")
            SideOfPier = $side
            DifferentialAxisStabilityQualified = $true
            AxisCenterX = $x
            AxisCenterY = $y
            ArcsecPerPixel = 30.87742981
        } | ConvertTo-Json | Set-Content -LiteralPath $path -Encoding utf8
        return $path
    }

    function New-CampaignManifest($paths) {
        $manifest = Join-Path $campaignRoot 'manifest.json'
        [ordered]@{
            CampaignId = 'synthetic-reciprocal-cycle'
            Legs = @(
                [ordered]@{ Role = 'west-outbound'; ResultPath = $paths[0]; ResultSha256 = (Get-FileHash -LiteralPath $paths[0] -Algorithm SHA256).Hash },
                [ordered]@{ Role = 'west-return'; ResultPath = $paths[1]; ResultSha256 = (Get-FileHash -LiteralPath $paths[1] -Algorithm SHA256).Hash },
                [ordered]@{ Role = 'east-outbound'; ResultPath = $paths[2]; ResultSha256 = (Get-FileHash -LiteralPath $paths[2] -Algorithm SHA256).Hash },
                [ordered]@{ Role = 'east-return'; ResultPath = $paths[3]; ResultSha256 = (Get-FileHash -LiteralPath $paths[3] -Algorithm SHA256).Hash }
            )
        } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifest -Encoding utf8
        return $manifest
    }
    It 'qualifies four reciprocal legs whose fitted axes agree within 30 arcsec' {
        $paths = @(
            (New-LegResult 'west-out' 750.00 360.00 'pierWest'),
            (New-LegResult 'west-back' 750.20 360.10 'pierWest'),
            (New-LegResult 'east-out' 749.85 359.90 'pierEast'),
            (New-LegResult 'east-back' 750.10 360.15 'pierEast')
        )
        $manifest = New-CampaignManifest -paths $paths
        $output = Join-Path $campaignRoot 'campaign.json'
        & $pwsh -NoProfile -File $campaignEvaluator -ManifestPath $manifest -OutputPath $output | Out-Null
        $LASTEXITCODE | Should Be 0
        $result = [IO.File]::ReadAllText($output) | ConvertFrom-Json
        $result.DifferentialPierSideStabilityQualified | Should Be $true
        $result.GrantsUpasAuthority | Should Be $false
    }

    It 'rejects duplicate run evidence even when the fitted centers agree' {
        $shared = New-LegResult 'shared' 750.00 360.00 'pierWest'
        $paths = @($shared, $shared, $shared, $shared)
        $manifest = New-CampaignManifest -paths $paths
        $output = Join-Path $campaignRoot 'campaign.json'
        & $pwsh -NoProfile -File $campaignEvaluator -ManifestPath $manifest -OutputPath $output | Out-Null
        $LASTEXITCODE | Should Be 2
        $result = [IO.File]::ReadAllText($output) | ConvertFrom-Json
        $result.DifferentialPierSideStabilityQualified | Should Be $false
    }

    It 'rejects a leg result changed after its hash was sealed in the manifest' {
        $paths = @(
            (New-LegResult 'west-out' 750.00 360.00 'pierWest'),
            (New-LegResult 'west-back' 750.20 360.10 'pierWest'),
            (New-LegResult 'east-out' 749.85 359.90 'pierEast'),
            (New-LegResult 'east-back' 750.10 360.15 'pierEast')
        )
        $manifest = New-CampaignManifest -paths $paths
        Add-Content -LiteralPath $paths[0] -Value ' ' -Encoding utf8
        $output = Join-Path $campaignRoot 'campaign.json'
        & $pwsh -NoProfile -File $campaignEvaluator -ManifestPath $manifest -OutputPath $output | Out-Null
        $LASTEXITCODE | Should Be 2
        $result = [IO.File]::ReadAllText($output) | ConvertFrom-Json
        $result.DifferentialPierSideStabilityQualified | Should Be $false
        ($result.Issues -join ' ') | Should Match 'hash mismatch'
    }

    It 'rejects four legs whose fitted axes differ by more than 30 arcsec' {
        $paths = @(
            (New-LegResult 'west-out' 750.00 360.00 'pierWest'),
            (New-LegResult 'west-back' 750.10 360.00 'pierWest'),
            (New-LegResult 'east-out' 752.00 360.00 'pierEast'),
            (New-LegResult 'east-back' 752.10 360.00 'pierEast')
        )
        $manifest = New-CampaignManifest -paths $paths
        $output = Join-Path $campaignRoot 'campaign.json'
        & $pwsh -NoProfile -File $campaignEvaluator -ManifestPath $manifest -OutputPath $output | Out-Null
        $LASTEXITCODE | Should Be 2
        $result = [IO.File]::ReadAllText($output) | ConvertFrom-Json
        $result.DifferentialPierSideStabilityQualified | Should Be $false
        $result.Issues.Count | Should BeGreaterThan 0
    }
}
