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
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));

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
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
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
    public void TamperedOagMainSolutionSourceFailsHashBinding() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
        var geometry = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(
            manifest.OagGeometryReceiptPath));
        var source = Path.Combine(Path.GetDirectoryName(
            manifest.OagGeometryReceiptPath)!, (string)geometry["MainSolutionSource"]!);
        File.AppendAllText(source, "tamper", new UTF8Encoding(false));

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains(
            "OAG main solution source SHA-256 does not match"));
    }

    [Test]
    public void MissingOagGuideSolutionSourceFailsClosed() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
        var geometry = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(
            manifest.OagGeometryReceiptPath));
        var source = Path.Combine(Path.GetDirectoryName(
            manifest.OagGeometryReceiptPath)!, (string)geometry["GuideSolutionSource"]!);
        File.Delete(source);

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains(
            "OAG geometry source is missing"));
    }

    [Test]
    public void ReusedOagSolutionArtifactFailsClosed() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
        var geometry = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(
            manifest.OagGeometryReceiptPath));
        geometry["GuideSolutionSource"] = geometry["MainSolutionSource"];
        geometry["GuideSolutionSha256"] = geometry["MainSolutionSha256"];
        File.WriteAllText(manifest.OagGeometryReceiptPath, geometry.ToString(),
            new UTF8Encoding(false));
        manifest = manifest with {
            OagGeometryReceiptSha256 = Sha256(manifest.OagGeometryReceiptPath)
        };

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains(
            "must be distinct artifacts"));
    }

    [Test]
    public void TamperedDerivedOagValueFailsIndependentRecomputation() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
        var geometry = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(
            manifest.OagGeometryReceiptPath));
        geometry["GuideToFarthestMainCornerUpperBoundPixels"] =
            geometry.Value<double>("GuideToFarthestMainCornerUpperBoundPixels") + 1.0;
        File.WriteAllText(manifest.OagGeometryReceiptPath, geometry.ToString(),
            new UTF8Encoding(false));
        manifest = manifest with {
            OagGeometryReceiptSha256 = Sha256(manifest.OagGeometryReceiptPath)
        };

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains(
            "does not reproduce its WCS sources"));
    }

    [Test]
    public void OperatorAttestedSchemaOneOagReceiptFailsClosed() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
        var geometry = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(
            manifest.OagGeometryReceiptPath));
        geometry["SchemaVersion"] = 1;
        geometry["GeometryProvenance"] = "operator-attested-diagnostic";
        File.WriteAllText(manifest.OagGeometryReceiptPath, geometry.ToString(),
            new UTF8Encoding(false));
        manifest = manifest with {
            OagGeometryReceiptSha256 = Sha256(manifest.OagGeometryReceiptPath)
        };

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains(
            "unsupported or grants forbidden authority"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void NonportableOagSourcePathFailsClosed(bool rooted) {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
        var geometry = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(
            manifest.OagGeometryReceiptPath));
        geometry["MainSolutionSource"] = rooted
            ? Path.Combine(root, "main-solution.wcs")
            : Path.Combine("..", "main-solution.wcs");
        File.WriteAllText(manifest.OagGeometryReceiptPath, geometry.ToString(),
            new UTF8Encoding(false));
        manifest = manifest with {
            OagGeometryReceiptSha256 = Sha256(manifest.OagGeometryReceiptPath)
        };

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains(rooted
            ? "bundle-relative paths"
            : "escapes the receipt directory"));
    }

    [Test]
    public void UppercaseOagSourceHashesRemainValid() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
        var geometry = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(
            manifest.OagGeometryReceiptPath));
        geometry["MainSolutionSha256"] =
            geometry.Value<string>("MainSolutionSha256")!.ToUpperInvariant();
        geometry["GuideSolutionSha256"] =
            geometry.Value<string>("GuideSolutionSha256")!.ToUpperInvariant();
        File.WriteAllText(manifest.OagGeometryReceiptPath, geometry.ToString(),
            new UTF8Encoding(false));
        manifest = manifest with {
            OagGeometryReceiptSha256 = Sha256(manifest.OagGeometryReceiptPath)
        };

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeTrue(string.Join("; ", result.Issues));
    }

    [Test]
    public void DistortedOagWcsFailsClosedEvenWithUpdatedHashes() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
        var geometry = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(
            manifest.OagGeometryReceiptPath));
        var source = Path.Combine(Path.GetDirectoryName(
            manifest.OagGeometryReceiptPath)!,
            geometry.Value<string>("MainSolutionSource")!);
        File.AppendAllText(source, Environment.NewLine + "PV1_0   = 0",
            new UTF8Encoding(false));
        geometry["MainSolutionSha256"] = Sha256(source);
        File.WriteAllText(manifest.OagGeometryReceiptPath, geometry.ToString(),
            new UTF8Encoding(false));
        manifest = manifest with {
            OagGeometryReceiptSha256 = Sha256(manifest.OagGeometryReceiptPath)
        };

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains(
            "distortion or PC-matrix"));
    }

    [Test]
    public void TimeIncoherentOagSourcesFailEvenWithUpdatedHashes() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
        var geometry = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(
            manifest.OagGeometryReceiptPath));
        var source = Path.Combine(Path.GetDirectoryName(
            manifest.OagGeometryReceiptPath)!,
            geometry.Value<string>("GuideSolutionSource")!);
        var text = File.ReadAllText(source).Replace(
            "2026-08-02T20:00:00Z", "2026-08-02T20:02:00Z");
        File.WriteAllText(source, text, new UTF8Encoding(false));
        geometry["GuideSolutionSha256"] = Sha256(source);
        File.WriteAllText(manifest.OagGeometryReceiptPath, geometry.ToString(),
            new UTF8Encoding(false));
        manifest = manifest with {
            OagGeometryReceiptSha256 = Sha256(manifest.OagGeometryReceiptPath)
        };

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains("not time-coherent"));
    }

    [Test]
    public void DuplicateOagReceiptPropertyFailsClosed() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
        var text = File.ReadAllText(manifest.OagGeometryReceiptPath).TrimEnd();
        text = text[..^1] + ",\"SchemaVersion\":2}";
        File.WriteAllText(manifest.OagGeometryReceiptPath, text,
            new UTF8Encoding(false));
        manifest = manifest with {
            OagGeometryReceiptSha256 = Sha256(manifest.OagGeometryReceiptPath)
        };

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains("cannot be validated"));
    }

    [Test]
    public void UnknownOagReceiptPropertyFailsClosed() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
        var geometry = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(
            manifest.OagGeometryReceiptPath));
        geometry["OperatorOverride"] = true;
        File.WriteAllText(manifest.OagGeometryReceiptPath, geometry.ToString(),
            new UTF8Encoding(false));
        manifest = manifest with {
            OagGeometryReceiptSha256 = Sha256(manifest.OagGeometryReceiptPath)
        };

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains(
            "properties do not exactly match"));
    }

    [Test]
    public void Phd2IntervalMissingPostBracketFailsClosed() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:20Z"));

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains("complete exposure bracket"));
    }

    [Test]
    public void TamperedFitsBytesFailHashBinding() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
        using (var stream = new FileStream(sources[0].FitsPath, FileMode.Append,
                   FileAccess.Write, FileShare.None)) {
            stream.WriteByte(1);
        }

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains("SHA-256 does not match"));
    }

    [Test]
    public void Phd2SummaryWithInvalidatingEventFailsClosed() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
        var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(
            manifest.Phd2SummaryPath));
        json["InvalidatingEvents"] = new Newtonsoft.Json.Linq.JArray("StarLost");
        File.WriteAllText(manifest.Phd2SummaryPath, json.ToString(),
            new UTF8Encoding(false));
        manifest = manifest with {
            Phd2SummarySha256 = Sha256(manifest.Phd2SummaryPath)
        };

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains("event provenance"));
    }

    [Test]
    public void TamperedRawPhd2EventArtifactFailsClosed() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
        File.AppendAllText(Path.Combine(root, "events.jsonl"),
            "{\"Event\":\"StarLost\"}\n", new UTF8Encoding(false));

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains("hash-mismatched"));
    }

    [Test]
    public void HashConsistentGuideStepSequenceGapFailsIndependentValidation() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
        var guideStepsPath = Path.Combine(root, "guidesteps.csv");
        var rows = File.ReadAllLines(guideStepsPath).ToList();
        rows.RemoveAt(10);
        File.WriteAllLines(guideStepsPath, rows, new UTF8Encoding(false));
        var summary = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(
            manifest.Phd2SummaryPath));
        summary["GuideStepsSha256"] = Sha256(guideStepsPath);
        summary["GuideStepCount"] = rows.Count - 1;
        File.WriteAllText(manifest.Phd2SummaryPath, summary.ToString(),
            new UTF8Encoding(false));
        manifest = manifest with {
            Phd2SummarySha256 = Sha256(manifest.Phd2SummaryPath)
        };

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains(
            "independent continuity validation"));
    }

    [Test]
    public void HashConsistentPermissiveCadenceSummaryFailsRawCrossCheck() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
        var summary = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(
            manifest.Phd2SummaryPath));
        summary["MaximumAllowedGuideStepGapSeconds"] = 500.0;
        summary["MaximumObservedGuideStepGapSeconds"] = 400.0;
        File.WriteAllText(manifest.Phd2SummaryPath, summary.ToString(),
            new UTF8Encoding(false));
        manifest = manifest with {
            Phd2SummarySha256 = Sha256(manifest.Phd2SummaryPath)
        };

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains(
            "independent continuity validation"));
    }

    [Test]
    public void HashConsistentTransientStateChangeFailsClosed() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
        var state = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(
            manifest.StateReceiptPath));
        var samples = (Newtonsoft.Json.Linq.JArray)state["Samples"]!;
        ((Newtonsoft.Json.Linq.JObject)samples[samples.Count / 2]!["State"]!)["filter"] = "Ha";
        File.WriteAllText(manifest.StateReceiptPath, state.ToString(),
            new UTF8Encoding(false));
        manifest = manifest with {
            StateReceiptSha256 = Sha256(manifest.StateReceiptPath)
        };

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains(
            "changed state sample"));
    }

    [Test]
    public void HashConsistentLongStateSamplingGapFailsClosed() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
        var state = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(
            manifest.StateReceiptPath));
        var samples = (Newtonsoft.Json.Linq.JArray)state["Samples"]!;
        var middle = samples.Count / 2;
        samples.RemoveAt(middle);
        samples.RemoveAt(middle);
        samples.RemoveAt(middle);
        state["SampleCount"] = samples.Count;
        state["MaximumObservedSampleGapSeconds"] = 20.0;
        File.WriteAllText(manifest.StateReceiptPath, state.ToString(),
            new UTF8Encoding(false));
        manifest = manifest with {
            StateReceiptSha256 = Sha256(manifest.StateReceiptPath)
        };

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains(
            "cadence, coverage, or summary"));
    }

    [Test]
    public void HashConsistentPermissiveStateSummaryFailsRawCrossCheck() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
        var state = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(
            manifest.StateReceiptPath));
        state["MaximumAllowedSampleGapSeconds"] = 500.0;
        state["MaximumObservedSampleGapSeconds"] = 400.0;
        File.WriteAllText(manifest.StateReceiptPath, state.ToString(),
            new UTF8Encoding(false));
        manifest = manifest with {
            StateReceiptSha256 = Sha256(manifest.StateReceiptPath)
        };

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains(
            "cadence, coverage, or summary"));
    }

    [Test]
    public void StatePHD2ConfigurationMismatchFailsCrossCheck() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
        var summary = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(
            manifest.Phd2SummaryPath));
        summary["Phd2Profile"] = "Wrong profile";
        File.WriteAllText(manifest.Phd2SummaryPath, summary.ToString(),
            new UTF8Encoding(false));
        manifest = manifest with {
            Phd2SummarySha256 = Sha256(manifest.Phd2SummaryPath)
        };

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains(
            "does not match its independent summary"));
    }

    [Test]
    public void HashConsistentStateChangingNinaEndpointFailsClosed() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
        var state = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(
            manifest.StateReceiptPath));
        state["StateChangingNinaEndpoints"] = new Newtonsoft.Json.Linq.JArray(
            "equipment/mount/slew");
        File.WriteAllText(manifest.StateReceiptPath, state.ToString(),
            new UTF8Encoding(false));
        manifest = manifest with {
            StateReceiptSha256 = Sha256(manifest.StateReceiptPath)
        };

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains(
            "identity, authority, or bracket coverage"));
    }

    [Test]
    public void HashConsistentActionPathInReadOnlyNinaSetFailsClosed() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
        var state = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(
            manifest.StateReceiptPath));
        var endpoints = (Newtonsoft.Json.Linq.JArray)state["ReadOnlyNinaEndpoints"]!;
        endpoints[0] = "equipment/mount/slew";
        File.WriteAllText(manifest.StateReceiptPath, state.ToString(),
            new UTF8Encoding(false));
        manifest = manifest with {
            StateReceiptSha256 = Sha256(manifest.StateReceiptPath)
        };

        var result = TppaActualExposureEvidenceProducer.Produce(manifest);

        result.Produced.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Contains(
            "identity, authority, or bracket coverage"));
    }

    [Test]
    [NonParallelizable]
    public void StateNumericsRemainInvariantUnderCommaDecimalCulture() {
        var manifest = Manifest(
            Utc("2026-08-02T19:59:50Z"), Utc("2026-08-02T20:18:40Z"));
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        var originalUiCulture = System.Globalization.CultureInfo.CurrentUICulture;
        try {
            var culture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            System.Globalization.CultureInfo.CurrentCulture = culture;
            System.Globalization.CultureInfo.CurrentUICulture = culture;

            var result = TppaActualExposureEvidenceProducer.Produce(manifest);

            result.Produced.Should().BeTrue();
            result.Issues.Should().BeEmpty();
        } finally {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
            System.Globalization.CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private TppaActualExposureEvidenceManifest Manifest(
            DateTime phd2Start, DateTime phd2End) {
        var policy = Policy();
        var policyPath = Path.Combine(root, "policy.json");
        WriteJson(policyPath, policy);
        var astap = Path.Combine(root, "astap.exe");
        File.WriteAllBytes(astap, Encoding.ASCII.GetBytes("pinned-test-astap"));
        var events = Path.Combine(root, "events.jsonl");
        var guideSteps = Path.Combine(root, "guidesteps.csv");
        var stepStart = phd2Start.AddSeconds(1);
        var stepEnd = phd2End.AddSeconds(-1);
        var stepRows = new List<string> {
            "timestamp_utc,monotonic_s,frame,camera_dx_px,camera_dy_px,ra_raw_px,dec_raw_px,ra_guide_px,dec_guide_px,ra_ms,dec_ms,snr,hfd,star_mass,event_json"
        };
        var eventRows = new List<string>();
        var frameNumber = 1L;
        for (var timestamp = stepStart; timestamp <= stepEnd;
                timestamp = timestamp.AddSeconds(5), frameNumber++) {
            stepRows.Add($"{timestamp:o},{1000 + 5 * (frameNumber - 1):F3},{frameNumber},0,0,0,0,0,0,0,0,20,3,1000,{{}} ");
            eventRows.Add($"{{\"Event\":\"GuideStep\",\"Frame\":{frameNumber}}}");
        }
        File.WriteAllLines(events, eventRows, new UTF8Encoding(false));
        File.WriteAllLines(guideSteps, stepRows, new UTF8Encoding(false));
        var phd2 = Path.Combine(root, "phd2-summary.json");
        WriteJson(phd2, new {
            SchemaVersion = 1,
            EvidenceMode = "GuidedTrackingRollWitness",
            CaptureStartUtc = phd2Start.ToString("o"),
            CaptureCompletedUtc = phd2End.ToString("o"),
            GuidingContinuityQualified = true,
            GuideOutputContinuouslyEnabled = true,
            GuideStepCoverageQualified = true,
            GuideStepCount = stepRows.Count - 1,
            GuideStepFrameSequenceContiguous = true,
            GuideStepTimeSequenceMonotonic = true,
            ObservedMedianGuideStepCadenceSeconds = 5.0,
            MaximumAllowedGuideStepGapSeconds = 15.0,
            MaximumObservedGuideStepGapSeconds = 5.0,
            InvalidatingEvents = Array.Empty<string>(),
            StateChangingRpcMethods = Array.Empty<string>(),
            EventsSha256 = Sha256(events),
            GuideStepsSha256 = Sha256(guideSteps),
            Phd2Profile = "OAG-L",
            Phd2ExposureMilliseconds = 1500,
            Phd2AlgorithmStateDigest = new string('c', 64),
            PHD2ConfigurationContinuityQualified = true,
            GrantsMountMotionAuthority = false,
            GrantsUpasAuthority = false,
            GrantsAbsoluteAccuracyClaim = false
        });
        var mainGeometrySource = Path.Combine(root, "main-solution.wcs");
        var guideGeometrySource = Path.Combine(root, "guide-solution.wcs");
        File.WriteAllLines(mainGeometrySource,
            WcsHeader(120.0, 30.0, 6248, 4176, 0.4715),
            new UTF8Encoding(false));
        File.WriteAllLines(guideGeometrySource,
            WcsHeader(120.0002, 30.0001, 1920, 1080, 0.501),
            new UTF8Encoding(false));
        var mainGeometry = TppaAstapWcsGeometryParser.Parse(mainGeometrySource);
        var guideGeometry = TppaAstapWcsGeometryParser.Parse(guideGeometrySource);
        var bound = TppaOagGeometryBoundCalculator.Compute(
            mainGeometry, guideGeometry);
        var geometry = Path.Combine(root, "oag-geometry.json");
        WriteJson(geometry, new {
            SchemaVersion = 2,
            Model = "orientation-independent-spherical-triangle-upper-bound",
            FormulaVersion =
                "orientation-independent-spherical-triangle-upper-bound/v2",
            GeometryProvenance = "derived-astap-wcs",
            MainSolutionSource = Path.GetFileName(mainGeometrySource),
            MainSolutionSha256 = Sha256(mainGeometrySource),
            GuideSolutionSource = Path.GetFileName(guideGeometrySource),
            GuideSolutionSha256 = Sha256(guideGeometrySource),
            MainCenterRightAscensionDegrees = mainGeometry.RightAscensionDegrees,
            MainCenterDeclinationDegrees = mainGeometry.DeclinationDegrees,
            MainObservationUtc = mainGeometry.ObservationUtc.ToString("O"),
            MainWidthPixels = mainGeometry.WidthPixels,
            MainHeightPixels = mainGeometry.HeightPixels,
            MainPixelScaleXArcseconds = mainGeometry.PixelScaleXArcseconds,
            MainPixelScaleYArcseconds = mainGeometry.PixelScaleYArcseconds,
            MainScaleModel = mainGeometry.ScaleModel,
            GuideCenterRightAscensionDegrees = guideGeometry.RightAscensionDegrees,
            GuideCenterDeclinationDegrees = guideGeometry.DeclinationDegrees,
            GuideObservationUtc = guideGeometry.ObservationUtc.ToString("O"),
            GuideWidthPixels = guideGeometry.WidthPixels,
            GuideHeightPixels = guideGeometry.HeightPixels,
            GuidePixelScaleXArcseconds = guideGeometry.PixelScaleXArcseconds,
            GuidePixelScaleYArcseconds = guideGeometry.PixelScaleYArcseconds,
            GuideScaleModel = guideGeometry.ScaleModel,
            ObservationDeltaSeconds = 0.0,
            MaximumObservationDeltaSeconds = 60.0,
            GuideLockOffsetXFromCenterPixels = (double?)null,
            GuideLockOffsetYFromCenterPixels = (double?)null,
            MainPixelScaleUsedArcseconds = bound.MainPixelScaleUsedArcseconds,
            GuidePixelScaleUsedArcseconds = bound.GuidePixelScaleUsedArcseconds,
            CenterSeparationArcseconds = bound.CenterSeparationArcseconds,
            CenterSeparationMainPixels = bound.CenterSeparationMainPixels,
            MainHalfDiagonalPixels = bound.MainHalfDiagonalPixels,
            GuideRadialEvidenceKind = bound.GuideRadialEvidenceKind,
            GuideRadialPixels = bound.GuideRadialPixels,
            GuideRadialArcseconds = bound.GuideRadialArcseconds,
            GuideRadialMainPixels = bound.GuideRadialMainPixels,
            GuideToFarthestMainCornerUpperBoundPixels =
                bound.GuideToFarthestMainCornerUpperBoundPixels,
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
            ["coolerOn"] = "true", ["trackingMode"] = "Sidereal",
            ["trackingEnabled"] = "true", ["mountConnected"] = "true",
            ["cameraConnected"] = "true", ["filterWheelConnected"] = "true",
            ["focuserConnected"] = "true", ["rotatorConnected"] = "true",
            ["mountSlewing"] = "false", ["filterWheelMoving"] = "false",
            ["focuserMoving"] = "false", ["focuserSettling"] = "false",
            ["rotatorMoving"] = "false", ["phd2Profile"] = "OAG-L",
            ["phd2ExposureMs"] = "1500",
            ["phd2AlgorithmStateDigest"] = new string('c', 64)
        };
        var stateStart = Utc("2026-08-02T19:59:45Z");
        var stateEnd = Utc("2026-08-02T20:18:40Z");
        var stateSamples = new List<TppaActualExposureStateSample>();
        for (var timestamp = stateStart; timestamp <= stateEnd;
                timestamp = timestamp.AddSeconds(5)) {
            stateSamples.Add(new(timestamp,
                (timestamp - stateStart).TotalSeconds,
                new Dictionary<string, string>(state)));
        }
        WriteJson(statePath, new TppaActualExposureStateReceipt(
            2, "ReadOnlySampledStateContinuityWitness", policy.OpticalTrainId,
            stateStart, stateEnd, state, stateSamples, stateSamples.Count,
            5.0, 5.0, 15.0, true,
            new[] {
                "equipment/mount/info", "equipment/camera/info",
                "equipment/filterwheel/info", "equipment/focuser/info",
                "equipment/rotator/info"
            }, Array.Empty<string>(), false, false, false));
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
    private static IEnumerable<string> WcsHeader(double ra, double dec,
            int width, int height, double scaleArcseconds) {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var scaleDegrees = scaleArcseconds / 3600.0;
        return new[] {
            "NAXIS   = 2",
            $"NAXIS1  = {width}",
            $"NAXIS2  = {height}",
            "CTYPE1  = 'RA---TAN'",
            "CTYPE2  = 'DEC--TAN'",
            "DATE-OBS= '2026-08-02T20:00:00Z'",
            $"CRVAL1  = {ra.ToString("R", invariant)}",
            $"CRVAL2  = {dec.ToString("R", invariant)}",
            $"CRPIX1  = {(0.5 * (width + 1)).ToString("R", invariant)}",
            $"CRPIX2  = {(0.5 * (height + 1)).ToString("R", invariant)}",
            $"CD1_1   = {(-scaleDegrees).ToString("R", invariant)}",
            "CD1_2   = 0",
            "CD2_1   = 0",
            $"CD2_2   = {scaleDegrees.ToString("R", invariant)}"
        };
    }
    private static void WriteJson(string path, object value) =>
        File.WriteAllText(path, JsonConvert.SerializeObject(value), new UTF8Encoding(false));
    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
