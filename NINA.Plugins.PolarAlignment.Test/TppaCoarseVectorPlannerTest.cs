using FluentAssertions;
using NINA.Plugins.PolarAlignment;
using NUnit.Framework;
using System;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaCoarseVectorPlannerTest {
        [Test]
        public void IdentityResponsePlansFullFiveDegreeBoundaryVector() {
            var result = Plan(Error(180.0, 240.0), Axes(), Calibration());

            result.IsAuthorized.Should().BeTrue();
            result.RequiresMove.Should().BeTrue();
            result.RequestedAzimuthDeltaDegrees.Should().BeApproximately(-2.7, 1e-12);
            result.RequestedAltitudeDeltaDegrees.Should().BeApproximately(-3.6, 1e-12);
            result.ProjectedResidualMinutes.Should().BeApproximately(30.0, 1e-9);
            result.AzimuthExpandedPath.LowerDegrees.Should().BeLessThan(-2.7);
            result.AltitudeExpandedPath.LowerDegrees.Should().BeLessThan(-3.6);
        }

        [Test]
        public void CoupledResponseUsesFullMatrixRatherThanIndependentAxisGains() {
            var calibration = Calibration(matrix: new UpasResponseMatrix(
                1.0, 0.25, -0.10, 1.1, 1.5));

            var result = Plan(Error(120.0, 60.0), Axes(), calibration);

            result.IsAuthorized.Should().BeTrue();
            var errorAfterAz = 2.0 + result.RequestedAzimuthDeltaDegrees
                + 0.25 * result.RequestedAltitudeDeltaDegrees;
            var errorAfterAlt = 1.0 - 0.10 * result.RequestedAzimuthDeltaDegrees
                + 1.1 * result.RequestedAltitudeDeltaDegrees;
            errorAfterAz.Should().BeApproximately(0.2, 1e-10);
            errorAfterAlt.Should().BeApproximately(0.1, 1e-10);
        }

        [Test]
        public void FineHandoffRequiresNoMove() {
            var result = Plan(Error(12.0, 12.0), Axes(), Calibration());

            result.IsAuthorized.Should().BeTrue();
            result.RequiresMove.Should().BeFalse();
            result.ProjectedResidualMinutes.Should().BeApproximately(
                Math.Sqrt(288.0), 1e-12);
        }

        [Test]
        public void ErrorAboveFiveDegreeObjectiveIsDenied() {
            var result = Plan(Error(301.0, 0.0), Axes(), Calibration());

            result.IsAuthorized.Should().BeFalse();
            result.Reason.Should().Contain("objective envelope");
        }

        [Test]
        public void UncertaintyExpandedIntermediatePathMustStayInsideOperationalEnvelope() {
            var axes = Axes(azimuth: 4.0, altitude: 0.0, sigma: 0.02);

            var result = Plan(Error(-120.0, 0.0), axes, Calibration());

            result.IsAuthorized.Should().BeFalse();
            result.AzimuthExpandedPath.UpperDegrees.Should().BeGreaterThan(4.4);
            result.Reason.Should().Contain("operational envelope");
        }

        [Test]
        public void OutwardMillidegreeRoundingNeverShrinksPath() {
            var axes = Axes(azimuth: 0.0004, altitude: 0.0, sigma: 0.0,
                azimuthEngagement: UpasAxisEngagementState.Negative);
            var calibration = Calibration(fixedUncertainty: 0.0, deadband: 0.0);

            var result = Plan(Error(100.0, 0.0, covariance: 0.0), axes, calibration);

            result.IsAuthorized.Should().BeTrue();
            result.AzimuthExpandedPath.Lower.Should().Be(-1500);
            result.AzimuthExpandedPath.Upper.Should().Be(1);
        }

        [Test]
        public void UnknownOrReversedEngagementAddsDirectionalDeadband() {
            var unknown = Plan(
                Error(-60.0, 0.0),
                Axes(azimuthEngagement: UpasAxisEngagementState.Unknown),
                Calibration(positiveDeadband: 0.12, negativeDeadband: 0.04));
            var engaged = Plan(
                Error(-60.0, 0.0),
                Axes(azimuthEngagement: UpasAxisEngagementState.Positive),
                Calibration(positiveDeadband: 0.12, negativeDeadband: 0.04));
            var reversed = Plan(
                Error(-60.0, 0.0),
                Axes(azimuthEngagement: UpasAxisEngagementState.Negative),
                Calibration(positiveDeadband: 0.12, negativeDeadband: 0.04));

            unknown.AzimuthAdditiveBoundDegrees.Should().BeApproximately(0.13, 1e-12);
            engaged.AzimuthAdditiveBoundDegrees.Should().BeApproximately(0.01, 1e-12);
            reversed.AzimuthAdditiveBoundDegrees.Should().BeApproximately(0.13, 1e-12);
        }

        [Test]
        public void CalibrationCovarianceExpandsModelAndResidualUncertaintyButNotPhysicalPath() {
            var zero = Plan(Error(120.0, 0.0), Axes(), Calibration(covariance: 0.0));
            var uncertain = Plan(Error(120.0, 0.0), Axes(), Calibration(covariance: 0.0025));

            uncertain.AzimuthCorrectionModelStandardUncertaintyDegrees
                .Should().BeGreaterThan(zero.AzimuthCorrectionModelStandardUncertaintyDegrees);
            uncertain.ProjectedResidualUpperMinutes.Should()
                .BeGreaterThan(zero.ProjectedResidualUpperMinutes);
            uncertain.AzimuthExpandedPath.Should().Be(zero.AzimuthExpandedPath);
        }

        [Test]
        public void EndpointMustRemainInsideCalibrationPositionRange() {
            var calibration = Calibration(minimumPosition: -1.0, maximumPosition: 1.0);

            var result = Plan(Error(120.0, 0.0), Axes(), calibration);

            result.IsAuthorized.Should().BeFalse();
            result.Reason.Should().Contain("calibration applicability");
        }

        [Test]
        public void DefensiveConditionNumberGateCannotBeBypassedByConstructedRecord() {
            var calibration = Calibration(matrix: new UpasResponseMatrix(
                1.0, 0.0, 0.0, 1.0, 10.01));

            var result = Plan(Error(60.0, 0.0), Axes(), calibration);

            result.IsAuthorized.Should().BeFalse();
            result.Reason.Should().Contain("condition limit");
        }

        [Test]
        public void InvalidInputCovarianceThrowsBeforePlanning() {
            var invalidError = new TppaCoarseErrorEvidence(60.0, 0.0, 1.0, 2.0, 1.0);
            var invalidAxes = new UpasSupervisorAxesEvidence(
                Axis(0.0, 1.0, UpasAxisEngagementState.Unknown),
                Axis(0.0, 1.0, UpasAxisEngagementState.Unknown),
                1.0, 2.0, 1.0);

            Action errorPlan = () => Plan(invalidError, Axes(), Calibration());
            Action axesPlan = () => Plan(Error(60.0, 0.0), invalidAxes, Calibration());

            errorPlan.Should().Throw<ArgumentOutOfRangeException>();
            axesPlan.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Test]
        public void PlannerDoesNotExposeMotionAuthority() {
            foreach (var property in typeof(TppaCoarseVectorPlan).GetProperties()) {
                property.PropertyType.Should().NotBe(typeof(IAutomatedMoveExecutor));
                property.PropertyType.Should().NotBe(typeof(IPolarAlignmentSystemVM));
            }
        }

        private static TppaCoarseVectorPlan Plan(
                TppaCoarseErrorEvidence error,
                UpasSupervisorAxesEvidence axes,
                UpasSupervisorCoarseResponseCalibration calibration) =>
            TppaCoarseVectorPlanner.Plan(error, axes, calibration);

        private static TppaCoarseErrorEvidence Error(
                double azimuth,
                double altitude,
                double covariance = 1.0) =>
            new(azimuth, altitude, covariance, 0.0, covariance);

        private static UpasSupervisorAxesEvidence Axes(
                double azimuth = 0.0,
                double altitude = 0.0,
                double sigma = 0.01,
                UpasAxisEngagementState azimuthEngagement = UpasAxisEngagementState.Unknown,
                UpasAxisEngagementState altitudeEngagement = UpasAxisEngagementState.Unknown) =>
            new(
                Axis(azimuth, sigma, azimuthEngagement),
                Axis(altitude, sigma, altitudeEngagement),
                sigma * sigma, 0.0, sigma * sigma);

        private static UpasSupervisorAxisEvidence Axis(
                double position,
                double sigma,
                UpasAxisEngagementState engagement) =>
            new(position, sigma, engagement, Array.Empty<UpasSupervisorPositionWitness>());

        private static UpasSupervisorCoarseResponseCalibration Calibration(
                UpasResponseMatrix matrix = null,
                double covariance = 0.0001,
                double fixedUncertainty = 0.01,
                double deadband = 0.05,
                double positiveDeadband = double.NaN,
                double negativeDeadband = double.NaN,
                double minimumPosition = -4.4,
                double maximumPosition = 4.4) {
            var matrixCovariance = new double[4, 4];
            for (var index = 0; index < 4; index++) {
                matrixCovariance[index, index] = covariance;
            }
            var positive = double.IsNaN(positiveDeadband) ? deadband : positiveDeadband;
            var negative = double.IsNaN(negativeDeadband) ? deadband : negativeDeadband;
            return new UpasSupervisorCoarseResponseCalibration(
                Guid.Parse("30000000-0000-0000-0000-000000000001"),
                new string('c', 64),
                DateTimeOffset.Parse("2026-08-03T12:00:00Z"),
                UpasCoarsePlanningSafetyPolicy.RequiredCoordinateConvention,
                matrix ?? new UpasResponseMatrix(1.0, 0.0, 0.0, 1.0, 1.0),
                matrixCovariance,
                fixedUncertainty, fixedUncertainty,
                new UpasDirectionalBounds(positive, negative),
                new UpasDirectionalBounds(positive, negative),
                minimumPosition, maximumPosition, minimumPosition, maximumPosition,
                20.0, 60.0, "hae29c-ec-full-rig-v1", 100.0, 5000.0,
                Guid.Parse("a7901d62-e1e8-4cce-bd6a-e3e986d8cd8a"));
        }
    }
}
