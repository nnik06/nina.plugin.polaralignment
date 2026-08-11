using FluentAssertions;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaClampLimitedRecoveryPolicyTest {
        [Test]
        public void Evaluate_UsesFullClampOnlyForItsOwnSaturatedAxis() {
            var result = TppaClampLimitedRecoveryPolicy.Evaluate(162, 20, 81, 75, 2, 0.05);

            result.IsEligible.Should().BeTrue(result.Reason);
            result.ClampLimitedX.Should().BeTrue();
            result.ClampLimitedY.Should().BeFalse();
            result.XUnits.Should().Be(81);
            result.YUnits.Should().BeApproximately(13, 1e-9);
        }

        [TestCase(5.1, 0.05)]
        [TestCase(2, 0.101)]
        public void Evaluate_RejectsUnqualifiedResponse(double conditionNumber, double uncertainty) {
            var result = TppaClampLimitedRecoveryPolicy.Evaluate(162, 150, 81, 75, conditionNumber, uncertainty);

            result.IsEligible.Should().BeFalse();
        }
    }
}
