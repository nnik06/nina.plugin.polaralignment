using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal static class UpasCoarsePlanningSafetyPolicy {
        public const int EvidenceSchemaVersion = 2;
        public const double PhysicalHardLimitDegrees = 5.4;
        public const double MinimumReservedTravelDegrees = 1.0;
        public const string RequiredCoordinateConvention = "azEastPositive_altUpPositive";
        public const double MaximumEvidenceLifetimeMilliseconds = 5000.0;
        public const double MaximumEvidenceRoundTripMilliseconds = 2000.0;
    }

    internal sealed record UpasSupervisorCoarseEvidenceEnvelope(
        int SchemaVersion,
        Guid SupervisorSessionId,
        Guid SupervisorBootId,
        string EvidenceId,
        string ClientRequestNonce,
        DateTimeOffset CapturedUtc,
        long CapturedMonotonicNs,
        double ServerProcessingMilliseconds,
        double ValidForMilliseconds,
        double OldestEvidenceAgeMilliseconds,
        double ObservedRoundTripMilliseconds,
        string CoordinateConvention) {

        public void RequireFreshAt(double elapsedSinceReceiveMilliseconds) {
            RequireNonNegativeFinite(elapsedSinceReceiveMilliseconds, nameof(elapsedSinceReceiveMilliseconds));
            var conservativeAge = OldestEvidenceAgeMilliseconds
                + ObservedRoundTripMilliseconds
                + elapsedSinceReceiveMilliseconds;
            var maximumAge = Math.Min(
                ValidForMilliseconds,
                UpasCoarsePlanningSafetyPolicy.MaximumEvidenceLifetimeMilliseconds);
            if (conservativeAge > maximumAge) {
                throw new JsonException(
                    $"Supervisor coarse-planning evidence is stale: conservative age " +
                    $"{conservativeAge:F0} ms exceeds {maximumAge:F0} ms.");
            }
        }

        private static void RequireNonNegativeFinite(double value, string name) {
            if (!double.IsFinite(value) || value < 0.0 || IsNegativeZero(value)) {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static bool IsNegativeZero(double value) =>
            value == 0.0 && BitConverter.DoubleToInt64Bits(value) < 0;
    }

    internal static class UpasSupervisorCoarseEvidenceSchemaGate {
        private static readonly string[] TopLevelProperties = {
            "schemaVersion", "supervisorSessionId", "supervisorBootId", "evidenceId",
            "clientRequestNonce", "capturedUtc", "capturedMonotonicNs",
            "serverProcessingMilliseconds", "validForMilliseconds",
            "oldestEvidenceAgeMilliseconds", "coordinateConvention", "hardLimitDegrees",
            "operationalReserveDegrees", "operationalLimitDegrees", "axes",
            "responseCalibration", "remainingTravelBudgetDegrees", "activeLeaseId",
            "activeTransactionId", "lockedReason", "capabilities"
        };
        private static readonly string[] AxisPairProperties = { "az", "alt" };
        private static readonly string[] AxisLimitProperties = { "minimum", "maximum" };

        public static JObject ParseSupportedRoot(string json) {
            if (string.IsNullOrWhiteSpace(json)) {
                throw new JsonException("Supervisor coarse-planning evidence response was empty.");
            }

            JObject root;
            try {
                using var textReader = new StringReader(json);
                using var jsonReader = new JsonTextReader(textReader) {
                    DateParseHandling = DateParseHandling.None
                };
                root = JObject.Load(jsonReader, new JsonLoadSettings {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                });
            } catch (JsonException) {
                throw;
            }
            var schema = root["schemaVersion"];
            if (schema?.Type != JTokenType.Integer
                    || schema.Value<long>() != UpasCoarsePlanningSafetyPolicy.EvidenceSchemaVersion) {
                throw new JsonException(
                    $"Unsupported supervisor coarse-planning evidence schema; exact integer " +
                    $"{UpasCoarsePlanningSafetyPolicy.EvidenceSchemaVersion} is required.");
            }
            return root;
        }

        public static UpasSupervisorCoarseEvidenceEnvelope ParseEnvelope(
                string json,
                string expectedClientRequestNonce,
                double observedRoundTripMilliseconds) {
            RequireLowerHexSha256(expectedClientRequestNonce, nameof(expectedClientRequestNonce));
            RequireNonNegativeFinite(observedRoundTripMilliseconds, nameof(observedRoundTripMilliseconds));
            if (observedRoundTripMilliseconds
                    > UpasCoarsePlanningSafetyPolicy.MaximumEvidenceRoundTripMilliseconds) {
                throw new JsonException("Supervisor coarse-planning evidence round trip exceeded the compiled limit.");
            }

            var root = ParseSupportedRoot(json);
            RequireExactProperties(root, TopLevelProperties, "coarse-planning evidence");
            var sessionId = RequireGuid(root, "supervisorSessionId");
            var bootId = RequireGuid(root, "supervisorBootId");
            var evidenceId = RequireString(root, "evidenceId");
            RequireLowerHexSha256(evidenceId, "evidenceId");
            var nonce = RequireString(root, "clientRequestNonce");
            RequireLowerHexSha256(nonce, "clientRequestNonce");
            if (!string.Equals(nonce, expectedClientRequestNonce, StringComparison.Ordinal)) {
                throw new JsonException("Supervisor coarse-planning evidence nonce does not match the request.");
            }

            var capturedUtcText = RequireString(root, "capturedUtc");
            if (!DateTimeOffset.TryParse(
                    capturedUtcText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var capturedUtc)
                    || capturedUtc.Offset != TimeSpan.Zero) {
                throw new JsonException("capturedUtc must be an ISO 8601 UTC timestamp.");
            }

            var capturedMonotonicNs = RequirePositiveLong(root, "capturedMonotonicNs");
            var serverProcessing = RequireNonNegativeFinite(root, "serverProcessingMilliseconds");
            var validFor = RequirePositiveFinite(root, "validForMilliseconds");
            var oldestEvidenceAge = RequireNonNegativeFinite(root, "oldestEvidenceAgeMilliseconds");
            if (validFor > UpasCoarsePlanningSafetyPolicy.MaximumEvidenceLifetimeMilliseconds) {
                throw new JsonException("Supervisor evidence lifetime exceeds the compiled maximum.");
            }
            if (serverProcessing > observedRoundTripMilliseconds) {
                throw new JsonException("Supervisor processing duration exceeds the client-observed round trip.");
            }

            var convention = RequireString(root, "coordinateConvention");
            if (convention != UpasCoarsePlanningSafetyPolicy.RequiredCoordinateConvention) {
                throw new JsonException("Supervisor coordinate convention is unsupported.");
            }

            ValidateLimitObject(RequireObject(root, "hardLimitDegrees"), -5.4, 5.4, "hardLimitDegrees");
            var reserve = RequireNonNegativeFinite(root, "operationalReserveDegrees");
            ValidateCompiledSafetyCrossChecks(-5.4, 5.4, reserve);
            ValidateLimitObject(RequireObject(root, "operationalLimitDegrees"), -4.4, 4.4, "operationalLimitDegrees");
            RequireObject(root, "axes");
            RequireObject(root, "responseCalibration");
            RequireObject(root, "remainingTravelBudgetDegrees");
            RequireObject(root, "capabilities");
            RequireOptionalString(root, "activeLeaseId");
            RequireOptionalString(root, "activeTransactionId");
            RequireOptionalString(root, "lockedReason");

            var envelope = new UpasSupervisorCoarseEvidenceEnvelope(
                2, sessionId, bootId, evidenceId, nonce, capturedUtc, capturedMonotonicNs,
                serverProcessing, validFor, oldestEvidenceAge, observedRoundTripMilliseconds,
                convention);
            envelope.RequireFreshAt(0.0);
            return envelope;
        }

        public static void ValidateCompiledSafetyCrossChecks(
                double negativeHardLimitDegrees,
                double positiveHardLimitDegrees,
                double operationalReserveDegrees) {
            if (!double.IsFinite(negativeHardLimitDegrees)
                    || !double.IsFinite(positiveHardLimitDegrees)
                    || !double.IsFinite(operationalReserveDegrees)
                    || IsNegativeZero(negativeHardLimitDegrees)
                    || IsNegativeZero(positiveHardLimitDegrees)
                    || IsNegativeZero(operationalReserveDegrees)
                    || negativeHardLimitDegrees != -UpasCoarsePlanningSafetyPolicy.PhysicalHardLimitDegrees
                    || positiveHardLimitDegrees != UpasCoarsePlanningSafetyPolicy.PhysicalHardLimitDegrees
                    || operationalReserveDegrees != UpasCoarsePlanningSafetyPolicy.MinimumReservedTravelDegrees) {
                throw new JsonException("Supervisor safety limits do not exactly cross-check compiled TPPA policy.");
            }
        }

        private static bool IsNegativeZero(double value) =>
            value == 0.0 && BitConverter.DoubleToInt64Bits(value) < 0;

        private static void ValidateLimitObject(
                JObject value, double expectedMinimum, double expectedMaximum, string name) {
            RequireExactProperties(value, AxisPairProperties, name);
            foreach (var axis in AxisPairProperties) {
                var limits = RequireObject(value, axis);
                RequireExactProperties(limits, AxisLimitProperties, $"{name}.{axis}");
                if (RequireFinite(limits, "minimum") != expectedMinimum
                        || RequireFinite(limits, "maximum") != expectedMaximum) {
                    throw new JsonException($"{name}.{axis} does not cross-check compiled TPPA policy.");
                }
            }
        }

        private static void RequireExactProperties(
                JObject value, IEnumerable<string> expected, string name) {
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

        private static string RequireOptionalString(JObject value, string name) {
            if (value[name]?.Type == JTokenType.Null) {
                return null;
            }
            return RequireString(value, name);
        }

        private static Guid RequireGuid(JObject value, string name) {
            if (!Guid.TryParse(RequireString(value, name), out var result) || result == Guid.Empty) {
                throw new JsonException($"{name} must be a non-empty UUID.");
            }
            return result;
        }

        private static long RequirePositiveLong(JObject value, string name) {
            if (value[name]?.Type != JTokenType.Integer) {
                throw new JsonException($"{name} must be an integer.");
            }
            var result = value[name].Value<long>();
            if (result <= 0) {
                throw new JsonException($"{name} must be positive.");
            }
            return result;
        }

        private static double RequireFinite(JObject value, string name) {
            var token = value[name];
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

        private static void RequireNonNegativeFinite(double value, string name) {
            if (!double.IsFinite(value) || value < 0.0 || IsNegativeZero(value)) {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static void RequireLowerHexSha256(string value, string name) {
            if (value == null || value.Length != 64
                    || value.Any(character => !((character >= '0' && character <= '9')
                        || (character >= 'a' && character <= 'f')))) {
                throw new JsonException($"{name} must be exactly 64 lowercase hexadecimal characters.");
            }
        }
    }
}
