using FluentAssertions;
using NINA.Core.Enum;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaDriftMoveVerificationPolicyTest {
        [Test]
        public void AcceptsExpectedTravelWithConstantKnownPierSide() {
            var result = Evaluate(endRa: 20.4);

            result.IsSafe.Should().BeTrue(result.Reason);
            result.RightAscensionTravelDegrees.Should().BeApproximately(20.4, 0.0001);
            result.DeclinationTravelDegrees.Should().BeApproximately(0.1, 0.0001);
            result.Reason.Should().Contain("constant destination pier side");
        }

        [Test]
        public void AcceptsRightAscensionWraparound() {
            var result = Evaluate(startRa: 350, endRa: 10);

            result.IsSafe.Should().BeTrue(result.Reason);
            result.RightAscensionTravelDegrees.Should().BeApproximately(20, 0.0001);
        }

        [Test]
        public void AcceptsUnknownPierSideButReportsUnavailableVerification() {
            var result = Evaluate(
                endRa: 20,
                startPierSide: PierSide.pierUnknown,
                endPierSide: PierSide.pierUnknown);

            result.IsSafe.Should().BeTrue(result.Reason);
            result.Reason.Should().Contain("unavailable");
        }

        [Test]
        public void RejectsUndertravel() {
            var result = Evaluate(endRa: 18.99);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("below");
        }

        [Test]
        public void RejectsOvershoot() {
            var result = Evaluate(endRa: 22.01);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("exceeds");
        }

        [Test]
        public void RejectsCrossAxisTravel() {
            var result = Evaluate(endRa: 20, endDec: 40.251);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("declination");
        }

        [Test]
        public void RejectsKnownPierSideChange() {
            var result = Evaluate(
                endRa: 20,
                startPierSide: PierSide.pierEast,
                endPierSide: PierSide.pierWest);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("changed");
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void RejectsNonFiniteInput(double endRa) {
            var result = Evaluate(endRa: endRa);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("finite");
        }

        [TestCase(0)]
        [TestCase(90)]
        public void RejectsInvalidExpectedTravel(double expectedTravel) {
            var result = Evaluate(endRa: 20, expectedTravel: expectedTravel);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("between 0 and 90");
        }

        private static TppaDriftMoveVerificationResult Evaluate(
            double startRa = 0,
            double endRa = 20,
            double startDec = 40,
            double endDec = 40.1,
            double expectedTravel = 20,
            PierSide startPierSide = PierSide.pierEast,
            PierSide endPierSide = PierSide.pierEast) =>
            TppaDriftMoveVerificationPolicy.Evaluate(
                startRa,
                endRa,
                startDec,
                endDec,
                expectedTravel,
                startPierSide,
                endPierSide);
    }
}
