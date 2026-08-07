using FluentAssertions;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaAutomatedArcEnvelopePolicyTest {
        private static readonly TppaMountMotionEnvelope Balcony = new(
            MinimumAltitudeDegrees: 25,
            MaximumAltitudeDegrees: 69,
            AzimuthStartDegrees: 270,
            AzimuthEndDegrees: 10);

        [Test]
        public void Evaluate_RejectsTheEntireArcWhenTheThirdSampleBreachesTheCeiling() {
            var result = TppaAutomatedArcEnvelopePolicy.Evaluate(
                true,
                Balcony,
                new[] {
                    new TppaArcEnvelopeSample("B", 320, 58),
                    new TppaArcEnvelopeSample("C", 338, 70.23)
                });

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("C").And.Contain("altitude");
        }

        [Test]
        public void Evaluate_AcceptsEverySampleInsideTheConfiguredEnvelope() {
            var result = TppaAutomatedArcEnvelopePolicy.Evaluate(
                true,
                Balcony,
                new[] {
                    new TppaArcEnvelopeSample("B", 320, 48),
                    new TppaArcEnvelopeSample("C", 338, 66)
                });

            result.IsSafe.Should().BeTrue();
        }

        [Test]
        public void Evaluate_DoesNotIntroduceAnEnvelopeGateWhenItIsDisabled() {
            var result = TppaAutomatedArcEnvelopePolicy.Evaluate(
                false,
                Balcony,
                new[] { new TppaArcEnvelopeSample("C", 180, 80) });

            result.IsSafe.Should().BeTrue();
        }
    }
}
