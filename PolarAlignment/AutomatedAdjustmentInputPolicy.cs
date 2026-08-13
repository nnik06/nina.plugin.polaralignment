using System;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct AutomatedAdjustmentInputDecision(
        bool IsEligible,
        string Reason);

    internal static class AutomatedAdjustmentInputPolicy {
        // Ordinary direct alignment stays in the demonstrated 120' envelope.
        // A separately qualified calibrated full-travel route may admit the
        // physical +/-5.4 degree envelope; it does not require the external
        // supervisor campaign or its provenance paperwork.
        public const double MaximumInitialErrorArcMinutes = 120.0;
        // Keep a clear operational margin inside the +/-5.4 degree physical
        // envelope. A fresh determination outside +/-5 degrees is still
        // reported, but it cannot authorize an automatic UPAS correction.
        public const double MaximumFieldInitialAxisErrorArcMinutes = 300.0;
        public const double MaximumFieldInitialTotalErrorArcMinutes = 458.205195;

        // Retained for compatibility with the supervisor campaign vocabulary.
        public const double MaximumSupervisorCoarseInitialErrorArcMinutes =
            MaximumFieldInitialTotalErrorArcMinutes;

        public static AutomatedAdjustmentInputDecision EvaluateForRoute(
            double azimuthErrorArcMinutes,
            double altitudeErrorArcMinutes,
            double totalErrorArcMinutes,
            bool supervisorCoarseRoute) {
            return EvaluateForRoute(
                azimuthErrorArcMinutes,
                altitudeErrorArcMinutes,
                totalErrorArcMinutes,
                supervisorCoarseRoute,
                qualifiedDirectFullTravelRoute: false);
        }

        public static AutomatedAdjustmentInputDecision EvaluateForRoute(
            double azimuthErrorArcMinutes,
            double altitudeErrorArcMinutes,
            double totalErrorArcMinutes,
            bool supervisorCoarseRoute,
            bool qualifiedDirectFullTravelRoute,
            bool qualifiedDirectAzimuthRoute = false,
            bool qualifiedDirectBootstrapRoute = false,
            bool qualifiedFirstRunBootstrapRoute = false) {
            if (qualifiedDirectFullTravelRoute) {
                return EvaluateCalibratedDirectFullTravel(
                    azimuthErrorArcMinutes,
                    altitudeErrorArcMinutes,
                    totalErrorArcMinutes);
            }

            if (qualifiedFirstRunBootstrapRoute) {
                return EvaluateFirstRunFullTravel(
                    azimuthErrorArcMinutes,
                    altitudeErrorArcMinutes,
                    totalErrorArcMinutes);
            }

            if (qualifiedDirectBootstrapRoute) {
                return EvaluateSupervisorCoarse(
                    azimuthErrorArcMinutes,
                    altitudeErrorArcMinutes,
                    totalErrorArcMinutes);
            }

            if (qualifiedDirectAzimuthRoute) {
                return EvaluateMixedDirectEnvelope(
                    azimuthErrorArcMinutes,
                    altitudeErrorArcMinutes,
                    totalErrorArcMinutes);
            }

            return supervisorCoarseRoute
                ? EvaluateSupervisorCoarse(
                    azimuthErrorArcMinutes,
                    altitudeErrorArcMinutes,
                    totalErrorArcMinutes)
                : Evaluate(
                    azimuthErrorArcMinutes,
                    altitudeErrorArcMinutes,
                    totalErrorArcMinutes);
        }

        public static AutomatedAdjustmentInputDecision Evaluate(
            double azimuthErrorArcMinutes,
            double altitudeErrorArcMinutes,
            double totalErrorArcMinutes) {
            return EvaluateWithLimit(
                azimuthErrorArcMinutes,
                altitudeErrorArcMinutes,
                totalErrorArcMinutes,
                MaximumInitialErrorArcMinutes,
                MaximumInitialErrorArcMinutes,
                "direct field automated-correction");
        }

        public static AutomatedAdjustmentInputDecision EvaluateSupervisorCoarse(
            double azimuthErrorArcMinutes,
            double altitudeErrorArcMinutes,
            double totalErrorArcMinutes) {
            return EvaluateWithLimit(
                azimuthErrorArcMinutes,
                altitudeErrorArcMinutes,
                totalErrorArcMinutes,
                MaximumFieldInitialAxisErrorArcMinutes,
                MaximumFieldInitialTotalErrorArcMinutes,
                "supervisor coarse-correction");
        }

        public static AutomatedAdjustmentInputDecision EvaluateCalibratedDirectFullTravel(
            double azimuthErrorArcMinutes,
            double altitudeErrorArcMinutes,
            double totalErrorArcMinutes) {
            return EvaluateWithLimit(
                azimuthErrorArcMinutes,
                altitudeErrorArcMinutes,
                totalErrorArcMinutes,
                MaximumFieldInitialAxisErrorArcMinutes,
                MaximumFieldInitialTotalErrorArcMinutes,
                "calibrated direct full-travel");
        }

        private static AutomatedAdjustmentInputDecision EvaluateFirstRunFullTravel(
            double azimuthErrorArcMinutes,
            double altitudeErrorArcMinutes,
            double totalErrorArcMinutes) {
            return EvaluateWithLimit(
                azimuthErrorArcMinutes,
                altitudeErrorArcMinutes,
                totalErrorArcMinutes,
                MaximumFieldInitialAxisErrorArcMinutes,
                MaximumFieldInitialTotalErrorArcMinutes,
                "attended first-run full-travel identification");
        }

        private static AutomatedAdjustmentInputDecision EvaluateMixedDirectEnvelope(
                double azimuthErrorArcMinutes,
                double altitudeErrorArcMinutes,
                double totalErrorArcMinutes) {
            if (!double.IsFinite(azimuthErrorArcMinutes)
                || !double.IsFinite(altitudeErrorArcMinutes)
                || !double.IsFinite(totalErrorArcMinutes)) {
                return new(false, "direct calibrated-azimuth input contains non-finite values");
            }

            var maximumTotal = Math.Sqrt(
                MaximumFieldInitialAxisErrorArcMinutes * MaximumFieldInitialAxisErrorArcMinutes
                + MaximumInitialErrorArcMinutes * MaximumInitialErrorArcMinutes);
            if (Math.Abs(azimuthErrorArcMinutes) > MaximumFieldInitialAxisErrorArcMinutes
                || Math.Abs(altitudeErrorArcMinutes) > MaximumInitialErrorArcMinutes
                || Math.Abs(totalErrorArcMinutes) > maximumTotal) {
                return new(false,
                    $"direct calibrated-azimuth input exceeds the mixed envelope: AZ <= {MaximumFieldInitialAxisErrorArcMinutes:F0}', ALT <= {MaximumInitialErrorArcMinutes:F0}', total <= {maximumTotal:F0}'");
            }

            return new(true,
                $"direct calibrated-azimuth input is inside the mixed envelope: AZ <= {MaximumFieldInitialAxisErrorArcMinutes:F0}', ALT <= {MaximumInitialErrorArcMinutes:F0}'");
        }

        private static AutomatedAdjustmentInputDecision EvaluateWithLimit(
            double azimuthErrorArcMinutes,
            double altitudeErrorArcMinutes,
            double totalErrorArcMinutes,
            double maximumAxisErrorArcMinutes,
            double maximumTotalErrorArcMinutes,
            string routeName) {
            if (!double.IsFinite(azimuthErrorArcMinutes)
                    || !double.IsFinite(altitudeErrorArcMinutes)
                    || !double.IsFinite(totalErrorArcMinutes)) {
                return new AutomatedAdjustmentInputDecision(
                    false,
                    "the fresh three-point result contains a non-finite axis or total error");
            }

            var componentMagnitudeArcMinutes = System.Math.Sqrt(
                azimuthErrorArcMinutes * azimuthErrorArcMinutes
                + altitudeErrorArcMinutes * altitudeErrorArcMinutes);
            if (System.Math.Abs(azimuthErrorArcMinutes) > maximumAxisErrorArcMinutes
                    || System.Math.Abs(altitudeErrorArcMinutes) > maximumAxisErrorArcMinutes
                    || System.Math.Max(System.Math.Abs(totalErrorArcMinutes), componentMagnitudeArcMinutes)
                        > maximumTotalErrorArcMinutes) {
                return new AutomatedAdjustmentInputDecision(
                    false,
                    $"the fresh error vector exceeds the {maximumAxisErrorArcMinutes:F0}' per-axis or {maximumTotalErrorArcMinutes:F0}' total {routeName} qualification limit");
            }

            return new AutomatedAdjustmentInputDecision(
                true,
                $"the fresh three-point result is finite and inside the {routeName} qualification limit");
        }
    }
}
