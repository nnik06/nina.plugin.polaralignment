using System;
using System.Globalization;

namespace NINA.Plugins.PolarAlignment {
    /// <summary>
    /// Scale and shape diagnostics for the triangle formed by three solved unit vectors.
    /// These values describe numerical geometry only and do not qualify absolute accuracy.
    /// </summary>
    internal readonly record struct TppaThreePointGeometry(
        double MinimumPairwiseSeparationDegrees,
        double MaximumPairwiseSeparationDegrees,
        double DoubledChordTriangleArea,
        double NormalizedTriangleQuality) {
        public bool IsFinite =>
            double.IsFinite(MinimumPairwiseSeparationDegrees)
            && double.IsFinite(MaximumPairwiseSeparationDegrees)
            && double.IsFinite(DoubledChordTriangleArea)
            && double.IsFinite(NormalizedTriangleQuality);

        public bool IsDegenerate =>
            !IsFinite
            || MaximumPairwiseSeparationDegrees <= 0
            || DoubledChordTriangleArea <= 1e-12;

        public static TppaThreePointGeometry Evaluate(Vector3 first, Vector3 second, Vector3 third) {
            ArgumentNullException.ThrowIfNull(first);
            ArgumentNullException.ThrowIfNull(second);
            ArgumentNullException.ThrowIfNull(third);

            var side12 = (second - first).Length;
            var side23 = (third - second).Length;
            var side13 = (third - first).Length;
            var doubledArea = Vector3.CrossProduct(second - first, third - first).Length;
            var squaredSideSum = side12 * side12 + side23 * side23 + side13 * side13;
            var normalizedQuality = squaredSideSum > 0
                ? 2.0 * Math.Sqrt(3.0) * doubledArea / squaredSideSum
                : 0.0;
            var separations = new[] {
                AngularSeparationDegrees(first, second),
                AngularSeparationDegrees(second, third),
                AngularSeparationDegrees(first, third)
            };

            return new TppaThreePointGeometry(
                Math.Min(separations[0], Math.Min(separations[1], separations[2])),
                Math.Max(separations[0], Math.Max(separations[1], separations[2])),
                doubledArea,
                normalizedQuality);
        }

        public string ToLogString() =>
            string.Format(
                CultureInfo.InvariantCulture,
                "minSeparation={0:F6} deg; maxSeparation={1:F6} deg; doubledChordArea={2:G9}; normalizedTriangleQuality={3:F6}",
                MinimumPairwiseSeparationDegrees,
                MaximumPairwiseSeparationDegrees,
                DoubledChordTriangleArea,
                NormalizedTriangleQuality);

        private static double AngularSeparationDegrees(Vector3 first, Vector3 second) {
            var denominator = first.Length * second.Length;
            if (!(denominator > 0)) {
                return double.NaN;
            }

            var cosine = Math.Clamp(Vector3.ScalarProduct(first, second) / denominator, -1.0, 1.0);
            return Math.Acos(cosine) * 180.0 / Math.PI;
        }
    }
}
