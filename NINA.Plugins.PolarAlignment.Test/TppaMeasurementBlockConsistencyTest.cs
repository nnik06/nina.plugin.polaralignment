using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaMeasurementBlockConsistencyTest {
        [Test]
        public void TonightSameArcReturnFailsComponentAndPhaseGate() {
            var initial = Set(
                (-20, 27, -19, 58, 28, 35),
                (-20, 49, -19, 50, 28, 45),
                (-20, 36, -19, 54, 28, 38),
                (-20, 12, -20, 03, 28, 28),
                (-20, 31, -19, 58, 28, 37),
                (-20, 49, -19, 48, 28, 44),
                (-20, 23, -20, 04, 28, 36),
                (-20, 27, -19, 59, 28, 36),
                (-21, 00, -19, 44, 28, 49),
                (-21, 01, -19, 39, 28, 46));
            var returned = Set(
                (-18, 56, -21, 54, 28, 57),
                (-19, 23, -21, 35, 29, 00),
                (-18, 53, -21, 49, 28, 51));

            var result = TppaMeasurementBlockConsistencyEvaluator.Evaluate(initial, returned);

            result.IsConsistent.Should().BeFalse();
            result.AzimuthDeltaMinutes.Should().BeApproximately(1 + 37.5 / 60.0, 1e-9);
            result.AltitudeDeltaMinutes.Should().BeApproximately(-(1 + 53.0 / 60.0), 1e-9);
            result.TotalDeltaMinutes.Should().BeApproximately(19.5 / 60.0, 1e-9);
            result.PhaseDeltaDegrees.Should().BeGreaterThan(2.5);
            result.Reason.Should().Contain("gate failed");
        }

        [Test]
        public void SmallSameArcShiftPasses() {
            var first = RepeatableSet(-20.0, -20.0, 28.284);
            var second = RepeatableSet(-19.8, -20.2, 28.286);

            var result = TppaMeasurementBlockConsistencyEvaluator.Evaluate(first, second);

            result.IsConsistent.Should().BeTrue();
            result.PhaseDeltaDegrees.Should().BeLessThan(2.5);
        }

        [Test]
        public void CircularPhaseDifferenceHandlesWrap() {
            TppaPolarErrorVector.CircularDistanceDegrees(179.0, -179.0)
                .Should().BeApproximately(2.0, 1e-9);
        }

        [Test]
        public void NonRepeatableBlockFailsClosed() {
            var good = RepeatableSet(-20.0, -20.0, 28.284);
            var bad = TppaMeasurementSetEvaluator.Evaluate(new[] {
                new TppaFreshMeasurement(-20, -20, 28.284),
                new TppaFreshMeasurement(-18, -22, 28.284),
                new TppaFreshMeasurement(-16, -24, 28.284),
            });

            var result = TppaMeasurementBlockConsistencyEvaluator.Evaluate(good, bad);

            result.IsConsistent.Should().BeFalse();
            result.Reason.Should().Contain("both measurement blocks");
        }

        [Test]
        public void LogStringUsesInvariantStructuredValues() {
            TppaPolarErrorVector.FromMinutes(-20.5, -19.5, 28.3)
                .ToLogString()
                .Should().Be("Az=-20.500', Alt=-19.500', Total=28.300', Phase=-136.432 deg");
        }

        private static TppaMeasurementSetResult RepeatableSet(double azimuth, double altitude, double total) =>
            TppaMeasurementSetEvaluator.Evaluate(new[] {
                new TppaFreshMeasurement(azimuth - 0.01, altitude + 0.01, total - 0.01),
                new TppaFreshMeasurement(azimuth, altitude, total),
                new TppaFreshMeasurement(azimuth + 0.01, altitude - 0.01, total + 0.01),
            });

        private static TppaMeasurementSetResult Set(params (int azMin, int azSec, int altMin, int altSec, int totalMin, int totalSec)[] values) =>
            TppaMeasurementSetEvaluator.Evaluate(values.Select(value => new TppaFreshMeasurement(
                value.azMin - value.azSec / 60.0,
                value.altMin - value.altSec / 60.0,
                value.totalMin + value.totalSec / 60.0)).ToArray());
    }
}
