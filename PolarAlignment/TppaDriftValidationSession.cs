using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaDriftSolveSample(
        DateTime ObservationTimeUtc,
        double SolvedDeclinationDegrees);

    internal readonly record struct TppaDriftTrackMetadata(
        string PositionId,
        double HourAngleDegrees,
        double AltitudeDegrees,
        bool HasComputedRefractionDrift,
        double ComputedRefractionDriftArcsecondsPerMinute);

    internal readonly record struct TppaDriftTrackReport(
        TppaDriftTrackMetadata Metadata,
        IReadOnlyList<TppaDriftSolveSample> Samples,
        TppaDeclinationDriftTrackFit Fit);

    internal readonly record struct TppaDriftValidationSessionReport(
        IReadOnlyList<TppaDriftTrackReport> Tracks,
        TppaDriftValidationResult Validation,
        bool IsComplete,
        string Reason);

    /// <summary>
    /// Owns one report-only A-B-C-A drift acquisition. The session stores all
    /// raw solves and cannot issue telescope or actuator commands.
    /// </summary>
    internal sealed class TppaDriftValidationSession {
        private static readonly string[] RequiredPositionOrder = { "A", "B", "C", "A" };

        private readonly List<TppaDriftTrackReport> completedTracks = new();
        private readonly List<TppaDriftSolveSample> activeSamples = new();
        private string activePositionId;
        private string invalidReason;

        public IReadOnlyList<TppaDriftTrackReport> CompletedTracks => completedTracks;
        public bool HasActiveTrack => !string.IsNullOrWhiteSpace(activePositionId);

        public bool TryBeginTrack(string positionId, out string reason) {
            if (!string.IsNullOrWhiteSpace(invalidReason)) {
                reason = invalidReason;
                return false;
            }
            if (HasActiveTrack) {
                reason = $"position {activePositionId} is still active";
                return false;
            }
            if (completedTracks.Count >= RequiredPositionOrder.Length) {
                reason = "the A-B-C-A acquisition is already complete";
                return false;
            }

            var requiredPosition = RequiredPositionOrder[completedTracks.Count];
            if (!string.Equals(positionId, requiredPosition, StringComparison.OrdinalIgnoreCase)) {
                reason = $"expected position {requiredPosition}, received {positionId}";
                return false;
            }

            activePositionId = requiredPosition;
            activeSamples.Clear();
            reason = string.Empty;
            return true;
        }

        public bool TryAddSolve(DateTime observationTimeUtc, double solvedDeclinationDegrees, out string reason) {
            if (!HasActiveTrack) {
                reason = "no drift track is active";
                return false;
            }
            if (observationTimeUtc.Kind != DateTimeKind.Utc) {
                reason = "solve observation time must be UTC";
                return false;
            }
            if (!double.IsFinite(solvedDeclinationDegrees) || Math.Abs(solvedDeclinationDegrees) > 90) {
                reason = "solved declination must be finite and within -90 to +90 degrees";
                return false;
            }
            if (activeSamples.Count > 0 && observationTimeUtc <= activeSamples[^1].ObservationTimeUtc) {
                reason = "solve observation times must increase strictly";
                return false;
            }

            activeSamples.Add(new TppaDriftSolveSample(observationTimeUtc, solvedDeclinationDegrees));
            reason = string.Empty;
            return true;
        }

        public TppaDriftTrackReport CompleteActiveTrack(
            TppaDriftTrackMetadata metadata,
            TppaDeclinationDriftTrackPolicy? policy = null) {
            if (!HasActiveTrack) {
                throw new InvalidOperationException("No drift track is active.");
            }
            if (!string.Equals(metadata.PositionId, activePositionId, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException($"Active position is {activePositionId}, but metadata is for {metadata.PositionId}.");
            }

            var rawSamples = activeSamples.ToArray();
            var estimator = new TppaDeclinationDriftTrackEstimator();
            if (rawSamples.Length > 0) {
                var firstTime = rawSamples[0].ObservationTimeUtc;
                var firstDeclination = rawSamples[0].SolvedDeclinationDegrees;
                foreach (var sample in rawSamples) {
                    var elapsedSeconds = (sample.ObservationTimeUtc - firstTime).TotalSeconds;
                    var localDeclinationArcseconds = (sample.SolvedDeclinationDegrees - firstDeclination) * 3600.0;
                    if (!estimator.TryAddSample(elapsedSeconds, localDeclinationArcseconds)) {
                        estimator.Invalidate("raw solve sequence could not be converted to a monotonic local declination track");
                        break;
                    }
                }
            }

            var fit = estimator.Evaluate(policy);
            var report = new TppaDriftTrackReport(metadata, rawSamples, fit);
            completedTracks.Add(report);
            activePositionId = null;
            activeSamples.Clear();
            return report;
        }

        public void Invalidate(string reason) {
            invalidReason = string.IsNullOrWhiteSpace(reason) ? "drift-validation acquisition was invalidated" : reason;
        }

        public TppaDriftValidationSessionReport Evaluate(
            double siteLatitudeDegrees,
            TppaDriftValidationPolicy? policy = null) {
            if (!string.IsNullOrWhiteSpace(invalidReason)) {
                return Incomplete(invalidReason);
            }
            if (HasActiveTrack) {
                return Incomplete($"position {activePositionId} is still active");
            }
            if (completedTracks.Count != RequiredPositionOrder.Length) {
                return Incomplete($"completed {completedTracks.Count}/{RequiredPositionOrder.Length} required tracks");
            }
            if (completedTracks.Any(track => !track.Fit.IsValid)) {
                var failedTrack = completedTracks.First(track => !track.Fit.IsValid);
                return Incomplete($"position {failedTrack.Metadata.PositionId} failed track qualification: {failedTrack.Fit.Reason}");
            }
            if (completedTracks.Any(track => !track.Metadata.HasComputedRefractionDrift)) {
                return Incomplete("every track requires an explicitly computed refraction drift before global validation");
            }

            var tracks = completedTracks.Select(track => track.Fit.ToValidationTrack(
                track.Metadata.PositionId,
                track.Metadata.HourAngleDegrees,
                track.Metadata.AltitudeDegrees,
                track.Metadata.ComputedRefractionDriftArcsecondsPerMinute)).ToArray();
            var validation = TppaDriftValidationEstimator.Evaluate(tracks, siteLatitudeDegrees, policy);
            return new TppaDriftValidationSessionReport(
                completedTracks.ToArray(),
                validation,
                true,
                validation.Reason);
        }

        private TppaDriftValidationSessionReport Incomplete(string reason) => new(
            completedTracks.ToArray(),
            default,
            false,
            reason);
    }
}
