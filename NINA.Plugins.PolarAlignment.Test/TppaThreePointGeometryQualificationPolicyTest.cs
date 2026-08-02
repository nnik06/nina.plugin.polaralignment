using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaThreePointGeometryQualificationPolicyTest {
        [Test]
        public void GeometryAtAbsoluteMotionFloorPassesNumericalObservabilityGate() {
            var geometry = new TppaThreePointGeometry(
                MinimumPairwiseSeparationDegrees: 15.0,
                MaximumPairwiseSeparationDegrees: 30.0,
                DoubledChordTriangleArea: 0.0111598085,
                NormalizedTriangleQuality: 0.171163);

            var result = TppaThreePointGeometryQualificationPolicy.Evaluate(geometry, 15.0);

            result.IsQualified.Should().BeTrue();
            result.Reason.Should().Contain("numerical-observability gates passed");
        }

        [Test]
        public void AugustSecondFieldGeometryFailsAbsoluteMotionSpanGate() {
            var geometry = new TppaThreePointGeometry(
                MinimumPairwiseSeparationDegrees: 9.289,
                MaximumPairwiseSeparationDegrees: 18.79,
                DoubledChordTriangleArea: 0.011,
                NormalizedTriangleQuality: 0.17);

            var result = TppaThreePointGeometryQualificationPolicy.Evaluate(geometry, 20.0);

            result.IsQualified.Should().BeFalse();
            result.Reason.Should().Contain("minimum pairwise separation");
            result.Reason.Should().Contain("15.00-degree absolute-evidence and motion floor");
        }

        [TestCase(14.999, false)]
        [TestCase(15.000, true)]
        public void SolvedMinimumPairwiseGateIsInclusive(
                double minimumSeparation,
                bool expected) {
            var geometry = new TppaThreePointGeometry(
                minimumSeparation,
                30.0,
                0.011,
                0.17);

            var result = TppaThreePointGeometryQualificationPolicy.Evaluate(geometry, 15.0);

            result.IsQualified.Should().Be(expected);
        }

        [TestCase(14.99, false)]
        [TestCase(15.00, true)]
        public void ConfiguredLegGateIsInclusive(double configuredLegDegrees, bool expected) {
            var isQualified = TppaThreePointGeometryQualificationPolicy
                .GetConfigurationIssues(configuredLegDegrees)
                .Count == 0;

            isQualified.Should().Be(expected);
        }

        [Test]
        public void ConfiguredLegBelowFloorIsRejected() {
            TppaThreePointGeometryQualificationPolicy
                .GetConfigurationIssues(14.99)
                .Should().ContainSingle()
                .Which.Should().Contain("at least 15 degrees");
        }

        [TestCase(7.99, 20.0, 0.17, "Solved-leg balance ratio")]
        [TestCase(10.0, 19.99, 0.17, "Solved end-to-end separation")]
        [TestCase(10.0, 20.0, 0.099, "Normalized solved-triangle quality")]
        public void EachSolvedGeometryBoundaryFailsClosed(
                double minimumSeparation,
                double maximumSeparation,
                double quality,
                string expectedIssue) {
            var geometry = new TppaThreePointGeometry(
                minimumSeparation,
                maximumSeparation,
                0.01,
                quality);

            var result = TppaThreePointGeometryQualificationPolicy.Evaluate(geometry, 15.0);

            result.IsQualified.Should().BeFalse();
            result.Reason.Should().Contain(expectedIssue);
        }

        [Test]
        public void NonFiniteOrDegenerateGeometryFailsClosed() {
            var geometry = new TppaThreePointGeometry(
                double.NaN,
                20.0,
                0.01,
                0.2);

            var result = TppaThreePointGeometryQualificationPolicy.Evaluate(geometry, 15.0);

            result.IsQualified.Should().BeFalse();
            result.Reason.Should().Contain("non-finite or degenerate");
        }

        [Test]
        public void LegBalanceIsScaleFreeAcrossDeclinationLikeForeshortening() {
            var wide = new TppaThreePointGeometry(10.0, 20.0, 0.01, 0.17);
            var foreshortened = new TppaThreePointGeometry(5.0, 10.0, 0.0025, 0.17);

            var wideResult =
                TppaThreePointGeometryQualificationPolicy.Evaluate(wide, 15.0);
            var shortResult =
                TppaThreePointGeometryQualificationPolicy.Evaluate(foreshortened, 15.0);

            wideResult.Reason.Should().NotContain("Solved-leg balance ratio");
            shortResult.Reason.Should().NotContain("Solved-leg balance ratio");
            shortResult.Reason.Should().Contain("Solved end-to-end separation");
        }

        [TestCase(null, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        public void ActuatorPreparationRequiresBoundSolvedGeometry(
                bool? solvedGeometryQualified,
                bool expected) {
            TppaThreePointGeometryQualificationPolicy
                .AllowsActuatorPreparation(solvedGeometryQualified)
                .Should().Be(expected);
        }

        [Test]
        public void VerificationAndOrdinaryAutomatedMinimumsRemainSingleSourced() {
            TppaVerificationSettlePolicy.MinimumQualifiedTargetDistanceDegrees
                .Should().Be(
                    TppaThreePointGeometryQualificationPolicy.MinimumConfiguredLegDegrees);
            TppaThreePointGeometryQualificationPolicy.MinimumSolvedPairwiseSeparationDegrees
                .Should().Be(TppaAbsoluteEvidenceBinder.MinimumQualifiedArcSpanDegrees);
        }
    }
}
