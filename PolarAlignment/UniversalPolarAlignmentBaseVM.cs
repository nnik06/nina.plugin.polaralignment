using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace NINA.Plugins.PolarAlignment {
    public abstract partial class UniversalPolarAlignmentBaseVM : BaseVM, IPolarAlignmentSystemVM {
        protected IPolarAlignmentSystem upa;

        protected abstract IPolarAlignmentSystem CreateSystem();
        protected abstract string SystemName { get; }

        protected UniversalPolarAlignmentBaseVM(IProfileService profileService) : base(profileService) {
            IsNotMoving = true;
        }

        [ObservableProperty]
        private bool connected;

        [ObservableProperty]
        private float positionX;

        [ObservableProperty]
        private float positionY;

        [ObservableProperty]
        private float targetPositionX;

        [ObservableProperty]
        private float targetPositionY;

        public abstract bool DoAutomatedAdjustments { get; set; }
        public abstract double AutomatedAdjustmentSettleTime { get; set; }
        public abstract float XGearRatio { get; set; }
        public abstract int XSpeed { get; set; }
        public abstract float YGearRatio { get; set; }
        public abstract int YSpeed { get; set; }
        public abstract bool ReverseAzimuth { get; set; }
        public abstract bool ReverseAltitude { get; set; }
        public abstract float XBacklashCompensation { get; set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(NudgeXCommand))]
        [NotifyCanExecuteChangedFor(nameof(NudgeYCommand))]
        [NotifyCanExecuteChangedFor(nameof(MoveXCommand))]
        [NotifyCanExecuteChangedFor(nameof(MoveYCommand))]
        private bool isNotMoving;

        private readonly object connectionSync = new();

        private CancellationTokenSource pollCts;

        private Task connectTask;

        private int connectionGeneration;

        // A persisted visual-scale confirmation is the operator's statement about the
        // physical state at session start. The first connection only opens transport;
        // a later reconnect can follow an unknown physical interruption and must clear it.
        private bool hasEstablishedPhysicalConnection;

        [RelayCommand]
        public Task Connect() {
            lock (connectionSync) {
                if (upa?.Connected == true) { return Task.CompletedTask; }
                if (connectTask?.IsCompleted == false) { return connectTask; }
                if (!PolarAlignmentActuatorConnectionGate.TryBeginOperation(out var operationScope)) {
                    Logger.Warning(
                        $"Suppressing {SystemName} connection because a report-only polar-alignment diagnostic is active " +
                        $"(suppression count {PolarAlignmentActuatorConnectionGate.SuppressionCount}).");
                    return Task.CompletedTask;
                }

                if (hasEstablishedPhysicalConnection) {
                    InvalidatePhysicalPositionConfirmation();
                    Logger.Info($"Cleared {SystemName} physical-position confirmation before a reconnect.");
                } else {
                    Logger.Info($"Preserving persisted {SystemName} physical-position confirmation for the first connection in this NINA process.");
                }
                var generation = ++connectionGeneration;
                connectTask = Task.Run(async () => {
                    using (operationScope) {
                        IPolarAlignmentSystem createdSystem = null;
                        try {
                            await Application.Current.Dispatcher.BeginInvoke(() => IsNotMoving = true);
                            createdSystem = CreateSystem();

                            CancellationTokenSource newPollCts;
                            lock (connectionSync) {
                                if (generation != connectionGeneration) {
                                    createdSystem.Dispose();
                                    return;
                                }
                                pollCts?.Cancel();
                                pollCts?.Dispose();
                                upa = createdSystem;
                                newPollCts = new CancellationTokenSource();
                                pollCts = newPollCts;
                            }

                            _ = StartPoll(createdSystem, newPollCts.Token);
                            lock (connectionSync) {
                                if (generation != connectionGeneration ||
                                    !ReferenceEquals(upa, createdSystem)) {
                                    return;
                                }
                                Connected = true;
                                hasEstablishedPhysicalConnection = true;
                            }
                            Notification.ShowInformation($"Successfully connected to {SystemName}");
                        } catch (Exception ex) {
                            lock (connectionSync) {
                                if (ReferenceEquals(upa, createdSystem)) {
                                    pollCts?.Cancel();
                                    pollCts?.Dispose();
                                    pollCts = null;
                                    upa = null;
                                }
                            }
                            createdSystem?.Dispose();
                            Logger.Error(ex);
                            Notification.ShowError($"Unable to connect to {SystemName}");
                        }
                    }
                });
                return connectTask;
            }
        }

        [RelayCommand]
        public void Disconnect() {
            Disconnect(invalidatePhysicalPositionConfirmation: true);
        }

        internal void Disconnect(bool invalidatePhysicalPositionConfirmation) {
            if (invalidatePhysicalPositionConfirmation) {
                InvalidatePhysicalPositionConfirmation();
            } else {
                Logger.Info($"Disconnecting {SystemName} for TPPA view-model disposal while preserving the operator-confirmed physical marker state.");
            }

            IPolarAlignmentSystem disconnectedSystem;
            CancellationTokenSource disconnectedPollCts;
            lock (connectionSync) {
                connectionGeneration++;
                disconnectedSystem = upa;
                upa = null;
                disconnectedPollCts = pollCts;
                pollCts = null;
            }

            Connected = false;
            try {
                disconnectedPollCts?.Cancel();
                disconnectedPollCts?.Dispose();
                disconnectedSystem?.Dispose();
            } catch (Exception ex) {
                Logger.Error(ex);
            }
            Notification.ShowInformation($"Disconnected from {SystemName}");
        }

        private IDisposable BeginActuatorSerialOperation(string action) {
            if (PolarAlignmentActuatorConnectionGate.TryBeginOperation(out var operationScope)) {
                return operationScope;
            }
            throw new InvalidOperationException(
                $"{action} on {SystemName} is suppressed while a report-only polar-alignment diagnostic is active.");
        }

        protected virtual void InvalidatePhysicalPositionConfirmation() {
        }

        [RelayCommand(CanExecute = (nameof(IsNotMoving)))]
        public async Task NudgeX(float position, CancellationToken token) {
            InvalidatePhysicalPositionConfirmation();
            await TryNudgeX(position, token);
        }

        public Task<bool> TryNudgeX(float position, CancellationToken token) {
            return TryNudgeX(position, token, applyBacklashCompensation: true);
        }

        public Task<bool> TryNudgeXForAutomation(float position, CancellationToken token) {
            return TryNudgeX(position, token, applyBacklashCompensation: false);
        }

        private async Task<bool> TryNudgeX(float position, CancellationToken token, bool applyBacklashCompensation) {
            try {
                using var operationScope = BeginActuatorSerialOperation("Movement");
                if (ReverseAzimuth) { position = position * -1; }
                await Application.Current.Dispatcher.BeginInvoke(() => IsNotMoving = false);

                Logger.Info($"Nudging {SystemName} along X axis by {position}");
                var lastDirection = upa.XLastDirection;
                var currentDirection = position >= 0 ? LastDirection.Positive : LastDirection.Negative;
                if (applyBacklashCompensation) {
                    await PreloadBacklash(lastDirection, currentDirection, token);
                } else if (lastDirection != currentDirection && Math.Abs(XBacklashCompensation) > 0) {
                    Logger.Info($"Skipping X backlash compensation for automated {SystemName} nudge so the learned response model only sees the commanded move.");
                }
                await upa.MoveRelative(Axis.XAxis, XSpeed, position, token).ConfigureAwait(false);
                return true;
            } catch (Exception ex) {
                Logger.Error(ex);
                if (ex is TimeoutException) {
                    Notification.ShowError($"Movement timeout: {ex.Message}");
                }
                return false;
            } finally {
                await Application.Current.Dispatcher.BeginInvoke(() => IsNotMoving = true);
            }
        }

        [RelayCommand(CanExecute = (nameof(IsNotMoving)))]
        public async Task NudgeY(float position, CancellationToken token) {
            InvalidatePhysicalPositionConfirmation();
            await TryNudgeY(position, token);
        }

        public async Task<bool> TryNudgeY(float position, CancellationToken token) {
            try {
                using var operationScope = BeginActuatorSerialOperation("Movement");
                if (ReverseAltitude) { position = position * -1; }
                await Application.Current.Dispatcher.BeginInvoke(() => IsNotMoving = false);

                Logger.Info($"Nudging {SystemName} along Y axis by {position}");
                await upa.MoveRelative(Axis.YAxis, YSpeed, position, token).ConfigureAwait(false);
                return true;
            } catch (Exception ex) {
                Logger.Error(ex);
                if (ex is TimeoutException) {
                    Notification.ShowError($"Movement timeout: {ex.Message}");
                }
                return false;
            } finally {
                await Application.Current.Dispatcher.BeginInvoke(() => IsNotMoving = true);
            }
        }

        public new void RaiseAllPropertiesChanged() {
            base.RaiseAllPropertiesChanged();
        }

        [RelayCommand(CanExecute = (nameof(IsNotMoving)))]
        public async Task MoveX(CancellationToken token) {
            try {
                using var operationScope = BeginActuatorSerialOperation("Movement");
                InvalidatePhysicalPositionConfirmation();
                await Application.Current.Dispatcher.BeginInvoke(() => IsNotMoving = false);

                var target = TargetPositionX;
                if (ReverseAzimuth) { target = target * -1; }

                Logger.Info($"Moving {SystemName} along X axis to {target}");
                var lastDirection = upa.XLastDirection;
                var currentDirection = target - upa.XPosition1 >= 0 ? LastDirection.Positive : LastDirection.Negative;
                await PreloadBacklash(lastDirection, currentDirection, token);
                await upa.MoveAbsolute(Axis.XAxis, XSpeed, target, token).ConfigureAwait(false);
            } catch (Exception ex) {
                Logger.Error(ex);
                if (ex is TimeoutException) {
                    Notification.ShowError($"Movement timeout: {ex.Message}");
                }
            } finally {
                await Application.Current.Dispatcher.BeginInvoke(() => IsNotMoving = true);
            }
        }

        private async Task PreloadBacklash(LastDirection lastDirection, LastDirection currentDirection, CancellationToken token) {
            if (lastDirection != currentDirection) {
                if (Math.Abs(XBacklashCompensation) > 0) {
                    Logger.Info("Direction changed. Preloading backlash in new direction before requested X move.");
                    var preload = BacklashCompensationPlanner.CreatePreloadMove(XBacklashCompensation, currentDirection);
                    await upa.MoveRelative(Axis.XAxis, XSpeed, preload, token).ConfigureAwait(false);
                }
            }
        }

        [RelayCommand(CanExecute = (nameof(IsNotMoving)))]
        public async Task MoveY(CancellationToken token) {
            try {
                using var operationScope = BeginActuatorSerialOperation("Movement");
                InvalidatePhysicalPositionConfirmation();
                await Application.Current.Dispatcher.BeginInvoke(() => IsNotMoving = false);

                var target = TargetPositionY;
                if (ReverseAltitude) { target = target * -1; }

                Logger.Info($"Moving {SystemName} along Y axis to {target}");
                await upa.MoveAbsolute(Axis.YAxis, YSpeed, target, token).ConfigureAwait(false);
            } catch (Exception ex) {
                Logger.Error(ex);
                if (ex is TimeoutException) {
                    Notification.ShowError($"Movement timeout: {ex.Message}");
                }
            } finally {
                await Application.Current.Dispatcher.BeginInvoke(() => IsNotMoving = true);
            }
        }

        private async Task StartPoll(IPolarAlignmentSystem system, CancellationToken token) {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false) && !token.IsCancellationRequested) {
                    if (!ReferenceEquals(upa, system)) { return; }
                    if (IsNotMoving &&
                        PolarAlignmentActuatorConnectionGate.TryBeginOperation(out var operationScope)) {
                        using (operationScope) {
                            await system.RefreshStatus(token).ConfigureAwait(false);
                            await Application.Current.Dispatcher.BeginInvoke(() => UpdatePositions(system));
                        }
                    }
                }
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                Logger.Error(ex);
            }
        }

        private void UpdatePositions(IPolarAlignmentSystem system) {
            if (!ReferenceEquals(upa, system)) { return; }
            PositionX = system.XPosition1;
            PositionY = system.YPosition1;
        }
    }
}
