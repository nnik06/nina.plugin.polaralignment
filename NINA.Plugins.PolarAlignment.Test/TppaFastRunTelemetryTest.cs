using FluentAssertions;
using Newtonsoft.Json.Linq;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaFastRunTelemetryTest {
        [Test]
        public void SerializesDeterministicRunScopedEvent() {
            var runId = Guid.Parse("11111111-2222-3333-4444-555555555555");
            var line = TppaFastRunTelemetry.Serialize(
                runId,
                "post-move-response",
                new DateTime(2026, 8, 3, 1, 2, 3, DateTimeKind.Utc),
                TimeSpan.FromMilliseconds(123456),
                new Dictionary<string, object> {
                    ["postTotalMinutes"] = 2.75,
                    ["classification"] = "ConvergedCandidate"
                });

            line.Should().StartWith(TppaFastRunTelemetry.Marker);
            var json = JObject.Parse(line[TppaFastRunTelemetry.Marker.Length..]);
            json.Value<int>("schemaVersion").Should().Be(1);
            json.Value<string>("runId").Should().Be(runId.ToString("D"));
            json.Value<string>("event").Should().Be("post-move-response");
            line.Should().Contain("\"observedUtc\":\"2026-08-03T01:02:03.0000000Z\"");
            json.Value<double>("elapsedSeconds").Should().Be(123.456);
            json.Value<string>("classification").Should().Be("ConvergedCandidate");
            json.Value<double>("postTotalMinutes").Should().Be(2.75);
        }

        [Test]
        public void RejectsInvalidIdentityTimeAndReservedFieldCollisions() {
            Action emptyId = () => TppaFastRunTelemetry.Serialize(
                Guid.Empty, "started", DateTime.UtcNow, TimeSpan.Zero);
            Action whitespaceEvent = () => TppaFastRunTelemetry.Serialize(
                Guid.NewGuid(), "   ", DateTime.UtcNow, TimeSpan.Zero);
            Action localTime = () => TppaFastRunTelemetry.Serialize(
                Guid.NewGuid(), "started", DateTime.Now, TimeSpan.Zero);
            Action negativeElapsed = () => TppaFastRunTelemetry.Serialize(
                Guid.NewGuid(), "started", DateTime.UtcNow, TimeSpan.FromSeconds(-1));
            Action collision = () => TppaFastRunTelemetry.Serialize(
                Guid.NewGuid(), "started", DateTime.UtcNow, TimeSpan.Zero,
                new Dictionary<string, object> { ["runId"] = "replacement" });

            emptyId.Should().Throw<ArgumentException>();
            whitespaceEvent.Should().Throw<ArgumentException>();
            localTime.Should().Throw<ArgumentException>();
            negativeElapsed.Should().Throw<ArgumentOutOfRangeException>();
            collision.Should().Throw<ArgumentException>();
        }

        [Test]
        public void NonfiniteEvidenceRemainsValidAndExplicitJson() {
            var line = TppaFastRunTelemetry.Serialize(
                Guid.NewGuid(),
                "post-move-response",
                DateTime.UtcNow,
                TimeSpan.Zero,
                new Dictionary<string, object> {
                    ["improvement"] = double.NaN,
                    ["reserve"] = double.PositiveInfinity,
                    ["optional"] = null!
                });

            var json = JObject.Parse(line[TppaFastRunTelemetry.Marker.Length..]);
            json.Value<string>("improvement").Should().Be("NaN");
            json.Value<string>("reserve").Should().Be("Infinity");
            json["optional"]!.Type.Should().Be(JTokenType.Null);
        }
    }
}
