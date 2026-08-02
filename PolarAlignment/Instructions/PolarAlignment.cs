using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Core.Utility.WindowService;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyWeatherData;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Equipment.Model;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using NINA.PlateSolving;
using NINA.PlateSolving.Interfaces;
using NINA.Plugin.Interfaces;
using NINA.Plugins.PolarAlignment.Dockables;
using NINA.Plugins.PolarAlignment.Properties;
using NINA.Profile.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using NINA.WPF.Base.Mediator;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static NINA.Equipment.Model.CaptureSequence;
using static NINA.Plugins.PolarAlignment.Dockables.DockablePolarAlignmentVM;

namespace NINA.Plugins.PolarAlignment.Instructions {
    /// <summary>
    /// This Item shows the basic principle on how to add a new Sequence Item to the N.I.N.A. sequencer via the plugin interface
    /// For ease of use this item inherits the abstract SequenceItem which already handles most of the running logic, like logging, exception handling etc.
    /// A complete custom implementation by just implementing ISequenceItem is possible too
    /// The following MetaData can be set to drive the initial values
    /// --> Name - The name that will be displayed for the item
    /// --> Description - a brief summary of what the item is doing. It will be displayed as a tooltip on mouseover in the application
    /// --> Icon - a string to the key value of a Geometry inside N.I.N.A.'s geometry resources
    ///
    /// If the item has some preconditions that should be validated, it shall also extend the IValidatable interface and add the validation logic accordingly.
    /// </summary>
    [ExportMetadata("Name", "Three Point Polar Alignment")]
    [ExportMetadata("Description", "Three Position Auto Polar Alignment anywhere in the sky")]
    [ExportMetadata("Icon", "ThreePointsSVG")]
    [ExportMetadata("Category", "Polar Alignment")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class PolarAlignment : SequenceItem, IValidatable, ISubscriber {
        private IProfileService profileService;
        private ICameraMediator cameraMediator;
        private IImagingMediator imagingMediator;
        private IFilterWheelMediator fwMediator;
        private ITelescopeMediator telescopeMediator;
        private IWindowService windowService;
        private readonly IMessageBroker messageBroker;
        private readonly IGuiderMediator guiderMediator;
        private IPlateSolverFactory plateSolverFactory;
        private IDomeMediator domeMediator;
        private IWeatherDataMediator weatherDataMediator;
        private PauseTokenSource pauseTS;
        private double moveRate;
        private double searchRadius;
        private int targetDistance;
        private bool eastDirection;
        private bool mountMotionEnvelopeEnabled;
        private double mountMotionMinimumAltitudeDegrees = -90;
        private double mountMotionMaximumAltitudeDegrees = 90;
        private double mountMotionAzimuthStartDegrees;
        private double mountMotionAzimuthEndDegrees = 360;
        private bool manualMode;
        private bool startFromCurrentPosition;
        private bool verificationOnly;
        private bool overdeterminedShadowModelCheck;
        private double verificationPointSettleTimeSeconds;
        private bool driftValidationOnly;
        private const double MinimumPositiveAlignmentTolerance = 0.5;
        private double alignmentTolerance;
        private bool enforceFiveMinuteRuntimeBudget;
        private IList<string> issues = new List<string>();
        private const string ResumeAlignmentTopic = $"{nameof(PolarAlignmentPlugin)}_{nameof(PolarAlignment)}_ResumeAlignment";
        private const string PauseAlignmentTopic = $"{nameof(PolarAlignmentPlugin)}_{nameof(PolarAlignment)}_PauseAlignment";        

        [OnDeserializing]
        public void OnDeserializing(StreamingContext context) {

        }

        [OnDeserialized]
        public void OnDeserialized(StreamingContext context) {
            MatchFilter();
        }

        private void MatchFilter() {
            try {
                var idx = this.Filter?.Position ?? -1;
                this.Filter = this.profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters?.FirstOrDefault(x => x.Name == this.Filter?.Name);
                if (this.Filter == null && idx >= 0) {
                    this.Filter = this.profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters?.FirstOrDefault(x => x.Position == idx);
                }
            } catch (Exception ex) {
                Logger.Error(ex);
            }
        }

        [ImportingConstructor]
        public PolarAlignment(
            IProfileService profileService,
            ICameraMediator cameraMediator,
            IImagingMediator imagingMediator,
            IFilterWheelMediator fwMediator,
            ITelescopeMediator telescopeMediator,
            IPlateSolverFactory plateSolverFactory,
            IDomeMediator domeMediator,
            IWeatherDataMediator weatherDataMediator,
            IMessageBroker messageBroker,
            IGuiderMediator guiderMediator) : this(profileService, cameraMediator, imagingMediator, fwMediator, telescopeMediator, plateSolverFactory, domeMediator, weatherDataMediator, new CustomWindowService(), messageBroker, guiderMediator) {
            
            Filter = null;
        }

        public PolarAlignment(
            IProfileService profileService,
            ICameraMediator cameraMediator,
            IImagingMediator imagingMediator,
            IFilterWheelMediator fwMediator,
            ITelescopeMediator telescopeMediator,
            IPlateSolverFactory plateSolverFactory,
            IDomeMediator domeMediator,
            IWeatherDataMediator weatherDataMediator,
            IWindowService windowService,
            IMessageBroker messageBroker,
            IGuiderMediator guiderMediator) {
            
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.imagingMediator = imagingMediator;
            this.fwMediator = fwMediator;
            this.telescopeMediator = telescopeMediator;
            this.windowService = windowService;
            this.messageBroker = messageBroker;
            this.guiderMediator = guiderMediator;
            this.plateSolverFactory = plateSolverFactory;
            this.domeMediator = domeMediator;
            this.weatherDataMediator = weatherDataMediator;

            if (windowService is DummyService) {
                Filter = profileService.ActiveProfile.PlateSolveSettings.Filter;
            }
            Gain = profileService.ActiveProfile.PlateSolveSettings.Gain;
            Offset = -1;
            Binning = new BinningMode(profileService.ActiveProfile.PlateSolveSettings.Binning, profileService.ActiveProfile.PlateSolveSettings.Binning);
            ExposureTime = profileService.ActiveProfile.PlateSolveSettings.ExposureTime;

            EastDirection = Properties.Settings.Default.DefaultEastDirection;
            MoveRate = Properties.Settings.Default.DefaultMoveRate;
            TargetDistance = Properties.Settings.Default.DefaultTargetDistance;
            SearchRadius = Properties.Settings.Default.DefaultSearchRadius;
            AlignmentTolerance = Properties.Settings.Default.AlignmentTolerance;
            EnforceFiveMinuteRuntimeBudget = true;

            CameraInfo = this.cameraMediator.GetInfo();

            if (Northern) {
                Coordinates = new InputTopocentricCoordinates(new TopocentricCoordinates(Angle.ByDegree(Properties.Settings.Default.DefaultAzimuthOffset), Latitude + Angle.ByDegree(Properties.Settings.Default.DefaultAltitudeOffset), Latitude, Longitude, profileService.ActiveProfile.AstrometrySettings.Elevation, new SystemDateTime()));
            } else {
                Coordinates = new InputTopocentricCoordinates(new TopocentricCoordinates(Angle.ByDegree(180 + Properties.Settings.Default.DefaultAzimuthOffset), Angle.ByDegree(Math.Abs(Latitude.Degree)) + Angle.ByDegree(Properties.Settings.Default.DefaultAltitudeOffset), Latitude, Longitude, profileService.ActiveProfile.AstrometrySettings.Elevation, new SystemDateTime()));
            }

            TPAPAVM = new TPAPAVM(profileService, weatherDataMediator);
            this.messageBroker.Subscribe(ResumeAlignmentTopic, this);
            this.messageBroker.Subscribe(PauseAlignmentTopic, this);
        }

        private PolarAlignment(PolarAlignment copyMe) : this(copyMe.profileService, copyMe.cameraMediator, copyMe.imagingMediator, copyMe.fwMediator, copyMe.telescopeMediator, copyMe.plateSolverFactory, copyMe.domeMediator, copyMe.weatherDataMediator, copyMe.messageBroker, copyMe.guiderMediator) {
            CopyMetaData(copyMe);
        }

        /// <summary>
        /// When items are put into the sequence via the factory, the factory will call the clone method. Make sure all the relevant fields are cloned with the object.
        /// </summary>
        /// <returns></returns>
        public override object Clone() {
            var clone = new PolarAlignment(this) {
                MoveRate = MoveRate,
                Filter = Filter,
                TargetDistance = TargetDistance,
                EastDirection = EastDirection,
                MountMotionEnvelopeEnabled = MountMotionEnvelopeEnabled,
                MountMotionMinimumAltitudeDegrees = MountMotionMinimumAltitudeDegrees,
                MountMotionMaximumAltitudeDegrees = MountMotionMaximumAltitudeDegrees,
                MountMotionAzimuthStartDegrees = MountMotionAzimuthStartDegrees,
                MountMotionAzimuthEndDegrees = MountMotionAzimuthEndDegrees,
                ExposureTime = ExposureTime,
                Binning = Binning == null ? null : new BinningMode(Binning.X, Binning.Y),
                Gain = Gain,
                Offset = Offset,
                ManualMode = ManualMode,
                StartFromCurrentPosition = StartFromCurrentPosition,
                VerificationOnly = VerificationOnly,
                OverdeterminedShadowModelCheck = OverdeterminedShadowModelCheck,
                VerificationPointSettleTimeSeconds = VerificationPointSettleTimeSeconds,
                DriftValidationOnly = DriftValidationOnly,

                AlignmentTolerance = AlignmentTolerance,
                EnforceFiveMinuteRuntimeBudget = EnforceFiveMinuteRuntimeBudget,
                Coordinates = this.Coordinates == null
                    ? null
                    : new InputTopocentricCoordinates(this.Coordinates.Coordinates.Copy())
            };

            if (clone.Binning == null) {
                Binning = new BinningMode(profileService.ActiveProfile.PlateSolveSettings.Binning, profileService.ActiveProfile.PlateSolveSettings.Binning);
            }

            return clone;
        }

        [JsonProperty]
        public InputTopocentricCoordinates Coordinates { get; set; }

        [JsonProperty]
        public double MoveRate {
            get => moveRate;
            set {
                moveRate = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public double SearchRadius {
            get => searchRadius;
            set {
                searchRadius = Math.Max(30, Math.Min(180, value));
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public bool EastDirection {
            get => eastDirection;
            set {
                eastDirection = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public int TargetDistance {
            get => targetDistance;
            set {
                targetDistance = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public bool MountMotionEnvelopeEnabled {
            get => mountMotionEnvelopeEnabled;
            set {
                mountMotionEnvelopeEnabled = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public double MountMotionMinimumAltitudeDegrees {
            get => mountMotionMinimumAltitudeDegrees;
            set {
                mountMotionMinimumAltitudeDegrees = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public double MountMotionMaximumAltitudeDegrees {
            get => mountMotionMaximumAltitudeDegrees;
            set {
                mountMotionMaximumAltitudeDegrees = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public double MountMotionAzimuthStartDegrees {
            get => mountMotionAzimuthStartDegrees;
            set {
                mountMotionAzimuthStartDegrees = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public double MountMotionAzimuthEndDegrees {
            get => mountMotionAzimuthEndDegrees;
            set {
                mountMotionAzimuthEndDegrees = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public bool ManualMode {
            get => manualMode;
            set {
                manualMode = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public bool StartFromCurrentPosition {
            get => startFromCurrentPosition;
            set {
                startFromCurrentPosition = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public bool VerificationOnly {
            get => verificationOnly;
            set {
                verificationOnly = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(IsDiagnosticOnlyMode));
            }
        }

        [JsonProperty]
        public bool OverdeterminedShadowModelCheck {
            get => overdeterminedShadowModelCheck;
            set {
                overdeterminedShadowModelCheck = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public double VerificationPointSettleTimeSeconds {
            get => verificationPointSettleTimeSeconds;
            set {
                verificationPointSettleTimeSeconds = double.IsFinite(value)
                    ? Math.Clamp(
                        value,
                        0.0,
                        TppaVerificationSettlePolicy.MaximumOverrideSeconds)
                    : 0.0;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public bool DriftValidationOnly {
            get => driftValidationOnly;
            set {
                driftValidationOnly = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(IsDiagnosticOnlyMode));
            }
        }

        public bool IsDiagnosticOnlyMode => VerificationOnly || DriftValidationOnly;

        [JsonProperty]
        public double AlignmentTolerance {
            get => alignmentTolerance;
            set {
                if (value < 0) {
                    value = 0;
                } else if (value > 0 && value < MinimumPositiveAlignmentTolerance) {
                    value = MinimumPositiveAlignmentTolerance;
                }

                alignmentTolerance = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public bool EnforceFiveMinuteRuntimeBudget {
            get => enforceFiveMinuteRuntimeBudget;
            set {
                enforceFiveMinuteRuntimeBudget = value;
                RaisePropertyChanged();
            }
        }



        private ApplicationStatus GetStatus(string status) {
            return new ApplicationStatus { Source = "TPPA", Status = status };
        }

        public NINA.Plugins.PolarAlignment.Avalon.UniversalPolarAlignmentVM UniversalPolarAlignmentVM => PolarAlignmentPlugin.UniversalPolarAlignmentVM;

        public bool ShowUpasRunSettings => PolarAlignmentPlugin.ActiveAlignmentSystemVM is NINA.Plugins.PolarAlignment.Avalon.UniversalPolarAlignmentVM;

        private TPAPAVM tpapa;
        public TPAPAVM TPAPAVM {
            get => tpapa;
            set {
                tpapa = value;
                RaisePropertyChanged();
            }
        }

        private async Task<PlateSolveResult> AutomatedNextPoint(IProgress<ApplicationStatus> progress, CancellationToken token, bool? eastDirectionOverride = null) {
            PlateSolveResult solve;
            var totalDistance = (double)TargetDistance;
            var previousMountRADegrees = telescopeMediator.GetCurrentPosition().RADegrees;

            await WaitIfPaused(token, progress);
            var settleOverride = TppaVerificationSettlePolicy.ResolveSequenceOverride(
                VerificationPointSettleTimeSeconds);
            await MoveToNextPoint(
                totalDistance, MoveRate, eastDirectionOverride ?? EastDirection,
                progress, token, settleOverride);

            if (domeMediator.GetInfo().Connected) {
                await domeMediator.WaitForDomeSynchronization(token);
            }

            solve = await Solve(TPAPAVM, 5.0, progress, token);

            var distance = Distance(previousMountRADegrees, telescopeMediator.GetCurrentPosition().RADegrees);
            if (distance - totalDistance < -1) {

                Logger.Warning($"The mount did not move far enough to reach the target distance for the next point ({Math.Round(distance, 2)}°/{Math.Round(totalDistance, 2)}°).");
                Notification.ShowWarning($"The mount did not move far enough to reach the target distance for the next point ({Math.Round(distance, 2)}°/{Math.Round(totalDistance, 2)}°).{Environment.NewLine}This will happen when the mount driver's rate implementation is not according to the specifications to be degrees per seconds!{Environment.NewLine}Tip: Increase the slew rate and adjust the timeout setting inside the plugin options.");
            }

            return solve;
        }

        private async Task<PlateSolveResult> ManualNextPoint(PlateSolveResult previousSolve, IProgress<ApplicationStatus> progress, CancellationToken token) {
            PlateSolveResult solve = null;
            bool traveledFarEnough;
            var totalDistance = (double)TargetDistance;

            var previousMountRADegrees = double.NaN;

            bool telescopeConnected = telescopeMediator.GetInfo().Connected;

            if (telescopeConnected) {
                previousMountRADegrees = telescopeMediator.GetCurrentPosition().RADegrees;
            }

            do {
                await WaitIfPaused(token, progress);
                double distance;
                if (telescopeConnected) {
                    distance = Distance(previousMountRADegrees, telescopeMediator.GetCurrentPosition().RADegrees);
                } else {
                    // Without telescope connection, the coordinates need to be determined via solving
                    solve = await Solve(TPAPAVM, 5.0, progress, token);
                    distance = Distance(previousSolve.Coordinates.RADegrees, solve.Coordinates.RADegrees);
                }
                if (distance - totalDistance < -1) {
                    traveledFarEnough = false;

                    progress.Report(new ApplicationStatus() { Status = $"Move mount along RA axis! {Math.Round(distance, 2)}°/{Math.Round(totalDistance, 2)}°" });
                    await Task.Delay(TimeSpan.FromSeconds(1));
                } else {
                    traveledFarEnough = true;
                }
            } while (!traveledFarEnough);

            if (telescopeConnected) {
                while (telescopeMediator.GetInfo().Slewing) {
                    progress.Report(new ApplicationStatus() { Status = "Moved far enough. Stop axis rotation now!" });
                    await Task.Delay(500, token);
                }
                await CoreUtil.Wait(TimeSpan.FromSeconds(profileService.ActiveProfile.TelescopeSettings.SettleTime), token, progress, "Settling");
                SetTrackingSidereal(true);
                if (domeMediator.GetInfo().Connected) {
                    progress.Report(new ApplicationStatus() { Status = $"Waiting for dome to synchronize" });
                    await domeMediator.WaitForDomeSynchronization(token);
                }
                solve = await Solve(TPAPAVM, 5.0, progress, token);
            }

            return solve;
        }

        private void SetTrackingSidereal(bool onOff) {
            try {

                telescopeMediator.SetTrackingMode(Equipment.Interfaces.TrackingMode.Sidereal);
            } catch (Exception) { }
            try {
                telescopeMediator.SetTrackingEnabled(onOff);
            } catch (Exception) { }
        }

        public void Pause() {
            if (pauseTS != null) {
                pauseTS.IsPaused = true;
                RaisePropertyChanged(nameof(IsPaused));
            }
        }
        public void Resume() {
            if (pauseTS != null) {
                pauseTS.IsPaused = false;
                RaisePropertyChanged(nameof(IsPaused));
            }
        }

        private bool isPaused;
        public bool IsPaused {
            get => isPaused;
            private set {
                isPaused = value;
                RaisePropertyChanged();
            }
        }
        public bool IsPausing { get => pauseTS?.IsPaused ?? false; }

        /// <summary>
        /// The core logic when the sequence item is running resides here
        /// Add whatever action is necessary
        /// </summary>
        /// <param name="progress">The application status progress that can be sent back during execution</param>
        /// <param name="token">When a cancel signal is triggered from outside, this token can be used to register to it or check if it is cancelled</param>
        /// <returns></returns>
        public override async Task Execute(IProgress<ApplicationStatus> externalProgress, CancellationToken token) {
            var alignmentRuntime = Stopwatch.StartNew();
            var executionPolicy = PolarAlignmentExecutionPolicy.Create(VerificationOnly, DriftValidationOnly);
            var automatedAdjustmentsEnabled =
                PolarAlignmentPlugin.ActiveAlignmentSystemVM?.DoAutomatedAdjustments == true
                || Properties.Settings.Default.DoAutomatedAdjustments;
            var enforceFastRuntimeBudget = EnforceFiveMinuteRuntimeBudget
                && automatedAdjustmentsEnabled
                && executionPolicy.AllowActuatorMovement;
            if (enforceFastRuntimeBudget) {
                var resolvedSettleSeconds = TppaVerificationSettlePolicy.Resolve(
                    profileService.ActiveProfile.TelescopeSettings.SettleTime,
                    VerificationPointSettleTimeSeconds);
                var fastConfiguration = TppaFastAlignmentExecutionBudget.EvaluateConfiguration(
                    resolvedSettleSeconds,
                    ExposureTime,
                    Properties.Settings.Default.AutoPause);
                Logger.Info(
                    $"TPPA_FAST_RUNTIME_CONFIGURATION eligible={fastConfiguration.IsEligible}; " +
                    $"settleSeconds={fastConfiguration.ResolvedSettleSeconds:F3}; " +
                    $"exposureSeconds={fastConfiguration.ExposureSeconds:F3}; " +
                    $"autoPause={fastConfiguration.AutoPauseEnabled}; " +
                    $"reason={fastConfiguration.Reason}.");
                if (!fastConfiguration.IsEligible) {
                    throw new SequenceEntityFailedException(
                        $"Automated polar alignment cannot satisfy the five-minute runtime contract: " +
                        $"{fastConfiguration.Reason}. No UPAS connection or movement was authorized.");
                }
            }
            using var fastRuntimeDeadlineCTS = enforceFastRuntimeBudget
                ? new CancellationTokenSource()
                : null;

            void EnsureFastRuntimeBudget(string operation, double reserveSeconds) {
                if (!enforceFastRuntimeBudget) {
                    return;
                }
                var decision = TppaFastAlignmentExecutionBudget.Evaluate(
                    alignmentRuntime.Elapsed,
                    reserveSeconds);
                Logger.Info(
                    $"TPPA_FAST_RUNTIME_BUDGET operation={operation}; " +
                    $"elapsedSeconds={decision.ElapsedSeconds:F1}; " +
                    $"remainingSeconds={decision.RemainingSeconds:F1}; " +
                    $"requiredReserveSeconds={decision.RequiredReserveSeconds:F1}; " +
                    $"allowed={decision.CanStart}.");
                if (!decision.CanStart) {
                    throw new SequenceEntityFailedException(
                        $"Automated polar alignment cannot complete '{operation}' " +
                        $"inside the five-minute runtime contract: {decision.Reason}. " +
                        "No additional UPAS movement was authorized.");
                }
            }
            var poleTargetIssue = RefractionAlignmentTarget.GetValidationIssues(
                Properties.Settings.Default.RefractionAdjustment,
                automatedAdjustmentsEnabled,
                executionPolicy.AllowActuatorMovement,
                DriftValidationOnly).FirstOrDefault();
            if (poleTargetIssue != null) {
                throw new InvalidOperationException(poleTargetIssue);
            }
            if (executionPolicy.AllowActuatorMovement && automatedAdjustmentsEnabled) {
                var settleAuthorityIssue =
                    TppaVerificationSettlePolicy.GetActuatorQualificationIssues(
                        profileService.ActiveProfile.TelescopeSettings.SettleTime,
                        VerificationPointSettleTimeSeconds)
                        .FirstOrDefault();
                if (settleAuthorityIssue != null) {
                    throw new SequenceEntityFailedException(
                        $"{settleAuthorityIssue} No UPAS movement was authorized.");
                }

                var geometryConfigurationIssue =
                    TppaThreePointGeometryQualificationPolicy.GetConfigurationIssues(TargetDistance)
                        .FirstOrDefault();
                if (geometryConfigurationIssue != null) {
                    throw new SequenceEntityFailedException(
                        $"{geometryConfigurationIssue} No UPAS movement was authorized.");
                }
            }
            using var actuatorConnectionSuppression = executionPolicy.AllowActuatorConnection
                ? null
                : await PolarAlignmentActuatorConnectionGate.SuppressAsync(
                    TimeSpan.FromSeconds(15),
                    token).ConfigureAwait(false);
            try {
                if (VerificationOnly && DriftValidationOnly) {
                    throw new InvalidOperationException(PolarAlignmentExecutionPolicy.ConflictingDiagnosticModesIssue);
                }

                using (var localCTS = CancellationTokenSource.CreateLinkedTokenSource(
                    token,
                    fastRuntimeDeadlineCTS?.Token ?? CancellationToken.None)) {
                    if (enforceFastRuntimeBudget) {
                        var remaining = TimeSpan.FromSeconds(
                            Math.Max(0.001,
                                TppaFastAlignmentExecutionBudget.MaximumRuntimeSeconds
                                - alignmentRuntime.Elapsed.TotalSeconds));
                        fastRuntimeDeadlineCTS.CancelAfter(remaining);
                        Logger.Info(
                            $"TPPA five-minute automated runtime contract armed: " +
                            $"elapsed={alignmentRuntime.Elapsed.TotalSeconds:F1}s; " +
                            $"remaining={remaining.TotalSeconds:F1}s.");
                    }
                    Guid correlatedGuid = Guid.NewGuid();
                    pauseTS = new PauseTokenSource();
                    try {
                        TPAPAVM?.Dispose(executionPolicy.DisconnectActuatorOnDispose);
                    } catch { }

                    TPAPAVM = new TPAPAVM(profileService, weatherDataMediator);
                    IProgress<ApplicationStatus> progress = new Progress<ApplicationStatus>(p => {
                        TPAPAVM.Status = p;
                        externalProgress?.Report(p);
                        messageBroker?.Publish(new PolarAlignmentProgressMessage(correlatedGuid, p));
                    });

                    windowService.Show(TPAPAVM, Loc.Instance["LblPolarAlignment"], System.Windows.ResizeMode.CanResizeWithGrip, System.Windows.WindowStyle.SingleBorderWindow);
                    windowService.OnClosed += (s, e) => {
                        try {
                            localCTS?.Cancel();
                        } catch { }
                    };

                    if (guiderMediator?.GetInfo()?.Connected == true) { 
                        Logger.Info("Stopping guiding to start polar alignment.");
                        try {
                            await guiderMediator.StopGuiding(token);
                        } catch { }
                        
                    }

                    var runWeatherInfo = weatherDataMediator.GetInfo();
                    var runRefractionParameters = RefractionParameters.GetRefractionParameters(runWeatherInfo);
                    var truePoleOffsetArcMinutes = RefractionAlignmentTarget.CalculateTruePoleOffsetArcMinutes(
                        Latitude.Degree,
                        runRefractionParameters);
                    var finiteTruePoleOffset = double.IsFinite(truePoleOffsetArcMinutes)
                        ? truePoleOffsetArcMinutes
                        : (double?)null;
                    var atmosphereSource = runWeatherInfo?.Connected == true
                        ? "weather-device-with-standard-fallbacks"
                        : "standard-atmosphere-fallback";
                    Logger.Info("TPPA_RUN_PROVENANCE " + new TppaRunProvenance(
                        TppaRunProvenance.CurrentSchemaVersion,
                        correlatedGuid,
                        DateTime.UtcNow,
                        Properties.Settings.Default.RefractionAdjustment,
                        RefractionAlignmentTarget.GetPoleTarget(Properties.Settings.Default.RefractionAdjustment),
                        finiteTruePoleOffset,
                        atmosphereSource,
                        runRefractionParameters.PressureHPa,
                        runRefractionParameters.Temperature,
                        runRefractionParameters.RelativeHumidity,
                        runRefractionParameters.Wavelength,
                        automatedAdjustmentsEnabled,
                        executionPolicy.AllowActuatorMovement,
                        VerificationOnly,
                        DriftValidationOnly,
                        AlignmentTolerance).ToJson());
                    WarnWhenTargetingRefractedPole(truePoleOffsetArcMinutes);

                    var currentPosition = telescopeMediator.GetInfo().Connected ? telescopeMediator.GetCurrentPosition().Transform(Latitude, Longitude) : null;
                    Logger.Info($"""
                        Starting polar alignment:
                            Manual mode: {ManualMode}
                            Verification only: {VerificationOnly}
                            5-position shadow model check: {OverdeterminedShadowModelCheck}
                            Drift validation only: {DriftValidationOnly}
                            Measure point distance: {TargetDistance}
                            Mount move rate: {MoveRate}
                            Timeout factor: {Properties.Settings.Default.MoveTimeoutFactor}
                            Direction east: {EastDirection}
                            Start from current: {StartFromCurrentPosition}
                            Altitude: {(StartFromCurrentPosition ? currentPosition?.Altitude : Coordinates.Coordinates.Altitude)}
                            Azimuth: {(StartFromCurrentPosition ? currentPosition?.Azimuth : Coordinates.Coordinates.Azimuth)}
                            Alignment tolerance: {AlignmentTolerance}
                            Filter: {Filter}
                            Exposure time: {ExposureTime}
                            Binning: {Binning}
                            Gain: {Gain}
                            Offset: {Offset}
                            Initial search radius: {SearchRadius}
                            Refraction adjustment: {Properties.Settings.Default.RefractionAdjustment}
                            Continuous error estimator: {Properties.Settings.Default.UseContinuousErrorEstimator}
                            Stop tracking when done: {Properties.Settings.Default.StopTrackingWhenDone}
                            Auto pause: {Properties.Settings.Default.AutoPause}
                            Avalon UPA: {Properties.Settings.Default.UseAvalonPolarAlignmentSystem}
                            OAPA: {Properties.Settings.Default.UseOAPAPolarAlignmentSystem}
                            Selected System: {Properties.Settings.Default.SelectedPolarAlignmentSystem}
                            Automated adjustments: {Properties.Settings.Default.DoAutomatedAdjustments}
                        """);

                    if (executionPolicy.RunSingleFreshVerification) {
                        await ExecuteVerificationOnly(TPAPAVM, correlatedGuid, progress, localCTS.Token);
                        return;
                    }

                    if (executionPolicy.RunDriftValidation) {
                        await ExecuteDriftValidationOnly(TPAPAVM, progress, localCTS.Token);
                        return;
                    }

                    TPAPAVM.ActivateFirstStep();

                    if (!ManualMode) {
                        if (!StartFromCurrentPosition) {
                            Logger.Info($"Slewing to initial position {Coordinates.Coordinates}");
                            SetTrackingSidereal(true);
                            await telescopeMediator.SlewToCoordinatesAsync(Coordinates.Coordinates, localCTS.Token);
                        } else {
                            Logger.Info($"Starting from current position {telescopeMediator.GetCurrentPosition()}");
                        }

                    } else {
                        if (telescopeMediator.GetInfo().Connected) {
                            Logger.Info($"Manual mode engaged with mount connection available. Running in semi manual mode with standard plate solver.");
                            SetTrackingSidereal(true);
                        } else {
                            Logger.Info($"Manual mode engaged without any mount connection. Running in complete blind mode using blind solver.");
                        }
                    }

                    if (telescopeMediator.GetInfo().Connected && domeMediator.GetInfo().Connected) {
                        await domeMediator.WaitForDomeSynchronization(token);
                    }

                    if (executionPolicy.AllowActuatorMovement && automatedAdjustmentsEnabled) {
                        if (!telescopeMediator.GetInfo().Connected) {
                            throw new SequenceEntityFailedException(
                                "Declination-aware TPPA geometry preflight requires a connected telescope. No UPAS movement was authorized.");
                        }
                        var plannedDeclination = telescopeMediator.GetCurrentPosition().Dec;
                        var arcFeasibility = TppaDeclinationArcFeasibilityPolicy.Evaluate(
                            TargetDistance,
                            plannedDeclination);
                        Logger.Info(
                            $"TPPA declination-aware geometry preflight: feasible={arcFeasibility.IsFeasible}; " +
                            $"predictedOnSkyLeg={arcFeasibility.PredictedOnSkyLegDegrees:F3} deg; " +
                            $"requiredRaLeg={arcFeasibility.RequiredRaLegDegrees:F3} deg; " +
                            $"reason={arcFeasibility.Reason}");
                        if (!arcFeasibility.IsFeasible) {
                            throw new SequenceEntityFailedException(arcFeasibility.Reason);
                        }
                    }

                    if (executionPolicy.AllowActuatorPreparation) {
                        EnsureFastRuntimeBudget(
                            "UPAS preparation and initial determination",
                            TppaFastAlignmentExecutionBudget.FreshDeterminationRetryReserveSeconds);
                        await TPAPAVM.PrepareUpasBeforeInitialMeasurement(
                            TargetDistance,
                            progress,
                            localCTS.Token);
                    }

                    var solve1 = await Solve(TPAPAVM, 5.0, progress, localCTS.Token);
                    var refractionParameter = runRefractionParameters;

                    var telescopeInfo = telescopeMediator.GetInfo();

                    var position1 = new Position(solve1.Coordinates, solve1.PositionAngle, Latitude, Longitude, Elevation, refractionParameter);

                    var point1MountInfo = telescopeMediator.GetInfo();
                    string point1MountInfoString = "";
                    if (point1MountInfo.Connected) {
                        point1MountInfoString = $" - Mount RA: {point1MountInfo.RightAscensionString}; Mount Dec: {point1MountInfo.DeclinationString}";
                    }
                    Logger.Info($"First measurement point {solve1.Coordinates} - Vector: {position1.Vector} - Position Angle: {position1.PositionAngle}{point1MountInfoString}");
                    var automatedVerificationStartPointing = !ManualMode && point1MountInfo.Connected
                        ? telescopeMediator.GetCurrentPosition()
                        : null;
                    var automatedVerificationEastDirection = EastDirection;

                    TPAPAVM.ActivateSecondStep();

                    PlateSolveResult solve2 = null;
                    if (!ManualMode) {
                        solve2 = await AutomatedNextPoint(progress, localCTS.Token);
                    } else {
                        solve2 = await ManualNextPoint(solve1, progress, localCTS.Token);
                    }

                    var position2 = new Position(solve2.Coordinates, solve2.PositionAngle, Latitude, Longitude, Elevation, refractionParameter);
                    var point2MountInfo = telescopeMediator.GetInfo();
                    string point2MountInfoString = "";
                    if (point2MountInfo.Connected) {
                        point2MountInfoString = $" - Mount RA: {point2MountInfo.RightAscensionString}; Mount Dec: {point2MountInfo.DeclinationString}";
                    }
                    Logger.Info($"Second measurement point {solve2.Coordinates} - Vector: {position2.Vector} - Position Angle: {position2.PositionAngle}{point2MountInfoString}");

                    TPAPAVM.ActivateThirdStep();


                    PlateSolveResult solve3 = null;
                    if (!ManualMode) {
                        solve3 = await AutomatedNextPoint(progress, localCTS.Token);
                    } else {
                        solve3 = await ManualNextPoint(solve2, progress, localCTS.Token);
                        await CoreUtil.Wait(TimeSpan.FromSeconds(10), localCTS.Token, progress, "Waiting for things to settle. Make sure the scope is tracking and don't move any further!");
                        solve3 = await Solve(TPAPAVM, 5.0, progress, localCTS.Token);
                    }

                    var position3 = new Position(solve3.Coordinates, solve3.PositionAngle, Latitude, Longitude, Elevation, refractionParameter);

                    var point3MountInfo = telescopeMediator.GetInfo();
                    string point3MountInfoString = "";
                    if (point3MountInfo.Connected) {
                        point3MountInfoString = $" - Mount RA: {point3MountInfo.RightAscensionString}; Mount Dec: {point3MountInfo.DeclinationString}";
                    }
                    Logger.Info($"Third measurement point {solve3.Coordinates} - Vector: {position3.Vector} - Position Angle: {position3.PositionAngle}{point3MountInfoString}");

                    progress?.Report(GetStatus("Calculating Error"));

                    Angle decSpread = Angle.Zero;
                    if (point1MountInfo.Connected && point2MountInfo.Connected && point3MountInfo.Connected) {
                        double CalculateDeclinationSpread(double value1, double value2, double value3) {
                            double min = Math.Min(value1, Math.Min(value2, value3));
                            double max = Math.Max(value1, Math.Max(value2, value3));
                            return max - min;
                        }
                        decSpread = Angle.ByDegree(CalculateDeclinationSpread(point1MountInfo.Declination, point2MountInfo.Declination, point3MountInfo.Declination));
                    }

                    // The initial three-point solve is pure calculation, so build it off the caller context
                    // and only publish the finished result back onto the view model afterwards.
                    var determination = await Task.Run(() => new PolarErrorDetermination(solve3,
                                                                                          position1,
                                                                                          position2,
                                                                                          position3,
                                                                                          Latitude,
                                                                                          Longitude,
                                                                                          Elevation,
                                                                                          refractionParameter,
                                                                                          Properties.Settings.Default.RefractionAdjustment,
                                                                                          decSpread.ArcSeconds),
                                                       localCTS.Token);
                    TPAPAVM.PolarErrorDetermination = determination;
                    BindFreshGeometryQualification(
                        TPAPAVM,
                        determination,
                        executionPolicy.AllowActuatorMovement && automatedAdjustmentsEnabled,
                        "initial fresh determination");
                    if (executionPolicy.AllowActuatorConfiguration && TPAPAVM.AutomatedAdjustmentRequiresFreshMeasurementFeedback) {
                        TPAPAVM.RebaseAutomatedAdjustmentToFreshDetermination();
                    }

                    var correctForRefraction = Properties.Settings.Default.RefractionAdjustment;
                    var activeTarget = correctForRefraction ? "true celestial pole" : "refracted apparent pole";

                    Logger.Info($"TPPA fresh 3-point calculated error: Az: {determination.InitialMountAxisAzimuthError}, Alt: {determination.InitialMountAxisAltitudeError}, Tot: {determination.InitialMountAxisTotalError}");
                    var freshVector = TppaPolarErrorVector.FromMinutes(
                        determination.InitialMountAxisAzimuthError.ArcMinutes,
                        determination.InitialMountAxisAltitudeError.ArcMinutes,
                        determination.InitialMountAxisTotalError.ArcMinutes);
                    Logger.Info($"TPPA fresh 3-point vector diagnostic: {freshVector.ToLogString()}.");
                    Logger.Info($"TPPA fresh 3-point active target diagnostic: {activeTarget}.");

                    try {
                        var alternateTarget = correctForRefraction ? "refracted apparent pole" : "true celestial pole";
                        var activePoleAltitude = determination.CalculateTargetPoleAltitudeDegrees(refractionParameter, correctForRefraction);
                        var alternatePoleAltitude = determination.CalculateTargetPoleAltitudeDegrees(refractionParameter, !correctForRefraction);
                        var poleSeparationArcMinutes = Math.Abs(activePoleAltitude - alternatePoleAltitude) * 60.0;
                        var alternateError = determination.CalculateInitialMountAxisError(refractionParameter, !correctForRefraction);
                        Logger.Info($"TPPA same-solves alternate target diagnostic ({alternateTarget}): Az: {alternateError.AzimuthError}, Alt: {alternateError.AltitudeError}, Tot: {alternateError.TotalError}");
                        Logger.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "TPPA same-solves target context: Observation={0:O}, Latitude={1:F6} deg, ActiveTarget={2}, ActivePoleAltitude={3:F8} deg, " +
                            "AlternateTarget={4}, AlternatePoleAltitude={5:F8} deg, PoleSeparation={6:F4} arcmin, Pressure={7:F2} hPa, " +
                            "Temperature={8:F2} C, RelativeHumidity={9:F4}, Wavelength={10:F4} um",
                            determination.InitialReferenceFrame.Coordinates.DateTime.Now,
                            determination.Latitude.Degree,
                            activeTarget,
                            activePoleAltitude,
                            alternateTarget,
                            alternatePoleAltitude,
                            poleSeparationArcMinutes,
                            refractionParameter.PressureHPa,
                            refractionParameter.Temperature,
                            refractionParameter.RelativeHumidity,
                            refractionParameter.Wavelength));
                    } catch (Exception ex) {
                        Logger.Warning($"TPPA same-solves alternate target diagnostic could not be calculated: {ex.Message}");
                    }

                    if (executionPolicy.AllowActuatorMovement
                            && TPAPAVM.ActiveAlignmentSystemVM?.DoAutomatedAdjustments == true) {
                        var inputDecision = AutomatedAdjustmentInputPolicy.Evaluate(
                            determination.InitialMountAxisAzimuthError.ArcMinutes,
                            determination.InitialMountAxisAltitudeError.ArcMinutes,
                            determination.InitialMountAxisTotalError.ArcMinutes);
                        Logger.Info(
                            $"TPPA automated-adjustment input qualification: " +
                            $"{(inputDecision.IsEligible ? "PASS" : "FAIL")}; {inputDecision.Reason}.");
                        if (!inputDecision.IsEligible) {
                            throw new SequenceEntityFailedException(
                                $"Automated polar-alignment correction was denied because {inputDecision.Reason}. " +
                                "Bring the mount closer manually and obtain a new qualified measurement.");
                        }
                    }

                    TPAPAVM.ActivateFourthStep();

                    if (executionPolicy.ShouldConnectActuator(
                        TPAPAVM.ActiveAlignmentSystemVM != null,
                        TPAPAVM.ActiveAlignmentSystemVM?.DoAutomatedAdjustments == true)) {
                        await TPAPAVM.ActiveAlignmentSystemVM.Connect();
                        if (!TPAPAVM.ActiveAlignmentSystemVM.Connected) {
                            throw new SequenceEntityFailedException("Unable to connect to Polar Alignment system. Cancelling polar alignment routine as automated adjustments are impossible.");
                        }
                    }

                    TPAPAVM.ArcsecPerPix = AstroUtil.ArcsecPerPixel(profileService.ActiveProfile.CameraSettings.PixelSize * Binning?.X ?? 1, profileService.ActiveProfile.TelescopeSettings.FocalLength);
                    var width = TPAPAVM.Image.Image.PixelWidth;
                    var height = TPAPAVM.Image.Image.PixelHeight;
                    TPAPAVM.Center = new Point(width / 2, height / 2);

                    await TPAPAVM.SelectNewReferenceStar(TPAPAVM.Center, localCTS.Token);

                    var sw = Stopwatch.StartNew();
                    var completionGuard = new AutomatedAlignmentCompletionGuard();
                    var freshFeedbackMoveCount = 0;
                    const int MaximumExtendedFreshFeedbackMoves = 18;
                    do {
                        await WaitIfPaused(localCTS.Token, progress);

                        if (TPAPAVM.AutomatedAdjustmentRequiresFreshMeasurementFeedback
                            && Math.Abs(TPAPAVM.PolarErrorDetermination.InitialMountAxisTotalError.ArcMinutes) <= AlignmentTolerance) {
                            EnsureFastRuntimeBudget(
                                "independent fresh completion confirmation",
                                TppaFastAlignmentExecutionBudget.FreshDeterminationReserveSeconds);
                            Logger.Info("Fresh UPAS measurement is below tolerance. Requiring one consecutive independent fresh three-point confirmation without moving.");
                            var completionCandidate = TPAPAVM.PolarErrorDetermination;
                            progress?.Report(new ApplicationStatus() { Status = "Confirming fresh three-point UPAS result" });
                            var confirmationDetermination = await MeasureFreshThreePointCompletionVerification(TPAPAVM,
                                                                                                                automatedVerificationStartPointing,
                                                                                                                automatedVerificationEastDirection,
                                                                                                                progress,
                                                                                                                localCTS.Token);
                            TPAPAVM.PolarErrorDetermination = confirmationDetermination;
                            BindFreshGeometryQualification(
                                TPAPAVM,
                                confirmationDetermination,
                                executionPolicy.AllowActuatorMovement && automatedAdjustmentsEnabled,
                                "completion confirmation");
                            TPAPAVM.UpdateAutomatedAdjustmentFromFreshDetermination();
                            var confirmedTotalErrorMinutes = Math.Abs(confirmationDetermination.InitialMountAxisTotalError.ArcMinutes);
                            var completionAgreement = FreshPolarAlignmentAgreementPolicy.Evaluate(
                                completionCandidate.InitialMountAxisAzimuthError.ArcMinutes,
                                completionCandidate.InitialMountAxisAltitudeError.ArcMinutes,
                                completionCandidate.InitialMountAxisTotalError.ArcMinutes,
                                confirmationDetermination.InitialMountAxisAzimuthError.ArcMinutes,
                                confirmationDetermination.InitialMountAxisAltitudeError.ArcMinutes,
                                confirmationDetermination.InitialMountAxisTotalError.ArcMinutes,
                                AlignmentTolerance);
                            Logger.Info($"TPPA fresh UPAS completion confirmation: Az: {confirmationDetermination.InitialMountAxisAzimuthError}, " +
                                        $"Alt: {confirmationDetermination.InitialMountAxisAltitudeError}, Tot: {confirmationDetermination.InitialMountAxisTotalError}. " +
                                        $"Repeatability: {(completionAgreement.IsRepeatable ? "PASS" : "FAIL")}; " +
                                        $"dAz={completionAgreement.AzimuthDeltaMinutes:+0.00;-0.00;0.00}', " +
                                        $"dAlt={completionAgreement.AltitudeDeltaMinutes:+0.00;-0.00;0.00}', " +
                                        $"dTot={completionAgreement.TotalDeltaMinutes:+0.00;-0.00;0.00}', " +
                                        $"threshold={completionAgreement.ThresholdMinutes:F2}'.");
                            if (confirmedTotalErrorMinutes <= AlignmentTolerance && completionAgreement.IsRepeatable) {
                                Logger.Info($"Two consecutive fresh three-point UPAS measurements are below alignment tolerance ({AlignmentTolerance}'). Automatically finishing polar alignment.");
                                Notification.ShowInformation(
                                    $"Two consecutive fresh three-point UPAS measurements are below alignment tolerance.{Environment.NewLine}" +
                                    $"Tolerance: {AlignmentTolerance}'{Environment.NewLine}" +
                                    $"Altitude Error: {Math.Round(confirmationDetermination.InitialMountAxisAltitudeError.ArcMinutes, 2)}'{Environment.NewLine}" +
                                    $"Azimuth Error: {Math.Round(confirmationDetermination.InitialMountAxisAzimuthError.ArcMinutes, 2)}'{Environment.NewLine}" +
                                    $"Total Error: {Math.Round(confirmedTotalErrorMinutes, 2)}'{Environment.NewLine}" +
                                    "Automatically finishing polar alignment.",
                                    TimeSpan.FromMinutes(1));
                                localCTS.Cancel();
                                continue;
                            }
                            if (confirmedTotalErrorMinutes <= AlignmentTolerance && !completionAgreement.IsRepeatable) {
                                EnsureFastRuntimeBudget(
                                    "independent fresh completion tie-breaker",
                                    TppaFastAlignmentExecutionBudget.FreshDeterminationReserveSeconds);
                                Logger.Warning("Two below-tolerance fresh measurements disagreed. Running one stationary fresh three-point tie-breaker before failing closed.");
                                progress?.Report(new ApplicationStatus() { Status = "Running fresh three-point completion tie-breaker" });
                                var tieBreakerDetermination = await MeasureFreshThreePointCompletionVerification(TPAPAVM,
                                                                                                                   automatedVerificationStartPointing,
                                                                                                                   automatedVerificationEastDirection,
                                                                                                                   progress,
                                                                                                                   localCTS.Token);
                                TPAPAVM.PolarErrorDetermination = tieBreakerDetermination;
                                BindFreshGeometryQualification(
                                    TPAPAVM,
                                    tieBreakerDetermination,
                                    executionPolicy.AllowActuatorMovement && automatedAdjustmentsEnabled,
                                    "completion tie-breaker");
                                TPAPAVM.UpdateAutomatedAdjustmentFromFreshDetermination();
                                var tieBreakerTotalErrorMinutes = Math.Abs(tieBreakerDetermination.InitialMountAxisTotalError.ArcMinutes);
                                var candidateTieAgreement = FreshPolarAlignmentAgreementPolicy.Evaluate(
                                    completionCandidate.InitialMountAxisAzimuthError.ArcMinutes,
                                    completionCandidate.InitialMountAxisAltitudeError.ArcMinutes,
                                    completionCandidate.InitialMountAxisTotalError.ArcMinutes,
                                    tieBreakerDetermination.InitialMountAxisAzimuthError.ArcMinutes,
                                    tieBreakerDetermination.InitialMountAxisAltitudeError.ArcMinutes,
                                    tieBreakerDetermination.InitialMountAxisTotalError.ArcMinutes,
                                    AlignmentTolerance);
                                var confirmationTieAgreement = FreshPolarAlignmentAgreementPolicy.Evaluate(
                                    confirmationDetermination.InitialMountAxisAzimuthError.ArcMinutes,
                                    confirmationDetermination.InitialMountAxisAltitudeError.ArcMinutes,
                                    confirmationDetermination.InitialMountAxisTotalError.ArcMinutes,
                                    tieBreakerDetermination.InitialMountAxisAzimuthError.ArcMinutes,
                                    tieBreakerDetermination.InitialMountAxisAltitudeError.ArcMinutes,
                                    tieBreakerDetermination.InitialMountAxisTotalError.ArcMinutes,
                                    AlignmentTolerance);
                                var tieBreakerHasConsensus = candidateTieAgreement.IsRepeatable || confirmationTieAgreement.IsRepeatable;
                                Logger.Info($"TPPA fresh UPAS completion tie-breaker: Az: {tieBreakerDetermination.InitialMountAxisAzimuthError}, " +
                                            $"Alt: {tieBreakerDetermination.InitialMountAxisAltitudeError}, Tot: {tieBreakerDetermination.InitialMountAxisTotalError}. " +
                                            $"Consensus with first={candidateTieAgreement.IsRepeatable}, second={confirmationTieAgreement.IsRepeatable}.");
                                if (tieBreakerTotalErrorMinutes <= AlignmentTolerance && tieBreakerHasConsensus) {
                                    Logger.Info($"Fresh three-point UPAS tie-breaker established below-tolerance consensus ({AlignmentTolerance}'). Automatically finishing polar alignment.");
                                    Notification.ShowInformation(
                                        $"Fresh three-point tie-breaker established below-tolerance consensus.{Environment.NewLine}" +
                                        $"Tolerance: {AlignmentTolerance}'{Environment.NewLine}" +
                                        $"Altitude Error: {Math.Round(tieBreakerDetermination.InitialMountAxisAltitudeError.ArcMinutes, 2)}'{Environment.NewLine}" +
                                        $"Azimuth Error: {Math.Round(tieBreakerDetermination.InitialMountAxisAzimuthError.ArcMinutes, 2)}'{Environment.NewLine}" +
                                        $"Total Error: {Math.Round(tieBreakerTotalErrorMinutes, 2)}'{Environment.NewLine}" +
                                        "Automatically finishing polar alignment.",
                                        TimeSpan.FromMinutes(1));
                                    localCTS.Cancel();
                                    continue;
                                }

                                throw new SequenceEntityFailedException(
                                    $"Three stationary fresh three-point UPAS measurements did not establish repeatable below-tolerance consensus. " +
                                    $"The tie-breaker total was {tieBreakerTotalErrorMinutes:F2}' with limit {AlignmentTolerance}', " +
                                    $"agreement with first={candidateTieAgreement.IsRepeatable}, agreement with second={confirmationTieAgreement.IsRepeatable}. " +
                                    "Automated alignment stopped without another UPAS move.");
                            }
                            Logger.Warning($"Fresh UPAS completion confirmation was above tolerance ({AlignmentTolerance}'). Continuing from the confirmed fresh measurement without moving first.");
                        }

                        EnsureFastRuntimeBudget(
                            "continuous correction solve",
                            TppaFastAlignmentExecutionBudget.ContinuousSolveReserveSeconds);
                        var continuousSolve = await Solve(TPAPAVM, 0, progress, localCTS.Token);
                        if (continuousSolve.Success) {
                            var estimateStable = await TPAPAVM.UpdateDetails(continuousSolve, progress, localCTS.Token);

                            var freshFeedbackControl = TPAPAVM.AutomatedAdjustmentRequiresFreshMeasurementFeedback;
                            if (estimateStable || freshFeedbackControl) {
                                if (!estimateStable && freshFeedbackControl) {
                                    Logger.Warning("Continuous polar error estimate was unstable. UPAS correction will proceed only from the last independent fresh three-point measurement.");
                                }
                                await messageBroker.Publish(
                                    new PolarAlignmentErrorMessage(
                                        correlatedGuid,
                                        altitudeError: TPAPAVM.PolarErrorDetermination.CurrentMountAxisAltitudeError.Degree,
                                        azimuthError: TPAPAVM.PolarErrorDetermination.CurrentMountAxisAzimuthError.Degree,
                                        totalError: TPAPAVM.PolarErrorDetermination.CurrentMountAxisTotalError.Degree
                                    )
                                );

                                Logger.Info($"TPPA correction-loop calculated error: Az: {TPAPAVM.PolarErrorDetermination.CurrentMountAxisAzimuthError}, Alt: {TPAPAVM.PolarErrorDetermination.CurrentMountAxisAltitudeError}, Tot: {TPAPAVM.PolarErrorDetermination.CurrentMountAxisTotalError}");

                                var totalErrorMinutes = Math.Abs(TPAPAVM.PolarErrorDetermination.CurrentMountAxisTotalError.ArcMinutes);
                                var completionDecision = TPAPAVM.AutomatedAdjustmentRequiresFreshMeasurementFeedback
                                    ? AutomatedAlignmentCompletionDecision.ContinueCorrection
                                    : completionGuard.Evaluate(totalErrorMinutes, AlignmentTolerance);
                                if (completionDecision == AutomatedAlignmentCompletionDecision.VerifyFreshThreePoint) {
                                    EnsureFastRuntimeBudget(
                                        "independent fresh completion verification",
                                        TppaFastAlignmentExecutionBudget.FreshDeterminationReserveSeconds);
                                    Logger.Info("Two stationary correction-frame solves are below tolerance. Starting an independent fresh three-point completion verification before finishing.");
                                    progress?.Report(new ApplicationStatus() { Status = "Running fresh three-point completion verification" });

                                    var verificationDetermination = await MeasureFreshThreePointCompletionVerification(TPAPAVM,
                                                                                                                       automatedVerificationStartPointing,
                                                                                                                       automatedVerificationEastDirection,
                                                                                                                       progress,
                                                                                                                       localCTS.Token);
                                    TPAPAVM.PolarErrorDetermination = verificationDetermination;
                                    BindFreshGeometryQualification(
                                        TPAPAVM,
                                        verificationDetermination,
                                        executionPolicy.AllowActuatorMovement && automatedAdjustmentsEnabled,
                                        "completion verification");
                                    var verifiedTotalErrorMinutes = Math.Abs(verificationDetermination.InitialMountAxisTotalError.ArcMinutes);
                                    var freshDecision = completionGuard.EvaluateFreshVerification(verifiedTotalErrorMinutes, AlignmentTolerance);

                                    Logger.Info($"TPPA completion-verification fresh 3-point calculated error: Az: {verificationDetermination.InitialMountAxisAzimuthError}, Alt: {verificationDetermination.InitialMountAxisAltitudeError}, Tot: {verificationDetermination.InitialMountAxisTotalError}");
                                    if (freshDecision == AutomatedAlignmentCompletionDecision.AbortAfterFreshVerificationFailures) {
                                        throw new InvalidOperationException($"Fresh three-point completion verification was above the selected {AlignmentTolerance}' tolerance. Automated alignment was stopped immediately because correction-frame feedback did not agree with an independent measurement.");
                                    } else if (freshDecision == AutomatedAlignmentCompletionDecision.Finish) {
                                        Logger.Info($"Fresh three-point verification is below alignment tolerance ({AlignmentTolerance}'). " +
                                            $"Altitude Error: {Math.Round(verificationDetermination.InitialMountAxisAltitudeError.ArcMinutes, 2)}'. " +
                                            $"Azimuth Error: {Math.Round(verificationDetermination.InitialMountAxisAzimuthError.ArcMinutes, 2)}'. " +
                                            $"Total Error: {Math.Round(verifiedTotalErrorMinutes, 2)}'. " +
                                            $"Automatically finishing polar alignment.");
                                        Notification.ShowInformation(
                                            $"Fresh three-point verification is below alignment tolerance.{Environment.NewLine}" +
                                            $"Tolerance: {AlignmentTolerance}'{Environment.NewLine}" +
                                            $"Altitude Error: {Math.Round(verificationDetermination.InitialMountAxisAltitudeError.ArcMinutes, 2)}'{Environment.NewLine}" +
                                            $"Azimuth Error: {Math.Round(verificationDetermination.InitialMountAxisAzimuthError.ArcMinutes, 2)}'{Environment.NewLine}" +
                                            $"Total Error: {Math.Round(verifiedTotalErrorMinutes, 2)}'{Environment.NewLine}" +
                                            $"Automatically finishing polar alignment.",
                                            TimeSpan.FromMinutes(1));
                                        localCTS.Cancel();
                                    } else {
                                        Logger.Warning($"Fresh three-point verification is above alignment tolerance ({AlignmentTolerance}'). Rebasing automated correction to the fresh result and continuing.");
                                        await TPAPAVM.SelectNewReferenceStar(TPAPAVM.Center, localCTS.Token);
                                        TPAPAVM.RebaseAutomatedAdjustmentToFreshDetermination();
                                        if (Properties.Settings.Default.AutoPause) {
                                            Pause();
                                        }
                                        continue;
                                    }
                                } else if (completionDecision == AutomatedAlignmentCompletionDecision.ValidateWithoutMoving) {
                                    var unresolvedState = TPAPAVM.AutomatedAdjustmentRequiresCompletionValidation
                                        ? " The UPAS azimuth engagement or reversal state is still unvalidated."
                                        : string.Empty;
                                    Logger.Info($"Total Error is below alignment tolerance ({AlignmentTolerance}') for the first solve.{unresolvedState} Holding the polar-alignment motors stationary and requiring one confirming solve before finishing.");
                                    progress?.Report(new ApplicationStatus() { Status = "Validating below-tolerance polar error without moving" });
                                    if (Properties.Settings.Default.AutoPause) {
                                        Pause();
                                    }
                                    continue;
                                }
                                else if (!enforceFastRuntimeBudget && sw.Elapsed > TimeSpan.FromMinutes(5)) {
                                    Logger.Info("Correction phase exceeded 5 minutes");
                                    Notification.ShowInformation($"Polar alignment correction phase has been running for multiple minutes.{Environment.NewLine}Consider restarting the process to improve precision");
                                    sw.Stop();
                                    sw.Reset();
                                }
                                localCTS.Token.ThrowIfCancellationRequested();
                                if (TPAPAVM.AutomatedAdjustmentRequiresFreshMeasurementFeedback
                                        && enforceFastRuntimeBudget
                                        && freshFeedbackMoveCount >= TppaFastAlignmentExecutionBudget.MaximumFreshFeedbackMoves) {
                                    throw new InvalidOperationException("UPAS five-minute automated alignment is one-shot and stopped after its fresh-measured move did not converge. No second UPAS movement was authorized.");
                                }
                                if (TPAPAVM.AutomatedAdjustmentRequiresFreshMeasurementFeedback
                                        && !enforceFastRuntimeBudget
                                        && freshFeedbackMoveCount >= MaximumExtendedFreshFeedbackMoves) {
                                    throw new InvalidOperationException($"UPAS automated alignment stopped after {MaximumExtendedFreshFeedbackMoves} fresh-measured moves without converging.");
                                }

                                if (enforceFastRuntimeBudget
                                        && executionPolicy.AllowActuatorMovement
                                        && TPAPAVM.ActiveAlignmentSystemVM?.DoAutomatedAdjustments == true) {
                                    var moveDecision = TppaFastAlignmentExecutionBudget.EvaluateBeforeMove(
                                        alignmentRuntime.Elapsed);
                                    Logger.Info(
                                        $"TPPA_FAST_RUNTIME_BUDGET operation=one-shot UPAS move, independent fresh response, and stationary confirmation; " +
                                        $"elapsedSeconds={moveDecision.ElapsedSeconds:F1}; remainingSeconds={moveDecision.RemainingSeconds:F1}; " +
                                        $"requiredReserveSeconds={moveDecision.RequiredReserveSeconds:F1}; allowed={moveDecision.CanStart}.");
                                    if (!moveDecision.CanStart) {
                                        throw new SequenceEntityFailedException($"Automated polar alignment cannot complete the one-shot UPAS move and both required fresh determinations inside the five-minute runtime contract: {moveDecision.Reason}. No UPAS movement was authorized.");
                                    }
                                }
                                var preMoveFreshVector = TppaPolarErrorVector.FromMinutes(
                                    TPAPAVM.PolarErrorDetermination.InitialMountAxisAzimuthError.ArcMinutes,
                                    TPAPAVM.PolarErrorDetermination.InitialMountAxisAltitudeError.ArcMinutes,
                                    TPAPAVM.PolarErrorDetermination.InitialMountAxisTotalError.ArcMinutes);
                                var moved = executionPolicy.AllowActuatorMovement
                                    && await TPAPAVM.MoveCloser(progress, localCTS.Token);
                                if (moved && TPAPAVM.AutomatedAdjustmentRequiresFreshMeasurementFeedback) {
                                    freshFeedbackMoveCount++;
                                    Logger.Info("UPAS move completed. Measuring an independent fresh three-point response before allowing another automated move.");
                                    progress?.Report(new ApplicationStatus() { Status = "Measuring fresh three-point UPAS response" });

                                    var feedbackDetermination = await MeasureFreshThreePointCompletionVerification(TPAPAVM,
                                                                                                                    automatedVerificationStartPointing,
                                                                                                                    automatedVerificationEastDirection,
                                                                                                                    progress,
                                                                                                                    localCTS.Token);
                                    TPAPAVM.PolarErrorDetermination = feedbackDetermination;
                                    BindFreshGeometryQualification(
                                        TPAPAVM,
                                        feedbackDetermination,
                                        executionPolicy.AllowActuatorMovement && automatedAdjustmentsEnabled,
                                        "post-move fresh feedback");
                                    Logger.Info($"TPPA fresh post-move response: Az: {feedbackDetermination.InitialMountAxisAzimuthError}, " +
                                                $"Alt: {feedbackDetermination.InitialMountAxisAltitudeError}, Tot: {feedbackDetermination.InitialMountAxisTotalError}");
                                    var postMoveFreshVector = TppaPolarErrorVector.FromMinutes(
                                        feedbackDetermination.InitialMountAxisAzimuthError.ArcMinutes,
                                        feedbackDetermination.InitialMountAxisAltitudeError.ArcMinutes,
                                        feedbackDetermination.InitialMountAxisTotalError.ArcMinutes);
                                    var responseDecision = TppaPostMoveResponsePolicy.Evaluate(
                                        preMoveFreshVector,
                                        postMoveFreshVector,
                                        AlignmentTolerance);
                                    Logger.Info("TPPA_POST_MOVE_RESPONSE " + responseDecision.ToLogString());
                                    var responseDisposition = TppaPostMoveResponsePolicy.DispositionForMode(
                                        responseDecision,
                                        enforceFastRuntimeBudget);

                                    if (responseDisposition.UpdateController) {
                                        TPAPAVM.UpdateAutomatedAdjustmentFromFreshDetermination();
                                    }

                                    if (responseDisposition.ContinueToStationaryConfirmation) {
                                        Logger.Info("Fresh post-move result is a convergence candidate. Returning directly to the loop's independent stationary three-point confirmation without another solve or move.");
                                        continue;
                                    }
                                    if (responseDisposition.FailureMessage != null) {
                                        throw new SequenceEntityFailedException(responseDisposition.FailureMessage);
                                    }
                                }
                            } else {
                                Logger.Warning("Skipping error publication and automated correction because the continuous estimate was unstable.");
                            }
                        }

                        if (Properties.Settings.Default.AutoPause) {
                            Pause();
                        }
                    } while (!localCTS.Token.IsCancellationRequested);

                    if (enforceFastRuntimeBudget) {
                        EnsureFastRuntimeBudget("successful completion", 0);
                        Logger.Info(
                            $"TPPA five-minute automated runtime contract completed in " +
                            $"{alignmentRuntime.Elapsed.TotalSeconds:F1}s.");
                    }

                    return;
                }
            } catch (OperationCanceledException) {
                if (enforceFastRuntimeBudget
                        && !token.IsCancellationRequested
                        && fastRuntimeDeadlineCTS?.IsCancellationRequested == true) {
                    throw new SequenceEntityFailedException(
                        $"Automated polar alignment exceeded the " +
                        $"{TppaFastAlignmentExecutionBudget.MaximumRuntimeSeconds:F0}-second runtime contract. " +
                        "The run stopped without authorizing another UPAS movement.");
                }
                throw;
            } catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError("Three Point Polar Alignment failed - " + ex.Message);
                throw;
            } finally {
                try {
                    await windowService?.Close();
                } catch { }
                try {
                    TPAPAVM?.Dispose(executionPolicy.DisconnectActuatorOnDispose);
                } catch (Exception) { }
                IsPaused = false;
                externalProgress?.Report(GetStatus(string.Empty));
                if (!executionPolicy.RunDriftValidation && Properties.Settings.Default.StopTrackingWhenDone) {
                    SetTrackingSidereal(false);
                }
            }
        }

        private TppaThreePointGeometryQualification BindFreshGeometryQualification(
                TPAPAVM viewModel,
                PolarErrorDetermination determination,
                bool requireForAutomatedMovement,
                string context) {
            var qualification = TppaThreePointGeometryQualificationPolicy.Evaluate(
                determination.ThreePointGeometry,
                TargetDistance);
            viewModel.BindAutomatedAdjustmentGeometryQualification(qualification);
            Logger.Info(
                $"TPPA {context} geometry: {determination.ThreePointGeometry.ToLogString()}; " +
                $"numerical-observability={(qualification.IsQualified ? "PASS" : "FAIL")}; " +
                $"reason={qualification.Reason}; absolute-accuracy=unqualified.");
            if (requireForAutomatedMovement && !qualification.IsQualified) {
                throw new SequenceEntityFailedException(
                    $"Automated polar-alignment correction was denied because {qualification.Reason} " +
                    "The geometry gate qualifies numerical observability only and no UPAS movement was authorized.");
            }
            return qualification;
        }

        private void WarnWhenTargetingRefractedPole(double offsetArcMinutes) {
            if (Properties.Settings.Default.RefractionAdjustment) {
                return;
            }

            if (!double.IsFinite(offsetArcMinutes) || offsetArcMinutes <= 0) {
                return;
            }

            var warning = $"Adjust for refraction is disabled. TPPA will target the apparent refracted pole, " +
                          $"estimated {offsetArcMinutes:0.00}' from the true pole for the current site and weather inputs.";
            var materialToTolerance = RefractionAlignmentTarget.IsMaterialToTolerance(offsetArcMinutes, AlignmentTolerance);
            if (materialToTolerance) {
                warning += $" This exceeds the selected {AlignmentTolerance:0.##}' alignment tolerance; " +
                           "drift-based polar-alignment tools may therefore report a larger residual.";
            }

            Logger.Warning(warning);
            if (materialToTolerance && Properties.Settings.Default.DoAutomatedAdjustments) {
                Notification.ShowWarning(warning);
            }
        }

        private async Task WaitIfPaused(CancellationToken token, IProgress<ApplicationStatus> progress) {
            if (IsPausing) {
                IsPaused = true;
                progress?.Report(GetStatus("Paused"));
                await pauseTS.Token.WaitWhilePausedAsync(token);
                progress?.Report(GetStatus(string.Empty));
                IsPaused = false;
            }
        }

        public bool Northern {
            get => profileService.ActiveProfile.AstrometrySettings.Latitude > 0;
        }

        /// <summary>
        /// Calculate the error based on the measured telescope axis compared to the polar axis
        /// Polar axis = Azimuth 0 | Altitude = Latitude
        /// </summary>
        /// <param name="axis"></param>
        /// <returns></returns>
        private (Angle, Angle) CalculateError(TopocentricCoordinates axis) {
            if (Northern) {
                var altError = axis.Altitude - Latitude;
                var azError = axis.Azimuth;
                return (altError, azError);
            } else {
                var altError = axis.Altitude + Latitude;
                var azError = axis.Azimuth + Angle.ByDegree(180);
                return (altError, azError);
            }
        }

        private FilterInfo filter;

        [JsonProperty]
        public FilterInfo Filter {
            get => filter;
            set {
                filter = value;
                RaisePropertyChanged();
            }
        }

        private double exposureTime;

        [JsonProperty]
        public double ExposureTime {
            get => exposureTime;
            set {
                exposureTime = value;
                RaisePropertyChanged();
            }
        }

        private int gain;

        [JsonProperty]
        public int Gain {
            get => gain;
            set {
                gain = value;
                RaisePropertyChanged();
            }
        }

        private int offset;

        [JsonProperty]
        public int Offset {
            get => offset;
            set {
                offset = value;
                RaisePropertyChanged();
            }
        }

        private BinningMode binning;

        [JsonProperty]
        public BinningMode Binning {
            get => binning;
            set {
                binning = value;
                RaisePropertyChanged();
            }
        }

        private CameraInfo cameraInfo;

        public CameraInfo CameraInfo {
            get => cameraInfo;
            private set {
                cameraInfo = value;
                RaisePropertyChanged();
            }
        }

        private async Task ExecuteDriftValidationOnly(
            TPAPAVM context,
            IProgress<ApplicationStatus> progress,
            CancellationToken token) {
            if (!telescopeMediator.GetInfo().Connected) {
                throw new InvalidOperationException(
                    "Drift-validation mode requires a connected telescope for the A-B-C-A measurement arc.");
            }

            var qualificationIssues = TppaVerificationSettlePolicy.GetQualificationIssues(
                TargetDistance,
                profileService.ActiveProfile.TelescopeSettings.SettleTime,
                VerificationPointSettleTimeSeconds);
            if (qualificationIssues.Count > 0) {
                throw new InvalidOperationException(
                    $"TPPA drift-validation qualification rejected: {string.Join(" ", qualificationIssues)}");
            }
            var telescopeInfo = telescopeMediator.GetInfo();
            var initialTracking = TppaDriftTrackingStatePolicy.Capture(
                telescopeInfo.TrackingEnabled,
                telescopeInfo.TrackingRate.TrackingMode);
            var refraction = RefractionParameters.GetRefractionParameters(weatherDataMediator.GetInfo());
            var elevationMeters = profileService.ActiveProfile.AstrometrySettings.Elevation;
            var preflightStartUtc = DateTime.UtcNow;
            var plannedPointA = StartFromCurrentPosition
                ? telescopeMediator.GetCurrentPosition()
                : new TopocentricCoordinates(
                    Coordinates.Coordinates.Azimuth,
                    Coordinates.Coordinates.Altitude,
                    Latitude,
                    Longitude,
                    elevationMeters,
                    new FixedObservationDateTime(preflightStartUtc))
                .Transform(
                    Epoch.J2000,
                    refraction.PressureHPa,
                    refraction.Temperature,
                    refraction.RelativeHumidity,
                    refraction.Wavelength);
            var preflight = TppaDriftArcPreflightFactory.Evaluate(
                plannedPointA,
                TargetDistance,
                preflightStartUtc,
                Latitude,
                Longitude,
                elevationMeters,
                refraction,
                destination => telescopeMediator.DestinationSideOfPier(destination));
            var selectedPreflight = preflight.ForDirection(EastDirection);
            var selectedDirection = EastDirection ? "+RA" : "-RA";
            if (!selectedPreflight.IsSafe) {
                throw new InvalidOperationException(
                    $"drift arc preflight rejected for selected {selectedDirection} direction: {selectedPreflight.Reason}");
            }
            Logger.Info(
                $"TPPA drift-validation selected-direction preflight passed for {selectedDirection}: {selectedPreflight.Reason}.");

            Coordinates pointA = null;
            TppaVerificationWaypointPlan driftWaypointPlan = null;
            var pointAPierSide = NINA.Core.Enum.PierSide.pierUnknown;
            Coordinates solvedPointA = null;
            Coordinates previousSolvedArcPoint = null;
            TppaDriftRuntimeObservation? pendingObservation = null;
            var arrivalIndex = 0;
            var metadataIndex = 0;
            Exception executionFailure = null;

            Logger.Info(
                "TPPA drift validation stops guiding. NINA 3.1 does not expose prior guiding-active state; an Advanced Sequence must explicitly start guiding after this diagnostic.");

            try {
                var report = await TppaDriftValidationOrchestrator.Run(
                Latitude.Degree,
                async (positionId, movementToken) => {
                    progress?.Report(new ApplicationStatus() {
                        Status = $"Moving to drift-validation position {positionId}"
                    });
                    switch (arrivalIndex++) {
                        case 0:
                            if (!StartFromCurrentPosition) {
                                Logger.Info($"Slewing to drift-validation position A {Coordinates.Coordinates}.");
                                SetTrackingSidereal(true);
                                await telescopeMediator.SlewToCoordinatesAsync(
                                    Coordinates.Coordinates,
                                    movementToken);
                            } else {
                                Logger.Info(
                                    $"Using current telescope pointing as drift-validation position A: {telescopeMediator.GetCurrentPosition()}.");
                            }
                            SetTrackingSidereal(true);
                            var initialTrackingConfirmed = false;
                            for (var attempt = 0; attempt < 20; attempt++) {
                                var trackingInfo = telescopeMediator.GetInfo();
                                if (trackingInfo.TrackingEnabled
                                        && trackingInfo.TrackingRate.TrackingMode
                                            == Equipment.Interfaces.TrackingMode.Sidereal) {
                                    initialTrackingConfirmed = true;
                                    break;
                                }
                                await Task.Delay(TimeSpan.FromMilliseconds(100), movementToken);
                            }
                            if (!initialTrackingConfirmed) {
                                throw new InvalidOperationException(
                                    "TPPA drift-validation initial A rejected because sidereal tracking could not be confirmed within two seconds.");
                            }
                            var initialSettleTime = TppaVerificationSettlePolicy.Resolve(
                                profileService.ActiveProfile.TelescopeSettings.SettleTime,
                                VerificationPointSettleTimeSeconds);
                            await CoreUtil.Wait(
                                TimeSpan.FromSeconds(initialSettleTime),
                                movementToken,
                                progress,
                                "Settling at drift-validation position A");
                            EnsureVerificationOnlyActualPositionSafe(
                                "drift-validation initial A post-settle");
                            pointA = telescopeMediator.GetCurrentPosition();
                            pointAPierSide = telescopeMediator.GetInfo().SideOfPier;
                            driftWaypointPlan = TppaVerificationWaypointPlan.Create(
                                pointA,
                                TargetDistance,
                                EastDirection);
                            foreach (var waypoint in driftWaypointPlan.Forward) {
                                EnsureVerificationOnlySlewDestinationSafe(waypoint);
                            }
                            Logger.Info(
                                "TPPA drift-validation exact A/B/C waypoint preflight passed after settled A capture.");
                            break;
                        case 1:
                        case 2:
                            if (driftWaypointPlan == null) {
                                throw new InvalidOperationException(
                                    "TPPA drift-validation exact waypoint plan was not initialized.");
                            }
                            var waypointIndex = arrivalIndex - 1;
                            await SlewToVerificationWaypoint(
                                driftWaypointPlan.Forward[waypointIndex],
                                TargetDistance,
                                $"drift-validation position {positionId}",
                                progress,
                                movementToken);
                            break;
                        case 3:
                            if (pointA == null) {
                                throw new InvalidOperationException(
                                    "Drift-validation position A was not captured.");
                            }
                            var returnStart = telescopeMediator.GetCurrentPosition();
                            var returnPreflight = TppaDriftLegPreflightFactory.EvaluateAbsolute(
                                returnStart,
                                pointA,
                                DateTime.UtcNow,
                                Latitude,
                                Longitude,
                                elevationMeters,
                                refraction,
                                destination => telescopeMediator.DestinationSideOfPier(destination));
                            if (!returnPreflight.IsSafe) {
                                throw new InvalidOperationException(
                                    $"TPPA drift-validation return to A rejected before slew: {returnPreflight.Reason}.");
                            }
                            Logger.Info(
                                $"TPPA drift-validation current-time return preflight passed: {returnPreflight.Reason}.");
                            await SlewToVerificationWaypoint(
                                pointA,
                                TargetDistance * 2.0,
                                "drift-validation return to A",
                                progress,
                                movementToken);
                            var returnedPointA = telescopeMediator.GetCurrentPosition();
                            var returnedPierSide =
                                telescopeMediator.DestinationSideOfPier(returnedPointA);
                            var closure = TppaDriftArcClosurePolicy.Evaluate(
                                pointA.RADegrees,
                                pointA.Dec,
                                returnedPointA.RADegrees,
                                returnedPointA.Dec,
                                pointAPierSide,
                                returnedPierSide);
                            if (!closure.IsSafe) {
                                throw new InvalidOperationException(
                                    $"TPPA drift-validation A-B-C-A closure rejected: {closure.Reason}.");
                            }
                            Logger.Info(
                                $"TPPA drift-validation A-B-C-A closure verified: separation={closure.PointingSeparationDegrees:F4} deg; {closure.Reason}.");
                            break;
                        default:
                            throw new InvalidOperationException(
                                "Drift-validation movement exceeded the A-B-C-A plan.");
                    }

                    if (domeMediator.GetInfo().Connected) {
                        await domeMediator.WaitForDomeSynchronization(movementToken);
                    }
                },
                async (positionId, metadataToken) => {
                    progress?.Report(new ApplicationStatus() {
                        Status = $"Capturing first drift solve at {positionId}"
                    });
                    var solve = await Solve(context, 30, progress, metadataToken);
                    pendingObservation = TppaDriftRuntimeMetadataFactory.Create(
                        positionId,
                        solve.Coordinates,
                        Latitude,
                        Longitude,
                        elevationMeters,
                        refraction);
                    if (metadataIndex == 0) {
                        if (!string.Equals(positionId, "A", StringComparison.OrdinalIgnoreCase)) {
                            throw new InvalidOperationException(
                                "The first drift-validation solved position must be A.");
                        }
                        solvedPointA = solve.Coordinates;
                    } else if (metadataIndex == 3) {
                        if (solvedPointA == null
                                || !string.Equals(positionId, "A", StringComparison.OrdinalIgnoreCase)) {
                            throw new InvalidOperationException(
                                "The final drift-validation solved position must close on captured A.");
                        }
                        var solvedClosure = TppaDriftArcClosurePolicy.Evaluate(
                            solvedPointA.RADegrees,
                            solvedPointA.Dec,
                            solve.Coordinates.RADegrees,
                            solve.Coordinates.Dec,
                            NINA.Core.Enum.PierSide.pierUnknown,
                            NINA.Core.Enum.PierSide.pierUnknown);
                        if (!solvedClosure.IsSafe) {
                            throw new InvalidOperationException(
                                $"TPPA drift-validation solved A closure rejected: {solvedClosure.Reason}.");
                        }
                        Logger.Info(
                            $"TPPA drift-validation solved A closure verified: separation={solvedClosure.PointingSeparationDegrees:F4} deg.");
                    }
                    if (metadataIndex is 1 or 2) {
                        if (previousSolvedArcPoint == null) {
                            throw new InvalidOperationException(
                                $"TPPA drift-validation solved move to {positionId} has no preceding solved position.");
                        }
                        var solvedMove = TppaDriftMoveVerificationPolicy.Evaluate(
                            previousSolvedArcPoint.RADegrees,
                            solve.Coordinates.RADegrees,
                            previousSolvedArcPoint.Dec,
                            solve.Coordinates.Dec,
                            TargetDistance,
                            NINA.Core.Enum.PierSide.pierUnknown,
                            NINA.Core.Enum.PierSide.pierUnknown);
                        if (!solvedMove.IsSafe) {
                            throw new InvalidOperationException(
                                $"TPPA drift-validation solved move to {positionId} rejected: {solvedMove.Reason}.");
                        }
                        Logger.Info(
                            $"TPPA drift-validation solved move to {positionId} verified: RA travel={solvedMove.RightAscensionTravelDegrees:F3} deg; DEC travel={solvedMove.DeclinationTravelDegrees:F3} deg.");
                    }
                    previousSolvedArcPoint = solve.Coordinates;
                    metadataIndex++;
                    var metadata = pendingObservation.Value.Metadata;
                    Logger.Info(
                        $"TPPA drift-validation metadata {positionId}: HA={metadata.HourAngleDegrees:F4} deg; altitude={metadata.AltitudeDegrees:F4} deg; refraction drift={metadata.ComputedRefractionDriftArcsecondsPerMinute:F5}\"/min.");
                    return metadata;
                },
                async (positionId, solveToken) => {
                    if (pendingObservation.HasValue) {
                        var initial = pendingObservation.Value;
                        pendingObservation = null;
                        return initial.Sample;
                    }

                    progress?.Report(new ApplicationStatus() {
                        Status = $"Collecting stationary drift solves at {positionId}"
                    });
                    var solve = await Solve(context, 30, progress, solveToken);
                    return new TppaDriftSolveSample(
                        solve.Coordinates.DateTime.UtcNow,
                        solve.Coordinates.Dec);
                },
                TppaDriftTrackAcquisitionPolicy.FieldDefault,
                TppaDeclinationDriftTrackPolicy.FieldDefault,
                TppaDriftValidationPolicy.FieldDefault,
                token);

                foreach (var track in report.Tracks) {
                    Logger.Info(
                        $"TPPA drift-validation track {track.Metadata.PositionId}: samples={track.Samples.Count}; duration={track.Fit.DurationSeconds:F1}s; DEC drift={track.Fit.DeclinationDriftArcsecondsPerMinute:F5}+/-{track.Fit.DeclinationDriftSigmaArcsecondsPerMinute:F5}\"/min; valid={track.Fit.IsValid}; reason={track.Fit.Reason}");
                }

                var validation = report.Validation;
                Logger.Info(
                    $"TPPA drift-validation result: valid={validation.IsValid}; Az={validation.AzimuthErrorArcMinutes:F3}'; Alt={validation.AltitudeErrorArcMinutes:F3}'; total={validation.TotalErrorArcMinutes:F3}'; Az sigma={validation.AzimuthSigmaArcMinutes:F3}'; Alt sigma={validation.AltitudeSigmaArcMinutes:F3}'; reduced chi2={validation.ReducedChiSquared:F3}; normal-matrix condition={validation.NormalMatrixConditionNumber:F3}; repeated residual={validation.MaximumRepeatedPositionStandardizedResidual:F3}; reason={validation.Reason}");
                progress?.Report(new ApplicationStatus() {
                    Status = validation.IsValid
                        ? $"Drift validation complete: {validation.TotalErrorArcMinutes:F2}' total (report only)"
                        : $"Drift validation rejected: {validation.Reason}"
                });
                if (!validation.IsValid) {
                    throw new InvalidOperationException(
                        $"TPPA report-only drift validation was rejected: {validation.Reason}");
                }
            } catch (Exception failure) {
                executionFailure = failure;
                throw;
            } finally {
                try {
                    SetTrackingSidereal(initialTracking.TrackingEnabled);
                } catch (Exception restorationFailure) {
                    if (executionFailure != null) {
                        Logger.Error(
                            "TPPA drift validation also failed to restore the original tracking-enabled state; preserving the original diagnostic failure.",
                            restorationFailure);
                    } else {
                        throw;
                    }
                }
            }
        }

        private Coordinates CaptureVerificationPointingFromMountTelemetry(string phase) {
            var mountInfo = telescopeMediator.GetInfo();
            if (!mountInfo.Connected) {
                throw new SequenceEntityFailedException(
                    $"Verification-only {phase} pointing capture requires a connected telescope.");
            }

            var pointing = TppaVerificationPointingSnapshot.FromMountTelemetry(
                mountInfo.Coordinates,
                phase);
            Logger.Info(
                $"TPPA verification-only captured {phase} from mount telemetry: " +
                $"RA={pointing.RADegrees:F6} deg; Dec={pointing.Dec:F6} deg; Epoch={pointing.Epoch}.");
            return pointing;
        }

        private async Task ExecuteVerificationOnly(TPAPAVM context,
                                                   Guid correlatedGuid,
                                                   IProgress<ApplicationStatus> progress,
                                                   CancellationToken token) {
            var verificationStartedUtc = DateTime.UtcNow;
            if (!telescopeMediator.GetInfo().Connected) {
                throw new InvalidOperationException("Verification-only mode requires a connected telescope so both measurements can use the same automated arc and A can be restored.");
            }

            var qualificationIssues = TppaVerificationSettlePolicy.GetQualificationIssues(
                TargetDistance,
                profileService.ActiveProfile.TelescopeSettings.SettleTime,
                VerificationPointSettleTimeSeconds);
            if (qualificationIssues.Count > 0) {
                throw new SequenceEntityFailedException(
                    $"Verification-only qualification rejected: {string.Join(" ", qualificationIssues)}");
            }

            var originalPointing = CaptureVerificationPointingFromMountTelemetry("original");
            Coordinates cleanupPointing = originalPointing;
            PolarErrorDetermination initialDetermination = null;
            PolarErrorDetermination reciprocalDetermination = null;
            PolarErrorDetermination verificationDetermination = null;
            IReadOnlyList<Position> reciprocalModelCheckSamples = null;
            TppaVerificationPartialDatasetReceipt verificationDataset = null;
            var originalEastDirection = EastDirection;
            context.ActivateFirstVerificationStep();

            await VerificationOnlyCleanupRunner.Run(
                async operationToken => {
                    if (!StartFromCurrentPosition) {
                        Logger.Info($"Slewing to verification-only initial position {Coordinates.Coordinates}");
                        EnsureVerificationOnlySlewDestinationSafe(Coordinates.Coordinates);
                        SetTrackingSidereal(true);
                        await telescopeMediator.SlewToCoordinatesAsync(Coordinates.Coordinates, operationToken);
                        EnsureVerificationOnlyActualPositionSafe("initial-position slew");
                    } else {
                        Logger.Info($"Starting verification-only measurement from current position {telescopeMediator.GetCurrentPosition()}");
                        EnsureVerificationOnlyActualPositionSafe("current-position start");
                    }

                    if (domeMediator.GetInfo().Connected) {
                        await domeMediator.WaitForDomeSynchronization(operationToken);
                    }

                    var initialSettleTimeSeconds = TppaVerificationSettlePolicy.Resolve(
                        profileService.ActiveProfile.TelescopeSettings.SettleTime,
                        VerificationPointSettleTimeSeconds);
                    Logger.Info(
                        $"TPPA verification-only initial point settle time: {initialSettleTimeSeconds:F3} seconds.");
                    await CoreUtil.Wait(
                        TimeSpan.FromSeconds(initialSettleTimeSeconds),
                        operationToken,
                        progress,
                        "Settling at verification-only point 1");
                    EnsureVerificationOnlyActualPositionSafe("initial point post-settle");

                    cleanupPointing = CaptureVerificationPointingFromMountTelemetry("A/correction");
                    Logger.Info($"TPPA verification-only captured A/correction pointing {cleanupPointing} before solve A.");
                    var waypointPlan = TppaVerificationWaypointPlan.Create(
                        cleanupPointing,
                        TargetDistance,
                        originalEastDirection);
                    var reciprocalWaypoints = OverdeterminedShadowModelCheck
                        ? waypointPlan.ReciprocalModelCheck
                        : waypointPlan.Reciprocal;
                    var forwardDirection = originalEastDirection ? "East" : "West";
                    var reciprocalDirection = originalEastDirection ? "West" : "East";
                    foreach (var waypoint in waypointPlan.Forward.Concat(reciprocalWaypoints)) {
                        EnsureVerificationOnlySlewDestinationSafe(waypoint);
                    }
                    Logger.Info($"TPPA verification-only waypoint preflight passed before measurement; " +
                                $"overdeterminedShadowModelCheck={OverdeterminedShadowModelCheck}; reciprocalSamples={reciprocalWaypoints.Count}.");

                    var expectedVerificationSamples = waypointPlan.Forward.Count * 2 + reciprocalWaypoints.Count;
                    var verificationLedger = new TppaVerificationSampleLedger(
                        correlatedGuid,
                        expectedVerificationSamples);

                    (VerificationOnlyArcMeasurement Initial,
                     VerificationOnlyArcMeasurement Reciprocal,
                     VerificationOnlyArcMeasurement Verification,
                     VerificationOnlyArcPlan<Coordinates> Plan) runResult;

                    runResult = await TppaVerificationReceiptRunner.Run(
                        () => VerificationOnlyArcRunner.Run<Coordinates, VerificationOnlyArcMeasurement>(
                            cleanupPointing,
                        async arcToken => {
                            progress?.Report(new ApplicationStatus() { Status = "Running verification-only initial three-point measurement" });
                            var measurement = await MeasureVerificationOnlyArc(
                                context, waypointPlan.Forward, forwardDirection,
                                "PrePositionedUnknown", false, correlatedGuid,
                                PreserveVerificationPoint, progress, arcToken);
                            context.PolarErrorDetermination = measurement.Determination;
                            return measurement;
                        },
                        async arcToken => {
                            progress?.Report(new ApplicationStatus() { Status = "Running verification-only reciprocal three-point measurement" });
                            context.ActivateFirstVerificationStep();
                            var measurement = await MeasureVerificationOnlyArc(
                                context, reciprocalWaypoints, reciprocalDirection,
                                forwardDirection, true, correlatedGuid,
                                PreserveVerificationPoint, progress, arcToken);
                            context.PolarErrorDetermination = measurement.Determination;
                            return measurement;
                        },
                        async arcToken => {
                            progress?.Report(new ApplicationStatus() { Status = "Running verification-only repeat three-point measurement" });
                            var measurement = await MeasureVerificationOnlyArc(
                                context, waypointPlan.Forward, forwardDirection,
                                reciprocalDirection, true, correlatedGuid,
                                PreserveVerificationPoint, progress, arcToken);
                            context.PolarErrorDetermination = measurement.Determination;
                            return measurement;
                        },
                        async (arcStart, arcToken) => {
                            progress?.Report(new ApplicationStatus() { Status = "Returning to A for verification-only repeat" });
                            EnsureVerificationOnlySlewDestinationSafe(arcStart);
                            SetTrackingSidereal(true);
                            await telescopeMediator.SlewToCoordinatesAsync(arcStart, arcToken);
                            EnsureVerificationOnlyActualPositionSafe("return-to-A slew");
                            if (domeMediator.GetInfo().Connected) {
                                await domeMediator.WaitForDomeSynchronization(arcToken);
                            }
                            var returnSettleTimeSeconds = TppaVerificationSettlePolicy.Resolve(
                                profileService.ActiveProfile.TelescopeSettings.SettleTime,
                                VerificationPointSettleTimeSeconds);
                            Logger.Info(
                                $"TPPA verification-only return-to-A settle time: {returnSettleTimeSeconds:F3} seconds.");
                            await CoreUtil.Wait(
                                TimeSpan.FromSeconds(returnSettleTimeSeconds),
                                arcToken,
                                progress,
                                "Settling at verification-only return to A");
                            EnsureVerificationOnlyActualPositionSafe("return-to-A post-settle");
                            context.ActivateFirstVerificationStep();
                        },
                            operationToken),
                        verificationLedger,
                        receipt => {
                            verificationDataset = receipt;
                            Logger.Info("TPPA_VERIFICATION_PARTIAL_DATASET " + receipt.ToJson());
                        });

                    void PreserveVerificationPoint(TppaVerificationPointReceipt point) {
                        var preserved = verificationLedger.Append(point);
                        Logger.Info("TPPA_VERIFICATION_POINT_RECEIPT " + preserved.ToJson());
                    }

                    initialDetermination = runResult.Initial.Determination;
                    reciprocalDetermination = runResult.Reciprocal.Determination;
                    verificationDetermination = runResult.Verification.Determination;
                    reciprocalModelCheckSamples = runResult.Reciprocal.Samples;
                    var actualSolveCount = runResult.Initial.Samples.Count
                        + runResult.Reciprocal.Samples.Count
                        + runResult.Verification.Samples.Count;
                    Logger.Info($"TPPA verification-only captured A pointing {runResult.Plan.ArcStart}; " +
                                $"legacy determinations used {runResult.Plan.TotalSolveCount} solves and " +
                                $"the optional model check used {actualSolveCount - runResult.Plan.TotalSolveCount} additional solves.");

                    var azimuthDeltaDegrees = verificationDetermination.InitialMountAxisAzimuthError.Degree
                        - initialDetermination.InitialMountAxisAzimuthError.Degree;
                    var altitudeDeltaDegrees = verificationDetermination.InitialMountAxisAltitudeError.Degree
                        - initialDetermination.InitialMountAxisAltitudeError.Degree;
                    var totalDeltaDegrees = verificationDetermination.InitialMountAxisTotalError.Degree
                        - initialDetermination.InitialMountAxisTotalError.Degree;
                    var vectorSeparationDegrees = Math.Sqrt(
                        azimuthDeltaDegrees * azimuthDeltaDegrees
                        + altitudeDeltaDegrees * altitudeDeltaDegrees);

                    Logger.Info($"TPPA verification-only initial result: Az: {initialDetermination.InitialMountAxisAzimuthError}, Alt: {initialDetermination.InitialMountAxisAltitudeError}, Tot: {initialDetermination.InitialMountAxisTotalError}");
                    Logger.Info($"TPPA verification-only reciprocal result: Az: {reciprocalDetermination.InitialMountAxisAzimuthError}, Alt: {reciprocalDetermination.InitialMountAxisAltitudeError}, Tot: {reciprocalDetermination.InitialMountAxisTotalError}");
                    Logger.Info($"TPPA verification-only repeated-forward result: Az: {verificationDetermination.InitialMountAxisAzimuthError}, Alt: {verificationDetermination.InitialMountAxisAltitudeError}, Tot: {verificationDetermination.InitialMountAxisTotalError}");
                    Logger.Info($"TPPA verification-only initial geometry: {initialDetermination.ThreePointGeometry.ToLogString()}; qualification=report-only.");
                    Logger.Info($"TPPA verification-only reciprocal geometry: {reciprocalDetermination.ThreePointGeometry.ToLogString()}; qualification=report-only.");
                    Logger.Info($"TPPA verification-only repeated-forward geometry: {verificationDetermination.ThreePointGeometry.ToLogString()}; qualification=report-only.");
                    Logger.Info($"TPPA verification-only repeat-minus-initial delta: Az: {Angle.ByDegree(azimuthDeltaDegrees)}, Alt: {Angle.ByDegree(altitudeDeltaDegrees)}, " +
                                $"magnitude change: {Angle.ByDegree(totalDeltaDegrees)}, vector separation: {Angle.ByDegree(vectorSeparationDegrees)}");

                    await messageBroker.Publish(new PolarAlignmentVerificationMessage(correlatedGuid,
                                                                                       initialDetermination.InitialMountAxisAltitudeError.Degree,
                                                                                       initialDetermination.InitialMountAxisAzimuthError.Degree,
                                                                                       initialDetermination.InitialMountAxisTotalError.Degree,
                                                                                       reciprocalDetermination.InitialMountAxisAltitudeError.Degree,
                                                                                       reciprocalDetermination.InitialMountAxisAzimuthError.Degree,
                                                                                       reciprocalDetermination.InitialMountAxisTotalError.Degree,
                                                                                       verificationDetermination.InitialMountAxisAltitudeError.Degree,
                                                                                       verificationDetermination.InitialMountAxisAzimuthError.Degree,
                                                                                       verificationDetermination.InitialMountAxisTotalError.Degree,
                                                                                       altitudeDeltaDegrees,
                                                                                       azimuthDeltaDegrees,
                                                                                       totalDeltaDegrees));
                },
                async cleanupToken => {
                    Logger.Info($"Restoring the verification-only A/correction pointing {cleanupPointing}.");
                    progress?.Report(new ApplicationStatus() { Status = "Restoring A/correction pointing" });
                    EnsureVerificationOnlySlewDestinationSafe(cleanupPointing);
                    SetTrackingSidereal(true);
                    await telescopeMediator.SlewToCoordinatesAsync(cleanupPointing, cleanupToken);
                    EnsureVerificationOnlyActualPositionSafe("cleanup slew");
                    if (domeMediator.GetInfo().Connected) {
                        await domeMediator.WaitForDomeSynchronization(cleanupToken);
                    }
                },
                token,
                TimeSpan.FromSeconds(120),
                cleanupFailure => {
                    Logger.Error("Verification-only pointing restoration failed after the measurements completed.", cleanupFailure);
                    Notification.ShowWarning(
                        $"Verification measurements completed, but restoring the original A pointing failed: {cleanupFailure.Message}",
                        TimeSpan.FromMinutes(1));
                },
                throwOnCleanupFailure: false);

            var initialAzimuthMinutes = initialDetermination.InitialMountAxisAzimuthError.ArcMinutes;
            var initialAltitudeMinutes = initialDetermination.InitialMountAxisAltitudeError.ArcMinutes;
            var initialTotalMinutes = initialDetermination.InitialMountAxisTotalError.ArcMinutes;
            var reciprocalAzimuthMinutes = reciprocalDetermination.InitialMountAxisAzimuthError.ArcMinutes;
            var reciprocalAltitudeMinutes = reciprocalDetermination.InitialMountAxisAltitudeError.ArcMinutes;
            var reciprocalTotalMinutes = reciprocalDetermination.InitialMountAxisTotalError.ArcMinutes;
            var verificationAzimuthMinutes = verificationDetermination.InitialMountAxisAzimuthError.ArcMinutes;
            var verificationAltitudeMinutes = verificationDetermination.InitialMountAxisAltitudeError.ArcMinutes;
            var verificationTotalMinutes = verificationDetermination.InitialMountAxisTotalError.ArcMinutes;
            var initialVector = TppaPolarErrorVector.FromMinutes(
                initialAzimuthMinutes, initialAltitudeMinutes, initialTotalMinutes);
            var reciprocalVector = TppaPolarErrorVector.FromMinutes(
                reciprocalAzimuthMinutes, reciprocalAltitudeMinutes, reciprocalTotalMinutes);
            var verificationVector = TppaPolarErrorVector.FromMinutes(
                verificationAzimuthMinutes, verificationAltitudeMinutes, verificationTotalMinutes);
            var repeatedForwardPhaseDelta = TppaPolarErrorVector.CircularDistanceDegrees(
                initialVector.PhaseDegrees,
                verificationVector.PhaseDegrees);
            Logger.Info($"TPPA verification-only initial vector diagnostic: {initialVector.ToLogString()}.");
            Logger.Info($"TPPA verification-only reciprocal vector diagnostic: {reciprocalVector.ToLogString()}.");
            Logger.Info($"TPPA verification-only repeated-forward vector diagnostic: {verificationVector.ToLogString()}.");
            Logger.Info($"TPPA verification-only repeated-forward phase delta: {repeatedForwardPhaseDelta:F3} deg.");
            try {
                var overdeterminedShadow = TppaOverdeterminedAxisEstimator.EvaluateShadow(
                    new[] {
                        initialDetermination.FirstPosition.Vector,
                        initialDetermination.SecondPosition.Vector,
                        initialDetermination.ThirdPosition.Vector
                    },
                    new[] {
                        reciprocalDetermination.FirstPosition.Vector,
                        reciprocalDetermination.SecondPosition.Vector,
                        reciprocalDetermination.ThirdPosition.Vector
                    },
                    new[] {
                        verificationDetermination.FirstPosition.Vector,
                        verificationDetermination.SecondPosition.Vector,
                        verificationDetermination.ThirdPosition.Vector
                    },
                    TppaFastQualificationRuntimeAdapter.TruePoleVector(Latitude.Degree));
                Logger.Info("TPPA_OVERDETERMINED_SHADOW " + overdeterminedShadow.ToJson());
                if (OverdeterminedShadowModelCheck && reciprocalModelCheckSamples?.Count == 5) {
                    var distinctSweep = TppaOverdeterminedAxisEstimator.EvaluateDistinctSweep(
                        reciprocalModelCheckSamples.Select(position => position.Vector).ToArray(),
                        new[] {
                            reciprocalDetermination.FirstPosition.Vector,
                            reciprocalDetermination.SecondPosition.Vector,
                            reciprocalDetermination.ThirdPosition.Vector
                        },
                        TppaFastQualificationRuntimeAdapter.TruePoleVector(Latitude.Degree));
                    Logger.Info("TPPA_OVERDETERMINED_5POSITION_SHADOW " + distinctSweep.ToJson());
                }
            } catch (Exception exception) {
                Logger.Warning($"TPPA overdetermined shadow estimator failed. It has no motion or completion authority. " +
                               $"{exception.GetType().Name}: {exception.Message}");
            }
            var verificationAgreement = FreshPolarAlignmentAgreementPolicy.Evaluate(
                initialAzimuthMinutes,
                initialAltitudeMinutes,
                initialTotalMinutes,
                verificationAzimuthMinutes,
                verificationAltitudeMinutes,
                verificationTotalMinutes,
                AlignmentTolerance);
            var initialObservationTimeUtc =
                initialDetermination.InitialReferenceFrame.Coordinates.DateTime.UtcNow;
            var reciprocalObservationTimeUtc =
                reciprocalDetermination.InitialReferenceFrame.Coordinates.DateTime.UtcNow;
            var verificationObservationTimeUtc =
                verificationDetermination.InitialReferenceFrame.Coordinates.DateTime.UtcNow;
            var reciprocalAgreement = FreshPolarAlignmentAgreementPolicy.EvaluateTimeCenteredReciprocity(
                initialAzimuthMinutes,
                initialAltitudeMinutes,
                initialObservationTimeUtc,
                reciprocalAzimuthMinutes,
                reciprocalAltitudeMinutes,
                reciprocalObservationTimeUtc,
                verificationAzimuthMinutes,
                verificationAltitudeMinutes,
                verificationObservationTimeUtc,
                AlignmentTolerance);
            var diagnosticPassed = verificationAgreement.IsRepeatable && reciprocalAgreement.IsRepeatable;
            Logger.Info($"TPPA verification-only repeatability verdict: {(verificationAgreement.IsRepeatable ? "PASS" : "FAIL")}; " +
                        $"threshold={verificationAgreement.ThresholdMinutes:F2}'; {verificationAgreement.Reason}.");
            Logger.Info($"TPPA verification-only reciprocity verdict: {(reciprocalAgreement.IsRepeatable ? "PASS" : "FAIL")}; " +
                        $"dAz={reciprocalAgreement.AzimuthDeltaMinutes:+0.00;-0.00;0.00}', " +
                        $"dAlt={reciprocalAgreement.AltitudeDeltaMinutes:+0.00;-0.00;0.00}', " +
                        $"magnitude change={reciprocalAgreement.TotalDeltaMinutes:+0.00;-0.00;0.00}', " +
                        $"vector separation={reciprocalAgreement.VectorDeltaMinutes:F2}', " +
                        $"threshold={reciprocalAgreement.ThresholdMinutes:F2}'; {reciprocalAgreement.Reason}.");
            Logger.Info($"TPPA verification-only reciprocity timing: firstForwardUtc={initialObservationTimeUtc:O}; " +
                        $"reciprocalUtc={reciprocalObservationTimeUtc:O}; repeatedForwardUtc={verificationObservationTimeUtc:O}.");
            var verificationSummary =
                $"Verification-only measurements complete. Overall: {(diagnosticPassed ? "PASS" : "FAIL")}.{Environment.NewLine}" +
                $"Repeatability: {(verificationAgreement.IsRepeatable ? "PASS" : "FAIL")}; reciprocity: {(reciprocalAgreement.IsRepeatable ? "PASS" : "FAIL")}.{Environment.NewLine}" +
                $"Reciprocity detail: {reciprocalAgreement.Reason}; " +
                $"vector separation {reciprocalAgreement.VectorDeltaMinutes:F2}'{Environment.NewLine}" +
                $"Initial: Az {initialAzimuthMinutes:F2}', Alt {initialAltitudeMinutes:F2}', Total {initialTotalMinutes:F2}'{Environment.NewLine}" +
                $"Reciprocal: Az {reciprocalAzimuthMinutes:F2}', Alt {reciprocalAltitudeMinutes:F2}', Total {reciprocalTotalMinutes:F2}'{Environment.NewLine}" +
                $"Repeated forward: Az {verificationAzimuthMinutes:F2}', Alt {verificationAltitudeMinutes:F2}', Total {verificationTotalMinutes:F2}'{Environment.NewLine}" +
                $"Delta: Az {verificationAzimuthMinutes - initialAzimuthMinutes:+0.00;-0.00;0.00}', " +
                $"Alt {verificationAltitudeMinutes - initialAltitudeMinutes:+0.00;-0.00;0.00}', " +
                $"magnitude change {verificationAgreement.TotalDeltaMinutes:+0.00;-0.00;0.00}', " +
                $"vector separation {verificationAgreement.VectorDeltaMinutes:F2}'{Environment.NewLine}" +
                $"Repeatability limit: {verificationAgreement.ThresholdMinutes:F2}'. " +
                "This checks internal consistency, not absolute polar-alignment accuracy.";
            if (diagnosticPassed) {
                Notification.ShowInformation(verificationSummary, TimeSpan.FromMinutes(1));
            } else {
                Notification.ShowWarning(verificationSummary, TimeSpan.FromMinutes(1));
            }
            var verificationRunSummary = TppaVerificationRunSummary.Create(
                correlatedGuid,
                verificationStartedUtc,
                DateTime.UtcNow,
                verificationAgreement.IsRepeatable,
                reciprocalAgreement.IsRepeatable,
                OverdeterminedShadowModelCheck ? 11 : 9,
                Properties.Settings.Default.RefractionAdjustment,
                OverdeterminedShadowModelCheck,
                initialTotalMinutes,
                reciprocalTotalMinutes,
                verificationTotalMinutes,
                verificationAgreement.VectorDeltaMinutes,
                reciprocalAgreement.VectorDeltaMinutes);
            Logger.Info("TPPA_VERIFICATION_RUN_SUMMARY " + verificationRunSummary.ToJson());
            PersistQualificationRunEvidence(
                correlatedGuid,
                verificationStartedUtc,
                verificationDataset,
                initialDetermination,
                reciprocalDetermination,
                verificationDetermination);
            progress?.Report(GetStatus($"Verification-only measurements complete: {(diagnosticPassed ? "passed" : "failed")}"));
            if (!diagnosticPassed) {
                throw new SequenceEntityFailedException(
                    "Verification-only polar-alignment diagnostics failed repeatability or reciprocity validation.");
            }
        }

        private void PersistQualificationRunEvidence(
                Guid runId,
                DateTime runStartedUtc,
                TppaVerificationPartialDatasetReceipt dataset,
                PolarErrorDetermination initial,
                PolarErrorDetermination reciprocal,
                PolarErrorDetermination repeatedForward) {
            if (dataset?.IsComplete != true || dataset.Samples == null
                    || initial == null || reciprocal == null || repeatedForward == null) {
                Logger.Warning("TPPA qualification run evidence was not emitted because the completed immutable dataset is unavailable.");
                return;
            }

            try {
                var samples = dataset.Samples.OrderBy(value => value.SequenceIndex).ToArray();
                var reciprocalSampleCount = samples.Length - 6;
                if (reciprocalSampleCount != 3 && reciprocalSampleCount != 5) {
                    Logger.Warning($"TPPA qualification run evidence was not emitted because reciprocal sample count {reciprocalSampleCount} is unsupported.");
                    return;
                }
                var initialPoints = SelectLegacyTriad(samples.Take(3).ToArray());
                var reciprocalPoints = SelectLegacyTriad(
                    samples.Skip(3).Take(reciprocalSampleCount).ToArray());
                var repeatedPoints = SelectLegacyTriad(samples.Skip(3 + reciprocalSampleCount).Take(3).ToArray());
                var determinations = new[] {
                    BuildDetermination("initial-forward", runStartedUtc, initialPoints, initial),
                    BuildDetermination("reciprocal", reciprocalPoints[0].ObservationUtc, reciprocalPoints, reciprocal),
                    BuildDetermination("repeated-forward", repeatedPoints[0].ObservationUtc, repeatedPoints, repeatedForward)
                };

                var assembly = typeof(PolarAlignment).Assembly;
                var pluginAssembly = TppaLoadedAssemblyEvidenceFactory.Capture(assembly);
                var qualificationCoreAssembly = TppaLoadedAssemblyEvidenceFactory.Capture(
                    typeof(TppaAbsoluteEvidenceBinder).Assembly);
                var pipelineDigest = pluginAssembly.Sha256;
                var instrumentManifest = FormattableString.Invariant(
                    $"pixelSize={profileService.ActiveProfile.CameraSettings.PixelSize:R}|focalLength={profileService.ActiveProfile.TelescopeSettings.FocalLength:R}|binningX={Binning?.X ?? 1}|binningY={Binning?.Y ?? 1}|gain={Gain}|offset={Offset}|filter={Filter?.Name ?? "none"}|exposure={ExposureTime:R}");
                var instrumentId = TppaQualificationRunEvidenceProducer.Sha256Utf8(instrumentManifest);
                var hardwareManifest = FormattableString.Invariant(
                    $"instrument={instrumentId}|targetDistance={TargetDistance}|eastDirection={EastDirection}|refraction={Properties.Settings.Default.RefractionAdjustment}|pluginPipeline={pipelineDigest}");
                var hardwareConfigurationId =
                    TppaQualificationRunEvidenceProducer.Sha256Utf8(hardwareManifest);
                var mechanicalStateDigest = TppaQualificationRunEvidenceProducer.Sha256Utf8(
                    $"hardware={hardwareConfigurationId}|run={runId:D}|correctionSequence=0");
                var process = Process.GetCurrentProcess();
                var processStartUtc = process.StartTime.ToUniversalTime();
                var sessionId = $"{Environment.MachineName}:{process.Id}:{processStartUtc:O}";
                var clockDomainId = $"windows-utc:{Environment.MachineName}:{processStartUtc:O}";
                var solver = plateSolverFactory.GetPlateSolver(
                    profileService.ActiveProfile.PlateSolveSettings);
                var solverIdentity = solver?.GetType().AssemblyQualifiedName
                    ?? "unknown-plate-solver";
                var siteManifest = FormattableString.Invariant(
                    $"latitude={Latitude.Degree:R}|longitude={Longitude.Degree:R}|elevation={Elevation:R}");
                var siteId = TppaQualificationRunEvidenceProducer.Sha256Utf8(siteManifest);
                var weather = weatherDataMediator.GetInfo();
                var refraction = RefractionParameters.GetRefractionParameters(weather);
                var atmosphere = new TppaRuntimeAtmosphereEvidence(
                    Source: "standard-atmosphere-fallback",
                    ObservationUtc: DateTime.UtcNow,
                    refraction.PressureHPa,
                    refraction.Temperature,
                    refraction.RelativeHumidity);
                var metadata = new TppaQualificationRunProductionMetadata(
                    runId.ToString("D"),
                    sessionId,
                    $"nina-tppa-runtime:{assembly.GetName().Name}:{assembly.GetName().Version}",
                    pipelineDigest,
                    CorrectionSequenceNumber: 0,
                    new TppaRuntimeIdentityEvidence(
                        hardwareConfigurationId,
                        mechanicalStateDigest,
                        clockDomainId,
                        ClockUncertaintyMilliseconds: double.MaxValue,
                        instrumentId,
                        solverIdentity,
                        TppaFastQualificationConventions.IcrsObservationEpoch,
                        TppaFastQualificationRuntimeAdapter.TopocentricHorizonNorthWestUp),
                    new TppaRuntimeSiteEvidence(
                        Latitude.Degree,
                        Longitude.Degree,
                        Elevation,
                        siteId),
                    atmosphere,
                    Properties.Settings.Default.RefractionAdjustment,
                    pluginAssembly,
                    qualificationCoreAssembly);
                var outputDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NINA",
                    "TPPAQualificationEvidence");
                var result = TppaQualificationRunEvidenceProducer.Produce(
                    metadata,
                    determinations,
                    outputDirectory);
                if (!result.Produced) {
                    Logger.Warning("TPPA qualification run evidence failed closed: " +
                                   string.Join("; ", result.ProductionIssues));
                    return;
                }
                Logger.Info($"TPPA_QUALIFICATION_RUN_EVIDENCE path={result.OutputPath}; " +
                            $"sha256={TppaQualificationRunEvidenceProducer.Sha256File(result.OutputPath)}; " +
                            $"qualificationIssues={string.Join(" | ", result.QualificationIssues)}");
            } catch (Exception exception) when (exception is not OutOfMemoryException) {
                Logger.Warning("TPPA qualification run evidence failed closed. " +
                               $"{exception.GetType().Name}: {exception.Message}");
            }

            static IReadOnlyList<TppaVerificationPointReceipt> SelectLegacyTriad(
                    IReadOnlyList<TppaVerificationPointReceipt> values) {
                if (values?.Count == 3) { return values.ToArray(); }
                if (values?.Count == 5) { return new[] { values[0], values[2], values[4] }; }
                throw new ArgumentException("A three- or five-point arc is required.", nameof(values));
            }

            TppaQualificationDeterminationInput BuildDetermination(
                    string role,
                    DateTime startedUtc,
                    IReadOnlyList<TppaVerificationPointReceipt> points,
                    PolarErrorDetermination determination) =>
                new(
                    $"{runId:D}-{role}",
                    startedUtc,
                    points[^1].ObservationUtc,
                    points,
                    determination.InitialMountAxisErrorPosition.Vector,
                    determination.ThreePointGeometry);
        }

        private async Task<VerificationOnlyArcMeasurement> MeasureVerificationOnlyArc(
                TPAPAVM context,
                IReadOnlyList<Coordinates> waypoints,
                string direction,
                string firstPointApproachDirection,
                bool firstPointApproachDirectionKnown,
                Guid runId,
                Action<TppaVerificationPointReceipt> preservePoint,
                IProgress<ApplicationStatus> progress,
                CancellationToken token) {
            if (waypoints == null || (waypoints.Count != 3 && waypoints.Count != 5)) {
                throw new SequenceEntityFailedException(
                    "Verification-only measurement requires exactly three legacy waypoints or five shadow-model waypoints.");
            }
            var refractionParameter = RefractionParameters.GetRefractionParameters(weatherDataMediator.GetInfo());
            var solves = new PlateSolveResult[waypoints.Count];
            var positions = new Position[waypoints.Count];
            var mountConnected = new bool[waypoints.Count];
            var mountDeclinations = new double[waypoints.Count];
            var middleIndex = waypoints.Count / 2;
            var stepTravelDegrees = TargetDistance * 2.0 / (waypoints.Count - 1);

            for (var index = 0; index < waypoints.Count; index++) {
                if (index == 0) {
                    context.ActivateFirstVerificationStep();
                    EnsureVerificationOnlyWaypointReached(
                        waypoints[index], $"verification-only {direction} point {index + 1}");
                } else {
                    if (index <= middleIndex) {
                        context.ActivateSecondStep();
                    } else {
                        context.ActivateThirdStep();
                    }
                    await SlewToVerificationWaypoint(
                        waypoints[index],
                        stepTravelDegrees,
                        $"verification-only {direction} point {index + 1}",
                        progress,
                        token);
                }
                solves[index] = await SolveVerificationPoint(context, 5.0, progress, token, runId);
                RecordVerificationOnlyPoint(index);
            }

            var firstObservationUtc = solves[0].Coordinates.DateTime.UtcNow;
            var finalObservationUtc = solves[^1].Coordinates.DateTime.UtcNow;
            Logger.Info($"TPPA verification-only arc timing: direction={direction}; " +
                        $"point1Utc={firstObservationUtc:O}; point{waypoints.Count}Utc={finalObservationUtc:O}; " +
                        $"sampleCount={waypoints.Count}; spanSeconds={(finalObservationUtc - firstObservationUtc).TotalSeconds:F3}; " +
                        $"exposureSeconds={ExposureTime:F3}.");
            var decSpread = Angle.Zero;
            if (mountConnected.All(connected => connected)) {
                decSpread = Angle.ByDegree(mountDeclinations.Max() - mountDeclinations.Min());
            }

            var determination = await Task.Run(() => new PolarErrorDetermination(
                solves[^1],
                positions[0],
                positions[middleIndex],
                positions[^1],
                Latitude,
                Longitude,
                Elevation,
                refractionParameter,
                Properties.Settings.Default.RefractionAdjustment,
                decSpread.ArcSeconds),
                token);
            return new VerificationOnlyArcMeasurement(determination, positions);

            void RecordVerificationOnlyPoint(int index) {
                var mountInfo = telescopeMediator.GetInfo();
                mountConnected[index] = mountInfo.Connected;
                mountDeclinations[index] = mountInfo.Declination;
                positions[index] = new Position(solves[index].Coordinates,
                                                solves[index].PositionAngle,
                                                Latitude,
                                                Longitude,
                                                Elevation,
                                                refractionParameter);
                var mountInfoSuffix = mountInfo.Connected
                    ? $" - Mount RA: {mountInfo.RightAscensionString}; Mount Dec: {mountInfo.DeclinationString}"
                    : string.Empty;
                Logger.Info($"TPPA verification-only point telemetry: direction={direction}; point={index + 1}; " +
                            $"observationUtc={solves[index].Coordinates.DateTime.UtcNow:O}; mountAz={mountInfo.Azimuth:F6}; " +
                            $"mountAlt={mountInfo.Altitude:F6}; solveRa={solves[index].Coordinates.RADegrees:F9}; " +
                            $"solveDec={solves[index].Coordinates.Dec:F9}; vector={positions[index].Vector}; " +
                            $"positionAngle={positions[index].PositionAngle}{mountInfoSuffix}");
                preservePoint?.Invoke(new TppaVerificationPointReceipt(
                    TppaVerificationPointReceipt.CurrentSchemaVersion,
                    runId,
                    SequenceIndex: 0,
                    direction,
                    PointIndex: index + 1,
                    DirectionSampleCount: waypoints.Count,
                    solves[index].Coordinates.DateTime.UtcNow,
                    mountInfo.Azimuth,
                    mountInfo.Altitude,
                    solves[index].Coordinates.RADegrees,
                    solves[index].Coordinates.Dec,
                    positions[index].PositionAngle,
                    positions[index].Vector.X,
                    positions[index].Vector.Y,
                    positions[index].Vector.Z,
                    mountInfo.SideOfPier));
                try {
                    var observation = TppaDriftRuntimeMetadataFactory.Create(
                        $"{direction}-{index + 1}",
                        solves[index].Coordinates,
                        Latitude,
                        Longitude,
                        Elevation,
                        refractionParameter);
                    var isFirstPoint = index == 0;
                    var contextEvidence = TppaVerificationPointContextEvidenceFactory.Create(
                        runId,
                        direction,
                        index + 1,
                        isFirstPoint ? firstPointApproachDirection : direction,
                        isFirstPoint ? firstPointApproachDirectionKnown : true,
                        isFirstPoint ? null : stepTravelDegrees,
                        isFirstPoint
                            ? null
                            : TppaVerificationSettlePolicy.Resolve(
                                profileService.ActiveProfile.TelescopeSettings.SettleTime,
                                VerificationPointSettleTimeSeconds),
                        observation,
                        Properties.Settings.Default.RefractionAdjustment,
                        refractionParameter);
                    Logger.Info("TPPA_VERIFICATION_POINT_CONTEXT " + contextEvidence.ToJson());
                } catch (Exception exception) when (exception is not OutOfMemoryException) {
                    Logger.Warning(
                        "TPPA verification point context evidence failed report-only. " +
                        $"{exception.GetType().Name}: {exception.Message}");
                }
            }
        }
        private void EnsureVerificationOnlyWaypointReached(Coordinates destination, string operation) {
            EnsureVerificationOnlyActualPositionSafe(operation);
            var currentPosition = telescopeMediator.GetCurrentPosition();
            var mountInfo = telescopeMediator.GetInfo();
            if (mountInfo.SideOfPier == PierSide.pierUnknown) {
                throw new SequenceEntityFailedException(
                    $"{operation} rejected because pier-side telemetry is unknown.");
            }

            var rightAscensionError = Distance(currentPosition.RADegrees, destination.RADegrees);
            var declinationError = Math.Abs(currentPosition.Dec - destination.Dec);
            if (rightAscensionError > 0.25 || declinationError > 0.05) {
                throw new SequenceEntityFailedException(
                    $"{operation} waypoint verification rejected: RA error {rightAscensionError:F3} deg, " +
                    $"Dec error {declinationError:F3} deg.");
            }

            Logger.Info(
                $"TPPA {operation} waypoint verification passed: RA error={rightAscensionError:F3} deg, " +
                $"Dec error={declinationError:F3} deg, pier side={mountInfo.SideOfPier}.");
        }
        private async Task SlewToVerificationWaypoint(
                Coordinates destination,
                double expectedTravelDegrees,
                string operation,
                IProgress<ApplicationStatus> progress,
                CancellationToken token) {
            var startPosition = telescopeMediator.GetCurrentPosition();
            var startInfo = telescopeMediator.GetInfo();
            if (!startInfo.Connected || startInfo.Slewing) {
                throw new SequenceEntityFailedException(
                    $"{operation} rejected because connected stationary mount telemetry is unavailable.");
            }

            EnsureVerificationOnlySlewDestinationSafe(destination);
            SetTrackingSidereal(true);
            var trackingConfirmed = false;
            for (var attempt = 0; attempt < 20; attempt++) {
                var trackingInfo = telescopeMediator.GetInfo();
                if (trackingInfo.TrackingEnabled
                        && trackingInfo.TrackingRate.TrackingMode
                            == Equipment.Interfaces.TrackingMode.Sidereal) {
                    trackingConfirmed = true;
                    break;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(100), token);
            }
            if (!trackingConfirmed) {
                throw new SequenceEntityFailedException(
                    $"{operation} rejected because sidereal tracking could not be confirmed within two seconds.");
            }

            progress?.Report(new ApplicationStatus() { Status = $"Slewing to {operation}" });
            Logger.Info(
                $"TPPA {operation} precise waypoint slew: from RA={startPosition.RADegrees:F6} deg, " +
                $"Dec={startPosition.Dec:F6} deg to RA={destination.RADegrees:F6} deg, " +
                $"Dec={destination.Dec:F6} deg; expected RA travel={expectedTravelDegrees:F3} deg.");
            await telescopeMediator.SlewToCoordinatesAsync(destination, token);
            EnsureVerificationOnlyActualPositionSafe(operation);

            var firstPosition = telescopeMediator.GetCurrentPosition();
            var firstInfo = telescopeMediator.GetInfo();
            await Task.Delay(TimeSpan.FromSeconds(1), token);
            EnsureVerificationOnlyActualPositionSafe($"{operation} fresh-telemetry confirmation");
            var finalPosition = telescopeMediator.GetCurrentPosition();
            var finalInfo = telescopeMediator.GetInfo();
            VerifyTravel(firstPosition, firstInfo.SideOfPier, "first");
            VerifyTravel(finalPosition, finalInfo.SideOfPier, "confirmed");
            if (Distance(firstPosition.RADegrees, finalPosition.RADegrees) > 0.05
                    || Math.Abs(firstPosition.Dec - finalPosition.Dec) > 0.05
                    || firstInfo.SideOfPier != finalInfo.SideOfPier) {
                throw new SequenceEntityFailedException(
                    $"{operation} rejected because the two post-slew telemetry samples were not stable.");
            }

            var settleTimeSeconds = TppaVerificationSettlePolicy.Resolve(
                profileService.ActiveProfile.TelescopeSettings.SettleTime,
                VerificationPointSettleTimeSeconds);
            Logger.Info($"TPPA precise waypoint settle time: {settleTimeSeconds:F3} seconds.");
            await CoreUtil.Wait(TimeSpan.FromSeconds(settleTimeSeconds), token, progress, "Settling");
            EnsureVerificationOnlyActualPositionSafe($"{operation} post-settle");
            var settledPosition = telescopeMediator.GetCurrentPosition();
            var settledInfo = telescopeMediator.GetInfo();
            VerifyTravel(settledPosition, settledInfo.SideOfPier, "post-settle");

            void VerifyTravel(Coordinates endPosition, PierSide endPierSide, string sample) {
                if (startInfo.SideOfPier == PierSide.pierUnknown
                        || endPierSide == PierSide.pierUnknown) {
                    throw new SequenceEntityFailedException(
                        $"{operation} {sample} travel verification rejected because pier-side telemetry is unknown.");
                }
                var result = TppaDriftMoveVerificationPolicy.Evaluate(
                    startPosition.RADegrees,
                    endPosition.RADegrees,
                    startPosition.Dec,
                    endPosition.Dec,
                    expectedTravelDegrees,
                    startInfo.SideOfPier,
                    endPierSide,
                    maximumUndertravelDegrees: 0.25,
                    maximumOvershootDegrees: 0.25,
                    maximumDeclinationTravelDegrees: 0.05);
                if (!result.IsSafe) {
                    throw new SequenceEntityFailedException(
                        $"{operation} {sample} travel verification rejected: {result.Reason}.");
                }
                var destinationRaError = Distance(endPosition.RADegrees, destination.RADegrees);
                var destinationDecError = Math.Abs(endPosition.Dec - destination.Dec);
                if (destinationRaError > 0.25 || destinationDecError > 0.05) {
                    throw new SequenceEntityFailedException(
                        $"{operation} {sample} destination verification rejected: " +
                        $"RA error {destinationRaError:F3} deg, Dec error {destinationDecError:F3} deg.");
                }
                Logger.Info(
                    $"TPPA {operation} {sample} travel verification passed: " +
                    $"RA={result.RightAscensionTravelDegrees:F3} deg, " +
                    $"Dec={result.DeclinationTravelDegrees:F3} deg; destination RA error={destinationRaError:F3} deg; " +
                    $"destination Dec error={destinationDecError:F3} deg; {result.Reason}.");
            }
        }

        private async Task<PolarErrorDetermination> MeasureFreshThreePointCompletionVerification(TPAPAVM context,
                                                                                                  Coordinates automatedVerificationStartPointing,
                                                                                                  bool eastDirection,
                                                                                                  IProgress<ApplicationStatus> progress,
                                                                                                  CancellationToken token) {
            var completionVerificationStopwatch = Stopwatch.StartNew();
            var threePointMilliseconds = double.NaN;
            var correctionPointing = telescopeMediator.GetCurrentPosition();
            var returnToCorrectionPointing = !ManualMode && telescopeMediator.GetInfo().Connected;
            var refractionParameter = RefractionParameters.GetRefractionParameters(weatherDataMediator.GetInfo());
            var solves = new PlateSolveResult[3];
            var positions = new Position[3];
            var mountConnected = new bool[3];
            var mountDeclinations = new double[3];
            PlateSolveResult returnSolve = null;
            Exception measurementFailure = null;

            try {
                if (!ManualMode && automatedVerificationStartPointing != null) {
                    Logger.Info($"Slewing to the original first measurement pointing {automatedVerificationStartPointing} so verification repeats the same mount arc and direction.");
                    progress?.Report(new ApplicationStatus() { Status = "Returning to first three-point measurement position" });
                    SetTrackingSidereal(true);
                    await telescopeMediator.SlewToCoordinatesAsync(automatedVerificationStartPointing, token);
                    if (domeMediator.GetInfo().Connected) {
                        await domeMediator.WaitForDomeSynchronization(token);
                    }
                }

                solves[0] = await Solve(context, 5.0, progress, token);
                var mountInfo0 = telescopeMediator.GetInfo();
                mountConnected[0] = mountInfo0.Connected;
                mountDeclinations[0] = mountInfo0.Declination;
                positions[0] = new Position(solves[0].Coordinates, solves[0].PositionAngle, Latitude, Longitude, Elevation, refractionParameter);
                var mountInfoSuffix0 = mountInfo0.Connected ? $" - Mount RA: {mountInfo0.RightAscensionString}; Mount Dec: {mountInfo0.DeclinationString}" : string.Empty;
                Logger.Info($"Completion verification first measurement point {solves[0].Coordinates} - Vector: {positions[0].Vector} - Position Angle: {positions[0].PositionAngle}{mountInfoSuffix0}");

                solves[1] = !ManualMode
                    ? await AutomatedNextPoint(progress, token, eastDirection)
                    : await ManualNextPoint(solves[0], progress, token);
                var mountInfo1 = telescopeMediator.GetInfo();
                mountConnected[1] = mountInfo1.Connected;
                mountDeclinations[1] = mountInfo1.Declination;
                positions[1] = new Position(solves[1].Coordinates, solves[1].PositionAngle, Latitude, Longitude, Elevation, refractionParameter);
                var mountInfoSuffix1 = mountInfo1.Connected ? $" - Mount RA: {mountInfo1.RightAscensionString}; Mount Dec: {mountInfo1.DeclinationString}" : string.Empty;
                Logger.Info($"Completion verification second measurement point {solves[1].Coordinates} - Vector: {positions[1].Vector} - Position Angle: {positions[1].PositionAngle}{mountInfoSuffix1}");

                if (!ManualMode) {
                    solves[2] = await AutomatedNextPoint(progress, token, eastDirection);
                } else {
                    solves[2] = await ManualNextPoint(solves[1], progress, token);
                    await CoreUtil.Wait(TimeSpan.FromSeconds(10), token, progress, "Waiting for things to settle. Make sure the scope is tracking and don't move any further!");
                    solves[2] = await Solve(context, 5.0, progress, token);
                }
                var mountInfo2 = telescopeMediator.GetInfo();
                mountConnected[2] = mountInfo2.Connected;
                mountDeclinations[2] = mountInfo2.Declination;
                positions[2] = new Position(solves[2].Coordinates, solves[2].PositionAngle, Latitude, Longitude, Elevation, refractionParameter);
                var mountInfoSuffix2 = mountInfo2.Connected ? $" - Mount RA: {mountInfo2.RightAscensionString}; Mount Dec: {mountInfo2.DeclinationString}" : string.Empty;
                Logger.Info($"Completion verification third measurement point {solves[2].Coordinates} - Vector: {positions[2].Vector} - Position Angle: {positions[2].PositionAngle}{mountInfoSuffix2}");
                threePointMilliseconds = completionVerificationStopwatch.Elapsed.TotalMilliseconds;
                Logger.Info(
                    $"TPPA_COMPLETION_VERIFICATION_TIMING schemaVersion=1; phase=three-point; " +
                    $"threePointMilliseconds={threePointMilliseconds:F1}; totalMilliseconds={threePointMilliseconds:F1}");
            } catch (Exception ex) {
                measurementFailure = ex;
                throw;
            } finally {
                if (returnToCorrectionPointing && !token.IsCancellationRequested) {
                    try {
                        var returnFieldStopwatch = Stopwatch.StartNew();
                        Logger.Info($"Returning to the pre-verification correction pointing {correctionPointing}.");
                        progress?.Report(new ApplicationStatus() { Status = "Returning to correction pointing" });
                        SetTrackingSidereal(true);
                        await telescopeMediator.SlewToCoordinatesAsync(correctionPointing, token);
                        if (domeMediator.GetInfo().Connected) {
                            await domeMediator.WaitForDomeSynchronization(token);
                        }

                        returnSolve = await Solve(context, 5.0, progress, token);
                        if (returnSolve?.Success != true) {
                            throw new InvalidOperationException("Unable to plate solve the returned correction field after fresh completion verification. Automated correction was stopped to avoid mixing reference frames.");
                        }
                        returnFieldStopwatch.Stop();
                        Logger.Info($"Completion verification captured the returned correction reference frame {returnSolve.Coordinates}.");
                        var returnPosition = new Position(returnSolve.Coordinates,
                                                          returnSolve.PositionAngle,
                                                          Latitude,
                                                          Longitude,
                                                          Elevation,
                                                          refractionParameter);
                        var threeSolveShadowEvidence = TppaThreeSolveShadowEvaluator.Evaluate(
                            new TppaThreeSolveShadowSample(
                                positions[2].Topocentric.Azimuth.Degree,
                                positions[2].PositionAngle,
                                positions[2].Vector.X,
                                positions[2].Vector.Y,
                                positions[2].Vector.Z),
                            new TppaThreeSolveShadowSample(
                                returnPosition.Topocentric.Azimuth.Degree,
                                returnPosition.PositionAngle,
                                returnPosition.Vector.X,
                                returnPosition.Vector.Y,
                                returnPosition.Vector.Z));
                        Logger.Info(threeSolveShadowEvidence.ToLogString());
                        Logger.Info(
                            $"TPPA_COMPLETION_VERIFICATION_TIMING schemaVersion=1; phase=return-field; " +
                            $"threePointMilliseconds={threePointMilliseconds:F1}; " +
                            $"returnFieldMilliseconds={returnFieldStopwatch.Elapsed.TotalMilliseconds:F1}; " +
                            $"totalMilliseconds={completionVerificationStopwatch.Elapsed.TotalMilliseconds:F1}");
                    } catch (Exception returnFailure) {
                        if (measurementFailure == null) {
                            throw;
                        }
                        Logger.Error("Returning to the correction field also failed; preserving the original fresh-measurement exception.", returnFailure);
                    }
                }
            }

            var decSpread = Angle.Zero;
            if (mountConnected.All(connected => connected)) {
                decSpread = Angle.ByDegree(mountDeclinations.Max() - mountDeclinations.Min());
            }

            var correctionReferenceFrame = returnToCorrectionPointing ? returnSolve : solves[2];
            return await Task.Run(() => new PolarErrorDetermination(correctionReferenceFrame,
                                                                     positions[0],
                                                                     positions[1],
                                                                     positions[2],
                                                                     Latitude,
                                                                     Longitude,
                                                                     Elevation,
                                                                     refractionParameter,
                                                                     Properties.Settings.Default.RefractionAdjustment,
                                                                     decSpread.ArcSeconds),
                                  token);
        }

        private static void StampSolveObservationTime(PlateSolveResult result, DateTime observationTimeUtc) {
            if (result?.Success != true || result.Coordinates == null) {
                return;
            }

            var source = result.Coordinates;
            result.Coordinates = new Coordinates(Angle.ByDegree(source.RADegrees),
                                                 Angle.ByDegree(source.Dec),
                                                 source.Epoch,
                                                 new FixedObservationDateTime(observationTimeUtc));
            Logger.Info($"TPPA plate solve timestamp fixed to exposure midpoint: observationUtc={observationTimeUtc:O}");
        }

        private Task<PlateSolveResult> Solve(
                TPAPAVM context,
                double searchRadiusIncrementOnFailure,
                IProgress<ApplicationStatus> progress,
                CancellationToken token) =>
            SolveCore(context, searchRadiusIncrementOnFailure, progress, token, null);

        private Task<PlateSolveResult> SolveVerificationPoint(
                TPAPAVM context,
                double searchRadiusIncrementOnFailure,
                IProgress<ApplicationStatus> progress,
                CancellationToken token,
                Guid diagnosticRunId) =>
            SolveCore(context, searchRadiusIncrementOnFailure, progress, token, diagnosticRunId);

        private async Task<PlateSolveResult> SolveCore(
                TPAPAVM context,
                double searchRadiusIncrementOnFailure,
                IProgress<ApplicationStatus> progress,
                CancellationToken token,
                Guid? diagnosticRunId) {
            var retryPolicy = TppaSolveRetryPolicy.FieldDefault;
            PlateSolveResult result = new PlateSolveResult { Success = false };
            double usedSearchRadius = SearchRadius;
            var attempt = 0;
            do {
                attempt++;
                token.ThrowIfCancellationRequested();
                var attemptStopwatch = Stopwatch.StartNew();

                var solver = plateSolverFactory.GetPlateSolver(profileService.ActiveProfile.PlateSolveSettings);
                var requestedCoordinates = telescopeMediator.GetCurrentPosition();
                IPlateSolver blindSolver = null;
                if (ManualMode && !telescopeMediator.GetInfo().Connected) {
                    Logger.Debug("Manual mode is enabled and no telescope is connected. Spawning the blind solver");
                    blindSolver = plateSolverFactory.GetBlindSolver(profileService.ActiveProfile.PlateSolveSettings);
                }

                var seq = new CaptureSequence() { Binning = Binning, Gain = Gain, ExposureTime = ExposureTime, Offset = Offset, FilterType = Filter, ImageType = ImageTypes.SNAPSHOT };
                var captureStartedUtc = DateTime.UtcNow;
                DateTime? observationTimeUtc = null;
                IRenderedImage image = null;
                Exception captureException = null;
                var captureStopwatch = Stopwatch.StartNew();
                try {
                    progress.Report(new ApplicationStatus() { Status = $"Capturing new image to solve..." });
                    image = await imagingMediator.CaptureAndPrepareImage(seq,
                                                                         new PrepareImageParameters(true, false),
                                                                         token,
                                                                         progress).ConfigureAwait(false);
                } catch (OperationCanceledException ex) {
                    Logger.Info("TPPA_SOLVE_ATTEMPT " + new TppaSolveAttemptProvenance(
                        TppaSolveAttemptProvenance.CurrentSchemaVersion,
                        attempt,
                        retryPolicy.MaximumAttempts,
                        captureStartedUtc,
                        ObservationTimeUtc: null,
                        seq.ExposureTime,
                        usedSearchRadius,
                        requestedCoordinates?.RADegrees,
                        requestedCoordinates?.Dec,
                        solver?.GetType().FullName,
                        CaptureSucceeded: false,
                        SolveSucceeded: false,
                        SolvedRightAscensionDegrees: null,
                        SolvedDeclinationDegrees: null,
                        FailureKind: "capture-cancelled",
                        FailureMessage: ex.Message).ToJson());
                    throw;
                } catch (Exception ex) {
                    captureException = ex;
                    Logger.Error(ex);
                } finally {
                    captureStopwatch.Stop();
                }

                token.ThrowIfCancellationRequested();

                if (image != null) {
                    observationTimeUtc = captureStartedUtc.AddSeconds(Math.Max(0, seq.ExposureTime) / 2.0);
                    context.Image = image;

                    var imageSolver = plateSolverFactory.GetImageSolver(solver, blindSolver);

                    var parameter = new PlateSolveParameter() {
                        Binning = Binning?.X ?? 1,
                        Coordinates = requestedCoordinates,
                        DownSampleFactor = profileService.ActiveProfile.PlateSolveSettings.DownSampleFactor,
                        FocalLength = profileService.ActiveProfile.TelescopeSettings.FocalLength,
                        MaxObjects = profileService.ActiveProfile.PlateSolveSettings.MaxObjects,
                        PixelSize = profileService.ActiveProfile.CameraSettings.PixelSize,
                        Regions = profileService.ActiveProfile.PlateSolveSettings.Regions,
                        SearchRadius = usedSearchRadius,
                        DisableNotifications = true
                    };

                    progress.Report(new ApplicationStatus() { Status = $"Solving image..." });
                    Exception solverException = null;
                    var solveStopwatch = Stopwatch.StartNew();
                    try {
                        result = await imageSolver.Solve(image.RawImageData,
                                                         parameter,
                                                         progress,
                                                         token).ConfigureAwait(false);
                        StampSolveObservationTime(result, observationTimeUtc.Value);
                    } catch (Exception ex) when (token.IsCancellationRequested) {
                        Logger.Info("TPPA_SOLVE_ATTEMPT " + new TppaSolveAttemptProvenance(
                            TppaSolveAttemptProvenance.CurrentSchemaVersion,
                            attempt,
                            retryPolicy.MaximumAttempts,
                            captureStartedUtc,
                            observationTimeUtc,
                            seq.ExposureTime,
                            usedSearchRadius,
                            requestedCoordinates?.RADegrees,
                            requestedCoordinates?.Dec,
                            solver?.GetType().FullName,
                            CaptureSucceeded: true,
                            SolveSucceeded: false,
                            SolvedRightAscensionDegrees: null,
                            SolvedDeclinationDegrees: null,
                            FailureKind: "solve-cancelled",
                            FailureMessage: ex.Message).ToJson());
                        throw new OperationCanceledException("Plate solve was cancelled.", ex, token);
                    } catch (Exception ex) {
                        solverException = ex;
                        result = new PlateSolveResult { Success = false };
                        Logger.Error(ex);
                    } finally {
                        solveStopwatch.Stop();
                    }

                    attemptStopwatch.Stop();
                    Logger.Info(
                        $"TPPA_SOLVE_TIMING schemaVersion=2; runId={diagnosticRunId?.ToString("D") ?? "none"}; attempt={attempt}; " +
                        $"captureMilliseconds={captureStopwatch.Elapsed.TotalMilliseconds:F1}; " +
                        $"solveMilliseconds={solveStopwatch.Elapsed.TotalMilliseconds:F1}; " +
                        $"totalMilliseconds={attemptStopwatch.Elapsed.TotalMilliseconds:F1}; " +
                        $"captureSucceeded=true; solveSucceeded={result.Success}.");

                    Logger.Info("TPPA_SOLVE_ATTEMPT " + new TppaSolveAttemptProvenance(
                        TppaSolveAttemptProvenance.CurrentSchemaVersion,
                        attempt,
                        retryPolicy.MaximumAttempts,
                        captureStartedUtc,
                        observationTimeUtc,
                        seq.ExposureTime,
                        usedSearchRadius,
                        requestedCoordinates?.RADegrees,
                        requestedCoordinates?.Dec,
                        solver?.GetType().FullName,
                        CaptureSucceeded: true,
                        SolveSucceeded: result.Success,
                        SolvedRightAscensionDegrees: result.Coordinates?.RADegrees,
                        SolvedDeclinationDegrees: result.Coordinates?.Dec,
                        FailureKind: solverException != null ? "solver-exception" : result.Success ? null : "solve-unsuccessful",
                        FailureMessage: solverException?.Message).ToJson());

                    if (!result.Success) {
                        usedSearchRadius = Math.Min(180, usedSearchRadius + Math.Max(0, searchRadiusIncrementOnFailure));
                        if (retryPolicy.CanStartAttempt(attempt)) {
                            await CoreUtil.Wait(
                                TimeSpan.FromSeconds(1),
                                token,
                                progress,
                                $"Plate solve failed. Starting attempt {attempt + 1}/{retryPolicy.MaximumAttempts}...");
                        }
                    }
                } else {
                    attemptStopwatch.Stop();
                    Logger.Info(
                        $"TPPA_SOLVE_TIMING schemaVersion=2; runId={diagnosticRunId?.ToString("D") ?? "none"}; attempt={attempt}; " +
                        $"captureMilliseconds={captureStopwatch.Elapsed.TotalMilliseconds:F1}; " +
                        $"solveMilliseconds=null; totalMilliseconds={attemptStopwatch.Elapsed.TotalMilliseconds:F1}; " +
                        $"captureSucceeded=false; solveSucceeded=false.");
                    Logger.Info("TPPA_SOLVE_ATTEMPT " + new TppaSolveAttemptProvenance(
                        TppaSolveAttemptProvenance.CurrentSchemaVersion,
                        attempt,
                        retryPolicy.MaximumAttempts,
                        captureStartedUtc,
                        ObservationTimeUtc: null,
                        seq.ExposureTime,
                        usedSearchRadius,
                        requestedCoordinates?.RADegrees,
                        requestedCoordinates?.Dec,
                        solver?.GetType().FullName,
                        CaptureSucceeded: false,
                        SolveSucceeded: false,
                        SolvedRightAscensionDegrees: null,
                        SolvedDeclinationDegrees: null,
                        FailureKind: captureException != null ? "capture-exception" : "capture-returned-null",
                        FailureMessage: captureException?.Message).ToJson());
                    if (retryPolicy.CanStartAttempt(attempt)) {
                        await CoreUtil.Wait(
                            TimeSpan.FromSeconds(1),
                            token,
                            progress,
                            $"Image capture failed. Starting attempt {attempt + 1}/{retryPolicy.MaximumAttempts}...");
                    }
                }
            } while (result.Success == false && retryPolicy.CanStartAttempt(attempt));
            if (!result.Success) {
                throw new SequenceEntityFailedException(
                    $"Plate solving failed after {retryPolicy.MaximumAttempts} attempts; TPPA cannot continue safely.");
            }
            return result;
        }

        private double Distance(double raDegrees1, double raDegrees2) {
            return 180 - Math.Abs(Math.Abs(raDegrees1 - raDegrees2) - 180);
        }

        private async Task MoveToNextPoint(
            double moveDistance,
            double rate,
            bool eastDirection,
            IProgress<ApplicationStatus> progress,
            CancellationToken token,
            double? settleTimeOverrideSeconds = null) {
            var totalStopwatch = Stopwatch.StartNew();
            var axisMotionStopwatch = new Stopwatch();
            var slewStopWaitStopwatch = new Stopwatch();
            var settleStopwatch = new Stopwatch();
            var traveledDegrees = 0.0;
            var adjustedRateForReport = rate;
            var settleRequestedSeconds = 0.0;
            var outcome = "failed";

            try {
                var startPosition = telescopeMediator.GetCurrentPosition();
                var currentPosition = telescopeMediator.GetCurrentPosition();
                var rates = telescopeMediator.GetInfo().PrimaryAxisRates;
                EnsureMountMotionEnvelope();

                var foundRate = rates
                    .OrderBy(x => x.Item2).LastOrDefault(x => x.Item1 <= rate && rate <= x.Item2 || x.Item2 < rate);

                var adjustedRate = rate;
                if (foundRate.Item2 < rate) {
                    Logger.Warning($"Provided MoveRate of {rate} is not supported. Using {foundRate.Item2} instead");
                    //The closest rate is below the specified move rate as no move rate is found for the value
                    adjustedRate = foundRate.Item2;
                }
                adjustedRateForReport = adjustedRate;

                Logger.Info($"Moving axis by {adjustedRate} into direction {(eastDirection ? "East" : "West")} until distance {moveDistance}° is traveled");
                axisMotionStopwatch.Start();
                telescopeMediator.MoveAxis(Core.Enum.TelescopeAxes.Primary, eastDirection ? adjustedRate : -adjustedRate);

                //Move Rate is expectedly degree/s - Add a failsafe timer at 2x
                var timeToDestination = TimeSpan.FromSeconds(moveDistance / adjustedRate * Properties.Settings.Default.MoveTimeoutFactor);

                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                    try {
                        cts.CancelAfter(timeToDestination);

                        traveledDegrees = Distance(currentPosition.RADegrees, startPosition.RADegrees);
                        while (traveledDegrees < moveDistance) {
                            await Task.Delay(100, cts.Token);
                            currentPosition = telescopeMediator.GetCurrentPosition();
                            EnsureMountMotionEnvelope();

                            traveledDegrees = Distance(currentPosition.RADegrees, startPosition.RADegrees);

                            progress?.Report(new ApplicationStatus() { Status = "Moving to next point", MaxProgress = (int)moveDistance, Progress = traveledDegrees, ProgressType = ApplicationStatus.StatusProgressType.ValueOfMaxValue });
                        }
                    } catch (OperationCanceledException) {
                        // Rethrow cancellation when parent token is cancelled
                        if (token.IsCancellationRequested) {
                            throw;
                        }
                        throw new TimeoutException($"RA-axis movement did not reach {moveDistance:F3} deg within {timeToDestination.TotalSeconds:F1} seconds.");
                    }
                }

                telescopeMediator.MoveAxis(Core.Enum.TelescopeAxes.Primary, 0);
                axisMotionStopwatch.Stop();

                slewStopWaitStopwatch.Start();
                while (telescopeMediator.GetInfo().Slewing) {
                    await CoreUtil.Wait(TimeSpan.FromMilliseconds(500), token, progress, "Waiting for mount to stop slewing");
                }
                slewStopWaitStopwatch.Stop();

                var settleTimeSeconds = TppaVerificationSettlePolicy.Resolve(
                    profileService.ActiveProfile.TelescopeSettings.SettleTime,
                    settleTimeOverrideSeconds ?? 0.0);
                settleRequestedSeconds = settleTimeSeconds;
                Logger.Info($"TPPA point settle time: {settleTimeSeconds:F3} seconds" +
                            (settleTimeOverrideSeconds.HasValue ? " (sequence override)." : " (profile setting)."));
                settleStopwatch.Start();
                await CoreUtil.Wait(TimeSpan.FromSeconds(settleTimeSeconds), token, progress, "Settling");
                settleStopwatch.Stop();
                SetTrackingSidereal(true);

                progress?.Report(new ApplicationStatus() { Status = string.Empty });
                outcome = "completed";
            } catch (OperationCanceledException) {
                outcome = "cancelled";
                throw;
            } catch (TimeoutException) {
                outcome = "timeout";
                throw;
            } finally {
                try {
                    // MoveAxis is rate-based and must be halted on every exit path.
                    telescopeMediator.MoveAxis(Core.Enum.TelescopeAxes.Primary, 0);
                } catch (Exception stopFailure) {
                    Logger.Error(
                        "Emergency RA-axis stop failed after TPPA drift-validation movement.",
                        stopFailure);
                }

                axisMotionStopwatch.Stop();
                slewStopWaitStopwatch.Stop();
                settleStopwatch.Stop();
                totalStopwatch.Stop();

                try {
                    Logger.Info("TPPA_MOVE_TIMING " + JsonConvert.SerializeObject(new {
                        schemaVersion = 1,
                        requestedDegrees = moveDistance,
                        traveledDegrees,
                        direction = eastDirection ? "East" : "West",
                        adjustedRate = adjustedRateForReport,
                        axisMotionMilliseconds = axisMotionStopwatch.Elapsed.TotalMilliseconds,
                        slewStopWaitMilliseconds = slewStopWaitStopwatch.Elapsed.TotalMilliseconds,
                        settleRequestedSeconds,
                        settleActualMilliseconds = settleStopwatch.Elapsed.TotalMilliseconds,
                        totalMilliseconds = totalStopwatch.Elapsed.TotalMilliseconds,
                        outcome
                    }));
                } catch (Exception timingFailure) {
                    Logger.Error("Failed to write TPPA movement timing evidence.", timingFailure);
                }
            }
        }

        private void EnsureVerificationOnlySlewDestinationSafe(TopocentricCoordinates destination) {
            if (destination == null) {
                throw new SequenceEntityFailedException(
                    "Verification-only slew destination is unavailable.");
            }

            var refraction = RefractionParameters.GetRefractionParameters(weatherDataMediator.GetInfo());
            var equatorialDestination = destination.Transform(
                Epoch.J2000,
                refraction.PressureHPa,
                refraction.Temperature,
                refraction.RelativeHumidity,
                refraction.Wavelength);
            EnsureVerificationOnlySlewDestinationSafe(
                equatorialDestination,
                destination.Azimuth.Degree,
                destination.Altitude.Degree);
        }

        private void EnsureVerificationOnlySlewDestinationSafe(Coordinates destination) {
            if (destination == null) {
                throw new SequenceEntityFailedException(
                    "Verification-only slew destination is unavailable.");
            }

            var nowUtc = DateTime.UtcNow;
            var refraction = RefractionParameters.GetRefractionParameters(weatherDataMediator.GetInfo());
            var horizontal = destination.Transform(
                Latitude,
                Longitude,
                Elevation,
                refraction.PressureHPa,
                refraction.Temperature,
                refraction.RelativeHumidity,
                refraction.Wavelength,
                nowUtc);
            EnsureVerificationOnlySlewDestinationSafe(
                destination,
                horizontal.Azimuth.Degree,
                horizontal.Altitude.Degree);
        }

        private void EnsureVerificationOnlySlewDestinationSafe(
                Coordinates equatorialDestination,
                double destinationAzimuthDegrees,
                double destinationAltitudeDegrees) {
            var mountInfo = telescopeMediator.GetInfo();
            var currentPierSide = mountInfo.SideOfPier;
            var destinationPierSide = telescopeMediator.DestinationSideOfPier(equatorialDestination);
            var envelope = new TppaMountMotionEnvelope(
                MountMotionMinimumAltitudeDegrees,
                MountMotionMaximumAltitudeDegrees,
                MountMotionAzimuthStartDegrees,
                MountMotionAzimuthEndDegrees);
            var result = VerificationOnlySlewSafetyPolicy.Evaluate(
                MountMotionEnvelopeEnabled,
                envelope,
                destinationAzimuthDegrees,
                destinationAltitudeDegrees,
                currentPierSide,
                destinationPierSide);
            if (!result.IsSafe) {
                throw new SequenceEntityFailedException(
                    $"Verification-only absolute slew rejected: {result.Reason}.");
            }

            Logger.Info(
                $"Verification-only absolute slew preflight passed: destination Az={destinationAzimuthDegrees:F2} deg, " +
                $"Alt={destinationAltitudeDegrees:F2} deg; {result.Reason}.");
        }

        private void EnsureVerificationOnlyActualPositionSafe(string operation) {
            var mount = telescopeMediator.GetInfo();
            var envelope = new TppaMountMotionEnvelope(
                MountMotionMinimumAltitudeDegrees,
                MountMotionMaximumAltitudeDegrees,
                MountMotionAzimuthStartDegrees,
                MountMotionAzimuthEndDegrees);
            var result = VerificationOnlySlewSafetyPolicy.EvaluateActualTelemetry(
                MountMotionEnvelopeEnabled,
                envelope,
                mount.Connected,
                mount.Slewing,
                mount.Azimuth,
                mount.Altitude);
            if (!result.IsSafe) {
                throw new SequenceEntityFailedException(
                    $"Verification-only {operation} rejected: {result.Reason}.");
            }

            Logger.Info(
                $"Verification-only {operation} post-slew check passed: {result.Reason}.");
        }

        private void EnsureMountMotionEnvelope() {
            if (!MountMotionEnvelopeEnabled) {
                return;
            }

            var mount = telescopeMediator.GetInfo();
            var envelope = new TppaMountMotionEnvelope(
                MountMotionMinimumAltitudeDegrees,
                MountMotionMaximumAltitudeDegrees,
                MountMotionAzimuthStartDegrees,
                MountMotionAzimuthEndDegrees);
            var violation = envelope.Validate(mount.Azimuth, mount.Altitude);
            if (!string.IsNullOrWhiteSpace(violation)) {
                throw new InvalidOperationException(
                    $"TPPA mount-motion envelope stopped the RA-axis move: {violation}.");
            }
        }

        public Angle Latitude {
            get => Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude);
        }
        public Angle Longitude {
            get => Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude);
        }
        public double Elevation {
            get => profileService.ActiveProfile.AstrometrySettings.Elevation;
        }

        /// <summary>
        /// This string will be used for logging
        /// </summary>
        /// <returns></returns>
        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(PolarAlignment)}, Rate: {MoveRate}, Distance: {TargetDistance}";
        }

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        public bool Validate() {
            var i = new List<string>();
            i.AddRange(PolarAlignmentExecutionPolicy.GetValidationIssues(VerificationOnly, DriftValidationOnly, ManualMode));
            if (OverdeterminedShadowModelCheck && !VerificationOnly) {
                i.Add("The 5-position shadow model check requires Verification only mode.");
            }
            if (VerificationOnly || DriftValidationOnly) {
                i.AddRange(TppaVerificationSettlePolicy.GetQualificationIssues(
                    TargetDistance, profileService.ActiveProfile.TelescopeSettings.SettleTime, VerificationPointSettleTimeSeconds));
            }

            //Location
            if (profileService.ActiveProfile.AstrometrySettings.Latitude == 0 && profileService.ActiveProfile.AstrometrySettings.Longitude == 0) {
                i.Add("No location has been set. Please set your latitude and longitude first as it is critical for the calculation to work!");
            }

            //Camera
            CameraInfo = this.cameraMediator.GetInfo();
            if (!CameraInfo.Connected) {
                i.Add(Loc.Instance["LblCameraNotConnected"]);
            } else {
                if (CameraInfo.CanSetGain && Gain > -1 && (Gain < CameraInfo.GainMin || Gain > CameraInfo.GainMax)) {
                    i.Add(string.Format(Loc.Instance["Lbl_SequenceItem_Imaging_TakeExposure_Validation_Gain"], CameraInfo.GainMin, CameraInfo.GainMax, Gain));
                }
                if (CameraInfo.CanSetOffset && Offset > -1 && (Offset < CameraInfo.OffsetMin || Offset > CameraInfo.OffsetMax)) {
                    i.Add(string.Format(Loc.Instance["Lbl_SequenceItem_Imaging_TakeExposure_Validation_Offset"], CameraInfo.OffsetMin, CameraInfo.OffsetMax, Offset));
                }
            }

            //Filter wheel
            if (filter != null && !fwMediator.GetInfo().Connected) {
                i.Add(Loc.Instance["LblFilterWheelNotConnected"]);
                i.Add("Either connect the filter wheel or clear the filter selection!");
            }

            //Mount
            var telescope = telescopeMediator.GetInfo();
            if (!ManualMode) {
                if (!telescope.Connected) {
                    i.Add(Loc.Instance["LblTelescopeNotConnected"]);
                    i.Add("Switch to manual mode if no telescope connection is available");
                } else if (!telescope.CanMovePrimaryAxis) {
                    i.Add("Telescope cannot move primary axis. This is required for the automated slews around the right ascension axis!");
                }
            }

            if (telescope.Connected && telescope.AtPark) {
                i.Add("Telescope is parked. Please unpark the telescope first!");
            }

            var executionPolicy = PolarAlignmentExecutionPolicy.Create(VerificationOnly, DriftValidationOnly);
            i.AddRange(RefractionAlignmentTarget.GetValidationIssues(
                Properties.Settings.Default.RefractionAdjustment,
                PolarAlignmentPlugin.ActiveAlignmentSystemVM?.DoAutomatedAdjustments == true,
                executionPolicy.AllowActuatorMovement,
                DriftValidationOnly));
            if (executionPolicy.AllowActuatorMovement
                    && (PolarAlignmentPlugin.ActiveAlignmentSystemVM?.DoAutomatedAdjustments == true
                        || Properties.Settings.Default.DoAutomatedAdjustments)) {
                i.AddRange(TppaVerificationSettlePolicy.GetActuatorQualificationIssues(
                    profileService.ActiveProfile.TelescopeSettings.SettleTime,
                    VerificationPointSettleTimeSeconds));
                i.AddRange(
                    TppaThreePointGeometryQualificationPolicy.GetConfigurationIssues(TargetDistance));
            }
            if (executionPolicy.AllowActuatorMovement && PolarAlignmentPlugin.ActiveAlignmentSystemVM != null && PolarAlignmentPlugin.ActiveAlignmentSystemVM?.DoAutomatedAdjustments == true && AlignmentTolerance == 0) {
                i.Add("Automated adjustments are enabled, but polar alignment tolerance is set to zero. Please set an alignment tolerance!");
            }


            Issues = i;
            return i.Count == 0;
        }

        public async Task OnMessageReceived(IMessage message) {
            if (message.Topic == ResumeAlignmentTopic) {
                try {
                    Logger.Info("Received message to resume polar alignment");
                    Resume();
                } catch (Exception ex) {
                    Logger.Error(ex);
                }
            } else if (message.Topic == PauseAlignmentTopic) {
                try {
                    Logger.Info("Received message to pause polar alignment");
                    Pause();
                } catch { }
            }
        }

        public class CustomWindowService : IWindowService {
            protected Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            protected CustomWindow window;

            public void Show(object content, string title = "", ResizeMode resizeMode = ResizeMode.NoResize, WindowStyle windowStyle = WindowStyle.None) {
                dispatcher.Invoke(DispatcherPriority.Normal, new Action(() => {
                    window = new CustomWindow() {
                        SizeToContent = SizeToContent.Manual,
                        Title = title,
                        Background = Application.Current.TryFindResource("BackgroundBrush") as Brush,
                        ResizeMode = resizeMode,
                        WindowStyle = windowStyle,
                        MinHeight = 600,
                        MinWidth = 600,
                        Style = Application.Current.TryFindResource("NoResizeWindow") as Style,
                    };
                    window.CloseCommand = new RelayCommand((object o) => window.Close());
                    window.Closed += (object sender, EventArgs e) => this.OnClosed?.Invoke(this, null);
                    window.ContentRendered += (object sender, EventArgs e) => window.InvalidateVisual();
                    window.Content = content;
                    window.Owner = Application.Current.MainWindow;
                    window.Show();
                }));
            }

            public void DelayedClose(TimeSpan t) {
                Task.Run(async () => {
                    await CoreUtil.Wait(t);
                    await this.Close();
                });
            }

            public async Task Close() {
                await dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                    window?.Close();
                }));
            }

            public IDispatcherOperationWrapper ShowDialog(object content, string title = "", ResizeMode resizeMode = ResizeMode.NoResize, WindowStyle windowStyle = WindowStyle.None, ICommand closeCommand = null) {
                return new DispatcherOperationWrapper(dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                    window = new CustomWindow() {
                        SizeToContent = SizeToContent.WidthAndHeight,
                        Title = title,
                        Background = Application.Current.TryFindResource("BackgroundBrush") as Brush,
                        ResizeMode = resizeMode,
                        WindowStyle = windowStyle,
                        Style = Application.Current.TryFindResource("NoResizeWindow") as Style,
                    };
                    if (closeCommand == null) {
                        window.CloseCommand = new RelayCommand((object o) => {
                            window.Close();
                            Application.Current.MainWindow?.Focus();
                        });
                    } else {
                        window.CloseCommand = closeCommand;
                    }
                    window.Closed += (object sender, EventArgs e) => this.OnClosed?.Invoke(this, null);
                    window.ContentRendered += (object sender, EventArgs e) => window.InvalidateVisual();

                    window.SizeChanged += Win_SizeChanged;
                    window.Content = content;
                    var mainwindow = System.Windows.Application.Current.MainWindow;
                    mainwindow.Opacity = 0.8;
                    window.Owner = Application.Current.MainWindow;
                    var result = window.ShowDialog();
                    this.OnDialogResultChanged?.Invoke(this, new DialogResultEventArgs(result));
                    mainwindow.Opacity = 1;
                })));
            }

            public event EventHandler OnDialogResultChanged;

            public event EventHandler OnClosed;

            private static void Win_SizeChanged(object sender, SizeChangedEventArgs e) {
                var mainwindow = System.Windows.Application.Current.MainWindow;
                var win = (System.Windows.Window)sender;
                win.Left = mainwindow.Left + (mainwindow.Width - win.ActualWidth) / 2;
                ;
                win.Top = mainwindow.Top + (mainwindow.Height - win.ActualHeight) / 2;
            }
        }
    }

    public class PolarAlignmentProgressMessage(Guid correlatedGuid, ApplicationStatus status) : IMessage {
        public Guid SenderId => Guid.Parse(PolarAlignmentPlugin.PluginId);

        public string Sender => nameof(PolarAlignmentPlugin);

        public DateTimeOffset SentAt => DateTime.UtcNow;

        public Guid MessageId => Guid.NewGuid();

        public DateTimeOffset? Expiration => null;

        public Guid? CorrelationId => correlatedGuid;

        public int Version => 1;

        public IDictionary<string, object> CustomHeaders => new Dictionary<string, object>();

        public string Topic => $"{nameof(PolarAlignmentPlugin)}_{nameof(PolarAlignment)}_Progress";

        public object Content { get; } = status;
    }
    
    public class PolarAlignmentVerificationMessage(Guid correlatedGuid,
                                                    double initialAltitudeError,
                                                    double initialAzimuthError,
                                                    double initialTotalError,
                                                    double reciprocalAltitudeError,
                                                    double reciprocalAzimuthError,
                                                    double reciprocalTotalError,
                                                    double verificationAltitudeError,
                                                    double verificationAzimuthError,
                                                    double verificationTotalError,
                                                    double altitudeDelta,
                                                    double azimuthDelta,
                                                    double totalDelta) : IMessage {
        public Guid SenderId => Guid.Parse(PolarAlignmentPlugin.PluginId);

        public string Sender => nameof(PolarAlignmentPlugin);

        public DateTimeOffset SentAt => DateTime.UtcNow;

        public Guid MessageId => Guid.NewGuid();

        public DateTimeOffset? Expiration => null;

        public Guid? CorrelationId => correlatedGuid;

        public int Version => 2;

        public IDictionary<string, object> CustomHeaders => new Dictionary<string, object>();

        public string Topic => $"{nameof(PolarAlignmentPlugin)}_{nameof(PolarAlignment)}_Verification";

        public object Content { get; } = new {
            InitialAzimuthError = initialAzimuthError,
            InitialAltitudeError = initialAltitudeError,
            InitialTotalError = initialTotalError,
            ReciprocalAzimuthError = reciprocalAzimuthError,
            ReciprocalAltitudeError = reciprocalAltitudeError,
            ReciprocalTotalError = reciprocalTotalError,
            VerificationAzimuthError = verificationAzimuthError,
            VerificationAltitudeError = verificationAltitudeError,
            VerificationTotalError = verificationTotalError,
            AzimuthDelta = azimuthDelta,
            AltitudeDelta = altitudeDelta,
            TotalDelta = totalDelta
        };
    }

    public class PolarAlignmentErrorMessage(Guid correlatedGuid, double altitudeError, double azimuthError, double totalError) : IMessage {

        public Guid SenderId => Guid.Parse(PolarAlignmentPlugin.PluginId);

        public string Sender => nameof(PolarAlignmentPlugin);

        public DateTimeOffset SentAt => DateTime.UtcNow;

        public Guid MessageId => Guid.NewGuid();

        public DateTimeOffset? Expiration => null;

        public Guid? CorrelationId => correlatedGuid;

        public int Version => 1;

        public IDictionary<string, object> CustomHeaders => new Dictionary<string, object>();

        public string Topic => $"{nameof(PolarAlignmentPlugin)}_{nameof(PolarAlignment)}_AlignmentError";

        public object Content { get; } = new {
            AzimuthError = azimuthError,
            AltitudeError = altitudeError,
            TotalError = totalError
        };
    }
}
