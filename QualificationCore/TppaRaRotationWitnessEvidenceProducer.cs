using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaRaRotationWitnessPointReceipt(
        int SchemaVersion,
        Guid RunId,
        string PositionId,
        int SequenceIndex,
        string MountCommandId,
        DateTime MountCommandIssuedUtc,
        DateTime MountCommandCompletedUtc,
        double CommandedRightAscensionDegrees,
        double CommandedDeclinationDegrees,
        bool TrackingEnabled,
        bool Slewing,
        string Phd2AppState,
        bool GuideOutputEnabled,
        DateTime CaptureStartedUtc,
        double ExposureSeconds,
        DateTime ObservationUtc,
        DateTime FitsDateObsUtc,
        string FitsDateObsConvention,
        double FitsTimestampUncertaintyMilliseconds,
        string SourceImageSha256,
        string SolverOutputSha256,
        string SolverIdentity,
        string SolverBinarySha256,
        string SolverHintPolicy,
        string SourceCoordinateFrame,
        double SiteLatitudeDegrees,
        double SiteLongitudeDegrees,
        double SiteElevationMeters,
        double AstapFieldOfViewDegrees,
        double MountAzimuthDegrees,
        double MountAltitudeDegrees,
        double SolvedRightAscensionDegrees,
        double SolvedDeclinationDegrees,
        double PositionAngleDegrees,
        double VectorX,
        double VectorY,
        double VectorZ,
        string SideOfPier) {
        public const int CurrentSchemaVersion = 3;
        public bool GrantsMotionAuthority => false;
        public bool GrantsCompletionAuthority => false;
        public string ToJson() => TppaRaRotationWitnessEvidenceProducer.SerializeRecord(this);
    }

    internal sealed record TppaRaRotationWitnessProductionMetadata(
        string BindsRunId,
        string SessionId,
        string ProducerId,
        string PipelineDigest,
        string HardwareConfigurationId,
        string MechanicalStateDigest,
        string SiteIdentity,
        double SiteLatitudeDegrees,
        double SiteLongitudeDegrees,
        double SiteElevationMeters,
        string ClockDomainId,
        double ClockUncertaintyMilliseconds,
        string CoordinateFrame,
        string MountAxisVectorFrame,
        string PoleTarget,
        int CorrectionSequenceNumber,
        string InstrumentId,
        string CalibrationDigest,
        bool CalibrationCurrent,
        string CalibrationSourceProducerId,
        string CalibrationSourceDigest,
        bool CalibrationDerivedFromTppa,
        string TrajectoryPreflightDigest,
        bool TrajectoryPreflightQualified,
        double TrajectoryMinimumAltitudeDegrees,
        double TrajectoryMaximumSampleStepDegrees,
        double TrajectoryTotalArcDegrees,
        double TrajectoryDesignConditionProxy,
        string InitialPhd2AppState,
        string FinalPhd2AppState,
        bool GuideOutputRestored,
        TppaWitnessUncertaintyEvidence Uncertainty);

    internal sealed record TppaRaRotationWitnessProductionResult(
        bool Produced,
        string EvidenceJson,
        string OutputPath,
        IReadOnlyList<string> QualificationIssues,
        IReadOnlyList<string> ProductionIssues) {
        public bool GrantsMotionAuthority => false;
        public bool GrantsCompletionAuthority => false;
        public bool GrantsAbsoluteAccuracyClaim => false;
    }

    /// <summary>
    /// Produces immutable, report-only evidence from four disjoint plate-solved
    /// RA-rotation observations A/B/C/A. The independent binder recomputes the
    /// axis and all qualification gates from the persisted raw solves.
    /// </summary>
    internal static class TppaRaRotationWitnessEvidenceProducer {
        private static readonly JsonSerializer EvidenceSerializer = JsonSerializer.Create(
            new JsonSerializerSettings {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Culture = CultureInfo.InvariantCulture,
                DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
                NullValueHandling = NullValueHandling.Include
            });

        public static TppaRaRotationWitnessProductionResult Produce(
                TppaRaRotationWitnessProductionMetadata metadata,
                IReadOnlyList<TppaRaRotationWitnessPointReceipt> sourcePoints,
                string outputDirectory) {
            var productionIssues = Validate(metadata, sourcePoints, outputDirectory);
            if (productionIssues.Count > 0) {
                return new(false, null, null, Array.Empty<string>(), productionIssues);
            }

            try {
                var solves = sourcePoints.Select(ToQualificationSolve).ToArray();
                var axis = FitAxis(solves[0].UnitVector, solves[1].UnitVector,
                    solves[2].UnitVector, metadata.SiteLatitudeDegrees);
                var evidence = new TppaQualificationWitnessEvidence(
                    TppaAbsoluteEvidenceBinder.CurrentEvidenceSchemaVersion,
                    EvidenceDigest: string.Empty,
                    metadata.BindsRunId,
                    metadata.SessionId,
                    metadata.ProducerId,
                    TppaAbsoluteEvidenceBinder.WitnessProducerKind,
                    metadata.PipelineDigest,
                    metadata.HardwareConfigurationId,
                    metadata.MechanicalStateDigest,
                    metadata.SiteIdentity,
                    metadata.SiteLatitudeDegrees,
                    metadata.SiteLongitudeDegrees,
                    metadata.SiteElevationMeters,
                    metadata.ClockDomainId,
                    metadata.ClockUncertaintyMilliseconds,
                    metadata.CoordinateFrame,
                    metadata.MountAxisVectorFrame,
                    metadata.PoleTarget,
                    TppaAbsoluteEvidenceBinder.AbsoluteTruePoleWitnessBasis,
                    TppaAbsoluteEvidenceBinder.RaRotationCircleWitnessMethod,
                    solves[^1].ObservationUtc,
                    metadata.CorrectionSequenceNumber,
                    metadata.InstrumentId,
                    solves.Select(value => value.ContentSha256).ToArray(),
                    solves,
                    metadata.TrajectoryPreflightDigest,
                    metadata.TrajectoryPreflightQualified,
                    metadata.TrajectoryMinimumAltitudeDegrees,
                    metadata.TrajectoryMaximumSampleStepDegrees,
                    metadata.TrajectoryTotalArcDegrees,
                    metadata.TrajectoryDesignConditionProxy,
                    metadata.InitialPhd2AppState,
                    metadata.FinalPhd2AppState,
                    metadata.GuideOutputRestored,
                    sourcePoints.Select(ToAcquisition).ToArray(),
                    metadata.CalibrationDigest,
                    metadata.CalibrationCurrent,
                    metadata.CalibrationSourceProducerId,
                    metadata.CalibrationSourceDigest,
                    metadata.CalibrationDerivedFromTppa,
                    metadata.Uncertainty,
                    axis);

                var derivationIssues = new List<string>();
                TppaQualificationEvidenceDeriver.DeriveWitness(
                    evidence, derivationIssues);
                if (derivationIssues.Count > 0) {
                    return new(false, null, null, derivationIssues,
                        Array.Empty<string>());
                }
                var uncertainty = TppaWitnessUncertainty.Evaluate(
                    metadata.Uncertainty,
                    maximumUpperBound95ArcSeconds: 30.0,
                    minimumCalibrationSamples: 3,
                    minimumClosureSamples: 2);
                var qualificationIssues = uncertainty.Issues.ToArray();

                var json = SerializeEvidence(evidence);
                Directory.CreateDirectory(outputDirectory);
                var outputPath = Path.Combine(outputDirectory,
                    SanitizeFileName(metadata.BindsRunId) + "-witness-evidence.json");
                WriteCreateNew(outputPath, Encoding.UTF8.GetBytes(json));
                return new(true, json, outputPath, qualificationIssues,
                    Array.Empty<string>());
            } catch (Exception exception) when (
                    exception is ArgumentException
                    || exception is ArithmeticException
                    || exception is IOException
                    || exception is InvalidOperationException
                    || exception is UnauthorizedAccessException) {
                return new(false, null, null, Array.Empty<string>(), new[] {
                    $"RA-rotation witness evidence production failed closed ({exception.GetType().Name}): {exception.Message}"
                });
            }
        }

        private static List<string> Validate(
                TppaRaRotationWitnessProductionMetadata metadata,
                IReadOnlyList<TppaRaRotationWitnessPointReceipt> sourcePoints,
                string outputDirectory) {
            var issues = new List<string>();
            if (metadata == null) {
                issues.Add("witness production metadata is missing");
                return issues;
            }
            if (string.IsNullOrWhiteSpace(metadata.BindsRunId)
                    || string.IsNullOrWhiteSpace(metadata.SessionId)
                    || string.IsNullOrWhiteSpace(metadata.ProducerId)
                    || string.IsNullOrWhiteSpace(metadata.InstrumentId)) {
                issues.Add("witness run, session, producer, or instrument identity is missing");
            }
            foreach (var (value, label) in new[] {
                    (metadata.PipelineDigest, "pipeline"),
                    (metadata.MechanicalStateDigest, "mechanical-state"),
                    (metadata.CalibrationDigest, "calibration"),
                    (metadata.CalibrationSourceDigest, "calibration-source") }) {
                if (!IsSha256(value)) { issues.Add($"witness {label} digest is invalid"); }
            }
            if (metadata.CorrectionSequenceNumber < 0) {
                issues.Add("witness correction sequence number cannot be negative");
            }
            if (!metadata.CalibrationCurrent) {
                issues.Add("witness calibration is stale");
            }
            if (!IsSha256(metadata.TrajectoryPreflightDigest)
                    || !metadata.TrajectoryPreflightQualified
                    || !double.IsFinite(metadata.TrajectoryMinimumAltitudeDegrees)
                    || metadata.TrajectoryMinimumAltitudeDegrees < 40
                    || !double.IsFinite(metadata.TrajectoryMaximumSampleStepDegrees)
                    || metadata.TrajectoryMaximumSampleStepDegrees <= 0
                    || metadata.TrajectoryMaximumSampleStepDegrees > 1
                    || !double.IsFinite(metadata.TrajectoryTotalArcDegrees)
                    || metadata.TrajectoryTotalArcDegrees < 45
                    || !double.IsFinite(metadata.TrajectoryDesignConditionProxy)
                    || metadata.TrajectoryDesignConditionProxy <= 0
                    || metadata.TrajectoryDesignConditionProxy > 4) {
                issues.Add("witness full-trajectory preflight is missing or unqualified");
            }
            if (string.IsNullOrWhiteSpace(metadata.InitialPhd2AppState)
                    || string.IsNullOrWhiteSpace(metadata.FinalPhd2AppState)
                    || !metadata.GuideOutputRestored) {
                issues.Add("witness PHD2 initial/final state or guide-output restoration is missing");
            }
            if (metadata.CalibrationDerivedFromTppa
                    || metadata.CalibrationSourceProducerId == metadata.ProducerId) {
                issues.Add("witness calibration is not independent of its measurement producer");
            }
            if (metadata.Uncertainty == null
                    || metadata.CalibrationDigest
                        != metadata.Uncertainty.EvidenceDigest
                    || metadata.CalibrationSourceDigest
                        != metadata.Uncertainty.InputDigest
                    || TppaWitnessUncertainty.ComputeEvidenceDigest(
                        metadata.Uncertainty) != metadata.Uncertainty.EvidenceDigest) {
                issues.Add("witness uncertainty is not bound to the declared calibration payload");
            } else {
                if (metadata.Uncertainty.CalibrationSampleCount < 3
                        || metadata.Uncertainty.ClosureSampleCount < 2) {
                    issues.Add("witness calibration or closure sample floor is unmet");
                }
                var terms = new[] {
                    metadata.Uncertainty.MeasurementStandardUncertaintyArcSeconds,
                    metadata.Uncertainty.CalibrationResidualArcSeconds,
                    metadata.Uncertainty.OrientationModelResidualArcSeconds,
                    metadata.Uncertainty.ClosureResidualArcSeconds,
                    metadata.Uncertainty.FrameSystematicBoundArcSeconds,
                    metadata.Uncertainty.DistortionSystematicBoundArcSeconds,
                    metadata.Uncertainty.MechanicalSystematicBoundArcSeconds
                };
                if (terms.Any(value => !double.IsFinite(value) || value <= 0)) {
                    issues.Add("witness uncertainty terms must be finite and positive");
                }
            }
            if (!double.IsFinite(metadata.ClockUncertaintyMilliseconds)
                    || metadata.ClockUncertaintyMilliseconds < 0
                    || metadata.ClockUncertaintyMilliseconds
                        > TppaAbsoluteEvidenceBinder.MaximumClockUncertaintyMilliseconds) {
                issues.Add("witness clock uncertainty is missing or exceeds one second");
            }
            if (metadata.CoordinateFrame
                    != TppaFastQualificationConventions.IcrsObservationEpoch
                    || metadata.MountAxisVectorFrame
                        != TppaAbsoluteEvidenceBinder.TopocentricHorizonNorthWestUp
                    || metadata.PoleTarget
                        != TppaFastQualificationConventions.TruePoleTarget) {
                issues.Add("witness coordinate frame or pole target is not qualified");
            }
            if (!double.IsFinite(metadata.SiteLatitudeDegrees)
                    || metadata.SiteLatitudeDegrees < -90
                    || metadata.SiteLatitudeDegrees > 90
                    || !double.IsFinite(metadata.SiteLongitudeDegrees)
                    || metadata.SiteLongitudeDegrees < -180
                    || metadata.SiteLongitudeDegrees > 180
                    || !double.IsFinite(metadata.SiteElevationMeters)
                    || metadata.SiteElevationMeters < -500
                    || metadata.SiteElevationMeters > 10000) {
                issues.Add("witness raw site coordinates or elevation are invalid");
            }
            if (string.IsNullOrWhiteSpace(outputDirectory)) {
                issues.Add("witness evidence output directory is missing");
            }
            var points = sourcePoints?.Where(value => value != null).ToArray()
                ?? Array.Empty<TppaRaRotationWitnessPointReceipt>();
            if (points.Length != 4 || points.Length != (sourcePoints?.Count ?? 0)) {
                issues.Add("exactly four non-null A/B/C/A point receipts are required");
            }
            if (points.Any(value => value.SchemaVersion
                    != TppaRaRotationWitnessPointReceipt.CurrentSchemaVersion)) {
                issues.Add("witness point receipt schema is unsupported");
            }
            if (points.Any(value => value.SideOfPier == "pierUnknown")) {
                issues.Add("witness point receipt has unknown pier side");
            }
            if (points.Length == 4) {
                if (points.Select(value => value.RunId).Distinct().Count() != 1
                        || points[0].RunId == Guid.Empty) {
                    issues.Add("witness point receipts do not share one non-empty run identity");
                }
                if (!points.Select(value => value.SequenceIndex)
                        .SequenceEqual(new[] { 0, 1, 2, 3 })) {
                    issues.Add("witness point receipt sequence is not A/B/C/A order");
                }
                if (!points.Select(value => value.PositionId)
                        .SequenceEqual(new[] { "A", "B", "C", "A" })) {
                    issues.Add("witness point receipt identities are not A/B/C/A");
                }
                if (points.Select(value => value.SideOfPier).Distinct().Count() != 1) {
                    issues.Add("witness pier side changed within the RA-rotation arc");
                }
                if (points.Select(value => value.MountCommandId)
                        .Any(string.IsNullOrWhiteSpace)
                        || points.Select(value => value.MountCommandId)
                            .Distinct(StringComparer.Ordinal).Count() != 4) {
                    issues.Add("witness mount-command identities are missing or reused");
                }
                if (points.Any(value => !ValidateAcquisition(value, metadata))) {
                    issues.Add("witness acquisition provenance is incomplete, moving, guided, or mistimed");
                }
                var declinationSpan = points
                    .Max(value => value.CommandedDeclinationDegrees)
                    - points.Min(value => value.CommandedDeclinationDegrees);
                var firstLeg = SignedAngularDelta(
                    points[0].CommandedRightAscensionDegrees,
                    points[1].CommandedRightAscensionDegrees);
                var secondLeg = SignedAngularDelta(
                    points[1].CommandedRightAscensionDegrees,
                    points[2].CommandedRightAscensionDegrees);
                var returnClosure = Math.Abs(SignedAngularDelta(
                    points[0].CommandedRightAscensionDegrees,
                    points[3].CommandedRightAscensionDegrees));
                var totalArc = Math.Abs(firstLeg + secondLeg);
                if (declinationSpan > 0.05
                        || Math.Sign(firstLeg) == 0
                        || Math.Sign(firstLeg) != Math.Sign(secondLeg)
                        || Math.Abs(firstLeg) < 15
                        || Math.Abs(secondLeg) < 15
                        || totalArc < 45
                        || Math.Abs(totalArc - metadata.TrajectoryTotalArcDegrees) > 0.5
                        || returnClosure
                            > TppaAbsoluteEvidenceBinder.MaximumClosureSeparationDegrees) {
                    issues.Add("witness commanded geometry is not fixed-declination monotonic A-B-C with qualified A return");
                }
            }
            return issues;
        }

        private static TppaQualificationSolveEvidence ToQualificationSolve(
                TppaRaRotationWitnessPointReceipt point) =>
            new(
                point.ObservationUtc,
                point.SourceImageSha256,
                point.SolvedRightAscensionDegrees,
                point.SolvedDeclinationDegrees,
                point.SideOfPier,
                new(point.VectorX, point.VectorY, point.VectorZ));

        private static TppaRaRotationWitnessAcquisitionEvidence ToAcquisition(
                TppaRaRotationWitnessPointReceipt point) =>
            new(
                point.SchemaVersion,
                point.PositionId,
                point.SequenceIndex,
                point.MountCommandId,
                point.MountCommandIssuedUtc,
                point.MountCommandCompletedUtc,
                point.CommandedRightAscensionDegrees,
                point.CommandedDeclinationDegrees,
                point.TrackingEnabled,
                point.Slewing,
                point.Phd2AppState,
                point.GuideOutputEnabled,
                point.CaptureStartedUtc,
                point.ExposureSeconds,
                point.ObservationUtc,
                point.FitsDateObsUtc,
                point.FitsDateObsConvention,
                point.FitsTimestampUncertaintyMilliseconds,
                point.SourceImageSha256,
                point.SolverOutputSha256,
                point.SolverIdentity,
                point.SolverBinarySha256,
                point.SolverHintPolicy,
                point.SourceCoordinateFrame,
                point.SiteLatitudeDegrees,
                point.SiteLongitudeDegrees,
                point.SiteElevationMeters,
                point.AstapFieldOfViewDegrees,
                point.MountAzimuthDegrees,
                point.MountAltitudeDegrees,
                point.SolvedRightAscensionDegrees,
                point.SolvedDeclinationDegrees,
                point.PositionAngleDegrees,
                point.SideOfPier);

        internal static string SerializeRecord(object value) {
            if (value == null) { throw new ArgumentNullException(nameof(value)); }
            return JObject.FromObject(value, EvidenceSerializer).ToString(Formatting.None);
        }

        private static string SerializeEvidence(object evidence) {
            if (evidence == null) { throw new ArgumentNullException(nameof(evidence)); }
            var token = JObject.FromObject(evidence, EvidenceSerializer);
            token["evidenceDigest"] = new string('0', 64);
            token["evidenceDigest"] =
                TppaAbsoluteEvidenceBinder.ComputeCanonicalEvidenceDigest(
                    token.ToString(Formatting.None));
            return token.ToString(Formatting.None);
        }

        private static bool ValidateAcquisition(
                TppaRaRotationWitnessPointReceipt point,
                TppaRaRotationWitnessProductionMetadata metadata) {
            var values = new[] {
                point.CommandedRightAscensionDegrees,
                point.CommandedDeclinationDegrees,
                point.ExposureSeconds,
                point.FitsTimestampUncertaintyMilliseconds,
                point.MountAzimuthDegrees,
                point.MountAltitudeDegrees,
                point.SolvedRightAscensionDegrees,
                point.SolvedDeclinationDegrees,
                point.PositionAngleDegrees,
                point.SiteLatitudeDegrees,
                point.SiteLongitudeDegrees,
                point.SiteElevationMeters,
                point.AstapFieldOfViewDegrees,
                point.VectorX,
                point.VectorY,
                point.VectorZ
            };
            var midpoint = point.CaptureStartedUtc
                + TimeSpan.FromSeconds(point.ExposureSeconds / 2);
            var fitsMidpoint = point.FitsDateObsConvention
                == TppaAbsoluteEvidenceBinder.FitsDateObsExposureStart
                ? point.FitsDateObsUtc
                    + TimeSpan.FromSeconds(point.ExposureSeconds / 2)
                : point.FitsDateObsUtc;
            return point.MountCommandIssuedUtc.Kind == DateTimeKind.Utc
                && point.MountCommandCompletedUtc.Kind == DateTimeKind.Utc
                && point.CaptureStartedUtc.Kind == DateTimeKind.Utc
                && point.ObservationUtc.Kind == DateTimeKind.Utc
                && point.FitsDateObsUtc.Kind == DateTimeKind.Utc
                && (point.FitsDateObsConvention
                        == TppaAbsoluteEvidenceBinder.FitsDateObsExposureStart
                    || point.FitsDateObsConvention
                        == TppaAbsoluteEvidenceBinder.FitsDateObsExposureMidpoint)
                && point.MountCommandCompletedUtc >= point.MountCommandIssuedUtc
                && point.CaptureStartedUtc >= point.MountCommandCompletedUtc
                && point.TrackingEnabled
                && !point.Slewing
                && !point.GuideOutputEnabled
                && !string.IsNullOrWhiteSpace(point.Phd2AppState)
                && values.All(double.IsFinite)
                && point.ExposureSeconds > 0
                && point.FitsTimestampUncertaintyMilliseconds >= 0
                && point.FitsTimestampUncertaintyMilliseconds <= 1000
                && Math.Abs((point.ObservationUtc - midpoint).TotalMilliseconds)
                    <= metadata.ClockUncertaintyMilliseconds
                && Math.Abs((fitsMidpoint - point.ObservationUtc)
                    .TotalMilliseconds)
                    <= point.FitsTimestampUncertaintyMilliseconds
                && IsSha256(point.SourceImageSha256)
                && IsSha256(point.SolverOutputSha256)
                && IsSha256(point.SolverBinarySha256)
                && !string.IsNullOrWhiteSpace(point.SolverIdentity)
                && point.SolverHintPolicy
                    == TppaAbsoluteEvidenceBinder.BlindNoMountHintSolvePolicy
                && point.SourceCoordinateFrame
                    == TppaFastQualificationConventions.IcrsObservationEpoch
                && Math.Abs(point.SiteLatitudeDegrees
                    - metadata.SiteLatitudeDegrees) <= 1e-9
                && Math.Abs(point.SiteLongitudeDegrees
                    - metadata.SiteLongitudeDegrees) <= 1e-9
                && Math.Abs(point.SiteElevationMeters
                    - metadata.SiteElevationMeters) <= 1e-6
                && point.AstapFieldOfViewDegrees >= 0.1
                && point.AstapFieldOfViewDegrees <= 20
                && IsUnitVector(point.VectorX, point.VectorY, point.VectorZ);
        }

        private static TppaQualificationVector FitAxis(
                TppaQualificationVector first,
                TppaQualificationVector second,
                TppaQualificationVector third,
                double latitudeDegrees) {
            var ab = new TppaQualificationVector(
                second.X - first.X, second.Y - first.Y, second.Z - first.Z);
            var bc = new TppaQualificationVector(
                third.X - second.X, third.Y - second.Y, third.Z - second.Z);
            var cross = new TppaQualificationVector(
                ab.Y * bc.Z - ab.Z * bc.Y,
                ab.Z * bc.X - ab.X * bc.Z,
                ab.X * bc.Y - ab.Y * bc.X);
            var norm = Math.Sqrt(cross.X * cross.X + cross.Y * cross.Y
                + cross.Z * cross.Z);
            if (!double.IsFinite(norm) || norm <= 1e-12) {
                throw new InvalidOperationException("witness RA-rotation geometry is degenerate");
            }
            var axis = new TppaQualificationVector(
                cross.X / norm, cross.Y / norm, cross.Z / norm);
            if ((latitudeDegrees >= 0 && axis.X < 0)
                    || (latitudeDegrees < 0 && axis.X > 0)) {
                axis = new(-axis.X, -axis.Y, -axis.Z);
            }
            return axis;
        }

        private static bool IsSha256(string value) =>
            value?.Length == 64 && value.All(character =>
                (character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f'));

        private static bool IsUnitVector(double x, double y, double z) {
            var length = Math.Sqrt(x * x + y * y + z * z);
            return double.IsFinite(length) && Math.Abs(length - 1) <= 1e-6;
        }

        private static double SignedAngularDelta(double first, double second) =>
            ((second - first + 540) % 360) - 180;

        private static string SanitizeFileName(string value) {
            var invalid = Path.GetInvalidFileNameChars();
            return new string((value ?? string.Empty)
                .Select(character => invalid.Contains(character) ? '_' : character)
                .ToArray());
        }

        private static void WriteCreateNew(string path, byte[] bytes) {
            using var stream = new FileStream(path, FileMode.CreateNew,
                FileAccess.Write, FileShare.Read);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
