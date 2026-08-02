namespace NINA.Plugins.PolarAlignment;

internal sealed record TppaOagGeometryBound(
    double MainPixelScaleUsedArcseconds,
    double GuidePixelScaleUsedArcseconds,
    double CenterSeparationArcseconds,
    double CenterSeparationMainPixels,
    double MainHalfDiagonalPixels,
    string GuideRadialEvidenceKind,
    double GuideRadialPixels,
    double GuideRadialArcseconds,
    double GuideRadialMainPixels,
    double GuideToFarthestMainCornerUpperBoundPixels);

internal static class TppaOagGeometryBoundCalculator {
    public static TppaOagGeometryBound Compute(
            TppaAstapWcsGeometry main,
            TppaAstapWcsGeometry guide,
            double? guideLockOffsetXFromCenterPixels = null,
            double? guideLockOffsetYFromCenterPixels = null) {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(guide);
        if (guideLockOffsetXFromCenterPixels.HasValue
                != guideLockOffsetYFromCenterPixels.HasValue) {
            throw new InvalidDataException("Guide lock offsets must be supplied together.");
        }
        if ((guideLockOffsetXFromCenterPixels.HasValue
                    && !double.IsFinite(guideLockOffsetXFromCenterPixels.Value))
                || (guideLockOffsetYFromCenterPixels.HasValue
                    && !double.IsFinite(guideLockOffsetYFromCenterPixels.Value))) {
            throw new InvalidDataException("Guide lock offsets must be finite.");
        }

        var mainScale = Math.Min(main.PixelScaleXArcseconds,
            main.PixelScaleYArcseconds);
        var guideScale = Math.Max(guide.PixelScaleXArcseconds,
            guide.PixelScaleYArcseconds);
        var centerSeparation = GreatCircleSeparationRadians(
            main.RightAscensionDegrees, main.DeclinationDegrees,
            guide.RightAscensionDegrees, guide.DeclinationDegrees)
            * 180.0 / Math.PI * 3600.0;
        var mainHalfDiagonal = 0.5 * Math.Sqrt(
            (double)main.WidthPixels * main.WidthPixels
            + (double)main.HeightPixels * main.HeightPixels);
        var guideRadial = guideLockOffsetXFromCenterPixels.HasValue
            ? Math.Sqrt(
                guideLockOffsetXFromCenterPixels.Value
                    * guideLockOffsetXFromCenterPixels.Value
                + guideLockOffsetYFromCenterPixels!.Value
                    * guideLockOffsetYFromCenterPixels.Value)
            : 0.5 * Math.Sqrt((double)guide.WidthPixels * guide.WidthPixels
                + (double)guide.HeightPixels * guide.HeightPixels);
        var evidenceKind = guideLockOffsetXFromCenterPixels.HasValue
            ? "measured-lock-offset-upper-bound"
            : "full-guide-sensor-upper-bound";
        var centerMainPixels = centerSeparation / mainScale;
        var guideRadialArcseconds = guideRadial * guideScale;
        var guideRadialMainPixels = guideRadialArcseconds / mainScale;
        return new(mainScale, guideScale, centerSeparation, centerMainPixels,
            mainHalfDiagonal, evidenceKind, guideRadial, guideRadialArcseconds,
            guideRadialMainPixels,
            centerMainPixels + guideRadialMainPixels + mainHalfDiagonal);
    }

    private static double GreatCircleSeparationRadians(double ra1Degrees,
            double dec1Degrees, double ra2Degrees, double dec2Degrees) {
        var ra1 = ra1Degrees * Math.PI / 180.0;
        var ra2 = ra2Degrees * Math.PI / 180.0;
        var dec1 = dec1Degrees * Math.PI / 180.0;
        var dec2 = dec2Degrees * Math.PI / 180.0;
        var cosine = Math.Sin(dec1) * Math.Sin(dec2)
            + Math.Cos(dec1) * Math.Cos(dec2) * Math.Cos(ra2 - ra1);
        return Math.Acos(Math.Clamp(cosine, -1.0, 1.0));
    }
}
