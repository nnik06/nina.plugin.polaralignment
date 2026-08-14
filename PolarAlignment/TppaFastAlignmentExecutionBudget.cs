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
        // Eight bounded feedback moves give the damped controller enough
        // iterations to converge from the physical full-travel envelope while
        // still guaranteeing a finite stop on persistently noisy or ineffective
        // responses. Elapsed time is telemetry, not movement authority.
        public const int MaximumFreshFeedbackMoves = 8;
        public const int MaximumFreshFeedbackMovesAfterBoundedYBootstrapProbe =
            MaximumFreshFeedbackMoves;

        public const double MaximumRuntimeSeconds = 300;
        public const double MinimumQualifiedInitialTotalMinutes = 0;
        public const double MaximumQualifiedInitialTotalMinutes =
            AutomatedAdjustmentInputPolicy.MaximumSupervisorCoarseInitialErrorArcMinutes;
        public const double FineControllerInitialTotalMinutes = 24;
        // The clean field maximum was 72.726s for a same-arc three-point
        // determination and return solve at the qualified 30s settle setting.
        public const double FreshDeterminationReserveSeconds = 75;
        // Direct field alignment uses the mount's five-second settle floor.
        // A three-point determination has a 40s operational reservation; if
        // the observed cadence is slower, the five-minute planner uses that
        // measured duration and declines iterations that no longer fit.
        public const double DirectFieldFreshDeterminationReserveSeconds = 40;
        // A direct post-move feedback sample is eligible only for the next
        // agreement pair. A pause or delay falls back to two new samples.
        public const double DirectFeedbackReuseMaximumAgeSeconds = 75;
        public const double ObservedCadenceSlackSeconds = 5;
        // Initial admission remains retry-capable before any actuator movement.
        public const double FreshDeterminationRetryReserveSeconds = 125;
        public const double UpasMoveReserveSeconds = 15;
        public const double MoveAndFreshFeedbackReserveSeconds =
            UpasMoveReserveSeconds + FreshDeterminationReserveSeconds;
        // Every move must leave enough time for its independent fresh response
        // and a terminal verify-only determination. The response may authorize
        // one further bounded move, but a successful run never ends on evidence
        // that was used to calculate another move.
        public const double MinimumCompleteMoveAndConfirmationReserveSeconds =
            MoveAndFreshFeedbackReserveSeconds + FreshDeterminationReserveSeconds;
        public const double ContinuousSolveReserveSeconds = 20;
        public const double UnconditionalQualifiedSettleSeconds = 30;
        // Field plate solving on the balcony can require five seconds under
        // bright urban sky. Keep the fast-path bound finite while accepting
        // the proven operational exposure.
        public const double MaximumFastExposureSeconds = 5;

        public static TppaFastAlignmentConfigurationDecision EvaluateConfiguration(
                double resolvedSettleSeconds,
                double exposureSeconds,
                bool autoPauseEnabled,
                double qualifiedSettleSeconds = UnconditionalQualifiedSettleSeconds) {
            var reasons = new System.Collections.Generic.List<string>();
            if (!double.IsFinite(qualifiedSettleSeconds)
                    || qualifiedSettleSeconds < 5.0
                    || qualifiedSettleSeconds > UnconditionalQualifiedSettleSeconds) {
                reasons.Add("qualified settle authority is invalid");
            } else if (!double.IsFinite(resolvedSettleSeconds)
                    || resolvedSettleSeconds < qualifiedSettleSeconds
                    || resolvedSettleSeconds > TppaVerificationSettlePolicy.MaximumOverrideSeconds) {
                reasons.Add(
                    $"resolved point settle must be between {qualifiedSettleSeconds:F0}s and " +
                    $"{TppaVerificationSettlePolicy.MaximumOverrideSeconds:F0}s; received {resolvedSettleSeconds:F3}s");
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
                    ? "operational configuration is eligible"
                    : "operational configuration is ineligible: " + string.Join("; ", reasons));
        }

        public static TppaFastAlignmentBudgetDecision EvaluateInitialTotal(
                double initialTotalMinutes) {
            if (!double.IsFinite(initialTotalMinutes) || initialTotalMinutes < 0) {
                throw new ArgumentOutOfRangeException(nameof(initialTotalMinutes));
            }
            var eligible = initialTotalMinutes >= MinimumQualifiedInitialTotalMinutes
                && initialTotalMinutes <= MaximumQualifiedInitialTotalMinutes;
            return new(
                eligible,
                ElapsedSeconds: 0,
                RemainingSeconds: 0,
                RequiredReserveSeconds: 0,
                eligible
                    ? $"initial total {initialTotalMinutes:F2}' is inside the qualified {MinimumQualifiedInitialTotalMinutes:F0}-{MaximumQualifiedInitialTotalMinutes:F0}' window"
                    : $"initial total {initialTotalMinutes:F2}' is outside the qualified {MinimumQualifiedInitialTotalMinutes:F0}-{MaximumQualifiedInitialTotalMinutes:F0}' window");
        }

        public static TppaFastAlignmentBudgetDecision EvaluateBeforeMove(
                TimeSpan elapsed,
                double observedFreshDeterminationSeconds,
                int completedMoves,
                double qualifiedFreshDeterminationReserveSeconds = FreshDeterminationReserveSeconds,
                double maximumRuntimeSeconds = MaximumRuntimeSeconds,
                int maximumFreshFeedbackMoves = MaximumFreshFeedbackMoves) {
            if (elapsed < TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(elapsed));
            }
            if (maximumFreshFeedbackMoves < 2
                    || maximumFreshFeedbackMoves > MaximumFreshFeedbackMoves) {
                throw new ArgumentOutOfRangeException(nameof(maximumFreshFeedbackMoves));
            }
            if (completedMoves < 0 || completedMoves >= maximumFreshFeedbackMoves) {
                throw new ArgumentOutOfRangeException(nameof(completedMoves));
            }
            if (!double.IsFinite(observedFreshDeterminationSeconds)
                    || observedFreshDeterminationSeconds <= 0) {
                throw new ArgumentOutOfRangeException(nameof(observedFreshDeterminationSeconds));
            }
            if (!double.IsFinite(qualifiedFreshDeterminationReserveSeconds)
                    || qualifiedFreshDeterminationReserveSeconds <= 0
                    || qualifiedFreshDeterminationReserveSeconds > FreshDeterminationReserveSeconds) {
                throw new ArgumentOutOfRangeException(nameof(qualifiedFreshDeterminationReserveSeconds));
            }

            // Before the first move, total runtime is a conservative proxy for
            // the initial fresh cadence. Later moves use the measured duration
            // of the immediately preceding post-move fresh determination so
            // setup time is not counted again as solve cadence.
            var observedCadenceSeconds = ResolveObservedCadenceSeconds(
                observedFreshDeterminationSeconds,
                qualifiedFreshDeterminationReserveSeconds);
            // Admit the next bounded move on the evidence it must itself
            // produce: a fresh response and one stationary completion check.
            // Do not reserve hypothetical later corrections here. Each later
            // move is independently re-admitted after its predecessor's fresh
            // response, while the absolute move-count cap remains in force.
            var requiredReserveSeconds = UpasMoveReserveSeconds
                + 2 * observedCadenceSeconds;
            return Evaluate(
                elapsed,
                requiredReserveSeconds,
                maximumRuntimeSeconds);
        }

        /// <summary>
        /// A first-run identification probe is calibration, not a polar-alignment
        /// correction. It needs one bounded move and one independent fresh response;
        /// it must not reserve hypothetical later corrections or a terminal
        /// completion claim before a response model exists.
        /// </summary>
        public static TppaFastAlignmentBudgetDecision EvaluateBeforeFirstRunIdentificationProbe(
                TimeSpan elapsed,
                double observedFreshDeterminationSeconds,
                double qualifiedFreshDeterminationReserveSeconds = FreshDeterminationReserveSeconds,
                double maximumRuntimeSeconds = MaximumRuntimeSeconds) {
            if (!double.IsFinite(observedFreshDeterminationSeconds)
                    || observedFreshDeterminationSeconds <= 0) {
                throw new ArgumentOutOfRangeException(nameof(observedFreshDeterminationSeconds));
            }
            if (!double.IsFinite(qualifiedFreshDeterminationReserveSeconds)
                    || qualifiedFreshDeterminationReserveSeconds <= 0
                    || qualifiedFreshDeterminationReserveSeconds > FreshDeterminationReserveSeconds) {
                throw new ArgumentOutOfRangeException(nameof(qualifiedFreshDeterminationReserveSeconds));
            }

            var responseReserveSeconds = ResolveObservedCadenceSeconds(
                observedFreshDeterminationSeconds,
                qualifiedFreshDeterminationReserveSeconds);
            return Evaluate(
                elapsed,
                UpasMoveReserveSeconds + responseReserveSeconds,
                maximumRuntimeSeconds);
        }

        public static TppaFastAlignmentBudgetDecision EvaluateBeforeFreshAgreement(
                TimeSpan elapsed,
                double observedFreshDeterminationSeconds,
                int completedMoves,
                double qualifiedFreshDeterminationReserveSeconds = FreshDeterminationReserveSeconds,
                double maximumRuntimeSeconds = MaximumRuntimeSeconds,
                int maximumFreshFeedbackMoves = MaximumFreshFeedbackMoves) {
            var moveDecision = EvaluateBeforeMove(
                elapsed,
                observedFreshDeterminationSeconds,
                completedMoves,
                qualifiedFreshDeterminationReserveSeconds,
                maximumRuntimeSeconds,
                maximumFreshFeedbackMoves);
            var observedCadenceSeconds = ResolveObservedCadenceSeconds(
                observedFreshDeterminationSeconds,
                qualifiedFreshDeterminationReserveSeconds);
            return Evaluate(
                elapsed,
                moveDecision.RequiredReserveSeconds + observedCadenceSeconds,
                maximumRuntimeSeconds);
        }

        public static TppaFastAlignmentBudgetDecision EvaluateBeforeFreshPair(
                TimeSpan elapsed,
                double observedFreshDeterminationSeconds,
                int completedMoves,
                double qualifiedFreshDeterminationReserveSeconds = FreshDeterminationReserveSeconds,
                double maximumRuntimeSeconds = MaximumRuntimeSeconds,
                int maximumFreshFeedbackMoves = MaximumFreshFeedbackMoves) {
            var agreementDecision = EvaluateBeforeFreshAgreement(
                elapsed,
                observedFreshDeterminationSeconds,
                completedMoves,
                qualifiedFreshDeterminationReserveSeconds,
                maximumRuntimeSeconds,
                maximumFreshFeedbackMoves);
            var observedCadenceSeconds = ResolveObservedCadenceSeconds(
                observedFreshDeterminationSeconds,
                qualifiedFreshDeterminationReserveSeconds);
            return Evaluate(
                elapsed,
                agreementDecision.RequiredReserveSeconds + observedCadenceSeconds,
                maximumRuntimeSeconds);
        }

        private static double ResolveObservedCadenceSeconds(
                double observedFreshDeterminationSeconds,
                double qualifiedFreshDeterminationReserveSeconds) {
            return Math.Max(
                qualifiedFreshDeterminationReserveSeconds,
                observedFreshDeterminationSeconds + ObservedCadenceSlackSeconds);
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
