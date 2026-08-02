using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaRaRotationWitnessCaptureRequestSpec(
        Guid RunId,
        string PositionId,
        int SequenceIndex,
        string Nonce,
        string MountCommandId,
        DateTime MountCommandIssuedUtc,
        DateTime MountCommandCompletedUtc,
        DateTime RequestedUtc,
        DateTime DeadlineUtc,
        double CommandedRightAscensionDegrees,
        double CommandedDeclinationDegrees,
        string ExpectedPierSide,
        double SiteLatitudeDegrees,
        double SiteLongitudeDegrees,
        double SiteElevationMeters,
        int ExposureMilliseconds,
        double AstapFieldOfViewDegrees,
        double FitsTimestampUncertaintyMilliseconds,
        string RequiredObserverPipelineDigest,
        string RequiredPointCapturePipelineDigest) {
        public bool GrantsMotionAuthority => false;
        public bool GrantsCompletionAuthority => false;
    }

    internal sealed record TppaRaRotationWitnessCaptureRequest(
        int SchemaVersion,
        string RequestDigest,
        Guid RunId,
        string PositionId,
        int SequenceIndex,
        string Nonce,
        string MountCommandId,
        DateTime MountCommandIssuedUtc,
        DateTime MountCommandCompletedUtc,
        DateTime RequestedUtc,
        DateTime DeadlineUtc,
        double CommandedRightAscensionDegrees,
        double CommandedDeclinationDegrees,
        string ExpectedPierSide,
        double SiteLatitudeDegrees,
        double SiteLongitudeDegrees,
        double SiteElevationMeters,
        int ExposureMilliseconds,
        double AstapFieldOfViewDegrees,
        double FitsTimestampUncertaintyMilliseconds,
        int MaximumAttempts,
        string RequiredObserverPipelineDigest,
        string RequiredPointCapturePipelineDigest,
        string RequiredSolverHintPolicy) {
        public const int CurrentSchemaVersion = 1;
        public bool GrantsMotionAuthority => false;
        public bool GrantsCompletionAuthority => false;
    }

    internal sealed record TppaRaRotationWitnessCaptureOutcome(
        int SchemaVersion,
        string RequestDigest,
        Guid RunId,
        string PositionId,
        int SequenceIndex,
        string Nonce,
        string Status,
        int AttemptNumber,
        DateTime StartedUtc,
        DateTime CompletedUtc,
        string ObserverPipelineDigest,
        string PointReceiptSha256,
        TppaRaRotationWitnessPointReceipt PointReceipt,
        IReadOnlyList<string> Issues) {
        public const int CurrentSchemaVersion = 1;
        public const string CapturedStatus = "captured";
        public bool GrantsMotionAuthority => false;
        public bool GrantsCompletionAuthority => false;
    }

    internal sealed record TppaRaRotationWitnessHandshakeResult(
        bool IsValid,
        IReadOnlyList<string> Issues) {
        public bool GrantsMotionAuthority => false;
        public bool GrantsCompletionAuthority => false;
    }

    internal static class TppaRaRotationWitnessHandshake {
        public static readonly TimeSpan MaximumRequestLifetime =
            TimeSpan.FromSeconds(60);

        private static readonly JsonSerializer Serializer = JsonSerializer.Create(
            new JsonSerializerSettings {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Culture = CultureInfo.InvariantCulture,
                DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
                NullValueHandling = NullValueHandling.Include
            });

        public static TppaRaRotationWitnessCaptureRequest CreateRequest(
                Guid runId,
                string positionId,
                int sequenceIndex,
                string nonce,
                string mountCommandId,
                DateTime mountCommandIssuedUtc,
                DateTime mountCommandCompletedUtc,
                DateTime requestedUtc,
                DateTime deadlineUtc,
                double commandedRightAscensionDegrees,
                double commandedDeclinationDegrees,
                string expectedPierSide,
                double siteLatitudeDegrees,
                double siteLongitudeDegrees,
                double siteElevationMeters,
                int exposureMilliseconds,
                double astapFieldOfViewDegrees,
                double fitsTimestampUncertaintyMilliseconds,
                string requiredObserverPipelineDigest,
                string requiredPointCapturePipelineDigest) {
            var request = new TppaRaRotationWitnessCaptureRequest(
                TppaRaRotationWitnessCaptureRequest.CurrentSchemaVersion,
                new string('0', 64),
                runId,
                positionId,
                sequenceIndex,
                nonce,
                mountCommandId,
                mountCommandIssuedUtc,
                mountCommandCompletedUtc,
                requestedUtc,
                deadlineUtc,
                commandedRightAscensionDegrees,
                commandedDeclinationDegrees,
                expectedPierSide,
                siteLatitudeDegrees,
                siteLongitudeDegrees,
                siteElevationMeters,
                exposureMilliseconds,
                astapFieldOfViewDegrees,
                fitsTimestampUncertaintyMilliseconds,
                MaximumAttempts: 1,
                requiredObserverPipelineDigest,
                requiredPointCapturePipelineDigest,
                TppaAbsoluteEvidenceBinder.BlindNoMountHintSolvePolicy);
            var issues = ValidateRequestFields(request, requestedUtc);
            if (issues.Count > 0) {
                throw new ArgumentException(string.Join("; ", issues));
            }
            return request with { RequestDigest = ComputeRequestDigest(request) };
        }

        public static TppaRaRotationWitnessCaptureRequest CreateRequest(
                TppaRaRotationWitnessCaptureRequestSpec spec) {
            ArgumentNullException.ThrowIfNull(spec);
            return CreateRequest(
                spec.RunId,
                spec.PositionId,
                spec.SequenceIndex,
                spec.Nonce,
                spec.MountCommandId,
                spec.MountCommandIssuedUtc,
                spec.MountCommandCompletedUtc,
                spec.RequestedUtc,
                spec.DeadlineUtc,
                spec.CommandedRightAscensionDegrees,
                spec.CommandedDeclinationDegrees,
                spec.ExpectedPierSide,
                spec.SiteLatitudeDegrees,
                spec.SiteLongitudeDegrees,
                spec.SiteElevationMeters,
                spec.ExposureMilliseconds,
                spec.AstapFieldOfViewDegrees,
                spec.FitsTimestampUncertaintyMilliseconds,
                spec.RequiredObserverPipelineDigest,
                spec.RequiredPointCapturePipelineDigest);
        }

        public static TppaRaRotationWitnessHandshakeResult ValidateRequest(
                TppaRaRotationWitnessCaptureRequest request,
                DateTime nowUtc) {
            var issues = ValidateRequestFields(request, nowUtc);
            if (request != null
                    && request.RequestDigest != ComputeRequestDigest(request)) {
                issues.Add("witness request digest is invalid");
            }
            return new(issues.Count == 0, issues);
        }

        public static TppaRaRotationWitnessHandshakeResult ValidateOutcome(
                TppaRaRotationWitnessCaptureRequest request,
                TppaRaRotationWitnessCaptureOutcome outcome) {
            var issues = new List<string>();
            var requestValidation = ValidateRequest(request, request?.RequestedUtc
                ?? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc));
            issues.AddRange(requestValidation.Issues);
            if (outcome == null) {
                issues.Add("witness capture outcome is missing");
                return new(false, issues);
            }
            if (outcome.SchemaVersion
                    != TppaRaRotationWitnessCaptureOutcome.CurrentSchemaVersion) {
                issues.Add("witness capture outcome schema is unsupported");
            }
            if (request == null) {
                return new(false, issues);
            }
            if (outcome.RequestDigest != request.RequestDigest
                    || outcome.RunId != request.RunId
                    || outcome.PositionId != request.PositionId
                    || outcome.SequenceIndex != request.SequenceIndex
                    || outcome.Nonce != request.Nonce) {
                issues.Add("witness capture outcome does not bind the exact request identity");
            }
            if (outcome.Status != TppaRaRotationWitnessCaptureOutcome.CapturedStatus
                    || outcome.AttemptNumber != 1
                    || request.MaximumAttempts != 1
                    || (outcome.Issues?.Count ?? 0) != 0) {
                issues.Add("witness capture outcome is failed, retried, or contains issues");
            }
            if (!IsUtc(outcome.StartedUtc)
                    || !IsUtc(outcome.CompletedUtc)
                    || outcome.StartedUtc < request.RequestedUtc
                    || outcome.CompletedUtc < outcome.StartedUtc
                    || outcome.CompletedUtc > request.DeadlineUtc) {
                issues.Add("witness capture outcome is outside its immutable UTC request window");
            }
            if (outcome.ObserverPipelineDigest
                    != request.RequiredObserverPipelineDigest) {
                issues.Add("witness observer pipeline digest does not match the request");
            }
            var point = outcome.PointReceipt;
            if (point == null) {
                issues.Add("witness point receipt is missing");
            } else {
                if (outcome.PointReceiptSha256 != Sha256Utf8(point.ToJson())) {
                    issues.Add("witness point receipt digest is invalid");
                }
                if (point.SchemaVersion
                            != TppaRaRotationWitnessPointReceipt.CurrentSchemaVersion
                        || point.RunId != request.RunId
                        || point.PositionId != request.PositionId
                        || point.SequenceIndex != request.SequenceIndex
                        || point.MountCommandId != request.MountCommandId
                        || point.MountCommandIssuedUtc != request.MountCommandIssuedUtc
                        || point.MountCommandCompletedUtc
                            != request.MountCommandCompletedUtc
                        || point.CommandedRightAscensionDegrees
                            != request.CommandedRightAscensionDegrees
                        || point.CommandedDeclinationDegrees
                            != request.CommandedDeclinationDegrees
                        || point.SideOfPier != request.ExpectedPierSide) {
                    issues.Add("witness point receipt does not bind the requested mount state");
                }
                if (point.SolverHintPolicy != request.RequiredSolverHintPolicy
                        || point.SolverHintPolicy
                            != TppaAbsoluteEvidenceBinder.BlindNoMountHintSolvePolicy) {
                    issues.Add("witness point receipt was not solved blind");
                }
                if (point.CaptureStartedUtc < request.RequestedUtc
                        || point.ObservationUtc > request.DeadlineUtc
                        || point.ObservationUtc > outcome.CompletedUtc) {
                    issues.Add("witness point receipt timing is outside the request outcome");
                }
            }
            return new(issues.Count == 0, issues);
        }

        public static string SerializeRequest(
                TppaRaRotationWitnessCaptureRequest request) =>
            Serialize(request);

        public static string SerializeOutcome(
                TppaRaRotationWitnessCaptureOutcome outcome) =>
            Serialize(outcome);

        public static string ComputeRequestDigest(
                TppaRaRotationWitnessCaptureRequest request) {
            if (request == null) { throw new ArgumentNullException(nameof(request)); }
            return Sha256Utf8(Serialize(request with {
                RequestDigest = new string('0', 64)
            }));
        }

        public static string Sha256Utf8(string value) =>
            Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(value ?? string.Empty)))
                .ToLowerInvariant();

        private static List<string> ValidateRequestFields(
                TppaRaRotationWitnessCaptureRequest request,
                DateTime nowUtc) {
            var issues = new List<string>();
            if (request == null) {
                issues.Add("witness capture request is missing");
                return issues;
            }
            if (request.SchemaVersion
                    != TppaRaRotationWitnessCaptureRequest.CurrentSchemaVersion) {
                issues.Add("witness capture request schema is unsupported");
            }
            if (request.RunId == Guid.Empty
                    || string.IsNullOrWhiteSpace(request.PositionId)
                    || string.IsNullOrWhiteSpace(request.Nonce)
                    || string.IsNullOrWhiteSpace(request.MountCommandId)) {
                issues.Add("witness request identity is incomplete");
            }
            if (request.SequenceIndex < 0 || request.SequenceIndex > 3
                    || request.PositionId != new[] { "A", "B", "C", "A" }[
                        Math.Clamp(request.SequenceIndex, 0, 3)]) {
                issues.Add("witness request position is not in A/B/C/A order");
            }
            if (!IsUtc(request.MountCommandIssuedUtc)
                    || !IsUtc(request.MountCommandCompletedUtc)
                    || !IsUtc(request.RequestedUtc)
                    || !IsUtc(request.DeadlineUtc)
                    || !IsUtc(nowUtc)
                    || request.MountCommandCompletedUtc
                        < request.MountCommandIssuedUtc
                    || request.RequestedUtc < request.MountCommandCompletedUtc
                    || request.DeadlineUtc <= request.RequestedUtc
                    || request.DeadlineUtc - request.RequestedUtc
                        > MaximumRequestLifetime
                    || nowUtc > request.DeadlineUtc) {
                issues.Add("witness request UTC timing or deadline is invalid");
            }
            if (!double.IsFinite(request.CommandedRightAscensionDegrees)
                    || request.CommandedRightAscensionDegrees < 0
                    || request.CommandedRightAscensionDegrees >= 360
                    || !double.IsFinite(request.CommandedDeclinationDegrees)
                    || request.CommandedDeclinationDegrees < -90
                    || request.CommandedDeclinationDegrees > 90) {
                issues.Add("witness request commanded coordinates are invalid");
            }
            if (!double.IsFinite(request.SiteLatitudeDegrees)
                    || request.SiteLatitudeDegrees < -90
                    || request.SiteLatitudeDegrees > 90
                    || !double.IsFinite(request.SiteLongitudeDegrees)
                    || request.SiteLongitudeDegrees < -180
                    || request.SiteLongitudeDegrees > 180
                    || !double.IsFinite(request.SiteElevationMeters)
                    || request.SiteElevationMeters < -500
                    || request.SiteElevationMeters > 10000
                    || request.ExposureMilliseconds < 100
                    || request.ExposureMilliseconds > 30000
                    || !double.IsFinite(request.AstapFieldOfViewDegrees)
                    || request.AstapFieldOfViewDegrees < 0.1
                    || request.AstapFieldOfViewDegrees > 20
                    || !double.IsFinite(
                        request.FitsTimestampUncertaintyMilliseconds)
                    || request.FitsTimestampUncertaintyMilliseconds < 0
                    || request.FitsTimestampUncertaintyMilliseconds > 1000) {
                issues.Add("witness request site or acquisition settings are invalid");
            }
            if (request.ExpectedPierSide is not ("pierEast" or "pierWest")) {
                issues.Add("witness request expected pier side is invalid");
            }
            if (request.MaximumAttempts != 1) {
                issues.Add("witness request must declare exactly one attempt");
            }
            if (!IsSha256(request.RequiredObserverPipelineDigest)) {
                issues.Add("witness request observer pipeline digest is invalid");
            }
            if (!IsSha256(request.RequiredPointCapturePipelineDigest)) {
                issues.Add("witness request point-capture pipeline digest is invalid");
            }
            if (request.RequiredSolverHintPolicy
                    != TppaAbsoluteEvidenceBinder.BlindNoMountHintSolvePolicy) {
                issues.Add("witness request does not require a blind solve");
            }
            return issues;
        }

        private static string Serialize(object value) =>
            JObject.FromObject(value, Serializer).ToString(Formatting.None);

        private static bool IsUtc(DateTime value) =>
            value.Kind == DateTimeKind.Utc;

        private static bool IsSha256(string value) =>
            value?.Length == 64 && value.All(Uri.IsHexDigit);
    }
}
