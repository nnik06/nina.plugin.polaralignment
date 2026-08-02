using System;
using System.Collections.Generic;
using System.Globalization;

namespace NINA.Plugins.PolarAlignment {
    internal readonly record struct TppaThreePointGeometryQualification(
        bool IsQualified,
        IReadOnlyList<string> Issues) {
        public string Reason => IsQualified
            ? "three-point numerical-observability gates passed"
            : string.Join(" ", Issues);
    }

    /// <summary>
    /// Fail-closed numerical-observability policy for a three-point TPPA solve.
    /// Passing this policy does not qualify absolute polar-alignment accuracy.
    /// </summary>
    internal static class TppaThreePointGeometryQualificationPolicy {
        public const double MinimumConfiguredLegDegrees = 15.0;
        public const double MinimumSolvedPairwiseSeparationDegrees =
            TppaAbsoluteEvidenceBinder.MinimumQualifiedArcSpanDegrees;
        public const double MinimumSolvedEndToEndSeparationDegrees = 20.0;
        public const double MinimumSolvedLegBalanceRatio = 0.40;
        public const double MinimumNormalizedTriangleQuality = 0.10;

        public static IReadOnlyList<string> GetConfigurationIssues(double configuredLegDegrees) {
            var issues = new List<string>();
            if (!double.IsFinite(configuredLegDegrees)
                    || configuredLegDegrees < MinimumConfiguredLegDegrees) {
                issues.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Automated adjustment requires at least {0:F0} degrees of configured RA travel per three-point leg; configured {1:F2} degrees.",
                    MinimumConfiguredLegDegrees,
                    configuredLegDegrees));
            }
            return issues;
        }

        public static bool AllowsActuatorPreparation(bool? solvedGeometryQualified) =>
            solvedGeometryQualified == true;

        public static TppaThreePointGeometryQualification Evaluate(
                TppaThreePointGeometry geometry,
                double configuredLegDegrees) {
            var issues = new List<string>();
            issues.AddRange(GetConfigurationIssues(configuredLegDegrees));

            if (!geometry.IsFinite || geometry.IsDegenerate) {
                issues.Add("The solved three-point geometry is non-finite or degenerate.");
            } else {
                if (geometry.MinimumPairwiseSeparationDegrees
                        < MinimumSolvedPairwiseSeparationDegrees) {
                    issues.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "Solved minimum pairwise separation {0:F2} degrees is below the {1:F2}-degree absolute-evidence and motion floor.",
                        geometry.MinimumPairwiseSeparationDegrees,
                        MinimumSolvedPairwiseSeparationDegrees));
                }
                var legBalanceRatio = geometry.MaximumPairwiseSeparationDegrees > 0
                    ? geometry.MinimumPairwiseSeparationDegrees
                        / geometry.MaximumPairwiseSeparationDegrees
                    : double.NaN;
                if (!double.IsFinite(legBalanceRatio)
                        || legBalanceRatio < MinimumSolvedLegBalanceRatio) {
                    issues.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "Solved-leg balance ratio {0:F3} is below the {1:F3} observability floor.",
                        legBalanceRatio,
                        MinimumSolvedLegBalanceRatio));
                }
                if (geometry.MaximumPairwiseSeparationDegrees
                        < MinimumSolvedEndToEndSeparationDegrees) {
                    issues.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "Solved end-to-end separation {0:F2} degrees is below the {1:F2}-degree observability floor.",
                        geometry.MaximumPairwiseSeparationDegrees,
                        MinimumSolvedEndToEndSeparationDegrees));
                }
                if (geometry.NormalizedTriangleQuality
                        < MinimumNormalizedTriangleQuality) {
                    issues.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "Normalized solved-triangle quality {0:F3} is below the {1:F3} observability floor.",
                        geometry.NormalizedTriangleQuality,
                        MinimumNormalizedTriangleQuality));
                }
            }

            return new TppaThreePointGeometryQualification(issues.Count == 0, issues);
        }
    }
}
