using System;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record JogCancellationStatusSample(
        string Status,
        float X,
        float Y,
        float Z);

    internal sealed record JogCancellationResult(
        int Attempts,
        int Confirmations,
        JogCancellationStatusSample FinalSample);

    /// <summary>
    /// Sends one GRBL realtime jog-cancel byte and proves the resulting stop
    /// from a bounded sequence of stable controller observations.
    /// </summary>
    internal static class JogCancellationOrchestrator {
        public static JogCancellationResult Execute(
                Action<byte> sendRealtimeCommand,
                Func<JogCancellationStatusSample> observeStatus,
                Action<TimeSpan> wait,
                int requiredConfirmations,
                int maximumAttempts,
                TimeSpan confirmationInterval) {
            ArgumentNullException.ThrowIfNull(sendRealtimeCommand);
            ArgumentNullException.ThrowIfNull(observeStatus);
            ArgumentNullException.ThrowIfNull(wait);
            if (requiredConfirmations < 1) {
                throw new ArgumentOutOfRangeException(nameof(requiredConfirmations));
            }
            if (maximumAttempts < requiredConfirmations) {
                throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
            }
            if (confirmationInterval < TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(confirmationInterval));
            }

            sendRealtimeCommand(
                UniversalPolarAlignmentBase.GrblJogCancelRealtimeCommand);
            var tracker = new JogCancellationConfirmationTracker(
                requiredConfirmations);
            JogCancellationStatusSample lastSample = null;
            for (var attempt = 1; attempt <= maximumAttempts; attempt++) {
                wait(confirmationInterval);
                lastSample = observeStatus()
                    ?? throw new InvalidOperationException(
                        "GRBL jog-cancellation status observation was missing.");
                if (tracker.Observe(
                        lastSample.Status,
                        (lastSample.X, lastSample.Y, lastSample.Z))) {
                    return new JogCancellationResult(
                        attempt,
                        tracker.Confirmations,
                        lastSample);
                }
            }

            throw new TimeoutException(
                $"GRBL jog cancellation was sent but {requiredConfirmations} " +
                $"stable Idle confirmations were not observed; " +
                $"last status={lastSample?.Status ?? "<none>"}.");
        }
    }
}
