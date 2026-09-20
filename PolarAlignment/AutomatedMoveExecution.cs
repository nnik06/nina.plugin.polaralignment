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
        private readonly IUpasSupervisorRawRelativeClient rawRelativeClient;

        public SupervisorRequiredAutomatedMoveExecutor(
                IUpasSupervisorRawRelativeClient rawRelativeClient) {
            this.rawRelativeClient = rawRelativeClient
                ?? throw new ArgumentNullException(nameof(rawRelativeClient));
        }

        public bool RequiresLocalActuatorConnection => false;

        public async Task<AutomatedMoveExecutionResult> ExecuteAsync(
            IPolarAlignmentSystemVM activeSystem,
            Axis axis,
            float logicalUnits,
            CancellationToken token) {
            if (axis != Axis.XAxis) {
                return AutomatedMoveExecutionResult.Denied(
                    axis == Axis.YAxis
                        ? "External UPAS supervisor altitude motion is disabled because it is not commissioned."
                        : "Unsupported external UPAS supervisor movement axis.");
            }
            if (activeSystem is not UniversalPolarAlignmentBaseVM upas) {
                return AutomatedMoveExecutionResult.Denied(
                    "External UPAS supervisor requires the universal UPAS actuator model.");
            }
            var logicalCommand = upas.ReverseAzimuth ? -logicalUnits : logicalUnits;
            var rawCommand = logicalCommand * upas.XGearRatio;
            if (!float.IsFinite(rawCommand) || rawCommand == 0
                    || rawCommand > int.MaxValue || rawCommand < int.MinValue) {
                return AutomatedMoveExecutionResult.Denied(
                    "TPPA cannot quantize the requested azimuth command to a bounded raw controller count.");
            }
            var rawCount = checked((int)Math.Round(rawCommand, MidpointRounding.AwayFromZero));
            return await rawRelativeClient.ExecuteAzimuthAsync(rawCount, token).ConfigureAwait(false);
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
