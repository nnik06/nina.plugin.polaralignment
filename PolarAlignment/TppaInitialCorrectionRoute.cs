using System;

namespace NINA.Plugins.PolarAlignment {
    internal enum TppaInitialCorrectionStage {
        Fine,
        Coarse,
        Rejected
    }

    internal sealed record TppaInitialCorrectionRouteDecision(
        TppaInitialCorrectionStage Stage,
        double InitialTotalMinutes,
        string Reason);

    internal static class TppaInitialCorrectionRoute {
        public const double MaximumCoarseInitialTotalMinutes =
            AutomatedAdjustmentInputPolicy.MaximumSupervisorCoarseInitialErrorArcMinutes;

        public static TppaInitialCorrectionRouteDecision Evaluate(double initialTotalMinutes) {
            if (!double.IsFinite(initialTotalMinutes) || initialTotalMinutes < 0.0) {
                throw new ArgumentOutOfRangeException(nameof(initialTotalMinutes));
            }

            if (initialTotalMinutes
                    <= TppaFastAlignmentExecutionBudget.FineControllerInitialTotalMinutes) {
                return new(
                    TppaInitialCorrectionStage.Fine,
                    initialTotalMinutes,
                    $"initial total {initialTotalMinutes:F2}' is inside the qualified fine-controller window");
            }

            if (initialTotalMinutes <= MaximumCoarseInitialTotalMinutes) {
                return new(
                    TppaInitialCorrectionStage.Coarse,
                    initialTotalMinutes,
                    $"initial total {initialTotalMinutes:F2}' requires the separately qualified coarse supervisor stage");
            }

            return new(
                TppaInitialCorrectionStage.Rejected,
                initialTotalMinutes,
                $"initial total {initialTotalMinutes:F2}' exceeds the {MaximumCoarseInitialTotalMinutes:F0}' coarse campaign ceiling");
        }
    }
}
