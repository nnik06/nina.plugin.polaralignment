using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaRaRotationWitnessHandshakeTest {
        [Test]
        public void ExactBlindSingleAttemptOutcomePassesWithoutAuthority() {
            var fixture = CreateFixture();

            var result = TppaRaRotationWitnessHandshake.ValidateOutcome(
                fixture.Request, fixture.Outcome);

            result.IsValid.Should().BeTrue(string.Join("; ", result.Issues));
            result.GrantsMotionAuthority.Should().BeFalse();
            result.GrantsCompletionAuthority.Should().BeFalse();
        }

        [Test]
        public void CanonicalRequestSpecProducesTheIdenticalBoundRequest() {
            var expected = CreateFixture().Request;
            var spec = new TppaRaRotationWitnessCaptureRequestSpec(
                expected.RunId,
                expected.PositionId,
                expected.SequenceIndex,
                expected.Nonce,
                expected.MountCommandId,
                expected.MountCommandIssuedUtc,
                expected.MountCommandCompletedUtc,
                expected.RequestedUtc,
                expected.DeadlineUtc,
                expected.CommandedRightAscensionDegrees,
                expected.CommandedDeclinationDegrees,
                expected.ExpectedPierSide,
                expected.SiteLatitudeDegrees,
                expected.SiteLongitudeDegrees,
                expected.SiteElevationMeters,
                expected.ExposureMilliseconds,
                expected.AstapFieldOfViewDegrees,
                expected.FitsTimestampUncertaintyMilliseconds,
                expected.RequiredObserverPipelineDigest,
                expected.RequiredPointCapturePipelineDigest);

            var actual = TppaRaRotationWitnessHandshake.CreateRequest(spec);

            actual.Should().Be(expected);
            actual.GrantsMotionAuthority.Should().BeFalse();
            actual.GrantsCompletionAuthority.Should().BeFalse();
        }

        [Test]
        public void LateOutcomeFailsClosed() {
            var fixture = CreateFixture();
            var outcome = fixture.Outcome with {
                CompletedUtc = fixture.Request.DeadlineUtc.AddMilliseconds(1)
            };

            var result = TppaRaRotationWitnessHandshake.ValidateOutcome(
                fixture.Request, outcome);

            result.IsValid.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("UTC request window"));
        }

        [Test]
        public void ReplayNonceOrSecondAttemptFailsClosed() {
            var fixture = CreateFixture();
            var outcome = fixture.Outcome with {
                Nonce = "replayed-nonce",
                AttemptNumber = 2
            };

            var result = TppaRaRotationWitnessHandshake.ValidateOutcome(
                fixture.Request, outcome);

            result.IsValid.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("exact request"));
            result.Issues.Should().Contain(issue => issue.Contains("retried"));
        }

        [Test]
        public void MountHintOrTamperedPointDigestFailsClosed() {
            var fixture = CreateFixture();
            var point = fixture.Outcome.PointReceipt with {
                SolverHintPolicy = "mount-coordinate-hint"
            };
            var outcome = fixture.Outcome with {
                PointReceipt = point,
                PointReceiptSha256 = new string('a', 64)
            };

            var result = TppaRaRotationWitnessHandshake.ValidateOutcome(
                fixture.Request, outcome);

            result.IsValid.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("digest"));
            result.Issues.Should().Contain(issue => issue.Contains("solved blind"));
        }

        [Test]
        public void ModifiedRequestDigestAndExpiredRequestFailClosed() {
            var fixture = CreateFixture();
            var request = fixture.Request with {
                CommandedDeclinationDegrees = fixture.Request.CommandedDeclinationDegrees + 1
            };

            var result = TppaRaRotationWitnessHandshake.ValidateRequest(
                request, request.DeadlineUtc.AddSeconds(1));

            result.IsValid.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("deadline"));
            result.Issues.Should().Contain(issue => issue.Contains("digest"));
        }

        private static Fixture CreateFixture() {
            var commandIssued = new DateTime(
                2026, 8, 1, 18, 0, 0, DateTimeKind.Utc);
            var commandCompleted = commandIssued.AddSeconds(2);
            var requested = commandCompleted.AddSeconds(1);
            var request = TppaRaRotationWitnessHandshake.CreateRequest(
                Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                "A",
                0,
                "nonce-123",
                "mount-command-0",
                commandIssued,
                commandCompleted,
                requested,
                requested.AddSeconds(45),
                100,
                80,
                "pierWest",
                25.2,
                55.3,
                25,
                1000,
                2.0,
                10,
                new string('b', 64),
                new string('f', 64));
            var observation = requested.AddSeconds(8);
            var point = new TppaRaRotationWitnessPointReceipt(
                TppaRaRotationWitnessPointReceipt.CurrentSchemaVersion,
                request.RunId,
                request.PositionId,
                request.SequenceIndex,
                request.MountCommandId,
                request.MountCommandIssuedUtc,
                request.MountCommandCompletedUtc,
                request.CommandedRightAscensionDegrees,
                request.CommandedDeclinationDegrees,
                true,
                false,
                "Stopped",
                false,
                observation.AddSeconds(-0.5),
                1,
                observation,
                observation.AddSeconds(-0.5),
                TppaAbsoluteEvidenceBinder.FitsDateObsExposureStart,
                10,
                new string('c', 64),
                new string('d', 64),
                "ASTAP-2026.1",
                new string('e', 64),
                TppaAbsoluteEvidenceBinder.BlindNoMountHintSolvePolicy,
                TppaFastQualificationConventions.IcrsObservationEpoch,
                request.SiteLatitudeDegrees,
                request.SiteLongitudeDegrees,
                request.SiteElevationMeters,
                request.AstapFieldOfViewDegrees,
                0,
                45,
                100,
                80,
                0,
                1,
                0,
                0,
                request.ExpectedPierSide);
            var outcome = new TppaRaRotationWitnessCaptureOutcome(
                TppaRaRotationWitnessCaptureOutcome.CurrentSchemaVersion,
                request.RequestDigest,
                request.RunId,
                request.PositionId,
                request.SequenceIndex,
                request.Nonce,
                TppaRaRotationWitnessCaptureOutcome.CapturedStatus,
                1,
                requested.AddSeconds(1),
                observation.AddSeconds(1),
                request.RequiredObserverPipelineDigest,
                TppaRaRotationWitnessHandshake.Sha256Utf8(point.ToJson()),
                point,
                Array.Empty<string>());
            return new(request, outcome);
        }

        private sealed record Fixture(
            TppaRaRotationWitnessCaptureRequest Request,
            TppaRaRotationWitnessCaptureOutcome Outcome);
    }
}
