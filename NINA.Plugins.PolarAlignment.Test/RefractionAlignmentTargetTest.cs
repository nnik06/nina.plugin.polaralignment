using FluentAssertions;
using NINA.Plugins.PolarAlignment.Instructions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class RefractionAlignmentTargetTest {
        [Test]
        public void CalculateTruePoleOffsetArcMinutes_DubaiStandardAtmosphere_MatchesNinaAstrometry() {
            var refraction = new RefractionParameters(1013.25, 15.0, 0.0, 0.55);

            var offset = RefractionAlignmentTarget.CalculateTruePoleOffsetArcMinutes(25.116194444444446,
                                                                                     refraction);

            offset.Should().BeApproximately(2.0167, 0.01);
        }

        [TestCase(2.0167, 0.5, true)]
        [TestCase(2.0167, 1.0, true)]
        [TestCase(2.0167, 3.0, false)]
        [TestCase(2.0167, 0.0, false)]
        public void IsMaterialToTolerance_OnlyFlagsPositiveTolerancesBelowOffset(double offsetArcMinutes,
                                                                                 double toleranceArcMinutes,
                                                                                 bool expected) {
            RefractionAlignmentTarget.IsMaterialToTolerance(offsetArcMinutes, toleranceArcMinutes)
                                     .Should()
                                     .Be(expected);
        }

        [Test]
        public void ApparentPoleModeBlocksAutomatedActuatorMovement() {
            var issues = RefractionAlignmentTarget.GetValidationIssues(
                refractionAdjustmentEnabled: false,
                automatedAdjustmentsEnabled: true,
                actuatorMovementAllowed: true,
                driftValidationOnly: false);

            issues.Should().ContainSingle()
                  .Which.Should().Be(RefractionAlignmentTarget.AutomatedAdjustmentRequiresTruePoleIssue);
        }

        [Test]
        public void ApparentPoleModeBlocksDriftValidation() {
            var issues = RefractionAlignmentTarget.GetValidationIssues(
                refractionAdjustmentEnabled: false,
                automatedAdjustmentsEnabled: false,
                actuatorMovementAllowed: false,
                driftValidationOnly: true);

            issues.Should().ContainSingle()
                  .Which.Should().Be(RefractionAlignmentTarget.DriftValidationRequiresTruePoleIssue);
        }

        [Test]
        public void ApparentPoleModeRemainsAvailableForMeasurementOnlyRuns() {
            var issues = RefractionAlignmentTarget.GetValidationIssues(
                refractionAdjustmentEnabled: false,
                automatedAdjustmentsEnabled: false,
                actuatorMovementAllowed: false,
                driftValidationOnly: false);

            issues.Should().BeEmpty();
            RefractionAlignmentTarget.GetPoleTarget(false)
                                     .Should().Be(RefractionAlignmentTarget.ApparentPoleTarget);
        }

        [Test]
        public void TruePoleModePassesAllTargetPolicyGates() {
            RefractionAlignmentTarget.GetValidationIssues(true, true, true, true)
                                     .Should().BeEmpty();
            RefractionAlignmentTarget.GetPoleTarget(true)
                                     .Should().Be(RefractionAlignmentTarget.TruePoleTarget);
        }
    }
}
