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
        public void InitialTotalErrorIsDefinedAsEuclideanHypotenuseOfComponents() {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PolarAlignment", "TPAPAVM.cs"));

            source.Should().Contain(
                "Angle.ByDegree(Accord.Math.Tools.Hypotenuse(altitudeError.Degree, azimuthError.Degree))");
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
