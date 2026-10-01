using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using NINA.Plugins.PolarAlignment.Qualification;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test;
[TestFixture]
public class SupervisorSkyRouteTest {
    private sealed class Handler : HttpMessageHandler {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
            Calls++;
            Assert.That(request.RequestUri!.AbsolutePath,Is.EqualTo("/v1/status"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                "{\"serviceVersion\":\"v1\",\"contractVersion\":\"1.1\",\"canMove\":false,\"reasonIfNot\":\"REFERENCE_UNQUALIFIED\",\"axes\":{\"AZ\":{},\"ALT\":{}},\"activeFaults\":[]}",Encoding.UTF8,"application/json") });
        }
    }
    [TestCase(null,false)] [TestCase("0",false)] [TestCase("1",true)]
    public void SelectionIsExplicitAndDefaultOff(string? value,bool expected) =>
        Assert.That(SupervisorSkyRoute.Requested(_=>value!),Is.EqualTo(expected));
    [Test]
    public void AmbiguousSelectionCannotSelectLegacyFallback() =>
        Assert.Throws<ArgumentException>(()=>SupervisorSkyRoute.Requested(_=>"true"));
    [Test]
    public void MissingProductionConfigurationRefusedBeforeTransport() =>
        Assert.Throws<ArgumentException>(()=>SupervisorSkyRoute.FromEnvironment(name=>name=="UPAS_CONTRACT_A_ROUTE"?"1":null!));
    [Test]
    public void InhibitedRouteRefusesBeforeMeasurementLeaseOrController() {
        using var handler=new Handler();
        var http=new HttpClient(handler){BaseAddress=new Uri("http://127.0.0.1:8899/")};
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer","test-only");
        using var route=new SupervisorSkyRoute(http,"tppa");
        var error=Assert.ThrowsAsync<InvalidOperationException>(async()=>await route.RequireReadyBeforeMeasurementAsync(CancellationToken.None));
        Assert.That(error!.Message,Does.Contain("REFERENCE_UNQUALIFIED"));
        Assert.That(handler.Calls,Is.EqualTo(1));
    }
}
