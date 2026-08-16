using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaDirectFullTravelRouteQualificationTest {
        [Test]
        public void Evaluate_AdmitsAValidRouteWhenTheLegacyConfirmationFlagIsFalse() {
            var result = Evaluate(enabled: true, confirmed: false, azimuthMinutes: 17, altitudeMinutes: -17);

            result.IsQualified.Should().BeTrue();
            result.Reason.Should().Contain("bounded-convergence");
        }

        [Test]
        public void Evaluate_RejectsASingularMeasuredResponseMatrix() {
            var result = TppaDirectFullTravelRouteQualification.Evaluate(
                true, true,
                0, -5.4, 5.4,
                0, -5.4, 5.4,
                0.03, 0.06,
                0.02, 0.04,
                108, 108,
                0.05, 0.05,
                120, 120);

            result.IsQualified.Should().BeFalse();
            result.Reason.Should().Contain("singular");
        }

        [Test]
        public void Evaluate_RejectsAPoorlyConditionedMeasuredResponseMatrix() {
            var result = TppaDirectFullTravelRouteQualification.Evaluate(
                true, true,
                0, -5.4, 5.4,
                0, -5.4, 5.4,
                0.5, 0.499,
                0, 0.01,
                81, 81,
                0.5, 0.5,
                24, 0);

            result.IsQualified.Should().BeFalse();
            result.Reason.Should().Contain("poorly conditioned");
        }

        [Test]
        public void Evaluate_RejectsAFeasibleRouteWithoutSignedMarkerHeadroom() {
            var result = TppaDirectFullTravelRouteQualification.Evaluate(
                true, true,
                5.2, -5.4, 5.4,
                0, -5.4, 5.4,
                0.5, 0,
                0, 0.5,
                81, 81,
                0.5, 0.5,
                -24, 0);

            result.IsQualified.Should().BeFalse();
            result.Reason.Should().Contain("headroom");
        }

        [Test]
        public void Evaluate_AllowsAnInwardCorrectionFromThePositiveTravelEdge() {
            var result = TppaDirectFullTravelRouteQualification.Evaluate(
                true, true,
                5.2, -5.4, 5.4,
                0, -5.4, 5.4,
                0.5, 0,
                0, 0.5,
                81, 81,
                0.5, 0.5,
                24, 0);

            result.IsQualified.Should().BeTrue();
        }

        [Test]
        public void Evaluate_UsesTheConfiguredReverseDirectionForSignedHeadroom() {
            var result = TppaDirectFullTravelRouteQualification.Evaluate(
                true, true,
                -5.2, -5.4, 5.4,
                0, -5.4, 5.4,
                0.5, 0,
                0, 0.5,
                81, 81,
                0.5, 0.5,
                24, 0,
                physicalAzimuthCommandDirectionMultiplier: -1);

            result.IsQualified.Should().BeTrue();
        }

        [Test]
        public void Evaluate_UsesPhysicalScaleRatherThanSkyResponseForSignedHeadroom() {
            var result = TppaDirectFullTravelRouteQualification.Evaluate(
                true, true,
                5.2, -5.4, 5.4,
                0, -5.4, 5.4,
                0.5, 0,
                0, 0.5,
                81, 81,
                0.001, 0.5,
                24, 0);

            result.IsQualified.Should().BeTrue();
        }

        [Test]
        public void Evaluate_AdmitsAFullEnvelopeRouteThatConvergesWithinTheFiniteMoveLimit() {
            var result = Evaluate(enabled: true, confirmed: true, azimuthMinutes: 300, altitudeMinutes: -300);

            result.IsQualified.Should().BeTrue(result.Reason);
            result.Feasibility.RequiredMoveCount.Should().BeLessOrEqualTo(
                TppaFastAlignmentExecutionBudget.MaximumFreshFeedbackMoves);
            result.Reason.Should().Contain("bounded-convergence");
        }

        [Test]
        public void Evaluate_AcceptsAConfirmedNearFieldRouteThatTheDampedRuntimeCanConverge() {
            var result = Evaluate(enabled: true, confirmed: true, azimuthMinutes: 17, altitudeMinutes: -17);

            result.IsQualified.Should().BeTrue();
            result.Feasibility.RequiredMoveCount.Should().Be(2);
            result.Reason.Should().Contain("bounded-convergence");
        }

        [Test]
        public void Evaluate_AdmitsAQualifiedClampLimitedFullDiagonal() {
            var result = TppaDirectFullTravelRouteQualification.Evaluate(
                true, true,
                0, -5.4, 5.4,
                0, -5.4, 5.4,
                2.0 / 60.0, 0,
                0, 2.0 / 60.0,
                81, 75,
                0.02, 0.02,
                300, -300,
                clampLimitedRecoveryEnabled: true,
                relativeResponseUncertainty: 0.05);

            result.IsQualified.Should().BeTrue();
            result.Feasibility.RequiredMoveCount.Should().Be(5);
        }

        [Test]
        public void Evaluate_AdmitsMeasuredDubaiMatrixForSubDegreeRoute() {
            var result = TppaDirectFullTravelRouteQualification.Evaluate(
                true, true,
                0, -5.4, 5.4,
                0, -5.4, 5.4,
                0.01317625, 0.00039600,
                0.000177833333333333, 0.01550825,
                80, 80,
                0.025, 0.022,
                60, -60,
                clampLimitedRecoveryEnabled: true,
                relativeResponseUncertainty: 0.05,
                terminalErrorMinutes: 3.0);

            result.IsQualified.Should().BeTrue(result.Reason);
        }

        [Test]
        public void Evaluate_AdmitsDeployedDampedDubaiMatrixButReportsFourMoveCornerRuntime() {
            var result = TppaDirectFullTravelRouteQualification.Evaluate(
                true, true,
                0, -5.4, 5.4,
                0, -5.4, 5.4,
                0.01317625, 0.00039600,
                0.000177833333333333, 0.01550825,
                80, 80,
                0.025, 0.022,
                60, -60,
                clampLimitedRecoveryEnabled: false,
                relativeResponseUncertainty: 1.0,
                terminalErrorMinutes: 3.0);

            result.IsQualified.Should().BeTrue(result.Reason);
            result.Feasibility.RequiredMoveCount.Should().Be(4);
            result.Feasibility.RequiredRuntimeSeconds.Should().BeApproximately(340.0, 1e-9);
        }

        [Test]
        public void Evaluate_DeniesMeasuredDubaiMatrixFullCornerWithoutPhysicalHeadroom() {
            var result = TppaDirectFullTravelRouteQualification.Evaluate(
                true, true,
                0, -5.4, 5.4,
                0, -5.4, 5.4,
                0.01317625, 0.00039600,
                0.000177833333333333, 0.01550825,
                80, 80,
                0.025, 0.022,
                300, -300,
                clampLimitedRecoveryEnabled: true,
                relativeResponseUncertainty: 0.05,
                terminalErrorMinutes: 3.0);

            result.IsQualified.Should().BeFalse();
            result.Reason.Should().Contain("headroom");
        }

        [Test]
        public void Evaluate_CarriesTheExplicitTripodFreeCoarseTier() {
            var result = TppaDirectFullTravelRouteQualification.Evaluate(
                true, true,
                0, -5.4, 5.4,
                0, -5.4, 5.4,
                0.05, 0,
                0, 0.05,
                81, 81,
                0.05, 0.05,
                300, -300,
                terminalErrorMinutes: TppaOperationalAlignmentTierPolicy.TripodFreeCoarseMaximumTotalMinutes);

            result.IsQualified.Should().BeTrue();
            result.Tier.Should().Be(TppaOperationalAlignmentTier.TripodFreeCoarse);
            result.Feasibility.RequiredMoveCount.Should().Be(3);
        }

        private static TppaDirectFullTravelRouteQualification Evaluate(bool enabled, bool confirmed, double azimuthMinutes, double altitudeMinutes) {
            return TppaDirectFullTravelRouteQualification.Evaluate(
                enabled, confirmed,
                0, -5.4, 5.4,
                0, -5.4, 5.4,
                0.05, 0,
                0, 0.05,
                81, 81,
                0.05, 0.05,
                azimuthMinutes, altitudeMinutes);
        }
    }
}
