using FluentAssertions;
using Newtonsoft.Json.Linq;
using NINA.Core.Enum;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaQualificationRunEvidenceProducerTest {
        private string outputDirectory;

        [SetUp]
        public void SetUp() {
            outputDirectory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "tppa-run-evidence-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown() {
            if (Directory.Exists(outputDirectory)) {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }

        [Test]
        public void ProducesBinderValidImmutableEvidenceButDoesNotInventAbsoluteQualification() {
            var fixture = CreateFixture();

            var result = TppaQualificationRunEvidenceProducer.Produce(
                fixture.Metadata,
                fixture.Determinations,
                outputDirectory);

            result.Produced.Should().BeTrue();
            File.Exists(result.OutputPath).Should().BeTrue();
            var token = JObject.Parse(result.EvidenceJson);
            token.Value<int>("schemaVersion").Should().Be(
                TppaAbsoluteEvidenceBinder.CurrentEvidenceSchemaVersion);
            token.Value<string>("evidenceDigest").Should().Be(
                TppaAbsoluteEvidenceBinder.ComputeCanonicalEvidenceDigest(
                    result.EvidenceJson));
            token.Value<bool>("atmosphereQualified").Should().BeFalse();
            token.Value<bool>("siteTimeProvenanceQualified").Should().BeFalse();
            token["determinations"]!.Count().Should().Be(3);
            token.SelectToken("pluginAssembly.sha256")!.Value<string>()
                .Should().Be(fixture.Metadata.PluginAssembly.Sha256);
            token.SelectToken("pluginAssembly.moduleVersionId")!.Value<string>()
                .Should().Be(fixture.Metadata.PluginAssembly.ModuleVersionId);
            token.SelectToken("qualificationCoreAssembly.sha256")!.Value<string>()
                .Should().Be(fixture.Metadata.QualificationCoreAssembly.Sha256);
            token["determinations"]!.All(value =>
                value["sourceVectorDigests"]!.Count() == 3).Should().BeTrue();

            var run = token.ToObject<TppaQualificationRunEvidence>()!;
            var witness = CreateWitness(run);
            var binding = TppaAbsoluteEvidenceBinder.Bind(
                Encoding.UTF8.GetBytes(result.EvidenceJson),
                Encoding.UTF8.GetBytes(
                    TppaQualificationRunEvidenceProducer.SerializeEvidence(witness)),
                TppaQualificationRunEvidenceProducer.Sha256Utf8("policy"),
                fixture.Determinations[^1].CompletedUtc.AddSeconds(20));

            binding.EvidenceValid.Should().BeTrue(
                string.Join(" | ", binding.Issues));
            binding.IsQualified.Should().BeFalse();
            binding.ReceiptJson.Should().NotBeNull();
            binding.Issues.Should().Contain(issue =>
                issue.Contains("atmosphere", StringComparison.OrdinalIgnoreCase));
        }

        [Test]
        public void RefusesIncompleteDeterminationBeforeCreatingAFile() {
            var fixture = CreateFixture();
            var incomplete = fixture.Determinations.ToArray();
            incomplete[1] = incomplete[1] with {
                SourcePoints = incomplete[1].SourcePoints.Take(2).ToArray()
            };

            var result = TppaQualificationRunEvidenceProducer.Produce(
                fixture.Metadata,
                incomplete,
                outputDirectory);

            result.Produced.Should().BeFalse();
            result.ProductionIssues.Should().Contain(issue =>
                issue.Contains("incomplete", StringComparison.OrdinalIgnoreCase));
            Directory.Exists(outputDirectory).Should().BeFalse();
        }

        [Test]
        public void CreateNewPersistenceNeverOverwritesAnExistingRunReceipt() {
            var fixture = CreateFixture();
            var first = TppaQualificationRunEvidenceProducer.Produce(
                fixture.Metadata,
                fixture.Determinations,
                outputDirectory);
            var original = File.ReadAllBytes(first.OutputPath);

            var second = TppaQualificationRunEvidenceProducer.Produce(
                fixture.Metadata,
                fixture.Determinations,
                outputDirectory);

            first.Produced.Should().BeTrue();
            second.Produced.Should().BeFalse();
            second.ProductionIssues.Should().Contain(issue =>
                issue.Contains("IOException", StringComparison.Ordinal));
            File.ReadAllBytes(first.OutputPath).Should().Equal(original);
        }

        [Test]
        public void CapturesActuallyLoadedPluginAndCoreAssemblyIdentity() {
            var plugin = TppaLoadedAssemblyEvidenceFactory.Capture(
                typeof(TppaQualificationRunEvidenceProducer).Assembly);
            var core = TppaLoadedAssemblyEvidenceFactory.Capture(
                typeof(TppaAbsoluteEvidenceBinder).Assembly);

            plugin.AssemblyName.Should().Be(
                TppaAbsoluteEvidenceBinder.PluginAssemblyName);
            core.AssemblyName.Should().Be(
                TppaAbsoluteEvidenceBinder.QualificationCoreAssemblyName);
            Path.IsPathRooted(plugin.Location).Should().BeTrue();
            Path.IsPathRooted(core.Location).Should().BeTrue();
            File.Exists(plugin.Location).Should().BeTrue();
            File.Exists(core.Location).Should().BeTrue();
            plugin.Sha256.Should().Be(
                TppaQualificationRunEvidenceProducer.Sha256File(plugin.Location));
            core.Sha256.Should().Be(
                TppaQualificationRunEvidenceProducer.Sha256File(core.Location));
            Guid.Parse(plugin.ModuleVersionId).Should().NotBe(Guid.Empty);
            Guid.Parse(core.ModuleVersionId).Should().NotBe(Guid.Empty);
        }

        [Test]
        public void RefusesPipelineDigestThatDoesNotMatchLoadedPlugin() {
            var fixture = CreateFixture();
            var metadata = fixture.Metadata with {
                PipelineDigest = TppaQualificationRunEvidenceProducer.Sha256Utf8(
                    "different-plugin")
            };

            var result = TppaQualificationRunEvidenceProducer.Produce(
                metadata,
                fixture.Determinations,
                outputDirectory);

            result.Produced.Should().BeFalse();
            result.ProductionIssues.Should().Contain(issue =>
                issue.Contains("loaded plugin", StringComparison.OrdinalIgnoreCase));
        }

        private static ProducerFixture CreateFixture() {
            var runId = "11111111-2222-3333-4444-555555555555";
            var start = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
            var mechanical = TppaQualificationRunEvidenceProducer.Sha256Utf8(
                "mechanical-state");
            var identity = new TppaRuntimeIdentityEvidence(
                HardwareConfigurationId: TppaQualificationRunEvidenceProducer.Sha256Utf8("hardware"),
                MechanicalStateId: mechanical,
                ClockDomainId: "windows-utc:test-process",
                ClockUncertaintyMilliseconds: double.MaxValue,
                TppaInstrumentId: TppaQualificationRunEvidenceProducer.Sha256Utf8("main-camera"),
                SolverIdentity: "test-solver-v1",
                CoordinateFrame: TppaFastQualificationConventions.IcrsObservationEpoch,
                MountAxisVectorFrame:
                    TppaFastQualificationRuntimeAdapter.TopocentricHorizonNorthWestUp);
            var site = new TppaRuntimeSiteEvidence(25.2, 55.3, 20, "dubai-test-site");
            var atmosphere = new TppaRuntimeAtmosphereEvidence(
                "standard-atmosphere-fallback",
                start,
                1013.25,
                15,
                0);
            var pluginAssembly = TppaLoadedAssemblyEvidenceFactory.Capture(
                typeof(TppaQualificationRunEvidenceProducer).Assembly);
            var qualificationCoreAssembly = TppaLoadedAssemblyEvidenceFactory.Capture(
                typeof(TppaAbsoluteEvidenceBinder).Assembly);
            var metadata = new TppaQualificationRunProductionMetadata(
                runId,
                "session-test",
                "tppa-runtime-test",
                pluginAssembly.Sha256,
                CorrectionSequenceNumber: 0,
                identity,
                site,
                atmosphere,
                RefractionAdjustmentEnabled: true,
                pluginAssembly,
                qualificationCoreAssembly);
            var determinations = new[] {
                Determination(runId + "-initial", start, 0, 0.20),
                Determination(runId + "-reciprocal", start.AddSeconds(40), 20, 0.22),
                Determination(runId + "-repeat", start.AddSeconds(80), 0, 0.21)
            };
            return new(metadata, determinations);
        }

        private static TppaQualificationDeterminationInput Determination(
                string id,
                DateTime startedUtc,
                double firstRaDegrees,
                double axisErrorArcMinutes) {
            var vectors = new[] {
                UnitVector(firstRaDegrees, 30),
                UnitVector(firstRaDegrees + 5, 31),
                UnitVector(firstRaDegrees + 10, 30)
            };
            var points = vectors.Select((vector, index) =>
                new TppaVerificationPointReceipt(
                    TppaVerificationPointReceipt.CurrentSchemaVersion,
                    Guid.Parse("11111111-2222-3333-4444-555555555555"),
                    SequenceIndex: index + 1,
                    Direction: "East",
                    PointIndex: index + 1,
                    DirectionSampleCount: 3,
                    ObservationUtc: startedUtc.AddSeconds(5 + index * 5),
                    MountAzimuthDegrees: 0,
                    MountAltitudeDegrees: 40,
                    SolvedRightAscensionDegrees: firstRaDegrees + index * 5,
                    SolvedDeclinationDegrees: 30 + (index == 1 ? 1 : 0),
                    PositionAngleDegrees: 0,
                    VectorX: vector.X,
                    VectorY: vector.Y,
                    VectorZ: vector.Z,
                    SideOfPier: PierSide.pierEast)).ToArray();
            var axis = Vector3.DeterminePlaneVector(vectors[0], vectors[1], vectors[2]);
            if (axis.X < 0) {
                axis = new Vector3(-axis.X, -axis.Y, -axis.Z);
            }
            return new(
                id,
                startedUtc,
                points[^1].ObservationUtc,
                points,
                axis,
                TppaThreePointGeometry.Evaluate(vectors[0], vectors[1], vectors[2]));
        }

        private static TppaQualificationWitnessEvidence CreateWitness(
                TppaQualificationRunEvidence run) {
            var uncertainty = new TppaWitnessUncertaintyEvidence(
                ModelId: "independent-witness-test-v1",
                InputDigest: TppaQualificationRunEvidenceProducer.Sha256Utf8("witness-input"),
                EvidenceDigest: new string('0', 64),
                CalibrationSampleCount: 6,
                ClosureSampleCount: 3,
                MeasurementStandardUncertaintyArcSeconds: 1,
                CalibrationResidualArcSeconds: 1,
                OrientationModelResidualArcSeconds: 1,
                ClosureResidualArcSeconds: 1,
                FrameSystematicBoundArcSeconds: 1,
                DistortionSystematicBoundArcSeconds: 1,
                MechanicalSystematicBoundArcSeconds: 1);
            uncertainty = uncertainty with {
                EvidenceDigest = TppaWitnessUncertainty.ComputeEvidenceDigest(uncertainty)
            };
            var observationUtc = run.Determinations[^1].CompletedUtc.AddSeconds(5);
            var witnessAxis = run.Determinations[^1].MountAxisVector;
            var witnessSolves = new[] {
                WitnessSolve(witnessAxis, observationUtc.AddSeconds(-3), 0, "a"),
                WitnessSolve(witnessAxis, observationUtc.AddSeconds(-2), 1, "b"),
                WitnessSolve(witnessAxis, observationUtc.AddSeconds(-1), 2, "c"),
                WitnessSolve(witnessAxis, observationUtc, 0, "return-a")
            };
            var positions = new[] { "A", "B", "C", "A" };
            var commandedRa = new[] { 100.0, 125.0, 150.0, 100.0 };
            var acquisitions = witnessSolves
                .Select((solve, index) => new TppaRaRotationWitnessAcquisitionEvidence(
                    TppaRaRotationWitnessPointReceipt.CurrentSchemaVersion,
                    positions[index],
                    index,
                    "mount-command-" + index,
                    solve.ObservationUtc.AddSeconds(-3),
                    solve.ObservationUtc.AddSeconds(-2),
                    commandedRa[index],
                    80,
                    true,
                    false,
                    "Stopped",
                    false,
                    solve.ObservationUtc.AddSeconds(-0.5),
                    1,
                    solve.ObservationUtc,
                    solve.ObservationUtc.AddSeconds(-0.5),
                    TppaAbsoluteEvidenceBinder.FitsDateObsExposureStart,
                    10,
                    solve.ContentSha256,
                    TppaQualificationRunEvidenceProducer.Sha256Utf8(
                        "solver-output-" + index),
                    "ASTAP-2026.1",
                    TppaQualificationRunEvidenceProducer.Sha256Utf8(
                        "astap-binary"),
                    TppaAbsoluteEvidenceBinder.BlindNoMountHintSolvePolicy,
                    TppaFastQualificationConventions.IcrsObservationEpoch,
                    run.SiteLatitudeDegrees,
                    run.SiteLongitudeDegrees,
                    run.SiteElevationMeters,
                    2.0,
                    0,
                    45,
                    solve.RightAscensionDegrees,
                    solve.DeclinationDegrees,
                    0,
                    solve.PierSide))
                .ToArray();
            return new(
                TppaAbsoluteEvidenceBinder.CurrentEvidenceSchemaVersion,
                EvidenceDigest: string.Empty,
                run.RunId,
                run.SessionId,
                ProducerId: "independent-witness-test",
                TppaAbsoluteEvidenceBinder.WitnessProducerKind,
                PipelineDigest: TppaQualificationRunEvidenceProducer.Sha256Utf8("witness-pipeline"),
                run.HardwareConfigurationId,
                run.MechanicalStateDigest,
                run.SiteIdentity,
                run.SiteLatitudeDegrees,
                run.SiteLongitudeDegrees,
                run.SiteElevationMeters,
                run.ClockDomainId,
                50,
                run.CoordinateFrame,
                run.MountAxisVectorFrame,
                run.PoleTarget,
                TppaAbsoluteEvidenceBinder.AbsoluteTruePoleWitnessBasis,
                TppaAbsoluteEvidenceBinder.RaRotationCircleWitnessMethod,
                observationUtc,
                CorrectionSequenceNumber: 0,
                InstrumentId: "independent-ipolar-test",
                SourceVectorDigests: witnessSolves
                    .Select(value => value.ContentSha256)
                    .ToArray(),
                SourceSolves: witnessSolves,
                TrajectoryPreflightDigest:
                    TppaQualificationRunEvidenceProducer.Sha256Utf8(
                        "trajectory-preflight"),
                TrajectoryPreflightQualified: true,
                TrajectoryMinimumAltitudeDegrees: 42,
                TrajectoryMaximumSampleStepDegrees: 1,
                TrajectoryTotalArcDegrees: 50,
                TrajectoryDesignConditionProxy: 2.8,
                InitialPhd2AppState: "Stopped",
                FinalPhd2AppState: "Stopped",
                GuideOutputRestored: true,
                Acquisitions: acquisitions,
                CalibrationDigest: uncertainty.EvidenceDigest,
                CalibrationCurrent: true,
                CalibrationSourceProducerId: "independent-calibration-test",
                CalibrationSourceDigest: uncertainty.InputDigest,
                CalibrationDerivedFromTppa: false,
                Uncertainty: uncertainty,
                MountAxisVector: run.Determinations[^1].MountAxisVector);
        }

        private static Vector3 AxisVector(double errorArcMinutes) {
            var radians = errorArcMinutes / 60.0 * Math.PI / 180.0;
            return new Vector3(Math.Sin(radians), 0, Math.Cos(radians));
        }

        private static TppaQualificationSolveEvidence WitnessSolve(
                TppaQualificationVector axis,
                DateTime observationUtc,
                int pointIndex,
                string sourceId) {
            var angle = pointIndex * 20.0 * Math.PI / 180.0;
            const double axialComponent = 0.25;
            var radialComponent = Math.Sqrt(1 - axialComponent * axialComponent);
            var seed = Math.Abs(axis.Z) < 0.9
                ? new TppaQualificationVector(0, 0, 1)
                : new TppaQualificationVector(0, 1, 0);
            var basisU = Normalize(Cross(seed, axis));
            var basisV = Cross(axis, basisU);
            var vector = new TppaQualificationVector(
                axialComponent * axis.X
                    + radialComponent * (Math.Cos(angle) * basisU.X
                        + Math.Sin(angle) * basisV.X),
                axialComponent * axis.Y
                    + radialComponent * (Math.Cos(angle) * basisU.Y
                        + Math.Sin(angle) * basisV.Y),
                axialComponent * axis.Z
                    + radialComponent * (Math.Cos(angle) * basisU.Z
                        + Math.Sin(angle) * basisV.Z));
            var rightAscension = Math.Atan2(vector.Y, vector.X) * 180.0 / Math.PI;
            if (rightAscension < 0) { rightAscension += 360; }
            return new(
                observationUtc,
                TppaQualificationRunEvidenceProducer.Sha256Utf8(
                    "witness-source-" + sourceId),
                rightAscension,
                Math.Asin(vector.Z) * 180.0 / Math.PI,
                "pierEast",
                vector);
        }

        private static TppaQualificationVector Cross(
                TppaQualificationVector left,
                TppaQualificationVector right) =>
            new(
                left.Y * right.Z - left.Z * right.Y,
                left.Z * right.X - left.X * right.Z,
                left.X * right.Y - left.Y * right.X);

        private static TppaQualificationVector Normalize(
                TppaQualificationVector value) {
            var length = Math.Sqrt(
                value.X * value.X + value.Y * value.Y + value.Z * value.Z);
            return new(value.X / length, value.Y / length, value.Z / length);
        }

        private static Vector3 UnitVector(double longitudeDegrees, double latitudeDegrees) {
            var longitude = longitudeDegrees * Math.PI / 180.0;
            var latitude = latitudeDegrees * Math.PI / 180.0;
            return new Vector3(
                Math.Cos(latitude) * Math.Cos(longitude),
                Math.Cos(latitude) * Math.Sin(longitude),
                Math.Sin(latitude));
        }

        private sealed record ProducerFixture(
            TppaQualificationRunProductionMetadata Metadata,
            IReadOnlyList<TppaQualificationDeterminationInput> Determinations);
    }
}
