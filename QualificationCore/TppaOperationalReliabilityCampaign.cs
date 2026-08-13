using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal enum TppaOperationalOutcomeCheck {
        NotRun,
        UnguidedDeclinationDrift,
        GuidedNineHundredSecondSub
    }

    /// <summary>
    /// Report-only field-campaign verdict for the operational TPPA goal. It
    /// never grants motion authority; it makes missing field evidence visible.
    /// </summary>
    internal sealed record TppaOperationalReliabilityAttempt(
        string AttemptId,
        bool FastAlignmentQualified,
        bool MotionSafetyGatesPassed,
        bool FailureLeftMountNoWorse,
        double StartingTotalErrorArcMinutes);

    internal sealed record TppaOperationalOutcomeEvidence(
        TppaOperationalOutcomeCheck Check,
        bool Passed,
        double? UnguidedDeclinationDriftArcSecondsPerMinute,
        string Description);

    internal sealed record TppaOperationalReliabilityCampaignResult(
        bool IsReliable,
        int AttemptCount,
        int QualifiedAttemptCount,
        IReadOnlyList<string> Issues);

    internal static class TppaOperationalReliabilityCampaign {
        public const int RequiredAttemptCount = 5;
        public const int RequiredQualifiedAttemptCount = 4;
        public const double MaximumUnguidedDeclinationDriftArcSecondsPerMinute = 0.79;

        public static TppaOperationalReliabilityCampaignResult Evaluate(
                IReadOnlyList<TppaOperationalReliabilityAttempt> attempts,
                TppaOperationalOutcomeEvidence outcome) {
            var issues = new List<string>();
            if (attempts == null) {
                throw new ArgumentNullException(nameof(attempts));
            }
            if (outcome == null) {
                throw new ArgumentNullException(nameof(outcome));
            }

            if (attempts.Count != RequiredAttemptCount) {
                issues.Add($"requires exactly {RequiredAttemptCount} initiated field attempts; received {attempts.Count}");
            }
            if (attempts.Any(attempt => attempt == null)) {
                issues.Add("attempt evidence contains a missing attempt");
            }
            var usableAttempts = attempts.Where(attempt => attempt != null).ToArray();
            if (usableAttempts.Any(attempt => string.IsNullOrWhiteSpace(attempt.AttemptId)
                    || !double.IsFinite(attempt.StartingTotalErrorArcMinutes)
                    || attempt.StartingTotalErrorArcMinutes < 0)) {
                issues.Add("attempt evidence contains an invalid identifier or starting TPPA error");
            }

            var qualified = usableAttempts.Count(attempt => attempt.FastAlignmentQualified);
            if (qualified < RequiredQualifiedAttemptCount) {
                issues.Add($"only {qualified} of {RequiredAttemptCount} attempts reached the fresh-confirmed <=3' result");
            }
            if (usableAttempts.Any(attempt => !attempt.MotionSafetyGatesPassed)) {
                issues.Add("one or more attempts recorded a motion safety-gate violation");
            }
            if (usableAttempts.Any(attempt => !attempt.FastAlignmentQualified
                    && !attempt.FailureLeftMountNoWorse)) {
                issues.Add("a failed attempt left the mount displaced without a no-worse restoration result");
            }

            if (outcome.Check == TppaOperationalOutcomeCheck.NotRun || !outcome.Passed) {
                issues.Add("no independent photon-backed outcome check passed");
            } else if (outcome.Check == TppaOperationalOutcomeCheck.UnguidedDeclinationDrift) {
                if (!outcome.UnguidedDeclinationDriftArcSecondsPerMinute.HasValue
                        || !double.IsFinite(outcome.UnguidedDeclinationDriftArcSecondsPerMinute.Value)
                        || outcome.UnguidedDeclinationDriftArcSecondsPerMinute.Value
                            > MaximumUnguidedDeclinationDriftArcSecondsPerMinute) {
                    issues.Add(
                        $"unguided DEC drift must be <= {MaximumUnguidedDeclinationDriftArcSecondsPerMinute:F2} arcsec/min");
                }
            } else if (outcome.Check != TppaOperationalOutcomeCheck.GuidedNineHundredSecondSub) {
                issues.Add("outcome check type is not recognized");
            }

            return new(issues.Count == 0, attempts.Count, qualified, issues);
        }
    }
}
