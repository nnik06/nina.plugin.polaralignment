using FluentAssertions;
using NINA.Equipment.Interfaces;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaDriftTrackingStatePolicyTest {
        [TestCase(true)]
        [TestCase(false)]
        public void CapturesSiderealTrackingEnabledState(bool enabled) {
            var snapshot = TppaDriftTrackingStatePolicy.Capture(
                enabled,
                TrackingMode.Sidereal);

            snapshot.TrackingEnabled.Should().Be(enabled);
        }

        [TestCase(TrackingMode.Lunar)]
        [TestCase(TrackingMode.Solar)]
        [TestCase(TrackingMode.King)]
        [TestCase(TrackingMode.Custom)]
        public void RejectsActiveNonSiderealTracking(TrackingMode mode) {
            var act = () => TppaDriftTrackingStatePolicy.Capture(true, mode);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*requires sidereal tracking*");
        }

        [Test]
        public void AllowsInactiveTrackingRegardlessOfStoredMode() {
            var snapshot = TppaDriftTrackingStatePolicy.Capture(false, TrackingMode.Custom);

            snapshot.TrackingEnabled.Should().BeFalse();
        }
    }
}
