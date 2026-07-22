using FluentAssertions;
using System;

namespace NINA.Plugins.PolarAlignment.Test {
    public class PolarDriftEstimatorTest {
        private static readonly PolarDriftStabilityPolicy FastPolicy = new(
            MinimumDurationSeconds: 20,
            MinimumSampleCount: 20,
            MaximumSigmaArcMinutes: 0.25,
            HalfSlopeDifferenceFloorArcMinutes: 0.5,
            HalfSlopeDifferenceFraction: 0.35,
            MaximumHalfDirectionDifferenceDegrees: 5.0);

        [Test]
        public void LinearTraceMatchesPHD2PolarDriftEquation() {
            var estimator = new PolarDriftEstimator();
            for (var i = 0; i <= 30; i++) {
                estimator.TryAddSample(i, 100 + 0.001 * i, 200 - 0.002 * i).Should().BeTrue();
            }

            var result = estimator.Evaluate(2.0, hemisphere: 1, mirror: 1, FastPolicy);
            var expected = Math.Sqrt(0.001 * 0.001 + 0.002 * 0.002)
                * PolarDriftEstimator.SecondsPerRadian * 2.0 / 60.0;

            result.PolarErrorArcMinutes.Should().BeApproximately(expected, 1e-9);
            result.XDriftPixelsPerSecond.Should().BeApproximately(0.001, 1e-12);
            result.YDriftPixelsPerSecond.Should().BeApproximately(-0.002, 1e-12);
            result.IsStable.Should().BeTrue();
        }

        [Test]
        public void ConstantLockOffsetDoesNotChangeEstimate() {
            var first = new PolarDriftEstimator();
            var second = new PolarDriftEstimator();
            for (var i = 0; i <= 30; i++) {
                first.TryAddSample(i, 0.001 * i, -0.002 * i);
                second.TryAddSample(i, 500 + 0.001 * i, -300 - 0.002 * i);
            }

            var a = first.Evaluate(2.0, 1, 1, FastPolicy);
            var b = second.Evaluate(2.0, 1, 1, FastPolicy);

            b.PolarErrorArcMinutes.Should().BeApproximately(a.PolarErrorArcMinutes, 1e-9);
            b.PoleDirectionCameraDegrees.Should().BeApproximately(a.PoleDirectionCameraDegrees, 1e-9);
        }

        [Test]
        public void DirectionMatchesPHD2TargetAndDisplayConventions() {
            static PolarDriftEstimate Evaluate(double xSlope, double ySlope, int hemisphere, int mirror) {
                var estimator = new PolarDriftEstimator();
                for (var i = 0; i <= 30; i++) {
                    estimator.TryAddSample(i, xSlope * i, ySlope * i);
                }
                return estimator.Evaluate(2.0, hemisphere, mirror, FastPolicy);
            }

            var north = Evaluate(0.001, 0, hemisphere: 1, mirror: 1);
            north.PoleDirectionCameraDegrees.Should().BeApproximately(90, 1e-9);
            north.Phd2DisplayAngleDegrees.Should().BeApproximately(-90, 1e-9);

            var south = Evaluate(0.001, 0, hemisphere: -1, mirror: 1);
            south.PoleDirectionCameraDegrees.Should().BeApproximately(-90, 1e-9);
            south.Phd2DisplayAngleDegrees.Should().BeApproximately(90, 1e-9);

            var mirroredNorth = Evaluate(0.001, 0, hemisphere: 1, mirror: -1);
            mirroredNorth.PoleDirectionCameraDegrees.Should().BeApproximately(-90, 1e-9);
            mirroredNorth.Phd2DisplayAngleDegrees.Should().BeApproximately(90, 1e-9);
        }

        [Test]
        public void CurvedTraceFailsHalfWindowConsistencyGate() {
            var estimator = new PolarDriftEstimator();
            for (var i = 0; i <= 60; i++) {
                var x = i <= 30 ? 0.001 * i : 0.03 - 0.004 * (i - 30);
                estimator.TryAddSample(i, x, 0);
            }

            var result = estimator.Evaluate(2.0, 1, 1, FastPolicy);

            result.IsStable.Should().BeFalse();
            result.Reason.Should().Contain("half-window");
        }

        [Test]
        public void RotatingTraceFailsHalfWindowDirectionGateEvenWhenMagnitudeMatches() {
            var estimator = new PolarDriftEstimator();
            const double speed = 0.001;
            var angle = 8.0 * Math.PI / 180.0;
            for (var i = 0; i <= 60; i++) {
                var elapsed = i <= 30 ? i : i - 30;
                var x = i <= 30 ? speed * i : 30 * speed + speed * Math.Cos(angle) * elapsed;
                var y = i <= 30 ? 0 : speed * Math.Sin(angle) * elapsed;
                estimator.TryAddSample(i, x, y);
            }

            var result = estimator.Evaluate(2.0, 1, 1, FastPolicy);

            result.IsStable.Should().BeFalse();
            result.HalfDirectionDifferenceDegrees.Should().BeGreaterThan(5.0);
            result.Reason.Should().Contain("direction");
        }

        [Test]
        public void FieldPolicyRejectsShortCapture() {
            var estimator = new PolarDriftEstimator();
            for (var i = 0; i <= 120; i++) {
                estimator.TryAddSample(i, 0.001 * i, 0);
            }

            var result = estimator.Evaluate(2.0, 1, 1);

            result.IsStable.Should().BeFalse();
            result.Reason.Should().Contain("duration");
        }

        [Test]
        public void ForeignGuidePulseInvalidationFailsClosed() {
            var estimator = new PolarDriftEstimator();
            for (var i = 0; i <= 30; i++) {
                estimator.TryAddSample(i, 0.001 * i, 0);
            }
            estimator.Invalidate("foreign guide pulse detected");

            var result = estimator.Evaluate(2.0, 1, 1, FastPolicy);

            result.IsStable.Should().BeFalse();
            result.Reason.Should().Contain("foreign guide pulse");
        }

        [Test]
        public void RejectsNonMonotonicAndNonFiniteSamples() {
            var estimator = new PolarDriftEstimator();

            estimator.TryAddSample(1, 0, 0).Should().BeTrue();
            estimator.TryAddSample(1, 1, 1).Should().BeFalse();
            estimator.TryAddSample(double.NaN, 1, 1).Should().BeFalse();
        }
    }
}