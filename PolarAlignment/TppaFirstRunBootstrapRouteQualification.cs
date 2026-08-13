using System;

namespace NINA.Plugins.PolarAlignment {
    /// <summary>
    /// Admits a first operational run that has no sky-response calibration yet.
    /// It grants identification moves only; the controller must measure two independent
    /// columns before it can calculate a correction.
    /// </summary>
    internal sealed record TppaFirstRunBootstrapRouteQualification(bool IsQualified, string Reason) {
        internal const double IdentificationProbeUnits = 20.0;

        public static TppaFirstRunBootstrapRouteQualification Evaluate(
                bool enabled,
                bool operatorConfirmed,
                bool azimuthTravelGuardEnabled,
                bool azimuthTravelGuardConfirmed,
                bool altitudeTravelGuardEnabled,
                bool altitudeTravelGuardConfirmed,
                bool azimuthPreSeatEnabled,
                double azimuthPreSeatUnits,
                double azimuthStartingPositionDegrees,
                double azimuthMinimumDegrees,
                double azimuthMaximumDegrees,
                double altitudeStartingPositionDegrees,
                double altitudeMinimumDegrees,
                double altitudeMaximumDegrees,
                double physicalAzimuthDegreesPerXUnit,
                double physicalAltitudeDegreesPerYUnit) {
            if (!enabled || !operatorConfirmed) {
                return Deny("the attended first-run bootstrap route is not enabled and confirmed");
            }
            if (!azimuthTravelGuardEnabled || !azimuthTravelGuardConfirmed
                || !altitudeTravelGuardEnabled || !altitudeTravelGuardConfirmed) {
                return Deny("both signed UPAS travel guards must be enabled and confirmed");
            }
            if (!azimuthPreSeatEnabled || !double.IsFinite(azimuthPreSeatUnits)
                || Math.Abs(azimuthPreSeatUnits) < 24.0) {
                return Deny("the attended first-run route requires the configured 24-unit azimuth pre-seat");
            }
            if (!Finite(azimuthStartingPositionDegrees, azimuthMinimumDegrees, azimuthMaximumDegrees,
                        altitudeStartingPositionDegrees, altitudeMinimumDegrees, altitudeMaximumDegrees,
                        physicalAzimuthDegreesPerXUnit, physicalAltitudeDegreesPerYUnit)
                || physicalAzimuthDegreesPerXUnit <= 0 || physicalAltitudeDegreesPerYUnit <= 0) {
                return Deny("the physical start, envelope, or worst-case degrees-per-unit bound is invalid");
            }

            var xProbeDegrees = IdentificationProbeUnits * Math.Abs(physicalAzimuthDegreesPerXUnit);
            var yProbeDegrees = IdentificationProbeUnits * Math.Abs(physicalAltitudeDegreesPerYUnit);
            if (!HasTwoSidedHeadroom(azimuthStartingPositionDegrees, azimuthMinimumDegrees, azimuthMaximumDegrees, xProbeDegrees)
                || !HasTwoSidedHeadroom(altitudeStartingPositionDegrees, altitudeMinimumDegrees, altitudeMaximumDegrees, yProbeDegrees)) {
                return Deny("the signed UPAS envelope lacks two-sided headroom for 20-unit X and Y identification moves");
            }

            return new(true, "the attended first-run route has bounded X/Y identification headroom; no unmeasured correction is authorized");
        }

        private static bool HasTwoSidedHeadroom(double start, double minimum, double maximum, double probe) =>
            minimum < maximum && start - probe >= minimum && start + probe <= maximum;

        private static bool Finite(params double[] values) {
            foreach (var value in values) {
                if (!double.IsFinite(value)) return false;
            }
            return true;
        }

        private static TppaFirstRunBootstrapRouteQualification Deny(string reason) => new(false, reason);
    }
}
