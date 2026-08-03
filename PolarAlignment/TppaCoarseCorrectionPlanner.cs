using System;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaCoarseCorrectionDecision(
        bool IsAuthorized,
        bool RequiresMove,
        double RequestedDeltaDegrees,
        double RequestUncertaintyDegrees,
        double ProjectedResidualMinutes,
        string Reason);

    internal sealed record TppaAxisResponseCalibration(
        Guid CalibrationId,
        string CoordinateConvention,
        string ErrorEquation,
        double ErrorDeltaDegreesPerPhysicalDegree,
        double ResponseRelativeUncertainty,
        double FixedCommandUncertaintyDegrees);

    internal static class TppaCoarseCorrectionPlanner {
        public const double MaximumObjectiveErrorMinutes = 240.0;
        public const double FineControllerHandoffMinutes = 24.0;
        public const double PhysicalHardLimitDegrees = 5.4;
        public const double MinimumReservedTravelDegrees = 1.0;
        public const double CoarseCorrectionFraction = 0.90;
        public const string RequiredCoordinateConvention = "azEastPositive_altUpPositive";
        public const string RequiredErrorEquation =
            "tppaErrorAfter=tppaErrorBefore+response*physicalDelta";

        public static TppaCoarseCorrectionDecision PlanAxis(
                double errorMinutes,
                double witnessedPositionDegrees,
                double witnessUncertaintyDegrees,
                TppaAxisResponseCalibration calibration) {
            RequireFinite(errorMinutes, nameof(errorMinutes));
            RequireFinite(witnessedPositionDegrees, nameof(witnessedPositionDegrees));
            RequireNonNegativeFinite(witnessUncertaintyDegrees, nameof(witnessUncertaintyDegrees));
            if (calibration == null) {
                throw new ArgumentNullException(nameof(calibration));
            }
            RequireFinite(calibration.ErrorDeltaDegreesPerPhysicalDegree, nameof(calibration));
            RequireNonNegativeFinite(calibration.ResponseRelativeUncertainty, nameof(calibration));
            RequireNonNegativeFinite(calibration.FixedCommandUncertaintyDegrees, nameof(calibration));
            if (calibration.CalibrationId == Guid.Empty
                    || calibration.CoordinateConvention != RequiredCoordinateConvention
                    || calibration.ErrorEquation != RequiredErrorEquation
                    || calibration.ErrorDeltaDegreesPerPhysicalDegree <= 0.0) {
                return Denied("signed physical-coordinate response calibration is missing or incompatible");
            }

            var magnitudeMinutes = Math.Abs(errorMinutes);
            if (magnitudeMinutes > MaximumObjectiveErrorMinutes) {
                return Denied($"axis error {magnitudeMinutes:F2}' exceeds the {MaximumObjectiveErrorMinutes:F0}' coarse handoff envelope");
            }
            if (Math.Abs(witnessedPositionDegrees) + witnessUncertaintyDegrees
                    > PhysicalHardLimitDegrees - MinimumReservedTravelDegrees) {
                return Denied("witnessed UPAS position does not preserve the required one-degree travel reserve");
            }
            if (magnitudeMinutes <= FineControllerHandoffMinutes) {
                return new(true, false, 0.0, 0.0, magnitudeMinutes,
                    $"axis error is already inside the {FineControllerHandoffMinutes:F0}' fine-controller envelope");
            }

            var errorDegrees = errorMinutes / 60.0;
            var requestedDelta = -CoarseCorrectionFraction * errorDegrees
                / calibration.ErrorDeltaDegreesPerPhysicalDegree;
            var requestUncertainty = Math.Abs(requestedDelta) * calibration.ResponseRelativeUncertainty
                + calibration.FixedCommandUncertaintyDegrees;
            var projectedPosition = witnessedPositionDegrees + requestedDelta;
            var guardedExtent = Math.Abs(projectedPosition)
                + witnessUncertaintyDegrees
                + requestUncertainty;
            if (guardedExtent > PhysicalHardLimitDegrees - MinimumReservedTravelDegrees) {
                return Denied(
                    $"planned endpoint plus uncertainty is {guardedExtent:F3} degrees from zero and would consume the required one-degree reserve");
            }

            var projectedResidual = Math.Abs(
                errorDegrees + requestedDelta * calibration.ErrorDeltaDegreesPerPhysicalDegree) * 60.0;
            return new(true, true, requestedDelta, requestUncertainty, projectedResidual,
                $"bounded coarse correction uses signed calibration {calibration.CalibrationId:D} and preserves witnessed headroom; supervisor verification is still required");
        }

        private static TppaCoarseCorrectionDecision Denied(string reason) =>
            new(false, false, 0.0, 0.0, double.NaN, reason);

        private static void RequireFinite(double value, string name) {
            if (!double.IsFinite(value)) {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static void RequireNonNegativeFinite(double value, string name) {
            RequireFinite(value, name);
            if (value < 0.0) {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }
}