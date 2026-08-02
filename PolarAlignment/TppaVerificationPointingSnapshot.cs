using System;
using NINA.Astrometry;

namespace NINA.Plugins.PolarAlignment {
    internal static class TppaVerificationPointingSnapshot {
        public static Coordinates FromMountTelemetry(
                Coordinates mountTelemetryCoordinates,
                string phase) {
            if (mountTelemetryCoordinates == null) {
                throw new InvalidOperationException(
                    $"Verification-only {phase} pointing telemetry is unavailable.");
            }

            if (!double.IsFinite(mountTelemetryCoordinates.RADegrees)
                    || !double.IsFinite(mountTelemetryCoordinates.Dec)) {
                throw new InvalidOperationException(
                    $"Verification-only {phase} pointing telemetry is non-finite.");
            }

            return mountTelemetryCoordinates;
        }
    }
}