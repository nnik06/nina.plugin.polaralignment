using FluentAssertions;
using Newtonsoft.Json.Linq;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaOverdeterminedAxisEstimatorTest {
        [Test]
        public void ExactSmallCircleRecoversKnownAxisAndRadius() {
            var axis = Unit(new Vector3(0.87, -0.12, 0.48));
            var samples = SmallCircle(axis, 55, -20, -10, 0, 10, 20);

            var result = TppaOverdeterminedAxisEstimator.Fit(samples, axis);

            result.IsNumericallyValid.Should().BeTrue();
            TppaOverdeterminedAxisEstimator.AxisSeparationArcMinutes(
                result.Axis,
                axis).Should().BeLessThan(1e-4);
            result.SmallCircleRadiusDegrees.Should().BeApproximately(55, 1e-8);
            result.ResidualRmsArcSeconds.Should().BeLessThan(1e-5);
            result.MaximumAbsoluteResidualArcSeconds.Should().BeLessThan(1e-5);
        }

        [Test]
        public void ArbitraryAxisSignIsResolvedAgainstExpectedPole() {
            var axis = Unit(new Vector3(0.91, 0.03, 0.41));
            var samples = SmallCircle(axis, 65, -18, -9, 0, 9, 18);

            var result = TppaOverdeterminedAxisEstimator.Fit(
                samples.Reverse().ToArray(),
                axis);

            result.IsNumericallyValid.Should().BeTrue();
            Dot(result.Axis, axis).Should().BePositive();
        }

        [Test]
        public void ShortArcFailsConditioningGateWithoutLosingNumericalFit() {
            var axis = Unit(new Vector3(0.9, -0.05, 0.43));
            var samples = SmallCircle(axis, 60, -2, -1, 0, 1, 2);

            var result = TppaOverdeterminedAxisEstimator.Fit(samples, axis);

            result.IsNumericallyValid.Should().BeTrue();
            result.IsQualified.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("arc span"));
        }

        [Test]
        public void NonCircularPointTripsResidualGate() {
            var axis = Unit(new Vector3(0.88, 0.07, 0.47));
            var samples = SmallCircle(axis, 58, -20, -10, 0, 10, 20).ToArray();
            samples[2] = Unit(Add(samples[2], Scale(axis, 0.002)));

            var result = TppaOverdeterminedAxisEstimator.Fit(samples, axis);

            result.IsNumericallyValid.Should().BeTrue();
            result.IsQualified.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("residual"));
        }

        [Test]
        public void ExistingThreeSweepShapeRemainsShadowOnlyAndExposesModelLimit() {
            var axis = Unit(new Vector3(0.9, 0.02, 0.435));
            var forward = SmallCircle(axis, 62, -15, 0, 15);
            var reciprocal = forward.Reverse().ToArray();
            var repeated = forward.ToArray();

            var result = TppaOverdeterminedAxisEstimator.EvaluateShadow(
                forward,
                reciprocal,
                repeated,
                axis);

            result.Pooled.IsNumericallyValid.Should().BeTrue();
            result.DistinctPositionCount.Should().Be(3);
            result.Issues.Should().Contain(issue => issue.Contains("distinct position"));
            result.GrantsMotionAuthority.Should().BeFalse();
            result.GrantsCompletionAuthority.Should().BeFalse();
            var json = JObject.Parse(result.ToJson());
            json.Value<bool>("grantsMotionAuthority").Should().BeFalse();
            json.Value<bool>("grantsCompletionAuthority").Should().BeFalse();
        }

        [Test]
        public void ReciprocalAxisBiasFailsClosed() {
            var axis = Unit(new Vector3(0.9, 0.01, 0.435));
            var biasedAxis = Rotate(axis, Unit(new Vector3(0, 0, 1)), 1.0 / 60.0);
            var forward = SmallCircle(axis, 62, -15, 0, 15);
            var reciprocal = SmallCircle(biasedAxis, 62, 15, 0, -15);

            var result = TppaOverdeterminedAxisEstimator.EvaluateShadow(
                forward,
                reciprocal,
                forward,
                axis);

            result.ForwardToReciprocalAxisDeltaArcMinutes.Should().BeGreaterThan(0.5);
            result.Issues.Should().Contain(issue => issue.Contains("forward-to-reciprocal"));
            result.IsShadowQualified.Should().BeFalse();
        }

        [Test]
        public void FiveDistinctPositionsQualifyExactModelWithoutGrantingAuthority() {
            var axis = Unit(new Vector3(0.9, 0.02, 0.435));
            var samples = SmallCircle(axis, 62, -20, -10, 0, 10, 20);
            var legacy = new[] { samples[0], samples[2], samples[4] };

            var result = TppaOverdeterminedAxisEstimator.EvaluateDistinctSweep(
                samples,
                legacy,
                axis);

            result.DistinctPositionCount.Should().Be(5);
            result.IsShadowQualified.Should().BeTrue(string.Join("; ", result.Issues));
            result.LegacyThreePointAxisDeltaArcMinutes.Should().BeLessThan(1e-4);
            result.MaximumHeldOutAxisDeltaArcMinutes.Should().BeLessThan(1e-4);
            result.GrantsMotionAuthority.Should().BeFalse();
            result.GrantsCompletionAuthority.Should().BeFalse();
            var json = JObject.Parse(result.ToJson());
            json.Value<bool>("grantsMotionAuthority").Should().BeFalse();
            json.Value<bool>("grantsCompletionAuthority").Should().BeFalse();
        }

        [Test]
        public void FivePositionModelCheckRejectsFieldDependentDeparture() {
            var axis = Unit(new Vector3(0.9, 0.02, 0.435));
            var samples = SmallCircle(axis, 62, -20, -10, 0, 10, 20).ToArray();
            var legacy = new[] { samples[0], samples[2], samples[4] };
            samples[1] = Unit(Add(samples[1], Scale(axis, 0.002)));

            var result = TppaOverdeterminedAxisEstimator.EvaluateDistinctSweep(
                samples,
                legacy,
                axis);

            result.IsShadowQualified.Should().BeFalse();
            result.Issues.Should().Contain(issue =>
                issue.Contains("residual") || issue.Contains("held-out"));
            result.GrantsMotionAuthority.Should().BeFalse();
        }

        [Test]
        public void FiveSamplesAtOnlyThreePositionsFailDistinctPositionGate() {
            var axis = Unit(new Vector3(0.9, 0.02, 0.435));
            var legacy = SmallCircle(axis, 62, -20, 0, 20);
            var samples = new[] { legacy[0], legacy[1], legacy[2], legacy[1], legacy[0] };

            var result = TppaOverdeterminedAxisEstimator.EvaluateDistinctSweep(
                samples,
                legacy,
                axis);

            result.DistinctPositionCount.Should().Be(3);
            result.IsShadowQualified.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("distinct position"));
        }

        private static Vector3[] SmallCircle(
                Vector3 axis,
                double radiusDegrees,
                params double[] phaseDegrees) {
            var seed = Math.Abs(axis.Z) < 0.9
                ? new Vector3(0, 0, 1)
                : new Vector3(1, 0, 0);
            var firstBasis = Unit(Vector3.CrossProduct(axis, seed));
            var secondBasis = Unit(Vector3.CrossProduct(axis, firstBasis));
            var radius = radiusDegrees * Math.PI / 180.0;
            return phaseDegrees.Select(phase => {
                var radians = phase * Math.PI / 180.0;
                var radial = Add(
                    Scale(firstBasis, Math.Cos(radians)),
                    Scale(secondBasis, Math.Sin(radians)));
                return Unit(Add(
                    Scale(axis, Math.Cos(radius)),
                    Scale(radial, Math.Sin(radius))));
            }).ToArray();
        }

        private static Vector3 Rotate(Vector3 value, Vector3 axis, double degrees) =>
            Vector3.RotateByRodrigues(
                value,
                axis,
                NINA.Astrometry.Angle.ByDegree(degrees)).ToUnitVector();

        private static Vector3 Add(Vector3 first, Vector3 second) =>
            new(first.X + second.X, first.Y + second.Y, first.Z + second.Z);

        private static Vector3 Scale(Vector3 value, double scalar) =>
            new(value.X * scalar, value.Y * scalar, value.Z * scalar);

        private static Vector3 Unit(Vector3 value) => value.ToUnitVector();

        private static double Dot(Vector3 first, Vector3 second) =>
            first.X * second.X + first.Y * second.Y + first.Z * second.Z;
    }
}
