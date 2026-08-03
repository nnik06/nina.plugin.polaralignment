using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NINA.Plugins.PolarAlignment;
using NUnit.Framework;
using System;
using System.IO;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class UpasSupervisorCoarseResponseCalibrationTest {
        private static readonly UpasSupervisorCoarseEvidenceEnvelope Envelope = new(
            2,
            Guid.Parse("acaa2759-b829-41ec-9e93-59449a3ac38c"),
            Guid.Parse("a7901d62-e1e8-4cce-bd6a-e3e986d8cd8a"),
            new string('b', 64),
            new string('a', 64),
            DateTimeOffset.Parse("2026-08-03T12:00:00Z"),
            123456789, 25, 5000, 500, 100,
            UpasCoarsePlanningSafetyPolicy.RequiredCoordinateConvention);

        private static readonly UpasSupervisorAxesEvidence Axes = new(
            new UpasSupervisorAxisEvidence(1.0, 0.01, UpasAxisEngagementState.Positive,
                Array.Empty<UpasSupervisorPositionWitness>()),
            new UpasSupervisorAxisEvidence(-0.5, 0.02, UpasAxisEngagementState.Unknown,
                Array.Empty<UpasSupervisorPositionWitness>()),
            0.0001, 0.0, 0.0004);

        private static string ValidJson => """
            {
              "schemaVersion": 2,
              "calibrationId": "30000000-0000-0000-0000-000000000001",
              "artifactSha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
              "capturedUtc": "2026-08-03T11:59:59.0000000Z",
              "equation": "tppaErrorAfter=tppaErrorBefore+R*physicalDelta",
              "units": "degrees",
              "coordinateConvention": "azEastPositive_altUpPositive",
              "responseMatrix": {
                "coordinateConvention": "azEastPositive_altUpPositive",
                "values": [
                  [1.0, 0.05],
                  [0.02, 1.1]
                ]
              },
              "matrixElementCovariance": [
                [0.0001, 0.0, 0.0, 0.0],
                [0.0, 0.0001, 0.0, 0.0],
                [0.0, 0.0, 0.0001, 0.0],
                [0.0, 0.0, 0.0, 0.0001]
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
                "alt": ["negative", "positive"]
              },
              "temperatureC": { "minimum": 25.0, "maximum": 55.0 },
              "loadProfileId": "hae29c-ec-full-rig-v1",
              "ageAtResponseMilliseconds": 200,
              "validForMilliseconds": 5000,
              "supervisorBootId": "a7901d62-e1e8-4cce-bd6a-e3e986d8cd8a"
            }
            """;

        private static JObject Mutable() {
            using var textReader = new StringReader(ValidJson);
            using var jsonReader = new JsonTextReader(textReader) {
                DateParseHandling = DateParseHandling.None
            };
            return JObject.Load(jsonReader, new JsonLoadSettings {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
            });
        }

        [Test]
        public void ValidFullMatrixCalibrationParsesAndComputesConditionNumber() {
            var result = Parse(Mutable());

            result.ResponseMatrix.ConditionNumber.Should().BeGreaterThan(1.0);
            result.ResponseMatrix.ConditionNumber.Should().BeLessThan(1.2);
            result.AzimuthDeadbandDegrees.WorstDegrees.Should().Be(0.10);
            result.AltitudeDeadbandDegrees.WorstDegrees.Should().Be(0.05);
            result.MatrixElementCovariance.GetLength(0).Should().Be(4);
        }

        [Test]
        public void ScalarGainAndArbitraryEquationAreRejected() {
            var scalar = Mutable();
            scalar["responseMatrix"]!["values"] = 1.0;
            var equation = Mutable();
            equation["equation"] = "clientDefined(error,delta)";

            Action parseScalar = () => Parse(scalar);
            Action parseEquation = () => Parse(equation);

            parseScalar.Should().Throw<JsonException>().WithMessage("*2x2*");
            parseEquation.Should().Throw<JsonException>().WithMessage("*equation*");
        }

        [Test]
        public void SingularAndIllConditionedMatricesAreRejected() {
            var singular = Mutable();
            singular["responseMatrix"]!["values"] = JArray.Parse("[[1,1],[1,1]]");
            var illConditioned = Mutable();
            illConditioned["responseMatrix"]!["values"] = JArray.Parse("[[1,0],[0,0.05]]");

            Action parseSingular = () => Parse(singular);
            Action parseIllConditioned = () => Parse(illConditioned);

            parseSingular.Should().Throw<JsonException>().WithMessage("*condition number*");
            parseIllConditioned.Should().Throw<JsonException>().WithMessage("*condition number*");
        }

        [Test]
        public void AsymmetricAndIndefiniteCoefficientCovarianceAreRejected() {
            var asymmetric = Mutable();
            asymmetric["matrixElementCovariance"]![0]![1] = 0.001;
            var indefinite = Mutable();
            indefinite["matrixElementCovariance"]![0]![1] = 0.01;
            indefinite["matrixElementCovariance"]![1]![0] = 0.01;

            Action parseAsymmetric = () => Parse(asymmetric);
            Action parseIndefinite = () => Parse(indefinite);

            parseAsymmetric.Should().Throw<JsonException>().WithMessage("*symmetric*");
            parseIndefinite.Should().Throw<JsonException>().WithMessage("*positive semidefinite*");
        }

        [Test]
        public void NegativeDeadbandAndCommandUncertaintyAreRejected() {
            var deadband = Mutable();
            deadband["deadbandDegrees"]!["az"]!["negative"] = -0.01;
            var uncertainty = Mutable();
            uncertainty["fixedCommandUncertaintyDegrees"]!["alt"] = -0.01;

            Action parseDeadband = () => Parse(deadband);
            Action parseUncertainty = () => Parse(uncertainty);

            parseDeadband.Should().Throw<JsonException>();
            parseUncertainty.Should().Throw<JsonException>();
        }

        [Test]
        public void PositionTemperatureAndLoadApplicabilityAreEnforced() {
            var position = Mutable();
            position["applicablePositionDegrees"]!["az"]!["maximum"] = 1.02;

            Action parsePosition = () => Parse(position);
            Action parseTemperature = () => Parse(Mutable(), temperatureC: 56.0);
            Action parseLoad = () => Parse(Mutable(), loadProfileId: "different-rig");

            parsePosition.Should().Throw<JsonException>().WithMessage("*position uncertainty*");
            parseTemperature.Should().Throw<JsonException>().WithMessage("*temperature*");
            parseLoad.Should().Throw<JsonException>().WithMessage("*load profile*");
        }

        [Test]
        public void BothDirectionsAreRequiredForUnknownEngagement() {
            var directions = Mutable();
            directions["applicableDirections"]!["alt"] = JArray.Parse("[\"positive\"]");

            Action parse = () => Parse(directions);

            parse.Should().Throw<JsonException>().WithMessage("*positive and negative*");
        }

        [Test]
        public void StaleFutureLifetimeAndCrossBootCalibrationAreRejected() {
            var stale = Mutable();
            stale["ageAtResponseMilliseconds"] = 500.1;
            var futureLifetime = Mutable();
            futureLifetime["validForMilliseconds"] = 5000.1;
            var crossBoot = Mutable();
            crossBoot["supervisorBootId"] = "f0000000-0000-0000-0000-000000000001";

            Action parseStale = () => Parse(stale);
            Action parseLifetime = () => Parse(futureLifetime);
            Action parseCrossBoot = () => Parse(crossBoot);

            parseStale.Should().Throw<JsonException>().WithMessage("*stale*");
            parseLifetime.Should().Throw<JsonException>().WithMessage("*validity*");
            parseCrossBoot.Should().Throw<JsonException>().WithMessage("*different supervisor boot*");
        }

        [Test]
        public void ConventionUnitsAndSchemaAreExact() {
            foreach (var mutation in new Action<JObject>[] {
                value => value["coordinateConvention"] = "azWestPositive_altUpPositive",
                value => value["responseMatrix"]!["coordinateConvention"] =
                    "azWestPositive_altUpPositive",
                value => value["units"] = "radians",
                value => value["schemaVersion"] = 3,
                value => value["capturedUtc"] = "2026-08-03T15:59:59+04:00"
            }) {
                var value = Mutable();
                mutation(value);
                Action parse = () => Parse(value);
                parse.Should().Throw<JsonException>();
            }
        }

        [Test]
        public void UnknownAndMissingPropertiesAreRejected() {
            var unknown = Mutable();
            unknown["gain"] = 0.9;
            var missing = Mutable();
            missing.Property("matrixElementCovariance")!.Remove();

            Action parseUnknown = () => Parse(unknown);
            Action parseMissing = () => Parse(missing);

            parseUnknown.Should().Throw<JsonException>().WithMessage("*frozen V2 contract*");
            parseMissing.Should().Throw<JsonException>().WithMessage("*frozen V2 contract*");
        }

        [Test]
        public void ResponseCalibrationDoesNotExposeMotionAuthority() {
            foreach (var property in typeof(UpasSupervisorCoarseResponseCalibration).GetProperties()) {
                property.PropertyType.Should().NotBe(typeof(IAutomatedMoveExecutor));
                property.PropertyType.Should().NotBe(typeof(IPolarAlignmentSystemVM));
            }
        }

        private static UpasSupervisorCoarseResponseCalibration Parse(
                JObject value,
                double temperatureC = 40.0,
                string loadProfileId = "hae29c-ec-full-rig-v1") =>
            UpasSupervisorCoarseResponseCalibrationParser.Parse(
                value, Envelope, Axes, temperatureC, loadProfileId);
    }
}
