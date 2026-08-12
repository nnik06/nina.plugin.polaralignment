using System;

namespace NINA.Plugins.PolarAlignment {
    /// <summary>
    /// Qualifies one physical UPAS axis for a direct correction without granting
    /// authority over the other axis. This is intentionally conservative: when
    /// the sign of the physical scale is not commissioned, it requires headroom
    /// in both directions from the visually confirmed start position.
    /// </summary>
    internal sealed record TppaDirectAxisRouteQualification(
        bool IsQualified,
        double PlannedUnits,
        string Reason) {
        internal const double MaximumAxisErrorMinutes = 324.0;
        internal const int MaximumMoves = 2;
        private const double MinimumLeverage = 1e-8;
        private const double CorrectionGain = 0.5;

        public static TppaDirectAxisRouteQualification Evaluate(
                bool enabled,
                bool operatorConfirmed,
                double startingPositionDegrees,
                double minimumDegrees,
                double maximumDegrees,
                double physicalDegreesPerUnit,
                double azimuthErrorDeltaPerUnitDegrees,
                double altitudeErrorDeltaPerUnitDegrees,
                double maximumUnitsPerMove,
                double azimuthErrorMinutes,
                double altitudeErrorMinutes) {
            if (!enabled) {
                return Deny("the calibrated direct axis route is not enabled");
            }

            if (!IsFinite(
                    startingPositionDegrees,
                    minimumDegrees,
                    maximumDegrees,
                    physicalDegreesPerUnit,
                    azimuthErrorDeltaPerUnitDegrees,
                    altitudeErrorDeltaPerUnitDegrees,
                    maximumUnitsPerMove,
                    azimuthErrorMinutes,
                    altitudeErrorMinutes)) {
                return Deny("the calibrated direct axis route contains non-finite values");
            }

            if (minimumDegrees >= maximumDegrees
                || startingPositionDegrees < minimumDegrees
                || startingPositionDegrees > maximumDegrees) {
                return Deny("the visual-marker starting position is outside the configured axis envelope");
            }

            if (Math.Abs(physicalDegreesPerUnit) <= 0 || maximumUnitsPerMove <= 0) {
                return Deny("the direct axis route has no physical travel calibration or move limit");
            }

            if (Math.Abs(azimuthErrorMinutes) > MaximumAxisErrorMinutes) {
                return Deny($"the azimuth error {Math.Abs(azimuthErrorMinutes):F2}' exceeds the calibrated direct-axis envelope of {MaximumAxisErrorMinutes:F0}'");
            }

            var leverage = azimuthErrorDeltaPerUnitDegrees * azimuthErrorDeltaPerUnitDegrees
                           + altitudeErrorDeltaPerUnitDegrees * altitudeErrorDeltaPerUnitDegrees;
            if (leverage <= MinimumLeverage) {
                return Deny("the measured direct-axis response is zero or unmeasured");
            }

            var rawUnits = -((azimuthErrorDeltaPerUnitDegrees * azimuthErrorMinutes / 60.0)
                           + (altitudeErrorDeltaPerUnitDegrees * altitudeErrorMinutes / 60.0)) / leverage;
            var plannedUnits = rawUnits * CorrectionGain;
            var requiredMoveCount = (int)Math.Ceiling(Math.Abs(plannedUnits) / maximumUnitsPerMove);
            if (requiredMoveCount > MaximumMoves) {
                return Deny($"the direct-axis correction needs {requiredMoveCount} moves; the five-minute route permits at most {MaximumMoves}");
            }

            var physicalDisplacement = Math.Min(Math.Abs(plannedUnits), maximumUnitsPerMove)
                                       * Math.Abs(physicalDegreesPerUnit);
            if (startingPositionDegrees - physicalDisplacement < minimumDegrees
                || startingPositionDegrees + physicalDisplacement > maximumDegrees) {
                return Deny("the visual-marker axis envelope lacks headroom for the direct-axis correction");
            }

            return new(true, plannedUnits, "the calibrated direct axis route passed response, physical envelope, and five-minute checks");
        }

        private static TppaDirectAxisRouteQualification Deny(string reason) => new(false, 0, reason);

        private static bool IsFinite(params double[] values) {
            foreach (var value in values) {
                if (double.IsNaN(value) || double.IsInfinity(value)) {
                    return false;
                }
            }

            return true;
        }
    }
}
