using System;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaMountMotionEnvelope(
        double MinimumAltitudeDegrees,
        double MaximumAltitudeDegrees,
        double AzimuthStartDegrees,
        double AzimuthEndDegrees) {

        public string GetConfigurationIssue() {
            if (!double.IsFinite(MinimumAltitudeDegrees)
                    || !double.IsFinite(MaximumAltitudeDegrees)
                    || MinimumAltitudeDegrees < -90
                    || MaximumAltitudeDegrees > 90
                    || MinimumAltitudeDegrees >= MaximumAltitudeDegrees) {
                return "configured altitude envelope is invalid";
            }
            if (!double.IsFinite(AzimuthStartDegrees) || !double.IsFinite(AzimuthEndDegrees)) {
                return "configured azimuth envelope is invalid";
            }

            return string.Empty;
        }

        public string Validate(double azimuthDegrees, double altitudeDegrees) {
            if (!double.IsFinite(azimuthDegrees) || !double.IsFinite(altitudeDegrees)) {
                return "mount position is not finite";
            }
            var configurationIssue = GetConfigurationIssue();
            if (!string.IsNullOrWhiteSpace(configurationIssue)) {
                return configurationIssue;
            }

            if (altitudeDegrees < MinimumAltitudeDegrees || altitudeDegrees > MaximumAltitudeDegrees) {
                return $"altitude {altitudeDegrees:F2} deg is outside {MinimumAltitudeDegrees:F2}..{MaximumAltitudeDegrees:F2} deg";
            }

            var azimuth = Normalize(azimuthDegrees);
            var start = Normalize(AzimuthStartDegrees);
            var end = Normalize(AzimuthEndDegrees);
            var azimuthAllowed = start <= end
                ? azimuth >= start && azimuth <= end
                : azimuth >= start || azimuth <= end;
            return azimuthAllowed
                ? string.Empty
                : $"azimuth {azimuth:F2} deg is outside {start:F2}..{end:F2} deg";
        }

        private static double Normalize(double value) {
            var normalized = value % 360.0;
            return normalized < 0 ? normalized + 360.0 : normalized;
        }
    }
}
