using System;
using NINA.Astrometry;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaDriftRuntimeObservation(
        TppaDriftSolveSample Sample,
        TppaDriftTrackMetadata Metadata);

    /// <summary>
    /// Converts one timestamped plate solve into the raw drift sample and
    /// position metadata required by report-only drift validation.
    /// </summary>
    internal static class TppaDriftRuntimeMetadataFactory {
        public static TppaDriftRuntimeObservation Create(
            string positionId,
            Coordinates solvedCoordinates,
            Angle latitude,
            Angle longitude,
            double elevationMeters,
            RefractionParameters refraction) {
            if (string.IsNullOrWhiteSpace(positionId)) {
                throw new ArgumentException("A drift position identifier is required.", nameof(positionId));
            }
            ArgumentNullException.ThrowIfNull(solvedCoordinates);
            ArgumentNullException.ThrowIfNull(refraction);

            var observationTimeUtc = solvedCoordinates.DateTime.UtcNow;
            if (observationTimeUtc.Kind != DateTimeKind.Utc) {
                throw new InvalidOperationException("The plate-solve observation time must be UTC.");
            }

            var refractionResult = TppaRefractionDriftCalculator.Evaluate(
                solvedCoordinates,
                latitude,
                longitude,
                elevationMeters,
                refraction,
                observationTimeUtc);
            if (!refractionResult.IsValid) {
                throw new InvalidOperationException(
                    $"Drift metadata at {positionId} failed refraction qualification: {refractionResult.Reason}");
            }

            var vacuumTopocentric = solvedCoordinates.Transform(
                latitude,
                longitude,
                elevationMeters,
                0,
                refraction.Temperature,
                0,
                refraction.Wavelength,
                observationTimeUtc);
            // Topocentric azimuth/altitude are of-date. Use declination in the
            // same frame so catalog precession cannot masquerade as hour angle.
            var solvedAtObservationEpoch = solvedCoordinates.Transform(Epoch.JNOW);
            var hourAngleDegrees = CalculateHourAngleDegrees(
                vacuumTopocentric.Azimuth.Degree,
                vacuumTopocentric.Altitude.Degree,
                latitude.Degree,
                solvedAtObservationEpoch.Dec);

            return new TppaDriftRuntimeObservation(
                new TppaDriftSolveSample(observationTimeUtc, solvedCoordinates.Dec),
                new TppaDriftTrackMetadata(
                    positionId.Trim().ToUpperInvariant(),
                    hourAngleDegrees,
                    refractionResult.ApparentAltitudeDegrees,
                    true,
                    refractionResult.DeclinationDriftArcsecondsPerMinute));
        }

        internal static double CalculateHourAngleDegrees(
            double azimuthDegrees,
            double altitudeDegrees,
            double latitudeDegrees,
            double declinationDegrees) {
            var azimuth = DegreesToRadians(azimuthDegrees);
            var altitude = DegreesToRadians(altitudeDegrees);
            var latitude = DegreesToRadians(latitudeDegrees);
            var declination = DegreesToRadians(declinationDegrees);
            var sinHourAngle = -Math.Sin(azimuth) * Math.Cos(altitude) / Math.Cos(declination);
            var cosHourAngle = (Math.Sin(altitude) - Math.Sin(latitude) * Math.Sin(declination))
                / (Math.Cos(latitude) * Math.Cos(declination));
            return Math.Atan2(sinHourAngle, cosHourAngle) * 180 / Math.PI;
        }

        private static double DegreesToRadians(double value) => value * Math.PI / 180;
    }
}
