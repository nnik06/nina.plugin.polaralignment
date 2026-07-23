using FluentAssertions;
using NINA.Core.Enum;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaDriftArcSafetyPolicyTest {
        [TestCase(PierSide.pierEast)]
        [TestCase(PierSide.pierWest)]
        [TestCase(PierSide.pierUnknown)]
        public void AcceptsQualifiedConstantSideArc(PierSide side) {
            var result = TppaDriftArcSafetyPolicy.Evaluate(Candidates(side));

            result.IsSafe.Should().BeTrue(result.Reason);
            result.MinimumPredictedAltitudeDegrees.Should().Be(38);
        }

        [Test]
        public void RejectsPierSideChange() {
            var candidates = Candidates(PierSide.pierEast);
            candidates[2] = candidates[2] with { PredictedPierSide = PierSide.pierWest };

            var result = TppaDriftArcSafetyPolicy.Evaluate(candidates);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("changes pier side");
        }

        [TestCase(29.999)]
        [TestCase(-1)]
        public void RejectsAltitudeBelowFloor(double altitudeDegrees) {
            var candidates = Candidates(PierSide.pierEast);
            candidates[1] = candidates[1] with { PredictedAltitudeDegrees = altitudeDegrees };

            var result = TppaDriftArcSafetyPolicy.Evaluate(candidates);

            result.IsSafe.Should().BeFalse();
            result.MinimumPredictedAltitudeDegrees.Should().Be(altitudeDegrees);
            result.Reason.Should().Contain("safety floor");
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void RejectsNonFiniteAltitude(double altitudeDegrees) {
            var candidates = Candidates(PierSide.pierUnknown);
            candidates[3] = candidates[3] with { PredictedAltitudeDegrees = altitudeDegrees };

            var result = TppaDriftArcSafetyPolicy.Evaluate(candidates);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("finite");
        }

        [Test]
        public void RejectsWrongPositionOrder() {
            var candidates = Candidates(PierSide.pierEast);
            candidates[2] = candidates[2] with { PositionId = "B" };

            var result = TppaDriftArcSafetyPolicy.Evaluate(candidates);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("must be C");
        }

        [Test]
        public void RejectsMissingCandidate() {
            var candidates = Candidates(PierSide.pierEast).Take(3).ToArray();

            var result = TppaDriftArcSafetyPolicy.Evaluate(candidates);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("exactly A-B-C-A");
        }

        private static TppaDriftArcCandidate[] Candidates(PierSide side) => new[] {
            new TppaDriftArcCandidate("A", 40, side),
            new TppaDriftArcCandidate("B", 39, side),
            new TppaDriftArcCandidate("C", 38, side),
            new TppaDriftArcCandidate("A", 41, side)
        };
    }
}
