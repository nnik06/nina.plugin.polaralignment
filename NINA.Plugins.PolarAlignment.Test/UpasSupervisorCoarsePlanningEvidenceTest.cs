using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NINA.Plugins.PolarAlignment;
using NUnit.Framework;
using System;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class UpasSupervisorCoarsePlanningEvidenceTest {
        private const string Nonce = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string EvidenceId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        [Test]
        public void OneAuthenticatedSnapshotBindsEveryPlanningComponent() {
            var json = ValidJson();
            var result = Parse(json);

            result.Envelope.EvidenceId.Should().Be(EvidenceId);
            result.Axes.Azimuth.PositionDegrees.Should().Be(0.25);
            result.ResponseCalibration.CalibrationId.Should().Be(
                Guid.Parse("30000000-0000-0000-0000-000000000001"));
            result.Runtime.MotionAuthorityIncluded.Should().BeFalse();
            result.ResponseContentSha256.Should().Be(
                UpasSupervisorCoarsePlanningEvidenceParser.Digest(json));
        }

        [Test]
        public void AuthenticationMethodBodyDigestAndEvidenceIdentityAreAllRequired() {
            var json = ValidJson();
            var digest = UpasSupervisorCoarsePlanningEvidenceParser.Digest(json);

            Action wrongMethod = () => Parse(json, authenticationMethod: "httpBearer");
            Action wrongBody = () => Parse(json, contentSha256: new string('d', 64));
            Action wrongIdentity = () => Parse(json, evidenceId: new string('e', 64));

            wrongMethod.Should().Throw<JsonException>().WithMessage("*authenticated HTTPS*");
            wrongBody.Should().Throw<JsonException>().WithMessage("*response bytes*");
            wrongIdentity.Should().Throw<JsonException>().WithMessage("*identity*");
            digest.Should().HaveLength(64);
        }

        [Test]
        public void CrossBootCalibrationSplicingFailsInsideAggregateParse() {
            var json = ValidJson();
            const string original =
                "\"supervisorBootId\": \"a7901d62-e1e8-4cce-bd6a-e3e986d8cd8a\"";
            var index = json.LastIndexOf(original, StringComparison.Ordinal);
            index.Should().BeGreaterThan(0);
            json = json.Remove(index, original.Length).Insert(index,
                "\"supervisorBootId\": \"f0000000-0000-0000-0000-000000000001\"");

            Action parse = () => Parse(json);

            parse.Should().Throw<JsonException>().WithMessage("*different supervisor boot*");
        }

        [Test]
        public void SnapshotMustStillBeFreshAtPlanEmission() {
            var evidence = Parse(ValidJson());

            Action stale = () => evidence.RequireFreshAtPlanEmission(4500.1);

            stale.Should().Throw<JsonException>().WithMessage("*stale*");
        }

        [Test]
        public void PlanReceiptIsDeterministicTamperEvidentAndExplicitlyNonAuthoritative() {
            var evidence = Parse(ValidJson());
            var error = new TppaCoarseErrorEvidence(180.0, 60.0, 1.0, 0.0, 1.0);
            var plan = TppaCoarseVectorPlanner.Plan(
                error, evidence.Axes, evidence.ResponseCalibration, evidence.Runtime);
            plan.IsAuthorized.Should().BeTrue();
            var first = TppaCoarsePlanningReceipt.Create(evidence, error, plan);
            var second = TppaCoarsePlanningReceipt.Create(evidence, error, plan);

            first.ReceiptContentSha256.Should().Be(second.ReceiptContentSha256);
            first.VerifyIntegrity().Should().BeTrue();
            var json = JObject.Parse(first.ToJson());
            json["motionAuthorityIncluded"]!.Value<bool>().Should().BeFalse();
            json["supervisorEvidence"]!["evidenceId"]!.Value<string>()
                .Should().Be(EvidenceId);
            json["responseCalibration"]!["artifactSha256"]!.Value<string>()
                .Should().Be(new string('c', 64));
            json["plan"]!["expandedPathMillidegrees"]!["az"]!["lower"]
                .Type.Should().Be(JTokenType.Integer);

            var changedError = error with { AzimuthMinutes = 179.0 };
            var changedPlan = TppaCoarseVectorPlanner.Plan(
                changedError, evidence.Axes, evidence.ResponseCalibration, evidence.Runtime);
            TppaCoarsePlanningReceipt.Create(evidence, changedError, changedPlan)
                .ReceiptContentSha256.Should().NotBe(first.ReceiptContentSha256);
        }

        [Test]
        public void AggregateAndReceiptTypesCannotReachMotionSurfaces() {
            foreach (var type in new[] {
                typeof(UpasSupervisorCoarsePlanningEvidence),
                typeof(TppaCoarsePlanningReceipt)
            }) {
                foreach (var property in type.GetProperties()) {
                    property.PropertyType.Should().NotBe(typeof(IAutomatedMoveExecutor));
                    property.PropertyType.Should().NotBe(typeof(IPolarAlignmentSystemVM));
                }
            }
        }

        private static UpasSupervisorCoarsePlanningEvidence Parse(
                string json,
                string? contentSha256 = null,
                string evidenceId = EvidenceId,
                string authenticationMethod = "httpsBearer") =>
            UpasSupervisorCoarsePlanningEvidenceParser.ParseAuthenticated(
                json, Nonce, 100.0, null, 35.0, "hae29c-ec-full-rig-v1",
                contentSha256 ?? UpasSupervisorCoarsePlanningEvidenceParser.Digest(json),
                evidenceId, authenticationMethod);

        internal static string ValidJson() => """
            {
              "schemaVersion": 2,
              "supervisorSessionId": "acaa2759-b829-41ec-9e93-59449a3ac38c",
              "supervisorBootId": "a7901d62-e1e8-4cce-bd6a-e3e986d8cd8a",
              "evidenceId": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
              "clientRequestNonce": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "capturedUtc": "2026-08-03T12:00:00Z",
              "capturedMonotonicNs": 123456789,
              "serverProcessingMilliseconds": 25.0,
              "validForMilliseconds": 5000.0,
              "oldestEvidenceAgeMilliseconds": 400.0,
              "coordinateConvention": "azEastPositive_altUpPositive",
              "hardLimitDegrees": {
                "az": { "minimum": -5.4, "maximum": 5.4 },
                "alt": { "minimum": -5.4, "maximum": 5.4 }
              },
              "operationalReserveDegrees": 1.0,
              "operationalLimitDegrees": {
                "az": { "minimum": -4.4, "maximum": 4.4 },
                "alt": { "minimum": -4.4, "maximum": 4.4 }
              },
              "axes": {
                "az": {
                  "positionDegrees": 0.25,
                  "positionStandardUncertaintyDegrees": 0.01,
                  "engagementState": "positive",
                  "agreement": "pass",
                  "witnesses": [
                    {
                      "witnessId": "10000000-0000-0000-0000-000000000001",
                      "artifactSha256": "1111111111111111111111111111111111111111111111111111111111111111",
                      "ageAtResponseMilliseconds": 100.0,
                      "sourceId": "hall-az-1",
                      "correlationGroup": "hall-az",
                      "sensorCalibrationId": "20000000-0000-0000-0000-000000000001",
                      "coordinateConvention": "azEastPositive_altUpPositive"
                    },
                    {
                      "witnessId": "10000000-0000-0000-0000-000000000002",
                      "artifactSha256": "2222222222222222222222222222222222222222222222222222222222222222",
                      "ageAtResponseMilliseconds": 120.0,
                      "sourceId": "p20-frame-42",
                      "correlationGroup": "p20-optical",
                      "sensorCalibrationId": "20000000-0000-0000-0000-000000000002",
                      "coordinateConvention": "azEastPositive_altUpPositive"
                    }
                  ]
                },
                "alt": {
                  "positionDegrees": -0.25,
                  "positionStandardUncertaintyDegrees": 0.01,
                  "engagementState": "negative",
                  "agreement": "pass",
                  "witnesses": [
                    {
                      "witnessId": "10000000-0000-0000-0000-000000000003",
                      "artifactSha256": "3333333333333333333333333333333333333333333333333333333333333333",
                      "ageAtResponseMilliseconds": 110.0,
                      "sourceId": "hall-alt-1",
                      "correlationGroup": "hall-alt",
                      "sensorCalibrationId": "20000000-0000-0000-0000-000000000003",
                      "coordinateConvention": "azEastPositive_altUpPositive"
                    },
                    {
                      "witnessId": "10000000-0000-0000-0000-000000000004",
                      "artifactSha256": "4444444444444444444444444444444444444444444444444444444444444444",
                      "ageAtResponseMilliseconds": 130.0,
                      "sourceId": "p20-frame-42",
                      "correlationGroup": "p20-optical",
                      "sensorCalibrationId": "20000000-0000-0000-0000-000000000004",
                      "coordinateConvention": "azEastPositive_altUpPositive"
                    }
                  ]
                },
                "positionCovarianceSquareDegrees": [[0.0001, 0.0], [0.0, 0.0001]]
              },
              "responseCalibration": {
                "schemaVersion": 2,
                "calibrationId": "30000000-0000-0000-0000-000000000001",
                "artifactSha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                "capturedUtc": "2026-08-03T11:59:59.0000000Z",
                "equation": "tppaErrorAfter=tppaErrorBefore+R*physicalDelta",
                "units": "degrees",
                "coordinateConvention": "azEastPositive_altUpPositive",
                "responseMatrix": {
                  "coordinateConvention": "azEastPositive_altUpPositive",
                  "values": [[1.0, 0.05], [0.02, 1.1]]
                },
                "matrixElementCovariance": [
                  [0.0001, 0.0, 0.0, 0.0], [0.0, 0.0001, 0.0, 0.0],
                  [0.0, 0.0, 0.0001, 0.0], [0.0, 0.0, 0.0, 0.0001]
                ],
                "fixedCommandUncertaintyDegrees": { "az": 0.01, "alt": 0.01 },
                "deadbandDegrees": {
                  "az": { "positive": 0.08, "negative": 0.10 },
                  "alt": { "positive": 0.04, "negative": 0.05 }
                },
                "applicablePositionDegrees": {
                  "az": { "minimum": -4.4, "maximum": 4.4 },
                  "alt": { "minimum": -4.4, "maximum": 4.4 }
                },
                "applicableDirections": {
                  "az": ["positive", "negative"],
                  "alt": ["positive", "negative"]
                },
                "temperatureC": { "minimum": 25.0, "maximum": 55.0 },
                "loadProfileId": "hae29c-ec-full-rig-v1",
                "ageAtResponseMilliseconds": 200.0,
                "validForMilliseconds": 5000.0,
                "supervisorBootId": "a7901d62-e1e8-4cce-bd6a-e3e986d8cd8a"
              },
              "remainingTravelBudgetDegrees": {
                "signConvention": "adjusterIncreasing",
                "az": { "positiveDegrees": 4.0, "negativeDegrees": 4.0 },
                "alt": { "positiveDegrees": 4.0, "negativeDegrees": 4.0 },
                "cumulativeSessionDegrees": 8.0
              },
              "activeLeaseId": null,
              "activeTransactionId": null,
              "lockedReason": null,
              "capabilities": {
                "planningEvidence": true,
                "physicalMotionAvailable": true,
                "atomicBudgetReservationAvailable": true,
                "motionAuthorityIncluded": false
              }
            }
            """;
    }
}
