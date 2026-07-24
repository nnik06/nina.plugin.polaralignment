using FluentAssertions;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugins.PolarAlignment.Test {
    [NonParallelizable]
    public class PolarAlignmentActuatorConnectionGateTest {
        [Test]
        public async Task SuppressionWaitsForAnInFlightOperationToDrain() {
            PolarAlignmentActuatorConnectionGate.TryBeginOperation(out var operation).Should().BeTrue();
            IDisposable suppression = null;
            try {
                var suppressionTask = PolarAlignmentActuatorConnectionGate.SuppressAsync(
                    TimeSpan.FromSeconds(1),
                    CancellationToken.None);
                await Task.Delay(20);
                suppressionTask.IsCompleted.Should().BeFalse();
                PolarAlignmentActuatorConnectionGate.IsSuppressed.Should().BeTrue();

                operation.Dispose();
                operation = null;
                suppression = await suppressionTask;

                PolarAlignmentActuatorConnectionGate.ActiveOperationCount.Should().Be(0);
                PolarAlignmentActuatorConnectionGate.IsSuppressed.Should().BeTrue();
                PolarAlignmentActuatorConnectionGate.TryBeginOperation(out _).Should().BeFalse();
            } finally {
                operation?.Dispose();
                suppression?.Dispose();
            }
            PolarAlignmentActuatorConnectionGate.IsSuppressed.Should().BeFalse();
        }

        [Test]
        public async Task SuppressionTimeoutFailsClosedAndReleasesItsScope() {
            PolarAlignmentActuatorConnectionGate.TryBeginOperation(out var operation).Should().BeTrue();
            try {
                Func<Task> act = async () => await PolarAlignmentActuatorConnectionGate.SuppressAsync(
                    TimeSpan.FromMilliseconds(20),
                    CancellationToken.None);
                await act.Should().ThrowAsync<TimeoutException>();
                PolarAlignmentActuatorConnectionGate.IsSuppressed.Should().BeFalse();
            } finally {
                operation.Dispose();
            }
            PolarAlignmentActuatorConnectionGate.ActiveOperationCount.Should().Be(0);
        }

        [Test]
        public async Task NestedSuppressionScopesRemainActiveUntilLastDispose() {
            IDisposable outer = null;
            IDisposable inner = null;
            try {
                outer = await PolarAlignmentActuatorConnectionGate.SuppressAsync(
                    TimeSpan.FromSeconds(1),
                    CancellationToken.None);
                inner = await PolarAlignmentActuatorConnectionGate.SuppressAsync(
                    TimeSpan.FromSeconds(1),
                    CancellationToken.None);

                inner.Dispose();
                inner = null;
                PolarAlignmentActuatorConnectionGate.IsSuppressed.Should().BeTrue();

                outer.Dispose();
                outer = null;
                PolarAlignmentActuatorConnectionGate.IsSuppressed.Should().BeFalse();
            } finally {
                inner?.Dispose();
                outer?.Dispose();
            }
        }

        [Test]
        public async Task RepeatedDisposeDoesNotReleaseAnotherScope() {
            IDisposable outer = null;
            IDisposable inner = null;
            try {
                outer = await PolarAlignmentActuatorConnectionGate.SuppressAsync(
                    TimeSpan.FromSeconds(1),
                    CancellationToken.None);
                inner = await PolarAlignmentActuatorConnectionGate.SuppressAsync(
                    TimeSpan.FromSeconds(1),
                    CancellationToken.None);

                inner.Dispose();
                inner.Dispose();
                inner = null;
                PolarAlignmentActuatorConnectionGate.IsSuppressed.Should().BeTrue();
            } finally {
                inner?.Dispose();
                outer?.Dispose();
            }
            PolarAlignmentActuatorConnectionGate.IsSuppressed.Should().BeFalse();
        }
    }
}