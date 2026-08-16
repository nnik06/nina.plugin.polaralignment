using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NINA.Plugins.PolarAlignment.Test {
    public class PolarAlignmentInstructionPersistenceTest {
        [Test]
        public void OperationalModesDefaultToExpectedValues() {
            var instruction = CreateInstruction();

            instruction.VerificationOnly.Should().BeFalse();
            instruction.OverdeterminedShadowModelCheck.Should().BeFalse();
            instruction.DriftValidationOnly.Should().BeFalse();
            instruction.EnforceFiveMinuteRuntimeBudget.Should().BeTrue();
            instruction.ForceSessionLocalUpasResponseCalibration.Should().BeFalse();
        }

        [Test]
        public void VerificationOnlyIsCopiedWithoutChangingAlignmentTolerance() {
            var instruction = CreateInstruction();
            instruction.VerificationOnly = true;
            instruction.OverdeterminedShadowModelCheck = true;
            instruction.AlignmentTolerance = 3.5;
            instruction.VerificationPointSettleTimeSeconds = 10.0;
            instruction.EnforceFiveMinuteRuntimeBudget = false;
            instruction.ForceSessionLocalUpasResponseCalibration = true;

            var clone = (Instructions.PolarAlignment)instruction.Clone();

            clone.VerificationOnly.Should().BeTrue();
            clone.OverdeterminedShadowModelCheck.Should().BeTrue();
            clone.AlignmentTolerance.Should().Be(3.5);
            clone.VerificationPointSettleTimeSeconds.Should().Be(10.0);
            clone.EnforceFiveMinuteRuntimeBudget.Should().BeFalse();
            clone.ForceSessionLocalUpasResponseCalibration.Should().BeTrue();
        }

        [Test]
        public void MissingFastBudgetJsonRetainsNewInstructionDefault() {
            var instruction = CreateInstruction();

            JsonConvert.PopulateObject("{}", instruction);

            instruction.EnforceFiveMinuteRuntimeBudget.Should().BeTrue();
        }

        [Test]
        public void DisabledAlignmentToleranceIsCopiedAsZero() {
            var instruction = CreateInstruction();
            instruction.AlignmentTolerance = 0.0;

            var clone = (Instructions.PolarAlignment)instruction.Clone();

            clone.AlignmentTolerance.Should().Be(0.0);
        }

        [Test]
        public void DisabledAlignmentToleranceRoundTripsAsExplicitZero() {
            var instruction = CreateInstruction();
            instruction.AlignmentTolerance = 0.0;

            var json = JsonConvert.SerializeObject(instruction);
            var serialized = JObject.Parse(json);
            var restored = CreateInstruction();
            JsonConvert.PopulateObject(json, restored);

            serialized[nameof(Instructions.PolarAlignment.AlignmentTolerance)]!
                .Value<double>().Should().Be(0.0);
            restored.AlignmentTolerance.Should().Be(0.0);
        }

        [Test]
        public void VerificationOnlyRoundTripsThroughInstructionJson() {
            var instruction = CreateInstruction();
            instruction.VerificationOnly = true;
            instruction.OverdeterminedShadowModelCheck = true;
            instruction.AlignmentTolerance = 4.5;
            instruction.VerificationPointSettleTimeSeconds = 10.0;
            instruction.EnforceFiveMinuteRuntimeBudget = false;
            instruction.ForceSessionLocalUpasResponseCalibration = true;

            var json = JsonConvert.SerializeObject(instruction);
            var serialized = JObject.Parse(json);
            var restored = CreateInstruction();
            JsonConvert.PopulateObject(json, restored);

            serialized[nameof(Instructions.PolarAlignment.VerificationOnly)]!.Value<bool>().Should().BeTrue();
            restored.VerificationOnly.Should().BeTrue();
            serialized[nameof(Instructions.PolarAlignment.OverdeterminedShadowModelCheck)]!.Value<bool>().Should().BeTrue();
            restored.OverdeterminedShadowModelCheck.Should().BeTrue();
            serialized[nameof(Instructions.PolarAlignment.VerificationPointSettleTimeSeconds)]!
                .Value<double>().Should().Be(10.0);
            restored.AlignmentTolerance.Should().Be(4.5);
            restored.VerificationPointSettleTimeSeconds.Should().Be(10.0);
            serialized[nameof(Instructions.PolarAlignment.EnforceFiveMinuteRuntimeBudget)]!
                .Value<bool>().Should().BeFalse();
            restored.EnforceFiveMinuteRuntimeBudget.Should().BeFalse();
            serialized[nameof(Instructions.PolarAlignment.ForceSessionLocalUpasResponseCalibration)]!
                .Value<bool>().Should().BeTrue();
            restored.ForceSessionLocalUpasResponseCalibration.Should().BeTrue();
        }

        [Test]
        public void DriftValidationOnlyIsCopiedWithoutChangingAlignmentTolerance() {
            var instruction = CreateInstruction();
            instruction.DriftValidationOnly = true;
            instruction.AlignmentTolerance = 3.5;

            var clone = (Instructions.PolarAlignment)instruction.Clone();

            clone.DriftValidationOnly.Should().BeTrue();
            clone.VerificationOnly.Should().BeFalse();
            clone.AlignmentTolerance.Should().Be(3.5);
        }

        [Test]
        public void DriftValidationOnlyRoundTripsThroughInstructionJson() {
            var instruction = CreateInstruction();
            instruction.DriftValidationOnly = true;
            instruction.AlignmentTolerance = 4.5;

            var json = JsonConvert.SerializeObject(instruction);
            var serialized = JObject.Parse(json);
            var restored = CreateInstruction();
            JsonConvert.PopulateObject(json, restored);

            serialized[nameof(Instructions.PolarAlignment.DriftValidationOnly)]!.Value<bool>().Should().BeTrue();
            restored.DriftValidationOnly.Should().BeTrue();
            restored.VerificationOnly.Should().BeFalse();
            restored.AlignmentTolerance.Should().Be(4.5);
        }


        private static Instructions.PolarAlignment CreateInstruction() {
            return PolarAlignmentSolveCancellationTest.CreatePolarAlignment(null!, null!);
        }
    }
}
