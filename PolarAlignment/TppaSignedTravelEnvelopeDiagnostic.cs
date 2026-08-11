using System;

namespace NINA.Plugins.PolarAlignment {
    /// <summary>
    /// Reports the physical-marker consequence of one signed UPAS command. This is
    /// deliberately diagnostic only: it does not grant motion authority or replace
    /// the established travel guards.
    /// </summary>
    internal sealed record TppaSignedTravelEnvelopeDiagnostic(
        bool IsInsideEnvelope,
        double ProjectedPositionDegrees,
        string Reason) {
        public static TppaSignedTravelEnvelopeDiagnostic Evaluate(
            double startingPositionDegrees,
            double minimumDegrees,
            double maximumDegrees,
            double commandUnits,
            double physicalDegreesPerUnit,
            int commandDirectionMultiplier) {
            if (!double.IsFinite(startingPositionDegrees)
                || !double.IsFinite(minimumDegrees)
                || !double.IsFinite(maximumDegrees)
                || !double.IsFinite(commandUnits)
                || !double.IsFinite(physicalDegreesPerUnit)
                || minimumDegrees >= maximumDegrees
                || startingPositionDegrees < minimumDegrees
                || startingPositionDegrees > maximumDegrees
                || physicalDegreesPerUnit <= 0) {
                return new(false, double.NaN, "signed marker diagnostic has an invalid range, start, command, or scale");
            }

            var direction = commandDirectionMultiplier < 0 ? -1 : 1;
            var projected = startingPositionDegrees + commandUnits * physicalDegreesPerUnit * direction;
            var inside = projected >= minimumDegrees && projected <= maximumDegrees;
            var reason = inside
                ? $"signed command projects to {Math.Round(projected, 3)} deg inside {Math.Round(minimumDegrees, 3)}..{Math.Round(maximumDegrees, 3)} deg"
                : $"signed command projects to {Math.Round(projected, 3)} deg outside {Math.Round(minimumDegrees, 3)}..{Math.Round(maximumDegrees, 3)} deg";
            return new(inside, projected, reason);
        }
    }
}
