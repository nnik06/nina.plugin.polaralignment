using System;

namespace NINA.Plugins.PolarAlignment {
    internal static class TppaAlignmentTolerancePolicy {
        public const double OperationalDefaultArcMinutes = 3.0;
        public const double MinimumPositiveArcMinutes = 0.5;

        public static double ResolvePersistedSetting(double configuredArcMinutes) {
            if (!double.IsFinite(configuredArcMinutes)) {
                return OperationalDefaultArcMinutes;
            }

            return ClampUserInput(configuredArcMinutes);
        }

        public static double ClampUserInput(double requestedArcMinutes) {
            if (!double.IsFinite(requestedArcMinutes)) {
                return OperationalDefaultArcMinutes;
            }

            if (requestedArcMinutes <= 0.0) {
                return 0.0;
            }

            return Math.Max(MinimumPositiveArcMinutes, requestedArcMinutes);
        }
    }
}
