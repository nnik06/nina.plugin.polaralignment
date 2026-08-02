using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NINA.Plugins.PolarAlignment;

internal sealed record TppaActualExposureEvidenceFrame(
    string Role,
    string FitsPath,
    string FitsSha256,
    string AstapCsvPath,
    string AstapCsvSha256);

internal sealed record TppaActualExposureStateReceipt(
    int SchemaVersion,
    string OpticalTrainId,
    DateTime ValidFromUtc,
    DateTime ValidThroughUtc,
    IReadOnlyDictionary<string, string> State,
    bool GrantsMountMotionAuthority,
    bool GrantsUpasAuthority,
    bool GrantsAbsoluteAccuracyClaim);

internal sealed record TppaActualExposureEvidenceManifest(
    int SchemaVersion,
    string CampaignId,
    DateTime CreatedUtc,
    TppaActualExposureStarShapePolicy Policy,
    string PolicySourcePath,
    string PolicySourceSha256,
    IReadOnlyList<TppaActualExposureEvidenceFrame> Frames,
    string AstapExecutablePath,
    string AstapExecutableSha256,
    string AstapInvocationContract,
    string Phd2SummaryPath,
    string Phd2SummarySha256,
    string OagGeometryReceiptPath,
    string OagGeometryReceiptSha256,
    string StateReceiptPath,
    string StateReceiptSha256,
    double MinimumPhd2CoverageMarginSeconds);

internal sealed record TppaActualExposureSourceDigest(
    string Role,
    string FitsSha256,
    string AstapCsvSha256);

internal sealed record TppaActualExposureEvidenceReceipt(
    int SchemaVersion,
    string CampaignId,
    DateTime CreatedUtc,
    string OpticalTrainId,
    DateTime BracketStartedUtc,
    DateTime BracketCompletedUtc,
    string PolicySourceSha256,
    string AstapExecutableSha256,
    string AstapInvocationContract,
    string Phd2SummarySha256,
    string OagGeometryReceiptSha256,
    string StateReceiptSha256,
    IReadOnlyList<TppaActualExposureSourceDigest> Sources,
    TppaActualExposureStarShapeResult StarShape,
    bool EvidenceValid,
    bool SeeingInclusiveDelivered900SecondStarShapeQualified,
    IReadOnlyList<string> Issues,
    bool PolarAlignmentInferenceQualified,
    bool GrantsAbsoluteAccuracyClaim,
    bool GrantsMountMotionAuthority,
    bool GrantsUpasAuthority);

internal sealed record TppaActualExposureEvidenceProductionResult(
    bool Produced,
    TppaActualExposureEvidenceReceipt Receipt,
    string ReceiptJson,
    IReadOnlyList<string> Issues);

internal static class TppaActualExposureEvidenceProducer {
    public const int CurrentManifestSchemaVersion = 1;
    public const int CurrentReceiptSchemaVersion = 1;
    private static readonly string[] RequiredStateKeys = {
        "targetRaDegrees", "targetDecDegrees", "pierSide", "rotatorAngleDegrees",
        "filter", "gain", "offset", "binning", "readoutMode", "focusPosition",
        "coolerSetPointC", "trackingMode", "phd2Profile", "phd2ExposureMs",
        "phd2AlgorithmStateDigest"
    };

    public static TppaActualExposureEvidenceProductionResult Produce(
            TppaActualExposureEvidenceManifest manifest) {
        var issues = new List<string>();
        if (manifest == null) {
            return Invalid("actual-exposure evidence manifest is missing");
        }
        if (manifest.SchemaVersion != CurrentManifestSchemaVersion
                || string.IsNullOrWhiteSpace(manifest.CampaignId)
                || !IsUtc(manifest.CreatedUtc)) {
            issues.Add("actual-exposure manifest identity or UTC creation time is invalid");
        }
        if (!double.IsFinite(manifest.MinimumPhd2CoverageMarginSeconds)
                || manifest.MinimumPhd2CoverageMarginSeconds < 0
                || manifest.MinimumPhd2CoverageMarginSeconds > 120) {
            issues.Add("PHD2 coverage margin is invalid");
        }

        var frames = manifest.Frames?.ToArray()
            ?? Array.Empty<TppaActualExposureEvidenceFrame>();
        var sources = new List<TppaActualExposureFrameSource>();
        var sourceDigests = new List<TppaActualExposureSourceDigest>();
        var fitsDigests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var frame in frames) {
            if (frame == null || (frame.Role != TppaActualExposureRoles.PreControl
                    && frame.Role != TppaActualExposureRoles.LongExposure
                    && frame.Role != TppaActualExposureRoles.PostControl)) {
                issues.Add("frame role is missing or unsupported");
                continue;
            }
            var fitsDigest = ValidateFile(frame.FitsPath, frame.FitsSha256,
                $"{frame.Role} FITS", issues);
            var csvDigest = ValidateFile(frame.AstapCsvPath, frame.AstapCsvSha256,
                $"{frame.Role} ASTAP CSV", issues);
            if (fitsDigest != null && !fitsDigests.Add(fitsDigest)) {
                issues.Add("a FITS artifact was reused within the exposure bracket");
            }
            if (fitsDigest != null && csvDigest != null) {
                sources.Add(new(frame.Role, frame.FitsPath, frame.AstapCsvPath));
                sourceDigests.Add(new(frame.Role, fitsDigest, csvDigest));
            }
        }
        var policyDigest = ValidateFile(manifest.PolicySourcePath,
            manifest.PolicySourceSha256, "star-shape policy source", issues);
        if (policyDigest != null) {
            try {
                var sourcePolicy = JsonConvert.DeserializeObject<TppaActualExposureStarShapePolicy>(
                    File.ReadAllText(manifest.PolicySourcePath), new JsonSerializerSettings {
                        MissingMemberHandling = MissingMemberHandling.Error
                    });
                if (sourcePolicy == null || !JToken.DeepEquals(
                        JToken.FromObject(sourcePolicy), JToken.FromObject(manifest.Policy))) {
                    issues.Add("manifest policy does not equal the hash-bound policy source");
                }
            } catch (Exception exception) {
                issues.Add($"star-shape policy source cannot be parsed strictly: {exception.Message}");
            }
        }
        var astapDigest = ValidateFile(manifest.AstapExecutablePath,
            manifest.AstapExecutableSha256, "ASTAP executable", issues);
        var phd2Digest = ValidateFile(manifest.Phd2SummaryPath,
            manifest.Phd2SummarySha256, "PHD2 summary", issues);
        var geometryDigest = ValidateFile(manifest.OagGeometryReceiptPath,
            manifest.OagGeometryReceiptSha256, "OAG geometry receipt", issues);
        var stateDigest = ValidateFile(manifest.StateReceiptPath,
            manifest.StateReceiptSha256, "state receipt", issues);
        if (string.IsNullOrWhiteSpace(manifest.AstapInvocationContract)
                || !manifest.AstapInvocationContract.Contains("-extract2",
                    StringComparison.Ordinal)) {
            issues.Add("ASTAP invocation contract does not pin -extract2 extraction");
        }

        var intervals = new List<(DateTime Start, DateTime End)>();
        if (issues.Count == 0) {
            foreach (var source in sources) {
                try {
                    using var image = TppaFitsImage.Open(source.FitsPath);
                    intervals.Add((image.DateObsUtc.UtcDateTime,
                        image.DateObsUtc.AddSeconds(image.ExposureSeconds).UtcDateTime));
                } catch (Exception exception) {
                    issues.Add($"FITS interval cannot be read: {exception.Message}");
                }
            }
        }
        var bracketStart = intervals.Count == 0 ? default : intervals.Min(value => value.Start);
        var bracketEnd = intervals.Count == 0 ? default : intervals.Max(value => value.End);
        if (phd2Digest != null && intervals.Count == sources.Count && sources.Count > 0) {
            ValidatePhd2(manifest.Phd2SummaryPath, bracketStart, bracketEnd,
                manifest.MinimumPhd2CoverageMarginSeconds, issues);
        }
        if (geometryDigest != null) {
            ValidateGeometry(manifest.OagGeometryReceiptPath, issues);
        }
        if (stateDigest != null && intervals.Count == sources.Count && sources.Count > 0) {
            ValidateState(manifest.StateReceiptPath, manifest.Policy?.OpticalTrainId,
                bracketStart, bracketEnd, issues);
        }

        TppaActualExposureStarShapeResult starShape = null;
        if (issues.Count == 0) {
            starShape = TppaActualExposureStarShapeAnalyzer.Analyze(manifest.Policy, sources);
            if (!starShape.EvidenceValid) {
                issues.AddRange(starShape.Issues);
            }
        }
        if (issues.Count > 0 || starShape == null) {
            return new(false, null, null, issues);
        }

        var receipt = new TppaActualExposureEvidenceReceipt(
            CurrentReceiptSchemaVersion,
            manifest.CampaignId,
            manifest.CreatedUtc,
            manifest.Policy.OpticalTrainId,
            bracketStart,
            bracketEnd,
            policyDigest,
            astapDigest,
            manifest.AstapInvocationContract,
            phd2Digest,
            geometryDigest,
            stateDigest,
            sourceDigests,
            starShape,
            true,
            starShape.SeeingInclusiveDelivered900SecondStarShapeQualified,
            starShape.Issues,
            PolarAlignmentInferenceQualified: false,
            GrantsAbsoluteAccuracyClaim: false,
            GrantsMountMotionAuthority: false,
            GrantsUpasAuthority: false);
        return new(true, receipt, Serialize(receipt), receipt.Issues);
    }

    private static void ValidatePhd2(string path, DateTime bracketStart,
            DateTime bracketEnd, double marginSeconds, ICollection<string> issues) {
        try {
            var json = ParseJsonWithoutDateCoercion(path);
            if (json.Value<int?>("SchemaVersion") != 1
                    || json.Value<string>("EvidenceMode") != "GuidedTrackingRollWitness"
                    || json.Value<bool?>("GuidingContinuityQualified") != true
                    || json.Value<bool?>("GuideOutputContinuouslyEnabled") != true
                    || json.Value<bool?>("GuideStepCoverageQualified") != true
                    || json.Value<bool?>("GrantsMountMotionAuthority") != false
                    || json.Value<bool?>("GrantsUpasAuthority") != false
                    || json.Value<bool?>("GrantsAbsoluteAccuracyClaim") != false) {
                issues.Add("PHD2 summary does not satisfy the guided continuity contract");
                return;
            }
            if (json["InvalidatingEvents"] is not JArray invalidatingEvents
                    || invalidatingEvents.Count != 0
                    || json["StateChangingRpcMethods"] is not JArray stateChangingMethods
                    || stateChangingMethods.Count != 0
                    || !IsSha256(json.Value<string>("EventsSha256"))
                    || !IsSha256(json.Value<string>("GuideStepsSha256"))) {
                issues.Add("PHD2 summary event provenance is incomplete or contains state changes");
                return;
            }
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            var eventsPath = Path.Combine(directory!, "events.jsonl");
            var guideStepsPath = Path.Combine(directory!, "guidesteps.csv");
            if (!File.Exists(eventsPath) || !File.Exists(guideStepsPath)
                    || !Sha256(File.ReadAllBytes(eventsPath)).Equals(
                        json.Value<string>("EventsSha256"),
                        StringComparison.OrdinalIgnoreCase)
                    || !Sha256(File.ReadAllBytes(guideStepsPath)).Equals(
                        json.Value<string>("GuideStepsSha256"),
                        StringComparison.OrdinalIgnoreCase)) {
                issues.Add("PHD2 raw event or guide-step artifact is missing or hash-mismatched");
                return;
            }
            var start = ParseUtc(json.Value<string>("CaptureStartUtc"));
            var end = ParseUtc(json.Value<string>("CaptureCompletedUtc"));
            if (start > bracketStart.AddSeconds(-marginSeconds)
                    || end < bracketEnd.AddSeconds(marginSeconds)) {
                issues.Add("PHD2 guided evidence does not contain the complete exposure bracket plus margin");
            }
        } catch (Exception exception) {
            issues.Add($"PHD2 summary cannot be validated: {exception.Message}");
        }
    }

    private static void ValidateGeometry(string path, ICollection<string> issues) {
        try {
            var json = ParseJsonWithoutDateCoercion(path);
            if (json.Value<int?>("SchemaVersion") != 1
                    || json.Value<string>("Model")
                        != "orientation-independent-spherical-triangle-upper-bound"
                    || json.Value<bool?>("UsesOrientationConvention") != false
                    || json.Value<bool?>("GrantsMotionAuthority") != false
                    || json.Value<bool?>("GrantsAbsolutePolarAccuracyClaim") != false
                    || !IsSha256(json.Value<string>("MainSolutionSha256"))
                    || !IsSha256(json.Value<string>("GuideSolutionSha256"))
                    || !(json.Value<double?>("GuideToFarthestMainCornerUpperBoundPixels") > 0)) {
                issues.Add("OAG geometry receipt is unsupported or grants forbidden authority");
            }
        } catch (Exception exception) {
            issues.Add($"OAG geometry receipt cannot be validated: {exception.Message}");
        }
    }

    private static void ValidateState(string path, string opticalTrainId,
            DateTime bracketStart, DateTime bracketEnd, ICollection<string> issues) {
        try {
            var state = JsonConvert.DeserializeObject<TppaActualExposureStateReceipt>(
                File.ReadAllText(path), new JsonSerializerSettings {
                    MissingMemberHandling = MissingMemberHandling.Error,
                    DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind
                });
            if (state == null || state.SchemaVersion != 1
                    || state.OpticalTrainId != opticalTrainId
                    || !IsUtc(state.ValidFromUtc) || !IsUtc(state.ValidThroughUtc)
                    || state.ValidFromUtc > bracketStart || state.ValidThroughUtc < bracketEnd
                    || state.GrantsMountMotionAuthority || state.GrantsUpasAuthority
                    || state.GrantsAbsoluteAccuracyClaim
                    || state.State == null
                    || RequiredStateKeys.Any(key => !state.State.TryGetValue(key, out var value)
                        || string.IsNullOrWhiteSpace(value))) {
                issues.Add("state-continuity receipt is incomplete or does not contain the bracket");
            }
        } catch (Exception exception) {
            issues.Add($"state-continuity receipt cannot be validated: {exception.Message}");
        }
    }

    private static string ValidateFile(string path, string expected,
            string label, ICollection<string> issues) {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !IsSha256(expected)) {
            issues.Add($"{label} path or declared SHA-256 is invalid");
            return null;
        }
        var actual = Sha256(File.ReadAllBytes(path));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) {
            issues.Add($"{label} SHA-256 does not match the supplied bytes");
            return null;
        }
        return actual;
    }

    private static JObject ParseJsonWithoutDateCoercion(string path) {
        using var stream = File.OpenText(path);
        using var reader = new JsonTextReader(stream) {
            DateParseHandling = DateParseHandling.None
        };
        return JObject.Load(reader);
    }

    private static DateTime ParseUtc(string value) {
        var parsed = DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
        if (!IsUtc(parsed)) { throw new JsonException("timestamp is not UTC"); }
        return parsed;
    }

    private static bool IsUtc(DateTime value) => value.Kind == DateTimeKind.Utc;
    private static bool IsSha256(string value) => value?.Length == 64
        && value.All(character => Uri.IsHexDigit(character));
    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Serialize(object value) =>
        JsonConvert.SerializeObject(value, Formatting.Indented);
    private static TppaActualExposureEvidenceProductionResult Invalid(string issue) =>
        new(false, null, null, new[] { issue });
}

internal sealed record TppaActualExposureEvidenceVerificationResult(
    bool Verified,
    bool SeeingInclusiveDelivered900SecondStarShapeQualified,
    IReadOnlyList<string> Issues);

internal static class TppaActualExposureEvidenceReceiptVerifier {
    public static TppaActualExposureEvidenceVerificationResult Verify(
            TppaActualExposureEvidenceManifest manifest, string receiptJson) {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(receiptJson)) {
            issues.Add("actual-exposure receipt is empty");
            return new(false, false, issues);
        }

        var current = TppaActualExposureEvidenceProducer.Produce(manifest);
        if (!current.Produced || current.ReceiptJson == null) {
            issues.AddRange(current.Issues);
            issues.Add("current actual-exposure evidence cannot reproduce a receipt");
            return new(false, false, issues);
        }
        if (!string.Equals(current.ReceiptJson, receiptJson, StringComparison.Ordinal)) {
            issues.Add("actual-exposure receipt does not exactly match the deterministic receipt reproduced from its manifest and source artifacts");
            return new(false, false, issues);
        }
        return new(
            true,
            current.Receipt.SeeingInclusiveDelivered900SecondStarShapeQualified,
            Array.Empty<string>());
    }
}
