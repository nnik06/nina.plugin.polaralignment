using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class UpasDirectTravelAdmissionPolicyTest {
        [Test]
        public void NonUpasAndManualRoutesNeedNoMarkerAdmission() {
            UpasDirectTravelAdmissionPolicy.GetIssues(
                useUpas: false,
                automatedAdjustmentsEnabled: true,
                azimuthGuardEnabled: false,
                azimuthMarkerConfirmed: false,
                altitudeGuardEnabled: false,
                altitudeMarkerConfirmed: false).Should().BeEmpty();

            UpasDirectTravelAdmissionPolicy.GetIssues(
                useUpas: true,
                automatedAdjustmentsEnabled: false,
                azimuthGuardEnabled: false,
                azimuthMarkerConfirmed: false,
                altitudeGuardEnabled: false,
                altitudeMarkerConfirmed: false).Should().BeEmpty();
        }

        [Test]
        public void DirectUpasRouteRequiresBothCurrentPhysicalMarkers() {
            var issues = UpasDirectTravelAdmissionPolicy.GetIssues(
                useUpas: true,
                automatedAdjustmentsEnabled: true,
                azimuthGuardEnabled: true,
                azimuthMarkerConfirmed: true,
                altitudeGuardEnabled: true,
                altitudeMarkerConfirmed: false);

            issues.Should().ContainSingle();
            issues.Single().Should().Contain("ALT marker");
        }

        [Test]
        public void DirectUpasRouteAdmitsBothConfirmedMarkers() {
            UpasDirectTravelAdmissionPolicy.GetIssues(
                useUpas: true,
                automatedAdjustmentsEnabled: true,
                azimuthGuardEnabled: true,
                azimuthMarkerConfirmed: true,
                altitudeGuardEnabled: true,
                altitudeMarkerConfirmed: true).Should().BeEmpty();
        }
    }
}
