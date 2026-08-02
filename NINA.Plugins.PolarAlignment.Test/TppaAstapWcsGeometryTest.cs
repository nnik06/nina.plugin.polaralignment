using System.IO;
using System.Text;
using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test;

[TestFixture]
public class TppaAstapWcsGeometryTest {
    private string root;

    [SetUp]
    public void SetUp() {
        root = Path.Combine(Path.GetTempPath(), "tppa-wcs-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown() {
        if (Directory.Exists(root)) { Directory.Delete(root, true); }
    }

    [Test]
    public void ParsesStrictCdSolution() {
        var path = Write("cd.wcs", Header(
            "CD1_1  = -0.0001309722",
            "CD1_2  = 0.0",
            "CD2_1  = 0.0",
            "CD2_2  = 0.0001309722"));

        var result = TppaAstapWcsGeometryParser.Parse(path);

        result.RightAscensionDegrees.Should().BeApproximately(123.456, 1e-10);
        result.DeclinationDegrees.Should().BeApproximately(45.678, 1e-10);
        result.ObservationUtc.Should().Be(
            DateTimeOffset.Parse("2026-08-02T20:00:00Z"));
        result.WidthPixels.Should().Be(6248);
        result.HeightPixels.Should().Be(4176);
        result.PixelScaleXArcseconds.Should().BeApproximately(0.4715, 0.0001);
        result.PixelScaleYArcseconds.Should().BeApproximately(0.4715, 0.0001);
        result.ScaleModel.Should().Be("CD");
    }

    [Test]
    public void ParsesStrictCdeltCrotaSolution() {
        var path = Write("cdelt.wcs", Header(
            "CDELT1  = -0.0005633333333333333",
            "CDELT2  = 0.0005633333333333333",
            "CROTA2  = -12.5"));

        var result = TppaAstapWcsGeometryParser.Parse(path);

        result.PixelScaleXArcseconds.Should().BeApproximately(2.028, 0.0001);
        result.PixelScaleYArcseconds.Should().BeApproximately(2.028, 0.0001);
        result.ScaleModel.Should().Be("CDELT-CROTA2");
    }

    [TestCase("CTYPE1  = 'RA---TPV'")]
    [TestCase("A_ORDER = 2")]
    [TestCase("PV1_0   = 0")]
    [TestCase("PC1_1   = 1")]
    [TestCase("NAXIS   = 3")]
    [TestCase("CD1_1   = NaN")]
    [TestCase("DATE-OBS= 'not-a-date'")]
    [TestCase("CD1_1   = -1e-300")]
    public void RejectsUnsupportedOrNonfiniteWcs(string replacement) {
        var lines = Header(
            "CD1_1  = -0.0001309722",
            "CD1_2  = 0.0",
            "CD2_1  = 0.0",
            "CD2_2  = 0.0001309722").ToList();
        var key = replacement[..8].Trim();
        var index = lines.FindIndex(line => line[..8].Trim() == key);
        if (index >= 0) { lines[index] = replacement; } else { lines.Add(replacement); }
        var path = Write("invalid.wcs", lines);

        Action parse = () => TppaAstapWcsGeometryParser.Parse(path);

        parse.Should().Throw<InvalidDataException>();
    }

    [Test]
    public void ProjectsTheGeometricSensorCenterInsteadOfUsingCrval() {
        var lines = Header(
            "CD1_1  = -0.0001309722",
            "CD1_2  = 0.0",
            "CD2_1  = 0.0",
            "CD2_2  = 0.0001309722").ToList();
        lines[lines.FindIndex(line => line.StartsWith("CRPIX1"))] =
            "CRPIX1  = 3000.5";
        var result = TppaAstapWcsGeometryParser.Parse(Write("offset.wcs", lines));

        result.RightAscensionDegrees.Should().NotBeApproximately(123.456, 1e-8);
        Math.Abs(result.DeclinationDegrees - 45.678).Should().BeLessThan(1e-5);
    }

    [Test]
    public void RejectsAHighlyShearedCdMatrixWithEqualColumnNorms() {
        var path = Write("sheared.wcs", Header(
            "CD1_1  = 0.0001309722",
            "CD1_2  = 0.0001309722",
            "CD2_1  = 0.0",
            "CD2_2  = 0.0"));

        Action parse = () => TppaAstapWcsGeometryParser.Parse(path);

        parse.Should().Throw<InvalidDataException>();
    }

    private string Write(string name, IEnumerable<string> lines) {
        var path = Path.Combine(root, name);
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
        return path;
    }

    private static IEnumerable<string> Header(params string[] scale) => new[] {
        "NAXIS   = 2",
        "NAXIS1  = 6248",
        "NAXIS2  = 4176",
        "CTYPE1  = 'RA---TAN'",
        "CTYPE2  = 'DEC--TAN'",
        "DATE-OBS= '2026-08-02T20:00:00Z'",
        "CRVAL1  = 123.456",
        "CRVAL2  = 45.678",
        "CRPIX1  = 3124.5",
        "CRPIX2  = 2088.5"
    }.Concat(scale);
}
