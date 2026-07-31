using FluentAssertions;
using NINA.Plugins.PolarAlignment.Instructions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System;
using System.Linq;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaFastQualificationReceiptVerifierTest {
        private const string PolicyDigest =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string SourceVectorDigest =
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        [Test]
        public void GeneratedReceiptRoundTripsThroughIndependentVerifier() {
            var receipt = CreateReceipt();

            var result = TppaFastQualificationReceiptVerifier.Verify(
                receipt.ToJson(),
                PolicyDigest,
                SourceVectorDigest);

            result.Issues.Should().BeEmpty();
            result.IsValid.Should().BeTrue();
            result.QualificationInput.Should().Be(QualifiedInput());
        }

        [Test]
        public void TamperedCampaignFailsContentDigestVerification() {
            var payload = JObject.Parse(CreateReceipt().ToJson());
            payload["campaignId"] = "tampered-campaign";

            var result = TppaFastQualificationReceiptVerifier.Verify(
                payload.ToString(),
                PolicyDigest);

            result.IsValid.Should().BeFalse();
            result.Issues.Should().Contain(
                issue => issue.Contains("receipt content digest"));
        }

        [Test]
        public void TamperedInputFailsEnvelopeDigestAndVerdictRecomputation() {
            var payload = JObject.Parse(CreateReceipt().ToJson());
            payload["qualificationInputEnvelope"]!["input"]![
                nameof(TppaFastQualificationInput.FinalReportedErrorArcMinutes)] = 2.0;

            var result = TppaFastQualificationReceiptVerifier.Verify(
                payload.ToString(),
                PolicyDigest);

            result.IsValid.Should().BeFalse();
            result.Issues.Should().Contain(
                issue => issue.Contains("input envelope digest"));
            result.Issues.Should().Contain(
                issue => issue.Contains("stored qualification verdict"));
        }

        [Test]
        public void MotionAuthorityIsAlwaysRejected() {
            var payload = JObject.Parse(CreateReceipt().ToJson());
            payload["grantsMotionAuthority"] = true;

            var result = TppaFastQualificationReceiptVerifier.Verify(
                payload.ToString(),
                PolicyDigest);

            result.IsValid.Should().BeFalse();
            result.Issues.Should().Contain(
                issue => issue.Contains("deny motion authority"));
        }

        [Test]
        public void DuplicateAndUnexpectedPropertiesFailClosed() {
            var json = CreateReceipt().ToJson();
            var duplicate = json.Insert(
                json.Length - 1,
                ",\"campaignId\":\"duplicate\"");
            var unexpected = JObject.Parse(json);
            unexpected["motionCommand"] = "X+300";

            var duplicateResult =
                TppaFastQualificationReceiptVerifier.Verify(duplicate);
            var unexpectedResult =
                TppaFastQualificationReceiptVerifier.Verify(unexpected.ToString());

            duplicateResult.IsValid.Should().BeFalse();
            duplicateResult.Issues.Should().Contain(
                issue => issue.Contains("receipt JSON is invalid"));
            unexpectedResult.IsValid.Should().BeFalse();
            unexpectedResult.Issues.Should().Contain(
                issue => issue.Contains("not recognized"));
        }

        [Test]
        public void WrongPolicySourceAndPolicyParametersFailClosed() {
            var payload = JObject.Parse(CreateReceipt().ToJson());
            payload["policyParametersEnvelope"]!["policy"]![
                nameof(TppaFastQualificationPolicy.MaximumDurationSeconds)] = 600.0;

            var result = TppaFastQualificationReceiptVerifier.Verify(
                payload.ToString(),
                "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc");

            result.IsValid.Should().BeFalse();
            result.Issues.Should().Contain(
                issue => issue.Contains("expected deployed policy"));
            result.Issues.Should().Contain(
                issue => issue.Contains("policy parameters envelope digest"));
            result.Issues.Should().Contain(
                issue => issue.Contains("frozen default qualification policy"));
        }

        [Test]
        public void IndependentSourceVectorDigestMismatchFailsClosed() {
            var result = TppaFastQualificationReceiptVerifier.Verify(
                CreateReceipt().ToJson(),
                PolicyDigest,
                "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc");

            result.IsValid.Should().BeFalse();
            result.Issues.Should().Contain(
                issue => issue.Contains("independent expected digest"));
        }

        [TestCase("")]
        [TestCase("ABCDEF")]
        [TestCase(
            "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB")]
        public void MalformedIndependentSourceVectorDigestFailsClosed(
                string expectedDigest) {
            var result = TppaFastQualificationReceiptVerifier.Verify(
                CreateReceipt().ToJson(),
                PolicyDigest,
                expectedDigest);

            result.IsValid.Should().BeFalse();
            result.Issues.Should().Contain(
                issue => issue.Contains("expected source polar-error vector digest"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("{not-json")]
        public void MissingOrMalformedJsonReturnsRefusalInsteadOfThrowing(string json) {
            var action = () => TppaFastQualificationReceiptVerifier.Verify(json);

            action.Should().NotThrow();
            var result = action();
            result.IsValid.Should().BeFalse();
            result.Issues.Should().NotBeEmpty();
            result.QualificationInput.Should().BeNull();
        }

        private static TppaFastQualificationReceipt CreateReceipt() =>
            TppaFastQualificationReceipt.Create(
                "campaign-20260731",
                new DateTime(2026, 7, 31, 20, 0, 0, DateTimeKind.Utc),
                PolicyDigest,
                SourceVectorDigest,
                QualifiedInput());

        private static TppaFastQualificationInput QualifiedInput() =>
            new(
                DurationSeconds: 242,
                FreshDeterminationCount: 3,
                FreshSolvesUncached: true,
                HardwareConfigurationId: "hardware-epoch-7",
                ClockDomainId: "mele-monotonic-boot-4",
                TppaInstrumentId: "main-camera-train",
                TppaInputPathDigest:
                    "1111111111111111111111111111111111111111111111111111111111111111",
                SolverIdentity: "astap-2026.07",
                MaximumPairwiseDeltaArcMinutes: 0.22,
                FinalReportedErrorArcMinutes: 0.61,
                DeltaMetric:
                    TppaFastQualificationConventions.SphericalVectorSeparationArcMinutes,
                ErrorMetric:
                    TppaFastQualificationConventions.SphericalPolarErrorMagnitudeArcMinutes,
                NoPhysicalAdjustmentBetweenDeterminations: true,
                GeometryQualified: true,
                MinimumArcSpanQualified: true,
                ClosureQualified: true,
                RefractionAdjustmentEnabled: true,
                PoleTarget: RefractionAlignmentTarget.TruePoleTarget,
                AtmosphereSource:
                    TppaFastQualificationConventions.QualifiedLocalWeatherStation,
                AtmosphereQualified: true,
                AtmosphereFresh: true,
                StationPressureQualified: true,
                AtmosphereTemperatureQualified: true,
                AtmosphereHumidityQualified: true,
                SiteTimeProvenanceQualified: true,
                CoordinateFrame: TppaFastQualificationConventions.IcrsObservationEpoch,
                CoordinateFrameQualified: true,
                IndependentWitnessQualified: true,
                IndependentWitnessSameMechanicalState: true,
                IndependentWitnessDisjointInputPathQualified: true,
                IndependentWitnessInstrumentId: "ipolar-camera",
                IndependentWitnessInputPathDigest:
                    "2222222222222222222222222222222222222222222222222222222222222222",
                IndependentWitnessPoleTarget: RefractionAlignmentTarget.TruePoleTarget,
                IndependentWitnessCoordinateFrame:
                    TppaFastQualificationConventions.IcrsObservationEpoch,
                IndependentWitnessCalibrationDigest:
                    "3333333333333333333333333333333333333333333333333333333333333333",
                IndependentWitnessCalibrationCurrent: true,
                IndependentWitnessUncertainty: QualifiedWitnessUncertainty(),
                IndependentTruePoleErrorArcMinutes: 0.25,
                TppaToIndependentDeltaArcMinutes: 0.25);

        private static TppaWitnessUncertaintyEvidence QualifiedWitnessUncertainty() {
            var evidence = new TppaWitnessUncertaintyEvidence(
                ModelId: "qualified-independent-pole-camera-v1",
                InputDigest:
                    "2222222222222222222222222222222222222222222222222222222222222222",
                EvidenceDigest: new string('0', 64),
                CalibrationSampleCount: 6,
                ClosureSampleCount: 3,
                MeasurementStandardUncertaintyArcSeconds: 1,
                CalibrationResidualArcSeconds: 2,
                OrientationModelResidualArcSeconds: 1,
                ClosureResidualArcSeconds: 1,
                FrameSystematicBoundArcSeconds: 1,
                DistortionSystematicBoundArcSeconds: 1,
                MechanicalSystematicBoundArcSeconds: 1);
            return evidence with {
                EvidenceDigest = TppaWitnessUncertainty.ComputeEvidenceDigest(evidence)
            };
        }
    }
}
