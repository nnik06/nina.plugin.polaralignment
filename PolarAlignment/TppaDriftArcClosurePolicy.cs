using System;
using NINA.Core.Enum;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaDriftArcClosureResult(
        bool IsSafe,
        double PointingSeparationDegrees,
        string Reason);

    internal static class TppaDriftArcClosurePolicy {
        public const double DefaultMaximumPointingSeparationDegrees = 0.25;

        public static TppaDriftArcClosureResult Evaluate(
            double expectedRightAscensionDegrees,
            double expectedDeclinationDegrees,
            double actualRightAscensionDegrees,
            double actualDeclinationDegrees,
            PierSide expectedPierSide,
            PierSide actualPierSide,
            double maximumPointingSeparationDegrees =
                DefaultMaximumPointingSeparationDegrees) {
            if (!AllFinite(
                    expectedRightAscensionDegrees,
                    expectedDeclinationDegrees,
                    actualRightAscensionDegrees,
                    actualDeclinationDegrees,
                    maximumPointingSeparationDegrees)) {
                return Invalid("all closure-verification inputs must be finite");
            }
            if (expectedDeclinationDegrees < -90
                    || expectedDeclinationDegrees > 90
                    || actualDeclinationDegrees < -90
                    || actualDeclinationDegrees > 90) {
                return Invalid("declination must be between -90 and 90 degrees");
            }
            if (maximumPointingSeparationDegrees < 0
                    || maximumPointingSeparationDegrees >= 90) {
                return Invalid(
                    "maximum pointing separation must be between 0 and 90 degrees");
            }

            var separation = GreatCircleDistance(
                expectedRightAscensionDegrees,
                expectedDeclinationDegrees,
                actualRightAscensionDegrees,
                actualDeclinationDegrees);
            if (separation > maximumPointingSeparationDegrees) {
                return new TppaDriftArcClosureResult(
                    false,
                    separation,
                    $"return pointing separation {separation:F4} deg exceeds the {maximumPointingSeparationDegrees:F3} deg maximum");
            }
            if (expectedPierSide != PierSide.pierUnknown
                    && actualPierSide != PierSide.pierUnknown
                    && expectedPierSide != actualPierSide) {
                return new TppaDriftArcClosureResult(
                    false,
                    separation,
                    $"return destination pier side changed from {expectedPierSide} to {actualPierSide}");
            }

            var pierSideSummary =
                expectedPierSide == PierSide.pierUnknown || actualPierSide == PierSide.pierUnknown
                    ? "pier-side verification unavailable"
                    : $"constant destination pier side {expectedPierSide}";
            return new TppaDriftArcClosureResult(
                true,
                separation,
                $"return pointing gate passed; {pierSideSummary}");
        }

        private static double GreatCircleDistance(
            double firstRaDegrees,
            double firstDecDegrees,
            double secondRaDegrees,
            double secondDecDegrees) {
            var firstDec = DegreesToRadians(firstDecDegrees);
            var secondDec = DegreesToRadians(secondDecDegrees);
            var deltaRa = DegreesToRadians(secondRaDegrees - firstRaDegrees);
            var cosine = Math.Sin(firstDec) * Math.Sin(secondDec)
                + Math.Cos(firstDec) * Math.Cos(secondDec) * Math.Cos(deltaRa);
            return Math.Acos(Math.Clamp(cosine, -1, 1)) * 180 / Math.PI;
        }

        private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;

        private static bool AllFinite(params double[] values) =>
            Array.TrueForAll(values, double.IsFinite);

        private static TppaDriftArcClosureResult Invalid(string reason) =>
            new(false, double.NaN, reason);
    }
}
