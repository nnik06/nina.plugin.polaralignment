using System.Globalization;
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

internal sealed record TppaActualExposureStateSample(
    DateTime TimestampUtc,
    double MonotonicSeconds,
    IReadOnlyDictionary<string, string> State);

internal sealed record TppaActualExposureStateReceipt(
    int SchemaVersion,
    string EvidenceMode,
    string OpticalTrainId,
    DateTime CaptureStartUtc,
    DateTime CaptureCompletedUtc,
    IReadOnlyDictionary<string, string> BaselineState,
    IReadOnlyList<TppaActualExposureStateSample> Samples,
    int SampleCount,
    double ObservedMedianSampleCadenceSeconds,
    double MaximumObservedSampleGapSeconds,
    double MaximumAllowedSampleGapSeconds,
    bool StateContinuityQualified,
    IReadOnlyList<string> ReadOnlyNinaEndpoints,
    IReadOnlyList<string> StateChangingNinaEndpoints,
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
    private const int CurrentStateSchemaVersion = 2;
    private const int MinimumStateSamples = 10;
    private const double MaximumStateGapSeconds = 15.0;
    private const double MaximumTargetCoordinateDeltaDegrees = 1.0 / 60.0;
    private const double MaximumRotatorDeltaDegrees = 0.02;
    private const double MaximumCoolerSetPointDeltaC = 0.1;
    private const double MaximumGeometryObservationDeltaSeconds = 60.0;
    private const int MaximumGeometrySourceBytes = 1_000_000;
    private static readonly HashSet<string> GeometryReceiptProperties = new(
        StringComparer.Ordinal) {
            "SchemaVersion", "Model", "FormulaVersion", "GeometryProvenance",
            "MainSolutionSource", "MainSolutionSha256", "GuideSolutionSource",
            "GuideSolutionSha256", "MainCenterRightAscensionDegrees",
            "MainCenterDeclinationDegrees", "MainObservationUtc", "MainWidthPixels",
            "MainHeightPixels", "MainPixelScaleXArcseconds",
            "MainPixelScaleYArcseconds", "MainScaleModel",
            "GuideCenterRightAscensionDegrees", "GuideCenterDeclinationDegrees",
            "GuideObservationUtc", "GuideWidthPixels", "GuideHeightPixels",
            "GuidePixelScaleXArcseconds", "GuidePixelScaleYArcseconds",
            "GuideScaleModel", "ObservationDeltaSeconds",
            "MaximumObservationDeltaSeconds", "GuideLockOffsetXFromCenterPixels",
            "GuideLockOffsetYFromCenterPixels", "MainPixelScaleUsedArcseconds",
            "GuidePixelScaleUsedArcseconds", "CenterSeparationArcseconds",
            "CenterSeparationMainPixels", "MainHalfDiagonalPixels",
            "GuideRadialEvidenceKind", "GuideRadialPixels",
            "GuideRadialArcseconds", "GuideRadialMainPixels",
            "GuideToFarthestMainCornerUpperBoundPixels", "UsesOrientationConvention",
            "GrantsMotionAuthority", "GrantsAbsolutePolarAccuracyClaim"
        };
    private static readonly string[] RequiredReadOnlyNinaEndpoints = {
        "equipment/mount/info", "equipment/camera/info",
        "equipment/filterwheel/info", "equipment/focuser/info",
        "equipment/rotator/info"
    };
    private static readonly string[] RequiredStateKeys = {
        "targetRaDegrees", "targetDecDegrees", "pierSide", "rotatorAngleDegrees",
        "filter", "gain", "offset", "binning", "readoutMode", "focusPosition",
        "coolerSetPointC", "coolerOn", "trackingMode", "trackingEnabled",
        "mountConnected", "cameraConnected", "filterWheelConnected",
        "focuserConnected", "rotatorConnected", "mountSlewing",
        "filterWheelMoving", "focuserMoving", "focuserSettling",
        "rotatorMoving", "phd2Profile", "phd2ExposureMs",
        "phd2AlgorithmStateDigest"
    };
    private static readonly IReadOnlyDictionary<string, string>
        RequiredInvariantState = new Dictionary<string, string> {
            ["coolerOn"] = "true",
            ["trackingMode"] = "Sidereal",
            ["trackingEnabled"] = "true",
            ["mountConnected"] = "true",
            ["cameraConnected"] = "true",
            ["filterWheelConnected"] = "true",
            ["focuserConnected"] = "true",
            ["rotatorConnected"] = "true",
            ["mountSlewing"] = "false",
            ["filterWheelMoving"] = "false",
            ["focuserMoving"] = "false",
            ["focuserSettling"] = "false",
            ["rotatorMoving"] = "false"
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
            ValidateState(manifest.StateReceiptPath, manifest.Phd2SummaryPath,
                manifest.Policy?.OpticalTrainId, bracketStart, bracketEnd, issues);
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
                    || json.Value<bool?>("PHD2ConfigurationContinuityQualified") != true
                    || string.IsNullOrWhiteSpace(json.Value<string>("Phd2Profile"))
                    || !(json.Value<int?>("Phd2ExposureMilliseconds") > 0)
                    || !IsSha256(json.Value<string>("Phd2AlgorithmStateDigest"))
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
            var start = ParseUtc(json.Value<string>("CaptureStartUtc"));
            var end = ParseUtc(json.Value<string>("CaptureCompletedUtc"));
            if (start > bracketStart.AddSeconds(-marginSeconds)
                    || end < bracketEnd.AddSeconds(marginSeconds)) {
                issues.Add("PHD2 guided evidence does not contain the complete exposure bracket plus margin");
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
            if (!ValidateGuideSteps(guideStepsPath, json, bracketStart,
                    bracketEnd, out var guideStepIssue)) {
                issues.Add(guideStepIssue);
                return;
            }
        } catch (Exception exception) {
            issues.Add($"PHD2 summary cannot be validated: {exception.Message}");
        }
    }

    private static bool ValidateGuideSteps(string path, JObject summary,
            DateTime bracketStart, DateTime bracketEnd, out string issue) {
        issue = "PHD2 guide-step artifact fails independent continuity validation";
        var lines = File.ReadAllLines(path);
        const string expectedHeader = "timestamp_utc,monotonic_s,frame,camera_dx_px,camera_dy_px,ra_raw_px,dec_raw_px,ra_guide_px,dec_guide_px,ra_ms,dec_ms,snr,hfd,star_mass,event_json";
        if (lines.Length < 11 || lines[0] != expectedHeader) { return false; }
        var timestamps = new List<DateTime>(lines.Length - 1);
        var monotonic = new List<double>(lines.Length - 1);
        var frames = new List<long>(lines.Length - 1);
        foreach (var line in lines.Skip(1)) {
            var columns = line.Split(',', 4);
            if (columns.Length < 4
                    || !DateTime.TryParse(columns[0], CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var timestamp)
                    || !IsUtc(timestamp)
                    || !double.TryParse(columns[1], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var monotonicSeconds)
                    || !double.IsFinite(monotonicSeconds)
                    || !long.TryParse(columns[2], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var frame)) {
                return false;
            }
            timestamps.Add(timestamp);
            monotonic.Add(monotonicSeconds);
            frames.Add(frame);
        }
        var gaps = new List<double>(frames.Count - 1);
        for (var index = 1; index < frames.Count; index++) {
            if (frames[index] != frames[index - 1] + 1
                    || timestamps[index] <= timestamps[index - 1]
                    || monotonic[index] <= monotonic[index - 1]) {
                return false;
            }
            gaps.Add(monotonic[index] - monotonic[index - 1]);
        }
        var medianCadence = Median(gaps);
        var maximumGap = gaps.Max();
        var maximumAllowedGap = 3.0 * medianCadence;
        var reportedMaximumAllowedGap = summary.Value<double?>(
            "MaximumAllowedGuideStepGapSeconds");
        var reportedMaximumObservedGap = summary.Value<double?>(
            "MaximumObservedGuideStepGapSeconds");
        if (!double.IsFinite(medianCadence) || medianCadence <= 0
                || maximumGap > maximumAllowedGap
                || timestamps[0] > bracketStart
                || timestamps[^1] < bracketEnd
                || summary.Value<long?>("GuideStepCount") != frames.Count
                || summary.Value<bool?>("GuideStepFrameSequenceContiguous") != true
                || summary.Value<bool?>("GuideStepTimeSequenceMonotonic") != true
                || !NearlyEqual(summary.Value<double?>(
                    "ObservedMedianGuideStepCadenceSeconds"), medianCadence)
                || !NearlyEqual(reportedMaximumAllowedGap,
                    maximumAllowedGap)
                || !WithinReportedRange(reportedMaximumObservedGap,
                    maximumGap, maximumAllowedGap)) {
            return false;
        }
        issue = string.Empty;
        return true;
    }

    private static double Median(IEnumerable<double> values) {
        var ordered = values.OrderBy(value => value).ToArray();
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 1 ? ordered[middle]
            : 0.5 * (ordered[middle - 1] + ordered[middle]);
    }

    private static bool NearlyEqual(double? left, double right) =>
        left.HasValue && double.IsFinite(left.Value)
        && Math.Abs(left.Value - right) <= Math.Max(0.001, 0.01 * right);

    private static bool WithinReportedRange(double? value, double minimum,
            double maximum) {
        if (!value.HasValue || !double.IsFinite(value.Value)) { return false; }
        var tolerance = Math.Max(0.001, 0.01 * maximum);
        return value.Value >= minimum - tolerance
            && value.Value <= maximum + tolerance;
    }

    private static void ValidateGeometry(string path, ICollection<string> issues) {
        try {
            var json = ParseJsonWithoutDateCoercion(path);
            var mainSource = json.Value<string>("MainSolutionSource");
            var guideSource = json.Value<string>("GuideSolutionSource");
            var mainSha256 = json.Value<string>("MainSolutionSha256");
            var guideSha256 = json.Value<string>("GuideSolutionSha256");
            RequireExactGeometryProperties(json);
            if (json.Value<int?>("SchemaVersion") != 2
                    || json.Value<string>("Model")
                        != "orientation-independent-spherical-triangle-upper-bound"
                    || json.Value<string>("FormulaVersion")
                        != "orientation-independent-spherical-triangle-upper-bound/v2"
                    || json.Value<string>("GeometryProvenance") != "derived-astap-wcs"
                    || json.Value<bool?>("UsesOrientationConvention") != false
                    || json.Value<bool?>("GrantsMotionAuthority") != false
                    || json.Value<bool?>("GrantsAbsolutePolarAccuracyClaim") != false
                    || string.IsNullOrWhiteSpace(mainSource)
                    || string.IsNullOrWhiteSpace(guideSource)
                    || !IsSha256(mainSha256)
                    || !IsSha256(guideSha256)
                    || !(json.Value<double?>("GuideToFarthestMainCornerUpperBoundPixels") > 0)) {
                issues.Add("OAG geometry receipt is unsupported or grants forbidden authority");
                return;
            }

            var receiptDirectory = Path.GetDirectoryName(Path.GetFullPath(path))
                ?? throw new IOException("OAG geometry receipt has no parent directory");
            var mainPath = ResolveGeometrySource(receiptDirectory, mainSource);
            var guidePath = ResolveGeometrySource(receiptDirectory, guideSource);
            var mainSourceBytes = ReadGeometrySource(mainPath, mainSha256,
                "OAG main solution source");
            var guideSourceBytes = ReadGeometrySource(guidePath, guideSha256,
                "OAG guide solution source");
            if (string.Equals(mainPath, guidePath, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(mainSourceBytes.Digest, guideSourceBytes.Digest,
                        StringComparison.OrdinalIgnoreCase)) {
                issues.Add("OAG main and guide solution sources must be distinct artifacts");
                return;
            }

            var main = TppaAstapWcsGeometryParser.Parse(mainSourceBytes.Bytes);
            var guide = TppaAstapWcsGeometryParser.Parse(guideSourceBytes.Bytes);
            var observationDelta = Math.Abs(
                (main.ObservationUtc - guide.ObservationUtc).TotalSeconds);
            if (observationDelta > MaximumGeometryObservationDeltaSeconds) {
                issues.Add("OAG main and guide WCS observations are not time-coherent");
                return;
            }
            var lockX = json.Value<double?>("GuideLockOffsetXFromCenterPixels");
            var lockY = json.Value<double?>("GuideLockOffsetYFromCenterPixels");
            var computed = TppaOagGeometryBoundCalculator.Compute(
                main, guide, lockX, lockY);
            RequireGeometryValue(json, "MainCenterRightAscensionDegrees",
                main.RightAscensionDegrees, issues);
            RequireGeometryValue(json, "MainCenterDeclinationDegrees",
                main.DeclinationDegrees, issues);
            RequireGeometryUtc(json, "MainObservationUtc", main.ObservationUtc, issues);
            RequireGeometryInteger(json, "MainWidthPixels", main.WidthPixels, issues);
            RequireGeometryInteger(json, "MainHeightPixels", main.HeightPixels, issues);
            RequireGeometryValue(json, "MainPixelScaleXArcseconds",
                main.PixelScaleXArcseconds, issues);
            RequireGeometryValue(json, "MainPixelScaleYArcseconds",
                main.PixelScaleYArcseconds, issues);
            RequireGeometryString(json, "MainScaleModel", main.ScaleModel, issues);
            RequireGeometryValue(json, "GuideCenterRightAscensionDegrees",
                guide.RightAscensionDegrees, issues);
            RequireGeometryValue(json, "GuideCenterDeclinationDegrees",
                guide.DeclinationDegrees, issues);
            RequireGeometryUtc(json, "GuideObservationUtc", guide.ObservationUtc, issues);
            RequireGeometryInteger(json, "GuideWidthPixels", guide.WidthPixels, issues);
            RequireGeometryInteger(json, "GuideHeightPixels", guide.HeightPixels, issues);
            RequireGeometryValue(json, "GuidePixelScaleXArcseconds",
                guide.PixelScaleXArcseconds, issues);
            RequireGeometryValue(json, "GuidePixelScaleYArcseconds",
                guide.PixelScaleYArcseconds, issues);
            RequireGeometryString(json, "GuideScaleModel", guide.ScaleModel, issues);
            RequireGeometryValue(json, "ObservationDeltaSeconds",
                observationDelta, issues);
            RequireGeometryValue(json, "MaximumObservationDeltaSeconds",
                MaximumGeometryObservationDeltaSeconds, issues);
            RequireGeometryValue(json, "MainPixelScaleUsedArcseconds",
                computed.MainPixelScaleUsedArcseconds, issues);
            RequireGeometryValue(json, "GuidePixelScaleUsedArcseconds",
                computed.GuidePixelScaleUsedArcseconds, issues);
            RequireGeometryValue(json, "CenterSeparationArcseconds",
                computed.CenterSeparationArcseconds, issues);
            RequireGeometryValue(json, "CenterSeparationMainPixels",
                computed.CenterSeparationMainPixels, issues);
            RequireGeometryValue(json, "MainHalfDiagonalPixels",
                computed.MainHalfDiagonalPixels, issues);
            RequireGeometryString(json, "GuideRadialEvidenceKind",
                computed.GuideRadialEvidenceKind, issues);
            RequireGeometryValue(json, "GuideRadialPixels",
                computed.GuideRadialPixels, issues);
            RequireGeometryValue(json, "GuideRadialArcseconds",
                computed.GuideRadialArcseconds, issues);
            RequireGeometryValue(json, "GuideRadialMainPixels",
                computed.GuideRadialMainPixels, issues);
            RequireGeometryValue(json,
                "GuideToFarthestMainCornerUpperBoundPixels",
                computed.GuideToFarthestMainCornerUpperBoundPixels, issues);
        } catch (Exception exception) {
            issues.Add($"OAG geometry receipt cannot be validated: {exception.Message}");
        }
    }

    private static string ResolveGeometrySource(string receiptDirectory,
            string source) {
        if (Path.IsPathRooted(source) || source.IndexOf(':') >= 0
                || source.IndexOf('\0') >= 0) {
            throw new InvalidDataException(
                "OAG geometry sources must use bundle-relative paths.");
        }
        var segments = source.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment =>
                segment != "." && segment != ".."
                && (segment.EndsWith(' ') || segment.EndsWith('.')))) {
            throw new InvalidDataException("OAG geometry source path is ambiguous.");
        }
        var resolved = Path.GetFullPath(Path.Combine(receiptDirectory, source));
        var relative = Path.GetRelativePath(receiptDirectory, resolved);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal)
                || relative.StartsWith(".." + Path.AltDirectorySeparatorChar,
                    StringComparison.Ordinal)) {
            throw new InvalidDataException(
                "OAG geometry source escapes the receipt directory.");
        }
        if (!File.Exists(resolved)) {
            throw new InvalidDataException("OAG geometry source is missing.");
        }
        EnsureNoReparsePoints(receiptDirectory, relative);
        return resolved;
    }

    private static void EnsureNoReparsePoints(string receiptDirectory, string relative) {
        var current = Path.GetFullPath(receiptDirectory);
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) {
            throw new InvalidDataException("OAG geometry bundle uses a reparse point.");
        }
        foreach (var segment in relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries)) {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) {
                throw new InvalidDataException("OAG geometry source uses a reparse point.");
            }
        }
    }

    private static VerifiedGeometrySource ReadGeometrySource(string path,
            string expected, string label) {
        if (!File.Exists(path) || !IsSha256(expected)) {
            throw new InvalidDataException(
                $"{label} path or declared SHA-256 is invalid");
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read);
        if (stream.Length <= 0 || stream.Length > MaximumGeometrySourceBytes) {
            throw new InvalidDataException($"{label} size is invalid");
        }
        using var buffer = new MemoryStream((int)stream.Length);
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var digest = Sha256(bytes);
        if (!digest.Equals(expected, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidDataException($"{label} SHA-256 does not match the supplied bytes");
        }
        return new(bytes, digest);
    }

    private static void RequireExactGeometryProperties(JObject json) {
        var actual = json.Properties().Select(property => property.Name).ToHashSet(
            StringComparer.Ordinal);
        if (!actual.SetEquals(GeometryReceiptProperties)) {
            throw new InvalidDataException(
                "OAG geometry receipt properties do not exactly match schema 2.");
        }
    }

    private static void RequireGeometryValue(JObject json, string name,
            double expected, ICollection<string> issues) {
        var actual = json.Value<double?>(name);
        var tolerance = Math.Max(1e-9, Math.Abs(expected) * 1e-10);
        if (!actual.HasValue || !double.IsFinite(actual.Value)
                || Math.Abs(actual.Value - expected) > tolerance) {
            issues.Add($"OAG derived geometry field {name} does not reproduce its WCS sources");
        }
    }

    private static void RequireGeometryUtc(JObject json, string name,
            DateTimeOffset expected, ICollection<string> issues) {
        var value = json.Value<string>(name);
        if (string.IsNullOrWhiteSpace(value)
                || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var actual)
                || actual.ToUniversalTime() != expected.ToUniversalTime()) {
            issues.Add($"OAG derived geometry field {name} does not reproduce its WCS sources");
        }
    }

    private static void RequireGeometryInteger(JObject json, string name,
            int expected, ICollection<string> issues) {
        if (json.Value<int?>(name) != expected) {
            issues.Add($"OAG derived geometry field {name} does not reproduce its WCS sources");
        }
    }

    private static void RequireGeometryString(JObject json, string name,
            string expected, ICollection<string> issues) {
        if (!string.Equals(json.Value<string>(name), expected,
                StringComparison.Ordinal)) {
            issues.Add($"OAG derived geometry field {name} does not reproduce its WCS sources");
        }
    }

    private static void ValidateState(string path, string phd2SummaryPath,
            string opticalTrainId, DateTime bracketStart, DateTime bracketEnd,
            ICollection<string> issues) {
        try {
            var state = JsonConvert.DeserializeObject<TppaActualExposureStateReceipt>(
                File.ReadAllText(path), new JsonSerializerSettings {
                    MissingMemberHandling = MissingMemberHandling.Error,
                    DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind
                });
            if (state == null || state.SchemaVersion != CurrentStateSchemaVersion
                    || state.EvidenceMode != "ReadOnlySampledStateContinuityWitness"
                    || state.OpticalTrainId != opticalTrainId
                    || !IsUtc(state.CaptureStartUtc)
                    || !IsUtc(state.CaptureCompletedUtc)
                    || state.CaptureStartUtc >= state.CaptureCompletedUtc
                    || state.CaptureStartUtc > bracketStart
                    || state.CaptureCompletedUtc < bracketEnd
                    || state.GrantsMountMotionAuthority || state.GrantsUpasAuthority
                    || state.GrantsAbsoluteAccuracyClaim
                    || state.StateContinuityQualified != true
                    || state.ReadOnlyNinaEndpoints == null
                    || state.StateChangingNinaEndpoints == null
                    || state.StateChangingNinaEndpoints.Count != 0
                    || state.ReadOnlyNinaEndpoints.Count
                        != RequiredReadOnlyNinaEndpoints.Length
                    || RequiredReadOnlyNinaEndpoints.Any(endpoint =>
                        !state.ReadOnlyNinaEndpoints.Contains(endpoint,
                            StringComparer.Ordinal))
                    || !ValidStateDictionary(state.BaselineState)
                    || state.Samples == null
                    || state.Samples.Count < MinimumStateSamples) {
                issues.Add("sampled state-continuity receipt identity, authority, or bracket coverage is invalid");
                return;
            }

            var samples = state.Samples.ToArray();
            var gaps = new List<double>(samples.Length - 1);
            for (var index = 0; index < samples.Length; index++) {
                var sample = samples[index];
                if (sample == null || !IsUtc(sample.TimestampUtc)
                        || !double.IsFinite(sample.MonotonicSeconds)
                        || sample.MonotonicSeconds < 0
                        || !ValidStateDictionary(sample.State)
                        || !StateMatchesBaseline(state.BaselineState, sample.State)) {
                    issues.Add("sampled state-continuity evidence contains an invalid or changed state sample");
                    return;
                }
                if (index == 0) { continue; }
                var utcGap = (sample.TimestampUtc - samples[index - 1].TimestampUtc)
                    .TotalSeconds;
                var monotonicGap = sample.MonotonicSeconds
                    - samples[index - 1].MonotonicSeconds;
                if (utcGap <= 0 || monotonicGap <= 0
                        || Math.Abs(utcGap - monotonicGap)
                            > Math.Max(1.0, 0.1 * monotonicGap)) {
                    issues.Add("sampled state-continuity timestamps are non-monotonic or clock-inconsistent");
                    return;
                }
                gaps.Add(monotonicGap);
            }

            var medianCadence = Median(gaps);
            var maximumObservedGap = gaps.Max();
            var maximumAllowedGap = Math.Min(MaximumStateGapSeconds,
                3.0 * medianCadence);
            var startBoundaryGap = (samples[0].TimestampUtc
                - state.CaptureStartUtc).TotalSeconds;
            var endBoundaryGap = (state.CaptureCompletedUtc
                - samples[^1].TimestampUtc).TotalSeconds;
            if (!double.IsFinite(medianCadence) || medianCadence <= 0
                    || maximumObservedGap > maximumAllowedGap
                    || startBoundaryGap < 0 || startBoundaryGap > maximumAllowedGap
                    || endBoundaryGap < 0 || endBoundaryGap > maximumAllowedGap
                    || samples[0].TimestampUtc > bracketStart
                    || samples[^1].TimestampUtc < bracketEnd
                    || state.SampleCount != samples.Length
                    || !NearlyEqual(state.ObservedMedianSampleCadenceSeconds,
                        medianCadence)
                    || !NearlyEqual(state.MaximumObservedSampleGapSeconds,
                        maximumObservedGap)
                    || !NearlyEqual(state.MaximumAllowedSampleGapSeconds,
                        maximumAllowedGap)) {
                issues.Add("sampled state-continuity cadence, coverage, or summary cross-check failed");
                return;
            }

            var phd2 = ParseJsonWithoutDateCoercion(phd2SummaryPath);
            if (phd2.Value<bool?>("PHD2ConfigurationContinuityQualified") != true
                    || string.IsNullOrWhiteSpace(phd2.Value<string>("Phd2Profile"))
                    || !(phd2.Value<int?>("Phd2ExposureMilliseconds") > 0)
                    || !IsSha256(phd2.Value<string>("Phd2AlgorithmStateDigest"))
                    || state.BaselineState["phd2Profile"]
                        != phd2.Value<string>("Phd2Profile")
                    || state.BaselineState["phd2ExposureMs"]
                        != phd2.Value<int>("Phd2ExposureMilliseconds")
                            .ToString(CultureInfo.InvariantCulture)
                    || state.BaselineState["phd2AlgorithmStateDigest"]
                        != phd2.Value<string>("Phd2AlgorithmStateDigest")) {
                issues.Add("sampled state-continuity PHD2 configuration does not match its independent summary");
            }
        } catch (Exception exception) {
            issues.Add($"state-continuity receipt cannot be validated: {exception.Message}");
        }
    }

    private static bool ValidStateDictionary(
            IReadOnlyDictionary<string, string> state) =>
        state != null
        && RequiredStateKeys.All(key => state.TryGetValue(key, out var value)
            && !string.IsNullOrWhiteSpace(value))
        && RequiredInvariantState.All(required =>
            string.Equals(state[required.Key], required.Value,
                StringComparison.Ordinal))
        && IsSha256(state["phd2AlgorithmStateDigest"]);

    private static bool StateMatchesBaseline(
            IReadOnlyDictionary<string, string> baseline,
            IReadOnlyDictionary<string, string> sample) {
        foreach (var key in RequiredStateKeys) {
            if (key == "targetRaDegrees") {
                if (!WithinAngularTolerance(baseline[key], sample[key],
                        MaximumTargetCoordinateDeltaDegrees)) { return false; }
            } else if (key == "targetDecDegrees") {
                if (!WithinNumericTolerance(baseline[key], sample[key],
                        MaximumTargetCoordinateDeltaDegrees)) { return false; }
            } else if (key == "rotatorAngleDegrees") {
                if (!WithinAngularTolerance(baseline[key], sample[key],
                        MaximumRotatorDeltaDegrees)) { return false; }
            } else if (key == "coolerSetPointC") {
                if (!WithinNumericTolerance(baseline[key], sample[key],
                        MaximumCoolerSetPointDeltaC)) { return false; }
            } else if (!string.Equals(baseline[key], sample[key],
                    StringComparison.Ordinal)) {
                return false;
            }
        }
        return true;
    }

    private static bool WithinNumericTolerance(string left, string right,
            double tolerance) =>
        double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture,
            out var leftValue)
        && double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture,
            out var rightValue)
        && double.IsFinite(leftValue) && double.IsFinite(rightValue)
        && Math.Abs(leftValue - rightValue) <= tolerance;

    private static bool WithinAngularTolerance(string left, string right,
            double tolerance) {
        if (!double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture,
                out var leftValue)
                || !double.TryParse(right, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var rightValue)
                || !double.IsFinite(leftValue) || !double.IsFinite(rightValue)) {
            return false;
        }
        var delta = Math.Abs(leftValue - rightValue) % 360.0;
        return Math.Min(delta, 360.0 - delta) <= tolerance;
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
        return JObject.Load(reader, new JsonLoadSettings {
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
            CommentHandling = CommentHandling.Ignore,
            LineInfoHandling = LineInfoHandling.Ignore
        });
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
    private sealed record VerifiedGeometrySource(byte[] Bytes, string Digest);
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
