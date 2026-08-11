using FluentAssertions;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaMountMotionEnvelopeTest {
        private static readonly TppaMountMotionEnvelope Balcony = new(
            MinimumAltitudeDegrees: 25,
            MaximumAltitudeDegrees: 55,
            AzimuthStartDegrees: 270,
            AzimuthEndDegrees: 10);

        [TestCase(270, 25)]
        [TestCase(315, 40)]
        [TestCase(359.9, 55)]
        [TestCase(0, 35)]
        [TestCase(10, 45)]
        public void Validate_AcceptsWrappedBalconyEnvelope(double azimuth, double altitude) {
            Balcony.Validate(azimuth, altitude).Should().BeEmpty();
        }

        [TestCase(269.9, 40, "azimuth")]
        [TestCase(10.1, 40, "azimuth")]
        [TestCase(300, 24.9, "altitude")]
        [TestCase(300, 55.1, "altitude")]
        public void Validate_RejectsOutsideEnvelope(double azimuth, double altitude, string axis) {
            Balcony.Validate(azimuth, altitude).Should().Contain(axis);
        }

        [Test]
        public void Validate_RejectsInvalidConfiguration() {
            new TppaMountMotionEnvelope(55, 25, 270, 10)
                .Validate(300, 40)
                .Should().Contain("configured altitude envelope");
        }

        [Test]
        public void GetConfigurationIssue_RejectsInvalidConfigurationWithoutAProbePosition() {
            new TppaMountMotionEnvelope(55, 25, 270, 10)
                .GetConfigurationIssue()
                .Should().Contain("configured altitude envelope");
        }
    }
}
