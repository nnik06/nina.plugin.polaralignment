using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaDirectFullTravelReadinessTest {
        [Test]
        public void Evaluate_ReportsNotEnabledWithoutTreatingItAsARouteFailure() {
            var result = Evaluate(enabled: false, confirmed: false);

            result.IsReady.Should().BeFalse();
            result.Reason.Should().Contain("not enabled");
        }

        [Test]
        public void Evaluate_ReportsReadyAtColdStartWhenTheLegacyConfirmationFlagIsFalse() {
            var result = Evaluate(enabled: true, confirmed: false);

            result.IsReady.Should().BeTrue();
            result.RequiresBoundedYBootstrap.Should().BeFalse();
            result.Reason.Should().Contain("fresh TPPA error");
        }

        [Test]
        public void Evaluate_ReportsMissingYResponseAsABoundedBootstrapRoute() {
            var result = TppaDirectFullTravelReadiness.Evaluate(
                true, true,
                0, -5.4, 5.4,
                0, -5.4, 5.4,
                0.05, 0,
                0, 0,
                81, 81,
                0.05, 0.05);

            result.IsReady.Should().BeTrue();
            result.RequiresBoundedYBootstrap.Should().BeTrue();
            result.Reason.Should().Contain("bounded Y bootstrap");
        }

        [Test]
        public void Evaluate_ReportsReadyWithoutPretendingTheFreshErrorWasQualified() {
            var result = Evaluate(enabled: true, confirmed: true);

            result.IsReady.Should().BeTrue();
            result.RequiresBoundedYBootstrap.Should().BeFalse();
            result.Reason.Should().Contain("fresh TPPA error");
            result.Reason.Should().Contain("motion-time");
        }

        private static TppaDirectFullTravelReadiness Evaluate(bool enabled, bool confirmed) {
            return TppaDirectFullTravelReadiness.Evaluate(
                enabled, confirmed,
                0, -5.4, 5.4,
                0, -5.4, 5.4,
                0.05, 0,
                0, 0.05,
                81, 81,
                0.05, 0.05);
        }
    }
}
