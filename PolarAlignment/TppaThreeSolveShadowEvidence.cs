using System;
using System.Globalization;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaThreeSolveShadowSample(
        double AzimuthDegrees,
        double PositionAngleDegrees,
        double VectorX,
        double VectorY,
        double VectorZ);

    internal sealed record TppaThreeSolveShadowEvidence(
        bool IsValid,
        double SolveCAzimuthDegrees,
        double ReturnAzimuthDegrees,
        double FieldSeparationDegrees,
        double PositionAngleSeparationDegrees,
        string SolveCNearestAxis,
        string ReturnNearestAxis,
        bool SolveCWithinAxisGate,
        bool ReturnWithinAxisGate,
        bool AxisGateAgreement,
        double MaximumAxisDistanceDegrees,
        bool GrantsFastPathAuthority,
        string Reason) {

        public string ToLogString() =>
            string.Format(
                CultureInfo.InvariantCulture,
                "TPPA_THREE_SOLVE_SHADOW schemaVersion=1; isValid={0}; " +
                "solveCAzimuthDegrees={1:F6}; returnAzimuthDegrees={2:F6}; " +
                "fieldSeparationDegrees={3:F6}; positionAngleSeparationDegrees={4:F6}; " +
                "solveCNearestAxis={5}; returnNearestAxis={6}; " +
                "solveCWithinAxisGate={7}; returnWithinAxisGate={8}; axisGateAgreement={9}; " +
                "maximumAxisDistanceDegrees={10:F3}; grantsFastPathAuthority={11}; reason={12}",
                IsValid,
                SolveCAzimuthDegrees,
                ReturnAzimuthDegrees,
                FieldSeparationDegrees,
                PositionAngleSeparationDegrees,
                SolveCNearestAxis,
                ReturnNearestAxis,
                SolveCWithinAxisGate,
                ReturnWithinAxisGate,
                AxisGateAgreement,
                MaximumAxisDistanceDegrees,
                GrantsFastPathAuthority,
                Reason);
    }

    internal static class TppaThreeSolveShadowEvaluator {
        public const double DefaultMaximumAxisDistanceDegrees = 5.0;

        public static TppaThreeSolveShadowEvidence Evaluate(
                TppaThreeSolveShadowSample solveC,
                TppaThreeSolveShadowSample returnedField,
                double maximumAxisDistanceDegrees = DefaultMaximumAxisDistanceDegrees) {
            if (!IsFinite(solveC)
                    || !IsFinite(returnedField)
                    || !double.IsFinite(maximumAxisDistanceDegrees)
                    || maximumAxisDistanceDegrees < 0
                    || maximumAxisDistanceDegrees > 90) {
                return Invalid(maximumAxisDistanceDegrees, "non-finite or out-of-range shadow input");
            }

            var solveCNorm = Norm(solveC);
            var returnNorm = Norm(returnedField);
            if (solveCNorm <= 1e-12 || returnNorm <= 1e-12) {
                return Invalid(maximumAxisDistanceDegrees, "zero-length shadow field vector");
            }

            var dot = (solveC.VectorX * returnedField.VectorX
                       + solveC.VectorY * returnedField.VectorY
                       + solveC.VectorZ * returnedField.VectorZ)
                      / (solveCNorm * returnNorm);
            dot = Math.Max(-1.0, Math.Min(1.0, dot));
            var fieldSeparationDegrees = Math.Acos(dot) * 180.0 / Math.PI;
            var positionAngleSeparationDegrees = CircularDistance(
                solveC.PositionAngleDegrees,
                returnedField.PositionAngleDegrees);

            var solveCDistanceToEastDegrees = CircularDistance(solveC.AzimuthDegrees, 90.0);
            var solveCDistanceToWestDegrees = CircularDistance(solveC.AzimuthDegrees, 270.0);
            var returnDistanceToEastDegrees = CircularDistance(returnedField.AzimuthDegrees, 90.0);
            var returnDistanceToWestDegrees = CircularDistance(returnedField.AzimuthDegrees, 270.0);
            var solveCNearestAxis = solveCDistanceToEastDegrees <= solveCDistanceToWestDegrees ? "east" : "west";
            var returnNearestAxis = returnDistanceToEastDegrees <= returnDistanceToWestDegrees ? "east" : "west";
            var solveCWithinAxisGate = Math.Min(solveCDistanceToEastDegrees, solveCDistanceToWestDegrees)
                <= maximumAxisDistanceDegrees;
            var returnWithinAxisGate = Math.Min(returnDistanceToEastDegrees, returnDistanceToWestDegrees)
                <= maximumAxisDistanceDegrees;

            return new(
                true,
                NormalizeDegrees(solveC.AzimuthDegrees),
                NormalizeDegrees(returnedField.AzimuthDegrees),
                fieldSeparationDegrees,
                positionAngleSeparationDegrees,
                solveCNearestAxis,
                returnNearestAxis,
                solveCWithinAxisGate,
                returnWithinAxisGate,
                solveCNearestAxis == returnNearestAxis && solveCWithinAxisGate == returnWithinAxisGate,
                maximumAxisDistanceDegrees,
                false,
                "report-only; no field-qualified solve-C reference-frame equivalence policy");
        }

        private static TppaThreeSolveShadowEvidence Invalid(double maximumAxisDistanceDegrees, string reason) =>
            new(
                false,
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                "unknown",
                "unknown",
                false,
                false,
                false,
                maximumAxisDistanceDegrees,
                false,
                reason);

        private static bool IsFinite(TppaThreeSolveShadowSample sample) =>
            double.IsFinite(sample.AzimuthDegrees)
            && double.IsFinite(sample.PositionAngleDegrees)
            && double.IsFinite(sample.VectorX)
            && double.IsFinite(sample.VectorY)
            && double.IsFinite(sample.VectorZ);

        private static double Norm(TppaThreeSolveShadowSample sample) =>
            Math.Sqrt(sample.VectorX * sample.VectorX
                      + sample.VectorY * sample.VectorY
                      + sample.VectorZ * sample.VectorZ);

        private static double CircularDistance(double firstDegrees, double secondDegrees) {
            var difference = Math.Abs(NormalizeDegrees(firstDegrees) - NormalizeDegrees(secondDegrees));
            return Math.Min(difference, 360.0 - difference);
        }

        private static double NormalizeDegrees(double degrees) {
            var normalized = degrees % 360.0;
            return normalized < 0 ? normalized + 360.0 : normalized;
        }
    }
}
