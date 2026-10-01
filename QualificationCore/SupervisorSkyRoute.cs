using System.Net.Http;
using System.Net.Http.Headers;

namespace NINA.Plugins.PolarAlignment.Qualification;

/// <summary>Explicit NINA route. A failed or inhibited service never selects a
/// plugin-owned controller. Original exposure times and sky errors pass through.</summary>
public sealed class SupervisorSkyRoute : IDisposable {
    private readonly HttpClient http;
    private readonly SupervisorSkyIntentConnection connection;
    public static bool Requested(Func<string,string> environment) {
        var value = environment("UPAS_CONTRACT_A_ROUTE");
        if (string.IsNullOrEmpty(value) || value == "0") return false;
        if (value != "1") throw new ArgumentException("UPAS_CONTRACT_A_ROUTE must be explicitly 0 or 1.");
        return true;
    }
    public SupervisorSkyRoute(HttpClient authenticatedHttp,string clientId) {
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
            BaseAddress = new Uri(endpoint,UriKind.Absolute), Timeout = TimeSpan.FromSeconds(10)
        };
        try {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",token);
            return new(client,identity);
        } catch { client.Dispose(); throw; }
    }
    public async Task RequireReadyBeforeMeasurementAsync(CancellationToken token) {
        var status = await connection.CheckAsync(token).ConfigureAwait(false);
        if (!status.GetProperty("canMove").GetBoolean())
            throw new InvalidOperationException("UPAS supervisor inhibited: " + status.GetProperty("reasonIfNot").GetString());
    }
    public Task<UpasJobOutcome> ExecuteFreshAsync(UpasSkyMeasurement measurement,string requestId,
        bool qualified,CancellationToken token) => connection.ExecuteAsync(measurement,requestId,qualified,token);
    public void Dispose() => http.Dispose();
}
