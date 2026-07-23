using FluentAssertions;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaDriftTrackAcquisitionRunnerTest {
        private static readonly DateTime Start = new(2026, 7, 23, 20, 0, 0, DateTimeKind.Utc);

        [Test]
        public async Task CollectsUntilExposureMidpointSpanReachesTarget() {
            var session = new TppaDriftValidationSession();
            var captureCount = 0;
            var delayCount = 0;

            var report = await TppaDriftTrackAcquisitionRunner.Run(
                session,
                Metadata("A"),
                _ => Task.FromResult(Sample(captureCount++)),
                Policy(maximumSampleCount: 100),
                qualificationPolicy: null,
                CancellationToken.None,
                (_, _) => {
                    delayCount++;
                    return Task.CompletedTask;
                });

            captureCount.Should().Be(31);
            delayCount.Should().Be(30);
            report.Samples.Should().HaveCount(31);
            report.Fit.DurationSeconds.Should().Be(300);
            report.Fit.IsValid.Should().BeTrue(report.Fit.Reason);
        }

        [Test]
        public async Task UsesExposureTimesRatherThanRequestedDelayForDuration() {
            var session = new TppaDriftValidationSession();
            var index = 0;

            var report = await TppaDriftTrackAcquisitionRunner.Run(
                session,
                Metadata("A"),
                _ => {
                    var sample = new TppaDriftSolveSample(
                        Start.AddSeconds(index * 30),
                        45 + index++ * 0.1 / 3600);
                    return Task.FromResult(sample);
                },
                Policy(maximumSampleCount: 20) with {
                    CadenceDelay = TimeSpan.Zero
                },
                TppaDeclinationDriftTrackPolicy.FieldDefault with {
                    MinimumSampleCount = 10
                },
                CancellationToken.None,
                (_, _) => Task.CompletedTask);

            report.Samples.Should().HaveCount(11);
            report.Fit.DurationSeconds.Should().Be(300);
        }

        [Test]
        public async Task FieldDefaultsQualifyRealisticThirtySecondSolveCadence() {
            var session = new TppaDriftValidationSession();
            var index = 0;
            var residualPatternArcseconds = new[] { 0.08, -0.04, 0.02, -0.06, 0.05 };

            var report = await TppaDriftTrackAcquisitionRunner.Run(
                session,
                Metadata("A"),
                _ => {
                    var elapsedSeconds = index * 35;
                    var driftArcseconds = 0.4 * elapsedSeconds / 60;
                    var residualArcseconds = residualPatternArcseconds[index % residualPatternArcseconds.Length];
                    index++;
                    return Task.FromResult(new TppaDriftSolveSample(
                        Start.AddSeconds(elapsedSeconds),
                        45 + (driftArcseconds + residualArcseconds) / 3600));
                },
                TppaDriftTrackAcquisitionPolicy.FieldDefault,
                TppaDeclinationDriftTrackPolicy.FieldDefault,
                CancellationToken.None,
                (_, _) => Task.CompletedTask);

            report.Samples.Should().HaveCount(10);
            report.Fit.AcceptedSampleCount.Should().BeGreaterThanOrEqualTo(8);
            report.Fit.DurationSeconds.Should().Be(315);
            report.Fit.DeclinationDriftArcsecondsPerMinute.Should().BeApproximately(0.4, 0.02);
            report.Fit.IsValid.Should().BeTrue(report.Fit.Reason);
        }

        [Test]
        public async Task CancellationInvalidatesSessionAndIsRethrown() {
            var session = new TppaDriftValidationSession();
            using var cts = new CancellationTokenSource();
            var index = 0;

            Func<Task> act = () => TppaDriftTrackAcquisitionRunner.Run(
                session,
                Metadata("A"),
                _ => {
                    var sample = Sample(index++);
                    if (index == 4) {
                        cts.Cancel();
                    }
                    return Task.FromResult(sample);
                },
                Policy(maximumSampleCount: 100),
                qualificationPolicy: null,
                cts.Token,
                (_, _) => Task.CompletedTask);

            await act.Should().ThrowAsync<OperationCanceledException>();
            var result = session.Evaluate(25.2);
            result.IsComplete.Should().BeFalse();
            result.Reason.Should().Contain("cancelled");
        }

        [Test]
        public async Task CaptureFailureInvalidatesSessionAndIsRethrown() {
            var session = new TppaDriftValidationSession();
            var index = 0;

            Func<Task> act = () => TppaDriftTrackAcquisitionRunner.Run(
                session,
                Metadata("A"),
                _ => index++ == 2
                    ? throw new InvalidOperationException("plate solve failed")
                    : Task.FromResult(Sample(index)),
                Policy(maximumSampleCount: 100),
                qualificationPolicy: null,
                CancellationToken.None,
                (_, _) => Task.CompletedTask);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*plate solve failed*");
            var result = session.Evaluate(25.2);
            result.IsComplete.Should().BeFalse();
            result.Reason.Should().Contain("plate solve failed");
        }

        [Test]
        public async Task RejectsNonMonotonicSolveBeforeCompletingTrack() {
            var session = new TppaDriftValidationSession();
            var index = 0;

            Func<Task> act = () => TppaDriftTrackAcquisitionRunner.Run(
                session,
                Metadata("A"),
                _ => Task.FromResult(new TppaDriftSolveSample(Start, 45 + index++ / 3600.0)),
                Policy(maximumSampleCount: 100),
                qualificationPolicy: null,
                CancellationToken.None,
                (_, _) => Task.CompletedTask);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*increase strictly*");
            session.HasActiveTrack.Should().BeTrue();
            session.Evaluate(25.2).Reason.Should().Contain("increase strictly");
        }

        [Test]
        public async Task BoundedSampleCountPreventsEndlessShortSpan() {
            var session = new TppaDriftValidationSession();
            var index = 0;

            Func<Task> act = () => TppaDriftTrackAcquisitionRunner.Run(
                session,
                Metadata("A"),
                _ => Task.FromResult(new TppaDriftSolveSample(
                    Start.AddSeconds(index),
                    45 + index++ * 0.01 / 3600)),
                Policy(maximumSampleCount: 5),
                qualificationPolicy: null,
                CancellationToken.None,
                (_, _) => Task.CompletedTask);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*within 5 solves*");
            session.Evaluate(25.2).Reason.Should().Contain("within 5 solves");
        }

        [Test]
        public async Task RejectsUnsafeDurationBeforeOpeningTrack() {
            var session = new TppaDriftValidationSession();

            Func<Task> act = () => TppaDriftTrackAcquisitionRunner.Run(
                session,
                Metadata("A"),
                _ => Task.FromResult(Sample(0)),
                Policy(maximumSampleCount: 100) with {
                    TargetDuration = TimeSpan.FromMinutes(2)
                },
                qualificationPolicy: null,
                CancellationToken.None);

            await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
            session.HasActiveTrack.Should().BeFalse();
        }

        private static TppaDriftTrackAcquisitionPolicy Policy(int maximumSampleCount) => new(
            TargetDuration: TimeSpan.FromMinutes(5),
            CadenceDelay: TimeSpan.FromSeconds(1),
            MaximumSampleCount: maximumSampleCount);

        private static TppaDriftTrackMetadata Metadata(string positionId) => new(
            positionId,
            HourAngleDegrees: -45,
            AltitudeDegrees: 40,
            HasComputedRefractionDrift: true,
            ComputedRefractionDriftArcsecondsPerMinute: 0.05);

        private static TppaDriftSolveSample Sample(int index) => new(
            Start.AddSeconds(index * 10),
            45 + (0.4 / 60 * index * 10) / 3600);
    }
}
