using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugins.PolarAlignment {
    /// <summary>
    /// Sequences report-only stationary drift tracks around an A-B-C-A
    /// telescope arc. The caller owns telescope movement; this coordinator
    /// has no polar-alignment actuator dependency.
    /// On failure or cancellation it deliberately leaves the telescope at
    /// its last known position instead of attempting an unverified recovery
    /// slew. The caller remains responsible for restoring tracking and guiding.
    /// </summary>
    internal static class TppaDriftValidationOrchestrator {
        private static readonly string[] PositionOrder = { "A", "B", "C", "A" };

        public static async Task<TppaDriftValidationSessionReport> Run(
            double siteLatitudeDegrees,
            Func<string, CancellationToken, Task> moveToPosition,
            Func<string, CancellationToken, Task<TppaDriftTrackMetadata>> captureMetadata,
            Func<string, CancellationToken, Task<TppaDriftSolveSample>> captureSolve,
            TppaDriftTrackAcquisitionPolicy? acquisitionPolicy,
            TppaDeclinationDriftTrackPolicy? trackQualificationPolicy,
            TppaDriftValidationPolicy? validationPolicy,
            CancellationToken token,
            Func<TimeSpan, CancellationToken, Task> delay = null) {
            ArgumentNullException.ThrowIfNull(moveToPosition);
            ArgumentNullException.ThrowIfNull(captureMetadata);
            ArgumentNullException.ThrowIfNull(captureSolve);
            if (!double.IsFinite(siteLatitudeDegrees) || Math.Abs(siteLatitudeDegrees) > 90) {
                throw new ArgumentOutOfRangeException(
                    nameof(siteLatitudeDegrees),
                    "site latitude must be finite and within -90 to +90 degrees");
            }

            var session = new TppaDriftValidationSession();
            try {
                foreach (var positionId in PositionOrder) {
                    token.ThrowIfCancellationRequested();
                    await moveToPosition(positionId, token);
                    token.ThrowIfCancellationRequested();

                    var metadata = await captureMetadata(positionId, token);
                    if (!string.Equals(metadata.PositionId, positionId, StringComparison.OrdinalIgnoreCase)) {
                        throw new InvalidOperationException(
                            $"Drift metadata position {metadata.PositionId} does not match expected position {positionId}.");
                    }

                    await TppaDriftTrackAcquisitionRunner.Run(
                        session,
                        metadata,
                        solveToken => captureSolve(positionId, solveToken),
                        acquisitionPolicy,
                        trackQualificationPolicy,
                        token,
                        delay);
                }

                return session.Evaluate(siteLatitudeDegrees, validationPolicy);
            } catch (OperationCanceledException) {
                session.Invalidate("A-B-C-A drift validation was cancelled");
                throw;
            } catch (Exception ex) {
                session.Invalidate($"A-B-C-A drift validation failed: {ex.Message}");
                throw;
            }
        }
    }
}
