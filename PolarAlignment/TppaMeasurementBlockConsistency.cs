using System;
using System.Globalization;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaPolarErrorVector(
        double AzimuthMinutes,
        double AltitudeMinutes,
        double TotalMinutes,
        double PhaseDegrees) {
        public static TppaPolarErrorVector FromMinutes(
            double azimuthMinutes,
            double altitudeMinutes,
            double totalMinutes) =>
            new(
                azimuthMinutes,
                altitudeMinutes,
                totalMinutes,
                NormalizeDegrees(Math.Atan2(altitudeMinutes, azimuthMinutes) * 180.0 / Math.PI));

        public string ToLogString() => string.Format(
            CultureInfo.InvariantCulture,
            "Az={0:+0.000;-0.000;0.000}', Alt={1:+0.000;-0.000;0.000}', Total={2:0.000}', Phase={3:+0.000;-0.000;0.000} deg",
            AzimuthMinutes,
            AltitudeMinutes,
            TotalMinutes,
            PhaseDegrees);

        internal static double CircularDistanceDegrees(double first, double second) =>
            Math.Abs(NormalizeDegrees(second - first));

        private static double NormalizeDegrees(double value) {
            var normalized = value % 360.0;
            if (normalized > 180.0) {
                normalized -= 360.0;
            } else if (normalized <= -180.0) {
                normalized += 360.0;
            }
            return normalized;
        }
    }

    internal readonly record struct TppaMeasurementBlockPolicy(
        double MaximumComponentDeltaMinutes,
        double MaximumTotalDeltaMinutes,
        double MaximumPhaseDeltaDegrees) {
        public static TppaMeasurementBlockPolicy SameArcFieldDefault => new(
            MaximumComponentDeltaMinutes: 30.0 / 60.0,
            MaximumTotalDeltaMinutes: 20.0 / 60.0,
            MaximumPhaseDeltaDegrees: 2.5);
    }

    internal readonly record struct TppaMeasurementBlockConsistencyResult(
        bool IsConsistent,
        double AzimuthDeltaMinutes,
        double AltitudeDeltaMinutes,
        double TotalDeltaMinutes,
        double PhaseDeltaDegrees,
        string Reason);

    internal static class TppaMeasurementBlockConsistencyEvaluator {
        public static TppaMeasurementBlockConsistencyResult Evaluate(
            TppaMeasurementSetResult first,
            TppaMeasurementSetResult second,
            TppaMeasurementBlockPolicy? policy = null) {
            var activePolicy = policy ?? TppaMeasurementBlockPolicy.SameArcFieldDefault;
            if (!ValidPolicy(activePolicy)) {
                return Invalid("cross-block policy is invalid");
            }
            if (!first.IsRepeatable || !second.IsRepeatable) {
                return Invalid("both measurement blocks must pass their within-block repeatability gates");
            }

            var firstVector = TppaPolarErrorVector.FromMinutes(
                first.MedianAzimuthMinutes,
                first.MedianAltitudeMinutes,
                first.MedianTotalMinutes);
            var secondVector = TppaPolarErrorVector.FromMinutes(
                second.MedianAzimuthMinutes,
                second.MedianAltitudeMinutes,
                second.MedianTotalMinutes);
            var azimuthDelta = secondVector.AzimuthMinutes - firstVector.AzimuthMinutes;
            var altitudeDelta = secondVector.AltitudeMinutes - firstVector.AltitudeMinutes;
            var totalDelta = secondVector.TotalMinutes - firstVector.TotalMinutes;
            var phaseDelta = TppaPolarErrorVector.CircularDistanceDegrees(
                firstVector.PhaseDegrees,
                secondVector.PhaseDegrees);
            var consistent = Math.Abs(azimuthDelta) <= activePolicy.MaximumComponentDeltaMinutes
                && Math.Abs(altitudeDelta) <= activePolicy.MaximumComponentDeltaMinutes
                && Math.Abs(totalDelta) <= activePolicy.MaximumTotalDeltaMinutes
                && phaseDelta <= activePolicy.MaximumPhaseDeltaDegrees;
            var reason = consistent
                ? "same-arc cross-block component, total, and phase gates passed"
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "same-arc cross-block gate failed: dAz={0:+0.000;-0.000;0.000}', dAlt={1:+0.000;-0.000;0.000}', dTotal={2:+0.000;-0.000;0.000}', dPhase={3:0.000} deg",
                    azimuthDelta,
                    altitudeDelta,
                    totalDelta,
                    phaseDelta);

            return new(consistent, azimuthDelta, altitudeDelta, totalDelta, phaseDelta, reason);
        }

        private static bool ValidPolicy(TppaMeasurementBlockPolicy policy) =>
            double.IsFinite(policy.MaximumComponentDeltaMinutes) && policy.MaximumComponentDeltaMinutes > 0
            && double.IsFinite(policy.MaximumTotalDeltaMinutes) && policy.MaximumTotalDeltaMinutes > 0
            && double.IsFinite(policy.MaximumPhaseDeltaDegrees) && policy.MaximumPhaseDeltaDegrees > 0;

        private static TppaMeasurementBlockConsistencyResult Invalid(string reason) =>
            new(false, double.NaN, double.NaN, double.NaN, double.NaN, reason);
    }
}
