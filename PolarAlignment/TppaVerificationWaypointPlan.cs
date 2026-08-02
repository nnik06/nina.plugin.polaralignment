using System;
using System.Collections.Generic;
using NINA.Astrometry;
using NINA.Core.Utility;

namespace NINA.Plugins.PolarAlignment {
    internal sealed class TppaVerificationWaypointPlan {
        private TppaVerificationWaypointPlan(
                Coordinates pointA,
                Coordinates pointB,
                Coordinates pointC,
                Coordinates pointAB,
                Coordinates pointBC) {
            Forward = new[] { pointA, pointB, pointC };
            Reciprocal = new[] { pointC, pointB, pointA };
            ReciprocalModelCheck = new[] { pointC, pointBC, pointB, pointAB, pointA };
        }

        public IReadOnlyList<Coordinates> Forward { get; }
        public IReadOnlyList<Coordinates> Reciprocal { get; }
        public IReadOnlyList<Coordinates> ReciprocalModelCheck { get; }

        public static TppaVerificationWaypointPlan Create(
                Coordinates pointA,
                double legDistanceDegrees,
                bool eastDirection) {
            ArgumentNullException.ThrowIfNull(pointA);
            if (!double.IsFinite(pointA.RADegrees)
                    || !double.IsFinite(pointA.Dec)
                    || !double.IsFinite(legDistanceDegrees)
                    || legDistanceDegrees <= 0
                    || legDistanceDegrees >= 90) {
                throw new ArgumentOutOfRangeException(
                    nameof(legDistanceDegrees),
                    "Verification waypoints require finite coordinates and a leg between 0 and 90 degrees.");
            }

            var signedLeg = eastDirection ? legDistanceDegrees : -legDistanceDegrees;
            var observationTimeUtc = pointA.DateTime.UtcNow;
            if (observationTimeUtc.Kind != DateTimeKind.Utc) {
                observationTimeUtc = observationTimeUtc.ToUniversalTime();
            }

            Coordinates Offset(double rightAscensionOffsetDegrees) => new(
                Angle.ByDegree(NormalizeDegrees(pointA.RADegrees + rightAscensionOffsetDegrees)),
                Angle.ByDegree(pointA.Dec),
                pointA.Epoch,
                new FixedObservationDateTime(observationTimeUtc));

            return new TppaVerificationWaypointPlan(
                Offset(0),
                Offset(signedLeg),
                Offset(signedLeg * 2),
                Offset(signedLeg * 0.5),
                Offset(signedLeg * 1.5));
        }

        private static double NormalizeDegrees(double degrees) =>
            (degrees % 360 + 360) % 360;
    }
}
