using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaThreeSolveShadowEvaluatorTest {
        [Test]
        public void IdenticalEastFieldIsMeasuredButNeverAuthorizesFastPath() {
            var sample = new TppaThreeSolveShadowSample(90, 12, 1, 0, 0);

            var result = TppaThreeSolveShadowEvaluator.Evaluate(sample, sample);

            result.IsValid.Should().BeTrue();
            result.FieldSeparationDegrees.Should().BeApproximately(0, 1e-9);
            result.PositionAngleSeparationDegrees.Should().BeApproximately(0, 1e-9);
            result.SolveCNearestAxis.Should().Be("east");
            result.SolveCWithinAxisGate.Should().BeTrue();
            result.AxisGateAgreement.Should().BeTrue();
            result.GrantsFastPathAuthority.Should().BeFalse();
        }

        [Test]
        public void ReportsSphericalAndWrappedPositionAngleSeparation() {
            var solveC = new TppaThreeSolveShadowSample(89, 359, 1, 0, 0);
            var returned = new TppaThreeSolveShadowSample(91, 1, 0, 1, 0);

            var result = TppaThreeSolveShadowEvaluator.Evaluate(solveC, returned);

            result.FieldSeparationDegrees.Should().BeApproximately(90, 1e-9);
            result.PositionAngleSeparationDegrees.Should().BeApproximately(2, 1e-9);
            result.AxisGateAgreement.Should().BeTrue();
        }

        [Test]
        public void DistinguishesOppositeCardinalAxes() {
            var solveC = new TppaThreeSolveShadowSample(90, 0, 1, 0, 0);
            var returned = new TppaThreeSolveShadowSample(270, 0, -1, 0, 0);

            var result = TppaThreeSolveShadowEvaluator.Evaluate(solveC, returned);

            result.SolveCNearestAxis.Should().Be("east");
            result.ReturnNearestAxis.Should().Be("west");
            result.AxisGateAgreement.Should().BeFalse();
            result.FieldSeparationDegrees.Should().BeApproximately(180, 1e-9);
        }

        [Test]
        public void InvalidVectorFailsClosedWithoutFastPathAuthority() {
            var solveC = new TppaThreeSolveShadowSample(90, 0, 0, 0, 0);
            var returned = new TppaThreeSolveShadowSample(90, 0, 1, 0, 0);

            var result = TppaThreeSolveShadowEvaluator.Evaluate(solveC, returned);

            result.IsValid.Should().BeFalse();
            result.GrantsFastPathAuthority.Should().BeFalse();
            result.Reason.Should().Contain("zero-length");
        }
    }
}
