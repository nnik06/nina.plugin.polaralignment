using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaQualificationVector(
        double X,
        double Y,
        double Z);

    internal sealed record TppaQualificationSolveEvidence(
        DateTime ObservationUtc,
        string ContentSha256,
        double RightAscensionDegrees,
        double DeclinationDegrees,
        string PierSide,
        TppaQualificationVector UnitVector);

    internal sealed record TppaQualificationDeterminationEvidence(
        string DeterminationId,
        DateTime StartedUtc,
        DateTime CompletedUtc,
        string MechanicalStateDigest,
        int CorrectionSequenceNumber,
        bool FreshSolvesUncached,
        bool GeometryQualified,
        bool MinimumArcSpanQualified,
        bool ClosureQualified,
        IReadOnlyList<string> SourceVectorDigests,
        IReadOnlyList<TppaQualificationSolveEvidence> SourceSolves,
        TppaQualificationVector MountAxisVector);

    internal sealed record TppaRaRotationWitnessAcquisitionEvidence(
        int SchemaVersion,
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
        string PierSide);

    internal sealed record TppaLoadedAssemblyEvidence(
        string AssemblyName,
        string AssemblyVersion,
        string InformationalVersion,
        string Location,
        string Sha256,
        string ModuleVersionId);

    internal sealed record TppaQualificationRunEvidence(
        int SchemaVersion,
        string EvidenceDigest,
        string RunId,
        string SessionId,
        string ProducerId,
        string ProducerKind,
        string PipelineDigest,
        string HardwareConfigurationId,
        string MechanicalStateDigest,
        string ClockDomainId,
        double ClockUncertaintyMilliseconds,
        string TppaInstrumentId,
        string SolverIdentity,
        string SiteIdentity,
        double SiteLatitudeDegrees,
        double SiteLongitudeDegrees,
        double SiteElevationMeters,
        string CoordinateFrame,
        string MountAxisVectorFrame,
        string PoleTarget,
        string AtmosphereSource,
        DateTime AtmosphereObservationUtc,
        double AtmospherePressureHPa,
        double AtmosphereTemperatureCelsius,
        double AtmosphereRelativeHumidityPercent,
        bool RefractionAdjustmentEnabled,
        bool AtmosphereQualified,
        bool AtmosphereFresh,
        bool StationPressureQualified,
        bool AtmosphereTemperatureQualified,
        bool AtmosphereHumidityQualified,
        bool SiteTimeProvenanceQualified,
        bool CoordinateFrameQualified,
        IReadOnlyList<TppaQualificationDeterminationEvidence> Determinations,
        TppaQualificationVector TargetPoleVector,
        TppaLoadedAssemblyEvidence PluginAssembly,
        TppaLoadedAssemblyEvidence QualificationCoreAssembly);

    internal sealed record TppaQualificationWitnessEvidence(
        int SchemaVersion,
        string EvidenceDigest,
        string BindsRunId,
        string SessionId,
        string ProducerId,
        string ProducerKind,
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
        string EvidenceBasis,
        string MeasurementMethod,
        DateTime ObservationUtc,
        int CorrectionSequenceNumber,
        string InstrumentId,
        IReadOnlyList<string> SourceVectorDigests,
        IReadOnlyList<TppaQualificationSolveEvidence> SourceSolves,
        string TrajectoryPreflightDigest,
        bool TrajectoryPreflightQualified,
        double TrajectoryMinimumAltitudeDegrees,
        double TrajectoryMaximumSampleStepDegrees,
        double TrajectoryTotalArcDegrees,
        double TrajectoryDesignConditionProxy,
        string InitialPhd2AppState,
        string FinalPhd2AppState,
        bool GuideOutputRestored,
        IReadOnlyList<TppaRaRotationWitnessAcquisitionEvidence> Acquisitions,
        string CalibrationDigest,
        bool CalibrationCurrent,
        string CalibrationSourceProducerId,
        string CalibrationSourceDigest,
        bool CalibrationDerivedFromTppa,
        TppaWitnessUncertaintyEvidence Uncertainty,
        TppaQualificationVector MountAxisVector);

    internal sealed record TppaAbsoluteEvidenceBinding(
        bool EvidenceValid,
        bool? IsQualified,
        string TppaEvidenceSha256,
        string WitnessEvidenceSha256,
        string SourcePolarErrorVectorDigest,
        string ReceiptJson,
        IReadOnlyList<string> Issues);

    /// <summary>
    /// Binds two immutable, separately produced evidence files to the frozen
    /// fast-qualification policy. It validates provenance and computes every
    /// vector-derived quantity; it never grants motion authority.
    /// </summary>
    internal static class TppaAbsoluteEvidenceBinder {
        public const int CurrentEvidenceSchemaVersion = 6;
        public const string PluginAssemblyName = "NINA.Plugins.PolarAlignment";
        public const string QualificationCoreAssemblyName =
            "NINA.Plugins.PolarAlignment.QualificationCore";
        public const double MaximumWitnessDelaySeconds = 300.0;
        public const double MaximumClockUncertaintyMilliseconds = 1000.0;
        public const double MaximumAtmosphereAgeSeconds = 300.0;
        public const double MinimumQualifiedArcSpanDegrees = 15.0;
        public const double MaximumClosureSeparationDegrees = 0.25;
        public const string TppaProducerKind = "tppa-runtime";
        public const string WitnessProducerKind = "independent-witness";
        public const string AbsoluteTruePoleWitnessBasis = "absolute-true-pole";
        public const string DifferentialStabilityWitnessBasis =
            "differential-stability-only";
        public const string RaRotationCircleWitnessMethod =
            "ra-rotation-circle";
        public const string FitsDateObsExposureStart =
            "exposure-start";
        public const string FitsDateObsExposureMidpoint =
            "exposure-midpoint";
        public const string BlindNoMountHintSolvePolicy =
            "blind-no-mount-hint";
        public const string TopocentricHorizonNorthWestUp =
            "topocentric-horizon-north-west-up";

        private static readonly JsonSerializer Serializer = JsonSerializer.Create(
            new JsonSerializerSettings {
                Culture = CultureInfo.InvariantCulture,
                DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
                FloatParseHandling = FloatParseHandling.Double,
                MissingMemberHandling = MissingMemberHandling.Error,
                NullValueHandling = NullValueHandling.Include
            });

        public static TppaAbsoluteEvidenceBinding Bind(
                byte[] tppaEvidenceBytes,
                byte[] witnessEvidenceBytes,
                string policySourceSha256,
                DateTime createdUtc) {
            var issues = new List<string>();
            var tppaSha256 = Digest(tppaEvidenceBytes);
            var witnessSha256 = Digest(witnessEvidenceBytes);
            if (!IsSha256(policySourceSha256)) {
                issues.Add("compiled policy source digest is missing or invalid");
            }
            if (createdUtc.Kind != DateTimeKind.Utc) {
                issues.Add("qualification receipt time is not UTC");
            }

            var tppa = Parse<TppaQualificationRunEvidence>(
                tppaEvidenceBytes,
                "TPPA",
                issues);
            var witness = Parse<TppaQualificationWitnessEvidence>(
                witnessEvidenceBytes,
                "witness",
                issues);
            if (tppa == null || witness == null) {
                return Invalid(tppaSha256, witnessSha256, issues);
            }

            ValidateEvidenceDigest(
                tppaEvidenceBytes,
                tppa.EvidenceDigest,
                "TPPA",
                issues);
            ValidateEvidenceDigest(
                witnessEvidenceBytes,
                witness.EvidenceDigest,
                "witness",
                issues);
            ValidateRun(tppa, issues);
            var derived = TppaQualificationEvidenceDeriver.Derive(tppa, issues);
            ValidateWitness(witness, issues);
            var derivedWitness = TppaQualificationEvidenceDeriver.DeriveWitness(
                witness,
                issues);
            ValidateBinding(tppa, witness, issues);
            if (issues.Count > 0) {
                return Invalid(tppaSha256, witnessSha256, issues);
            }

            try {
                var ordered = derived.OrderedDeterminations;
                var vectors = derived.MountAxisVectors;
                var finalVector = vectors[^1];
                var input = new TppaFastQualificationInput(
                    DurationSeconds: (ordered[^1].CompletedUtc
                        - ordered[0].StartedUtc).TotalSeconds,
                    FreshDeterminationCount: ordered.Count,
                    FreshSolvesUncached: derived.FreshSolvesUncached,
                    HardwareConfigurationId: tppa.HardwareConfigurationId,
                    ClockDomainId: tppa.ClockDomainId,
                    TppaInstrumentId: tppa.TppaInstrumentId,
                    TppaInputPathDigest: tppaSha256,
                    SolverIdentity: tppa.SolverIdentity,
                    MaximumPairwiseDeltaArcMinutes: MaximumPairwiseArcMinutes(vectors),
                    FinalReportedErrorArcMinutes: AngularSeparationArcMinutes(
                        finalVector,
                        derived.TargetPoleVector),
                    DeltaMetric:
                        TppaFastQualificationConventions.SphericalVectorSeparationArcMinutes,
                    ErrorMetric:
                        TppaFastQualificationConventions.SphericalPolarErrorMagnitudeArcMinutes,
                    NoPhysicalAdjustmentBetweenDeterminations: true,
                    GeometryQualified: derived.GeometryQualified,
                    MinimumArcSpanQualified: derived.MinimumArcSpanQualified,
                    ClosureQualified: derived.ClosureQualified,
                    RefractionAdjustmentEnabled: tppa.RefractionAdjustmentEnabled,
                    PoleTarget: tppa.PoleTarget,
                    AtmosphereSource: tppa.AtmosphereSource,
                    AtmosphereQualified: derived.AtmosphereQualified,
                    AtmosphereFresh: derived.AtmosphereFresh,
                    StationPressureQualified: derived.StationPressureQualified,
                    AtmosphereTemperatureQualified:
                        derived.AtmosphereTemperatureQualified,
                    AtmosphereHumidityQualified:
                        derived.AtmosphereHumidityQualified,
                    SiteTimeProvenanceQualified:
                        derived.SiteTimeProvenanceQualified,
                    CoordinateFrame: tppa.CoordinateFrame,
                    CoordinateFrameQualified: derived.CoordinateFrameQualified,
                    IndependentWitnessQualified: true,
                    IndependentWitnessSameMechanicalState: true,
                    IndependentWitnessDisjointInputPathQualified: true,
                    IndependentWitnessInstrumentId: witness.InstrumentId,
                    IndependentWitnessInputPathDigest: witnessSha256,
                    IndependentWitnessPoleTarget: witness.PoleTarget,
                    IndependentWitnessCoordinateFrame: witness.CoordinateFrame,
                    IndependentWitnessCalibrationDigest: witness.CalibrationDigest,
                    IndependentWitnessCalibrationCurrent: witness.CalibrationCurrent,
                    IndependentWitnessUncertainty: witness.Uncertainty,
                    IndependentTruePoleErrorArcMinutes: AngularSeparationArcMinutes(
                        derivedWitness.MountAxisVector,
                        derived.TargetPoleVector),
                    TppaToIndependentDeltaArcMinutes: AngularSeparationArcMinutes(
                        finalVector,
                        derivedWitness.MountAxisVector));
                var result = TppaFastQualification.Evaluate(input);
                var sourceDigest = BuildSourceVectorDigest(
                    vectors,
                    derived.TargetPoleVector);
                var receipt = TppaFastQualificationReceipt.Create(
                    tppa.RunId,
                    createdUtc,
                    policySourceSha256,
                    sourceDigest,
                    input);
                return new TppaAbsoluteEvidenceBinding(
                    true,
                    result.IsFastTruePoleQualified,
                    tppaSha256,
                    witnessSha256,
                    sourceDigest,
                    receipt.ToJson(),
                    result.Issues);
            } catch (Exception ex) when (
                    ex is ArgumentException
                    || ex is ArithmeticException
                    || ex is InvalidOperationException) {
                issues.Add($"qualification evidence cannot be evaluated: {ex.Message}");
                return Invalid(tppaSha256, witnessSha256, issues);
            }
        }

        internal static string ComputeCanonicalEvidenceDigest(string json) {
            var token = JObject.Parse(
                json,
                new JsonLoadSettings {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                });
            token.Remove("evidenceDigest");
            return Digest(Encoding.UTF8.GetBytes(
                SortToken(token).ToString(Formatting.None)));
        }

        private static T Parse<T>(
                byte[] evidenceBytes,
                string label,
                ICollection<string> issues) where T : class {
            if (evidenceBytes == null || evidenceBytes.Length == 0) {
                issues.Add($"{label} evidence is empty");
                return null;
            }
            try {
                var json = new UTF8Encoding(false, true).GetString(evidenceBytes);
                var token = JObject.Parse(
                    json,
                    new JsonLoadSettings {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                    });
                return token.ToObject<T>(Serializer);
            } catch (Exception ex) when (
                    ex is JsonException
                    || ex is DecoderFallbackException
                    || ex is ArgumentException) {
                issues.Add($"{label} evidence cannot be parsed strictly: {ex.Message}");
                return null;
            }
        }

        private static void ValidateRun(
                TppaQualificationRunEvidence evidence,
                ICollection<string> issues) {
            if (evidence.SchemaVersion != CurrentEvidenceSchemaVersion) {
                issues.Add("TPPA evidence schema version is unsupported");
            }
            RequireNonBlank(evidence.RunId, "TPPA run identity", issues);
            RequireNonBlank(evidence.SessionId, "TPPA session identity", issues);
            RequireNonBlank(evidence.ProducerId, "TPPA producer identity", issues);
            if (evidence.ProducerKind != TppaProducerKind) {
                issues.Add("TPPA producer kind is invalid");
            }
            RequireSha256(evidence.PipelineDigest, "TPPA pipeline digest", issues);
            RequireNonBlank(
                evidence.HardwareConfigurationId,
                "TPPA hardware configuration identity",
                issues);
            RequireSha256(evidence.MechanicalStateDigest, "TPPA mechanical state digest", issues);
            RequireNonBlank(evidence.ClockDomainId, "TPPA clock domain", issues);
            RequireNonBlank(evidence.TppaInstrumentId, "TPPA instrument identity", issues);
            RequireNonBlank(evidence.SolverIdentity, "TPPA solver identity", issues);
            RequireNonBlank(evidence.SiteIdentity, "TPPA site identity", issues);
            RequireNonBlank(evidence.CoordinateFrame, "TPPA coordinate frame", issues);
            RequireNonBlank(
                evidence.MountAxisVectorFrame,
                "TPPA mount-axis vector frame",
                issues);
            RequireNonBlank(evidence.PoleTarget, "TPPA pole target", issues);
            ValidateLoadedAssembly(
                evidence.PluginAssembly,
                PluginAssemblyName,
                "TPPA loaded plugin assembly",
                issues);
            ValidateLoadedAssembly(
                evidence.QualificationCoreAssembly,
                QualificationCoreAssemblyName,
                "TPPA loaded qualification-core assembly",
                issues);
            if (evidence.PluginAssembly != null
                    && IsSha256(evidence.PipelineDigest)
                    && !string.Equals(
                        evidence.PipelineDigest,
                        evidence.PluginAssembly.Sha256,
                        StringComparison.OrdinalIgnoreCase)) {
                issues.Add(
                    "TPPA pipeline digest does not match the loaded plugin assembly SHA-256");
            }
            RequireNonBlank(evidence.AtmosphereSource, "TPPA atmosphere source", issues);
            if (!IsUnitVector(evidence.TargetPoleVector)) {
                issues.Add("TPPA target pole vector is missing, non-finite, or not unit length");
            }

            var determinations = evidence.Determinations?.ToArray()
                ?? Array.Empty<TppaQualificationDeterminationEvidence>();
            var presentDeterminations = determinations
                .Where(value => value != null)
                .ToArray();
            if (presentDeterminations.Length < 3) {
                issues.Add("TPPA evidence has fewer than three determinations");
            }
            if (presentDeterminations.Length != determinations.Length) {
                issues.Add("TPPA evidence contains a null determination");
            }
            if (presentDeterminations.Length == 0) { return; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var sourceDigests = new HashSet<string>(StringComparer.Ordinal);
            DateTime? previousCompleted = null;
            var correctionSequenceNumber =
                presentDeterminations[0].CorrectionSequenceNumber;
            foreach (var determination in presentDeterminations
                         .OrderBy(value => value.StartedUtc)) {
                if (string.IsNullOrWhiteSpace(determination.DeterminationId)
                        || !ids.Add(determination.DeterminationId)) {
                    issues.Add("TPPA determination identity is missing or duplicated");
                }
                if (!IsUtc(determination.StartedUtc)
                        || !IsUtc(determination.CompletedUtc)
                        || determination.CompletedUtc <= determination.StartedUtc) {
                    issues.Add("TPPA determination timestamps are invalid or not UTC");
                }
                if (previousCompleted.HasValue
                        && determination.StartedUtc < previousCompleted.Value) {
                    issues.Add("TPPA determinations overlap or are out of order");
                }
                previousCompleted = determination.CompletedUtc;
                if (determination.MechanicalStateDigest
                        != evidence.MechanicalStateDigest) {
                    issues.Add("TPPA determination mechanical state changed");
                }
                if (determination.CorrectionSequenceNumber
                        != correctionSequenceNumber) {
                    issues.Add("TPPA correction sequence changed between determinations");
                }
                if (!IsUnitVector(determination.MountAxisVector)) {
                    issues.Add("TPPA determination mount-axis vector is invalid");
                }
                var sources = determination.SourceVectorDigests?.ToArray()
                    ?? Array.Empty<string>();
                if (sources.Length < 3) {
                    issues.Add("TPPA determination has fewer than three source vectors");
                }
                foreach (var digest in sources) {
                    if (!IsSha256(digest) || !sourceDigests.Add(digest)) {
                        issues.Add("TPPA source vector digest is invalid or reused");
                    }
                }
            }
        }

        private static void ValidateWitness(
                TppaQualificationWitnessEvidence evidence,
                ICollection<string> issues) {
            if (evidence.SchemaVersion != CurrentEvidenceSchemaVersion) {
                issues.Add("witness evidence schema version is unsupported");
            }
            RequireNonBlank(evidence.BindsRunId, "witness bound run identity", issues);
            RequireNonBlank(evidence.SessionId, "witness session identity", issues);
            RequireNonBlank(evidence.ProducerId, "witness producer identity", issues);
            if (evidence.ProducerKind != WitnessProducerKind) {
                issues.Add("witness producer kind is invalid");
            }
            RequireSha256(evidence.PipelineDigest, "witness pipeline digest", issues);
            RequireNonBlank(
                evidence.HardwareConfigurationId,
                "witness hardware configuration identity",
                issues);
            RequireSha256(evidence.MechanicalStateDigest, "witness mechanical state digest", issues);
            RequireNonBlank(evidence.SiteIdentity, "witness site identity", issues);
            RequireNonBlank(evidence.ClockDomainId, "witness clock domain", issues);
            RequireNonBlank(evidence.CoordinateFrame, "witness coordinate frame", issues);
            RequireNonBlank(
                evidence.MountAxisVectorFrame,
                "witness mount-axis vector frame",
                issues);
            RequireNonBlank(evidence.PoleTarget, "witness pole target", issues);
            if (evidence.EvidenceBasis != AbsoluteTruePoleWitnessBasis) {
                issues.Add("witness evidence basis is not absolute true-pole evidence");
            }
            if (evidence.MeasurementMethod != RaRotationCircleWitnessMethod) {
                issues.Add("witness measurement method is not a qualified RA-rotation circle");
            }
            if (!double.IsFinite(evidence.ClockUncertaintyMilliseconds)
                    || evidence.ClockUncertaintyMilliseconds < 0
                    || evidence.ClockUncertaintyMilliseconds
                        > MaximumClockUncertaintyMilliseconds) {
                issues.Add("witness clock uncertainty is missing or exceeds one second");
            }
            if (!double.IsFinite(evidence.SiteLatitudeDegrees)
                    || evidence.SiteLatitudeDegrees < -90
                    || evidence.SiteLatitudeDegrees > 90
                    || !double.IsFinite(evidence.SiteLongitudeDegrees)
                    || evidence.SiteLongitudeDegrees < -180
                    || evidence.SiteLongitudeDegrees > 180
                    || !double.IsFinite(evidence.SiteElevationMeters)
                    || evidence.SiteElevationMeters < -500
                    || evidence.SiteElevationMeters > 10000) {
                issues.Add("witness raw site coordinates or elevation are invalid");
            }
            RequireNonBlank(evidence.InstrumentId, "witness instrument identity", issues);
            if (!IsUtc(evidence.ObservationUtc)) {
                issues.Add("witness observation time is not UTC");
            }
            if (!IsUnitVector(evidence.MountAxisVector)) {
                issues.Add("witness mount-axis vector is missing, non-finite, or not unit length");
            }
            var sources = evidence.SourceVectorDigests?.ToArray()
                ?? Array.Empty<string>();
            if (sources.Length == 0
                    || sources.Any(digest => !IsSha256(digest))
                    || sources.Distinct(StringComparer.Ordinal).Count() != sources.Length) {
                issues.Add("witness source vector digests are missing, invalid, or duplicated");
            }
            ValidateWitnessAcquisitions(evidence, issues);
            RequireSha256(evidence.CalibrationDigest, "witness calibration digest", issues);
            RequireNonBlank(
                evidence.CalibrationSourceProducerId,
                "witness calibration producer identity",
                issues);
            RequireSha256(
                evidence.CalibrationSourceDigest,
                "witness calibration source digest",
                issues);
            if (!evidence.CalibrationCurrent) {
                issues.Add("witness calibration is stale");
            }
            if (evidence.CalibrationDerivedFromTppa) {
                issues.Add("witness calibration is derived from TPPA evidence");
            }
            ValidateUncertainty(evidence, issues);
        }

        private static void ValidateWitnessAcquisitions(
                TppaQualificationWitnessEvidence evidence,
                ICollection<string> issues) {
            RequireSha256(
                evidence.TrajectoryPreflightDigest,
                "witness trajectory preflight digest",
                issues);
            if (!evidence.TrajectoryPreflightQualified
                    || !double.IsFinite(evidence.TrajectoryMinimumAltitudeDegrees)
                    || evidence.TrajectoryMinimumAltitudeDegrees < 40
                    || !double.IsFinite(evidence.TrajectoryMaximumSampleStepDegrees)
                    || evidence.TrajectoryMaximumSampleStepDegrees <= 0
                    || evidence.TrajectoryMaximumSampleStepDegrees > 1
                    || !double.IsFinite(evidence.TrajectoryTotalArcDegrees)
                    || evidence.TrajectoryTotalArcDegrees < 45
                    || !double.IsFinite(evidence.TrajectoryDesignConditionProxy)
                    || evidence.TrajectoryDesignConditionProxy <= 0
                    || evidence.TrajectoryDesignConditionProxy > 4) {
                issues.Add("witness full-trajectory preflight is missing or unqualified");
            }
            RequireNonBlank(
                evidence.InitialPhd2AppState,
                "witness initial PHD2 app state",
                issues);
            RequireNonBlank(
                evidence.FinalPhd2AppState,
                "witness final PHD2 app state",
                issues);
            if (!evidence.GuideOutputRestored) {
                issues.Add("witness PHD2 guide-output state was not restored");
            }

            var acquisitions = evidence.Acquisitions?
                .Where(value => value != null)
                .ToArray()
                ?? Array.Empty<TppaRaRotationWitnessAcquisitionEvidence>();
            if (acquisitions.Length != 4
                    || acquisitions.Length != (evidence.Acquisitions?.Count ?? 0)) {
                issues.Add("witness requires exactly four non-null A/B/C/A acquisition receipts");
                return;
            }
            if (!acquisitions.Select(value => value.PositionId)
                    .SequenceEqual(new[] { "A", "B", "C", "A" })
                    || !acquisitions.Select(value => value.SequenceIndex)
                        .SequenceEqual(new[] { 0, 1, 2, 3 })) {
                issues.Add("witness acquisition receipts are not in exact A/B/C/A order");
            }
            if (acquisitions.Select(value => value.MountCommandId)
                    .Any(string.IsNullOrWhiteSpace)
                    || acquisitions.Select(value => value.MountCommandId)
                        .Distinct(StringComparer.Ordinal).Count() != 4) {
                issues.Add("witness mount-command identities are missing or reused");
            }
            if (acquisitions.Any(value =>
                    value.SchemaVersion
                        != TppaRaRotationWitnessPointReceipt.CurrentSchemaVersion
                    || !IsUtc(value.MountCommandIssuedUtc)
                    || !IsUtc(value.MountCommandCompletedUtc)
                    || !IsUtc(value.CaptureStartedUtc)
                    || !IsUtc(value.ObservationUtc)
                    || !IsUtc(value.FitsDateObsUtc)
                    || (value.FitsDateObsConvention != FitsDateObsExposureStart
                        && value.FitsDateObsConvention
                            != FitsDateObsExposureMidpoint)
                    || value.MountCommandCompletedUtc < value.MountCommandIssuedUtc
                    || value.CaptureStartedUtc < value.MountCommandCompletedUtc
                    || value.ObservationUtc < value.CaptureStartedUtc
                    || !value.TrackingEnabled
                    || value.Slewing
                    || value.GuideOutputEnabled
                    || string.IsNullOrWhiteSpace(value.Phd2AppState)
                    || !double.IsFinite(value.ExposureSeconds)
                    || value.ExposureSeconds <= 0
                    || !double.IsFinite(value.FitsTimestampUncertaintyMilliseconds)
                    || value.FitsTimestampUncertaintyMilliseconds < 0
                    || value.FitsTimestampUncertaintyMilliseconds > 1000
                    || !IsSha256(value.SourceImageSha256)
                    || !IsSha256(value.SolverOutputSha256)
                    || !IsSha256(value.SolverBinarySha256)
                    || string.IsNullOrWhiteSpace(value.SolverIdentity)
                    || value.SolverHintPolicy != BlindNoMountHintSolvePolicy
                    || value.SourceCoordinateFrame
                        != TppaFastQualificationConventions.IcrsObservationEpoch
                    || Math.Abs(value.SiteLatitudeDegrees
                        - evidence.SiteLatitudeDegrees) > 1e-9
                    || Math.Abs(value.SiteLongitudeDegrees
                        - evidence.SiteLongitudeDegrees) > 1e-9
                    || Math.Abs(value.SiteElevationMeters
                        - evidence.SiteElevationMeters) > 1e-6
                    || !double.IsFinite(value.AstapFieldOfViewDegrees)
                    || value.AstapFieldOfViewDegrees < 0.1
                    || value.AstapFieldOfViewDegrees > 20
                    || !AllFinite(
                        value.CommandedRightAscensionDegrees,
                        value.CommandedDeclinationDegrees,
                        value.MountAzimuthDegrees,
                        value.MountAltitudeDegrees,
                        value.SolvedRightAscensionDegrees,
                        value.SolvedDeclinationDegrees,
                        value.PositionAngleDegrees))) {
                issues.Add("witness acquisition provenance is incomplete, non-finite, moving, guided, or mistimed");
            }
            foreach (var value in acquisitions) {
                var midpoint = value.CaptureStartedUtc
                    + TimeSpan.FromSeconds(value.ExposureSeconds / 2);
                var midpointError = Math.Abs(
                    (value.ObservationUtc - midpoint).TotalMilliseconds);
                var fitsMidpoint = value.FitsDateObsConvention
                    == FitsDateObsExposureStart
                    ? value.FitsDateObsUtc
                        + TimeSpan.FromSeconds(value.ExposureSeconds / 2)
                    : value.FitsDateObsUtc;
                var fitsError = Math.Abs(
                    (fitsMidpoint - value.ObservationUtc).TotalMilliseconds);
                if (midpointError > evidence.ClockUncertaintyMilliseconds
                        || fitsError > value.FitsTimestampUncertaintyMilliseconds) {
                    issues.Add(
                        $"witness {value.PositionId} exposure midpoint or FITS timestamp is inconsistent");
                }
            }
            if (acquisitions.Select(value => value.PierSide)
                    .Distinct(StringComparer.Ordinal).Count() != 1
                    || acquisitions[0].PierSide == "pierUnknown") {
                issues.Add("witness acquisition receipts have unknown or changing pier side");
            }
            var declinationSpan = acquisitions
                .Max(value => value.CommandedDeclinationDegrees)
                - acquisitions.Min(value => value.CommandedDeclinationDegrees);
            var firstLeg = SignedAngularDelta(
                acquisitions[0].CommandedRightAscensionDegrees,
                acquisitions[1].CommandedRightAscensionDegrees);
            var secondLeg = SignedAngularDelta(
                acquisitions[1].CommandedRightAscensionDegrees,
                acquisitions[2].CommandedRightAscensionDegrees);
            var returnClosure = Math.Abs(SignedAngularDelta(
                acquisitions[0].CommandedRightAscensionDegrees,
                acquisitions[3].CommandedRightAscensionDegrees));
            var totalArc = Math.Abs(firstLeg + secondLeg);
            if (declinationSpan > 0.05
                    || Math.Sign(firstLeg) == 0
                    || Math.Sign(firstLeg) != Math.Sign(secondLeg)
                    || Math.Abs(firstLeg) < 15
                    || Math.Abs(secondLeg) < 15
                    || totalArc < 45
                    || Math.Abs(totalArc - evidence.TrajectoryTotalArcDegrees) > 0.5
                    || returnClosure > MaximumClosureSeparationDegrees) {
                issues.Add("witness commanded geometry is not fixed-declination monotonic A-B-C with qualified A return");
            }

            var sourceDigests = evidence.SourceVectorDigests
                ?? Array.Empty<string>();
            var acquisitionDigests = acquisitions
                .Select(value => value.SourceImageSha256)
                .ToArray();
            if (!sourceDigests.SequenceEqual(acquisitionDigests)) {
                issues.Add("witness source-vector digests do not bind the acquired guider images");
            }
            var solves = evidence.SourceSolves
                ?? Array.Empty<TppaQualificationSolveEvidence>();
            if (solves.Count != acquisitions.Length
                    || solves.Where(value => value != null)
                        .Select(value => value.ContentSha256)
                        .SequenceEqual(acquisitionDigests) == false
                    || solves.Where(value => value != null)
                        .Select(value => value.ObservationUtc)
                        .SequenceEqual(acquisitions.Select(value => value.ObservationUtc))
                        == false) {
                issues.Add("witness derived solves are detached from acquisition receipts");
            }
        }

        private static void ValidateBinding(
                TppaQualificationRunEvidence tppa,
                TppaQualificationWitnessEvidence witness,
                ICollection<string> issues) {
            if (witness.BindsRunId != tppa.RunId) {
                issues.Add("witness does not bind the TPPA run identity");
            }
            if (witness.SessionId != tppa.SessionId) {
                issues.Add("TPPA and witness session identities differ");
            }
            if (!string.IsNullOrWhiteSpace(witness.ProducerId)
                    && !string.IsNullOrWhiteSpace(tppa.ProducerId)
                    && witness.ProducerId == tppa.ProducerId) {
                issues.Add("TPPA and witness producer identities alias");
            }
            if (IsSha256(witness.PipelineDigest)
                    && IsSha256(tppa.PipelineDigest)
                    && witness.PipelineDigest == tppa.PipelineDigest) {
                issues.Add("TPPA and witness pipeline digests alias");
            }
            if (witness.HardwareConfigurationId != tppa.HardwareConfigurationId
                    || witness.MechanicalStateDigest != tppa.MechanicalStateDigest
                    || witness.SiteIdentity != tppa.SiteIdentity
                    || witness.SiteLatitudeDegrees != tppa.SiteLatitudeDegrees
                    || witness.SiteLongitudeDegrees != tppa.SiteLongitudeDegrees
                    || witness.SiteElevationMeters != tppa.SiteElevationMeters
                    || witness.CoordinateFrame != tppa.CoordinateFrame
                    || witness.MountAxisVectorFrame != tppa.MountAxisVectorFrame
                    || witness.PoleTarget != tppa.PoleTarget) {
                issues.Add("TPPA and witness physical, site, frame, or pole identities differ");
            }
            if (!string.IsNullOrWhiteSpace(witness.InstrumentId)
                    && !string.IsNullOrWhiteSpace(tppa.TppaInstrumentId)
                    && witness.InstrumentId == tppa.TppaInstrumentId) {
                issues.Add("TPPA and witness instrument identities alias");
            }

            var determinations = (tppa.Determinations
                    ?? Array.Empty<TppaQualificationDeterminationEvidence>())
                .Where(value => value != null)
                .OrderBy(value => value.StartedUtc)
                .ToArray();
            if (determinations.Length > 0) {
                var final = determinations[^1];
                if (witness.CorrectionSequenceNumber
                        != final.CorrectionSequenceNumber) {
                    issues.Add(
                        "witness correction sequence does not match final TPPA state");
                }
                var witnessDelay =
                    (witness.ObservationUtc - final.CompletedUtc).TotalSeconds;
                var witnessStart = witness.SourceSolves?
                    .Where(value => value != null)
                    .Select(value => value.ObservationUtc)
                    .DefaultIfEmpty(default)
                    .Min() ?? default;
                if (!double.IsFinite(witnessDelay)
                        || witnessDelay < 0
                        || witnessDelay > MaximumWitnessDelaySeconds
                        || witnessStart.Kind != DateTimeKind.Utc
                        || witnessStart < final.CompletedUtc) {
                    issues.Add(
                        "witness arc is stale or overlaps/precedes TPPA completion");
                }
            }

            var tppaSources = determinations
                .SelectMany(value => value.SourceVectorDigests
                    ?? Array.Empty<string>())
                .ToHashSet(StringComparer.Ordinal);
            var witnessSources = (witness.SourceVectorDigests
                    ?? Array.Empty<string>())
                .ToHashSet(StringComparer.Ordinal);
            if (tppaSources.Overlaps(witnessSources)) {
                issues.Add("TPPA and witness source-vector inputs overlap");
            }
            if (tppaSources.Contains(witness.CalibrationSourceDigest)
                    || witnessSources.Contains(witness.CalibrationSourceDigest)
                    || (!string.IsNullOrWhiteSpace(
                            witness.CalibrationSourceProducerId)
                        && witness.CalibrationSourceProducerId == tppa.ProducerId)
                    || (!string.IsNullOrWhiteSpace(
                            witness.CalibrationSourceProducerId)
                        && witness.CalibrationSourceProducerId
                            == witness.ProducerId)) {
                issues.Add(
                    "witness calibration provenance aliases TPPA or witness evidence");
            }
        }

        private static void ValidateUncertainty(
                TppaQualificationWitnessEvidence witness,
                ICollection<string> issues) {
            var uncertainty = witness.Uncertainty;
            if (uncertainty == null) {
                issues.Add("witness uncertainty evidence is missing");
                return;
            }
            if (uncertainty.InputDigest != witness.CalibrationSourceDigest
                    || uncertainty.EvidenceDigest != witness.CalibrationDigest
                    || TppaWitnessUncertainty.ComputeEvidenceDigest(uncertainty)
                        != uncertainty.EvidenceDigest) {
                issues.Add("witness uncertainty evidence is not bound to its calibration payload");
            }
            if (uncertainty.CalibrationSampleCount < 3
                    || uncertainty.ClosureSampleCount < 2) {
                issues.Add("witness uncertainty calibration or closure sample floor is unmet");
            }
            var terms = new[] {
                uncertainty.MeasurementStandardUncertaintyArcSeconds,
                uncertainty.CalibrationResidualArcSeconds,
                uncertainty.OrientationModelResidualArcSeconds,
                uncertainty.ClosureResidualArcSeconds,
                uncertainty.FrameSystematicBoundArcSeconds,
                uncertainty.DistortionSystematicBoundArcSeconds,
                uncertainty.MechanicalSystematicBoundArcSeconds
            };
            if (terms.Any(value => !double.IsFinite(value) || value <= 0)) {
                issues.Add("witness uncertainty terms must all be finite and positive");
            }
        }

        private static void ValidateLoadedAssembly(
                TppaLoadedAssemblyEvidence evidence,
                string expectedAssemblyName,
                string label,
                ICollection<string> issues) {
            if (evidence == null) {
                issues.Add($"{label} evidence is missing");
                return;
            }
            if (!string.Equals(
                    evidence.AssemblyName,
                    expectedAssemblyName,
                    StringComparison.Ordinal)) {
                issues.Add($"{label} name is invalid");
            }
            if (!Version.TryParse(evidence.AssemblyVersion, out var version)
                    || version.Build < 0
                    || version.Revision < 0) {
                issues.Add($"{label} version is invalid");
            }
            if (string.IsNullOrWhiteSpace(evidence.InformationalVersion)) {
                issues.Add($"{label} informational version is missing");
            }
            if (string.IsNullOrWhiteSpace(evidence.Location)
                    || !Path.IsPathRooted(evidence.Location)) {
                issues.Add($"{label} location is missing or not absolute");
            }
            if (!IsSha256(evidence.Sha256)) {
                issues.Add($"{label} SHA-256 is invalid");
            }
            if (!Guid.TryParseExact(evidence.ModuleVersionId, "D", out var mvid)
                    || mvid == Guid.Empty) {
                issues.Add($"{label} MVID is invalid");
            }
        }

        private static void ValidateEvidenceDigest(
                byte[] evidenceBytes,
                string expected,
                string label,
                ICollection<string> issues) {
            try {
                var json = new UTF8Encoding(false, true).GetString(evidenceBytes);
                var actual = ComputeCanonicalEvidenceDigest(json);
                if (!IsSha256(expected) || actual != expected) {
                    issues.Add($"{label} evidence digest is missing or does not match");
                }
            } catch (Exception ex) when (
                    ex is JsonException
                    || ex is DecoderFallbackException) {
                issues.Add($"{label} evidence digest cannot be recomputed: {ex.Message}");
            }
        }

        private static double MaximumPairwiseArcMinutes(
                IReadOnlyList<TppaQualificationVector> vectors) {
            var maximum = 0.0;
            for (var left = 0; left < vectors.Count; left++) {
                for (var right = left + 1; right < vectors.Count; right++) {
                    maximum = Math.Max(
                        maximum,
                        AngularSeparationArcMinutes(vectors[left], vectors[right]));
                }
            }
            return maximum;
        }

        private static double AngularSeparationArcMinutes(
                TppaQualificationVector first,
                TppaQualificationVector second) {
            var dot = first.X * second.X + first.Y * second.Y + first.Z * second.Z;
            var cosine = Math.Clamp(dot, -1.0, 1.0);
            return Math.Acos(cosine) * 180.0 / Math.PI * 60.0;
        }

        private static string BuildSourceVectorDigest(
                IEnumerable<TppaQualificationVector> vectors,
                TppaQualificationVector target) {
            var payload = new JObject {
                ["mountAxisVectors"] = new JArray(vectors.Select(VectorToken)),
                ["targetPoleVector"] = VectorToken(target)
            };
            return Digest(Encoding.UTF8.GetBytes(payload.ToString(Formatting.None)));
        }

        private static JObject VectorToken(TppaQualificationVector vector) =>
            new() {
                ["x"] = vector.X,
                ["y"] = vector.Y,
                ["z"] = vector.Z
            };

        private static bool IsUnitVector(TppaQualificationVector vector) {
            if (vector == null
                    || !double.IsFinite(vector.X)
                    || !double.IsFinite(vector.Y)
                    || !double.IsFinite(vector.Z)) {
                return false;
            }
            var length = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y
                + vector.Z * vector.Z);
            return double.IsFinite(length) && Math.Abs(length - 1.0) <= 1e-6;
        }

        private static bool AllFinite(params double[] values) =>
            values.All(double.IsFinite);

        private static double SignedAngularDelta(double first, double second) =>
            ((second - first + 540) % 360) - 180;

        private static bool IsUtc(DateTime value) => value.Kind == DateTimeKind.Utc;

        private static void RequireNonBlank(
                string value,
                string label,
                ICollection<string> issues) {
            if (string.IsNullOrWhiteSpace(value)) {
                issues.Add($"{label} is missing");
            }
        }

        private static void RequireSha256(
                string value,
                string label,
                ICollection<string> issues) {
            if (!IsSha256(value)) {
                issues.Add($"{label} is missing or invalid");
            }
        }

        private static bool IsSha256(string value) =>
            value?.Length == 64
            && value.All(character =>
                (character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f'));

        private static TppaAbsoluteEvidenceBinding Invalid(
                string tppaSha256,
                string witnessSha256,
                IReadOnlyList<string> issues) =>
            new(
                false,
                null,
                tppaSha256,
                witnessSha256,
                null,
                null,
                issues.ToArray());

        private static string Digest(byte[] bytes) =>
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        private static JToken SortToken(JToken token) => token switch {
            JObject value => new JObject(value.Properties()
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => new JProperty(
                    property.Name,
                    SortToken(property.Value)))),
            JArray value => new JArray(value.Select(SortToken)),
            _ => token.DeepClone()
        };
    }
}
