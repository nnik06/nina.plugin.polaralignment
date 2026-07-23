using System;
using System.Collections.Generic;
using System.Linq;
using NINA.Core.Enum;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaDriftArcCandidate(
        string PositionId,
        double PredictedAltitudeDegrees,
        PierSide PredictedPierSide);

    internal readonly record struct TppaDriftArcSafetyResult(
        bool IsSafe,
        double MinimumPredictedAltitudeDegrees,
        string Reason);

    internal static class TppaDriftArcSafetyPolicy {
        private static readonly string[] RequiredPositionOrder = { "A", "B", "C", "A" };

        public static TppaDriftArcSafetyResult Evaluate(
            IReadOnlyList<TppaDriftArcCandidate> candidates,
            double minimumAltitudeDegrees = TppaRefractionDriftCalculator.MinimumAltitudeDegrees) {
            if (candidates is null || candidates.Count != RequiredPositionOrder.Length) {
                return Invalid("the predicted drift arc must contain exactly A-B-C-A");
            }
            if (!double.IsFinite(minimumAltitudeDegrees)
                    || minimumAltitudeDegrees < 0
                    || minimumAltitudeDegrees >= 90) {
                return Invalid("the minimum altitude must be finite and between 0 and 90 degrees");
            }

            for (var index = 0; index < RequiredPositionOrder.Length; index++) {
                if (!string.Equals(
                        candidates[index].PositionId,
                        RequiredPositionOrder[index],
                        StringComparison.OrdinalIgnoreCase)) {
                    return Invalid(
                        $"predicted drift position {index + 1} must be {RequiredPositionOrder[index]}");
                }
            }

            if (candidates.Any(candidate => !double.IsFinite(candidate.PredictedAltitudeDegrees))) {
                return Invalid("every predicted drift altitude must be finite");
            }

            var minimumPredictedAltitude = candidates.Min(candidate => candidate.PredictedAltitudeDegrees);
            if (minimumPredictedAltitude < minimumAltitudeDegrees) {
                return new TppaDriftArcSafetyResult(
                    false,
                    minimumPredictedAltitude,
                    $"predicted drift altitude {minimumPredictedAltitude:F2} deg is below the {minimumAltitudeDegrees:F0} deg safety floor");
            }

            var knownPierSides = candidates
                .Where(candidate => candidate.PredictedPierSide != PierSide.pierUnknown)
                .Select(candidate => candidate.PredictedPierSide)
                .Distinct()
                .ToArray();
            if (knownPierSides.Length > 1) {
                return new TppaDriftArcSafetyResult(
                    false,
                    minimumPredictedAltitude,
                    "the predicted A-B-C-A drift arc changes pier side");
            }

            return new TppaDriftArcSafetyResult(
                true,
                minimumPredictedAltitude,
                knownPierSides.Length == 0
                    ? "altitude gate passed; the driver did not provide pier-side predictions"
                    : $"altitude and constant {knownPierSides[0]} pier-side gates passed");

            TppaDriftArcSafetyResult Invalid(string reason) => new(
                false,
                double.NaN,
                reason);
        }
    }
}
