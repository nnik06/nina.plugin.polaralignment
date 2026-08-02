using System;
using System.Linq;
using FluentAssertions;
using NINA.Astrometry;
using NINA.Core.Utility;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaVerificationWaypointPlanTest {
        [Test]
        public void EastPlanUsesFixedAbsoluteWaypointsAcrossRaWrap() {
            var start = Coordinate(350, 42);

            var plan = TppaVerificationWaypointPlan.Create(start, 15, true);

            plan.Forward[0].RADegrees.Should().BeApproximately(350, 1e-9);
            plan.Forward[1].RADegrees.Should().BeApproximately(5, 1e-9);
            plan.Forward[2].RADegrees.Should().BeApproximately(20, 1e-9);
            plan.Reciprocal[0].RADegrees.Should().BeApproximately(20, 1e-9);
            plan.Reciprocal[1].RADegrees.Should().BeApproximately(5, 1e-9);
            plan.Reciprocal[2].RADegrees.Should().BeApproximately(350, 1e-9);
            plan.ReciprocalModelCheck.Select(point => point.RADegrees).Should().Equal(
                20, 12.5, 5, 357.5, 350);
        }

        [Test]
        public void WestPlanUsesFixedAbsoluteWaypointsAcrossRaWrap() {
            var start = Coordinate(10, -12);

            var plan = TppaVerificationWaypointPlan.Create(start, 15, false);

            plan.Forward[0].RADegrees.Should().BeApproximately(10, 1e-9);
            plan.Forward[1].RADegrees.Should().BeApproximately(355, 1e-9);
            plan.Forward[2].RADegrees.Should().BeApproximately(340, 1e-9);
            plan.Reciprocal[0].RADegrees.Should().BeApproximately(340, 1e-9);
            plan.Reciprocal[1].RADegrees.Should().BeApproximately(355, 1e-9);
            plan.Reciprocal[2].RADegrees.Should().BeApproximately(10, 1e-9);
            plan.ReciprocalModelCheck.Select(point => point.RADegrees).Should().Equal(
                340, 347.5, 355, 2.5, 10);
        }

        [Test]
        public void PlanPreservesDeclinationAndEpochForEveryWaypoint() {
            var start = Coordinate(120, 48);

            var plan = TppaVerificationWaypointPlan.Create(start, 20, true);

            foreach (var point in plan.Forward.Concat(plan.ReciprocalModelCheck)) {
                point.Dec.Should().BeApproximately(48, 1e-9);
                point.Epoch.Should().Be(Epoch.JNOW);
            }
        }

        [TestCase(0)]
        [TestCase(90)]
        [TestCase(double.NaN)]
        public void PlanRejectsInvalidLegDistance(double distance) {
            var start = Coordinate(120, 48);

            Action create = () => TppaVerificationWaypointPlan.Create(start, distance, true);

            create.Should().Throw<ArgumentOutOfRangeException>();
        }

        private static Coordinates Coordinate(double raDegrees, double decDegrees) =>
            new(
                Angle.ByDegree(raDegrees),
                Angle.ByDegree(decDegrees),
                Epoch.JNOW,
                new FixedObservationDateTime(new DateTime(2026, 7, 26, 19, 0, 0, DateTimeKind.Utc)));
    }
}
