using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaThreePointGeometryTest {
        [Test]
        public void OrthogonalUnitVectorsProduceFiniteEquilateralChordGeometry() {
            var geometry = TppaThreePointGeometry.Evaluate(
                new Vector3(1, 0, 0),
                new Vector3(0, 1, 0),
                new Vector3(0, 0, 1));

            geometry.IsFinite.Should().BeTrue();
            geometry.IsDegenerate.Should().BeFalse();
            geometry.MinimumPairwiseSeparationDegrees.Should().BeApproximately(90, 1e-12);
            geometry.MaximumPairwiseSeparationDegrees.Should().BeApproximately(90, 1e-12);
            geometry.DoubledChordTriangleArea.Should().BeApproximately(Math.Sqrt(3), 1e-12);
            geometry.NormalizedTriangleQuality.Should().BeApproximately(1, 1e-12);
            geometry.ToLogString().Should().Contain("minSeparation=90.000000 deg");
        }

        [Test]
        public void RepeatedPointsAreDegenerate() {
            var point = new Vector3(1, 0, 0);

            var geometry = TppaThreePointGeometry.Evaluate(point, point, point);

            geometry.IsDegenerate.Should().BeTrue();
            geometry.DoubledChordTriangleArea.Should().Be(0);
        }
    }
}
