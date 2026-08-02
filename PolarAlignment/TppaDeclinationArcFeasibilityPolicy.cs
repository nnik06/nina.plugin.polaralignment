using System;
using System.Collections.Generic;
using System.Globalization;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaDeclinationArcFeasibility(
        bool IsFeasible,
        double PredictedOnSkyLegDegrees,
        double RequiredRaLegDegrees,
        string Reason);

    /// <summary>
    /// Predicts the great-circle separation produced by an RA move at fixed
    /// declination. This is a planning gate; solved geometry remains authoritative.
    /// </summary>
    internal static class TppaDeclinationArcFeasibilityPolicy {
        public const double MinimumOnSkyLegDegrees =
            TppaAbsoluteEvidenceBinder.MinimumQualifiedArcSpanDegrees;

        public static TppaDeclinationArcFeasibility Evaluate(
                double configuredRaLegDegrees,
                double plannedDeclinationDegrees) {
            if (!double.IsFinite(configuredRaLegDegrees)
                    || !double.IsFinite(plannedDeclinationDegrees)
                    || configuredRaLegDegrees < 0
                    || configuredRaLegDegrees > 180
                    || Math.Abs(plannedDeclinationDegrees) > 90) {
                return new TppaDeclinationArcFeasibility(
                    false,
                    double.NaN,
                    double.NaN,
                    "Planned TPPA RA-leg geometry is non-finite or outside celestial bounds.");
            }

            var declinationRadians = DegreesToRadians(plannedDeclinationDegrees);
            var raLegRadians = DegreesToRadians(configuredRaLegDegrees);
            var predicted = RadiansToDegrees(2.0 * Math.Asin(Math.Clamp(
                Math.Abs(Math.Cos(declinationRadians) * Math.Sin(raLegRadians / 2.0)),
                0.0,
                1.0)));
            var denominator = Math.Abs(Math.Cos(declinationRadians));
            var requiredRatio = denominator <= 1e-12
                ? double.PositiveInfinity
                : Math.Sin(DegreesToRadians(MinimumOnSkyLegDegrees) / 2.0)
                    / denominator;
            var required = !double.IsFinite(requiredRatio) || requiredRatio > 1.0
                ? double.PositiveInfinity
                : RadiansToDegrees(2.0 * Math.Asin(Math.Clamp(requiredRatio, 0.0, 1.0)));
            var feasible = predicted + 1e-9 >= MinimumOnSkyLegDegrees;
            var requiredText = double.IsFinite(required)
                ? string.Format(CultureInfo.InvariantCulture, "{0:F2}", required)
                : "no finite amount of";
            var reason = feasible
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "Configured {0:F2}-degree RA leg predicts {1:F2} degrees on sky at declination {2:+0.00;-0.00;0.00} degrees.",
                    configuredRaLegDegrees,
                    predicted,
                    plannedDeclinationDegrees)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "Configured {0:F2}-degree RA leg predicts only {1:F2} degrees on sky at declination {2:+0.00;-0.00;0.00} degrees; the {3:F2}-degree motion floor requires {4} degrees of RA travel. No UPAS movement was authorized.",
                    configuredRaLegDegrees,
                    predicted,
                    plannedDeclinationDegrees,
                    MinimumOnSkyLegDegrees,
                    requiredText);

            return new TppaDeclinationArcFeasibility(feasible, predicted, required, reason);
        }

        private static double DegreesToRadians(double value) => value * Math.PI / 180.0;
        private static double RadiansToDegrees(double value) => value * 180.0 / Math.PI;
    }
}