using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaWitnessUncertaintyEvidence(
        string ModelId,
        string InputDigest,
        string EvidenceDigest,
        int CalibrationSampleCount,
        int ClosureSampleCount,
        double MeasurementStandardUncertaintyArcSeconds,
        double CalibrationResidualArcSeconds,
        double OrientationModelResidualArcSeconds,
        double ClosureResidualArcSeconds,
        double FrameSystematicBoundArcSeconds,
        double DistortionSystematicBoundArcSeconds,
        double MechanicalSystematicBoundArcSeconds);

    internal sealed record TppaWitnessUncertaintyResult(
        bool IsQualified,
        double? UpperBound95ArcSeconds,
        IReadOnlyList<string> Issues);

    /// <summary>
    /// Computes a conservative witness uncertainty bound from measured terms.
    /// Random uncertainty receives two-sigma coverage; systematic bounds add
    /// linearly. The evidence digest makes the payload tamper-evident, but does
    /// not authenticate its producer or prove the field calibration.
    /// </summary>
    internal static class TppaWitnessUncertainty {
        private const double CoverageFactor = 2.0;
        private const string EvidenceSchema =
            "tppa-witness-uncertainty-evidence-v1";

        public static TppaWitnessUncertaintyResult Evaluate(
                TppaWitnessUncertaintyEvidence evidence,
                double maximumUpperBound95ArcSeconds,
                int minimumCalibrationSamples,
                int minimumClosureSamples) {
            var issues = new List<string>();
            if (evidence == null) {
                issues.Add("independent witness uncertainty evidence is missing");
                return new(false, null, issues);
            }

            if (string.IsNullOrWhiteSpace(evidence.ModelId)) {
                issues.Add("independent witness uncertainty model identity is missing");
            }
            if (!IsSha256(evidence.InputDigest)) {
                issues.Add("independent witness uncertainty input digest is missing or invalid");
            }
            if (!IsSha256(evidence.EvidenceDigest)
                    || !string.Equals(
                        evidence.EvidenceDigest,
                        ComputeEvidenceDigest(evidence),
                        StringComparison.Ordinal)) {
                issues.Add(
                    "independent witness uncertainty evidence digest is missing or does not match its numeric payload");
            }
            if (minimumCalibrationSamples < 1 || minimumClosureSamples < 1) {
                issues.Add("independent witness uncertainty policy sample floors are invalid");
            }
            if (evidence.CalibrationSampleCount < minimumCalibrationSamples) {
                issues.Add(
                    $"independent witness calibration has only {evidence.CalibrationSampleCount} sample(s)");
            }
            if (evidence.ClosureSampleCount < minimumClosureSamples) {
                issues.Add(
                    $"independent witness closure has only {evidence.ClosureSampleCount} sample(s)");
            }

            var terms = new[] {
                (evidence.MeasurementStandardUncertaintyArcSeconds,
                    nameof(evidence.MeasurementStandardUncertaintyArcSeconds)),
                (evidence.CalibrationResidualArcSeconds,
                    nameof(evidence.CalibrationResidualArcSeconds)),
                (evidence.OrientationModelResidualArcSeconds,
                    nameof(evidence.OrientationModelResidualArcSeconds)),
                (evidence.ClosureResidualArcSeconds,
                    nameof(evidence.ClosureResidualArcSeconds)),
                (evidence.FrameSystematicBoundArcSeconds,
                    nameof(evidence.FrameSystematicBoundArcSeconds)),
                (evidence.DistortionSystematicBoundArcSeconds,
                    nameof(evidence.DistortionSystematicBoundArcSeconds)),
                (evidence.MechanicalSystematicBoundArcSeconds,
                    nameof(evidence.MechanicalSystematicBoundArcSeconds))
            };
            foreach (var (value, name) in terms) {
                if (!double.IsFinite(value) || value <= 0) {
                    issues.Add($"independent witness uncertainty term {name} must be finite and positive");
                }
            }
            if (!double.IsFinite(maximumUpperBound95ArcSeconds)
                    || maximumUpperBound95ArcSeconds <= 0) {
                issues.Add("independent witness uncertainty policy bound is invalid");
            }

            double? upperBound = null;
            if (!issues.Any(issue => issue.Contains("term "))
                    && double.IsFinite(maximumUpperBound95ArcSeconds)
                    && maximumUpperBound95ArcSeconds > 0) {
                upperBound = CoverageFactor
                    * evidence.MeasurementStandardUncertaintyArcSeconds
                    + evidence.CalibrationResidualArcSeconds
                    + evidence.OrientationModelResidualArcSeconds
                    + evidence.ClosureResidualArcSeconds
                    + evidence.FrameSystematicBoundArcSeconds
                    + evidence.DistortionSystematicBoundArcSeconds
                    + evidence.MechanicalSystematicBoundArcSeconds;
                if (!double.IsFinite(upperBound.Value) || upperBound.Value <= 0) {
                    issues.Add("independent witness uncertainty bound is non-finite or zero");
                    upperBound = null;
                } else if (upperBound.Value > maximumUpperBound95ArcSeconds) {
                    issues.Add(
                        FormattableString.Invariant(
                            $"independent witness 95% uncertainty {upperBound.Value:F3}\" exceeds {maximumUpperBound95ArcSeconds:F3}\""));
                }
            }

            return new(!issues.Any(), upperBound, issues);
        }

        public static string ComputeEvidenceDigest(
                TppaWitnessUncertaintyEvidence evidence) {
            if (evidence == null) {
                return null;
            }

            var canonical = new StringBuilder();
            Append(canonical, EvidenceSchema);
            Append(canonical, evidence.ModelId);
            Append(canonical, evidence.InputDigest);
            Append(canonical, evidence.CalibrationSampleCount.ToString(
                CultureInfo.InvariantCulture));
            Append(canonical, evidence.ClosureSampleCount.ToString(
                CultureInfo.InvariantCulture));
            Append(canonical, evidence.MeasurementStandardUncertaintyArcSeconds
                .ToString("R", CultureInfo.InvariantCulture));
            Append(canonical, evidence.CalibrationResidualArcSeconds
                .ToString("R", CultureInfo.InvariantCulture));
            Append(canonical, evidence.OrientationModelResidualArcSeconds
                .ToString("R", CultureInfo.InvariantCulture));
            Append(canonical, evidence.ClosureResidualArcSeconds
                .ToString("R", CultureInfo.InvariantCulture));
            Append(canonical, evidence.FrameSystematicBoundArcSeconds
                .ToString("R", CultureInfo.InvariantCulture));
            Append(canonical, evidence.DistortionSystematicBoundArcSeconds
                .ToString("R", CultureInfo.InvariantCulture));
            Append(canonical, evidence.MechanicalSystematicBoundArcSeconds
                .ToString("R", CultureInfo.InvariantCulture));

            return Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
                .ToLowerInvariant();
        }

        private static void Append(StringBuilder destination, string value) {
            var safeValue = value ?? string.Empty;
            destination.Append(safeValue.Length.ToString(CultureInfo.InvariantCulture));
            destination.Append(':');
            destination.Append(safeValue);
        }

        private static bool IsSha256(string value) =>
            value?.Length == 64
            && value.All(character =>
                (character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f'));
    }
}