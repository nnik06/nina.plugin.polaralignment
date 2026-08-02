using FluentAssertions;
using System;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaVerificationPointContextEvidenceFactoryTest {
        [Test]
        public void PreservesApproachTimingAndAtmosphereWithoutGrantingAuthority() {
            var observedUtc = new DateTime(2026, 8, 1, 17, 30, 0, DateTimeKind.Utc);
            var observation = new TppaDriftRuntimeObservation(
                new TppaDriftSolveSample(observedUtc, 72.5),
                new TppaDriftTrackMetadata("EAST-2", -22.5, 41.25, true, 0.03125));
            var refraction = new RefractionParameters(1002.5, 36.2, 68.0, 0.55);

            var result = TppaVerificationPointContextEvidenceFactory.Create(
                Guid.Parse("f6c7db30-6a43-42cb-9550-58b1b3b6c67d"),
                "East",
                2,
                "East",
                true,
                15,
                30,
                observation,
                true,
                refraction);

            result.ApproachDirection.Should().Be("East");
            result.ApproachDirectionKnown.Should().BeTrue();
            result.RequestedRightAscensionTravelDegrees.Should().Be(15);
            result.SettleSeconds.Should().Be(30);
            result.ObservationUtc.Should().Be(observedUtc);
            result.HourAngleDegrees.Should().Be(-22.5);
            result.ApparentAltitudeDegrees.Should().Be(41.25);
            result.RefractionAdjustmentEnabled.Should().BeTrue();
            result.ComputedRefractionDriftArcsecondsPerMinute.Should().Be(0.03125);
            result.PressureHPa.Should().Be(1002.5);
            result.TemperatureCelsius.Should().Be(36.2);
            result.RelativeHumidity.Should().Be(68.0);
            result.GrantsMotionAuthority.Should().BeFalse();
            result.GrantsCompletionAuthority.Should().BeFalse();
        }

        [Test]
        public void SerializesUnknownPrepositioningExplicitly() {
            var observedUtc = new DateTime(2026, 8, 1, 17, 30, 0, DateTimeKind.Utc);
            var observation = new TppaDriftRuntimeObservation(
                new TppaDriftSolveSample(observedUtc, 72.5),
                new TppaDriftTrackMetadata("WEST-1", 12, 35, true, 0));

            var result = TppaVerificationPointContextEvidenceFactory.Create(
                Guid.NewGuid(),
                "West",
                1,
                "PrePositionedUnknown",
                false,
                null,
                null,
                observation,
                false,
                new RefractionParameters(1013.25, 15, 0));

            result.ToJson().Should().Contain("\"approachDirection\":\"PrePositionedUnknown\"");
            result.ToJson().Should().Contain("\"approachDirectionKnown\":false");
            result.ToJson().Should().Contain("\"requestedRightAscensionTravelDegrees\":null");
            result.ToJson().Should().Contain("\"grantsMotionAuthority\":false");
        }
    }
}
