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
        public void ResultAboveDirectLimitIsDenied() {
            var decision = AutomatedAdjustmentInputPolicy.Evaluate(-120.1, 0.0, 120.1);

            decision.IsEligible.Should().BeFalse();
            decision.Reason.Should().Contain("120'");
        }

        [Test]
        public void ComponentMagnitudeAboveDirectLimitIsDeniedWhenReportedTotalIsInsideLimit() {
            var decision = AutomatedAdjustmentInputPolicy.Evaluate(100.0, 100.0, 100.0);

            decision.IsEligible.Should().BeFalse();
            decision.Reason.Should().Contain("120'");
        }

        [Test]
        public void RouteSelectorKeepsDirectAndSupervisorEnvelopesSeparate() {
            var direct = AutomatedAdjustmentInputPolicy.EvaluateForRoute(
                180.0, 0.0, 180.0, supervisorCoarseRoute: false);
            var supervisor = AutomatedAdjustmentInputPolicy.EvaluateForRoute(
                180.0, 0.0, 180.0, supervisorCoarseRoute: true);

            direct.IsEligible.Should().BeFalse();
            direct.Reason.Should().Contain("direct field");
            supervisor.IsEligible.Should().BeTrue();
            supervisor.Reason.Should().Contain("supervisor");
        }

        [Test]
        public void SupervisorCoarseRouteAcceptsFullFieldEnvelope() {
            var decision = AutomatedAdjustmentInputPolicy.EvaluateSupervisorCoarse(
                324.0, -324.0, 458.205195);

            decision.IsEligible.Should().BeTrue();
            decision.Reason.Should().Contain("supervisor coarse-correction");
        }

        [Test]
        public void SupervisorCoarseRouteRejectsAxisAboveFivePointFourDegrees() {
            var decision = AutomatedAdjustmentInputPolicy.EvaluateSupervisorCoarse(
                -324.1, 0.0, 324.1);

            decision.IsEligible.Should().BeFalse();
            decision.Reason.Should().Contain("324'");
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
