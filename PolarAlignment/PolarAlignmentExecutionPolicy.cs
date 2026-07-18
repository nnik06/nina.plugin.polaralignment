using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugins.PolarAlignment {
    /// <summary>
    /// Defines which actuator-related phases are permitted for a TPPA instruction run.
    /// </summary>
    internal readonly struct PolarAlignmentExecutionPolicy {
        internal const string VerificationOnlyManualModeIssue = "Verification-only mode requires automated mount control to replay the identical measurement arc. Turn off Manual Mode to continue.";
        private PolarAlignmentExecutionPolicy(bool runSingleFreshVerification,
                                              bool allowActuatorConfiguration,
                                              bool allowActuatorPreparation,
                                              bool allowActuatorConnection,
                                              bool allowActuatorMovement,
                                              bool disconnectActuatorOnDispose) {
            RunSingleFreshVerification = runSingleFreshVerification;
            AllowActuatorConfiguration = allowActuatorConfiguration;
            AllowActuatorPreparation = allowActuatorPreparation;
            AllowActuatorConnection = allowActuatorConnection;
            AllowActuatorMovement = allowActuatorMovement;
            DisconnectActuatorOnDispose = disconnectActuatorOnDispose;
        }

        public bool RunSingleFreshVerification { get; }
        public bool AllowActuatorConfiguration { get; }
        public bool AllowActuatorPreparation { get; }
        public bool AllowActuatorConnection { get; }
        public bool AllowActuatorMovement { get; }
        public bool DisconnectActuatorOnDispose { get; }

        public static PolarAlignmentExecutionPolicy Create(bool verificationOnly) {
            return verificationOnly
                ? new PolarAlignmentExecutionPolicy(true, false, false, false, false, false)
                : new PolarAlignmentExecutionPolicy(false, true, true, true, true, true);
        }

        public static IReadOnlyList<string> GetValidationIssues(bool verificationOnly, bool manualMode) {
            return verificationOnly && manualMode
                ? new[] { VerificationOnlyManualModeIssue }
                : Array.Empty<string>();
        }
    }

    /// <summary>
    /// Selects A for verification-only cleanup once it has been captured, with the pre-run pointing as a fallback.
    /// </summary>
    internal sealed class VerificationOnlyRestoreTarget<TPoint> {
        public VerificationOnlyRestoreTarget(TPoint preRunPointing) {
            Pointing = preRunPointing;
        }

        public TPoint Pointing { get; private set; }
        public bool HasCapturedArcStart { get; private set; }

        public void CaptureArcStart(TPoint arcStart) {
            Pointing = arcStart;
            HasCapturedArcStart = true;
        }
    }

    /// <summary>
    /// Describes the fixed capture and pointing contract for a verification-only run.
    /// </summary>
    internal readonly struct VerificationOnlyArcPlan<TPoint> {
        public VerificationOnlyArcPlan(TPoint arcStart) {
            ArcStart = arcStart;
        }

        public TPoint ArcStart { get; }
        public TPoint RestorePointing => ArcStart;
        public int DeterminationCount => 2;
        public int SolvesPerDetermination => 3;
        public int TotalSolveCount => DeterminationCount * SolvesPerDetermination;
        public bool RequiresCorrectionFieldSolve => false;
        public IReadOnlyList<TPoint> DeterminationStarts => new[] { ArcStart, ArcStart };
    }

    /// <summary>
    /// Executes exactly two three-point determinations from the same captured arc start.
    /// </summary>
    internal static class VerificationOnlyArcRunner {
        public static async Task<(TDetermination Initial, TDetermination Verification, VerificationOnlyArcPlan<TPoint> Plan)> Run<TPoint, TDetermination>(
            TPoint arcStart,
            Func<CancellationToken, Task<TDetermination>> captureInitial,
            Func<CancellationToken, Task<TDetermination>> captureVerification,
            Func<TPoint, CancellationToken, Task> returnToArcStart,
            CancellationToken token) {
            var initial = await captureInitial(token);
            var plan = new VerificationOnlyArcPlan<TPoint>(arcStart);
            await returnToArcStart(plan.DeterminationStarts[1], token);
            var verification = await captureVerification(token);
            return (initial, verification, plan);
        }
    }

    /// <summary>
    /// Runs verification work with pointing cleanup that is independent from caller cancellation.
    /// </summary>
    internal static class VerificationOnlyCleanupRunner {
        public static async Task Run(Func<CancellationToken, Task> operation,
                                     Func<CancellationToken, Task> cleanup,
                                     CancellationToken operationToken,
                                     TimeSpan cleanupTimeout,
                                     Action<Exception> cleanupFailureLogger = null) {
            ExceptionDispatchInfo operationFailure = null;
            try {
                await operation(operationToken);
            } catch (Exception ex) {
                operationFailure = ExceptionDispatchInfo.Capture(ex);
            }

            Exception cleanupFailure = null;
            using (var cleanupCTS = new CancellationTokenSource(cleanupTimeout)) {
                try {
                    await cleanup(cleanupCTS.Token);
                } catch (Exception ex) {
                    cleanupFailure = ex;
                }
            }

            if (operationFailure != null) {
                if (cleanupFailure != null) {
                    cleanupFailureLogger?.Invoke(cleanupFailure);
                }
                operationFailure.Throw();
            }

            if (cleanupFailure != null) {
                ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            }
        }
    }
}