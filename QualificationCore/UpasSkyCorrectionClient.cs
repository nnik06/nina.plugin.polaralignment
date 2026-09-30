// Offline integration candidate. No hardware adapter or NINA registration.
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NINA.Plugins.PolarAlignment.Qualification;

public sealed record UpasSkyMeasurement(
    string MeasurementId, string MeasurementSessionId, string MeasuredAtUtc,
    string CoordinateConvention, string Hemisphere, double AzErrArcmin, double AltErrArcmin) {
    public const string Convention = "tppaAzimuthCoordinateTruePoleRefractionOnV1";
    public void Validate() {
        if (string.IsNullOrWhiteSpace(MeasurementId) || string.IsNullOrWhiteSpace(MeasurementSessionId) ||
            CoordinateConvention != Convention || Hemisphere is not ("NORTH" or "SOUTH") ||
            !double.IsFinite(AzErrArcmin) || !double.IsFinite(AltErrArcmin) ||
            !MeasuredAtUtc.EndsWith("Z", StringComparison.Ordinal) ||
            !DateTimeOffset.TryParse(MeasuredAtUtc, out _))
            throw new ArgumentException("Qualified sky identity, original exposure UTC and supported convention required.");
    }
}

public sealed record UpasJobOutcome(string JobId, string State, JsonElement? Evidence, string Reason) {
    public bool MechanicalExecutionComplete => State == "EXECUTED";
    // A mechanical outcome never certifies a newly measured sky residual.
    public bool SkyAlignmentVerified => false;
}

public sealed class UpasSkyCorrectionClient {
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly HashSet<string> Terminal = new() { "EXECUTED", "PARTIAL", "REFUSED", "UNCERTAIN" };
    private readonly HttpClient http;
    private readonly string clientId;
    private readonly TimeSpan pollInterval, timeout, cleanupTimeout;
    public UpasSkyCorrectionClient(HttpClient http, string clientId, TimeSpan? timeout = null,
        TimeSpan? pollInterval = null, TimeSpan? cleanupTimeout = null) {
        this.http = http ?? throw new ArgumentNullException(nameof(http));
        if (string.IsNullOrWhiteSpace(clientId)) throw new ArgumentException("Authenticated client identity required.");
        this.clientId = clientId;
        this.timeout = timeout ?? TimeSpan.FromSeconds(30);
        this.pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(100);
        this.cleanupTimeout = cleanupTimeout ?? TimeSpan.FromSeconds(2);
        if (this.timeout <= TimeSpan.Zero || this.timeout > TimeSpan.FromMinutes(2) ||
            this.pollInterval <= TimeSpan.Zero || this.pollInterval > TimeSpan.FromSeconds(1) ||
            this.cleanupTimeout <= TimeSpan.Zero || this.cleanupTimeout > TimeSpan.FromSeconds(5))
            throw new ArgumentException("Finite polling and independent cleanup bounds required.");
    }

    private async Task<JsonElement> Request(HttpMethod method, string path, object body, CancellationToken token) {
        using var request = new HttpRequestMessage(method, path);
        if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Contract A {(int)response.StatusCode}: {content}", null, response.StatusCode);
        using var document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }

    private static UpasJobOutcome Outcome(JsonElement result) {
        var id = result.GetProperty("jobId").GetString();
        var state = result.GetProperty("state").GetString();
        if (string.IsNullOrWhiteSpace(id) || !(Terminal.Contains(state) || state is "ACCEPTED" or "RUNNING"))
            throw new InvalidDataException("Unrecognized job identity/state.");
        return new(id, state, result.Clone(), result.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null);
    }

    public async Task<UpasJobOutcome> ExecuteAsync(UpasSkyMeasurement measurement, string requestId,
        CancellationToken cancellationToken = default) {
        measurement.Validate();
        if (string.IsNullOrWhiteSpace(requestId)) throw new ArgumentException("Stable request identity required.");
        string leaseId = null, jobId = null;
        bool terminalKnown = false;
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        execution.CancelAfter(timeout);
        var token = execution.Token;
        var timer = Stopwatch.StartNew();
        double renewedAt = 0;
        try {
            var lease = await Request(HttpMethod.Post, "/v1/lease", new { clientId, idempotencyKey = requestId + ":lease" }, token).ConfigureAwait(false);
            leaseId = lease.GetProperty("leaseId").GetString();
            var body = new {
                measurement.MeasurementId, measurement.MeasurementSessionId, measurement.MeasuredAtUtc,
                measurement.CoordinateConvention, measurement.Hemisphere, measurement.AzErrArcmin, measurement.AltErrArcmin,
                leaseId, idempotencyKey = requestId + ":sky"
            };
            JsonElement accepted;
            try { accepted = await Request(HttpMethod.Post, "/v1/sky-corrections", body, token).ConfigureAwait(false); }
            catch (HttpRequestException ex) when (ex.StatusCode == null && !token.IsCancellationRequested) {
                // One lost-response recovery uses the identical durable key and body.
                // It cannot create a second job or a new allowance.
                accepted = await Request(HttpMethod.Post, "/v1/sky-corrections", body, token).ConfigureAwait(false);
            }
            jobId = accepted.GetProperty("jobId").GetString();
            if (string.IsNullOrWhiteSpace(jobId)) throw new InvalidDataException("Job acknowledgement lacks identity.");
            while (true) {
                var result = Outcome(await Request(HttpMethod.Get, "/v1/jobs/" + Uri.EscapeDataString(jobId), null, token).ConfigureAwait(false));
                if (result.JobId != jobId) throw new InvalidDataException("Job identity changed.");
                if (Terminal.Contains(result.State)) { terminalKnown = true; return result; }
                if (timer.Elapsed.TotalSeconds - renewedAt >= 60) {
                    await Request(HttpMethod.Post, "/v1/lease/renew", new { clientId, leaseId, idempotencyKey = requestId + ":renew:" + (int)timer.Elapsed.TotalSeconds }, token).ConfigureAwait(false);
                    renewedAt = timer.Elapsed.TotalSeconds;
                }
                await Task.Delay(pollInterval, token).ConfigureAwait(false);
            }
        } catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or InvalidDataException or JsonException) {
            // Independent budget: caller cancellation must not cancel stop/poll cleanup.
            using var cleanup = new CancellationTokenSource(cleanupTimeout);
            try {
                if (leaseId != null) {
                    var stop = await Request(HttpMethod.Post, "/v1/stop", new { leaseId, idempotencyKey = requestId + ":stop" }, cleanup.Token).ConfigureAwait(false);
                    if (jobId == null && stop.TryGetProperty("jobId", out var stoppedId) && stoppedId.ValueKind == JsonValueKind.String)
                        jobId = stoppedId.GetString();
                }
                while (jobId != null) {
                    var result = Outcome(await Request(HttpMethod.Get, "/v1/jobs/" + Uri.EscapeDataString(jobId), null, cleanup.Token).ConfigureAwait(false));
                    if (result.JobId != jobId) throw new InvalidDataException("Cleanup identity changed.");
                    if (Terminal.Contains(result.State)) { terminalKnown = true; return result; }
                    await Task.Delay(pollInterval, cleanup.Token).ConfigureAwait(false);
                }
            } catch (Exception cleanupError) when (cleanupError is OperationCanceledException or HttpRequestException or InvalidDataException or JsonException) { }
            return new(jobId, "UNCERTAIN", null, ex.GetType().Name + ": " + ex.Message);
        } finally {
            // Revocation permits no further chunks. Its acknowledgement isn't stop proof.
            if (leaseId != null) {
                using var cleanup = new CancellationTokenSource(cleanupTimeout);
                try { await Request(HttpMethod.Post, "/v1/lease/release", new { clientId, leaseId, idempotencyKey = requestId + ":release" }, cleanup.Token).ConfigureAwait(false); }
                catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or JsonException) { }
            }
            _ = terminalKnown; // Kept distinct from stop ACK and release acknowledgement.
        }
    }

    public Task<JsonElement> RecordFeedbackAsync(string jobId, string kind, UpasSkyMeasurement measurement,
        string idempotencyKey, CancellationToken token = default) {
        measurement.Validate();
        if (kind is not ("POST_JOB" or "FINAL_SUCCESS")) throw new ArgumentException("Feedback kind required.");
        return Request(HttpMethod.Post, "/v1/measurement-feedback", new { clientId, idempotencyKey, jobId, kind, measurement }, token);
    }
}

/// <summary>Common headless orchestration seam used by the unregistered plugin adapter.</summary>
public sealed class UpasSkyJobOrchestrator {
    private readonly UpasSkyCorrectionClient client;
    public UpasSkyJobOrchestrator(UpasSkyCorrectionClient client) => this.client = client;
    public Task<UpasJobOutcome> ExecuteQualifiedMeasurementAsync(UpasSkyMeasurement measurement,
        string requestId, bool freshGeometryAndSettlingQualified, CancellationToken token = default) {
        if (!freshGeometryAndSettlingQualified)
            throw new InvalidOperationException("Fresh three-point geometry and settling qualification required.");
        return client.ExecuteAsync(measurement, requestId, token);
    }
}
