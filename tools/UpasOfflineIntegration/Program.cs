using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using NINA.Plugins.PolarAlignment;
using NINA.Plugins.PolarAlignment.Qualification;

bool production = args.Length == 7;
if (args.Length != 4 && !production) throw new ArgumentException("Usage: PythonExe SupervisorRoot EncoderRoot NewJournalFile (synthetic stdio only)");
using var transport = new SyntheticProcessHandler(args[0], args[1], args[2], args[3], production ? args[4] : null, production ? args[5] : null, production ? args[6] : null);
using var http = new HttpClient(transport) { BaseAddress = new Uri(production ? "http://127.0.0.1" : "http://offline.invalid") };
if (production) {
    http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "stdio-demo-identity-not-network-token");
    var connection = new SupervisorSkyIntentConnection(http, "tppa");
    var productionAdapter = UpasSkyJobIntegrationAdapter.ForProductionConnection(connection);
    var exposure = new UpasSkyMeasurement("production-m", "production-s", DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), UpasSkyMeasurement.Convention, "NORTH", -18, -18);
    var refused = await productionAdapter.ExecuteAsync(exposure, "production-intent", true, CancellationToken.None);
    var diagnostics = await transport.ControlAsync(null);
    if (refused.State != "REFUSED" || refused.JobId != null || refused.MechanicalExecutionComplete || refused.SkyAlignmentVerified || diagnostics.GetProperty("writes").GetInt32() != 0)
        throw new Exception("Inhibited production connection dispatched or misreported completion.");
    Console.WriteLine(JsonSerializer.Serialize(new { result = refused, diagnostics, hardwareOperations = 0,
        path = "actual plugin production seam -> actual async authenticated client -> source-bound inhibited supervisor -> native encoder clock/profile binding" }));
    return;
}
var client = new UpasSkyCorrectionClient(http, "tppa", timeout: TimeSpan.FromSeconds(10), pollInterval: TimeSpan.FromMilliseconds(5));
var adapter = new UpasSkyJobIntegrationAdapter(client);
var measurement = new UpasSkyMeasurement("dotnet-measurement", "dotnet-session", "2026-10-01T00:00:01Z", UpasSkyMeasurement.Convention, "NORTH", -18, -18);
bool rejected = false;
try { _ = adapter.ExecuteAsync(measurement, "invalid", false, CancellationToken.None); }
catch (InvalidOperationException) { rejected = true; }
if (!rejected) throw new Exception("Unqualified geometry was not rejected.");
var result = await adapter.ExecuteAsync(measurement, "dotnet-job", true, CancellationToken.None);
if (!result.MechanicalExecutionComplete || result.SkyAlignmentVerified) throw new Exception(JsonSerializer.Serialize(result));
// Repeat the full call: durable keys must return original job without new writes.
var repeat = await adapter.ExecuteAsync(measurement, "dotnet-job", true, CancellationToken.None);
if (repeat.JobId != result.JobId || repeat.State != result.State) throw new Exception("Idempotent lifecycle mismatch.");
var control = await transport.ControlAsync(null);
if (control.GetProperty("writes").GetInt32() != 6) throw new Exception("Unexpected duplicate or missing writes.");
Console.WriteLine(JsonSerializer.Serialize(new { result, repeatedJobId = repeat.JobId, syntheticOnly = true, control,
    path = "actual plugin adapter -> actual async C# client -> actual Python coordinator -> actual encoder provider -> synthetic ports" }));

sealed class SyntheticProcessHandler : HttpMessageHandler {
    private readonly Process process;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Task<string> errors;
    private bool disposed;
    private readonly bool production;
    public SyntheticProcessHandler(string python, string supervisor, string encoder, string journal, string? package = null, string? host = null, string? boot = null) {
        production = package != null;
        if (File.Exists(journal)) throw new ArgumentException("Fresh finite demo journal required.");
        var start = new ProcessStartInfo(python) { UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add(Path.Combine(supervisor, "tools", production ? "production_candidate_stdio.py" : "offline_integration_bridge.py"));
        start.ArgumentList.Add("--journal"); start.ArgumentList.Add(journal);
        if (production) {
            start.ArgumentList.Add("--package"); start.ArgumentList.Add(package!);
            start.ArgumentList.Add("--host"); start.ArgumentList.Add(host!);
            start.ArgumentList.Add("--boot"); start.ArgumentList.Add(boot!);
        }
        start.Environment["PYTHONPATH"] = Path.Combine(supervisor,"src") + Path.PathSeparator + Path.Combine(encoder,"src");
        process = Process.Start(start) ?? throw new Exception("Synthetic bridge failed to start.");
        errors = process.StandardError.ReadToEndAsync();
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
        JsonElement? body = request.Content == null ? null : JsonDocument.Parse(await request.Content.ReadAsStringAsync(token)).RootElement.Clone();
        await gate.WaitAsync(token).ConfigureAwait(false);
        try {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { method = request.Method.Method, path = request.RequestUri!.AbsolutePath, client = "tppa", body })).ConfigureAwait(false);
            // Drain exactly one reply even when the caller cancels. No reply can
            // be mistaken for the subsequent stop request on this stdio channel.
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            if (line == null) throw new HttpRequestException("Synthetic bridge exited: " + await errors.ConfigureAwait(false));
            using var reply = JsonDocument.Parse(line);
            return new HttpResponseMessage((HttpStatusCode)reply.RootElement.GetProperty("status").GetInt32()) {
                Content = new StringContent(reply.RootElement.GetProperty("body").GetRawText(), Encoding.UTF8,"application/json") };
        } finally { gate.Release(); }
    }
    public async Task<JsonElement> ControlAsync(string? mode) {
        using var client = new HttpClient(this, false) { BaseAddress = new Uri(production ? "http://127.0.0.1" : "http://offline.invalid") };
        using var response = await client.PostAsync(production ? "/candidate/diagnostics" : "/offline/test/control",new StringContent(JsonSerializer.Serialize(new { mode }),Encoding.UTF8,"application/json"));
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return doc.RootElement.Clone();
    }
    protected override void Dispose(bool disposing) {
        if (disposing && !disposed) {
            disposed = true;
            process.StandardInput.Close();
            if (!process.WaitForExit(10000)) { process.Kill(true); throw new Exception("Synthetic bridge failed bounded cleanup."); }
            if (process.ExitCode != 0) throw new Exception(errors.GetAwaiter().GetResult());
            process.Dispose(); gate.Dispose();
        }
        base.Dispose(disposing);
    }
}

