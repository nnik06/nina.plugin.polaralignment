using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class PolarDriftRepeatabilityTest {
        [Test]
        public void TonightCapturesFailRepeatabilityGate() {
            var result = PolarDriftRepeatabilityEvaluator.Evaluate(new[] {
                StableEstimate(7.797, -40.39), StableEstimate(6.550, -25.98)
            });

            result.IsRepeatable.Should().BeFalse();
            result.MaximumMagnitudeDifferenceArcMinutes.Should().BeApproximately(1.247, 0.001);
            result.MaximumDirectionDifferenceDegrees.Should().BeApproximately(14.41, 0.01);
            result.Reason.Should().Contain("magnitude disagreement");
        }

        [Test]
        public void CloseIndependentCapturesPass() {
            var result = PolarDriftRepeatabilityEvaluator.Evaluate(new[] {
                StableEstimate(8.30, 47.5), StableEstimate(8.36, 43.0)
            });

            result.IsRepeatable.Should().BeTrue();
        }

        [Test]
        public void DirectionComparisonHandlesWraparound() {
            var result = PolarDriftRepeatabilityEvaluator.Evaluate(new[] {
                StableEstimate(8.0, 179.0), StableEstimate(8.1, -179.0)
            });

            result.IsRepeatable.Should().BeTrue();
            result.MaximumDirectionDifferenceDegrees.Should().BeApproximately(2.0, 1e-9);
        }

        [Test]
        public void DirectionOnlyMismatchFailsClosed() {
            var result = PolarDriftRepeatabilityEvaluator.Evaluate(new[] {
                StableEstimate(8.0, 10.0), StableEstimate(8.1, 20.0)
            });

            result.IsRepeatable.Should().BeFalse();
            result.Reason.Should().Contain("direction disagreement");
        }

        [Test]
        public void SingleCaptureCannotAuthorizeRepeatability() {
            var result = PolarDriftRepeatabilityEvaluator.Evaluate(new[] {
                StableEstimate(8.0, 10.0)
            });

            result.IsRepeatable.Should().BeFalse();
            result.Reason.Should().Contain("at least two");
        }

        [Test]
        public void NonFiniteCaptureFailsClosed() {
            var result = PolarDriftRepeatabilityEvaluator.Evaluate(new[] {
                StableEstimate(8.0, 10.0), StableEstimate(double.NaN, 10.0)
            });

            result.IsRepeatable.Should().BeFalse();
            result.Reason.Should().Contain("finite");
        }

        [Test]
        public void UnstableCaptureFailsClosed() {
            var result = PolarDriftRepeatabilityEvaluator.Evaluate(new[] {
                StableEstimate(8.0, 20.0), StableEstimate(8.0, 20.0) with { IsStable = false }
            });

            result.IsRepeatable.Should().BeFalse();
            result.Reason.Should().Contain("within-capture");
        }

        private static PolarDriftEstimate StableEstimate(double magnitude, double displayAngle) => new(
            SampleCount: 500, DurationSeconds: 1200, XDriftPixelsPerSecond: 0,
            YDriftPixelsPerSecond: 0, PolarErrorArcMinutes: magnitude,
            PolarErrorSigmaArcMinutes: 0.03, PoleDirectionCameraDegrees: -displayAngle,
            Phd2DisplayAngleDegrees: displayAngle, HalfSlopeDifferenceArcMinutes: 0.2,
            IsStable: true, Reason: "stable");
    }
}
