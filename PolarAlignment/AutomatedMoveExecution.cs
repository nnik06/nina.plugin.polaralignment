using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record AutomatedMoveExecutionResult(bool PhysicalMotionVerified, string Reason) {
        public static AutomatedMoveExecutionResult Verified(string reason) => new(true, reason);
        public static AutomatedMoveExecutionResult Denied(string reason) => new(false, reason);
    }

    internal interface IAutomatedMoveExecutor {
        bool RequiresLocalActuatorConnection { get; }

        Task<AutomatedMoveExecutionResult> ExecuteAsync(
            IPolarAlignmentSystemVM activeSystem,
            Axis axis,
            float logicalUnits,
            CancellationToken token);
    }

    internal interface IExternalSupervisorRequirement {
        bool IsRequired { get; }
    }

    internal sealed class SettingsExternalSupervisorRequirement : IExternalSupervisorRequirement {
        public bool IsRequired => Properties.Settings.Default.RequireExternalUpasSupervisorForAutomatedMoves;
    }

    internal sealed class LegacyAutomatedMoveExecutor : IAutomatedMoveExecutor {
        public bool RequiresLocalActuatorConnection => true;

        public async Task<AutomatedMoveExecutionResult> ExecuteAsync(
            IPolarAlignmentSystemVM activeSystem,
            Axis axis,
            float logicalUnits,
            CancellationToken token) {
            if (activeSystem == null) {
                return AutomatedMoveExecutionResult.Denied("No polar-alignment actuator is selected.");
            }

            var moved = axis switch {
                Axis.XAxis => await activeSystem.TryNudgeXForAutomation(logicalUnits, token).ConfigureAwait(false),
                Axis.YAxis => await activeSystem.TryNudgeY(logicalUnits, token).ConfigureAwait(false),
                _ => false
            };
            return moved
                ? AutomatedMoveExecutionResult.Verified("Legacy actuator reported successful physical movement.")
                : AutomatedMoveExecutionResult.Denied($"Legacy actuator rejected the {axis} movement.");
        }
    }

    internal sealed class SupervisorRequiredAutomatedMoveExecutor : IAutomatedMoveExecutor {
        private readonly IUpasSupervisorStatusSource statusSource;

        public SupervisorRequiredAutomatedMoveExecutor(IUpasSupervisorStatusSource statusSource) {
            this.statusSource = statusSource ?? throw new ArgumentNullException(nameof(statusSource));
        }

        public bool RequiresLocalActuatorConnection => false;

        public async Task<AutomatedMoveExecutionResult> ExecuteAsync(
            IPolarAlignmentSystemVM activeSystem,
            Axis axis,
            float logicalUnits,
            CancellationToken token) {
            _ = activeSystem;
            _ = logicalUnits;

            UpasSupervisorStatus status;
            try {
                status = await statusSource.GetStatusAsync(token).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                return AutomatedMoveExecutionResult.Denied(
                    $"External UPAS supervisor status was unavailable or invalid: {ex.Message}");
            }

            var readiness = UpasSupervisorReadinessPolicy.Evaluate(status, axis);
            if (!readiness.IsReady) {
                return AutomatedMoveExecutionResult.Denied(readiness.Reason);
            }

            // Deliberate commissioning boundary: this release has no correction-submission
            // transport. Even a future success-shaped status cannot reach local hardware.
            return AutomatedMoveExecutionResult.Denied(
                "External UPAS supervisor correction submission is not commissioned in this plugin build.");
        }
    }

    internal sealed class ModeSelectingAutomatedMoveExecutor : IAutomatedMoveExecutor {
        private readonly IExternalSupervisorRequirement requirement;
        private readonly IAutomatedMoveExecutor legacy;
        private readonly IAutomatedMoveExecutor supervisorRequired;

        public ModeSelectingAutomatedMoveExecutor(
            IExternalSupervisorRequirement requirement,
            IAutomatedMoveExecutor legacy,
            IAutomatedMoveExecutor supervisorRequired) {
            this.requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
            this.legacy = legacy ?? throw new ArgumentNullException(nameof(legacy));
            this.supervisorRequired = supervisorRequired ?? throw new ArgumentNullException(nameof(supervisorRequired));
        }

        private IAutomatedMoveExecutor Active => requirement.IsRequired ? supervisorRequired : legacy;

        public bool RequiresLocalActuatorConnection => Active.RequiresLocalActuatorConnection;

        public Task<AutomatedMoveExecutionResult> ExecuteAsync(
            IPolarAlignmentSystemVM activeSystem,
            Axis axis,
            float logicalUnits,
            CancellationToken token) =>
            Active.ExecuteAsync(activeSystem, axis, logicalUnits, token);
    }
}
