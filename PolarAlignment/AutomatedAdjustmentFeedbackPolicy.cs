namespace NINA.Plugins.PolarAlignment {
    internal static class AutomatedAdjustmentFeedbackPolicy {
        public static bool AllowsContinuousFeedback(bool requiresFreshMeasurementFeedback) {
            return !requiresFreshMeasurementFeedback;
        }
    }
}
