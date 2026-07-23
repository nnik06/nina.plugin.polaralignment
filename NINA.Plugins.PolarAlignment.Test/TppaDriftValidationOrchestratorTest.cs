using FluentAssertions;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaDriftValidationOrchestratorTest {
        private const double LatitudeDegrees = 25.2;
        private static readonly DateTime Start = new(2026, 7, 23, 20, 0, 0, DateTimeKind.Utc);

        [Test]
        public async Task RunsExactAbcaOrderAndReturnsQualifiedReport() {
            var moves = new List<string>();
            var metadataPositions = new List<string>();
            var solvePositions = new List<string>();
            var sampleIndexByPosition = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var visitIndex = 0;

            var report = await TppaDriftValidationOrchestrator.Run(
                LatitudeDegrees,
                (positionId, _) => {
                    moves.Add(positionId);
                    visitIndex++;
                    sampleIndexByPosition[positionId] = 0;
                    return Task.CompletedTask;
                },
                (positionId, _) => {
                    metadataPositions.Add($"{visitIndex}:{positionId}");
                    return Task.FromResult(Metadata(positionId));
                },
                (positionId, _) => {
                    solvePositions.Add(positionId);
                    var sampleIndex = sampleIndexByPosition[positionId]++;
                    return Task.FromResult(Sample(positionId, sampleIndex));
                },
                AcquisitionPolicy(),
                TrackPolicy(),
                ValidationPolicy(),
                CancellationToken.None,
                (_, _) => Task.CompletedTask);

            moves.Should().Equal("A", "B", "C", "A");
            metadataPositions.Should().Equal("1:A", "2:B", "3:C", "4:A");
            solvePositions.Should().HaveCount(124);
            report.IsComplete.Should().BeTrue(report.Reason);
            report.Tracks.Select(track => track.Metadata.PositionId)
                .Should().Equal("A", "B", "C", "A");
            report.Validation.IsValid.Should().BeTrue(report.Validation.Reason);
        }

        [Test]
        public async Task DoesNotMovePastTrackThatFails() {
            var moves = new List<string>();
            var sampleIndexByPosition = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            Func<Task> act = () => TppaDriftValidationOrchestrator.Run(
                LatitudeDegrees,
                (positionId, _) => {
                    moves.Add(positionId);
                    sampleIndexByPosition[positionId] = 0;
                    return Task.CompletedTask;
                },
                (positionId, _) => Task.FromResult(Metadata(positionId)),
                (positionId, _) => {
                    var sampleIndex = sampleIndexByPosition[positionId]++;
                    if (positionId == "B" && sampleIndex == 2) {
                        throw new InvalidOperationException("synthetic solve failure");
                    }
                    return Task.FromResult(Sample(positionId, sampleIndex));
                },
                AcquisitionPolicy(),
                TrackPolicy(),
                ValidationPolicy(),
                CancellationToken.None,
                (_, _) => Task.CompletedTask);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*synthetic solve failure*");
            moves.Should().Equal("A", "B");
        }

        [Test]
        public async Task MovementFailureDoesNotAttemptRecoverySlewOrCapture() {
            var moves = new List<string>();
            var captures = new List<string>();

            Func<Task> act = () => TppaDriftValidationOrchestrator.Run(
                LatitudeDegrees,
                (positionId, _) => {
                    moves.Add(positionId);
                    if (positionId == "B") {
                        throw new InvalidOperationException("synthetic mount limit");
                    }
                    return Task.CompletedTask;
                },
                (positionId, _) => Task.FromResult(Metadata(positionId)),
                (positionId, _) => {
                    captures.Add(positionId);
                    return Task.FromResult(Sample(positionId, captures.Count - 1));
                },
                AcquisitionPolicy(),
                TrackPolicy(),
                ValidationPolicy(),
                CancellationToken.None,
                (_, _) => Task.CompletedTask);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*synthetic mount limit*");
            moves.Should().Equal("A", "B");
            moves.Should().NotContain("C");
            captures.Should().OnlyContain(positionId => positionId == "A");
        }

        [Test]
        public async Task RejectsMismatchedMetadataBeforeCapturingTrack() {
            var solveCount = 0;

            Func<Task> act = () => TppaDriftValidationOrchestrator.Run(
                LatitudeDegrees,
                (_, _) => Task.CompletedTask,
                (_, _) => Task.FromResult(Metadata("B")),
                (_, _) => {
                    solveCount++;
                    return Task.FromResult(Sample("A", 0));
                },
                AcquisitionPolicy(),
                TrackPolicy(),
                ValidationPolicy(),
                CancellationToken.None,
                (_, _) => Task.CompletedTask);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*does not match expected position A*");
            solveCount.Should().Be(0);
        }

        [Test]
        public async Task CancellationStopsBeforeNextPosition() {
            using var cts = new CancellationTokenSource();
            var moves = new List<string>();
            var sampleIndex = 0;

            Func<Task> act = () => TppaDriftValidationOrchestrator.Run(
                LatitudeDegrees,
                (positionId, _) => {
                    moves.Add(positionId);
                    return Task.CompletedTask;
                },
                (positionId, _) => Task.FromResult(Metadata(positionId)),
                (positionId, _) => {
                    var sample = Sample(positionId, sampleIndex++);
                    if (sampleIndex == 4) {
                        cts.Cancel();
                    }
                    return Task.FromResult(sample);
                },
                AcquisitionPolicy(),
                TrackPolicy(),
                ValidationPolicy(),
                cts.Token,
                (_, _) => Task.CompletedTask);

            await act.Should().ThrowAsync<OperationCanceledException>();
            moves.Should().Equal("A");
        }

        [TestCase(double.NaN)]
        [TestCase(91)]
        public async Task RejectsInvalidLatitudeBeforeMovement(double latitudeDegrees) {
            var moveCount = 0;

            Func<Task> act = () => TppaDriftValidationOrchestrator.Run(
                latitudeDegrees,
                (_, _) => {
                    moveCount++;
                    return Task.CompletedTask;
                },
                (positionId, _) => Task.FromResult(Metadata(positionId)),
                (positionId, _) => Task.FromResult(Sample(positionId, 0)),
                AcquisitionPolicy(),
                TrackPolicy(),
                ValidationPolicy(),
                CancellationToken.None);

            await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
            moveCount.Should().Be(0);
        }

        private static TppaDriftTrackMetadata Metadata(string positionId) {
            var hourAngle = positionId switch {
                "A" => -45,
                "B" => -15,
                "C" => 15,
                _ => throw new ArgumentOutOfRangeException(nameof(positionId))
            };
            return new TppaDriftTrackMetadata(
                positionId,
                hourAngle,
                AltitudeDegrees: 40,
                HasComputedRefractionDrift: true,
                ComputedRefractionDriftArcsecondsPerMinute: 0);
        }

        private static TppaDriftSolveSample Sample(string positionId, int sampleIndex) {
            var hourAngle = Metadata(positionId).HourAngleDegrees;
            var drift = TppaDriftValidationEstimator.PredictDeclinationDriftArcsecondsPerMinute(
                8,
                -5,
                LatitudeDegrees,
                hourAngle);
            return new TppaDriftSolveSample(
                Start.AddSeconds(sampleIndex * 10),
                45 + drift * sampleIndex * 10 / 60 / 3600);
        }

        private static TppaDriftTrackAcquisitionPolicy AcquisitionPolicy() => new(
            TargetDuration: TimeSpan.FromMinutes(5),
            CadenceDelay: TimeSpan.Zero,
            MaximumSampleCount: 40);

        private static TppaDeclinationDriftTrackPolicy TrackPolicy() =>
            TppaDeclinationDriftTrackPolicy.FieldDefault with {
                MinimumSampleCount = 20
            };

        private static TppaDriftValidationPolicy ValidationPolicy() =>
            TppaDriftValidationPolicy.FieldDefault;
    }
}
