param(
    [Parameter(Mandatory = $true)]
    [string]$SamplesPath,
    [ValidateRange(5, 500)]
    [int]$MinimumSlewingFrames = 10,
    [ValidateRange(5, 500)]
    [int]$MinimumTrackPoints = 10,
    [ValidateRange(1.0, 500.0)]
    [double]$MaximumStepPixels = 80.0,
    [ValidateRange(1.0, 1000.0)]
    [double]$MinimumPathSpanPixels = 20.0,
    [ValidateRange(1.0, 180.0)]
    [double]$MinimumAngularSpanDegrees = 10.0,
    [ValidateRange(0.1, 300.0)]
    [double]$MaximumRmsResidualArcsec = 15.0,
    [ValidateRange(0.1, 300.0)]
    [double]$MaximumResidualArcsec = 30.0,
    [ValidateRange(0.1, 300.0)]
    [double]$MaximumJackknifeCenterStandardErrorArcsec = 15.0,
    [ValidateRange(0.1, 300.0)]
    [double]$MaximumLeaveOneOutCenterShiftArcsec = 30.0,
    [ValidateRange(0.1, 300.0)]
    [double]$ArcsecPerPixel = 30.87742981,
    [string]$OutputPath = ''
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
if (-not ('IPolarAxisStarDetector' -as [type])) {
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
using System.Drawing;
using System.Drawing.Imaging;

public sealed class IPolarAxisStarPoint {
    public double X { get; set; }
    public double Y { get; set; }
    public double Contrast { get; set; }
}

public sealed class IPolarCircleFit {
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double Radius { get; set; }
    public double RmsResidual { get; set; }
    public double MaximumResidual { get; set; }
    public double AngularSpanDegrees { get; set; }
}

public static class IPolarAxisStarDetector {
    public static IPolarAxisStarPoint[] Find(string path, int cropLeft, double minimumContrast) {
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

            var accepted = new IPolarAxisStarPoint[100];
            int acceptedCount = 0;
            for (int y = 10; y < height - 10; y += 2) {
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
                    double localBackground = background / count;
                    double contrast = center - localBackground;
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
                            double w = Math.Max(0.0, value - localBackground);
                            weightedX += (x + xx) * w;
                            weightedY += (y + yy) * w;
                            weight += w;
                        }
                    }
                    if (weight <= 0.0) continue;
                    var candidate = new IPolarAxisStarPoint {
                        X = weightedX / weight,
                        Y = weightedY / weight,
                        Contrast = contrast
                    };
                    bool separated = true;
                    for (int acceptedIndex = 0; acceptedIndex < acceptedCount; acceptedIndex++) {
                        var prior = accepted[acceptedIndex];
                        double deltaX = candidate.X - prior.X;
                        double deltaY = candidate.Y - prior.Y;
                        if (deltaX * deltaX + deltaY * deltaY < 64.0) { separated = false; break; }
                    }
                    if (separated && acceptedCount < accepted.Length) accepted[acceptedCount++] = candidate;
                }
            }
            var result = new IPolarAxisStarPoint[acceptedCount];
            Array.Copy(accepted, result, acceptedCount);
            return result;
        }
    }

    private static double[] Solve3(double[,] matrix, double[] rhs) {
        var augmented = new double[3, 4];
        for (int row = 0; row < 3; row++) {
            for (int column = 0; column < 3; column++) augmented[row, column] = matrix[row, column];
            augmented[row, 3] = rhs[row];
        }
        for (int pivot = 0; pivot < 3; pivot++) {
            int best = pivot;
            for (int row = pivot + 1; row < 3; row++) {
                if (Math.Abs(augmented[row, pivot]) > Math.Abs(augmented[best, pivot])) best = row;
            }
            if (Math.Abs(augmented[best, pivot]) < 1e-9) throw new InvalidOperationException("Circle fit is singular.");
            if (best != pivot) {
                for (int column = pivot; column < 4; column++) {
                    double temporary = augmented[pivot, column];
                    augmented[pivot, column] = augmented[best, column];
                    augmented[best, column] = temporary;
                }
            }
            double divisor = augmented[pivot, pivot];
            for (int column = pivot; column < 4; column++) augmented[pivot, column] /= divisor;
            for (int row = 0; row < 3; row++) {
                if (row == pivot) continue;
                double factor = augmented[row, pivot];
                for (int column = pivot; column < 4; column++) augmented[row, column] -= factor * augmented[pivot, column];
            }
        }
        return new[] { augmented[0, 3], augmented[1, 3], augmented[2, 3] };
    }

    public static IPolarCircleFit FitCircle(IPolarAxisStarPoint[] points) {
        if (points == null || points.Length < 3) throw new ArgumentException("At least three points are required.");
        var normal = new double[3, 3];
        var rhs = new double[3];
        foreach (var point in points) {
            double[] row = { 2.0 * point.X, 2.0 * point.Y, 1.0 };
            double target = point.X * point.X + point.Y * point.Y;
            for (int i = 0; i < 3; i++) {
                rhs[i] += row[i] * target;
                for (int j = 0; j < 3; j++) normal[i, j] += row[i] * row[j];
            }
        }
        double[] solved = Solve3(normal, rhs);
        double centerX = solved[0], centerY = solved[1];
        double radiusSquared = solved[2] + centerX * centerX + centerY * centerY;
        if (radiusSquared <= 0.0) throw new InvalidOperationException("Circle fit produced a non-positive radius.");
        double radius = Math.Sqrt(radiusSquared);
        double sumSquares = 0.0, maximum = 0.0;
        var angles = new double[points.Length];
        for (int index = 0; index < points.Length; index++) {
            double dx = points[index].X - centerX;
            double dy = points[index].Y - centerY;
            double residual = Math.Abs(Math.Sqrt(dx * dx + dy * dy) - radius);
            sumSquares += residual * residual;
            maximum = Math.Max(maximum, residual);
            angles[index] = Math.Atan2(dy, dx);
        }
        double unwrapped = angles[0], minimum = unwrapped, maximumAngle = unwrapped;
        for (int index = 1; index < angles.Length; index++) {
            double candidate = angles[index];
            while (candidate - unwrapped > Math.PI) candidate -= 2.0 * Math.PI;
            while (candidate - unwrapped < -Math.PI) candidate += 2.0 * Math.PI;
            unwrapped = candidate;
            minimum = Math.Min(minimum, unwrapped);
            maximumAngle = Math.Max(maximumAngle, unwrapped);
        }
        return new IPolarCircleFit {
            CenterX = centerX,
            CenterY = centerY,
            Radius = radius,
            RmsResidual = Math.Sqrt(sumSquares / points.Length),
            MaximumResidual = maximum,
            AngularSpanDegrees = (maximumAngle - minimum) * 180.0 / Math.PI
        };
    }
}
'@
}

function Get-NearestStar {
    param($Reference, $Candidates, [double]$MaximumDistance)
    $nearest = $null
    $distance = [double]::PositiveInfinity
    foreach ($candidate in $Candidates) {
        $dx = [double]$candidate.X - [double]$Reference.X
        $dy = [double]$candidate.Y - [double]$Reference.Y
        $candidateDistance = [Math]::Sqrt($dx * $dx + $dy * $dy)
        if ($candidateDistance -lt $distance) { $nearest = $candidate; $distance = $candidateDistance }
    }
    if ($distance -le $MaximumDistance) { return $nearest }
    return $null
}

function Get-PathSpanPixels {
    param($Points)
    $maximum = 0.0
    for ($left = 0; $left -lt $Points.Count; $left++) {
        for ($right = $left + 1; $right -lt $Points.Count; $right++) {
            $dx = [double]$Points[$left].X - [double]$Points[$right].X
            $dy = [double]$Points[$left].Y - [double]$Points[$right].Y
            $maximum = [Math]::Max($maximum, [Math]::Sqrt($dx * $dx + $dy * $dy))
        }
    }
    return $maximum
}

$samplesFullPath = [IO.Path]::GetFullPath($SamplesPath)
if (-not (Test-Path -LiteralPath $samplesFullPath -PathType Leaf)) { throw "Samples file not found: $samplesFullPath" }
$frameRoot = Join-Path (Split-Path -Parent $samplesFullPath) 'frames'
$rows = @([IO.File]::ReadAllLines($samplesFullPath) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_ | ConvertFrom-Json })
$slewingRows = @($rows | Where-Object { $_.Frame -and -not $_.CaptureError -and $_.Mount -and [bool]$_.Mount.Slewing })
$issues = New-Object System.Collections.Generic.List[string]
if ($slewingRows.Count -lt $MinimumSlewingFrames) { $issues.Add("Only $($slewingRows.Count) slewing frames; $MinimumSlewingFrames required.") }
$observedPierSides = @($slewingRows | ForEach-Object { [string]$_.Mount.SideOfPier } | Where-Object { $_ } | Select-Object -Unique)
if ($observedPierSides.Count -ne 1 -or $observedPierSides[0] -notin @('pierEast', 'pierWest')) {
    $issues.Add("Slewing frames require one valid pier side; observed '$($observedPierSides -join ',')'.")
}

$detections = New-Object System.Collections.Generic.List[object]
foreach ($row in $slewingRows) {
    $framePath = Join-Path $frameRoot ([string]$row.Frame)
    if (-not (Test-Path -LiteralPath $framePath -PathType Leaf)) { $issues.Add("Missing frame: $framePath"); continue }
    $actualHash = (Get-FileHash -LiteralPath $framePath -Algorithm SHA256).Hash
    if ($actualHash -ne [string]$row.FrameSha256) { $issues.Add("Frame hash mismatch: $($row.Frame)"); continue }
    $stars = @([IPolarAxisStarDetector]::Find($framePath, 230, 12.0))
    [void]$detections.Add([pscustomobject]@{ Frame = [string]$row.Frame; Stars = $stars })
}

$candidateTracks = New-Object System.Collections.Generic.List[object]
if ($detections.Count -gt 0) {
    foreach ($startingStar in @($detections[0].Stars | Sort-Object Contrast -Descending | Select-Object -First 20)) {
        $track = New-Object System.Collections.Generic.List[object]
        [void]$track.Add($startingStar)
        $current = $startingStar
        for ($frameIndex = 1; $frameIndex -lt $detections.Count; $frameIndex++) {
            $next = Get-NearestStar -Reference $current -Candidates $detections[$frameIndex].Stars -MaximumDistance $MaximumStepPixels
            if ($null -eq $next) { break }
            [void]$track.Add($next)
            $current = $next
        }
        $span = Get-PathSpanPixels -Points $track
        [void]$candidateTracks.Add([pscustomobject]@{ Points = $track.ToArray(); Count = $track.Count; Span = $span })
    }
}

$selected = $candidateTracks | Sort-Object Count, Span -Descending | Select-Object -First 1
$fit = $null
$leaveOneOutCenterRmsPixels = $null
$leaveOneOutCenterMaximumPixels = $null
$jackknifeCenterStandardErrorPixels = $null
if ($null -eq $selected -or $selected.Count -lt $MinimumTrackPoints) {
    $count = if ($null -eq $selected) { 0 } else { $selected.Count }
    $issues.Add("Longest stellar track has $count points; $MinimumTrackPoints required.")
} elseif ([double]$selected.Span -lt $MinimumPathSpanPixels) {
    $issues.Add(("Stellar path span {0:F2} px is below the required {1:F2} px." -f [double]$selected.Span, $MinimumPathSpanPixels))
} else {
    try { $fit = [IPolarAxisStarDetector]::FitCircle([IPolarAxisStarPoint[]]$selected.Points) }
    catch { $issues.Add("Circle fit failed: $($_.Exception.Message)") }
}

if ($null -ne $fit) {
    try {
        $centerShiftSquares = 0.0
        $centerShiftMaximum = 0.0
        $leaveOneOutCenters = New-Object System.Collections.Generic.List[object]
        for ($excluded = 0; $excluded -lt $selected.Points.Count; $excluded++) {
            $subset = New-Object System.Collections.Generic.List[IPolarAxisStarPoint]
            for ($pointIndex = 0; $pointIndex -lt $selected.Points.Count; $pointIndex++) {
                if ($pointIndex -ne $excluded) { [void]$subset.Add($selected.Points[$pointIndex]) }
            }
            $subsetFit = [IPolarAxisStarDetector]::FitCircle($subset.ToArray())
            $centerDx = [double]$subsetFit.CenterX - [double]$fit.CenterX
            $centerDy = [double]$subsetFit.CenterY - [double]$fit.CenterY
            $centerShift = [Math]::Sqrt($centerDx * $centerDx + $centerDy * $centerDy)
            $centerShiftSquares += $centerShift * $centerShift
            $centerShiftMaximum = [Math]::Max($centerShiftMaximum, $centerShift)
            [void]$leaveOneOutCenters.Add([pscustomobject]@{ X = [double]$subsetFit.CenterX; Y = [double]$subsetFit.CenterY })
        }
        $leaveOneOutCenterRmsPixels = [Math]::Sqrt($centerShiftSquares / $selected.Points.Count)
        $leaveOneOutCenterMaximumPixels = $centerShiftMaximum
        $meanLeaveOneOutCenterX = ($leaveOneOutCenters | Measure-Object -Property X -Average).Average
        $meanLeaveOneOutCenterY = ($leaveOneOutCenters | Measure-Object -Property Y -Average).Average
        $jackknifeSumSquares = 0.0
        foreach ($center in $leaveOneOutCenters) {
            $jackknifeDx = [double]$center.X - [double]$meanLeaveOneOutCenterX
            $jackknifeDy = [double]$center.Y - [double]$meanLeaveOneOutCenterY
            $jackknifeSumSquares += $jackknifeDx * $jackknifeDx + $jackknifeDy * $jackknifeDy
        }
        $jackknifeCenterStandardErrorPixels = [Math]::Sqrt(
            (($selected.Points.Count - 1.0) / $selected.Points.Count) * $jackknifeSumSquares)
    } catch {
        $issues.Add("Leave-one-out axis-center stability failed: $($_.Exception.Message)")
    }
    if ([double]$fit.AngularSpanDegrees -lt $MinimumAngularSpanDegrees) {
        $issues.Add(("Angular span {0:F2} deg is below the required {1:F2} deg." -f [double]$fit.AngularSpanDegrees, $MinimumAngularSpanDegrees))
    }
    if ([double]$fit.RmsResidual * $ArcsecPerPixel -gt $MaximumRmsResidualArcsec) {
        $issues.Add(("RMS residual {0:F2} arcsec exceeds {1:F2} arcsec." -f ([double]$fit.RmsResidual * $ArcsecPerPixel), $MaximumRmsResidualArcsec))
    }
    if ([double]$fit.MaximumResidual * $ArcsecPerPixel -gt $MaximumResidualArcsec) {
        $issues.Add(("Maximum residual {0:F2} arcsec exceeds {1:F2} arcsec." -f ([double]$fit.MaximumResidual * $ArcsecPerPixel), $MaximumResidualArcsec))
    }
    if (($null -ne $jackknifeCenterStandardErrorPixels) -and (([double]$jackknifeCenterStandardErrorPixels * $ArcsecPerPixel) -gt $MaximumJackknifeCenterStandardErrorArcsec)) {
        $issues.Add(("Jackknife axis-center standard error {0:F2} arcsec exceeds {1:F2} arcsec." -f ([double]$jackknifeCenterStandardErrorPixels * $ArcsecPerPixel), $MaximumJackknifeCenterStandardErrorArcsec))
    }
    if (($null -ne $leaveOneOutCenterMaximumPixels) -and (([double]$leaveOneOutCenterMaximumPixels * $ArcsecPerPixel) -gt $MaximumLeaveOneOutCenterShiftArcsec)) {
        $issues.Add(("Maximum leave-one-out axis-center shift {0:F2} arcsec exceeds {1:F2} arcsec." -f ([double]$leaveOneOutCenterMaximumPixels * $ArcsecPerPixel), $MaximumLeaveOneOutCenterShiftArcsec))
    }
}

$result = [ordered]@{
    SchemaVersion = 1
    EvaluatedUtc = [DateTime]::UtcNow.ToString('o')
    RunId = Split-Path -Leaf (Split-Path -Parent $samplesFullPath)
    SamplesPath = $samplesFullPath
    SamplesSha256 = (Get-FileHash -LiteralPath $samplesFullPath -Algorithm SHA256).Hash
    SideOfPier = if ($observedPierSides.Count -eq 1) { $observedPierSides[0] } else { $null }
    SlewingFrameCount = $slewingRows.Count
    VerifiedFrameCount = $detections.Count
    DetectionCounts = @($detections | ForEach-Object { $_.Stars.Count })
    TrackPointCount = if ($null -eq $selected) { 0 } else { $selected.Count }
    PathSpanPixels = if ($null -eq $selected) { $null } else { [double]$selected.Span }
    AngularSpanDegrees = if ($null -eq $fit) { $null } else { [double]$fit.AngularSpanDegrees }
    AxisCenterX = if ($null -eq $fit) { $null } else { [double]$fit.CenterX }
    AxisCenterY = if ($null -eq $fit) { $null } else { [double]$fit.CenterY }
    RadiusPixels = if ($null -eq $fit) { $null } else { [double]$fit.Radius }
    RmsResidualPixels = if ($null -eq $fit) { $null } else { [double]$fit.RmsResidual }
    MaximumResidualPixels = if ($null -eq $fit) { $null } else { [double]$fit.MaximumResidual }
    RmsResidualArcsec = if ($null -eq $fit) { $null } else { [double]$fit.RmsResidual * $ArcsecPerPixel }
    MaximumResidualArcsec = if ($null -eq $fit) { $null } else { [double]$fit.MaximumResidual * $ArcsecPerPixel }
    LeaveOneOutCenterRmsPixels = $leaveOneOutCenterRmsPixels
    LeaveOneOutCenterMaximumPixels = $leaveOneOutCenterMaximumPixels
    LeaveOneOutCenterRmsArcsec = if ($null -eq $leaveOneOutCenterRmsPixels) { $null } else { [double]$leaveOneOutCenterRmsPixels * $ArcsecPerPixel }
    LeaveOneOutCenterMaximumArcsec = if ($null -eq $leaveOneOutCenterMaximumPixels) { $null } else { [double]$leaveOneOutCenterMaximumPixels * $ArcsecPerPixel }
    JackknifeCenterStandardErrorPixels = $jackknifeCenterStandardErrorPixels
    JackknifeCenterStandardErrorArcsec = if ($null -eq $jackknifeCenterStandardErrorPixels) { $null } else { [double]$jackknifeCenterStandardErrorPixels * $ArcsecPerPixel }
    ArcsecPerPixel = $ArcsecPerPixel
    DifferentialAxisStabilityQualified = ($issues.Count -eq 0)
    Issues = $issues.ToArray()
    GrantsUpasAuthority = $false
    GrantsAbsoluteAccuracyClaim = $false
}
$json = $result | ConvertTo-Json -Depth 6
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $json, [Text.UTF8Encoding]::new($false))
}
$result
if (-not $result.DifferentialAxisStabilityQualified) { exit 2 }
