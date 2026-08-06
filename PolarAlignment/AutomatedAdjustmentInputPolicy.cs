namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct AutomatedAdjustmentInputDecision(
        bool IsEligible,
        string Reason);

    internal static class AutomatedAdjustmentInputPolicy {
        // The legacy controller remains limited to the range qualified before
        // supervisor-backed coarse planning was introduced.
        public const double MaximumInitialErrorArcMinutes = 120.0;
        // The physical UPAS guard remains inside the approximately +/-6 degree
        // hardware stops. The field controller may admit +/-5.4 degrees on
        // either axis only after its independent fresh-determination gate.
        public const double MaximumFieldInitialAxisErrorArcMinutes = 324.0;
        public const double MaximumFieldInitialTotalErrorArcMinutes = 458.205195;

        // Retained for compatibility with the supervisor campaign vocabulary.
        public const double MaximumSupervisorCoarseInitialErrorArcMinutes =
            MaximumFieldInitialTotalErrorArcMinutes;

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
                MaximumFieldInitialAxisErrorArcMinutes,
                MaximumFieldInitialTotalErrorArcMinutes,
                "supervisor coarse-correction");
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
