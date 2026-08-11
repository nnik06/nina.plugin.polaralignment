using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaDirectBootstrapRouteQualificationTest {
        [Test]
        public void QualifiedXAndTwoSidedAltitudeHeadroomAdmitBootstrap() {
            var result = Evaluate();

            result.IsQualified.Should().BeTrue();
            result.Reason.Should().Contain("two-sided physical Y-probe headroom");
        }

        [Test]
        public void MissingXResponseDeniesBootstrap() {
            var result = Evaluate(azimuthDeltaPerXUnitDegrees: 0, altitudeDeltaPerXUnitDegrees: 0);

            result.IsQualified.Should().BeFalse();
            result.Reason.Should().Contain("static X response");
        }

        [Test]
        public void AltitudeNearLimitDeniesBootstrap() {
            var result = Evaluate(altitudeStartingPositionDegrees: 5.0);

            result.IsQualified.Should().BeFalse();
            result.Reason.Should().Contain("two-sided headroom");
        }

        private static TppaDirectBootstrapRouteQualification Evaluate(
            double azimuthDeltaPerXUnitDegrees = 0.025,
            double altitudeDeltaPerXUnitDegrees = 0,
            double altitudeStartingPositionDegrees = 0) {
            return TppaDirectBootstrapRouteQualification.Evaluate(
                enabled: true,
                operatorConfirmed: true,
                azimuthStartingPositionDegrees: 0,
                azimuthMinimumDegrees: -5.4,
                azimuthMaximumDegrees: 5.4,
                altitudeStartingPositionDegrees,
                altitudeMinimumDegrees: -5.4,
                altitudeMaximumDegrees: 5.4,
                azimuthDeltaPerXUnitDegrees,
                altitudeDeltaPerXUnitDegrees,
                maximumYUnitsPerMove: 81,
                physicalAltitudeDegreesPerYUnit: 0.022);
        }
    }
}
