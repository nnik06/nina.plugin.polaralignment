using FluentAssertions;
using NINA.Plugins.PolarAlignment;
using NINA.Plugins.PolarAlignment.Instructions;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaFastQualificationTest {
        private static TppaFastQualificationInput QualifiedInput() =>
            new(
                DurationSeconds: 238,
                FreshDeterminationCount: 3,
                FreshSolvesUncached: true,
                HardwareConfigurationId: "main-camera-mount-epoch-001",
                ClockDomainId: "mele-monotonic-v1",
                TppaInstrumentId: "main-camera-solver-001",
                TppaInputPathDigest: new string('d', 64),
                SolverIdentity: "astap-2026.1",
                MaximumPairwiseDeltaArcMinutes: 0.22,
                FinalReportedErrorArcMinutes: 0.65,
                DeltaMetric: TppaFastQualificationConventions.SphericalVectorSeparationArcMinutes,
                ErrorMetric: TppaFastQualificationConventions.SphericalPolarErrorMagnitudeArcMinutes,
                NoPhysicalAdjustmentBetweenDeterminations: true,
                GeometryQualified: true,
                MinimumArcSpanQualified: true,
                ClosureQualified: true,
                RefractionAdjustmentEnabled: true,
                PoleTarget: RefractionAlignmentTarget.TruePoleTarget,
                AtmosphereSource: TppaFastQualificationConventions.QualifiedLocalWeatherStation,
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
                IndependentWitnessInstrumentId: "ipolar-serial-001",
                IndependentWitnessInputPathDigest: new string('e', 64),
                IndependentWitnessPoleTarget: RefractionAlignmentTarget.TruePoleTarget,
                IndependentWitnessCoordinateFrame: TppaFastQualificationConventions.IcrsObservationEpoch,
                IndependentWitnessCalibrationDigest: new string('a', 64),
                IndependentWitnessCalibrationCurrent: true,
                IndependentWitnessUncertainty: QualifiedWitnessUncertainty(),
                IndependentTruePoleErrorArcMinutes: 0.25,
                TppaToIndependentDeltaArcMinutes: 0.30);

        private static TppaWitnessUncertaintyEvidence QualifiedWitnessUncertainty() {
            var evidence = new TppaWitnessUncertaintyEvidence(
                ModelId: "qualified-independent-pole-camera-v1",
                InputDigest: new string('e', 64),
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

        [Test]
        public void PassingEveryGateSupportsTheNarrowFastTruePoleClaim() {
            var result = TppaFastQualification.Evaluate(QualifiedInput());

            result.IsFastTruePoleQualified.Should().BeTrue();
            result.Issues.Should().BeEmpty();
        }

        [Test]
        public void RepeatableFastTppaWithoutIndependentTruthRemainsUnproven() {
            var input = QualifiedInput() with {
                IndependentWitnessQualified = false,
                IndependentTruePoleErrorArcMinutes = null,
                TppaToIndependentDeltaArcMinutes = null
            };

            var result = TppaFastQualification.Evaluate(input);

            result.IsFastTruePoleQualified.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("independent"));
        }

        [Test]
        public void ApparentPoleCannotPassEvenWhenReportedErrorIsZero() {
            var input = QualifiedInput() with {
                FinalReportedErrorArcMinutes = 0,
                RefractionAdjustmentEnabled = false,
                PoleTarget = RefractionAlignmentTarget.ApparentPoleTarget
            };

            var result = TppaFastQualification.Evaluate(input);

            result.IsFastTruePoleQualified.Should().BeFalse();
            result.Issues.Should().ContainSingle(issue => issue.Contains("true celestial pole"));
        }

        [Test]
        public void SpeedAndAccuracyGatesFailIndependently() {
            var duration = TppaFastQualification.Evaluate(
                QualifiedInput() with { DurationSeconds = 301 });
            var accuracy = TppaFastQualification.Evaluate(
                QualifiedInput() with { FinalReportedErrorArcMinutes = 1.01 });

            duration.Issues.Should().ContainSingle(issue => issue.Contains("duration"));
            accuracy.Issues.Should().ContainSingle(issue => issue.Contains("reported final error"));
        }

        [Test]
        public void ThreeFreshUncachedDeterminationsAreRequired() {
            var tooFew = TppaFastQualification.Evaluate(
                QualifiedInput() with { FreshDeterminationCount = 2 });
            var cached = TppaFastQualification.Evaluate(
                QualifiedInput() with { FreshSolvesUncached = false });

            tooFew.Issues.Should().ContainSingle(issue => issue.Contains("only 2"));
            cached.Issues.Should().ContainSingle(issue => issue.Contains("cached"));
        }

        [Test]
        public void SameArcAgreementCannotHideGeometrySpanOrClosureFailure() {
            var input = QualifiedInput() with {
                MaximumPairwiseDeltaArcMinutes = 0.05,
                GeometryQualified = false,
                MinimumArcSpanQualified = false,
                ClosureQualified = false
            };

            var result = TppaFastQualification.Evaluate(input);

            result.Issues.Should().Contain(issue => issue.Contains("geometry"));
            result.Issues.Should().Contain(issue => issue.Contains("arc span"));
            result.Issues.Should().Contain(issue => issue.Contains("closure"));
        }

        [Test]
        public void RepeatabilityRequiresNoMotionAndSphericalVectorMetric() {
            var input = QualifiedInput() with {
                NoPhysicalAdjustmentBetweenDeterminations = false,
                DeltaMetric = "raw-alt-az-component-difference"
            };

            var result = TppaFastQualification.Evaluate(input);

            result.Issues.Should().Contain(issue => issue.Contains("physical state"));
            result.Issues.Should().Contain(issue => issue.Contains("spherical-vector"));
        }

        [Test]
        public void AtmosphereSiteTimeAndCoordinateFrameMustBeQualified() {
            var input = QualifiedInput() with {
                AtmosphereSource = "standard-atmosphere-fallback",
                AtmosphereQualified = false,
                AtmosphereFresh = false,
                StationPressureQualified = false,
                AtmosphereTemperatureQualified = false,
                AtmosphereHumidityQualified = false,
                SiteTimeProvenanceQualified = false,
                CoordinateFrameQualified = false
            };

            var result = TppaFastQualification.Evaluate(input);

            result.Issues.Should().Contain(issue => issue.Contains("station pressure"));
            result.Issues.Should().Contain(issue => issue.Contains("site coordinates"));
            result.Issues.Should().Contain(issue => issue.Contains("coordinate frame"));
        }

        [Test]
        public void AtmosphereAndCoordinateFrameUseQualifiedAllowlists() {
            var atmosphere = TppaFastQualification.Evaluate(
                QualifiedInput() with {
                    AtmosphereSource = "standard-atmosphere-fallback"
                });
            var frame = TppaFastQualification.Evaluate(
                QualifiedInput() with {
                    CoordinateFrame = "J2000",
                    IndependentWitnessCoordinateFrame = "J2000"
                });

            atmosphere.Issues.Should().Contain(issue => issue.Contains("atmosphere provenance"));
            frame.Issues.Should().Contain(issue => issue.Contains("coordinate frame"));
        }

        [Test]
        public void InstrumentAndInputPathMustBeActuallyDisjoint() {
            var result = TppaFastQualification.Evaluate(
                QualifiedInput() with {
                    IndependentWitnessInstrumentId = "main-camera-solver-001",
                    IndependentWitnessInputPathDigest = new string('d', 64)
                });

            result.Issues.Should().Contain(issue => issue.Contains("not independent"));
            result.Issues.Should().Contain(issue => issue.Contains("aliases"));
        }

        [Test]
        public void WitnessMustBeDisjointIdentifiedCurrentAndFrameMatched() {
            var input = QualifiedInput() with {
                IndependentWitnessSameMechanicalState = false,
                IndependentWitnessDisjointInputPathQualified = false,
                IndependentWitnessInstrumentId = string.Empty,
                IndependentWitnessPoleTarget = RefractionAlignmentTarget.ApparentPoleTarget,
                IndependentWitnessCoordinateFrame = "J2000",
                IndependentWitnessCalibrationDigest = string.Empty,
                IndependentWitnessCalibrationCurrent = false
            };

            var result = TppaFastQualification.Evaluate(input);

            result.Issues.Should().Contain(issue => issue.Contains("same mechanical state"));
            result.Issues.Should().Contain(issue => issue.Contains("disjoint input path"));
            result.Issues.Should().Contain(issue => issue.Contains("instrument identity"));
            result.Issues.Should().Contain(issue => issue.Contains("true-pole convention"));
            result.Issues.Should().Contain(issue => issue.Contains("coordinate frame"));
            result.Issues.Should().Contain(issue => issue.Contains("calibration provenance"));
        }

        [Test]
        public void WitnessErrorAndDisagreementMustComposeInsideOneArcMinute() {
            var result = TppaFastQualification.Evaluate(
                QualifiedInput() with {
                    IndependentTruePoleErrorArcMinutes = 0.6,
                    TppaToIndependentDeltaArcMinutes = 0.5
                },
                new TppaFastQualificationPolicy(
                    MaximumIndependentErrorArcMinutes: 0.7,
                    MaximumIndependentDeltaArcMinutes: 0.7,
                    MaximumCombinedAbsoluteErrorArcMinutes: 1.0));

            result.Issues.Should().ContainSingle(issue => issue.Contains("absolute-error budget"));
        }

        [Test]
        public void PointEstimateAtBoundaryCannotHideWitnessUncertainty() {
            var result = TppaFastQualification.Evaluate(
                QualifiedInput() with {
                    DurationSeconds = 300,
                    FreshDeterminationCount = 3,
                    MaximumPairwiseDeltaArcMinutes = 0.5,
                    FinalReportedErrorArcMinutes = 1.0,
                    IndependentTruePoleErrorArcMinutes = 0.5,
                    TppaToIndependentDeltaArcMinutes = 0.5
                });

            result.IsFastTruePoleQualified.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("true-pole error"));
            result.Issues.Should().Contain(issue => issue.Contains("do not agree"));
        }

        [Test]
        public void BooleanOnlyWitnessCannotQualifyWithoutMeasuredUncertainty() {
            var result = TppaFastQualification.Evaluate(
                QualifiedInput() with { IndependentWitnessUncertainty = null });

            result.IsFastTruePoleQualified.Should().BeFalse();
            result.Issues.Should().Contain(
                issue => issue.Contains("uncertainty evidence is missing"));
        }

        [Test]
        public void FieldReplayRejectsFiveArcminuteIpolarDisagreement() {
            var result = TppaFastQualification.Evaluate(
                QualifiedInput() with {
                    IndependentTruePoleErrorArcMinutes = 109.96,
                    TppaToIndependentDeltaArcMinutes = 5.0
                });

            result.IsFastTruePoleQualified.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("true-pole error"));
            result.Issues.Should().Contain(issue => issue.Contains("do not agree"));
        }

        [Test]
        public void ShadowReceiptBindsCampaignInputVectorAndNeverGrantsMotion() {
            var created = new DateTime(2026, 7, 31, 20, 0, 0, DateTimeKind.Utc);
            var receipt = TppaFastQualificationReceipt.Create(
                "campaign-2026-07-31",
                created,
                new string('b', 64),
                new string('c', 64),
                QualifiedInput());

            receipt.IsFastTruePoleQualified.Should().BeTrue();
            receipt.GrantsMotionAuthority.Should().BeFalse();
            receipt.QualificationInputSha256.Should().HaveLength(64);
            receipt.PolicyParametersSha256.Should().HaveLength(64);
            receipt.ReceiptContentSha256.Should().HaveLength(64);
            receipt.HasValidContentDigest().Should().BeTrue();

            var json = JObject.Parse(receipt.ToJson());
            json.Value<int>("schemaVersion").Should().Be(TppaFastQualificationReceipt.CurrentSchemaVersion);
            json.Value<string>("campaignId").Should().Be("campaign-2026-07-31");
            json.Value<string>("sourcePolarErrorVectorDigest").Should().Be(new string('c', 64));
            json.Value<bool>("grantsMotionAuthority").Should().BeFalse();
            json.Value<DateTime>("createdUtc").Kind.Should().Be(DateTimeKind.Utc);
            json.Value<string>("policyParametersSha256").Should().Be(receipt.PolicyParametersSha256);
            json.Value<string>("receiptContentSha256").Should().Be(receipt.ReceiptContentSha256);
            json["qualificationInputEnvelope"]!["input"]![
                nameof(TppaFastQualificationInput.HardwareConfigurationId)]!
                .Value<string>().Should().Be("main-camera-mount-epoch-001");
            json["policyParametersEnvelope"]!["policy"]![
                nameof(TppaFastQualificationPolicy.MaximumDurationSeconds)]!
                .Value<double>().Should().Be(300);
        }

        [Test]
        public void ShadowReceiptDigestChangesWhenQualificationEvidenceChanges() {
            var created = new DateTime(2026, 7, 31, 20, 0, 0, DateTimeKind.Utc);
            var passing = TppaFastQualificationReceipt.Create(
                "campaign-2026-07-31",
                created,
                new string('b', 64),
                new string('c', 64),
                QualifiedInput());
            var failing = TppaFastQualificationReceipt.Create(
                "campaign-2026-07-31",
                created,
                new string('b', 64),
                new string('c', 64),
                QualifiedInput() with { RefractionAdjustmentEnabled = false });

            failing.IsFastTruePoleQualified.Should().BeFalse();
            failing.QualificationInputSha256.Should().NotBe(
                passing.QualificationInputSha256);
        }

        [Test]
        public void ReceiptContentDigestBindsCampaignVectorPolicyAndVerdict() {
            var created = new DateTime(2026, 7, 31, 20, 0, 0, DateTimeKind.Utc);
            var baseline = TppaFastQualificationReceipt.Create(
                "campaign-a", created, new string('b', 64), new string('c', 64), QualifiedInput());
            var differentCampaign = TppaFastQualificationReceipt.Create(
                "campaign-b", created, new string('b', 64), new string('c', 64), QualifiedInput());
            var differentVector = TppaFastQualificationReceipt.Create(
                "campaign-a", created, new string('b', 64), new string('f', 64), QualifiedInput());
            var repeat = TppaFastQualificationReceipt.Create(
                "campaign-a", created, new string('b', 64), new string('c', 64), QualifiedInput());

            baseline.QualificationInputSha256.Should().Be(
                differentCampaign.QualificationInputSha256);
            baseline.ReceiptContentSha256.Should().NotBe(
                differentCampaign.ReceiptContentSha256);
            baseline.ReceiptContentSha256.Should().NotBe(
                differentVector.ReceiptContentSha256);
            baseline.ReceiptContentSha256.Should().Be(repeat.ReceiptContentSha256);
            baseline.ToJson().Should().Be(repeat.ToJson());
        }

        [Test]
        public void ShadowReceiptRejectsAmbiguousIdentityAndTime() {
            Action localTime = () => TppaFastQualificationReceipt.Create(
                "campaign", DateTime.Now, new string('b', 64), new string('c', 64), QualifiedInput());
            Action badDigest = () => TppaFastQualificationReceipt.Create(
                "campaign", DateTime.UtcNow, "BAD", new string('c', 64), QualifiedInput());

            localTime.Should().Throw<ArgumentException>().WithMessage("*UTC*");
            badDigest.Should().Throw<ArgumentException>().WithMessage("*lowercase SHA-256*");
        }

        [Test]
        public void ReceiptIssuesAreInvariantAcrossHostCulture() {
            var originalCulture = CultureInfo.CurrentCulture;
            try {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var receipt = TppaFastQualificationReceipt.Create(
                    "campaign",
                    new DateTime(2026, 7, 31, 20, 0, 0, DateTimeKind.Utc),
                    new string('b', 64),
                    new string('c', 64),
                    QualifiedInput() with {
                        MaximumPairwiseDeltaArcMinutes = 0.75,
                        FinalReportedErrorArcMinutes = 1.25
                    });

                receipt.Issues.Should().Contain(issue => issue.Contains("0.750"));
                receipt.Issues.Should().Contain(issue => issue.Contains("1.250"));
                receipt.ToJson().Should().NotContain("0,750");
            } finally {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }
    }
}
