using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugins.PolarAlignment {
    /// <summary>
    /// Serializes report-only diagnostics against UPAS/OAPA discovery.
    /// Suppression is process-wide because UI commands can execute on another context.
    /// </summary>
    internal static class PolarAlignmentActuatorConnectionGate {
        private static readonly object syncRoot = new();
        private static int suppressionCount;
        private static int activeOperationCount;
        private static TaskCompletionSource<bool> noActiveOperations = CreateCompletedSignal();

        public static bool IsSuppressed => Volatile.Read(ref suppressionCount) > 0;
        internal static int SuppressionCount => Volatile.Read(ref suppressionCount);
        internal static int ActiveOperationCount => Volatile.Read(ref activeOperationCount);

        public static bool TryBeginOperation(out IDisposable operationScope) {
            lock (syncRoot) {
                if (suppressionCount > 0) {
                    operationScope = null;
                    return false;
                }
                if (activeOperationCount == 0) {
                    noActiveOperations = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }
                activeOperationCount++;
                operationScope = new OperationScope();
                return true;
            }
        }

        public static async Task<IDisposable> SuppressAsync(
            TimeSpan activeOperationDrainTimeout,
            CancellationToken token) {
            Task drainTask;
            var scope = new SuppressionScope();
            lock (syncRoot) {
                suppressionCount++;
                drainTask = noActiveOperations.Task;
            }

            try {
                await drainTask.WaitAsync(activeOperationDrainTimeout, token).ConfigureAwait(false);
                return scope;
            } catch (TimeoutException) {
                scope.Dispose();
                throw new TimeoutException(
                    $"A report-only polar-alignment diagnostic could not start because " +
                    $"{ActiveOperationCount} actuator serial operation(s) did not finish within " +
                    $"{activeOperationDrainTimeout.TotalSeconds:0.#} seconds.");
            } catch {
                scope.Dispose();
                throw;
            }
        }

        private static TaskCompletionSource<bool> CreateCompletedSignal() {
            var signal = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            signal.SetResult(true);
            return signal;
        }

        private sealed class OperationScope : IDisposable {
            private int disposed;

            public void Dispose() {
                if (Interlocked.Exchange(ref disposed, 1) != 0) { return; }
                TaskCompletionSource<bool> completedSignal = null;
                lock (syncRoot) {
                    if (activeOperationCount <= 0) {
                        throw new InvalidOperationException("Actuator connection gate underflow.");
                    }
                    activeOperationCount--;
                    if (activeOperationCount == 0) {
                        completedSignal = noActiveOperations;
                    }
                }
                completedSignal?.TrySetResult(true);
            }
        }

        private sealed class SuppressionScope : IDisposable {
            private int disposed;

            public void Dispose() {
                if (Interlocked.Exchange(ref disposed, 1) != 0) { return; }
                lock (syncRoot) {
                    if (suppressionCount <= 0) {
                        throw new InvalidOperationException("Actuator suppression gate underflow.");
                    }
                    suppressionCount--;
                }
            }
        }
    }
}