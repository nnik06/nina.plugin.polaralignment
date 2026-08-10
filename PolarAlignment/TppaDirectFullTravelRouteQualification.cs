using System;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaDirectFullTravelRouteQualification(
        bool IsQualified,
        TppaDirectFullTravelFeasibilityDecision Feasibility,
        string Reason) {
        public static TppaDirectFullTravelRouteQualification Evaluate(
            bool enabled,
            bool operatorConfirmed,
            double azimuthStartingPositionDegrees,
            double azimuthMinimumDegrees,
            double azimuthMaximumDegrees,
            double altitudeStartingPositionDegrees,
            double altitudeMinimumDegrees,
            double altitudeMaximumDegrees,
            double azimuthDeltaPerXUnitDegrees,
            double azimuthDeltaPerYUnitDegrees,
            double altitudeDeltaPerXUnitDegrees,
            double altitudeDeltaPerYUnitDegrees,
            double maximumXUnitsPerMove,
            double maximumYUnitsPerMove,
            double physicalAzimuthDegreesPerXUnit,
            double physicalAltitudeDegreesPerYUnit,
            double azimuthErrorMinutes,
            double altitudeErrorMinutes) {
            if (!enabled || !operatorConfirmed) {
                return Deny("the calibrated direct full-travel route is not enabled and operator-confirmed");
            }

            if (!IsFinite(
                    azimuthStartingPositionDegrees,
                    azimuthMinimumDegrees,
                    azimuthMaximumDegrees,
                    altitudeStartingPositionDegrees,
                    altitudeMinimumDegrees,
                    altitudeMaximumDegrees,
                    azimuthDeltaPerXUnitDegrees,
                    azimuthDeltaPerYUnitDegrees,
                    altitudeDeltaPerXUnitDegrees,
                    altitudeDeltaPerYUnitDegrees,
                    maximumXUnitsPerMove,
                    maximumYUnitsPerMove,
                    physicalAzimuthDegreesPerXUnit,
                    physicalAltitudeDegreesPerYUnit,
                    azimuthErrorMinutes,
                    altitudeErrorMinutes)) {
                return Deny("the calibrated direct full-travel route contains non-finite values");
            }

            if (!Contains(azimuthStartingPositionDegrees, azimuthMinimumDegrees, azimuthMaximumDegrees)
                || !Contains(altitudeStartingPositionDegrees, altitudeMinimumDegrees, altitudeMaximumDegrees)) {
                return Deny("the signed visual-marker starting position is outside its configured travel envelope");
            }

            var determinant = azimuthDeltaPerXUnitDegrees * altitudeDeltaPerYUnitDegrees
                              - azimuthDeltaPerYUnitDegrees * altitudeDeltaPerXUnitDegrees;
            if (Math.Abs(determinant) < 1e-8) {
                return Deny("the measured 2x2 UPAS response matrix is singular or unmeasured");
            }

            var feasibility = TppaDirectFullTravelFeasibilityPolicy.Evaluate(
                azimuthErrorMinutes,
                altitudeErrorMinutes,
                MaximumColumnMagnitudeMinutes(azimuthDeltaPerXUnitDegrees, altitudeDeltaPerXUnitDegrees),
                MaximumColumnMagnitudeMinutes(azimuthDeltaPerYUnitDegrees, altitudeDeltaPerYUnitDegrees),
                maximumXUnitsPerMove,
                maximumYUnitsPerMove,
                initialAgreementSeconds: 80,
                perMoveFreshFeedbackSeconds: 40,
                terminalConfirmationSeconds: 40,
                perMoveOverheadSeconds: 15);

            if (!feasibility.IsFeasible) {
                return new(false, feasibility, feasibility.Reason);
            }

            if (!HasSignedHeadroom(
                    azimuthStartingPositionDegrees,
                    azimuthMinimumDegrees,
                    azimuthMaximumDegrees,
                    physicalAzimuthDegreesPerXUnit,
                    feasibility.RequiredXUnits)
                || !HasSignedHeadroom(
                    altitudeStartingPositionDegrees,
                    altitudeMinimumDegrees,
                    altitudeMaximumDegrees,
                    physicalAltitudeDegreesPerYUnit,
                    feasibility.RequiredYUnits)) {
                return new(false, feasibility, "the signed visual-marker travel envelope lacks required full-route headroom");
            }

            return new(true, feasibility, "the calibrated direct full-travel route passed envelope, response, headroom, and five-minute feasibility checks");
        }

        private static TppaDirectFullTravelRouteQualification Deny(string reason) {
            return new(false, new(false, 0, 0, 0, 0, reason), reason);
        }

        private static bool Contains(double value, double minimum, double maximum) {
            return minimum < maximum && value >= minimum && value <= maximum;
        }

        private static bool HasSignedHeadroom(double start, double minimum, double maximum, double degreesPerUnit, double requiredUnits) {
            var displacement = Math.Abs(degreesPerUnit * requiredUnits);
            return start - displacement >= minimum && start + displacement <= maximum;
        }

        private static double MaximumColumnMagnitudeMinutes(double primaryDegreesPerUnit, double crossDegreesPerUnit) {
            return Math.Sqrt(primaryDegreesPerUnit * primaryDegreesPerUnit + crossDegreesPerUnit * crossDegreesPerUnit) * 60.0;
        }

        private static bool IsFinite(params double[] values) {
            foreach (var value in values) {
                if (double.IsNaN(value) || double.IsInfinity(value)) {
                    return false;
                }
            }

            return true;
        }
    }
}
