using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NINA.Plugins.PolarAlignment;

internal sealed record TppaAstapWcsGeometry(
    double RightAscensionDegrees,
    double DeclinationDegrees,
    DateTimeOffset ObservationUtc,
    int WidthPixels,
    int HeightPixels,
    double PixelScaleXArcseconds,
    double PixelScaleYArcseconds,
    string ScaleModel);

internal static class TppaAstapWcsGeometryParser {
    private static readonly string[] CdKeys = {
        "CD1_1", "CD1_2", "CD2_1", "CD2_2"
    };
    private static readonly Regex SipKey = new(
        "^(A|B|AP|BP)(?:_ORDER|_[0-9]+_[0-9]+)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private const double MaximumScaleAnisotropyFraction = 0.02;
    private const double MinimumPixelScaleArcseconds = 0.01;
    private const double MaximumPixelScaleArcseconds = 60.0;
    private const int MaximumImageDimensionPixels = 100000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static TppaAstapWcsGeometry Parse(string path) {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
            throw new InvalidDataException("ASTAP WCS source is missing.");
        }
        return Parse(File.ReadAllBytes(path));
    }

    public static TppaAstapWcsGeometry Parse(byte[] sourceBytes) {
        ArgumentNullException.ThrowIfNull(sourceBytes);
        var header = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        var text = StrictUtf8.GetString(sourceBytes);
        using var lines = new StringReader(text);
        while (lines.ReadLine() is { } line) {
            if (string.IsNullOrWhiteSpace(line) || line.Length < 3) { continue; }
            var equals = line.IndexOf('=');
            if (equals <= 0) { continue; }
            var keyWidth = Math.Min(8, equals);
            var key = line[..keyWidth].Trim();
            if (string.IsNullOrWhiteSpace(key)) { continue; }
            if (!header.TryAdd(key, ParseScalar(line[(equals + 1)..]))) {
                throw new InvalidDataException($"ASTAP WCS contains duplicate {key}.");
            }
        }

        foreach (var key in header.Keys) {
            if (SipKey.IsMatch(key)
                    || key.StartsWith("PV1_", StringComparison.OrdinalIgnoreCase)
                    || key.StartsWith("PV2_", StringComparison.OrdinalIgnoreCase)
                    || key.StartsWith("PC1_", StringComparison.OrdinalIgnoreCase)
                    || key.StartsWith("PC2_", StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidDataException(
                    $"ASTAP WCS distortion or PC-matrix keyword {key} is unsupported.");
            }
        }

        if (RequiredInt(header, "NAXIS") != 2) {
            throw new InvalidDataException("ASTAP WCS must describe a two-dimensional image.");
        }
        var width = RequiredInt(header, "NAXIS1");
        var height = RequiredInt(header, "NAXIS2");
        if (width <= 0 || height <= 0 || width > MaximumImageDimensionPixels
                || height > MaximumImageDimensionPixels) {
            throw new InvalidDataException("ASTAP WCS dimensions are invalid.");
        }
        if (!string.Equals(RequiredString(header, "CTYPE1"), "RA---TAN",
                    StringComparison.Ordinal)
                || !string.Equals(RequiredString(header, "CTYPE2"), "DEC--TAN",
                    StringComparison.Ordinal)) {
            throw new InvalidDataException(
                "ASTAP WCS must use the undistorted RA---TAN/DEC--TAN projection.");
        }
        var ra = RequiredDouble(header, "CRVAL1");
        var dec = RequiredDouble(header, "CRVAL2");
        if (ra < 0 || ra >= 360 || dec < -90 || dec > 90) {
            throw new InvalidDataException("ASTAP WCS centre is out of range.");
        }
        var crpix1 = RequiredDouble(header, "CRPIX1");
        var crpix2 = RequiredDouble(header, "CRPIX2");
        if (crpix1 < 0.5 || crpix1 > width + 0.5
                || crpix2 < 0.5 || crpix2 > height + 0.5) {
            throw new InvalidDataException("ASTAP WCS reference pixel is outside the image.");
        }
        var observationUtc = RequiredObservationUtc(header);

        var hasAnyCd = CdKeys.Any(header.ContainsKey);
        var hasAllCd = CdKeys.All(header.ContainsKey);
        var hasAnyCdelt = header.ContainsKey("CDELT1")
            || header.ContainsKey("CDELT2") || header.ContainsKey("CROTA2");
        var hasAllCdelt = header.ContainsKey("CDELT1")
            && header.ContainsKey("CDELT2") && header.ContainsKey("CROTA2");
        if ((hasAnyCd && !hasAllCd) || (hasAnyCdelt && !hasAllCdelt)
                || hasAllCd == hasAllCdelt) {
            throw new InvalidDataException(
                "ASTAP WCS must contain exactly one complete CD or CDELT/CROTA2 scale model.");
        }

        double xScale;
        double yScale;
        double cd11;
        double cd12;
        double cd21;
        double cd22;
        string model;
        if (hasAllCd) {
            cd11 = RequiredDouble(header, "CD1_1");
            cd12 = RequiredDouble(header, "CD1_2");
            cd21 = RequiredDouble(header, "CD2_1");
            cd22 = RequiredDouble(header, "CD2_2");
            var determinant = cd11 * cd22 - cd12 * cd21;
            if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-18) {
                throw new InvalidDataException("ASTAP WCS CD matrix is singular.");
            }
            xScale = 3600.0 * Math.Sqrt(cd11 * cd11 + cd21 * cd21);
            yScale = 3600.0 * Math.Sqrt(cd12 * cd12 + cd22 * cd22);
            RequireWellConditionedCd(cd11, cd12, cd21, cd22);
            model = "CD";
        } else {
            var cdelt1 = RequiredDouble(header, "CDELT1");
            var cdelt2 = RequiredDouble(header, "CDELT2");
            var rotation = RequiredDouble(header, "CROTA2") * Math.PI / 180.0;
            cd11 = cdelt1 * Math.Cos(rotation);
            cd12 = -cdelt2 * Math.Sin(rotation);
            cd21 = cdelt1 * Math.Sin(rotation);
            cd22 = cdelt2 * Math.Cos(rotation);
            xScale = 3600.0 * Math.Abs(cdelt1);
            yScale = 3600.0 * Math.Abs(cdelt2);
            model = "CDELT-CROTA2";
        }
        if (!double.IsFinite(xScale) || !double.IsFinite(yScale)
                || xScale < MinimumPixelScaleArcseconds
                || yScale < MinimumPixelScaleArcseconds
                || xScale > MaximumPixelScaleArcseconds
                || yScale > MaximumPixelScaleArcseconds) {
            throw new InvalidDataException("ASTAP WCS pixel scale is invalid.");
        }
        var meanScale = 0.5 * (xScale + yScale);
        if (Math.Abs(xScale - yScale) / meanScale
                > MaximumScaleAnisotropyFraction) {
            throw new InvalidDataException("ASTAP WCS pixel-scale anisotropy exceeds 2 percent.");
        }
        var sensorCenter = ProjectTanPixel(ra, dec, cd11, cd12, cd21, cd22,
            0.5 * (width + 1.0) - crpix1, 0.5 * (height + 1.0) - crpix2);
        return new(sensorCenter.RaDegrees, sensorCenter.DecDegrees, observationUtc,
            width, height, xScale, yScale, model);
    }

    private static void RequireWellConditionedCd(double cd11, double cd12,
            double cd21, double cd22) {
        var a = cd11 * cd11 + cd21 * cd21;
        var b = cd11 * cd12 + cd21 * cd22;
        var d = cd12 * cd12 + cd22 * cd22;
        var discriminant = Math.Sqrt(Math.Max(0.0,
            (a - d) * (a - d) + 4.0 * b * b));
        var largest = Math.Sqrt(0.5 * (a + d + discriminant));
        var smallestSquared = 0.5 * (a + d - discriminant);
        if (!double.IsFinite(largest) || smallestSquared <= 0) {
            throw new InvalidDataException("ASTAP WCS CD matrix is ill-conditioned.");
        }
        var smallest = Math.Sqrt(smallestSquared);
        if ((largest - smallest) / (0.5 * (largest + smallest))
                > MaximumScaleAnisotropyFraction) {
            throw new InvalidDataException(
                "ASTAP WCS CD matrix scale/shear condition exceeds 2 percent.");
        }
    }

    private static (double RaDegrees, double DecDegrees) ProjectTanPixel(
            double raDegrees, double decDegrees, double cd11, double cd12,
            double cd21, double cd22, double deltaX, double deltaY) {
        var factor = Math.PI / 180.0;
        var ra0 = raDegrees * factor;
        var dec0 = decDegrees * factor;
        var xi = (cd11 * deltaX + cd12 * deltaY) * factor;
        var eta = (cd21 * deltaX + cd22 * deltaY) * factor;
        var denominator = Math.Cos(dec0) - eta * Math.Sin(dec0);
        var ra = ra0 + Math.Atan2(xi, denominator);
        var dec = Math.Atan2(Math.Sin(dec0) + eta * Math.Cos(dec0),
            Math.Sqrt(denominator * denominator + xi * xi));
        var normalizedRa = ((ra / factor) % 360.0 + 360.0) % 360.0;
        return (normalizedRa, dec / factor);
    }

    private static DateTimeOffset RequiredObservationUtc(
            IReadOnlyDictionary<string, string> header) {
        var value = RequiredString(header, "DATE-OBS");
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)) {
            throw new InvalidDataException("ASTAP WCS DATE-OBS is invalid.");
        }
        return parsed.ToUniversalTime();
    }

    private static string ParseScalar(string value) {
        var withoutComment = value.Split('/', 2)[0].Trim();
        return withoutComment.Length >= 2 && withoutComment[0] == '\''
            && withoutComment[^1] == '\''
                ? withoutComment[1..^1].Replace("''", "'").Trim()
                : withoutComment;
    }

    private static string RequiredString(IReadOnlyDictionary<string, string> header,
            string key) => header.TryGetValue(key, out var value)
            && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidDataException($"ASTAP WCS is missing {key}.");

    private static double RequiredDouble(
            IReadOnlyDictionary<string, string> header, string key) {
        var value = RequiredString(header, key);
        if (!double.TryParse(value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var parsed)
                || !double.IsFinite(parsed)) {
            throw new InvalidDataException($"ASTAP WCS {key} is not finite.");
        }
        return parsed;
    }

    private static int RequiredInt(IReadOnlyDictionary<string, string> header,
            string key) {
        var value = RequiredString(header, key);
        if (!int.TryParse(value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var parsed)) {
            throw new InvalidDataException($"ASTAP WCS {key} is not an integer.");
        }
        return parsed;
    }
}
