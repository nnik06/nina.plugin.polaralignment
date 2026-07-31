using NINA.Core.Enum;
using NINA.Plugins.PolarAlignment.Instructions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaRuntimeSolveEvidence(
        DateTime ObservationUtc,
        string ContentSha256,
        double RightAscensionDegrees,
        double DeclinationDegrees,
        PierSide PierSide);

    internal sealed record TppaRuntimeDeterminationEvidence(
        DateTime StartedUtc,
        DateTime CompletedUtc,
        IReadOnlyList<TppaRuntimeSolveEvidence> Solves,
        Vector3 MountAxisVector,
        TppaThreePointGeometry Geometry);

    internal sealed record TppaRuntimeIdentityEvidence(
        string HardwareConfigurationId,
        string MechanicalStateId,
        string ClockDomainId,
        double ClockUncertaintyMilliseconds,
        string TppaInstrumentId,
        string SolverIdentity,
        string CoordinateFrame,
        string MountAxisVectorFrame);

    internal sealed record TppaRuntimeSiteEvidence(
        double LatitudeDegrees,
        double LongitudeDegrees,
        double ElevationMeters,
        string SourceId);

    internal sealed record TppaRuntimeAtmosphereEvidence(
        string Source,
        DateTime ObservationUtc,
        double PressureHPa,
        double TemperatureCelsius,
        double RelativeHumidityPercent);

    internal sealed record TppaRuntimeWitnessEvidence(
        DateTime ObservationUtc,
        string HardwareConfigurationId,
        string MechanicalStateId,
        string InstrumentId,
        string InputPathDigest,
        string CoordinateFrame,
        string MountAxisVectorFrame,
        string PoleTarget,
        string CalibrationDigest,
        DateTime CalibrationValidFromUtc,
        DateTime CalibrationValidUntilUtc,
        TppaWitnessUncertaintyEvidence Uncertainty,
        Vector3 MountAxisVector);

    internal sealed record TppaFastQualificationRuntimeEvidence(
        IReadOnlyList<TppaRuntimeDeterminationEvidence> Determinations,
        TppaRuntimeIdentityEvidence Identity,
        TppaRuntimeSiteEvidence Site,
        TppaRuntimeAtmosphereEvidence Atmosphere,
        TppaRuntimeWitnessEvidence IndependentWitness,
        bool RefractionAdjustmentEnabled,
        int PhysicalAdjustmentCommandCount);

    internal sealed record TppaFastQualificationRuntimeAdapterResult(
        TppaFastQualificationInput QualificationInput,
        string SourcePolarErrorVectorDigest,
        IReadOnlyList<string> DerivationIssues) {
        public bool IsDerivationComplete => DerivationIssues.Count == 0;

        public bool IsFastTruePoleQualified {
            get {
                if (!IsDerivationComplete) {
                    return false;
                }
                try {
                    return TppaFastQualification.Evaluate(QualificationInput)
                        .IsFastTruePoleQualified;
                } catch (ArgumentException) {
                    return false;
                } catch (ArithmeticException) {
                    return false;
                }
            }
        }
    }

    /// <summary>
    /// Converts factual VerificationOnly observations into the frozen fast-path
    /// input. It does not own persistence, execution, or motion authority.
    /// </summary>
    internal static class TppaFastQualificationRuntimeAdapter {
        private const double MaximumAtmosphereAgeSeconds = 300.0;
        private const double MaximumClockUncertaintyMilliseconds = 1000.0;
        internal const string TopocentricHorizonNorthWestUp =
            "topocentric-horizon-north-west-up";

        public static TppaFastQualificationRuntimeAdapterResult Derive(
                TppaFastQualificationRuntimeEvidence evidence) {
            try {
                return DeriveCore(Snapshot(evidence));
            } catch (Exception exception) when (
                    exception is not OutOfMemoryException) {
                return new(
                    SafeFailureInput(),
                    Digest("runtime-evidence-derivation-failed"),
                    new[] {
                        $"runtime evidence derivation failed closed ({exception.GetType().Name})"
                    });
            }
        }

        private static TppaFastQualificationRuntimeAdapterResult DeriveCore(
                TppaFastQualificationRuntimeEvidence evidence) {
            var issues = new List<string>();
            if (evidence == null) {
                issues.Add("runtime evidence is missing");
                return new(
                    SafeFailureInput(),
                    Digest("missing-runtime-evidence"),
                    issues);
            }

            var determinations = evidence.Determinations?
                .Where(determination => determination != null)
                .ToArray()
                ?? Array.Empty<TppaRuntimeDeterminationEvidence>();
            if (determinations.Length != (evidence.Determinations?.Count ?? 0)) {
                issues.Add("runtime determinations contain null entries");
            }

            var timestampsQualified = TryGetRunBounds(
                determinations,
                out var runStartedUtc,
                out var runCompletedUtc,
                issues);
            var durationSeconds = timestampsQualified
                ? (runCompletedUtc - runStartedUtc).TotalSeconds
                : double.MaxValue;

            var determinationFreshness = determinations
                .Select(determination => IsFreshDetermination(determination, issues))
                .ToArray();
            var allSolveDigests = determinations
                .SelectMany(determination =>
                    determination.Solves ?? Array.Empty<TppaRuntimeSolveEvidence>())
                .Select(solve => solve?.ContentSha256)
                .ToArray();
            var uniqueSolveDigests = allSolveDigests.Length > 0
                && allSolveDigests.All(IsSha256)
                && allSolveDigests.Distinct(StringComparer.Ordinal).Count()
                    == allSolveDigests.Length;
            if (!uniqueSolveDigests) {
                issues.Add("solve content digests are missing, invalid, or reused");
            }

            var vectors = determinations
                .Select(determination => determination.MountAxisVector)
                .ToArray();
            var vectorsQualified = vectors.Length > 0 && vectors.All(IsUnitVector);
            if (!vectorsQualified) {
                issues.Add("one or more fitted mount-axis vectors are missing, non-finite, or not unit length");
            }

            var siteQualified = IsSiteQualified(evidence.Site);
            if (!siteQualified) {
                issues.Add("site latitude, longitude, elevation, or source is unqualified");
            }
            var targetPoleVector = siteQualified
                ? TruePoleVector(evidence.Site.LatitudeDegrees)
                : null;

            var maximumPairwiseDelta = vectorsQualified
                ? MaximumPairwiseSeparationArcMinutes(vectors)
                : double.MaxValue;
            var finalReportedError = vectorsQualified && targetPoleVector != null
                ? AngularSeparationArcMinutes(vectors[^1], targetPoleVector)
                : double.MaxValue;
            if (!IsFiniteNonNegative(maximumPairwiseDelta)
                    || !IsFiniteNonNegative(finalReportedError)) {
                issues.Add("spherical mount-axis separation produced a non-finite result");
                maximumPairwiseDelta = double.MaxValue;
                finalReportedError = double.MaxValue;
                vectorsQualified = false;
            }

            var geometryQualified = determinations.Length > 0
                && determinations.All(determination =>
                    determination.Geometry.IsFinite
                    && !determination.Geometry.IsDegenerate);
            var minimumArcSpanQualified = geometryQualified
                && determinations.All(determination =>
                    determination.Geometry.MinimumPairwiseSeparationDegrees
                        >= TppaVerificationSettlePolicy.MinimumQualifiedTargetDistanceDegrees);
            if (!geometryQualified) {
                issues.Add("one or more three-point geometries are non-finite or degenerate");
            }
            if (!minimumArcSpanQualified) {
                issues.Add("one or more three-point arcs are below the qualified span");
            }

            var closureQualified = IsClosureQualified(determinations, issues);
            var identity = evidence.Identity;
            var identityQualified = IsIdentityQualified(identity);
            if (!identityQualified) {
                issues.Add("runtime hardware, mechanical, clock, instrument, solver, or frame identity is incomplete");
            }
            var clockQualified = identity != null
                && IsFiniteNonNegative(identity.ClockUncertaintyMilliseconds)
                && identity.ClockUncertaintyMilliseconds
                    <= MaximumClockUncertaintyMilliseconds;
            if (!clockQualified) {
                issues.Add("runtime clock uncertainty is missing or exceeds one second");
            }

            var atmosphere = evidence.Atmosphere;
            var pressureQualified = atmosphere != null
                && double.IsFinite(atmosphere.PressureHPa)
                && atmosphere.PressureHPa >= 500
                && atmosphere.PressureHPa <= 1100;
            var temperatureQualified = atmosphere != null
                && double.IsFinite(atmosphere.TemperatureCelsius)
                && atmosphere.TemperatureCelsius >= -100
                && atmosphere.TemperatureCelsius <= 100;
            var humidityQualified = atmosphere != null
                && double.IsFinite(atmosphere.RelativeHumidityPercent)
                && atmosphere.RelativeHumidityPercent >= 0
                && atmosphere.RelativeHumidityPercent <= 100;
            var atmosphereSourceQualified = atmosphere?.Source
                == TppaFastQualificationConventions.QualifiedLocalWeatherStation;
            var atmosphereFresh = timestampsQualified
                && atmosphere != null
                && IsUtc(atmosphere.ObservationUtc)
                && atmosphere.ObservationUtc
                    >= runStartedUtc.AddSeconds(-MaximumAtmosphereAgeSeconds)
                && atmosphere.ObservationUtc
                    <= runCompletedUtc.AddSeconds(MaximumAtmosphereAgeSeconds);
            var atmosphereQualified = atmosphereSourceQualified
                && atmosphereFresh
                && pressureQualified
                && temperatureQualified
                && humidityQualified;
            if (!atmosphereQualified) {
                issues.Add("runtime atmosphere evidence is missing, stale, implausible, or not from the qualified station");
            }

            var coordinateFrameQualified = identity?.CoordinateFrame
                    == TppaFastQualificationConventions.IcrsObservationEpoch
                && identity.MountAxisVectorFrame
                    == TopocentricHorizonNorthWestUp;
            var inputPathDigest = BuildInputPathDigest(
                determinations,
                identity,
                evidence.Site,
                evidence.Atmosphere);
            var sourceVectorDigest = BuildSourceVectorDigest(
                determinations,
                targetPoleVector);

            var witness = evidence.IndependentWitness;
            var witnessVectorQualified = IsUnitVector(witness?.MountAxisVector);
            var witnessTimeQualified = timestampsQualified
                && witness != null
                && IsUtc(witness.ObservationUtc)
                && witness.ObservationUtc
                    >= runStartedUtc.AddSeconds(-MaximumAtmosphereAgeSeconds)
                && witness.ObservationUtc
                    <= runCompletedUtc.AddSeconds(MaximumAtmosphereAgeSeconds);
            var witnessCalibrationCurrent = witness != null
                && IsSha256(witness.CalibrationDigest)
                && IsUtc(witness.CalibrationValidFromUtc)
                && IsUtc(witness.CalibrationValidUntilUtc)
                && witness.CalibrationValidFromUtc <= witness.ObservationUtc
                && witness.CalibrationValidUntilUtc >= witness.ObservationUtc;
            var witnessSameMechanicalState = witness != null
                && identity != null
                && witness.HardwareConfigurationId == identity.HardwareConfigurationId
                && witness.MechanicalStateId == identity.MechanicalStateId;
            var witnessDisjointInput = witness != null
                && IsSha256(witness.InputPathDigest)
                && witness.InputPathDigest != inputPathDigest
                && witness.InstrumentId != identity?.TppaInstrumentId;
            var witnessFrameQualified = witness?.CoordinateFrame
                == TppaFastQualificationConventions.IcrsObservationEpoch
                && witness.CoordinateFrame == identity?.CoordinateFrame
                && witness.MountAxisVectorFrame
                    == TopocentricHorizonNorthWestUp
                && witness.MountAxisVectorFrame == identity?.MountAxisVectorFrame;
            var witnessPoleQualified =
                witness?.PoleTarget == RefractionAlignmentTarget.TruePoleTarget;
            var uncertaintyPolicy = new TppaFastQualificationPolicy();
            var witnessUncertainty = TppaWitnessUncertainty.Evaluate(
                witness?.Uncertainty,
                uncertaintyPolicy.MaximumIndependentWitnessUncertainty95ArcSeconds,
                uncertaintyPolicy.MinimumIndependentWitnessCalibrationSamples,
                uncertaintyPolicy.MinimumIndependentWitnessClosureSamples);
            var witnessUncertaintyQualified = witness != null
                && witnessUncertainty.IsQualified
                && witness.Uncertainty.InputDigest == witness.InputPathDigest;
            if (!witnessUncertaintyQualified) {
                issues.Add("independent witness uncertainty is missing, unbounded, or detached from its input path");
            }
            var witnessQualified = witnessVectorQualified
                && witnessTimeQualified
                && witnessCalibrationCurrent
                && witnessSameMechanicalState
                && witnessDisjointInput
                && witnessFrameQualified
                && witnessPoleQualified
                && witnessUncertaintyQualified
                && !string.IsNullOrWhiteSpace(witness.InstrumentId);
            if (!witnessQualified) {
                issues.Add("independent witness evidence is missing, stale, aliased, or outside its calibration");
            }

            double? independentError = witnessQualified && targetPoleVector != null
                ? AngularSeparationArcMinutes(
                    witness.MountAxisVector,
                    targetPoleVector)
                : null;
            double? tppaToIndependentDelta = witnessQualified && vectorsQualified
                ? AngularSeparationArcMinutes(
                    vectors[^1],
                    witness.MountAxisVector)
                : null;

            if (evidence.PhysicalAdjustmentCommandCount < 0) {
                issues.Add("physical adjustment command count cannot be negative");
            }

            var input = new TppaFastQualificationInput(
                DurationSeconds: durationSeconds,
                FreshDeterminationCount: determinationFreshness.Count(value => value),
                FreshSolvesUncached:
                    determinationFreshness.All(value => value)
                    && uniqueSolveDigests,
                HardwareConfigurationId: identity?.HardwareConfigurationId,
                ClockDomainId: identity?.ClockDomainId,
                TppaInstrumentId: identity?.TppaInstrumentId,
                TppaInputPathDigest: inputPathDigest,
                SolverIdentity: identity?.SolverIdentity,
                MaximumPairwiseDeltaArcMinutes: maximumPairwiseDelta,
                FinalReportedErrorArcMinutes: finalReportedError,
                DeltaMetric:
                    TppaFastQualificationConventions.SphericalVectorSeparationArcMinutes,
                ErrorMetric:
                    TppaFastQualificationConventions.SphericalPolarErrorMagnitudeArcMinutes,
                NoPhysicalAdjustmentBetweenDeterminations:
                    evidence.PhysicalAdjustmentCommandCount == 0,
                GeometryQualified: geometryQualified,
                MinimumArcSpanQualified: minimumArcSpanQualified,
                ClosureQualified: closureQualified,
                RefractionAdjustmentEnabled: evidence.RefractionAdjustmentEnabled,
                PoleTarget: evidence.RefractionAdjustmentEnabled
                    ? RefractionAlignmentTarget.TruePoleTarget
                    : RefractionAlignmentTarget.ApparentPoleTarget,
                AtmosphereSource: atmosphere?.Source,
                AtmosphereQualified: atmosphereQualified,
                AtmosphereFresh: atmosphereFresh,
                StationPressureQualified: pressureQualified,
                AtmosphereTemperatureQualified: temperatureQualified,
                AtmosphereHumidityQualified: humidityQualified,
                SiteTimeProvenanceQualified:
                    siteQualified && clockQualified && timestampsQualified,
                CoordinateFrame: identity?.CoordinateFrame,
                CoordinateFrameQualified: coordinateFrameQualified,
                IndependentWitnessQualified: witnessQualified,
                IndependentWitnessSameMechanicalState: witnessSameMechanicalState,
                IndependentWitnessDisjointInputPathQualified: witnessDisjointInput,
                IndependentWitnessInstrumentId: witness?.InstrumentId,
                IndependentWitnessInputPathDigest: witness?.InputPathDigest,
                IndependentWitnessPoleTarget: witness?.PoleTarget,
                IndependentWitnessCoordinateFrame: witness?.CoordinateFrame,
                IndependentWitnessCalibrationDigest: witness?.CalibrationDigest,
                IndependentWitnessCalibrationCurrent: witnessCalibrationCurrent,
                IndependentWitnessUncertainty: witness?.Uncertainty,
                IndependentTruePoleErrorArcMinutes: independentError,
                TppaToIndependentDeltaArcMinutes: tppaToIndependentDelta);

            return new(input, sourceVectorDigest, issues);
        }

        internal static double AngularSeparationArcMinutes(
                Vector3 first,
                Vector3 second) {
            if (!IsFiniteVector(first) || !IsFiniteVector(second)
                    || !double.IsFinite(first.Length)
                    || !double.IsFinite(second.Length)
                    || !(first.Length > 0)
                    || !(second.Length > 0)) {
                return double.NaN;
            }
            var firstUnit = first.ToUnitVector();
            var secondUnit = second.ToUnitVector();
            var cosine = Math.Clamp(
                Vector3.ScalarProduct(firstUnit, secondUnit),
                -1.0,
                1.0);
            return Math.Acos(cosine) * 180.0 / Math.PI * 60.0;
        }

        internal static Vector3 TruePoleVector(double latitudeDegrees) {
            var latitudeRadians = Math.Abs(latitudeDegrees) * Math.PI / 180.0;
            var hemisphere = latitudeDegrees >= 0 ? 1.0 : -1.0;
            return new Vector3(
                hemisphere * Math.Cos(latitudeRadians),
                0,
                Math.Sin(latitudeRadians));
        }

        private static bool TryGetRunBounds(
                IReadOnlyList<TppaRuntimeDeterminationEvidence> determinations,
                out DateTime runStartedUtc,
                out DateTime runCompletedUtc,
                ICollection<string> issues) {
            runStartedUtc = default;
            runCompletedUtc = default;
            if (determinations.Count == 0
                    || determinations.Any(determination =>
                        !IsUtc(determination.StartedUtc)
                        || !IsUtc(determination.CompletedUtc)
                        || determination.CompletedUtc < determination.StartedUtc)) {
                issues.Add("runtime determination timestamps are missing, non-UTC, or reversed");
                return false;
            }
            for (var index = 1; index < determinations.Count; index++) {
                if (determinations[index].StartedUtc
                        < determinations[index - 1].CompletedUtc) {
                    issues.Add("runtime determination time ranges overlap or are out of order");
                    return false;
                }
            }
            runStartedUtc = determinations[0].StartedUtc;
            runCompletedUtc = determinations[^1].CompletedUtc;
            return true;
        }

        private static bool IsFreshDetermination(
                TppaRuntimeDeterminationEvidence determination,
                ICollection<string> issues) {
            var solves = determination.Solves;
            var valid = solves?.Count == 3
                && IsUtc(determination.StartedUtc)
                && IsUtc(determination.CompletedUtc)
                && determination.CompletedUtc >= determination.StartedUtc;
            if (!valid) {
                issues.Add("each determination must contain exactly three solves and a valid UTC interval");
                return false;
            }
            DateTime? previous = null;
            PierSide? determinationPierSide = null;
            foreach (var solve in solves) {
                if (solve == null
                        || !IsUtc(solve.ObservationUtc)
                        || solve.ObservationUtc < determination.StartedUtc
                        || solve.ObservationUtc > determination.CompletedUtc
                        || (previous.HasValue && solve.ObservationUtc <= previous.Value)
                        || !IsSha256(solve.ContentSha256)
                        || !double.IsFinite(solve.RightAscensionDegrees)
                        || solve.RightAscensionDegrees < 0
                        || solve.RightAscensionDegrees >= 360
                        || !double.IsFinite(solve.DeclinationDegrees)
                        || solve.DeclinationDegrees < -90
                        || solve.DeclinationDegrees > 90
                        || solve.PierSide == PierSide.pierUnknown
                        || (determinationPierSide.HasValue
                            && solve.PierSide != determinationPierSide.Value)) {
                    issues.Add("determination solve evidence is stale, unordered, malformed, or outside its interval");
                    return false;
                }
                determinationPierSide ??= solve.PierSide;
                previous = solve.ObservationUtc;
            }
            return true;
        }

        private static bool IsClosureQualified(
                IReadOnlyList<TppaRuntimeDeterminationEvidence> determinations,
                ICollection<string> issues) {
            if (determinations.Count < 2
                    || determinations[0].Solves?.Count != 3
                    || determinations[^1].Solves?.Count != 3) {
                issues.Add("returned-A closure cannot be derived from the solve evidence");
                return false;
            }
            var first = determinations[0].Solves[0];
            var returned = determinations[^1].Solves[0];
            if (first == null || returned == null
                    || first.PierSide == PierSide.pierUnknown
                    || returned.PierSide == PierSide.pierUnknown) {
                issues.Add("returned-A closure lacks known pier-side evidence");
                return false;
            }
            var closure = TppaDriftArcClosurePolicy.Evaluate(
                first.RightAscensionDegrees,
                first.DeclinationDegrees,
                returned.RightAscensionDegrees,
                returned.DeclinationDegrees,
                first.PierSide,
                returned.PierSide);
            if (!closure.IsSafe) {
                issues.Add($"returned-A closure failed: {closure.Reason}");
            }
            return closure.IsSafe;
        }

        private static bool IsIdentityQualified(TppaRuntimeIdentityEvidence identity) =>
            identity != null
            && !string.IsNullOrWhiteSpace(identity.HardwareConfigurationId)
            && !string.IsNullOrWhiteSpace(identity.MechanicalStateId)
            && !string.IsNullOrWhiteSpace(identity.ClockDomainId)
            && !string.IsNullOrWhiteSpace(identity.TppaInstrumentId)
            && !string.IsNullOrWhiteSpace(identity.SolverIdentity)
            && !string.IsNullOrWhiteSpace(identity.CoordinateFrame)
            && !string.IsNullOrWhiteSpace(identity.MountAxisVectorFrame);

        private static bool IsSiteQualified(TppaRuntimeSiteEvidence site) =>
            site != null
            && double.IsFinite(site.LatitudeDegrees)
            && site.LatitudeDegrees >= -90
            && site.LatitudeDegrees <= 90
            && double.IsFinite(site.LongitudeDegrees)
            && site.LongitudeDegrees >= -180
            && site.LongitudeDegrees <= 180
            && double.IsFinite(site.ElevationMeters)
            && site.ElevationMeters >= -500
            && site.ElevationMeters <= 10000
            && !string.IsNullOrWhiteSpace(site.SourceId);

        private static double MaximumPairwiseSeparationArcMinutes(
                IReadOnlyList<Vector3> vectors) {
            var maximum = 0.0;
            for (var first = 0; first < vectors.Count; first++) {
                for (var second = first + 1; second < vectors.Count; second++) {
                    maximum = Math.Max(
                        maximum,
                        AngularSeparationArcMinutes(vectors[first], vectors[second]));
                }
            }
            return maximum;
        }

        private static string BuildInputPathDigest(
                IEnumerable<TppaRuntimeDeterminationEvidence> determinations,
                TppaRuntimeIdentityEvidence identity,
                TppaRuntimeSiteEvidence site,
                TppaRuntimeAtmosphereEvidence atmosphere) {
            var solves = new JArray();
            foreach (var determination in determinations) {
                foreach (var solve in determination.Solves
                         ?? Array.Empty<TppaRuntimeSolveEvidence>()) {
                    solves.Add(new JObject {
                        ["observationUtc"] = solve?.ObservationUtc,
                        ["contentSha256"] = solve?.ContentSha256,
                        ["raDegrees"] = solve?.RightAscensionDegrees,
                        ["decDegrees"] = solve?.DeclinationDegrees,
                        ["pierSide"] = solve?.PierSide.ToString()
                    });
                }
            }
            var payload = new JObject {
                ["hardwareConfigurationId"] = identity?.HardwareConfigurationId,
                ["mechanicalStateId"] = identity?.MechanicalStateId,
                ["clockDomainId"] = identity?.ClockDomainId,
                ["tppaInstrumentId"] = identity?.TppaInstrumentId,
                ["solverIdentity"] = identity?.SolverIdentity,
                ["coordinateFrame"] = identity?.CoordinateFrame,
                ["mountAxisVectorFrame"] = identity?.MountAxisVectorFrame,
                ["site"] = site == null ? null : JObject.FromObject(site),
                ["atmosphere"] = atmosphere == null ? null : JObject.FromObject(atmosphere),
                ["solves"] = solves
            };
            return Digest(payload.ToString(Formatting.None));
        }

        private static string BuildSourceVectorDigest(
                IEnumerable<TppaRuntimeDeterminationEvidence> determinations,
                Vector3 targetPoleVector) {
            var vectors = new JArray(determinations.Select(determination =>
                VectorToken(determination.MountAxisVector)));
            var payload = new JObject {
                ["mountAxisVectors"] = vectors,
                ["targetPoleVector"] = VectorToken(targetPoleVector)
            };
            return Digest(payload.ToString(Formatting.None));
        }

        private static JToken VectorToken(Vector3 vector) =>
            vector == null
                ? JValue.CreateNull()
                : new JObject {
                    ["x"] = vector.X,
                    ["y"] = vector.Y,
                    ["z"] = vector.Z
                };

        private static TppaFastQualificationRuntimeEvidence Snapshot(
                TppaFastQualificationRuntimeEvidence evidence) {
            if (evidence == null) {
                return null;
            }
            var determinations = evidence.Determinations?
                .Select(determination => determination == null
                    ? null
                    : determination with {
                        Solves = determination.Solves?
                            .Select(solve => solve == null ? null : solve with { })
                            .ToArray(),
                        MountAxisVector = CopyVector(determination.MountAxisVector)
                    })
                .ToArray();
            var identity = evidence.Identity is null
                ? null
                : evidence.Identity with { };
            var site = evidence.Site is null
                ? null
                : evidence.Site with { };
            var atmosphere = evidence.Atmosphere is null
                ? null
                : evidence.Atmosphere with { };
            var witness = evidence.IndependentWitness is null
                ? null
                : evidence.IndependentWitness with {
                    Uncertainty = evidence.IndependentWitness.Uncertainty is null
                        ? null
                        : evidence.IndependentWitness.Uncertainty with { },
                    MountAxisVector =
                        CopyVector(evidence.IndependentWitness.MountAxisVector)
                };
            return evidence with {
                Determinations = determinations,
                Identity = identity,
                Site = site,
                Atmosphere = atmosphere,
                IndependentWitness = witness
            };
        }

        private static Vector3 CopyVector(Vector3 vector) =>
            vector == null ? null : new Vector3(vector.X, vector.Y, vector.Z);

        private static bool IsFiniteVector(Vector3 vector) =>
            vector != null
            && double.IsFinite(vector.X)
            && double.IsFinite(vector.Y)
            && double.IsFinite(vector.Z)
            && double.IsFinite(vector.Length)
            && vector.Length > 0;

        private static bool IsUnitVector(Vector3 vector) =>
            IsFiniteVector(vector)
            && Math.Abs(vector.Length - 1.0) <= 1e-6;

        private static bool IsFiniteNonNegative(double value) =>
            double.IsFinite(value) && value >= 0;

        private static bool IsUtc(DateTime value) =>
            value.Kind == DateTimeKind.Utc;

        private static bool IsSha256(string value) =>
            value?.Length == 64
            && value.All(character =>
                (character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f'));

        private static string Digest(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
                .ToLowerInvariant();

        private static TppaFastQualificationInput SafeFailureInput() =>
            new(
                DurationSeconds: double.MaxValue,
                FreshDeterminationCount: 0,
                FreshSolvesUncached: false,
                HardwareConfigurationId: null,
                ClockDomainId: null,
                TppaInstrumentId: null,
                TppaInputPathDigest: Digest("missing-input-path"),
                SolverIdentity: null,
                MaximumPairwiseDeltaArcMinutes: double.MaxValue,
                FinalReportedErrorArcMinutes: double.MaxValue,
                DeltaMetric: TppaFastQualificationConventions.SphericalVectorSeparationArcMinutes,
                ErrorMetric: TppaFastQualificationConventions.SphericalPolarErrorMagnitudeArcMinutes,
                NoPhysicalAdjustmentBetweenDeterminations: false,
                GeometryQualified: false,
                MinimumArcSpanQualified: false,
                ClosureQualified: false,
                RefractionAdjustmentEnabled: false,
                PoleTarget: RefractionAlignmentTarget.ApparentPoleTarget,
                AtmosphereSource: null,
                AtmosphereQualified: false,
                AtmosphereFresh: false,
                StationPressureQualified: false,
                AtmosphereTemperatureQualified: false,
                AtmosphereHumidityQualified: false,
                SiteTimeProvenanceQualified: false,
                CoordinateFrame: null,
                CoordinateFrameQualified: false,
                IndependentWitnessQualified: false,
                IndependentWitnessSameMechanicalState: false,
                IndependentWitnessDisjointInputPathQualified: false,
                IndependentWitnessInstrumentId: null,
                IndependentWitnessInputPathDigest: null,
                IndependentWitnessPoleTarget: null,
                IndependentWitnessCoordinateFrame: null,
                IndependentWitnessCalibrationDigest: null,
                IndependentWitnessCalibrationCurrent: false,
                IndependentWitnessUncertainty: null,
                IndependentTruePoleErrorArcMinutes: null,
                TppaToIndependentDeltaArcMinutes: null);
    }
}
