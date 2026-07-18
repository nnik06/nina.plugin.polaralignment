using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NINA.Plugins.PolarAlignment.Test {
    public class PolarAlignmentInstructionPersistenceTest {
        [Test]
        public void VerificationOnlyDefaultsToFalse() {
            var instruction = CreateInstruction();

            instruction.VerificationOnly.Should().BeFalse();
        }

        [Test]
        public void VerificationOnlyIsCopiedWithoutChangingAlignmentTolerance() {
            var instruction = CreateInstruction();
            instruction.VerificationOnly = true;
            instruction.AlignmentTolerance = 3.5;

            var clone = (Instructions.PolarAlignment)instruction.Clone();

            clone.VerificationOnly.Should().BeTrue();
            clone.AlignmentTolerance.Should().Be(3.5);
        }

        [Test]
        public void VerificationOnlyRoundTripsThroughInstructionJson() {
            var instruction = CreateInstruction();
            instruction.VerificationOnly = true;
            instruction.AlignmentTolerance = 4.5;

            var json = JsonConvert.SerializeObject(instruction);
            var serialized = JObject.Parse(json);
            var restored = CreateInstruction();
            JsonConvert.PopulateObject(json, restored);

            serialized[nameof(Instructions.PolarAlignment.VerificationOnly)]!.Value<bool>().Should().BeTrue();
            restored.VerificationOnly.Should().BeTrue();
            restored.AlignmentTolerance.Should().Be(4.5);
        }

        private static Instructions.PolarAlignment CreateInstruction() {
            return PolarAlignmentSolveCancellationTest.CreatePolarAlignment(null!, null!);
        }
    }
}