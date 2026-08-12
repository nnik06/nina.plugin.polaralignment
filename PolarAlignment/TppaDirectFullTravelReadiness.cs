namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaDirectFullTravelReadiness(
        bool IsReady,
        bool RequiresBoundedYBootstrap,
        string Reason) {
        /// <summary>
        /// Checks the static part of the attended direct full-travel route before the
        /// first TPPA capture. The live route still qualifies the fresh error vector,
        /// projected travel, and five-minute convergence budget immediately before motion.
        /// </summary>
        public static TppaDirectFullTravelReadiness Evaluate(
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
            bool clampLimitedRecoveryEnabled = false,
            double relativeResponseUncertainty = double.PositiveInfinity,
            int physicalAzimuthCommandDirectionMultiplier = 1,
            int physicalAltitudeCommandDirectionMultiplier = 1,
            double terminalErrorMinutes = TppaDirectFullTravelFeasibilityPolicy.DefaultTerminalErrorMinutes) {
            if (!enabled) {
                return new(false, false, "the calibrated direct full-travel route is not enabled");
            }

            var qualification = TppaDirectFullTravelRouteQualification.Evaluate(
                enabled,
                operatorConfirmed,
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
                azimuthErrorMinutes: 0,
                altitudeErrorMinutes: 0,
                clampLimitedRecoveryEnabled,
                relativeResponseUncertainty,
                physicalAzimuthCommandDirectionMultiplier,
                physicalAltitudeCommandDirectionMultiplier,
                terminalErrorMinutes);
            if (!qualification.IsQualified) {
                var bootstrap = TppaDirectBootstrapRouteQualification.Evaluate(
                    enabled,
                    operatorConfirmed,
                    azimuthStartingPositionDegrees,
                    azimuthMinimumDegrees,
                    azimuthMaximumDegrees,
                    altitudeStartingPositionDegrees,
                    altitudeMinimumDegrees,
                    altitudeMaximumDegrees,
                    azimuthDeltaPerXUnitDegrees,
                    altitudeDeltaPerXUnitDegrees,
                    maximumYUnitsPerMove,
                    physicalAltitudeDegreesPerYUnit);
                return bootstrap.IsQualified
                    ? new(true, true,
                        "the full 2x2 route is not configured, but the bounded Y bootstrap is ready; a fresh, conditioned Y response is required before any two-axis correction")
                    : new(false, false, qualification.Reason);
            }

            return new(true, false,
                "the static direct full-travel route is ready; fresh TPPA error, projected headroom, and five-minute convergence remain motion-time qualifications");
        }
    }
}
