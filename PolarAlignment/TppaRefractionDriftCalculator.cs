using System;
using NINA.Astrometry;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaRefractionDriftResult(
        bool IsValid,
        double DeclinationDriftArcsecondsPerMinute,
        double ApparentAltitudeDegrees,
        double MidpointClosureArcseconds,
        string Reason);

    /// <summary>
    /// Computes the declination drift that a plate solver observes from
    /// atmospheric refraction alone while an aligned mount tracks.
    /// </summary>
    internal static class TppaRefractionDriftCalculator {
        internal const double MinimumAltitudeDegrees = 30;
        internal const double MaximumClosureArcseconds = 0.05;
        private static readonly TimeSpan DefaultHalfSpan = TimeSpan.FromSeconds(30);

        public static TppaRefractionDriftResult Evaluate(
            Coordinates solvedCoordinates,
            Angle latitude,
            Angle longitude,
            double elevationMeters,
            RefractionParameters refraction,
            DateTime observationTimeUtc,
            TimeSpan? halfSpan = null) {
            var validationIssue = ValidateInputs(
                solvedCoordinates,
                latitude,
                longitude,
                elevationMeters,
                refraction,
                observationTimeUtc,
                halfSpan ?? DefaultHalfSpan);
            if (validationIssue != null) {
                return Invalid(validationIssue);
            }

            var interval = halfSpan ?? DefaultHalfSpan;
            var apparentAtMidpoint = solvedCoordinates.Transform(
                latitude,
                longitude,
                elevationMeters,
                refraction.PressureHPa,
                refraction.Temperature,
                refraction.RelativeHumidity,
                refraction.Wavelength,
                observationTimeUtc);
            var altitudeDegrees = apparentAtMidpoint.Altitude.Degree;
            if (!double.IsFinite(altitudeDegrees) || altitudeDegrees < MinimumAltitudeDegrees) {
                return Invalid(
                    $"apparent altitude {altitudeDegrees:F2} deg is below the {MinimumAltitudeDegrees:F0} deg qualification floor",
                    altitudeDegrees);
            }

            // Convert the physical line of sight at the midpoint into the
            // vacuum equatorial coordinate followed by an aligned mount.
            var mechanicalCoordinate = apparentAtMidpoint.Transform(
                Epoch.J2000,
                0,
                refraction.Temperature,
                0,
                refraction.Wavelength);

            var before = SimulatePlateSolve(
                mechanicalCoordinate,
                latitude,
                longitude,
                elevationMeters,
                refraction,
                observationTimeUtc - interval);
            var after = SimulatePlateSolve(
                mechanicalCoordinate,
                latitude,
                longitude,
                elevationMeters,
                refraction,
                observationTimeUtc + interval);
            var reconstructedMidpoint = SimulatePlateSolve(
                mechanicalCoordinate,
                latitude,
                longitude,
                elevationMeters,
                refraction,
                observationTimeUtc);
            var inputAtJ2000 = solvedCoordinates.Transform(Epoch.J2000);

            var elapsedMinutes = 2 * interval.TotalMinutes;
            var drift = (after.Dec - before.Dec) * 3600 / elapsedMinutes;
            var closure = AngularSeparationArcseconds(inputAtJ2000, reconstructedMidpoint);
            if (!double.IsFinite(drift) || !double.IsFinite(closure)) {
                return Invalid("refraction transform produced a non-finite result", altitudeDegrees);
            }
            if (closure > MaximumClosureArcseconds) {
                return Invalid(
                    $"refraction transform midpoint closure {closure:F3}\" exceeds {MaximumClosureArcseconds:F3}\"",
                    altitudeDegrees,
                    closure);
            }

            return new TppaRefractionDriftResult(
                true,
                drift,
                altitudeDegrees,
                closure,
                string.Empty);
        }

        private static Coordinates SimulatePlateSolve(
            Coordinates mechanicalCoordinate,
            Angle latitude,
            Angle longitude,
            double elevationMeters,
            RefractionParameters refraction,
            DateTime observationTimeUtc) {
            var physicalLineOfSight = mechanicalCoordinate.Transform(
                latitude,
                longitude,
                elevationMeters,
                0,
                refraction.Temperature,
                0,
                refraction.Wavelength,
                observationTimeUtc);
            return physicalLineOfSight.Transform(
                Epoch.J2000,
                refraction.PressureHPa,
                refraction.Temperature,
                refraction.RelativeHumidity,
                refraction.Wavelength);
        }

        private static string ValidateInputs(
            Coordinates coordinates,
            Angle latitude,
            Angle longitude,
            double elevationMeters,
            RefractionParameters refraction,
            DateTime observationTimeUtc,
            TimeSpan halfSpan) {
            if (coordinates == null) return "solved coordinates are required";
            if (observationTimeUtc.Kind != DateTimeKind.Utc) return "observation time must be UTC";
            if (!double.IsFinite(latitude.Degree) || Math.Abs(latitude.Degree) > 90) return "site latitude is invalid";
            if (!double.IsFinite(longitude.Degree) || Math.Abs(longitude.Degree) > 360) return "site longitude is invalid";
            if (!double.IsFinite(elevationMeters) || elevationMeters < -500 || elevationMeters > 10000) return "site elevation is invalid";
            if (refraction == null) return "refraction parameters are required";
            if (!double.IsFinite(refraction.PressureHPa) || refraction.PressureHPa < 500 || refraction.PressureHPa > 1100) return "pressure must be between 500 and 1100 hPa";
            if (!double.IsFinite(refraction.Temperature) || refraction.Temperature < -100 || refraction.Temperature > 100) return "temperature is invalid";
            if (!double.IsFinite(refraction.RelativeHumidity) || refraction.RelativeHumidity < 0 || refraction.RelativeHumidity > 1) return "relative humidity must be between 0 and 1";
            if (!double.IsFinite(refraction.Wavelength) || refraction.Wavelength <= 0) return "wavelength is invalid";
            if (halfSpan <= TimeSpan.Zero || halfSpan > TimeSpan.FromMinutes(5)) return "finite-difference half-span must be greater than zero and no more than five minutes";
            return null;
        }

        private static double AngularSeparationArcseconds(Coordinates first, Coordinates second) {
            var meanDeclinationRadians = 0.5 * (first.Dec + second.Dec) * Math.PI / 180;
            var deltaRaDegrees = NormalizeDegrees(second.RADegrees - first.RADegrees);
            var deltaDecDegrees = second.Dec - first.Dec;
            return Math.Sqrt(
                Math.Pow(deltaRaDegrees * Math.Cos(meanDeclinationRadians), 2)
                + Math.Pow(deltaDecDegrees, 2)) * 3600;
        }

        private static double NormalizeDegrees(double value) => (value + 540) % 360 - 180;

        private static TppaRefractionDriftResult Invalid(
            string reason,
            double altitudeDegrees = double.NaN,
            double closureArcseconds = double.NaN) => new(
                false,
                double.NaN,
                altitudeDegrees,
                closureArcseconds,
                reason);
    }
}
