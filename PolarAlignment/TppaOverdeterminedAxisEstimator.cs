using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record VerificationOnlyArcMeasurement(
        PolarErrorDetermination Determination,
        IReadOnlyList<Position> Samples);

    internal sealed record TppaOverdeterminedAxisPolicy(
        int MinimumSamples = 4,
        double MinimumArcSpanDegrees = 15.0,
        double MaximumInPlaneConditionNumber = 100.0,
        double MaximumResidualRmsArcSeconds = 30.0,
        double MaximumResidualArcSeconds = 60.0,
        double MaximumSubsetAxisDeltaArcMinutes = 0.5,
        int MinimumDistinctPositionsForModelCheck = 5,
        double DistinctPositionToleranceDegrees = 0.25);

    internal sealed record TppaOverdeterminedAxisFit(
        bool IsNumericallyValid,
        Vector3 Axis,
        double PlaneOffset,
        double SmallCircleRadiusDegrees,
        IReadOnlyList<double> ResidualArcSeconds,
        double ResidualRmsArcSeconds,
        double MaximumAbsoluteResidualArcSeconds,
        double ArcSpanDegrees,
        double InPlaneConditionNumber,
        double SmallestEigenvalue,
        double MiddleEigenvalue,
        double LargestEigenvalue,
        IReadOnlyList<string> Issues) {
        public bool IsQualified => IsNumericallyValid && Issues.Count == 0;
    }

    internal sealed record TppaOverdeterminedAxisDiagnostics(
        TppaOverdeterminedAxisFit Pooled,
        TppaOverdeterminedAxisFit ForwardOnly,
        IReadOnlyList<TppaOverdeterminedAxisFit> LeaveOneSweepOut,
        int DistinctPositionCount,
        double InitialToRepeatedAxisDeltaArcMinutes,
        double ForwardToReciprocalAxisDeltaArcMinutes,
        double MaximumHeldOutAxisDeltaArcMinutes,
        IReadOnlyList<string> Issues) {
        public bool GrantsMotionAuthority => false;
        public bool GrantsCompletionAuthority => false;
        public bool IsShadowQualified => Issues.Count == 0;

        public string ToJson() {
            var settings = new JsonSerializerSettings {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Culture = CultureInfo.InvariantCulture,
                NullValueHandling = NullValueHandling.Include
            };
            return JsonConvert.SerializeObject(this, Formatting.None, settings);
        }
    }

    internal sealed record TppaOverdeterminedSweepDiagnostics(
        TppaOverdeterminedAxisFit Fit,
        IReadOnlyList<TppaOverdeterminedAxisFit> LeaveOneOut,
        int DistinctPositionCount,
        double LegacyThreePointAxisDeltaArcMinutes,
        double MaximumHeldOutAxisDeltaArcMinutes,
        IReadOnlyList<string> Issues) {
        public bool GrantsMotionAuthority => false;
        public bool GrantsCompletionAuthority => false;
        public bool IsShadowQualified => Issues.Count == 0;

        public string ToJson() {
            var settings = new JsonSerializerSettings {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Culture = CultureInfo.InvariantCulture,
                NullValueHandling = NullValueHandling.Include
            };
            return JsonConvert.SerializeObject(this, Formatting.None, settings);
        }
    }

    /// <summary>
    /// Fits a small-circle plane to redundant topocentric unit vectors. This is
    /// report-only metrology and cannot authorize movement or completion.
    /// </summary>
    internal static class TppaOverdeterminedAxisEstimator {
        private const double NumericalTolerance = 1e-15;
        private const double RadiansToArcSeconds = 180.0 / Math.PI * 3600.0;

        public static TppaOverdeterminedAxisFit Fit(
                IReadOnlyList<Vector3> samples,
                Vector3 expectedPole,
                TppaOverdeterminedAxisPolicy policy = null) {
            var activePolicy = policy ?? new TppaOverdeterminedAxisPolicy();
            var issues = new List<string>();
            if (samples == null || samples.Count < activePolicy.MinimumSamples) {
                issues.Add($"at least {activePolicy.MinimumSamples} unit-vector samples are required");
                return Invalid(issues);
            }
            if (!TryNormalize(expectedPole, out var pole)) {
                issues.Add("expected pole vector is missing or non-finite");
                return Invalid(issues);
            }

            var vectors = new Vector3[samples.Count];
            for (var index = 0; index < samples.Count; index++) {
                if (!TryNormalize(samples[index], out vectors[index])) {
                    issues.Add($"sample {index + 1} is missing, non-finite, or zero length");
                    return Invalid(issues);
                }
            }

            var mean = new Vector3(
                vectors.Average(vector => vector.X),
                vectors.Average(vector => vector.Y),
                vectors.Average(vector => vector.Z));
            var scatter = new double[3, 3];
            foreach (var vector in vectors) {
                var delta = new[] {
                    vector.X - mean.X,
                    vector.Y - mean.Y,
                    vector.Z - mean.Z
                };
                for (var row = 0; row < 3; row++) {
                    for (var column = row; column < 3; column++) {
                        scatter[row, column] += delta[row] * delta[column];
                    }
                }
            }
            scatter[1, 0] = scatter[0, 1];
            scatter[2, 0] = scatter[0, 2];
            scatter[2, 1] = scatter[1, 2];

            var eigen = SymmetricEigenDecomposition(scatter);
            if (eigen == null) {
                issues.Add("small-circle scatter eigensystem did not converge");
                return Invalid(issues);
            }
            var order = Enumerable.Range(0, 3)
                .OrderBy(index => eigen.Values[index])
                .ToArray();
            var axis = new Vector3(
                eigen.Vectors[0, order[0]],
                eigen.Vectors[1, order[0]],
                eigen.Vectors[2, order[0]]).ToUnitVector();
            if (Dot(axis, pole) < 0) {
                axis = Negate(axis);
            }

            var smallest = Math.Max(0, eigen.Values[order[0]]);
            var middle = Math.Max(0, eigen.Values[order[1]]);
            var largest = Math.Max(0, eigen.Values[order[2]]);
            var planeOffset = vectors.Average(vector => Dot(axis, vector));
            if (!double.IsFinite(planeOffset) || planeOffset <= 0 || planeOffset > 1) {
                issues.Add("fitted small-circle plane offset is outside (0, 1]");
                return Invalid(issues);
            }

            var radius = Math.Acos(Math.Clamp(planeOffset, -1.0, 1.0));
            var residuals = vectors
                .Select(vector =>
                    (Math.Acos(Math.Clamp(Dot(axis, vector), -1.0, 1.0)) - radius)
                    * RadiansToArcSeconds)
                .ToArray();
            var rms = Math.Sqrt(residuals.Average(value => value * value));
            var maximumResidual = residuals.Max(value => Math.Abs(value));
            var arcSpan = MaximumPairwiseSeparationDegrees(vectors);
            var condition = middle > NumericalTolerance
                ? largest / middle
                : double.PositiveInfinity;

            if (arcSpan < activePolicy.MinimumArcSpanDegrees) {
                issues.Add($"arc span {arcSpan:F3} deg is below {activePolicy.MinimumArcSpanDegrees:F3} deg");
            }
            if (!double.IsFinite(condition)
                    || condition > activePolicy.MaximumInPlaneConditionNumber) {
                issues.Add($"in-plane condition number {condition:F3} exceeds {activePolicy.MaximumInPlaneConditionNumber:F3}");
            }
            if (rms > activePolicy.MaximumResidualRmsArcSeconds) {
                issues.Add($"residual RMS {rms:F3} arcsec exceeds {activePolicy.MaximumResidualRmsArcSeconds:F3} arcsec");
            }
            if (maximumResidual > activePolicy.MaximumResidualArcSeconds) {
                issues.Add($"maximum residual {maximumResidual:F3} arcsec exceeds {activePolicy.MaximumResidualArcSeconds:F3} arcsec");
            }

            return new(
                IsNumericallyValid: true,
                Axis: axis,
                PlaneOffset: planeOffset,
                SmallCircleRadiusDegrees: radius * 180.0 / Math.PI,
                ResidualArcSeconds: residuals,
                ResidualRmsArcSeconds: rms,
                MaximumAbsoluteResidualArcSeconds: maximumResidual,
                ArcSpanDegrees: arcSpan,
                InPlaneConditionNumber: condition,
                SmallestEigenvalue: smallest,
                MiddleEigenvalue: middle,
                LargestEigenvalue: largest,
                Issues: issues);
        }

        public static TppaOverdeterminedAxisDiagnostics EvaluateShadow(
                IReadOnlyList<Vector3> initialForward,
                IReadOnlyList<Vector3> reciprocal,
                IReadOnlyList<Vector3> repeatedForward,
                Vector3 expectedPole,
                TppaOverdeterminedAxisPolicy policy = null) {
            var activePolicy = policy ?? new TppaOverdeterminedAxisPolicy();
            var issues = new List<string>();
            if (!HasThree(initialForward)
                    || !HasThree(reciprocal)
                    || !HasThree(repeatedForward)) {
                issues.Add("shadow diagnostics require three complete three-point sweeps");
                return new(
                    Invalid(issues),
                    Invalid(issues),
                    Array.Empty<TppaOverdeterminedAxisFit>(),
                    0,
                    double.PositiveInfinity,
                    double.PositiveInfinity,
                    double.PositiveInfinity,
                    issues);
            }

            var sweeps = new[] {
                initialForward.ToArray(),
                reciprocal.ToArray(),
                repeatedForward.ToArray()
            };
            var pooledSamples = sweeps.SelectMany(sweep => sweep).ToArray();
            var forwardSamples = initialForward.Concat(repeatedForward).ToArray();
            var pooled = Fit(pooledSamples, expectedPole, activePolicy);
            var forward = Fit(forwardSamples, expectedPole, activePolicy);
            var heldOut = Enumerable.Range(0, sweeps.Length)
                .Select(heldOutIndex => Fit(
                    sweeps.Where((_, index) => index != heldOutIndex)
                        .SelectMany(sweep => sweep)
                        .ToArray(),
                    expectedPole,
                    activePolicy))
                .ToArray();

            AddPrefixedIssues(issues, "pooled", pooled.Issues);
            AddPrefixedIssues(issues, "forward-only", forward.Issues);
            for (var index = 0; index < heldOut.Length; index++) {
                AddPrefixedIssues(issues, $"leave-sweep-{index + 1}-out", heldOut[index].Issues);
            }

            var initialAxis = ExactThreePointAxis(initialForward, expectedPole);
            var reciprocalAxis = ExactThreePointAxis(reciprocal, expectedPole);
            var repeatedAxis = ExactThreePointAxis(repeatedForward, expectedPole);
            var repeatDelta = AxisSeparationArcMinutes(initialAxis, repeatedAxis);
            var directionDelta = forward.IsNumericallyValid
                ? AxisSeparationArcMinutes(forward.Axis, reciprocalAxis)
                : double.PositiveInfinity;
            var heldOutDelta = pooled.IsNumericallyValid
                    && heldOut.All(result => result.IsNumericallyValid)
                ? heldOut.Max(result => AxisSeparationArcMinutes(pooled.Axis, result.Axis))
                : double.PositiveInfinity;
            var distinctPositions = CountDistinctPositions(
                pooledSamples,
                activePolicy.DistinctPositionToleranceDegrees);

            AddSubsetGateIssue(issues, "initial-to-repeated", repeatDelta, activePolicy);
            AddSubsetGateIssue(issues, "forward-to-reciprocal", directionDelta, activePolicy);
            AddSubsetGateIssue(issues, "maximum held-out", heldOutDelta, activePolicy);
            if (distinctPositions < activePolicy.MinimumDistinctPositionsForModelCheck) {
                issues.Add(
                    $"only {distinctPositions} distinct position(s) are available; " +
                    $"at least {activePolicy.MinimumDistinctPositionsForModelCheck} are required to test small-circle model fidelity");
            }

            return new(
                pooled,
                forward,
                heldOut,
                distinctPositions,
                repeatDelta,
                directionDelta,
                heldOutDelta,
                issues);
        }

        public static TppaOverdeterminedSweepDiagnostics EvaluateDistinctSweep(
                IReadOnlyList<Vector3> samples,
                IReadOnlyList<Vector3> legacyThreePointSamples,
                Vector3 expectedPole,
                TppaOverdeterminedAxisPolicy policy = null) {
            var activePolicy = policy ?? new TppaOverdeterminedAxisPolicy();
            var issues = new List<string>();
            if (samples == null
                    || samples.Count < activePolicy.MinimumDistinctPositionsForModelCheck) {
                issues.Add(
                    $"at least {activePolicy.MinimumDistinctPositionsForModelCheck} samples are required for a distinct-position model check");
                return new(
                    Invalid(issues),
                    Array.Empty<TppaOverdeterminedAxisFit>(),
                    0,
                    double.PositiveInfinity,
                    double.PositiveInfinity,
                    issues);
            }
            if (!HasThree(legacyThreePointSamples)) {
                issues.Add("exactly three legacy samples are required for the compatibility comparison");
                return new(
                    Invalid(issues),
                    Array.Empty<TppaOverdeterminedAxisFit>(),
                    0,
                    double.PositiveInfinity,
                    double.PositiveInfinity,
                    issues);
            }

            var fit = Fit(samples, expectedPole, activePolicy);
            var leaveOneOut = Enumerable.Range(0, samples.Count)
                .Select(heldOutIndex => Fit(
                    samples.Where((_, index) => index != heldOutIndex).ToArray(),
                    expectedPole,
                    activePolicy))
                .ToArray();
            AddPrefixedIssues(issues, "fit", fit.Issues);
            // Four-point leave-one-out subsets are less conditioned by design;
            // their axis deltas, not their standalone fit gates, test stability.
            var distinctPositions = CountDistinctPositions(
                samples,
                activePolicy.DistinctPositionToleranceDegrees);
            if (distinctPositions < activePolicy.MinimumDistinctPositionsForModelCheck) {
                issues.Add(
                    $"only {distinctPositions} distinct position(s) are available; " +
                    $"at least {activePolicy.MinimumDistinctPositionsForModelCheck} are required to test small-circle model fidelity");
            }

            var legacyAxis = ExactThreePointAxis(legacyThreePointSamples, expectedPole);
            var legacyDelta = fit.IsNumericallyValid
                ? AxisSeparationArcMinutes(fit.Axis, legacyAxis)
                : double.PositiveInfinity;
            var heldOutDelta = fit.IsNumericallyValid
                    && leaveOneOut.All(result => result.IsNumericallyValid)
                ? leaveOneOut.Max(result => AxisSeparationArcMinutes(fit.Axis, result.Axis))
                : double.PositiveInfinity;
            AddSubsetGateIssue(issues, "legacy-three-point-to-five-point", legacyDelta, activePolicy);
            AddSubsetGateIssue(issues, "maximum held-out", heldOutDelta, activePolicy);

            return new(
                fit,
                leaveOneOut,
                distinctPositions,
                legacyDelta,
                heldOutDelta,
                issues);
        }

        internal static double AxisSeparationArcMinutes(Vector3 first, Vector3 second) {
            if (!TryNormalize(first, out var firstUnit)
                    || !TryNormalize(second, out var secondUnit)) {
                return double.PositiveInfinity;
            }
            return Math.Acos(Math.Clamp(Dot(firstUnit, secondUnit), -1.0, 1.0))
                * 180.0 / Math.PI * 60.0;
        }

        private static Vector3 ExactThreePointAxis(
                IReadOnlyList<Vector3> samples,
                Vector3 expectedPole) {
            var axis = Vector3.DeterminePlaneVector(samples[0], samples[1], samples[2]);
            return Dot(axis, expectedPole) < 0 ? Negate(axis) : axis;
        }

        private static int CountDistinctPositions(
                IEnumerable<Vector3> samples,
                double toleranceDegrees) {
            var representatives = new List<Vector3>();
            foreach (var sample in samples) {
                if (!representatives.Any(existing =>
                        SeparationDegrees(existing, sample) <= toleranceDegrees)) {
                    representatives.Add(sample);
                }
            }
            return representatives.Count;
        }

        private static void AddSubsetGateIssue(
                ICollection<string> issues,
                string name,
                double value,
                TppaOverdeterminedAxisPolicy policy) {
            if (!double.IsFinite(value)
                    || value > policy.MaximumSubsetAxisDeltaArcMinutes) {
                issues.Add(
                    $"{name} axis delta {value:F3} arcmin exceeds " +
                    $"{policy.MaximumSubsetAxisDeltaArcMinutes:F3} arcmin");
            }
        }

        private static void AddPrefixedIssues(
                ICollection<string> destination,
                string prefix,
                IEnumerable<string> source) {
            foreach (var issue in source) {
                destination.Add($"{prefix}: {issue}");
            }
        }

        private static bool HasThree(IReadOnlyList<Vector3> samples) =>
            samples?.Count == 3;

        private static TppaOverdeterminedAxisFit Invalid(IReadOnlyList<string> issues) =>
            new(
                IsNumericallyValid: false,
                Axis: null,
                PlaneOffset: double.NaN,
                SmallCircleRadiusDegrees: double.NaN,
                ResidualArcSeconds: Array.Empty<double>(),
                ResidualRmsArcSeconds: double.PositiveInfinity,
                MaximumAbsoluteResidualArcSeconds: double.PositiveInfinity,
                ArcSpanDegrees: 0,
                InPlaneConditionNumber: double.PositiveInfinity,
                SmallestEigenvalue: double.NaN,
                MiddleEigenvalue: double.NaN,
                LargestEigenvalue: double.NaN,
                Issues: issues.ToArray());

        private static bool TryNormalize(Vector3 vector, out Vector3 unit) {
            unit = null;
            if (vector == null
                    || !double.IsFinite(vector.X)
                    || !double.IsFinite(vector.Y)
                    || !double.IsFinite(vector.Z)
                    || !double.IsFinite(vector.Length)
                    || vector.Length <= NumericalTolerance) {
                return false;
            }
            unit = vector.ToUnitVector();
            return true;
        }

        private static double MaximumPairwiseSeparationDegrees(
                IReadOnlyList<Vector3> vectors) {
            var maximum = 0.0;
            for (var first = 0; first < vectors.Count; first++) {
                for (var second = first + 1; second < vectors.Count; second++) {
                    maximum = Math.Max(
                        maximum,
                        SeparationDegrees(vectors[first], vectors[second]));
                }
            }
            return maximum;
        }

        private static double SeparationDegrees(Vector3 first, Vector3 second) =>
            Math.Acos(Math.Clamp(Dot(first.ToUnitVector(), second.ToUnitVector()), -1.0, 1.0))
            * 180.0 / Math.PI;

        private static double Dot(Vector3 first, Vector3 second) =>
            first.X * second.X + first.Y * second.Y + first.Z * second.Z;

        private static Vector3 Negate(Vector3 value) =>
            new(-value.X, -value.Y, -value.Z);

        private sealed record SymmetricEigenResult(double[] Values, double[,] Vectors);

        private static SymmetricEigenResult SymmetricEigenDecomposition(double[,] source) {
            var matrix = (double[,])source.Clone();
            var vectors = new double[3, 3] {
                { 1, 0, 0 },
                { 0, 1, 0 },
                { 0, 0, 1 }
            };

            for (var iteration = 0; iteration < 64; iteration++) {
                var first = 0;
                var second = 1;
                var maximum = Math.Abs(matrix[0, 1]);
                if (Math.Abs(matrix[0, 2]) > maximum) {
                    first = 0;
                    second = 2;
                    maximum = Math.Abs(matrix[0, 2]);
                }
                if (Math.Abs(matrix[1, 2]) > maximum) {
                    first = 1;
                    second = 2;
                    maximum = Math.Abs(matrix[1, 2]);
                }
                if (maximum <= NumericalTolerance) {
                    return new(
                        new[] { matrix[0, 0], matrix[1, 1], matrix[2, 2] },
                        vectors);
                }

                var angle = 0.5 * Math.Atan2(
                    2.0 * matrix[first, second],
                    matrix[second, second] - matrix[first, first]);
                var cosine = Math.Cos(angle);
                var sine = Math.Sin(angle);
                var oldFirst = matrix[first, first];
                var oldSecond = matrix[second, second];
                var oldCross = matrix[first, second];
                matrix[first, first] = cosine * cosine * oldFirst
                    - 2.0 * sine * cosine * oldCross
                    + sine * sine * oldSecond;
                matrix[second, second] = sine * sine * oldFirst
                    + 2.0 * sine * cosine * oldCross
                    + cosine * cosine * oldSecond;
                matrix[first, second] = 0;
                matrix[second, first] = 0;

                for (var index = 0; index < 3; index++) {
                    if (index == first || index == second) {
                        continue;
                    }
                    var oldIndexFirst = matrix[index, first];
                    var oldIndexSecond = matrix[index, second];
                    matrix[index, first] = cosine * oldIndexFirst - sine * oldIndexSecond;
                    matrix[first, index] = matrix[index, first];
                    matrix[index, second] = sine * oldIndexFirst + cosine * oldIndexSecond;
                    matrix[second, index] = matrix[index, second];
                }

                for (var row = 0; row < 3; row++) {
                    var oldVectorFirst = vectors[row, first];
                    var oldVectorSecond = vectors[row, second];
                    vectors[row, first] = cosine * oldVectorFirst - sine * oldVectorSecond;
                    vectors[row, second] = sine * oldVectorFirst + cosine * oldVectorSecond;
                }
            }
            return null;
        }
    }
}
