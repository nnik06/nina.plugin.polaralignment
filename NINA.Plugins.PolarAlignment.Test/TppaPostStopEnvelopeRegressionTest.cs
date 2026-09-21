using System.Reflection;
using FluentAssertions;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.Interfaces;
using NINA.PlateSolving.Interfaces;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaPostStopEnvelopeRegressionTest {
        [TestCase(295.7544583333, 320.566375)]
        [TestCase(320.566375, 343.6678333333)]
        public void RecordedRateMoveEndpointsFailExistingPreciseWaypointTolerance(double start, double end) {
            var result = TppaDriftMoveVerificationPolicy.Evaluate(
                start, end, 44.31758, 44.31758, 22,
                NINA.Core.Enum.PierSide.pierEast, NINA.Core.Enum.PierSide.pierEast,
                maximumUndertravelDegrees: 0.25, maximumOvershootDegrees: 0.25,
                maximumDeclinationTravelDegrees: 0.05);
            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("exceeds");
            TestContext.Out.WriteLine($"RECORDED_ENDPOINT travel={result.RightAscensionTravelDegrees:F9}; {result.Reason}");
        }

        [TestCase(295.7544583333, true)]
        [TestCase(350, true)]
        [TestCase(10, false)]
        [TestCase(295.7544583333, false)]
        public void AbsolutePlanRetainsDirectionDeclinationAndEpoch(double start, bool east) {
            var origin = new Coordinates(Angle.ByDegree(start), Angle.ByDegree(44.31758), Epoch.J2000);
            var plan = TppaVerificationWaypointPlan.Create(origin, 22, east);
            for (var i = 0; i < 3; i++) {
                var expected = ((start + (east ? 22 : -22) * i) % 360 + 360) % 360;
                plan.Forward[i].RADegrees.Should().BeApproximately(expected, 1e-9);
                plan.Forward[i].Dec.Should().BeApproximately(origin.Dec, 1e-9);
                plan.Forward[i].Epoch.Should().Be(origin.Epoch);
            }
        }

        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(false, true)]
        public async Task ActualMoveRejectsUnsafePostStopTelemetry(bool stillSlewing, bool disconnected) {
            var mount = DispatchProxy.Create<ITelescopeMediator, MountProxy>();
            var fake = (MountProxy)(object)mount;
            fake.UnsafeAfterStop = true;
            fake.StillSlewing = stillSlewing;
            fake.DisconnectedAfterStop = disconnected;
            var item = Create(mount);
            var act = async () => await Move(item);
            var failure = await act.Should().ThrowAsync<Exception>();
            failure.Which.Message.Should().Contain(disconnected ? "connected mount telemetry" : "altitude");
            fake.StopCalls.Should().BeGreaterThanOrEqualTo(2, "normal and finally stops both remain");
            fake.PostStopPositionReads.Should().BeGreaterThan(0);
            fake.TrackingCalls.Should().Be(0, "unsafe endpoint cannot complete and re-enable tracking");
        }

        [Test]
        public async Task ActualMoveChecksSafeSettledEndpointBeforeCompletion() {
            var mount = DispatchProxy.Create<ITelescopeMediator, MountProxy>();
            var fake = (MountProxy)(object)mount;
            await Move(Create(mount));
            fake.PostStopPositionReads.Should().BeGreaterThanOrEqualTo(2);
            fake.TrackingCalls.Should().BeGreaterThan(0);
        }

        [Test]
        public async Task ActualMoveRejectsViolationAppearingDuringSettling() {
            var mount = DispatchProxy.Create<ITelescopeMediator, MountProxy>();
            var fake = (MountProxy)(object)mount;
            fake.UnsafeAfterStop = true;
            fake.UnsafeAfterReads = 3;
            var act = async () => await Move(Create(mount), settleSeconds: 1);
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*altitude*");
            fake.PostStopPositionReads.Should().BeGreaterThanOrEqualTo(3);
            fake.TrackingCalls.Should().Be(0);
        }

        [Test]
        public async Task ActualMoveBoundsAStopThatNeverBecomesIdle() {
            var mount = DispatchProxy.Create<ITelescopeMediator, MountProxy>();
            var fake = (MountProxy)(object)mount;
            fake.StillSlewing = true;
            var act = async () => await Move(Create(mount), distance: 0.2);
            await act.Should().ThrowAsync<TimeoutException>().WithMessage("*bounded stop wait*");
            fake.StopCalls.Should().BeGreaterThanOrEqualTo(2);
            fake.TrackingCalls.Should().Be(0);
        }

        [Test]
        public void BuiltAssemblyContainsMeasurementOnlyHoldMethod() {
            typeof(Instructions.PolarAlignment).GetMethod("RunMeasurementOnlyContinuousUpdates",
                BindingFlags.Instance | BindingFlags.NonPublic).Should().NotBeNull();
        }

        private static Instructions.PolarAlignment Create(ITelescopeMediator mount) {
            var item = PolarAlignmentSolveCancellationTest.CreatePolarAlignment(
                DispatchProxy.Create<IImagingMediator, MountProxy>(),
                DispatchProxy.Create<IPlateSolverFactory, MountProxy>());
            typeof(Instructions.PolarAlignment).GetField("telescopeMediator", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(item, mount);
            item.MountMotionEnvelopeEnabled = true;
            item.MountMotionMinimumAltitudeDegrees = 15;
            item.MountMotionMaximumAltitudeDegrees = 55;
            item.MountMotionAzimuthStartDegrees = 270;
            item.MountMotionAzimuthEndDegrees = 10;
            return item;
        }

        private static Task Move(Instructions.PolarAlignment item, double distance = 22, double settleSeconds = 0) => (Task)typeof(Instructions.PolarAlignment)
            .GetMethod("MoveToNextPoint", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(item, new object?[] { distance, 2d, true, new Progress<ApplicationStatus>(), CancellationToken.None, settleSeconds })!;

        public class MountProxy : DispatchProxy {
            public bool UnsafeAfterStop, StillSlewing, DisconnectedAfterStop;
            public int StopCalls, PostStopPositionReads, TrackingCalls;
            public int UnsafeAfterReads;
            private bool driving;
            protected override object? Invoke(MethodInfo? method, object?[]? args) {
                var stopped = StopCalls > 0;
                if (method!.Name == "MoveAxis") {
                    if (Convert.ToDouble(args![1]) == 0) StopCalls++; else driving = true;
                    return method.ReturnType == typeof(bool) ? true : null;
                }
                if (method.Name == "GetCurrentPosition") {
                    if (stopped) PostStopPositionReads++;
                    // Recorded RA increments, not a simulated claim of a new rig result.
                    return new Coordinates(Angle.ByDegree(100 + (stopped ? 24.8119166667 : driving ? 22.370875 : 0)),
                        Angle.ByDegree(44.31758), Epoch.J2000);
                }
                if (method.Name == "GetInfo") {
                    var info = Activator.CreateInstance(method.ReturnType)!;
                    void Set(string name, object value) => method.ReturnType.GetProperty(name)!.SetValue(info, value);
                    Set("Connected", !(stopped && DisconnectedAfterStop));
                    Set("Slewing", stopped && StillSlewing);
                    Set("Azimuth", 312.878109793);
                    Set("Altitude", stopped && UnsafeAfterStop && PostStopPositionReads >= UnsafeAfterReads ? 55.528988638 : 53.09);
                    var rates = method.ReturnType.GetProperty("PrimaryAxisRates")!;
                    var element = rates.PropertyType.GetGenericArguments()[0];
                    var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
                    list.Add(Activator.CreateInstance(element, 1d, 3d));
                    rates.SetValue(info, list);
                    return info;
                }
                if (method.Name.StartsWith("SetTracking", StringComparison.Ordinal)) TrackingCalls++;
                return method.ReturnType == typeof(void) ? null : method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
            }
        }
    }
}
