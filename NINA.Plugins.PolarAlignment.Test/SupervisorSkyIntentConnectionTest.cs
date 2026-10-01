using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NINA.Plugins.PolarAlignment.Qualification;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test;

[TestFixture]
public class SupervisorSkyIntentConnectionTest {
    private sealed class StatusHandler : HttpMessageHandler {
        public int Calls;
        public string Version="1.1";
        public bool Bad;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
            Calls++;await Task.Delay(5,token);
            if(request.RequestUri!.AbsolutePath!="/v1/status")throw new Exception("Inhibited connection attempted ownership or motion.");
            var body=Bad ? "{}" : JsonSerializer.Serialize(new {serviceVersion="production-inhibited-v1",contractVersion=Version,
                canMove=false,reasonIfNot="PRODUCTION_BINDING_NOT_FIELD_QUALIFIED",axes=new {AZ=new {health="UNKNOWN"},ALT=new {health="UNKNOWN"}},activeFaults=Array.Empty<string>()});
            return new(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")};
        }
    }
    private static HttpClient Http(StatusHandler handler,string endpoint="http://127.0.0.1:8899") {
        var http=new HttpClient(handler){BaseAddress=new Uri(endpoint)};
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer","test-only-token");
        return http;
    }
    [Test]
    public async Task InhibitionProducesRefusedBeforeAnyLeaseAndDoesNotVerifySky() {
        using var handler=new StatusHandler();using var http=Http(handler);
        var connection=new SupervisorSkyIntentConnection(http,"tppa");
        var adapter=UpasSkyJobIntegrationAdapter.ForProductionConnection(connection);
        var measurement=new UpasSkyMeasurement("m","s","2026-10-01T04:30:00Z",UpasSkyMeasurement.Convention,"NORTH",18,-18);
        var pending=adapter.ExecuteAsync(measurement,"stable",true,CancellationToken.None);
        Assert.That(pending.IsCompleted,Is.False);
        var result=await pending;
        Assert.Multiple(()=>{Assert.That(result.State,Is.EqualTo("REFUSED"));Assert.That(result.JobId,Is.Null);
            Assert.That(result.MechanicalExecutionComplete,Is.False);Assert.That(result.SkyAlignmentVerified,Is.False);Assert.That(handler.Calls,Is.EqualTo(1));});
    }
    [TestCase("http://remote.invalid")] [TestCase("https://remote.invalid/other/")] [TestCase("https://user:pass@remote.invalid/")]
    public void UnsafeOrAmbiguousOriginRejectedBeforeTransport(string endpoint) {
        using var handler=new StatusHandler();using var http=Http(handler,endpoint);
        Assert.Throws<ArgumentException>(()=>new SupervisorSkyIntentConnection(http,"tppa"));Assert.That(handler.Calls,Is.Zero);
    }
    [Test]
    public void MissingBearerIdentityRejectedBeforeTransport() {
        using var handler=new StatusHandler();using var http=Http(handler);http.DefaultRequestHeaders.Authorization=null;
        Assert.Throws<ArgumentException>(()=>new SupervisorSkyIntentConnection(http,"tppa"));Assert.That(handler.Calls,Is.Zero);
    }
    [TestCase(false)] [TestCase(true)]
    public void IncompatibleStatusNeverFallsBackToRawMoves(bool bad) {
        using var handler=new StatusHandler{Version="old",Bad=bad};using var http=Http(handler);
        var connection=new SupervisorSkyIntentConnection(http,"tppa");
        Assert.ThrowsAsync<InvalidDataException>(async()=>await connection.CheckAsync());Assert.That(handler.Calls,Is.EqualTo(1));
    }
}


