using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record UpasSupervisorTppaObservationLease(
        string LeaseId,
        string Nonce,
        Guid CampaignId,
        string BootId,
        long MotionEpoch,
        long MotionCounter,
        long OpenedMonotonicNs,
        long ExpiresMonotonicNs);

    internal sealed record UpasSupervisorTppaObservationAttestation(
        string LeaseId,
        string Nonce,
        Guid CampaignId,
        string BootId,
        long MotionEpoch,
        long MotionCounter,
        long OpenedMonotonicNs,
        long ClosedMonotonicNs,
        string CaptureDigestSha256,
        string AttestationSha256);

    internal interface IUpasSupervisorTppaObservationClient {
        Task<UpasSupervisorTppaObservationLease> OpenAsync(
            Guid campaignId,
            TimeSpan duration,
            CancellationToken token);

        Task<UpasSupervisorTppaObservationAttestation> CloseAsync(
            UpasSupervisorTppaObservationLease lease,
            string captureDigestSha256,
            CancellationToken token);
    }

    internal sealed class HttpsUpasSupervisorTppaObservationClient
            : IUpasSupervisorTppaObservationClient {
        internal const string ClientId = "nina-tppa";
        internal static readonly TimeSpan MaximumLeaseDuration = TimeSpan.FromSeconds(120);

        private readonly HttpClient httpClient;
        private readonly Uri endpoint;
        private readonly Func<string> tokenProvider;
        private readonly TimeSpan requestTimeout;

        public HttpsUpasSupervisorTppaObservationClient(
                HttpClient httpClient,
                string endpoint,
                Func<string> tokenProvider,
                TimeSpan? requestTimeout = null) {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.endpoint = HttpsUpasSupervisorCoarseEvidenceSource.ValidateEndpoint(endpoint);
            this.tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
            this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(15);
            if (this.requestTimeout <= TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(requestTimeout));
            }
        }

        public async Task<UpasSupervisorTppaObservationLease> OpenAsync(
                Guid campaignId,
                TimeSpan duration,
                CancellationToken token) {
            if (campaignId == Guid.Empty) throw new ArgumentException("Campaign identity is required.", nameof(campaignId));
            if (duration <= TimeSpan.Zero || duration > MaximumLeaseDuration) {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }
            var payload = new JObject {
                ["schemaVersion"] = 1,
                ["clientId"] = ClientId,
                ["campaignId"] = campaignId.ToString("D"),
                ["durationMilliseconds"] = checked((long)Math.Ceiling(duration.TotalMilliseconds)),
                ["requestBodySha256"] = new string('0', 64)
            };
            payload["requestBodySha256"] = HttpsUpasSupervisorCoarseTppaExecutor.ComputeRequestBodySha256(payload);
            var root = await SendAsync(new Uri(endpoint, "v1/coarse/tppa-observations"), payload, token).ConfigureAwait(false);
            RequireExactProperties(root, "schemaVersion", "leaseId", "nonce", "campaignId", "bootId",
                "motionEpoch", "motionCounter", "openedMonotonicNs", "expiresMonotonicNs");
            RequireSchema(root);
            var returnedCampaign = RequireGuid(root, "campaignId");
            if (returnedCampaign != campaignId) throw new InvalidOperationException("Observation response campaign does not match the request.");
            var opened = RequireNonnegativeInteger(root, "openedMonotonicNs");
            var expires = RequireNonnegativeInteger(root, "expiresMonotonicNs");
            if (expires <= opened) throw new InvalidOperationException("Observation lease has an invalid monotonic lifetime.");
            return new UpasSupervisorTppaObservationLease(
                RequireText(root, "leaseId"), RequireHex(root, "nonce"), returnedCampaign,
                RequireText(root, "bootId"), RequireNonnegativeInteger(root, "motionEpoch"),
                RequireNonnegativeInteger(root, "motionCounter"), opened, expires);
        }

        public async Task<UpasSupervisorTppaObservationAttestation> CloseAsync(
                UpasSupervisorTppaObservationLease lease,
                string captureDigestSha256,
                CancellationToken token) {
            if (lease == null) throw new ArgumentNullException(nameof(lease));
            RequireLowerHex(captureDigestSha256, nameof(captureDigestSha256));
            var payload = new JObject {
                ["schemaVersion"] = 1,
                ["clientId"] = ClientId,
                ["leaseId"] = lease.LeaseId,
                ["nonce"] = lease.Nonce,
                ["captureDigestSha256"] = captureDigestSha256,
                ["requestBodySha256"] = new string('0', 64)
            };
            payload["requestBodySha256"] = HttpsUpasSupervisorCoarseTppaExecutor.ComputeRequestBodySha256(payload);
            var root = await SendAsync(new Uri(endpoint, "v1/coarse/tppa-observations/close"), payload, token).ConfigureAwait(false);
            RequireExactProperties(root, "schemaVersion", "leaseId", "nonce", "campaignId", "bootId",
                "motionEpoch", "motionCounter", "openedMonotonicNs", "closedMonotonicNs",
                "captureDigestSha256", "attestationSha256");
            RequireSchema(root);
            var campaign = RequireGuid(root, "campaignId");
            var closed = RequireNonnegativeInteger(root, "closedMonotonicNs");
            var attestation = new UpasSupervisorTppaObservationAttestation(
                RequireText(root, "leaseId"), RequireHex(root, "nonce"), campaign,
                RequireText(root, "bootId"), RequireNonnegativeInteger(root, "motionEpoch"),
                RequireNonnegativeInteger(root, "motionCounter"),
                RequireNonnegativeInteger(root, "openedMonotonicNs"), closed,
                RequireHex(root, "captureDigestSha256"), RequireHex(root, "attestationSha256"));
            if (attestation.LeaseId != lease.LeaseId || attestation.Nonce != lease.Nonce
                    || attestation.CampaignId != lease.CampaignId || attestation.BootId != lease.BootId
                    || attestation.MotionEpoch != lease.MotionEpoch || attestation.MotionCounter != lease.MotionCounter
                    || attestation.OpenedMonotonicNs != lease.OpenedMonotonicNs
                    || attestation.CaptureDigestSha256 != captureDigestSha256
                    || attestation.ClosedMonotonicNs < attestation.OpenedMonotonicNs) {
                throw new InvalidOperationException("Observation attestation does not match the opened lease or capture.");
            }
            return attestation;
        }

        private async Task<JObject> SendAsync(Uri uri, JObject payload, CancellationToken token) {
            var bearer = tokenProvider();
            if (string.IsNullOrWhiteSpace(bearer)) throw new InvalidOperationException("UPAS_SUPERVISOR_CLIENT_TOKEN is not configured.");
            using var request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            request.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(requestTimeout);
            try {
                using var response = await httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) {
                    throw new InvalidOperationException($"UPAS observation endpoint rejected the request ({(int)response.StatusCode}): {body}");
                }
                return JObject.Parse(body);
            } catch (OperationCanceledException ex) when (!token.IsCancellationRequested) {
                throw new TimeoutException("UPAS observation request timed out.", ex);
            }
        }

        private static void RequireExactProperties(JObject root, params string[] expected) {
            var actual = new System.Collections.Generic.HashSet<string>(root.Properties().Select(item => item.Name), StringComparer.Ordinal);
            if (!actual.SetEquals(expected)) throw new InvalidOperationException("Observation response schema is not exact.");
        }
        private static void RequireSchema(JObject root) {
            if (root["schemaVersion"]?.Type != JTokenType.Integer || root.Value<int>("schemaVersion") != 1)
                throw new InvalidOperationException("Observation response schema version is unsupported.");
        }
        private static string RequireText(JObject root, string name) {
            if (root[name]?.Type != JTokenType.String || string.IsNullOrWhiteSpace(root.Value<string>(name)))
                throw new InvalidOperationException($"Observation response {name} is invalid.");
            return root.Value<string>(name);
        }
        private static string RequireHex(JObject root, string name) {
            var value = RequireText(root, name); RequireLowerHex(value, name); return value;
        }
        private static void RequireLowerHex(string value, string name) {
            if (value?.Length != 64 || value.Any(c => !(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')))
                throw new InvalidOperationException($"{name} is not a lowercase SHA-256 digest.");
        }
        private static Guid RequireGuid(JObject root, string name) {
            return Guid.TryParseExact(RequireText(root, name), "D", out var value) && value != Guid.Empty
                ? value : throw new InvalidOperationException($"Observation response {name} is invalid.");
        }
        private static long RequireNonnegativeInteger(JObject root, string name) {
            if (root[name]?.Type != JTokenType.Integer) throw new InvalidOperationException($"Observation response {name} is invalid.");
            var value = root.Value<long>(name);
            return value >= 0 ? value : throw new InvalidOperationException($"Observation response {name} is negative.");
        }
    }
}
