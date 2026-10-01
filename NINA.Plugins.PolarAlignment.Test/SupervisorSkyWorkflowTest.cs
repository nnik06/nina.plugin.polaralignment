using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using NINA.Plugins.PolarAlignment.Qualification;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test;

[TestFixture]
public class SupervisorSkyWorkflowTest {
    private sealed class Handler : HttpMessageHandler {
        public readonly string Session = Guid.NewGuid().ToString("D");
        public readonly List<string> Paths = new();
        public bool InvalidReference, RefusedJob;
        public int Submits, Feedback;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            token.ThrowIfCancellationRequested();
            string path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            object row;
            if (path == "/attended/v1/sky-reference") row = new {
                state = "READY", measurementSessionId = Session,
                readyAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1).UtcDateTime.ToString("O"),
                expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5).UtcDateTime.ToString("O"),
                standingMotionPermit = InvalidReference
            };
            else if (path.EndsWith("/close")) row = new { closed = true };
            else if (path == "/v1/lease") row = new { leaseId = "lease" };
            else if (path == "/v1/lease/release") row = new { released = true };
            else if (path == "/v1/sky-corrections") { Submits++; row = new { jobId = "job", state = "ACCEPTED" }; }
            else if (path == "/v1/jobs/job") row = new { jobId = "job", state = RefusedJob ? "REFUSED" : "EXECUTED", reason = RefusedJob ? "BOUND" : (string?)null };
            else if (path == "/v1/measurement-feedback") { Feedback++; row = new { recorded = true }; }
            else throw new Exception("Unexpected route " + path);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(JsonSerializer.Serialize(row), Encoding.UTF8, "application/json")
            });
        }
    }
    private static SkyAlignmentMeasurement Measurement(string session, double error = 1) {
        string stamp = DateTimeOffset.UtcNow.UtcDateTime.ToString("O");
        return new(new(Guid.NewGuid().ToString("D"), session, stamp, UpasSkyMeasurement.Convention, "NORTH", error, 0),
            Math.Abs(error), true, stamp);
    }
    private static (Handler, HttpClient, SupervisorSkyWorkflow) Setup() {
        var handler = new Handler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://offline.invalid") };
        return (handler, http, new(http, "tppa", Guid.NewGuid().ToString("D")));
    }
    [Test]
    public void MeasurementCannotStartBeforeHomeReadiness() {
        var (h, http, workflow) = Setup(); using var owned = http;
        Assert.ThrowsAsync<InvalidOperationException>(() => workflow.AlignAsync(Measurement(h.Session), (_, _) => Task.FromResult(Measurement(h.Session)), 3));
        Assert.That(h.Paths, Is.Empty);
    }
    [Test]
    public async Task InTargetNeedsIndependentStationaryConfirmationWithoutMotion() {
        var (h, http, workflow) = Setup(); using var owned = http;
        await workflow.PrepareAsync(CancellationToken.None);
        var answer = await workflow.AlignAsync(Measurement(h.Session), async (session, token) => {
            await Task.Delay(5, token); return Measurement(session);
        }, 3);
        Assert.Multiple(() => { Assert.That(answer.AlignmentVerified, Is.True); Assert.That(h.Submits, Is.Zero); });
        await workflow.CloseAsync();
        Assert.That(h.Paths.Last(), Does.EndWith("/close"));
        Assert.Throws<InvalidOperationException>(() => _ = workflow.MeasurementSessionId);
    }
    [TestCase("session")]
    [TestCase("geometry")]
    [TestCase("stale")]
    [TestCase("future")]
    public async Task InvalidMeasurementRefusedBeforeLease(string kind) {
        var (h, http, workflow) = Setup(); using var owned = http;
        await workflow.PrepareAsync(CancellationToken.None);
        var value = Measurement(h.Session);
        if (kind == "session") value = value with { Measurement = value.Measurement with { MeasurementSessionId = "foreign" } };
        if (kind == "geometry") value = value with { GeometryAndSettlingQualified = false };
        if (kind == "stale") value = value with { EarliestExposureUtc = DateTimeOffset.UtcNow.AddMinutes(-2).UtcDateTime.ToString("O") };
        if (kind == "future") value = value with { Measurement = value.Measurement with { MeasuredAtUtc = DateTimeOffset.UtcNow.AddMinutes(2).UtcDateTime.ToString("O") } };
        Assert.ThrowsAsync<InvalidOperationException>(() => workflow.AlignAsync(value, (_, _) => Task.FromResult(Measurement(h.Session)), 3));
        Assert.That(h.Paths, Does.Not.Contain("/v1/lease"));
    }
    [Test]
    public async Task ReusedExposureCannotConfirmAlignment() {
        var (h, http, workflow) = Setup(); using var owned = http;
        await workflow.PrepareAsync(CancellationToken.None); var initial = Measurement(h.Session);
        Assert.ThrowsAsync<InvalidOperationException>(() => workflow.AlignAsync(initial, (_, _) => Task.FromResult(initial), 3));
        Assert.That(h.Submits, Is.Zero);
    }
    [Test]
    public async Task MechanicalExecutionRequiresPostMoveAndFinalMeasurement() {
        var (h, http, workflow) = Setup(); using var owned = http;
        await workflow.PrepareAsync(CancellationToken.None); int measurements = 0;
        var answer = await workflow.AlignAsync(Measurement(h.Session, 6), async (session, token) => {
            await Task.Delay(5, token); measurements++; return Measurement(session);
        }, 3);
        Assert.Multiple(() => { Assert.That(answer.AlignmentVerified, Is.True); Assert.That(h.Submits, Is.EqualTo(1)); Assert.That(measurements, Is.EqualTo(2)); Assert.That(h.Feedback, Is.EqualTo(2)); });
    }
    [Test]
    public async Task FiniteConvergenceCeilingStopsWithoutExtraJob() {
        var (h, http, workflow) = Setup(); using var owned = http;
        await workflow.PrepareAsync(CancellationToken.None);
        var answer = await workflow.AlignAsync(Measurement(h.Session, 6), async (session, token) => {
            await Task.Delay(5, token); return Measurement(session, 6);
        }, 3, maximumJobs: 1);
        Assert.Multiple(() => { Assert.That(answer.State, Is.EqualTo("BOUNDED_INCOMPLETE")); Assert.That(answer.AlignmentVerified, Is.False); Assert.That(h.Submits, Is.EqualTo(1)); });
    }
    [Test]
    public async Task RefusedJobNeverFallsBackOrClaimsAlignment() {
        var (h, http, workflow) = Setup(); using var owned = http; h.RefusedJob = true;
        await workflow.PrepareAsync(CancellationToken.None);
        var answer = await workflow.AlignAsync(Measurement(h.Session, 6), (_, _) => throw new Exception("Measurement must not run after refusal"), 3);
        Assert.Multiple(() => { Assert.That(answer.State, Is.EqualTo("REFUSED")); Assert.That(answer.AlignmentVerified, Is.False); Assert.That(h.Submits, Is.EqualTo(1)); Assert.That(h.Feedback, Is.Zero); });
    }
    [Test]
    public async Task InvalidStandingPermitReferenceCannotBeUsed() {
        var (h, http, workflow) = Setup(); using var owned = http; h.InvalidReference = true;
        Assert.ThrowsAsync<InvalidDataException>(() => workflow.PrepareAsync(CancellationToken.None));
        await workflow.CloseAsync(); Assert.That(h.Submits, Is.Zero);
    }
    [Test]
    public async Task CallerCancellationStillAllowsIndependentOwnedClosure() {
        var (h, http, workflow) = Setup(); using var owned = http;
        await workflow.PrepareAsync(CancellationToken.None); using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(() => workflow.AlignAsync(Measurement(h.Session, 6), (_, _) => Task.FromResult(Measurement(h.Session)), 3, token: cts.Token));
        await workflow.CloseAsync(); Assert.That(h.Paths.Last(), Does.EndWith("/close"));
    }
}
