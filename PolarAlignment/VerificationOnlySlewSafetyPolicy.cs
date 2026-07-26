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

        public static VerificationOnlySlewSafetyResult EvaluateActualTelemetry(
            bool envelopeEnabled,
            TppaMountMotionEnvelope envelope,
            bool mountConnected,
            bool mountSlewing,
            double actualAzimuthDegrees,
            double actualAltitudeDegrees) {
            if (!mountConnected) {
                return new(false, "post-slew mount telemetry is unavailable");
            }

            if (mountSlewing) {
                return new(false, "post-slew mount telemetry still reports slewing");
            }

            if (!double.IsFinite(actualAzimuthDegrees) || !double.IsFinite(actualAltitudeDegrees)) {
                return new(false, "post-slew mount position is not finite");
            }

            if (!envelopeEnabled) {
                return new(
                    true,
                    $"stationary post-slew telemetry accepted at Az={actualAzimuthDegrees:F2} deg, Alt={actualAltitudeDegrees:F2} deg");
            }

            var envelopeFailure = envelope.Validate(actualAzimuthDegrees, actualAltitudeDegrees);
            return string.IsNullOrEmpty(envelopeFailure)
                ? new(
                    true,
                    $"stationary post-slew telemetry is inside the configured envelope at Az={actualAzimuthDegrees:F2} deg, Alt={actualAltitudeDegrees:F2} deg")
                : new(false, $"post-slew telemetry violates the configured envelope: {envelopeFailure}");
        }

    }
}
