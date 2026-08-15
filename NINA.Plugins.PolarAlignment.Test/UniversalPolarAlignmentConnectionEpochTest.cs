using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class UniversalPolarAlignmentConnectionEpochTest {
        [Test]
        public void PreservedViewModelHandoffStartsANewSafeConnectionEpoch() {
            UniversalPolarAlignmentBaseVM.NextConnectionHistoryState(
                hasEstablishedPhysicalConnection: true,
                invalidatePhysicalPositionConfirmation: false)
                .Should().BeFalse();
        }

        [Test]
        public void ExplicitDisconnectRetainsReconnectInvalidationHistory() {
            UniversalPolarAlignmentBaseVM.NextConnectionHistoryState(
                hasEstablishedPhysicalConnection: true,
                invalidatePhysicalPositionConfirmation: true)
                .Should().BeTrue();
        }

        [Test]
        public void NeverConnectedStateRemainsAFirstConnection() {
            UniversalPolarAlignmentBaseVM.NextConnectionHistoryState(
                hasEstablishedPhysicalConnection: false,
                invalidatePhysicalPositionConfirmation: true)
                .Should().BeFalse();
        }

        [Test]
        public void PreservedDisconnectBeforeAnyConnectionRemainsAFirstConnection() {
            UniversalPolarAlignmentBaseVM.NextConnectionHistoryState(
                hasEstablishedPhysicalConnection: false,
                invalidatePhysicalPositionConfirmation: false)
                .Should().BeFalse();
        }
    }
}
