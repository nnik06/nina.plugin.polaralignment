using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using NINA.Plugins.PolarAlignment;
using NINA.Plugins.PolarAlignment.Qualification;

if (args.Length != 4) throw new ArgumentException("Usage: PythonExe SupervisorRoot EncoderRoot NewJournalFile (synthetic stdio only)");
using var transport = new SyntheticProcessHandler(args[0], args[1], args[2], args[3]);
using var http = new HttpClient(transport) { BaseAddress = new Uri("http://offline.invalid") };
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
    public SyntheticProcessHandler(string python, string supervisor, string encoder, string journal) {
        if (File.Exists(journal)) throw new ArgumentException("Fresh finite demo journal required.");
        var start = new ProcessStartInfo(python) { UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add(Path.Combine(supervisor, "tools", "offline_integration_bridge.py"));
        start.ArgumentList.Add("--journal"); start.ArgumentList.Add(journal);
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
        using var client = new HttpClient(this, false) { BaseAddress = new Uri("http://offline.invalid") };
        using var response = await client.PostAsync("/offline/test/control",new StringContent(JsonSerializer.Serialize(new { mode }),Encoding.UTF8,"application/json"));
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
