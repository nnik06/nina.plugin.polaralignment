using System;
using System.Collections.Generic;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaArcEnvelopeSample(
        string Name,
        double AzimuthDegrees,
        double AltitudeDegrees);

    internal readonly record struct TppaAutomatedArcEnvelopeResult(
        bool IsSafe,
        string Reason);

    internal static class TppaAutomatedArcEnvelopePolicy {
        public static TppaAutomatedArcEnvelopeResult Evaluate(
                bool envelopeEnabled,
                TppaMountMotionEnvelope envelope,
                IReadOnlyList<TppaArcEnvelopeSample> samples) {
            if (!envelopeEnabled) {
                return new(true, "mount-motion envelope is disabled");
            }

            if (samples == null || samples.Count == 0) {
                return new(false, "automated TPPA arc has no predicted samples");
            }

            foreach (var sample in samples) {
                var violation = envelope.Validate(sample.AzimuthDegrees, sample.AltitudeDegrees);
                if (!string.IsNullOrWhiteSpace(violation)) {
                    return new(false, $"predicted {sample.Name} sample {violation}");
                }
            }

            return new(true, "every predicted automated TPPA sample is inside the configured mount-motion envelope");
        }
    }
}
