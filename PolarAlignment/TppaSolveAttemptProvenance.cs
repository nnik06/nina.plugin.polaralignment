using Newtonsoft.Json;
using System;
using System.Globalization;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaSolveRetryPolicy(int MaximumAttempts) {
        public static TppaSolveRetryPolicy FieldDefault => new(MaximumAttempts: 5);

        public bool CanStartAttempt(int completedAttempts) =>
            MaximumAttempts > 0
            && completedAttempts >= 0
            && completedAttempts < MaximumAttempts;
    }

    internal sealed record TppaSolveAttemptProvenance(
        [property: JsonProperty("schemaVersion")]
        int SchemaVersion,
        [property: JsonProperty("attempt")]
        int Attempt,
        [property: JsonProperty("maximumAttempts")]
        int MaximumAttempts,
        [property: JsonProperty("captureStartedUtc")]
        DateTime CaptureStartedUtc,
        [property: JsonProperty("observationTimeUtc")]
        DateTime? ObservationTimeUtc,
        [property: JsonProperty("exposureSeconds")]
        double ExposureSeconds,
        [property: JsonProperty("searchRadiusDegrees")]
        double SearchRadiusDegrees,
        [property: JsonProperty("mountRightAscensionDegrees")]
        double? MountRightAscensionDegrees,
        [property: JsonProperty("mountDeclinationDegrees")]
        double? MountDeclinationDegrees,
        [property: JsonProperty("solverType")]
        string SolverType,
        [property: JsonProperty("captureSucceeded")]
        bool CaptureSucceeded,
        [property: JsonProperty("solveSucceeded")]
        bool SolveSucceeded,
        [property: JsonProperty("solvedRightAscensionDegrees")]
        double? SolvedRightAscensionDegrees,
        [property: JsonProperty("solvedDeclinationDegrees")]
        double? SolvedDeclinationDegrees,
        [property: JsonProperty("failureKind")]
        string FailureKind,
        [property: JsonProperty("failureMessage")]
        string FailureMessage) {
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
