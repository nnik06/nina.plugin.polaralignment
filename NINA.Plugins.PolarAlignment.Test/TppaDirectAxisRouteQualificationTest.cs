using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaDirectAxisRouteQualificationTest {
        [Test]
        public void Evaluate_AdmitsCalibratedAzimuthWithoutAltitudeCalibration() {
            var result = Evaluate(enabled: true, confirmed: true, start: 0, physicalDegreesPerUnit: 0.05, azimuthMinutes: 300, altitudeMinutes: 60);

            result.IsQualified.Should().BeTrue();
            result.PlannedUnits.Should().BeNegative();
        }

        [Test]
        public void Evaluate_RejectsMissingPhysicalScaleEvenWithASkyResponse() {
            var result = Evaluate(enabled: true, confirmed: true, start: 0, physicalDegreesPerUnit: 0, azimuthMinutes: 60, altitudeMinutes: 0);

            result.IsQualified.Should().BeFalse();
            result.Reason.Should().Contain("physical travel calibration");
        }

        [Test]
        public void Evaluate_RejectsInsufficientVisualMarkerHeadroom() {
            var result = Evaluate(enabled: true, confirmed: true, start: 5.2, physicalDegreesPerUnit: 0.05, azimuthMinutes: 180, altitudeMinutes: 0);

            result.IsQualified.Should().BeFalse();
            result.Reason.Should().Contain("headroom");
        }

        private static TppaDirectAxisRouteQualification Evaluate(
                bool enabled,
                bool confirmed,
                double start,
                double physicalDegreesPerUnit,
                double azimuthMinutes,
                double altitudeMinutes) {
            return TppaDirectAxisRouteQualification.Evaluate(
                enabled,
                confirmed,
                start,
                -5.4,
                5.4,
                physicalDegreesPerUnit,
                azimuthErrorDeltaPerUnitDegrees: 0.05,
                altitudeErrorDeltaPerUnitDegrees: 0,
                maximumUnitsPerMove: 81,
                azimuthMinutes,
                altitudeMinutes);
        }
    }
}
