using FluentAssertions;
using NINA.Astrometry;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaRefractionDriftCalculatorTest {
        private static readonly Angle Latitude = Angle.ByDegree(25.2);
        private static readonly Angle Longitude = Angle.ByDegree(55.3);
        private static readonly DateTime ObservationTime = new(2026, 7, 23, 20, 0, 0, DateTimeKind.Utc);
        private static readonly RefractionParameters Atmosphere = new(1005, 30, 0.5, 0.55);

        [Test]
        public void ComputesFiniteRefractionDriftAndClosesAtMidpoint() {
            var solved = CreateSolvedCoordinate(azimuthDegrees: 275, altitudeDegrees: 35, Atmosphere);

            var result = TppaRefractionDriftCalculator.Evaluate(
                solved,
                Latitude,
                Longitude,
                elevationMeters: 10,
                Atmosphere,
                ObservationTime);

            result.IsValid.Should().BeTrue(result.Reason);
            double.IsFinite(result.DeclinationDriftArcsecondsPerMinute).Should().BeTrue();
            Math.Abs(result.DeclinationDriftArcsecondsPerMinute).Should().BeGreaterThan(0.01);
            result.MidpointClosureArcseconds.Should().BeLessThan(TppaRefractionDriftCalculator.MaximumClosureArcseconds);
        }

        [Test]
        public void MatchesDirectSyntheticPlateSolveDifference() {
            var solved = CreateSolvedCoordinate(azimuthDegrees: 275, altitudeDegrees: 40, Atmosphere);
            var result = TppaRefractionDriftCalculator.Evaluate(
                solved,
                Latitude,
                Longitude,
                elevationMeters: 10,
                Atmosphere,
                ObservationTime,
                TimeSpan.FromSeconds(45));

            var apparentMidpoint = solved.Transform(
                Latitude,
                Longitude,
                10,
                Atmosphere.PressureHPa,
                Atmosphere.Temperature,
                Atmosphere.RelativeHumidity,
                Atmosphere.Wavelength,
                ObservationTime);
            var mechanical = apparentMidpoint.Transform(Epoch.J2000, 0, Atmosphere.Temperature, 0, Atmosphere.Wavelength);
            var before = Simulate(mechanical, ObservationTime.AddSeconds(-45));
            var after = Simulate(mechanical, ObservationTime.AddSeconds(45));
            var expected = (after.Dec - before.Dec) * 3600 / 1.5;

            result.IsValid.Should().BeTrue(result.Reason);
            result.DeclinationDriftArcsecondsPerMinute.Should().BeApproximately(expected, 1e-9);
        }

        [Test]
        public void RejectsTrackBelowAltitudeFloor() {
            var solved = CreateSolvedCoordinate(azimuthDegrees: 275, altitudeDegrees: 25, Atmosphere);

            var result = TppaRefractionDriftCalculator.Evaluate(
                solved,
                Latitude,
                Longitude,
                10,
                Atmosphere,
                ObservationTime);

            result.IsValid.Should().BeFalse();
            result.Reason.Should().Contain("altitude");
        }

        [TestCase(0, 30, 0.5, "pressure")]
        [TestCase(1005, 200, 0.5, "temperature")]
        [TestCase(1005, 30, 2, "humidity")]
        public void RejectsInvalidAtmosphere(
            double pressure,
            double temperature,
            double humidity,
            string expectedReason) {
            var solved = CreateSolvedCoordinate(275, 40, Atmosphere);
            var invalidAtmosphere = new RefractionParameters(pressure, temperature, humidity, 0.55);

            var result = TppaRefractionDriftCalculator.Evaluate(
                solved,
                Latitude,
                Longitude,
                10,
                invalidAtmosphere,
                ObservationTime);

            result.IsValid.Should().BeFalse();
            result.Reason.Should().Contain(expectedReason);
        }

        [Test]
        public void RejectsNonUtcObservationTime() {
            var solved = CreateSolvedCoordinate(275, 40, Atmosphere);

            var result = TppaRefractionDriftCalculator.Evaluate(
                solved,
                Latitude,
                Longitude,
                10,
                Atmosphere,
                DateTime.SpecifyKind(ObservationTime, DateTimeKind.Local));

            result.IsValid.Should().BeFalse();
            result.Reason.Should().Contain("UTC");
        }

        private static Coordinates CreateSolvedCoordinate(
            double azimuthDegrees,
            double altitudeDegrees,
            RefractionParameters atmosphere) => new TopocentricCoordinates(
                Angle.ByDegree(azimuthDegrees),
                Angle.ByDegree(altitudeDegrees),
                Latitude,
                Longitude,
                10,
                new FixedObservationDateTime(ObservationTime))
            .Transform(
                Epoch.J2000,
                atmosphere.PressureHPa,
                atmosphere.Temperature,
                atmosphere.RelativeHumidity,
                atmosphere.Wavelength);

        private static Coordinates Simulate(Coordinates mechanical, DateTime time) => mechanical
            .Transform(Latitude, Longitude, 10, 0, Atmosphere.Temperature, 0, Atmosphere.Wavelength, time)
            .Transform(Epoch.J2000, Atmosphere.PressureHPa, Atmosphere.Temperature, Atmosphere.RelativeHumidity, Atmosphere.Wavelength);
    }
}
