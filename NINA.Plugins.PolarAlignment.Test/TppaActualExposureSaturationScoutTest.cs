using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test;

[TestFixture]
public sealed class TppaActualExposureSaturationScoutTest {
    [Test]
    public void TwoPointScoutPassesWithPedestalSafeProjection() {
        using var fixture = new Fixture();
        var result = fixture.Analyze();
        Assert.That(result.Verdict, Is.EqualTo(TppaSaturationScoutVerdicts.Pass));
        Assert.That(result.MedianImpliedPedestalAdu, Is.EqualTo(500).Within(0.01));
        Assert.That(result.MedianProjectedBackgroundAdu, Is.EqualTo(9500).Within(1));
        Assert.That(result.PredictsGuidingOrStarShapeSuccess, Is.False);
        Assert.That(result.GrantsSequenceStartAuthority, Is.False);
    }

    [Test]
    public void FilterMismatchIsInconclusive() {
        using var fixture = new Fixture(longFilter: "Ha 3nm");
        Assert.That(fixture.Analyze().Verdict,
            Is.EqualTo(TppaSaturationScoutVerdicts.Inconclusive));
    }

    [Test]
    public void BlownProjectedBackgroundFails() {
        using var fixture = new Fixture(skyRateAduPerSecond: 30.0);
        var result = fixture.Analyze();
        Assert.That(result.Verdict, Is.EqualTo(TppaSaturationScoutVerdicts.Fail));
        Assert.That(result.Issues, Has.Some.Contains("background exceeds"));
    }

    [Test]
    public void SparseBrightStarCoreIsExempt() {
        using var fixture = new Fixture(addBrightStar: true);
        var result = fixture.Analyze();
        Assert.That(result.Verdict, Is.EqualTo(TppaSaturationScoutVerdicts.Pass),
            string.Join("; ", result.Issues));
        Assert.That(result.ProjectedExemptBrightSources, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void ExtendedSaturatedRegionFails() {
        using var fixture = new Fixture(addExtendedSaturation: true);
        var result = fixture.Analyze();
        Assert.That(result.Verdict, Is.EqualTo(TppaSaturationScoutVerdicts.Fail));
        Assert.That(result.ProjectedResidualSaturatedFraction,
            Is.GreaterThan(fixture.Policy.MaximumProjectedResidualSaturatedFraction));
    }

    [Test]
    public void StaleScoutIsInconclusive() {
        using var fixture = new Fixture(ageMinutes: 30);
        Assert.That(fixture.Analyze().Verdict,
            Is.EqualTo(TppaSaturationScoutVerdicts.Inconclusive));
    }

    [Test]
    public void PlaceholderFilterPolicyIsInconclusive() {
        using var fixture = new Fixture();
        fixture.Policy.RequiredFilterName = "REPLACE-WITH-EXACT-FILTER-NAME";
        fixture.Policy.AllowedFilterNames = new[] { fixture.Policy.RequiredFilterName };
        Assert.That(fixture.Analyze().Verdict,
            Is.EqualTo(TppaSaturationScoutVerdicts.Inconclusive));
    }

    private sealed class Fixture : IDisposable {
        private readonly string root;
        private readonly string shortPath;
        private readonly string longPath;
        private readonly DateTime now;

        public Fixture(double skyRateAduPerSecond = 10.0,
                string longFilter = "OIII 3nm", bool addBrightStar = false,
                bool addExtendedSaturation = false, double ageMinutes = 1) {
            root = Path.Combine(Path.GetTempPath(), "tppa-scout-" + Guid.NewGuid());
            Directory.CreateDirectory(root);
            shortPath = Path.Combine(root, "short.fits");
            longPath = Path.Combine(root, "long.fits");
            now = new DateTime(2026, 8, 3, 20, 10, 0, DateTimeKind.Utc);
            var shortObserved = now.AddMinutes(-ageMinutes).AddSeconds(-70);
            var longObserved = now.AddMinutes(-ageMinutes);
            WriteFits(shortPath, shortObserved, 10, "OIII 3nm",
                500 + 10 * skyRateAduPerSecond, addBrightStar,
                addExtendedSaturation);
            WriteFits(longPath, longObserved, 60, longFilter,
                500 + 60 * skyRateAduPerSecond, addBrightStar,
                addExtendedSaturation);
            Policy = new() {
                OpticalTrainId = "WO-GT81-IV-0.8-OAG-L-ASI2600MM-gain100-bin1",
                RequiredFilterName = "OIII 3nm",
                AllowedFilterNames = new[] { "OIII 3nm", "Ha 3nm" },
                MaximumExemptBrightSourceFraction = 0.01
            };
        }

        public TppaActualExposureScoutPolicy Policy { get; }

        public TppaActualExposureSaturationScoutReceipt Analyze() =>
            TppaActualExposureSaturationScoutAnalyzer.Analyze(
                shortPath, longPath, new string('a', 64), new string('b', 64),
                Policy, now);

        public void Dispose() {
            try { Directory.Delete(root, true); } catch { }
        }

        private static void WriteFits(string path, DateTime observed,
                double exposure, string filter, double background,
                bool brightStar, bool extended) {
            const int width = 256;
            const int height = 256;
            var pixels = Enumerable.Repeat((float)background, width * height).ToArray();
            if (brightStar) {
                for (var y = 122; y <= 134; y++) {
                    for (var x = 122; x <= 134; x++) {
                        var dx = x - 128;
                        var dy = y - 128;
                        pixels[y * width + x] += (float)(50000
                            * Math.Exp(-0.5 * (dx * dx + dy * dy) / 4.0));
                    }
                }
            }
            if (extended) {
                for (var y = 80; y < 130; y++) {
                    for (var x = 80; x < 130; x++) {
                        pixels[y * width + x] = 60000;
                    }
                }
            }
            var cards = new List<string> {
                Card("SIMPLE", "T"), Card("BITPIX", "-32"), Card("NAXIS", "2"),
                Card("NAXIS1", width.ToString(CultureInfo.InvariantCulture)),
                Card("NAXIS2", height.ToString(CultureInfo.InvariantCulture)),
                Card("EXPTIME", exposure.ToString("0.###", CultureInfo.InvariantCulture)),
                Card("DATE-OBS", Quote(observed.ToString(
                    "yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))),
                Card("INSTRUME", Quote("ASI2600MM Pro")),
                Card("FILTER", Quote(filter)), Card("XBINNING", "1"),
                Card("YBINNING", "1"), Card("GAIN", "100"),
                Card("OFFSET", "50"), "END".PadRight(80)
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
}
