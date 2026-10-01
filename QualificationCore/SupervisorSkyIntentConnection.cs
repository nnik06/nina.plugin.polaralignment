using System.Net.Http;
using System.Text.Json;

namespace NINA.Plugins.PolarAlignment.Qualification;

/// <summary>
/// Async production connection seam. Status inhibition is honored without a
/// lease, command or fallback to a plugin-owned motor. A ready status still
/// requires the supervisor to admit the subsequently submitted finite job.
/// </summary>
public sealed class SupervisorSkyIntentConnection {
    private readonly HttpClient http;
    private readonly UpasSkyCorrectionClient client;
    public SupervisorSkyIntentConnection(HttpClient authenticatedHttp, string clientId) {
        http = authenticatedHttp ?? throw new ArgumentNullException(nameof(authenticatedHttp));
        var endpoint = http.BaseAddress ?? throw new ArgumentException("Explicit supervisor endpoint required.");
        if (!endpoint.IsAbsoluteUri || !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment) ||
            endpoint.AbsolutePath != "/" ||
            !(endpoint.Scheme == "https" || endpoint.Scheme == "http" && endpoint.IsLoopback))
            throw new ArgumentException("HTTPS or local loopback supervisor origin required.");
        if (http.DefaultRequestHeaders.Authorization?.Scheme != "Bearer" ||
            string.IsNullOrWhiteSpace(http.DefaultRequestHeaders.Authorization.Parameter))
            throw new ArgumentException("Explicit authenticated supervisor identity required.");
        client = new(http, clientId);
    }

    public async Task<JsonElement> CheckAsync(CancellationToken token = default) {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(5));
        using var response = await http.GetAsync("/v1/status", budget.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false));
        var row = document.RootElement;
        var expected = new[] { "serviceVersion", "contractVersion", "canMove", "reasonIfNot", "axes", "activeFaults" };
        if (row.ValueKind != JsonValueKind.Object ||
            !row.EnumerateObject().Select(x => x.Name).OrderBy(x=>x).SequenceEqual(expected.OrderBy(x=>x)) ||
            row.GetProperty("contractVersion").GetString() != "1.1" ||
            string.IsNullOrWhiteSpace(row.GetProperty("serviceVersion").GetString()) ||
            row.GetProperty("canMove").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Contract A status is incompatible.");
        var axes = row.GetProperty("axes");
        if (axes.ValueKind != JsonValueKind.Object || axes.EnumerateObject().Count() != 2 ||
            !axes.TryGetProperty("AZ", out _) || !axes.TryGetProperty("ALT", out _) ||
            row.GetProperty("activeFaults").ValueKind != JsonValueKind.Array ||
            !row.GetProperty("canMove").GetBoolean() &&
            (row.GetProperty("reasonIfNot").ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(row.GetProperty("reasonIfNot").GetString())))
            throw new InvalidDataException("Both-axis status and concrete inhibition required.");
        return row.Clone();
    }

    public async Task<UpasJobOutcome> ExecuteAsync(UpasSkyMeasurement originalExposureMeasurement,
        string requestId, bool freshGeometryAndSettlingQualified, CancellationToken token = default) {
        if (!freshGeometryAndSettlingQualified)
            throw new InvalidOperationException("Fresh three-point geometry and settling qualification required.");
        originalExposureMeasurement.Validate();
        if (string.IsNullOrWhiteSpace(requestId)) throw new ArgumentException("Stable request identity required.");
        var status = await CheckAsync(token).ConfigureAwait(false);
        if (!status.GetProperty("canMove").GetBoolean())
            return new(null, "REFUSED", status, status.GetProperty("reasonIfNot").GetString());
        return await client.ExecuteAsync(originalExposureMeasurement, requestId, token).ConfigureAwait(false);
    }
}
