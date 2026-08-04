namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct AutomatedAdjustmentInputDecision(
        bool IsEligible,
        string Reason);

    internal static class AutomatedAdjustmentInputPolicy {
        // The legacy controller remains limited to the range qualified before
        // supervisor-backed coarse planning was introduced.
        public const double MaximumInitialErrorArcMinutes = 120.0;
        // The sealed five-minute campaign may use the wider UPAS starting
        // envelope only through the supervisor transaction boundary.
        public const double MaximumSupervisorCoarseInitialErrorArcMinutes = 300.0;

        public static AutomatedAdjustmentInputDecision EvaluateForRoute(
            double azimuthErrorArcMinutes,
            double altitudeErrorArcMinutes,
            double totalErrorArcMinutes,
            bool supervisorCoarseRoute) {
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
                "legacy automated-correction");
        }

        public static AutomatedAdjustmentInputDecision EvaluateSupervisorCoarse(
            double azimuthErrorArcMinutes,
            double altitudeErrorArcMinutes,
            double totalErrorArcMinutes) {
            return EvaluateWithLimit(
                azimuthErrorArcMinutes,
                altitudeErrorArcMinutes,
                totalErrorArcMinutes,
                MaximumSupervisorCoarseInitialErrorArcMinutes,
                "supervisor coarse-correction");
        }

        private static AutomatedAdjustmentInputDecision EvaluateWithLimit(
            double azimuthErrorArcMinutes,
            double altitudeErrorArcMinutes,
            double totalErrorArcMinutes,
            double maximumErrorArcMinutes,
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
            if (System.Math.Max(System.Math.Abs(totalErrorArcMinutes), componentMagnitudeArcMinutes)
                    > maximumErrorArcMinutes) {
                return new AutomatedAdjustmentInputDecision(
                    false,
                    $"the fresh error vector exceeds the {maximumErrorArcMinutes:F0}' {routeName} qualification limit");
            }

            return new AutomatedAdjustmentInputDecision(
                true,
                $"the fresh three-point result is finite and inside the {routeName} qualification limit");
        }
    }
}
