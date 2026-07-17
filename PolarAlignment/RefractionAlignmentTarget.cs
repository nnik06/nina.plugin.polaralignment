using System;
using NINA.Astrometry;

namespace NINA.Plugins.PolarAlignment.Instructions {
    internal static class RefractionAlignmentTarget {
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
    }
}
