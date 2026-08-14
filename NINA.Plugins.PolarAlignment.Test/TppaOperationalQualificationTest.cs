using FluentAssertions;
using NINA.Plugins.PolarAlignment.Instructions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaOperationalQualificationTest {
        private static TppaOperationalQualificationInput PassingInput() =>
            new(
                DurationSeconds: 240,
                FreshDeterminationCount: 2,
                FreshSolvesUncached: true,
                MaximumDeterminationErrorArcMinutes: 2.8,
                MaximumPairwiseDeltaArcMinutes: 0.4,
                NoPhysicalAdjustmentBetweenDeterminations: true,
                GeometryQualified: true,
                ClosureQualified: true,
                SafetyGatesPassed: true,
                RefractionAdjustmentEnabled: true,
                PoleTarget: RefractionAlignmentTarget.TruePoleTarget);

        [Test]
        public void TwoFreshDeterminationsAtThreeArcMinutesQualifyOperationally() {
            var result = TppaOperationalQualification.Evaluate(PassingInput());

            result.IsOperationallyQualified.Should().BeTrue();
            result.Issues.Should().BeEmpty();
        }

        [Test]
        public void OneDeterminationCannotQualify() {
            var result = TppaOperationalQualification.Evaluate(
                PassingInput() with { FreshDeterminationCount = 1 });

            result.IsOperationallyQualified.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("only 1"));
        }

        [Test]
        public void AnyDeterminationAboveThreeArcMinutesFails() {
            var result = TppaOperationalQualification.Evaluate(
                PassingInput() with { MaximumDeterminationErrorArcMinutes = 3.01 });

            result.IsOperationallyQualified.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("exceeding 3.000"));
        }

        [Test]
        public void StaleOrMovedOrUnsafeRunsFailClosed() {
            var result = TppaOperationalQualification.Evaluate(
                PassingInput() with {
                    FreshSolvesUncached = false,
                    NoPhysicalAdjustmentBetweenDeterminations = false,
                    SafetyGatesPassed = false
                });

            result.IsOperationallyQualified.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("cached"));
            result.Issues.Should().Contain(issue => issue.Contains("physical state"));
            result.Issues.Should().Contain(issue => issue.Contains("safety"));
        }

        [Test]
        public void RefractionAndTruePoleAreRequired() {
            var result = TppaOperationalQualification.Evaluate(
                PassingInput() with {
                    RefractionAdjustmentEnabled = false,
                    PoleTarget = RefractionAlignmentTarget.ApparentPoleTarget
                });

            result.IsOperationallyQualified.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("true celestial pole"));
        }

        [Test]
        public void GeometryClosureAndRepeatabilityFailClosedWhileRuntimeIsTelemetry() {
            var result = TppaOperationalQualification.Evaluate(
                PassingInput() with {
                    MaximumPairwiseDeltaArcMinutes = 0.51,
                    GeometryQualified = false,
                    ClosureQualified = false,
                    DurationSeconds = 300.01
                });

            result.IsOperationallyQualified.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("repeatability"));
            result.Issues.Should().Contain(issue => issue.Contains("geometry"));
            result.Issues.Should().Contain(issue => issue.Contains("closure"));
            result.Issues.Should().NotContain(issue => issue.Contains("300.0"));
        }

        [Test]
        public void RuntimeAloneNeverRejectsAQualifiedOperationalResult() {
            var result = TppaOperationalQualification.Evaluate(
                PassingInput() with { DurationSeconds = 3600 });

            result.IsOperationallyQualified.Should().BeTrue();
            result.Issues.Should().BeEmpty();
        }
    }
}
