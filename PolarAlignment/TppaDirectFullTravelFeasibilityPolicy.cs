using System;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaDirectFullTravelFeasibilityDecision(
        bool IsFeasible,
        double RequiredXUnits,
        double RequiredYUnits,
        int RequiredMoveCount,
        double RequiredRuntimeSeconds,
        string Reason,
        double MinimumXDisplacementUnits = 0,
        double MaximumXDisplacementUnits = 0,
        double MinimumYDisplacementUnits = 0,
        double MaximumYDisplacementUnits = 0);

    /// <summary>
    /// Pure feasibility check for the attended direct full-travel route. It deliberately does
    /// not plan or execute a correction. A later actuator integration must still apply its
    /// travel, settle, cancellation, and fresh-determination gates for every move.
    /// </summary>
    internal static class TppaDirectFullTravelFeasibilityPolicy {
        // Keep the full-travel feasibility model aligned with the direct
        // runtime contract: the first move consumes an initial fresh pair and
        // later moves reuse accepted fresh feedback as their first agreement
        // sample, allowing three bounded feedback moves in five minutes.
        internal const int MaximumFeedbackMoves =
            TppaFastAlignmentExecutionBudget.MaximumFreshFeedbackMoves;
        internal const double MaximumAxisErrorMinutes = 324.0;
        internal const double MaximumTotalErrorMinutes = 458.205195;
        internal const double DefaultTerminalErrorMinutes =
            TppaOperationalAlignmentTierPolicy.ImagingReadyMaximumTotalMinutes;
        private const double NormalEquationDamping = 1e-6;
        private const double ConfirmedCorrectionGain = 0.65;

        public static TppaDirectFullTravelFeasibilityDecision Evaluate(
            double azimuthErrorMinutes,
            double altitudeErrorMinutes,
            double azimuthDeltaPerXUnitDegrees,
            double azimuthDeltaPerYUnitDegrees,
            double altitudeDeltaPerXUnitDegrees,
            double altitudeDeltaPerYUnitDegrees,
            double maximumXUnitsPerMove,
            double maximumYUnitsPerMove,
            double initialAgreementSeconds,
            double perMoveFreshFeedbackSeconds,
            double terminalConfirmationSeconds,
            double perMoveOverheadSeconds,
            double maximumRuntimeSeconds = TppaFastAlignmentExecutionBudget.MaximumRuntimeSeconds,
            bool clampLimitedRecoveryEnabled = false,
            double relativeResponseUncertainty = double.PositiveInfinity,
            double terminalErrorMinutes = DefaultTerminalErrorMinutes) {
            if (!AreFinite(
                    azimuthErrorMinutes,
                    altitudeErrorMinutes,
                    azimuthDeltaPerXUnitDegrees,
                    azimuthDeltaPerYUnitDegrees,
                    altitudeDeltaPerXUnitDegrees,
                    altitudeDeltaPerYUnitDegrees,
                    maximumXUnitsPerMove,
                    maximumYUnitsPerMove,
                    initialAgreementSeconds,
                    perMoveFreshFeedbackSeconds,
                    terminalConfirmationSeconds,
                    perMoveOverheadSeconds,
                    maximumRuntimeSeconds,
                    terminalErrorMinutes)) {
                return Deny("all feasibility inputs must be finite");
            }

            var tier = TppaOperationalAlignmentTierPolicy.Evaluate(terminalErrorMinutes);
            if (tier.Tier == TppaOperationalAlignmentTier.NonOperational) {
                return Deny("the requested direct-route terminal target is not an operational TPPA tier");
            }

            var totalMinutes = Math.Sqrt(
                azimuthErrorMinutes * azimuthErrorMinutes
                + altitudeErrorMinutes * altitudeErrorMinutes);
            if (Math.Abs(azimuthErrorMinutes) > MaximumAxisErrorMinutes
                || Math.Abs(altitudeErrorMinutes) > MaximumAxisErrorMinutes
                || totalMinutes > MaximumTotalErrorMinutes) {
                return Deny("the fresh error vector is outside the direct full-travel envelope");
            }

            if (maximumXUnitsPerMove <= 0
                || maximumYUnitsPerMove <= 0
                || initialAgreementSeconds <= 0
                || perMoveFreshFeedbackSeconds <= 0
                || terminalConfirmationSeconds <= 0
                || perMoveOverheadSeconds < 0
                || maximumRuntimeSeconds <= 0) {
                return Deny("calibrated authority, timing, and bounded command inputs must be positive");
            }

            var azimuthResidualDegrees = azimuthErrorMinutes / 60.0;
            var altitudeResidualDegrees = altitudeErrorMinutes / 60.0;
            var requiredXUnits = 0.0;
            var requiredYUnits = 0.0;
            var cumulativeXUnits = 0.0;
            var cumulativeYUnits = 0.0;
            var minimumXDisplacementUnits = 0.0;
            var maximumXDisplacementUnits = 0.0;
            var minimumYDisplacementUnits = 0.0;
            var maximumYDisplacementUnits = 0.0;
            var requiredMoves = 0;
            while (ResidualMinutes(azimuthResidualDegrees, altitudeResidualDegrees) > terminalErrorMinutes
                   && requiredMoves <= MaximumFeedbackMoves) {
                if (!TryCreateRuntimeEquivalentCommand(
                        azimuthDeltaPerXUnitDegrees,
                        azimuthDeltaPerYUnitDegrees,
                        altitudeDeltaPerXUnitDegrees,
                        altitudeDeltaPerYUnitDegrees,
                        azimuthResidualDegrees,
                        altitudeResidualDegrees,
                        maximumXUnitsPerMove,
                        maximumYUnitsPerMove,
                        clampLimitedRecoveryEnabled,
                        relativeResponseUncertainty,
                        out var xMagnitude,
                        out var yMagnitude)) {
                    return Deny("the measured 2x2 response cannot produce a bounded direct correction");
                }

                var priorResidualMinutes = ResidualMinutes(azimuthResidualDegrees, altitudeResidualDegrees);
                azimuthResidualDegrees += azimuthDeltaPerXUnitDegrees * xMagnitude
                                           + azimuthDeltaPerYUnitDegrees * yMagnitude;
                altitudeResidualDegrees += altitudeDeltaPerXUnitDegrees * xMagnitude
                                            + altitudeDeltaPerYUnitDegrees * yMagnitude;
                requiredXUnits += Math.Abs(xMagnitude);
                requiredYUnits += Math.Abs(yMagnitude);
                cumulativeXUnits += xMagnitude;
                cumulativeYUnits += yMagnitude;
                minimumXDisplacementUnits = Math.Min(minimumXDisplacementUnits, cumulativeXUnits);
                maximumXDisplacementUnits = Math.Max(maximumXDisplacementUnits, cumulativeXUnits);
                minimumYDisplacementUnits = Math.Min(minimumYDisplacementUnits, cumulativeYUnits);
                maximumYDisplacementUnits = Math.Max(maximumYDisplacementUnits, cumulativeYUnits);
                requiredMoves++;
                if (ResidualMinutes(azimuthResidualDegrees, altitudeResidualDegrees) >= priorResidualMinutes) {
                    return Deny("the calibrated damped controller does not reduce the fresh error vector");
                }
            }
            var requiredRuntimeSeconds = initialAgreementSeconds
                + requiredMoves * (perMoveOverheadSeconds + perMoveFreshFeedbackSeconds)
                + terminalConfirmationSeconds;

            if (requiredMoves > MaximumFeedbackMoves) {
                return new(
                    false,
                    requiredXUnits,
                    requiredYUnits,
                    requiredMoves,
                    requiredRuntimeSeconds,
                    $"the calibrated damped controller requires {requiredMoves} fresh-feedback moves; the direct route permits at most {MaximumFeedbackMoves}",
                    minimumXDisplacementUnits, maximumXDisplacementUnits,
                    minimumYDisplacementUnits, maximumYDisplacementUnits);
            }

            if (ResidualMinutes(azimuthResidualDegrees, altitudeResidualDegrees) > terminalErrorMinutes) {
                return new(
                    false,
                    requiredXUnits,
                    requiredYUnits,
                    requiredMoves,
                    requiredRuntimeSeconds,
                    $"the calibrated damped controller cannot reach {terminalErrorMinutes:F1} arcmin within {MaximumFeedbackMoves} fresh-feedback moves",
                    minimumXDisplacementUnits, maximumXDisplacementUnits,
                    minimumYDisplacementUnits, maximumYDisplacementUnits);
            }

            if (requiredRuntimeSeconds > maximumRuntimeSeconds) {
                return new(
                    false,
                    requiredXUnits,
                    requiredYUnits,
                    requiredMoves,
                    requiredRuntimeSeconds,
                    $"calibrated route requires {requiredRuntimeSeconds:F1}s, exceeding the {maximumRuntimeSeconds:F1}s runtime contract",
                    minimumXDisplacementUnits, maximumXDisplacementUnits,
                    minimumYDisplacementUnits, maximumYDisplacementUnits);
            }

            return new(
                true,
                requiredXUnits,
                requiredYUnits,
                requiredMoves,
                requiredRuntimeSeconds,
                "calibrated damped route reaches the terminal error target within the bounded feedback and runtime contract",
                minimumXDisplacementUnits, maximumXDisplacementUnits,
                minimumYDisplacementUnits, maximumYDisplacementUnits);
        }

        private static TppaDirectFullTravelFeasibilityDecision Deny(string reason) {
            return new(false, 0, 0, 0, 0, reason);
        }

        private static bool TryCreateRuntimeEquivalentCommand(
            double azimuthDeltaPerXUnit,
            double azimuthDeltaPerYUnit,
            double altitudeDeltaPerXUnit,
            double altitudeDeltaPerYUnit,
            double azimuthErrorDegrees,
            double altitudeErrorDegrees,
            double maximumXUnits,
            double maximumYUnits,
            bool clampLimitedRecoveryEnabled,
            double relativeResponseUncertainty,
            out double xMagnitude,
            out double yMagnitude) {
            var m00 = azimuthDeltaPerXUnit * azimuthDeltaPerXUnit
                      + altitudeDeltaPerXUnit * altitudeDeltaPerXUnit
                      + NormalEquationDamping;
            var m01 = azimuthDeltaPerXUnit * azimuthDeltaPerYUnit
                      + altitudeDeltaPerXUnit * altitudeDeltaPerYUnit;
            var m11 = azimuthDeltaPerYUnit * azimuthDeltaPerYUnit
                      + altitudeDeltaPerYUnit * altitudeDeltaPerYUnit
                      + NormalEquationDamping;
            var rhs0 = -(azimuthDeltaPerXUnit * azimuthErrorDegrees
                         + altitudeDeltaPerXUnit * altitudeErrorDegrees);
            var rhs1 = -(azimuthDeltaPerYUnit * azimuthErrorDegrees
                         + altitudeDeltaPerYUnit * altitudeErrorDegrees);
            var determinant = m00 * m11 - m01 * m01;
            if (Math.Abs(determinant) <= NormalEquationDamping) {
                xMagnitude = 0;
                yMagnitude = 0;
                return false;
            }

            var rawX = ((rhs0 * m11) - (rhs1 * m01)) / determinant;
            var rawY = ((m00 * rhs1) - (m01 * rhs0)) / determinant;
            var recovery = clampLimitedRecoveryEnabled
                ? TppaClampLimitedRecoveryPolicy.Evaluate(
                    rawX,
                    rawY,
                    maximumXUnits,
                    maximumYUnits,
                    CalculateConditionNumber(
                        azimuthDeltaPerXUnit,
                        altitudeDeltaPerXUnit,
                        azimuthDeltaPerYUnit,
                        altitudeDeltaPerYUnit),
                    relativeResponseUncertainty)
                : null;
            xMagnitude = recovery?.IsEligible == true
                ? recovery.XUnits
                : Clamp(rawX * ConfirmedCorrectionGain, maximumXUnits);
            yMagnitude = recovery?.IsEligible == true
                ? recovery.YUnits
                : Clamp(rawY * ConfirmedCorrectionGain, maximumYUnits);
            return true;
        }

        private static double CalculateConditionNumber(double ax, double ay, double bx, double by) {
            var trace = ax * ax + ay * ay + bx * bx + by * by;
            var determinant = ax * by - ay * bx;
            var discriminant = Math.Sqrt(Math.Max(0, trace * trace - 4 * determinant * determinant));
            var minimumEigenvalue = (trace - discriminant) / 2;
            var maximumEigenvalue = (trace + discriminant) / 2;
            return minimumEigenvalue <= 0 ? double.PositiveInfinity : Math.Sqrt(maximumEigenvalue / minimumEigenvalue);
        }

        private static double Clamp(double magnitude, double maximumMagnitude) {
            return Math.Sign(magnitude) * Math.Min(Math.Abs(magnitude), maximumMagnitude);
        }

        private static double ResidualMinutes(double azimuthDegrees, double altitudeDegrees) {
            return Math.Sqrt(azimuthDegrees * azimuthDegrees + altitudeDegrees * altitudeDegrees) * 60.0;
        }

        private static bool AreFinite(params double[] values) {
            foreach (var value in values) {
                if (double.IsNaN(value) || double.IsInfinity(value)) {
                    return false;
                }
            }

            return true;
        }
    }
}
