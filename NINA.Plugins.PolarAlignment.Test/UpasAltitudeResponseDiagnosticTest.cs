using System;
using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class UpasAltitudeResponseDiagnosticTest {
        private static readonly DateTime Start = new DateTime(2026, 8, 8, 0, 0, 0, DateTimeKind.Utc);

        [Test]
        public void Evaluate_ValidFreshMatchingDiagnosticIsDiagnosticOnlyValid() {
            var diagnostic = CreateValid();

            diagnostic.Evaluate(Start.AddMinutes(10), "rig-a", "physical-a", "reverse-a", TimeSpan.FromHours(1), 0.25, 2.0, 0.35)
                .Should().Be(UpasAltitudeResponseDiagnosticValidity.Valid);
        }

        [TestCase("rig-b", "physical-a", "reverse-a", (int)UpasAltitudeResponseDiagnosticValidity.RigConfigurationChanged)]
        [TestCase("rig-a", "physical-b", "reverse-a", (int)UpasAltitudeResponseDiagnosticValidity.PhysicalPositionChanged)]
        [TestCase("rig-a", "physical-a", "reverse-b", (int)UpasAltitudeResponseDiagnosticValidity.ReverseSettingChanged)]
        public void Evaluate_MismatchedEpochOrReverseSettingInvalidates(
            string rigEpoch,
            string physicalEpoch,
            string reverseFingerprint,
            int expected) {
            CreateValid().Evaluate(Start.AddMinutes(10), rigEpoch, physicalEpoch, reverseFingerprint, TimeSpan.FromHours(1), 0.25, 2.0, 0.35)
                .Should().Be((UpasAltitudeResponseDiagnosticValidity)expected);
        }

        [Test]
        public void Evaluate_RejectsInsufficientSamplesAndExcessiveDispersion() {
            var insufficient = CreateValid(sampleCount: 2);
            var diffuse = CreateValid(mad: 0.01 / 60.0);

            insufficient.Evaluate(Start.AddMinutes(10), "rig-a", "physical-a", "reverse-a", TimeSpan.FromHours(1), 0.25, 2.0, 0.35)
                .Should().Be(UpasAltitudeResponseDiagnosticValidity.InsufficientSamples);
            diffuse.Evaluate(Start.AddMinutes(10), "rig-a", "physical-a", "reverse-a", TimeSpan.FromHours(1), 0.25, 2.0, 0.35)
                .Should().Be(UpasAltitudeResponseDiagnosticValidity.DispersionExceeded);
        }

        [Test]
        public void Evaluate_RejectsClockAnomalyExpiredAndInvalidDirection() {
            var futureComputed = CreateValid(computed: Start.AddMinutes(11));
            var expired = CreateValid(last: Start.AddMinutes(1));
            var wrongDirection = CreateValid(direction: -1);

            futureComputed.Evaluate(Start.AddMinutes(10), "rig-a", "physical-a", "reverse-a", TimeSpan.FromHours(1), 0.25, 2.0, 0.35)
                .Should().Be(UpasAltitudeResponseDiagnosticValidity.ClockAnomaly);
            expired.Evaluate(Start.AddMinutes(10), "rig-a", "physical-a", "reverse-a", TimeSpan.FromMinutes(5), 0.25, 2.0, 0.35)
                .Should().Be(UpasAltitudeResponseDiagnosticValidity.Expired);
            wrongDirection.Evaluate(Start.AddMinutes(10), "rig-a", "physical-a", "reverse-a", TimeSpan.FromHours(1), 0.25, 2.0, 0.35)
                .Should().Be(UpasAltitudeResponseDiagnosticValidity.DegenerateValue);
        }

        [Test]
        public void Evaluate_RejectsInsufficientStimulusAndExcessiveCrossAxisCoupling() {
            var insufficientStimulus = CreateValid(minimumCommandMagnitude: 1.5);
            var coupled = CreateValid(medianAzimuthDelta: 0.01 / 60.0);

            insufficientStimulus.Evaluate(Start.AddMinutes(10), "rig-a", "physical-a", "reverse-a", TimeSpan.FromHours(1), 0.25, 2.0, 0.35)
                .Should().Be(UpasAltitudeResponseDiagnosticValidity.InsufficientStimulus);
            coupled.Evaluate(Start.AddMinutes(10), "rig-a", "physical-a", "reverse-a", TimeSpan.FromHours(1), 0.25, 2.0, 0.35)
                .Should().Be(UpasAltitudeResponseDiagnosticValidity.CrossAxisCouplingExceeded);
        }

        private static UpasAltitudeResponseDiagnostic CreateValid(
            int sampleCount = 3,
            double mad = 0.001 / 60.0,
            int direction = 1,
            double minimumCommandMagnitude = 2.0,
            double medianAzimuthDelta = 0.001 / 60.0,
            DateTime? last = null,
            DateTime? computed = null) {
            var observedLast = last ?? Start.AddMinutes(5);
            return new UpasAltitudeResponseDiagnostic(
                UpasAltitudeResponseDiagnostic.CurrentSchemaVersion,
                0.01 / 60.0,
                mad,
                minimumCommandMagnitude,
                medianAzimuthDelta,
                12.0 / 60.0,
                direction,
                sampleCount,
                Start,
                observedLast,
                computed ?? observedLast,
                "rig-a",
                "physical-a",
                "reverse-a");
        }
    }
}
