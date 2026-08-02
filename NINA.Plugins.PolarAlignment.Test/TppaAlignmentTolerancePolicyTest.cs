using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaAlignmentTolerancePolicyTest {
        [TestCase(0.0)]
        [TestCase(-1.0)]
        public void ResolvePersistedSetting_PreservesDisabledState(double configured) {
            TppaAlignmentTolerancePolicy.ResolvePersistedSetting(configured)
                .Should().Be(0.0);
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void ResolvePersistedSetting_ReplacesNonFiniteValue(double configured) {
            TppaAlignmentTolerancePolicy.ResolvePersistedSetting(configured)
                .Should().Be(3.0);
        }

        [Test]
        public void ResolvePersistedSetting_ClampsPositiveSubminimumValue() {
            TppaAlignmentTolerancePolicy.ResolvePersistedSetting(0.1)
                .Should().Be(0.5);
        }

        [Test]
        public void ClampUserInput_ClampsPositiveSubminimumValue() {
            TppaAlignmentTolerancePolicy.ClampUserInput(0.1)
                .Should().Be(0.5);
        }

        [TestCase(0.0)]
        [TestCase(-1.0)]
        public void ClampUserInput_PreservesDisabledState(double requested) {
            TppaAlignmentTolerancePolicy.ClampUserInput(requested)
                .Should().Be(0.0);
        }

        [TestCase(0.5)]
        [TestCase(3.0)]
        [TestCase(10.0)]
        public void ResolvePersistedSetting_PreservesValidExplicitValue(double configured) {
            TppaAlignmentTolerancePolicy.ResolvePersistedSetting(configured)
                .Should().Be(configured);
        }
    }
}
