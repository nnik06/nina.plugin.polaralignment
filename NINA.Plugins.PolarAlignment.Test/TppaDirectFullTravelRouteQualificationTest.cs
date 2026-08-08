using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaDirectFullTravelRouteQualificationTest {
        [Test]
        public void Evaluate_RejectsAnUnconfirmedRouteBeforeReadingItsCalibration() {
            var result = Evaluate(enabled: true, confirmed: false, azimuthMinutes: 60, altitudeMinutes: 60);

            result.IsQualified.Should().BeFalse();
            result.Reason.Should().Contain("operator-confirmed");
        }

        [Test]
        public void Evaluate_RejectsASingularMeasuredResponseMatrix() {
            var result = TppaDirectFullTravelRouteQualification.Evaluate(
                true, true,
                0, -5.4, 5.4,
                0, -5.4, 5.4,
                0.03, 0.06,
                0.02, 0.04,
                108, 108,
                120, 120);

            result.IsQualified.Should().BeFalse();
            result.Reason.Should().Contain("singular");
        }

        [Test]
        public void Evaluate_RejectsAFeasibleRouteWithoutSignedMarkerHeadroom() {
            var result = TppaDirectFullTravelRouteQualification.Evaluate(
                true, true,
                5.2, -5.4, 5.4,
                0, -5.4, 5.4,
                0.05, 0,
                0, 0.05,
                81, 81,
                180, 0);

            result.IsQualified.Should().BeFalse();
            result.Reason.Should().Contain("headroom");
        }

        [Test]
        public void Evaluate_AcceptsAConfirmedTwoMoveRouteFromVisualZero() {
            var result = Evaluate(enabled: true, confirmed: true, azimuthMinutes: 300, altitudeMinutes: -300);

            result.IsQualified.Should().BeTrue();
            result.Feasibility.RequiredMoveCount.Should().Be(2);
            result.Reason.Should().Contain("five-minute");
        }

        private static TppaDirectFullTravelRouteQualification Evaluate(bool enabled, bool confirmed, double azimuthMinutes, double altitudeMinutes) {
            return TppaDirectFullTravelRouteQualification.Evaluate(
                enabled, confirmed,
                0, -5.4, 5.4,
                0, -5.4, 5.4,
                0.05, 0,
                0, 0.05,
                81, 81,
                azimuthMinutes, altitudeMinutes);
        }
    }
}
