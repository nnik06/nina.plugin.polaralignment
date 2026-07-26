using System;
using System.Collections.Generic;

namespace NINA.Plugins.PolarAlignment {
    internal static class TppaVerificationSettlePolicy {
        public const double MaximumOverrideSeconds = 30.0;
        public const double MinimumQualifiedSettleSeconds = 30.0;
        public const double MinimumQualifiedTargetDistanceDegrees = 15.0;

        public static double Resolve(double profileSettleSeconds, double verificationOverrideSeconds) {
            var profile = double.IsFinite(profileSettleSeconds)
                ? Math.Max(0.0, profileSettleSeconds)
                : 0.0;
            if (!double.IsFinite(verificationOverrideSeconds) || verificationOverrideSeconds <= 0.0) {
                return profile;
            }

            return Math.Clamp(verificationOverrideSeconds, 0.0, MaximumOverrideSeconds);
        }

        public static IReadOnlyList<string> GetQualificationIssues(
            double targetDistanceDegrees,
            double profileSettleSeconds,
            double verificationOverrideSeconds) {
            var issues = new List<string>();
            if (!double.IsFinite(targetDistanceDegrees)
                    || targetDistanceDegrees < MinimumQualifiedTargetDistanceDegrees) {
                issues.Add(
                    $"Verification-only target distance must be at least {MinimumQualifiedTargetDistanceDegrees:F0} degrees per leg.");
            }

            var effectiveSettleSeconds = Resolve(profileSettleSeconds, verificationOverrideSeconds);
            if (effectiveSettleSeconds < MinimumQualifiedSettleSeconds) {
                issues.Add(
                    $"Verification-only effective point settle time must be at least {MinimumQualifiedSettleSeconds:F0} seconds.");
            }

            return issues;
        }
    }
}
