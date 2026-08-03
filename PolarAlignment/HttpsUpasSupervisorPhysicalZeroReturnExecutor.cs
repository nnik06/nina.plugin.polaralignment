using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal sealed class HttpsUpasSupervisorPhysicalZeroReturnExecutor
            : IUpasSupervisorPhysicalZeroReturnExecutor {
        internal const string ClientId = "nina-tppa";
        internal const string ZeroReferenceId = "upas-physical-zero-v1";
        internal const double LeaseDurationSeconds = 120.0;

        private readonly HttpClient httpClient;
        private readonly Uri endpoint;
        private readonly Func<string> tokenProvider;
        private readonly TimeSpan operationTimeout;
        private readonly Func<string> idempotencyKeyProvider;

        public HttpsUpasSupervisorPhysicalZeroReturnExecutor(
                HttpClient httpClient,
                string endpoint,
                Func<string> tokenProvider,
                TimeSpan? operationTimeout = null,
                Func<string> idempotencyKeyProvider = null) {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.endpoint = HttpsUpasSupervisorCoarseEvidenceSource.ValidateEndpoint(endpoint);
            this.tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
            this.operationTimeout = operationTimeout ?? TimeSpan.FromSeconds(120);
            if (this.operationTimeout <= TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(operationTimeout));
            }
            this.idempotencyKeyProvider = idempotencyKeyProvider
                ?? (() => "tppa-zero-" + Guid.NewGuid().ToString("N"));
        }

        public async Task<UpasSupervisorPhysicalZeroReturnResult> ReturnAsync(
                string preEvidenceId,
                double currentTemperatureC,
                double requiredTravelDegrees,
                string currentLoadProfileId,
                CancellationToken token) {
            RequireLowerHexSha256(preEvidenceId, nameof(preEvidenceId));
            if (!double.IsFinite(currentTemperatureC)
                    || currentTemperatureC < -100.0 || currentTemperatureC > 100.0) {
                throw new ArgumentOutOfRangeException(nameof(currentTemperatureC));
            }
            if (!double.IsFinite(requiredTravelDegrees) || requiredTravelDegrees <= 0.0) {
                throw new ArgumentOutOfRangeException(nameof(requiredTravelDegrees));
            }
            if (string.IsNullOrWhiteSpace(currentLoadProfileId)) {
                throw new ArgumentException("Current load profile is required.",
                    nameof(currentLoadProfileId));
            }
            var bearerToken = tokenProvider();
            if (string.IsNullOrWhiteSpace(bearerToken)) {
                throw new InvalidOperationException("UPAS_SUPERVISOR_CLIENT_TOKEN is not configured.");
            }

            using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            operationCts.CancelAfter(operationTimeout);
            string leaseId = null;
            try {
                leaseId = await AcquireLeaseAsync(
                    bearerToken, requiredTravelDegrees, operationCts.Token).ConfigureAwait(false);
                var idempotencyKey = idempotencyKeyProvider();
                if (string.IsNullOrWhiteSpace(idempotencyKey)) {
                    throw new InvalidOperationException("Physical-zero idempotency key is empty.");
                }
                var temperatureMilliCelsius = checked((int)Math.Round(
                    currentTemperatureC * 1000.0, MidpointRounding.AwayFromZero));
                var requestWithoutDigest = new JObject {
                    ["schemaVersion"] = 1,
                    ["clientId"] = ClientId,
                    ["leaseId"] = leaseId,
                    ["idempotencyKey"] = idempotencyKey,
                    ["preEvidenceId"] = preEvidenceId,
                    ["currentTemperatureMilliCelsius"] = temperatureMilliCelsius,
                    ["currentLoadProfileId"] = currentLoadProfileId,
                    ["zeroReferenceId"] = ZeroReferenceId
                };
                var digest = ComputeRequestBodySha256(requestWithoutDigest);
                requestWithoutDigest["requestBodySha256"] = digest;
                var response = await SendJsonAsync(
                    HttpMethod.Post,
                    new Uri(endpoint, "v1/coarse/physical-zero"),
                    bearerToken,
                    requestWithoutDigest,
                    operationCts.Token).ConfigureAwait(false);
                return ParseTerminalResponse(response);
            } catch (OperationCanceledException ex) when (!token.IsCancellationRequested) {
                throw new TimeoutException(
                    $"UPAS physical-zero transaction did not complete within "
                    + $"{operationTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.",
                    ex);
            } finally {
                if (!string.IsNullOrWhiteSpace(leaseId)) {
                    await TryReleaseLeaseAsync(leaseId, bearerToken).ConfigureAwait(false);
                }
            }
        }

        internal static string ComputeRequestBodySha256(JObject requestWithoutDigest) {
            if (requestWithoutDigest == null) {
                throw new ArgumentNullException(nameof(requestWithoutDigest));
            }
            var properties = requestWithoutDigest.Properties()
                .Where(property => property.Name != "requestBodySha256")
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => JsonConvert.ToString(property.Name)
                    + ":" + CanonicalScalar(property.Value));
            var canonical = "{" + string.Join(",", properties) + "}";
            return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        }

        private async Task<string> AcquireLeaseAsync(
                string bearerToken,
                double requiredTravelDegrees,
                CancellationToken token) {
            var payload = new JObject {
                ["schemaVersion"] = 1,
                ["clientId"] = ClientId,
                ["durationSeconds"] = LeaseDurationSeconds,
                ["maximumTravelDegrees"] = requiredTravelDegrees
            };
            var response = await SendJsonAsync(
                HttpMethod.Post, new Uri(endpoint, "v1/leases"),
                bearerToken, payload, token).ConfigureAwait(false);
            RequireExactProperties(response,
                "leaseId", "clientId", "issuedMonotonicNs", "expiresMonotonicNs",
                "plannedTravelDegrees", "maximumTravelDegrees");
            if (response["leaseId"]?.Type != JTokenType.String
                    || !Guid.TryParse(response["leaseId"]?.Value<string>(), out _)
                    || response["clientId"]?.Type != JTokenType.String
                    || response["clientId"]?.Value<string>() != ClientId
                    || response["maximumTravelDegrees"]?.Type is not (
                        JTokenType.Float or JTokenType.Integer)
                    || Math.Abs(response["maximumTravelDegrees"].Value<double>()
                        - requiredTravelDegrees) > 1e-9) {
                throw new InvalidOperationException(
                    "UPAS supervisor returned an invalid or mismatched lease.");
            }
            return response["leaseId"].Value<string>();
        }

        private async Task<JObject> SendJsonAsync(
                HttpMethod method,
                Uri uri,
                string bearerToken,
                JObject payload,
                CancellationToken token) {
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent(
                payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
            using var response = await httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri != uri) {
                throw new InvalidOperationException("UPAS supervisor request was redirected.");
            }
            var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (!string.Equals(response.Content.Headers.ContentType?.MediaType,
                    "application/json", StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException("UPAS supervisor response is not application/json.");
            }
            return JObject.Parse(body, new JsonLoadSettings {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
            });
        }

        private static UpasSupervisorPhysicalZeroReturnResult ParseTerminalResponse(JObject root) {
            RequireExactProperties(root,
                "schemaVersion", "transactionId", "state", "replayed",
                "planKind", "returnWasRequired", "tppaCampaign");
            var valid = root["schemaVersion"]?.Type == JTokenType.Integer
                && root["schemaVersion"]?.Value<int>() == 1
                && root["transactionId"]?.Type == JTokenType.String
                && Guid.TryParse(root["transactionId"]?.Value<string>(), out _)
                && root["state"]?.Type == JTokenType.String
                && root["state"]?.Value<string>() == "completed"
                && root["replayed"]?.Type == JTokenType.Boolean
                && root["planKind"]?.Type == JTokenType.String
                && root["planKind"]?.Value<string>() == "physicalZeroPreflight"
                && root["returnWasRequired"]?.Type == JTokenType.Boolean
                && root["tppaCampaign"]?.Type == JTokenType.Object;
            if (!valid) {
                throw new InvalidOperationException(
                    "UPAS supervisor physical-zero response is not a valid completed transaction.");
            }
            var campaign = (JObject)root["tppaCampaign"];
            RequireExactProperties(campaign,
                "campaignId", "openedMonotonicNs", "expiresMonotonicNs");
            if (campaign["campaignId"]?.Type != JTokenType.String
                    || !Guid.TryParse(campaign["campaignId"]?.Value<string>(), out _)
                    || campaign["openedMonotonicNs"]?.Type != JTokenType.Integer
                    || campaign["expiresMonotonicNs"]?.Type != JTokenType.Integer
                    || campaign["expiresMonotonicNs"].Value<long>()
                        <= campaign["openedMonotonicNs"].Value<long>()) {
                throw new InvalidOperationException(
                    "UPAS supervisor returned an invalid TPPA campaign.");
            }
            return new UpasSupervisorPhysicalZeroReturnResult(
                true, root["transactionId"].Value<string>(), "completed",
                campaign["campaignId"].Value<string>(),
                campaign["expiresMonotonicNs"].Value<long>());
        }

        private async Task TryReleaseLeaseAsync(string leaseId, string bearerToken) {
            try {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var uri = new Uri(endpoint, "v1/leases/" + Uri.EscapeDataString(leaseId));
                using var request = new HttpRequestMessage(HttpMethod.Delete, uri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                using var response = await httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);
                if (response.RequestMessage?.RequestUri != uri) {
                    return;
                }
            } catch {
                // Lease expiry remains the server-side fail-safe.
            }
        }

        private static string CanonicalScalar(JToken token) =>
            token.Type switch {
                JTokenType.String => JsonConvert.ToString(token.Value<string>()),
                JTokenType.Integer => token.Value<long>().ToString(CultureInfo.InvariantCulture),
                _ => throw new InvalidOperationException(
                    "Physical-zero digest supports only string and integer request fields.")
            };

        private static void RequireExactProperties(JObject value, params string[] expected) {
            var actual = value.Properties().Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal);
            var required = expected.OrderBy(name => name, StringComparer.Ordinal);
            if (!actual.SequenceEqual(required, StringComparer.Ordinal)) {
                throw new InvalidOperationException(
                    "UPAS supervisor response properties do not match the frozen contract.");
            }
        }

        private static void RequireLowerHexSha256(string value, string name) {
            if (value == null || value.Length != 64
                    || value.Any(character => !((character >= '0' && character <= '9')
                        || (character >= 'a' && character <= 'f')))) {
                throw new InvalidOperationException(
                    $"{name} must be exactly 64 lowercase hexadecimal characters.");
            }
        }
    }
}
