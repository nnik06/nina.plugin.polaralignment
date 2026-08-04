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
            var decision = AutomatedAdjustmentInputPolicy.Evaluate(-120.1, 0.0, 120.1);

            decision.IsEligible.Should().BeFalse();
            decision.Reason.Should().Contain("120'");
        }

        [Test]
        public void ComponentMagnitudeAboveLimitIsDeniedWhenReportedTotalIsInsideLimit() {
            var decision = AutomatedAdjustmentInputPolicy.Evaluate(100.0, 100.0, 100.0);

            decision.IsEligible.Should().BeFalse();
            decision.Reason.Should().Contain("120'");
        }

        [Test]
        public void RouteSelectorKeepsLegacyAndSupervisorEnvelopesSeparate() {
            var legacy = AutomatedAdjustmentInputPolicy.EvaluateForRoute(
                180.0, 0.0, 180.0, supervisorCoarseRoute: false);
            var supervisor = AutomatedAdjustmentInputPolicy.EvaluateForRoute(
                180.0, 0.0, 180.0, supervisorCoarseRoute: true);

            legacy.IsEligible.Should().BeFalse();
            legacy.Reason.Should().Contain("legacy");
            supervisor.IsEligible.Should().BeTrue();
            supervisor.Reason.Should().Contain("supervisor");
        }

        [Test]
        public void SupervisorCoarseRouteAcceptsExactFiveDegreeVector() {
            var decision = AutomatedAdjustmentInputPolicy.EvaluateSupervisorCoarse(
                240.0, -180.0, 300.0);

            decision.IsEligible.Should().BeTrue();
            decision.Reason.Should().Contain("supervisor coarse-correction");
        }

        [Test]
        public void SupervisorCoarseRouteRejectsVectorAboveFiveDegrees() {
            var decision = AutomatedAdjustmentInputPolicy.EvaluateSupervisorCoarse(
                -300.1, 0.0, 300.1);

            decision.IsEligible.Should().BeFalse();
            decision.Reason.Should().Contain("300'");
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
