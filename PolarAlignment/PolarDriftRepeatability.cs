using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct PolarDriftRepeatabilityPolicy(
        int MinimumStableEstimates,
        double MaximumMagnitudeDifferenceFloorArcMinutes,
        double MaximumMagnitudeDifferenceFraction,
        double MaximumDirectionDifferenceDegrees) {
        public static PolarDriftRepeatabilityPolicy FieldDefault => new(3, 0.5, 0.15, 7.5);
    }

    internal readonly record struct PolarDriftRepeatabilityResult(
        int EstimateCount,
        double MeanPolarErrorArcMinutes,
        double MaximumMagnitudeDifferenceArcMinutes,
        double MaximumDirectionDifferenceDegrees,
        bool IsRepeatable,
        string Reason);

    internal static class PolarDriftRepeatabilityEvaluator {
        public static PolarDriftRepeatabilityResult Evaluate(
            IReadOnlyList<PolarDriftEstimate> estimates,
            PolarDriftRepeatabilityPolicy? policy = null) {
            var activePolicy = policy ?? PolarDriftRepeatabilityPolicy.FieldDefault;
            if (estimates is null || estimates.Count < activePolicy.MinimumStableEstimates) {
                return Invalid(estimates?.Count ?? 0, $"at least {activePolicy.MinimumStableEstimates} independent stable captures are required");
            }
            if (estimates.Any(estimate => !estimate.IsStable)) {
                return Invalid(estimates.Count, "at least one capture failed its within-capture stability gate");
            }
            if (estimates.Any(estimate => !double.IsFinite(estimate.PolarErrorArcMinutes)
                    || !double.IsFinite(estimate.Phd2DisplayAngleDegrees))) {
                return Invalid(estimates.Count, "capture magnitude and direction must be finite");
            }

            var meanMagnitude = estimates.Average(estimate => estimate.PolarErrorArcMinutes);
            var maximumMagnitudeDifference = 0.0;
            var maximumDirectionDifference = 0.0;
            for (var left = 0; left < estimates.Count; left++) {
                for (var right = left + 1; right < estimates.Count; right++) {
                    maximumMagnitudeDifference = Math.Max(maximumMagnitudeDifference,
                        Math.Abs(estimates[left].PolarErrorArcMinutes - estimates[right].PolarErrorArcMinutes));
                    maximumDirectionDifference = Math.Max(maximumDirectionDifference,
                        CircularDifferenceDegrees(estimates[left].Phd2DisplayAngleDegrees,
                            estimates[right].Phd2DisplayAngleDegrees));
                }
            }

            var magnitudeLimit = Math.Max(activePolicy.MaximumMagnitudeDifferenceFloorArcMinutes,
                meanMagnitude * activePolicy.MaximumMagnitudeDifferenceFraction);
            if (maximumMagnitudeDifference > magnitudeLimit) {
                return new(estimates.Count, meanMagnitude, maximumMagnitudeDifference, maximumDirectionDifference,
                    false, $"cross-capture magnitude disagreement {maximumMagnitudeDifference:F3}' exceeds {magnitudeLimit:F3}'");
            }
            if (maximumDirectionDifference > activePolicy.MaximumDirectionDifferenceDegrees) {
                return new(estimates.Count, meanMagnitude, maximumMagnitudeDifference, maximumDirectionDifference,
                    false, $"cross-capture direction disagreement {maximumDirectionDifference:F2} deg exceeds {activePolicy.MaximumDirectionDifferenceDegrees:F2} deg");
            }
            return new(estimates.Count, meanMagnitude, maximumMagnitudeDifference, maximumDirectionDifference,
                true, "independent captures passed magnitude and direction repeatability gates");
        }

        private static PolarDriftRepeatabilityResult Invalid(int count, string reason) =>
            new(count, double.NaN, double.NaN, double.NaN, false, reason);

        private static double CircularDifferenceDegrees(double first, double second) {
            var difference = Math.Abs(first - second) % 360.0;
            return difference > 180.0 ? 360.0 - difference : difference;
        }
    }
}
