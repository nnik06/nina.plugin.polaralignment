using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaMeasurementTrendTest {
        [Test]
        public void JulyMonotonicWalkPassesMadButFailsTrendGate() {
            var start = new DateTime(2026, 7, 23, 2, 6, 41, DateTimeKind.Utc);
            var timed = new[] {
                Sample(start, 0, -30, 0, -10, 47, 31, 53),
                Sample(start, 365, -30, 43, -9, 53, 32, 16),
                Sample(start, 725, -30, 33, -9, 59, 32, 9),
                Sample(start, 1086, -30, 57, -9, 43, 32, 26),
                Sample(start, 1446, -31, 15, -9, 43, 32, 44),
            };

            var repeatability = TppaMeasurementSetEvaluator.Evaluate(
                timed.Select(sample => new TppaFreshMeasurement(
                    sample.AzimuthMinutes,
                    sample.AltitudeMinutes,
                    sample.TotalMinutes)).ToArray());
            var trend = TppaMeasurementTrendEvaluator.Evaluate(timed);

            repeatability.IsRepeatable.Should().BeTrue(repeatability.Reason);
            trend.IsStable.Should().BeFalse();
            trend.AzimuthTrendSpanMinutes.Should().BeGreaterThan(1.0);
            trend.TotalTrendSpanMinutes.Should().BeGreaterThan(40.0 / 60.0);
            trend.Reason.Should().Contain("trend gate failed");
        }

        [Test]
        public void StationarySeriesPassesTrendGate() {
            var start = new DateTime(2026, 7, 24, 0, 0, 0, DateTimeKind.Utc);
            var result = TppaMeasurementTrendEvaluator.Evaluate(new[] {
                new TppaTimedFreshMeasurement(start, -20.00, -10.00, 22.36),
                new TppaTimedFreshMeasurement(start.AddMinutes(6), -20.04, -9.98, 22.38),
                new TppaTimedFreshMeasurement(start.AddMinutes(12), -19.98, -10.03, 22.35),
                new TppaTimedFreshMeasurement(start.AddMinutes(18), -20.02, -10.01, 22.37),
                new TppaTimedFreshMeasurement(start.AddMinutes(24), -20.01, -10.00, 22.36),
            });

            result.IsStable.Should().BeTrue(result.Reason);
            result.ObservationSpanMinutes.Should().Be(24);
            result.AzimuthTrendSpanMinutes.Should().BeLessThan(30.0 / 60.0);
            result.TotalTrendSpanMinutes.Should().BeLessThan(20.0 / 60.0);
        }

        [Test]
        public void NonUtcOrNonIncreasingTimesFailClosed() {
            var utc = new DateTime(2026, 7, 24, 0, 0, 0, DateTimeKind.Utc);
            var localResult = TppaMeasurementTrendEvaluator.Evaluate(new[] {
                Point(DateTime.SpecifyKind(utc, DateTimeKind.Local)),
                Point(utc.AddMinutes(6)),
                Point(utc.AddMinutes(12)),
                Point(utc.AddMinutes(18)),
                Point(utc.AddMinutes(24)),
            });
            var orderResult = TppaMeasurementTrendEvaluator.Evaluate(new[] {
                Point(utc),
                Point(utc.AddMinutes(6)),
                Point(utc.AddMinutes(6)),
                Point(utc.AddMinutes(18)),
                Point(utc.AddMinutes(24)),
            });

            var separationResult = TppaMeasurementTrendEvaluator.Evaluate(new[] {
                Point(utc),
                Point(utc.AddSeconds(1)),
                Point(utc.AddMinutes(12)),
                Point(utc.AddMinutes(18)),
                Point(utc.AddMinutes(24)),
            });

            localResult.IsStable.Should().BeFalse();
            localResult.Reason.Should().Contain("UTC");
            orderResult.IsStable.Should().BeFalse();
            orderResult.Reason.Should().Contain("strictly increasing");
            separationResult.IsStable.Should().BeFalse();
            separationResult.Reason.Should().Contain("separated");
        }

        private static TppaTimedFreshMeasurement Sample(
            DateTime start,
            int elapsedSeconds,
            int azimuthMinutes,
            int azimuthSeconds,
            int altitudeMinutes,
            int altitudeSeconds,
            int totalMinutes,
            int totalSeconds) =>
            new(
                start.AddSeconds(elapsedSeconds),
                SignedMinutes(azimuthMinutes, azimuthSeconds),
                SignedMinutes(altitudeMinutes, altitudeSeconds),
                totalMinutes + totalSeconds / 60.0);

        private static double SignedMinutes(int minutes, int seconds) =>
            minutes < 0 ? minutes - seconds / 60.0 : minutes + seconds / 60.0;

        private static TppaTimedFreshMeasurement Point(DateTime time) =>
            new(time, -20, -10, Math.Sqrt(500));
    }
}
