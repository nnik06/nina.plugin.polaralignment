using System;
using System.IO;
using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaFastPostMoveOrchestrationContractTest {
        [Test]
        public void ConvergedPostMoveResponseReturnsDirectlyToStationaryConfirmationBeforeAnyFurtherMove() {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PolarAlignment", "Instructions", "PolarAlignment.cs"));
            var loopStart = source.IndexOf("do {", StringComparison.Ordinal);
            var confirmationGate = source.IndexOf("independent fresh completion confirmation", loopStart, StringComparison.Ordinal);
            var move = source.IndexOf("() => TPAPAVM.MoveCloser", confirmationGate, StringComparison.Ordinal);
            var convergenceBranch = source.IndexOf("if (responseDisposition.ContinueToStationaryConfirmation)", move, StringComparison.Ordinal);
            var directContinue = source.IndexOf("continue;", convergenceBranch, StringComparison.Ordinal);
            var loopEnd = source.IndexOf("} while (!localCTS.Token.IsCancellationRequested);", directContinue, StringComparison.Ordinal);
            var laterMove = source.IndexOf("() => TPAPAVM.MoveCloser", directContinue, StringComparison.Ordinal);

            loopStart.Should().BeGreaterThanOrEqualTo(0);
            confirmationGate.Should().BeGreaterThan(loopStart);
            move.Should().BeGreaterThan(confirmationGate);
            convergenceBranch.Should().BeGreaterThan(move);
            directContinue.Should().BeGreaterThan(convergenceBranch);
            loopEnd.Should().BeGreaterThan(directContinue);
            (laterMove < 0 || laterMove > loopEnd).Should().BeTrue(
                "a converged fast-mode response must re-enter at the stationary confirmation gate before any further motion path");
        }

        [Test]
        public void FastModeBudgetsEveryMoveForFreshResponseAndTerminalVerification() {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PolarAlignment", "Instructions", "PolarAlignment.cs"));

            source.Should().Contain("maximumObservedFreshDeterminationSeconds,");
            source.Should().Contain("freshFeedbackMoveCount,");
            source.Should().Contain("qualifiedFreshDeterminationReserveSeconds);");
            source.Should().Contain("terminal verify-only determination");
            source.Should().Contain("responseDecision.CouldAuthorizeAnotherMove");
            source.Should().Contain("freshFeedbackMoveCount >= freshFeedbackMoveLimit");
            source.Should().Contain("TPAPAVM.LastAutomatedAdjustmentWasBoundedYBootstrapProbe");
            source.Should().Contain("MaximumFreshFeedbackMovesAfterBoundedYBootstrapProbe");
            source.Should().Contain("TPAPAVM.HasQualifiedSessionLocalYBootstrapResponse");
            source.Should().Contain("TPAPAVM.AbortBoundedYBootstrapProbeAfterRegression");
            source.Should().Contain("The run stopped on fresh evidence without authorizing another move.");
            source.Should().Contain("maximumObservedFreshDeterminationSeconds = Math.Max(");
            source.Should().Contain("freshDeterminationStopwatch.Elapsed.TotalSeconds");
            source.Should().Contain("directPostMoveFeedbackEligibleForReuse");
            source.Should().Contain("DirectFeedbackReuseMaximumAgeSeconds");
            source.Should().Contain("Reusing the accepted, settled post-move fresh determination");
            source.Should().Contain("EvaluateBeforeFreshPair(");
            source.Should().Contain("InvalidateDirectPostMoveFeedback(\"a new direct UPAS move is about to execute\")");
            source.Should().Contain("InvalidateDirectPostMoveFeedback(\"the sequence was paused\")");
            source.Should().Contain("directUpasMotionEpoch");
            source.Should().Contain("directPostMoveFeedbackMotionEpoch == directUpasMotionEpoch");
            source.Should().Contain("directPostMoveFeedbackMotionEpoch = directUpasMotionEpoch");
            source.Should().Contain("directPostMoveFeedbackMotionEpoch = -1");
            source.Should().Contain("too old or belongs to a previous motion epoch");
        }

        [Test]
        public void FastModeTreatsLifecycleTelemetryAsNonBlockingDiagnosticEvidence() {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PolarAlignment", "Instructions", "PolarAlignment.cs"));

            source.Should().Contain("bool TryLogFastRunEvent(");
            source.Should().Contain("TryLogFastRunEvent(\"started\"");
            source.Should().Contain("TryLogFastRunEvent(\"initial-fresh-determination\"");
            source.Should().Contain("TryLogFastRunEvent(\"admission-rejected\"");
            source.Should().Contain("TppaFastAlignmentExecutionBudget.EvaluateInitialTotal(");
            source.Should().Contain("TppaFastActuatorAdmissionGate.ExecuteIfAuthorizedAsync");
            source.Should().Contain("TppaPostMoveResponsePolicy.Evaluate(");
            source.Should().Contain("EnsureFastRuntimeBudget(");
            source.Should().Contain("TryLogFastRunEvent(\"post-move-response\"");
            source.Should().Contain("TryLogFastRunEvent(\"completed\"");
            source.Should().NotContain("if (!TryLogFastRunEvent(\"started\"");
            source.Should().NotContain("if (!TryLogFastRunEvent(\"initial-fresh-determination\"");
            source.Should().NotContain("if (!TryLogFastRunEvent(\"post-move-response\"");
            source.Should().NotContain("if (!TryLogFastRunEvent(\"completed\"");
            source.Should().NotContain("[\"reason\"] = ex.Message");
            source.Should().Contain("if (!enforceFastRuntimeBudget || (terminal && fastTerminalEventLogged))");
            source.Should().Contain("fastTerminalEventLogged = true;");
            source.Should().NotContain("if (!TryLogFastRunEvent(\"cancelled\"");
            source.Should().NotContain("if (!TryLogFastRunEvent(\"failed\"");
            source.Should().NotContain("if (!TryLogFastRunEvent(\"abandoned\"");
        }

        [Test]
        public void DirectFieldModeKeepsSupervisorProvenanceAuthoritiesOutsideItsMotionPath() {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PolarAlignment", "Instructions", "PolarAlignment.cs"));
            var supervisorMode = source.IndexOf(
                "var supervisorCampaignMode = enforceFastRuntimeBudget", StringComparison.Ordinal);
            var supervisorBranch = source.IndexOf("if (supervisorCampaignMode) {", supervisorMode, StringComparison.Ordinal);
            var physicalZero = source.IndexOf("RequireFreshPhysicalZeroAdmission", supervisorBranch, StringComparison.Ordinal);
            var branchEnd = source.IndexOf("// The five-minute contract starts only after physical-zero admission.", supervisorBranch, StringComparison.Ordinal);

            supervisorMode.Should().BeGreaterThanOrEqualTo(0);
            supervisorBranch.Should().BeGreaterThan(supervisorMode);
            physicalZero.Should().BeGreaterThan(supervisorBranch);
            branchEnd.Should().BeGreaterThan(physicalZero);
            source.Should().Contain("activeTppaCovarianceAuthority?.RepositoryHead ?? \"direct-field\"");
            source.Should().Contain("activeTppaCadenceAuthority?.AuthorityId.ToString(\"D\") ?? \"direct-field\"");
        }

        [Test]
        public void InitialTotalErrorIsDefinedAsEuclideanHypotenuseOfComponents() {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PolarAlignment", "TPAPAVM.cs"));

            source.Should().Contain(
                "Angle.ByDegree(Accord.Math.Tools.Hypotenuse(altitudeError.Degree, azimuthError.Degree))");
        }

        [Test]
        public void PartialDiagonalExecutionStopsTheRunInsteadOfClaimingACompletedCorrection() {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PolarAlignment", "TPAPAVM.cs"));
            var yDenied = source.IndexOf("Y command was denied", StringComparison.Ordinal);
            var partialAbort = source.IndexOf("TPPA_UPAS_PARTIAL_DIAGONAL_ABORT", yDenied, StringComparison.Ordinal);
            var failedReturn = source.IndexOf("return false;", partialAbort, StringComparison.Ordinal);
            var nextSuccessfulReturn = source.IndexOf("return true;", partialAbort, StringComparison.Ordinal);

            yDenied.Should().BeGreaterThanOrEqualTo(0);
            partialAbort.Should().BeGreaterThan(yDenied);
            failedReturn.Should().BeGreaterThan(partialAbort);
            (nextSuccessfulReturn < 0 || nextSuccessfulReturn > failedReturn).Should().BeTrue(
                "a verified X move followed by a denied Y move is an incomplete diagonal, not a successful correction");
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
