using System;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct FreshPolarAlignmentAgreement(
        bool IsRepeatable,
        double AzimuthDeltaMinutes,
        double AltitudeDeltaMinutes,
        double TotalDeltaMinutes,
        double VectorDeltaMinutes,
        double ThresholdMinutes,
        string Reason);

    internal static class FreshPolarAlignmentAgreementPolicy {
        private const double MinimumThresholdMinutes = 0.5;
        private const double CoarseAcquisitionRelativeThreshold = 0.03;
        private const double MaximumCoarseAcquisitionThresholdMinutes = 3.0;
        private const double MinimumReciprocitySpanSeconds = 1.0;

        public static FreshPolarAlignmentAgreement Evaluate(
            double firstAzimuthMinutes,
            double firstAltitudeMinutes,
            double firstTotalMinutes,
            double secondAzimuthMinutes,
            double secondAltitudeMinutes,
            double secondTotalMinutes,
            double toleranceMinutes) {
            var thresholdMinutes = double.IsFinite(toleranceMinutes) && toleranceMinutes > 0
                ? Math.Max(MinimumThresholdMinutes, toleranceMinutes)
                : MinimumThresholdMinutes;
            var values = new[] {
                firstAzimuthMinutes, firstAltitudeMinutes, firstTotalMinutes,
                secondAzimuthMinutes, secondAltitudeMinutes, secondTotalMinutes
            };
            if (Array.Exists(values, value => !double.IsFinite(value))) {
                return new FreshPolarAlignmentAgreement(false, double.NaN, double.NaN, double.NaN, double.NaN,
                    thresholdMinutes, "one or more measurements were not finite");
            }

            var azimuthDeltaMinutes = secondAzimuthMinutes - firstAzimuthMinutes;
            var altitudeDeltaMinutes = secondAltitudeMinutes - firstAltitudeMinutes;
            var totalDeltaMinutes = secondTotalMinutes - firstTotalMinutes;
            var vectorDeltaMinutes = Math.Sqrt(azimuthDeltaMinutes * azimuthDeltaMinutes
                + altitudeDeltaMinutes * altitudeDeltaMinutes);
            var isRepeatable = vectorDeltaMinutes <= thresholdMinutes;
            var reason = isRepeatable
                ? "signed azimuth/altitude vector delta is within the repeatability threshold"
                : "signed azimuth/altitude vector delta exceeds the repeatability threshold";
            return new FreshPolarAlignmentAgreement(isRepeatable, azimuthDeltaMinutes, altitudeDeltaMinutes,
                totalDeltaMinutes, vectorDeltaMinutes, thresholdMinutes, reason);
        }

        /// <summary>
        /// Applies a bounded relative agreement allowance only before the first coarse actuator correction.
        /// Completion and post-move verification must continue to use <see cref="Evaluate"/>.
        /// </summary>
        public static FreshPolarAlignmentAgreement EvaluateForCoarseAcquisition(
            double firstAzimuthMinutes,
            double firstAltitudeMinutes,
            double firstTotalMinutes,
            double secondAzimuthMinutes,
            double secondAltitudeMinutes,
            double secondTotalMinutes,
            double toleranceMinutes) {
            var averageTotalMinutes = (Math.Abs(firstTotalMinutes) + Math.Abs(secondTotalMinutes)) / 2.0;
            var coarseThresholdMinutes = Math.Min(
                MaximumCoarseAcquisitionThresholdMinutes,
                Math.Max(
                    double.IsFinite(toleranceMinutes) && toleranceMinutes > 0
                        ? Math.Max(MinimumThresholdMinutes, toleranceMinutes)
                        : MinimumThresholdMinutes,
                    averageTotalMinutes * CoarseAcquisitionRelativeThreshold));
            var result = Evaluate(
                firstAzimuthMinutes,
                firstAltitudeMinutes,
                firstTotalMinutes,
                secondAzimuthMinutes,
                secondAltitudeMinutes,
                secondTotalMinutes,
                coarseThresholdMinutes);
            return result with {
                Reason = result.IsRepeatable
                    ? "signed azimuth/altitude vector delta is within the bounded coarse-acquisition repeatability threshold"
                    : "signed azimuth/altitude vector delta exceeds the bounded coarse-acquisition repeatability threshold"
            };
        }

        public static FreshPolarAlignmentAgreement EvaluateCenteredReciprocity(
            double firstForwardAzimuthMinutes,
            double firstForwardAltitudeMinutes,
            double reciprocalAzimuthMinutes,
            double reciprocalAltitudeMinutes,
            double repeatedForwardAzimuthMinutes,
            double repeatedForwardAltitudeMinutes,
            double toleranceMinutes) {
            var centeredForwardAzimuth = (firstForwardAzimuthMinutes + repeatedForwardAzimuthMinutes) / 2.0;
            var centeredForwardAltitude = (firstForwardAltitudeMinutes + repeatedForwardAltitudeMinutes) / 2.0;
            var centeredForwardTotal = Math.Sqrt(centeredForwardAzimuth * centeredForwardAzimuth
                + centeredForwardAltitude * centeredForwardAltitude);
            var reciprocalTotal = Math.Sqrt(reciprocalAzimuthMinutes * reciprocalAzimuthMinutes
                + reciprocalAltitudeMinutes * reciprocalAltitudeMinutes);
            return Evaluate(centeredForwardAzimuth, centeredForwardAltitude, centeredForwardTotal,
                reciprocalAzimuthMinutes, reciprocalAltitudeMinutes, reciprocalTotal, toleranceMinutes);
        }

        public static FreshPolarAlignmentAgreement EvaluateTimeCenteredReciprocity(
            double firstForwardAzimuthMinutes,
            double firstForwardAltitudeMinutes,
            DateTime firstForwardObservationTimeUtc,
            double reciprocalAzimuthMinutes,
            double reciprocalAltitudeMinutes,
            DateTime reciprocalObservationTimeUtc,
            double repeatedForwardAzimuthMinutes,
            double repeatedForwardAltitudeMinutes,
            DateTime repeatedForwardObservationTimeUtc,
            double toleranceMinutes) {
            var values = new[] {
                firstForwardAzimuthMinutes, firstForwardAltitudeMinutes,
                reciprocalAzimuthMinutes, reciprocalAltitudeMinutes,
                repeatedForwardAzimuthMinutes, repeatedForwardAltitudeMinutes
            };
            var thresholdMinutes = double.IsFinite(toleranceMinutes) && toleranceMinutes > 0
                ? Math.Max(MinimumThresholdMinutes, toleranceMinutes)
                : MinimumThresholdMinutes;
            if (firstForwardObservationTimeUtc.Kind != DateTimeKind.Utc
                    || reciprocalObservationTimeUtc.Kind != DateTimeKind.Utc
                    || repeatedForwardObservationTimeUtc.Kind != DateTimeKind.Utc) {
                return new FreshPolarAlignmentAgreement(
                    false, double.NaN, double.NaN, double.NaN, double.NaN, thresholdMinutes,
                    "reciprocity observation timestamps must use DateTimeKind.Utc");
            }
            if (reciprocalObservationTimeUtc <= firstForwardObservationTimeUtc
                    || repeatedForwardObservationTimeUtc <= reciprocalObservationTimeUtc) {
                return new FreshPolarAlignmentAgreement(
                    false, double.NaN, double.NaN, double.NaN, double.NaN, thresholdMinutes,
                    "reciprocity observation timestamps must be strictly increasing");
            }
            if (Array.Exists(values, value => !double.IsFinite(value))) {
                return new FreshPolarAlignmentAgreement(
                    false, double.NaN, double.NaN, double.NaN, double.NaN, thresholdMinutes,
                    "one or more reciprocity measurements were not finite");
            }

            var fullSpanSeconds =
                (repeatedForwardObservationTimeUtc - firstForwardObservationTimeUtc).TotalSeconds;
            if (fullSpanSeconds < MinimumReciprocitySpanSeconds) {
                return new FreshPolarAlignmentAgreement(
                    false, double.NaN, double.NaN, double.NaN, double.NaN, thresholdMinutes,
                    $"reciprocity observation span must be at least {MinimumReciprocitySpanSeconds:F1} second");
            }
            var reciprocalOffsetSeconds =
                (reciprocalObservationTimeUtc - firstForwardObservationTimeUtc).TotalSeconds;
            var interpolationFraction = reciprocalOffsetSeconds / fullSpanSeconds;
            var centeredForwardAzimuth = firstForwardAzimuthMinutes
                + interpolationFraction * (repeatedForwardAzimuthMinutes - firstForwardAzimuthMinutes);
            var centeredForwardAltitude = firstForwardAltitudeMinutes
                + interpolationFraction * (repeatedForwardAltitudeMinutes - firstForwardAltitudeMinutes);
            var centeredForwardTotal = Math.Sqrt(centeredForwardAzimuth * centeredForwardAzimuth
                + centeredForwardAltitude * centeredForwardAltitude);
            var reciprocalTotal = Math.Sqrt(reciprocalAzimuthMinutes * reciprocalAzimuthMinutes
                + reciprocalAltitudeMinutes * reciprocalAltitudeMinutes);
            return Evaluate(centeredForwardAzimuth, centeredForwardAltitude, centeredForwardTotal,
                reciprocalAzimuthMinutes, reciprocalAltitudeMinutes, reciprocalTotal, toleranceMinutes);
        }
    }
    internal enum AutomatedAlignmentCompletionDecision {
        ContinueCorrection,
        ValidateWithoutMoving,
        VerifyFreshThreePoint,
        AbortAfterFreshVerificationFailures,
        Finish
    }

    /// <summary>
    /// Prevents one optimistic post-move solve from ending automated alignment.
    /// The UPAS is held stationary while a second below-tolerance solve confirms
    /// that the result is repeatable.
    /// </summary>
    internal sealed class AutomatedAlignmentCompletionGuard {
        private const int MaximumFailedFreshVerifications = 1;
        private int consecutiveBelowToleranceObservations;
        private int failedFreshVerifications;

        public AutomatedAlignmentCompletionDecision Evaluate(double totalErrorMinutes, double toleranceMinutes) {
            var isBelowTolerance = IsBelowTolerance(totalErrorMinutes, toleranceMinutes);
            if (!isBelowTolerance) {
                consecutiveBelowToleranceObservations = 0;
                return AutomatedAlignmentCompletionDecision.ContinueCorrection;
            }

            consecutiveBelowToleranceObservations++;
            return consecutiveBelowToleranceObservations >= 2
                ? AutomatedAlignmentCompletionDecision.VerifyFreshThreePoint
                : AutomatedAlignmentCompletionDecision.ValidateWithoutMoving;
        }

        public AutomatedAlignmentCompletionDecision EvaluateFreshVerification(double totalErrorMinutes, double toleranceMinutes) {
            var isBelowTolerance = IsBelowTolerance(totalErrorMinutes, toleranceMinutes);
            consecutiveBelowToleranceObservations = 0;
            if (isBelowTolerance) {
                failedFreshVerifications = 0;
                return AutomatedAlignmentCompletionDecision.Finish;
            }

            failedFreshVerifications++;
            return failedFreshVerifications >= MaximumFailedFreshVerifications
                ? AutomatedAlignmentCompletionDecision.AbortAfterFreshVerificationFailures
                : AutomatedAlignmentCompletionDecision.ContinueCorrection;
        }
        private static bool IsBelowTolerance(double totalErrorMinutes, double toleranceMinutes) {
            return double.IsFinite(totalErrorMinutes)
                && totalErrorMinutes >= 0
                && double.IsFinite(toleranceMinutes)
                && toleranceMinutes > 0
                && totalErrorMinutes <= toleranceMinutes;
        }
    }
}
