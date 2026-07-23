using FluentAssertions;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaDeclinationDriftTrackEstimatorTest {
        [Test]
        public void RecoversLinearDriftWithWhiteNoise() {
            var estimator = BuildLinearTrack(0.42, index => 0.35 * Math.Sin(index * 1.7));

            var result = estimator.Evaluate();

            result.IsValid.Should().BeTrue(result.Reason);
            result.DeclinationDriftArcsecondsPerMinute.Should().BeApproximately(0.42, 0.01);
            result.DeclinationDriftSigmaArcsecondsPerMinute.Should().BeLessThan(0.05);
        }

        [Test]
        public void IsRobustToIsolatedLargeOutliers() {
            var estimator = BuildLinearTrack(0.42, index =>
                index is 12 or 41 or 55 ? 15 : 0.25 * Math.Sin(index * 1.3));

            var result = estimator.Evaluate();

            result.IsValid.Should().BeTrue(result.Reason);
            result.AcceptedSampleCount.Should().Be(58);
            result.DeclinationDriftArcsecondsPerMinute.Should().BeApproximately(0.42, 0.02);
        }

        [Test]
        public void RejectsCurvedTrackThroughHalfWindowGate() {
            var estimator = new TppaDeclinationDriftTrackEstimator();
            for (var index = 0; index <= 60; index++) {
                var elapsedSeconds = index * 5.0;
                var driftPerMinute = index <= 30 ? 0.2 : 1.0;
                var declination = index <= 30
                    ? 0.2 / 60.0 * elapsedSeconds
                    : 0.2 / 60.0 * 150.0 + driftPerMinute / 60.0 * (elapsedSeconds - 150.0);
                estimator.TryAddSample(elapsedSeconds, declination);
            }

            var policy = TppaDeclinationDriftTrackPolicy.FieldDefault with {
                MaximumAbsoluteLagOneResidualCorrelation = 0.99
            };
            var result = estimator.Evaluate(policy);

            result.IsValid.Should().BeFalse();
            result.Reason.Should().Contain("half-window");
        }

        [Test]
        public void RejectsStronglyAutocorrelatedResiduals() {
            var estimator = BuildLinearTrack(0.42, index => 0.4 * Math.Sin(index * 0.15));

            var result = estimator.Evaluate();

            result.IsValid.Should().BeFalse();
            result.Reason.Should().Contain("correlation");
        }

        [Test]
        public void RejectsShortTrack() {
            var estimator = new TppaDeclinationDriftTrackEstimator();
            for (var index = 0; index <= 40; index++) {
                estimator.TryAddSample(index * 5.0, index * 0.02);
            }

            var result = estimator.Evaluate();

            result.IsValid.Should().BeFalse();
            result.Reason.Should().Contain("duration");
        }

        [Test]
        public void RejectsNonMonotonicOrNonFiniteSamples() {
            var estimator = new TppaDeclinationDriftTrackEstimator();

            estimator.TryAddSample(1, 0).Should().BeTrue();
            estimator.TryAddSample(1, 1).Should().BeFalse();
            estimator.TryAddSample(double.NaN, 1).Should().BeFalse();
            estimator.TryAddSample(2, double.PositiveInfinity).Should().BeFalse();
        }

        [Test]
        public void QualifiedFitConvertsToGlobalValidationTrack() {
            var estimator = BuildLinearTrack(0.42, index => 0.2 * Math.Sin(index * 1.7));
            var fit = estimator.Evaluate();

            var track = fit.ToValidationTrack("A", -45, 40, 0.05);

            track.PositionId.Should().Be("A");
            track.DeclinationDriftArcsecondsPerMinute.Should().BeApproximately(0.42, 0.01);
            track.ComputedRefractionDriftArcsecondsPerMinute.Should().Be(0.05);
        }

        private static TppaDeclinationDriftTrackEstimator BuildLinearTrack(
            double driftArcsecondsPerMinute,
            Func<int, double> noise) {
            var estimator = new TppaDeclinationDriftTrackEstimator();
            for (var index = 0; index <= 60; index++) {
                var elapsedSeconds = index * 5.0;
                var declination = driftArcsecondsPerMinute / 60.0 * elapsedSeconds + noise(index);
                estimator.TryAddSample(elapsedSeconds, declination);
            }
            return estimator;
        }
    }
}
