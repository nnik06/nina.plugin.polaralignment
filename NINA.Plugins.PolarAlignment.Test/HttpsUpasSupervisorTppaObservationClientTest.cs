using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Newtonsoft.Json.Linq;

namespace NINA.Plugins.PolarAlignment.Test {
    public class HttpsUpasSupervisorTppaObservationClientTest {
        private static readonly Guid CampaignId = Guid.Parse("50000000-0000-4000-8000-000000000001");
        private const string LeaseId = "60000000-0000-4000-8000-000000000001";
        private const string BootId = "70000000-0000-4000-8000-000000000001";
        private static readonly string Nonce = new('a', 64);
        private static readonly string Capture = new('b', 64);
        private static readonly string Attestation = new('c', 64);

        [Test]
        public async Task OpensAndClosesExactNonceBoundObservation() {
            var handler = new RecordingHandler();
            using var http = new HttpClient(handler);
            var client = new HttpsUpasSupervisorTppaObservationClient(
                http, "https://supervisor.test/", () => "token");

            var lease = await client.OpenAsync(
                CampaignId, TimeSpan.FromSeconds(75), CancellationToken.None);
            var sealedObservation = await client.CloseAsync(
                lease, Capture, CancellationToken.None);

            lease.Nonce.Should().Be(Nonce);
            sealedObservation.AttestationSha256.Should().Be(Attestation);
            handler.Requests.Should().HaveCount(2);
            handler.Requests[0].Path.Should().Be("/v1/coarse/tppa-observations");
            handler.Requests[0].Body["durationMilliseconds"]!.Value<long>().Should().Be(75000);
            handler.Requests[1].Path.Should().Be("/v1/coarse/tppa-observations/close");
            handler.Requests[1].Body["nonce"]!.Value<string>().Should().Be(Nonce);
            handler.Requests[1].Body["captureDigestSha256"]!.Value<string>().Should().Be(Capture);
            handler.Requests.Should().OnlyContain(item => item.Authorization == "Bearer token");
        }

        [Test]
        public async Task AbortsExactNonceBoundObservation() {
            var handler = new RecordingHandler();
            using var http = new HttpClient(handler);
            var client = new HttpsUpasSupervisorTppaObservationClient(
                http, "https://supervisor.test/", () => "token");
            var lease = await client.OpenAsync(
                CampaignId, TimeSpan.FromSeconds(75), CancellationToken.None);

            await client.AbortAsync(lease, CancellationToken.None);

            handler.Requests.Should().HaveCount(2);
            handler.Requests[1].Path.Should().Be("/v1/coarse/tppa-observations/abort");
            handler.Requests[1].Body["leaseId"]!.Value<string>().Should().Be(LeaseId);
            handler.Requests[1].Body["nonce"]!.Value<string>().Should().Be(Nonce);
            handler.Requests[1].Body["requestBodySha256"]!.Value<string>()
                .Should().MatchRegex("^[0-9a-f]{64}$")
                .And.NotBe(new string('0', 64));
            handler.Requests.Should().OnlyContain(item => item.Authorization == "Bearer token");
        }
        [Test]
        public async Task RejectsAttestationThatDoesNotMatchOpenedLease() {
            var handler = new RecordingHandler(closeMotionCounter: 8);
            using var http = new HttpClient(handler);
            var client = new HttpsUpasSupervisorTppaObservationClient(
                http, "https://supervisor.test/", () => "token");
            var lease = await client.OpenAsync(
                CampaignId, TimeSpan.FromSeconds(75), CancellationToken.None);

            var action = () => client.CloseAsync(lease, Capture, CancellationToken.None);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*does not match*");
        }

        [Test]
        public async Task FailsClosedWhenSupervisorRejectsOpen() {
            var handler = new RecordingHandler(openStatus: HttpStatusCode.Conflict);
            using var http = new HttpClient(handler);
            var client = new HttpsUpasSupervisorTppaObservationClient(
                http, "https://supervisor.test/", () => "token");

            var action = () => client.OpenAsync(
                CampaignId, TimeSpan.FromSeconds(75), CancellationToken.None);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*rejected the request (409)*");
        }

        private sealed record CapturedRequest(string Path, string Authorization, JObject Body);

        private sealed class RecordingHandler : HttpMessageHandler {
            private readonly long closeMotionCounter;
            private readonly HttpStatusCode openStatus;
            public RecordingHandler(long closeMotionCounter = 7, HttpStatusCode openStatus = HttpStatusCode.Created) {
                this.closeMotionCounter = closeMotionCounter;
                this.openStatus = openStatus;
            }
            public List<CapturedRequest> Requests { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(
                    HttpRequestMessage request, CancellationToken cancellationToken) {
                var text = await request.Content!.ReadAsStringAsync(cancellationToken);
                Requests.Add(new(
                    request.RequestUri!.AbsolutePath,
                    request.Headers.Authorization?.ToString(),
                    JObject.Parse(text)));
                if (request.RequestUri.AbsolutePath.EndsWith("/abort", StringComparison.Ordinal)) {
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }                if (request.RequestUri.AbsolutePath.EndsWith("/close", StringComparison.Ordinal)) {
                    return Json(HttpStatusCode.OK, new JObject {
                        ["schemaVersion"] = 1, ["leaseId"] = LeaseId, ["nonce"] = Nonce,
                        ["campaignId"] = CampaignId.ToString("D"), ["bootId"] = BootId,
                        ["motionEpoch"] = 4, ["motionCounter"] = closeMotionCounter,
                        ["openedMonotonicNs"] = 1000, ["closedMonotonicNs"] = 2000,
                        ["captureDigestSha256"] = Capture, ["attestationSha256"] = Attestation
                    });
                }
                if (openStatus != HttpStatusCode.Created) {
                    return Json(openStatus, new JObject { ["detail"] = "rejected" });
                }
                return Json(HttpStatusCode.Created, new JObject {
                    ["schemaVersion"] = 1, ["leaseId"] = LeaseId, ["nonce"] = Nonce,
                    ["campaignId"] = CampaignId.ToString("D"), ["bootId"] = BootId,
                    ["motionEpoch"] = 4, ["motionCounter"] = 7,
                    ["openedMonotonicNs"] = 1000, ["expiresMonotonicNs"] = 76000
                });
            }

            private static HttpResponseMessage Json(HttpStatusCode status, JObject body) => new(status) {
                Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json")
            };
        }
    }
}
