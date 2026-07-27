using System;
using System.Collections.Generic;
using NINA.Astrometry;

namespace NINA.Plugins.PolarAlignment.Instructions {
    internal static class RefractionAlignmentTarget {
        internal const string TruePoleTarget = "true-celestial-pole";
        internal const string ApparentPoleTarget = "apparent-refracted-pole";
        internal const string AutomatedAdjustmentRequiresTruePoleIssue =
            "Automated polar-axis adjustment requires 'Adjust for refraction' to be enabled so completion targets the true celestial pole.";
        internal const string DriftValidationRequiresTruePoleIssue =
            "Drift-validation mode requires 'Adjust for refraction' to be enabled so TPPA and drift results refer to the same true-pole target.";

        internal static double CalculateTruePoleOffsetArcMinutes(double latitudeDegrees,
                                                                 RefractionParameters refractionParameters) {
            refractionParameters ??= RefractionParameters.GetRefractionParameters();
            var truePoleAltitude = Math.Abs(latitudeDegrees);
            var refractedPoleAltitude = AstroUtil.CalculateRefractedAltitude(truePoleAltitude,
                                                                             refractionParameters.PressureHPa,
                                                                             refractionParameters.Temperature,
                                                                             refractionParameters.RelativeHumidity,
                                                                             refractionParameters.Wavelength);
            if (double.IsNaN(refractedPoleAltitude) || double.IsInfinity(refractedPoleAltitude)) {
                return double.NaN;
            }

            return Math.Abs(refractedPoleAltitude - truePoleAltitude) * 60.0;
        }

        internal static bool IsMaterialToTolerance(double offsetArcMinutes, double toleranceArcMinutes) {
            return toleranceArcMinutes > 0 && offsetArcMinutes > toleranceArcMinutes;
        }

        internal static string GetPoleTarget(bool refractionAdjustmentEnabled) =>
            refractionAdjustmentEnabled ? TruePoleTarget : ApparentPoleTarget;

        internal static IReadOnlyList<string> GetValidationIssues(bool refractionAdjustmentEnabled,
                                                                  bool automatedAdjustmentsEnabled,
                                                                  bool actuatorMovementAllowed,
                                                                  bool driftValidationOnly) {
            var issues = new List<string>();
            if (refractionAdjustmentEnabled) {
                return issues;
            }

            if (automatedAdjustmentsEnabled && actuatorMovementAllowed) {
                issues.Add(AutomatedAdjustmentRequiresTruePoleIssue);
            }
            if (driftValidationOnly) {
                issues.Add(DriftValidationRequiresTruePoleIssue);
            }
            return issues;
        }
    }
}
