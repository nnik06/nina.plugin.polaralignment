using FluentAssertions;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaDirectDynamicCorrectionAuthorityTest {
        [Test]
        public void AcceptsOnlyTheUninterruptedFreshResponse() {
            var result = TppaDirectDynamicCorrectionAuthority.Evaluate(
                true, true, 2, 2, 5, 5, 2, 2, 10, 75);
            result.CanAuthorize.Should().BeTrue(result.Reason);
        }

        [TestCase(3, 2, 5, 5, 2, 2, 10)]
        [TestCase(2, 2, 6, 5, 2, 2, 10)]
        [TestCase(2, 2, 5, 5, 3, 2, 10)]
        [TestCase(2, 2, 5, 5, 2, 2, 76)]
        public void DeniesAnyInterveningStateOrExpiry(
                int currentMotionEpoch,
                int grantedMotionEpoch,
                int currentMeasurementSequence,
                int grantedMeasurementSequence,
                int currentMoveCount,
                int grantedMoveCount,
                double ageSeconds) {
            var result = TppaDirectDynamicCorrectionAuthority.Evaluate(
                true, true,
                currentMotionEpoch, grantedMotionEpoch,
                currentMeasurementSequence, grantedMeasurementSequence,
                currentMoveCount, grantedMoveCount,
                ageSeconds, 75);
            result.CanAuthorize.Should().BeFalse(result.Reason);
        }
    }
}
