using System;
using System.Collections.Generic;

namespace NINA.Plugins.PolarAlignment {
    /// <summary>
    /// Operational imaging-fitness gate. This deliberately does not claim
    /// traceable absolute polar-axis accuracy or require an external witness.
    /// </summary>
    internal sealed record TppaOperationalQualificationPolicy(
        double MaximumDurationSeconds = 300.0,
        int MinimumFreshDeterminations = 2,
        double MaximumDeterminationErrorArcMinutes = 3.0,
        double MaximumRepeatabilityDeltaArcMinutes = 0.5);

    internal sealed record TppaOperationalQualificationInput(
        double DurationSeconds,
        int FreshDeterminationCount,
        bool FreshSolvesUncached,
        double MaximumDeterminationErrorArcMinutes,
        double MaximumPairwiseDeltaArcMinutes,
        bool NoPhysicalAdjustmentBetweenDeterminations,
        bool GeometryQualified,
        bool ClosureQualified,
        bool SafetyGatesPassed,
        bool RefractionAdjustmentEnabled,
        string PoleTarget);

    internal sealed record TppaOperationalQualificationResult(
        bool IsOperationallyQualified,
        IReadOnlyList<string> Issues);

    internal static class TppaOperationalQualification {
        public static TppaOperationalQualificationResult Evaluate(
                TppaOperationalQualificationInput input,
                TppaOperationalQualificationPolicy policy = null) {
            var activePolicy = policy ?? new TppaOperationalQualificationPolicy();
            var issues = new List<string>();

            RequireFiniteNonNegative(input.DurationSeconds, nameof(input.DurationSeconds));
            RequireFiniteNonNegative(
                input.MaximumDeterminationErrorArcMinutes,
                nameof(input.MaximumDeterminationErrorArcMinutes));
            RequireFiniteNonNegative(
                input.MaximumPairwiseDeltaArcMinutes,
                nameof(input.MaximumPairwiseDeltaArcMinutes));

            if (!double.IsFinite(activePolicy.MaximumDurationSeconds)
                    || activePolicy.MaximumDurationSeconds <= 0) {
                throw new ArgumentOutOfRangeException(
                    nameof(policy),
                    "maximum duration policy must be finite and positive");
            }
            if (input.DurationSeconds > activePolicy.MaximumDurationSeconds) {
                issues.Add(
                    $"operational qualification took {input.DurationSeconds:F1}s, exceeding {activePolicy.MaximumDurationSeconds:F1}s");
            }
            if (input.FreshDeterminationCount < activePolicy.MinimumFreshDeterminations) {
                issues.Add(
                    $"only {input.FreshDeterminationCount} independent fresh determination(s) are available");
            }
            if (!input.FreshSolvesUncached) {
                issues.Add("fresh determinations include cached or reused solves");
            }
            if (input.MaximumDeterminationErrorArcMinutes
                    > activePolicy.MaximumDeterminationErrorArcMinutes) {
                issues.Add(
                    $"a fresh determination reported {input.MaximumDeterminationErrorArcMinutes:F3}', exceeding {activePolicy.MaximumDeterminationErrorArcMinutes:F3}'");
            }
            if (input.MaximumPairwiseDeltaArcMinutes
                    > activePolicy.MaximumRepeatabilityDeltaArcMinutes) {
                issues.Add(
                    $"fresh determination repeatability delta {input.MaximumPairwiseDeltaArcMinutes:F3}' exceeds {activePolicy.MaximumRepeatabilityDeltaArcMinutes:F3}'");
            }
            if (!input.NoPhysicalAdjustmentBetweenDeterminations) {
                issues.Add("physical state changed between fresh determinations");
            }
            if (!input.GeometryQualified) {
                issues.Add("three-point geometry is not qualified");
            }
            if (!input.ClosureQualified) {
                issues.Add("measurement closure is not qualified");
            }
            if (!input.SafetyGatesPassed) {
                issues.Add("one or more motion or equipment safety gates failed");
            }
            if (!input.RefractionAdjustmentEnabled
                    || input.PoleTarget != TppaFastQualificationConventions.TruePoleTarget) {
                issues.Add("run did not target the true celestial pole with refraction enabled");
            }

            return new TppaOperationalQualificationResult(issues.Count == 0, issues);
        }

        private static void RequireFiniteNonNegative(double value, string name) {
            if (!double.IsFinite(value) || value < 0) {
                throw new ArgumentOutOfRangeException(name);
            }
        }

    }
}