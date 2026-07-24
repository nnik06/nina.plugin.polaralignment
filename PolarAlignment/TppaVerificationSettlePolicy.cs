using System;

namespace NINA.Plugins.PolarAlignment {
    internal static class TppaVerificationSettlePolicy {
        public const double MaximumOverrideSeconds = 30.0;

        public static double Resolve(double profileSettleSeconds, double verificationOverrideSeconds) {
            var profile = double.IsFinite(profileSettleSeconds)
                ? Math.Max(0.0, profileSettleSeconds)
                : 0.0;
            if (!double.IsFinite(verificationOverrideSeconds) || verificationOverrideSeconds <= 0.0) {
                return profile;
            }

            return Math.Clamp(verificationOverrideSeconds, 0.0, MaximumOverrideSeconds);
        }
    }
}
