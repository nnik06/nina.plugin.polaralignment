using NINA.Core.Utility;
using NINA.Profile.Interfaces;
using NINA.Plugins.PolarAlignment.Avalon;

namespace NINA.Plugins.PolarAlignment.Avalon {
    public partial class UniversalPolarAlignmentVM : UniversalPolarAlignmentBaseVM {
        public UniversalPolarAlignmentVM(IProfileService profileService) : base(profileService) {
            // The response values can be reviewed across sessions, but their
            // motion authority is valid only after the current session's
            // marker and axis sense have been checked.
            if (Properties.Settings.Default.AvalonDirectFullTravelRouteConfirmed) {
                Properties.Settings.Default.AvalonDirectFullTravelRouteConfirmed = false;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                Logger.Info("UPAS calibrated direct full-travel route attestation cleared for the new NINA session.");
            }
        }

        protected override string SystemName => "Avalon Polar Alignment System";

        protected override IPolarAlignmentSystem CreateSystem() => new UniversalPolarAlignment();

        public override bool DoAutomatedAdjustments {
            get => Properties.Settings.Default.DoAutomatedAdjustments;
            set {
                Properties.Settings.Default.DoAutomatedAdjustments = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public override double AutomatedAdjustmentSettleTime {
            get => Properties.Settings.Default.AutomatedAdjustmentSettleTime;
            set {
                Properties.Settings.Default.AutomatedAdjustmentSettleTime = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public override float XGearRatio {
            get => Properties.Settings.Default.AvalonXGearRatio;
            set {
                if (value < 1) { value = 1; }
                if (Properties.Settings.Default.AvalonXGearRatio != value) {
                    InvalidateDirectFullTravelConfirmation();
                }
                Properties.Settings.Default.AvalonXGearRatio = value;
                if (upa != null) { upa.XGearRatio = value; }
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(PositionX));
            }
        }

        public override int XSpeed {
            get => Properties.Settings.Default.AvalonXSpeed;
            set {
                Properties.Settings.Default.AvalonXSpeed = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public override float YGearRatio {
            get => Properties.Settings.Default.AvalonYGearRatio;
            set {
                if (Properties.Settings.Default.AvalonYGearRatio != value) {
                    InvalidateAltitudeTravelConfirmation();
                    InvalidateDirectFullTravelConfirmation();
                }
                if (value < 1) { value = 1; }
                Properties.Settings.Default.AvalonYGearRatio = value;
                if (upa != null) { upa.YGearRatio = value; }
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(PositionY));
            }
        }

        public override int YSpeed {
            get => Properties.Settings.Default.AvalonYSpeed;
            set {
                Properties.Settings.Default.AvalonYSpeed = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public override bool ReverseAzimuth {
            get => Properties.Settings.Default.AvalonReverseAzimuth;
            set {
                if (Properties.Settings.Default.AvalonReverseAzimuth != value) {
                    Properties.Settings.Default.AvalonRememberedAzimuthResponsePerUnit = 0;
                    InvalidateDirectFullTravelConfirmation();
                }
                Properties.Settings.Default.AvalonReverseAzimuth = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public override bool ReverseAltitude {
            get => Properties.Settings.Default.AvalonReverseAltitude;
            set {
                if (Properties.Settings.Default.AvalonReverseAltitude != value) {
                    InvalidateAltitudeTravelConfirmation();
                    InvalidateDirectFullTravelConfirmation();
                }
                Properties.Settings.Default.AvalonReverseAltitude = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public override float XBacklashCompensation {
            get => Properties.Settings.Default.AvalonXBacklashCompensation;
            set {
                Properties.Settings.Default.AvalonXBacklashCompensation = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool PreSeatAzimuthBeforeMeasurement {
            get => Properties.Settings.Default.AvalonPreSeatAzimuthBeforeMeasurement;
            set {
                Properties.Settings.Default.AvalonPreSeatAzimuthBeforeMeasurement = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double AzimuthPreSeatUnits {
            get => Properties.Settings.Default.AvalonAzimuthPreSeatUnits;
            set {
                Properties.Settings.Default.AvalonAzimuthPreSeatUnits = value < 0 ? 0 : value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool AzimuthTravelGuardEnabled {
            get => Properties.Settings.Default.AvalonAzimuthTravelGuardEnabled;
            set {
                Properties.Settings.Default.AvalonAzimuthTravelGuardEnabled = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool AzimuthTravelGuardConfirmed {
            get => Properties.Settings.Default.AvalonAzimuthTravelGuardConfirmed;
            set {
                if (value && !Properties.Settings.Default.AvalonAzimuthTravelGuardConfirmed) {
                    Logger.Info(
                        $"UPAS attended physical datum attested: axis=AZ, start={AzimuthStartingPositionDegrees:F3} deg, " +
                        $"range=[{Properties.Settings.Default.AvalonAzimuthMinimumDegrees:F3}, {Properties.Settings.Default.AvalonAzimuthMaximumDegrees:F3}] deg, " +
                        $"scale={AzimuthDegreesPerNudgeUnit:F6} deg/X-unit, source=operator-manual-entry.");
                }
                Properties.Settings.Default.AvalonAzimuthTravelGuardConfirmed = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double AzimuthStartingPositionDegrees {
            get => Properties.Settings.Default.AvalonAzimuthStartingPositionDegrees;
            set {
                if (Properties.Settings.Default.AvalonAzimuthStartingPositionDegrees != value) {
                    InvalidateAzimuthTravelConfirmation();
                    InvalidateDirectFullTravelConfirmation();
                }
                Properties.Settings.Default.AvalonAzimuthStartingPositionDegrees = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double AzimuthTravelLimitDegrees {
            get => Properties.Settings.Default.AvalonAzimuthTravelLimitDegrees;
            set {
                var normalized = value < 0 ? 0 : value;
                if (Properties.Settings.Default.AvalonAzimuthTravelLimitDegrees != normalized) {
                    InvalidateDirectFullTravelConfirmation();
                }
                Properties.Settings.Default.AvalonAzimuthTravelLimitDegrees = normalized;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double AzimuthDegreesPerNudgeUnit {
            get => Properties.Settings.Default.AvalonAzimuthDegreesPerNudgeUnit;
            set {
                var normalized = value <= 0 ? 0.025 : value;
                if (Properties.Settings.Default.AvalonAzimuthDegreesPerNudgeUnit != normalized) {
                    InvalidateDirectFullTravelConfirmation();
                }
                Properties.Settings.Default.AvalonAzimuthDegreesPerNudgeUnit = normalized;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool AltitudeTravelGuardEnabled {
            get => Properties.Settings.Default.AvalonAltitudeTravelGuardEnabled;
            set {
                Properties.Settings.Default.AvalonAltitudeTravelGuardEnabled = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool AltitudeTravelGuardConfirmed {
            get => Properties.Settings.Default.AvalonAltitudeTravelGuardConfirmed;
            set {
                if (value && !Properties.Settings.Default.AvalonAltitudeTravelGuardConfirmed) {
                    Logger.Info(
                        $"UPAS attended physical datum attested: axis=ALT, start={AltitudeStartingPositionDegrees:F3} deg, " +
                        $"range=[{AltitudeMinimumDegrees:F3}, {AltitudeMaximumDegrees:F3}] deg, " +
                        $"scale={AltitudeDegreesPerNudgeUnit:F6} deg/Y-unit, source=operator-manual-entry.");
                }
                Properties.Settings.Default.AvalonAltitudeTravelGuardConfirmed = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double AltitudeStartingPositionDegrees {
            get => Properties.Settings.Default.AvalonAltitudeStartingPositionDegrees;
            set {
                if (Properties.Settings.Default.AvalonAltitudeStartingPositionDegrees != value) {
                    InvalidateAltitudeTravelConfirmation();
                    InvalidateDirectFullTravelConfirmation();
                }
                Properties.Settings.Default.AvalonAltitudeStartingPositionDegrees = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double AltitudeMinimumDegrees {
            get => Properties.Settings.Default.AvalonAltitudeMinimumDegrees;
            set {
                if (Properties.Settings.Default.AvalonAltitudeMinimumDegrees != value) {
                    InvalidateAltitudeTravelConfirmation();
                    InvalidateDirectFullTravelConfirmation();
                }
                Properties.Settings.Default.AvalonAltitudeMinimumDegrees = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double AltitudeMaximumDegrees {
            get => Properties.Settings.Default.AvalonAltitudeMaximumDegrees;
            set {
                if (Properties.Settings.Default.AvalonAltitudeMaximumDegrees != value) {
                    InvalidateAltitudeTravelConfirmation();
                    InvalidateDirectFullTravelConfirmation();
                }
                Properties.Settings.Default.AvalonAltitudeMaximumDegrees = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double AltitudeDegreesPerNudgeUnit {
            get => Properties.Settings.Default.AvalonAltitudeDegreesPerNudgeUnit;
            set {
                var normalized = value <= 0 ? 0.022 : value;
                if (Properties.Settings.Default.AvalonAltitudeDegreesPerNudgeUnit != normalized) {
                    InvalidateAltitudeTravelConfirmation();
                    InvalidateDirectFullTravelConfirmation();
                }
                Properties.Settings.Default.AvalonAltitudeDegreesPerNudgeUnit = normalized;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool DirectFullTravelRouteEnabled {
            get => Properties.Settings.Default.AvalonDirectFullTravelRouteEnabled;
            set {
                Properties.Settings.Default.AvalonDirectFullTravelRouteEnabled = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool DirectFullTravelRouteConfirmed {
            get => Properties.Settings.Default.AvalonDirectFullTravelRouteConfirmed;
            set {
                if (value && !Properties.Settings.Default.AvalonDirectFullTravelRouteConfirmed) {
                    Logger.Info(
                        "UPAS calibrated direct full-travel route attested: " +
                        $"response=[[{CalibratedAzimuthDeltaPerXUnit:F6}, {CalibratedAzimuthDeltaPerYUnit:F6}], " +
                        $"[{CalibratedAltitudeDeltaPerXUnit:F6}, {CalibratedAltitudeDeltaPerYUnit:F6}]] deg/unit, " +
                        $"max=[{CalibratedMaximumXUnitsPerMove:F1}, {CalibratedMaximumYUnitsPerMove:F1}] units, " +
                        $"relativeUncertainty={CalibratedResponseRelativeUncertainty:F3}, " +
                        $"recovery={ClampLimitedRecoveryEnabled}.");
                }
                Properties.Settings.Default.AvalonDirectFullTravelRouteConfirmed = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool ClampLimitedRecoveryEnabled {
            get => Properties.Settings.Default.AvalonClampLimitedRecoveryEnabled;
            set {
                SetDirectRouteSetting(nameof(Properties.Settings.Default.AvalonClampLimitedRecoveryEnabled), value);
                RaisePropertyChanged();
            }
        }

        public double CalibratedResponseRelativeUncertainty {
            get => Properties.Settings.Default.AvalonCalibratedResponseRelativeUncertainty;
            set {
                SetDirectRouteSetting(nameof(Properties.Settings.Default.AvalonCalibratedResponseRelativeUncertainty), value);
                RaisePropertyChanged();
            }
        }

        public double CalibratedAzimuthDeltaPerXUnit {
            get => Properties.Settings.Default.AvalonCalibratedAzimuthDeltaPerXUnit;
            set => SetDirectRouteResponse(nameof(Properties.Settings.Default.AvalonCalibratedAzimuthDeltaPerXUnit), value, nameof(CalibratedAzimuthDeltaPerXUnit));
        }

        public double CalibratedAzimuthDeltaPerYUnit {
            get => Properties.Settings.Default.AvalonCalibratedAzimuthDeltaPerYUnit;
            set => SetDirectRouteResponse(nameof(Properties.Settings.Default.AvalonCalibratedAzimuthDeltaPerYUnit), value, nameof(CalibratedAzimuthDeltaPerYUnit));
        }

        public double CalibratedAltitudeDeltaPerXUnit {
            get => Properties.Settings.Default.AvalonCalibratedAltitudeDeltaPerXUnit;
            set => SetDirectRouteResponse(nameof(Properties.Settings.Default.AvalonCalibratedAltitudeDeltaPerXUnit), value, nameof(CalibratedAltitudeDeltaPerXUnit));
        }

        public double CalibratedAltitudeDeltaPerYUnit {
            get => Properties.Settings.Default.AvalonCalibratedAltitudeDeltaPerYUnit;
            set => SetDirectRouteResponse(nameof(Properties.Settings.Default.AvalonCalibratedAltitudeDeltaPerYUnit), value, nameof(CalibratedAltitudeDeltaPerYUnit));
        }

        public double CalibratedMaximumXUnitsPerMove {
            get => Properties.Settings.Default.AvalonCalibratedMaximumXUnitsPerMove;
            set => SetDirectRouteResponse(nameof(Properties.Settings.Default.AvalonCalibratedMaximumXUnitsPerMove), value, nameof(CalibratedMaximumXUnitsPerMove));
        }

        public double CalibratedMaximumYUnitsPerMove {
            get => Properties.Settings.Default.AvalonCalibratedMaximumYUnitsPerMove;
            set => SetDirectRouteResponse(nameof(Properties.Settings.Default.AvalonCalibratedMaximumYUnitsPerMove), value, nameof(CalibratedMaximumYUnitsPerMove));
        }

        public double DirectRouteAzimuthMinimumDegrees {
            get => Properties.Settings.Default.AvalonAzimuthMinimumDegrees;
            set => SetDirectRouteResponse(nameof(Properties.Settings.Default.AvalonAzimuthMinimumDegrees), value, nameof(DirectRouteAzimuthMinimumDegrees));
        }

        public double DirectRouteAzimuthMaximumDegrees {
            get => Properties.Settings.Default.AvalonAzimuthMaximumDegrees;
            set => SetDirectRouteResponse(nameof(Properties.Settings.Default.AvalonAzimuthMaximumDegrees), value, nameof(DirectRouteAzimuthMaximumDegrees));
        }

        protected override void InvalidatePhysicalPositionConfirmation() {
            Logger.Warning(
                $"Invalidating UPAS attended physical datum after connection/reset state change. " +
                $"Caller stack: {new System.Diagnostics.StackTrace(skipFrames: 1, fNeedFileInfo: true)}");
            InvalidateAzimuthTravelConfirmation();
            InvalidateAltitudeTravelConfirmation();
            InvalidateDirectFullTravelConfirmation();
        }

        private void InvalidateAzimuthTravelConfirmation() {
            if (!Properties.Settings.Default.AvalonAzimuthTravelGuardConfirmed) {
                return;
            }

            Logger.Warning("UPAS attended physical datum invalidated: axis=AZ. Re-attest the observed physical position before automated movement.");
            Properties.Settings.Default.AvalonAzimuthTravelGuardConfirmed = false;
            CoreUtil.SaveSettings(Properties.Settings.Default);
            RaisePropertyChanged(nameof(AzimuthTravelGuardConfirmed));
        }

        private void InvalidateAltitudeTravelConfirmation() {
            if (!Properties.Settings.Default.AvalonAltitudeTravelGuardConfirmed) {
                return;
            }

            Logger.Warning("UPAS attended physical datum invalidated: axis=ALT. Re-attest the observed physical position before automated movement.");
            Properties.Settings.Default.AvalonAltitudeTravelGuardConfirmed = false;
            CoreUtil.SaveSettings(Properties.Settings.Default);
            RaisePropertyChanged(nameof(AltitudeTravelGuardConfirmed));
        }

        private void SetDirectRouteSetting<T>(string name, T value) {
            if (!Equals(Properties.Settings.Default[name], value)) {
                InvalidateDirectFullTravelConfirmation();
            }
            Properties.Settings.Default[name] = value;
            CoreUtil.SaveSettings(Properties.Settings.Default);
        }

        private void SetDirectRouteResponse(string name, double value, string propertyName) {
            SetDirectRouteSetting(name, value);
            RaisePropertyChanged(propertyName);
        }

        private void InvalidateDirectFullTravelConfirmation() {
            if (!Properties.Settings.Default.AvalonDirectFullTravelRouteConfirmed) {
                return;
            }

            Logger.Warning("UPAS calibrated direct full-travel route invalidated by a configuration change. Re-attest the measured response before automated movement.");
            Properties.Settings.Default.AvalonDirectFullTravelRouteConfirmed = false;
            CoreUtil.SaveSettings(Properties.Settings.Default);
            RaisePropertyChanged(nameof(DirectFullTravelRouteConfirmed));
        }
    }
}
