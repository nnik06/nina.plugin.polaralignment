using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaCoarseDeterminationReceiptBuilderTest {
        [Test]
        public void ObservationCaptureDigestMatchesIndependentPythonRfc8785Vector() {
            var start = new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc);
            TppaCoarseSourceSolveEvidence Solve(
                    string id,
                    char image,
                    char output,
                    int offsetSeconds,
                    double raDegrees) {
                var exposureStart = start.AddSeconds(offsetSeconds);
                return new(
                    Guid.Parse(id),
                    new string(image, 64),
                    new string(output, 64),
                    exposureStart,
                    1000,
                    exposureStart.AddMilliseconds(500),
                    exposureStart.AddMilliseconds(-100),
                    true,
                    false,
                    raDegrees,
                    20.0,
                    "pierEast");
            }
            var solves = new[] {
                Solve("60000000-0000-4000-8000-000000000001", 'b', 'c', 0, 10.0),
                Solve("60000000-0000-4000-8000-000000000002", 'd', 'e', 2, 11.0),
                Solve("60000000-0000-4000-8000-000000000003", 'f', '1', 4, 12.0)
            };

            var digest = TppaCoarseDeterminationReceiptBuilder
                .BuildObservationCaptureDigest(
                    Guid.Parse("50000000-0000-4000-8000-000000000001"),
                    new string('a', 64),
                    solves);

            digest.Should().Be(
                "085dddcae3e5b0485653d6e8ab4af7ec12e50d1d27a76868b82eb56dc2ca647f");
        }
    }
}