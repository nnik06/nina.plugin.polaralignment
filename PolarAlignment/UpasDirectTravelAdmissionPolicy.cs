using System.Collections.Generic;

namespace NINA.Plugins.PolarAlignment {
    internal static class UpasDirectTravelAdmissionPolicy {
        public static IReadOnlyList<string> GetIssues(
                bool useUpas,
                bool automatedAdjustmentsEnabled,
                bool azimuthGuardEnabled,
                bool azimuthMarkerConfirmed,
                bool altitudeGuardEnabled,
                bool altitudeMarkerConfirmed) {
            if (!useUpas || !automatedAdjustmentsEnabled) {
                return System.Array.Empty<string>();
            }

            var issues = new List<string>();
            if (!azimuthGuardEnabled || !azimuthMarkerConfirmed) {
                issues.Add("Automated UPAS alignment requires the current physical AZ marker to be checked and confirmed in the travel guard.");
            }
            if (!altitudeGuardEnabled || !altitudeMarkerConfirmed) {
                issues.Add("Automated UPAS alignment requires the current physical ALT marker to be checked and confirmed in the travel guard.");
            }
            return issues;
        }
    }
}
