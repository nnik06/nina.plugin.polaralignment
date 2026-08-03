using System;
using System.Collections.Generic;
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
    internal sealed record TppaCoarseDeterminationEvidence(
        Guid DeterminationId,
        string ObservationLeaseNonce,
        string ObservationAttestationSha256,
        DateTime StartedUtc,
        DateTime CompletedUtc,
        string TargetSkyArcId,
        bool TruePoleRefractionEnabled,
        string RepositoryHead,
        string PluginAssemblySha256,
        string HardwareConfigurationId,
        string MechanicalStateSha256,
        string SolverIdentity,
        string CatalogIdentity,
        IReadOnlyList<TppaCoarseSourceSolveEvidence> SourceSolves,
        double AzimuthErrorMinutes,
        double AltitudeErrorMinutes,
        double CovarianceAzAzSquareMinutes,
        double CovarianceAzAltSquareMinutes,
        double CovarianceAltAltSquareMinutes);

    internal sealed record UpasSupervisorCoarseTppaResult(
        bool Completed,
        bool MoveWasRequired,
        string TransactionId,
        string State);

    internal interface IUpasSupervisorCoarseTppaExecutor {
        Task<UpasSupervisorCoarseTppaResult> ExecuteAsync(
            TppaCoarseDeterminationEvidence first,
            TppaCoarseDeterminationEvidence second,
            double currentTemperatureC,
            double requiredTravelDegrees,
            string currentLoadProfileId,
            CancellationToken token);
    }

    internal sealed class HttpsUpasSupervisorCoarseTppaExecutor
            : IUpasSupervisorCoarseTppaExecutor {
        internal const string ClientId = "nina-tppa";
        internal const double LeaseDurationSeconds = 120.0;
        internal const double MaximumDeterminationAgeMilliseconds = 180_000.0;

        private readonly HttpClient httpClient;
        private readonly Uri endpoint;
        private readonly Func<string> tokenProvider;
        private readonly TimeSpan operationTimeout;
        private readonly Func<string> idempotencyKeyProvider;
        private readonly Func<DateTime> utcNowProvider;
        private readonly string covarianceAuthoritySha256;

        public HttpsUpasSupervisorCoarseTppaExecutor(
                HttpClient httpClient,
                string endpoint,
                Func<string> tokenProvider,
                string covarianceAuthoritySha256,
                TimeSpan? operationTimeout = null,
                Func<string> idempotencyKeyProvider = null,
                Func<DateTime> utcNowProvider = null) {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.endpoint = HttpsUpasSupervisorCoarseEvidenceSource.ValidateEndpoint(endpoint);
            this.tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
            RequireLowerHexSha256(
                covarianceAuthoritySha256, nameof(covarianceAuthoritySha256));
            this.covarianceAuthoritySha256 = covarianceAuthoritySha256;
            this.operationTimeout = operationTimeout ?? TimeSpan.FromSeconds(120);
            if (this.operationTimeout <= TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(operationTimeout));
            }
            this.idempotencyKeyProvider = idempotencyKeyProvider
                ?? (() => "tppa-coarse-" + Guid.NewGuid().ToString("N"));
            this.utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);
        }

        public async Task<UpasSupervisorCoarseTppaResult> ExecuteAsync(
                TppaCoarseDeterminationEvidence first,
                TppaCoarseDeterminationEvidence second,
                double currentTemperatureC,
                double requiredTravelDegrees,
                string currentLoadProfileId,
                CancellationToken token) {
            TppaCoarseDeterminationReceiptBuilder.ValidateIndependent(first, second);
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
            var nowUtc = RequireUtc(utcNowProvider(), "request time");
            var firstDetermination = new JObject {
                ["receipt"] = TppaCoarseDeterminationReceiptBuilder.Build(first, nowUtc),
                ["observationAttestationSha256"] = first.ObservationAttestationSha256
            };
            var secondDetermination = new JObject {
                ["receipt"] = TppaCoarseDeterminationReceiptBuilder.Build(second, nowUtc),
                ["observationAttestationSha256"] = second.ObservationAttestationSha256
            };
            var idempotencyKey = idempotencyKeyProvider();
            if (string.IsNullOrWhiteSpace(idempotencyKey)) {
                throw new InvalidOperationException("Coarse TPPA idempotency key is empty.");
            }
            string leaseId = null;
            try {
                leaseId = await AcquireLeaseAsync(
                    bearerToken, requiredTravelDegrees, operationCts.Token).ConfigureAwait(false);
                var requestWithoutDigest = new JObject {
                    ["schemaVersion"] = 1,
                    ["clientId"] = ClientId,
                    ["leaseId"] = leaseId,
                    ["idempotencyKey"] = idempotencyKey,
                    ["currentTemperatureMilliCelsius"] = ToScaledInteger(
                        currentTemperatureC, 1000.0, nameof(currentTemperatureC)),
                    ["currentLoadProfileId"] = currentLoadProfileId,
                    ["covarianceAuthoritySha256"] = covarianceAuthoritySha256,
                    ["determinations"] = new JArray {
                        firstDetermination,
                        secondDetermination
                    }
                };
                requestWithoutDigest["requestBodySha256"] =
                    ComputeRequestBodySha256(requestWithoutDigest);
                var response = await SendJsonAsync(
                    HttpMethod.Post,
                    new Uri(endpoint, "v1/coarse/tppa-correction"),
                    bearerToken,
                    requestWithoutDigest,
                    operationCts.Token).ConfigureAwait(false);
                return ParseTerminalResponse(response);
            } catch (OperationCanceledException ex) when (!token.IsCancellationRequested) {
                throw new TimeoutException(
                    $"UPAS coarse TPPA transaction did not complete within "
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
            var clone = (JObject)requestWithoutDigest.DeepClone();
            clone.Remove("requestBodySha256");
            var canonical = Canonicalize(clone);
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

        private static UpasSupervisorCoarseTppaResult ParseTerminalResponse(JObject root) {
            RequireExactProperties(root,
                "schemaVersion", "transactionId", "state", "replayed",
                "planKind", "moveWasRequired");
            var valid = root["schemaVersion"]?.Type == JTokenType.Integer
                && root["schemaVersion"]?.Value<int>() == 1
                && root["transactionId"]?.Type == JTokenType.String
                && Guid.TryParse(root["transactionId"]?.Value<string>(), out _)
                && root["state"]?.Value<string>() == "COMPLETED"
                && root["replayed"]?.Type == JTokenType.Boolean
                && root["planKind"]?.Value<string>() == "coarseCorrection"
                && root["moveWasRequired"]?.Type == JTokenType.Boolean;
            if (!valid) {
                throw new InvalidOperationException(
                    "UPAS supervisor coarse TPPA response is not a valid completed transaction.");
            }
            return new UpasSupervisorCoarseTppaResult(
                true,
                root["moveWasRequired"].Value<bool>(),
                root["transactionId"].Value<string>(),
                "COMPLETED");
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

        private static string Canonicalize(JToken token) => token.Type switch {
            JTokenType.Object => "{" + string.Join(",", ((JObject)token).Properties()
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => JsonConvert.ToString(property.Name)
                    + ":" + Canonicalize(property.Value))) + "}",
            JTokenType.Array => "[" + string.Join(",", ((JArray)token)
                .Select(Canonicalize)) + "]",
            JTokenType.String => JsonConvert.ToString(token.Value<string>()),
            JTokenType.Integer => token.Value<long>().ToString(CultureInfo.InvariantCulture),
            JTokenType.Boolean => token.Value<bool>() ? "true" : "false",
            JTokenType.Null => "null",
            _ => throw new InvalidOperationException(
                "Coarse TPPA digest supports only exact strings, integers, booleans, arrays, and objects.")
        };

        private static long CovarianceInteger(double squareMinutes, string name) {
            if (!double.IsFinite(squareMinutes)) {
                throw new ArgumentOutOfRangeException(name);
            }
            return ToScaledInteger(squareMinutes / 3600.0, 1_000_000_000_000.0, name);
        }

        private static long ToScaledInteger(double value, double scale, string name) {
            if (!double.IsFinite(value) || !double.IsFinite(scale) || scale <= 0.0) {
                throw new ArgumentOutOfRangeException(name);
            }
            return checked((long)Math.Round(value * scale, MidpointRounding.AwayFromZero));
        }

        private static DateTime RequireUtc(DateTime value, string name) {
            if (value.Kind != DateTimeKind.Utc) {
                throw new ArgumentException($"{name} must be UTC.", name);
            }
            return value;
        }



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
