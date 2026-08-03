using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Newtonsoft.Json.Linq;

namespace NINA.Plugins.PolarAlignment.Test {
    public class HttpsUpasSupervisorCoarseTppaExecutorTest {
        private const string LeaseId = "20000000-0000-4000-8000-000000000002";
        private const string TransactionId = "30000000-0000-4000-8000-000000000001";
        private static readonly DateTime RequestUtc =
            new(2026, 8, 3, 16, 0, 0, DateTimeKind.Utc);

        [Test]
        public void NestedDigestMatchesSupervisorPythonRfc8785Vector() {
            var payload = RequestPayload();

            HttpsUpasSupervisorCoarseTppaExecutor
                .ComputeRequestBodySha256(payload)
                .Should().Be("ec441a2c1ccc8553c773e6cd9e0a9fc09d9a0b4ccf7b4f47975d07ba71bbdf5b");
        }

        [Test]
        public async Task AcquiresLeaseExecutesSealedCoarseTransactionAndReleasesLease() {
            var handler = new RecordingHandler();
            using var client = new HttpClient(handler);
            var executor = new HttpsUpasSupervisorCoarseTppaExecutor(
                client,
                "https://supervisor.test/",
                () => "token",
                idempotencyKeyProvider: () => "fixed-key",
                utcNowProvider: () => RequestUtc);

            var result = await executor.ExecuteAsync(
                Determination(1, 'a', RequestUtc.AddSeconds(-2), 120, 60),
                Determination(2, 'b', RequestUtc.AddSeconds(-1), 121.2, 59.4),
                35.0,
                8.8,
                "hae29c-ec-full-rig-v1",
                CancellationToken.None);

            result.Completed.Should().BeTrue();
            result.MoveWasRequired.Should().BeTrue();
            result.TransactionId.Should().Be(TransactionId);
            handler.Requests.Should().HaveCount(3);
            handler.Requests[0].Body["maximumTravelDegrees"]!.Value<double>()
                .Should().Be(8.8);
            handler.Requests[1].Uri.AbsolutePath.Should().Be("/v1/coarse/tppa-correction");
            handler.Requests[1].Body["requestBodySha256"]!.Value<string>()
                .Should().Be("ec441a2c1ccc8553c773e6cd9e0a9fc09d9a0b4ccf7b4f47975d07ba71bbdf5b");
            var determinations = (JArray)handler.Requests[1].Body["determinations"]!;
            determinations[0]!["ageAtRequestMilliseconds"]!.Value<long>().Should().Be(2000);
            determinations[0]!["azimuthErrorMicrodegrees"]!.Value<long>()
                .Should().Be(2_000_000);
            determinations[0]!["covarianceAzAzSquareMicrodegrees"]!.Value<long>()
                .Should().Be(100_000_000);
            handler.Requests[2].Method.Should().Be(HttpMethod.Delete);
            handler.Requests.Should().OnlyContain(
                request => request.Authorization == "Bearer token");
        }

        [Test]
        public async Task RejectsStaleDeterminationBeforeLeaseOrMotion() {
            var handler = new RecordingHandler();
            using var client = new HttpClient(handler);
            var executor = new HttpsUpasSupervisorCoarseTppaExecutor(
                client,
                "https://supervisor.test/",
                () => "token",
                utcNowProvider: () => RequestUtc);

            var action = () => executor.ExecuteAsync(
                Determination(1, 'a', RequestUtc.AddMinutes(-4), 120, 60),
                Determination(2, 'b', RequestUtc.AddSeconds(-1), 121.2, 59.4),
                35.0, 8.8, "hae29c-ec-full-rig-v1", CancellationToken.None);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*future-dated or stale*");

            handler.Requests.Should().BeEmpty();

        }

        private static TppaCoarseDeterminationEvidence Determination(
                int suffix,
                char receiptCharacter,
                DateTime capturedUtc,
                double azimuthMinutes,
                double altitudeMinutes) => new(
            Guid.Parse($"10000000-0000-4000-8000-{suffix:D12}"),
            new string(receiptCharacter, 64),
            capturedUtc,
            "safe-arc-a",
            true,
            true,
            azimuthMinutes,
            altitudeMinutes,
            0.36,
            0.0,
            0.36);

        private static JObject RequestPayload() => new() {
            ["schemaVersion"] = 1,
            ["clientId"] = "nina-tppa",
            ["leaseId"] = LeaseId,
            ["idempotencyKey"] = "fixed-key",
            ["currentTemperatureMilliCelsius"] = 35000,
            ["currentLoadProfileId"] = "hae29c-ec-full-rig-v1",
            ["determinations"] = new JArray {
                WireDetermination(1, 'a', 2000, 2_000_000, 1_000_000),
                WireDetermination(2, 'b', 1000, 2_020_000, 990_000)
            }
        };

        private static JObject WireDetermination(
                int suffix,
                char receiptCharacter,
                int ageMilliseconds,
                int azimuthMicrodegrees,
                int altitudeMicrodegrees) => new() {
            ["determinationId"] = $"10000000-0000-4000-8000-{suffix:D12}",
            ["receiptSha256"] = new string(receiptCharacter, 64),
            ["ageAtRequestMilliseconds"] = ageMilliseconds,
            ["targetSkyArcId"] = "safe-arc-a",
            ["truePoleRefractionEnabled"] = true,
            ["stationary"] = true,
            ["azimuthErrorMicrodegrees"] = azimuthMicrodegrees,
            ["altitudeErrorMicrodegrees"] = altitudeMicrodegrees,
            ["covarianceAzAzSquareMicrodegrees"] = 100_000_000,
            ["covarianceAzAltSquareMicrodegrees"] = 0,
            ["covarianceAltAltSquareMicrodegrees"] = 100_000_000
        };

        private sealed record CapturedRequest(
            HttpMethod Method,
            Uri Uri,
            string Authorization,
            JObject Body);

        private sealed class RecordingHandler : HttpMessageHandler {
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
                        ["state"] = "COMPLETED",
                        ["replayed"] = false,
                        ["planKind"] = "coarseCorrection",
                        ["moveWasRequired"] = true
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
