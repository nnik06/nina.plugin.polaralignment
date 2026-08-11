using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaSignedTravelEnvelopeDiagnosticTest {
        [Test]
        public void Evaluate_RecognizesAnInwardCommandNearThePositiveLimit() {
            var result = TppaSignedTravelEnvelopeDiagnostic.Evaluate(5.2, -5.4, 5.4, -39, 0.05, 1);

            result.IsInsideEnvelope.Should().BeTrue();
            result.ProjectedPositionDegrees.Should().BeApproximately(3.25, 0.0001);
        }

        [Test]
        public void Evaluate_RejectsAnOutwardCommandNearThePositiveLimit() {
            var result = TppaSignedTravelEnvelopeDiagnostic.Evaluate(5.2, -5.4, 5.4, 5, 0.05, 1);

            result.IsInsideEnvelope.Should().BeFalse();
            result.Reason.Should().Contain("outside");
        }

        [Test]
        public void Evaluate_AppliesTheVerifiedPhysicalDirectionMultiplier() {
            var result = TppaSignedTravelEnvelopeDiagnostic.Evaluate(5.2, -5.4, 5.4, 5, 0.05, -1);

            result.IsInsideEnvelope.Should().BeTrue();
            result.ProjectedPositionDegrees.Should().BeApproximately(4.95, 0.0001);
        }

        [Test]
        public void Evaluate_RejectsAnInvalidPhysicalScale() {
            var result = TppaSignedTravelEnvelopeDiagnostic.Evaluate(0, -5.4, 5.4, 1, 0, 1);

            result.IsInsideEnvelope.Should().BeFalse();
            result.Reason.Should().Contain("invalid");
        }
    }
}
