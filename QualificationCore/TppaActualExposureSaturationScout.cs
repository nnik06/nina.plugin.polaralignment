using System.Globalization;

namespace NINA.Plugins.PolarAlignment;

internal static class TppaSaturationScoutVerdicts {
    public const string Pass = "PASS";
    public const string Fail = "FAIL";
    public const string Inconclusive = "INCONCLUSIVE";
}

internal sealed class TppaActualExposureScoutPolicy {
    public int SchemaVersion { get; set; } = 1;
    public string OpticalTrainId { get; set; } = string.Empty;
    public string RequiredFilterName { get; set; } = string.Empty;
    public string[] AllowedFilterNames { get; set; } = Array.Empty<string>();
    public double LinearityCeilingAdu { get; set; } = 50000.0;
    public double ScoutShortExposureSeconds { get; set; } = 10.0;
    public double ScoutLongExposureSeconds { get; set; } = 60.0;
    public double ExpectedLongExposureSeconds { get; set; } = 900.0;
    public double MaximumProjectedBackgroundAdu { get; set; } = 20000.0;
    public double MinimumProjectedSkyAbovePedestalAdu { get; set; } = 500.0;
    public double MaximumProjectedGradientAdu { get; set; } = 8000.0;
    public double MaximumImpliedPedestalDeltaAdu { get; set; } = 150.0;
    public double ExpectedPedestalAdu { get; set; } = 500.0;
    public double MaximumProjectedResidualSaturatedFraction { get; set; } = 0.0002;
    public double MaximumScoutResidualSaturatedFraction { get; set; } = 0.00001;
    public int MaximumExemptBrightSources { get; set; } = 30;
    public int MaximumExemptSourceAreaPixels { get; set; } = 1500;
    public double MaximumExemptBrightSourceFraction { get; set; } = 0.0015;
    public double MaximumEvidenceAgeMinutes { get; set; } = 15.0;
    public double MaximumScoutSeparationMinutes { get; set; } = 5.0;
    public int RequiredGain { get; set; } = 100;
    public int RequiredOffset { get; set; } = 50;
    public int RequiredBinning { get; set; } = 1;
}

internal sealed record TppaActualExposureSaturationScoutReceipt(
    int SchemaVersion,
    DateTime GeneratedUtc,
    string Verdict,
    string OpticalTrainId,
    string RequiredFilterName,
    string ShortFitsSha256,
    string LongFitsSha256,
    DateTime ShortObservationUtc,
    DateTime LongObservationUtc,
    double ShortExposureSeconds,
    double LongExposureSeconds,
    double TargetExposureSeconds,
    double MedianImpliedPedestalAdu,
    double MinimumProjectedBackgroundAdu,
    double MedianProjectedBackgroundAdu,
    double MaximumProjectedBackgroundAdu,
    double ProjectedBackgroundGradientAdu,
    double ProjectedResidualSaturatedFraction,
    double LongScoutResidualSaturatedFraction,
    int ProjectedExemptBrightSources,
    int LongScoutExemptBrightSources,
    IReadOnlyList<string> Issues,
    bool GrantsSequenceStartAuthority,
    bool GrantsMotionAuthority,
    bool PredictsGuidingOrStarShapeSuccess);

internal static class TppaActualExposureSaturationScoutAnalyzer {
    private const int BorderPixels = 16;
    private const int TileSize = 64;
    private const double ExposureToleranceSeconds = 0.5;

    private sealed record SaturationSummary(
        double ResidualFraction,
        int ExemptComponents);

    public static TppaActualExposureSaturationScoutReceipt Analyze(
            string shortFitsPath,
            string longFitsPath,
            string shortFitsSha256,
            string longFitsSha256,
            TppaActualExposureScoutPolicy policy,
            DateTime nowUtc) {
        var inconclusive = new List<string>();
        var failures = new List<string>();
        var shortObservation = default(DateTime);
        var longObservation = default(DateTime);
        var shortExposure = double.NaN;
        var longExposure = double.NaN;
        var pedestal = double.NaN;
        var projected = Array.Empty<double>();
        var projectedSaturation = new SaturationSummary(double.NaN, 0);
        var scoutSaturation = new SaturationSummary(double.NaN, 0);

        if (policy == null) {
            inconclusive.Add("saturation scout policy is missing");
            return Receipt();
        }
        ValidatePolicy(policy, inconclusive);
        try {
            using var shortImage = TppaFitsImage.Open(shortFitsPath);
            using var longImage = TppaFitsImage.Open(longFitsPath);
            shortObservation = shortImage.DateObsUtc.UtcDateTime;
            longObservation = longImage.DateObsUtc.UtcDateTime;
            shortExposure = shortImage.ExposureSeconds;
            longExposure = longImage.ExposureSeconds;
            if (shortImage.Width != longImage.Width
                    || shortImage.Height != longImage.Height) {
                inconclusive.Add("scout FITS dimensions differ");
            }
            if (Math.Abs(shortExposure - policy.ScoutShortExposureSeconds)
                    > ExposureToleranceSeconds
                    || Math.Abs(longExposure - policy.ScoutLongExposureSeconds)
                    > ExposureToleranceSeconds) {
                inconclusive.Add("scout exposure durations do not match policy");
            }
            ValidateHeader(shortImage, policy, "short", inconclusive);
            ValidateHeader(longImage, policy, "long", inconclusive);
            if (longObservation <= shortObservation
                    || (longObservation - shortObservation).TotalMinutes
                        > policy.MaximumScoutSeparationMinutes) {
                inconclusive.Add("scout observations are not a fresh ordered pair");
            }
            var normalizedNow = nowUtc.Kind == DateTimeKind.Utc
                ? nowUtc : nowUtc.ToUniversalTime();
            var age = (normalizedNow - longObservation).TotalMinutes;
            if (age < -1.0 || age > policy.MaximumEvidenceAgeMinutes) {
                inconclusive.Add("scout pair is outside the freshness window");
            }
            if (inconclusive.Count == 0) {
                var tiles = MeasureTiles(shortImage, longImage, policy);
                pedestal = Median(tiles.Select(value => value.Pedestal));
                projected = tiles.Select(value => value.Projected).ToArray();
                if (tiles.Any(value => !double.IsFinite(value.Rate) || value.Rate <= 0)) {
                    inconclusive.Add("one or more scout tiles have non-positive sky rate");
                }
                if (Math.Abs(pedestal - policy.ExpectedPedestalAdu)
                        > policy.MaximumImpliedPedestalDeltaAdu) {
                    inconclusive.Add("two-scout implied pedestal is inconsistent with policy");
                }
                var minProjected = projected.Min();
                var maxProjected = projected.Max();
                if (minProjected - pedestal < policy.MinimumProjectedSkyAbovePedestalAdu) {
                    inconclusive.Add("projected scout has insufficient sky signal");
                }
                if (maxProjected > policy.MaximumProjectedBackgroundAdu) {
                    failures.Add("projected 900-second background exceeds policy");
                }
                if (maxProjected - minProjected > policy.MaximumProjectedGradientAdu) {
                    failures.Add("projected 900-second background gradient exceeds policy");
                }
                projectedSaturation = MeasureSaturation(shortImage, pedestal,
                    policy.ExpectedLongExposureSeconds / shortExposure,
                    policy.LinearityCeilingAdu, policy);
                scoutSaturation = MeasureSaturation(longImage, pedestal, 1.0,
                    policy.LinearityCeilingAdu, policy);
                if (projectedSaturation.ResidualFraction
                        > policy.MaximumProjectedResidualSaturatedFraction) {
                    failures.Add("projected 900-second residual saturation exceeds policy");
                }
                if (scoutSaturation.ResidualFraction
                        > policy.MaximumScoutResidualSaturatedFraction) {
                    failures.Add("long scout is already saturated beyond bright-source allowance");
                }
            }
        } catch (Exception exception) {
            inconclusive.Add($"scout FITS analysis failed: {exception.Message}");
        }
        return Receipt();

        TppaActualExposureSaturationScoutReceipt Receipt() {
            var verdict = failures.Count > 0
                ? TppaSaturationScoutVerdicts.Fail
                : inconclusive.Count > 0
                    ? TppaSaturationScoutVerdicts.Inconclusive
                    : TppaSaturationScoutVerdicts.Pass;
            var issues = inconclusive.Concat(failures).ToArray();
            return new(1, NormalizeUtc(nowUtc), verdict,
                policy?.OpticalTrainId ?? string.Empty,
                policy?.RequiredFilterName ?? string.Empty,
                shortFitsSha256 ?? string.Empty,
                longFitsSha256 ?? string.Empty,
                shortObservation, longObservation, shortExposure, longExposure,
                policy?.ExpectedLongExposureSeconds ?? double.NaN,
                pedestal,
                projected.Length == 0 ? double.NaN : projected.Min(),
                projected.Length == 0 ? double.NaN : Median(projected),
                projected.Length == 0 ? double.NaN : projected.Max(),
                projected.Length == 0 ? double.NaN : projected.Max() - projected.Min(),
                projectedSaturation.ResidualFraction,
                scoutSaturation.ResidualFraction,
                projectedSaturation.ExemptComponents,
                scoutSaturation.ExemptComponents,
                issues, false, false, false);
        }
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static void ValidatePolicy(
            TppaActualExposureScoutPolicy policy, List<string> issues) {
        if (policy.SchemaVersion != 1
                || string.IsNullOrWhiteSpace(policy.OpticalTrainId)
                || string.IsNullOrWhiteSpace(policy.RequiredFilterName)
                || policy.RequiredFilterName.StartsWith("REPLACE-", StringComparison.Ordinal)
                || policy.AllowedFilterNames == null
                || !policy.AllowedFilterNames.Contains(
                    policy.RequiredFilterName, StringComparer.Ordinal)) {
            issues.Add("scout policy identity or exact filter allow-list is invalid");
        }
        var positive = new[] { policy.LinearityCeilingAdu,
            policy.ScoutShortExposureSeconds, policy.ScoutLongExposureSeconds,
            policy.ExpectedLongExposureSeconds, policy.MaximumProjectedBackgroundAdu,
            policy.MinimumProjectedSkyAbovePedestalAdu,
            policy.MaximumProjectedGradientAdu,
            policy.MaximumImpliedPedestalDeltaAdu, policy.MaximumEvidenceAgeMinutes,
            policy.MaximumScoutSeparationMinutes };
        if (positive.Any(value => !double.IsFinite(value) || value <= 0)
                || policy.ScoutLongExposureSeconds <= policy.ScoutShortExposureSeconds
                || policy.ExpectedLongExposureSeconds <= policy.ScoutLongExposureSeconds
                || policy.RequiredGain < 0 || policy.RequiredOffset < 0
                || policy.RequiredBinning < 1) {
            issues.Add("scout policy numeric contract is invalid");
        }
        var fractions = new[] { policy.MaximumProjectedResidualSaturatedFraction,
            policy.MaximumScoutResidualSaturatedFraction,
            policy.MaximumExemptBrightSourceFraction };
        if (fractions.Any(value => !double.IsFinite(value) || value <= 0 || value >= 1)
                || policy.MaximumExemptBrightSources < 0
                || policy.MaximumExemptSourceAreaPixels < 4) {
            issues.Add("scout policy saturation contract is invalid");
        }
    }

    private static void ValidateHeader(TppaFitsImage image,
            TppaActualExposureScoutPolicy policy, string label, List<string> issues) {
        if (!image.TryGetString("FILTER", out var filter)
                || !string.Equals(filter.Trim(), policy.RequiredFilterName,
                    StringComparison.Ordinal)) {
            issues.Add($"{label} scout FILTER does not exactly match policy");
        }
        ValidateIntegerHeader(image, "GAIN", policy.RequiredGain, label, issues);
        ValidateIntegerHeader(image, "OFFSET", policy.RequiredOffset, label, issues);
        ValidateIntegerHeader(image, "XBINNING", policy.RequiredBinning, label, issues);
        ValidateIntegerHeader(image, "YBINNING", policy.RequiredBinning, label, issues);
    }

    private static void ValidateIntegerHeader(TppaFitsImage image, string key,
            int expected, string label, List<string> issues) {
        if (!image.TryGetDouble(key, out var value)
                || Math.Abs(value - expected) > 1e-9) {
            issues.Add($"{label} scout {key} does not match policy");
        }
    }

    private sealed record TileResult(double Pedestal, double Rate, double Projected);

    private static IReadOnlyList<TileResult> MeasureTiles(TppaFitsImage shortImage,
            TppaFitsImage longImage, TppaActualExposureScoutPolicy policy) {
        var results = new List<TileResult>();
        for (var top = BorderPixels; top < shortImage.Height - BorderPixels; top += TileSize) {
            var bottom = Math.Min(top + TileSize, shortImage.Height - BorderPixels);
            for (var left = BorderPixels; left < shortImage.Width - BorderPixels; left += TileSize) {
                var right = Math.Min(left + TileSize, shortImage.Width - BorderPixels);
                var shortValues = new List<double>((right - left) * (bottom - top));
                var longValues = new List<double>(shortValues.Capacity);
                for (var y = top; y < bottom; y++) {
                    for (var x = left; x < right; x++) {
                        var shortValue = shortImage.ReadPixel(x, y);
                        var longValue = longImage.ReadPixel(x, y);
                        if (double.IsFinite(shortValue) && double.IsFinite(longValue)) {
                            shortValues.Add(shortValue);
                            longValues.Add(longValue);
                        }
                    }
                }
                if (shortValues.Count < 16) { continue; }
                var shortMedian = Median(shortValues);
                var longMedian = Median(longValues);
                var interval = longImage.ExposureSeconds - shortImage.ExposureSeconds;
                var rate = (longMedian - shortMedian) / interval;
                var pedestal = shortMedian - rate * shortImage.ExposureSeconds;
                var projected = longMedian + rate
                    * (policy.ExpectedLongExposureSeconds - longImage.ExposureSeconds);
                results.Add(new(pedestal, rate, projected));
            }
        }
        if (results.Count == 0) {
            throw new InvalidDataException("scout images have no usable interior tiles");
        }
        return results;
    }

    private static SaturationSummary MeasureSaturation(TppaFitsImage image,
            double pedestal, double scale, double ceiling,
            TppaActualExposureScoutPolicy policy) {
        var width = image.Width;
        var height = image.Height;
        var mask = new bool[checked(width * height)];
        var total = 0;
        for (var y = BorderPixels; y < height - BorderPixels; y++) {
            for (var x = BorderPixels; x < width - BorderPixels; x++) {
                var value = image.ReadPixel(x, y);
                var projected = pedestal + (value - pedestal) * scale;
                if (double.IsFinite(projected) && projected >= ceiling) {
                    mask[y * width + x] = true;
                    total++;
                }
            }
        }
        var usablePixels = checked((width - 2 * BorderPixels) * (height - 2 * BorderPixels));
        var maximumExemptPixels = (int)Math.Floor(
            usablePixels * policy.MaximumExemptBrightSourceFraction);
        var exempted = 0;
        var exemptComponents = 0;
        var queue = new Queue<int>();
        for (var y = BorderPixels; y < height - BorderPixels; y++) {
            for (var x = BorderPixels; x < width - BorderPixels; x++) {
                var start = y * width + x;
                if (!mask[start]) { continue; }
                mask[start] = false;
                queue.Enqueue(start);
                var area = 0;
                while (queue.Count > 0) {
                    var index = queue.Dequeue();
                    area++;
                    var cy = index / width;
                    var cx = index - cy * width;
                    for (var dy = -1; dy <= 1; dy++) {
                        for (var dx = -1; dx <= 1; dx++) {
                            if (dx == 0 && dy == 0) { continue; }
                            var nx = cx + dx;
                            var ny = cy + dy;
                            if (nx < BorderPixels || nx >= width - BorderPixels
                                    || ny < BorderPixels || ny >= height - BorderPixels) {
                                continue;
                            }
                            var neighbor = ny * width + nx;
                            if (!mask[neighbor]) { continue; }
                            mask[neighbor] = false;
                            queue.Enqueue(neighbor);
                        }
                    }
                }
                if (area >= 1
                        && area <= policy.MaximumExemptSourceAreaPixels
                        && exemptComponents < policy.MaximumExemptBrightSources
                        && exempted + area <= maximumExemptPixels) {
                    exempted += area;
                    exemptComponents++;
                }
            }
        }
        return new((double)Math.Max(0, total - exempted) / usablePixels,
            exemptComponents);
    }

    private static double Median(IEnumerable<double> source) {
        var values = source.Where(double.IsFinite).OrderBy(value => value).ToArray();
        if (values.Length == 0) { return double.NaN; }
        var middle = values.Length / 2;
        return values.Length % 2 == 1 ? values[middle]
            : 0.5 * (values[middle - 1] + values[middle]);
    }
}
