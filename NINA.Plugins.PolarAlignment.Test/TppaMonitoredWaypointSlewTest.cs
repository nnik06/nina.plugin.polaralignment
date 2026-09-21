using FluentAssertions;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaMonitoredWaypointSlewTest {
        [Test]
        public async Task SuccessfulSlewChecksEnvelopeBeforeDuringAndAfter() {
            var reads = 0; var stops = 0;
            await TppaMonitoredWaypointSlew.Run(async ct => {
                await Task.Delay(20, ct); return true;
            }, () => reads++, () => stops++, TimeSpan.FromSeconds(1), CancellationToken.None);
            reads.Should().BeGreaterThanOrEqualTo(3);
            stops.Should().Be(0);
        }

        [Test]
        public async Task FalseSuccessIsFailureAndStops() {
            var stops = 0;
            var act = () => TppaMonitoredWaypointSlew.Run(_ => Task.FromResult(false),
                () => { }, () => stops++, TimeSpan.FromSeconds(1), CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*returned failure*");
            stops.Should().Be(1);
        }

        [Test]
        public async Task InFlightEnvelopeViolationStopsAndCancelsDriver() {
            var reads = 0; var stops = 0; CancellationToken driverToken = default;
            var pending = new TaskCompletionSource<bool>();
            var act = () => TppaMonitoredWaypointSlew.Run(ct => { driverToken = ct; return pending.Task; },
                () => { if (++reads == 2) throw new InvalidOperationException("altitude exceeds 55"); },
                () => stops++, TimeSpan.FromSeconds(1), CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds 55*");
            driverToken.IsCancellationRequested.Should().BeTrue();
            stops.Should().Be(1);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DeadlineAndOwnerCancellationStopEvenIfDriverIgnoresCancellation(bool ownerCancels) {
            var stops = 0;
            using var owner = new CancellationTokenSource();
            var pending = new TaskCompletionSource<bool>();
            var act = () => TppaMonitoredWaypointSlew.Run(_ => {
                if (ownerCancels) owner.CancelAfter(20);
                return pending.Task;
            }, () => { }, () => stops++, TimeSpan.FromMilliseconds(100), owner.Token);
            if (ownerCancels) await act.Should().ThrowAsync<OperationCanceledException>();
            else await act.Should().ThrowAsync<TimeoutException>();
            stops.Should().Be(1);
        }

        [Test]
        public async Task UnsafeInitialTelemetryNeverStarts() {
            var starts = 0; var stops = 0;
            var act = () => TppaMonitoredWaypointSlew.Run(_ => { starts++; return Task.FromResult(true); },
                () => throw new InvalidOperationException("disconnected"), () => stops++,
                TimeSpan.FromSeconds(1), CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("disconnected");
            starts.Should().Be(0); stops.Should().Be(0);
        }

        [Test]
        public async Task FinalUnsafeTelemetryStillStops() {
            var reads = 0; var stops = 0;
            var act = () => TppaMonitoredWaypointSlew.Run(_ => Task.FromResult(true),
                () => { if (++reads == 2) throw new InvalidOperationException("disconnected"); },
                () => stops++, TimeSpan.FromSeconds(1), CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("disconnected");
            stops.Should().Be(1);
        }

        [Test]
        public async Task FailedStopRetainsBothFailures() {
            var act = () => TppaMonitoredWaypointSlew.Run(_ => Task.FromResult(false),
                () => { }, () => throw new InvalidOperationException("stop failed"),
                TimeSpan.FromSeconds(1), CancellationToken.None);
            var result = await act.Should().ThrowAsync<AggregateException>();
            result.Which.InnerExceptions.Should().HaveCount(2);
        }
    }
}
