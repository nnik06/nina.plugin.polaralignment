using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaFirstRunBootstrapRouteQualificationTest {
        [Test]
        public void ConfirmedCenteredTravelEnvelopeAdmitsFirstRunIdentification() {
            var decision = Evaluate();

            decision.IsQualified.Should().BeTrue();
            decision.Reason.Should().Contain("bounded X/Y identification");
        }

        [Test]
        public void UnconfirmedTravelGuardDeniesFirstRunIdentification() {
            var decision = Evaluate(altitudeTravelGuardConfirmed: false);

            decision.IsQualified.Should().BeFalse();
            decision.Reason.Should().Contain("travel guards");
        }

        [Test]
        public void MissingAzimuthPreSeatDeniesFirstRunIdentification() {
            var decision = TppaFirstRunBootstrapRouteQualification.Evaluate(
                enabled: true, operatorConfirmed: true,
                azimuthTravelGuardEnabled: true, azimuthTravelGuardConfirmed: true,
                altitudeTravelGuardEnabled: true, altitudeTravelGuardConfirmed: true,
                azimuthPreSeatEnabled: false, azimuthPreSeatUnits: 24,
                azimuthStartingPositionDegrees: 0, azimuthMinimumDegrees: -5.4, azimuthMaximumDegrees: 5.4,
                altitudeStartingPositionDegrees: 0, altitudeMinimumDegrees: -5.4, altitudeMaximumDegrees: 5.4,
                physicalAzimuthDegreesPerXUnit: 0.025, physicalAltitudeDegreesPerYUnit: 0.022);

            decision.IsQualified.Should().BeFalse();
            decision.Reason.Should().Contain("pre-seat");
        }

        [Test]
        public void NearLimitStartingPositionDeniesFirstRunIdentification() {
            var decision = Evaluate(azimuthStartingPositionDegrees: 5.0);

            decision.IsQualified.Should().BeFalse();
            decision.Reason.Should().Contain("two-sided headroom");
        }

        private static TppaFirstRunBootstrapRouteQualification Evaluate(
                bool altitudeTravelGuardConfirmed = true,
                double azimuthStartingPositionDegrees = 0) =>
            TppaFirstRunBootstrapRouteQualification.Evaluate(
                enabled: true, operatorConfirmed: true,
                azimuthTravelGuardEnabled: true, azimuthTravelGuardConfirmed: true,
                altitudeTravelGuardEnabled: true, altitudeTravelGuardConfirmed: altitudeTravelGuardConfirmed,
                azimuthPreSeatEnabled: true, azimuthPreSeatUnits: 24,
                azimuthStartingPositionDegrees, azimuthMinimumDegrees: -5.4, azimuthMaximumDegrees: 5.4,
                altitudeStartingPositionDegrees: 0, altitudeMinimumDegrees: -5.4, altitudeMaximumDegrees: 5.4,
                physicalAzimuthDegreesPerXUnit: 0.025, physicalAltitudeDegreesPerYUnit: 0.022);
    }
}
