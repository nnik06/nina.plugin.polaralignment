using FluentAssertions;
using NINA.Core.Enum;
using NINA.Plugins.PolarAlignment.Instructions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaFastQualificationRuntimeAdapterTest {
        [Test]
        public void QualifiedRuntimeEvidenceDerivesExactSphericalAdmissionInput() {
            var evidence = QualifiedEvidence();

            var result = TppaFastQualificationRuntimeAdapter.Derive(evidence);

            result.DerivationIssues.Should().BeEmpty();
            result.IsDerivationComplete.Should().BeTrue();
            result.IsFastTruePoleQualified.Should().BeTrue();
            result.QualificationInput.FreshDeterminationCount.Should().Be(3);
            result.QualificationInput.FreshSolvesUncached.Should().BeTrue();
            result.QualificationInput.MaximumPairwiseDeltaArcMinutes
                .Should().BeApproximately(0.10, 1e-6);
            result.QualificationInput.FinalReportedErrorArcMinutes
                .Should().BeApproximately(0.55, 1e-6);
            result.QualificationInput.IndependentTruePoleErrorArcMinutes
                .Should().BeApproximately(0.20, 1e-6);
            result.QualificationInput.TppaToIndependentDeltaArcMinutes
                .Should().BeApproximately(0.35, 1e-6);
            result.MaximumDeterminationErrorArcMinutes.Should().BeLessThan(3.0);
            result.EvaluateOperationalQualification(safetyGatesPassed: true)
                .IsOperationallyQualified.Should().BeTrue();
            result.SourcePolarErrorVectorDigest.Should().MatchRegex("^[0-9a-f]{64}$");
            result.QualificationInput.TppaInputPathDigest.Should().MatchRegex("^[0-9a-f]{64}$");
        }

        [Test]
        public void ReusedSolveDigestFailsFreshnessAndQualification() {
            var evidence = QualifiedEvidence();
            var determinations = evidence.Determinations.ToArray();
            var firstDigest = determinations[0].Solves[0].ContentSha256;
            var finalSolves = determinations[^1].Solves.ToArray();
            finalSolves[^1] = finalSolves[^1] with { ContentSha256 = firstDigest };
            determinations[^1] = determinations[^1] with { Solves = finalSolves };

            var result = TppaFastQualificationRuntimeAdapter.Derive(
                evidence with { Determinations = determinations });

            result.IsDerivationComplete.Should().BeFalse();
            result.QualificationInput.FreshSolvesUncached.Should().BeFalse();
            result.IsFastTruePoleQualified.Should().BeFalse();
            result.DerivationIssues.Should().Contain(
                issue => issue.Contains("reused"));
        }

        [Test]
        public void MissingWitnessFailsClosedWithoutInventingAbsoluteAccuracy() {
            var evidence = QualifiedEvidence() with { IndependentWitness = null };

            var result = TppaFastQualificationRuntimeAdapter.Derive(evidence);
            var qualification =
                TppaFastQualification.Evaluate(result.QualificationInput);

            result.IsDerivationComplete.Should().BeFalse();
            result.IsFastTruePoleQualified.Should().BeFalse();
            result.QualificationInput.IndependentTruePoleErrorArcMinutes
                .Should().BeNull();
            result.QualificationInput.TppaToIndependentDeltaArcMinutes
                .Should().BeNull();
            qualification.Issues.Should().Contain(
                issue => issue.Contains("independent true-pole witness"));
        }

        [Test]
        public void DetachedWitnessUncertaintyDigestFailsClosed() {
            var evidence = QualifiedEvidence();
            var witness = evidence.IndependentWitness! with {
                Uncertainty = QualifiedWitnessUncertainty(Sha(99))
            };

            var result = TppaFastQualificationRuntimeAdapter.Derive(
                evidence with { IndependentWitness = witness });

            result.IsDerivationComplete.Should().BeFalse();
            result.IsFastTruePoleQualified.Should().BeFalse();
            result.DerivationIssues.Should().Contain(
                issue => issue.Contains("detached"));
        }

        [Test]
        public void StaleAtmosphereWrongFrameAndPhysicalMovementAllFailClosed() {
            var evidence = QualifiedEvidence();
            var staleAtmosphere = evidence.Atmosphere with {
                ObservationUtc = evidence.Determinations[0].StartedUtc.AddHours(-1)
            };
            var wrongFrame = evidence.Identity with {
                CoordinateFrame = "j2000-without-observation-epoch"
            };

            var result = TppaFastQualificationRuntimeAdapter.Derive(
                evidence with {
                    Atmosphere = staleAtmosphere,
                    Identity = wrongFrame,
                    PhysicalAdjustmentCommandCount = 1
                });
            var qualification =
                TppaFastQualification.Evaluate(result.QualificationInput);

            result.IsFastTruePoleQualified.Should().BeFalse();
            result.QualificationInput.AtmosphereFresh.Should().BeFalse();
            result.QualificationInput.CoordinateFrameQualified.Should().BeFalse();
            result.QualificationInput.NoPhysicalAdjustmentBetweenDeterminations
                .Should().BeFalse();
            qualification.Issues.Should().Contain(
                issue => issue.Contains("atmosphere provenance"));
            qualification.Issues.Should().Contain(
                issue => issue.Contains("coordinate frame"));
            qualification.Issues.Should().Contain(
                issue => issue.Contains("physical state changed"));
        }

        [Test]
        public void ExtremeFiniteVectorFailsClosedWithoutThrowing() {
            var evidence = QualifiedEvidence();
            var determinations = evidence.Determinations.ToArray();
            determinations[1] = determinations[1] with {
                MountAxisVector = new Vector3(1e200, 1e200, 1e200)
            };

            var action = () => TppaFastQualificationRuntimeAdapter.Derive(
                evidence with { Determinations = determinations });

            action.Should().NotThrow();
            var result = action();
            result.IsDerivationComplete.Should().BeFalse();
            result.IsFastTruePoleQualified.Should().BeFalse();
            result.DerivationIssues.Should().Contain(
                issue => issue.Contains("unit length"));
        }

        [Test]
        public void WrongMountAxisFrameFailsClosed() {
            var evidence = QualifiedEvidence();
            var identity = evidence.Identity with {
                MountAxisVectorFrame = "equatorial-cartesian"
            };

            var result = TppaFastQualificationRuntimeAdapter.Derive(
                evidence with { Identity = identity });

            result.IsFastTruePoleQualified.Should().BeFalse();
            result.QualificationInput.CoordinateFrameQualified.Should().BeFalse();
        }

        [Test]
        public void InvalidRaOrMixedPierSideFailsFreshness() {
            var evidence = QualifiedEvidence();
            var determinations = evidence.Determinations.ToArray();
            var solves = determinations[0].Solves.ToArray();
            solves[0] = solves[0] with { RightAscensionDegrees = 360 };
            solves[1] = solves[1] with { PierSide = PierSide.pierWest };
            determinations[0] = determinations[0] with { Solves = solves };

            var result = TppaFastQualificationRuntimeAdapter.Derive(
                evidence with { Determinations = determinations });

            result.IsFastTruePoleQualified.Should().BeFalse();
            result.QualificationInput.FreshDeterminationCount.Should().Be(2);
        }

        [Test]
        public void ReturnedAClosureUsesGreatCircleAndKnownPierSide() {
            var evidence = QualifiedEvidence();
            var determinations = evidence.Determinations.ToArray();
            var finalSolves = determinations[^1].Solves.ToArray();
            finalSolves[0] = finalSolves[0] with {
                RightAscensionDegrees = finalSolves[0].RightAscensionDegrees + 1
            };
            determinations[^1] = determinations[^1] with { Solves = finalSolves };

            var result = TppaFastQualificationRuntimeAdapter.Derive(
                evidence with { Determinations = determinations });

            result.IsDerivationComplete.Should().BeFalse();
            result.QualificationInput.ClosureQualified.Should().BeFalse();
            result.DerivationIssues.Should().Contain(
                issue => issue.Contains("returned-A closure failed"));
        }

        [Test]
        public void TruePoleVectorAndSeparationAreHemisphereSafe() {
            var north = TppaFastQualificationRuntimeAdapter.TruePoleVector(25);
            var south = TppaFastQualificationRuntimeAdapter.TruePoleVector(-25);

            north.X.Should().BePositive();
            south.X.Should().BeNegative();
            north.Z.Should().BeApproximately(south.Z, 1e-12);
            TppaFastQualificationRuntimeAdapter.AngularSeparationArcMinutes(
                    south,
                    AxisAtAltitude(-25, 0.5))
                .Should().BeApproximately(0.5, 1e-6);
        }

        [Test]
        public void NullRuntimeEvidenceReturnsSafeFiniteFailureInput() {
            var action = () => TppaFastQualificationRuntimeAdapter.Derive(null);

            action.Should().NotThrow();
            var result = action();
            result.IsDerivationComplete.Should().BeFalse();
            result.IsFastTruePoleQualified.Should().BeFalse();
            result.QualificationInput.DurationSeconds.Should().Be(double.MaxValue);
            result.SourcePolarErrorVectorDigest.Should().MatchRegex("^[0-9a-f]{64}$");
        }

        private static TppaFastQualificationRuntimeEvidence QualifiedEvidence() {
            var start = new DateTime(2026, 7, 31, 20, 0, 0, DateTimeKind.Utc);
            var determinations = new[] {
                Determination(start, 0, 0.45),
                Determination(start.AddSeconds(80), 3, 0.50),
                Determination(start.AddSeconds(160), 6, 0.55)
            };
            var identity = new TppaRuntimeIdentityEvidence(
                "hardware-epoch-7",
                "mechanical-state-11",
                "mele-monotonic-boot-4",
                25,
                "main-camera-train",
                "astap-2026.07",
                TppaFastQualificationConventions.IcrsObservationEpoch,
                TppaFastQualificationRuntimeAdapter.TopocentricHorizonNorthWestUp);
            var site = new TppaRuntimeSiteEvidence(
                25,
                55,
                18,
                "surveyed-balcony-site-v1");
            var atmosphere = new TppaRuntimeAtmosphereEvidence(
                TppaFastQualificationConventions.QualifiedLocalWeatherStation,
                start.AddSeconds(120),
                1002,
                39,
                63);
            var witness = new TppaRuntimeWitnessEvidence(
                start.AddSeconds(230),
                identity.HardwareConfigurationId,
                identity.MechanicalStateId,
                "independent-pole-camera",
                Sha(40),
                identity.CoordinateFrame,
                identity.MountAxisVectorFrame,
                RefractionAlignmentTarget.TruePoleTarget,
                Sha(41),
                start.AddDays(-1),
                start.AddDays(1),
                QualifiedWitnessUncertainty(Sha(40)),
                AxisAtAltitude(25, 0.20));
            return new TppaFastQualificationRuntimeEvidence(
                determinations,
                identity,
                site,
                atmosphere,
                witness,
                RefractionAdjustmentEnabled: true,
                PhysicalAdjustmentCommandCount: 0);
        }

        private static TppaRuntimeDeterminationEvidence Determination(
                DateTime start,
                int digestOffset,
                double axisErrorArcMinutes) {
            var solves = new List<TppaRuntimeSolveEvidence> {
                new(
                    start.AddSeconds(10),
                    Sha(digestOffset + 1),
                    10,
                    60,
                    PierSide.pierEast),
                new(
                    start.AddSeconds(30),
                    Sha(digestOffset + 2),
                    25,
                    60,
                    PierSide.pierEast),
                new(
                    start.AddSeconds(60),
                    Sha(digestOffset + 3),
                    40,
                    60,
                    PierSide.pierEast)
            };
            return new TppaRuntimeDeterminationEvidence(
                start,
                start.AddSeconds(70),
                solves,
                AxisAtAltitude(25, axisErrorArcMinutes),
                new TppaThreePointGeometry(
                    MinimumPairwiseSeparationDegrees: 15,
                    MaximumPairwiseSeparationDegrees: 30,
                    DoubledChordTriangleArea: 0.01,
                    NormalizedTriangleQuality: 0.25));
        }

        private static Vector3 AxisAtAltitude(
                double latitudeDegrees,
                double offsetArcMinutes) {
            var altitude = Math.Abs(latitudeDegrees) + offsetArcMinutes / 60.0;
            var altitudeRadians = altitude * Math.PI / 180.0;
            var hemisphere = latitudeDegrees >= 0 ? 1.0 : -1.0;
            return new Vector3(
                hemisphere * Math.Cos(altitudeRadians),
                0,
                Math.Sin(altitudeRadians));
        }

        private static TppaWitnessUncertaintyEvidence QualifiedWitnessUncertainty(
                string inputDigest) {
            var evidence = new TppaWitnessUncertaintyEvidence(
                ModelId: "qualified-independent-pole-camera-v1",
                InputDigest: inputDigest,
                EvidenceDigest: new string('0', 64),
                CalibrationSampleCount: 6,
                ClosureSampleCount: 3,
                MeasurementStandardUncertaintyArcSeconds: 1,
                CalibrationResidualArcSeconds: 2,
                OrientationModelResidualArcSeconds: 1,
                ClosureResidualArcSeconds: 1,
                FrameSystematicBoundArcSeconds: 1,
                DistortionSystematicBoundArcSeconds: 1,
                MechanicalSystematicBoundArcSeconds: 1);
            return evidence with {
                EvidenceDigest = TppaWitnessUncertainty.ComputeEvidenceDigest(evidence)
            };
        }

        private static string Sha(int value) =>
            value.ToString("x").PadLeft(64, '0');
    }
}
