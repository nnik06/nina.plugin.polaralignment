using System;
using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Utility;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaDriftLegPreflightResult(
        bool IsSafe,
        double MinimumPredictedAltitudeDegrees,
        string Reason);

    internal readonly record struct TppaDriftRelativeLegPreflightResult(
        TppaDriftLegPreflightResult PositiveRaDestination,
        TppaDriftLegPreflightResult NegativeRaDestination) {
        public bool IsSafe => PositiveRaDestination.IsSafe && NegativeRaDestination.IsSafe;
        public string Reason => IsSafe
            ? $"both current-time RA sign interpretations passed; minimum predicted altitudes +RA={PositiveRaDestination.MinimumPredictedAltitudeDegrees:F2} deg, -RA={NegativeRaDestination.MinimumPredictedAltitudeDegrees:F2} deg"
            : $"current-time relative-leg preflight rejected; +RA: {PositiveRaDestination.Reason}; -RA: {NegativeRaDestination.Reason}";
    }

    internal static class TppaDriftLegPreflightFactory {
        private static readonly TimeSpan MovementAllowance = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan TrackDuration = TimeSpan.FromMinutes(5);

        public static TppaDriftRelativeLegPreflightResult EvaluateRelative(
            Coordinates current,
            double distanceDegrees,
            DateTime issueTimeUtc,
            Angle latitude,
            Angle longitude,
            double elevationMeters,
            RefractionParameters refraction,
            Func<Coordinates, PierSide> destinationSideOfPier) {
            ValidateCommon(current, issueTimeUtc, refraction, destinationSideOfPier);
            if (!double.IsFinite(distanceDegrees) || distanceDegrees <= 0 || distanceDegrees >= 90) {
                throw new ArgumentOutOfRangeException(
                    nameof(distanceDegrees),
                    "drift leg distance must be finite and between 0 and 90 degrees");
            }

            var currentPierSide = destinationSideOfPier(current);
            return new TppaDriftRelativeLegPreflightResult(
                EvaluateDestination(
                    Offset(current, distanceDegrees, issueTimeUtc + MovementAllowance),
                    currentPierSide,
                    issueTimeUtc,
                    latitude,
                    longitude,
                    elevationMeters,
                    refraction,
                    destinationSideOfPier),
                EvaluateDestination(
                    Offset(current, -distanceDegrees, issueTimeUtc + MovementAllowance),
                    currentPierSide,
                    issueTimeUtc,
                    latitude,
                    longitude,
                    elevationMeters,
                    refraction,
                    destinationSideOfPier));
        }

        public static TppaDriftLegPreflightResult EvaluateAbsolute(
            Coordinates current,
            Coordinates destination,
            DateTime issueTimeUtc,
            Angle latitude,
            Angle longitude,
            double elevationMeters,
            RefractionParameters refraction,
            Func<Coordinates, PierSide> destinationSideOfPier) {
            ValidateCommon(current, issueTimeUtc, refraction, destinationSideOfPier);
            ArgumentNullException.ThrowIfNull(destination);
            return EvaluateDestination(
                WithObservationTime(destination, issueTimeUtc + MovementAllowance),
                destinationSideOfPier(current),
                issueTimeUtc,
                latitude,
                longitude,
                elevationMeters,
                refraction,
                destinationSideOfPier);
        }

        private static TppaDriftLegPreflightResult EvaluateDestination(
            Coordinates destination,
            PierSide currentPierSide,
            DateTime issueTimeUtc,
            Angle latitude,
            Angle longitude,
            double elevationMeters,
            RefractionParameters refraction,
            Func<Coordinates, PierSide> destinationSideOfPier) {
            var trackStart = issueTimeUtc + MovementAllowance;
            var trackEnd = trackStart + TrackDuration;
            var startAltitude = ApparentAltitude(destination, trackStart);
            var endAltitude = ApparentAltitude(destination, trackEnd);
            var minimumAltitude = Math.Min(startAltitude, endAltitude);
            if (!double.IsFinite(minimumAltitude)) {
                return new(false, minimumAltitude, "predicted destination altitude is not finite");
            }
            if (minimumAltitude < TppaRefractionDriftCalculator.MinimumAltitudeDegrees) {
                return new(
                    false,
                    minimumAltitude,
                    $"predicted destination altitude {minimumAltitude:F2} deg is below the {TppaRefractionDriftCalculator.MinimumAltitudeDegrees:F0} deg safety floor");
            }

            var destinationPierSide = destinationSideOfPier(destination);
            if (currentPierSide != PierSide.pierUnknown
                    && destinationPierSide != PierSide.pierUnknown
                    && currentPierSide != destinationPierSide) {
                return new(
                    false,
                    minimumAltitude,
                    $"predicted destination pier side changes from {currentPierSide} to {destinationPierSide}");
            }

            return new(
                true,
                minimumAltitude,
                currentPierSide == PierSide.pierUnknown || destinationPierSide == PierSide.pierUnknown
                    ? "altitude gate passed; pier-side prediction unavailable"
                    : $"altitude and constant {currentPierSide} destination pier-side gates passed");

            double ApparentAltitude(Coordinates coordinate, DateTime observationTimeUtc) =>
                coordinate.Transform(
                    latitude,
                    longitude,
                    elevationMeters,
                    refraction.PressureHPa,
                    refraction.Temperature,
                    refraction.RelativeHumidity,
                    refraction.Wavelength,
                    observationTimeUtc).Altitude.Degree;
        }

        private static void ValidateCommon(
            Coordinates current,
            DateTime issueTimeUtc,
            RefractionParameters refraction,
            Func<Coordinates, PierSide> destinationSideOfPier) {
            ArgumentNullException.ThrowIfNull(current);
            ArgumentNullException.ThrowIfNull(refraction);
            ArgumentNullException.ThrowIfNull(destinationSideOfPier);
            if (issueTimeUtc.Kind != DateTimeKind.Utc) {
                throw new ArgumentException("drift leg issue time must be UTC", nameof(issueTimeUtc));
            }
        }

        private static Coordinates Offset(
            Coordinates source,
            double rightAscensionOffsetDegrees,
            DateTime observationTimeUtc) => new(
                Angle.ByDegree((source.RADegrees + rightAscensionOffsetDegrees) % 360),
                Angle.ByDegree(source.Dec),
                source.Epoch,
                new FixedObservationDateTime(observationTimeUtc));

        private static Coordinates WithObservationTime(
            Coordinates source,
            DateTime observationTimeUtc) => new(
                Angle.ByDegree(source.RADegrees),
                Angle.ByDegree(source.Dec),
                source.Epoch,
                new FixedObservationDateTime(observationTimeUtc));
    }
}
