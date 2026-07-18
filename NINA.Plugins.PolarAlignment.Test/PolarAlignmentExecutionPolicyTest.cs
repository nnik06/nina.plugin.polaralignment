using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugins.PolarAlignment.Test {
    public class PolarAlignmentExecutionPolicyTest {
        [Test]
        public void VerificationOnlyRunsOneFreshVerificationAndBypassesAllActuatorPhases() {
            var policy = PolarAlignmentExecutionPolicy.Create(verificationOnly: true);

            policy.RunSingleFreshVerification.Should().BeTrue();
            policy.AllowActuatorConfiguration.Should().BeFalse();
            policy.AllowActuatorPreparation.Should().BeFalse();
            policy.AllowActuatorConnection.Should().BeFalse();
            policy.AllowActuatorMovement.Should().BeFalse();
            policy.DisconnectActuatorOnDispose.Should().BeFalse();
        }

        [Test]
        public void NormalRunPreservesAllActuatorPhasesWithoutForcedVerification() {
            var policy = PolarAlignmentExecutionPolicy.Create(verificationOnly: false);

            policy.RunSingleFreshVerification.Should().BeFalse();
            policy.AllowActuatorConfiguration.Should().BeTrue();
            policy.AllowActuatorPreparation.Should().BeTrue();
            policy.AllowActuatorConnection.Should().BeTrue();
            policy.AllowActuatorMovement.Should().BeTrue();
            policy.DisconnectActuatorOnDispose.Should().BeTrue();
        }

        [Test]
        public void VerificationOnlyRejectsManualModeWithClearValidationIssue() {
            var issues = PolarAlignmentExecutionPolicy.GetValidationIssues(verificationOnly: true, manualMode: true);

            issues.Should().ContainSingle()
                .Which.Should().Be(PolarAlignmentExecutionPolicy.VerificationOnlyManualModeIssue);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        public void OtherModeCombinationsDoNotAddVerificationOnlyValidationIssue(bool verificationOnly, bool manualMode) {
            PolarAlignmentExecutionPolicy.GetValidationIssues(verificationOnly, manualMode).Should().BeEmpty();
        }

        [Test]
        public async Task ArcRunnerUsesCapturedAForBothDeterminationsAndExactlySixSolves() {
            const string a = "A";
            var solveCount = 0;
            var returnedPointings = new List<string>();

            var result = await VerificationOnlyArcRunner.Run<string, string>(
                a,
                _ => {
                    solveCount += 3;
                    return Task.FromResult("initial");
                },
                _ => {
                    solveCount += 3;
                    return Task.FromResult("verification");
                },
                (pointing, _) => {
                    returnedPointings.Add(pointing);
                    return Task.CompletedTask;
                },
                CancellationToken.None);

            result.Initial.Should().Be("initial");
            result.Verification.Should().Be("verification");
            result.Plan.DeterminationStarts.Should().Equal(a, a);
            result.Plan.RestorePointing.Should().Be(a);
            result.Plan.TotalSolveCount.Should().Be(6);
            result.Plan.RequiresCorrectionFieldSolve.Should().BeFalse();
            solveCount.Should().Be(6);
            returnedPointings.Should().Equal(a);
        }

        [Test]
        public void RestoreTargetFallsBackToPreRunPointingUntilAIsCaptured() {
            var target = new VerificationOnlyRestoreTarget<string>("pre-run");

            target.Pointing.Should().Be("pre-run");
            target.HasCapturedArcStart.Should().BeFalse();

            target.CaptureArcStart("A");

            target.Pointing.Should().Be("A");
            target.HasCapturedArcStart.Should().BeTrue();
        }

        [Test]
        public void SecondVerificationArcCanReactivateMeasurementStepsCleanly() {
            var context = PolarAlignmentSolveCancellationTest.CreatePolarAlignment(null!, null!).TPAPAVM;
            context.ActivateFirstVerificationStep();
            context.ActivateSecondStep();
            context.ActivateThirdStep();

            context.ActivateFirstVerificationStep();
            context.ActivateSecondStep();

            context.Steps[0].Active.Should().BeFalse();
            context.Steps[0].Completed.Should().BeTrue();
            context.Steps[1].Active.Should().BeTrue();
            context.Steps[1].Completed.Should().BeFalse();
            context.Steps[2].Active.Should().BeFalse();
            context.Steps[2].Completed.Should().BeFalse();
        }

        [Test]
        public async Task CleanupRunsWithIndependentTokenWhenOperationIsCancelled() {
            using var callerCTS = new CancellationTokenSource();
            callerCTS.Cancel();
            var cancellation = new OperationCanceledException(callerCTS.Token);
            var cleanupCalled = false;
            var cleanupTokenWasCancelled = true;

            Func<Task> act = () => VerificationOnlyCleanupRunner.Run(
                _ => Task.FromException(cancellation),
                cleanupToken => {
                    cleanupCalled = true;
                    cleanupTokenWasCancelled = cleanupToken.IsCancellationRequested;
                    return Task.CompletedTask;
                },
                callerCTS.Token,
                TimeSpan.FromSeconds(1));

            var thrown = await act.Should().ThrowAsync<OperationCanceledException>();
            thrown.Which.Should().BeSameAs(cancellation);
            cleanupCalled.Should().BeTrue();
            cleanupTokenWasCancelled.Should().BeFalse();
        }

        [Test]
        public async Task CleanupFailureDoesNotReplaceOriginalMeasurementFailure() {
            var measurementFailure = new InvalidOperationException("measurement failed");
            var cleanupFailure = new InvalidOperationException("cleanup failed");
            Exception? loggedCleanupFailure = null;

            Func<Task> act = () => VerificationOnlyCleanupRunner.Run(
                _ => Task.FromException(measurementFailure),
                _ => Task.FromException(cleanupFailure),
                CancellationToken.None,
                TimeSpan.FromSeconds(1),
                ex => loggedCleanupFailure = ex);

            var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
            thrown.Which.Should().BeSameAs(measurementFailure);
            loggedCleanupFailure.Should().BeSameAs(cleanupFailure);
        }
    }
}