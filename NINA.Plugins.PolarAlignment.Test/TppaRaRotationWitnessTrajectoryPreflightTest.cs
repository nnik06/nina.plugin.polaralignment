using FluentAssertions;
using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Utility;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaRaRotationWitnessTrajectoryPreflightTest {
        private static readonly DateTime Start =
            new(2026, 8, 1, 16, 0, 0, DateTimeKind.Utc);
        private static readonly TppaMountMotionEnvelope Balcony =
            new(25, 55, 270, 10);

        [Test]
        public void AcceptsFullySampledQualifiedTrajectory() {
            var result = Evaluate(
                coordinate => new(
                    AzimuthDegrees: 350 + SignedOffset(coordinate) * 0.1,
                    AltitudeDegrees: 45),
                _ => PierSide.pierEast);

            result.IsSafe.Should().BeTrue(result.Reason);
            result.TotalArcDegrees.Should().Be(50);
            result.DesignConditionProxy.Should().BeLessThan(4);
            result.MinimumPredictedAltitudeDegrees.Should().Be(45);
            result.Samples.Should().HaveCountGreaterThan(100);
            result.Samples
                .Where(sample => sample.SegmentId.Contains("slew"))
                .Zip(result.Samples
                    .Where(sample => sample.SegmentId.Contains("slew"))
                    .Skip(1),
                    (first, second) => Math.Abs(
                        second.RightAscensionOffsetDegrees
                        - first.RightAscensionOffsetDegrees))
                .Where(step => step > 0)
                .Should().OnlyContain(step => step <= 1.000000001);
        }

        [Test]
        public void RejectsIntermediateEnvelopeViolationWhenWaypointsPass() {
            var result = Evaluate(
                coordinate => {
                    var offset = SignedOffset(coordinate);
                    return new TppaRaRotationWitnessHorizontalPosition(
                        AzimuthDegrees: Math.Abs(offset - 12.5) < 0.6 ? 180 : 350,
                        AltitudeDegrees: 45);
                },
                _ => PierSide.pierEast);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("A-B-slew");
            result.Reason.Should().Contain("azimuth");
        }

        [Test]
        public void RejectsOperationalAltitudeBelowFloorInsideHardEnvelope() {
            var result = Evaluate(
                coordinate => new(
                    AzimuthDegrees: 350,
                    AltitudeDegrees: Math.Abs(SignedOffset(coordinate) - 12.5) < 0.6
                        ? 39.5
                        : 45),
                _ => PierSide.pierEast);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("operational floor");
        }

        [Test]
        public void RejectsUnknownPierSide() {
            var result = Evaluate(
                _ => new(350, 45),
                _ => PierSide.pierUnknown);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("unknown predicted pier side");
        }

        [Test]
        public void RejectsPierSideChangeAtIntermediateSample() {
            var result = Evaluate(
                _ => new(350, 45),
                coordinate => SignedOffset(coordinate) > 12
                    ? PierSide.pierWest
                    : PierSide.pierEast);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("changes predicted pier side");
        }

        [Test]
        public void RejectsInsufficientTotalArc() {
            var result = Evaluate(
                _ => new(350, 45),
                _ => PierSide.pierEast,
                legDistanceDegrees: 20);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("total arc");
        }

        [TestCase(269.9)]
        [TestCase(10.1)]
        public void RejectsWrappedAzimuthOutsideBalcony(double azimuthDegrees) {
            var result = Evaluate(
                _ => new(azimuthDegrees, 45),
                _ => PierSide.pierEast);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("azimuth");
        }

        [Test]
        public void RejectsSamplingStepAboveOneDegree() {
            var act = () => TppaRaRotationWitnessTrajectoryPreflight.EvaluateProjected(
                PointA(),
                25,
                true,
                Start,
                Balcony,
                _ => new(350, 45),
                _ => PierSide.pierEast,
                maximumSampleStepDegrees: 1.01);

            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        private static TppaRaRotationWitnessTrajectoryPreflightResult Evaluate(
                Func<Coordinates, TppaRaRotationWitnessHorizontalPosition> projector,
                Func<Coordinates, PierSide> pierSide,
                double legDistanceDegrees = 25) =>
            TppaRaRotationWitnessTrajectoryPreflight.EvaluateProjected(
                PointA(),
                legDistanceDegrees,
                true,
                Start,
                Balcony,
                projector,
                pierSide);

        private static Coordinates PointA() => new(
            Angle.ByDegree(100),
            Angle.ByDegree(80),
            Epoch.J2000,
            new FixedObservationDateTime(Start));

        private static double SignedOffset(Coordinates coordinate) =>
            ((coordinate.RADegrees - 100 + 540) % 360) - 180;
    }
}
