using System;
using NINA.Core.Enum;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaDriftMoveVerificationResult(
        bool IsSafe,
        double RightAscensionTravelDegrees,
        double DeclinationTravelDegrees,
        string Reason);

    internal static class TppaDriftMoveVerificationPolicy {
        public const double DefaultMaximumUndertravelDegrees = 1;
        public const double DefaultMaximumOvershootDegrees = 2;
        public const double DefaultMaximumDeclinationTravelDegrees = 0.25;

        public static TppaDriftMoveVerificationResult Evaluate(
            double startRightAscensionDegrees,
            double endRightAscensionDegrees,
            double startDeclinationDegrees,
            double endDeclinationDegrees,
            double expectedRightAscensionTravelDegrees,
            PierSide startPierSide,
            PierSide endPierSide,
            double maximumUndertravelDegrees = DefaultMaximumUndertravelDegrees,
            double maximumOvershootDegrees = DefaultMaximumOvershootDegrees,
            double maximumDeclinationTravelDegrees = DefaultMaximumDeclinationTravelDegrees) {
            if (!AllFinite(
                    startRightAscensionDegrees,
                    endRightAscensionDegrees,
                    startDeclinationDegrees,
                    endDeclinationDegrees,
                    expectedRightAscensionTravelDegrees,
                    maximumUndertravelDegrees,
                    maximumOvershootDegrees,
                    maximumDeclinationTravelDegrees)) {
                return Invalid("all move-verification inputs must be finite");
            }
            if (expectedRightAscensionTravelDegrees <= 0 || expectedRightAscensionTravelDegrees >= 90) {
                return Invalid("expected RA travel must be between 0 and 90 degrees");
            }
            if (maximumUndertravelDegrees < 0
                    || maximumOvershootDegrees < 0
                    || maximumDeclinationTravelDegrees < 0) {
                return Invalid("move-verification tolerances cannot be negative");
            }

            var raTravel = AngularDistance(startRightAscensionDegrees, endRightAscensionDegrees);
            var decTravel = Math.Abs(endDeclinationDegrees - startDeclinationDegrees);
            var minimumTravel = expectedRightAscensionTravelDegrees - maximumUndertravelDegrees;
            var maximumTravel = expectedRightAscensionTravelDegrees + maximumOvershootDegrees;

            if (raTravel < minimumTravel) {
                return Rejected(
                    $"reported RA travel {raTravel:F3} deg is below the {minimumTravel:F3} deg minimum");
            }
            if (raTravel > maximumTravel) {
                return Rejected(
                    $"reported RA travel {raTravel:F3} deg exceeds the {maximumTravel:F3} deg maximum");
            }
            if (decTravel > maximumDeclinationTravelDegrees) {
                return Rejected(
                    $"reported declination travel {decTravel:F3} deg exceeds the {maximumDeclinationTravelDegrees:F3} deg maximum");
            }
            if (startPierSide != PierSide.pierUnknown
                    && endPierSide != PierSide.pierUnknown
                    && startPierSide != endPierSide) {
                return Rejected(
                    $"reported destination pier side changed from {startPierSide} to {endPierSide}");
            }

            var pierSideSummary = startPierSide == PierSide.pierUnknown || endPierSide == PierSide.pierUnknown
                ? "pier-side verification unavailable"
                : $"constant destination pier side {startPierSide}";
            return new TppaDriftMoveVerificationResult(
                true,
                raTravel,
                decTravel,
                $"RA and declination travel gates passed; {pierSideSummary}");

            TppaDriftMoveVerificationResult Rejected(string reason) =>
                new(false, raTravel, decTravel, reason);
            TppaDriftMoveVerificationResult Invalid(string reason) =>
                new(false, double.NaN, double.NaN, reason);
        }

        private static double AngularDistance(double firstDegrees, double secondDegrees) =>
            180 - Math.Abs(Math.Abs(Normalize(firstDegrees) - Normalize(secondDegrees)) - 180);

        private static double Normalize(double degrees) => (degrees % 360 + 360) % 360;

        private static bool AllFinite(params double[] values) =>
            Array.TrueForAll(values, double.IsFinite);
    }
}
