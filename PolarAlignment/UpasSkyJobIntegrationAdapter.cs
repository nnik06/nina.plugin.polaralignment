using System.Threading;
using System.Threading.Tasks;
using NINA.Plugins.PolarAlignment.Qualification;

namespace NINA.Plugins.PolarAlignment;

/// <summary>
/// Explicit async connection seam; NINA route registration remains inhibited. No MEF export, settings switch, hardware
/// connection or automatic route selection. Existing TPPA sky math and topics
/// remain untouched. A future reviewed activation must supply real qualification.
/// </summary>
public sealed class UpasSkyJobIntegrationAdapter {
    private readonly UpasSkyJobOrchestrator orchestrator;
    private readonly SupervisorSkyIntentConnection connection;
    public UpasSkyJobIntegrationAdapter(UpasSkyCorrectionClient client) => orchestrator = new(client);
    private UpasSkyJobIntegrationAdapter(SupervisorSkyIntentConnection connection, bool production) => this.connection = connection ?? throw new System.ArgumentNullException(nameof(connection));
    public static UpasSkyJobIntegrationAdapter ForProductionConnection(SupervisorSkyIntentConnection connection) => new(connection, true);
    public Task<UpasJobOutcome> ExecuteAsync(UpasSkyMeasurement originalExposureMeasurement,
        string requestId, bool freshGeometryAndSettlingQualified, CancellationToken token) =>
        connection != null ? connection.ExecuteAsync(originalExposureMeasurement, requestId, freshGeometryAndSettlingQualified, token) :
        orchestrator.ExecuteQualifiedMeasurementAsync(originalExposureMeasurement, requestId,
            freshGeometryAndSettlingQualified, token);
}


