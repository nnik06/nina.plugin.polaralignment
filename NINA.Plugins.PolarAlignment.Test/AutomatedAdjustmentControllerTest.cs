using FluentAssertions;
using System;

namespace NINA.Plugins.PolarAlignment.Test {
    /// <summary>
    /// Closed-loop tests for the automated correction controller.
    ///
    /// These tests simulate the hardware as a local linear response:
    /// the controller issues X/Y commands, the synthetic mount changes the reported
    /// azimuth/altitude error, and the next solve feeds that observed error back.
    /// </summary>
    public class AutomatedAdjustmentControllerTest {
        [Test]
        public void FreshPolarAlignmentAgreementPolicy_AcceptsSignedVectorWithinToleranceScaledLimit() {
            var result = FreshPolarAlignmentAgreementPolicy.Evaluate(
                0.20, -0.30, 0.36,
                0.55, -0.05, 0.55,
                toleranceMinutes: 0.5);

            result.IsRepeatable.Should().BeTrue();
            result.VectorDeltaMinutes.Should().BeApproximately(Math.Sqrt(0.35 * 0.35 + 0.25 * 0.25), 1e-9);
            result.ThresholdMinutes.Should().Be(0.5);
        }

        [Test]
        public void FreshPolarAlignmentAgreementPolicy_CoarseAcquisitionAllowsBoundedRelativeDelta() {
            var result = FreshPolarAlignmentAgreementPolicy.EvaluateForCoarseAcquisition(
                63.578, -5.948, 63.856,
                65.125, -5.042, 65.320,
                toleranceMinutes: 1.0);

            result.IsRepeatable.Should().BeTrue();
            result.VectorDeltaMinutes.Should().BeApproximately(1.793, 0.002);
            result.ThresholdMinutes.Should().BeApproximately(1.93764, 0.0001);
        }

        [Test]
        public void FreshPolarAlignmentAgreementPolicy_CoarseAcquisitionIsCappedAndDoesNotRelaxFinalAgreement() {
            var coarse = FreshPolarAlignmentAgreementPolicy.EvaluateForCoarseAcquisition(
                300, 0, 300,
                304, 0, 304,
                toleranceMinutes: 1.0);
            var final = FreshPolarAlignmentAgreementPolicy.Evaluate(
                300, 0, 300,
                304, 0, 304,
                toleranceMinutes: 1.0);

            coarse.ThresholdMinutes.Should().Be(3.0);
            coarse.IsRepeatable.Should().BeFalse();
            final.ThresholdMinutes.Should().Be(1.0);
            final.IsRepeatable.Should().BeFalse();
        }

        [Test]
        public void FreshPolarAlignmentAgreementPolicy_RejectsDifferentSignedVectorDespiteBothTotalsBeingSmall() {
            var result = FreshPolarAlignmentAgreementPolicy.Evaluate(
                -0.60, 0.10, 0.61,
                0.60, 0.10, 0.61,
                toleranceMinutes: 1.0);

            result.IsRepeatable.Should().BeFalse();
            result.AzimuthDeltaMinutes.Should().BeApproximately(1.20, 1e-9);
            result.TotalDeltaMinutes.Should().BeApproximately(0, 1e-9);
            result.VectorDeltaMinutes.Should().BeApproximately(1.20, 1e-9);
        }

        [Test]
        public void FreshPolarAlignmentAgreementPolicy_SeparatesMagnitudeChangeFromVectorSeparation() {
            var result = FreshPolarAlignmentAgreementPolicy.Evaluate(
                2.288, -15.641, 15.808,
                -0.961, -11.886, 11.925,
                toleranceMinutes: 1.0);

            result.TotalDeltaMinutes.Should().BeApproximately(-3.883, 1e-9);
            result.VectorDeltaMinutes.Should().BeApproximately(
                Math.Sqrt(3.249 * 3.249 + 3.755 * 3.755),
                1e-9);
            result.VectorDeltaMinutes.Should().BeGreaterThan(Math.Abs(result.TotalDeltaMinutes));
        }

        [Test]
        public void FreshPolarAlignmentAgreementPolicy_UsesSelectedToleranceAboveMinimumFloor() {
            var result = FreshPolarAlignmentAgreementPolicy.Evaluate(
                1.0, -1.0, 1.41,
                2.5, -1.0, 2.69,
                toleranceMinutes: 2.0);

            result.IsRepeatable.Should().BeTrue();
            result.ThresholdMinutes.Should().Be(2.0);
        }

        [Test]
        public void FreshPolarAlignmentAgreementPolicy_CenteredReciprocityRemovesLinearDrift() {
            var result = FreshPolarAlignmentAgreementPolicy.EvaluateCenteredReciprocity(
                firstForwardAzimuthMinutes: 1.0,
                firstForwardAltitudeMinutes: -2.0,
                reciprocalAzimuthMinutes: 2.0,
                reciprocalAltitudeMinutes: -1.0,
                repeatedForwardAzimuthMinutes: 3.0,
                repeatedForwardAltitudeMinutes: 0.0,
                toleranceMinutes: 0.5);

            result.IsRepeatable.Should().BeTrue();
            result.VectorDeltaMinutes.Should().BeApproximately(0, 1e-9);
        }

        [Test]
        public void FreshPolarAlignmentAgreementPolicy_TimeCenteredReciprocityRemovesAsymmetricLinearDrift() {
            var start = new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc);
            var result = FreshPolarAlignmentAgreementPolicy.EvaluateTimeCenteredReciprocity(
                firstForwardAzimuthMinutes: 1.0,
                firstForwardAltitudeMinutes: -2.0,
                firstForwardObservationTimeUtc: start,
                reciprocalAzimuthMinutes: 3.0,
                reciprocalAltitudeMinutes: 0.0,
                reciprocalObservationTimeUtc: start.AddMinutes(4),
                repeatedForwardAzimuthMinutes: 6.0,
                repeatedForwardAltitudeMinutes: 3.0,
                repeatedForwardObservationTimeUtc: start.AddMinutes(10),
                toleranceMinutes: 0.5);

            result.IsRepeatable.Should().BeTrue();
            result.VectorDeltaMinutes.Should().BeApproximately(0, 1e-9);
        }

        [Test]
        public void FreshPolarAlignmentAgreementPolicy_TimeCenteredReciprocityDetectsDirectionBias() {
            var start = new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc);
            var result = FreshPolarAlignmentAgreementPolicy.EvaluateTimeCenteredReciprocity(
                firstForwardAzimuthMinutes: 1.0,
                firstForwardAltitudeMinutes: -2.0,
                firstForwardObservationTimeUtc: start,
                reciprocalAzimuthMinutes: 4.0,
                reciprocalAltitudeMinutes: 0.0,
                reciprocalObservationTimeUtc: start.AddMinutes(4),
                repeatedForwardAzimuthMinutes: 6.0,
                repeatedForwardAltitudeMinutes: 3.0,
                repeatedForwardObservationTimeUtc: start.AddMinutes(10),
                toleranceMinutes: 0.5);

            result.IsRepeatable.Should().BeFalse();
            result.AzimuthDeltaMinutes.Should().BeApproximately(1.0, 1e-9);
            result.AltitudeDeltaMinutes.Should().BeApproximately(0.0, 1e-9);
            result.VectorDeltaMinutes.Should().BeApproximately(1.0, 1e-9);
            result.Reason.Should().Contain("exceeds");
        }

        [Test]
        public void FreshPolarAlignmentAgreementPolicy_TimeCenteredReciprocityRejectsInvalidTiming() {
            var start = new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc);
            var result = FreshPolarAlignmentAgreementPolicy.EvaluateTimeCenteredReciprocity(
                firstForwardAzimuthMinutes: 1.0,
                firstForwardAltitudeMinutes: -2.0,
                firstForwardObservationTimeUtc: start,
                reciprocalAzimuthMinutes: 2.0,
                reciprocalAltitudeMinutes: -1.0,
                reciprocalObservationTimeUtc: start.AddMinutes(6),
                repeatedForwardAzimuthMinutes: 3.0,
                repeatedForwardAltitudeMinutes: 0.0,
                repeatedForwardObservationTimeUtc: start.AddMinutes(5),
                toleranceMinutes: 0.5);

            result.IsRepeatable.Should().BeFalse();
            result.Reason.Should().Contain("strictly increasing");
            double.IsNaN(result.AzimuthDeltaMinutes).Should().BeTrue();
            double.IsNaN(result.AltitudeDeltaMinutes).Should().BeTrue();
            double.IsNaN(result.TotalDeltaMinutes).Should().BeTrue();
            double.IsNaN(result.VectorDeltaMinutes).Should().BeTrue();
        }

        [Test]
        public void FreshPolarAlignmentAgreementPolicy_TimeCenteredReciprocityRejectsNonUtcTimestamp() {
            var start = new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Unspecified);
            var result = FreshPolarAlignmentAgreementPolicy.EvaluateTimeCenteredReciprocity(
                1.0, -2.0, start,
                3.0, 0.0, start.AddMinutes(4),
                6.0, 3.0, start.AddMinutes(10),
                toleranceMinutes: 0.5);

            result.IsRepeatable.Should().BeFalse();
            result.Reason.Should().Contain("DateTimeKind.Utc");
        }

        [Test]
        public void FreshPolarAlignmentAgreementPolicy_TimeCenteredReciprocityRejectsDegenerateSpan() {
            var start = new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc);
            var result = FreshPolarAlignmentAgreementPolicy.EvaluateTimeCenteredReciprocity(
                1.0, -2.0, start,
                3.0, 0.0, start.AddTicks(1),
                6.0, 3.0, start.AddTicks(2),
                toleranceMinutes: 0.5);

            result.IsRepeatable.Should().BeFalse();
            result.Reason.Should().Contain("at least");
        }

        [Test]
        public void FreshPolarAlignmentAgreementPolicy_TimeCenteredReciprocityRejectsNonFiniteMeasurement() {
            var start = new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc);
            var result = FreshPolarAlignmentAgreementPolicy.EvaluateTimeCenteredReciprocity(
                double.NaN, -2.0, start,
                3.0, 0.0, start.AddMinutes(4),
                6.0, 3.0, start.AddMinutes(10),
                toleranceMinutes: 0.5);

            result.IsRepeatable.Should().BeFalse();
            result.Reason.Should().Contain("not finite");
        }

        [Test]
        public void FreshPolarAlignmentAgreementPolicy_TimeCenteredReciprocityMatchesMidpointPolicy() {
            var start = new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc);
            var timed = FreshPolarAlignmentAgreementPolicy.EvaluateTimeCenteredReciprocity(
                1.0, -2.0, start,
                3.0, -1.0, start.AddMinutes(5),
                3.0, 0.0, start.AddMinutes(10),
                toleranceMinutes: 0.5);
            var midpoint = FreshPolarAlignmentAgreementPolicy.EvaluateCenteredReciprocity(
                1.0, -2.0,
                3.0, -1.0,
                3.0, 0.0,
                toleranceMinutes: 0.5);

            timed.Should().BeEquivalentTo(midpoint);
        }

        [Test]
        public void FreshPolarAlignmentAgreementPolicy_CenteredReciprocityDetectsDirectionBias() {
            var result = FreshPolarAlignmentAgreementPolicy.EvaluateCenteredReciprocity(
                firstForwardAzimuthMinutes: 1.0,
                firstForwardAltitudeMinutes: -2.0,
                reciprocalAzimuthMinutes: 3.0,
                reciprocalAltitudeMinutes: -1.0,
                repeatedForwardAzimuthMinutes: 3.0,
                repeatedForwardAltitudeMinutes: 0.0,
                toleranceMinutes: 0.5);

            result.IsRepeatable.Should().BeFalse();
            result.AzimuthDeltaMinutes.Should().BeApproximately(1.0, 1e-9);
        }

        [Test]
        public void FreshPolarAlignmentAgreementPolicy_InvalidMeasurementFailsClosed() {
            var result = FreshPolarAlignmentAgreementPolicy.Evaluate(
                double.NaN, 0, 0,
                0, 0, 0,
                toleranceMinutes: 1.0);

            result.IsRepeatable.Should().BeFalse();
            result.Reason.Should().Contain("not finite");
        }
        [Test]
        public void AutomatedAdjustmentController_WorseningZeroCrossingDoesNotReplaceTrustedGain() {
            // Golden trace from the 2026-07-17 02:36 UPAS field run. The former controller
            // replaced the trusted gain with the weak acquisition response, commanded X=2.566,
            // overshot through zero, then incorrectly continued in the positive direction.
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.SeedUpasAzimuthResponseMemory(1.4379 / 60.0);
            controller.UpdateObservation(-10.0 / 60.0, 0);

            var firstAcquisition = controller.CreatePlan();
            firstAcquisition.XMagnitude.Should().Be(8.0);
            controller.NoteSuccessfulExecution(firstAcquisition);
            controller.UpdateObservation(-6.0 / 60.0, 0);

            var confirmingAcquisition = controller.CreatePlan();
            confirmingAcquisition.XMagnitude.Should().Be(8.0);
            controller.NoteSuccessfulExecution(confirmingAcquisition);
            controller.UpdateObservation(6.05 / 60.0, 0);
            controller.TryGetTrustedXAzimuthResponse(out var trustedAfterCrossing).Should().BeTrue();

            (trustedAfterCrossing * 60.0).Should().BeApproximately(1.4379, 1e-6);
        }

        [Test]
        public void AutomatedAlignmentCompletionGuard_RequiresTwoConsecutiveBelowToleranceObservations() {
            var guard = new AutomatedAlignmentCompletionGuard();

            guard.Evaluate(0.8, 1.0).Should().Be(AutomatedAlignmentCompletionDecision.ValidateWithoutMoving);
            guard.Evaluate(0.8, 1.0).Should().Be(AutomatedAlignmentCompletionDecision.VerifyFreshThreePoint);
            guard.EvaluateFreshVerification(0.8, 1.0).Should().Be(AutomatedAlignmentCompletionDecision.Finish);
        }

        [Test]
        public void AutomatedAlignmentCompletionGuard_ReboundRestartsValidation() {
            var guard = new AutomatedAlignmentCompletionGuard();

            guard.Evaluate(0.8, 1.0).Should().Be(AutomatedAlignmentCompletionDecision.ValidateWithoutMoving);
            guard.Evaluate(1.1, 1.0).Should().Be(AutomatedAlignmentCompletionDecision.ContinueCorrection);
            guard.Evaluate(0.8, 1.0).Should().Be(AutomatedAlignmentCompletionDecision.ValidateWithoutMoving);
        }

        [Test]
        public void AutomatedAlignmentCompletionGuard_FailedFreshVerificationAbortsImmediately() {
            var guard = new AutomatedAlignmentCompletionGuard();

            guard.Evaluate(0.8, 1.0).Should().Be(AutomatedAlignmentCompletionDecision.ValidateWithoutMoving);
            guard.Evaluate(0.8, 1.0).Should().Be(AutomatedAlignmentCompletionDecision.VerifyFreshThreePoint);
            guard.EvaluateFreshVerification(1.1, 1.0).Should().Be(AutomatedAlignmentCompletionDecision.AbortAfterFreshVerificationFailures);
        }

        [TestCase(double.NaN, 1.0)]
        [TestCase(double.PositiveInfinity, 1.0)]
        [TestCase(-0.1, 1.0)]
        [TestCase(0.5, 0.0)]
        [TestCase(0.5, double.NaN)]
        public void AutomatedAlignmentCompletionGuard_InvalidInputsFailClosed(double totalErrorMinutes, double toleranceMinutes) {
            var guard = new AutomatedAlignmentCompletionGuard();

            guard.Evaluate(totalErrorMinutes, toleranceMinutes)
                .Should().Be(AutomatedAlignmentCompletionDecision.ContinueCorrection);
        }
        [Test]
        public void AutomatedAdjustmentController_LearnsReversedAzimuthAxisAndConverges() {
            // X is intentionally reversed here: a positive X command makes the azimuth error worse.
            // The controller should identify that from the measured response and converge anyway.
            var controller = new AutomatedAdjustmentController();
            var plant = new[,] {
                { +0.08,  0.00 },
                {  0.00, -0.07 }
            };

            var result = RunClosedLoop(controller, plant, initialAzimuthErrorDegrees: 0.4, initialAltitudeErrorDegrees: -0.25, maxIterations: 18);

            result.FinalErrorDegrees.Should().BeLessThan(0.03);
            result.Iterations.Should().BeLessThan(18);
            controller.HasResponseModel.Should().BeTrue();
        }

        [TestCase(-0.08, -0.07)]
        [TestCase(-0.08, 0.07)]
        [TestCase(0.08, -0.07)]
        [TestCase(0.08, 0.07)]
        public void AutomatedAdjustmentController_LearnsAxisPolarityAndConverges(double azimuthDeltaPerXUnit, double altitudeDeltaPerYUnit) {
            var controller = new AutomatedAdjustmentController();
            var plant = new[,] {
                { azimuthDeltaPerXUnit,  0.00 },
                { 0.00, altitudeDeltaPerYUnit }
            };

            var result = RunClosedLoop(controller, plant, initialAzimuthErrorDegrees: 0.4, initialAltitudeErrorDegrees: -0.25, maxIterations: 18);

            result.FinalErrorDegrees.Should().BeLessThan(0.03);
            result.Iterations.Should().BeLessThan(18);
            controller.HasResponseModel.Should().BeTrue();
        }


        [Test]
        public void AutomatedAdjustmentController_UsesRememberedUpasResponseToChooseCurrentCorrectionDirection() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.SeedUpasAzimuthResponseMemory(-0.45 / 60.0);

            controller.UpdateObservation(10.0 / 60.0, 0);
            var positiveErrorPlan = controller.CreatePlan();

            positiveErrorPlan.XMagnitude.Should().BePositive();
            positiveErrorPlan.XMagnitude.Should().BeApproximately(8.0, 1e-9);

            controller.Reset();
            controller.SeedUpasAzimuthResponseMemory(-0.45 / 60.0);
            controller.UpdateObservation(-10.0 / 60.0, 0);
            var negativeErrorPlan = controller.CreatePlan();

            negativeErrorPlan.XMagnitude.Should().BeNegative();
            negativeErrorPlan.XMagnitude.Should().BeApproximately(-8.0, 1e-9);
        }

        [TestCase(1.0, 2.6)]
        [TestCase(-1.0, -2.6)]
        [TestCase(0.6, 2.0)]
        public void AutomatedAdjustmentController_UsesDampedRememberedResponseNearTolerance(double azimuthErrorArcminutes, double expectedX) {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.SeedUpasAzimuthResponseMemory(-0.25 / 60.0);
            controller.UpdateObservation(azimuthErrorArcminutes / 60.0, 0);

            var plan = controller.CreatePlan();

            plan.XMagnitude.Should().BeApproximately(expectedX, 1e-9);
            plan.IsProbe.Should().BeTrue();
            plan.Reason.Should().Contain("remembered UPAS azimuth response");
        }

        [Test]
        public void AutomatedAdjustmentController_RobustMemoryDoesNotFollowOneDirectionalOutlier() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.SeedUpasAzimuthResponseMemory(-0.25 / 60.0);
            controller.UpdateObservation(40.0 / 60.0, 0);
            var plan = new AutomatedAdjustmentPlan(24, 0, true, "Synthetic trusted azimuth probe");
            controller.NoteSuccessfulExecution(plan);

            var afterError = 40.0 / 60.0 + plan.XMagnitude * (-0.5 / 60.0);
            controller.UpdateObservation(afterError, 0);

            controller.TryGetTrustedXAzimuthResponse(out var remembered).Should().BeTrue();
            (remembered * 60.0).Should().BeApproximately(-0.25, 1e-9);
        }

        [Test]
        public void AutomatedAdjustmentController_SeededUpasSeatingDoesNotConfirmAzimuthResponse() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.SeedXSeating(-1);
            controller.UpdateObservation(10.0 / 60.0, 0);

            var plan = controller.CreatePlan();

            plan.XMagnitude.Should().BeNegative();
            plan.XMagnitude.Should().BeApproximately(-8.0, 1e-9);
            plan.IsProbe.Should().BeTrue();
            controller.TryGetTrustedXAzimuthResponse(out _).Should().BeFalse();
        }

        [Test]
        public void AutomatedAdjustmentController_LearnsPoorCalibrationAndAxisCrossCoupling() {
            // This plant is deliberately miscalibrated and cross-coupled:
            // one X unit changes the sky by far more than one arcminute, and each axis affects
            // both residual error components. The controller should still settle by learning A.
            var controller = new AutomatedAdjustmentController();
            var plant = new[,] {
                { -0.18, -0.03 },
                { +0.04, -0.11 }
            };

            var result = RunClosedLoop(controller, plant, initialAzimuthErrorDegrees: 0.9, initialAltitudeErrorDegrees: 0.6, maxIterations: 18);

            result.FinalErrorDegrees.Should().BeLessThan(0.04);
            result.Iterations.Should().BeLessThan(18);
            controller.SampleCount.Should().BeGreaterThanOrEqualTo(3);
        }

        [Test]
        public void AutomatedAdjustmentController_DoesNotLearnFromFailedMove() {
            // A failed nudge must not be treated as a valid identification sample.
            // Otherwise the controller would "learn" zero response and corrupt the model.
            var controller = new AutomatedAdjustmentController();
            controller.UpdateObservation(0.5, -0.3);

            var firstPlan = controller.CreatePlan();
            firstPlan.HasMovement.Should().BeTrue();
            firstPlan.IsProbe.Should().BeTrue();
            firstPlan.XMagnitude.Should().NotBe(0);

            controller.NoteFailedExecution(firstPlan);
            controller.UpdateObservation(0.5, -0.3);

            controller.SampleCount.Should().Be(0);

            var secondPlan = controller.CreatePlan();
            secondPlan.HasMovement.Should().BeTrue();
            secondPlan.IsProbe.Should().BeTrue();
            secondPlan.XMagnitude.Should().NotBe(0);
        }

        [Test]
        public void AutomatedAdjustmentController_ChargesFullAttemptedXTravelAfterFailedMove() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true) {
                AzimuthTravelGuardEnabled = true,
                AzimuthTravelGuardConfirmed = true,
                AzimuthTravelLimitDegrees = 1.0,
                AzimuthDegreesPerXUnit = 0.025,
            };
            controller.UpdateObservation(30.0 / 60.0, 0);

            var plan = controller.CreatePlan();
            plan.XMagnitude.Should().Be(8.0);

            controller.NoteFailedExecution(plan);

            controller.AzimuthTravelUsedDegrees.Should().BeApproximately(0.2, 0.0001);
        }

        [Test]
        public void AutomatedAdjustmentController_DoesNotContinueStaleXEngagementAfterFailedMove() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(30.0 / 60.0, 0);

            var firstPlan = controller.CreatePlan();
            controller.NoteSuccessfulExecution(firstPlan);
            controller.UpdateObservation(31.0 / 60.0, 0);

            var engagementPlan = controller.CreatePlan();
            engagementPlan.Reason.Should().Contain("Continuing UPAS azimuth engagement run");

            controller.NoteFailedExecution(engagementPlan);

            var nextPlan = controller.CreatePlan();
            nextPlan.HasMovement.Should().BeTrue();
            nextPlan.Reason.Should().Be("Probing azimuth response (attempt 1)");
        }

        [Test]
        public void AutomatedAdjustmentController_RejectsSubNoiseSamples() {
            var controller = new AutomatedAdjustmentController();
            controller.UpdateObservation(0.5, -0.3);

            var plan = controller.CreatePlan();
            plan.HasMovement.Should().BeTrue();
            plan.IsProbe.Should().BeTrue();

            controller.NoteSuccessfulExecution(plan);
            controller.UpdateObservation(0.5 + (0.1 / 60.0), -0.3);

            controller.SampleCount.Should().Be(0);
            controller.HasResponseModel.Should().BeFalse();
        }

        [Test]
        public void AutomatedAdjustmentController_ProbesWhenAboveNoiseFloorWithoutModel() {
            var controller = new AutomatedAdjustmentController();
            controller.UpdateObservation(1.5 / 60.0, 0);

            var plan = controller.CreatePlan();

            plan.HasMovement.Should().BeTrue();
            plan.IsProbe.Should().BeTrue();
        }

        [Test]
        public void AutomatedAdjustmentController_DoesNotProbeBelowNoiseFloorWithoutModel() {
            var controller = new AutomatedAdjustmentController();
            controller.UpdateObservation(0.2 / 60.0, 0);

            var plan = controller.CreatePlan();

            plan.HasMovement.Should().BeFalse();
            plan.Reason.Should().Contain("probe resolution");
        }

        [Test]
        public void AutomatedAdjustmentController_RejectsImplausiblyLargeSamples() {
            var controller = new AutomatedAdjustmentController();
            controller.UpdateObservation(0.5, -0.3);

            var plan = controller.CreatePlan();
            plan.HasMovement.Should().BeTrue();
            plan.IsProbe.Should().BeTrue();

            controller.NoteSuccessfulExecution(plan);
            controller.UpdateObservation(0.5 + (80.0 / 60.0), -0.3);

            controller.SampleCount.Should().Be(0);
            controller.HasResponseModel.Should().BeFalse();

            var retryPlan = controller.CreatePlan();
            retryPlan.HasMovement.Should().BeTrue();
            retryPlan.IsProbe.Should().BeTrue();
            retryPlan.XMagnitude.Should().Be(4.0);
        }

        [Test]
        public void AutomatedAdjustmentController_DoesNotSacrificeSolvedAltitudeForSmallAzimuthImprovement() {
            var controller = new AutomatedAdjustmentController();
            var azimuthError = 40.0 / 60.0;
            var altitudeError = 0.1 / 60.0;

            controller.UpdateObservation(azimuthError, altitudeError);

            var xProbe = new AutomatedAdjustmentPlan(9, 0, true, "Synthetic azimuth probe");
            controller.NoteSuccessfulExecution(xProbe);
            var afterXAzimuth = azimuthError - (0.081 / 60.0) * xProbe.XMagnitude;
            controller.UpdateObservation(afterXAzimuth, altitudeError);

            var yProbe = new AutomatedAdjustmentPlan(0, 2, true, "Synthetic altitude probe");
            controller.NoteSuccessfulExecution(yProbe);
            controller.UpdateObservation(afterXAzimuth - (1.2 / 60.0) * yProbe.YMagnitude,
                                         altitudeError + (2.4 / 60.0) * yProbe.YMagnitude);

            controller.HasResponseModel.Should().BeTrue();
            controller.UpdateObservation(azimuthError, altitudeError);

            var correction = controller.CreatePlan();

            correction.HasMovement.Should().BeTrue();
            Math.Abs(correction.XMagnitude).Should().BeGreaterThan(Math.Abs(correction.YMagnitude));
        }
        [Test]
        public void AutomatedAdjustmentController_DoesNotUseSmallAltitudeAxisCrossTermForDominantAzimuthError() {
            var controller = new AutomatedAdjustmentController();
            controller.UpdateObservation(0.8, 0.005);

            var xProbe = new AutomatedAdjustmentPlan(4, 0, true, "Synthetic azimuth probe");
            controller.NoteSuccessfulExecution(xProbe);
            controller.UpdateObservation(0.8 - 0.08 * xProbe.XMagnitude, 0.005);

            var yProbe = new AutomatedAdjustmentPlan(0, 2, true, "Synthetic altitude probe");
            controller.NoteSuccessfulExecution(yProbe);
            controller.UpdateObservation(0.8 - 0.08 * xProbe.XMagnitude - 0.01 * yProbe.YMagnitude,
                                         0.005 + 0.07 * yProbe.YMagnitude);

            controller.UpdateObservation(0.8, 0.005);

            var correction = controller.CreatePlan();

            correction.HasMovement.Should().BeTrue();
            Math.Abs(correction.XMagnitude).Should().BeGreaterThan(Math.Abs(correction.YMagnitude));
        }

        [Test]
        public void AutomatedAdjustmentController_DoesNotFlipXDirectionWhenWorseningOccursBeforeClearance() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(30.0 / 60.0, -9.0 / 60.0);

            var firstProbe = controller.CreatePlan();
            firstProbe.XMagnitude.Should().Be(8.0);
            controller.NoteSuccessfulExecution(firstProbe);
            controller.UpdateObservation(40.5 / 60.0, -9.0 / 60.0);

            var sameDirectionProbe = controller.CreatePlan();

            sameDirectionProbe.HasMovement.Should().BeTrue();
            sameDirectionProbe.IsProbe.Should().BeTrue();
            sameDirectionProbe.XMagnitude.Should().Be(8.0);
            sameDirectionProbe.YMagnitude.Should().Be(0);
        }

        [Test]
        public void AutomatedAdjustmentController_KeepsXDirectionWhenTinyDominantAzimuthProbeWorsensBeforeClearance() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(35.0 / 60.0, 0);

            var firstProbe = controller.CreatePlan();
            firstProbe.XMagnitude.Should().Be(8.0);
            controller.NoteSuccessfulExecution(firstProbe);
            controller.UpdateObservation(40.0 / 60.0, 0);

            var retryProbe = controller.CreatePlan();

            retryProbe.IsProbe.Should().BeTrue();
            retryProbe.XMagnitude.Should().Be(8.0);
            retryProbe.YMagnitude.Should().Be(0);
        }

        [Test]
        public void AutomatedAdjustmentController_ContinuesSameXDirectionBeforeClearanceWhenItWorsensDominantAzimuth() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(35.0 / 60.0, 0);

            var firstProbe = controller.CreatePlan();
            firstProbe.XMagnitude.Should().Be(8.0);
            controller.NoteSuccessfulExecution(firstProbe);
            controller.UpdateObservation(35.1 / 60.0, 0);

            var sameDirectionProbe = controller.CreatePlan();
            sameDirectionProbe.XMagnitude.Should().Be(8.0);
            controller.NoteSuccessfulExecution(sameDirectionProbe);
            controller.UpdateObservation(40.0 / 60.0, 0);

            var committedProbe = controller.CreatePlan();

            committedProbe.IsProbe.Should().BeTrue();
            committedProbe.XMagnitude.Should().Be(8.0);
            committedProbe.YMagnitude.Should().Be(0);
        }

        [Test]
        public void AutomatedAdjustmentController_ReversesXDirectionAfterTwoStrongWorseningsBeforeClearance() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(35.0 / 60.0, 0);

            var firstProbe = controller.CreatePlan();
            firstProbe.XMagnitude.Should().Be(8.0);
            controller.NoteSuccessfulExecution(firstProbe);
            controller.UpdateObservation(41.0 / 60.0, 0);

            var secondProbe = controller.CreatePlan();
            secondProbe.XMagnitude.Should().Be(8.0);
            controller.NoteSuccessfulExecution(secondProbe);
            controller.UpdateObservation(47.0 / 60.0, 0);

            var reversedPlan = controller.CreatePlan();
            reversedPlan.HasMovement.Should().BeTrue();
            reversedPlan.XMagnitude.Should().Be(-12.0);
            reversedPlan.YMagnitude.Should().Be(0);
        }

        [Test]
        public void AutomatedAdjustmentController_RequiresConsecutiveStrongImprovementBeforeEarlyEngagementConfirmation() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(40.0 / 60.0, 0);

            var firstProbe = controller.CreatePlan();
            controller.NoteSuccessfulExecution(firstProbe);
            controller.UpdateObservation(36.0 / 60.0, 0);

            controller.SampleCount.Should().Be(0);
            var confirmationProbe = controller.CreatePlan();
            confirmationProbe.XMagnitude.Should().Be(8.0);
            controller.NoteSuccessfulExecution(confirmationProbe);
            controller.UpdateObservation(32.0 / 60.0, 0);

            controller.SampleCount.Should().Be(1);
            controller.CreatePlan().Reason.Should().Contain("Confirmed azimuth");
        }

        [Test]
        public void AutomatedAdjustmentController_ReversesEarlyOnlyAfterConsecutiveStrongWorsening() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(35.0 / 60.0, 0);

            var firstProbe = controller.CreatePlan();
            controller.NoteSuccessfulExecution(firstProbe);
            controller.UpdateObservation(41.0 / 60.0, 0);

            var confirmationProbe = controller.CreatePlan();
            confirmationProbe.XMagnitude.Should().Be(8.0);
            controller.NoteSuccessfulExecution(confirmationProbe);
            controller.UpdateObservation(47.0 / 60.0, 0);

            var reversedPlan = controller.CreatePlan();
            reversedPlan.HasMovement.Should().BeTrue();
            reversedPlan.XMagnitude.Should().Be(-12.0);
            reversedPlan.YMagnitude.Should().Be(0);
        }

        [Test]
        public void AutomatedAdjustmentController_DoesNotReverseOnTwoCorrelatedSubThresholdWorsenings() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(35.0 / 60.0, 0);
            var firstProbe = controller.CreatePlan();
            controller.NoteSuccessfulExecution(firstProbe);
            controller.UpdateObservation(37.4 / 60.0, 0);
            var secondProbe = controller.CreatePlan();
            controller.NoteSuccessfulExecution(secondProbe);
            controller.UpdateObservation(39.8 / 60.0, 0);

            var nextPlan = controller.CreatePlan();
            nextPlan.HasMovement.Should().BeTrue();
            nextPlan.XMagnitude.Should().Be(8.0);
            nextPlan.YMagnitude.Should().Be(0);
        }

        [Test]
        public void AutomatedAdjustmentController_WeakObservationResetsEarlyWorseningStreak() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(35.0 / 60.0, 0);

            var firstMove = new AutomatedAdjustmentPlan(4, 0, true, "Synthetic first engagement move");
            controller.NoteSuccessfulExecution(firstMove);
            controller.UpdateObservation(41.0 / 60.0, 0);

            var weakMove = new AutomatedAdjustmentPlan(4, 0, true, "Synthetic weak-evidence move");
            controller.NoteSuccessfulExecution(weakMove);
            controller.UpdateObservation(43.0 / 60.0, 0);

            var secondStrongMove = new AutomatedAdjustmentPlan(4, 0, true, "Synthetic second strong move");
            controller.NoteSuccessfulExecution(secondStrongMove);
            controller.UpdateObservation(49.0 / 60.0, 0);

            var nextPlan = controller.CreatePlan();
            nextPlan.HasMovement.Should().BeTrue();
            nextPlan.XMagnitude.Should().BePositive();
            nextPlan.YMagnitude.Should().Be(0);
        }
        [Test]
        public void AutomatedAdjustmentController_PermitsOnlyOneEarlyReversalPerAcquisition() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(35.0 / 60.0, 0);
            var firstProbe = controller.CreatePlan();
            controller.NoteSuccessfulExecution(firstProbe);
            controller.UpdateObservation(41.0 / 60.0, 0);
            var secondProbe = controller.CreatePlan();
            controller.NoteSuccessfulExecution(secondProbe);
            controller.UpdateObservation(47.0 / 60.0, 0);

            var firstReversal = controller.CreatePlan();
            firstReversal.XMagnitude.Should().BeNegative();
            controller.NoteSuccessfulExecution(firstReversal);
            controller.UpdateObservation(53.0 / 60.0, 0);
            var secondDirectionProbe = controller.CreatePlan();
            secondDirectionProbe.XMagnitude.Should().BeNegative();
            controller.NoteSuccessfulExecution(secondDirectionProbe);
            controller.UpdateObservation(59.0 / 60.0, 0);

            var clearancePlan = controller.CreatePlan();
            clearancePlan.HasMovement.Should().BeTrue();
            clearancePlan.XMagnitude.Should().BeNegative();
            clearancePlan.YMagnitude.Should().Be(0);
            controller.NoteSuccessfulExecution(clearancePlan);
            controller.UpdateObservation(65.0 / 60.0, 0);

            var blockedPlan = controller.CreatePlan();
            blockedPlan.HasMovement.Should().BeFalse();
            blockedPlan.Reason.Should().Contain("Both automated azimuth motor directions");
        }
        [Test]
        public void AutomatedAdjustmentController_DoesNotConfirmEarlyEngagementOnSingleLargeZeroCrossing() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(2.0 / 60.0, 0);

            var firstProbe = controller.CreatePlan();
            controller.NoteSuccessfulExecution(firstProbe);
            controller.UpdateObservation(-2.0 / 60.0, 0);

            controller.SampleCount.Should().Be(0);
            var nextProbe = controller.CreatePlan();
            nextProbe.HasMovement.Should().BeTrue();
            nextProbe.XMagnitude.Should().Be(4.0);
            nextProbe.YMagnitude.Should().Be(0);
        }
        [Test]
        public void AutomatedAdjustmentController_ContinuesLargeRatioAzimuthAcquisitionBeforeClearance() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(-121.7 / 60.0, 3.4 / 60.0);

            var firstProbe = controller.CreatePlan();
            firstProbe.XMagnitude.Should().Be(8.0);
            controller.NoteSuccessfulExecution(firstProbe);
            controller.UpdateObservation(-124.1 / 60.0, 7.75 / 60.0);

            var secondProbe = controller.CreatePlan();
            secondProbe.XMagnitude.Should().Be(8.0);
            controller.NoteSuccessfulExecution(secondProbe);
            controller.UpdateObservation(-125.6 / 60.0, 5.0 / 60.0);

            var committedProbe = controller.CreatePlan();

            committedProbe.HasMovement.Should().BeTrue();
            committedProbe.XMagnitude.Should().Be(8.0);
            committedProbe.YMagnitude.Should().Be(0);
        }

        [Test]
        public void AutomatedAdjustmentController_UsesUpasEngagementWhenAzimuthIsLargerButNotThreeTimesAltitude() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(10.0 / 60.0, 5.0 / 60.0);

            var firstProbe = controller.CreatePlan();
            firstProbe.XMagnitude.Should().Be(8.0);
            firstProbe.YMagnitude.Should().Be(0);
            controller.NoteSuccessfulExecution(firstProbe);
            controller.UpdateObservation(10.1 / 60.0, 5.0 / 60.0);

            var secondProbe = controller.CreatePlan();

            secondProbe.HasMovement.Should().BeTrue();
            secondProbe.XMagnitude.Should().Be(8.0);
            secondProbe.YMagnitude.Should().Be(0);
            secondProbe.Reason.Should().Contain("engagement");
        }

        [Test]
        public void AutomatedAdjustmentController_UsesProportionalXCorrectionAfterDominantAzimuthIsConfirmed() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(40.0 / 60.0, 0);

            var firstProbe = controller.CreatePlan();
            controller.NoteSuccessfulExecution(firstProbe);
            controller.UpdateObservation(45.0 / 60.0, 0);

            var backlashProbe = controller.CreatePlan();
            backlashProbe.XMagnitude.Should().Be(8.0);
            controller.NoteSuccessfulExecution(backlashProbe);
            controller.UpdateObservation(45.0 / 60.0, 0);

            var confirmingProbe = controller.CreatePlan();
            confirmingProbe.XMagnitude.Should().Be(8.0);
            controller.NoteSuccessfulExecution(confirmingProbe);
            controller.UpdateObservation(39.0 / 60.0, 0);

            var correction = controller.CreatePlan();

            correction.Reason.Should().Contain("Confirmed azimuth");
            correction.XMagnitude.Should().Be(20.0);
            correction.YMagnitude.Should().Be(0);
        }

        [Test]
        public void AutomatedAdjustmentController_RaisesTinyConfirmedXReversalToProbeMagnitude() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(40.0 / 60.0, 0);

            var firstProbe = controller.CreatePlan();
            controller.NoteSuccessfulExecution(firstProbe);
            controller.UpdateObservation(45.0 / 60.0, 0);

            var backlashProbe = controller.CreatePlan();
            controller.NoteSuccessfulExecution(backlashProbe);
            controller.UpdateObservation(45.0 / 60.0, 0);

            var confirmingProbe = controller.CreatePlan();
            controller.NoteSuccessfulExecution(confirmingProbe);
            controller.UpdateObservation(39.0 / 60.0, 0);

            controller.UpdateObservation(-1.5 / 60.0, 0);
            var firstReversal = controller.CreatePlan();
            firstReversal.HasMovement.Should().BeFalse();
            firstReversal.Reason.Should().Contain("Debouncing");

            var confirmedReversal = controller.CreatePlan();

            confirmedReversal.HasMovement.Should().BeTrue();
            confirmedReversal.XMagnitude.Should().Be(-4.0);
            confirmedReversal.YMagnitude.Should().Be(0);
            confirmedReversal.Reason.Should().Contain("X reversal clearance");
            confirmedReversal.Reason.Should().Contain("near-target X limit");
        }

        [Test]
        public void AutomatedAdjustmentController_DoesNotTouchYNearToleranceWhenOnlyAzimuthRemains() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(5.0 / 60.0, 0.05 / 60.0);

            var firstProbe = controller.CreatePlan();
            controller.NoteSuccessfulExecution(firstProbe);
            controller.UpdateObservation(7.0 / 60.0, 0.05 / 60.0);

            var committedProbe = controller.CreatePlan();

            committedProbe.HasMovement.Should().BeTrue();
            committedProbe.XMagnitude.Should().Be(8.0);
            committedProbe.YMagnitude.Should().Be(0);
        }

        [Test]
        public void AutomatedAdjustmentController_StopsAfterRepeatedSubNoiseProbeResponses() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(0.5, 0);

            var firstXProbe = controller.CreatePlan();
            firstXProbe.XMagnitude.Should().Be(8.0);
            controller.NoteSuccessfulExecution(firstXProbe);
            controller.UpdateObservation(0.5 + (0.1 / 60.0), 0);

            var secondXProbe = controller.CreatePlan();
            secondXProbe.XMagnitude.Should().Be(8.0);
            controller.NoteSuccessfulExecution(secondXProbe);
            controller.UpdateObservation(0.5 + (0.2 / 60.0), 0);

            var thirdXProbe = controller.CreatePlan();
            thirdXProbe.XMagnitude.Should().Be(8.0);
            controller.NoteSuccessfulExecution(thirdXProbe);
            controller.UpdateObservation(0.5 + (0.3 / 60.0), 0);

            var fourthXProbe = controller.CreatePlan();
            fourthXProbe.XMagnitude.Should().Be(12.0);
            controller.NoteSuccessfulExecution(fourthXProbe);
            controller.UpdateObservation(0.5 + (0.4 / 60.0), 0);

            var fifthXProbe = controller.CreatePlan();
            fifthXProbe.XMagnitude.Should().Be(18.0);
            controller.NoteSuccessfulExecution(fifthXProbe);
            controller.UpdateObservation(0.5 + (0.5 / 60.0), 0);

            var sixthXProbe = controller.CreatePlan();
            sixthXProbe.XMagnitude.Should().Be(20.0);
            controller.NoteSuccessfulExecution(sixthXProbe);
            controller.UpdateObservation(0.5 + (0.6 / 60.0), 0);

            var seventhXProbe = controller.CreatePlan();
            seventhXProbe.XMagnitude.Should().Be(20.0);
            controller.NoteSuccessfulExecution(seventhXProbe);
            controller.UpdateObservation(0.5 + (0.7 / 60.0), 0);

            var plan = controller.CreatePlan();
            plan.HasMovement.Should().BeFalse();
            plan.Reason.Should().Contain("refusing to probe altitude");
        }

        [Test]
        public void AutomatedAdjustmentController_ResetsUnsafeModelAfterRepeatedSafeMoveSkips() {
            var controller = new AutomatedAdjustmentController();
            controller.UpdateObservation(0.5, 0.5);

            var xProbe = controller.CreatePlan();
            controller.NoteSuccessfulExecution(xProbe);
            controller.UpdateObservation(0.5 + 0.1 * xProbe.XMagnitude, 0.5);

            var yProbe = controller.CreatePlan();
            controller.NoteSuccessfulExecution(yProbe);
            controller.UpdateObservation(0.5 + 0.1 * xProbe.XMagnitude, 0.5 + 0.1 * yProbe.YMagnitude);

            controller.HasResponseModel.Should().BeTrue();
            controller.UpdateObservation(0, 0);

            var firstPlan = controller.CreatePlan();

            firstPlan.HasMovement.Should().BeFalse();
            firstPlan.IsProbe.Should().BeFalse();
            firstPlan.Reason.Should().Contain("safe improvement");

            var secondPlan = controller.CreatePlan();

            secondPlan.HasMovement.Should().BeFalse();
            secondPlan.IsProbe.Should().BeFalse();
            secondPlan.Reason.Should().Contain("probe resolution");
            controller.SampleCount.Should().Be(0);
        }

        [Test]
        public void AutomatedAdjustmentController_ContinuesTinyDominantAzimuthImprovementBeforeLearning() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(10.0 / 60.0, 0);

            var tinyXProbe = new AutomatedAdjustmentPlan(4, 0, true, "Synthetic tiny azimuth probe");
            controller.NoteSuccessfulExecution(tinyXProbe);
            controller.UpdateObservation(9.5 / 60.0, 0);

            controller.SampleCount.Should().Be(0);
            var nextProbe = controller.CreatePlan();
            nextProbe.XMagnitude.Should().Be(8.0);
            nextProbe.YMagnitude.Should().Be(0);
        }

        [Test]
        public void AutomatedAdjustmentController_RemembersConfirmedXDirectionAfterModelReset() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(40.0 / 60.0, 0);

            var confirmingProbe = new AutomatedAdjustmentPlan(-24, 0, true, "Synthetic trusted azimuth probe");
            controller.NoteSuccessfulExecution(confirmingProbe);
            controller.UpdateObservation(30.0 / 60.0, 0);
            controller.SampleCount.Should().Be(1);

            var outlierProbe = new AutomatedAdjustmentPlan(0, 2, true, "Synthetic altitude outlier");
            controller.NoteSuccessfulExecution(outlierProbe);
            controller.UpdateObservation(30.0 / 60.0, 50.0 / 60.0);
            controller.SampleCount.Should().Be(0);

            var nextProbe = controller.CreatePlan();
            nextProbe.XMagnitude.Should().Be(-8.0);
            nextProbe.YMagnitude.Should().Be(0);
        }

        [Test]
        public void AutomatedAdjustmentController_LimitsEveryNearTargetXCorrectionToFourUnits() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(10.0 / 60.0, 0);

            var confirmingProbe = new AutomatedAdjustmentPlan(24, 0, true, "Synthetic trusted azimuth probe");
            controller.NoteSuccessfulExecution(confirmingProbe);
            controller.UpdateObservation(5.5 / 60.0, 0);

            controller.UpdateObservation(3.0 / 60.0, 0);
            var correction = controller.CreatePlan();

            correction.HasMovement.Should().BeTrue();
            correction.XMagnitude.Should().Be(4.0);
            correction.YMagnitude.Should().Be(0);
            correction.Reason.Should().Contain("near-target X limit");
        }

        [Test]
        public void AutomatedAdjustmentController_LimitsEngagementMoveAfterNearTargetMoveIsSwallowed() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.SeedUpasAzimuthResponseMemory(0.25 / 60.0);
            controller.UpdateObservation(1.0 / 60.0, 0);

            var rememberedMove = controller.CreatePlan();
            rememberedMove.HasMovement.Should().BeTrue();
            controller.NoteSuccessfulExecution(rememberedMove);
            controller.UpdateObservation(1.0 / 60.0, 0);

            var engagementMove = controller.CreatePlan();

            engagementMove.HasMovement.Should().BeTrue();
            Math.Abs(engagementMove.XMagnitude).Should().Be(4.0);
            engagementMove.YMagnitude.Should().Be(0);
            engagementMove.Reason.Should().Contain("near-target X limit");
        }

        [Test]
        public void AutomatedAdjustmentController_DoesNotOvershootRemainingEngagementClearance() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(-3.0 / 60.0, 0);

            var firstMove = controller.CreatePlan();
            firstMove.XMagnitude.Should().Be(4.0);
            controller.NoteSuccessfulExecution(firstMove);
            controller.UpdateObservation(-3.3 / 60.0, 0);

            var secondMove = controller.CreatePlan();
            secondMove.XMagnitude.Should().Be(8.0);
            controller.NoteSuccessfulExecution(secondMove);
            controller.UpdateObservation(-3.3 / 60.0, 0);

            var thirdMove = controller.CreatePlan();
            thirdMove.XMagnitude.Should().Be(8.0);
            controller.NoteSuccessfulExecution(thirdMove);
            controller.UpdateObservation(-3.3 / 60.0, 0);

            var finalClearanceMove = controller.CreatePlan();

            finalClearanceMove.XMagnitude.Should().Be(4.0);
            finalClearanceMove.YMagnitude.Should().Be(0);
            finalClearanceMove.Reason.Should().Contain("20/24");
        }

        [Test]
        public void AutomatedAdjustmentController_LimitsNearTargetXComponentOfCombinedMove() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(10.0, 10.0);

            var xSample = new AutomatedAdjustmentPlan(24, 0, true, "Synthetic X response");
            controller.NoteSuccessfulExecution(xSample);
            controller.UpdateObservation(6.0, 6.4);

            var ySample = new AutomatedAdjustmentPlan(0, 8, true, "Synthetic Y response");
            controller.NoteSuccessfulExecution(ySample);
            controller.UpdateObservation(4.8, 5.0666666667);

            controller.UpdateObservation(2.5 / 60.0, 20.0 / 60.0);
            var debouncedReversal = controller.CreatePlan();
            debouncedReversal.HasMovement.Should().BeFalse();
            var correction = controller.CreatePlan();

            correction.HasMovement.Should().BeTrue();
            Math.Abs(correction.XMagnitude).Should().Be(4.0);
            Math.Abs(correction.YMagnitude).Should().BeGreaterThan(0);
            correction.Reason.Should().Contain("near-target X limit");
        }

        [Test]
        public void AutomatedAdjustmentController_UnlocksLargerYMoveOnlyAfterThreeConsistentResponses() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(0.25, 2.0);

            var xSample = new AutomatedAdjustmentPlan(8, 0, true, "Synthetic X response");
            controller.NoteSuccessfulExecution(xSample);
            controller.UpdateObservation(0.17, 2.0);

            var firstYSample = new AutomatedAdjustmentPlan(0, 8, true, "Synthetic first Y response");
            controller.NoteSuccessfulExecution(firstYSample);
            controller.UpdateObservation(0.17, 1.84);

            var conservativePlan = controller.CreatePlan();
            conservativePlan.YMagnitude.Should().Be(8.0);
            controller.NoteSuccessfulExecution(conservativePlan);
            controller.UpdateObservation(0.17, 1.68);

            var secondConservativePlan = controller.CreatePlan();
            secondConservativePlan.YMagnitude.Should().Be(8.0);
            controller.NoteSuccessfulExecution(secondConservativePlan);
            controller.UpdateObservation(0.17, 1.52);

            var confirmedPlan = controller.CreatePlan();

            confirmedPlan.YMagnitude.Should().Be(16.0);
            confirmedPlan.XMagnitude.Should().Be(0);
        }

        [Test]
        public void BacklashCompensationPlanner_PositiveDirection_PreloadsPositiveOnly() {
            var preload = BacklashCompensationPlanner.CreatePreloadMove(3f, LastDirection.Positive);

            preload.Should().Be(3f);
        }

        [Test]
        public void BacklashCompensationPlanner_NegativeDirection_PreloadsNegativeOnly() {
            var preload = BacklashCompensationPlanner.CreatePreloadMove(3f, LastDirection.Negative);

            preload.Should().Be(-3f);
        }

        [Test]
        public void AutomatedAdjustmentController_DebouncesSingleXDirectionReversal() {
            var controller = new AutomatedAdjustmentController();
            controller.UpdateObservation(0.5, 0.25);

            var xProbe = controller.CreatePlan();
            controller.NoteSuccessfulExecution(xProbe);
            controller.UpdateObservation(0.5 - 0.1 * xProbe.XMagnitude, 0.25);

            var yProbe = controller.CreatePlan();
            controller.NoteSuccessfulExecution(yProbe);
            controller.UpdateObservation(0.5 - 0.1 * xProbe.XMagnitude, 0.25 - 0.1 * yProbe.YMagnitude);

            controller.UpdateObservation(-0.2, 0.25);
            var firstReversal = controller.CreatePlan();

            firstReversal.HasMovement.Should().BeFalse();
            firstReversal.Reason.Should().Contain("Debouncing");

            var confirmedReversal = controller.CreatePlan();
            confirmedReversal.HasMovement.Should().BeTrue();
            confirmedReversal.XMagnitude.Should().BeLessThan(0);
        }
        private static ClosedLoopResult RunClosedLoop(AutomatedAdjustmentController controller,
                                                      double[,] plant,
                                                      double initialAzimuthErrorDegrees,
                                                      double initialAltitudeErrorDegrees,
                                                      int maxIterations) {
            var azimuthError = initialAzimuthErrorDegrees;
            var altitudeError = initialAltitudeErrorDegrees;

            controller.UpdateObservation(azimuthError, altitudeError);

            for (var iteration = 0; iteration < maxIterations; iteration++) {
                var plan = controller.CreatePlan();
                if (!plan.HasMovement) {
                    if (plan.Reason?.Contains("Debouncing") == true) {
                        controller.UpdateObservation(azimuthError, altitudeError);
                        continue;
                    }

                    break;
                }

                azimuthError += plant[0, 0] * plan.XMagnitude + plant[0, 1] * plan.YMagnitude;
                altitudeError += plant[1, 0] * plan.XMagnitude + plant[1, 1] * plan.YMagnitude;

                controller.NoteSuccessfulExecution(plan);
                controller.UpdateObservation(azimuthError, altitudeError);

                var totalError = Math.Sqrt(azimuthError * azimuthError + altitudeError * altitudeError);
                if (totalError < 0.02) {
                    return new ClosedLoopResult(totalError, iteration + 1);
                }
            }

            return new ClosedLoopResult(Math.Sqrt(azimuthError * azimuthError + altitudeError * altitudeError), maxIterations);
        }

        private sealed class ClosedLoopResult {
            public ClosedLoopResult(double finalErrorDegrees, int iterations) {
                FinalErrorDegrees = finalErrorDegrees;
                Iterations = iterations;
            }

            public double FinalErrorDegrees { get; }
            public int Iterations { get; }
        }

        [Test]
        public void AutomatedAdjustmentController_BlocksXMoveWhenAzimuthTravelGuardIsUnconfirmed() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true) {
                AzimuthTravelGuardEnabled = true,
                AzimuthTravelGuardConfirmed = false,
                AzimuthTravelLimitDegrees = 2,
                AzimuthDegreesPerXUnit = 0.025,
            };
            controller.UpdateObservation(30.0 / 60.0, 0);

            var plan = controller.CreatePlan();

            plan.HasMovement.Should().BeFalse();
            plan.Reason.Should().Contain("visual marker confirmation");
        }

        [Test]
        public void AutomatedAdjustmentController_BlocksXMoveWhenAzimuthTravelBudgetWouldBeExceeded() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true) {
                AzimuthTravelGuardEnabled = true,
                AzimuthTravelGuardConfirmed = true,
                AzimuthTravelLimitDegrees = 0.1,
                AzimuthDegreesPerXUnit = 0.025,
            };
            controller.UpdateObservation(30.0 / 60.0, 0);

            var plan = controller.CreatePlan();

            plan.HasMovement.Should().BeFalse();
            plan.Reason.Should().Contain("travel guard refused");
        }

        [Test]
        public void AutomatedAdjustmentController_ConsumesAzimuthTravelBudgetAfterSuccessfulXExecution() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true) {
                AzimuthTravelGuardEnabled = true,
                AzimuthTravelGuardConfirmed = true,
                AzimuthTravelLimitDegrees = 0.41,
                AzimuthDegreesPerXUnit = 0.025,
            };
            controller.UpdateObservation(30.0 / 60.0, 0);

            var firstPlan = controller.CreatePlan();
            firstPlan.XMagnitude.Should().Be(8.0);
            controller.NoteSuccessfulExecution(firstPlan);
            controller.AzimuthTravelUsedDegrees.Should().BeApproximately(0.2, 0.0001);
            controller.UpdateObservation(31.0 / 60.0, 0);

            var secondPlan = controller.CreatePlan();
            secondPlan.XMagnitude.Should().Be(8.0);
            controller.NoteSuccessfulExecution(secondPlan);
            controller.AzimuthTravelUsedDegrees.Should().BeApproximately(0.4, 0.0001);
            controller.UpdateObservation(32.0 / 60.0, 0);

            var blockedPlan = controller.CreatePlan();

            blockedPlan.HasMovement.Should().BeFalse();
            blockedPlan.Reason.Should().Contain("travel guard refused");
        }

        [Test]
        public void AutomatedAdjustmentController_ExternalPreSeatConsumesAzimuthTravelBudget() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true) {
                AzimuthTravelGuardEnabled = true,
                AzimuthTravelGuardConfirmed = true,
                AzimuthTravelLimitDegrees = 1.0,
                AzimuthDegreesPerXUnit = 0.025,
            };

            controller.CanExecuteAzimuthTravel(30, out _).Should().BeTrue();
            controller.NoteExternalAzimuthTravel(30, "test pre-seat");

            controller.AzimuthTravelUsedDegrees.Should().BeApproximately(0.75, 0.0001);
            controller.CanExecuteAzimuthTravel(11, out var reason).Should().BeFalse();
            reason.Should().Contain("travel guard refused");
        }

        [Test]
        public void AutomatedAdjustmentController_BlocksXMoveBeyondConservativeAzimuthEnvelope() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.ConfigureAzimuthTravelGuard(true, true, 5.0, -5.4, 5.4, 0.025, 1);

            controller.CanExecuteAzimuthTravel(20, out var reason).Should().BeFalse();

            reason.Should().Contain("conservative physical interval");
        }

        [Test]
        public void AutomatedAdjustmentController_TracksSignedAzimuthIntervalAfterExecutedMove() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.ConfigureAzimuthTravelGuard(true, true, 0, -5.4, 5.4, 0.025, 1);

            controller.NoteExternalAzimuthTravel(20, "test first-run X identification");

            controller.AzimuthPossibleMinimumDegrees.Should().BeApproximately(0, 0.0001);
            controller.AzimuthPossibleMaximumDegrees.Should().BeApproximately(0.5, 0.0001);
        }

        [Test]
        public void AutomatedAdjustmentController_BlocksYMoveUntilPhysicalAltitudeMarkerIsConfirmed() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.ConfigureAltitudeTravelGuard(true, false, 0, -5, 5, 0.022, 1);

            controller.CanExecuteAltitudeTravel(8, out var reason).Should().BeFalse();
            reason.Should().Contain("physical ALT marker confirmation");
        }

        [Test]
        public void AutomatedAdjustmentController_BlocksYMoveBeyondConservativeAltitudeEnvelope() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.ConfigureAltitudeTravelGuard(true, true, 4.9, -5, 5, 0.022, 1);

            controller.CanExecuteAltitudeTravel(5, out var reason).Should().BeFalse();
            reason.Should().Contain("outside");
        }

        [Test]
        public void AutomatedAdjustmentController_AppliesAltitudeDirectionMultiplier() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.ConfigureAltitudeTravelGuard(true, true, 4.9, -5, 5, 0.022, -1);

            controller.CanExecuteAltitudeTravel(5, out _).Should().BeTrue();
            controller.CanExecuteAltitudeTravel(-5, out var reason).Should().BeFalse();
            reason.Should().Contain("outside");
        }

        [Test]
        public void AutomatedAdjustmentController_SuccessfulYMoveExpandsConservativeAltitudeInterval() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.ConfigureAltitudeTravelGuard(true, true, 0, -5, 5, 0.022, 1);
            controller.UpdateObservation(0, 1);

            controller.NoteSuccessfulExecution(new AutomatedAdjustmentPlan(0, 8, true, "test Y move"));

            controller.AltitudePossibleMinimumDegrees.Should().BeApproximately(0, 0.0001);
            controller.AltitudePossibleMaximumDegrees.Should().BeApproximately(0.176, 0.0001);
        }

        [Test]
        public void AutomatedAdjustmentController_FailedYMoveStillExpandsConservativeAltitudeInterval() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.ConfigureAltitudeTravelGuard(true, true, 0, -5, 5, 0.022, 1);

            controller.NoteFailedExecution(new AutomatedAdjustmentPlan(0, -8, true, "failed Y move"));

            controller.AltitudePossibleMinimumDegrees.Should().BeApproximately(-0.176, 0.0001);
            controller.AltitudePossibleMaximumDegrees.Should().BeApproximately(0, 0.0001);
        }

        [Test]
        public void AutomatedAdjustmentController_ReversalWidensRatherThanRewindsAltitudeInterval() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.ConfigureAltitudeTravelGuard(true, true, 0, -5, 5, 0.022, 1);
            controller.UpdateObservation(0, 1);

            controller.NoteSuccessfulExecution(new AutomatedAdjustmentPlan(0, 8, true, "positive Y move"));
            controller.NoteSuccessfulExecution(new AutomatedAdjustmentPlan(0, -8, true, "negative Y move"));

            controller.AltitudePossibleMinimumDegrees.Should().BeApproximately(-0.176, 0.0001);
            controller.AltitudePossibleMaximumDegrees.Should().BeApproximately(0.176, 0.0001);
        }

        [Test]
        public void AutomatedAdjustmentController_RepeatedIdenticalConfigurationPreservesAltitudeInterval() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.ConfigureAltitudeTravelGuard(true, true, 0, -5, 5, 0.022, 1);
            controller.UpdateObservation(0, 1);
            controller.NoteSuccessfulExecution(new AutomatedAdjustmentPlan(0, 8, true, "Y move"));

            controller.ConfigureAltitudeTravelGuard(true, true, 0, -5, 5, 0.022, 1);

            controller.AltitudePossibleMaximumDegrees.Should().BeApproximately(0.176, 0.0001);
        }

        [Test]
        public void AutomatedAdjustmentController_CreatesBoundedFirstMoveForFullEnvelopeConvergence() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.ConfigureCalibratedDirectFullTravelRoute(
                enabled: true,
                operatorConfirmed: true,
                azimuthStartingPositionDegrees: 0,
                azimuthMinimumDegrees: -5.4,
                azimuthMaximumDegrees: 5.4,
                altitudeStartingPositionDegrees: 0,
                altitudeMinimumDegrees: -5.4,
                altitudeMaximumDegrees: 5.4,
                azimuthDeltaPerXUnitDegrees: 0.05,
                azimuthDeltaPerYUnitDegrees: 0,
                altitudeDeltaPerXUnitDegrees: 0,
                altitudeDeltaPerYUnitDegrees: 0.05,
                maximumXUnitsPerMove: 81,
                maximumYUnitsPerMove: 81,
                physicalAzimuthDegreesPerXUnit: 0.05,
                physicalAltitudeDegreesPerYUnit: 0.05);
            controller.UpdateObservation(5, -5);

            var plan = controller.CreatePlan();

            plan.HasMovement.Should().BeTrue(plan.Reason);
            plan.Reason.Should().Contain("Calibrated direct full-travel correction");
            plan.XMagnitude.Should().BeLessOrEqualTo(81);
            plan.YMagnitude.Should().BeLessOrEqualTo(81);
            plan.ExpectedAzimuthDeltaDegrees.Should().BeNegative();
            plan.ExpectedAltitudeDeltaDegrees.Should().BePositive();
        }

        [Test]
        public void AutomatedAdjustmentController_RejectsSingularDirectFullTravelProfile() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.ConfigureCalibratedDirectFullTravelRoute(
                enabled: true,
                operatorConfirmed: true,
                azimuthStartingPositionDegrees: 0,
                azimuthMinimumDegrees: -5.4,
                azimuthMaximumDegrees: 5.4,
                altitudeStartingPositionDegrees: 0,
                altitudeMinimumDegrees: -5.4,
                altitudeMaximumDegrees: 5.4,
                azimuthDeltaPerXUnitDegrees: 0.05,
                azimuthDeltaPerYUnitDegrees: 0.10,
                altitudeDeltaPerXUnitDegrees: 0.025,
                altitudeDeltaPerYUnitDegrees: 0.05,
                maximumXUnitsPerMove: 81,
                maximumYUnitsPerMove: 81,
                physicalAzimuthDegreesPerXUnit: 0.05,
                physicalAltitudeDegreesPerYUnit: 0.05);
            controller.UpdateObservation(5, -5);

            var plan = controller.CreatePlan();

            plan.Reason.Should().NotContain("Calibrated direct full-travel correction");
        }

        [Test]
        public void AutomatedAdjustmentController_LatchesMotionOffWhenClampLimitedRecoveryFreshResponseDisagrees() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.ConfigureCalibratedDirectFullTravelRoute(
                enabled: true,
                operatorConfirmed: true,
                azimuthStartingPositionDegrees: 0,
                azimuthMinimumDegrees: -5.4,
                azimuthMaximumDegrees: 5.4,
                altitudeStartingPositionDegrees: 0,
                altitudeMinimumDegrees: -5.4,
                altitudeMaximumDegrees: 5.4,
                azimuthDeltaPerXUnitDegrees: 2.0 / 60.0,
                azimuthDeltaPerYUnitDegrees: 0,
                altitudeDeltaPerXUnitDegrees: 0,
                altitudeDeltaPerYUnitDegrees: 2.0 / 60.0,
                maximumXUnitsPerMove: 81,
                maximumYUnitsPerMove: 75,
                physicalAzimuthDegreesPerXUnit: 0.02,
                physicalAltitudeDegreesPerYUnit: 0.02,
                clampLimitedRecoveryEnabled: true,
                relativeResponseUncertainty: 0.05);
            controller.UpdateObservation(324.0 / 60.0, -300.0 / 60.0);

            var plan = controller.CreatePlan();

            plan.IsClampLimitedTravelRecovery.Should().BeTrue();
            plan.Reason.Should().Contain("clamp-limited");
            plan.XMagnitude.Should().BeApproximately(-81, 0.001);
            plan.YMagnitude.Should().BeApproximately(75, 0.001);

            controller.NoteSuccessfulExecution(plan);
            // A growing residual is incompatible with the expected two-axis response.
            controller.UpdateObservation(6.0, -5.5);

            controller.MotionAuthorityAborted.Should().BeTrue();
            controller.MotionAuthorityAbortReason.Should().Contain("clamp-limited recovery");
            controller.CreatePlan().HasMovement.Should().BeFalse();
        }

        [Test]
        public void AutomatedAdjustmentController_BootstrapsYBeforeUsingAnIncompleteCalibratedAzimuthRoute() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.ConfigureCalibratedDirectFullTravelRoute(
                enabled: true,
                operatorConfirmed: true,
                azimuthStartingPositionDegrees: 0,
                azimuthMinimumDegrees: -5.4,
                azimuthMaximumDegrees: 5.4,
                altitudeStartingPositionDegrees: 0,
                altitudeMinimumDegrees: -5.4,
                altitudeMaximumDegrees: 5.4,
                azimuthDeltaPerXUnitDegrees: 0.05,
                azimuthDeltaPerYUnitDegrees: 0,
                altitudeDeltaPerXUnitDegrees: 0,
                altitudeDeltaPerYUnitDegrees: 0,
                maximumXUnitsPerMove: 81,
                maximumYUnitsPerMove: 81,
                physicalAzimuthDegreesPerXUnit: 0.05,
                physicalAltitudeDegreesPerYUnit: 0.05);
            controller.UpdateObservation(5, -1);

            var plan = controller.CreatePlan();

            plan.HasMovement.Should().BeTrue();
            plan.IsProbe.Should().BeTrue();
            plan.Reason.Should().Contain("calibrated azimuth bootstrap");
            plan.XMagnitude.Should().Be(0);
            plan.YMagnitude.Should().Be(TppaDirectBootstrapRouteQualification.BootstrapYProbeUnits);
        }

        [Test]
        public void AutomatedAdjustmentController_UsesCalibratedXColumnToBootstrapOneAltitudeProbe() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.ConfigureCalibratedDirectFullTravelRoute(
                enabled: true,
                operatorConfirmed: true,
                azimuthStartingPositionDegrees: 0,
                azimuthMinimumDegrees: -5.4,
                azimuthMaximumDegrees: 5.4,
                altitudeStartingPositionDegrees: 0,
                altitudeMinimumDegrees: -5.4,
                altitudeMaximumDegrees: 5.4,
                azimuthDeltaPerXUnitDegrees: 0.05,
                azimuthDeltaPerYUnitDegrees: 0,
                altitudeDeltaPerXUnitDegrees: 0,
                altitudeDeltaPerYUnitDegrees: 0,
                maximumXUnitsPerMove: 81,
                maximumYUnitsPerMove: 81,
                physicalAzimuthDegreesPerXUnit: 0.05,
                physicalAltitudeDegreesPerYUnit: 0.05);
            controller.UpdateObservation(5, -1);

            var probe = controller.CreatePlan();

            probe.IsProbe.Should().BeTrue();
            probe.XMagnitude.Should().Be(0);
            probe.YMagnitude.Should().Be(TppaDirectBootstrapRouteQualification.BootstrapYProbeUnits);
            probe.Reason.Should().Contain("calibrated azimuth bootstrap");

            controller.NoteSuccessfulExecution(probe);
            controller.UpdateObservation(5.02, -0.9);

            controller.HasResponseModel.Should().BeTrue();
            var correction = controller.CreatePlan();
            correction.HasMovement.Should().BeTrue();
            correction.Reason.Should().NotContain("azimuth-only");
        }

        [Test]
        public void AutomatedAdjustmentController_UsesObservableYProbeAfterUnresponsiveXWhenAltitudeDominates() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.UpdateObservation(3.0 / 60.0, 30.0 / 60.0);

            var xProbe = controller.CreatePlan();
            xProbe.XMagnitude.Should().Be(4);
            xProbe.YMagnitude.Should().Be(0);
            controller.NoteSuccessfulExecution(xProbe);

            // The immediate fresh determination did not resolve a sky response from X.
            controller.UpdateObservation(3.0 / 60.0, 30.0 / 60.0);

            var yProbe = controller.CreatePlan();
            yProbe.XMagnitude.Should().Be(0);
            yProbe.YMagnitude.Should().Be(TppaDirectBootstrapRouteQualification.BootstrapYProbeUnits);
            yProbe.Reason.Should().Contain("after an unresponsive azimuth probe");
        }

        [Test]
        public void AutomatedAdjustmentController_FirstRunBootstrapMeasuresXThenYBeforeCorrection() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.ConfigureCalibratedDirectFullTravelRoute(
                enabled: true, operatorConfirmed: true,
                azimuthStartingPositionDegrees: 0, azimuthMinimumDegrees: -5.4, azimuthMaximumDegrees: 5.4,
                altitudeStartingPositionDegrees: 0, altitudeMinimumDegrees: -5.4, altitudeMaximumDegrees: 5.4,
                azimuthDeltaPerXUnitDegrees: 0, azimuthDeltaPerYUnitDegrees: 0,
                altitudeDeltaPerXUnitDegrees: 0, altitudeDeltaPerYUnitDegrees: 0,
                maximumXUnitsPerMove: 0, maximumYUnitsPerMove: 0,
                physicalAzimuthDegreesPerXUnit: 0.025, physicalAltitudeDegreesPerYUnit: 0.022);
            controller.SeedXSeating(1);
            controller.ConfigureFirstRunTwoAxisBootstrap(true);
            controller.UpdateObservation(1.0, 1.0);
            controller.HasPendingFirstRunTwoAxisBootstrapProbe.Should().BeTrue();

            var x = controller.CreatePlan();
            x.XMagnitude.Should().Be(TppaFirstRunBootstrapRouteQualification.IdentificationProbeUnits);
            x.YMagnitude.Should().Be(0);
            controller.NoteSuccessfulExecution(x);
            controller.UpdateObservation(0.8, 1.0);
            controller.HasPendingFirstRunTwoAxisBootstrapProbe.Should().BeTrue();

            var y = controller.CreatePlan();
            y.XMagnitude.Should().Be(0);
            y.YMagnitude.Should().Be(TppaFirstRunBootstrapRouteQualification.IdentificationProbeUnits);
            controller.NoteSuccessfulExecution(y);
            controller.UpdateObservation(0.8, 0.8);

            controller.HasPendingFirstRunTwoAxisBootstrapProbe.Should().BeFalse();
            controller.HasResponseModel.Should().BeTrue();
            controller.CreatePlan().Reason.Should().NotContain("First-run bounded");
        }

        [Test]
        public void AutomatedAdjustmentController_FirstRunWeakXResponseRetriesXInsteadOfProbingY() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.ConfigureFirstRunTwoAxisBootstrap(true);
            controller.SeedXSeating(1);
            controller.UpdateObservation(1.0, 1.0);

            var firstX = controller.CreatePlan();
            controller.NoteSuccessfulExecution(firstX);
            controller.UpdateObservation(0.99, 1.0);

            controller.SampleCount.Should().Be(0,
                "an above-jitter but implausibly weak per-unit response must not qualify the X column");
            controller.HasPendingFirstRunTwoAxisBootstrapProbe.Should().BeTrue();
            var retryX = controller.CreatePlan();
            retryX.XMagnitude.Should().Be(TppaFirstRunBootstrapRouteQualification.IdentificationProbeUnits);
            retryX.YMagnitude.Should().Be(0);
            retryX.Reason.Should().Contain("X response identification");
        }

        [Test]
        public void AutomatedAdjustmentController_FirstRunWeakXResponseExhaustionStopsWithSpecificReason() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.ConfigureFirstRunTwoAxisBootstrap(true);
            controller.SeedXSeating(1);
            controller.UpdateObservation(1.0, 1.0);

            for (var attempt = 0; attempt < 5; attempt++) {
                var x = controller.CreatePlan();
                x.HasMovement.Should().BeTrue();
                x.XMagnitude.Should().Be(TppaFirstRunBootstrapRouteQualification.IdentificationProbeUnits);
                x.YMagnitude.Should().Be(0);
                controller.NoteSuccessfulExecution(x);
                controller.UpdateObservation(1.0 - ((attempt + 1) * 0.01), 1.0);
            }

            var exhausted = controller.CreatePlan();
            exhausted.HasMovement.Should().BeFalse();
            exhausted.Reason.Should().Contain("First-run azimuth response");
            exhausted.Reason.Should().Contain("after 5 bounded probes");
            exhausted.Reason.Should().Contain("X backlash or mechanics");
        }

        [Test]
        public void AutomatedAdjustmentController_FirstRunXRetryCrossesDeadbandThenMeasuresY() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.ConfigureFirstRunTwoAxisBootstrap(true);
            controller.SeedXSeating(1);
            controller.UpdateObservation(1.0, 1.0);

            var weakX = controller.CreatePlan();
            controller.NoteSuccessfulExecution(weakX);
            controller.UpdateObservation(0.99, 1.0);

            var engagedX = controller.CreatePlan();
            controller.NoteSuccessfulExecution(engagedX);
            controller.UpdateObservation(0.49, 1.0);

            controller.SampleCount.Should().Be(1);
            var y = controller.CreatePlan();
            y.XMagnitude.Should().Be(0);
            y.YMagnitude.Should().Be(TppaFirstRunBootstrapRouteQualification.IdentificationProbeUnits);
            y.Reason.Should().Contain("Y response identification");
        }

        [Test]
        public void AutomatedAdjustmentController_FirstRunBootstrapConvergesLastFieldVectorWithinMoveCeiling() {
            const double azimuthResponsePerXUnitDegrees = -0.025;
            const double altitudeResponsePerYUnitDegrees = -0.022;
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.ConfigureCalibratedDirectFullTravelRoute(
                enabled: true, operatorConfirmed: true,
                azimuthStartingPositionDegrees: 0, azimuthMinimumDegrees: -5.4, azimuthMaximumDegrees: 5.4,
                altitudeStartingPositionDegrees: 0, altitudeMinimumDegrees: -5.4, altitudeMaximumDegrees: 5.4,
                azimuthDeltaPerXUnitDegrees: 0, azimuthDeltaPerYUnitDegrees: 0,
                altitudeDeltaPerXUnitDegrees: 0, altitudeDeltaPerYUnitDegrees: 0,
                maximumXUnitsPerMove: 0, maximumYUnitsPerMove: 0,
                physicalAzimuthDegreesPerXUnit: 0.025, physicalAltitudeDegreesPerYUnit: 0.022);
            controller.SeedXSeating(1);
            controller.ConfigureFirstRunTwoAxisBootstrap(true);

            var azimuthErrorDegrees = -34.502364573021396 / 60.0;
            var altitudeErrorDegrees = 38.22095542503256 / 60.0;
            var azimuthPositionDegrees = 0.0;
            var altitudePositionDegrees = 0.0;
            var moveCount = 0;
            var sawConditionedCoarseCorrection = false;
            controller.UpdateObservation(azimuthErrorDegrees, altitudeErrorDegrees);

            while (Math.Sqrt(
                       azimuthErrorDegrees * azimuthErrorDegrees
                       + altitudeErrorDegrees * altitudeErrorDegrees) * 60.0 > 3.0
                   && moveCount < TppaFastAlignmentExecutionBudget.MaximumFreshFeedbackMoves) {
                var plan = controller.CreatePlan();
                plan.HasMovement.Should().BeTrue(plan.Reason);
                if (plan.Reason.StartsWith("Session-local conditioned coarse correction")) {
                    if (!sawConditionedCoarseCorrection) {
                        Math.Max(Math.Abs(plan.XMagnitude), Math.Abs(plan.YMagnitude)).Should().BeGreaterThan(8.0);
                    }
                    sawConditionedCoarseCorrection = true;
                    Math.Abs(plan.XMagnitude).Should().BeLessOrEqualTo(
                        AutomatedAdjustmentController.FirstRunCalibratedMaximumUnitsPerMove);
                    Math.Abs(plan.YMagnitude).Should().BeLessOrEqualTo(
                        AutomatedAdjustmentController.FirstRunCalibratedMaximumUnitsPerMove);
                }

                controller.NoteSuccessfulExecution(plan);
                azimuthErrorDegrees += plan.XMagnitude * azimuthResponsePerXUnitDegrees;
                altitudeErrorDegrees += plan.YMagnitude * altitudeResponsePerYUnitDegrees;
                azimuthPositionDegrees += plan.XMagnitude * 0.025;
                altitudePositionDegrees += plan.YMagnitude * 0.022;
                controller.UpdateObservation(azimuthErrorDegrees, altitudeErrorDegrees);
                moveCount++;
            }

            var finalTotalMinutes = Math.Sqrt(
                azimuthErrorDegrees * azimuthErrorDegrees
                + altitudeErrorDegrees * altitudeErrorDegrees) * 60.0;
            finalTotalMinutes.Should().BeLessThanOrEqualTo(3.0);
            sawConditionedCoarseCorrection.Should().BeTrue();
            moveCount.Should().BeLessThanOrEqualTo(
                TppaFastAlignmentExecutionBudget.MaximumFreshFeedbackMoves);
            azimuthPositionDegrees.Should().BeInRange(-5.4, 5.4);
            altitudePositionDegrees.Should().BeInRange(-5.4, 5.4);
        }

        [TestCase(-34.502364573021396, 38.22095542503256, -0.025, -0.022, 0.000, 0.000)]
        [TestCase(34.5, -38.2, -0.025, -0.022, 0.000, 0.000)]
        [TestCase(-22.0, -18.0, -0.018, -0.030, 0.000, 0.000)]
        [TestCase(48.0, 26.0, -0.030, -0.018, 0.002, -0.002)]
        [TestCase(300.0, 300.0, -0.025, -0.022, 0.000, 0.000)]
        [TestCase(300.0, -300.0, -0.025, -0.022, 0.000, 0.000)]
        [TestCase(-300.0, 300.0, -0.025, -0.022, 0.000, 0.000)]
        [TestCase(-300.0, -300.0, -0.025, -0.022, 0.000, 0.000)]
        public void AutomatedAdjustmentController_FirstRunConvergenceMatrixStaysBounded(
                double initialAzimuthMinutes,
                double initialAltitudeMinutes,
                double azimuthResponsePerXUnitDegrees,
                double altitudeResponsePerYUnitDegrees,
                double azimuthResponsePerYUnitDegrees,
                double altitudeResponsePerXUnitDegrees) {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.ConfigureCalibratedDirectFullTravelRoute(
                enabled: true, operatorConfirmed: true,
                azimuthStartingPositionDegrees: 0, azimuthMinimumDegrees: -5.4, azimuthMaximumDegrees: 5.4,
                altitudeStartingPositionDegrees: 0, altitudeMinimumDegrees: -5.4, altitudeMaximumDegrees: 5.4,
                azimuthDeltaPerXUnitDegrees: 0, azimuthDeltaPerYUnitDegrees: 0,
                altitudeDeltaPerXUnitDegrees: 0, altitudeDeltaPerYUnitDegrees: 0,
                maximumXUnitsPerMove: 0, maximumYUnitsPerMove: 0,
                physicalAzimuthDegreesPerXUnit: Math.Abs(azimuthResponsePerXUnitDegrees),
                physicalAltitudeDegreesPerYUnit: Math.Abs(altitudeResponsePerYUnitDegrees));
            controller.SeedXSeating(1);
            controller.ConfigureFirstRunTwoAxisBootstrap(true);

            var azimuthErrorDegrees = initialAzimuthMinutes / 60.0;
            var altitudeErrorDegrees = initialAltitudeMinutes / 60.0;
            var azimuthPositionDegrees = 0.0;
            var altitudePositionDegrees = 0.0;
            var moveCount = 0;
            controller.UpdateObservation(azimuthErrorDegrees, altitudeErrorDegrees);

            while (Math.Sqrt(
                       azimuthErrorDegrees * azimuthErrorDegrees
                       + altitudeErrorDegrees * altitudeErrorDegrees) * 60.0 > 3.0
                   && moveCount < TppaFastAlignmentExecutionBudget.MaximumFreshFeedbackMoves) {
                var plan = controller.CreatePlan();
                plan.HasMovement.Should().BeTrue(plan.Reason);

                controller.NoteSuccessfulExecution(plan);
                azimuthErrorDegrees += plan.XMagnitude * azimuthResponsePerXUnitDegrees
                    + plan.YMagnitude * azimuthResponsePerYUnitDegrees;
                altitudeErrorDegrees += plan.XMagnitude * altitudeResponsePerXUnitDegrees
                    + plan.YMagnitude * altitudeResponsePerYUnitDegrees;
                azimuthPositionDegrees += plan.XMagnitude * Math.Abs(azimuthResponsePerXUnitDegrees);
                altitudePositionDegrees += plan.YMagnitude * Math.Abs(altitudeResponsePerYUnitDegrees);
                controller.UpdateObservation(azimuthErrorDegrees, altitudeErrorDegrees);
                moveCount++;
            }

            var finalTotalMinutes = Math.Sqrt(
                azimuthErrorDegrees * azimuthErrorDegrees
                + altitudeErrorDegrees * altitudeErrorDegrees) * 60.0;
            finalTotalMinutes.Should().BeLessThanOrEqualTo(3.0);
            moveCount.Should().BeLessThanOrEqualTo(
                TppaFastAlignmentExecutionBudget.MaximumFreshFeedbackMoves);
            azimuthPositionDegrees.Should().BeInRange(-5.4, 5.4);
            altitudePositionDegrees.Should().BeInRange(-5.4, 5.4);
        }

        [Test]
        public void AutomatedAdjustmentController_ExportsOnlyAQualifiedTwoAxisFirstRunResponseModel() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.ConfigureFirstRunTwoAxisBootstrap(true);
            controller.SeedXSeating(1);
            controller.UpdateObservation(1.0, 1.0);

            var x = controller.CreatePlan();
            controller.NoteSuccessfulExecution(x);
            controller.UpdateObservation(0.8, 1.0);

            controller.TryGetFirstRunResponseModel(out _).Should().BeFalse(
                "one response column cannot identify a two-axis direct route");

            var y = controller.CreatePlan();
            controller.NoteSuccessfulExecution(y);
            controller.UpdateObservation(0.8, 0.8);

            controller.TryGetFirstRunResponseModel(out var model).Should().BeTrue();
            model.AzimuthDeltaPerXUnit.Should().BeApproximately(-0.01, 1e-9);
            model.AzimuthDeltaPerYUnit.Should().BeApproximately(0, 1e-9);
            model.AltitudeDeltaPerXUnit.Should().BeApproximately(0, 1e-9);
            model.AltitudeDeltaPerYUnit.Should().BeApproximately(-0.01, 1e-9);
        }

        [Test]
        public void AutomatedAdjustmentController_PromotesStrongIndependentBootstrapToSessionLocalCoarseCorrection() {
            var controller = CreateBootstrapController();
            controller.UpdateObservation(5, -5);

            controller.MayRequireCalibratedAltitudeBootstrapProbe.Should().BeTrue();
            var probe = controller.CreatePlan();
            probe.IsProbe.Should().BeTrue();
            probe.IsBoundedYBootstrapProbe.Should().BeTrue();
            probe.YMagnitude.Should().Be(TppaDirectBootstrapRouteQualification.BootstrapYProbeUnits);

            controller.NoteSuccessfulExecution(probe);
            controller.UpdateObservation(5, -4.5);

            controller.MayRequireCalibratedAltitudeBootstrapProbe.Should().BeFalse();

            var coarse = controller.CreatePlan();
            coarse.HasMovement.Should().BeTrue();
            coarse.Reason.Should().Be("Session-local conditioned coarse correction");
            Math.Abs(coarse.XMagnitude).Should().BeGreaterThan(8);
            Math.Abs(coarse.YMagnitude).Should().BeGreaterThan(8);
        }

        [Test]
        public void AutomatedAdjustmentController_LatchesOffAllMotionAfterWeakSessionLocalCoarseFeedback() {
            var controller = CreateBootstrapController();
            controller.UpdateObservation(5, -5);
            var probe = controller.CreatePlan();
            controller.NoteSuccessfulExecution(probe);
            controller.UpdateObservation(5, -4.5);

            var coarse = controller.CreatePlan();
            coarse.Reason.Should().Be("Session-local conditioned coarse correction");
            controller.NoteSuccessfulExecution(coarse);
            controller.UpdateObservation(4.9, -4.3);

            controller.MotionAuthorityAborted.Should().BeTrue();
            controller.CreatePlan().HasMovement.Should().BeFalse();
            controller.CreatePlan().Reason.Should().Contain("latched off");

            controller.Reset();
            controller.UpdateObservation(5, -5);
            controller.CreatePlan().HasMovement.Should().BeFalse();
        }

        [Test]
        public void AutomatedAdjustmentController_LatchesOffAllMotionWhenSessionLocalCoarseExecutionIsUnverified() {
            var controller = CreateBootstrapController();
            controller.UpdateObservation(5, -5);
            var probe = controller.CreatePlan();
            controller.NoteSuccessfulExecution(probe);
            controller.UpdateObservation(5, -4.5);

            var coarse = controller.CreatePlan();
            coarse.Reason.Should().Be("Session-local conditioned coarse correction");
            controller.NoteFailedExecution(coarse);

            controller.MotionAuthorityAborted.Should().BeTrue();
            controller.CreatePlan().HasMovement.Should().BeFalse();
        }

        [Test]
        public void AutomatedAdjustmentController_DoesNotPromoteWeakBootstrapYResponseToSessionLocalCoarseCorrection() {
            var controller = CreateBootstrapController();
            controller.UpdateObservation(5, -5);
            var probe = controller.CreatePlan();
            controller.NoteSuccessfulExecution(probe);

            // The 20-unit probe changed the reported altitude by only 3 arcmin.
            // That is deliberately below the six-arcmin coarse-promotion floor.
            controller.UpdateObservation(5, -4.95);

            controller.CreatePlan().Reason.Should().NotBe("Session-local conditioned coarse correction");
        }

        [Test]
        public void AutomatedAdjustmentController_DoesNotPromoteCollinearBootstrapYResponseToSessionLocalCoarseCorrection() {
            var controller = CreateBootstrapController();
            controller.UpdateObservation(5, -5);
            var probe = controller.CreatePlan();
            controller.NoteSuccessfulExecution(probe);

            // The Y probe is large, but it only reproduces the trusted X/AZ direction.
            controller.UpdateObservation(5.5, -5);

            controller.CreatePlan().Reason.Should().NotBe("Session-local conditioned coarse correction");
        }

        [Test]
        public void AutomatedAdjustmentController_LatchesOffMotionAfterRegressedBoundedYBootstrapProbe() {
            var controller = CreateBootstrapController();
            controller.UpdateObservation(5, -5);
            var probe = controller.CreatePlan();
            probe.IsBoundedYBootstrapProbe.Should().BeTrue();
            controller.NoteSuccessfulExecution(probe);

            controller.AbortBoundedYBootstrapProbeAfterRegression(5.5, -5);

            controller.MotionAuthorityAborted.Should().BeTrue();
            controller.CreatePlan().HasMovement.Should().BeFalse();
            controller.CreatePlan().Reason.Should().Contain("latched off");
        }

        [Test]
        public void AutomatedAdjustmentController_ReportsQualifiedBoundedYBootstrapOnlyAfterFreshIndependentResponse() {
            var controller = CreateBootstrapController();
            controller.UpdateObservation(5, -5);
            controller.HasQualifiedSessionLocalYBootstrapResponse.Should().BeFalse();
            var probe = controller.CreatePlan();
            controller.NoteSuccessfulExecution(probe);
            controller.UpdateObservation(5, -4.5);

            controller.HasQualifiedSessionLocalYBootstrapResponse.Should().BeTrue();
        }

        private static AutomatedAdjustmentController CreateBootstrapController() {
            var controller = new AutomatedAdjustmentController(useUpasEngagementController: true);
            controller.ConfigureCalibratedDirectFullTravelRoute(
                enabled: true,
                operatorConfirmed: true,
                azimuthStartingPositionDegrees: 0,
                azimuthMinimumDegrees: -5.4,
                azimuthMaximumDegrees: 5.4,
                altitudeStartingPositionDegrees: 0,
                altitudeMinimumDegrees: -5.4,
                altitudeMaximumDegrees: 5.4,
                azimuthDeltaPerXUnitDegrees: 0.05,
                azimuthDeltaPerYUnitDegrees: 0,
                altitudeDeltaPerXUnitDegrees: 0,
                altitudeDeltaPerYUnitDegrees: 0,
                maximumXUnitsPerMove: 81,
                maximumYUnitsPerMove: 81,
                physicalAzimuthDegreesPerXUnit: 0.05,
                physicalAltitudeDegreesPerYUnit: 0.05);
            return controller;
        }
    }
}
