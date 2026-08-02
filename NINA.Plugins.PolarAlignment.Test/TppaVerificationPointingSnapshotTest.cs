using System;
using FluentAssertions;
using NINA.Astrometry;
using NINA.Core.Utility;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaVerificationPointingSnapshotTest {
        [Test]
        public void UsesValidatedMountTelemetryCoordinates() {
            var telemetry = Coordinate(317.5, 48.1);

            var selected = TppaVerificationPointingSnapshot.FromMountTelemetry(
                telemetry,
                "A/correction");

            selected.Should().BeSameAs(telemetry);
            selected.RADegrees.Should().BeApproximately(317.5, 1e-9);
        }

        [Test]
        public void RejectsMissingMountTelemetryCoordinates() {
            Action capture = () => TppaVerificationPointingSnapshot.FromMountTelemetry(
                null,
                "A/correction");

            capture.Should().Throw<InvalidOperationException>()
                .WithMessage("*telemetry is unavailable*");
        }

        [Test]
        public void RejectsNonFiniteMountTelemetryCoordinates() {
            var telemetry = Coordinate(double.NaN, 48.1);

            Action capture = () => TppaVerificationPointingSnapshot.FromMountTelemetry(
                telemetry,
                "A/correction");

            capture.Should().Throw<InvalidOperationException>()
                .WithMessage("*telemetry is non-finite*");
        }

        private static Coordinates Coordinate(double raDegrees, double decDegrees) =>
            new(
                Angle.ByDegree(raDegrees),
                Angle.ByDegree(decDegrees),
                Epoch.JNOW,
                new FixedObservationDateTime(
                    new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)));
    }
}