using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaFastActuatorAdmissionGateTest {
        [Test]
        public async Task RejectedFastStartCannotReachActuatorCallback() {
            var calls = 0;

            var action = async () => await TppaFastActuatorAdmissionGate.ExecuteIfAuthorizedAsync(
                enforceFastRuntimeBudget: true,
                initialAdmissionGranted: false,
                operation: "connection",
                action: () => {
                    calls++;
                    return Task.CompletedTask;
                });

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*has not granted admission*");
            calls.Should().Be(0);
        }

        [Test]
        public async Task AdmittedFastStartCanReachActuatorCallback() {
            var calls = 0;

            await TppaFastActuatorAdmissionGate.ExecuteIfAuthorizedAsync(
                true,
                true,
                "connection",
                () => {
                    calls++;
                    return Task.CompletedTask;
                });

            calls.Should().Be(1);
        }

        [Test]
        public async Task NonFastModeDoesNotRequireFastAdmission() {
            var calls = 0;

            var result = await TppaFastActuatorAdmissionGate.ExecuteIfAuthorizedAsync(
                false,
                false,
                "movement",
                () => {
                    calls++;
                    return Task.FromResult(true);
                });

            result.Should().BeTrue();
            calls.Should().Be(1);
        }
    }
}