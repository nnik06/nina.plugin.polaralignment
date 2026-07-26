using FluentAssertions;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaDriftValidationEstimatorTest {
        private const double LatitudeDegrees = 25.2;

        [Test]
        public void PositiveAzimuthErrorProducesPositiveFieldCentreDeclinationDriftOnMeridian() {
            var drift = TppaDriftValidationEstimator.PredictDeclinationDriftArcsecondsPerMinute(
                azimuthErrorArcMinutes: 1,
                altitudeErrorArcMinutes: 0,
                siteLatitudeDegrees: LatitudeDegrees,
                hourAngleDegrees: 0);

            drift.Should().BePositive(
                "the plate-solved field-centre observable has the declared positive azimuth sign on the meridian");
        }

        [Test]
        public void OneArcminuteAzimuthErrorMatchesExternalMeridianCoefficientFixture() {
            var drift = TppaDriftValidationEstimator.PredictDeclinationDriftArcsecondsPerMinute(
                azimuthErrorArcMinutes: 1,
                altitudeErrorArcMinutes: 0,
                siteLatitudeDegrees: 25.2,
                hourAngleDegrees: 0);

            drift.Should().BeApproximately(
                0.23753176207226007,
                1e-12,
                "the hard-coded value was independently calculated from sidereal rate, radians per arcminute, and cos(latitude)");
        }

        [Test]
        public void PositiveAltitudeErrorChangesFieldCentreDeclinationDriftSignAcrossMeridian() {
            var eastDrift = TppaDriftValidationEstimator.PredictDeclinationDriftArcsecondsPerMinute(
                azimuthErrorArcMinutes: 0,
                altitudeErrorArcMinutes: 1,
                siteLatitudeDegrees: LatitudeDegrees,
                hourAngleDegrees: -90);
            var westDrift = TppaDriftValidationEstimator.PredictDeclinationDriftArcsecondsPerMinute(
                azimuthErrorArcMinutes: 0,
                altitudeErrorArcMinutes: 1,
                siteLatitudeDegrees: LatitudeDegrees,
                hourAngleDegrees: 90);

            eastDrift.Should().BePositive(
                "positive altitude error raises the plate-solved field-centre declination drift east of the meridian");
            westDrift.Should().BeNegative(
                "positive altitude error lowers the plate-solved field-centre declination drift west of the meridian");
            Math.Abs(eastDrift).Should().BeApproximately(Math.Abs(westDrift), 1e-9);
        }

        [Test]
        public void RecoversInjectedPolarErrorFromWellConditionedTracks() {
            var tracks = CreateTracks(azimuthErrorArcMinutes: -3.0, altitudeErrorArcMinutes: 2.0);

            var result = TppaDriftValidationEstimator.Evaluate(tracks, LatitudeDegrees);

            result.IsValid.Should().BeTrue(result.Reason);
            result.AzimuthErrorArcMinutes.Should().BeApproximately(-3.0, 0.01);
            result.AltitudeErrorArcMinutes.Should().BeApproximately(2.0, 0.01);
            result.TotalErrorArcMinutes.Should().BeApproximately(Math.Sqrt(13), 0.01);
        }

        [TestCase(25.2, -4.0, -3.0)]
        [TestCase(25.2, -4.0, 3.0)]
        [TestCase(25.2, 4.0, -3.0)]
        [TestCase(25.2, 4.0, 3.0)]
        [TestCase(-25.2, -4.0, -3.0)]
        [TestCase(-25.2, -4.0, 3.0)]
        [TestCase(-25.2, 4.0, -3.0)]
        [TestCase(-25.2, 4.0, 3.0)]
        public void RecoversEverySignQuadrantAcrossHemispheres(
            double latitudeDegrees,
            double azimuthErrorArcMinutes,
            double altitudeErrorArcMinutes) {
            var tracks = CreateTracks(
                azimuthErrorArcMinutes,
                altitudeErrorArcMinutes,
                hourAngles: new[] { -80.0, -25.0, 40.0, -75.0 },
                siteLatitudeDegrees: latitudeDegrees);

            var result = TppaDriftValidationEstimator.Evaluate(tracks, latitudeDegrees);

            result.IsValid.Should().BeTrue(result.Reason);
            result.AzimuthErrorArcMinutes.Should().BeApproximately(azimuthErrorArcMinutes, 0.01);
            result.AltitudeErrorArcMinutes.Should().BeApproximately(altitudeErrorArcMinutes, 0.01);
        }

        [Test]
        public void ComputedRefractionIsRemovedBeforePolarFit() {
            var tracks = CreateTracks(
                azimuthErrorArcMinutes: 1.5,
                altitudeErrorArcMinutes: -2.5,
                refractionDriftArcsecondsPerMinute: 0.35);

            var result = TppaDriftValidationEstimator.Evaluate(tracks, LatitudeDegrees);

            result.IsValid.Should().BeTrue(result.Reason);
            result.AzimuthErrorArcMinutes.Should().BeApproximately(1.5, 0.01);
            result.AltitudeErrorArcMinutes.Should().BeApproximately(-2.5, 0.01);
        }

        [Test]
        public void RejectsAcquisitionWithoutRepeatedPosition() {
            var tracks = CreateTracks(-3, 2)
                .Select((track, index) => track with { PositionId = ((char)('A' + index)).ToString() })
                .ToArray();

            var result = TppaDriftValidationEstimator.Evaluate(tracks, LatitudeDegrees);

            result.IsValid.Should().BeFalse();
            result.Reason.Should().Contain("revisit");
        }

        [Test]
        public void RejectsShortTracks() {
            var tracks = CreateTracks(-3, 2)
                .Select(track => track with { DurationSeconds = 240 })
                .ToArray();

            var result = TppaDriftValidationEstimator.Evaluate(tracks, LatitudeDegrees);

            result.IsValid.Should().BeFalse();
            result.Reason.Should().Contain("300");
        }

        [Test]
        public void RejectsPoorHourAngleGeometry() {
            var tracks = CreateTracks(-3, 2, hourAngles: new[] { -4.0, -2.0, 0.0, 2.0 });

            var result = TppaDriftValidationEstimator.Evaluate(tracks, LatitudeDegrees);

            result.IsValid.Should().BeFalse();
            result.Reason.Should().Contain("condition number");
        }

        [Test]
        public void RejectsNonstationaryRepeatedPosition() {
            var tracks = CreateTracks(-3, 2);
            tracks[^1] = tracks[^1] with {
                DeclinationDriftArcsecondsPerMinute = tracks[^1].DeclinationDriftArcsecondsPerMinute + 1.0
            };

            var result = TppaDriftValidationEstimator.Evaluate(tracks, LatitudeDegrees);

            result.IsValid.Should().BeFalse();
            result.Reason.Should().Contain("chi-squared");
        }

        [Test]
        public void SeededMonteCarloRecoversTwoArcminuteScaleErrors() {
            var random = new Random(73023);
            var validCount = 0;
            var withinOneArcMinuteCount = 0;
            for (var trial = 0; trial < 200; trial++) {
                var tracks = CreateTracks(azimuthErrorArcMinutes: 1.5, altitudeErrorArcMinutes: -1.25);
                for (var index = 0; index < tracks.Length; index++) {
                    tracks[index] = tracks[index] with {
                        DeclinationDriftArcsecondsPerMinute =
                            tracks[index].DeclinationDriftArcsecondsPerMinute + NextGaussian(random) * 0.08
                    };
                }

                var result = TppaDriftValidationEstimator.Evaluate(tracks, LatitudeDegrees);
                if (!result.IsValid) {
                    continue;
                }
                validCount++;
                if (Math.Abs(result.AzimuthErrorArcMinutes - 1.5) <= 1.0
                        && Math.Abs(result.AltitudeErrorArcMinutes + 1.25) <= 1.0) {
                    withinOneArcMinuteCount++;
                }
            }

            validCount.Should().BeGreaterThan(180);
            withinOneArcMinuteCount.Should().BeGreaterThan(180);
        }

        private static TppaDriftTrack[] CreateTracks(
            double azimuthErrorArcMinutes,
            double altitudeErrorArcMinutes,
            double refractionDriftArcsecondsPerMinute = 0,
            double[]? hourAngles = null,
            double siteLatitudeDegrees = LatitudeDegrees) {
            hourAngles ??= new[] { -75.0, -20.0, 35.0, -70.0 };
            var ids = new[] { "A", "B", "C", "A" };
            return hourAngles.Select((hourAngle, index) => {
                var drift = TppaDriftValidationEstimator.PredictDeclinationDriftArcsecondsPerMinute(
                    azimuthErrorArcMinutes,
                    altitudeErrorArcMinutes,
                    siteLatitudeDegrees,
                    hourAngle,
                    refractionDriftArcsecondsPerMinute);
                return new TppaDriftTrack(
                    ids[index],
                    hourAngle,
                    AltitudeDegrees: 40,
                    DurationSeconds: 300,
                    SampleCount: 40,
                    DeclinationDriftArcsecondsPerMinute: drift,
                    DeclinationDriftSigmaArcsecondsPerMinute: 0.10,
                    ComputedRefractionDriftArcsecondsPerMinute: refractionDriftArcsecondsPerMinute);
            }).ToArray();
        }

        private static double NextGaussian(Random random) {
            var first = 1.0 - random.NextDouble();
            var second = 1.0 - random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(first)) * Math.Cos(2.0 * Math.PI * second);
        }
    }
}
