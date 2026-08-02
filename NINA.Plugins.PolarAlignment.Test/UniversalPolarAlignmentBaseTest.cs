using FluentAssertions;
using NINA.Plugins.PolarAlignment.Avalon;
using NINA.Plugins.PolarAlignment.OAPA;
using System;

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
        [TestCase("Idle", true)]
        [TestCase("Hold:0", true)]
        [TestCase("Door:1", true)]
        [TestCase("Alarm", true)]
        [TestCase("Jog", false)]
        [TestCase("Run", false)]
        public void IsControllerStoppedStatus_RecognizesNonMovingStates(string status, bool expected) {
            UniversalPolarAlignmentBase.IsControllerStoppedStatus(status).Should().Be(expected);
        }
    }
}
