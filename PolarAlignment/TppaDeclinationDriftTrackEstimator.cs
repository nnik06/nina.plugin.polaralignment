using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaDeclinationDriftTrackPolicy(
        double MinimumDurationSeconds,
        int MinimumSampleCount,
        double OutlierSigmaThreshold,
        double MaximumOutlierFraction,
        double MaximumSlopeSigmaArcsecondsPerMinute,
        double HalfSlopeDifferenceFloorArcsecondsPerMinute,
        double HalfSlopeDifferenceFraction,
        double MaximumAbsoluteLagOneResidualCorrelation) {
        public static TppaDeclinationDriftTrackPolicy FieldDefault => new(
            MinimumDurationSeconds: 300,
            MinimumSampleCount: 8,
            OutlierSigmaThreshold: 4,
            MaximumOutlierFraction: 0.15,
            MaximumSlopeSigmaArcsecondsPerMinute: 0.20,
            HalfSlopeDifferenceFloorArcsecondsPerMinute: 0.15,
            HalfSlopeDifferenceFraction: 0.35,
            MaximumAbsoluteLagOneResidualCorrelation: 0.65);
    }

    internal readonly record struct TppaDeclinationDriftTrackFit(
        int InputSampleCount,
        int AcceptedSampleCount,
        double DurationSeconds,
        double DeclinationDriftArcsecondsPerMinute,
        double DeclinationDriftSigmaArcsecondsPerMinute,
        double ResidualRmsArcseconds,
        double LagOneResidualCorrelation,
        double HalfSlopeDifferenceArcsecondsPerMinute,
        bool IsValid,
        string Reason) {
        public TppaDriftTrack ToValidationTrack(
            string positionId,
            double hourAngleDegrees,
            double altitudeDegrees,
            double computedRefractionDriftArcsecondsPerMinute) => new(
                positionId,
                hourAngleDegrees,
                altitudeDegrees,
                DurationSeconds,
                AcceptedSampleCount,
                DeclinationDriftArcsecondsPerMinute,
                DeclinationDriftSigmaArcsecondsPerMinute,
                computedRefractionDriftArcsecondsPerMinute);
    }

    /// <summary>
    /// Fits a qualified declination slope from timestamped plate solves. Input
    /// declinations must use one continuous local arcsecond coordinate.
    /// </summary>
    internal sealed class TppaDeclinationDriftTrackEstimator {
        private readonly List<Sample> samples = new();
        private string invalidReason;

        public bool TryAddSample(double elapsedSeconds, double declinationArcseconds) {
            if (!double.IsFinite(elapsedSeconds) || !double.IsFinite(declinationArcseconds)) {
                return false;
            }
            if (samples.Count > 0 && elapsedSeconds <= samples[^1].ElapsedSeconds) {
                return false;
            }

            samples.Add(new Sample(elapsedSeconds, declinationArcseconds));
            return true;
        }

        public void Invalidate(string reason) {
            invalidReason = string.IsNullOrWhiteSpace(reason) ? "track was invalidated" : reason;
        }

        public TppaDeclinationDriftTrackFit Evaluate(TppaDeclinationDriftTrackPolicy? policy = null) {
            var activePolicy = policy ?? TppaDeclinationDriftTrackPolicy.FieldDefault;
            if (!string.IsNullOrWhiteSpace(invalidReason)) {
                return Invalid(invalidReason);
            }
            if (samples.Count < 3) {
                return Invalid("at least three samples are required");
            }

            var durationSeconds = samples[^1].ElapsedSeconds - samples[0].ElapsedSeconds;
            if (durationSeconds < activePolicy.MinimumDurationSeconds) {
                return Invalid($"track duration {durationSeconds:F1}s is below {activePolicy.MinimumDurationSeconds:F1}s");
            }
            if (samples.Count < activePolicy.MinimumSampleCount) {
                return Invalid($"sample count {samples.Count} is below {activePolicy.MinimumSampleCount}");
            }

            var initialFit = Fit(samples);
            if (!initialFit.IsValid) {
                return Invalid("initial declination fit is singular");
            }
            var initialResiduals = Residuals(samples, initialFit);
            var robustSigma = RobustSigma(initialResiduals);
            var accepted = robustSigma > 0
                ? samples.Where((_, index) => Math.Abs(initialResiduals[index]) <= activePolicy.OutlierSigmaThreshold * robustSigma).ToArray()
                : samples.ToArray();
            var rejectedFraction = 1.0 - (double)accepted.Length / samples.Count;
            if (rejectedFraction > activePolicy.MaximumOutlierFraction) {
                return Invalid($"outlier fraction {rejectedFraction:P1} exceeds {activePolicy.MaximumOutlierFraction:P1}");
            }
            if (accepted.Length < activePolicy.MinimumSampleCount) {
                return Invalid($"only {accepted.Length} samples remain after outlier rejection");
            }

            var fit = Fit(accepted);
            if (!fit.IsValid) {
                return Invalid("qualified declination fit is singular");
            }
            var residuals = Residuals(accepted, fit);
            var residualRms = Math.Sqrt(residuals.Average(residual => residual * residual));
            var lagOneCorrelation = LagOneCorrelation(residuals);
            var correlationInflation = lagOneCorrelation > 0
                ? Math.Sqrt((1.0 + lagOneCorrelation) / Math.Max(0.05, 1.0 - lagOneCorrelation))
                : 1.0;
            var slopeSigmaArcsecondsPerMinute = fit.SlopeStandardErrorArcsecondsPerSecond * 60.0 * correlationInflation;
            var slopeArcsecondsPerMinute = fit.SlopeArcsecondsPerSecond * 60.0;

            var midpoint = (accepted[0].ElapsedSeconds + accepted[^1].ElapsedSeconds) / 2.0;
            var firstFit = Fit(accepted.Where(sample => sample.ElapsedSeconds <= midpoint).ToArray());
            var secondFit = Fit(accepted.Where(sample => sample.ElapsedSeconds > midpoint).ToArray());
            var halfSlopeDifference = firstFit.IsValid && secondFit.IsValid
                ? Math.Abs(secondFit.SlopeArcsecondsPerSecond - firstFit.SlopeArcsecondsPerSecond) * 60.0
                : double.PositiveInfinity;
            var halfSlopeLimit = Math.Max(
                activePolicy.HalfSlopeDifferenceFloorArcsecondsPerMinute,
                Math.Abs(slopeArcsecondsPerMinute) * activePolicy.HalfSlopeDifferenceFraction);

            if (!double.IsFinite(slopeSigmaArcsecondsPerMinute)
                    || slopeSigmaArcsecondsPerMinute > activePolicy.MaximumSlopeSigmaArcsecondsPerMinute) {
                return Result(false,
                    $"slope uncertainty {slopeSigmaArcsecondsPerMinute:F3} arcsec/min exceeds {activePolicy.MaximumSlopeSigmaArcsecondsPerMinute:F3} arcsec/min");
            }
            if (Math.Abs(lagOneCorrelation) > activePolicy.MaximumAbsoluteLagOneResidualCorrelation) {
                return Result(false,
                    $"lag-one residual correlation {lagOneCorrelation:F3} exceeds +/-{activePolicy.MaximumAbsoluteLagOneResidualCorrelation:F3}");
            }
            if (halfSlopeDifference > halfSlopeLimit) {
                return Result(false,
                    $"half-window slope disagreement {halfSlopeDifference:F3} arcsec/min exceeds {halfSlopeLimit:F3} arcsec/min");
            }
            return Result(true, "duration, sample count, outlier, uncertainty, correlation, and half-window gates passed");

            TppaDeclinationDriftTrackFit Result(bool valid, string reason) => new(
                samples.Count,
                accepted.Length,
                durationSeconds,
                slopeArcsecondsPerMinute,
                slopeSigmaArcsecondsPerMinute,
                residualRms,
                lagOneCorrelation,
                halfSlopeDifference,
                valid,
                reason);
        }

        private TppaDeclinationDriftTrackFit Invalid(string reason) => new(
            samples.Count,
            0,
            samples.Count > 1 ? samples[^1].ElapsedSeconds - samples[0].ElapsedSeconds : 0,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            false,
            reason);

        private static FitResult Fit(IReadOnlyList<Sample> input) {
            if (input.Count < 3) {
                return default;
            }

            var meanTime = input.Average(sample => sample.ElapsedSeconds);
            var meanDeclination = input.Average(sample => sample.DeclinationArcseconds);
            var timeSumSquares = 0.0;
            var timeDeclinationSum = 0.0;
            foreach (var sample in input) {
                var centeredTime = sample.ElapsedSeconds - meanTime;
                timeSumSquares += centeredTime * centeredTime;
                timeDeclinationSum += centeredTime * (sample.DeclinationArcseconds - meanDeclination);
            }
            if (!(timeSumSquares > 0) || !double.IsFinite(timeSumSquares)) {
                return default;
            }

            var slope = timeDeclinationSum / timeSumSquares;
            var intercept = meanDeclination - slope * meanTime;
            var residualSumSquares = 0.0;
            foreach (var sample in input) {
                var residual = sample.DeclinationArcseconds - (intercept + slope * sample.ElapsedSeconds);
                residualSumSquares += residual * residual;
            }
            var degreesOfFreedom = input.Count - 2;
            var slopeStandardError = Math.Sqrt((residualSumSquares / degreesOfFreedom) / timeSumSquares);
            return new FitResult(true, slope, intercept, slopeStandardError);
        }

        private static double[] Residuals(IReadOnlyList<Sample> input, FitResult fit) =>
            input.Select(sample => sample.DeclinationArcseconds
                - (fit.InterceptArcseconds + fit.SlopeArcsecondsPerSecond * sample.ElapsedSeconds)).ToArray();

        private static double RobustSigma(IReadOnlyList<double> residuals) {
            var median = Median(residuals);
            var deviations = residuals.Select(residual => Math.Abs(residual - median)).ToArray();
            return 1.4826 * Median(deviations);
        }

        private static double Median(IReadOnlyList<double> values) {
            if (values.Count == 0) {
                return double.NaN;
            }
            var ordered = values.OrderBy(value => value).ToArray();
            var middle = ordered.Length / 2;
            return ordered.Length % 2 == 0
                ? (ordered[middle - 1] + ordered[middle]) / 2.0
                : ordered[middle];
        }

        private static double LagOneCorrelation(IReadOnlyList<double> residuals) {
            if (residuals.Count < 3) {
                return double.NaN;
            }
            var mean = residuals.Average();
            var denominator = residuals.Sum(residual => (residual - mean) * (residual - mean));
            if (!(denominator > 0)) {
                return 0;
            }
            var numerator = 0.0;
            for (var index = 1; index < residuals.Count; index++) {
                numerator += (residuals[index - 1] - mean) * (residuals[index] - mean);
            }
            return numerator / denominator;
        }

        private readonly record struct Sample(double ElapsedSeconds, double DeclinationArcseconds);
        private readonly record struct FitResult(
            bool IsValid,
            double SlopeArcsecondsPerSecond,
            double InterceptArcseconds,
            double SlopeStandardErrorArcsecondsPerSecond);
    }
}
