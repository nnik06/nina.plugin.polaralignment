using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Newtonsoft.Json.Linq;

namespace NINA.Plugins.PolarAlignment.Test {
    public class HttpsUpasSupervisorPhysicalZeroReturnExecutorTest {
        private const string LeaseId = "20000000-0000-4000-8000-000000000002";
        private const string TransactionId = "20000000-0000-4000-8000-000000000001";

        [Test]
        public void DigestMatchesSupervisorPythonRfc8785Vector() {
            var payload = new JObject {
                ["schemaVersion"] = 1,
                ["clientId"] = "nina-tppa",
                ["leaseId"] = LeaseId,
                ["idempotencyKey"] = "fixed-key",
                ["preEvidenceId"] = new string('b', 64),
                ["currentTemperatureMilliCelsius"] = 35000,
                ["currentLoadProfileId"] = "hae29c-ec-full-rig-v1",
                ["zeroReferenceId"] = "upas-physical-zero-v1"
            };

            HttpsUpasSupervisorPhysicalZeroReturnExecutor
                .ComputeRequestBodySha256(payload)
                .Should().Be("3f76066ab4fa85cfbc6e491ee250a9e6676deb61e4bb643136860a5bf90af625");
        }

        [Test]
        public async Task AcquiresProportionalLeaseExecutesTerminalZeroAndReleasesLease() {
            var handler = new RecordingHandler();
            using var client = new HttpClient(handler);
            var executor = new HttpsUpasSupervisorPhysicalZeroReturnExecutor(
                client,
                "https://supervisor.test/",
                () => "token",
                idempotencyKeyProvider: () => "fixed-key");

            var result = await executor.ReturnAsync(
                new string('b', 64),
                35.0,
                8.8,
                "hae29c-ec-full-rig-v1",
                CancellationToken.None);

            result.IsCompleted.Should().BeTrue();
            result.TransactionId.Should().Be(TransactionId);
            handler.Requests.Should().HaveCount(3);
            handler.Requests[0].Method.Should().Be(HttpMethod.Post);
            handler.Requests[0].Uri.AbsolutePath.Should().Be("/v1/leases");
            handler.Requests[0].Body["maximumTravelDegrees"]!.Value<double>()
                .Should().Be(8.8);
            handler.Requests[1].Uri.AbsolutePath.Should().Be("/v1/coarse/physical-zero");
            handler.Requests[1].Body["currentTemperatureMilliCelsius"]!.Value<int>()
                .Should().Be(35000);
            handler.Requests[1].Body["requestBodySha256"]!.Value<string>()
                .Should().Be("3f76066ab4fa85cfbc6e491ee250a9e6676deb61e4bb643136860a5bf90af625");
            handler.Requests[2].Method.Should().Be(HttpMethod.Delete);
            handler.Requests[2].Uri.AbsolutePath.Should().Be("/v1/leases/" + LeaseId);
            handler.Requests.Should().OnlyContain(
                request => request.Authorization == "Bearer token");
        }

        [Test]
        public async Task RejectsNonTerminalPhysicalZeroResponseAndStillReleasesLease() {
            var handler = new RecordingHandler(terminalState: "postWitnessCaptured");
            using var client = new HttpClient(handler);
            var executor = new HttpsUpasSupervisorPhysicalZeroReturnExecutor(
                client,
                "https://supervisor.test/",
                () => "token",
                idempotencyKeyProvider: () => "fixed-key");

            var action = () => executor.ReturnAsync(
                new string('b', 64), 35.0, 0.5,
                "hae29c-ec-full-rig-v1", CancellationToken.None);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*not a valid completed transaction*");
            handler.Requests.Last().Method.Should().Be(HttpMethod.Delete);
        }

        private sealed record CapturedRequest(
            HttpMethod Method,
            Uri Uri,
            string Authorization,
            JObject Body);

        private sealed class RecordingHandler : HttpMessageHandler {
            private readonly string terminalState;
            public RecordingHandler(string terminalState = "completed") =>
                this.terminalState = terminalState;
            public List<CapturedRequest> Requests { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(
                    HttpRequestMessage request, CancellationToken cancellationToken) {
                var text = request.Content == null
                    ? "{}"
                    : await request.Content.ReadAsStringAsync(cancellationToken);
                Requests.Add(new(
                    request.Method,
                    request.RequestUri!,
                    request.Headers.Authorization?.ToString(),
                    JObject.Parse(text)));

                if (request.Method == HttpMethod.Delete) {
                    return new HttpResponseMessage(HttpStatusCode.NoContent) {
                        RequestMessage = request
                    };
                }
                var body = request.RequestUri!.AbsolutePath == "/v1/leases"
                    ? new JObject {
                        ["leaseId"] = LeaseId,
                        ["clientId"] = "nina-tppa",
                        ["issuedMonotonicNs"] = 1,
                        ["expiresMonotonicNs"] = 2,
                        ["plannedTravelDegrees"] = 0.0,
                        ["maximumTravelDegrees"] =
                            JObject.Parse(text)["maximumTravelDegrees"]
                    }
                    : new JObject {
                        ["schemaVersion"] = 1,
                        ["transactionId"] = TransactionId,
                        ["state"] = terminalState,
                        ["replayed"] = false,
                        ["planKind"] = "physicalZeroPreflight",
                        ["returnWasRequired"] = true
                    };
                return new HttpResponseMessage(HttpStatusCode.OK) {
                    RequestMessage = request,
                    Content = new StringContent(
                        body.ToString(Newtonsoft.Json.Formatting.None),
                        Encoding.UTF8,
                        "application/json")
                };
            }
        }
    }
}
