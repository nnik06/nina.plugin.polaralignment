using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal interface IUpasSupervisorRawRelativeClient {
        Task<AutomatedMoveExecutionResult> ExecuteAzimuthAsync(
            int rawCount, CancellationToken token);
    }

    internal sealed class HttpsUpasSupervisorRawRelativeClient
            : IUpasSupervisorRawRelativeClient {
        internal const string ClientId = "nina-tppa";
        internal const string CoordinateConvention = "grblRawRelative_v1";
        internal const double LeaseDurationSeconds = 120.0;

        private readonly HttpClient httpClient;
        private readonly Uri endpoint;
        private readonly Func<string> tokenProvider;
        private readonly TimeSpan operationTimeout;
        private readonly TimeSpan pollInterval;
        private readonly Func<string> idempotencyKeyProvider;

        public HttpsUpasSupervisorRawRelativeClient(
                HttpClient httpClient,
                string endpoint,
                Func<string> tokenProvider,
                TimeSpan? operationTimeout = null,
                TimeSpan? pollInterval = null,
                Func<string> idempotencyKeyProvider = null) {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.endpoint = HttpsUpasSupervisorCoarseEvidenceSource.ValidateEndpoint(endpoint);
            this.tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
            this.operationTimeout = operationTimeout ?? TimeSpan.FromSeconds(120);
            this.pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(250);
            if (this.operationTimeout <= TimeSpan.Zero || this.pollInterval <= TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(operationTimeout));
            }
            this.idempotencyKeyProvider = idempotencyKeyProvider
                ?? (() => "tppa-raw-" + Guid.NewGuid().ToString("N"));
        }

        public async Task<AutomatedMoveExecutionResult> ExecuteAzimuthAsync(
                int rawCount, CancellationToken token) {
            if (rawCount == 0) {
                return AutomatedMoveExecutionResult.Denied(
                    "TPPA quantized the azimuth command to zero controller units.");
            }
            var bearerToken = tokenProvider();
            if (string.IsNullOrWhiteSpace(bearerToken)) {
                return AutomatedMoveExecutionResult.Denied(
                    "UPAS_SUPERVISOR_CLIENT_TOKEN is not configured.");
            }

            using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            operationCts.CancelAfter(operationTimeout);
            string leaseId = null;
            try {
                leaseId = await AcquireRawLeaseAsync(
                    bearerToken, Math.Abs(rawCount), operationCts.Token).ConfigureAwait(false);
                var idempotencyKey = idempotencyKeyProvider();
                if (string.IsNullOrWhiteSpace(idempotencyKey)) {
                    return AutomatedMoveExecutionResult.Denied(
                        "UPAS raw-relative idempotency key is empty.");
                }
                var payload = new JObject {
                    ["schemaVersion"] = 1,
                    ["clientId"] = ClientId,
                    ["leaseId"] = leaseId,
                    ["idempotencyKey"] = idempotencyKey,
                    ["measurementSessionId"] = "tppa-" + Guid.NewGuid().ToString("N"),
                    ["measurementId"] = "move-" + Guid.NewGuid().ToString("N"),
                    ["axis"] = "az",
                    ["rawCount"] = rawCount,
                    ["clientMaximumRawUnits"] = Math.Abs(rawCount),
                    ["coordinateConvention"] = CoordinateConvention,
                    ["reason"] = "TPPA bounded sky-response command"
                };
                payload["requestBodySha256"] =
                    HttpsUpasSupervisorCoarseTppaExecutor.ComputeRequestBodySha256(payload);
                var submitted = await SendJsonAsync(
                    HttpMethod.Post, new Uri(endpoint, "v1/raw-relative-corrections"),
                    bearerToken, payload, operationCts.Token).ConfigureAwait(false);
                var transactionId = RequireTransactionId(submitted, "raw-relative acceptance");
                return await PollTerminalAsync(
                    transactionId, rawCount, bearerToken, operationCts.Token).ConfigureAwait(false);
            } catch (OperationCanceledException ex) when (!token.IsCancellationRequested) {
                return AutomatedMoveExecutionResult.Denied(
                    $"UPAS raw-relative transaction timed out after "
                    + $"{operationTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds: {ex.Message}");
            } catch (Exception ex) {
                return AutomatedMoveExecutionResult.Denied(
                    "UPAS raw-relative transaction was rejected or unavailable: " + ex.Message);
            } finally {
                if (!string.IsNullOrWhiteSpace(leaseId)) {
                    await TryReleaseLeaseAsync(leaseId, bearerToken).ConfigureAwait(false);
                }
            }
        }

        private async Task<string> AcquireRawLeaseAsync(
                string bearerToken, int rawUnits, CancellationToken token) {
            var payload = new JObject {
                ["schemaVersion"] = 1,
                ["clientId"] = ClientId,
                ["durationSeconds"] = LeaseDurationSeconds,
                ["maximumRawUnits"] = rawUnits,
                ["coordinateConvention"] = CoordinateConvention
            };
            var response = await SendJsonAsync(
                HttpMethod.Post, new Uri(endpoint, "v1/raw-relative-leases"),
                bearerToken, payload, token).ConfigureAwait(false);
            if (response["leaseId"]?.Type != JTokenType.String
                    || !Guid.TryParse(response["leaseId"]?.Value<string>(), out _)
                    || response["clientId"]?.Value<string>() != ClientId) {
                throw new InvalidOperationException("UPAS supervisor returned an invalid raw-relative lease.");
            }
            return response["leaseId"].Value<string>();
        }

        private async Task<AutomatedMoveExecutionResult> PollTerminalAsync(
                string transactionId, int expectedRawCount, string bearerToken,
                CancellationToken token) {
            while (true) {
                var response = await SendJsonAsync(
                    HttpMethod.Get,
                    new Uri(endpoint, "v1/transactions/" + Uri.EscapeDataString(transactionId)),
                    bearerToken, null, token).ConfigureAwait(false);
                var state = response["state"]?.Value<string>();
                if (state == "VERIFIED") {
                    var actualRaw = response["plan"]?["rawUnits"]?.Value<int?>();
                    return actualRaw == Math.Abs(expectedRawCount)
                        ? AutomatedMoveExecutionResult.Verified(
                            $"UPAS supervisor verified raw azimuth command {expectedRawCount}.")
                        : AutomatedMoveExecutionResult.Denied(
                            "UPAS supervisor verified a transaction with a mismatched raw command.");
                }
                if (state is "REJECTED" or "ABORTED_PRE_MOTION" or "LOCKED_UNCERTAIN"
                        or "AZ_PROTECTION_ASSESSED") {
                    var detail = response["detail"]?.Value<string>()
                        ?? response["errorCode"]?.Value<string>() ?? state;
                    return AutomatedMoveExecutionResult.Denied(
                        "UPAS supervisor terminated the raw-relative transaction: " + detail);
                }
                await Task.Delay(pollInterval, token).ConfigureAwait(false);
            }
        }

        private async Task<JObject> SendJsonAsync(HttpMethod method, Uri uri,
                string bearerToken, JObject payload, CancellationToken token) {
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (payload != null) {
                request.Content = new StringContent(
                    payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
            }
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

        private static string RequireTransactionId(JObject response, string operation) {
            var value = response["transactionId"]?.Value<string>();
            if (!Guid.TryParse(value, out _)) {
                throw new InvalidOperationException($"UPAS {operation} response lacks a transaction ID.");
            }
            return value;
        }

        private async Task TryReleaseLeaseAsync(string leaseId, string bearerToken) {
            try {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var request = new HttpRequestMessage(HttpMethod.Delete,
                    new Uri(endpoint, "v1/leases/" + Uri.EscapeDataString(leaseId)));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                await httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            } catch {
                // Lease expiry remains the server-side fail-safe.
            }
        }
    }
}
