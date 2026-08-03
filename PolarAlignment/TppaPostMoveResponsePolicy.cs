using System;
using System.Globalization;

namespace NINA.Plugins.PolarAlignment {
    internal enum TppaPostMoveResponseClassification {
        ConvergedCandidate,
        Improved,
        Inconclusive,
        Regressed
    }

    internal sealed record TppaPostMoveResponseDecision(
        TppaPostMoveResponseClassification Classification,
        double PreMoveTotalMinutes,
        double PostMoveTotalMinutes,
        double TotalImprovementMinutes,
        double RequiredImprovementMinutes,
        double MaximumComponentWorseningMinutes,
        bool ShouldRunStationaryConfirmation,
        bool CouldAuthorizeAnotherMove,
        string Reason) {
        public string ToLogString() => string.Format(
            CultureInfo.InvariantCulture,
            "schemaVersion=1; classification={0}; preTotal={1:0.000}'; postTotal={2:0.000}'; improvement={3:+0.000;-0.000;0.000}'; requiredImprovement={4:0.000}'; maxComponentWorsening={5:+0.000;-0.000;0.000}'; confirm={6}; couldAuthorizeAnotherMove={7}; reason={8}",
            Classification,
            PreMoveTotalMinutes,
            PostMoveTotalMinutes,
            TotalImprovementMinutes,
            RequiredImprovementMinutes,
            MaximumComponentWorseningMinutes,
            ShouldRunStationaryConfirmation,
            CouldAuthorizeAnotherMove,
            Reason);
    }

    internal sealed record TppaPostMoveResponseDisposition(
        bool UpdateController,
        bool ContinueToStationaryConfirmation,
        string FailureMessage);

    internal static class TppaPostMoveResponsePolicy {
        public const double EvidenceEnvelopeMinutes = 1.5;
        public const double MinimumFractionalImprovement = 0.15;
        public const double ComponentImprovementVetoMinutes = 1.5;
        public const double ComponentRegressionMinutes = 5.0;
        private const double TotalConsistencyToleranceMinutes = 0.1;

        public static TppaPostMoveResponseDecision Evaluate(
                TppaPolarErrorVector preMove,
                TppaPolarErrorVector postMove,
                double targetMinutes) {
            if (!ValidTarget(targetMinutes)
                    || !ValidVector(preMove)
                    || !ValidVector(postMove)) {
                return Decision(
                    TppaPostMoveResponseClassification.Inconclusive,
                    preMove,
                    postMove,
                    targetMinutes,
                    "invalid or internally inconsistent post-move evidence");
            }

            var improvement = preMove.TotalMinutes - postMove.TotalMinutes;
            var requiredImprovement = Math.Max(
                EvidenceEnvelopeMinutes,
                preMove.TotalMinutes * MinimumFractionalImprovement);
            var azimuthWorsening = Math.Abs(postMove.AzimuthMinutes) - Math.Abs(preMove.AzimuthMinutes);
            var altitudeWorsening = Math.Abs(postMove.AltitudeMinutes) - Math.Abs(preMove.AltitudeMinutes);
            var maximumComponentWorsening = Math.Max(azimuthWorsening, altitudeWorsening);

            if (postMove.TotalMinutes <= targetMinutes) {
                return new(
                    TppaPostMoveResponseClassification.ConvergedCandidate,
                    preMove.TotalMinutes,
                    postMove.TotalMinutes,
                    improvement,
                    requiredImprovement,
                    maximumComponentWorsening,
                    ShouldRunStationaryConfirmation: true,
                    CouldAuthorizeAnotherMove: false,
                    "fresh post-move total is at or below the operational target; stationary confirmation is still required");
            }

            if (improvement <= -EvidenceEnvelopeMinutes
                    || maximumComponentWorsening >= ComponentRegressionMinutes) {
                return new(
                    TppaPostMoveResponseClassification.Regressed,
                    preMove.TotalMinutes,
                    postMove.TotalMinutes,
                    improvement,
                    requiredImprovement,
                    maximumComponentWorsening,
                    ShouldRunStationaryConfirmation: false,
                    CouldAuthorizeAnotherMove: false,
                    "fresh post-move evidence shows material total or component regression");
            }

            if (improvement >= requiredImprovement
                    && maximumComponentWorsening < ComponentImprovementVetoMinutes) {
                return new(
                    TppaPostMoveResponseClassification.Improved,
                    preMove.TotalMinutes,
                    postMove.TotalMinutes,
                    improvement,
                    requiredImprovement,
                    maximumComponentWorsening,
                    ShouldRunStationaryConfirmation: false,
                    CouldAuthorizeAnotherMove: true,
                    "fresh post-move evidence shows meaningful improvement outside the preregistered envelope");
            }

            return new(
                TppaPostMoveResponseClassification.Inconclusive,
                preMove.TotalMinutes,
                postMove.TotalMinutes,
                improvement,
                requiredImprovement,
                maximumComponentWorsening,
                ShouldRunStationaryConfirmation: false,
                CouldAuthorizeAnotherMove: false,
                "fresh post-move change is inside the evidence envelope or has component cross-coupling");
        }

        public static TppaPostMoveResponseDisposition DispositionForMode(
                TppaPostMoveResponseDecision decision,
                bool enforceFastRuntimeBudget) {
            if (!enforceFastRuntimeBudget) {
                return new(UpdateController: true, ContinueToStationaryConfirmation: false, FailureMessage: null);
            }

            return decision.Classification switch {
                TppaPostMoveResponseClassification.ConvergedCandidate => new(
                    UpdateController: true,
                    ContinueToStationaryConfirmation: true,
                    FailureMessage: null),
                TppaPostMoveResponseClassification.Improved => new(
                    UpdateController: true,
                    ContinueToStationaryConfirmation: false,
                    FailureMessage: null),
                TppaPostMoveResponseClassification.Inconclusive => new(
                    UpdateController: false,
                    ContinueToStationaryConfirmation: false,
                    FailureMessage: "The one permitted UPAS move did not produce a fresh TPPA improvement outside the evidence envelope. The run stopped without authorizing another move."),
                TppaPostMoveResponseClassification.Regressed => new(
                    UpdateController: false,
                    ContinueToStationaryConfirmation: false,
                    FailureMessage: "The one permitted UPAS move materially regressed the fresh TPPA result. The run stopped without reversing or authorizing another move."),
                _ => new(
                    UpdateController: false,
                    ContinueToStationaryConfirmation: false,
                    FailureMessage: "The post-move TPPA response had an unknown classification. The run stopped without authorizing another move.")
            };
        }

        private static TppaPostMoveResponseDecision Decision(
                TppaPostMoveResponseClassification classification,
                TppaPolarErrorVector preMove,
                TppaPolarErrorVector postMove,
                double targetMinutes,
                string reason) => new(
                    classification,
                    preMove.TotalMinutes,
                    postMove.TotalMinutes,
                    double.NaN,
                    double.NaN,
                    double.NaN,
                    ShouldRunStationaryConfirmation: false,
                    CouldAuthorizeAnotherMove: false,
                    reason);

        private static bool ValidTarget(double targetMinutes) =>
            double.IsFinite(targetMinutes) && targetMinutes > 0.0;

        private static bool ValidVector(TppaPolarErrorVector vector) {
            if (!double.IsFinite(vector.AzimuthMinutes)
                    || !double.IsFinite(vector.AltitudeMinutes)
                    || !double.IsFinite(vector.TotalMinutes)
                    || vector.TotalMinutes < 0.0) {
                return false;
            }

            var calculatedTotal = Math.Sqrt(
                vector.AzimuthMinutes * vector.AzimuthMinutes
                + vector.AltitudeMinutes * vector.AltitudeMinutes);
            return Math.Abs(calculatedTotal - vector.TotalMinutes)
                <= TotalConsistencyToleranceMinutes;
        }
    }
}
