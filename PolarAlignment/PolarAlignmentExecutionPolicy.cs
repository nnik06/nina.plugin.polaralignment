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
        internal const string DriftValidationManualModeIssue = "Drift-validation mode requires automated mount control to visit the A-B-C-A measurement positions. Turn off Manual Mode to continue.";
        internal const string ConflictingDiagnosticModesIssue = "Verification-only mode and drift-validation mode cannot be enabled together. Select one diagnostic mode.";
        private PolarAlignmentExecutionPolicy(bool runSingleFreshVerification,
                                              bool runDriftValidation,
                                              bool allowActuatorConfiguration,
                                              bool allowActuatorPreparation,
                                              bool allowActuatorConnection,
                                              bool allowActuatorMovement,
                                              bool disconnectActuatorOnDispose) {
            RunSingleFreshVerification = runSingleFreshVerification;
            RunDriftValidation = runDriftValidation;
            AllowActuatorConfiguration = allowActuatorConfiguration;
            AllowActuatorPreparation = allowActuatorPreparation;
            AllowActuatorConnection = allowActuatorConnection;
            AllowActuatorMovement = allowActuatorMovement;
            DisconnectActuatorOnDispose = disconnectActuatorOnDispose;
        }

        public bool RunSingleFreshVerification { get; }
        public bool RunDriftValidation { get; }
        public bool AllowActuatorConfiguration { get; }
        public bool AllowActuatorPreparation { get; }
        public bool AllowActuatorConnection { get; }
        public bool AllowActuatorMovement { get; }
        public bool DisconnectActuatorOnDispose { get; }

        public static PolarAlignmentExecutionPolicy Create(bool verificationOnly, bool driftValidationOnly = false) {
            if (verificationOnly && driftValidationOnly) {
                return new PolarAlignmentExecutionPolicy(false, false, false, false, false, false, false);
            }

            if (verificationOnly) {
                return new PolarAlignmentExecutionPolicy(true, false, false, false, false, false, false);
            }

            if (driftValidationOnly) {
                return new PolarAlignmentExecutionPolicy(false, true, false, false, false, false, false);
            }

            return new PolarAlignmentExecutionPolicy(false, false, true, true, true, true, true);
        }

        public static IReadOnlyList<string> GetValidationIssues(bool verificationOnly,
                                                                bool driftValidationOnly,
                                                                bool manualMode) {
            var issues = new List<string>();
            if (verificationOnly && driftValidationOnly) {
                issues.Add(ConflictingDiagnosticModesIssue);
            }
            if (verificationOnly && manualMode) {
                issues.Add(VerificationOnlyManualModeIssue);
            }
            if (driftValidationOnly && manualMode) {
                issues.Add(DriftValidationManualModeIssue);
            }
            return issues;
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
        public int DeterminationCount => 3;
        public int SolvesPerDetermination => 3;
        public int TotalSolveCount => DeterminationCount * SolvesPerDetermination;
        public bool RequiresCorrectionFieldSolve => false;
        public bool ReciprocalStartsAtInitialArcEnd => true;
        public IReadOnlyList<TPoint> ForwardDeterminationStarts => new[] { ArcStart, ArcStart };
    }

    /// <summary>
    /// Executes a forward arc, its reciprocal from the endpoint, and a repeated forward arc.
    /// </summary>
    internal static class VerificationOnlyArcRunner {
        public static async Task<(TDetermination Initial, TDetermination Reciprocal, TDetermination Verification, VerificationOnlyArcPlan<TPoint> Plan)> Run<TPoint, TDetermination>(
            TPoint arcStart,
            Func<CancellationToken, Task<TDetermination>> captureInitial,
            Func<CancellationToken, Task<TDetermination>> captureReciprocal,
            Func<CancellationToken, Task<TDetermination>> captureVerification,
            Func<TPoint, CancellationToken, Task> returnToArcStart,
            CancellationToken token) {
            var initial = await captureInitial(token);
            var plan = new VerificationOnlyArcPlan<TPoint>(arcStart);
            var reciprocal = await captureReciprocal(token);
            await returnToArcStart(plan.ForwardDeterminationStarts[1], token);
            var verification = await captureVerification(token);
            return (initial, reciprocal, verification, plan);
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
                                     Action<Exception> cleanupFailureLogger = null,
                                     bool throwOnCleanupFailure = true) {
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

            if (cleanupFailure != null && throwOnCleanupFailure) {
                ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            } else if (cleanupFailure != null) {
                cleanupFailureLogger?.Invoke(cleanupFailure);
            }
        }
    }
}