using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaDirectFullTravelFeasibilityPolicyTest {
        [Test]
        public void Evaluate_ProvesExistingSmallAuthorityCannotCoverFullEnvelopeInTwoMoves() {
            var result = Evaluate(
                azimuthMinutes: 324,
                altitudeMinutes: 0,
                azimuthDegreesPerXUnit: 1.0 / 60.0,
                azimuthDegreesPerYUnit: 0,
                altitudeDegreesPerXUnit: 0,
                altitudeDegreesPerYUnit: 1.0 / 60.0,
                maximumXUnits: 20,
                maximumYUnits: 16);

            result.IsFeasible.Should().BeFalse();
            result.RequiredMoveCount.Should().Be(0);
            result.Reason.Should().Contain("cannot produce");
        }

        [Test]
        public void Evaluate_RejectsCalibratedThreeMoveFullEnvelopeRouteWhenDampedRuntimeCannotConverge() {
            var result = Evaluate(
                azimuthMinutes: 324,
                altitudeMinutes: -300,
                azimuthDegreesPerXUnit: 2.0 / 60.0,
                azimuthDegreesPerYUnit: 0,
                altitudeDegreesPerXUnit: 0,
                altitudeDegreesPerYUnit: 2.0 / 60.0,
                maximumXUnits: 81,
                maximumYUnits: 75);

            result.IsFeasible.Should().BeFalse();
            result.RequiredMoveCount.Should().Be(4);
            result.RequiredRuntimeSeconds.Should().BeApproximately(340, 0.001);
            result.Reason.Should().Contain("requires 4 fresh-feedback moves");
        }

        [Test]
        public void Evaluate_AdmitsQualifiedClampLimitedRecoveryWhenItConvergesWithinBudget() {
            var result = TppaDirectFullTravelFeasibilityPolicy.Evaluate(
                324, -300,
                2.0 / 60.0, 0,
                0, 2.0 / 60.0,
                81, 75,
                initialAgreementSeconds: 80,
                perMoveFreshFeedbackSeconds: 40,
                terminalConfirmationSeconds: 40,
                perMoveOverheadSeconds: 15,
                clampLimitedRecoveryEnabled: true,
                relativeResponseUncertainty: 0.05);

            result.IsFeasible.Should().BeTrue();
            result.RequiredMoveCount.Should().Be(2);
            result.RequiredRuntimeSeconds.Should().BeApproximately(230, 0.001);
        }

        [Test]
        public void Evaluate_AdmitsThreeMoveFullEnvelopeBulkAcquisition() {
            var result = TppaDirectFullTravelFeasibilityPolicy.Evaluate(
                300, -300,
                0.05, 0,
                0, 0.05,
                81, 81,
                initialAgreementSeconds: 80,
                perMoveFreshFeedbackSeconds: 40,
                terminalConfirmationSeconds: 40,
                perMoveOverheadSeconds: 15,
                terminalErrorMinutes: TppaOperationalAlignmentTierPolicy.TripodFreeCoarseMaximumTotalMinutes);

            result.IsFeasible.Should().BeTrue();
            result.RequiredMoveCount.Should().Be(3);
            result.RequiredRuntimeSeconds.Should().BeApproximately(285, 0.001);
        }

        [Test]
        public void Evaluate_RejectsRouteThatFitsMoveCountButExceedsRuntime() {
            var result = TppaDirectFullTravelFeasibilityPolicy.Evaluate(
                3, 3, 1, 0, 0, 1, 100, 100,
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
                azimuthDegreesPerXUnit: 2.0 / 60.0,
                azimuthDegreesPerYUnit: 0,
                altitudeDegreesPerXUnit: 0,
                altitudeDegreesPerYUnit: 2.0 / 60.0,
                maximumXUnits: 200,
                maximumYUnits: 200);

            result.IsFeasible.Should().BeFalse();
            result.Reason.Should().Contain("outside");
        }

        private static TppaDirectFullTravelFeasibilityDecision Evaluate(
            double azimuthMinutes,
            double altitudeMinutes,
            double azimuthDegreesPerXUnit,
            double azimuthDegreesPerYUnit,
            double altitudeDegreesPerXUnit,
            double altitudeDegreesPerYUnit,
            double maximumXUnits,
            double maximumYUnits) {
            return TppaDirectFullTravelFeasibilityPolicy.Evaluate(
                azimuthMinutes,
                altitudeMinutes,
                azimuthDegreesPerXUnit,
                azimuthDegreesPerYUnit,
                altitudeDegreesPerXUnit,
                altitudeDegreesPerYUnit,
                maximumXUnits,
                maximumYUnits,
                initialAgreementSeconds: 80,
                perMoveFreshFeedbackSeconds: 40,
                terminalConfirmationSeconds: 40,
                perMoveOverheadSeconds: 15);
        }
    }
}
