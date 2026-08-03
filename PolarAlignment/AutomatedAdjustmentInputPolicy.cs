namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct AutomatedAdjustmentInputDecision(
        bool IsEligible,
        string Reason);

    internal static class AutomatedAdjustmentInputPolicy {
        // The preregistered campaign uses the UPAS operational starting envelope,
        // while the supervisor independently enforces physical position limits.
        public const double MaximumInitialErrorArcMinutes = 300.0;

        public static AutomatedAdjustmentInputDecision Evaluate(
            double azimuthErrorArcMinutes,
            double altitudeErrorArcMinutes,
            double totalErrorArcMinutes) {
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
                    > MaximumInitialErrorArcMinutes) {
                return new AutomatedAdjustmentInputDecision(
                    false,
                    $"the fresh error vector exceeds the {MaximumInitialErrorArcMinutes:F0}' automated-correction qualification limit");
            }

            return new AutomatedAdjustmentInputDecision(
                true,
                "the fresh three-point result is finite and inside the automated-correction qualification limit");
        }
    }
}
