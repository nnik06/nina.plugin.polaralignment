using FluentAssertions;
using Newtonsoft.Json.Linq;
using NINA.Plugins.PolarAlignment;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class HttpsUpasSupervisorRawRelativeClientTest {
        private const string LeaseId = "20000000-0000-4000-8000-000000000002";
        private const string TransactionId = "30000000-0000-4000-8000-000000000001";

        [Test]
        public void RawRelativeDigestMatchesSupervisorPythonRfc8785Vector() {
            var payload = new JObject {
                ["schemaVersion"] = 1,
                ["clientId"] = "nina-tppa",
                ["leaseId"] = LeaseId,
                ["idempotencyKey"] = "fixed-key",
                ["measurementSessionId"] = "tppa-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                ["measurementId"] = "move-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                ["axis"] = "az",
                ["rawCount"] = -17,
                ["clientMaximumRawUnits"] = 17,
                ["coordinateConvention"] = "grblRawRelative_v1",
                ["reason"] = "TPPA bounded sky-response command"
            };

            HttpsUpasSupervisorCoarseTppaExecutor.ComputeRequestBodySha256(payload)
                .Should().Be("610a5d30683fcdfacee75333fcbe9f90664e9083adbe3b25d25bc19e4967c377");
        }

        [Test]
        public async Task SendsOneSignedRawCommandWithoutPhysicalTravelFields() {
            var handler = new RecordingHandler();
            using var http = new HttpClient(handler);
            var client = new HttpsUpasSupervisorRawRelativeClient(
                http, "https://supervisor.test/base/", () => "token",
                TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(1), () => "fixed-key");

            var result = await client.ExecuteAzimuthAsync(-17, CancellationToken.None);

            result.PhysicalMotionVerified.Should().BeTrue(result.Reason);
            handler.Requests.Should().HaveCount(4);
            handler.Requests[0].Uri.Should().Be(new Uri("https://supervisor.test/base/v1/raw-relative-leases"));
            handler.Requests[0].Body["maximumRawUnits"]!.Value<int>().Should().Be(17);
            handler.Requests[0].Body["maximumTravelDegrees"].Should().BeNull();
            handler.Requests[1].Uri.Should().Be(new Uri("https://supervisor.test/base/v1/raw-relative-corrections"));
            handler.Requests[1].Body["rawCount"]!.Value<int>().Should().Be(-17);
            handler.Requests[1].Body["clientMaximumRawUnits"]!.Value<int>().Should().Be(17);
            handler.Requests[1].Body["requestedDeltaDegrees"].Should().BeNull();
            handler.Requests[1].Body["coordinateConvention"]!.Value<string>()
                .Should().Be("grblRawRelative_v1");
            handler.Requests[3].Method.Should().Be(HttpMethod.Delete);
        }

        [Test]
        public async Task RejectsZeroBeforeAnyRequest() {
            var handler = new RecordingHandler();
            using var http = new HttpClient(handler);
            var client = new HttpsUpasSupervisorRawRelativeClient(
                http, "https://supervisor.test/", () => "token");

            var result = await client.ExecuteAzimuthAsync(0, CancellationToken.None);

            result.PhysicalMotionVerified.Should().BeFalse();
            handler.Requests.Should().BeEmpty();
        }

        private sealed record CapturedRequest(HttpMethod Method, Uri Uri, JObject Body);

        private sealed class RecordingHandler : HttpMessageHandler {
            public List<CapturedRequest> Requests { get; } = new();

            protected override Task<HttpResponseMessage> SendAsync(
                    HttpRequestMessage request, CancellationToken cancellationToken) {
                var body = request.Content == null
                    ? new JObject()
                    : JObject.Parse(request.Content.ReadAsStringAsync(cancellationToken).Result);
                Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, body));
                var response = request.Method == HttpMethod.Delete
                    ? new HttpResponseMessage(HttpStatusCode.NoContent)
                    : new HttpResponseMessage(request.RequestUri!.AbsolutePath.EndsWith("raw-relative-leases")
                        ? HttpStatusCode.Created : HttpStatusCode.Accepted);
                if (request.Method == HttpMethod.Post
                        && request.RequestUri!.AbsolutePath.EndsWith("raw-relative-leases")) {
                    response.Content = Json(new JObject {
                        ["leaseId"] = LeaseId,
                        ["clientId"] = "nina-tppa"
                    });
                } else if (request.Method == HttpMethod.Post) {
                    response.Content = Json(new JObject {
                        ["transactionId"] = TransactionId
                    });
                } else if (request.Method == HttpMethod.Get) {
                    response = new HttpResponseMessage(HttpStatusCode.OK) {
                        Content = Json(new JObject {
                            ["state"] = "VERIFIED",
                            ["plan"] = new JObject { ["rawUnits"] = 17 }
                        })
                    };
                }
                response.RequestMessage = request;
                return Task.FromResult(response);
            }

            private static StringContent Json(JObject value) =>
                new(value.ToString(), Encoding.UTF8, "application/json");
        }
    }
}
