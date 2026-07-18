using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class AutomatedAdjustmentFeedbackPolicyTest {
        [Test]
        public void ContinuousFeedbackIsRejectedForFreshFeedbackController() {
            AutomatedAdjustmentFeedbackPolicy.AllowsContinuousFeedback(requiresFreshMeasurementFeedback: true).Should().BeFalse();
        }

        [Test]
        public void ContinuousFeedbackRemainsAvailableForLegacyControllers() {
            AutomatedAdjustmentFeedbackPolicy.AllowsContinuousFeedback(requiresFreshMeasurementFeedback: false).Should().BeTrue();
        }
    }
}
