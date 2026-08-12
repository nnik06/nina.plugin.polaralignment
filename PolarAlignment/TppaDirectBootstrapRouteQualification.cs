using System;

namespace NINA.Plugins.PolarAlignment {
    /// <summary>
    /// Qualifies admission to a live Y-response bootstrap. This deliberately grants only the
    /// bounded Y probe, never a full-travel correction: a fresh, conditioned Y column remains
    /// necessary before the controller can consider a larger move.
    /// </summary>
    internal sealed record TppaDirectBootstrapRouteQualification(bool IsQualified, string Reason) {
        internal const double BootstrapYProbeUnits = 20.0;
        private const double MinimumAxisResponseDegreesPerUnit = 0.08 / 60.0;
        private const double MaximumAxisResponseDegreesPerUnit = 15.0 / 60.0;

        public static TppaDirectBootstrapRouteQualification Evaluate(
            bool enabled,
            bool operatorConfirmed,
            double azimuthStartingPositionDegrees,
            double azimuthMinimumDegrees,
            double azimuthMaximumDegrees,
            double altitudeStartingPositionDegrees,
            double altitudeMinimumDegrees,
            double altitudeMaximumDegrees,
            double azimuthDeltaPerXUnitDegrees,
            double altitudeDeltaPerXUnitDegrees,
            double maximumYUnitsPerMove,
            double physicalAltitudeDegreesPerYUnit) {
            if (!enabled) {
                return Deny("the calibrated direct bootstrap route is not enabled");
            }

            if (!IsFinite(
                    azimuthStartingPositionDegrees,
                    azimuthMinimumDegrees,
                    azimuthMaximumDegrees,
                    altitudeStartingPositionDegrees,
                    altitudeMinimumDegrees,
                    altitudeMaximumDegrees,
                    azimuthDeltaPerXUnitDegrees,
                    altitudeDeltaPerXUnitDegrees,
                    maximumYUnitsPerMove,
                    physicalAltitudeDegreesPerYUnit)) {
                return Deny("the calibrated direct bootstrap route contains non-finite values");
            }

            if (!Contains(azimuthStartingPositionDegrees, azimuthMinimumDegrees, azimuthMaximumDegrees)
                || !Contains(altitudeStartingPositionDegrees, altitudeMinimumDegrees, altitudeMaximumDegrees)) {
                return Deny("the signed visual-marker starting position is outside its configured travel envelope");
            }

            var xResponseMagnitude = Math.Sqrt(
                azimuthDeltaPerXUnitDegrees * azimuthDeltaPerXUnitDegrees
                + altitudeDeltaPerXUnitDegrees * altitudeDeltaPerXUnitDegrees);
            if (xResponseMagnitude < MinimumAxisResponseDegreesPerUnit
                || xResponseMagnitude > MaximumAxisResponseDegreesPerUnit) {
                return Deny("the static X response is unmeasured or outside the plausible response range");
            }

            if (maximumYUnitsPerMove < BootstrapYProbeUnits || physicalAltitudeDegreesPerYUnit <= 0) {
                return Deny("the direct bootstrap route lacks enough bounded Y authority for an observable probe");
            }

            var probeDisplacement = BootstrapYProbeUnits * physicalAltitudeDegreesPerYUnit;
            if (altitudeStartingPositionDegrees - probeDisplacement < altitudeMinimumDegrees
                || altitudeStartingPositionDegrees + probeDisplacement > altitudeMaximumDegrees) {
                return Deny("the signed ALT visual-marker envelope lacks two-sided headroom for the Y bootstrap probe");
            }

            return new(true,
                "the direct bootstrap route has a plausible X response and two-sided physical Y-probe headroom");
        }

        private static TppaDirectBootstrapRouteQualification Deny(string reason) => new(false, reason);

        private static bool Contains(double value, double minimum, double maximum) {
            return minimum < maximum && value >= minimum && value <= maximum;
        }

        private static bool IsFinite(params double[] values) {
            foreach (var value in values) {
                if (!double.IsFinite(value)) {
                    return false;
                }
            }

            return true;
        }
    }
}
