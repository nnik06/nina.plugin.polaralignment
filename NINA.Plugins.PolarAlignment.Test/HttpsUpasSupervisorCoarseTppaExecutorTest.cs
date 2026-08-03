using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Newtonsoft.Json.Linq;

namespace NINA.Plugins.PolarAlignment.Test {
    public class HttpsUpasSupervisorCoarseTppaExecutorTest {
        private const string LeaseId = "20000000-0000-4000-8000-000000000002";
        private const string TransactionId = "30000000-0000-4000-8000-000000000001";
        private static readonly string CovarianceAuthoritySha256 = new('c', 64);
        private static readonly DateTime RequestUtc =
            new(2026, 8, 3, 16, 0, 0, DateTimeKind.Utc);

        [Test]
        public void NestedDigestMatchesSupervisorPythonRfc8785Vector() {
            var payload = RequestPayload();

            HttpsUpasSupervisorCoarseTppaExecutor
                .ComputeRequestBodySha256(payload)
                .Should().Be("4b069c21bf0f7731197dc08ff432de90bdd2bd635f9c8491866b93f67a4334e2");
        }

        [Test]
        public async Task AcquiresLeaseExecutesSealedCoarseTransactionAndReleasesLease() {
            var handler = new RecordingHandler();
            using var client = new HttpClient(handler);
            var executor = new HttpsUpasSupervisorCoarseTppaExecutor(
                client,
                "https://supervisor.test/",
                () => "token",
                CovarianceAuthoritySha256,
                idempotencyKeyProvider: () => "fixed-key",
                utcNowProvider: () => RequestUtc);

            var result = await executor.ExecuteAsync(
                Determination(1, 'a', RequestUtc.AddSeconds(-30), 120, 60),
                Determination(2, 'd', RequestUtc.AddSeconds(-1), 121.2, 59.4),
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
                .Should().Be("4b069c21bf0f7731197dc08ff432de90bdd2bd635f9c8491866b93f67a4334e2");
            handler.Requests[1].Body["covarianceAuthoritySha256"]!.Value<string>()
                .Should().Be(CovarianceAuthoritySha256);
            var determinations = (JArray)handler.Requests[1].Body["determinations"]!;
            var firstReceipt = (JObject)determinations[0]!["receipt"]!;
            firstReceipt["azimuthErrorMicrodegrees"]!.Value<long>()
                .Should().Be(2_000_000);
            firstReceipt["covarianceAzAzSquareMicrodegrees"]!.Value<long>()
                .Should().Be(100_000_000);
            firstReceipt["sourceSolves"]!.Count().Should().Be(3);
            firstReceipt["receiptSha256"]!.Value<string>()
                .Should().Be("96f9c304e4e9c7205ed3fd6755ff3c65125353acda88d0ab6348ebff074577cc");
            determinations[1]!["receipt"]!["receiptSha256"]!.Value<string>()
                .Should().Be("60616c8cf1d60aa637a427369a8833c8fdc0f8e21e1203ec27f95b0779120363");
            handler.Requests[2].Method.Should().Be(HttpMethod.Delete);
            handler.Requests.Should().OnlyContain(
                request => request.Authorization == "Bearer token");
        }

        [Test]
        public async Task RejectsOverlappingDeterminationsBeforeLeaseOrMotion() {
            var handler = new RecordingHandler();
            using var client = new HttpClient(handler);
            var executor = new HttpsUpasSupervisorCoarseTppaExecutor(
                client,
                "https://supervisor.test/",
                () => "token",
                CovarianceAuthoritySha256,
                utcNowProvider: () => RequestUtc);

            var action = () => executor.ExecuteAsync(
                Determination(1, 'a', RequestUtc.AddSeconds(-10), 120, 60),
                Determination(2, 'd', RequestUtc.AddSeconds(-1), 121.2, 59.4),
                35.0, 8.8, "hae29c-ec-full-rig-v1", CancellationToken.None);

            await action.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*intervals overlap*");
            handler.Requests.Should().BeEmpty();
        }

        [Test]
        public async Task RejectsReusedSourceFramesBeforeLeaseOrMotion() {
            var handler = new RecordingHandler();
            using var client = new HttpClient(handler);
            var executor = new HttpsUpasSupervisorCoarseTppaExecutor(
                client,
                "https://supervisor.test/",
                () => "token",
                CovarianceAuthoritySha256,
                utcNowProvider: () => RequestUtc);
            var first = Determination(1, 'a', RequestUtc.AddSeconds(-30), 120, 60);
            var second = Determination(2, 'd', RequestUtc.AddSeconds(-1), 121.2, 59.4)
                with { SourceSolves = first.SourceSolves };

            var action = () => executor.ExecuteAsync(
                first, second, 35.0, 8.8, "hae29c-ec-full-rig-v1",
                CancellationToken.None);

            await action.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*reuse source images or solver outputs*");
            handler.Requests.Should().BeEmpty();
        }

        [Test]
        public async Task RejectsStaleDeterminationBeforeLeaseOrMotion() {
            var handler = new RecordingHandler();
            using var client = new HttpClient(handler);
            var executor = new HttpsUpasSupervisorCoarseTppaExecutor(
                client,
                "https://supervisor.test/",
                () => "token",
                CovarianceAuthoritySha256,
                utcNowProvider: () => RequestUtc);

            var action = () => executor.ExecuteAsync(
                Determination(1, 'a', RequestUtc.AddMinutes(-4), 120, 60),
                Determination(2, 'd', RequestUtc.AddSeconds(-1), 121.2, 59.4),
                35.0, 8.8, "hae29c-ec-full-rig-v1", CancellationToken.None);

            await action.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*stale or mistimed*");

            handler.Requests.Should().BeEmpty();

        }

        private static TppaCoarseDeterminationEvidence Determination(
                int suffix,
                char sourceBase,
                DateTime completedUtc,
                double azimuthMinutes,
                double altitudeMinutes) {
            var startedUtc = completedUtc.AddSeconds(-20);
            var sources = Enumerable.Range(0, 3).Select(index => {
                var exposureStarted = startedUtc.AddSeconds(2 + index * 5);
                var digestCharacter = (char)(sourceBase + index);
                return new TppaCoarseSourceSolveEvidence(
                    Guid.Parse($"40000000-0000-4000-8000-{suffix * 10 + index:D12}"),
                    new string(digestCharacter, 64),
                    new string((char)('1' + (suffix - 1) * 3 + index), 64),
                    exposureStarted,
                    2000,
                    exposureStarted.AddSeconds(1),
                    startedUtc.AddSeconds(1),
                    true,
                    false,
                    100.0 + suffix + index / 10.0,
                    20.0 + suffix + index / 10.0,
                    "pierEast");
            }).ToArray();
            return new(
                Guid.Parse($"10000000-0000-4000-8000-{suffix:D12}"),
                startedUtc,
                completedUtc,
                "safe-arc-a",
                true,
                true,
                new string('c', 40),
                new string('d', 64),
                "hae29c-ec",
                new string('e', 64),
                "astap-2026.1",
                "d50-v17",
                sources,
                azimuthMinutes,
                altitudeMinutes,
                0.36,
                0.0,
                0.36);
        }

        private static JObject RequestPayload() => new() {
            ["schemaVersion"] = 1,
            ["clientId"] = "nina-tppa",
            ["leaseId"] = LeaseId,
            ["idempotencyKey"] = "fixed-key",
            ["currentTemperatureMilliCelsius"] = 35000,
            ["currentLoadProfileId"] = "hae29c-ec-full-rig-v1",
            ["covarianceAuthoritySha256"] = CovarianceAuthoritySha256,
            ["determinations"] = new JArray {
                new JObject {
                    ["receipt"] = TppaCoarseDeterminationReceiptBuilder.Build(
                        Determination(1, 'a', RequestUtc.AddSeconds(-30), 120, 60),
                        RequestUtc)
                },
                new JObject {
                    ["receipt"] = TppaCoarseDeterminationReceiptBuilder.Build(
                        Determination(2, 'd', RequestUtc.AddSeconds(-1), 121.2, 59.4),
                        RequestUtc)
                }
            }
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
