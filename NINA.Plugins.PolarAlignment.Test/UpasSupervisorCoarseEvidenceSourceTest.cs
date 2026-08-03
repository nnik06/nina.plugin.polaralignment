using FluentAssertions;
using NINA.Plugins.PolarAlignment;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class UpasSupervisorCoarseEvidenceSourceTest {
        private const string Nonce = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string EvidenceId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private const string DenialFixtureSha256 =
            "7d793fe296e73c2ca3ed87057047f2299745fd842715c4fee10077a88f103470";

        [TestCase("http://127.0.0.1:8443/")]
        [TestCase("https://user:secret@127.0.0.1:8443/")]
        [TestCase("https://127.0.0.1:8443/?unsafe=true")]
        public void EndpointMustBeUnadornedHttps(string endpoint) {
            Action validate = () =>
                HttpsUpasSupervisorCoarseEvidenceSource.ValidateEndpoint(endpoint);

            validate.Should().Throw<ArgumentException>();
        }

        [Test]
        public async Task MissingBearerFailsBeforeNetworkActivity() {
            var handler = new EvidenceHandler(ValidResponse());
            var source = Source(handler, token: "");

            Func<Task> get = async () => await source.GetAsync(
                null, 35.0, "hae29c-ec-full-rig-v1", CancellationToken.None);

            await get.Should().ThrowAsync<InvalidOperationException>();
            handler.RequestCount.Should().Be(0);
        }

        [Test]
        public async Task AuthenticatedReadOnlyRequestReturnsBoundAggregate() {
            var handler = new EvidenceHandler(ValidResponse());
            var source = Source(handler);

            var result = await source.GetAsync(
                null, 35.0, "hae29c-ec-full-rig-v1", CancellationToken.None);

            result.Envelope.EvidenceId.Should().Be(EvidenceId);
            handler.RequestCount.Should().Be(1);
            handler.LastMethod.Should().Be(HttpMethod.Get);
            handler.LastAuthorization.Should().Be("Bearer secret");
            handler.LastUri.Should().Be(new Uri(
                "https://127.0.0.1:8443/base/v1/coarse-planning-evidence?nonce=" + Nonce));
        }

        [Test]
        public async Task RedirectAndBodyDigestMismatchFailClosed() {
            var redirected = new EvidenceHandler(ValidResponse()) {
                FinalUri = new Uri("https://127.0.0.1:8443/other")
            };
            var mismatched = new EvidenceHandler(ValidResponse()) {
                ContentDigestOverride = new string('d', 64)
            };

            Func<Task> redirect = async () => await Source(redirected).GetAsync(
                null, 35.0, "hae29c-ec-full-rig-v1", CancellationToken.None);
            Func<Task> digest = async () => await Source(mismatched).GetAsync(
                null, 35.0, "hae29c-ec-full-rig-v1", CancellationToken.None);

            await redirect.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*redirected*");
            await digest.Should().ThrowAsync<Newtonsoft.Json.JsonException>()
                .WithMessage("*response bytes*");
        }

        [Test]
        public async Task MissingDuplicateAndMalformedBindingHeadersReject() {
            var missing = new EvidenceHandler(ValidResponse()) { OmitEvidenceId = true };
            var duplicate = new EvidenceHandler(ValidResponse()) { DuplicateContentDigest = true };
            var malformed = new EvidenceHandler(ValidResponse()) { EvidenceIdOverride = "ABC" };

            foreach (var handler in new[] { missing, duplicate, malformed }) {
                Func<Task> get = async () => await Source(handler).GetAsync(
                    null, 35.0, "hae29c-ec-full-rig-v1", CancellationToken.None);
                await get.Should().ThrowAsync<InvalidOperationException>();
            }
        }

        [Test]
        public async Task ReadinessDenialIsSpecificAndNeverMaterializesEvidence() {
            var handler = new EvidenceHandler(ReadinessDenial()) {
                StatusCode = HttpStatusCode.Conflict
            };
            var source = Source(handler);

            Func<Task> get = async () => await source.GetAsync(
                null, 35.0, "hae29c-ec-full-rig-v1", CancellationToken.None);

            var failure = await get.Should()
                .ThrowAsync<UpasSupervisorCoarseEvidenceUnavailableException>();
            failure.Which.MissingAuthorities.Should().HaveCount(9);
            failure.Which.MissingAuthorities.Should().Contain(
                "atomicSignedAzAltPhysicalPosition");
            failure.Which.MissingAuthorities.Should().Contain(
                "fullTwoByTwoSkyResponseCalibration");
            UpasSupervisorCoarsePlanningEvidenceParser.Digest(ReadinessDenial())
                .Should().Be(DenialFixtureSha256);
            handler.RequestCount.Should().Be(1);
        }

        [TestCase("\"ready\":false", "\"ready\":\"false\"")]
        [TestCase("\"clientRequestNonce\":\"" + Nonce,
            "\"clientRequestNonce\":\"" + EvidenceId)]
        [TestCase("\"ready\":false", "\"unexpected\":0,\"ready\":false")]
        public async Task MalformedReadinessDenialFailsClosed(
                string original,
                string replacement) {
            var handler = new EvidenceHandler(
                ReadinessDenial().Replace(original, replacement, StringComparison.Ordinal)) {
                StatusCode = HttpStatusCode.Conflict
            };

            Func<Task> get = async () => await Source(handler).GetAsync(
                null, 35.0, "hae29c-ec-full-rig-v1", CancellationToken.None);

            await get.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*malformed*readiness denial*");
        }

        [Test]
        public void EvidenceSourceCannotReachAMotionSurface() {
            var fields = typeof(HttpsUpasSupervisorCoarseEvidenceSource)
                .GetFields(System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Public);

            fields.Should().NotContain(field =>
                typeof(IAutomatedMoveExecutor).IsAssignableFrom(field.FieldType)
                || typeof(IPolarAlignmentSystemVM).IsAssignableFrom(field.FieldType));
        }

        private static HttpsUpasSupervisorCoarseEvidenceSource Source(
                EvidenceHandler handler,
                string token = "secret") => new(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            "https://127.0.0.1:8443/base",
            () => token,
            TimeSpan.FromSeconds(2),
            () => Nonce);

        private static string ValidResponse() =>
            UpasSupervisorCoarsePlanningEvidenceTest.ValidJson()
                .Replace("\"serverProcessingMilliseconds\": 25.0",
                    "\"serverProcessingMilliseconds\": 0.0",
                    StringComparison.Ordinal);

        private static string ReadinessDenial() =>
            File.ReadAllText(
                Path.Combine(
                    TestContext.CurrentContext.TestDirectory,
                    "CoarseEvidenceFixtures",
                    "readiness-denial-v2.json"),
                new UTF8Encoding(false, true));

        private sealed class EvidenceHandler : HttpMessageHandler {
            private readonly string body;

            public EvidenceHandler(string body) => this.body = body;

            public int RequestCount { get; private set; }
            public Uri? LastUri { get; private set; }
            public HttpMethod? LastMethod { get; private set; }
            public string? LastAuthorization { get; private set; }
            public Uri? FinalUri { get; init; }
            public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;
            public string? ContentDigestOverride { get; init; }
            public string? EvidenceIdOverride { get; init; }
            public bool OmitEvidenceId { get; init; }
            public bool DuplicateContentDigest { get; init; }

            protected override Task<HttpResponseMessage> SendAsync(
                    HttpRequestMessage request,
                    CancellationToken cancellationToken) {
                RequestCount++;
                LastUri = request.RequestUri;
                LastMethod = request.Method;
                LastAuthorization = request.Headers.Authorization?.ToString();
                var response = new HttpResponseMessage(StatusCode) {
                    RequestMessage = FinalUri == null
                        ? request
                        : new HttpRequestMessage(HttpMethod.Get, FinalUri),
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                if (StatusCode == HttpStatusCode.OK) {
                    var digest = ContentDigestOverride
                        ?? UpasSupervisorCoarsePlanningEvidenceParser.Digest(body);
                    response.Headers.TryAddWithoutValidation(
                        HttpsUpasSupervisorCoarseEvidenceSource.ContentSha256Header,
                        DuplicateContentDigest ? new[] { digest, digest } : new[] { digest });
                    if (!OmitEvidenceId) {
                        response.Headers.TryAddWithoutValidation(
                            HttpsUpasSupervisorCoarseEvidenceSource.EvidenceIdHeader,
                            EvidenceIdOverride ?? EvidenceId);
                    }
                }
                return Task.FromResult(response);
            }
        }
    }
}
