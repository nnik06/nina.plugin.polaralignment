using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaFastAlignmentExecutionBudgetTest {
        [Test]
        public void AllowsCleanFreshDeterminationWhenReserveFitsExactly() {
            var result = TppaFastAlignmentExecutionBudget.Evaluate(
                TimeSpan.FromSeconds(225),
                TppaFastAlignmentExecutionBudget.FreshDeterminationReserveSeconds);

            result.CanStart.Should().BeTrue(result.Reason);
            result.RemainingSeconds.Should().Be(75);
        }

        [Test]
        public void InitialAdmissionRetainsObservedRetryTail() {
            TppaFastAlignmentExecutionBudget.FreshDeterminationRetryReserveSeconds
                .Should().Be(125);
        }

        [Test]
        public void MinimumCompleteMoveTailIncludesFeedbackAndStationaryConfirmation() {
            TppaFastAlignmentExecutionBudget.MinimumCompleteMoveAndConfirmationReserveSeconds
                .Should().Be(
                    TppaFastAlignmentExecutionBudget.UpasMoveReserveSeconds
                    + 2 * TppaFastAlignmentExecutionBudget.FreshDeterminationReserveSeconds);
        }

        [Test]
        public void FastContractExplicitlyPermitsOnlyOneFreshFeedbackMove() {
            TppaFastAlignmentExecutionBudget.MaximumFreshFeedbackMoves.Should().Be(1);
        }

        [Test]
        public void CleanInitialDeterminationUsesMinimumMoveTail() {
            var result = TppaFastAlignmentExecutionBudget.EvaluateBeforeMove(
                TimeSpan.FromSeconds(60));

            result.CanStart.Should().BeTrue(result.Reason);
            result.RequiredReserveSeconds.Should().Be(165);
        }

        [Test]
        public void ObservedNinetySecondCadencePermitsOneMoveAndBothFreshChecks() {
            var result = TppaFastAlignmentExecutionBudget.EvaluateBeforeMove(
                TimeSpan.FromSeconds(90));

            result.CanStart.Should().BeTrue(result.Reason);
            result.RemainingSeconds.Should().Be(210);
            result.RequiredReserveSeconds.Should().Be(195);
        }

        [Test]
        public void ObservedCadencePermitsMoveAtExactDynamicBoundary() {
            var result = TppaFastAlignmentExecutionBudget.EvaluateBeforeMove(
                TimeSpan.FromSeconds(95));

            result.CanStart.Should().BeTrue(result.Reason);
            result.RemainingSeconds.Should().Be(205);
            result.RequiredReserveSeconds.Should().Be(205);
        }

        [Test]
        public void RejectsMoveBeyondDynamicCadenceBoundary() {
            var result = TppaFastAlignmentExecutionBudget.EvaluateBeforeMove(
                TimeSpan.FromSeconds(95.001));

            result.CanStart.Should().BeFalse();
            result.RemainingSeconds.Should().BeApproximately(204.999, 0.0001);
            result.RequiredReserveSeconds.Should().BeApproximately(205.002, 0.0001);
        }

        [Test]
        public void PostMoveFeedbackCanStartStationaryConfirmationOnCleanPath() {
            var result = TppaFastAlignmentExecutionBudget.Evaluate(
                TimeSpan.FromSeconds(210),
                TppaFastAlignmentExecutionBudget.FreshDeterminationReserveSeconds);

            result.CanStart.Should().BeTrue(result.Reason);
        }

        [Test]
        public void RejectsConfirmationAfterCleanReserveIsConsumed() {
            var result = TppaFastAlignmentExecutionBudget.Evaluate(
                TimeSpan.FromSeconds(225.001),
                TppaFastAlignmentExecutionBudget.FreshDeterminationReserveSeconds);

            result.CanStart.Should().BeFalse();
        }

        [Test]
        public void AcceptsQualifiedFiveMinuteConfiguration() {
            var result = TppaFastAlignmentExecutionBudget.EvaluateConfiguration(30, 3, false);

            result.IsEligible.Should().BeTrue(result.Reason);
        }

        [TestCase(29.9)]
        [TestCase(30.1)]
        [TestCase(120)]
        [TestCase(double.NaN)]
        public void RejectsUnqualifiedSettleForFiveMinuteMode(double settleSeconds) {
            var result = TppaFastAlignmentExecutionBudget.EvaluateConfiguration(
                settleSeconds, 3, false);

            result.IsEligible.Should().BeFalse();
            result.Reason.Should().Contain("settle");
        }

        [TestCase(0)]
        [TestCase(3.001)]
        [TestCase(double.PositiveInfinity)]
        public void RejectsUnqualifiedExposureForFiveMinuteMode(double exposureSeconds) {
            var result = TppaFastAlignmentExecutionBudget.EvaluateConfiguration(
                30, exposureSeconds, false);

            result.IsEligible.Should().BeFalse();
            result.Reason.Should().Contain("exposure");
        }

        [Test]
        public void RejectsAutoPauseForFiveMinuteMode() {
            var result = TppaFastAlignmentExecutionBudget.EvaluateConfiguration(30, 3, true);

            result.IsEligible.Should().BeFalse();
            result.Reason.Should().Contain("Auto pause");
        }

        [Test]
        public void RejectsExpiredBudgetEvenWithoutReserve() {
            var result = TppaFastAlignmentExecutionBudget.Evaluate(
                TimeSpan.FromSeconds(300.001),
                0);

            result.CanStart.Should().BeFalse();
        }

        [TestCase(-1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void RejectsInvalidReserve(double reserve) {
            var action = () => TppaFastAlignmentExecutionBudget.Evaluate(
                TimeSpan.Zero,
                reserve);

            action.Should().Throw<ArgumentOutOfRangeException>();
        }
    }
}
