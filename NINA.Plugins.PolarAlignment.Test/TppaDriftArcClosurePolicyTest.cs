using FluentAssertions;
using NINA.Core.Enum;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaDriftArcClosurePolicyTest {
        [Test]
        public void AcceptsCloseReturnAcrossRightAscensionWraparound() {
            var result = Evaluate(
                expectedRa: 359.9,
                expectedDec: 30,
                actualRa: 0.1,
                actualDec: 30);

            result.IsSafe.Should().BeTrue(result.Reason);
            result.PointingSeparationDegrees.Should().BeLessThan(0.25);
        }

        [Test]
        public void UsesGreatCircleSeparationNearPole() {
            var result = Evaluate(
                expectedRa: 0,
                expectedDec: 89,
                actualRa: 10,
                actualDec: 89);

            result.IsSafe.Should().BeTrue(result.Reason);
            result.PointingSeparationDegrees.Should().BeLessThan(0.25);
        }

        [Test]
        public void RejectsReturnOutsideTolerance() {
            var result = Evaluate(actualRa: 1);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("exceeds");
        }

        [Test]
        public void RejectsKnownDestinationPierSideChange() {
            var result = Evaluate(
                actualRa: 0.1,
                expectedPierSide: PierSide.pierEast,
                actualPierSide: PierSide.pierWest);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("changed");
        }

        [Test]
        public void AcceptsUnknownPierSideButReportsUnavailableVerification() {
            var result = Evaluate(
                actualRa: 0.1,
                expectedPierSide: PierSide.pierUnknown,
                actualPierSide: PierSide.pierUnknown);

            result.IsSafe.Should().BeTrue(result.Reason);
            result.Reason.Should().Contain("unavailable");
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void RejectsNonFiniteCoordinate(double actualRa) {
            var result = Evaluate(actualRa: actualRa);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("finite");
        }

        [TestCase(-90.1)]
        [TestCase(90.1)]
        public void RejectsInvalidDeclination(double actualDec) {
            var result = Evaluate(actualDec: actualDec);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("declination");
        }

        private static TppaDriftArcClosureResult Evaluate(
            double expectedRa = 0,
            double expectedDec = 30,
            double actualRa = 0,
            double actualDec = 30,
            PierSide expectedPierSide = PierSide.pierEast,
            PierSide actualPierSide = PierSide.pierEast) =>
            TppaDriftArcClosurePolicy.Evaluate(
                expectedRa,
                expectedDec,
                actualRa,
                actualDec,
                expectedPierSide,
                actualPierSide);
    }
}
