using System;

namespace NINA.Plugins.PolarAlignment {
    /// <summary>
    /// Diagnostic-only summary of observed UPAS altitude response. This type deliberately has
    /// no dependency on the adjustment controller or device APIs, and must not be used to plan
    /// a move without a separately reviewed opt-in.
    /// </summary>
    internal sealed class UpasAltitudeResponseDiagnostic {
        internal const int CurrentSchemaVersion = 1;
        internal const int MinimumSamples = 3;

        internal UpasAltitudeResponseDiagnostic(
            int schemaVersion,
            double medianAltitudeDeltaDegreesPerYUnit,
            double medianAbsoluteDeviationDegreesPerYUnit,
            double minimumCommandMagnitudeYUnits,
            double medianAzimuthDeltaDegreesPerYUnit,
            double medianStartingTotalErrorDegrees,
            int commandDirection,
            int sampleCount,
            DateTime firstObservedUtc,
            DateTime lastObservedUtc,
            DateTime computedUtc,
            string rigConfigurationEpoch,
            string physicalPositionEpoch,
            string reverseSettingFingerprint) {
            SchemaVersion = schemaVersion;
            MedianAltitudeDeltaDegreesPerYUnit = medianAltitudeDeltaDegreesPerYUnit;
            MedianAbsoluteDeviationDegreesPerYUnit = medianAbsoluteDeviationDegreesPerYUnit;
            MinimumCommandMagnitudeYUnits = minimumCommandMagnitudeYUnits;
            MedianAzimuthDeltaDegreesPerYUnit = medianAzimuthDeltaDegreesPerYUnit;
            MedianStartingTotalErrorDegrees = medianStartingTotalErrorDegrees;
            CommandDirection = commandDirection;
            SampleCount = sampleCount;
            FirstObservedUtc = firstObservedUtc;
            LastObservedUtc = lastObservedUtc;
            ComputedUtc = computedUtc;
            RigConfigurationEpoch = rigConfigurationEpoch;
            PhysicalPositionEpoch = physicalPositionEpoch;
            ReverseSettingFingerprint = reverseSettingFingerprint;
        }

        public int SchemaVersion { get; }
        public double MedianAltitudeDeltaDegreesPerYUnit { get; }
        public double MedianAbsoluteDeviationDegreesPerYUnit { get; }
        public double MinimumCommandMagnitudeYUnits { get; }
        public double MedianAzimuthDeltaDegreesPerYUnit { get; }
        public double MedianStartingTotalErrorDegrees { get; }
        public int CommandDirection { get; }
        public int SampleCount { get; }
        public DateTime FirstObservedUtc { get; }
        public DateTime LastObservedUtc { get; }
        public DateTime ComputedUtc { get; }
        public string RigConfigurationEpoch { get; }
        public string PhysicalPositionEpoch { get; }
        public string ReverseSettingFingerprint { get; }

        /// <summary>
        /// Returns a deterministic diagnostic validity result. It does not grant control
        /// authority and callers must not translate a valid result into an actuator command.
        /// </summary>
        public UpasAltitudeResponseDiagnosticValidity Evaluate(
            DateTime nowUtc,
            string rigConfigurationEpoch,
            string physicalPositionEpoch,
            string reverseSettingFingerprint,
            TimeSpan maximumAge,
            double maximumRelativeMad,
            double minimumRequiredCommandMagnitudeYUnits,
            double maximumCrossAxisToPrimaryRatio) {
            if (SchemaVersion != CurrentSchemaVersion) {
                return UpasAltitudeResponseDiagnosticValidity.SchemaVersionMismatch;
            }

            if (!IsUtc(FirstObservedUtc) || !IsUtc(LastObservedUtc) || !IsUtc(ComputedUtc) || !IsUtc(nowUtc)
                || FirstObservedUtc > LastObservedUtc || LastObservedUtc > ComputedUtc || ComputedUtc > nowUtc) {
                return UpasAltitudeResponseDiagnosticValidity.ClockAnomaly;
            }

            if (SampleCount < MinimumSamples) {
                return UpasAltitudeResponseDiagnosticValidity.InsufficientSamples;
            }

            if (!IsFinite(MedianAltitudeDeltaDegreesPerYUnit)
                || !IsFinite(MedianAbsoluteDeviationDegreesPerYUnit)
                || !IsFinite(MinimumCommandMagnitudeYUnits)
                || !IsFinite(MedianAzimuthDeltaDegreesPerYUnit)
                || !IsFinite(MedianStartingTotalErrorDegrees)
                || MedianAbsoluteDeviationDegreesPerYUnit < 0
                || MedianStartingTotalErrorDegrees < 0
                || CommandDirection == 0
                || Math.Sign(MedianAltitudeDeltaDegreesPerYUnit) != Math.Sign(CommandDirection)) {
                return UpasAltitudeResponseDiagnosticValidity.DegenerateValue;
            }

            if (!IsFinite(minimumRequiredCommandMagnitudeYUnits)
                || minimumRequiredCommandMagnitudeYUnits <= 0
                || MinimumCommandMagnitudeYUnits < minimumRequiredCommandMagnitudeYUnits) {
                return UpasAltitudeResponseDiagnosticValidity.InsufficientStimulus;
            }

            if (string.IsNullOrWhiteSpace(RigConfigurationEpoch)
                || string.IsNullOrWhiteSpace(PhysicalPositionEpoch)
                || string.IsNullOrWhiteSpace(ReverseSettingFingerprint)) {
                return UpasAltitudeResponseDiagnosticValidity.MissingProvenance;
            }

            if (!string.Equals(RigConfigurationEpoch, rigConfigurationEpoch, StringComparison.Ordinal)) {
                return UpasAltitudeResponseDiagnosticValidity.RigConfigurationChanged;
            }

            if (!string.Equals(PhysicalPositionEpoch, physicalPositionEpoch, StringComparison.Ordinal)) {
                return UpasAltitudeResponseDiagnosticValidity.PhysicalPositionChanged;
            }

            if (!string.Equals(ReverseSettingFingerprint, reverseSettingFingerprint, StringComparison.Ordinal)) {
                return UpasAltitudeResponseDiagnosticValidity.ReverseSettingChanged;
            }

            if (maximumAge < TimeSpan.Zero || nowUtc - LastObservedUtc > maximumAge) {
                return UpasAltitudeResponseDiagnosticValidity.Expired;
            }

            if (!IsFinite(maximumRelativeMad) || maximumRelativeMad < 0
                || MedianAbsoluteDeviationDegreesPerYUnit / Math.Abs(MedianAltitudeDeltaDegreesPerYUnit) > maximumRelativeMad) {
                return UpasAltitudeResponseDiagnosticValidity.DispersionExceeded;
            }

            if (!IsFinite(maximumCrossAxisToPrimaryRatio) || maximumCrossAxisToPrimaryRatio < 0
                || Math.Abs(MedianAzimuthDeltaDegreesPerYUnit / MedianAltitudeDeltaDegreesPerYUnit) > maximumCrossAxisToPrimaryRatio) {
                return UpasAltitudeResponseDiagnosticValidity.CrossAxisCouplingExceeded;
            }

            return UpasAltitudeResponseDiagnosticValidity.Valid;
        }

        private static bool IsUtc(DateTime value) {
            return value.Kind == DateTimeKind.Utc;
        }

        private static bool IsFinite(double value) {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    internal enum UpasAltitudeResponseDiagnosticValidity {
        Valid,
        SchemaVersionMismatch,
        ClockAnomaly,
        InsufficientSamples,
        DegenerateValue,
        MissingProvenance,
        RigConfigurationChanged,
        PhysicalPositionChanged,
        ReverseSettingChanged,
        Expired,
        DispersionExceeded,
        InsufficientStimulus,
        CrossAxisCouplingExceeded
    }
}
