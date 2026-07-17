using FluentAssertions;
using Newtonsoft.Json;
using System.Reflection;

namespace NINA.Plugins.PolarAlignment.Test {
    public class AlignmentCompletionGuardTest {
        [Test]
        public void Observe_RequiresTwoConsecutiveMeasurementsWithinSelectedTolerance() {
            var guard = new AlignmentCompletionGuard();

            guard.Observe(totalErrorMinutes: 0.45, toleranceMinutes: 0.5).Should().BeFalse();
            guard.AwaitingConfirmation.Should().BeTrue();
            guard.ConsecutiveMeasurementsWithinTolerance.Should().Be(1);

            guard.Observe(totalErrorMinutes: 0.48, toleranceMinutes: 0.5).Should().BeTrue();
            guard.AwaitingConfirmation.Should().BeFalse();
            guard.ConsecutiveMeasurementsWithinTolerance.Should().Be(2);
        }

        [Test]
        public void Observe_ResetsConfirmationWhenNextMeasurementExceedsTolerance() {
            var guard = new AlignmentCompletionGuard();

            guard.Observe(totalErrorMinutes: 0.45, toleranceMinutes: 0.5).Should().BeFalse();
            guard.Observe(totalErrorMinutes: 0.75, toleranceMinutes: 0.5).Should().BeFalse();

            guard.AwaitingConfirmation.Should().BeFalse();
            guard.ConsecutiveMeasurementsWithinTolerance.Should().Be(0);
            guard.Observe(totalErrorMinutes: 0.4, toleranceMinutes: 0.5).Should().BeFalse();
            guard.AwaitingConfirmation.Should().BeTrue();
        }

        [Test]
        public void Observe_DoesNotAutoCompleteWhenToleranceIsDisabled() {
            var guard = new AlignmentCompletionGuard();

            guard.Observe(totalErrorMinutes: 0, toleranceMinutes: 0).Should().BeFalse();
            guard.AwaitingConfirmation.Should().BeFalse();
        }

        [TestCase(double.NaN, 1.0)]
        [TestCase(double.PositiveInfinity, 1.0)]
        [TestCase(-0.1, 1.0)]
        [TestCase(0.1, double.NaN)]
        [TestCase(0.1, double.PositiveInfinity)]
        public void Observe_FailsClosedForInvalidMeasurementsOrTolerance(double totalErrorMinutes, double toleranceMinutes) {
            var guard = new AlignmentCompletionGuard();

            guard.Observe(0.1, 1.0).Should().BeFalse();
            guard.AwaitingConfirmation.Should().BeTrue();

            guard.Observe(totalErrorMinutes, toleranceMinutes).Should().BeFalse();
            guard.AwaitingConfirmation.Should().BeFalse();
            guard.ConsecutiveMeasurementsWithinTolerance.Should().Be(0);
        }

        [Test]
        public void Observe_DoesNotAcceptComponentWiseSquareWhenTotalVectorExceedsTolerance() {
            var guard = new AlignmentCompletionGuard();

            guard.Observe(totalErrorMinutes: 1.03, toleranceMinutes: 1.0).Should().BeFalse();
            guard.AwaitingConfirmation.Should().BeFalse();
            guard.ConsecutiveMeasurementsWithinTolerance.Should().Be(0);

            guard.Observe(totalErrorMinutes: 0.99, toleranceMinutes: 1.0).Should().BeFalse();
            guard.AwaitingConfirmation.Should().BeTrue();
            guard.Observe(totalErrorMinutes: 0.98, toleranceMinutes: 1.0).Should().BeTrue();
        }
        [Test]
        public void TPAPAVM_RuntimeImageIsExcludedFromJsonSerialization() {
            var imageProperty = typeof(TPAPAVM).GetProperty(nameof(TPAPAVM.Image));

            imageProperty.Should().NotBeNull();
            imageProperty!.GetCustomAttribute<JsonIgnoreAttribute>().Should().NotBeNull();
        }
    }
}