using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaSolvedPointing(
        int Attempt,
        double SearchRadiusDegrees,
        double MountRightAscensionDegrees,
        double MountDeclinationDegrees,
        double SolvedRightAscensionDegrees,
        double SolvedDeclinationDegrees);

    internal readonly record struct TppaSolveConsistencyQualification(
        bool IsQualified,
        double MinimumResidualDegrees,
        double MaximumResidualDegrees,
        IReadOnlyList<string> Issues) {
        public string Reason => IsQualified
            ? "three-point solve-attempt and pointing-residual gates passed"
            : string.Join(" ", Issues);
    }

    /// <summary>
    /// Qualifies whether a completed three-point solve is safe to use as an actuator input.
    /// It deliberately does not change measurement-only reporting.
    /// </summary>
    internal static class TppaSolveConsistencyQualificationPolicy {
        public const int RequiredSolvedPoints = 3;
        public const int MaximumAuthorizedAttempt = 1;
        public const double MaximumResidualSpreadDegrees = 1.0;
        public const double MaximumResidualDegrees = 10.0;

        public static TppaSolveConsistencyQualification Evaluate(
                IReadOnlyCollection<TppaSolvedPointing> points) {
            var issues = new List<string>();
            if (points == null || points.Count != RequiredSolvedPoints) {
                issues.Add($"exactly {RequiredSolvedPoints} successful solve points are required for actuator qualification.");
                return new(false, double.NaN, double.NaN, issues);
            }

            if (points.Any(point => point.Attempt > MaximumAuthorizedAttempt)) {
                issues.Add("one or more solve points required a retry; retry-escalated solves are measurement-only.");
            }
            var residuals = points.Select(ResidualDegrees).ToArray();
            if (residuals.Any(value => !double.IsFinite(value))) {
                issues.Add("one or more mount-to-solve residuals are non-finite.");
                return new(false, double.NaN, double.NaN, issues);
            }
            var minimum = residuals.Min();
            var maximum = residuals.Max();
            if (maximum > MaximumResidualDegrees) {
                issues.Add($"mount-to-solve residual {maximum:F2} degrees exceeds the {MaximumResidualDegrees:F2}-degree coordinate-consistency ceiling.");
            }
            if (maximum - minimum > MaximumResidualSpreadDegrees) {
                issues.Add($"mount-to-solve residual spread {maximum - minimum:F2} degrees exceeds the {MaximumResidualSpreadDegrees:F2}-degree actuator floor.");
            }
            return new(issues.Count == 0, minimum, maximum, issues);
        }

        private static double ResidualDegrees(TppaSolvedPointing point) {
            if (!double.IsFinite(point.MountRightAscensionDegrees)
                    || !double.IsFinite(point.MountDeclinationDegrees)
                    || !double.IsFinite(point.SolvedRightAscensionDegrees)
                    || !double.IsFinite(point.SolvedDeclinationDegrees)) {
                return double.NaN;
            }
            var mountRa = DegreesToRadians(point.MountRightAscensionDegrees);
            var mountDec = DegreesToRadians(point.MountDeclinationDegrees);
            var solvedRa = DegreesToRadians(point.SolvedRightAscensionDegrees);
            var solvedDec = DegreesToRadians(point.SolvedDeclinationDegrees);
            var cosine = Math.Sin(mountDec) * Math.Sin(solvedDec)
                + Math.Cos(mountDec) * Math.Cos(solvedDec) * Math.Cos(mountRa - solvedRa);
            return RadiansToDegrees(Math.Acos(Math.Clamp(cosine, -1.0, 1.0)));
        }

        private static double DegreesToRadians(double value) => value * Math.PI / 180.0;
        private static double RadiansToDegrees(double value) => value * 180.0 / Math.PI;
    }
}
