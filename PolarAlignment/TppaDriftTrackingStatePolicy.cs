using System;
using NINA.Equipment.Interfaces;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaDriftTrackingSnapshot(bool TrackingEnabled);

    internal static class TppaDriftTrackingStatePolicy {
        public static TppaDriftTrackingSnapshot Capture(
            bool trackingEnabled,
            TrackingMode trackingMode) {
            if (trackingEnabled && trackingMode != TrackingMode.Sidereal) {
                throw new InvalidOperationException(
                    $"Drift validation requires sidereal tracking, but the telescope is currently using {trackingMode} tracking.");
            }

            return new TppaDriftTrackingSnapshot(trackingEnabled);
        }
    }
}
