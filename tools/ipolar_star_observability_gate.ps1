param(
    [Parameter(Mandatory = $true)]
    [string]$SamplesPath,
    [ValidateRange(3, 100)]
    [int]$MinimumFrames = 5,
    [ValidateRange(1, 100)]
    [int]$MinimumStableStars = 5,
    [ValidateRange(1.0, 100.0)]
    [double]$MinimumLocalContrast = 12.0,
    [ValidateRange(0.25, 10.0)]
    [double]$MaximumMatchDistancePixels = 3.0,
    [ValidateRange(0.1, 10.0)]
    [double]$MaximumRmsScatterPixels = 1.5,
    [ValidateRange(5.0, 500.0)]
    [double]$MinimumStableVerticalSpanPixels = 40.0,
    [ValidateRange(5.0, 500.0)]
    [double]$MinimumStableHorizontalSpanPixels = 40.0,
    [ValidateRange(1.0, 500.0)]
    [double]$MinimumStableMinorAxisRmsPixels = 20.0,
    [ValidateRange(1.0, 1000.0)]
    [double]$MaximumStableAnisotropyRatio = 20.0,
    [string]$OutputPath = ''
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
if (-not ('IPolarStarGate' -as [type])) {
    $compilerReferences = @(
        @([AppDomain]::CurrentDomain.GetAssemblies() |
            Where-Object { -not $_.IsDynamic -and -not [string]::IsNullOrWhiteSpace($_.Location) } |
            ForEach-Object { $_.Location })
        (Join-Path $PSHOME 'System.Private.CoreLib.dll')
        [Drawing.Bitmap].Assembly.Location
        [Drawing.Rectangle].Assembly.Location
        (Join-Path $PSHOME 'System.Private.Windows.GdiPlus.dll')
        (Join-Path $PSHOME 'System.Private.Windows.Core.dll')
    ) | Select-Object -Unique
    Add-Type -ReferencedAssemblies $compilerReferences -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

public sealed class IPolarStarPoint {
    public double X { get; set; }
    public double Y { get; set; }
    public double Contrast { get; set; }
}

public static class IPolarStarGate {
    public static IPolarStarPoint[] Find(string path, int cropLeft, int cropTop, double minimumContrast) {
        using (var source = new Bitmap(path))
        using (var image = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format24bppRgb)) {
            int width = image.Width;
            int height = image.Height;
            var data = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var bytes = new byte[Math.Abs(data.Stride) * height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            image.UnlockBits(data);

            Func<int, int, double> luminance = (x, y) => {
                int offset = y * data.Stride + x * 3;
                double b = bytes[offset];
                double g = bytes[offset + 1];
                double r = bytes[offset + 2];
                if (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) > 45.0) return -1.0;
                return 0.2126 * r + 0.7152 * g + 0.0722 * b;
            };

            var accepted = new IPolarStarPoint[100];
            int acceptedCount = 0;
            for (int y = Math.Max(cropTop, 10); y < height - 10; y += 2) {
                for (int x = Math.Max(cropLeft, 10); x < width - 10; x += 2) {
                    double center = luminance(x, y);
                    if (center < 40.0) continue;
                    double background = 0.0;
                    int count = 0;
                    int[] dx = { -8, 8, 0, 0, -6, -6, 6, 6 };
                    int[] dy = { 0, 0, -8, 8, -6, 6, -6, 6 };
                    for (int i = 0; i < dx.Length; i++) {
                        double sample = luminance(x + dx[i], y + dy[i]);
                        if (sample >= 0.0) { background += sample; count++; }
                    }
                    if (count < 6) continue;
                    double contrast = center - background / count;
                    if (contrast < minimumContrast) continue;

                    bool localMaximum = true;
                    for (int yy = -2; yy <= 2 && localMaximum; yy++) {
                        for (int xx = -2; xx <= 2; xx++) {
                            if ((xx != 0 || yy != 0) && luminance(x + xx, y + yy) > center + 1.0) {
                                localMaximum = false;
                                break;
                            }
                        }
                    }
                    if (!localMaximum) continue;

                    double weightedX = 0.0, weightedY = 0.0, weight = 0.0;
                    for (int yy = -3; yy <= 3; yy++) {
                        for (int xx = -3; xx <= 3; xx++) {
                            double value = luminance(x + xx, y + yy);
                            double w = Math.Max(0.0, value - background / count);
                            weightedX += (x + xx) * w;
                            weightedY += (y + yy) * w;
                            weight += w;
                        }
                    }
                    if (weight > 0.0 && acceptedCount < accepted.Length) {
                        var candidate = new IPolarStarPoint { X = weightedX / weight, Y = weightedY / weight, Contrast = contrast };
                        bool separated = true;
                        for (int acceptedIndex = 0; acceptedIndex < acceptedCount; acceptedIndex++) {
                            double deltaX = candidate.X - accepted[acceptedIndex].X;
                            double deltaY = candidate.Y - accepted[acceptedIndex].Y;
                            if (deltaX * deltaX + deltaY * deltaY < 64.0) { separated = false; break; }
                        }
                        if (separated) accepted[acceptedCount++] = candidate;
                    }
                }
            }

            var result = new IPolarStarPoint[acceptedCount];
            Array.Copy(accepted, result, acceptedCount);
            return result;
        }
    }
}
'@
}

function Get-MatchedPoint {
    param($Reference, $Candidates, [double]$MaximumDistance)
    $best = $null
    $bestDistance = [double]::PositiveInfinity
    foreach ($candidate in $Candidates) {
        $dx = [double]$candidate.X - [double]$Reference.X
        $dy = [double]$candidate.Y - [double]$Reference.Y
        $distance = [Math]::Sqrt($dx * $dx + $dy * $dy)
        if ($distance -lt $bestDistance) { $best = $candidate; $bestDistance = $distance }
    }
    if ($bestDistance -le $MaximumDistance) { return $best }
    return $null
}

$samplesFullPath = [IO.Path]::GetFullPath($SamplesPath)
if (-not (Test-Path -LiteralPath $samplesFullPath -PathType Leaf)) {
    throw "Samples file not found: $samplesFullPath"
}
$frameRoot = Join-Path (Split-Path -Parent $samplesFullPath) 'frames'
$rows = @(Get-Content -LiteralPath $samplesFullPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_ | ConvertFrom-Json })
$usable = @($rows | Where-Object { $_.Frame -and -not $_.CaptureError } | Select-Object -First $MinimumFrames)
$issues = New-Object System.Collections.Generic.List[string]
if ($usable.Count -lt $MinimumFrames) { $issues.Add("Only $($usable.Count) usable frames; $MinimumFrames required.") }

$hashes = @($usable | ForEach-Object { [string]$_.FrameSha256 } | Where-Object { $_ } | Select-Object -Unique)
if ($hashes.Count -lt [Math]::Min(3, $MinimumFrames)) { $issues.Add('Frames are not provably fresh/unique.') }

$detections = New-Object System.Collections.Generic.List[object]
foreach ($row in $usable) {
    $framePath = Join-Path $frameRoot ([string]$row.Frame)
    if (-not (Test-Path -LiteralPath $framePath -PathType Leaf)) {
        $issues.Add("Missing frame $framePath")
        continue
    }
    $actualHash = (Get-FileHash -LiteralPath $framePath -Algorithm SHA256).Hash
    if ($actualHash -ne [string]$row.FrameSha256) { $issues.Add("Frame hash mismatch: $($row.Frame)"); continue }
    $stars = @([IPolarStarGate]::Find($framePath, 230, 60, $MinimumLocalContrast))
    [void]$detections.Add([pscustomobject]@{ Frame = [string]$row.Frame; Stars = $stars })
}

$stable = New-Object System.Collections.Generic.List[object]
if ($detections.Count -eq $MinimumFrames) {
    foreach ($reference in $detections[0].Stars) {
        $matches = New-Object System.Collections.Generic.List[object]
        [void]$matches.Add($reference)
        for ($index = 1; $index -lt $detections.Count; $index++) {
            $match = Get-MatchedPoint -Reference $reference -Candidates $detections[$index].Stars -MaximumDistance $MaximumMatchDistancePixels
            if ($null -eq $match) { break }
            [void]$matches.Add($match)
        }
        if ($matches.Count -ne $detections.Count) { continue }
        $meanX = ($matches | Measure-Object -Property X -Average).Average
        $meanY = ($matches | Measure-Object -Property Y -Average).Average
        $squared = @($matches | ForEach-Object { ([double]$_.X - $meanX) * ([double]$_.X - $meanX) + ([double]$_.Y - $meanY) * ([double]$_.Y - $meanY) })
        $rms = [Math]::Sqrt(($squared | Measure-Object -Average).Average)
        if ($rms -le $MaximumRmsScatterPixels) {
            [void]$stable.Add([pscustomobject]@{ X = $meanX; Y = $meanY; RmsScatterPixels = $rms })
        }
    }
}

if ($stable.Count -lt $MinimumStableStars) {
    $issues.Add("Only $($stable.Count) stable stars; $MinimumStableStars required.")
}
$stableHorizontalSpan = 0.0
$stableVerticalSpan = 0.0
$stableMinorAxisRms = 0.0
$stableMajorAxisRms = 0.0
$stableAnisotropyRatio = [double]::PositiveInfinity
if ($stable.Count -ge 2) {
    $stableX = @($stable | ForEach-Object { [double]$_.X })
    $stableY = @($stable | ForEach-Object { [double]$_.Y })
    $stableHorizontalSpan = ($stableX | Measure-Object -Maximum).Maximum - ($stableX | Measure-Object -Minimum).Minimum
    $stableVerticalSpan = ($stableY | Measure-Object -Maximum).Maximum - ($stableY | Measure-Object -Minimum).Minimum

    $meanX = ($stableX | Measure-Object -Average).Average
    $meanY = ($stableY | Measure-Object -Average).Average
    $varianceX = 0.0
    $varianceY = 0.0
    $covarianceXY = 0.0
    foreach ($point in $stable) {
        $dx = [double]$point.X - $meanX
        $dy = [double]$point.Y - $meanY
        $varianceX += $dx * $dx
        $varianceY += $dy * $dy
        $covarianceXY += $dx * $dy
    }
    $varianceX /= $stable.Count
    $varianceY /= $stable.Count
    $covarianceXY /= $stable.Count
    $trace = $varianceX + $varianceY
    $discriminant = [Math]::Sqrt([Math]::Max(0.0, (($varianceX - $varianceY) * ($varianceX - $varianceY)) + (4.0 * $covarianceXY * $covarianceXY)))
    $majorEigenvalue = [Math]::Max(0.0, 0.5 * ($trace + $discriminant))
    $minorEigenvalue = [Math]::Max(0.0, 0.5 * ($trace - $discriminant))
    $stableMajorAxisRms = [Math]::Sqrt($majorEigenvalue)
    $stableMinorAxisRms = [Math]::Sqrt($minorEigenvalue)
    if ($stableMinorAxisRms -gt 0.0) {
        $stableAnisotropyRatio = $stableMajorAxisRms / $stableMinorAxisRms
    }
}
if ($stable.Count -ge $MinimumStableStars -and $stableHorizontalSpan -lt $MinimumStableHorizontalSpanPixels) {
    $issues.Add("Stable detections span only $([Math]::Round($stableHorizontalSpan, 3)) horizontal pixels; $MinimumStableHorizontalSpanPixels required to reject UI columns and borders.")
}
if ($stable.Count -ge $MinimumStableStars -and $stableVerticalSpan -lt $MinimumStableVerticalSpanPixels) {
    $issues.Add("Stable detections span only $([Math]::Round($stableVerticalSpan, 3)) vertical pixels; $MinimumStableVerticalSpanPixels required to reject UI rows and borders.")
}
if ($stable.Count -ge $MinimumStableStars -and $stableMinorAxisRms -lt $MinimumStableMinorAxisRmsPixels) {
    $issues.Add("Stable detections have only $([Math]::Round($stableMinorAxisRms, 3)) pixels minor-axis RMS spread; $MinimumStableMinorAxisRmsPixels required for two-dimensional observability.")
}
if ($stable.Count -ge $MinimumStableStars -and $stableAnisotropyRatio -gt $MaximumStableAnisotropyRatio) {
    $issues.Add("Stable detections have anisotropy ratio $([Math]::Round($stableAnisotropyRatio, 3)); maximum $MaximumStableAnisotropyRatio allowed for two-dimensional observability.")
}
$result = [ordered]@{
    SchemaVersion = 2
    EvaluatedUtc = [DateTime]::UtcNow.ToString('o')
    SamplesPath = $samplesFullPath
    FrameCount = $usable.Count
    UniqueFrameHashCount = $hashes.Count
    DetectionCounts = @($detections | ForEach-Object { $_.Stars.Count })
    StableStarCount = $stable.Count
    StableHorizontalSpanPixels = $stableHorizontalSpan
    StableVerticalSpanPixels = $stableVerticalSpan
    StableMinorAxisRmsPixels = $stableMinorAxisRms
    StableMajorAxisRmsPixels = $stableMajorAxisRms
    StableAnisotropyRatio = $stableAnisotropyRatio
    StableStars = $stable.ToArray()
    Passed = ($issues.Count -eq 0)
    Issues = $issues.ToArray()
    GrantsUpasAuthority = $false
    GrantsAbsoluteAccuracyClaim = $false
}
$json = $result | ConvertTo-Json -Depth 6
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $json, [Text.UTF8Encoding]::new($false))
}
$result
if (-not $result.Passed) { exit 2 }
