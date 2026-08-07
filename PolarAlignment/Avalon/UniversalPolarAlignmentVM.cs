using NINA.Core.Utility;
using NINA.Profile.Interfaces;
using NINA.Plugins.PolarAlignment.Avalon;

namespace NINA.Plugins.PolarAlignment.Avalon {
    public partial class UniversalPolarAlignmentVM : UniversalPolarAlignmentBaseVM {
        public UniversalPolarAlignmentVM(IProfileService profileService) : base(profileService) { }

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
                Properties.Settings.Default.AvalonAzimuthTravelGuardConfirmed = value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double AzimuthTravelLimitDegrees {
            get => Properties.Settings.Default.AvalonAzimuthTravelLimitDegrees;
            set {
                Properties.Settings.Default.AvalonAzimuthTravelLimitDegrees = value < 0 ? 0 : value;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double AzimuthDegreesPerNudgeUnit {
            get => Properties.Settings.Default.AvalonAzimuthDegreesPerNudgeUnit;
            set {
                Properties.Settings.Default.AvalonAzimuthDegreesPerNudgeUnit = value <= 0 ? 0.025 : value;
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
                }
                Properties.Settings.Default.AvalonAltitudeDegreesPerNudgeUnit = normalized;
                CoreUtil.SaveSettings(Properties.Settings.Default);
                RaisePropertyChanged();
            }
        }

        protected override void InvalidatePhysicalPositionConfirmation() {
            Logger.Warning(
                $"Clearing UPAS visual-marker travel confirmations. " +
                $"Caller stack: {new System.Diagnostics.StackTrace(skipFrames: 1, fNeedFileInfo: true)}");
            AzimuthTravelGuardConfirmed = false;
            InvalidateAltitudeTravelConfirmation();
        }

        private void InvalidateAltitudeTravelConfirmation() {
            if (!Properties.Settings.Default.AvalonAltitudeTravelGuardConfirmed) {
                return;
            }

            Properties.Settings.Default.AvalonAltitudeTravelGuardConfirmed = false;
            CoreUtil.SaveSettings(Properties.Settings.Default);
            RaisePropertyChanged(nameof(AltitudeTravelGuardConfirmed));
        }
    }
}
