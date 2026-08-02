using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Globalization;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaVerificationPointContextEvidence(
        int SchemaVersion,
        Guid RunId,
        string ArcDirection,
        int PointIndex,
        string ApproachDirection,
        bool ApproachDirectionKnown,
        double? RequestedRightAscensionTravelDegrees,
        double? SettleSeconds,
        DateTime ObservationUtc,
        double HourAngleDegrees,
        double ApparentAltitudeDegrees,
        bool RefractionAdjustmentEnabled,
        double ComputedRefractionDriftArcsecondsPerMinute,
        double PressureHPa,
        double TemperatureCelsius,
        double RelativeHumidity,
        double WavelengthMicrons) {

        public const int CurrentSchemaVersion = 1;
        public bool GrantsMotionAuthority => false;
        public bool GrantsCompletionAuthority => false;

        public string ToJson() {
            var settings = new JsonSerializerSettings {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Culture = CultureInfo.InvariantCulture,
                NullValueHandling = NullValueHandling.Include
            };
            return JsonConvert.SerializeObject(this, Formatting.None, settings);
        }
    }

    internal static class TppaVerificationPointContextEvidenceFactory {
        public static TppaVerificationPointContextEvidence Create(
            Guid runId,
            string arcDirection,
            int pointIndex,
            string approachDirection,
            bool approachDirectionKnown,
            double? requestedRightAscensionTravelDegrees,
            double? settleSeconds,
            TppaDriftRuntimeObservation observation,
            bool refractionAdjustmentEnabled,
            RefractionParameters refraction) {
            if (runId == Guid.Empty) { throw new ArgumentException("A run identifier is required.", nameof(runId)); }
            if (string.IsNullOrWhiteSpace(arcDirection)) { throw new ArgumentException("An arc direction is required.", nameof(arcDirection)); }
            if (pointIndex < 1) { throw new ArgumentOutOfRangeException(nameof(pointIndex)); }
            if (string.IsNullOrWhiteSpace(approachDirection)) { throw new ArgumentException("An approach direction label is required.", nameof(approachDirection)); }
            if (requestedRightAscensionTravelDegrees is < 0) { throw new ArgumentOutOfRangeException(nameof(requestedRightAscensionTravelDegrees)); }
            if (settleSeconds is < 0) { throw new ArgumentOutOfRangeException(nameof(settleSeconds)); }
            ArgumentNullException.ThrowIfNull(refraction);

            return new TppaVerificationPointContextEvidence(
                TppaVerificationPointContextEvidence.CurrentSchemaVersion,
                runId,
                arcDirection.Trim(),
                pointIndex,
                approachDirection.Trim(),
                approachDirectionKnown,
                requestedRightAscensionTravelDegrees,
                settleSeconds,
                observation.Sample.ObservationTimeUtc,
                observation.Metadata.HourAngleDegrees,
                observation.Metadata.AltitudeDegrees,
                refractionAdjustmentEnabled,
                observation.Metadata.ComputedRefractionDriftArcsecondsPerMinute,
                refraction.PressureHPa,
                refraction.Temperature,
                refraction.RelativeHumidity,
                refraction.Wavelength);
        }
    }
}
