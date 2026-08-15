using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaSolveConsistencyQualificationPolicyTest {
        [Test]
        public void FirstAttemptConsistentPointsAuthorizeActuatorInput() {
            var result = TppaSolveConsistencyQualificationPolicy.Evaluate(new[] {
                Point(1, 3.931, 63.938, 0.547, 63.452),
                Point(1, 344.368, 63.938, 342.543, 62.390),
                Point(1, 325.652, 63.938, 324.511, 62.396)
            });

            result.IsQualified.Should().BeTrue();
            (result.MaximumResidualDegrees - result.MinimumResidualDegrees).Should().BeLessThan(1.0);
        }

        [Test]
        public void ConsistentRetriedPointAuthorizesActuatorInput() {
            var result = TppaSolveConsistencyQualificationPolicy.Evaluate(new[] {
                Point(2, 3.931, 63.938, 0.547, 63.452),
                Point(1, 344.368, 63.938, 342.543, 62.390),
                Point(1, 325.652, 63.938, 324.511, 62.396)
            });

            result.IsQualified.Should().BeTrue();
            result.Reason.Should().NotContain("retry");
        }

        [Test]
        public void AugustEleventhFallbackSolveFailsResidualSpread() {
            var result = TppaSolveConsistencyQualificationPolicy.Evaluate(new[] {
                Point(4, 2.709, 63.938, 355.154, 63.396),
                Point(1, 342.015, 63.938, 340.111, 62.346),
                Point(1, 321.172, 63.938, 320.099, 62.375)
            });

            result.IsQualified.Should().BeFalse();
            result.Reason.Should().Contain("residual spread");
            result.Reason.Should().NotContain("retry");
        }

        [Test]
        public void RetriedPointStillFailsResidualSpread() {
            var result = TppaSolveConsistencyQualificationPolicy.Evaluate(new[] {
                Point(2, 0, 60, 0, 60), Point(1, 15, 60, 15, 60), Point(1, 30, 60, 33, 60)
            });

            result.IsQualified.Should().BeFalse();
            result.Reason.Should().Contain("residual spread");
        }

        [Test]
        public void RetriedPointStillFailsResidualMagnitude() {
            var result = TppaSolveConsistencyQualificationPolicy.Evaluate(new[] {
                Point(2, 0, 60, 40, 60), Point(1, 15, 60, 55, 60), Point(1, 30, 60, 70, 60)
            });

            result.IsQualified.Should().BeFalse();
            result.Reason.Should().Contain("coordinate-consistency ceiling");
        }

        [Test]
        public void RetriedPointWithNonFiniteCoordinateFailsClosed() {
            var result = TppaSolveConsistencyQualificationPolicy.Evaluate(new[] {
                Point(2, double.NaN, 60, 0, 60), Point(1, 15, 60, 15, 60), Point(1, 30, 60, 30, 60)
            });

            result.IsQualified.Should().BeFalse();
            result.Reason.Should().Contain("non-finite");
        }

        [Test]
        public void MissingPointFailsClosed() {
            var result = TppaSolveConsistencyQualificationPolicy.Evaluate(new[] {
                Point(1, 0, 60, 0, 60), Point(1, 15, 60, 15, 60)
            });

            result.IsQualified.Should().BeFalse();
            result.Reason.Should().Contain("exactly 3");
        }

        [Test]
        public void GrossButCorrelatedPointingOffsetFailsClosed() {
            var result = TppaSolveConsistencyQualificationPolicy.Evaluate(new[] {
                Point(1, 0, 60, 40, 60), Point(1, 15, 60, 55, 60), Point(1, 30, 60, 70, 60)
            });

            result.IsQualified.Should().BeFalse();
            result.Reason.Should().Contain("coordinate-consistency ceiling");
        }

        private static TppaSolvedPointing Point(int attempt, double mountRa, double mountDec, double solvedRa, double solvedDec) =>
            new(attempt, 30, mountRa, mountDec, solvedRa, solvedDec);
    }
}
