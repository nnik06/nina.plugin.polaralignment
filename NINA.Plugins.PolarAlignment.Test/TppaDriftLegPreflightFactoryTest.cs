using FluentAssertions;
using NINA.Astrometry;
using NINA.Core.Enum;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaDriftLegPreflightFactoryTest {
        private static readonly Angle Latitude = Angle.ByDegree(25.2);
        private static readonly Angle Longitude = Angle.ByDegree(55.3);
        private static readonly DateTime Start = new(2026, 7, 23, 20, 0, 0, DateTimeKind.Utc);
        private static readonly RefractionParameters Atmosphere = new(1005, 30, 0.5, 0.55);

        [Test]
        public void AcceptsBothCurrentTimeRelativeDestinationsAtHighAltitude() {
            var current = FromTopocentric(0, 65);

            var result = TppaDriftLegPreflightFactory.EvaluateRelative(
                current,
                10,
                Start,
                Latitude,
                Longitude,
                10,
                Atmosphere,
                _ => PierSide.pierEast);

            result.IsSafe.Should().BeTrue(result.Reason);
            result.PositiveRaDestination.MinimumPredictedAltitudeDegrees.Should().BeGreaterThan(30);
            result.NegativeRaDestination.MinimumPredictedAltitudeDegrees.Should().BeGreaterThan(30);
        }

        [Test]
        public void RejectsRelativeLegWhenEitherDirectionChangesKnownPierSide() {
            var current = FromTopocentric(0, 65);
            var initialRa = current.RADegrees;

            var result = TppaDriftLegPreflightFactory.EvaluateRelative(
                current,
                10,
                Start,
                Latitude,
                Longitude,
                10,
                Atmosphere,
                coordinate => AngularDistance(initialRa, coordinate.RADegrees) > 5
                    ? PierSide.pierWest
                    : PierSide.pierEast);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("changes");
        }

        [Test]
        public void RejectsAbsoluteReturnBelowAltitudeFloor() {
            var current = FromTopocentric(0, 65);
            var destination = FromTopocentric(270, 20);

            var result = TppaDriftLegPreflightFactory.EvaluateAbsolute(
                current,
                destination,
                Start,
                Latitude,
                Longitude,
                10,
                Atmosphere,
                _ => PierSide.pierUnknown);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("safety floor");
        }

        [Test]
        public void RejectsAbsoluteReturnAcrossKnownPierSide() {
            var current = FromTopocentric(0, 65);
            var destination = FromTopocentric(5, 65);

            var result = TppaDriftLegPreflightFactory.EvaluateAbsolute(
                current,
                destination,
                Start,
                Latitude,
                Longitude,
                10,
                Atmosphere,
                coordinate => ReferenceEquals(coordinate, current)
                    ? PierSide.pierEast
                    : PierSide.pierWest);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("changes");
        }

        [TestCase(0)]
        [TestCase(double.NaN)]
        [TestCase(90)]
        public void RejectsInvalidRelativeDistance(double distanceDegrees) {
            var current = FromTopocentric(0, 65);

            var act = () => TppaDriftLegPreflightFactory.EvaluateRelative(
                current,
                distanceDegrees,
                Start,
                Latitude,
                Longitude,
                10,
                Atmosphere,
                _ => PierSide.pierUnknown);

            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        private static Coordinates FromTopocentric(double azimuthDegrees, double altitudeDegrees) =>
            new TopocentricCoordinates(
                Angle.ByDegree(azimuthDegrees),
                Angle.ByDegree(altitudeDegrees),
                Latitude,
                Longitude,
                10,
                new FixedObservationDateTime(Start))
            .Transform(
                Epoch.J2000,
                Atmosphere.PressureHPa,
                Atmosphere.Temperature,
                Atmosphere.RelativeHumidity,
                Atmosphere.Wavelength);

        private static double AngularDistance(double first, double second) =>
            180 - Math.Abs(Math.Abs(first - second) - 180);
    }
}
