using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaDeclinationArcFeasibilityPolicyTest {
        [TestCase(0.0, 15.0, 15.0)]
        [TestCase(62.0, 20.0, 9.35)]
        [TestCase(-62.0, 20.0, 9.35)]
        public void PredictsFixedDeclinationOnSkyLeg(
                double declinationDegrees,
                double raLegDegrees,
                double expectedOnSkyDegrees) {
            var result = TppaDeclinationArcFeasibilityPolicy.Evaluate(
                raLegDegrees,
                declinationDegrees);

            result.PredictedOnSkyLegDegrees.Should().BeApproximately(
                expectedOnSkyDegrees,
                0.02);
        }

        [Test]
        public void AugustSecondFieldGeometryIsRejectedBeforeActuatorPreparation() {
            var result = TppaDeclinationArcFeasibilityPolicy.Evaluate(20.0, 62.34875);

            result.IsFeasible.Should().BeFalse();
            result.PredictedOnSkyLegDegrees.Should().BeApproximately(9.245, 0.01);
            result.RequiredRaLegDegrees.Should().BeApproximately(32.67, 0.02);
            result.Reason.Should().Contain("No UPAS movement was authorized");
        }

        [Test]
        public void RequiredLegRoundTripsToMotionFloor() {
            var first = TppaDeclinationArcFeasibilityPolicy.Evaluate(20.0, 62.0);
            var roundTrip = TppaDeclinationArcFeasibilityPolicy.Evaluate(
                first.RequiredRaLegDegrees,
                62.0);

            roundTrip.IsFeasible.Should().BeTrue();
            roundTrip.PredictedOnSkyLegDegrees.Should().BeApproximately(
                TppaAbsoluteEvidenceBinder.MinimumQualifiedArcSpanDegrees,
                1e-9);
        }

        [TestCase(83.0)]
        [TestCase(-83.0)]
        [TestCase(90.0)]
        public void NearPolarDeclinationIsImpossibleForFiniteRaTravel(double declinationDegrees) {
            var result = TppaDeclinationArcFeasibilityPolicy.Evaluate(
                180.0,
                declinationDegrees);

            result.IsFeasible.Should().BeFalse();
            result.RequiredRaLegDegrees.Should().Be(double.PositiveInfinity);
        }

        [TestCase(double.NaN, 0.0)]
        [TestCase(20.0, double.NaN)]
        [TestCase(-1.0, 0.0)]
        [TestCase(181.0, 0.0)]
        [TestCase(20.0, 91.0)]
        public void InvalidInputsFailClosed(double raLegDegrees, double declinationDegrees) {
            TppaDeclinationArcFeasibilityPolicy
                .Evaluate(raLegDegrees, declinationDegrees)
                .IsFeasible.Should().BeFalse();
        }

        [Test]
        public void MotionFloorIsSingleSourcedFromAbsoluteEvidenceContract() {
            TppaDeclinationArcFeasibilityPolicy.MinimumOnSkyLegDegrees.Should().Be(
                TppaAbsoluteEvidenceBinder.MinimumQualifiedArcSpanDegrees);
        }
    }
}