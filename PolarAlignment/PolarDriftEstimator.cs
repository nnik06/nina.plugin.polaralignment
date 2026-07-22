using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct PolarDriftStabilityPolicy(
        double MinimumDurationSeconds,
        int MinimumSampleCount,
        double MaximumSigmaArcMinutes,
        double HalfSlopeDifferenceFloorArcMinutes,
        double HalfSlopeDifferenceFraction,
        double MaximumHalfDirectionDifferenceDegrees) {
        public static PolarDriftStabilityPolicy FieldDefault => new(
            MinimumDurationSeconds: 600,
            MinimumSampleCount: 200,
            MaximumSigmaArcMinutes: 0.25,
            HalfSlopeDifferenceFloorArcMinutes: 0.5,
            HalfSlopeDifferenceFraction: 0.15,
            MaximumHalfDirectionDifferenceDegrees: 5.0);
    }

    internal readonly record struct PolarDriftEstimate(
        int SampleCount,
        double DurationSeconds,
        double XDriftPixelsPerSecond,
        double YDriftPixelsPerSecond,
        double PolarErrorArcMinutes,
        double PolarErrorSigmaArcMinutes,
        double PoleDirectionCameraDegrees,
        double Phd2DisplayAngleDegrees,
        double HalfSlopeDifferenceArcMinutes,
        double HalfDirectionDifferenceDegrees,
        bool IsStable,
        string Reason);

    /// <summary>
    /// Reproduces PHD2 Polar Drift Align's camera-space least-squares estimate
    /// from passive GuideStep dx/dy samples. This estimator never controls PHD2
    /// or an actuator; orchestration must fail closed around it.
    /// </summary>
    internal sealed class PolarDriftEstimator {
        internal const double SecondsPerRadian = 24.0 * 3600.0 / (2.0 * Math.PI);

        private readonly List<Sample> samples = new();
        private string invalidReason;

        public void Reset() {
            samples.Clear();
            invalidReason = null;
        }

        public void Invalidate(string reason) {
            invalidReason = string.IsNullOrWhiteSpace(reason) ? "capture was invalidated" : reason;
        }

        public bool TryAddSample(double elapsedSeconds, double dxPixels, double dyPixels) {
            if (!double.IsFinite(elapsedSeconds) || !double.IsFinite(dxPixels) || !double.IsFinite(dyPixels)) {
                return false;
            }
            if (samples.Count > 0 && elapsedSeconds <= samples[^1].ElapsedSeconds) {
                return false;
            }

            samples.Add(new Sample(elapsedSeconds, dxPixels, dyPixels));
            return true;
        }

        public PolarDriftEstimate Evaluate(
            double pixelScaleArcsecondsPerPixel,
            int hemisphere,
            int mirror,
            PolarDriftStabilityPolicy? policy = null) {
            var activePolicy = policy ?? PolarDriftStabilityPolicy.FieldDefault;
            if (!string.IsNullOrWhiteSpace(invalidReason)) {
                return Invalid(invalidReason);
            }
            if (!double.IsFinite(pixelScaleArcsecondsPerPixel) || pixelScaleArcsecondsPerPixel <= 0) {
                return Invalid("pixel scale must be finite and positive");
            }
            if (hemisphere is not (-1 or 1) || mirror is not (-1 or 1)) {
                return Invalid("hemisphere and mirror must each be -1 or +1");
            }
            if (samples.Count < 3) {
                return Invalid("at least three samples are required");
            }

            var fit = Fit(samples);
            if (!fit.IsValid) {
                return Invalid("least-squares fit is singular");
            }

            var slopeMagnitude = Magnitude(fit.XSlope, fit.YSlope);
            var errorArcMinutes = slopeMagnitude * SecondsPerRadian * pixelScaleArcsecondsPerPixel / 60.0;
            var sigmaSlope = slopeMagnitude > 0
                ? Math.Sqrt(
                    Math.Pow(fit.XSlope / slopeMagnitude * fit.XSlopeStandardError, 2)
                    + Math.Pow(fit.YSlope / slopeMagnitude * fit.YSlopeStandardError, 2))
                : Magnitude(fit.XSlopeStandardError, fit.YSlopeStandardError);
            var sigmaArcMinutes = sigmaSlope * SecondsPerRadian * pixelScaleArcsecondsPerPixel / 60.0;
            var thetaDegrees = Math.Atan2(fit.YSlope, fit.XSlope) * 180.0 / Math.PI;
            var alphaDegrees = thetaDegrees + hemisphere * 90.0 * mirror;
            var poleDirectionDegrees = NormalizeDegrees(alphaDegrees);
            var phd2DisplayAngleDegrees = NormalizeDegrees(-alphaDegrees);
            var durationSeconds = samples[^1].ElapsedSeconds - samples[0].ElapsedSeconds;

            var midpoint = (samples[0].ElapsedSeconds + samples[^1].ElapsedSeconds) / 2.0;
            var firstHalf = samples.Where(sample => sample.ElapsedSeconds <= midpoint).ToArray();
            var secondHalf = samples.Where(sample => sample.ElapsedSeconds > midpoint).ToArray();
            var firstFit = Fit(firstHalf);
            var secondFit = Fit(secondHalf);
            var halfSlopeDifferenceArcMinutes = firstFit.IsValid && secondFit.IsValid
                ? Magnitude(secondFit.XSlope - firstFit.XSlope, secondFit.YSlope - firstFit.YSlope)
                    * SecondsPerRadian * pixelScaleArcsecondsPerPixel / 60.0
                : double.PositiveInfinity;

            var halfDirectionDifferenceDegrees = firstFit.IsValid && secondFit.IsValid
                ? Math.Abs(NormalizeDegrees(
                    Math.Atan2(secondFit.YSlope, secondFit.XSlope) * 180.0 / Math.PI
                    - Math.Atan2(firstFit.YSlope, firstFit.XSlope) * 180.0 / Math.PI))
                : double.PositiveInfinity;

            var consistencyLimit = Math.Max(
                activePolicy.HalfSlopeDifferenceFloorArcMinutes,
                errorArcMinutes * activePolicy.HalfSlopeDifferenceFraction);
            string reason;
            var stable = false;
            if (durationSeconds < activePolicy.MinimumDurationSeconds) {
                reason = $"capture duration {durationSeconds:F1}s is below {activePolicy.MinimumDurationSeconds:F1}s";
            } else if (samples.Count < activePolicy.MinimumSampleCount) {
                reason = $"sample count {samples.Count} is below {activePolicy.MinimumSampleCount}";
            } else if (!double.IsFinite(sigmaArcMinutes) || sigmaArcMinutes > activePolicy.MaximumSigmaArcMinutes) {
                reason = $"polar-error uncertainty {sigmaArcMinutes:F3}' exceeds {activePolicy.MaximumSigmaArcMinutes:F3}'";
            } else if (halfSlopeDifferenceArcMinutes > consistencyLimit) {
                reason = $"half-window slope disagreement {halfSlopeDifferenceArcMinutes:F3}' exceeds {consistencyLimit:F3}'";
            } else if (halfDirectionDifferenceDegrees > activePolicy.MaximumHalfDirectionDifferenceDegrees) {
                reason = $"half-window direction disagreement {halfDirectionDifferenceDegrees:F2} deg exceeds {activePolicy.MaximumHalfDirectionDifferenceDegrees:F2} deg";
            } else {
                stable = true;
                reason = "duration, sample count, uncertainty, and half-window magnitude/direction consistency gates passed";
            }

            return new PolarDriftEstimate(
                samples.Count,
                durationSeconds,
                fit.XSlope,
                fit.YSlope,
                errorArcMinutes,
                sigmaArcMinutes,
                poleDirectionDegrees,
                phd2DisplayAngleDegrees,
                halfSlopeDifferenceArcMinutes,
                halfDirectionDifferenceDegrees,
                stable,
                reason);
        }

        private PolarDriftEstimate Invalid(string reason) => new(
            samples.Count,
            samples.Count > 1 ? samples[^1].ElapsedSeconds - samples[0].ElapsedSeconds : 0,
            double.NaN,
            double.NaN,
            double.NaN,
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

            var meanT = input.Average(sample => sample.ElapsedSeconds);
            var meanX = input.Average(sample => sample.X);
            var meanY = input.Average(sample => sample.Y);
            var sxx = 0.0;
            var stx = 0.0;
            var sty = 0.0;
            foreach (var sample in input) {
                var dt = sample.ElapsedSeconds - meanT;
                sxx += dt * dt;
                stx += dt * (sample.X - meanX);
                sty += dt * (sample.Y - meanY);
            }
            if (!(sxx > 0) || !double.IsFinite(sxx)) {
                return default;
            }

            var xSlope = stx / sxx;
            var ySlope = sty / sxx;
            var xIntercept = meanX - xSlope * meanT;
            var yIntercept = meanY - ySlope * meanT;
            var xResidualSumSquares = 0.0;
            var yResidualSumSquares = 0.0;
            foreach (var sample in input) {
                var xResidual = sample.X - (xIntercept + xSlope * sample.ElapsedSeconds);
                var yResidual = sample.Y - (yIntercept + ySlope * sample.ElapsedSeconds);
                xResidualSumSquares += xResidual * xResidual;
                yResidualSumSquares += yResidual * yResidual;
            }

            var degreesOfFreedom = input.Count - 2;
            var xSlopeStandardError = Math.Sqrt((xResidualSumSquares / degreesOfFreedom) / sxx);
            var ySlopeStandardError = Math.Sqrt((yResidualSumSquares / degreesOfFreedom) / sxx);
            return new FitResult(true, xSlope, ySlope, xSlopeStandardError, ySlopeStandardError);
        }

        private static double Magnitude(double x, double y) {
            var scale = Math.Max(Math.Abs(x), Math.Abs(y));
            if (scale == 0) {
                return 0;
            }
            var normalizedX = x / scale;
            var normalizedY = y / scale;
            return scale * Math.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY);
        }
        private static double NormalizeDegrees(double degrees) {
            var normalized = degrees % 360.0;
            if (normalized <= -180.0) {
                normalized += 360.0;
            } else if (normalized > 180.0) {
                normalized -= 360.0;
            }
            return normalized;
        }

        private readonly record struct Sample(double ElapsedSeconds, double X, double Y);
        private readonly record struct FitResult(
            bool IsValid,
            double XSlope,
            double YSlope,
            double XSlopeStandardError,
            double YSlopeStandardError);
    }
}