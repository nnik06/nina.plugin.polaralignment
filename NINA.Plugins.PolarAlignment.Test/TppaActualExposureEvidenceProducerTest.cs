using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Newtonsoft.Json;

namespace NINA.Plugins.PolarAlignment.Test;

[TestFixture]
public class TppaActualExposureEvidenceProducerTest {
    private TppaActualExposureStarShapeTest fixture;
    private IReadOnlyList<TppaActualExposureFrameSource> sources;
    private string root;

    [SetUp]
    public void SetUp() {
        fixture = new TppaActualExposureStarShapeTest();
        fixture.SetUp();
        root = (string)typeof(TppaActualExposureStarShapeTest)
            .GetField("root", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(fixture)!;
        sources = (IReadOnlyList<TppaActualExposureFrameSource>)
            typeof(TppaActualExposureStarShapeTest)
                .GetMethod("BuildBracket", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(fixture, new object[] { 1.9, 1.8, false, 1.0 })!;
    }

    [TearDown]
    public void TearDown() => fixture.TearDown();

    [Test]
    public void CompleteGuidedBracketProducesNonAuthoritativeReceipt() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:17:30Z"));

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeTrue(string.Join("; ", result.Issues));
        result.Receipt.EvidenceValid.Should().BeTrue();
        result.Receipt.SeeingInclusiveDelivered900SecondStarShapeQualified.Should().BeTrue();
        result.Receipt.PolarAlignmentInferenceQualified.Should().BeFalse();
        result.Receipt.GrantsAbsoluteAccuracyClaim.Should().BeFalse();
        result.Receipt.GrantsMountMotionAuthority.Should().BeFalse();
        result.Receipt.GrantsUpasAuthority.Should().BeFalse();
        result.Receipt.CreatedUtc.Should().Be(manifest.CreatedUtc);
        TppaActualExposureEvidenceProducer.Produce(manifest).ReceiptJson
            .Should().Be(result.ReceiptJson);
    }

    [Test]
    public void DeterministicReceiptVerifierRejectsTampering() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:17:30Z"));
        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        var verified = TppaActualExposureEvidenceReceiptVerifier.Verify(
            manifest, result.ReceiptJson);
        var tampered = TppaActualExposureEvidenceReceiptVerifier.Verify(
            manifest, result.ReceiptJson.Replace("\"EvidenceValid\": true", "\"EvidenceValid\": false"));

        verified.Verified.Should().BeTrue(string.Join("; ", verified.Issues));
        verified.SeeingInclusiveDelivered900SecondStarShapeQualified.Should().BeTrue();
        tampered.Verified.Should().BeFalse();
        tampered.Issues.Should().Contain(issue => issue.Contains("does not exactly match"));
    }

    [Test]
    public void Phd2IntervalMissingPostBracketFailsClosed() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:16:50Z"));

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains("complete exposure bracket"));
    }

    [Test]
    public void TamperedFitsBytesFailHashBinding() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:17:30Z"));
        using (var stream = new FileStream(sources[0].FitsPath, FileMode.Append,
                   FileAccess.Write, FileShare.None)) {
            stream.WriteByte(1);
        }

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains("SHA-256 does not match"));
    }

    private TppaActualExposureEvidenceManifest Manifest(
            DateTime phd2Start, DateTime phd2End) {
        var policy = Policy();
        var policyPath = Path.Combine(root, "policy.json");
        WriteJson(policyPath, policy);
        var astap = Path.Combine(root, "astap.exe");
        File.WriteAllBytes(astap, Encoding.ASCII.GetBytes("pinned-test-astap"));
        var phd2 = Path.Combine(root, "phd2-summary.json");
        WriteJson(phd2, new {
            SchemaVersion = 1,
            EvidenceMode = "GuidedTrackingRollWitness",
            CaptureStartUtc = phd2Start.ToString("o"),
            CaptureCompletedUtc = phd2End.ToString("o"),
            GuidingContinuityQualified = true,
            GuideOutputContinuouslyEnabled = true,
            GuideStepCoverageQualified = true,
            GrantsMountMotionAuthority = false,
            GrantsUpasAuthority = false,
            GrantsAbsoluteAccuracyClaim = false
        });
        var geometry = Path.Combine(root, "oag-geometry.json");
        WriteJson(geometry, new {
            SchemaVersion = 1,
            Model = "orientation-independent-spherical-triangle-upper-bound",
            MainSolutionSha256 = new string('a', 64),
            GuideSolutionSha256 = new string('b', 64),
            GuideToFarthestMainCornerUpperBoundPixels = 3200.0,
            UsesOrientationConvention = false,
            GrantsMotionAuthority = false,
            GrantsAbsolutePolarAccuracyClaim = false
        });
        var statePath = Path.Combine(root, "state.json");
        var state = new Dictionary<string, string> {
            ["targetRaDegrees"] = "100", ["targetDecDegrees"] = "20",
            ["pierSide"] = "East", ["rotatorAngleDegrees"] = "0",
            ["filter"] = "OIII", ["gain"] = "100", ["offset"] = "50",
            ["binning"] = "1x1", ["readoutMode"] = "Default",
            ["focusPosition"] = "12345", ["coolerSetPointC"] = "-10",
            ["trackingMode"] = "Sidereal", ["phd2Profile"] = "OAG-L",
            ["phd2ExposureMs"] = "1500",
            ["phd2AlgorithmStateDigest"] = new string('c', 64)
        };
        WriteJson(statePath, new TppaActualExposureStateReceipt(
            1, policy.OpticalTrainId,
            Utc("2026-08-02T19:59:45Z"), Utc("2026-08-02T20:17:30Z"),
            state, false, false, false));
        var frames = sources.Select(source => new TppaActualExposureEvidenceFrame(
            source.Role, source.FitsPath, Sha256(source.FitsPath),
            source.AstapCatalogPath, Sha256(source.AstapCatalogPath))).ToArray();
        return new(1, "actual-exposure-test", DateTime.UtcNow, policy,
            policyPath, Sha256(policyPath), frames,
            astap, Sha256(astap), "astap.exe -f input.fits -extract2",
            phd2, Sha256(phd2), geometry, Sha256(geometry),
            statePath, Sha256(statePath), 5.0);
    }

    private TppaActualExposureStarShapePolicy Policy() =>
        (TppaActualExposureStarShapePolicy)typeof(TppaActualExposureStarShapeTest)
            .GetMethod("Policy", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(fixture, Array.Empty<object>())!;

    private static DateTime Utc(string value) =>
        DateTime.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind)
            .ToUniversalTime();
    private static void WriteJson(string path, object value) =>
        File.WriteAllText(path, JsonConvert.SerializeObject(value), new UTF8Encoding(false));
    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}