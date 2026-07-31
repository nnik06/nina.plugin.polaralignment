using System;
using System.Collections.Generic;
using System.Linq;
using NINA.Plugins.PolarAlignment.Instructions;

namespace NINA.Plugins.PolarAlignment {
    internal static class TppaFastQualificationConventions {
        public const string SphericalVectorSeparationArcMinutes =
            "spherical-vector-separation-arcminutes";
        public const string SphericalPolarErrorMagnitudeArcMinutes =
            "spherical-polar-error-magnitude-arcminutes";
        public const string QualifiedLocalWeatherStation =
            "qualified-local-weather-station";
        public const string IcrsObservationEpoch =
            "icrs-observation-epoch";
    }

    internal sealed record TppaFastQualificationPolicy(
        double MaximumDurationSeconds = 300,
        int MinimumFreshDeterminations = 3,
        double MaximumRepeatabilityDeltaArcMinutes = 0.5,
        double MaximumFinalErrorArcMinutes = 1.0,
        double MaximumIndependentErrorArcMinutes = 0.5,
        double MaximumIndependentDeltaArcMinutes = 0.5,
        double MaximumCombinedAbsoluteErrorArcMinutes = 1.0,
        double MaximumIndependentWitnessUncertainty95ArcSeconds = 30.0,
        int MinimumIndependentWitnessCalibrationSamples = 3,
        int MinimumIndependentWitnessClosureSamples = 2);

    internal sealed record TppaFastQualificationInput(
        double DurationSeconds,
        int FreshDeterminationCount,
        bool FreshSolvesUncached,
        string HardwareConfigurationId,
        string ClockDomainId,
        string TppaInstrumentId,
        string TppaInputPathDigest,
        string SolverIdentity,
        double MaximumPairwiseDeltaArcMinutes,
        double FinalReportedErrorArcMinutes,
        string DeltaMetric,
        string ErrorMetric,
        bool NoPhysicalAdjustmentBetweenDeterminations,
        bool GeometryQualified,
        bool MinimumArcSpanQualified,
        bool ClosureQualified,
        bool RefractionAdjustmentEnabled,
        string PoleTarget,
        string AtmosphereSource,
        bool AtmosphereQualified,
        bool AtmosphereFresh,
        bool StationPressureQualified,
        bool AtmosphereTemperatureQualified,
        bool AtmosphereHumidityQualified,
        bool SiteTimeProvenanceQualified,
        string CoordinateFrame,
        bool CoordinateFrameQualified,
        bool IndependentWitnessQualified,
        bool IndependentWitnessSameMechanicalState,
        bool IndependentWitnessDisjointInputPathQualified,
        string IndependentWitnessInstrumentId,
        string IndependentWitnessInputPathDigest,
        string IndependentWitnessPoleTarget,
        string IndependentWitnessCoordinateFrame,
        string IndependentWitnessCalibrationDigest,
        bool IndependentWitnessCalibrationCurrent,
        TppaWitnessUncertaintyEvidence IndependentWitnessUncertainty,
        double? IndependentTruePoleErrorArcMinutes,
        double? TppaToIndependentDeltaArcMinutes);

    internal sealed record TppaFastQualificationResult(
        bool IsFastTruePoleQualified,
        IReadOnlyList<string> Issues);

    internal static class TppaFastQualification {
        public static TppaFastQualificationResult Evaluate(
            TppaFastQualificationInput input,
            TppaFastQualificationPolicy policy = null) {
            var activePolicy = policy ?? new TppaFastQualificationPolicy();
            var issues = new List<string>();

            RequireFiniteNonNegative(input.DurationSeconds, nameof(input.DurationSeconds));
            RequireFiniteNonNegative(
                input.MaximumPairwiseDeltaArcMinutes,
                nameof(input.MaximumPairwiseDeltaArcMinutes));
            RequireFiniteNonNegative(
                input.FinalReportedErrorArcMinutes,
                nameof(input.FinalReportedErrorArcMinutes));
            RequireOptionalFiniteNonNegative(
                input.IndependentTruePoleErrorArcMinutes,
                nameof(input.IndependentTruePoleErrorArcMinutes));
            RequireOptionalFiniteNonNegative(
                input.TppaToIndependentDeltaArcMinutes,
                nameof(input.TppaToIndependentDeltaArcMinutes));

            if (string.IsNullOrWhiteSpace(input.HardwareConfigurationId)
                    || string.IsNullOrWhiteSpace(input.ClockDomainId)
                    || string.IsNullOrWhiteSpace(input.TppaInstrumentId)
                    || string.IsNullOrWhiteSpace(input.SolverIdentity)) {
                issues.Add(
                    "TPPA hardware epoch, clock domain, instrument, or solver identity is missing");
            }
            if (!IsSha256(input.TppaInputPathDigest)) {
                issues.Add("TPPA input-path provenance digest is missing or invalid");
            }
            if (input.DurationSeconds > activePolicy.MaximumDurationSeconds) {
                issues.Add(
                    FormattableString.Invariant(
                        $"run duration {input.DurationSeconds:F1}s exceeds the {activePolicy.MaximumDurationSeconds:F1}s fast-path ceiling"));
            }
            if (input.FreshDeterminationCount < activePolicy.MinimumFreshDeterminations) {
                issues.Add(
                    $"only {input.FreshDeterminationCount} fresh determination(s) are available");
            }
            if (!input.FreshSolvesUncached) {
                issues.Add("fresh determinations include cached or reused solves");
            }
            if (input.DeltaMetric
                    != TppaFastQualificationConventions.SphericalVectorSeparationArcMinutes) {
                issues.Add("repeatability and witness deltas do not use spherical-vector separation");
            }
            if (input.ErrorMetric
                    != TppaFastQualificationConventions.SphericalPolarErrorMagnitudeArcMinutes) {
                issues.Add("reported and witness errors do not use spherical polar-error magnitude");
            }
            if (!input.NoPhysicalAdjustmentBetweenDeterminations) {
                issues.Add("physical state changed between fresh determinations");
            }
            if (input.MaximumPairwiseDeltaArcMinutes
                    > activePolicy.MaximumRepeatabilityDeltaArcMinutes) {
                issues.Add(
                    FormattableString.Invariant(
                        $"repeatability delta {input.MaximumPairwiseDeltaArcMinutes:F3}' exceeds {activePolicy.MaximumRepeatabilityDeltaArcMinutes:F3}'"));
            }
            if (input.FinalReportedErrorArcMinutes
                    > activePolicy.MaximumFinalErrorArcMinutes) {
                issues.Add(
                    FormattableString.Invariant(
                        $"reported final error {input.FinalReportedErrorArcMinutes:F3}' exceeds {activePolicy.MaximumFinalErrorArcMinutes:F3}'"));
            }
            if (!input.GeometryQualified) {
                issues.Add("three-point geometry is not qualified");
            }
            if (!input.MinimumArcSpanQualified) {
                issues.Add("three-point arc span is below the accuracy floor");
            }
            if (!input.ClosureQualified) {
                issues.Add("measurement closure is not qualified");
            }
            if (!input.RefractionAdjustmentEnabled
                    || input.PoleTarget != RefractionAlignmentTarget.TruePoleTarget) {
                issues.Add("run did not target the true celestial pole");
            }
            if (input.AtmosphereSource
                    != TppaFastQualificationConventions.QualifiedLocalWeatherStation
                    || !input.AtmosphereQualified
                    || !input.AtmosphereFresh
                    || !input.StationPressureQualified
                    || !input.AtmosphereTemperatureQualified
                    || !input.AtmosphereHumidityQualified) {
                issues.Add(
                    "atmosphere provenance is missing, stale, or lacks qualified station pressure, temperature, or humidity");
            }
            if (!input.SiteTimeProvenanceQualified) {
                issues.Add("site coordinates, elevation, epoch, or clock provenance is unqualified");
            }
            if (input.CoordinateFrame
                    != TppaFastQualificationConventions.IcrsObservationEpoch
                    || !input.CoordinateFrameQualified) {
                issues.Add("TPPA coordinate frame or epoch reduction is unqualified");
            }
            if (!input.IndependentWitnessQualified) {
                issues.Add("independent true-pole witness is not qualified");
            }
            if (!input.IndependentWitnessSameMechanicalState) {
                issues.Add("independent witness was not captured in the same mechanical state");
            }
            if (!input.IndependentWitnessDisjointInputPathQualified) {
                issues.Add("independent witness does not provide a qualified disjoint input path");
            }
            if (string.IsNullOrWhiteSpace(input.IndependentWitnessInstrumentId)
                    || input.IndependentWitnessInstrumentId == input.TppaInstrumentId) {
                issues.Add("independent witness instrument identity is missing or not independent");
            }
            if (!IsSha256(input.IndependentWitnessInputPathDigest)
                    || input.IndependentWitnessInputPathDigest
                        == input.TppaInputPathDigest) {
                issues.Add(
                    "independent witness input path is missing or aliases the TPPA input path");
            }
            if (input.IndependentWitnessPoleTarget
                    != RefractionAlignmentTarget.TruePoleTarget) {
                issues.Add("independent witness does not use the true-pole convention");
            }
            if (string.IsNullOrWhiteSpace(input.IndependentWitnessCoordinateFrame)
                    || input.IndependentWitnessCoordinateFrame != input.CoordinateFrame
                    || input.CoordinateFrame != TppaFastQualificationConventions.IcrsObservationEpoch) {
                issues.Add("independent witness coordinate frame does not match TPPA");
            }
            if (!IsSha256(input.IndependentWitnessCalibrationDigest)
                    || !input.IndependentWitnessCalibrationCurrent) {
                issues.Add("independent witness calibration provenance is missing or stale");
            }

            var witnessUncertainty = TppaWitnessUncertainty.Evaluate(
                input.IndependentWitnessUncertainty,
                activePolicy.MaximumIndependentWitnessUncertainty95ArcSeconds,
                activePolicy.MinimumIndependentWitnessCalibrationSamples,
                activePolicy.MinimumIndependentWitnessClosureSamples);
            issues.AddRange(witnessUncertainty.Issues);
            var witnessUncertaintyArcMinutes = witnessUncertainty.IsQualified
                    && witnessUncertainty.UpperBound95ArcSeconds.HasValue
                ? witnessUncertainty.UpperBound95ArcSeconds.Value / 60.0
                : (double?)null;
            if (!input.IndependentTruePoleErrorArcMinutes.HasValue
                    || !witnessUncertaintyArcMinutes.HasValue
                    || input.IndependentTruePoleErrorArcMinutes.Value
                        + witnessUncertaintyArcMinutes.Value
                        > activePolicy.MaximumIndependentErrorArcMinutes) {
                issues.Add("independent witness does not establish the required true-pole error");
            }
            if (!input.TppaToIndependentDeltaArcMinutes.HasValue
                    || !witnessUncertaintyArcMinutes.HasValue
                    || input.TppaToIndependentDeltaArcMinutes.Value
                        + witnessUncertaintyArcMinutes.Value
                        > activePolicy.MaximumIndependentDeltaArcMinutes) {
                issues.Add("TPPA and the independent witness do not agree within the policy");
            }
            if (input.IndependentTruePoleErrorArcMinutes.HasValue
                    && input.TppaToIndependentDeltaArcMinutes.HasValue
                    && witnessUncertaintyArcMinutes.HasValue
                    && input.IndependentTruePoleErrorArcMinutes.Value
                        + input.TppaToIndependentDeltaArcMinutes.Value
                        + witnessUncertaintyArcMinutes.Value
                        > activePolicy.MaximumCombinedAbsoluteErrorArcMinutes) {
                issues.Add("witness error plus TPPA disagreement exceeds the absolute-error budget");
            }

            return new TppaFastQualificationResult(!issues.Any(), issues);
        }

        private static bool IsSha256(string value) =>
            value?.Length == 64
            && value.All(character =>
                (character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f'));

        private static void RequireFiniteNonNegative(double value, string name) {
            if (!double.IsFinite(value) || value < 0) {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static void RequireOptionalFiniteNonNegative(
            double? value,
            string name) {
            if (value.HasValue) {
                RequireFiniteNonNegative(value.Value, name);
            }
        }
    }
}
