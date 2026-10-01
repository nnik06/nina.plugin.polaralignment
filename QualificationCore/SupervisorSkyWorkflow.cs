using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace NINA.Plugins.PolarAlignment.Qualification;

public sealed record SkyAlignmentMeasurement(UpasSkyMeasurement Measurement, double TotalArcmin, bool GeometryAndSettlingQualified, string EarliestExposureUtc);
public sealed record SkyAlignmentOutcome(bool AlignmentVerified, string State, int MechanicalJobs, UpasSkyMeasurement LastMeasurement, string Reason);

/// <summary>Continuous reference is supervisor-owned. This class carries sky
/// intent only; no axis conversion, serial connection or controller fallback.</summary>
public sealed class SupervisorSkyWorkflow {
    private readonly HttpClient http;
    private readonly string operationId;
    private readonly UpasSkyCorrectionClient jobs;
    private JsonElement? reference;
    public string MeasurementSessionId => reference?.GetProperty("measurementSessionId").GetString()
        ?? throw new InvalidOperationException("Supervisor reference has not been prepared.");

    public SupervisorSkyWorkflow(HttpClient http, string clientId, string admittedOperationId) {
        if (!Guid.TryParse(admittedOperationId, out _)) throw new ArgumentException("Exact pre-admitted finite operation UUID required.");
        this.http = http; operationId = admittedOperationId;
        jobs = new(http, clientId, timeout: TimeSpan.FromMinutes(2));
    }

    private async Task<JsonElement> Request(HttpMethod method, string path, object? body, CancellationToken token) {
        using var request = new HttpRequestMessage(method, path);
        if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
        return document.RootElement.Clone();
    }

    public async Task PrepareAsync(CancellationToken token) {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromMinutes(10));
        var row = await Request(HttpMethod.Post, "/attended/v1/sky-reference", new { operationId }, budget.Token).ConfigureAwait(false);
        while (row.GetProperty("state").GetString() == "PREPARING") {
            await Task.Delay(100, budget.Token).ConfigureAwait(false);
            row = await Request(HttpMethod.Get, "/attended/v1/sky-reference/" + operationId, null, budget.Token).ConfigureAwait(false);
        }
        if (row.GetProperty("state").GetString() != "READY" ||
            !Guid.TryParse(row.GetProperty("measurementSessionId").GetString(), out _) ||
            row.GetProperty("standingMotionPermit").GetBoolean() ||
            !TryUtc(row.GetProperty("readyAtUtc").GetString(), out var ready) ||
            !TryUtc(row.GetProperty("expiresAtUtc").GetString(), out var expiry) || expiry <= ready || expiry <= DateTimeOffset.UtcNow)
            throw new InvalidDataException("Finite original-home reference receipt required.");
        reference = row;
    }

    private static bool TryUtc(string? value, out DateTimeOffset stamp) {
        stamp = default;
        return value?.EndsWith("Z", StringComparison.Ordinal) == true && DateTimeOffset.TryParse(value, out stamp);
    }

    private void Require(SkyAlignmentMeasurement value, DateTimeOffset after, HashSet<string> identities) {
        value.Measurement.Validate();
        if (!value.GeometryAndSettlingQualified || !double.IsFinite(value.TotalArcmin) || value.TotalArcmin < 0 ||
            value.Measurement.MeasurementSessionId != MeasurementSessionId ||
            !TryUtc(value.Measurement.MeasuredAtUtc, out var stamp) || stamp < after || stamp > DateTimeOffset.UtcNow ||
            !TryUtc(value.EarliestExposureUtc, out var first) || first < after || first > stamp ||
            DateTimeOffset.UtcNow >= DateTimeOffset.Parse(reference!.Value.GetProperty("expiresAtUtc").GetString()!) ||
            !identities.Add(value.Measurement.MeasurementId))
            throw new InvalidOperationException("Fresh independent post-reference geometry/settling and original exposure UTC required.");
    }

    public async Task<SkyAlignmentOutcome> AlignAsync(SkyAlignmentMeasurement initial,
        Func<string, CancellationToken, Task<SkyAlignmentMeasurement>> measureFresh,
        double targetArcmin, int maximumJobs = 3, CancellationToken token = default) {
        if (!double.IsFinite(targetArcmin) || targetArcmin <= 0 || targetArcmin > 24 || maximumJobs < 1 || maximumJobs > 12)
            throw new ArgumentException("Existing operational target and finite correction ceiling required.");
        if (reference is null) throw new InvalidOperationException("Prepare supervisor home before the first measurement.");
        var seen = new HashSet<string>();
        var after = DateTimeOffset.Parse(reference.Value.GetProperty("readyAtUtc").GetString()!);
        Require(initial, after, seen);
        var current = initial; var count = 0; string? lastJob = null;
        while (true) {
            token.ThrowIfCancellationRequested();
            if (current.TotalArcmin <= targetArcmin) {
                var confirmation = await measureFresh(MeasurementSessionId, token).ConfigureAwait(false);
                Require(confirmation, DateTimeOffset.Parse(current.Measurement.MeasuredAtUtc).AddTicks(1), seen);
                var az = confirmation.Measurement.AzErrArcmin - current.Measurement.AzErrArcmin;
                var alt = confirmation.Measurement.AltErrArcmin - current.Measurement.AltErrArcmin;
                if (confirmation.TotalArcmin <= targetArcmin && Math.Sqrt(az * az + alt * alt) <= Math.Min(3, targetArcmin)) {
                    if (lastJob != null) await jobs.RecordFeedbackAsync(lastJob, "FINAL_SUCCESS", confirmation.Measurement,
                        Guid.NewGuid().ToString("D"), token).ConfigureAwait(false);
                    return new(true, "ALIGNED", count, confirmation.Measurement, null!);
                }
                return new(false, "CONFIRMATION_FAILED", count, confirmation.Measurement, "Independent stationary confirmation failed.");
            }
            if (count >= maximumJobs) return new(false, "BOUNDED_INCOMPLETE", count, current.Measurement, "Finite convergence ceiling reached.");
            var result = await jobs.ExecuteAsync(current.Measurement, Guid.NewGuid().ToString("D"), token).ConfigureAwait(false);
            if (!result.MechanicalExecutionComplete)
                return new(false, result.State, count, current.Measurement, result.Reason);
            count++; lastJob = result.JobId;
            // This is a freshness lower bound, never a replacement exposure time.
            after = DateTimeOffset.UtcNow;
            current = await measureFresh(MeasurementSessionId, token).ConfigureAwait(false);
            Require(current, after, seen);
            await jobs.RecordFeedbackAsync(lastJob, "POST_JOB", current.Measurement,
                Guid.NewGuid().ToString("D"), token).ConfigureAwait(false);
        }
    }

    public async Task CloseAsync() {
        // Even cancelled preparation may own a worker. Exact operation identity
        // reaches its original cooperative stop/cleanup; never force a process.
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var row = await Request(HttpMethod.Post, "/attended/v1/sky-reference/" + operationId + "/close", new { }, budget.Token).ConfigureAwait(false);
        if (!row.GetProperty("closed").GetBoolean()) throw new InvalidOperationException("Original supervisor cleanup remains unconfirmed.");
        reference = null;
    }
}
