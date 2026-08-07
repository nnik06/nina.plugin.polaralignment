using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaDirectFullTravelFeasibilityPolicyTest {
        [Test]
        public void Evaluate_ProvesExistingSmallAuthorityCannotCoverFullEnvelopeInTwoMoves() {
            var result = Evaluate(
                azimuthMinutes: 324,
                altitudeMinutes: 0,
                azimuthMinutesPerUnit: 1,
                altitudeMinutesPerUnit: 1,
                maximumXUnits: 20,
                maximumYUnits: 16);

            result.IsFeasible.Should().BeFalse();
            result.RequiredMoveCount.Should().Be(17);
            result.Reason.Should().Contain("17 fresh-feedback moves");
        }

        [Test]
        public void Evaluate_AcceptsCalibratedTwoMoveFullEnvelopeRouteInsideFiveMinutes() {
            var result = Evaluate(
                azimuthMinutes: 324,
                altitudeMinutes: -300,
                azimuthMinutesPerUnit: 2,
                altitudeMinutesPerUnit: 2,
                maximumXUnits: 81,
                maximumYUnits: 75);

            result.IsFeasible.Should().BeTrue();
            result.RequiredMoveCount.Should().Be(2);
            result.RequiredRuntimeSeconds.Should().BeApproximately(230, 0.001);
        }

        [Test]
        public void Evaluate_RejectsRouteThatFitsMoveCountButExceedsRuntime() {
            var result = TppaDirectFullTravelFeasibilityPolicy.Evaluate(
                100, 100, 2, 2, 100, 100,
                initialAgreementSeconds: 200,
                perMoveFreshFeedbackSeconds: 70,
                terminalConfirmationSeconds: 40,
                perMoveOverheadSeconds: 15);

            result.IsFeasible.Should().BeFalse();
            result.RequiredMoveCount.Should().Be(1);
            result.Reason.Should().Contain("exceeding");
        }

        [Test]
        public void Evaluate_RejectsVectorOutsideFullEnvelope() {
            var result = Evaluate(
                azimuthMinutes: 325,
                altitudeMinutes: 0,
                azimuthMinutesPerUnit: 2,
                altitudeMinutesPerUnit: 2,
                maximumXUnits: 200,
                maximumYUnits: 200);

            result.IsFeasible.Should().BeFalse();
            result.Reason.Should().Contain("outside");
        }

        private static TppaDirectFullTravelFeasibilityDecision Evaluate(
            double azimuthMinutes,
            double altitudeMinutes,
            double azimuthMinutesPerUnit,
            double altitudeMinutesPerUnit,
            double maximumXUnits,
            double maximumYUnits) {
            return TppaDirectFullTravelFeasibilityPolicy.Evaluate(
                azimuthMinutes,
                altitudeMinutes,
                azimuthMinutesPerUnit,
                altitudeMinutesPerUnit,
                maximumXUnits,
                maximumYUnits,
                initialAgreementSeconds: 80,
                perMoveFreshFeedbackSeconds: 40,
                terminalConfirmationSeconds: 40,
                perMoveOverheadSeconds: 15);
        }
    }
}
