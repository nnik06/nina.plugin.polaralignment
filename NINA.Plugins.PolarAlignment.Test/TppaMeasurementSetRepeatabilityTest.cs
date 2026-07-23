using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaMeasurementSetRepeatabilityTest {
        [Test]
        public void TonightInitialSameArcSetPassesFieldGate() {
            var result = TppaMeasurementSetEvaluator.Evaluate(new[] {
                Sample(-20, 27, -19, 58, 28, 35),
                Sample(-20, 49, -19, 50, 28, 45),
                Sample(-20, 36, -19, 54, 28, 38),
                Sample(-20, 12, -20, 03, 28, 28),
                Sample(-20, 31, -19, 58, 28, 37),
                Sample(-20, 49, -19, 48, 28, 44),
                Sample(-20, 23, -20, 04, 28, 36),
                Sample(-20, 27, -19, 59, 28, 36),
                Sample(-21, 00, -19, 44, 28, 49),
                Sample(-21, 01, -19, 39, 28, 46),
            });

            result.IsRepeatable.Should().BeTrue();
            result.MedianTotalMinutes.Should().BeApproximately(28 + 37.5 / 60.0, 1e-9);
            result.AzimuthMadMinutes.Should().BeApproximately(13.0 / 60.0, 1e-9);
            result.AltitudeMadMinutes.Should().BeApproximately(6.5 / 60.0, 1e-9);
            result.TotalMadMinutes.Should().BeApproximately(4.5 / 60.0, 1e-9);
        }

        [Test]
        public void TonightReturnSameArcSetPassesFieldGate() {
            var result = TppaMeasurementSetEvaluator.Evaluate(new[] {
                Sample(-18, 56, -21, 54, 28, 57),
                Sample(-19, 23, -21, 35, 29, 00),
                Sample(-18, 53, -21, 49, 28, 51),
            });

            result.IsRepeatable.Should().BeTrue();
            result.AzimuthMadMinutes.Should().BeApproximately(3.0 / 60.0, 1e-9);
            result.AltitudeMadMinutes.Should().BeApproximately(5.0 / 60.0, 1e-9);
            result.TotalMadMinutes.Should().BeApproximately(3.0 / 60.0, 1e-9);
        }

        [Test]
        public void TwoMeasurementsCannotAuthorizeAdjustment() {
            var result = TppaMeasurementSetEvaluator.Evaluate(new[] {
                Sample(-18, 31, -24, 13, 30, 29),
                Sample(-16, 42, -21, 46, 27, 26),
            });

            result.IsRepeatable.Should().BeFalse();
            result.Reason.Should().Contain("at least 3");
        }

        [Test]
        public void ComponentScatterFailsEvenWhenTotalLooksStable() {
            var result = TppaMeasurementSetEvaluator.Evaluate(new[] {
                new TppaFreshMeasurement(-20.0, -20.0, 28.30),
                new TppaFreshMeasurement(-18.0, -22.0, 28.31),
                new TppaFreshMeasurement(-16.0, -24.0, 28.29),
            });

            result.IsRepeatable.Should().BeFalse();
            result.TotalMadMinutes.Should().BeLessThan(10.0 / 60.0);
            result.Reason.Should().Contain("MAD gate failed");
        }

        [Test]
        public void InvalidMeasurementFailsClosed() {
            var result = TppaMeasurementSetEvaluator.Evaluate(new[] {
                new TppaFreshMeasurement(0, 0, 0),
                new TppaFreshMeasurement(double.NaN, 0, 0),
                new TppaFreshMeasurement(0, 0, 0),
            });

            result.IsRepeatable.Should().BeFalse();
            result.Reason.Should().Contain("invalid");
        }

        private static TppaFreshMeasurement Sample(
            int azimuthMinutes, int azimuthSeconds,
            int altitudeMinutes, int altitudeSeconds,
            int totalMinutes, int totalSeconds) =>
            new(
                azimuthMinutes - azimuthSeconds / 60.0,
                altitudeMinutes - altitudeSeconds / 60.0,
                totalMinutes + totalSeconds / 60.0);
    }
}
