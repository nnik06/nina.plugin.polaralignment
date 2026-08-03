using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal enum UpasAxisEngagementState {
        Positive,
        Negative,
        Unknown
    }

    internal sealed record UpasSupervisorPositionWitness(
        Guid WitnessId,
        string ArtifactSha256,
        double AgeAtResponseMilliseconds,
        string SourceId,
        string CorrelationGroup,
        Guid SensorCalibrationId,
        string CoordinateConvention);

    internal sealed record UpasSupervisorAxisEvidence(
        double PositionDegrees,
        double PositionStandardUncertaintyDegrees,
        UpasAxisEngagementState EngagementState,
        IReadOnlyList<UpasSupervisorPositionWitness> Witnesses);

    internal sealed record UpasSupervisorAxesEvidence(
        UpasSupervisorAxisEvidence Azimuth,
        UpasSupervisorAxisEvidence Altitude,
        double CovarianceAzAzSquareDegrees,
        double CovarianceAzAltSquareDegrees,
        double CovarianceAltAltSquareDegrees) {

        public int IndependentCorrelationGroupCount =>
            Azimuth.Witnesses.Concat(Altitude.Witnesses)
                .Select(witness => witness.CorrelationGroup)
                .Distinct(StringComparer.Ordinal)
                .Count();
    }

    internal static class UpasSupervisorCoarseAxisEvidenceParser {
        private const double CovarianceTolerance = 1e-12;

        private static readonly string[] AxesProperties = {
            "az", "alt", "positionCovarianceSquareDegrees"
        };

        private static readonly string[] AxisProperties = {
            "positionDegrees", "positionStandardUncertaintyDegrees",
            "engagementState", "agreement", "witnesses"
        };

        private static readonly string[] WitnessProperties = {
            "witnessId", "artifactSha256", "ageAtResponseMilliseconds", "sourceId",
            "correlationGroup", "sensorCalibrationId", "coordinateConvention"
        };

        public static UpasSupervisorAxesEvidence Parse(
                JObject axes,
                UpasSupervisorCoarseEvidenceEnvelope envelope) {
            if (axes == null) {
                throw new ArgumentNullException(nameof(axes));
            }
            if (envelope == null) {
                throw new ArgumentNullException(nameof(envelope));
            }
            RequireExactProperties(axes, AxesProperties, "axes");

            var azimuth = ParseAxis(RequireObject(axes, "az"), "axes.az", envelope);
            var altitude = ParseAxis(RequireObject(axes, "alt"), "axes.alt", envelope);
            var covariance = ParseCovariance(axes["positionCovarianceSquareDegrees"]);

            RequireApproximatelyEqual(
                covariance.AzAz,
                azimuth.PositionStandardUncertaintyDegrees
                    * azimuth.PositionStandardUncertaintyDegrees,
                "azimuth covariance diagonal does not match its standard uncertainty");
            RequireApproximatelyEqual(
                covariance.AltAlt,
                altitude.PositionStandardUncertaintyDegrees
                    * altitude.PositionStandardUncertaintyDegrees,
                "altitude covariance diagonal does not match its standard uncertainty");

            var determinant = covariance.AzAz * covariance.AltAlt
                - covariance.AzAlt * covariance.AzAlt;
            if (covariance.AzAz < 0.0
                    || covariance.AltAlt < 0.0
                    || determinant < -CovarianceTolerance) {
                throw new JsonException("Position covariance must be positive semidefinite.");
            }

            var allWitnesses = azimuth.Witnesses.Concat(altitude.Witnesses).ToArray();
            RequireUnique(allWitnesses.Select(witness => witness.WitnessId), "witness ID");

            return new UpasSupervisorAxesEvidence(
                azimuth,
                altitude,
                covariance.AzAz,
                covariance.AzAlt,
                covariance.AltAlt);
        }

        private static UpasSupervisorAxisEvidence ParseAxis(
                JObject axis,
                string name,
                UpasSupervisorCoarseEvidenceEnvelope envelope) {
            RequireExactProperties(axis, AxisProperties, name);
            var position = RequireFinite(axis, "positionDegrees");
            var uncertainty = RequirePositiveFinite(axis, "positionStandardUncertaintyDegrees");
            if (Math.Abs(position) + 3.0 * uncertainty
                    >= UpasCoarsePlanningSafetyPolicy.PhysicalHardLimitDegrees) {
                throw new JsonException($"{name} witness uncertainty reaches the compiled hard limit.");
            }

            var engagement = RequireString(axis, "engagementState") switch {
                "positive" => UpasAxisEngagementState.Positive,
                "negative" => UpasAxisEngagementState.Negative,
                "unknown" => UpasAxisEngagementState.Unknown,
                _ => throw new JsonException($"{name}.engagementState is unsupported.")
            };
            if (RequireString(axis, "agreement") != "pass") {
                throw new JsonException($"{name} required witness sources do not agree.");
            }
            if (axis["witnesses"] is not JArray witnessesArray || witnessesArray.Count == 0) {
                throw new JsonException($"{name}.witnesses must be a non-empty array.");
            }
            var witnesses = witnessesArray.Select((token, index) =>
                token is JObject witness
                    ? ParseWitness(witness, $"{name}.witnesses[{index}]", envelope)
                    : throw new JsonException($"{name}.witnesses entries must be objects."))
                .ToArray();
            RequireUnique(witnesses.Select(witness => witness.WitnessId), $"{name} witness ID");

            return new UpasSupervisorAxisEvidence(position, uncertainty, engagement, witnesses);
        }

        private static UpasSupervisorPositionWitness ParseWitness(
                JObject witness,
                string name,
                UpasSupervisorCoarseEvidenceEnvelope envelope) {
            RequireExactProperties(witness, WitnessProperties, name);
            var witnessId = RequireGuid(witness, "witnessId");
            var artifact = RequireLowerHexSha256(witness, "artifactSha256");
            var age = RequireNonNegativeFinite(witness, "ageAtResponseMilliseconds");
            if (age > envelope.OldestEvidenceAgeMilliseconds) {
                throw new JsonException($"{name} age exceeds the envelope's oldest-evidence age.");
            }
            var sourceId = RequireString(witness, "sourceId");
            var correlationGroup = RequireString(witness, "correlationGroup");
            var calibrationId = RequireGuid(witness, "sensorCalibrationId");
            var convention = RequireString(witness, "coordinateConvention");
            if (convention != envelope.CoordinateConvention
                    || convention != UpasCoarsePlanningSafetyPolicy.RequiredCoordinateConvention) {
                throw new JsonException($"{name} coordinate convention does not match the envelope.");
            }
            return new UpasSupervisorPositionWitness(
                witnessId, artifact, age, sourceId, correlationGroup, calibrationId, convention);
        }

        private static (double AzAz, double AzAlt, double AltAlt) ParseCovariance(JToken token) {
            if (token is not JArray rows || rows.Count != 2
                    || rows.Any(row => row is not JArray columns || columns.Count != 2)) {
                throw new JsonException("positionCovarianceSquareDegrees must be a 2x2 array.");
            }
            var aa = RequireFinite(rows[0][0], "position covariance az-az");
            var ab = RequireFinite(rows[0][1], "position covariance az-alt");
            var ba = RequireFinite(rows[1][0], "position covariance alt-az");
            var bb = RequireFinite(rows[1][1], "position covariance alt-alt");
            RequireApproximatelyEqual(ab, ba, "Position covariance must be symmetric");
            return (aa, ab, bb);
        }

        private static void RequireApproximatelyEqual(double actual, double expected, string reason) {
            if (Math.Abs(actual - expected) > CovarianceTolerance) {
                throw new JsonException(reason + ".");
            }
        }

        private static void RequireUnique<T>(IEnumerable<T> values, string name) {
            var array = values.ToArray();
            if (array.Distinct().Count() != array.Length) {
                throw new JsonException($"Duplicate {name} is forbidden.");
            }
        }

        private static void RequireExactProperties(
                JObject value,
                IEnumerable<string> expected,
                string name) {
            var actual = value.Properties().Select(property => property.Name)
                .OrderBy(item => item, StringComparer.Ordinal);
            var required = expected.OrderBy(item => item, StringComparer.Ordinal);
            if (!actual.SequenceEqual(required, StringComparer.Ordinal)) {
                throw new JsonException($"{name} properties do not match the frozen V2 contract.");
            }
        }

        private static JObject RequireObject(JObject value, string name) =>
            value[name] is JObject result
                ? result
                : throw new JsonException($"{name} must be an object.");

        private static string RequireString(JObject value, string name) =>
            value[name]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace(value[name].Value<string>())
                ? value[name].Value<string>()
                : throw new JsonException($"{name} must be a non-empty string.");

        private static Guid RequireGuid(JObject value, string name) {
            if (!Guid.TryParse(RequireString(value, name), out var result) || result == Guid.Empty) {
                throw new JsonException($"{name} must be a non-empty UUID.");
            }
            return result;
        }

        private static string RequireLowerHexSha256(JObject value, string name) {
            var result = RequireString(value, name);
            if (result.Length != 64
                    || result.Any(character => !((character >= '0' && character <= '9')
                        || (character >= 'a' && character <= 'f')))) {
                throw new JsonException($"{name} must be exactly 64 lowercase hexadecimal characters.");
            }
            return result;
        }

        private static double RequireFinite(JObject value, string name) =>
            RequireFinite(value[name], name);

        private static double RequireFinite(JToken token, string name) {
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)) {
                throw new JsonException($"{name} must be numeric.");
            }
            var result = token.Value<double>();
            if (!double.IsFinite(result) || IsNegativeZero(result)) {
                throw new JsonException($"{name} must be finite and not negative zero.");
            }
            return result;
        }

        private static double RequireNonNegativeFinite(JObject value, string name) {
            var result = RequireFinite(value, name);
            if (result < 0.0) {
                throw new JsonException($"{name} must be non-negative.");
            }
            return result;
        }

        private static double RequirePositiveFinite(JObject value, string name) {
            var result = RequireFinite(value, name);
            if (result <= 0.0) {
                throw new JsonException($"{name} must be positive.");
            }
            return result;
        }

        private static bool IsNegativeZero(double value) =>
            value == 0.0 && BitConverter.DoubleToInt64Bits(value) < 0;
    }
}
