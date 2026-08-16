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
        public void OperationalModeRequiresFreshResponseAndTerminalVerificationForEveryMove() {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PolarAlignment", "Instructions", "PolarAlignment.cs"));

            source.Should().Contain("maximumObservedFreshDeterminationSeconds,");
            source.Should().Contain("freshFeedbackMoveCount,");
            source.Should().Contain("qualifiedFreshDeterminationReserveSeconds);");
            source.Should().Contain("terminal verification");
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
            source.Should().Contain("Reusing the accepted, settled post-move fresh determination as the current feedback");
            source.Should().Contain("without a redundant pre-move sweep");
            source.Should().Contain("secondDirectDetermination = firstDirectDetermination;");
            source.Should().Contain("TPPA_OPERATIONAL_MOVE_ADMISSION");
            source.Should().Contain("InvalidateDirectPostMoveFeedback(\"a new direct UPAS move is about to execute\")");
            source.Should().Contain("InvalidateDirectPostMoveFeedback(\"the sequence was paused\")");
            source.Should().Contain("directUpasMotionEpoch");
            source.Should().Contain("directPostMoveFeedbackMotionEpoch == directUpasMotionEpoch");
            source.Should().Contain("directPostMoveFeedbackMotionEpoch = directUpasMotionEpoch");
            source.Should().Contain("directPostMoveFeedbackMotionEpoch = -1");
            source.Should().Contain("too old or belongs to a previous motion epoch");
        }

        [Test]
        public void AcceptedCurrentEpochFeedbackSkipsOnlyTheRedundantPreMoveSweep() {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PolarAlignment", "Instructions", "PolarAlignment.cs"));
            var secondDetermination = source.IndexOf("PolarErrorDetermination secondDirectDetermination;", StringComparison.Ordinal);
            var reuseBranch = source.IndexOf("} else if (directFeedbackCanSeedNextMove) {", secondDetermination, StringComparison.Ordinal);
            var nextBranch = source.IndexOf("} else {", reuseBranch + 1, StringComparison.Ordinal);
            var reuseBody = source.Substring(reuseBranch, nextBranch - reuseBranch);

            secondDetermination.Should().BeGreaterThanOrEqualTo(0);
            reuseBranch.Should().BeGreaterThanOrEqualTo(0);
            nextBranch.Should().BeGreaterThan(reuseBranch);
            reuseBody.Should().Contain("secondDirectDetermination = firstDirectDetermination;");
            reuseBody.Should().NotContain("MeasureFreshThreePointForActiveCampaign(");
            source.Should().Contain("independent fresh completion confirmation");
            source.Should().Contain("directPostMoveFeedbackMotionEpoch == directUpasMotionEpoch");
            source.Should().Contain("DirectFeedbackReuseMaximumAgeSeconds");
        }

        [Test]
        public void FreshFeedbackControlSkipsTheIgnoredContinuousCorrectionSolve() {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PolarAlignment", "Instructions", "PolarAlignment.cs"));
            var freshBranch = source.IndexOf("if (freshFeedbackControl) {", StringComparison.Ordinal);
            var legacyBranch = source.IndexOf("} else {", freshBranch, StringComparison.Ordinal);
            var freshBody = source.Substring(freshBranch, legacyBranch - freshBranch);

            freshBranch.Should().BeGreaterThanOrEqualTo(0);
            legacyBranch.Should().BeGreaterThan(freshBranch);
            freshBody.Should().Contain("skipping the legacy continuous correction-frame solve");
            freshBody.Should().NotContain("await Solve(");
            source.Substring(legacyBranch).Should().Contain("var continuousSolve = await Solve(");
            source.Should().Contain("? TPAPAVM.PolarErrorDetermination.InitialMountAxisTotalError");
        }

        [Test]
        public void FirstRunBootstrapRemainsWithinTheThreeMoveFastContract() {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PolarAlignment", "Instructions", "PolarAlignment.cs"));
            source.Should().Contain("var freshFeedbackMoveLimit = TppaFastAlignmentExecutionBudget.MaximumFreshFeedbackMoves;");
            source.Should().Contain("MaximumFreshFeedbackMovesAfterBoundedYBootstrapProbe");
            source.Should().Contain("The bounded Y bootstrap probe did not produce a qualified independent fresh response.");
            source.Should().Contain("independent fresh completion confirmation");
            source.Should().NotContain("EvaluateBeforeDynamicAuthorityMove(");
            source.Should().NotContain("directDynamicAuthorityGranted");
            source.Should().NotContain("directDynamicAuthorityFeedbackMoveCount");
        }

        [Test]
        public void RegressingBoundedYBootstrapProbeAbortsEvenBeforeFastRuntimeIsArmed() {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PolarAlignment", "Instructions", "PolarAlignment.cs"));

            source.Should().Contain("if (boundedYBootstrapProbeAwaitingFeedback) {");
            source.Should().NotContain("if (boundedYBootstrapProbeAwaitingFeedback && fastRuntimeContractArmed)");
            var responseGate = source.IndexOf("if (boundedYBootstrapProbeAwaitingFeedback) {", StringComparison.Ordinal);
            var regressionGate = source.IndexOf(
                "responseDecision.Classification == TppaPostMoveResponseClassification.Regressed",
                responseGate,
                StringComparison.Ordinal);
            var abort = source.IndexOf("TPAPAVM.AbortBoundedYBootstrapProbeAfterRegression();", regressionGate, StringComparison.Ordinal);
            var controllerUpdate = source.IndexOf("TPAPAVM.UpdateAutomatedAdjustmentFromFreshDetermination();", abort, StringComparison.Ordinal);

            responseGate.Should().BeGreaterThanOrEqualTo(0);
            regressionGate.Should().BeGreaterThan(responseGate);
            abort.Should().BeGreaterThan(regressionGate);
            controllerUpdate.Should().BeGreaterThan(abort);
        }

        [Test]
        public void OperationalMotionProtocolDoesNotUseElapsedTimeAsAdmissionAuthority() {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PolarAlignment", "Instructions", "PolarAlignment.cs"));

            source.Should().Contain("TPPA_OPERATIONAL_MOVE_ADMISSION");
            source.Should().Contain("Elapsed time is telemetry only");
            source.Should().NotContain("fastRuntimeDeadlineCTS");
            source.Should().NotContain("EvaluateBeforeMove(");
            source.Should().NotContain("EvaluateBeforeFreshAgreement(");
            source.Should().NotContain("five-minute runtime contract");
        }

        [Test]
        public void OperationalMotionControlsRemainActiveWithoutTheRuntimeBudgetOption() {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PolarAlignment", "Instructions", "PolarAlignment.cs"));

            source.Should().Contain(
                "var operationalMotionProtocolActive = automatedAdjustmentsEnabled");
            source.Should().Contain("if (operationalMotionProtocolActive) {");
            source.Should().Contain("var admission = TppaFastAlignmentExecutionBudget.EvaluateInitialTotal(");
            source.Should().Contain("TppaFastOperationalRouteAdmissionPolicy.Evaluate(");
            source.Should().Contain(
                "fastRuntimeContractArmed = operationalMotionProtocolActive");
            source.Should().Contain("&& operationalMotionProtocolActive");
            source.Should().Contain("&& !TPAPAVM.HasPendingFirstRunTwoAxisBootstrapProbe");
        }

        [Test]
        public void FirstRunDirectRouteDoesNotRequireASeparateConfirmationFlag() {
            var vmSource = File.ReadAllText(Path.Combine(RepositoryRoot(), "PolarAlignment", "TPAPAVM.cs"));
            var instructionSource = File.ReadAllText(Path.Combine(RepositoryRoot(), "PolarAlignment", "Instructions", "PolarAlignment.cs"));

            var bootstrapConfiguration = vmSource.IndexOf("automatedAdjustmentController.ConfigureFirstRunTwoAxisBootstrap(", StringComparison.Ordinal);
            var bootstrapConfigurationEnd = vmSource.IndexOf("if (useUpasController)", bootstrapConfiguration, StringComparison.Ordinal);
            var bootstrapQualification = instructionSource.IndexOf("var firstRunBootstrapQualification", StringComparison.Ordinal);
            var qualificationEnd = instructionSource.IndexOf("Logger.Info(", bootstrapQualification, StringComparison.Ordinal);

            bootstrapConfiguration.Should().BeGreaterThanOrEqualTo(0);
            bootstrapConfigurationEnd.Should().BeGreaterThan(bootstrapConfiguration);
            vmSource.Substring(bootstrapConfiguration, bootstrapConfigurationEnd - bootstrapConfiguration)
                .Should().NotContain("AvalonDirectFullTravelRouteConfirmed");
            bootstrapQualification.Should().BeGreaterThanOrEqualTo(0);
            qualificationEnd.Should().BeGreaterThan(bootstrapQualification);
            instructionSource.Substring(bootstrapQualification, qualificationEnd - bootstrapQualification)
                .Should().NotContain("AvalonDirectFullTravelRouteConfirmed");
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
            var branchEnd = source.IndexOf("// Operational timing telemetry starts only after physical-zero admission.", supervisorBranch, StringComparison.Ordinal);

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
            var persistentLatch = source.IndexOf("AbortAfterPartialDiagonalExecution", yDenied, StringComparison.Ordinal);
            var explicitNoInverse = source.IndexOf("remains outstanding and un-reversed", yDenied, StringComparison.Ordinal);
            var failedReturn = source.IndexOf("return false;", partialAbort, StringComparison.Ordinal);
            var nextSuccessfulReturn = source.IndexOf("return true;", partialAbort, StringComparison.Ordinal);

            yDenied.Should().BeGreaterThanOrEqualTo(0);
            persistentLatch.Should().BeGreaterThan(yDenied);
            explicitNoInverse.Should().BeGreaterThan(persistentLatch);
            partialAbort.Should().BeGreaterThan(yDenied);
            failedReturn.Should().BeGreaterThan(partialAbort);
            (nextSuccessfulReturn < 0 || nextSuccessfulReturn > failedReturn).Should().BeTrue(
                "a verified X move followed by a denied Y move is an incomplete diagonal, not a successful correction");
        }

        [Test]
        public void ResponseLearningExportsModelAndProbeFloorEvidenceWithoutAddingMotionAuthority() {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "PolarAlignment", "AutomatedAdjustmentController.cs"));

            source.Should().Contain("TPPA_UPAS_FRESH_RESPONSE_SAMPLE");
            source.Should().Contain("modelQualified=true");
            source.Should().Contain("rejectedXProbes=");
            source.Should().Contain("TPPA_UPAS_PROBE_REJECTED");
            source.Should().Contain("This is report-only field evidence. It deliberately has no authority over motion.");
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
