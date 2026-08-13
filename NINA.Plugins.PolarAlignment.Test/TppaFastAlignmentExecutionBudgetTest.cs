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
        public void FastContractCapsFreshFeedbackMovesAtThree() {
            TppaFastAlignmentExecutionBudget.MaximumFreshFeedbackMoves.Should().Be(3);
        }

        [TestCase(0.0)]
        [TestCase(24.0)]
        [TestCase(120.0)]
        [TestCase(299.999)]
        [TestCase(300.0)]
        public void AcceptsInitialTotalsInsideQualifiedWindow(double totalMinutes) {
            var result = TppaFastAlignmentExecutionBudget.EvaluateInitialTotal(totalMinutes);

            result.CanStart.Should().BeTrue(result.Reason);
        }

        [TestCase(458.206)]
        [TestCase(600.0)]
        public void RejectsInitialTotalsOutsideQualifiedWindow(double totalMinutes) {
            var result = TppaFastAlignmentExecutionBudget.EvaluateInitialTotal(totalMinutes);

            result.CanStart.Should().BeFalse();
            result.Reason.Should().Contain("outside the qualified");
        }

        [Test]
        public void SlowInitialDeterminationCanStillFundOneCompleteBoundedMove() {
            var result = TppaFastAlignmentExecutionBudget.EvaluateBeforeMove(
                TimeSpan.FromSeconds(60),
                observedFreshDeterminationSeconds: 60,
                completedMoves: 0);

            result.CanStart.Should().BeTrue(result.Reason);
            result.RequiredReserveSeconds.Should().Be(165);
        }

        [Test]
        public void SlowCadenceCanFundOneCompleteBoundedMoveWhenItsOwnTailFits() {
            var result = TppaFastAlignmentExecutionBudget.EvaluateBeforeMove(
                TimeSpan.FromSeconds(90),
                observedFreshDeterminationSeconds: 85,
                completedMoves: 0);

            result.CanStart.Should().BeTrue(result.Reason);
            result.RemainingSeconds.Should().Be(210);
            result.RequiredReserveSeconds.Should().Be(195);
        }

        [Test]
        public void NextMoveNeedsItsOwnResponseAndTerminalConfirmation() {
            var result = TppaFastAlignmentExecutionBudget.EvaluateBeforeMove(
                TimeSpan.FromSeconds(95),
                observedFreshDeterminationSeconds: 35,
                completedMoves: 0,
                qualifiedFreshDeterminationReserveSeconds:
                    TppaFastAlignmentExecutionBudget.DirectFieldFreshDeterminationReserveSeconds);

            result.CanStart.Should().BeTrue(result.Reason);
            result.RemainingSeconds.Should().Be(205);
            result.RequiredReserveSeconds.Should().Be(95);
        }

        [Test]
        public void AdmitsNextMoveWhenItsOwnDynamicCadenceFits() {
            var result = TppaFastAlignmentExecutionBudget.EvaluateBeforeMove(
                TimeSpan.FromSeconds(94.997),
                observedFreshDeterminationSeconds: 35.001,
                completedMoves: 0,
                qualifiedFreshDeterminationReserveSeconds:
                    TppaFastAlignmentExecutionBudget.DirectFieldFreshDeterminationReserveSeconds);

            result.CanStart.Should().BeTrue(result.Reason);
            result.RemainingSeconds.Should().BeApproximately(205.003, 0.0001);
            result.RequiredReserveSeconds.Should().BeApproximately(95.002, 0.0001);
        }

        [Test]
        public void SecondMoveUsesMeasuredFreshCadenceInsteadOfTotalElapsedTime() {
            var result = TppaFastAlignmentExecutionBudget.EvaluateBeforeMove(
                TimeSpan.FromSeconds(130),
                observedFreshDeterminationSeconds: 70,
                completedMoves: 1,
                maximumFreshFeedbackMoves: 2);

            result.CanStart.Should().BeTrue(result.Reason);
            result.RemainingSeconds.Should().Be(170);
            result.RequiredReserveSeconds.Should().Be(165);
        }

        [Test]
        public void SecondMoveIsDeniedWhenFreshCadenceCannotFundTerminalVerification() {
            var result = TppaFastAlignmentExecutionBudget.EvaluateBeforeMove(
                TimeSpan.FromSeconds(140),
                observedFreshDeterminationSeconds: 80,
                completedMoves: 1,
                maximumFreshFeedbackMoves: 2);

            result.CanStart.Should().BeFalse(result.Reason);
            result.RequiredReserveSeconds.Should().Be(185);
        }

        [Test]
        public void DirectFieldPlanCanFundThreeFreshFeedbackMovesWhenPostMoveFeedbackSeedsLaterAgreements() {
            var beforeFirstAgreement = TppaFastAlignmentExecutionBudget.EvaluateBeforeFreshAgreement(
                TimeSpan.FromSeconds(35),
                observedFreshDeterminationSeconds: 35,
                completedMoves: 0,
                qualifiedFreshDeterminationReserveSeconds:
                    TppaFastAlignmentExecutionBudget.DirectFieldFreshDeterminationReserveSeconds,
                maximumFreshFeedbackMoves:
                    TppaFastAlignmentExecutionBudget.MaximumFreshFeedbackMoves);
            var beforeFirstMove = TppaFastAlignmentExecutionBudget.EvaluateBeforeMove(
                TimeSpan.FromSeconds(70),
                observedFreshDeterminationSeconds: 35,
                completedMoves: 0,
                qualifiedFreshDeterminationReserveSeconds:
                    TppaFastAlignmentExecutionBudget.DirectFieldFreshDeterminationReserveSeconds,
                maximumFreshFeedbackMoves:
                    TppaFastAlignmentExecutionBudget.MaximumFreshFeedbackMoves);
            var beforeSecondMove = TppaFastAlignmentExecutionBudget.EvaluateBeforeMove(
                TimeSpan.FromSeconds(120),
                observedFreshDeterminationSeconds: 35,
                completedMoves: 1,
                qualifiedFreshDeterminationReserveSeconds:
                    TppaFastAlignmentExecutionBudget.DirectFieldFreshDeterminationReserveSeconds,
                maximumFreshFeedbackMoves:
                    TppaFastAlignmentExecutionBudget.MaximumFreshFeedbackMoves);
            var beforeThirdMove = TppaFastAlignmentExecutionBudget.EvaluateBeforeMove(
                TimeSpan.FromSeconds(170),
                observedFreshDeterminationSeconds: 35,
                completedMoves: 2,
                qualifiedFreshDeterminationReserveSeconds:
                    TppaFastAlignmentExecutionBudget.DirectFieldFreshDeterminationReserveSeconds,
                maximumFreshFeedbackMoves:
                    TppaFastAlignmentExecutionBudget.MaximumFreshFeedbackMoves);

            beforeFirstAgreement.CanStart.Should().BeTrue(beforeFirstAgreement.Reason);
            beforeFirstMove.CanStart.Should().BeTrue(beforeFirstMove.Reason);
            beforeSecondMove.CanStart.Should().BeTrue(beforeSecondMove.Reason);
            beforeThirdMove.CanStart.Should().BeTrue(beforeThirdMove.Reason);
            beforeFirstAgreement.RequiredReserveSeconds.Should().Be(135);
            beforeFirstMove.RequiredReserveSeconds.Should().Be(95);
            beforeSecondMove.RequiredReserveSeconds.Should().Be(95);
            beforeThirdMove.RequiredReserveSeconds.Should().Be(95);
        }

        [Test]
        public void FirstRunIdentificationAndOneCorrectionFitTheDirectFieldBudget() {
            var xProbe = TppaFastAlignmentExecutionBudget.EvaluateBeforeMove(
                TimeSpan.FromSeconds(40),
                observedFreshDeterminationSeconds: 35,
                completedMoves: 0,
                qualifiedFreshDeterminationReserveSeconds:
                    TppaFastAlignmentExecutionBudget.DirectFieldFreshDeterminationReserveSeconds);
            var yProbe = TppaFastAlignmentExecutionBudget.EvaluateBeforeMove(
                TimeSpan.FromSeconds(95),
                observedFreshDeterminationSeconds: 35,
                completedMoves: 1,
                qualifiedFreshDeterminationReserveSeconds:
                    TppaFastAlignmentExecutionBudget.DirectFieldFreshDeterminationReserveSeconds);
            var correction = TppaFastAlignmentExecutionBudget.EvaluateBeforeMove(
                TimeSpan.FromSeconds(190),
                observedFreshDeterminationSeconds: 35,
                completedMoves: 2,
                qualifiedFreshDeterminationReserveSeconds:
                    TppaFastAlignmentExecutionBudget.DirectFieldFreshDeterminationReserveSeconds);

            xProbe.CanStart.Should().BeTrue(xProbe.Reason);
            yProbe.CanStart.Should().BeTrue(yProbe.Reason);
            correction.CanStart.Should().BeTrue(correction.Reason);
            correction.RequiredReserveSeconds.Should().Be(95);
        }

        [Test]
        public void FirstRunIdentificationReservesOnlyItsMandatoryFreshResponse() {
            var result = TppaFastAlignmentExecutionBudget.EvaluateBeforeFirstRunIdentificationProbe(
                TimeSpan.FromSeconds(98.5),
                observedFreshDeterminationSeconds: 83.0,
                qualifiedFreshDeterminationReserveSeconds:
                    TppaFastAlignmentExecutionBudget.DirectFieldFreshDeterminationReserveSeconds);

            result.CanStart.Should().BeTrue(result.Reason);
            result.RemainingSeconds.Should().BeApproximately(201.5, 0.001);
            result.RequiredReserveSeconds.Should().Be(103.0);
        }

        [Test]
        public void FirstRunIdentificationStillRejectsWhenItsOwnResponseCannotFit() {
            var result = TppaFastAlignmentExecutionBudget.EvaluateBeforeFirstRunIdentificationProbe(
                TimeSpan.FromSeconds(198),
                observedFreshDeterminationSeconds: 83.0,
                qualifiedFreshDeterminationReserveSeconds:
                    TppaFastAlignmentExecutionBudget.DirectFieldFreshDeterminationReserveSeconds);

            result.CanStart.Should().BeFalse(result.Reason);
            result.RequiredReserveSeconds.Should().Be(103.0);
        }

        [Test]
        public void FourthMoveCannotBeEvaluated() {
            var action = () => TppaFastAlignmentExecutionBudget.EvaluateBeforeMove(
                TimeSpan.FromSeconds(150), 70, completedMoves: 3);

            action.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Test]
        public void BoundedYBootstrapAllowanceReservesThreeMovesAndTerminalVerification() {
            var firstMove = TppaFastAlignmentExecutionBudget.EvaluateBeforeMove(
                TimeSpan.FromSeconds(95),
                observedFreshDeterminationSeconds: 35,
                completedMoves: 0,
                qualifiedFreshDeterminationReserveSeconds:
                    TppaFastAlignmentExecutionBudget.DirectFieldFreshDeterminationReserveSeconds,
                maximumFreshFeedbackMoves:
                    TppaFastAlignmentExecutionBudget.MaximumFreshFeedbackMovesAfterBoundedYBootstrapProbe);
            var thirdMove = TppaFastAlignmentExecutionBudget.EvaluateBeforeMove(
                TimeSpan.FromSeconds(205),
                observedFreshDeterminationSeconds: 35,
                completedMoves: 2,
                qualifiedFreshDeterminationReserveSeconds:
                    TppaFastAlignmentExecutionBudget.DirectFieldFreshDeterminationReserveSeconds,
                maximumFreshFeedbackMoves:
                    TppaFastAlignmentExecutionBudget.MaximumFreshFeedbackMovesAfterBoundedYBootstrapProbe);

            firstMove.CanStart.Should().BeTrue(firstMove.Reason);
            firstMove.RequiredReserveSeconds.Should().Be(95);
            thirdMove.CanStart.Should().BeTrue(thirdMove.Reason);
            thirdMove.RequiredReserveSeconds.Should().Be(95);
        }

        [Test]
        public void ExpiredPostMoveFeedbackFallsBackToTwoNewFreshDeterminations() {
            var result = TppaFastAlignmentExecutionBudget.EvaluateBeforeFreshPair(
                TimeSpan.FromSeconds(35),
                observedFreshDeterminationSeconds: 35,
                completedMoves: 0,
                qualifiedFreshDeterminationReserveSeconds:
                    TppaFastAlignmentExecutionBudget.DirectFieldFreshDeterminationReserveSeconds);

            result.RequiredReserveSeconds.Should().Be(175);
            result.CanStart.Should().BeTrue(result.Reason);
        }

        [Test]
        public void RejectsUnsupportedMoveAllowance() {
            var action = () => TppaFastAlignmentExecutionBudget.EvaluateBeforeMove(
                TimeSpan.FromSeconds(60),
                observedFreshDeterminationSeconds: 35,
                completedMoves: 0,
                maximumFreshFeedbackMoves: 4);

            action.Should().Throw<ArgumentOutOfRangeException>();
        }

        [TestCase(0)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void RejectsInvalidObservedCadence(double cadenceSeconds) {
            var action = () => TppaFastAlignmentExecutionBudget.EvaluateBeforeMove(
                TimeSpan.FromSeconds(60), cadenceSeconds, completedMoves: 0);

            action.Should().Throw<ArgumentOutOfRangeException>();
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
            var result = TppaFastAlignmentExecutionBudget.EvaluateConfiguration(30, 5, false);

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
        [TestCase(5.001)]
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
        public void CommissionedCadenceCanQualifyShorterSettleAndReserve() {
            var configuration = TppaFastAlignmentExecutionBudget.EvaluateConfiguration(
                15, 3, false, qualifiedSettleSeconds: 15);
            var movement = TppaFastAlignmentExecutionBudget.EvaluateBeforeMove(
                TimeSpan.FromSeconds(100),
                observedFreshDeterminationSeconds: 35,
                completedMoves: 0,
                qualifiedFreshDeterminationReserveSeconds: 45,
                maximumFreshFeedbackMoves: 2);

            configuration.IsEligible.Should().BeTrue(configuration.Reason);
            movement.CanStart.Should().BeTrue(movement.Reason);
            movement.RequiredReserveSeconds.Should().Be(105);
        }

        [TestCase(4.999)]
        [TestCase(30.001)]
        [TestCase(double.NaN)]
        public void RejectsInvalidCommissionedSettle(double qualifiedSettleSeconds) {
            var result = TppaFastAlignmentExecutionBudget.EvaluateConfiguration(
                15, 3, false, qualifiedSettleSeconds);

            result.IsEligible.Should().BeFalse();
            result.Reason.Should().Contain("authority");
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
