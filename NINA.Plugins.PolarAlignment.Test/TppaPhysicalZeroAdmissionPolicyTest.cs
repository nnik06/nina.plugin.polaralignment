using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaPhysicalZeroAdmissionPolicyTest {
        private const string Nonce =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string EvidenceId =
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        [Test]
        public void AcceptsOnlyWhenBothThreeSigmaBoundsAreInsidePhysicalZero() {
            var result = TppaPhysicalZeroAdmissionPolicy.Evaluate(
                Parse(positionAz: 0.069, positionAlt: -0.069, uncertainty: 0.01));

            result.IsEligible.Should().BeTrue(result.Reason);
            result.AzimuthAbsoluteBoundDegrees.Should().BeApproximately(0.099, 1e-12);
            result.AltitudeAbsoluteBoundDegrees.Should().BeApproximately(0.099, 1e-12);
            result.EvidenceId.Should().Be(EvidenceId);
        }

        [TestCase(0.071, 0.0)]
        [TestCase(0.0, -0.071)]
        public void RejectsWhenEitherThreeSigmaBoundExceedsPhysicalZero(
                double positionAz,
                double positionAlt) {
            var result = TppaPhysicalZeroAdmissionPolicy.Evaluate(
                Parse(positionAz, positionAlt, uncertainty: 0.01));

            result.IsEligible.Should().BeFalse();
            result.Reason.Should().Contain("does not place both UPAS axes");
        }

        [Test]
        public void RejectsNominalZeroWhenUncertaintyAloneExceedsTolerance() {
            var result = TppaPhysicalZeroAdmissionPolicy.Evaluate(
                Parse(positionAz: 0.0, positionAlt: 0.0, uncertainty: 0.034));

            result.IsEligible.Should().BeFalse();
            result.AzimuthAbsoluteBoundDegrees.Should().BeApproximately(0.102, 1e-12);
        }

        private static UpasSupervisorCoarsePlanningEvidence Parse(
                double positionAz,
                double positionAlt,
                double uncertainty) {
            var json = UpasSupervisorCoarsePlanningEvidenceTest.ValidJson()
                .Replace("\"positionDegrees\": 0.25", $"\"positionDegrees\": {positionAz:R}")
                .Replace("\"positionDegrees\": -0.25", $"\"positionDegrees\": {positionAlt:R}")
                .Replace("\"positionStandardUncertaintyDegrees\": 0.01",
                    $"\"positionStandardUncertaintyDegrees\": {uncertainty:R}")
                .Replace("[0.0001, 0.0]", $"[{uncertainty * uncertainty:R}, 0.0]")
                .Replace("[0.0, 0.0001]", $"[0.0, {uncertainty * uncertainty:R}]");
            return UpasSupervisorCoarsePlanningEvidenceParser.ParseAuthenticated(
                json,
                Nonce,
                observedRoundTripMilliseconds: 100.0,
                expectedCallerLeaseId: null,
                currentTemperatureC: 35.0,
                currentLoadProfileId: "hae29c-ec-full-rig-v1",
                authenticatedResponseContentSha256:
                    UpasSupervisorCoarsePlanningEvidenceParser.Digest(json),
                authenticatedEvidenceId: EvidenceId,
                authenticationMethod:
                    UpasSupervisorCoarsePlanningEvidenceParser.RequiredAuthenticationMethod);
        }
    }
}
