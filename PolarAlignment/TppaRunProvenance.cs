using Newtonsoft.Json;
using System;
using System.Globalization;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaRunProvenance(
        [property: JsonProperty("schemaVersion")]
        int SchemaVersion,
        [property: JsonProperty("runId")]
        Guid RunId,
        [property: JsonProperty("runStartedUtc")]
        DateTime RunStartedUtc,
        [property: JsonProperty("refractionAdjustmentEnabled")]
        bool RefractionAdjustmentEnabled,
        [property: JsonProperty("poleTarget")]
        string PoleTarget,
        [property: JsonProperty("estimatedTruePoleOffsetArcMinutes")]
        double? EstimatedTruePoleOffsetArcMinutes,
        [property: JsonProperty("atmosphereSource")]
        string AtmosphereSource,
        [property: JsonProperty("pressureHPa")]
        double PressureHPa,
        [property: JsonProperty("temperatureCelsius")]
        double TemperatureCelsius,
        [property: JsonProperty("relativeHumidity")]
        double RelativeHumidity,
        [property: JsonProperty("wavelengthMicrons")]
        double WavelengthMicrons,
        [property: JsonProperty("automatedAdjustmentsEnabled")]
        bool AutomatedAdjustmentsEnabled,
        [property: JsonProperty("actuatorMovementAllowed")]
        bool ActuatorMovementAllowed,
        [property: JsonProperty("verificationOnly")]
        bool VerificationOnly,
        [property: JsonProperty("driftValidationOnly")]
        bool DriftValidationOnly,
        [property: JsonProperty("alignmentToleranceArcMinutes")]
        double AlignmentToleranceArcMinutes) {
        public const int CurrentSchemaVersion = 1;

        private static readonly JsonSerializerSettings SerializerSettings = new() {
            Culture = CultureInfo.InvariantCulture,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            NullValueHandling = NullValueHandling.Include
        };

        public string ToJson() =>
            JsonConvert.SerializeObject(this, Formatting.None, SerializerSettings);
    }
}
