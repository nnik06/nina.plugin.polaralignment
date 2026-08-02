using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaRaRotationWitnessEvidenceProducerTest {
        [Test]
        public void ProducesFourSolveAbsoluteWitnessWithoutAuthority() {
            var fixture = CreateFixture(returnAngleDegrees: 0.0);
            try {
                var result = TppaRaRotationWitnessEvidenceProducer.Produce(
                    fixture.Metadata, fixture.Points, fixture.OutputDirectory);

                result.Produced.Should().BeTrue(string.Join("; ",
                    result.ProductionIssues) + "; "
                    + string.Join("; ", result.QualificationIssues));
                result.GrantsMotionAuthority.Should().BeFalse();
                result.GrantsCompletionAuthority.Should().BeFalse();
                result.GrantsAbsoluteAccuracyClaim.Should().BeFalse();
                File.Exists(result.OutputPath).Should().BeTrue();
                var evidence = JsonConvert.DeserializeObject<TppaQualificationWitnessEvidence>(
                    result.EvidenceJson);
                evidence.SchemaVersion.Should().Be(
                    TppaAbsoluteEvidenceBinder.CurrentEvidenceSchemaVersion);
                evidence.EvidenceBasis.Should().Be(
                    TppaAbsoluteEvidenceBinder.AbsoluteTruePoleWitnessBasis);
                evidence.MeasurementMethod.Should().Be(
                    TppaAbsoluteEvidenceBinder.RaRotationCircleWitnessMethod);
                evidence.SourceSolves.Should().HaveCount(4);
                evidence.SourceVectorDigests.Should().OnlyHaveUniqueItems();
            } finally {
                if (Directory.Exists(fixture.OutputDirectory)) {
                    Directory.Delete(fixture.OutputDirectory, true);
                }
            }
        }

        [Test]
        public void ReturnedAOutsideClosureFailsBeforeWritingEvidence() {
            var fixture = CreateFixture(returnAngleDegrees: 2.0);
            try {
                var result = TppaRaRotationWitnessEvidenceProducer.Produce(
                    fixture.Metadata, fixture.Points, fixture.OutputDirectory);

                result.Produced.Should().BeFalse();
                result.ProductionIssues.Should().Contain(
                    issue => issue.Contains("qualified A return"));
                Directory.Exists(fixture.OutputDirectory).Should().BeFalse();
            } finally {
                if (Directory.Exists(fixture.OutputDirectory)) {
                    Directory.Delete(fixture.OutputDirectory, true);
                }
            }
        }

        [Test]
        public void OverBudgetButStructurallyValidUncertaintyStillProducesEvidence() {
            var fixture = CreateFixture(returnAngleDegrees: 0.0);
            var uncertaintyWithoutDigest = fixture.Metadata.Uncertainty with {
                EvidenceDigest = string.Empty,
                MeasurementStandardUncertaintyArcSeconds = 20
            };
            var uncertainty = uncertaintyWithoutDigest with {
                EvidenceDigest = TppaWitnessUncertainty.ComputeEvidenceDigest(
                    uncertaintyWithoutDigest)
            };
            var metadata = fixture.Metadata with {
                CalibrationDigest = uncertainty.EvidenceDigest,
                CalibrationSourceDigest = uncertainty.InputDigest,
                Uncertainty = uncertainty
            };
            try {
                var result = TppaRaRotationWitnessEvidenceProducer.Produce(
                    metadata, fixture.Points, fixture.OutputDirectory);

                result.Produced.Should().BeTrue();
                result.QualificationIssues.Should().Contain(
                    issue => issue.Contains("exceeds"));
                File.Exists(result.OutputPath).Should().BeTrue();
            } finally {
                if (Directory.Exists(fixture.OutputDirectory)) {
                    Directory.Delete(fixture.OutputDirectory, true);
                }
            }
        }

        [Test]
        public void GuideOutputEnabledDuringAnyCaptureFailsClosed() {
            var fixture = CreateFixture(returnAngleDegrees: 0.0);
            var points = fixture.Points.ToArray();
            points[1] = points[1] with { GuideOutputEnabled = true };
            try {
                var result = TppaRaRotationWitnessEvidenceProducer.Produce(
                    fixture.Metadata, points, fixture.OutputDirectory);

                result.Produced.Should().BeFalse();
                result.ProductionIssues.Should().Contain(
                    issue => issue.Contains("guided"));
            } finally {
                Cleanup(fixture.OutputDirectory);
            }
        }

        [Test]
        public void MountCoordinateSolveHintFailsClosed() {
            var fixture = CreateFixture(returnAngleDegrees: 0.0);
            var points = fixture.Points.ToArray();
            points[1] = points[1] with {
                SolverHintPolicy = "mount-coordinate-hint"
            };
            try {
                var result = TppaRaRotationWitnessEvidenceProducer.Produce(
                    fixture.Metadata, points, fixture.OutputDirectory);

                result.Produced.Should().BeFalse();
                result.ProductionIssues.Should().Contain(
                    issue => issue.Contains("provenance"));
            } finally {
                Cleanup(fixture.OutputDirectory);
            }
        }

        [Test]
        public void FitsTimestampOutsideDeclaredClockBoundFailsClosed() {
            var fixture = CreateFixture(returnAngleDegrees: 0.0);
            var points = fixture.Points.ToArray();
            points[2] = points[2] with {
                FitsDateObsUtc = points[2].ObservationUtc.AddSeconds(2)
            };
            try {
                var result = TppaRaRotationWitnessEvidenceProducer.Produce(
                    fixture.Metadata, points, fixture.OutputDirectory);

                result.Produced.Should().BeFalse();
                result.ProductionIssues.Should().Contain(
                    issue => issue.Contains("mistimed"));
            } finally {
                Cleanup(fixture.OutputDirectory);
            }
        }

        [Test]
        public void NonMonotonicCommandedRaArcFailsClosed() {
            var fixture = CreateFixture(returnAngleDegrees: 0.0);
            var points = fixture.Points.ToArray();
            points[2] = points[2] with {
                CommandedRightAscensionDegrees = 110
            };
            try {
                var result = TppaRaRotationWitnessEvidenceProducer.Produce(
                    fixture.Metadata, points, fixture.OutputDirectory);

                result.Produced.Should().BeFalse();
                result.ProductionIssues.Should().Contain(
                    issue => issue.Contains("monotonic"));
            } finally {
                Cleanup(fixture.OutputDirectory);
            }
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public void FailedTrajectoryOrStateRestoreFailsClosed(
                bool trajectoryQualified,
                bool guideOutputRestored) {
            var fixture = CreateFixture(returnAngleDegrees: 0.0);
            var metadata = fixture.Metadata with {
                TrajectoryPreflightQualified = trajectoryQualified,
                GuideOutputRestored = guideOutputRestored
            };
            try {
                var result = TppaRaRotationWitnessEvidenceProducer.Produce(
                    metadata, fixture.Points, fixture.OutputDirectory);

                result.Produced.Should().BeFalse();
                result.ProductionIssues.Should().NotBeEmpty();
            } finally {
                Cleanup(fixture.OutputDirectory);
            }
        }

        private static Fixture CreateFixture(double returnAngleDegrees) {
            var axis = Normalize(new(0.90, 0.10, 0.42));
            var tangent = Normalize(new(-axis.Y, axis.X, 0));
            var bitangent = Cross(axis, tangent);
            var started = new DateTime(2026, 8, 1, 20, 0, 0, DateTimeKind.Utc);
            var angles = new[] { 0.0, 25.0, 50.0, returnAngleDegrees };
            var positions = new[] { "A", "B", "C", "A" };
            var points = new List<TppaRaRotationWitnessPointReceipt>();
            for (var index = 0; index < angles.Length; index++) {
                var vector = OnCircle(axis, tangent, bitangent, angles[index]);
                var observation = started.AddSeconds(index * 15);
                points.Add(new(
                    TppaRaRotationWitnessPointReceipt.CurrentSchemaVersion,
                    Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                    positions[index],
                    index,
                    "mount-command-" + index,
                    observation.AddSeconds(-3),
                    observation.AddSeconds(-2),
                    100 + angles[index],
                    80,
                    true,
                    false,
                    "Stopped",
                    false,
                    observation.AddSeconds(-0.5),
                    1,
                    observation,
                    observation.AddSeconds(-0.5),
                    TppaAbsoluteEvidenceBinder.FitsDateObsExposureStart,
                    10,
                    Sha((char)('a' + index)),
                    Sha((char)('5' + index)),
                    "ASTAP-2026.1",
                    Sha('f'),
                    TppaAbsoluteEvidenceBinder.BlindNoMountHintSolvePolicy,
                    TppaFastQualificationConventions.IcrsObservationEpoch,
                    25.2,
                    55.3,
                    25,
                    2.0,
                    0,
                    40,
                    index == 3 ? 10 : 10 + index * 5,
                    20,
                    0,
                    vector.X,
                    vector.Y,
                    vector.Z,
                    "pierWest"));
            }

            var uncertaintyWithoutDigest = new TppaWitnessUncertaintyEvidence(
                "witness-model-v1",
                Sha('1'),
                string.Empty,
                3,
                2,
                1,
                1,
                1,
                1,
                1,
                1,
                1);
            var uncertainty = uncertaintyWithoutDigest with {
                EvidenceDigest = TppaWitnessUncertainty.ComputeEvidenceDigest(
                    uncertaintyWithoutDigest)
            };
            var metadata = new TppaRaRotationWitnessProductionMetadata(
                "qualification-run-1",
                "session-1",
                "independent-witness-producer",
                Sha('2'),
                "hardware-1",
                Sha('3'),
                "dubai-balcony",
                25.2,
                55.3,
                25,
                "utc-ntp",
                10,
                TppaFastQualificationConventions.IcrsObservationEpoch,
                TppaAbsoluteEvidenceBinder.TopocentricHorizonNorthWestUp,
                TppaFastQualificationConventions.TruePoleTarget,
                0,
                "independent-guide-camera-1",
                uncertainty.EvidenceDigest,
                true,
                "calibration-producer",
                uncertainty.InputDigest,
                false,
                Sha('4'),
                true,
                42,
                1,
                50,
                2.8,
                "Stopped",
                "Stopped",
                true,
                uncertainty);
            return new(metadata, points,
                Path.Combine(Path.GetTempPath(), "tppa-witness-" + Guid.NewGuid()));
        }

        private static TppaQualificationVector OnCircle(
                TppaQualificationVector axis,
                TppaQualificationVector tangent,
                TppaQualificationVector bitangent,
                double angleDegrees) {
            const double axial = 0.45;
            var radial = Math.Sqrt(1 - axial * axial);
            var angle = angleDegrees * Math.PI / 180.0;
            return Normalize(new(
                axial * axis.X + radial * (Math.Cos(angle) * tangent.X
                    + Math.Sin(angle) * bitangent.X),
                axial * axis.Y + radial * (Math.Cos(angle) * tangent.Y
                    + Math.Sin(angle) * bitangent.Y),
                axial * axis.Z + radial * (Math.Cos(angle) * tangent.Z
                    + Math.Sin(angle) * bitangent.Z)));
        }

        private static TppaQualificationVector Cross(
                TppaQualificationVector left,
                TppaQualificationVector right) => new(
            left.Y * right.Z - left.Z * right.Y,
            left.Z * right.X - left.X * right.Z,
            left.X * right.Y - left.Y * right.X);

        private static TppaQualificationVector Normalize(TppaQualificationVector value) {
            var norm = Math.Sqrt(value.X * value.X + value.Y * value.Y
                + value.Z * value.Z);
            return new(value.X / norm, value.Y / norm, value.Z / norm);
        }

        private static string Sha(char value) => new(value, 64);

        private static void Cleanup(string path) {
            if (Directory.Exists(path)) {
                Directory.Delete(path, true);
            }
        }

        private sealed record Fixture(
            TppaRaRotationWitnessProductionMetadata Metadata,
            IReadOnlyList<TppaRaRotationWitnessPointReceipt> Points,
            string OutputDirectory);
    }
}
