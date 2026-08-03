using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaInitialCorrectionRouteTest {
        [TestCase(0.0)]
        [TestCase(24.0)]
        public void RoutesQualifiedFineTotalsToFineController(double totalMinutes) {
            var decision = TppaInitialCorrectionRoute.Evaluate(totalMinutes);

            decision.Stage.Should().Be(TppaInitialCorrectionStage.Fine);
        }

        [TestCase(24.001)]
        [TestCase(60.0)]
        [TestCase(300.0)]
        public void RoutesLargerQualifiedTotalsOnlyToCoarseSupervisor(double totalMinutes) {
            var decision = TppaInitialCorrectionRoute.Evaluate(totalMinutes);

            decision.Stage.Should().Be(TppaInitialCorrectionStage.Coarse);
            decision.Reason.Should().Contain("separately qualified");
        }

        [TestCase(300.001)]
        [TestCase(600.0)]
        public void RejectsTotalsAboveCoarseCampaignCeiling(double totalMinutes) {
            var decision = TppaInitialCorrectionRoute.Evaluate(totalMinutes);

            decision.Stage.Should().Be(TppaInitialCorrectionStage.Rejected);
        }

        [TestCase(-0.001)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void RejectsInvalidTotals(double totalMinutes) {
            var action = () => TppaInitialCorrectionRoute.Evaluate(totalMinutes);

            action.Should().Throw<ArgumentOutOfRangeException>();
        }
    }
}
