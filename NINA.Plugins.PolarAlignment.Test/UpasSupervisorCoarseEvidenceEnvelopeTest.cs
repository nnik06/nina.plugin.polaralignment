using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NINA.Plugins.PolarAlignment;
using NUnit.Framework;
using System;
using System.Linq;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class UpasSupervisorCoarseEvidenceEnvelopeTest {
        private const string Nonce =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        private static string ValidJson => """
            {
              "schemaVersion": 2,
              "supervisorSessionId": "acaa2759-b829-41ec-9e93-59449a3ac38c",
              "supervisorBootId": "a7901d62-e1e8-4cce-bd6a-e3e986d8cd8a",
              "evidenceId": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
              "clientRequestNonce": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "capturedUtc": "2026-08-03T12:00:00.0000000Z",
              "capturedMonotonicNs": 123456789,
              "serverProcessingMilliseconds": 25,
              "validForMilliseconds": 5000,
              "oldestEvidenceAgeMilliseconds": 500,
              "coordinateConvention": "azEastPositive_altUpPositive",
              "hardLimitDegrees": {
                "az": { "minimum": -5.4, "maximum": 5.4 },
                "alt": { "minimum": -5.4, "maximum": 5.4 }
              },
              "operationalReserveDegrees": 1.0,
              "operationalLimitDegrees": {
                "az": { "minimum": -4.4, "maximum": 4.4 },
                "alt": { "minimum": -4.4, "maximum": 4.4 }
              },
              "axes": {},
              "responseCalibration": {},
              "remainingTravelBudgetDegrees": {},
              "activeLeaseId": null,
              "activeTransactionId": null,
              "lockedReason": null,
              "capabilities": {}
            }
            """;

        private static JObject Mutable() => JObject.Parse(ValidJson);

        [Test]
        public void DuplicateTopLevelSchemaVersionIsRejectedBeforeAdmission() {
            var duplicate = ValidJson.Replace(
                "\"schemaVersion\": 2,",
                "\"schemaVersion\": 2, \"schemaVersion\": 2,");

            Action parse = () => UpasSupervisorCoarseEvidenceSchemaGate.ParseSupportedRoot(duplicate);

            parse.Should().Throw<JsonException>();
        }

        [Test]
        public void ValidNonceBoundEnvelopeParsesWithoutMotionAuthority() {
            var envelope = Parse(ValidJson, 100);

            envelope.SchemaVersion.Should().Be(2);
            envelope.ClientRequestNonce.Should().Be(Nonce);
            envelope.ObservedRoundTripMilliseconds.Should().Be(100);
            Action fresh = () => envelope.RequireFreshAt(1000);
            fresh.Should().NotThrow();
            typeof(UpasSupervisorCoarseEvidenceEnvelope).GetMethods()
                .Should().NotContain(method => typeof(IAutomatedMoveExecutor)
                    .IsAssignableFrom(method.ReturnType));
        }

        [Test]
        public void NonceReplayAndInvalidNonceShapeAreRejected() {
            Action replay = () => UpasSupervisorCoarseEvidenceSchemaGate.ParseEnvelope(
                ValidJson, new string('c', 64), 100);
            Action invalidShape = () => UpasSupervisorCoarseEvidenceSchemaGate.ParseEnvelope(
                ValidJson, "ABC", 100);

            replay.Should().Throw<JsonException>().WithMessage("*nonce does not match*");
            invalidShape.Should().Throw<JsonException>().WithMessage("*lowercase hexadecimal*");
        }

        [Test]
        public void ExcessiveClientRoundTripIsRejected() {
            Action parse = () => Parse(ValidJson, 2000.1);

            parse.Should().Throw<JsonException>().WithMessage("*round trip*");
        }

        [Test]
        public void FreshnessIsRecheckedBeforePlanEmission() {
            var envelope = Parse(ValidJson, 100);
            Action fresh = () => envelope.RequireFreshAt(4399.9);
            Action expired = () => envelope.RequireFreshAt(4400.1);

            fresh.Should().NotThrow();
            expired.Should().Throw<JsonException>().WithMessage("*stale*");
        }

        [Test]
        public void PayloadCannotExtendLifetimeBeyondCompiledMaximum() {
            var payload = Mutable();
            payload["validForMilliseconds"] = 5000.1;

            Action parse = () => Parse(payload.ToString(), 100);

            parse.Should().Throw<JsonException>().WithMessage("*lifetime*");
        }

        [Test]
        public void SupervisorClockIsRecordedButNeverComparedToClientClock() {
            var payload = Mutable();
            payload["capturedMonotonicNs"] = long.MaxValue;

            var envelope = Parse(payload.ToString(), 100);

            envelope.CapturedMonotonicNs.Should().Be(long.MaxValue);
        }

        [Test]
        public void SafetyCrossChecksRejectPayloadLimitAndReserveChanges() {
            var limitPayload = Mutable();
            limitPayload["hardLimitDegrees"]!["az"]!["maximum"] = 5.5;
            var reservePayload = Mutable();
            reservePayload["operationalReserveDegrees"] = 0.9;

            Action limit = () => Parse(limitPayload.ToString(), 100);
            Action reserve = () => Parse(reservePayload.ToString(), 100);

            limit.Should().Throw<JsonException>().WithMessage("*cross-check*");
            reserve.Should().Throw<JsonException>().WithMessage("*cross-check*");
        }

        [Test]
        public void UnknownAndMissingTopLevelPropertiesAreRejected() {
            var unknown = Mutable();
            unknown["fallbackAllowed"] = true;
            var missing = Mutable();
            missing.Property("axes")!.Remove();

            Action parseUnknown = () => Parse(unknown.ToString(), 100);
            Action parseMissing = () => Parse(missing.ToString(), 100);

            parseUnknown.Should().Throw<JsonException>().WithMessage("*frozen V2 contract*");
            parseMissing.Should().Throw<JsonException>().WithMessage("*frozen V2 contract*");
        }

        [Test]
        public void InvalidUtcAndImpossibleServerProcessingAreRejected() {
            var localTime = Mutable();
            localTime["capturedUtc"] = "2026-08-03T16:00:00+04:00";
            var impossibleProcessing = Mutable();
            impossibleProcessing["serverProcessingMilliseconds"] = 101;

            Action utc = () => Parse(localTime.ToString(), 100);
            Action processing = () => Parse(impossibleProcessing.ToString(), 100);

            utc.Should().Throw<JsonException>().WithMessage("*UTC timestamp*");
            processing.Should().Throw<JsonException>().WithMessage("*processing duration*");
        }

        [Test]
        public void RequiredNestedContainersCannotBeScalarsOrNull() {
            foreach (var property in new[] {
                "axes", "responseCalibration", "remainingTravelBudgetDegrees", "capabilities"
            }) {
                var payload = Mutable();
                payload[property] = null;
                Action parse = () => Parse(payload.ToString(), 100);
                parse.Should().Throw<JsonException>($"because {property} is a required object");
            }
        }

        [Test]
        public void EnvelopeCannotReferenceMotionTypes() {
            var referencedTypes = typeof(UpasSupervisorCoarseEvidenceEnvelope).Assembly
                .GetTypes()
                .Where(type => type == typeof(UpasSupervisorCoarseEvidenceEnvelope))
                .SelectMany(type => type.GetProperties().Select(property => property.PropertyType)
                    .Concat(type.GetFields().Select(field => field.FieldType)));

            referencedTypes.Should().NotContain(type =>
                typeof(IAutomatedMoveExecutor).IsAssignableFrom(type)
                || typeof(IPolarAlignmentSystemVM).IsAssignableFrom(type));
        }

        private static UpasSupervisorCoarseEvidenceEnvelope Parse(string json, double roundTrip) =>
            UpasSupervisorCoarseEvidenceSchemaGate.ParseEnvelope(json, Nonce, roundTrip);
    }
}
