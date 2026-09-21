using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugins.PolarAlignment {
    internal static class TppaMonitoredWaypointSlew {
        public static async Task Run(
                Func<CancellationToken, Task<bool>> slew,
                Action observeEnvelope,
                Action stop,
                TimeSpan timeout,
                CancellationToken token) {
            if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan) {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }
            token.ThrowIfCancellationRequested();
            observeEnvelope();
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
            bounded.CancelAfter(timeout);
            Task<bool> pending = null;
            try {
                pending = slew(bounded.Token);
                while (!pending.IsCompleted) {
                    observeEnvelope();
                    await Task.WhenAny(pending, Task.Delay(100, bounded.Token));
                    bounded.Token.ThrowIfCancellationRequested();
                }
                if (!await pending.WaitAsync(bounded.Token)) {
                    throw new InvalidOperationException("Absolute waypoint slew returned failure.");
                }
                observeEnvelope();
            } catch (Exception failure) {
                bounded.Cancel();
                // A driver may ignore cancellation: stop explicitly and never admit a next move.
                // Observe any eventual asynchronous exception without waiting indefinitely.
                if (pending != null) {
                    _ = pending.ContinueWith(t => { _ = t.Exception; },
                        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted,
                        TaskScheduler.Default);
                }
                try { stop(); }
                catch (Exception stopFailure) {
                    throw new AggregateException("Absolute waypoint failed and StopSlew also failed.",
                        failure, stopFailure);
                }
                if (failure is OperationCanceledException && !token.IsCancellationRequested) {
                    throw new TimeoutException("Absolute waypoint slew exceeded its bounded move-time allowance.", failure);
                }
                throw;
            }
        }
    }
}
