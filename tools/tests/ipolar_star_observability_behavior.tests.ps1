Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$toolsRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$gatePath = Join-Path $toolsRoot 'ipolar_star_observability_gate.ps1'

Describe 'iPolar star observability behavior' {
    BeforeAll {
        Add-Type -AssemblyName System.Drawing
        $fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('ipolar-star-gate-' + [guid]::NewGuid().ToString('N'))
        $pwsh = (Get-Process -Id $PID).Path
    }

    AfterAll {
        if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
    }

    It 'accepts fresh frames containing stable compact neutral stars' {
        $runRoot = Join-Path $fixtureRoot 'stars'
        New-Item -ItemType Directory -Force -Path (Join-Path $runRoot 'frames') | Out-Null
        $samples = Join-Path $runRoot 'samples.jsonl'
        $locations = @(@(320, 120), @(450, 210), @(610, 330), @(820, 180), @(1010, 480), @(1160, 610))
        for ($index = 1; $index -le 5; $index++) {
            $name = '{0:D4}-stars.png' -f $index
            $path = Join-Path (Join-Path $runRoot 'frames') $name
            $bitmap = [Drawing.Bitmap]::new(1295, 735)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([Drawing.Color]::FromArgb(12 + $index, 12 + $index, 12 + $index))
                foreach ($location in $locations) {
                    $graphics.FillEllipse([Drawing.Brushes]::White, $location[0] - 2, $location[1] - 2, 5, 5)
                }
                $bitmap.SetPixel($index, 1, [Drawing.Color]::FromArgb($index, 0, 0))
                $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
            } finally { $graphics.Dispose(); $bitmap.Dispose() }
            [ordered]@{ Frame = $name; FrameSha256 = (Get-FileHash $path -Algorithm SHA256).Hash; CaptureError = $null } |
                ConvertTo-Json -Compress | Add-Content -LiteralPath $samples -Encoding utf8
        }
        & $pwsh -NoProfile -File $gatePath -SamplesPath $samples -MinimumFrames 5 -MinimumStableStars 5 | Out-Null
        $LASTEXITCODE | Should Be 0
    }

    It 'rejects fresh but starless dawn-gradient frames' {
        $runRoot = Join-Path $fixtureRoot 'gradient'
        New-Item -ItemType Directory -Force -Path (Join-Path $runRoot 'frames') | Out-Null
        $samples = Join-Path $runRoot 'samples.jsonl'
        for ($index = 1; $index -le 5; $index++) {
            $name = '{0:D4}-gradient.png' -f $index
            $path = Join-Path (Join-Path $runRoot 'frames') $name
            $bitmap = [Drawing.Bitmap]::new(1295, 735)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            $brush = [Drawing.Drawing2D.LinearGradientBrush]::new(
                [Drawing.Point]::new(0, 0), [Drawing.Point]::new(0, 735),
                [Drawing.Color]::FromArgb(80 + $index, 80 + $index, 80 + $index),
                [Drawing.Color]::FromArgb(200 + $index, 200 + $index, 200 + $index))
            try {
                $graphics.FillRectangle($brush, 0, 0, 1295, 735)
                $bitmap.SetPixel($index, 1, [Drawing.Color]::FromArgb($index, 0, 0))
                $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
            } finally { $brush.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
            [ordered]@{ Frame = $name; FrameSha256 = (Get-FileHash $path -Algorithm SHA256).Hash; CaptureError = $null } |
                ConvertTo-Json -Compress | Add-Content -LiteralPath $samples -Encoding utf8
        }
        & $pwsh -NoProfile -File $gatePath -SamplesPath $samples -MinimumFrames 5 -MinimumStableStars 5 | Out-Null
        $LASTEXITCODE | Should Be 2
    }

    It 'rejects stable compact features confined to the title bar' {
        $runRoot = Join-Path $fixtureRoot 'title-bar'
        New-Item -ItemType Directory -Force -Path (Join-Path $runRoot 'frames') | Out-Null
        $samples = Join-Path $runRoot 'samples.jsonl'
        for ($index = 1; $index -le 5; $index++) {
            $name = '{0:D4}-title-bar.png' -f $index
            $path = Join-Path (Join-Path $runRoot 'frames') $name
            $bitmap = [Drawing.Bitmap]::new(1295, 735)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([Drawing.Color]::FromArgb(12, 12, 12))
                foreach ($x in 260..1160 | Where-Object { ($_ - 260) % 20 -eq 0 }) {
                    $graphics.FillEllipse([Drawing.Brushes]::White, $x - 2, 12, 5, 5)
                }
                $bitmap.SetPixel($index, 1, [Drawing.Color]::FromArgb($index, 0, 0))
                $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
            } finally { $graphics.Dispose(); $bitmap.Dispose() }
            [ordered]@{ Frame = $name; FrameSha256 = (Get-FileHash $path -Algorithm SHA256).Hash; CaptureError = $null } |
                ConvertTo-Json -Compress | Add-Content -LiteralPath $samples -Encoding utf8
        }
        & $pwsh -NoProfile -File $gatePath -SamplesPath $samples -MinimumFrames 5 -MinimumStableStars 5 | Out-Null
        $LASTEXITCODE | Should Be 2
    }

    It 'rejects stable compact features confined to a horizontal viewport border' {
        $runRoot = Join-Path $fixtureRoot 'viewport-border'
        New-Item -ItemType Directory -Force -Path (Join-Path $runRoot 'frames') | Out-Null
        $samples = Join-Path $runRoot 'samples.jsonl'
        for ($index = 1; $index -le 5; $index++) {
            $name = '{0:D4}-viewport-border.png' -f $index
            $path = Join-Path (Join-Path $runRoot 'frames') $name
            $bitmap = [Drawing.Bitmap]::new(1295, 735)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([Drawing.Color]::FromArgb(12, 12, 12))
                foreach ($x in 260..1160 | Where-Object { ($_ - 260) % 20 -eq 0 }) {
                    $graphics.FillEllipse([Drawing.Brushes]::White, $x - 2, 78, 5, 5)
                }
                $bitmap.SetPixel($index, 1, [Drawing.Color]::FromArgb($index, 0, 0))
                $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
            } finally { $graphics.Dispose(); $bitmap.Dispose() }
            [ordered]@{ Frame = $name; FrameSha256 = (Get-FileHash $path -Algorithm SHA256).Hash; CaptureError = $null } |
                ConvertTo-Json -Compress | Add-Content -LiteralPath $samples -Encoding utf8
        }
        & $pwsh -NoProfile -File $gatePath -SamplesPath $samples -MinimumFrames 5 -MinimumStableStars 5 | Out-Null
        $LASTEXITCODE | Should Be 2
    }

    It 'rejects stable compact features confined to a vertical viewport border' {
        $runRoot = Join-Path $fixtureRoot 'vertical-border'
        New-Item -ItemType Directory -Force -Path (Join-Path $runRoot 'frames') | Out-Null
        $samples = Join-Path $runRoot 'samples.jsonl'
        $locations = @(@(650, 120), @(650, 220), @(650, 320), @(650, 420), @(650, 520), @(650, 620))
        for ($index = 1; $index -le 5; $index++) {
            $name = '{0:D4}-vertical-border.png' -f $index
            $path = Join-Path (Join-Path $runRoot 'frames') $name
            $bitmap = [Drawing.Bitmap]::new(1295, 735)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([Drawing.Color]::FromArgb(12, 12, 12))
                foreach ($location in $locations) {
                    $graphics.FillEllipse([Drawing.Brushes]::White, $location[0] - 2, $location[1] - 2, 5, 5)
                }
                $bitmap.SetPixel($index, 1, [Drawing.Color]::FromArgb($index, 0, 0))
                $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
            } finally { $graphics.Dispose(); $bitmap.Dispose() }
            [ordered]@{ Frame = $name; FrameSha256 = (Get-FileHash $path -Algorithm SHA256).Hash; CaptureError = $null } |
                ConvertTo-Json -Compress | Add-Content -LiteralPath $samples -Encoding utf8
        }
        & $pwsh -NoProfile -File $gatePath -SamplesPath $samples -MinimumFrames 5 -MinimumStableStars 5 | Out-Null
        $LASTEXITCODE | Should Be 2
    }

    It 'rejects stable compact features confined to a diagonal line' {
        $runRoot = Join-Path $fixtureRoot 'diagonal-line'
        New-Item -ItemType Directory -Force -Path (Join-Path $runRoot 'frames') | Out-Null
        $samples = Join-Path $runRoot 'samples.jsonl'
        $locations = @(@(300, 100), @(450, 200), @(600, 300), @(750, 400), @(900, 500), @(1050, 600))
        for ($index = 1; $index -le 5; $index++) {
            $name = '{0:D4}-diagonal-line.png' -f $index
            $path = Join-Path (Join-Path $runRoot 'frames') $name
            $bitmap = [Drawing.Bitmap]::new(1295, 735)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([Drawing.Color]::FromArgb(12, 12, 12))
                foreach ($location in $locations) {
                    $graphics.FillEllipse([Drawing.Brushes]::White, $location[0] - 2, $location[1] - 2, 5, 5)
                }
                $bitmap.SetPixel($index, 1, [Drawing.Color]::FromArgb($index, 0, 0))
                $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
            } finally { $graphics.Dispose(); $bitmap.Dispose() }
            [ordered]@{ Frame = $name; FrameSha256 = (Get-FileHash $path -Algorithm SHA256).Hash; CaptureError = $null } |
                ConvertTo-Json -Compress | Add-Content -LiteralPath $samples -Encoding utf8
        }
        & $pwsh -NoProfile -File $gatePath -SamplesPath $samples -MinimumFrames 5 -MinimumStableStars 5 | Out-Null
        $LASTEXITCODE | Should Be 2
    }
}
