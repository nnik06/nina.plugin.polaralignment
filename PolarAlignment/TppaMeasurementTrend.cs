using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaTimedFreshMeasurement(
        DateTime ObservationTimeUtc,
        double AzimuthMinutes,
        double AltitudeMinutes,
        double TotalMinutes);

    internal readonly record struct TppaMeasurementTrendPolicy(
        int MinimumMeasurements,
        double MinimumSampleSeparationMinutes,
        double MinimumObservationSpanMinutes,
        double MaximumComponentTrendSpanMinutes,
        double MaximumTotalTrendSpanMinutes) {
        /// <summary>
        /// Provisional report-only thresholds inherited from the existing same-arc
        /// consistency gates. Field qualification is still required before these
        /// values can authorize movement or support an absolute-accuracy claim.
        /// </summary>
        public static TppaMeasurementTrendPolicy DiagnosticDefault => new(
            MinimumMeasurements: 5,
            MinimumSampleSeparationMinutes: 0.1,
            MinimumObservationSpanMinutes: 20,
            MaximumComponentTrendSpanMinutes: 30.0 / 60.0,
            MaximumTotalTrendSpanMinutes: 20.0 / 60.0);
    }

    internal readonly record struct TppaMeasurementTrendResult(
        bool IsStable,
        int Count,
        double ObservationSpanMinutes,
        double AzimuthSlopeMinutesPerMinute,
        double AltitudeSlopeMinutesPerMinute,
        double TotalSlopeMinutesPerMinute,
        double AzimuthTrendSpanMinutes,
        double AltitudeTrendSpanMinutes,
        double TotalTrendSpanMinutes,
        string Reason);

    /// <summary>
    /// Detects monotonic movement that a median/MAD repeatability gate can miss.
    /// The slope is the median of every pairwise slope (Theil-Sen), making the
    /// diagnostic resistant to a single bad solve without hiding a sustained walk.
    /// </summary>
    internal static class TppaMeasurementTrendEvaluator {
        public static TppaMeasurementTrendResult Evaluate(
            IReadOnlyList<TppaTimedFreshMeasurement> measurements,
            TppaMeasurementTrendPolicy? policy = null) {
            var activePolicy = policy ?? TppaMeasurementTrendPolicy.DiagnosticDefault;
            if (measurements == null) {
                return Invalid(0, "measurement trend set is null");
            }
            if (activePolicy.MinimumMeasurements < 3
                    || !PositiveFinite(activePolicy.MinimumSampleSeparationMinutes)
                    || !PositiveFinite(activePolicy.MinimumObservationSpanMinutes)
                    || !PositiveFinite(activePolicy.MaximumComponentTrendSpanMinutes)
                    || !PositiveFinite(activePolicy.MaximumTotalTrendSpanMinutes)) {
                return Invalid(measurements.Count, "measurement-trend policy is invalid");
            }
            if (measurements.Count < activePolicy.MinimumMeasurements) {
                return Invalid(
                    measurements.Count,
                    $"at least {activePolicy.MinimumMeasurements} timestamped same-geometry measurements are required");
            }

            DateTime? previousTime = null;
            foreach (var measurement in measurements) {
                if (measurement.ObservationTimeUtc.Kind != DateTimeKind.Utc) {
                    return Invalid(measurements.Count, "all observation times must be UTC");
                }
                if (previousTime.HasValue && measurement.ObservationTimeUtc <= previousTime.Value) {
                    return Invalid(measurements.Count, "observation times must be strictly increasing");
                }
                if (previousTime.HasValue
                        && (measurement.ObservationTimeUtc - previousTime.Value).TotalMinutes
                            < activePolicy.MinimumSampleSeparationMinutes) {
                    return Invalid(
                        measurements.Count,
                        $"adjacent observations must be separated by at least {activePolicy.MinimumSampleSeparationMinutes:F3} min");
                }
                if (!double.IsFinite(measurement.AzimuthMinutes)
                        || !double.IsFinite(measurement.AltitudeMinutes)
                        || !double.IsFinite(measurement.TotalMinutes)
                        || measurement.TotalMinutes < 0) {
                    return Invalid(measurements.Count, "one or more trend measurements are invalid");
                }
                previousTime = measurement.ObservationTimeUtc;
            }

            var observationSpan = (
                measurements[^1].ObservationTimeUtc - measurements[0].ObservationTimeUtc).TotalMinutes;
            if (observationSpan < activePolicy.MinimumObservationSpanMinutes) {
                return Invalid(
                    measurements.Count,
                    $"measurement span {observationSpan:F2} min is shorter than {activePolicy.MinimumObservationSpanMinutes:F2} min");
            }

            var azimuthSlope = TheilSenSlope(measurements, sample => sample.AzimuthMinutes);
            var altitudeSlope = TheilSenSlope(measurements, sample => sample.AltitudeMinutes);
            var totalSlope = TheilSenSlope(measurements, sample => sample.TotalMinutes);
            var azimuthTrendSpan = Math.Abs(azimuthSlope) * observationSpan;
            var altitudeTrendSpan = Math.Abs(altitudeSlope) * observationSpan;
            var totalTrendSpan = Math.Abs(totalSlope) * observationSpan;
            var stable = azimuthTrendSpan <= activePolicy.MaximumComponentTrendSpanMinutes
                && altitudeTrendSpan <= activePolicy.MaximumComponentTrendSpanMinutes
                && totalTrendSpan <= activePolicy.MaximumTotalTrendSpanMinutes;
            var reason = stable
                ? "same-geometry robust trend-span gates passed"
                : $"same-geometry trend gate failed: Az={azimuthTrendSpan:F3}', Alt={altitudeTrendSpan:F3}', Total={totalTrendSpan:F3}'";

            return new(
                stable,
                measurements.Count,
                observationSpan,
                azimuthSlope,
                altitudeSlope,
                totalSlope,
                azimuthTrendSpan,
                altitudeTrendSpan,
                totalTrendSpan,
                reason);
        }

        private static double TheilSenSlope(
            IReadOnlyList<TppaTimedFreshMeasurement> measurements,
            Func<TppaTimedFreshMeasurement, double> valueSelector) {
            var slopes = new List<double>(measurements.Count * (measurements.Count - 1) / 2);
            for (var first = 0; first < measurements.Count - 1; first++) {
                for (var second = first + 1; second < measurements.Count; second++) {
                    var elapsedMinutes = (
                        measurements[second].ObservationTimeUtc - measurements[first].ObservationTimeUtc).TotalMinutes;
                    slopes.Add((valueSelector(measurements[second]) - valueSelector(measurements[first])) / elapsedMinutes);
                }
            }
            slopes.Sort();
            return Median(slopes);
        }

        private static double Median(IReadOnlyList<double> values) {
            var middle = values.Count / 2;
            return values.Count % 2 == 0
                ? (values[middle - 1] + values[middle]) / 2.0
                : values[middle];
        }

        private static bool PositiveFinite(double value) => double.IsFinite(value) && value > 0;

        private static TppaMeasurementTrendResult Invalid(int count, string reason) => new(
            false,
            count,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            reason);
    }
}
