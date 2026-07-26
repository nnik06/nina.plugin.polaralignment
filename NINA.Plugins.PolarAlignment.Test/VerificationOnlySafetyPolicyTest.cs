using FluentAssertions;
using NINA.Core.Enum;

namespace NINA.Plugins.PolarAlignment.Test {
    public class VerificationOnlySafetyPolicyTest {
        private static readonly TppaMountMotionEnvelope BalconyEnvelope =
            new(25.0, 55.0, 270.0, 10.0);

        [Test]
        public void VerificationSettleOverrideUsesProfileWhenUnset() {
            TppaVerificationSettlePolicy.Resolve(3.5, 0.0).Should().Be(3.5);
            TppaVerificationSettlePolicy.Resolve(45.0, 0.0).Should().Be(45.0);
        }

        [Test]
        public void VerificationSettleOverrideReplacesProfileAndIsBounded() {
            TppaVerificationSettlePolicy.Resolve(3.5, 10.0).Should().Be(10.0);
            TppaVerificationSettlePolicy.Resolve(3.5, 100.0)
                .Should().Be(TppaVerificationSettlePolicy.MaximumOverrideSeconds);
        }

        [Test]
        public void VerificationQualificationRejectsShortLegsAndShortSettling() {
            var issues = TppaVerificationSettlePolicy.GetQualificationIssues(
                10.0,
                5.0,
                10.0);

            issues.Should().Contain(issue => issue.Contains("at least 15 degrees"));
            issues.Should().Contain(issue => issue.Contains("at least 30 seconds"));
        }

        [Test]
        public void VerificationQualificationAcceptsFieldQualifiedConfiguration() {
            TppaVerificationSettlePolicy.GetQualificationIssues(
                    15.0,
                    5.0,
                    30.0)
                .Should().BeEmpty();

            TppaVerificationSettlePolicy.GetQualificationIssues(
                    15.0,
                    30.0,
                    0.0)
                .Should().BeEmpty();
        }

        [Test]
        public void VerificationSlewAcceptsSafeConstantPierSideDestination() {
            var result = VerificationOnlySlewSafetyPolicy.Evaluate(
                true,
                BalconyEnvelope,
                315.0,
                30.0,
                PierSide.pierEast,
                PierSide.pierEast);

            result.IsSafe.Should().BeTrue();
        }

        [Test]
        public void VerificationSlewRejectsDestinationOutsideEnvelope() {
            var result = VerificationOnlySlewSafetyPolicy.Evaluate(
                true,
                BalconyEnvelope,
                20.0,
                30.0,
                PierSide.pierEast,
                PierSide.pierEast);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("azimuth");
        }

        [Test]
        public void VerificationSlewRejectsKnownPierSideChange() {
            var result = VerificationOnlySlewSafetyPolicy.Evaluate(
                true,
                BalconyEnvelope,
                315.0,
                30.0,
                PierSide.pierEast,
                PierSide.pierWest);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("pier side changes");
        }

        [Test]
        public void VerificationSlewRejectsUnknownPierSide() {
            var result = VerificationOnlySlewSafetyPolicy.Evaluate(
                true,
                BalconyEnvelope,
                315.0,
                30.0,
                PierSide.pierUnknown,
                PierSide.pierUnknown);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("unavailable");
        }

        [Test]
        public void PostSlewTelemetryRejectsObservedAltitudeOutsideEnvelope() {
            var result = VerificationOnlySlewSafetyPolicy.EvaluateActualTelemetry(
                true,
                BalconyEnvelope,
                true,
                false,
                308.02,
                56.44);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("altitude 56.44");
        }

        [Test]
        public void PostSlewTelemetryAcceptsStationaryPositionAtEnvelopeBoundary() {
            var result = VerificationOnlySlewSafetyPolicy.EvaluateActualTelemetry(
                true,
                BalconyEnvelope,
                true,
                false,
                315.0,
                55.0);

            result.IsSafe.Should().BeTrue();
        }

        [Test]
        public void PostSlewTelemetryRejectsUnavailableMount() {
            var result = VerificationOnlySlewSafetyPolicy.EvaluateActualTelemetry(
                true,
                BalconyEnvelope,
                false,
                false,
                double.NaN,
                double.NaN);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("unavailable");
        }

        [Test]
        public void PostSlewTelemetryRejectsMountStillSlewing() {
            var result = VerificationOnlySlewSafetyPolicy.EvaluateActualTelemetry(
                true,
                BalconyEnvelope,
                true,
                true,
                315.0,
                30.0);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("still reports slewing");
        }

        [Test]
        public void PostSlewTelemetryRejectsNonFinitePositionWhenEnvelopeEnabled() {
            var result = VerificationOnlySlewSafetyPolicy.EvaluateActualTelemetry(
                true,
                BalconyEnvelope,
                true,
                false,
                double.NaN,
                30.0);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("not finite");
        }

        [Test]
        public void PostSlewTelemetryRejectsNonFinitePositionWhenEnvelopeDisabled() {
            var result = VerificationOnlySlewSafetyPolicy.EvaluateActualTelemetry(
                false,
                BalconyEnvelope,
                true,
                false,
                double.NaN,
                30.0);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("not finite");
        }
    }
}
