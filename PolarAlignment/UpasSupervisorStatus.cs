using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record UpasSupervisorCapabilities(
        bool PhysicalMotion,
        bool AzDryRun,
        bool Alt,
        bool P20Wss);

    internal sealed record UpasSupervisorLease(
        string LeaseId,
        string ClientId,
        long IssuedMonotonicNs,
        long ExpiresMonotonicNs,
        double PlannedTravelDegrees,
        double MaximumTravelDegrees);

    internal sealed record UpasSupervisorStatus(
        int SchemaVersion,
        string Mode,
        Guid SupervisorSessionId,
        string LockedReason,
        UpasSupervisorLease ActiveLease,
        string ActiveTransactionId,
        IReadOnlyList<string> WitnessedAxes,
        double RemainingSessionTravelDegrees,
        UpasSupervisorCapabilities Capabilities) {

        private static readonly string[] TopLevelProperties = {
            "schemaVersion", "mode", "supervisorSessionId", "lockedReason",
            "activeLease", "activeTransactionId", "witnessedAxes",
            "remainingSessionTravelDegrees", "capabilities", "sensor"
        };

        private static readonly string[] CapabilityProperties = {
            "physicalMotion", "azDryRun", "alt", "p20Wss"
        };

        private static readonly string[] LeaseProperties = {
            "leaseId", "clientId", "issuedMonotonicNs", "expiresMonotonicNs",
            "plannedTravelDegrees", "maximumTravelDegrees"
        };

        public static UpasSupervisorStatus Parse(string json) {
            if (string.IsNullOrWhiteSpace(json)) {
                throw new JsonException("Supervisor status response was empty.");
            }

            JObject root;
            try {
                root = JObject.Parse(json);
            } catch (JsonException) {
                throw;
            }
            RequireExactProperties(root, TopLevelProperties, "status");

            var schemaVersion = RequireInteger(root, "schemaVersion");
            if (schemaVersion != 1) {
                throw new JsonException($"Unsupported supervisor status schema {schemaVersion}.");
            }

            var mode = RequireString(root, "mode");
            if (mode != "phase1DryRun" && mode != "phase2DryRunReadOnlyP20") {
                throw new JsonException($"Unsupported supervisor mode '{mode}'.");
            }

            if (!Guid.TryParse(RequireString(root, "supervisorSessionId"), out var sessionId)
                || sessionId == Guid.Empty) {
                throw new JsonException("Supervisor session ID must be a non-empty UUID.");
            }

            var lockedReason = OptionalString(root, "lockedReason");
            var activeTransactionId = OptionalString(root, "activeTransactionId");
            var remainingTravel = RequireFiniteNumber(root, "remainingSessionTravelDegrees");
            if (remainingTravel < 0) {
                throw new JsonException("Remaining session travel must be non-negative.");
            }

            var witnessedAxesToken = root["witnessedAxes"];
            if (witnessedAxesToken is not JArray witnessedAxesArray) {
                throw new JsonException("witnessedAxes must be an array.");
            }
            var witnessedAxes = witnessedAxesArray.Select(token =>
                token.Type == JTokenType.String
                    ? token.Value<string>()
                    : throw new JsonException("witnessedAxes entries must be strings."))
                .ToArray();
            if (witnessedAxes.Any(axis => axis != "az" && axis != "alt")
                || witnessedAxes.Distinct(StringComparer.Ordinal).Count() != witnessedAxes.Length) {
                throw new JsonException("witnessedAxes contains an unsupported or duplicate axis.");
            }

            if (root["capabilities"] is not JObject capabilitiesObject) {
                throw new JsonException("capabilities must be an object.");
            }
            RequireExactProperties(capabilitiesObject, CapabilityProperties, "capabilities");
            var capabilities = new UpasSupervisorCapabilities(
                RequireBoolean(capabilitiesObject, "physicalMotion"),
                RequireBoolean(capabilitiesObject, "azDryRun"),
                RequireBoolean(capabilitiesObject, "alt"),
                RequireBoolean(capabilitiesObject, "p20Wss"));

            UpasSupervisorLease activeLease = null;
            if (root["activeLease"]?.Type != JTokenType.Null) {
                if (root["activeLease"] is not JObject leaseObject) {
                    throw new JsonException("activeLease must be null or an object.");
                }
                RequireExactProperties(leaseObject, LeaseProperties, "activeLease");
                activeLease = new UpasSupervisorLease(
                    RequireString(leaseObject, "leaseId"),
                    RequireString(leaseObject, "clientId"),
                    RequireLong(leaseObject, "issuedMonotonicNs"),
                    RequireLong(leaseObject, "expiresMonotonicNs"),
                    RequireFiniteNumber(leaseObject, "plannedTravelDegrees"),
                    RequireFiniteNumber(leaseObject, "maximumTravelDegrees"));
                if (activeLease.IssuedMonotonicNs <= 0
                    || activeLease.ExpiresMonotonicNs <= activeLease.IssuedMonotonicNs
                    || activeLease.PlannedTravelDegrees < 0
                    || activeLease.MaximumTravelDegrees <= 0
                    || activeLease.PlannedTravelDegrees > activeLease.MaximumTravelDegrees) {
                    throw new JsonException("activeLease contains an invalid lifetime or travel budget.");
                }
            }

            var sensor = root["sensor"];
            if (sensor == null || (sensor.Type != JTokenType.Null && sensor.Type != JTokenType.Object)) {
                throw new JsonException("sensor must be null or an object.");
            }

            return new UpasSupervisorStatus(
                schemaVersion,
                mode,
                sessionId,
                lockedReason,
                activeLease,
                activeTransactionId,
                witnessedAxes,
                remainingTravel,
                capabilities);
        }

        private static void RequireExactProperties(JObject value, IEnumerable<string> expected, string name) {
            var actual = value.Properties().Select(property => property.Name).OrderBy(item => item, StringComparer.Ordinal);
            var required = expected.OrderBy(item => item, StringComparer.Ordinal);
            if (!actual.SequenceEqual(required, StringComparer.Ordinal)) {
                throw new JsonException($"{name} properties do not match the frozen V1 contract.");
            }
        }

        private static string RequireString(JObject value, string name) =>
            value[name]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace(value[name].Value<string>())
                ? value[name].Value<string>()
                : throw new JsonException($"{name} must be a non-empty string.");

        private static string OptionalString(JObject value, string name) {
            if (value[name]?.Type == JTokenType.Null) {
                return null;
            }
            if (value[name]?.Type != JTokenType.String
                || string.IsNullOrWhiteSpace(value[name].Value<string>())) {
                throw new JsonException($"{name} must be null or a non-empty string.");
            }
            return value[name].Value<string>();
        }

        private static int RequireInteger(JObject value, string name) =>
            value[name]?.Type == JTokenType.Integer
                ? value[name].Value<int>()
                : throw new JsonException($"{name} must be an integer.");

        private static long RequireLong(JObject value, string name) =>
            value[name]?.Type == JTokenType.Integer
                ? value[name].Value<long>()
                : throw new JsonException($"{name} must be an integer.");

        private static bool RequireBoolean(JObject value, string name) =>
            value[name]?.Type == JTokenType.Boolean
                ? value[name].Value<bool>()
                : throw new JsonException($"{name} must be a boolean.");

        private static double RequireFiniteNumber(JObject value, string name) {
            var token = value[name];
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)) {
                throw new JsonException($"{name} must be numeric.");
            }
            var result = token.Value<double>();
            if (double.IsNaN(result) || double.IsInfinity(result)) {
                throw new JsonException($"{name} must be finite.");
            }
            return result;
        }
    }

    internal sealed record UpasSupervisorReadiness(bool IsReady, string Reason);

    internal static class UpasSupervisorReadinessPolicy {
        public static UpasSupervisorReadiness Evaluate(UpasSupervisorStatus status, Axis axis) {
            if (status == null) {
                return new(false, "External UPAS supervisor status is missing.");
            }
            if (!string.IsNullOrWhiteSpace(status.LockedReason)) {
                return new(false, $"External UPAS supervisor is locked: {status.LockedReason}");
            }
            if (!status.Capabilities.PhysicalMotion) {
                return new(false, "External UPAS supervisor reports physicalMotion=false; local fallback is forbidden.");
            }
            if (axis == Axis.YAxis && !status.Capabilities.Alt) {
                return new(false, "External UPAS supervisor reports altitude motion is disabled.");
            }
            if (axis != Axis.XAxis && axis != Axis.YAxis) {
                return new(false, "Unsupported automated movement axis.");
            }
            return new(true, "External UPAS supervisor reports the requested physical capability.");
        }
    }

    internal static class UpasSupervisorCoarsePlanningReadinessPolicy {
        public static UpasSupervisorReadiness Evaluate(UpasSupervisorStatus status) {
            if (status == null) {
                return new(false, "External UPAS supervisor status is missing.");
            }

            // Frozen status V1 proves service/capability state only. It does not carry signed
            // witnessed positions, freshness, uncertainty, or response-calibration evidence.
            // Coarse planning must therefore remain unavailable until a separately versioned
            // evidence contract is implemented and validated end to end.
            return new(false,
                "Supervisor status V1 cannot authorize coarse planning because signed, fresh " +
                "position and response-calibration evidence is unavailable.");
        }
    }

    internal interface IUpasSupervisorStatusSource {
        Task<UpasSupervisorStatus> GetStatusAsync(CancellationToken token);
    }

    internal sealed class HttpsUpasSupervisorStatusSource : IUpasSupervisorStatusSource {
        private readonly HttpClient httpClient;
        private readonly Uri statusUri;
        private readonly Func<string> tokenProvider;
        private readonly TimeSpan operationTimeout;

        public HttpsUpasSupervisorStatusSource(
            HttpClient httpClient,
            string endpoint,
            Func<string> tokenProvider,
            TimeSpan? operationTimeout = null) {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
            statusUri = BuildStatusUri(endpoint);
            this.operationTimeout = operationTimeout ?? TimeSpan.FromSeconds(10);
            if (this.operationTimeout <= TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(operationTimeout));
            }
        }

        internal static Uri BuildStatusUri(string endpoint) {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var baseUri)
                || baseUri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(baseUri.UserInfo)
                || !string.IsNullOrEmpty(baseUri.Query)
                || !string.IsNullOrEmpty(baseUri.Fragment)) {
                throw new ArgumentException("UPAS supervisor endpoint must be an absolute HTTPS URI without credentials, query, or fragment.", nameof(endpoint));
            }
            var normalized = baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
                ? baseUri
                : new Uri(baseUri.AbsoluteUri + "/", UriKind.Absolute);
            return new Uri(normalized, "v1/status");
        }

        public async Task<UpasSupervisorStatus> GetStatusAsync(CancellationToken token) {
            var bearerToken = tokenProvider();
            if (string.IsNullOrWhiteSpace(bearerToken)) {
                throw new InvalidOperationException("UPAS_SUPERVISOR_CLIENT_TOKEN is not configured.");
            }

            using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            operationCts.CancelAfter(operationTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, statusUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            try {
                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    operationCts.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(operationCts.Token).ConfigureAwait(false);
                return UpasSupervisorStatus.Parse(json);
            } catch (OperationCanceledException ex) when (!token.IsCancellationRequested) {
                throw new TimeoutException(
                    $"External UPAS supervisor status did not complete within {operationTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.",
                    ex);
            }
        }
    }

    internal sealed class SettingsUpasSupervisorStatusSource : IUpasSupervisorStatusSource {
        internal static readonly HttpClient HttpClient = new() {
            Timeout = TimeSpan.FromSeconds(10)
        };

        public Task<UpasSupervisorStatus> GetStatusAsync(CancellationToken token) {
            var source = new HttpsUpasSupervisorStatusSource(
                HttpClient,
                Properties.Settings.Default.UpasSupervisorEndpoint,
                () => Environment.GetEnvironmentVariable("UPAS_SUPERVISOR_CLIENT_TOKEN"));
            return source.GetStatusAsync(token);
        }
    }

    internal static class AutomatedMoveExecutorFactory {
        public static IAutomatedMoveExecutor CreateDefault() {
            return new ModeSelectingAutomatedMoveExecutor(
                new SettingsExternalSupervisorRequirement(),
                new LegacyAutomatedMoveExecutor(),
                new SupervisorRequiredAutomatedMoveExecutor(
                    new HttpsUpasSupervisorRawRelativeClient(
                        SettingsUpasSupervisorStatusSource.HttpClient,
                        Properties.Settings.Default.UpasSupervisorEndpoint,
                        () => Environment.GetEnvironmentVariable("UPAS_SUPERVISOR_CLIENT_TOKEN"))));
        }
    }
}
