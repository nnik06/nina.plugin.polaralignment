using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class AutomatedAdjustmentInputPolicyTest {
        [Test]
        public void FiniteResultInsideLimitIsEligible() {
            var decision = AutomatedAdjustmentInputPolicy.Evaluate(59.9, -5.2, 60.1);

            decision.IsEligible.Should().BeTrue();
        }

        [Test]
        public void ExactLimitRemainsEligible() {
            var decision = AutomatedAdjustmentInputPolicy.Evaluate(96.0, -72.0, 120.0);

            decision.IsEligible.Should().BeTrue();
        }

        [Test]
        public void ResultAboveLimitIsDenied() {
            var decision = AutomatedAdjustmentInputPolicy.Evaluate(-234.2, 25.5, 235.6);

            decision.IsEligible.Should().BeFalse();
            decision.Reason.Should().Contain("120'");
        }

        [Test]
        public void ComponentMagnitudeAboveLimitIsDeniedWhenReportedTotalIsInsideLimit() {
            var decision = AutomatedAdjustmentInputPolicy.Evaluate(100.0, 100.0, 100.0);

            decision.IsEligible.Should().BeFalse();
            decision.Reason.Should().Contain("120'");
        }

        [TestCase(double.NaN, 0.0, 1.0)]
        [TestCase(0.0, double.PositiveInfinity, 1.0)]
        [TestCase(0.0, 0.0, double.NegativeInfinity)]
        public void NonFiniteResultIsDenied(
            double azimuthErrorArcMinutes,
            double altitudeErrorArcMinutes,
            double totalErrorArcMinutes) {
            var decision = AutomatedAdjustmentInputPolicy.Evaluate(
                azimuthErrorArcMinutes,
                altitudeErrorArcMinutes,
                totalErrorArcMinutes);

            decision.IsEligible.Should().BeFalse();
            decision.Reason.Should().Contain("non-finite");
        }
    }
}
