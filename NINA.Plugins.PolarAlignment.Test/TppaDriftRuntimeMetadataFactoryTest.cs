using FluentAssertions;
using NINA.Astrometry;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaDriftRuntimeMetadataFactoryTest {
        private static readonly Angle Latitude = Angle.ByDegree(25.2);
        private static readonly Angle Longitude = Angle.ByDegree(55.3);
        private static readonly DateTime ObservationTime = new(2026, 7, 23, 20, 0, 0, DateTimeKind.Utc);
        private static readonly RefractionParameters Atmosphere = new(1005, 30, 0.5, 0.55);

        [TestCase(275, 40)]
        [TestCase(10, 50)]
        [TestCase(180, 35)]
        public void CreatesFiniteSolveDerivedMetadata(double azimuthDegrees, double altitudeDegrees) {
            var solved = CreateSolvedCoordinate(azimuthDegrees, altitudeDegrees);

            var observation = TppaDriftRuntimeMetadataFactory.Create(
                "b",
                solved,
                Latitude,
                Longitude,
                10,
                Atmosphere);

            observation.Sample.ObservationTimeUtc.Should().Be(ObservationTime);
            observation.Sample.SolvedDeclinationDegrees.Should().Be(solved.Dec);
            observation.Metadata.PositionId.Should().Be("B");
            observation.Metadata.HourAngleDegrees.Should().BeInRange(-180, 180);
            observation.Metadata.AltitudeDegrees.Should().BeApproximately(altitudeDegrees, 1e-7);
            observation.Metadata.HasComputedRefractionDrift.Should().BeTrue();
            double.IsFinite(observation.Metadata.ComputedRefractionDriftArcsecondsPerMinute).Should().BeTrue();
        }

        [TestCase(90, 0)]
        [TestCase(-90, 0)]
        [TestCase(-180, 45)]
        [TestCase(0, -45)]
        public void HourAngleRoundTripsKnownEquatorialGeometry(double expectedHourAngleDegrees, double declinationDegrees) {
            const double latitudeDegrees = 25;
            var hourAngle = expectedHourAngleDegrees * Math.PI / 180;
            var declination = declinationDegrees * Math.PI / 180;
            var latitude = latitudeDegrees * Math.PI / 180;
            var sinAltitude = Math.Sin(latitude) * Math.Sin(declination)
                + Math.Cos(latitude) * Math.Cos(declination) * Math.Cos(hourAngle);
            var altitude = Math.Asin(sinAltitude);
            var sinAzimuth = -Math.Sin(hourAngle) * Math.Cos(declination) / Math.Cos(altitude);
            var cosAzimuth = (Math.Sin(declination) - Math.Sin(altitude) * Math.Sin(latitude))
                / (Math.Cos(altitude) * Math.Cos(latitude));
            var azimuthDegrees = NormalizeDegrees(Math.Atan2(sinAzimuth, cosAzimuth) * 180 / Math.PI);

            var actual = TppaDriftRuntimeMetadataFactory.CalculateHourAngleDegrees(
                azimuthDegrees,
                altitude * 180 / Math.PI,
                latitudeDegrees,
                declinationDegrees);

            actual.Should().BeApproximately(expectedHourAngleDegrees, 1e-9);
        }

        [Test]
        public void RejectsBelowAltitudeQualificationFloor() {
            var solved = CreateSolvedCoordinate(275, 25);

            var act = () => TppaDriftRuntimeMetadataFactory.Create(
                "A",
                solved,
                Latitude,
                Longitude,
                10,
                Atmosphere);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*altitude*");
        }

        private static Coordinates CreateSolvedCoordinate(double azimuthDegrees, double altitudeDegrees) =>
            new TopocentricCoordinates(
                Angle.ByDegree(azimuthDegrees),
                Angle.ByDegree(altitudeDegrees),
                Latitude,
                Longitude,
                10,
                new FixedObservationDateTime(ObservationTime))
            .Transform(
                Epoch.J2000,
                Atmosphere.PressureHPa,
                Atmosphere.Temperature,
                Atmosphere.RelativeHumidity,
                Atmosphere.Wavelength);

        private static double NormalizeDegrees(double value) => (value + 360) % 360;
    }
}
