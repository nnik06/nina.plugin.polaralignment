using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;
using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test;

[TestFixture]
public class TppaActualExposureStarShapeTest {
    private string root;

    [SetUp]
    public void SetUp() {
        root = Path.Combine(Path.GetTempPath(), "tppa-star-shape-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown() {
        if (Directory.Exists(root)) { Directory.Delete(root, true); }
    }

    [Test]
    public void RoundNineHundredSecondFramePassesSeeingInclusiveGate() {
        var sources = BuildBracket(longSigmaX: 1.9, longSigmaY: 1.8);

        var result = TppaActualExposureStarShapeAnalyzer.Analyze(Policy(), sources);

        result.EvidenceValid.Should().BeTrue(string.Join("; ", result.Issues));
        result.ActualLongExposureArtifactPresent.Should().BeTrue();
        result.SeeingInclusiveDelivered900SecondStarShapeQualified.Should()
            .BeTrue(string.Join("; ", result.Issues));
        result.MatchedUsableStars.Should().BeGreaterThanOrEqualTo(25);
        result.Zones.Should().OnlyContain(zone => zone.StarCount >= 3);
        result.PolarAlignmentInferenceQualified.Should().BeFalse();
        result.GrantsAbsoluteAccuracyClaim.Should().BeFalse();
        result.GrantsMountMotionAuthority.Should().BeFalse();
        result.GrantsUpasAuthority.Should().BeFalse();
    }

    [Test]
    public void ElongatedLongFrameFailsWithoutBecomingPolarAlignmentEvidence() {
        var sources = BuildBracket(longSigmaX: 3.0, longSigmaY: 1.8);

        var result = TppaActualExposureStarShapeAnalyzer.Analyze(Policy(), sources);

        result.EvidenceValid.Should().BeTrue();
        result.SeeingInclusiveDelivered900SecondStarShapeQualified.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains("major-axis growth")
            || issue.Contains("differential ellipticity"));
        result.PolarAlignmentInferenceQualified.Should().BeFalse();
        result.GrantsAbsoluteAccuracyClaim.Should().BeFalse();
    }

    [Test]
    public void SingleControlAndMissingPostBracketFailClosed() {
        var sources = BuildBracket(1.9, 1.8)
            .Where(source => source.Role != TppaActualExposureRoles.PostControl)
            .Take(2).ToArray();

        var result = TppaActualExposureStarShapeAnalyzer.Analyze(Policy(), sources);

        result.EvidenceValid.Should().BeFalse();
        result.ActualLongExposureArtifactPresent.Should().BeFalse();
        result.SeeingInclusiveDelivered900SecondStarShapeQualified.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains("split pre/post control bracket"));
    }

    [Test]
    public void ChangedFilterStateFailsBeforeShapeReduction() {
        var sources = BuildBracket(1.9, 1.8, changedLongFilter: true);

        var result = TppaActualExposureStarShapeAnalyzer.Analyze(Policy(), sources);

        result.EvidenceValid.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains("FILTER changed"));
    }

    [Test]
    public void UndersampledTrainNeverGetsAbsoluteEccentricityAuthority() {
        var policy = Policy();
        policy.OpticalTrainId = "WO-GT81-IV-0.8-ASI2600MM";
        policy.PixelScaleArcsecondsPerPixel = 2.03;
        policy.ApertureRadiusArcseconds = 10.15;
        policy.BackgroundInnerRadiusArcseconds = 14.21;
        policy.BackgroundOuterRadiusArcseconds = 20.30;
        policy.BlendExclusionRadiusArcseconds = 50.0;
        policy.AbsoluteEccentricityQualified = false;
        var sources = BuildBracket(1.9, 1.8, pixelScale: 2.03);

        var result = TppaActualExposureStarShapeAnalyzer.Analyze(policy, sources);

        result.AbsoluteEccentricityQualified.Should().BeFalse();
        result.PolarAlignmentInferenceQualified.Should().BeFalse();
    }

    [Test]
    public void LongFrameAttritionFailsEvenWhenSurvivorFloorPasses() {
        var policy = Policy();
        policy.MinimumMatchedStars = 15;
        var sources = BuildBracketAdvanced(1.9, 1.8, false, 1.0, 1.0,
            longStarCount: 22, degradedOuterNe: false,
            nonfiniteLongStars: 0, controlOutlierIndex: -1);

        var result = TppaActualExposureStarShapeAnalyzer.Analyze(policy, sources);

        result.EvidenceValid.Should().BeTrue();
        result.MatchedUsableStars.Should().BeGreaterThanOrEqualTo(15);
        result.LongFrameAttritionFraction.Should().BeGreaterThan(0.15);
        result.SeeingInclusiveDelivered900SecondStarShapeQualified.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains("star attrition"));
    }

    [Test]
    public void SingleOuterQuadrantDegradationCannotHideBehindPooledMedian() {
        var sources = BuildBracketAdvanced(1.9, 1.8, false, 1.0, 1.0,
            StarPositions.Length, degradedOuterNe: true,
            nonfiniteLongStars: 0, controlOutlierIndex: -1);

        var result = TppaActualExposureStarShapeAnalyzer.Analyze(Policy(), sources);

        result.EvidenceValid.Should().BeTrue();
        result.SeeingInclusiveDelivered900SecondStarShapeQualified.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains("outer-zone"));
    }

    [Test]
    public void IncorrectWcsScaleFailsBeforeShapeQualification() {
        var sources = BuildBracketAdvanced(1.9, 1.8, false, 1.0, 1.10,
            StarPositions.Length, false, 0, -1);

        var result = TppaActualExposureStarShapeAnalyzer.Analyze(Policy(), sources);

        result.EvidenceValid.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains("pixel scale disagrees"));
    }

    [Test]
    public void NonfiniteLongFramePixelsBecomeMeasuredAttrition() {
        var policy = Policy();
        policy.MinimumMatchedStars = 15;
        var sources = BuildBracketAdvanced(1.9, 1.8, false, 1.0, 1.0,
            StarPositions.Length, false, nonfiniteLongStars: 6,
            controlOutlierIndex: -1);

        var result = TppaActualExposureStarShapeAnalyzer.Analyze(policy, sources);

        result.LongFrameRejectedStars.Should().BeGreaterThanOrEqualTo(5);
        result.LongFrameAttritionFraction.Should().BeGreaterThan(0.15);
        result.SeeingInclusiveDelivered900SecondStarShapeQualified.Should().BeFalse();
    }

    [Test]
    public void UndersampledAdaptiveMomentsNeverQualifyDeliveredShape() {
        var sources = BuildBracketAdvanced(0.75, 0.75, false, 1.0, 1.0,
            StarPositions.Length, false, 0, -1, controlSigma: 0.75);

        var result = TppaActualExposureStarShapeAnalyzer.Analyze(Policy(), sources);

        result.SeeingInclusiveDelivered900SecondStarShapeQualified.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains("undersampled")
            || issue.Contains("star floor") || issue.Contains("attrition"));
    }

    [Test]
    public void OneUnstableControlFrameFailsScatterGate() {
        var sources = BuildBracketAdvanced(1.9, 1.8, false, 1.0, 1.0,
            StarPositions.Length, false, 0, controlOutlierIndex: 2);

        var result = TppaActualExposureStarShapeAnalyzer.Analyze(Policy(), sources);

        result.EvidenceValid.Should().BeTrue();
        result.SeeingInclusiveDelivered900SecondStarShapeQualified.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains("control-frame star-shape scatter"));
    }

    private TppaActualExposureStarShapePolicy Policy() => new() {
        OpticalTrainId = "EdgeHD-9.25-0.7-ASI2600MM",
        PixelScaleArcsecondsPerPixel = 1.0,
        GainElectronsPerAdu = 0.25,
        ApertureRadiusArcseconds = 8.0,
        BackgroundInnerRadiusArcseconds = 10.0,
        BackgroundOuterRadiusArcseconds = 14.0,
        BlendExclusionRadiusArcseconds = 24.0,
        LinearityCeilingAdu = 50000,
        MinimumApertureSnr = 20,
        MinimumMatchedStars = 25,
        MinimumStarsPerZone = 3,
        MaximumMedianMajorSigmaGrowthFraction = 0.20,
        MaximumOuterMedianMajorSigmaGrowthFraction = 0.25,
        MaximumMedianDifferentialEllipticity = 0.15,
        MaximumPrePostMajorSigmaDriftFraction = 0.10,
        AbsoluteEccentricityQualified = true,
        MaximumLongMedianEccentricity = 0.60
    };

    private IReadOnlyList<TppaActualExposureFrameSource> BuildBracket(
            double longSigmaX, double longSigmaY,
            bool changedLongFilter = false, double pixelScale = 1.0) =>
        BuildBracketAdvanced(longSigmaX, longSigmaY, changedLongFilter,
            pixelScale, pixelScale, StarPositions.Length, false, 0, -1);

    private IReadOnlyList<TppaActualExposureFrameSource> BuildBracketAdvanced(
            double longSigmaX, double longSigmaY, bool changedLongFilter,
            double catalogPixelScale, double wcsPixelScale, int longStarCount,
            bool degradedOuterNe, int nonfiniteLongStars,
            int controlOutlierIndex, double controlSigma = 1.8) {
        var starts = new[] {
            DateTimeOffset.Parse("2026-08-02T20:00:00Z"),
            DateTimeOffset.Parse("2026-08-02T20:00:20Z"),
            DateTimeOffset.Parse("2026-08-02T20:00:40Z"),
            DateTimeOffset.Parse("2026-08-02T20:01:00Z"),
            DateTimeOffset.Parse("2026-08-02T20:01:20Z"),
            DateTimeOffset.Parse("2026-08-02T20:01:40Z"),
            DateTimeOffset.Parse("2026-08-02T20:16:55Z"),
            DateTimeOffset.Parse("2026-08-02T20:17:15Z"),
            DateTimeOffset.Parse("2026-08-02T20:17:35Z"),
            DateTimeOffset.Parse("2026-08-02T20:17:55Z"),
            DateTimeOffset.Parse("2026-08-02T20:18:15Z")
        };
        var roles = new[] {
            TppaActualExposureRoles.PreControl,
            TppaActualExposureRoles.PreControl,
            TppaActualExposureRoles.PreControl,
            TppaActualExposureRoles.PreControl,
            TppaActualExposureRoles.PreControl,
            TppaActualExposureRoles.LongExposure,
            TppaActualExposureRoles.PostControl,
            TppaActualExposureRoles.PostControl,
            TppaActualExposureRoles.PostControl,
            TppaActualExposureRoles.PostControl,
            TppaActualExposureRoles.PostControl
        };
        var result = new List<TppaActualExposureFrameSource>();
        for (var index = 0; index < roles.Length; index++) {
            var isLong = roles[index] == TppaActualExposureRoles.LongExposure;
            var outlier = !isLong && index == controlOutlierIndex;
            var sigmaX = isLong ? longSigmaX : outlier ? 2.8 : controlSigma;
            var sigmaY = isLong ? longSigmaY : outlier ? 2.8 : controlSigma;
            var exposure = isLong ? 900.0 : 15.0;
            var filter = isLong && changedLongFilter ? "Ha" : "OIII";
            var fits = Path.Combine(root, $"frame-{index}.fits");
            var csv = Path.Combine(root, $"frame-{index}.csv");
            var positions = isLong
                ? StarPositions.Take(longStarCount).ToArray()
                : StarPositions;
            WriteFits(fits, starts[index], exposure, filter, sigmaX, sigmaY,
                wcsPixelScale, positions, isLong && degradedOuterNe,
                isLong ? nonfiniteLongStars : 0);
            WriteCatalog(csv, catalogPixelScale, positions);
            result.Add(new(roles[index], fits, csv));
        }
        return result;
    }

    private static readonly (double X, double Y)[] StarPositions = {
        (100,100),(128,100),(156,100),(114,128),(142,128),
        (30,190),(60,190),(30,220),(60,220),
        (196,190),(226,190),(196,220),(226,220),
        (30,36),(60,36),(30,66),(60,66),
        (196,36),(226,36),(196,66),(226,66),
        (90,145),(118,155),(146,145),(174,128),
        (90,180),(170,180)
    };

    private static void WriteCatalog(string path, double pixelScale,
            IReadOnlyList<(double X, double Y)> positions) {
        var lines = new List<string> { "x,y,hfd,snr,flux,ra[0..360],dec[0..360]" };
        var dec0 = 20.0;
        foreach (var (x, y) in positions) {
            var ra = 100.0 + (x - 128.0) * pixelScale
                / (3600.0 * Math.Cos(dec0 * Math.PI / 180.0));
            var dec = dec0 + (y - 128.0) * pixelScale / 3600.0;
            lines.Add(string.Join(",", new[] { x, y, 4.5, 200, 50000, ra, dec }
                .Select(value => value.ToString("0.########", CultureInfo.InvariantCulture))));
        }
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static void WriteFits(string path, DateTimeOffset start,
            double exposure, string filter, double sigmaX, double sigmaY,
            double pixelScale, IReadOnlyList<(double X, double Y)> positions,
            bool degradedOuterNe, int nonfiniteStars) {
        const int width = 256, height = 256;
        var pixels = new float[width * height];
        for (var y = 0; y < height; y++) {
            for (var x = 0; x < width; x++) {
                var value = 1000.0 + 0.03 * x + 0.02 * y;
                foreach (var (sx, sy) in positions) {
                    var localSigmaX = degradedOuterNe && sx > 128 && sy > 128
                        ? 3.5 : sigmaX;
                    var dx = x + 1 - sx; var dy = y + 1 - sy;
                    value += 8000.0 * Math.Exp(-0.5 * (dx * dx / (localSigmaX * localSigmaX)
                        + dy * dy / (sigmaY * sigmaY)));
                }
                pixels[y * width + x] = (float)value;
            }
        }
        foreach (var (sx, sy) in positions.Take(nonfiniteStars)) {
            var x = (int)Math.Round(sx) - 1;
            var y = (int)Math.Round(sy) - 1;
            pixels[y * width + x] = float.NaN;
        }
        var cards = new List<string> {
            Card("SIMPLE", "T"), Card("BITPIX", "-32"), Card("NAXIS", "2"),
            Card("NAXIS1", width.ToString(CultureInfo.InvariantCulture)),
            Card("NAXIS2", height.ToString(CultureInfo.InvariantCulture)),
            Card("EXPTIME", exposure.ToString("0.###", CultureInfo.InvariantCulture)),
            Card("DATE-OBS", Quote(start.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))),
            Card("INSTRUME", Quote("ASI2600MM Pro")), Card("FILTER", Quote(filter)),
            Card("XBINNING", "1"), Card("YBINNING", "1"),
            Card("GAIN", "100"), Card("OFFSET", "50"),
            Card("CDELT1", (-pixelScale / 3600.0).ToString("0.############", CultureInfo.InvariantCulture)),
            Card("CDELT2", (pixelScale / 3600.0).ToString("0.############", CultureInfo.InvariantCulture)),
            "END".PadRight(80)
        };
        var header = Encoding.ASCII.GetBytes(string.Concat(cards));
        var headerLength = ((header.Length + 2879) / 2880) * 2880;
        var dataLength = pixels.Length * 4;
        var paddedDataLength = ((dataLength + 2879) / 2880) * 2880;
        var bytes = new byte[headerLength + paddedDataLength];
        Array.Fill(bytes, (byte)' ', 0, headerLength);
        Buffer.BlockCopy(header, 0, bytes, 0, header.Length);
        for (var index = 0; index < pixels.Length; index++) {
            BinaryPrimitives.WriteInt32BigEndian(
                bytes.AsSpan(headerLength + index * 4, 4),
                BitConverter.SingleToInt32Bits(pixels[index]));
        }
        File.WriteAllBytes(path, bytes);
    }

    private static string Card(string key, string value) =>
        (key.PadRight(8) + "= " + value).PadRight(80);
    private static string Quote(string value) => "'" + value + "'";
}
