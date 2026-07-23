using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaDriftValidationPolicy(
        double MinimumTrackDurationSeconds,
        int MinimumSamplesPerTrack,
        double MinimumAltitudeDegrees,
        double MaximumTrackSlopeSigmaArcsecondsPerMinute,
        double MaximumDesignConditionNumber,
        double MaximumReducedChiSquared,
        double MaximumRepeatedPositionStandardizedResidual) {
        public static TppaDriftValidationPolicy FieldDefault => new(
            MinimumTrackDurationSeconds: 300,
            MinimumSamplesPerTrack: 8,
            MinimumAltitudeDegrees: 30,
            MaximumTrackSlopeSigmaArcsecondsPerMinute: 0.20,
            MaximumDesignConditionNumber: 100,
            MaximumReducedChiSquared: 4,
            MaximumRepeatedPositionStandardizedResidual: 3);
    }

    internal readonly record struct TppaDriftTrack(
        string PositionId,
        double HourAngleDegrees,
        double AltitudeDegrees,
        double DurationSeconds,
        int SampleCount,
        double DeclinationDriftArcsecondsPerMinute,
        double DeclinationDriftSigmaArcsecondsPerMinute,
        double ComputedRefractionDriftArcsecondsPerMinute);

    internal readonly record struct TppaDriftValidationResult(
        int TrackCount,
        double AzimuthErrorArcMinutes,
        double AltitudeErrorArcMinutes,
        double TotalErrorArcMinutes,
        double AzimuthSigmaArcMinutes,
        double AltitudeSigmaArcMinutes,
        double ReducedChiSquared,
        double DesignConditionNumber,
        double MaximumRepeatedPositionStandardizedResidual,
        bool IsValid,
        string Reason);

    /// <summary>
    /// Fits a polar-error vector from declination drift measured at several TPPA
    /// positions. It is report-only and does not expose actuator commands.
    /// </summary>
    internal static class TppaDriftValidationEstimator {
        private const double SiderealRateArcsecondsPerMinute = 360.0 * 3600.0 / 1436.068;
        private const double RadiansToArcMinutes = 180.0 * 60.0 / Math.PI;

        public static TppaDriftValidationResult Evaluate(
            IReadOnlyList<TppaDriftTrack> tracks,
            double siteLatitudeDegrees,
            TppaDriftValidationPolicy? policy = null) {
            var activePolicy = policy ?? TppaDriftValidationPolicy.FieldDefault;
            if (tracks is null || tracks.Count < 4) {
                return Invalid(tracks?.Count ?? 0, "at least four A-B-C-A tracks are required");
            }
            if (!double.IsFinite(siteLatitudeDegrees) || Math.Abs(siteLatitudeDegrees) > 90) {
                return Invalid(tracks.Count, "site latitude must be finite and within -90 to +90 degrees");
            }
            if (tracks.Any(track => !IsFinite(track))) {
                return Invalid(tracks.Count, "all track measurements must be finite");
            }
            if (tracks.Any(track => string.IsNullOrWhiteSpace(track.PositionId))) {
                return Invalid(tracks.Count, "every track must have a position identifier");
            }
            if (tracks.Any(track => track.DurationSeconds < activePolicy.MinimumTrackDurationSeconds)) {
                return Invalid(tracks.Count, $"every track must span at least {activePolicy.MinimumTrackDurationSeconds:F0}s");
            }
            if (tracks.Any(track => track.SampleCount < activePolicy.MinimumSamplesPerTrack)) {
                return Invalid(tracks.Count, $"every track must contain at least {activePolicy.MinimumSamplesPerTrack} samples");
            }
            if (tracks.Any(track => track.AltitudeDegrees < activePolicy.MinimumAltitudeDegrees)) {
                return Invalid(tracks.Count, $"every track must remain at or above {activePolicy.MinimumAltitudeDegrees:F1} degrees altitude");
            }
            if (tracks.Any(track => track.DeclinationDriftSigmaArcsecondsPerMinute <= 0
                    || track.DeclinationDriftSigmaArcsecondsPerMinute > activePolicy.MaximumTrackSlopeSigmaArcsecondsPerMinute)) {
                return Invalid(tracks.Count,
                    $"every track slope sigma must be positive and no greater than {activePolicy.MaximumTrackSlopeSigmaArcsecondsPerMinute:F3} arcsec/min");
            }

            var repeatedPositionIds = tracks
                .GroupBy(track => track.PositionId, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() >= 2)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (repeatedPositionIds.Count == 0) {
                return Invalid(tracks.Count, "the acquisition must revisit at least one position");
            }

            var latitudeRadians = DegreesToRadians(siteLatitudeDegrees);
            var normal00 = 0.0;
            var normal01 = 0.0;
            var normal11 = 0.0;
            var rhs0 = 0.0;
            var rhs1 = 0.0;
            foreach (var track in tracks) {
                var hourAngleRadians = DegreesToRadians(track.HourAngleDegrees);
                var coefficientAltitude = -Math.Sin(hourAngleRadians);
                var coefficientAzimuth = Math.Cos(latitudeRadians) * Math.Cos(hourAngleRadians);
                var correctedDrift = track.DeclinationDriftArcsecondsPerMinute
                    - track.ComputedRefractionDriftArcsecondsPerMinute;
                var normalizedObservation = correctedDrift / SiderealRateArcsecondsPerMinute;
                var normalizedSigma = track.DeclinationDriftSigmaArcsecondsPerMinute / SiderealRateArcsecondsPerMinute;
                var weight = 1.0 / (normalizedSigma * normalizedSigma);

                normal00 += weight * coefficientAzimuth * coefficientAzimuth;
                normal01 += weight * coefficientAzimuth * coefficientAltitude;
                normal11 += weight * coefficientAltitude * coefficientAltitude;
                rhs0 += weight * coefficientAzimuth * normalizedObservation;
                rhs1 += weight * coefficientAltitude * normalizedObservation;
            }

            var determinant = normal00 * normal11 - normal01 * normal01;
            if (!(determinant > 0) || !double.IsFinite(determinant)) {
                return Invalid(tracks.Count, "drift-validation geometry is singular");
            }
            var conditionNumber = SymmetricConditionNumber(normal00, normal01, normal11);
            if (!double.IsFinite(conditionNumber) || conditionNumber > activePolicy.MaximumDesignConditionNumber) {
                return Invalid(tracks.Count,
                    $"drift-validation geometry condition number {conditionNumber:F2} exceeds {activePolicy.MaximumDesignConditionNumber:F2}");
            }

            var azimuthErrorRadians = (normal11 * rhs0 - normal01 * rhs1) / determinant;
            var altitudeErrorRadians = (normal00 * rhs1 - normal01 * rhs0) / determinant;
            var chiSquared = 0.0;
            var maximumRepeatedResidual = 0.0;
            foreach (var track in tracks) {
                var hourAngleRadians = DegreesToRadians(track.HourAngleDegrees);
                var predictedDrift = SiderealRateArcsecondsPerMinute * (
                    azimuthErrorRadians * Math.Cos(latitudeRadians) * Math.Cos(hourAngleRadians)
                    - altitudeErrorRadians * Math.Sin(hourAngleRadians))
                    + track.ComputedRefractionDriftArcsecondsPerMinute;
                var standardizedResidual = Math.Abs(track.DeclinationDriftArcsecondsPerMinute - predictedDrift)
                    / track.DeclinationDriftSigmaArcsecondsPerMinute;
                chiSquared += standardizedResidual * standardizedResidual;
                if (repeatedPositionIds.Contains(track.PositionId)) {
                    maximumRepeatedResidual = Math.Max(maximumRepeatedResidual, standardizedResidual);
                }
            }

            var degreesOfFreedom = tracks.Count - 2;
            var reducedChiSquared = chiSquared / degreesOfFreedom;
            var covarianceScale = Math.Max(1.0, reducedChiSquared);
            var azimuthVariance = covarianceScale * normal11 / determinant;
            var altitudeVariance = covarianceScale * normal00 / determinant;
            var azimuthArcMinutes = azimuthErrorRadians * RadiansToArcMinutes;
            var altitudeArcMinutes = altitudeErrorRadians * RadiansToArcMinutes;
            var totalArcMinutes = Math.Sqrt(azimuthArcMinutes * azimuthArcMinutes + altitudeArcMinutes * altitudeArcMinutes);
            var azimuthSigmaArcMinutes = Math.Sqrt(azimuthVariance) * RadiansToArcMinutes;
            var altitudeSigmaArcMinutes = Math.Sqrt(altitudeVariance) * RadiansToArcMinutes;

            if (!double.IsFinite(reducedChiSquared) || reducedChiSquared > activePolicy.MaximumReducedChiSquared) {
                return Result(false,
                    $"global reduced chi-squared {reducedChiSquared:F2} exceeds {activePolicy.MaximumReducedChiSquared:F2}");
            }
            if (maximumRepeatedResidual > activePolicy.MaximumRepeatedPositionStandardizedResidual) {
                return Result(false,
                    $"repeated-position standardized residual {maximumRepeatedResidual:F2} exceeds {activePolicy.MaximumRepeatedPositionStandardizedResidual:F2}");
            }
            return Result(true, "geometry, track quality, global fit, and repeated-position gates passed");

            TppaDriftValidationResult Result(bool valid, string reason) => new(
                tracks.Count,
                azimuthArcMinutes,
                altitudeArcMinutes,
                totalArcMinutes,
                azimuthSigmaArcMinutes,
                altitudeSigmaArcMinutes,
                reducedChiSquared,
                conditionNumber,
                maximumRepeatedResidual,
                valid,
                reason);
        }

        internal static double PredictDeclinationDriftArcsecondsPerMinute(
            double azimuthErrorArcMinutes,
            double altitudeErrorArcMinutes,
            double siteLatitudeDegrees,
            double hourAngleDegrees,
            double computedRefractionDriftArcsecondsPerMinute = 0) {
            var azimuthErrorRadians = azimuthErrorArcMinutes / RadiansToArcMinutes;
            var altitudeErrorRadians = altitudeErrorArcMinutes / RadiansToArcMinutes;
            var latitudeRadians = DegreesToRadians(siteLatitudeDegrees);
            var hourAngleRadians = DegreesToRadians(hourAngleDegrees);
            return SiderealRateArcsecondsPerMinute * (
                azimuthErrorRadians * Math.Cos(latitudeRadians) * Math.Cos(hourAngleRadians)
                - altitudeErrorRadians * Math.Sin(hourAngleRadians))
                + computedRefractionDriftArcsecondsPerMinute;
        }

        private static bool IsFinite(TppaDriftTrack track) =>
            double.IsFinite(track.HourAngleDegrees)
            && double.IsFinite(track.AltitudeDegrees)
            && double.IsFinite(track.DurationSeconds)
            && double.IsFinite(track.DeclinationDriftArcsecondsPerMinute)
            && double.IsFinite(track.DeclinationDriftSigmaArcsecondsPerMinute)
            && double.IsFinite(track.ComputedRefractionDriftArcsecondsPerMinute);

        private static double SymmetricConditionNumber(double a, double b, double d) {
            var trace = a + d;
            var root = Math.Sqrt((a - d) * (a - d) + 4 * b * b);
            var maximumEigenvalue = (trace + root) / 2.0;
            var minimumEigenvalue = (trace - root) / 2.0;
            return minimumEigenvalue > 0 ? maximumEigenvalue / minimumEigenvalue : double.PositiveInfinity;
        }

        private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

        private static TppaDriftValidationResult Invalid(int trackCount, string reason) => new(
            trackCount,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            false,
            reason);
    }
}
