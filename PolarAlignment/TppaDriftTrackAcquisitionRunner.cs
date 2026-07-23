using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaDriftTrackAcquisitionPolicy(
        TimeSpan TargetDuration,
        TimeSpan CadenceDelay,
        int MaximumSampleCount) {
        public static TppaDriftTrackAcquisitionPolicy FieldDefault => new(
            TargetDuration: TimeSpan.FromMinutes(5),
            CadenceDelay: TimeSpan.FromSeconds(5),
            MaximumSampleCount: 1000);
    }

    /// <summary>
    /// Collects one stationary, report-only drift track. Movement between
    /// positions remains the caller's responsibility.
    /// </summary>
    internal static class TppaDriftTrackAcquisitionRunner {
        public static async Task<TppaDriftTrackReport> Run(
            TppaDriftValidationSession session,
            TppaDriftTrackMetadata metadata,
            Func<CancellationToken, Task<TppaDriftSolveSample>> captureSolve,
            TppaDriftTrackAcquisitionPolicy? acquisitionPolicy,
            TppaDeclinationDriftTrackPolicy? qualificationPolicy,
            CancellationToken token,
            Func<TimeSpan, CancellationToken, Task> delay = null) {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(captureSolve);

            var policy = acquisitionPolicy ?? TppaDriftTrackAcquisitionPolicy.FieldDefault;
            var issue = ValidatePolicy(policy);
            if (issue != null) {
                throw new ArgumentOutOfRangeException(nameof(acquisitionPolicy), issue);
            }
            if (!session.TryBeginTrack(metadata.PositionId, out var beginReason)) {
                throw new InvalidOperationException($"Could not begin drift track {metadata.PositionId}: {beginReason}");
            }

            delay ??= static (duration, cancellationToken) => Task.Delay(duration, cancellationToken);
            DateTime? firstObservationTime = null;
            try {
                for (var sampleIndex = 0; sampleIndex < policy.MaximumSampleCount; sampleIndex++) {
                    token.ThrowIfCancellationRequested();
                    var sample = await captureSolve(token);
                    if (!session.TryAddSolve(
                            sample.ObservationTimeUtc,
                            sample.SolvedDeclinationDegrees,
                            out var addReason)) {
                        throw new InvalidOperationException(
                            $"Rejected drift solve {sampleIndex + 1} at {metadata.PositionId}: {addReason}");
                    }

                    firstObservationTime ??= sample.ObservationTimeUtc;
                    if (sample.ObservationTimeUtc - firstObservationTime.Value >= policy.TargetDuration) {
                        return session.CompleteActiveTrack(metadata, qualificationPolicy);
                    }

                    await delay(policy.CadenceDelay, token);
                }

                throw new InvalidOperationException(
                    $"Drift track {metadata.PositionId} did not span {policy.TargetDuration.TotalSeconds:F0}s within {policy.MaximumSampleCount} solves.");
            } catch (OperationCanceledException) {
                session.Invalidate($"drift track {metadata.PositionId} was cancelled");
                throw;
            } catch (Exception ex) {
                session.Invalidate($"drift track {metadata.PositionId} failed: {ex.Message}");
                throw;
            }
        }

        private static string ValidatePolicy(TppaDriftTrackAcquisitionPolicy policy) {
            if (policy.TargetDuration < TimeSpan.FromMinutes(5)
                    || policy.TargetDuration > TimeSpan.FromHours(1)) {
                return "target duration must be between five minutes and one hour";
            }
            if (policy.CadenceDelay < TimeSpan.Zero
                    || policy.CadenceDelay > TimeSpan.FromMinutes(1)) {
                return "cadence delay must be between zero and one minute";
            }
            if (policy.MaximumSampleCount < 3 || policy.MaximumSampleCount > 10000) {
                return "maximum sample count must be between 3 and 10000";
            }
            return null;
        }
    }
}
