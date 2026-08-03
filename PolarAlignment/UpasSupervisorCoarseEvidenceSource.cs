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

namespace NINA.Plugins.PolarAlignment {
    internal interface IUpasSupervisorCoarseEvidenceSource {
        Task<UpasSupervisorCoarsePlanningEvidence> GetAsync(
            Guid? expectedCallerLeaseId,
            double currentTemperatureC,
            string currentLoadProfileId,
            CancellationToken token);
    }

    internal sealed class HttpsUpasSupervisorCoarseEvidenceSource
            : IUpasSupervisorCoarseEvidenceSource {
        public const string ContentSha256Header = "X-UPAS-Content-SHA256";
        public const string EvidenceIdHeader = "X-UPAS-Evidence-ID";

        private readonly HttpClient httpClient;
        private readonly Uri endpoint;
        private readonly Func<string> tokenProvider;
        private readonly Func<string> nonceProvider;
        private readonly TimeSpan operationTimeout;

        public HttpsUpasSupervisorCoarseEvidenceSource(
                HttpClient httpClient,
                string endpoint,
                Func<string> tokenProvider,
                TimeSpan? operationTimeout = null,
                Func<string> nonceProvider = null) {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
            this.endpoint = ValidateEndpoint(endpoint);
            this.operationTimeout = operationTimeout ?? TimeSpan.FromSeconds(10);
            if (this.operationTimeout <= TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(operationTimeout));
            }
            this.nonceProvider = nonceProvider ?? CreateNonce;
        }

        public async Task<UpasSupervisorCoarsePlanningEvidence> GetAsync(
                Guid? expectedCallerLeaseId,
                double currentTemperatureC,
                string currentLoadProfileId,
                CancellationToken token) {
            if (!double.IsFinite(currentTemperatureC)) {
                throw new ArgumentOutOfRangeException(nameof(currentTemperatureC));
            }
            if (string.IsNullOrWhiteSpace(currentLoadProfileId)) {
                throw new ArgumentException("Current load profile is required.", nameof(currentLoadProfileId));
            }
            var bearerToken = tokenProvider();
            if (string.IsNullOrWhiteSpace(bearerToken)) {
                throw new InvalidOperationException("UPAS_SUPERVISOR_CLIENT_TOKEN is not configured.");
            }
            var nonce = nonceProvider();
            RequireLowerHexSha256(nonce, "generated request nonce");
            var requestUri = BuildEvidenceUri(endpoint, nonce);

            using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            operationCts.CancelAfter(operationTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var started = Stopwatch.GetTimestamp();
            try {
                using var response = await httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead,
                    operationCts.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri != requestUri) {
                    throw new InvalidOperationException("UPAS coarse evidence request was redirected.");
                }
                if (!string.Equals(response.Content.Headers.ContentType?.MediaType,
                        "application/json", StringComparison.OrdinalIgnoreCase)) {
                    throw new InvalidOperationException("UPAS coarse evidence response is not application/json.");
                }
                var authenticatedContentSha256 = RequireSingleHeader(response, ContentSha256Header);
                var authenticatedEvidenceId = RequireSingleHeader(response, EvidenceIdHeader);
                var body = await response.Content.ReadAsByteArrayAsync(operationCts.Token)
                    .ConfigureAwait(false);
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var json = new UTF8Encoding(false, true).GetString(body);
                return UpasSupervisorCoarsePlanningEvidenceParser.ParseAuthenticated(
                    json, nonce, elapsed, expectedCallerLeaseId,
                    currentTemperatureC, currentLoadProfileId,
                    authenticatedContentSha256, authenticatedEvidenceId,
                    UpasSupervisorCoarsePlanningEvidenceParser.RequiredAuthenticationMethod);
            } catch (OperationCanceledException ex) when (!token.IsCancellationRequested) {
                throw new TimeoutException(
                    $"External UPAS supervisor coarse evidence did not complete within " +
                    $"{operationTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.", ex);
            }
        }

        internal static Uri ValidateEndpoint(string endpoint) {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var value)
                    || value.Scheme != Uri.UriSchemeHttps
                    || !string.IsNullOrEmpty(value.UserInfo)
                    || !string.IsNullOrEmpty(value.Query)
                    || !string.IsNullOrEmpty(value.Fragment)) {
                throw new ArgumentException(
                    "UPAS supervisor endpoint must be an absolute HTTPS URI without credentials, query, or fragment.",
                    nameof(endpoint));
            }
            return value.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
                ? value
                : new Uri(value.AbsoluteUri + "/", UriKind.Absolute);
        }

        internal static Uri BuildEvidenceUri(Uri endpoint, string nonce) {
            RequireLowerHexSha256(nonce, nameof(nonce));
            return new Uri(endpoint,
                "v1/coarse-planning-evidence?nonce=" + nonce);
        }

        private static string RequireSingleHeader(HttpResponseMessage response, string name) {
            if (!response.Headers.TryGetValues(name, out var values)) {
                throw new InvalidOperationException($"UPAS coarse evidence response is missing {name}.");
            }
            var materialized = values.ToArray();
            if (materialized.Length != 1) {
                throw new InvalidOperationException($"UPAS coarse evidence response has ambiguous {name}.");
            }
            RequireLowerHexSha256(materialized[0], name);
            return materialized[0];
        }

        private static string CreateNonce() {
            var bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            return string.Concat(bytes.Select(value => value.ToString("x2")));
        }

        private static void RequireLowerHexSha256(string value, string name) {
            if (value == null || value.Length != 64
                    || value.Any(character => !((character >= '0' && character <= '9')
                        || (character >= 'a' && character <= 'f')))) {
                throw new InvalidOperationException($"{name} must be exactly 64 lowercase hexadecimal characters.");
            }
        }
    }
}
