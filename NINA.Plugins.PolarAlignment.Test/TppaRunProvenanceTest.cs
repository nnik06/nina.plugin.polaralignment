using FluentAssertions;
using Newtonsoft.Json.Linq;
using NINA.Plugins.PolarAlignment.Instructions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaRunProvenanceTest {
        [Test]
        public void RunJsonStatesPoleTargetOffsetAtmosphereAndAutomationScope() {
            var started = new DateTime(2026, 7, 27, 20, 0, 0, DateTimeKind.Utc);
            var runId = Guid.Parse("7ceef550-8ef7-41a7-8e58-2a559f01ca98");
            var provenance = new TppaRunProvenance(
                TppaRunProvenance.CurrentSchemaVersion,
                runId,
                started,
                RefractionAdjustmentEnabled: true,
                PoleTarget: RefractionAlignmentTarget.TruePoleTarget,
                EstimatedTruePoleOffsetArcMinutes: 2.0167,
                AtmosphereSource: "standard-atmosphere-fallback",
                PressureHPa: 1013.25,
                TemperatureCelsius: 15,
                RelativeHumidity: 0,
                WavelengthMicrons: 0.55,
                AutomatedAdjustmentsEnabled: true,
                ActuatorMovementAllowed: true,
                VerificationOnly: false,
                DriftValidationOnly: false,
                AlignmentToleranceArcMinutes: 1);

            var json = JObject.Parse(provenance.ToJson());

            json.Value<int>("schemaVersion").Should().Be(1);
            Guid.Parse(json.Value<string>("runId")!).Should().Be(runId);
            json.Value<DateTime>("runStartedUtc").Kind.Should().Be(DateTimeKind.Utc);
            json.Value<bool>("refractionAdjustmentEnabled").Should().BeTrue();
            json.Value<string>("poleTarget").Should().Be(RefractionAlignmentTarget.TruePoleTarget);
            json.Value<double>("estimatedTruePoleOffsetArcMinutes").Should().BeApproximately(2.0167, 1e-9);
            json.Value<string>("atmosphereSource").Should().Be("standard-atmosphere-fallback");
            json.Value<double>("pressureHPa").Should().Be(1013.25);
            json.Value<bool>("automatedAdjustmentsEnabled").Should().BeTrue();
            json.Value<bool>("actuatorMovementAllowed").Should().BeTrue();
        }

        [Test]
        public void UnknownTruePoleOffsetSerializesAsNull() {
            var provenance = new TppaRunProvenance(
                TppaRunProvenance.CurrentSchemaVersion,
                Guid.NewGuid(),
                DateTime.UtcNow,
                RefractionAdjustmentEnabled: false,
                PoleTarget: RefractionAlignmentTarget.ApparentPoleTarget,
                EstimatedTruePoleOffsetArcMinutes: null,
                AtmosphereSource: "standard-atmosphere-fallback",
                PressureHPa: 1013.25,
                TemperatureCelsius: 15,
                RelativeHumidity: 0,
                WavelengthMicrons: 0.55,
                AutomatedAdjustmentsEnabled: false,
                ActuatorMovementAllowed: false,
                VerificationOnly: true,
                DriftValidationOnly: false,
                AlignmentToleranceArcMinutes: 1);

            var json = JObject.Parse(provenance.ToJson());

            json["estimatedTruePoleOffsetArcMinutes"]!.Type.Should().Be(JTokenType.Null);
            json.Value<string>("poleTarget").Should().Be(RefractionAlignmentTarget.ApparentPoleTarget);
        }
    }
}
