using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaFastOperationalRouteAdmissionPolicyTest {
        [Test]
        public void NonFastMotionDoesNotRequireAResponseRoute() {
            var decision = TppaFastOperationalRouteAdmissionPolicy.Evaluate(
                fastMotionRequested: false,
                qualifiedDirectFullTravelRoute: false,
                qualifiedDirectBootstrapRoute: false);

            decision.IsEligible.Should().BeTrue();
        }

        [Test]
        public void FastMotionAcceptsAQualifiedMeasuredTwoByTwoRoute() {
            var decision = TppaFastOperationalRouteAdmissionPolicy.Evaluate(
                fastMotionRequested: true,
                qualifiedDirectFullTravelRoute: true,
                qualifiedDirectBootstrapRoute: false);

            decision.IsEligible.Should().BeTrue();
            decision.Reason.Should().Contain("2x2");
        }

        [Test]
        public void FastMotionAcceptsMeasuredXWithBoundedYBootstrap() {
            var decision = TppaFastOperationalRouteAdmissionPolicy.Evaluate(
                fastMotionRequested: true,
                qualifiedDirectFullTravelRoute: false,
                qualifiedDirectBootstrapRoute: true);

            decision.IsEligible.Should().BeTrue();
            decision.Reason.Should().Contain("bounded Y bootstrap");
        }

        [Test]
        public void FastMotionRejectsAnUnmeasuredYCorrectionPath() {
            var decision = TppaFastOperationalRouteAdmissionPolicy.Evaluate(
                fastMotionRequested: true,
                qualifiedDirectFullTravelRoute: false,
                qualifiedDirectBootstrapRoute: false);

            decision.IsEligible.Should().BeFalse();
            decision.Reason.Should().Contain("unmeasured Y axis");
        }
    }
}
