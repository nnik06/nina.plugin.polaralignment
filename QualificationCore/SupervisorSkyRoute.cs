using System.Net.Http;
using System.Net.Http.Headers;

namespace NINA.Plugins.PolarAlignment.Qualification;

/// <summary>Explicit NINA route. A failed or inhibited service never selects a
/// plugin-owned controller. Original exposure times and sky errors pass through.</summary>
public sealed class SupervisorSkyRoute : IDisposable, IAsyncDisposable {
    private SupervisorSkyWorkflow? workflow;
    public string MeasurementSessionId => workflow?.MeasurementSessionId ?? throw new InvalidOperationException("Prepared supervisor reference required.");
    private readonly HttpClient http;
    private readonly SupervisorSkyIntentConnection connection;
    public static bool Requested(Func<string,string> environment) {
        var value = environment("UPAS_CONTRACT_A_ROUTE");
        if (string.IsNullOrEmpty(value) || value == "0") return false;
        if (value != "1") throw new ArgumentException("UPAS_CONTRACT_A_ROUTE must be explicitly 0 or 1.");
        return true;
    }
    public static void RequireOpticalConfiguration(string actual, Func<string,string> environment) {
        var expected = environment("UPAS_SKY_OPTICAL_CONFIGURATION_ID");
        if (string.IsNullOrWhiteSpace(expected) || !System.Text.RegularExpressions.Regex.IsMatch(expected,"^[0-9a-f]{64}$") || actual != expected)
            throw new InvalidOperationException("Exact adopted optical configuration required; requalify after configuration changes.");
    }
    public SupervisorSkyRoute(HttpClient authenticatedHttp,string clientId, string? admittedOperationId = null) {
        if (admittedOperationId != null) workflow = new(authenticatedHttp,clientId,admittedOperationId);
        http = authenticatedHttp;
        connection = new(http,clientId);
    }
    public static SupervisorSkyRoute FromEnvironment(Func<string,string> environment) {
        if (!Requested(environment)) throw new InvalidOperationException("Supervisor route is not selected.");
        var endpoint = environment("UPAS_CONTRACT_A_ENDPOINT");
        var token = environment("UPAS_SUPERVISOR_CLIENT_TOKEN");
        var identity = environment("UPAS_CONTRACT_A_CLIENT_ID");
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(identity))
            throw new ArgumentException("Explicit supervisor origin, bearer token and client identity required.");
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) {
            BaseAddress = new Uri(endpoint,UriKind.Absolute), Timeout = TimeSpan.FromSeconds(60)
        };
        try {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",token);
            var operation = environment("UPAS_SKY_OPERATION_ID");
            if (string.IsNullOrWhiteSpace(operation)) throw new ArgumentException("Exact finite supervisor sky operation admission required.");
            return new(client,identity,operation);
        } catch { client.Dispose(); throw; }
    }
    public async Task RequireReadyBeforeMeasurementAsync(CancellationToken token) {
        if (workflow != null) await workflow.PrepareAsync(token).ConfigureAwait(false);
        var status = await connection.CheckAsync(token).ConfigureAwait(false);
        if (!status.GetProperty("canMove").GetBoolean())
            throw new InvalidOperationException("UPAS supervisor inhibited: " + status.GetProperty("reasonIfNot").GetString());
    }
    public Task<UpasJobOutcome> ExecuteFreshAsync(UpasSkyMeasurement measurement,string requestId,
        bool qualified,CancellationToken token) => connection.ExecuteAsync(measurement,requestId,qualified,token);
    public Task<SkyAlignmentOutcome> AlignAsync(SkyAlignmentMeasurement initial, Func<string,CancellationToken,Task<SkyAlignmentMeasurement>> fresh, double tolerance, CancellationToken token) =>
        (workflow ?? throw new InvalidOperationException("Prepared workflow required.")).AlignAsync(initial,fresh,tolerance,3,token);
    public void Dispose() => http.Dispose();
    public async ValueTask DisposeAsync() {
        try { if (workflow != null) await workflow.CloseAsync().ConfigureAwait(false); }
        finally { http.Dispose(); }
    }
}
