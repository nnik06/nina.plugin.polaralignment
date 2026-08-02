using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal static class TppaFastRunTelemetry {
        public const int CurrentSchemaVersion = 1;
        public const string Marker = "TPPA_FAST_RUN_EVENT ";

        public static string Serialize(
                Guid runId,
                string eventName,
                DateTime observedUtc,
                TimeSpan elapsed,
                IReadOnlyDictionary<string, object> fields = null) {
            if (runId == Guid.Empty) {
                throw new ArgumentException("Fast-run telemetry requires a non-empty run id.", nameof(runId));
            }
            if (string.IsNullOrWhiteSpace(eventName)) {
                throw new ArgumentException("Fast-run telemetry requires an event name.", nameof(eventName));
            }
            if (observedUtc.Kind != DateTimeKind.Utc) {
                throw new ArgumentException("Fast-run telemetry timestamps must be UTC.", nameof(observedUtc));
            }
            if (elapsed < TimeSpan.Zero || !double.IsFinite(elapsed.TotalSeconds)) {
                throw new ArgumentOutOfRangeException(nameof(elapsed));
            }

            var payload = new JObject {
                ["schemaVersion"] = CurrentSchemaVersion,
                ["runId"] = runId.ToString("D"),
                ["event"] = eventName.Trim(),
                ["observedUtc"] = observedUtc.ToString("O", CultureInfo.InvariantCulture),
                ["elapsedSeconds"] = Math.Round(elapsed.TotalSeconds, 3)
            };
            foreach (var field in (fields ?? new Dictionary<string, object>())
                         .OrderBy(pair => pair.Key, StringComparer.Ordinal)) {
                if (payload.ContainsKey(field.Key)) {
                    throw new ArgumentException($"Telemetry field '{field.Key}' conflicts with a required field.", nameof(fields));
                }
                payload[field.Key] = ToEvidenceToken(field.Value);
            }
            return Marker + payload.ToString(Formatting.None);
        }

        private static JToken ToEvidenceToken(object value) {
            if (value == null) {
                return JValue.CreateNull();
            }
            if (value is double doubleValue && !double.IsFinite(doubleValue)) {
                return new JValue(doubleValue.ToString(CultureInfo.InvariantCulture));
            }
            if (value is float floatValue && !float.IsFinite(floatValue)) {
                return new JValue(floatValue.ToString(CultureInfo.InvariantCulture));
            }
            return JToken.FromObject(value);
        }
    }
}
