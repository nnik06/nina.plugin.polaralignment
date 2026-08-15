using FluentAssertions;
using NINA.Plugins.PolarAlignment.Avalon;
using NINA.Plugins.PolarAlignment.OAPA;
using System;
using System.Collections.Generic;
using System.IO;

namespace NINA.Plugins.PolarAlignment.Test {
    public class UniversalPolarAlignmentBaseTest {
        [Test]
        public void GrblJogCancelRealtimeCommand_MatchesProtocol() {
            UniversalPolarAlignmentBase.GrblJogCancelRealtimeCommand.Should().Be(0x85);
        }

        [Test]
        public void CalculateMovementTimeout_ScalesWithDistanceAndFeedRate() {
            var timeout = UniversalPolarAlignmentBase.CalculateMovementTimeout(0f, 220f, 700);

            timeout.TotalSeconds.Should().BeApproximately(64.5714, 0.0001);
        }

        [Test]
        public void CalculateMovementTimeout_KeepsShortMovesAboveMinimum() {
            var timeout = UniversalPolarAlignmentBase.CalculateMovementTimeout(0f, 0.001f, 1000);

            timeout.TotalSeconds.Should().BeApproximately(8, 0.0001);
        }

        [Test]
        public void CalculateMovementTimeout_UsesFallbackForInvalidSpeed() {
            var timeout = UniversalPolarAlignmentBase.CalculateMovementTimeout(0f, 10f, 0);

            timeout.TotalSeconds.Should().BeApproximately(30, 0.0001);
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("ok", false)]
        [TestCase("  ok  ", false)]
        [TestCase("<Idle|MPos:1.000,2.000,0.000|>", true)]
        [TestCase(" <Hold:0|WPos:-1.000,+2.000,0.000|> ", true)]
        public void IsStatusLineCandidate_OnlyAcceptsControllerStatusLines(string line, bool expected) {
            UniversalPolarAlignmentBase.IsStatusLineCandidate(line).Should().Be(expected);
        }

        [Test]
        public void AvalonStatusRegex_ParsesMPosWPosAndSubStateStatuses() {
            UniversalPolarAlignmentBase.TryParseStatus(UniversalPolarAlignment.StatusRegex(),
                                                       "<Hold:0|WPos:-1.25,+2.5,0|>",
                                                       out var status,
                                                       out var x,
                                                       out var y,
                                                       out var z).Should().BeTrue();

            status.Should().Be("Hold:0");
            x.Should().BeApproximately(-1.25f, 0.0001f);
            y.Should().BeApproximately(2.5f, 0.0001f);
            z.Should().BeApproximately(0f, 0.0001f);
        }

        [Test]
        public void OapaStatusRegex_ParsesMPosWPosAndOptionalTelemetry() {
            UniversalPolarAlignmentBase.TryParseStatus(UniversalPolarAlignmentOAPA.StatusRegex(),
                                                       "<Run|MPos:1.0,2.0,3.0|T:4,R:1,E:0,S:700.5|>",
                                                       out var status,
                                                       out var x,
                                                       out var y,
                                                       out var z).Should().BeTrue();

            status.Should().Be("Run");
            x.Should().BeApproximately(1f, 0.0001f);
            y.Should().BeApproximately(2f, 0.0001f);
            z.Should().BeApproximately(3f, 0.0001f);

            UniversalPolarAlignmentBase.TryParseStatus(UniversalPolarAlignmentOAPA.StatusRegex(),
                                                       "<Door:1|WPos:.5,-.25,0|>",
                                                       out status,
                                                       out x,
                                                       out y,
                                                       out z).Should().BeTrue();
            status.Should().Be("Door:1");
            x.Should().BeApproximately(0.5f, 0.0001f);
            y.Should().BeApproximately(-0.25f, 0.0001f);
        }

        [Test]
        public void TryParseStatus_RejectsNonStatusResponsesWithoutCoordinates() {
            UniversalPolarAlignmentBase.TryParseStatus(UniversalPolarAlignment.StatusRegex(),
                                                       "ok",
                                                       out _,
                                                       out _,
                                                       out _,
                                                       out _).Should().BeFalse();
        }

        [Test]
        public void StatusQueryDrainsStaleInputAndHasOneBoundedRetry() {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PolarAlignment", "UniversalPolarAlignmentBase.cs"));
            var updateStatus = source.IndexOf("private void UpdateStatus()", StringComparison.Ordinal);
            var updateStatusEnd = source.IndexOf("internal static bool TryParseStatus", updateStatus, StringComparison.Ordinal);
            var body = source.Substring(updateStatus, updateStatusEnd - updateStatus);

            body.Should().Contain("port.DiscardInBuffer();");
            body.IndexOf("port.DiscardInBuffer();", StringComparison.Ordinal)
                .Should().BeLessThan(body.IndexOf("port.WriteLine(\"?\");", StringComparison.Ordinal));
            body.Should().Contain("queryAttempt < MaxStatusQueryAttempts");
            source.Should().Contain("private const int MaxStatusQueryAttempts = 2;");
        }
        [TestCase("Idle", true)]
        [TestCase("Hold:0", true)]
        [TestCase("Hold:1", false)]
        [TestCase("Door:0", true)]
        [TestCase("Door:1", true)]
        [TestCase("Door:2", false)]
        [TestCase("Door:3", false)]
        [TestCase("Alarm", true)]
        [TestCase("Jog", false)]
        [TestCase("Run", false)]
        public void IsControllerStoppedStatus_RecognizesNonMovingStates(string status, bool expected) {
            UniversalPolarAlignmentBase.IsControllerStoppedStatus(status).Should().Be(expected);
        }

        [TestCase("Idle", true)]
        [TestCase(" idle ", true)]
        [TestCase("Hold:0", false)]
        [TestCase("Hold:1", false)]
        [TestCase("Door:0", false)]
        [TestCase("Door:1", false)]
        [TestCase("Alarm", false)]
        [TestCase("Jog", false)]
        public void IsJogCancellationTerminalStatus_RequiresIdle(string status, bool expected) {
            UniversalPolarAlignmentBase.IsJogCancellationTerminalStatus(status).Should().Be(expected);
        }

        [Test]
        public void AreControllerPositionsStable_RequiresEveryAxisWithinTolerance() {
            UniversalPolarAlignmentBase.AreControllerPositionsStable(
                (1f, 2f, 3f),
                (1.009f, 1.991f, 3.01f)).Should().BeTrue();

            UniversalPolarAlignmentBase.AreControllerPositionsStable(
                (1f, 2f, 3f),
                (1.011f, 2f, 3f)).Should().BeFalse();
        }

        [Test]
        public void JogCancellationConfirmationRequiresTwoConsecutiveStableIdleSamples() {
            var tracker = new JogCancellationConfirmationTracker(2);

            tracker.Observe("Hold:1", (1f, 2f, 3f)).Should().BeFalse();
            tracker.Confirmations.Should().Be(0);
            tracker.Observe("Idle", (1f, 2f, 3f)).Should().BeFalse();
            tracker.Confirmations.Should().Be(1);
            tracker.Observe("Idle", (1.005f, 2f, 3f)).Should().BeTrue();
            tracker.Confirmations.Should().Be(2);
        }

        [Test]
        public void JogCancellationConfirmationRestartsWhenIdlePositionMoves() {
            var tracker = new JogCancellationConfirmationTracker(2);

            tracker.Observe("Idle", (1f, 2f, 3f)).Should().BeFalse();
            tracker.Observe("Idle", (1.02f, 2f, 3f)).Should().BeFalse();
            tracker.Confirmations.Should().Be(1);
            tracker.Observe("Idle", (1.02f, 2f, 3f)).Should().BeTrue();
        }

        [Test]
        public void JogCancellationConfirmationRestartsAfterNonIdleState() {
            var tracker = new JogCancellationConfirmationTracker(2);

            tracker.Observe("Idle", (1f, 2f, 3f)).Should().BeFalse();
            tracker.Observe("Door:2", (1f, 2f, 3f)).Should().BeFalse();
            tracker.Confirmations.Should().Be(0);
            tracker.Observe("Idle", (1f, 2f, 3f)).Should().BeFalse();
            tracker.Observe("Idle", (1f, 2f, 3f)).Should().BeTrue();
        }

        [Test]
        public void JogCancellationConfirmationRejectsInvalidThreshold() {
            var action = () => new JogCancellationConfirmationTracker(0);

            action.Should().Throw<System.ArgumentOutOfRangeException>();
        }

        [Test]
        public void JogCancellationOrchestratorSendsCancelAndWaitsForStableIdle() {
            var sentCommands = new List<byte>();
            var waits = new List<TimeSpan>();
            var samples = new Queue<JogCancellationStatusSample>(new[] {
                new JogCancellationStatusSample("Run", 1f, 2f, 3f),
                new JogCancellationStatusSample("Hold:1", 1.5f, 2f, 3f),
                new JogCancellationStatusSample("Idle", 1.75f, 2f, 3f),
                new JogCancellationStatusSample("Idle", 1.755f, 2f, 3f)
            });

            var result = JogCancellationOrchestrator.Execute(
                sentCommands.Add,
                samples.Dequeue,
                waits.Add,
                2,
                10,
                TimeSpan.FromMilliseconds(300));

            sentCommands.Should().Equal(0x85);
            waits.Should().HaveCount(4)
                .And.OnlyContain(value => value == TimeSpan.FromMilliseconds(300));
            result.Attempts.Should().Be(4);
            result.Confirmations.Should().Be(2);
            result.FinalSample.Status.Should().Be("Idle");
            result.FinalSample.X.Should().BeApproximately(1.755f, 0.0001f);
        }

        [Test]
        public void JogCancellationOrchestratorFailsClosedWithoutStableIdle() {
            var sentCommands = new List<byte>();
            var observations = 0;

            var action = () => JogCancellationOrchestrator.Execute(
                sentCommands.Add,
                () => {
                    observations++;
                    return new JogCancellationStatusSample(
                        observations == 2 ? "Door:2" : "Idle",
                        observations,
                        0f,
                        0f);
                },
                _ => { },
                2,
                3,
                TimeSpan.Zero);

            action.Should().Throw<TimeoutException>()
                .WithMessage("*2 stable Idle confirmations were not observed*");
            sentCommands.Should().Equal(0x85);
            observations.Should().Be(3);
        }

        [Test]
        public void JogCancellationOrchestratorRejectsMissingObservation() {
            var sentCommands = new List<byte>();

            var action = () => JogCancellationOrchestrator.Execute(
                sentCommands.Add,
                () => null,
                _ => { },
                2,
                3,
                TimeSpan.Zero);

            action.Should().Throw<InvalidOperationException>()
                .WithMessage("*status observation was missing*");
            sentCommands.Should().Equal(0x85);
        }

        [Test]
        public void JogCancellationOrchestratorRejectsImpossibleAttemptBudget() {
            var action = () => JogCancellationOrchestrator.Execute(
                _ => { },
                () => new JogCancellationStatusSample("Idle", 0f, 0f, 0f),
                _ => { },
                2,
                1,
                TimeSpan.Zero);

            action.Should().Throw<ArgumentOutOfRangeException>()
                .Which.ParamName.Should().Be("maximumAttempts");
        }

        private static string RepositoryRoot() {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "PolarAlignment"))) {
                directory = directory.Parent;
            }
            return directory?.FullName
                ?? throw new DirectoryNotFoundException("Could not locate the TPPA repository root from the test output directory.");
        }
    }
}
