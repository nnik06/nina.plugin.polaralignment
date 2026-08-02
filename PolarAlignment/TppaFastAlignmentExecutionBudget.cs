using System;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaFastAlignmentBudgetDecision(
        bool CanStart,
        double ElapsedSeconds,
        double RemainingSeconds,
        double RequiredReserveSeconds,
        string Reason);

    internal sealed record TppaFastAlignmentConfigurationDecision(
        bool IsEligible,
        double ResolvedSettleSeconds,
        double ExposureSeconds,
        bool AutoPauseEnabled,
        string Reason);

    internal static class TppaFastAlignmentExecutionBudget {
        public const int MaximumFreshFeedbackMoves = 1;

        public const double MaximumRuntimeSeconds = 300;
        // The clean field maximum was 72.726s for a same-arc three-point
        // determination and return solve at the qualified 30s settle setting.
        public const double FreshDeterminationReserveSeconds = 75;
        // Initial admission remains retry-capable before any actuator movement.
        public const double FreshDeterminationRetryReserveSeconds = 125;
        public const double UpasMoveReserveSeconds = 15;
        public const double MoveAndFreshFeedbackReserveSeconds =
            UpasMoveReserveSeconds + FreshDeterminationReserveSeconds;
        // Before any move, reserve both independent post-move feedback and the
        // mandatory stationary confirmation. A retry may still exhaust the hard
        // deadline, but no subsequent movement is then authorized.
        public const double MinimumCompleteMoveAndConfirmationReserveSeconds =
            MoveAndFreshFeedbackReserveSeconds + FreshDeterminationReserveSeconds;
        public const double ContinuousSolveReserveSeconds = 20;
        public const double QualifiedFastSettleSeconds = 30;
        public const double MaximumFastExposureSeconds = 3;

        public static TppaFastAlignmentConfigurationDecision EvaluateConfiguration(
                double resolvedSettleSeconds,
                double exposureSeconds,
                bool autoPauseEnabled) {
            var reasons = new System.Collections.Generic.List<string>();
            if (!double.IsFinite(resolvedSettleSeconds)
                    || Math.Abs(resolvedSettleSeconds - QualifiedFastSettleSeconds) > 0.001) {
                reasons.Add(
                    $"resolved point settle must be {QualifiedFastSettleSeconds:F0}s; received {resolvedSettleSeconds:F3}s");
            }
            if (!double.IsFinite(exposureSeconds)
                    || exposureSeconds <= 0
                    || exposureSeconds > MaximumFastExposureSeconds) {
                reasons.Add(
                    $"plate-solve exposure must be finite, positive, and at most {MaximumFastExposureSeconds:F0}s; received {exposureSeconds:F3}s");
            }
            if (autoPauseEnabled) {
                reasons.Add("Auto pause must be disabled");
            }

            var eligible = reasons.Count == 0;
            return new(
                eligible,
                resolvedSettleSeconds,
                exposureSeconds,
                autoPauseEnabled,
                eligible
                    ? "five-minute configuration is eligible"
                    : "five-minute configuration is ineligible: " + string.Join("; ", reasons));
        }

        public static TppaFastAlignmentBudgetDecision EvaluateBeforeMove(
                TimeSpan elapsed,
                double maximumRuntimeSeconds = MaximumRuntimeSeconds) {
            if (elapsed < TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(elapsed));
            }

            // Total pre-move runtime is a deliberately conservative proxy for
            // the cadence of each future fresh determination. It catches slow
            // solve/settle nights before physical motion instead of assuming
            // every post-move determination will match the 75s clean minimum.
            var observedCadenceSeconds = Math.Max(
                FreshDeterminationReserveSeconds,
                elapsed.TotalSeconds);
            var requiredReserveSeconds = UpasMoveReserveSeconds
                + 2 * observedCadenceSeconds;
            return Evaluate(
                elapsed,
                requiredReserveSeconds,
                maximumRuntimeSeconds);
        }

        public static TppaFastAlignmentBudgetDecision Evaluate(
                TimeSpan elapsed,
                double requiredReserveSeconds,
                double maximumRuntimeSeconds = MaximumRuntimeSeconds) {
            if (elapsed < TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(elapsed));
            }
            if (!double.IsFinite(requiredReserveSeconds)
                    || requiredReserveSeconds < 0) {
                throw new ArgumentOutOfRangeException(nameof(requiredReserveSeconds));
            }
            if (!double.IsFinite(maximumRuntimeSeconds)
                    || maximumRuntimeSeconds <= 0) {
                throw new ArgumentOutOfRangeException(nameof(maximumRuntimeSeconds));
            }

            var elapsedSeconds = elapsed.TotalSeconds;
            var remainingSeconds = maximumRuntimeSeconds - elapsedSeconds;
            var canStart = remainingSeconds >= requiredReserveSeconds;
            var reason = canStart
                ? $"fast-alignment budget permits the operation: {remainingSeconds:F1}s remain and {requiredReserveSeconds:F1}s are reserved"
                : $"fast-alignment budget denies the operation: only {remainingSeconds:F1}s remain but {requiredReserveSeconds:F1}s are required";
            return new(
                canStart,
                elapsedSeconds,
                remainingSeconds,
                requiredReserveSeconds,
                reason);
        }
    }
}
