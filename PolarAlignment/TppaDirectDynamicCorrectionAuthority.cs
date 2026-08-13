using System;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaDirectDynamicCorrectionAuthorityDecision(
        bool CanAuthorize,
        string Reason);

    /// <summary>
    /// Validates the single-use authority earned by an accepted fresh response to
    /// a bounded UPAS move. This is intentionally narrower than normal fresh-pair
    /// authority: it can authorize only the immediately following direct move.
    /// </summary>
    internal static class TppaDirectDynamicCorrectionAuthority {
        public static TppaDirectDynamicCorrectionAuthorityDecision Evaluate(
                bool isGranted,
                bool hasCurrentFreshDetermination,
                int currentMotionEpoch,
                int grantedMotionEpoch,
                int currentFreshMeasurementSequence,
                int grantedFreshMeasurementSequence,
                int currentFeedbackMoveCount,
                int grantedFeedbackMoveCount,
                double ageSeconds,
                double maximumAgeSeconds) {
            if (!isGranted) {
                return new(false, "no accepted post-move response granted dynamic authority");
            }
            if (!hasCurrentFreshDetermination) {
                return new(false, "the accepted fresh response is unavailable");
            }
            if (currentMotionEpoch != grantedMotionEpoch) {
                return new(false, "a different UPAS motion epoch intervened");
            }
            if (currentFreshMeasurementSequence != grantedFreshMeasurementSequence) {
                return new(false, "another fresh determination intervened");
            }
            if (currentFeedbackMoveCount != grantedFeedbackMoveCount) {
                return new(false, "another move-feedback state intervened");
            }
            if (!double.IsFinite(ageSeconds) || ageSeconds < 0 || ageSeconds > maximumAgeSeconds) {
                return new(false, "the accepted fresh response expired");
            }
            return new(true, "accepted post-move response remains a single-use direct authority");
        }
    }
}
