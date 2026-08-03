using System;
using System.Collections.Generic;

namespace NINA.Plugins.PolarAlignment {
    internal static class TppaVerificationSettlePolicy {
        public const double MaximumOverrideSeconds = 120.0;
        // This threshold gates both verification evidence and automated actuator authority.
        public const double MinimumQualifiedSettleSeconds = 30.0;
        public const double MinimumQualifiedTargetDistanceDegrees =
            TppaThreePointGeometryQualificationPolicy.MinimumConfiguredLegDegrees;

        public static double Resolve(double profileSettleSeconds, double verificationOverrideSeconds) {
            var profile = double.IsFinite(profileSettleSeconds)
                ? Math.Max(0.0, profileSettleSeconds)
                : 0.0;
            return ResolveSequenceOverride(verificationOverrideSeconds) ?? profile;
        }

        public static double? ResolveSequenceOverride(double configuredSeconds) {
            if (!double.IsFinite(configuredSeconds) || configuredSeconds <= 0.0) {
                return null;
            }

            return Math.Clamp(configuredSeconds, 0.0, MaximumOverrideSeconds);
        }

        public static IReadOnlyList<string> GetActuatorQualificationIssues(
            double profileSettleSeconds,
            double sequenceOverrideSeconds,
            double minimumQualifiedSettleSeconds = MinimumQualifiedSettleSeconds) {
            var issues = new List<string>();
            if (!double.IsFinite(minimumQualifiedSettleSeconds)
                    || minimumQualifiedSettleSeconds < 5.0
                    || minimumQualifiedSettleSeconds > MinimumQualifiedSettleSeconds) {
                issues.Add("The commissioned minimum settle authority is invalid.");
                return issues;
            }
            var effectiveSettleSeconds = Resolve(
                profileSettleSeconds,
                sequenceOverrideSeconds);
            if (!(effectiveSettleSeconds >= minimumQualifiedSettleSeconds)) {
                issues.Add(
                    $"Automated adjustments require an effective point settle time of at least {minimumQualifiedSettleSeconds:F0} seconds under the active cadence authority.");
            }

            return issues;
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
            if (!(effectiveSettleSeconds >= MinimumQualifiedSettleSeconds)) {
                issues.Add(
                    $"Verification-only effective point settle time must be at least {MinimumQualifiedSettleSeconds:F0} seconds.");
            }

            return issues;
        }
    }
}
