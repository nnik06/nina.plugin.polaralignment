using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using NINA.Image.Interfaces;
using NINA.PlateSolving;

namespace NINA.Plugins.PolarAlignment {
    internal static class TppaSolveEvidenceHash {
        private static readonly byte[] ImageDomain = Encoding.ASCII.GetBytes("TPPA-IMAGE-V1\0");

        internal static string ImageSha256(IImageData image) {
            if (image?.Data == null || image.Properties == null) {
                throw new ArgumentException("Captured image data and properties are required.", nameof(image));
            }
            var pixels = image.Data.FlatArray;
            var integerPixels = image.Data.FlatArrayInt;
            if ((pixels == null || pixels.Length == 0) && (integerPixels == null || integerPixels.Length == 0)) {
                throw new ArgumentException("Captured image has no canonical pixel array.", nameof(image));
            }
            var expected = checked(image.Properties.Width * image.Properties.Height);
            var count = pixels?.Length ?? integerPixels!.Length;
            if (image.Properties.Width <= 0 || image.Properties.Height <= 0 || count != expected) {
                throw new ArgumentException("Captured image dimensions do not match its pixel array.", nameof(image));
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(ImageDomain);
            Span<byte> header = stackalloc byte[25];
            BinaryPrimitives.WriteInt32LittleEndian(header[0..4], image.Properties.Width);
            BinaryPrimitives.WriteInt32LittleEndian(header[4..8], image.Properties.Height);
            BinaryPrimitives.WriteInt32LittleEndian(header[8..12], image.Properties.BitDepth);
            header[12] = image.Properties.IsBayered ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteInt32LittleEndian(header[13..17], image.Properties.Gain);
            BinaryPrimitives.WriteInt32LittleEndian(header[17..21], image.Properties.Offset);
            BinaryPrimitives.WriteInt32LittleEndian(header[21..25], pixels != null ? 16 : 32);
            hash.AppendData(header);
            Span<byte> value = stackalloc byte[4];
            if (pixels != null) {
                foreach (var pixel in pixels) {
                    BinaryPrimitives.WriteUInt16LittleEndian(value[0..2], pixel);
                    hash.AppendData(value[0..2]);
                }
            } else {
                foreach (var pixel in integerPixels!) {
                    BinaryPrimitives.WriteInt32LittleEndian(value, pixel);
                    hash.AppendData(value);
                }
            }
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }

        internal static string SolverOutputSha256(PlateSolveResult result, DateTime observationMidpointUtc) {
            if (result?.Success != true || result.Coordinates == null) {
                throw new ArgumentException("A successful plate solve is required.", nameof(result));
            }
            if (observationMidpointUtc.Kind != DateTimeKind.Utc) {
                throw new ArgumentException("Observation midpoint must be UTC.", nameof(observationMidpointUtc));
            }
            var payload = new JObject {
                ["schemaVersion"] = 1,
                ["observationMidpointUtc"] = observationMidpointUtc.ToString("O"),
                ["rightAscensionMicrodegrees"] = Scale(result.Coordinates.RADegrees),
                ["declinationMicrodegrees"] = Scale(result.Coordinates.Dec),
                ["positionAngleMicrodegrees"] = Scale(result.PositionAngle),
                ["pixelScaleMicroarcseconds"] = Scale(result.Pixscale),
                ["radiusMicrodegrees"] = Scale(result.Radius),
                ["flipped"] = result.Flipped
            };
            return HttpsUpasSupervisorCoarseTppaExecutor.ComputeRequestBodySha256(payload);
        }

        private static long Scale(double value) {
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            return checked((long)Math.Round(value * 1_000_000.0, MidpointRounding.AwayFromZero));
        }
    }
}
