using System;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaPhysicalZeroAdmissionDecision(
        bool IsEligible,
        double AzimuthAbsoluteBoundDegrees,
        double AltitudeAbsoluteBoundDegrees,
        string EvidenceId,
        string Reason);

    internal static class TppaPhysicalZeroAdmissionPolicy {
        public const double ZeroToleranceDegrees = 0.1;
        public const double SigmaMultiplier = 3.0;

        public static TppaPhysicalZeroAdmissionDecision Evaluate(
                UpasSupervisorCoarsePlanningEvidence evidence) {
            if (evidence == null) {
                throw new ArgumentNullException(nameof(evidence));
            }

            var azimuthBound = ConservativeAbsoluteBound(evidence.Axes.Azimuth);
            var altitudeBound = ConservativeAbsoluteBound(evidence.Axes.Altitude);
            var eligible = azimuthBound <= ZeroToleranceDegrees
                && altitudeBound <= ZeroToleranceDegrees;
            return new(
                eligible,
                azimuthBound,
                altitudeBound,
                evidence.Envelope.EvidenceId,
                eligible
                    ? $"fresh physical evidence places both UPAS axes within {ZeroToleranceDegrees:F3} deg of zero"
                    : $"fresh physical evidence does not place both UPAS axes within {ZeroToleranceDegrees:F3} deg of zero "
                        + $"(AZ bound {azimuthBound:F3} deg, ALT bound {altitudeBound:F3} deg)");
        }

        private static double ConservativeAbsoluteBound(UpasSupervisorAxisEvidence axis) =>
            Math.Abs(axis.PositionDegrees)
                + SigmaMultiplier * axis.PositionStandardUncertaintyDegrees;
    }
}
