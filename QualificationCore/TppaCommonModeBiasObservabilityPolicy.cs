using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaBiasArcObservation(
        string ArcId,
        string MeasurementDigest,
        TppaQualificationVector EstimatedMountAxis,
        bool AxisFitQualified,
        double AxisUncertaintyBoundArcMinutes,
        double CenterHourAngleDegrees,
        double CenterDeclinationDegrees,
        string PierSide,
        DateTime ObservationUtc,
        string HardwareConfigurationId,
        string MechanicalStateDigest,
        string SiteIdentity,
        string EnvironmentEnvelopeId,
        string CoordinateFrame,
        bool RefractionAdjustmentEnabled,
        double MechanicalCameraRotationDegrees);

    internal sealed record TppaWitnessAxisObservation(
        string WitnessId,
        string MeasurementDigest,
        string QualificationReceiptDigest,
        string InstrumentId,
        string DetectorSerial,
        string OpticalTrainId,
        string SolverId,
        /// <summary>Digest of the immutable witness pipeline configuration, not image data.</summary>
        string PipelineConfigurationDigest,
        bool Qualified,
        TppaQualificationVector EstimatedMountAxis,
        double AxisUncertaintyBoundArcMinutes,
        string PierSide,
        DateTime ObservationUtc,
        string HardwareConfigurationId,
        string MechanicalStateDigest,
        string SiteIdentity,
        string EnvironmentEnvelopeId,
        string CoordinateFrame,
        bool RefractionAdjustmentEnabled);

    /// <summary>
    /// Mount-axis vectors are topocentric North-West-Up unit vectors. The
    /// coordinate-frame token describes the astrometric sky observations, not
    /// the vector component basis.
    /// </summary>
    internal sealed record TppaCommonModeBiasEvidence(
        string TppaInstrumentId,
        string TppaDetectorSerial,
        string TppaOpticalTrainId,
        string TppaSolverId,
        /// <summary>Digest of the immutable TPPA pipeline configuration, not image data.</summary>
        string TppaPipelineConfigurationDigest,
        string BasisConventionId,
        TppaQualificationVector LocalNorth,
        TppaQualificationVector LocalWest,
        TppaQualificationVector LocalUp,
        double SiteLatitudeDegrees,
        string CelestialPoleDirection,
        IReadOnlyList<TppaWitnessAxisObservation> Witnesses,
        IReadOnlyList<TppaBiasArcObservation> Arcs);

    internal sealed record TppaCommonModeBiasPolicy(
        int MinimumArcCount = 8,
        int MinimumArcsPerPierSide = 4,
        int MinimumWitnessCount = 2,
        int MinimumWitnessesPerPierSide = 1,
        int MinimumDistinctPointingCount = 4,
        double MinimumDistinctPointingSeparationDegrees = 5.0,
        double MinimumHourAngleSpanDegrees = 20.0,
        double MinimumDeclinationSpanDegrees = 10.0,
        double MinimumGeometryEigenvalue = 0.02,
        double MaximumGeometryConditionNumber = 25.0,
        double MaximumArcAxisUncertaintyArcMinutes = 0.15,
        double MaximumWitnessUncertaintyArcMinutes = 0.25,
        double MaximumCalibrationUncertaintyArcMinutes = 0.5,
        double MaximumBiasMagnitudeArcMinutes = 5.0,
        double MaximumPierOddBiasArcMinutes = 0.25,
        double MaximumCameraRotationSpreadDegrees = 0.1,
        double MaximumCrossPierPointingMismatchDegrees = 1.0,
        double MaximumCampaignDurationSeconds = 7200.0,
        double MinimumArcSeparationSeconds = 30.0,
        double MaximumWitnessEndpointOffsetSeconds = 300.0,
        double MinimumTangentRadiusDegrees = 5.0);

    internal sealed record TppaCommonModeBiasObservabilityResult(
        bool IsIdentifiable,
        TppaCommonModeBiasPolicy ActivePolicy,
        double BiasWestwardSkyComponentArcMinutes,
        double BiasZenithwardSkyComponentArcMinutes,
        double BiasMagnitudeArcMinutes,
        double EastPierBiasWestwardArcMinutes,
        double EastPierBiasZenithwardArcMinutes,
        double WestPierBiasWestwardArcMinutes,
        double WestPierBiasZenithwardArcMinutes,
        double PierOddBiasArcMinutes,
        double InSampleFieldVariationArcMinutes,
        double MaximumArcAxisUncertaintyArcMinutes,
        double WitnessUncertaintyArcMinutes,
        double WitnessObservedSpreadArcMinutes,
        double WitnessPierOddBiasArcMinutes,
        double ConservativeCalibrationUncertaintyArcMinutes,
        double HourAngleSpanDegrees,
        double DeclinationSpanDegrees,
        double ArcGeometryConditionNumber,
        int ArcCount,
        int DistinctPointingCount,
        int EastPierArcCount,
        int WestPierArcCount,
        int WitnessCount,
        int EastPierWitnessCount,
        int WestPierWitnessCount,
        DateTime CampaignStartedUtc,
        DateTime CampaignEndedUtc,
        string HardwareConfigurationId,
        string MechanicalStateDigest,
        string SiteIdentity,
        string EnvironmentEnvelopeId,
        string CoordinateFrame,
        string BasisConventionId,
        IReadOnlyList<string> Issues) {
        public bool GrantsMotionAuthority => false;
        public bool GrantsCompletionAuthority => false;
    }

    /// <summary>
    /// Determines whether geometrically distinct TPPA arcs and a balanced,
    /// independent mount-axis witness set make a common-mode bias observable.
    /// This is a slow report-only calibration campaign. It neither creates a
    /// reusable receipt nor authorizes actuator movement or completion.
    /// </summary>
    internal static class TppaCommonModeBiasObservabilityPolicy {
        internal const string TopocentricNwuBasis = "topocentric-nwu-v1";
        internal const string NorthPole = "north";
        internal const string SouthPole = "south";
        private const double NumericalTolerance = 1e-12;
        private const double FrameTolerance = 1e-9;
        private const double ArcMinutesPerRadian = 180.0 / Math.PI * 60.0;
        private const string EastPierSide = "east";
        private const string WestPierSide = "west";

        public static TppaCommonModeBiasObservabilityResult Evaluate(
                TppaCommonModeBiasEvidence evidence,
                TppaCommonModeBiasPolicy policy = null) {
            var activePolicy = policy ?? new TppaCommonModeBiasPolicy();
            ValidatePolicy(activePolicy);
            var issues = new List<string>();
            var arcs = evidence?.Arcs?.ToArray() ?? Array.Empty<TppaBiasArcObservation>();
            var witnesses = evidence?.Witnesses?.ToArray() ?? Array.Empty<TppaWitnessAxisObservation>();
            if (evidence == null) { issues.Add("bias-observability evidence is required"); }
            if (arcs.Length < activePolicy.MinimumArcCount) {
                issues.Add($"at least {activePolicy.MinimumArcCount} qualified arcs are required");
            }
            if (witnesses.Length < activePolicy.MinimumWitnessCount) {
                issues.Add($"at least {activePolicy.MinimumWitnessCount} independent witness observations are required");
            }

            var firstArc = arcs.FirstOrDefault(value => value != null);
            ValidateBasis(evidence, issues, out var localNorth, out var localWest, out var localUp);
            ValidateTppaIdentity(evidence, issues);

            var arcAxes = new List<UnitVector>();
            var arcUncertainties = new List<double>();
            var hourAngles = new List<double>();
            var declinations = new List<double>();
            var arcTimes = new List<DateTime>();
            var rotations = new List<double>();
            var arcPierSides = new List<string>();
            var arcIds = new HashSet<string>(StringComparer.Ordinal);
            var arcDigests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < arcs.Length; index++) {
                var arc = arcs[index];
                var label = $"arc {index + 1}";
                if (arc == null) { issues.Add($"{label} is missing"); continue; }
                if (string.IsNullOrWhiteSpace(arc.ArcId) || !arcIds.Add(arc.ArcId)) {
                    issues.Add($"{label} has a missing or duplicate arc ID");
                }
                if (!IsSha256(arc.MeasurementDigest) || !arcDigests.Add(arc.MeasurementDigest)) {
                    issues.Add($"{label} has a missing, invalid, or duplicate measurement digest");
                }
                if (!arc.AxisFitQualified) { issues.Add($"{label} axis fit is not qualified"); }
                if (!IsFiniteNonNegative(arc.AxisUncertaintyBoundArcMinutes)
                        || arc.AxisUncertaintyBoundArcMinutes > activePolicy.MaximumArcAxisUncertaintyArcMinutes) {
                    issues.Add($"{label} axis uncertainty exceeds the calibration policy");
                } else { arcUncertainties.Add(arc.AxisUncertaintyBoundArcMinutes); }
                if (!double.IsFinite(arc.CenterHourAngleDegrees)
                        || arc.CenterHourAngleDegrees < -180 || arc.CenterHourAngleDegrees > 180
                        || !double.IsFinite(arc.CenterDeclinationDegrees)
                        || arc.CenterDeclinationDegrees < -90 || arc.CenterDeclinationDegrees > 90) {
                    issues.Add($"{label} geometry coordinates are invalid");
                } else {
                    hourAngles.Add(arc.CenterHourAngleDegrees);
                    declinations.Add(arc.CenterDeclinationDegrees);
                }
                var pierSide = NormalizePierSide(arc.PierSide);
                if (pierSide == null) { issues.Add($"{label} pier side must be east or west"); }
                else { arcPierSides.Add(pierSide); }
                if (arc.ObservationUtc.Kind != DateTimeKind.Utc) {
                    issues.Add($"{label} observation time is not UTC");
                } else { arcTimes.Add(arc.ObservationUtc); }
                if (!double.IsFinite(arc.MechanicalCameraRotationDegrees)) {
                    issues.Add($"{label} mechanical camera rotation is non-finite");
                } else { rotations.Add(NormalizeDegrees(arc.MechanicalCameraRotationDegrees)); }
                if (!arc.RefractionAdjustmentEnabled) {
                    issues.Add($"{label} did not target the true pole with refraction adjustment enabled");
                }
                ValidateArcEpoch(firstArc, arc, label, issues);
                if (!TryUnit(arc.EstimatedMountAxis, out var axis)) {
                    issues.Add($"{label} estimated mount-axis vector is missing, non-finite, or zero length");
                } else { arcAxes.Add(axis); }
            }

            var campaignStart = arcTimes.Count > 0 ? arcTimes.Min() : default;
            var campaignEnd = arcTimes.Count > 0 ? arcTimes.Max() : default;
            ValidateCampaignTiming(arcTimes, campaignStart, campaignEnd, activePolicy, issues);
            var witnessAxes = ValidateWitnesses(
                evidence, witnesses, firstArc, arcDigests, campaignStart, campaignEnd,
                activePolicy, issues, out var witnessStatedUncertainty,
                out var witnessPierSides, out var witnessTimes);
            ValidateWitnessEndpointCoverage(
                witnessTimes, campaignStart, campaignEnd, activePolicy, issues);
            var witnessAxisValid = TryBalancedMeanAxis(
                witnessAxes, witnessPierSides, out var witnessAxis,
                out var witnessPierOddBias);
            if (!witnessAxisValid) {
                issues.Add("independent witness set does not yield one pier-balanced finite mount-axis vector");
            }
            var witnessSpread = witnessAxisValid
                ? witnessAxes.Select(value => AngularSeparationArcMinutes(witnessAxis, value)).DefaultIfEmpty(double.PositiveInfinity).Max()
                : double.PositiveInfinity;
            var witnessUncertainty = Math.Max(witnessStatedUncertainty, witnessSpread);
            if (!double.IsFinite(witnessUncertainty)
                    || witnessUncertainty > activePolicy.MaximumWitnessUncertaintyArcMinutes) {
                issues.Add($"independent witness observed or stated uncertainty {witnessUncertainty:F3} arcmin exceeds policy");
            }

            var westwardTangent = default(UnitVector);
            var zenithwardTangent = default(UnitVector);
            var tangentFrameValid = witnessAxisValid
                && ValidatePoleAndBuildTangentFrame(
                    evidence, witnessAxis, localNorth, localWest, localUp,
                    activePolicy, issues, out westwardTangent, out zenithwardTangent);
            var arcVectors = new List<(double Westward, double Zenithward, string PierSide)>();
            if (tangentFrameValid
                    && arcAxes.Count == arcs.Length
                    && arcPierSides.Count == arcs.Length) {
                for (var index = 0; index < arcAxes.Count; index++) {
                    var arcAxis = arcAxes[index];
                    if (Dot(witnessAxis, arcAxis) <= 0) {
                        issues.Add("a TPPA mount-axis polarity disagrees with the independent witness");
                        continue;
                    }
                    var bias = ToTangentBias(
                        witnessAxis, arcAxis, westwardTangent, zenithwardTangent);
                    arcVectors.Add((bias.Westward, bias.Zenithward, arcPierSides[index]));
                }
            }

            var eastArcCount = arcPierSides.Count(value => value == EastPierSide);
            var westArcCount = arcPierSides.Count(value => value == WestPierSide);
            if (eastArcCount < activePolicy.MinimumArcsPerPierSide
                    || westArcCount < activePolicy.MinimumArcsPerPierSide) {
                issues.Add($"both pier sides require at least {activePolicy.MinimumArcsPerPierSide} arcs (east={eastArcCount}, west={westArcCount})");
            }
            if (eastArcCount != westArcCount) {
                issues.Add($"TPPA arc counts must be exactly pier balanced (east={eastArcCount}, west={westArcCount})");
            }
            var eastWitnessCount = witnessPierSides.Count(value => value == EastPierSide);
            var westWitnessCount = witnessPierSides.Count(value => value == WestPierSide);
            if (eastWitnessCount < activePolicy.MinimumWitnessesPerPierSide
                    || westWitnessCount < activePolicy.MinimumWitnessesPerPierSide) {
                issues.Add($"both pier sides require at least {activePolicy.MinimumWitnessesPerPierSide} witness observations (east={eastWitnessCount}, west={westWitnessCount})");
            }
            if (eastWitnessCount != westWitnessCount) {
                issues.Add($"independent witness counts must be exactly pier balanced (east={eastWitnessCount}, west={westWitnessCount})");
            }
            ValidateCrossPierPointingMatches(arcs, activePolicy, issues);

            var unwrappedHourAngles = UnwrapMinimalCircularArc(hourAngles, out var hourAngleSpan);
            var declinationSpan = Span(declinations);
            if (hourAngleSpan < activePolicy.MinimumHourAngleSpanDegrees) {
                issues.Add($"hour-angle span {hourAngleSpan:F3} deg is below {activePolicy.MinimumHourAngleSpanDegrees:F3} deg");
            }
            if (declinationSpan < activePolicy.MinimumDeclinationSpanDegrees) {
                issues.Add($"declination span {declinationSpan:F3} deg is below {activePolicy.MinimumDeclinationSpanDegrees:F3} deg");
            }
            var distinctPointingCount = CountDistinctPointingClusters(
                hourAngles, declinations, activePolicy.MinimumDistinctPointingSeparationDegrees);
            if (distinctPointingCount < activePolicy.MinimumDistinctPointingCount) {
                issues.Add($"only {distinctPointingCount} distinct TPPA pointings were observed; {activePolicy.MinimumDistinctPointingCount} are required");
            }
            var geometryCondition = GeometryConditionNumber(
                unwrappedHourAngles, declinations, hourAngleSpan, declinationSpan,
                out var smallestGeometryEigenvalue);
            if (!double.IsFinite(geometryCondition)
                    || geometryCondition > activePolicy.MaximumGeometryConditionNumber
                    || smallestGeometryEigenvalue < activePolicy.MinimumGeometryEigenvalue) {
                issues.Add($"arc geometry is rank deficient or ill-conditioned (condition={geometryCondition:F3}, smallest eigenvalue={smallestGeometryEigenvalue:F4})");
            }
            var rotationSpread = MinimalCircularSpan(rotations);
            if (rotationSpread > activePolicy.MaximumCameraRotationSpreadDegrees) {
                issues.Add($"mechanical camera rotation spread {rotationSpread:F3} deg exceeds {activePolicy.MaximumCameraRotationSpreadDegrees:F3} deg");
            }

            var eastVectors = arcVectors.Where(value => value.PierSide == EastPierSide).ToArray();
            var westVectors = arcVectors.Where(value => value.PierSide == WestPierSide).ToArray();
            var pierMeansValid = arcVectors.Count == arcs.Length
                && eastVectors.Length > 0 && westVectors.Length > 0;
            var eastMeanWestward = pierMeansValid
                ? eastVectors.Average(value => value.Westward) : double.NaN;
            var eastMeanZenithward = pierMeansValid
                ? eastVectors.Average(value => value.Zenithward) : double.NaN;
            var westMeanWestward = pierMeansValid
                ? westVectors.Average(value => value.Westward) : double.NaN;
            var westMeanZenithward = pierMeansValid
                ? westVectors.Average(value => value.Zenithward) : double.NaN;
            var meanWestward = pierMeansValid
                ? (eastMeanWestward + westMeanWestward) / 2.0 : double.NaN;
            var meanZenithward = pierMeansValid
                ? (eastMeanZenithward + westMeanZenithward) / 2.0 : double.NaN;
            var biasMagnitude = double.IsFinite(meanWestward) && double.IsFinite(meanZenithward)
                ? Math.Sqrt(Square(meanWestward) + Square(meanZenithward))
                : double.PositiveInfinity;
            var pierOddBias = pierMeansValid
                ? Math.Sqrt(
                    Square((eastMeanWestward - westMeanWestward) / 2.0)
                    + Square((eastMeanZenithward - westMeanZenithward) / 2.0))
                : double.PositiveInfinity;
            var inSampleVariation = pierMeansValid
                ? arcVectors.Max(value => {
                    var pierMeanWestward = value.PierSide == EastPierSide
                        ? eastMeanWestward : westMeanWestward;
                    var pierMeanZenithward = value.PierSide == EastPierSide
                        ? eastMeanZenithward : westMeanZenithward;
                    return Math.Sqrt(
                        Square(value.Westward - pierMeanWestward)
                        + Square(value.Zenithward - pierMeanZenithward));
                })
                : double.PositiveInfinity;
            var maximumArcUncertainty = arcUncertainties.Count == arcs.Length && arcs.Length > 0
                ? arcUncertainties.Max() : double.PositiveInfinity;
            var conservativeUncertainty = witnessUncertainty + maximumArcUncertainty
                + inSampleVariation + pierOddBias;
            if (!double.IsFinite(biasMagnitude) || biasMagnitude > activePolicy.MaximumBiasMagnitudeArcMinutes) {
                issues.Add($"common-mode bias magnitude {biasMagnitude:F3} arcmin exceeds {activePolicy.MaximumBiasMagnitudeArcMinutes:F3} arcmin");
            }
            if (!double.IsFinite(pierOddBias)
                    || pierOddBias > activePolicy.MaximumPierOddBiasArcMinutes) {
                issues.Add($"pier-odd detector-frame bias {pierOddBias:F3} arcmin exceeds {activePolicy.MaximumPierOddBiasArcMinutes:F3} arcmin");
            }
            if (!double.IsFinite(conservativeUncertainty)
                    || conservativeUncertainty > activePolicy.MaximumCalibrationUncertaintyArcMinutes) {
                issues.Add($"conservative calibration uncertainty {conservativeUncertainty:F3} arcmin exceeds {activePolicy.MaximumCalibrationUncertaintyArcMinutes:F3} arcmin");
            }

            return new(
                IsIdentifiable: issues.Count == 0,
                ActivePolicy: activePolicy,
                BiasWestwardSkyComponentArcMinutes: meanWestward,
                BiasZenithwardSkyComponentArcMinutes: meanZenithward,
                BiasMagnitudeArcMinutes: biasMagnitude,
                EastPierBiasWestwardArcMinutes: eastMeanWestward,
                EastPierBiasZenithwardArcMinutes: eastMeanZenithward,
                WestPierBiasWestwardArcMinutes: westMeanWestward,
                WestPierBiasZenithwardArcMinutes: westMeanZenithward,
                PierOddBiasArcMinutes: pierOddBias,
                InSampleFieldVariationArcMinutes: inSampleVariation,
                MaximumArcAxisUncertaintyArcMinutes: maximumArcUncertainty,
                WitnessUncertaintyArcMinutes: witnessUncertainty,
                WitnessObservedSpreadArcMinutes: witnessSpread,
                WitnessPierOddBiasArcMinutes: witnessPierOddBias,
                ConservativeCalibrationUncertaintyArcMinutes: conservativeUncertainty,
                HourAngleSpanDegrees: hourAngleSpan,
                DeclinationSpanDegrees: declinationSpan,
                ArcGeometryConditionNumber: geometryCondition,
                ArcCount: arcs.Length,
                DistinctPointingCount: distinctPointingCount,
                EastPierArcCount: eastArcCount,
                WestPierArcCount: westArcCount,
                WitnessCount: witnesses.Length,
                EastPierWitnessCount: eastWitnessCount,
                WestPierWitnessCount: westWitnessCount,
                CampaignStartedUtc: campaignStart,
                CampaignEndedUtc: campaignEnd,
                HardwareConfigurationId: firstArc?.HardwareConfigurationId,
                MechanicalStateDigest: firstArc?.MechanicalStateDigest,
                SiteIdentity: firstArc?.SiteIdentity,
                EnvironmentEnvelopeId: firstArc?.EnvironmentEnvelopeId,
                CoordinateFrame: firstArc?.CoordinateFrame,
                BasisConventionId: evidence?.BasisConventionId,
                Issues: issues);
        }

        private static IReadOnlyList<UnitVector> ValidateWitnesses(
                TppaCommonModeBiasEvidence evidence,
                IReadOnlyList<TppaWitnessAxisObservation> witnesses,
                TppaBiasArcObservation expectedArc,
                IReadOnlySet<string> arcMeasurementDigests,
                DateTime campaignStart,
                DateTime campaignEnd,
                TppaCommonModeBiasPolicy policy,
                ICollection<string> issues,
                out double maximumStatedUncertainty,
                out IReadOnlyList<string> pierSides,
                out IReadOnlyList<DateTime> observationTimes) {
            var axes = new List<UnitVector>();
            var sides = new List<string>();
            var uncertainties = new List<double>();
            var observationTimesInternal = new List<DateTime>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var measurementDigests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var receiptDigests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var firstWitness = witnesses.FirstOrDefault(value => value != null);
            for (var index = 0; index < witnesses.Count; index++) {
                var witness = witnesses[index];
                var label = $"witness {index + 1}";
                if (witness == null) { issues.Add($"{label} is missing"); continue; }
                if (string.IsNullOrWhiteSpace(witness.WitnessId) || !ids.Add(witness.WitnessId)) {
                    issues.Add($"{label} has a missing or duplicate ID");
                }
                if (!IsSha256(witness.MeasurementDigest) || !measurementDigests.Add(witness.MeasurementDigest)) {
                    issues.Add($"{label} has a missing, invalid, or duplicate measurement digest");
                }
                if (arcMeasurementDigests.Contains(witness.MeasurementDigest)) {
                    issues.Add($"{label} reuses a TPPA arc measurement digest");
                }
                if (!IsSha256(witness.QualificationReceiptDigest) || !receiptDigests.Add(witness.QualificationReceiptDigest)) {
                    issues.Add($"{label} has a missing, invalid, or duplicate qualification receipt digest");
                }
                if (!witness.Qualified) { issues.Add($"{label} is not qualified"); }
                ValidateIndependentIdentity(evidence, firstWitness, witness, label, issues);
                ValidateWitnessEpoch(expectedArc, witness, label, issues);
                var side = NormalizePierSide(witness.PierSide);
                if (side == null) { issues.Add($"{label} pier side must be east or west"); }
                else { sides.Add(side); }
                if (witness.ObservationUtc.Kind != DateTimeKind.Utc) {
                    issues.Add($"{label} observation time is not UTC");
                } else if (campaignStart != default
                        && (witness.ObservationUtc < campaignStart || witness.ObservationUtc > campaignEnd)) {
                    issues.Add($"{label} observation is outside the TPPA calibration campaign window");
                } else { observationTimesInternal.Add(witness.ObservationUtc); }
                if (!IsFiniteNonNegative(witness.AxisUncertaintyBoundArcMinutes)
                        || witness.AxisUncertaintyBoundArcMinutes > policy.MaximumWitnessUncertaintyArcMinutes) {
                    issues.Add($"{label} stated uncertainty exceeds the calibration policy");
                } else { uncertainties.Add(witness.AxisUncertaintyBoundArcMinutes); }
                if (!TryUnit(witness.EstimatedMountAxis, out var axis)) {
                    issues.Add($"{label} mount-axis vector is missing, non-finite, or zero length");
                } else if (axes.Count > 0 && Dot(axes[0], axis) <= 0) {
                    issues.Add($"{label} mount-axis polarity disagrees with the witness set");
                } else { axes.Add(axis); }
            }
            maximumStatedUncertainty = uncertainties.Count == witnesses.Count && witnesses.Count > 0
                ? uncertainties.Max() : double.PositiveInfinity;
            pierSides = sides;
            observationTimes = observationTimesInternal;
            return axes;
        }

        private static void ValidateIndependentIdentity(
                TppaCommonModeBiasEvidence evidence,
                TppaWitnessAxisObservation expected,
                TppaWitnessAxisObservation actual,
                string label,
                ICollection<string> issues) {
            if (evidence == null || expected == null || actual == null) { return; }
            var witnessIdentityInvalid = string.IsNullOrWhiteSpace(actual.InstrumentId)
                || actual.InstrumentId != expected.InstrumentId
                || string.IsNullOrWhiteSpace(actual.DetectorSerial)
                || actual.DetectorSerial != expected.DetectorSerial
                || string.IsNullOrWhiteSpace(actual.OpticalTrainId)
                || actual.OpticalTrainId != expected.OpticalTrainId
                || string.IsNullOrWhiteSpace(actual.SolverId)
                || actual.SolverId != expected.SolverId
                || !IsSha256(actual.PipelineConfigurationDigest)
                || !DigestEquals(actual.PipelineConfigurationDigest, expected.PipelineConfigurationDigest);
            var aliasedWithTppa = actual.InstrumentId == evidence.TppaInstrumentId
                || actual.DetectorSerial == evidence.TppaDetectorSerial
                || actual.OpticalTrainId == evidence.TppaOpticalTrainId
                || actual.SolverId == evidence.TppaSolverId
                || DigestEquals(actual.PipelineConfigurationDigest, evidence.TppaPipelineConfigurationDigest);
            if (witnessIdentityInvalid || aliasedWithTppa) {
                issues.Add($"{label} witness identity is inconsistent or not independent of TPPA");
            }
        }

        private static void ValidateWitnessEpoch(
                TppaBiasArcObservation expected,
                TppaWitnessAxisObservation actual,
                string label,
                ICollection<string> issues) {
            if (expected == null || actual == null) { return; }
            if (string.IsNullOrWhiteSpace(actual.HardwareConfigurationId)
                    || actual.HardwareConfigurationId != expected.HardwareConfigurationId
                    || !IsSha256(actual.MechanicalStateDigest)
                    || !DigestEquals(actual.MechanicalStateDigest, expected.MechanicalStateDigest)
                    || string.IsNullOrWhiteSpace(actual.SiteIdentity)
                    || actual.SiteIdentity != expected.SiteIdentity
                    || string.IsNullOrWhiteSpace(actual.EnvironmentEnvelopeId)
                    || actual.EnvironmentEnvelopeId != expected.EnvironmentEnvelopeId
                    || actual.CoordinateFrame != TppaFastQualificationConventions.IcrsObservationEpoch
                    || actual.CoordinateFrame != expected.CoordinateFrame
                    || !actual.RefractionAdjustmentEnabled) {
                issues.Add($"{label} does not share the TPPA hardware, mechanical, site, environment, true-pole, and coordinate-frame epoch");
            }
        }

        private static void ValidateArcEpoch(
                TppaBiasArcObservation expected,
                TppaBiasArcObservation actual,
                string label,
                ICollection<string> issues) {
            if (expected == null || actual == null) { return; }
            if (string.IsNullOrWhiteSpace(actual.HardwareConfigurationId)
                    || actual.HardwareConfigurationId != expected.HardwareConfigurationId
                    || !IsSha256(actual.MechanicalStateDigest)
                    || !DigestEquals(actual.MechanicalStateDigest, expected.MechanicalStateDigest)
                    || string.IsNullOrWhiteSpace(actual.SiteIdentity)
                    || actual.SiteIdentity != expected.SiteIdentity
                    || string.IsNullOrWhiteSpace(actual.EnvironmentEnvelopeId)
                    || actual.EnvironmentEnvelopeId != expected.EnvironmentEnvelopeId
                    || actual.CoordinateFrame != TppaFastQualificationConventions.IcrsObservationEpoch
                    || actual.CoordinateFrame != expected.CoordinateFrame) {
                issues.Add($"{label} does not share one qualified hardware, mechanical, site, environment, and coordinate-frame epoch");
            }
        }

        private static void ValidateTppaIdentity(
                TppaCommonModeBiasEvidence evidence,
                ICollection<string> issues) {
            if (string.IsNullOrWhiteSpace(evidence?.TppaInstrumentId)
                    || string.IsNullOrWhiteSpace(evidence?.TppaDetectorSerial)
                    || string.IsNullOrWhiteSpace(evidence?.TppaOpticalTrainId)
                    || string.IsNullOrWhiteSpace(evidence?.TppaSolverId)
                    || !IsSha256(evidence?.TppaPipelineConfigurationDigest)) {
                issues.Add("TPPA instrument, detector, optical-train, solver, and pipeline-configuration identities are required");
            }
        }

        private static void ValidateBasis(
                TppaCommonModeBiasEvidence evidence,
                ICollection<string> issues,
                out UnitVector north,
                out UnitVector west,
                out UnitVector up) {
            var northValid = TryUnit(evidence?.LocalNorth, out north);
            var westValid = TryUnit(evidence?.LocalWest, out west);
            var upValid = TryUnit(evidence?.LocalUp, out up);
            if (evidence?.BasisConventionId != TopocentricNwuBasis
                    || !northValid || !westValid || !upValid
                    || !IsApproximatelyUnit(evidence.LocalNorth)
                    || !IsApproximatelyUnit(evidence.LocalWest)
                    || !IsApproximatelyUnit(evidence.LocalUp)
                    || Math.Abs(Dot(north, west)) > FrameTolerance
                    || Math.Abs(Dot(north, up)) > FrameTolerance
                    || Math.Abs(Dot(west, up)) > FrameTolerance
                    || Distance(Cross(up, north), west) > FrameTolerance) {
                issues.Add("mount-axis vector basis is not a proven right-handed topocentric North-West-Up frame");
            }
        }

        private static bool ValidatePoleAndBuildTangentFrame(
                TppaCommonModeBiasEvidence evidence,
                UnitVector axis,
                UnitVector north,
                UnitVector west,
                UnitVector up,
                TppaCommonModeBiasPolicy policy,
                ICollection<string> issues,
                out UnitVector westwardTangent,
                out UnitVector zenithwardTangent) {
            westwardTangent = default;
            zenithwardTangent = default;
            if (evidence == null || !double.IsFinite(evidence.SiteLatitudeDegrees)
                    || evidence.SiteLatitudeDegrees < -90 || evidence.SiteLatitudeDegrees > 90
                    || (evidence.CelestialPoleDirection != NorthPole
                        && evidence.CelestialPoleDirection != SouthPole)) {
                issues.Add("site latitude and celestial-pole direction are required for the topocentric basis");
                return false;
            }
            var latitude = evidence.SiteLatitudeDegrees * Math.PI / 180.0;
            var expectedPole = Add(
                Scale(north, Math.Cos(latitude)),
                Scale(up, Math.Sin(latitude)));
            if (evidence.CelestialPoleDirection == SouthPole) {
                expectedPole = Scale(expectedPole, -1);
            }
            var plausibility = AngularSeparationDegrees(axis, expectedPole);
            if (plausibility > policy.MaximumBiasMagnitudeArcMinutes / 60.0 + 0.5) {
                issues.Add($"independent witness axis is inconsistent with the declared site latitude and pole direction by {plausibility:F3} deg");
            }
            var tangentRadius = Math.Acos(Math.Clamp(Math.Abs(Dot(axis, up)), -1.0, 1.0)) * 180.0 / Math.PI;
            if (tangentRadius < policy.MinimumTangentRadiusDegrees) {
                issues.Add($"mount axis is within {policy.MinimumTangentRadiusDegrees:F1} deg of the topocentric zenith/nadir singularity");
                return false;
            }
            if (!TryUnit(Cross(up, axis), out westwardTangent)) {
                issues.Add("unable to construct a stable westward tangent from the declared NWU basis");
                return false;
            }
            if (Dot(westwardTangent, west) < 0) {
                westwardTangent = Scale(westwardTangent, -1);
            }
            if (!TryUnit(Subtract(up, Scale(axis, Dot(up, axis))), out zenithwardTangent)) {
                issues.Add("unable to construct a stable westward/zenithward tangent frame from the declared NWU basis");
                return false;
            }
            return true;
        }

        private static void ValidateCampaignTiming(
                IReadOnlyList<DateTime> times,
                DateTime start,
                DateTime end,
                TppaCommonModeBiasPolicy policy,
                ICollection<string> issues) {
            if (times.Count <= 1) { return; }
            var duration = (end - start).TotalSeconds;
            if (duration > policy.MaximumCampaignDurationSeconds) {
                issues.Add($"calibration campaign duration {duration:F1}s exceeds {policy.MaximumCampaignDurationSeconds:F1}s");
            }
            var ordered = times.OrderBy(value => value).ToArray();
            for (var index = 1; index < ordered.Length; index++) {
                var separation = (ordered[index] - ordered[index - 1]).TotalSeconds;
                if (separation < policy.MinimumArcSeparationSeconds) {
                    issues.Add($"TPPA arc observations are separated by only {separation:F1}s");
                    break;
                }
            }
        }

        private static void ValidateWitnessEndpointCoverage(
                IReadOnlyList<DateTime> witnessTimes,
                DateTime campaignStart,
                DateTime campaignEnd,
                TppaCommonModeBiasPolicy policy,
                ICollection<string> issues) {
            if (witnessTimes.Count == 0 || campaignStart == default || campaignEnd == default) { return; }
            var firstOffset = (witnessTimes.Min() - campaignStart).TotalSeconds;
            var lastOffset = (campaignEnd - witnessTimes.Max()).TotalSeconds;
            if (firstOffset > policy.MaximumWitnessEndpointOffsetSeconds
                    || lastOffset > policy.MaximumWitnessEndpointOffsetSeconds) {
                issues.Add($"independent witnesses do not cover both TPPA campaign endpoints within {policy.MaximumWitnessEndpointOffsetSeconds:F1}s");
            }
        }

        private static void ValidateCrossPierPointingMatches(
                IReadOnlyList<TppaBiasArcObservation> arcs,
                TppaCommonModeBiasPolicy policy,
                ICollection<string> issues) {
            var valid = arcs.Where(value => value != null
                && NormalizePierSide(value.PierSide) != null
                && double.IsFinite(value.CenterHourAngleDegrees)
                && double.IsFinite(value.CenterDeclinationDegrees)).ToArray();
            foreach (var arc in valid) {
                var side = NormalizePierSide(arc.PierSide);
                var opposite = side == EastPierSide ? WestPierSide : EastPierSide;
                var best = valid
                    .Where(value => NormalizePierSide(value.PierSide) == opposite)
                    .Select(value => PointingSeparationDegrees(arc, value))
                    .DefaultIfEmpty(double.PositiveInfinity)
                    .Min();
                if (best > policy.MaximumCrossPierPointingMismatchDegrees) {
                    issues.Add($"arc {arc.ArcId} has no matched opposite-pier pointing within {policy.MaximumCrossPierPointingMismatchDegrees:F3} deg");
                }
            }
        }

        private static double PointingSeparationDegrees(
                TppaBiasArcObservation left,
                TppaBiasArcObservation right) {
            var leftHa = left.CenterHourAngleDegrees * Math.PI / 180.0;
            var rightHa = right.CenterHourAngleDegrees * Math.PI / 180.0;
            var leftDec = left.CenterDeclinationDegrees * Math.PI / 180.0;
            var rightDec = right.CenterDeclinationDegrees * Math.PI / 180.0;
            var cosine = Math.Sin(leftDec) * Math.Sin(rightDec)
                + Math.Cos(leftDec) * Math.Cos(rightDec) * Math.Cos(leftHa - rightHa);
            return Math.Acos(Math.Clamp(cosine, -1.0, 1.0)) * 180.0 / Math.PI;
        }

        private static (double Westward, double Zenithward) ToTangentBias(
                UnitVector witness,
                UnitVector estimate,
                UnitVector westwardTangent,
                UnitVector zenithwardTangent) {
            var cosine = Math.Clamp(Dot(witness, estimate), -1.0, 1.0);
            var angle = Math.Acos(cosine);
            if (angle <= NumericalTolerance) { return (0, 0); }
            var direction = Scale(
                Subtract(estimate, Scale(witness, cosine)),
                1.0 / Math.Sin(angle));
            return (
                angle * Dot(direction, westwardTangent) * ArcMinutesPerRadian,
                angle * Dot(direction, zenithwardTangent) * ArcMinutesPerRadian);
        }

        private static int CountDistinctPointingClusters(
                IReadOnlyList<double> hourAngles,
                IReadOnlyList<double> declinations,
                double thresholdDegrees) {
            if (hourAngles.Count == 0 || hourAngles.Count != declinations.Count) { return 0; }
            var parent = Enumerable.Range(0, hourAngles.Count).ToArray();
            int Find(int value) {
                while (parent[value] != value) {
                    parent[value] = parent[parent[value]];
                    value = parent[value];
                }
                return value;
            }
            void Union(int first, int second) {
                var a = Find(first); var b = Find(second);
                if (a != b) { parent[b] = a; }
            }
            for (var first = 0; first < hourAngles.Count; first++) {
                for (var second = first + 1; second < hourAngles.Count; second++) {
                    if (SkySeparationDegrees(
                            hourAngles[first], declinations[first],
                            hourAngles[second], declinations[second]) < thresholdDegrees) {
                        Union(first, second);
                    }
                }
            }
            return Enumerable.Range(0, parent.Length).Select(Find).Distinct().Count();
        }

        private static double SkySeparationDegrees(
                double firstHourAngle,
                double firstDeclination,
                double secondHourAngle,
                double secondDeclination) {
            var firstDec = firstDeclination * Math.PI / 180.0;
            var secondDec = secondDeclination * Math.PI / 180.0;
            var deltaHa = CircularDeltaDegrees(firstHourAngle, secondHourAngle) * Math.PI / 180.0;
            var cosine = Math.Sin(firstDec) * Math.Sin(secondDec)
                + Math.Cos(firstDec) * Math.Cos(secondDec) * Math.Cos(deltaHa);
            return Math.Acos(Math.Clamp(cosine, -1.0, 1.0)) * 180.0 / Math.PI;
        }

        private static IReadOnlyList<double> UnwrapMinimalCircularArc(
                IReadOnlyList<double> degrees,
                out double span) {
            span = 0;
            if (degrees.Count == 0) { return Array.Empty<double>(); }
            var normalized = degrees.Select(NormalizeDegrees).OrderBy(value => value).ToArray();
            var largestGap = -1.0;
            var start = normalized[0];
            for (var index = 0; index < normalized.Length; index++) {
                var next = index + 1 < normalized.Length ? normalized[index + 1] : normalized[0] + 360.0;
                var gap = next - normalized[index];
                if (gap > largestGap) { largestGap = gap; start = NormalizeDegrees(next); }
            }
            var unwrapped = degrees.Select(value => {
                var normalizedValue = NormalizeDegrees(value);
                return normalizedValue < start ? normalizedValue + 360.0 : normalizedValue;
            }).ToArray();
            span = Span(unwrapped);
            return unwrapped;
        }

        private static double MinimalCircularSpan(IReadOnlyList<double> degrees) {
            UnwrapMinimalCircularArc(degrees, out var span);
            return span;
        }

        private static double GeometryConditionNumber(
                IReadOnlyList<double> hourAngles,
                IReadOnlyList<double> declinations,
                double hourAngleSpan,
                double declinationSpan,
                out double smallestEigenvalue) {
            smallestEigenvalue = 0;
            if (hourAngles.Count < 2 || hourAngles.Count != declinations.Count
                    || hourAngleSpan <= NumericalTolerance || declinationSpan <= NumericalTolerance) {
                return double.PositiveInfinity;
            }
            var meanHourAngle = hourAngles.Average();
            var meanDeclination = declinations.Average();
            var xx = 0.0; var xy = 0.0; var yy = 0.0;
            for (var index = 0; index < hourAngles.Count; index++) {
                var x = (hourAngles[index] - meanHourAngle) / hourAngleSpan;
                var y = (declinations[index] - meanDeclination) / declinationSpan;
                xx += x * x; xy += x * y; yy += y * y;
            }
            xx /= hourAngles.Count; xy /= hourAngles.Count; yy /= hourAngles.Count;
            var trace = xx + yy;
            var discriminant = Math.Sqrt(Math.Max(0, Square(xx - yy) + 4 * xy * xy));
            var largest = (trace + discriminant) / 2.0;
            smallestEigenvalue = (trace - discriminant) / 2.0;
            return smallestEigenvalue > NumericalTolerance ? largest / smallestEigenvalue : double.PositiveInfinity;
        }

        private static bool TryMeanAxis(IReadOnlyList<UnitVector> axes, out UnitVector mean) {
            if (axes.Count == 0) { mean = default; return false; }
            var sum = axes.Aggregate(new UnitVector(0, 0, 0), Add);
            return TryUnit(sum, out mean);
        }

        private static bool TryBalancedMeanAxis(
                IReadOnlyList<UnitVector> axes,
                IReadOnlyList<string> pierSides,
                out UnitVector mean,
                out double pierOddBiasArcMinutes) {
            mean = default;
            pierOddBiasArcMinutes = double.PositiveInfinity;
            if (axes.Count == 0 || axes.Count != pierSides.Count) { return false; }
            var east = axes.Where((_, index) => pierSides[index] == EastPierSide).ToArray();
            var west = axes.Where((_, index) => pierSides[index] == WestPierSide).ToArray();
            if (east.Length == 0 || west.Length == 0
                    || east.Length != west.Length
                    || !TryMeanAxis(east, out var eastMean)
                    || !TryMeanAxis(west, out var westMean)
                    || Dot(eastMean, westMean) <= 0
                    || !TryUnit(Add(eastMean, westMean), out mean)) {
                return false;
            }
            pierOddBiasArcMinutes = AngularSeparationArcMinutes(eastMean, westMean) / 2.0;
            return true;
        }

        private static double AngularSeparationArcMinutes(UnitVector first, UnitVector second) =>
            AngularSeparationDegrees(first, second) * 60.0;

        private static double AngularSeparationDegrees(UnitVector first, UnitVector second) =>
            Math.Acos(Math.Clamp(Dot(first, second), -1.0, 1.0)) * 180.0 / Math.PI;

        private static double Span(IReadOnlyList<double> values) =>
            values.Count > 0 ? values.Max() - values.Min() : 0;

        private static string NormalizePierSide(string value) {
            if (string.Equals(value, EastPierSide, StringComparison.OrdinalIgnoreCase)) { return EastPierSide; }
            if (string.Equals(value, WestPierSide, StringComparison.OrdinalIgnoreCase)) { return WestPierSide; }
            return null;
        }

        private static double CircularDeltaDegrees(double first, double second) {
            var delta = NormalizeDegrees(second) - NormalizeDegrees(first);
            if (delta > 180) { delta -= 360; }
            if (delta < -180) { delta += 360; }
            return delta;
        }

        private static double NormalizeDegrees(double degrees) {
            var normalized = degrees % 360.0;
            return normalized < 0 ? normalized + 360.0 : normalized;
        }

        private static bool TryUnit(TppaQualificationVector value, out UnitVector unit) =>
            TryUnit(value == null ? default : new UnitVector(value.X, value.Y, value.Z), out unit);

        private static bool TryUnit(UnitVector value, out UnitVector unit) {
            var length = Math.Sqrt(Dot(value, value));
            if (!double.IsFinite(length) || length <= NumericalTolerance) {
                unit = default; return false;
            }
            unit = Scale(value, 1.0 / length); return true;
        }

        private static bool IsApproximatelyUnit(TppaQualificationVector value) {
            if (value == null) { return false; }
            var length = Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
            return double.IsFinite(length) && Math.Abs(length - 1.0) <= FrameTolerance;
        }

        private static UnitVector Cross(UnitVector first, UnitVector second) => new(
            first.Y * second.Z - first.Z * second.Y,
            first.Z * second.X - first.X * second.Z,
            first.X * second.Y - first.Y * second.X);
        private static UnitVector Add(UnitVector first, UnitVector second) =>
            new(first.X + second.X, first.Y + second.Y, first.Z + second.Z);
        private static UnitVector Subtract(UnitVector first, UnitVector second) =>
            new(first.X - second.X, first.Y - second.Y, first.Z - second.Z);
        private static UnitVector Scale(UnitVector value, double scale) =>
            new(value.X * scale, value.Y * scale, value.Z * scale);
        private static double Dot(UnitVector first, UnitVector second) =>
            first.X * second.X + first.Y * second.Y + first.Z * second.Z;
        private static double Distance(UnitVector first, UnitVector second) =>
            Math.Sqrt(Square(first.X - second.X) + Square(first.Y - second.Y) + Square(first.Z - second.Z));
        private static double Square(double value) => value * value;
        private static bool IsFiniteNonNegative(double value) => double.IsFinite(value) && value >= 0;
        private static bool IsFinitePositive(double value) => double.IsFinite(value) && value > 0;
        private static bool IsSha256(string value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
        private static bool DigestEquals(string first, string second) =>
            string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

        private static void ValidatePolicy(TppaCommonModeBiasPolicy policy) {
            var floor = new TppaCommonModeBiasPolicy();
            if (policy.MinimumArcCount < floor.MinimumArcCount
                    || policy.MinimumArcsPerPierSide < floor.MinimumArcsPerPierSide
                    || policy.MinimumWitnessCount < floor.MinimumWitnessCount
                    || policy.MinimumWitnessesPerPierSide < floor.MinimumWitnessesPerPierSide
                    || policy.MinimumDistinctPointingCount < floor.MinimumDistinctPointingCount
                    || policy.MinimumArcCount < 2 * policy.MinimumArcsPerPierSide
                    || policy.MinimumWitnessCount < 2 * policy.MinimumWitnessesPerPierSide
                    || !IsFinitePositive(policy.MinimumDistinctPointingSeparationDegrees)
                    || !IsFinitePositive(policy.MinimumHourAngleSpanDegrees)
                    || !IsFinitePositive(policy.MinimumDeclinationSpanDegrees)
                    || !IsFinitePositive(policy.MinimumGeometryEigenvalue)
                    || !IsFinitePositive(policy.MaximumGeometryConditionNumber)
                    || !IsFiniteNonNegative(policy.MaximumArcAxisUncertaintyArcMinutes)
                    || !IsFiniteNonNegative(policy.MaximumWitnessUncertaintyArcMinutes)
                    || !IsFinitePositive(policy.MaximumCalibrationUncertaintyArcMinutes)
                    || !IsFinitePositive(policy.MaximumBiasMagnitudeArcMinutes)
                    || !IsFiniteNonNegative(policy.MaximumPierOddBiasArcMinutes)
                    || !IsFiniteNonNegative(policy.MaximumCameraRotationSpreadDegrees)
                    || !IsFiniteNonNegative(policy.MaximumCrossPierPointingMismatchDegrees)
                    || !IsFinitePositive(policy.MaximumCampaignDurationSeconds)
                    || !IsFinitePositive(policy.MinimumArcSeparationSeconds)
                    || !IsFiniteNonNegative(policy.MaximumWitnessEndpointOffsetSeconds)
                    || !IsFinitePositive(policy.MinimumTangentRadiusDegrees)
                    || policy.MinimumDistinctPointingSeparationDegrees < floor.MinimumDistinctPointingSeparationDegrees
                    || policy.MinimumHourAngleSpanDegrees < floor.MinimumHourAngleSpanDegrees
                    || policy.MinimumDeclinationSpanDegrees < floor.MinimumDeclinationSpanDegrees
                    || policy.MinimumGeometryEigenvalue < floor.MinimumGeometryEigenvalue
                    || policy.MaximumGeometryConditionNumber > floor.MaximumGeometryConditionNumber
                    || policy.MaximumArcAxisUncertaintyArcMinutes > floor.MaximumArcAxisUncertaintyArcMinutes
                    || policy.MaximumWitnessUncertaintyArcMinutes > floor.MaximumWitnessUncertaintyArcMinutes
                    || policy.MaximumCalibrationUncertaintyArcMinutes > floor.MaximumCalibrationUncertaintyArcMinutes
                    || policy.MaximumBiasMagnitudeArcMinutes > floor.MaximumBiasMagnitudeArcMinutes
                    || policy.MaximumPierOddBiasArcMinutes > floor.MaximumPierOddBiasArcMinutes
                    || policy.MaximumCameraRotationSpreadDegrees > floor.MaximumCameraRotationSpreadDegrees
                    || policy.MaximumCrossPierPointingMismatchDegrees > floor.MaximumCrossPierPointingMismatchDegrees
                    || policy.MaximumCampaignDurationSeconds > floor.MaximumCampaignDurationSeconds
                    || policy.MinimumArcSeparationSeconds < floor.MinimumArcSeparationSeconds
                    || policy.MaximumWitnessEndpointOffsetSeconds > floor.MaximumWitnessEndpointOffsetSeconds
                    || policy.MinimumTangentRadiusDegrees < floor.MinimumTangentRadiusDegrees) {
                throw new ArgumentOutOfRangeException(nameof(policy));
            }
        }

        private readonly record struct UnitVector(double X, double Y, double Z);
    }
}
