using FluentAssertions;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaDriftValidationSessionTest {
        private const double LatitudeDegrees = 25.2;

        [Test]
        public void EnforcesABCAOrderAndPreservesRawSolves() {
            var session = new TppaDriftValidationSession();

            session.TryBeginTrack("B", out var wrongOrderReason).Should().BeFalse();
            wrongOrderReason.Should().Contain("expected position A");
            session.TryBeginTrack("A", out _).Should().BeTrue();
            AddLinearSamples(session, 0.4);
            var report = session.CompleteActiveTrack(Metadata("A", -75));

            report.Samples.Should().HaveCount(61);
            report.Samples[0].ObservationTimeUtc.Kind.Should().Be(DateTimeKind.Utc);
            report.Fit.IsValid.Should().BeTrue(report.Fit.Reason);
            session.TryBeginTrack("B", out _).Should().BeTrue();
        }

        [Test]
        public void RejectsNonUtcOrNonMonotonicSolveTimes() {
            var session = new TppaDriftValidationSession();
            session.TryBeginTrack("A", out _).Should().BeTrue();
            var utc = new DateTime(2026, 7, 23, 20, 0, 0, DateTimeKind.Utc);

            session.TryAddSolve(DateTime.SpecifyKind(utc, DateTimeKind.Local), 45, out var kindReason).Should().BeFalse();
            kindReason.Should().Contain("UTC");
            session.TryAddSolve(utc, 45, out _).Should().BeTrue();
            session.TryAddSolve(utc, 45.1, out var orderReason).Should().BeFalse();
            orderReason.Should().Contain("increase");
        }

        [Test]
        public void FailsClosedWhenRefractionCorrectionIsMissing() {
            var session = BuildCompleteSession(hasRefraction: false);

            var result = session.Evaluate(LatitudeDegrees);

            result.IsComplete.Should().BeFalse();
            result.Reason.Should().Contain("refraction");
        }

        [Test]
        public void CompleteSyntheticSessionRecoversInjectedVector() {
            var session = BuildCompleteSession(hasRefraction: true);

            var result = session.Evaluate(LatitudeDegrees);

            result.IsComplete.Should().BeTrue(result.Reason);
            result.Validation.IsValid.Should().BeTrue(result.Validation.Reason);
            result.Validation.AzimuthErrorArcMinutes.Should().BeApproximately(-3, 0.05);
            result.Validation.AltitudeErrorArcMinutes.Should().BeApproximately(2, 0.05);
        }

        [Test]
        public void InvalidationPreventsAResult() {
            var session = BuildCompleteSession(hasRefraction: true);
            session.Invalidate("cloud interruption");

            var result = session.Evaluate(LatitudeDegrees);

            result.IsComplete.Should().BeFalse();
            result.Reason.Should().Contain("cloud interruption");
        }

        private static TppaDriftValidationSession BuildCompleteSession(bool hasRefraction) {
            var session = new TppaDriftValidationSession();
            var ids = new[] { "A", "B", "C", "A" };
            var hourAngles = new[] { -75.0, -20.0, 35.0, -70.0 };
            for (var index = 0; index < ids.Length; index++) {
                session.TryBeginTrack(ids[index], out _).Should().BeTrue();
                var drift = TppaDriftValidationEstimator.PredictDeclinationDriftArcsecondsPerMinute(
                    azimuthErrorArcMinutes: -3,
                    altitudeErrorArcMinutes: 2,
                    siteLatitudeDegrees: LatitudeDegrees,
                    hourAngleDegrees: hourAngles[index],
                    computedRefractionDriftArcsecondsPerMinute: 0.05);
                AddLinearSamples(session, drift);
                session.CompleteActiveTrack(new TppaDriftTrackMetadata(
                    ids[index],
                    hourAngles[index],
                    AltitudeDegrees: 40,
                    HasComputedRefractionDrift: hasRefraction,
                    ComputedRefractionDriftArcsecondsPerMinute: 0.05));
            }
            return session;
        }

        private static void AddLinearSamples(TppaDriftValidationSession session, double driftArcsecondsPerMinute) {
            var start = new DateTime(2026, 7, 23, 20, 0, 0, DateTimeKind.Utc);
            const double baseDeclinationDegrees = 45;
            for (var index = 0; index <= 60; index++) {
                var elapsedSeconds = index * 5.0;
                var driftDegrees = driftArcsecondsPerMinute / 60.0 * elapsedSeconds / 3600.0;
                var noiseDegrees = 0.15 * Math.Sin(index * 1.7) / 3600.0;
                session.TryAddSolve(start.AddSeconds(elapsedSeconds), baseDeclinationDegrees + driftDegrees + noiseDegrees, out _)
                    .Should().BeTrue();
            }
        }

        private static TppaDriftTrackMetadata Metadata(string positionId, double hourAngleDegrees) => new(
            positionId,
            hourAngleDegrees,
            AltitudeDegrees: 40,
            HasComputedRefractionDrift: true,
            ComputedRefractionDriftArcsecondsPerMinute: 0);
    }
}
