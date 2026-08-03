using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record UpasSupervisorCoarsePlanningEvidence(
        UpasSupervisorCoarseEvidenceEnvelope Envelope,
        UpasSupervisorAxesEvidence Axes,
        UpasSupervisorCoarseResponseCalibration ResponseCalibration,
        UpasSupervisorCoarseRuntimeConstraints Runtime,
        string ResponseContentSha256) {

        public void RequireFreshAtPlanEmission(double elapsedSinceReceiveMilliseconds) =>
            Envelope.RequireFreshAt(elapsedSinceReceiveMilliseconds);
    }

    /// <summary>
    /// Materializes one inert planning snapshot only after its exact response bytes and
    /// evidence identity have been bound by the authenticated HTTPS transport.
    /// This parser neither acquires a lease nor exposes a motion client.
    /// </summary>
    internal static class UpasSupervisorCoarsePlanningEvidenceParser {
        public const string RequiredAuthenticationMethod = "httpsBearer";

        public static UpasSupervisorCoarsePlanningEvidence ParseAuthenticated(
                string json,
                string expectedClientRequestNonce,
                double observedRoundTripMilliseconds,
                Guid? expectedCallerLeaseId,
                double currentTemperatureC,
                string currentLoadProfileId,
                string authenticatedResponseContentSha256,
                string authenticatedEvidenceId,
                string authenticationMethod) {
            RequireLowerHexSha256(authenticatedResponseContentSha256,
                nameof(authenticatedResponseContentSha256));
            RequireLowerHexSha256(authenticatedEvidenceId, nameof(authenticatedEvidenceId));
            if (!string.Equals(authenticationMethod, RequiredAuthenticationMethod,
                    StringComparison.Ordinal)) {
                throw new JsonException("Coarse-planning evidence was not obtained through the required authenticated HTTPS bearer transport.");
            }
            var actualContentSha256 = Digest(json);
            if (!string.Equals(actualContentSha256, authenticatedResponseContentSha256,
                    StringComparison.Ordinal)) {
                throw new JsonException("Coarse-planning response bytes do not match the authenticated transport digest.");
            }

            var envelope = UpasSupervisorCoarseEvidenceSchemaGate.ParseEnvelope(
                json, expectedClientRequestNonce, observedRoundTripMilliseconds);
            if (!string.Equals(envelope.EvidenceId, authenticatedEvidenceId,
                    StringComparison.Ordinal)) {
                throw new JsonException("Coarse-planning evidence identity does not match the authenticated transport identity.");
            }

            var root = UpasSupervisorCoarseEvidenceSchemaGate.ParseSupportedRoot(json);
            var axes = UpasSupervisorCoarseAxisEvidenceParser.Parse(
                RequireObject(root, "axes"), envelope);
            var calibration = UpasSupervisorCoarseResponseCalibrationParser.Parse(
                RequireObject(root, "responseCalibration"), envelope, axes,
                currentTemperatureC, currentLoadProfileId);
            var runtime = UpasSupervisorCoarseRuntimeConstraintsParser.Parse(
                root, expectedCallerLeaseId);
            envelope.RequireFreshAt(0.0);

            return new UpasSupervisorCoarsePlanningEvidence(
                envelope, axes, calibration, runtime, actualContentSha256);
        }

        internal static string Digest(string value) {
            if (value == null) {
                throw new ArgumentNullException(nameof(value));
            }
            using var sha256 = SHA256.Create();
            return string.Concat(sha256.ComputeHash(Encoding.UTF8.GetBytes(value))
                .Select(item => item.ToString("x2")));
        }

        private static JObject RequireObject(JObject root, string name) =>
            root[name] is JObject result
                ? result
                : throw new JsonException($"{name} must be an object.");

        private static void RequireLowerHexSha256(string value, string name) {
            if (value == null || value.Length != 64
                    || value.Any(character => !((character >= '0' && character <= '9')
                        || (character >= 'a' && character <= 'f')))) {
                throw new JsonException($"{name} must be exactly 64 lowercase hexadecimal characters.");
            }
        }
    }
}
