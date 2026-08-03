using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NINA.Plugins.PolarAlignment;
using NUnit.Framework;
using System;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class UpasSupervisorCoarseAxisEvidenceTest {
        private static readonly UpasSupervisorCoarseEvidenceEnvelope Envelope = new(
            2,
            Guid.Parse("acaa2759-b829-41ec-9e93-59449a3ac38c"),
            Guid.Parse("a7901d62-e1e8-4cce-bd6a-e3e986d8cd8a"),
            new string('b', 64),
            new string('a', 64),
            DateTimeOffset.Parse("2026-08-03T12:00:00Z"),
            123456789,
            25,
            5000,
            500,
            100,
            UpasCoarsePlanningSafetyPolicy.RequiredCoordinateConvention);

        private static string ValidAxesJson => """
            {
              "az": {
                "positionDegrees": 1.25,
                "positionStandardUncertaintyDegrees": 0.01,
                "engagementState": "positive",
                "agreement": "pass",
                "witnesses": [
                  {
                    "witnessId": "10000000-0000-0000-0000-000000000001",
                    "artifactSha256": "1111111111111111111111111111111111111111111111111111111111111111",
                    "ageAtResponseMilliseconds": 100,
                    "sourceId": "hall-az-1",
                    "correlationGroup": "hall-az",
                    "sensorCalibrationId": "20000000-0000-0000-0000-000000000001",
                    "coordinateConvention": "azEastPositive_altUpPositive"
                  },
                  {
                    "witnessId": "10000000-0000-0000-0000-000000000002",
                    "artifactSha256": "2222222222222222222222222222222222222222222222222222222222222222",
                    "ageAtResponseMilliseconds": 120,
                    "sourceId": "p20-az-1",
                    "correlationGroup": "p20-optical",
                    "sensorCalibrationId": "20000000-0000-0000-0000-000000000002",
                    "coordinateConvention": "azEastPositive_altUpPositive"
                  }
                ]
              },
              "alt": {
                "positionDegrees": -0.75,
                "positionStandardUncertaintyDegrees": 0.02,
                "engagementState": "unknown",
                "agreement": "pass",
                "witnesses": [
                  {
                    "witnessId": "10000000-0000-0000-0000-000000000003",
                    "artifactSha256": "3333333333333333333333333333333333333333333333333333333333333333",
                    "ageAtResponseMilliseconds": 110,
                    "sourceId": "hall-alt-1",
                    "correlationGroup": "hall-alt",
                    "sensorCalibrationId": "20000000-0000-0000-0000-000000000003",
                    "coordinateConvention": "azEastPositive_altUpPositive"
                  },
                  {
                    "witnessId": "10000000-0000-0000-0000-000000000004",
                    "artifactSha256": "4444444444444444444444444444444444444444444444444444444444444444",
                    "ageAtResponseMilliseconds": 130,
                    "sourceId": "p20-alt-1",
                    "correlationGroup": "p20-optical",
                    "sensorCalibrationId": "20000000-0000-0000-0000-000000000004",
                    "coordinateConvention": "azEastPositive_altUpPositive"
                  }
                ]
              },
              "positionCovarianceSquareDegrees": [
                [0.0001, 0.00002],
                [0.00002, 0.0004]
              ]
            }
            """;

        private static JObject Mutable() => JObject.Parse(ValidAxesJson);

        [Test]
        public void ValidSignedAxesAndWitnessProvenanceParse() {
            var result = Parse(Mutable());

            result.Azimuth.PositionDegrees.Should().Be(1.25);
            result.Altitude.PositionDegrees.Should().Be(-0.75);
            result.Azimuth.EngagementState.Should().Be(UpasAxisEngagementState.Positive);
            result.Altitude.EngagementState.Should().Be(UpasAxisEngagementState.Unknown);
            result.CovarianceAzAltSquareDegrees.Should().Be(0.00002);
            result.IndependentCorrelationGroupCount.Should().Be(3);
        }

        [Test]
        public void WitnessDisagreementRejectsWithoutAveraging() {
            var axes = Mutable();
            axes["az"]!["agreement"] = "fail";

            Action parse = () => Parse(axes);

            parse.Should().Throw<JsonException>().WithMessage("*do not agree*");
        }

        [TestCase("sideways")]
        [TestCase("")]
        public void UnknownOrEmptyEngagementLiteralIsRejected(string value) {
            var axes = Mutable();
            axes["az"]!["engagementState"] = value;

            Action parse = () => Parse(axes);

            parse.Should().Throw<JsonException>();
        }

        [Test]
        public void WitnessOlderThanEnvelopeClaimIsRejected() {
            var axes = Mutable();
            axes["az"]!["witnesses"]![0]!["ageAtResponseMilliseconds"] = 500.1;

            Action parse = () => Parse(axes);

            parse.Should().Throw<JsonException>().WithMessage("*oldest-evidence age*");
        }

        [Test]
        public void MismatchedCoordinateConventionIsRejected() {
            var axes = Mutable();
            axes["alt"]!["witnesses"]![0]!["coordinateConvention"] = "altUp_azWest";

            Action parse = () => Parse(axes);

            parse.Should().Throw<JsonException>().WithMessage("*does not match*");
        }

        [Test]
        public void DuplicateWitnessIdentityIsRejected() {
            var axes = Mutable();
            axes["alt"]!["witnesses"]![0]!["witnessId"] =
                axes["az"]!["witnesses"]![0]!["witnessId"]!.DeepClone();

            Action parse = () => Parse(axes);

            parse.Should().Throw<JsonException>().WithMessage("*Duplicate witness ID*");
        }

        [Test]
        public void OneP20FrameMayWitnessBothScalesWithoutCreatingIndependence() {
            var axes = Mutable();
            axes["alt"]!["witnesses"]![1]!["artifactSha256"] =
                axes["az"]!["witnesses"]![1]!["artifactSha256"]!.DeepClone();
            axes["alt"]!["witnesses"]![1]!["sourceId"] = "p20-frame-42";
            axes["az"]!["witnesses"]![1]!["sourceId"] = "p20-frame-42";

            var result = Parse(axes);

            result.IndependentCorrelationGroupCount.Should().Be(3,
                "the shared P20 optical frame is one correlation group across both axes");
        }

        [Test]
        public void CorrelationGroupsArePreservedRatherThanCountedAsIndependent() {
            var axes = Mutable();
            axes["az"]!["witnesses"]![1]!["correlationGroup"] = "hall-az";

            var result = Parse(axes);

            result.IndependentCorrelationGroupCount.Should().Be(3,
                "the duplicate azimuth group must not create another independent source");
        }

        [Test]
        public void NonSymmetricAndNonPositiveSemidefiniteCovarianceAreRejected() {
            var asymmetric = Mutable();
            asymmetric["positionCovarianceSquareDegrees"]![1]![0] = 0.00003;
            var indefinite = Mutable();
            indefinite["positionCovarianceSquareDegrees"]![0]![1] = 0.001;
            indefinite["positionCovarianceSquareDegrees"]![1]![0] = 0.001;

            Action parseAsymmetric = () => Parse(asymmetric);
            Action parseIndefinite = () => Parse(indefinite);

            parseAsymmetric.Should().Throw<JsonException>().WithMessage("*symmetric*");
            parseIndefinite.Should().Throw<JsonException>().WithMessage("*positive semidefinite*");
        }

        [Test]
        public void CovarianceDiagonalMustMatchDeclaredStandardUncertainty() {
            var axes = Mutable();
            axes["positionCovarianceSquareDegrees"]![0]![0] = 0.0002;

            Action parse = () => Parse(axes);

            parse.Should().Throw<JsonException>().WithMessage("*standard uncertainty*");
        }

        [Test]
        public void UncertaintyExpandedWitnessCannotReachCompiledHardLimit() {
            var axes = Mutable();
            axes["az"]!["positionDegrees"] = 5.37;
            axes["az"]!["positionStandardUncertaintyDegrees"] = 0.01;

            Action parse = () => Parse(axes);

            parse.Should().Throw<JsonException>().WithMessage("*hard limit*");
        }

        [Test]
        public void UnknownAndMissingAxisPropertiesAreRejected() {
            var unknown = Mutable();
            unknown["az"]!["averagedPosition"] = 1.2;
            var missing = Mutable();
            ((JObject)missing["alt"]!).Property("agreement")!.Remove();

            Action parseUnknown = () => Parse(unknown);
            Action parseMissing = () => Parse(missing);

            parseUnknown.Should().Throw<JsonException>().WithMessage("*frozen V2 contract*");
            parseMissing.Should().Throw<JsonException>().WithMessage("*frozen V2 contract*");
        }

        [Test]
        public void NegativeZeroAndNonFiniteNumericValuesAreRejected() {
            var negativeZero = Mutable();
            negativeZero["az"]!["positionDegrees"] = BitConverter.Int64BitsToDouble(long.MinValue);
            var nonFinite = Mutable();
            nonFinite["alt"]!["positionDegrees"] = double.PositiveInfinity;

            Action parseNegativeZero = () => Parse(negativeZero);
            Action parseNonFinite = () => Parse(nonFinite);

            parseNegativeZero.Should().Throw<JsonException>();
            parseNonFinite.Should().Throw<JsonException>();
        }

        [Test]
        public void AxisEvidenceTypesCannotReferenceMotionAuthority() {
            foreach (var type in new[] {
                typeof(UpasSupervisorAxesEvidence),
                typeof(UpasSupervisorAxisEvidence),
                typeof(UpasSupervisorPositionWitness)
            }) {
                foreach (var property in type.GetProperties()) {
                    property.PropertyType.Should().NotBe(typeof(IAutomatedMoveExecutor));
                    property.PropertyType.Should().NotBe(typeof(IPolarAlignmentSystemVM));
                }
            }
        }

        private static UpasSupervisorAxesEvidence Parse(JObject axes) =>
            UpasSupervisorCoarseAxisEvidenceParser.Parse(axes, Envelope);
    }
}
