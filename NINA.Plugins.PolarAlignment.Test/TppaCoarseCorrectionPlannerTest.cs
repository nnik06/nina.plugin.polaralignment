using FluentAssertions;
using NINA.Plugins.PolarAlignment;
using NUnit.Framework;
using System;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaCoarseCorrectionPlannerTest {
        [TestCase(240.0, -3.6)]
        [TestCase(-240.0, 3.6)]
        [TestCase(60.0, -0.9)]
        public void CenteredAxisPlansOppositeSignedNinetyPercentCorrection(
                double errorMinutes,
                double expectedDelta) {
            var result = Plan(errorMinutes, witnessedPosition: 0.0);

            result.IsAuthorized.Should().BeTrue();
            result.RequiresMove.Should().BeTrue();
            result.RequestedDeltaDegrees.Should().BeApproximately(expectedDelta, 1e-9);
            result.ProjectedResidualMinutes.Should().BeApproximately(Math.Abs(errorMinutes) * 0.1, 1e-9);
            result.ProjectedResidualLowerMinutes.Should().BeLessThanOrEqualTo(result.ProjectedResidualMinutes);
            result.ProjectedResidualUpperMinutes.Should().BeGreaterThanOrEqualTo(result.ProjectedResidualMinutes);
        }

        [Test]
        public void FourDegreeStartPreservesOneDegreeNominalTravelReserve() {
            var result = Plan(240.0, witnessedPosition: 0.0);

            (Math.Abs(result.RequestedDeltaDegrees) + result.RequestUncertaintyDegrees)
                .Should().BeLessThan(4.0);
        }

        [Test]
        public void ErrorOutsideFourDegreeObjectiveIsDenied() {
            var result = Plan(240.01, witnessedPosition: 0.0);

            result.IsAuthorized.Should().BeFalse();
            result.Reason.Should().Contain("exceeds");
        }

        [Test]
        public void ErrorInsideFineEnvelopeDoesNotRequestCoarseMotion() {
            var result = Plan(24.0, witnessedPosition: 0.0);

            result.IsAuthorized.Should().BeTrue();
            result.RequiresMove.Should().BeFalse();
            result.ProjectedResidualLowerMinutes.Should().Be(24.0);
            result.ProjectedResidualUpperMinutes.Should().Be(24.0);
        }

        [Test]
        public void TopStratumReportsConservativeResidualInterval() {
            var result = Plan(240.0, witnessedPosition: 0.0);

            result.ProjectedResidualMinutes.Should().BeApproximately(24.0, 1e-9);
            result.ProjectedResidualLowerMinutes.Should().BeApproximately(11.94, 1e-9);
            result.ProjectedResidualUpperMinutes.Should().BeApproximately(36.06, 1e-9);
            result.ProjectedResidualUpperMinutes.Should().BeGreaterThan(
                TppaCoarseCorrectionPlanner.FineControllerHandoffMinutes);
        }

        [Test]
        public void WitnessNearReserveBoundaryIsDeniedBeforePlanning() {
            var result = Plan(120.0, witnessedPosition: 4.36, witnessUncertainty: 0.05);

            result.IsAuthorized.Should().BeFalse();
            result.Reason.Should().Contain("one-degree travel reserve");
        }

        [Test]
        public void PlannedEndpointAndUncertaintyCannotConsumeReserve() {
            var result = Plan(-180.0, witnessedPosition: 1.5, witnessUncertainty: 0.05);

            result.IsAuthorized.Should().BeFalse();
            result.Reason.Should().Contain("planned endpoint plus uncertainty");
        }

        [Test]
        public void TopStratumIsDeniedWhenQualifiedResponseWouldConsumeReserve() {
            var result = TppaCoarseCorrectionPlanner.PlanAxis(
                240.0, 0.0, 0.05, Calibration(response: 0.8));

            result.IsAuthorized.Should().BeFalse();
            result.Reason.Should().Contain("planned endpoint plus uncertainty");
        }

        [Test]
        public void CalibratedResponseScalesPhysicalCommand() {
            var result = TppaCoarseCorrectionPlanner.PlanAxis(
                120.0, 0.0, 0.05, Calibration(response: 0.8));

            result.RequestedDeltaDegrees.Should().BeApproximately(-2.25, 1e-9);
            result.ProjectedResidualMinutes.Should().BeApproximately(12.0, 1e-9);
        }

        [TestCase("coordinate", "wrong", "tppaErrorAfter=tppaErrorBefore+response*physicalDelta", 1.0)]
        [TestCase("equation", "azEastPositive_altUpPositive", "wrong", 1.0)]
        [TestCase("response", "azEastPositive_altUpPositive", "tppaErrorAfter=tppaErrorBefore+response*physicalDelta", -1.0)]
        public void IncompatibleSignedCalibrationIsDenied(
                string _, string coordinateConvention, string equation, double response) {
            var calibration = Calibration(response) with {
                CoordinateConvention = coordinateConvention,
                ErrorEquation = equation
            };

            var result = TppaCoarseCorrectionPlanner.PlanAxis(120.0, 0.0, 0.05, calibration);

            result.IsAuthorized.Should().BeFalse();
            result.Reason.Should().Contain("signed physical-coordinate response calibration");
        }

        [Test]
        public void EmptyCalibrationIdIsDenied() {
            var result = TppaCoarseCorrectionPlanner.PlanAxis(
                120.0, 0.0, 0.05, Calibration() with { CalibrationId = Guid.Empty });

            result.IsAuthorized.Should().BeFalse();
        }
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void NonFiniteInputsAreRejected(double value) {
            Action act = () => TppaCoarseCorrectionPlanner.PlanAxis(
                value, 0.0, 0.05, Calibration());

            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        private static TppaCoarseCorrectionDecision Plan(
                double errorMinutes,
                double witnessedPosition,
                double witnessUncertainty = 0.05) =>
            TppaCoarseCorrectionPlanner.PlanAxis(
                errorMinutes,
                witnessedPosition,
                witnessUncertainty,
                Calibration());

        private static TppaAxisResponseCalibration Calibration(double response = 1.0) =>
            new(
                Guid.Parse("11111111-2222-3333-4444-555555555555"),
                TppaCoarseCorrectionPlanner.RequiredCoordinateConvention,
                TppaCoarseCorrectionPlanner.RequiredErrorEquation,
                response,
                ResponseRelativeUncertainty: 0.05,
                FixedCommandUncertaintyDegrees: 0.02);
    }
}
