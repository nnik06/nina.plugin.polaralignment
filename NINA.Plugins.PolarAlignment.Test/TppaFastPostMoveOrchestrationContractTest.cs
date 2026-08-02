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
            var move = source.IndexOf("await TPAPAVM.MoveCloser", confirmationGate, StringComparison.Ordinal);
            var convergenceBranch = source.IndexOf("if (responseDisposition.ContinueToStationaryConfirmation)", move, StringComparison.Ordinal);
            var directContinue = source.IndexOf("continue;", convergenceBranch, StringComparison.Ordinal);
            var loopEnd = source.IndexOf("} while (!localCTS.Token.IsCancellationRequested);", directContinue, StringComparison.Ordinal);
            var laterMove = source.IndexOf("await TPAPAVM.MoveCloser", directContinue, StringComparison.Ordinal);

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
        public void FastModeRequiresLifecycleEvidenceBeforeQualificationOrMovement() {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PolarAlignment", "Instructions", "PolarAlignment.cs"));

            source.Should().Contain("bool TryLogFastRunEvent(");
            source.Should().Contain("if (!TryLogFastRunEvent(\"started\"");
            source.Should().Contain("if (!TryLogFastRunEvent(\"initial-fresh-determination\"");
            source.Should().Contain("if (!TryLogFastRunEvent(\"post-move-response\"");
            source.Should().Contain("if (!TryLogFastRunEvent(\"completed\"");
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
