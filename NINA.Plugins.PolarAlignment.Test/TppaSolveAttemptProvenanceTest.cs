using FluentAssertions;
using Newtonsoft.Json.Linq;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaSolveAttemptProvenanceTest {
        [Test]
        public void FieldRetryBudgetAllowsExactlyFiveAttempts() {
            var policy = TppaSolveRetryPolicy.FieldDefault;

            policy.MaximumAttempts.Should().Be(5);
            Enumerable.Range(0, 5).Should().OnlyContain(completed => policy.CanStartAttempt(completed));
            policy.CanStartAttempt(5).Should().BeFalse();
            policy.CanStartAttempt(-1).Should().BeFalse();
        }

        [Test]
        public void SolveAttemptJsonUsesStableSchemaAndUtcTimestamps() {
            var captureStarted = new DateTime(2026, 7, 26, 1, 2, 3, DateTimeKind.Utc);
            var observation = captureStarted.AddSeconds(1.5);
            var provenance = new TppaSolveAttemptProvenance(
                TppaSolveAttemptProvenance.CurrentSchemaVersion,
                Attempt: 2,
                MaximumAttempts: 5,
                CaptureStartedUtc: captureStarted,
                ObservationTimeUtc: observation,
                ExposureSeconds: 3,
                SearchRadiusDegrees: 7.5,
                MountRightAscensionDegrees: 12.25,
                MountDeclinationDegrees: 44.5,
                SolverType: "ASTAP",
                CaptureSucceeded: true,
                SolveSucceeded: true,
                SolvedRightAscensionDegrees: 12.26,
                SolvedDeclinationDegrees: 44.49,
                FailureKind: null,
                FailureMessage: null);

            var json = JObject.Parse(provenance.ToJson());

            json.Value<int>("schemaVersion").Should().Be(1);
            json.Value<int>("attempt").Should().Be(2);
            json.Value<int>("maximumAttempts").Should().Be(5);
            json.Value<DateTime>("captureStartedUtc").Kind.Should().Be(DateTimeKind.Utc);
            json.Value<DateTime>("observationTimeUtc").Should().Be(observation);
            json.Value<double>("searchRadiusDegrees").Should().Be(7.5);
            json.Value<double>("mountRightAscensionDegrees").Should().Be(12.25);
            json.Value<double>("solvedDeclinationDegrees").Should().Be(44.49);
            json.Value<bool>("captureSucceeded").Should().BeTrue();
            json.Value<bool>("solveSucceeded").Should().BeTrue();
        }

        [Test]
        public void CaptureFailureSerializesUnknownObservationAndSolveValuesAsNull() {
            var captureStarted = new DateTime(2026, 7, 26, 1, 2, 3, DateTimeKind.Utc);
            var provenance = new TppaSolveAttemptProvenance(
                TppaSolveAttemptProvenance.CurrentSchemaVersion,
                Attempt: 1,
                MaximumAttempts: 5,
                CaptureStartedUtc: captureStarted,
                ObservationTimeUtc: null,
                ExposureSeconds: 3,
                SearchRadiusDegrees: 5,
                MountRightAscensionDegrees: null,
                MountDeclinationDegrees: null,
                SolverType: "ASTAP",
                CaptureSucceeded: false,
                SolveSucceeded: false,
                SolvedRightAscensionDegrees: null,
                SolvedDeclinationDegrees: null,
                FailureKind: "capture-exception",
                FailureMessage: "camera unavailable");

            var json = JObject.Parse(provenance.ToJson());

            json.ContainsKey("observationTimeUtc").Should().BeTrue();
            json["observationTimeUtc"]!.Type.Should().Be(JTokenType.Null);
            json["solvedRightAscensionDegrees"]!.Type.Should().Be(JTokenType.Null);
            json["solvedDeclinationDegrees"]!.Type.Should().Be(JTokenType.Null);
            json.Value<string>("failureKind").Should().Be("capture-exception");
            json.Value<string>("failureMessage").Should().Be("camera unavailable");
        }
    }
}
