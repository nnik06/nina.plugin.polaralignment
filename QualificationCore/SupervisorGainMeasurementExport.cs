using System.Text.Json;
using System.Globalization;

namespace NINA.Plugins.PolarAlignment.Qualification;

/// <summary>Explicit measurement-only handoff. No controller/client or accuracy adoption.</summary>
public sealed class SupervisorGainMeasurementExport {
    private readonly string requestPath;
    private readonly JsonElement request;
    private readonly DateTimeOffset after, expiry;
    private readonly string opticalConfigurationId;
    public SupervisorGainMeasurementExport(string requestPath, bool measurementOnly, bool contractARoute,
        bool automated, bool refractionOn, string opticalConfigurationId) {
        RequireMeasurementOnly(measurementOnly, contractARoute, automated, refractionOn);
        this.opticalConfigurationId = opticalConfigurationId;
        this.requestPath = Path.GetFullPath(requestPath);
        using var document = JsonDocument.Parse(File.ReadAllBytes(this.requestPath));
        request = document.RootElement.Clone();
        if (request.GetProperty("scope").GetString() != "ATTENDED_SKY_GAIN_MEASUREMENT"
            || !request.GetProperty("owner_sky_enabled").GetBoolean()
            || !Guid.TryParse(request.GetProperty("measurementSessionId").GetString(), out _)
            || request.GetProperty("host_id").GetString() != Environment.MachineName
            || !OriginalUtc(request.GetProperty("readyAfterUtc").GetString(), out after)
            || !OriginalUtc(request.GetProperty("expiresAtUtc").GetString(), out expiry)
            || expiry <= after || expiry <= DateTimeOffset.UtcNow || expiry - after > TimeSpan.FromSeconds(1700)
            || request.GetProperty("hemisphere").GetString() is not ("NORTH" or "SOUTH")
            || !System.Text.RegularExpressions.Regex.IsMatch(opticalConfigurationId, "^[0-9a-f]{64}$")
            || request.GetProperty("optical_configuration_id").GetString() != opticalConfigurationId)
            throw new InvalidDataException("Exact finite attended supervisor measurement handoff required.");
        var output = request.GetProperty("outputFile").GetString();
        if (output == null || !System.Text.RegularExpressions.Regex.IsMatch(output, "^[0-4]\\.json$"))
            throw new InvalidDataException("Single new measurement filename required.");
    }
    private static bool OriginalUtc(string? value, out DateTimeOffset stamp) {
        stamp = default;
        return value?.EndsWith("Z", StringComparison.Ordinal) == true && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out stamp);
    }
    public static void RequireMeasurementOnly(bool measurementOnly, bool contractARoute, bool automated, bool refractionOn) {
        if (!measurementOnly || contractARoute || automated || !refractionOn)
            throw new InvalidOperationException("Sky-probe export requires measurement-only true-pole mode, with automated adjustments disabled.");
    }
    public void Publish(double azErrArcmin, double altErrArcmin, string hemisphere,
                        IEnumerable<DateTime> originalExposureUtc, bool geometryAndSolveConsistencyQualified, string currentOpticalConfigurationId) {
        var times = originalExposureUtc.ToArray();
        if (currentOpticalConfigurationId != opticalConfigurationId || !geometryAndSolveConsistencyQualified || !double.IsFinite(azErrArcmin) || !double.IsFinite(altErrArcmin)
            || hemisphere != request.GetProperty("hemisphere").GetString() || times.Length != 3
            || times.Any(t => t.Kind != DateTimeKind.Utc || t > DateTime.UtcNow || t < after.UtcDateTime)
            || !(times[0] < times[1] && times[1] < times[2]) || DateTimeOffset.UtcNow >= expiry)
            throw new InvalidDataException("Three original fresh qualified exposure times and finite errors required.");
        var row = new {
            measurementId = Guid.NewGuid().ToString("D"),
            measurementSessionId = request.GetProperty("measurementSessionId").GetString(),
            measuredAtUtc = times[2].ToString("O"), original_exposure_utc = times.Select(t => t.ToString("O")).ToArray(),
            coordinateConvention = UpasSkyMeasurement.Convention, hemisphere, azErrArcmin, altErrArcmin,
            three_point_geometry_qualified = true, synthetic = false,
            binding_pins = request.GetProperty("binding_pins"), optical_configuration_id = opticalConfigurationId,
            source = "TPPA_ORIGINAL_THREE_POINT_EXPORT", absolute_accuracy_qualified = false
        };
        var directory = Path.GetDirectoryName(requestPath)!;
        var target = Path.Combine(directory, request.GetProperty("outputFile").GetString()!);
        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("D") + ".tmp");
        try {
            File.WriteAllText(temporary, JsonSerializer.Serialize(row));
            File.Move(temporary, target, overwrite: false);
        } finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
