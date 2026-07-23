using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaFreshMeasurement(
        double AzimuthMinutes,
        double AltitudeMinutes,
        double TotalMinutes);

    internal readonly record struct TppaMeasurementSetPolicy(
        int MinimumMeasurements,
        double MaximumComponentMadMinutes,
        double MaximumTotalMadMinutes) {
        public static TppaMeasurementSetPolicy FieldDefault => new(
            MinimumMeasurements: 3,
            MaximumComponentMadMinutes: 20.0 / 60.0,
            MaximumTotalMadMinutes: 10.0 / 60.0);
    }

    internal readonly record struct TppaMeasurementSetResult(
        bool IsRepeatable,
        int Count,
        double MedianAzimuthMinutes,
        double MedianAltitudeMinutes,
        double MedianTotalMinutes,
        double AzimuthMadMinutes,
        double AltitudeMadMinutes,
        double TotalMadMinutes,
        string Reason);

    internal static class TppaMeasurementSetEvaluator {
        public static TppaMeasurementSetResult Evaluate(
            IReadOnlyCollection<TppaFreshMeasurement> measurements,
            TppaMeasurementSetPolicy? policy = null) {
            var activePolicy = policy ?? TppaMeasurementSetPolicy.FieldDefault;
            if (measurements == null) {
                return Invalid(0, "measurement set is null");
            }
            if (activePolicy.MinimumMeasurements < 3
                    || !PositiveFinite(activePolicy.MaximumComponentMadMinutes)
                    || !PositiveFinite(activePolicy.MaximumTotalMadMinutes)) {
                return Invalid(measurements.Count, "measurement-set policy is invalid");
            }
            if (measurements.Count < activePolicy.MinimumMeasurements) {
                return Invalid(
                    measurements.Count,
                    $"at least {activePolicy.MinimumMeasurements} same-geometry measurements are required");
            }
            if (measurements.Any(sample =>
                    !double.IsFinite(sample.AzimuthMinutes)
                    || !double.IsFinite(sample.AltitudeMinutes)
                    || !double.IsFinite(sample.TotalMinutes)
                    || sample.TotalMinutes < 0)) {
                return Invalid(measurements.Count, "one or more measurements are invalid");
            }

            var azimuth = measurements.Select(sample => sample.AzimuthMinutes).OrderBy(value => value).ToArray();
            var altitude = measurements.Select(sample => sample.AltitudeMinutes).OrderBy(value => value).ToArray();
            var total = measurements.Select(sample => sample.TotalMinutes).OrderBy(value => value).ToArray();
            var medianAzimuth = Median(azimuth);
            var medianAltitude = Median(altitude);
            var medianTotal = Median(total);
            var azimuthMad = Mad(azimuth, medianAzimuth);
            var altitudeMad = Mad(altitude, medianAltitude);
            var totalMad = Mad(total, medianTotal);
            var repeatable = azimuthMad <= activePolicy.MaximumComponentMadMinutes
                && altitudeMad <= activePolicy.MaximumComponentMadMinutes
                && totalMad <= activePolicy.MaximumTotalMadMinutes;
            var reason = repeatable
                ? "same-geometry component and total MAD gates passed"
                : $"same-geometry MAD gate failed: Az={azimuthMad:F3}', Alt={altitudeMad:F3}', Total={totalMad:F3}'";

            return new(
                repeatable,
                measurements.Count,
                medianAzimuth,
                medianAltitude,
                medianTotal,
                azimuthMad,
                altitudeMad,
                totalMad,
                reason);
        }

        private static TppaMeasurementSetResult Invalid(int count, string reason) =>
            new(false, count, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, reason);

        private static bool PositiveFinite(double value) => double.IsFinite(value) && value > 0;

        private static double Mad(IReadOnlyList<double> values, double median) =>
            Median(values.Select(value => Math.Abs(value - median)).OrderBy(value => value).ToArray());

        private static double Median(IReadOnlyList<double> values) {
            var middle = values.Count / 2;
            return values.Count % 2 == 0
                ? (values[middle - 1] + values[middle]) / 2.0
                : values[middle];
        }
    }
}
