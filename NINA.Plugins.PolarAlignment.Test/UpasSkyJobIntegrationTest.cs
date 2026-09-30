using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using NINA.Plugins.PolarAlignment.Qualification;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test;

[TestFixture]
public class UpasSkyJobIntegrationTest {
    private static UpasSkyMeasurement Measurement() => new("m", "session", "2026-10-01T00:00:01Z", UpasSkyMeasurement.Convention, "NORTH", -18, 18);
    private sealed class Handler : HttpMessageHandler {
        public readonly List<(string Path, string Body)> Requests = new();
        public bool Held, LostSubmit, WrongIdentity;
        public int Stops, Polls;
        private bool stopped, lost;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            await Task.Delay(5, token).ConfigureAwait(false);
            var path=request.RequestUri!.AbsolutePath;
            var body=request.Content==null ? "" : await request.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            Requests.Add((path,body));
            string result;
            if (path=="/v1/lease") result="{\"leaseId\":\"lease\",\"expiresAtUtc\":\"2026-10-01T00:02:01Z\"}";
            else if(path=="/v1/sky-corrections") {
                if(LostSubmit && !lost) {lost=true;throw new HttpRequestException("Lost synthetic response.");}
                result="{\"jobId\":\"job\",\"state\":\"ACCEPTED\"}";
            } else if(path=="/v1/stop") {Stops++;stopped=true;result="{\"jobId\":\"job\",\"cancelRequested\":true,\"stopConfirmed\":false}";}
            else if(path=="/v1/lease/release") result="{\"released\":true,\"stopConfirmed\":false}";
            else if(path.StartsWith("/v1/jobs/")) {
                Polls++;
                result=JsonSerializer.Serialize(new {jobId=WrongIdentity ? "foreign" : "job",state=stopped ? "PARTIAL" : Held ? "RUNNING" : "EXECUTED",reason=stopped ? "CLIENT_STOP" : (string?)null});
            } else throw new Exception("Unexpected route "+path);
            return new(HttpStatusCode.OK) {Content=new StringContent(result,Encoding.UTF8,"application/json")};
        }
    }

    [Test]
    public async Task PluginSeamUsesSkyOnlyOriginalExposureAndDoesNotVerifySky() {
        using var handler=new Handler();using var http=new HttpClient(handler) {BaseAddress=new Uri("http://offline.invalid")};
        var adapter=new UpasSkyJobIntegrationAdapter(new(http,"client",pollInterval:TimeSpan.FromMilliseconds(5)));
        var pending=adapter.ExecuteAsync(Measurement(),"request",true,CancellationToken.None);
        Assert.That(pending.IsCompleted,Is.False,"Client must return while I/O is pending.");
        var result=await pending;
        Assert.Multiple(()=>{Assert.That(result.MechanicalExecutionComplete,Is.True);Assert.That(result.SkyAlignmentVerified,Is.False);Assert.That(handler.Stops,Is.Zero);});
        using var payload=JsonDocument.Parse(handler.Requests.Single(x=>x.Path=="/v1/sky-corrections").Body);
        var row=payload.RootElement;
        Assert.That(row.GetProperty("measuredAtUtc").GetString(),Is.EqualTo(Measurement().MeasuredAtUtc));
        Assert.That(row.EnumerateObject().Select(x=>x.Name),Is.EquivalentTo(new[]{"measurementId","measurementSessionId","measuredAtUtc","coordinateConvention","hemisphere","azErrArcmin","altErrArcmin","leaseId","idempotencyKey"}));
        Assert.That(typeof(UpasSkyJobIntegrationAdapter).GetCustomAttributes(false),Is.Empty,"Candidate must not export itself to NINA.");
    }

    [TestCase(false)] [TestCase(true)]
    public async Task TimeoutAndCallerCancellationHaveIndependentStopCleanup(bool cancelCaller) {
        using var handler=new Handler {Held=true};using var http=new HttpClient(handler) {BaseAddress=new Uri("http://offline.invalid")};
        var client=new UpasSkyCorrectionClient(http,"client",timeout:TimeSpan.FromMilliseconds(150),pollInterval:TimeSpan.FromMilliseconds(5));
        using var cts=new CancellationTokenSource();
        var task=client.ExecuteAsync(Measurement(),"request",cts.Token);
        while(handler.Polls==0)await Task.Delay(1);
        if(cancelCaller)cts.Cancel();
        var result=await task;
        Assert.Multiple(()=>{Assert.That(result.State,Is.EqualTo("PARTIAL"));Assert.That(handler.Stops,Is.EqualTo(1));Assert.That(handler.Requests.Last().Path,Is.EqualTo("/v1/lease/release"));});
    }

    [Test]
    public async Task LostSubmitUsesIdenticalKeyBodyOnce() {
        using var handler=new Handler {LostSubmit=true};using var http=new HttpClient(handler) {BaseAddress=new Uri("http://offline.invalid")};
        var result=await new UpasSkyCorrectionClient(http,"client").ExecuteAsync(Measurement(),"stable-request");
        var submissions=handler.Requests.Where(x=>x.Path=="/v1/sky-corrections").ToArray();
        Assert.Multiple(()=>{Assert.That(result.State,Is.EqualTo("EXECUTED"));Assert.That(submissions.Length,Is.EqualTo(2));Assert.That(submissions[0].Body,Is.EqualTo(submissions[1].Body));});
    }

    [Test]
    public async Task WrongPolledIdentityRemainsUncertain() {
        using var handler=new Handler {WrongIdentity=true};using var http=new HttpClient(handler) {BaseAddress=new Uri("http://offline.invalid")};
        var result=await new UpasSkyCorrectionClient(http,"client").ExecuteAsync(Measurement(),"request");
        Assert.Multiple(()=>{Assert.That(result.State,Is.EqualTo("UNCERTAIN"));Assert.That(handler.Stops,Is.EqualTo(1));});
    }

    [Test]
    public void UnqualifiedGeometryRejectsBeforeAnyTransport() {
        using var handler=new Handler();using var http=new HttpClient(handler) {BaseAddress=new Uri("http://offline.invalid")};
        var adapter=new UpasSkyJobIntegrationAdapter(new(http,"client"));
        Assert.Throws<InvalidOperationException>(()=>adapter.ExecuteAsync(Measurement(),"request",false,CancellationToken.None));
        Assert.That(handler.Requests,Is.Empty);
    }
}
