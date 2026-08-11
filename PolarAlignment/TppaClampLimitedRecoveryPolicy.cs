using System;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaClampLimitedRecoveryCommand(
        bool IsEligible,
        bool ClampLimitedX,
        bool ClampLimitedY,
        double XUnits,
        double YUnits,
        string Reason);

    /// <summary>
    /// Preserves damping near target while allowing an already bounded, field-qualified axis
    /// to consume its whole authorized travel when the inverse command exceeds that travel.
    /// </summary>
    internal static class TppaClampLimitedRecoveryPolicy {
        public const double MaximumResponseConditionNumber = 5.0;
        public const double MaximumRelativeResponseUncertainty = 0.10;
        public const double DampedCorrectionGain = 0.65;
        public const double ClampEngagementFraction = 0.95;

        public static TppaClampLimitedRecoveryCommand Evaluate(
            double rawXUnits,
            double rawYUnits,
            double maximumXUnits,
            double maximumYUnits,
            double responseConditionNumber,
            double relativeResponseUncertainty) {
            if (!double.IsFinite(rawXUnits) || !double.IsFinite(rawYUnits)
                || !double.IsFinite(maximumXUnits) || !double.IsFinite(maximumYUnits)
                || !double.IsFinite(responseConditionNumber) || !double.IsFinite(relativeResponseUncertainty)
                || maximumXUnits <= 0 || maximumYUnits <= 0) {
                return Deny("recovery inputs must be finite with positive per-axis limits");
            }

            if (responseConditionNumber > MaximumResponseConditionNumber
                || relativeResponseUncertainty > MaximumRelativeResponseUncertainty
                || relativeResponseUncertainty < 0) {
                return Deny("the field response model is not qualified for clamp-limited recovery");
            }

            // The inverse uses a small numerical damping term. Treat an axis that is within
            // five percent of its hard maximum as clamp-limited so the feasibility model and
            // runtime do not disagree at the recovery boundary.
            var clampLimitedX = Math.Abs(rawXUnits) >= maximumXUnits * ClampEngagementFraction;
            var clampLimitedY = Math.Abs(rawYUnits) >= maximumYUnits * ClampEngagementFraction;
            if (!clampLimitedX && !clampLimitedY) {
                return Deny("no inverse-command axis is clamp-limited");
            }

            return new(
                true,
                clampLimitedX,
                clampLimitedY,
                Clamp(clampLimitedX ? rawXUnits : rawXUnits * DampedCorrectionGain, maximumXUnits),
                Clamp(clampLimitedY ? rawYUnits : rawYUnits * DampedCorrectionGain, maximumYUnits),
                "per-axis clamp-limited recovery");
        }

        private static TppaClampLimitedRecoveryCommand Deny(string reason) => new(false, false, false, 0, 0, reason);

        private static double Clamp(double value, double maximum) =>
            Math.Sign(value) * Math.Min(Math.Abs(value), maximum);
    }
}
