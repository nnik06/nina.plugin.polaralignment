using System.Threading;
using System.Threading.Tasks;
using NINA.Plugins.PolarAlignment.Qualification;

namespace NINA.Plugins.PolarAlignment;

/// <summary>
/// Unregistered offline candidate seam. No MEF export, settings switch, hardware
/// connection or automatic route selection. Existing TPPA sky math and topics
/// remain untouched. A future reviewed activation must supply real qualification.
/// </summary>
public sealed class UpasSkyJobIntegrationAdapter {
    private readonly UpasSkyJobOrchestrator orchestrator;
    public UpasSkyJobIntegrationAdapter(UpasSkyCorrectionClient client) => orchestrator = new(client);
    public Task<UpasJobOutcome> ExecuteAsync(UpasSkyMeasurement originalExposureMeasurement,
        string requestId, bool freshGeometryAndSettlingQualified, CancellationToken token) =>
        orchestrator.ExecuteQualifiedMeasurementAsync(originalExposureMeasurement, requestId,
            freshGeometryAndSettlingQualified, token);
}
