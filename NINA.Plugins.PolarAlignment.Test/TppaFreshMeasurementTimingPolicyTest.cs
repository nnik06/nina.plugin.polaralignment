using FluentAssertions;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaFreshMeasurementTimingPolicyTest {
        [Test]
        public void OperationalUpasProfile_UsesOnlyTheThreeMeasurementPoints() {
            TppaFreshMeasurementTimingPolicy.RequiresReturnField(
                    operationalUpasProfile: true,
                    manualMode: false,
                    mountConnected: true)
                .Should().BeFalse();
        }

        [TestCase(false, false, true)]
        [TestCase(false, true, true)]
        [TestCase(false, false, false)]
        public void NonOperationalPaths_PreserveTheirExistingReturnFieldBehavior(
                bool operationalUpasProfile,
                bool manualMode,
                bool mountConnected) {
            TppaFreshMeasurementTimingPolicy.RequiresReturnField(
                    operationalUpasProfile,
                    manualMode,
                    mountConnected)
                .Should().Be(!manualMode && mountConnected);
        }
    }
}
