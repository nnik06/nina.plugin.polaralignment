using System;
using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Utility;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaDriftArcPreflightResult(
        TppaDriftArcSafetyResult PositiveRaArc,
        TppaDriftArcSafetyResult NegativeRaArc) {
        public bool IsSafe => PositiveRaArc.IsSafe && NegativeRaArc.IsSafe;
        public TppaDriftArcSafetyResult ForDirection(bool eastDirection) =>
            eastDirection ? PositiveRaArc : NegativeRaArc;
        public string Reason => IsSafe
            ? $"both RA sign interpretations passed; minimum predicted altitudes +RA={PositiveRaArc.MinimumPredictedAltitudeDegrees:F2} deg, -RA={NegativeRaArc.MinimumPredictedAltitudeDegrees:F2} deg"
            : $"drift arc preflight rejected; +RA: {PositiveRaArc.Reason}; -RA: {NegativeRaArc.Reason}";
    }

    internal static class TppaDriftArcPreflightFactory {
        private static readonly TimeSpan TrackDuration = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan MovementAllowance = TimeSpan.FromMinutes(1);

        public static TppaDriftArcPreflightResult Evaluate(
            Coordinates pointA,
            double pointDistanceDegrees,
            DateTime startTimeUtc,
            Angle latitude,
            Angle longitude,
            double elevationMeters,
            RefractionParameters refraction,
            Func<Coordinates, PierSide> destinationSideOfPier) {
            ArgumentNullException.ThrowIfNull(pointA);
            ArgumentNullException.ThrowIfNull(refraction);
            ArgumentNullException.ThrowIfNull(destinationSideOfPier);
            if (!double.IsFinite(pointDistanceDegrees)
                    || pointDistanceDegrees <= 0
                    || pointDistanceDegrees >= 90) {
                throw new ArgumentOutOfRangeException(
                    nameof(pointDistanceDegrees),
                    "drift point distance must be finite and between 0 and 90 degrees");
            }
            if (startTimeUtc.Kind != DateTimeKind.Utc) {
                throw new ArgumentException("drift preflight start time must be UTC", nameof(startTimeUtc));
            }

            return new TppaDriftArcPreflightResult(
                EvaluateSignedArc(
                    pointA,
                    pointDistanceDegrees,
                    startTimeUtc,
                    latitude,
                    longitude,
                    elevationMeters,
                    refraction,
                    destinationSideOfPier),
                EvaluateSignedArc(
                    pointA,
                    -pointDistanceDegrees,
                    startTimeUtc,
                    latitude,
                    longitude,
                    elevationMeters,
                    refraction,
                    destinationSideOfPier));
        }

        private static TppaDriftArcSafetyResult EvaluateSignedArc(
            Coordinates pointA,
            double signedPointDistanceDegrees,
            DateTime startTimeUtc,
            Angle latitude,
            Angle longitude,
            double elevationMeters,
            RefractionParameters refraction,
            Func<Coordinates, PierSide> destinationSideOfPier) {
            var pointBStart = startTimeUtc + TrackDuration + MovementAllowance;
            var pointCStart = pointBStart + TrackDuration + MovementAllowance;
            var returnAStart = pointCStart + TrackDuration + MovementAllowance;
            var candidates = new[] {
                Candidate("A", pointA, startTimeUtc, startTimeUtc + TrackDuration),
                Candidate("B", Offset(pointA, signedPointDistanceDegrees, pointBStart), pointBStart, pointBStart + TrackDuration),
                Candidate("C", Offset(pointA, 2 * signedPointDistanceDegrees, pointCStart), pointCStart, pointCStart + TrackDuration),
                Candidate("A", Offset(pointA, 0, returnAStart), returnAStart, returnAStart + TrackDuration)
            };
            return TppaDriftArcSafetyPolicy.Evaluate(candidates);

            TppaDriftArcCandidate Candidate(
                string positionId,
                Coordinates coordinate,
                DateTime trackStartUtc,
                DateTime trackEndUtc) {
                var startAltitude = ApparentAltitude(coordinate, trackStartUtc);
                var endAltitude = ApparentAltitude(coordinate, trackEndUtc);
                return new TppaDriftArcCandidate(
                    positionId,
                    Math.Min(startAltitude, endAltitude),
                    destinationSideOfPier(coordinate));
            }

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

        private static Coordinates Offset(
            Coordinates source,
            double rightAscensionOffsetDegrees,
            DateTime observationTimeUtc) => new(
                Angle.ByDegree(NormalizeDegrees(source.RADegrees + rightAscensionOffsetDegrees)),
                Angle.ByDegree(source.Dec),
                source.Epoch,
                new FixedObservationDateTime(observationTimeUtc));

        private static double NormalizeDegrees(double value) => (value % 360 + 360) % 360;
    }
}
