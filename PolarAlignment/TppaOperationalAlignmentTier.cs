using System;

namespace NINA.Plugins.PolarAlignment {
    internal enum TppaOperationalAlignmentTier {
        ImagingReady,
        TripodFreeCoarse,
        NonOperational
    }

    internal sealed record TppaOperationalAlignmentTierDecision(
        TppaOperationalAlignmentTier Tier,
        bool IsImagingReady,
        string CompletionClaim);

    internal static class TppaOperationalAlignmentTierPolicy {
        public const double ImagingReadyMaximumTotalMinutes = 3.0;
        public const double TripodFreeCoarseMaximumTotalMinutes = 24.0;

        public static TppaOperationalAlignmentTierDecision Evaluate(double toleranceMinutes) {
            if (!double.IsFinite(toleranceMinutes) || toleranceMinutes <= 0.0) {
                return new(
                    TppaOperationalAlignmentTier.NonOperational,
                    false,
                    "invalid alignment tolerance; no operational completion claim");
            }

            if (toleranceMinutes <= ImagingReadyMaximumTotalMinutes) {
                return new(
                    TppaOperationalAlignmentTier.ImagingReady,
                    true,
                    "imaging-ready TPPA alignment; independent on-sky outcome verification remains required");
            }

            if (toleranceMinutes <= TripodFreeCoarseMaximumTotalMinutes) {
                return new(
                    TppaOperationalAlignmentTier.TripodFreeCoarse,
                    false,
                    "tripod-free coarse alignment; this result is not imaging readiness");
            }

            return new(
                TppaOperationalAlignmentTier.NonOperational,
                false,
                "tolerance exceeds the 24 arcmin tripod-free operational tier; no operational completion claim");
        }
    }
}
