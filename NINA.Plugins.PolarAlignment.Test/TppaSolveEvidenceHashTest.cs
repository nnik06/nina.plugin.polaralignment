using FluentAssertions;
using NINA.Astrometry;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using NINA.PlateSolving;
using System.Reflection;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaSolveEvidenceHashTest {
        [Test]
        public void PixelDigestChangesWithPixelsAndAcquisitionProperties() {
            var first = Image(new ushort[] { 1, 2, 3, 4 }, gain: 100);
            var repeat = Image(new ushort[] { 1, 2, 3, 4 }, gain: 100);
            var changedPixel = Image(new ushort[] { 1, 2, 3, 5 }, gain: 100);
            var changedGain = Image(new ushort[] { 1, 2, 3, 4 }, gain: 101);

            TppaSolveEvidenceHash.ImageSha256(first).Should().Be(TppaSolveEvidenceHash.ImageSha256(repeat));
            TppaSolveEvidenceHash.ImageSha256(first).Should().NotBe(TppaSolveEvidenceHash.ImageSha256(changedPixel));
            TppaSolveEvidenceHash.ImageSha256(first).Should().NotBe(TppaSolveEvidenceHash.ImageSha256(changedGain));
        }

        [Test]
        public void SolverDigestIsCanonicalAndObservationBound() {
            var time = new DateTime(2026, 8, 3, 20, 0, 0, DateTimeKind.Utc);
            var solve = new PlateSolveResult {
                Coordinates = new Coordinates(Angle.ByDegree(15), Angle.ByDegree(40), Epoch.J2000),
                PositionAngle = 12.5, Pixscale = 1.2, Radius = 0.8, Flipped = false, Success = true
            };

            var first = TppaSolveEvidenceHash.SolverOutputSha256(solve, time);
            var repeat = TppaSolveEvidenceHash.SolverOutputSha256(solve, time);
            var later = TppaSolveEvidenceHash.SolverOutputSha256(solve, time.AddMilliseconds(1));

            first.Should().Be(repeat).And.HaveLength(64);
            later.Should().NotBe(first);
        }

        private static IImageData Image(ushort[] pixels, int gain) {
            var data = DispatchProxy.Create<IImageData, ImageDataProxy>();
            ((ImageDataProxy)(object)data).Pixels = new ImageArray(pixels);
            ((ImageDataProxy)(object)data).PropertiesValue = new ImageProperties(2, 2, 16, false, gain, 10);
            return data;
        }

        private class ImageDataProxy : DispatchProxy {
            public IImageArray Pixels { get; set; } = null!;
            public ImageProperties PropertiesValue { get; set; } = null!;

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
                targetMethod?.Name switch {
                    "get_Data" => Pixels,
                    "get_Properties" => PropertiesValue,
                    _ => targetMethod?.ReturnType.IsValueType == true
                        ? Activator.CreateInstance(targetMethod.ReturnType)
                        : null
                };
        }
    }
}
