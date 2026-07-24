using NINA.Core.Enum;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct VerificationOnlySlewSafetyResult(bool IsSafe, string Reason);

    internal static class VerificationOnlySlewSafetyPolicy {
        public static VerificationOnlySlewSafetyResult Evaluate(
            bool envelopeEnabled,
            TppaMountMotionEnvelope envelope,
            double destinationAzimuthDegrees,
            double destinationAltitudeDegrees,
            PierSide currentPierSide,
            PierSide destinationPierSide) {
            if (envelopeEnabled) {
                var envelopeFailure = envelope.Validate(
                    destinationAzimuthDegrees,
                    destinationAltitudeDegrees);
                if (!string.IsNullOrEmpty(envelopeFailure)) {
                    return new(false, envelopeFailure);
                }
            }

            if (currentPierSide == PierSide.pierUnknown || destinationPierSide == PierSide.pierUnknown) {
                return new(false, "pier-side verification is unavailable");
            }

            if (currentPierSide != destinationPierSide) {
                return new(
                    false,
                    $"predicted destination pier side changes from {currentPierSide} to {destinationPierSide}");
            }

            return new(
                true,
                $"destination envelope and constant {currentPierSide} pier-side gates passed");
        }


    }
}
