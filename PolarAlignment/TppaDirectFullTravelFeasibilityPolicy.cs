using System;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaDirectFullTravelFeasibilityDecision(
        bool IsFeasible,
        double RequiredXUnits,
        double RequiredYUnits,
        int RequiredMoveCount,
        double RequiredRuntimeSeconds,
        string Reason);

    /// <summary>
    /// Pure feasibility check for the attended direct full-travel route. It deliberately does
    /// not plan or execute a correction. A later actuator integration must still apply its
    /// travel, settle, cancellation, and fresh-determination gates for every move.
    /// </summary>
    internal static class TppaDirectFullTravelFeasibilityPolicy {
        internal const int MaximumFeedbackMoves = 2;
        internal const double MaximumAxisErrorMinutes = 324.0;
        internal const double MaximumTotalErrorMinutes = 458.205195;

        public static TppaDirectFullTravelFeasibilityDecision Evaluate(
            double azimuthErrorMinutes,
            double altitudeErrorMinutes,
            double azimuthMinutesCorrectedPerXUnit,
            double altitudeMinutesCorrectedPerYUnit,
            double maximumXUnitsPerMove,
            double maximumYUnitsPerMove,
            double initialAgreementSeconds,
            double perMoveFreshFeedbackSeconds,
            double terminalConfirmationSeconds,
            double perMoveOverheadSeconds,
            double maximumRuntimeSeconds = TppaFastAlignmentExecutionBudget.MaximumRuntimeSeconds) {
            if (!AreFinite(
                    azimuthErrorMinutes,
                    altitudeErrorMinutes,
                    azimuthMinutesCorrectedPerXUnit,
                    altitudeMinutesCorrectedPerYUnit,
                    maximumXUnitsPerMove,
                    maximumYUnitsPerMove,
                    initialAgreementSeconds,
                    perMoveFreshFeedbackSeconds,
                    terminalConfirmationSeconds,
                    perMoveOverheadSeconds,
                    maximumRuntimeSeconds)) {
                return Deny("all feasibility inputs must be finite");
            }

            var totalMinutes = Math.Sqrt(
                azimuthErrorMinutes * azimuthErrorMinutes
                + altitudeErrorMinutes * altitudeErrorMinutes);
            if (Math.Abs(azimuthErrorMinutes) > MaximumAxisErrorMinutes
                || Math.Abs(altitudeErrorMinutes) > MaximumAxisErrorMinutes
                || totalMinutes > MaximumTotalErrorMinutes) {
                return Deny("the fresh error vector is outside the direct full-travel envelope");
            }

            if (azimuthMinutesCorrectedPerXUnit <= 0
                || altitudeMinutesCorrectedPerYUnit <= 0
                || maximumXUnitsPerMove <= 0
                || maximumYUnitsPerMove <= 0
                || initialAgreementSeconds <= 0
                || perMoveFreshFeedbackSeconds <= 0
                || terminalConfirmationSeconds <= 0
                || perMoveOverheadSeconds < 0
                || maximumRuntimeSeconds <= 0) {
                return Deny("calibrated authority, timing, and bounded command inputs must be positive");
            }

            var requiredXUnits = Math.Abs(azimuthErrorMinutes) / azimuthMinutesCorrectedPerXUnit;
            var requiredYUnits = Math.Abs(altitudeErrorMinutes) / altitudeMinutesCorrectedPerYUnit;
            var requiredMoves = Math.Max(
                CeilingRatio(requiredXUnits, maximumXUnitsPerMove),
                CeilingRatio(requiredYUnits, maximumYUnitsPerMove));
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
                    $"calibrated authority requires {requiredMoves} fresh-feedback moves; the five-minute contract permits at most {MaximumFeedbackMoves}");
            }

            if (requiredRuntimeSeconds > maximumRuntimeSeconds) {
                return new(
                    false,
                    requiredXUnits,
                    requiredYUnits,
                    requiredMoves,
                    requiredRuntimeSeconds,
                    $"calibrated route requires {requiredRuntimeSeconds:F1}s, exceeding the {maximumRuntimeSeconds:F1}s runtime contract");
            }

            return new(
                true,
                requiredXUnits,
                requiredYUnits,
                requiredMoves,
                requiredRuntimeSeconds,
                "calibrated route fits the bounded full-travel feedback and runtime contract");
        }

        private static TppaDirectFullTravelFeasibilityDecision Deny(string reason) {
            return new(false, 0, 0, 0, 0, reason);
        }

        private static int CeilingRatio(double numerator, double denominator) {
            return numerator <= 0 ? 0 : (int)Math.Ceiling(numerator / denominator);
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
