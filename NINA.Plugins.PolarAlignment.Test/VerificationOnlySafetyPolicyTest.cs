using FluentAssertions;
using NINA.Core.Enum;

namespace NINA.Plugins.PolarAlignment.Test {
    public class VerificationOnlySafetyPolicyTest {
        private static readonly TppaMountMotionEnvelope BalconyEnvelope =
            new(25.0, 55.0, 270.0, 10.0);

        [Test]
        public void VerificationSettleOverrideUsesProfileWhenUnset() {
            TppaVerificationSettlePolicy.Resolve(3.5, 0.0).Should().Be(3.5);
            TppaVerificationSettlePolicy.Resolve(45.0, 0.0).Should().Be(45.0);
        }

        [Test]
        public void VerificationSettleOverrideReplacesProfileAndIsBounded() {
            TppaVerificationSettlePolicy.Resolve(3.5, 10.0).Should().Be(10.0);
            TppaVerificationSettlePolicy.Resolve(3.5, 100.0)
                .Should().Be(TppaVerificationSettlePolicy.MaximumOverrideSeconds);
        }

        [Test]
        public void VerificationSlewAcceptsSafeConstantPierSideDestination() {
            var result = VerificationOnlySlewSafetyPolicy.Evaluate(
                true,
                BalconyEnvelope,
                315.0,
                30.0,
                PierSide.pierEast,
                PierSide.pierEast);

            result.IsSafe.Should().BeTrue();
        }

        [Test]
        public void VerificationSlewRejectsDestinationOutsideEnvelope() {
            var result = VerificationOnlySlewSafetyPolicy.Evaluate(
                true,
                BalconyEnvelope,
                20.0,
                30.0,
                PierSide.pierEast,
                PierSide.pierEast);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("azimuth");
        }

        [Test]
        public void VerificationSlewRejectsKnownPierSideChange() {
            var result = VerificationOnlySlewSafetyPolicy.Evaluate(
                true,
                BalconyEnvelope,
                315.0,
                30.0,
                PierSide.pierEast,
                PierSide.pierWest);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("pier side changes");
        }

        [Test]
        public void VerificationSlewRejectsUnknownPierSide() {
            var result = VerificationOnlySlewSafetyPolicy.Evaluate(
                true,
                BalconyEnvelope,
                315.0,
                30.0,
                PierSide.pierUnknown,
                PierSide.pierUnknown);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("unavailable");
        }
    }
}
