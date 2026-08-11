using FluentAssertions;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaOperationalAlignmentTierPolicyTest {
        [TestCase(1.5)]
        [TestCase(3.0)]
        public void ImagingToleranceReportsImagingReadyTier(double toleranceMinutes) {
            var decision = TppaOperationalAlignmentTierPolicy.Evaluate(toleranceMinutes);

            decision.Tier.Should().Be(TppaOperationalAlignmentTier.ImagingReady);
            decision.IsImagingReady.Should().BeTrue();
            decision.CompletionClaim.Should().Contain("imaging-ready");
        }

        [TestCase(3.1)]
        [TestCase(24.0)]
        public void CoarseToleranceNeverReportsImagingReadiness(double toleranceMinutes) {
            var decision = TppaOperationalAlignmentTierPolicy.Evaluate(toleranceMinutes);

            decision.Tier.Should().Be(TppaOperationalAlignmentTier.TripodFreeCoarse);
            decision.IsImagingReady.Should().BeFalse();
            decision.CompletionClaim.Should().Contain("not imaging readiness");
        }

        [TestCase(24.1)]
        [TestCase(0.0)]
        [TestCase(-1.0)]
        public void UnsupportedToleranceMakesNoOperationalCompletionClaim(double toleranceMinutes) {
            var decision = TppaOperationalAlignmentTierPolicy.Evaluate(toleranceMinutes);

            decision.Tier.Should().Be(TppaOperationalAlignmentTier.NonOperational);
            decision.IsImagingReady.Should().BeFalse();
            decision.CompletionClaim.Should().Contain("no operational completion claim");
        }
    }
}
