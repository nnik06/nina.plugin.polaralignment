namespace NINA.Plugins.PolarAlignment {
    internal enum AutomatedAlignmentCompletionDecision {
        ContinueCorrection,
        ValidateWithoutMoving,
        VerifyFreshThreePoint,
        AbortAfterFreshVerificationFailures,
        Finish
    }

    /// <summary>
    /// Prevents one optimistic post-move solve from ending automated alignment.
    /// The UPAS is held stationary while a second below-tolerance solve confirms
    /// that the result is repeatable.
    /// </summary>
    internal sealed class AutomatedAlignmentCompletionGuard {
        private const int MaximumFailedFreshVerifications = 2;
        private int consecutiveBelowToleranceObservations;
        private int failedFreshVerifications;

        public AutomatedAlignmentCompletionDecision Evaluate(double totalErrorMinutes, double toleranceMinutes) {
            var isBelowTolerance = IsBelowTolerance(totalErrorMinutes, toleranceMinutes);
            if (!isBelowTolerance) {
                consecutiveBelowToleranceObservations = 0;
                return AutomatedAlignmentCompletionDecision.ContinueCorrection;
            }

            consecutiveBelowToleranceObservations++;
            return consecutiveBelowToleranceObservations >= 2
                ? AutomatedAlignmentCompletionDecision.VerifyFreshThreePoint
                : AutomatedAlignmentCompletionDecision.ValidateWithoutMoving;
        }

        public AutomatedAlignmentCompletionDecision EvaluateFreshVerification(double totalErrorMinutes, double toleranceMinutes) {
            var isBelowTolerance = IsBelowTolerance(totalErrorMinutes, toleranceMinutes);
            consecutiveBelowToleranceObservations = 0;
            if (isBelowTolerance) {
                failedFreshVerifications = 0;
                return AutomatedAlignmentCompletionDecision.Finish;
            }

            failedFreshVerifications++;
            return failedFreshVerifications >= MaximumFailedFreshVerifications
                ? AutomatedAlignmentCompletionDecision.AbortAfterFreshVerificationFailures
                : AutomatedAlignmentCompletionDecision.ContinueCorrection;
        }
        private static bool IsBelowTolerance(double totalErrorMinutes, double toleranceMinutes) {
            return double.IsFinite(totalErrorMinutes)
                && totalErrorMinutes >= 0
                && double.IsFinite(toleranceMinutes)
                && toleranceMinutes > 0
                && totalErrorMinutes <= toleranceMinutes;
        }
    }
}
